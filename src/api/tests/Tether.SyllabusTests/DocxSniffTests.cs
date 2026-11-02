using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Tether.Syllabus.Application;

namespace Tether.SyllabusTests;

/// <summary>
/// DR-04 (docs/architecture/document-reader.md): the intake sniff used to accept a DOCX only when <c>[Content_Types].xml</c> was the FIRST
/// local entry, so a valid package written by a tool that orders entries differently was refused with 415. Contract API_CONTRACTS_P2 5.1
/// item 1 says DOCX = ZIP with <c>[Content_Types].xml</c> and <c>word/document.xml</c>; the sniff now looks for both anywhere in the central
/// directory, read from a bounded tail window while streaming. The deep checks (bombs, unsafe paths, macros, OLE) stay in the sandboxed reader.
/// All packages are synthetic and built in memory here (no project reference to the reader's test fixtures).
/// </summary>
public class DocxSniffTests
{
    private const string Ct = "[Content_Types].xml";
    private const string Doc = "word/document.xml";

    private static readonly string[] Standard = [Ct, "_rels/.rels", Doc, "word/_rels/document.xml.rels", "word/styles.xml", "docProps/core.xml", "docProps/app.xml"];

    /// <summary>A synthetic package with the entries in exactly this order (System.IO.Compression writes them in the order created).</summary>
    internal static byte[] Zip(IEnumerable<string> names, Func<string, byte[]>? data = null, string? comment = null, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (comment is not null) zip.Comment = comment;
            foreach (var n in names)
            {
                var e = zip.CreateEntry(n, level);
                using var s = e.Open();
                s.Write(data?.Invoke(n) ?? Encoding.UTF8.GetBytes("<x>Synthetic Course 101</x>"));
            }
        }
        return ms.ToArray();
    }

    private static string[] With(string name, int index)
    {
        var rest = Standard.Where(n => n != name).ToList();
        rest.Insert(index < 0 ? rest.Count : index, name);
        return [.. rest];
    }

    private static Task<(SniffKind Kind, string? Sha, byte[]? TextBytes, Microsoft.AspNetCore.Http.IResult? Error)> ReadAsync(byte[] bytes, int chunk = 64 * 1024, long? max = null) =>
        UploadIntake.ReadFileAsync(new Trickle(bytes, chunk), new SyllabusOptions { UploadMaxBytes = max ?? 10L * 1024 * 1024 }, CancellationToken.None);

    public static TheoryData<string, string[]> ValidOrders => new()
    {
        { "content types first (Word)", Standard },
        { "content types in the middle", With(Ct, 3) },
        { "content types last", With(Ct, -1) },
        { "document first, content types last", [Doc, .. Standard.Where(n => n is not Ct and not Doc), Ct] },
        { "only the two required parts, reversed", [Doc, Ct] },
        { "directory entries and other parts first", ["word/", "word/media/", "customXml/item1.xml", .. With(Ct, -1)] },
    };

    [Theory]
    [MemberData(nameof(ValidOrders))]
    public async Task DR04_a_valid_package_is_a_docx_wherever_content_types_sits(string name, string[] order)
    {
        var bytes = Zip(order);
        Assert.True(ZipDirectory.HasDocxParts(bytes, bytes.Length), name);
        foreach (var chunk in new[] { 1, 7, 64, 65, 4096, 64 * 1024 })   // chunk boundaries across the head and the tail window
        {
            var r = await ReadAsync(bytes, chunk);
            Assert.Null(r.Error);
            Assert.Equal(SniffKind.Docx, r.Kind);
            Assert.Null(r.TextBytes);                                         // DOCX bytes are never kept as text
            Assert.Equal(64, r.Sha!.Length);
        }
    }

    [Fact]
    public void DR04_part_names_match_ascii_case_insensitively_like_the_reader()
    {
        var bytes = Zip(["Word/Document.XML", "_rels/.rels", "[content_types].xml"]);
        Assert.True(ZipDirectory.HasDocxParts(bytes, bytes.Length));
    }

    public static TheoryData<string, string[]> MissingParts => new()
    {
        { "no content types", [.. Standard.Where(n => n != Ct)] },
        { "no main document", [.. Standard.Where(n => n != Doc)] },
        { "neither", ["_rels/.rels", "docProps/core.xml"] },
        { "content types in a folder", ["sub/[Content_Types].xml", Doc] },
        { "document under a parent path", [Ct, "../word/document.xml"] },
        { "document as an absolute path", [Ct, "/word/document.xml"] },
        { "document with a backslash", [Ct, "word\\document.xml"] },
        { "lookalike names", ["[Content_Types].xml.bak", Ct + "x", "xword/document.xml", "word/document.xml.rels"] },
        { "a directory named like the part", [Ct, "word/document.xml/"] },
        { "an ordinary zip", ["readme.txt", "data.csv"] },
    };

    [Theory]
    [MemberData(nameof(MissingParts))]
    public async Task DR04_a_zip_without_both_required_parts_is_unsupported(string name, string[] order)
    {
        var bytes = Zip(order);
        Assert.False(ZipDirectory.HasDocxParts(bytes, bytes.Length), name);
        var r = await ReadAsync(bytes);
        Assert.NotNull(r.Error);                                               // 415 unsupported_type at the route
        Assert.Null(r.Sha);
    }

    [Fact]
    public async Task DR04_a_local_header_with_no_central_directory_is_unsupported_including_the_old_fake_docx()
    {
        // The pre-DR-04 fake: a local header naming [Content_Types].xml and nothing else (no central directory, no end record).
        var fake = new byte[200];
        "PK\x03\x04"u8.CopyTo(fake); fake[26] = 19; "[Content_Types].xml"u8.CopyTo(fake.AsSpan(30));
        foreach (var bytes in new[] { fake, [.. "PK\x03\x04"u8, .. new byte[100]], "PK\x03\x04"u8.ToArray(), [.. "PK\x03\x04"u8, .. new byte[18]] })
        {
            Assert.False(ZipDirectory.HasDocxParts(bytes, bytes.Length));
            Assert.NotNull((await ReadAsync(bytes)).Error);
        }
    }

    // ---- hostile or malformed central directories: always unsupported, never an exception ----

    private static byte[] Valid() => Zip(With(Ct, -1));

    private static int EocdAt(byte[] b) => b.AsSpan().LastIndexOf("PK\x05\x06"u8);

    public static TheoryData<string, Func<byte[], byte[]>> Malformed => new()
    {
        { "entry count larger than the directory", b => { var c = (byte[])b.Clone(); var i = EocdAt(c); BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(i + 8), 9); BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(i + 10), 9); return c; } },
        { "entry count smaller than the directory", b => { var c = (byte[])b.Clone(); var i = EocdAt(c); BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(i + 8), 2); BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(i + 10), 2); return c; } },
        { "disk counts disagree", b => { var c = (byte[])b.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(EocdAt(c) + 8), 1); return c; } },
        { "multi-disk", b => { var c = (byte[])b.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(EocdAt(c) + 4), 1); return c; } },
        { "zip64 marker counts", b => { var c = (byte[])b.Clone(); var i = EocdAt(c); BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(i + 8), 0xFFFF); BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(i + 10), 0xFFFF); return c; } },
        { "zip64 marker offset", b => { var c = (byte[])b.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(EocdAt(c) + 16), 0xFFFFFFFF); return c; } },
        { "directory offset past the end record", b => { var c = (byte[])b.Clone(); var i = EocdAt(c); BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(i + 16), (uint)i + 4); return c; } },
        { "directory size wrong", b => { var c = (byte[])b.Clone(); var i = EocdAt(c); BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(i + 12), BinaryPrimitives.ReadUInt32LittleEndian(c.AsSpan(i + 12)) - 1); return c; } },
        { "comment length lies", b => { var c = (byte[])b.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(EocdAt(c) + 20), 40); return c; } },
        { "central header signature broken", b => { var c = (byte[])b.Clone(); var i = EocdAt(c); var cd = (int)BinaryPrimitives.ReadUInt32LittleEndian(c.AsSpan(i + 16)); c[cd + 3] = 0; return c; } },
        { "name length runs past the directory", b => { var c = (byte[])b.Clone(); var i = EocdAt(c); var cd = (int)BinaryPrimitives.ReadUInt32LittleEndian(c.AsSpan(i + 16)); BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(cd + 28), 0xFFFF); return c; } },
        { "zero-length name", b => { var c = (byte[])b.Clone(); var i = EocdAt(c); var cd = (int)BinaryPrimitives.ReadUInt32LittleEndian(c.AsSpan(i + 16)); BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(cd + 28), 0); return c; } },
        { "local header offset inside the directory", b => { var c = (byte[])b.Clone(); var i = EocdAt(c); var cd = BinaryPrimitives.ReadUInt32LittleEndian(c.AsSpan(i + 16)); BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan((int)cd + 42), cd); return c; } },
        { "bytes after the end record", b => [.. b, 0x00] },
        { "end record truncated", b => b[..^1] },
        { "directory truncated", b => { var i = EocdAt(b); return [.. b[..(i - 10)], .. b[i..]]; } },
        { "garbage prepended to the directory start", b => { var i = EocdAt(b); var cd = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i + 16)); return [.. b[..cd], 0x00, .. b[cd..]]; } },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task DR04_a_malformed_central_directory_is_unsupported_and_never_throws(string name, Func<byte[], byte[]> mutate)
    {
        var good = Valid();
        Assert.True(ZipDirectory.HasDocxParts(good, good.Length));
        var bad = mutate(good);
        Assert.False(ZipDirectory.HasDocxParts(bad, bad.Length), name);
        Assert.NotNull((await ReadAsync(bad)).Error);
    }

    [Fact]
    public void DR04_an_archive_comment_up_to_the_zip_maximum_is_accepted()
    {
        foreach (var len in new[] { 1, 100, 65_535 })
        {
            var bytes = Zip(With(Ct, -1), comment: new string('c', len));
            Assert.True(ZipDirectory.HasDocxParts(bytes, bytes.Length), $"comment {len}");
        }
    }

    [Fact]
    public void DR04_entry_count_and_name_length_follow_the_readers_limits()
    {
        var at = Zip([.. Enumerable.Range(0, ZipDirectory.MaxEntries - 2).Select(i => $"p/{i}.xml"), Doc, Ct], _ => []);
        Assert.True(ZipDirectory.HasDocxParts(at, at.Length));                                                          // 1,000 entries
        var over = Zip([.. Enumerable.Range(0, ZipDirectory.MaxEntries - 1).Select(i => $"p/{i}.xml"), Doc, Ct], _ => []);
        Assert.False(ZipDirectory.HasDocxParts(over, over.Length));                                                     // 1,001

        var longOk = Zip([Ct, Doc, "a/" + new string('n', ZipDirectory.MaxNameBytes - 2)]);
        Assert.True(ZipDirectory.HasDocxParts(longOk, longOk.Length));                                                  // 260 bytes
        var longBad = Zip([Ct, Doc, "a/" + new string('n', ZipDirectory.MaxNameBytes - 1)]);
        Assert.False(ZipDirectory.HasDocxParts(longBad, longBad.Length));                                               // 261 bytes
    }

    [Fact]
    public async Task DR04_a_large_package_is_sniffed_from_the_tail_window_and_a_directory_larger_than_the_window_is_unsupported()
    {
        // 1.5 MB of incompressible data first, then the parts, content types last: the head is far from the directory.
        var noise = new byte[1_500_000];
        new Random(42).NextBytes(noise);
        var big = Zip(["media/noise.bin", .. With(Ct, -1)], n => n == "media/noise.bin" ? noise : Encoding.UTF8.GetBytes("<x/>"), level: CompressionLevel.NoCompression);
        Assert.True(big.Length > ZipDirectory.TailBytes * 2);
        foreach (var chunk in new[] { 1000, 64 * 1024 })
        {
            var r = await ReadAsync(big, chunk);
            Assert.Null(r.Error);
            Assert.Equal(SniffKind.Docx, r.Kind);
        }

        // Only the window is ever looked at: the same file seen through a window that ends before the directory starts is refused.
        var i = EocdAt(big);
        var cdSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(big.AsSpan(i + 12));
        var tooSmall = big.AsSpan(big.Length - (big.Length - i) - cdSize + 1);
        Assert.False(ZipDirectory.HasDocxParts(tooSmall, big.Length));
        var exact = big.AsSpan(big.Length - (big.Length - i) - cdSize);
        Assert.True(ZipDirectory.HasDocxParts(exact, big.Length));
    }

    [Fact]
    public async Task DR04_the_streaming_cap_still_applies_to_a_package()
    {
        var bytes = Valid();
        var r = await ReadAsync(bytes, 512, max: bytes.Length - 1);
        Assert.NotNull(r.Error);                                               // 413 too_large, counted while reading
        Assert.Null(r.Sha);
    }

    [Fact]
    public async Task Pdf_and_text_sniffing_are_unchanged()
    {
        var pdf = (await ReadAsync("%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n"u8.ToArray(), 7));
        Assert.Equal(SniffKind.Pdf, pdf.Kind); Assert.Null(pdf.Error); Assert.Null(pdf.TextBytes);
        var text = await ReadAsync(Encoding.UTF8.GetBytes("Quiz due Oct 20, 2026 at 9:00 AM. " + new string('x', 300)), 5);
        Assert.Equal(SniffKind.Text, text.Kind); Assert.Null(text.Error); Assert.Equal(334, text.TextBytes!.Length);
        var shortText = await ReadAsync("Hi"u8.ToArray());
        Assert.Equal(SniffKind.Text, shortText.Kind); Assert.Equal(2, shortText.TextBytes!.Length);
        Assert.NotNull((await ReadAsync([])).Error);
    }

    /// <summary>Returns at most <c>chunk</c> bytes per read, so the head, the type decision and the tail window cross read boundaries.</summary>
    private sealed class Trickle(byte[] data, int chunk) : Stream
    {
        private int _pos;
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(Math.Min(count, chunk), data.Length - _pos);
            Array.Copy(data, _pos, buffer, offset, n); _pos += n;
            return n;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var n = Math.Min(Math.Min(buffer.Length, chunk), data.Length - _pos);
            data.AsSpan(_pos, n).CopyTo(buffer.Span); _pos += n;
            return ValueTask.FromResult(n);
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
