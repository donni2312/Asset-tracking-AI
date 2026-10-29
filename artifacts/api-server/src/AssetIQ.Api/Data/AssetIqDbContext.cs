using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AssetIQ.Api.Data;

/// <summary>
/// EF Core mapping of the AssetIQ database. The schema is owned by lib/db (the Drizzle schema and the SQL
/// migrations in lib/db/migrations, applied by `pnpm db:migrate`); this context never creates or migrates
/// tables, it only maps onto them.
/// </summary>
public sealed class AssetIqDbContext(DbContextOptions<AssetIqDbContext> options) : DbContext(options)
{
    public DbSet<Sbu> Sbus => Set<Sbu>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<RfidReader> RfidReaders => Set<RfidReader>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Movement> Movements => Set<Movement>();
    public DbSet<GatePass> GatePasses => Set<GatePass>();
    public DbSet<AiInsight> AiInsights => Set<AiInsight>();
    public DbSet<AssetLifecycleEvent> AssetLifecycleEvents => Set<AssetLifecycleEvent>();
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();
    public DbSet<CategoryTemplateField> CategoryTemplateFields => Set<CategoryTemplateField>();
    public DbSet<AssetAttributeValue> AssetAttributeValues => Set<AssetAttributeValue>();
    public DbSet<GroupSetting> GroupSettings => Set<GroupSetting>();
    public DbSet<AssetUpgrade> AssetUpgrades => Set<AssetUpgrade>();
    public DbSet<DepreciationEntry> DepreciationEntries => Set<DepreciationEntry>();
    public DbSet<DepreciationLock> DepreciationLocks => Set<DepreciationLock>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();
    public DbSet<AccessLogEntry> AccessLog => Set<AccessLogEntry>();
    public DbSet<BcNumberRequest> BcNumberRequests => Set<BcNumberRequest>();
    public DbSet<BcMockNumberSeries> BcMockNumberSeries => Set<BcMockNumberSeries>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // The columns are `timestamp without time zone` holding UTC. Npgsql only writes DateTimes of
        // kind Unspecified to them, so values are written as Unspecified and read back as Utc.
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcTimestampConverter>()
            .HaveColumnType("timestamp without time zone");
        configurationBuilder.Properties<DateTime?>()
            .HaveConversion<UtcTimestampConverter>()
            .HaveColumnType("timestamp without time zone");
        configurationBuilder.Properties<string>().HaveColumnType("text");
        configurationBuilder.Properties<decimal>().HaveColumnType("numeric");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(e =>
        {
            e.ToTable("users");
            e.HasIndex(x => x.Username).IsUnique().HasDatabaseName("users_username_unique");
            e.HasOne<Sbu>().WithMany().HasForeignKey(x => x.SbuId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Sbu>(e =>
        {
            e.ToTable("sbus");
            e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("sbus_code_unique");
        });

        modelBuilder.Entity<Location>(e =>
        {
            e.ToTable("locations");
            e.HasOne(x => x.Sbu).WithMany().HasForeignKey(x => x.SbuId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<RfidReader>(e =>
        {
            e.ToTable("rfid_readers");
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Asset>(e =>
        {
            e.ToTable("assets");
            e.HasIndex(x => x.SerialNumber).IsUnique().HasDatabaseName("assets_serial_number_unique");
            e.HasIndex(x => x.RfidTag).IsUnique().HasDatabaseName("assets_rfid_tag_unique");
            e.HasIndex(x => x.MacAddress).IsUnique().HasDatabaseName("assets_mac_address_unique");
            e.HasIndex(x => x.AssetNumber).IsUnique().HasDatabaseName("assets_asset_number_unique");
            e.Property(x => x.OriginalCost).HasColumnType("numeric(18,2)");
            e.Property(x => x.ExchangeRate).HasColumnType("numeric(18,6)");
            e.Property(x => x.CostUsd).HasColumnType("numeric(18,2)");
            e.Property(x => x.ResidualValue).HasColumnType("numeric(18,2)");
            e.HasOne(x => x.AssetCategory).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Attributes).WithOne().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.CurrentLocation).WithMany().HasForeignKey(x => x.CurrentLocationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Sbu).WithMany().HasForeignKey(x => x.SbuId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Movement>(e =>
        {
            e.ToTable("movements");
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.FromLocation).WithMany().HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.ToLocation).WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.RfidReader).WithMany().HasForeignKey(x => x.RfidReaderId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.GatePass).WithMany().HasForeignKey(x => x.GatePassId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<GatePass>(e =>
        {
            e.ToTable("gate_passes");
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.GateLocation).WithMany().HasForeignKey(x => x.GateLocationId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AiInsight>(e =>
        {
            e.ToTable("ai_insights");
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.GatePass).WithMany().HasForeignKey(x => x.GatePassId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AssetLifecycleEvent>(e =>
        {
            e.ToTable("asset_lifecycle_events");
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.FromSbu).WithMany().HasForeignKey(x => x.FromSbuId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.ToSbu).WithMany().HasForeignKey(x => x.ToSbuId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AssetCategory>(e =>
        {
            e.ToTable("asset_categories");
            e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("asset_categories_code_unique");
            e.HasIndex(x => x.Name).IsUnique().HasDatabaseName("asset_categories_name_unique");
            e.Property(x => x.SubCategories).HasColumnType("jsonb");
            e.Property(x => x.AnnualRate).HasColumnType("numeric(9,6)");
            e.HasMany(x => x.Fields).WithOne().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<CategoryTemplateField>(e =>
        {
            e.ToTable("category_template_fields");
            e.HasIndex(x => new { x.CategoryId, x.Key }).IsUnique().HasDatabaseName("category_template_fields_category_key_unique");
            e.Property(x => x.AllowedValues).HasColumnType("jsonb");
        });

        modelBuilder.Entity<AssetAttributeValue>(e =>
        {
            e.ToTable("asset_attribute_values");
            e.HasIndex(x => new { x.AssetId, x.FieldId }).IsUnique().HasDatabaseName("asset_attribute_values_asset_field_unique");
            e.HasOne(x => x.Field).WithMany().HasForeignKey(x => x.FieldId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<GroupSetting>(e =>
        {
            e.ToTable("group_settings");
            e.HasKey(x => x.Key);
            e.Property(x => x.Value).HasColumnType("jsonb");
        });

        modelBuilder.Entity<AssetUpgrade>(e =>
        {
            e.ToTable("asset_upgrades");
            e.Property(x => x.FieldChanges).HasColumnType("jsonb");
            e.Property(x => x.Cost).HasColumnType("numeric(18,2)");
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<DepreciationEntry>(e =>
        {
            e.ToTable("depreciation_entries");
            e.HasIndex(x => new { x.AssetId, x.Period }).IsUnique().HasDatabaseName("depreciation_entries_asset_period_unique");
            foreach (var money in new[] { "OpeningCost", "OpeningAccumulated", "Charge", "Adjustment", "ClosingAccumulated", "ClosingNbv" })
            {
                e.Property(money).HasColumnType("numeric(18,2)");
            }
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<DepreciationLock>(e =>
        {
            e.ToTable("depreciation_locks");
            e.HasIndex(x => new { x.SbuId, x.Period }).IsUnique().HasDatabaseName("depreciation_locks_sbu_period_unique");
            e.HasOne<Sbu>().WithMany().HasForeignKey(x => x.SbuId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AuditEntry>(e => e.ToTable("audit_log"));
        modelBuilder.Entity<AccessLogEntry>(e => e.ToTable("access_log"));
        modelBuilder.Entity<BcNumberRequest>(e =>
        {
            e.ToTable("bc_number_requests");
            e.HasIndex(x => x.AssetId).IsUnique().HasDatabaseName("bc_number_requests_asset_id_unique");
        });
        modelBuilder.Entity<BcMockNumberSeries>(e =>
        {
            e.ToTable("bc_mock_number_series");
            e.HasIndex(x => x.Prefix).IsUnique().HasDatabaseName("bc_mock_number_series_prefix_unique");
        });

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(SnakeCase(property.Name));
            }
        }
    }

    /// <summary>"CurrentLocationId" → "current_location_id".</summary>
    internal static string SnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}

/// <summary>UTC in memory, kind-less in `timestamp without time zone` columns.</summary>
internal sealed class UtcTimestampConverter() : ValueConverter<DateTime, DateTime>(
    v => DateTime.SpecifyKind(v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v, DateTimeKind.Unspecified),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
