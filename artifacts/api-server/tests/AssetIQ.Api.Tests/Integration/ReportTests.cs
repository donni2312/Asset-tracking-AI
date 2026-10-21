using System.Net.Http.Headers;
using System.Text;
using AssetIQ.Api.Data;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Tests.Infrastructure;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>CSV report downloads: GET /api/reports/*.csv.</summary>
[Collection(ApiCollection.Name)]
public sealed class ReportTests(ApiFixture api) : IAsyncLifetime
{
    private TestWorld w = null!;

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        w = await TestWorld.SeedAsync(api);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Download(int Status, string? ContentType, string? FileName, byte[] Bytes)
    {
        public string Text => Encoding.UTF8.GetString(Bytes.AsSpan(Bytes.Length >= 3 && Bytes[0] == 0xEF ? 3 : 0));
        public string[] Lines => Text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    private async Task<Download> Get(string path, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api" + path);
        var bearer = token ?? w.ViewerToken;
        if (bearer.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await api.Client.SendAsync(request);
        return new Download(
            (int)response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName,
            await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task The_asset_register_downloads_as_an_excel_friendly_csv()
    {
        var csv = await Get("/reports/assets.csv");

        Assert.Equal((200, "text/csv", "assetiq-assets-2026-10-14.csv"), (csv.Status, csv.ContentType, csv.FileName));
        Assert.Equal([0xEF, 0xBB, 0xBF], csv.Bytes[..3]); // UTF-8 BOM so Excel reads the encoding
        Assert.StartsWith("Asset ID,Name,Category,Serial number,RFID tag,Status,Assigned to,Current location,SBU code,SBU name,", csv.Lines[0]);
        Assert.Contains("Registered (Africa/Harare)", csv.Lines[0]);
        Assert.Equal(3, csv.Lines.Length); // header + two assets, ordered by name
        Assert.StartsWith($"{w.Laptop.Id},Dell Latitude 5440,Laptop,SN-DL-001,TAG-DL-001,Active,John Doe,TSAS IT Office,TSAS,", csv.Lines[1]);
        Assert.EndsWith(",2026-10-14 10:00,2026-10-14 10:00", csv.Lines[1]); // organisation time, not UTC
    }

    [Fact]
    public async Task Reports_apply_the_same_filters_as_the_screens()
    {
        Assert.Equal(2, (await Get("/reports/assets.csv?q=epson")).Lines.Length);
        Assert.Single((await Get($"/reports/assets.csv?sbuId={w.Bak.Id}")).Lines);
        Assert.Equal(2, (await Get("/reports/assets.csv?status=active&category=Laptop")).Lines.Length);
    }

    [Fact]
    public async Task The_movement_log_covers_a_date_range_in_the_organisation_timezone()
    {
        await using (var db = api.NewDbContext())
        {
            db.Movements.AddRange(
                // 23:30 on the 12th and 00:30 on the 14th in Harare.
                new Movement { AssetId = w.Laptop.Id, FromLocationId = w.Office.Id, ToLocationId = w.Gate.Id, TriggeredBy = "rfid", RfidReaderId = w.GateReader.Id, Timestamp = new DateTime(2026, 10, 12, 21, 30, 0, DateTimeKind.Utc) },
                new Movement { AssetId = w.Laptop.Id, FromLocationId = w.Gate.Id, ToLocationId = null, TriggeredBy = "gate_pass", Timestamp = new DateTime(2026, 10, 13, 22, 30, 0, DateTimeKind.Utc) });
            await db.SaveChangesAsync();
        }

        var all = await Get("/reports/movements.csv");
        Assert.Equal(3, all.Lines.Length);
        Assert.Contains(",TSAS IT Office,TSAS Main Gate,RFID scan,TSAS Gate Reader,", all.Lines[1]);
        Assert.Contains(",TSAS Main Gate,Off-premises,Gate pass exit,", all.Lines[2]);

        var day14 = await Get("/reports/movements.csv?from=2026-10-14&to=2026-10-14");
        Assert.Equal(2, day14.Lines.Length);
        Assert.StartsWith($"2,2026-10-14 00:30,", day14.Lines[1]);
    }

    [Fact]
    public async Task Gate_pass_insight_and_lifecycle_reports_list_their_rows()
    {
        await api.CallAsync("POST", "/rfid/simulate", new { rfidTag = "TAG-DL-001", readerId = w.GateReader.Id }); // pending pass + alert
        await api.CallAsync("POST", $"/assets/{w.Projector.Id}/lifecycle", new { eventType = "repair_sent", performedBy = "IT Helpdesk", vendor = "Epson Service" });

        var passes = await Get("/reports/gate-passes.csv?status=pending");
        Assert.Equal(2, passes.Lines.Length);
        Assert.StartsWith("1,Pending,Raised at a gate,", passes.Lines[1]);

        var insights = await Get("/reports/insights.csv");
        Assert.Equal(2, insights.Lines.Length);
        Assert.Contains(",Unauthorized movement,Warning,", insights.Lines[1]);

        var lifecycle = await Get("/reports/lifecycle.csv?eventType=repair_sent");
        Assert.Equal(2, lifecycle.Lines.Length);
        Assert.Contains(",Sent for repair,IT Helpdesk,", lifecycle.Lines[1]);
    }

    [Fact]
    public async Task Free_text_cannot_become_a_spreadsheet_formula()
    {
        await using (var db = api.NewDbContext())
        {
            var laptop = await db.Assets.FindAsync(w.Laptop.Id);
            laptop!.Notes = "=HYPERLINK(\"http://evil.example\",\"click\")";
            laptop.AssignedTo = "Doe, John";
            await db.SaveChangesAsync();
        }
        var line = (await Get("/reports/assets.csv")).Lines[1];
        Assert.Contains(",\"Doe, John\",", line);
        Assert.Contains(",\"'=HYPERLINK(\"\"http://evil.example\"\",\"\"click\"\")\",", line);
    }

    [Fact]
    public async Task Downloads_need_a_signed_in_user_and_valid_dates()
    {
        Assert.Equal(401, (await Get("/reports/assets.csv", ApiFixture.Anonymous)).Status);
        Assert.Equal(200, (await Get("/reports/movements.csv", w.ViewerToken)).Status);
        Assert.Equal(400, (await Get("/reports/movements.csv?from=14/10/2026")).Status);
        Assert.Equal(400, (await Get("/reports/movements.csv?from=2026-10-15&to=2026-10-14")).Status);
        Assert.Equal(400, (await Get("/reports/gate-passes.csv?to=2026-02-30")).Status);
    }
}

public sealed class CsvBuilderTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+27 12", "'+27 12")]
    [InlineData("-5", "'-5")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("", "")]
    public void Quotes_and_neutralises_text(string value, string expected)
    {
        Assert.Equal(expected, CsvBuilder.Cell(value));
    }

    [Fact]
    public void Writes_numbers_invariantly_booleans_as_words_and_nulls_as_empty()
    {
        Assert.Equal("1250.5", CsvBuilder.Cell(1250.5m));
        Assert.Equal("-3", CsvBuilder.Cell(-3));
        Assert.Equal("Yes", CsvBuilder.Cell(true));
        Assert.Equal("", CsvBuilder.Cell(null));
        Assert.Equal("a,,1\r\n", new CsvBuilder().Row("a", null, 1).ToString());
    }
}
