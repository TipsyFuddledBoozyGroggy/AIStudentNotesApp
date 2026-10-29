using System.Collections.Concurrent;
using Tether.Ai.Budget;
using Tether.Ai.Persistence;
using Tether.Ai.Provider;
using Tether.Ai.Validation;
using Tether.IntegrationTests.Infrastructure;
using Tether.SharedKernel;
using Tether.SharedKernel.Ai;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tether.IntegrationTests;

/// <summary>Stands in for the Syllabus module's probe: a scope is live only if (user, scope) was marked live.</summary>
public sealed class FakeDocumentProbe : IAiDocumentScopeProbe
{
    private readonly ConcurrentDictionary<(Guid, Guid), bool> _live = new();
    private int _calls;
    public string Kind => AiScopeKinds.Syllabus;
    public int Calls => Volatile.Read(ref _calls);
    public bool Throw { get; set; }

    /// <summary>When set, only the first N calls (counted from <see cref="ResetCalls"/>) answer live; later ones answer not live.</summary>
    public int? LiveForCalls { get; set; }

    public void MarkLive(Guid user, Guid scope) => _live[(user, scope)] = true;
    public void ResetCalls() { Interlocked.Exchange(ref _calls, 0); LiveForCalls = null; Throw = false; }

    public Task<bool> IsLiveAsync(Guid userId, Guid scopeId, CancellationToken ct)
    {
        var n = Interlocked.Increment(ref _calls);
        if (Throw) throw new InvalidOperationException("probe fault");
        if (LiveForCalls is { } max && n > max) return Task.FromResult(false);
        return Task.FromResult(_live.ContainsKey((userId, scopeId)));
    }
}

/// <summary>
/// Lecture pool: 500k per session, 700k per user-day, 5M per month. Syllabus pool: 300k per job, 600k per user-day, 1M per month.
/// A fake Syllabus probe is registered so the gateway can serve document scopes.
/// </summary>
public sealed class ScopePoolFactory : ApiFactory
{
    public FakeDocumentProbe Probe { get; } = new();

    protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
    [
        new("Ai:Budget:PerSessionMicroUsd", "500000"), new("Ai:Budget:PerUserDailyMicroUsd", "700000"), new("Ai:Budget:GlobalMonthlyMicroUsd", "5000000"),
        new("Ai:Pools:syllabus:PerJobMicroUsd", "300000"), new("Ai:Pools:syllabus:PerUserDailyMicroUsd", "600000"), new("Ai:Pools:syllabus:GlobalMonthlyMicroUsd", "1000000"),
        new("Ai:CallTimeoutSeconds", "120"), new("Ai:Summary:TickSeconds", "3600"),
    ];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.AddSingleton<IAiDocumentScopeProbe>(Probe));
    }
}

public sealed record DocResult(string Json);

/// <summary>A003: document-scoped AI work, independent budget pools, and the gateway's scope checks. Tests in a class run serially and reset the ledger.</summary>
public class AiScopePoolTests(ScopePoolFactory factory) : AiBudgetTestBase(factory), IClassFixture<ScopePoolFactory>
{
    private ScopePoolFactory Fx => factory;
    private const long K = 1000;

    private async Task<bool> ReserveAsync(Guid userId, AiScope scope, long estimate)
    {
        await using var sp = Scopes.CreateUserScope(userId);
        var wf = scope.IsLecture ? AiWorkflow.QuickRecap : AiWorkflow.SyllabusExtract;
        var o = await sp.ServiceProvider.GetRequiredService<BudgetGate>().ReserveAsync(userId, scope, wf, "t.v1", "m", estimate, default);
        return o.Reservation is not null;
    }

    private async Task ResetAsync()
    {
        _ = F.Services; // boots the host, which migrates the database
        await F.ExecAsync("delete from ai.ai_usage; delete from ai.global_spend;");
        var mock = F.Services.GetRequiredService<MockAiClient>();
        mock.Fault = MockFault.None;
        mock.FaultOnly = null;
        Fx.Probe.ResetCalls();
    }

