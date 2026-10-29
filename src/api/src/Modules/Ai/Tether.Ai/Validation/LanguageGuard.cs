using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Tether.Ai.Validation;

/// <summary>One student-visible string the model wrote, plus the cited source text it may legitimately quote (null when none).</summary>
internal sealed record GuardText(string? Text, string? Source = null);

/// <summary>Rule ids and match counts only. Never the text (CLAUDE.md invariant 10).</summary>
internal sealed record LanguageScan(IReadOnlyDictionary<string, int> Hits, bool Incomplete)
{
    public bool Clean => Hits.Count == 0 && !Incomplete;
    /// <summary>Safe to log: <c>rule=count</c> pairs in a stable order, plus <c>incomplete</c> when scanning could not finish.</summary>
    public string Summary => string.Join(',', Hits.OrderBy(h => h.Key, StringComparer.Ordinal).Select(h => $"{h.Key}={h.Value}").Concat(Incomplete ? ["incomplete"] : []));
}

/// <summary>
/// Post-generation check of student-facing wording (docs/ux/language-guide.md section 5.2). A regex FLOOR: it catches the obvious
/// forms and misses paraphrase ("you may have drifted off"), so it complements, and never replaces, the prompt clause and human review.
/// It runs only on text the model wrote, never on student input. A hit that also appears verbatim in the cited source is quoted
/// lecture content and is exempt (a psychology lecture may say "ADHD", a lecturer may say "don't forget"). Default mode is LOG-ONLY.
/// Work is bounded: every regex has a timeout, scanned text is capped, and any failure is reported as "incomplete" (enforce mode then
/// rejects, log-only mode passes). Logs carry rule ids and counts only.
/// </summary>
internal static partial class LanguageGuard
{
    private const int TimeoutMs = 100;
    public const int MaxScanChars = 20_000;

