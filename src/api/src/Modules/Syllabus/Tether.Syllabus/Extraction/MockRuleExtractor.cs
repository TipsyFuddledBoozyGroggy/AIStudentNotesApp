using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Tether.Syllabus.Application;
using Tether.Syllabus.Text;

namespace Tether.Syllabus.Extraction;

internal sealed record ExtractorInput(string Text, DateOnly? TermStart, DateOnly? TermEnd, string DefaultTimezone, bool HiddenContentDropped, bool InjectionSuspectFromText);
internal sealed record ProposedSnippet(int Start, int End, string Text, int ContextStart, int ContextEnd, string ContextText, string Precision);
internal sealed record ProposedRecurrence(string[] ByDay, TimeOnly Start, TimeOnly? End, DateOnly From, DateOnly Until, string UntilSource);
internal sealed record ProposedItem(
    string Kind, string Title, string WhenStatus, string? DateText, string? TimeText, int? Year, int? Month, int? Day, string? Weekday,
    DateOnly? Date, DateOnly? EndDate, TimeOnly? Time, TimeOnly? EndTime, bool AllDay, string? Timezone, string? TimezoneSource, string? Derivation,
    string? UnknownReason, IReadOnlyList<(DateOnly Date, TimeOnly? Time)> Alternatives, ProposedRecurrence? Recurrence, string Confidence,
    string ConfidenceReason, IReadOnlyList<string> Flags, ProposedSnippet Snippet);
internal sealed record ExtractorOutput(IReadOnlyList<ProposedItem> Items, IReadOnlyList<ProposedSnippet> Unclaimed, bool InjectionSuspect, bool LowTextQuality, int DroppedInvalid);

/// <summary>
/// The extraction seam of the Syllabus module (a LOCAL port; the generalised AI gateway seam comes later, docs/ai/syllabus-extraction-design.md).
/// The only implementation is the deterministic rule-based MOCK. It is extractive: every snippet is a verbatim slice of the canonical text
/// sliced by the server, and it cannot invent a date. It tests the pipeline, validators, UI states and persistence, not model quality.
/// The orchestrator does not rely on that (SBV-01): <see cref="SyllabusLimits.Apply"/> checks every snippet's offsets against the canonical
/// text, rejects (and counts) any whose text or context is not the slice at those offsets, and stores only the slices it cuts itself.
/// It also ignores the proposed context window and computes its own from the snippet offsets (SBV-06, <see cref="SyllabusLimits.ServerContext"/>).
/// </summary>
internal interface ISyllabusExtractor
{
    string Version { get; }
    string Model { get; }
    ExtractorOutput Extract(ExtractorInput input);
}

