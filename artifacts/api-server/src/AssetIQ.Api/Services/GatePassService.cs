using AssetIQ.Api.Services.Approvals;
using AssetIQ.Api.Auth;
using System.Globalization;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

/// <param name="AssetId">Asset leaving the premises.</param>
/// <param name="RequestedBy">Who asks (free text: there is no employee directory in the MVP).</param>
/// <param name="Reason">Business reason.</param>
/// <param name="Destination">Where the asset is going, if known.</param>
/// <param name="ReturnDate">When it is due back (date part, UTC — as the dashboard sends YYYY-MM-DD).</param>
/// <param name="ItemAssetIds">Phase 4c: further items leaving under the same pass (same SBU).</param>
public sealed record GatePassRequest(int AssetId, string RequestedBy, string Reason, string? Destination, DateTime? ReturnDate, IReadOnlyList<int>? ItemAssetIds = null);

/// <param name="DecidedBy">Name of the approver taking the decision.</param>
/// <param name="Note">Optional note recorded with the decision.</param>
public sealed record GatePassDecision(string DecidedBy, string? Note);

public sealed record LocationRef(int Id, string Name, string Type);

/// <summary>
/// Gate pass workflow (plan section 6.3):
/// passes are requested manually or raised automatically by a gate reader (see RfidScanService);
/// approval releases the asset — if it is waiting at a gate (always the case for passes raised by a
/// gate reader) the exit is recorded immediately as a gate_pass movement, while a pass approved in
/// advance records that movement when a gate reader sees the asset leave;
/// rejection flags the asset (critical / warning insight + audit note in the asset history).
/// </summary>
public sealed class GatePassService(
    AssetIqDbContext db,
    AppClock clock,
    AppConfig config,
    MovementService movements,
    InsightService insights,
    ILogger<GatePassService> logger, ApprovalService approvals)
{
    /// <summary>Passes with asset, SBU and gate names, and when the asset left under them.</summary>
    public IQueryable<GatePassDto> Project(IQueryable<GatePass> passes) => passes.Select(p => new GatePassDto
    {
        Id = p.Id,
        AssetId = p.AssetId,
        AssetName = p.Asset.Name,
        RfidTag = p.Asset.RfidTag ?? "",
        SbuId = p.Asset.SbuId,
        SbuName = p.Asset.Sbu!.Name,
        RequestedBy = p.RequestedBy,
        ApprovedBy = p.ApprovedBy,
        Status = p.Status,
        Reason = p.Reason,
        Destination = p.Destination,
        ReturnDate = p.ReturnDate,
        DecisionNote = p.DecisionNote,
        Source = p.Source,
        GateLocationId = p.GateLocationId,
        GateLocationName = p.GateLocation!.Name,
        DecidedAt = p.DecidedAt,
        ExitedAt = db.Movements
            .Where(m => m.GatePassId == p.Id && m.TriggeredBy == MovementTriggers.GatePass)
            .Max(m => (DateTime?)m.Timestamp),
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        Items = db.GatePassItems.Where(i => i.GatePassId == p.Id).OrderBy(i => i.Id).Select(i => new GatePassItemDto
        {
            AssetId = i.AssetId,
            AssetName = i.Asset.Name,
            RfidTag = i.Asset.RfidTag,
            AssetNumber = i.Asset.AssetNumber,
            ExitedAt = i.ExitedAt,
            ExitOfficer = i.ExitOfficer,
            ReturnedAt = i.ReturnedAt,
            ReturnOfficer = i.ReturnOfficer,
            ReturnCondition = i.ReturnCondition,
            ReturnNotes = i.ReturnNotes,
        }).ToList(),
    });

    /// <summary>The passes with these ids as API objects, in the given order.</summary>
    public async Task<List<GatePassDto>> ToDtosAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var list = ids.ToList();
        var byId = await Project(db.GatePasses.IgnoreQueryFilters().AsNoTracking().Where(p => list.Contains(p.Id))).ToDictionaryAsync(p => p.Id, ct);
        return list.Select(id => byId[id]).ToList();
    }

    public async Task<GatePassDto> ToDtoAsync(int id, CancellationToken ct) => (await ToDtosAsync([id], ct))[0];

    public Task<List<GatePassDto>> ListAsync(string? status, int? sbuId, CancellationToken ct)
    {
        var query = db.GatePasses.AsNoTracking();
        if (status is not null) query = query.Where(p => p.Status == status);
        if (sbuId is not null) query = query.Where(p => p.Asset.SbuId == sbuId);
        return Project(query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)).ToListAsync(ct);
    }

    public async Task<GatePassDto> GetAsync(int id, CancellationToken ct) =>
        await Project(db.GatePasses.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == id)).FirstOrDefaultAsync(ct)
        ?? throw new ApiException(StatusCodes.Status404NotFound, "Gate pass not found");

    /// <summary>
    /// Matrix "Raise gate pass": 404 for an unknown asset, 403 unless the caller may raise one for it
    /// (<see cref="AccessPolicy.CanRaiseGatePass"/>).
    /// </summary>
    public async Task AuthorizeRaiseAsync(AuthUser user, int assetId, CancellationToken ct, IReadOnlyList<int>? itemAssetIds = null)
    {
        var asset = await db.Assets.IgnoreQueryFilters().AsNoTracking().Include(a => a.AssetCategory).FirstOrDefaultAsync(a => a.Id == assetId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Asset not found");
        if (!AccessPolicy.CanRaiseGatePass(user, asset, asset.AssetCategory.IsIt)) throw AccessPolicy.Deny(user, "raise a gate pass for this asset");
        foreach (var id in itemAssetIds ?? [])
        {
            var item = await db.Assets.IgnoreQueryFilters().AsNoTracking().Include(a => a.AssetCategory).FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, $"Asset {id} not found");
            // ASSUMPTION: the items of one pass belong to one SBU, so one line manager (of the first item) decides it.
            if (item.SbuId != asset.SbuId) throw new ApiException(StatusCodes.Status400BadRequest, "itemAssetIds: every item of a pass must belong to the same SBU");
            if (!AccessPolicy.CanRaiseGatePass(user, item, item.AssetCategory.IsIt)) throw AccessPolicy.Deny(user, $"raise a gate pass for \"{item.Name}\"");
        }
    }

    /// <summary>
    /// Phase 4: a gate pass is decided through the approvals engine. The line manager decides (the HOD of the asset's
    /// department, the SBU Admin Manager when there is no HOD available, Group IT / SBU IT for IT assets); a
    /// high-value pass also needs SBU Finance (SF-05; asset cost above <c>gate_pass_finance_threshold_usd</c>).
    /// Both are open at once and may decide in any order.
    /// </summary>
    public async Task<ApprovalRequest> OpenApprovalAsync(GatePass pass, Asset asset, AuthUser? requester, CancellationToken ct)
    {
        var threshold = await db.GroupSettings.AsNoTracking().Where(x => x.Key == "gate_pass_finance_threshold_usd").Select(x => x.Value).FirstOrDefaultAsync(ct);
        var highValue = decimal.TryParse(threshold, NumberStyles.Number, CultureInfo.InvariantCulture, out var limit)
            && (asset.CostUsd ?? asset.PurchaseValue ?? 0m) > limit;
        List<StepSpec> steps = [new(1, StepRoles.LineManager, asset.SbuId, asset.DepartmentId, "Line manager")];
        if (highValue) steps.Add(new(1, Roles.SbuFinance, asset.SbuId, null, "SBU Finance (high-value pass, SF-05)"));
        var loaded = asset.AssetCategory is null
            ? await db.Assets.IgnoreQueryFilters().Include(a => a.AssetCategory).FirstAsync(a => a.Id == asset.Id, ct)
            : asset;
        return await approvals.OpenAsync(ApprovalKinds.GatePass, loaded, pass.Id, $"Gate pass #{pass.Id}: {asset.Name} — {pass.Reason}",
            new { pass.Reason, pass.Destination, pass.ReturnDate }, steps, requester, pass.RequestedBy, ct);
    }

    /// <summary>Pending passes raised before the approvals engine (or by the seed) get their request, so they reach the inbox.</summary>
    public async Task EnsureApprovalRequestsAsync(CancellationToken ct)
    {
        var withRequest = db.ApprovalRequests.Where(r => r.Kind == ApprovalKinds.GatePass).Select(r => r.SubjectId);
        var missing = await db.GatePasses.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Status == GatePassStatuses.Pending && !withRequest.Contains(p.Id)).ToListAsync(ct);
        foreach (var pass in missing)
        {
            var asset = await db.Assets.IgnoreQueryFilters().Include(a => a.AssetCategory).FirstAsync(a => a.Id == pass.AssetId, ct);
            await OpenApprovalAsync(pass, asset, null, ct);
        }
    }

    /// <summary>
    /// The caller's decision on a gate pass: 404 unknown, 409 already decided, 403 nothing for the caller to decide.
    /// The pass is approved (or rejected) once its request is; until then it stays pending.
    /// </summary>
    public async Task<GatePassDto> DecideAsync(AuthUser user, int passId, bool approve, string? note, JsonBody input, CancellationToken ct)
    {
        var pass = await db.GatePasses.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.Id == passId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Gate pass not found");
        if (pass.Status != GatePassStatuses.Pending) throw new ApiException(StatusCodes.Status409Conflict, $"Gate pass #{passId} has already been {pass.Status}");
        var request = await approvals.FindBySubjectAsync(ApprovalKinds.GatePass, passId, ct);
        if (request is null || request.Status != ApprovalStatuses.Pending)
        {
            // Passes raised before the approvals engine get their request when first decided.
            var asset = await db.Assets.IgnoreQueryFilters().Include(a => a.AssetCategory).FirstAsync(a => a.Id == pass.AssetId, ct);
            request = await OpenApprovalAsync(pass, asset, null, ct);
        }
        await approvals.DecideAsync(user, request.Id, approve, note, input, ct);
        return await GetAsync(passId, ct);
    }

    /// <summary>Manual request (the "Request gate pass" form). One pending pass per asset at a time.</summary>
    public async Task<GatePassDto> CreateRequestAsync(GatePassRequest input, CancellationToken ct, AuthUser? requester = null)
    {
        var now = clock.UtcNow;
        var requestedBy = input.RequestedBy.Trim();
        var reason = input.Reason.Trim();
        if (requestedBy.Length == 0) throw new ApiException(StatusCodes.Status400BadRequest, "requestedBy is required");
        if (reason.Length == 0) throw new ApiException(StatusCodes.Status400BadRequest, "reason is required");

        var returnDate = input.ReturnDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dateError = GatePassRules.ValidateReturnDate(returnDate, now, config.TimeZone);
        if (dateError is not null) throw new ApiException(StatusCodes.Status400BadRequest, dateError);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var asset = await LockAssetAsync(input.AssetId, ct);

        var pendingId = await db.GatePasses.IgnoreQueryFilters()
            .Where(p => p.AssetId == asset.Id && p.Status == GatePassStatuses.Pending)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (pendingId is not null)
        {
            throw new ApiException(StatusCodes.Status409Conflict, $"\"{asset.Name}\" already has a pending gate pass (#{pendingId})");
        }

        var destination = input.Destination?.Trim();
        var pass = new GatePass
        {
            AssetId = asset.Id,
            RequestedBy = requestedBy,
            Reason = reason,
            Destination = string.IsNullOrEmpty(destination) ? null : destination,
            ReturnDate = returnDate,
            Status = GatePassStatuses.Pending,
            Source = GatePassSources.Manual,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.GatePasses.Add(pass);
        await db.SaveChangesAsync(ct);
        foreach (var itemId in new[] { asset.Id }.Concat(input.ItemAssetIds ?? []).Distinct())
            db.GatePassItems.Add(new GatePassItem { GatePassId = pass.Id, AssetId = itemId });
        await db.SaveChangesAsync(ct);
        await OpenApprovalAsync(pass, asset, requester, ct);
        var dto = await ToDtoAsync(pass.Id, ct);
        await transaction.CommitAsync(ct);
        return dto;
    }

    /// <summary>Approve; when the asset is waiting at a gate the gate_pass exit is recorded straight away.</summary>
    public async Task<GatePassDto> ApproveAsync(int id, GatePassDecision decision, CancellationToken ct)
    {
        var (decidedBy, note) = Normalise(decision);
        var now = clock.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var pass = await ClaimPendingPassAsync(id, GatePassStatuses.Approved, decidedBy, note, now, ct);
        await DismissExitAttemptAlertsAsync(id, ct);

        var (asset, location) = await LoadAssetWithLocationAsync(pass.AssetId, ct);
        var exit = location?.Type == LocationTypes.Gate
            ? await movements.RecordGatePassExitAsync(pass, asset, location.Name, null, now, ct, location.Id)
            : null;

        var dto = await ToDtoAsync(pass.Id, ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Gate pass {GatePassId} approved by {DecidedBy}; exit movement {ExitMovementId}", id, decidedBy, exit?.Id);
        return dto;
    }

    /// <summary>Reject and flag the asset: an unauthorised-movement alert plus a durable note in its history.</summary>
    public async Task<GatePassDto> RejectAsync(int id, GatePassDecision decision, CancellationToken ct)
    {
        var (decidedBy, note) = Normalise(decision);
        var now = clock.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var pass = await ClaimPendingPassAsync(id, GatePassStatuses.Rejected, decidedBy, note, now, ct);
        var (asset, location) = await LoadAssetWithLocationAsync(pass.AssetId, ct);
        var heldAtGate = location?.Type == LocationTypes.Gate;

        // The flag supersedes the earlier "exit attempt" warning for this pass.
        await DismissExitAttemptAlertsAsync(id, ct);
        var flag = await insights.RaiseAsync(new InsightDraft(
            "gate_pass_rejected",
            InsightTypes.UnauthorizedMovement,
            pass.Source == GatePassSources.RfidGate || heldAtGate ? InsightSeverities.Critical : InsightSeverities.Warning,
            $"Flagged: gate pass #{id} for \"{asset.Name}\" was rejected by {decidedBy}{(note is null ? "" : $" ({note})")} — " +
            (heldAtGate && location is not null
                ? $"the asset is at \"{location.Name}\" and must not leave the premises"
                : "the asset must not leave the premises"),
            asset.Id,
            id), InsightDedupe.Message, ct);

        // Durable audit trail in the asset history (insights can be dismissed).
        db.AssetLifecycleEvents.Add(new AssetLifecycleEvent
        {
            AssetId = asset.Id,
            EventType = LifecycleEventTypes.Note,
            PerformedBy = decidedBy,
            Notes = $"Flagged: gate pass #{id} rejected{(note is null ? "" : $" — {note}")}. The asset must not leave the premises.",
            Timestamp = now,
        });
        await db.SaveChangesAsync(ct);

        var dto = await ToDtoAsync(pass.Id, ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Gate pass {GatePassId} rejected by {DecidedBy}; flag insight {FlagInsightId}", id, decidedBy, flag?.Id);
        return dto;
    }

    private static (string DecidedBy, string? Note) Normalise(GatePassDecision decision)
    {
        var decidedBy = decision.DecidedBy.Trim();
        if (decidedBy.Length == 0) throw new ApiException(StatusCodes.Status400BadRequest, "decidedBy is required — record who took the decision");
        var note = decision.Note?.Trim();
        return (decidedBy, string.IsNullOrEmpty(note) ? null : note);
    }

    /// <summary>Atomically move a pass out of "pending"; 404 if it does not exist, 409 if it was already decided.</summary>
    private async Task<GatePass> ClaimPendingPassAsync(int id, string status, string decidedBy, string? note, DateTime now, CancellationToken ct)
    {
        var claimed = await db.GatePasses.IgnoreQueryFilters()
            .Where(p => p.Id == id && p.Status == GatePassStatuses.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, status)
                .SetProperty(p => p.ApprovedBy, decidedBy)
                .SetProperty(p => p.DecisionNote, note)
                .SetProperty(p => p.DecidedAt, (DateTime?)now)
                .SetProperty(p => p.UpdatedAt, now), ct);
        if (claimed == 1) return await db.GatePasses.IgnoreQueryFilters().AsNoTracking().FirstAsync(p => p.Id == id, ct);

        var existing = await db.GatePasses.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == id).Select(p => p.Status).FirstOrDefaultAsync(ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Gate pass not found");
        throw new ApiException(StatusCodes.Status409Conflict, $"Gate pass #{id} has already been {existing}");
    }

    /// <summary>The asset row, locked for the rest of the transaction.</summary>
    private async Task<Asset> LockAssetAsync(int assetId, CancellationToken ct) =>
        (await db.Assets.FromSql($"SELECT * FROM assets WHERE id = {assetId} FOR UPDATE").IgnoreQueryFilters().ToListAsync(ct)).FirstOrDefault()
        ?? throw new ApiException(StatusCodes.Status404NotFound, "Asset not found");

    private async Task<(Asset Asset, LocationRef? Location)> LoadAssetWithLocationAsync(int assetId, CancellationToken ct)
    {
        var asset = await LockAssetAsync(assetId, ct);
        var location = asset.CurrentLocationId is { } locationId
            ? await db.Locations.AsNoTracking()
                .Where(l => l.Id == locationId)
                .Select(l => new LocationRef(l.Id, l.Name, l.Type))
                .FirstOrDefaultAsync(ct)
            : null;
        return (asset, location);
    }

    private Task<int> DismissExitAttemptAlertsAsync(int gatePassId, CancellationToken ct) =>
        db.AiInsights.IgnoreQueryFilters()
            .Where(i => i.GatePassId == gatePassId && i.Type == InsightTypes.UnauthorizedMovement && !i.Dismissed)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Dismissed, true), ct);
}
