using System.Globalization;
using System.Text.RegularExpressions;

namespace AssetIQ.Api.Services.NlQuery;

/// <summary>
/// Deterministic, rule-based question parser. It always works (no network, no API key) and is the
/// fallback whenever the OpenAI parser is disabled or fails. Covers the plan's three example
/// questions (section 6.5):
/// "Where is the Dell laptop assigned to John Doe?" → locate_asset;
/// "Which assets have not moved in 30 days?" → not_moved;
/// "Show me all gate passes approved this week" → gate_passes.
/// </summary>
public static class RuleParser
{
    // ECMAScript semantics: \b, \s and \d are ASCII-based, as in the original JavaScript patterns.
    private const RegexOptions Js = RegexOptions.ECMAScript;

    private static readonly HashSet<string> Stopwords =
    [
        "where", "is", "are", "was", "the", "a", "an", "my", "our", "his", "her", "their", "currently", "current",
        "located", "location", "locate", "find", "show", "me", "please", "can", "could", "you", "tell", "which", "what",
        "of", "for", "now", "right", "at", "in", "on", "to", "be", "does", "do", "whereabouts", "asset", "assets", "item",
        "items", "device", "devices", "i", "we", "it", "its", "that", "this", "there", "here", "and", "or", "with", "from",
        "by", "about", "any", "all", "whose", "has", "have", "been", "kept", "sitting", "lying", "gone",
    ];

    /// <summary>Words that end a person's name captured after "assigned to …".</summary>
    private static readonly HashSet<string> NameTerminators =
    [
        "in", "at", "on", "this", "that", "currently", "now", "today", "right", "please", "and", "or", "is", "was",
        "who", "which", "with", "from", "for", "located", "kept",
    ];

    private static readonly HashSet<string> DeviceWords = ["asset", "device", "computer", "machine", "equipment", "kit"];

    private static readonly Regex CurlyApostrophes = new("[‘’]");
    private static readonly Regex Punctuation = new("[“”\"?!.,;:()\\[\\]]");
    private static readonly Regex Whitespace = new(@"\s+");

    private static readonly Regex AssigneePhrase = new(
        @"\b(?:assigned to|issued to|allocated to|held by|belonging to|belongs to|used by)\s+([a-z][a-z'-]*(?:\s+[a-z][a-z'-]*){0,3})", Js);
    private static readonly Regex NameWord = new("^[a-z][a-z-]*$", Js);
    private static readonly Regex PossessiveSuffix = new("'s$", Js);
    private static readonly Regex EdgeApostrophes = new("^'+|'+$", Js);

    private static readonly Regex Duration = new(@"\b(\d{1,4})\s*(day|days|week|weeks|month|months)\b", Js);
    private static readonly Regex Fortnight = new(@"\b(?:a|one) fortnight\b|\btwo weeks\b", Js);
    private static readonly Regex OneWeek = new(@"\b(?:a|one) week\b", Js);
    private static readonly Regex OneMonth = new(@"\b(?:a|one) month\b", Js);

    private static readonly Regex Today = new(@"\btoday\b", Js);
    private static readonly Regex Yesterday = new(@"\byesterday\b", Js);
    private static readonly Regex ThisWeek = new(@"\b(?:this|the current) week\b", Js);
    private static readonly Regex LastWeek = new(@"\b(?:last|previous|past) week\b", Js);
    private static readonly Regex ThisMonth = new(@"\bthis month\b", Js);
    private static readonly Regex LastNDays = new(@"\b(?:last|past|previous)\s+(\d{1,3})\s+days?\b", Js);

    private static readonly Regex Approved = new(@"\bapproved\b|\bauthori[sz]ed\b", Js);
    private static readonly Regex Rejected = new(@"\brejected\b|\bdenied\b|\bdeclined\b", Js);
    private static readonly Regex Pending = new(@"\bpending\b|\bawaiting\b|\bwaiting\b|\boutstanding\b|\bunapproved\b", Js);

    private static readonly Regex GatePass = new(@"\bgate[\s-]?pass(?:es)?\b|\bpasses\b", Js);
    private static readonly Regex NotMoved = new(
        @"\b(?:not|never|hasn't|haven't|hasnt|havent)\s+(?:been\s+)?(?:moved|used|seen|scanned|touched)\b|\bidle\b|\bstationary\b|\bunmoved\b|\bno movements?\b|\bnot moving\b", Js);
    private static readonly Regex Locate = new(@"\bwhere\b|\blocate\b|\bfind\b|\bwhereabouts\b|\blocation of\b|\bwhere's\b", Js);
    private static readonly Regex Unassigned = new(@"\bunassigned\b|\bnot assigned\b|\bno one\b|\bnobody\b|\bwithout an? (?:owner|assignee)\b", Js);
    private static readonly Regex Count = new(@"\bhow many\b|\btotal\b|\bcount\b|\bnumber of\b|\bsummary\b|\boverview\b", Js);
    private static readonly Regex Missing = new(@"\bmissing\b|\blost\b|\bstolen\b", Js);
    private static readonly Regex Maintenance = new(@"\bmaintenance\b|\brepairs?\b|\bbeing (?:fixed|repaired)\b", Js);
    private static readonly Regex Retired = new(@"\bretired\b|\bdisposed\b|\bdecommissioned\b", Js);

    public static string NormaliseQuestion(string question)
    {
        var q = CurlyApostrophes.Replace(question.ToLowerInvariant(), "'");
        q = Punctuation.Replace(q, " ");
        return Whitespace.Replace(q, " ").Trim();
    }

