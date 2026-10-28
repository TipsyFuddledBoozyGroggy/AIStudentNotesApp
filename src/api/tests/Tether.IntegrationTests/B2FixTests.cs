using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Tether.IntegrationTests.Infrastructure;
using Tether.Lectures.Contracts;
using Tether.Transcript.Application;
using Tether.Transcript.Hub;
using Tether.Transcript.Relay;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;

namespace Tether.IntegrationTests;

/// <summary>
/// Regression tests for docs/security/m2-b2-review.md (B2-01 to B2-09). All audio is synthetic zero PCM against the
/// deterministic replay provider; no vendor, microphone or real lecture is involved.
/// </summary>
public class B2PushTests(B2PushTests.Factory f) : LectureTestBase(f), IClassFixture<B2PushTests.Factory>
{
    /// <summary>Polling intervals are set so large that only a push can close a connection within the test timeout.</summary>
    public sealed class Factory : RealtimeFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Extra =>
            [new("Realtime:RevalidateSeconds", "3600"), new("Realtime:RelayCheckSeconds", "3600"), new("Realtime:MaxConnectionSeconds", "7200")];
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

    private async Task<Rt.AudioClient> OpenAsync(Actor a, string sessionId)
    {
        var client = await Rt.AudioClient.ConnectAsync(F, Rt.CookieOf(a.Client), sessionId);
        await client.HelloAsync(await TicketAsync(a.Client, sessionId));
        return client;
    }

    // ---------------- B2-03: logout pushes ----------------

    [Fact]
    public async Task Logout_closes_the_audio_socket_and_the_hub_connection_by_push_in_well_under_a_second()
    {
        var (a, _, id) = await LiveAsync();
        await using var audio = await OpenAsync(a, id);
        await using var hub = await Rt.HubProbe.StartAsync(F, Rt.CookieOf(a.Client));
        await hub.JoinAsync(id);
        await audio.SendAudioAsync(2);
        Assert.True(await Rt.WaitAsync(() => audio.FramesOf("ack").Count >= 2));

        var sw = Stopwatch.StartNew();
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        var code = await audio.WaitCloseAsync(3000);
        var audioMs = sw.ElapsedMilliseconds;
        var hubClosed = await Task.WhenAny(hub.Closed.Task, Task.Delay(3000)) == hub.Closed.Task;
        var hubMs = sw.ElapsedMilliseconds;

        Assert.Equal(4401, code);
        Assert.True(audioMs < 1000, $"audio socket took {audioMs} ms to close after logout");
        Assert.True(hubClosed, "hub connection must be closed by logout");
        Assert.True(hubMs < 1000, $"hub connection took {hubMs} ms to close after logout");
    }

    [Fact]
    public async Task Logging_in_again_in_the_same_browser_also_pushes_the_old_login_sessions_connections_closed()
    {
        var (a, _, id) = await LiveAsync();
        await using var hub = await Rt.HubProbe.StartAsync(F, Rt.CookieOf(a.Client));
        await hub.JoinAsync(id);
        // N-03: a new login that carries the old cookie revokes the old login session.
        var res = await a.Client.PostAsync("/api/v1/dev/login", JsonContentOf(new { email = a.Email }));
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.True(await Task.WhenAny(hub.Closed.Task, Task.Delay(1500)) == hub.Closed.Task);
    }

    private static HttpContent JsonContentOf(object o) => System.Net.Http.Json.JsonContent.Create(o);

    // ---------------- B2-04: consume vs pause race ----------------

