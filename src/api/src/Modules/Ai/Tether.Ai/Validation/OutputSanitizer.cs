using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tether.Ai.Validation;

/// <summary>
/// Makes model output inert before it is stored or rendered (SEC-024): no remote URLs (including mailto:, tel:, protocol-relative and
/// bare-domain forms), no markdown links or images (which could exfiltrate through an auto-loaded image), no HTML tags, no control,
/// format (zero-width, bidi, tag) characters. The rules are applied repeatedly until the text stops changing, so a payload built by
/// nesting (<c>&lt;&lt;b&gt;script&gt;</c>, <c>[[a](b)](//x)</c>) cannot be rebuilt by the removal of its inner part (B3-01). The client
/// renders everything as plain text; this is defence in depth, not the only line.
/// </summary>
internal static partial class OutputSanitizer
{
    private const int MaxPasses = 16;

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")] private static partial Regex Image();
    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")] private static partial Regex Link();
    [GeneratedRegex(@"</?[A-Za-z!?][^>]*>")] private static partial Regex Tag();
    // Schemes with a payload; mailto/tel/sms are links too. (?i) so JaVaScRiPt: is caught.
    [GeneratedRegex(@"(?i)\b(?:https?|ftp|file|data|javascript|vbscript|mailto|tel|sms|blob|about):[^\s]*|\bwww\.[^\s]+")] private static partial Regex Url();
    // //host/path (protocol-relative). Not preceded by a word character, ':' or '/', and the first character after // is not a space,
    // so a bare "// comment" is left alone.
    [GeneratedRegex(@"(?<![\w:/])//[A-Za-z0-9][^\s]*")] private static partial Regex ProtocolRelative();
    // Bare domains: a listed TLD, or any TLD followed by a path ("evil.example/login").
    [GeneratedRegex(@"(?i)\b(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+(?:(?:com|org|net|edu|gov|io|ai|app|dev|xyz|info|biz|ly|tk|top|site|online|ru|cn|uk|example|invalid|test|local)\b(?:[/?#][^\s]*)?|[a-z]{2,}(?=/)[^\s]*)")] private static partial Regex BareDomain();
    // A '<' still followed by something tag-like after the fixpoint (an unclosed tag): made inert by a look-alike character.
    [GeneratedRegex(@"<(?=/?[A-Za-z!?])")] private static partial Regex LeftoverOpen();
    [GeneratedRegex(@"\s{2,}")] private static partial Regex Spaces();

    public const string LinkRemoved = "[link removed]";

    /// <summary>Returns the cleaned text; <paramref name="changed"/> is true when anything active or non-printable was removed.</summary>
    public static string Clean(string? s, out bool changed)
    {
        changed = false;
        if (string.IsNullOrEmpty(s)) return "";
        var t = StripInvisible(s, ref changed);

        var stable = false;
        for (var pass = 0; pass < MaxPasses && !stable; pass++)
        {
            var before = t;
            t = Image().Replace(t, "");
            t = Link().Replace(t, "$1");
            t = Tag().Replace(t, "");
            t = Url().Replace(t, LinkRemoved);
            t = ProtocolRelative().Replace(t, LinkRemoved);
            t = BareDomain().Replace(t, LinkRemoved);
            stable = t == before;
            if (!stable) changed = true;
        }
        // Not converged (cannot happen with these rules, but never trust a loop bound): drop every bracket character.
        if (!stable) { t = t.Replace("<", "").Replace(">", "").Replace("[", "").Replace("]", "").Replace("(", "").Replace(")", ""); changed = true; }

        var open = LeftoverOpen().Replace(t, "\u2039");
        if (!ReferenceEquals(open, t) && open != t) changed = true;
        return Spaces().Replace(open, " ").Trim();
    }

    /// <summary>Control characters are dropped (tab/newline become a space); so is every Unicode "Format" (Cf) code point: zero-width, bidi and tag characters.</summary>
    private static string StripInvisible(string s, ref bool changed)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var rune in s.EnumerateRunes())
        {
            var cat = Rune.GetUnicodeCategory(rune);
            if (rune.Value is '\t' or '\n' or '\r' || cat is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) { sb.Append(' '); changed = true; }
            else if (cat is UnicodeCategory.Control or UnicodeCategory.Format) changed = true;
            else sb.Append(rune.ToString());
        }
        return sb.ToString();
    }
}
