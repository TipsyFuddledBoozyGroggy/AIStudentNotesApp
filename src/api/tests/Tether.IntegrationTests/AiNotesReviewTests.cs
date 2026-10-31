using System.Net;
using System.Text.Json;
using Tether.Ai.CatchUp;
using Tether.Ai.Domain;
using Tether.Ai.Notes;
using Tether.Ai.Persistence;
using Tether.Ai.Provider;
using Tether.Ai.Validation;
using Tether.Courses.Contracts;
using Tether.IntegrationTests.Infrastructure;
using Tether.Lectures.Contracts;
using Tether.SharedKernel;
using Tether.Transcript.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tether.IntegrationTests;

/// <summary>Helpers shared by the review-fix test classes (docs/security/notes-after-stop-review.md).</summary>
public abstract class NotesReviewTestBase(ApiFactory factory) : NotesTestBase(factory)
{
    protected async Task<string> StoppedInCourseAsync(Actor a, string courseId, int finals = 3)
    {
        var id = (await StartOkAsync(a.Client, courseId)).GetProperty("id").GetString()!;
        if (finals > 0) await SeedFinalsAsync(id, a.UserId, finals);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/stop")).StatusCode);
        return id;
    }

    protected Task<string?> CourseOfAsync(string sessionId) => F.TextAsync($"select course_id from lectures.lecture_sessions where id = '{sessionId}'");

    protected Task<string?> StateOfPressAsync(Guid pressId) => F.TextAsync($"select state from ai.lecture_notes where id = '{pressId}'");

    private protected async Task<GatewayOutcome<NotesChunkResult>> NotesCallAsync(Guid userId, string sessionId, Guid pressId)
    {
        await using var scope = F.Services.GetRequiredService<IServiceScopeFactory>().CreateUserScope(userId);
        var opts = F.Services.GetRequiredService<AiOptions>();
        var sid = Guid.Parse(sessionId);
        var seg = new FinalSegment(sid, 1, 1, 2000, 3500, "Mitosis has four phases.", null);
        var part = NotesRequests.Build(userId, sid, pressId, [seg], false, opts.Notes).Parts.Single();
        return await scope.ServiceProvider.GetRequiredService<AiGateway>().RunAsync(part.Request, j => NotesValidator.Validate(j, part.Context), default);
    }

    protected Task InsertUsageAsync(Guid userId, string sessionId, string workflow, long micros) =>
        F.ExecAsync("insert into ai.ai_usage (id, user_id, session_id, scope_kind, scope_id, pool, workflow, prompt_version, model, input_tokens, cached_tokens, output_tokens, est_cost_micros, latency_ms, outcome, created_at) " +
                    $"values ('{Guid.NewGuid()}', '{userId}', '{sessionId}', 'lecture', '{sessionId}', 'lecture', '{workflow}', 'x.v1', 'mock-fast', 0, 0, 0, {micros}, 0, 'ok', '{F.Clock.GetUtcNow():O}')");

    protected async Task WaitAsync(Func<Task<bool>> done, string what)
    {
        for (var i = 0; i < 100; i++) { if (await done()) return; await Task.Delay(100); } // gentle: test DB connections are unpooled
        Assert.Fail("timed out waiting for " + what);
    }
}

/// <summary>Instant mock: NAS-02 budget in both directions, NAS-03 one pending press per user, NAS-05/08 audit and limiter, NAS-06 conditional write, NAS-09 and the reopened Final summary.</summary>
public class AiNotesReviewTests(ApiFactory factory) : NotesReviewTestBase(factory), IClassFixture<ApiFactory>
{
    // ---------- NAS-02: notes have their own sub-cap and can never use the live floor; live work is never held to notes limits ----------

    private async Task<GatewayOutcome<CatchUpResult>> LiveCallAsync(Guid userId, string sessionId)
    {
        await using var scope = F.Services.GetRequiredService<IServiceScopeFactory>().CreateUserScope(userId);
        var opts = F.Services.GetRequiredService<AiOptions>();
        var sid = Guid.Parse(sessionId);
        var seg = new FinalSegment(sid, 1, 1, 0, 1500, "Mitosis has four phases.", null);
        var built = CatchUpRequests.Build(userId, sid, Guid.NewGuid(), 5, [seg], opts.CatchUp);
        return await scope.ServiceProvider.GetRequiredService<AiGateway>().RunAsync(built.Request, j => CatchUpValidator.Validate(j, built.Context), default);
    }

