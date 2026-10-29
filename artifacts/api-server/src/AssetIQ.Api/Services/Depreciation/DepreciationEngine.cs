using System.Globalization;
using AssetIQ.Api.Domain;

namespace AssetIQ.Api.Services.Depreciation;

/// <summary>A calendar month, "YYYY-MM" on the wire and in the database.</summary>
public readonly record struct Period(int Year, int Month) : IComparable<Period>
{
    public static Period Parse(string text)
    {
        if (TryParse(text, out var period)) return period;
        throw new FormatException($"'{text}' is not a month (YYYY-MM)");
    }

    public static bool TryParse(string? text, out Period period)
    {
        period = default;
        if (text is null || text.Length < 7) return false;
        if (!DateTime.TryParseExact(text[..7], "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return false;
        period = new Period(d.Year, d.Month);
        return true;
    }

    /// <summary>The month of a YYYY-MM-DD date.</summary>
    public static Period? OfDate(string? date) => TryParse(date, out var p) ? p : null;

    public static Period Of(DateTime instant) => new(instant.Year, instant.Month);

    public Period Next() => Month == 12 ? new(Year + 1, 1) : new(Year, Month + 1);

    public Period Previous() => Month == 1 ? new(Year - 1, 12) : new(Year, Month - 1);

    public Period AddMonths(int months)
    {
        var index = Year * 12 + (Month - 1) + months;
        return new(index / 12, index % 12 + 1);
    }

    /// <summary>Months from this period to <paramref name="other"/> (other − this).</summary>
    public int MonthsUntil(Period other) => (other.Year * 12 + other.Month) - (Year * 12 + Month);

    public Period StartOfYear => new(Year, 1);

    public int CompareTo(Period other) => (Year, Month).CompareTo((other.Year, other.Month));

    public static bool operator <(Period a, Period b) => a.CompareTo(b) < 0;
    public static bool operator >(Period a, Period b) => a.CompareTo(b) > 0;
    public static bool operator <=(Period a, Period b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Period a, Period b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Year:D4}-{Month:D2}";
}

/// <summary>Group depreciation policy of a category (GF-03).</summary>
public sealed record DepreciationPolicy(string Method, int? UsefulLifeMonths, decimal? AnnualRate);

/// <summary>An amount added to cost from <see cref="Period"/> on: a capitalised upgrade (AU-07).</summary>
/// <param name="RevisedLifeMonths">Remaining life entered by Finance, counted from <see cref="Period"/>; null keeps the current remaining life.</param>
public sealed record CapitalAddition(Period Period, decimal Amount, int? RevisedLifeMonths = null);

public sealed record DepreciationInput(
    decimal Cost, Period Start, DepreciationPolicy Policy, decimal ResidualValue,
    IReadOnlyList<CapitalAddition> Additions, Period? DisposalPeriod = null);

/// <summary>One month of a schedule. Opening cost includes additions capitalised in that month.</summary>
public sealed record MonthLine(Period Period, decimal OpeningCost, decimal OpeningAccumulated, decimal Charge, decimal Adjustment)
{
    public decimal ClosingAccumulated => OpeningAccumulated + Adjustment + Charge;
    public decimal ClosingNbv => OpeningCost - ClosingAccumulated;
    /// <summary>What the month posts to the books: its charge plus any catch-up adjustment.</summary>
    public decimal Posted => Charge + Adjustment;
}

/// <summary>
/// Monthly depreciation (AU-05, GF-03, SF-03). Pure and deterministic: money is decimal, each month's charge
/// is rounded to cents (half away from zero). Rules:
/// <list type="bullet">
/// <item>The schedule starts in the in-use month with a full month's charge (Group setting depreciation_convention).</item>
/// <item>Straight-line: (cost − accumulated − residual) ÷ remaining months. With no additions this is the
/// constant (cost − residual) ÷ life; a capitalised upgrade is spread over the remaining (or revised) life.</item>
/// <item>Reducing-balance: opening NBV × annual rate ÷ 12.</item>
/// <item>NBV never goes below the residual value; nothing is charged from the disposal month on.</item>
/// </list>
/// </summary>
public static class DepreciationEngine
{
    // ASSUMPTION: the brief's "(depreciable cost − residual value) ÷ remaining months" is read as the cost not
    // yet depreciated (cost − accumulated) over the remaining months: the only reading that reproduces the
    // worked example ($1,200/48 months, $144 upgrade in month 13 → $29.00 a month).
    public static IReadOnlyList<MonthLine> Schedule(DepreciationInput input, Period through)
    {
        var lines = new List<MonthLine>();
        var policy = input.Policy;
        var straightLine = policy.Method == DepreciationMethods.StraightLine;
        if (straightLine && policy.UsefulLifeMonths is not > 0) throw new ArgumentException("Straight-line depreciation needs a useful life");
        if (!straightLine && policy.AnnualRate is not > 0) throw new ArgumentException("Reducing-balance depreciation needs an annual rate");

        var cost = input.Cost;
        var accumulated = 0m;
        // Last month of the useful life (straight-line).
        var lifeEnd = input.Start.AddMonths((policy.UsefulLifeMonths ?? 1) - 1);

        for (var period = input.Start; period <= through; period = period.Next())
        {
            if (input.DisposalPeriod is { } disposal && period >= disposal) break;

            foreach (var addition in input.Additions.Where(a => a.Period == period))
            {
                cost += addition.Amount;
                if (addition.RevisedLifeMonths is > 0 and var revised) lifeEnd = period.AddMonths(revised - 1);
            }

            var depreciable = Math.Max(0m, cost - accumulated - input.ResidualValue);
            decimal charge;
            if (straightLine)
            {
                var remaining = Math.Max(1, period.MonthsUntil(lifeEnd) + 1);
                charge = Round(depreciable / remaining);
            }
            else
            {
                charge = Round((cost - accumulated) * policy.AnnualRate!.Value / 12m);
            }
            charge = Math.Min(Math.Max(charge, 0m), depreciable);

            lines.Add(new MonthLine(period, cost, accumulated, charge, 0m));
            accumulated += charge;
        }
        return lines;
    }

    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}

/// <summary>A stored month (depreciation_entries) as the ledger needs it.</summary>
public sealed record PostedMonth(Period Period, decimal OpeningCost, decimal OpeningAccumulated, decimal Charge, decimal Adjustment);

/// <summary>
/// Combines the schedule as it should be today with the months already locked by SBU Finance (AU-05):
/// a locked month is final and is never recalculated; the difference a later change makes to locked months
/// is posted as an adjustment in the first open month, after which the ledger follows the schedule exactly.
/// </summary>
public static class DepreciationLedger
{
    /// <param name="ideal">The schedule computed from today's data.</param>
    /// <param name="posted">Months stored by earlier runs.</param>
    /// <param name="lockedThrough">The latest locked month of the asset's SBU (it and all earlier months are locked).</param>
    public static IReadOnlyList<MonthLine> Merge(IReadOnlyList<MonthLine> ideal, IReadOnlyDictionary<Period, PostedMonth> posted, Period? lockedThrough)
    {
        var result = new List<MonthLine>(ideal.Count);
        var closing = 0m;
        foreach (var line in ideal)
        {
            if (lockedThrough is { } locked && line.Period <= locked)
            {
                // Final as posted. A locked month that was never posted (e.g. an asset back-dated into it) posts nothing.
                var month = posted.TryGetValue(line.Period, out var p)
                    ? new MonthLine(p.Period, p.OpeningCost, p.OpeningAccumulated, p.Charge, p.Adjustment)
                    : new MonthLine(line.Period, line.OpeningCost, closing, 0m, 0m);
                result.Add(month);
                closing = month.ClosingAccumulated;
                continue;
            }
            var adjustment = line.OpeningAccumulated - closing;
            var open = new MonthLine(line.Period, line.OpeningCost, closing, line.Charge, adjustment);
            result.Add(open);
            closing = open.ClosingAccumulated;
        }
        return result;
    }
}
