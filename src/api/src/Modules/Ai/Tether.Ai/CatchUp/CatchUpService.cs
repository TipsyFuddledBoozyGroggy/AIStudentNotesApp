using System.Collections.Concurrent;
using System.Threading.Channels;
using Tether.Ai.Budget;
using Tether.Ai.Domain;
using Tether.Ai.Persistence;
using Tether.Ai.Provider;
using Tether.Ai.Validation;
using Tether.Lectures.Contracts;
using Tether.SharedKernel;
using Tether.Transcript.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Tether.Ai.CatchUp;

internal enum StartKind { Ok, NotFound, InvalidState, RateLimited, KeyConflict }

internal sealed record StartResult(StartKind Kind, string? Json = null, bool Replayed = false);

internal sealed record CatchUpJob(Guid UserId, Guid SessionId, Guid CatchUpId);

/// <summary>Bounded hand-off to the catch-up lane. A full queue refuses the job (the student keeps the extractive view); it never blocks the request.</summary>
internal sealed class CatchUpQueue(AiOptions options)
{
    public Channel<CatchUpJob> Channel { get; } = System.Threading.Channels.Channel.CreateBounded<CatchUpJob>(
        new BoundedChannelOptions(Math.Max(1, options.CatchUp.QueueCapacity)) { FullMode = BoundedChannelFullMode.Wait, SingleReader = false });

    public bool TryEnqueue(CatchUpJob job) => Channel.Writer.TryWrite(job);
}

/// <summary>Per-session sliding-minute limiter for catch-up requests (SEC-036). In-process, like the other realtime limiters (ADR-020d).</summary>
internal sealed class CatchUpLimiter(AiOptions options, TimeProvider clock)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<Guid, List<DateTimeOffset>> _hits = new();
    private long _nextSweepTicks;

    public int TrackedSessions => _hits.Count;

    /// <summary>Gives back the newest token: the request that took it ended up sharing another request's catch-up and made no paid call of its own.</summary>
    public void Release(Guid sessionId)
    {
        if (!_hits.TryGetValue(sessionId, out var q)) return;
        lock (q) { if (q.Count > 0) q.RemoveAt(q.Count - 1); }
    }

    public bool TryAcquire(Guid sessionId)
    {
        var now = clock.GetUtcNow();
        Sweep(now);
        var q = _hits.GetOrAdd(sessionId, _ => new List<DateTimeOffset>());
        lock (q)
        {
            while (q.Count > 0 && now - q[0] >= Window) q.RemoveAt(0);
            if (q.Count >= options.CatchUp.PerMinutePerSession) return false;
            q.Add(now);
            return true;
        }
    }

    /// <summary>Drops sessions with no hit inside the window (B3-05c), at most once per window, so the map is bounded by recent activity and not by every session ever used.</summary>
    private void Sweep(DateTimeOffset now)
    {
        var next = Interlocked.Read(ref _nextSweepTicks);
        if (now.UtcTicks < next || Interlocked.CompareExchange(ref _nextSweepTicks, (now + Window).UtcTicks, next) != next) return;
        foreach (var (id, q) in _hits)
        {
            lock (q)
            {
                while (q.Count > 0 && now - q[0] >= Window) q.RemoveAt(0);
                if (q.Count == 0) _hits.TryRemove(new KeyValuePair<Guid, List<DateTimeOffset>>(id, q));
            }
        }
    }
}

/// <summary>Moves a catch-up that will never get its AI version to <c>Unavailable</c> so a client waiting for it stops waiting (B3-04).</summary>
internal static class CatchUpStates
{
    public static string WithAiState(string resultJson, string aiState)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(resultJson)!.AsObject();
        node["aiState"] = aiState;
        return node.ToJsonString();
    }

    /// <summary>Only a row that is still <c>Pending</c> is changed (a result that arrived meanwhile wins). Runs inside the owning user's scope.</summary>
    public static async Task ExpireAsync(AiDbContext db, CatchUpRow row, DateTimeOffset now, CancellationToken ct)
    {
        var json = WithAiState(row.ResultJson, "Unavailable");
        // Fixed parameterised statement, explicitly owner-scoped (bulk ExecuteUpdate is banned outside identity.sessions).
        await db.Database.ExecuteSqlAsync($"update ai.catch_ups set ai_state = 'Unavailable', result = {json}::jsonb, updated_at = {now} where id = {row.Id} and user_id = {row.UserId} and ai_state = 'Pending'", ct);
    }
}