    /// <summary>"assigned to John Doe", "held by …", or a possessive such as "John Doe's laptop".</summary>
    public static (string? Assignee, string Remainder) ExtractAssignee(string q)
    {
        var match = AssigneePhrase.Match(q);
        if (match.Success)
        {
            var name = new List<string>();
            foreach (var word in match.Groups[1].Value.Split(' '))
            {
                if (NameTerminators.Contains(word)) break;
                name.Add(word);
            }
            if (name.Count > 0) return (string.Join(' ', name), ReplaceFirst(q, match.Value, " "));
        }

        // Possessive: "<first> <last>'s <device>". Scan tokens so "where's john doe's laptop" works
        // (a single pattern match would stop at "where's john").
        var tokens = q.Split(' ');
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            var next = tokens[i + 1];
            if (!tokens[i].EndsWith("'s", StringComparison.Ordinal) ||
                !(Vocabulary.CategoryFromWord(next) is not null || DeviceWords.Contains(next)))
            {
                continue;
            }
            var surname = tokens[i][..^2];
            var previous = i > 0 ? tokens[i - 1] : "";
            var nameWords = new[] { previous, surname }.Where(w => NameWord.IsMatch(w) && !Stopwords.Contains(w)).ToList();
            if (nameWords.Count == 0) continue;
            var start = nameWords.Count == 2 ? i - 1 : i;
            var rest = string.Join(' ', tokens.Take(start).Concat(tokens.Skip(i + 1)));
            return (string.Join(' ', nameWords), rest);
        }
        return (null, q);
    }

    /// <summary>Significant words left after removing stop words (brand, model, category…).</summary>
    public static List<string> ExtractTerms(string text) =>
        text.Split(' ')
            .Select(w => EdgeApostrophes.Replace(PossessiveSuffix.Replace(w, ""), ""))
            .Where(w => w.Length > 1 && !Stopwords.Contains(w))
            .ToList();

    /// <summary>"30 days", "4 weeks", "2 months", "a week" → days.</summary>
    public static int ParseDays(string q, int fallback = 30)
    {
        var match = Duration.Match(q);
        if (match.Success)
        {
            var n = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var unit = match.Groups[2].Value;
            var factor = unit.StartsWith("week", StringComparison.Ordinal) ? 7 : unit.StartsWith("month", StringComparison.Ordinal) ? 30 : 1;
            return Math.Max(1, n * factor);
        }
        if (Fortnight.IsMatch(q)) return 14;
        if (OneWeek.IsMatch(q)) return 7;
        if (OneMonth.IsMatch(q)) return 30;
        return fallback;
    }

    public static (string Period, int? PeriodDays) ParsePeriod(string q)
    {
        if (Today.IsMatch(q)) return (Periods.Today, null);
        if (Yesterday.IsMatch(q)) return (Periods.Yesterday, null);
        if (ThisWeek.IsMatch(q)) return (Periods.ThisWeek, null);
        if (LastWeek.IsMatch(q)) return (Periods.LastWeek, null);
        if (ThisMonth.IsMatch(q)) return (Periods.ThisMonth, null);
        var lastN = LastNDays.Match(q);
        if (lastN.Success) return (Periods.LastNDays, Math.Max(1, int.Parse(lastN.Groups[1].Value, CultureInfo.InvariantCulture)));
        return (Periods.AllTime, null);
    }

    private static string? GatePassStatus(string q)
    {
        if (Approved.IsMatch(q)) return Domain.GatePassStatuses.Approved;
        if (Rejected.IsMatch(q)) return Domain.GatePassStatuses.Rejected;
        if (Pending.IsMatch(q)) return Domain.GatePassStatuses.Pending;
        return null;
    }

    public static Intent ParseQuestion(string question)
    {
        var q = NormaliseQuestion(question);
        if (q.Length == 0) return new HelpIntent();

        if (GatePass.IsMatch(q))
        {
            var (period, periodDays) = ParsePeriod(q);
            return new GatePassesIntent(GatePassStatus(q), period, periodDays);
        }

        if (NotMoved.IsMatch(q)) return new NotMovedIntent(ParseDays(q));

        if (Unassigned.IsMatch(q)) return new UnassignedIntent();

        var (assignee, rest) = ExtractAssignee(q);

        if (Locate.IsMatch(q)) return new LocateAssetIntent(ExtractTerms(rest), assignee);

        if (assignee is not null) return new AssetsAssignedToIntent(assignee);

        if (Missing.IsMatch(q)) return new AssetsByStatusIntent(Domain.AssetStatuses.Missing);
        if (Maintenance.IsMatch(q)) return new AssetsByStatusIntent(Domain.AssetStatuses.Maintenance);
        if (Retired.IsMatch(q)) return new AssetsByStatusIntent(Domain.AssetStatuses.Retired);

        var category = q.Split(' ').Select(Vocabulary.CategoryFromWord).FirstOrDefault(c => c is not null);
        if (category is not null) return new CategoryCountIntent(category);

        if (Count.IsMatch(q)) return new CountSummaryIntent();

        return new HelpIntent();
    }

    private static string ReplaceFirst(string text, string search, string replacement)
    {
        var index = text.IndexOf(search, StringComparison.Ordinal);
        return index < 0 ? text : string.Concat(text.AsSpan(0, index), replacement, text.AsSpan(index + search.Length));
    }
}
