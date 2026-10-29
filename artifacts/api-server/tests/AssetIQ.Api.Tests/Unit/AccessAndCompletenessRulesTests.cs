using AssetIQ.Api.Auth;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Services;
using AssetIQ.Api.Services.Templates;

namespace AssetIQ.Api.Tests.Unit;

/// <summary>AU-11 completeness rules and the Phase 1 access rules (AccessPolicy).</summary>
public sealed class AccessAndCompletenessRulesTests
{
    private static readonly Template Furniture = new(7, "FF", "Furniture & Fittings", false, ["Desk"], "{SubCategory} – {Make} {Model}", null,
        [new(1, "make", "Make", "text", null, true, null), new(2, "model", "Model", "text", null, true, null), new(3, "room", "Room", "text", null, true, null)]);

    private static CompletenessFacts Complete() => new(Furniture, new HashSet<string> { "make", "model", "room" }, null, NoSerialStatuses.Approved,
        "TSF-FF-2021-00003", 1, "SBU Admin store", "good", 450m, "INV-1", "PO-1", 0);

    [Fact]
    public void AU_11_a_complete_record_has_no_gaps()
    {
        Assert.Empty(CompletenessRules.Gaps(Complete()));
    }

    [Fact]
    public void AU_11_lists_every_missing_priority_1_item_with_who_fixes_it()
    {
        var facts = Complete() with
        {
            FilledFieldKeys = new HashSet<string> { "make" }, NoSerialStatus = NoSerialStatuses.Pending, AssetNumber = null,
            OriginalCost = null, InvoiceNo = null, PoNo = " ", Condition = null, PendingUpgrades = 2,
        };
        var gaps = CompletenessRules.Gaps(facts);

        Assert.Equal(
            ["Description field missing: Model", "Description field missing: Room", "Condition missing", "\"No serial\" reason awaiting SBU Finance approval",
             "Asset number pending from Business Central", "Cost missing", "Invoice missing", "PO number missing", "2 upgrades awaiting Finance decision"],
            gaps.Select(g => g.Message));
        Assert.Equal("SBU Admin Manager", gaps[0].Owner);
        Assert.Equal(CompletenessRules.SbuFinance, gaps[3].Owner);
    }

    [Fact]
    public void AU_11_no_serial_without_any_reason_or_with_a_rejected_one_is_a_gap()
    {
        Assert.Contains(CompletenessRules.Gaps(Complete() with { NoSerialStatus = null }), g => g.Field == "serialNumber");
        Assert.Contains(CompletenessRules.Gaps(Complete() with { NoSerialStatus = NoSerialStatuses.Rejected }), g => g.Message.Contains("rejected"));
    }

    [Fact]
    public void AU_11_percent_complete_is_rounded_to_one_decimal()
    {
        Assert.Equal(94.1m, CompletenessService.Percent(16, 17));
        Assert.Equal(100m, CompletenessService.Percent(0, 0));
    }

    private static AuthUser U(string role, int? sbu = null) => new(1, role, role, role, sbu);

    // "custodian" is the employee holding the asset (null = not issued, i.e. in store until Phase 3).
    private static Asset A(int sbu, string status = AssetStatuses.Active, int? custodian = 7) =>
        new() { SbuId = sbu, Status = status, AssignedTo = custodian is null ? "Stores" : "Someone", CustodianEmployeeId = custodian };

    [Fact]
    public void Scope_sbu_roles_see_their_own_sbu_only_and_group_it_sees_it_assets_only()
    {
        Assert.True(AccessPolicy.CanView(U(Roles.SbuFinance, 1), A(1), isIt: false));
        Assert.False(AccessPolicy.CanView(U(Roles.SbuFinance, 1), A(2), isIt: false));
        Assert.True(AccessPolicy.CanView(U(Roles.SbuIt, 1), A(1), isIt: true));
        Assert.False(AccessPolicy.CanView(U(Roles.SbuIt, 1), A(1), isIt: false));
        Assert.True(AccessPolicy.CanView(U(Roles.SbuAdmin, 1), A(1), isIt: false));
        Assert.False(AccessPolicy.CanView(U(Roles.SbuAdmin, 1), A(1), isIt: true));
        Assert.True(AccessPolicy.CanView(U(Roles.GroupIt), A(5), isIt: true));
        Assert.False(AccessPolicy.CanView(U(Roles.GroupIt), A(5), isIt: false));
        Assert.True(AccessPolicy.CanView(U(Roles.GroupFinance), A(5), isIt: false));
        Assert.True(AccessPolicy.CanView(U(Roles.Auditor), A(5), isIt: true));
        Assert.True(AccessPolicy.CanView(U(Roles.Stores, 1), A(1, custodian: null), isIt: true));
        Assert.False(AccessPolicy.CanView(U(Roles.Stores, 1), A(1), isIt: true));
        Assert.False(AccessPolicy.CanView(U(Roles.SbuFinance, null), A(1), isIt: true)); // an SBU role without an SBU sees nothing
    }

