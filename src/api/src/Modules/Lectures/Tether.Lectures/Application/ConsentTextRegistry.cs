namespace Tether.Lectures.Application;

/// <summary>
/// Server-side registry of consent text versions (C-13). A client-supplied <c>textVersion</c> is only accepted if it is
/// listed here, so a forged or stale version cannot create a consent record. Wording is Product Owner / counsel
/// territory (gate G1, BLOCKED): nothing here is approved text.
/// </summary>
internal sealed class ConsentTextRegistry
{
    public const string DraftV1 = "draft-1";

    /// <summary>
    /// DRAFT, NOT APPROVED. Placeholder wording used only so the flow can be built and tested. It must be replaced by text
    /// approved under gate G1 (recording law, campus policy, vendor terms) before any real classroom use.
    /// </summary>
    public const string DraftV1Text =
        "DRAFT (not legal text): I understand this will record and transcribe audio from my microphone. " +
        "I confirm that recording this course's lectures is allowed by my school and by my instructor. " +
        "I can pause or stop at any time.";

    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal) { [DraftV1] = DraftV1Text };

    /// <summary>Versions the server will accept for new sessions and ticket issuance.</summary>
    public IReadOnlyCollection<string> AcceptedVersions => _texts.Keys;

    public string Current => DraftV1;

    public bool IsAccepted(string? version) => version is not null && _texts.ContainsKey(version);

    public string? TextFor(string version) => _texts.GetValueOrDefault(version);
}
