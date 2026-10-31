using System.Net;
using Tether.Ai.CatchUp;
using Tether.Ai.Provider;
using Tether.Ai.Summary;
using Tether.Ai.Validation;
using Tether.IntegrationTests.Infrastructure;
using Tether.SharedKernel;
using Tether.Transcript.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.IntegrationTests;

/// <summary>
/// B3-06 (Product Owner decision): no transcript text goes to an AI vendor once the student has paused, stopped or deleted. The mock
/// provider here BLOCKS (it answers nothing until cancelled), so a test can tell "a call is in flight" from "no call was made".
/// One catch-up worker, so a second job really waits in the queue behind the first. The summary timer is parked; tests tick by hand.
/// </summary>
public sealed class BlockingAiFactory : ApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
    [
        new("Ai:Mock:Fault", "Timeout"), new("Ai:CallTimeoutSeconds", "120"), new("Ai:CatchUp:TimeoutSeconds", "120"),
        new("Ai:CatchUp:StaleSeconds", "3600"), new("Ai:CatchUp:Workers", "1"), new("Ai:Summary:TickSeconds", "3600"),
    ];
}

public class AiSessionCutoffTests(BlockingAiFactory factory) : NotesTestBase(factory), IClassFixture<BlockingAiFactory>
{
    private SummaryScheduler Scheduler => F.Services.GetRequiredService<SummaryScheduler>();

    public static TheoryData<string> Actions => new() { "pause", "stop", "delete" };

    private static Task<HttpResponseMessage> ActAsync(HttpClient c, string id, string action) =>
        action == "delete" ? c.DeleteAsync($"/api/v1/sessions/{id}") : PostAsync(c, $"/sessions/{id}/{action}");

    private static void AssertActed(HttpResponseMessage r, string action) =>
        Assert.Equal(action == "delete" ? HttpStatusCode.NoContent : HttpStatusCode.OK, r.StatusCode);

    private async Task WaitForCallsAsync(int calls)
    {
        for (var i = 0; i < 200 && Mock.Calls < calls; i++) await Task.Delay(25);
        Assert.True(Mock.Calls >= calls, $"expected {calls} provider call(s), saw {Mock.Calls}");
    }

    private async Task AssertNothingHeldOrSpentAsync(Guid userId)
    {
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{userId}' and outcome = 'reserved'"));
        Assert.Equal(0, await F.ScalarAsync($"select coalesce(sum(est_cost_micros),0) from ai.ai_usage where user_id = '{userId}'"));
        Assert.Equal(0, await F.ScalarAsync("select coalesce(sum(spent_micros),0) from ai.global_spend")); // the hold was given back to the global counter too
    }

    private void FeedScheduler(Guid userId, string sessionId, int finals = 8)
    {
        for (var n = 1; n <= finals; n++) Scheduler.OnFinalPersisted(userId, Guid.Parse(sessionId), n);
    }

