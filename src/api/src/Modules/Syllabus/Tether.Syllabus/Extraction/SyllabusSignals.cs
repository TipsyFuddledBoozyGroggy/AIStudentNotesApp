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

    // SBV-14: the fixed vocabulary of confidenceReason (API_CONTRACTS_P2.md section 4, "confidence"). Copy reviewed in docs/ux/language-guide.md 8.3.
    internal const string ReasonFullDate = "The full date is written in your syllabus.";
    internal const string ReasonYearFromTerm = "The year comes from your term dates.";
    internal const string ReasonFromTerm = "Worked out from your term dates.";
    internal const string ReasonNoDate = "No date is written for this.";
    internal const string ReasonUnclear = "One detail here is unclear. Please check.";
    internal const string ReasonHidden = "This file has hidden text, so please check each date.";
    internal const string ReasonChanged = "The wording around this date may mean it changed.";
    internal const string ReasonTba = "The date is to be announced.";
    internal const string ReasonConflicting = "The date and the weekday written do not agree.";
    internal const string ReasonAmbiguous = "The date could be read two ways.";
    internal const string ReasonNotVerifiable = "The date could not be matched to your term.";
    internal const string ReasonNotStated = "No full date is written for this.";

    /// <summary>Every <c>confidenceReason</c> the server ever stores: the rule's reasons and the two set by item edits (<see cref="ItemRules"/>).</summary>
    internal static readonly IReadOnlySet<string> ReasonVocabulary = new HashSet<string>(StringComparer.Ordinal)
    {
        ReasonFullDate, ReasonYearFromTerm, ReasonFromTerm, ReasonNoDate, ReasonUnclear, ReasonHidden, ReasonChanged, ReasonTba, ReasonConflicting,
        ReasonAmbiguous, ReasonNotVerifiable, ReasonNotStated, ItemRules.StudentReason, ItemRules.EditedAiReason,
    };

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

    internal static readonly Regex CompactDays = new(@"\b(?<c>MWF|MTWRF|MW|WF|MF|TR|TTh|TuTh)\b", RegexOptions.CultureInvariant, Timeout);

    /// <summary>
    /// SBV-12: the weekdays a recurrence may be built from, as the mock reads them: every weekday name (singular or plural) and every compact
    /// code (MWF, TR, ...) written in [<paramref name="from"/>, <paramref name="to"/>) of <paramref name="text"/>, whole inside that span, as
    /// <see cref="DayOfWeek"/> numbers (0 = Sunday). The mock passes the whole line; the server passes the stored quote's span of its context.
    /// </summary>
    public static SortedSet<int> WrittenWeekdays(string text, int from, int to)
    {
        var days = new SortedSet<int>();
        foreach (Match m in Weekday.Matches(text))
            if (m.Index >= from && m.Index + m.Length <= to)
                days.Add(m.Groups["wd"].Value.ToLowerInvariant()[..2] switch { "mo" => 1, "tu" => 2, "we" => 3, "th" => 4, "fr" => 5, "sa" => 6, _ => 0 });
        foreach (Match m in CompactDays.Matches(text))
            if (m.Index >= from && m.Index + m.Length <= to)
                foreach (var d in m.Groups["c"].Value switch
                {
                    "MWF" => new[] { 1, 3, 5 }, "MTWRF" => new[] { 1, 2, 3, 4, 5 }, "MW" => new[] { 1, 3 }, "WF" => new[] { 3, 5 }, "MF" => new[] { 1, 5 },
                    _ => new[] { 2, 4 },
                }) days.Add(d);
        return days;
    }

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
    /// The flags syllabus-item.schema.json (allOf P2-08/P2-19) forces to <c>please_check</c> on ANY item, a dateless policy included (SBV-11).
    /// The web parser refuses an item that breaks this, and with it the whole page.
    /// </summary>
    internal static readonly IReadOnlySet<string> SchemaForcingFlags = new HashSet<string>(StringComparer.Ordinal)
        { "injection_suspect", "supersession_cue", "low_text_quality" };

    /// <summary>
    /// The confidence rule (V12): a needs-date item is please_check; a dateless policy is probably_right unless it carries one of the
    /// <see cref="SchemaForcingFlags"/> (SBV-11), then please_check; any forcing flag or hidden content in the file forces please_check on a
    /// dated item; only an explicit full date looks right; anything else is probably right.
    /// </summary>
    public static (string Confidence, string Reason) Confidence(string whenStatus, string? unknownReason, string? derivation, IReadOnlyCollection<string> flags, bool hiddenContentDropped)
    {
        var forced = flags.Any(ForcingFlags.Contains) || hiddenContentDropped;
        if (whenStatus == "needs_date")
            return ("please_check", unknownReason switch
            {
                "tba" => ReasonTba, "conflicting" => ReasonConflicting, "ambiguous_format" => ReasonAmbiguous, "date_not_verifiable" => ReasonNotVerifiable,
                _ => ReasonNotStated,
            });
        // a policy has no date, so the date-worded reasons ("this date", "each date") do not fit; hidden content alone does not force it (Lead ruling)
        if (whenStatus == "not_applicable") return flags.Any(SchemaForcingFlags.Contains) ? ("please_check", ReasonUnclear) : ("probably_right", ReasonNoDate);
        if (forced) return ("please_check", hiddenContentDropped ? ReasonHidden : flags.Contains("supersession_cue") ? ReasonChanged : ReasonUnclear);
        if (derivation == "explicit_full") return ("looks_right", ReasonFullDate);
        return ("probably_right", derivation == "year_from_term" ? ReasonYearFromTerm : ReasonFromTerm);
    }

    /// <summary>
    /// SBV-14: the reason stored for <paramref name="confidence"/>, always from the fixed vocabulary, never the extractor's text. When the
    /// stored confidence is the rule's, it is the rule's reason. When an extractor was more cautious than the rule (the stored confidence is
    /// the lower of the two, V12), it is <see cref="ReasonUnclear"/> for please_check, and for probably_right (the rule said looks_right, which
    /// only an explicit full date gets) <see cref="ReasonFullDate"/>, which states the evidence and not a certainty. An unknown confidence keeps
    /// the rule's reason (the database refuses the value anyway).
    /// </summary>
    public static string Reason(string confidence, string whenStatus, string? unknownReason, string? derivation, IReadOnlyCollection<string> flags, bool hiddenContentDropped)
    {
        var (rule, reason) = Confidence(whenStatus, unknownReason, derivation, flags, hiddenContentDropped);
        if (confidence == rule) return reason;
        return confidence switch { "please_check" => ReasonUnclear, "probably_right" when derivation == "explicit_full" => ReasonFullDate, _ => reason };
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
/// and the server cap). SBV-14: the reason is always set by the server (<see cref="SyllabusSignals.Reason"/>, fixed vocabulary) for the
/// stored confidence; the extractor's reason text is never stored. An item the server has nothing to change is returned as the same
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

        var (ruleConfidence, _) = SyllabusSignals.Confidence(p.WhenStatus, p.UnknownReason, p.Derivation, merged, doc.HiddenContentDropped);
        var lower = Rank(p.Confidence) is { } given && Rank(ruleConfidence) is { } rule && rule < given;   // an unknown value is left for the DB to refuse
        var confidence = lower ? ruleConfidence : p.Confidence;
        // SBV-14: the reason is always the server's, from the fixed vocabulary, for the confidence that is stored (never the extractor's text)
        var reason = SyllabusSignals.Reason(confidence, p.WhenStatus, p.UnknownReason, p.Derivation, merged, doc.HiddenContentDropped);
        var sameFlags = merged.SequenceEqual(p.Flags, StringComparer.Ordinal);

        if (!lower && sameFlags && string.Equals(reason, p.ConfidenceReason, StringComparison.Ordinal)) return p;
        return p with { Flags = sameFlags ? p.Flags : merged, Confidence = confidence, ConfidenceReason = reason };
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

/// <summary>
/// SBV-12 (docs/ai/syllabus-extraction-design.md section 17, condition 5): the server does not trust the extractor's RESOLVED date. An item's
/// date fields must be what is WRITTEN in its stored quote, read with the same matchers the mock uses (<see cref="SyllabusSignals.FindDates"/>,
/// <see cref="SyllabusSignals.MonthDayOf"/>, <see cref="SyllabusSignals.WrittenWeekdays"/>). It runs on the server-cut snippet and server-chosen
/// context (after SBV-01 and SBV-06), and before the flags are re-derived (SBV-10). A date counts as written in the quote only when the whole
/// written date (as the matcher reads it from the context) lies inside the snippet, so a quote cut to "Dec 1" out of "Dec 19, 2026", or to
/// "Dec 9" out of "Dec 9, 2026", does not support a date.
/// <list type="bullet">
/// <item>(a) a resolved single date: some date written in the quote reads as its month and day; when that written date has a year, it is the
/// date's year; when the item has an end date, the written date is a range whose second day is the end date's day in the same month (as the mock
/// builds a range);</item>
/// <item>(b) <c>explicit_full</c>: that written date has a year (four digits, or a numeric date's two-digit year read as 20yy, exactly as the
/// mock reads it, which also calls such a date <c>explicit_full</c>);</item>
/// <item>(c) an ambiguous numeric date ("3/4", "10/5") reads as no calendar day (<see cref="SyllabusSignals.MonthDayOf"/>), so it can never
/// support a resolved date; the contract's handling is <c>needs_date</c> / <c>ambiguous_format</c>;</item>
/// <item>(d) a recurrence: no date is written in the quote (a written date is a single occurrence, as in the mock), every weekday of
/// <c>byDay</c> is written in the quote (a weekday name, singular or plural, or a compact code such as MWF), and <c>when.date</c> is
/// <c>recurrence.from</c> and falls on one of those weekdays (API_CONTRACTS_P2.md section 4: <c>when.date</c> is the first occurrence).</item>
/// </list>
/// A resolved non-recurring date with any other derivation (<c>relative_to_anchor</c>) has no written date to match and fails closed. Items
/// that are not resolved, and items without a date, are not checked here (the database's <c>ck_items_needs_date_fields</c> holds the
/// needs_date shape). Lead ruling: an item that fails is an invalid model-written field, dropped and counted by
/// <see cref="SyllabusLimits.Apply"/>, and its verbatim snippet stays visible as an unclaimed date; the claimed date is never stored or corrected.
/// The mock's items always pass (it builds them from the same readings), so its output is stored byte-identical.
/// </summary>
internal static class ServerDates
{
    public static bool Consistent(ProposedItem p)
    {
        var s = p.Snippet;
        var ctx = s.ContextText;
        var from = s.Start - s.ContextStart; var to = s.End - s.ContextStart;
        if (from < 0 || to > ctx.Length || from >= to) return false;                              // not the server's snippet: refuse rather than guess

        if (p.Recurrence is { } rec) return RecurrenceWritten(p, rec, ctx, from, to);
        if (p.WhenStatus != "resolved" || p.Date is not { } date) return true;

        foreach (var hit in SyllabusSignals.FindDates(ctx))
        {
            if (hit.Index < from || hit.Index + hit.Length > to) continue;                        // not whole inside the quote
            if (SyllabusSignals.MonthDayOf(hit) is not { } md || md != (date.Month, date.Day)) continue;   // (a), (c)
            if (hit.Year is { } y ? y != date.Year : p.Derivation == "explicit_full") continue;   // (a) the written year; (b) explicit_full needs one
            if (p.EndDate is { } end && !(hit.Day2 is { } day2 && end.Year == date.Year && end.Month == date.Month && end.Day == day2)) continue;
            return true;
        }
        return false;
    }

    private static bool RecurrenceWritten(ProposedItem p, ProposedRecurrence rec, string ctx, int from, int to)
    {
        // a date written in (or reaching into) the quote makes it a single occurrence, never a weekly rule
        if (SyllabusSignals.FindDates(ctx).Any(h => h.Index < to && h.Index + h.Length > from)) return false;
        if (rec.ByDay is not { Length: > 0 } byDay) return false;
        var written = SyllabusSignals.WrittenWeekdays(ctx, from, to).Select(d => Local.Weekday((DayOfWeek)d)).ToHashSet(StringComparer.Ordinal);
        if (!byDay.All(written.Contains)) return false;
        if (p.Date is { } date && (date != rec.From || !byDay.Contains(Local.Weekday(date.DayOfWeek), StringComparer.Ordinal))) return false;
        return true;
    }
}
