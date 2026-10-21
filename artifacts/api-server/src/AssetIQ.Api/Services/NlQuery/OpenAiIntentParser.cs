using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AssetIQ.Api.Configuration;

namespace AssetIQ.Api.Services.NlQuery;

/// <summary>Any failure of the OpenAI layer; the caller falls back to the rule-based parser.</summary>
public sealed class OpenAiParseException(string message) : Exception(message);

/// <summary>The model's reply: every key present, null where it does not apply.</summary>
public sealed record FlatIntent(
    string Intent,
    IReadOnlyList<string> Terms,
    string? Assignee,
    int? Days,
    string? Status,
    string? Period,
    int? PeriodDays,
    string? Category);

/// <summary>
/// Optional OpenAI layer: the model only translates the question into a structured intent (JSON Schema
/// structured output); the intent is then executed by the same deterministic code as the rule-based
/// parser, so only the question text leaves the system, answers always come from the database, and any
/// failure (no key, timeout, HTTP error, invalid JSON) falls back to the rules.
/// </summary>
public sealed class OpenAiIntentParser(HttpClient http)
{
    private static readonly IReadOnlyList<string> StatusValues = [.. Vocabulary.GatePassStatuses, .. Vocabulary.AssetStatuses];

    public static readonly string SystemPrompt = string.Join("\n",
        "You translate questions about TSL AssetIQ, an enterprise asset register, into a JSON intent.",
        "Choose exactly one intent:",
        "- locate_asset: where a specific asset is. Put brand/model/category words in \"terms\" (e.g. [\"dell\",\"laptop\"]) and a person's name in \"assignee\" if the asset is described by who holds it.",
        "- assets_assigned_to: list the assets held by a person (\"assignee\" required).",
        "- not_moved: assets that have not moved for a number of days (\"days\"; convert weeks/months to days, default 30).",
        $"- gate_passes: list gate passes. \"status\" is pending, approved, rejected or null for all; \"period\" is one of {string.Join(", ", Periods.All)} (\"periodDays\" only for last_n_days).",
        $"- assets_by_status: assets whose status is {string.Join(", ", Vocabulary.AssetStatuses)} (\"status\").",
        $"- category_count: how many / which assets of a category (\"category\" is one of {string.Join(", ", Vocabulary.Categories)}).",
        "- count_summary: overall totals.",
        "- unassigned: assets not assigned to anyone.",
        "- help: anything else.",
        "Use null (or [] for terms) for fields that do not apply. Never invent asset names, tags or people.");

