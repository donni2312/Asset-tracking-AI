using System.Linq.Expressions;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

/// <summary>The most recent movement of an asset.</summary>
public sealed record LastMovement(int AssetId, DateTime Timestamp, string TriggeredBy, int? GatePassId);

/// <summary>Assets in the shape of the API's <c>Asset</c> schema (with location and SBU names).</summary>
public sealed class AssetQueries(AssetIqDbContext db)
{
    public static readonly Expression<Func<Asset, AssetDto>> ToDto = a => new AssetDto
    {
        Id = a.Id,
        Name = a.Name,
        Category = a.Category,
        CategoryId = a.CategoryId,
        CategoryName = a.AssetCategory.Name,
        SubCategory = a.SubCategory,
        SerialNumber = a.SerialNumber,
        NoSerialReason = a.NoSerialReason,
        NoSerialStatus = a.NoSerialStatus,
        MacAddress = a.MacAddress,
        RfidTag = a.RfidTag,
        AssetNumber = a.AssetNumber,
        PreviousAssetNumber = a.PreviousAssetNumber,
        Status = a.Status,
        Condition = a.Condition,
        AssignedTo = a.AssignedTo,
        CurrentLocationId = a.CurrentLocationId,
        CurrentLocationName = a.CurrentLocation!.Name,
        SbuId = a.SbuId,
        SbuName = a.Sbu!.Name,
        SbuCode = a.Sbu!.Code,
        PurchaseDate = a.PurchaseDate,
        PurchaseValue = a.PurchaseValue,
        OriginalCost = a.OriginalCost,
        Currency = a.Currency,
        ExchangeRate = a.ExchangeRate,
        CostUsd = a.CostUsd,
        CapitalisedAt = a.CapitalisedAt,
        CapitalisedBy = a.CapitalisedBy,
        ResidualValue = a.ResidualValue,
        Supplier = a.Supplier,
        InvoiceNo = a.InvoiceNo,
        PoNo = a.PoNo,
        InUseDate = a.InUseDate,
        WarrantyEnds = a.WarrantyEnds,
        DisposalDate = a.DisposalDate,
        Notes = a.Notes,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt,
    };

    /// <summary>Every asset matching <paramref name="where"/>, ordered by name.</summary>
    public Task<List<AssetDto>> ListDetailedAsync(Expression<Func<Asset, bool>>? where = null, CancellationToken ct = default)
    {
        IQueryable<Asset> query = db.Assets.AsNoTracking();
        if (where is not null) query = query.Where(where);
        return query.OrderBy(a => a.Name).Select(ToDto).ToListAsync(ct);
    }

    public Task<AssetDto?> GetDetailedAsync(int id, CancellationToken ct = default) =>
        db.Assets.AsNoTracking().Where(a => a.Id == id).Select(ToDto).FirstOrDefaultAsync(ct);

    /// <summary>Most recent movement per asset (latest timestamp, then highest id).</summary>
    public async Task<Dictionary<int, LastMovement>> LastMovementsForAsync(IReadOnlyCollection<int> assetIds, CancellationToken ct = default)
    {
        if (assetIds.Count == 0) return [];
        var ids = assetIds.ToList();
        var rows = await db.Movements.AsNoTracking()
            .Where(m => ids.Contains(m.AssetId))
            .GroupBy(m => m.AssetId)
            .Select(g => g
                .OrderByDescending(m => m.Timestamp)
                .ThenByDescending(m => m.Id)
                .Select(m => new LastMovement(m.AssetId, m.Timestamp, m.TriggeredBy, m.GatePassId))
                .First())
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.AssetId);
    }

    /// <summary>
    /// Time of the latest movement of every asset that has moved at least once, across every SBU (the AI rules
    /// need it); callers only read it for assets they already hold.
    /// </summary>
    public async Task<Dictionary<int, DateTime>> LastMovedAtAsync(CancellationToken ct = default)
    {
        var rows = await db.Movements.IgnoreQueryFilters().AsNoTracking()
            .GroupBy(m => m.AssetId)
            .Select(g => new { AssetId = g.Key, LastMovedAt = g.Max(m => m.Timestamp) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.AssetId, r => r.LastMovedAt);
    }
}
