using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Tether.Ai.Contracts;
using Tether.Docs.Contracts;
using Tether.Docs.Domain;
using Tether.Lectures.Contracts;
using Microsoft.AspNetCore.WebUtilities;

namespace Tether.Docs.Application;

/// <summary>
/// The text policy of the Docs export (L-FORGE, SBV-01). Everything that reaches a document is untrusted plain text: it is re-derived from the
/// stored notes, reduced to ONE line (NFC, no control, format, bidi, zero-width, tag, private-use or surrogate-damage characters; every kind of
/// whitespace becomes one space), and cut at a code-point cap. A value that is empty afterwards is dropped, and the caller counts the drop.
/// </summary>
internal static class DocText
{
    public const int TitleMax = 200, BlockMax = 500;
    private const char Ellipsis = (char)0x2026;

    /// <summary>Cleans <paramref name="input"/>. Returns false if nothing is left. <paramref name="cut"/> is true when it was shortened to fit.</summary>
    public static bool TryClean(string? input, int maxCodePoints, out string clean, out bool cut)
    {
        clean = ""; cut = false;
        if (string.IsNullOrEmpty(input)) return false;
        string s;
        try { s = input.IsNormalized(NormalizationForm.FormC) ? input : input.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { s = input; } // lone surrogate: handled rune by rune below (replacement chars are dropped)
        var sb = new StringBuilder(s.Length);
        var pendingSpace = false;
        foreach (var r in s.EnumerateRunes())
        {
            var v = r.Value;
            var c = Rune.GetUnicodeCategory(r);
            if (v == 0xFFFD) continue;                                                  // decoding damage or a lone surrogate
            if (Rune.IsWhiteSpace(r) || c is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator || v is 0x85 or 0x09 or 0x0A or 0x0D)
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (c is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned or UnicodeCategory.Surrogate) continue;
            if (v is >= 0xE0000 and <= 0xE007F) continue;                              // tag characters (invisible text)
            if (v is 0x034F or 0x115F or 0x1160 or 0x3164 or 0xFFA0 or 0x2800 or 0xFFFC) continue; // invisible fillers
            if ((v & 0xFFFE) == 0xFFFE) continue;                                       // noncharacters
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(r.ToString());
        }
        var text = sb.ToString();
        if (text.Length == 0) return false;
        var total = CodePoints(text);
        if (total > maxCodePoints)
        {
            cut = true;
            var keep = maxCodePoints - 1;                                               // room for the ellipsis
            var end = 0; var n = 0;
            foreach (var r in text.EnumerateRunes()) { if (n == keep) break; end += r.Utf16SequenceLength; n++; }
            text = text[..end].TrimEnd() + "…";
        }
        clean = text;
        return true;
    }

    public static int CodePoints(string s)
    {
        var n = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
            n++;
        }
        return n;
    }
}

/// <summary>What the builder dropped or shortened (L-DROP: every drop is counted; the counts are logged, never the text).</summary>
internal sealed record BuildStats(int Dropped, int Shortened, int CitationsDropped);

/// <summary>The built document, its counts and its digest-ready form.</summary>
internal sealed record BuiltDocument(DocumentModel Document, int Sections, int Bullets, int Citations, bool TooLarge, bool NothingToExport, int TotalBlocks, BuildStats Stats);

/// <summary>
/// Deterministic builder: stored notes + lecture facts in, document out. No AI client, no network, no clock. Same input, same output, so the same
/// digest (B06, B09). The first block is the title as a level 1 heading, then an optional Final summary, one level 2 heading per notes section
/// with its bullets (citations reduced to lecture times), the closing source paragraph and, when the notes were cut short, a paragraph that says
/// where they stop (DXD-13).
/// </summary>
internal static class DocumentBuilder
{
    public const string FallbackTitle = "Lecture notes";
    public const string SummaryHeading = "Final summary";
    public const string MockSource = "Made by Tether from your lecture notes. Test routine, not an AI model: treat as estimates and check them against the transcript.";
    public const string ModelSource = "Made by Tether from your lecture notes. Written by an AI model from the transcript: it may be wrong, so check it against the transcript.";

