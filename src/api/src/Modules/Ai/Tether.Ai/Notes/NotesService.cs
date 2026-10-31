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
using NpgsqlTypes;

namespace Tether.Ai.Notes;

/// <summary>
/// The gateway's permission check for the two after-Stop workflows (PO-18d). A provider call for a stopped lecture is allowed only
/// while THIS press is <c>Pending</c>, belongs to this user and session, has STARTED running (NAS-03: only the job itself ever calls), and
/// started less than <see cref="NotesOptions.StaleAfter"/> ago. A cheap, user-scoped, untracked read; the gateway runs it before the
/// reservation and again immediately before the vendor call.
/// </summary>
internal sealed class NotesPressProbe(AiDbContext db, AiOptions options, TimeProvider clock)
{
    public async Task<bool> IsLiveAsync(Guid userId, Guid sessionId, Guid pressId, CancellationToken ct)
    {
        var fresh = clock.GetUtcNow() - options.Notes.StaleAfter;
        return await db.Notes.AsNoTracking().AnyAsync(n => n.Id == pressId && n.SessionId == sessionId && n.UserId == userId
            && n.State == NotesStates.Pending && n.StartedAt != null && n.StartedAt > fresh, ct);
    }
}

internal sealed record NotesJob(Guid UserId, Guid SessionId, Guid PressId);

/// <summary>
/// Bounded hand-off to the notes lane. A full queue refuses the job (the press ends Unavailable); it never blocks the request. Fairness
/// (NAS-03) comes from the one-pending-press-per-user rule (<c>ux_lecture_notes_pending_user</c>): the queue holds at most one job per user,
/// so one student can never fill it.
/// </summary>
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

    /// <summary>A token (the hit's time) to hand back with <see cref="Release"/>, or null when the user is over the limit.</summary>
    public DateTimeOffset? TryAcquire(Guid userId)
    {
        var now = clock.GetUtcNow();
        if (_hits.Count > 10_000) foreach (var (k, v) in _hits) lock (v) if (v.Count == 0 || now - v[^1] >= Window) _hits.TryRemove(k, out _);
        var q = _hits.GetOrAdd(userId, _ => []);
        lock (q)
        {
            while (q.Count > 0 && now - q[0] >= Window) q.RemoveAt(0);
            if (q.Count >= options.Notes.PerMinutePerUser) return null;
            q.Add(now);
            return now;
        }
    }

    /// <summary>NAS-03: gives back exactly the token this press took (not "the newest", which may belong to another press of the same user).</summary>
    public void Release(Guid userId, DateTimeOffset? token)
    {
        if (token is not { } t || !_hits.TryGetValue(userId, out var q)) return;
        lock (q) { var i = q.IndexOf(t); if (i >= 0) q.RemoveAt(i); }
    }

    public int Count(Guid userId) { if (!_hits.TryGetValue(userId, out var q)) return 0; lock (q) return q.Count; }
}

/// <summary>
/// NAS-06: the running notes jobs, so a press that expires (stale on a GET or press) cancels its own job at once instead of letting it finish
/// and overwrite the expiry. Each job's token also cancels itself after <see cref="NotesOptions.StaleAfter"/> (the permission's lifetime).
/// </summary>
internal sealed class NotesJobs(AiOptions options)
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public CancellationTokenSource Start(Guid pressId, CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(options.Notes.StaleAfter);
        _running[pressId] = cts;
        return cts;
    }

    public void Cancel(Guid pressId)
    {
        if (_running.TryGetValue(pressId, out var cts))
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { /* finished meanwhile */ }
        }
    }

    public void End(Guid pressId, CancellationTokenSource cts)
    {
        _running.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(pressId, cts));
        cts.Dispose();
    }

    public bool IsRunning(Guid pressId) => _running.ContainsKey(pressId);
}

internal enum NotesStartKind { Ok, NotFound, InvalidState, RateLimited }

