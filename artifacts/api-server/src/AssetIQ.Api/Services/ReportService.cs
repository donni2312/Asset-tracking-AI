using System.Globalization;
using System.Text.RegularExpressions;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

/// <summary>A finished CSV download.</summary>
public sealed record CsvReport(string FileName, byte[] Content);

/// <summary>
/// [from, to] calendar days (YYYY-MM-DD, organisation time zone, both inclusive); either end may be open.
/// </summary>
public sealed partial record DayRange(string? From, string? To)
{
    public static DayRange Parse(string? from, string? to)
    {
        var range = new DayRange(Normalise(from, "from"), Normalise(to, "to"));
        if (range.From is not null && range.To is not null && string.CompareOrdinal(range.From, range.To) > 0)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "from must not be after to");
        }
        return range;
    }

    /// <summary>UTC instants: start of the first day, and start of the day after the last day.</summary>
    public (DateTime? Start, DateTime? End) ToInstants(TimeZoneInfo zone) => (
        From is null ? null : ZonedTime.StartOfDayKey(From, zone),
        To is null ? null : ZonedTime.StartOfDayKey(To, zone) + ZonedTime.Day);

    private static string? Normalise(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (!IsoDate().IsMatch(text) || !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, $"{name} must be a date (YYYY-MM-DD)");
        }
        return text;
    }

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}$")]
    private static partial Regex IsoDate();
}

/// <summary>
/// The CSV report downloads (Reports page and the Export buttons). Each report holds the same rows as the
/// matching screen; times are in the organisation time zone, named in the column header.
/// </summary>
public sealed class ReportService(AssetIqDbContext db, AppClock clock, AppConfig config, GatePassService gatePasses)
{
    private static readonly Dictionary<string, string> TriggerLabels = new()
    {
        [MovementTriggers.Rfid] = "RFID scan",
        [MovementTriggers.Manual] = "Manual",
        [MovementTriggers.GatePass] = "Gate pass exit",
    };

    private static readonly Dictionary<string, string> LifecycleLabels = new()
    {
        [LifecycleEventTypes.Deployment] = "Deployment",
        [LifecycleEventTypes.ReturnToIt] = "Returned to IT",
        [LifecycleEventTypes.RepairSent] = "Sent for repair",
        [LifecycleEventTypes.RepairReturned] = "Returned from repair",
        [LifecycleEventTypes.SbuTransfer] = "SBU transfer",
        [LifecycleEventTypes.Note] = "Note",
    };

    private string TimeHeader(string label) => $"{label} ({config.Timezone})";

    private string Local(DateTime? instant)
    {
        if (instant is null) return "";
        var p = ZonedTime.Parts(instant.Value, config.TimeZone);
        return string.Create(CultureInfo.InvariantCulture, $"{p.Year:D4}-{p.Month:D2}-{p.Day:D2} {p.Hour:D2}:{p.Minute:D2}");
    }

    private CsvReport Finish(string report, CsvBuilder csv) =>
        new($"assetiq-{report}-{ZonedTime.DateKey(clock.UtcNow, config.TimeZone)}.csv", csv.ToBytes());

    private static string Title(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..].Replace('_', ' ');

    /// <summary>The asset register, with the Assets page's filters.</summary>
    public async Task<CsvReport> AssetsAsync(string? status, int? locationId, int? sbuId, string? category, string? search, CancellationToken ct)
    {
        var query = db.Assets.AsNoTracking();
        if (status is not null) query = query.Where(a => a.Status == status);
        if (locationId is not null) query = query.Where(a => a.CurrentLocationId == locationId);
        if (sbuId is not null) query = query.Where(a => a.SbuId == sbuId);
        if (category is not null) query = query.Where(a => a.Category == category);
        if (search is not null)
        {
            var pattern = $"%{search}%";
            query = query.Where(a => EF.Functions.ILike(a.Name, pattern) || EF.Functions.ILike(a.SerialNumber!, pattern) || EF.Functions.ILike(a.RfidTag!, pattern));
        }
        var rows = await query.OrderBy(a => a.Name).Select(AssetQueries.ToDto).ToListAsync(ct);

        var csv = new CsvBuilder().Row(
            "Asset ID", "Name", "Category", "Serial number", "RFID tag", "Status", "Assigned to", "Current location",
            "SBU code", "SBU name", "Purchase date", "Purchase value (USD)", "Notes", TimeHeader("Registered"), TimeHeader("Last updated"));
        foreach (var a in rows)
        {
            csv.Row(a.Id, a.Name, a.Category, a.SerialNumber, a.RfidTag, Title(a.Status), a.AssignedTo,
                a.CurrentLocationName ?? "Off-premises / unknown", a.SbuCode, a.SbuName, a.PurchaseDate, a.PurchaseValue, a.Notes,
                Local(a.CreatedAt), Local(a.UpdatedAt));
        }
        return Finish("assets", csv);
    }

