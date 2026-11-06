using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Tether.IntegrationTests.Infrastructure;

namespace Tether.IntegrationTests;

/// <summary>Validates REAL docs export responses against src/shared/contracts/docs.export.v1 (the same validator and draft as Tether.ContractTests).</summary>
public static class DocsSchemas
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
        }
        throw new DirectoryNotFoundException("src/shared/contracts not found; set TETHER_CONTRACTS_DIR.");
    }

    private static (EvaluationOptions, Dictionary<string, JsonSchema>) Load()
    {
        var options = new EvaluationOptions { OutputFormat = OutputFormat.List, EvaluateAs = SpecVersion.Draft202012 };
        var schemas = new Dictionary<string, JsonSchema>();
        foreach (var file in Directory.GetFiles(Path.Combine(FindDir(), "docs.export.v1"), "*.schema.json"))
        {
            var schema = JsonSchema.FromText(File.ReadAllText(file));
            schemas[Path.GetFileName(file)[..^".schema.json".Length]] = schema;
            options.SchemaRegistry.Register(schema);
        }
        return (options, schemas);
    }

    public static void AssertValid(string schemaName, JsonElement json)
    {
        var (options, schemas) = State.Value;
        Assert.True(schemas.TryGetValue(schemaName, out var schema), "No schema " + schemaName);
        var result = schema!.Evaluate(JsonNode.Parse(json.GetRawText()), options);
        Assert.True(result.IsValid, "Response does not match " + schemaName + ".schema.json: " + json.GetRawText());
    }
}

/// <summary>The default host with the Development-only fault flag ON (the real-stack e2e does the same) and the large race pool.</summary>
public sealed class DocsFactory : ApiFactory
{
    protected override int MaxPoolSize => RacePool.Size;
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Docs:Demo:Faults:Enabled", "true")];
}

/// <summary>Shared helpers for the Demo Doc export tests: a Stopped lecture with Ready notes (mock AI, synthetic transcript), and raw request building.</summary>
public abstract class DocsExportTestBase(ApiFactory factory) : NotesTestBase(factory)
{
    protected const string Api = "/api/v1";

    protected sealed record Lecture(Actor A, string SessionId, string NotesId)
    {
        public Guid SessionGuid => Guid.Parse(SessionId);
    }

