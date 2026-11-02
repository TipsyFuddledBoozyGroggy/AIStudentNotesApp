using System.Diagnostics;
using System.Text;
using Tether.DocReader;
using Tether.DocReaderTests.Fixtures;
using static Tether.DocReaderTests.Fixtures.DocxBuilder;

namespace Tether.DocReaderTests;

/// <summary>
/// Design section 3.1: "one test per bomb type also runs at the production cap to prove the real values hold". Every fixture here is
/// read with <see cref="DocxLimits.Default"/> and stays under the 10 MB input cap.
/// </summary>
public class DocxProductionCapTests
{
    [Fact]
    public void The_defaults_are_the_documented_values()
    {
        var d = DocxLimits.Default;
        Assert.Equal(10L * 1024 * 1024, d.MaxInputBytes);
        Assert.Equal(1_000, d.MaxEntries);
        Assert.Equal(20L * 1024 * 1024, d.MaxEntryUncompressedBytes);
        Assert.Equal(50L * 1024 * 1024, d.MaxTotalUncompressedBytes);
        Assert.Equal(100, d.MaxCompressionRatio);
        Assert.Equal(1L * 1024 * 1024, d.CompressionRatioCheckAfterBytes);
        Assert.Equal(8, d.MaxPathDepth);
        Assert.Equal(260, d.MaxPathLength);
        Assert.Equal(256, d.MaxXmlDepth);
        Assert.Equal(20_000_000, d.MaxXmlCharactersPerPart);
        Assert.Equal(200_000, d.MaxTextCharacters);
    }

    /// <summary>
    /// An XML part with <paramref name="fillerBytes"/> bytes of a skewed random a/b comment before the root closes. The comment is read
    /// through (comments are ignored, not skipped at the byte level), compresses about 10:1 (under the 100:1 ratio), and is not run text.
    /// </summary>
    private static ZipEntrySpec FilledPart(string name, string xml, string closeTag, long fillerBytes, int seed) => new()
    {
        Name = name,
        Precompressed = TestZip.DeflateGenerated(s =>
        {
            var cut = xml.LastIndexOf(closeTag, StringComparison.Ordinal);
            s.Write(Encoding.UTF8.GetBytes(xml[..cut] + "<!--"));
            var rng = new Random(seed);
            var block = new byte[64 * 1024];
            for (var left = fillerBytes; left > 0; left -= block.Length)
            {
                for (var i = 0; i < block.Length; i++) block[i] = rng.Next(10) == 0 ? (byte)'b' : (byte)'a';
                s.Write(block, 0, (int)Math.Min(block.Length, left));
            }
            s.Write(Encoding.UTF8.GetBytes("-->" + xml[cut..]));
        }),
    };

    private static ZipEntrySpec SpaceBomb(int megabytes) => new()
    {
        Name = "word/document.xml",
        Precompressed = TestZip.DeflateGenerated(s =>
        {
            var xml = Document(P("Synthetic"));
            var cut = xml.IndexOf("<w:sectPr/>", StringComparison.Ordinal);
            s.Write(Encoding.UTF8.GetBytes(xml[..cut]));
            var block = new byte[1024 * 1024]; Array.Fill(block, (byte)' ');
            for (var i = 0; i < megabytes; i++) s.Write(block);              // about 1,000:1 compression
            s.Write(Encoding.UTF8.GetBytes(xml[cut..]));
        }),
    };

    [Fact]
    public void Gigabyte_bomb_with_an_honest_header_is_refused_on_the_declared_size_without_inflating()
    {
        var bytes = Standard("").Remove("word/document.xml").Add(SpaceBomb(1024)).Build();
        Assert.True(bytes.Length < DocxLimits.Default.MaxInputBytes, $"fixture is {bytes.Length} bytes");
        var sw = Stopwatch.StartNew();
        var r = Reader.Read(bytes);
        sw.Stop();
        Assert.Equal(DocxFailure.EntryTooLarge, r.Failure);
        Assert.Equal(DocxFailures.UnreadableFile, r.StatusReason);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.Elapsed}");
    }

    [Fact]
    public void Ratio_bomb_that_lies_about_its_size_at_the_production_caps()
    {
        // 50 MB of spaces deflate to about 50 KB; the header claims 10 MB (under the 20 MB cap). The ratio check (100:1 after 1 MB)
        // trips at about 5 MB inflated, before the per-entry cap and before the lie could matter.
        var bomb = SpaceBomb(50);
        bomb.DeclaredUncompressedSize = 10 * 1024 * 1024;
        var bytes = Standard("").Remove("word/document.xml").Add(bomb).Build();
        var sw = Stopwatch.StartNew();
        var r = Reader.Read(bytes);
        sw.Stop();
        Assert.Equal(DocxFailure.CompressionRatioExceeded, r.Failure);
        Assert.Equal(DocxFailures.UnreadableFile, r.StatusReason);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
    }

    [Fact]
    public void Per_entry_cap_at_the_production_caps()
    {
        var doc = FilledPart("word/document.xml", Document(P("Synthetic")), "</w:document>", 21L * 1024 * 1024, 1);
        var bytes = Standard("").Remove("word/document.xml").Add(doc).Build();
        Assert.True(bytes.Length < DocxLimits.Default.MaxInputBytes, $"fixture is {bytes.Length} bytes");
        var r = Reader.Read(bytes);
        Assert.Equal(DocxFailure.EntryTooLarge, r.Failure);
        Assert.Equal(DocxFailures.UnreadableFile, r.StatusReason);
    }

    [Fact]
    public void Total_cap_at_the_production_caps()
    {
        const long each = 18L * 1024 * 1024;   // three parts of 18 MB: each under the 20 MB entry cap, together over 50 MB
        var b = Standard(SyllabusBody())
            .Remove("[Content_Types].xml").Add(FilledPart("[Content_Types].xml", ContentTypes(), "</Types>", each, 2))
            .Remove("_rels/.rels").Add(FilledPart("_rels/.rels", PackageRels(), "</Relationships>", each, 3))
            .Remove("word/_rels/document.xml.rels").Add(FilledPart("word/_rels/document.xml.rels", DocumentRels(), "</Relationships>", each, 4));
        var bytes = b.Build();
        Assert.True(bytes.Length < DocxLimits.Default.MaxInputBytes, $"fixture is {bytes.Length} bytes");
        var r = Reader.Read(bytes);
        Assert.Equal(DocxFailure.TotalTooLarge, r.Failure);
        Assert.Equal(DocxFailures.UnreadableFile, r.StatusReason);
    }

    [Fact]
    public void Entry_flood_at_the_production_cap_and_the_largest_allowed_count()
    {
        static DocxBuilder WithEntries(int total)
        {
            var b = Standard(SyllabusBody());
            for (var i = 7; i < total; i++) b.Add(new ZipEntrySpec { Name = $"word/media/e{i:D5}.png", Deflate = false });   // Standard has 7 entries
            return b;
        }
        Assert.True(WithEntries(1_000).Read().Succeeded);
        Assert.Equal(DocxFailure.TooManyEntries, WithEntries(1_001).Read().Failure);
    }

    [Fact]
    public void Text_cap_at_the_production_value_counts_characters_not_paragraphs()
    {
        var line = new string('x', 999) ;
        var under = string.Concat(Enumerable.Repeat(P(line), 200));                    // 199,800 characters
        Assert.True(Standard(under).Read().Succeeded);
        var over = under + P(new string('y', 201));                                      // 200,001 characters
        Assert.Equal(DocxFailure.TextTooLong, Standard(over).Read().Failure);
    }
}