    public const string Blame = "blame.second_person", LostTrack = "blame.lost_track", Deficit = "blame.deficit", Clinical = "clinical",
        Urgency = "pressure.urgency", Deadline = "pressure.deadline", Exclaim = "tone.exclaim", Emoji = "tone.emoji", Filler = "tone.filler",
        Certainty = "certainty", OldName = "name.old", CatchUp = "name.catch_up";

    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex(@"\byou(?:'ve|'d|\s+have|\s+had)?\s+(?:missed|forgot|forgotten|lost|fell\s+behind|failed|didn't\s+(?:get|understand|notice|pay))\b", Opts, TimeoutMs)] private static partial Regex BlameRx();
    [GeneratedRegex(@"\b(?:lost\s+track|lose\s+track|lost\s+the\s+thread|lose\s+the\s+thread|zon(?:ed|ing)\s+out|spaced\s+out|not\s+paying\s+attention|fell\s+behind|falling\s+behind)\b", Opts, TimeoutMs)] private static partial Regex LostTrackRx();
    [GeneratedRegex(@"\b(?:your|the\s+student's)\s+(?:weakness(?:es)?|deficits?|struggles?|struggling)\b|\bstruggl(?:e|es|ing)\s+(?:with|to)\b", Opts, TimeoutMs)] private static partial Regex DeficitRx();
    // "ADD" is matched case-sensitively only: lowercase "add" is an everyday verb.
    [GeneratedRegex(@"\b(?:adhd|attention\s+deficit|executive\s+dysfunction|symptoms?|diagnos\w*|disorders?|medications?|inattentive|hyperactiv\w*)\b|(?-i:\bADD\b)", Opts, TimeoutMs)] private static partial Regex ClinicalRx();
    [GeneratedRegex(@"\b(?:hurry|urgent\w*|asap|immediately|right\s+now|last\s+chance|don't\s+forget|make\s+sure|you\s+(?:must|need\s+to|have\s+to|should))\b", Opts, TimeoutMs)] private static partial Regex UrgencyRx();
    [GeneratedRegex(@"\b(?:overdue|running\s+out\s+of\s+time|only\s+\d+\s+(?:days?|minutes?|hours?)\s+left|deadline\s+looming)\b", Opts, TimeoutMs)] private static partial Regex DeadlineRx();
    [GeneratedRegex(@"\b(?:oops|awesome|great\s+job|well\s+done|amazing|don't\s+worry|no\s+worries|simply|just\s+(?:do|read|try)|obviously|clearly|easy)\b", Opts, TimeoutMs)] private static partial Regex FillerRx();
    [GeneratedRegex(@"\b(?:definitely|certainly|guaranteed|the\s+correct\s+answer\s+is|you\s+are\s+(?:wrong|incorrect|right))\b", Opts, TimeoutMs)] private static partial Regex CertaintyRx();
    [GeneratedRegex(@"\bI\s+lost\s+track\b", Opts, TimeoutMs)] private static partial Regex OldNameRx();
    [GeneratedRegex(@"\bcatch(?:es|ing)?[\s-]?up\b", Opts, TimeoutMs)] private static partial Regex CatchUpRx();

    private static readonly (string Id, Func<Regex> Rx)[] Rules =
    [
        (Blame, BlameRx), (LostTrack, LostTrackRx), (Deficit, DeficitRx), (Clinical, ClinicalRx), (Urgency, UrgencyRx), (Deadline, DeadlineRx),
        (Filler, FillerRx), (Certainty, CertaintyRx), (OldName, OldNameRx), (CatchUp, CatchUpRx),
    ];

    public static LanguageScan ScanText(string? text, string? source) => Scan([new GuardText(text, source)]);

    public static LanguageScan Scan(IEnumerable<GuardText> texts)
    {
        var hits = new Dictionary<string, int>(StringComparer.Ordinal);
        var incomplete = false;
        try
        {
            foreach (var gt in texts)
            {
                if (string.IsNullOrEmpty(gt.Text)) continue;
                var t = gt.Text;
                if (t.Length > MaxScanChars) { t = t[..MaxScanChars]; incomplete = true; }
                t = t.Replace('\u2019', '\'').Replace('\u2018', '\'');
                var src = gt.Source is null ? null : gt.Source.Replace('\u2019', '\'').Replace('\u2018', '\'');
                try
                {
                    foreach (var (id, rx) in Rules)
                        foreach (Match m in rx().Matches(t))
                            if (!Quoted(src, m.Value)) Add(hits, id);
                    CountExclamations(t, src, hits);
                    CountEmoji(t, src, hits);
                }
                catch (RegexMatchTimeoutException) { incomplete = true; }
            }
        }
        catch (Exception) { incomplete = true; } // fail-safe: a scanner fault is "incomplete", never an exception into the pipeline
        return new LanguageScan(hits, incomplete);
    }

    private static void Add(Dictionary<string, int> hits, string id) => hits[id] = hits.GetValueOrDefault(id) + 1;

    /// <summary>True when the matched phrase also appears verbatim (any case) in the cited source: quoted lecture content.</summary>
    private static bool Quoted(string? source, string match) => !string.IsNullOrEmpty(source) && source.Contains(NormalizeSpaces(match), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSpaces(string s) => Regex.Replace(s, @"\s+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(TimeoutMs));

    /// <summary>"!" outside notation. Exempt: after a digit, ")" or "]", after a lone letter (n!), before "=" (!=), or the same word! in the source.</summary>
    private static void CountExclamations(string t, string? src, Dictionary<string, int> hits)
    {
        for (var i = 0; i < t.Length; i++)
        {
            if (t[i] != '!') continue;
            if (i + 1 < t.Length && t[i + 1] == '=') continue;
            if (i > 0)
            {
                var p = t[i - 1];
                if (char.IsDigit(p) || p is ')' or ']') continue;
                if (char.IsLetter(p) && (i == 1 || !char.IsLetterOrDigit(t[i - 2]))) continue; // n!, k!
            }
            var s = i;
            while (s > 0 && char.IsLetter(t[s - 1])) s--;
            if (s < i && Quoted(src, t[s..(i + 1)])) continue;
            Add(hits, Exclaim);
        }
    }

    private static void CountEmoji(string t, string? src, Dictionary<string, int> hits)
    {
        foreach (var r in t.EnumerateRunes())
        {
            var v = r.Value;
            var emoji = v is >= 0x1F300 and <= 0x1FAFF or >= 0x2600 and <= 0x27BF or >= 0x1F000 and <= 0x1F2FF or 0x2B50 or 0x2B55;
            if (emoji && !Quoted(src, r.ToString())) Add(hits, Emoji);
        }
    }

    // ---------------- text extraction: exactly the strings a student can read ----------------

    private static string All(IReadOnlyDictionary<string, Provider.SourceLine> allowed) => string.Join('\n', allowed.Values.Select(l => l.Text));

    private static string? Cited(IReadOnlyDictionary<string, Provider.SourceLine> allowed, IReadOnlyList<CitationRef> cites) =>
        cites.Count == 0 ? null : string.Join('\n', cites.Select(c => allowed.TryGetValue(c.SegmentId, out var l) ? l.Text : ""));

    public static IEnumerable<GuardText> Texts(SummaryDelta d, SummaryContext ctx)
    {
        var all = All(ctx.Allowed);
        yield return new GuardText(d.Tldr, all);
        foreach (var t in d.Topics)
        {
            var src = Cited(ctx.Allowed, t.Citations);
            yield return new GuardText(t.Key, src);
            yield return new GuardText(t.Text, src);
        }
        foreach (var term in d.NewTerms) yield return new GuardText(term, all);
    }

    public static IEnumerable<GuardText> Texts(CatchUpResult r, CatchUpContext ctx)
    {
        var all = All(ctx.Allowed);
        GuardText C(Claim c) => new(c.Text, Cited(ctx.Allowed, c.Citations));
        yield return C(r.NowDiscussing);
        foreach (var c in r.Short) yield return C(c);
        foreach (var d in r.Detail)
        {
            yield return C(d.Claim);
            if (d.WhyItMatters is not null) yield return new GuardText(d.WhyItMatters, Cited(ctx.Allowed, d.Claim.Citations));
        }
        foreach (var k in r.KeyTerms)
        {
            var src = Cited(ctx.Allowed, k.Citations);
            yield return new GuardText(k.Term, src);
            if (k.Definition is not null) yield return new GuardText(k.Definition, src);
        }
        foreach (var q in r.Questions) yield return new GuardText(q, all);
        foreach (var g in r.Gaps) yield return new GuardText(g, all);
    }

    // ---------------- pipeline gate ----------------

    /// <summary>
    /// Runs after schema validation and sanitising. LOG-ONLY (<paramref name="enforce"/> false): logs rule ids and counts and returns the
    /// result unchanged. Enforce: a hit, or an incomplete scan, becomes <see cref="ValidationCodes.LanguageViolation"/>, so the gateway
    /// asks once more and then falls back to the extractive/fixed text. A fault in extraction or scanning never throws.
    /// </summary>
    public static Validated<T> Apply<T>(Validated<T> validated, Func<T, IEnumerable<GuardText>> texts, bool enforce, ILogger log, string workflow) where T : class
    {
        if (!validated.Ok) return validated;
        LanguageScan scan;
        try { scan = Scan(texts(validated.Value!).ToList()); }
        catch (Exception) { scan = new LanguageScan(new Dictionary<string, int>(), Incomplete: true); }
        if (scan.Clean) return validated;
        try { log.LogInformation("Language guard {Workflow} mode {Mode} rules {Rules}", workflow, enforce ? "enforce" : "log-only", scan.Summary); }
        catch (Exception) { /* logging must never break the pipeline */ }
        return enforce ? Validated<T>.Fail(ValidationCodes.LanguageViolation) : validated;
    }
}
