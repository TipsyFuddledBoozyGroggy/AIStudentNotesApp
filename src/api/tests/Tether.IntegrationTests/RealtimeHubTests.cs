using System.Net;
using System.Text.Json;
using Tether.IntegrationTests.Infrastructure;
using Tether.Lectures.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.IntegrationTests;

/// <summary>Hub /hubs/lecture, protocol lecture.v1: Origin, auth, Join/resume, isolation, limits and connection lifetime.</summary>
public class RealtimeHubTests(RealtimeHubTests.Factory f) : LectureTestBase(f), IClassFixture<RealtimeHubTests.Factory>
{
    public sealed class Factory : RealtimeFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Extra =>
            [new("Realtime:ReplayCap", "4"), new("Realtime:MaxConnectionSeconds", "3600")];
    }

    private async Task<(Actor A, string Course, string Session)> LiveAsync()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var s = await StartOkAsync(a.Client, course);
        return (a, course, s.GetProperty("id").GetString()!);
    }

    // ---------------- Origin and authentication (F-04, ADR-017, C-07) ----------------

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("http://evil.example")]
    [InlineData("HTTP://LOCALHOST:3000")]      // exact ordinal match, no case folding
    [InlineData("http://localhost:3000/")]     // an Origin header never carries a path
    public async Task Negotiate_with_a_missing_null_or_foreign_Origin_is_403_before_anything_else(string? origin)
    {
        var a = await NewActorAsync();
        var c = f.NewClient(withOrigin: false);
        var req = new HttpRequestMessage(HttpMethod.Post, "/hubs/lecture/negotiate?negotiateVersion=1");
        req.Headers.TryAddWithoutValidation("Cookie", Rt.CookieOf(a.Client));
        if (origin is not null) req.Headers.TryAddWithoutValidation("Origin", origin);
        await (await c.SendAsync(req)).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("http://evil.example")]
    public async Task The_WebSocket_upgrade_GET_needs_an_allowed_Origin_too(string? origin)
    {
        var a = await NewActorAsync();
        var res = await Rt.UpgradeGetAsync(f, "/hubs/lecture?id=abc", origin, Rt.CookieOf(a.Client));
        await res.AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        var ex = await Assert.ThrowsAnyAsync<Exception>(async () => { await using var _ = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(a.Client), origin); });
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task Negotiate_without_a_session_cookie_is_401_and_with_one_succeeds_offering_only_WebSockets()
    {
        var c = f.NewClient();
        await (await c.PostAsync("/hubs/lecture/negotiate?negotiateVersion=1", null)).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");

        var a = await NewActorAsync();
        var res = await a.Client.PostAsync("/hubs/lecture/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.BodyAsync();
        var transports = body.GetProperty("availableTransports").EnumerateArray().Select(t => t.GetProperty("transport").GetString()).ToList();
        Assert.Equal(["WebSockets"], transports); // C-07: no long polling, no SSE
    }

    [Fact]
    public async Task A_hub_connection_without_a_cookie_cannot_start()
    {
        await Assert.ThrowsAnyAsync<Exception>(async () => { await using var _ = await Rt.HubProbe.StartAsync(f, ""); });
    }

    // ---------------- Join, replay, validation ----------------

    [Fact]
    public async Task Join_returns_a_contract_valid_response_and_replays_persisted_events_as_valid_hub_events()
    {
        var (a, _, id) = await LiveAsync();
        await PostAsync(a.Client, $"/sessions/{id}/pause");
        await PostAsync(a.Client, $"/sessions/{id}/resume");
        await using var hub = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(a.Client));

        var res = await hub.JoinAsync(id, "0");
        ContractSchemas.AssertValid("join-response", res);
        Assert.Equal("Active", res.GetProperty("state").GetString());
        Assert.Equal("3", res.GetProperty("currentSeq").GetString());
        Assert.Equal(JsonValueKind.Null, res.GetProperty("currentSummaryRevision").ValueKind);
        Assert.Equal("1", res.GetProperty("replay").GetProperty("fromSeq").GetString());
        Assert.Equal("3", res.GetProperty("replay").GetProperty("toSeq").GetString());
        Assert.False(res.GetProperty("replay").GetProperty("truncated").GetBoolean());

        Assert.True(await Rt.WaitAsync(() => hub.Of("session.state").Count >= 3));
        foreach (var e in hub.Snapshot()) ContractSchemas.AssertValid("hub-event", e);
        Assert.Equal(["1", "2", "3"], hub.Of("session.state").Select(e => e.GetProperty("seq").GetString()!).ToArray());
        Assert.Equal(["Active", "Paused", "Active"], hub.Of("session.state").Select(e => e.GetProperty("payload").GetProperty("state").GetString()!).ToArray());
        Assert.All(hub.Of("session.state"), e => Assert.Equal(id, e.GetProperty("sessionId").GetString()));
    }

    [Fact]
    public async Task Join_without_lastSeq_replays_from_the_start_and_with_lastSeq_only_newer_events()
    {
        var (a, _, id) = await LiveAsync();
        await PostAsync(a.Client, $"/sessions/{id}/pause");
        await using var hub = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(a.Client));
        var res = await hub.JoinAsync(id, "1");
        Assert.Equal("2", res.GetProperty("replay").GetProperty("fromSeq").GetString());
        Assert.Equal("2", res.GetProperty("replay").GetProperty("toSeq").GetString());
        Assert.True(await Rt.WaitAsync(() => hub.Snapshot().Count == 1));

        var again = await hub.JoinAsync(id, "2");
        Assert.Equal(JsonValueKind.Null, again.GetProperty("replay").GetProperty("fromSeq").ValueKind);
        Assert.Equal(JsonValueKind.Null, again.GetProperty("replay").GetProperty("toSeq").ValueKind);
        ContractSchemas.AssertValid("join-response", again);
    }

    [Fact]
    public async Task An_out_of_range_lastSeq_returns_current_state_with_an_empty_replay()
    {
        var (a, _, id) = await LiveAsync();
        await using var hub = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(a.Client));
        var res = await hub.JoinAsync(id, "999999");
        ContractSchemas.AssertValid("join-response", res);
        Assert.Equal("1", res.GetProperty("currentSeq").GetString());
        Assert.Equal(JsonValueKind.Null, res.GetProperty("replay").GetProperty("fromSeq").ValueKind);
        Assert.False(res.GetProperty("replay").GetProperty("truncated").GetBoolean());
        await Task.Delay(100);
        Assert.Empty(hub.Snapshot());
    }

    [Fact]
    public async Task A_backlog_over_the_replay_cap_is_truncated_and_nothing_is_replayed_so_the_client_pages_REST()
    {
        var (a, _, id) = await LiveAsync();
        for (var i = 0; i < 4; i++) { await PostAsync(a.Client, $"/sessions/{id}/pause"); await PostAsync(a.Client, $"/sessions/{id}/resume"); } // seq 1..9; cap is 4
        await using var hub = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(a.Client));
        var res = await hub.JoinAsync(id, "0");
        ContractSchemas.AssertValid("join-response", res);
        Assert.True(res.GetProperty("replay").GetProperty("truncated").GetBoolean());
        Assert.Equal("9", res.GetProperty("currentSeq").GetString());
        await Task.Delay(150);
        Assert.Empty(hub.Snapshot());

        var page = await (await a.Client.GetAsync($"/api/v1/sessions/{id}/transcript?afterSeq=0&limit=500")).BodyAsync();
        Assert.Equal(9, page.GetProperty("events").GetArrayLength()); // the REST fallback has all of it
    }

    [Theory]
    [InlineData("""{"sessionId":"{ID}","protocol":"lecture.v2"}""")]
    [InlineData("""{"sessionId":"{ID}"}""")]
    [InlineData("""{"protocol":"lecture.v1"}""")]
    [InlineData("""{"sessionId":"{ID}","protocol":"lecture.v1","extra":1}""")]
    [InlineData("""{"sessionId":"{ID}","protocol":"lecture.v1","lastSeq":5}""")]
    [InlineData("""{"sessionId":"{ID}","protocol":"lecture.v1","lastSeq":"-1"}""")]
    [InlineData("""{"sessionId":"{ID}","protocol":"lecture.v1","lastSeq":"007"}""")]
    [InlineData("""{"sessionId":"{ID}","protocol":"lecture.v1","lastSummaryRevision":-1}""")]
    [InlineData("""{"sessionId":"not-a-guid","protocol":"lecture.v1"}""")]
    [InlineData("""["x"]""")]
    public async Task Join_rejects_anything_that_is_not_a_valid_join_request(string json)
    {
        var (a, _, id) = await LiveAsync();
        await using var hub = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(a.Client));
        var arg = JsonDocument.Parse(json.Replace("{ID}", id)).RootElement;
        var ex = await Assert.ThrowsAsync<HubException>(() => hub.Conn.InvokeAsync<JsonElement>("Join", arg));
        Assert.Contains("validation_failed", ex.Message);
    }

    [Fact]
    public async Task Joining_a_foreign_session_fails_exactly_like_joining_a_missing_one()
    {
        var (a, _, aSession) = await LiveAsync();
        var (b, _, _) = await LiveAsync();
        await using var hubB = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(b.Client));
        var foreign = await Assert.ThrowsAsync<HubException>(() => hubB.JoinAsync(aSession));
        var missing = await Assert.ThrowsAsync<HubException>(() => hubB.JoinAsync(Guid.NewGuid().ToString()));
        Assert.Contains("not_found", foreign.Message);
        Assert.Equal(missing.Message, foreign.Message);
        Assert.Empty(hubB.Snapshot());
    }

    // ---------------- isolation ----------------

    [Fact]
    public async Task Two_users_with_overlapping_seq_never_see_each_others_events()
    {
        var (a, _, aId) = await LiveAsync();
        var (b, _, bId) = await LiveAsync();
        await using var hubA = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(a.Client));
        await using var hubB = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(b.Client));
        await hubA.JoinAsync(aId); await hubB.JoinAsync(bId);
        Assert.True(await Rt.WaitAsync(() => hubA.Snapshot().Count >= 1 && hubB.Snapshot().Count >= 1));
        var aBefore = hubA.Snapshot().Count; var bBefore = hubB.Snapshot().Count;

        await PostAsync(a.Client, $"/sessions/{aId}/pause");
        Assert.True(await Rt.WaitAsync(() => hubA.Snapshot().Count == aBefore + 1));
        await Task.Delay(150);
        Assert.Equal(bBefore, hubB.Snapshot().Count); // seq 2 exists for both users; only A's reaches A
        Assert.All(hubA.Snapshot(), e => Assert.Equal(aId, e.GetProperty("sessionId").GetString()));
        Assert.All(hubB.Snapshot(), e => Assert.Equal(bId, e.GetProperty("sessionId").GetString()));

        // And a user cannot read the other's transcript over REST either.
        await (await b.Client.GetAsync($"/api/v1/sessions/{aId}/transcript")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Live_session_state_changes_reach_a_joined_client_as_valid_persisted_events_and_Leave_stops_them()
    {
        var (a, _, id) = await LiveAsync();
        await using var hub = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(a.Client));
        await hub.JoinAsync(id);
        await PostAsync(a.Client, $"/sessions/{id}/pause");
        Assert.True(await Rt.WaitAsync(() => hub.Of("session.state").Any(e => e.GetProperty("seq").GetString() == "2")));
        var paused = hub.Of("session.state").Single(e => e.GetProperty("seq").GetString() == "2");
        ContractSchemas.AssertValid("hub-event", paused);
        Assert.Equal("Paused", paused.GetProperty("payload").GetProperty("state").GetString());
        Assert.Equal("user", paused.GetProperty("payload").GetProperty("reason").GetString());

        await hub.Conn.InvokeAsync("Leave", new { sessionId = id });
        var n = hub.Snapshot().Count;
        await PostAsync(a.Client, $"/sessions/{id}/resume");
        await Task.Delay(200);
        Assert.Equal(n, hub.Snapshot().Count);
    }

    // ---------------- limits ----------------

    [Fact]
    public async Task Join_is_limited_per_user_and_session_per_minute()
    {
        var limited = new LimitedFactory();
        await ((IAsyncLifetime)limited).InitializeAsync();
        try
        {
            var a = await limited.LoginAsync($"u{Guid.NewGuid():N}@dev.local");
            var course = await CourseAsync(a);
            var id = (await StartOkAsync(a, course)).GetProperty("id").GetString()!;
            await using var hub = await Rt.HubProbe.StartAsync(limited, Rt.CookieOf(a));
            for (var i = 0; i < 10; i++) await hub.JoinAsync(id);
            var ex = await Assert.ThrowsAsync<HubException>(() => hub.JoinAsync(id));
            Assert.Contains("rate_limited", ex.Message);
        }
        finally { await ((IAsyncLifetime)limited).DisposeAsync(); }
    }

    private sealed class LimitedFactory : ApiFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
            [new("Realtime:RevalidateSeconds", "1"), new("Lectures:LifecycleJobSeconds", "3600")]; // default Join limit: 10 per minute
    }

    [Fact]
    public async Task At_most_three_concurrent_hub_connections_per_user()
    {
        var a = await NewActorAsync();
        var cookie = Rt.CookieOf(a.Client);
        await using var c1 = await Rt.HubProbe.StartAsync(f, cookie);
        await using var c2 = await Rt.HubProbe.StartAsync(f, cookie);
        await using var c3 = await Rt.HubProbe.StartAsync(f, cookie);
        await using var c4 = await Rt.HubProbe.StartAsync(f, cookie); // accepted by the transport, then aborted by the server
        Assert.True(await Task.WhenAny(c4.Closed.Task, Task.Delay(5000)) == c4.Closed.Task, "the fourth connection must be closed");
        Assert.False(c1.Closed.Task.IsCompleted);
        Assert.False(c2.Closed.Task.IsCompleted);
        Assert.False(c3.Closed.Task.IsCompleted);
    }

    [Fact]
    public async Task A_message_over_4_KB_closes_the_connection()
    {
        var (a, _, id) = await LiveAsync();
        await using var hub = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(a.Client));
        try { await hub.Conn.InvokeAsync("Ping", new string('x', 6000)); } catch (Exception) { /* connection torn down */ }
        Assert.True(await Task.WhenAny(hub.Closed.Task, Task.Delay(5000)) == hub.Closed.Task);
        _ = id;
    }

    // ---------------- connection lifetime (C-06) ----------------

    [Fact]
    public async Task Revoking_the_login_session_drops_the_hub_connection_within_the_revalidation_interval()
    {
        var (a, _, id) = await LiveAsync();
        await using var hub = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(a.Client));
        await hub.JoinAsync(id);
        await F.ExecAsync($"update identity.sessions set revoked_at = now() where user_id = '{a.UserId}'");
        Assert.True(await Task.WhenAny(hub.Closed.Task, Task.Delay(8000)) == hub.Closed.Task, "logout must drop the connection (C-06)");
    }

    [Fact]
    public async Task A_hub_connection_is_dropped_after_its_maximum_lifetime()
    {
        var (a, _, id) = await LiveAsync();
        await using var hub = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(a.Client));
        await hub.JoinAsync(id);
        F.Clock.Advance(TimeSpan.FromMinutes(61));
        Assert.True(await Task.WhenAny(hub.Closed.Task, Task.Delay(8000)) == hub.Closed.Task);
    }

    [Fact]
    public async Task Deleting_the_session_closes_hub_connections_that_joined_it_and_other_users_connections_stay()
    {
        var (a, _, id) = await LiveAsync();
        var (b, _, bId) = await LiveAsync();
        await using var hubA = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(a.Client));
        await using var hubB = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(b.Client));
        await hubA.JoinAsync(id); await hubB.JoinAsync(bId);
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/v1/sessions/{id}")).StatusCode);
        Assert.True(await Task.WhenAny(hubA.Closed.Task, Task.Delay(5000)) == hubA.Closed.Task);
        Assert.False(hubB.Closed.Task.IsCompleted);
    }

    [Fact]
    public async Task Purging_the_course_closes_the_hub_connection_via_revalidation_because_the_FK_cascade_removed_the_session()
    {
        var (a, course, id) = await LiveAsync();
        await using var hub = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(a.Client));
        await hub.JoinAsync(id);
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/v1/courses/{course}?purge=true")).StatusCode);
        Assert.True(await Task.WhenAny(hub.Closed.Task, Task.Delay(8000)) == hub.Closed.Task);
    }

    [Fact]
    public async Task Hub_Ping_counts_as_a_heartbeat_for_the_idle_auto_pause_job()
    {
        var (a, _, id) = await LiveAsync();
        await using var hub = await Rt.HubProbe.StartAsync(f, Rt.CookieOf(a.Client));
        await hub.JoinAsync(id);
        F.Clock.Advance(TimeSpan.FromMinutes(4));
        await hub.Conn.InvokeAsync("Ping");
        F.Clock.Advance(TimeSpan.FromMinutes(4));
        using var scope = F.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Tether.Lectures.Application.SessionLifecycleJob>().RunOnceAsync(default);
        Assert.Equal("Active", await F.TextAsync($"select state from lectures.lecture_sessions where id = '{id}'"));
    }

    [Fact]
    public async Task Sessions_report_the_Demo_source_while_the_replay_provider_is_active_and_validate_against_the_schema()
    {
        var (a, _, id) = await LiveAsync();
        var body = await (await a.Client.GetAsync($"/api/v1/sessions/{id}")).BodyAsync();
        ContractSchemas.AssertValid("session", body);
        Assert.True(body.GetProperty("demoSource").GetBoolean());
        Assert.True(F.Services.GetRequiredService<IDemoSourceIndicator>().IsDemoSource);
    }
}
