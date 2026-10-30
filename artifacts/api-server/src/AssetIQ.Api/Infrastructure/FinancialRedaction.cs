using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AssetIQ.Api.Infrastructure;

/// <summary>
/// Cost, value, depreciation and invoice amounts reach only the roles of <see cref="AccessPolicy.CanSeeFinancials"/>
/// (product owner, 29 Oct 2026: Group IT sees the inventory only; an employee never sees what an item cost).
/// </summary>
public static class FinancialRedaction
{
    public static AssetDto Redact(AssetDto a) => a with
    {
        PurchaseValue = null, OriginalCost = null, ExchangeRate = null, CostUsd = null, ResidualValue = null,
    };

    public static AssetUpgradeDto Redact(AssetUpgradeDto u) => u with { Cost = null, InvoiceAmount = null, ExchangeRate = null };

    public static RepairDto Redact(RepairDto r) => r with { Cost = null };

    public static DisposalDto Redact(DisposalDto d) => d with { Proceeds = null, NbvAtDisposal = null, GainLoss = null };

    public static ServiceLogDto Redact(ServiceLogDto s) => s with { Cost = null, Currency = null };

    public static FleetItemDto Redact(FleetItemDto f) => f with { Services = f.Services.Select(Redact).ToList() };

    public static AssetCardDto Redact(AssetCardDto c) => c with
    {
        Asset = Redact(c.Asset),
        Value = null,
        Upgrades = c.Upgrades.Select(Redact).ToList(),
        Schedule = [],
    };

    /// <summary>The response value with every financial figure removed, or the value itself when it carries none.</summary>
    public static object? Apply(object? value) => value switch
    {
        AssetDto a => Redact(a),
        AssetCardDto c => Redact(c),
        AssetUpgradeDto u => Redact(u),
        RepairDto r => Redact(r),
        DisposalDto d => Redact(d),
        FleetItemDto f => Redact(f),
        IEnumerable<FleetItemDto> list => list.Select(Redact).ToList(),
        IEnumerable<AssetDto> list => list.Select(Redact).ToList(),
        IEnumerable<RepairDto> list => list.Select(Redact).ToList(),
        IEnumerable<DisposalDto> list => list.Select(Redact).ToList(),
        IEnumerable<AssetUpgradeDto> list => list.Select(Redact).ToList(),
        _ => value,
    };
}

/// <summary>Applies <see cref="FinancialRedaction"/> to every JSON response of a caller who may not see financial data.</summary>
public sealed class FinancialRedactionFilter : IAsyncResultFilter
{
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult result && context.HttpContext.User.GetAuthUser() is { } user && !AccessPolicy.CanSeeFinancials(user))
        {
            result.Value = FinancialRedaction.Apply(result.Value);
        }
        return next();
    }
}
