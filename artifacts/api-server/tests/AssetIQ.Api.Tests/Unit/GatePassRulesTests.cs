using AssetIQ.Api.Data;
using AssetIQ.Api.Services;

namespace AssetIQ.Api.Tests.Unit;

/// <summary>Ported from src/services/gate-pass-rules.test.ts.</summary>
public sealed class GatePassRulesTests
{
    private static readonly TimeZoneInfo Harare = TimeZoneInfo.FindSystemTimeZoneById("Africa/Harare");
    private static readonly DateTime Wednesday10Am = new(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);

    private static GatePass Pass(int id = 1, string status = "approved", string? returnDate = null, DateTime? decidedAt = null) => new()
    {
        Id = id,
        Status = status,
        ReturnDate = returnDate,
        DecidedAt = decidedAt ?? new DateTime(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void Only_approved_passes_authorise_an_exit()
    {
        Assert.False(GatePassRules.IsPassValidAt(Pass(status: "pending"), Wednesday10Am, Harare));
        Assert.False(GatePassRules.IsPassValidAt(Pass(status: "rejected"), Wednesday10Am, Harare));
        Assert.True(GatePassRules.IsPassValidAt(Pass(), Wednesday10Am, Harare));
    }

    [Fact]
    public void Is_valid_up_to_and_including_the_return_date_in_the_organisation_timezone()
    {
        Assert.True(GatePassRules.IsPassValidAt(Pass(returnDate: "2026-10-14"), Wednesday10Am, Harare));
        Assert.True(GatePassRules.IsPassValidAt(Pass(returnDate: "2026-10-20"), Wednesday10Am, Harare));
        Assert.False(GatePassRules.IsPassValidAt(Pass(returnDate: "2026-10-13"), Wednesday10Am, Harare));
    }

    [Fact]
    public void Does_not_authorise_a_scan_that_happened_before_the_approval()
    {
        Assert.False(GatePassRules.IsPassValidAt(Pass(decidedAt: new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc)), Wednesday10Am, Harare));
    }

    [Fact]
    public void Returns_the_most_recently_approved_valid_pass()
    {
        var older = Pass(1, decidedAt: new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc));
        var newer = Pass(2, decidedAt: new DateTime(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc));
        var expired = Pass(3, returnDate: "2026-10-01", decidedAt: new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc));
        Assert.Equal(2, GatePassRules.FindValidApprovedPass([older, expired, newer], Wednesday10Am, Harare)?.Id);
    }

    [Fact]
    public void Returns_nothing_when_no_pass_is_valid()
    {
        Assert.Null(GatePassRules.FindValidApprovedPass([Pass(status: "pending")], Wednesday10Am, Harare));
    }

    [Fact]
    public void Infers_direction_from_whether_the_asset_is_on_the_premises()
    {
        Assert.Equal("internal", GatePassRules.GateDirection(false, true));
        Assert.Equal("exit", GatePassRules.GateDirection(true, true));
        Assert.Equal("entry", GatePassRules.GateDirection(true, false));
    }

    [Theory]
    [InlineData(false, "internal", false, false, "not_gate", true)]
    [InlineData(true, "entry", false, false, "entry", true)]
    [InlineData(true, "exit", true, false, "authorized", true)]
    [InlineData(true, "exit", false, true, "pending_existing", false)]
    [InlineData(true, "exit", false, false, "pending_created", false)]
    public void Resolves_the_gate_outcome(bool isGate, string direction, bool hasValidPass, bool hasPendingPass, string outcome, bool authorized)
    {
        var result = GatePassRules.ResolveGateOutcome(isGate, direction, hasValidPass, hasPendingPass);
        Assert.Equal(outcome, result);
        Assert.Equal(authorized, GatePassRules.IsAuthorizedOutcome(result));
    }

    [Fact]
    public void Accepts_empty_today_and_future_return_dates()
    {
        Assert.Null(GatePassRules.ValidateReturnDate(null, Wednesday10Am, Harare));
        Assert.Null(GatePassRules.ValidateReturnDate("2026-10-14", Wednesday10Am, Harare));
        Assert.Null(GatePassRules.ValidateReturnDate("2026-11-01", Wednesday10Am, Harare));
    }

    [Fact]
    public void Rejects_past_or_malformed_return_dates()
    {
        Assert.Contains("past", GatePassRules.ValidateReturnDate("2026-10-13", Wednesday10Am, Harare));
        Assert.Contains("YYYY-MM-DD", GatePassRules.ValidateReturnDate("14/10/2026", Wednesday10Am, Harare));
    }
}
