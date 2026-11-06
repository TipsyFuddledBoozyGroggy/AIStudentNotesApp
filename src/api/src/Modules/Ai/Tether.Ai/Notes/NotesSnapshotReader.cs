using Tether.Ai.Contracts;
using Tether.Ai.Domain;
using Tether.Ai.Persistence;
using Tether.Ai.Provider;
using Tether.Ai.Summary;
using Tether.Ai.Validation;
using Tether.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Tether.Ai.Notes;

/// <summary>
/// <see cref="INotesSnapshotReader"/>: a read-only copy of the stored notes of the AMBIENT user's session, for the Docs export (invariant 3: no
/// AI call, no prompt, no transcript, no token). Every read goes through <see cref="AiDbContext"/>'s user query filter, so a scope for another user
/// finds nothing; an explicit owner check backs that up. It never starts, expires or changes a press.
/// </summary>
internal sealed class NotesSnapshotReader(AiDbContext db, ICurrentUser currentUser) : INotesSnapshotReader
{
    public async Task<NotesSnapshot?> FindCurrentAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } me) return null;
        var row = await db.Notes.AsNoTracking().Where(n => n.SessionId == sessionId).OrderByDescending(n => n.Revision).FirstOrDefaultAsync(ct);
        if (row is null || row.UserId != me) return null;

        var ready = row.State == NotesStates.Ready;
        var sections = ready
            ? NotesJson.ReadSections(row.SectionsJson)
                .Select(s => new NotesSnapshotSection(s.Heading, s.Bullets.Select(b => new NotesSnapshotBullet(b.Text, b.Citations.Select(c => c.StartMs).ToList())).ToList()))
                .ToList()
            : new List<NotesSnapshotSection>();

        FinalSummarySnapshot? summary = null;
        if (ready && row.FinalSummaryState == FinalSummaryStates.Ready && row.FinalSummaryRevision is { } rev)
        {
            var s = await db.Summaries.AsNoTracking().FirstOrDefaultAsync(x => x.SessionId == sessionId && x.Revision == rev && x.Status == "Final", ct);
            if (s is not null && s.UserId == me)
            {
                var topics = SummaryJson.Read<List<TopicState>>(s.TopicsJson).Select(t => t.Text).ToList();
                var cites = SummaryJson.Read<List<CitationRef>>(s.CitationsJson).Select(c => c.StartMs).ToList();
                summary = new FinalSummarySnapshot(s.Tldr, topics, cites);
            }
        }

        return new NotesSnapshot(sessionId, NotesJson.PublicId(row.Id), row.Revision, row.State, ready ? AiSource.Of(row.Model) : null,
            ready && row.Truncated, ready ? row.CoveredToMs : null, sections, summary);
    }
}
