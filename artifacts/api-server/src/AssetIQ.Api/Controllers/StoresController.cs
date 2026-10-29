using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using AssetIQ.Api.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Controllers;

/// <summary>Phase 4b: purchase orders and receiving, stock, equipment requests and issue, returns, HR toolbox, departments.</summary>
public sealed class StoresController(AssetIqDbContext db, StoresService stores, ToolboxService toolbox, AccessLog accessLog) : ApiControllerBase
{
    // ── purchase orders and receiving (ST-01) ───────────────────────────────

    [HttpGet("purchase-orders")]
    [RequireRole(Roles.StoresReaders)]
    [FailureMessage("Failed to list purchase orders")]
    public async Task<List<PurchaseOrderDto>> PurchaseOrders([FromQuery] string? status)
    {
        var query = db.PurchaseOrders.AsNoTracking();
        if (Roles.IsSbuScoped(Caller.Role)) query = query.Where(p => p.SbuId == Caller.SbuId);
        if (RequestValues.OptionalText(status) is { } s) query = query.Where(p => p.Status == s);
        var ids = await query.OrderByDescending(p => p.CreatedAt).Select(p => p.Id).Take(200).ToListAsync(Aborted);
        var list = new List<PurchaseOrderDto>();
        foreach (var id in ids) list.Add(await PurchaseOrderAsync(id));
        return list;
    }

    [HttpGet("purchase-orders/{id}")]
    [RequireRole(Roles.StoresReaders)]
    [FailureMessage("Failed to load the purchase order")]
    public async Task<PurchaseOrderDto> PurchaseOrder(string id)
    {
        var po = await PurchaseOrderAsync(RequestValues.Id(id));
        if (Roles.IsSbuScoped(Caller.Role) && po.SbuId != Caller.SbuId) throw AccessPolicy.Deny(Caller, "see another SBU's purchase orders");
        return po;
    }

