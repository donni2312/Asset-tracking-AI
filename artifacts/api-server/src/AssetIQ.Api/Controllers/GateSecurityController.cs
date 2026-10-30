using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetIQ.Api.Controllers;

/// <summary>Phase 5b: lost / stolen checklist (GI-07), shift view (SG-04), visitor equipment (SG-05), incidents (SG-06).</summary>
public sealed class GateSecurityController(GateSecurityService security) : ApiControllerBase
{
    private const long MaxPhotoBytes = 5 * 1024 * 1024;

    [HttpGet("assets/{id}/security-actions")]
    [RequireRole(Roles.RecordReaders)]
    [FailureMessage("Failed to load the security checklist")]
    public Task<SecurityChecklistDto> Checklist(string id) => security.ChecklistAsync(Caller, RequestValues.Id(id), Aborted);

    /// <summary>GI-07: body { action: remote_wipe | account_disable | police_report, reference? (police case number), notes? }.</summary>
    [HttpPost("assets/{id}/security-actions")]
    [RequireRole(Roles.SecurityRecorders)]
    [FailureMessage("Failed to record the security action")]
    public async Task<IActionResult> Record(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var action = input.RequiredEnum("action", GateSecurityService.SecurityActionKinds);
        var reference = input.OptionalString("reference");
        var notes = input.OptionalString("notes");
        input.ThrowIfInvalid();
        var assetId = RequestValues.Id(id);
        await security.RecordSecurityActionAsync(Caller, assetId, action, reference, notes, Aborted);
        return Status201(await security.ChecklistAsync(Caller, assetId, Aborted));
    }

    [HttpGet("gate/shift")]
    [RequireRole(Roles.GateOfficers)]
    [FailureMessage("Failed to load the shift view")]
    public Task<GateShiftDto> Shift() => security.ShiftAsync(Caller, Aborted);

    [HttpGet("gate/visitor-items")]
    [RequireRole(Roles.GateOfficers)]
    [FailureMessage("Failed to list visitor equipment")]
    public Task<List<VisitorItemDto>> VisitorItems([FromQuery] string? all) => security.VisitorItemsAsync(Caller, all != "true", Aborted);

    /// <summary>SG-05: multipart { gateLocationId, ownerName, ownerCompany?, item, serialNumber?, photo? (JPEG / PNG) }.</summary>
    [HttpPost("gate/visitor-items")]
    [RequireRole(Roles.GateOfficers)]
    [Consumes("multipart/form-data")]
    [FailureMessage("Failed to log the visitor's item")]
    public async Task<IActionResult> CheckIn([FromForm] string? gateLocationId, [FromForm] string? ownerName, [FromForm] string? ownerCompany,
        [FromForm] string? item, [FromForm] string? serialNumber, IFormFile? photo)
    {
        var gate = RequestValues.OptionalInt(gateLocationId) ?? throw new ApiException(StatusCodes.Status400BadRequest, "gateLocationId: Required");
        if (string.IsNullOrWhiteSpace(ownerName)) throw new ApiException(StatusCodes.Status400BadRequest, "ownerName: Required");
        if (string.IsNullOrWhiteSpace(item)) throw new ApiException(StatusCodes.Status400BadRequest, "item: Required");
        var row = await security.CheckInAsync(Caller, gate, ownerName.Trim(), Blank(ownerCompany), item.Trim(), Blank(serialNumber), await PhotoAsync(photo), Aborted);
        return Status201((await security.VisitorItemsAsync(Caller, true, Aborted)).Single(v => v.Id == row.Id));
    }

    /// <summary>SG-05: body { gateLocationId }; the item leaves without a gate pass.</summary>
    [HttpPost("gate/visitor-items/{id}/check-out")]
    [RequireRole(Roles.GateOfficers)]
    [FailureMessage("Failed to check the item out")]
    public async Task<List<VisitorItemDto>> CheckOut(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var gate = input.RequiredInt("gateLocationId");
        input.ThrowIfInvalid();
        await security.CheckOutAsync(Caller, RequestValues.Id(id), gate, Aborted);
        return await security.VisitorItemsAsync(Caller, true, Aborted);
    }

    [HttpGet("gate/incidents")]
    [RequireRole(Roles.IncidentReaders)]
    [FailureMessage("Failed to list incidents")]
    public Task<List<GateIncidentDto>> Incidents() => security.IncidentsAsync(Caller, Aborted);

    /// <summary>SG-06: multipart { kind: forged_pass | unknown_tag | refused_search | other, gateLocationId?, code?, gatePassId?, notes, photo? }.</summary>
    [HttpPost("gate/incidents")]
    [RequireRole(Roles.GateOfficers)]
    [Consumes("multipart/form-data")]
    [FailureMessage("Failed to escalate the incident")]
    public async Task<IActionResult> Escalate([FromForm] string? kind, [FromForm] string? gateLocationId, [FromForm] string? code, [FromForm] string? gatePassId,
        [FromForm] string? notes, IFormFile? photo)
    {
        var k = RequestValues.OptionalChoice(kind, GateSecurityService.IncidentKinds)
            ?? throw new ApiException(StatusCodes.Status400BadRequest, $"kind: one of {string.Join(", ", GateSecurityService.IncidentKinds)}");
        if (string.IsNullOrWhiteSpace(notes) || notes.Trim().Length < 3) throw new ApiException(StatusCodes.Status400BadRequest, "notes: what happened");
        var row = await security.EscalateAsync(Caller, k, RequestValues.OptionalInt(gateLocationId), Blank(code), RequestValues.OptionalInt(gatePassId), notes, await PhotoAsync(photo), Aborted);
        return Status201((await security.IncidentsAsync(Caller, Aborted)).Single(i => i.Id == row.Id));
    }

    [HttpGet("gate/incidents/{id}/photo")]
    [RequireRole(Roles.IncidentReaders)]
    [FailureMessage("Failed to load the photo")]
    public async Task<IActionResult> IncidentPhoto(string id)
    {
        var photo = await security.PhotoAsync(Caller, "incident", RequestValues.Id(id), Aborted);
        return File(photo.Content, photo.ContentType);
    }

    [HttpGet("gate/visitor-items/{id}/photo")]
    [RequireRole(Roles.GateOfficers)]
    [FailureMessage("Failed to load the photo")]
    public async Task<IActionResult> VisitorPhoto(string id)
    {
        var photo = await security.PhotoAsync(Caller, "visitor", RequestValues.Id(id), Aborted);
        return File(photo.Content, photo.ContentType);
    }

    private static async Task<UploadedPhoto?> PhotoAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0) return null;
        if (file.Length > MaxPhotoBytes) throw new ApiException(StatusCodes.Status400BadRequest, "photo: at most 5 MB");
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer);
        var content = buffer.ToArray();
        var type = AttachmentsController.Sniff(content);
        if (type is not ("image/jpeg" or "image/png")) throw new ApiException(StatusCodes.Status400BadRequest, "photo: a JPEG or PNG image");
        return new UploadedPhoto(content, type);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
