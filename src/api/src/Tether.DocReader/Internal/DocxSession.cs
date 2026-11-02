using System.IO.Compression;
using System.Xml;

namespace Tether.DocReader.Internal;

/// <summary>
/// One read of one package, in the order of design section 1.3: (1) the input, bounded; (2) the container signature; (3) every entry name,
/// flag and declared size from the central directory, before any XML; (4) the first bytes of every entry (nested archives, OLE);
/// (5) <c>[Content_Types].xml</c>; (6) every relationship part; (7) the main part and its styles. Everything is in memory: nothing is
/// written anywhere and nothing outside the archive is ever resolved.
/// </summary>
internal sealed class DocxSession(DocxLimits limits)
{
    private readonly InflateBudget _budget = new(limits.MaxTotalUncompressedBytes);
    private readonly Dictionary<string, ZipArchiveEntry> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Counters _counters = new();
    private readonly char[] _scratch = new char[4096];
    private long _inputLength;

    private static DocxRejectedException Reject(DocxFailure reason) => new(reason);

    public DocxResult Run(Stream input)
    {
        var bytes = ReadInput(input);
        _inputLength = bytes.Length;
        var head = bytes.AsSpan();
        if (head.StartsWith(Signatures.Cfb)) throw Reject(DocxFailure.EncryptedOrLegacyContainer);
        if (!head.StartsWith(Signatures.ZipLocal)) throw Reject(DocxFailure.NotAZip);

        ZipArchive zip;
        IReadOnlyCollection<ZipArchiveEntry> entries;
        try
        {
            zip = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read, leaveOpen: false);
            entries = zip.Entries;
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException or ArgumentException or IOException)
        {
            throw Reject(DocxFailure.MalformedArchive);
        }

