using System.Net;
using Tether.Ai.Domain;
using Tether.Ai.Provider;
using Tether.Ai.Summary;
using Tether.IntegrationTests.Infrastructure;
using Tether.SharedKernel;
using Tether.SharedKernel.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tether.IntegrationTests;

// Deterministic reproductions for docs/data/lock-order-audit.md (F-1, F-2, F-4, F-5, F-7). Method as in RelayDeleteDeadlockTests: a
// CommandTrap stops the code under test just before the statement that closes a lock cycle, a raw connection plays the other transaction,
// and pg_blocking_pids decides when to close the cycle. No sleeps. PostgreSQL runs its deadlock check in a waiter deadlock_timeout after
// that waiter began to wait, and the checker aborts its own transaction: the code under test always starts waiting first, so it is the
// victim, which is the path each fix is about. The other side's statement returning normally (not 40P01) proves that.

/// <summary>Plays the other transaction of a two-lock cycle on its own connection.</summary>
internal sealed class OtherSide : IAsyncDisposable
{
    private readonly NpgsqlConnection _conn;
    private readonly NpgsqlTransaction _tx;
    private OtherSide(NpgsqlConnection conn, NpgsqlTransaction tx) { _conn = conn; _tx = tx; }

    /// <summary>Completes once the cycle was closed, the code under test was aborted, and this side committed. Faults if this side was the victim.</summary>
    public Task Done { get; private set; } = Task.CompletedTask;

    /// <summary>Opens a transaction and takes the first lock (<paramref name="first"/>) now.</summary>
    public static async Task<OtherSide> HoldAsync(ApiFactory f, string first)
    {
        var conn = new NpgsqlConnection(f.ConnectionString);
        await conn.OpenAsync();
        var side = new OtherSide(conn, await conn.BeginTransactionAsync());
        await side.ExecAsync(first);
        return side;
    }

    /// <summary>
    /// In the background: once <paramref name="victimPid"/> waits on this side, run <paramref name="second"/> (it waits on the victim, closing the
    /// cycle, and returns when PostgreSQL has aborted the victim), then <paramref name="then"/> statements, then commit.
    /// </summary>
    public void CloseCycleWhenWaiting(ApiFactory f, int victimPid, string second, params string[] then) => Done = Task.Run(async () =>
    {
        var waiting = await Rt.WaitAsync(async () => await LockRace.BlockedByAsync(f, victimPid, _conn.ProcessID), 10000);
        if (!waiting) { await _tx.RollbackAsync(); throw new InvalidOperationException("the code under test never waited on the other side"); }
        await ExecAsync(second);
        foreach (var s in then) await ExecAsync(s);
        await _tx.CommitAsync();
    });

    private async Task ExecAsync(string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, _conn, _tx);
        await cmd.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try { await Done; } catch (Exception) { /* reported by the test that awaits Done */ }
        await _tx.DisposeAsync();
        await _conn.DisposeAsync();
    }
}

internal static class LockRace
{
    public static int Pid(System.Data.Common.DbCommand cmd) => ((NpgsqlConnection)cmd.Connection!).ProcessID;

    public static async Task<bool> BlockedByAsync(ApiFactory f, int waiterPid, int holderPid) =>
        await f.ScalarAsync($"select count(*) from (select 1) x where {holderPid} = any(pg_blocking_pids({waiterPid}))") > 0;

    public static Task<long> DeadlocksAsync(ApiFactory f) => f.ScalarAsync("select deadlocks from pg_stat_database where datname = current_database()");

    /// <summary>pg_stat counters are flushed asynchronously (at most once a second per backend).</summary>
    public static async Task AssertDeadlockCountedAsync(ApiFactory f, long before, long expected = 1) =>
        Assert.True(await Rt.WaitAsync(async () => await DeadlocksAsync(f) >= before + expected, 10000),
            $"PostgreSQL recorded fewer than {expected} deadlock(s): the race window was not reached");
}

