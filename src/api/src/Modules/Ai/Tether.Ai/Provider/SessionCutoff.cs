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
internal sealed class SessionCutoff : ISessionLifecycleObserver, IAiScopeCutoff
{
    private readonly object _gate = new();
    private readonly Dictionary<AiScope, List<CancellationTokenSource>> _leases = [];

    public int TrackedSessions { get { lock (_gate) return _leases.Count; } }

    /// <summary>A token that is cancelled when <paramref name="ct"/> is, or when the session is paused, stopped or deleted. Dispose it when the call ends.</summary>
    public Lease Acquire(Guid sessionId, CancellationToken ct) => Acquire(AiScope.Lecture(sessionId), ct);

    public Lease Acquire(AiScope scope, CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_gate)
        {
            if (!_leases.TryGetValue(scope, out var list)) _leases[scope] = list = [];
            list.Add(cts);
        }
        return new Lease(this, scope, cts);
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

    public Task OnDeletedAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
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
