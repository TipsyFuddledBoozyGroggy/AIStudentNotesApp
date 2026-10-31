using Tether.Ai.Domain;
using Tether.Ai.Persistence;
using Tether.Ai.Provider;
using Tether.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Tether.Ai.Budget;

/// <summary>
/// Global monthly spend counter (design section 10), one row per (period, pool): lecture and syllabus spend have separate monthly
/// ceilings. It spans all users, so it is not an EF entity: an entity would be filtered
/// to the current user, and bypassing the filter is banned in source. Access is a few fixed, parameterised statements. Every write
/// is a single atomic statement, and callers run it inside the same transaction as the ledger row it belongs to (B3-02).
/// </summary>
internal sealed class GlobalSpend(AiDbContext db)
{
    public static string PeriodOf(DateTimeOffset now) => now.UtcDateTime.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    public async Task<long> SpentAsync(string period, string pool, CancellationToken ct) =>
        await db.Database.SqlQuery<long>($"select coalesce((select spent_micros from ai.global_spend where period = {period} and pool = {pool}), 0) as \"Value\"").SingleAsync(ct);

    /// <summary>
    /// Adds <paramref name="micros"/> only if the pool's month total stays within <paramref name="cap"/>; one statement, so concurrent
    /// callers cannot both pass a stale read. Returns false (nothing added) when the cap would be crossed.
    /// </summary>
    public async Task<bool> TryAddAsync(string period, string pool, long micros, long cap, CancellationToken ct) =>
        await db.Database.ExecuteSqlAsync($"""
            insert into ai.global_spend (period, pool, spent_micros, updated_at)
            select {period}::text, {pool}::text, {micros}::bigint, now() where {micros}::bigint <= {cap}::bigint
            on conflict (period, pool) do update set spent_micros = ai.global_spend.spent_micros + excluded.spent_micros, updated_at = now()
            where ai.global_spend.spent_micros + excluded.spent_micros <= {cap}::bigint
            """, ct) > 0;

