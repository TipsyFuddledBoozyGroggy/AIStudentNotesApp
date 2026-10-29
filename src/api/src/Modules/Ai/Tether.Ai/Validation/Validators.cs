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
