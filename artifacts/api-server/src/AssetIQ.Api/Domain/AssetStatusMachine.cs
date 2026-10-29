namespace AssetIQ.Api.Domain;

/// <summary>
/// Phase 3: the one state machine for asset status. Every status change (edits, lifecycle events, gate exits and
/// returns, Business Central numbers, the status endpoint) goes through <see cref="Check"/>; a change that is not
/// in <see cref="Transitions"/> is refused. The table is in docs/implementation-plan.md §11.1.
/// </summary>
public static class AssetStatusMachine
{
    /// <summary>from → (to → what the change is called). Anything missing is forbidden; Disposed is final.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Transitions =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            [AssetStatuses.PendingAssetNumber] = new Dictionary<string, string>
            {
                [AssetStatuses.InStore] = "asset number issued",
            },
            [AssetStatuses.InStore] = new Dictionary<string, string>
            {
                [AssetStatuses.InUse] = "issue",
                [AssetStatuses.OutOnGatePass] = "leave on a gate pass",
                [AssetStatuses.InRepair] = "send for repair",
                [AssetStatuses.TransferPending] = "request a transfer",
                [AssetStatuses.PendingDisposal] = "recommend for disposal",
                [AssetStatuses.LostStolen] = "report lost or stolen",
            },
            [AssetStatuses.InUse] = new Dictionary<string, string>
            {
                [AssetStatuses.InStore] = "return",
                [AssetStatuses.OutOnGatePass] = "leave on a gate pass",
                [AssetStatuses.InRepair] = "send for repair",
                [AssetStatuses.TransferPending] = "request a transfer",
                [AssetStatuses.PendingDisposal] = "recommend for disposal",
                [AssetStatuses.LostStolen] = "report lost or stolen",
            },
            [AssetStatuses.OutOnGatePass] = new Dictionary<string, string>
            {
                [AssetStatuses.InUse] = "return through the gate",
                [AssetStatuses.InStore] = "return through the gate",
                [AssetStatuses.InRepair] = "send for repair",
                [AssetStatuses.LostStolen] = "report lost or stolen",
            },
            [AssetStatuses.InRepair] = new Dictionary<string, string>
            {
                [AssetStatuses.InStore] = "return from repair",
                [AssetStatuses.InUse] = "return from repair",
                [AssetStatuses.OutOnGatePass] = "leave on a gate pass",
                [AssetStatuses.PendingDisposal] = "recommend for disposal",
                [AssetStatuses.LostStolen] = "report lost or stolen",
            },
            [AssetStatuses.TransferPending] = new Dictionary<string, string>
            {
                [AssetStatuses.InStore] = "decide the transfer",
                [AssetStatuses.InUse] = "decide the transfer",
            },
            [AssetStatuses.PendingDisposal] = new Dictionary<string, string>
            {
                [AssetStatuses.InStore] = "reject the disposal",
                [AssetStatuses.InUse] = "reject the disposal",
                [AssetStatuses.Disposed] = "approve the disposal",
            },
            [AssetStatuses.LostStolen] = new Dictionary<string, string>
            {
                [AssetStatuses.InStore] = "record as recovered",
                [AssetStatuses.PendingDisposal] = "write off",
            },
            [AssetStatuses.Disposed] = new Dictionary<string, string>(),
        };

    public static bool IsAllowed(string from, string to) => Transitions.TryGetValue(from, out var next) && next.ContainsKey(to);

    public static IReadOnlyCollection<string> Next(string from) =>
        Transitions.TryGetValue(from, out var next) ? next.Keys.ToList() : [];

    /// <summary>The facts the guards need about the asset and the change.</summary>
    public sealed record Facts(bool HasAssetNumber, bool Tagged, string? LostDate = null, string? Reporter = null, string? Notes = null);

    /// <summary>
    /// Null when <paramref name="from"/> → <paramref name="to"/> may happen, otherwise why not: not in the table,
    /// or a guard of the stories (ST-02 issue needs tag and number; GI-07 lost / stolen needs date, reporter, notes).
    /// </summary>
    public static string? Check(string from, string to, Facts facts)
    {
        if (from == to) return $"The asset is already {Label(to)}";
        if (from == AssetStatuses.Disposed) return "A disposed asset is final; its status cannot change";
        if (!IsAllowed(from, to)) return $"An asset cannot go from {Label(from)} to {Label(to)}";
        if (to == AssetStatuses.InUse && (!facts.HasAssetNumber || !facts.Tagged))
            return "An item cannot be issued while it is untagged or waiting for its asset number (ST-02)";
        if (to == AssetStatuses.LostStolen)
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(facts.LostDate)) missing.Add("the date");
            if (string.IsNullOrWhiteSpace(facts.Reporter)) missing.Add("who reported it");
            if (string.IsNullOrWhiteSpace(facts.Notes)) missing.Add("incident notes");
            if (missing.Count > 0) return $"Lost / stolen needs {string.Join(", ", missing)} (GI-07)";
        }
        return null;
    }

    /// <summary>Counted as active (dashboards, AI answers): not lost / stolen, disposed or waiting for disposal (GI-07).</summary>
    public static readonly IReadOnlyList<string> Active =
        [AssetStatuses.InStore, AssetStatuses.InUse, AssetStatuses.OutOnGatePass, AssetStatuses.InRepair, AssetStatuses.TransferPending];

    public static string Label(string status) => status switch
    {
        AssetStatuses.PendingAssetNumber => "Pending asset number",
        AssetStatuses.InStore => "In store",
        AssetStatuses.InUse => "In use",
        AssetStatuses.OutOnGatePass => "Out on gate pass",
        AssetStatuses.InRepair => "In repair",
        AssetStatuses.TransferPending => "Transfer pending",
        AssetStatuses.PendingDisposal => "Pending disposal",
        AssetStatuses.LostStolen => "Lost / stolen",
        AssetStatuses.Disposed => "Disposed",
        _ => status,
    };
}
