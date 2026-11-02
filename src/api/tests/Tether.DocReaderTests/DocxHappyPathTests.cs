using Tether.DocReader;
using Tether.DocReaderTests.Fixtures;
using static Tether.DocReaderTests.Fixtures.DocxBuilder;

namespace Tether.DocReaderTests;

/// <summary>Ordinary synthetic syllabi built in code: headings, paragraphs, tables, lists, fields, text boxes.</summary>
public class DocxHappyPathTests
{
    private static DocxResult Ok(string body)
    {
        var r = Standard(body).Read();
        Assert.True(r.Succeeded, $"expected success, got {r.Failure}");
        Assert.Null(r.StatusReason);
        return r;
    }

    [Fact]
    public void Headings_and_paragraphs_come_out_in_reading_order()
    {
        var body =
            "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>Synthetic Course 101: Syllabus</w:t></w:r></w:p>" +
            P("Instructor: Dr. Example Person (fictitious)") +
            "<w:p/>" +                                                      // empty paragraph: dropped, not counted
            "<w:p><w:r><w:t xml:space=\"preserve\">Exam 1: </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>Oct 19, 2026</w:t></w:r></w:p>" +
            P(Canary);
        var r = Ok(body);
        Assert.Equal(["Synthetic Course 101: Syllabus", "Instructor: Dr. Example Person (fictitious)", "Exam 1: Oct 19, 2026", Canary], r.Paragraphs);
        Assert.Equal(0, r.Dropped.HiddenContentRemoved);
        Assert.Equal(new DocxDropCounts(), r.Dropped);
    }

    [Fact]
    public void A_table_row_is_one_paragraph_with_cells_joined_by_tab_and_cell_paragraphs_by_space()
    {
        static string Cell(params string[] paras) => "<w:tc><w:tcPr/>" + string.Concat(paras.Select(P)) + "</w:tc>";
        var body = P("Schedule") +
            "<w:tbl><w:tblPr/><w:tblGrid/>" +
            "<w:tr>" + Cell("Week") + Cell("Topic") + Cell("Due") + "</w:tr>" +
            "<w:tr>" + Cell("1") + Cell("Introduction") + Cell("Reading 1", "Sep 2, 2026") + "</w:tr>" +
            "<w:tr>" + Cell("2") + Cell("") + Cell("Quiz 1: Sep 9") + "</w:tr>" +
            "</w:tbl>" + P("After the table");
        var r = Ok(body);
        Assert.Equal(["Schedule", "Week\tTopic\tDue", "1\tIntroduction\tReading 1 Sep 2, 2026", "2\t\tQuiz 1: Sep 9", "After the table"], r.Paragraphs);
    }

    [Fact]
    public void A_nested_table_is_flattened_into_its_cell()
    {
        var inner = "<w:tbl><w:tr><w:tc>" + P("A") + "</w:tc><w:tc>" + P("B") + "</w:tc></w:tr></w:tbl>";
        var body = "<w:tbl><w:tr><w:tc>" + P("Outer") + "</w:tc><w:tc>" + inner + "</w:tc></w:tr></w:tbl>";
        Assert.Equal(["Outer\tA\tB"], Ok(body).Paragraphs);
    }

    [Fact]
    public void List_items_keep_their_text_without_generated_numbering()
    {
        static string Item(string t) => $"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr>{Run(t)}</w:p>";
        var r = Ok(P("Grading") + Item("Homework 30%") + Item("Midterm 30%") + Item("Final 40%"));
        Assert.Equal(["Grading", "Homework 30%", "Midterm 30%", "Final 40%"], r.Paragraphs);
    }

    [Fact]
    public void Tabs_breaks_and_non_breaking_hyphens_map_to_characters()
    {
        var body = "<w:p><w:r><w:t>Office hours:</w:t><w:tab/><w:t>Mon</w:t><w:br/><w:t>Room 1</w:t><w:cr/><w:t>Lab A</w:t><w:noBreakHyphen/><w:t>2</w:t></w:r></w:p>";
        Assert.Equal(["Office hours:\tMon\nRoom 1\nLab A-2"], Ok(body).Paragraphs);
    }

