using System.Security;
using System.Text;
using Tether.DocReader;

namespace Tether.DocReaderTests.Fixtures;

/// <summary>
/// Builds synthetic DOCX packages in memory (docs/architecture/document-reader.md section 3.1). All text is synthetic ("Synthetic Course
/// 101"); the only name in any metadata is <see cref="AuthorPlaceholder"/>, which tests assert is never returned. Every package carries
/// <see cref="Canary"/> in a body cell or paragraph so a later log-canary suite can reuse the same fixtures.
/// </summary>
internal sealed class DocxBuilder
{
    public const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    public const string Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    public const string Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    public const string V = "urn:schemas-microsoft-com:vml";
    public const string RelTypeBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    public const string MainType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";

    /// <summary>The only "person" in any generated metadata. Must never appear in reader output.</summary>
    public const string AuthorPlaceholder = "SYNTHETIC-AUTHOR-PLACEHOLDER-QX7";
    public const string Canary = "TETHER-DOCX-CANARY-2f9a";

    private readonly List<ZipEntrySpec> _entries = [];

    public static string Esc(string s) => SecurityElement.Escape(s);

    public static string P(string text) => $"<w:p><w:r><w:t xml:space=\"preserve\">{Esc(text)}</w:t></w:r></w:p>";

    public static string Run(string text, string rPr = "") =>
        $"<w:r>{(rPr.Length > 0 ? "<w:rPr>" + rPr + "</w:rPr>" : "")}<w:t xml:space=\"preserve\">{Esc(text)}</w:t></w:r>";

    public static string Document(string bodyInner) =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<w:document xmlns:w=\"{W}\" xmlns:r=\"{R}\" xmlns:mc=\"{Mc}\" xmlns:wp=\"{Wp}\" xmlns:wps=\"{Wps}\" xmlns:v=\"{V}\" mc:Ignorable=\"wps\">" +
        $"<w:body>{bodyInner}<w:sectPr/></w:body></w:document>";

    public static string ContentTypes(string extra = "") =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Default Extension=\"png\" ContentType=\"image/png\"/>" +
        $"<Override PartName=\"/word/document.xml\" ContentType=\"{MainType}\"/>" +
        "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
        "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
        "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>" +
        extra + "</Types>";

    public static string Rels(string inner) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{inner}</Relationships>";

    public static string Rel(string id, string type, string target, bool external = false) =>
        $"<Relationship Id=\"{id}\" Type=\"{(type.Contains("://", StringComparison.Ordinal) ? type : RelTypeBase + type)}\" Target=\"{Esc(target)}\"{(external ? " TargetMode=\"External\"" : "")}/>";

    public static string PackageRels(string mainTarget = "word/document.xml", bool mainExternal = false) => Rels(
        Rel("rId1", "officeDocument", mainTarget, mainExternal) +
        Rel("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml") +
        Rel("rId3", "extended-properties", "docProps/app.xml"));

    public static string DocumentRels(string extra = "") => Rels(Rel("rIdStyles", "styles", "styles.xml") + extra);

    public static string Styles(string extra = "") =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<w:styles xmlns:w=\"{W}\">" +
        "<w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val=\"22\"/></w:rPr></w:rPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:rPr><w:b/><w:sz w:val=\"32\"/></w:rPr></w:style>" +
        extra + "</w:styles>";

    /// <summary>Core properties with the placeholder in every person field. The reader must never read this part.</summary>
    public static string Core() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
        "xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
        $"<dc:title>Synthetic title property</dc:title><dc:creator>{AuthorPlaceholder}</dc:creator><cp:lastModifiedBy>{AuthorPlaceholder}</cp:lastModifiedBy>" +
        "<dcterms:created xsi:type=\"dcterms:W3CDTF\">2026-01-01T00:00:00Z</dcterms:created></cp:coreProperties>";

    public static string App() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Company>{AuthorPlaceholder}</Company><Pages>1</Pages></Properties>";

    /// <summary>A complete, valid package whose body is <paramref name="bodyInner"/> (raw WordprocessingML inside <c>w:body</c>).</summary>
    public static DocxBuilder Standard(string bodyInner) => new DocxBuilder()
        .Set("[Content_Types].xml", ContentTypes())
        .Set("_rels/.rels", PackageRels())
        .Set("word/document.xml", Document(bodyInner))
        .Set("word/_rels/document.xml.rels", DocumentRels())
        .Set("word/styles.xml", Styles())
        .Set("docProps/core.xml", Core())
        .Set("docProps/app.xml", App());

    /// <summary>A small ordinary syllabus body with the canary.</summary>
    public static string SyllabusBody() =>
        P("Synthetic Course 101") + P("Exam 1: Oct 19, 2026") + P("Contact: " + Canary);

    public DocxBuilder Set(string name, string xml) => Set(name, Encoding.UTF8.GetBytes(xml));

    public DocxBuilder Set(string name, byte[] data)
    {
        var i = _entries.FindIndex(e => e.Name == name);
        var spec = new ZipEntrySpec { Name = name, Data = data };
        if (i >= 0) _entries[i] = spec; else _entries.Add(spec);
        return this;
    }

    /// <summary>Adds an entry even when the name exists (duplicates, raw specs).</summary>
    public DocxBuilder Add(ZipEntrySpec spec) { _entries.Add(spec); return this; }

    public DocxBuilder Remove(string name) { _entries.RemoveAll(e => e.Name == name); return this; }

    public DocxBuilder Mutate(string name, Action<ZipEntrySpec> change) { change(_entries.Single(e => e.Name == name)); return this; }

    public DocxBuilder MoveToEnd(string name)
    {
        var e = _entries.Single(x => x.Name == name);
        _entries.Remove(e); _entries.Add(e);
        return this;
    }

    public byte[] Build() => TestZip.Write(_entries);

    public DocxResult Read(DocxLimits? limits = null) => Reader.Read(Build(), limits);
}

internal static class Reader
{
    public static DocxResult Read(byte[] bytes, DocxLimits? limits = null) => DocxReader.Read(new MemoryStream(bytes, writable: false), limits ?? DocxLimits.Default);

    /// <summary>Everything the result exposes, as one string, for "never returned" assertions.</summary>
    public static string Everything(DocxResult r) =>
        string.Join("\n", r.Paragraphs) + "\n" + r.Dropped + "\n" + r.Failure + "\n" + r.StatusReason;
}