    /// <summary>Strict JSON schema sent to the Chat Completions API (every key required; null = not applicable).</summary>
    public static JsonObject IntentJsonSchema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("intent", "terms", "assignee", "days", "status", "period", "periodDays", "category"),
        ["properties"] = new JsonObject
        {
            ["intent"] = new JsonObject { ["type"] = "string", ["enum"] = Strings(IntentKinds.All) },
            ["terms"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            ["assignee"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["days"] = new JsonObject { ["type"] = new JsonArray("integer", "null") },
            ["status"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["enum"] = WithNull(StatusValues) },
            ["period"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["enum"] = WithNull(Periods.All) },
            ["periodDays"] = new JsonObject { ["type"] = new JsonArray("integer", "null") },
            ["category"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
        },
    };

    public async Task<Intent> ParseAsync(string question, OpenAiOptions options, CancellationToken ct)
    {
        if (options.ApiKey is null) throw new OpenAiParseException("OPENAI_API_KEY is not configured");

        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = question }),
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "asset_query_intent", ["strict"] = true, ["schema"] = IntentJsonSchema() },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl}/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (options.AuthHeader == "api-key") request.Headers.Add("api-key", options.ApiKey);
        else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.TimeoutMs);
        using var response = await http.SendAsync(request, timeout.Token);
        var text = await response.Content.ReadAsStringAsync(timeout.Token);

        if (!response.IsSuccessStatusCode)
        {
            // Only the status and error code are surfaced — never the request or the key.
            var code = ErrorCode(text);
            throw new OpenAiParseException($"OpenAI request failed with HTTP {(int)response.StatusCode}{(string.IsNullOrEmpty(code) ? "" : $" ({code})")}");
        }

        string? content;
        try
        {
            using var payload = JsonDocument.Parse(text);
            var message = payload.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("message", out var m) ? m : default;
            if (message.ValueKind == JsonValueKind.Object && message.TryGetProperty("refusal", out var refusal)
                && refusal.ValueKind == JsonValueKind.String && refusal.GetString()!.Length > 0)
            {
                throw new OpenAiParseException("OpenAI refused to parse the question");
            }
            content = message.ValueKind == JsonValueKind.Object && message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;
        }
        catch (JsonException)
        {
            throw new OpenAiParseException("OpenAI returned an unreadable response");
        }
        if (string.IsNullOrEmpty(content)) throw new OpenAiParseException("OpenAI returned an empty response");

        try
        {
            using var intentJson = JsonDocument.Parse(content);
            return FlatToIntent(ParseFlat(intentJson.RootElement));
        }
        catch (JsonException)
        {
            throw new OpenAiParseException("OpenAI returned an intent that is not JSON");
        }
    }

    /// <summary>Validate the reply against the schema; throws when a key is missing or has the wrong type.</summary>
    public static FlatIntent ParseFlat(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new OpenAiParseException("The intent is not a JSON object");

        var intent = Enum(root, "intent", IntentKinds.All, nullable: false)!;
        var termsElement = Required(root, "terms");
        if (termsElement.ValueKind != JsonValueKind.Array || termsElement.EnumerateArray().Any(t => t.ValueKind != JsonValueKind.String))
        {
            throw new OpenAiParseException("terms must be an array of strings");
        }
        return new FlatIntent(
            intent,
            termsElement.EnumerateArray().Select(t => t.GetString()!).ToList(),
            NullableString(root, "assignee"),
            NullableInt(root, "days"),
            Enum(root, "status", StatusValues, nullable: true),
            Enum(root, "period", Periods.All, nullable: true),
            NullableInt(root, "periodDays"),
            NullableString(root, "category"));
    }

    /// <summary>Convert the model's flat JSON into an Intent; throws when required parameters are missing.</summary>
    public static Intent FlatToIntent(FlatIntent flat)
    {
        var assignee = flat.Assignee?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(assignee)) assignee = null;
        switch (flat.Intent)
        {
            case IntentKinds.LocateAsset:
            {
                var terms = flat.Terms.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).ToList();
                if (terms.Count == 0 && assignee is null) throw new OpenAiParseException("locate_asset needs terms or an assignee");
                return new LocateAssetIntent(terms, assignee);
            }
            case IntentKinds.AssetsAssignedTo:
                return assignee is null
                    ? throw new OpenAiParseException("assets_assigned_to needs an assignee")
                    : new AssetsAssignedToIntent(assignee);
            case IntentKinds.NotMoved:
                return new NotMovedIntent(flat.Days is > 0 ? flat.Days.Value : 30);
            case IntentKinds.GatePasses:
            {
                var period = flat.Period ?? Periods.AllTime;
                return new GatePassesIntent(
                    flat.Status is not null && Vocabulary.GatePassStatuses.Contains(flat.Status) ? flat.Status : null,
                    period,
                    period == Periods.LastNDays ? Math.Max(1, flat.PeriodDays ?? 7) : null);
            }
            case IntentKinds.AssetsByStatus:
                return flat.Status is not null && Vocabulary.AssetStatuses.Contains(flat.Status)
                    ? new AssetsByStatusIntent(flat.Status)
                    : throw new OpenAiParseException("assets_by_status needs an asset status");
            case IntentKinds.CategoryCount:
            {
                var category = flat.Category is null ? null : Vocabulary.CategoryFromWord(flat.Category);
                return category is null
                    ? throw new OpenAiParseException("category_count needs a known category")
                    : new CategoryCountIntent(category);
            }
            case IntentKinds.CountSummary:
                return new CountSummaryIntent();
            case IntentKinds.Unassigned:
                return new UnassignedIntent();
            default:
                return new HelpIntent();
        }
    }

    private static JsonElement Required(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) ? value : throw new OpenAiParseException($"{name} is missing");

    private static string? NullableString(JsonElement root, string name)
    {
        var value = Required(root, name);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => throw new OpenAiParseException($"{name} must be a string or null"),
        };
    }

    private static int? NullableInt(JsonElement root, string name)
    {
        var value = Required(root, name);
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d) && Math.Floor(d) == d && d is >= int.MinValue and <= int.MaxValue)
        {
            return (int)d;
        }
        throw new OpenAiParseException($"{name} must be an integer or null");
    }

    private static string? Enum(JsonElement root, string name, IReadOnlyList<string> allowed, bool nullable)
    {
        var value = Required(root, name);
        if (nullable && value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.String && allowed.Contains(value.GetString()!)) return value.GetString();
        throw new OpenAiParseException($"{name} has an unexpected value");
    }

    private static string? ErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object) return null;
            foreach (var key in new[] { "code", "type" })
            {
                if (!error.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) continue;
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
            }
            return null;
        }
        catch (JsonException)
        {
            return null; // unparsable error bodies carry no code
        }
    }

    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    private static JsonArray WithNull(IEnumerable<string> values) =>
        new(values.Select(v => (JsonNode?)JsonValue.Create(v)).Append(null).ToArray());
}
