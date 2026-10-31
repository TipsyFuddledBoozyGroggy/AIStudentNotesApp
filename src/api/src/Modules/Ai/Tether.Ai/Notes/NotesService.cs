using System.Collections.Concurrent;
using System.Threading.Channels;
using Tether.Ai.Budget;
using Tether.Ai.Domain;
using Tether.Ai.Persistence;
using Tether.Ai.Provider;
using Tether.Ai.Summary;
using Tether.Ai.Validation;
using Tether.Lectures.Contracts;
using Tether.SharedKernel;
using Tether.Transcript.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Tether.Ai.Notes;

/// <summary>
/// The gateway's permission check for the two after-Stop workflows (PO-18d). A provider call for a stopped lecture is allowed only
/// while THIS press is <c>Pending</c>, belongs to this user and session, and is younger than <see cref="NotesOptions.StaleAfter"/>.
/// A cheap, user-scoped, untracked read; the gateway runs it before the reservation and again immediately before the vendor call.
/// </summary>
internal sealed class NotesPressProbe(AiDbContext db, AiOptions options, TimeProvider clock)
{
    public async Task<bool> IsLiveAsync(Guid userId, Guid sessionId, Guid pressId, CancellationToken ct)
    {
        var fresh = clock.GetUtcNow() - options.Notes.StaleAfter;
        return await db.Notes.AsNoTracking().AnyAsync(n => n.Id == pressId && n.SessionId == sessionId && n.UserId == userId
            && n.State == NotesStates.Pending && n.RequestedAt > fresh, ct);
    }
}

internal sealed record NotesJob(Guid UserId, Guid SessionId, Guid PressId);

/// <summary>Bounded hand-off to the notes lane. A full queue refuses the job (the press ends Unavailable); it never blocks the request.</summary>
internal sealed class NotesQueue(AiOptions options)
{
    public Channel<NotesJob> Channel { get; } = System.Threading.Channels.Channel.CreateBounded<NotesJob>(
        new BoundedChannelOptions(Math.Max(1, options.Notes.QueueCapacity)) { FullMode = BoundedChannelFullMode.Wait, SingleReader = false });

    public bool TryEnqueue(NotesJob job) => Channel.Writer.TryWrite(job);
}

/// <summary>Per-USER sliding-minute limiter for new presses (a student has many sessions; the per-session bound is the attempt cap). In-process (ADR-020d).</summary>
internal sealed class NotesLimiter(AiOptions options, TimeProvider clock)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<Guid, List<DateTimeOffset>> _hits = new();

    public bool TryAcquire(Guid userId)
    {
        var now = clock.GetUtcNow();
        if (_hits.Count > 10_000) foreach (var (k, v) in _hits) lock (v) if (v.Count == 0 || now - v[^1] >= Window) _hits.TryRemove(k, out _);
        var q = _hits.GetOrAdd(userId, _ => []);
        lock (q)
        {
            while (q.Count > 0 && now - q[0] >= Window) q.RemoveAt(0);
            if (q.Count >= options.Notes.PerMinutePerUser) return false;
            q.Add(now);
            return true;
        }
    }

    /// <summary>Gives the newest token back: the press ended up sharing another press's generation and costs nothing of its own.</summary>
    public void Release(Guid userId)
    {
        if (!_hits.TryGetValue(userId, out var q)) return;
        lock (q) { if (q.Count > 0) q.RemoveAt(q.Count - 1); }
    }
}

internal enum NotesStartKind { Ok, NotFound, InvalidState, RateLimited }

internal sealed record NotesStartResult(NotesStartKind Kind, LectureNotesRow? Row = null, bool Created = false, bool Replayed = false);