internal sealed class MockRuleExtractor : ISyllabusExtractor
{
    public const string ExtractorVersion = "mock-rules-1";
    public string Version => ExtractorVersion;
    public string Model => "mock";

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);
    private const RegexOptions Opts = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    private static readonly Regex MonthDay = new(
        @"\b(?<mon>Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sept?(?:ember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s+(?<day>\d{1,2})(?:st|nd|rd|th)?(?:\s*[-–]\s*(?<day2>\d{1,2})(?:st|nd|rd|th)?)?(?:\s*,\s*|\s+)?(?<year>(?:19|20|21)\d{2}\b)?",
        Opts, Timeout);
    private static readonly Regex Numeric = new(@"(?<![\d/.-])(?<m>\d{1,2})/(?<d>\d{1,2})(?:/(?<y>\d{4}|\d{2}))?(?![\d/])", Opts, Timeout);
    private static readonly Regex Iso = new(@"(?<![\d-])(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})(?![\d-])", Opts, Timeout);
    private static readonly Regex Clock = new(
        @"(?<![\d:])(?<h1>\d{1,2})(?::(?<m1>\d{2}))?\s*(?<a1>[ap]\.?m\.?)?\s*(?:[-–]\s*(?<h2>\d{1,2})(?::(?<m2>\d{2}))?\s*(?<a2>[ap]\.?m\.?)?)?(?![\d:])",
        Opts, Timeout);
    private static readonly Regex Weekday = new(@"\b(?<wd>Mon|Tue(?:s)?|Wed(?:nes)?|Thu(?:r|rs)?|Fri|Sat(?:ur)?|Sun)(?:day)?s?\b\.?", Opts, Timeout);
    private static readonly Regex CompactDays = new(@"\b(?<c>MWF|MTWRF|MW|WF|MF|TR|TTh|TuTh)\b", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Tba = new(@"\b(TBA|TBD|to be announced|to be determined)\b", Opts, Timeout);
    private static readonly Regex Tentative = new(@"\b(tentative|subject to change|may (?:shift|change|move)|approximately|provisional)\b", Opts, Timeout);
    private static readonly Regex Policy = new(@"\b(late (?:work|policy|submission)s?|attendance policy|academic integrity|honou?r code|grading policy|accommodations?)\b", Opts, Timeout);
    private static readonly Regex ExamK = new(@"\b(exam|midterm|final|quiz|test)\b", Opts, Timeout);
    private static readonly Regex AssignK = new(@"\b(due|assignment|homework|problem set|pset|ps\d+|project|paper|essay|lab report|lab)\b", Opts, Timeout);
    private static readonly Regex ReadK = new(@"\b(read|reading|chapter|chapters|ch\.)\b", Opts, Timeout);
    private static readonly Regex OfficeK = new(@"\boffice hours?\b", Opts, Timeout);
    private static readonly Regex MeetK = new(@"\b(lectures?|class meets|meets|meeting|section)\b", Opts, Timeout);
    private static readonly Regex NoClassK = new(@"\b(no class|holiday|break|recess|no lecture)\b", Opts, Timeout);
    private static readonly Regex InjectionRx = new(
        @"(ignore (?:all |any )?(?:the )?(?:previous|prior|above|earlier) (?:instructions|prompts?)|disregard (?:all |any )?(?:the )?(?:previous|prior|above)|\bsystem\s*:|\byou are (?:now )?(?:a|an|the) |reveal (?:your |the )?(?:system )?prompt|</?\s*untrusted|developer message|new instructions\s*:)",
        Opts, Timeout);

    private static readonly string[] SupersessionCues =
    [
        "canceled", "cancelled", "cancellation", "postponed", "rescheduled", "reschedule", "moved", "delayed", "pushed back", "changed", "change of",
        "updated", "revised", "extended", "replaced", "instead", "formerly", "previously", "no longer", "is not", "will not be", "won't be",
        "not held", "not on", "unless", "except", "tbd", "tba", "to be announced", "subject to change",
    ];

    public ExtractorOutput Extract(ExtractorInput input)
    {
        var text = input.Text;
        var lines = DocumentText.LineSpans(text);
        var items = new List<ProposedItem>();
        var unclaimed = new List<ProposedSnippet>();
        var dropped = 0;

        var injection = input.InjectionSuspectFromText || InjectionRx.IsMatch(text);
        var lowQuality = IsLowQuality(text);

        for (var li = 0; li < lines.Count; li++)
        {
            var (ls, le) = lines[li];
            var line = text.Substring(ls, le - ls);
            var dates = FindDates(line);
            var kind = Classify(line, hasDate: dates.Count > 0);

            // SBV-02: every proposal lost to the item or unclaimed cap is counted in droppedInvalid (the orchestrator clamps it to the wire maximum)
            if (kind == "policy" && dates.Count == 0)
            {
                if (items.Count < input.MaxAi()) items.Add(BuildPolicy(text, lines, li, injection, lowQuality, input));
                else dropped++;
                continue;
            }
            if (dates.Count == 0)
            {
                if (kind is not null && Tba.IsMatch(line))
                {
                    if (items.Count < input.MaxAi()) items.Add(BuildNeedsDate(text, lines, li, kind, "tba", injection, lowQuality, input));
                    else dropped++;
                }
                continue;
            }

            var first = dates[0];
            if (kind is null)
            {
                foreach (var d in dates) AddUnclaimed(unclaimed, text, lines, li, d, ref dropped);
                continue;
            }
            // extra dates on a claimed line stay visible as "dates we noticed"
            foreach (var d in dates.Skip(1)) AddUnclaimed(unclaimed, text, lines, li, d, ref dropped);
            if (items.Count >= input.MaxAi()) { dropped++; continue; }
            var rec = TryRecurrence(line, kind, first);
            items.Add(rec is not null
                ? BuildRecurring(text, lines, li, kind, rec, injection, lowQuality, input)
                : BuildDated(text, lines, li, kind, first, injection, lowQuality, input));
        }

        // recurring lines without any explicit date
        // (handled above only when a date exists; weekly patterns without a date are picked up here)
        for (var li = 0; li < lines.Count; li++)
        {
            var (ls, le) = lines[li];
            var line = text.Substring(ls, le - ls);
            if (FindDates(line).Count > 0) continue;
            var kind = Classify(line, hasDate: false);
            if (kind is not ("office_hours" or "other")) continue;
            var rec = TryRecurrence(line, kind, null);
            if (rec is null) continue;
            if (items.Count >= input.MaxAi()) { dropped++; continue; }
            items.Add(BuildRecurring(text, lines, li, kind, rec, injection, lowQuality, input));
        }

        return new ExtractorOutput(items, unclaimed, injection, lowQuality, dropped);
    }

    // ---- classification and parsing ----

    private static string? Classify(string line, bool hasDate)
    {
        if (OfficeK.IsMatch(line)) return "office_hours";
        if (NoClassK.IsMatch(line)) return "other";
        if (Policy.IsMatch(line) && !hasDate) return "policy";
        if (ExamK.IsMatch(line)) return "exam";
        if (AssignK.IsMatch(line)) return "assignment";
        if (ReadK.IsMatch(line)) return "reading";
        if (MeetK.IsMatch(line)) return "other";
        return null;
    }

    private sealed record DateHit(int Index, int Length, int? Year, int? Month, int? Day, int? Day2, bool Ambiguous, bool Invalid, string Text, string Style);

    private static List<DateHit> FindDates(string line)
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

    private static string? WeekdayBefore(string line, int index)
    {
        var prefix = line[..index];
        var m = Weekday.Matches(prefix).LastOrDefault();
        if (m is null || prefix.Length - (m.Index + m.Length) > 3) return null;
        return m.Groups["wd"].Value.ToLowerInvariant()[..2] switch { "mo" => "MO", "tu" => "TU", "we" => "WE", "th" => "TH", "fr" => "FR", "sa" => "SA", _ => "SU" };
    }

    private static (TimeOnly Start, TimeOnly? End, string Text)? FindTime(string line, int? afterIndex)
    {
        foreach (Match m in Clock.Matches(line))
        {
            var hasColon = m.Groups["m1"].Success; var ap1 = m.Groups["a1"].Value; var ap2 = m.Groups["a2"].Value;
            if (!hasColon && ap1.Length == 0 && ap2.Length == 0) continue;           // a bare number is not a time
            var h1 = int.Parse(m.Groups["h1"].Value, CultureInfo.InvariantCulture);
            var mi1 = hasColon ? int.Parse(m.Groups["m1"].Value, CultureInfo.InvariantCulture) : 0;
            var meridiem1 = ap1.Length > 0 ? ap1 : ap2;
            if (!ToHour(h1, meridiem1, out var hh1) || mi1 > 59) continue;
            TimeOnly? end = null;
            if (m.Groups["h2"].Success)
            {
                var h2 = int.Parse(m.Groups["h2"].Value, CultureInfo.InvariantCulture);
                var mi2 = m.Groups["m2"].Success ? int.Parse(m.Groups["m2"].Value, CultureInfo.InvariantCulture) : 0;
                var meridiem2 = ap2.Length > 0 ? ap2 : ap1;
                if (ToHour(h2, meridiem2, out var hh2) && mi2 <= 59) end = new TimeOnly(hh2, mi2);
            }
            return (new TimeOnly(hh1, mi1), end is { } e && e > new TimeOnly(hh1, mi1) ? e : null, m.Value.Trim());
        }
        return null;
    }

    private static bool ToHour(int h, string meridiem, out int hour)
    {
        hour = h;
        if (meridiem.Length == 0) return h is >= 0 and <= 23;
        if (h is < 1 or > 12) return false;
        var pm = char.ToLowerInvariant(meridiem[0]) == 'p';
        hour = pm ? (h == 12 ? 12 : h + 12) : (h == 12 ? 0 : h);
        return true;
    }

    private static ProposedRecurrence? TryRecurrence(string line, string kind, DateHit? date)
    {
        if (date is not null) return null;                                           // an explicit date is a single occurrence
        if (kind is not ("office_hours" or "other")) return null;
        var days = new SortedSet<int>();
        foreach (Match m in Weekday.Matches(line))
            days.Add(m.Groups["wd"].Value.ToLowerInvariant()[..2] switch { "mo" => 1, "tu" => 2, "we" => 3, "th" => 4, "fr" => 5, "sa" => 6, _ => 0 });
        foreach (Match m in CompactDays.Matches(line))
            foreach (var d in m.Groups["c"].Value switch
            {
                "MWF" => new[] { 1, 3, 5 }, "MTWRF" => new[] { 1, 2, 3, 4, 5 }, "MW" => new[] { 1, 3 }, "WF" => new[] { 3, 5 }, "MF" => new[] { 1, 5 },
                _ => new[] { 2, 4 },
            }) days.Add(d);
        if (days.Count == 0) return null;
        if (FindTime(line, null) is not { } t) return null;
        var codes = days.Select(d => Local.Weekday((DayOfWeek)d)).ToArray();
        // From/Until are filled in by the builder (they need the term); a placeholder keeps this method pure.
        return new ProposedRecurrence(codes, t.Start, t.End, default, default, "term_end");
    }

    private static bool IsLowQuality(string text)
    {
        if (text.Length < 40) return false;
        var good = 0;
        foreach (var ch in text) if (char.IsLetterOrDigit(ch) || ch is ' ' or '\n' or '.' or ',' or ':' or '-' or '/' or '(' or ')' or ';' or '\'' or '"' or '?' or '!' or '%' or '&' or '$' or '#') good++;
        return (double)good / text.Length < 0.7;
    }

    // ---- item builders ----

    private static string[] ForcingFlags => ["tentative_language", "weekday_mismatch", "table_layout_risk", "outside_term", "injection_suspect", "supersession_cue", "low_text_quality", "dst_ambiguous"];

    private static ProposedItem BuildDated(string text, List<(int Start, int End)> lines, int li, string kind, DateHit hit, bool injection, bool lowQuality, ExtractorInput input)
    {
        var (ls, le) = lines[li];
        var line = text.Substring(ls, le - ls);
        var flags = new List<string>();
        string? unknown = null; string? derivation = null;
        DateOnly? date = null; DateOnly? endDate = null;
        var alts = new List<(DateOnly, TimeOnly?)>();

        var weekday = WeekdayBefore(line, hit.Index);
        var time = FindTime(line, hit.Index + hit.Length);
        if (time is null && kind == "exam") flags.Add("time_missing");

        var month = hit.Month; var day = hit.Day;
        if (hit.Invalid) unknown = "ambiguous_format";
        else if (hit.Ambiguous) unknown = "ambiguous_format";
        else if (month is null or < 1 or > 12 || day is null or < 1 or > 31) unknown = "ambiguous_format";
        else
        {
            // a numeric date where only the day-first reading is valid (m > 12) is read day-first
            if (hit.Style == "numeric" && month > 12 && day <= 12) (month, day) = (day, month);
            int? year = hit.Year;
            if (year is not null)
            {
                if (year is < 2000 or > 2100 || day > DateTime.DaysInMonth(year.Value, month.Value)) unknown = "date_not_verifiable";
                else { date = new DateOnly(year.Value, month.Value, day.Value); derivation = "explicit_full"; }
            }
            else if (input.TermStart is { } ts && input.TermEnd is { } te)
            {
                var candidates = new List<DateOnly>();
                foreach (var y in new[] { ts.Year, te.Year }.Distinct())
                    if (y is >= 2000 and <= 2100 && day <= DateTime.DaysInMonth(y, month.Value))
                    {
                        var c = new DateOnly(y, month.Value, day.Value);
                        if (c >= ts && c <= te) candidates.Add(c);
                    }
                if (candidates.Count == 1) { date = candidates[0]; derivation = "year_from_term"; flags.Add("year_inferred"); }
                else { unknown = "date_not_verifiable"; flags.Add("outside_term"); }
            }
            else unknown = "not_stated";   // the year is not written and no term was supplied: never default to the current year

            if (date is { } d0 && weekday is not null && Local.Weekday(d0.DayOfWeek) != weekday)
            {
                alts.Add((d0, null)); date = null; derivation = null; unknown = "conflicting"; flags.Add("weekday_mismatch");
            }
            if (date is { } d1 && hit.Day2 is { } day2 && day2 > hit.Day && day2 <= DateTime.DaysInMonth(d1.Year, d1.Month))
                endDate = new DateOnly(d1.Year, d1.Month, day2);
        }

        if (date is null && unknown is null) unknown = "date_not_verifiable";
        var resolved = date is not null;
        if (!resolved) { time = null; endDate = null; }
        var allDay = resolved && time is null;
        string? tz = resolved ? input.DefaultTimezone : null;
        if (resolved && time is not null)
        {
            flags.Add("timezone_assumed");
            if (Local.IsInDstGap(date!.Value, time.Value.Start, input.DefaultTimezone)) flags.Add("dst_ambiguous");
        }
        return Finish(text, lines, li, kind, line, hit, weekday, time, resolved ? "resolved" : "needs_date", date, endDate, allDay, tz, resolved ? "course_default" : null,
            derivation, resolved ? null : unknown, alts, null, flags, injection, lowQuality, input);
    }

    private static ProposedItem BuildNeedsDate(string text, List<(int Start, int End)> lines, int li, string kind, string reason, bool injection, bool lowQuality, ExtractorInput input)
    {
        var (ls, le) = lines[li];
        var line = text.Substring(ls, le - ls);
        return Finish(text, lines, li, kind, line, null, null, null, "needs_date", null, null, false, null, null, null, reason, [], null, [], injection, lowQuality, input);
    }

    private static ProposedItem BuildPolicy(string text, List<(int Start, int End)> lines, int li, bool injection, bool lowQuality, ExtractorInput input)
    {
        var (ls, le) = lines[li];
        var line = text.Substring(ls, le - ls);
        return Finish(text, lines, li, "policy", line, null, null, null, "not_applicable", null, null, false, null, null, null, null, [], null, [], injection, lowQuality, input);
    }

    private static ProposedItem BuildRecurring(string text, List<(int Start, int End)> lines, int li, string kind, ProposedRecurrence rec, bool injection, bool lowQuality, ExtractorInput input)
    {
        var (ls, le) = lines[li];
        var line = text.Substring(ls, le - ls);
        var flags = new List<string>();
        if (input.TermStart is not { } ts || input.TermEnd is not { } te)
            return Finish(text, lines, li, kind, line, null, null, null, "needs_date", null, null, false, null, null, null, "not_stated", [], null, flags, injection, lowQuality, input);
        // first occurrence on or after the term start; until the term end (stated by the student, so flag until_from_term)
        var wanted = rec.ByDay.ToHashSet();
        var d = ts;
        while (!wanted.Contains(Local.Weekday(d.DayOfWeek)) && d <= te) d = d.AddDays(1);
        if (d > te) return Finish(text, lines, li, kind, line, null, null, null, "needs_date", null, null, false, null, null, null, "date_not_verifiable", [], null, flags, injection, lowQuality, input);
        flags.Add("until_from_term"); flags.Add("timezone_assumed");
        var tm = FindTime(line, null)!.Value;
        var full = rec with { From = d, Until = te, UntilSource = "term_end" };
        return Finish(text, lines, li, kind, line, null, null, (rec.Start, rec.End, tm.Text), "resolved", d, null, false, input.DefaultTimezone, "course_default",
            "year_from_term", null, [], full, flags, injection, lowQuality, input);
    }

    private static ProposedItem Finish(string text, List<(int Start, int End)> lines, int li, string kind, string line, DateHit? hit, string? weekday,
        (TimeOnly Start, TimeOnly? End, string Text)? time, string whenStatus, DateOnly? date, DateOnly? endDate, bool allDay, string? tz, string? tzSource,
        string? derivation, string? unknown, IReadOnlyList<(DateOnly, TimeOnly?)> alts, ProposedRecurrence? rec, List<string> flags, bool injection, bool lowQuality, ExtractorInput input)
    {
        var snippet = SliceSnippet(text, lines, li, hit);
        if (Tentative.IsMatch(line)) flags.Add("tentative_language");
        if (HasSupersessionCue(snippet.Text) || HasSupersessionCue(snippet.ContextText)) flags.Add("supersession_cue");
        if (injection) flags.Add("injection_suspect");
        if (lowQuality) flags.Add("low_text_quality");
        var distinct = flags.Distinct().ToList();

        string confidence; string reason;
        var forced = distinct.Any(f => ForcingFlags.Contains(f)) || input.HiddenContentDropped;
        if (whenStatus == "needs_date")
        {
            confidence = "please_check";
            reason = unknown switch
            {
                "tba" => "The date is to be announced.", "conflicting" => "The date and the weekday written do not agree.",
                "ambiguous_format" => "The date could be read two ways.", "date_not_verifiable" => "The date could not be matched to your term.",
                _ => "No full date is written for this.",
            };
        }
        else if (whenStatus == "not_applicable") { confidence = "probably_right"; reason = "No date is written for this."; }
        else if (forced) { confidence = "please_check"; reason = input.HiddenContentDropped ? "This file has hidden text, so please check each date." : distinct.Contains("supersession_cue") ? "The wording around this date may mean it changed." : "One detail here is unclear. Please check."; }
        else if (derivation == "explicit_full") { confidence = "looks_right"; reason = "The full date is written in your syllabus."; }
        else { confidence = "probably_right"; reason = derivation == "year_from_term" ? "The year comes from your term dates." : "Worked out from your term dates."; }

        var title = MakeTitle(line, kind, hit, time?.Text);
        return new ProposedItem(kind, title, whenStatus, hit?.Text is { } dt ? Truncate(dt, 100) : null, time?.Text is { Length: > 0 } tt ? Truncate(tt, 50) : null,
            hit?.Year, hit?.Month, hit?.Day, weekday, date, endDate, whenStatus == "resolved" && !allDay ? time?.Start : null,
            whenStatus == "resolved" && !allDay ? time?.End : null, allDay, tz, tzSource, derivation, unknown, alts, rec, confidence, reason, distinct, snippet);
    }

    private static string Truncate(string s, int cp) => TextPolicy.CodePoints(s) <= cp ? s : new string(s.EnumerateRunes().Take(cp).SelectMany(r => r.ToString()).ToArray());

    private static bool HasSupersessionCue(string s)
    {
        string folded;
        try { folded = s.Normalize(NormalizationForm.FormKC).ToLowerInvariant(); } catch (ArgumentException) { folded = s.ToLowerInvariant(); }
        foreach (var cue in SupersessionCues)
            if (Regex.IsMatch(folded, @"(?<![a-z])" + Regex.Escape(cue) + @"(?![a-z])", RegexOptions.CultureInvariant, Timeout)) return true;
        return false;
    }

    private static string MakeTitle(string line, string kind, DateHit? hit, string? timeText)
    {
        var t = line;
        if (hit is not null && hit.Index + hit.Length <= t.Length) t = t.Remove(hit.Index, hit.Length);
        if (!string.IsNullOrEmpty(timeText)) { var i = t.IndexOf(timeText, StringComparison.Ordinal); if (i >= 0) t = t.Remove(i, timeText.Length); }
        t = Weekday.Replace(t, " ");
        t = Regex.Replace(t, @"\s+", " ", RegexOptions.CultureInvariant, Timeout).Trim(' ', ':', '-', '–', ',', ';', '|', '.', '(', ')');
        if (TextPolicy.CodePoints(t) > 120) t = Truncate(t, 120).TrimEnd(' ', ':', '-', ',', ';');
        if (t.Length >= 2 && TextPolicy.TryValidate(t, TextKind.SingleLine, 200, out var ok)) return ok;
        return kind switch
        {
            "exam" => "Exam", "assignment" => "Assignment", "reading" => "Reading", "office_hours" => "Office hours", "policy" => "Course policy", _ => "Class event",
        };
    }

    private static ProposedSnippet SliceSnippet(string text, List<(int Start, int End)> lines, int li, DateHit? hit)
    {
        var (ls, le) = lines[li];
        var start = ls; var end = le;
        if (TextPolicy.CodePoints(text.AsSpan(start, end - start).ToString()) > 600)
        {
            // a very long line: a window of at most 600 code points around the date, cut on rune boundaries
            var center = hit is null ? ls : ls + hit.Index;
            start = Math.Max(ls, center - 250); end = Math.Min(le, start + 580);
            if (char.IsLowSurrogate(text[start]) && start > ls) start--;
            if (end < le && char.IsHighSurrogate(text[end - 1])) end--;
            while (start < end && text[start] == ' ') start++;
            while (end > start && text[end - 1] == ' ') end--;
        }
        var snippetText = text.Substring(start, end - start);
        // Context window: the full line around the snippet (up to 1,500 code points; a longer line is cut around the snippet). Matching of the
        // supersession cues runs over the snippet and this window, so neighbouring unrelated lines must not leak into it.
        var (cs, ce) = ContextWindow(text, ls, le, start, end, SyllabusLimits.ContextText);
        return new ProposedSnippet(start, end, snippetText, cs, ce, text.Substring(cs, ce - cs), "exact");
    }

    /// <summary>
    /// SB-02: the context window around [start, end) inside the line [ls, le), at most <paramref name="maxCodePoints"/> code points in total
    /// (the snippet included), so it always fits varchar(1500). It grows one code point at a time on each side (half the remaining budget each,
    /// any budget one side cannot use goes to the other), so it never splits a surrogate pair and its bounds stay UTF-16 offsets into the text.
    /// </summary>
    internal static (int Start, int End) ContextWindow(string text, int ls, int le, int start, int end, int maxCodePoints)
    {
        if (TextPolicy.CodePoints(text.Substring(ls, le - ls)) <= maxCodePoints) return (ls, le);
        var budget = maxCodePoints - TextPolicy.CodePoints(text.Substring(start, end - start));
        var cs = start; var ce = end;
        var left = budget / 2; var right = budget - left;
        int Back(int i) => i - (i - 2 >= ls && char.IsLowSurrogate(text[i - 1]) && char.IsHighSurrogate(text[i - 2]) ? 2 : 1);
        int Fwd(int i) => i + (i + 1 < le && char.IsHighSurrogate(text[i]) && char.IsLowSurrogate(text[i + 1]) ? 2 : 1);
        for (; left > 0 && cs > ls; left--) cs = Back(cs);
        for (right += left; right > 0 && ce < le; right--) ce = Fwd(ce);          // unused left budget moves right
        for (; right > 0 && cs > ls; right--) cs = Back(cs);                        // unused right budget moves back left
        while (cs < start && text[cs] == ' ') cs++;
        while (ce > end && text[ce - 1] == ' ') ce--;
        return (cs, ce);
    }

    /// <summary>Adds a "date we noticed" while there is room under <see cref="SyllabusLimits.MaxUnclaimed"/>; one beyond the cap is counted (SBV-02), not kept.</summary>
    private static void AddUnclaimed(List<ProposedSnippet> list, string text, List<(int Start, int End)> lines, int li, DateHit hit, ref int dropped)
    {
        var (ls, le) = lines[li];
        var start = ls + hit.Index; var end = start + hit.Length;
        if (end > le || end <= start) return;
        var s = text.Substring(start, end - start);
        if (s.Length == 0 || s[0] == ' ' || s[^1] == ' ') return;
        if (list.Count >= SyllabusLimits.MaxUnclaimed) { dropped++; return; }
        var cs = ls; var ce = le;
        if (TextPolicy.CodePoints(text.Substring(cs, ce - cs)) > 1500) { cs = start; ce = end; }
        list.Add(new ProposedSnippet(start, end, s, cs, ce, text.Substring(cs, ce - cs), "exact"));
    }
}

