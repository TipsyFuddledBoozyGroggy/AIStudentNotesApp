using System.Net;
using System.Text.Json;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.IntegrationTests;

/// <summary>Audio socket /ingest/sessions/{id}/audio (speech.v1) with the deterministic demo provider.</summary>
public class RealtimeAudioTests(RealtimeAudioTests.Factory f) : LectureTestBase(f), IClassFixture<RealtimeAudioTests.Factory>
{
    public sealed class Factory : RealtimeFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Extra =>
            [new("Realtime:HelloTimeoutMs", "600"), new("Realtime:FailedHelloPerMinute", "1000")];
    }

    private async Task<(Actor A, string Course, string Session)> LiveAsync()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var s = await StartOkAsync(a.Client, course);
        return (a, course, s.GetProperty("id").GetString()!);
    }

    private static async Task<string> TicketAsync(HttpClient c, string sessionId)
    {
        var res = await PostAsync(c, $"/sessions/{sessionId}/audio-ticket");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.BodyAsync()).GetProperty("ticket").GetString()!;
    }

    private async Task<Rt.AudioClient> OpenAsync(Actor a, string sessionId, string? ticket = null)
    {
        var client = await Rt.AudioClient.ConnectAsync(F, Rt.CookieOf(a.Client), sessionId);
        await client.HelloAsync(ticket ?? await TicketAsync(a.Client, sessionId));
        return client;
    }

    private Task<long> EventCountAsync(string id) => F.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}'");

    // ---------------- before the upgrade ----------------

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("http://evil.example")]
    public async Task The_upgrade_needs_an_allowed_Origin_and_is_refused_with_403_before_any_socket_exists(string? origin)
    {
        var (a, _, id) = await LiveAsync();
        var res = await Rt.UpgradeGetAsync(F, $"/ingest/sessions/{id}/audio", origin, Rt.CookieOf(a.Client));
        await res.AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Rt.AudioClient.ConnectAsync(F, Rt.CookieOf(a.Client), id, origin));
        Assert.Contains("403", ex.Message);
    }

    [Fact]
    public async Task The_upgrade_without_a_session_cookie_is_401_and_a_plain_GET_is_400()
    {
        var (a, _, id) = await LiveAsync();
        await (await Rt.UpgradeGetAsync(F, $"/ingest/sessions/{id}/audio", ApiFactory.Origin, null)).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        await (await a.Client.GetAsync($"/ingest/sessions/{id}/audio")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Upgrades_are_limited_per_user()
    {
        var limited = new UpgradeLimitFactory();
        await ((IAsyncLifetime)limited).InitializeAsync();
        try
        {
            var c = await limited.LoginAsync($"u{Guid.NewGuid():N}@dev.local");
            var id = Guid.NewGuid();
            for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/ingest/sessions/{id}/audio")).StatusCode);
            await (await c.GetAsync($"/ingest/sessions/{id}/audio")).AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited");
        }
        finally { await ((IAsyncLifetime)limited).DisposeAsync(); }
    }

    [Fact]
    public async Task Repeated_failed_hellos_block_further_upgrades_for_that_user()
    {
        var strict = new FailedHelloFactory();
        await ((IAsyncLifetime)strict).InitializeAsync();
        try
        {
            var a = await strict.LoginAsync($"u{Guid.NewGuid():N}@dev.local");
            var id = (await StartOkAsync(a, await CourseAsync(a))).GetProperty("id").GetString()!;
            for (var i = 0; i < 3; i++)
            {
                await using var bad = await Rt.AudioClient.ConnectAsync(strict, Rt.CookieOf(a), id);
                await bad.HelloAsync(new string((char)(65 + i), 43));
                Assert.Equal(4401, await bad.WaitCloseAsync());
            }
            await (await Rt.UpgradeGetAsync(strict, $"/ingest/sessions/{id}/audio", ApiFactory.Origin, Rt.CookieOf(a))).AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited");
        }
        finally { await ((IAsyncLifetime)strict).DisposeAsync(); }
    }

    private sealed class FailedHelloFactory : ApiFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Realtime:FailedHelloPerMinute", "3"), new("Realtime:UpgradePermitPerMinute", "1000")];
    }

    private sealed class UpgradeLimitFactory : ApiFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Realtime:UpgradePermitPerMinute", "3")];
    }

    // ---------------- ticket and hello ----------------

    [Fact]
    public async Task A_valid_ticket_streams_demo_audio_into_persisted_gap_free_events_and_live_hub_events()
    {
        var (a, _, id) = await LiveAsync();
        await using var hub = await Rt.HubProbe.StartAsync(F, Rt.CookieOf(a.Client));
        await hub.JoinAsync(id);
        await using var audio = await OpenAsync(a, id);
        await audio.SendAudioAsync(48); // 12 s of silence-valued PCM; the demo source is driven by audio position only

        Assert.True(await Rt.WaitAsync(async () => await EventCountAsync(id) >= 5), "4 finals and 1 correction expected");
        Assert.True(await Rt.WaitAsync(() => audio.FramesOf("ack").Count == 48));

        var page = await (await a.Client.GetAsync($"/api/v1/sessions/{id}/transcript?afterSeq=0&limit=500")).BodyAsync();
        ContractSchemas.AssertValid("transcript-page", page);
        var events = page.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(Enumerable.Range(1, events.Count).Select(i => i.ToString()), events.Select(e => e.GetProperty("seq").GetString()!)); // gap-free, shared with session.state
        Assert.Equal("session.state", events[0].GetProperty("type").GetString());
        var finals = events.Where(e => e.GetProperty("type").GetString() == "transcript.final").ToList();
        Assert.Equal(["seg_1", "seg_2", "seg_3", "seg_4"], finals.Select(e => e.GetProperty("payload").GetProperty("segmentId").GetString()!).ToArray());
        Assert.All(finals, e => Assert.Equal("replay-demo", e.GetProperty("payload").GetProperty("provider").GetString()));
        Assert.Equal([0L, 3000L, 6000L, 9000L], finals.Select(e => e.GetProperty("payload").GetProperty("startMs").GetInt64()).ToArray());
        var correction = events.Single(e => e.GetProperty("type").GetString() == "transcript.correction").GetProperty("payload");
        Assert.Equal("seg_2", correction.GetProperty("segmentId").GetString());
        Assert.Equal(2, correction.GetProperty("revision").GetInt32());
        Assert.Equal(1, correction.GetProperty("supersedes").GetInt32());
        Assert.False(page.GetProperty("hasMore").GetBoolean());

        // Live: persisted events carry the same seq, partials are ephemeral with seq null, everything validates.
        Assert.True(await Rt.WaitAsync(() => hub.Of("transcript.final").Count == 4 && hub.Of("transcript.partial").Count > 0 && hub.Of("transcript.correction").Count == 1));
        foreach (var e in hub.Snapshot()) ContractSchemas.AssertValid("hub-event", e);
        Assert.All(hub.Of("transcript.partial"), e => Assert.Equal(JsonValueKind.Null, e.GetProperty("seq").ValueKind));
        Assert.All(hub.Of("transcript.final"), e => Assert.Equal(JsonValueKind.String, e.GetProperty("seq").ValueKind));
        var seg1Partials = hub.Of("transcript.partial").Where(e => e.GetProperty("payload").GetProperty("segmentId").GetString() == "seg_1").Select(e => e.GetProperty("payload").GetProperty("revision").GetInt32()).ToList();
        Assert.Equal(seg1Partials.Order().ToList(), seg1Partials); // monotonic revisions
        Assert.Equal(seg1Partials.Distinct().Count(), seg1Partials.Count);
        Assert.Contains(hub.Of("stt.status"), e => e.GetProperty("payload").GetProperty("state").GetString() == "Ok");
        foreach (var ack in audio.FramesOf("ack")) ContractSchemas.AssertValid("speech.ack", ack);

        // Persisted text lives in the source table only; partials were never stored.
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from transcript.transcript_events where kind not in ('Final','Correction','Gap') and session_id = '{id}'"));
        Assert.Equal(5, await EventCountAsync(id));
    }

    [Fact]
    public async Task Timecodes_come_from_accepted_sample_counts_not_from_the_client_supplied_offset()
    {
        var (a, _, id) = await LiveAsync();
        await using var audio = await OpenAsync(a, id);
        await audio.SendAudioAsync(12, bogusOffset: 999_999_999); // 3 s of audio, absurd client offsets
        Assert.True(await Rt.WaitAsync(async () => await F.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}' and kind = 'Final'") >= 1));
        Assert.Equal(2500, await F.ScalarAsync($"select end_ms from transcript.transcript_events where session_id = '{id}' and kind = 'Final' and segment_no = 1"));
    }

    [Fact]
    public async Task A_ticket_works_once_replay_bad_foreign_and_expired_tickets_all_close_with_the_same_4401()
    {
        var (a, _, id) = await LiveAsync();
        var (b, _, _) = await LiveAsync();
        var ticket = await TicketAsync(a.Client, id);

        await using (var first = await Rt.AudioClient.ConnectAsync(F, Rt.CookieOf(a.Client), id))
        {
            await first.HelloAsync(ticket);
            await first.SendAudioAsync(2);
            Assert.True(await Rt.WaitAsync(() => first.FramesOf("ack").Count == 2));
        }
        await using var replay = await Rt.AudioClient.ConnectAsync(F, Rt.CookieOf(a.Client), id);
        await replay.HelloAsync(ticket);
        Assert.Equal(4401, await replay.WaitCloseAsync());

        await using var garbage = await Rt.AudioClient.ConnectAsync(F, Rt.CookieOf(a.Client), id);
        await garbage.HelloAsync(new string('A', 43));
        Assert.Equal(4401, await garbage.WaitCloseAsync());

        // Another user's cookie with this user's ticket, and the owner's ticket for someone else's session.
        var live = await TicketAsync(a.Client, id);
        await using var foreignCookie = await Rt.AudioClient.ConnectAsync(F, Rt.CookieOf(b.Client), id);
        await foreignCookie.HelloAsync(live);
        Assert.Equal(4401, await foreignCookie.WaitCloseAsync());

        await using var expired = await Rt.AudioClient.ConnectAsync(F, Rt.CookieOf(a.Client), id);
        F.Clock.Advance(TimeSpan.FromSeconds(31));
        await expired.HelloAsync(live);
        Assert.Equal(4401, await expired.WaitCloseAsync());
    }

    [Fact]
    public async Task A_ticket_for_a_session_that_is_paused_has_been_invalidated_and_closes_4401()
    {
        var (a, _, id) = await LiveAsync();
        var ticket = await TicketAsync(a.Client, id);
        await PostAsync(a.Client, $"/sessions/{id}/pause");
        await using var audio = await Rt.AudioClient.ConnectAsync(F, Rt.CookieOf(a.Client), id);
        await audio.HelloAsync(ticket);
        Assert.Equal(4401, await audio.WaitCloseAsync());
        Assert.Equal(0, await EventCountAsync(id));
    }

    [Fact]
    public async Task Hello_must_arrive_in_time_be_small_text_and_match_the_schema()
    {
        var (a, _, id) = await LiveAsync();
        await using (var slow = await Rt.AudioClient.ConnectAsync(F, Rt.CookieOf(a.Client), id))
            Assert.Equal(1008, await slow.WaitCloseAsync()); // nothing sent within the hello timeout

        await using (var binary = await Rt.AudioClient.ConnectAsync(F, Rt.CookieOf(a.Client), id))
        {
            await binary.SendBinaryAsync(Rt.AudioClient.Frame(0));
            Assert.Equal(1008, await binary.WaitCloseAsync()); // binary before hello
        }
        await using (var big = await Rt.AudioClient.ConnectAsync(F, Rt.CookieOf(a.Client), id))
        {
            await big.HelloAsync("x", raw: "{\"type\":\"hello\",\"pad\":\"" + new string('a', 1500) + "\"}");
            Assert.Equal(1009, await big.WaitCloseAsync());
        }
        foreach (var bad in new[]
        {
            """{"type":"hello","version":"speech.v1","ticket":"aaaaaaaaaaaaaaaaaaaaaaaa","sampleRate":48000,"encoding":"pcm_s16le","channels":1}""",
            """{"type":"hello","version":"speech.v1","ticket":"aaaaaaaaaaaaaaaaaaaaaaaa","sampleRate":16000,"encoding":"pcm_s16le","channels":2}""",
            """{"type":"hello","version":"speech.v2","ticket":"aaaaaaaaaaaaaaaaaaaaaaaa","sampleRate":16000,"encoding":"pcm_s16le","channels":1}""",
            """{"type":"hello","version":"speech.v1","ticket":"aaaaaaaaaaaaaaaaaaaaaaaa","sampleRate":16000,"encoding":"pcm_s16le","channels":1,"extra":true}""",
            """{"type":"hello","version":"speech.v1","ticket":"short","sampleRate":16000,"encoding":"pcm_s16le","channels":1}""",
            "not json",
        })
        {
            await using var c = await Rt.AudioClient.ConnectAsync(F, Rt.CookieOf(a.Client), id);
            await c.HelloAsync("x", raw: bad);
            Assert.Equal(1007, await c.WaitCloseAsync());
        }
    }

    // ---------------- frames and controls ----------------

    [Fact]
    public async Task Oversize_odd_and_short_frames_and_bad_controls_close_the_socket()
    {
        var (a, _, id) = await LiveAsync();
        async Task<int?> Expect(Func<Rt.AudioClient, Task> act)
        {
            await using var c = await OpenAsync(a, id);
            await act(c);
            return await c.WaitCloseAsync();
        }
        Assert.Equal(1009, await Expect(c => c.SendBinaryAsync(Rt.AudioClient.Frame(0, ms: 251)))); // more than 250 ms
        Assert.Equal(1007, await Expect(c => c.SendBinaryAsync(new byte[12 + 3])));                 // odd PCM length
        Assert.Equal(1007, await Expect(c => c.SendBinaryAsync(new byte[5])));                      // shorter than the header
        Assert.Equal(1008, await Expect(c => c.SendTextAsync("""{"type":"resume"}""")));           // resume is REST only (C-15)
        Assert.Equal(1008, await Expect(c => c.SendTextAsync("""{"type":"pause","x":1}""")));
        Assert.Equal(1009, await Expect(c => c.SendTextAsync("{\"type\":\"pause\",\"pad\":\"" + new string('a', 4200) + "\"}")));
    }

    [Fact]
    public async Task A_pause_control_only_ends_the_stream_and_REST_stays_authoritative()
    {
        var (a, _, id) = await LiveAsync();
        await using var c = await OpenAsync(a, id);
        await c.SendAudioAsync(2);
        await c.SendTextAsync("""{"type":"pause"}""");
        Assert.Equal(1000, await c.WaitCloseAsync());
        Assert.Equal("Active", await F.TextAsync($"select state from lectures.lecture_sessions where id = '{id}'"));
    }

    [Fact]
    public async Task A_second_socket_for_the_same_session_gets_4409()
    {
        var (a, _, id) = await LiveAsync();
        await using var first = await OpenAsync(a, id);
        await first.SendAudioAsync(1);
        Assert.True(await Rt.WaitAsync(() => first.FramesOf("ack").Count == 1));
        await using var second = await OpenAsync(a, id);
        Assert.Equal(4409, await second.WaitCloseAsync());
        Assert.False(await Task.WhenAny(first.WaitCloseAsync(300)) is { IsCompleted: true, Result: not null } && false);
    }

    // ---------------- cutoff on pause / stop / delete / auth loss ----------------

    [Fact]
    public async Task Pause_cuts_the_stream_discards_later_frames_and_clears_partials_and_resume_needs_a_new_ticket()
    {
        var (a, _, id) = await LiveAsync();
        await using var hub = await Rt.HubProbe.StartAsync(F, Rt.CookieOf(a.Client));
        await hub.JoinAsync(id);
        await using var audio = await OpenAsync(a, id);
        await audio.SendAudioAsync(14); // 3.5 s: segment 1 final, segment 2 in progress (partial)
        Assert.True(await Rt.WaitAsync(() => hub.Of("transcript.partial").Any(e => e.GetProperty("payload").GetProperty("segmentId").GetString() == "seg_2")));
        var before = await EventCountAsync(id);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/pause")).StatusCode);
        Assert.Equal(4409, await audio.WaitCloseAsync());
        try { await audio.SendAudioAsync(40); } catch (Exception) { /* socket already closed */ }
        await Task.Delay(300);
        // Only the session.state(Paused) event was added after the pause; no transcript event past the cutoff.
        Assert.Equal(before, await EventCountAsync(id));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}' and kind = 'Final' and segment_no >= 2"));

        // Partials are cleared on pause: a fresh join shows none.
        await using var late = await Rt.HubProbe.StartAsync(F, Rt.CookieOf(a.Client));
        await late.JoinAsync(id, "0");
        await Task.Delay(150);
        Assert.Empty(late.Of("transcript.partial"));
        Assert.Equal("Paused", late.Of("session.state").Last().GetProperty("payload").GetProperty("state").GetString());

        // The old ticket is gone; resume then a new ticket continues the timeline from the accepted audio so far.
        await PostAsync(a.Client, $"/sessions/{id}/resume");
        await using var again = await OpenAsync(a, id);
        await again.SendAudioAsync(32); // 8 s more
        Assert.True(await Rt.WaitAsync(async () => await F.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}' and kind = 'Final'") >= 2));
        var starts = (await F.TextAsync($"select string_agg(start_ms::text || ':' || segment_no::text, ',' order by seq) from transcript.transcript_events where session_id = '{id}' and kind = 'Final'"))!;
        Assert.StartsWith("0:1,", starts);
        Assert.Equal(starts.Split(',').Length, starts.Split(',').Select(s => s.Split(':')[1]).Distinct().Count()); // segment numbers never repeat across resume
        Assert.True(await F.ScalarAsync($"select min(start_ms) from transcript.transcript_events where session_id = '{id}' and kind = 'Final' and seq > (select max(seq) from transcript.transcript_events where session_id = '{id}' and segment_no = 1 and kind = 'Final')") >= 3500);
    }

    [Fact]
    public async Task Stop_closes_the_socket_and_no_event_is_written_afterwards()
    {
        var (a, _, id) = await LiveAsync();
        await using var audio = await OpenAsync(a, id);
        await audio.SendAudioAsync(4);
        await PostAsync(a.Client, $"/sessions/{id}/stop");
        Assert.Equal(4409, await audio.WaitCloseAsync());
        var n = await EventCountAsync(id);
        try { await audio.SendAudioAsync(60); } catch (Exception) { }
        await Task.Delay(300);
        Assert.Equal(n, await EventCountAsync(id));
    }

    [Fact]
    public async Task Deleting_a_streaming_session_closes_the_socket_and_leaves_no_transcript_or_progress_rows()
    {
        for (var round = 0; round < 4; round++) // repeated: delete racing in-flight writes must never orphan rows (B1-12)
        {
            var (a, _, id) = await LiveAsync();
            await using var audio = await OpenAsync(a, id);
            var sender = Task.Run(async () => { try { await audio.SendAudioAsync(60); } catch (Exception) { } });
            Assert.True(await Rt.WaitAsync(() => audio.FramesOf("ack").Count >= 2));
            Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/v1/sessions/{id}")).StatusCode);
            Assert.Equal(4409, await audio.WaitCloseAsync());
            await sender;
            await Task.Delay(200);
            Assert.Equal(0, await F.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}'"));
            Assert.Equal(0, await F.ScalarAsync($"select count(*) from transcript.audio_progress where session_id = '{id}'"));
        }
    }

    [Fact]
    public async Task Purging_the_course_closes_the_audio_socket_via_the_watchdog()
    {
        var (a, course, id) = await LiveAsync();
        await using var audio = await OpenAsync(a, id);
        await audio.SendAudioAsync(2);
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/v1/courses/{course}?purge=true")).StatusCode);
        Assert.Equal(4409, await audio.WaitCloseAsync());
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}'"));
    }

    [Fact]
    public async Task Losing_the_login_session_closes_the_audio_socket_with_4401()
    {
        var (a, _, id) = await LiveAsync();
        await using var audio = await OpenAsync(a, id);
        await audio.SendAudioAsync(2);
        await F.ExecAsync($"update identity.sessions set revoked_at = now() where user_id = '{a.UserId}'");
        Assert.Equal(4401, await audio.WaitCloseAsync());
    }

    // ---------------- ingest limits ----------------

    [Fact]
    public async Task Frames_over_the_real_time_budget_are_dropped_with_a_rate_limited_error_and_the_socket_is_eventually_closed()
    {
        var limited = new BudgetFactory();
        await ((IAsyncLifetime)limited).InitializeAsync();
        try
        {
            var a = await limited.LoginAsync($"u{Guid.NewGuid():N}@dev.local");
            var id = (await StartOkAsync(a, await CourseAsync(a))).GetProperty("id").GetString()!;
            var ticket = (await (await PostAsync(a, $"/sessions/{id}/audio-ticket")).BodyAsync()).GetProperty("ticket").GetString()!;
            await using var audio = await Rt.AudioClient.ConnectAsync(limited, Rt.CookieOf(a), id);
            await audio.HelloAsync(ticket);
            try { await audio.SendAudioAsync(400); } catch (Exception) { /* closed by the server */ }
            Assert.Equal(1008, await audio.WaitCloseAsync());
            Assert.Contains(audio.FramesOf("error"), e => e.GetProperty("code").GetString() == "rate_limited");
            foreach (var err in audio.FramesOf("error")) ContractSchemas.AssertValid("speech.error", err);
            Assert.True(audio.FramesOf("ack").Count < 400);
        }
        finally { await ((IAsyncLifetime)limited).DisposeAsync(); }
    }

    private sealed class BudgetFactory : ApiFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
            [new("Realtime:RateBurstSeconds", "1"), new("Lectures:LifecycleJobSeconds", "3600"), new("RateLimiting:AudioTicketPermitPerMinute", "100")];
    }

    // ---------------- control frames ----------------

    [Fact]
    public async Task Silence_for_the_configured_period_records_a_network_gap_without_inventing_a_duration()
    {
        var quiet = new SilenceFactory();
        await ((IAsyncLifetime)quiet).InitializeAsync();
        try
        {
            var a = await quiet.LoginAsync($"u{Guid.NewGuid():N}@dev.local");
            var id = (await StartOkAsync(a, await CourseAsync(a))).GetProperty("id").GetString()!;
            var ticket = (await (await PostAsync(a, $"/sessions/{id}/audio-ticket")).BodyAsync()).GetProperty("ticket").GetString()!;
            await using var audio = await Rt.AudioClient.ConnectAsync(quiet, Rt.CookieOf(a), id);
            await audio.HelloAsync(ticket);
            await audio.SendAudioAsync(2);
            Assert.True(await Rt.WaitAsync(async () => await quiet.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}' and kind = 'Gap' and gap_reason = 'network'") == 1, 8000));
            Assert.Equal(0, await quiet.ScalarAsync($"select to_ms - from_ms from transcript.transcript_events where session_id = '{id}' and kind = 'Gap'"));
            await Task.Delay(1500);
            Assert.Equal(1, await quiet.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}' and kind = 'Gap'")); // once per silence episode
        }
        finally { await ((IAsyncLifetime)quiet).DisposeAsync(); }
    }

    private sealed class SilenceFactory : RealtimeFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Extra => [new("Realtime:SilenceGapSeconds", "1")];
    }
}
