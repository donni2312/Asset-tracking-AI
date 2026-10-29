using System.Security.Cryptography;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Controllers;

/// <summary>
/// Invoices, photos and documents on an asset (AU-01 photo, AU-04 / SF-01 invoice, AU-07 upgrade invoice),
/// shown and downloaded on the asset card. Stored in the database (product owner, 29 Oct 2026) and never
/// deleted: they are audit evidence (a trigger refuses changes). Files are checked by their content, not by
/// the name or the type the browser claims.
/// </summary>
public sealed class AttachmentsController(AssetIqDbContext db, AppClock clock, AssetAccess access, AuditTrail audit, AccessLog accessLog) : ApiControllerBase
{
    public const int MaxBytes = 10 * 1024 * 1024;

    [HttpGet("assets/{id}/attachments")]
    [RequireRole(Roles.RecordReaders)]
    [FailureMessage("Failed to list attachments")]
    public async Task<List<AttachmentDto>> List(string id)
    {
        var assetId = RequestValues.Id(id);
        await access.EnsureVisibleAsync(Caller, assetId, Aborted);
        return await db.AssetAttachments.AsNoTracking().Where(a => a.AssetId == assetId).OrderByDescending(a => a.UploadedAt).ThenByDescending(a => a.Id)
            .Select(a => new AttachmentDto
            {
                Id = a.Id, AssetId = a.AssetId, UpgradeId = a.UpgradeId, Kind = a.Kind, FileName = a.FileName, ContentType = a.ContentType,
                SizeBytes = a.SizeBytes, UploadedBy = a.UploadedBy, UploadedAt = a.UploadedAt,
            })
            .ToListAsync(Aborted);
    }

    /// <summary>multipart/form-data: <c>file</c> (PDF, JPEG or PNG, up to 10 MB), <c>kind</c> (invoice | photo | other), optional <c>upgradeId</c>.</summary>
    [HttpPost("assets/{id}/attachments")]
    [RequireRole(Roles.Attachers)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxBytes + 64 * 1024)]
    [FailureMessage("Failed to upload the attachment")]
    public async Task<IActionResult> Upload(string id, IFormFile? file, [FromForm] string? kind, [FromForm] string? upgradeId)
    {
        var assetId = RequestValues.Id(id);
        var asset = await access.LoadAsync(Caller, assetId, Aborted);
        if (!AccessPolicy.CanAttach(Caller, asset, asset.AssetCategory.IsIt)) throw AccessPolicy.Deny(Caller, "add attachments to this asset");
        var attachmentKind = RequestValues.OptionalChoice(kind, AttachmentKinds.All) ?? throw new ApiException(StatusCodes.Status400BadRequest, "kind: invoice, photo or other");
        var upgrade = RequestValues.OptionalInt(upgradeId);
        if (file is null || file.Length == 0) throw new ApiException(StatusCodes.Status400BadRequest, "file: Required");
        if (file.Length > MaxBytes) throw new ApiException(StatusCodes.Status400BadRequest, "file: at most 10 MB");
        if (upgrade is not null && !await db.AssetUpgrades.IgnoreQueryFilters().AnyAsync(u => u.Id == upgrade && u.AssetId == assetId, Aborted))
            throw new ApiException(StatusCodes.Status400BadRequest, "upgradeId: not an upgrade of this asset");

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, Aborted);
        var content = buffer.ToArray();
        var contentType = Sniff(content) ?? throw new ApiException(StatusCodes.Status400BadRequest, "file: only PDF, JPEG and PNG files can be attached");
        if (attachmentKind == AttachmentKinds.Photo && contentType == "application/pdf") throw new ApiException(StatusCodes.Status400BadRequest, "file: a photo must be a JPEG or PNG image");

        var attachment = new AssetAttachment
        {
            AssetId = assetId, UpgradeId = upgrade, Kind = attachmentKind, FileName = SafeName(file.FileName, contentType), ContentType = contentType,
            SizeBytes = content.Length, Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), Content = content,
            UploadedBy = Caller.Name, UploadedByRole = Caller.Role, UploadedAt = clock.UtcNow,
        };
        db.AssetAttachments.Add(attachment);
        await db.SaveChangesAsync(Aborted);
        audit.Record(Caller, "attachment", attachment.Id, assetId, "attachment_added", attachmentKind, null, attachment.FileName,
            upgrade is null ? null : $"Upgrade #{upgrade}");
        await db.SaveChangesAsync(Aborted);
        return Status201(new AttachmentDto
        {
            Id = attachment.Id, AssetId = assetId, UpgradeId = upgrade, Kind = attachmentKind, FileName = attachment.FileName, ContentType = contentType,
            SizeBytes = attachment.SizeBytes, UploadedBy = attachment.UploadedBy, UploadedAt = attachment.UploadedAt,
        });
    }

    /// <summary>The file; <c>?inline=true</c> to display it (photo, PDF preview) instead of downloading it.</summary>
    [HttpGet("attachments/{id}")]
    [RequireRole(Roles.RecordReaders)]
    [FailureMessage("Failed to load the attachment")]
    public async Task<IActionResult> Download(string id, [FromQuery] string? inline)
    {
        var attachmentId = RequestValues.Id(id);
        var assetId = await db.AssetAttachments.IgnoreQueryFilters().Where(a => a.Id == attachmentId).Select(a => (int?)a.AssetId).FirstOrDefaultAsync(Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Attachment not found");
        await access.EnsureVisibleAsync(Caller, assetId, Aborted);
        var file = await db.AssetAttachments.AsNoTracking().FirstAsync(a => a.Id == attachmentId, Aborted);
        await accessLog.ExportAsync(Caller, $"attachment {file.FileName} (asset {assetId})", Request, Aborted);
        if (string.Equals(inline, "true", StringComparison.OrdinalIgnoreCase))
        {
            Response.Headers.ContentDisposition = $"inline; filename=\"{file.FileName}\"";
            return File(file.Content, file.ContentType);
        }
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>PDF, JPEG or PNG by their first bytes; null for anything else.</summary>
    public static string? Sniff(byte[] content) => content switch
    {
        [0x25, 0x50, 0x44, 0x46, 0x2D, ..] => "application/pdf",             // %PDF-
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        _ => null,
    };

    /// <summary>The uploaded name without folders or characters that break a header, with the right extension.</summary>
    public static string SafeName(string? name, string contentType)
    {
        var baseName = Path.GetFileNameWithoutExtension(Path.GetFileName((name ?? "").Replace('\\', '/').Split('/').Last()));
        var cleaned = new string(baseName.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' or '.').ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "attachment";
        if (cleaned.Length > 100) cleaned = cleaned[..100];
        var extension = contentType switch { "application/pdf" => ".pdf", "image/jpeg" => ".jpg", _ => ".png" };
        return cleaned + extension;
    }
}
