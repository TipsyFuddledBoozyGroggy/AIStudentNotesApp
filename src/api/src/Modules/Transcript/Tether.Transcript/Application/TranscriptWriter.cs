using Tether.Lectures.Contracts;
using Tether.SharedKernel.Persistence;
using Tether.Transcript.Domain;
using Tether.Transcript.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tether.Transcript.Application;

/// <summary>
/// Persists one source-transcript event with its seq. The seq comes from the Lectures counter inside THIS transaction, and the
/// allocation also verifies that the session still exists and is Active, so a late write after pause, stop or delete is
/// rejected (returns null and nothing is kept). Run it inside a scope for the owning user.
/// </summary>
internal sealed class TranscriptWriter(TranscriptDbContext db, ISessionSequencer sequencer, TimeProvider clock)
{
    internal const int MaxAttempts = 3;

    /// <remarks>
    /// A session delete takes the session row and then (cascade) the counter row; this write takes the counter row (seq allocation)
    /// and then KEY SHARE on the session row (FK check of the new row). PostgreSQL aborts one side with 40P01. When the delete is
    /// the victim it retries (SessionService.DeleteAsync); when this write is, the transaction rolled back and nothing was kept, so
    /// the write is retried: the retry's allocation sees the committed delete and returns null (the relay closes 4409), or, if the
    /// session is still there, the event is written rather than lost. Any other failure, or a deadlock that persists past
    /// <see cref="MaxAttempts"/>, still throws (the relay closes 1011).
    /// </remarks>
    public async Task<Envelope?> AppendAsync(Guid userId, Guid sessionId, Action<TranscriptEvent> fill, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return await TryAppendAsync(userId, sessionId, fill, ct); }
            catch (Exception ex) when (attempt < MaxAttempts && PgErrors.IsDeadlockOrSerialization(ex))
            {
                db.ChangeTracker.Clear(); // the failed insert is still tracked as Added
                await Task.Delay(Random.Shared.Next(5, 40), ct); // jitter; the other side holds the locks only briefly
            }
        }
    }

    private async Task<Envelope?> TryAppendAsync(Guid userId, Guid sessionId, Action<TranscriptEvent> fill, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var e = new TranscriptEvent { SessionId = sessionId, UserId = userId, OccurredAt = new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero) };
        fill(e);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var seq = await sequencer.AllocateAsync(tx.GetDbTransaction(), userId, sessionId, ct);
        if (seq is null)
        {
            await tx.RollbackAsync(ct);
            return null;
        }
        e.Seq = seq.Value;
        db.Events.Add(e);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return EventMapper.ToEnvelope(e);
    }

    public async Task<int> MaxSegmentNoAsync(Guid sessionId, CancellationToken ct) =>
        await db.Events.AsNoTracking().Where(e => e.SessionId == sessionId && e.SegmentNo != null).MaxAsync(e => e.SegmentNo, ct) ?? 0;

    /// <summary>Highest revision of a segment so far; the original final is revision 1.</summary>
    public async Task<int> CurrentRevisionAsync(Guid sessionId, int segmentNo, CancellationToken ct) =>
        await db.Events.AsNoTracking().Where(e => e.SessionId == sessionId && e.SegmentNo == segmentNo && e.Kind == TranscriptEventKind.Correction)
            .MaxAsync(e => e.Revision, ct) ?? 1;

    public async Task<bool> FinalExistsAsync(Guid sessionId, int segmentNo, CancellationToken ct) =>
        await db.Events.AsNoTracking().AnyAsync(e => e.SessionId == sessionId && e.SegmentNo == segmentNo && e.Kind == TranscriptEventKind.Final, ct);

    public async Task<long> LoadAcceptedMsAsync(Guid sessionId, CancellationToken ct) =>
        await db.Progress.AsNoTracking().Where(p => p.SessionId == sessionId).Select(p => (long?)p.AcceptedMs).FirstOrDefaultAsync(ct) ?? 0;

    /// <summary>Upserts how much audio was accepted. A deleted session makes the FK fail; the caller treats that as gone.</summary>
    public async Task SaveAcceptedMsAsync(Guid userId, Guid sessionId, long acceptedMs, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var row = await db.Progress.FirstOrDefaultAsync(p => p.SessionId == sessionId, ct);
        if (row is null) db.Progress.Add(new AudioProgress { SessionId = sessionId, UserId = userId, AcceptedMs = acceptedMs, UpdatedAt = now });
        else { row.AcceptedMs = Math.Max(row.AcceptedMs, acceptedMs); row.UpdatedAt = now; }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Reads persisted events for replay and paging, merging the session.state events owned by Lectures by seq (section 13.1).</summary>
internal sealed class TranscriptReader(TranscriptDbContext db, ISessionEventReader states)
{
    public async Task<TranscriptPage> ReadAfterAsync(Guid userId, Guid sessionId, long afterSeq, int limit, CancellationToken ct)
    {
        var transcript = await db.Events.AsNoTracking()
            .Where(e => e.SessionId == sessionId && e.Seq > afterSeq).OrderBy(e => e.Seq).Take(limit + 1).ToListAsync(ct);
        var state = await states.ReadAfterAsync(userId, sessionId, afterSeq, limit + 1, ct);
        // Each source is ordered and capped at limit+1, so the first limit+1 of the union are exactly the next events.
        var merged = transcript.Select(EventMapper.ToEnvelope).Concat(state.Select(s => EventMapper.ToEnvelope(sessionId, s)))
            .OrderBy(e => long.Parse(e.Seq!, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var hasMore = merged.Count > limit;
        return new TranscriptPage(hasMore ? merged.Take(limit).ToList() : merged, hasMore);
    }
}
