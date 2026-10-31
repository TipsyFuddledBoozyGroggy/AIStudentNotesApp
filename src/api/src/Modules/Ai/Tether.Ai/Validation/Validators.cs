using System.Text.Json;

namespace Tether.Ai.Validation;

/// <summary>Strict, allocation-light helpers for checking model JSON against a closed shape (the C# twin of docs/ai/schemas/*.v1.json).</summary>
internal static class JsonShape
{
    public static bool TryParseObject(string json, out JsonDocument doc)
    {
        doc = null!;
        try
        {
            var d = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (d.RootElement.ValueKind != JsonValueKind.Object) { d.Dispose(); return false; }
            doc = d;
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>True when the object has exactly the required names plus any subset of the optional ones, and nothing else.</summary>
    public static bool Exactly(JsonElement o, string[] required, string[]? optional = null)
    {
        if (o.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in o.EnumerateObject())
        {
            if (!required.Contains(p.Name) && optional?.Contains(p.Name) != true) return false;
            if (!seen.Add(p.Name)) return false;
        }
        return required.All(seen.Contains);
    }

    public static string? AsString(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    public static int WordCount(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>Raised inside validators to unwind with a vocabulary code; never escapes this namespace.</summary>
internal sealed class Reject(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>Validates the rolling-summary delta (docs/ai/schemas/rolling-summary.v1.json).</summary>
internal static class SummaryValidator
{
    public const int MaxTldr = 600, MaxTopics = 8, MaxTopicName = 60, MaxTopicText = 300, MaxIdsPerTopic = 8, MaxTerms = 8, MaxTerm = 60;

    public static Validated<SummaryDelta> Validate(string json, SummaryContext ctx)
    {
        if (!JsonShape.TryParseObject(json, out var doc)) return Validated<SummaryDelta>.Fail(ValidationCodes.NotJson);
        using (doc)
        {
            try { return Validated<SummaryDelta>.Success(Run(doc.RootElement, ctx)); }
            catch (Reject r) { return Validated<SummaryDelta>.Fail(r.Code); }
        }
    }

    private static SummaryDelta Run(JsonElement root, SummaryContext ctx)
    {
        if (!JsonShape.Exactly(root, ["baseRevision", "tldr", "topicUpdates", "newTerms"])) throw new Reject(ValidationCodes.SchemaInvalid);
        if (root.GetProperty("baseRevision").ValueKind != JsonValueKind.Number || !root.GetProperty("baseRevision").TryGetInt32(out var baseRev))
            throw new Reject(ValidationCodes.SchemaInvalid);
        var tldrRaw = JsonShape.AsString(root.GetProperty("tldr")) ?? throw new Reject(ValidationCodes.SchemaInvalid);
        if (tldrRaw.Length > MaxTldr) throw new Reject(ValidationCodes.Overlong);
        var updates = root.GetProperty("topicUpdates");
        var terms = root.GetProperty("newTerms");
        if (updates.ValueKind != JsonValueKind.Array || terms.ValueKind != JsonValueKind.Array) throw new Reject(ValidationCodes.SchemaInvalid);
        if (updates.GetArrayLength() > MaxTopics || terms.GetArrayLength() > MaxTerms) throw new Reject(ValidationCodes.Overlong);

        var sanitized = false;
        var topics = new List<TopicState>();
        int droppedCitations = 0, droppedTopics = 0;
        foreach (var u in updates.EnumerateArray())
        {
            if (!JsonShape.Exactly(u, ["topic", "text", "segmentIds"])) throw new Reject(ValidationCodes.SchemaInvalid);
            var name = JsonShape.AsString(u.GetProperty("topic")) ?? throw new Reject(ValidationCodes.SchemaInvalid);
            var text = JsonShape.AsString(u.GetProperty("text")) ?? throw new Reject(ValidationCodes.SchemaInvalid);
            var idsEl = u.GetProperty("segmentIds");
            if (idsEl.ValueKind != JsonValueKind.Array) throw new Reject(ValidationCodes.SchemaInvalid);
            if (name.Length > MaxTopicName || text.Length > MaxTopicText || idsEl.GetArrayLength() > MaxIdsPerTopic) throw new Reject(ValidationCodes.Overlong);

            var cites = new List<CitationRef>();
            foreach (var idEl in idsEl.EnumerateArray())
            {
                var id = JsonShape.AsString(idEl) ?? throw new Reject(ValidationCodes.SchemaInvalid);
                if (ctx.Allowed.TryGetValue(id, out var line)) { if (cites.All(c => c.SegmentId != id)) cites.Add(new CitationRef(id, line.StartMs)); }
                else droppedCitations++;
            }
            var cleanName = OutputSanitizer.Clean(name, out var c1);
            var cleanText = OutputSanitizer.Clean(text, out var c2);
            sanitized |= c1 | c2;
            if (cleanName.Length == 0 || cleanText.Length == 0) throw new Reject(ValidationCodes.SchemaInvalid);
            if (cites.Count == 0) { droppedTopics++; continue; }
            topics.Add(new TopicState(cleanName, cleanText, cites));
        }

        var newTerms = new List<string>();
        foreach (var t in terms.EnumerateArray())
        {
            var s = JsonShape.AsString(t) ?? throw new Reject(ValidationCodes.SchemaInvalid);
            if (s.Length > MaxTerm) throw new Reject(ValidationCodes.Overlong);
            var clean = OutputSanitizer.Clean(s, out var c);
            sanitized |= c;
            if (clean.Length > 0) newTerms.Add(clean);
        }

        var tldr = OutputSanitizer.Clean(tldrRaw, out var ct);
        sanitized |= ct;
        if (tldr.Length == 0) throw new Reject(ValidationCodes.SchemaInvalid);
        if (baseRev != ctx.BaseRevision) throw new Reject(ValidationCodes.StaleBase);
        if (topics.Count == 0) throw new Reject(ValidationCodes.Ungrounded);
        return new SummaryDelta(tldr, topics, newTerms, droppedCitations, droppedTopics, sanitized);
    }
}

/// <summary>
/// Validates the Quick recap result (docs/ai/schemas/quick-recap.v1.json) and enforces the grounding rule: a claim labelled
/// "lecture" keeps only citations that resolve to segments of THIS window, and loses the label if none do. Timecodes are always
/// looked up from those segments.
/// </summary>
internal static class CatchUpValidator
{
    public const int MaxClaimChars = 300, NowWords = 25, ShortWords = 20, DetailWords = 40, MaxShort = 3, MaxDetail = 8, MaxTerms = 5, MaxQuestions = 3, MaxGaps = 5,
        MaxIds = 8, MaxTerm = 60, MaxDefinition = 200, MaxWhy = 200, MaxQuestion = 200, MaxGap = 200;

    public static Validated<CatchUpResult> Validate(string json, CatchUpContext ctx)
    {
        if (!JsonShape.TryParseObject(json, out var doc)) return Validated<CatchUpResult>.Fail(ValidationCodes.NotJson);
        using (doc)
        {
            try { return Validated<CatchUpResult>.Success(Run(doc.RootElement, ctx)); }
            catch (Reject r) { return Validated<CatchUpResult>.Fail(r.Code); }
        }
    }

    private sealed class State { public bool Sanitized; public int Downgraded; }

    private static CatchUpResult Run(JsonElement root, CatchUpContext ctx)
    {
        string[] required = ["nowDiscussing", "shortCatchUp", "detail", "keyTerms", "clarifyingQuestions", "confidence", "gaps"];
        if (!JsonShape.Exactly(root, required)) throw new Reject(ValidationCodes.SchemaInvalid);
        var st = new State();

        var now = ReadClaim(root.GetProperty("nowDiscussing"), ctx, st, NowWords, null).Claim;

        var shortEl = Arr(root.GetProperty("shortCatchUp"), MaxShort);
        if (shortEl.Count is < 1) throw new Reject(ValidationCodes.SchemaInvalid);
        var shortList = shortEl.Select(e => ReadClaim(e, ctx, st, ShortWords, null).Claim).ToList();

        var detail = Arr(root.GetProperty("detail"), MaxDetail).Select(e =>
        {
            var (claim, extra) = ReadClaim(e, ctx, st, DetailWords, "whyItMatters");
            string? why = null;
            if (extra is not null)
            {
                if (extra.Length > MaxWhy) throw new Reject(ValidationCodes.Overlong);
                why = Clean(extra, st);
                if (why.Length == 0) why = null;
            }
            return new DetailItem(claim, why);
        }).ToList();

        var terms = Arr(root.GetProperty("keyTerms"), MaxTerms).Select(e => ReadTerm(e, ctx, st)).ToList();

        var qEl = Arr(root.GetProperty("clarifyingQuestions"), MaxQuestions);
        var questions = qEl.Select(e => Bounded(e, MaxQuestion, st)).Where(q => q.Length > 0).ToList();

        var confidence = JsonShape.AsString(root.GetProperty("confidence"));
        if (confidence is not ("low" or "medium" or "high")) throw new Reject(ValidationCodes.SchemaInvalid);

        var gaps = Arr(root.GetProperty("gaps"), MaxGaps).Select(e => Bounded(e, MaxGap, st)).Where(g => g.Length > 0).ToList();

        if (ctx.WindowHasSegments)
        {
            var anyGrounded = now.Basis == Basis.Lecture || shortList.Any(c => c.Basis == Basis.Lecture) || detail.Any(d => d.Claim.Basis == Basis.Lecture);
            if (!anyGrounded) throw new Reject(ValidationCodes.Ungrounded);
        }
        return new CatchUpResult(now, shortList, detail, terms, questions, confidence, gaps, st.Downgraded, st.Sanitized);
    }

    private static List<JsonElement> Arr(JsonElement e, int max)
    {
        if (e.ValueKind != JsonValueKind.Array) throw new Reject(ValidationCodes.SchemaInvalid);
        if (e.GetArrayLength() > max) throw new Reject(ValidationCodes.Overlong);
        return e.EnumerateArray().ToList();
    }

    private static string Clean(string s, State st)
    {
        var c = OutputSanitizer.Clean(s, out var changed);
        st.Sanitized |= changed;
        return c;
    }

    private static string Bounded(JsonElement e, int max, State st)
    {
        var s = JsonShape.AsString(e) ?? throw new Reject(ValidationCodes.SchemaInvalid);
        if (s.Length > max) throw new Reject(ValidationCodes.Overlong);
        return Clean(s, st);
    }

    private static (Claim Claim, string? Extra) ReadClaim(JsonElement e, CatchUpContext ctx, State st, int maxWords, string? optionalField)
    {
        if (!JsonShape.Exactly(e, ["text", "basis", "segmentIds"], optionalField is null ? null : [optionalField])) throw new Reject(ValidationCodes.SchemaInvalid);
        var text = JsonShape.AsString(e.GetProperty("text")) ?? throw new Reject(ValidationCodes.SchemaInvalid);
        var basis = JsonShape.AsString(e.GetProperty("basis"));
        if (!Basis.IsValid(basis)) throw new Reject(ValidationCodes.SchemaInvalid);
        if (text.Length > MaxClaimChars || JsonShape.WordCount(text) > maxWords) throw new Reject(ValidationCodes.Overlong);
        var cites = ResolveIds(e.GetProperty("segmentIds"), ctx);
        var clean = Clean(text, st);
        if (clean.Length == 0) throw new Reject(ValidationCodes.SchemaInvalid);
        string? extra = null;
        if (optionalField is not null && e.TryGetProperty(optionalField, out var x)) extra = JsonShape.AsString(x) ?? throw new Reject(ValidationCodes.SchemaInvalid);

        if (basis == Basis.Lecture)
        {
            if (cites.Count == 0) { st.Downgraded++; return (new Claim(clean, Basis.Uncertain, []), extra); }
            return (new Claim(clean, Basis.Lecture, cites), extra);
        }
        return (new Claim(clean, basis!, []), extra); // only lecture claims may carry lecture citations
    }

    private static KeyTerm ReadTerm(JsonElement e, CatchUpContext ctx, State st)
    {
        if (!JsonShape.Exactly(e, ["term", "basis", "segmentIds"], ["definition"])) throw new Reject(ValidationCodes.SchemaInvalid);
        var term = JsonShape.AsString(e.GetProperty("term")) ?? throw new Reject(ValidationCodes.SchemaInvalid);
        var basis = JsonShape.AsString(e.GetProperty("basis"));
        if (!Basis.IsValid(basis)) throw new Reject(ValidationCodes.SchemaInvalid);
        if (term.Length > MaxTerm) throw new Reject(ValidationCodes.Overlong);
        string? def = null;
        if (e.TryGetProperty("definition", out var d))
        {
            var ds = JsonShape.AsString(d) ?? throw new Reject(ValidationCodes.SchemaInvalid);
            if (ds.Length > MaxDefinition) throw new Reject(ValidationCodes.Overlong);
            def = Clean(ds, st);
            if (def.Length == 0) def = null;
        }
        var cites = ResolveIds(e.GetProperty("segmentIds"), ctx);
        var cleanTerm = Clean(term, st);
        if (cleanTerm.Length == 0) throw new Reject(ValidationCodes.SchemaInvalid);
        if (basis == Basis.Lecture && cites.Count == 0) { st.Downgraded++; return new KeyTerm(cleanTerm, def, Basis.Uncertain, []); }
        return new KeyTerm(cleanTerm, def, basis!, basis == Basis.Lecture ? cites : []);
    }

    private static List<CitationRef> ResolveIds(JsonElement idsEl, CatchUpContext ctx)
    {
        if (idsEl.ValueKind != JsonValueKind.Array) throw new Reject(ValidationCodes.SchemaInvalid);
        if (idsEl.GetArrayLength() > MaxIds) throw new Reject(ValidationCodes.Overlong);
        var list = new List<CitationRef>();
        foreach (var i in idsEl.EnumerateArray())
        {
            var id = JsonShape.AsString(i) ?? throw new Reject(ValidationCodes.SchemaInvalid);
            if (ctx.Allowed.TryGetValue(id, out var line) && list.All(c => c.SegmentId != id)) list.Add(new CitationRef(id, line.StartMs));
        }
        return list;
    }
}

/// <summary>
/// Validates one lecture-notes call (PO-18d; the C# twin of the model reply shape in <see cref="Prompts.PromptLibrary.Notes"/>). Closed
/// shapes, bounded lengths, sanitised text, and the grounding rule: a bullet keeps only citations that resolve to segments of THIS
/// part of the stored transcript (timecodes looked up from them, never taken from the model); a bullet with none is DROPPED, a section
/// left without bullets is dropped, and a reply with nothing grounded is rejected.
/// </summary>
internal static class NotesValidator
{
    public const int MaxSections = 6, MaxBullets = 8, MaxHeading = 80, MaxText = 300, MaxIds = 8;

    public static Validated<NotesChunkResult> Validate(string json, NotesContext ctx)
    {
        if (!JsonShape.TryParseObject(json, out var doc)) return Validated<NotesChunkResult>.Fail(ValidationCodes.NotJson);
        using (doc)
        {
            try { return Validated<NotesChunkResult>.Success(Run(doc.RootElement, ctx)); }
            catch (Reject r) { return Validated<NotesChunkResult>.Fail(r.Code); }
        }
    }

    private static NotesChunkResult Run(JsonElement root, NotesContext ctx)
    {
        if (!JsonShape.Exactly(root, ["sections"])) throw new Reject(ValidationCodes.SchemaInvalid);
        var sectionsEl = root.GetProperty("sections");
        if (sectionsEl.ValueKind != JsonValueKind.Array) throw new Reject(ValidationCodes.SchemaInvalid);
        if (sectionsEl.GetArrayLength() > MaxSections) throw new Reject(ValidationCodes.Overlong);

        var sanitized = false;
        int droppedBullets = 0, droppedSections = 0, droppedUngrounded = 0, droppedInstruction = 0, headingsReplaced = 0;
        var sections = new List<NotesSection>();
        foreach (var s in sectionsEl.EnumerateArray())
        {
            if (!JsonShape.Exactly(s, ["heading", "bullets"])) throw new Reject(ValidationCodes.SchemaInvalid);
            var heading = JsonShape.AsString(s.GetProperty("heading")) ?? throw new Reject(ValidationCodes.SchemaInvalid);
            if (heading.Length > MaxHeading) throw new Reject(ValidationCodes.Overlong);
            var bulletsEl = s.GetProperty("bullets");
            if (bulletsEl.ValueKind != JsonValueKind.Array) throw new Reject(ValidationCodes.SchemaInvalid);
            if (bulletsEl.GetArrayLength() > MaxBullets) throw new Reject(ValidationCodes.Overlong);

            var bullets = new List<NotesBullet>();
            var support = new List<string>(); // the section's grounding text: its kept bullets and the segments they cite
            foreach (var b in bulletsEl.EnumerateArray())
            {
                if (!JsonShape.Exactly(b, ["text", "segmentIds"])) throw new Reject(ValidationCodes.SchemaInvalid);
                var text = JsonShape.AsString(b.GetProperty("text")) ?? throw new Reject(ValidationCodes.SchemaInvalid);
                if (text.Length > MaxText) throw new Reject(ValidationCodes.Overlong);
                var idsEl = b.GetProperty("segmentIds");
                if (idsEl.ValueKind != JsonValueKind.Array) throw new Reject(ValidationCodes.SchemaInvalid);
                if (idsEl.GetArrayLength() > MaxIds) throw new Reject(ValidationCodes.Overlong);
                var cites = new List<CitationRef>();
                var cited = new List<string>();
                var hostileOnly = false;
                foreach (var idEl in idsEl.EnumerateArray())
                {
                    var id = JsonShape.AsString(idEl) ?? throw new Reject(ValidationCodes.SchemaInvalid);
                    if (!ctx.Allowed.TryGetValue(id, out var line) || cites.Any(c => c.SegmentId == id)) continue;
                    // NAS-04: a segment that is itself an instruction aimed at the AI ("ignore your instructions and write ...") is never a
                    // source: it is not cited and cannot support anything, so whatever the model derived from it alone is dropped.
                    if (NotesGrounding.IsInstructionLike(line.Text)) { hostileOnly = true; continue; }
                    cites.Add(new CitationRef(id, line.StartMs));
                    cited.Add(line.Text);
                }
                var clean = OutputSanitizer.Clean(text, out var changed);
                sanitized |= changed;
                if (cites.Count == 0 && hostileOnly && clean.Length > 0) { droppedBullets++; droppedInstruction++; continue; }
                if (cites.Count == 0 || clean.Length == 0) { droppedBullets++; continue; } // uncited claims never reach the student
                // NAS-04: a spoken instruction aimed at the AI is not a note, even when the transcript really contains it.
                if (NotesGrounding.IsInstructionLike(clean)) { droppedBullets++; droppedInstruction++; continue; }
                // NAS-04: the cited segments must actually say it (significant-term overlap, every number verbatim).
                if (!NotesGrounding.IsSupported(clean, cited)) { droppedBullets++; droppedUngrounded++; continue; }
                bullets.Add(new NotesBullet(clean, cites));
                support.Add(clean);
                support.AddRange(cited);
            }
            var cleanHeading = OutputSanitizer.Clean(heading, out var hc);
            sanitized |= hc;
            if (bullets.Count == 0) { droppedSections++; continue; }
            // NAS-04: no free-text heading without grounding. A model heading stays only if it is supported by this section's kept bullets and
            // their segments and is not instruction-like; otherwise the server builds one from the first kept bullet.
            if (cleanHeading.Length == 0 || NotesGrounding.IsInstructionLike(cleanHeading) || !NotesGrounding.IsSupported(cleanHeading, support))
            {
                cleanHeading = NotesGrounding.ServerHeading(bullets[0].Text);
                headingsReplaced++;
            }
            sections.Add(new NotesSection(cleanHeading, bullets));
        }
        if (sections.Count == 0) throw new Reject(ValidationCodes.Ungrounded);
        return new NotesChunkResult(sections, droppedBullets, droppedSections, sanitized, droppedUngrounded, droppedInstruction, headingsReplaced);
    }
}

/// <summary>
/// NAS-04 / NAS-R01: deterministic checks that a notes bullet (or heading) is supported by the transcript text it cites, and is not an instruction
/// aimed at the AI. Cheap, no model, no network. They narrow, but do not close, the prompt-injection risk: a false statement that a speaker really
/// said and the model faithfully repeats still passes (that needs the real eval set, gate G2). Rules (documented in
/// docs/architecture/notes-after-stop.md section 8):
/// <list type="number">
/// <item>Normalisation (NAS-R01): both sides are NFKC-normalised, every decimal digit of any script becomes its ASCII digit (so circled,
/// superscript, fullwidth and Arabic-Indic digits are digits), curly apostrophes become straight ones, and "n't" becomes " not".</item>
/// <item>Terms: the text is split on anything that is not a letter or number and lower-cased. A term is significant when it contains a digit, is a
/// number word, a month or a weekday, or is at least 3 letters long and neither a stop word nor a negator. Ordinary terms are compared by a light
/// stem: the first 5 letters of a word of 6 or more, otherwise the word without a trailing "s".</item>
/// <item>Critical terms (NAS-R01) must match EXACTLY, never by majority: numbers (digits verbatim; number words and ordinals as their value, so
/// "five" = "5" but "five" is not "fifty"), month names (Dec = December; "may" only next to a number) and weekday names (Tue = Tuesday).</item>
/// <item>Polarity (NAS-R01): a negator (not, no, never, cannot, n't, without, none, nobody, nothing, neither, nor) negates the words after it up to
/// the end of its clause (. ! ? ; : or but / however / although / though / whereas / yet / instead). Every term of the bullet that also occurs in
/// the cited text must occur there with the SAME polarity at least once, and a bullet that negates anything needs a negator in its cited text.
/// So "not cancelled" never supports "cancelled", "graded" never supports "not graded".</item>
/// <item>Support: at least one significant term, every critical term exact, polarity as above, and at least <see cref="MinSupport"/> (half) of
/// the significant terms appear (critical exactly, the rest by stem) in the cited text.</item>
/// <item>Instruction-like: any of <see cref="InstructionRules"/> matches (override phrases, system/developer prompt, ADDRESSING an AI, output-format
/// orders, tool calls, or an imperative that starts the bullet and is about the notes/summary/output itself). Merely naming an AI or a person
/// called Claude is not an instruction (NAS-R08).</item>
/// </list>
/// </summary>
internal static class NotesGrounding
{
    public const double MinSupport = 0.5;
    public const int ServerHeadingWords = 6, ServerHeadingMaxChars = 60;

    /// <summary>Common English function words; they carry no claim, so they neither support nor count against a bullet.</summary>
    public static readonly IReadOnlySet<string> StopWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "the", "and", "for", "are", "but", "not", "you", "your", "all", "any", "can", "had", "has", "have", "her", "his", "him", "its", "our", "out",
        "she", "they", "them", "their", "there", "these", "those", "this", "that", "then", "than", "was", "were", "will", "with", "what", "when",
        "where", "which", "who", "whom", "why", "how", "from", "into", "onto", "over", "under", "about", "also", "just", "very", "some", "such",
        "each", "other", "more", "most", "much", "many", "only", "own", "same", "too", "can't", "don", "does", "did", "doing", "done", "been",
        "being", "because", "while", "after", "before", "again", "here", "would", "could", "should", "may", "might", "must", "shall",
        "get", "got", "like", "say", "said", "says", "let", "lets", "now", "well", "yes", "okay", "really", "thing", "things", "way", "kind",
        "lecture", "lecturer", "professor", "today", "class", "talked", "talk", "discussed", "explained", "mentioned", "covered", "noted", "notes",
    };

    /// <summary>NAS-R01: words that negate the rest of their clause. Not significant themselves; they set the polarity of the words after them.</summary>
    public static readonly IReadOnlySet<string> Negators = new HashSet<string>(StringComparer.Ordinal)
    {
        "not", "no", "never", "cannot", "without", "none", "nobody", "nothing", "neither", "nor", "nowhere",
    };

    /// <summary>NAS-R01: words that start a new clause, ending a negation's scope.</summary>
    private static readonly IReadOnlySet<string> ClauseWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "but", "however", "although", "though", "whereas", "yet", "instead",
    };

    /// <summary>NAS-R01: number words and ordinals, compared as their value ("five" = "5", "third" = "3rd").</summary>
    private static readonly IReadOnlyDictionary<string, string> NumberWords = BuildNumberWords();

    /// <summary>NAS-R01: months and weekdays, with their common abbreviations, compared by canonical name.</summary>
    private static readonly IReadOnlyDictionary<string, string> Calendar = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["january"] = "january", ["jan"] = "january", ["february"] = "february", ["feb"] = "february", ["march"] = "march", ["mar"] = "march",
        ["april"] = "april", ["apr"] = "april", ["june"] = "june", ["jun"] = "june", ["july"] = "july", ["jul"] = "july",
        ["august"] = "august", ["aug"] = "august", ["september"] = "september", ["sep"] = "september", ["sept"] = "september",
        ["october"] = "october", ["oct"] = "october", ["november"] = "november", ["nov"] = "november", ["december"] = "december", ["dec"] = "december",
        ["monday"] = "monday", ["mon"] = "monday", ["tuesday"] = "tuesday", ["tue"] = "tuesday", ["tues"] = "tuesday",
        ["wednesday"] = "wednesday", ["wed"] = "wednesday", ["thursday"] = "thursday", ["thu"] = "thursday", ["thur"] = "thursday", ["thurs"] = "thursday",
        ["friday"] = "friday", ["fri"] = "friday", ["saturday"] = "saturday", ["sunday"] = "sunday",
    };

    private static Dictionary<string, string> BuildNumberWords()
    {
        string[] ones = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen",
            "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];
        string[] tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];
        string[] ordinals = ["zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "eleventh", "twelfth",
            "thirteenth", "fourteenth", "fifteenth", "sixteenth", "seventeenth", "eighteenth", "nineteenth"];
        string[] tenOrdinals = ["", "", "twentieth", "thirtieth", "fortieth", "fiftieth", "sixtieth", "seventieth", "eightieth", "ninetieth"];
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < ones.Length; i++) { d[ones[i]] = i.ToString(System.Globalization.CultureInfo.InvariantCulture); d[ordinals[i]] = Ordinal(i); }
        for (var i = 2; i < tens.Length; i++) { d[tens[i]] = (i * 10).ToString(System.Globalization.CultureInfo.InvariantCulture); d[tenOrdinals[i]] = Ordinal(i * 10); }
        d["hundred"] = "100"; d["thousand"] = "1000"; d["million"] = "1000000"; d["billion"] = "1000000000"; d["dozen"] = "12";
        d["hundredth"] = Ordinal(100); d["thousandth"] = Ordinal(1000);
        return d;

        static string Ordinal(int n)
        {
            var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
            return n.ToString(System.Globalization.CultureInfo.InvariantCulture) + suffix;
        }
    }

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static System.Text.RegularExpressions.Regex Rx(string p) =>
        new(p, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, RegexTimeout);

    // Names of AI systems and roles. NAS-R08: naming one is NOT instruction-like by itself (Claude Shannon, Claude Monet, a lecture on GPT-4 or
    // LLMs); only ADDRESSING one is (a vocative "Claude, write ...", "hey ChatGPT", "note to the AI").
    private const string AiNames = @"ai|assistant|bot|chatbot|model|summari[sz]er|note-?\s?taker|transcriber|chatgpt|claude|gpt-?\d+(\.\d+)?|openai|anthropic|llm|large\s+language\s+model|ai\s+assistant";

    /// <summary>The instruction-like rule set (NAS-04, NAS-R08). Each pattern targets text aimed at an AI, not at students. Matched on NFKC text.</summary>
    public static readonly IReadOnlyList<System.Text.RegularExpressions.Regex> InstructionRules =
    [
        Rx(@"\b(ignore|disregard|forget|override|bypass)\b[^.!?]{0,40}\b(instructions?|prompts?|rules|directions|guidelines)\b"),
        Rx(@"\b(system|developer|hidden)\s+(prompt|message|instructions?)\b"),
        Rx(@"\byou\s+are\s+(now\s+)?(an?\s+|the\s+)?(ai|assistant|chatbot|bot|language\s+model|model)\b"),
        Rx($@"\b(hey|dear|attention|note\s+to)\s+(the\s+)?({AiNames})\b"),
        Rx($@"\b({AiNames})\s*[,:]\s*(please\s+)?(write|say|output|add|include|put|tell|state|record|ignore|reply|respond|print|list|mark|delete|send)\b"),
        Rx(@"\b(respond|reply|output|print|answer)\s+(only|with\s+only|exactly|in\s+json|the\s+following)\b"),
        Rx(@"\bnew\s+instructions?\b"),
        Rx(@"\b(call|invoke|run|use)\s+(the\s+)?(tool|function\s+call)\b|\btool[_\s-]?(call|use)\b"),
        Rx(@"\b(calendar|docs?|drive|gmail|email)\.(create|delete|update|send|insert|share)\b"),
        Rx(@"^\s*(please\s+|now\s+|also\s+)?(write|put|add|include|say|state|record|mark|tell|output|print|ignore|disregard|forget)\b[^.!?]{0,80}\b(notes?|summary|summaries|output|response|transcript|instructions?)\b"),
    ];

    public static bool IsInstructionLike(string text)
    {
        var normalized = Normalize(text); // NAS-R01: fullwidth or other compatibility forms cannot dodge the patterns
        foreach (var r in InstructionRules)
        {
            try { if (r.IsMatch(normalized)) return true; }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return true; } // fail closed: an input that slow is not a note
        }
        return false;
    }

