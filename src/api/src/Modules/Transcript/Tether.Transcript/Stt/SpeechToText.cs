using System.Threading.Channels;

namespace Tether.Transcript.Stt;

/// <summary>
/// Provider seam (API_CONTRACTS section 5). M2 ships only the deterministic <see cref="ReplaySttProvider"/>. A real adapter
/// needs Product Owner authorization of PO-2/3/4 and gates G1 and G2; nothing here is a vendor integration. No vendor type may
/// leak past an adapter, credentials come from managed secrets only, and neither audio nor text is ever logged.
/// </summary>
internal interface ISpeechToTextProvider
{
    string Name { get; }

    /// <summary>True when the text is synthetic. Surfaced to students as the "Demo source" indication (section 12.2).</summary>
    bool IsDemo { get; }

    /// <param name="startOffsetMs">Session audio time (excluding paused time) at which this stream begins.</param>
    ISttStream Open(long startOffsetMs);
}

internal interface ISttStream : IAsyncDisposable
{
    /// <summary>
    /// Offers one chunk. Offsets are derived by the server from accepted sample counts (never from client clocks), so a stream
    /// can be driven purely by audio position. May apply backpressure by awaiting.
    /// </summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> pcm, long startMs, long endMs, CancellationToken ct);

    /// <summary>Events in provider order. Completes after <see cref="CompleteAsync"/> or a fatal failure.</summary>
    ChannelReader<SttEvent> Events { get; }

    /// <summary>Stops the stream. Anything not yet delivered is discarded by the caller (pause/stop cutoff, C-14).</summary>
    ValueTask CompleteAsync(CancellationToken ct);
}

/// <summary>Provider events. <c>SegmentKey</c> is local to one stream; the relay maps it to the session-wide <c>seg_{n}</c>.</summary>
internal abstract record SttEvent;
internal sealed record SttPartial(int SegmentKey, int Revision, long StartMs, string Text) : SttEvent;
internal sealed record SttFinal(int SegmentKey, long StartMs, long EndMs, string Text, double? Confidence) : SttEvent;
internal sealed record SttCorrection(int SegmentKey, string Text) : SttEvent;
internal sealed record SttGap(long FromMs, long ToMs, string Reason) : SttEvent;
internal sealed record SttStatus(string State) : SttEvent; // Ok | Degraded | Down
