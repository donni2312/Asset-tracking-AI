using AssetIQ.Api.Tests.Infrastructure;
using Npgsql;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>
/// The Phase 1 migrations on a database that already holds data (Supabase, the local cluster): existing assets
/// get a template category, a USD cost and a Business Central number request, and the schema ends up exactly
/// as on a fresh database.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class MigrationTests(ApiFixture api)
{
    [Fact]
    public async Task AU_01_existing_assets_are_backfilled_into_template_categories_and_queued_for_asset_numbers()
    {
        var name = $"assetiq_mig_{Guid.NewGuid():N}"[..28];
        var admin = new NpgsqlConnectionStringBuilder(api.ConnectionString) { Database = "postgres", Pooling = false }.ConnectionString;
        await Exec(admin, $"CREATE DATABASE \"{name}\"");
        var connection = new NpgsqlConnectionStringBuilder(api.ConnectionString) { Database = name, Pooling = false }.ConnectionString;
        try
        {
            var migrations = Migrations.Load().ToList();
            await Exec(connection, migrations[0]); // the schema as `pnpm db:push` created it before Phase 1
            await Exec(connection, """
                INSERT INTO sbus (code, name) VALUES ('TSAS', 'TSAS');
                INSERT INTO assets (name, category, serial_number, rfid_tag, sbu_id, purchase_value) VALUES
                  ('Dell Latitude 5440', 'Laptop', 'SN-1', 'TAG-1', 1, 1350),
                  ('Moisture Analyser MA-100', 'Other', 'SN-2', 'TAG-2', 1, NULL),
                  ('Toyota Hilux', 'Vehicle', 'SN-3', 'TAG-3', 1, 42000);
                """);

            foreach (var sql in migrations.Skip(1)) await Exec(connection, sql);

            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var read = new NpgsqlCommand("""
                SELECT a.name, c.code, a.sub_category, a.original_cost, a.currency, r.status
                FROM assets a JOIN asset_categories c ON c.id = a.category_id JOIN bc_number_requests r ON r.asset_id = a.id
                ORDER BY a.id
                """, db);
            await using var reader = await read.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await reader.ReadAsync())
                rows.Add($"{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}|{(reader.IsDBNull(3) ? "" : reader.GetDecimal(3).ToString("0.00"))}|{(reader.IsDBNull(4) ? "" : reader.GetString(4))}|{reader.GetString(5)}");

            Assert.Equal(
            [
                "Dell Latitude 5440|COMP|Laptop|1350.00|USD|queued",
                "Moisture Analyser MA-100|EQ|Other|||queued",
                "Toyota Hilux|MV|Light vehicle|42000.00|USD|queued",
            ], rows);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await Exec(admin, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
        }
    }

    private static async Task Exec(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
