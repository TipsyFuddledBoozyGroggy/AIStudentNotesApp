using System.Globalization;
using System.Text.Json.Serialization;
using Tether.Lectures.Contracts;
using Tether.SharedKernel.Http;
using Tether.Transcript.Domain;

namespace Tether.Transcript.Application;

// Wire shapes must keep validating against src/shared/contracts/lecture.v1/*.schema.json (additionalProperties:false).
// Records serialize camelCase through the web defaults used by minimal APIs and by the SignalR JSON protocol.

/// <summary>Envelope on every server event: <c>{v, type, sessionId, seq|null, ts, payload}</c> (section 11.1).</summary>
internal sealed record Envelope(int V, string Type, string SessionId, string? Seq, DateTimeOffset Ts, object Payload)
{
    public static Envelope Persisted(string type, Guid sessionId, long seq, DateTimeOffset ts, object payload) =>
        new(1, type, sessionId.ToString(), seq.ToString(CultureInfo.InvariantCulture), ts, payload);

    public static Envelope Ephemeral(string type, Guid sessionId, DateTimeOffset ts, object payload) =>
        new(1, type, sessionId.ToString(), null, ts, payload);
}

internal sealed record FinalPayload(string SegmentId, long StartMs, long EndMs, string Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Confidence, string Provider);
internal sealed record CorrectionPayload(string SegmentId, int Revision, string Text, int Supersedes);
internal sealed record GapPayload(long FromMs, long ToMs, string Reason);
internal sealed record StatePayload(string State, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason);
internal sealed record PartialPayload(string SegmentId, int Revision, long StartMs, string Text);
internal sealed record SttStatusPayload(string State);
internal sealed record ErrorPayload(string Code, string Detail);

internal sealed record TranscriptPage(IReadOnlyList<Envelope> Events, bool HasMore);

internal static class EventMapper
{
    public static string SegmentId(int n) => $"seg_{n}";

    public static Envelope ToEnvelope(TranscriptEvent e) => e.Kind switch
    {
        TranscriptEventKind.Final => Envelope.Persisted("transcript.final", e.SessionId, e.Seq, e.OccurredAt,
            new FinalPayload(SegmentId(e.SegmentNo!.Value), e.StartMs!.Value, e.EndMs!.Value, e.Text!, e.Confidence, e.Provider!)),
        TranscriptEventKind.Correction => Envelope.Persisted("transcript.correction", e.SessionId, e.Seq, e.OccurredAt,
            new CorrectionPayload(SegmentId(e.SegmentNo!.Value), e.Revision!.Value, e.Text!, e.Supersedes!.Value)),
        _ => Envelope.Persisted("gap", e.SessionId, e.Seq, e.OccurredAt, new GapPayload(e.FromMs!.Value, e.ToMs!.Value, e.GapReason!)),
    };

    public static Envelope ToEnvelope(Guid sessionId, SessionStateRecord r) =>
        Envelope.Persisted("session.state", sessionId, r.Seq, r.OccurredAt, new StatePayload(r.State.ToString(), r.Reason));

    public static Envelope ToEnvelope(SessionStateChange c) =>
        Envelope.Persisted("session.state", c.SessionId, c.Seq, c.OccurredAt, new StatePayload(c.State.ToString(), c.Reason));

    /// <summary>Hub/socket error events: <c>detail</c> is the fixed server text for the code, never exception text (C-24).</summary>
    public static Envelope Error(Guid sessionId, string code, DateTimeOffset ts) =>
        Envelope.Ephemeral("error", sessionId, ts, new ErrorPayload(code, ErrorVocabulary.DetailFor(code)));
}
