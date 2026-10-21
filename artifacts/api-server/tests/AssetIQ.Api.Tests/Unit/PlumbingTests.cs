using System.Text.Json;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Npgsql;

namespace AssetIQ.Api.Tests.Unit;

public sealed class PlumbingTests
{
    private static readonly TimeZoneInfo Harare = TimeZoneInfo.FindSystemTimeZoneById("Africa/Harare");

    [Fact]
    public void Converts_a_libpq_database_url_to_an_npgsql_connection_string()
    {
        var builder = new NpgsqlConnectionStringBuilder(DatabaseUrl.ToConnectionString("postgres://postgres:p%40ss@localhost:5433/assetiq"));
        Assert.Equal(("localhost", 5433, "assetiq", "postgres", "p@ss"), (builder.Host, builder.Port, builder.Database, builder.Username, builder.Password));

        var replit = new NpgsqlConnectionStringBuilder(DatabaseUrl.ToConnectionString("postgresql://neondb_owner:secret@ep-x.neon.tech/neondb?sslmode=require"));
        Assert.Equal((5432, SslMode.Require), (replit.Port, replit.SslMode));

        const string keyValue = "Host=db;Port=5432;Database=assetiq";
        Assert.Equal(keyValue, DatabaseUrl.ToConnectionString(keyValue));
    }

    [Fact]
    public void Parses_dotenv_lines_like_the_node_loader()
    {
        var values = DotEnvFile.Parse(
        [
            "# comment",
            "",
            "DATABASE_URL=postgres://postgres:postgres@localhost:5433/assetiq",
            "PORT = 8080",
            "AUTH_SECRET=\"quoted value # not a comment\"",
            "OPENAI_API_KEY=",
            "export ORG_TIMEZONE=Africa/Harare",
            "WORK_DAYS=1-5 # trailing comment",
        ]);
        Assert.Equal("postgres://postgres:postgres@localhost:5433/assetiq", values["DATABASE_URL"]);
        Assert.Equal("8080", values["PORT"]);
        Assert.Equal("quoted value # not a comment", values["AUTH_SECRET"]);
        Assert.Equal("", values["OPENAI_API_KEY"]);
        Assert.Equal("Africa/Harare", values["ORG_TIMEZONE"]);
        Assert.Equal("1-5", values["WORK_DAYS"]);
    }

    [Fact]
    public void Reads_bodies_with_the_contracts_required_optional_and_nullable_rules()
    {
        using var document = JsonDocument.Parse("""{ "name": "x", "count": 3, "empty": "", "nothing": null, "float": 1.5, "status": "lost" }""");
        var body = JsonBody.From(document.RootElement);

        Assert.Equal("x", body.RequiredString("name"));
        Assert.Equal(3, body.RequiredInt("count"));
        Assert.Null(body.OptionalString("absent"));
        Assert.False(body.NullableString("absent").IsSet);
        Assert.Equal(new Optional<string?>(true, null), body.NullableString("nothing"));
        Assert.True(body.IsValid);

        body.RequiredString("missing");
        body.RequiredString("empty", minLength: 1);
        body.OptionalString("nothing");
        body.OptionalInt("float");
        body.OptionalEnum("status", ["active", "missing"]);
        Assert.Equal(
        [
            "missing: Required",
            "empty: String must contain at least 1 character(s)",
            "nothing: Expected string, received null",
            "float: Expected integer, received float",
            "status: Invalid enum value. Expected 'active' | 'missing', received 'lost'",
        ], body.Issues);

        var error = Assert.Throws<ApiException>(body.ThrowIfInvalid);
        Assert.Equal(400, error.Status);
    }

    [Fact]
    public void Reports_a_missing_or_non_object_body()
    {
        Assert.False(JsonBody.From(null).IsValid);
        using var array = JsonDocument.Parse("[1]");
        Assert.False(JsonBody.From(array.RootElement).IsValid);
    }

    [Theory]
    [InlineData("2026-10-16", "2026-10-16T00:00:00.000Z")]
    [InlineData("2026-10-17T00:00:00.000Z", "2026-10-17T00:00:00.000Z")]
    [InlineData("2026-10-17T02:00:00+02:00", "2026-10-17T00:00:00.000Z")]
    [InlineData("2026-10-14T06:00:00.1234567Z", "2026-10-14T06:00:00.123Z")]
    public void Reads_dates_like_javascripts_date_constructor(string text, string expectedIso)
    {
        var parsed = JsonBody.ParseDateString(text);
        Assert.NotNull(parsed);
        Assert.Equal(expectedIso, UtcDateTimeJsonConverter.ToIsoString(parsed.Value));
    }

    [Theory]
    [InlineData("2026-02-30")]
    [InlineData("31/02/2026")]
    [InlineData("soon")]
    public void Rejects_impossible_dates(string text)
    {
        Assert.Null(JsonBody.ParseDateString(text));
    }

    [Theory]
    [InlineData("12", 12)]
    [InlineData(" 7 ", 7)]
    [InlineData("7.0", 7)]
    [InlineData("1e3", 1000)]
    [InlineData("0x10", 16)]
    [InlineData("", 0)]
    public void Parses_numbers_like_javascripts_number_function(string text, double expected)
    {
        Assert.Equal(expected, JsNumber.Parse(text));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public void Rejects_route_ids_that_are_not_positive_integers(string raw)
    {
        var error = Assert.Throws<ApiException>(() => RequestValues.Id(raw));
        Assert.Equal((400, $"Invalid id: {raw}"), (error.Status, error.Message));
    }

    [Fact]
    public void Buckets_movements_per_day_in_the_organisation_timezone()
    {
        var points = DashboardService.BucketMovementsByDay(
        [
            new(new DateTime(2026, 10, 13, 22, 30, 0, DateTimeKind.Utc), "rfid"), // 00:30 on the 14th in Harare
            new(new DateTime(2026, 10, 13, 21, 30, 0, DateTimeKind.Utc), "manual"), // 23:30 on the 13th
            new(new DateTime(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc), "gate_pass"),
        ], ["2026-10-13", "2026-10-14"], Harare);

        Assert.Equal(
        [
            ("2026-10-13", 0, 1, 0, 1),
            ("2026-10-14", 1, 0, 1, 2),
        ], points.Select(p => (p.Date, p.Rfid, p.Manual, p.GatePass, p.Total)));
    }

    [Fact]
    public void Writes_timestamps_exactly_like_javascripts_to_iso_string()
    {
        Assert.Equal("2026-10-14T08:20:00.000Z", UtcDateTimeJsonConverter.ToIsoString(new DateTime(2026, 10, 14, 8, 20, 0, DateTimeKind.Utc)));
        Assert.Equal("2026-10-14T08:20:00.000Z", UtcDateTimeJsonConverter.ToIsoString(new DateTime(2026, 10, 14, 8, 20, 0, DateTimeKind.Unspecified)));
        Assert.Equal("2026-10-14T08:20:00.123Z", UtcDateTimeJsonConverter.ToIsoString(new DateTime(2026, 10, 14, 8, 20, 0, 123, DateTimeKind.Utc).AddTicks(4567)));
    }
}
