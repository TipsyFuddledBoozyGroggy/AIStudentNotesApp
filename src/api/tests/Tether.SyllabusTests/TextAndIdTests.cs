using System.Text;
using System.Text.Json;
using Tether.SharedKernel;
using Tether.Syllabus.Application;
using Tether.Syllabus.Text;

// NOTE: @uXXXX / @UXXXXXXXX are placeholders turned into real escapes by a post-processing step (the editing tool decodes escapes into raw characters).

namespace Tether.SyllabusTests;

/// <summary>WireId (canonical id parser), itemKey vector, TextPolicy over the shared fixture corpus, document canonicalization and strict binding.</summary>
public class TextAndIdTests
{
    private static string FixturesDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "src", "shared", "contracts", "fixtures");
            if (Directory.Exists(c)) return c;
        }
        throw new DirectoryNotFoundException("fixtures not found");
    }

    // ---------------------------------------------------------------- WireId (R2-05, R3-14)

    [Fact]
    public void WireId_round_trips_only_the_canonical_form()
    {
        var g = Guid.CreateVersion7();
        var wire = WireId.Format("itm", g);
        Assert.Equal("itm_" + g.ToString("D"), wire);
        Assert.True(WireId.TryParse("itm", wire, out var back));
        Assert.Equal(g, back);
        Assert.True(WireId.TryParse(null, g.ToString("D"), out back));
        Assert.Equal(g, back);
    }

    [Theory]
    [InlineData("upper")] [InlineData("braced")] [InlineData("urn")] [InlineData("nohyphen")] [InlineData("space")] [InlineData("trailing_lf")]
    [InlineData("wrong_prefix")] [InlineData("no_prefix")] [InlineData("double_prefix")] [InlineData("empty")] [InlineData("trailing_char")]
    public void WireId_rejects_every_alias_spelling(string kind)
    {
        var g = Guid.CreateVersion7();
        var d = g.ToString("D");
        var spelling = kind switch
        {
            "upper" => "itm_" + d.ToUpperInvariant(),
            "braced" => "itm_{" + d + "}",
            "urn" => "urn:uuid:" + d,
            "nohyphen" => "itm_" + g.ToString("N"),
            "space" => " itm_" + d,
            "trailing_lf" => "itm_" + d + "\n",
            "wrong_prefix" => "syl_" + d,
            "no_prefix" => d,
            "double_prefix" => "itm_itm_" + d,
            "empty" => "",
            _ => "itm_" + d + "x",
        };
        Assert.False(WireId.TryParse("itm", spelling, out _), kind);
    }

    [Fact]
    public void WireId_rejects_prefixed_ids_where_a_bare_id_is_expected_and_null()
    {
        var g = Guid.NewGuid();
        Assert.False(WireId.TryParse(null, "crs_" + g.ToString("D"), out _));
        Assert.False(WireId.TryParse("itm", null, out _));
    }

    [Fact]
    public void ItemKey_matches_the_pinned_contract_vector_and_is_stable()
    {
        // same recomputation as SyllabusContractTests (itemKey = ik_ + first 32 of base64url(SHA-256("tether.syllabus.item.v1\n" + syl + "\n" + itm)))
        var expected = "ik_" + Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("tether.syllabus.item.v1\nsyl_1\nitm_1")))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_')[..32];
        Assert.Equal(expected, ItemKey.Derive("syl_1", "itm_1"));
        Assert.Equal(35, ItemKey.Derive("syl_1", "itm_1").Length);
        Assert.NotEqual(ItemKey.Derive("syl_1", "itm_1"), ItemKey.Derive("syl_1", "itm_2"));
        Assert.Matches("^ik_[A-Za-z0-9_-]{32}$", ItemKey.Derive(WireId.Format("syl", Guid.NewGuid()), WireId.Format("itm", Guid.NewGuid())));
    }

    // ---------------------------------------------------------------- TextPolicy

    private static IEnumerable<(string File, JsonElement Root)> Fixtures(string kind, string prefix) =>
        Directory.GetFiles(Path.Combine(FixturesDir(), kind), prefix + "*.json").Order()
            .Select(f => (Path.GetFileName(f), JsonDocument.Parse(File.ReadAllText(f)).RootElement.Clone()));

    [Fact]
    public void Every_valid_item_create_fixture_passes_the_server_TextPolicy()
    {
        var any = false;
        foreach (var (file, root) in Fixtures("valid", "syllabus-item-create-request--"))
        {
            any = true;
            Assert.True(TextPolicy.TryValidate(root.GetProperty("title").GetString(), TextKind.SingleLine, 200, out _), file);
            if (root.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.String)
                Assert.True(TextPolicy.TryValidate(n.GetString(), TextKind.Notes, 500, out _), file);
        }
        Assert.True(any);
    }

    [Fact]
    public void Every_invalid_redteam_title_and_notes_fixture_is_rejected_by_the_server_TextPolicy()
    {
        var checkedTitles = 0; var checkedNotes = 0;
        foreach (var (file, root) in Fixtures("invalid", "syllabus-item-create-request--"))
        {
            if ((file.Contains("title.json", StringComparison.Ordinal) && file.Contains("redteam", StringComparison.Ordinal)) || file.Contains("textpolicy-astral-201", StringComparison.Ordinal))
            {
                checkedTitles++;
                Assert.False(TextPolicy.TryValidate(root.GetProperty("title").GetString(), TextKind.SingleLine, 200, out _), file);
            }
            else if (file.Contains("redteam-notes", StringComparison.Ordinal))
            {
                checkedNotes++;
                Assert.False(TextPolicy.TryValidate(root.GetProperty("notes").GetString(), TextKind.Notes, 500, out _), file);
            }
        }
        Assert.True(checkedTitles >= 30, "title fixtures checked: " + checkedTitles);
        Assert.True(checkedNotes >= 7, "notes fixtures checked: " + checkedNotes);
    }

    [Theory]
    [InlineData("Exam 1\n")] [InlineData("Exam 1\r\n")] [InlineData(" Exam 1")] [InlineData("Exam 1 ")] [InlineData("Exam  1")] [InlineData("Exam\t1")]
    [InlineData("\u00A0")] [InlineData("\u3000")] [InlineData("Exam\u00A01")] [InlineData("a\u200Bb")] [InlineData("a\u202Eb")] [InlineData("a\uFEFFb")]
    [InlineData("a\u00ADb")] [InlineData("\uFFFD")] [InlineData("a\uE000b")] [InlineData("a\u0378b")] [InlineData("\u0483a")] [InlineData("a\u2028b")]
    [InlineData("a\u061Cb")] [InlineData("a\u2066b")] [InlineData("a\u034Fb")] [InlineData("a\u2800b")] [InlineData("a\uFFFCb")] [InlineData("a\u180Eb")]
    [InlineData("a\uFE01b")] [InlineData("a\U000E0100b")] [InlineData("a\U000E0041b")] [InlineData("a\U000F0000b")] [InlineData("a\U0001D173b")]
    public void TextPolicy_rejects_single_line_hazards(string s) => Assert.False(TextPolicy.TryValidate(s, TextKind.SingleLine, 200, out _));

    [Fact]
    public void TextPolicy_rejects_lone_surrogates_and_long_runs_of_marks_and_vs16_after_whitespace()
    {
        Assert.False(TextPolicy.TryValidate("a" + (char)0xD800 + "b", TextKind.SingleLine, 200, out _));
        Assert.False(TextPolicy.TryValidate("a" + (char)0xDC00 + "b", TextKind.SingleLine, 200, out _));
        Assert.False(TextPolicy.TryValidate("a" + (char)0xDC00 + (char)0xD800 + "b", TextKind.SingleLine, 200, out _));      // reversed pair
        Assert.False(TextPolicy.TryValidate("a" + new string((char)0x05B0, 5), TextKind.SingleLine, 200, out _));            // 5 Hebrew points in a row
        Assert.False(TextPolicy.TryValidate("a" + new string((char)0x064B, 5), TextKind.SingleLine, 200, out _));            // 5 Arabic marks in a row
        Assert.True(TextPolicy.TryValidate("a" + new string((char)0x0301, 4), TextKind.SingleLine, 200, out _));             // up to 4 is fine
        Assert.False(TextPolicy.TryValidate("\u00A0\uFE0F", TextKind.Notes, 500, out _));                                    // VS16 after NBSP
        Assert.False(TextPolicy.TryValidate("a\u3000\uFE0F", TextKind.Notes, 500, out _));                                   // VS16 after U+3000
        Assert.False(TextPolicy.TryValidate("a \uFE0F", TextKind.Notes, 500, out _));
        Assert.False(TextPolicy.TryValidate("ab\uFE0F", TextKind.SingleLine, 200, out _));                                   // VS16 after an ASCII letter
    }

    [Fact]
    public void TextPolicy_counts_code_points_not_utf16_units()
    {
        var emoji200 = string.Concat(Enumerable.Repeat("\U0001F600", 200));
        Assert.Equal(400, emoji200.Length);
        Assert.True(TextPolicy.TryValidate(emoji200, TextKind.SingleLine, 200, out _));
        Assert.False(TextPolicy.TryValidate(emoji200 + "\U0001F600", TextKind.SingleLine, 200, out _));
        Assert.Equal(200, TextPolicy.CodePoints(emoji200));
    }

    [Fact]
    public void TextPolicy_allows_zwnj_and_zwj_only_between_letters_or_emoji()
    {
        Assert.True(TextPolicy.TryValidate("\u0645\u06CC\u200C\u062E\u0648\u0627\u0647\u0645", TextKind.SingleLine, 200, out _));        // Persian
        Assert.True(TextPolicy.TryValidate("\U0001F468\u200D\U0001F469\u200D\U0001F467", TextKind.SingleLine, 200, out _));               // ZWJ family emoji
        Assert.True(TextPolicy.TryValidate("1\uFE0F\u20E3", TextKind.SingleLine, 200, out _));                                          // keycap
        Assert.True(TextPolicy.TryValidate("\u2764\uFE0F", TextKind.SingleLine, 200, out _));                                           // VS16 emoji
        Assert.False(TextPolicy.TryValidate("\u200Dab", TextKind.SingleLine, 200, out _));
        Assert.False(TextPolicy.TryValidate("ab\u200D", TextKind.SingleLine, 200, out _));
        Assert.False(TextPolicy.TryValidate("a\u200D\u200Db", TextKind.SingleLine, 200, out _));
        Assert.False(TextPolicy.TryValidate("a \u200Db", TextKind.SingleLine, 200, out _));
        Assert.False(TextPolicy.TryValidate("a.\u200Db", TextKind.SingleLine, 200, out _));
    }

    [Fact]
    public void TextPolicy_normalizes_to_nfc_and_keeps_verbatim_compatibility_characters()
    {
        Assert.True(TextPolicy.TryValidate("Cafe\u0301", TextKind.SingleLine, 200, out var nfc));
        // B1-10: NFC composition needs ICU. This must FAIL if normalisation is a no-op (InvariantGlobalization=true), not mirror it.
        Assert.Equal("Caf\u00E9", nfc);
        Assert.NotEqual("Cafe\u0301", nfc);
        Assert.True(TextPolicy.NormalizationAvailable);
        Assert.False(AppContext.TryGetSwitch("System.Globalization.Invariant", out var inv) && inv);
        // A decomposed sequence and its precomposed form are equal after the policy, so duplicate detection and display agree.
        Assert.True(TextPolicy.TryValidate("e\u0301", TextKind.SingleLine, 200, out var decomposed));
        Assert.True(TextPolicy.TryValidate("\u00E9", TextKind.SingleLine, 200, out var precomposed));
        Assert.Equal(precomposed, decomposed);
        Assert.Equal(1, decomposed.Length);
        Assert.True(TextPolicy.TryValidate("\uFB01nal", TextKind.SingleLine, 200, out var lig));     // NFC, not NFKC: the ligature stays
        Assert.Equal("\uFB01nal", lig);
    }

    [Fact]
    public void Notes_allow_lf_and_nbsp_inside_a_line_but_at_most_ten_lines()
    {
        Assert.True(TextPolicy.TryValidate("a\nb\u00A0c", TextKind.Notes, 500, out _));
        Assert.True(TextPolicy.TryValidate(string.Join("\n", Enumerable.Repeat("x", 10)), TextKind.Notes, 500, out _));
        Assert.False(TextPolicy.TryValidate(string.Join("\n", Enumerable.Repeat("x", 11)), TextKind.Notes, 500, out _));
        Assert.False(TextPolicy.TryValidate("a\r\nb", TextKind.Notes, 500, out _));
        Assert.False(TextPolicy.TryValidate("a\n", TextKind.Notes, 500, out _));
    }

    // ---------------------------------------------------------------- document canonicalization

    [Fact]
    public void Document_text_strips_hidden_characters_counts_them_and_flags_tag_chars_as_suspect()
    {
        var tag = char.ConvertFromUtf32(0xE0041);
        var doc = DocumentText.Canonicalize("Exam on Oct 19" + tag + "\u202E hidden \u200B text\r\n  Second   line  ");
        Assert.DoesNotContain(tag, doc.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("\u202E", doc.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("\u200B", doc.Text, StringComparison.Ordinal);
        Assert.Equal("Exam on Oct 19 hidden text\nSecond line", doc.Text);
        Assert.True(doc.HiddenRemoved >= 3);
        Assert.True(doc.InjectionSuspect);                         // a tag character and a bidi override
    }

    [Fact]
    public void Document_text_ordinary_soft_hyphens_and_a_leading_bom_are_not_signals()
    {
        var doc = DocumentText.Canonicalize("\uFEFFsoft\u00ADhyphen line");
        Assert.Equal("softhyphen line", doc.Text);
        Assert.Equal(0, doc.HiddenRemoved);
        Assert.False(doc.InjectionSuspect);
    }

    [Fact]
    public void Document_text_thirty_thousand_emoji_is_valid_and_counts_code_points()
    {
        var doc = DocumentText.Canonicalize(string.Concat(Enumerable.Repeat("\U0001F600", 30_000)));
        Assert.Equal(30_000, doc.CodePointCount);
        Assert.Equal(60_000, doc.Utf16Length);
    }

    [Fact]
    public void Document_text_keeps_nfc_and_normalizes_whitespace_and_lines()
    {
        var doc = DocumentText.Canonicalize("Line\u00A0one\t\tx\n\n\n\nLine two\u2028Line three");
        Assert.Equal("Line one x\n\nLine two\nLine three", doc.Text);
        Assert.Equal([(0, 10), (12, 20), (21, 31)], DocumentText.LineSpans(doc.Text).Select(l => (l.Start, l.End)).ToArray());
    }

    [Fact]
    public void SBV04_stripping_a_hidden_character_between_a_letter_and_its_mark_still_yields_nfc()
    {
        // "e" ZWSP U+0301 is NFC (the ZWSP blocks composition). Stripping the ZWSP leaves "e" U+0301, which is NOT NFC: the contract
        // (API_CONTRACTS_P2.md section 2.2) says the canonical text is NFC, so it must come out as the precomposed U+00E9.
        var input = "e\u200B\u0301";
        Assert.True(input.IsNormalized(NormalizationForm.FormC));          // the probe really is NFC before stripping
        var doc = DocumentText.Canonicalize(input);
        Assert.Equal("\u00E9", doc.Text);
        Assert.True(doc.Text.IsNormalized(NormalizationForm.FormC));
        Assert.Equal(1, doc.HiddenRemoved);                                // the stripped ZWSP is still counted
    }

    /// <summary>
    /// SBV-04 hostile corpus: every input hides a stripped character (format, bidi, tag, variation selector, filler, BOM, line break kind)
    /// between characters that compose or reorder under NFC once it is gone. The canonical text must be NFC, canonicalization must be
    /// idempotent (a second pass changes nothing), and the line spans must cover the final string (all offsets are into the final text).
    /// </summary>
    [Theory]
    [InlineData("e\u200B\u0301", "\u00E9")]                                           // ZWSP (Cf)
    [InlineData("A\u2060\u030A", "\u00C5")]                                           // WORD JOINER (Cf)
    [InlineData("e\u202E\u0301", "\u00E9")]                                           // RLO (bidi override, suspect)
    [InlineData("e\U000E0041\u0301", "\u00E9")]                                       // tag character
    [InlineData("e\uFE01\u0301", "\u00E9")]                                           // variation selector not after an emoji
    [InlineData("e\U000E0100\u0301", "\u00E9")]                                       // variation selector supplement
    [InlineData("e\u3164\u0301", "\u00E9")]                                           // Hangul filler
    [InlineData("xe\uFEFF\u0301", "x\u00E9")]                                         // BOM that is not at the start
    [InlineData("\u1100\u200B\u1161", "\uAC00")]                                      // Hangul L + V compose to a syllable
    [InlineData("q\u0301\u200B\u0316", "q\u0316\u0301")]                                    // marks reordered by canonical class (230 after 220)
    [InlineData("Midterm in Salle Ve\u200B\u0301rone: Oct 19, 2026", "Midterm in Salle V\u00E9rone: Oct 19, 2026")]
    [InlineData("line one\r\nCafe\u200D\u200B\u0301\nend", "line one\nCaf\u00E9\nend")]  // stray ZWJ + ZWSP, CRLF
    public void SBV04_canonical_text_is_nfc_idempotent_and_spans_cover_the_final_string(string raw, string expectedSourceOrEmpty)
    {
        var doc = DocumentText.Canonicalize(raw);
        Assert.True(doc.Text.IsNormalized(NormalizationForm.FormC), Escape(doc.Text));
        Assert.Equal(doc.Text.Normalize(NormalizationForm.FormC), doc.Text);
        var expected = expectedSourceOrEmpty.Normalize(NormalizationForm.FormC);
        Assert.Equal(Escape(expected), Escape(doc.Text));
        Assert.True(doc.HiddenRemoved >= 1);
        Assert.Equal(doc.Text, DocumentText.Canonicalize(doc.Text).Text);             // idempotent
        foreach (var (start, end) in DocumentText.LineSpans(doc.Text))
            Assert.InRange(end, start + 1, doc.Text.Length);
        Assert.Equal(doc.Utf16Length, doc.Text.Length);
    }

    private static string Escape(string s) => string.Concat(s.Select(c => c < 0x80 ? c.ToString() : $"\\u{(int)c:X4}"));

    // ---------------------------------------------------------------- strict binding (R2-05, R3-02)

    [Fact]
    public void Binder_rejects_duplicate_dictionary_members_and_a_default_serializer_would_collapse_them()
    {
        const string dup = """{"action":"approve","itemEtags":{"itm_a":"W/\"1\"","itm_a":"W/\"2\""}}""";
        Assert.False(JsonBody.TryBind<BatchRequest>(Encoding.UTF8.GetBytes(dup), out _));
        // negative control: the default options silently keep the last one, so the strict setting is what makes the difference
        var collapsed = JsonSerializer.Deserialize<BatchRequest>(dup, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Single(collapsed!.ItemEtags);
    }

    [Fact]
    public void Binder_rejects_unknown_and_readonly_members_and_wrong_types_and_missing_required()
    {
        foreach (var body in new[]
        {
            """{"kind":"other","title":"t","date":"2026-09-20","origin":"ai"}""", """{"kind":"other","title":"t","date":"2026-09-20","userId":"x"}""",
            """{"kind":"other","title":"t","date":"2026-09-20","confidence":"looks_right"}""", """{"kind":"other","title":"t"}""",
            """{"kind":"other","title":null,"date":"2026-09-20"}""", """{"kind":"other","title":5,"date":"2026-09-20"}""",
            """{"kind":"other","title":"t","date":"2026-09-20","allDay":"yes"}""", """{"kind":"other","title":"t","date":"2026-09-20"} trailing""",
        })
            Assert.False(JsonBody.TryBind<ItemCreateRequest>(Encoding.UTF8.GetBytes(body), out _), body);
        Assert.True(JsonBody.TryBind<ItemCreateRequest>(Encoding.UTF8.GetBytes("""{"kind":"other","title":"t","date":"2026-09-20"}"""), out _));
    }

    private static byte[] Join(string a, byte[] mid, string b) => [.. Encoding.UTF8.GetBytes(a), .. mid, .. Encoding.UTF8.GetBytes(b)];

    [Fact]
    public void Binder_maps_lone_surrogate_escapes_and_invalid_utf8_to_a_clean_failure_never_an_exception()
    {
        var bodies = new List<byte[]>
        {
            Encoding.UTF8.GetBytes("""{"kind":"other","title":"a\ud800b","date":"2026-09-20"}"""),               // lone high surrogate
            Encoding.UTF8.GetBytes("""{"kind":"other","title":"a\udc00b","date":"2026-09-20"}"""),               // lone low surrogate
            Encoding.UTF8.GetBytes("""{"kind":"other","title":"a\udc00\ud800b","date":"2026-09-20"}"""),         // reversed pair
            Join("{\"kind\":\"other\",\"title\":\"a", [0xFF], "b\",\"date\":\"2026-09-20\"}"),                           // 0xFF is never valid UTF-8
            Join("{\"kind\":\"other\",\"title\":\"a", [0xED, 0xA0, 0x80], "b\",\"date\":\"2026-09-20\"}"),               // CESU-8 surrogate encoding
        };
        foreach (var b in bodies) Assert.False(JsonBody.TryBind<ItemCreateRequest>(b, out _));
    }

    // ---------------------------------------------------------------- strict wall-clock parsing and zones

    [Theory]
    [InlineData("2026-02-29")] [InlineData("2026-02-30")] [InlineData("9999-01-01")] [InlineData("1999-12-31")] [InlineData("2101-01-01")]
    [InlineData("2026-09-20\n")] [InlineData("2026-9-20")] [InlineData("2026/09/20")] [InlineData("2026-09-20T00:00:00Z")] [InlineData("")]
    public void Dates_must_be_real_calendar_dates_in_2000_2100_without_trailing_characters(string s) => Assert.False(Local.TryDate(s, out _));

    [Theory]
    [InlineData("2028-02-29")] [InlineData("2000-01-01")] [InlineData("2100-12-31")]
    public void Real_dates_parse(string s) => Assert.True(Local.TryDate(s, out _));

    [Theory]
    [InlineData("24:00")] [InlineData("12:60")] [InlineData("9:00")] [InlineData("09:00:00")] [InlineData("09:00\n")] [InlineData("0900")]
    public void Times_must_be_hh_mm(string s) => Assert.False(Local.TryTime(s, out _));

    [Fact]
    public void Zone_allow_list_rejects_abbreviations_offsets_and_unknown_zones()
    {
        Assert.True(ZoneAllowList.Contains("America/New_York"));
        Assert.True(ZoneAllowList.Contains("Etc/UTC")); // what the web client sends for a browser that reports UTC
        foreach (var z in new[] { "EST", "UTC", "GMT", "Etc/GMT", "etc/utc", "Etc/UTC\n", "Etc/UTC ", "Etc/GMT+5", "-05:00", "America/Nowhere", "america/new_york", "America/New_York\n", "" }) Assert.False(ZoneAllowList.Contains(z), z);
    }

    [Fact]
    public void Etc_UTC_resolves_on_this_host_and_has_no_dst_gap()
    {
        ZoneAllowList.EnsureHostSupportsAll();
        Assert.False(Local.IsInDstGap(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), "Etc/UTC"));
    }

    [Fact]
    public void Spring_forward_gap_is_detected_in_a_real_zone()
    {
        Assert.True(Local.IsInDstGap(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), "America/New_York"));
        Assert.False(Local.IsInDstGap(new DateOnly(2026, 3, 8), new TimeOnly(3, 30), "America/New_York"));
    }

    [Fact]
    public void Idempotency_key_format_is_8_to_128_url_safe_characters_with_no_trailing_newline()
    {
        Assert.True(SyllabusService.ValidKey("abcdefgh"));
        Assert.True(SyllabusService.ValidKey(new string('a', 128)));
        foreach (var k in new[] { "short", new string('a', 129), "has space1", "abcdefgh\n", "abc/defgh" }) Assert.False(SyllabusService.ValidKey(k), k);
        Assert.False(SyllabusService.ValidKey(null));
    }
}

