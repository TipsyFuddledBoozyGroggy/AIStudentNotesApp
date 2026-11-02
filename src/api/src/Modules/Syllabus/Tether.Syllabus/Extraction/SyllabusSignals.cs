using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Tether.Syllabus.Application;

namespace Tether.Syllabus.Extraction;

/// <summary>
/// SBV-10: the text matchers behind the context-based item flags and the confidence rule, in ONE place. The mock extractor
/// (<see cref="MockRuleExtractor"/>) uses them to propose its flags and confidence, and the orchestrator (<see cref="ServerFlags"/>) uses the
/// very same matchers to re-derive them from the stored, server-cut snippet and context, so an extractor can add a flag but never remove one.
/// </summary>
internal static class SyllabusSignals
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);
    internal const RegexOptions Opts = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    internal static readonly Regex MonthDay = new(
        @"\b(?<mon>Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sept?(?:ember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s+(?<day>\d{1,2})(?:st|nd|rd|th)?(?:\s*[-–]\s*(?<day2>\d{1,2})(?:st|nd|rd|th)?)?(?:\s*,\s*|\s+)?(?<year>(?:19|20|21)\d{2}\b)?",
        Opts, Timeout);
    internal static readonly Regex Numeric = new(@"(?<![\d/.-])(?<m>\d{1,2})/(?<d>\d{1,2})(?:/(?<y>\d{4}|\d{2}))?(?![\d/])", Opts, Timeout);
    internal static readonly Regex Iso = new(@"(?<![\d-])(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})(?![\d-])", Opts, Timeout);
    internal static readonly Regex Weekday = new(@"\b(?<wd>Mon|Tue(?:s)?|Wed(?:nes)?|Thu(?:r|rs)?|Fri|Sat(?:ur)?|Sun)(?:day)?s?\b\.?", Opts, Timeout);
    internal static readonly Regex Tentative = new(@"\b(tentative|subject to change|may (?:shift|change|move)|approximately|provisional)\b", Opts, Timeout);
    internal static readonly Regex Injection = new(
        @"(ignore (?:all |any )?(?:the )?(?:previous|prior|above|earlier) (?:instructions|prompts?)|disregard (?:all |any )?(?:the )?(?:previous|prior|above)|\bsystem\s*:|\byou are (?:now )?(?:a|an|the) |reveal (?:your |the )?(?:system )?prompt|</?\s*untrusted|developer message|new instructions\s*:)",
        Opts, Timeout);

    private static readonly string[] SupersessionCues =
    [
        "canceled", "cancelled", "cancellation", "postponed", "rescheduled", "reschedule", "moved", "delayed", "pushed back", "changed", "change of",
        "updated", "revised", "extended", "replaced", "instead", "formerly", "previously", "no longer", "is not", "will not be", "won't be",
        "not held", "not on", "unless", "except", "tbd", "tba", "to be announced", "subject to change",
    ];

    /// <summary>The flags that force <c>please_check</c> on a dated item (the DB check <c>ck_items_flags_force_check</c> enforces all but <c>dst_ambiguous</c>).</summary>
    internal static readonly IReadOnlySet<string> ForcingFlags = new HashSet<string>(StringComparer.Ordinal)
        { "tentative_language", "weekday_mismatch", "table_layout_risk", "outside_term", "injection_suspect", "supersession_cue", "low_text_quality", "dst_ambiguous" };

    public static bool HasSupersessionCue(string s)
    {
        string folded;
        try { folded = s.Normalize(NormalizationForm.FormKC).ToLowerInvariant(); } catch (ArgumentException) { folded = s.ToLowerInvariant(); }
        foreach (var cue in SupersessionCues)
            if (Regex.IsMatch(folded, @"(?<![a-z])" + Regex.Escape(cue) + @"(?![a-z])", RegexOptions.CultureInvariant, Timeout)) return true;
        return false;
    }

    public static bool IsLowQuality(string text)
    {
        if (text.Length < 40) return false;
        var good = 0;
        foreach (var ch in text) if (char.IsLetterOrDigit(ch) || ch is ' ' or '\n' or '.' or ',' or ':' or '-' or '/' or '(' or ')' or ';' or '\'' or '"' or '?' or '!' or '%' or '&' or '$' or '#') good++;
        return (double)good / text.Length < 0.7;
    }

    internal sealed record DateHit(int Index, int Length, int? Year, int? Month, int? Day, int? Day2, bool Ambiguous, bool Invalid, string Text, string Style);

    public static List<DateHit> FindDates(string line)
    {
        var hits = new List<DateHit>();
        foreach (Match m in Iso.Matches(line))
            hits.Add(new DateHit(m.Index, m.Length, int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture), null, false, false, m.Value.TrimEnd(), "iso"));
        foreach (Match m in MonthDay.Matches(line))
        {
            if (hits.Any(h => Overlaps(h, m))) continue;
            var yearText = m.Groups["year"].Success ? m.Groups["year"].Value : null;
            var span = m.Value.TrimEnd(' ', ',');
            hits.Add(new DateHit(m.Index, span.Length, yearText is null ? null : int.Parse(yearText, CultureInfo.InvariantCulture), MonthNumber(m.Groups["mon"].Value),
                int.Parse(m.Groups["day"].Value, CultureInfo.InvariantCulture), m.Groups["day2"].Success ? int.Parse(m.Groups["day2"].Value, CultureInfo.InvariantCulture) : null,
                false, false, span, "month"));
        }
        foreach (Match m in Numeric.Matches(line))
        {
            if (hits.Any(h => Overlaps(h, m))) continue;
            var a = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture); var b = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
            int? year = null;
            if (m.Groups["y"].Success) { var y = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture); year = y < 100 ? 2000 + y : y; }
            // month first is the default (design question 7); both readings possible and different is "ambiguous_format", never guessed
            var ambiguous = a is >= 1 and <= 12 && b is >= 1 and <= 12 && a != b;
            var invalid = !(a is >= 1 and <= 12) && !(b is >= 1 and <= 12) || (a > 12 && b > 12);
            hits.Add(new DateHit(m.Index, m.Length, year, a, b, null, ambiguous, invalid, m.Value, "numeric"));
        }
        return [.. hits.OrderBy(h => h.Index)];
    }

    private static bool Overlaps(DateHit h, Match m) => m.Index < h.Index + h.Length && h.Index < m.Index + m.Length;

    private static int MonthNumber(string s) => s.ToLowerInvariant()[..3] switch
    {
        "jan" => 1, "feb" => 2, "mar" => 3, "apr" => 4, "may" => 5, "jun" => 6, "jul" => 7, "aug" => 8, "sep" => 9, "oct" => 10, "nov" => 11, _ => 12,
    };

    /// <summary>The weekday written right before <paramref name="index"/> (at most 3 characters between them), as an RFC 5545 code, or null.</summary>
    public static string? WeekdayBefore(string line, int index)
    {
        var prefix = line[..index];
        var m = Weekday.Matches(prefix).LastOrDefault();
        if (m is null || prefix.Length - (m.Index + m.Length) > 3) return null;
        return WeekdayCode(m.Groups["wd"].Value);
    }

    public static string WeekdayCode(string wd) =>
        wd.ToLowerInvariant()[..2] switch { "mo" => "MO", "tu" => "TU", "we" => "WE", "th" => "TH", "fr" => "FR", "sa" => "SA", _ => "SU" };

    /// <summary>
    /// The month and day a date hit stands for, exactly as the mock reads it, or null when it is not one calendar day: an invalid or
    /// ambiguous numeric date, or a month or day out of range (so "14/9" is "ambiguous_format", never guessed day-first).
    /// </summary>
    public static (int Month, int Day)? MonthDayOf(DateHit hit)
    {
        if (hit.Invalid || hit.Ambiguous || hit.Month is not { } month || hit.Day is not { } day) return null;
        if (month is < 1 or > 12 || day is < 1 or > 31) return null;
        return (month, day);
    }

    /// <summary>
    /// The confidence rule (V12): a needs-date item is please_check; a dateless policy is probably_right; any forcing flag or hidden content
    /// in the file forces please_check; only an explicit full date looks right; anything else is probably right.
    /// </summary>
    public static (string Confidence, string Reason) Confidence(string whenStatus, string? unknownReason, string? derivation, IReadOnlyCollection<string> flags, bool hiddenContentDropped)
    {
        var forced = flags.Any(ForcingFlags.Contains) || hiddenContentDropped;
        if (whenStatus == "needs_date")
            return ("please_check", unknownReason switch
            {
                "tba" => "The date is to be announced.", "conflicting" => "The date and the weekday written do not agree.",
                "ambiguous_format" => "The date could be read two ways.", "date_not_verifiable" => "The date could not be matched to your term.",
                _ => "No full date is written for this.",
            });
        if (whenStatus == "not_applicable") return ("probably_right", "No date is written for this.");
        if (forced)
            return ("please_check", hiddenContentDropped ? "This file has hidden text, so please check each date."
                : flags.Contains("supersession_cue") ? "The wording around this date may mean it changed." : "One detail here is unclear. Please check.");
        if (derivation == "explicit_full") return ("looks_right", "The full date is written in your syllabus.");
        return ("probably_right", derivation == "year_from_term" ? "The year comes from your term dates." : "Worked out from your term dates.");
    }
}

