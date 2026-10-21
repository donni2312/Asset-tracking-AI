using System.Text.RegularExpressions;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;

namespace AssetIQ.Api.Services;

/// <summary>
/// Pure gate-pass rules (no database access) — the decision logic behind the plan's section 6.3
/// workflow, kept separate so it can be unit tested.
/// </summary>
public static partial class GatePassRules
{
    // Gate direction: exit = the asset was on the premises; entry = it was off-premises (returning);
    // internal = the reader is not at a gate.
    public const string Entry = "entry";
    public const string Exit = "exit";
    public const string Internal = "internal";

    // Gate outcome.
    public const string NotGate = "not_gate";
    public const string EntryOutcome = "entry";
    /// <summary>Exit covered by a valid approved pass.</summary>
    public const string Authorized = "authorized";
    /// <summary>Exit without a pass — a pending pass was raised automatically.</summary>
    public const string PendingCreated = "pending_created";
    /// <summary>Exit without a pass — a pending pass already exists.</summary>
    public const string PendingExisting = "pending_existing";

    /// <summary>
    /// An approved pass authorises exits from the moment it is approved until the end of its return
    /// date (organisation time zone). A pass with no return date is open-ended and stays valid.
    /// </summary>
    public static bool IsPassValidAt(GatePass pass, DateTime at, TimeZoneInfo zone)
    {
        if (pass.Status != GatePassStatuses.Approved) return false;
        if (pass.DecidedAt is { } decidedAt && decidedAt > at) return false; // approved after the scan
        if (string.IsNullOrEmpty(pass.ReturnDate)) return true;
        return string.CompareOrdinal(pass.ReturnDate, ZonedTime.DateKey(at, zone)) >= 0;
    }

    /// <summary>The most recently approved pass that is valid at <paramref name="at"/>, if any.</summary>
    public static GatePass? FindValidApprovedPass(IEnumerable<GatePass> passes, DateTime at, TimeZoneInfo zone) =>
        passes.Where(p => IsPassValidAt(p, at, zone))
            .OrderByDescending(p => p.DecidedAt ?? DateTime.UnixEpoch)
            .ThenByDescending(p => p.Id)
            .FirstOrDefault();

    /// <summary>
    /// Gate readers in the MVP are not directional, so direction is inferred from the asset's state: an
    /// asset with a known on-site location is leaving; an off-premises asset (no location) is coming back.
    /// </summary>
    public static string GateDirection(bool isGate, bool assetOnPremises) =>
        !isGate ? Internal : assetOnPremises ? Exit : Entry;

    public static string ResolveGateOutcome(bool isGate, string direction, bool hasValidPass, bool hasPendingPass)
    {
        if (!isGate) return NotGate;
        if (direction == Entry) return EntryOutcome;
        if (hasValidPass) return Authorized;
        return hasPendingPass ? PendingExisting : PendingCreated;
    }

    /// <summary>False only for gate exits that are not covered by an approved pass.</summary>
    public static bool IsAuthorizedOutcome(string outcome) => outcome is not (PendingCreated or PendingExisting);

    /// <summary>An error message when the requested return date is malformed or in the past, otherwise null.</summary>
    public static string? ValidateReturnDate(string? returnDate, DateTime now, TimeZoneInfo zone)
    {
        if (string.IsNullOrEmpty(returnDate)) return null;
        if (!IsoDate().IsMatch(returnDate)) return "returnDate must be a date (YYYY-MM-DD)";
        return string.CompareOrdinal(returnDate, ZonedTime.DateKey(now, zone)) < 0 ? "returnDate cannot be in the past" : null;
    }

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}$")]
    private static partial Regex IsoDate();
}