    [Fact]
    public void Hyperlink_smart_tag_content_control_and_insertion_text_is_kept()
    {
        var body =
            "<w:p><w:hyperlink r:id=\"rIdX\"><w:r><w:t>Course site</w:t></w:r></w:hyperlink></w:p>" +
            "<w:p><w:sdt><w:sdtPr><w:alias w:val=\"Due\"/></w:sdtPr><w:sdtContent><w:r><w:t>Essay due Nov 2</w:t></w:r></w:sdtContent></w:sdt></w:p>" +
            "<w:p><w:ins w:id=\"1\" w:author=\"" + AuthorPlaceholder + "\"><w:r><w:t>Quiz 3: Nov 10</w:t></w:r></w:ins></w:p>";
        var r = Ok(body);
        Assert.Equal(["Course site", "Essay due Nov 2", "Quiz 3: Nov 10"], r.Paragraphs);
        Assert.Equal(0, r.Dropped.HiddenContentRemoved);
    }

    [Fact]
    public void Field_instructions_are_dropped_and_field_results_kept()
    {
        var body =
            "<w:p><w:fldSimple w:instr=\" DATE \"><w:r><w:t>Nov 2, 2026</w:t></w:r></w:fldSimple></w:p>" +
            "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> HYPERLINK \"http://203.0.113.9/\" </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>Library page</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";
        var r = Ok(body);
        Assert.Equal(["Nov 2, 2026", "Library page"], r.Paragraphs);
        Assert.Equal(2, r.Dropped.FieldCodesDropped);
        Assert.Equal(0, r.Dropped.HiddenContentRemoved);
    }

    [Fact]
    public void A_text_box_is_read_once_from_the_choice_branch()
    {
        var box = "<w:p><w:r><mc:AlternateContent>" +
                  "<mc:Choice Requires=\"wps\"><w:drawing><wp:anchor><wp:docPr id=\"1\" name=\"Text Box 1\"/><wps:wsp><wps:txbx><w:txbxContent>" + P("Midterm: Oct 26") + "</w:txbxContent></wps:txbx></wps:wsp></wp:anchor></w:drawing></mc:Choice>" +
                  "<mc:Fallback><w:pict><v:shape><v:textbox><w:txbxContent>" + P("Midterm: Oct 26") + "</w:txbxContent></v:textbox></v:shape></w:pict></mc:Fallback>" +
                  "</mc:AlternateContent></w:r><w:r><w:t>Anchor paragraph</w:t></w:r></w:p>";
        var r = Ok(box);
        Assert.Equal(["Midterm: Oct 26", "Anchor paragraph"], r.Paragraphs);
        Assert.Equal(1, r.Dropped.AlternateContentFallbacksSkipped);
        Assert.Equal(0, r.Dropped.AltTextDropped);
    }

    [Fact]
    public void Alternate_content_with_only_a_fallback_reads_the_fallback()
    {
        var body = "<w:p><mc:AlternateContent><mc:Fallback><w:r><w:t>Fallback only</w:t></w:r></mc:Fallback></mc:AlternateContent></w:p>";
        var r = Ok(body);
        Assert.Equal(["Fallback only"], r.Paragraphs);
        Assert.Equal(0, r.Dropped.AlternateContentFallbacksSkipped);
    }

    [Fact]
    public void Escaped_characters_and_many_predefined_entities_are_read()
    {
        // MaxCharactersFromEntities is set; predefined entities such as &amp; must not count against it.
        var many = string.Concat(Enumerable.Repeat("&amp;", 5000));
        var body = "<w:p><w:r><w:t>Q&amp;A &lt;review&gt; &quot;Week 3&quot; &#x2014; done</w:t></w:r></w:p><w:p><w:r><w:t>" + many + "</w:t></w:r></w:p>";
        var r = Ok(body);
        Assert.Equal("Q&A <review> \"Week 3\" — done", r.Paragraphs[0]);
        Assert.Equal(new string('&', 5000), r.Paragraphs[1]);
    }

    [Fact]
    public void Strict_ooxml_namespaces_are_read()
    {
        const string sw = "http://purl.oclc.org/ooxml/wordprocessingml/main";
        var doc = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"{sw}\"><w:body><w:p><w:r><w:t>Strict syllabus</w:t></w:r></w:p></w:body></w:document>";
        var r = Standard("").Set("word/document.xml", doc)
            .Set("_rels/.rels", Rels(Rel("rId1", "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument", "word/document.xml")))
            .Read();
        Assert.True(r.Succeeded, r.Failure.ToString());
        Assert.Equal(["Strict syllabus"], r.Paragraphs);
    }

