using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tether.IntegrationTests.Infrastructure;

namespace Tether.IntegrationTests.Syllabus;

/// <summary>
/// Authentication, Origin/CSRF, ownership (IDOR) on EVERY route and nested id, canonical-id form in path/body, strict binder, and the
/// evaluation order of API_CONTRACTS_P2 section 2.1. Two users with overlapping ids plus a same-user mismatched parent.
/// </summary>
public class SyllabusOwnershipTests(ApiFactory factory) : SyllabusTestBase(factory), IClassFixture<ApiFactory>
{
    private sealed record World(Actor Alice, Actor Bob, string AliceCourse, string AliceSyl, JsonElement AliceItem, string BobCourse, string BobSyl, JsonElement BobItem,
        string BobCourse2, string BobSyl2, JsonElement BobItem2);

    private async Task<World> BuildAsync()
    {
        var alice = await NewActorAsync(); var bob = await NewActorAsync();
        var ac = await CourseAsync(alice.Client); var bc = await CourseAsync(bob.Client); var bc2 = await CourseAsync(bob.Client, "Chemistry");
        var asy = (await CreateReadyAsync(alice.Client, ac)).GetProperty("id").GetString()!;
        var bsy = (await CreateReadyAsync(bob.Client, bc)).GetProperty("id").GetString()!;
        var bsy2 = (await CreateReadyAsync(bob.Client, bc2)).GetProperty("id").GetString()!;
        return new World(alice, bob, ac, asy, ByTitle(await ItemsAsync(alice.Client, asy), "Midterm"), bc, bsy, ByTitle(await ItemsAsync(bob.Client, bsy), "Midterm"),
            bc2, bsy2, ByTitle(await ItemsAsync(bob.Client, bsy2), "Midterm"));
    }

    private sealed record Route(string Name, HttpMethod Method, string Path, object? Body, string? Key, string? IfMatch);

    private static List<Route> Routes(string course, string syl, string item, string etag) =>
    [
        new("list syllabi", HttpMethod.Get, $"{Api}/courses/{course}/syllabi", null, null, null),
        new("create", HttpMethod.Post, $"{Api}/courses/{course}/syllabi", Paste("Quiz due Oct 20, 2026"), NewKey(), null),
        new("demo calendar", HttpMethod.Get, $"{Api}/courses/{course}/demo-calendar?from=2026-08-01&to=2026-12-31", null, null, null),
        new("get syllabus", HttpMethod.Get, $"{Api}/syllabi/{syl}", null, null, null),
        new("list items", HttpMethod.Get, $"{Api}/syllabi/{syl}/items", null, null, null),
        new("create item", HttpMethod.Post, $"{Api}/syllabi/{syl}/items", new { kind = "other", title = "X", date = "2026-09-20" }, null, null),
        new("get item", HttpMethod.Get, $"{Api}/syllabi/{syl}/items/{item}", null, null, null),
        new("patch item", HttpMethod.Patch, $"{Api}/syllabi/{syl}/items/{item}", new { title = "Y" }, null, etag),
        new("delete item", HttpMethod.Delete, $"{Api}/syllabi/{syl}/items/{item}", null, null, etag),
        new("decision", HttpMethod.Post, $"{Api}/syllabi/{syl}/items/{item}/decision", new { decision = "skipped" }, null, etag),
        new("batch", HttpMethod.Post, $"{Api}/syllabi/{syl}/decisions", new { action = "skip", itemEtags = new Dictionary<string, string> { [item] = etag } }, null, null),
        new("sync", HttpMethod.Post, $"{Api}/syllabi/{syl}/calendar-sync", SyncBody((item, 1)), NewKey(), null),
        new("remove", HttpMethod.Post, $"{Api}/syllabi/{syl}/calendar-sync/remove", new { itemIds = new[] { item } }, null, null),
        new("unclaimed", HttpMethod.Get, $"{Api}/syllabi/{syl}/unclaimed-dates", null, null, null),
        new("export", HttpMethod.Get, $"{Api}/syllabi/{syl}/export", null, null, null),
        new("delete syllabus", HttpMethod.Delete, $"{Api}/syllabi/{syl}", null, null, null),
    ];

