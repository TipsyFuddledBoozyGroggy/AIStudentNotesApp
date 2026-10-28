using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Tether.ContractTests;

/// <summary>
/// Semantic checks for the DRAFT syllabus.v1 contract (docs/API_CONTRACTS_P2.md) that JSON Schema alone cannot express
/// or that guard against drift between documents, schemas and fixtures. Schema validation of every fixture is done by
/// <see cref="ContractFixtureTests"/> (valid/invalid theories); lecture.v1 is not touched here.
/// </summary>
public sealed class SyllabusContractTests
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

    private static string? FindDoc()
    {
        for (var d = new DirectoryInfo(ContractsDir); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "docs", "API_CONTRACTS_P2.md");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static IEnumerable<string> SchemaNames() =>
        Directory.GetFiles(SchemaDir, "*.schema.json").Select(f => Path.GetFileName(f)[..^".schema.json".Length]).Order();

    private static IEnumerable<(string File, JsonNode Node)> Fixtures(string kind) =>
        Directory.GetFiles(Path.Combine(ContractsDir, "fixtures", kind), "syllabus*.json").Order()
            .Select(f => (Path.GetFileName(f), JsonNode.Parse(File.ReadAllText(f))!));

    private static string SchemaOf(string file) => file[..file.IndexOf("--", StringComparison.Ordinal)];

    [Fact]
    public void Schema_set_is_present_and_every_name_is_syllabus_prefixed()
    {
        var names = SchemaNames().ToList();
        Assert.True(names.Count >= 20, $"expected the full syllabus.v1 set, found {names.Count}");
        Assert.All(names, n => Assert.StartsWith("syllabus", n));
    }

    [Fact]
    public void Every_schema_has_at_least_one_valid_and_one_invalid_fixture()
    {
        var valid = Fixtures("valid").Select(f => SchemaOf(f.File)).ToHashSet();
        var invalid = Fixtures("invalid").Select(f => SchemaOf(f.File)).ToHashSet();
        foreach (var n in SchemaNames().Where(n => n != "syllabus-common"))
        {
            Assert.Contains(n, valid);
            Assert.Contains(n, invalid);
        }
    }

    [Fact]
    public void Every_object_schema_is_strict()
    {
        foreach (var name in SchemaNames())
            AssertStrict(JsonNode.Parse(File.ReadAllText(Path.Combine(SchemaDir, name + ".schema.json")))!, name);
    }

    private static void AssertStrict(JsonNode? node, string where)
    {
        switch (node)
        {
            case JsonObject o:
                if (o.ContainsKey("properties") && o["type"]?.GetValue<string>() == "object")
                    Assert.True(o["additionalProperties"]?.GetValue<bool>() == false, $"{where}: object with properties must set additionalProperties:false");
                foreach (var kv in o) AssertStrict(kv.Value, where);
                break;
            case JsonArray a:
                foreach (var i in a) AssertStrict(i, where);
                break;
        }
    }

    [Fact]
    public void Confidence_is_an_enum_and_no_numeric_score_exists_anywhere()
    {
        var common = JsonNode.Parse(File.ReadAllText(Path.Combine(SchemaDir, "syllabus-common.schema.json")))!;
        var values = common["$defs"]!["confidence"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "looks_right", "probably_right", "please_check" }, values);
        foreach (var name in SchemaNames())
        {
            var text = File.ReadAllText(Path.Combine(SchemaDir, name + ".schema.json"));
            Assert.DoesNotContain("\"score\"", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("confidenceScore", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("percent", text.Replace("10 percent", ""), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Item_key_in_every_valid_fixture_matches_the_documented_derivation()
    {
        var checkedKeys = 0;
        foreach (var (file, node) in Fixtures("valid"))
        {
            Walk(node, null);

            void Walk(JsonNode? n, string? syllabusId)
            {
                switch (n)
                {
                    case JsonObject o:
                        var sid = o["syllabusId"]?.GetValue<string>() ?? syllabusId;
                        if (o["id"]?.GetValue<string>() is { } id && id.StartsWith("syl_")) sid = id; // a Syllabus resource
                        var itemId = o["itemId"]?.GetValue<string>() ?? (o.ContainsKey("itemKey") && o["id"] is not null ? o["id"]!.GetValue<string>() : null);
                        if (o["itemKey"]?.GetValue<string>() is { } key && itemId is not null && sid is not null)
                        {
                            Assert.Equal(DeriveItemKey(sid, itemId), key);
                            checkedKeys++;
                        }
                        foreach (var kv in o) Walk(kv.Value, sid);
                        break;
                    case JsonArray a:
                        foreach (var i in a) Walk(i, syllabusId);
                        break;
                }
            }
        }
        Assert.True(checkedKeys >= 20, $"expected to verify many item keys, verified {checkedKeys}");
    }

    private static string DeriveItemKey(string syllabusId, string itemId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"tether.syllabus.item.v1\n{syllabusId}\n{itemId}"));
        var b64 = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return "ik_" + b64[..32];
    }

    [Fact]
    public void Item_key_derivation_has_a_pinned_test_vector()
    {
        // Pinned so a second implementation (frontend, future Google provider) can verify itself.
        Assert.Equal("ik_" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("tether.syllabus.item.v1\nsyl_1\nitm_1"))).TrimEnd('=').Replace('+', '-').Replace('/', '_')[..32],
            DeriveItemKey("syl_1", "itm_1"));
        Assert.NotEqual(DeriveItemKey("syl_1", "itm_1"), DeriveItemKey("syl_1", "itm_2"));
        Assert.NotEqual(DeriveItemKey("syl_1", "itm_1"), DeriveItemKey("syl_2", "itm_1"));
        Assert.Equal(35, DeriveItemKey("syl_1", "itm_1").Length);
    }

    [Fact]
    public void Exact_snippets_have_offsets_that_match_their_text_length()
    {
        var seen = 0;
        foreach (var (file, node) in Fixtures("valid"))
            Walk(node);
        Assert.True(seen >= 5, $"expected to check several snippets, checked {seen}");

        void Walk(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o:
                    if (o["text"] is JsonValue t && o["start"] is JsonValue s && o["end"] is JsonValue e && o["precision"] is JsonValue p)
                    {
                        var start = s.GetValue<int>(); var end = e.GetValue<int>();
                        Assert.True(end > start, "snippet end must be greater than start");
                        if (p.GetValue<string>() == "exact") Assert.Equal(t.GetValue<string>().Length, end - start);
                        seen++;
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
    public void Valid_items_never_break_the_approval_rules()
    {
        var items = 0;
        foreach (var (file, node) in Fixtures("valid"))
            Walk(node);
        Assert.True(items >= 15, $"checked {items} items");

        void Walk(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o:
                    if (o["when"] is JsonObject w && o["decision"] is JsonValue d && o["syncState"] is JsonValue ss && o["kind"] is JsonValue k)
                    {
                        items++;
                        var status = w["status"]!.GetValue<string>();
                        if (status != "resolved") Assert.NotEqual("approved", d.GetValue<string>());
                        if (ss.GetValue<string>() != "not_synced") Assert.Equal("approved", d.GetValue<string>());
                        if (k.GetValue<string>() == "policy") Assert.Equal("not_applicable", status);
                        if (status == "needs_date") Assert.Null(w["date"]);
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
    public void Doc_exists_is_draft_and_documents_every_error_code()
    {
        var docPath = FindDoc();
        if (docPath is null) return; // contracts mounted without docs/ (standalone TETHER_CONTRACTS_DIR run)
        var doc = File.ReadAllText(docPath);
        Assert.Contains("DRAFT v1", doc);
        Assert.Contains("not frozen", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("provisional", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reconciliation", doc);
        Assert.Contains("Open decisions", doc);

        var common = JsonNode.Parse(File.ReadAllText(Path.Combine(SchemaDir, "syllabus-common.schema.json")))!["$defs"]!;
        foreach (var def in new[] { "errorCode", "itemErrorCode" })
            foreach (var code in common[def]!["enum"]!.AsArray().Select(c => c!.GetValue<string>()))
                Assert.Contains($"`{code}`", doc);
        foreach (var def in new[] { "extractionStatus", "itemKind", "confidence", "decision", "syncState" })
            foreach (var v in common[def]!["enum"]!.AsArray().Select(c => c!.GetValue<string>()))
                Assert.Contains($"`{v}`", doc);
    }

    [Fact]
    public void Only_the_mock_calendar_provider_is_expressible()
    {
        foreach (var n in new[] { "syllabus-calendar-sync-request", "syllabus-calendar-sync-result", "syllabus-calendar-remove-result" })
        {
            var text = File.ReadAllText(Path.Combine(SchemaDir, n + ".schema.json"));
            Assert.Contains("\"const\": \"mock\"", text);
            Assert.DoesNotContain("google", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- security-review fold-in (docs/security/p2-contract-review.md) ----

    private static string DocText() => FindDoc() is { } p ? File.ReadAllText(p) : "";

    [Fact]
    public void Snippet_and_context_offsets_are_utf16_code_units_including_a_non_bmp_character()
    {
        var checkedSnippets = 0;
        foreach (var (file, node) in Fixtures("valid")) Walk(file, node);
        Assert.True(checkedSnippets >= 5, $"checked {checkedSnippets}");

        void Walk(string file, JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o:
                    if (o["text"] is JsonValue t && o["start"] is JsonValue s && o["precision"] is JsonValue && o["context"] is JsonObject ctx)
                    {
                        var text = t.GetValue<string>(); var start = s.GetValue<int>(); var end = o["end"]!.GetValue<int>();
                        var ctext = ctx["text"]!.GetValue<string>(); var cs = ctx["start"]!.GetValue<int>(); var ce = ctx["end"]!.GetValue<int>();
                        Assert.True(cs <= start && end <= ce, $"{file}: context must contain the snippet");
                        Assert.Equal(ctext.Length, ce - cs);           // string.Length = UTF-16 code units
                        if (o["precision"]!.GetValue<string>() == "exact")
                            Assert.Equal(text, ctext.Substring(start - cs, text.Length));
                        checkedSnippets++;
                    }
                    foreach (var kv in o) Walk(file, kv.Value);
                    break;
                case JsonArray a:
                    foreach (var i in a) Walk(file, i);
                    break;
            }
        }

        // The emoji fixture must really diverge between code points and UTF-16 units, or it proves nothing.
        var emoji = Fixtures("valid").Single(f => f.File == "syllabus-item--snippet-emoji-before.json").Node["snippet"]!;
        var ctxText = emoji["context"]!["text"]!.GetValue<string>();
        Assert.NotEqual(ctxText.Length, ctxText.EnumerateRunes().Count());
        var before = ctxText[..(emoji["start"]!.GetValue<int>() - emoji["context"]!["start"]!.GetValue<int>())];
        Assert.Equal(3, before.Length);                     // emoji (2 units) + space
        Assert.Equal(2, before.EnumerateRunes().Count());   // a code-point implementation would be off by one
    }

    [Fact]
    public void Redteam_fixtures_exist_for_every_text_policy_and_hostile_case()
    {
        var valid = Fixtures("valid").Select(f => f.File).ToHashSet();
        var invalid = Fixtures("invalid").Select(f => f.File).ToHashSet();
        string[] mustBeRejected =
        {
            "syllabus-item-create-request--redteam-rlo-title.json", "syllabus-item-create-request--redteam-zero-width-title.json",
            "syllabus-item-create-request--redteam-crlf-title.json", "syllabus-item-create-request--redteam-nul-title.json",
            "syllabus-item-create-request--redteam-combining-marks-title.json", "syllabus-item-create-request--redteam-notes-crlf.json",
            "syllabus-item-create-request--redteam-notes-nul.json", "syllabus-item-update-request--redteam-rlo-title.json",
            "syllabus-item-create-request--readonly-origin.json", "syllabus-item-update-request--readonly-syncState.json",
            "syllabus-item-update-request--readonly-userId.json", "syllabus-item-update-request--null-title.json",
            "syllabus-item--suspect-flag-looks-right.json", "syllabus-item--supersession-probably-right.json",
            "syllabus--suspect-batch-allowed.json", "syllabus-create-request--missing-attestation.json",
            "syllabus-create-request--nul-text.json", "syllabus-decision-batch-request--items-array-shape.json",
            "syllabus-calendar-sync-request--item-ids-shape.json", "syllabus-item-create-request--year-9999.json",
        };
        string[] mustBeAccepted =
        {
            "syllabus-item-create-request--redteam-formula-title-as-text.json", "syllabus-item-create-request--redteam-html-title-as-text.json",
            "syllabus-item--redteam-supersession-cue.json", "syllabus-item--redteam-suspect-document.json",
            "syllabus--redteam-suspect-hidden-dropped.json", "syllabus-item--snippet-emoji-before.json",
        };
        foreach (var f in mustBeRejected) Assert.Contains(f, invalid);
        foreach (var f in mustBeAccepted) Assert.Contains(f, valid);
    }

    [Fact]
    public void Batch_and_sync_ids_are_object_keys_so_duplicates_are_unrepresentable_and_the_server_rejects_duplicate_members()
    {
        var batch = File.ReadAllText(Path.Combine(SchemaDir, "syllabus-decision-batch-request.schema.json"));
        var sync = File.ReadAllText(Path.Combine(SchemaDir, "syllabus-calendar-sync-request.schema.json"));
        Assert.Contains("\"itemEtags\"", batch);
        Assert.Contains("\"itemRevisions\"", sync);
        Assert.DoesNotContain("\"itemIds\": {", sync);
        // The server must parse with duplicate member names disallowed (contract section 2.1).
        var dup = "{\"action\":\"skip\",\"itemEtags\":{\"itm_1\":\"a\",\"itm_1\":\"b\"}}";
        Assert.ThrowsAny<Exception>(() =>
            System.Text.Json.JsonDocument.Parse(dup, new System.Text.Json.JsonDocumentOptions { AllowDuplicateProperties = false }));
    }

    [Theory]
    [InlineData("abcdefgh", true)]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e", true)]
    [InlineData("short", false)]
    [InlineData("has space in it", false)]
    [InlineData("new\nline-key-1", false)]
    public void Idempotency_key_format(string key, bool ok)
    {
        var common = JsonNode.Parse(File.ReadAllText(Path.Combine(SchemaDir, "syllabus-common.schema.json")))!["$defs"]!;
        var re = new System.Text.RegularExpressions.Regex(common["idempotencyKey"]!["pattern"]!.GetValue<string>());
        Assert.Equal(ok, re.IsMatch(key));
    }

    [Fact]
    public void Caps_in_schemas_match_the_documented_single_set_of_constants()
    {
        var export = JsonNode.Parse(File.ReadAllText(Path.Combine(SchemaDir, "syllabus-export.schema.json")))!["properties"]!;
        Assert.Equal(300, export["items"]!["maxItems"]!.GetValue<int>());
        Assert.Equal(200, export["unclaimedDates"]!["maxItems"]!.GetValue<int>());
        Assert.Equal(1000, export["calendarEvents"]!["maxItems"]!.GetValue<int>());
        var counts = JsonNode.Parse(File.ReadAllText(Path.Combine(SchemaDir, "syllabus.schema.json")))!["properties"]!["counts"]!["properties"]!;
        Assert.Equal(300, counts["total"]!["maximum"]!.GetValue<int>());
        Assert.Equal(200, counts["unclaimedDates"]!["maximum"]!.GetValue<int>());
        var doc = DocText();
        if (doc.Length == 0) return;
        Assert.Contains("300 items per syllabus", doc);
        Assert.Contains("1,000 events per syllabus", doc);
    }

    [Fact]
    public void Model_source_fixtures_are_labelled_shape_only()
    {
        foreach (var (file, node) in Fixtures("valid"))
            if (node is JsonObject o && o["aiSource"]?.GetValue<string>() == "model")
                Assert.Contains("shape-only", file);
    }

    [Fact]
    public void Doc_states_the_normative_security_requirements()
    {
        var doc = DocText();
        if (doc.Length == 0) return;
        string[] required =
        {
            "TextPolicy", "UTF-16 code unit", "never in SQL", "calendar.v1", "Idempotency-Replayed", "(user, method, route template, key)",
            "180 days", "injectionSuspect", "batchApproveAllowed", "noticeVersion", "attestation", "separate budget pool",
            "Upload and extraction hardening", "no network", "within 1 hour", "no cross-user", "Lead decision, provisional where PO-owned",
            "XXE", "zip bomb", "Origin", "inclusive", "exclusive", "supersession", "FOR UPDATE", "UNLOGGED",
        };
        foreach (var r in required) Assert.Contains(r, doc, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("calendarEvents=", doc);
        Assert.DoesNotContain("24 h hard", doc);
    }

    [Fact]
    public void No_schema_names_google_or_allows_orphaned_events()
    {
        foreach (var n in SchemaNames())
        {
            var text = File.ReadAllText(Path.Combine(SchemaDir, n + ".schema.json"));
            Assert.DoesNotContain("google", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("orphan", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
