using AssetIQ.Api.Configuration;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;

namespace AssetIQ.Api.Services;

/// <param name="Rule">Which rule produced the draft (for logs and tests; not stored).</param>
/// <param name="Type">Insight type, e.g. unauthorized_movement.</param>
/// <param name="Severity">info | warning | critical</param>
/// <param name="Message">Text shown on the dashboard.</param>
/// <param name="AssetId">The asset the insight is about, if any.</param>
/// <param name="GatePassId">The gate pass the insight is about, so deciding the pass can resolve it.</param>
public sealed record InsightDraft(string Rule, string Type, string Severity, string Message, int? AssetId, int? GatePassId = null);

public sealed record ScanAsset(int Id, string Name, string RfidTag, int? SbuId, string? SbuCode);

public sealed record ScanLocation(int Id, string Name, string Type, int? SbuId, string? SbuCode);

public sealed record ScanGate(string Outcome, int? GatePassId);

public sealed record ScanContext(
    ScanAsset Asset,
    ScanLocation Location,
    string ReaderName,
    DateTime ScannedAt,
    ScanGate Gate,
    TimeZoneInfo TimeZone,
    WorkingHours WorkingHours);

public sealed record PeriodicAsset(int Id, string Name, string Status, string SerialNumber, DateTime CreatedAt);

/// <summary>
/// Pure anomaly rules (plan section 6.4). Each rule inspects a context and returns an
/// <see cref="InsightDraft"/> or null; persisting drafts (with de-duplication) is InsightService's job.
/// </summary>
public static class AnomalyRules
{
    private static bool OutsideWorkingHours(ScanContext ctx) =>
        !ZonedTime.IsWithinWorkingHours(ctx.ScannedAt, ctx.TimeZone, ctx.WorkingHours);

    /// <summary>
    /// A gate exit that is not covered by an approved gate pass; critical when it also happens outside
    /// working hours. Authorised exits and entries raise nothing.
    /// </summary>
    public static InsightDraft? UnauthorizedGateExit(ScanContext ctx)
    {
        var (outcome, gatePassId) = ctx.Gate;
        if (outcome is not (GatePassRules.PendingCreated or GatePassRules.PendingExisting)) return null;
        var afterHours = OutsideWorkingHours(ctx);
        var sbu = string.IsNullOrEmpty(ctx.Location.SbuCode) ? "" : $" [{ctx.Location.SbuCode}]";
        var passText = gatePassId is null
            ? "no gate pass on record"
            : outcome == GatePassRules.PendingCreated
                ? $"pending gate pass #{gatePassId} raised for approval"
                : $"pending gate pass #{gatePassId} still awaiting approval";
        return new InsightDraft(
            "unauthorized_gate_exit",
            InsightTypes.UnauthorizedMovement,
            afterHours ? InsightSeverities.Critical : InsightSeverities.Warning,
            $"Unauthorised exit attempt: \"{ctx.Asset.Name}\" ({ctx.Asset.RfidTag}) detected at gate \"{ctx.Location.Name}\"{sbu}" +
            $" without an approved gate pass{(afterHours ? " outside working hours" : "")} — {passText}",
            ctx.Asset.Id,
            gatePassId);
    }

    /// <summary>The asset's owning SBU differs from the SBU of the location (e.g. the gate) that detected it.</summary>
    public static InsightDraft? SbuLocationMismatch(ScanContext ctx)
    {
        var (asset, location) = (ctx.Asset, ctx.Location);
        if (asset.SbuId is null || location.SbuId is null || asset.SbuId == location.SbuId) return null;
        var where = location.Type == LocationTypes.Gate ? $"gate \"{location.Name}\"" : $"\"{location.Name}\"";
        return new InsightDraft(
            "sbu_location_mismatch",
            InsightTypes.Anomaly,
            InsightSeverities.Warning,
            $"Location mismatch: \"{asset.Name}\" belongs to {asset.SbuCode ?? $"SBU #{asset.SbuId}"}" +
            $" but was detected at {where} ({location.SbuCode ?? $"SBU #{location.SbuId}"})",
            asset.Id);
    }

    /// <summary>A scan outside working hours, judged on the scan timestamp in the organisation time zone.</summary>
    public static InsightDraft? OutOfHours(ScanContext ctx)
    {
        if (!OutsideWorkingHours(ctx)) return null;
        var isGate = ctx.Location.Type == LocationTypes.Gate;
        return new InsightDraft(
            "out_of_hours",
            InsightTypes.UnusualPattern,
            isGate ? InsightSeverities.Warning : InsightSeverities.Info,
            $"Out-of-hours scan: \"{ctx.Asset.Name}\" detected at {(isGate ? "gate " : "")}\"{ctx.Location.Name}\"" +
            $" on {ZonedTime.FormatZoned(ctx.ScannedAt, ctx.TimeZone)} — outside working hours ({ZonedTime.DescribeWorkingHours(ctx.WorkingHours)})",
            ctx.Asset.Id);
    }

    public static readonly IReadOnlyList<Func<ScanContext, InsightDraft?>> ScanRules = [UnauthorizedGateExit, SbuLocationMismatch, OutOfHours];

    /// <summary>Run every scan rule and return the drafts that fired, in rule order.</summary>
    public static IReadOnlyList<InsightDraft> EvaluateScan(ScanContext ctx) =>
        ScanRules.Select(rule => rule(ctx)).OfType<InsightDraft>().ToList();

    // ── Periodic rules (evaluated when insights are listed) ───────────────────

    /// <summary>An active asset that has not moved (or, if never moved, not since registration) for <paramref name="idleDays"/>.</summary>
    public static InsightDraft? IdleAsset(PeriodicAsset asset, DateTime? lastMovedAt, DateTime now, int idleDays)
    {
        if (asset.Status != AssetStatuses.Active) return null;
        var since = lastMovedAt ?? asset.CreatedAt;
        if (now - since < idleDays * ZonedTime.Day) return null;
        return new InsightDraft(
            "idle_asset",
            InsightTypes.IdleAsset,
            InsightSeverities.Info,
            $"Asset \"{asset.Name}\" has not moved in over {idleDays} days — verify its location and status",
            asset.Id);
    }

    /// <summary>An asset whose status is "missing".</summary>
    public static InsightDraft? MissingAsset(PeriodicAsset asset)
    {
        if (asset.Status != AssetStatuses.Missing) return null;
        return new InsightDraft(
            "missing_asset",
            InsightTypes.MissingAsset,
            InsightSeverities.Critical,
            $"Asset \"{asset.Name}\" (SN: {asset.SerialNumber}) is marked missing — initiate recovery procedure",
            asset.Id);
    }
}
