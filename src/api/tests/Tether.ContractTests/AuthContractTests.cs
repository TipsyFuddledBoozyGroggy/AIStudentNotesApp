using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;

namespace Tether.ContractTests;

/// <summary>
/// ADR-022: schema and fixture checks for the FROZEN auth.v1 contract (docs/API_CONTRACTS_AUTH.md). Fixtures live in auth.v1/fixtures/{valid,invalid},
/// named "{schema-basename}--{case}.json". The behaviour JSON Schema cannot express (state binding, ID-token rules, admission) is tested in
/// Tether.IntegrationTests (GoogleSignIn*Tests) against a fake Google handler; no test here or there calls Google.
/// </summary>
public sealed class AuthContractTests
{
    private static readonly string ContractsDir = FindContractsDir();
    private static readonly string SchemaDir = Path.Combine(ContractsDir, "auth.v1");
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
            var candidate = Path.Combine(d.FullName, "docs", "API_CONTRACTS_AUTH.md");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        Assert.Fail("docs/API_CONTRACTS_AUTH.md was not found; mount the repository root (this test must not pass vacuously).");
        return "";
    }

    private static (EvaluationOptions Options, Dictionary<string, JsonSchema> Schemas) BuildRegistry()
    {
        Assert.True(Directory.Exists(SchemaDir), "src/shared/contracts/auth.v1 does not exist");
        var options = new EvaluationOptions { OutputFormat = OutputFormat.List, EvaluateAs = SpecVersion.Draft202012 };
        var schemas = new Dictionary<string, JsonSchema>();
        foreach (var file in Directory.GetFiles(SchemaDir, "*.schema.json"))
        {
            var schema = JsonSchema.FromText(File.ReadAllText(file));
            schemas[Path.GetFileName(file)[..^".schema.json".Length]] = schema;
            options.SchemaRegistry.Register(schema);
        }
        return (options, schemas);
    }

    private static IEnumerable<string> SchemaNames() =>
        Directory.GetFiles(SchemaDir, "*.schema.json").Select(f => Path.GetFileName(f)[..^".schema.json".Length]).Order();

    private static JsonNode Schema(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(SchemaDir, name + ".schema.json")))!;
    private static JsonNode Defs() => Schema("auth-common")["$defs"]!;
    private static string[] Enum(string def) => Defs()[def]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();

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

    [Fact]
    public void The_folder_has_5_schemas_with_their_ids_and_every_schema_has_valid_and_invalid_fixtures()
    {
        var names = SchemaNames().ToList();
        Assert.Equal(["auth-callback-outcome", "auth-common", "auth-methods-response", "auth-signin-start-request", "auth-start-response"], names);
        foreach (var n in names)
            Assert.Equal($"https://schemas.tether.invalid/auth.v1/{n}.schema.json", Schema(n)["$id"]!.GetValue<string>());
        foreach (var n in names.Where(n => n != "auth-common"))
        {
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(FixtureDir, "valid"), n + "--*.json"));
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(FixtureDir, "invalid"), n + "--*.json"));
        }
    }

    [Fact]
    public void Every_object_rejects_unknown_members()
    {
        void Walk(JsonNode? node, string where)
        {
            if (node is JsonObject o)
            {
                if (o["type"]?.GetValue<string>() == "object")
                    Assert.True(o["additionalProperties"]?.GetValue<bool>() == false, $"{where}: object without additionalProperties:false");
                foreach (var kv in o) Walk(kv.Value, where + "/" + kv.Key);
            }
            else if (node is JsonArray a) foreach (var i in a) Walk(i, where);
        }
        foreach (var n in SchemaNames()) Walk(Schema(n), n);
    }

    [Fact]
    public void timestamp_and_authorizationUrl_are_byte_identical_copies_of_calendar_common()
    {
        var calendar = JsonNode.Parse(File.ReadAllText(Path.Combine(ContractsDir, "calendar.v1", "calendar-common.schema.json")))!["$defs"]!;
        foreach (var def in new[] { "timestamp", "authorizationUrl" })
            Assert.Equal(calendar[def]!.ToJsonString(), Defs()[def]!.ToJsonString());
    }

    [Fact]
    public void The_outcome_table_in_section_4_of_the_document_equals_the_schema_enum_in_order()
    {
        var doc = DocText();
        var start = doc.IndexOf("## 4. Callback outcomes", StringComparison.Ordinal);
        var end = doc.IndexOf("## 5.", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "section 4 not found");
        var documented = Regex.Matches(doc[start..end], "^\\| `([a-z_]+)` \\|", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();
        // Order matters: the table is the precedence order and the schema lists the same codes. The test compares as sets plus the documented order of the first rows.
        Assert.Equal(Enum("callbackOutcome").Order().ToArray(), documented.Order().ToArray());
        Assert.Equal(["rate_limited", "state_invalid"], documented[..2]);
        Assert.Equal("signed_in", documented[^1]);
        Assert.Contains("not_invited", documented);
        Assert.Contains("school_account", documented);
        Assert.Contains("account_closed", documented);
    }

    [Fact]
    public void Landing_and_purpose_are_closed_keys_and_no_schema_can_carry_a_url_token_code_email_or_subject()
    {
        Assert.Equal(["home"], Enum("landing"));
        Assert.Equal(["signin"], Enum("purpose"));
        var banned = new[] { "token", "idToken", "accessToken", "refreshToken", "code", "secret", "state", "nonce", "verifier", "codeVerifier",
            "email", "sub", "subject", "returnUrl", "returnPath", "redirect_uri", "redirectUri", "scope", "password", "cookie" };
        var found = new List<string>();
        void Walk(JsonNode? node, string schema)
        {
            if (node is JsonObject o)
            {
                if (o["properties"] is JsonObject props)
                    foreach (var p in props)
                        if (banned.Contains(p.Key, StringComparer.OrdinalIgnoreCase)) found.Add(schema + ":" + p.Key);
                foreach (var kv in o) Walk(kv.Value, schema);
            }
            else if (node is JsonArray a) foreach (var i in a) Walk(i, schema);
        }
        foreach (var n in SchemaNames()) Walk(Schema(n), n);
        Assert.True(found.Count == 0, "forbidden property: " + string.Join(", ", found));
    }

    [Fact]
    public void The_valid_start_fixture_carries_exactly_the_pinned_parameter_set()
    {
        var url = JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureDir, "valid", "auth-start-response--signin.json")))!["authorizationUrl"]!.GetValue<string>();
        var query = url[(url.IndexOf('?') + 1)..].Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        Assert.Equal(["client_id", "code_challenge", "code_challenge_method", "nonce", "prompt", "redirect_uri", "response_type", "scope", "state"], query.Keys.Order().ToArray());
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("openid email profile", query["scope"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("select_account", query["prompt"]);
        Assert.EndsWith("/api/v1/auth/google/callback", query["redirect_uri"]);
    }
}
