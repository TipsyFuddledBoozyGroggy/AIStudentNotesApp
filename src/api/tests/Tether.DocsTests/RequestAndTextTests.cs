using System.Text;
using System.Text.Json;
using Tether.Docs.Application;

namespace Tether.DocsTests;

/// <summary>The text policy (L-FORGE, SBV-01), the strict request parsers (checked against the contract's own fixtures) and the fault header.</summary>
public class RequestAndTextTests
{
    private static string C(int cp) => char.ConvertFromUtf32(cp);

    // ---------------- text policy ----------------

    [Fact]
    public void Text_has_bidi_overrides_isolates_zero_width_tag_characters_controls_and_line_breaks_removed_and_whitespace_collapsed()
    {
        var hostile = string.Concat("A", C(0x202E), "b", C(0x202C), " ", C(0x200B), "c", C(0x2066), "d", C(0x2069), C(0xE0041), C(0xFEFF), "\r\n\t e", C(0), C(0x85), C(0x2028), "f\n");
        Assert.True(DocText.TryClean(hostile, 500, out var clean, out var cut));
        Assert.Equal("Ab cd e f", clean);
        Assert.False(cut);
        Assert.DoesNotContain(clean, char.IsControl);
        Assert.Equal("x y", Clean("  x " + C(0xA0) + C(0x3000) + " y  "));    // no-break and ideographic spaces are spaces
        Assert.Equal("[x](javascript:alert(1)) <img onerror=x>", Clean("[x](javascript:alert(1)) <img onerror=x>"));   // markup is left alone: the page renders text only
    }

    [Fact]
    public void Text_is_NFC_and_an_empty_result_is_a_drop()
    {
        Assert.Equal("Caf" + C(0xE9), Clean("Cafe" + C(0x301)));
        Assert.False(DocText.TryClean("", 10, out _, out _));
        Assert.False(DocText.TryClean(null, 10, out _, out _));
        Assert.False(DocText.TryClean(string.Concat(C(0x200B), C(0x202E), "\n\t "), 10, out _, out _));
        Assert.True(DocText.TryClean("lone " + (char)0xD800 + " surrogate", 50, out var s, out _));
        Assert.Equal("lone surrogate", s);
    }

    [Fact]
    public void Text_is_cut_at_the_code_point_cap_with_an_ellipsis_and_never_inside_a_surrogate_pair()
    {
        var pairs = string.Concat(Enumerable.Repeat(C(0x1F600), 600));
        Assert.True(DocText.TryClean(pairs, 500, out var clean, out var cut));
        Assert.True(cut);
        Assert.Equal(500, DocText.CodePoints(clean));
        Assert.EndsWith(C(0x2026), clean);
        Assert.DoesNotContain(clean.EnumerateRunes(), r => r.Value == 0xFFFD);   // no pair was split
        Assert.True(DocText.TryClean(new string('a', 500), 500, out var exact, out var cutExact));
        Assert.False(cutExact);
        Assert.Equal(500, exact.Length);
    }

    private static string Clean(string s)
    {
        Assert.True(DocText.TryClean(s, 500, out var c, out _));
        return c;
    }

    // ---------------- request parsers against the contract's fixtures ----------------

