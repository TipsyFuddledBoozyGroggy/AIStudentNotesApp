using System.Globalization;
using System.Text;

namespace Tether.Syllabus.Text;

/// <summary>Result of canonicalizing pasted or extracted text (docs/API_CONTRACTS_P2.md section 2.2 "Document-derived text").</summary>
internal sealed record CanonicalDocument(string Text, int HiddenRemoved, bool InjectionSuspect)
{
    public int Utf16Length => Text.Length;
    public int CodePointCount => TextPolicy.CodePoints(Text);
}

/// <summary>
/// Document-derived text is canonicalized by STRIPPING the characters TextPolicy rejects on student fields, rather than failing the
/// whole document. Stripping is itself a signal (R2-06, R3-08): tag characters, bidi controls, other format characters, variation
/// selectors outside an emoji sequence, fillers, braille blank and U+FFFC are counted in <c>HiddenRemoved</c>; tag characters, the
/// U+E0100 supplement, bidi overrides/isolates and more than 8 stripped format characters also set <c>InjectionSuspect</c>.
/// The result is NFC (not NFKC: snippets stay verbatim), uses LF only, collapses space runs and trims every line. NFC is applied before
/// stripping and again after it (SBV-04), so the final string is NFC even when a stripped character sat between a letter and its mark.
/// </summary>
internal static class DocumentText
{
    public static CanonicalDocument Canonicalize(string raw)
    {
        var s = raw.IsNormalized(NormalizationForm.FormC) ? raw : raw.Normalize(NormalizationForm.FormC);
        s = s.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u0085', '\n').Replace(((char)0x2028).ToString(), "\n").Replace(((char)0x2029).ToString(), "\n");

        var input = new List<Rune>(s.Length);
        foreach (var r in s.EnumerateRunes()) input.Add(r);

        var output = new List<Rune>(input.Count);
        var hidden = 0; var cfStripped = 0; var suspect = false; var marks = 0;
        for (var i = 0; i < input.Count; i++)
        {
            var r = input[i]; var v = r.Value; var c = TextPolicy.Cat(r);
            var prevOut = output.Count > 0 ? output[^1] : (Rune?)null;
            var nextIn = i + 1 < input.Count ? input[i + 1] : (Rune?)null;

            if (v == '\n') { output.Add(r); marks = 0; continue; }
            if (v == '\t') { output.Add(new Rune(' ')); marks = 0; continue; }
            if (c == UnicodeCategory.Control) continue;                                   // silently dropped (NUL is rejected earlier)
            if (c == UnicodeCategory.SpaceSeparator || (Rune.IsWhiteSpace(r) && v != '\n')) { output.Add(new Rune(' ')); marks = 0; continue; }
            if (c is UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned or UnicodeCategory.Surrogate || v == 0xFFFD) continue;

            if (TextPolicy.IsTagChar(v)) { hidden++; cfStripped++; suspect = true; continue; }
            if (TextPolicy.IsVariationSelector(v))
            {
                var keep = false;
                if (v is 0xFE0E or 0xFE0F && prevOut is { } b && !TextPolicy.IsVariationSelector(b.Value))
                    keep = b.Value > 0x7F ? !TextPolicy.IsWhitespace(b) && TextPolicy.IsEmojiLike(b) || TextPolicy.Cat(b) == UnicodeCategory.OtherSymbol
                                          : b.Value is >= '0' and <= '9' or '#' or '*';
                if (keep) { output.Add(r); continue; }
                hidden++;
                if (v >= 0xE0100) suspect = true;
                continue;
            }
            if (TextPolicy.IsInvisibleFiller(v)) { hidden++; continue; }
            if (c == UnicodeCategory.Format)
            {
                if (v == 0x00AD) continue;                                                  // soft hyphen: ordinary, not a signal
                if (v == 0xFEFF && i == 0) continue;                                        // a lone BOM at the start: ordinary
                if (v is 0x200C or 0x200D && prevOut is { } p && nextIn is { } n
                    && (TextPolicy.IsLetterLike(TextPolicy.Cat(p)) || TextPolicy.IsMark(TextPolicy.Cat(p)) || p.Value == 0xFE0F || (p.Value > 0x7F && TextPolicy.IsEmojiLike(p)))
                    && (TextPolicy.IsLetterLike(TextPolicy.Cat(n)) || TextPolicy.IsMark(TextPolicy.Cat(n)) || (n.Value > 0x7F && TextPolicy.IsEmojiLike(n)))
                    && p.Value is not (0x200C or 0x200D))
                { output.Add(r); marks = 0; continue; }
                hidden++; cfStripped++;
                if (TextPolicy.IsBidiOverrideOrIsolate(v)) suspect = true;
                continue;
            }
            if (TextPolicy.IsMark(c))
            {
                if (prevOut is not { } pm || TextPolicy.IsWhitespace(pm) || pm.Value == '\n' || ++marks > 4) continue;   // orphan / excess marks dropped
                output.Add(r); continue;
            }
            marks = 0;
            output.Add(r);
        }
        if (cfStripped > 8) suspect = true;

        // Collapse space runs, trim each line, drop leading/trailing blank lines, keep at most one blank line in a row.
        var sb = new StringBuilder(output.Count);
        foreach (var r in output) sb.Append(r.ToString());
        var lines = sb.ToString().Split('\n');
        var result = new StringBuilder(sb.Length);
        var blankRun = 0; var wroteAny = false;
        foreach (var raw1 in lines)
        {
            var line = CollapseSpaces(raw1).Trim(' ');
            if (line.Length == 0)
            {
                if (wroteAny) blankRun++;
                continue;
            }
            if (wroteAny) { result.Append('\n'); if (blankRun > 0) result.Append('\n'); }
            blankRun = 0; wroteAny = true;
            result.Append(line);
        }
        // SBV-04: stripping can bring a letter and its mark together (letter, ZWSP, U+0301), or leave marks out of canonical order, so the
        // stripped text is NFC-normalized once more. This is the final string: every offset, line span, snippet slice (SBV-01) and the paste
        // content hash is computed from it. NFC only composes or reorders, it cannot bring back a stripped character, a space run or an
        // edge space, so a second Canonicalize pass changes nothing (idempotent). Text that was already NFC is unchanged, so its hash is too.
        var text = result.ToString();
        if (!text.IsNormalized(NormalizationForm.FormC)) text = text.Normalize(NormalizationForm.FormC);
        return new CanonicalDocument(text, hidden, suspect);
    }

    private static string CollapseSpaces(string s)
    {
        if (!s.Contains("  ", StringComparison.Ordinal)) return s;
        var sb = new StringBuilder(s.Length);
        var prevSpace = false;
        foreach (var ch in s)
        {
            if (ch == ' ') { if (prevSpace) continue; prevSpace = true; } else prevSpace = false;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>UTF-16 spans of the non-empty lines of canonical text (start inclusive, end exclusive).</summary>
    public static List<(int Start, int End)> LineSpans(string text)
    {
        var spans = new List<(int, int)>();
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || text[i] == '\n')
            {
                if (i > start) spans.Add((start, i));
                start = i + 1;
            }
        }
        return spans;
    }
}
