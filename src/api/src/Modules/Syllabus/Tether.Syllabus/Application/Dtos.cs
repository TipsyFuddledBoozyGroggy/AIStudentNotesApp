using Tether.SharedKernel;
using Tether.Syllabus.Domain;

namespace Tether.Syllabus.Application;

// ---- requests (separate from entities, never bound onto them; unknown members are rejected by the serializer) ----

internal sealed class AttestationDto
{
    public required bool Accepted { get; init; }
    public required string AcceptedAt { get; init; }
}

internal sealed class CreateSyllabusRequest
{
    public string? Title { get; init; }
    public string? Text { get; init; }
    public string? TermStart { get; init; }
    public string? TermEnd { get; init; }
    public required string DefaultTimezone { get; init; }
    public bool AllowDuplicate { get; init; }
    public required string NoticeVersion { get; init; }
    public required AttestationDto Attestation { get; init; }
}

internal sealed class ItemCreateRequest
{
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public string? Notes { get; init; }
    public required string Date { get; init; }
    public string? EndDate { get; init; }
    public string? Time { get; init; }
    public string? EndTime { get; init; }
    public bool? AllDay { get; init; }
    public string? Timezone { get; init; }
}

internal sealed class DecisionRequest
{
    public required string Decision { get; init; }
    public bool? AcknowledgeCheck { get; init; }
}

internal sealed class BatchRequest
{
    public required string Action { get; init; }
    public required Dictionary<string, string> ItemEtags { get; init; }
}

internal sealed class SyncConfirmDto
{
    public required string Provider { get; init; }
    public required bool AcknowledgedDemo { get; init; }
}

internal sealed class SyncRequest
{
    public required Dictionary<string, int> ItemRevisions { get; init; }
    public required SyncConfirmDto Confirm { get; init; }
}

internal sealed class RemoveRequest
{
    public required List<string> ItemIds { get; init; }
}

// ---- responses (field names and nullability follow syllabus.v1 exactly; every property is required on the wire, null included) ----

internal sealed record DocumentFacts(int? PageCount, int CharCount, int[] UnreadablePages, int HiddenContentRemoved, bool InjectionSuspect, bool HiddenContentDropped);
internal sealed record UnprocessedRangeDto(int FromPage, int ToPage, string Reason);
internal sealed record ContextDto(string? TermStart, string? TermEnd, string DefaultTimezone);
internal sealed record ProvenanceDto(string ExtractorVersion, string? PromptVersion, string SchemaVersion, string? Model);
internal sealed record CountsDto(int Total, int Undecided, int Approved, int Skipped, int NeedsDate, int Synced, int DroppedInvalid, int UnclaimedDates);
internal sealed record NoticeDto(string Version, bool Attested, DateTimeOffset AttestedAt);
internal sealed record RetentionDto(bool CanonicalTextPurged, DateTimeOffset? CanonicalTextPurgeAt);

internal sealed record SyllabusResponse(
    string Id, string CourseId, string? Title, string InputKind, string? FileType, string Status, string? StatusReason, int? PollAfterSeconds,
    string AiSource, bool DemoSource, bool DemoCalendar, bool OriginalRetained, string ContentSha256, DocumentFacts Document,
    UnprocessedRangeDto[] UnprocessedRanges, ContextDto Context, ProvenanceDto Provenance, CountsDto Counts, string Etag,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool BatchApproveAllowed, NoticeDto Notice, RetentionDto Retention);

internal sealed record WrittenParts(int? Year, int? Month, int? Day, string? Weekday);
internal sealed record WrittenDto(string? DateText, string? TimeText, WrittenParts Parts);
internal sealed record AlternativeDto(string Date, string? Time);
internal sealed record WhenDto(string Status, WrittenDto? Written, string? Date, string? EndDate, string? Time, string? EndTime, bool AllDay,
    string? Timezone, string? TimezoneSource, string? Derivation, string? UnknownReason, AlternativeDto[] Alternatives);
internal sealed record RecurrenceDto(string Pattern, string[] ByDay, string StartTime, string? EndTime, string From, string Until, string UntilSource, string[] Exceptions);
internal sealed record ContextSliceDto(string Text, int Start, int End);
internal sealed record SnippetDto(string? Text, int? Start, int? End, int? Page, string Precision, ContextSliceDto? Context);

