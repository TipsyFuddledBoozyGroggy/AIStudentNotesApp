using System.Globalization;
using System.Text;

namespace Tether.Syllabus.Text;

internal enum TextKind { SingleLine, Notes }

/// <summary>
/// The one server TextPolicy for student-supplied text (docs/API_CONTRACTS_P2.md section 2.2). It is defined by Unicode general
/// category and explicit rules, not by a character list, and is authoritative: the schema patterns are only a coarse first filter.
/// Student text is rejected, never silently repaired. Document-derived text goes through <see cref="DocumentText"/>, which strips.
/// Lengths are Unicode code points; only snippet offsets are UTF-16 code units.
/// </summary>
internal static class TextPolicy
{
    /// <summary>
    /// False when the runtime cannot compose: with InvariantGlobalization=true (or no ICU) string.Normalize is a no-op for non-ASCII text, so NFC would
    /// silently not be applied. The repo default is now InvariantGlobalization=false (Directory.Build.props, B1-10) and the module fails startup when this is false.
    /// </summary>
    public static bool NormalizationAvailable { get; } = "e\u0301".Normalize(NormalizationForm.FormC) == "\u00E9";

    /// <summary>Fail closed (B1-10): refuse to run with text that would be stored un-normalised.</summary>
    public static void EnsureNormalizationAvailable(bool available)
    {
        if (!available)
            throw new InvalidOperationException("Unicode normalisation (NFC) is not working on this host: ICU is missing or InvariantGlobalization is enabled. Install libicu in the runtime image and set InvariantGlobalization=false; the Syllabus module will not run with un-normalised text.");
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

    public static bool HasLoneSurrogate(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]))
            {
                if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) return true;
                i++;
            }
            else if (char.IsLowSurrogate(s[i])) return true;
        }
        return false;
    }

    public static UnicodeCategory Cat(Rune r) => Rune.GetUnicodeCategory(r);

    public static bool IsMark(UnicodeCategory c) => c is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
    public static bool IsLetterLike(UnicodeCategory c) => c is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
        or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
        or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber;
    public static bool IsVariationSelector(int v) =>
        v is >= 0xFE00 and <= 0xFE0F or >= 0x180B and <= 0x180D or 0x180F or >= 0xE0100 and <= 0xE01EF;
    public static bool IsTagChar(int v) => v is >= 0xE0000 and <= 0xE007F;
    public static bool IsBidiControl(int v) => v is 0x061C or 0x200E or 0x200F or >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069;
    public static bool IsBidiOverrideOrIsolate(int v) => v is >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069 or 0x061C;
    public static bool IsInvisibleFiller(int v) => v is 0x034F or 0x115F or 0x1160 or 0x3164 or 0xFFA0 or 0x2800 or 0xFFFC;

    /// <summary>Base that may carry a VS15/VS16 or sit beside ZWJ in an emoji sequence: a non-ASCII symbol, or a keycap base.</summary>
    public static bool IsEmojiLike(Rune r)
    {
        var c = Cat(r);
        return (r.Value > 0x7F && c is UnicodeCategory.OtherSymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.MathSymbol)
            || r.Value is 0x200D;
    }

    /// <summary>True if <paramref name="s"/> satisfies the policy; <paramref name="normalized"/> is the NFC value to store.</summary>
    public static bool TryValidate(string? input, TextKind kind, int maxCodePoints, out string normalized)
    {
        normalized = "";
        if (input is null || input.Length == 0 || HasLoneSurrogate(input)) return false;
        // U+FFFD is decoding damage and never legitimate in student text (it cannot arrive from a valid JSON string).
        string s;
        try { s = input.IsNormalized(NormalizationForm.FormC) ? input : input.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return false; }
        if (CodePoints(s) > maxCodePoints) return false;

        var runes = new List<Rune>(s.Length);
        foreach (var r in s.EnumerateRunes()) runes.Add(r);
        if (runes.Count == 0) return false;

        var lines = 1;
        var consecutiveMarks = 0;
        for (var i = 0; i < runes.Count; i++)
        {
            var r = runes[i];
            var v = r.Value;
            var c = Cat(r);
            var prev = i > 0 ? runes[i - 1] : (Rune?)null;
            var next = i + 1 < runes.Count ? runes[i + 1] : (Rune?)null;

            if (v == '\n')
            {
                if (kind != TextKind.Notes) return false;
                if (++lines > 10) return false;
                consecutiveMarks = 0;
                continue;
            }
            if (c == UnicodeCategory.Control) return false;                       // Cc: every C0/C1 control incl. NUL, CR, tab, DEL, U+0085
            if (c is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) return false;
            if (c is UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned or UnicodeCategory.Surrogate) return false;
            if (v == 0xFFFD || IsInvisibleFiller(v) || IsTagChar(v)) return false;
            if (c == UnicodeCategory.Format)
            {
                // ZWNJ and ZWJ only between letters, marks, digits or emoji (Persian, Indic conjuncts, ZWJ emoji).
                if (v is 0x200C or 0x200D)
                {
                    if (prev is not { } p || next is not { } n) return false;
                    var pc = Cat(p); var nc = Cat(n);
                    var prevOk = IsLetterLike(pc) || IsMark(pc) || p.Value == 0xFE0F || (p.Value > 0x7F && IsEmojiLike(p) && p.Value != 0x200D);
                    var nextOk = IsLetterLike(nc) || IsMark(nc) || (n.Value > 0x7F && IsEmojiLike(n) && n.Value != 0x200D);
                    if (!prevOk || !nextOk) return false;
                    if (p.Value is 0x200C or 0x200D) return false;
                    consecutiveMarks = 0;
                    continue;
                }
                return false; // Cf: soft hyphen, bidi marks and controls, BOM, word joiner, musical/other format controls, ...
            }
            if (IsVariationSelector(v))
            {
                if (v is not (0xFE0E or 0xFE0F)) return false;
                // VS15/VS16 only straight after a non-space base: an emoji/symbol base, or a keycap base 0-9 # * (never doubled).
                if (prev is not { } b) return false;
                var bv = b.Value;
                if (IsVariationSelector(bv)) return false;
                if (bv <= 0x7F) { if (!(bv is >= '0' and <= '9' or '#' or '*')) return false; }
                else
                {
                    var bc = Cat(b);
                    if (IsWhitespace(b) || bc is UnicodeCategory.SpaceSeparator) return false;
                }
                continue;
            }

            if (IsWhitespace(r))
            {
                if (kind == TextKind.SingleLine && v != ' ') return false;     // NBSP, U+3000, U+2007 ...: only a single U+0020 between words
                if (kind == TextKind.SingleLine && prev is { Value: ' ' }) return false;   // no runs
                consecutiveMarks = 0;
                continue;
            }
            if (IsMark(c))
            {
                // Orphan combining mark: at the start, after whitespace, or at a line start. More than 4 in a row.
                if (prev is not { } pm || IsWhitespace(pm) || pm.Value == '\n') return false;
                if (++consecutiveMarks > 4) return false;
                continue;
            }
            consecutiveMarks = 0;
        }

        // Leading and trailing whitespace (any Unicode whitespace, LF included) and whole-value whitespace are rejected.
        if (IsWhitespace(runes[0]) || IsWhitespace(runes[^1])) return false;
        normalized = s;
        return true;
    }

    public static bool IsWhitespace(Rune r) => Rune.IsWhiteSpace(r) || Cat(r) == UnicodeCategory.SpaceSeparator;
}