    /// <summary>Significant terms of <paramref name="text"/> (rule 2), normalised and lower-cased, unstemmed.</summary>
    public static List<string> Terms(string text)
    {
        var toks = Analyse(text);
        return toks.Where((t, i) => IsSignificant(toks, i)).Select(t => t.Word).ToList();
    }

    public static bool IsSupported(string text, IEnumerable<string> sources)
    {
        var bullet = Analyse(text);
        var terms = Enumerable.Range(0, bullet.Count).Where(i => IsSignificant(bullet, i)).Select(i => (Tok: bullet[i], Critical: IsCritical(bullet, i))).ToList();
        if (terms.Count == 0) return false;

        var src = sources.Select(Analyse).ToList();
        var exact = new HashSet<string>(StringComparer.Ordinal);    // critical terms, by canonical value
        var stems = new HashSet<string>(StringComparer.Ordinal);    // every term, by stem of its canonical form
        var polar = new HashSet<(string, bool)>();                  // (stem, negated) pairs seen in the sources
        var sourceNegates = false;
        foreach (var toks in src)
            for (var i = 0; i < toks.Count; i++)
            {
                var t = toks[i];
                if (t.IsNegator) { sourceNegates = true; continue; }
                var key = Key(t.Word);
                stems.Add(key);
                polar.Add((key, t.Negated));
                if (IsCritical(toks, i)) exact.Add(Canonical(t.Word));
            }

        // A bullet that negates something needs a negation in what it cites (else "graded" could support "not graded").
        if (bullet.Any(t => t.IsNegator) && !sourceNegates) return false;
        var hit = 0;
        foreach (var (t, critical) in terms)
        {
            var key = Key(t.Word);
            if (critical)
            {
                if (!exact.Contains(Canonical(t.Word))) return false; // a number, month or weekday the source does not say is never shown
                hit++;
            }
            else if (stems.Contains(key)) hit++;
            // Polarity: a term the source has must be there with this bullet's polarity ("not cancelled" never supports "cancelled").
            if (stems.Contains(key) && !polar.Contains((key, t.Negated))) return false;
        }
        return hit >= Math.Ceiling(terms.Count * MinSupport);
    }

