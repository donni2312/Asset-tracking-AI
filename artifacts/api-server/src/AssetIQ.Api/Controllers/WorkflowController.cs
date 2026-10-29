using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using AssetIQ.Api.Services.Approvals;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Controllers;

/// <summary>
/// Phase 4 (slice 4a): repairs, disposals and transfers are requested here and decided in the one approvals inbox;
/// approvers on leave delegate (LM-03); everyone has an in-app notification list.
/// </summary>
public sealed class WorkflowController(AssetIqDbContext db, AppClock clock, AssetAccess access, WorkflowService workflows, ApprovalService approvals, AuditTrail audit)
    : ApiControllerBase
{
    // ── repairs (SI-05) ─────────────────────────────────────────────────────

    [HttpPost("assets/{id}/repairs")]
    [RequireRole(Roles.TechnicalEditors)]
    [FailureMessage("Failed to request the repair")]
    public async Task<IActionResult> RequestRepair(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var request = new RepairRequestInput(input.RequiredString("fault", minLength: 3), input.OptionalString("action"), input.OptionalString("parts"), input.OptionalString("vendor"));
        input.ThrowIfInvalid();
        return Status201(ToDto(await workflows.RequestRepairAsync(Caller, RequestValues.Id(id), request, Aborted)));
    }

    [HttpGet("assets/{id}/repairs")]
    [RequireRole(Roles.RecordReaders)]
    [FailureMessage("Failed to list repairs")]
    public async Task<List<RepairDto>> Repairs(string id)
    {
        var assetId = RequestValues.Id(id);
        await access.EnsureVisibleAsync(Caller, assetId, Aborted);
        return (await db.AssetRepairs.AsNoTracking().Where(r => r.AssetId == assetId).OrderByDescending(r => r.Id).ToListAsync(Aborted)).Select(ToDto).ToList();
    }

    /// <summary>Finance records the cost; IT the work, parts, vendor and downtime.</summary>
    [HttpPatch("repairs/{id}")]
    [RequireRole(Roles.RepairEditors)]
    [FailureMessage("Failed to update the repair")]
    public async Task<RepairDto> UpdateRepair(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var cost = input.NullableNumber("cost");
        var action = input.OptionalString("action");
        var parts = input.OptionalString("parts");
        var vendor = input.OptionalString("vendor");
        var downtime = input.OptionalInt("downtimeDays");
        input.ThrowIfInvalid();
        return ToDto(await workflows.UpdateRepairAsync(Caller, RequestValues.Id(id), cost.Value, cost.IsSet, action, parts, vendor, downtime, Aborted));
    }

    // ── disposals (SI-08, SF-04, GF-04, GI-10) ──────────────────────────────

    [HttpPost("assets/{id}/disposals")]
    [RequireRole(Roles.DisposalRecommenders)]
    [FailureMessage("Failed to recommend the disposal")]
    public async Task<IActionResult> RecommendDisposal(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var request = new DisposalRequestInput(input.RequiredString("reason", minLength: 3), input.OptionalString("condition"));
        input.ThrowIfInvalid();
        return Status201(ToDto(await workflows.RecommendDisposalAsync(Caller, RequestValues.Id(id), request, Aborted)));
    }

    [HttpGet("assets/{id}/disposals")]
    [RequireRole(Roles.RecordReaders)]
    [FailureMessage("Failed to list disposals")]
    public async Task<List<DisposalDto>> Disposals(string id)
    {
        var assetId = RequestValues.Id(id);
        await access.EnsureVisibleAsync(Caller, assetId, Aborted);
        return (await db.AssetDisposals.AsNoTracking().Where(d => d.AssetId == assetId).OrderByDescending(d => d.Id).ToListAsync(Aborted)).Select(ToDto).ToList();
    }

    // ── transfers (GI-05, SI-10, AM-08) ─────────────────────────────────────

    [HttpPost("assets/{id}/transfers")]
    [RequireRole(Roles.TechnicalEditors)]
    [FailureMessage("Failed to request the transfer")]
    public async Task<IActionResult> RequestTransfer(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var request = new TransferRequestInput(input.RequiredInt("toSbuId"), input.RequiredString("reason", minLength: 3));
        input.ThrowIfInvalid();
        return Status201(ToDto(await workflows.RequestTransferAsync(Caller, RequestValues.Id(id), request, Aborted)));
    }

    // ── approval requests ───────────────────────────────────────────────────

    [HttpGet("approval-requests/{id}")]
    [RequireRole(Roles.Everyone)]
    [FailureMessage("Failed to load the request")]
    public async Task<ApprovalRequestDto> GetRequest(string id)
    {
        var request = await approvals.FindAsync(RequestValues.Id(id), Aborted) ?? throw new ApiException(StatusCodes.Status404NotFound, "Approval request not found");
        var mayDecide = (await approvals.InboxAsync(Caller, Aborted)).Any(i => i.RequestId == request.Id);
        if (!mayDecide && request.RequestedByUserId != Caller.Id)
        {
            if (request.AssetId is { } assetId) await access.EnsureVisibleAsync(Caller, assetId, Aborted);
            else if (!AccessPolicy.Sys(Caller) && !(Roles.IsSbuScoped(Caller.Role) ? Caller.SbuId == request.SbuId : true)) throw AccessPolicy.Deny(Caller, "see this request");
        }
        return ToDto(request);
    }

    /// <summary>Approve or reject the caller's step, with a comment and whatever the step needs (disposal: method, proceeds …; data-wipe note).</summary>
    [HttpPost("approval-requests/{id}/decision")]
    [RequireRole(Roles.Everyone)]
    [FailureMessage("Failed to record the decision")]
    public async Task<ApprovalRequestDto> Decide(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var decision = input.RequiredEnum("decision", ["approve", "reject"]);
        var comment = input.OptionalString("comment");
        if (decision == "reject" && string.IsNullOrWhiteSpace(comment)) throw new ApiException(StatusCodes.Status400BadRequest, "comment: say why the request is rejected");
        var request = await approvals.FindAsync(RequestValues.Id(id), Aborted) ?? throw new ApiException(StatusCodes.Status404NotFound, "Approval request not found");
        if (request.Kind == ApprovalKinds.GatePass)
        {
            await HttpContext.RequestServices.GetRequiredService<GatePassService>().DecideAsync(Caller, request.SubjectId!.Value, decision == "approve", comment, input, Aborted);
        }
        else
        {
            await approvals.DecideAsync(Caller, request.Id, decision == "approve", comment, input, Aborted);
        }
        return ToDto((await approvals.FindAsync(request.Id, Aborted))!);
    }

    // ── delegation (LM-03) ──────────────────────────────────────────────────

    [HttpGet("delegations")]
    [RequireRole(Roles.Everyone)]
    [FailureMessage("Failed to list delegations")]
    public async Task<List<DelegationDto>> Delegations()
    {
        var me = Caller.Id;
        return await db.Delegations.AsNoTracking().Where(d => d.UserId == me || d.DelegateUserId == me).OrderByDescending(d => d.StartsAt)
            .Select(d => new DelegationDto(d.Id, d.UserId, db.Users.Where(u => u.Id == d.UserId).Select(u => u.Name).First(), d.DelegateUserId,
                db.Users.Where(u => u.Id == d.DelegateUserId).Select(u => u.Name).FirstOrDefault(), d.StartsAt, d.EndsAt, d.Reason))
            .ToListAsync(Aborted);
    }

    /// <summary>Who the caller may name as delegate: active colleagues of the same SBU (any SBU for Group roles).</summary>
    [HttpGet("delegations/candidates")]
    [RequireRole(Roles.Approvers)]
    [FailureMessage("Failed to list colleagues")]
    public Task<List<ColleagueDto>> Candidates()
    {
        var me = Caller;
        var query = db.Users.AsNoTracking().Where(u => u.Active && u.Id != me.Id && u.Role != Roles.Auditor);
        if (Roles.IsSbuScoped(me.Role)) query = query.Where(u => u.SbuId == me.SbuId);
        return query.OrderBy(u => u.Name).Select(u => new ColleagueDto(u.Id, u.Name, u.Role)).ToListAsync(Aborted);
    }

    /// <summary>LM-03: I am on leave from … to …; my approvals go to the delegate (or, with none, to whoever stands in).</summary>
    // ASSUMPTION: the delegate must be an active user of the same SBU (any SBU for Group roles), and not oneself.
    [HttpPost("delegations")]
    [RequireRole(Roles.Approvers)]
    [FailureMessage("Failed to record the leave")]
    public async Task<IActionResult> Delegate([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var delegateId = input.OptionalInt("delegateUserId");
        var starts = input.OptionalDate("startsAt") ?? clock.UtcNow;
        var ends = input.OptionalDate("endsAt");
        if (!input.Has("endsAt")) input.RequiredString("endsAt");
        var reason = input.OptionalString("reason");
        input.ThrowIfInvalid();
        if (ends is null || ends <= starts) throw new ApiException(StatusCodes.Status400BadRequest, "endsAt: must be after startsAt");
        if (delegateId is { } d)
        {
            if (d == Caller.Id) throw new ApiException(StatusCodes.Status400BadRequest, "delegateUserId: you cannot delegate to yourself");
            var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == d && u.Active, Aborted)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Delegate not found");
            if (Roles.IsSbuScoped(Caller.Role) && target.SbuId != Caller.SbuId)
                throw new ApiException(StatusCodes.Status400BadRequest, "delegateUserId: choose someone in your own SBU");
        }
        var delegation = new Delegation { UserId = Caller.Id, DelegateUserId = delegateId, StartsAt = starts, EndsAt = ends.Value, Reason = reason, CreatedAt = clock.UtcNow };
        db.Delegations.Add(delegation);
        await db.SaveChangesAsync(Aborted);
        audit.Record(Caller, "delegation", delegation.Id, null, "delegated", "delegateUserId", null, delegateId, $"{starts:yyyy-MM-dd} – {ends:yyyy-MM-dd} {reason}".Trim());
        await db.SaveChangesAsync(Aborted);
        return Status201((await Delegations()).First(x => x.Id == delegation.Id));
    }

    // ── notifications ───────────────────────────────────────────────────────

    [HttpGet("notifications")]
    [RequireRole(Roles.Everyone)]
    [FailureMessage("Failed to load notifications")]
    public Task<List<NotificationDto>> Notifications([FromQuery] string? unread)
    {
        var me = Caller.Id;
        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == me);
        if (string.Equals(unread, "true", StringComparison.OrdinalIgnoreCase)) query = query.Where(n => n.ReadAt == null);
        return query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(100)
            .Select(n => new NotificationDto(n.Id, n.Kind, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt)).ToListAsync(Aborted);
    }

    [HttpPost("notifications/read")]
    [RequireRole(Roles.Everyone)]
    [FailureMessage("Failed to mark notifications read")]
    public async Task<IActionResult> MarkRead([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var one = input.OptionalInt("id");
        input.ThrowIfInvalid();
        var me = Caller.Id;
        var now = clock.UtcNow;
        await db.Notifications.Where(n => n.UserId == me && n.ReadAt == null && (one == null || n.Id == one))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, (DateTime?)now), Aborted);
        return NoContent();
    }

    // ── mapping ─────────────────────────────────────────────────────────────

    internal static RepairDto ToDto(AssetRepair r) =>
        new(r.Id, r.AssetId, r.Fault, r.Action, r.Parts, r.Vendor, r.DowntimeDays, r.Cost, r.Status, r.RequestedBy, r.CreatedAt, r.CompletedAt);

    internal static DisposalDto ToDto(AssetDisposal d) =>
        new(d.Id, d.AssetId, d.Reason, d.Condition, d.RecommendedBy, d.Method, d.DisposalDate, d.Proceeds, d.Buyer, d.NbvAtDisposal, d.GainLoss, d.DataWipeNote, d.Status, d.CreatedAt);

    internal static ApprovalRequestDto ToDto(ApprovalRequest r) =>
        new(r.Id, r.Kind, r.AssetId, r.Asset?.AssetNumber, r.Asset?.Name ?? "", r.Status, r.Summary, r.RequestedBy, r.CreatedAt, r.DecidedAt,
            r.Steps.OrderBy(s => s.StepNo).ThenBy(s => s.Id)
                .Select(s => new ApprovalStepDto(s.Id, s.StepNo, s.Role, s.Label, s.Status, s.DecidedBy, s.OnBehalfOf, s.DecidedAt, s.Comment, s.EscalatedToRole, s.RemindedAt))
                .ToList());
}
