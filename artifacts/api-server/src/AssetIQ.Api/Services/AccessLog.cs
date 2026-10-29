using AssetIQ.Api.Auth;
using AssetIQ.Api.Data;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>
    /// One row per request the System Administrator makes ("GET /api/assets?sbuId=2 → 200"): they may do
    /// everything, so everything they do is on record. Written with plain SQL so it never saves anything else the
    /// request left in the change tracker.
    /// </summary>
    public async Task RequestAsync(AuthUser user, HttpContext context, bool failed)
    {
        var detail = $"{context.Request.Method} {context.Request.Path}{context.Request.QueryString} → {(failed ? "failed" : context.Response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture))}";
        // The column is `timestamp without time zone` holding UTC (see AssetIqDbContext).
        var at = clock.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture);
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO access_log (user_id, username, role, action, detail, at) VALUES ({user.Id}, {user.Username}, {user.Role}, {"request"}, {detail}, {at}::timestamp)");
    }
}