    /// <summary>A Stopped lecture whose notes press finished Ready.</summary>
    protected async Task<Lecture> ReadyAsync(Actor? actor = null, int finals = 3)
    {
        var (a, id) = await StoppedSessionAsync(actor, finals);
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(a.Client, id)).StatusCode);
        var notes = await WaitSettledAsync(a.Client, id);
        Assert.Equal("Ready", notes.GetProperty("status").GetString());
        return new Lecture(a, id, notes.GetProperty("notesId").GetString()!);
    }

    /// <summary>A Stopped lecture whose notes row is written straight into the table as Ready (the notes press is limited per user, so a test that needs many lectures of one user cannot press for each).</summary>
    protected async Task<Lecture> ReadyBySqlAsync(Actor? actor = null)
    {
        var (a, id) = await StoppedSessionAsync(actor, finals: 0);
        var press = Guid.NewGuid();
        var sections = Sections(("Mitosis", [("Cells divide in four phases.", [2000L]), ("Prophase comes first.", [4000L, 6000L])]));
        await F.ExecAsync("insert into ai.lecture_notes (id, session_id, user_id, revision, state, idempotency_key, requested_at, started_at, completed_at, sections, truncated, notice_version, covered_from_ms, covered_to_ms, model, final_summary_state) " +
                          $"values ('{press}', '{id}', '{a.UserId}', 1, 'Ready', 'k{press:N}', now(), now(), now(), '{sections}'::jsonb, false, 'notes-notice-draft-1', 0, 9000, 'mock-fast', 'NotNeeded')");
        return new Lecture(a, id, "nt_" + press.ToString("N"));
    }

    protected static HttpRequestMessage Req(HttpMethod m, string path, object? body = null, string? key = null, string? fault = null)
    {
        var r = new HttpRequestMessage(m, Api + path);
        if (body is not null) r.Content = body is string s ? new StringContent(s, Encoding.UTF8, "application/json") : System.Net.Http.Json.JsonContent.Create(body);
        if (key is not null) r.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        if (fault is not null) r.Headers.TryAddWithoutValidation("X-Demo-Docs-Fault", fault);
        return r;
    }

    protected static Task<HttpResponseMessage> PreviewAsync(HttpClient c, string sessionId, string notesId) =>
        c.SendAsync(Req(HttpMethod.Post, $"/sessions/{sessionId}/docs-export/preview", new { notesId }));

    protected static async Task<JsonElement> PreviewOkAsync(HttpClient c, string sessionId, string notesId)
    {
        var res = await PreviewAsync(c, sessionId, notesId);
        Assert.True(res.StatusCode == HttpStatusCode.OK, $"{(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        var b = await res.BodyAsync();
        DocsSchemas.AssertValid("docs-preview", b);
        return b;
    }

    /// <summary>The confirm body a client would build from a preview it showed: ids and digest only, never text.</summary>
    protected static Dictionary<string, object?> ConfirmBody(JsonElement preview, string mode, bool ack = true)
    {
        var existing = preview.GetProperty("existing");
        var has = existing.ValueKind == JsonValueKind.Object;
        return new()
        {
            ["notesId"] = preview.GetProperty("notesId").GetString(),
            ["contentDigest"] = preview.GetProperty("contentDigest").GetString(),
            ["mode"] = mode,
            ["expectedExistingVersion"] = mode == "create" || !has ? null : existing.GetProperty("docRef").GetProperty("version").GetInt32(),
            ["expectedExistingDocId"] = mode == "create" || !has ? null : existing.GetProperty("docRef").GetProperty("docId").GetString(),
            ["confirm"] = new Dictionary<string, object?> { ["provider"] = "demo_docs", ["acknowledgedDemo"] = ack },
        };
    }

    protected static Task<HttpResponseMessage> ConfirmAsync(HttpClient c, string sessionId, object body, string? key = null, string? fault = null) =>
        c.SendAsync(Req(HttpMethod.Post, $"/sessions/{sessionId}/docs-export", body, key ?? NewKey(), fault));

    protected static async Task<JsonElement> OkRecordAsync(HttpResponseMessage res)
    {
        Assert.True(res.StatusCode == HttpStatusCode.OK, $"{(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        var b = await res.BodyAsync();
        DocsSchemas.AssertValid("docs-export-record", b);
        return b;
    }

    /// <summary>Preview, then confirm the first export (create). Returns the record.</summary>
    protected async Task<JsonElement> ExportOnceAsync(Lecture l, string? key = null)
    {
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        return await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "create"), key));
    }

    protected static string[] ActionsOf(JsonElement preview) => preview.GetProperty("actions").EnumerateArray().Select(a => a.GetString()!).ToArray();

    protected static string DocIdOf(JsonElement record) => record.GetProperty("docRef").GetProperty("docId").GetString()!;

    protected static Guid Guid32(string wire) => Guid.Parse(wire[(wire.IndexOf('_') + 1)..]);

    protected Task<long> DocRowsAsync(string table, Guid user, string where = "true") =>
        F.ScalarAsync($"select count(*) from docs.{table} where user_id = '{user}' and {where}");

    protected static bool Replayed(HttpResponseMessage r) => r.Headers.TryGetValues("Idempotency-Replayed", out var v) && v.Single() == "true";

    /// <summary>Rewrites the stored notes of a lecture (sections JSON, truncation) so a test controls the document the builder sees.</summary>
    protected Task SetNotesAsync(string sessionId, string sectionsJson, bool truncated = false, long coveredToMs = 9000) =>
        F.ExecAsync($"update ai.lecture_notes set sections = '{sectionsJson.Replace("'", "''")}'::jsonb, truncated = {(truncated ? "true" : "false")}, covered_to_ms = {coveredToMs} where session_id = '{sessionId}'");

    protected static string Sections(params (string Heading, (string Text, long[] At)[] Bullets)[] sections) =>
        JsonSerializer.Serialize(sections.Select(s => new
        {
            heading = s.Heading,
            bullets = s.Bullets.Select(b => new { text = b.Text, citations = b.At.Select((ms, i) => new { segmentId = "seg_" + (i + 1), startMs = ms }) }),
        }));
}
