using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services.Stores;

/// <summary>
/// Phase 4b, HR toolbox and departments: an employee's toolbox (HR-01) with what they have acknowledged (HR-03),
/// exit clearance built from it (HR-04; returned items go back to the SBU's Stores, IT ones to SBU IT per ST-05), and
/// what a department holds, by person (LM-01, LM-02). No cost or value is shown (money stays with Finance).
/// </summary>
public sealed class ToolboxService(AssetIqDbContext db, AppClock clock, AuditTrail audit, NotificationService notifications, AssetStatusService statuses,
    AssetIQ.Api.Services.Depreciation.DepreciationService depreciation)
{
    public async Task<ToolboxDto> ToolboxAsync(AuthUser user, int employeeId, CancellationToken ct)
    {
        var employee = await EmployeeAsync(user, employeeId, ct);
        var held = await db.Assets.IgnoreQueryFilters().AsNoTracking().Include(a => a.AssetCategory)
            .Where(a => a.CustodianEmployeeId == employeeId).OrderBy(a => a.Name).ToListAsync(ct);
        var acks = await db.AssetAcknowledgements.AsNoTracking().Where(k => k.EmployeeId == employeeId).OrderByDescending(k => k.Id).ToListAsync(ct);
        var items = held.Select(a =>
        {
            var ack = acks.FirstOrDefault(k => k.AssetId == a.Id);
            return new ToolboxItemDto(a.Id, a.AssetNumber, a.Name, a.RfidTag, a.AssetCategory.Name, a.Condition, a.Status, ack?.IssuedAt, ack?.Id, ack?.AcknowledgedAt);
        }).ToList();
        // Past items: the audit trail records every change of holder.
        var key = employeeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var past = await db.AuditLog.AsNoTracking()
            .Where(e => e.Field == "custodianEmployeeId" && e.OldValue == key && e.AssetId != null)
            .OrderByDescending(e => e.At).Select(e => new { e.AssetId, e.At }).ToListAsync(ct);
        var pastIds = past.Select(p => p.AssetId!.Value).Distinct().Where(id => held.All(h => h.Id != id)).ToList();
        var names = await db.Assets.IgnoreQueryFilters().AsNoTracking().Where(a => pastIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => (a.AssetNumber, a.Name), ct);
        var history = pastIds.Select(id => new ToolboxHistoryDto(id, names[id].AssetNumber, names[id].Name, past.First(p => p.AssetId == id).At)).ToList();
        return new ToolboxDto(employee.Id, employee.EmployeeNo, employee.Name, employee.JobRole, employee.Active, items, history,
            await db.ExitClearances.AsNoTracking().Where(c => c.EmployeeId == employeeId && c.Status == "open").Select(c => (int?)c.Id).FirstOrDefaultAsync(ct));
    }

    /// <summary>HR-03 in the app: the employee signed in as themselves.</summary>
    public async Task AcknowledgeAsync(AuthUser user, int acknowledgementId, CancellationToken ct)
    {
        var ack = await db.AssetAcknowledgements.FirstOrDefaultAsync(k => k.Id == acknowledgementId, ct) ?? throw new ApiException(StatusCodes.Status404NotFound, "Acknowledgement not found");
        if (user.EmployeeId != ack.EmployeeId) throw AccessPolicy.Deny(user, "acknowledge an item issued to someone else");
        await AcceptAsync(ack, "app", user, ct);
    }

    /// <summary>HR-03 by the emailed link (no sign-in; the token is the proof).</summary>
    public async Task AcknowledgeByTokenAsync(string token, CancellationToken ct)
    {
        var ack = await db.AssetAcknowledgements.FirstOrDefaultAsync(k => k.Token == token, ct) ?? throw new ApiException(StatusCodes.Status404NotFound, "This link is not valid");
        await AcceptAsync(ack, "email", null, ct);
    }

    private async Task AcceptAsync(AssetAcknowledgement ack, string method, AuthUser? user, CancellationToken ct)
    {
        if (ack.AcknowledgedAt is not null) return;
        ack.AcknowledgedAt = clock.UtcNow;
        ack.Method = method;
        audit.Record(user, "acknowledgement", ack.Id, ack.AssetId, "acknowledged", "method", null, method, "Received and accepted the company asset policy (HR-03)");
        await db.SaveChangesAsync(ct);
    }

    // ── exit clearance (HR-04) ───────────────────────────────────────────────

    public async Task<ExitClearance> StartExitAsync(AuthUser user, int employeeId, string reason, CancellationToken ct)
    {
        var employee = await EmployeeAsync(user, employeeId, ct);
        if (!AccessPolicy.CanManagePeople(user, employee.SbuId)) throw AccessPolicy.Deny(user, "start an exit clearance (SBU HR)");
        if (await db.ExitClearances.AnyAsync(c => c.EmployeeId == employeeId && c.Status == "open", ct)) throw new ApiException(StatusCodes.Status409Conflict, "The employee already has an open exit clearance");
        var assets = await db.Assets.IgnoreQueryFilters().Where(a => a.CustodianEmployeeId == employeeId).Select(a => a.Id).ToListAsync(ct);
        var clearance = new ExitClearance
        {
            EmployeeId = employeeId, Reason = reason, CreatedBy = user.Name, CreatedAt = clock.UtcNow,
            Items = assets.Select(id => new ExitClearanceItem { AssetId = id }).ToList(),
        };
        db.ExitClearances.Add(clearance);
        await db.SaveChangesAsync(ct);
        audit.Record(user, "exit_clearance", clearance.Id, null, "started", "items", null, assets.Count, $"{employee.Name}: {reason}");
        await db.SaveChangesAsync(ct);
        return clearance;
    }

    public async Task<ExitClearanceDto> ClearanceAsync(AuthUser user, int clearanceId, CancellationToken ct)
    {
        var clearance = await db.ExitClearances.AsNoTracking().Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == clearanceId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Exit clearance not found");
        var employee = await EmployeeAsync(user, clearance.EmployeeId, ct);
        var ids = clearance.Items.Select(i => i.AssetId).ToList();
        var assets = await db.Assets.IgnoreQueryFilters().AsNoTracking().Include(a => a.AssetCategory).Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        // HR-05: the value of what is outstanding reaches Finance only (money stays with Finance).
        var nbv = new Dictionary<int, decimal>();
        if (AccessPolicy.CanSeeFinancials(user))
        {
            foreach (var (id, lines) in await depreciation.SchedulesAsync(ids, depreciation.CurrentPeriod, ct))
                if (lines.Count > 0) nbv[id] = lines[^1].ClosingNbv;
            foreach (var id in ids.Where(i => !nbv.ContainsKey(i))) nbv[id] = assets[id].CostUsd ?? 0m;
        }
        return new ExitClearanceDto(clearance.Id, employee.Id, employee.Name, clearance.Reason, clearance.Status, clearance.ExceptionApprovedBy, clearance.ExceptionReason,
            clearance.CreatedAt, clearance.CompletedAt,
            clearance.Items.OrderBy(i => i.Id).Select(i => new ExitClearanceItemDto(i.Id, i.AssetId, assets[i.AssetId].AssetNumber, assets[i.AssetId].Name,
                assets[i.AssetId].AssetCategory.IsIt, i.Outcome, i.RecordedBy, i.RecordedAt, i.Notes, i.RecoveryOutcome, i.RecoveryApprovedBy, i.RecoveryNote,
                nbv.TryGetValue(i.AssetId, out var value) ? value : null)).ToList());
    }

    /// <summary>
    /// Records what happened to one item: returned or damaged (back to the SBU's Stores; IT items to SBU IT, ST-05) or
    /// missing (lost / stolen, SBU Finance notified, HR-05). SBU HR records any item; SBU IT its IT items (SI-09), the
    /// Admin Manager and Stores the non-IT ones.
    /// </summary>
    public async Task RecordExitItemAsync(AuthUser user, int clearanceId, int itemId, string outcome, string? notes, CancellationToken ct)
    {
        var clearance = await db.ExitClearances.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == clearanceId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Exit clearance not found");
        if (clearance.Status != "open") throw new ApiException(StatusCodes.Status409Conflict, "The exit clearance is complete");
        var item = clearance.Items.FirstOrDefault(i => i.Id == itemId) ?? throw new ApiException(StatusCodes.Status404NotFound, "Item not found");
        var asset = await db.Assets.IgnoreQueryFilters().Include(a => a.AssetCategory).FirstAsync(a => a.Id == item.AssetId, ct);
        var employee = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == clearance.EmployeeId, ct);
        var own = AccessPolicy.OwnSbu(user, employee.SbuId);
        var allowed = AccessPolicy.Sys(user) || (own && (user.Role == Roles.SbuHr
            || (asset.AssetCategory.IsIt ? user.Role == Roles.SbuIt : user.Role is Roles.SbuAdmin or Roles.Stores)));
        if (!allowed) throw AccessPolicy.Deny(user, "record this item of the exit clearance");
        if (item.Outcome != "outstanding") throw new ApiException(StatusCodes.Status409Conflict, $"The item is already recorded as {item.Outcome}");

        var sbuCode = await db.Sbus.Where(s => s.Id == employee.SbuId).Select(s => s.Code).FirstOrDefaultAsync(ct);
        if (outcome is "returned" or "damaged")
        {
            statuses.TrySystemChange(user, asset, new StatusChange(AssetStatuses.InStore, $"Returned at exit clearance #{clearance.Id}"));
            audit.RecordChange(user, "asset", asset.Id, asset.Id, "custodianEmployeeId", asset.CustodianEmployeeId, null, $"Exit clearance #{clearance.Id}");
            asset.CustodianEmployeeId = null;
            asset.DepartmentId = null;
            asset.AssignedTo = AssetStatusService.StoreHolder(asset, sbuCode);
            if (outcome == "damaged") asset.Condition = "damaged";
        }
        else
        {
            statuses.TrySystemChange(user, asset, new StatusChange(AssetStatuses.LostStolen, $"Missing at exit clearance #{clearance.Id}",
                clock.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), user.Name, notes ?? $"Not returned by {employee.Name} at exit"));
            var finance = await db.Users.Where(u => u.Active && u.Role == Roles.SbuFinance && u.SbuId == employee.SbuId).ToListAsync(ct);
            notifications.Notify(finance, "loss", $"Missing at exit: {asset.Name}", $"{employee.Name} did not return {asset.AssetNumber}. Recovery per policy (HR-05).", $"/assets/{asset.Id}/card");
        }
        item.Outcome = outcome;
        item.RecordedBy = user.Name;
        item.RecordedAt = clock.UtcNow;
        item.Notes = notes;
        audit.Record(user, "exit_clearance", clearance.Id, asset.Id, "item_recorded", "outcome", "outstanding", outcome, notes);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// HR-05: for a missing or damaged item, SBU HR records the outcome under the company's recovery policy (recovered,
    /// written off, cost recovery) with the approver; SBU Finance is notified. A recovered item goes back to store.
    /// </summary>
    public async Task RecordRecoveryAsync(AuthUser user, int clearanceId, int itemId, string outcome, string approvedBy, string? note, CancellationToken ct)
    {
        var clearance = await db.ExitClearances.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == clearanceId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Exit clearance not found");
        var employee = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == clearance.EmployeeId, ct);
        if (!(AccessPolicy.Sys(user) || user.Role == Roles.SbuHr && AccessPolicy.OwnSbu(user, employee.SbuId)))
            throw AccessPolicy.Deny(user, "record the recovery outcome (SBU HR does, HR-05)");
        var item = clearance.Items.FirstOrDefault(i => i.Id == itemId) ?? throw new ApiException(StatusCodes.Status404NotFound, "Item not found");
        if (item.Outcome is not ("missing" or "damaged")) throw new ApiException(StatusCodes.Status409Conflict, "Only a missing or damaged item has a recovery outcome");
        if (item.RecoveryOutcome is not null) throw new ApiException(StatusCodes.Status409Conflict, $"Already recorded as {item.RecoveryOutcome}");
        var asset = await db.Assets.IgnoreQueryFilters().Include(a => a.AssetCategory).FirstAsync(a => a.Id == item.AssetId, ct);
        if (outcome == "recovered" && item.Outcome == "missing")
        {
            var sbuCode = await db.Sbus.Where(s => s.Id == employee.SbuId).Select(s => s.Code).FirstOrDefaultAsync(ct);
            statuses.TrySystemChange(user, asset, new StatusChange(AssetStatuses.InStore, $"Recovered after exit clearance #{clearance.Id}"));
            asset.AssignedTo = AssetStatusService.StoreHolder(asset, sbuCode);
        }
        item.RecoveryOutcome = outcome;
        item.RecoveryApprovedBy = approvedBy;
        item.RecoveryNote = note;
        item.RecoveryRecordedBy = user.Name;
        item.RecoveryRecordedAt = clock.UtcNow;
        audit.Record(user, "exit_clearance", clearance.Id, asset.Id, "recovery_recorded", "recoveryOutcome", null, outcome, $"Approved by {approvedBy}{(note is null ? "" : $": {note}")}");
        var finance = await db.Users.Where(u => u.Active && u.Role == Roles.SbuFinance && u.SbuId == employee.SbuId).ToListAsync(ct);
        notifications.Notify(finance, "loss", $"Recovery outcome for {asset.Name}: {outcome.Replace('_', ' ')}",
            $"{employee.Name}, exit clearance #{clearance.Id}. Approved by {approvedBy}.", $"/assets/{asset.Id}/card");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>HR-04: complete only when nothing is outstanding, unless SBU HR records an approved exception; the employee then leaves.</summary>
    public async Task CompleteExitAsync(AuthUser user, int clearanceId, string? exceptionApprovedBy, string? exceptionReason, CancellationToken ct)
    {
        var clearance = await db.ExitClearances.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == clearanceId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Exit clearance not found");
        var employee = await db.Employees.FirstAsync(e => e.Id == clearance.EmployeeId, ct);
        if (!AccessPolicy.CanManagePeople(user, employee.SbuId)) throw AccessPolicy.Deny(user, "complete an exit clearance (SBU HR)");
        if (clearance.Status != "open") throw new ApiException(StatusCodes.Status409Conflict, "The exit clearance is already complete");
        var outstanding = clearance.Items.Count(i => i.Outcome == "outstanding");
        if (outstanding > 0 && (string.IsNullOrWhiteSpace(exceptionApprovedBy) || string.IsNullOrWhiteSpace(exceptionReason)))
            throw new ApiException(StatusCodes.Status409Conflict, $"{outstanding} item(s) are still outstanding; return them, or record an approved exception (who approved it and why)");
        clearance.Status = "completed";
        clearance.CompletedAt = clock.UtcNow;
        clearance.ExceptionApprovedBy = outstanding > 0 ? exceptionApprovedBy!.Trim() : null;
        clearance.ExceptionReason = outstanding > 0 ? exceptionReason!.Trim() : null;
        audit.Record(user, "exit_clearance", clearance.Id, null, "completed", "outstanding", null, outstanding,
            outstanding > 0 ? $"Exception approved by {clearance.ExceptionApprovedBy}: {clearance.ExceptionReason}" : null);
        // The employee has left; items still held under an approved exception stay on record against them.
        if (audit.RecordChange(user, "employee", employee.Id, null, "active", employee.Active, false, $"Exit clearance #{clearance.Id}")) employee.Active = false;
        await db.SaveChangesAsync(ct);
    }

    // ── departments (LM-01, LM-02) ──────────────────────────────────────────

    public async Task<DepartmentHoldingsDto> HoldingsAsync(AuthUser user, int departmentId, CancellationToken ct)
    {
        var department = await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == departmentId, ct) ?? throw new ApiException(StatusCodes.Status404NotFound, "Department not found");
        if (!AccessPolicy.CanViewPerson(user, department.SbuId, department.Id)) throw AccessPolicy.Deny(user, "see this department");
        var employees = await db.Employees.AsNoTracking().Where(e => e.DepartmentId == departmentId && e.Active).OrderBy(e => e.Name).ToListAsync(ct);
        var assets = await db.Assets.IgnoreQueryFilters().AsNoTracking().Include(a => a.AssetCategory).Include(a => a.CurrentLocation)
            .Where(a => a.DepartmentId == departmentId && a.Status != AssetStatuses.Disposed).OrderBy(a => a.Name).ToListAsync(ct);
        var items = assets.Select(a => new HoldingItemDto(a.Id, a.AssetNumber, a.Name, a.AssetCategory.Name, a.AssetCategory.IsIt, a.AssignedTo, a.CustodianEmployeeId,
            a.Status, a.CurrentLocation?.Name, a.Condition)).ToList();
        var people = employees.Select(e => new EmployeeHoldingDto(e.Id, e.Name, e.JobRole, items.Count(i => i.EmployeeId == e.Id))).ToList();
        return new DepartmentHoldingsDto(department.Id, department.Name, items, people);
    }

    public static ExportTable HoldingsTable(DepartmentHoldingsDto h) => new(
        $"Department holdings — {h.DepartmentName}", [$"{h.Items.Count} items, {h.Employees.Count} people"],
        [new("Asset no."), new("Description"), new("Category"), new("Holder"), new("Status"), new("Location"), new("Condition")],
        h.Items.Select(i => new ExportRow([i.AssetNumber, i.Description, i.Category, i.Holder, AssetStatusMachine.Label(i.Status), i.Location, i.Condition])).ToList());

    private async Task<Employee> EmployeeAsync(AuthUser user, int employeeId, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct) ?? throw new ApiException(StatusCodes.Status404NotFound, "Employee not found");
        if (user.EmployeeId != employeeId && !AccessPolicy.CanViewPerson(user, employee.SbuId, employee.DepartmentId)) throw AccessPolicy.Deny(user, "see this employee's toolbox");
        return employee;
    }
}
