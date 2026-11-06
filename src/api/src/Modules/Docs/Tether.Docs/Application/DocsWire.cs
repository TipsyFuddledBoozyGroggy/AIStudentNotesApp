using System.Text.Json;
using System.Text.Json.Serialization;
using Tether.Docs.Contracts;
using Tether.Docs.Domain;
using Tether.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace Tether.Docs.Application;

// ---- response shapes (docs.export.v1; every member is required by the schemas, nulls ARE written) ----

internal sealed record DocRefDto(string DocId, string Provider, string Label, int Version, string OpenKind, string OpenPath, string? OpenUrl)
{
    public static DocRefDto Of(Guid docId, int version)
    {
        var id = WireId.Format(DocsConstants.DocPrefix, docId);
        return new DocRefDto(id, DocsConstants.Provider, DocsConstants.Label, version, "in_app", "/demo-docs/" + id, null);
    }
}

internal sealed record CitationDto(long StartMs);

internal sealed class BlockDto
{
    public required string Kind { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Level { get; init; }
    public required string Text { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<CitationDto>? Citations { get; init; }

    public static BlockDto Of(DocBlock b) => new()
    {
        Kind = b.Kind, Text = b.Text,
        Level = b.Kind == "heading" ? b.Level ?? 1 : null,
        Citations = b.Kind == "bullet" ? (b.CitationStartMs ?? []).Select(ms => new CitationDto(ms)).ToList() : null,
    };
}

internal sealed record DocumentDto(string Title, IReadOnlyList<BlockDto> Blocks)
{
    public static DocumentDto Of(string title, IReadOnlyList<DocBlock> blocks) => new(title, blocks.Select(BlockDto.Of).ToList());
}

internal sealed record DestinationDto(string Provider, string Label, bool IsDemo)
{
    public static readonly DestinationDto Demo = new(DocsConstants.Provider, DocsConstants.Label, true);
}

internal sealed record CountsDto(int Sections, int Bullets, int Citations);

internal sealed record ExistingDto(string ExportId, DocRefDto DocRef, DateTimeOffset LastExportedAt, bool UpToDate);

internal sealed record PreviewDto(
    bool PreviewOnly, string SessionId, string NotesId, bool NotesTruncated, string NotesSource, DestinationDto Destination, DocumentDto Document,
    CountsDto Counts, string ContentDigest, ExistingDto? Existing, IReadOnlyList<string> Actions, bool CanWrite, string? BlockedReason, DateTimeOffset GeneratedAt);

internal sealed record RecordErrorDto(string Code, bool Retryable, int? RetryAfterSeconds);

internal sealed record RetryDto(bool Allowed, int AttemptsLeft);

internal sealed record ExportRecordDto(
    string ExportId, string SessionId, string NotesId, string Provider, bool DemoDoc, string Mode, string Status, string Outcome, string ContentDigest,
    DocRefDto? DocRef, int Attempt, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, RecordErrorDto? Error, RetryDto Retry)
{
    public static ExportRecordDto Of(DocExport e)
    {
        var failed = e.Status == ExportStatus.Failed;
        var left = Math.Max(0, DocsConstants.MaxAttempts - e.Attempt);
        return new ExportRecordDto(
            WireId.Format(DocsConstants.ExportPrefix, e.Id), e.SessionId.ToString("D"), "nt_" + e.NotesId.ToString("N"), e.Provider, true, e.Mode, e.Status, e.Outcome, e.ContentDigest,
            e.DocId is { } d && e.DocVersion is { } v ? DocRefDto.Of(d, v) : null, e.Attempt, e.CreatedAt, e.CompletedAt,
            failed ? new RecordErrorDto(e.ErrorCode ?? "provider_unavailable", e.Retryable ?? false, e.RetryAfterSeconds) : null,
            new RetryDto(failed && e.Retryable == true && e.Attempt < DocsConstants.MaxAttempts, left));
    }
}

internal sealed record StateDto(string SessionId, DocRefDto? Target, ExportRecordDto? LastExport);

internal sealed record DemoDocDto(string DocId, string Provider, bool DemoDoc, DocumentDto Document, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

// ---- request parsing (docs-preview-request, docs-export-request): strict, exact member sets, canonical ids ----

internal sealed record PreviewRequest(string NotesId);

internal sealed record ConfirmRequest(string NotesId, string ContentDigest, string Mode, int? ExpectedVersion, Guid? ExpectedDocId)
{
    /// <summary>The fingerprint material of the body: every field length-prefixed, so two different bodies never share a string.</summary>
    public string Canonical() => Hashing.Material(NotesId, ContentDigest, Mode, ExpectedVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ExpectedDocId is { } d ? WireId.Format(DocsConstants.DocPrefix, d) : null, DocsConstants.Provider, "true");
}

internal static class RequestParser
{
    public static bool ValidNotesId(string? s) =>
        s is { Length: 35 } && s.StartsWith("nt_", StringComparison.Ordinal) && s.AsSpan(3).IndexOfAnyExcept("0123456789abcdef") < 0;

    public static bool ValidDigest(string? s) =>
        s is { Length: 46 } && s.StartsWith("cd_", StringComparison.Ordinal) && s.Skip(3).All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    public static IResult? ParsePreview(byte[] body, out PreviewRequest? req)
    {
        req = null;
        using var doc = JsonBody.TryParse(body);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return DocsProblem.InvalidBare();
        var count = 0;
        string? notes = null;
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            count++;
            if (p.Name != "notesId") return DocsProblem.Invalid("request", "unknown_member");
            notes = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
        }
        if (count != 1 || !ValidNotesId(notes)) return DocsProblem.Invalid("notesId");
        req = new PreviewRequest(notes!);
        return null;
    }

    private static readonly string[] ConfirmMembers = ["notesId", "contentDigest", "mode", "expectedExistingVersion", "expectedExistingDocId", "confirm"];

    public static IResult? ParseConfirm(byte[] body, out ConfirmRequest? req)
    {
        req = null;
        using var doc = JsonBody.TryParse(body);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return DocsProblem.InvalidBare();
        var root = doc.RootElement;
        foreach (var p in root.EnumerateObject())
            if (!ConfirmMembers.Contains(p.Name)) return DocsProblem.Invalid("request", "unknown_member"); // a client can not send text, a title or a document
        foreach (var m in ConfirmMembers)
            if (!root.TryGetProperty(m, out _)) return DocsProblem.Invalid(m, "required");

        string? Str(string name) => root.GetProperty(name).ValueKind == JsonValueKind.String ? root.GetProperty(name).GetString() : null;
        var notes = Str("notesId");
        if (!ValidNotesId(notes)) return DocsProblem.Invalid("notesId");
        var digest = Str("contentDigest");
        if (!ValidDigest(digest)) return DocsProblem.Invalid("contentDigest");
        var mode = Str("mode");
        if (mode is not ("create" or "update" or "create_another")) return DocsProblem.Invalid("mode");

        int? version = null;
        var ev = root.GetProperty("expectedExistingVersion");
        if (ev.ValueKind == JsonValueKind.Number)
        {
            if (!ev.TryGetInt32(out var n) || n < 1) return DocsProblem.Invalid("expectedExistingVersion");
            version = n;
        }
        else if (ev.ValueKind != JsonValueKind.Null) return DocsProblem.Invalid("expectedExistingVersion");

        Guid? docId = null;
        var ed = root.GetProperty("expectedExistingDocId");
        if (ed.ValueKind == JsonValueKind.String)
        {
            if (!WireId.TryParse(DocsConstants.DocPrefix, ed.GetString(), out var g)) return DocsProblem.Invalid("expectedExistingDocId", "not_canonical");
            docId = g;
        }
        else if (ed.ValueKind != JsonValueKind.Null) return DocsProblem.Invalid("expectedExistingDocId");

        // create goes with no existing document; update and create_another need one (docs-export-request allOf)
        if (mode == "create" ? version is not null || docId is not null : version is null || docId is null)
            return DocsProblem.Invalid("mode", "existing_mismatch");

        var confirm = root.GetProperty("confirm");
        if (confirm.ValueKind != JsonValueKind.Object) return DocsProblem.Invalid("confirm");
        var seen = 0; string? provider = null; var ack = false;
        foreach (var p in confirm.EnumerateObject())
        {
            seen++;
            if (p.Name == "provider") provider = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
            else if (p.Name == "acknowledgedDemo") ack = p.Value.ValueKind == JsonValueKind.True;
            else return DocsProblem.Invalid("confirm", "unknown_member");
        }
        if (seen != 2 || provider != DocsConstants.Provider) return DocsProblem.Invalid("confirm.provider");   // a real provider is not expressible
        if (!ack) return DocsProblem.Invalid("confirm.acknowledgedDemo");

        req = new ConfirmRequest(notes!, digest!, mode, version, docId);
        return null;
    }
}
