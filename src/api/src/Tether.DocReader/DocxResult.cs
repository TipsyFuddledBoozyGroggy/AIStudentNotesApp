namespace Tether.DocReader;

/// <summary>
/// Why a DOCX was refused. Each value maps to one syllabus <c>statusReason</c> (<see cref="DocxFailures.ToStatusReason"/>). The values carry
/// no parser or library text, no part names and nothing from the document (contract 5.1 item 8, section 8 "parser exception text").
/// </summary>
public enum DocxFailure
{
    /// <summary>The input stream is longer than <see cref="DocxLimits.MaxInputBytes"/>.</summary>
    InputTooLarge = 1,
    /// <summary>The input stream could not be read.</summary>
    InputUnreadable,
    /// <summary>The bytes do not start with a ZIP local file header (PDF, text, empty, data before the archive).</summary>
    NotAZip,
    /// <summary>An OLE compound file: a password-encrypted OOXML package or a legacy binary .doc.</summary>
    EncryptedOrLegacyContainer,
    /// <summary>The archive is truncated, corrupt, spanned, uses an unsupported method, or is ambiguous.</summary>
    MalformedArchive,
    /// <summary>More than <see cref="DocxLimits.MaxEntries"/> entries.</summary>
    TooManyEntries,
    /// <summary>An entry name with <c>..</c>, a leading slash, a drive letter, a backslash, a control character, or too deep or long.</summary>
    UnsafeEntryName,
    /// <summary>Two entries whose names are equal ignoring case.</summary>
    DuplicateEntryName,
    /// <summary>An entry with the ZIP encryption flag.</summary>
    EncryptedEntry,
    /// <summary>One entry inflated (or declared) past <see cref="DocxLimits.MaxEntryUncompressedBytes"/>.</summary>
    EntryTooLarge,
    /// <summary>All entries together inflated past <see cref="DocxLimits.MaxTotalUncompressedBytes"/>.</summary>
    TotalTooLarge,
    /// <summary>An entry inflated past <see cref="DocxLimits.MaxCompressionRatio"/> times its compressed size (zip bomb).</summary>
    CompressionRatioExceeded,
    /// <summary>An XML part inflated to a different size than its header declared.</summary>
    SizeMismatch,
    /// <summary>An entry that is itself an archive (ZIP, 7z, RAR).</summary>
    NestedArchive,
    /// <summary>OLE objects, ActiveX, embedded packages, or anything under <c>word/embeddings/</c> or <c>word/activeX/</c>.</summary>
    EmbeddedObject,
    /// <summary>Macro-enabled content types, <c>vbaProject.bin</c>, <c>vbaData.xml</c> or a VBA relationship.</summary>
    MacroContent,
    /// <summary>An <c>aFChunk</c> (altChunk) relationship or element.</summary>
    AltChunk,
    /// <summary><c>[Content_Types].xml</c> is missing.</summary>
    MissingContentTypes,
    /// <summary>No single internal <c>officeDocument</c> relationship resolving to a part in the archive.</summary>
    MissingMainPart,
    /// <summary>The <c>officeDocument</c> relationship is external.</summary>
    ExternalMainPart,
    /// <summary>The main part is not a WordprocessingML document (for example a spreadsheet or presentation).</summary>
    NotWordprocessingDocument,
    /// <summary>A part has a DOCTYPE (DTDs, external entities and entity expansion are prohibited).</summary>
    DtdProhibited,
    /// <summary>XML nested deeper than <see cref="DocxLimits.MaxXmlDepth"/>.</summary>
    XmlTooDeep,
    /// <summary>XML rejected by the parser: malformed, or over <see cref="DocxLimits.MaxXmlCharactersPerPart"/>.</summary>
    InvalidXml,
    /// <summary>More than <see cref="DocxLimits.MaxTextCharacters"/> characters of run text; the reader stopped during extraction.</summary>
    TextTooLong,
    /// <summary>The runtime ran out of memory or stack while reading.</summary>
    ResourceExhausted,
    /// <summary>Any other failure. Never carries the exception or its message.</summary>
    Unexpected,
}

/// <summary>Maps a <see cref="DocxFailure"/> to the syllabus.v1 <c>statusReason</c> vocabulary (design section 2.4).</summary>
public static class DocxFailures
{
    /// <summary>Syllabus <c>statusReason</c> for a refused or broken file.</summary>
    public const string UnreadableFile = "unreadable_file";
    /// <summary>Syllabus <c>statusReason</c> for a file over the text or byte cap.</summary>
    public const string TooLong = "too_long";