internal static class ExtractorInputExtensions
{
    public static int MaxAi(this ExtractorInput _) => 200;
}

/// <summary>
/// SB-02: the column limits of syllabus.items / unclaimed_dates in Unicode code points (PostgreSQL varchar(n) and char_length count code
/// points; docs/data/schema-syllabus-p2.md). The orchestrator re-checks every proposed field against them before insert, so an extractor that
/// overflows a column drops that one item (counted in droppedInvalid) instead of failing the whole syllabus with invalid_output.
/// </summary>
internal static class SyllabusLimits
{
    public const int Title = 200, DateText = 120, TimeText = 60, ConfidenceReason = 200, SnippetText = 600, ContextText = 1500;
    /// <summary>The span checks of ck_items_snippet, ck_items_context and ck_unclaimed_dates_* (offsets in the canonical text).</summary>
    public const int SnippetSpan = 1200, ContextSpan = 3000;
    /// <summary>The cap on stored unclaimed dates per syllabus (syllabus.v1 counts.unclaimedDates maximum).</summary>
    public const int MaxUnclaimed = 200;
    /// <summary>syllabus.v1 counts.droppedInvalid maximum.</summary>
    public const int MaxDroppedInvalid = 1000;

    private static bool Len(string? s, int max, bool required = false) => s is null ? !required : s.Length > 0 && TextPolicy.CodePoints(s) <= max;

