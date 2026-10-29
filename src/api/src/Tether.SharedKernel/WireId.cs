namespace Tether.SharedKernel;

/// <summary>
/// The one canonical wire form of an id (docs/API_CONTRACTS_P2.md decision 2 and section 2.1, R2-05): an optional three letter
/// lowercase prefix and an underscore, then a lowercase hyphenated GUID (<c>Guid.ToString("D")</c>), 64 characters at most.
/// Parsing is strict: the input is parsed and then re-formatted, and the two strings must be ordinally equal. That single rule
/// rejects upper case, braces, <c>urn:uuid:</c>, missing hyphens, surrounding whitespace, a trailing newline and a wrong prefix,
/// so no alias spelling can reach a lookup. Only the API edge uses this; domain, EF and jobs use <see cref="Guid"/>.
/// </summary>
public static class WireId
{
    /// <summary>Formats an id. A null prefix gives the bare lowercase hyphenated GUID (the form Courses and Lectures already use).</summary>
    public static string Format(string? prefix, Guid id) =>
        prefix is null ? id.ToString("D") : prefix + "_" + id.ToString("D");

    /// <summary>
    /// True only if <paramref name="s"/> is exactly the canonical text of a GUID id with <paramref name="prefix"/> (null means no prefix).
    /// A malformed id and a well-formed id that does not exist are told apart only by form, never by whether a row exists.
    /// </summary>
    public static bool TryParse(string? prefix, string? s, out Guid id)
    {
        id = Guid.Empty;
        if (s is null) return false;
        var expectedLength = (prefix is null ? 0 : prefix.Length + 1) + 36;
        if (s.Length != expectedLength || expectedLength > 64) return false;
        var guidPart = s;
        if (prefix is not null)
        {
            if (!s.StartsWith(prefix, StringComparison.Ordinal) || s[prefix.Length] != '_') return false;
            guidPart = s[(prefix.Length + 1)..];
        }
        if (!Guid.TryParseExact(guidPart, "D", out var g)) return false;
        if (!string.Equals(Format(prefix, g), s, StringComparison.Ordinal)) return false; // round trip: no alias spelling
        id = g;
        return true;
    }
}
