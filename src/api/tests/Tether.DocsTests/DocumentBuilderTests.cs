using System.Security.Cryptography;
using System.Text;
using Tether.Ai.Contracts;
using Tether.Docs.Application;
using Tether.Docs.Contracts;
using Tether.Lectures.Contracts;
using Tether.SharedKernel.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Tether.DocsTests;

/// <summary>DX-B03 (B06 to B09b): the deterministic document builder and the content digest. Pure code: no database, no network, no AI.</summary>
public class DocumentBuilderTests
{
    private static readonly Guid Session = Guid.Parse("0192f0a1-b2c3-4d5e-8f9a-0b1c2d3e4f01");
    private const string NotesId = "nt_0192f0a1b2c34d5e8f9a0b1c2d3e4f52";

    private static DocsSessionFacts Facts(string? title = "Cell division") =>
        new(Session, LectureState.Stopped, new DateTimeOffset(2026, 11, 5, 23, 30, 0, TimeSpan.FromHours(-5)), title, null);   // 04:30 UTC on Nov 6: the UTC date, not the local one

    private static NotesSnapshot Notes(string source = "mock", bool truncated = false, long? coveredTo = 9000, FinalSummarySnapshot? summary = null, params NotesSnapshotSection[] sections) =>
        new(Session, NotesId, 1, "Ready", source, truncated, coveredTo,
            sections.Length > 0 ? sections : [new NotesSnapshotSection("Mitosis has four phases", [new NotesSnapshotBullet("Mitosis has four phases.", [2000]), new NotesSnapshotBullet("Prophase comes first.", [4000, 6000])])],
            summary);

    // ---------------- B06: golden output ----------------

    [Fact]
    public void B06_The_builder_output_for_known_notes_is_exactly_the_documented_golden_document()
    {
        var b = DocumentBuilder.Build(Facts(), Notes());
        Assert.Equal("Cell division - Nov 6, 2026 UTC", b.Document.Title);
        Assert.Equal(
        [
            ("heading", 1, "Cell division - Nov 6, 2026 UTC", null),
            ("heading", 2, "Mitosis has four phases", null),
            ("bullet", null, "Mitosis has four phases.", "2000"),
            ("bullet", null, "Prophase comes first.", "4000,6000"),
            ("paragraph", null, DocumentBuilder.MockSource, null),
        ], b.Document.Blocks.Select(x => (x.Kind, x.Level, x.Text, x.CitationStartMs is null ? null : string.Join(",", x.CitationStartMs))).ToArray());
        Assert.Equal((1, 2, 3), (b.Sections, b.Bullets, b.Citations));
        Assert.False(b.TooLarge);
        Assert.False(b.NothingToExport);
        Assert.Equal("Made by Tether from your lecture notes. Test routine, not an AI model: treat as estimates and check them against the transcript.", DocumentBuilder.MockSource);
    }

    [Fact]
    public void B06_A_model_source_gets_the_model_sentence_and_the_Final_summary_comes_before_the_sections()
    {
        var summary = new FinalSummarySnapshot("Cells divide.", ["Four phases", "Spindle fibres"], [2000, 4000]);
        var b = DocumentBuilder.Build(Facts(), Notes(source: "model", summary: summary));
        var kinds = b.Document.Blocks.Select(x => x.Kind + ":" + x.Text).ToList();
        Assert.Equal("heading:Cell division - Nov 6, 2026 UTC", kinds[0]);
        Assert.Equal("heading:Final summary", kinds[1]);
        Assert.Equal(["paragraph:Cells divide.", "paragraph:Four phases", "paragraph:Spindle fibres", "paragraph:Transcript times: 00:02, 00:04"], kinds.Skip(2).Take(4));
        Assert.Equal("paragraph:" + DocumentBuilder.ModelSource, kinds[^1]);
        Assert.Equal(1, b.Sections);   // the summary heading is not a notes section
    }