    public static bool Fits(ProposedSnippet s) =>
        Len(s.Text, SnippetText, required: true) && Len(s.ContextText, ContextText, required: true)
        && s.Start >= 0 && s.End > s.Start && s.ContextStart >= 0 && s.ContextStart <= s.Start && s.End <= s.ContextEnd
        && s.End - s.Start <= SnippetSpan && s.ContextEnd - s.ContextStart <= ContextSpan && s.Precision is "exact" or "line";

    public static bool Fits(ProposedItem p) =>
        Len(p.Title, Title, required: true) && Len(p.DateText, DateText) && Len(p.TimeText, TimeText) && Len(p.ConfidenceReason, ConfidenceReason, required: true)
        && Fits(p.Snippet);

    /// <summary>What the length backstop keeps, and how many proposals it threw away.</summary>
    internal sealed record Kept(IReadOnlyList<ProposedItem> Items, IReadOnlyList<ProposedSnippet> Unclaimed, int DroppedInvalid);

    /// <summary>True when <paramref name="i"/> is a valid cut point of <paramref name="text"/>: inside [0, Length] and not between the two halves of a surrogate pair.</summary>
    private static bool IsBoundary(string text, int i) =>
        i >= 0 && i <= text.Length && !(i > 0 && i < text.Length && char.IsHighSurrogate(text[i - 1]) && char.IsLowSurrogate(text[i]));