    private Task<long> GlobalPoolAsync(string pool) => F.ScalarAsync($"select coalesce(sum(spent_micros), 0) from ai.global_spend where pool = '{pool}'");

    private static AiRequest DocRequest(Guid userId, Guid syllabusId, int maxOutputTokens = 700) => AiRequest.ForDocument(
        AiScope.Syllabus(syllabusId), AiWorkflow.SyllabusExtract, "syllabus.test", AiTier.Fast, "SYSTEM", "chunk=1",
        [new UntrustedBlock(BlockKinds.SourceChunk, [new SourceLine("L0001 p1", 0, "Final exam: December 12."), new SourceLine("L0002 p1", 0, "Office hours Tuesday.")])],
        maxOutputTokens, userId, "k", TimeSpan.FromSeconds(30));

    private async Task<GatewayOutcome<DocResult>> RunDocAsync(Guid userId, AiRequest req)
    {
        await using var sp = Scopes.CreateUserScope(userId);
        return await sp.ServiceProvider.GetRequiredService<AiGateway>().RunAsync(req, j => Validated<DocResult>.Success(new DocResult(j)), default);
    }

    // ---------- pool independence ----------

    [Fact]
    public async Task Exhausting_the_syllabus_daily_pool_leaves_lecture_ai_working_for_the_same_user()
    {
        await ResetAsync();
        var a = await NewActorAsync();
        Assert.True(await ReserveAsync(a.UserId, AiScope.Syllabus(Guid.NewGuid()), 300 * K));
        Assert.True(await ReserveAsync(a.UserId, AiScope.Syllabus(Guid.NewGuid()), 300 * K));
        Assert.False(await ReserveAsync(a.UserId, AiScope.Syllabus(Guid.NewGuid()), 100 * K)); // syllabus user-day cap (600k) reached

        Assert.True(await ReserveAsync(a.UserId, AiScope.Lecture(Guid.NewGuid()), 400 * K)); // lecture pool untouched
        Assert.Equal(600 * K, await F.ScalarAsync($"select sum(est_cost_micros) from ai.ai_usage where user_id = '{a.UserId}' and pool = 'syllabus'"));
        Assert.Equal(400 * K, await F.ScalarAsync($"select sum(est_cost_micros) from ai.ai_usage where user_id = '{a.UserId}' and pool = 'lecture'"));
    }

    [Fact]
    public async Task Exhausting_the_lecture_daily_pool_leaves_syllabus_ai_working_for_the_same_user()
    {
        await ResetAsync();
        var a = await NewActorAsync();
        Assert.True(await ReserveAsync(a.UserId, AiScope.Lecture(Guid.NewGuid()), 400 * K));
        Assert.True(await ReserveAsync(a.UserId, AiScope.Lecture(Guid.NewGuid()), 300 * K));
        Assert.False(await ReserveAsync(a.UserId, AiScope.Lecture(Guid.NewGuid()), 100 * K)); // lecture user-day cap (700k) reached

        Assert.True(await ReserveAsync(a.UserId, AiScope.Syllabus(Guid.NewGuid()), 300 * K));
    }

    [Fact]
    public async Task The_global_monthly_ceiling_is_per_pool_and_each_pool_has_its_own_counter_row()
    {
        await ResetAsync();
        for (var i = 0; i < 3; i++)
            Assert.True(await ReserveAsync((await NewActorAsync()).UserId, AiScope.Syllabus(Guid.NewGuid()), 300 * K)); // 900k of 1M; different users so no daily cap applies
        Assert.False(await ReserveAsync((await NewActorAsync()).UserId, AiScope.Syllabus(Guid.NewGuid()), 300 * K));   // would cross the syllabus month ceiling
        Assert.True(await ReserveAsync((await NewActorAsync()).UserId, AiScope.Lecture(Guid.NewGuid()), 400 * K));     // lecture ceiling (5M) unaffected

        Assert.Equal(900 * K, await GlobalPoolAsync("syllabus"));
        Assert.Equal(400 * K, await GlobalPoolAsync("lecture"));
        Assert.Equal(2, await F.ScalarAsync("select count(*) from ai.global_spend"));
    }

