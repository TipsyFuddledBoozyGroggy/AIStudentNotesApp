using Tether.Lectures.Contracts;

namespace Tether.Ai.Provider;

/// <summary>
/// Per-session cancellation for AI work (B3-06, Product Owner decision: no transcript text goes to a vendor after the student pauses,
/// stops or deletes). Every provider call runs under a <see cref="Lease"/> that is cancelled the moment the session leaves Active.
/// It listens through the existing <see cref="ISessionLifecycleObserver"/> contract, so no module reference is added.
/// A lease is taken BEFORE the gateway re-checks the session state, so a pause that commits after the check still cancels the call,
/// and one that commits before it is seen by the check. In-process only, like the other session registries (ADR-020d).
/// </summary>
internal sealed class SessionCutoff : ISessionLifecycleObserver
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, List<CancellationTokenSource>> _leases = [];

    public int TrackedSessions { get { lock (_gate) return _leases.Count; } }

    /// <summary>A token that is cancelled when <paramref name="ct"/> is, or when the session is paused, stopped or deleted. Dispose it when the call ends.</summary>
    public Lease Acquire(Guid sessionId, CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_gate)
        {
            if (!_leases.TryGetValue(sessionId, out var list)) _leases[sessionId] = list = [];
            list.Add(cts);
        }
        return new Lease(this, sessionId, cts);
    }

    public void Cancel(Guid sessionId)
    {
        List<CancellationTokenSource> snapshot;
        lock (_gate) snapshot = _leases.TryGetValue(sessionId, out var list) ? [.. list] : [];
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

    private void Release(Guid sessionId, CancellationTokenSource cts)
    {
        lock (_gate)
        {
            if (_leases.TryGetValue(sessionId, out var list))
            {
                list.Remove(cts);
                if (list.Count == 0) _leases.Remove(sessionId);
            }
        }
        cts.Dispose();
    }

    internal sealed class Lease : IDisposable
    {
        private readonly SessionCutoff _owner;
        private readonly Guid _sessionId;
        private readonly CancellationTokenSource _cts;
        internal Lease(SessionCutoff owner, Guid sessionId, CancellationTokenSource cts) { _owner = owner; _sessionId = sessionId; _cts = cts; }
        public CancellationToken Token => _cts.Token;
        public void Dispose() => _owner.Release(_sessionId, _cts);
    }
}
