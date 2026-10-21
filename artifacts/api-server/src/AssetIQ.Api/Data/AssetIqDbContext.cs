using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AssetIQ.Api.Data;

/// <summary>
/// EF Core mapping of the existing AssetIQ database. The schema is owned by lib/db (Drizzle,
/// `pnpm db:push`); this context never creates or migrates tables, it only maps onto them.
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
