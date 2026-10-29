using AssetIQ.Api.Auth;
using AssetIQ.Api.Data;
using AssetIQ.Api.Infrastructure;

namespace AssetIQ.Api.Services.Approvals;

/// <summary>A decided gate pass request approves or rejects the pass (with its gate exit and alerts, as before Phase 4).</summary>
public sealed class GatePassApprovalHandler(GatePassService gatePasses) : IApprovalHandler
{
    public string Kind => ApprovalKinds.GatePass;

    public Task BeforeApproveAsync(AuthUser user, ApprovalRequest request, ApprovalStep step, JsonBody input, CancellationToken ct) => Task.CompletedTask;

    public async Task OnApprovedAsync(AuthUser user, ApprovalRequest request, string? comment, CancellationToken ct) =>
        await gatePasses.ApproveAsync(request.SubjectId!.Value, new GatePassDecision(Approvers(request), comment), ct);

    public async Task OnRejectedAsync(AuthUser user, ApprovalRequest request, string? comment, CancellationToken ct) =>
        await gatePasses.RejectAsync(request.SubjectId!.Value, new GatePassDecision(user.Name, comment), ct);

    private static string Approvers(ApprovalRequest request) =>
        string.Join(" & ", request.Steps.Where(s => s.Status == ApprovalStatuses.Approved).OrderBy(s => s.StepNo).ThenBy(s => s.Id).Select(s => s.DecidedBy).Distinct());
}