    [Fact]
    public async Task Notes_that_reached_their_daily_sub_cap_are_BudgetLimited_while_live_Quick_recap_still_runs()
    {
        var (a, old) = await StoppedSessionAsync();
        var (_, other) = await StoppedSessionAsync(a); // old presses on another lecture spent the whole notes allowance today
        var opts = F.Services.GetRequiredService<AiOptions>();
        await InsertUsageAsync(a.UserId, other, "Notes", opts.Notes.PerUserDailyMicro);

        var before = Mock.Calls;
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(a.Client, old)).StatusCode);
        Assert.Equal("BudgetLimited", (await WaitSettledAsync(a.Client, old)).GetProperty("status").GetString());
        Assert.Equal(before, Mock.Calls); // the gateway refused the first notes call against the sub-cap; nothing reached the provider

        // The other direction: the same user's live lecture is not held to the notes sub-cap.
        var (_, live) = await LiveSessionAsync(a);
        var outcome = await LiveCallAsync(a.UserId, live);
        Assert.True(outcome.Ok, outcome.Failure);
        Assert.Equal(before + 1, Mock.Calls);
    }

    [Fact]
    public async Task Live_spend_inside_the_floor_refuses_notes_but_the_live_lecture_can_still_spend_the_floor()
    {
        var (a, old) = await StoppedSessionAsync();
        var (_, other) = await StoppedSessionAsync(a);
        var opts = F.Services.GetRequiredService<AiOptions>();
        // Live work today already reaches 50,000 micro-USD into the floor reserved for live features.
        var spent = opts.Budget.PerUserDailyMicro - opts.Notes.LiveFloorPerUserDailyMicro + 50_000;
        await InsertUsageAsync(a.UserId, other, "RollingSummary", spent);
        var press = Guid.NewGuid();
        await InsertPressAsync(old, a.UserId, press);
        var before = Mock.Calls;

        var notes = await NotesCallAsync(a.UserId, old, press); // the gateway's own reservation check, not only the advisory one
        Assert.Equal(AiFailure.BudgetLimited, notes.Failure);
        Assert.Equal(before, Mock.Calls);
        await F.ExecAsync($"update ai.lecture_notes set state = 'Unavailable', completed_at = now() where id = '{press}'");

        var (_, live) = await LiveSessionAsync(a);
        var outcome = await LiveCallAsync(a.UserId, live);
        Assert.True(outcome.Ok, outcome.Failure); // live TL;DR and Quick recap can never be starved by old-lecture presses
        Assert.Equal(before + 1, Mock.Calls);
    }

    // ---------- NAS-R03: the whole press must fit what is left, before the first call ----------

    /// <summary>The runner's own per-press estimate for this session's stored finals (every planned call, twice).</summary>
    private async Task<long> PressEstimateAsync(Guid userId, string sessionId)
    {
        await using var scope = F.Services.GetRequiredService<IServiceScopeFactory>().CreateUserScope(userId);
        var sp = scope.ServiceProvider;
        var opts = sp.GetRequiredService<AiOptions>();
        var sid = Guid.Parse(sessionId);
        var finals = await sp.GetRequiredService<ITranscriptSegmentReader>().ReadFinalsAfterAsync(userId, sid, 0, 500, default);
        var press = Guid.NewGuid();
        var plan = NotesRequests.Build(userId, sid, press, finals, false, opts.Notes);
        var latest = await sp.GetRequiredService<Tether.Ai.Summary.SummaryStore>().LatestAsync(sid, default);
        var finalRequests = sp.GetRequiredService<Tether.Ai.Summary.SummaryEngine>().PlanFinalRequests(userId, sid, press, latest, plan.Used);
        return NotesRunner.PressEstimate(opts, plan.Parts.Select(p => p.Request), finalRequests);
    }

    [Fact]
    public async Task A_press_whose_whole_estimate_does_not_fit_the_remaining_notes_allowance_is_BudgetLimited_before_any_call()
    {
        var (a, id) = await StoppedSessionAsync();
        var (_, other) = await StoppedSessionAsync(a);
        var opts = F.Services.GetRequiredService<AiOptions>();
        var estimate = await PressEstimateAsync(a.UserId, id);
        Assert.True(estimate <= opts.Notes.PerRequestMicro); // the per-press cap alone would let it through
        // Earlier presses today leave just under this press's estimate: the first call alone would still fit, the whole press does not.
        await InsertUsageAsync(a.UserId, other, "Notes", opts.Notes.PerUserDailyMicro - (estimate - 1));
        var before = Mock.Calls;

        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(a.Client, id)).StatusCode);
        Assert.Equal("BudgetLimited", (await WaitSettledAsync(a.Client, id)).GetProperty("status").GetString());
        await Task.Delay(200);
        Assert.Equal(before, Mock.Calls); // no partial send: nothing reached the provider
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.ai_usage where scope_id = '{id}' and workflow = 'Notes' and outcome = 'budget_denied'"));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.summary_revisions where session_id = '{id}'"));
        await AssertNothingHeldAsync(a.UserId);
    }

    // ---------- NAS-R07: the database requires the notice and the press id ----------

    [Fact]
    public async Task The_database_refuses_a_press_or_a_notes_requested_audit_row_without_its_notice_or_press_id()
    {
        var (a, id) = await StoppedSessionAsync();
        var noNotice = await Assert.ThrowsAsync<PostgresException>(() => F.ExecAsync(
            "insert into ai.lecture_notes (id, session_id, user_id, revision, state, idempotency_key, requested_at, sections, truncated) " +
            $"values ('{Guid.NewGuid()}', '{id}', '{a.UserId}', 1, 'Unavailable', 'k1', now(), '[]', false)"));
        Assert.Equal(PostgresErrorCodes.NotNullViolation, noNotice.SqlState);

        string Audit(string action, string subject, string notice) =>
            "insert into lectures.audit_events (id, user_id, session_id, action, occurred_at, subject_id, notice_version) " +
            $"values ('{Guid.NewGuid()}', '{a.UserId}', '{id}', '{action}', now(), {subject}, {notice})";
        var noSubject = await Assert.ThrowsAsync<PostgresException>(() => F.ExecAsync(Audit("notes.requested", "null", "'notes-notice-draft-1'")));
        Assert.Equal("ck_audit_events_notes_requested", noSubject.ConstraintName);
        var noNoticeAudit = await Assert.ThrowsAsync<PostgresException>(() => F.ExecAsync(Audit("notes.requested", $"'{Guid.NewGuid()}'", "null")));
        Assert.Equal("ck_audit_events_notes_requested", noNoticeAudit.ConstraintName);
        await F.ExecAsync(Audit("notes.requested", $"'{Guid.NewGuid()}'", "'notes-notice-draft-1'")); // complete: accepted
        await F.ExecAsync(Audit("session.stop", "null", "null"));                                    // other actions are unaffected
    }

    // ---------- NAS-03: at most one pending press per user ----------

    [Fact]
    public async Task A_second_press_while_another_lecture_of_the_same_user_is_pending_is_429_and_creates_audits_and_spends_nothing()
    {
        var (a, first) = await StoppedSessionAsync();
        var (_, second) = await StoppedSessionAsync(a);
        var pending = Guid.NewGuid();
        await InsertPressAsync(first, a.UserId, pending);
        var audits = await PressAuditsAsync(a.UserId);
        var tokens = F.Services.GetRequiredService<NotesLimiter>().Count(a.UserId);

        await (await PressAsync(a.Client, second)).AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.Equal(0, await NotesRowsAsync(second));
        Assert.Equal(audits, await PressAuditsAsync(a.UserId));
        Assert.Equal(tokens, F.Services.GetRequiredService<NotesLimiter>().Count(a.UserId)); // a refused press holds no limiter token

        // A different user is not affected, and once the first press ends this user can press again.
        var (b, theirs) = await StoppedSessionAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(b.Client, theirs)).StatusCode);
        await F.ExecAsync($"update ai.lecture_notes set state = 'Unavailable', completed_at = now() where id = '{pending}'");
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(a.Client, second)).StatusCode);
        Assert.Equal("Ready", (await WaitSettledAsync(a.Client, second)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_database_allows_only_one_pending_press_per_user_even_across_sessions()
    {
        var (a, first) = await StoppedSessionAsync();
        var (_, second) = await StoppedSessionAsync(a);
        await InsertPressAsync(first, a.UserId, Guid.NewGuid());
        var ex = await Assert.ThrowsAsync<PostgresException>(() => InsertPressAsync(second, a.UserId, Guid.NewGuid()));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
        Assert.Equal("ux_lecture_notes_pending_user", ex.ConstraintName);
    }

    // ---------- NAS-06: the final write is conditional ----------

    [Fact]
    public async Task A_ready_result_is_never_written_over_a_press_that_is_no_longer_pending()
    {
        var (a, id) = await StoppedSessionAsync();
        var press = Guid.NewGuid();
        await InsertPressAsync(id, a.UserId, press, state: "Unavailable");
        await using var scope = F.Services.GetRequiredService<IServiceScopeFactory>().CreateUserScope(a.UserId);
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        var row = new LectureNotesRow
        {
            Id = press, SessionId = Guid.Parse(id), UserId = a.UserId, State = NotesStates.Ready, CompletedAt = F.Clock.GetUtcNow(), SectionsJson = "[]",
            CoveredFromMs = 0, CoveredToMs = 1, SourceFromSeq = 1, SourceToSeq = 1, FinalSummaryState = FinalSummaryStates.NotNeeded, Model = "mock-fast", PromptVersion = "lecture-notes.v1",
        };
        Assert.Equal(0, await NotesStore.CompleteReadyAsync(db, row, default));
        Assert.Equal("Unavailable", await StateOfPressAsync(press));
        Assert.Equal(0, await NotesStore.FinishAsync(db, press, a.UserId, NotesStates.BudgetLimited, F.Clock.GetUtcNow(), default));
        Assert.Equal("Unavailable", await StateOfPressAsync(press));
    }

    // ---------- NAS-09 + the reopened Stopped session ----------

    [Fact]
    public async Task The_final_summary_is_pushed_only_to_the_owners_joined_connection_and_a_reopened_stopped_session_reads_it_as_Final()
    {
        var (a, id) = await StoppedSessionAsync(finals: 4);
        var bob = await NewActorAsync();
        await using var owner = await Rt.HubProbe.StartAsync(F, Rt.CookieOf(a.Client));
        await owner.JoinAsync(id); // a student still looking at the stopped lecture
        await using var stranger = await Rt.HubProbe.StartAsync(F, Rt.CookieOf(bob.Client));
        var refused = await Assert.ThrowsAsync<HubException>(() => stranger.JoinAsync(id));
        Assert.EndsWith("not_found", refused.Message);

        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(a.Client, id)).StatusCode);
        var notes = await WaitSettledAsync(a.Client, id);
        var rev = notes.GetProperty("finalSummaryRevision").GetInt32();
        await WaitAsync(() => Task.FromResult(owner.Of("summary.revision").Count > 0), "the owner's summary.revision event");

        var pushed = Assert.Single(owner.Of("summary.revision")).GetProperty("payload");
        Assert.Equal("Final", pushed.GetProperty("status").GetString());
        Assert.Equal(rev, pushed.GetProperty("revision").GetInt32());
        ContractSchemas.AssertValid("summary-revision", pushed);
        Assert.Empty(stranger.Snapshot());

        // Reopened later (a new sign-in, no hub): the latest summary of the stopped session IS the Final one.
        var again = await F.LoginAsync(a.Email);
        Assert.Equal("Stopped", (await (await again.GetAsync($"/api/v1/sessions/{id}")).BodyAsync()).GetProperty("state").GetString());
        var latest = await again.GetAsync($"/api/v1/sessions/{id}/summary");
        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        var body = await latest.BodyAsync();
        ContractSchemas.AssertValid("summary-revision", body);
        Assert.Equal("Final", body.GetProperty("status").GetString());
        Assert.Equal(rev, body.GetProperty("revision").GetInt32());
    }
}