/// <summary>
/// SBV-10 (docs/ai/syllabus-extraction-design.md section 17, condition 3): the orchestrator re-derives the context-based flags of every kept
/// item from the stored text, after <see cref="SyllabusLimits.Apply"/> has re-cut the quote and chosen the context (SBV-01, SBV-06), with the
/// same matchers the mock uses (<see cref="SyllabusSignals"/>):
/// <list type="bullet">
/// <item><c>supersession_cue</c>: a cue in the stored snippet or the server-chosen context (exactly the mock's test);</item>
/// <item><c>tentative_language</c>: tentative wording in the snippet or context (the mock tests the whole line, which is the context unless
/// the line is over 1,500 code points);</item>
/// <item><c>injection_suspect</c> and <c>low_text_quality</c>: document-level, from the whole canonical text and the intake flag, as the mock;</item>
/// <item><c>weekday_mismatch</c>: a resolved, non-recurring date whose written date in the quote has a weekday right before it that is not
/// the date's weekday (the mock refuses to resolve such a date at all);</item>
/// <item><c>outside_term</c>: a resolved date outside the student's term whose year was not written (V5; an explicit full date is the
/// source's word and is not flagged, as in the mock);</item>
/// <item><c>dst_ambiguous</c>: a resolved, non-recurring local time in a spring-forward gap of an allow-listed zone.</item>
/// </list>
/// The final flags are the extractor's (in its order, duplicates merged) followed by the server's that it left out, so an extractor can add
/// a flag but never remove one. Unknown flags are passed through untouched, so the database check still refuses them as before. The
/// confidence is then lowered, never raised, to what <see cref="SyllabusSignals.Confidence"/> gives for the merged set (V12: min of the model
/// and the server cap); when it is lowered, the reason is the rule's. An item the server has nothing to add to is returned as the same
/// instance, so the mock's output is stored byte-identical, and the step is idempotent.
/// </summary>
internal static class ServerFlags
{
    /// <summary>What the server reads from the whole canonical document, once per syllabus.</summary>
    internal sealed record DocumentSignals(bool InjectionSuspect, bool LowTextQuality, bool HiddenContentDropped);

