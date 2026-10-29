using AssetIQ.Api.Auth;
using AssetIQ.Api.Data;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

/// <summary>
/// Loads one asset for a caller, telling "no such asset" (404) apart from "not yours" (403): the global
/// query filter alone would make another SBU's asset look missing, and the brief asks for a 403.
/// </summary>
public sealed class AssetAccess(AssetIqDbContext db)
{
    /// <summary>The asset (tracked when <paramref name="track"/>), with its category; 404 / 403 otherwise.</summary>
    public async Task<Asset> LoadAsync(AuthUser user, int assetId, CancellationToken ct, bool track = false)
    {
        var query = db.Assets.IgnoreQueryFilters().Include(a => a.AssetCategory).Where(a => a.Id == assetId);
        var asset = await (track ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Asset not found");
        if (!AccessPolicy.CanView(user, asset, asset.AssetCategory.IsIt)) throw AccessPolicy.Deny(user, "view this asset");
        return asset;
    }

    /// <summary>404 / 403 for the asset behind a gate pass, movement or other row the caller cannot see.</summary>
    public Task EnsureVisibleAsync(AuthUser user, int assetId, CancellationToken ct) => LoadAsync(user, assetId, ct);
}
