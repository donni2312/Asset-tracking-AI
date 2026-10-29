using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services.Depreciation;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

/// <param name="InvoiceAmount">The invoice amount in <paramref name="Currency"/>, captured as invoiced.</param>
/// <param name="ExchangeRate"><paramref name="Currency"/> → USD (1 for USD).</param>
public sealed record LogUpgradeInput(
    string UpgradeDate, string Description, string BeforeText, string AfterText, decimal InvoiceAmount,
    string? Supplier, string? InvoiceNo, IReadOnlyDictionary<string, string?> FieldChanges, string Currency = "USD", decimal ExchangeRate = 1m);

/// <param name="ExchangeRate">Finance may confirm a different rate for a foreign-currency invoice when deciding.</param>
public sealed record UpgradeDecision(string Treatment, string? Comment, int? RevisedLifeMonths, string? EffectivePeriod, decimal? ExchangeRate = null);

/// <summary>
/// Upgrades (AU-07): work on an asset that increased its value or capacity. SBU IT, the SBU Admin Manager or
/// Stores log it; only SBU Finance of the asset's SBU decides:
/// <list type="bullet">
/// <item>capitalise: added to cost from the month of approval (or the effective month Finance gives) and
/// depreciated over the remaining, or revised, useful life;</item>
/// <item>expense: an ordinary repair, kept in the history, not added to cost;</item>
/// <item>reject: the upgrade is not accepted; nothing changes.</item>
/// </list>
/// On capitalise or expense the asset's specification fields take their "after" values and the description is
/// rebuilt. Decisions and comments go to the audit trail.
/// </summary>
public sealed class UpgradeService(AssetIqDbContext db, AppClock clock, AuditTrail audit, AssetRegistrationService registration, DepreciationService depreciation)
{
    public async Task<AssetUpgrade> LogAsync(AuthUser user, int assetId, LogUpgradeInput input, CancellationToken ct)
    {
        var asset = await db.Assets.IgnoreQueryFilters().AsNoTracking().Include(a => a.AssetCategory).ThenInclude(c => c.Fields)
            .FirstOrDefaultAsync(a => a.Id == assetId, ct) ?? throw new ApiException(StatusCodes.Status404NotFound, "Asset not found");
        if (!AccessPolicy.CanLogUpgrade(user, asset.AssetCategory.IsIt, asset.SbuId))
            throw AccessPolicy.Deny(user, "log upgrades on this asset (SBU IT, the SBU Admin Manager or Stores of its SBU may)");
        var problems = new List<string>();
        var currency = input.Currency.Trim().ToUpperInvariant();
        if (input.InvoiceAmount <= 0) problems.Add("invoiceAmount: must be greater than 0");
        if (decimal.Round(input.InvoiceAmount, 2) != input.InvoiceAmount) problems.Add("invoiceAmount: at most 2 decimal places");
        problems.AddRange(CurrencyProblems(currency, input.ExchangeRate));
        foreach (var key in input.FieldChanges.Keys.Where(k => asset.AssetCategory.Fields.All(f => f.Key != k)))
            problems.Add($"fieldChanges.{key}: is not a field of {asset.AssetCategory.Name}");
        if (problems.Count > 0) throw new ApiException(StatusCodes.Status400BadRequest, string.Join("; ", problems));

        var upgrade = new AssetUpgrade
        {
            AssetId = assetId,
            UpgradeDate = input.UpgradeDate,
            Description = input.Description.Trim(),
            BeforeText = input.BeforeText.Trim(),
            AfterText = input.AfterText.Trim(),
            FieldChanges = JsonSerializer.Serialize(input.FieldChanges),
            Currency = currency,
            InvoiceAmount = input.InvoiceAmount,
            ExchangeRate = input.ExchangeRate,
            Cost = UsdOf(input.InvoiceAmount, input.ExchangeRate),
            Supplier = input.Supplier,
            InvoiceNo = input.InvoiceNo,
            LoggedBy = user.Name,
            LoggedByRole = user.Role,
            Status = UpgradeStatuses.Pending,
            CreatedAt = clock.UtcNow,
        };
        db.AssetUpgrades.Add(upgrade);
        await db.SaveChangesAsync(ct);
        audit.Record(user, "upgrade", upgrade.Id, assetId, "upgrade_logged", "upgrade", null,
            $"{upgrade.Description}: {upgrade.BeforeText} → {upgrade.AfterText}, {AuditTrail.Text(upgrade.Cost)}" +
            (currency == "USD" ? "" : $" ({AuditTrail.Text(upgrade.InvoiceAmount)} {currency} × {AuditTrail.Text(upgrade.ExchangeRate)})"));
        await db.SaveChangesAsync(ct);
        return upgrade;
    }