/// <summary>
/// Handles the deliberate "Make notes" press (PO-18d). Only a Stopped session is accepted. The press is audited (content-free) and
/// recorded as a <c>Pending</c> row BEFORE anything is queued; that row is the only permission the gateway accepts for sending the
/// stopped lecture's stored transcript to a provider. One generation per session: a press while one is pending or after one is ready
/// gets that one back; only an Unavailable or BudgetLimited attempt can be followed by a new one (up to the attempt cap).
/// </summary>
internal sealed class NotesService(
    AiDbContext db, ISessionDirectory directory, ISessionAuditLog audit, ITranscriptSegmentReader reader, BudgetGate budget, AiHealth health,
    AiOptions options, NotesQueue queue, NotesLimiter limiter, TimeProvider clock, ILogger<NotesService> log)
{
    public async Task<NotesStartResult> RequestAsync(Guid userId, Guid sessionId, string idempotencyKey, CancellationToken ct)
    {
        var session = await directory.FindAsync(userId, sessionId, ct);
        if (session is null) return new(NotesStartKind.NotFound);
        if (session.State != LectureState.Stopped) return new(NotesStartKind.InvalidState); // B3-06: nothing goes anywhere while live or paused

        await ExpireStaleAsync(sessionId, ct);

        var prior = await db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.SessionId == sessionId && n.IdempotencyKey == idempotencyKey, ct);
        if (prior is not null) return new(NotesStartKind.Ok, prior, Replayed: true);

        var latest = await LatestAsync(sessionId, ct);
        if (latest is { State: NotesStates.Pending or NotesStates.Ready or NotesStates.NotNeeded }) return new(NotesStartKind.Ok, latest); // no duplicate generation

        if (!limiter.TryAcquire(userId)) return new(NotesStartKind.RateLimited);
        if ((latest?.Revision ?? 0) >= options.Notes.MaxAttemptsPerSession) { limiter.Release(userId); return new(NotesStartKind.RateLimited); }

        // The press is audited before anything can be sent. A failed audit write means no press (the exception becomes a 500, nothing queued).
        if (!await audit.RecordNotesRequestedAsync(userId, sessionId, ct)) { limiter.Release(userId); return new(NotesStartKind.NotFound); }

        var now = clock.GetUtcNow();
        string state;
        if (await reader.LatestEndMsAsync(userId, sessionId, ct) is null) state = NotesStates.NotNeeded; // no transcript: nothing to send
        else if (!options.Enabled || health.IsOpen) state = NotesStates.Unavailable;
        else state = (await budget.CheckAsync(sessionId, AiWorkflow.Notes, 0, ct)).Decision.Allowed ? NotesStates.Pending : NotesStates.BudgetLimited;

        var row = new LectureNotesRow
        {
            Id = Ids.New(), SessionId = sessionId, UserId = userId, Revision = (latest?.Revision ?? 0) + 1, State = state, IdempotencyKey = idempotencyKey,
            RequestedAt = now, CompletedAt = state == NotesStates.Pending ? null : now,
        };
        db.Notes.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent press won (same key, same revision, or the one-pending index): hand back its row, start nothing.
            limiter.Release(userId);
            db.ChangeTracker.Clear();
            var keyed = await db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.SessionId == sessionId && n.IdempotencyKey == idempotencyKey, ct);
            if (keyed is not null) return new(NotesStartKind.Ok, keyed, Replayed: true);
            var winner = await LatestAsync(sessionId, ct);
            return winner is null ? new(NotesStartKind.RateLimited) : new(NotesStartKind.Ok, winner);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            db.ChangeTracker.Clear();
            return new(NotesStartKind.NotFound); // deleted between the lookup and the insert
        }

        if (state == NotesStates.Pending && !queue.TryEnqueue(new NotesJob(userId, sessionId, row.Id)))
        {
            log.LogWarning("Notes queue full; press marked unavailable");
            row.State = NotesStates.Unavailable;
            row.CompletedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        return new(NotesStartKind.Ok, row, Created: true);
    }

    /// <summary>The newest attempt of the session, after expiring a lost one. Null when the student never pressed.</summary>
    public async Task<LectureNotesRow?> GetLatestAsync(Guid sessionId, CancellationToken ct)
    {
        await ExpireStaleAsync(sessionId, ct);
        return await LatestAsync(sessionId, ct);
    }

    private Task<LectureNotesRow?> LatestAsync(Guid sessionId, CancellationToken ct) =>
        db.Notes.AsNoTracking().Where(n => n.SessionId == sessionId).OrderByDescending(n => n.Revision).FirstOrDefaultAsync(ct);

    /// <summary>A press whose job was lost (restart, crash) must not stay Pending forever; the gateway already refuses it once it is this old.</summary>
    private async Task ExpireStaleAsync(Guid sessionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var cutoff = now - options.Notes.StaleAfter;
        var stale = await db.Notes.AsNoTracking().Where(n => n.SessionId == sessionId && n.State == NotesStates.Pending && n.RequestedAt < cutoff).ToListAsync(ct);
        foreach (var r in stale) await NotesStore.MarkUnavailableAsync(db, r.Id, r.UserId, now, ct);
    }
}