internal sealed record NotesStartResult(NotesStartKind Kind, LectureNotesRow? Row = null, bool Created = false, bool Replayed = false);

/// <summary>
/// Handles the deliberate "Make notes" press (PO-18d). Only a Stopped session is accepted. The press is audited (content-free, with its id
/// and the notice version the student saw) and recorded as a <c>Pending</c> row BEFORE anything is queued; that row is the only permission
/// the gateway accepts for sending the stopped lecture's stored transcript to a provider. One generation per session: a press while one is
/// pending or after one is ready gets that one back; only an Unavailable or BudgetLimited attempt can be followed by a new one (up to the
/// attempt cap). One pending press per user across all sessions (NAS-03): a press while another session's press is pending is 429.
/// </summary>
internal sealed class NotesService(
    AiDbContext db, ISessionDirectory directory, ISessionAuditLog audit, ITranscriptSegmentReader reader, BudgetGate budget, AiHealth health,
    AiOptions options, NotesQueue queue, NotesLimiter limiter, NotesJobs jobs, SessionCutoff cutoff, TimeProvider clock, ILogger<NotesService> log)
{
    public async Task<NotesStartResult> RequestAsync(Guid userId, Guid sessionId, string idempotencyKey, string noticeVersion, CancellationToken ct)
    {
        var session = await directory.FindAsync(userId, sessionId, ct);
        if (session is null) return new(NotesStartKind.NotFound);
        if (session.State != LectureState.Stopped) return new(NotesStartKind.InvalidState); // B3-06: nothing goes anywhere while live or paused
        // NAS-01: a session whose delete (or course purge) has been announced is gone for AI purposes, even while the purge has not committed.
        if (cutoff.IsTombstoned(sessionId)) return new(NotesStartKind.NotFound);

        await ExpireStaleAsync(userId, ct);

        var prior = await db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.SessionId == sessionId && n.IdempotencyKey == idempotencyKey, ct);
        if (prior is not null) return new(NotesStartKind.Ok, prior, Replayed: true);

        var latest = await LatestAsync(sessionId, ct);
        if (latest is { State: NotesStates.Pending or NotesStates.Ready or NotesStates.NotNeeded }) return new(NotesStartKind.Ok, latest); // no duplicate generation

        // NAS-03: one press pending or running per user. The unique index enforces it under races; this is the cheap, common answer.
        if (await db.Notes.AsNoTracking().AnyAsync(n => n.UserId == userId && n.State == NotesStates.Pending, ct)) return new(NotesStartKind.RateLimited);

        if (limiter.TryAcquire(userId) is not { } token) return new(NotesStartKind.RateLimited);
        var keep = false;
        try
        {
            if ((latest?.Revision ?? 0) >= options.Notes.MaxAttemptsPerSession) return new(NotesStartKind.RateLimited);

            // The press is audited before anything can be sent, with its own id (NAS-08: the ledger rows of its calls carry the same id) and
            // the notice version the student saw (NAS-05). A failed audit write means no press: the exception becomes a 500, nothing is
            // queued, and (finally) the limiter token is given back.
            var pressId = Ids.New();
            if (!await audit.RecordNotesRequestedAsync(userId, sessionId, pressId, noticeVersion, ct)) return new(NotesStartKind.NotFound);

            var now = clock.GetUtcNow();
            string state;
            if (await reader.LatestEndMsAsync(userId, sessionId, ct) is null) state = NotesStates.NotNeeded; // no transcript: nothing to send
            else if (!options.Enabled || health.IsNotesOpen) state = NotesStates.Unavailable;
            else state = (await budget.CheckAsync(sessionId, AiWorkflow.Notes, 0, ct)).Decision.Allowed ? NotesStates.Pending : NotesStates.BudgetLimited;

            var row = new LectureNotesRow
            {
                Id = pressId, SessionId = sessionId, UserId = userId, Revision = (latest?.Revision ?? 0) + 1, State = state, IdempotencyKey = idempotencyKey,
                RequestedAt = now, CompletedAt = state == NotesStates.Pending ? null : now, NoticeVersion = noticeVersion,
            };
            db.Notes.Add(row);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // A concurrent press won (same key, same revision, the one-pending-per-session or the one-pending-per-user index):
                // hand back this session's winner, start nothing. If the winner is another session's press, this one is refused (429).
                db.ChangeTracker.Clear();
                var keyed = await db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.SessionId == sessionId && n.IdempotencyKey == idempotencyKey, ct);
                if (keyed is not null) return new(NotesStartKind.Ok, keyed, Replayed: true);
                var winner = await LatestAsync(sessionId, ct);
                return winner is { State: NotesStates.Pending or NotesStates.Ready or NotesStates.NotNeeded } ? new(NotesStartKind.Ok, winner) : new(NotesStartKind.RateLimited);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
            {
                db.ChangeTracker.Clear();
                return new(NotesStartKind.NotFound); // deleted between the lookup and the insert
            }
            keep = true;

            if (state == NotesStates.Pending && !queue.TryEnqueue(new NotesJob(userId, sessionId, row.Id)))
            {
                log.LogWarning("Notes queue full; press marked unavailable");
                row.State = NotesStates.Unavailable;
                row.CompletedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
            }
            return new(NotesStartKind.Ok, row, Created: true);
        }
        finally
        {
            if (!keep) limiter.Release(userId, token); // NAS-08: refused, lost a race, or the audit write failed: this press cost nothing
        }
    }

    /// <summary>The newest attempt of the session, after expiring a lost one. Null when the student never pressed.</summary>
    public async Task<LectureNotesRow?> GetLatestAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        await ExpireStaleAsync(userId, ct);
        return await LatestAsync(sessionId, ct);
    }

    private Task<LectureNotesRow?> LatestAsync(Guid sessionId, CancellationToken ct) =>
        db.Notes.AsNoTracking().Where(n => n.SessionId == sessionId).OrderByDescending(n => n.Revision).FirstOrDefaultAsync(ct);

    /// <summary>
    /// A press whose job was lost (restart, crash) or ran out of time must not stay Pending: one that STARTED more than
    /// <see cref="NotesOptions.StaleAfter"/> ago (the gateway already refuses it), or one still QUEUED <see cref="NotesOptions.MaxQueueWait"/>
    /// after the press. Covers all of the user's presses, since one pending press per user blocks the others (NAS-03). An expired press
    /// cancels its running job (NAS-06).
    /// </summary>
    private async Task ExpireStaleAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var started = now - options.Notes.StaleAfter;
        var queued = now - options.Notes.MaxQueueWait;
        var stale = await db.Notes.AsNoTracking()
            .Where(n => n.UserId == userId && n.State == NotesStates.Pending
                        && ((n.StartedAt != null && n.StartedAt < started) || (n.StartedAt == null && n.RequestedAt < queued)))
            .Select(n => n.Id).ToListAsync(ct);
        foreach (var id in stale)
            if (await NotesStore.MarkUnavailableAsync(db, id, userId, now, ct) > 0) jobs.Cancel(id);
    }
}

