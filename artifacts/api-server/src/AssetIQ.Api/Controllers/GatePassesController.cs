using Microsoft.EntityFrameworkCore;
using AssetIQ.Api.Data;
using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetIQ.Api.Controllers;

/// <summary>Gate passes. Lists show the passes of assets the caller may see; decisions follow the permissions matrix.</summary>
public sealed class GatePassesController(GatePassService gatePasses, AssetIqDbContext db, AssetAccess access) : ApiControllerBase
{
    [HttpGet("gate-passes")]
    [RequireRole(Roles.GatePassReaders)]
    [FailureMessage("Failed to list gate passes")]
    public Task<List<GatePassDto>> List([FromQuery] string? status, [FromQuery] string? sbuId) =>
        gatePasses.ListAsync(RequestValues.OptionalChoice(status, GatePassStatuses.All), RequestValues.OptionalInt(sbuId), Aborted);

    /// <summary>The "Request gate pass" form.</summary>
    [HttpPost("gate-passes")]
    [RequireRole(Roles.GatePassRaisers)]
    [FailureMessage("Failed to create gate pass")]
    public async Task<IActionResult> Create([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var request = new GatePassRequest(
            input.RequiredInt("assetId"),
            input.RequiredString("requestedBy", minLength: 1),
            input.RequiredString("reason", minLength: 1),
            input.OptionalString("destination"),
            input.OptionalDate("returnDate"));
        input.ThrowIfInvalid();
        await gatePasses.AuthorizeRaiseAsync(Caller, request.AssetId, Aborted);
        return Status201(await gatePasses.CreateRequestAsync(request, Aborted));
    }

    [HttpGet("gate-passes/{id}")]
    [RequireRole(Roles.GatePassReaders)]
    [FailureMessage("Failed to get gate pass")]
    public async Task<GatePassDto> Get(string id)
    {
        var passId = RequestValues.Id(id);
        var assetId = await db.GatePasses.IgnoreQueryFilters().Where(p => p.Id == passId).Select(p => (int?)p.AssetId).FirstOrDefaultAsync(Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Gate pass not found");
        // The Gate Officer checks passes of their own SBU's assets at the gate (SG-01); others as for the asset.
        if (Caller.Role == Roles.GateOfficer)
        {
            var sbu = await db.Assets.IgnoreQueryFilters().Where(a => a.Id == assetId).Select(a => a.SbuId).FirstAsync(Aborted);
            if (!AccessPolicy.OwnSbu(Caller, sbu)) throw AccessPolicy.Deny(Caller, "check another SBU's gate passes");
        }
        else
        {
            await access.EnsureVisibleAsync(Caller, assetId, Aborted);
        }
        return await gatePasses.GetAsync(passId, Aborted);
    }

    /// <summary>Records the gate_pass exit when the asset is waiting at a gate, and resolves the exit-attempt alert.</summary>
    [HttpPost("gate-passes/{id}/approve")]
    [RequireRole(Roles.GatePassApprovers)]
    [FailureMessage("Failed to approve gate pass")]
    public async Task<GatePassDto> Approve(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var passId = RequestValues.Id(id);
        var decision = ReadDecision(body);
        await gatePasses.AuthorizeDecisionAsync(Caller, passId, Aborted);
        return await gatePasses.ApproveAsync(passId, decision, Aborted);
    }

    /// <summary>Flags the asset: an unauthorised-movement alert plus an audit note in its history.</summary>
    [HttpPost("gate-passes/{id}/reject")]
    [RequireRole(Roles.GatePassApprovers)]
    [FailureMessage("Failed to reject gate pass")]
    public async Task<GatePassDto> Reject(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var passId = RequestValues.Id(id);
        var decision = ReadDecision(body);
        await gatePasses.AuthorizeDecisionAsync(Caller, passId, Aborted);
        return await gatePasses.RejectAsync(passId, decision, Aborted);
    }

    /// <summary>The decision is recorded against the signed-in approver; a typed "decidedBy" is no longer trusted.</summary>
    // ASSUMPTION: "decidedBy" stays accepted (older dashboard builds send it) but the signed-in user's name is recorded.
    private GatePassDecision ReadDecision(JsonElement? body)
    {
        var input = JsonBody.From(body);
        input.OptionalString("decidedBy");
        var decision = new GatePassDecision(Caller.Name, input.OptionalString("note"));
        input.ThrowIfInvalid();
        return decision;
    }
}