internal static class NotesStore
{
    /// <summary>Only a row that is still Pending changes. Fixed parameterised statement, explicitly owner-scoped (bulk ExecuteUpdate is banned).</summary>
    public static Task<int> MarkUnavailableAsync(AiDbContext db, Guid id, Guid userId, DateTimeOffset now, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"update ai.lecture_notes set state = 'Unavailable', completed_at = {now} where id = {id} and user_id = {userId} and state = 'Pending'", ct);
}

/// <summary>
/// The notes lane: its own bounded queue and workers, so a press never delays a live summary or a Quick recap. Each job re-reads the
/// STORED finals (never partials, never the client), sends them part by part through the gateway, then makes the Final summary
/// revision with the same press, then stores the result. Every provider call re-checks, in the gateway, that the session is Stopped
/// and this press is still pending; Delete cancels an in-flight call (SessionCutoff) and the FK cascade removes everything.
/// </summary>
internal sealed class NotesRunner(NotesQueue queue, IServiceScopeFactory scopes, AiOptions options, SummaryEngine summaries, TimeProvider clock, ILogger<NotesRunner> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SweepOrphansAsync(stoppingToken);
        await Task.WhenAll(Enumerable.Range(0, Math.Max(1, options.Notes.Workers)).Select(_ => WorkAsync(stoppingToken)));
    }

    /// <summary>At startup every Pending press lost its in-memory job (single instance, ADR-020d): it becomes Unavailable so no client waits. Best effort.</summary>
    public async Task<int> SweepOrphansAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
            var n = await db.Database.ExecuteSqlAsync($"update ai.lecture_notes set state = 'Unavailable', completed_at = {clock.GetUtcNow()} where state = 'Pending'", ct);
            if (n > 0) log.LogWarning("Marked {Count} orphaned notes presses unavailable at startup", n);
            return n;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("Notes orphan sweep failed: {ExceptionType}", ex.GetType().Name);
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
                    log.LogWarning("Notes job failed: {ExceptionType}", ex.GetType().Name);
                    await MarkUnavailableAsync(job);
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private async Task MarkUnavailableAsync(NotesJob job)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var scope = scopes.CreateUserScope(job.UserId);
            await NotesStore.MarkUnavailableAsync(scope.ServiceProvider.GetRequiredService<AiDbContext>(), job.PressId, job.UserId, clock.GetUtcNow(), cts.Token);
        }
        catch (Exception ex)
        {
            log.LogWarning("Could not mark notes press unavailable: {ExceptionType}", ex.GetType().Name);
        }
    }

    public async Task ProcessAsync(NotesJob job, CancellationToken ct)
    {
        await using var scope = scopes.CreateUserScope(job.UserId);
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AiDbContext>();
        var row = await db.Notes.FirstOrDefaultAsync(n => n.Id == job.PressId && n.SessionId == job.SessionId, ct);
        if (row is null || row.State != NotesStates.Pending) return; // deleted meanwhile, or already expired

        // The source: stored finals only (corrections applied), oldest first, bounded.
        var reader = sp.GetRequiredService<ITranscriptSegmentReader>();
        var finals = new List<FinalSegment>();
        long afterSeq = 0;
        var readTruncated = false;
        while (true)
        {
            var page = await reader.ReadFinalsAfterAsync(job.UserId, job.SessionId, afterSeq, 500, ct);
            if (page.Count == 0) break;
            finals.AddRange(page);
            afterSeq = page[^1].Seq;
            if (finals.Count >= options.Notes.MaxSegments) { readTruncated = true; finals.RemoveRange(options.Notes.MaxSegments, finals.Count - options.Notes.MaxSegments); break; }
            if (page.Count < 500) break;
        }
        var now = clock.GetUtcNow();
        if (finals.Count == 0) { Complete(row, NotesStates.NotNeeded, now); await SaveAsync(db, ct); return; }

        var plan = NotesRequests.Build(job.UserId, job.SessionId, job.PressId, finals, readTruncated, options.Notes);
        var latestSummary = await sp.GetRequiredService<SummaryStore>().LatestAsync(job.SessionId, ct);
        var finalRequests = summaries.PlanFinalRequests(job.UserId, job.SessionId, job.PressId, latestSummary, plan.Used);

        // Per-press cap on the ESTIMATED cost of everything this press may send. Over it: no call at all.
        var estimate = plan.Parts.Sum(p => AiGateway.Estimate(options, p.Request)) + finalRequests.Sum(r => AiGateway.Estimate(options, r));
        if (estimate > options.Notes.PerRequestMicro)
        {
            await sp.GetRequiredService<UsageLedger>().RecordAsync(job.UserId, job.SessionId, AiWorkflow.Notes, Prompts.PromptLibrary.NotesVersion, "none",
                new AiTokenUsage(0, 0, 0), 0, 0, Outcomes.BudgetDenied, ct);
            Complete(row, NotesStates.BudgetLimited, now);
            await SaveAsync(db, ct);
            return;
        }

        var gateway = sp.GetRequiredService<AiGateway>();
        var sections = new List<NotesSection>();
        string? failure = null;
        var model = "";
        foreach (var part in plan.Parts)
        {
            var ctx = part.Context;
            var outcome = await gateway.RunAsync(part.Request,
                json => LanguageGuard.Apply(NotesValidator.Validate(json, ctx), r => LanguageGuard.Texts(r, ctx), options.LanguageGuardEnforce, log, "Notes"), ct);
            if (!outcome.Ok) { failure = outcome.Failure; break; }
            model = outcome.Model;
            foreach (var s in outcome.Value!.Sections) if (sections.Count < NotesJson.MaxSections) sections.Add(s);
        }

        if (failure is not null)
        {
            log.LogInformation("Notes press ended without notes: {Reason}", failure); // vocabulary code only
            Complete(row, failure == AiFailure.BudgetLimited ? NotesStates.BudgetLimited : NotesStates.Unavailable, clock.GetUtcNow());
            await SaveAsync(db, ct);
            return;
        }

        // The same press makes the Final summary revision (PO-18d), while the press is still Pending (the gateway checks it per call).
        var (finalRevision, finalFailure) = await summaries.FinalAsync(job.UserId, job.SessionId, job.PressId, plan.Used, sp, ct);
        if (finalFailure is not null) log.LogInformation("Final summary not made: {Reason}", finalFailure);

        row.State = NotesStates.Ready;
        row.CompletedAt = clock.GetUtcNow();
        row.SectionsJson = NotesJson.WriteSections(sections);
        row.CoveredFromMs = plan.Used.Min(s => s.StartMs);
        row.CoveredToMs = plan.Used.Max(s => s.EndMs);
        row.SourceFromSeq = plan.Used.Min(s => s.Seq);
        row.SourceToSeq = plan.Used.Max(s => s.Seq);
        row.Truncated = plan.Truncated;
        row.FinalSummaryRevision = finalRevision;
        row.Model = model;
        row.PromptVersion = Prompts.PromptLibrary.NotesVersion;
        await SaveAsync(db, ct);
    }

    private static void Complete(LectureNotesRow row, string state, DateTimeOffset now)
    {
        row.State = state;
        row.CompletedAt = now;
    }

    /// <summary>The session (and so the row) may have been deleted meanwhile: then there is nothing to keep (C-21) and nothing is recreated.</summary>
    private async Task SaveAsync(AiDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) { log.LogInformation("Notes result not stored: {ExceptionType}", ex.GetType().Name); }
    }
}
