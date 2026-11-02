using System.Reflection;
using Tether.DocReader;
using Tether.DocReaderTests.Fixtures;
using static Tether.DocReaderTests.Fixtures.DocxBuilder;

namespace Tether.DocReaderTests;

/// <summary>
/// PO default DR-CONTRACT-META: core and extended properties (author, last modified by, company, title, dates) are never read, never
/// counted and never returned. Comment authors and tracked-change authors are never returned either.
/// </summary>
public class DocxPrivacyTests
{
    public static TheoryData<string> Fixtures() => new()
    {
        "plain", "comments", "tracked", "custom-props",
    };

    private static DocxBuilder Build(string kind) => kind switch
    {
        "plain" => Standard(SyllabusBody()),
        "comments" => Standard("<w:p><w:commentRangeStart w:id=\"0\"/>" + Run("Exam 1") + "<w:commentRangeEnd w:id=\"0\"/><w:r><w:commentReference w:id=\"0\"/></w:r></w:p>")
            .Set("word/comments.xml", $"<w:comments xmlns:w=\"{W}\"><w:comment w:id=\"0\" w:author=\"{AuthorPlaceholder}\">{P("note")}</w:comment></w:comments>")
            .Set("word/people.xml", $"<w15:people xmlns:w15=\"http://schemas.microsoft.com/office/word/2012/wordml\"><w15:person w15:author=\"{AuthorPlaceholder}\"/></w15:people>"),
        "tracked" => Standard($"<w:p><w:ins w:id=\"1\" w:author=\"{AuthorPlaceholder}\" w:date=\"2026-01-01T00:00:00Z\">{Run("Quiz")}</w:ins>" +
                              $"<w:del w:id=\"2\" w:author=\"{AuthorPlaceholder}\"><w:r><w:delText>old</w:delText></w:r></w:del></w:p>"),
        _ => Standard(SyllabusBody()).Set("docProps/custom.xml",
            $"<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/custom-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
            $"<property fmtid=\"{{D5CDD505-2E9C-101B-9397-08002B2CF9AE}}\" pid=\"2\" name=\"Owner\"><vt:lpwstr>{AuthorPlaceholder}</vt:lpwstr></property></Properties>"),
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void The_author_placeholder_is_never_returned(string kind)
    {
        var builder = Build(kind);
        // Positive control: the placeholder really is inside the package bytes.
        Assert.Contains(AuthorPlaceholder, System.Text.Encoding.UTF8.GetString(builder.Build()) + Decompressed(builder));
        var r = builder.Read();
        Assert.True(r.Succeeded, r.Failure.ToString());
        Assert.DoesNotContain(AuthorPlaceholder, Reader.Everything(r));
        Assert.DoesNotContain("Synthetic title property", Reader.Everything(r));
    }

    [Fact]
    public void Metadata_is_not_counted_as_hidden_content()
    {
        var r = Standard(SyllabusBody()).Read();
        Assert.Equal(0, r.Dropped.HiddenContentRemoved);
    }

    [Fact]
    public void A_broken_properties_part_is_not_even_parsed()
    {
        // If docProps were read, a DOCTYPE there would refuse the file. It must not: the reader never opens the part beyond the 8-byte signature check.
        var r = Standard(SyllabusBody()).Set("docProps/core.xml", "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY a \"b\">]><x>&a;</x>").Read();
        Assert.True(r.Succeeded, r.Failure.ToString());
    }

    [Fact]
    public void The_public_result_types_have_no_metadata_members()
    {
        var banned = new[] { "Author", "Creator", "Modified", "Company", "Title", "Owner", "Person", "Name", "Created", "Property", "Properties" };
        foreach (var t in new[] { typeof(DocxResult), typeof(DocxDropCounts) })
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                Assert.DoesNotContain(banned, b => p.Name.Contains(b, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_reader_assembly_exposes_only_the_documented_public_types()
    {
        var types = typeof(DocxReader).Assembly.GetExportedTypes().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(["DocxDropCounts", "DocxFailure", "DocxFailures", "DocxLimits", "DocxReader", "DocxResult"], types);
    }

    private static string Decompressed(DocxBuilder b)
    {
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(b.Build()));
        return string.Concat(zip.Entries.Select(e => { using var s = new StreamReader(e.Open()); return s.ReadToEnd(); }));
    }
}
