using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AssetIQ.Api.Auth;

/// <summary>The signed-in caller, as stored in the database at the time of the request.</summary>
/// <param name="SbuId">The SBU an SBU-scoped role acts on (null for Group roles).</param>
/// <param name="EmployeeId">The employee the account belongs to (HOD, Employee; optional for the others).</param>
/// <param name="DepartmentId">That employee's department: the HOD's scope.</param>
public sealed record AuthUser(int Id, string Username, string Name, string Role, int? SbuId = null, int? EmployeeId = null, int? DepartmentId = null);

public static class AuthDefaults
{
    public const string Scheme = "AssetIQ";
    internal const string DisplayNameClaim = "assetiq:name";
    internal const string SbuClaim = "assetiq:sbu";
    internal const string EmployeeClaim = "assetiq:employee";
    internal const string DepartmentClaim = "assetiq:department";
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in user, or null when the request carried no valid token.</summary>
    public static AuthUser? GetAuthUser(this ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true) return null;
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return null;
        return new AuthUser(
            id,
            principal.FindFirstValue(ClaimTypes.Name) ?? "",
            principal.FindFirstValue(AuthDefaults.DisplayNameClaim) ?? "",
            principal.FindFirstValue(ClaimTypes.Role) ?? "",
            OptionalId(principal, AuthDefaults.SbuClaim),
            OptionalId(principal, AuthDefaults.EmployeeClaim),
            OptionalId(principal, AuthDefaults.DepartmentClaim));
    }

    private static int? OptionalId(ClaimsPrincipal principal, string claim) =>
        int.TryParse(principal.FindFirstValue(claim), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
}

/// <summary>
/// Reads the bearer token and signs the caller in. It never rejects a request — public endpoints keep
/// working signed out — so each endpoint states its own requirement with <see cref="RequireRoleAttribute"/>.
/// The role is re-read from the database on every request rather than trusted from the token, so
/// demoting or disabling an account takes effect immediately instead of when the token expires. The same
/// goes for an access expiry date (AU-12): past it, the token no longer signs anyone in. A role that is not
/// one of <see cref="Roles.All"/> (the retired viewer / operator / admin) signs nobody in either.
/// On success the request's <see cref="AccessScope"/> is narrowed to what the caller may see.
/// </summary>
public sealed class TokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    TokenService tokens,
    AppClock clock,
    AssetIqDbContext db,
    AccessScope scope)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.FirstOrDefault();
        if (header is null || !header.StartsWith("bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var token = header[7..].Trim();
        if (token.Length == 0) return AuthenticateResult.NoResult();

        var payload = tokens.Verify(token);
        if (payload is null) return AuthenticateResult.NoResult();

        try
        {
            var user = await db.Users.AsNoTracking()
                .Where(u => u.Id == payload.Sub)
                .Select(u => new
                {
                    u.Id, u.Username, u.Name, u.Role, u.Active, u.SbuId, u.AccessExpiresAt, u.EmployeeId,
                    DepartmentId = db.Employees.Where(x => x.Id == u.EmployeeId).Select(x => x.DepartmentId).FirstOrDefault(),
                })
                .FirstOrDefaultAsync(Context.RequestAborted);
            if (user is { Active: true } && !(user.AccessExpiresAt <= clock.UtcNow) && Roles.All.Contains(user.Role))
            {
                List<Claim> claims =
                [
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(AuthDefaults.DisplayNameClaim, user.Name),
                    new Claim(ClaimTypes.Role, user.Role),
                ];
                if (user.SbuId is { } sbuId) claims.Add(new Claim(AuthDefaults.SbuClaim, sbuId.ToString(CultureInfo.InvariantCulture)));
                if (user.EmployeeId is { } employeeId) claims.Add(new Claim(AuthDefaults.EmployeeClaim, employeeId.ToString(CultureInfo.InvariantCulture)));
                if (user.DepartmentId is { } departmentId) claims.Add(new Claim(AuthDefaults.DepartmentClaim, departmentId.ToString(CultureInfo.InvariantCulture)));
                var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
                scope.Narrow(principal.GetAuthUser()!);
                return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Could not load the user behind a token");
        }
        return AuthenticateResult.NoResult();
    }
}

/// <summary>Satisfied by a signed-in user whose role is in <see cref="Allowed"/> (or any role for <see cref="Roles.Everyone"/>).</summary>
public sealed class RoleRequirement(string group) : IAuthorizationRequirement
{
    public IReadOnlySet<string> Allowed { get; } = Roles.Parse(group);
    public bool AnyRole => Allowed.Contains(Roles.Everyone);
}

/// <summary>
/// Checks <see cref="RoleRequirement"/>. Auditors (AU-12) are read-only whatever the endpoint lists: they
/// pass only on GET requests (reading, exports).
/// </summary>
public sealed class RoleHandler(IHttpContextAccessor http) : AuthorizationHandler<RoleRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RoleRequirement requirement)
    {
        var user = context.User.GetAuthUser();
        if (user is null) return Task.CompletedTask;
        if (!requirement.AnyRole && !requirement.Allowed.Contains(user.Role)) return Task.CompletedTask;
        if (user.Role == Roles.Auditor)
        {
            var method = (context.Resource as HttpContext ?? http.HttpContext)?.Request.Method;
            if (!HttpMethods.IsGet(method ?? "")) return Task.CompletedTask;
        }
        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>
/// <c>[RequireRole(Roles.GatePassRaisers)]</c>: the endpoint needs a signed-in user holding one of the roles of
/// the comma-separated group (<see cref="Roles.Everyone"/>: any role). Record-level rules (own SBU, IT or not)
/// are checked by <see cref="AccessPolicy"/> on top.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireRoleAttribute(string roles) : AuthorizeAttribute(PolicyPrefix + roles)
{
    public const string PolicyPrefix = "roles:";
}

/// <summary>Builds the <see cref="RoleRequirement"/> policy for any <c>roles:</c> policy name.</summary>
public sealed class RolePolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
        policyName.StartsWith(RequireRoleAttribute.PolicyPrefix, StringComparison.Ordinal)
            ? new AuthorizationPolicyBuilder().AddRequirements(new RoleRequirement(policyName[RequireRoleAttribute.PolicyPrefix.Length..])).Build()
            : await base.GetPolicyAsync(policyName);
}

/// <summary>
/// 401 when nobody is signed in, 403 when they are but lack the role — different to the caller:
/// one means "sign in", the other "ask someone else".
/// </summary>
public sealed class JsonAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await next(context);
            return;
        }
        var user = context.User.GetAuthUser();
        if (user is null)
        {
            await ApiErrorMiddleware.WriteErrorAsync(context, StatusCodes.Status401Unauthorized, "Sign in to continue");
            return;
        }
        var message = user.Role == Roles.Auditor && !HttpMethods.IsGet(context.Request.Method)
            ? "Auditors have read-only access (AU-12)."
            : $"Your role ({Roles.DisplayName(user.Role)}) may not do this.";
        await ApiErrorMiddleware.WriteErrorAsync(context, StatusCodes.Status403Forbidden, message);
    }
}
