using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetIQ.Api.Controllers;

public sealed class GatePassesController(GatePassService gatePasses) : ApiControllerBase
{
    [HttpGet("gate-passes")]
    [FailureMessage("Failed to list gate passes")]
    public Task<List<GatePassDto>> List([FromQuery] string? status, [FromQuery] string? sbuId) =>
        gatePasses.ListAsync(RequestValues.OptionalChoice(status, GatePassStatuses.All), RequestValues.OptionalInt(sbuId), Aborted);

    /// <summary>The "Request gate pass" form.</summary>
    [HttpPost("gate-passes")]
    [RequireRole(Roles.Operator)]
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
        return Status201(await gatePasses.CreateRequestAsync(request, Aborted));
    }

    [HttpGet("gate-passes/{id}")]
    [FailureMessage("Failed to get gate pass")]
    public Task<GatePassDto> Get(string id) => gatePasses.GetAsync(RequestValues.Id(id), Aborted);

    /// <summary>Records the gate_pass exit when the asset is waiting at a gate, and resolves the exit-attempt alert.</summary>
    [HttpPost("gate-passes/{id}/approve")]
    [RequireRole(Roles.Operator)]
    [FailureMessage("Failed to approve gate pass")]
    public Task<GatePassDto> Approve(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var passId = RequestValues.Id(id);
        return gatePasses.ApproveAsync(passId, ReadDecision(body), Aborted);
    }

    /// <summary>Flags the asset: an unauthorised-movement alert plus an audit note in its history.</summary>
    [HttpPost("gate-passes/{id}/reject")]
    [RequireRole(Roles.Operator)]
    [FailureMessage("Failed to reject gate pass")]
    public Task<GatePassDto> Reject(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var passId = RequestValues.Id(id);
        return gatePasses.RejectAsync(passId, ReadDecision(body), Aborted);
    }

    private static GatePassDecision ReadDecision(JsonElement? body)
    {
        var input = JsonBody.From(body);
        var decision = new GatePassDecision(input.RequiredString("decidedBy", minLength: 1), input.OptionalString("note"));
        input.ThrowIfInvalid();
        return decision;
    }
}
