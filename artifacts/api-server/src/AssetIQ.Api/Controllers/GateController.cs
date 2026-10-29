using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetIQ.Api.Controllers;

/// <summary>Phase 4c: the Gate Officer's screen (SG-01 … SG-03, GI-06) and verification counts (SI-07, SF-06).</summary>
public sealed class GateController(GateService gate, VerificationService verification) : ApiControllerBase
{
    // ── gate ────────────────────────────────────────────────────────────────

    /// <summary>SG-01: scan of a gate pass QR (GP-12) or an item's tag / number.</summary>
    [HttpGet("gate/scan")]
    [RequireRole(Roles.GateOfficers)]
    [FailureMessage("Failed to check the scan")]
    public Task<GateScanDto> Scan([FromQuery] string? code) => gate.ScanAsync(Caller, code, Aborted);

    /// <summary>SG-03: body { gateLocationId, assetIds?: [...] (default: every item not yet out), notes? }.</summary>
    [HttpPost("gate-passes/{id}/exit")]
    [RequireRole(Roles.GateOfficers)]
    [FailureMessage("Failed to record the exit")]
    public async Task<GatePassDto> Exit(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var gateId = input.RequiredInt("gateLocationId");
        var notes = input.OptionalString("notes");
        input.ThrowIfInvalid();
        return await gate.RecordExitAsync(Caller, RequestValues.Id(id), gateId, RequestValues.IntList(body, "assetIds"), notes, Aborted);
    }

    /// <summary>SG-03 / SI-04: body { gateLocationId, items?: [{ assetId, condition?, notes? }] (default: every item out) }.</summary>
    [HttpPost("gate-passes/{id}/return")]
    [RequireRole(Roles.GateOfficers)]
    [FailureMessage("Failed to record the return")]
    public async Task<GatePassDto> Return(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var gateId = input.RequiredInt("gateLocationId");
        input.ThrowIfInvalid();
        var items = new List<GateReturnInput>();
        if (body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in arr.EnumerateArray())
            {
                var item = JsonBody.From(x);
                items.Add(new GateReturnInput(item.RequiredInt("assetId"), item.OptionalEnum("condition", AssetConditions.All), item.OptionalString("notes")));
                item.ThrowIfInvalid();
            }
        }
        return await gate.RecordReturnAsync(Caller, RequestValues.Id(id), gateId, items, Aborted);
    }

    [HttpGet("gate/alerts")]
    [RequireRole(Roles.GateOfficers)]
    [FailureMessage("Failed to list the gate alerts")]
    public Task<List<GateAlertDto>> Alerts() => gate.AlertsAsync(Caller, Aborted);

    /// <summary>SG-02: body { action: stopped | released_with_override | escalated, notes (required unless stopped), gateLocationId? }.</summary>
    [HttpPost("gate/alerts/{insightId}/actions")]
    [RequireRole(Roles.GateOfficers)]
    [FailureMessage("Failed to record the action")]
    public async Task<IActionResult> Act(string insightId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var action = input.RequiredEnum("action", GateActions.All);
        var notes = input.OptionalString("notes");
        var gateId = input.OptionalInt("gateLocationId");
        input.ThrowIfInvalid();
        return Status201(await gate.ActAsync(Caller, RequestValues.Id(insightId), action, notes, gateId, Aborted));
    }

    [HttpGet("gate/overdue")]
    [RequireRole(Roles.GateOfficers)]
    [FailureMessage("Failed to list overdue returns")]
    public Task<List<GateOverdueDto>> Overdue() => gate.OverdueAsync(Caller, Aborted);

    // ── verification ────────────────────────────────────────────────────────

    [HttpGet("verification-sessions")]
    [RequireRole(Roles.VerificationReaders)]
    [FailureMessage("Failed to list the counts")]
    public Task<List<VerificationSessionDto>> Sessions() => verification.ListAsync(Caller, Aborted);

    /// <summary>SI-07: body { locationId, scope?: it | non_it | all (default: what the caller counts) }.</summary>
    [HttpPost("verification-sessions")]
    [RequireRole(Roles.VerificationRunners)]
    [FailureMessage("Failed to start the count")]
    public async Task<IActionResult> Start([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var locationId = input.RequiredInt("locationId");
        var scope = input.OptionalEnum("scope", VerificationScopes.Values);
        input.ThrowIfInvalid();
        var session = await verification.StartAsync(Caller, locationId, scope, Aborted);
        return Status201(await verification.GetAsync(Caller, session.Id, Aborted));
    }

    [HttpGet("verification-sessions/{id}")]
    [RequireRole(Roles.VerificationReaders)]
    [FailureMessage("Failed to load the count")]
    public Task<VerificationSessionDto> Session(string id) => verification.GetAsync(Caller, RequestValues.Id(id), Aborted);

    /// <summary>Body { codes: [...] } (tags, asset numbers or serials).</summary>
    [HttpPost("verification-sessions/{id}/scans")]
    [RequireRole(Roles.VerificationRunners)]
    [FailureMessage("Failed to record the scans")]
    public async Task<VerificationSessionDto> Scans(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var codes = RequestValues.StringList(body, "codes") ?? throw new ApiException(StatusCodes.Status400BadRequest, "codes: Required");
        var sessionId = RequestValues.Id(id);
        await verification.ScanAsync(Caller, sessionId, codes, Aborted);
        return await verification.GetAsync(Caller, sessionId, Aborted);
    }

    [HttpPost("verification-sessions/{id}/close")]
    [RequireRole(Roles.VerificationRunners)]
    [FailureMessage("Failed to close the count")]
    public async Task<VerificationSessionDto> Close(string id)
    {
        var sessionId = RequestValues.Id(id);
        await verification.CloseAsync(Caller, sessionId, Aborted);
        return await verification.GetAsync(Caller, sessionId, Aborted);
    }

    /// <summary>SF-06: body { resolution: adjust | write_off | investigate | register, comment }.</summary>
    [HttpPost("verification-sessions/{id}/results/{resultId}/resolve")]
    [RequireRole(Roles.FinanceWriters)]
    [FailureMessage("Failed to resolve the difference")]
    public async Task<VerificationSessionDto> Resolve(string id, string resultId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var resolution = input.RequiredEnum("resolution", VerificationResolutions.All);
        var comment = input.RequiredString("comment", minLength: 3);
        input.ThrowIfInvalid();
        var sessionId = RequestValues.Id(id);
        await verification.ResolveAsync(Caller, sessionId, RequestValues.Id(resultId), resolution, comment, Aborted);
        return await verification.GetAsync(Caller, sessionId, Aborted);
    }
}
