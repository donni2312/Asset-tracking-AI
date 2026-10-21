using System.Security.Cryptography;
using AssetIQ.Api.Infrastructure;

namespace AssetIQ.Api.Configuration;

/// <summary>Working hours in the organisation time zone. EndHour is exclusive: 18 means "until 17:59".</summary>
public sealed record WorkingHours(int StartHour, int EndHour, IReadOnlyList<int> Days);

/// <param name="ApiKey">null = OpenAI disabled; the rule-based parser answers every question.</param>
/// <param name="Model">Chat Completions model.</param>
/// <param name="BaseUrl">OpenAI-compatible base URL (OpenAI, or an Azure OpenAI v1 endpoint), without a trailing slash.</param>
/// <param name="AuthHeader">"authorization" sends <c>Authorization: Bearer key</c>; "api-key" sends <c>api-key: key</c> (Azure).</param>
/// <param name="TimeoutMs">Request timeout; on expiry the rule-based parser answers.</param>
public sealed record OpenAiOptions(string? ApiKey, string Model, string BaseUrl, string AuthHeader, int TimeoutMs);

/// <param name="Secret">HMAC key for bearer tokens. Random per process when AUTH_SECRET is unset or too short.</param>
/// <param name="TokenTtlHours">How long a sign-in lasts.</param>
/// <param name="SecretConfigured">True when AUTH_SECRET was supplied: tokens then survive a restart.</param>
public sealed record AuthOptions(string Secret, int TokenTtlHours, bool SecretConfigured);

/// <summary>
/// Runtime configuration from environment variables (and the repository .env file in development),
/// with the MVP defaults. See .env.example at the repository root for the full list.
/// </summary>
public sealed class AppConfig
{
    public const string DefaultTimezone = "Africa/Harare";
    public const string DefaultOpenAiModel = "gpt-4o-mini";

    public required string Timezone { get; init; }
    public required TimeZoneInfo TimeZone { get; init; }
    public required WorkingHours WorkingHours { get; init; }
    /// <summary>An active asset with no movement for this many days raises an idle_asset insight.</summary>
    public required int IdleDays { get; init; }
    public required OpenAiOptions OpenAi { get; init; }
    public required AuthOptions Auth { get; init; }

    public static AppConfig Load(IConfiguration configuration) => Load(key => configuration[key]);

    public static AppConfig Load(Func<string, string?> env)
    {
        var startHour = Int(env("WORK_HOURS_START"), 7, 0, 23);
        var endHour = Int(env("WORK_HOURS_END"), 18, 1, 24);
        var (timezone, zone) = ValidTimeZone(env("ORG_TIMEZONE"));
        var openAiKey = env("OPENAI_API_KEY")?.Trim();
        var baseUrl = env("OPENAI_BASE_URL")?.Trim();
        var model = env("OPENAI_MODEL")?.Trim();
        var secret = env("AUTH_SECRET")?.Trim();
        var secretConfigured = secret is { Length: >= 16 };

        return new AppConfig
        {
            Timezone = timezone,
            TimeZone = zone,
            WorkingHours = new WorkingHours(startHour, endHour > startHour ? endHour : Math.Min(startHour + 1, 24), ParseWorkDays(env("WORK_DAYS"))),
            IdleDays = Int(env("IDLE_ASSET_DAYS"), 7, 1, 365),
            OpenAi = new OpenAiOptions(
                string.IsNullOrEmpty(openAiKey) ? null : openAiKey,
                string.IsNullOrEmpty(model) ? DefaultOpenAiModel : model,
                (string.IsNullOrEmpty(baseUrl) ? "https://api.openai.com/v1" : baseUrl).TrimEnd('/'),
                string.Equals(env("OPENAI_AUTH_HEADER")?.Trim(), "api-key", StringComparison.OrdinalIgnoreCase) ? "api-key" : "authorization",
                Int(env("OPENAI_TIMEOUT_MS"), 8000, 1000, 60000)),
            // Never fall back to a hard-coded signing key: anyone who knows it could mint admin tokens.
            // Without AUTH_SECRET the process signs with a random key, so sessions end on restart.
            Auth = new AuthOptions(
                secretConfigured ? secret! : Convert.ToHexString(RandomNumberGenerator.GetBytes(48)).ToLowerInvariant(),
                Int(env("AUTH_TOKEN_TTL_HOURS"), 12, 1, 720),
                secretConfigured),
        };
    }

    /// <summary>"1-5" → [1..5]; "1,3,5" → [1,3,5]; "0,6" → weekends. 0 = Sunday. Defaults to Monday–Friday.</summary>
    public static IReadOnlyList<int> ParseWorkDays(string? raw)
    {
        int[] fallback = [1, 2, 3, 4, 5];
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var days = new SortedSet<int>();
        foreach (var part in raw.Split(','))
        {
            var bounds = part.Split('-');
            var from = JsNumber.Parse(bounds[0]);
            if (!JsNumber.IsInteger(from)) continue;
            var to = bounds.Length == 1 ? from : JsNumber.Parse(bounds[1]);
            if (!JsNumber.IsInteger(to)) continue;
            // Only 0–6 can survive, so clamp before looping.
            for (var d = Math.Max(from, 0); d <= Math.Min(to, 6); d++) days.Add((int)d);
        }
        return days.Count > 0 ? days.ToArray() : fallback;
    }

    private static int Int(string? value, int fallback, int min, int max)
    {
        if (value is null || value.Trim().Length == 0) return fallback;
        var n = JsNumber.Parse(value);
        return JsNumber.IsInteger(n) && n >= min && n <= max ? (int)n : fallback;
    }

    private static (string Name, TimeZoneInfo Zone) ValidTimeZone(string? raw)
    {
        var name = raw?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            try
            {
                return (name, TimeZoneInfo.FindSystemTimeZoneById(name));
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // fall through to the default
            }
        }
        return (DefaultTimezone, TimeZoneInfo.FindSystemTimeZoneById(DefaultTimezone));
    }
}
