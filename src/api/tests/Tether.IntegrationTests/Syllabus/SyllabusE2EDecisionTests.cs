using System.Net;
using System.Text;
using System.Text.Json;
using Tether.IntegrationTests.Infrastructure;

namespace Tether.IntegrationTests.Syllabus;

/// <summary>
/// Pins the Lead decisions that real-stack E2E exposed (API_CONTRACTS_P2 section 13.1 rows 61 to 63): term dates are paired,
/// item PATCH takes application/json only, non-upload 415s use neutral wording, and If-Match accepts only a single etag.
/// </summary>
public class SyllabusE2EDecisionTests(ApiFactory factory) : SyllabusTestBase(factory), IClassFixture<ApiFactory>
{
    private const string Neutral = "The content type of the request is not supported.";

    private async Task<(Actor A, string Syl, JsonElement Item)> ReadyAsync()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var syl = await CreateReadyAsync(a.Client, course);
        var sid = syl.GetProperty("id").GetString()!;
        return (a, sid, (await ItemsAsync(a.Client, sid)).First());
    }

    private static string ItemPath(string syl, JsonElement item) => $"{Api}/syllabi/{syl}/items/{item.GetProperty("id").GetString()}";

    private static HttpRequestMessage Patch(string path, string contentType, string? ifMatch, string body = "{\"title\":\"Renamed\"}")
    {
        var r = new HttpRequestMessage(HttpMethod.Patch, path) { Content = new StringContent(body, Encoding.UTF8) };
        r.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        if (ifMatch is not null) r.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return r;
    }

    // ---- (1) term dates are sent together or not at all

    [Fact]
    public async Task Term_dates_must_be_sent_together_or_not_at_all()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var onlyStart = Paste(); onlyStart.Remove("termEnd");
        var onlyEnd = Paste(); onlyEnd.Remove("termStart");
        foreach (var body in new[] { onlyStart, onlyEnd, Paste(termEnd: null), Paste(termStart: null) })
        {
            var res = await CreateAsync(a.Client, course, body);
            await res.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
            Assert.Equal("required", (await res.BodyAsync()).GetProperty("errors").GetProperty("termStart")[0].GetString());
        }
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.syllabi where user_id = '{a.UserId}'"));

        var none = Paste(); none.Remove("termStart"); none.Remove("termEnd");
        await CreateReadyAsync(a.Client, course, none);                                              // waits for the extraction job (one at a time per user)
        var pair = Paste(text: Sample + "\nQuiz 2 due Nov 9, 2026");
        Assert.Equal(HttpStatusCode.Accepted, (await CreateAsync(a.Client, course, pair)).StatusCode);
    }

    // ---- (2) PATCH media type

    [Fact]
    public async Task Item_patch_accepts_application_json_and_rejects_merge_patch_json_with_415()
    {
        var (a, syl, item) = await ReadyAsync();
        var path = ItemPath(syl, item);
        var tag = item.GetProperty("etag").GetString()!;

        foreach (var ct in new[] { "application/merge-patch+json", "application/json-patch+json", "text/plain" })
        {
            var res = await a.Client.SendAsync(Patch(path, ct, tag));
            await res.AssertProblemAsync(HttpStatusCode.UnsupportedMediaType, "unsupported_type");
            Assert.Equal(Neutral, (await res.BodyAsync()).GetProperty("detail").GetString());
        }
        // nothing was applied: the same etag still works
        var ok = await a.Client.SendAsync(Patch(path, "application/json; charset=utf-8", tag));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Renamed", (await ok.BodyAsync()).GetProperty("title").GetString());
    }

    // ---- (3) non-upload 415 wording

    [Fact]
    public async Task Json_route_415_uses_the_neutral_problem_and_the_same_wire_code()
    {
        var (a, syl, item) = await ReadyAsync();
        var wrong = new HttpRequestMessage(HttpMethod.Post, $"{Api}/syllabi/{syl}/items") { Content = new StringContent("x", Encoding.UTF8, "text/plain") };
        var res = await a.Client.SendAsync(wrong);
        await res.AssertProblemAsync(HttpStatusCode.UnsupportedMediaType, "unsupported_type");
        var body = await res.BodyAsync();
        Assert.Equal("Unsupported media type.", body.GetProperty("title").GetString());
        Assert.Equal(Neutral, body.GetProperty("detail").GetString());
        Assert.DoesNotContain("PDF", body.GetRawText());
        SyllabusSchemas.AssertValid("syllabus-problem", body);

        // decision and batch take the same path
        var d = new HttpRequestMessage(HttpMethod.Post, ItemPath(syl, item) + "/decision") { Content = new StringContent("{}", Encoding.UTF8, "text/plain") };
        d.Headers.TryAddWithoutValidation("If-Match", item.GetProperty("etag").GetString());
        var dr = await a.Client.SendAsync(d);
        await dr.AssertProblemAsync(HttpStatusCode.UnsupportedMediaType, "unsupported_type");
        Assert.Equal(Neutral, (await dr.BodyAsync()).GetProperty("detail").GetString());
        var b = new HttpRequestMessage(HttpMethod.Post, $"{Api}/syllabi/{syl}/decisions") { Content = new StringContent("{}", Encoding.UTF8, "text/plain") };
        var br = await a.Client.SendAsync(b);
        await br.AssertProblemAsync(HttpStatusCode.UnsupportedMediaType, "unsupported_type");
        Assert.Equal(Neutral, (await br.BodyAsync()).GetProperty("detail").GetString());
    }

    // ---- (4) If-Match forms

    [Fact]
    public async Task If_match_star_is_412_lists_are_400_and_only_a_single_etag_is_accepted()
    {
        var (a, syl, item) = await ReadyAsync();
        var path = ItemPath(syl, item);
        var tag = item.GetProperty("etag").GetString()!;
        Assert.StartsWith("W/\"", tag);                                                           // weak validators

        foreach (var im in new[] { "*", "W/*", "\"zzz\"", "zzz" })
            await (await a.Client.SendAsync(Patch(path, "application/json", im))).AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");
        // a comma-separated list is split by the host into several values, so it is the same 400 as two header fields (even if it contains the right tag)
        foreach (var im in new[] { tag + ", W/\"other\"", "\"zzz\", " + tag })
            await (await a.Client.SendAsync(Patch(path, "application/json", im))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        // blank is 428, two header fields are 400 too
        await (await a.Client.SendAsync(Patch(path, "application/json", " "))).AssertProblemAsync((HttpStatusCode)428, "precondition_required");
        var two = Patch(path, "application/json", tag); two.Headers.TryAddWithoutValidation("If-Match", tag);
        await (await a.Client.SendAsync(two)).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        // DELETE and decision behave the same way for *
        var del = new HttpRequestMessage(HttpMethod.Delete, path); del.Headers.TryAddWithoutValidation("If-Match", "*");
        await (await a.Client.SendAsync(del)).AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");
        var dec = Req(HttpMethod.Post, path + "/decision", new { decision = "skipped" }, ifMatch: "*");
        await (await a.Client.SendAsync(dec)).AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");

        // nothing changed, and the exact etag works: the weak form, then the strong form of the same opaque value
        var ok = await a.Client.SendAsync(Patch(path, "application/json", tag));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var next = (await ok.BodyAsync()).GetProperty("etag").GetString()!;
        var strong = next[2..];
        var ok2 = await a.Client.SendAsync(Patch(path, "application/json", strong, "{\"title\":\"Renamed again\"}"));
        Assert.Equal(HttpStatusCode.OK, ok2.StatusCode);
    }
}
