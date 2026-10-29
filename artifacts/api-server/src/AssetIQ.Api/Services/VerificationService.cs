using AssetIQ.Api.Auth;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

/// <summary>
/// Phase 4c, physical verification (SI-07) and reconciliation (SF-06). A session counts one location: the items are
/// scanned, closing it compares the scans with the register (found, missing, unrecorded), and SBU Finance resolves
/// each difference with a comment. The session is reconciled (signed off) once every difference is resolved.
/// </summary>
public sealed class VerificationService(
    AssetIqDbContext db,
    AppClock clock,
    AppConfig config,
    AssetStatusService statuses,
    NotificationService notifications,
    AuditTrail audit)
{
    /// <summary>Statuses an item at a location is expected to be counted in (not off the premises, lost or disposed).</summary>
    private static readonly string[] NotExpected = [AssetStatuses.OutOnGatePass, AssetStatuses.LostStolen, AssetStatuses.Disposed];

    /// <summary>The scope a role counts: SBU IT and Group IT the IT items, Stores and the Admin Manager the non-IT items, Finance either.</summary>
    public static string ScopeFor(AuthUser user, string? requested) => user.Role switch
    {
        Roles.SbuIt or Roles.GroupIt => requested is null or VerificationScopes.It ? VerificationScopes.It
            : throw AccessPolicy.Deny(user, "count non-IT items (Stores or the SBU Admin Manager does)"),
        Roles.Stores or Roles.SbuAdmin => requested is null or VerificationScopes.NonIt ? VerificationScopes.NonIt
            : throw AccessPolicy.Deny(user, "count IT items (SBU IT does)"),
        _ => requested ?? VerificationScopes.All,
    };

    public async Task<VerificationSession> StartAsync(AuthUser user, int locationId, string? scope, CancellationToken ct)
    {
        var location = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == locationId, ct)
            ?? throw new ApiException(StatusCodes.Status400BadRequest, "locationId: no such location");
        if (location.SbuId is null) throw new ApiException(StatusCodes.Status400BadRequest, "locationId: the location belongs to no SBU");
        EnsureSbu(user, location.SbuId, "count another SBU's location");
        var resolved = ScopeFor(user, scope);
        var open = await db.VerificationSessions.AnyAsync(s => s.LocationId == locationId && s.Scope == resolved && s.Status == VerificationStatuses.Open, ct);
        if (open) throw new ApiException(StatusCodes.Status409Conflict, $"A {resolved} count of \"{location.Name}\" is already open");
        var session = new VerificationSession
        {
            SbuId = location.SbuId.Value, LocationId = locationId, Scope = resolved, Status = VerificationStatuses.Open,
            StartedBy = Name(user), StartedAt = clock.UtcNow,
        };
        db.VerificationSessions.Add(session);
        await db.SaveChangesAsync(ct);
        audit.Record(user, "verification", session.Id, null, "verification_started", "location", null, location.Name, resolved);
        await db.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>Scans into an open session; a code scanned twice counts once. Unknown codes are kept (unrecorded).</summary>
    public async Task<int> ScanAsync(AuthUser user, int sessionId, IReadOnlyList<string> codes, CancellationToken ct)
    {
        var session = await LoadAsync(user, sessionId, ct);
        if (session.Status != VerificationStatuses.Open) throw new ApiException(StatusCodes.Status409Conflict, "The count has been closed");
        if (!CanRun(user, session)) throw AccessPolicy.Deny(user, "scan into this count");
        var seen = (await db.VerificationScans.Where(s => s.SessionId == sessionId).Select(s => s.Code).ToListAsync(ct)).ToHashSet();
        var added = 0;
        foreach (var raw in codes)
        {
            var code = raw.Trim().ToUpperInvariant();
            if (code.Length == 0 || !seen.Add(code)) continue;
            var assetId = await db.Assets.IgnoreQueryFilters()
                .Where(a => a.AssetNumber!.ToUpper() == code || a.RfidTag!.ToUpper() == code || a.PreviousAssetNumber!.ToUpper() == code || a.SerialNumber == code)
                .Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
            if (assetId is not null && await db.VerificationScans.AnyAsync(s => s.SessionId == sessionId && s.AssetId == assetId, ct)) continue;
            db.VerificationScans.Add(new VerificationScan { SessionId = sessionId, Code = code, AssetId = assetId, ScannedBy = Name(user), ScannedAt = clock.UtcNow });
            added++;
        }
        await db.SaveChangesAsync(ct);
        return added;
    }

    /// <summary>Close the count: found / missing / unrecorded against what the register says is at the location.</summary>
    public async Task CloseAsync(AuthUser user, int sessionId, CancellationToken ct)
    {
        var session = await LoadAsync(user, sessionId, ct);
        if (session.Status != VerificationStatuses.Open) throw new ApiException(StatusCodes.Status409Conflict, "The count has already been closed");
        if (!CanRun(user, session)) throw AccessPolicy.Deny(user, "close this count");

        var expected = await ExpectedQuery(session).Select(a => a.Id).ToListAsync(ct);
        var scans = await db.VerificationScans.Where(s => s.SessionId == sessionId).ToListAsync(ct);
        var scanned = scans.Where(s => s.AssetId is not null).Select(s => s.AssetId!.Value).ToHashSet();
        var expectedSet = expected.ToHashSet();
        var now = clock.UtcNow;
        foreach (var id in expected)
        {
            db.VerificationResults.Add(new VerificationResult
            {
                SessionId = sessionId, AssetId = id, Result = scanned.Contains(id) ? VerificationResults.Found : VerificationResults.Missing,
            });
        }
        foreach (var scan in scans.Where(s => s.AssetId is null || !expectedSet.Contains(s.AssetId.Value)))
            db.VerificationResults.Add(new VerificationResult { SessionId = sessionId, AssetId = scan.AssetId, Code = scan.Code, Result = VerificationResults.Unrecorded });

        session.Status = VerificationStatuses.Closed;
        session.ClosedAt = now;
        await db.SaveChangesAsync(ct);
        var differences = await db.VerificationResults.CountAsync(r => r.SessionId == sessionId && r.Result != VerificationResults.Found, ct);
        if (differences == 0)
        {
            session.Status = VerificationStatuses.Reconciled;
        }
        else
        {
            var finance = await db.Users.Where(u => u.Active && u.Role == Roles.SbuFinance && u.SbuId == session.SbuId).ToListAsync(ct);
            notifications.Notify(finance, "verification", $"Count #{sessionId}: {differences} difference(s) to reconcile",
                $"Counted by {session.StartedBy}. Resolve each one with a comment (SF-06).", "/verification");
        }
        audit.Record(user, "verification", sessionId, null, "verification_closed", "differences", null, differences, null);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// SF-06: SBU Finance resolves a difference, with a comment. Missing: adjust (recorded elsewhere), write off (marked
    /// lost / stolen), investigate. Unrecorded: adjust (the register moved to this location), register (an unknown item
    /// to be registered), investigate.
    /// </summary>
    public async Task ResolveAsync(AuthUser user, int sessionId, int resultId, string resolution, string comment, CancellationToken ct)
    {
        var session = await LoadAsync(user, sessionId, ct);
        if (!AccessPolicy.IsSbuFinanceOf(user, session.SbuId)) throw AccessPolicy.Deny(user, "reconcile a count (SBU Finance does, SF-06)");
        if (session.Status == VerificationStatuses.Open) throw new ApiException(StatusCodes.Status409Conflict, "Close the count first");
        var result = await db.VerificationResults.FirstOrDefaultAsync(r => r.Id == resultId && r.SessionId == sessionId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Result not found");
        if (result.Result == VerificationResults.Found) throw new ApiException(StatusCodes.Status409Conflict, "A found item needs no resolution");
        if (result.Resolution is not null) throw new ApiException(StatusCodes.Status409Conflict, $"Already resolved ({result.Resolution})");
        var allowed = result.Result == VerificationResults.Missing
            ? new[] { VerificationResolutions.Adjust, VerificationResolutions.WriteOff, VerificationResolutions.Investigate }
            : result.AssetId is null
                ? [VerificationResolutions.Register, VerificationResolutions.Investigate]
                : [VerificationResolutions.Adjust, VerificationResolutions.Investigate];
        if (!allowed.Contains(resolution))
            throw new ApiException(StatusCodes.Status400BadRequest, $"resolution: for a {result.Result} item one of {string.Join(", ", allowed)}");

        var now = clock.UtcNow;
        var text = comment.Trim();
        if (result.AssetId is { } assetId)
        {
            var asset = await db.Assets.IgnoreQueryFilters().Include(a => a.AssetCategory).FirstAsync(a => a.Id == assetId, ct);
            if (resolution == VerificationResolutions.WriteOff)
            {
                // ASSUMPTION: a write-off marks the item lost / stolen (GI-07 facts from the count); the book value is
                // written off through the disposal workflow as for any lost item.
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, config.TimeZone)).ToString("yyyy-MM-dd");
                if (!statuses.TrySystemChange(user, asset, new StatusChange(AssetStatuses.LostStolen, $"Written off after count #{sessionId}: {text}", today, Name(user), text)))
                    throw new ApiException(StatusCodes.Status409Conflict, $"\"{asset.Name}\" is {AssetStatusMachine.Label(asset.Status)} and cannot be marked lost / stolen");
            }
            else if (resolution == VerificationResolutions.Adjust && result.Result == VerificationResults.Unrecorded)
            {
                db.Movements.Add(new Movement
                {
                    AssetId = asset.Id, FromLocationId = asset.CurrentLocationId, ToLocationId = session.LocationId, TriggeredBy = MovementTriggers.Manual,
                    MovedBy = Name(user), Notes = $"Register adjusted after count #{sessionId}: {text}", Timestamp = now,
                });
                asset.CurrentLocationId = session.LocationId;
                asset.UpdatedAt = now;
            }
        }
        else if (resolution == VerificationResolutions.Register)
        {
            var registrars = await db.Users.Where(u => u.Active && (u.Role == Roles.Stores || u.Role == Roles.SbuIt) && u.SbuId == session.SbuId).ToListAsync(ct);
            notifications.Notify(registrars, "verification", $"Register the item {result.Code}", $"Found in count #{sessionId} but not in the register: {text}", "/assets/new");
        }

        result.Resolution = resolution;
        result.Comment = text;
        result.ResolvedBy = Name(user);
        result.ResolvedAt = now;
        audit.Record(user, "verification", sessionId, result.AssetId, "verification_resolved", result.Result, null, resolution, text);
        await db.SaveChangesAsync(ct);

        var open = await db.VerificationResults.AnyAsync(r => r.SessionId == sessionId && r.Result != VerificationResults.Found && r.Resolution == null, ct);
        if (!open)
        {
            session.Status = VerificationStatuses.Reconciled;
            audit.Record(user, "verification", sessionId, null, "verification_reconciled", null, null, null, null);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<List<VerificationSessionDto>> ListAsync(AuthUser user, CancellationToken ct)
    {
        var query = db.VerificationSessions.AsNoTracking();
        if (Roles.IsSbuScoped(user.Role)) query = query.Where(s => s.SbuId == user.SbuId);
        var ids = await query.OrderByDescending(s => s.StartedAt).Select(s => s.Id).Take(200).ToListAsync(ct);
        var list = new List<VerificationSessionDto>();
        foreach (var id in ids) list.Add(await GetAsync(user, id, ct, details: false));
        return list;
    }

    public async Task<VerificationSessionDto> GetAsync(AuthUser user, int sessionId, CancellationToken ct, bool details = true)
    {
        var session = await LoadAsync(user, sessionId, ct);
        var location = await db.Locations.AsNoTracking().Where(l => l.Id == session.LocationId).Select(l => l.Name).FirstAsync(ct);
        var sbu = await db.Sbus.AsNoTracking().Where(s => s.Id == session.SbuId).Select(s => s.Code).FirstAsync(ct);
        var results = await (from r in db.VerificationResults.AsNoTracking().Where(r => r.SessionId == sessionId)
                             join a in db.Assets.IgnoreQueryFilters() on r.AssetId equals a.Id into aa
                             from a in aa.DefaultIfEmpty()
                             orderby r.Result, r.Id
                             select new VerificationResultDto
                             {
                                 Id = r.Id, AssetId = r.AssetId, AssetName = a == null ? null : a.Name, AssetNumber = a == null ? null : a.AssetNumber,
                                 RfidTag = a == null ? null : a.RfidTag, Code = r.Code, Result = r.Result,
                                 RecordedLocationName = a == null ? null : a.CurrentLocation!.Name,
                                 Resolution = r.Resolution, Comment = r.Comment, ResolvedBy = r.ResolvedBy, ResolvedAt = r.ResolvedAt,
                             }).ToListAsync(ct);
        var scans = details
            ? await (from s in db.VerificationScans.AsNoTracking().Where(s => s.SessionId == sessionId)
                     join a in db.Assets.IgnoreQueryFilters() on s.AssetId equals a.Id into aa
                     from a in aa.DefaultIfEmpty()
                     orderby s.ScannedAt descending, s.Id descending
                     select new VerificationScanDto { Code = s.Code, AssetId = s.AssetId, AssetName = a == null ? null : a.Name, ScannedBy = s.ScannedBy, ScannedAt = s.ScannedAt })
                .ToListAsync(ct)
            : [];
        var scanCount = details ? scans.Count : await db.VerificationScans.CountAsync(s => s.SessionId == sessionId, ct);
        var expected = session.Status == VerificationStatuses.Open
            ? await ExpectedQuery(session).CountAsync(ct)
            : results.Count(r => r.Result != VerificationResults.Unrecorded);
        return new VerificationSessionDto
        {
            Id = session.Id, SbuId = session.SbuId, SbuCode = sbu, LocationId = session.LocationId, LocationName = location, Scope = session.Scope,
            Status = session.Status, StartedBy = session.StartedBy, StartedAt = session.StartedAt, ClosedAt = session.ClosedAt,
            ExpectedCount = expected, ScanCount = scanCount,
            FoundCount = results.Count(r => r.Result == VerificationResults.Found),
            MissingCount = results.Count(r => r.Result == VerificationResults.Missing),
            UnrecordedCount = results.Count(r => r.Result == VerificationResults.Unrecorded),
            UnresolvedCount = results.Count(r => r.Result != VerificationResults.Found && r.Resolution is null),
            CanScan = session.Status == VerificationStatuses.Open && CanRun(user, session),
            CanResolve = session.Status == VerificationStatuses.Closed && AccessPolicy.IsSbuFinanceOf(user, session.SbuId),
            Results = details ? results : [],
            Scans = scans,
        };
    }

    private IQueryable<Asset> ExpectedQuery(VerificationSession session)
    {
        var query = db.Assets.IgnoreQueryFilters().Where(a => a.CurrentLocationId == session.LocationId && !NotExpected.Contains(a.Status));
        return session.Scope switch
        {
            VerificationScopes.It => query.Where(a => a.AssetCategory.IsIt),
            VerificationScopes.NonIt => query.Where(a => !a.AssetCategory.IsIt),
            _ => query,
        };
    }

    private async Task<VerificationSession> LoadAsync(AuthUser user, int sessionId, CancellationToken ct)
    {
        var session = await db.VerificationSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Count not found");
        EnsureSbu(user, session.SbuId, "see another SBU's counts");
        return session;
    }

    /// <summary>Whoever may start a count of that scope at the SBU may scan into it and close it.</summary>
    private static bool CanRun(AuthUser user, VerificationSession session)
    {
        if (AccessPolicy.Sys(user)) return true;
        if (!Roles.Parse(Roles.VerificationRunners).Contains(user.Role)) return false;
        try { return ScopeFor(user, session.Scope) == session.Scope; }
        catch (ApiException) { return false; }
    }

    private static void EnsureSbu(AuthUser user, int? sbuId, string action)
    {
        if (!AccessPolicy.Sys(user) && Roles.IsSbuScoped(user.Role) && !AccessPolicy.OwnSbu(user, sbuId)) throw AccessPolicy.Deny(user, action);
    }

    private static string Name(AuthUser user) => user.Name.Length > 0 ? user.Name : user.Username;
}
