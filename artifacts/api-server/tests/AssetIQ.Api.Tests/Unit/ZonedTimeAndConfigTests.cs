using AssetIQ.Api.Configuration;
using AssetIQ.Api.Infrastructure;

namespace AssetIQ.Api.Tests.Unit;

/// <summary>Ported from src/lib/time.test.ts: Harare is UTC+2 with no daylight saving.</summary>
public sealed class ZonedTimeAndConfigTests
{
    private static readonly TimeZoneInfo Harare = TimeZoneInfo.FindSystemTimeZoneById("Africa/Harare");
    private static readonly WorkingHours Hours = new(7, 18, [1, 2, 3, 4, 5]);

    private static DateTime Utc(string iso) => DateTime.Parse(iso, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

    [Fact]
    public void Reads_wall_clock_parts_in_the_organisation_timezone()
    {
        var p = ZonedTime.Parts(Utc("2026-10-14T08:00:00Z"), Harare);
        Assert.Equal((2026, 10, 14, 10, 0, 3), (p.Year, p.Month, p.Day, p.Hour, p.Minute, p.Weekday));
    }

    [Fact]
    public void Computes_the_local_calendar_day_not_the_utc_day()
    {
        // 23:30 UTC on the 13th is already 01:30 on the 14th in Harare
        Assert.Equal("2026-10-14", ZonedTime.DateKey(Utc("2026-10-13T23:30:00Z"), Harare));
    }

    [Fact]
    public void Finds_local_midnight_and_the_monday_that_starts_the_week()
    {
        var wednesday = Utc("2026-10-14T08:00:00Z");
        Assert.Equal("2026-10-13T22:00:00.000Z", UtcDateTimeJsonConverter.ToIsoString(ZonedTime.StartOfZonedDay(wednesday, Harare)));
        Assert.Equal("2026-10-11T22:00:00.000Z", UtcDateTimeJsonConverter.ToIsoString(ZonedTime.StartOfZonedWeek(wednesday, Harare)));
        // A Monday is its own week start; a Sunday belongs to the week that started six days earlier
        Assert.Equal("2026-10-11T22:00:00.000Z", UtcDateTimeJsonConverter.ToIsoString(ZonedTime.StartOfZonedWeek(Utc("2026-10-12T05:00:00Z"), Harare)));
        Assert.Equal("2026-10-11T22:00:00.000Z", UtcDateTimeJsonConverter.ToIsoString(ZonedTime.StartOfZonedWeek(Utc("2026-10-18T12:00:00Z"), Harare)));
    }

    [Fact]
    public void Lists_the_last_n_calendar_days_oldest_first()
    {
        Assert.Equal(["2026-10-12", "2026-10-13", "2026-10-14"], ZonedTime.LastNDayKeys(Utc("2026-10-14T08:00:00Z"), 3, Harare));
    }

    [Fact]
    public void Formats_times_for_messages()
    {
        Assert.Equal("Sat 17 Oct 02:14", ZonedTime.FormatZoned(Utc("2026-10-17T00:14:00Z"), Harare));
        Assert.Equal("Mon–Fri 07:00–18:00", ZonedTime.DescribeWorkingHours(Hours));
    }

    [Theory]
    [InlineData("2026-10-14T05:00:00Z", true)] // Wed 07:00
    [InlineData("2026-10-14T15:59:00Z", true)] // Wed 17:59
    [InlineData("2026-10-14T04:59:00Z", false)] // Wed 06:59
    [InlineData("2026-10-14T16:00:00Z", false)] // Wed 18:00
    [InlineData("2026-10-17T08:00:00Z", false)] // Sat 10:00
    public void Working_hours_are_weekdays_from_07_00_to_17_59_local(string instant, bool inside)
    {
        Assert.Equal(inside, ZonedTime.IsWithinWorkingHours(Utc(instant), Harare, Hours));
    }

    [Fact]
    public void Parses_working_day_lists_and_ranges()
    {
        Assert.Equal([1, 2, 3, 4, 5], AppConfig.ParseWorkDays("1-5"));
        Assert.Equal([0, 6], AppConfig.ParseWorkDays("0,6"));
        Assert.Equal([1, 2, 3, 5], AppConfig.ParseWorkDays("1-3,5"));
        Assert.Equal([1, 2, 3, 4, 5], AppConfig.ParseWorkDays("nonsense"));
    }

    [Fact]
    public void Falls_back_to_safe_defaults_for_invalid_values()
    {
        var cfg = AppConfig.Load(Env(("ORG_TIMEZONE", "Not/AZone"), ("WORK_HOURS_START", "25"), ("OPENAI_API_KEY", "  ")));
        Assert.Equal("Africa/Harare", cfg.Timezone);
        Assert.Equal(7, cfg.WorkingHours.StartHour);
        Assert.Null(cfg.OpenAi.ApiKey);
    }

    [Fact]
    public void Reads_the_openai_settings()
    {
        var cfg = AppConfig.Load(Env(("OPENAI_API_KEY", "sk-test"), ("OPENAI_BASE_URL", "https://example.test/openai/v1/"), ("OPENAI_AUTH_HEADER", "api-key")));
        Assert.Equal("sk-test", cfg.OpenAi.ApiKey);
        Assert.Equal("https://example.test/openai/v1", cfg.OpenAi.BaseUrl);
        Assert.Equal("api-key", cfg.OpenAi.AuthHeader);
    }

    [Fact]
    public void Never_uses_a_short_or_missing_auth_secret()
    {
        var missing = AppConfig.Load(Env());
        Assert.False(missing.Auth.SecretConfigured);
        Assert.Equal(96, missing.Auth.Secret.Length); // 48 random bytes, hex

        var tooShort = AppConfig.Load(Env(("AUTH_SECRET", "short")));
        Assert.False(tooShort.Auth.SecretConfigured);
        Assert.NotEqual("short", tooShort.Auth.Secret);

        var configured = AppConfig.Load(Env(("AUTH_SECRET", "  a-long-enough-secret  ")));
        Assert.True(configured.Auth.SecretConfigured);
        Assert.Equal("a-long-enough-secret", configured.Auth.Secret);
    }

    [Fact]
    public void Keeps_numbers_in_range_and_the_working_day_end_after_its_start()
    {
        var cfg = AppConfig.Load(Env(("WORK_HOURS_START", "9"), ("WORK_HOURS_END", "8"), ("IDLE_ASSET_DAYS", "0"), ("AUTH_TOKEN_TTL_HOURS", "24")));
        Assert.Equal(new[] { 9, 10 }, new[] { cfg.WorkingHours.StartHour, cfg.WorkingHours.EndHour });
        Assert.Equal(7, cfg.IdleDays);
        Assert.Equal(24, cfg.Auth.TokenTtlHours);
    }

    private static Func<string, string?> Env(params (string Key, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Key, v => v.Value);
        return key => map.GetValueOrDefault(key);
    }
}
