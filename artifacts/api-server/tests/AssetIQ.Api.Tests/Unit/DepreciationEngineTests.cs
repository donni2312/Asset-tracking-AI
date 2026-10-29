using AssetIQ.Api.Domain;
using AssetIQ.Api.Services.Depreciation;

namespace AssetIQ.Api.Tests.Unit;

/// <summary>AU-05 / GF-03 / SF-03 / AU-07: the depreciation rules, including the worked example of the brief.</summary>
public sealed class DepreciationEngineTests
{
    private static readonly Period Jan2025 = new(2025, 1);
    private static readonly DepreciationPolicy FourYearsStraightLine = new(DepreciationMethods.StraightLine, 48, null);

    private static IReadOnlyList<MonthLine> Schedule(decimal cost, DepreciationPolicy policy, Period through, decimal residual = 0m,
        IReadOnlyList<CapitalAddition>? additions = null, Period? disposal = null) =>
        DepreciationEngine.Schedule(new DepreciationInput(cost, Jan2025, policy, residual, additions ?? [], disposal), through);

    [Fact]
    public void AU_05_worked_example_straight_line_with_an_upgrade_capitalised_in_month_13()
    {
        // $1,200 over 48 months, no residual; month 13 a $144 upgrade is capitalised (remaining life 36 months).
        var month13 = Jan2025.AddMonths(12);
        var lines = Schedule(1200m, FourYearsStraightLine, month13, additions: [new CapitalAddition(month13, 144m)]);

        Assert.Equal(13, lines.Count);
        Assert.All(lines.Take(12), l => Assert.Equal(25.00m, l.Charge));
        var month12 = lines[11];
        Assert.Equal((300.00m, 900.00m), (month12.ClosingAccumulated, month12.ClosingNbv));

        var m13 = lines[12];
        Assert.Equal(29.00m, m13.Charge); // 25.00 + 144.00 ÷ 36
        Assert.Equal(1344.00m, m13.OpeningCost);
        Assert.Equal((329.00m, 1015.00m), (m13.ClosingAccumulated, m13.ClosingNbv));
    }

    [Fact]
    public void AU_05_after_the_upgrade_the_charge_stays_29_and_the_asset_is_fully_depreciated_at_the_end_of_its_life()
    {
        var month13 = Jan2025.AddMonths(12);
        var lines = Schedule(1200m, FourYearsStraightLine, Jan2025.AddMonths(59), additions: [new CapitalAddition(month13, 144m)]);

        Assert.All(lines.Skip(12).Take(36), l => Assert.Equal(29.00m, l.Charge));
        Assert.Equal(48, lines.Count(l => l.Charge > 0));
        Assert.Equal(1344.00m, lines.Last().ClosingAccumulated);
        Assert.Equal(0m, lines.Last().ClosingNbv);
    }

    [Fact]
    public void AU_05_reducing_balance_charges_opening_nbv_times_the_annual_rate_over_12()
    {
        var policy = new DepreciationPolicy(DepreciationMethods.ReducingBalance, null, 0.24m);
        var lines = Schedule(10_000m, policy, Jan2025.AddMonths(2));

        Assert.Equal(200.00m, lines[0].Charge);              // 10,000.00 × 2%
        Assert.Equal(196.00m, lines[1].Charge);              //  9,800.00 × 2%
        Assert.Equal(192.08m, lines[2].Charge);              //  9,604.00 × 2%
        Assert.Equal(9411.92m, lines[2].ClosingNbv);
    }

    [Fact]
    public void AU_05_charges_are_rounded_to_cents_and_add_up_exactly_to_the_cost()
    {
        var lines = Schedule(1000m, new DepreciationPolicy(DepreciationMethods.StraightLine, 3, null), Jan2025.AddMonths(5));

        // 1,000 ÷ 3 = 333.33; then 666.67 ÷ 2 = 333.335 → 333.34; the last month takes what is left.
        Assert.Equal(new[] { 333.33m, 333.34m, 333.33m }, lines.Take(3).Select(l => l.Charge));
        Assert.Equal(1000m, lines.Sum(l => l.Charge));
        Assert.Equal(0m, lines.Last().ClosingNbv);
        Assert.All(lines.Skip(3), l => Assert.Equal(0m, l.Charge));
    }

    [Fact]
    public void AU_05_depreciation_never_takes_nbv_below_the_residual_value()
    {
        var straight = Schedule(1200m, FourYearsStraightLine, Jan2025.AddMonths(60), residual: 200m);
        Assert.Equal(20.83m, straight[0].Charge); // (1,200 − 200) ÷ 48
        Assert.Equal(200m, straight.Last().ClosingNbv);
        Assert.All(straight, l => Assert.True(l.ClosingNbv >= 200m));

        var reducing = Schedule(1000m, new DepreciationPolicy(DepreciationMethods.ReducingBalance, null, 0.9m), Jan2025.AddMonths(120), residual: 150m);
        Assert.Equal(150m, reducing.Last().ClosingNbv);
        Assert.All(reducing, l => Assert.True(l.ClosingNbv >= 150m));
    }

