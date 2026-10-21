using System.Net;
using System.Text;
using System.Text.Json;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Services.NlQuery;

namespace AssetIQ.Api.Tests.Unit;

/// <summary>Replies with a canned response and records every request it was sent.</summary>
public sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

    public static StubHandler Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") });

    /// <summary>A Chat Completions reply whose message content is <paramref name="content"/> serialized as JSON.</summary>
    public static StubHandler Completion(object content) =>
        Json(new { choices = new[] { new { message = new { content = JsonSerializer.Serialize(content) } } } });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add((request, body));
        return respond(request);
    }
}

/// <summary>Ported from src/services/nl-query/openai-parser.test.ts.</summary>
public sealed class OpenAiIntentParserTests
{
    private static readonly OpenAiOptions Options = new("sk-test", "gpt-test", "https://api.example.test/v1", "authorization", 2000);

    /// <summary>A reply with every key present (null where it does not apply), overridden per test.</summary>
    private static Dictionary<string, object?> Flat(params (string Key, object? Value)[] overrides)
    {
        var flat = new Dictionary<string, object?>
        {
            ["intent"] = "help", ["terms"] = Array.Empty<string>(), ["assignee"] = null, ["days"] = null,
            ["status"] = null, ["period"] = null, ["periodDays"] = null, ["category"] = null,
        };
        foreach (var (key, value) in overrides) flat[key] = value;
        return flat;
    }

    private static FlatIntent ToFlat(Dictionary<string, object?> values)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(values));
        return OpenAiIntentParser.ParseFlat(document.RootElement);
    }

    [Fact]
    public async Task Sends_only_the_question_with_a_strict_json_schema_and_maps_the_reply_to_an_intent()
    {
        var handler = StubHandler.Completion(Flat(("intent", "locate_asset"), ("terms", new[] { "Dell", "laptop" }), ("assignee", "John Doe")));
        var intent = await new OpenAiIntentParser(new HttpClient(handler)).ParseAsync("Where is the Dell laptop assigned to John Doe?", Options, default);

        Assert.Equal(new LocateAssetIntent(["dell", "laptop"], "john doe"), intent);

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal("https://api.example.test/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", request.Headers.Authorization!.ToString());
        using var sent = JsonDocument.Parse(body);
        Assert.Equal("gpt-test", sent.RootElement.GetProperty("model").GetString());
        Assert.True(sent.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("strict").GetBoolean());
        var last = sent.RootElement.GetProperty("messages").EnumerateArray().Last();
        Assert.Equal("user", last.GetProperty("role").GetString());
        Assert.Equal("Where is the Dell laptop assigned to John Doe?", last.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Supports_azure_style_api_key_authentication()
    {
        var handler = StubHandler.Completion(Flat(("intent", "count_summary")));
        await new OpenAiIntentParser(new HttpClient(handler)).ParseAsync("How many assets?", Options with { AuthHeader = "api-key" }, default);
        var (request, _) = Assert.Single(handler.Calls);
        Assert.Equal("sk-test", Assert.Single(request.Headers.GetValues("api-key")));
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task Throws_on_http_errors_without_leaking_details()
    {
        var handler = StubHandler.Json(new { error = new { code = "invalid_api_key", message = "Incorrect API key provided: sk-t***" } }, HttpStatusCode.Unauthorized);
        var ex = await Assert.ThrowsAsync<OpenAiParseException>(() => new OpenAiIntentParser(new HttpClient(handler)).ParseAsync("x", Options, default));
        Assert.Contains("HTTP 401 (invalid_api_key)", ex.Message);
        Assert.DoesNotContain("sk-t", ex.Message);
    }

    [Fact]
    public async Task Throws_on_malformed_or_schema_violating_replies()
    {
        var notJson = StubHandler.Json(new { choices = new[] { new { message = new { content = "not json" } } } });
        await Assert.ThrowsAsync<OpenAiParseException>(() => new OpenAiIntentParser(new HttpClient(notJson)).ParseAsync("x", Options, default));

        var wrongShape = StubHandler.Completion(new { intent = "teleport_asset" });
        await Assert.ThrowsAsync<OpenAiParseException>(() => new OpenAiIntentParser(new HttpClient(wrongShape)).ParseAsync("x", Options, default));
    }

    [Fact]
    public async Task Refuses_to_run_without_an_api_key()
    {
        var ex = await Assert.ThrowsAsync<OpenAiParseException>(() =>
            new OpenAiIntentParser(new HttpClient(StubHandler.Json(new { }))).ParseAsync("x", Options with { ApiKey = null }, default));
        Assert.Contains("not configured", ex.Message);
    }

    [Fact]
    public void Maps_gate_pass_queries_and_defaults_the_period()
    {
        Assert.Equal(new GatePassesIntent("approved", "this_week", null),
            OpenAiIntentParser.FlatToIntent(ToFlat(Flat(("intent", "gate_passes"), ("status", "approved"), ("period", "this_week")))));
        Assert.Equal(new GatePassesIntent(null, "all", null), OpenAiIntentParser.FlatToIntent(ToFlat(Flat(("intent", "gate_passes")))));
    }

    [Fact]
    public void Normalises_categories_and_rejects_unknown_ones()
    {
        Assert.Equal(new CategoryCountIntent("Laptop"), OpenAiIntentParser.FlatToIntent(ToFlat(Flat(("intent", "category_count"), ("category", "laptops")))));
        Assert.Throws<OpenAiParseException>(() => OpenAiIntentParser.FlatToIntent(ToFlat(Flat(("intent", "category_count"), ("category", "spaceships")))));
    }

    [Fact]
    public void Rejects_intents_that_are_missing_required_parameters()
    {
        Assert.Throws<OpenAiParseException>(() => OpenAiIntentParser.FlatToIntent(ToFlat(Flat(("intent", "locate_asset")))));
        Assert.Throws<OpenAiParseException>(() => OpenAiIntentParser.FlatToIntent(ToFlat(Flat(("intent", "assets_assigned_to")))));
        Assert.Throws<OpenAiParseException>(() => OpenAiIntentParser.FlatToIntent(ToFlat(Flat(("intent", "assets_by_status"), ("status", "approved")))));
    }

    [Fact]
    public void Defaults_not_moved_to_30_days()
    {
        Assert.Equal(new NotMovedIntent(30), OpenAiIntentParser.FlatToIntent(ToFlat(Flat(("intent", "not_moved")))));
    }

    [Fact]
    public void Rejects_replies_with_missing_keys()
    {
        using var document = JsonDocument.Parse("""{ "intent": "help" }""");
        Assert.Throws<OpenAiParseException>(() => OpenAiIntentParser.ParseFlat(document.RootElement));
    }
}