    [Fact]
    public void AM_01_only_the_sbus_admin_manager_edits_non_it_assets_and_stores_may_also_register_them_at_receipt()
    {
        Assert.True(AccessPolicy.CanRegister(U(Roles.SbuAdmin, 1), isIt: false, sbuId: 1));
        Assert.False(AccessPolicy.CanRegister(U(Roles.SbuAdmin, 1), isIt: false, sbuId: 2));
        // Product owner, 29 Oct 2026: Stores registers non-IT items when they are received (matrix "Yes (at receipt)").
        Assert.True(AccessPolicy.CanRegister(U(Roles.Stores, 1), isIt: false, sbuId: 1));
        Assert.False(AccessPolicy.CanRegister(U(Roles.Stores, 1), isIt: false, sbuId: 2));
        Assert.False(AccessPolicy.CanEditTechnical(U(Roles.Stores, 1), isIt: false, sbuId: 1));
        foreach (var role in Roles.All.Except([Roles.SbuAdmin, Roles.Stores]))
        {
            Assert.False(AccessPolicy.CanRegister(U(role, 1), isIt: false, sbuId: 1), role);
            Assert.False(AccessPolicy.CanEditTechnical(U(role, 1), isIt: false, sbuId: 1), role);
        }
    }

    [Fact]
    public void AU_07_only_sbu_finance_of_the_assets_sbu_decides_and_auditors_change_nothing()
    {
        Assert.True(AccessPolicy.IsSbuFinanceOf(U(Roles.SbuFinance, 1), 1));
        Assert.False(AccessPolicy.IsSbuFinanceOf(U(Roles.SbuFinance, 2), 1));
        Assert.False(AccessPolicy.IsSbuFinanceOf(U(Roles.GroupFinance), 1));
        Assert.True(AccessPolicy.CanLogUpgrade(U(Roles.SbuIt, 1), isIt: true, sbuId: 1));
        Assert.True(AccessPolicy.CanLogUpgrade(U(Roles.Stores, 1), isIt: false, sbuId: 1));
        Assert.False(AccessPolicy.CanLogUpgrade(U(Roles.SbuIt, 1), isIt: false, sbuId: 1));
        Assert.False(AccessPolicy.CanLogUpgrade(U(Roles.SbuFinance, 1), isIt: true, sbuId: 1));

        var auditor = U(Roles.Auditor);
        Assert.False(AccessPolicy.CanRegister(auditor, true, 1));
        Assert.False(AccessPolicy.CanEditTechnical(auditor, true, 1));
        Assert.False(AccessPolicy.IsSbuFinanceOf(auditor, 1));
        Assert.False(AccessPolicy.CanRunDepreciation(auditor, null));
        Assert.False(AccessPolicy.CanManageCategories(auditor)); // and RoleHandler refuses every non-GET request of an auditor
    }

    [Fact]
    public void Templates_group_finance_sets_categories_group_it_the_it_fields_the_admin_manager_the_non_it_fields()
    {
        Assert.True(AccessPolicy.CanManageCategories(U(Roles.GroupFinance)));
        Assert.True(AccessPolicy.CanManageCategories(U(Roles.SystemAdmin))); // sets the final rates (product owner, 29 Oct 2026)
        Assert.False(AccessPolicy.CanManageCategories(U(Roles.GroupIt)));
        Assert.True(AccessPolicy.CanManageFields(U(Roles.GroupIt), isIt: true));
        Assert.False(AccessPolicy.CanManageFields(U(Roles.GroupIt), isIt: false));
        Assert.True(AccessPolicy.CanManageFields(U(Roles.SbuAdmin, 1), isIt: false));
        Assert.False(AccessPolicy.CanManageFields(U(Roles.GroupFinance), isIt: true));
    }
}