    [Fact]
    public async Task A_pause_that_commits_between_ticket_consume_and_relay_registration_still_stops_the_stream()
    {
        var (a, _, id) = await LiveAsync();
        var registry = F.Services.GetRequiredService<RelayRegistry>();
        registry.AfterTicketConsumed = async _ =>
        {
            registry.AfterTicketConsumed = null;
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/pause")).StatusCode);
        };
        try
        {
            await using var audio = await OpenAsync(a, id);
            Assert.Equal(4409, await audio.WaitCloseAsync(3000)); // refused before the provider stream opened
            try { await audio.SendAudioAsync(4); } catch (Exception) { /* already closed */ }
            Assert.Empty(audio.FramesOf("ack"));
        }
        finally { registry.AfterTicketConsumed = null; }
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}' and kind = 'Final'"));
    }

    // ---------------- B2-05: fast reconnect ----------------

    [Fact]
    public async Task A_fast_reconnect_after_the_previous_socket_ended_is_not_refused_with_4409()
    {
        var (a, _, id) = await LiveAsync();
        var first = await OpenAsync(a, id);
        await first.SendAudioAsync(4);
        Assert.True(await Rt.WaitAsync(() => first.FramesOf("ack").Count >= 4));
        await first.SendTextAsync("""{"type":"pause"}"""); // client ends this stream; the session stays Active
        Assert.Equal(1000, await first.WaitCloseAsync(3000));
        // By the time the client has seen the close, the old relay must already be gone from the registry (it used to stay
        // registered until its workers drained, so a quick reconnect hit "already has an audio socket" = 4409).
        Assert.False(F.Services.GetRequiredService<RelayRegistry>().Contains(Guid.Parse(id)), "old relay still registered after its close");

        await using var second = await OpenAsync(a, id); // immediately, no waiting for drain
        await second.SendAudioAsync(2);
        Assert.True(await Rt.WaitAsync(() => second.FramesOf("ack").Count >= 2, 3000), "reconnect must be accepted");
        await first.DisposeAsync();
    }

    // ---------------- B2-07 / B2-06: archive and purge ----------------

    [Fact]
    public async Task Archiving_a_course_stops_its_live_session_and_closes_the_socket_by_push()
    {
        var (a, course, id) = await LiveAsync();
        await using var audio = await OpenAsync(a, id);
        await using var hub = await Rt.HubProbe.StartAsync(F, Rt.CookieOf(a.Client));
        await hub.JoinAsync(id);
        await audio.SendAudioAsync(2);

        var sw = Stopwatch.StartNew();
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/v1/courses/{course}")).StatusCode);
        Assert.Equal(4409, await audio.WaitCloseAsync(3000));
        Assert.True(sw.ElapsedMilliseconds < 1500, $"took {sw.ElapsedMilliseconds} ms");

        Assert.Equal("Stopped", await F.TextAsync($"select state from lectures.lecture_sessions where id = '{id}'"));
        Assert.Equal(1, await CountAsync("consent_events", $"session_id = '{id}' and kind = 'Stop'"));
        Assert.Equal("course_archived", await F.TextAsync($"select reason from lectures.session_events where session_id = '{id}' order by seq desc limit 1"));
        Assert.True(await Rt.WaitAsync(() => hub.Of("session.state").Any(e => e.GetProperty("payload").GetProperty("state").GetString() == "Stopped"), 2000));
        // a second archive request is harmless
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/v1/courses/{course}")).StatusCode);
        Assert.Equal(1, await CountAsync("consent_events", $"session_id = '{id}' and kind = 'Stop'"));
    }

    [Fact]
    public async Task Purging_a_course_writes_a_Stop_consent_event_and_a_delete_audit_and_closes_both_connections_by_push()
    {
        var (a, course, id) = await LiveAsync();
        await using var audio = await OpenAsync(a, id);
        await using var hub = await Rt.HubProbe.StartAsync(F, Rt.CookieOf(a.Client));
        await hub.JoinAsync(id);
        await audio.SendAudioAsync(2);

        var sw = Stopwatch.StartNew();
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/v1/courses/{course}?purge=true")).StatusCode);
        Assert.Equal(4409, await audio.WaitCloseAsync(3000));
        Assert.True(await Task.WhenAny(hub.Closed.Task, Task.Delay(3000)) == hub.Closed.Task);
        Assert.True(sw.ElapsedMilliseconds < 1500, $"took {sw.ElapsedMilliseconds} ms");

        Assert.Equal(0, await CountAsync("lecture_sessions", $"id = '{id}'"));
        Assert.Equal(1, await CountAsync("consent_events", $"session_id = '{id}' and kind = 'Start'"));
        Assert.Equal(1, await CountAsync("consent_events", $"session_id = '{id}' and kind = 'Stop'"));
        Assert.Equal(1, await CountAsync("audit_events", $"session_id = '{id}' and action = 'session.stop'"));
        Assert.Equal(1, await CountAsync("audit_events", $"session_id = '{id}' and action = 'session.delete'"));
    }
}

