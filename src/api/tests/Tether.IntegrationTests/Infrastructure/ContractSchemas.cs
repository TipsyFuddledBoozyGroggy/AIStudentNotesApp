using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Tether.IntegrationTests.Infrastructure;

/// <summary>
/// Validates real API responses against src/shared/contracts/lecture.v1 (same draft 2020-12 approach and validator as
/// Tether.ContractTests). Located via TETHER_CONTRACTS_DIR, else by walking up to src/shared/contracts.
/// </summary>
public static class ContractSchemas
{
    private static readonly Lazy<(EvaluationOptions Options, Dictionary<string, JsonSchema> Schemas)> State = new(Load);

    private static string FindDir()
    {
        var env = Environment.GetEnvironmentVariable("TETHER_CONTRACTS_DIR");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "src", "shared", "contracts");
            if (Directory.Exists(c)) return c;
            c = Path.Combine(d.FullName, "shared", "contracts");
            if (Directory.Exists(c)) return c;
        }
        throw new DirectoryNotFoundException("src/shared/contracts not found; set TETHER_CONTRACTS_DIR.");
    }

    private static (EvaluationOptions, Dictionary<string, JsonSchema>) Load()
    {
        var options = new EvaluationOptions { OutputFormat = OutputFormat.List, EvaluateAs = SpecVersion.Draft202012 };
        var schemas = new Dictionary<string, JsonSchema>();
        foreach (var file in Directory.GetFiles(Path.Combine(FindDir(), "lecture.v1"), "*.schema.json"))
        {
            var schema = JsonSchema.FromText(File.ReadAllText(file));
            schemas[Path.GetFileName(file)[..^".schema.json".Length]] = schema;
            options.SchemaRegistry.Register(schema);
        }
        return (options, schemas);
    }

    /// <summary>Asserts <paramref name="json"/> validates against lecture.v1/{schemaName}.schema.json.</summary>
    public static void AssertValid(string schemaName, JsonElement json)
    {
        var (options, schemas) = State.Value;
        Assert.True(schemas.TryGetValue(schemaName, out var schema), $"No schema '{schemaName}'");
        var result = schema!.Evaluate(JsonNode.Parse(json.GetRawText()), options);
        Assert.True(result.IsValid, $"Response does not match {schemaName}.schema.json: {json.GetRawText()}");
    }

    /// <summary>Negative control: proves the validator is live by rejecting a body that must not validate.</summary>
    public static bool IsValid(string schemaName, string rawJson)
    {
        var (options, schemas) = State.Value;
        return schemas[schemaName].Evaluate(JsonNode.Parse(rawJson), options).IsValid;
    }
}
