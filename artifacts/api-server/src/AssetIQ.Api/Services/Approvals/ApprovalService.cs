using System.Globalization;
using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services.Approvals;

public static class ApprovalKinds
{
    public const string Repair = "repair";
    public const string Disposal = "disposal";
    public const string Transfer = "transfer";
    public const string GatePass = "gate_pass";
    /// <summary>Phase 4b: LM-05 / HR-02 equipment requests (no asset yet).</summary>
    public const string Equipment = "equipment";
}

/// <summary>Approver roles of a step: the user roles, plus the line manager (HOD, or who stands in for them).</summary>
public static class StepRoles
{
    public const string LineManager = "line_manager";
}

/// <param name="StepNo">Steps with the same number are open at the same time and may be decided in any order.</param>
public sealed record StepSpec(int StepNo, string Role, int? SbuId, int? DepartmentId, string Label);

/// <summary>What a kind of request does when a step is approved, and when the whole request is decided.</summary>
public interface IApprovalHandler
{
    string Kind { get; }
    /// <summary>Validates and records what the approver must add at this step (Finance's disposal details, the data-wipe note …).</summary>
    Task BeforeApproveAsync(AuthUser user, ApprovalRequest request, ApprovalStep step, JsonBody input, CancellationToken ct);
    Task OnApprovedAsync(AuthUser user, ApprovalRequest request, string? comment, CancellationToken ct);
    Task OnRejectedAsync(AuthUser user, ApprovalRequest request, string? comment, CancellationToken ct);
}

/// <summary>
/// Who may decide a step (Phase 4). Pure, so the routing can be unit tested. The line manager is the HOD of the
/// asset's department; the SBU Admin Manager stands in when the department has no HOD available; for IT assets
/// Group IT and SBU IT may decide too (permissions matrix "Approve gate pass"). An escalated step is also open to
/// the role it escalated to. The System Administrator may decide anything.
/// </summary>
public static class ApprovalRouting
{
    public static bool Eligible(AuthUser user, ApprovalStep step, bool isIt, bool hodAvailable)
    {
        if (AccessPolicy.Sys(user)) return true;
        if (step.EscalatedToRole is { } escalated && RoleMatches(user, escalated, step.SbuId)) return true;
        return step.Role switch
        {
            StepRoles.LineManager =>
                (user.Role == Roles.Hod && user.SbuId == step.SbuId && step.DepartmentId is not null && user.DepartmentId == step.DepartmentId)
                || (user.Role == Roles.SbuAdmin && user.SbuId == step.SbuId && !hodAvailable)
                || (isIt && (user.Role == Roles.GroupIt || (user.Role == Roles.SbuIt && user.SbuId == step.SbuId))),
            _ => RoleMatches(user, step.Role, step.SbuId),
        };
    }

    private static bool RoleMatches(AuthUser user, string role, int? sbuId) =>
        user.Role == role && (!Roles.IsSbuScoped(role) || (user.SbuId is not null && user.SbuId == sbuId));

    /// <summary>The next approver when a step is not answered in time.</summary>
    public static string EscalationOf(string role) => role switch
    {
        StepRoles.LineManager or Roles.Hod => Roles.SbuAdmin,
        Roles.SbuIt => Roles.GroupIt,
        Roles.SbuFinance => Roles.GroupFinance,
        _ => Roles.SystemAdmin,
    };
}