public class NormalizationGuardTests
{
    [Fact]
    public void Startup_guard_fails_closed_when_normalisation_is_unavailable_and_passes_when_it_works()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => TextPolicy.EnsureNormalizationAvailable(false));
        Assert.Contains("ICU", ex.Message);
        TextPolicy.EnsureNormalizationAvailable(true);
        TextPolicy.EnsureNormalizationAvailable(TextPolicy.NormalizationAvailable); // this host must be able to compose
    }
}

/// <summary>B1-14: a host without tzdata must not silently report "no DST gap" (that would let a nonexistent local time through unflagged).</summary>
public class TimeZoneFailClosedTests
{
    [Fact]
    public void A_zone_the_host_cannot_resolve_is_an_explicit_error_not_false()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Local.IsInDstGap(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), "Nowhere/Missing_Zone"));
        Assert.Contains("tzdata", ex.Message);
    }

    [Fact]
    public void Startup_check_names_every_allow_listed_zone_the_host_lacks()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ZoneAllowList.EnsureHostSupportsAll(z => z is not ("America/New_York" or "Europe/Kyiv")));
        Assert.Contains("America/New_York", ex.Message);
        Assert.Contains("Europe/Kyiv", ex.Message);
        Assert.Contains("tzdata", ex.Message);
    }

    [Fact]
    public void This_host_resolves_every_allow_listed_zone_and_still_detects_the_spring_forward_gap()
    {
        ZoneAllowList.EnsureHostSupportsAll();
        Assert.True(Local.IsInDstGap(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), "America/New_York"));
    }
}
