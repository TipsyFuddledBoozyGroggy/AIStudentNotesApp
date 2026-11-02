namespace Tether.DocReader;

/// <summary>
/// Hard limits for <see cref="DocxReader"/>. Defaults follow docs/architecture/document-reader.md section 1.3 (DOCX rules 1 and 4) and
/// docs/API_CONTRACTS_P2.md sections 5.1 (items 3, 4 and 7) and 10. Every byte limit is applied to bytes actually inflated and counted
/// while reading, never to the sizes a ZIP header declares. Tests lower these values so small fixtures can trip them.
/// </summary>
public sealed record DocxLimits
{
    /// <summary>The production defaults.</summary>
    public static DocxLimits Default { get; } = new();

    /// <summary>Bytes read from the input stream (the 10 MB upload cap, contract section 10). Over it: <see cref="DocxFailure.InputTooLarge"/>.</summary>
    public long MaxInputBytes { get; init; } = 10L * 1024 * 1024;

    /// <summary>ZIP entries, directories included (design: 1,000).</summary>
    public int MaxEntries { get; init; } = 1_000;

    /// <summary>Inflated bytes of one entry, counted while reading (design: 20 MB).</summary>
    public long MaxEntryUncompressedBytes { get; init; } = 20L * 1024 * 1024;

    /// <summary>Inflated bytes across every entry the reader opens, counted while reading (design: 50 MB).</summary>
    public long MaxTotalUncompressedBytes { get; init; } = 50L * 1024 * 1024;

    /// <summary>Inflated-to-compressed ratio of one entry (design: 100:1), checked once the entry passes <see cref="CompressionRatioCheckAfterBytes"/>.</summary>
    public int MaxCompressionRatio { get; init; } = 100;

    /// <summary>The ratio check starts once an entry has inflated this many bytes (design: 1 MB), so small, highly repetitive parts pass.</summary>
    public long CompressionRatioCheckAfterBytes { get; init; } = 1L * 1024 * 1024;

    /// <summary>Path segments in an entry name (design: 8).</summary>
    public int MaxPathDepth { get; init; } = 8;

    /// <summary>UTF-16 length of an entry name (design: 260).</summary>
    public int MaxPathLength { get; init; } = 260;

    /// <summary>XML element depth in any part (design: 256). Enforced on every node the reader passes, skipped subtrees included.</summary>
    public int MaxXmlDepth { get; init; } = 256;

    /// <summary><c>XmlReaderSettings.MaxCharactersInDocument</c> for each XML part (design: 20,000,000).</summary>
    public long MaxXmlCharactersPerPart { get; init; } = 20_000_000;

    /// <summary>
    /// Raw text characters (UTF-16 units) read from <c>w:t</c> runs, visible or dropped, before the reader stops with
    /// <see cref="DocxFailure.TextTooLong"/> (design conflict C-5: raw cap 200,000; the trusted side applies the 50,000 code-point cap to canonical text).
    /// </summary>
    public int MaxTextCharacters { get; init; } = 200_000;

    internal void Validate()
    {
        static void Positive(long v, string name)
        {
            if (v <= 0) throw new ArgumentOutOfRangeException(name, v, "DocxLimits values must be positive.");
        }
        Positive(MaxInputBytes, nameof(MaxInputBytes));
        Positive(MaxEntries, nameof(MaxEntries));
        Positive(MaxEntryUncompressedBytes, nameof(MaxEntryUncompressedBytes));
        Positive(MaxTotalUncompressedBytes, nameof(MaxTotalUncompressedBytes));
        Positive(MaxCompressionRatio, nameof(MaxCompressionRatio));
        Positive(CompressionRatioCheckAfterBytes, nameof(CompressionRatioCheckAfterBytes));
        Positive(MaxPathDepth, nameof(MaxPathDepth));
        Positive(MaxPathLength, nameof(MaxPathLength));
        Positive(MaxXmlDepth, nameof(MaxXmlDepth));
        Positive(MaxXmlCharactersPerPart, nameof(MaxXmlCharactersPerPart));
        Positive(MaxTextCharacters, nameof(MaxTextCharacters));
        if (MaxCompressionRatio > 100_000_000) throw new ArgumentOutOfRangeException(nameof(MaxCompressionRatio), MaxCompressionRatio, "At most 100,000,000.");
        if (MaxInputBytes > int.MaxValue / 2) throw new ArgumentOutOfRangeException(nameof(MaxInputBytes), MaxInputBytes, "The input is held in memory; at most 1 GB.");
    }
}
