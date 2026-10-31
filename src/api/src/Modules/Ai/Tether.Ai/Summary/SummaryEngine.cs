using System.Collections.Concurrent;
using Tether.Ai.Budget;
using Tether.Ai.Domain;
using Tether.Ai.Provider;
using Tether.Ai.Validation;
using Tether.Lectures.Contracts;
using Tether.SharedKernel;
using Tether.Transcript.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tether.Ai.Summary;

internal sealed class SessionSummaryState(Guid userId, Guid sessionId)
{
    public Guid UserId { get; } = userId;
    public Guid SessionId { get; } = sessionId;
    public readonly object Lock = new();
    public int Pending;
    public DateTimeOffset? FirstPendingAt;
    public DateTimeOffset LastRunStart = DateTimeOffset.MinValue;
    public bool InFlight;
    /// <summary>Paused: no provider call until the student resumes (B3-06). Stop and delete remove the state instead.</summary>
    public bool Suspended;
    public double IntervalMultiplier = 1;
    public int ConsecutiveFailures;
    public string? LastStatusKey;
}

/// <summary>
/// Decides WHEN a session's summary is updated. It is fed only by <see cref="ITranscriptFinalObserver"/> (committed finals; partials
/// never reach it) and its observer methods only touch counters under a lock, so transcription never waits on AI. At most one update
/// per session is in flight; segments arriving meanwhile just raise the pending count (latest wins: the next run reads everything
/// newer than the last revision from the stored transcript, not from a queue). Single instance only (ADR-020d).
/// </summary>
internal sealed class SummaryScheduler(AiOptions options, TimeProvider clock, SummaryEngine engine, ILogger<SummaryScheduler> log)
    : ITranscriptFinalObserver, ISessionLifecycleObserver
{
    private readonly ConcurrentDictionary<Guid, SessionSummaryState> _states = new();
    private readonly SemaphoreSlim _concurrency = new(Math.Max(1, options.Summary.MaxConcurrent));

    public int PendingFor(Guid sessionId) => _states.TryGetValue(sessionId, out var s) ? s.Pending : 0;
    public int TrackedSessions => _states.Count;
    public bool IsTracked(Guid sessionId) => _states.ContainsKey(sessionId);

    // N-01: sessions seen stopped, failed or deleted. A final's observer call runs AFTER its commit, so it can arrive after the Stop or
    // Delete notification already removed the state; without this it would re-create the state and tick (reserve, find the session
    // closed, settle) every MinInterval until the process restarts. A stopped or deleted session never becomes Active again, so the
    // entry never needs to be undone; old ones are pruned (a late observer call lags its commit by milliseconds, not an hour).
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _closed = new();
    private static readonly TimeSpan ClosedKeep = TimeSpan.FromHours(1);
    private const int ClosedSoftCap = 256, ClosedHardCap = 20_000;

    private void MarkClosedAndDrop(Guid sessionId)
    {
        _states.TryRemove(sessionId, out _);
        var now = clock.GetUtcNow();
        if (_closed.Count >= ClosedSoftCap)
            foreach (var kv in _closed)
                if (now - kv.Value > ClosedKeep) _closed.TryRemove(kv.Key, out _);
        if (_closed.Count < ClosedHardCap) _closed[sessionId] = now; // beyond the cap the engine's own closed-session check still drops a zombie
    }

    /// <summary>The engine found the session closed for good (stopped, failed or gone): stop tracking it for good.</summary>
    public void Forget(Guid sessionId) => MarkClosedAndDrop(sessionId);

    public void OnFinalPersisted(Guid userId, Guid sessionId, long seq)
    {
        if (!options.Enabled) return;
        if (_closed.ContainsKey(sessionId)) return;
        if (!_states.TryGetValue(sessionId, out var st))
        {
            if (_states.Count >= options.Summary.MaxTrackedSessions) return; // bounded memory: beyond the cap new sessions simply get no TL;DR
            st = _states.GetOrAdd(sessionId, _ => new SessionSummaryState(userId, sessionId));
        }
        if (st.UserId != userId) return;
        lock (st.Lock)
        {
            st.Pending++;
            st.FirstPendingAt ??= clock.GetUtcNow();
        }
    }

    public Task OnStateChangedAsync(SessionStateChange change, CancellationToken ct)
    {
        // Counters only; never awaits AI, so a pause or stop is never slowed by it (ADR-019b).
        // B3-06 (Product Owner): nothing is sent to the vendor once the student paused or stopped. Pause used to request a flush and
        // Stop a final pass; neither exists now. In-flight calls are cancelled by SessionCutoff, and the gateway re-checks the state.
        if (change.State is LectureState.Stopped or LectureState.Failed)
        {
            // Even when no state exists yet: a final of this session may still be on its way to OnFinalPersisted (N-01).
            if (!_states.TryGetValue(change.SessionId, out var closing) || closing.UserId == change.UserId) MarkClosedAndDrop(change.SessionId);
            return Task.CompletedTask;
        }
        if (!_states.TryGetValue(change.SessionId, out var st) || st.UserId != change.UserId) return Task.CompletedTask;
        switch (change.State)
        {
            case LectureState.Paused: lock (st.Lock) st.Suspended = true; break;
            case LectureState.Active: lock (st.Lock) st.Suspended = false; break;
        }
        return Task.CompletedTask;
    }

    public Task OnDeletedAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        if (!_states.TryGetValue(sessionId, out var st) || st.UserId == userId) MarkClosedAndDrop(sessionId);
        return Task.CompletedTask;
    }

    /// <summary>Runs every due session once (and waits for them). Called by the worker on a timer and directly by tests.</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        if (!options.Enabled) return;
        var now = clock.GetUtcNow();
        var due = new List<SessionSummaryState>();
        foreach (var st in _states.Values)
            lock (st.Lock)
            {
                if (st.InFlight || st.Suspended) continue;
                var isDue = FlushPolicy.IsDue(st.Pending, st.FirstPendingAt, st.LastRunStart, now, options.Summary, st.IntervalMultiplier);
                if (!isDue) continue;
                st.InFlight = true;
                st.LastRunStart = now;
                due.Add(st);
            }
        await Task.WhenAll(due.Select(s => RunOneAsync(s, ct)));
    }

    private async Task RunOneAsync(SessionSummaryState st, CancellationToken ct)
    {
        await _concurrency.WaitAsync(ct);
        try
        {
            if (await engine.RunAsync(st, ct)) Forget(st.SessionId); // closed for good: no zombie state (N-01)
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("Summary run failed: {ExceptionType}", ex.GetType().Name); // type only (SEC-027)
        }
        finally
        {
            _concurrency.Release();
            lock (st.Lock) st.InFlight = false;
        }
    }
}

