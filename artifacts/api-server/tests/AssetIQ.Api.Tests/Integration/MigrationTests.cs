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

    [Fact]
    public async Task Phase2_admin_accounts_become_group_it_and_operator_and_viewer_accounts_are_disabled_not_deleted()
    {
        var name = $"assetiq_mig_{Guid.NewGuid():N}"[..28];
        var admin = new NpgsqlConnectionStringBuilder(api.ConnectionString) { Database = "postgres", Pooling = false }.ConnectionString;
        await Exec(admin, $"CREATE DATABASE \"{name}\"");
        var connection = new NpgsqlConnectionStringBuilder(api.ConnectionString) { Database = name, Pooling = false }.ConnectionString;
        try
        {
            var migrations = Migrations.Load().ToList();
            foreach (var sql in migrations.Take(4)) await Exec(connection, sql); // up to 0003
            await Exec(connection, """
                INSERT INTO users (username, name, role, password_hash) VALUES
                  ('admin', 'Admin', 'admin', 'x'), ('operator', 'Operator', 'operator', 'x'), ('viewer', 'Viewer', 'viewer', 'x'), ('gf', 'GF', 'group_finance', 'x');
                """);
            foreach (var sql in migrations.Skip(4)) await Exec(connection, sql);

            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var read = new NpgsqlCommand("SELECT username, role, active FROM users ORDER BY id", db);
            await using var reader = await read.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await reader.ReadAsync()) rows.Add($"{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetBoolean(2)}");
            Assert.Equal(["admin|group_it|True", "operator|operator|False", "viewer|viewer|False", "gf|group_finance|True"], rows);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await Exec(admin, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
        }
    }

    [Fact]
    public async Task Phase3_the_original_statuses_are_mapped_onto_the_state_machine()
    {
        var name = $"assetiq_mig_{Guid.NewGuid():N}"[..28];
        var admin = new NpgsqlConnectionStringBuilder(api.ConnectionString) { Database = "postgres", Pooling = false }.ConnectionString;
        await Exec(admin, $"CREATE DATABASE \"{name}\"");
        var connection = new NpgsqlConnectionStringBuilder(api.ConnectionString) { Database = name, Pooling = false }.ConnectionString;
        try
        {
            var migrations = Migrations.Load().ToList();
            foreach (var sql in migrations.Take(6)) await Exec(connection, sql); // up to 0005
            await Exec(connection, """
                INSERT INTO sbus (code, name) VALUES ('TSAS', 'TSAS');
                INSERT INTO locations (name, type, building, sbu_id) VALUES ('TSAS Office', 'office', 'TSAS House', 1);
                INSERT INTO assets (name, category, category_id, status, assigned_to, current_location_id, sbu_id) VALUES
                  ('Held laptop', 'Laptop', 1, 'active', 'John Doe', 1, 1),
                  ('Spare laptop', 'Laptop', 1, 'active', 'TSAS IT store', 1, 1),
                  ('Laptop on a gate pass', 'Laptop', 1, 'active', 'Rudo Moyo', NULL, 1),
                  ('Projector in repair', 'Projector', 4, 'maintenance', NULL, 1, 1),
                  ('Old printer', 'Printer', 4, 'retired', NULL, 1, 1),
                  ('Lost phone', 'Phone', 2, 'missing', 'Tendai', NULL, 1),
                  ('New laptop', 'Laptop', 1, 'pending_asset_number', 'TSAS IT store', 1, 1);
                INSERT INTO movements (asset_id, from_location_id, to_location_id, triggered_by) VALUES (3, 1, NULL, 'gate_pass');
                """);
            foreach (var sql in migrations.Skip(6)) await Exec(connection, sql);

            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var read = new NpgsqlCommand("SELECT status, lost_date IS NOT NULL, disposal_date IS NOT NULL FROM assets ORDER BY id", db);
            await using var reader = await read.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await reader.ReadAsync()) rows.Add($"{reader.GetString(0)}|{reader.GetBoolean(1)}|{reader.GetBoolean(2)}");
            Assert.Equal(
            [
                "in_use|False|False", "in_store|False|False", "out_on_gate_pass|False|False", "in_repair|False|False",
                "disposed|False|True", "lost_stolen|True|False", "pending_asset_number|False|False",
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
