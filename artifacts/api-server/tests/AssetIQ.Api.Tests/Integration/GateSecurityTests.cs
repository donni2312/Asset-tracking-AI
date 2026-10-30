using System.Net.Http.Headers;
using System.Text.Json;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Services;
using AssetIQ.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>
/// Phase 5b: the security checklist of a lost or stolen device (GI-07), the Gate Officer's shift view (SG-04), visitor
/// equipment (SG-05), incident escalation (SG-06) and the HOD's daily digest (LM-04).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GateSecurityTests(ApiFixture api) : IAsyncLifetime
{
    private RoleWorld r = null!;
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        r = await RoleWorld.SeedAsync(api);
        await using var db = api.NewDbContext();
        (await db.Employees.SingleAsync(e => e.Id == r.TsasStaff.Id)).Phone = "+263 77 123 4567";
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<ApiResponse> LoseAsync(int assetId, string role) =>
        api.CallAsync("POST", $"/assets/{assetId}/status", new { status = "lost_stolen", reason = "Reported stolen", lostDate = "2026-10-13", reporter = "John Doe", notes = "Taken from a car" }, r[role]);

    private async Task<(int Status, JsonElement Body)> FormAsync(string path, Dictionary<string, string> fields, string role, byte[]? photo = null)
    {
        using var form = new MultipartFormDataContent();
        foreach (var (k, v) in fields) form.Add(new StringContent(v), k);
        if (photo is not null)
        {
            var file = new ByteArrayContent(photo);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "photo", "photo.png");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api{path}") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", r[role]);
        using var response = await api.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task GI_07_a_lost_device_has_a_security_checklist_of_remote_wipe_account_disable_and_police_report()
    {
        Assert.Equal(409, (await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/security-actions", new { action = "remote_wipe" }, r[Roles.SbuIt])).Status); // not lost
        Assert.Equal(200, (await LoseAsync(r.W.Laptop.Id, Roles.SbuIt)).Status);
        var list = await api.CallAsync("GET", $"/assets/{r.W.Laptop.Id}/security-actions", token: r[Roles.SbuIt]);
        Assert.Equal(["remote_wipe", "account_disable", "police_report"], list.Body.Prop("actions").Items().Select(a => a.Str("action")).ToList());
        Assert.True(list.Body.Bool("canRecord"));
        Assert.Equal("Taken from a car", list.Body.Str("incidentNotes"));

        Assert.Equal(403, (await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/security-actions", new { action = "remote_wipe" }, r[Roles.SbuAdmin])).Status);
        Assert.Equal(201, (await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/security-actions", new { action = "remote_wipe", notes = "Intune wipe issued" }, r[Roles.SbuIt])).Status);
        Assert.Equal(409, (await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/security-actions", new { action = "remote_wipe" }, r[Roles.SbuIt])).Status);
        Assert.Equal(400, (await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/security-actions", new { action = "police_report" }, r[Roles.SbuIt])).Status);
        var done = await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/security-actions", new { action = "police_report", reference = "CR 112/10/26 Borrowdale" }, r[Roles.SbuIt]);
        Assert.Equal([true, false, true], done.Body.Prop("actions").Items().Select(a => a.Bool("done")).ToList());

        // A non-IT item: the police report only, recorded by the Admin Manager.
        Assert.Equal(200, (await LoseAsync(r.TsasDesk.Id, Roles.SbuAdmin)).Status);
        var desk = await api.CallAsync("GET", $"/assets/{r.TsasDesk.Id}/security-actions", token: r[Roles.SbuAdmin]);
        Assert.Equal(["police_report"], desk.Body.Prop("actions").Items().Select(a => a.Str("action")).ToList());
        Assert.Equal(400, (await api.CallAsync("POST", $"/assets/{r.TsasDesk.Id}/security-actions", new { action = "remote_wipe" }, r[Roles.SbuAdmin])).Status);
        Assert.Equal(201, (await api.CallAsync("POST", $"/assets/{r.TsasDesk.Id}/security-actions", new { action = "police_report", reference = "CR 113/10/26" }, r[Roles.SbuAdmin])).Status);
    }

    [Fact]
    public async Task SG_04_the_shift_view_lists_passes_due_out_today_and_overdue_items_with_holder_and_phone()
    {
        Assert.Equal("approved", (await api.CallAsync("POST", $"/gate-passes/{r.TsasPass.Id}/approve", new { }, r[Roles.Hod])).Body.Str("status"));
        var shift = await api.CallAsync("GET", "/gate/shift", token: r[Roles.GateOfficer]);
        Assert.Equal(200, shift.Status);
        var due = shift.Body.Prop("dueOut").Items().Single();
        Assert.Equal((r.TsasPass.Id, "John Doe", "+263 77 123 4567"), (due.Int("gatePassId"), due.Str("holder"), due.Str("phone")));
        Assert.Empty(shift.Body.Prop("overdue").Items());

        await api.CallAsync("POST", $"/gate-passes/{r.TsasPass.Id}/exit", new { gateLocationId = r.W.Gate.Id }, r[Roles.GateOfficer]);
        api.SetNow("2026-10-22T08:00:00Z");
        var later = await api.CallAsync("GET", "/gate/shift", token: r[Roles.GateOfficer]);
        Assert.Empty(later.Body.Prop("dueOut").Items());
        Assert.Equal("+263 77 123 4567", later.Body.Prop("overdue").Items().Single().Str("phone"));
        Assert.Equal(403, (await api.CallAsync("GET", "/gate/shift", token: r[Roles.Employee])).Status);
    }

    [Fact]
    public async Task SG_05_a_visitors_own_equipment_is_logged_on_entry_and_leaves_without_a_gate_pass()
    {
        var fields = new Dictionary<string, string> { ["gateLocationId"] = r.W.Gate.Id.ToString(), ["ownerName"] = "Peter Banda", ["ownerCompany"] = "Axis Solutions", ["item"] = "Lenovo laptop", ["serialNumber"] = "vis-123" };
        var logged = await FormAsync("/gate/visitor-items", fields, Roles.GateOfficer, Png);
        Assert.True(logged.Status == 201, logged.Body.GetRawText());
        Assert.Equal(("VIS-123", true), (logged.Body.Str("serialNumber"), logged.Body.Bool("hasPhoto")));
        var id = logged.Body.Int("id");
        Assert.Equal(403, (await FormAsync("/gate/visitor-items", new(fields) { ["gateLocationId"] = r.W.BakGate.Id.ToString() }, Roles.GateOfficer)).Status);
        Assert.Equal(400, (await FormAsync("/gate/visitor-items", fields, Roles.GateOfficer, [1, 2, 3])).Status); // not an image

        var scan = await api.CallAsync("GET", "/gate/scan?code=VIS-123", token: r[Roles.GateOfficer]);
        Assert.Equal(("clear", id), (scan.Body.Str("verdict"), scan.Body.Int("visitorItemId")));
        Assert.Contains("Peter Banda", scan.Body.Str("message"));

        var after = await api.CallAsync("POST", $"/gate/visitor-items/{id}/check-out", new { gateLocationId = r.W.Gate.Id }, r[Roles.GateOfficer]);
        Assert.Empty(after.Body.Items());
        Assert.Equal(409, (await api.CallAsync("POST", $"/gate/visitor-items/{id}/check-out", new { gateLocationId = r.W.Gate.Id }, r[Roles.GateOfficer])).Status);
        Assert.Equal(404, (await api.CallAsync("GET", "/gate/scan?code=VIS-123", token: r[Roles.GateOfficer])).Status);
        Assert.Single((await api.CallAsync("GET", "/gate/visitor-items?all=true", token: r[Roles.GateOfficer])).Body.Items());
    }

    [Fact]
    public async Task SG_06_an_incident_is_escalated_with_photo_and_notes_to_the_admin_manager_and_the_owner_sbus_it()
    {
        var res = await FormAsync("/gate/incidents", new() { ["kind"] = "refused_search", ["gateLocationId"] = r.W.Gate.Id.ToString(), ["code"] = "TAG-DL-001", ["notes"] = "Holder refused the bag search" }, Roles.GateOfficer, Png);
        Assert.True(res.Status == 201, res.Body.GetRawText());
        Assert.Equal((r.W.Laptop.Id, true), (res.Body.Int("assetId"), res.Body.Bool("hasPhoto")));
        Assert.Contains((await api.CallAsync("GET", "/notifications", token: r[Roles.SbuAdmin])).Body.Items(), n => n.Str("kind") == "incident");
        Assert.Contains((await api.CallAsync("GET", "/notifications", token: r[Roles.SbuIt])).Body.Items(), n => n.Str("kind") == "incident");
        var history = await api.CallAsync("GET", $"/assets/{r.W.Laptop.Id}/lifecycle", token: r[Roles.GroupIt]);
        Assert.Contains(history.Body.Items(), e => (e.Str("notes") ?? "").StartsWith("Gate incident (refused search)", StringComparison.Ordinal));

        Assert.Equal(400, (await FormAsync("/gate/incidents", new() { ["kind"] = "forged_pass", ["gateLocationId"] = r.W.Gate.Id.ToString(), ["notes"] = "" }, Roles.GateOfficer)).Status);
        Assert.Single((await api.CallAsync("GET", "/gate/incidents", token: r[Roles.SbuAdmin])).Body.Items());
        using (var request = new HttpRequestMessage(HttpMethod.Get, $"/api/gate/incidents/{res.Body.Int("id")}/photo"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", r[Roles.SbuAdmin]);
            using var photo = await api.Client.SendAsync(request);
            Assert.Equal(("image/png", 200), (photo.Content.Headers.ContentType?.MediaType, (int)photo.StatusCode));
        }
        Assert.Equal(403, (await FormAsync("/gate/incidents", new() { ["kind"] = "other", ["notes"] = "x x x" }, Roles.Employee)).Status);
    }

    [Fact]
    public async Task LM_04_the_hod_gets_a_daily_digest_and_one_in_app_alert_per_problem_in_their_department()
    {
        await using (var db = api.NewDbContext())
        {
            (await db.Assets.SingleAsync(a => a.Id == r.TsasDesk.Id)).Condition = "damaged";
            await db.SaveChangesAsync();
        }
        Assert.Equal(200, (await LoseAsync(r.W.Laptop.Id, Roles.SbuIt)).Status);
        async Task<int> DigestAsync()
        {
            using var scope = api.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<GateSecurityService>().SendHodDigestsAsync(CancellationToken.None);
        }
        Assert.Equal(1, await DigestAsync());
        Assert.Equal(0, await DigestAsync()); // once a day
        var alerts = (await api.CallAsync("GET", "/notifications", token: r[Roles.Hod])).Body.Items().Where(n => n.Str("kind") == "department").ToList();
        Assert.Equal(2, alerts.Count); // the lost laptop and the damaged desk
        Assert.All(alerts, a => Assert.StartsWith("/assets/", a.Str("link")));

        api.SetNow("2026-10-15T08:00:00Z");
        Assert.Equal(1, await DigestAsync());
        Assert.Equal(2, (await api.CallAsync("GET", "/notifications", token: r[Roles.Hod])).Body.Items().Count(n => n.Str("kind") == "department")); // no repeats
        await using var check = api.NewDbContext();
        Assert.Equal(2, await check.OutboundEmails.CountAsync(e => e.Subject.Contains("your department today")));
    }
}