internal static class NotesStore
{
    /// <summary>Only a row that is still Pending changes. Fixed parameterised statement, explicitly owner-scoped (bulk ExecuteUpdate is banned).</summary>
    public static Task<int> MarkUnavailableAsync(AiDbContext db, Guid id, Guid userId, DateTimeOffset now, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"update ai.lecture_notes set state = 'Unavailable', completed_at = {now} where id = {id} and user_id = {userId} and state = 'Pending'", ct);

    /// <summary>NAS-03: the job claims its press; the permission window starts now. 0 rows: not pending any more (expired, deleted) or already started.</summary>
    public static Task<int> TryStartAsync(AiDbContext db, Guid id, Guid userId, DateTimeOffset now, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"update ai.lecture_notes set started_at = {now} where id = {id} and user_id = {userId} and state = 'Pending' and started_at is null", ct);

    /// <summary>NAS-06: ends a press without content (BudgetLimited, Unavailable, NotNeeded) ONLY if it is still Pending. Returns rows changed.</summary>
    public static Task<int> FinishAsync(AiDbContext db, Guid id, Guid userId, string state, DateTimeOffset now, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"update ai.lecture_notes set state = {state}, completed_at = {now} where id = {id} and user_id = {userId} and state = 'Pending'", ct);

    /// <summary>
    /// NAS-06: stores the Ready result ONLY if the press is still Pending (a press expired by a GET, or deleted, is never written back to
    /// Ready). Returns rows changed; 0 means the result is discarded.
    /// </summary>
    public static Task<int> CompleteReadyAsync(AiDbContext db, LectureNotesRow r, CancellationToken ct)
    {
        // Interpolated (parameterised) statement; the nullable revision goes in as a typed parameter so a null is still an integer.
        var revision = new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = (object?)r.FinalSummaryRevision ?? DBNull.Value };
        long coveredFrom = r.CoveredFromMs!.Value, coveredTo = r.CoveredToMs!.Value, fromSeq = r.SourceFromSeq!.Value, toSeq = r.SourceToSeq!.Value;
        string sections = r.SectionsJson, finalState = r.FinalSummaryState!, model = r.Model ?? "", promptVersion = r.PromptVersion ?? "";
        var done = r.CompletedAt!.Value;
        return db.Database.ExecuteSqlAsync($"""
            update ai.lecture_notes set state = 'Ready', completed_at = {done}, sections = {sections}::jsonb, covered_from_ms = {coveredFrom}, covered_to_ms = {coveredTo},
                source_from_seq = {fromSeq}, source_to_seq = {toSeq}, truncated = {r.Truncated}, final_summary_revision = {revision}, final_summary_state = {finalState},
                model = {model}, prompt_version = {promptVersion}
            where id = {r.Id} and user_id = {r.UserId} and state = 'Pending'
            """, ct);
    }
}

