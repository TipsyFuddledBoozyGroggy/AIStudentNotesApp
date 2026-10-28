using Tether.Lectures.Persistence;
using Tether.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tether.Lectures.Application;

/// <summary>
/// Unattended-session limits (C-17 and the 3 h hard cap). The job has no user, so it finds due sessions with a set-based
/// read and then acts per session inside that session owner's scope, through the same state machine as the REST commands
/// (consent event, audit event, seq, ticket invalidation, observers). The timings are the contract defaults and are
/// UNCONFIRMED Product Owner decisions.
/// Not implemented: moving silent sessions to Failed ("abandoned", section 2). It conflicts with the 5 min auto-pause and the
/// 30 min paused auto-stop, so it is raised to the Lead Architect instead of being guessed.
/// </summary>
internal sealed class SessionLifecycleJob(LecturesDbContext db, IServiceScopeFactory scopes, SessionActivityTracker activity, LectureLimits limits, TimeProvider clock)
{
    public const string ReasonAutoPause = "auto_pause_no_audio";
    public const string ReasonAutoStop = "auto_stop_paused_timeout";
    public const string ReasonHardCap = "hard_cap";

    internal sealed class DueRow
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string State { get; set; } = "";
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset LastEventAt { get; set; }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // Raw read on purpose: a system job has no user, and the query-filter bypass is banned in source.
        var live = await db.Database.SqlQuery<DueRow>($"""
            SELECT s.id AS "Id", s.user_id AS "UserId", s.state AS "State", s.started_at AS "StartedAt",
                   COALESCE((SELECT max(e.occurred_at) FROM lectures.session_events e WHERE e.session_id = s.id), s.started_at) AS "LastEventAt"
            FROM lectures.lecture_sessions s WHERE s.state IN ('Active','Paused')
            """).ToListAsync(ct);

        var acted = 0;
        foreach (var row in live)
        {
            (Transition, string)? action = null;
            if (now - row.StartedAt >= limits.HardCap) action = (Transition.Stop, ReasonHardCap);
            else if (row.State == "Paused" && now - row.LastEventAt >= limits.AutoStopAfterPaused) action = (Transition.Stop, ReasonAutoStop);
            else if (row.State == "Active")
            {
                var last = activity.LastActivity(row.Id) is { } t && t > row.LastEventAt ? t : row.LastEventAt;
                if (now - last >= limits.AutoPauseAfterNoActivity) action = (Transition.Pause, ReasonAutoPause);
            }
            if (action is not { } a) continue;

            await using var scope = scopes.CreateUserScope(row.UserId);
            var outcome = await scope.ServiceProvider.GetRequiredService<SessionService>().TransitionAsync(row.Id, a.Item1, ct, a.Item2);
            if (outcome.Row is not null) acted++;
        }
        return acted;
    }
}

internal sealed class SessionLifecycleService(IServiceScopeFactory scopes, ILogger<SessionLifecycleService> log, TimeSpan interval) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<SessionLifecycleJob>().RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning("Lecture lifecycle job failed: {ExceptionType}", ex.GetType().Name); // type only
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}
