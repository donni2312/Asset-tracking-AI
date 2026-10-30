using System.Globalization;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

public sealed record FleetUpdate(Optional<string?> FleetStatus, Optional<int?> DriverEmployeeId, Optional<string?> DriverName, int? Reading);

/// <summary>
/// Phase 4d, the SBU Admin Manager's fleet: the register of vehicles (and generators) with driver, depot, status and
/// mileage (AM-02), legal documents with reminders at 30 and 7 days and red once expired (AM-03), and service
/// schedules by mileage / hours or date with the service history (AM-04). Only the SBU's Admin Manager changes
/// anything; the Group roles see a read-only roll-up; the cost of a service is entered and seen by Finance only.
/// </summary>
public sealed class FleetService(AssetIqDbContext db, AppClock clock, AppConfig config, AuditTrail audit, NotificationService notifications)
{
    public const string Vehicles = "MV";
    public const string Generators = "PG";
    private static readonly string[] FleetCategories = [Vehicles, Generators];
    /// <summary>A service is "due soon" within this many days, or this share of the interval.</summary>
    private const int DueSoonDays = 14;
    private const decimal DueSoonShare = 0.1m;

    private string Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, config.TimeZone)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Who may see the fleet of an SBU: its Admin Manager and Finance; Group roles across SBUs (read-only).</summary>
    public static bool CanView(AuthUser user, int? sbuId) => AccessPolicy.Sys(user) || user.Role switch
    {
        Roles.GroupFinance or Roles.GroupIt or Roles.Auditor => true,
        Roles.SbuAdmin or Roles.SbuFinance => AccessPolicy.OwnSbu(user, sbuId),
        _ => false,
    };

    /// <summary>AM-01 / AM-02: only the SBU's Admin Manager changes its fleet records.</summary>
    public static bool CanEdit(AuthUser user, int? sbuId) => AccessPolicy.Sys(user) || user.Role == Roles.SbuAdmin && AccessPolicy.OwnSbu(user, sbuId);

    public async Task<List<FleetItemDto>> ListAsync(AuthUser user, int? sbuId, string? kind, CancellationToken ct)
    {
        var codes = kind switch { "generators" => new[] { Generators }, "all" => FleetCategories, _ => [Vehicles] };
        var query = db.Assets.IgnoreQueryFilters().AsNoTracking().Where(a => codes.Contains(a.AssetCategory.Code) && a.Status != AssetStatuses.Disposed);
        if (Roles.IsSbuScoped(user.Role)) query = query.Where(a => a.SbuId == user.SbuId);
        else if (sbuId is not null) query = query.Where(a => a.SbuId == sbuId);
        var ids = await query.OrderBy(a => a.SbuId).ThenBy(a => a.Name).Select(a => a.Id).ToListAsync(ct);
        return await BuildAsync(user, ids, withServices: false, ct);
    }

    public async Task<FleetItemDto> GetAsync(AuthUser user, int assetId, CancellationToken ct)
    {
        await LoadAsync(user, assetId, edit: false, ct);
        return (await BuildAsync(user, [assetId], withServices: true, ct)).Single();
    }

    public async Task UpdateAsync(AuthUser user, int assetId, FleetUpdate update, CancellationToken ct)
    {
        var asset = await LoadAsync(user, assetId, edit: true, ct);
        var details = await DetailsAsync(user, assetId, ct);
        if (update.FleetStatus.IsSet && audit.RecordChange(user, "fleet", details.Id, assetId, "fleetStatus", details.FleetStatus, update.FleetStatus.Value))
            details.FleetStatus = update.FleetStatus.Value ?? "active";
        if (update.DriverEmployeeId.IsSet)
        {
            if (update.DriverEmployeeId.Value is { } employeeId
                && !await db.Employees.AnyAsync(e => e.Id == employeeId && e.SbuId == asset.SbuId && e.Active, ct))
                throw new ApiException(StatusCodes.Status400BadRequest, "driverEmployeeId: no active employee of this SBU");
            audit.RecordChange(user, "fleet", details.Id, assetId, "driverEmployeeId", details.DriverEmployeeId, update.DriverEmployeeId.Value);
            details.DriverEmployeeId = update.DriverEmployeeId.Value;
        }
        if (update.DriverName.IsSet && audit.RecordChange(user, "fleet", details.Id, assetId, "driverName", details.DriverName, update.DriverName.Value))
            details.DriverName = update.DriverName.Value;
        if (update.Reading is { } reading) await SetReadingAsync(user, asset, reading, "Reading updated", ct);
        details.UpdatedBy = Name(user);
        details.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>AM-03: a document (or its renewal) with its expiry date; the latest one of a kind is in force.</summary>
    public async Task AddDocumentAsync(AuthUser user, int assetId, string kind, string? reference, string expiresOn, CancellationToken ct)
    {
        var asset = await LoadAsync(user, assetId, edit: true, ct);
        if (asset.AssetCategory.Code != Vehicles) throw new ApiException(StatusCodes.Status400BadRequest, "Licence, insurance, fitness and ZINARA documents are kept for vehicles");
        var row = new VehicleDocument { AssetId = assetId, Kind = kind, Reference = reference, ExpiresOn = expiresOn, CreatedBy = Name(user), CreatedAt = clock.UtcNow };
        db.VehicleDocuments.Add(row);
        await db.SaveChangesAsync(ct);
        audit.Record(user, "vehicle_document", row.Id, assetId, "document_recorded", kind, null, expiresOn, reference);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>AM-04: the service interval, by mileage / hours, by date, or both (whichever comes first).</summary>
    public async Task SetScheduleAsync(AuthUser user, int assetId, int? intervalReading, int? intervalDays, string? startDate, int? startReading, CancellationToken ct)
    {
        await LoadAsync(user, assetId, edit: true, ct);
        if (intervalReading is null && intervalDays is null) throw new ApiException(StatusCodes.Status400BadRequest, "Give an interval in km / hours, in days, or both");
        if (intervalReading is <= 0 || intervalDays is <= 0) throw new ApiException(StatusCodes.Status400BadRequest, "Intervals must be positive");
        var schedule = await db.ServiceSchedules.FirstOrDefaultAsync(s => s.AssetId == assetId, ct);
        if (schedule is null)
        {
            schedule = new ServiceSchedule { AssetId = assetId, StartDate = startDate ?? Today, StartReading = startReading ?? await ReadingAsync(assetId, ct) };
            db.ServiceSchedules.Add(schedule);
        }
        else
        {
            if (startDate is not null) schedule.StartDate = startDate;
            if (startReading is not null) schedule.StartReading = startReading;
        }
        audit.Record(user, "service_schedule", schedule.Id == 0 ? null : schedule.Id, assetId, "service_schedule_set", "interval", null,
            $"{intervalReading?.ToString(CultureInfo.InvariantCulture) ?? "—"} / {intervalDays?.ToString(CultureInfo.InvariantCulture) ?? "—"} days");
        schedule.IntervalReading = intervalReading;
        schedule.IntervalDays = intervalDays;
        schedule.OverdueNotifiedFor = null;
        schedule.UpdatedBy = Name(user);
        schedule.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>AM-04: a service done (date, mileage / hours, garage, work). The cost is added by SBU Finance.</summary>
    public async Task<ServiceLog> LogServiceAsync(AuthUser user, int assetId, string serviceDate, int? reading, string garage, string workDone, CancellationToken ct)
    {
        var asset = await LoadAsync(user, assetId, edit: true, ct);
        if (string.CompareOrdinal(serviceDate, Today) > 0) throw new ApiException(StatusCodes.Status400BadRequest, "serviceDate: cannot be in the future");
        var row = new ServiceLog
        {
            AssetId = assetId, ServiceDate = serviceDate, Reading = reading, Garage = garage, WorkDone = workDone, LoggedBy = Name(user), CreatedAt = clock.UtcNow,
        };
        db.ServiceLogs.Add(row);
        db.AssetLifecycleEvents.Add(new AssetLifecycleEvent
        {
            AssetId = assetId, EventType = LifecycleEventTypes.Note, PerformedBy = Name(user), Vendor = garage,
            Notes = $"Serviced {serviceDate}{(reading is null ? "" : $" at {reading} {Unit(asset.AssetCategory.Code)}")}: {workDone}", Timestamp = clock.UtcNow,
        });
        if (reading is { } r && r > (await ReadingAsync(assetId, ct) ?? 0)) await SetReadingAsync(user, asset, r, $"Service on {serviceDate}", ct);
        var schedule = await db.ServiceSchedules.FirstOrDefaultAsync(s => s.AssetId == assetId, ct);
        if (schedule is not null) schedule.OverdueNotifiedFor = null;
        await db.SaveChangesAsync(ct);
        audit.Record(user, "service_log", row.Id, assetId, "service_logged", null, null, serviceDate, workDone);
        await db.SaveChangesAsync(ct);
        var finance = await db.Users.Where(u => u.Active && u.Role == Roles.SbuFinance && u.SbuId == asset.SbuId).ToListAsync(ct);
        notifications.Notify(finance, "service", $"Service to cost: {asset.Name}", $"{garage}, {serviceDate}: {workDone}", $"/fleet/{assetId}");
        await db.SaveChangesAsync(ct);
        return row;
    }

    /// <summary>The cost of a service (SBU Finance of the vehicle's SBU).</summary>
    public async Task<int> SetServiceCostAsync(AuthUser user, int logId, decimal cost, string currency, CancellationToken ct)
    {
        var log = await db.ServiceLogs.FirstOrDefaultAsync(l => l.Id == logId, ct) ?? throw new ApiException(StatusCodes.Status404NotFound, "Service not found");
        var sbuId = await db.Assets.IgnoreQueryFilters().Where(a => a.Id == log.AssetId).Select(a => a.SbuId).FirstAsync(ct);
        if (!AccessPolicy.IsSbuFinanceOf(user, sbuId)) throw AccessPolicy.Deny(user, "record the cost of a service (SBU Finance does)");
        if (cost < 0) throw new ApiException(StatusCodes.Status400BadRequest, "cost: cannot be negative");
        audit.RecordChange(user, "service_log", log.Id, log.AssetId, "cost", log.Cost, cost, currency);
        log.Cost = cost;
        log.Currency = currency;
        log.CostBy = Name(user);
        await db.SaveChangesAsync(ct);
        return log.AssetId;
    }

    /// <summary>AM-03 / AM-04: reminders at 30 and 7 days, the expiry itself, and overdue services — each once — to the SBU's Admin Manager.</summary>
    public async Task<int> SendRemindersAsync(CancellationToken ct)
    {
        var sent = 0;
        var now = clock.UtcNow;
        var today = DateOnly.ParseExact(Today, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var current = await CurrentDocumentsQuery().ToListAsync(ct);
        foreach (var doc in current)
        {
            var days = DaysLeft(doc.ExpiresOn, today);
            string? title = null;
            if (days < 0 && doc.ExpiredNotifiedAt is null)
            {
                doc.ExpiredNotifiedAt = now;
                doc.Reminded7At ??= now;
                doc.Reminded30At ??= now;
                title = "expired";
            }
            else if (days is >= 0 and <= 7 && doc.Reminded7At is null)
            {
                doc.Reminded7At = now;
                doc.Reminded30At ??= now;
                title = $"expires in {days} day(s)";
            }
            else if (days is > 7 and <= 30 && doc.Reminded30At is null)
            {
                doc.Reminded30At = now;
                title = $"expires in {days} days";
            }
            if (title is null) continue;
            var asset = await db.Assets.IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.Id == doc.AssetId, ct);
            await NotifyAdminsAsync(asset, "fleet", $"{KindLabel(doc.Kind)} {title}: {asset.Name}", $"{KindLabel(doc.Kind)} {doc.Reference} — expiry {doc.ExpiresOn}", ct);
            sent++;
        }
        await db.SaveChangesAsync(ct);

        foreach (var schedule in await db.ServiceSchedules.ToListAsync(ct))
        {
            var asset = await db.Assets.IgnoreQueryFilters().AsNoTracking().Include(a => a.AssetCategory).FirstOrDefaultAsync(a => a.Id == schedule.AssetId, ct);
            if (asset is null || asset.Status == AssetStatuses.Disposed) continue;
            var due = await DueAsync(schedule, today, ct);
            var key = $"{due.DueDate}|{due.DueReading}";
            if (due.State != "overdue" || schedule.OverdueNotifiedFor == key) continue;
            schedule.OverdueNotifiedFor = key;
            await NotifyAdminsAsync(asset, "fleet", $"Service overdue: {asset.Name}",
                $"Due {(due.DueDate is null ? "" : $"on {due.DueDate}")}{(due.DueDate is not null && due.DueReading is not null ? " or " : "")}{(due.DueReading is null ? "" : $"at {due.DueReading} {Unit(asset.AssetCategory.Code)}")}", ct);
            sent++;
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task<List<FleetItemDto>> BuildAsync(AuthUser user, List<int> ids, bool withServices, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var today = DateOnly.ParseExact(Today, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var assets = await db.Assets.IgnoreQueryFilters().AsNoTracking().Include(a => a.AssetCategory).Include(a => a.Sbu).Include(a => a.CurrentLocation)
            .Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var attributes = (await db.AssetAttributeValues.AsNoTracking().Include(v => v.Field)
            .Where(v => ids.Contains(v.AssetId) && new[] { "registration_number", "make", "model", "mileage", "hours_run" }.Contains(v.Field.Key))
            .ToListAsync(ct)).ToLookup(v => v.AssetId);
        var details = await db.FleetDetails.AsNoTracking().Where(d => ids.Contains(d.AssetId)).ToDictionaryAsync(d => d.AssetId, ct);
        var driverIds = details.Values.Where(d => d.DriverEmployeeId is not null).Select(d => d.DriverEmployeeId!.Value).ToList();
        var drivers = await db.Employees.AsNoTracking().Where(e => driverIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Name, ct);
        var documents = (await CurrentDocumentsQuery().AsNoTracking().Where(d => ids.Contains(d.AssetId)).ToListAsync(ct)).ToLookup(d => d.AssetId);
        var schedules = await db.ServiceSchedules.AsNoTracking().Where(s => ids.Contains(s.AssetId)).ToDictionaryAsync(s => s.AssetId, ct);
        var logs = withServices
            ? (await db.ServiceLogs.AsNoTracking().Where(l => ids.Contains(l.AssetId)).OrderByDescending(l => l.ServiceDate).ThenByDescending(l => l.Id).ToListAsync(ct)).ToLookup(l => l.AssetId)
            : null;

        var list = new List<FleetItemDto>();
        foreach (var id in ids)
        {
            var a = assets[id];
            string? Attr(string key) => attributes[id].FirstOrDefault(v => v.Field.Key == key)?.ValueText;
            var reading = int.TryParse(Attr(a.AssetCategory.Code == Vehicles ? "mileage" : "hours_run"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : (int?)null;
            details.TryGetValue(id, out var d);
            var docs = documents[id].OrderBy(x => x.Kind).Select(x => ToDto(x, today)).ToList();
            ServiceDueDto? due = schedules.TryGetValue(id, out var schedule) ? await DueAsync(schedule, today, ct, reading) : null;
            list.Add(new FleetItemDto
            {
                AssetId = id, AssetNumber = a.AssetNumber, Name = a.Name, SbuId = a.SbuId, SbuCode = a.Sbu?.Code, CategoryCode = a.AssetCategory.Code,
                SubCategory = a.SubCategory, RegistrationNumber = Attr("registration_number"),
                MakeModel = string.Join(" ", new[] { Attr("make"), Attr("model") }.Where(x => !string.IsNullOrWhiteSpace(x))),
                DriverEmployeeId = d?.DriverEmployeeId,
                Driver = d?.DriverEmployeeId is { } de && drivers.TryGetValue(de, out var dn) ? dn : d?.DriverName ?? a.AssignedTo,
                LocationId = a.CurrentLocationId, Depot = a.CurrentLocation?.Name,
                FleetStatus = d?.FleetStatus ?? (a.Status == AssetStatuses.InRepair ? "in_service" : "active"),
                AssetStatus = a.Status, Reading = reading, ReadingUnit = Unit(a.AssetCategory.Code),
                Documents = docs, DocumentsExpired = docs.Any(x => x.State == "expired"), NextService = due,
                CanEdit = CanEdit(user, a.SbuId),
                Services = logs?[id].Select(l => new ServiceLogDto
                {
                    Id = l.Id, AssetId = l.AssetId, ServiceDate = l.ServiceDate, Reading = l.Reading, Garage = l.Garage, WorkDone = l.WorkDone,
                    Cost = l.Cost, Currency = l.Currency, CostBy = l.CostBy, LoggedBy = l.LoggedBy,
                }).ToList() ?? [],
            });
        }
        return list;
    }

    /// <summary>The latest document of each kind per vehicle (the one in force).</summary>
    private IQueryable<VehicleDocument> CurrentDocumentsQuery() =>
        db.VehicleDocuments.Where(d => !db.VehicleDocuments.Any(o => o.AssetId == d.AssetId && o.Kind == d.Kind && (o.CreatedAt > d.CreatedAt || o.CreatedAt == d.CreatedAt && o.Id > d.Id)));

    private async Task<ServiceDueDto> DueAsync(ServiceSchedule schedule, DateOnly today, CancellationToken ct, int? currentReading = null)
    {
        var last = await db.ServiceLogs.AsNoTracking().Where(l => l.AssetId == schedule.AssetId).OrderByDescending(l => l.ServiceDate).ThenByDescending(l => l.Id)
            .Select(l => new { l.ServiceDate, l.Reading }).FirstOrDefaultAsync(ct);
        var fromDate = DateOnly.ParseExact(last?.ServiceDate ?? schedule.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var fromReading = last?.Reading ?? schedule.StartReading;
        currentReading ??= await ReadingAsync(schedule.AssetId, ct);
        var dueDate = schedule.IntervalDays is { } days ? fromDate.AddDays(days) : (DateOnly?)null;
        var dueReading = schedule.IntervalReading is { } interval && fromReading is { } start ? start + interval : (int?)null;
        var overdue = dueDate < today || dueReading is not null && currentReading >= dueReading;
        var soon = dueDate is { } dd && dd.DayNumber - today.DayNumber <= DueSoonDays
            || dueReading is not null && currentReading is not null && schedule.IntervalReading is { } iv && dueReading - currentReading <= iv * DueSoonShare;
        return new ServiceDueDto
        {
            IntervalReading = schedule.IntervalReading, IntervalDays = schedule.IntervalDays,
            DueDate = dueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), DueReading = dueReading,
            State = overdue ? "overdue" : soon ? "due_soon" : "ok",
        };
    }

    private async Task<int?> ReadingAsync(int assetId, CancellationToken ct)
    {
        var text = await db.AssetAttributeValues.AsNoTracking().Where(v => v.AssetId == assetId && (v.Field.Key == "mileage" || v.Field.Key == "hours_run"))
            .Select(v => v.ValueText).FirstOrDefaultAsync(ct);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : null;
    }

    /// <summary>The mileage / hours template field; a lower reading than the recorded one is refused.</summary>
    private async Task SetReadingAsync(AuthUser user, Asset asset, int reading, string reason, CancellationToken ct)
    {
        var key = asset.AssetCategory.Code == Vehicles ? "mileage" : "hours_run";
        var field = await db.CategoryTemplateFields.FirstOrDefaultAsync(f => f.CategoryId == asset.CategoryId && f.Key == key, ct)
            ?? throw new ApiException(StatusCodes.Status409Conflict, $"The category has no {key} field");
        var value = await db.AssetAttributeValues.FirstOrDefaultAsync(v => v.AssetId == asset.Id && v.FieldId == field.Id, ct);
        if (value is not null && value.ValueNumber is { } before && reading < before)
            throw new ApiException(StatusCodes.Status400BadRequest, $"reading: {reading} is below the recorded {before:0} {Unit(asset.AssetCategory.Code)}");
        if (value is null)
        {
            value = new AssetAttributeValue { AssetId = asset.Id, FieldId = field.Id };
            db.AssetAttributeValues.Add(value);
        }
        audit.RecordChange(user, "asset", asset.Id, asset.Id, key, value.ValueText.Length == 0 ? null : value.ValueText, reading.ToString(CultureInfo.InvariantCulture), reason);
        value.ValueText = reading.ToString(CultureInfo.InvariantCulture);
        value.ValueNumber = reading;
    }

    private async Task<FleetDetail> DetailsAsync(AuthUser user, int assetId, CancellationToken ct)
    {
        var details = await db.FleetDetails.FirstOrDefaultAsync(d => d.AssetId == assetId, ct);
        if (details is not null) return details;
        details = new FleetDetail { AssetId = assetId, FleetStatus = "active", UpdatedBy = Name(user), UpdatedAt = clock.UtcNow };
        db.FleetDetails.Add(details);
        await db.SaveChangesAsync(ct);
        return details;
    }

    private async Task<Asset> LoadAsync(AuthUser user, int assetId, bool edit, CancellationToken ct)
    {
        var asset = await db.Assets.IgnoreQueryFilters().Include(a => a.AssetCategory).FirstOrDefaultAsync(a => a.Id == assetId, ct);
        if (asset is null || !FleetCategories.Contains(asset.AssetCategory.Code)) throw new ApiException(StatusCodes.Status404NotFound, "Vehicle or generator not found");
        if (!CanView(user, asset.SbuId)) throw AccessPolicy.Deny(user, "see this SBU's fleet");
        if (edit && !CanEdit(user, asset.SbuId)) throw AccessPolicy.Deny(user, "change fleet records (the SBU Admin Manager does, AM-01)");
        if (edit && asset.Status == AssetStatuses.Disposed) throw new ApiException(StatusCodes.Status409Conflict, "The asset has been disposed");
        return asset;
    }

    private async Task NotifyAdminsAsync(Asset asset, string kind, string title, string body, CancellationToken ct)
    {
        var admins = await db.Users.Where(u => u.Active && u.Role == Roles.SbuAdmin && u.SbuId == asset.SbuId).ToListAsync(ct);
        notifications.Notify(admins, kind, title, body, $"/fleet/{asset.Id}");
    }

    private static VehicleDocumentDto ToDto(VehicleDocument d, DateOnly today)
    {
        var days = DaysLeft(d.ExpiresOn, today);
        return new VehicleDocumentDto
        {
            Id = d.Id, Kind = d.Kind, Reference = d.Reference, ExpiresOn = d.ExpiresOn, DaysLeft = days,
            State = days < 0 ? "expired" : days <= 7 ? "due_7" : days <= 30 ? "due_30" : "ok",
        };
    }

    private static int DaysLeft(string expiresOn, DateOnly today) =>
        DateOnly.ParseExact(expiresOn, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber - today.DayNumber;

    private static string KindLabel(string kind) => kind switch
    {
        "licence" => "Vehicle licence", "insurance" => "Insurance", "fitness" => "Certificate of fitness", "zinara" => "ZINARA licence", _ => kind,
    };

    private static string Unit(string categoryCode) => categoryCode == Vehicles ? "km" : "h";

    private static string Name(AuthUser user) => user.Name.Length > 0 ? user.Name : user.Username;
}
