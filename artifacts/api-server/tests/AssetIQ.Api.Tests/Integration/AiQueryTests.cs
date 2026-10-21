using System.Net;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Data;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services.NlQuery;
using AssetIQ.Api.Tests.Infrastructure;
using AssetIQ.Api.Tests.Unit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>Natural-language queries — the plan's three example questions answered from real data. Ported from src/routes/ai-query.test.ts.</summary>
[Collection(ApiCollection.Name)]
public sealed class AiQueryTests(ApiFixture api) : IAsyncLifetime
{
    private TestWorld w = null!;

    private DateTime DaysAgo(double days) => api.Now - TimeSpan.FromDays(days);

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        w = await TestWorld.SeedAsync(api);

        await using var db = api.NewDbContext();
        // Both assets were registered long ago.
        await db.Assets.ExecuteUpdateAsync(s => s.SetProperty(a => a.CreatedAt, DaysAgo(200)));
        // Laptop moved two days ago; projector last moved 45 days ago.
        db.Movements.AddRange(
            new Movement { AssetId = w.Laptop.Id, ToLocationId = w.Office.Id, TriggeredBy = "rfid", Timestamp = DaysAgo(2) },
            new Movement { AssetId = w.Projector.Id, ToLocationId = w.Office.Id, TriggeredBy = "manual", Timestamp = DaysAgo(45) });
        // One pass approved this week (Tue 13 Oct), one approved last week (Thu 8 Oct), one pending.
        db.GatePasses.AddRange(
            new GatePass
            {
                AssetId = w.Laptop.Id, RequestedBy = "John Doe", Reason = "Client demo", Destination = "Borrowdale", Status = "approved",
                ApprovedBy = "Rudo Chikwanha", DecidedAt = DaysAgo(1), CreatedAt = api.Now, UpdatedAt = api.Now,
            },
            new GatePass
            {
                AssetId = w.Projector.Id, RequestedBy = "Tendai Moyo", Reason = "Expo", Status = "approved",
                ApprovedBy = "Rudo Chikwanha", DecidedAt = DaysAgo(6), CreatedAt = api.Now, UpdatedAt = api.Now,
            },
            new GatePass { AssetId = w.Projector.Id, RequestedBy = "Tendai Moyo", Reason = "Board meeting", Status = "pending", CreatedAt = api.Now, UpdatedAt = api.Now });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<ApiResponse> Ask(string question) => api.CallAsync("POST", "/ai/query", new { question });

    [Fact]
    public async Task Where_is_the_dell_laptop_assigned_to_john_doe()
    {
        var res = await Ask("Where is the Dell laptop assigned to John Doe?");
        Assert.Equal(200, res.Status);
        Assert.Equal(("locate_asset", "rules"), (res.Body.Str("intent"), res.Body.Str("source")));
        Assert.Contains("Dell Latitude 5440 (TAG-DL-001) is at TSAS IT Office [TSAS], assigned to John Doe.", res.Body.Str("answer"));
        Assert.Equal([w.Laptop.Id], res.Body.Prop("relatedAssets").Items().Select(a => a.Int("id")));
    }

    [Fact]
    public async Task Which_assets_have_not_moved_in_30_days()
    {
        var res = await Ask("Which assets have not moved in 30 days?");
        Assert.Equal("not_moved", res.Body.Str("intent"));
        Assert.StartsWith("1 asset has not moved in 30 days:", res.Body.Str("answer"));
        Assert.Contains("Epson EB-X51 Projector", res.Body.Str("answer"));
        Assert.Contains("45 days ago", res.Body.Str("answer"));
        Assert.Equal([w.Projector.Id], res.Body.Prop("relatedAssets").Items().Select(a => a.Int("id")));
    }

    [Fact]
    public async Task Show_me_all_gate_passes_approved_this_week()
    {
        var res = await Ask("Show me all gate passes approved this week");
        Assert.Equal("gate_passes", res.Body.Str("intent"));
        Assert.StartsWith("1 gate pass approved this week:", res.Body.Str("answer"));
        Assert.Contains("Dell Latitude 5440 → Borrowdale — approved by Rudo Chikwanha on Tue 13 Oct 10:00", res.Body.Str("answer"));
        var pass = Assert.Single(res.Body.Prop("relatedGatePasses").Items());
        Assert.Equal((w.Laptop.Id, "approved"), (pass.Int("assetId"), pass.Str("status")));
    }

    [Fact]
    public async Task Answers_last_week_separately_from_this_week()
    {
        var res = await Ask("Gate passes approved last week");
        Assert.Equal([w.Projector.Id], res.Body.Prop("relatedGatePasses").Items().Select(p => p.Int("assetId")));
    }

    [Fact]
    public async Task Falls_back_to_updated_at_for_passes_decided_before_decided_at_existed()
    {
        await using (var db = api.NewDbContext())
        {
            db.GatePasses.Add(new GatePass
            {
                AssetId = w.Projector.Id, RequestedBy = "Tendai Moyo", Reason = "Legacy", Status = "approved",
                ApprovedBy = "Legacy Approver", DecidedAt = null, CreatedAt = api.Now, UpdatedAt = DaysAgo(0.5),
            });
            await db.SaveChangesAsync();
        }
        var res = await Ask("Show me all gate passes approved this week");
        Assert.Equal(2, res.Body.Prop("relatedGatePasses").Items().Count);
        Assert.Contains("approved by Legacy Approver", res.Body.Str("answer"));
    }

