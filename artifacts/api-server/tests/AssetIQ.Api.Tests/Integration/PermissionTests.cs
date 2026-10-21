using AssetIQ.Api.Tests.Infrastructure;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>
/// Who may do what (CLAUDE.md §17). Reading and asking the AI assistant stay open to everyone; every change
/// needs a signed-in user: viewers may only record asset movements, operators may do everything except the
/// RFID simulator and the Business Central sync, administrators may do everything. A refused call is 401
/// when signed out and 403 otherwise; an allowed call may still fail validation, but never with 401 or 403.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PermissionTests(ApiFixture api) : IAsyncLifetime
{
    private TestWorld w = null!;

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        w = await TestWorld.SeedAsync(api);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Endpoint → the lowest role that may call it ("anyone" = no sign-in needed).</summary>
    public static TheoryData<string, string> Matrix => new()
    {
        { "GET /assets", "anyone" },
        { "GET /dashboard/summary", "anyone" },
        { "GET /gate-passes", "anyone" },
        { "GET /bc/sync-status", "anyone" },
        { "POST /ai/query", "anyone" },
        { "GET /reports/assets.csv", "viewer" },
        { "GET /reports/movements.csv", "viewer" },
        { "POST /movements", "viewer" },
        { "POST /assets", "operator" },
        { "PATCH /assets/{laptop}", "operator" },
        { "DELETE /assets/99999", "operator" },
        { "POST /assets/{laptop}/lifecycle", "operator" },
        { "POST /locations", "operator" },
        { "PATCH /locations/{office}", "operator" },
        { "POST /sbus", "operator" },
        { "PATCH /sbus/{tsas}", "operator" },
        { "POST /gate-passes", "operator" },
        { "POST /gate-passes/9999/approve", "operator" },
        { "POST /gate-passes/9999/reject", "operator" },
        { "POST /insights/9999/dismiss", "operator" },
        { "POST /rfid/simulate", "admin" },
        { "POST /bc/sync", "admin" },
    };

    private static readonly string[] Callers = ["anyone", "viewer", "operator", "admin"];

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Each_role_may_do_exactly_what_it_is_allowed_to(string endpoint, string minimum)
    {
        var (method, template) = (endpoint[..endpoint.IndexOf(' ')], endpoint[(endpoint.IndexOf(' ') + 1)..]);
        var path = template
            .Replace("{laptop}", w.Laptop.Id.ToString())
            .Replace("{office}", w.Office.Id.ToString())
            .Replace("{tsas}", w.Tsas.Id.ToString());

        foreach (var caller in Callers)
        {
            var token = caller switch
            {
                "anyone" => ApiFixture.Anonymous,
                "viewer" => w.ViewerToken,
                "operator" => w.OperatorToken,
                _ => w.AdminToken,
            };
            var res = await api.CallAsync(method, path, Body(method, template, caller), token);
            var allowed = Array.IndexOf(Callers, caller) >= Array.IndexOf(Callers, minimum);
            if (allowed)
            {
                Assert.True(res.Status is not (401 or 403), $"{caller} should be allowed to {endpoint} but got {res.Status} {res.Error()}");
            }
            else
            {
                Assert.True(res.Status == (caller == "anyone" ? 401 : 403), $"{caller} should be refused {endpoint} but got {res.Status}");
            }
        }
    }

    [Fact]
    public async Task A_refusal_names_the_role_that_is_needed()
    {
        var res = await api.CallAsync("POST", "/rfid/simulate", new { rfidTag = "TAG-EP-001", readerId = w.OfficeReader.Id }, w.OperatorToken);
        Assert.Equal((403, "This action needs the admin role; you are signed in as operator."), (res.Status, res.Error()));

        var signedOut = await api.CallAsync("POST", "/movements", new { assetId = w.Projector.Id, toLocationId = w.Gate.Id }, ApiFixture.Anonymous);
        Assert.Equal((401, "Sign in to continue"), (signedOut.Status, signedOut.Error()));
    }

    [Fact]
    public async Task A_viewer_can_record_a_movement()
    {
        var res = await api.CallAsync("POST", "/movements", new { assetId = w.Projector.Id, toLocationId = w.Gate.Id, movedBy = "Nyasha Gumbo" }, w.ViewerToken);
        Assert.Equal(201, res.Status);
        Assert.Equal(("manual", "TSAS Main Gate"), (res.Body.Str("triggeredBy"), res.Body.Str("toLocationName")));
    }

    /// <summary>A valid body for each write, unique per caller where a value must be unique.</summary>
    private object? Body(string method, string template, string caller) => (method, template) switch
    {
        ("POST", "/ai/query") => new { question = "How many assets in total?" },
        ("POST", "/movements") => new { assetId = w.Projector.Id, toLocationId = caller == "admin" ? w.BakWarehouse.Id : w.Gate.Id },
        ("POST", "/assets") => new { name = "HP EliteBook", category = "Laptop", serialNumber = $"SN-{caller}", rfidTag = $"TAG-{caller}" },
        ("PATCH", "/assets/{laptop}") => new { notes = $"edited by {caller}" },
        ("POST", "/assets/{laptop}/lifecycle") => new { eventType = "note", performedBy = caller },
        ("POST", "/locations") => new { name = $"Room {caller}", type = "office", building = "TSAS House" },
        ("PATCH", "/locations/{office}") => new { description = caller },
        ("POST", "/sbus") => new { code = $"X-{caller}", name = $"Unit {caller}" },
        ("PATCH", "/sbus/{tsas}") => new { description = caller },
        ("POST", "/gate-passes") => new { assetId = w.Projector.Id, requestedBy = caller, reason = "Event" },
        ("POST", "/gate-passes/9999/approve") or ("POST", "/gate-passes/9999/reject") => new { decidedBy = caller },
        ("POST", "/rfid/simulate") => new { rfidTag = "TAG-EP-001", readerId = w.OfficeReader.Id },
        _ => null,
    };
}
