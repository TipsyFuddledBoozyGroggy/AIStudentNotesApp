using System.Text.Json;
using Tether.Ai.Provider;

namespace Tether.AiTests;

internal static class T
{
    public static SourceLine Line(int n, long startMs, string text) => new($"seg_{n}", startMs, text);

    public static IReadOnlyDictionary<string, SourceLine> Allowed(params SourceLine[] lines) => lines.ToDictionary(l => l.Id);

    public static AiOptions Options(params KeyValuePair<string, string?>[] extra) =>
        AiOptions.From(new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(extra).Build());

    public static string Json(object o) => JsonSerializer.Serialize(o);

    public static AiRequest Request(AiWorkflow w, string instruction, params UntrustedBlock[] blocks) =>
        new(w, "test.v1", AiTier.Fast, "SYSTEM", instruction, blocks, 700, Guid.NewGuid(), Guid.NewGuid(), "k", TimeSpan.FromSeconds(5));

    public static string SummaryJson(int baseRevision, string tldr, params (string topic, string text, string[] ids)[] topics) => Json(new
    {
        baseRevision, tldr,
        topicUpdates = topics.Select(t => new { topic = t.topic, text = t.text, segmentIds = t.ids }).ToArray(),
        newTerms = Array.Empty<string>(),
    });
}
