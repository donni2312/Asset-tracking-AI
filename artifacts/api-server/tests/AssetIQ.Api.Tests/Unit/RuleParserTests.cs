using AssetIQ.Api.Services.NlQuery;

namespace AssetIQ.Api.Tests.Unit;

/// <summary>Ported from src/services/nl-query/rule-parser.test.ts.</summary>
public sealed class RuleParserTests
{
    [Fact]
    public void Where_is_the_dell_laptop_assigned_to_john_doe()
    {
        Assert.Equal(new LocateAssetIntent(["dell", "laptop"], "john doe"), RuleParser.ParseQuestion("Where is the Dell laptop assigned to John Doe?"));
    }

    [Fact]
    public void Which_assets_have_not_moved_in_30_days()
    {
        Assert.Equal(new NotMovedIntent(30), RuleParser.ParseQuestion("Which assets have not moved in 30 days?"));
    }

    [Fact]
    public void Show_me_all_gate_passes_approved_this_week()
    {
        Assert.Equal(new GatePassesIntent("approved", "this_week", null), RuleParser.ParseQuestion("Show me all gate passes approved this week"));
    }

    public static TheoryData<string, Intent> Variations => new()
    {
        { "Where's John Doe's laptop?", new LocateAssetIntent(["laptop"], "john doe") },
        { "Find the Epson projector", new LocateAssetIntent(["epson", "projector"], null) },
        { "What is assigned to Tendai Moyo?", new AssetsAssignedToIntent("tendai moyo") },
        { "Which assets haven't moved for 6 weeks?", new NotMovedIntent(42) },
        { "Show idle assets", new NotMovedIntent(30) },
        { "Which gate passes are pending?", new GatePassesIntent("pending", "all", null) },
        { "Gate passes rejected last week", new GatePassesIntent("rejected", "last_week", null) },
        { "gate-passes in the last 10 days", new GatePassesIntent(null, "last_n_days", 10) },
        { "Which assets are missing?", new AssetsByStatusIntent("lost_stolen") },
        { "What is in repair?", new AssetsByStatusIntent("in_repair") },
        { "How many laptops do we have?", new CategoryCountIntent("Laptop") },
        { "How many assets in total?", new CountSummaryIntent() },
        { "Show me unassigned assets", new UnassignedIntent() },
        { "Tell me a joke", new HelpIntent() },
        { "   ", new HelpIntent() },
    };

    [Theory]
    [MemberData(nameof(Variations))]
    public void Parses_variations(string question, Intent expected)
    {
        Assert.Equal(expected, RuleParser.ParseQuestion(question));
    }

    [Fact]
    public void Extracts_the_assignee_and_strips_it_from_the_remaining_text()
    {
        var (assignee, remainder) = RuleParser.ExtractAssignee("where is the dell laptop assigned to john doe currently");
        Assert.Equal("john doe", assignee);
        Assert.DoesNotContain("john", remainder);
    }

    [Fact]
    public void Does_not_treat_contractions_as_names()
    {
        Assert.Null(RuleParser.ExtractAssignee("where's the laptop").Assignee);
    }

    [Fact]
    public void Converts_durations_to_days()
    {
        Assert.Equal(30, RuleParser.ParseDays("not moved in 30 days"));
        Assert.Equal(60, RuleParser.ParseDays("not moved in 2 months"));
        Assert.Equal(7, RuleParser.ParseDays("not moved in a week"));
        Assert.Equal(30, RuleParser.ParseDays("not moved"));
    }

    [Fact]
    public void Recognises_reporting_periods()
    {
        Assert.Equal("today", RuleParser.ParsePeriod("approved today").Period);
        Assert.Equal("yesterday", RuleParser.ParsePeriod("approved yesterday").Period);
        Assert.Equal("this_month", RuleParser.ParsePeriod("approved this month").Period);
        Assert.Equal("all", RuleParser.ParsePeriod("approved ever").Period);
    }

    [Fact]
    public void Normalises_curly_quotes_punctuation_and_spacing()
    {
        Assert.Equal("where's john's laptop", RuleParser.NormaliseQuestion("  Where’s   “John’s”  laptop?! "));
    }
}