    public async Task<AssetUpgrade> DecideAsync(AuthUser user, int upgradeId, UpgradeDecision decision, CancellationToken ct)
    {
        var upgrade = await db.AssetUpgrades.IgnoreQueryFilters().Include(u => u.Asset).ThenInclude(a => a.AssetCategory).FirstOrDefaultAsync(u => u.Id == upgradeId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Upgrade not found");
        if (!AccessPolicy.IsSbuFinanceOf(user, upgrade.Asset.SbuId))
            throw AccessPolicy.Deny(user, "decide on upgrades (only SBU Finance of the asset's SBU may)");
        if (upgrade.Status != UpgradeStatuses.Pending) throw new ApiException(StatusCodes.Status409Conflict, $"The upgrade was already {upgrade.Status}");
        if (decision.ExchangeRate is { } confirmedRate)
        {
            var rateProblems = CurrencyProblems(upgrade.Currency, confirmedRate).ToList();
            if (rateProblems.Count > 0) throw new ApiException(StatusCodes.Status400BadRequest, string.Join("; ", rateProblems));
        }

        Period? effective = null;
        if (decision.Treatment == UpgradeTreatments.Capitalise)
        {
            if (upgrade.Asset.CapitalisedAt is null)
                throw new ApiException(StatusCodes.Status409Conflict, "The asset itself is not capitalised yet; capitalise it before capitalising upgrades");
            effective = depreciation.CurrentPeriod;
            if (decision.EffectivePeriod is not null)
            {
                if (!Period.TryParse(decision.EffectivePeriod, out var p)) throw new ApiException(StatusCodes.Status400BadRequest, "effectivePeriod: a month (YYYY-MM)");
                if (p > effective) throw new ApiException(StatusCodes.Status400BadRequest, "effectivePeriod: cannot be a future month");
                if (Period.OfDate(upgrade.Asset.InUseDate) is { } start && p < start)
                    throw new ApiException(StatusCodes.Status400BadRequest, "effectivePeriod: cannot be before the asset's in-use month");
                effective = p;
            }
            if (decision.RevisedLifeMonths is <= 0) throw new ApiException(StatusCodes.Status400BadRequest, "revisedLifeMonths: must be greater than 0");
        }
        else if (decision.RevisedLifeMonths is not null || decision.EffectivePeriod is not null)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "revisedLifeMonths and effectivePeriod apply only when capitalising");
        }

        var status = decision.Treatment switch
        {
            UpgradeTreatments.Capitalise => UpgradeStatuses.Capitalised,
            UpgradeTreatments.Expense => UpgradeStatuses.Expensed,
            _ => UpgradeStatuses.Rejected,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Specification fields take their "after" values (capitalise or expense); this also rebuilds the description.
        var changes = JsonSerializer.Deserialize<Dictionary<string, string?>>(upgrade.FieldChanges) ?? [];
        if (status != UpgradeStatuses.Rejected && changes.Count > 0)
        {
            await registration.UpdateTechnicalAsync(SystemActorFor(user, upgrade.Asset), upgrade.AssetId,
                new TechnicalUpdate(null, changes, null, default, default, null, null, default, default, default, $"Upgrade #{upgrade.Id} {status}: {upgrade.Description}"), ct);
        }

        if (decision.ExchangeRate is { } rate && rate != upgrade.ExchangeRate)
        {
            var cost = UsdOf(upgrade.InvoiceAmount ?? upgrade.Cost, rate);
            audit.RecordChange(user, "upgrade", upgrade.Id, upgrade.AssetId, "exchangeRate", upgrade.ExchangeRate, rate, decision.Comment);
            audit.RecordChange(user, "upgrade", upgrade.Id, upgrade.AssetId, "cost", upgrade.Cost, cost, decision.Comment);
            upgrade.ExchangeRate = rate;
            upgrade.Cost = cost;
        }
        upgrade.Status = status;
        upgrade.DecidedBy = user.Name;
        upgrade.DecidedAt = clock.UtcNow;
        upgrade.DecisionComment = decision.Comment;
        upgrade.CapitalisedPeriod = effective?.ToString();
        upgrade.RevisedLifeMonths = status == UpgradeStatuses.Capitalised ? decision.RevisedLifeMonths : null;
        audit.Record(user, "upgrade", upgrade.Id, upgrade.AssetId, $"upgrade_{status}", "status", UpgradeStatuses.Pending, status,
            decision.Comment ?? (effective is null ? null : $"Capitalised from {effective}{(decision.RevisedLifeMonths is { } m ? $", revised life {m} months" : "")}"));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return upgrade;
    }

    /// <summary>The USD value of an invoice amount, rounded to cents.</summary>
    public static decimal UsdOf(decimal amount, decimal rate) => decimal.Round(amount * rate, 2, MidpointRounding.AwayFromZero);

    /// <summary>Invoices in any currency are captured as they are (product owner, 29 Oct 2026): a 3-letter code and its rate to USD.</summary>
    public static IEnumerable<string> CurrencyProblems(string currency, decimal rate)
    {
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper)) yield return "currency: a 3-letter ISO code (USD, ZWG, ZAR)";
        if (rate <= 0) yield return "exchangeRate: must be greater than 0";
        if (currency == "USD" && rate != 1m) yield return "exchangeRate: must be 1 for USD";
    }

    /// <summary>
    /// The specification update is part of Finance's decision, not a technical edit by Finance, so it is made
    /// under the asset owner's technical role while the audit entries still name the Finance officer.
    /// </summary>
    private static AuthUser SystemActorFor(AuthUser finance, Asset asset) =>
        finance with { Role = !asset.AssetCategory.IsIt ? Roles.SbuAdmin : Roles.SbuIt };
}