    // ---------- rolling summary ----------

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task An_in_flight_summary_call_is_cancelled_when_the_student_pauses_stops_or_deletes(string action)
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId, 8);
        FeedScheduler(a.UserId, id);
        var before = Mock.Calls;

        var tick = Scheduler.TickAsync(default);
        await WaitForCallsAsync(before + 1); // the vendor call is in flight, blocked

        AssertActed(await ActAsync(a.Client, id, action), action);
        await tick.WaitAsync(TimeSpan.FromSeconds(10)); // returns because the call was cancelled, not because it timed out (120 s)

        Assert.Equal(before + 1, Mock.Calls); // no repair attempt, no retry
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.summary_revisions where session_id = '{id}'"));
        await AssertNothingHeldOrSpentAsync(a.UserId);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task No_summary_call_is_made_after_pause_stop_or_delete_even_if_segments_are_pending(string action)
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId, 8);
        FeedScheduler(a.UserId, id);
        AssertActed(await ActAsync(a.Client, id, action), action);
        var before = Mock.Calls;

        // Pause used to request a flush and Stop a final pass; neither may reach the provider now (B3-06). PO-18d keeps that: a Final summary
        // after Stop is made only by the deliberate "Make notes" press (AiNotesTests), never by Stop itself.
        await Scheduler.TickAsync(default);

        Assert.Equal(before, Mock.Calls);
        await AssertNothingHeldOrSpentAsync(a.UserId);
    }

    [Fact]
    public async Task A_resumed_session_summarises_again()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId, 8);
        FeedScheduler(a.UserId, id);
        AssertActed(await ActAsync(a.Client, id, "pause"), "pause");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/resume")).StatusCode);
        var before = Mock.Calls;

        var tick = Scheduler.TickAsync(default);
        await WaitForCallsAsync(before + 1);
        AssertActed(await ActAsync(a.Client, id, "stop"), "stop");
        await tick.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // ---------- N-01: no zombie scheduler state after Stop or Delete ----------

    [Theory]
    [InlineData("stop")]
    [InlineData("delete")]
    public async Task A_final_committed_just_before_stop_or_delete_cannot_recreate_scheduler_state_when_its_observer_runs_late(string action)
    {
        var (a, id) = await LiveSessionAsync();
        var sid = Guid.Parse(id);
        AssertActed(await ActAsync(a.Client, id, action), action); // the observer has already run and found no state to remove

        for (var n = 1; n <= 8; n++) Scheduler.OnFinalPersisted(a.UserId, sid, n); // the final's own observer call arrives afterwards

        Assert.Equal(0, Scheduler.PendingFor(sid));
        Assert.False(Scheduler.IsTracked(sid));
        var before = Mock.Calls;
        await Scheduler.TickAsync(default);
        Assert.Equal(before, Mock.Calls);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}'")); // not even a reserve and settle pair
    }

    [Fact]
    public async Task A_tracked_session_that_is_found_closed_is_dropped_and_makes_no_reservation_on_later_ticks()
    {
        var (a, id) = await LiveSessionAsync();
        var sid = Guid.Parse(id);
        await SeedFinalsAsync(id, a.UserId, 8);
        FeedScheduler(a.UserId, id);
        Assert.True(Scheduler.IsTracked(sid));
        // Stopped behind the observers' back (the race the review describes): no notification reaches the scheduler.
        await F.ExecAsync($"update lectures.lecture_sessions set state = 'Stopped', stopped_at = now() where id = '{id}'");
        var before = Mock.Calls;

        await Scheduler.TickAsync(default);
        await Scheduler.TickAsync(default);

        Assert.False(Scheduler.IsTracked(sid));
        Assert.Equal(before, Mock.Calls);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}'"));
    }

    [Fact]
    public async Task A_paused_session_stays_tracked_so_a_resume_still_summarises()
    {
        var (a, id) = await LiveSessionAsync();
        var sid = Guid.Parse(id);
        await SeedFinalsAsync(id, a.UserId, 8);
        FeedScheduler(a.UserId, id);
        AssertActed(await ActAsync(a.Client, id, "pause"), "pause");
        await Scheduler.TickAsync(default);
        Assert.True(Scheduler.IsTracked(sid));
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/resume")).StatusCode);
        Assert.True(Scheduler.IsTracked(sid));
        AssertActed(await ActAsync(a.Client, id, "stop"), "stop");
    }

    // ---------- the gateway re-check (every provider call, for every workflow) ----------

    private async Task<GatewayOutcome<CatchUpResult>> GatewayAsync(Guid userId, string sessionId)
    {
        await using var scope = F.Services.GetRequiredService<IServiceScopeFactory>().CreateUserScope(userId);
        var opts = F.Services.GetRequiredService<AiOptions>();
        var sid = Guid.Parse(sessionId);
        var seg = new FinalSegment(sid, 1, 1, 0, 1500, "Mitosis has four phases.", null);
        var built = CatchUpRequests.Build(userId, sid, Guid.NewGuid(), 5, [seg], opts.CatchUp);
        return await scope.ServiceProvider.GetRequiredService<AiGateway>().RunAsync(built.Request, j => CatchUpValidator.Validate(j, built.Context), default);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task The_gateway_makes_no_call_and_spends_nothing_for_a_session_that_is_not_active(string action)
    {
        var (a, id) = await LiveSessionAsync();
        AssertActed(await ActAsync(a.Client, id, action), action);
        var before = Mock.Calls;

        var outcome = await GatewayAsync(a.UserId, id);

        Assert.Equal(AiFailure.SessionClosed, outcome.Failure);
        Assert.Equal(before, Mock.Calls);
        await AssertNothingHeldOrSpentAsync(a.UserId);
    }

    [Fact]
    public async Task The_gateway_makes_no_call_for_a_session_that_belongs_to_someone_else()
    {
        var (alice, id) = await LiveSessionAsync();
        var bob = await NewActorAsync();
        var before = Mock.Calls;

        var outcome = await GatewayAsync(bob.UserId, id);

        Assert.Equal(AiFailure.SessionClosed, outcome.Failure);
        Assert.Equal(before, Mock.Calls);
        await AssertNothingHeldOrSpentAsync(bob.UserId);
        Assert.Equal(HttpStatusCode.OK, (await alice.Client.GetAsync($"/api/v1/sessions/{id}")).StatusCode);
    }

    // ---------- Quick recap ----------

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task An_in_flight_catch_up_call_is_cancelled_and_a_queued_one_never_calls_the_provider(string action)
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        var before = Mock.Calls;

        var first = await (await QuickRecapAsync(a.Client, id, new { windowMinutes = 2 })).BodyAsync();
        await WaitForCallsAsync(before + 1); // the single worker is now blocked inside the provider
        var second = await (await QuickRecapAsync(a.Client, id, new { windowMinutes = 5 })).BodyAsync(); // waits in the queue
        Assert.Equal("Pending", second.GetProperty("aiState").GetString());

        AssertActed(await ActAsync(a.Client, id, action), action);
        if (action != "delete")
        {
            await WaitNotPendingAsync(Guid.Parse(first.GetProperty("catchUpId").GetString()!["cu_".Length..]));
            await WaitNotPendingAsync(Guid.Parse(second.GetProperty("catchUpId").GetString()!["cu_".Length..]));
        }
        else
        {
            for (var i = 0; i < 200 && await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and outcome = 'reserved'") > 0; i++) await Task.Delay(25);
        }
        await Task.Delay(300); // give the worker time to (wrongly) dequeue and call for the second job

        Assert.Equal(before + 1, Mock.Calls); // only the first, cancelled call ever reached the provider
        if (action != "delete")
        {
            // The student keeps the extractive text; the AI version is declared unavailable so no client waits for it.
            Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.catch_ups where session_id = '{id}' and ai_state = 'Pending'"));
            Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.catch_ups where session_id = '{id}' and kind = 'Ai'"));
        }
        else
        {
            Assert.Equal(0, await CatchUpCountAsync(id));
        }
        await AssertNothingHeldOrSpentAsync(a.UserId);
    }

    [Fact]
    public async Task Quick_recap_on_a_paused_session_returns_the_extractive_answer_and_never_asks_the_provider()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        AssertActed(await ActAsync(a.Client, id, "pause"), "pause");
        var before = Mock.Calls;

        var res = await QuickRecapAsync(a.Client, id);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.BodyAsync();
        Assert.Equal("Extractive", body.GetProperty("kind").GetString());
        Assert.Equal("Unavailable", body.GetProperty("aiState").GetString());
        await Task.Delay(300);
        Assert.Equal(before, Mock.Calls);
        await AssertNothingHeldOrSpentAsync(a.UserId);
    }

    // ---------- PO-18d: Delete cuts off an in-flight "Make notes" call ----------

    [Fact]
    public async Task Deleting_mid_job_cancels_the_in_flight_call_and_leaves_no_notes_summary_or_hold()
    {
        var (a, id) = await StoppedSessionAsync();
        var before = Mock.Calls;
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(a.Client, id)).StatusCode);
        for (var i = 0; i < 200 && Mock.Calls < before + 1; i++) await Task.Delay(25);
        Assert.Equal(before + 1, Mock.Calls); // the notes call is in flight, blocked

        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/v1/sessions/{id}")).StatusCode);
        for (var i = 0; i < 200 && await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and outcome = 'reserved'") > 0; i++) await Task.Delay(25);
        await Task.Delay(300); // give the job time to (wrongly) make the final-summary call or store something

        Assert.Equal(before + 1, Mock.Calls); // no final-summary call, no repair
        Assert.Equal(0, await NotesRowsAsync(id));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.summary_revisions where session_id = '{id}'"));
        await AssertNothingHeldAsync(a.UserId);
        Assert.Equal(0, await F.ScalarAsync($"select coalesce(sum(est_cost_micros),0) from ai.ai_usage where user_id = '{a.UserId}'"));
        Assert.Equal(1, await PressAuditsAsync(a.UserId)); // the content-free audit of the press survives, like session.delete
    }

    [Fact]
    public async Task Purging_the_course_of_a_stopped_session_cancels_its_in_flight_notes_call_and_removes_everything()
    {
        var (a, id) = await StoppedSessionAsync();
        var course = await F.TextAsync($"select course_id from lectures.lecture_sessions where id = '{id}'");
        var before = Mock.Calls;
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(a.Client, id)).StatusCode);
        for (var i = 0; i < 200 && Mock.Calls < before + 1; i++) await Task.Delay(25);
        Assert.Equal(before + 1, Mock.Calls);

        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/v1/courses/{course}?purge=true")).StatusCode);
        for (var i = 0; i < 200 && await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and outcome = 'reserved'") > 0; i++) await Task.Delay(25);
        await Task.Delay(300);

        Assert.Equal(before + 1, Mock.Calls); // cancelled by the purge notification, not by the 30 s call timeout; no final-summary call
        Assert.Equal(0, await NotesRowsAsync(id));
        await AssertNothingHeldAsync(a.UserId);
    }
}