/// <summary>The mock fails every FinalSummary call (and nothing else), so the notes succeed and the final step does not.</summary>
public sealed class FailingFinalSummaryFactory : ApiFactory
{
    private sealed class FailFinal(MockAiClient inner) : IAiClient
    {
        public string ProviderName => inner.ProviderName;
        public bool IsMock => inner.IsMock;
        public Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct) =>
            request.Workflow == AiWorkflow.FinalSummary ? throw new AiClientException(AiFailureKind.Unavailable) : inner.CompleteAsync(request, ct);
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.AddSingleton<IAiClient>(sp => new FailFinal(sp.GetRequiredService<MockAiClient>())));
    }
}

public class AiNotesFinalFailureTests(FailingFinalSummaryFactory factory) : NotesReviewTestBase(factory), IClassFixture<FailingFinalSummaryFactory>
{
    [Fact]
    public async Task A_failed_final_summary_is_reported_with_the_Ready_notes_never_skipped_silently()
    {
        var (a, id) = await StoppedSessionAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(a.Client, id)).StatusCode);
        var notes = await WaitSettledAsync(a.Client, id);
        ContractSchemas.AssertValid("notes", notes);
        Assert.Equal("Ready", notes.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, notes.GetProperty("finalSummaryRevision").ValueKind);
        Assert.Equal("Unavailable", notes.GetProperty("finalSummaryStatus").GetString());
        Assert.Equal("Unavailable", await F.TextAsync($"select final_summary_state from ai.lecture_notes where session_id = '{id}'"));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.summary_revisions where session_id = '{id}' and status = 'Final'"));
        await AssertNothingHeldAsync(a.UserId);
    }
}

