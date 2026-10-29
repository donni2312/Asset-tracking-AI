using System.Globalization;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

/// <summary>What Business Central needs to open a fixed-asset card.</summary>
public sealed record BcFixedAssetRequest(int AssetId, string SbuCode, string ClassCode, int Year, string Description);

/// <summary>Business Central could not be reached or refused the request; the request stays queued.</summary>
public sealed class BcUnavailableException(string message) : Exception(message);

/// <summary>Issues Business Central fixed-asset numbers (AU-03).</summary>
public interface IBcFixedAssetClient
{
    Task<string> CreateFixedAssetAsync(BcFixedAssetRequest request, CancellationToken ct);
}

/// <summary>
/// Stand-in for Business Central until the real integration (decision: use mock numbers for now). It
/// issues numbers in the format the mockups show, {SBU}-{CLASS}-{YEAR}-{NNNNN} (e.g. BAK-IT-2026-00047),
/// from a counter in bc_mock_number_series, so a number is never issued twice. <see cref="Available"/>
/// = false makes it behave as if BC were down (the BC_MOCK_UNAVAILABLE setting, and the tests).
/// </summary>
public sealed class MockBcFixedAssetClient(IServiceScopeFactory scopes, IConfiguration configuration) : IBcFixedAssetClient
{
    public bool Available { get; set; } = !string.Equals(configuration["BC_MOCK_UNAVAILABLE"], "true", StringComparison.OrdinalIgnoreCase);

    public async Task<string> CreateFixedAssetAsync(BcFixedAssetRequest request, CancellationToken ct)
    {
        if (!Available) throw new BcUnavailableException("Business Central is not reachable (mock set to unavailable)");
        var prefix = $"{request.SbuCode}-{request.ClassCode}-{request.Year:D4}";
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetIqDbContext>();
        // Numbers already on the register (imported or seeded) are skipped, as BC's own number series would.
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var next = await db.Database.SqlQuery<int>($"""
                INSERT INTO bc_mock_number_series (prefix, last_number) VALUES ({prefix}, 1)
                ON CONFLICT (prefix) DO UPDATE SET last_number = bc_mock_number_series.last_number + 1
                RETURNING last_number AS "Value"
                """).ToListAsync(ct);
            var number = $"{prefix}-{next[0].ToString("D5", CultureInfo.InvariantCulture)}";
            if (!await db.Assets.IgnoreQueryFilters().AnyAsync(a => a.AssetNumber == number, ct)) return number;
        }
        throw new BcUnavailableException($"The mock number series {prefix} is exhausted");
    }
}

/// <summary>
/// The queue of asset-number requests (AU-03). An asset created in AssetIQ is "Pending asset number" until
/// Business Central returns its number; requests BC could not answer are retried with back-off by
/// <see cref="BcNumberWorker"/>. A number is never made up locally, and once set it can never change
/// (the database enforces it).
/// </summary>
public sealed class BcNumberService(AssetIqDbContext db, IBcFixedAssetClient bc, AppClock clock, AuditTrail audit, ILogger<BcNumberService> logger)
{
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    /// <summary>Adds the request to the context (saved with the new asset).</summary>
    public void Enqueue(Asset asset)
    {
        var now = clock.UtcNow;
        db.BcNumberRequests.Add(new BcNumberRequest { AssetId = asset.Id, Status = "queued", NextAttemptAt = now, CreatedAt = now, UpdatedAt = now });
    }

    /// <summary>Tries every request that is due; returns how many numbers were issued.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct, int? onlyAssetId = null)
    {
        var now = clock.UtcNow;
        var due = await db.BcNumberRequests
            .Where(r => r.Status == "queued" && r.NextAttemptAt <= now && (onlyAssetId == null || r.AssetId == onlyAssetId))
            .OrderBy(r => r.Id)
            .Take(50)
            .ToListAsync(ct);
        var issued = 0;
        foreach (var request in due)
        {
            if (await TryIssueAsync(request, ct)) issued++;
        }
        return issued;
    }

    private async Task<bool> TryIssueAsync(BcNumberRequest request, CancellationToken ct)
    {
        var asset = await db.Assets.IgnoreQueryFilters().Include(a => a.Sbu).Include(a => a.AssetCategory).FirstAsync(a => a.Id == request.AssetId, ct);
        var now = clock.UtcNow;
        if (asset.AssetNumber is not null)
        {
            request.Status = "done";
            request.AssetNumber = asset.AssetNumber;
            request.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return false;
        }

        request.Attempts++;
        request.UpdatedAt = now;
        string number;
        try
        {
            var year = Depreciation.Period.OfDate(asset.PurchaseDate)?.Year ?? asset.CreatedAt.Year;
            // IT assets share the "IT" class, as in the mockups (BAK-IT-…); other categories use their own code (AGRI-MV-…).
            var classCode = asset.AssetCategory.IsIt ? "IT" : asset.AssetCategory.Code;
            number = await bc.CreateFixedAssetAsync(new BcFixedAssetRequest(asset.Id, asset.Sbu?.Code ?? "GRP", classCode, year, asset.Name), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var backoff = TimeSpan.FromMinutes(Math.Min(MaxBackoff.TotalMinutes, Math.Pow(2, request.Attempts - 1)));
            request.NextAttemptAt = now + backoff;
            request.LastError = ex.Message;
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Business Central number request for asset {AssetId} failed (attempt {Attempt}): {Error}", asset.Id, request.Attempts, ex.Message);
            return false;
        }

        asset.AssetNumber = number;
        if (asset.Status == AssetStatuses.PendingAssetNumber) asset.Status = AssetStatuses.Active;
        asset.UpdatedAt = now;
        request.Status = "done";
        request.AssetNumber = number;
        request.LastError = null;
        audit.Record(null, "asset", asset.Id, asset.Id, "asset_number_issued", "assetNumber", null, number, "Issued by Business Central");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // The number clashes with one already on the register: never overwrite, keep the request queued.
            db.ChangeTracker.Clear();
            var error = $"Business Central returned {number}, which is already used: {ex.InnerException?.Message}";
            await db.BcNumberRequests.Where(r => r.Id == request.Id).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Attempts, request.Attempts)
                .SetProperty(r => r.NextAttemptAt, now.AddMinutes(1))
                .SetProperty(r => r.LastError, error)
                .SetProperty(r => r.UpdatedAt, now), ct);
            logger.LogError(ex, "Business Central number {Number} for asset {AssetId} is already used", number, asset.Id);
            return false;
        }
        return true;
    }
}

/// <summary>Retries queued Business Central number requests every 30 seconds (not in the Testing environment).</summary>
public sealed class BcNumberWorker(IServiceScopeFactory scopes, ILogger<BcNumberWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<BcNumberService>().ProcessDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Processing the Business Central number queue failed");
            }
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { }
        }
    }
}