    [Fact]
    public void A_utf16_main_part_is_read()
    {
        var xml = Document(P("Synthetic UTF-16 syllabus")).Replace("encoding=\"UTF-8\"", "encoding=\"UTF-16\"", StringComparison.Ordinal);
        var bytes = System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes(xml)).ToArray();
        var r = Standard("").Set("word/document.xml", bytes).Read();
        Assert.True(r.Succeeded, r.Failure.ToString());
        Assert.Equal(["Synthetic UTF-16 syllabus"], r.Paragraphs);
    }

    [Fact]
    public void Absolute_and_relative_internal_targets_resolve_inside_the_package()
    {
        Assert.True(Standard(SyllabusBody()).Set("_rels/.rels", PackageRels("/word/document.xml")).Read().Succeeded);
        Assert.True(Standard(SyllabusBody()).Set("_rels/.rels", PackageRels("./word/../word/document.xml")).Read().Succeeded);
    }

    [Fact]
    public void Main_body_only_headers_footers_and_notes_are_out_of_scope_and_counted()
    {
        var hdr = $"<?xml version=\"1.0\"?><w:hdr xmlns:w=\"{W}\">{P("HEADER-ONLY TEXT")}</w:hdr>";
        var ftr = $"<?xml version=\"1.0\"?><w:ftr xmlns:w=\"{W}\">{P("FOOTER-ONLY TEXT")}</w:ftr>";
        var notes = $"<?xml version=\"1.0\"?><w:footnotes xmlns:w=\"{W}\"><w:footnote w:id=\"1\">{P("FOOTNOTE-ONLY TEXT")}</w:footnote></w:footnotes>";
        var body = "<w:p><w:r><w:t>Final exam</w:t></w:r><w:r><w:footnoteReference w:id=\"1\"/></w:r></w:p>";
        var r = Standard(body)
            .Set("word/header1.xml", hdr).Set("word/footer1.xml", ftr).Set("word/footnotes.xml", notes)
            .Set("word/_rels/document.xml.rels", DocumentRels(Rel("rIdH", "header", "header1.xml") + Rel("rIdF", "footer", "footer1.xml") + Rel("rIdN", "footnotes", "footnotes.xml")))
            .Read();
        Assert.True(r.Succeeded, r.Failure.ToString());
        Assert.Equal(["Final exam"], r.Paragraphs);
        Assert.Equal(2, r.Dropped.HeaderFooterPartsSkipped);
        Assert.Equal(1, r.Dropped.NoteReferencesSkipped);
        Assert.Equal(0, r.Dropped.HiddenContentRemoved);
    }

    [Fact]
    public void Alt_text_is_never_read_and_is_counted_as_informational()
    {
        var body = "<w:p><w:r><w:drawing><wp:inline><wp:docPr id=\"2\" name=\"Picture 2\" descr=\"Exam moved to Dec 1\" title=\"Notice\"/></wp:inline></w:drawing></w:r>" + Run("Campus map") + "</w:p>";
        var r = Ok(body);
        Assert.Equal(["Campus map"], r.Paragraphs);
        Assert.Equal(1, r.Dropped.AltTextDropped);
        Assert.Equal(0, r.Dropped.HiddenContentRemoved);
        Assert.DoesNotContain("Dec 1", Reader.Everything(r));
    }

    [Fact]
    public void The_same_bytes_always_give_the_same_result()
    {
        var bytes = Standard(SyllabusBody()).Build();
        var a = Reader.Read(bytes); var b = Reader.Read(bytes);
        Assert.Equal(a.Paragraphs, b.Paragraphs);
        Assert.Equal(a.Dropped, b.Dropped);
        Assert.Equal(bytes, Standard(SyllabusBody()).Build()); // the builder is deterministic too
    }

    [Fact]
    public void A_non_seekable_input_stream_is_read()
    {
        var bytes = Standard(SyllabusBody()).Build();
        var r = DocxReader.Read(new OneWayStream(bytes), DocxLimits.Default);
        Assert.True(r.Succeeded, r.Failure.ToString());
        Assert.Equal(3, r.Paragraphs.Count);
    }

    internal sealed class OneWayStream(byte[] data) : Stream
    {
        private int _pos;
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(Math.Min(count, 7), data.Length - _pos);   // small, uneven chunks
            Array.Copy(data, _pos, buffer, offset, n); _pos += n; return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