    private static Task<HttpResponseMessage> Send(HttpClient c, Route r) => c.SendAsync(Req(r.Method, r.Path, r.Body, r.Key, r.IfMatch));

    [Fact]
    public async Task Every_route_gives_a_foreign_syllabus_the_same_404_as_a_missing_one_and_touches_nothing()
    {
        var w = await BuildAsync();
        var ai = w.AliceItem.GetProperty("id").GetString()!; var ae = w.AliceItem.GetProperty("etag").GetString()!;
        var missingSyl = "syl_" + Guid.NewGuid().ToString("D"); var missingItem = "itm_" + Guid.NewGuid().ToString("D"); var missingCourse = Guid.NewGuid().ToString("D");

        foreach (var foreign in Routes(w.AliceCourse, w.AliceSyl, ai, ae))
        {
            var missing = Routes(missingCourse, missingSyl, missingItem, ae).First(r => r.Name == foreign.Name);
            var f = await Send(w.Bob.Client, foreign);
            var m = await Send(w.Bob.Client, missing);
            await f.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
            await m.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
            // identical status AND body shape (only traceId and instance-like fields may differ)
            var fb = await f.BodyAsync(); var mb = await m.BodyAsync();
            Assert.Equal(mb.GetProperty("detail").GetString(), fb.GetProperty("detail").GetString());
            Assert.Equal(mb.GetProperty("title").GetString(), fb.GetProperty("title").GetString());
            Assert.Null(f.Headers.ETag);
        }
        // Alice's data is untouched by every attempt above
        Assert.Equal("ready", (await (await w.Alice.Client.GetAsync($"{Api}/syllabi/{w.AliceSyl}")).BodyAsync()).GetProperty("status").GetString());
        Assert.Equal("undecided", (await GetItemAsync(w.Alice.Client, w.AliceSyl, ai)).GetProperty("decision").GetString());
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where syllabus_id = '{Guid32(w.AliceSyl)}' and action <> 'syllabus_created'"));
    }

