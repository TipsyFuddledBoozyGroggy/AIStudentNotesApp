using System.Xml;

namespace Tether.DocReader.Internal;

/// <summary>Internal control flow only: thrown inside the reader and turned into a <see cref="DocxResult"/> at the boundary. Carries no text.</summary>
internal sealed class DocxRejectedException(DocxFailure reason) : Exception
{
    public DocxFailure Reason { get; } = reason;
}

internal static class Ns
{
    public const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public const string WStrict = "http://purl.oclc.org/ooxml/wordprocessingml/main";
    public const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    public const string Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    public const string WpStrict = "http://purl.oclc.org/ooxml/drawingml/wordprocessingDrawing";
    public const string Vml = "urn:schemas-microsoft-com:vml";
    public const string ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    public const string Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    public const string OfficeDocument = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    public const string OfficeDocumentStrict = "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument";
    public const string MainContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";

    public static bool IsW(string ns) => ns == W || ns == WStrict;
    public static bool IsWp(string ns) => ns == Wp || ns == WpStrict;
}

internal static class Signatures
{
    public static ReadOnlySpan<byte> ZipLocal => [0x50, 0x4B, 0x03, 0x04];
    public static ReadOnlySpan<byte> ZipEmpty => [0x50, 0x4B, 0x05, 0x06];
    public static ReadOnlySpan<byte> ZipSpanned => [0x50, 0x4B, 0x07, 0x08];
    public static ReadOnlySpan<byte> Cfb => [0xD0, 0xCF, 0x11, 0xE0];
    public static ReadOnlySpan<byte> SevenZip => [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    public static ReadOnlySpan<byte> Rar => [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07];

    public static bool IsArchive(ReadOnlySpan<byte> head) =>
        head.StartsWith(ZipLocal) || head.StartsWith(ZipEmpty) || head.StartsWith(ZipSpanned) || head.StartsWith(SevenZip) || head.StartsWith(Rar);
}

/// <summary>The one XmlReaderSettings every part is parsed with (design rule 4, contract 5.1 item 4).</summary>
internal static class SafeXml
{
    public static XmlReaderSettings Settings(DocxLimits limits) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,         // a DOCTYPE throws: no DTD, no external or internal entity, no billion laughs
        XmlResolver = null,                             // nothing is ever resolved
        // Note: 0 means "no limit" for both character settings, so the entity limit is a small positive number. Predefined entities
        // (&amp; and friends) and character references do not count against it; with DTDs prohibited no other entity can exist.
        MaxCharactersFromEntities = 1024,
        MaxCharactersInDocument = limits.MaxXmlCharactersPerPart,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = false,
        CheckCharacters = true,
        CloseInput = false,
        ConformanceLevel = ConformanceLevel.Document,
        ValidationType = ValidationType.None,
    };
}

/// <summary>An XmlReader wrapper whose every Read enforces the element depth cap, including inside subtrees the caller skips.</summary>
internal sealed class XmlCursor(XmlReader reader, int maxDepth)
{
    public XmlReader R { get; } = reader;

    public bool Read()
    {
        if (!R.Read()) return false;
        if (R.NodeType == XmlNodeType.Element && R.Depth > maxDepth) throw new DocxRejectedException(DocxFailure.XmlTooDeep);
        return true;
    }

    /// <summary>At an element start: reads to its end (depth-checked). Returns true if the subtree held any non-whitespace text.</summary>
    public bool Skip(char[] scratch)
    {
        if (R.NodeType != XmlNodeType.Element || R.IsEmptyElement) return false;
        var depth = R.Depth;
        var sawText = false;
        while (Read())
        {
            if (R.NodeType == XmlNodeType.EndElement && R.Depth == depth) return sawText;
            if (!sawText && R.NodeType is XmlNodeType.Text or XmlNodeType.CDATA) sawText = HasNonWhitespace(scratch);
        }
        return sawText;
    }

    /// <summary>Drains the current text node in chunks (never materialising a huge node) and reports whether it held non-whitespace.</summary>
    private bool HasNonWhitespace(char[] scratch)
    {
        var found = false;
        if (R.CanReadValueChunk)
        {
            int n;
            while ((n = R.ReadValueChunk(scratch, 0, scratch.Length)) > 0)
                if (!found && scratch.AsSpan(0, n).ContainsAnyExcept(" \t\r\n")) found = true;
            return found;
        }
        return R.Value.AsSpan().ContainsAnyExcept(" \t\r\n");
    }

    /// <summary>The attribute in the element's own namespace (for <c>w:val</c>, <c>w:id</c>), else the unqualified one.</summary>
    public string? Attr(string localName) => R.GetAttribute(localName, R.NamespaceURI) ?? R.GetAttribute(localName);
}
