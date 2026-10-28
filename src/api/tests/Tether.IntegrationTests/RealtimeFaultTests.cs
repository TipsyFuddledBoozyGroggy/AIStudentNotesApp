using System.Net;
using Tether.IntegrationTests.Infrastructure;

namespace Tether.IntegrationTests;

/// <summary>Fault injection in the deterministic demo provider (drop, delay, error) and the relay's own rate and backpressure handling.</summary>
public class RealtimeFaultTests
{
    private abstract class Fixture(IEnumerable<KeyValuePair<string, string?>> extra) : RealtimeFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Extra => extra;
    }

    private sealed class DropFactory() : Fixture([new("Speech:Replay:Faults:DropFromMs", "3000"), new("Speech:Replay:Faults:DropToMs", "6000")]);
    private sealed class ErrorFactory() : Fixture([new("Speech:Replay:Faults:ErrorAtMs", "3000"), new("Speech:Replay:Faults:ErrorDurationMs", "3000")]);
    private sealed class SlowFactory() : Fixture([new("Speech:Replay:Faults:DelayMs", "300"), new("Realtime:ChannelFrames", "2"), new("Realtime:BackpressureMs", "50")]);
    private sealed class BurstFactory() : Fixture([new("Realtime:EventsPerSecond", "1")]);

    private static async Task<(HttpClient A, string Id, Rt.AudioClient Audio)> StartStreamAsync(ApiFactory f)
    {
        var a = await f.LoginAsync($"u{Guid.NewGuid():N}@dev.local");
        var course = (await (await a.PostAsJsonAsyncCompat("/api/v1/courses", new { name = "Bio" })).BodyAsync()).GetProperty("id").GetString()!;
        var session = await (await a.PostAsJsonAsyncCompat($"/api/v1/courses/{course}/sessions",
            new { title = "t", consent = new { acknowledged = true, textVersion = Tether.Lectures.Application.ConsentTextRegistry.DraftV1 } })).BodyAsync();
        var id = session.GetProperty("id").GetString()!;
        var ticket = (await (await a.PostAsync($"/api/v1/sessions/{id}/audio-ticket", null)).BodyAsync()).GetProperty("ticket").GetString()!;
        var audio = await Rt.AudioClient.ConnectAsync(f, Rt.CookieOf(a), id);
        await audio.HelloAsync(ticket);
        return (a, id, audio);
    }

    [Fact]
    public async Task A_dropped_audio_range_becomes_one_provider_gap_and_the_lost_segment_is_never_fabricated()
    {
        var f = new DropFactory();
        await ((IAsyncLifetime)f).InitializeAsync();
        try
        {
            var (a, id, audio) = await StartStreamAsync(f);
            await using var _ = audio;
            await audio.SendAudioAsync(48);
            Assert.True(await Rt.WaitAsync(async () => await f.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}' and kind = 'Final'") == 3));

            var page = await (await a.GetAsync($"/api/v1/sessions/{id}/transcript?limit=500")).BodyAsync();
            ContractSchemas.AssertValid("transcript-page", page);
            var gaps = page.GetProperty("events").EnumerateArray().Where(e => e.GetProperty("type").GetString() == "gap").ToList();
            var gap = Assert.Single(gaps).GetProperty("payload");
            Assert.Equal(3000, gap.GetProperty("fromMs").GetInt64());
            Assert.Equal(6000, gap.GetProperty("toMs").GetInt64());
            Assert.Equal("provider", gap.GetProperty("reason").GetString());
            Assert.Equal(["seg_1", "seg_3", "seg_4"], page.GetProperty("events").EnumerateArray()
                .Where(e => e.GetProperty("type").GetString() == "transcript.final").Select(e => e.GetProperty("payload").GetProperty("segmentId").GetString()!).ToArray());
            Assert.Equal(0, await f.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}' and kind = 'Correction'")); // seg_2 (and its correction) never existed
        }
        finally { await ((IAsyncLifetime)f).DisposeAsync(); }
    }

    [Fact]
    public async Task A_provider_error_surfaces_as_Down_then_Ok_with_a_gap_and_a_socket_error_while_the_session_continues()
    {
        var f = new ErrorFactory();
        await ((IAsyncLifetime)f).InitializeAsync();
        try
        {
            var (a, id, audio) = await StartStreamAsync(f);
            await using var _ = audio;
            await using var hub = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(a));
            await hub.JoinAsync(id);
            await audio.SendAudioAsync(48);

            // The initial Ok may be published before this hub joined (it is ephemeral and not replayed), so assert the Down -> Ok tail.
            Assert.True(await Rt.WaitAsync(() => hub.Of("stt.status").Count >= 2 && hub.Of("stt.status").Last().GetProperty("payload").GetProperty("state").GetString() == "Ok"));
            Assert.Equal(["Down", "Ok"], hub.Of("stt.status").Select(e => e.GetProperty("payload").GetProperty("state").GetString()!).TakeLast(2).ToArray());
            foreach (var e in hub.Of("stt.status")) ContractSchemas.AssertValid("stt.status", e);
            Assert.Contains(audio.FramesOf("error"), e => e.GetProperty("code").GetString() == "provider_unavailable");
            Assert.True(await Rt.WaitAsync(async () => await f.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}' and kind = 'Gap' and gap_reason = 'provider' and from_ms = 3000 and to_ms = 6000") == 1));
            Assert.True(await Rt.WaitAsync(async () => await f.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}' and kind = 'Final'") == 3));
            Assert.Equal("Active", await f.TextAsync($"select state from lectures.lecture_sessions where id = '{id}'")); // an error does not end the session
        }
        finally { await ((IAsyncLifetime)f).DisposeAsync(); }
    }

    [Fact]
    public async Task A_slow_provider_applies_backpressure_then_drops_frames_and_records_a_gap_never_blocking_forever()
    {
        var f = new SlowFactory();
        await ((IAsyncLifetime)f).InitializeAsync();
        try
        {
            var (a, id, audio) = await StartStreamAsync(f);
            await using var _ = audio;
            await audio.SendAudioAsync(60);
            Assert.True(await Rt.WaitAsync(async () => await f.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}' and kind = 'Gap' and gap_reason = 'provider'") >= 1, 15000),
                "overflowing the bounded channel must be reported as a gap");
            var page = await (await a.GetAsync($"/api/v1/sessions/{id}/transcript?limit=500")).BodyAsync();
            ContractSchemas.AssertValid("transcript-page", page);
            foreach (var g in page.GetProperty("events").EnumerateArray().Where(e => e.GetProperty("type").GetString() == "gap"))
                Assert.True(g.GetProperty("payload").GetProperty("toMs").GetInt64() >= g.GetProperty("payload").GetProperty("fromMs").GetInt64());
            Assert.True(audio.FramesOf("ack").Count < 60, "dropped frames are not acknowledged");
        }
        finally { await ((IAsyncLifetime)f).DisposeAsync(); }
    }

    [Fact]
    public async Task The_persisted_event_rate_is_capped_and_the_excess_is_reported_as_one_gap_instead_of_silently_lost()
    {
        var f = new BurstFactory();
        await ((IAsyncLifetime)f).InitializeAsync();
        try
        {
            var (a, id, audio) = await StartStreamAsync(f);
            await using var _ = audio;
            await audio.SendAudioAsync(48); // every final lands inside one second: only one fits the 1 event/s cap
            Assert.True(await Rt.WaitAsync(async () => await f.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}' and kind = 'Gap' and gap_reason = 'provider'") >= 1, 10000));
            Assert.True(await f.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}' and kind = 'Final'") < 4);
            // seq stays gap-free whatever was dropped.
            Assert.Equal(0, await f.ScalarAsync($"""
                select count(*) from (
                  select seq from transcript.transcript_events where session_id = '{id}'
                  union all select seq from lectures.session_events where session_id = '{id}') s
                having count(*) <> max(seq)
                """) );
        }
        finally { await ((IAsyncLifetime)f).DisposeAsync(); }
    }
}

internal static class HttpClientCompat
{
    public static Task<HttpResponseMessage> PostAsJsonAsyncCompat(this HttpClient c, string path, object body) =>
        System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(c, path, body);
}
