using AssetIQ.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>
/// The EF Core model maps onto the existing database and nothing else: every mapped property is a real
/// column with the same type and nullability, and every column of every table is mapped. The schema
/// comes from Database/schema.sql (drizzle-kit's DDL for lib/db/src/schema).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SchemaConformanceTests(ApiFixture api)
{
    private sealed record Column(string Table, string Name, string DataType, bool IsNullable);

    [Fact]
    public async Task The_ef_model_matches_the_database_schema_column_for_column()
    {
        var columns = await LoadColumnsAsync();
        await using var db = api.NewDbContext();
        var problems = new List<string>();

        var mappedTables = new HashSet<string>();
        foreach (var entity in db.Model.GetEntityTypes())
        {
            var table = entity.GetTableName()!;
            mappedTables.Add(table);
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            var dbColumns = columns.Where(c => c.Table == table).ToDictionary(c => c.Name);
            if (dbColumns.Count == 0)
            {
                problems.Add($"table {table} does not exist");
                continue;
            }

            var mapped = new HashSet<string>();
            foreach (var property in entity.GetProperties())
            {
                var name = property.GetColumnName(store)!;
                mapped.Add(name);
                if (!dbColumns.TryGetValue(name, out var column))
                {
                    problems.Add($"{table}.{name} is mapped but does not exist");
                    continue;
                }
                if (column.IsNullable != property.IsNullable)
                {
                    problems.Add($"{table}.{name}: database nullable={column.IsNullable}, model nullable={property.IsNullable}");
                }
                if (!string.Equals(column.DataType, property.GetColumnType(store), StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"{table}.{name}: database type {column.DataType}, model type {property.GetColumnType(store)}");
                }
            }
            problems.AddRange(dbColumns.Keys.Except(mapped).Select(c => $"{table}.{c} exists but is not mapped"));
        }
        problems.AddRange(columns.Select(c => c.Table).Distinct().Except(mappedTables).Select(t => $"table {t} is not mapped"));

        Assert.Empty(problems);
    }

    [Fact]
    public async Task Every_table_can_be_queried_through_ef_core()
    {
        await api.ResetAsync();
        await using var db = api.NewDbContext();
        Assert.False(await db.Users.AnyAsync());
        Assert.False(await db.Sbus.AnyAsync());
        Assert.False(await db.Locations.AnyAsync());
        Assert.False(await db.RfidReaders.AnyAsync());
        Assert.False(await db.Assets.AnyAsync());
        Assert.False(await db.Movements.AnyAsync());
        Assert.False(await db.GatePasses.AnyAsync());
        Assert.False(await db.AiInsights.AnyAsync());
        Assert.False(await db.AssetLifecycleEvents.AnyAsync());
    }

    private async Task<List<Column>> LoadColumnsAsync()
    {
        await using var connection = new NpgsqlConnection(api.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT table_name, column_name, data_type, is_nullable = 'YES'
            FROM information_schema.columns
            WHERE table_schema = 'public'
            ORDER BY table_name, ordinal_position
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<Column>();
        while (await reader.ReadAsync())
        {
            columns.Add(new Column(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
        }
        return columns;
    }
}