    [Fact]
    public void AU_07_an_expensed_upgrade_changes_neither_cost_nor_charge()
    {
        // An expensed upgrade is not a capital addition: the schedule is the one without it.
        var without = Schedule(1200m, FourYearsStraightLine, Jan2025.AddMonths(20));
        Assert.All(without, l => Assert.Equal((1200m, 25m), (l.OpeningCost, l.Charge)));
    }

    [Fact]
    public void AU_07_a_revised_useful_life_spreads_the_remaining_cost_over_the_new_life()
    {
        var month13 = Jan2025.AddMonths(12);
        var lines = Schedule(1200m, FourYearsStraightLine, Jan2025.AddMonths(30), additions: [new CapitalAddition(month13, 144m, RevisedLifeMonths: 12)]);

        Assert.Equal(87.00m, lines[12].Charge); // (1,344 − 300) ÷ 12
        Assert.Equal(0m, lines[23].ClosingNbv);
        Assert.All(lines.Skip(24), l => Assert.Equal(0m, l.Charge));
    }

    [Fact]
    public void AU_05_disposed_assets_stop_depreciating_from_the_disposal_month()
    {
        var lines = Schedule(1200m, FourYearsStraightLine, Jan2025.AddMonths(11), disposal: Jan2025.AddMonths(6));

        Assert.Equal(6, lines.Count);
        Assert.Equal(new Period(2025, 6), lines.Last().Period);
        Assert.Equal(150m, lines.Last().ClosingAccumulated);
    }

    [Fact]
    public void AU_05_a_change_that_affects_a_locked_month_is_posted_as_an_adjustment_in_the_next_open_month()
    {
        // Months 1–12 were run and locked at $25.00. A back-dated $144 upgrade effective in month 7 (a locked month)
        // changes what months 7–12 should have charged; locked months stay as posted and month 13 catches up.
        var lockedThrough = Jan2025.AddMonths(11);
        var posted = Schedule(1200m, FourYearsStraightLine, lockedThrough)
            .ToDictionary(l => l.Period, l => new PostedMonth(l.Period, l.OpeningCost, l.OpeningAccumulated, l.Charge, l.Adjustment));
        var month7 = Jan2025.AddMonths(6);
        var ideal = Schedule(1200m, FourYearsStraightLine, Jan2025.AddMonths(13), additions: [new CapitalAddition(month7, 144m)]);

        var ledger = DepreciationLedger.Merge(ideal, posted, lockedThrough);

        Assert.All(ledger.Take(12), l => Assert.Equal((25.00m, 0m), (l.Charge, l.Adjustment)));
        Assert.Equal(300.00m, ledger[11].ClosingAccumulated);
        var month13 = ledger[12];
        var shouldHaveBeen = ideal[11].ClosingAccumulated; // 6 × 25 + 6 × (25 + 144/42)
        Assert.Equal(shouldHaveBeen - 300.00m, month13.Adjustment);
        Assert.Equal(ideal[12].ClosingAccumulated, month13.ClosingAccumulated);
        Assert.Equal((ideal[13].Charge, 0m), (ledger[13].Charge, ledger[13].Adjustment));
    }

    [Fact]
    public void AU_05_a_locked_month_that_was_never_posted_posts_nothing_and_the_first_open_month_catches_up()
    {
        var ideal = Schedule(1200m, FourYearsStraightLine, Jan2025.AddMonths(3));
        var ledger = DepreciationLedger.Merge(ideal, new Dictionary<Period, PostedMonth>(), Jan2025.AddMonths(1));

        Assert.Equal((0m, 0m), (ledger[0].Posted, ledger[1].Posted));
        Assert.Equal((25m, 50m), (ledger[2].Charge, ledger[2].Adjustment));
        Assert.Equal(75m, ledger[2].ClosingAccumulated);
    }

    [Fact]
    public void Periods_parse_compare_and_step_across_years()
    {
        Assert.Equal(new Period(2026, 10), Period.Parse("2026-10"));
        Assert.Equal(new Period(2026, 10), Period.OfDate("2026-10-19"));
        Assert.Equal(new Period(2027, 1), new Period(2026, 12).Next());
        Assert.Equal(new Period(2025, 12), new Period(2026, 1).Previous());
        Assert.Equal(13, new Period(2025, 1).MonthsUntil(new Period(2026, 2)));
        Assert.True(new Period(2025, 12) < new Period(2026, 1));
        Assert.False(Period.TryParse("2026-13", out _));
    }
}
