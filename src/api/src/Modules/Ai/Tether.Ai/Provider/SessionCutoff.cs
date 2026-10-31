using Tether.Lectures.Contracts;
using Tether.SharedKernel.Ai;

namespace Tether.Ai.Provider;

/// <summary>
/// Per-session cancellation for AI work (B3-06, Product Owner decision: no transcript text goes to a vendor after the student pauses,
/// stops or deletes). Every provider call runs under a <see cref="Lease"/> that is cancelled the moment the session leaves Active.
/// It listens through the existing <see cref="ISessionLifecycleObserver"/> contract, so no module reference is added.
/// A lease is taken BEFORE the gateway re-checks the session state, so a pause that commits after the check still cancels the call,
/// and one that commits before it is seen by the check. In-process only, like the other session registries (ADR-020d).
/// </summary>
internal sealed class SessionCutoff(TimeProvider clock) : ISessionLifecycleObserver, IAiScopeCutoff
{
    /// <summary>
    /// NAS-01: how long a deleted lecture scope stays refused. A course purge notifies BEFORE its delete commits (to stop recording and
    /// cut off in-flight calls), so for that gap the session still exists and is Stopped; without a tombstone a notes job between two
    /// calls, a queued press dequeued in the gap, or a new press could start a vendor call. Ten minutes covers any purge transaction with
    /// a wide margin; a purge that rolls back leaves its (already stopped) sessions refused for AI for that long, which is the safe side.
    /// </summary>
    public static readonly TimeSpan TombstoneFor = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private readonly Dictionary<AiScope, List<CancellationTokenSource>> _leases = [];
    private readonly Dictionary<AiScope, DateTimeOffset> _tombstones = [];

    public int TrackedSessions { get { lock (_gate) return _leases.Count; } }

    /// <summary>A token that is cancelled when <paramref name="ct"/> is, or when the session is paused, stopped or deleted. Dispose it when the call ends.</summary>
    public Lease Acquire(Guid sessionId, CancellationToken ct) => Acquire(AiScope.Lecture(sessionId), ct);

    /// <summary>A lease on a tombstoned (deleted or being-purged) scope is born cancelled, so the gateway refuses the call before any state check.</summary>
    public Lease Acquire(AiScope scope, CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bool dead;
        lock (_gate)
        {
            dead = IsTombstonedLocked(scope);
            if (!_leases.TryGetValue(scope, out var list)) _leases[scope] = list = [];
            list.Add(cts);
        }
        if (dead) cts.Cancel();
        return new Lease(this, scope, cts);
    }

    /// <summary>True while a lecture scope was deleted (or announced as purged) less than <see cref="TombstoneFor"/> ago.</summary>
    public bool IsTombstoned(Guid sessionId) => IsTombstoned(AiScope.Lecture(sessionId));

    public bool IsTombstoned(AiScope scope) { lock (_gate) return IsTombstonedLocked(scope); }

    private bool IsTombstonedLocked(AiScope scope)
    {
        if (!_tombstones.TryGetValue(scope, out var until)) return false;
        if (clock.GetUtcNow() < until) return true;
        _tombstones.Remove(scope);
        return false;
    }

    private void Tombstone(AiScope scope)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (_tombstones.Count > 10_000) foreach (var (k, v) in _tombstones.ToList()) if (now >= v) _tombstones.Remove(k); // bounded memory
            _tombstones[scope] = now + TombstoneFor;
        }
    }

    public void Cancel(Guid sessionId) => Cancel(AiScope.Lecture(sessionId));

    public void Cancel(string kind, Guid scopeId) => Cancel(new AiScope(kind, scopeId));

    public void Cancel(AiScope scope)
    {
        List<CancellationTokenSource> snapshot;
        lock (_gate) snapshot = _leases.TryGetValue(scope, out var list) ? [.. list] : [];
        foreach (var cts in snapshot)
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* the call finished in the meantime */ }
        }
    }

    public Task OnStateChangedAsync(SessionStateChange change, CancellationToken ct)
    {
        if (change.State != LectureState.Active) Cancel(change.SessionId);
        return Task.CompletedTask;
    }

    /// <summary>Delete, and both course-purge notifications (before and after the commit): refuse the scope from now on, then cut off what is in flight.</summary>
    public Task OnDeletedAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        Tombstone(AiScope.Lecture(sessionId));
        Cancel(sessionId);
        return Task.CompletedTask;
    }

    private void Release(AiScope scope, CancellationTokenSource cts)
    {
        lock (_gate)
        {
            if (_leases.TryGetValue(scope, out var list))
            {
                list.Remove(cts);
                if (list.Count == 0) _leases.Remove(scope);
            }
        }
        cts.Dispose();
    }

    internal sealed class Lease : IDisposable
    {
        private readonly SessionCutoff _owner;
        private readonly AiScope _scope;
        private readonly CancellationTokenSource _cts;
        internal Lease(SessionCutoff owner, AiScope scope, CancellationTokenSource cts) { _owner = owner; _scope = scope; _cts = cts; }
        public CancellationToken Token => _cts.Token;
        public void Dispose() => _owner.Release(_scope, _cts);
    }
}
