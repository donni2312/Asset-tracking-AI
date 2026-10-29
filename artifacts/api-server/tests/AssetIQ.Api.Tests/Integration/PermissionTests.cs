using AssetIQ.Api.Domain;
using AssetIQ.Api.Tests.Infrastructure;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>
/// Phase 2: the Permissions matrix of docs/user-stories.md, enforced by the API. Every SBU role is a TSAS ("SBU A")
/// account; the BAK ("SBU B") records are what it must not reach. A refused call is 401 when signed out and 403
/// otherwise; an allowed call may still fail validation or conflict, but never with 401 or 403.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PermissionTests(ApiFixture api) : IAsyncLifetime
{
    private RoleWorld r = null!;

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        r = await RoleWorld.SeedAsync(api);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── signed out ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Phase2_signed_out_callers_get_401_on_every_read_and_only_health_and_sign_in_stay_open()
    {
        foreach (var path in new[]
                 {
                     "/assets", $"/assets/{r.W.Laptop.Id}", "/dashboard/summary", "/dashboard/activity", "/gate-passes", "/movements", "/insights",
                     "/locations", "/sbus", "/lifecycle", "/rfid/readers", "/bc/sync-status", "/reports/assets.csv", "/categories", "/settings",
                     "/finance/register", "/employees", "/departments", "/users", "/scan?code=TAG-DL-001",
                 })
        {
            Assert.True((await api.CallAsync("GET", path, token: ApiFixture.Anonymous)).Status == 401, $"GET {path} should need a sign-in");
        }
        Assert.Equal(401, (await api.CallAsync("POST", "/ai/query", new { question = "How many assets?" }, ApiFixture.Anonymous)).Status);
        Assert.Equal(200, (await api.CallAsync("GET", "/healthz", token: ApiFixture.Anonymous)).Status);
        Assert.Equal(401, (await api.CallAsync("POST", "/auth/login", new { username = "nobody", password = "wrong-password" }, ApiFixture.Anonymous)).Status);
    }

    // ── SBU A user → SBU B record: read, edit, export ──────────────────────────────

    /// <summary>For each SBU role: a BAK record of the kind the role works with, how it would edit it, and an export of it.</summary>
    public static TheoryData<string, string, string, string, string> CrossSbu => new()
    {
        { Roles.SbuIt, "GET /assets/{bakLaptop}", "PATCH /assets/{bakLaptop}", "GET /assets/{bakLaptop}/card.pdf", "GET /assets/{laptop}" },
        { Roles.SbuFinance, "GET /assets/{bakDesk}", "PATCH /assets/{bakDesk}/financial", "GET /assets/{bakDesk}/depreciation.xlsx", "GET /assets/{desk}" },
        { Roles.GateOfficer, "GET /gate-passes/{bakPass}", "POST /movements bak", "GET /reports/movements.csv?assetId={bakLaptop}", "GET /gate-passes/{tsasPass}" },
        { Roles.Hod, "GET /assets/{bakLaptop}", "POST /gate-passes/{bakPass}/approve", "GET /assets/{bakLaptop}/card.pdf", "GET /assets/{laptop}" },
        { Roles.SbuHr, "GET /employees/{bakStaff}", "PATCH /employees/{bakStaff}", "GET /assets/{bakLaptop}/card.pdf", "GET /employees/{staff}" },
        { Roles.Stores, "GET /assets/{bakStore}", "POST /assets/{bakStore}/lifecycle", "GET /assets/{bakStore}/card.pdf", "GET /scan?code=TAG-EP-001" },
        { Roles.SbuAdmin, "GET /assets/{bakDesk}", "PATCH /assets/{bakDesk}", "GET /assets/{bakDesk}/card.pdf", "GET /assets/{desk}" },
        { Roles.Employee, "GET /assets/{bakLaptop}", "PATCH /assets/{bakLaptop}", "GET /assets/{bakLaptop}/card.pdf", "GET /assets/{laptop}" },
    };

    [Theory]
    [MemberData(nameof(CrossSbu))]
    public async Task Phase2_an_sbu_a_user_reading_editing_or_exporting_an_sbu_b_record_gets_403(string role, string read, string edit, string export, string ownRead)
    {
        var token = r[role];
        Assert.True((await Call(ownRead, token)).Status == 200, $"{role} should read its own SBU's record ({ownRead})");
        foreach (var request in new[] { read, edit })
        {
            var res = await Call(request, token);
            Assert.True(res.Status == 403, $"{role}: {request} on an SBU B record should be 403, was {res.Status} {res.Error()}");
        }
        var exported = await Call(export, token);
        if (role == Roles.GateOfficer)
        {
            // The Gate Officer may download its gate log, but it never contains SBU B rows.
            Assert.Equal(200, exported.Status);
        }
        else
        {
            Assert.True(exported.Status == 403, $"{role}: {export} should be 403, was {exported.Status}");
        }
    }

    [Fact]
    public async Task Phase2_group_it_sees_and_acts_on_it_assets_only()
    {
        var token = r[Roles.GroupIt];
        Assert.Equal(200, (await Call("GET /assets/{bakLaptop}", token)).Status);
        Assert.Equal(403, (await Call("GET /assets/{bakDesk}", token)).Status);
        Assert.Equal(403, (await Call("PATCH /assets/{bakDesk}", token)).Status);
        Assert.Equal(403, (await Call("GET /assets/{bakDesk}/card.pdf", token)).Status);
        var list = await api.CallAsync("GET", "/assets", token: token);
        Assert.DoesNotContain(list.Body.Items(), a => a.Str("assetNumber")!.Contains("-FF-"));
    }

    [Fact]
    public async Task Phase2_lists_reports_and_exports_hold_only_the_callers_scope()
    {
        string[] Numbers(ApiResponse res) => res.Body.Items().Select(a => a.Str("assetNumber") ?? "").Order().ToArray();
        Assert.Equal(["TSAS-IT-2026-00001", "TSAS-IT-2026-00002"], Numbers(await api.CallAsync("GET", "/assets", token: r[Roles.SbuIt])));
        Assert.Equal(["TSAS-FF-2026-00001"], Numbers(await api.CallAsync("GET", "/assets", token: r[Roles.SbuAdmin])));
        Assert.Equal(["TSAS-FF-2026-00001", "TSAS-IT-2026-00001"], Numbers(await api.CallAsync("GET", "/assets", token: r[Roles.Employee])));
        Assert.Equal(["TSAS-FF-2026-00001", "TSAS-IT-2026-00001"], Numbers(await api.CallAsync("GET", "/assets", token: r[Roles.Hod])));
        Assert.Equal(["TSAS-FF-2026-00001", "TSAS-IT-2026-00001"], Numbers(await api.CallAsync("GET", "/assets", token: r[Roles.SbuHr])));
        Assert.Empty(Numbers(await api.CallAsync("GET", "/assets", token: r[Roles.Stores]))); // no non-IT item in store, no IT item for disposal
        Assert.Equal(6, (await api.CallAsync("GET", "/assets", token: r[Roles.GroupFinance])).Body.Items().Count);

        // A filter for another SBU returns nothing rather than leaking it.
        Assert.Empty((await api.CallAsync("GET", $"/assets?sbuId={r.W.Bak.Id}", token: r[Roles.SbuIt])).Body.Items());
        // The CSV export, the dashboard and the gate passes follow the same scope.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/reports/assets.csv");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", r[Roles.SbuIt]);
        var csv = await (await api.Client.SendAsync(request)).Content.ReadAsStringAsync();
        Assert.Contains("SN-DL-001", csv);
        Assert.DoesNotContain("SN-BAK-", csv);
        Assert.Equal(2, (await api.CallAsync("GET", "/dashboard/summary", token: r[Roles.SbuIt])).Body.Int("totalAssets"));
        var passes = await api.CallAsync("GET", "/gate-passes", token: r[Roles.SbuIt]);
        Assert.Equal([r.TsasPass.Id], passes.Body.Items().Select(p => p.Int("id")));
        Assert.DoesNotContain((await api.CallAsync("GET", $"/sbus/{r.W.Tsas.Id}/assets", token: r[Roles.SbuAdmin])).Body.Items(), a => a.Str("sbuCode") == "BAK");
        Assert.Equal(403, (await api.CallAsync("GET", $"/sbus/{r.W.Bak.Id}/assets", token: r[Roles.SbuIt])).Status);
    }

    // ── the matrix, cell by cell ────────────────────────────────────────────────

    /// <summary>
    /// (matrix row, request, allowed roles, refused roles). Every "—" cell of an implemented row has a refused
    /// representative action; the "Yes" cells next to it show the same request is not refused for everyone.
    /// Rows whose workflow arrives later (receive against PO, request equipment, exit clearance, disposals,
    /// verification, fleet) are covered when their endpoints are built.
    /// </summary>
    public static TheoryData<string, string, string, string> Matrix
    {
        get
        {
            var data = new TheoryData<string, string, string, string>();
            // The System Administrator may do everything (product owner, 29 Oct 2026), whatever the row says.
            void Row(string row, string request, string[] allowed, string[] refused) =>
                data.Add(row, request, string.Join(",", allowed.Append(Roles.SystemAdmin).Distinct()), string.Join(",", refused.Where(r => r != Roles.SystemAdmin)));
            const string gf = Roles.GroupFinance, gi = Roles.GroupIt, si = Roles.SbuIt, sf = Roles.SbuFinance, go = Roles.GateOfficer, hod = Roles.Hod,
                hr = Roles.SbuHr, st = Roles.Stores, sa = Roles.SbuAdmin, au = Roles.Auditor, em = Roles.Employee, sys = Roles.SystemAdmin;

            Row("View assets: on scan only", "GET /assets", [gf, gi, si, sf, hod, hr, st, sa, au, em], [go, sys]);
            Row("View assets: on scan only", "GET /assets/{laptop}", [gf, gi, si, sf, hod, hr, au, em], [go, st, sa, sys]);
            Row("View assets: on scan only", "GET /scan?code=TAG-DL-001", [gf, gi, si, sf, go, hod, hr, st, sa, au, em], [sys]);
            // Phase 3: an unassigned, tagged IT item stays with SBU IT, not Stores (SI-02).
            Row("View assets: items in store / own dept / toolboxes", "GET /assets/{projector}", [gf, gi, si, sf, au], [go, hod, hr, st, sa, em]);
            Row("View assets: own SBU (non-IT)", "GET /assets/{desk}", [gf, sf, hod, hr, sa, au, em], [gi, si, go, st, sys]);

            Row("Register / tag asset (IT)", "POST /assets laptop", [gi, si, st], [gf, sf, go, hod, hr, sa, au, em, sys]);
            Row("Register / tag asset (non-IT)", "POST /assets desk", [sa, st], [gf, gi, si, sf, go, hod, hr, au, em, sys]);
            Row("Register: SBU Finance capitalise only", "POST /assets/{laptop}/capitalise", [sf], [gf, gi, si, go, hod, hr, st, sa, au, em, sys]);

            Row("Edit financial fields", "PATCH /assets/{laptop}/financial", [sf], [gf, gi, si, go, hod, hr, st, sa, au, em, sys]);
            Row("Set categories & depreciation rates", "PATCH /categories/1", [gf, sys], [gi, si, sf, go, hod, hr, st, sa, au, em]);
            Row("Set categories: IT fields only", "POST /categories/1/fields", [gi], [gf, si, sf, go, hod, hr, st, sa, au, em, sys]);
            Row("Set categories: non-IT fields", "POST /categories/7/fields", [sa], [gf, gi, si, sf, go, hod, hr, st, au, em, sys]);

            Row("Edit technical fields (IT)", "PATCH /assets/{laptop}", [gi, si], [gf, sf, go, hod, hr, st, sa, au, em, sys]);
            Row("Edit technical fields (non-IT)", "PATCH /assets/{desk}", [sa], [gf, gi, si, sf, go, hod, hr, st, au, em, sys]);

            Row("Issue / assign to employee (IT)", "POST /assets/{projector}/lifecycle deployment", [gi, si, st], [gf, sf, go, hod, hr, sa, au, em, sys]);
            Row("Issue / assign to employee (non-IT)", "POST /assets/{desk}/lifecycle deployment", [sa, st], [gf, gi, si, sf, go, hod, hr, au, em, sys]);

            Row("Raise gate pass (IT)", "POST /gate-passes projector", [gi, si], [gf, sf, go, hod, hr, st, sa, au, em, sys]);
            Row("Raise gate pass (non-IT)", "POST /gate-passes desk", [sa], [gf, gi, si, sf, go, hod, hr, st, au, em, sys]);
            Row("Approve gate pass", "POST /gate-passes/{tsasPass}/approve", [gi, si, hod], [gf, sf, go, hr, st, sa, au, em, sys]);

            Row("Check exits and returns at gate: view log", "GET /movements", [gi, si, go, au], [gf, sf, hod, hr, st, sa, em, sys]);
            Row("Check exits and returns at gate: record", "POST /movements", [gi, si, go], [gf, sf, hod, hr, st, sa, au, em, sys]);

            Row("Approve inter-SBU transfer", "POST /assets/{projector}/lifecycle sbu_transfer", [gi], [gf, si, sf, go, hod, hr, st, sa, au, em, sys]);
            Row("Manage users", "POST /users", [gi, si], [gf, sf, go, hod, hr, st, sa, au, em, sys]);

            Row("Finance reports (AU-09 … AU-11)", "GET /finance/register", [gf, sf, au], [gi, si, go, hod, hr, st, sa, em, sys]);
            Row("Employee toolboxes: SBU HR maintains", "POST /employees", [hr], [gf, gi, si, sf, go, hod, st, sa, au, em, sys]);
            return data;
        }
    }

    /// <summary>The refused roles go first, so nothing an allowed role changes can hide a refusal.</summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Phase2_permissions_matrix(string row, string request, string allowed, string refused)
    {
        foreach (var role in refused.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var res = await Call(request, r[role]);
            Assert.True(res.Status == 403, $"[{row}] {role} should be refused {request} but got {res.Status} {res.Error()}");
        }
        foreach (var role in allowed.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var res = await Call(request, r[role]);
            Assert.True(res.Status is not (401 or 403), $"[{row}] {role} should be allowed {request} but got {res.Status} {res.Error()}");
        }
    }

    // ── record-level rules ────────────────────────────────────────────────────────

    [Fact]
    public async Task Phase2_gate_passes_go_to_the_hod_the_admin_manager_only_when_there_is_no_hod_and_sbu_finance_only_above_the_threshold()
    {
        // The projector belongs to no department, so no HOD can decide: the SBU Admin Manager may.
        await using (var db = api.NewDbContext())
        {
            var projector = await db.Assets.FindAsync(r.W.Projector.Id);
            projector!.CostUsd = 900m; // above gate_pass_finance_threshold_usd (500)
            await db.SaveChangesAsync();
        }
        var pass = await api.CallAsync("POST", "/gate-passes", new { assetId = r.W.Projector.Id, requestedBy = "Farai", reason = "Workshop" }, r[Roles.SbuIt]);
        Assert.Equal(201, pass.Status);
        var passId = pass.Body.Int("id");
        Assert.Equal(403, (await api.CallAsync("POST", $"/gate-passes/{passId}/approve", new { }, r[Roles.Hod])).Status);
        var finance = await api.CallAsync("POST", $"/gate-passes/{passId}/approve", new { note = "High value, insured" }, r[Roles.SbuFinance]);
        Assert.Equal(200, finance.Status);
        Assert.Equal("SBU Finance TSAS", finance.Body.Str("approvedBy")); // the signed-in approver, not a typed name

        // The laptop's department has an HOD: the Admin Manager may not step in; the HOD may.
        Assert.Equal(403, (await api.CallAsync("POST", $"/gate-passes/{r.TsasPass.Id}/reject", new { }, r[Roles.SbuAdmin])).Status);
        Assert.Equal(403, (await api.CallAsync("POST", $"/gate-passes/{r.TsasPass.Id}/approve", new { }, r[Roles.SbuFinance])).Status); // no cost: not high-value
        Assert.Equal(200, (await api.CallAsync("POST", $"/gate-passes/{r.TsasPass.Id}/approve", new { }, r[Roles.Hod])).Status);
    }

    [Fact]
    public async Task Phase2_a_scan_shows_the_gate_officer_their_own_sbus_asset_in_full_and_others_only_basic_identification()
    {
        var own = await api.CallAsync("GET", "/scan?code=TAG-DL-001", token: r[Roles.GateOfficer]);
        Assert.Equal(200, own.Status);
        Assert.Equal(("John Doe", "not_approved", false), (own.Body.Str("holder"), own.Body.Str("gateClearance"), own.Body.Bool("canOpen")));
        Assert.Equal(r.TsasPass.Id, own.Body.Int("gatePassId"));

        var other = await api.CallAsync("GET", "/scan?code=TAG-SN-BAK-1", token: r[Roles.GateOfficer]);
        Assert.Equal(200, other.Status);
        Assert.Equal(("BAK", "not_shown"), (other.Body.Str("sbuCode"), other.Body.Str("gateClearance")));
        Assert.True(other.Body.IsNull("holder"));
        Assert.True(other.Body.IsNull("gatePassId"));

        // SI-03: SBU IT scanning another SBU's device sees the same basic identification and cannot open it.
        var it = await api.CallAsync("GET", "/scan?code=BAK-IT-2026-00001", token: r[Roles.SbuIt]);
        Assert.Equal((false, true), (it.Body.Bool("canOpen"), it.Body.IsNull("holder")));
    }

    [Fact]
    public async Task Phase2_auditors_are_refused_every_change_and_the_refusal_says_why()
    {
        var res = await api.CallAsync("PATCH", $"/assets/{r.W.Laptop.Id}", new { notes = "x" }, r[Roles.Auditor]);
        Assert.Equal((403, "Auditors have read-only access (AU-12)."), (res.Status, res.Error()));
        var role = await api.CallAsync("POST", "/rfid/simulate", new { rfidTag = "TAG-EP-001", readerId = r.W.OfficeReader.Id }, r[Roles.SbuIt]);
        Assert.Equal((403, "Your role (SBU IT Admin / Technician) may not do this."), (role.Status, role.Error()));
        var signedOut = await api.CallAsync("POST", "/movements", new { assetId = r.W.Projector.Id, toLocationId = r.W.Gate.Id }, ApiFixture.Anonymous);
        Assert.Equal((401, "Sign in to continue"), (signedOut.Status, signedOut.Error()));
    }

    // ── request helpers ─────────────────────────────────────────────────────────

    private Task<ApiResponse> Call(string request, string token)
    {
        var parts = request.Split(' ');
        var (method, template, variant) = (parts[0], parts[1], parts.Length > 2 ? parts[2] : null);
        var path = template
            .Replace("{laptop}", r.W.Laptop.Id.ToString()).Replace("{projector}", r.W.Projector.Id.ToString()).Replace("{desk}", r.TsasDesk.Id.ToString())
            .Replace("{bakLaptop}", r.BakLaptop.Id.ToString()).Replace("{bakDesk}", r.BakDesk.Id.ToString()).Replace("{bakStore}", r.BakStoreItem.Id.ToString())
            .Replace("{tsasPass}", r.TsasPass.Id.ToString()).Replace("{bakPass}", r.BakPass.Id.ToString())
            .Replace("{staff}", r.TsasStaff.Id.ToString()).Replace("{bakStaff}", r.BakStaff.Id.ToString());
        return api.CallAsync(method, path, Body(method, template, variant, token), token);
    }

    private object? Body(string method, string template, string? variant, string token) => (method, template, variant) switch
    {
        ("POST", "/assets", "laptop") => new
        {
            categoryId = Categories.Computers, subCategory = "Laptop", sbuId = r.W.Tsas.Id, currentLocationId = r.W.Office.Id, assignedTo = "TSAS IT store", condition = "new",
            attributes = new { make = "HP", model = "EliteBook", serial_number = $"SN-{token.GetHashCode():X}", processor = "i5", ram = 8, storage = "256 GB SSD", operating_system = "Windows 11" },
        },
        ("POST", "/assets", "desk") => new
        {
            categoryId = Categories.Furniture, subCategory = "Desk", sbuId = r.W.Tsas.Id, currentLocationId = r.W.Office.Id, assignedTo = "Admin store", condition = "new",
            attributes = new { make = "Generic", model = "Executive", material = "Oak", room = "Boardroom" }, noSerialReason = "Furniture: no manufacturer serial",
        },
        ("PATCH", _, _) when template.EndsWith("/financial") => new { supplier = "Axis Computers", reason = "Invoice received" },
        ("PATCH", _, _) when template.StartsWith("/assets/") => new { notes = "checked", reason = "Inspection" },
        ("PATCH", "/categories/1", _) => new { usefulLifeMonths = 48, reason = "Group policy" },
        ("PATCH", _, _) when template.StartsWith("/employees/") => new { phone = "+263 77 000 0000" },
        ("POST", "/categories/1/fields", _) => new { key = $"dock_{token.GetHashCode():x}", label = "Dock", type = "text", required = false },
        ("POST", "/categories/7/fields", _) => new { key = $"finish_{token.GetHashCode():x}", label = "Finish", type = "text", required = false },
        ("POST", _, "deployment") => new { eventType = "deployment", performedBy = "Test", custodianEmployeeId = r.TsasStaff.Id },
        ("POST", _, "sbu_transfer") => new { eventType = "sbu_transfer", performedBy = "Test", fromSbuId = r.W.Tsas.Id, toSbuId = r.W.Bak.Id },
        ("POST", "/assets/{bakStore}/lifecycle", _) => new { eventType = "deployment", performedBy = "Test", assignedTo = "Someone" },
        ("POST", "/gate-passes", "projector") => new { assetId = r.W.Projector.Id, requestedBy = "Farai", reason = "Workshop" },
        ("POST", "/gate-passes", "desk") => new { assetId = r.TsasDesk.Id, requestedBy = "Rudo", reason = "Event" },
        ("POST", "/movements", "bak") => new { assetId = r.BakLaptop.Id, toLocationId = r.W.BakGate.Id },
        ("POST", "/movements", _) => new { assetId = r.W.Projector.Id, toLocationId = r.W.Gate.Id },
        ("POST", "/users", _) => new { username = $"u-{token.GetHashCode():x}", name = "New Clerk", password = "a-long-password", role = Roles.Stores, sbuId = r.W.Tsas.Id },
        ("POST", "/employees", _) => new { employeeNo = $"TSAS-{token.GetHashCode():X}", name = "New Starter" },
        ("POST", _, _) when template.EndsWith("/approve") || template.EndsWith("/capitalise") => new { },
        _ => null,
    };
}
