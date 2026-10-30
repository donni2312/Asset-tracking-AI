using System.Globalization;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

public sealed record UploadedPhoto(byte[] Content, string ContentType);

/// <summary>
/// Phase 5b: the security checklist of a lost or stolen device (GI-07), the Gate Officer's shift view (SG-04),
/// visitors' and contractors' own equipment (SG-05), incidents escalated from the gate (SG-06), and the HOD's daily
/// digest of overdue returns, lost items and faulty equipment in their department (LM-04).
/// </summary>
public sealed class GateSecurityService(
    AssetIqDbContext db, AppClock clock, AppConfig config, AuditTrail audit, NotificationService notifications, GateService gate)
{
    public static readonly IReadOnlyList<string> SecurityActionKinds = ["remote_wipe", "account_disable", "police_report"];
    public static readonly IReadOnlyList<string> IncidentKinds = ["forged_pass", "unknown_tag", "refused_search", "other"];

    private string Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, config.TimeZone)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ── GI-07 ───────────────────────────────────────────────────────────────

    /// <summary>The checklist of a lost or stolen asset: remote wipe and account disable for IT devices, a police report for every item.</summary>
    public async Task<SecurityChecklistDto> ChecklistAsync(AuthUser user, int assetId, CancellationToken ct)
    {
        var asset = await db.Assets.IgnoreQueryFilters().AsNoTracking().Include(a => a.AssetCategory).FirstOrDefaultAsync(a => a.Id == assetId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Asset not found");
        if (!AccessPolicy.CanView(user, asset, asset.AssetCategory.IsIt)) throw AccessPolicy.Deny(user, "see this asset");
        var done = await db.SecurityActions.AsNoTracking().Where(s => s.AssetId == assetId).ToListAsync(ct);
        var kinds = asset.AssetCategory.IsIt ? SecurityActionKinds : ["police_report"];
        return new SecurityChecklistDto(assetId, asset.Status, asset.LostDate, asset.LostReportedBy, asset.LostNotes,
            kinds.Select(k => done.FirstOrDefault(d => d.Action == k) is { } d
                ? new SecurityActionDto(k, true, d.Reference, d.Notes, d.DoneBy, d.DoneAt)
                : new SecurityActionDto(k, false, null, null, null, null)).ToList(),
            asset.Status == AssetStatuses.LostStolen && CanRecord(user, asset));
    }

    /// <summary>GI-07: record one security action for a lost or stolen asset (a police report needs its case number).</summary>
    public async Task RecordSecurityActionAsync(AuthUser user, int assetId, string action, string? reference, string? notes, CancellationToken ct)
    {
        var asset = await db.Assets.IgnoreQueryFilters().Include(a => a.AssetCategory).FirstOrDefaultAsync(a => a.Id == assetId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Asset not found");
        if (!CanRecord(user, asset)) throw AccessPolicy.Deny(user, "record security actions for this asset");
        if (asset.Status != AssetStatuses.LostStolen) throw new ApiException(StatusCodes.Status409Conflict, "Security actions are recorded for a lost or stolen asset");
        if (!asset.AssetCategory.IsIt && action != "police_report") throw new ApiException(StatusCodes.Status400BadRequest, "action: a non-IT item has only the police report");
        if (action == "police_report" && string.IsNullOrWhiteSpace(reference)) throw new ApiException(StatusCodes.Status400BadRequest, "reference: the police case number");
        if (action != "police_report" && user.Role is not (Roles.GroupIt or Roles.SbuIt or Roles.SystemAdmin))
            throw AccessPolicy.Deny(user, "record a remote wipe or account disable (IT does)");
        if (await db.SecurityActions.AnyAsync(s => s.AssetId == assetId && s.Action == action, ct)) throw new ApiException(StatusCodes.Status409Conflict, $"{action} is already recorded");
        var name = Name(user);
        db.SecurityActions.Add(new SecurityAction { AssetId = assetId, Action = action, Reference = reference?.Trim(), Notes = notes, DoneBy = name, DoneAt = clock.UtcNow });
        db.AssetLifecycleEvents.Add(new AssetLifecycleEvent
        {
            AssetId = assetId, EventType = LifecycleEventTypes.Note, PerformedBy = name, Timestamp = clock.UtcNow,
            Notes = $"Security action: {action.Replace('_', ' ')}{(reference is null ? "" : $" ({reference})")}{(notes is null ? "" : $" — {notes}")}",
        });
        audit.Record(user, "asset", assetId, assetId, "security_action", action, null, reference ?? "done", notes);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>SBU IT / Group IT for IT devices; the SBU Admin Manager for non-IT items (the police report).</summary>
    private static bool CanRecord(AuthUser user, Asset asset) => AccessPolicy.Sys(user) || (asset.AssetCategory.IsIt
        ? user.Role == Roles.GroupIt || user.Role == Roles.SbuIt && AccessPolicy.OwnSbu(user, asset.SbuId)
        : user.Role == Roles.SbuAdmin && AccessPolicy.OwnSbu(user, asset.SbuId));

    // ── SG-04 ───────────────────────────────────────────────────────────────

    /// <summary>SG-04: approved passes still to go out today, and every overdue item, with the holder and their phone.</summary>
    public async Task<GateShiftDto> ShiftAsync(AuthUser user, CancellationToken ct)
    {
        var today = Today;
        var passes = db.GatePasses.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Status == GatePassStatuses.Approved && (p.ReturnDate == null || string.Compare(p.ReturnDate, today) >= 0)
                // A pass from before Phase 4c has no item rows yet: its one item has not left unless a gate_pass movement says so.
                && (db.GatePassItems.Any(i => i.GatePassId == p.Id && i.ExitedAt == null)
                    || !db.GatePassItems.Any(i => i.GatePassId == p.Id) && !db.Movements.Any(m => m.GatePassId == p.Id && m.TriggeredBy == MovementTriggers.GatePass)));
        if (Roles.IsSbuScoped(user.Role)) passes = passes.Where(p => p.Asset.SbuId == user.SbuId);
        var due = await passes.OrderBy(p => p.ReturnDate).Select(p => new { p.Id, p.AssetId, p.Asset.Name, p.RequestedBy, p.Destination, p.ReturnDate, p.Asset.CustodianEmployeeId, p.Asset.AssignedTo }).ToListAsync(ct);
        var overdue = await gate.OverdueAsync(user, ct);
        var assetIds = overdue.Select(o => o.AssetId).ToList();
        var holders = await db.Assets.IgnoreQueryFilters().AsNoTracking().Where(a => assetIds.Contains(a.Id))
            .Select(a => new { a.Id, a.CustodianEmployeeId, a.AssignedTo }).ToDictionaryAsync(a => a.Id, ct);
        var employeeIds = due.Select(d => d.CustodianEmployeeId).Concat(holders.Values.Select(h => h.CustodianEmployeeId)).OfType<int>().Distinct().ToList();
        var employees = await db.Employees.AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        // The phone of the custodian, else of an employee whose name matches the requester.
        var requesterNames = due.Select(d => d.RequestedBy).Concat(overdue.Select(o => o.RequestedBy)).Distinct().ToList();
        var byName = (await db.Employees.AsNoTracking().Where(e => requesterNames.Contains(e.Name) && e.Active).ToListAsync(ct)).GroupBy(e => e.Name).ToDictionary(g => g.Key, g => g.First());
        (string? Holder, string? Phone) Contact(int? custodianId, string? assignedTo, string requestedBy)
        {
            if (custodianId is { } id && employees.TryGetValue(id, out var e)) return (e.Name, e.Phone);
            return byName.TryGetValue(requestedBy, out var r) ? (r.Name, r.Phone) : (assignedTo ?? requestedBy, null);
        }
        return new GateShiftDto(today,
            due.Select(d => { var c = Contact(d.CustodianEmployeeId, d.AssignedTo, d.RequestedBy); return new ShiftPassDto(d.Id, d.AssetId, d.Name, c.Holder, c.Phone, d.Destination, d.ReturnDate); }).ToList(),
            overdue.Select(o =>
            {
                var h = holders.GetValueOrDefault(o.AssetId);
                var c = Contact(h?.CustodianEmployeeId, h?.AssignedTo, o.RequestedBy);
                return new ShiftPassDto(o.GatePassId, o.AssetId, o.AssetName, c.Holder, c.Phone, o.Destination, o.ReturnDate);
            }).ToList());
    }

    // ── SG-05 ───────────────────────────────────────────────────────────────

    public async Task<VisitorItem> CheckInAsync(AuthUser user, int gateLocationId, string owner, string? company, string item, string? serial, UploadedPhoto? photo, CancellationToken ct)
    {
        var gateLocation = await GateOfAsync(user, gateLocationId, ct);
        var row = new VisitorItem
        {
            SbuId = gateLocation.SbuId!.Value, GateLocationId = gateLocation.Id, OwnerName = owner, OwnerCompany = company, Item = item,
            SerialNumber = serial?.Trim().ToUpperInvariant(), Photo = photo?.Content, PhotoContentType = photo?.ContentType, CheckedInBy = Name(user), CheckedInAt = clock.UtcNow,
        };
        db.VisitorItems.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<List<VisitorItemDto>> VisitorItemsAsync(AuthUser user, bool onSiteOnly, CancellationToken ct)
    {
        var query = db.VisitorItems.AsNoTracking();
        if (Roles.IsSbuScoped(user.Role)) query = query.Where(v => v.SbuId == user.SbuId);
        if (onSiteOnly) query = query.Where(v => v.CheckedOutAt == null);
        var gates = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        return (await query.OrderByDescending(v => v.CheckedInAt).Take(300)
                .Select(v => new { v.Id, v.GateLocationId, v.OwnerName, v.OwnerCompany, v.Item, v.SerialNumber, HasPhoto = v.Photo != null, v.CheckedInBy, v.CheckedInAt, v.CheckedOutBy, v.CheckedOutAt })
                .ToListAsync(ct))
            .Select(v => new VisitorItemDto(v.Id, gates.GetValueOrDefault(v.GateLocationId), v.OwnerName, v.OwnerCompany, v.Item, v.SerialNumber, v.HasPhoto,
                v.CheckedInBy, v.CheckedInAt, v.CheckedOutBy, v.CheckedOutAt)).ToList();
    }

    /// <summary>SG-05: the item leaves without a gate pass.</summary>
    public async Task CheckOutAsync(AuthUser user, int id, int gateLocationId, CancellationToken ct)
    {
        var row = await db.VisitorItems.FirstOrDefaultAsync(v => v.Id == id, ct) ?? throw new ApiException(StatusCodes.Status404NotFound, "Visitor item not found");
        var gateLocation = await GateOfAsync(user, gateLocationId, ct);
        if (gateLocation.SbuId != row.SbuId) throw AccessPolicy.Deny(user, "check out an item logged at another SBU");
        if (row.CheckedOutAt is not null) throw new ApiException(StatusCodes.Status409Conflict, "The item has already left");
        row.CheckedOutAt = clock.UtcNow;
        row.CheckedOutBy = Name(user);
        row.CheckedOutGateId = gateLocation.Id;
        await db.SaveChangesAsync(ct);
    }

    // ── SG-06 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// SG-06: one-tap escalation from the gate with notes (and a photo). The SBU's Admin Manager is told as the security
    /// supervisor, and SBU IT and the Admin Manager of the asset's own SBU; the incident goes into the asset's history.
    /// </summary>
    // ASSUMPTION: there is no Security Supervisor role; the SBU Admin Manager receives security escalations.
    public async Task<GateIncident> EscalateAsync(AuthUser user, string kind, int? gateLocationId, string? code, int? gatePassId, string notes, UploadedPhoto? photo, CancellationToken ct)
    {
        Location? gateLocation = gateLocationId is { } g ? await GateOfAsync(user, g, ct) : null;
        var sbuId = gateLocation?.SbuId ?? user.SbuId ?? throw new ApiException(StatusCodes.Status400BadRequest, "gateLocationId: Required");
        Asset? asset = null;
        if (code?.Trim().ToUpperInvariant() is { Length: > 0 } text)
            asset = await db.Assets.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.AssetNumber!.ToUpper() == text || a.RfidTag!.ToUpper() == text || a.SerialNumber == text, ct);
        if (gatePassId is { } passId)
        {
            var pass = await db.GatePasses.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.Id == passId, ct)
                ?? throw new ApiException(StatusCodes.Status400BadRequest, "gatePassId: no such gate pass");
            asset ??= await db.Assets.IgnoreQueryFilters().FirstAsync(a => a.Id == pass.AssetId, ct);
        }
        var officer = Name(user);
        var row = new GateIncident
        {
            SbuId = sbuId, GateLocationId = gateLocation?.Id, Kind = kind, AssetId = asset?.Id, GatePassId = gatePassId, Code = code?.Trim(), Notes = notes.Trim(),
            Photo = photo?.Content, PhotoContentType = photo?.ContentType, Officer = officer, CreatedAt = clock.UtcNow,
        };
        db.GateIncidents.Add(row);
        if (asset is not null)
        {
            db.AssetLifecycleEvents.Add(new AssetLifecycleEvent
            {
                AssetId = asset.Id, EventType = LifecycleEventTypes.Note, PerformedBy = officer, Timestamp = clock.UtcNow,
                Notes = $"Gate incident ({kind.Replace('_', ' ')}){(gateLocation is null ? "" : $" at {gateLocation.Name}")}: {row.Notes}",
            });
        }
        await db.SaveChangesAsync(ct);
        audit.Record(user, "gate_incident", row.Id, asset?.Id, "gate_incident", kind, null, code, row.Notes);
        var ownerSbu = asset?.SbuId;
        var people = await db.Users.Where(u => u.Active && (u.Role == Roles.SbuAdmin && (u.SbuId == sbuId || u.SbuId == ownerSbu)
            || u.Role == Roles.SbuIt && ownerSbu != null && u.SbuId == ownerSbu)).ToListAsync(ct);
        notifications.Notify(people, "incident", $"Gate incident: {kind.Replace('_', ' ')}{(asset is null ? "" : $" — {asset.Name}")}",
            $"{officer}{(gateLocation is null ? "" : $" at {gateLocation.Name}")}: {row.Notes}", asset is null ? "/gate" : $"/assets/{asset.Id}/card");
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<List<GateIncidentDto>> IncidentsAsync(AuthUser user, CancellationToken ct)
    {
        var query = db.GateIncidents.AsNoTracking();
        if (Roles.IsSbuScoped(user.Role)) query = query.Where(i => i.SbuId == user.SbuId || db.Assets.IgnoreQueryFilters().Any(a => a.Id == i.AssetId && a.SbuId == user.SbuId));
        var gates = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        var rows = await query.OrderByDescending(i => i.CreatedAt).Take(200)
            .Select(i => new { i.Id, i.Kind, i.GateLocationId, i.AssetId, AssetName = db.Assets.IgnoreQueryFilters().Where(a => a.Id == i.AssetId).Select(a => a.Name).FirstOrDefault(),
                i.GatePassId, i.Code, i.Notes, HasPhoto = i.Photo != null, i.Officer, i.CreatedAt })
            .ToListAsync(ct);
        return rows.Select(i => new GateIncidentDto(i.Id, i.Kind, i.GateLocationId is { } g ? gates.GetValueOrDefault(g) : null, i.AssetId, i.AssetName, i.GatePassId,
            i.Code, i.Notes, i.HasPhoto, i.Officer, i.CreatedAt)).ToList();
    }

    public async Task<UploadedPhoto> PhotoAsync(AuthUser user, string what, int id, CancellationToken ct)
    {
        var (sbuId, photo, type) = what == "incident"
            ? await db.GateIncidents.AsNoTracking().Where(i => i.Id == id).Select(i => new ValueTuple<int, byte[]?, string?>(i.SbuId, i.Photo, i.PhotoContentType)).FirstOrDefaultAsync(ct)
            : await db.VisitorItems.AsNoTracking().Where(v => v.Id == id).Select(v => new ValueTuple<int, byte[]?, string?>(v.SbuId, v.Photo, v.PhotoContentType)).FirstOrDefaultAsync(ct);
        if (photo is null || type is null) throw new ApiException(StatusCodes.Status404NotFound, "No photo");
        if (!AccessPolicy.Sys(user) && Roles.IsSbuScoped(user.Role) && !AccessPolicy.OwnSbu(user, sbuId)) throw AccessPolicy.Deny(user, "see another SBU's gate records");
        return new UploadedPhoto(photo, type);
    }

    // ── LM-04 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// LM-04: once a day (from 07:00 local time) each HOD gets an in-app alert for every new problem in their department
    /// — an overdue gate pass return, a lost or stolen item, faulty equipment — each linked to the asset, and one email
    /// digest of everything still open.
    /// </summary>
    public async Task<int> SendHodDigestsAsync(CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, config.TimeZone);
        if (local.Hour < 7) return 0;
        var today = Today;
        var hods = await db.Users.Where(u => u.Active && u.Role == Roles.Hod && u.EmployeeId != null && (u.LastDigestOn == null || u.LastDigestOn != today)).ToListAsync(ct);
        if (hods.Count == 0) return 0;
        var overdue = await gate.OverdueAsync(null, ct);
        var sent = 0;
        foreach (var hod in hods)
        {
            hod.LastDigestOn = today;
            var departmentId = await db.Employees.Where(e => e.Id == hod.EmployeeId).Select(e => e.DepartmentId).FirstOrDefaultAsync(ct);
            if (departmentId is null) continue;
            var assets = await db.Assets.IgnoreQueryFilters().AsNoTracking().Where(a => a.DepartmentId == departmentId && a.Status != AssetStatuses.Disposed)
                .Select(a => new { a.Id, a.Name, a.AssetNumber, a.Status, a.Condition, a.AssignedTo }).ToListAsync(ct);
            var ids = assets.Select(a => a.Id).ToHashSet();
            var alerts = new List<(string Key, string Title, int AssetId)>();
            foreach (var o in overdue.Where(o => ids.Contains(o.AssetId)))
                alerts.Add(($"overdue:{o.GatePassId}", $"Overdue return: {o.AssetName} (due {o.ReturnDate}, {o.RequestedBy})", o.AssetId));
            foreach (var a in assets.Where(a => a.Status == AssetStatuses.LostStolen))
                alerts.Add(("lost", $"Lost / stolen: {a.Name} ({a.AssetNumber ?? "number pending"}, {a.AssignedTo})", a.Id));
            // ASSUMPTION: "faulty" = condition poor or damaged and not already with repairs (as on the SI-06 status board).
            foreach (var a in assets.Where(a => a.Condition is "poor" or "damaged" && a.Status is not (AssetStatuses.InRepair or AssetStatuses.RepairRequested or AssetStatuses.LostStolen)))
                alerts.Add(("faulty", $"Faulty: {a.Name} ({a.Condition}, {a.AssignedTo})", a.Id));
            if (alerts.Count == 0) continue;

            var links = alerts.Select(x => $"/assets/{x.AssetId}/card").ToList();
            var known = (await db.Notifications.AsNoTracking().Where(n => n.UserId == hod.Id && n.Kind == "department" && links.Contains(n.Link!))
                .Select(n => n.Title).ToListAsync(ct)).ToHashSet();
            foreach (var alert in alerts.Where(x => !known.Contains(x.Title)))
                notifications.InAppOnly(hod, "department", alert.Title, "In your department (LM-04)", $"/assets/{alert.AssetId}/card");
            notifications.EmailTo(hod, $"AssetIQ: your department today — {alerts.Count} item(s) to follow up",
                string.Join("\n", alerts.Select(x => $"• {x.Title} — /assets/{x.AssetId}/card")));
            sent++;
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }

    private async Task<Location> GateOfAsync(AuthUser user, int gateLocationId, CancellationToken ct)
    {
        var location = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == gateLocationId, ct)
            ?? throw new ApiException(StatusCodes.Status400BadRequest, "gateLocationId: no such location");
        if (location.Type != LocationTypes.Gate) throw new ApiException(StatusCodes.Status400BadRequest, $"gateLocationId: \"{location.Name}\" is not a gate");
        if (!AccessPolicy.Sys(user) && Roles.IsSbuScoped(user.Role) && !AccessPolicy.OwnSbu(user, location.SbuId)) throw AccessPolicy.Deny(user, "record at another SBU's gate");
        return location;
    }

    private static string Name(AuthUser user) => user.Name.Length > 0 ? user.Name : user.Username;
}
