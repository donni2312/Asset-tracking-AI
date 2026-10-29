using System.Globalization;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Data;
using AssetIQ.Api.Infrastructure;

namespace AssetIQ.Api.Services;

/// <summary>
/// Writes the append-only audit trail (AU-06, GF-07). Entries are added to the caller's DbContext, so they
/// are saved in the same transaction as the change they describe. Nothing in the API updates or deletes
/// an entry, and the database rejects both.
/// </summary>
public sealed class AuditTrail(AssetIqDbContext db, AppClock clock)
{
    public void Record(AuthUser? user, string entity, int? entityId, int? assetId, string action,
        string? field = null, object? oldValue = null, object? newValue = null, string? reason = null)
    {
        db.AuditLog.Add(new AuditEntry
        {
            Entity = entity,
            EntityId = entityId,
            AssetId = assetId,
            Action = action,
            Field = field,
            OldValue = Text(oldValue),
            NewValue = Text(newValue),
            Reason = reason,
            UserId = user?.Id,
            UserName = user is null ? "system" : (user.Name.Length > 0 ? user.Name : user.Username),
            At = clock.UtcNow,
        });
    }

    /// <summary>One "updated" entry per field whose value changed; returns whether anything changed.</summary>
    public bool RecordChange(AuthUser? user, string entity, int? entityId, int? assetId, string field, object? oldValue, object? newValue, string? reason = null)
    {
        var before = Text(oldValue);
        var after = Text(newValue);
        if (before == after) return false;
        Record(user, entity, entityId, assetId, "updated", field, before, after, reason);
        return true;
    }

    public static string? Text(object? value) => value switch
    {
        null => null,
        decimal d => d.ToString("0.00####", CultureInfo.InvariantCulture),
        DateTime t => t.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