    [HttpPost("purchase-orders")]
    [RequireRole(Roles.FinanceWriters)]
    [FailureMessage("Failed to create the purchase order")]
    public async Task<IActionResult> CreatePurchaseOrder([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var sbuId = input.OptionalInt("sbuId") ?? Caller.SbuId ?? 0;
        var poNo = input.RequiredString("poNo", minLength: 2);
        var supplier = input.RequiredString("supplier", minLength: 2);
        input.ThrowIfInvalid();
        var lines = new List<PoLineInput>();
        if (body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty("lines", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var l in arr.EnumerateArray())
            {
                var line = JsonBody.From(l);
                lines.Add(new PoLineInput(line.RequiredInt("categoryId"), line.RequiredString("description", minLength: 2), line.RequiredInt("quantity"), line.OptionalNumber("unitCost")));
                line.ThrowIfInvalid();
            }
        }
        var po = await stores.CreatePurchaseOrderAsync(Caller, sbuId, poNo, supplier, lines, Aborted);
        return Status201(await PurchaseOrderAsync(po.Id));
    }

    /// <summary>ST-01: lines = [{ purchaseOrderLineId, quantityReceived, quantityDamaged, serials: [...], condition }].</summary>
    [HttpPost("purchase-orders/{id}/receipts")]
    [RequireRole(Roles.Stores)]
    [FailureMessage("Failed to record the delivery")]
    public async Task<IActionResult> Receive(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var notes = input.OptionalString("notes");
        input.ThrowIfInvalid();
        var lines = new List<ReceiptLineInput>();
        if (body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty("lines", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var l in arr.EnumerateArray())
            {
                var line = JsonBody.From(l);
                var serials = l.TryGetProperty("serials", out var s) && s.ValueKind == JsonValueKind.Array
                    ? s.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : [];
                lines.Add(new ReceiptLineInput(line.RequiredInt("purchaseOrderLineId"), line.RequiredInt("quantityReceived"), line.OptionalInt("quantityDamaged") ?? 0,
                    serials, line.OptionalString("condition")));
                line.ThrowIfInvalid();
            }
        }
        if (lines.Count == 0) throw new ApiException(StatusCodes.Status400BadRequest, "lines: at least one line");
        var poId = RequestValues.Id(id);
        await stores.ReceiveAsync(Caller, poId, lines, notes, Aborted);
        return Status201(await PurchaseOrderAsync(poId));
    }

    /// <summary>ST-03: items in store by category, type and location (the caller's scope).</summary>
    [HttpGet("stores/stock")]
    [RequireRole(Roles.StoresReaders)]
    [FailureMessage("Failed to load the stock")]
    public async Task<List<StockLineDto>> Stock()
    {
        var rows = await db.Assets.AsNoTracking().Where(a => a.Status == AssetStatuses.InStore)
            .Select(a => new { Sbu = a.Sbu!.Code, Category = a.AssetCategory.Name, a.SubCategory, Location = a.CurrentLocation!.Name, Untagged = a.RfidTag == null })
            .ToListAsync(Aborted);
        return rows.GroupBy(r => (r.Sbu, r.Category, r.SubCategory, r.Location))
            .Select(g => new StockLineDto(g.Key.Sbu, g.Key.Category, g.Key.SubCategory, g.Key.Location, g.Count(), g.Count(x => x.Untagged)))
            .OrderBy(s => s.SbuCode).ThenBy(s => s.Category).ThenBy(s => s.SubCategory).ToList();
    }

    // ── equipment requests and issue (LM-05, HR-02, ST-04) ──────────────────

    [HttpGet("equipment-requests")]
    [RequireRole(Roles.Everyone)]
    [FailureMessage("Failed to list equipment requests")]
    public async Task<List<EquipmentRequestDto>> EquipmentRequests([FromQuery] string? status)
    {
        var query = db.EquipmentRequests.AsNoTracking();
        if (Caller.Role == Roles.Hod) query = query.Where(r => r.DepartmentId == Caller.DepartmentId);
        else if (Caller.Role == Roles.Employee) query = query.Where(r => r.EmployeeId == Caller.EmployeeId);
        else if (Roles.IsSbuScoped(Caller.Role)) query = query.Where(r => r.SbuId == Caller.SbuId);
        if (RequestValues.OptionalText(status) is { } s) query = query.Where(r => r.Status == s);
        return await query.OrderByDescending(r => r.CreatedAt).Take(200)
            .Select(r => new EquipmentRequestDto(r.Id, r.SbuId, r.EmployeeId, db.Employees.Where(e => e.Id == r.EmployeeId).Select(e => e.Name).FirstOrDefault(),
                r.DepartmentId, db.Departments.Where(d => d.Id == r.DepartmentId).Select(d => d.Name).FirstOrDefault(), r.CategoryId,
                db.AssetCategories.Where(c => c.Id == r.CategoryId).Select(c => c.Name).First(), r.ItemType, r.Quantity, r.IssuedQuantity, r.Reason, r.Urgency,
                r.Status, r.Source, r.RequestedBy, r.CreatedAt))
            .ToListAsync(Aborted);
    }

    [HttpPost("equipment-requests")]
    [RequireRole(Roles.EquipmentRequesters)]
    [FailureMessage("Failed to request equipment")]
    public async Task<IActionResult> RequestEquipment([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var request = new EquipmentRequestInput(input.OptionalInt("employeeId"), input.OptionalInt("departmentId"), input.RequiredInt("categoryId"),
            input.RequiredString("itemType", minLength: 2), input.OptionalInt("quantity") ?? 1, input.RequiredString("reason", minLength: 3),
            input.OptionalEnum("urgency", ["low", "normal", "high"]) ?? "normal", Caller.Role == Roles.SbuHr ? "onboarding" : "request");
        input.ThrowIfInvalid();
        var created = await stores.RequestEquipmentAsync(Caller, request, Aborted);
        return Status201((await EquipmentRequests(null)).First(r => r.Id == created.Id));
    }

    [HttpPost("equipment-requests/{id}/issue")]
    [RequireRole(Roles.LifecycleWriters)]
    [FailureMessage("Failed to issue the item")]
    public async Task<IActionResult> Issue(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var assetId = input.RequiredInt("assetId");
        var employeeId = input.OptionalInt("employeeId");
        input.ThrowIfInvalid();
        var ack = await stores.IssueAsync(Caller, RequestValues.Id(id), assetId, employeeId, Aborted);
        return Status201(await AcknowledgementAsync(ack.Id));
    }

    // ── returns (ST-05) ─────────────────────────────────────────────────────

    [HttpPost("assets/{id}/returns")]
    [RequireRole(Roles.Stores)]
    [FailureMessage("Failed to record the return")]
    public async Task<IActionResult> Return(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var condition = input.RequiredEnum("condition", ["good", "repair", "scrap"]);
        var notes = input.OptionalString("notes");
        input.ThrowIfInvalid();
        var asset = await stores.ReceiveReturnAsync(Caller, RequestValues.Id(id), condition, notes, Aborted);
        return Ok(new { asset.Id, asset.Status, asset.AssignedTo, asset.Condition });
    }

    // ── acknowledgements (HR-03) ────────────────────────────────────────────

    [HttpGet("acknowledgements")]
    [RequireRole(Roles.Everyone)]
    [FailureMessage("Failed to list acknowledgements")]
    public async Task<List<AcknowledgementDto>> Acknowledgements([FromQuery] string? employeeId)
    {
        var target = RequestValues.OptionalInt(employeeId) ?? Caller.EmployeeId ?? 0;
        if (target != Caller.EmployeeId)
        {
            var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == target, Aborted) ?? throw new ApiException(StatusCodes.Status404NotFound, "Employee not found");
            if (!AccessPolicy.CanViewPerson(Caller, employee.SbuId, employee.DepartmentId)) throw AccessPolicy.Deny(Caller, "see this employee's acknowledgements");
        }
        var ids = await db.AssetAcknowledgements.AsNoTracking().Where(k => k.EmployeeId == target).OrderByDescending(k => k.Id).Select(k => k.Id).ToListAsync(Aborted);
        var list = new List<AcknowledgementDto>();
        foreach (var ack in ids) list.Add(await AcknowledgementAsync(ack));
        return list;
    }

    [HttpPost("acknowledgements/{id}/accept")]
    [RequireRole(Roles.Everyone)]
    [FailureMessage("Failed to record the acknowledgement")]
    public async Task<AcknowledgementDto> Accept(string id)
    {
        var ackId = RequestValues.Id(id);
        await toolbox.AcknowledgeAsync(Caller, ackId, Aborted);
        return await AcknowledgementAsync(ackId);
    }

    /// <summary>HR-03 by the emailed link: no sign-in, the token is the proof.</summary>
    [HttpPost("acknowledgements/accept-link")]
    [AllowAnonymous]
    [FailureMessage("Failed to record the acknowledgement")]
    public async Task<IActionResult> AcceptByLink([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var token = input.RequiredString("token", minLength: 16);
        input.ThrowIfInvalid();
        await toolbox.AcknowledgeByTokenAsync(token.Trim(), Aborted);
        return NoContent();
    }

    // ── toolbox and exit clearance (HR-01, HR-04) ───────────────────────────

    [HttpGet("employees/{id}/toolbox")]
    [RequireRole(Roles.Everyone)]
    [FailureMessage("Failed to load the toolbox")]
    public Task<ToolboxDto> Toolbox(string id) => toolbox.ToolboxAsync(Caller, RequestValues.Id(id), Aborted);

    [HttpPost("employees/{id}/exit-clearance")]
    [RequireRole(Roles.SbuHr)]
    [FailureMessage("Failed to start the exit clearance")]
    public async Task<IActionResult> StartExit(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var reason = input.RequiredEnum("reason", ["resignation", "termination", "retirement", "transfer"]);
        input.ThrowIfInvalid();
        var clearance = await toolbox.StartExitAsync(Caller, RequestValues.Id(id), reason, Aborted);
        return Status201(await toolbox.ClearanceAsync(Caller, clearance.Id, Aborted));
    }

    [HttpGet("exit-clearances/{id}")]
    [RequireRole(Roles.PeopleReaders)]
    [FailureMessage("Failed to load the exit clearance")]
    public Task<ExitClearanceDto> Clearance(string id) => toolbox.ClearanceAsync(Caller, RequestValues.Id(id), Aborted);

    [HttpPost("exit-clearances/{id}/items/{itemId}")]
    [RequireRole(Roles.ClearanceRecorders)]
    [FailureMessage("Failed to record the item")]
    public async Task<ExitClearanceDto> RecordItem(string id, string itemId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var outcome = input.RequiredEnum("outcome", ["returned", "damaged", "missing"]);
        var notes = input.OptionalString("notes");
        input.ThrowIfInvalid();
        await toolbox.RecordExitItemAsync(Caller, RequestValues.Id(id), RequestValues.Id(itemId), outcome, notes, Aborted);
        return await toolbox.ClearanceAsync(Caller, RequestValues.Id(id), Aborted);
    }

    [HttpPost("exit-clearances/{id}/complete")]
    [RequireRole(Roles.SbuHr)]
    [FailureMessage("Failed to complete the exit clearance")]
    public async Task<ExitClearanceDto> CompleteExit(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var approvedBy = input.OptionalString("exceptionApprovedBy");
        var reason = input.OptionalString("exceptionReason");
        input.ThrowIfInvalid();
        await toolbox.CompleteExitAsync(Caller, RequestValues.Id(id), approvedBy, reason, Aborted);
        return await toolbox.ClearanceAsync(Caller, RequestValues.Id(id), Aborted);
    }

    // ── departments (LM-01, LM-02) ──────────────────────────────────────────

    [HttpGet("departments/{id}/holdings")]
    [RequireRole(Roles.PeopleReaders)]
    [FailureMessage("Failed to load the department")]
    public Task<DepartmentHoldingsDto> Holdings(string id) => toolbox.HoldingsAsync(Caller, RequestValues.Id(id), Aborted);

    [HttpGet("departments/{id}/holdings.xlsx")]
    [RequireRole(Roles.PeopleReaders)]
    [FailureMessage("Failed to export the department")]
    public async Task<IActionResult> HoldingsExcel(string id)
    {
        var holdings = await toolbox.HoldingsAsync(Caller, RequestValues.Id(id), Aborted);
        await accessLog.ExportAsync(Caller, $"department holdings {holdings.DepartmentName}", Request, Aborted);
        return Download(TableExport.Excel(ToolboxService.HoldingsTable(holdings)), TableExport.ExcelContentType, $"department-{holdings.DepartmentId}.xlsx");
    }

    // ── mapping ─────────────────────────────────────────────────────────────

    private async Task<PurchaseOrderDto> PurchaseOrderAsync(int id)
    {
        var po = await db.PurchaseOrders.AsNoTracking().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Purchase order not found");
        var receipts = await db.GoodsReceipts.AsNoTracking().Include(r => r.Lines).Where(r => r.PurchaseOrderId == id).OrderBy(r => r.Id).ToListAsync(Aborted);
        var categories = await db.AssetCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, Aborted);
        var sbu = await db.Sbus.AsNoTracking().Where(s => s.Id == po.SbuId).Select(s => s.Code).FirstAsync(Aborted);
        var money = AccessPolicy.CanSeeFinancials(Caller);
        var lines = po.Lines.OrderBy(l => l.Id).ToList();
        return new PurchaseOrderDto(po.Id, po.SbuId, sbu, po.PoNo, po.Supplier, po.Status, po.CreatedBy, po.CreatedAt,
            lines.Select(l => new PurchaseOrderLineDto(l.Id, l.CategoryId, categories.GetValueOrDefault(l.CategoryId, ""), l.Description, l.Quantity, l.ReceivedQuantity,
                money ? l.UnitCost : null)).ToList(),
            receipts.Select(r => new GoodsReceiptDto(r.Id, r.ReceivedAt, r.ReceivedBy, r.Flagged, r.FlagReason, r.Notes,
                r.Lines.Select(l => new GoodsReceiptLineDto(l.Id, l.PurchaseOrderLineId, lines.FirstOrDefault(x => x.Id == l.PurchaseOrderLineId)?.Description ?? "",
                    l.QuantityReceived, l.QuantityDamaged, JsonSerializer.Deserialize<List<string>>(l.Serials) ?? [], l.Condition, l.Registered)).ToList())).ToList());
    }

    private async Task<AcknowledgementDto> AcknowledgementAsync(int id) =>
        // Across scopes on purpose: an employee sees what was issued to them (the query filter applies to the whole query).
        await db.AssetAcknowledgements.IgnoreQueryFilters().AsNoTracking().Where(k => k.Id == id)
            .Select(k => new AcknowledgementDto(k.Id, k.AssetId, db.Assets.Where(a => a.Id == k.AssetId).Select(a => a.AssetNumber).FirstOrDefault(),
                db.Assets.Where(a => a.Id == k.AssetId).Select(a => a.Name).First(), k.EmployeeId, k.IssuedBy, k.IssuedAt, k.AcknowledgedAt, k.Method))
            .FirstAsync(Aborted);
}