/// <summary>A <see cref="CommandTrap"/> on the Lectures and the Courses DbContexts; the lifecycle job is effectively off.</summary>
public sealed class LockOrderFactory : ApiFactory
{
    public CommandTrap LecturesTrap { get; } = new();
    public CommandTrap CoursesTrap { get; } = new();

    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Lectures:LifecycleJobSeconds", "3600")];

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s =>
        {
            s.ConfigureDbContext<Tether.Lectures.Persistence.LecturesDbContext>(o => o.AddInterceptors(LecturesTrap));
            s.ConfigureDbContext<Tether.Courses.Persistence.CoursesDbContext>(o => o.AddInterceptors(CoursesTrap));
        });
    }
}

public class LockOrderLecturesTests(LockOrderFactory f) : LectureTestBase(f), IClassFixture<LockOrderFactory>
{
    private LockOrderFactory Fx => (LockOrderFactory)F;

    // ---------------- F-4: Stop versus a session delete ----------------

    /// <summary>
    /// The Stop SaveChanges is ONE batch, emitted by EF Core 10 in this order (captured with a CommandTrap on 2026-11-01):
    /// DELETE audio_tickets, INSERT audit_events, INSERT consent_events, UPDATE lecture_sessions, UPDATE session_counters, INSERT session_events.
    /// With an outstanding ticket that is "ticket row, then session row", the reverse of a session delete (session row, then its cascade to
    /// audio_tickets). The other side here takes exactly the delete's two locks in the delete's order (FOR UPDATE conflicts with the Stop's
    /// UPDATE as DELETE does), because the real cascade's child order cannot be paused between two of its rows.
    /// </summary>
    private static bool IsTransitionBatch(string sql) =>
        sql.Contains("DELETE FROM lectures.audio_tickets", StringComparison.Ordinal) && sql.Contains("UPDATE lectures.lecture_sessions", StringComparison.Ordinal);

    [Fact]
    public async Task A_stop_chosen_as_the_deadlock_victim_of_a_delete_lock_order_is_retried_and_never_refused()
    {
        var a = await NewActorAsync();
        var id = (await StartOkAsync(a.Client, await CourseAsync(a.Client))).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/audio-ticket")).StatusCode);
        OtherSide? side = null;
        Fx.LecturesTrap.Arm(IsTransitionBatch, async cmd =>
        {
            side = await OtherSide.HoldAsync(F, $"select 1 from lectures.lecture_sessions where id = '{id}' for update");
            side.CloseCycleWhenWaiting(F, LockRace.Pid(cmd), $"delete from lectures.audio_tickets where session_id = '{id}'");
        });
        var before = await LockRace.DeadlocksAsync(F);

        var res = await PostAsync(a.Client, $"/sessions/{id}/stop");
        Assert.True(Fx.LecturesTrap.Fired, "the race window was not reached");
        await using (side!) await side!.Done; // the other side was NOT the victim: the Stop was

        // ADR-019b: Pause and Stop are never refused. A lost deadlock is retried, not a 500.
        Assert.True(res.StatusCode == HttpStatusCode.OK, $"{(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        Assert.Equal("Stopped", (await res.BodyAsync()).GetProperty("state").GetString());
        await LockRace.AssertDeadlockCountedAsync(F, before);
        Assert.Equal("Stopped", await F.TextAsync($"select state from lectures.lecture_sessions where id = '{id}'"));
        Assert.Equal(1, await CountAsync("consent_events", $"session_id = '{id}' and kind = 'Stop'")); // the aborted attempt left nothing
        Assert.Equal(1, await CountAsync("audit_events", $"session_id = '{id}' and action = 'session.stop'"));
        Assert.Equal(2, await CountAsync("session_events", $"session_id = '{id}'"));
        Assert.Equal(0, await CountAsync("audio_tickets", $"session_id = '{id}'"));
    }

    // ---------------- F-1: course purge as the deadlock victim ----------------

    private static bool IsCourseDelete(string sql) => sql.Contains("DELETE FROM courses.courses", StringComparison.Ordinal);

    /// <summary>Holds a cascade child of the course (a stopped session row), then asks for KEY SHARE on the course row the purge already holds, as a
    /// writer that inserts a row referencing the course would (F-3's shape).</summary>
    private void ArmPurgeDeadlock(string course, string heldSession, List<OtherSide> sides, int times)
    {
        Fx.CoursesTrap.Arm(IsCourseDelete, async cmd =>
        {
            if (--times > 0) ArmPurgeDeadlock(course, heldSession, sides, times); // the retry's DELETE is trapped again
            var side = await OtherSide.HoldAsync(F, $"select 1 from lectures.lecture_sessions where id = '{heldSession}' for update");
            side.CloseCycleWhenWaiting(F, LockRace.Pid(cmd), $"select 1 from courses.courses where id = '{course}' for key share");
            lock (sides) sides.Add(side);
        });
    }

    private async Task<(Actor A, string Course, string Stopped, string Live)> CourseWithTwoSessionsAsync()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var stopped = (await StartOkAsync(a.Client, course)).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{stopped}/stop")).StatusCode);
        var live = (await StartOkAsync(a.Client, course)).GetProperty("id").GetString()!;
        return (a, course, stopped, live);
    }

