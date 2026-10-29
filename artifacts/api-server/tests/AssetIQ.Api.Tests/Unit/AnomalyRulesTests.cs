using AssetIQ.Api.Configuration;
using AssetIQ.Api.Services;

namespace AssetIQ.Api.Tests.Unit;

/// <summary>Ported from src/services/anomaly-rules.test.ts.</summary>
public sealed class AnomalyRulesTests
{
    private static readonly TimeZoneInfo Harare = TimeZoneInfo.FindSystemTimeZoneById("Africa/Harare");
    private static readonly DateTime InHours = new(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc); // Wed 10:00 Harare
    private static readonly DateTime Saturday2Am = new(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc); // Sat 02:00 Harare

    private static readonly ScanLocation Office = new(2, "TSAS IT Office", "office", 1, "TSAS");
    private static readonly ScanAsset Laptop = new(7, "Dell Latitude 5440", "TAG-1", 1, "TSAS");
    private static readonly ScanLocation TsasGate = new(3, "TSAS Main Gate", "gate", 1, "TSAS");
    private static readonly ScanLocation BakGate = new(9, "BAK Depot Gate", "gate", 2, "BAK");

    private static ScanContext Ctx(
        ScanAsset? asset = null, ScanLocation? location = null, DateTime? scannedAt = null, ScanGate? gate = null) =>
        new(asset ?? Laptop, location ?? TsasGate, "TSAS Gate Reader", scannedAt ?? InHours, gate ?? new ScanGate("authorized", 12),
            Harare, new WorkingHours(7, 18, [1, 2, 3, 4, 5]));

    [Fact]
    public void Raises_nothing_for_an_authorised_gate_exit()
    {
        Assert.Empty(AnomalyRules.EvaluateScan(Ctx()));
    }

    [Fact]
    public void Raises_nothing_for_an_asset_returning_through_a_gate()
    {
        Assert.Empty(AnomalyRules.EvaluateScan(Ctx(gate: new ScanGate("entry", null))));
    }

    [Fact]
    public void Raises_nothing_for_an_in_hours_scan_in_the_assets_own_sbu()
    {
        Assert.Empty(AnomalyRules.EvaluateScan(Ctx(location: Office, gate: new ScanGate("not_gate", null))));
    }

    [Fact]
    public void Combines_rules_for_an_unauthorised_out_of_hours_exit_at_another_sbus_gate()
    {
        var drafts = AnomalyRules.EvaluateScan(Ctx(scannedAt: Saturday2Am, location: BakGate, gate: new ScanGate("pending_created", 30)));
        Assert.Equal(["unauthorized_gate_exit", "sbu_location_mismatch", "out_of_hours"], drafts.Select(d => d.Rule));
    }

    [Fact]
    public void Emits_unauthorized_movement_when_a_gate_exit_has_no_approved_pass()
    {
        var draft = AnomalyRules.UnauthorizedGateExit(Ctx(gate: new ScanGate("pending_created", 12)));
        Assert.NotNull(draft);
        Assert.Equal(("unauthorized_movement", "warning", 7, 12), (draft.Type, draft.Severity, draft.AssetId, draft.GatePassId));
        Assert.Contains("pending gate pass #12 raised for approval", draft.Message);
    }

    [Fact]
    public void Mentions_an_existing_pending_pass_instead_of_raising_a_new_one()
    {
        Assert.Contains("still awaiting approval", AnomalyRules.UnauthorizedGateExit(Ctx(gate: new ScanGate("pending_existing", 12)))?.Message);
    }

    [Fact]
    public void Escalates_to_critical_outside_working_hours()
    {
        var draft = AnomalyRules.UnauthorizedGateExit(Ctx(scannedAt: Saturday2Am, gate: new ScanGate("pending_created", 12)));
        Assert.Equal("critical", draft?.Severity);
        Assert.Contains("outside working hours", draft?.Message);
    }

    [Theory]
    [InlineData("authorized")]
    [InlineData("entry")]
    [InlineData("not_gate")]
    public void Stays_quiet_for_other_outcomes(string outcome)
    {
        Assert.Null(AnomalyRules.UnauthorizedGateExit(Ctx(gate: new ScanGate(outcome, null))));
    }

    [Fact]
    public void Flags_an_asset_detected_at_a_gate_that_belongs_to_a_different_sbu()
    {
        var draft = AnomalyRules.SbuLocationMismatch(Ctx(location: BakGate));
        Assert.NotNull(draft);
        Assert.Equal(("anomaly", "warning", 7), (draft.Type, draft.Severity, draft.AssetId));
        Assert.Equal("Location mismatch: \"Dell Latitude 5440\" belongs to TSAS but was detected at gate \"BAK Depot Gate\" (BAK)", draft.Message);
    }

    [Fact]
    public void Does_not_flag_a_mismatch_when_either_side_has_no_sbu_or_they_match()
    {
        Assert.Null(AnomalyRules.SbuLocationMismatch(Ctx()));
        Assert.Null(AnomalyRules.SbuLocationMismatch(Ctx(asset: Laptop with { SbuId = null, SbuCode = null })));
        Assert.Null(AnomalyRules.SbuLocationMismatch(Ctx(location: TsasGate with { SbuId = null, SbuCode = null })));
    }

    [Fact]
    public void Judges_out_of_hours_on_the_scan_timestamp_not_the_processing_time()
    {
        Assert.Null(AnomalyRules.OutOfHours(Ctx(scannedAt: InHours)));
        var draft = AnomalyRules.OutOfHours(Ctx(scannedAt: Saturday2Am));
        Assert.Equal(("unusual_pattern", "warning"), (draft?.Type, draft?.Severity));
        Assert.Contains("Sat 17 Oct 02:00", draft?.Message);
    }

    [Fact]
    public void Out_of_hours_is_informational_away_from_the_gates()
    {
        Assert.Equal("info", AnomalyRules.OutOfHours(Ctx(scannedAt: Saturday2Am, location: Office))?.Severity);
    }

    private static readonly DateTime Now = new(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);
    private static readonly PeriodicAsset Projector = new(1, "Epson Projector", "in_store", "SN1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Flags_active_assets_idle_for_longer_than_the_threshold()
    {
        Assert.Equal("idle_asset", AnomalyRules.IdleAsset(Projector, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), Now, 7)?.Type);
        Assert.Null(AnomalyRules.IdleAsset(Projector, new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), Now, 7));
    }

    [Fact]
    public void Measures_never_moved_assets_from_their_registration_date()
    {
        Assert.Null(AnomalyRules.IdleAsset(Projector with { CreatedAt = new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc) }, null, Now, 7));
        Assert.NotNull(AnomalyRules.IdleAsset(Projector, null, Now, 7));
    }

    [Fact]
    public void Ignores_assets_that_are_not_active_and_flags_missing_ones_as_critical()
    {
        Assert.Null(AnomalyRules.IdleAsset(Projector with { Status = "disposed" }, null, Now, 7));
        Assert.Equal("critical", AnomalyRules.MissingAsset(Projector with { Status = "lost_stolen" })?.Severity);
        Assert.Null(AnomalyRules.MissingAsset(Projector));
    }
}