    public static string TruncatedLine(long coveredToMs) => $"These notes stop at {FormatMs(coveredToMs)}. The lecture ran longer than the notes can cover.";

    /// <summary>mm:ss, or h:mm:ss from an hour on (the same as the lecture screens).</summary>
    public static string FormatMs(long ms)
    {
        var total = Math.Max(0, ms / 1000);
        var h = total / 3600; var m = total % 3600 / 60; var s = total % 60;
        return h > 0 ? string.Create(CultureInfo.InvariantCulture, $"{h}:{m:00}:{s:00}") : string.Create(CultureInfo.InvariantCulture, $"{m:00}:{s:00}");
    }

    /// <summary>Lecture title (or the fallback) and the start date. The session stores no zone today, so the date is the UTC date and says UTC.</summary>
    public static string BuildTitle(DocsSessionFacts facts)
    {
        var date = (facts.TimeZone is null ? facts.StartedAt.UtcDateTime : TimeZoneDate(facts)).ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        var suffix = " - " + date + (facts.TimeZone is null ? " UTC" : "");
        _ = DocText.TryClean(facts.Title, DocText.TitleMax - suffix.Length, out var name, out _);
        if (name.Length == 0) name = FallbackTitle;
        return name + suffix;
    }

    private static DateTime TimeZoneDate(DocsSessionFacts facts)
    {
        try { return TimeZoneInfo.ConvertTime(facts.StartedAt, TimeZoneInfo.FindSystemTimeZoneById(facts.TimeZone!)).DateTime; }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return facts.StartedAt.UtcDateTime; }
    }

    public static BuiltDocument Build(DocsSessionFacts facts, NotesSnapshot notes)
    {
        var title = BuildTitle(facts);
        var blocks = new List<DocBlock> { new("heading", title, 1, null) };
        int droppedBlocks = 0, shortened = 0, droppedCitations = 0;

        bool Clean(string? raw, int max, out string text)
        {
            if (!DocText.TryClean(raw, max, out text, out var cut)) { droppedBlocks++; return false; }
            if (cut) shortened++;
            return true;
        }

        if (notes.FinalSummary is { } fs)
        {
            var summaryBlocks = new List<DocBlock>();
            if (Clean(fs.Tldr, DocText.BlockMax, out var tldr)) summaryBlocks.Add(new("paragraph", tldr, null, null));
            foreach (var topic in fs.Topics)
                if (Clean(topic, DocText.BlockMax, out var t)) summaryBlocks.Add(new("paragraph", t, null, null));
            if (fs.CitationStartMs.Count > 0)
                summaryBlocks.Add(new("paragraph", "Transcript times: " + string.Join(", ", fs.CitationStartMs.Take(8).Select(FormatMs)), null, null));
            if (summaryBlocks.Count > 0)
            {
                blocks.Add(new("heading", SummaryHeading, 2, null));
                blocks.AddRange(summaryBlocks);
            }
        }

        int sections = 0, bullets = 0, citations = 0;
        foreach (var section in notes.Sections)
        {
            if (!Clean(section.Heading, DocText.BlockMax, out var heading)) { droppedBlocks += section.Bullets.Count; continue; }
            blocks.Add(new("heading", heading, 2, null));
            sections++;
            foreach (var b in section.Bullets)
            {
                var cites = b.CitationStartMs.Where(ms => ms >= 0).Take(8).ToList();
                droppedCitations += b.CitationStartMs.Count - cites.Count;
                if (cites.Count == 0) { droppedBlocks++; continue; }               // a bullet without a lecture time is not a notes bullet (contract: at least one citation)
                if (!Clean(b.Text, DocText.BlockMax, out var text)) continue;
                blocks.Add(new("bullet", text, null, cites));
                bullets++; citations += cites.Count;
            }
        }

        blocks.Add(new("paragraph", string.Equals(notes.Source, "mock", StringComparison.Ordinal) ? MockSource : ModelSource, null, null));
        if (notes.Truncated) blocks.Add(new("paragraph", TruncatedLine(notes.CoveredToMs ?? 0), null, null));

        var total = blocks.Count;
        var tooLarge = total > DocsConstants.MaxBlocks;
        var shown = tooLarge ? blocks.Take(DocsConstants.MaxBlocks).ToList() : blocks;   // preview only: a blocked preview never writes, and says so
        return new BuiltDocument(new DocumentModel(title, shown), Math.Min(sections, 400), Math.Min(bullets, 400), Math.Min(citations, 3200),
            tooLarge, bullets == 0, total, new BuildStats(droppedBlocks, shortened, droppedCitations));
    }
}

