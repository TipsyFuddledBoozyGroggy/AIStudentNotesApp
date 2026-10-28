using Tether.Ai.Domain;
using Tether.Ai.Provider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tether.Ai.Persistence;

namespace Tether.Ai.Budget;

internal sealed record ReaperOptions(TimeSpan Interval, TimeSpan Grace)
{
    public static ReaperOptions Default { get; } = new(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3));
}

/// <summary>
/// Settles <c>reserved</c> ledger rows that nothing will ever settle (N-02): the process crashed or was killed between the reservation
/// and the settle, or the host shut down at the wrong moment. Such a row keeps its estimate inside the session, user-day and global
/// totals, which silently eats real budget. The reaper settles it to zero spend (<c>error</c>, cost 0) and gives the estimate back to
/// the global counter, exactly like a call that failed before reaching the vendor, using the same atomic <see cref="UsageLedger.SettleAsync"/>.
///
/// Two triggers. At STARTUP every reservation is stale: the in-memory queues and the calls they belonged to died with the previous
/// process, and there is a single instance (ADR-020d). Afterwards, every <see cref="ReaperOptions.Interval"/>, rows older than the
/// longest provider call timeout plus <see cref="ReaperOptions.Grace"/> (a call cannot still be running after that). A call that is
/// somehow still running and settles later finds its row already settled (<c>where outcome = 'reserved'</c>) and changes nothing, so
/// nothing is released twice. With more than one instance the startup sweep would release another instance's live holds: do not scale
/// out without replacing it (ADR-020d).
/// </summary>
internal sealed class ReservationReaper(IServiceScopeFactory scopes, AiOptions options, TimeProvider clock, ILogger<ReservationReaper> log) : BackgroundService
{
    private const int BatchSize = 200, MaxBatchesPerSweep = 25;

    private ReaperOptions Cfg => options.Reaper ?? ReaperOptions.Default;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SweepSafelyAsync(startup: true, stoppingToken);
            using var timer = new PeriodicTimer(Cfg.Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken)) await SweepSafelyAsync(startup: false, stoppingToken);
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private async Task SweepSafelyAsync(bool startup, CancellationToken ct)
    {
        try { await SweepAsync(startup, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("AI reservation sweep failed: {ExceptionType}", ex.GetType().Name); // best effort; the next tick retries
        }
    }

    /// <summary>Settles stale reservations to zero and returns how many it processed. <paramref name="startup"/> treats every reservation as stale.</summary>
    public async Task<int> SweepAsync(bool startup, CancellationToken ct)
    {
        var maxCall = options.CallTimeout > options.CatchUp.Timeout ? options.CallTimeout : options.CatchUp.Timeout;
        var cutoff = startup ? clock.GetUtcNow().AddDays(1) : clock.GetUtcNow() - (maxCall + Cfg.Grace);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<UsageLedger>();
        var total = 0;
        for (var batch = 0; batch < MaxBatchesPerSweep; batch++)
        {
            // Fixed, parameterised statement across users (like GlobalSpend); no entity, so no per-user filter is bypassed.
            var stale = await db.Database.SqlQuery<StaleRow>($"""
                select id as "Id", user_id as "UserId", est_cost_micros as "EstCostMicros", model as "Model",
                       to_char(created_at at time zone 'UTC', 'YYYY-MM') as "Period"
                from ai.ai_usage where outcome = 'reserved' and created_at < {cutoff} order by created_at limit {BatchSize}
                """).ToListAsync(ct);
            if (stale.Count == 0) break;

            var settled = 0;
            foreach (var r in stale)
                if (await ledger.SettleAsync(new Reservation(r.Id, r.UserId, r.Period, r.EstCostMicros), r.Model, new AiTokenUsage(0, 0, 0), 0, 0, Outcomes.Error))
                    settled++;
            total += settled;
            if (settled == 0 || stale.Count < BatchSize) break; // nothing progressed (ledger faults): try again next tick
        }
        if (total > 0) log.LogWarning("Released {Count} stale AI budget reservations ({Kind})", total, startup ? "startup" : "periodic");
        return total;
    }

    internal sealed class StaleRow
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public long EstCostMicros { get; set; }
        public string Model { get; set; } = "";
        public string Period { get; set; } = "";
    }
}
