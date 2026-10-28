using Tether.Ai.Domain;
using Tether.Ai.Persistence;
using Tether.Ai.Provider;
using Tether.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Tether.Ai.Budget;

/// <summary>
/// Global monthly spend counter (design section 10). It spans all users, so it is not an EF entity: an entity would be filtered
/// to the current user, and bypassing the filter is banned in source. Access is a few fixed, parameterised statements. Every write
/// is a single atomic statement, and callers run it inside the same transaction as the ledger row it belongs to (B3-02).
/// </summary>
internal sealed class GlobalSpend(AiDbContext db)
{
    public static string PeriodOf(DateTimeOffset now) => now.UtcDateTime.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    public async Task<long> SpentAsync(string period, CancellationToken ct) =>
        await db.Database.SqlQuery<long>($"select coalesce((select spent_micros from ai.global_spend where period = {period}), 0) as \"Value\"").SingleAsync(ct);

    /// <summary>
    /// Adds <paramref name="micros"/> only if the month's total stays within <paramref name="cap"/>; one statement, so concurrent
    /// callers cannot both pass a stale read. Returns false (nothing added) when the cap would be crossed.
    /// </summary>
    public async Task<bool> TryAddAsync(string period, long micros, long cap, CancellationToken ct) =>
        await db.Database.ExecuteSqlAsync($"""
            insert into ai.global_spend (period, spent_micros, updated_at)
            select {period}::text, {micros}::bigint, now() where {micros}::bigint <= {cap}::bigint
            on conflict (period) do update set spent_micros = ai.global_spend.spent_micros + excluded.spent_micros, updated_at = now()
            where ai.global_spend.spent_micros + excluded.spent_micros <= {cap}::bigint
            """, ct) > 0;