        using (zip)
        {
            CheckEntries(entries);
            ScanSignatures(entries);
            var types = ReadContentTypes();
            var rels = ReadAllRelationships();
            var main = FindMainPart(rels, types);
            var mainRels = rels.GetValueOrDefault(main.FullName) ?? [];
            var styles = ReadStyles(main.FullName, mainRels);
            var paragraphs = ParsePart(main, x => new BodyExtractor(x, styles, _counters, limits).Run());
            return DocxResult.Ok(paragraphs, _counters.ToCounts());
        }
    }

    private byte[] ReadInput(Stream input)
    {
        try
        {
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            int n;
            while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (ms.Length + n > limits.MaxInputBytes) throw Reject(DocxFailure.InputTooLarge);   // counted while reading
                ms.Write(buffer, 0, n);
            }
            return ms.ToArray();
        }
        catch (Exception e) when (e is IOException or NotSupportedException or ObjectDisposedException)
        {
            throw Reject(DocxFailure.InputUnreadable);
        }
    }

    // ---- (3) names, flags, declared sizes -----------------------------------------------------------------------------------

    private void CheckEntries(IReadOnlyCollection<ZipArchiveEntry> entries)
    {
        if (entries.Count > limits.MaxEntries) throw Reject(DocxFailure.TooManyEntries);
        foreach (var e in entries)
        {
            var name = e.FullName;
            if (!IsSafeName(name)) throw Reject(DocxFailure.UnsafeEntryName);
            if (!_byName.TryAdd(name, e)) throw Reject(DocxFailure.DuplicateEntryName);
            if (e.IsEncrypted) throw Reject(DocxFailure.EncryptedEntry);
            if (e.Length > limits.MaxEntryUncompressedBytes || e.Length < 0) throw Reject(DocxFailure.EntryTooLarge);   // early, on the header; real bytes are counted later
            var file = name[(name.LastIndexOf('/') + 1)..];
            if (file.Equals("vbaProject.bin", StringComparison.OrdinalIgnoreCase) || file.Equals("vbaData.xml", StringComparison.OrdinalIgnoreCase))
                throw Reject(DocxFailure.MacroContent);
            if (name.StartsWith("word/embeddings/", StringComparison.OrdinalIgnoreCase) || name.StartsWith("word/activeX/", StringComparison.OrdinalIgnoreCase))
                throw Reject(DocxFailure.EmbeddedObject);
        }
    }

    private bool IsSafeName(string name)
    {
        if (name.Length == 0 || name.Length > limits.MaxPathLength) return false;
        foreach (var c in name)
            if (c < 0x20 || c == 0x7F || c == '\\' || c == ':') return false;    // controls and NUL, backslash paths, drive letters and streams
        if (name[0] == '/') return false;
        var trimmed = name.EndsWith('/') ? name[..^1] : name;                      // a directory entry
        var segments = trimmed.Split('/');
        if (segments.Length > limits.MaxPathDepth) return false;
        foreach (var s in segments)
            if (s.Length == 0 || s == "." || s == "..") return false;
        return true;
    }

    // ---- (4) first bytes of every entry -------------------------------------------------------------------------------------

    private void ScanSignatures(IReadOnlyCollection<ZipArchiveEntry> entries)
    {
        var head = new byte[8];
        foreach (var e in entries)
        {
            int n;
            using (var s = Open(e))
            {
                try { n = s.ReadAtLeast(head, head.Length, throwOnEndOfStream: false); }
                catch (InvalidDataException) { throw Reject(DocxFailure.MalformedArchive); }
            }
            var span = head.AsSpan(0, n);
            if (Signatures.IsArchive(span)) throw Reject(DocxFailure.NestedArchive);
            if (span.StartsWith(Signatures.Cfb)) throw Reject(DocxFailure.EmbeddedObject);
        }
    }

    // ---- (5) content types ---------------------------------------------------------------------------------------------------

    private sealed class ContentTypeMap
    {
        public readonly Dictionary<string, string> Defaults = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Overrides = new(StringComparer.OrdinalIgnoreCase);

        public string? For(string partName)
        {
            if (Overrides.TryGetValue("/" + partName, out var t)) return t;
            var dot = partName.LastIndexOf('.');
            return dot >= 0 && Defaults.TryGetValue(partName[(dot + 1)..], out var d) ? d : null;
        }
    }

    private ContentTypeMap ReadContentTypes()
    {
        if (!_byName.TryGetValue("[Content_Types].xml", out var entry)) throw Reject(DocxFailure.MissingContentTypes);
        return ParsePart(entry, x =>
        {
            var map = new ContentTypeMap();
            var r = x.R;
            while (x.Read())
            {
                if (r.NodeType != XmlNodeType.Element || r.Depth != 1 || r.NamespaceURI != Ns.ContentTypes) continue;
                var type = r.GetAttribute("ContentType");
                if (type is null) continue;
                CheckContentType(type);
                if (r.LocalName == "Default" && r.GetAttribute("Extension") is { } ext) AddUnambiguous(map.Defaults, ext, type);
                else if (r.LocalName == "Override" && r.GetAttribute("PartName") is { } part) AddUnambiguous(map.Overrides, part, type);
            }
            return map;
        });
    }

    /// <summary>Two declarations of one key that disagree would let two parsers see two different documents: refuse.</summary>
    private static void AddUnambiguous(Dictionary<string, string> map, string key, string type)
    {
        if (map.TryGetValue(key, out var existing) && !existing.Equals(type, StringComparison.OrdinalIgnoreCase)) throw Reject(DocxFailure.MalformedArchive);
        map[key] = type;
    }

    private static readonly string[] EmbeddedPackageTypes =
    [
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.template",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.template",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.openxmlformats-officedocument.presentationml.slideshow",
        "application/vnd.openxmlformats-officedocument.presentationml.template",
        "application/msword", "application/vnd.ms-excel", "application/vnd.ms-powerpoint",
    ];

    private static void CheckContentType(string type)
    {
        var t = type.Trim();
        if (t.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase) || t.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)
            || t.Contains("vbaData", StringComparison.OrdinalIgnoreCase))
            throw Reject(DocxFailure.MacroContent);
        if (t.Contains("oleObject", StringComparison.OrdinalIgnoreCase) || t.Contains("activeX", StringComparison.OrdinalIgnoreCase)
            || t.Contains("officedocument.package", StringComparison.OrdinalIgnoreCase)
            || EmbeddedPackageTypes.Any(p => t.Equals(p, StringComparison.OrdinalIgnoreCase)))
            throw Reject(DocxFailure.EmbeddedObject);
    }

    // ---- (6) relationships ---------------------------------------------------------------------------------------------------

    private readonly record struct Relationship(string Type, string Target, bool External);

    /// <summary>Every <c>_rels/*.rels</c> part, keyed by its source part name ("" for the package). External targets are counted, never resolved.</summary>
    private Dictionary<string, List<Relationship>> ReadAllRelationships()
    {
        var all = new Dictionary<string, List<Relationship>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, entry) in _byName)
        {
            if (SourceOfRels(name) is not { } source) continue;
            var list = ParsePart(entry, x =>
            {
                var rels = new List<Relationship>();
                var r = x.R;
                while (x.Read())
                {
                    if (r.NodeType != XmlNodeType.Element || r.Depth != 1 || r.LocalName != "Relationship" || r.NamespaceURI != Ns.Relationships) continue;
                    var type = r.GetAttribute("Type") ?? "";
                    var mode = r.GetAttribute("TargetMode");
                    var rel = new Relationship(type, r.GetAttribute("Target") ?? "", mode is not null && mode != "Internal");
                    CheckRelationshipType(type);
                    if (rel.External) _counters.ExternalRelationships++;
                    rels.Add(rel);
                }
                return rels;
            });
            all[source] = list;
        }
        return all;
    }

    /// <summary>"_rels/.rels" is the package's (""); "word/_rels/document.xml.rels" belongs to "word/document.xml"; anything else is not a rels part.</summary>
    private static string? SourceOfRels(string name)
    {
        if (!name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)) return null;
        var slash = name.LastIndexOf('/');
        if (slash < 0) return null;
        var dir = name[..slash];
        var file = name[(slash + 1)..^".rels".Length];
        if (dir.Equals("_rels", StringComparison.OrdinalIgnoreCase)) return file.Length == 0 ? "" : file;
        if (!dir.EndsWith("/_rels", StringComparison.OrdinalIgnoreCase)) return null;
        return dir[..^"/_rels".Length] + "/" + file;
    }

    private static string RelKind(string type) => type[(type.LastIndexOf('/') + 1)..];

    private static void CheckRelationshipType(string type)
    {
        var kind = RelKind(type);
        if (kind.Equals("aFChunk", StringComparison.OrdinalIgnoreCase)) throw Reject(DocxFailure.AltChunk);
        if (kind.Equals("vbaProject", StringComparison.OrdinalIgnoreCase) || kind.Equals("wordVbaData", StringComparison.OrdinalIgnoreCase))
            throw Reject(DocxFailure.MacroContent);
        if (kind.Equals("oleObject", StringComparison.OrdinalIgnoreCase) || kind.Equals("package", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("control", StringComparison.OrdinalIgnoreCase) || kind.Equals("activeXControlBinary", StringComparison.OrdinalIgnoreCase))
            throw Reject(DocxFailure.EmbeddedObject);
    }

    /// <summary>Resolves an internal target against its source's folder. Null when it leaves the package root, names a scheme, or is not a plain path.</summary>
    internal static string? ResolveTarget(string baseDir, string target)
    {
        if (target.Length == 0) return null;
        var t = Uri.UnescapeDataString(target);
        foreach (var c in t)
            if (c < 0x20 || c is '\\' or ':' or '?' or '#') return null;
        var path = t[0] == '/' ? t[1..] : baseDir + t;
        var segments = new List<string>();
        foreach (var s in path.Split('/'))
        {
            if (s.Length == 0 || s == ".") continue;
            if (s == "..")
            {
                if (segments.Count == 0) return null;
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(s);
        }
        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    // ---- (7) main part and styles ---------------------------------------------------------------------------------------------

    private ZipArchiveEntry FindMainPart(Dictionary<string, List<Relationship>> rels, ContentTypeMap types)
    {
        if (!rels.TryGetValue("", out var package)) throw Reject(DocxFailure.MissingMainPart);
        var mains = package.Where(r => r.Type is Ns.OfficeDocument or Ns.OfficeDocumentStrict).ToList();
        if (mains.Count == 0) throw Reject(DocxFailure.MissingMainPart);
        if (mains.Count > 1) throw Reject(DocxFailure.MalformedArchive);
        if (mains[0].External) throw Reject(DocxFailure.ExternalMainPart);
        if (ResolveTarget("", mains[0].Target) is not { } name || !_byName.TryGetValue(name, out var entry)) throw Reject(DocxFailure.MissingMainPart);
        if (!string.Equals(types.For(entry.FullName), Ns.MainContentType, StringComparison.OrdinalIgnoreCase)) throw Reject(DocxFailure.NotWordprocessingDocument);
        return entry;
    }

    private StyleTable ReadStyles(string mainName, List<Relationship> mainRels)
    {
        var dir = mainName.Contains('/') ? mainName[..(mainName.LastIndexOf('/') + 1)] : "";
        StyleTable styles = StyleTable.Empty;
        foreach (var rel in mainRels)
        {
            var kind = RelKind(rel.Type);
            if (kind is "header" or "footer") _counters.HeaderFooterParts++;
            if (kind != "styles" || rel.External || !ReferenceEquals(styles, StyleTable.Empty)) continue;
            if (ResolveTarget(dir, rel.Target) is { } name && _byName.TryGetValue(name, out var entry))
                styles = ParsePart(entry, x => StyleTable.Parse(x, _scratch));
        }
        return styles;
    }

    // ---- bounded part reading -------------------------------------------------------------------------------------------------

    private BoundedEntryStream Open(ZipArchiveEntry e)
    {
        Stream raw;
        try { raw = e.Open(); }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException) { throw Reject(DocxFailure.MalformedArchive); }
        // The compressed size is a header value: never let it exceed the input it must come from, so the ratio check cannot be lied away.
        return new BoundedEntryStream(raw, Math.Min(e.CompressedLength, _inputLength), limits, _budget);
    }

    /// <summary>
    /// Parses one XML part with the safe settings and the depth cursor, reads it to the end, and then checks the inflated size and CRC
    /// against the header (a lying header is refused). Parser exceptions become reasons here and never cross the boundary.
    /// </summary>
    private T ParsePart<T>(ZipArchiveEntry entry, Func<XmlCursor, T> parse)
    {
        using var stream = Open(entry);
        try
        {
            T result;
            using (var reader = XmlReader.Create(stream, SafeXml.Settings(limits)))
            {
                var cursor = new XmlCursor(reader, limits.MaxXmlDepth);
                result = parse(cursor);
                while (cursor.Read()) { }                      // to the end of the part: trailing content is validated and counted
            }
            var tail = new byte[1];
            if (stream.Read(tail, 0, 1) > 0 || stream.BytesRead != entry.Length) throw Reject(DocxFailure.SizeMismatch);
            if (stream.Crc32 != entry.Crc32) throw Reject(DocxFailure.MalformedArchive);
            return result;
        }
        catch (XmlException ex) when (ex.InnerException is DocxRejectedException inner) { throw inner; }
        catch (XmlException) { throw Reject(stream.HeadHasDoctype() ? DocxFailure.DtdProhibited : DocxFailure.InvalidXml); }
        catch (InvalidDataException) { throw Reject(DocxFailure.MalformedArchive); }
    }
}
