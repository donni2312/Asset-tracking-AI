using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>
/// Phase 4b: Stores (ST-01 … ST-05) receiving against POs entered in AssetIQ, registration, stock, issue only against an
/// approved equipment request (LM-05), the employee's acknowledgement (HR-03), returns with the IT rule, the HR toolbox
/// (HR-01) and exit clearance (HR-04), and department holdings (LM-01, LM-02).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class StoresAndToolboxTests(ApiFixture api) : IAsyncLifetime
{
    private RoleWorld r = null!;

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        r = await RoleWorld.SeedAsync(api);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string?> StatusOf(int assetId) =>
        (await api.CallAsync("GET", $"/assets/{assetId}", token: r[Roles.GroupFinance])).Body.Str("status");

    private Task<ApiResponse> Decide(int requestId, string role, object body) => api.CallAsync("POST", $"/approval-requests/{requestId}/decision", body, r[role]);

    private async Task<int> InboxRequestAsync(string role, string kind) =>
        (await api.CallAsync("GET", "/approvals", token: r[role])).Body.Items().Single(i => i.Str("type") == kind).Int("requestId");

    private async Task<ApiResponse> PurchaseOrderAsync() =>
        await api.CallAsync("POST", "/purchase-orders", new
        {
            poNo = "po-tsas-2026-0200", supplier = "Axis Computers",
            lines = new object[]
            {
                new { categoryId = Categories.Computers, description = "Dell Latitude 5450", quantity = 2, unitCost = 1250m },
                new { categoryId = Categories.Furniture, description = "Office chair", quantity = 1 },
            },
        }, r[Roles.SbuFinance]);

    [Fact]
    public async Task ST_01_stores_receives_against_the_po_and_a_short_or_damaged_delivery_is_flagged_to_finance()
    {
        var po = await PurchaseOrderAsync();
        Assert.Equal((201, "PO-TSAS-2026-0200", "open"), (po.Status, po.Body.Str("poNo"), po.Body.Str("status")));
        var lines = po.Body.Prop("lines").Items();
        Assert.True((await api.CallAsync("GET", $"/purchase-orders/{po.Body.Int("id")}", token: r[Roles.Stores])).Body.Prop("lines").Items()[0].IsNull("unitCost"));
        Assert.Equal(403, (await api.CallAsync("POST", "/purchase-orders", new { poNo = "X-1", supplier = "Y", lines = new object[] { new { categoryId = 1, description = "x", quantity = 1 } } }, r[Roles.Stores])).Status);

        var tooMany = await api.CallAsync("POST", $"/purchase-orders/{po.Body.Int("id")}/receipts",
            new { lines = new object[] { new { purchaseOrderLineId = lines[0].Int("id"), quantityReceived = 3 } } }, r[Roles.Stores]);
        Assert.Equal(400, tooMany.Status);
        Assert.Equal(403, (await api.CallAsync("POST", $"/purchase-orders/{po.Body.Int("id")}/receipts",
            new { lines = new object[] { new { purchaseOrderLineId = lines[0].Int("id"), quantityReceived = 1 } } }, r[Roles.SbuIt])).Status);

        var received = await api.CallAsync("POST", $"/purchase-orders/{po.Body.Int("id")}/receipts", new
        {
            lines = new object[] { new { purchaseOrderLineId = lines[0].Int("id"), quantityReceived = 2, quantityDamaged = 1, serials = new[] { "sn-r-1", "SN-R-2" }, condition = "new" } },
        }, r[Roles.Stores]);
        Assert.Equal((201, "partly_received"), (received.Status, received.Body.Str("status")));
        var receipt = received.Body.Prop("receipts").Items().Single();
        Assert.True(receipt.Bool("flagged"));
        Assert.Equal("1 damaged: Dell Latitude 5450; short 1: Office chair", receipt.Str("flagReason"));
        Assert.Contains((await api.CallAsync("GET", "/notifications", token: r[Roles.SbuFinance])).Body.Items(), n => n.Str("kind") == "receiving");
    }

    [Fact]
    public async Task ST_02_and_ST_03_received_items_are_registered_from_the_delivery_finance_is_told_to_capitalise_and_stock_shows_them()
    {
        var po = await PurchaseOrderAsync();
        var line = po.Body.Prop("lines").Items()[0];
        var received = await api.CallAsync("POST", $"/purchase-orders/{po.Body.Int("id")}/receipts",
            new { lines = new object[] { new { purchaseOrderLineId = line.Int("id"), quantityReceived = 2, quantityDamaged = 1 } } }, r[Roles.Stores]);
        var receiptLine = received.Body.Prop("receipts").Items().Single().Prop("lines").Items().Single().Int("id");
        object Laptop(string serial) => new
        {
            categoryId = Categories.Computers, subCategory = "Laptop", currentLocationId = r.W.Office.Id, assignedTo = "TSAS IT store", condition = "new", receiptLineId = receiptLine,
            attributes = new { make = "Dell", model = "Latitude 5450", serial_number = serial, processor = "i5", ram = 16, storage = "512 GB SSD", operating_system = "Windows 11" },
        };
        var created = await api.CallAsync("POST", "/assets", Laptop("SN-R-1"), r[Roles.Stores]);
        Assert.True(created.Status == 201, created.Error());
        Assert.Equal(("PO-TSAS-2026-0200", "Axis Computers"), (created.Body.Str("poNo"), created.Body.Str("supplier")));
        Assert.Equal(409, (await api.CallAsync("POST", "/assets", Laptop("SN-R-2"), r[Roles.Stores])).Status); // the second one was damaged
        Assert.Contains((await api.CallAsync("GET", "/notifications", token: r[Roles.SbuFinance])).Body.Items(), n => n.Str("kind") == "capitalise");

        var stock = await api.CallAsync("GET", "/stores/stock", token: r[Roles.SbuIt]);
        Assert.Contains(stock.Body.Items(), s => s.Str("subCategory") == "Laptop" && s.Int("count") >= 1 && s.Int("untagged") >= 1);
    }

    [Fact]
    public async Task LM_05_ST_04_HR_03_an_approved_request_is_issued_and_the_employee_acknowledges_it()
    {
        // SBU IT asks for a projector for John Doe: his line manager, then SBU Finance.
        var request = await api.CallAsync("POST", "/equipment-requests", new { employeeId = r.TsasStaff.Id, categoryId = Categories.Peripherals, itemType = "Projector", reason = "Client presentations" }, r[Roles.SbuIt]);
        Assert.Equal(201, request.Status);
        var requestId = request.Body.Int("id");
        Assert.Equal(409, (await api.CallAsync("POST", $"/equipment-requests/{requestId}/issue", new { assetId = r.W.Projector.Id }, r[Roles.SbuIt])).Status); // not approved yet

        Assert.Equal(200, (await Decide(await InboxRequestAsync(Roles.Hod, "equipment"), Roles.Hod, new { decision = "approve" })).Status);
        Assert.Equal(200, (await Decide(await InboxRequestAsync(Roles.SbuFinance, "equipment"), Roles.SbuFinance, new { decision = "approve" })).Status);
        Assert.Contains((await api.CallAsync("GET", "/notifications", token: r[Roles.Stores])).Body.Items(), n => n.Str("kind") == "issue");

        Assert.Equal(409, (await api.CallAsync("POST", $"/assets/{r.W.Projector.Id}/lifecycle", new { eventType = "deployment", performedBy = "Clerk", custodianEmployeeId = r.TsasStaff.Id }, r[Roles.Stores])).Status);
        var issued = await api.CallAsync("POST", $"/equipment-requests/{requestId}/issue", new { assetId = r.W.Projector.Id }, r[Roles.Stores]);
        Assert.Equal(201, issued.Status);
        Assert.Equal("in_use", await StatusOf(r.W.Projector.Id));
        Assert.Equal("fulfilled", (await api.CallAsync("GET", "/equipment-requests", token: r[Roles.SbuIt])).Body.Items().Single(x => x.Int("id") == requestId).Str("status"));

        var mine = await api.CallAsync("GET", "/acknowledgements", token: r[Roles.Employee]);
        var ack = mine.Body.Items().Single(a => a.Int("assetId") == r.W.Projector.Id);
        Assert.True(ack.IsNull("acknowledgedAt"));
        Assert.Equal(403, (await api.CallAsync("POST", $"/acknowledgements/{ack.Int("id")}/accept", new { }, r[Roles.SbuIt])).Status);
        var accepted = await api.CallAsync("POST", $"/acknowledgements/{ack.Int("id")}/accept", new { }, r[Roles.Employee]);
        Assert.Equal(("app", false), (accepted.Body.Str("method"), accepted.Body.IsNull("acknowledgedAt")));
        var toolbox = await api.CallAsync("GET", $"/employees/{r.TsasStaff.Id}/toolbox", token: r[Roles.SbuHr]);
        Assert.False(toolbox.Body.Prop("items").Items().Single(i => i.Int("assetId") == r.W.Projector.Id).IsNull("acknowledgedAt"));
    }

    [Fact]
    public async Task HR_03_an_employee_without_an_account_acknowledges_by_the_emailed_link()
    {
        int newcomer;
        await using (var db = api.NewDbContext())
        {
            var employee = new Employee { EmployeeNo = "TSAS-0099", Name = "Chiedza Ndoro", SbuId = r.W.Tsas.Id, DepartmentId = r.TsasOps.Id, Email = "chiedza@tsl.co.zw", CreatedAt = api.Now, UpdatedAt = api.Now };
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
            newcomer = employee.Id;
        }
        Assert.Equal(201, (await api.CallAsync("POST", $"/assets/{r.W.Projector.Id}/lifecycle", new { eventType = "deployment", performedBy = "Farai", custodianEmployeeId = newcomer }, r[Roles.SbuIt])).Status);
        await using var check = api.NewDbContext();
        var email = await check.OutboundEmails.SingleAsync(e => e.ToAddress == "chiedza@tsl.co.zw");
        var token = email.Body[(email.Body.IndexOf("/acknowledge/", StringComparison.Ordinal) + "/acknowledge/".Length)..].Trim();
        Assert.Equal(404, (await api.CallAsync("POST", "/acknowledgements/accept-link", new { token = "0000000000000000bad" }, ApiFixture.Anonymous)).Status);
        Assert.Equal(204, (await api.CallAsync("POST", "/acknowledgements/accept-link", new { token }, ApiFixture.Anonymous)).Status);
        Assert.Equal("email", (await check.AssetAcknowledgements.AsNoTracking().SingleAsync(a => a.EmployeeId == newcomer)).Method);
    }

    [Fact]
    public async Task ST_05_stores_takes_back_non_it_items_and_it_items_only_once_recommended_for_disposal()
    {
        var it = await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/returns", new { condition = "good" }, r[Roles.Stores]);
        Assert.Equal(409, it.Status);
        Assert.Contains("ST-05", it.Error());
        await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/disposals", new { reason = "Dead battery and board" }, r[Roles.SbuIt]);
        var forDisposal = await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/returns", new { condition = "scrap" }, r[Roles.Stores]);
        Assert.Equal((200, "pending_disposal", "TSAS Stores"), (forDisposal.Status, forDisposal.Body.Str("status"), forDisposal.Body.Str("assignedTo")));

        var desk = await api.CallAsync("POST", $"/assets/{r.TsasDesk.Id}/returns", new { condition = "good" }, r[Roles.Stores]);
        Assert.Equal((200, "in_store", "TSAS Stores"), (desk.Status, desk.Body.Str("status"), desk.Body.Str("assignedTo")));
        Assert.Equal(403, (await api.CallAsync("POST", $"/assets/{r.TsasDesk.Id}/returns", new { condition = "good" }, r[Roles.SbuAdmin])).Status);
    }

    [Fact]
    public async Task HR_04_exit_clearance_cannot_complete_while_items_are_outstanding_and_returned_items_go_back_to_the_sbu()
    {
        var started = await api.CallAsync("POST", $"/employees/{r.TsasStaff.Id}/exit-clearance", new { reason = "resignation" }, r[Roles.SbuHr]);
        Assert.Equal(201, started.Status);
        var clearanceId = started.Body.Int("id");
        var items = started.Body.Prop("items").Items();
        Assert.Equal(2, items.Count); // the laptop and the desk
        Assert.Equal(409, (await api.CallAsync("POST", $"/exit-clearances/{clearanceId}/complete", new { }, r[Roles.SbuHr])).Status);

        var laptopItem = items.Single(i => i.Int("assetId") == r.W.Laptop.Id).Int("id");
        var deskItem = items.Single(i => i.Int("assetId") == r.TsasDesk.Id).Int("id");
        Assert.Equal(403, (await api.CallAsync("POST", $"/exit-clearances/{clearanceId}/items/{laptopItem}", new { outcome = "returned" }, r[Roles.Stores])).Status); // IT goes to SBU IT
        Assert.Equal(200, (await api.CallAsync("POST", $"/exit-clearances/{clearanceId}/items/{laptopItem}", new { outcome = "returned" }, r[Roles.SbuIt])).Status);
        var laptop = (await api.CallAsync("GET", $"/assets/{r.W.Laptop.Id}", token: r[Roles.SbuIt])).Body;
        Assert.Equal(("in_store", "TSAS IT store"), (laptop.Str("status"), laptop.Str("assignedTo")));
        Assert.Equal(200, (await api.CallAsync("POST", $"/exit-clearances/{clearanceId}/items/{deskItem}", new { outcome = "missing", notes = "Not in the office" }, r[Roles.SbuHr])).Status);
        Assert.Equal("lost_stolen", await StatusOf(r.TsasDesk.Id));
        Assert.Contains((await api.CallAsync("GET", "/notifications", token: r[Roles.SbuFinance])).Body.Items(), n => n.Str("kind") == "loss");

        var done = await api.CallAsync("POST", $"/exit-clearances/{clearanceId}/complete", new { }, r[Roles.SbuHr]);
        Assert.Equal((200, "completed"), (done.Status, done.Body.Str("status")));
        await using var db = api.NewDbContext();
        Assert.False((await db.Employees.SingleAsync(e => e.Id == r.TsasStaff.Id)).Active);
        var toolbox = await api.CallAsync("GET", $"/employees/{r.TsasStaff.Id}/toolbox", token: r[Roles.SbuHr]);
        Assert.Contains(toolbox.Body.Prop("history").Items(), h => h.Int("assetId") == r.W.Laptop.Id);
    }

    [Fact]
    public async Task HR_04_an_approved_exception_lets_hr_complete_with_items_outstanding()
    {
        var clearanceId = (await api.CallAsync("POST", $"/employees/{r.TsasStaff.Id}/exit-clearance", new { reason = "transfer" }, r[Roles.SbuHr])).Body.Int("id");
        var missingApprover = await api.CallAsync("POST", $"/exit-clearances/{clearanceId}/complete", new { exceptionReason = "Moves to BAK with the laptop" }, r[Roles.SbuHr]);
        Assert.Equal(409, missingApprover.Status);
        var done = await api.CallAsync("POST", $"/exit-clearances/{clearanceId}/complete",
            new { exceptionApprovedBy = "Group HR Director", exceptionReason = "Moves to BAK with the laptop (GI-05 transfer to follow)" }, r[Roles.SbuHr]);
        Assert.Equal(("completed", "Group HR Director"), (done.Body.Str("status"), done.Body.Str("exceptionApprovedBy")));
        Assert.Equal(403, (await api.CallAsync("POST", $"/employees/{r.BakStaff.Id}/exit-clearance", new { reason = "resignation" }, r[Roles.SbuHr])).Status);
    }

    [Fact]
    public async Task LM_01_and_LM_02_the_hod_sees_what_the_department_holds_by_person_and_exports_it()
    {
        var holdings = await api.CallAsync("GET", $"/departments/{r.TsasOps.Id}/holdings", token: r[Roles.Hod]);
        Assert.Equal(200, holdings.Status);
        Assert.Equal(new[] { r.TsasDesk.Id, r.W.Laptop.Id }.Order(), holdings.Body.Prop("items").Items().Select(i => i.Int("assetId")).Order());
        Assert.Equal(2, holdings.Body.Prop("employees").Items().Single(e => e.Int("employeeId") == r.TsasStaff.Id).Int("items"));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/departments/{r.TsasOps.Id}/holdings.xlsx");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", r[Roles.Hod]);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await api.Client.SendAsync(request)).StatusCode);
        Assert.Equal(403, (await api.CallAsync("GET", $"/departments/{r.BakOps.Id}/holdings", token: r[Roles.Hod])).Status);
    }

    [Fact]
    public async Task HR_05_hr_records_the_recovery_outcome_with_the_approver_and_only_finance_sees_the_value()
    {
        var started = await api.CallAsync("POST", $"/employees/{r.TsasStaff.Id}/exit-clearance", new { reason = "resignation" }, r[Roles.SbuHr]);
        var clearanceId = started.Body.Int("id");
        var deskItem = started.Body.Prop("items").Items().Single(i => i.Int("assetId") == r.TsasDesk.Id).Int("id");
        Assert.Equal(409, (await api.CallAsync("POST", $"/exit-clearances/{clearanceId}/items/{deskItem}/recovery", new { outcome = "cost_recovery", approvedBy = "Rudo Moyo" }, r[Roles.SbuHr])).Status); // not missing yet
        await api.CallAsync("POST", $"/exit-clearances/{clearanceId}/items/{deskItem}", new { outcome = "missing", notes = "Not in the office" }, r[Roles.SbuHr]);

        Assert.Equal(403, (await api.CallAsync("POST", $"/exit-clearances/{clearanceId}/items/{deskItem}/recovery", new { outcome = "written_off", approvedBy = "X" }, r[Roles.SbuFinance])).Status);
        var recorded = await api.CallAsync("POST", $"/exit-clearances/{clearanceId}/items/{deskItem}/recovery",
            new { outcome = "cost_recovery", approvedBy = "Rudo Moyo (HOD)", note = "Deducted from final pay per policy" }, r[Roles.SbuHr]);
        Assert.True(recorded.Status == 200, recorded.Error());
        var item = recorded.Body.Prop("items").Items().Single(i => i.Int("id") == deskItem);
        Assert.Equal(("cost_recovery", "Rudo Moyo (HOD)"), (item.Str("recoveryOutcome"), item.Str("recoveryApprovedBy")));
        Assert.True(item.IsNull("netBookValue")); // HR does not see money
        Assert.Equal(2, (await api.CallAsync("GET", "/notifications", token: r[Roles.SbuFinance])).Body.Items().Count(n => n.Str("kind") == "loss"));
        Assert.Equal(409, (await api.CallAsync("POST", $"/exit-clearances/{clearanceId}/items/{deskItem}/recovery", new { outcome = "recovered", approvedBy = "Rudo Moyo" }, r[Roles.SbuHr])).Status);

        var finance = await api.CallAsync("GET", $"/exit-clearances/{clearanceId}", token: r[Roles.SbuFinance]);
        Assert.Equal(1200m, finance.Body.Prop("items").Items().Single(i => i.Int("id") == deskItem).Prop("netBookValue").GetDecimal());
    }
}
