using System.Net;
using Tether.IntegrationTests.Infrastructure;
using Tether.Lectures.Contracts;
using Tether.Transcript.Relay;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.IntegrationTests;

/// <summary>
/// Deterministic form of the intermittent CI failure of
/// <see cref="RealtimeAudioTests.Deleting_a_streaming_session_closes_the_socket_and_leaves_no_transcript_or_progress_rows"/>
/// (run 41353274348: Expected 4409, Actual 1011). A transcript write and the session delete take the same two locks in opposite
/// order: the writer holds the session counter row (seq allocation) and its FK check on the new transcript row needs KEY SHARE on the
/// session row; the delete holds the session row and its cascade needs the counter row. PostgreSQL aborts one side (40P01). The delete
/// already retries when it is the victim; when the WRITER was the victim the relay treated the deadlock as an internal error and
/// closed 1011, although the only thing that happened is that the session was deleted.
/// </summary>
public class RelayDeleteDeadlockTests(RelayDeleteDeadlockTests.Factory f) : LectureTestBase(f), IClassFixture<RelayDeleteDeadlockTests.Factory>
{
    /// <summary>
    /// Polling off (only the writer or the push can close the socket), a <see cref="CommandTrap"/> on the Transcript DbContext, and a
    /// gate that holds the delete's push until the relay has finished its own close. The gate fixes the order CI saw: the relay's own
    /// reaction to the failed write lands before the push. Without it the push (4409) and the relay's reaction race.
    /// </summary>
    public sealed class Factory : RealtimeFactory
    {
        public CommandTrap Trap { get; } = new();
        public volatile string? HoldDeletePushFor;

        protected override IEnumerable<KeyValuePair<string, string?>> Extra =>
            [new("Realtime:RevalidateSeconds", "3600"), new("Realtime:RelayCheckSeconds", "3600"), new("Realtime:MaxConnectionSeconds", "7200")];

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(s =>
            {
                s.ConfigureDbContext<Tether.Transcript.Persistence.TranscriptDbContext>(o => o.AddInterceptors(Trap));
                // First in the observer list, so it runs before the realtime observer that pushes the close.
                s.Insert(0, ServiceDescriptor.Singleton<ISessionLifecycleObserver>(sp => new HoldDeletePush(sp.GetRequiredService<RelayRegistry>(), this)));
            });
        }
    }

    private sealed class HoldDeletePush(RelayRegistry relays, Factory f) : ISessionLifecycleObserver
    {
        public Task OnStateChangedAsync(SessionStateChange change, CancellationToken ct) => Task.CompletedTask;

        public async Task OnDeletedAsync(Guid userId, Guid sessionId, CancellationToken ct)
        {
            if (f.HoldDeletePushFor != sessionId.ToString()) return;
            await Rt.WaitAsync(() => !relays.Contains(sessionId), 5000); // the relay leaves the registry just before it closes
        }
    }

    /// <summary>The relay's transcript insert (TranscriptWriter.AppendAsync, after the seq allocation took the counter row lock).</summary>
    private static bool IsTranscriptInsert(string sql) =>
        sql.Contains("INSERT INTO", StringComparison.Ordinal) && sql.Contains("transcript_events", StringComparison.Ordinal);

    [Fact]
    public async Task A_transcript_write_chosen_as_the_deadlock_victim_of_a_delete_closes_4409_and_leaves_no_rows()
    {
        var factory = (Factory)F;
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var id = (await StartOkAsync(a.Client, course)).GetProperty("id").GetString()!;
        factory.HoldDeletePushFor = id;
        Task<HttpResponseMessage>? delete = null;

        // At the first transcript insert the writer holds the counter row. Start the delete: it locks the session row and its cascade
        // waits on the writer. Release the insert only after the delete's own deadlock check has run and found no cycle (PostgreSQL
        // checks once, deadlock_timeout after the wait began; pg_locks.waitstart says when). The insert then waits on the session row,
        // closing the cycle, and the writer's check is the one that finds it: the writer is the victim, as in the CI failure.
        factory.Trap.Arm(IsTranscriptInsert, async cmd =>
        {
            var writerPid = ((Npgsql.NpgsqlConnection)cmd.Connection!).ProcessID;
            delete = a.Client.DeleteAsync($"/api/v1/sessions/{id}");
            var past = await Rt.WaitAsync(async () => await F.ScalarAsync(
                $"select count(*) from pg_locks l where not l.granted and {writerPid} = any(pg_blocking_pids(l.pid)) " +
                "and l.waitstart < clock_timestamp() - current_setting('deadlock_timeout')::interval - interval '300 milliseconds'") > 0, 10000);
            Assert.True(past, "the delete never waited on the writer past its deadlock check");
        });

        try
        {
            await using var audio = await Rt.AudioClient.ConnectAsync(F, Rt.CookieOf(a.Client), id);
            var res = await PostAsync(a.Client, $"/sessions/{id}/audio-ticket");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            await audio.HelloAsync((await res.BodyAsync()).GetProperty("ticket").GetString()!);
            var sender = Task.Run(async () => { try { await audio.SendAudioAsync(60); } catch (Exception) { } });

            // The session was deleted: by contract that is 4409 (section 15), whichever side PostgreSQL aborted.
            Assert.Equal(4409, await audio.WaitCloseAsync(15000));
            Assert.True(factory.Trap.Fired, "the race window was not reached");
            Assert.NotNull(delete);
            Assert.Equal(HttpStatusCode.NoContent, (await delete!).StatusCode);
            await sender;
        }
        finally { factory.HoldDeletePushFor = null; }

        Assert.Equal(0, await F.ScalarAsync($"select count(*) from lectures.lecture_sessions where id = '{id}'"));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{id}'"));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from transcript.audio_progress where session_id = '{id}'"));
    }
}
