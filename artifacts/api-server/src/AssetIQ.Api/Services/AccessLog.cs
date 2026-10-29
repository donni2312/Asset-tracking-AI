using AssetIQ.Api.Auth;
using AssetIQ.Api.Data;
using AssetIQ.Api.Infrastructure;

namespace AssetIQ.Api.Services;

/// <summary>
/// Records sign-ins and exports (AU-12: every login and export an auditor makes is logged). Everyone's are
/// recorded, so Group Finance can also see who took data out of the system. Append-only in the database.
/// </summary>
public sealed class AccessLog(AssetIqDbContext db, AppClock clock)
{
    public async Task RecordAsync(int? userId, string username, string role, string action, string? detail, CancellationToken ct)
    {
        db.AccessLog.Add(new AccessLogEntry { UserId = userId, Username = username, Role = role, Action = action, Detail = detail, At = clock.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    public Task ExportAsync(AuthUser user, string what, HttpRequest request, CancellationToken ct) =>
        RecordAsync(user.Id, user.Username, user.Role, "export", $"{what}{request.QueryString}", ct);
}
