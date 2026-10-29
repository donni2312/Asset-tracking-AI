using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services.NlQuery;

public sealed record QueryAnswer(string Answer, IReadOnlyList<AssetDto> RelatedAssets, IReadOnlyList<GatePassDto> RelatedGatePasses);

/// <summary>Turns an <see cref="Intent"/> into database queries and a plain-English answer.</summary>
public sealed class IntentExecutor(AssetIqDbContext db, AssetQueries assets, GatePassService gatePasses)
{
    private const int MaxListed = 10;

    public static readonly IReadOnlyList<string> ExampleQuestions =
    [
        "Where is the Dell laptop assigned to John Doe?",
        "Which assets have not moved in 30 days?",
        "Show me all gate passes approved this week",
        "Which assets are missing?",
        "How many laptops do we have?",
        "Show me unassigned assets",
    ];

    public Task<QueryAnswer> ExecuteAsync(Intent intent, DateTime now, TimeZoneInfo zone, CancellationToken ct) => intent switch
    {
        LocateAssetIntent locate => LocateAssetAsync(locate, zone, ct),
        AssetsAssignedToIntent assigned => AssetsAssignedToAsync(assigned.Assignee, ct),
        NotMovedIntent notMoved => NotMovedAsync(notMoved.Days, now, zone, ct),
        GatePassesIntent passes => GatePassesAsync(passes, now, zone, ct),
        AssetsByStatusIntent byStatus => AssetsByStatusAsync(byStatus.Status, ct),
        CategoryCountIntent category => CategoryCountAsync(category.Category, ct),
        CountSummaryIntent => CountSummaryAsync(ct),
        UnassignedIntent => UnassignedAsync(ct),
        _ => Task.FromResult(Help()),
    };

    /// <summary>Every word of the query name appears in the assignee (case-insensitive).</summary>
    public static bool AssigneeMatches(string? assignedTo, string query)
    {
        if (string.IsNullOrEmpty(assignedTo)) return false;
        var hay = assignedTo.ToLowerInvariant();
        return query.ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(part => hay.Contains(part, StringComparison.Ordinal));
    }

    /// <summary>How many query terms the asset matches (brand/model in the name, category words, serial, tag).</summary>
    public static int ScoreAsset(AssetDto asset, IReadOnlyList<string> terms)
    {
        var hay = $"{asset.Name} {asset.Category} {asset.SerialNumber} {asset.RfidTag}".ToLowerInvariant();
        var score = 0;
        foreach (var term in terms)
        {
            var category = Vocabulary.CategoryFromWord(term);
            if (category is not null && asset.Category.ToLowerInvariant() == category.ToLowerInvariant()) score++;
            else if (hay.Contains(term.ToLowerInvariant(), StringComparison.Ordinal)) score++;
        }
        return score;
    }

    /// <summary>[from, to) window for a period in the organisation time zone; null bounds are open.</summary>
    public static (DateTime? From, DateTime? To, string Label) PeriodWindow(string period, int? periodDays, DateTime now, TimeZoneInfo zone)
    {
        switch (period)
        {
            case Periods.Today:
                return (ZonedTime.StartOfZonedDay(now, zone), null, "today");
            case Periods.Yesterday:
            {
                var today = ZonedTime.StartOfZonedDay(now, zone);
                return (ZonedTime.StartOfZonedDay(today - TimeSpan.FromHours(12), zone), today, "yesterday");
            }
            case Periods.ThisWeek:
                return (ZonedTime.StartOfZonedWeek(now, zone), null, "this week");
            case Periods.LastWeek:
            {
                var thisWeek = ZonedTime.StartOfZonedWeek(now, zone);
                return (ZonedTime.StartOfZonedWeek(thisWeek - 3 * ZonedTime.Day, zone), thisWeek, "last week");
            }
            case Periods.ThisMonth:
                return (ZonedTime.StartOfZonedMonth(now, zone), null, "this month");
            case Periods.LastNDays:
            {
                var n = periodDays ?? 7;
                return (DaysBefore(now, n), null, $"in the last {n} days");
            }
            default:
                return (null, null, "");
        }
    }

