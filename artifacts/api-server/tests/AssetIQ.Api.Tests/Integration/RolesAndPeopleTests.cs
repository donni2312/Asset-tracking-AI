using System.Net.Http.Headers;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>
/// Phase 2 user management (GI-08), departments and employees, attachments, and the Phase 1 follow-ups the
/// product owner decided on 29 Oct 2026: Stores registers non-IT items at receipt, invoices and photos are
/// stored as attachments on the asset card, upgrade invoices are captured in any currency, the default
/// residual value is 0 and editable, and the System Administrator sets the final depreciation rates.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RolesAndPeopleTests(ApiFixture api) : IAsyncLifetime
{
    private RoleWorld r = null!;

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        r = await RoleWorld.SeedAsync(api);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── GI-08: user accounts ────────────────────────────────────────────────────

    [Fact]
    public async Task GI_08_sbu_it_manages_its_own_sbus_accounts_only_and_group_it_manages_everyone_but_auditors()
    {
        object NewUser(string username, string role, int? sbuId, int? employeeId = null) =>
            new { username, name = "New User", password = "a-long-password", role, sbuId, employeeId };
        var sbuIt = r[Roles.SbuIt];
        var created = await api.CallAsync("POST", "/users", NewUser("tsas.clerk", Roles.Stores, null), sbuIt);
        Assert.Equal(201, created.Status);
        Assert.Equal((Roles.Stores, r.W.Tsas.Id, "Stores Clerk"), (created.Body.Str("role"), created.Body.Int("sbuId"), created.Body.Str("roleName")));

        Assert.Equal(403, (await api.CallAsync("POST", "/users", NewUser("bak.clerk", Roles.Stores, r.W.Bak.Id), sbuIt)).Status);
        Assert.Equal(403, (await api.CallAsync("POST", "/users", NewUser("tsas.groupit", Roles.GroupIt, null), sbuIt)).Status);
        Assert.Equal(403, (await api.CallAsync("POST", "/users", NewUser("ext.audit", Roles.Auditor, null), r[Roles.GroupIt])).Status);
        Assert.Equal(400, (await api.CallAsync("POST", "/users", NewUser("bak.hod", Roles.Hod, r.W.Bak.Id), r[Roles.GroupIt])).Status); // no employee link
        Assert.Equal(201, (await api.CallAsync("POST", "/users", NewUser("bak.hod", Roles.Hod, r.W.Bak.Id, r.BakStaff.Id), r[Roles.GroupIt])).Status);
        Assert.Equal(201, (await api.CallAsync("POST", "/users", NewUser("sys.admin2", Roles.SystemAdmin, null), r[Roles.GroupIt])).Status);

        var users = await api.CallAsync("GET", "/users", token: sbuIt);
        Assert.All(users.Body.Items(), u => Assert.Equal(r.W.Tsas.Id, u.IntOrNull("sbuId")));

        // Promote a TSAS clerk: SBU IT may give SBU roles only; nobody changes their own role or disables themselves.
        var clerkId = created.Body.Int("id");
        Assert.Equal(200, (await api.CallAsync("PATCH", $"/users/{clerkId}", new { role = Roles.SbuAdmin, reason = "New post" }, sbuIt)).Status);
        Assert.Equal(403, (await api.CallAsync("PATCH", $"/users/{clerkId}", new { role = Roles.GroupFinance, sbuId = (int?)null }, sbuIt)).Status);
        var me = await api.CallAsync("GET", "/auth/me", token: sbuIt);
        Assert.Equal(403, (await api.CallAsync("PATCH", $"/users/{me.Body.Int("id")}", new { active = false }, sbuIt)).Status);
        var audit = await api.NewDbContext().AuditLog.Where(e => e.Entity == "user" && e.EntityId == clerkId).Select(e => e.Field + ":" + e.NewValue).ToListAsync();
        Assert.Contains("role:sbu_admin", audit);
    }

    [Fact]
    public async Task Phase2_retired_roles_cannot_sign_in_and_their_tokens_sign_nobody_in()
    {
        await using (var db = api.NewDbContext())
        {
            db.Users.Add(new UserAccount
            {
                Username = "old.operator", Name = "Old Operator", Role = "operator", Active = true, PasswordHash = new Api.Auth.PasswordHasher().Hash("old-password-1"),
                CreatedAt = api.Now, UpdatedAt = api.Now,
            });
            await db.SaveChangesAsync();
        }
        var login = await api.CallAsync("POST", "/auth/login", new { username = "old.operator", password = "old-password-1" }, ApiFixture.Anonymous);
        Assert.Equal((403, "Your account has no current role. Ask Group IT to assign one."), (login.Status, login.Error()));
        var token = await Accounts.TokenAsync(api, "admin");
        Assert.Equal(401, (await api.CallAsync("GET", "/assets", token: token)).Status);
    }

    [Fact]
    public async Task Phase2_the_signed_in_user_carries_role_scope_and_department_for_the_sidebar()
    {
        var hod = await api.CallAsync("GET", "/auth/me", token: r[Roles.Hod]);
        Assert.Equal(("Line Manager / HOD", "Tobacco Sales Administration Services · Operations", "Operations"),
            (hod.Body.Str("roleName"), hod.Body.Str("scope"), hod.Body.Str("departmentName")));
        var gate = await api.CallAsync("GET", "/auth/me", token: r[Roles.GateOfficer]);
        Assert.Equal("Tobacco Sales Administration Services (on scan only)", gate.Body.Str("scope"));
        Assert.Equal(12, (await api.CallAsync("GET", "/roles", token: r[Roles.Employee])).Body.Items().Count);
    }

    // ── departments and employees ───────────────────────────────────────────────

    [Fact]
    public async Task Phase2_sbu_hr_keeps_its_own_sbus_employees_and_the_hod_sees_their_department()
    {
        var hr = r[Roles.SbuHr];
        var created = await api.CallAsync("POST", "/employees", new { employeeNo = "tsas-0100", name = "Nyasha Gumbo", departmentId = r.TsasOps.Id, jobRole = "Driver", lineManagerId = r.TsasHodEmployee.Id }, hr);
        Assert.Equal(201, created.Status);
        Assert.Equal(("TSAS-0100", "Operations", "Rudo Moyo"), (created.Body.Str("employeeNo"), created.Body.Str("departmentName"), created.Body.Str("lineManagerName")));
        Assert.Equal(400, (await api.CallAsync("POST", "/employees", new { employeeNo = "TSAS-0101", name = "Xolani Dube", departmentId = r.BakOps.Id }, hr)).Status);
        Assert.Equal(403, (await api.CallAsync("POST", "/employees", new { employeeNo = "BAK-0100", name = "Xolani Dube", sbuId = r.W.Bak.Id }, hr)).Status);

        // The laptop's holder cannot be marked as left while holding it (HR-04 comes first).
        Assert.Equal(409, (await api.CallAsync("PATCH", $"/employees/{r.TsasStaff.Id}", new { active = false }, hr)).Status);

        var asHod = await api.CallAsync("GET", "/employees", token: r[Roles.Hod]);
        Assert.All(asHod.Body.Items(), e => Assert.Equal(r.TsasOps.Id, e.IntOrNull("departmentId")));
        var all = await api.CallAsync("GET", "/employees", token: r[Roles.GroupFinance]);
        Assert.Contains(all.Body.Items(), e => e.Str("sbuCode") == "BAK");
        Assert.All((await api.CallAsync("GET", "/employees", token: r[Roles.SbuIt])).Body.Items(), e => Assert.Equal("TSAS", e.Str("sbuCode")));
        Assert.Equal(403, (await api.CallAsync("GET", $"/employees/{r.BakStaff.Id}", token: r[Roles.SbuIt])).Status);
    }

    [Fact]
    public async Task SI_02_issuing_to_an_employee_record_sets_the_holder_and_department_and_puts_it_in_their_toolbox()
    {
        var issued = await api.CallAsync("POST", $"/assets/{r.W.Projector.Id}/lifecycle",
            new { eventType = "deployment", performedBy = "Farai", custodianEmployeeId = r.TsasStaff.Id }, r[Roles.SbuIt]);
        Assert.Equal(201, issued.Status);
        Assert.Equal("John Doe", issued.Body.Str("assignedTo"));
        var asset = await api.CallAsync("GET", $"/assets/{r.W.Projector.Id}", token: r[Roles.Employee]); // now theirs to see
        Assert.Equal(200, asset.Status);
        var bakEmployee = await api.CallAsync("PATCH", $"/assets/{r.W.Laptop.Id}", new { custodianEmployeeId = r.BakStaff.Id }, r[Roles.SbuIt]);
        Assert.Equal(400, bakEmployee.Status);
    }

    // ── AM-01 follow-up: Stores registers non-IT items at receipt ─────────────────

    [Fact]
    public async Task AM_01_stores_registers_non_it_items_at_receipt_but_only_the_admin_manager_edits_them()
    {
        var desk = new
        {
            categoryId = Categories.Furniture, subCategory = "Chair", currentLocationId = r.W.Office.Id, assignedTo = "TSAS Stores", condition = "new",
            attributes = new { make = "Herman Miller", model = "Aeron", material = "Mesh", room = "Store room" }, noSerialReason = "Furniture: no manufacturer serial",
        };
        var created = await api.CallAsync("POST", "/assets", desk, r[Roles.Stores]);
        Assert.True(created.Status == 201, $"{created.Status} {created.Error()}");
        Assert.Equal(r.W.Tsas.Id, created.Body.Int("sbuId"));
        Assert.Equal(403, (await api.CallAsync("PATCH", $"/assets/{created.Body.Int("id")}", new { notes = "x" }, r[Roles.Stores])).Status);
        Assert.Equal(403, (await api.CallAsync("POST", "/assets", desk, r[Roles.SbuIt])).Status);
    }

    // ── AU-01 / AU-04 follow-up: attachments ─────────────────────────────────────

    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private async Task<(int Status, string Body)> UploadAsync(int assetId, byte[] content, string fileName, string kind, string token)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(kind), "kind");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/assets/{assetId}/attachments") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await api.Client.SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AU_04_the_invoice_and_photos_are_attached_to_the_asset_shown_on_its_card_and_kept_for_the_audit()
    {
        var invoice = await UploadAsync(r.W.Laptop.Id, Pdf, @"C:\Users\finance\INV 2026-0042.pdf", "invoice", r[Roles.SbuFinance]);
        Assert.Equal(201, invoice.Status);
        Assert.Equal(201, (await UploadAsync(r.W.Laptop.Id, Png, "front.png", "photo", r[Roles.SbuIt])).Status);
        Assert.Equal(400, (await UploadAsync(r.W.Laptop.Id, "MZ not a pdf"u8.ToArray(), "evil.pdf", "invoice", r[Roles.SbuFinance])).Status);
        Assert.Equal(400, (await UploadAsync(r.W.Laptop.Id, Pdf, "scan.pdf", "photo", r[Roles.SbuIt])).Status);
        Assert.Equal(403, (await UploadAsync(r.W.Laptop.Id, Pdf, "x.pdf", "invoice", r[Roles.Hod])).Status);
        Assert.Equal(403, (await UploadAsync(r.BakLaptop.Id, Pdf, "x.pdf", "invoice", r[Roles.SbuFinance])).Status);

        var list = await api.CallAsync("GET", $"/assets/{r.W.Laptop.Id}/attachments", token: r[Roles.Auditor]);
        Assert.Equal(["INV 2026-0042.pdf", "front.png"], list.Body.Items().Select(a => a.Str("fileName")).Order(StringComparer.Ordinal));
        var id = list.Body.Items().First(a => a.Str("kind") == "invoice").Int("id");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/attachments/{id}?inline=true");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", r[Roles.Auditor]);
        using var response = await api.Client.SendAsync(request);
        Assert.Equal(("application/pdf", "inline"), (response.Content.Headers.ContentType?.MediaType, response.Content.Headers.ContentDisposition?.DispositionType));
        Assert.Equal(Pdf, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(403, (await api.CallAsync("GET", $"/attachments/{id}", token: r[Roles.SbuAdmin])).Status);

        var card = await api.CallAsync("GET", $"/assets/{r.W.Laptop.Id}/card", token: r[Roles.SbuFinance]);
        Assert.True(card.Body.Prop("can").Bool("attach"));
        await using var db = api.NewDbContext();
        Assert.Contains(await db.AccessLog.Where(e => e.Action == "export").Select(e => e.Detail).ToListAsync(), d => d!.StartsWith("attachment INV 2026-0042.pdf"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"DELETE FROM asset_attachments WHERE id = {id}"));
        Assert.Contains("append-only", delete.MessageText);
        Assert.Equal(409, (await api.CallAsync("DELETE", $"/assets/{r.W.Laptop.Id}", token: r[Roles.SbuIt])).Status);
    }

    // ── AU-07 follow-up: upgrade invoices in any currency ───────────────────────

    [Fact]
    public async Task AU_07_an_upgrade_invoice_is_captured_in_its_own_currency_and_finance_may_confirm_the_rate()
    {
        var logged = await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/upgrades", new
        {
            upgradeDate = "2026-10-12", description = "RAM upgrade", beforeText = "8 GB", afterText = "16 GB",
            invoiceAmount = 2500m, currency = "zar", exchangeRate = 0.054m, supplier = "Incredible Connection", invoiceNo = "IC-7781",
        }, r[Roles.SbuIt]);
        Assert.Equal(201, logged.Status);
        Assert.Equal(("ZAR", 2500m, 0.054m, 135.00m), (logged.Body.Str("currency"), logged.Body.Prop("invoiceAmount").GetDecimal(), logged.Body.Prop("exchangeRate").GetDecimal(), logged.Body.Prop("cost").GetDecimal()));

        Assert.Equal(400, (await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/upgrades", new
        {
            upgradeDate = "2026-10-12", description = "SSD", beforeText = "256", afterText = "512", invoiceAmount = 90m, currency = "EUR",
        }, r[Roles.SbuIt])).Status); // a foreign invoice needs its rate
        var usd = await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/upgrades", new
        {
            upgradeDate = "2026-10-12", description = "Battery", beforeText = "Worn", afterText = "New", cost = 85m,
        }, r[Roles.SbuIt]);
        Assert.Equal(("USD", 85m, 1m), (usd.Body.Str("currency"), usd.Body.Prop("invoiceAmount").GetDecimal(), usd.Body.Prop("exchangeRate").GetDecimal()));

        var decided = await api.CallAsync("POST", $"/upgrades/{logged.Body.Int("id")}/decision",
            new { treatment = "expense", exchangeRate = 0.055m, comment = "Rate on the payment date" }, r[Roles.SbuFinance]);
        Assert.Equal(200, decided.Status);
        Assert.Equal((0.055m, 137.50m), (decided.Body.Prop("exchangeRate").GetDecimal(), decided.Body.Prop("cost").GetDecimal()));
    }

    // ── AU-05 follow-up: default residual value and who sets the final rates ─────

    [Fact]
    public async Task AU_05_the_default_residual_is_0_and_the_system_administrator_can_set_it_and_the_final_rates()
    {
        var categories = await api.CallAsync("GET", "/categories", token: r[Roles.SystemAdmin]);
        Assert.All(categories.Body.Items(), c => Assert.Equal(0m, c.Prop("defaultResidualPercent").GetDecimal()));

        var set = await api.CallAsync("PATCH", $"/categories/{Categories.Computers}", new { usefulLifeMonths = 36, defaultResidualPercent = 10m, reason = "Final Group rates" }, r[Roles.SystemAdmin]);
        Assert.Equal(200, set.Status);
        Assert.Equal((36, 10m), (set.Body.Int("usefulLifeMonths"), set.Body.Prop("defaultResidualPercent").GetDecimal()));
        Assert.Equal(400, (await api.CallAsync("PATCH", $"/categories/{Categories.Computers}", new { defaultResidualPercent = 120m }, r[Roles.SystemAdmin])).Status);
        Assert.Equal(403, (await api.CallAsync("PATCH", $"/categories/{Categories.Computers}", new { defaultResidualPercent = 5m }, r[Roles.GroupIt])).Status);
        Assert.Equal(200, (await api.CallAsync("PATCH", "/settings/warranty_alert_days", new { value = 30 }, r[Roles.SystemAdmin])).Status);
        Assert.Equal(403, (await api.CallAsync("GET", "/assets", token: r[Roles.SystemAdmin])).Status); // categories and settings only

        // Capitalising applies the category's default (10% of 1,500.00) while Finance has not set a residual itself.
        var fin = await api.CallAsync("PATCH", $"/assets/{r.W.Projector.Id}/financial",
            new { originalCost = 1500m, currency = "USD", exchangeRate = 1m, inUseDate = "2026-10-01", reason = "Invoice" }, r[Roles.SbuFinance]);
        Assert.Equal(200, fin.Status);
        await using (var db = api.NewDbContext())
        {
            await db.Assets.Where(a => a.Id == r.W.Projector.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.CategoryId, Categories.Computers));
        }
        Assert.Equal(200, (await api.CallAsync("POST", $"/assets/{r.W.Projector.Id}/capitalise", new { }, r[Roles.SbuFinance])).Status);
        await using var check = api.NewDbContext();
        Assert.Equal(150.00m, (await check.Assets.IgnoreQueryFilters().FirstAsync(a => a.Id == r.W.Projector.Id)).ResidualValue);
    }
}