    public static DocumentSignals Document(ExtractorInput input) =>
        new(input.InjectionSuspectFromText || SyllabusSignals.Injection.IsMatch(input.Text), SyllabusSignals.IsLowQuality(input.Text), input.HiddenContentDropped);

    public static ProposedItem Rederive(ProposedItem p, ExtractorInput input, DocumentSignals doc)
    {
        var derived = Derive(p, input, doc);
        var merged = new List<string>(p.Flags.Count + derived.Count);
        foreach (var f in p.Flags.Concat(derived)) if (!merged.Contains(f, StringComparer.Ordinal)) merged.Add(f);

        var (ruleConfidence, ruleReason) = SyllabusSignals.Confidence(p.WhenStatus, p.UnknownReason, p.Derivation, merged, doc.HiddenContentDropped);
        var lower = Rank(p.Confidence) is { } given && Rank(ruleConfidence) is { } rule && rule < given;   // an unknown value is left for the DB to refuse

        if (!lower && merged.SequenceEqual(p.Flags, StringComparer.Ordinal)) return p;
        return p with
        {
            Flags = merged,
            Confidence = lower ? ruleConfidence : p.Confidence,
            ConfidenceReason = lower ? ruleReason : p.ConfidenceReason,
        };
    }

    private static int? Rank(string confidence) => confidence switch { "please_check" => 0, "probably_right" => 1, "looks_right" => 2, _ => null };

