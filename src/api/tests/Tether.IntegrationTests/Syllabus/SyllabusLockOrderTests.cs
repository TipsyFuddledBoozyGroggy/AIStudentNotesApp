using System.Data.Common;
using System.Net;
using Tether.IntegrationTests.Infrastructure;
using Tether.Syllabus.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.IntegrationTests.Syllabus;

/// <summary>The syllabus module's DbContext with a <see cref="CommandTrap"/>; the extraction worker stays on (tests need ready syllabi).</summary>
public sealed class SyllabusLockOrderFactory : ApiFactory
{
    public CommandTrap Trap { get; } = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.ConfigureDbContext<SyllabusDbContext>(o => o.AddInterceptors(Trap)));
    }
}

/// <summary>
/// docs/data/lock-order-audit.md F-3: the calendar sync and the item create with an Idempotency-Key hold the syllabus row (FOR UPDATE; the sync
/// also its item rows) and then insert a row whose FK references courses.courses (calendar_events, idempotency_keys: KEY SHARE on the course
/// row). A course purge holds the course row and its cascade needs the syllabus row: the opposite order. Method as in LockOrderFixTests: the
/// trap stops the writer after it took the syllabus lock and before its first course-referencing insert; the other side takes the course row
/// FOR UPDATE (what the purge's DELETE holds), waits until the writer blocks on it, then asks for the syllabus row (what the cascade needs).
/// The writer started waiting first, so its deadlock check runs first and it is the victim. No sleeps.
/// </summary>
public class SyllabusLockOrderTests(SyllabusLockOrderFactory f) : SyllabusTestBase(f), IClassFixture<SyllabusLockOrderFactory>
{
    private CommandTrap Trap => ((SyllabusLockOrderFactory)F).Trap;