/// <summary>Handles <c>POST /quick-recap</c>: builds the extractive answer immediately and queues the AI version (never awaited).</summary>
internal sealed class CatchUpService(
    AiDbContext db, ITranscriptSegmentReader reader, ISessionDirectory directory, BudgetGate budget, AiHealth health, AiOptions options,
    CatchUpQueue queue, CatchUpLimiter limiter, TimeProvider clock, ILogger<CatchUpService> log)
{
    private const int MaxWindowSegments = 400;
    public const int MaxPerSession = 200;

    public async Task<StartResult> StartAsync(Guid userId, Guid sessionId, int windowMinutes, string? idempotencyKey, CancellationToken ct)
    {
        var session = await directory.FindAsync(userId, sessionId, ct);
        if (session is null) return new(StartKind.NotFound);
        if (session.State is not (LectureState.Active or LectureState.Paused)) return new(StartKind.InvalidState);

        await ExpireStalePendingAsync(sessionId, ct); // a lost job must not block this session or be replayed as "Pending" forever (B3-04)

        if (idempotencyKey is not null)
        {
            var prior = await db.CatchUps.AsNoTracking().FirstOrDefaultAsync(c => c.SessionId == sessionId && c.IdempotencyKey == idempotencyKey, ct);
            if (prior is not null) return prior.WindowMinutes == windowMinutes ? new(StartKind.Ok, prior.ResultJson, Replayed: true) : new(StartKind.KeyConflict);
        }

        var now = clock.GetUtcNow();
        var since = now - options.CatchUp.CoalesceWindow;
        var inFlight = await db.CatchUps.AsNoTracking()
            .Where(c => c.SessionId == sessionId && c.WindowMinutes == windowMinutes && c.AiState == "Pending" && c.CreatedAt > since)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
        if (inFlight is not null) return new(StartKind.Ok, inFlight.ResultJson); // double click: one catch-up, one charge

        if (!limiter.TryAcquire(sessionId) || await db.CatchUps.AsNoTracking().CountAsync(c => c.SessionId == sessionId, ct) >= MaxPerSession)
        {
            // A burst of parallel clicks on the same window is not abuse: they share the in-flight catch-up (no new call, no new charge).
            if (idempotencyKey is not null
                && await db.CatchUps.AsNoTracking().FirstOrDefaultAsync(c => c.SessionId == sessionId && c.IdempotencyKey == idempotencyKey, ct) is { } keyed)
                return keyed.WindowMinutes == windowMinutes ? new(StartKind.Ok, keyed.ResultJson, Replayed: true) : new(StartKind.KeyConflict);
            var running = await db.CatchUps.AsNoTracking().Where(c => c.SessionId == sessionId && c.WindowMinutes == windowMinutes && c.AiState == "Pending")
                .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
            return running is null ? new(StartKind.RateLimited) : new(StartKind.Ok, running.ResultJson);
        }

        var latestEnd = await reader.LatestEndMsAsync(userId, sessionId, ct) ?? 0;
        var (from, to) = CatchUpWindow.Bounds(latestEnd, windowMinutes);
        var slice = await reader.ReadWindowAsync(userId, sessionId, from, to, MaxWindowSegments, ct);

        string aiState;
        if (slice.Segments.Count == 0) aiState = "NotNeeded";
        else if (!options.Enabled || health.IsOpen || session.State != LectureState.Active) aiState = "Unavailable"; // paused: the extractive answer only, no text to the vendor (B3-06)
        else aiState = (await budget.CheckAsync(sessionId, AiWorkflow.QuickRecap, 0, ct)).Decision.Allowed ? "Pending" : "BudgetLimited";

        var id = Ids.New();
        var wire = CatchUpBuilder.Extractive(id, windowMinutes, from, to, slice.Segments, aiState);
        var row = new CatchUpRow
        {
            Id = id, SessionId = sessionId, UserId = userId, WindowMinutes = windowMinutes, Kind = "Extractive", AiState = aiState, FromMs = from, ToMs = to,
            ResultJson = CatchUpJson.Write(wire), IdempotencyKey = idempotencyKey, CreatedAt = now, UpdatedAt = now,
        };
        db.CatchUps.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            limiter.Release(sessionId);
            db.ChangeTracker.Clear();
            // A concurrent request with the same key won the insert: return its row.
            if (idempotencyKey is not null
                && await db.CatchUps.AsNoTracking().FirstOrDefaultAsync(c => c.SessionId == sessionId && c.IdempotencyKey == idempotencyKey, ct) is { } winner)
                return winner.WindowMinutes == windowMinutes ? new(StartKind.Ok, winner.ResultJson, Replayed: true) : new(StartKind.KeyConflict);
            // Otherwise a parallel request without that key created the in-flight catch-up for this window first (one per session and
            // window, enforced by ux_catch_ups_pending, B3-05a): hand back that one instead of creating a second paid call.
            var existing = await db.CatchUps.AsNoTracking().Where(c => c.SessionId == sessionId && c.WindowMinutes == windowMinutes)
                .OrderByDescending(c => c.AiState == "Pending").ThenByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
            return existing is null ? new(StartKind.RateLimited) : new(StartKind.Ok, existing.ResultJson);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            db.ChangeTracker.Clear();
            return new(StartKind.NotFound); // the session was deleted between the lookup and the insert
        }

        if (aiState == "Pending" && !queue.TryEnqueue(new CatchUpJob(userId, sessionId, id)))
        {
            log.LogWarning("Catch-up queue full; serving extractive only");
            row.AiState = "Unavailable";
            row.ResultJson = CatchUpJson.Write(wire with { AiState = "Unavailable" });
            await db.SaveChangesAsync(ct);
        }
        return new(StartKind.Ok, row.ResultJson);
    }

    public async Task<CatchUpRow?> GetAsync(Guid sessionId, Guid catchUpId, CancellationToken ct)
    {
        var row = await db.CatchUps.AsNoTracking().FirstOrDefaultAsync(c => c.Id == catchUpId && c.SessionId == sessionId, ct);
        if (row is not null && IsStale(row))
        {
            await CatchUpStates.ExpireAsync(db, row, clock.GetUtcNow(), ct);
            row = await db.CatchUps.AsNoTracking().FirstOrDefaultAsync(c => c.Id == catchUpId && c.SessionId == sessionId, ct); // it may have finished meanwhile
        }
        return row;
    }

    private bool IsStale(CatchUpRow r) => r.AiState == "Pending" && clock.GetUtcNow() - r.CreatedAt > options.CatchUp.StaleAfter;

    private async Task ExpireStalePendingAsync(Guid sessionId, CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow() - options.CatchUp.StaleAfter;
        var stale = await db.CatchUps.AsNoTracking().Where(c => c.SessionId == sessionId && c.AiState == "Pending" && c.CreatedAt < cutoff).ToListAsync(ct);
        foreach (var r in stale) await CatchUpStates.ExpireAsync(db, r, clock.GetUtcNow(), ct);
    }
}

