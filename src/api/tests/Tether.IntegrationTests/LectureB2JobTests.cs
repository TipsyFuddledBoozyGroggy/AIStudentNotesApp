using System.Net;
using Tether.IntegrationTests.Infrastructure;
using Tether.Lectures.Application;
using Tether.Lectures.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.IntegrationTests;

/// <summary>
/// Unattended-session limits (C-17, hard cap 3 h). Timings are the contract defaults and UNCONFIRMED Product Owner decisions.
/// The job is driven directly with a controllable clock; the hosted service only calls the same method on a timer.
/// </summary>
public class LectureB2JobTests(ApiFactory factory) : LectureTestBase(factory), IClassFixture<ApiFactory>
{
    private async Task<int> RunJobAsync()
    {
        using var scope = F.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SessionLifecycleJob>().RunOnceAsync(default);
    }

    private async Task<(Actor A, string Id)> LiveAsync()
    {
        var a = await NewActorAsync();
        var s = await StartOkAsync(a.Client, await CourseAsync(a.Client));
        return (a, s.GetProperty("id").GetString()!);
    }

    private Task<string?> StateAsync(string id) => F.TextAsync($"select state from lectures.lecture_sessions where id = '{id}'");

    [Fact]
    public async Task An_Active_session_with_no_activity_for_five_minutes_is_auto_paused_with_reason_events_and_no_ticket()
    {
        var (a, id) = await LiveAsync();
        await PostAsync(a.Client, $"/sessions/{id}/audio-ticket");
        F.Clock.Advance(TimeSpan.FromMinutes(4));
        await RunJobAsync();
        Assert.Equal("Active", await StateAsync(id));

        F.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(await RunJobAsync() >= 1);
        Assert.Equal("Paused", await StateAsync(id));
        Assert.Equal("auto_pause_no_audio", await F.TextAsync($"select reason from lectures.session_events where session_id = '{id}' and seq = 2"));
        Assert.Equal(1, await CountAsync("consent_events", $"session_id = '{id}' and kind = 'Pause'"));
        Assert.Equal(1, await CountAsync("audit_events", $"session_id = '{id}' and action = 'session.pause'"));
        Assert.Equal(0, await CountAsync("audio_tickets", $"session_id = '{id}'"));
        Assert.Equal(2, await F.ScalarAsync($"select last_seq from lectures.session_counters where session_id = '{id}'"));
    }

    [Fact]
    public async Task Hub_or_audio_activity_keeps_an_Active_session_from_auto_pausing()
    {
        var (_, id) = await LiveAsync();
        F.Clock.Advance(TimeSpan.FromMinutes(4));
        F.Services.GetRequiredService<ISessionActivity>().Touch(Guid.Parse(id));
        F.Clock.Advance(TimeSpan.FromMinutes(4));
        await RunJobAsync();
        Assert.Equal("Active", await StateAsync(id));
        F.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(await RunJobAsync() >= 1);
        Assert.Equal("Paused", await StateAsync(id));
    }

    [Fact]
    public async Task A_Paused_session_is_auto_stopped_after_thirty_minutes_with_reason()
    {
        var (a, id) = await LiveAsync();
        await PostAsync(a.Client, $"/sessions/{id}/pause");
        F.Clock.Advance(TimeSpan.FromMinutes(29));
        await RunJobAsync();
        Assert.Equal("Paused", await StateAsync(id));
        F.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(await RunJobAsync() >= 1);
        Assert.Equal("Stopped", await StateAsync(id));
        Assert.Equal("auto_stop_paused_timeout", await F.TextAsync($"select reason from lectures.session_events where session_id = '{id}' order by seq desc limit 1"));
        Assert.Equal(1, await CountAsync("consent_events", $"session_id = '{id}' and kind = 'Stop'"));
    }

    [Fact]
    public async Task The_three_hour_hard_cap_stops_a_session_even_if_it_is_active_and_busy()
    {
        var (_, id) = await LiveAsync();
        for (var i = 0; i < 18; i++) // 18 x 10 min, activity every step so only the cap can act
        {
            F.Clock.Advance(TimeSpan.FromMinutes(10));
            F.Services.GetRequiredService<ISessionActivity>().Touch(Guid.Parse(id));
            if (i < 17) { await RunJobAsync(); Assert.Equal("Active", await StateAsync(id)); }
        }
        Assert.True(await RunJobAsync() >= 1);
        Assert.Equal("Stopped", await StateAsync(id));
        Assert.Equal("hard_cap", await F.TextAsync($"select reason from lectures.session_events where session_id = '{id}' order by seq desc limit 1"));
    }

    [Fact]
    public async Task The_job_acts_per_owner_and_leaves_other_users_sessions_alone()
    {
        var (_, idOld) = await LiveAsync();
        F.Clock.Advance(TimeSpan.FromMinutes(6));
        var (_, idNew) = await LiveAsync();
        Assert.True(await RunJobAsync() >= 1);
        Assert.Equal("Paused", await StateAsync(idOld));
        Assert.Equal("Active", await StateAsync(idNew));
    }

    [Fact]
    public async Task Session_state_events_carry_the_user_reason_for_student_commands()
    {
        var (a, id) = await LiveAsync();
        await PostAsync(a.Client, $"/sessions/{id}/pause");
        Assert.Equal("user", await F.TextAsync($"select reason from lectures.session_events where session_id = '{id}' and seq = 2"));
        Assert.Null(await F.TextAsync($"select reason from lectures.session_events where session_id = '{id}' and seq = 1") is { Length: > 0 } r ? r : null);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/stop")).StatusCode);
    }

    [Fact]
    public void The_lifecycle_job_runs_as_a_hosted_service()
    {
        Assert.Contains(F.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), h => h.GetType().Name == "SessionLifecycleService");
    }
}