/// <summary>B2-02: hub invocation limits across session ids and per connection, with a low per-user Join budget.</summary>
public class B2HubLimitTests(B2HubLimitTests.Factory f) : LectureTestBase(f), IClassFixture<B2HubLimitTests.Factory>
{
    public sealed class Factory : RealtimeFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Extra =>
            [new("Realtime:JoinPermitPerUserPerMinute", "5"), new("Realtime:HubInvocationsPerMinute", "20")];
    }

    private static async Task<string?> JoinErrorAsync(Rt.HubProbe hub, string sessionId)
    {
        try { await hub.JoinAsync(sessionId); return null; }
        catch (HubException ex) { return ex.Message; }
    }

    [Fact]
    public async Task Joins_with_many_distinct_session_ids_are_limited_per_user_before_any_database_lookup()
    {
        var a = await NewActorAsync();
        await using var hub = await Rt.HubProbe.StartAsync(F, Rt.CookieOf(a.Client));
        var errors = new List<string?>();
        for (var i = 0; i < 12; i++) errors.Add(await JoinErrorAsync(hub, Guid.NewGuid().ToString()));
        Assert.Equal(5, errors.Count(e => e!.EndsWith("not_found")));
        Assert.Equal(7, errors.Count(e => e!.EndsWith("rate_limited")));

        // the budget is per user, not per connection: a second connection does not get a fresh one
        await using var hub2 = await Rt.HubProbe.StartAsync(F, Rt.CookieOf(a.Client));
        Assert.EndsWith("rate_limited", (await JoinErrorAsync(hub2, Guid.NewGuid().ToString()))!);
    }

    [Fact]
    public async Task Any_hub_invocation_flood_on_one_connection_is_limited()
    {
        var a = await NewActorAsync();
        await using var hub = await Rt.HubProbe.StartAsync(F, Rt.CookieOf(a.Client));
        var limited = 0; var other = new List<string>();
        for (var i = 0; i < 60; i++)
        {
            try { await hub.Conn.InvokeAsync("Ping"); }
            catch (HubException ex) when (ex.Message.EndsWith("rate_limited")) { limited++; }
            catch (Exception ex) { other.Add(ex.GetType().Name + ":" + ex.Message); }
        }
        Assert.True(other.Count == 0, string.Join("; ", other.Take(3)));
        Assert.True(limited >= 30, $"expected the flood to be limited, got {limited} rejections; permit={F.Services.GetRequiredService<RealtimeOptions>().HubInvocationsPerMinute} tracked={F.Services.GetRequiredService<HubInvocationLimiter>().TrackedKeys}");
    }
}

/// <summary>B2-02: limiter dictionaries must not grow without bound.</summary>
public class B2LimiterMemoryTests
{
    private static RealtimeOptions Options(params (string, string)[] extra) =>
        RealtimeOptions.From(new ConfigurationBuilder().AddInMemoryCollection(extra.Select(e => new KeyValuePair<string, string?>(e.Item1, e.Item2))).Build());

    [Fact]
    public void JoinLimiter_prunes_idle_keys_and_caps_the_number_of_tracked_keys()
    {
        var clock = new TestClock();
        var limiter = new JoinLimiter(Options(("Realtime:JoinPermitPerMinute", "1000"), ("Realtime:JoinPermitPerUserPerMinute", "100000")), clock);
        var user = Guid.NewGuid();
        for (var i = 0; i < 25000; i++) limiter.TryAcquire(user, Guid.NewGuid());
        Assert.True(limiter.TrackedKeys <= JoinLimiter.MaxTrackedKeys, $"tracked {limiter.TrackedKeys}");

        clock.Advance(TimeSpan.FromMinutes(3));
        limiter.TryAcquire(Guid.NewGuid(), Guid.NewGuid()); // any later call sweeps
        Assert.True(limiter.TrackedKeys <= 4, $"idle keys must be evicted, tracked {limiter.TrackedKeys}");
    }

    [Fact]
    public void FailedHelloLimiter_prunes_idle_keys_and_caps_the_number_of_tracked_keys()
    {
        var clock = new TestClock();
        var limiter = new FailedHelloLimiter(Options(), clock);
        for (var i = 0; i < 25000; i++) limiter.Record(Guid.NewGuid(), $"10.{i / 256}.{i % 256}.1");
        Assert.True(limiter.TrackedKeys <= FailedHelloLimiter.MaxTrackedKeys, $"tracked {limiter.TrackedKeys}");

        clock.Advance(TimeSpan.FromMinutes(3));
        limiter.IsBlocked(Guid.NewGuid(), "10.9.9.9");
        limiter.Record(Guid.NewGuid(), "10.9.9.9");
        Assert.True(limiter.TrackedKeys <= 4, $"idle keys must be evicted, tracked {limiter.TrackedKeys}");
    }

