using System.Linq.Expressions;
using System.Text;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AssetIQ.Api.Data;

/// <summary>
/// EF Core mapping of the AssetIQ database. The schema is owned by lib/db (the Drizzle schema and the SQL
/// migrations in lib/db/migrations, applied by `pnpm db:migrate`); this context never creates or migrates
/// tables, it only maps onto them.
/// Assets, and every table that hangs off an asset, carry a global query filter: the request's
/// <see cref="AccessScope"/> (Phase 2). A context made outside a request (tests, the BC worker) is unrestricted.
/// </summary>
public sealed class AssetIqDbContext(DbContextOptions<AssetIqDbContext> options, AccessScope? scope = null) : DbContext(options)
{
    /// <summary>What the current caller may see; read by the query filters every time a query runs.</summary>
    public AccessScope Scope { get; } = scope ?? new AccessScope();

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
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<AssetAttachment> AssetAttachments => Set<AssetAttachment>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<ApprovalStep> ApprovalSteps => Set<ApprovalStep>();
    public DbSet<Delegation> Delegations => Set<Delegation>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<OutboundEmail> OutboundEmails => Set<OutboundEmail>();
    public DbSet<AssetRepair> AssetRepairs => Set<AssetRepair>();
    public DbSet<AssetDisposal> AssetDisposals => Set<AssetDisposal>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();
    public DbSet<EquipmentRequest> EquipmentRequests => Set<EquipmentRequest>();
    public DbSet<AssetAcknowledgement> AssetAcknowledgements => Set<AssetAcknowledgement>();
    public DbSet<ExitClearance> ExitClearances => Set<ExitClearance>();
    public DbSet<ExitClearanceItem> ExitClearanceItems => Set<ExitClearanceItem>();
    public DbSet<GatePassItem> GatePassItems => Set<GatePassItem>();
    public DbSet<GateAction> GateActions => Set<GateAction>();
    public DbSet<VerificationSession> VerificationSessions => Set<VerificationSession>();
    public DbSet<VerificationScan> VerificationScans => Set<VerificationScan>();
    public DbSet<VerificationResult> VerificationResults => Set<VerificationResult>();

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
            e.HasIndex(x => x.EmployeeId).IsUnique().HasDatabaseName("users_employee_id_unique");
            e.HasOne<Sbu>().WithMany().HasForeignKey(x => x.SbuId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.NoAction);
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
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.CustodianEmployeeId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasQueryFilter(AssetVisible);
        });

        modelBuilder.Entity<Movement>(e =>
        {
            e.ToTable("movements");
            e.HasQueryFilter(VisibleThrough<Movement>(nameof(Movement.Asset)));
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.FromLocation).WithMany().HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.ToLocation).WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.RfidReader).WithMany().HasForeignKey(x => x.RfidReaderId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.GatePass).WithMany().HasForeignKey(x => x.GatePassId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<GatePass>(e =>
        {
            e.ToTable("gate_passes");
            e.HasQueryFilter(VisibleThrough<GatePass>(nameof(GatePass.Asset)));
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.GateLocation).WithMany().HasForeignKey(x => x.GateLocationId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AiInsight>(e =>
        {
            e.ToTable("ai_insights");
            // Insights about no particular asset are for Group-wide roles only.
            e.HasQueryFilter(Or<AiInsight>(i => i.AssetId == null && (Scope.Unrestricted || (!Scope.Nothing && Scope.SbuId == null)),
                VisibleThrough<AiInsight>(nameof(AiInsight.Asset))));
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.GatePass).WithMany().HasForeignKey(x => x.GatePassId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AssetLifecycleEvent>(e =>
        {
            e.ToTable("asset_lifecycle_events");
            e.HasQueryFilter(VisibleThrough<AssetLifecycleEvent>(nameof(AssetLifecycleEvent.Asset)));
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
            e.Property(x => x.DefaultResidualPercent).HasColumnType("numeric(5,2)");
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
            e.Property(x => x.InvoiceAmount).HasColumnType("numeric(18,2)");
            e.Property(x => x.ExchangeRate).HasColumnType("numeric(18,6)");
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
            e.HasQueryFilter(VisibleThrough<AssetUpgrade>(nameof(AssetUpgrade.Asset)));
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

        modelBuilder.Entity<Department>(e =>
        {
            e.ToTable("departments");
            e.HasIndex(x => new { x.SbuId, x.Name }).IsUnique().HasDatabaseName("departments_sbu_name_unique");
            e.HasOne<Sbu>().WithMany().HasForeignKey(x => x.SbuId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Employee>(e =>
        {
            e.ToTable("employees");
            e.HasIndex(x => x.EmployeeNo).IsUnique().HasDatabaseName("employees_employee_no_unique");
            e.HasOne<Sbu>().WithMany().HasForeignKey(x => x.SbuId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.LineManagerId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<AssetAttachment>(e =>
        {
            e.ToTable("asset_attachments");
            e.Property(x => x.Content).HasColumnType("bytea");
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<AssetUpgrade>().WithMany().HasForeignKey(x => x.UpgradeId).OnDelete(DeleteBehavior.NoAction);
            e.HasQueryFilter(VisibleThrough<AssetAttachment>(nameof(AssetAttachment.Asset)));
        });

        // Phase 4. Approval requests are not filtered by the asset scope: an approver's inbox is decided by the
        // routing (role, SBU, department, delegation), which may reach beyond what the approver sees day to day.
        modelBuilder.Entity<ApprovalRequest>(e =>
        {
            e.ToTable("approval_requests");
            e.Property(x => x.Details).HasColumnType("jsonb");
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Sbu>().WithMany().HasForeignKey(x => x.SbuId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ApprovalStep>(e => e.ToTable("approval_steps"));
        modelBuilder.Entity<Delegation>(e => e.ToTable("delegations"));
        modelBuilder.Entity<Notification>(e => e.ToTable("notifications"));
        modelBuilder.Entity<OutboundEmail>(e => e.ToTable("outbound_emails"));
        modelBuilder.Entity<AssetRepair>(e =>
        {
            e.ToTable("asset_repairs");
            e.Property(x => x.Cost).HasColumnType("numeric(18,2)");
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<AssetDisposal>(e =>
        {
            e.ToTable("asset_disposals");
            foreach (var money in new[] { "Proceeds", "NbvAtDisposal", "GainLoss" }) e.Property(money).HasColumnType("numeric(18,2)");
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
        });

        // Phase 4b
        modelBuilder.Entity<PurchaseOrder>(e =>
        {
            e.ToTable("purchase_orders");
            e.HasIndex(x => x.PoNo).IsUnique().HasDatabaseName("purchase_orders_po_no_unique");
            e.HasOne<Sbu>().WithMany().HasForeignKey(x => x.SbuId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<PurchaseOrderLine>(e =>
        {
            e.ToTable("purchase_order_lines");
            e.Property(x => x.UnitCost).HasColumnType("numeric(18,2)");
            e.HasOne<AssetCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<GoodsReceipt>(e =>
        {
            e.ToTable("goods_receipts");
            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<GoodsReceiptLine>(e =>
        {
            e.ToTable("goods_receipt_lines");
            e.Property(x => x.Serials).HasColumnType("jsonb");
            e.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<EquipmentRequest>(e =>
        {
            e.ToTable("equipment_requests");
            e.HasOne<Sbu>().WithMany().HasForeignKey(x => x.SbuId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<AssetAcknowledgement>(e =>
        {
            e.ToTable("asset_acknowledgements");
            e.HasIndex(x => x.Token).IsUnique().HasDatabaseName("asset_acknowledgements_token_unique");
        });
        modelBuilder.Entity<ExitClearance>(e =>
        {
            e.ToTable("exit_clearances");
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ClearanceId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ExitClearanceItem>(e => e.ToTable("exit_clearance_items"));

        // Phase 4c
        modelBuilder.Entity<GatePassItem>(e =>
        {
            e.ToTable("gate_pass_items");
            e.HasOne<GatePass>().WithMany().HasForeignKey(x => x.GatePassId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<GateAction>(e => e.ToTable("gate_actions"));
        modelBuilder.Entity<VerificationSession>(e => e.ToTable("verification_sessions"));
        modelBuilder.Entity<VerificationScan>(e => e.ToTable("verification_scans"));
        modelBuilder.Entity<VerificationResult>(e => e.ToTable("verification_results"));

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(SnakeCase(property.Name));
            }
        }
    }

    /// <summary>The "View assets" rule of <see cref="AccessScope"/>, evaluated with this request's scope (AccessPolicy.Visible mirrors it).</summary>
    private Expression<Func<Asset, bool>> AssetVisible => a => Scope.Unrestricted || (!Scope.Nothing
        && (Scope.SbuId == null || a.SbuId == Scope.SbuId)
        && (Scope.It == null || a.AssetCategory.IsIt == Scope.It)
        && (!Scope.InStoreOnly || a.Status == AssetStatuses.PendingAssetNumber
            || (a.Status == AssetStatuses.InStore && (!a.AssetCategory.IsIt || a.RfidTag == null))
            || (a.Status == AssetStatuses.PendingDisposal && a.AssetCategory.IsIt))
        && (Scope.DepartmentId == null || a.DepartmentId == Scope.DepartmentId)
        && (Scope.EmployeeId == null || a.CustodianEmployeeId == Scope.EmployeeId)
        && (!Scope.HeldByEmployee || a.CustodianEmployeeId != null));

    /// <summary>A row is visible when the asset it belongs to (through <paramref name="navigation"/>) is.</summary>
    private Expression<Func<T, bool>> VisibleThrough<T>(string navigation)
    {
        var visible = AssetVisible;
        var row = Expression.Parameter(typeof(T), "row");
        var body = ReplacingExpressionVisitor.Replace(visible.Parameters[0], Expression.Property(row, navigation), visible.Body);
        return Expression.Lambda<Func<T, bool>>(body, row);
    }

    private static Expression<Func<T, bool>> Or<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var body = ReplacingExpressionVisitor.Replace(right.Parameters[0], left.Parameters[0], right.Body);
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(left.Body, body), left.Parameters[0]);
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
