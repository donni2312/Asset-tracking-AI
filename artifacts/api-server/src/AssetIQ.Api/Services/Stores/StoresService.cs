using System.Security.Cryptography;
using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services.Approvals;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services.Stores;

public sealed record PoLineInput(int CategoryId, string Description, int Quantity, decimal? UnitCost);
public sealed record ReceiptLineInput(int PurchaseOrderLineId, int QuantityReceived, int QuantityDamaged, IReadOnlyList<string> Serials, string? Condition);
public sealed record EquipmentRequestInput(int? EmployeeId, int? DepartmentId, int CategoryId, string ItemType, int Quantity, string Reason, string Urgency, string Source);

/// <summary>
/// Stores (Phase 4b, ST-01 … ST-05): purchase orders entered in AssetIQ until Business Central's feed replaces them
/// (product owner, 29 Oct 2026), receiving against them with short / damaged deliveries flagged, registration of the
/// received items (ST-02), the stock view (ST-03), equipment requests (LM-05, HR-02) approved through the approvals
/// engine, issue only against an approved request with the recipient's acknowledgement (ST-04, HR-03), and returns
/// (ST-05, with the IT rule).
/// </summary>
public sealed class StoresService(
    AssetIqDbContext db, AppClock clock, AuditTrail audit, ApprovalService approvals, NotificationService notifications, AssetStatusService statuses,
    AssetRegistrationService registration, WorkflowService workflows)
{
    // ── purchase orders (entered by SBU Finance) ─────────────────────────────

    public async Task<PurchaseOrder> CreatePurchaseOrderAsync(AuthUser user, int sbuId, string poNo, string supplier, IReadOnlyList<PoLineInput> lines, CancellationToken ct)
    {
        if (!AccessPolicy.IsSbuFinanceOf(user, sbuId) && user.Role != Roles.GroupFinance) throw AccessPolicy.Deny(user, "enter purchase orders for this SBU");
        if (lines.Count == 0) throw new ApiException(StatusCodes.Status400BadRequest, "lines: at least one line");
        if (lines.Any(l => l.Quantity <= 0)) throw new ApiException(StatusCodes.Status400BadRequest, "lines: quantities must be greater than 0");
        var number = poNo.Trim().ToUpperInvariant();
        if (await db.PurchaseOrders.AnyAsync(p => p.PoNo == number, ct)) throw new ApiException(StatusCodes.Status409Conflict, "That PO number already exists");
        var categories = lines.Select(l => l.CategoryId).Distinct().ToList();
        if (await db.AssetCategories.CountAsync(c => categories.Contains(c.Id), ct) != categories.Count) throw new ApiException(StatusCodes.Status404NotFound, "Category not found");
        var po = new PurchaseOrder
        {
            SbuId = sbuId, PoNo = number, Supplier = supplier.Trim(), CreatedBy = user.Name, CreatedAt = clock.UtcNow,
            Lines = lines.Select(l => new PurchaseOrderLine { CategoryId = l.CategoryId, Description = l.Description.Trim(), Quantity = l.Quantity, UnitCost = l.UnitCost }).ToList(),
        };
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync(ct);
        audit.Record(user, "purchase_order", po.Id, null, "created", "poNo", null, po.PoNo, $"{po.Supplier}, {lines.Sum(l => l.Quantity)} items");
        await db.SaveChangesAsync(ct);
        return po;
    }

    /// <summary>ST-01: count a delivery against the PO; short or damaged deliveries are flagged to SBU Finance (Procurement).</summary>
    // ASSUMPTION: there is no Procurement role; SBU Finance, who entered the PO, receives the short / damaged flag.
    public async Task<GoodsReceipt> ReceiveAsync(AuthUser user, int purchaseOrderId, IReadOnlyList<ReceiptLineInput> lines, string? notes, CancellationToken ct)
    {
        var po = await db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == purchaseOrderId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Purchase order not found");
        if (!(user.Role == Roles.Stores && AccessPolicy.OwnSbu(user, po.SbuId)) && !AccessPolicy.Sys(user)) throw AccessPolicy.Deny(user, "receive goods for this SBU (Stores, ST-01)");
        if (po.Status is "received" or "closed") throw new ApiException(StatusCodes.Status409Conflict, $"PO {po.PoNo} is fully received");
        var problems = new List<string>();
        foreach (var l in lines)
        {
            var poLine = po.Lines.FirstOrDefault(x => x.Id == l.PurchaseOrderLineId);
            if (poLine is null) { problems.Add($"line {l.PurchaseOrderLineId}: not a line of {po.PoNo}"); continue; }
            if (l.QuantityReceived < 0 || l.QuantityDamaged < 0 || l.QuantityDamaged > l.QuantityReceived) problems.Add($"line {l.PurchaseOrderLineId}: quantities");
            if (poLine.ReceivedQuantity + l.QuantityReceived > poLine.Quantity) problems.Add($"line {l.PurchaseOrderLineId}: more than ordered ({poLine.Quantity})");
        }
        if (problems.Count > 0) throw new ApiException(StatusCodes.Status400BadRequest, string.Join("; ", problems));

        var receipt = new GoodsReceipt { PurchaseOrderId = po.Id, ReceivedBy = user.Name, Notes = notes, ReceivedAt = clock.UtcNow };
        var flags = new List<string>();
        foreach (var l in lines)
        {
            var poLine = po.Lines.First(x => x.Id == l.PurchaseOrderLineId);
            poLine.ReceivedQuantity += l.QuantityReceived;
            receipt.Lines.Add(new GoodsReceiptLine
            {
                PurchaseOrderLineId = poLine.Id, QuantityReceived = l.QuantityReceived, QuantityDamaged = l.QuantityDamaged,
                Serials = JsonSerializer.Serialize(l.Serials.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0)), Condition = l.Condition,
            });
            if (l.QuantityDamaged > 0) flags.Add($"{l.QuantityDamaged} damaged: {poLine.Description}");
        }
        foreach (var poLine in po.Lines.Where(x => x.ReceivedQuantity < x.Quantity))
            flags.Add($"short {poLine.Quantity - poLine.ReceivedQuantity}: {poLine.Description}");
        receipt.Flagged = flags.Count > 0;
        receipt.FlagReason = flags.Count > 0 ? string.Join("; ", flags) : null;
        po.Status = po.Lines.All(x => x.ReceivedQuantity >= x.Quantity) ? "received" : "partly_received";
        db.GoodsReceipts.Add(receipt);
        await db.SaveChangesAsync(ct);
        audit.Record(user, "goods_receipt", receipt.Id, null, "goods_received", "purchaseOrder", null, po.PoNo, receipt.FlagReason);
        if (receipt.Flagged)
        {
            var finance = await db.Users.Where(u => u.Active && u.Role == Roles.SbuFinance && u.SbuId == po.SbuId).ToListAsync(ct);
            notifications.Notify(finance, "receiving", $"Delivery against {po.PoNo} flagged", receipt.FlagReason!, "/stores");
        }
        await db.SaveChangesAsync(ct);
        return receipt;
    }

    /// <summary>ST-02: checks a received line still has items to register; returns the PO number and supplier for the asset.</summary>
    public async Task<(string PoNo, string Supplier)> ReceiptLineForRegistrationAsync(int receiptLineId, int sbuId, int categoryId, CancellationToken ct)
    {
        var line = await db.GoodsReceiptLines.FirstOrDefaultAsync(l => l.Id == receiptLineId, ct) ?? throw new ApiException(StatusCodes.Status404NotFound, "Receipt line not found");
        var poLine = await db.PurchaseOrderLines.FirstAsync(l => l.Id == line.PurchaseOrderLineId, ct);
        var po = await db.PurchaseOrders.FirstAsync(p => p.Id == poLine.PurchaseOrderId, ct);
        if (po.SbuId != sbuId) throw new ApiException(StatusCodes.Status400BadRequest, "receiptLineId: the delivery is another SBU's");
        if (poLine.CategoryId != categoryId) throw new ApiException(StatusCodes.Status400BadRequest, "receiptLineId: the PO line is for another category");
        if (line.Registered >= line.QuantityReceived - line.QuantityDamaged) throw new ApiException(StatusCodes.Status409Conflict, "Every good item of this delivery line is already registered");
        return (po.PoNo, po.Supplier);
    }

    /// <summary>ST-02 / ST-03: one more item of the line registered; SBU Finance is told to capitalise it.</summary>
    public async Task MarkRegisteredAsync(int receiptLineId, int assetId, string assetName, CancellationToken ct)
    {
        var line = await db.GoodsReceiptLines.FirstAsync(l => l.Id == receiptLineId, ct);
        line.Registered++;
        var poLine = await db.PurchaseOrderLines.FirstAsync(l => l.Id == line.PurchaseOrderLineId, ct);
        var po = await db.PurchaseOrders.FirstAsync(p => p.Id == poLine.PurchaseOrderId, ct);
        var finance = await db.Users.Where(u => u.Active && u.Role == Roles.SbuFinance && u.SbuId == po.SbuId).ToListAsync(ct);
        notifications.Notify(finance, "capitalise", $"Received and registered: {assetName}", $"From {po.PoNo} ({po.Supplier}). Capitalise it (SF-01).", $"/assets/{assetId}/card");
        await db.SaveChangesAsync(ct);
    }

    // ── equipment requests (LM-05, HR-02) ───────────────────────────────────

    /// <summary>
    /// LM-05: a request for equipment for an employee or department. Line manager first (skipped when the HOD asks for
    /// their own department), then SBU Finance; once approved, Stores and IT are told to issue it.
    /// </summary>
    public async Task<EquipmentRequest> RequestEquipmentAsync(AuthUser user, EquipmentRequestInput input, CancellationToken ct)
    {
        var sbuId = user.SbuId ?? throw AccessPolicy.Deny(user, "request equipment (an SBU role is needed)");
        int? department = input.DepartmentId;
        if (input.EmployeeId is { } eid)
        {
            var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eid, ct) ?? throw new ApiException(StatusCodes.Status404NotFound, "Employee not found");
            if (employee.SbuId != sbuId) throw new ApiException(StatusCodes.Status400BadRequest, "employeeId: another SBU's employee");
            department ??= employee.DepartmentId;
        }
        if (user.Role == Roles.Hod && department != user.DepartmentId) throw AccessPolicy.Deny(user, "request equipment for another department");
        if (input.Quantity <= 0) throw new ApiException(StatusCodes.Status400BadRequest, "quantity: must be greater than 0");
        var category = await db.AssetCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == input.CategoryId, ct) ?? throw new ApiException(StatusCodes.Status404NotFound, "Category not found");

        var request = new EquipmentRequest
        {
            SbuId = sbuId, EmployeeId = input.EmployeeId, DepartmentId = department, CategoryId = category.Id, ItemType = input.ItemType.Trim(), Quantity = input.Quantity,
            Reason = input.Reason.Trim(), Urgency = input.Urgency, Source = input.Source, RequestedBy = user.Name, RequestedByUserId = user.Id, CreatedAt = clock.UtcNow,
        };
        db.EquipmentRequests.Add(request);
        await db.SaveChangesAsync(ct);
        List<StepSpec> steps = [];
        if (!(user.Role == Roles.Hod && department == user.DepartmentId)) steps.Add(new(1, StepRoles.LineManager, sbuId, department, "Line manager (LM-03)"));
        steps.Add(new(steps.Count + 1, Roles.SbuFinance, sbuId, null, "SBU Finance"));
        await approvals.OpenAsync(ApprovalKinds.Equipment, null, sbuId, request.Id, $"{request.Quantity} × {request.ItemType} ({category.Name}): {request.Reason}",
            new { request.ItemType, request.Quantity, request.Urgency, category.IsIt }, steps, user, user.Name, ct);
        return request;
    }

    internal async Task EquipmentDecidedAsync(ApprovalRequest approval, bool approved, CancellationToken ct)
    {
        var request = await db.EquipmentRequests.FirstAsync(r => r.Id == approval.SubjectId, ct);
        request.Status = approved ? "approved" : "rejected";
        if (!approved) return;
        var fulfil = await db.Users.Where(u => u.Active && u.SbuId == request.SbuId && (u.Role == Roles.Stores || u.Role == Roles.SbuIt)).ToListAsync(ct);
        notifications.Notify(fulfil, "issue", $"Approved request to issue: {request.Quantity} × {request.ItemType}", request.Reason, "/stores");
    }

    /// <summary>
    /// ST-04: issue an item against an approved request. The item must be in store, tagged and numbered (ST-02); the
    /// recipient acknowledges it in the app or by the emailed link (HR-03).
    /// </summary>
    public async Task<AssetAcknowledgement> IssueAsync(AuthUser user, int requestId, int assetId, int? employeeId, CancellationToken ct)
    {
        var request = await db.EquipmentRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct) ?? throw new ApiException(StatusCodes.Status404NotFound, "Equipment request not found");
        if (request.Status != "approved") throw new ApiException(StatusCodes.Status409Conflict, $"Items are issued only against an approved request (ST-04); this one is {request.Status}");
        var asset = await workflows.LoadAsync(assetId, ct);
        if (asset.SbuId != request.SbuId) throw new ApiException(StatusCodes.Status400BadRequest, "assetId: the item is another SBU's");
        if (!AccessPolicy.CanRecordLifecycle(user, LifecycleEventTypes.Deployment, asset, asset.AssetCategory.IsIt)) throw AccessPolicy.Deny(user, "issue this item");
        if (asset.Status != AssetStatuses.InStore) throw new ApiException(StatusCodes.Status409Conflict, $"Only an item in store can be issued; this one is {AssetStatusMachine.Label(asset.Status)}");
        var recipient = employeeId ?? request.EmployeeId ?? throw new ApiException(StatusCodes.Status400BadRequest, "employeeId: who receives the item");

        statuses.Change(user, asset, new StatusChange(AssetStatuses.InUse, $"Issued against equipment request #{request.Id}"));
        await registration.AssignCustodianAsync(user, asset, recipient, $"Equipment request #{request.Id}", ct);
        db.AssetLifecycleEvents.Add(new AssetLifecycleEvent
        {
            AssetId = asset.Id, EventType = LifecycleEventTypes.Deployment, PerformedBy = user.Name, AssignedTo = asset.AssignedTo,
            Notes = $"Issued against equipment request #{request.Id}", Timestamp = clock.UtcNow,
        });
        request.IssuedQuantity++;
        if (request.IssuedQuantity >= request.Quantity) request.Status = "fulfilled";
        var ack = await RequestAcknowledgementAsync(user, asset, recipient, request.Id, ct);
        await db.SaveChangesAsync(ct);
        return ack;
    }

    /// <summary>HR-03: the employee signs for the item, in the app or through the emailed link; until then the toolbox shows it unacknowledged.</summary>
    public async Task<AssetAcknowledgement> RequestAcknowledgementAsync(AuthUser user, Asset asset, int employeeId, int? requestId, CancellationToken ct)
    {
        var ack = new AssetAcknowledgement
        {
            AssetId = asset.Id, EmployeeId = employeeId, EquipmentRequestId = requestId, IssuedBy = user.Name, IssuedAt = clock.UtcNow,
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
        };
        db.AssetAcknowledgements.Add(ack);
        var employee = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == employeeId, ct);
        var accounts = await db.Users.Where(u => u.Active && u.EmployeeId == employeeId).ToListAsync(ct);
        var body = $"{asset.Name} ({asset.AssetNumber}) was issued to you by {user.Name}. Please confirm you received it and accept the company asset policy before using it.";
        if (accounts.Count > 0) notifications.Notify(accounts, "acknowledgement", "Please acknowledge an item issued to you", body, "/my-items");
        else notifications.EmailOnly(employee.Email, "AssetIQ: please acknowledge an item issued to you", $"{body}\n\nConfirm: /acknowledge/{ack.Token}");
        return ack;
    }

    // ── returns (ST-05) ──────────────────────────────────────────────────────

    /// <summary>
    /// ST-05: Stores takes an item back and records its condition. Good items go back to stock; items for repair are
    /// handed to the technical owner; scrap is recommended for disposal. Stores receives IT items only once SBU IT has
    /// recommended them for disposal; other IT returns go to SBU IT.
    /// </summary>
    public async Task<Asset> ReceiveReturnAsync(AuthUser user, int assetId, string condition, string? notes, CancellationToken ct)
    {
        var asset = await workflows.LoadAsync(assetId, ct);
        if (!(user.Role == Roles.Stores && AccessPolicy.OwnSbu(user, asset.SbuId)) && !AccessPolicy.Sys(user)) throw AccessPolicy.Deny(user, "receive returns for this SBU (Stores)");
        var sbuCode = await db.Sbus.Where(s => s.Id == asset.SbuId).Select(s => s.Code).FirstOrDefaultAsync(ct);
        if (asset.AssetCategory.IsIt)
        {
            if (asset.Status != AssetStatuses.PendingDisposal)
                throw new ApiException(StatusCodes.Status409Conflict, "Stores receives an IT item only once SBU IT has recommended it for disposal (ST-05); return it to SBU IT");
            ClearHolder(asset, $"{sbuCode} Stores");
            audit.Record(user, "asset", asset.Id, asset.Id, "returned_to_stores", "condition", asset.Condition, condition, notes ?? "Held for disposal");
            asset.Condition = condition == "scrap" ? "damaged" : asset.Condition;
            await db.SaveChangesAsync(ct);
            return asset;
        }
        if (asset.Status != AssetStatuses.InUse) throw new ApiException(StatusCodes.Status409Conflict, $"Only an item in use can be returned; this one is {AssetStatusMachine.Label(asset.Status)}");
        statuses.Change(user, asset, new StatusChange(AssetStatuses.InStore, $"Returned to Stores ({condition}){(notes is null ? "" : $": {notes}")}"));
        ClearHolder(asset, $"{sbuCode} Stores");
        if (audit.RecordChange(user, "asset", asset.Id, asset.Id, "condition", asset.Condition, condition == "good" ? "good" : condition == "repair" ? "poor" : "damaged", notes))
            asset.Condition = condition == "good" ? "good" : condition == "repair" ? "poor" : "damaged";
        await db.SaveChangesAsync(ct);
        if (condition == "repair")
        {
            var admins = await db.Users.Where(u => u.Active && u.Role == Roles.SbuAdmin && u.SbuId == asset.SbuId).ToListAsync(ct);
            notifications.Notify(admins, "repair", $"Returned for repair: {asset.Name}", notes ?? "Returned to Stores needing repair", $"/assets/{asset.Id}/card");
            await db.SaveChangesAsync(ct);
        }
        if (condition == "scrap") await workflows.RecommendDisposalAsync(user, asset.Id, new DisposalRequestInput(notes ?? "Returned to Stores as scrap", "damaged"), ct);
        return asset;
    }

    private static void ClearHolder(Asset asset, string holder)
    {
        asset.CustodianEmployeeId = null;
        asset.DepartmentId = null;
        asset.AssignedTo = holder;
    }
}

/// <summary>An approved equipment request is handed to Stores / IT to issue.</summary>
public sealed class EquipmentApprovalHandler(StoresService stores) : IApprovalHandler
{
    public string Kind => ApprovalKinds.Equipment;
    public Task BeforeApproveAsync(AuthUser user, ApprovalRequest request, ApprovalStep step, JsonBody input, CancellationToken ct) => Task.CompletedTask;
    public Task OnApprovedAsync(AuthUser user, ApprovalRequest request, string? comment, CancellationToken ct) => stores.EquipmentDecidedAsync(request, true, ct);
    public Task OnRejectedAsync(AuthUser user, ApprovalRequest request, string? comment, CancellationToken ct) => stores.EquipmentDecidedAsync(request, false, ct);
}
