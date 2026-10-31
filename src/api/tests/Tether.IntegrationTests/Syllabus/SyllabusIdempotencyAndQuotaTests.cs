using System.Net;
using System.Text.Json;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Tether.Syllabus.Application;

namespace Tether.IntegrationTests.Syllabus;

/// <summary>The extraction worker is OFF here, so created syllabi stay queued until a test runs the runner by hand (deterministic in-flight and replay-while-running cases).</summary>
public class SyllabusIdempotencyAndQuotaTests(SyllabusIdempotencyAndQuotaTests.Factory f) : SyllabusTestBase(f), IClassFixture<SyllabusIdempotencyAndQuotaTests.Factory>
{
    public sealed class Factory : ApiFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            new("Syllabus:Worker:Enabled", "false"),
            new("Syllabus:SyllabiPerUser", "6"),
            new("Syllabus:NoticeVersions:0", "draft-2026-10"),
            new("Syllabus:NoticeVersions:1", "n" + new string('v', 63)),
        ];
    }

    private Task RunJobsAsync() => RunAsync();
    private async Task RunAsync() { var r = F.Services.GetRequiredService<ExtractionRunner>(); while (await r.RunOnceAsync(CancellationToken.None)) { } }

    private static string Txt(int n) => $"Quiz {n} due Oct {10 + n}, 2026 at 9:00 AM";

    [Fact]
    public async Task Same_key_same_body_is_a_replay_even_while_the_job_runs_and_never_extraction_in_progress()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var key = NewKey(); var body = Paste(Txt(1));
        var first = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{course}/syllabi", body, key));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var id = (await first.BodyAsync()).GetProperty("id").GetString();
        Assert.Equal("queued", (await first.BodyAsync()).GetProperty("status").GetString());
        // the original job has not run: a different request would be 409 extraction_in_progress ...
        await (await CreateAsync(a.Client, course, Paste(Txt(2)))).AssertProblemAsync(HttpStatusCode.Conflict, "extraction_in_progress");
        // ... but a retry of the SAME create is recognised as a retry first
        var replay = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{course}/syllabi", body, key));
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(id, (await replay.BodyAsync()).GetProperty("id").GetString());
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.syllabi where user_id = '{a.UserId}'"));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.extraction_jobs where user_id = '{a.UserId}'"));
        // the same key with a different body (or on a different course) is 400, never the first syllabus
        await (await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{course}/syllabi", Paste(Txt(3)), key))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var other = await CourseAsync(a.Client, "Other");
        await (await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{other}/syllabi", body, key))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await RunAsync();
    }

    [Fact]
    public async Task One_extraction_in_flight_per_user_by_the_database_and_released_when_the_job_finishes()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        Assert.Equal(HttpStatusCode.Accepted, (await CreateAsync(a.Client, course, Paste(Txt(1)))).StatusCode);
        await (await CreateAsync(a.Client, course, Paste(Txt(2)))).AssertProblemAsync(HttpStatusCode.Conflict, "extraction_in_progress");
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.extraction_jobs where user_id = '{a.UserId}' and state in ('queued','running')"));
        await RunAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await CreateAsync(a.Client, course, Paste(Txt(2)))).StatusCode);
        // a different user is not affected
        var b = await NewActorAsync(); var bc = await CourseAsync(b.Client);
        Assert.Equal(HttpStatusCode.Accepted, (await CreateAsync(b.Client, bc, Paste(Txt(1)))).StatusCode);
        await RunAsync();
    }

    [Fact]
    public async Task Two_simultaneous_identical_creates_yield_one_syllabus_and_one_job()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var key = NewKey(); var body = Paste(Txt(1));
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{course}/syllabi", body, key))));
        var seen = string.Join("; ", await Task.WhenAll(results.Select(async r => $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}")));
        // API_CONTRACTS_P2 2.1: a retry with the same key and fingerprint while the original job is running "gets the replay (202, ...), not 409 extraction_in_progress".
        Assert.All(results, r => Assert.True(r.StatusCode == HttpStatusCode.Accepted, seen));
        var ids = (await Task.WhenAll(results.Select(async r => (await r.BodyAsync()).GetProperty("id").GetString()))).Distinct().ToList();
        Assert.Single(ids);
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.syllabi where user_id = '{a.UserId}'"));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.extraction_jobs where user_id = '{a.UserId}'"));
        await RunAsync();
    }

    [Fact]
    public async Task Delete_turns_the_create_key_into_a_content_free_tombstone_and_a_late_retry_is_404_and_creates_nothing()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var key = NewKey(); var body = Paste(Txt(1));
        var created = await (await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{course}/syllabi", body, key))).BodyAsync();
        var id = created.GetProperty("id").GetString()!;
        await RunAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.SendAsync(Req(HttpMethod.Delete, $"{Api}/syllabi/{id}"))).StatusCode);

        var row = await F.TextAsync($"select concat_ws('|', course_id, result_id, syllabus_id, tombstoned_at is not null) from syllabus.idempotency_keys where user_id = '{a.UserId}' and route_template like 'POST /courses%'");
        Assert.Equal("t", row);                                       // no course id, no syllabus id, no result id survive
        var retry = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{course}/syllabi", body, key));
        await retry.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");   // indistinguishable on the wire from any missing resource
        Assert.False(retry.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.syllabi where user_id = '{a.UserId}'"));
        // a different fingerprint on the tombstoned key is 400; a fresh key creates a new syllabus
        await (await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{course}/syllabi", Paste(Txt(2)), key))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(HttpStatusCode.Accepted, (await CreateAsync(a.Client, course, body)).StatusCode);
        await RunAsync();
    }

    [Fact]
    public async Task Per_course_cap_per_user_cap_duplicates_and_the_never_refunded_daily_counter()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        for (var i = 1; i <= 5; i++)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await CreateAsync(a.Client, course, Paste(Txt(i)))).StatusCode);
            await RunAsync();
        }
        await (await CreateAsync(a.Client, course, Paste(Txt(6)))).AssertProblemAsync(HttpStatusCode.Conflict, "syllabus_limit_reached");   // 5 per course
        var c2 = await CourseAsync(a.Client, "Second");
        Assert.Equal(HttpStatusCode.Accepted, (await CreateAsync(a.Client, c2, Paste(Txt(6)))).StatusCode);                                     // 6th for the user (cap configured to 6)
        await RunAsync();
        var c3 = await CourseAsync(a.Client, "Third");
        await (await CreateAsync(a.Client, c3, Paste(Txt(7)))).AssertProblemAsync(HttpStatusCode.Conflict, "syllabus_limit_reached");           // per-user cap

        // duplicates: same content in the same course is 409 with the existing id, unless allowDuplicate
        var b = await NewActorAsync(); var bc = await CourseAsync(b.Client);
        var first = await (await CreateAsync(b.Client, bc, Paste(Txt(1)))).BodyAsync(); await RunAsync();
        var dup = await CreateAsync(b.Client, bc, Paste(Txt(1)));
        await dup.AssertProblemAsync(HttpStatusCode.Conflict, "duplicate_syllabus");
        var problem = await dup.BodyAsync();
        Assert.Equal(first.GetProperty("id").GetString(), problem.GetProperty("existingSyllabusId").GetString());
        SyllabusSchemas.AssertValid("syllabus-problem", problem);
        Assert.Equal(HttpStatusCode.Accepted, (await CreateAsync(b.Client, bc, Paste(Txt(1), allowDuplicate: true))).StatusCode);
        await RunAsync();
        // another user's identical upload is not a duplicate of mine and never reveals mine
        var c = await NewActorAsync(); var cc = await CourseAsync(c.Client);
        Assert.Equal(HttpStatusCode.Accepted, (await CreateAsync(c.Client, cc, Paste(Txt(1)))).StatusCode);
        await RunAsync();
    }

    [Fact]
    public async Task Daily_creation_counter_is_durable_counts_in_mock_mode_and_is_never_refunded_by_delete_or_failure()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var ids = new List<string>();
        for (var i = 1; i <= 10; i++)
        {
            var res = await CreateAsync(a.Client, course, Paste(Txt(i)));
            Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
            ids.Add((await res.BodyAsync()).GetProperty("id").GetString()!);
            await RunAsync();
            // keep clear of the per-course and per-user caps: delete as we go (the counter must NOT be refunded)
            Assert.Equal(HttpStatusCode.NoContent, (await a.Client.SendAsync(Req(HttpMethod.Delete, $"{Api}/syllabi/{ids[^1]}"))).StatusCode);
        }
        Assert.Equal(10, await F.ScalarAsync($"select created from syllabus.creation_counters where user_id = '{a.UserId}'"));
        var eleventh = await CreateAsync(a.Client, course, Paste(Txt(11)));
        await eleventh.AssertProblemAsync((HttpStatusCode)429, "rate_limited");
        Assert.True(eleventh.Headers.Contains("Retry-After"));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.syllabi where user_id = '{a.UserId}'"));
        // another user's counter is independent
        var b = await NewActorAsync(); var bc = await CourseAsync(b.Client);
        Assert.Equal(HttpStatusCode.Accepted, (await CreateAsync(b.Client, bc, Paste(Txt(1)))).StatusCode);
        await RunAsync();
        // a rejected create (validation) does not count
        var bad = Paste(Txt(2)); bad["defaultTimezone"] = "EST";
        await (await CreateAsync(b.Client, bc, bad)).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(1, await F.ScalarAsync($"select created from syllabus.creation_counters where user_id = '{b.UserId}'"));
    }

    [Fact]
    public async Task Item_create_idempotency_replays_the_item_and_a_key_reuse_with_another_body_or_syllabus_is_400()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await (await CreateAsync(a.Client, course, Paste(Txt(1)))).BodyAsync()).GetProperty("id").GetString()!; await RunAsync();
        var key = NewKey();
        var body = new { kind = "other", title = "Study group", date = "2026-09-20" };
        var r1 = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/items", body, key));
        var r2 = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/items", body, key));
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode); Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        Assert.Equal("true", r2.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal((await r1.BodyAsync()).GetProperty("id").GetString(), (await r2.BodyAsync()).GetProperty("id").GetString());
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.items where syllabus_id = '{Guid32(sid)}' and origin = 'student'"));
        await (await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/items", new { kind = "other", title = "Different", date = "2026-09-20" }, key))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Hand_entry_is_allowed_only_into_ready_or_partial_syllabi()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await (await CreateAsync(a.Client, course, Paste(Txt(1)))).BodyAsync()).GetProperty("id").GetString()!;   // still queued
        await (await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/items", new { kind = "other", title = "x", date = "2026-09-20" }))).AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");
        await (await Sync(a.Client, sid, NewKey(), ("itm_" + Guid.NewGuid().ToString("D"), 1))).AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");
        await RunAsync();
    }

    [Fact]
    public async Task A_64_character_notice_version_persists_and_an_unknown_one_is_rejected()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var long64 = "n" + new string('v', 63);
        var body = Paste(Txt(1)); body["noticeVersion"] = long64;
        var res = await CreateAsync(a.Client, course, body);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        Assert.Equal(long64, (await res.BodyAsync()).GetProperty("notice").GetProperty("version").GetString());
        await RunAsync();
        var unknown = Paste(Txt(2)); unknown["noticeVersion"] = "something-else";
        await (await CreateAsync(a.Client, course, unknown)).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task A_30000_emoji_paste_succeeds_and_persists_and_over_50000_code_points_is_413()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var emoji = string.Concat(Enumerable.Repeat("\U0001F600", 30_000));
        var res = await CreateAsync(a.Client, course, Paste(emoji));
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        Assert.Equal(30_000, (await res.BodyAsync()).GetProperty("document").GetProperty("charCount").GetInt32());
        Assert.Equal(60_000, await F.ScalarAsync($"select utf16_length from syllabus.canonical_texts where user_id = '{a.UserId}'"));
        await RunAsync();
        var big = string.Concat(Enumerable.Repeat("\U0001F600", 50_001));
        await (await CreateAsync(a.Client, await CourseAsync(a.Client, "Big"), Paste(big))).AssertProblemAsync((HttpStatusCode)413, "too_large");
    }
}