    [Fact]
    public async Task The_per_job_cap_applies_to_one_syllabus_scope_and_not_to_the_next()
    {
        await ResetAsync();
        var a = await NewActorAsync();
        var job = Guid.NewGuid();
        Assert.True(await ReserveAsync(a.UserId, AiScope.Syllabus(job), 300 * K));
        Assert.False(await ReserveAsync(a.UserId, AiScope.Syllabus(job), 1));                    // job cap (300k) reached
        Assert.True(await ReserveAsync(a.UserId, AiScope.Syllabus(Guid.NewGuid()), 300 * K));   // another job has its own cap
    }

    // ---------- scope isolation ----------

    [Fact]
    public async Task The_same_id_under_two_scope_kinds_and_two_users_never_shares_spend()
    {
        await ResetAsync();
        var a = await NewActorAsync();
        var b = await NewActorAsync();
        var id = Guid.NewGuid();
        Assert.True(await ReserveAsync(a.UserId, AiScope.Lecture(id), 400 * K));
        Assert.True(await ReserveAsync(a.UserId, AiScope.Syllabus(id), 300 * K));  // lecture spend under the same id does not count toward the job
        Assert.True(await ReserveAsync(b.UserId, AiScope.Lecture(id), 400 * K));   // another user's rows under the same id are invisible (query filter)
        Assert.False(await ReserveAsync(a.UserId, AiScope.Lecture(id), 200 * K));  // 400k + 200k > 500k session cap, still enforced
    }

