using System.Collections.Concurrent;

namespace Tether.Transcript.Application;

/// <summary>
/// Latest <c>transcript.partial</c> per segment, held in memory per (user, session) only (C-11). Never persisted, never logged.
/// Cleared on pause, stop and delete. A final for the segment removes its partial.
/// </summary>
internal sealed class PartialStore
{
    private readonly ConcurrentDictionary<(Guid User, Guid Session), ConcurrentDictionary<int, Envelope>> _partials = new();

    public void Set(Guid userId, Guid sessionId, int segmentNo, Envelope partial) =>
        _partials.GetOrAdd((userId, sessionId), _ => new()).AddOrUpdate(segmentNo, partial, (_, _) => partial);

    public void Remove(Guid userId, Guid sessionId, int segmentNo)
    {
        if (_partials.TryGetValue((userId, sessionId), out var d)) d.TryRemove(segmentNo, out _);
    }

    public void Clear(Guid userId, Guid sessionId) => _partials.TryRemove((userId, sessionId), out _);

    public IReadOnlyList<Envelope> Snapshot(Guid userId, Guid sessionId) =>
        _partials.TryGetValue((userId, sessionId), out var d) ? d.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList() : [];
}