/// <summary>
/// The content digest (docs-common#/$defs/contentDigest, DXD-06): <c>cd_</c> + base64url(HMAC-SHA-256(K, label LF sessionId LF canonical JSON)).
/// K is derived by HKDF under its own label and is never the idempotency or audit key. The digest is an equality check on content, never proof of
/// authorization. Canonical JSON: UTF-8, no whitespace, members in schema order, strings NFC with only the quote, backslash and u00XX control
/// escapes (upper-case hex; the text policy removes controls, so no stored text ever needs one), integers in decimal.
/// </summary>
internal static class ContentDigest
{
    public const string Label = Tether.SharedKernel.Security.DerivedKeys.Labels.DocsContentDigest;

    public static string Canonical(string notesId, DocumentModel doc)
    {
        var sb = new StringBuilder(512);
        sb.Append("{\"v\":1,\"notesId\":"); Str(sb, notesId);
        sb.Append(",\"title\":"); Str(sb, doc.Title);
        sb.Append(",\"blocks\":[");
        for (var i = 0; i < doc.Blocks.Count; i++)
        {
            var b = doc.Blocks[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"kind\":"); Str(sb, b.Kind);
            switch (b.Kind)
            {
                case "heading":
                    sb.Append(",\"level\":").Append((b.Level ?? 1).ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"text\":"); Str(sb, b.Text);
                    break;
                case "bullet":
                    sb.Append(",\"text\":"); Str(sb, b.Text);
                    sb.Append(",\"citations\":[");
                    var cs = b.CitationStartMs ?? [];
                    for (var j = 0; j < cs.Count; j++)
                    {
                        if (j > 0) sb.Append(',');
                        sb.Append("{\"startMs\":").Append(cs[j].ToString(CultureInfo.InvariantCulture)).Append('}');
                    }
                    sb.Append(']');
                    break;
                default:
                    sb.Append(",\"text\":"); Str(sb, b.Text);
                    break;
            }
            sb.Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static void Str(StringBuilder sb, string s)
    {
        s = s.IsNormalized(NormalizationForm.FormC) ? s : s.Normalize(NormalizationForm.FormC);
        sb.Append('"');
        foreach (var ch in s)
        {
            if (ch == '"') sb.Append("\\\"");
            else if (ch == '\\') sb.Append("\\\\");
            else if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
            else sb.Append(ch);
        }
        sb.Append('"');
    }

    public static string Compute(byte[] key, Guid sessionId, string notesId, DocumentModel doc)
    {
        var input = Label + "\n" + sessionId.ToString("D") + "\n" + Canonical(notesId, doc);
        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(input));
        return "cd_" + WebEncoders.Base64UrlEncode(mac);
    }

    /// <summary>Constant-time comparison of two digests of the same length; anything else is a mismatch.</summary>
    public static bool Equal(string a, string b)
    {
        var x = Encoding.ASCII.GetBytes(a); var y = Encoding.ASCII.GetBytes(b);
        return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y);
    }
}