/// <summary>
/// The catch-up lane: its own bounded queue and its own workers, so a slow rolling summary can never delay a student who just clicked
/// Quick recap (and a slow catch-up never delays summaries). Each job re-reads the window from the stored transcript.
/// </summary>
internal sealed class CatchUpRunner(CatchUpQueue queue, IServiceScopeFactory scopes, AiOptions options, TimeProvider clock, ILogger<CatchUpRunner> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SweepOrphansAsync(stoppingToken);
        await Task.WhenAll(Enumerable.Range(0, Math.Max(1, options.CatchUp.Workers)).Select(_ => WorkAsync(stoppingToken)));
    }

    /// <summary>
    /// The job queue is in memory, so at startup every catch-up still <c>Pending</c> lost its job in the previous process (a single
    /// instance, ADR-020d). They become <c>Unavailable</c> (the extractive text stays) so no client waits for an AI version that will
    /// never come (B3-04). One fixed statement across users, like <see cref="GlobalSpend"/>; best effort.
    /// </summary>
    public async Task<int> SweepOrphansAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
            var now = clock.GetUtcNow();
            var n = await db.Database.ExecuteSqlAsync($"""
                update ai.catch_ups set ai_state = 'Unavailable', result = jsonb_set(result, array['aiState'], to_jsonb('Unavailable'::text)), updated_at = {now}
                where ai_state = 'Pending'
                """, ct);
            if (n > 0) log.LogWarning("Marked {Count} orphaned catch-ups unavailable at startup", n);
            return n;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("Catch-up orphan sweep failed: {ExceptionType}", ex.GetType().Name);
            return 0;
        }
    }

    private async Task WorkAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var job in queue.Channel.Reader.ReadAllAsync(ct))
            {
                try { await ProcessAsync(job, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { await MarkUnavailableAsync(job); throw; }
                catch (Exception ex)
                {
                    log.LogWarning("Catch-up job failed: {ExceptionType}", ex.GetType().Name);
                    await MarkUnavailableAsync(job);
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    /// <summary>Best effort, and not tied to the (possibly cancelled) host token: the student's row must not stay Pending after a failed job.</summary>
    private async Task MarkUnavailableAsync(CatchUpJob job)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var scope = scopes.CreateUserScope(job.UserId);
            var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
            var row = await db.CatchUps.AsNoTracking().FirstOrDefaultAsync(c => c.Id == job.CatchUpId && c.SessionId == job.SessionId, cts.Token);
            if (row is { AiState: "Pending" }) await CatchUpStates.ExpireAsync(db, row, clock.GetUtcNow(), cts.Token);
        }
        catch (Exception ex)
        {
            log.LogWarning("Could not mark catch-up unavailable: {ExceptionType}", ex.GetType().Name);
        }
    }

    public async Task ProcessAsync(CatchUpJob job, CancellationToken ct)
    {
        await using var scope = scopes.CreateUserScope(job.UserId);
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AiDbContext>();
        var row = await db.CatchUps.FirstOrDefaultAsync(c => c.Id == job.CatchUpId && c.SessionId == job.SessionId, ct);
        if (row is null || row.Kind == "Ai" || row.AiState != "Pending") return; // deleted meanwhile, already upgraded, or already declared unavailable

        var reader = sp.GetRequiredService<ITranscriptSegmentReader>();
        var slice = await reader.ReadWindowAsync(job.UserId, job.SessionId, row.FromMs, row.ToMs, 400, ct);
        var built = CatchUpRequests.Build(job.UserId, job.SessionId, job.CatchUpId, row.WindowMinutes, slice.Segments, options.CatchUp);
        var outcome = await sp.GetRequiredService<AiGateway>().RunAsync(built.Request, json => LanguageGuard.Apply(CatchUpValidator.Validate(json, built.Context), r => LanguageGuard.Texts(r, built.Context), options.LanguageGuardEnforce, log, "QuickRecap"), ct);

        CatchUpWire wire;
        if (outcome.Ok)
        {
            wire = CatchUpBuilder.FromAi(job.CatchUpId, row.WindowMinutes, row.FromMs, row.ToMs, outcome.Value!, built.Truncated || slice.Truncated, outcome.Model);
            row.Kind = "Ai"; row.AiState = "Ready"; row.Model = outcome.Model; row.PromptVersion = built.Request.PromptVersion;
        }
        else
        {
            // Still the extractive version; the state only says why no AI version followed, so the client can stop waiting.
            row.AiState = outcome.Failure == AiFailure.BudgetLimited ? "BudgetLimited" : "Unavailable";
            wire = CatchUpBuilder.Extractive(job.CatchUpId, row.WindowMinutes, row.FromMs, row.ToMs, slice.Segments, row.AiState);
        }
        row.ResultJson = CatchUpJson.Write(wire);
        row.UpdatedAt = clock.GetUtcNow();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return; } // the session or the row vanished: nothing to publish (C-21)

        using var doc = System.Text.Json.JsonDocument.Parse(row.ResultJson);
        await sp.GetRequiredService<ILiveEventPublisher>().PublishEphemeralAsync(job.UserId, job.SessionId, "catchup.ready", doc.RootElement.Clone(), ct);
    }
}
