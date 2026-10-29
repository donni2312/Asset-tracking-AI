using AssetIQ.Api.Auth;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Controllers;

/// <summary>
/// A scan of a tag, asset number or serial (SG-01, SI-03). This is the only way the Gate Officer sees an asset
/// (matrix "View assets: on scan only"). Anyone who may scan gets the basic identification; the holder, the
/// photo and the gate pass only for an asset they may see, or at their own SBU's gate.
/// </summary>
public sealed class ScanController(AssetIqDbContext db, AppClock clock, AppConfig config) : ApiControllerBase
{
    [HttpGet("scan")]
    [RequireRole(Roles.Scanners)]
    [FailureMessage("Failed to look up the scan")]
    public async Task<ScanResultDto> Scan([FromQuery] string? code)
    {
        var text = (code ?? "").Trim().ToUpperInvariant();
        if (text.Length == 0) throw new ApiException(StatusCodes.Status400BadRequest, "code: Required");
        var asset = await db.Assets.IgnoreQueryFilters().AsNoTracking().Include(a => a.AssetCategory).Include(a => a.Sbu)
            .Where(a => a.AssetNumber!.ToUpper() == text || a.RfidTag!.ToUpper() == text || a.PreviousAssetNumber!.ToUpper() == text || a.SerialNumber == text)
            .FirstOrDefaultAsync(Aborted) ?? throw new ApiException(StatusCodes.Status404NotFound, $"No asset has the number, tag or serial {code!.Trim()}");

        var visible = AccessPolicy.CanView(Caller, asset, asset.AssetCategory.IsIt);
        var atOwnGate = Caller.Role == Roles.GateOfficer && AccessPolicy.OwnSbu(Caller, asset.SbuId);
        var full = visible || atOwnGate;
        var basic = new ScanResultDto
        {
            AssetId = asset.Id,
            CanOpen = visible && Roles.Parse(Roles.RecordReaders).Contains(Caller.Role),
            Description = asset.Name, AssetNumber = asset.AssetNumber, RfidTag = asset.RfidTag, SbuCode = asset.Sbu?.Code, SbuName = asset.Sbu?.Name,
            Status = asset.Status, GateClearance = "not_shown",
        };
        if (!full) return basic;

        var passes = await db.GatePasses.IgnoreQueryFilters().AsNoTracking().Where(p => p.AssetId == asset.Id).OrderByDescending(p => p.CreatedAt).ToListAsync(Aborted);
        var valid = GatePassRules.FindValidApprovedPass(passes.Where(p => p.Status == GatePassStatuses.Approved), clock.UtcNow, config.TimeZone);
        var shown = valid ?? passes.FirstOrDefault();
        var photo = await db.AssetAttachments.IgnoreQueryFilters().Where(a => a.AssetId == asset.Id && a.Kind == AttachmentKinds.Photo)
            .OrderByDescending(a => a.UploadedAt).Select(a => (int?)a.Id).FirstOrDefaultAsync(Aborted);
        return basic with
        {
            Holder = asset.AssignedTo,
            PhotoAttachmentId = photo,
            GateClearance = valid is null ? "not_approved" : "approved",
            GatePassId = shown?.Id,
            GatePassStatus = shown?.Status,
            GatePassReturnDate = shown?.ReturnDate,
        };
    }
}