    private static string FixtureDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "src", "shared", "contracts", "docs.export.v1", "fixtures");
            if (Directory.Exists(c)) return c;
        }
        throw new DirectoryNotFoundException("docs.export.v1 fixtures not found (mount the repository root)");
    }

    public static IEnumerable<object[]> Files(string kind, string schema) =>
        Directory.GetFiles(Path.Combine(FixtureDir(), kind), schema + "--*.json").Order().Select(f => new object[] { Path.GetFileName(f) });

    public static IEnumerable<object[]> ValidConfirm() => Files("valid", "docs-export-request");
    public static IEnumerable<object[]> InvalidConfirm() => Files("invalid", "docs-export-request");
    public static IEnumerable<object[]> ValidPreview() => Files("valid", "docs-preview-request");
    public static IEnumerable<object[]> InvalidPreview() => Files("invalid", "docs-preview-request");

    /// <summary>
    /// The contract's fixtures write document ids as ddc_ + 32 hex digits, but the id rule they inherit from syllabus-common (R2-05) calls a missing hyphen an alias
    /// and refuses it. The server emits and accepts only the hyphenated GUID form, so the fixtures are normalised to it before parsing (reported to the Lead).
    /// </summary>
    private static byte[] Fixture(string kind, string file) => Encoding.UTF8.GetBytes(System.Text.RegularExpressions.Regex.Replace(
        File.ReadAllText(Path.Combine(FixtureDir(), kind, file)), "ddc_([0-9a-f]{8})([0-9a-f]{4})([0-9a-f]{4})([0-9a-f]{4})([0-9a-f]{12})", "ddc_$1-$2-$3-$4-$5"));

    [Theory, MemberData(nameof(ValidConfirm))]
    public void Every_valid_confirm_fixture_parses(string file)
    {
        var problem = RequestParser.ParseConfirm(Fixture("valid", file), out var req);
        Assert.Null(problem);
        Assert.NotNull(req);
    }

    [Theory, MemberData(nameof(InvalidConfirm))]
    public void Every_invalid_confirm_fixture_is_refused_with_a_400(string file)
    {
        var problem = RequestParser.ParseConfirm(Fixture("invalid", file), out var req);
        Assert.NotNull(problem);
        Assert.Null(req);
        Assert.Equal(400, ((DocsProblem)problem!).Status);
    }

    [Theory, MemberData(nameof(ValidPreview))]
    public void Every_valid_preview_request_fixture_parses(string file) => Assert.Null(RequestParser.ParsePreview(Fixture("valid", file), out _));

    [Theory, MemberData(nameof(InvalidPreview))]
    public void Every_invalid_preview_request_fixture_is_refused(string file) => Assert.NotNull(RequestParser.ParsePreview(Fixture("invalid", file), out _));

    [Fact]
    public void The_fixture_sets_are_not_empty_so_the_theories_above_mean_something()
    {
        Assert.True(ValidConfirm().Count() >= 3);
        Assert.True(InvalidConfirm().Count() >= 8);
        Assert.True(InvalidPreview().Count() >= 2);
    }

    [Fact]
    public void Confirm_parsing_is_strict_about_duplicates_trailing_commas_comments_numbers_and_depth()
    {
        var ok = """{"notesId":"nt_00000000000000000000000000000001","contentDigest":"cd_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","mode":"create","expectedExistingVersion":null,"expectedExistingDocId":null,"confirm":{"provider":"demo_docs","acknowledgedDemo":true}}""";
        Assert.Null(RequestParser.ParseConfirm(Encoding.UTF8.GetBytes(ok), out _));
        foreach (var bad in new[]
        {
            ok.Replace("\"mode\":\"create\"", "\"mode\":\"create\",\"mode\":\"update\""),   // duplicate member
            ok.Replace("true}}", "true,}}"),                                                // trailing comma
            "/* c */" + ok,                                                                  // comment
            ok.Replace("null,\"expectedExistingDocId\"", "1.0,\"expectedExistingDocId\""),   // a fractional version
            ok.Replace("\"mode\":\"create\"", "\"mode\":\"CREATE\""),                        // case
            ok + " trailing",
            "[" + ok + "]",
            ok.Replace("\"acknowledgedDemo\":true", "\"acknowledgedDemo\":\"true\""),
            ok.Replace("nt_00000000000000000000000000000001", "nt_0000000000000000000000000000000G"),
            ok.Replace("nt_00000000000000000000000000000001", "nt_00000000000000000000000000000001\n"),   // a trailing newline passes no pattern
            ok.Replace("cd_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "cd_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"),
        })
            Assert.NotNull(RequestParser.ParseConfirm(Encoding.UTF8.GetBytes(bad), out _));
        Assert.NotNull(RequestParser.ParseConfirm([0xFF, 0xFE, 0x00], out _));   // not UTF-8
        Assert.NotNull(RequestParser.ParseConfirm([], out _));
    }

    [Fact]
    public void An_update_needs_the_exact_document_it_names_and_its_canonical_form_is_ddc_plus_a_hyphenated_guid()
    {
        var id = Guid.NewGuid();
        string Body(string mode, string ver, string doc) =>
            $$$"""{"notesId":"nt_00000000000000000000000000000001","contentDigest":"cd_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","mode":"{{{mode}}}","expectedExistingVersion":{{{ver}}},"expectedExistingDocId":{{{doc}}},"confirm":{"provider":"demo_docs","acknowledgedDemo":true}}""";
        Assert.Null(RequestParser.ParseConfirm(Encoding.UTF8.GetBytes(Body("update", "3", $"\"ddc_{id:D}\"")), out var req));
        Assert.Equal((3, id), (req!.ExpectedVersion, req.ExpectedDocId!.Value));
        foreach (var doc in new[] { $"\"DDC_{id:D}\"", $"\"ddc_{id:N}\"", $"\"ddc_{id:D}\"".ToUpperInvariant(), $"\"ddc_{{{id:D}}}\"", $"\"dxp_{id:D}\"", $"\"{id:D}\"", "null" })
            Assert.NotNull(RequestParser.ParseConfirm(Encoding.UTF8.GetBytes(Body("update", "3", doc)), out _));
        Assert.NotNull(RequestParser.ParseConfirm(Encoding.UTF8.GetBytes(Body("update", "0", $"\"ddc_{id:D}\"")), out _));
        Assert.NotNull(RequestParser.ParseConfirm(Encoding.UTF8.GetBytes(Body("create", "1", "null")), out _));
    }

    // ---------------- the Idempotency-Key and the fingerprint material ----------------

    [Fact]
    public void The_idempotency_key_form_matches_the_contract_and_the_fingerprint_material_cannot_collide()
    {
        Assert.True(Hashing.ValidKey("abcdefgh"));
        Assert.True(Hashing.ValidKey(new string('k', 128)));
        foreach (var bad in new[] { "short", new string('k', 129), "has space 12", "tab\tkey12345", "key12345\n", "ключ-key-12345", null })
            Assert.False(Hashing.ValidKey(bad));
        Assert.NotEqual(Hashing.Material("a|b", "c"), Hashing.Material("a", "b|c"));
        Assert.NotEqual(Hashing.Material(null, "x"), Hashing.Material("", "x"));
    }

    // ---------------- fault header ----------------

    [Theory]
    [InlineData("unavailable", "unavailable", 5)]
    [InlineData("UNAVAILABLE", "unavailable", 5)]
    [InlineData("rate_limited", "rate_limited", 30)]
    [InlineData("rate_limited:90", "rate_limited", 90)]
    [InlineData("rate_limited:99999", "rate_limited", 3600)]
    [InlineData("rate_limited:0", "rate_limited", 1)]
    [InlineData("rejected", "rejected", 0)]
    [InlineData("slow", "slow", 5)]
    [InlineData("slow:2", "slow", 2)]
    [InlineData("slow:600", "slow", 5)]
    public void The_fault_header_is_parsed_with_the_documented_caps(string raw, string kind, int seconds)
    {
        Assert.True(DemoFaultContext.TryParseHeader(raw, out var k, out var s));
        Assert.Equal((kind, seconds), (k, s));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("quota_exceeded")]
    [InlineData("reconnect_required")]
    [InlineData("doc_changed_outside")]
    [InlineData("drop table")]
    public void An_unknown_fault_is_ignored_and_the_reserved_real_provider_codes_can_not_be_injected(string? raw) =>
        Assert.False(DemoFaultContext.TryParseHeader(raw, out _, out _));

    [Fact]
    public void The_slow_cap_is_5_seconds_and_the_retry_after_cap_is_3600()
    {
        Assert.Equal(5, DemoFaultContext.SlowCapSeconds);
        Assert.Equal(3600, DemoFaultContext.RetryAfterCap);
    }
}
