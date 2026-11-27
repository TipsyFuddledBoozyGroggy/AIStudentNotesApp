using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Tether.Identity.Google;

/// <summary>
/// Tether's own invite list (ADR-022 R3), the inner gate behind Google's Testing test-user list. Read once at startup from
/// <c>Auth:Invite:Emails</c> (comma separated; the PO's set-invites script writes it and restarts the API). Matching is exact after trimming
/// and lower-casing; no Gmail dot or plus folding. Never logged: only the count is.
/// </summary>
internal sealed partial class InviteList
{
    public const string ConfigKey = "Auth:Invite:Emails";
    public const int MaxEntries = 100;

    private readonly HashSet<string> _emails;
    private InviteList(HashSet<string> emails) => _emails = emails;

    public int Count => _emails.Count;
    public static readonly InviteList Empty = new([]);

    [GeneratedRegex(@"^[^\s@,]+@[^\s@,]+\.[^\s@,]+$")]
    private static partial Regex EmailPattern();

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    public bool Contains(string? email) => !string.IsNullOrWhiteSpace(email) && _emails.Contains(Normalize(email));

    /// <summary>Parses and validates the list. Messages carry an entry number, never an address.</summary>
    public static InviteList From(IConfiguration config, bool required)
    {
        var raw = config[ConfigKey] ?? "";
        var set = new HashSet<string>(StringComparer.Ordinal);
        var entries = raw.Split(',');
        var n = 0;
        foreach (var entry in entries)
        {
            var e = Normalize(entry);
            if (e.Length == 0) continue;
            n++;
            if (e.Length > 320 || !EmailPattern().IsMatch(e))
                throw new InvalidOperationException($"{ConfigKey} entry {n} is not an email address.");
            if (!set.Add(e))
                throw new InvalidOperationException($"{ConfigKey} entry {n} is a duplicate.");
        }
        if (set.Count > MaxEntries)
            throw new InvalidOperationException($"{ConfigKey} has more than {MaxEntries} entries (Google's Testing status allows at most {MaxEntries} test users).");
        if (required && set.Count == 0)
            throw new InvalidOperationException($"{ConfigKey} is empty. Google sign-in is invite-only: list at least one address (scripts/azure/set-invites.ps1).");
        return new InviteList(set);
    }
}
