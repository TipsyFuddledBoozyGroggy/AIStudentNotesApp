using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;

namespace Tether.ContractTests;

/// <summary>
/// DX-B10 (B44): schema and fixture checks for the DRAFT docs.export.v1 contract (docs/API_CONTRACTS_DOCS_EXPORT.md). Like CalendarContractTests it keeps its
/// own fixtures under docs.export.v1/fixtures/{valid,invalid}, so the frozen top-level fixture set (and ContractFixtureTests) is untouched. Fixture naming:
/// "{schema-basename}--{case}.json". Everything here is mock only; no test calls Google. The semantic rules JSON Schema cannot express (digest
/// recomputation, mode against the existing document, block cap, text policy, canonical ids) are tested in Tether.DocsTests and the integration tests.
/// </summary>
public sealed class DocsExportContractTests
{
    private static readonly string ContractsDir = FindContractsDir();
    private static readonly string SchemaDir = Path.Combine(ContractsDir, "docs.export.v1");
    private static readonly string FixtureDir = Path.Combine(SchemaDir, "fixtures");
    private static readonly Lazy<(EvaluationOptions Options, Dictionary<string, JsonSchema> Schemas)> Registry = new(BuildRegistry);

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

    private static string DocText()
    {
        for (var d = new DirectoryInfo(ContractsDir); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "docs", "API_CONTRACTS_DOCS_EXPORT.md");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        Assert.Fail("docs/API_CONTRACTS_DOCS_EXPORT.md was not found; mount the repository root (this test must not pass vacuously).");
        return "";
    }

    private static (EvaluationOptions Options, Dictionary<string, JsonSchema> Schemas) BuildRegistry()
    {
        Assert.True(Directory.Exists(SchemaDir), "src/shared/contracts/docs.export.v1 does not exist");
        var options = new EvaluationOptions { OutputFormat = OutputFormat.List, EvaluateAs = SpecVersion.Draft202012 };
        var schemas = new Dictionary<string, JsonSchema>();
        foreach (var file in Directory.GetFiles(SchemaDir, "*.schema.json"))
        {
            var schema = JsonSchema.FromText(File.ReadAllText(file));
            schemas[Path.GetFileName(file)[..^".schema.json".Length]] = schema;
            options.SchemaRegistry.Register(schema);   // every file in the folder is registered before use: cross-file $refs are relative
        }
        return (options, schemas);
    }

    private static IEnumerable<string> SchemaNames() =>
        Directory.GetFiles(SchemaDir, "*.schema.json").Select(f => Path.GetFileName(f)[..^".schema.json".Length]).Order();