/// <summary>
/// NAS-R05: the press expires while its last FinalSummary call is answering. This reproduces the window in which a GET has COMMITTED the
/// expiry but not yet cancelled the job: the press row is set Unavailable from a separate connection after the vendor call and before the
/// job appends the Final revision.
/// </summary>
public sealed class ExpireDuringFinalSummaryFactory : ApiFactory
{
    private sealed class ExpireOnFinal(MockAiClient inner, string connectionString) : IAiClient
    {
        public string ProviderName => inner.ProviderName;
        public bool IsMock => inner.IsMock;
        public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            var response = await inner.CompleteAsync(request, ct);
            if (request.Workflow == AiWorkflow.FinalSummary && request.PressId is { } press)
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync(CancellationToken.None);
                await using var cmd = new NpgsqlCommand($"update ai.lecture_notes set state = 'Unavailable', completed_at = now() where id = '{press}' and state = 'Pending'", conn);
                await cmd.ExecuteNonQueryAsync(CancellationToken.None);
            }
            return response;
        }
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.AddSingleton<IAiClient>(sp => new ExpireOnFinal(sp.GetRequiredService<MockAiClient>(), ConnectionString)));
    }
}

public class AiNotesFinalAfterExpiryTests(ExpireDuringFinalSummaryFactory factory) : NotesReviewTestBase(factory), IClassFixture<ExpireDuringFinalSummaryFactory>
{
    [Fact]
    public async Task A_final_summary_revision_is_never_stored_or_pushed_for_a_press_that_expired_before_the_append()
    {
        var (a, id) = await StoppedSessionAsync(finals: 4);
        await using var owner = await Rt.HubProbe.StartAsync(F, Rt.CookieOf(a.Client));
        await owner.JoinAsync(id);

        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(a.Client, id)).StatusCode);
        Assert.Equal("Unavailable", (await WaitSettledAsync(a.Client, id)).GetProperty("status").GetString());
        await WaitAsync(async () => await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and workflow = 'FinalSummary' and outcome <> 'reserved'") > 0, "the final call to settle");
        await Task.Delay(300);

        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.summary_revisions where session_id = '{id}' and status = 'Final'"));
        Assert.DoesNotContain(owner.Of("summary.revision"), e => e.GetProperty("payload").GetProperty("status").GetString() == "Final");
        await AssertNothingHeldAsync(a.UserId);
    }
}