    /// <summary>Adds a signed correction (actual minus reserved) to the counter, never below zero.</summary>
    public Task AdjustAsync(string period, long deltaMicros, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            insert into ai.global_spend (period, spent_micros, updated_at) values ({period}::text, greatest({deltaMicros}::bigint, 0), now())
            on conflict (period) do update set spent_micros = greatest(ai.global_spend.spent_micros + {deltaMicros}::bigint, 0), updated_at = now()
            """, ct);
}

/// <summary>An amount held against the caps before a provider call. It is part of the totals (stored as a <c>reserved</c> ledger row) until settled.</summary>
internal sealed record Reservation(Guid UsageId, Guid UserId, string Period, long EstimateMicro);

/// <summary>
/// Writes and totals the content-free usage ledger. Spend is RESERVED before a provider call and SETTLED after it (B3-02): the
/// reservation is the ledger row itself (outcome <c>reserved</c>, cost = the estimate), so every cap check sees calls that are
/// still in flight. Settling rewrites that row and corrects the global counter in one transaction. If settling keeps failing, the
/// reservation simply stays: spend is over-counted, never lost, and the caller is told so it can fail closed.
/// </summary>
internal sealed class UsageLedger(AiDbContext db, GlobalSpend global, TimeProvider clock, ILogger<UsageLedger> log)
{
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(10);
    private const int SettleAttempts = 3;

    /// <summary>
    /// Records a row that is not a hold: a denied call or a discarded stale delta. Normally zero cost. A failure is logged by type and
    /// never touches other tracked entities (only this row is detached).
    /// </summary>
    public async Task RecordAsync(Guid userId, Guid sessionId, AiWorkflow workflow, string promptVersion, string model, AiTokenUsage usage,
        long costMicro, int latencyMs, string outcome, CancellationToken ct)
    {
        var row = NewRow(userId, sessionId, workflow, promptVersion, model, usage, costMicro, latencyMs, outcome);
        try
        {
            db.Usage.Add(row);
            await db.SaveChangesAsync(ct);
            db.Entry(row).State = EntityState.Detached;
            if (costMicro > 0) await global.AdjustAsync(GlobalSpend.PeriodOf(row.CreatedAt), costMicro, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("AI ledger write failed: {ExceptionType}", ex.GetType().Name);
            db.Entry(row).State = EntityState.Detached;
        }
    }

    /// <summary>Inserts the <c>reserved</c> row. The caller owns the transaction (and the per-user lock).</summary>
    public async Task<Reservation> InsertReservationAsync(Guid userId, Guid sessionId, AiWorkflow workflow, string promptVersion, string model, long estimateMicro, CancellationToken ct)
    {
        var row = NewRow(userId, sessionId, workflow, promptVersion, model, new AiTokenUsage(0, 0, 0), estimateMicro, 0, Outcomes.Reserved);
        db.Usage.Add(row);
        try { await db.SaveChangesAsync(ct); }
        finally { db.Entry(row).State = EntityState.Detached; }
        return new Reservation(row.Id, userId, GlobalSpend.PeriodOf(row.CreatedAt), estimateMicro);
    }

    /// <summary>
    /// Replaces the reservation by the real figures (or by zero, to release it) and corrects the global counter, in one transaction.
    /// Uses its own timeout, not the caller's token: a completed call is paid for even if the host is shutting down. Returns false when
    /// it could not be done (the reservation then stays as the recorded spend).
    /// </summary>
    public async Task<bool> SettleAsync(Reservation r, string model, AiTokenUsage usage, long actualMicro, int latencyMs, string outcome)
    {
        for (var attempt = 1; attempt <= SettleAttempts; attempt++)
        {
            using var cts = new CancellationTokenSource(SettleTimeout);
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(cts.Token);
                var rows = await db.Database.ExecuteSqlAsync($"""
                    update ai.ai_usage set outcome = {outcome}, model = {model}, input_tokens = {usage.InputTokens}, cached_tokens = {usage.CachedTokens},
                        output_tokens = {usage.OutputTokens}, est_cost_micros = {actualMicro}, latency_ms = {latencyMs}
                    where id = {r.UsageId} and user_id = {r.UserId} and outcome = 'reserved'
                    """, cts.Token);
                // 0 rows: already settled, or the user (and so the row) is gone. Then there is nothing to correct either.
                if (rows > 0 && actualMicro != r.EstimateMicro) await global.AdjustAsync(r.Period, actualMicro - r.EstimateMicro, cts.Token);
                await tx.CommitAsync(cts.Token);
                return true;
            }
            catch (Exception ex)
            {
                log.LogWarning("AI ledger settle failed (attempt {Attempt}): {ExceptionType}", attempt, ex.GetType().Name);
            }
        }
        return false;
    }

    public async Task<(long Session, long UserDay)> SpentAsync(Guid sessionId, CancellationToken ct)
    {
        var dayStart = new DateTimeOffset(clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
        var session = await db.Usage.AsNoTracking().Where(u => u.SessionId == sessionId).SumAsync(u => (long?)u.EstCostMicros, ct) ?? 0;
        var day = await db.Usage.AsNoTracking().Where(u => u.CreatedAt >= dayStart).SumAsync(u => (long?)u.EstCostMicros, ct) ?? 0;
        return (session, day);
    }

    private AiUsageRow NewRow(Guid userId, Guid sessionId, AiWorkflow workflow, string promptVersion, string model, AiTokenUsage usage, long costMicro, int latencyMs, string outcome) => new()
    {
        Id = Ids.New(), UserId = userId, SessionId = sessionId, Workflow = workflow.ToString(), PromptVersion = promptVersion, Model = model,
        InputTokens = usage.InputTokens, CachedTokens = usage.CachedTokens, OutputTokens = usage.OutputTokens, EstCostMicros = costMicro,
        LatencyMs = latencyMs, Outcome = outcome, CreatedAt = clock.GetUtcNow(),
    };
}

internal sealed record BudgetCheck(BudgetDecision Decision, double Pressure);

internal sealed record ReserveOutcome(BudgetDecision Decision, Reservation? Reservation);

/// <summary>Reads current spend and applies <see cref="BudgetMath"/>; <see cref="ReserveAsync"/> also holds the amount. Run inside the owning user's scope.</summary>
internal sealed class BudgetGate(AiDbContext db, UsageLedger ledger, GlobalSpend global, AiOptions options, TimeProvider clock)
{
    public async Task<double> PressureAsync(Guid sessionId, CancellationToken ct) => (await SpendAsync(sessionId, ct)).Pressure;

    private async Task<(double Pressure, bool WouldExceed)> SpendAsync(Guid sessionId, CancellationToken ct, long estimateMicro = 0)
    {
        var b = options.Budget;
        var (session, day) = await ledger.SpentAsync(sessionId, ct);
        var month = await global.SpentAsync(GlobalSpend.PeriodOf(clock.GetUtcNow()), ct);
        var pressure = Math.Max(BudgetMath.Pressure(session, b.PerSessionMicro), Math.Max(BudgetMath.Pressure(day, b.PerUserDailyMicro), BudgetMath.Pressure(month, b.GlobalMonthlyMicro)));
        var exceed = session + estimateMicro > b.PerSessionMicro || day + estimateMicro > b.PerUserDailyMicro || month + estimateMicro > b.GlobalMonthlyMicro;
        return (pressure, exceed);
    }

    /// <summary>Read-only advisory check (no hold). Used where no provider call follows immediately.</summary>
    public async Task<BudgetCheck> CheckAsync(Guid sessionId, AiWorkflow workflow, long estimateMicro, CancellationToken ct)
    {
        var (pressure, exceed) = await SpendAsync(sessionId, ct, estimateMicro);
        return new BudgetCheck(BudgetMath.Decide(workflow, pressure, exceed, options.Budget), pressure);
    }

    /// <summary>
    /// Check and hold in one step. A per-user advisory lock serialises the session and user-day caps for that user; the global cap is
    /// enforced by the single conditional statement in <see cref="GlobalSpend.TryAddAsync"/>; the hold (a <c>reserved</c> ledger row)
    /// and the global increment commit together. The reservation is therefore visible to every concurrent caller before any of them
    /// reaches the provider, so N parallel calls against a cap of one call admit exactly one.
    /// </summary>
    public async Task<ReserveOutcome> ReserveAsync(Guid userId, Guid sessionId, AiWorkflow workflow, string promptVersion, string model, long estimateMicro, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"select pg_advisory_xact_lock(hashtextextended({"ai-budget:" + userId.ToString("N")}, 0))", ct);
        var (pressure, exceed) = await SpendAsync(sessionId, ct, estimateMicro);
        var decision = BudgetMath.Decide(workflow, pressure, exceed, options.Budget);
        if (!decision.Allowed) return new ReserveOutcome(decision, null);

        var period = GlobalSpend.PeriodOf(clock.GetUtcNow());
        if (!await global.TryAddAsync(period, estimateMicro, options.Budget.GlobalMonthlyMicro, ct))
            return new ReserveOutcome(new BudgetDecision(false, null, "budget_limited"), null);
        var reservation = await ledger.InsertReservationAsync(userId, sessionId, workflow, promptVersion, model, estimateMicro, ct);
        await tx.CommitAsync(ct);
        return new ReserveOutcome(decision, reservation);
    }
}