    private static JsonNode Schema(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(SchemaDir, name + ".schema.json")))!;
    private static JsonNode Defs() => Schema("docs-common")["$defs"]!;

    public static IEnumerable<object[]> Fixtures(string kind) =>
        Directory.GetFiles(Path.Combine(FixtureDir, kind), "*.json").Order().Select(f => new object[] { Path.GetFileName(f) });

    public static IEnumerable<object[]> ValidFixtures() => Fixtures("valid");
    public static IEnumerable<object[]> InvalidFixtures() => Fixtures("invalid");

    private static string SchemaOf(string file)
    {
        var i = file.IndexOf("--", StringComparison.Ordinal);
        Assert.True(i > 0, $"Fixture {file} must be named '{{schema}}--{{case}}.json'");
        return file[..i];
    }

    private static bool Validate(string fixtureFile, string kind)
    {
        var (options, schemas) = Registry.Value;
        var name = SchemaOf(fixtureFile);
        Assert.True(schemas.TryGetValue(name, out var schema), $"No schema '{name}' for fixture {fixtureFile}");
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureDir, kind, fixtureFile)));
        return schema!.Evaluate(node, options).IsValid;
    }

    [Theory, MemberData(nameof(ValidFixtures))]
    public void Valid_fixture_passes(string file) => Assert.True(Validate(file, "valid"), $"{file} should be valid");

    [Theory, MemberData(nameof(InvalidFixtures))]
    public void Invalid_fixture_fails(string file) => Assert.False(Validate(file, "invalid"), $"{file} should be rejected");

    // ---- structure ----

    [Fact]
    public void B44_The_folder_has_the_8_schemas_20_valid_and_42_invalid_fixtures_with_the_documented_ids()
    {
        var names = SchemaNames().ToList();
        Assert.Equal(8, names.Count);
        Assert.All(names, n => Assert.StartsWith("docs-", n));
        foreach (var n in names)
            Assert.Equal($"https://schemas.tether.invalid/docs.export.v1/{n}.schema.json", Schema(n)["$id"]!.GetValue<string>());
        Assert.Equal(21, Directory.GetFiles(Path.Combine(FixtureDir, "valid"), "*.json").Length);
        Assert.Equal(49, Directory.GetFiles(Path.Combine(FixtureDir, "invalid"), "*.json").Length);
    }

    [Fact]
    public void B44_Every_schema_except_the_shared_definitions_has_a_valid_and_an_invalid_fixture_and_each_invalid_one_is_named_for_a_schema()
    {
        foreach (var n in SchemaNames().Where(n => n != "docs-common"))
        {
            Assert.Contains(Directory.GetFiles(Path.Combine(FixtureDir, "valid"), n + "--*.json"), _ => true);
            Assert.Contains(Directory.GetFiles(Path.Combine(FixtureDir, "invalid"), n + "--*.json"), _ => true);
        }
        var known = SchemaNames().ToHashSet();
        foreach (var kind in new[] { "valid", "invalid" })
            foreach (var f in Directory.GetFiles(Path.Combine(FixtureDir, kind), "*.json"))
                Assert.Contains(SchemaOf(Path.GetFileName(f)), known);
    }

    [Fact]
    public void B44_id_timestamp_and_idempotencyKey_are_the_same_definitions_as_syllabus_common()
    {
        var syllabus = JsonNode.Parse(File.ReadAllText(Path.Combine(ContractsDir, "syllabus.v1", "syllabus-common.schema.json")))!["$defs"]!;
        var docs = Defs();
        foreach (var def in new[] { "id", "timestamp", "idempotencyKey" })
            foreach (var key in new[] { "type", "pattern", "minLength", "maxLength", "format" })
                Assert.Equal(syllabus[def]![key]?.ToJsonString(), docs[def]![key]?.ToJsonString());
    }

    [Fact]
    public void B44_No_schema_has_a_property_that_could_carry_a_token_a_code_a_secret_or_a_segment_id()
    {
        var banned = new[] { "token", "access", "refresh", "code", "secret", "segmentId", "segment_id", "accessToken", "refreshToken", "password", "cookie" };
        void Walk(JsonNode? node, string schema, List<string> offenders)
        {
            if (node is JsonObject o)
            {
                if (o["properties"] is JsonObject props)
                    foreach (var p in props)
                        // `code` is allowed only as an ERROR code (problem body, record error): a $ref to the errorCode or recordErrorCode enum, never an OAuth authorization code
                        if (banned.Contains(p.Key, StringComparer.OrdinalIgnoreCase)
                            && !(p.Key == "code" && p.Value?["$ref"]?.GetValue<string>() is { } r && (r.EndsWith("#/$defs/errorCode", StringComparison.Ordinal) || r.EndsWith("#/$defs/recordErrorCode", StringComparison.Ordinal))))
                            offenders.Add(schema + ":" + p.Key);
                foreach (var kv in o) Walk(kv.Value, schema, offenders);
            }
            else if (node is JsonArray a) foreach (var i in a) Walk(i, schema, offenders);
        }
        var found = new List<string>();
        foreach (var n in SchemaNames()) Walk(Schema(n), n, found);
        Assert.True(found.Count == 0, "token-like property: " + string.Join(", ", found));
    }

    [Fact]
    public void B44_The_error_codes_in_section_5_of_the_document_equal_the_schema_enum()
    {
        var doc = DocText();
        var start = doc.IndexOf("## 5. Error codes", StringComparison.Ordinal);
        var end = doc.IndexOf("## 6.", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "section 5 not found");
        var section = doc[start..end];
        var intro = section[..section.IndexOf("Additions", StringComparison.Ordinal)];
        var fromProse = Regex.Matches(intro, "`([a-z_]+)` (\\d{3})").Select(m => m.Groups[1].Value);
        var fromTable = Regex.Matches(section, "^\\| `([a-z_]+)` \\| (\\d{3}) \\|", RegexOptions.Multiline).Select(m => m.Groups[1].Value);
        var documented = fromProse.Concat(fromTable).ToHashSet();
        var schema = Defs()["errorCode"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToHashSet();
        Assert.Equal(documented.Order().ToList(), schema.Order().ToList());
        Assert.Contains("retry_limit_reached", schema);
        Assert.Contains("unsupported_media_type", schema);
        Assert.Contains("export_in_progress", schema);   // reserved for a real provider, never emitted by the demo
    }

    [Fact]
    public void B44_The_provider_is_the_constant_demo_docs_so_a_real_provider_is_not_expressible()
    {
        Assert.Equal(["demo_docs"], Defs()["provider"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
        var reserved = Defs()["recordErrorCode"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToHashSet();
        Assert.All(new[] { "provider_unavailable", "rate_limited", "provider_rejected", "reconnect_required", "doc_changed_outside", "quota_exceeded" }, c => Assert.Contains(c, reserved));
    }
}