    private async Task<QueryAnswer> LocateAssetAsync(LocateAssetIntent intent, TimeZoneInfo zone, CancellationToken ct)
    {
        var all = await assets.ListDetailedAsync(ct: ct);
        var candidates = intent.Assignee is null ? all : all.Where(a => AssigneeMatches(a.AssignedTo, intent.Assignee)).ToList();

        List<AssetDto> matches = [];
        var partial = false;
        if (intent.Terms.Count > 0)
        {
            var scored = candidates.Select(asset => (Asset: asset, Score: ScoreAsset(asset, intent.Terms))).Where(s => s.Score > 0).ToList();
            var best = scored.Count == 0 ? 0 : scored.Max(s => s.Score);
            matches = scored.Where(s => s.Score == best).Select(s => s.Asset).ToList();
            partial = matches.Count > 0 && best < intent.Terms.Count;
        }
        else if (intent.Assignee is not null)
        {
            matches = candidates;
        }

        var described = string.Join(" ",
            new[] { string.Join(" ", intent.Terms), intent.Assignee is null ? "" : $"assigned to {intent.Assignee}" }.Where(s => s.Length > 0));
        if (matches.Count == 0)
        {
            var hint = intent.Assignee is not null && candidates.Count == 0
                ? $" Nobody matching \"{intent.Assignee}\" currently holds an asset."
                : " Try the asset name, brand, category, serial number or RFID tag.";
            return new QueryAnswer($"I couldn't find an asset matching \"{(described.Length > 0 ? described : "your description")}\".{hint}", [], []);
        }

        var last = await assets.LastMovementsForAsync(matches.Select(m => m.Id).ToList(), ct);
        var lines = matches.Select(a => DescribeWhereabouts(a, last.GetValueOrDefault(a.Id), zone)).ToList();
        var prefix = partial ? "Closest match" + (matches.Count > 1 ? "es" : "") + ":\n" : "";
        var answer = lines.Count == 1
            ? $"{prefix}{lines[0]}"
            : $"{prefix}{Plural(matches.Count, "matching asset")}:\n{BulletList(lines)}";
        return new QueryAnswer(answer, matches, []);
    }

    private async Task<QueryAnswer> AssetsAssignedToAsync(string assignee, CancellationToken ct)
    {
        var held = (await assets.ListDetailedAsync(ct: ct)).Where(a => AssigneeMatches(a.AssignedTo, assignee)).ToList();
        if (held.Count == 0) return new QueryAnswer($"No assets are currently assigned to anyone matching \"{assignee}\".", [], []);
        var lines = held.Select(a => $"{a.Name} ({a.Category}) — {a.CurrentLocationName ?? "no recorded location"}").ToList();
        return new QueryAnswer($"{Plural(held.Count, "asset")} assigned to {held[0].AssignedTo}:\n{BulletList(lines)}", held, []);
    }

    private async Task<QueryAnswer> NotMovedAsync(int days, DateTime now, TimeZoneInfo zone, CancellationToken ct)
    {
        var tracked = (await assets.ListDetailedAsync(ct: ct)).Where(a => a.Status != AssetStatuses.Disposed).ToList();
        var lastMoved = await assets.LastMovedAtAsync(ct);
        var cutoff = DaysBefore(now, days);

        var stale = tracked
            .Select(asset => (Asset: asset, LastMovedAt: lastMoved.TryGetValue(asset.Id, out var at) ? at : (DateTime?)null))
            .Where(x => (x.LastMovedAt ?? x.Asset.CreatedAt) < cutoff)
            .OrderBy(x => x.LastMovedAt ?? x.Asset.CreatedAt)
            .ToList();

        if (stale.Count == 0) return new QueryAnswer($"Every tracked asset has moved within the last {days} days.", [], []);

        var lines = stale.Select(x =>
        {
            var where = x.Asset.CurrentLocationName ?? "no recorded location";
            if (x.LastMovedAt is not { } lastMovedAt)
            {
                return $"{x.Asset.Name} ({where}) — never moved since it was registered on {ZonedTime.FormatZonedDay(x.Asset.CreatedAt, zone)}";
            }
            var ago = (long)Math.Floor((now - lastMovedAt) / ZonedTime.Day);
            return $"{x.Asset.Name} ({where}) — last moved {ZonedTime.FormatZonedDay(lastMovedAt, zone)}, {ago} days ago";
        }).ToList();
        return new QueryAnswer(
            $"{Plural(stale.Count, "asset")} {(stale.Count == 1 ? "has" : "have")} not moved in {days} days:\n{BulletList(lines)}",
            stale.Select(s => s.Asset).ToList(),
            []);
    }