/// <summary>The press audit throws (a database error in Lectures): 500, nothing created, and the limiter token is given back (NAS-08).</summary>
public sealed class FailingAuditFactory : ApiFactory
{
    private sealed class Boom : ISessionAuditLog
    {
        public Task<bool> RecordNotesRequestedAsync(Guid userId, Guid sessionId, Guid pressId, string noticeVersion, CancellationToken ct = default) =>
            throw new InvalidOperationException("simulated audit failure");
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.AddScoped<ISessionAuditLog, Boom>());
    }
}

public class AiNotesAuditFailureTests(FailingAuditFactory factory) : NotesReviewTestBase(factory), IClassFixture<FailingAuditFactory>
{
    [Fact]
    public async Task A_failed_audit_write_creates_nothing_sends_nothing_and_returns_the_rate_limit_token()
    {
        var (a, id) = await StoppedSessionAsync();
        var before = Mock.Calls;
        var res = await PressAsync(a.Client, id);
        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        Assert.Equal(0, await NotesRowsAsync(id));
        Assert.Equal(0, F.Services.GetRequiredService<NotesLimiter>().Count(a.UserId));
        await Task.Delay(200);
        Assert.Equal(before, Mock.Calls);
    }
}

/// <summary>
/// NAS-01: holds a course purge in the gap between the pre-commit notice and the commit, so a test can act exactly there. The Lectures
/// handler (registered first) has already stopped recording and sent the pre-commit "deleted" notice when this one blocks.
/// </summary>
public sealed class PurgeGap
{
    public bool Armed { get; set; }
    public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Reset() { Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); Release = new(TaskCreationOptions.RunContinuationsAsynchronously); Armed = true; }

    internal sealed class Handler(PurgeGap gap) : ICourseLifecycleHandler
    {
        public Task OnArchivingAsync(Guid userId, Guid courseId, CancellationToken ct = default) => Task.CompletedTask;
        public async Task OnPurgingAsync(Guid userId, Guid courseId, CancellationToken ct = default)
        {
            if (!gap.Armed) return;
            gap.Armed = false;
            gap.Entered.TrySetResult();
            await gap.Release.Task;
        }
        public Task OnPurgedAsync(Guid userId, Guid courseId, CancellationToken ct = default) => Task.CompletedTask;
    }
}