    /// <summary>
    /// SBV-01: the snippet as the server cuts it from the canonical text. The extractor's offsets must fit the columns and spans
    /// (<see cref="Fits(ProposedSnippet)"/>), lie within the text, and fall on code-point boundaries; its <c>Text</c> and <c>ContextText</c>
    /// must equal (ordinal) the canonical slices at those offsets. A proposal that disagrees is rejected (the caller counts it): text that is
    /// not at its offsets is model text, never a quote of the student's syllabus. On success the returned record carries the slices cut here,
    /// so what is stored comes from the canonical text, not from the extractor. SBV-06: only the snippet offsets survive; the context offsets
    /// and text are replaced by <see cref="ServerContext"/>, so the extractor cannot choose how much of the line is shown around a quote.
    /// </summary>
    internal static bool TryCanonical(ProposedSnippet s, string canonical, out ProposedSnippet sliced)
    {
        sliced = s;
        if (!Fits(s)) return false;
        if (s.End > canonical.Length || s.ContextEnd > canonical.Length) return false;
        if (!IsBoundary(canonical, s.Start) || !IsBoundary(canonical, s.End) || !IsBoundary(canonical, s.ContextStart) || !IsBoundary(canonical, s.ContextEnd)) return false;
        var text = canonical.Substring(s.Start, s.End - s.Start);
        var context = canonical.Substring(s.ContextStart, s.ContextEnd - s.ContextStart);
        if (!string.Equals(text, s.Text, StringComparison.Ordinal) || !string.Equals(context, s.ContextText, StringComparison.Ordinal)) return false;
        // SBV-06: the extractor's (verified) context window is discarded; the server picks the window from the snippet offsets alone
        var (cs, ce) = ServerContext(canonical, s.Start, s.End);
        sliced = s with { Text = text, ContextStart = cs, ContextEnd = ce, ContextText = canonical.Substring(cs, ce - cs) };
        return Fits(sliced);                                                      // always true by construction; a defensive, counted drop otherwise
    }

