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
public sealed record AuthUser(int Id, string Username, string Name, string Role);

public static class AuthDefaults
{
    public const string Scheme = "AssetIQ";
    internal const string DisplayNameClaim = "assetiq:name";
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
            principal.FindFirstValue(ClaimTypes.Role) ?? "");
    }
}

/// <summary>
/// Reads the bearer token and signs the caller in. It never rejects a request — public endpoints keep
/// working signed out — so each endpoint states its own requirement with <see cref="RequireRoleAttribute"/>.
/// The role is re-read from the database on every request rather than trusted from the token, so
/// demoting or disabling an account takes effect immediately instead of when the token expires.
/// </summary>
public sealed class TokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    TokenService tokens,
    AssetIqDbContext db)
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
                .Select(u => new { u.Id, u.Username, u.Name, u.Role, u.Active })
                .FirstOrDefaultAsync(Context.RequestAborted);
            if (user is { Active: true })
            {
                var identity = new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
                        new Claim(ClaimTypes.Name, user.Username),
                        new Claim(AuthDefaults.DisplayNameClaim, user.Name),
                        new Claim(ClaimTypes.Role, user.Role),
                    ],
                    Scheme.Name);
                return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Could not load the user behind a token");
        }
        return AuthenticateResult.NoResult();
    }
}

/// <summary>Satisfied by a signed-in user holding <see cref="Role"/> or a higher role.</summary>
public sealed class MinimumRoleRequirement(string role) : IAuthorizationRequirement
{
    public string Role { get; } = role;
}

public sealed class MinimumRoleHandler : AuthorizationHandler<MinimumRoleRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MinimumRoleRequirement requirement)
    {
        var user = context.User.GetAuthUser();
        if (user is not null && Roles.Satisfies(user.Role, requirement.Role)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary><c>[RequireRole(Roles.Operator)]</c>: the endpoint needs a signed-in user with at least that role.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireRoleAttribute(string role) : AuthorizeAttribute(PolicyFor(role))
{
    public static string PolicyFor(string role) => $"role:{role}";
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
        var required = policy.Requirements.OfType<MinimumRoleRequirement>().Select(r => r.Role).FirstOrDefault() ?? "required";
        await ApiErrorMiddleware.WriteErrorAsync(context, StatusCodes.Status403Forbidden,
            $"This action needs the {required} role; you are signed in as {user.Role}.");
    }
}
