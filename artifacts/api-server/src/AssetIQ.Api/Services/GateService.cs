using AssetIQ.Api.Auth;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

public sealed record GateReturnInput(int AssetId, string? Condition, string? Notes);

/// <summary>
/// Phase 4c, the Gate Officer (SG-01 … SG-03, GI-06, SI-04): checking a gate pass at the gate (green / red), recording
/// each item's exit and return (partial returns on a multi-item pass), what was done about an alert, and the flags for
/// items not back by their return date.
/// </summary>
public sealed class GateService(
    AssetIqDbContext db,
    AppClock clock,
    AppConfig config,
    MovementService movements,
    GatePassService gatePasses,
    AssetStatusService statuses,
    InsightService insights,
    NotificationService notifications,
    AuditTrail audit)
{
    /// <summary>
    /// SG-01: a scan at the gate, of the pass QR (<c>GP-12</c>) or an item's tag / number. Green ("clear") when a valid
    /// approved pass lets the item(s) leave, amber ("return") when items are expected back, red ("stop") otherwise.
    /// </summary>
    public async Task<GateScanDto> ScanAsync(AuthUser user, string? code, CancellationToken ct)
    {
        var text = (code ?? "").Trim().ToUpperInvariant();
        if (text.Length == 0) throw new ApiException(StatusCodes.Status400BadRequest, "code: Required");
        var now = clock.UtcNow;

        GatePass? pass;
        Asset? scanned = null;
        if (text.StartsWith("GP-", StringComparison.Ordinal) && int.TryParse(text[3..], out var passId))
        {
            pass = await db.GatePasses.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.Id == passId, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, $"Gate pass {code!.Trim()} not found");
        }
        else
        {
            scanned = await db.Assets.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.AssetNumber!.ToUpper() == text || a.RfidTag!.ToUpper() == text || a.PreviousAssetNumber!.ToUpper() == text || a.SerialNumber == text)
                .FirstOrDefaultAsync(ct);
            if (scanned is null)
            {
                // SG-05 (Phase 5b): a visitor's or contractor's own equipment logged on entry may leave without a gate pass.
                var visitor = await db.VisitorItems.AsNoTracking()
                    .Where(v => v.SerialNumber == text && v.CheckedOutAt == null && (!Roles.IsSbuScoped(user.Role) || v.SbuId == user.SbuId))
                    .OrderByDescending(v => v.CheckedInAt).FirstOrDefaultAsync(ct);
                if (visitor is not null)
                    return new GateScanDto { Verdict = GateVerdicts.Clear, Message = $"Visitor's own equipment: {visitor.Item}, {visitor.OwnerName}{(visitor.OwnerCompany is null ? "" : $" ({visitor.OwnerCompany})")} — check it out", VisitorItemId = visitor.Id };
                throw new ApiException(StatusCodes.Status404NotFound, $"No asset has the number, tag or serial {code!.Trim()}");
            }
            var assetId = scanned.Id;
            // The pass the item is out on, else a valid approved one, else the latest.
            var passIds = db.GatePassItems.Where(i => i.AssetId == assetId).Select(i => i.GatePassId);
            var candidates = await db.GatePasses.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.AssetId == assetId || passIds.Contains(p.Id)).OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
            var outOn = await db.GatePassItems.AsNoTracking().Where(i => i.AssetId == assetId && i.ExitedAt != null && i.ReturnedAt == null)
                .OrderByDescending(i => i.ExitedAt).Select(i => (int?)i.GatePassId).FirstOrDefaultAsync(ct);
            pass = candidates.FirstOrDefault(p => p.Id == outOn)
                ?? GatePassRules.FindValidApprovedPass(candidates.Where(p => p.Status == GatePassStatuses.Approved), now, config.TimeZone)
                ?? candidates.FirstOrDefault();
        }

        var sbuId = pass is not null
            ? await db.Assets.IgnoreQueryFilters().Where(a => a.Id == pass.AssetId).Select(a => a.SbuId).FirstAsync(ct)
            : scanned!.SbuId;
        if (!AccessPolicy.Sys(user) && Roles.IsSbuScoped(user.Role) && !AccessPolicy.OwnSbu(user, sbuId))
            throw AccessPolicy.Deny(user, "check another SBU's items at the gate");

        if (pass is null)
        {
            return new GateScanDto
            {
                Verdict = GateVerdicts.Stop,
                Message = $"No gate pass for {scanned!.Name}: it must not leave the premises",
                AssetId = scanned.Id,
            };
        }

        await EnsureItemsAsync(pass.Id, ct);
        var dto = await gatePasses.GetAsync(pass.Id, ct);
        var waiting = dto.Items.Where(i => i.ExitedAt is null && (scanned is null || i.AssetId == scanned.Id)).ToList();
        var outside = dto.Items.Where(i => i.ExitedAt is not null && i.ReturnedAt is null && (scanned is null || i.AssetId == scanned.Id)).ToList();
        var (verdict, message) = outside.Count > 0
            ? (GateVerdicts.Return, $"Expected back under gate pass #{pass.Id}: {outside.Count} item(s){(pass.ReturnDate is null ? "" : $", due {pass.ReturnDate}")}")
            : pass.Status != GatePassStatuses.Approved
                ? (GateVerdicts.Stop, $"Gate pass #{pass.Id} is {pass.Status}: the item must not leave")
                : !GatePassRules.IsPassValidAt(pass, now, config.TimeZone)
                    ? (GateVerdicts.Stop, $"Gate pass #{pass.Id} has expired (return date {pass.ReturnDate})")
                    : waiting.Count == 0
                        ? (GateVerdicts.Stop, $"Every item of gate pass #{pass.Id} has already been used")
                        : (GateVerdicts.Clear, $"Gate pass #{pass.Id} approved by {pass.ApprovedBy}: {waiting.Count} item(s) may leave");
        return new GateScanDto { Verdict = verdict, Message = message, AssetId = scanned?.Id ?? pass.AssetId, GatePass = dto };
    }

    /// <summary>SG-03: the selected items (all that have not left when none are given) leave under an approved, valid pass.</summary>
    public async Task<GatePassDto> RecordExitAsync(AuthUser user, int passId, int gateLocationId, IReadOnlyList<int>? assetIds, string? notes, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var pass = await LoadPassForGateAsync(user, passId, ct);
        var gate = await LoadGateAsync(user, gateLocationId, ct);
        if (pass.Status != GatePassStatuses.Approved)
            throw new ApiException(StatusCodes.Status409Conflict, $"Gate pass #{passId} is {pass.Status}: nothing may leave under it");
        if (!GatePassRules.IsPassValidAt(pass, now, config.TimeZone))
            throw new ApiException(StatusCodes.Status409Conflict, $"Gate pass #{passId} has expired (return date {pass.ReturnDate})");

        await EnsureItemsAsync(passId, ct);
        var items = await db.GatePassItems.Where(i => i.GatePassId == passId).OrderBy(i => i.Id).ToListAsync(ct);
        var chosen = Choose(items, assetIds, i => i.ExitedAt is null, "has already left");
        foreach (var item in chosen)
        {
            var asset = await LockAssetAsync(item.AssetId, ct);
            await movements.RecordGatePassExitAsync(pass, asset, gate.Name, null, now, ct, gate.Id, OfficerName(user), notes);
        }
        audit.Record(user, "gate_pass", passId, pass.AssetId, "gate_exit", "items", null, chosen.Count, notes);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await gatePasses.GetAsync(passId, ct);
    }

    /// <summary>SG-03 / SI-04: items coming back (some or all); each returns to its holder or into store.</summary>
    public async Task<GatePassDto> RecordReturnAsync(AuthUser user, int passId, int gateLocationId, IReadOnlyList<GateReturnInput> returns, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var pass = await LoadPassForGateAsync(user, passId, ct);
        var gate = await LoadGateAsync(user, gateLocationId, ct);
        var items = await db.GatePassItems.Where(i => i.GatePassId == passId).OrderBy(i => i.Id).ToListAsync(ct);
        var byAsset = returns.ToDictionary(r => r.AssetId);
        var chosen = Choose(items, returns.Count == 0 ? null : returns.Select(r => r.AssetId).ToList(),
            i => i.ExitedAt is not null && i.ReturnedAt is null, "is not out under this pass");
        foreach (var item in chosen)
        {
            var input = byAsset.GetValueOrDefault(item.AssetId);
            var asset = await LockAssetAsync(item.AssetId, ct);
            var condition = input?.Condition;
            db.Movements.Add(new Movement
            {
                AssetId = asset.Id,
                FromLocationId = null,
                ToLocationId = gate.Id,
                TriggeredBy = MovementTriggers.Manual,
                GatePassId = passId,
                MovedBy = OfficerName(user),
                Notes = $"Returned through {gate.Name} under gate pass #{passId}{(condition is null ? "" : $" ({condition})")}{(input?.Notes is null ? "" : $" — {input.Notes}")}",
                Timestamp = now,
            });
            if (asset.Status == AssetStatuses.OutOnGatePass)
                statuses.TrySystemChange(user, asset, new StatusChange(AssetStatusService.ReturnStatus(asset), $"Returned through {gate.Name} (gate pass #{passId})"));
            if (condition is not null && audit.RecordChange(user, "asset", asset.Id, asset.Id, "condition", asset.Condition, condition, $"Returned under gate pass #{passId}"))
                asset.Condition = condition;
            asset.CurrentLocationId = gate.Id;
            asset.UpdatedAt = now;
            item.ReturnedAt = now;
            item.ReturnGateId = gate.Id;
            item.ReturnOfficer = OfficerName(user);
            item.ReturnCondition = condition;
            item.ReturnNotes = input?.Notes;
        }
        await db.SaveChangesAsync(ct);
        // The overdue flags of the items now back are resolved.
        var back = chosen.Select(i => i.AssetId).ToList();
        await db.AiInsights.IgnoreQueryFilters()
            .Where(i => i.GatePassId == passId && i.AssetId != null && back.Contains(i.AssetId.Value) && i.Type == InsightTypes.MissingAsset && !i.Dismissed)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Dismissed, true), ct);
        await transaction.CommitAsync(ct);
        return await gatePasses.GetAsync(passId, ct);
    }

    /// <summary>SG-02: open unauthorised-movement alerts at the caller's SBU's gates, with what has been done about each.</summary>
    public async Task<List<GateAlertDto>> AlertsAsync(AuthUser user, CancellationToken ct)
    {
        var query = db.AiInsights.IgnoreQueryFilters().AsNoTracking()
            .Where(i => !i.Dismissed && i.AssetId != null && (i.Type == InsightTypes.UnauthorizedMovement || i.Type == InsightTypes.MissingAsset && i.GatePassId != null));
        if (Roles.IsSbuScoped(user.Role)) query = query.Where(i => db.Assets.IgnoreQueryFilters().Any(a => a.Id == i.AssetId && a.SbuId == user.SbuId));
        var rows = await query.OrderByDescending(i => i.CreatedAt).Take(200)
            .Select(i => new { i.Id, i.AssetId, i.Type, i.Severity, i.Message, i.GatePassId, i.CreatedAt }).ToListAsync(ct);
        var assetIds = rows.Select(r => r.AssetId!.Value).Distinct().ToList();
        var assets = await db.Assets.IgnoreQueryFilters().AsNoTracking().Where(a => assetIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Name, a.RfidTag, a.AssetNumber }).ToDictionaryAsync(a => a.Id, ct);
        var ids = rows.Select(r => r.Id).ToList();
        var actions = (await db.GateActions.AsNoTracking().Where(a => a.InsightId != null && ids.Contains(a.InsightId.Value)).OrderBy(a => a.At).ToListAsync(ct))
            .ToLookup(a => a.InsightId!.Value);
        return rows.Select(r => new GateAlertDto
        {
            InsightId = r.Id,
            AssetId = r.AssetId!.Value,
            AssetName = assets[r.AssetId.Value].Name,
            RfidTag = assets[r.AssetId.Value].RfidTag,
            AssetNumber = assets[r.AssetId.Value].AssetNumber,
            Type = r.Type,
            Severity = r.Severity,
            Message = r.Message,
            GatePassId = r.GatePassId,
            CreatedAt = r.CreatedAt,
            Actions = actions[r.Id].Select(ToDto).ToList(),
        }).ToList();
    }

    /// <summary>
    /// SG-02: what the Gate Officer did about an alert. Stopped: the item stays (noted in its history). Released with
    /// override: the reason is required, the exit is recorded and the SBU Admin Manager (and SBU IT for an IT item) is told.
    /// Escalated: the reason is required; the same people are told and the alert stays open.
    /// </summary>
    public async Task<GateActionDto> ActAsync(AuthUser user, int insightId, string action, string? notes, int? gateLocationId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var insight = await db.AiInsights.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == insightId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Alert not found");
        if (insight.AssetId is null) throw new ApiException(StatusCodes.Status409Conflict, "This alert is not about an item");
        var asset = await db.Assets.IgnoreQueryFilters().Include(a => a.AssetCategory).FirstAsync(a => a.Id == insight.AssetId, ct);
        if (!AccessPolicy.Sys(user) && Roles.IsSbuScoped(user.Role) && !AccessPolicy.OwnSbu(user, asset.SbuId))
            throw AccessPolicy.Deny(user, "act on another SBU's alerts");
        if (insight.Dismissed) throw new ApiException(StatusCodes.Status409Conflict, "This alert has already been closed");
        if (action != GateActions.Stopped && string.IsNullOrWhiteSpace(notes))
            throw new ApiException(StatusCodes.Status400BadRequest, "notes: give the reason (who authorised the release, or why it is escalated)");
        var gate = gateLocationId is { } g ? await LoadGateAsync(user, g, ct) : null;
        var officer = OfficerName(user);

        var row = new GateAction
        {
            InsightId = insight.Id, AssetId = asset.Id, GateLocationId = gate?.Id ?? (insight.GatePassId is null ? null : asset.CurrentLocationId),
            Action = action, Notes = notes?.Trim(), Officer = officer, At = now,
        };
        db.GateActions.Add(row);
        var label = action switch
        {
            GateActions.Stopped => "Stopped at the gate",
            GateActions.ReleasedWithOverride => "Released at the gate with an override",
            _ => "Escalated by the Gate Officer",
        };
        db.AssetLifecycleEvents.Add(new AssetLifecycleEvent
        {
            AssetId = asset.Id, EventType = LifecycleEventTypes.Note, PerformedBy = officer,
            Notes = $"{label}{(gate is null ? "" : $" ({gate.Name})")}{(row.Notes is null ? "" : $": {row.Notes}")}", Timestamp = now,
        });
        if (action == GateActions.ReleasedWithOverride)
        {
            // ASSUMPTION: an override release records the exit (off the premises) but no gate pass, so the status is unchanged.
            insight.Dismissed = true;
            db.Movements.Add(new Movement
            {
                AssetId = asset.Id, FromLocationId = asset.CurrentLocationId, ToLocationId = null, TriggeredBy = MovementTriggers.Manual,
                GatePassId = insight.GatePassId, MovedBy = officer, Notes = $"Released with override by {officer}: {row.Notes}", Timestamp = now,
            });
            asset.CurrentLocationId = null;
            asset.UpdatedAt = now;
        }
        audit.Record(user, "gate_action", null, asset.Id, $"gate_{action}", null, null, null, row.Notes);
        await db.SaveChangesAsync(ct);
        if (action != GateActions.Stopped)
        {
            var roles = asset.AssetCategory.IsIt ? new[] { Roles.SbuAdmin, Roles.SbuIt } : [Roles.SbuAdmin];
            var people = await db.Users.Where(u => u.Active && roles.Contains(u.Role) && u.SbuId == asset.SbuId).ToListAsync(ct);
            notifications.Notify(people, "gate", $"{label}: {asset.Name}", row.Notes ?? insight.Message, $"/assets/{asset.Id}/card");
            await db.SaveChangesAsync(ct);
        }
        return ToDto(row);
    }

    /// <summary>GI-06: items still out after their pass's return date.</summary>
    public async Task<List<GateOverdueDto>> OverdueAsync(AuthUser? user, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, config.TimeZone)).ToString("yyyy-MM-dd");
        var query = from i in db.GatePassItems.AsNoTracking()
                    join p in db.GatePasses.IgnoreQueryFilters() on i.GatePassId equals p.Id
                    join a in db.Assets.IgnoreQueryFilters() on i.AssetId equals a.Id
                    where i.ExitedAt != null && i.ReturnedAt == null && p.ReturnDate != null && string.Compare(p.ReturnDate, today) < 0
                    select new { i, p, a };
        if (user is not null && Roles.IsSbuScoped(user.Role)) query = query.Where(x => x.a.SbuId == user.SbuId);
        return await query.OrderBy(x => x.p.ReturnDate).Select(x => new GateOverdueDto
        {
            GatePassId = x.p.Id, AssetId = x.a.Id, AssetName = x.a.Name, RfidTag = x.a.RfidTag, AssetNumber = x.a.AssetNumber, SbuId = x.a.SbuId,
            RequestedBy = x.p.RequestedBy, Destination = x.p.Destination, ReturnDate = x.p.ReturnDate!, ExitedAt = x.i.ExitedAt!.Value,
        }).ToListAsync(ct);
    }

    /// <summary>GI-06: flag each overdue item once (the flag stays open until it is back); the SBU Admin Manager is told.</summary>
    public async Task<int> FlagOverdueAsync(CancellationToken ct)
    {
        var raised = 0;
        foreach (var item in await OverdueAsync(null, ct))
        {
            var row = await insights.RaiseAsync(new InsightDraft("gate_return_overdue", InsightTypes.MissingAsset, InsightSeverities.Warning,
                $"Overdue: \"{item.AssetName}\" was due back on {item.ReturnDate} under gate pass #{item.GatePassId} ({item.RequestedBy})",
                item.AssetId, item.GatePassId), InsightDedupe.Message, ct);
            if (row is null) continue;
            raised++;
            var admins = await db.Users.Where(u => u.Active && (u.Role == Roles.SbuAdmin || u.Role == Roles.GateOfficer) && u.SbuId == item.SbuId).ToListAsync(ct);
            notifications.Notify(admins, "gate", $"Not back: {item.AssetName}", row.Message, "/gate");
            await db.SaveChangesAsync(ct);
        }
        return raised;
    }

    /// <summary>Passes from before Phase 4c (one asset each) get their item row.</summary>
    public async Task EnsureItemsAsync(int passId, CancellationToken ct)
    {
        if (await db.GatePassItems.AnyAsync(i => i.GatePassId == passId, ct)) return;
        var pass = await db.GatePasses.IgnoreQueryFilters().AsNoTracking().FirstAsync(p => p.Id == passId, ct);
        var exited = await db.Movements.IgnoreQueryFilters()
            .Where(m => m.GatePassId == passId && m.TriggeredBy == MovementTriggers.GatePass).Select(m => (DateTime?)m.Timestamp).MaxAsync(ct);
        db.GatePassItems.Add(new GatePassItem { GatePassId = passId, AssetId = pass.AssetId, ExitedAt = exited });
        await db.SaveChangesAsync(ct);
    }

    private static List<GatePassItem> Choose(List<GatePassItem> items, IReadOnlyList<int>? assetIds, Func<GatePassItem, bool> open, string why)
    {
        if (assetIds is null || assetIds.Count == 0)
        {
            var all = items.Where(open).ToList();
            if (all.Count == 0) throw new ApiException(StatusCodes.Status409Conflict, $"Every item of this pass {why}");
            return all;
        }
        var chosen = new List<GatePassItem>();
        foreach (var id in assetIds.Distinct())
        {
            var item = items.FirstOrDefault(i => i.AssetId == id)
                ?? throw new ApiException(StatusCodes.Status400BadRequest, $"Asset {id} is not on this gate pass");
            if (!open(item)) throw new ApiException(StatusCodes.Status409Conflict, $"Asset {id} {why}");
            chosen.Add(item);
        }
        return chosen;
    }

    private async Task<GatePass> LoadPassForGateAsync(AuthUser user, int passId, CancellationToken ct)
    {
        var pass = await db.GatePasses.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.Id == passId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Gate pass not found");
        var sbuId = await db.Assets.IgnoreQueryFilters().Where(a => a.Id == pass.AssetId).Select(a => a.SbuId).FirstAsync(ct);
        if (!AccessPolicy.Sys(user) && Roles.IsSbuScoped(user.Role) && !AccessPolicy.OwnSbu(user, sbuId))
            throw AccessPolicy.Deny(user, "record exits and returns of another SBU's items");
        return pass;
    }

    private async Task<Location> LoadGateAsync(AuthUser user, int gateLocationId, CancellationToken ct)
    {
        var gate = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == gateLocationId, ct)
            ?? throw new ApiException(StatusCodes.Status400BadRequest, "gateLocationId: no such location");
        if (gate.Type != LocationTypes.Gate) throw new ApiException(StatusCodes.Status400BadRequest, $"gateLocationId: \"{gate.Name}\" is not a gate");
        if (!AccessPolicy.Sys(user) && Roles.IsSbuScoped(user.Role) && !AccessPolicy.OwnSbu(user, gate.SbuId))
            throw AccessPolicy.Deny(user, "record at another SBU's gate");
        return gate;
    }

    private async Task<Asset> LockAssetAsync(int assetId, CancellationToken ct) =>
        (await db.Assets.FromSql($"SELECT * FROM assets WHERE id = {assetId} FOR UPDATE").IgnoreQueryFilters().Include(a => a.AssetCategory).ToListAsync(ct)).First();

    private static string OfficerName(AuthUser user) => user.Name.Length > 0 ? user.Name : user.Username;

    private static GateActionDto ToDto(GateAction a) => new()
    {
        Id = a.Id, InsightId = a.InsightId, AssetId = a.AssetId, GateLocationId = a.GateLocationId, Action = a.Action, Notes = a.Notes, Officer = a.Officer, At = a.At,
    };
}