/// <summary>
/// The notes lane: its own bounded queue and workers, so a press never delays a live summary or a Quick recap. Each job re-reads the
/// STORED finals (never partials, never the client), sends them part by part through the gateway, then makes the Final summary
/// revision with the same press, then stores the result. Every provider call re-checks, in the gateway, that the session is Stopped
/// and this press is still pending; Delete cancels an in-flight call (SessionCutoff) and the FK cascade removes everything.
/// </summary>
internal sealed class NotesRunner(NotesQueue queue, IServiceScopeFactory scopes, AiOptions options, SummaryEngine summaries, NotesJobs jobs, SessionCutoff cutoff,
    TimeProvider clock, ILogger<NotesRunner> log) : BackgroundService
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
        var row = await db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == job.PressId && n.SessionId == job.SessionId, ct);
        if (row is null || row.State != NotesStates.Pending) return; // deleted meanwhile, or already expired
        if (cutoff.IsTombstoned(job.SessionId)) { await NotesStore.MarkUnavailableAsync(db, row.Id, row.UserId, clock.GetUtcNow(), ct); return; } // NAS-01
        // NAS-03: the permission window starts when the job starts, so time spent waiting in the queue never eats it.
        if (await NotesStore.TryStartAsync(db, row.Id, row.UserId, clock.GetUtcNow(), ct) == 0) return;

        var jcts = jobs.Start(row.Id, ct);
        try
        {
            await RunAsync(job, row, sp, db, jcts.Token);
        }
        catch (OperationCanceledException) when (jcts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // NAS-06: the press expired (a GET or press saw it stale, or its own StaleAfter ran out). Nothing is stored over the expiry.
            log.LogInformation("Notes job stopped: press expired");
            await NotesStore.MarkUnavailableAsync(db, row.Id, row.UserId, clock.GetUtcNow(), CancellationToken.None);
        }
        finally
        {
            jobs.End(row.Id, jcts);
        }
    }

    private async Task RunAsync(NotesJob job, LectureNotesRow row, IServiceProvider sp, AiDbContext db, CancellationToken ct)
    {
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
        if (finals.Count == 0) { await FinishAsync(db, row, NotesStates.NotNeeded, ct); return; }

        var plan = NotesRequests.Build(job.UserId, job.SessionId, job.PressId, finals, readTruncated, options.Notes);
        var latestSummary = await sp.GetRequiredService<SummaryStore>().LatestAsync(job.SessionId, ct);
        var finalRequests = summaries.PlanFinalRequests(job.UserId, job.SessionId, job.PressId, latestSummary, plan.Used);

        // Per-press cap on the ESTIMATED cost of everything this press may send (NAS-07: every call may be made twice, one repair). Over it: no call at all.
        var estimate = PressEstimate(options, plan.Parts.Select(p => p.Request), finalRequests);
        if (estimate > options.Notes.PerRequestMicro)
        {
            await sp.GetRequiredService<UsageLedger>().RecordAsync(job.UserId, job.SessionId, AiWorkflow.Notes, Prompts.PromptLibrary.NotesVersion, "none",
                new AiTokenUsage(0, 0, 0), 0, 0, Outcomes.BudgetDenied, ct, job.PressId);
            await FinishAsync(db, row, NotesStates.BudgetLimited, ct);
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
            await FinishAsync(db, row, failure == AiFailure.BudgetLimited ? NotesStates.BudgetLimited : NotesStates.Unavailable, ct);
            return;
        }

        // The same press makes the Final summary revision (PO-18d), while the press is still Pending (the gateway checks it per call).
        // NAS-03: its outcome is always recorded (finalSummaryStatus); a failure is never silent while the notes show Ready.
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
        row.FinalSummaryState = FinalStateOf(finalRevision, finalFailure);
        row.Model = model;
        row.PromptVersion = Prompts.PromptLibrary.NotesVersion;
        try
        {
            if (await NotesStore.CompleteReadyAsync(db, row, ct) == 0) log.LogInformation("Notes result discarded: press no longer pending");
        }
        catch (DbUpdateException ex) { log.LogInformation("Notes result not stored: {ExceptionType}", ex.GetType().Name); }
        catch (PostgresException ex) { log.LogInformation("Notes result not stored: {ExceptionType}", ex.GetType().Name); }
    }

    /// <summary>NAS-07: the per-press estimate: every planned call (notes parts and final-summary chunks) at the gateway's own hold, twice (the one repair).</summary>
    internal static long PressEstimate(AiOptions options, IEnumerable<AiRequest> parts, IEnumerable<AiRequest> finals) =>
        2 * (parts.Sum(r => AiGateway.Estimate(options, r)) + finals.Sum(r => AiGateway.Estimate(options, r)));

    /// <summary>NAS-03: the finalSummaryStatus of a Ready press. A revision means Ready; no failure and no revision means there was nothing to summarise.</summary>
    internal static string FinalStateOf(int? revision, string? failure) => (revision, failure) switch
    {
        ({ }, _) => FinalSummaryStates.Ready,
        (null, null) => FinalSummaryStates.NotNeeded,
        (null, AiFailure.BudgetLimited) => FinalSummaryStates.BudgetLimited,
        _ => FinalSummaryStates.Unavailable,
    };

    /// <summary>The session (and so the row) may have been deleted, or the press expired, meanwhile: then nothing is written (C-21, NAS-06).</summary>
    private async Task FinishAsync(AiDbContext db, LectureNotesRow row, string state, CancellationToken ct)
    {
        try { await NotesStore.FinishAsync(db, row.Id, row.UserId, state, clock.GetUtcNow(), ct); }
        catch (DbUpdateException ex) { log.LogInformation("Notes result not stored: {ExceptionType}", ex.GetType().Name); }
        catch (PostgresException ex) { log.LogInformation("Notes result not stored: {ExceptionType}", ex.GetType().Name); }
    }
}