    /// <summary>
    /// SBV-06: the context window of the snippet [<paramref name="start"/>, <paramref name="end"/>), chosen by the server from the canonical
    /// text and the snippet offsets only, never from the extractor, so a model cannot narrow a quote's context to hide a word ("The final is
    /// NOT on Dec 9" cut to "on Dec 9"). It is the whole canonical line (or run of lines, if the snippet crosses a line break) that contains
    /// the snippet. A line over <see cref="ContextText"/> code points gets the same window the mock uses for its items
    /// (<see cref="MockRuleExtractor.ContextWindow"/>: at most 1,500 code points around the snippet, never splitting a surrogate pair, so it
    /// also stays within <see cref="ContextSpan"/> UTF-16 units), whose edges are then moved inwards to grapheme-cluster boundaries where one
    /// exists between the window edge and the snippet. The result always contains the snippet and is an exact slice of the canonical text.
    /// For the mock's own output this equals the context it proposed, except an unclaimed date on a line over 1,500 code points, where the
    /// mock's snippet-only context is widened to the window.
    /// </summary>
    internal static (int Start, int End) ServerContext(string canonical, int start, int end)
    {
        var ls = start == 0 ? 0 : canonical.LastIndexOf('\n', start - 1) + 1;
        var nl = canonical.IndexOf('\n', end);
        var le = nl < 0 ? canonical.Length : nl;
        var (cs, ce) = MockRuleExtractor.ContextWindow(canonical, ls, le, start, end, ContextText);
        if (cs == ls && ce == le) return (cs, ce);                                // the whole line fits: nothing is cut

        // Snap the cut edges to grapheme-cluster boundaries (UAX #29, as .NET's StringInfo implements it). Segmentation restarts a short way
        // before the window instead of at the line start, so a 100,000-unit line costs O(window) per snippet; a line break is always a
        // boundary, and the canonical text keeps at most 4 marks per base, so the segmentation is back in step within that margin (a run of
        // more than 32 regional-indicator pairs right at the edge is the one case where a cut can still land between two flags' halves).
        var from = Math.Max(ls, cs - 64);
        if (from > ls && char.IsLowSurrogate(canonical[from]) && char.IsHighSurrogate(canonical[from - 1])) from--;
        int? snapStart = null; var snapEnd = -1;
        for (var b = from; ; )
        {
            if (snapStart is null && b >= cs && b <= start) snapStart = b;
            if (b >= end && b <= ce) snapEnd = b;
            if (b >= le || b > ce) break;
            b += Math.Max(1, System.Globalization.StringInfo.GetNextTextElementLength(canonical.AsSpan(b, le - b)));
        }
        if (snapStart is { } s0) cs = s0;
        if (snapEnd >= 0) ce = snapEnd;
        while (cs < start && canonical[cs] == ' ') cs++;
        while (ce > end && canonical[ce - 1] == ' ') ce--;
        return (cs, ce);
    }