/// <summary>Records every "deleted" notice and whether the session row still existed when it arrived (pre- or post-commit).</summary>
public sealed class DeletionRecorder(string connectionString) : ISessionLifecycleObserver
{
    public List<(Guid SessionId, bool RowExisted)> Deleted { get; } = [];

    public Task OnStateChangedAsync(SessionStateChange change, CancellationToken ct) => Task.CompletedTask;

    public async Task OnDeletedAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);
        await using var cmd = new NpgsqlCommand($"select count(*) from lectures.lecture_sessions where id = '{sessionId}'", conn);
        var exists = Convert.ToInt64(await cmd.ExecuteScalarAsync(CancellationToken.None)) > 0;
        lock (Deleted) Deleted.Add((sessionId, exists));
    }
}

public sealed class PurgeGapFactory : ApiFactory
{
    public PurgeGap Gap { get; } = new();
    public DeletionRecorder Recorder { get; }

    public PurgeGapFactory() => Recorder = new DeletionRecorder(ConnectionString);

    protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
    [
        new("Ai:Mock:Fault", "Timeout"), new("Ai:CallTimeoutSeconds", "120"), new("Ai:CatchUp:TimeoutSeconds", "120"),
        new("Ai:CatchUp:StaleSeconds", "3600"), new("Ai:CatchUp:Workers", "1"), new("Ai:Summary:TickSeconds", "3600"),
    ];

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton(Gap);
            s.AddScoped<ICourseLifecycleHandler, PurgeGap.Handler>(); // after the Lectures handler, so it blocks AFTER the pre-commit notice
            s.AddSingleton<ISessionLifecycleObserver>(Recorder);
        });
    }
}

/// <summary>
/// NAS-01, NAS-03 queue wait and NAS-06 expiry on the BLOCKING mock (a call answers nothing until it is cancelled), so "a call was made"
/// and "no call was made" are unambiguous. One notes worker: a second user's press really waits in the queue.
/// </summary>
public class AiNotesPurgeGapTests(PurgeGapFactory factory) : NotesReviewTestBase(factory), IClassFixture<PurgeGapFactory>
{
    private PurgeGapFactory G => (PurgeGapFactory)F;