internal sealed record ItemResponse(
    string Id, string SyllabusId, string CourseId, string ItemKey, string Origin, string Kind, string Title, string? Notes, WhenDto When,
    RecurrenceDto? Recurrence, string Confidence, string ConfidenceReason, string[] Flags, SnippetDto? Snippet, bool Edited, string Decision,
    string SyncState, int? SyncedRevision, int Revision, string Etag, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

internal sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

internal sealed record BatchResultItem(string ItemId, string Outcome, string? ErrorCode, ItemResponse? Item);
internal sealed record BatchCounts(int Applied, int Unchanged, int Rejected);
internal sealed record BatchResult(IReadOnlyList<BatchResultItem> Results, BatchCounts Counts);

internal sealed record SyncResultItem(string ItemId, string ItemKey, string Outcome, string? ErrorCode, int EventCount, string SyncState);
internal sealed record SyncCounts(int Created, int Updated, int Unchanged, int Failed, int NotEligible);
internal sealed record SyncResult(string SyncId, string SyllabusId, string Provider, bool DemoCalendar, string Status, DateTimeOffset CompletedAt,
    IReadOnlyList<SyncResultItem> Results, SyncCounts Counts);
internal sealed record RemoveCounts(int Removed, int NotPresent, int Failed);
internal sealed record RemoveResult(string SyllabusId, string Provider, string Status, IReadOnlyList<SyncResultItem> Results, RemoveCounts Counts);

internal sealed record EventEnd(string Date, string? Time);
internal sealed record CalendarEventDto(string EventId, string SyllabusId, string ItemId, string ItemKey, string? OccurrenceDate, string Title,
    string Kind, bool AllDay, EventStart Start, EventEnd End, bool EndDerived, int SyncedRevision, DateTimeOffset SyncedAt, bool Demo);
internal sealed record EventStart(string Date, string? Time, string Timezone);

internal sealed record UnclaimedDto(string Id, SnippetDto Snippet);
internal sealed record ExportDto(string SchemaVersion, DateTimeOffset ExportedAt, SyllabusResponse Syllabus, IReadOnlyList<ItemResponse> Items,
    IReadOnlyList<UnclaimedDto> UnclaimedDates, IReadOnlyList<CalendarEventDto> CalendarEvents);

internal static class Mapping
{
    public static SyllabusResponse ToResponse(this SyllabusDoc s, CountsDto counts, SyllabusETag etags)
    {
        var inProgress = s.Status is "queued" or "extracting" or "proposing";
        var ranges = System.Text.Json.JsonSerializer.Deserialize<UnprocessedRangeDto[]>(s.UnprocessedRanges, JsonBody.Out) ?? [];
        return new SyllabusResponse(
            Ids2.Syl(s.Id), Ids2.Course(s.CourseId), s.Title, s.InputKind, s.FileType, s.Status, s.StatusReason,
            inProgress ? 2 : null, s.AiSource, s.DemoSource, true, false, s.ContentSha256,
            new DocumentFacts(s.PageCount, s.CharCount, [.. s.UnreadablePages.Select(p => (int)p)], s.HiddenContentRemoved, s.InjectionSuspect, s.HiddenContentDropped),
            ranges, new ContextDto(Local.DateOrNull(s.TermStart), Local.DateOrNull(s.TermEnd), s.DefaultTimezone),
            new ProvenanceDto(s.ExtractorVersion ?? "mock-rules-1", s.PromptVersion, s.SchemaVersion ?? "syllabus.extract.v1", s.Model),
            counts, etags.ForSyllabus(s), s.CreatedAt, s.UpdatedAt, s.BatchApproveAllowed,
            new NoticeDto(s.NoticeVersion, true, s.NoticeAttestedAt),
            new RetentionDto(s.CanonicalTextPurgedAt is not null, s.CanonicalTextPurgeAt));
    }

    public static SnippetDto? Snippet(int? start, int? end, short? page, string? precision, string? text, string? context, int? ctxStart, int? ctxEnd)
    {
        if (precision is null) return null;
        if (text is null) return new SnippetDto(null, null, null, page, precision, null);   // after the retention purge: all four null together
        return new SnippetDto(text, start, end, page, precision,
            context is null || ctxStart is null || ctxEnd is null ? null : new ContextSliceDto(context, ctxStart.Value, ctxEnd.Value));
    }

    public static ItemResponse ToResponse(this SyllabusItem i, IReadOnlyList<ItemAlternative> alternatives, SyllabusETag etags)
    {
        var written = i.WrittenDateText is null && i.WrittenTimeText is null && i.WrittenYear is null && i.WrittenMonth is null && i.WrittenDay is null && i.WrittenWeekday is null
            ? null
            : new WrittenDto(i.WrittenDateText, i.WrittenTimeText, new WrittenParts(i.WrittenYear, i.WrittenMonth, i.WrittenDay, i.WrittenWeekday?.Trim()));
        var alts = alternatives.OrderBy(a => a.Ordinal).Select(a => new AlternativeDto(Local.Date(a.DateLocal), Local.TimeOrNull(a.TimeLocal))).ToArray();
        RecurrenceDto? rec = i.RecByDay is null ? null : new RecurrenceDto("weekly", i.RecByDay, Local.Time(i.RecStartTime!.Value), Local.TimeOrNull(i.RecEndTime),
            Local.Date(i.RecFrom!.Value), Local.Date(i.RecUntil!.Value), i.RecUntilSource!, [.. i.RecExceptions.Order().Select(Local.Date)]);
        return new ItemResponse(
            Ids2.Itm(i.Id), Ids2.Syl(i.SyllabusId), Ids2.Course(i.CourseId), i.ItemKey.Trim(), i.Origin, i.Kind, i.Title, i.Notes,
            new WhenDto(i.WhenStatus, written, Local.DateOrNull(i.DateLocal), Local.DateOrNull(i.EndDateLocal), Local.TimeOrNull(i.TimeLocal),
                Local.TimeOrNull(i.EndTimeLocal), i.AllDay, i.Timezone, i.TimezoneSource, i.Derivation, i.UnknownReason, alts),
            rec, i.Confidence, i.ConfidenceReason, i.Flags, Snippet(i.SnippetStart, i.SnippetEnd, i.SnippetPage, i.SnippetPrecision, i.SnippetText, i.ContextText, i.ContextStart, i.ContextEnd),
            i.Edited, i.Decision, i.SyncState, i.SyncedRevision, i.Revision, etags.ForItem(i), i.CreatedAt, i.UpdatedAt);
    }

    public static CalendarEventDto ToDto(this CalendarEvent e) => new(
        WireId.Format(Ids2.Event, e.Id), Ids2.Syl(e.SyllabusId), Ids2.Itm(e.ItemId), e.ItemKey.Trim(), Local.DateOrNull(e.OccurrenceDate), e.Title, e.Kind,
        e.AllDay, new EventStart(Local.Date(e.StartDate), Local.TimeOrNull(e.StartTime), e.StartTimezone),
        new EventEnd(Local.Date(e.EndDate), Local.TimeOrNull(e.EndTime)), e.EndDerived, e.SyncedRevision, e.SyncedAt, true);
}
