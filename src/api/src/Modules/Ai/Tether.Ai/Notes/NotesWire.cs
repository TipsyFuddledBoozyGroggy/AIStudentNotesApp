using System.Text.Json;
using Tether.Ai.Domain;
using Tether.Ai.Prompts;
using Tether.Ai.Provider;
using Tether.Ai.Summary;
using Tether.Ai.Validation;
using Tether.Transcript.Contracts;

namespace Tether.Ai.Notes;

/// <summary>Wire shape of <c>POST</c>/<c>GET /sessions/{id}/notes</c>: must keep validating against lecture.v1/notes.schema.json (additionalProperties:false).</summary>
internal sealed record NotesWire(
    string NotesId, int Revision, string Status, string? Source, DateTimeOffset RequestedAt, DateTimeOffset? GeneratedAt,
    long? CoveredFromMs, long? CoveredToMs, IReadOnlyList<NotesSection> Sections, int? FinalSummaryRevision, bool Truncated);

internal static class NotesJson
{
    /// <summary>Web defaults; nulls ARE written (every field is required by the schema, several are nullable).</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public const int MaxSections = 50;

    public static string PublicId(Guid id) => "nt_" + id.ToString("N");

    public static string WriteSections(IReadOnlyList<NotesSection> sections) => JsonSerializer.Serialize(sections, Options);

    public static IReadOnlyList<NotesSection> ReadSections(string json) => JsonSerializer.Deserialize<List<NotesSection>>(json, Options) ?? [];

    /// <summary>Only a Ready row carries content and a source; every other state is an empty shell (the schema's if/then/else).</summary>
    public static NotesWire ToWire(LectureNotesRow r)
    {
        var ready = r.State == NotesStates.Ready;
        return new NotesWire(PublicId(r.Id), r.Revision, r.State, ready ? AiSource.Of(r.Model) : null, r.RequestedAt, ready ? r.CompletedAt : null,
            ready ? r.CoveredFromMs : null, ready ? r.CoveredToMs : null, ready ? ReadSections(r.SectionsJson) : [], ready ? r.FinalSummaryRevision : null,
            ready && r.Truncated);
    }
}

/// <summary>Splits the stored finals of a stopped lecture into the bounded parts that one press may send. Pure.</summary>
internal static class NotesRequests
{
    public sealed record Part(AiRequest Request, NotesContext Context, IReadOnlyList<FinalSegment> Segments);

    public sealed record Plan(IReadOnlyList<Part> Parts, IReadOnlyList<FinalSegment> Used, bool Truncated);

    /// <summary>
    /// Oldest first: parts of at most <see cref="NotesOptions.ChunkChars"/> prompt characters, at most <see cref="NotesOptions.MaxChunks"/>
    /// of them. A longer lecture is cut at the END (notes keep the start; <c>coveredToMs</c> and <c>truncated</c> say so).
    /// </summary>
    public static Plan Build(Guid userId, Guid sessionId, Guid pressId, IReadOnlyList<FinalSegment> finals, bool readTruncated, NotesOptions o)
    {
        var groups = new List<List<FinalSegment>>();
        var current = new List<FinalSegment>();
        var used = 0;
        var truncated = readTruncated;
        foreach (var s in finals)
        {
            var len = NewestChunks.Cost(s);
            if (used + len > o.ChunkChars && current.Count > 0)
            {
                groups.Add(current); current = []; used = 0;
                if (groups.Count >= Math.Max(1, o.MaxChunks)) { truncated = true; break; }
            }
            current.Add(s); used += len;
        }
        if (current.Count > 0 && groups.Count < Math.Max(1, o.MaxChunks)) groups.Add(current);

        var parts = new List<Part>();
        for (var i = 0; i < groups.Count; i++)
        {
            var lines = groups[i].Select(SummaryRequests.ToLine).ToList();
            var allowed = groups[i].ToDictionary(s => s.SegmentId, SummaryRequests.ToLine, StringComparer.Ordinal);
            var req = new AiRequest(AiWorkflow.Notes, PromptLibrary.NotesVersion, AiTier.Balanced, PromptLibrary.Notes, $"part={i + 1} of {groups.Count}",
                [new UntrustedBlock(BlockKinds.SourceChunk, lines)], o.MaxOutputTokens, userId, sessionId, $"notes:{pressId:N}:{i + 1}", o.Timeout, PressId: pressId);
            parts.Add(new Part(req, new NotesContext(allowed), groups[i]));
        }
        return new Plan(parts, groups.SelectMany(g => g).ToList(), truncated);
    }
}
