using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Controllers;

public sealed class AuthController(
    AssetIqDbContext db,
    PasswordHasher passwords,
    TokenService tokens,
    AppConfig config,
    AppClock clock) : ApiControllerBase
{
    /// <summary>
    /// A wrong username and a wrong password give the same answer, so the endpoint cannot be used to
    /// find out which accounts exist.
    /// </summary>
    private static ApiException BadCredentials() => new(StatusCodes.Status401Unauthorized, "Incorrect username or password");

    [HttpPost("auth/login")]
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

        var now = clock.UtcNow;
        var expiresAt = now.AddHours(config.Auth.TokenTtlHours);
        var token = tokens.Sign(new TokenPayload(user.Id, user.Username, user.Role, new DateTimeOffset(expiresAt).ToUnixTimeSeconds()));

        await db.Users.Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAt, (DateTime?)now).SetProperty(u => u.UpdatedAt, now), Aborted);

        return new LoginResultDto(token, expiresAt, ToDto(user));
    }

    /// <summary>Who the bearer token belongs to (used to restore a session).</summary>
    [HttpGet("auth/me")]
    [RequireRole(Roles.Viewer)]
    [FailureMessage("Could not load the current user")]
    public async Task<UserDto> Me()
    {
        var id = User.GetAuthUser()!.Id;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, Aborted)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Sign in to continue");
        return ToDto(user);
    }

    private static UserDto ToDto(UserAccount user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Name = user.Name,
        Role = user.Role,
        LastLoginAt = user.LastLoginAt,
    };
}
