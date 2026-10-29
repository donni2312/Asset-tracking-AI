using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AssetIQ.Api.Data;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AssetIQ.Api.Tests.Infrastructure;

/// <summary>Response of <see cref="ApiFixture.CallAsync"/>: status code and parsed JSON body (Undefined when empty).</summary>
public sealed record ApiResponse(int Status, JsonElement Body);

/// <summary>
/// The real API (WebApplicationFactory) against a throwaway PostgreSQL database built by applying
/// lib/db/migrations in journal order (exactly what `pnpm db:migrate` does), with a fake clock. Shared by every integration test class; each test resets
/// the tables first, so tests never see each other's rows.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string AuthSecret = "test-secret-do-not-use-in-production";

    /// <summary>Wednesday 14 Oct 2026, 10:00 in Harare (inside working hours).</summary>
    public static readonly DateTimeOffset Wednesday10Am = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    // TRUNCATE bypasses the append-only triggers on audit_log / access_log, which only reject UPDATE and DELETE.
    private const string TruncateAll =
        "TRUNCATE \"users\", \"ai_insights\", \"asset_lifecycle_events\", \"movements\", \"gate_passes\", " +
        "\"asset_attribute_values\", \"asset_upgrades\", \"depreciation_entries\", \"depreciation_locks\", \"bc_number_requests\", " +
        "\"bc_mock_number_series\", \"assets\", \"category_template_fields\", \"asset_categories\", \"group_settings\", " +
        "\"audit_log\", \"access_log\", \"rfid_readers\", \"locations\", \"sbus\" RESTART IDENTITY CASCADE";

    /// <summary>The reference data (category templates, Group settings) of migration 0002, re-inserted after every reset.</summary>
    private string _referenceData = "";

    private TestPostgres? _server;
    private string _databaseName = "";
    private string _adminConnectionString = "";
    private WebApplicationFactory<Program>? _factory;

    public TestClock Clock { get; } = new(Wednesday10Am);

    public string ConnectionString { get; private set; } = "";

    public HttpClient Client { get; private set; } = null!;

    public IServiceProvider Services => _factory!.Services;

    public async Task InitializeAsync()
    {
        _server = await TestPostgres.StartAsync();
        _adminConnectionString = _server.AdminConnectionString;
        _databaseName = $"assetiq_test_{Guid.NewGuid():N}"[..30];

        await using (var admin = new NpgsqlConnection(_adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = _databaseName, Pooling = true }.ConnectionString;
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            foreach (var sql in Migrations.Load())
            {
                await using var command = new NpgsqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync();
            }
        }
        _referenceData = Migrations.ReferenceData();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("DATABASE_URL", ConnectionString);
            builder.UseSetting("ORG_TIMEZONE", "Africa/Harare");
            builder.UseSetting("WORK_HOURS_START", "7");
            builder.UseSetting("WORK_HOURS_END", "18");
            builder.UseSetting("WORK_DAYS", "1-5");
            builder.UseSetting("IDLE_ASSET_DAYS", "7");
            // Tests never call OpenAI; the OpenAI parser is exercised with a stubbed HTTP handler.
            builder.UseSetting("OPENAI_API_KEY", "");
            // A fixed signing key, so a token minted in a test verifies in the same run.
            builder.UseSetting("AUTH_SECRET", AuthSecret);
            builder.UseSetting("LOG_LEVEL", "silent");
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
        });
        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        // Each step runs even if an earlier one fails: the server must always be stopped.
        try
        {
            Client?.Dispose();
            if (_factory is not null) await _factory.DisposeAsync();
        }
        finally
        {
            try
            {
                NpgsqlConnection.ClearAllPools();
                if (_databaseName.Length > 0 && _adminConnectionString.Length > 0)
                {
                    await using var admin = new NpgsqlConnection(_adminConnectionString);
                    await admin.OpenAsync();
                    await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
                    await drop.ExecuteNonQueryAsync();
                }
            }
            catch (NpgsqlException)
            {
                // A throwaway cluster is deleted below anyway; on an external server the database is left behind.
            }
            finally
            {
                if (_server is not null) await _server.DisposeAsync();
            }
        }
    }

    /// <summary>Empty every table, restart the id sequences, reset the clock and the in-memory BC sync state.</summary>
    public async Task ResetAsync()
    {
        Clock.SetUtcNow(Wednesday10Am);
        DefaultToken = null;
        Services.GetRequiredService<BcSyncService>().Reset();
        Services.GetRequiredService<BcSyncService>().SyncDuration = BcSyncService.DefaultSyncDuration;
        Services.GetRequiredService<MockBcFixedAssetClient>().Available = true;
        await using var db = NewDbContext();
        await db.Database.ExecuteSqlRawAsync(TruncateAll);
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var reference = new NpgsqlCommand(_referenceData, connection);
        await reference.ExecuteNonQueryAsync();
    }

    /// <summary>A DbContext on the test database, for arranging rows and checking what the API wrote.</summary>
    public AssetIqDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AssetIqDbContext>().UseNpgsql(ConnectionString).Options);

    public DateTime Now => Clock.GetUtcNow().UtcDateTime;

    public void SetNow(DateTimeOffset instant) => Clock.SetUtcNow(instant);

    public void SetNow(string isoInstant) => Clock.SetUtcNow(DateTimeOffset.Parse(isoInstant, System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Pass as <c>token</c> to send a request signed out.</summary>
    public const string Anonymous = "";

    /// <summary>Token used when a call names none: the administrator of the current <see cref="TestWorld"/>.</summary>
    public string? DefaultToken { get; set; }

    /// <summary>Sends a request as <paramref name="token"/>, or as <see cref="DefaultToken"/> when it is null.</summary>
    public async Task<ApiResponse> CallAsync(string method, string path, object? body = null, string? token = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api" + path);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }
        var bearer = token ?? DefaultToken;
        if (!string.IsNullOrEmpty(bearer)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        using var response = await Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        // Non-JSON bodies (CSV reports) carry no JSON to inspect; callers read those through Client directly.
        var isJson = response.Content.Headers.ContentType?.MediaType?.Contains("json") ?? true;
        var json = text.Length == 0 || !isJson ? default : JsonDocument.Parse(text).RootElement.Clone();
        return new ApiResponse((int)response.StatusCode, json);
    }
}

/// <summary>The SQL migrations copied next to the tests from lib/db/migrations.</summary>
public static class Migrations
{
    private static string Folder => Path.Combine(AppContext.BaseDirectory, "Database", "migrations");

    /// <summary>Every migration's SQL, in journal order.</summary>
    public static IEnumerable<string> Load()
    {
        using var journal = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "meta", "_journal.json")));
        foreach (var entry in journal.RootElement.GetProperty("entries").EnumerateArray())
        {
            yield return File.ReadAllText(Path.Combine(Folder, entry.GetProperty("tag").GetString() + ".sql"));
        }
    }

    /// <summary>The block between "-- reference-data:begin" and "-- reference-data:end" (category templates, settings).</summary>
    public static string ReferenceData()
    {
        var sql = File.ReadAllText(Path.Combine(Folder, "0002_reference_data_and_guards.sql"));
        var start = sql.IndexOf("-- reference-data:begin", StringComparison.Ordinal);
        var end = sql.IndexOf("-- reference-data:end", StringComparison.Ordinal);
        return sql[start..end];
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
