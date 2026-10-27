using Tether.Lectures.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tether.Lectures.Application;

/// <summary>
/// B1-10: removes expired audio tickets (past expiry plus a grace period) and idempotency rows older than the 24 h window.
/// Runs without a user, so it uses set-based raw SQL deletes instead of the per-user query filter. It touches only these two
/// tables; evidence tables are append-only and are never swept here.
/// </summary>
internal sealed class ExpiredRowSweeper(LecturesDbContext db, TimeProvider clock)
{
    public static readonly TimeSpan TicketGrace = TimeSpan.FromMinutes(5);

    public async Task<(int Tickets, int Keys)> SweepAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var ticketCutoff = now - TicketGrace;
        var keyCutoff = now - SessionService.IdempotencyWindow;
        // Raw SQL on purpose: a system job has no user, and the architecture tests ban query-filter bypass and EF bulk
        // operations on owned sets. Only these two non-evidence tables are touched.
        var tickets = await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM lectures.audio_tickets WHERE expires_at < {ticketCutoff}", ct);
        var keys = await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM lectures.idempotency_keys WHERE created_at < {keyCutoff}", ct);
        return (tickets, keys);
    }
}

internal sealed class ExpiredRowSweeperService(IServiceScopeFactory scopes, ILogger<ExpiredRowSweeperService> log, TimeSpan interval) : BackgroundService
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
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ExpiredRowSweeper>().SweepAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning("Lecture sweep failed: {ExceptionType}", ex.GetType().Name); // type only, never row data
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}