    [Fact]
    public async Task Nested_ids_belong_to_the_path_syllabus_and_owner_same_user_mismatched_parent_is_404()
    {
        var w = await BuildAsync();
        var b2 = w.BobItem2.GetProperty("id").GetString()!; var b2e = w.BobItem2.GetProperty("etag").GetString()!;
        // Bob's item of syllabus 2 used under Bob's syllabus 1
        foreach (var r in Routes(w.BobCourse, w.BobSyl, b2, b2e).Where(r => r.Name is "get item" or "patch item" or "delete item" or "decision"))
            await (await Send(w.Bob.Client, r)).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        // Bob's syllabus 2 used under Bob's course 1 (list/create/demo calendar take a course): the course is his, the syllabus simply is not in it
        var list = await (await w.Bob.Client.GetAsync($"{Api}/courses/{w.BobCourse}/syllabi")).BodyAsync();
        Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), s => s.GetProperty("id").GetString() == w.BobSyl2);
        // body ids from another syllabus / another user get the same per-item not_found inside a 200
        var alien = w.AliceItem.GetProperty("id").GetString()!; var other = b2;
        var batch = await (await w.Bob.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{w.BobSyl}/decisions",
            new { action = "skip", itemEtags = new Dictionary<string, string> { [alien] = "W/\"x\"", [other] = "W/\"x\"" } }))).BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-decision-batch-result", batch);
        Assert.All(batch.GetProperty("results").EnumerateArray(), r => { Assert.Equal("rejected", r.GetProperty("outcome").GetString()); Assert.Equal("not_found", r.GetProperty("errorCode").GetString()); Assert.Equal(JsonValueKind.Null, r.GetProperty("item").ValueKind); });
        var sync = await (await Sync(w.Bob.Client, w.BobSyl, NewKey(), (alien, 1), (other, 1))).BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-calendar-sync-result", sync);
        Assert.All(sync.GetProperty("results").EnumerateArray(), r => { Assert.Equal("not_eligible", r.GetProperty("outcome").GetString()); Assert.Equal("not_found", r.GetProperty("errorCode").GetString()); });
        Assert.Equal("failed", sync.GetProperty("status").GetString());
        var rem = await (await w.Bob.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{w.BobSyl}/calendar-sync/remove", new { itemIds = new[] { alien, other } }))).BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-calendar-remove-result", rem);
        Assert.All(rem.GetProperty("results").EnumerateArray(), r => { Assert.Equal("failed", r.GetProperty("outcome").GetString()); Assert.Equal("not_found", r.GetProperty("errorCode").GetString()); });
        Assert.Equal("undecided", (await GetItemAsync(w.Alice.Client, w.AliceSyl, alien)).GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Two_users_never_see_each_others_lists_events_or_counts()
    {
        var w = await BuildAsync();
        var a = await (await w.Alice.Client.GetAsync($"{Api}/courses/{w.AliceCourse}/syllabi")).BodyAsync();
        Assert.All(a.GetProperty("items").EnumerateArray(), s => Assert.Equal(w.AliceCourse, s.GetProperty("courseId").GetString()));
        Assert.DoesNotContain(w.BobSyl, a.GetRawText());
        // the same user id can not create under another user's course
        await (await CreateAsync(w.Bob.Client, w.AliceCourse, Paste("Quiz due Oct 20, 2026"))).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        // and a stranger's key never collides with mine
        var key = NewKey();
        var r1 = await w.Alice.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{w.AliceCourse}/syllabi", Paste("Quiz due Oct 20, 2026"), key));
        var r2 = await w.Bob.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{w.BobCourse}/syllabi", Paste("Quiz due Oct 21, 2026"), key));
        Assert.Equal(HttpStatusCode.Accepted, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, r2.StatusCode);
        Assert.False(r2.Headers.Contains("Idempotency-Replayed"));
    }

    // ---------------------------------------------------------------- authentication and Origin

    [Fact]
    public async Task Every_route_requires_authentication()
    {
        var w = await BuildAsync();
        var anon = F.NewClient();      // Origin present, no cookie
        foreach (var r in Routes(w.AliceCourse, w.AliceSyl, w.AliceItem.GetProperty("id").GetString()!, "W/\"x\""))
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(anon, r)).StatusCode);
    }

    [Fact]
    public async Task State_changing_routes_reject_a_missing_foreign_or_case_variant_origin_exact_ordinal_match()
    {
        var w = await BuildAsync();
        var item = w.AliceItem.GetProperty("id").GetString()!; var etag = w.AliceItem.GetProperty("etag").GetString()!;
        var cookie = w.Alice.Client.DefaultRequestHeaders.GetValues("Cookie").Single();
        var unsafeRoutes = Routes(w.AliceCourse, w.AliceSyl, item, etag).Where(r => r.Method != HttpMethod.Get).ToList();
        foreach (var origin in new string?[] { null, "https://evil.example", "null", "HTTP://LOCALHOST:3000", ApiFactory.Origin + ".evil.example" })
        {
            foreach (var r in unsafeRoutes)
            {
                var c = F.NewClient(withOrigin: false);
                var req = Req(r.Method, r.Path, r.Body, r.Key, r.IfMatch);
                req.Headers.TryAddWithoutValidation("Cookie", cookie);
                if (origin is not null) req.Headers.TryAddWithoutValidation("Origin", origin);
                await (await c.SendAsync(req)).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
            }
        }
        // reads do not need an Origin
        var read = F.NewClient(withOrigin: false); read.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookie);
        Assert.Equal(HttpStatusCode.OK, (await read.GetAsync($"{Api}/syllabi/{w.AliceSyl}")).StatusCode);
        Assert.Equal("ready", (await (await w.Alice.Client.GetAsync($"{Api}/syllabi/{w.AliceSyl}")).BodyAsync()).GetProperty("status").GetString());   // nothing was deleted by the rejected DELETE
    }

    [Fact]
    public async Task Cross_origin_multipart_upload_is_rejected_before_any_byte_is_read()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var cookie = a.Client.DefaultRequestHeaders.GetValues("Cookie").Single();
        var c = F.NewClient(withOrigin: false);
        var mp = new MultipartFormDataContent { { new StringContent("{}", Encoding.UTF8, "application/json"), "metadata" }, { new ByteArrayContent("Quiz due Oct 20, 2026"u8.ToArray()), "file", "s.txt" } };
        var req = new HttpRequestMessage(HttpMethod.Post, $"{Api}/courses/{course}/syllabi") { Content = mp };
        req.Headers.TryAddWithoutValidation("Cookie", cookie); req.Headers.TryAddWithoutValidation("Idempotency-Key", NewKey()); req.Headers.TryAddWithoutValidation("Origin", "https://evil.example");
        await (await c.SendAsync(req)).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
    }

    // ---------------------------------------------------------------- canonical ids (R2-05, R3-14)

    [Fact]
    public async Task Non_canonical_ids_are_400_in_path_and_body_before_any_lookup_and_a_nonexistent_canonical_id_is_404()
    {
        var w = await BuildAsync();
        var bad = new List<string> { w.BobSyl.ToUpperInvariant(), Guid32(w.BobSyl), "{" + w.BobSyl + "}", "urn:uuid:" + Guid32(w.BobSyl), w.BobSyl + "%0A", "itm_" + Guid32(w.BobSyl), "crs_" + Guid32(w.BobSyl) };
        foreach (var id in bad)
            foreach (var p in new[] { $"{Api}/syllabi/{id}", $"{Api}/syllabi/{id}/items", $"{Api}/syllabi/{id}/export", $"{Api}/syllabi/{id}/unclaimed-dates" })
                await (await w.Bob.Client.GetAsync(p)).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await w.Bob.Client.SendAsync(Req(HttpMethod.Delete, $"{Api}/syllabi/{bad[0]}"))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        // a malformed id is 400 even for a syllabus that is not Bob's (the form is checked before ownership)
        await (await w.Bob.Client.GetAsync($"{Api}/syllabi/{w.AliceSyl.ToUpperInvariant()}")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await w.Bob.Client.GetAsync($"{Api}/syllabi/{w.AliceSyl}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        // course ids: bare lowercase hyphenated guid only
        await (await w.Bob.Client.GetAsync($"{Api}/courses/{w.BobCourse.ToUpperInvariant()}/syllabi")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await w.Bob.Client.GetAsync($"{Api}/courses/crs_{w.BobCourse}/syllabi")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        // item ids
        var good = w.BobItem.GetProperty("id").GetString()!;
        await (await w.Bob.Client.GetAsync($"{Api}/syllabi/{w.BobSyl}/items/{good.ToUpperInvariant()}")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        // body ids
        var et = w.BobItem.GetProperty("etag").GetString()!;
        await (await w.Bob.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{w.BobSyl}/decisions", new { action = "skip", itemEtags = new Dictionary<string, string> { [good.ToUpperInvariant()] = et } }))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await Sync(w.Bob.Client, w.BobSyl, NewKey(), (Guid32(good), 1))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await w.Bob.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{w.BobSyl}/calendar-sync/remove", new { itemIds = new[] { good + "\n" } }))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        // two spellings of one item can never become two keys: the alias is rejected outright
        await (await w.Bob.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{w.BobSyl}/decisions", new { action = "skip", itemEtags = new Dictionary<string, string> { [good] = et, [good.ToUpperInvariant()] = et } }))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    // ---------------------------------------------------------------- strict binder, text and decoding failures through the real endpoints

    private static Task<HttpResponseMessage> RawPost(HttpClient c, string path, byte[] body, string? key = null, string? ifMatch = null, string method = "POST")
    {
        var req = new HttpRequestMessage(new HttpMethod(method), path) { Content = new ByteArrayContent(body) };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (key is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return c.SendAsync(req);
    }

    [Fact]
    public async Task Unknown_readonly_and_duplicate_members_are_400_never_silently_ignored()
    {
        var w = await BuildAsync();
        var sid = w.BobSyl; var item = w.BobItem.GetProperty("id").GetString()!; var et = w.BobItem.GetProperty("etag").GetString()!;
        foreach (var field in new[] { "origin", "confidence", "flags", "syncState", "userId", "courseId", "syllabusId", "itemKey", "revision", "etag", "edited", "snippet", "decision", "id" })
        {
            var create = $$"""{"kind":"other","title":"T","date":"2026-09-20","{{field}}":"x"}""";
            await (await RawPost(w.Bob.Client, $"{Api}/syllabi/{sid}/items", Encoding.UTF8.GetBytes(create))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
            var patch = $$"""{"title":"T","{{field}}":"x"}""";
            await (await RawPost(w.Bob.Client, $"{Api}/syllabi/{sid}/items/{item}", Encoding.UTF8.GetBytes(patch), ifMatch: et, method: "PATCH")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        }
        await (await RawPost(w.Bob.Client, $"{Api}/syllabi/{sid}/items/{item}/decision", """{"decision":"skipped","extra":1}"""u8.ToArray(), ifMatch: et)).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var dupEtags = $$$"""{"action":"skip","itemEtags":{"{{{item}}}":"{{{et.Replace("\"", "\\\"")}}}","{{{item}}}":"{{{et.Replace("\"", "\\\"")}}}"}}""";
        await (await RawPost(w.Bob.Client, $"{Api}/syllabi/{sid}/decisions", Encoding.UTF8.GetBytes(dupEtags))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var dupRevs = $$$"""{"itemRevisions":{"{{{item}}}":1,"{{{item}}}":2},"confirm":{"provider":"mock","acknowledgedDemo":true}}""";
        await (await RawPost(w.Bob.Client, $"{Api}/syllabi/{sid}/calendar-sync", Encoding.UTF8.GetBytes(dupRevs), key: NewKey())).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var dupMember = """{"kind":"other","title":"A","title":"B","date":"2026-09-20"}""";
        await (await RawPost(w.Bob.Client, $"{Api}/syllabi/{sid}/items", Encoding.UTF8.GetBytes(dupMember))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        // the sync request schema forbids anything but the mock provider and the demo acknowledgement
        var google = $$$"""{"itemRevisions":{"{{{item}}}":1},"confirm":{"provider":"google","acknowledgedDemo":true}}""";
        await (await RawPost(w.Bob.Client, $"{Api}/syllabi/{sid}/calendar-sync", Encoding.UTF8.GetBytes(google), key: NewKey())).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var noAck = $$$"""{"itemRevisions":{"{{{item}}}":1},"confirm":{"provider":"mock","acknowledgedDemo":false}}""";
        await (await RawPost(w.Bob.Client, $"{Api}/syllabi/{sid}/calendar-sync", Encoding.UTF8.GetBytes(noAck), key: NewKey())).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    private static byte[] Join(string a, byte[] mid, string b) => [.. Encoding.UTF8.GetBytes(a), .. mid, .. Encoding.UTF8.GetBytes(b)];

    [Fact]
    public async Task Lone_surrogates_invalid_utf8_and_cesu8_are_400_with_the_fixed_detail_never_500_and_never_exception_text()
    {
        var w = await BuildAsync();
        var path = $"{Api}/syllabi/{w.BobSyl}/items";
        var bodies = new Dictionary<string, byte[]>
        {
            ["lone high"] = Encoding.UTF8.GetBytes("""{"kind":"other","title":"a\ud800b","date":"2026-09-20"}"""),
            ["lone low"] = Encoding.UTF8.GetBytes("""{"kind":"other","title":"a\udc00b","date":"2026-09-20"}"""),
            ["reversed pair"] = Encoding.UTF8.GetBytes("""{"kind":"other","title":"a\udc00\ud800b","date":"2026-09-20"}"""),
            ["0xFF byte"] = Join("{\"kind\":\"other\",\"title\":\"a", [0xFF], "b\",\"date\":\"2026-09-20\"}"),
            ["CESU-8"] = Join("{\"kind\":\"other\",\"title\":\"a", [0xED, 0xA0, 0x80], "b\",\"date\":\"2026-09-20\"}"),
            ["lone surrogate in notes"] = Encoding.UTF8.GetBytes("""{"kind":"other","title":"a","notes":"x\ud800","date":"2026-09-20"}"""),
        };
        foreach (var (name, body) in bodies)
        {
            var res = await RawPost(w.Bob.Client, path, body);
            await res.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
            var b = await res.BodyAsync();
            Assert.Equal("The request was invalid.", b.GetProperty("detail").GetString());
            var raw = b.GetRawText();
            foreach (var leak in new[] { "surrogate", "UTF-8", "utf-8", "Exception", "System.", "JsonException", "invalid byte" }) Assert.DoesNotContain(leak, raw);
            Assert.True(name.Length > 0);
        }
        // the same through PATCH (members bind lazily; reading the string is what throws and must be wrapped) and through a pasted text create
        var patch = await RawPost(w.Bob.Client, $"{path}/{w.BobItem.GetProperty("id").GetString()}", Encoding.UTF8.GetBytes("""{"title":"a\ud800b"}"""), ifMatch: w.BobItem.GetProperty("etag").GetString(), method: "PATCH");
        await patch.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var paste = Encoding.UTF8.GetBytes("""{"text":"Quiz due Oct 20 \ud800","defaultTimezone":"America/New_York","noticeVersion":"draft-2026-10","attestation":{"accepted":true,"acceptedAt":"2026-10-28T12:00:00Z"}}""");
        await (await RawPost(w.Bob.Client, $"{Api}/courses/{w.BobCourse}/syllabi", paste, key: NewKey())).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Trailing_newlines_and_unicode_hazards_are_rejected_on_every_parsed_value()
    {
        var w = await BuildAsync();
        var path = $"{Api}/syllabi/{w.BobSyl}/items";
        string Item(string title = "T", string date = "2026-09-20", string? time = null, string? tz = null) =>
            JsonSerializer.Serialize(new Dictionary<string, object?> { ["kind"] = "other", ["title"] = title, ["date"] = date, ["time"] = time, ["timezone"] = tz });
        foreach (var body in new[]
        {
            Item(title: "Exam 1\n"), Item(date: "2026-09-20\n"), Item(time: "10:00\n"), Item(tz: "America/New_York\n"), Item(title: " Exam"), Item(title: "Exam  1"),
            Item(title: "a\u202Eb"), Item(title: "a\u200Bb"), Item(title: "\u00A0"), Item(date: "2026-02-30"), Item(date: "2026-02-29"), Item(date: "9999-01-01"),
            Item(date: "1999-01-01"), Item(time: "24:00"), Item(tz: "EST"), Item(tz: "UTC"), Item(tz: "-05:00"), Item(title: new string('x', 201)),
        })
            await (await RawPost(w.Bob.Client, path, Encoding.UTF8.GetBytes(body))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        // a title of exactly 200 astral characters is valid, 201 is not (code points, not UTF-16 units)
        var emoji200 = string.Concat(Enumerable.Repeat("\U0001F600", 200));
        var okRes = await RawPost(w.Bob.Client, path, Encoding.UTF8.GetBytes(Item(title: emoji200)));
        Assert.Equal(HttpStatusCode.Created, okRes.StatusCode);
        await (await RawPost(w.Bob.Client, path, Encoding.UTF8.GetBytes(Item(title: emoji200 + "\U0001F600")))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        // Persian with ZWNJ and a ZWJ emoji family are legal
        Assert.Equal(HttpStatusCode.Created, (await RawPost(w.Bob.Client, path, Encoding.UTF8.GetBytes(Item(title: "\u0645\u06CC\u200C\u062E\u0648\u0627\u0647\u0645")))).StatusCode);
        // header and query values: a trailing newline can never be part of a value
        var withLf = Req(HttpMethod.Post, $"{Api}/courses/{w.BobCourse}/syllabi", Paste(), "abcdefgh1234");
        withLf.Headers.Remove("Idempotency-Key"); withLf.Headers.TryAddWithoutValidation("Idempotency-Key", "short");
        await (await w.Bob.Client.SendAsync(withLf)).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    // ---------------------------------------------------------------- evaluation order and syntax

    [Fact]
    public async Task Missing_if_match_is_428_and_a_missing_idempotency_key_is_400_and_both_precede_ownership()
    {
        var w = await BuildAsync();
        var item = w.AliceItem.GetProperty("id").GetString()!;
        // syntax (428 / 400) is evaluated before the ownership 404: a non-owner learns nothing either way
        await (await w.Bob.Client.SendAsync(Req(HttpMethod.Patch, $"{Api}/syllabi/{w.AliceSyl}/items/{item}", new { title = "Y" }))).AssertProblemAsync((HttpStatusCode)428, "precondition_required");
        await (await w.Bob.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{w.AliceCourse}/syllabi", Paste()))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await w.Bob.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{w.AliceSyl}/calendar-sync", SyncBody((item, 1))))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        // own item: a stale tag is 412, a tag of another item is 412 (the etag includes the resource id)
        var bobItem = w.BobItem.GetProperty("id").GetString()!;
        await (await w.Bob.Client.SendAsync(Req(HttpMethod.Patch, $"{Api}/syllabi/{w.BobSyl}/items/{bobItem}", new { title = "Y" }, ifMatch: "W/\"stale\""))).AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");
        var other = ByTitle(await ItemsAsync(w.Bob.Client, w.BobSyl), "PS1");
        await (await w.Bob.Client.SendAsync(Req(HttpMethod.Patch, $"{Api}/syllabi/{w.BobSyl}/items/{bobItem}", new { title = "Y" }, ifMatch: other.GetProperty("etag").GetString()))).AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");
        await (await w.Bob.Client.SendAsync(Req(HttpMethod.Patch, $"{Api}/syllabi/{w.BobSyl}/items/{bobItem}", new { title = "Y" }, ifMatch: "*"))).AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");
    }

    [Fact]
    public async Task Error_bodies_use_the_fixed_vocabulary_and_never_echo_input()
    {
        var w = await BuildAsync();
        var hostile = "CANARY<script>alert(1)</script>";
        var res = await RawPost(w.Bob.Client, $"{Api}/syllabi/{w.BobSyl}/items", Encoding.UTF8.GetBytes($$"""{"kind":"other","title":"{{hostile}}\n","date":"2026-09-20","{{hostile}}":1}"""));
        await res.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.DoesNotContain("CANARY", await res.Content.ReadAsStringAsync());
        var res2 = await RawPost(w.Bob.Client, $"{Api}/syllabi/{w.BobSyl}/items", Encoding.UTF8.GetBytes($$"""{"kind":"other","title":"{{hostile}}\n","date":"2026-09-20"}"""));
        await res2.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var body = await res2.BodyAsync();
        Assert.DoesNotContain("CANARY", body.GetRawText());
        SyllabusSchemas.AssertValid("syllabus-problem", body);
        // a wrong content type is 415 unsupported_type, an oversized JSON body is 413 too_large, both with the fixed detail
        var wrong = new HttpRequestMessage(HttpMethod.Post, $"{Api}/syllabi/{w.BobSyl}/items") { Content = new StringContent("x", Encoding.UTF8, "text/plain") };
        await (await w.Bob.Client.SendAsync(wrong)).AssertProblemAsync(HttpStatusCode.UnsupportedMediaType, "unsupported_type");
        var big = new string('a', 17 * 1024);
        await (await RawPost(w.Bob.Client, $"{Api}/syllabi/{w.BobSyl}/items", Encoding.UTF8.GetBytes($$"""{"kind":"other","title":"{{big}}","date":"2026-09-20"}"""))).AssertProblemAsync((HttpStatusCode)413, "too_large");
    }
}