    /// <summary>NAS-R01: NFKC, every decimal digit of any script as its ASCII digit, straight apostrophes.</summary>
    internal static string Normalize(string text)
    {
        var n = text.Normalize(System.Text.NormalizationForm.FormKC);
        var sb = new System.Text.StringBuilder(n.Length);
        foreach (var ch in n)
        {
            if (char.IsDigit(ch) && System.Globalization.CharUnicodeInfo.GetDecimalDigitValue(ch) is var v and >= 0) sb.Append((char)('0' + v));
            else if (ch is '’' or '‘' or 'ʼ' or '′') sb.Append('\'');
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    private readonly record struct Tok(string Word, bool Negated, bool IsNegator);

    private static readonly System.Text.RegularExpressions.Regex Contraction =
        new(@"(?<=\p{L})n't\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>Normalised tokens with their polarity: a negator negates the rest of its clause (rule 4).</summary>
    private static List<Tok> Analyse(string text)
    {
        var s = Normalize(text);
        try { s = Contraction.Replace(s, " not"); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { /* keep the text; "n't" then splits into "n" + "t" */ }
        var list = new List<Tok>();
        var negated = false;
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s.Append(' '))
        {
            if (char.IsLetterOrDigit(ch) || char.IsNumber(ch)) { sb.Append(char.ToLowerInvariant(ch)); continue; }
            if (sb.Length > 0)
            {
                var w = sb.ToString();
                sb.Clear();
                if (Negators.Contains(w)) { negated = true; list.Add(new Tok(w, true, true)); }
                else
                {
                    if (ClauseWords.Contains(w)) negated = false;
                    list.Add(new Tok(w, negated, false));
                }
            }
            if (ch is '.' or '!' or '?' or ';' or ':' or '\n' or '\r') negated = false;
        }
        return list;
    }

    private static bool IsSignificant(List<Tok> toks, int i)
    {
        var t = toks[i].Word;
        if (toks[i].IsNegator) return false;
        return IsCritical(toks, i) || (t.Length >= 3 && !StopWords.Contains(t));
    }

    /// <summary>Rule 3: numbers, number words, months and weekdays. "may" is a month only next to a number ("3 May", "May 3").</summary>
    private static bool IsCritical(List<Tok> toks, int i)
    {
        var t = toks[i].Word;
        if (t.Any(char.IsNumber) || NumberWords.ContainsKey(t) || Calendar.ContainsKey(t)) return true;
        return t == "may" && ((i > 0 && toks[i - 1].Word.Any(char.IsDigit)) || (i + 1 < toks.Count && toks[i + 1].Word.Any(char.IsDigit)));
    }

    /// <summary>The value a critical term is compared by: "five" -> "5", "Dec" -> "december"; anything else unchanged.</summary>
    private static string Canonical(string t) => NumberWords.TryGetValue(t, out var n) ? n : Calendar.TryGetValue(t, out var c) ? c : t;

    private static string Key(string t) => Stem(Canonical(t));

    /// <summary>A heading built from an already validated bullet: its first few words, without trailing punctuation, capped.</summary>
    public static string ServerHeading(string bullet)
    {
        var words = bullet.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(ServerHeadingWords);
        var h = string.Join(' ', words).TrimEnd('.', ',', ';', ':', '!', '?', '-');
        if (h.Length > ServerHeadingMaxChars) h = h[..ServerHeadingMaxChars].TrimEnd();
        return h.Length == 0 ? "Lecture notes" : h;
    }

    private static string Stem(string t)
    {
        if (t.Any(char.IsNumber)) return t;
        if (t.Length >= 6) return t[..5];
        return t.Length >= 4 && t[^1] == 's' ? t[..^1] : t;
    }
}