    private async Task<QueryAnswer> GatePassesAsync(GatePassesIntent intent, DateTime now, TimeZoneInfo zone, CancellationToken ct)
    {
        // Approvals and rejections are dated by their decision; passes decided before decided_at existed
        // fall back to updated_at (the decision time in the old schema). Everything else by its creation.
        var decided = intent.Status is GatePassStatuses.Approved or GatePassStatuses.Rejected;
        var (from, to, label) = PeriodWindow(intent.Period, intent.PeriodDays, now, zone);

        var query = db.GatePasses.AsNoTracking();
        if (intent.Status is not null) query = query.Where(p => p.Status == intent.Status);
        if (decided)
        {
            if (from is { } start) query = query.Where(p => (p.DecidedAt ?? p.UpdatedAt) >= start);
            if (to is { } end) query = query.Where(p => (p.DecidedAt ?? p.UpdatedAt) < end);
            query = query.OrderByDescending(p => p.DecidedAt ?? p.UpdatedAt).ThenByDescending(p => p.Id);
        }
        else
        {
            if (from is { } start) query = query.Where(p => p.CreatedAt >= start);
            if (to is { } end) query = query.Where(p => p.CreatedAt < end);
            query = query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id);
        }
        var passes = await gatePasses.Project(query).ToListAsync(ct);

        var verb = intent.Status switch
        {
            GatePassStatuses.Approved => "approved",
            GatePassStatuses.Rejected => "rejected",
            GatePassStatuses.Pending => "pending",
            _ => "raised",
        };
        var when = label.Length > 0 ? $" {label}" : "";
        if (passes.Count == 0)
        {
            var none = intent.Status == GatePassStatuses.Pending ? $"No gate passes are pending{when}." : $"No gate passes were {verb}{when}.";
            return new QueryAnswer(none, [], []);
        }

        var lines = passes.Select(p =>
        {
            var destination = string.IsNullOrEmpty(p.Destination) ? "" : $" → {p.Destination}";
            var decidedAt = p.DecidedAt ?? p.UpdatedAt;
            var detail = p.Status switch
            {
                GatePassStatuses.Approved => $"approved by {p.ApprovedBy ?? "unknown"} on {ZonedTime.FormatZoned(decidedAt, zone)}",
                GatePassStatuses.Rejected => $"rejected by {p.ApprovedBy ?? "unknown"} on {ZonedTime.FormatZoned(decidedAt, zone)}",
                _ => $"requested by {p.RequestedBy} on {ZonedTime.FormatZoned(p.CreatedAt, zone)}{(p.Source == GatePassSources.RfidGate ? " (auto-raised at a gate)" : "")}",
            };
            return $"#{p.Id} {p.AssetName}{destination} — {detail}";
        }).ToList();

