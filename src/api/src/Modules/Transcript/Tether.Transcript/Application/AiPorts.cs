using Tether.SharedKernel;
using Tether.Transcript.Contracts;
using Tether.Transcript.Domain;
using Tether.Transcript.Hub;
using Tether.Transcript.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Tether.Transcript.Application;

/// <summary>
/// B3 read port over the source transcript (<see cref="ITranscriptSegmentReader"/>). Reads only persisted FINAL events, applies the
/// newest correction text, and refuses to read for any user other than the current one (defence in depth on top of the EF filter).
/// </summary>
internal sealed class TranscriptSegmentReader(TranscriptDbContext db, ICurrentUser current) : ITranscriptSegmentReader
{
    private bool Mine(Guid userId) => current.UserId == userId;

    public async Task<IReadOnlyList<FinalSegment>> ReadFinalsAfterAsync(Guid userId, Guid sessionId, long afterSeq, int limit, CancellationToken ct = default)
    {
        if (!Mine(userId) || limit < 1) return [];
        var finals = await db.Events.AsNoTracking()
            .Where(e => e.SessionId == sessionId && e.Kind == TranscriptEventKind.Final && e.Seq > afterSeq)
            .OrderBy(e => e.Seq).Take(limit).ToListAsync(ct);
        return await WithCorrectionsAsync(sessionId, finals, ct);
    }

    public async Task<IReadOnlyList<FinalSegment>> ReadFinalsUpToAsync(Guid userId, Guid sessionId, long upToSeq, int limit, CancellationToken ct = default)
    {
        if (!Mine(userId) || limit < 1) return [];
        var finals = await db.Events.AsNoTracking()
            .Where(e => e.SessionId == sessionId && e.Kind == TranscriptEventKind.Final && e.Seq <= upToSeq)
            .OrderByDescending(e => e.Seq).Take(limit).ToListAsync(ct);
        finals.Reverse();
        return await WithCorrectionsAsync(sessionId, finals, ct);
    }

    public async Task<SegmentSlice> ReadWindowAsync(Guid userId, Guid sessionId, long fromMs, long toMs, int limit, CancellationToken ct = default)
    {
        if (!Mine(userId) || limit < 1) return new SegmentSlice([], false);
        var finals = await db.Events.AsNoTracking()
            .Where(e => e.SessionId == sessionId && e.Kind == TranscriptEventKind.Final && e.EndMs > fromMs && e.EndMs <= toMs)
            .OrderByDescending(e => e.Seq).Take(limit + 1).ToListAsync(ct);
        var truncated = finals.Count > limit;
        if (truncated) finals.RemoveAt(finals.Count - 1);
        finals.Reverse();
        return new SegmentSlice(await WithCorrectionsAsync(sessionId, finals, ct), truncated);
    }

    public async Task<long?> LatestEndMsAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        if (!Mine(userId)) return null;
        return await db.Events.AsNoTracking()
            .Where(e => e.SessionId == sessionId && e.Kind == TranscriptEventKind.Final)
            .MaxAsync(e => e.EndMs, ct);
    }

    private async Task<IReadOnlyList<FinalSegment>> WithCorrectionsAsync(Guid sessionId, List<TranscriptEvent> finals, CancellationToken ct)
    {
        if (finals.Count == 0) return [];
        var nos = finals.Select(f => f.SegmentNo).ToList();
        var corrections = (await db.Events.AsNoTracking()
            .Where(e => e.SessionId == sessionId && e.Kind == TranscriptEventKind.Correction && nos.Contains(e.SegmentNo))
            .ToListAsync(ct))
            .GroupBy(e => e.SegmentNo!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Revision).First().Text!);
        return finals.Select(f => new FinalSegment(sessionId, f.Seq, f.SegmentNo!.Value, f.StartMs!.Value, f.EndMs!.Value,
            corrections.TryGetValue(f.SegmentNo!.Value, out var t) ? t : f.Text!, f.Confidence)).ToList();
    }
}

/// <summary>
/// Gives the AI module a way to reach the session's hub group without a reference to hub types. Accepts only the three
/// AI event types, so it cannot be used to forge transcript, state or error events.
/// </summary>
internal sealed class LiveEventPublisher(LiveFanout fanout, TimeProvider clock) : ILiveEventPublisher
{
    private static readonly HashSet<string> Allowed = ["summary.revision", "summary.status", "catchup.ready"];

    public Task PublishEphemeralAsync(Guid userId, Guid sessionId, string type, object payload, CancellationToken ct = default)
    {
        if (!Allowed.Contains(type)) throw new ArgumentException("Event type is not publishable by this port.", nameof(type));
        return fanout.PublishAsync(userId, sessionId, Envelope.Ephemeral(type, sessionId, clock.GetUtcNow(), payload), ct);
    }
}