    [Fact]
    public void B07_The_document_carries_lecture_times_only_no_segment_id_transcript_text_or_token()
    {
        var b = DocumentBuilder.Build(Facts(), Notes());
        var props = typeof(DocBlock).GetProperties().Select(p => p.Name).ToList();
        Assert.Equal(["Kind", "Text", "Level", "CitationStartMs"], props);
        Assert.All(b.Document.Blocks, x => Assert.DoesNotContain("seg", x.Text, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------- B08: the block cap ----------------

    [Fact]
    public void B08_Over_400_blocks_is_TooLarge_nothing_is_cut_from_the_counts_and_the_shown_document_stays_within_the_schema_cap()
    {
        var many = Enumerable.Range(1, 50).Select(i => new NotesSnapshotSection("Section " + i,
            Enumerable.Range(1, 8).Select(j => new NotesSnapshotBullet($"Point {i}.{j}", [i * 1000L + j])).ToList())).ToArray();
        var b = DocumentBuilder.Build(Facts(), Notes(sections: many));
        Assert.True(b.TooLarge);
        Assert.Equal(1 + 50 + 400 + 1, b.TotalBlocks);        // title, headings, bullets, closing paragraph: all counted
        Assert.Equal(50, b.Sections);
        Assert.Equal(400, b.Bullets);
        Assert.Equal(400, b.Document.Blocks.Count);           // the preview shows 400 (the schema's cap); canWrite is false so none of it is saved

        var exactly = Enumerable.Range(1, 49).Select(i => new NotesSnapshotSection("S" + i, Enumerable.Range(1, 8).Select(j => new NotesSnapshotBullet($"p{j}", [1L])).ToList())).ToArray();
        var at = DocumentBuilder.Build(Facts(), Notes(sections: exactly));
        Assert.Equal(1 + 49 + 392 + 1, at.TotalBlocks);        // 443: still too large
        var fits = DocumentBuilder.Build(Facts(), Notes(sections: Enumerable.Range(1, 40).Select(i => new NotesSnapshotSection("S" + i, Enumerable.Range(1, 8).Select(j => new NotesSnapshotBullet($"p{j}", [1L])).ToList())).ToArray()));
        Assert.Equal(1 + 40 + 320 + 1, fits.TotalBlocks);      // 362
        Assert.False(fits.TooLarge);
    }

    [Fact]
    public void B08_Notes_without_a_bullet_have_nothing_to_export()
    {
        var b = DocumentBuilder.Build(Facts(), Notes(sections: [new NotesSnapshotSection("Empty", [])]));
        Assert.True(b.NothingToExport);
        Assert.False(b.TooLarge);
    }

    [Fact]
    public void B08_A_bullet_without_a_lecture_time_is_dropped_and_counted_and_citations_past_8_are_counted()
    {
        var b = DocumentBuilder.Build(Facts(), Notes(sections: [new NotesSnapshotSection("S",
        [
            new NotesSnapshotBullet("kept", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]),
            new NotesSnapshotBullet("no time", []),
            new NotesSnapshotBullet("   ", [5]),
        ])]));
        Assert.Equal(1, b.Bullets);
        Assert.Equal(8, b.Citations);
        Assert.Equal(2, b.Stats.Dropped);              // the bullet with no time and the one that is empty after cleaning
        Assert.Equal(2, b.Stats.CitationsDropped);     // 9 and 10
    }

    // ---------------- B09a: truncated notes ----------------

    [Fact]
    public void B09a_Truncated_notes_add_a_closing_paragraph_that_says_where_they_stop()
    {
        var b = DocumentBuilder.Build(Facts(), Notes(truncated: true, coveredTo: 754_000));
        var last = b.Document.Blocks[^1];
        Assert.Equal("paragraph", last.Kind);
        Assert.Equal("These notes stop at 12:34. The lecture ran longer than the notes can cover.", last.Text);
        Assert.Equal(DocumentBuilder.MockSource, b.Document.Blocks[^2].Text);          // the source line stays the closing source paragraph, the stop line follows it
        Assert.Equal("1:02:03", DocumentBuilder.FormatMs(3_723_000));
        Assert.Equal("00:00", DocumentBuilder.FormatMs(-5));
        var plain = DocumentBuilder.Build(Facts(), Notes());
        Assert.DoesNotContain(plain.Document.Blocks, x => x.Text.StartsWith("These notes stop", StringComparison.Ordinal));
    }

    // ---------------- title ----------------

    [Fact]
    public void The_title_is_the_lecture_title_or_the_fallback_plus_the_UTC_date_and_never_passes_200_code_points()
    {
        Assert.Equal("Lecture notes - Nov 6, 2026 UTC", DocumentBuilder.BuildTitle(Facts(null)));
        Assert.Equal("Lecture notes - Nov 6, 2026 UTC", DocumentBuilder.BuildTitle(Facts("   ")));
        var long200 = DocumentBuilder.BuildTitle(Facts(new string('x', 200)));
        Assert.True(DocText.CodePoints(long200) <= 200);
        Assert.EndsWith(" - Nov 6, 2026 UTC", long200);
        // a stored zone, when a session carries one, gives the local date without the word UTC
        var zoned = Facts("Cell division") with { TimeZone = "America/New_York" };
        Assert.Equal("Cell division - Nov 5, 2026", DocumentBuilder.BuildTitle(zoned));
    }

    // ---------------- B09: the digest ----------------

    private static readonly byte[] TestKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    private static DocumentModel GoldenDoc()
    {
        var cafe = "Cafe" + (char)0x301;                  // decomposed: the canonical form must be NFC
        return new DocumentModel("Cell division - Nov 5, 2026 UTC",
        [
            new DocBlock("heading", "Cell division - Nov 5, 2026 UTC", 1, null),
            new DocBlock("heading", "Mitosis", 2, null),
            new DocBlock("bullet", "Say \"hi\" \\ " + cafe, null, [2000, 4000]),
            new DocBlock("paragraph", "Made by Tether.", null, null),
        ]);
    }

    [Fact]
    public void B09_The_canonical_serializer_is_pinned_by_a_golden_string_and_a_golden_digest_computed_independently()
    {
        var canonical = ContentDigest.Canonical("nt_0192f0a1b2c34d5e8f9a0b1c2d3e4f52", GoldenDoc());
        Assert.Equal(
            @"{""v"":1,""notesId"":""nt_0192f0a1b2c34d5e8f9a0b1c2d3e4f52"",""title"":""Cell division - Nov 5, 2026 UTC"",""blocks"":[{""kind"":""heading"",""level"":1,""text"":""Cell division - Nov 5, 2026 UTC""},{""kind"":""heading"",""level"":2,""text"":""Mitosis""},{""kind"":""bullet"",""text"":""Say \""hi\"" \\ Caf" + (char)0xE9 + @""",""citations"":[{""startMs"":2000},{""startMs"":4000}]},{""kind"":""paragraph"",""text"":""Made by Tether.""}]}",
            canonical);
        Assert.Equal(365, Encoding.UTF8.GetByteCount(canonical));   // UTF-8, no whitespace, the e-acute is two bytes
        // Expected value computed OUTSIDE this code base with openssl (HMAC-SHA-256, key = bytes 0..31, input = label LF session id LF canonical JSON):
        //   printf 'docs-content-digest.v1\n0192f0a1-b2c3-4d5e-8f9a-0b1c2d3e4f01\n' + canonical | openssl dgst -sha256 -mac HMAC -macopt hexkey:00..1f -binary | base64url
        Assert.Equal("cd_0knKwjC9OdohaOriGOSid-V1tJY5G7EyElDrawM5ZoE",
            ContentDigest.Compute(TestKey, Guid.Parse("0192f0a1-b2c3-4d5e-8f9a-0b1c2d3e4f01"), "nt_0192f0a1b2c34d5e8f9a0b1c2d3e4f52", GoldenDoc()));
    }

    [Fact]
    public void B09_Control_characters_are_the_only_escapes_besides_quote_and_backslash_and_non_ASCII_is_not_escaped()
    {
        var doc = new DocumentModel("t", [new DocBlock("paragraph", "a" + (char)1 + "b/" + (char)0x2603 + "<>&'", null, null)]);
        var canonical = ContentDigest.Canonical("nt_" + new string('a', 32), doc);
        Assert.Contains("\"text\":\"a\\u0001b/" + (char)0x2603 + "<>&'\"", canonical);
    }

    [Fact]
    public void B09_The_same_notes_give_the_same_digest_and_any_change_to_title_session_notes_id_label_or_key_changes_it()
    {
        var key = TestKey;
        string D(DocsSessionFacts f, NotesSnapshot n, byte[]? k = null) { var b = DocumentBuilder.Build(f, n); return ContentDigest.Compute(k ?? key, f.SessionId, n.NotesId, b.Document); }
        var baseline = D(Facts(), Notes());
        Assert.Equal(baseline, D(Facts(), Notes()));
        Assert.Matches("^cd_[A-Za-z0-9_-]{43}$", baseline);
        Assert.NotEqual(baseline, D(Facts("Another title"), Notes()));
        Assert.NotEqual(baseline, D(Facts() with { SessionId = Guid.NewGuid() }, Notes()));
        Assert.NotEqual(baseline, D(Facts(), Notes() with { NotesId = "nt_" + new string('b', 32) }));
        Assert.NotEqual(baseline, D(Facts(), Notes(source: "model")));
        Assert.NotEqual(baseline, D(Facts(), Notes(), Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        var doc = DocumentBuilder.Build(Facts(), Notes()).Document;
        var other = Encoding.UTF8.GetBytes("other-label.v1\n" + Session.ToString("D") + "\n" + ContentDigest.Canonical(NotesId, doc));
        var otherDigest = "cd_" + Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(HMACSHA256.HashData(key, other));
        Assert.NotEqual(baseline, otherDigest);   // the label is part of the input
    }

    [Fact]
    public void B09_The_digest_comparison_is_constant_time_shaped_and_rejects_other_lengths()
    {
        var d = "cd_" + new string('A', 43);
        Assert.True(ContentDigest.Equal(d, d));
        Assert.False(ContentDigest.Equal(d, "cd_" + new string('A', 42) + "B"));
        Assert.False(ContentDigest.Equal(d, d + "A"));
        Assert.False(ContentDigest.Equal(d, ""));
    }

    [Fact]
    public void B09b_The_digest_key_is_derived_under_its_own_label_and_is_not_the_idempotency_etag_audit_or_oauth_key()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Security:RootSecret"] = Convert.ToBase64String(Enumerable.Range(1, 48).Select(i => (byte)(i * 7 + 3)).ToArray()) }).Build();
        var derived = RootSecret.Resolve(cfg, new FakeEnv("Test"));
        Assert.Equal("docs-content-digest.v1", DerivedKeys.Labels.DocsContentDigest);
        var digestKey = derived.Derive(DerivedKeys.Labels.DocsContentDigest);
        foreach (var other in new[] { DerivedKeys.Labels.Idempotency, DerivedKeys.Labels.ETag, DerivedKeys.Labels.SyllabusAudit, DerivedKeys.Labels.OAuthState })
        {
            Assert.NotEqual(DerivedKeys.Labels.DocsContentDigest, other);
            Assert.NotEqual(Convert.ToHexString(derived.Derive(other)), Convert.ToHexString(digestKey));
        }
        var keys = DocsKeys.From(cfg, derived);
        Assert.Equal(Convert.ToHexString(digestKey), Convert.ToHexString(keys.Digest));
        Assert.DoesNotContain(keys.IdempotencyRing, k => Convert.ToHexString(k) == Convert.ToHexString(keys.Digest));
        Assert.Equal(ContentDigest.Label, DerivedKeys.Labels.DocsContentDigest);
    }

    private sealed class FakeEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
