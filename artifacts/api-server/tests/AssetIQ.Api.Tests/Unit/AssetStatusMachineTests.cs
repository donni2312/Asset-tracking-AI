using AssetIQ.Api.Domain;

namespace AssetIQ.Api.Tests.Unit;

/// <summary>
/// Phase 3: every allowed and every forbidden status transition (docs/implementation-plan.md §11.1), written out
/// here independently of the implementation, plus the guards of ST-02 and GI-07 and the finality of Disposed.
/// </summary>
public sealed class AssetStatusMachineTests
{
    private const string P = AssetStatuses.PendingAssetNumber, S = AssetStatuses.InStore, U = AssetStatuses.InUse, G = AssetStatuses.OutOnGatePass,
        Q = AssetStatuses.RepairRequested, R = AssetStatuses.InRepair, T = AssetStatuses.TransferPending, D = AssetStatuses.PendingDisposal, L = AssetStatuses.LostStolen, X = AssetStatuses.Disposed;

    /// <summary>The table, row by row: from → the statuses it may go to.</summary>
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        [P] = [S],
        [S] = [U, G, Q, R, T, D, L],
        [U] = [S, G, Q, R, T, D, L],
        [G] = [U, S, R, L],
        [Q] = [R, S, U],
        [R] = [S, U, G, D, L],
        [T] = [S, U],
        [D] = [S, U, X, L],
        [L] = [S, D],
        [X] = [],
    };

    public static TheoryData<string, string, bool> EveryPair()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var from in AssetStatuses.All)
            foreach (var to in AssetStatuses.All)
                if (from != to) data.Add(from, to, Allowed[from].Contains(to));
        return data;
    }

    /// <summary>Facts that satisfy every guard, so only the table decides.</summary>
    private static readonly AssetStatusMachine.Facts Ready = new(HasAssetNumber: true, Tagged: true, LostDate: "2026-10-20", Reporter: "Farai Ncube", Notes: "Taken from a car");

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Phase3_each_listed_transition_is_allowed_and_every_other_is_refused(string from, string to, bool allowed)
    {
        Assert.Equal(allowed, AssetStatusMachine.IsAllowed(from, to));
        var problem = AssetStatusMachine.Check(from, to, Ready);
        if (allowed) Assert.Null(problem);
        else Assert.NotNull(problem);
    }

    [Fact]
    public void Phase3_the_table_covers_all_ten_statuses_and_nothing_else()
    {
        Assert.Equal(10, AssetStatuses.All.Count); // Phase 4 added "repair requested"
        Assert.Equal(AssetStatuses.All.Order(), AssetStatusMachine.Transitions.Keys.Order());
        Assert.All(AssetStatusMachine.Transitions.Values.SelectMany(v => v.Keys), to => Assert.Contains(to, AssetStatuses.All));
    }

    [Fact]
    public void Phase3_disposed_is_final()
    {
        Assert.Empty(AssetStatusMachine.Next(X));
        foreach (var to in AssetStatuses.All.Where(s => s != X))
            Assert.Equal("A disposed asset is final; its status cannot change", AssetStatusMachine.Check(X, to, Ready));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void ST_02_an_item_cannot_be_issued_untagged_or_without_its_asset_number(bool numbered, bool tagged)
    {
        foreach (var from in new[] { S, G, Q, R, T, D })
        {
            Assert.Contains("(ST-02)", AssetStatusMachine.Check(from, U, Ready with { HasAssetNumber = numbered, Tagged = tagged }));
        }
        Assert.Null(AssetStatusMachine.Check(S, R, Ready with { HasAssetNumber = numbered, Tagged = tagged })); // other moves are fine
    }

    [Fact]
    public void SI_05_an_it_asset_goes_for_repair_only_through_finance_approval_and_a_non_it_asset_directly()
    {
        var it = Ready with { IsIt = true };
        foreach (var from in new[] { S, U, G })
            Assert.Equal("An IT asset goes for repair only once Finance approves the repair request (SI-05)", AssetStatusMachine.Check(from, R, it));
        Assert.Null(AssetStatusMachine.Check(Q, R, it));
        Assert.Null(AssetStatusMachine.Check(U, R, Ready)); // non-IT (e.g. a vehicle service)
    }

    [Fact]
    public void GI_07_lost_or_stolen_needs_a_date_a_reporter_and_notes()
    {
        Assert.Equal("Lost / stolen needs the date, who reported it, incident notes (GI-07)", AssetStatusMachine.Check(U, L, new(true, true)));
        Assert.Equal("Lost / stolen needs incident notes (GI-07)", AssetStatusMachine.Check(U, L, Ready with { Notes = "  " }));
        Assert.Null(AssetStatusMachine.Check(U, L, Ready));
        Assert.DoesNotContain(L, AssetStatusMachine.Active); // excluded from active counts
        Assert.DoesNotContain(X, AssetStatusMachine.Active);
    }

    [Fact]
    public void Phase3_pending_asset_number_only_leaves_for_in_store_and_a_status_cannot_change_to_itself()
    {
        Assert.Equal([S], AssetStatusMachine.Next(P));
        Assert.Equal("The asset is already In use", AssetStatusMachine.Check(U, U, Ready));
    }
}
