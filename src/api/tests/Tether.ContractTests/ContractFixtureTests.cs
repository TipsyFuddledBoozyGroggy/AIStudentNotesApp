using System.Text.Json.Nodes;
using Json.Schema;

namespace Tether.ContractTests;

/// <summary>
/// Validates src/shared/contracts fixtures against the lecture.v1 JSON Schemas (draft 2020-12).
/// Fixture naming: "{schema-basename}--{case}.json" where the schema is lecture.v1/{schema-basename}.schema.json.
/// </summary>
public sealed class ContractFixtureTests
{
    private static readonly string ContractsDir = FindContractsDir();
    private static readonly Lazy<EvaluationOptions> Options = new(BuildOptions);
    private static readonly Dictionary<string, JsonSchema> Schemas = new();

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

    private static EvaluationOptions BuildOptions()
    {
        var options = new EvaluationOptions { OutputFormat = OutputFormat.List, EvaluateAs = SpecVersion.Draft202012 };
        foreach (var file in Directory.GetFiles(Path.Combine(ContractsDir, "lecture.v1"), "*.schema.json"))
        {
            var schema = JsonSchema.FromText(File.ReadAllText(file));
            var name = Path.GetFileName(file)[..^".schema.json".Length];
            Schemas[name] = schema;
            options.SchemaRegistry.Register(schema);
        }
        return options;
    }

    private static bool Validate(string schemaName, string fixturePath)
    {
        _ = Options.Value;
        Assert.True(Schemas.TryGetValue(schemaName, out var schema), $"No schema '{schemaName}' for fixture {Path.GetFileName(fixturePath)}");
        var node = JsonNode.Parse(File.ReadAllText(fixturePath));
        return schema!.Evaluate(node, Options.Value).IsValid;
    }

    private static string SchemaOf(string fixturePath)
    {
        var name = Path.GetFileNameWithoutExtension(fixturePath);
        var i = name.IndexOf("--", StringComparison.Ordinal);
        Assert.True(i > 0, $"Fixture {name} must be named '{{schema}}--{{case}}'");
        return name[..i];
    }

    public static IEnumerable<object[]> Fixtures(string kind) =>
        Directory.GetFiles(Path.Combine(ContractsDir, "fixtures", kind), "*.json").Order().Select(f => new object[] { Path.GetFileName(f) });

    public static IEnumerable<object[]> ValidFixtures() => Fixtures("valid");
    public static IEnumerable<object[]> InvalidFixtures() => Fixtures("invalid");

    [Theory, MemberData(nameof(ValidFixtures))]
    public void Valid_fixture_passes(string file)
    {
        var path = Path.Combine(ContractsDir, "fixtures", "valid", file);
        Assert.True(Validate(SchemaOf(path), path), $"{file} should be valid");
    }

    [Theory, MemberData(nameof(InvalidFixtures))]
    public void Invalid_fixture_fails(string file)
    {
        var path = Path.Combine(ContractsDir, "fixtures", "invalid", file);
        Assert.False(Validate(SchemaOf(path), path), $"{file} should be rejected");
    }

    [Fact]
    public void Hub_events_validate_against_the_union_schema()
    {
        _ = Options.Value;
        foreach (var path in Directory.GetFiles(Path.Combine(ContractsDir, "fixtures", "valid"), "*.json"))
        {
            var isEvent = JsonNode.Parse(File.ReadAllText(path)) is JsonObject o && o.ContainsKey("payload") && o.ContainsKey("v");
            if (!isEvent) continue;
            Assert.True(Validate("hub-event", path), $"{Path.GetFileName(path)} should match hub-event");
        }
    }

    [Fact]
    public void Every_hub_event_has_at_least_one_valid_and_one_invalid_fixture()
    {
        string[] events = ["session.state", "transcript.partial", "transcript.final", "transcript.correction", "gap",
            "summary.revision", "summary.status", "catchup.ready", "stt.status", "error"];
        var valid = Directory.GetFiles(Path.Combine(ContractsDir, "fixtures", "valid")).Select(SchemaOf).ToHashSet();
        var invalid = Directory.GetFiles(Path.Combine(ContractsDir, "fixtures", "invalid")).Select(SchemaOf).ToHashSet();
        foreach (var e in events)
        {
            Assert.Contains(e, valid);
            Assert.Contains(e, invalid);
        }
    }

    [Fact]
    public void Speaker_is_never_required_anywhere()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(ContractsDir, "lecture.v1"), "*.schema.json"))
        {
            var root = JsonNode.Parse(File.ReadAllText(file))!;
            AssertSpeakerNotRequired(root, Path.GetFileName(file));
        }
    }

    private static void AssertSpeakerNotRequired(JsonNode? node, string where)
    {
        switch (node)
        {
            case JsonObject o:
                if (o["required"] is JsonArray req)
                    Assert.DoesNotContain(req, r => r?.GetValue<string>() == "speaker");
                foreach (var kv in o) AssertSpeakerNotRequired(kv.Value, where);
                break;
            case JsonArray a:
                foreach (var item in a) AssertSpeakerNotRequired(item, where);
                break;
        }
    }
}