        var assetIds = passes.Select(p => p.AssetId).Distinct().ToList();
        var relatedAssets = await assets.ListDetailedAsync(a => assetIds.Contains(a.Id), ct);
        var count = Plural(passes.Count, "gate pass", "gate passes");
        var head = intent.Status == GatePassStatuses.Pending ? $"{count} pending{when}" : $"{count} {verb}{when}";
        return new QueryAnswer($"{head}:\n{BulletList(lines)}", relatedAssets, passes);
    }

    private async Task<QueryAnswer> AssetsByStatusAsync(string status, CancellationToken ct)
    {
        var matching = await assets.ListDetailedAsync(a => a.Status == status, ct);
        var label = status switch
        {
            AssetStatuses.InRepair => "in repair",
            AssetStatuses.LostStolen => "lost or stolen",
            _ => status,
        };
        if (matching.Count == 0) return new QueryAnswer($"No assets are currently {label}.", [], []);
        var lines = matching
            .Select(a => $"{a.Name} (SN {a.SerialNumber}){(string.IsNullOrEmpty(a.CurrentLocationName) ? "" : $" — last known at {a.CurrentLocationName}")}")
            .ToList();
        return new QueryAnswer(
            $"{Plural(matching.Count, "asset")} {(matching.Count == 1 ? "is" : "are")} {label}:\n{BulletList(lines)}",
            matching,
            []);
    }

    private async Task<QueryAnswer> CategoryCountAsync(string category, CancellationToken ct)
    {
        var lower = category.ToLowerInvariant();
        var inCategory = (await assets.ListDetailedAsync(ct: ct)).Where(a => a.Category.ToLowerInvariant() == lower).ToList();
        int ByStatus(string s) => inCategory.Count(a => a.Status == s);
        var answer = inCategory.Count == 0
            ? $"There are no {lower} assets in the register."
            : $"There {(inCategory.Count == 1 ? "is" : "are")} {Plural(inCategory.Count, lower)} in the register — " +
              $"{ByStatus(AssetStatuses.InUse)} in use, {ByStatus(AssetStatuses.InStore)} in store, {ByStatus(AssetStatuses.InRepair)} in repair, " +
              $"{ByStatus(AssetStatuses.LostStolen)} lost or stolen, {ByStatus(AssetStatuses.Disposed)} disposed.";
        return new QueryAnswer(answer, inCategory, []);
    }

    private async Task<QueryAnswer> CountSummaryAsync(CancellationToken ct)
    {
        var all = await assets.ListDetailedAsync(ct: ct);
        int ByStatus(string s) => all.Count(a => a.Status == s);
        return new QueryAnswer(
            $"Total assets: {all.Count}. In use: {ByStatus(AssetStatuses.InUse)}. In store: {ByStatus(AssetStatuses.InStore)}. In repair: {ByStatus(AssetStatuses.InRepair)}. " +
            $"Lost or stolen: {ByStatus(AssetStatuses.LostStolen)}. Disposed: {ByStatus(AssetStatuses.Disposed)}.",
            [],
            []);
    }

    private async Task<QueryAnswer> UnassignedAsync(CancellationToken ct)
    {
        var free = (await assets.ListDetailedAsync(ct: ct))
            .Where(a => string.IsNullOrEmpty(a.AssignedTo) && a.Status != AssetStatuses.Disposed)
            .ToList();
        if (free.Count == 0) return new QueryAnswer("Every active asset is assigned to someone.", [], []);
        return new QueryAnswer(
            $"{Plural(free.Count, "asset")} {(free.Count == 1 ? "is" : "are")} currently unassigned:\n{BulletList(free.Select(a => $"{a.Name} ({a.Category})").ToList())}",
            free,
            []);
    }

    private static QueryAnswer Help() =>
        new($"I can answer questions about the asset register, for example:\n{string.Join("\n", ExampleQuestions.Select(q => $"• \"{q}\""))}", [], []);

    private static string DescribeWhereabouts(AssetDto asset, LastMovement? last, TimeZoneInfo zone)
    {
        var sbu = string.IsNullOrEmpty(asset.SbuCode) ? "" : $" [{asset.SbuCode}]";
        string where;
        if (!string.IsNullOrEmpty(asset.CurrentLocationName)) where = $"is at {asset.CurrentLocationName}{sbu}";
        else if (last?.TriggeredBy == MovementTriggers.GatePass)
            where = $"is off-premises (left under gate pass #{last.GatePassId?.ToString() ?? "null"} on {ZonedTime.FormatZoned(last.Timestamp, zone)})";
        else where = "has no recorded location";
        var holder = string.IsNullOrEmpty(asset.AssignedTo) ? "" : $", assigned to {asset.AssignedTo}";
        var seen = last is null
            ? " No movements recorded yet."
            : $" Last movement: {ZonedTime.FormatZoned(last.Timestamp, zone)} ({TriggerLabel(last.TriggeredBy)}).";
        return $"{asset.Name} ({asset.RfidTag}) {where}{holder}.{seen}";
    }

    private static string TriggerLabel(string triggeredBy) => triggeredBy switch
    {
        MovementTriggers.Rfid => "RFID scan",
        MovementTriggers.GatePass => "gate pass exit",
        _ => "manual move",
    };

    private static string Plural(int n, string one, string? many = null) => $"{n} {(n == 1 ? one : many ?? one + "s")}";

    private static string BulletList(IReadOnlyList<string> lines)
    {
        var shown = lines.Take(MaxListed).Select(l => $"• {l}").ToList();
        if (lines.Count > MaxListed) shown.Add($"…and {lines.Count - MaxListed} more.");
        return string.Join("\n", shown);
    }

    /// <summary><paramref name="now"/> minus a number of days, clamped to the earliest representable instant.</summary>
    private static DateTime DaysBefore(DateTime now, int days)
    {
        var span = TimeSpan.FromDays(Math.Max(0, days));
        return now - DateTime.MinValue < span ? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc) : now - span;
    }
}