    private Task<long> DeleteAuditsAsync(string session) => CountAsync("audit_events", $"session_id = '{session}' and action = 'session.delete'");

    [Fact]
    public async Task A_purge_chosen_as_the_deadlock_victim_is_retried_returns_204_and_audits_each_session_once()
    {
        var (a, course, stopped, live) = await CourseWithTwoSessionsAsync();
        var sides = new List<OtherSide>();
        ArmPurgeDeadlock(course, stopped, sides, times: 1);
        var before = await LockRace.DeadlocksAsync(F);

        var res = await a.Client.DeleteAsync($"/api/v1/courses/{course}?purge=true");
        Assert.True(Fx.CoursesTrap.Fired, "the race window was not reached");
        foreach (var s in sides) await using (s) await s.Done;

        Assert.True(res.StatusCode == HttpStatusCode.NoContent, $"{(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        await LockRace.AssertDeadlockCountedAsync(F, before);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from courses.courses where id = '{course}'"));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from lectures.lecture_sessions where course_id = '{course}'"));
        Assert.Equal(1, await DeleteAuditsAsync(stopped));
        Assert.Equal(1, await DeleteAuditsAsync(live));
        Assert.Equal(1, await CountAsync("consent_events", $"session_id = '{live}' and kind = 'Stop'"));
    }

    [Fact]
    public async Task A_purge_that_keeps_losing_deadlocks_is_409_not_500_and_a_client_retry_does_not_audit_twice()
    {
        var (a, course, stopped, live) = await CourseWithTwoSessionsAsync();
        var sides = new List<OtherSide>();
        ArmPurgeDeadlock(course, stopped, sides, times: 3); // every attempt loses
        var before = await LockRace.DeadlocksAsync(F);

        var res = await a.Client.DeleteAsync($"/api/v1/courses/{course}?purge=true");
        foreach (var s in sides) await using (s) await s.Done;
        Assert.Equal(3, sides.Count);
        await res.AssertProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");
        await LockRace.AssertDeadlockCountedAsync(F, before, expected: 3);

        // The purge rolled back: the course and both sessions are intact (the live one was stopped before the delete, as it must be).
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from courses.courses where id = '{course}'"));
        Assert.Equal("Stopped", await F.TextAsync($"select state from lectures.lecture_sessions where id = '{live}'"));
        Assert.Equal(1, await DeleteAuditsAsync(stopped));
        Assert.Equal(1, await DeleteAuditsAsync(live));

        // The client retries: it succeeds, and the pre-commit step does not write a second session.delete row for either session.
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/v1/courses/{course}?purge=true")).StatusCode);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from courses.courses where id = '{course}'"));
        Assert.Equal(1, await DeleteAuditsAsync(stopped));
        Assert.Equal(1, await DeleteAuditsAsync(live));
        Assert.Equal(1, await CountAsync("consent_events", $"session_id = '{live}' and kind = 'Stop'"));
    }

    // ---------------- F-7: the parent went away between the read and the insert ----------------

    [Fact]
    public async Task Creating_a_session_in_a_course_purged_after_the_ownership_check_is_404_not_500()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        Fx.LecturesTrap.Arm(sql => sql.Contains("INSERT INTO lectures.lecture_sessions", StringComparison.Ordinal),
            () => F.ExecAsync($"delete from courses.courses where id = '{course}'"));

        var res = await StartAsync(a.Client, course);
        Assert.True(Fx.LecturesTrap.Fired, "the race window was not reached");
        await res.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from lectures.lecture_sessions where course_id = '{course}'"));
    }

    [Fact]
    public async Task Issuing_a_ticket_for_a_session_deleted_after_the_read_is_404_not_500()
    {
        var a = await NewActorAsync();
        var id = (await StartOkAsync(a.Client, await CourseAsync(a.Client))).GetProperty("id").GetString()!;
        Fx.LecturesTrap.Arm(sql => sql.Contains("INSERT INTO lectures.audio_tickets", StringComparison.Ordinal),
            () => F.ExecAsync($"delete from lectures.lecture_sessions where id = '{id}'"));

        var res = await PostAsync(a.Client, $"/sessions/{id}/audio-ticket");
        Assert.True(Fx.LecturesTrap.Fired, "the race window was not reached");
        await res.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(0, await CountAsync("audio_tickets", $"session_id = '{id}'"));
    }
}

