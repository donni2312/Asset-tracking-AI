using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Controllers;

public sealed class AuthController(
    AssetIqDbContext db,
    PasswordHasher passwords,
    TokenService tokens,
    AppConfig config,
    AppClock clock,
    AccessLog accessLog) : ApiControllerBase
{
    /// <summary>
    /// A wrong username and a wrong password give the same answer, so the endpoint cannot be used to
    /// find out which accounts exist.
    /// </summary>
    private static ApiException BadCredentials() => new(StatusCodes.Status401Unauthorized, "Incorrect username or password");

    [HttpPost("auth/login")]
    [AllowAnonymous]
    [FailureMessage("Could not sign in")]
    public async Task<LoginResultDto> Login([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var rawUsername = input.RequiredString("username", minLength: 1);
        var password = input.RequiredString("password", minLength: 1);
        if (!input.IsValid) throw BadCredentials();
        var username = rawUsername.Trim().ToLowerInvariant();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username, Aborted);
        // Verify even when there is no such user, so the reply takes the same time either way.
        var ok = passwords.Verify(password, user?.PasswordHash ?? PasswordHasher.DummyHash);
        if (user is null || !ok || !user.Active) throw BadCredentials();
        // Phase 2: an account still holding a retired role (viewer / operator / admin) waits for Group IT.
        if (!Roles.All.Contains(user.Role))
        {
            await accessLog.RecordAsync(user.Id, user.Username, user.Role, "login_refused", "no current role", Aborted);
            throw new ApiException(StatusCodes.Status403Forbidden, "Your account has no current role. Ask Group IT to assign one.");
        }

        var now = clock.UtcNow;
        // AU-12: time-limited access (auditors) is refused after its expiry date; the attempt is logged.
        if (user.AccessExpiresAt is { } expiry && expiry <= now)
        {
            await accessLog.RecordAsync(user.Id, user.Username, user.Role, "login_refused", $"access expired {expiry:yyyy-MM-dd}", Aborted);
            throw new ApiException(StatusCodes.Status403Forbidden, $"Your access expired on {expiry:dd MMM yyyy}. Ask Group Finance to extend it.");
        }
        var expiresAt = now.AddHours(config.Auth.TokenTtlHours);
        var token = tokens.Sign(new TokenPayload(user.Id, user.Username, user.Role, new DateTimeOffset(expiresAt).ToUnixTimeSeconds()));

        await db.Users.Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAt, (DateTime?)now).SetProperty(u => u.UpdatedAt, now), Aborted);
        await accessLog.RecordAsync(user.Id, user.Username, user.Role, "login", null, Aborted);

        return new LoginResultDto(token, expiresAt, await ToDtoAsync(user));
    }

    /// <summary>Who the bearer token belongs to (used to restore a session).</summary>
    [HttpGet("auth/me")]
    [RequireRole(Roles.Everyone)]
    [FailureMessage("Could not load the current user")]
    public async Task<UserDto> Me()
    {
        var id = User.GetAuthUser()!.Id;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, Aborted)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Sign in to continue");
        return await ToDtoAsync(user);
    }

    private async Task<UserDto> ToDtoAsync(UserAccount user)
    {
        var sbu = user.SbuId is { } id ? await db.Sbus.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, Aborted) : null;
        var employee = user.EmployeeId is { } eid ? await db.Employees.AsNoTracking().Include(e => e.Department).FirstOrDefaultAsync(e => e.Id == eid, Aborted) : null;
        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Name = user.Name,
            Role = user.Role,
            RoleName = Roles.DisplayName(user.Role),
            SbuId = user.SbuId,
            SbuCode = sbu?.Code,
            Scope = ScopeOf(user.Role, sbu?.Name, employee?.Department?.Name),
            EmployeeId = employee?.Id,
            EmployeeName = employee?.Name,
            DepartmentId = employee?.DepartmentId,
            DepartmentName = employee?.Department?.Name,
            AccessExpiresAt = user.AccessExpiresAt,
            LastLoginAt = user.LastLoginAt,
        };
    }

    /// <summary>The data scope shown under the role on the sidebar (Permissions matrix, "View assets").</summary>
    private static string ScopeOf(string role, string? sbuName, string? departmentName) => role switch
    {
        Roles.GroupFinance => "All SBUs",
        Roles.GroupIt => "All SBUs (IT assets)",
        Roles.SbuIt => $"{sbuName ?? "No SBU set"} (IT assets)",
        Roles.SbuFinance => $"{sbuName ?? "No SBU set"} (all assets)",
        Roles.GateOfficer => $"{sbuName ?? "No SBU set"} (on scan only)",
        Roles.Hod => $"{sbuName ?? "No SBU set"} · {departmentName ?? "no department set"}",
        Roles.SbuHr => $"{sbuName ?? "No SBU set"} (employee toolboxes)",
        Roles.SbuAdmin => $"{sbuName ?? "No SBU set"} (non-IT assets)",
        Roles.Stores => $"{sbuName ?? "No SBU set"} (items in store)",
        Roles.Employee => "My items",
        Roles.Auditor => "All SBUs (read only)",
        Roles.SystemAdmin => "Everything (every request is logged)",
        _ => "No access",
    };
}