    [Fact]
    public async Task Explains_when_nothing_matches()
    {
        var res = await Ask("Where is the HP printer?");
        Assert.Contains("couldn't find an asset matching", res.Body.Str("answer"));
        Assert.Empty(res.Body.Prop("relatedAssets").Items());
    }

    [Fact]
    public async Task Reports_an_asset_that_left_under_a_gate_pass_as_off_premises()
    {
        await using (var db = api.NewDbContext())
        {
            await db.Assets.Where(a => a.Id == w.Laptop.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.CurrentLocationId, (int?)null));
            db.Movements.Add(new Movement { AssetId = w.Laptop.Id, TriggeredBy = "gate_pass", GatePassId = 1, Timestamp = DaysAgo(1) });
            await db.SaveChangesAsync();
        }
        var res = await Ask("Where is John Doe's laptop?");
        Assert.Contains("is off-premises (left under gate pass #1", res.Body.Str("answer"));
    }

    [Fact]
    public async Task Offers_help_for_unrelated_questions_and_rejects_empty_ones()
    {
        var res = await Ask("What's the weather like?");
        Assert.Equal("help", res.Body.Str("intent"));
        Assert.Contains("Where is the Dell laptop assigned to John Doe?", res.Body.Str("answer"));
        Assert.Equal(400, (await Ask("   ")).Status);
    }

    [Fact]
    public async Task Answers_count_category_status_and_unassigned_questions()
    {
        Assert.Equal("Total assets: 2. Active: 2. In maintenance: 0. Missing: 0. Retired: 0.", (await Ask("How many assets in total?")).Body.Str("answer"));
        Assert.Equal("There is 1 laptop in the register — 1 active, 0 in maintenance, 0 missing, 0 retired.", (await Ask("How many laptops do we have?")).Body.Str("answer"));
        Assert.Equal("No assets are currently missing.", (await Ask("Which assets are missing?")).Body.Str("answer"));
        Assert.Equal("1 asset is currently unassigned:\n• Epson EB-X51 Projector (Projector)", (await Ask("Show me unassigned assets")).Body.Str("answer"));
        Assert.Equal("1 asset assigned to John Doe:\n• Dell Latitude 5440 (Laptop) — TSAS IT Office", (await Ask("What is assigned to John Doe?")).Body.Str("answer"));
    }

    // ── With OpenAI configured (stubbed HTTP; never the real service) ─────────

    private NlQueryService ServiceWithOpenAi(IServiceScope scope, StubHandler handler, string? apiKey = "sk-test")
    {
        var config = scope.ServiceProvider.GetRequiredService<AppConfig>();
        var withKey = new AppConfig
        {
            Timezone = config.Timezone,
            TimeZone = config.TimeZone,
            WorkingHours = config.WorkingHours,
            IdleDays = config.IdleDays,
            OpenAi = config.OpenAi with { ApiKey = apiKey },
            Auth = config.Auth,
        };
        return new NlQueryService(
            withKey,
            scope.ServiceProvider.GetRequiredService<AppClock>(),
            scope.ServiceProvider.GetRequiredService<IntentExecutor>(),
            new OpenAiIntentParser(new HttpClient(handler)),
            NullLogger<NlQueryService>.Instance);
    }

    private static object Completion(string intent, int? days = null) =>
        new { intent, terms = Array.Empty<string>(), assignee = (string?)null, days, status = (string?)null, period = (string?)null, periodDays = (int?)null, category = (string?)null };

    [Fact]
    public async Task Uses_the_models_intent_and_answers_from_the_database()
    {
        using var scope = api.Services.CreateScope();
        var handler = StubHandler.Completion(Completion("not_moved", 30));
        var result = await ServiceWithOpenAi(scope, handler).AnswerAsync("Anything gathering dust for a month?", default);

        Assert.Single(handler.Calls);
        Assert.Equal(("openai", "not_moved"), (result.Source, result.Intent));
        Assert.Equal([w.Projector.Id], result.RelatedAssets.Select(a => a.Id));
    }

    [Fact]
    public async Task Falls_back_to_the_rule_based_parser_when_openai_fails()
    {
        using var scope = api.Services.CreateScope();
        var handler = StubHandler.Json(new { }, HttpStatusCode.ServiceUnavailable);
        var result = await ServiceWithOpenAi(scope, handler).AnswerAsync("Where is the Dell laptop assigned to John Doe?", default);

        Assert.Equal(("rules", "locate_asset"), (result.Source, result.Intent));
        Assert.Contains("TSAS IT Office", result.Answer);
    }

    [Fact]
    public async Task Does_not_call_openai_when_no_key_is_configured()
    {
        using var scope = api.Services.CreateScope();
        var handler = StubHandler.Completion(Completion("count_summary"));
        var result = await ServiceWithOpenAi(scope, handler, apiKey: null).AnswerAsync("Which assets are missing?", default);

        Assert.Empty(handler.Calls);
        Assert.Equal("rules", result.Source);
    }
}