    [Fact]
    public void FailedHelloLimiter_treats_all_addresses_of_one_IPv6_64_as_one_client()
    {
        var limiter = new FailedHelloLimiter(Options(("Realtime:FailedHelloPerMinute", "2")), new TestClock()); // ip budget = 10
        for (var i = 1; i <= 10; i++) limiter.Record(Guid.NewGuid(), $"2001:db8:1:2::{i:x}");
        Assert.True(limiter.IsBlocked(Guid.NewGuid(), "2001:db8:1:2:ffff:ffff:ffff:ffff"));
        Assert.False(limiter.IsBlocked(Guid.NewGuid(), "2001:db8:1:3::1")); // a different /64 is a different client
    }
}

/// <summary>B2-08: demo-only settings must not start outside Development or Test.</summary>
public class B2StartupGuardTests
{
    private sealed class Prod(string key, string value, string env = "Production") : ApiFactory
    {
        protected override string EnvironmentName => env;
        protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Auth:DevLogin:Enabled", "false"), new(key, value)];
    }

    [Theory]
    [InlineData("Speech:Replay:Faults:DelayMs", "50", "Production")]
    [InlineData("Speech:Replay:Faults:ErrorAtMs", "1000", "Production")]
    [InlineData("Speech:Replay:Faults:DropFromMs", "0", "Staging")]
    [InlineData("Speech:Replay:Script", """[{"startMs":0,"endMs":1000,"text":"x"}]""", "Production")]
    [InlineData("Speech:Provider", "deepgram", "Production")]
    public async Task Startup_fails_when_demo_faults_script_or_a_non_demo_provider_are_configured_outside_development(string key, string value, string env)
    {
        var f = new Prod(key, value, env);
        await ((IAsyncLifetime)f).InitializeAsync();
        try
        {
            var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
            var chain = new List<string>();
            for (Exception? e = ex; e is not null; e = e.InnerException) chain.Add(e.Message);
            Assert.Contains(chain, m => m.Contains("Speech:", StringComparison.Ordinal));
        }
        finally { await ((IAsyncLifetime)f).DisposeAsync(); }
    }
}

/// <summary>B2-09: demoSource is stored on the session when it is created.</summary>
public class B2DemoSourcePersistenceTests(B2DemoSourcePersistenceTests.Factory f) : LectureTestBase(f), IClassFixture<B2DemoSourcePersistenceTests.Factory>
{
    public sealed class Toggle : IDemoSourceIndicator { public bool IsDemoSource { get; set; } = true; }

    public sealed class Factory : ApiFactory
    {
        public Toggle Indicator { get; } = new();
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(s =>
            {
                s.RemoveAll<IDemoSourceIndicator>();
                s.AddSingleton<IDemoSourceIndicator>(Indicator);
            });
        }
    }

    [Fact]
    public async Task A_session_keeps_the_demoSource_it_was_created_with_when_the_provider_changes_later()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        f.Indicator.IsDemoSource = true;
        var demoId = (await StartOkAsync(a.Client, course)).GetProperty("id").GetString()!;
        await PostAsync(a.Client, $"/sessions/{demoId}/stop");
        Assert.Equal("true", await F.TextAsync($"select demo_source::text from lectures.lecture_sessions where id = '{demoId}'"));

        f.Indicator.IsDemoSource = false; // pretend a real provider was configured afterwards
        try
        {
            var old = await (await a.Client.GetAsync($"/api/v1/sessions/{demoId}")).BodyAsync();
            Assert.True(old.GetProperty("demoSource").GetBoolean(), "an old demo session must still say demo");
            var list = await (await a.Client.GetAsync($"/api/v1/courses/{course}/sessions")).BodyAsync();
            Assert.True(list.GetProperty("items")[0].GetProperty("demoSource").GetBoolean());

            var fresh = await StartOkAsync(a.Client, course);
            Assert.False(fresh.GetProperty("demoSource").GetBoolean());
            ContractSchemas.AssertValid("session", fresh);
        }
        finally { f.Indicator.IsDemoSource = true; }
    }
}
