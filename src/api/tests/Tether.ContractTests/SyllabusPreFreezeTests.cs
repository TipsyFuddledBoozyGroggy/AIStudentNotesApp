using System.Text.Json.Nodes;
using Json.Schema;

namespace Tether.ContractTests;

/// <summary>
/// Pre-freeze checks for the DRAFT syllabus.v1 contract (docs/security/p2-contract-rereview.md R2-01..R2-06).
/// The definition theory evaluates the REAL schema definitions with JsonSchema.Net (not a hand-written Regex) and covers
/// values that cannot live in a fixture file (lone surrogates are not valid JSON text for System.Text.Json).
/// Like all contract tests it proves shape only; the server TextPolicy, the id parser and the binder are still required
/// server tests (docs/API_CONTRACTS_P2.md section 12).
/// </summary>
public sealed class SyllabusPreFreezeTests
{
    private static readonly string ContractsDir = FindContractsDir();
    private static readonly string SchemaDir = Path.Combine(ContractsDir, "syllabus.v1");

    private static string FindContractsDir()
    {
        var env = Environment.GetEnvironmentVariable("TETHER_CONTRACTS_DIR");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "src", "shared", "contracts");
            if (Directory.Exists(candidate)) return candidate;
            candidate = Path.Combine(d.FullName, "shared", "contracts");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("src/shared/contracts not found; set TETHER_CONTRACTS_DIR.");
    }

    private static IEnumerable<(string File, JsonNode Node)> Fixtures(string kind) =>
        Directory.GetFiles(Path.Combine(ContractsDir, "fixtures", kind), "syllabus*.json").Order()
            .Select(f => (Path.GetFileName(f), JsonNode.Parse(File.ReadAllText(f))!));

    private static readonly Lazy<EvaluationOptions> DefOptions = new(() =>
    {
        var o = new EvaluationOptions { OutputFormat = OutputFormat.List, EvaluateAs = SpecVersion.Draft202012 };
        foreach (var f in Directory.GetFiles(SchemaDir, "*.schema.json"))
            o.SchemaRegistry.Register(JsonSchema.FromText(File.ReadAllText(f)));
        return o;
    });

    private static bool DefIsValid(string def, string value)
    {
        var schema = JsonSchema.FromText("{\"$ref\":\"https://schemas.tether.invalid/syllabus.v1/syllabus-common.schema.json#/$defs/" + def + "\"}");
        return schema.Evaluate(JsonValue.Create(value), DefOptions.Value).IsValid;
    }

    public static IEnumerable<object[]> DefCases()
    {
        var a32 = new string('a', 32);
        var a64 = new string('a', 64);
        // R2-01: a single trailing LF must never validate, for every anchored pattern.
        yield return new object[] { "title", "Exam 1", true };
        yield return new object[] { "title", "Exam 1\n", false };
        yield return new object[] { "title", "Exam 1\r\n", false };
        yield return new object[] { "textLine", "Full date written", true };
        yield return new object[] { "textLine", "Full date written\n", false };
        yield return new object[] { "notes", "Bring ID\nRoom 204", true };
        yield return new object[] { "notes", "Bring ID\n", false };
        yield return new object[] { "notes", "", true };
        yield return new object[] { "id", "itm_01", true };
        yield return new object[] { "id", "itm_01\n", false };
        yield return new object[] { "id", "\nitm_01", false };
        yield return new object[] { "id", "itm_0f8fad5b-d9cb-469f-a165-70867728950e", true };
        yield return new object[] { "id", "ITM_01", false };
        yield return new object[] { "id", "itm_0F8FAD5B-D9CB-469F-A165-70867728950E", false };
        yield return new object[] { "id", "itm_{0f8fad5b-d9cb-469f-a165-70867728950e}", false };
        yield return new object[] { "itemKey", "ik_" + a32, true };
        yield return new object[] { "itemKey", "ik_" + a32 + "\n", false };
        yield return new object[] { "timestamp", "2026-10-28T12:00:00.000Z", true };
        yield return new object[] { "timestamp", "2026-10-28T12:00:00.000Z\n", false };
        yield return new object[] { "localDate", "2026-10-19", true };
        yield return new object[] { "localDate", "2026-10-19\n", false };
        yield return new object[] { "localTime", "10:00", true };
        yield return new object[] { "localTime", "10:00\n", false };
        yield return new object[] { "ianaZone", "America/New_York", true };
        yield return new object[] { "ianaZone", "America/New_York\n", false };
        yield return new object[] { "sha256Hex", a64, true };
        yield return new object[] { "sha256Hex", a64 + "\n", false };
        yield return new object[] { "noticeVersion", "draft-2026-10", true };
        yield return new object[] { "noticeVersion", "draft-2026-10\n", false };
        yield return new object[] { "idempotencyKey", "abcdefgh", true };
        yield return new object[] { "idempotencyKey", "abcdefgh\n", false };
        // R2-02: Unicode-category bypasses found by the re-review probe.
        yield return new object[] { "title", "Exam︀ 1", false };                 // variation selector
        yield return new object[] { "title", "Exam \U000E0100 1", false };            // variation selector supplement
        yield return new object[] { "title", "Exam͏ 1", false };                 // combining grapheme joiner
        yield return new object[] { "title", "Examㅤ 1", false };                 // Hangul filler
        yield return new object[] { "title", "Examᅟ 1", false };
        yield return new object[] { "title", "Exam⠀ 1", false };                 // braille blank
        yield return new object[] { "title", "Exam￼ 1", false };                 // object replacement
        yield return new object[] { "title", "Exam \U0001D173 1", false };            // Cf musical control
        yield return new object[] { "title", "Exam �", false };                  // replacement char (how a lone surrogate arrives)
        yield return new object[] { "title", " ", false };                       // NBSP, whole value
        yield return new object[] { "title", "　", false };                       // ideographic space, whole value
        yield return new object[] { "title", " Exam", false };
        yield return new object[] { "title", "Exam ", false };
        yield return new object[] { "title", "́Exam", false };                   // stray combining mark
        yield return new object[] { "title", "a᪰᪱᪲᪳᪴", false };   // marks outside the U+0300 block
        yield return new object[] { "title", "a⃒⃓⃐⃑⃔", false };
        yield return new object[] { "title", "á̂̃̄", true };   // four marks are fine
        yield return new object[] { "title", "Exam ", false };                  // private use
        // R2-03: ZWNJ/ZWJ only between letters/emoji, so Persian and ZWJ emoji work.
        yield return new object[] { "title", "می‌خواهم", true };
        yield return new object[] { "title", "Study \U0001F468‍\U0001F469‍\U0001F467", true };
        yield return new object[] { "title", "❤️ Review", true };
        yield return new object[] { "title", "क्‍ष", true };
        yield return new object[] { "title", "‌Exam", false };
        yield return new object[] { "title", "Exam‍", false };
        yield return new object[] { "title", "a‍‍b", false };
        yield return new object[] { "title", "a ‍b", false };
        yield return new object[] { "title", "Exam​1", false };                  // ZWSP stays rejected
        yield return new object[] { "title", "‮exam", false };                   // RLO stays rejected
        yield return new object[] { "title", "‏exam", false };                   // RLM stays rejected
        yield return new object[] { "title", "\U0001F600 and CJK \U00020BB7", true };
    }

    [Theory, MemberData(nameof(DefCases))]
    public void Text_and_id_definitions_follow_the_unicode_rules_under_a_real_json_schema_evaluation(string def, string value, bool expected)
        => Assert.Equal(expected, DefIsValid(def, value));

    [Fact]
    public void The_title_pattern_itself_rejects_lone_surrogates_and_plane_15_16_private_use()
    {
        // JSON decoding turns a lone surrogate into U+FFFD before the schema sees it (also rejected), but the pattern must
        // not depend on that: evaluate it as a .NET regex on the raw UTF-16 string.
        var common = JsonNode.Parse(File.ReadAllText(Path.Combine(SchemaDir, "syllabus-common.schema.json")))!["$defs"]!;
        var re = new System.Text.RegularExpressions.Regex(common["title"]!["pattern"]!.GetValue<string>());
        foreach (var value in new[] { "Exam \uD800", "Exam \uDC00 1", "Exam \uDB40", "􀀀 x" })
            Assert.DoesNotMatch(re, value);
        Assert.Matches(re, "Exam \uD83D\uDE00 ok");
    }

    [Fact]
    public void No_pattern_in_any_schema_ends_in_a_dollar_anchor()
    {
        // Under .NET (and ECMAScript) semantics $ matches before a final LF; every pattern must use (?![\s\S]) instead (R2-01).
        var seen = 0;
        foreach (var f in Directory.GetFiles(SchemaDir, "*.schema.json"))
            Walk(JsonNode.Parse(File.ReadAllText(f))!, Path.GetFileName(f));
        Assert.True(seen >= 15, $"expected to inspect many patterns, saw {seen}");

        void Walk(JsonNode? n, string where)
        {
            switch (n)
            {
                case JsonObject o:
                    foreach (var kv in o)
                    {
                        if (kv.Key == "pattern" && kv.Value is JsonValue v && v.TryGetValue<string>(out var pat))
                        {
                            seen++;
                            Assert.False(pat.EndsWith('$'), $"{where}: pattern ends in $ (matches before a trailing LF): {pat}");
                            Assert.EndsWith(@"(?![\s\S])", pat);
                        }
                        else Walk(kv.Value, where);
                    }
                    break;
                case JsonArray a:
                    foreach (var i in a) Walk(i, where);
                    break;
            }
        }
    }

    private sealed record BatchDto(string Action, Dictionary<string, string> ItemEtags);

    [Fact]
    public void Typed_binding_with_the_duplicate_member_option_rejects_duplicates_in_dtos_and_dictionaries_and_unknown_members()
    {
        // R2-05: the option must be on the SERIALIZER options used for binding (not only JsonDocument).
        var strict = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            AllowDuplicateProperties = false,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        };
        var dupKey = "{\"action\":\"skip\",\"itemEtags\":{\"itm_1\":\"a\",\"itm_1\":\"b\"}}";
        var dupProp = "{\"action\":\"skip\",\"action\":\"approve\",\"itemEtags\":{\"itm_1\":\"a\"}}";
        var unknown = "{\"action\":\"skip\",\"itemEtags\":{\"itm_1\":\"a\"},\"userId\":\"u\"}";
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<BatchDto>(dupKey, strict));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<BatchDto>(dupProp, strict));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<BatchDto>(unknown, strict));
        var ok = System.Text.Json.JsonSerializer.Deserialize<BatchDto>("{\"action\":\"skip\",\"itemEtags\":{\"itm_1\":\"a\"}}", strict)!;
        Assert.Single(ok.ItemEtags);

        // Negative control: with default serializer options the duplicate key is silently collapsed (last wins). This is the
        // hole the contract closes by requiring the option on the binder.
        var lax = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var collapsed = System.Text.Json.JsonSerializer.Deserialize<BatchDto>(dupKey, lax)!;
        Assert.Single(collapsed.ItemEtags);
        Assert.Equal("b", collapsed.ItemEtags["itm_1"]);
    }

    [Fact]
    public void Canonical_id_form_is_lowercase_hyphenated_and_every_fixture_id_uses_it()
    {
        var re = new System.Text.RegularExpressions.Regex(@"^(?:[a-z]{3}_)?[a-z0-9]+(?:-[a-z0-9]+)*(?![\s\S])");
        var seen = 0;
        foreach (var (file, node) in Fixtures("valid")) Walk(node);
        Assert.True(seen >= 50, $"checked {seen} ids");

        void Walk(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o:
                    foreach (var kv in o)
                    {
                        if (kv.Key is "id" or "syllabusId" or "courseId" or "itemId" or "existingSyllabusId" && kv.Value is JsonValue v && v.TryGetValue<string>(out var s))
                        {
                            Assert.Matches(re, s);
                            seen++;
                        }
                        Walk(kv.Value);
                    }
                    break;
                case JsonArray a:
                    foreach (var i in a) Walk(i);
                    break;
            }
        }
    }

    [Fact]
    public void Purged_snippets_have_null_text_and_context_together()
    {
        var purged = 0;
        foreach (var (_, node) in Fixtures("valid")) Walk(node);
        Assert.True(purged >= 1, "expected at least one purged-snippet fixture");

        void Walk(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o:
                    if (o.ContainsKey("precision") && o.ContainsKey("context") && o.ContainsKey("text"))
                    {
                        var textNull = o["text"] is null; var ctxNull = o["context"] is null;
                        Assert.Equal(textNull, ctxNull);
                        if (textNull) purged++;
                    }
                    foreach (var kv in o) Walk(kv.Value);
                    break;
                case JsonArray a:
                    foreach (var i in a) Walk(i);
                    break;
            }
        }
    }

    [Fact]
    public void Required_pre_freeze_fixtures_exist()
    {
        var valid = Fixtures("valid").Select(f => f.File).ToHashSet();
        var invalid = Fixtures("invalid").Select(f => f.File).ToHashSet();
        string[] mustBeRejected =
        {
            "syllabus-item-create-request--redteam-trailing-lf-title.json", "syllabus-item-create-request--redteam-trailing-lf-date.json",
            "syllabus-decision-batch-request--redteam-trailing-lf-id.json", "syllabus-calendar-sync-request--redteam-trailing-lf-id.json",
            "syllabus--redteam-trailing-lf-hash.json", "syllabus-idempotency-key--redteam-trailing-lf.json",
            "syllabus-item-create-request--redteam-variation-selector-supplement-title.json", "syllabus-item-create-request--redteam-hangul-filler-3164-title.json",
            "syllabus-item-create-request--redteam-braille-blank-title.json", "syllabus-item-create-request--redteam-musical-format-control-title.json",
            "syllabus-item-create-request--redteam-nbsp-whole-title.json", "syllabus-item-create-request--redteam-ideographic-space-whole-title.json",
            "syllabus-item-create-request--redteam-orphan-combining-mark-title.json", "syllabus-item-create-request--redteam-zwj-doubled-title.json",
            "syllabus--hidden-removed-not-dropped.json", "syllabus--hidden-removed-batch-allowed.json",
            "syllabus-item--snippet-text-null-context-present.json", "syllabus-item--sync-failed-not-approved.json",
            "syllabus-item--synced-null-revision.json", "syllabus-item--ai-edited-date-probably-right.json",
            "syllabus-decision-batch-request--non-canonical-id-uppercase.json", "syllabus-calendar-sync-request--revision-over-int32.json",
        };
        string[] mustBeAccepted =
        {
            "syllabus-item-create-request--textpolicy-persian-zwnj-title.json", "syllabus-item-create-request--textpolicy-zwj-emoji-family-title.json",
            "syllabus-item-create-request--textpolicy-emoji-vs16-title.json", "syllabus-item--snippet-text-purged-after-retention.json",
            "syllabus-item--sync-failed-never-synced.json", "syllabus-item--ai-edited-date-please-check.json",
            "syllabus--redteam-tag-and-bidi-chars-stripped.json", "syllabus-idempotency-key--basic.json",
            "syllabus-problem--errors-key-with-hyphenated-id.json", "syllabus-decision-batch-request--canonical-guid-id.json",
        };
        foreach (var f in mustBeRejected) Assert.Contains(f, invalid);
        foreach (var f in mustBeAccepted) Assert.Contains(f, valid);
    }
}
