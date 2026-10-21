using AssetIQ.Api.Domain;

namespace AssetIQ.Api.Services.NlQuery;

/// <summary>
/// Structured intents for the natural-language query engine. Both parsers (rule-based and the optional
/// OpenAI parser) produce an Intent; <see cref="IntentExecutor"/> turns it into database queries and a
/// written answer.
/// </summary>
public abstract record Intent(string Kind);

public sealed record LocateAssetIntent(IReadOnlyList<string> Terms, string? Assignee) : Intent(IntentKinds.LocateAsset)
{
    public bool Equals(LocateAssetIntent? other) =>
        other is not null && Assignee == other.Assignee && Terms.SequenceEqual(other.Terms);

    public override int GetHashCode() => HashCode.Combine(Kind, Assignee, string.Join('\u0001', Terms));

    public override string ToString() => $"LocateAssetIntent {{ Terms = [{string.Join(", ", Terms)}], Assignee = {Assignee} }}";
}

public sealed record AssetsAssignedToIntent(string Assignee) : Intent(IntentKinds.AssetsAssignedTo);

public sealed record NotMovedIntent(int Days) : Intent(IntentKinds.NotMoved);

/// <param name="Status">pending | approved | rejected, or null for all.</param>
/// <param name="Period">One of <see cref="Periods.All"/>.</param>
/// <param name="PeriodDays">Only for last_n_days.</param>
public sealed record GatePassesIntent(string? Status, string Period, int? PeriodDays) : Intent(IntentKinds.GatePasses);

public sealed record AssetsByStatusIntent(string Status) : Intent(IntentKinds.AssetsByStatus);

public sealed record CategoryCountIntent(string Category) : Intent(IntentKinds.CategoryCount);

public sealed record CountSummaryIntent() : Intent(IntentKinds.CountSummary);

public sealed record UnassignedIntent() : Intent(IntentKinds.Unassigned);

public sealed record HelpIntent() : Intent(IntentKinds.Help);

public static class IntentKinds
{
    public const string LocateAsset = "locate_asset";
    public const string AssetsAssignedTo = "assets_assigned_to";
    public const string NotMoved = "not_moved";
    public const string GatePasses = "gate_passes";
    public const string AssetsByStatus = "assets_by_status";
    public const string CategoryCount = "category_count";
    public const string CountSummary = "count_summary";
    public const string Unassigned = "unassigned";
    public const string Help = "help";

    public static readonly IReadOnlyList<string> All =
        [LocateAsset, AssetsAssignedTo, NotMoved, GatePasses, AssetsByStatus, CategoryCount, CountSummary, Unassigned, Help];
}

public static class Periods
{
    public const string Today = "today";
    public const string Yesterday = "yesterday";
    public const string ThisWeek = "this_week";
    public const string LastWeek = "last_week";
    public const string ThisMonth = "this_month";
    public const string LastNDays = "last_n_days";
    public const string AllTime = "all";

    public static readonly IReadOnlyList<string> All = [Today, Yesterday, ThisWeek, LastWeek, ThisMonth, LastNDays, AllTime];
}

public static class Vocabulary
{
    public static readonly IReadOnlyList<string> GatePassStatuses = Domain.GatePassStatuses.All;

    public static readonly IReadOnlyList<string> AssetStatuses = Domain.AssetStatuses.All;

    /// <summary>Asset categories used by the registry.</summary>
    public static readonly IReadOnlyList<string> Categories =
        ["Laptop", "Desktop", "Projector", "Phone", "Printer", "Network Equipment", "UPS", "Furniture", "Vehicle", "Other"];

    private static readonly Dictionary<string, string> CategoryAliases = new(StringComparer.Ordinal)
    {
        ["laptop"] = "Laptop",
        ["laptops"] = "Laptop",
        ["notebook"] = "Laptop",
        ["notebooks"] = "Laptop",
        ["desktop"] = "Desktop",
        ["desktops"] = "Desktop",
        ["pc"] = "Desktop",
        ["pcs"] = "Desktop",
        ["workstation"] = "Desktop",
        ["workstations"] = "Desktop",
        ["projector"] = "Projector",
        ["projectors"] = "Projector",
        ["phone"] = "Phone",
        ["phones"] = "Phone",
        ["smartphone"] = "Phone",
        ["smartphones"] = "Phone",
        ["mobile"] = "Phone",
        ["mobiles"] = "Phone",
        ["printer"] = "Printer",
        ["printers"] = "Printer",
        ["router"] = "Network Equipment",
        ["routers"] = "Network Equipment",
        ["switch"] = "Network Equipment",
        ["switches"] = "Network Equipment",
        ["network"] = "Network Equipment",
        ["ups"] = "UPS",
        ["furniture"] = "Furniture",
        ["chair"] = "Furniture",
        ["chairs"] = "Furniture",
        ["vehicle"] = "Vehicle",
        ["vehicles"] = "Vehicle",
        ["truck"] = "Vehicle",
        ["trucks"] = "Vehicle",
        ["car"] = "Vehicle",
        ["cars"] = "Vehicle",
    };

    /// <summary>Map a word ("laptops", "PCs") or category name to the registry's category, or null.</summary>
    public static string? CategoryFromWord(string word)
    {
        var w = word.Trim().ToLowerInvariant();
        if (w.Length == 0) return null;
        if (CategoryAliases.TryGetValue(w, out var category)) return category;
        return Categories.FirstOrDefault(c => c.ToLowerInvariant() == w);
    }
}