/// <summary>
/// The approvals engine (Phase 4): opens requests with their steps, gives each approver one inbox (including
/// what they decide for a colleague on leave, LM-03), records every decision and comment in the audit trail,
/// reminds and then escalates steps left unanswered, and hands the outcome to the kind's handler.
/// </summary>
public sealed class ApprovalService(
    AssetIqDbContext db, AppClock clock, AuditTrail audit, NotificationService notifications, IServiceProvider services)
{
    public Task<ApprovalRequest> OpenAsync(string kind, Asset asset, int? subjectId, string summary, object details, IEnumerable<StepSpec> steps,
        AuthUser? requester, string requestedBy, CancellationToken ct) =>
        OpenAsync(kind, asset.Id, asset.SbuId ?? 0, subjectId, summary, details, steps, requester, requestedBy, ct);

    /// <summary>A request about no particular asset yet (an equipment request).</summary>
    public async Task<ApprovalRequest> OpenAsync(string kind, int? assetId, int sbuId, int? subjectId, string summary, object details, IEnumerable<StepSpec> steps,
        AuthUser? requester, string requestedBy, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var specs = steps.ToList();
        var first = specs.Min(s => s.StepNo);
        var request = new ApprovalRequest
        {
            Kind = kind, AssetId = assetId, SbuId = sbuId, SubjectId = subjectId, Summary = summary,
            Details = JsonSerializer.Serialize(details), RequestedBy = requestedBy, RequestedByUserId = requester?.Id, CreatedAt = now,
            Steps = specs.Select(s => new ApprovalStep
            {
                StepNo = s.StepNo, Role = s.Role, SbuId = s.SbuId, DepartmentId = s.DepartmentId, Label = s.Label,
                Status = s.StepNo == first ? ApprovalStatuses.Pending : ApprovalStatuses.Waiting, CreatedAt = now,
            }).ToList(),
        };
        db.ApprovalRequests.Add(request);
        await db.SaveChangesAsync(ct);
        audit.Record(requester, "approval", request.Id, assetId, "approval_requested", kind, null, summary,
            string.Join("; ", request.Steps.Select(s => s.Label)));
        foreach (var step in request.Steps.Where(s => s.Status == ApprovalStatuses.Pending))
            await NotifyApproversAsync(request, step, "approval", $"Approval needed: {summary}", ct);
        await db.SaveChangesAsync(ct);
        return request;
    }

    /// <summary>Adds a step while a request is open (e.g. Group Finance once a disposal turns out to be above the threshold).</summary>
    public void AddStep(ApprovalRequest request, StepSpec spec) =>
        request.Steps.Add(new ApprovalStep
        {
            StepNo = spec.StepNo, Role = spec.Role, SbuId = spec.SbuId, DepartmentId = spec.DepartmentId, Label = spec.Label,
            Status = ApprovalStatuses.Waiting, CreatedAt = clock.UtcNow,
        });

    public Task<ApprovalRequest?> FindAsync(int id, CancellationToken ct) =>
        db.ApprovalRequests.Include(r => r.Steps).Include(r => r.Asset).ThenInclude(a => a!.AssetCategory).IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<ApprovalRequest?> FindBySubjectAsync(string kind, int subjectId, CancellationToken ct) =>
        db.ApprovalRequests.Include(r => r.Steps).Include(r => r.Asset).ThenInclude(a => a!.AssetCategory).IgnoreQueryFilters()
            .Where(r => r.Kind == kind && r.SubjectId == subjectId).OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);

    /// <summary>
    /// The caller's decision on the first open step they may decide (directly, by escalation or for a colleague
    /// on leave). 404 unknown request, 409 already decided, 403 nothing for the caller to decide.
    /// </summary>
    public async Task<ApprovalRequest> DecideAsync(AuthUser user, int requestId, bool approve, string? comment, JsonBody input, CancellationToken ct)
    {
        var request = await FindAsync(requestId, ct) ?? throw new ApiException(StatusCodes.Status404NotFound, "Approval request not found");
        if (request.Status != ApprovalStatuses.Pending) throw new ApiException(StatusCodes.Status409Conflict, $"The request was already {request.Status}");
        var (step, onBehalfOf) = await StepForAsync(user, request, ct);
        if (step is null)
            throw new ApiException(StatusCodes.Status403Forbidden,
                $"Your role ({Roles.DisplayName(user.Role)}) has no step to decide on this request; it waits for {string.Join(", ", request.Steps.Where(s => s.Status == ApprovalStatuses.Pending).Select(s => s.Label))}");
        // Resolved here, not injected: the handlers use the workflows, which open requests through this service.
        var handler = services.GetServices<IApprovalHandler>().Single(h => h.Kind == request.Kind);
        if (approve) await handler.BeforeApproveAsync(user, request, step, input, ct);
        input.ThrowIfInvalid();

        var now = clock.UtcNow;
        step.Status = approve ? ApprovalStatuses.Approved : ApprovalStatuses.Rejected;
        step.DecidedBy = user.Name;
        step.DecidedByUserId = user.Id;
        step.OnBehalfOf = onBehalfOf;
        step.DecidedAt = now;
        step.Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        audit.Record(user, "approval", request.Id, request.AssetId, approve ? "approval_step_approved" : "approval_step_rejected", step.Label,
            ApprovalStatuses.Pending, step.Status, onBehalfOf is null ? step.Comment : $"{step.Comment ?? ""} (for {onBehalfOf}, on leave)".Trim());

        if (!approve)
        {
            foreach (var other in request.Steps.Where(s => s.Status is ApprovalStatuses.Pending or ApprovalStatuses.Waiting)) other.Status = ApprovalStatuses.Rejected;
            await FinishAsync(user, request, ApprovalStatuses.Rejected, step.Comment, handler, ct);
            return request;
        }
        if (request.Steps.Any(s => s.StepNo == step.StepNo && s.Status == ApprovalStatuses.Pending))
        {
            await db.SaveChangesAsync(ct);
            return request;
        }
        var next = request.Steps.Where(s => s.Status == ApprovalStatuses.Waiting).Select(s => (int?)s.StepNo).Min();
        if (next is null)
        {
            await FinishAsync(user, request, ApprovalStatuses.Approved, step.Comment, handler, ct);
            return request;
        }
        foreach (var s in request.Steps.Where(s => s.StepNo == next && s.Status == ApprovalStatuses.Waiting))
        {
            s.Status = ApprovalStatuses.Pending;
            s.CreatedAt = now; // reminders and escalation count from when the step opened
            await NotifyApproversAsync(request, s, "approval", $"Approval needed: {request.Summary}", ct);
        }
        await db.SaveChangesAsync(ct);
        return request;
    }

    private async Task FinishAsync(AuthUser user, ApprovalRequest request, string status, string? comment, IApprovalHandler handler, CancellationToken ct)
    {
        request.Status = status;
        request.DecidedAt = clock.UtcNow;
        audit.Record(user, "approval", request.Id, request.AssetId, $"approval_{status}", request.Kind, ApprovalStatuses.Pending, status, comment);
        if (status == ApprovalStatuses.Approved) await handler.OnApprovedAsync(user, request, comment, ct);
        else await handler.OnRejectedAsync(user, request, comment, ct);
        if (request.RequestedByUserId is { } requesterId)
        {
            var requester = await db.Users.Where(u => u.Id == requesterId).ToListAsync(ct);
            notifications.Notify(requester, "decision", $"{Title(request.Kind)} {status}: {request.Summary}",
                comment is null ? $"Decided by {user.Name}." : $"Decided by {user.Name}: {comment}", LinkOf(request));
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>One inbox (Phase 4): every open step the caller may decide, including those of a colleague on leave.</summary>
    public async Task<List<ApprovalItemDto>> InboxAsync(AuthUser user, CancellationToken ct)
    {
        var open = await db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
            .Include(r => r.Steps).Include(r => r.Asset).ThenInclude(a => a!.AssetCategory).Include(r => r.Asset).ThenInclude(a => a!.Sbu)
            .Where(r => r.Status == ApprovalStatuses.Pending)
            .OrderBy(r => r.CreatedAt).ToListAsync(ct);
        var items = new List<ApprovalItemDto>();
        foreach (var request in open)
        {
            var (step, onBehalfOf) = await StepForAsync(user, request, ct);
            if (step is null) continue;
            var a = request.Asset;
            var sbuCode = a?.Sbu?.Code ?? await db.Sbus.Where(x => x.Id == request.SbuId).Select(x => x.Code).FirstOrDefaultAsync(ct);
            items.Add(new ApprovalItemDto($"request-{request.Id}", request.Kind, a?.Id, a?.AssetNumber, a?.Name ?? request.Summary, sbuCode, request.Summary,
                request.CreatedAt, request.RequestedBy, null, request.Id, step.Label, NeedsOf(request, step), onBehalfOf, step.EscalatedToRole is not null));
        }
        return items;
    }

    /// <summary>Reminders, then escalation, for steps nobody has answered (group settings, hours). Returns how many were sent.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var reminderHours = await SettingHoursAsync("approval_reminder_hours", 24, ct);
        var escalationHours = await SettingHoursAsync("approval_escalation_hours", 48, ct);
        var open = await db.ApprovalRequests.IgnoreQueryFilters().Include(r => r.Steps).Include(r => r.Asset).ThenInclude(a => a!.AssetCategory)
            .Where(r => r.Status == ApprovalStatuses.Pending).ToListAsync(ct);
        var sent = 0;
        foreach (var request in open)
        {
            foreach (var step in request.Steps.Where(s => s.Status == ApprovalStatuses.Pending))
            {
                var age = now - step.CreatedAt;
                if (step.EscalatedAt is null && age >= TimeSpan.FromHours(escalationHours))
                {
                    step.EscalatedToRole = ApprovalRouting.EscalationOf(step.Role);
                    step.EscalatedAt = now;
                    audit.Record(null, "approval", request.Id, request.AssetId, "approval_escalated", step.Label, step.Role, step.EscalatedToRole,
                        $"No decision after {escalationHours} hours");
                    await NotifyApproversAsync(request, step, "escalation", $"Escalated to you: {request.Summary}", ct);
                    sent++;
                }
                else if (step.RemindedAt is null && step.EscalatedAt is null && age >= TimeSpan.FromHours(reminderHours))
                {
                    step.RemindedAt = now;
                    audit.Record(null, "approval", request.Id, request.AssetId, "approval_reminder", step.Label, null, null,
                        $"No decision after {reminderHours} hours");
                    await NotifyApproversAsync(request, step, "reminder", $"Reminder: {request.Summary} is waiting for you", ct);
                    sent++;
                }
            }
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }

    /// <summary>The first open step <paramref name="user"/> may decide, directly or for a colleague on leave (whose name is returned).</summary>
    private async Task<(ApprovalStep? Step, string? OnBehalfOf)> StepForAsync(AuthUser user, ApprovalRequest request, CancellationToken ct)
    {
        var isIt = request.Asset?.AssetCategory.IsIt ?? false;
        var openSteps = request.Steps.Where(s => s.Status == ApprovalStatuses.Pending).OrderBy(s => s.Id).ToList();
        if (openSteps.Count == 0) return (null, null);
        var hod = new Dictionary<int, bool>();
        foreach (var s in openSteps) hod[s.Id] = await HodAvailableAsync(s, ct);
        var direct = openSteps.FirstOrDefault(s => ApprovalRouting.Eligible(user, s, isIt, hod[s.Id]));
        if (direct is not null) return (direct, null);
        foreach (var colleague in await OnLeaveDelegatingToAsync(user.Id, ct))
        {
            var step = openSteps.FirstOrDefault(s => ApprovalRouting.Eligible(colleague, s, isIt, hod[s.Id]));
            if (step is not null) return (step, colleague.Name);
        }
        return (null, null);
    }

    /// <summary>LM-03: the people currently on leave who named <paramref name="delegateUserId"/> to decide for them.</summary>
    private async Task<List<AuthUser>> OnLeaveDelegatingToAsync(int delegateUserId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var ids = await db.Delegations.AsNoTracking()
            .Where(d => d.DelegateUserId == delegateUserId && d.StartsAt <= now && d.EndsAt > now).Select(d => d.UserId).ToListAsync(ct);
        return await AuthUsersAsync(db.Users.Where(u => ids.Contains(u.Id) && u.Active), ct);
    }

    /// <summary>The step's department has an active HOD who is not on leave (otherwise the SBU Admin Manager stands in).</summary>
    private async Task<bool> HodAvailableAsync(ApprovalStep step, CancellationToken ct)
    {
        var department = step.DepartmentId;
        if (department is null) return false;
        var now = clock.UtcNow;
        return await db.Users.AnyAsync(u => u.Active && u.Role == Roles.Hod && u.SbuId == step.SbuId
            && db.Employees.Any(e => e.Id == u.EmployeeId && e.DepartmentId == department)
            && !db.Delegations.Any(d => d.UserId == u.Id && d.StartsAt <= now && d.EndsAt > now), ct);
    }

    private async Task NotifyApproversAsync(ApprovalRequest request, ApprovalStep step, string kind, string title, CancellationToken ct)
    {
        var asset = await AssetOfAsync(request, ct);
        var isIt = asset?.AssetCategory.IsIt ?? false;
        var hod = await HodAvailableAsync(step, ct);
        var candidates = await AuthUsersAsync(db.Users.Where(u => u.Active && u.Role != Roles.SystemAdmin && u.Role != Roles.Auditor), ct);
        var eligible = candidates.Where(c => ApprovalRouting.Eligible(c, step, isIt, hod)).Select(c => c.Id).ToHashSet();
        // Colleagues on leave: their delegate gets the message instead.
        var now = clock.UtcNow;
        var delegates = await db.Delegations.AsNoTracking()
            .Where(d => eligible.Contains(d.UserId) && d.DelegateUserId != null && d.StartsAt <= now && d.EndsAt > now).Select(d => d.DelegateUserId!.Value).ToListAsync(ct);
        eligible.UnionWith(delegates);
        var recipients = await db.Users.Where(u => eligible.Contains(u.Id)).ToListAsync(ct);
        notifications.Notify(recipients, kind, title, $"{step.Label}: {request.Summary}{(asset is null ? "" : $" (asset {asset.AssetNumber ?? asset.Id.ToString(CultureInfo.InvariantCulture)})")}", LinkOf(request));
    }

    /// <summary>The request's asset with its category (a request just opened has not loaded it).</summary>
    private async Task<Asset?> AssetOfAsync(ApprovalRequest request, CancellationToken ct) =>
        request.Asset is { AssetCategory: not null } loaded ? loaded
            : request.AssetId is { } id ? await db.Assets.IgnoreQueryFilters().AsNoTracking().Include(a => a.AssetCategory).FirstAsync(a => a.Id == id, ct)
            : null;

    private async Task<List<AuthUser>> AuthUsersAsync(IQueryable<UserAccount> users, CancellationToken ct) =>
        (await users.Select(u => new { u.Id, u.Username, u.Name, u.Role, u.SbuId, u.EmployeeId, DepartmentId = db.Employees.Where(e => e.Id == u.EmployeeId).Select(e => e.DepartmentId).FirstOrDefault() })
            .ToListAsync(ct))
        .Select(u => new AuthUser(u.Id, u.Username, u.Name, u.Role, u.SbuId, u.EmployeeId, u.DepartmentId)).ToList();

    private async Task<double> SettingHoursAsync(string key, double fallback, CancellationToken ct)
    {
        var value = await db.GroupSettings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var hours) && hours > 0 ? hours : fallback;
    }

    /// <summary>What the approver must add at this step (shown in the inbox).</summary>
    public static IReadOnlyList<string> NeedsOf(ApprovalRequest request, ApprovalStep step) => (request.Kind, step.Role) switch
    {
        (ApprovalKinds.Disposal, Roles.SbuFinance) => ["method", "disposalDate", "proceeds"],
        (ApprovalKinds.Disposal, Roles.GroupIt) => ["dataWipeNote"],
        _ => [],
    };

    private static string LinkOf(ApprovalRequest request) => $"/approvals?request={request.Id}";

    private static string Title(string kind) => kind switch
    {
        ApprovalKinds.GatePass => "Gate pass",
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(kind),
    };
}

public static class ApprovalStatuses
{
    public const string Waiting = "waiting";
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}