    /// <summary>
    /// SB-02 / SBR-06 / SBV-01 / SBV-02: the orchestrator's backstop. Items and unclaimed dates that do not fit their columns, or whose snippet
    /// is not the canonical slice at its offsets (<see cref="TryCanonical"/>), are dropped, and EVERY drop is counted in <c>droppedInvalid</c>:
    /// items and unclaimed dates alike, the overflow beyond <paramref name="maxItems"/> and <see cref="MaxUnclaimed"/>, plus what the extractor
    /// itself dropped (clamped to <see cref="MaxDroppedInvalid"/>, the wire maximum). An item that is dropped for a field the model wrote (for
    /// example an oversize title steered by hostile syllabus text) but whose snippet is canonical is kept as an unclaimed date instead, so the
    /// student still sees that the syllabus mentions a date there: every kept snippet is re-cut here from <paramref name="canonical"/>, never
    /// model text. A policy item (no date by definition) is not kept that way. The demoted item still counts as dropped (it is not an item).
    /// Unclaimed dates stay within <see cref="MaxUnclaimed"/> (the extractor's own list first).
    /// </summary>
    public static Kept Apply(ExtractorOutput output, int maxItems, string canonical)
    {
        var proposed = output.Items.Take(maxItems).ToList();
        var items = new List<ProposedItem>(proposed.Count);
        var droppedItems = new List<(ProposedItem Item, ProposedSnippet? Trace)>();
        foreach (var p in proposed)
        {
            var canonicalSnippet = TryCanonical(p.Snippet, canonical, out var sn);
            if (canonicalSnippet && Fits(p)) items.Add(p with { Snippet = sn });
            else droppedItems.Add((p, canonicalSnippet ? sn : null));             // a forged or out-of-range snippet leaves no trace
        }

        var candidates = output.Unclaimed.Take(MaxUnclaimed).ToList();
        var unclaimed = new List<ProposedSnippet>(candidates.Count);
        foreach (var u in candidates) if (TryCanonical(u, canonical, out var su)) unclaimed.Add(su);
        var overflow = (long)Math.Max(0, output.Items.Count - proposed.Count) + Math.Max(0, output.Unclaimed.Count - candidates.Count);
        var dropped = (long)Math.Max(0, output.DroppedInvalid) + overflow + droppedItems.Count + (candidates.Count - unclaimed.Count);

        var taken = new HashSet<(int, int)>(unclaimed.Select(u => (u.Start, u.End)));
        foreach (var p in items) taken.Add((p.Snippet.Start, p.Snippet.End));      // the same span is already visible as an item
        foreach (var (p, trace) in droppedItems)
        {
            if (unclaimed.Count >= MaxUnclaimed) break;                            // no room: the item is lost, and it is already counted
            if (p.WhenStatus == "not_applicable" || trace is null || !taken.Add((trace.Start, trace.End))) continue;
            unclaimed.Add(trace);
        }
        return new Kept(items, unclaimed, (int)Math.Clamp(dropped, 0, MaxDroppedInvalid));
    }
}