    private static bool Carries(DbCommand cmd, Guid g) =>
        cmd.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid v ? v == g : p.Value is IEnumerable<Guid> e && e.Contains(g));

    /// <summary>The sync's EF read of the item rows it has just locked (raw SQL is not intercepted; this is the next EF command).</summary>
    private static bool IsLockedItemsRead(string sql) =>
        sql.Contains("FROM syllabus.items", StringComparison.Ordinal) && sql.Contains("= ANY (", StringComparison.Ordinal);

    private static bool IsItemInsert(string sql) => sql.Contains("INSERT INTO syllabus.items", StringComparison.Ordinal);

    /// <summary>Arms the cycle on the first matching command that carries <paramref name="scope"/> (re-armed if another command matches first).
    /// With <paramref name="purge"/> the other side then really deletes the course; without it, it commits nothing and the course survives.</summary>
    private void ArmCourseCycle(Func<string, bool> match, Guid scope, string course, string syllabus, bool purge, List<OtherSide> sides)
    {
        Trap.Arm(match, async cmd =>
        {
            if (!Carries(cmd, scope)) { ArmCourseCycle(match, scope, course, syllabus, purge, sides); return; }
            var side = await OtherSide.HoldAsync(F, $"select 1 from courses.courses where id = '{course}' for update");
            side.CloseCycleWhenWaiting(F, LockRace.Pid(cmd), $"select 1 from syllabus.syllabi where id = '{syllabus}' for update",
                purge ? [$"delete from courses.courses where id = '{course}'"] : []);
            lock (sides) sides.Add(side);
        });
    }

    private async Task<(Actor A, string Course, string Sid, string Item, int Rev)> ApprovedMidtermAsync()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste("Midterm exam: Oct 19, 2026 at 10:00 AM"))).GetProperty("id").GetString()!;
        var res = await Decide(a.Client, sid, ByTitle(await ItemsAsync(a.Client, sid), "Midterm"), "approved");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var ap = await res.BodyAsync();
        return (a, course, sid, ap.GetProperty("id").GetString()!, ap.GetProperty("revision").GetInt32());
    }

    private Task<long> RowsAsync(string table, Guid user, string extra = "true") =>
        F.ScalarAsync($"select count(*) from syllabus.{table} where user_id = '{user}' and {extra}");

    private static async Task AwaitSidesAsync(List<OtherSide> sides)
    {
        Assert.Single(sides); // the race window was reached exactly once
        foreach (var s in sides) await using (s) await s.Done; // the other side was NOT the victim: the writer was
    }

    private static HttpRequestMessage AddItem(string sid, string key) =>
        Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/items", new { kind = "assignment", title = "Lab report", date = "2026-10-02" }, key);

    // ---------------- calendar sync ----------------

    [Fact]
    public async Task A_sync_chosen_as_the_deadlock_victim_of_a_course_purge_is_retried_and_answers_404_not_500()
    {
        var (a, course, sid, item, rev) = await ApprovedMidtermAsync();
        var sides = new List<OtherSide>();
        ArmCourseCycle(IsLockedItemsRead, Guid.Parse(Guid32(item)), course, Guid32(sid), purge: true, sides);
        var before = await LockRace.DeadlocksAsync(F);

        var res = await Sync(a.Client, sid, NewKey(), (item, rev));
        await AwaitSidesAsync(sides);

        await res.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await LockRace.AssertDeadlockCountedAsync(F, before);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from courses.courses where id = '{course}'"));
        foreach (var t in new[] { "syllabi", "items", "calendar_events", "sync_runs", "idempotency_keys" })
            Assert.Equal(0, await RowsAsync(t, a.UserId));
    }

    [Fact]
    public async Task A_sync_that_loses_a_deadlock_while_the_course_survives_is_retried_once_writes_one_event_and_replays_by_key()
    {
        var (a, course, sid, item, rev) = await ApprovedMidtermAsync();
        var sides = new List<OtherSide>();
        ArmCourseCycle(IsLockedItemsRead, Guid.Parse(Guid32(item)), course, Guid32(sid), purge: false, sides);
        var before = await LockRace.DeadlocksAsync(F);
        var key = NewKey();

        var res = await Sync(a.Client, sid, key, (item, rev));
        await AwaitSidesAsync(sides);

        Assert.True(res.StatusCode == HttpStatusCode.OK, $"{(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        var body = await res.BodyAsync();
        Assert.Equal("all_succeeded", body.GetProperty("status").GetString());
        Assert.Equal("created", body.GetProperty("results")[0].GetProperty("outcome").GetString());
        await LockRace.AssertDeadlockCountedAsync(F, before);
        // the aborted attempt left nothing: exactly one event, one run, one key
        Assert.Equal(1, await RowsAsync("calendar_events", a.UserId, $"item_id = '{Guid32(item)}'"));
        Assert.Equal(1, await RowsAsync("sync_runs", a.UserId, "kind = 'sync'"));
        Assert.Equal(1, await RowsAsync("idempotency_keys", a.UserId, "result_kind = 'sync_run'"));
        Assert.Equal("synced", (await GetItemAsync(a.Client, sid, item)).GetProperty("syncState").GetString());

        var again = await Sync(a.Client, sid, key, (item, rev));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("true", again.Headers.TryGetValues("Idempotency-Replayed", out var v) ? v.Single() : null);
        Assert.Equal(body.GetProperty("syncId").GetString(), (await again.BodyAsync()).GetProperty("syncId").GetString());
        Assert.Equal(1, await RowsAsync("calendar_events", a.UserId, $"item_id = '{Guid32(item)}'"));
        Assert.Equal(1, await RowsAsync("sync_runs", a.UserId, "kind = 'sync'"));
    }

    // ---------------- item create with an Idempotency-Key ----------------

    [Fact]
    public async Task An_item_create_with_a_key_chosen_as_the_deadlock_victim_of_a_course_purge_is_retried_and_answers_404_not_500()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste("Midterm exam: Oct 19, 2026 at 10:00 AM"))).GetProperty("id").GetString()!;
        var sides = new List<OtherSide>();
        ArmCourseCycle(IsItemInsert, Guid.Parse(Guid32(sid)), course, Guid32(sid), purge: true, sides);
        var before = await LockRace.DeadlocksAsync(F);

        var res = await a.Client.SendAsync(AddItem(sid, NewKey()));
        await AwaitSidesAsync(sides);

        await res.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await LockRace.AssertDeadlockCountedAsync(F, before);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from courses.courses where id = '{course}'"));
        foreach (var t in new[] { "syllabi", "items", "idempotency_keys" })
            Assert.Equal(0, await RowsAsync(t, a.UserId));
    }

    [Fact]
    public async Task An_item_create_with_a_key_that_loses_a_deadlock_while_the_course_survives_creates_one_item_and_replays_by_key()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste("Midterm exam: Oct 19, 2026 at 10:00 AM"))).GetProperty("id").GetString()!;
        var sides = new List<OtherSide>();
        ArmCourseCycle(IsItemInsert, Guid.Parse(Guid32(sid)), course, Guid32(sid), purge: false, sides);
        var before = await LockRace.DeadlocksAsync(F);
        var key = NewKey();

        var res = await a.Client.SendAsync(AddItem(sid, key));
        await AwaitSidesAsync(sides);

        Assert.True(res.StatusCode == HttpStatusCode.Created, $"{(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        var id = (await res.BodyAsync()).GetProperty("id").GetString()!;
        await LockRace.AssertDeadlockCountedAsync(F, before);
        Assert.Equal(1, await RowsAsync("items", a.UserId, "origin = 'student'"));
        Assert.Equal(1, await RowsAsync("idempotency_keys", a.UserId, "result_kind = 'item'"));

        var again = await a.Client.SendAsync(AddItem(sid, key));
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal("true", again.Headers.TryGetValues("Idempotency-Replayed", out var v) ? v.Single() : null);
        Assert.Equal(id, (await again.BodyAsync()).GetProperty("id").GetString());
        Assert.Equal(1, await RowsAsync("items", a.UserId, "origin = 'student'"));
    }
}
