using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Tether.IntegrationTests.Infrastructure;

namespace Tether.IntegrationTests.Syllabus;

/// <summary>Shared helpers for the Syllabus (P2-B1) integration tests. Every test uses its own users; ids are never shared between tests.</summary>
public abstract class SyllabusTestBase(ApiFactory factory)
{
    protected ApiFactory F { get; } = factory;
    protected const string Api = "/api/v1";

    protected sealed record Actor(HttpClient Client, Guid UserId, string Email);

    protected async Task<Actor> NewActorAsync()
    {
        var email = $"u{Guid.NewGuid():N}@dev.local";
        var c = await F.LoginAsync(email);
        var me = await (await c.GetAsync(Api + "/me")).BodyAsync();
        return new Actor(c, Guid.Parse(me.GetProperty("id").GetString()!), email);
    }

    protected static async Task<string> CourseAsync(HttpClient c, string name = "Biology 101")
    {
        var res = await c.PostAsJsonAsync(Api + "/courses", new { name });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.BodyAsync()).GetProperty("id").GetString()!;
    }

    /// <summary>A synthetic syllabus (fictional course). Verified weekdays in 2026: Sep 14 and Oct 19 are Mondays, Aug 25 a Tuesday.</summary>
    public const string Sample = """
        BIO 101 Fall 2026 Syllabus
        PS1 due Mon Sept 14, 11:59 PM
        Midterm exam: Oct 19, 2026 at 10:00 AM
        Final exam: TBA
        Office hours Tuesdays 2:00-3:00 PM
        Late work policy: 10% per day.
        Read chapter 3 by Sept 21
        Project proposal due 3/4
        Guest lecture on Nov 3
        """;

    protected static string NewKey() => "k" + Guid.NewGuid().ToString("N");

    protected static Dictionary<string, object?> Paste(string text = Sample, string? title = "BIO 101", string? termStart = "2026-08-24", string? termEnd = "2026-12-11",
        string tz = "America/New_York", bool allowDuplicate = false) => new()
    {
        ["title"] = title, ["text"] = text, ["termStart"] = termStart, ["termEnd"] = termEnd, ["defaultTimezone"] = tz, ["allowDuplicate"] = allowDuplicate,
        ["noticeVersion"] = "draft-2026-10", ["attestation"] = new { accepted = true, acceptedAt = DateTimeOffset.UtcNow.ToString("O") },
    };

    protected static HttpRequestMessage Req(HttpMethod m, string path, object? body = null, string? key = null, string? ifMatch = null)
    {
        var r = new HttpRequestMessage(m, path);
        if (body is not null) r.Content = body is string s ? new StringContent(s, Encoding.UTF8, "application/json") : JsonContent.Create(body);
        if (key is not null) r.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        if (ifMatch is not null) r.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return r;
    }

    protected static Task<HttpResponseMessage> CreateAsync(HttpClient c, string courseId, object body, string? key = null) =>
        c.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{courseId}/syllabi", body, key ?? NewKey()));

    /// <summary>Creates from pasted text and waits for the background job (the real hosted worker) to finish.</summary>
    protected async Task<JsonElement> CreateReadyAsync(HttpClient c, string courseId, object? body = null)
    {
        var res = await CreateAsync(c, courseId, body ?? Paste());
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var id = (await res.BodyAsync()).GetProperty("id").GetString()!;
        return await WaitDoneAsync(c, id);
    }

    protected static async Task<JsonElement> WaitDoneAsync(HttpClient c, string syllabusId, int seconds = 20)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            var b = await (await c.GetAsync($"{Api}/syllabi/{syllabusId}")).BodyAsync();
            if (b.GetProperty("pollAfterSeconds").ValueKind == JsonValueKind.Null) return b;
            Assert.True(DateTime.UtcNow < until, "extraction did not finish in time");
            await Task.Delay(50);
        }
    }

    protected static async Task<List<JsonElement>> ItemsAsync(HttpClient c, string syllabusId, string query = "")
    {
        var all = new List<JsonElement>();
        string? cursor = null;
        do
        {
            var q = (query.Length > 0 ? query + "&" : "") + "limit=200" + (cursor is null ? "" : "&cursor=" + cursor);
            var page = await (await c.GetAsync($"{Api}/syllabi/{syllabusId}/items?{q}")).BodyAsync();
            all.AddRange(page.GetProperty("items").EnumerateArray());
            cursor = page.GetProperty("nextCursor").ValueKind == JsonValueKind.Null ? null : page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        return all;
    }

    protected static JsonElement ByTitle(IEnumerable<JsonElement> items, string startsWith) =>
        items.First(i => i.GetProperty("title").GetString()!.StartsWith(startsWith, StringComparison.Ordinal));

    protected static Task<HttpResponseMessage> Decide(HttpClient c, string syl, JsonElement item, string decision, bool? ack = null) =>
        c.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{syl}/items/{item.GetProperty("id").GetString()}/decision",
            ack is null ? new { decision } : new { decision, acknowledgeCheck = ack }, ifMatch: item.GetProperty("etag").GetString()));

    protected static async Task<JsonElement> GetItemAsync(HttpClient c, string syl, string itemId)
    {
        var res = await c.GetAsync($"{Api}/syllabi/{syl}/items/{itemId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.BodyAsync();
    }

    protected static Dictionary<string, object> SyncBody(params (string Id, int Rev)[] items) => new()
    {
        ["itemRevisions"] = items.ToDictionary(i => i.Id, i => (object)i.Rev),
        ["confirm"] = new { provider = "mock", acknowledgedDemo = true },
    };

    protected static Task<HttpResponseMessage> Sync(HttpClient c, string syl, string? key, params (string Id, int Rev)[] items) =>
        c.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{syl}/calendar-sync", SyncBody(items), key));

    protected static string Guid32(string wire) => wire[(wire.IndexOf('_') + 1)..];
}
