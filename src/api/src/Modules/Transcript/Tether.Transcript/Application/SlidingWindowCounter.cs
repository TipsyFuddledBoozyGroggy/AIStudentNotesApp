using System.Collections.Concurrent;

namespace Tether.Transcript.Application;

/// <summary>
/// Per-key sliding-window hit counter with bounded memory (B2-02). Idle keys are swept at least every half window, and the
/// number of tracked keys is capped: when the table is full, <see cref="TryAcquire"/> refuses new keys (fail closed) and
/// <see cref="Record"/> drops them. In process only; with more than one instance every limit multiplies (single-instance
/// deployment, docs/architecture/m2-b2-realtime.md).
/// </summary>
internal sealed class SlidingWindowCounter<TKey>(int limit, TimeSpan window, TimeProvider clock, int maxKeys) where TKey : notnull
{
    private sealed class Bucket
    {
        public readonly Queue<long> Hits = new();
        public bool Dead;
    }

    private readonly ConcurrentDictionary<TKey, Bucket> _buckets = new();
    private long _lastSweep;

    public int TrackedKeys => _buckets.Count;

    /// <summary>Counts a hit if the key is under its limit. False means refused.</summary>
    public bool TryAcquire(TKey key) => Hit(key, enforce: true);

    /// <summary>Counts a hit unconditionally (no limit check); silently dropped if the table is full.</summary>
    public void Record(TKey key) => Hit(key, enforce: false);

    public int Count(TKey key)
    {
        var now = clock.GetUtcNow().UtcTicks;
        MaybeSweep(now);
        if (!_buckets.TryGetValue(key, out var b)) return 0;
        lock (b)
        {
            if (b.Dead) return 0;
            Prune(b, now);
            return b.Hits.Count;
        }
    }

    public void Forget(TKey key) => _buckets.TryRemove(key, out _);

    private bool Hit(TKey key, bool enforce)
    {
        var now = clock.GetUtcNow().UtcTicks;
        MaybeSweep(now);
        while (true)
        {
            if (!_buckets.TryGetValue(key, out var b))
            {
                if (_buckets.Count >= maxKeys)
                {
                    Sweep(now);
                    if (_buckets.Count >= maxKeys) return false;
                }
                b = _buckets.GetOrAdd(key, _ => new Bucket());
            }
            lock (b)
            {
                if (b.Dead) continue; // swept between lookup and lock: take a fresh bucket
                Prune(b, now);
                if (enforce && b.Hits.Count >= limit) return false;
                b.Hits.Enqueue(now);
                return true;
            }
        }
    }

    private void Prune(Bucket b, long now)
    {
        while (b.Hits.Count > 0 && now - b.Hits.Peek() >= window.Ticks) b.Hits.Dequeue();
    }

    private void MaybeSweep(long now)
    {
        var last = Interlocked.Read(ref _lastSweep);
        if (now - last < window.Ticks / 2) return;
        if (Interlocked.CompareExchange(ref _lastSweep, now, last) != last) return;
        Sweep(now);
    }

    private void Sweep(long now)
    {
        foreach (var (key, b) in _buckets)
        {
            lock (b)
            {
                Prune(b, now);
                if (b.Hits.Count > 0) continue;
                b.Dead = true;
                _buckets.TryRemove(new KeyValuePair<TKey, Bucket>(key, b));
            }
        }
    }
}