    private static List<string> Derive(ProposedItem p, ExtractorInput input, DocumentSignals doc)
    {
        var s = p.Snippet;
        var flags = new List<string>();
        if (SyllabusSignals.Tentative.IsMatch(s.Text) || SyllabusSignals.Tentative.IsMatch(s.ContextText)) flags.Add("tentative_language");
        if (WeekdayMismatch(p)) flags.Add("weekday_mismatch");
        if (OutsideTerm(p, input)) flags.Add("outside_term");
        if (doc.InjectionSuspect) flags.Add("injection_suspect");                                   // the whole text was matched (it contains every context)
        if (SyllabusSignals.HasSupersessionCue(s.Text) || SyllabusSignals.HasSupersessionCue(s.ContextText)) flags.Add("supersession_cue");
        if (doc.LowTextQuality) flags.Add("low_text_quality");
        if (DstGap(p)) flags.Add("dst_ambiguous");
        return flags;
    }

    /// <summary>
    /// A resolved single date whose written form in the quote (a date in the context that overlaps the snippet and reads as the same month,
    /// day and, if written, year) has a weekday right before it that is not the date's weekday.
    /// </summary>
    private static bool WeekdayMismatch(ProposedItem p)
    {
        if (p.WhenStatus != "resolved" || p.Date is not { } d || p.Recurrence is not null) return false;
        var ctx = p.Snippet.ContextText;
        var from = p.Snippet.Start - p.Snippet.ContextStart; var to = p.Snippet.End - p.Snippet.ContextStart;
        var actual = Local.Weekday(d.DayOfWeek);
        foreach (var hit in SyllabusSignals.FindDates(ctx))
        {
            if (hit.Index >= to || hit.Index + hit.Length <= from) continue;                       // not part of the quote
            if (SyllabusSignals.MonthDayOf(hit) is not { } md || md != (d.Month, d.Day) || (hit.Year is { } y && y != d.Year)) continue;
            if (SyllabusSignals.WeekdayBefore(ctx, hit.Index) is { } written && written != actual) return true;
        }
        return false;
    }

    /// <summary>V5: a resolved date outside [termStart, termEnd] is only acceptable when the year is written (explicit_full).</summary>
    private static bool OutsideTerm(ProposedItem p, ExtractorInput input) =>
        p.WhenStatus == "resolved" && p.Date is { } d && p.Derivation != "explicit_full"
        && input.TermStart is { } ts && input.TermEnd is { } te && (d < ts || d > te);

    /// <summary>A resolved single local time that does not exist in its zone. Only allow-listed zones are tested (an unknown zone is not this check's business).</summary>
    private static bool DstGap(ProposedItem p) =>
        p.WhenStatus == "resolved" && p.Recurrence is null && !p.AllDay && p.Date is { } d && p.Time is { } t && ZoneAllowList.Contains(p.Timezone)
        && Local.IsInDstGap(d, t, p.Timezone!);
}