    [Fact]
    public async Task Ledger_rows_carry_the_scope_and_dual_write_session_id_for_lectures_only()
    {
        await ResetAsync();
        var a = await NewActorAsync();
        var lec = Guid.NewGuid(); var syl = Guid.NewGuid();
        Assert.True(await ReserveAsync(a.UserId, AiScope.Lecture(lec), 1000));
        Assert.True(await ReserveAsync(a.UserId, AiScope.Syllabus(syl), 1000));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.ai_usage where scope_kind = 'lecture' and pool = 'lecture' and scope_id = '{lec}' and session_id = '{lec}'"));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.ai_usage where scope_kind = 'syllabus' and pool = 'syllabus' and scope_id = '{syl}' and session_id is null and workflow = 'SyllabusExtract'"));
    }

    [Fact]
    public async Task The_database_refuses_inconsistent_scope_rows()
    {
        await ResetAsync();
        var a = await NewActorAsync();
        const string cols = "id, user_id, workflow, prompt_version, model, input_tokens, cached_tokens, output_tokens, est_cost_micros, latency_ms, outcome, created_at, scope_kind, scope_id, pool, session_id";
        Task Insert(string workflow, string kind, string pool, string session) => F.ExecAsync(
            $"insert into ai.ai_usage ({cols}) values ('{Guid.NewGuid()}', '{a.UserId}', '{workflow}', 'p', 'm', 0, 0, 0, 0, 0, 'ok', now(), '{kind}', '{Guid.NewGuid()}', '{pool}', {session})");
        await Assert.ThrowsAsync<PostgresException>(() => Insert("QuickRecap", "syllabus", "syllabus", "null"));                       // lecture workflow on a document scope
        await Assert.ThrowsAsync<PostgresException>(() => Insert("SyllabusExtract", "lecture", "lecture", "null"));                   // document workflow on a lecture scope
        await Assert.ThrowsAsync<PostgresException>(() => Insert("SyllabusExtract", "syllabus", "lecture", "null"));                  // pool must equal the scope kind
        await Assert.ThrowsAsync<PostgresException>(() => Insert("SyllabusExtract", "syllabus", "syllabus", $"'{Guid.NewGuid()}'")); // document row with a session
        await Assert.ThrowsAsync<PostgresException>(() => Insert("QuickRecap", "lecture", "lecture", "null"));                         // lecture row without a session
        await Assert.ThrowsAsync<PostgresException>(() => Insert("SyllabusExtract", "other", "other", "null"));                       // unknown kind
    }

    // ---------- reaper ----------

    [Fact]
    public async Task The_reaper_settles_a_stale_syllabus_hold_and_returns_it_to_the_syllabus_counter_only()
    {
        await ResetAsync();
        var a = await NewActorAsync();
        Assert.True(await ReserveAsync(a.UserId, AiScope.Syllabus(Guid.NewGuid()), 300 * K));
        Assert.True(await ReserveAsync(a.UserId, AiScope.Lecture(Guid.NewGuid()), 200 * K));
        Assert.Equal(300 * K, await GlobalPoolAsync("syllabus"));
        Assert.Equal(200 * K, await GlobalPoolAsync("lecture"));

        var swept = await F.Services.GetRequiredService<ReservationReaper>().SweepAsync(startup: true, default);

        Assert.Equal(2, swept);
        Assert.Equal(0, await GlobalPoolAsync("syllabus"));
        Assert.Equal(0, await GlobalPoolAsync("lecture"));
        Assert.Equal(0, await F.ScalarAsync("select count(*) from ai.ai_usage where outcome = 'reserved'"));
        Assert.Equal(2, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and outcome = 'error' and est_cost_micros = 0"));
    }

    [Fact]
    public async Task The_reaper_releases_only_the_syllabus_pool_when_only_a_syllabus_hold_is_stale()
    {
        await ResetAsync();
        var a = await NewActorAsync();
        Assert.True(await ReserveAsync(a.UserId, AiScope.Syllabus(Guid.NewGuid()), 300 * K));
        F.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(await ReserveAsync(a.UserId, AiScope.Lecture(Guid.NewGuid()), 200 * K)); // fresh, genuinely in flight

        Assert.Equal(1, await F.Services.GetRequiredService<ReservationReaper>().SweepAsync(startup: false, default));

        Assert.Equal(0, await GlobalPoolAsync("syllabus"));
        Assert.Equal(200 * K, await GlobalPoolAsync("lecture"));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and outcome = 'reserved' and pool = 'lecture'"));
    }

    // ---------- gateway ----------

    [Fact]
    public async Task A_live_document_scope_runs_through_the_gateway_and_is_accounted_in_the_syllabus_pool()
    {
        await ResetAsync();
        var a = await NewActorAsync();
        var syl = Guid.NewGuid();
        Fx.Probe.MarkLive(a.UserId, syl);

        var outcome = await RunDocAsync(a.UserId, DocRequest(a.UserId, syl));

        Assert.True(outcome.Ok, outcome.Failure);
        Assert.StartsWith("mock", outcome.Model);
        Assert.Contains("L0001 p1", outcome.Value!.Json);
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and scope_kind = 'syllabus' and scope_id = '{syl}' and pool = 'syllabus' and session_id is null and outcome = 'ok'"));
        Assert.Equal(0, await F.ScalarAsync("select count(*) from ai.ai_usage where outcome = 'reserved'"));
        Assert.Equal(0, await GlobalPoolAsync("lecture"));
        Assert.True(await GlobalPoolAsync("syllabus") > 0);
    }

    [Fact]
    public async Task A_document_scope_that_is_not_live_for_this_user_gets_no_call_and_no_spend()
    {
        await ResetAsync();
        var a = await NewActorAsync();
        var b = await NewActorAsync();
        var syl = Guid.NewGuid();
        Fx.Probe.MarkLive(b.UserId, syl); // live, but it is someone else's
        var mock = F.Services.GetRequiredService<MockAiClient>();
        var before = mock.Calls;

        var outcome = await RunDocAsync(a.UserId, DocRequest(a.UserId, syl));

        Assert.Equal("session_closed", outcome.Failure);
        Assert.Equal(before, mock.Calls);
        Assert.Equal(0, await F.ScalarAsync("select count(*) from ai.ai_usage"));
        Assert.Equal(0, await F.ScalarAsync("select count(*) from ai.global_spend"));
    }

    [Fact]
    public async Task A_probe_that_throws_fails_closed()
    {
        await ResetAsync();
        var a = await NewActorAsync();
        var syl = Guid.NewGuid();
        Fx.Probe.MarkLive(a.UserId, syl);
        Fx.Probe.Throw = true;
        var mock = F.Services.GetRequiredService<MockAiClient>();
        var before = mock.Calls;

        var outcome = await RunDocAsync(a.UserId, DocRequest(a.UserId, syl));

        Assert.Equal("session_closed", outcome.Failure);
        Assert.Equal(before, mock.Calls);
        Assert.Equal(0, await F.ScalarAsync("select count(*) from ai.ai_usage"));
    }

    [Fact]
    public async Task A_scope_that_stops_being_live_after_the_reservation_releases_the_hold_without_calling_the_vendor()
    {
        await ResetAsync();
        var a = await NewActorAsync();
        var syl = Guid.NewGuid();
        Fx.Probe.MarkLive(a.UserId, syl);
        Fx.Probe.LiveForCalls = 1; // the cheap pre-check passes; the last gate before the vendor does not
        var mock = F.Services.GetRequiredService<MockAiClient>();
        var before = mock.Calls;

        var outcome = await RunDocAsync(a.UserId, DocRequest(a.UserId, syl));

        Assert.Equal("session_closed", outcome.Failure);
        Assert.Equal(2, Fx.Probe.Calls);
        Assert.Equal(before, mock.Calls);
        Assert.Equal(0, await F.ScalarAsync("select count(*) from ai.ai_usage where outcome = 'reserved'"));
        Assert.Equal(0, await F.ScalarAsync("select coalesce(sum(est_cost_micros), 0) from ai.ai_usage"));
        Assert.Equal(0, await GlobalPoolAsync("syllabus"));
    }

    [Fact]
    public async Task A_document_call_above_the_job_cap_is_denied_and_recorded_without_spend()
    {
        await ResetAsync();
        var a = await NewActorAsync();
        var syl = Guid.NewGuid();
        Fx.Probe.MarkLive(a.UserId, syl);
        var mock = F.Services.GetRequiredService<MockAiClient>();
        var before = mock.Calls;

        // 700k output tokens at the Fast placeholder price (500k micro-USD per million) is about 350k, above the 300k job cap.
        var outcome = await RunDocAsync(a.UserId, DocRequest(a.UserId, syl, maxOutputTokens: 700_000));

        Assert.Equal("budget_limited", outcome.Failure);
        Assert.Equal(before, mock.Calls);
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.ai_usage where scope_kind = 'syllabus' and scope_id = '{syl}' and outcome = 'budget_denied' and est_cost_micros = 0"));
        Assert.Equal(0, await GlobalPoolAsync("syllabus"));
    }

    [Fact]
    public async Task Cancelling_a_document_scope_aborts_the_call_in_flight_and_gives_the_hold_back()
    {
        await ResetAsync();
        var a = await NewActorAsync();
        var syl = Guid.NewGuid();
        Fx.Probe.MarkLive(a.UserId, syl);
        var mock = F.Services.GetRequiredService<MockAiClient>();
        mock.FaultOnly = AiWorkflow.SyllabusExtract;
        mock.Fault = MockFault.Timeout; // the mock now hangs until cancelled
        var before = mock.Calls;
        var run = RunDocAsync(a.UserId, DocRequest(a.UserId, syl));
        for (var i = 0; i < 200 && mock.Calls <= before; i++) await Task.Delay(25);
        Assert.True(mock.Calls > before, "the call should be in flight");
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.ai_usage where scope_id = '{syl}' and outcome = 'reserved'"));

        F.Services.GetRequiredService<IAiScopeCutoff>().Cancel(AiScopeKinds.Syllabus, syl);

        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal("session_closed", outcome.Failure);
        Assert.Equal(0, await F.ScalarAsync("select count(*) from ai.ai_usage where outcome = 'reserved'"));
        Assert.Equal(0, await F.ScalarAsync("select coalesce(sum(est_cost_micros), 0) from ai.ai_usage"));
        Assert.Equal(0, await GlobalPoolAsync("syllabus"));
    }

    [Fact]
    public async Task The_lecture_path_still_requires_an_active_session_and_writes_lecture_rows()
    {
        await ResetAsync();
        var a = await NewActorAsync();
        var outcome = await RunGatewayAsync(a);
        Assert.True(outcome.Ok, outcome.Failure);
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and scope_kind = 'lecture' and pool = 'lecture' and session_id = scope_id and workflow = 'QuickRecap'"));
        Assert.Equal(0, await GlobalPoolAsync("syllabus"));
        Assert.Equal(0, Fx.Probe.Calls); // the document probe is never consulted for a lecture
    }

    [Fact]
    public async Task A_workflow_that_does_not_match_its_scope_is_a_programming_error_not_a_call()
    {
        var a = await NewActorAsync();
        var bad = DocRequest(a.UserId, Guid.NewGuid()) with { Workflow = AiWorkflow.QuickRecap };
        await Assert.ThrowsAsync<ArgumentException>(() => RunDocAsync(a.UserId, bad));
    }
}