// ---------------- F-2: Final-summary append (press row lock, then the FK check on the session) ----------------

public class LockOrderSummaryAppendTests(NotesPressRaceFactory f) : NotesTestBase(f), IClassFixture<NotesPressRaceFactory>
{
    [Fact]
    public async Task A_final_append_chosen_as_the_deadlock_victim_of_a_session_delete_is_retried_and_finds_the_press_gone()
    {
        var (a, id) = await StoppedSessionAsync(finals: 0);
        var press = Guid.NewGuid();
        await InsertPressAsync(id, a.UserId, press);
        var trap = ((NotesPressRaceFactory)F).Trap;
        var session = Guid.Parse(id);
        OtherSide? side = null;
        // The writer holds the press row (FOR UPDATE) and its insert's FK check needs KEY SHARE on the session row. The other side takes the
        // delete's locks in the delete's order: the session row, then (cascade) the press row; once the writer is aborted it really deletes.
        // Only this session's insert is trapped (re-armed if some other summary write reaches the trap first).
        void Arm() => trap.Arm(sql => sql.Contains("INSERT INTO ai.summary_revisions", StringComparison.Ordinal), async cmd =>
        {
            if (!cmd.Parameters.Cast<System.Data.Common.DbParameter>().Any(p => p.Value is Guid g && g == session)) { Arm(); return; }
            side = await OtherSide.HoldAsync(F, $"select 1 from lectures.lecture_sessions where id = '{id}' for update");
            side.CloseCycleWhenWaiting(F, LockRace.Pid(cmd), $"select 1 from ai.lecture_notes where id = '{press}' for update",
                $"delete from lectures.lecture_sessions where id = '{id}'");
        });
        Arm();
        var before = await LockRace.DeadlocksAsync(F);

        AppendResult result;
        await using (var scope = F.Services.GetRequiredService<IServiceScopeFactory>().CreateUserScope(a.UserId))
        {
            var row = new SummaryRevisionRow
            {
                SessionId = Guid.Parse(id), Revision = 1, UserId = a.UserId, Kind = "Delta", Status = "Final", BasedOnSeq = 1, SourceFromSeq = 1, SourceToSeq = 1,
                Tldr = "Synthetic final.", Model = "mock-fast", PromptVersion = "test.v1", CreatedAt = F.Clock.GetUtcNow(),
            };
            result = await scope.ServiceProvider.GetRequiredService<SummaryStore>().AppendAsync(row, 0, default, press);
        }
        Assert.True(trap.Fired, "the race window was not reached");
        await using (side!) await side!.Done;

        Assert.Equal(AppendResult.PressGone, result); // the retry re-took the press lock after the delete committed: nothing stored
        await LockRace.AssertDeadlockCountedAsync(F, before);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.summary_revisions where session_id = '{id}'"));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from lectures.lecture_sessions where id = '{id}'"));
    }
}