    private async Task<Task<HttpResponseMessage>> PurgeHeldInTheGapAsync(Actor a, string courseId)
    {
        G.Gap.Reset();
        var purge = a.Client.DeleteAsync($"/api/v1/courses/{courseId}?purge=true");
        await G.Gap.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); // pre-commit notice sent, delete NOT committed
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from courses.courses where id = '{courseId}'"));
        return purge;
    }

    [Fact]
    public async Task A_notes_job_between_two_calls_cannot_start_its_next_call_in_the_purge_gap_and_a_new_press_there_is_404()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var id = await StoppedInCourseAsync(a, course);
        var spare = await StoppedInCourseAsync(a, course);
        var press = Guid.NewGuid();
        await InsertPressAsync(id, a.UserId, press); // a running job's press, between part 1 and part 2: it holds no lease
        var audits = await PressAuditsAsync(a.UserId);
        var before = Mock.Calls;

        var purge = await PurgeHeldInTheGapAsync(a, course);
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from lectures.lecture_sessions where id = '{id}'")); // still there, Stopped
        Assert.Equal("Pending", await StateOfPressAsync(press));

        // The job's next call: the session exists and the press is Pending, but the scope is tombstoned, so the lease is born cancelled.
        var next = await NotesCallAsync(a.UserId, id, press).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(AiFailure.SessionClosed, next.Failure);
        // A new press arriving in the gap is refused like a missing session: nothing created, audited or sent.
        await (await PressAsync(a.Client, spare)).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(0, await NotesRowsAsync(spare));
        Assert.Equal(audits, await PressAuditsAsync(a.UserId));

        G.Gap.Release.TrySetResult();
        Assert.Equal(HttpStatusCode.NoContent, (await purge).StatusCode);
        Assert.Equal(before, Mock.Calls);
        await AssertNothingHeldAsync(a.UserId);
        Assert.Equal(0, await NotesRowsAsync(id));
        // The authoritative notice came AFTER the commit, for every purged session (the stopped ones too).
        lock (G.Recorder.Deleted)
        {
            Assert.Contains((Guid.Parse(id), true), G.Recorder.Deleted);   // pre-commit
            Assert.Contains((Guid.Parse(id), false), G.Recorder.Deleted);  // post-commit
            Assert.Contains((Guid.Parse(spare), false), G.Recorder.Deleted);
        }
    }

    [Fact]
    public async Task A_press_queued_behind_another_job_and_dequeued_in_the_purge_gap_never_reaches_the_provider()
    {
        var (alice, busy) = await StoppedSessionAsync();
        var bob = await NewActorAsync();
        var course = await CourseAsync(bob.Client);
        var queued = await StoppedInCourseAsync(bob, course);
        var before = Mock.Calls;

        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(alice.Client, busy)).StatusCode);
        await WaitAsync(() => Task.FromResult(Mock.Calls >= before + 1), "alice's call to be in flight"); // the single worker is now blocked
        var res = await PressAsync(bob.Client, queued);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var bobPress = Guid.Parse((await res.BodyAsync()).GetProperty("notesId").GetString()!["nt_".Length..]);
        Assert.Equal("Pending", await StateOfPressAsync(bobPress)); // waiting in the queue

        var purge = await PurgeHeldInTheGapAsync(bob, course);
        // Free the worker: Alice deletes her lecture, which cancels her call; the worker then dequeues Bob's press INSIDE the gap.
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync($"/api/v1/sessions/{busy}")).StatusCode);
        await WaitAsync(async () => await StateOfPressAsync(bobPress) != "Pending", "bob's queued press to be dequeued");
        Assert.Equal("Unavailable", await StateOfPressAsync(bobPress));
        await Task.Delay(300); // give the job time to (wrongly) call
        Assert.Equal(before + 1, Mock.Calls); // only Alice's cancelled call ever reached the provider

        G.Gap.Release.TrySetResult();
        Assert.Equal(HttpStatusCode.NoContent, (await purge).StatusCode);
        Assert.Equal(0, await NotesRowsAsync(queued));
        await AssertNothingHeldAsync(bob.UserId);
        await AssertNothingHeldAsync(alice.UserId);
    }

    [Fact]
    public async Task Time_spent_waiting_in_the_queue_does_not_eat_the_permission_window()
    {
        var (alice, busy) = await StoppedSessionAsync();
        var (bob, waiting) = await StoppedSessionAsync();
        var opts = F.Services.GetRequiredService<AiOptions>();
        var before = Mock.Calls;
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(alice.Client, busy)).StatusCode);
        await WaitAsync(() => Task.FromResult(Mock.Calls >= before + 1), "alice's call to be in flight");
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(bob.Client, waiting)).StatusCode);

        // Bob's press waits longer than the whole permission window (but less than the queue limit) before it starts.
        Assert.True(opts.Notes.MaxQueueWait > opts.Notes.StaleAfter);
        F.Clock.Advance(opts.Notes.StaleAfter + TimeSpan.FromSeconds(30));
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync($"/api/v1/sessions/{busy}")).StatusCode); // frees the worker

        // The job starts now; its window starts now, so its first call is allowed (and blocks in the mock).
        await WaitAsync(() => Task.FromResult(Mock.Calls >= before + 2), "bob's first call");
        Assert.Equal("Pending", (await (await bob.Client.GetAsync($"/api/v1/sessions/{waiting}/notes")).BodyAsync()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await bob.Client.DeleteAsync($"/api/v1/sessions/{waiting}")).StatusCode);
        await WaitAsync(async () => await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{bob.UserId}' and outcome = 'reserved'") == 0, "bob's hold to be released");
    }

    // ---------- NAS-R02: the queue limit is enforced when the job starts, with no GET ----------

    [Fact]
    public async Task A_press_still_queued_past_the_queue_limit_is_never_started_even_if_nobody_reads_it()
    {
        var (alice, busy) = await StoppedSessionAsync();
        var (bob, waiting) = await StoppedSessionAsync();
        var opts = F.Services.GetRequiredService<AiOptions>();
        var before = Mock.Calls;
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(alice.Client, busy)).StatusCode);
        await WaitAsync(() => Task.FromResult(Mock.Calls >= before + 1), "alice's call to be in flight"); // the single worker is blocked
        var res = await PressAsync(bob.Client, waiting);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var bobPress = Guid.Parse((await res.BodyAsync()).GetProperty("notesId").GetString()!["nt_".Length..]);

        // Bob closes the tab: from here on nobody sends a GET or a press for Bob. His press waits past the queue limit.
        F.Clock.Advance(opts.Notes.MaxQueueWait + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync($"/api/v1/sessions/{busy}")).StatusCode); // frees the worker

        await WaitAsync(async () => await StateOfPressAsync(bobPress) != "Pending", "bob's press to be dequeued");
        Assert.Equal("Unavailable", await StateOfPressAsync(bobPress));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.lecture_notes where id = '{bobPress}' and started_at is null")); // never claimed
        await Task.Delay(300); // give the job time to (wrongly) call
        Assert.Equal(before + 1, Mock.Calls); // only Alice's cancelled call ever reached the provider
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{bob.UserId}' and workflow in ('Notes', 'FinalSummary')"));
    }

    // ---------- NAS-R04: a session created in the purge gap ----------

    [Fact]
    public async Task A_session_started_in_the_purge_gap_gets_the_post_commit_deleted_notice_and_a_deletion_audit_row()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var old = await StoppedInCourseAsync(a, course, finals: 0);

        var purge = await PurgeHeldInTheGapAsync(a, course);
        // Another tab starts a new lecture in the course while the purge sits between its pre-commit notice and the commit.
        var late = (await StartOkAsync(a.Client, course)).GetProperty("id").GetString()!;
        G.Gap.Release.TrySetResult();
        Assert.Equal(HttpStatusCode.NoContent, (await purge).StatusCode);

        Assert.Equal(0, await F.ScalarAsync($"select count(*) from lectures.lecture_sessions where id in ('{late}', '{old}')")); // the cascade removed both
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from lectures.audit_events where session_id = '{late}' and action = 'session.delete'"));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from lectures.audit_events where session_id = '{old}' and action = 'session.delete'"));
        lock (G.Recorder.Deleted)
        {
            Assert.Contains((Guid.Parse(late), false), G.Recorder.Deleted); // announced deleted after the commit (cancel, tombstone, hub close)
            Assert.Contains((Guid.Parse(old), false), G.Recorder.Deleted);
        }
    }

    [Fact]
    public async Task A_press_that_expires_mid_call_cancels_its_job_and_is_never_overwritten_back_to_Ready()
    {
        var (a, id) = await StoppedSessionAsync();
        var opts = F.Services.GetRequiredService<AiOptions>();
        var before = Mock.Calls;
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(a.Client, id)).StatusCode);
        await WaitAsync(() => Task.FromResult(Mock.Calls >= before + 1), "the notes call to be in flight");

        F.Clock.Advance(opts.Notes.StaleAfter + TimeSpan.FromSeconds(1));
        var read = await (await a.Client.GetAsync($"/api/v1/sessions/{id}/notes")).BodyAsync(); // the GET expires it
        Assert.Equal("Unavailable", read.GetProperty("status").GetString());

        // The job is cancelled at once (not after the 30 s call timeout), makes no final-summary call, and stores nothing over the expiry.
        await WaitAsync(async () => await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and outcome = 'reserved'") == 0, "the hold to be released");
        await WaitAsync(() => Task.FromResult(!F.Services.GetRequiredService<NotesJobs>().IsRunning(Guid.Parse(read.GetProperty("notesId").GetString()!["nt_".Length..]))), "the job to end");
        await Task.Delay(300);
        Assert.Equal(before + 1, Mock.Calls);
        var after = await (await a.Client.GetAsync($"/api/v1/sessions/{id}/notes")).BodyAsync();
        Assert.Equal("Unavailable", after.GetProperty("status").GetString());
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.summary_revisions where session_id = '{id}'"));
    }
}