    /// <summary>Adds a signed correction (actual minus reserved) to the pool's counter, never below zero.</summary>
    public Task AdjustAsync(string period, string pool, long deltaMicros, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            insert into ai.global_spend (period, pool, spent_micros, updated_at) values ({period}::text, {pool}::text, greatest({deltaMicros}::bigint, 0), now())
            on conflict (period, pool) do update set spent_micros = greatest(ai.global_spend.spent_micros + {deltaMicros}::bigint, 0), updated_at = now()
            """, ct);
}

/// <summary>An amount held against the caps before a provider call. It is part of the totals (stored as a <c>reserved</c> ledger row) until settled.</summary>
internal sealed record Reservation(Guid UsageId, Guid UserId, string Period, long EstimateMicro, string Pool = Tether.SharedKernel.Ai.AiScopeKinds.Lecture);
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
    public async Task RecordAsync(Guid userId, AiScope scope, AiWorkflow workflow, string promptVersion, string model, AiTokenUsage usage,
        long costMicro, int latencyMs, string outcome, CancellationToken ct, Guid? pressId = null)
    {
        var row = NewRow(userId, scope, workflow, promptVersion, model, usage, costMicro, latencyMs, outcome, pressId);
        try
        {
            db.Usage.Add(row);
            await db.SaveChangesAsync(ct);
            db.Entry(row).State = EntityState.Detached;
            if (costMicro > 0) await global.AdjustAsync(GlobalSpend.PeriodOf(row.CreatedAt), row.Pool, costMicro, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("AI ledger write failed: {ExceptionType}", ex.GetType().Name);
            db.Entry(row).State = EntityState.Detached;
        }
    }

    /// <summary>Inserts the <c>reserved</c> row. The caller owns the transaction (and the per-user lock).</summary>
    public async Task<Reservation> InsertReservationAsync(Guid userId, AiScope scope, AiWorkflow workflow, string promptVersion, string model, long estimateMicro, CancellationToken ct, Guid? pressId = null)
    {
        var row = NewRow(userId, scope, workflow, promptVersion, model, new AiTokenUsage(0, 0, 0), estimateMicro, 0, Outcomes.Reserved, pressId);
        db.Usage.Add(row);
        try { await db.SaveChangesAsync(ct); }
        finally { db.Entry(row).State = EntityState.Detached; }
        return new Reservation(row.Id, userId, GlobalSpend.PeriodOf(row.CreatedAt), estimateMicro, row.Pool);
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
                if (rows > 0 && actualMicro != r.EstimateMicro) await global.AdjustAsync(r.Period, r.Pool, actualMicro - r.EstimateMicro, cts.Token);
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

    /// <summary>Spend of one scope (a session or a document job) and the user's spend today in the scope's pool. Pools never see each other's rows.</summary>
    public async Task<(long Scope, long UserDay)> SpentAsync(AiScope scope, CancellationToken ct)
    {
        var dayStart = new DateTimeOffset(clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
        var kind = scope.Kind; var id = scope.Id; var pool = scope.Pool;
        var spent = await db.Usage.AsNoTracking().Where(u => u.ScopeKind == kind && u.ScopeId == id).SumAsync(u => (long?)u.EstCostMicros, ct) ?? 0;
        var day = await db.Usage.AsNoTracking().Where(u => u.Pool == pool && u.CreatedAt >= dayStart).SumAsync(u => (long?)u.EstCostMicros, ct) ?? 0;
        return (spent, day);
    }

    public Task<(long Scope, long UserDay)> SpentAsync(Guid sessionId, CancellationToken ct) => SpentAsync(AiScope.Lecture(sessionId), ct);

    /// <summary>NAS-02: today's spend of the user's after-Stop workflows (Notes + FinalSummary), the notes sub-cap's counter. Holds included.</summary>
    public async Task<long> NotesSpentTodayAsync(CancellationToken ct)
    {
        var dayStart = new DateTimeOffset(clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
        string notes = nameof(AiWorkflow.Notes), final = nameof(AiWorkflow.FinalSummary), pool = Tether.SharedKernel.Ai.AiScopeKinds.Lecture;
        return await db.Usage.AsNoTracking().Where(u => u.Pool == pool && u.CreatedAt >= dayStart && (u.Workflow == notes || u.Workflow == final))
            .SumAsync(u => (long?)u.EstCostMicros, ct) ?? 0;
    }

    public Task RecordAsync(Guid userId, Guid sessionId, AiWorkflow workflow, string promptVersion, string model, AiTokenUsage usage,
        long costMicro, int latencyMs, string outcome, CancellationToken ct, Guid? pressId = null) =>
        RecordAsync(userId, AiScope.Lecture(sessionId), workflow, promptVersion, model, usage, costMicro, latencyMs, outcome, ct, pressId);

    public Task<Reservation> InsertReservationAsync(Guid userId, Guid sessionId, AiWorkflow workflow, string promptVersion, string model, long estimateMicro, CancellationToken ct) =>
        InsertReservationAsync(userId, AiScope.Lecture(sessionId), workflow, promptVersion, model, estimateMicro, ct);

    // Lecture rows dual-write session_id = scope_id (the legacy key, ck_ai_usage_scope_session); document rows have no session.
    // NAS-08: the after-Stop workflows carry the press that caused them, so each vendor call can be linked to its notes.requested audit row.
    private AiUsageRow NewRow(Guid userId, AiScope scope, AiWorkflow workflow, string promptVersion, string model, AiTokenUsage usage, long costMicro, int latencyMs, string outcome, Guid? pressId) => new()
    {
        Id = Ids.New(), UserId = userId, SessionId = scope.IsLecture ? scope.Id : null, ScopeKind = scope.Kind, ScopeId = scope.Id, Pool = scope.Pool,
        PressId = workflow is AiWorkflow.Notes or AiWorkflow.FinalSummary ? pressId : null,
        Workflow = workflow.ToString(), PromptVersion = promptVersion, Model = model,
        InputTokens = usage.InputTokens, CachedTokens = usage.CachedTokens, OutputTokens = usage.OutputTokens, EstCostMicros = costMicro,
        LatencyMs = latencyMs, Outcome = outcome, CreatedAt = clock.GetUtcNow(),
    };
}

internal sealed record BudgetCheck(BudgetDecision Decision, double Pressure);

internal sealed record ReserveOutcome(BudgetDecision Decision, Reservation? Reservation);

/// <summary>Reads current spend and applies <see cref="BudgetMath"/>; <see cref="ReserveAsync"/> also holds the amount. Run inside the owning user's scope.</summary>
internal sealed class BudgetGate(AiDbContext db, UsageLedger ledger, GlobalSpend global, AiOptions options, TimeProvider clock)
{
    public Task<double> PressureAsync(Guid sessionId, CancellationToken ct) => PressureAsync(AiScope.Lecture(sessionId), ct);

    public async Task<double> PressureAsync(AiScope scope, CancellationToken ct) => (await SpendAsync(scope, AiWorkflow.RollingSummary, ct)).Pressure;

    /// <summary>
    /// Highest pressure and whether the estimate would cross a cap, each measured against the SCOPE'S OWN POOL (per scope, per user-day, per month).
    /// NAS-02: the after-Stop workflows also face the notes sub-cap and may not use the live floors (<see cref="BudgetMath.NotesWouldExceed"/>).
    /// </summary>
    private async Task<(double Pressure, bool WouldExceed)> SpendAsync(AiScope scope, AiWorkflow workflow, CancellationToken ct, long estimateMicro = 0)
    {
        var caps = options.Budget.For(scope.Pool);
        var (spent, day) = await ledger.SpentAsync(scope, ct);
        var month = await global.SpentAsync(GlobalSpend.PeriodOf(clock.GetUtcNow()), scope.Pool, ct);
        var pressure = Math.Max(BudgetMath.Pressure(spent, caps.PerScopeMicro), Math.Max(BudgetMath.Pressure(day, caps.PerUserDailyMicro), BudgetMath.Pressure(month, caps.GlobalMonthlyMicro)));
        var exceed = spent + estimateMicro > caps.PerScopeMicro || day + estimateMicro > caps.PerUserDailyMicro || month + estimateMicro > caps.GlobalMonthlyMicro;
        if (!exceed && BudgetMath.IsNotesWorkflow(workflow))
            exceed = BudgetMath.NotesWouldExceed(await ledger.NotesSpentTodayAsync(ct), day, month, estimateMicro, options.Budget, options.Notes);
        return (pressure, exceed);
    }

    /// <summary>Read-only advisory check (no hold). Used where no provider call follows immediately.</summary>
    public Task<BudgetCheck> CheckAsync(Guid sessionId, AiWorkflow workflow, long estimateMicro, CancellationToken ct) =>
        CheckAsync(AiScope.Lecture(sessionId), workflow, estimateMicro, ct);

    public async Task<BudgetCheck> CheckAsync(AiScope scope, AiWorkflow workflow, long estimateMicro, CancellationToken ct)
    {
        var (pressure, exceed) = await SpendAsync(scope, workflow, ct, estimateMicro);
        return new BudgetCheck(BudgetMath.Decide(workflow, pressure, exceed, options.Budget), pressure);
    }

    public Task<ReserveOutcome> ReserveAsync(Guid userId, Guid sessionId, AiWorkflow workflow, string promptVersion, string model, long estimateMicro, CancellationToken ct) =>
        ReserveAsync(userId, AiScope.Lecture(sessionId), workflow, promptVersion, model, estimateMicro, ct);

    /// <summary>
    /// Check and hold in one step. A per-user advisory lock serialises the scope and user-day caps for that user (all pools: one lock per
    /// user is simpler and the critical section is short); the global cap is
    /// enforced by the single conditional statement in <see cref="GlobalSpend.TryAddAsync"/>; the hold (a <c>reserved</c> ledger row)
    /// and the global increment commit together. The reservation is therefore visible to every concurrent caller before any of them
    /// reaches the provider, so N parallel calls against a cap of one call admit exactly one.
    /// </summary>
    public async Task<ReserveOutcome> ReserveAsync(Guid userId, AiScope scope, AiWorkflow workflow, string promptVersion, string model, long estimateMicro, CancellationToken ct, Guid? pressId = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"select pg_advisory_xact_lock(hashtextextended({"ai-budget:" + userId.ToString("N")}, 0))", ct);
        var (pressure, exceed) = await SpendAsync(scope, workflow, ct, estimateMicro);
        var decision = BudgetMath.Decide(workflow, pressure, exceed, options.Budget);
        if (!decision.Allowed) return new ReserveOutcome(decision, null);

        var period = GlobalSpend.PeriodOf(clock.GetUtcNow());
        // NAS-02: the atomic global check for notes stops below the month cap by the live floor, so concurrent notes can never eat it.
        var monthCap = options.Budget.For(scope.Pool).GlobalMonthlyMicro;
        if (BudgetMath.IsNotesWorkflow(workflow)) monthCap -= options.Notes.LiveFloorGlobalMonthlyMicro;
        if (!await global.TryAddAsync(period, scope.Pool, estimateMicro, monthCap, ct))
            return new ReserveOutcome(new BudgetDecision(false, null, "budget_limited"), null);
        var reservation = await ledger.InsertReservationAsync(userId, scope, workflow, promptVersion, model, estimateMicro, ct, pressId);
        await tx.CommitAsync(ct);
        return new ReserveOutcome(decision, reservation);
    }
}