    /// <summary>The movement log for a period, oldest first.</summary>
    public async Task<CsvReport> MovementsAsync(DayRange range, int? assetId, int? sbuId, CancellationToken ct)
    {
        var (start, end) = range.ToInstants(config.TimeZone);
        var query = db.Movements.AsNoTracking();
        if (start is not null) query = query.Where(m => m.Timestamp >= start);
        if (end is not null) query = query.Where(m => m.Timestamp < end);
        if (assetId is not null) query = query.Where(m => m.AssetId == assetId);
        if (sbuId is not null) query = query.Where(m => m.Asset.SbuId == sbuId);
        var rows = await query
            .OrderBy(m => m.Timestamp).ThenBy(m => m.Id)
            .Select(m => new
            {
                m.Id, m.Timestamp, m.AssetId, AssetName = m.Asset.Name, m.Asset.RfidTag, SbuCode = m.Asset.Sbu!.Code,
                From = m.FromLocation!.Name, To = m.ToLocation!.Name, m.TriggeredBy, Reader = m.RfidReader!.Name,
                m.GatePassId, m.MovedBy, m.Notes,
            })
            .ToListAsync(ct);

        var csv = new CsvBuilder().Row(
            "Movement ID", TimeHeader("Date/time"), "Asset ID", "Asset", "RFID tag", "SBU", "From", "To", "Triggered by",
            "RFID reader", "Gate pass #", "Moved by", "Notes");
        foreach (var m in rows)
        {
            csv.Row(m.Id, Local(m.Timestamp), m.AssetId, m.AssetName, m.RfidTag, m.SbuCode, m.From,
                m.To ?? (m.TriggeredBy == MovementTriggers.GatePass ? "Off-premises" : null),
                TriggerLabels.GetValueOrDefault(m.TriggeredBy, m.TriggeredBy), m.Reader, m.GatePassId, m.MovedBy, m.Notes);
        }
        return Finish("movements", csv);
    }

    /// <summary>Gate passes requested in a period (newest first), optionally one status.</summary>
    public async Task<CsvReport> GatePassesAsync(string? status, int? sbuId, DayRange range, CancellationToken ct)
    {
        var (start, end) = range.ToInstants(config.TimeZone);
        var query = db.GatePasses.AsNoTracking();
        if (status is not null) query = query.Where(p => p.Status == status);
        if (sbuId is not null) query = query.Where(p => p.Asset.SbuId == sbuId);
        if (start is not null) query = query.Where(p => p.CreatedAt >= start);
        if (end is not null) query = query.Where(p => p.CreatedAt < end);
        var rows = await gatePasses.Project(query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)).ToListAsync(ct);
        var sbuCodes = await db.Sbus.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code, ct);

        var csv = new CsvBuilder().Row(
            "Gate pass #", "Status", "Source", "Asset ID", "Asset", "RFID tag", "SBU", "Requested by", "Reason", "Destination",
            "Return date", "Gate", "Decided by", TimeHeader("Decided at"), "Decision note", TimeHeader("Left premises at"),
            TimeHeader("Requested at"));
        foreach (var p in rows)
        {
            csv.Row(p.Id, Title(p.Status), p.Source == GatePassSources.RfidGate ? "Raised at a gate" : "Requested in advance",
                p.AssetId, p.AssetName, p.RfidTag, p.SbuId is { } s ? sbuCodes.GetValueOrDefault(s) : null, p.RequestedBy, p.Reason,
                p.Destination, p.ReturnDate, p.GateLocationName, p.ApprovedBy, Local(p.DecidedAt), p.DecisionNote,
                Local(p.ExitedAt), Local(p.CreatedAt));
        }
        return Finish("gate-passes", csv);
    }

    /// <summary>AI alerts, newest first; open ones only unless <paramref name="includeDismissed"/>.</summary>
    public async Task<CsvReport> InsightsAsync(bool includeDismissed, CancellationToken ct)
    {
        var query = db.AiInsights.AsNoTracking();
        if (!includeDismissed) query = query.Where(i => !i.Dismissed);
        var rows = await query.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id).Select(InsightService.ToDto).ToListAsync(ct);

        var csv = new CsvBuilder().Row(
            "Insight ID", TimeHeader("Raised at"), "Type", "Severity", "Asset ID", "Asset", "SBU", "Gate pass #", "Message", "Dismissed");
        foreach (var i in rows)
        {
            csv.Row(i.Id, Local(i.CreatedAt), Title(i.Type), Title(i.Severity), i.AssetId, i.AssetName, i.SbuCode, i.GatePassId,
                i.Message, i.Dismissed);
        }
        return Finish("insights", csv);
    }

    /// <summary>Lifecycle events (deployments, repairs, transfers, notes) in a period, oldest first.</summary>
    public async Task<CsvReport> LifecycleAsync(string? eventType, int? sbuId, DayRange range, CancellationToken ct)
    {
        var (start, end) = range.ToInstants(config.TimeZone);
        var query = db.AssetLifecycleEvents.AsNoTracking();
        if (eventType is not null) query = query.Where(e => e.EventType == eventType);
        if (sbuId is not null) query = query.Where(e => e.Asset.SbuId == sbuId);
        if (start is not null) query = query.Where(e => e.Timestamp >= start);
        if (end is not null) query = query.Where(e => e.Timestamp < end);
        var rows = await query
            .OrderBy(e => e.Timestamp).ThenBy(e => e.Id)
            .Select(e => new
            {
                e.Id, e.Timestamp, e.AssetId, AssetName = e.Asset.Name, e.Asset.RfidTag, e.EventType, e.PerformedBy,
                FromSbu = e.FromSbu!.Code, ToSbu = e.ToSbu!.Code, e.AssignedTo, e.Vendor, e.TicketRef,
                e.ConditionBefore, e.ConditionAfter, e.Notes,
            })
            .ToListAsync(ct);

        var csv = new CsvBuilder().Row(
            "Event ID", TimeHeader("Date/time"), "Asset ID", "Asset", "RFID tag", "Event", "Performed by", "From SBU", "To SBU",
            "Assigned to", "Vendor", "Ticket ref", "Condition before", "Condition after", "Notes");
        foreach (var e in rows)
        {
            csv.Row(e.Id, Local(e.Timestamp), e.AssetId, e.AssetName, e.RfidTag, LifecycleLabels.GetValueOrDefault(e.EventType, e.EventType),
                e.PerformedBy, e.FromSbu, e.ToSbu, e.AssignedTo, e.Vendor, e.TicketRef, e.ConditionBefore, e.ConditionAfter, e.Notes);
        }
        return Finish("lifecycle", csv);
    }
}