// ---------------- F-5: a transient deadlock in the budget reservation must not open the breaker ----------------

public sealed class ReserveTrapFactory : ApiFactory
{
    public CommandTrap Trap { get; } = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.ConfigureDbContext<Tether.Ai.Persistence.AiDbContext>(o => o.AddInterceptors(Trap)));
    }
}

public class LockOrderReserveTests(ReserveTrapFactory f) : AiBudgetTestBase(f), IClassFixture<ReserveTrapFactory>
{
    /// <summary>The reservation's conditional global-spend upsert (GlobalSpend.TryAddAsync; AdjustAsync uses greatest() instead).</summary>
    private static bool IsReserveUpsert(string sql) =>
        sql.Contains("insert into ai.global_spend", StringComparison.Ordinal) && !sql.Contains("greatest", StringComparison.Ordinal);

    [Fact]
    public async Task A_reservation_chosen_as_a_deadlock_victim_is_retried_once_and_does_not_open_the_breaker()
    {
        var a = await NewActorAsync();
        var period = F.Clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        await F.ExecAsync($"insert into ai.global_spend (period, pool, spent_micros, updated_at) values ('{period}', 'lecture', 0, now()) on conflict do nothing");
        var trap = ((ReserveTrapFactory)F).Trap;
        OtherSide? side = null;
        // The reservation holds the per-user advisory lock and its upsert waits on the global row; the other side holds the global row and
        // then asks for the same advisory lock. Any 40P01/40001 in the reservation used to call RecordLedgerFault (both lanes open for 60 s).
        trap.Arm(IsReserveUpsert, async cmd =>
        {
            side = await OtherSide.HoldAsync(F, $"update ai.global_spend set updated_at = now() where period = '{period}' and pool = 'lecture'");
            side.CloseCycleWhenWaiting(F, LockRace.Pid(cmd), $"select pg_advisory_xact_lock(hashtextextended('ai-budget:{a.UserId:N}', 0))");
        });
        var before = await LockRace.DeadlocksAsync(F);

        var outcome = await RunGatewayAsync(a);
        Assert.True(trap.Fired, "the race window was not reached");
        await using (side!) await side!.Done;

        Assert.True(outcome.Ok, outcome.Failure);
        await LockRace.AssertDeadlockCountedAsync(F, before);
        var health = F.Services.GetRequiredService<AiHealth>();
        Assert.False(health.IsOpen, "one transient deadlock opened the live breaker");
        Assert.False(health.IsNotesOpen, "one transient deadlock opened the notes breaker");
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and outcome = 'reserved'"));
    }
}

// ---------------- F-8: the shared classifier ----------------

public class PgErrorsTests
{
    private static PostgresException Pg(string state) => new("synthetic", "ERROR", "ERROR", state);

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected, true)]
    [InlineData(PostgresErrorCodes.SerializationFailure, true)]
    [InlineData(PostgresErrorCodes.UniqueViolation, false)]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, false)]
    public void Classifies_the_sql_state_anywhere_in_the_exception_chain(string state, bool expected)
    {
        Assert.Equal(expected, PgErrors.IsDeadlockOrSerialization(Pg(state)));
        Assert.Equal(expected, PgErrors.IsDeadlockOrSerialization(new DbUpdateException("wrapped", Pg(state))));
        Assert.Equal(expected, PgErrors.IsDeadlockOrSerialization(new InvalidOperationException("outer", new DbUpdateException("wrapped", Pg(state)))));
        Assert.Equal(state == PostgresErrorCodes.ForeignKeyViolation, PgErrors.IsForeignKeyViolation(new DbUpdateException("wrapped", Pg(state))));
    }

    [Fact]
    public void Other_exceptions_are_not_retryable() => Assert.False(PgErrors.IsDeadlockOrSerialization(new TimeoutException()));
}