/// <summary>Background timer that ticks the scheduler. Failures are contained; nothing here can reach the ingest path.</summary>
internal sealed class SummaryWorker(SummaryScheduler scheduler, AiOptions options, ILogger<SummaryWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Summary.Tick);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await scheduler.TickAsync(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Summary tick failed: {ExceptionType}", ex.GetType().Name); }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}

/// <summary>One summary update (and the periodic compaction against the source transcript) for one session.</summary>
internal sealed class SummaryEngine(IServiceScopeFactory scopes, AiOptions options, TimeProvider clock, ILogger<SummaryEngine> log)
{
    private enum Step { Done, Nothing, Failed, Gone }

    /// <summary>Runs one update. Returns true when the session turned out to be stopped, failed or gone (not merely paused), so the scheduler should forget it.</summary>
    public async Task<bool> RunAsync(SessionSummaryState st, CancellationToken ct)
    {
        int startPending; DateTimeOffset? firstPending;
        lock (st.Lock)
        {
            startPending = st.Pending; firstPending = st.FirstPendingAt;
            st.Pending = 0; st.FirstPendingAt = null;
        }

        await using var scope = scopes.CreateUserScope(st.UserId);
        var sp = scope.ServiceProvider;
        var publisher = sp.GetRequiredService<ILiveEventPublisher>();
        var lag = firstPending is { } f ? (int)Math.Max(0, (clock.GetUtcNow() - f).TotalSeconds) : 0;
        await PublishStatusAsync(publisher, st, "Running", lag, null, always: true, ct);

        string? failure = null;
        var backlog = false;
        try
        {
            var (step, fail, more) = await DeltaAsync(st, sp, publisher, ct);
            backlog = more;
            failure = fail;
            if (step == Step.Gone) return true;
            var store = sp.GetRequiredService<SummaryStore>();
            if (failure is null)
            {
                var latest = await store.LatestAsync(st.SessionId, ct);
                var due = latest is not null && step == Step.Done && latest.Revision % options.Summary.CompactEveryRevisions == 0;
                if (latest is not null && due)
                {
                    var cf = await CompactAsync(st, sp, publisher, ct);
                    if (cf == AiFailure.SessionClosed) failure = cf;
                }
            }
            var pressure = await sp.GetRequiredService<BudgetGate>().PressureAsync(st.SessionId, ct);
            lock (st.Lock) st.IntervalMultiplier = BudgetMath.IntervalMultiplier(pressure, options.Budget);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("Summary step failed: {ExceptionType}", ex.GetType().Name);
            failure = AiFailure.Unavailable;
        }

        if (failure is null)
        {
            lock (st.Lock)
            {
                st.ConsecutiveFailures = 0;
                if (backlog) { st.Pending += options.Summary.FlushSegments; st.FirstPendingAt ??= clock.GetUtcNow(); }
            }
            await PublishStatusAsync(publisher, st, "Pending", 0, null, always: false, ct);
            return false;
        }

        if (failure == AiFailure.SessionClosed)
        {
            // The student paused, stopped or deleted (B3-06). Not a fault: nothing is counted against the session, shown as an error or retried now.
            // The segments stay pending, so a resume summarises them.
            lock (st.Lock)
            {
                st.Pending += startPending;
                st.FirstPendingAt = firstPending ?? clock.GetUtcNow();
            }
            return await IsClosedForGoodAsync(sp, st, ct);
        }

        int failures;
        lock (st.Lock)
        {
            st.Pending += startPending;
            st.FirstPendingAt = firstPending ?? clock.GetUtcNow();
            failures = ++st.ConsecutiveFailures;
        }
        var state = failure == AiFailure.BudgetLimited || failures >= 3 ? "Failed" : "Delayed";
        await PublishStatusAsync(publisher, st, state, lag, AiFailure.ToWireReason(failure), always: false, ct);
        return false;
    }

    /// <summary>
    /// After a SessionClosed outcome: is the session stopped, failed or gone (so tracking it only produces churn), as opposed to paused (it
    /// resumes) or Active again (a pause and quick resume)? Any lookup problem keeps the state: it is dropped only when positively closed.
    /// </summary>
    private async Task<bool> IsClosedForGoodAsync(IServiceProvider sp, SessionSummaryState st, CancellationToken ct)
    {
        try
        {
            var info = await sp.GetRequiredService<ISessionDirectory>().FindAsync(st.UserId, st.SessionId, ct);
            return info is null || info.State is LectureState.Stopped or LectureState.Failed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("Summary closed-session check failed: {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }

    private async Task<(Step Step, string? Failure, bool MoreBacklog)> DeltaAsync(SessionSummaryState st, IServiceProvider sp, ILiveEventPublisher publisher, CancellationToken ct)
    {
        var store = sp.GetRequiredService<SummaryStore>();
        var reader = sp.GetRequiredService<ITranscriptSegmentReader>();
        var gateway = sp.GetRequiredService<AiGateway>();
        const int limit = 200;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var latest = await store.LatestAsync(st.SessionId, ct);
            var baseRev = latest?.Revision ?? 0;
            var afterSeq = latest?.SourceToSeq ?? 0;
            var fresh = await reader.ReadFinalsAfterAsync(st.UserId, st.SessionId, afterSeq, limit, ct);
            if (fresh.Count == 0) return (Step.Nothing, null, false);
            var recent = await reader.ReadFinalsUpToAsync(st.UserId, st.SessionId, afterSeq, 20, ct);
            var existing = latest is null ? [] : SummaryJson.Read<List<TopicState>>(latest.TopicsJson);

            var built = SummaryRequests.BuildDelta(st.UserId, st.SessionId, baseRev, existing, recent, fresh, options.Summary, options.CallTimeout);
            if (built.TruncatedOldest) log.LogWarning("Summary input truncated to newest lines for session {SessionId}", st.SessionId); // metric, no content
            var ctx = new SummaryContext(baseRev, built.Allowed);
            var outcome = await gateway.RunAsync(built.Request, json => LanguageGuard.Apply(SummaryValidator.Validate(json, ctx), d => LanguageGuard.Texts(d, ctx), options.LanguageGuardEnforce, log, "RollingSummary"), ct);
            if (!outcome.Ok) return (Step.Failed, outcome.Failure, false);

            var delta = outcome.Value!;
            var topics = SummaryMerger.Merge(existing, delta);
            var terms = (latest is null ? [] : SummaryJson.Read<List<string>>(latest.TermsJson)).Concat(delta.NewTerms).Distinct(StringComparer.OrdinalIgnoreCase).TakeLast(30).ToList();
            var row = new SummaryRevisionRow
            {
                SessionId = st.SessionId, UserId = st.UserId, Revision = baseRev + 1, Kind = "Delta", Status = "Draft",
                BasedOnSeq = fresh.Max(s => s.Seq), SourceFromSeq = fresh.Min(s => s.Seq), SourceToSeq = fresh.Max(s => s.Seq),
                CoveredFromMs = latest?.CoveredFromMs ?? fresh.Min(s => s.StartMs), CoveredToMs = Math.Max(latest?.CoveredToMs ?? 0, fresh.Max(s => s.EndMs)),
                Tldr = delta.Tldr, TopicsJson = SummaryJson.Write(topics), TermsJson = SummaryJson.Write(terms),
                CitationsJson = SummaryJson.Write(SummaryMerger.Citations(topics)), Model = outcome.Model, PromptVersion = built.Request.PromptVersion, CreatedAt = clock.GetUtcNow(),
            };
            switch (await store.AppendAsync(row, baseRev, ct))
            {
                case AppendResult.Ok:
                    await PublishRevisionAsync(publisher, st, row, ct);
                    return (Step.Done, null, fresh.Count >= limit);
                case AppendResult.SessionGone:
                    return (Step.Gone, null, false);
                default:
                    // Stale: someone else advanced the revision after we read it. Discard this delta and try once more from the new base.
                    await sp.GetRequiredService<UsageLedger>().RecordAsync(st.UserId, st.SessionId, AiWorkflow.RollingSummary, row.PromptVersion, row.Model,
                        new AiTokenUsage(0, 0, 0), 0, 0, Outcomes.Stale, ct);
                    continue;
            }
        }
        return (Step.Failed, AiFailure.InvalidOutput, false);
    }

    /// <summary>
    /// Compaction (design section 3 step 4): re-derives the summary from the SOURCE transcript in bounded chunks so accumulated drift is
    /// reset. If the transcript is longer than the chunk budget, the oldest part is carried over from the existing summary.
    /// </summary>
    private async Task<string?> CompactAsync(SessionSummaryState st, IServiceProvider sp, ILiveEventPublisher publisher, CancellationToken ct)
    {
        var latest = await sp.GetRequiredService<SummaryStore>().LatestAsync(st.SessionId, ct);
        if (latest is null) return null;

        // Read the whole source in pages, forming character-bounded chunks; keep only the newest CompactionMaxChunks in memory.
        var reader = sp.GetRequiredService<ITranscriptSegmentReader>();
        var chunks = new NewestChunks(options.Summary.CompactionChunkChars, options.Summary.CompactionMaxChunks);
        long afterSeq = 0;
        while (true)
        {
            var page = await reader.ReadFinalsAfterAsync(st.UserId, st.SessionId, afterSeq, 200, ct);
            if (page.Count == 0) break;
            foreach (var s in page) chunks.Add(s);
            afterSeq = page[^1].Seq;
            if (page.Count < 200) break;
        }
        var (failure, row) = await RederiveAsync(st.UserId, st.SessionId, latest, chunks.Complete(), "Draft", null, sp, ct);
        if (row is not null) await PublishRevisionAsync(publisher, st.UserId, st.SessionId, row, ct);
        return failure;
    }

    /// <summary>
    /// PO-18d: the FINAL summary revision, made only as part of a deliberate "Make notes" press on a stopped session (never on Stop
    /// itself, B3-06). The same re-derivation as compaction, over <paramref name="source"/> (the stored finals the press read), with
    /// status <c>Final</c> and workflow <see cref="AiWorkflow.FinalSummary"/>, which the gateway runs only while press
    /// <paramref name="pressId"/> is pending. Returns the stored revision, or the failure code (no revision is stored then).
    /// </summary>
    public async Task<(int? Revision, string? Failure)> FinalAsync(Guid userId, Guid sessionId, Guid pressId, IReadOnlyList<FinalSegment> source, IServiceProvider sp, CancellationToken ct)
    {
        var latest = await sp.GetRequiredService<SummaryStore>().LatestAsync(sessionId, ct);
        var plan = Plan(source);
        if (plan.Kept.Count == 0) return (null, null);
        var (failure, row) = await RederiveAsync(userId, sessionId, latest, plan, "Final", pressId, sp, ct);
        if (row is null) return (null, failure ?? AiFailure.Unavailable);
        await PublishRevisionAsync(sp.GetRequiredService<ILiveEventPublisher>(), userId, sessionId, row, ct);
        return (row.Revision, null);
    }

    /// <summary>The requests a final summary starts with, for the per-press cost estimate (later chunks also carry the topics so far, so this is an estimate).</summary>
    public IReadOnlyList<AiRequest> PlanFinalRequests(Guid userId, Guid sessionId, Guid pressId, SummaryRevisionRow? latest, IReadOnlyList<FinalSegment> source)
    {
        var plan = Plan(source);
        var carriedOver = latest is not null && plan.Dropped;
        IReadOnlyList<TopicState> topics = carriedOver ? SummaryJson.Read<List<TopicState>>(latest!.TopicsJson) : [];
        return plan.Kept.Select(c => SummaryRequests.BuildDelta(userId, sessionId, latest?.Revision ?? 0, topics, [], c, options.Summary, options.CallTimeout,
            compaction: true, workflow: AiWorkflow.FinalSummary, pressId: pressId).Request).ToList();
    }

    private NewestChunks.Result Plan(IReadOnlyList<FinalSegment> source)
    {
        var chunks = new NewestChunks(options.Summary.CompactionChunkChars, options.Summary.CompactionMaxChunks);
        foreach (var s in source) chunks.Add(s);
        return chunks.Complete();
    }

    /// <summary>Re-derives a revision from source chunks and appends it. <paramref name="latest"/> may be null (a final summary of a lecture that never got a live one).</summary>
    private async Task<(string? Failure, SummaryRevisionRow? Row)> RederiveAsync(Guid userId, Guid sessionId, SummaryRevisionRow? latest, NewestChunks.Result plan,
        string status, Guid? pressId, IServiceProvider sp, CancellationToken ct)
    {
        var chunks = plan.Kept;
        if (chunks.Count == 0) return (null, null);
        var gateway = sp.GetRequiredService<AiGateway>();
        var baseRevision = latest?.Revision ?? 0;
        var carriedOver = latest is not null && plan.Dropped; // older chunks were not re-read: start from the existing summary
        IReadOnlyList<TopicState> topics = carriedOver ? SummaryJson.Read<List<TopicState>>(latest!.TopicsJson) : [];
        var tldr = latest?.Tldr ?? "";
        var terms = new List<string>();
        string model = "";
        var workflow = status == "Final" ? AiWorkflow.FinalSummary : AiWorkflow.Compaction;
        foreach (var chunk in chunks)
        {
            var built = SummaryRequests.BuildDelta(userId, sessionId, baseRevision, topics, [], chunk, options.Summary, options.CallTimeout, compaction: true, workflow: workflow, pressId: pressId);
            var ctx = new SummaryContext(baseRevision, built.Allowed);
            var outcome = await gateway.RunAsync(built.Request, json => LanguageGuard.Apply(SummaryValidator.Validate(json, ctx), d => LanguageGuard.Texts(d, ctx), options.LanguageGuardEnforce, log, "RollingSummary"), ct);
            if (!outcome.Ok) return (outcome.Failure, null);
            topics = SummaryMerger.Merge(topics, outcome.Value!);
            tldr = outcome.Value!.Tldr;
            terms.AddRange(outcome.Value.NewTerms);
            model = outcome.Model;
        }

        var all = chunks.SelectMany(c => c).ToList();
        var row = new SummaryRevisionRow
        {
            SessionId = sessionId, UserId = userId, Revision = baseRevision + 1, Kind = "Compaction", Status = status,
            BasedOnSeq = all.Max(s => s.Seq), SourceFromSeq = carriedOver ? latest!.SourceFromSeq : all.Min(s => s.Seq), SourceToSeq = all.Max(s => s.Seq),
            CoveredFromMs = carriedOver ? latest!.CoveredFromMs : all.Min(s => s.StartMs), CoveredToMs = Math.Max(latest?.CoveredToMs ?? 0, all.Max(s => s.EndMs)),
            Tldr = tldr, TopicsJson = SummaryJson.Write(topics), TermsJson = SummaryJson.Write(terms.Distinct(StringComparer.OrdinalIgnoreCase).TakeLast(30).ToList()),
            CitationsJson = SummaryJson.Write(SummaryMerger.Citations(topics)), Model = model, PromptVersion = Prompts.PromptLibrary.RollingSummaryVersion, CreatedAt = clock.GetUtcNow(),
        };
        return await sp.GetRequiredService<SummaryStore>().AppendAsync(row, baseRevision, ct) switch
        {
            AppendResult.Ok => (null, row),
            AppendResult.SessionGone => (AiFailure.SessionClosed, null),
            _ => (AiFailure.InvalidOutput, null),
        };
    }

    private Task PublishRevisionAsync(ILiveEventPublisher publisher, SessionSummaryState st, SummaryRevisionRow row, CancellationToken ct) =>
        PublishRevisionAsync(publisher, st.UserId, st.SessionId, row, ct);

    private async Task PublishRevisionAsync(ILiveEventPublisher publisher, Guid userId, Guid sessionId, SummaryRevisionRow row, CancellationToken ct)
    {
        try { await publisher.PublishEphemeralAsync(userId, sessionId, "summary.revision", SummaryJson.ToPayload(row), ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Summary publish failed: {ExceptionType}", ex.GetType().Name); }
    }

    private async Task PublishStatusAsync(ILiveEventPublisher publisher, SessionSummaryState st, string state, int lagSeconds, string? reason, bool always, CancellationToken ct)
    {
        var key = state + "/" + reason;
        lock (st.Lock)
        {
            if (!always && st.LastStatusKey == key) return;
            st.LastStatusKey = key;
        }
        try { await publisher.PublishEphemeralAsync(st.UserId, st.SessionId, "summary.status", new SummaryStatusPayload(state, lagSeconds, reason), ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Summary status publish failed: {ExceptionType}", ex.GetType().Name); }
    }
}