public sealed class ScopeMigrationFactory : ApiFactory { }

/// <summary>A003 schema change on a database that already holds legacy ledger rows, and its guarded Down.</summary>
public class AiScopeMigrationTests(ScopeMigrationFactory factory) : AiBudgetTestBase(factory), IClassFixture<ScopeMigrationFactory>
{
    private const string A002 = "20261028054119_A002_BudgetReservationAndPendingDedupe";
    private const string A003 = "20261028194636_A003_AiScope";
    private const string A004 = "20261029145247_A004_RenameLostTrackToQuickRecap";

    private async Task MigrateAsync(string target)
    {
        await using var sp = F.Services.CreateAsyncScope();
        var db = sp.ServiceProvider.GetRequiredService<AiDbContext>();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private Task<long> HasColumnAsync(string table, string column) =>
        F.ScalarAsync($"select count(*) from information_schema.columns where table_schema = 'ai' and table_name = '{table}' and column_name = '{column}'");

    [Fact]
    public async Task Up_backfills_legacy_rows_down_is_guarded_by_document_rows_and_the_cycle_is_repeatable()
    {
        var a = await NewActorAsync();
        var s1 = Guid.NewGuid(); var s2 = Guid.NewGuid();

        // Roll the Ai schema back to the pre-A003 shape (no document rows exist yet, so the guard passes) and insert legacy rows.
        await MigrateAsync(A002);
        Assert.Equal(0, await HasColumnAsync("ai_usage", "scope_id"));
        Assert.Equal(0, await HasColumnAsync("global_spend", "pool"));
        const string legacyCols = "id, user_id, session_id, workflow, prompt_version, model, input_tokens, cached_tokens, output_tokens, est_cost_micros, latency_ms, outcome, created_at";
        await F.ExecAsync(
            $"insert into ai.ai_usage ({legacyCols}) values " +
            $"('{Guid.NewGuid()}', '{a.UserId}', '{s1}', 'LostTrack', 'p', 'm', 1, 0, 2, 111, 5, 'ok', now()), " +
            $"('{Guid.NewGuid()}', '{a.UserId}', '{s1}', 'RollingSummary', 'p', 'm', 1, 0, 2, 222, 5, 'reserved', now()), " +
            $"('{Guid.NewGuid()}', '{a.UserId}', '{s2}', 'Compaction', 'p', 'm', 1, 0, 2, 333, 5, 'error', now()); " +
            "insert into ai.global_spend (period, spent_micros, updated_at) values ('2026-10', 666, now());");

        await MigrateAsync(A003);

        Assert.Equal(3, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and scope_kind = 'lecture' and pool = 'lecture' and scope_id = session_id"));
        Assert.Equal(2, await F.ScalarAsync($"select count(*) from ai.ai_usage where scope_id = '{s1}'"));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.ai_usage where scope_id = '{s2}' and est_cost_micros = 333"));
        Assert.Equal(666, await F.ScalarAsync("select spent_micros from ai.global_spend where period = '2026-10' and pool = 'lecture'"));
        Assert.Equal("period,pool", await F.TextAsync(
            "select string_agg(a.attname, ',' order by array_position(i.indkey::int2[], a.attnum)) from pg_index i " +
            "join pg_attribute a on a.attrelid = i.indrelid and a.attnum = any(i.indkey) where i.indrelid = 'ai.global_spend'::regclass and i.indisprimary"));
        Assert.Equal(1, await F.ScalarAsync("select count(*) from information_schema.columns where table_schema = 'ai' and table_name = 'ai_usage' and column_name = 'session_id' and is_nullable = 'YES'"));
        Assert.Equal(1, await F.ScalarAsync("select count(*) from information_schema.columns where table_schema = 'ai' and table_name = 'ai_usage' and column_name = 'scope_id' and is_nullable = 'NO'"));

        // A document row now exists: Down must refuse and leave everything as it was (the migration is transactional).
        await F.ExecAsync(
            "insert into ai.ai_usage (id, user_id, scope_kind, scope_id, pool, workflow, prompt_version, model, input_tokens, cached_tokens, output_tokens, est_cost_micros, latency_ms, outcome, created_at) " +
            $"values ('{Guid.NewGuid()}', '{a.UserId}', 'syllabus', '{Guid.NewGuid()}', 'syllabus', 'SyllabusExtract', 'p', 'm', 0, 0, 0, 5, 0, 'ok', now())");
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync(A002));
        Assert.Contains("A003 cannot be rolled back", ex.ToString());
        Assert.Equal(1, await HasColumnAsync("ai_usage", "scope_id"));
        Assert.Equal(4, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}'"));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.\"__ef_migrations_history\" where \"MigrationId\" = '{A003}'"));

        // A syllabus-pool counter row alone also blocks Down.
        await F.ExecAsync("delete from ai.ai_usage where scope_kind = 'syllabus'; insert into ai.global_spend (period, pool, spent_micros, updated_at) values ('2026-10', 'syllabus', 0, now());");
        await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync(A002));
        Assert.Equal(1, await HasColumnAsync("global_spend", "pool"));
        await F.ExecAsync("delete from ai.global_spend where pool = 'syllabus'"); // throwaway test database only: production policy is forward-fix, never delete spend evidence

        // With no document-scope evidence left, Down restores the old shape without touching lecture rows, and Up again is clean.
        await MigrateAsync(A002);
        Assert.Equal(0, await HasColumnAsync("ai_usage", "scope_id"));
        Assert.Equal(0, await HasColumnAsync("ai_usage", "pool"));
        Assert.Equal(0, await HasColumnAsync("global_spend", "pool"));
        Assert.Equal(1, await F.ScalarAsync("select count(*) from information_schema.columns where table_schema = 'ai' and table_name = 'ai_usage' and column_name = 'session_id' and is_nullable = 'NO'"));
        Assert.Equal(3, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}'"));
        Assert.Equal(666, await F.ScalarAsync("select spent_micros from ai.global_spend where period = '2026-10'"));

        await MigrateAsync(A003);
        Assert.Equal(3, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and scope_id = session_id"));
        Assert.Equal(666, await F.ScalarAsync("select spent_micros from ai.global_spend where period = '2026-10' and pool = 'lecture'"));

        // A004 (PO sensitive-language rename): the legacy LostTrack row is relabelled, the CHECK accepts only the new name, and Down is reversible.
        await F.ExecAsync($"update ai.ai_usage set prompt_version = 'lost-track.v1' where user_id = '{a.UserId}' and workflow = 'LostTrack'");
        await MigrateAsync(A004);
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and workflow = 'QuickRecap' and prompt_version = 'quick-recap.v1'"));
        Assert.Equal(0, await F.ScalarAsync("select count(*) from ai.ai_usage where workflow = 'LostTrack' or prompt_version = 'lost-track.v1'"));
        var def = await F.TextAsync("select pg_get_constraintdef(oid) from pg_constraint where conname = 'ck_ai_usage_workflow' and conrelid = 'ai.ai_usage'::regclass");
        Assert.Contains("QuickRecap", def);
        Assert.DoesNotContain("LostTrack", def);
        await MigrateAsync(A003);
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and workflow = 'LostTrack' and prompt_version = 'lost-track.v1'"));
        await MigrateAsync(A004);
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and workflow = 'QuickRecap'"));
    }
}