    /// <summary>
    /// <see cref="DocxFailure.TextTooLong"/> and <see cref="DocxFailure.InputTooLarge"/> are <c>too_long</c>; every other value is
    /// <c>unreadable_file</c>, including the decompression caps (a bomb is not a long syllabus, design rule 1) and encrypted containers
    /// (design rule 1 and H17; whether an encrypted DOCX should say <c>password_protected</c> instead is open for the Lead).
    /// </summary>
    public static string ToStatusReason(this DocxFailure failure) =>
        failure is DocxFailure.TextTooLong or DocxFailure.InputTooLarge ? TooLong : UnreadableFile;
}

/// <summary>
/// What the reader left out. <see cref="HiddenContentRemoved"/> counts only content that a reader of the document body would not see
/// and that could hide meaning (PO default DR-CONTRACT-META; design rule 6). The other counts are informational: they describe content
/// that was out of scope or not text, so the caller can surface it without disabling batch approval. Document properties (author,
/// title, dates) are never read, never counted and never returned.
/// </summary>
public sealed record DocxDropCounts
{
    /// <summary>Runs hidden by <c>w:vanish</c> or <c>w:specVanish</c>, directly or through a style.</summary>
    public int HiddenRuns { get; init; }
    /// <summary>Runs under 1 pt (<c>w:sz</c> below 2 half-points), directly or through a style.</summary>
    public int TinyRuns { get; init; }
    /// <summary>Tracked deletions (<c>w:del</c>, <c>w:moveFrom</c>) that held text. Tracked insertions are kept as the final view.</summary>
    public int TrackedDeletions { get; init; }
    /// <summary>Distinct comment anchors in the body. Comment text is never read.</summary>
    public int Comments { get; init; }

    /// <summary>Relationships with an external target (hyperlinks, linked images, attached templates). Never resolved or fetched.</summary>
    public int ExternalRelationshipsIgnored { get; init; }
    /// <summary>Field instructions (<c>w:instrText</c>, <c>w:fldSimple</c>) dropped; the displayed field result is kept.</summary>
    public int FieldCodesDropped { get; init; }
    /// <summary>Images or shapes with alt text or a title; alt text is not visible on the page and is not read.</summary>
    public int AltTextDropped { get; init; }
    /// <summary><c>mc:Fallback</c> branches skipped because a <c>mc:Choice</c> branch was read (avoids text boxes appearing twice).</summary>
    public int AlternateContentFallbacksSkipped { get; init; }
    /// <summary>Footnote and endnote references in the body; note text is out of scope (PO default DR-SCOPE: main body only).</summary>
    public int NoteReferencesSkipped { get; init; }
    /// <summary>Header and footer parts of the main document; out of scope (PO default DR-SCOPE: main body only).</summary>
    public int HeaderFooterPartsSkipped { get; init; }
    /// <summary><c>w:bdo</c> and <c>w:dir</c> elements: text whose display order is forced, so the extracted order may differ from what is shown.</summary>
    public int DirectionOverrides { get; init; }

    /// <summary>The value for <c>document.hiddenContentRemoved</c> from the reader (the canonicaliser adds its own count on the trusted side).</summary>
    public int HiddenContentRemoved => HiddenRuns + TinyRuns + TrackedDeletions + Comments;
}

/// <summary>The outcome of <see cref="DocxReader.Read"/>: text paragraphs in reading order, or a typed failure. Never both.</summary>
public sealed class DocxResult
{
    private static readonly DocxDropCounts NoCounts = new();

    private DocxResult(DocxFailure? failure, IReadOnlyList<string> paragraphs, DocxDropCounts dropped)
    {
        Failure = failure;
        Paragraphs = paragraphs;
        Dropped = dropped;
    }

    /// <summary>True when the document was read.</summary>
    public bool Succeeded => Failure is null;

    /// <summary>Why the file was refused, or null on success.</summary>
    public DocxFailure? Failure { get; }

    /// <summary>The syllabus <c>statusReason</c> for <see cref="Failure"/>, or null on success.</summary>
    public string? StatusReason => Failure?.ToStatusReason();

    /// <summary>
    /// Non-empty paragraphs of the main body in reading order. A table row is one paragraph with cells joined by a tab (paragraphs inside a
    /// cell joined by a space); <c>w:br</c> and <c>w:cr</c> are a line feed inside a paragraph; <c>w:tab</c> is a tab. Text is raw: the trusted
    /// side canonicalises it (bidi controls and zero-width characters are kept here so the canonicaliser can count them). Empty on failure.
    /// </summary>
    public IReadOnlyList<string> Paragraphs { get; }

    /// <summary>What was dropped. All zero on failure.</summary>
    public DocxDropCounts Dropped { get; }

    internal static DocxResult Ok(List<string> paragraphs, DocxDropCounts dropped) => new(null, paragraphs.AsReadOnly(), dropped);

    internal static DocxResult Fail(DocxFailure failure) => new(failure, Array.Empty<string>(), NoCounts);
}
