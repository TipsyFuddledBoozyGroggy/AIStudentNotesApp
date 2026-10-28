using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;

namespace Tether.Transcript.Stt;

internal sealed record ReplaySegment(long StartMs, long EndMs, string Text, string? Correction = null);

/// <summary>Fault injection for tests and resilience drills. All keys are optional: Speech:Replay:Faults:*.</summary>
internal sealed record ReplayFaults(long? DropFromMs = null, long? DropToMs = null, int DelayMs = 0, long? ErrorAtMs = null, long ErrorDurationMs = 0)
{
    public static ReplayFaults From(IConfiguration c)
    {
        var f = c.GetSection("Speech:Replay:Faults");
        return new ReplayFaults(
            f.GetValue<long?>("DropFromMs"), f.GetValue<long?>("DropToMs"), f.GetValue("DelayMs", 0),
            f.GetValue<long?>("ErrorAtMs"), f.GetValue("ErrorDurationMs", 0L));
    }
}

/// <summary>
/// Deterministic, audio-offset-driven demo source. It contains NO real transcription: it emits a fixed script (or the JSON
/// script in <c>Speech:Replay:Script</c>) as the accumulated audio position passes each point, so the same audio timing always
/// yields the same events. Every final it produces carries provider <c>replay-demo</c>, and sessions report
/// <c>demoSource: true</c> while it is the active provider, so its text cannot be mistaken for a real lecture.
/// </summary>
internal sealed class ReplaySttProvider(IReadOnlyList<ReplaySegment> script, ReplayFaults faults) : ISpeechToTextProvider
{
    public const string ProviderName = "replay-demo";
    public const long PartialStepMs = 500;

    public string Name => ProviderName;
    public bool IsDemo => true;

    public ISttStream Open(long startOffsetMs) => new ReplayStream(script, faults, startOffsetMs);

    /// <summary>Built-in script: obviously synthetic text, 2.5 s segments every 3 s, one later correction.</summary>
    public static IReadOnlyList<ReplaySegment> DefaultScript { get; } =
    [
        new(0, 2500, "Demo replay segment one. This is a fixed fixture and not a real lecture."),
        new(3000, 5500, "Demo replay segment two. The words come from a script driven by audio position.", "Demo replay segment two, corrected. The words come from a script driven by audio position."),
        new(6000, 8500, "Demo replay segment three. Nothing here was spoken by anyone."),
        new(9000, 11500, "Demo replay segment four. Replace this source before any real use."),
    ];

    public static IReadOnlyList<ReplaySegment> ParseScript(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ReplaySegment>();
        foreach (var e in doc.RootElement.EnumerateArray())
            list.Add(new ReplaySegment(e.GetProperty("startMs").GetInt64(), e.GetProperty("endMs").GetInt64(), e.GetProperty("text").GetString()!,
                e.TryGetProperty("correction", out var c) ? c.GetString() : null));
        return list.OrderBy(s => s.StartMs).ToList();
    }

    private sealed class ReplayStream : ISttStream
    {
        private readonly Channel<SttEvent> _events = Channel.CreateBounded<SttEvent>(new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        private readonly ReplayFaults _faults;
        private readonly List<(ReplaySegment Seg, int Key)> _pending;
        private readonly Dictionary<int, long> _partialsSent = [];
        private readonly Dictionary<int, long> _correctionDue = [];
        private readonly Dictionary<int, string> _corrections = [];
        private bool _dropReported, _downSent, _upSent, _done;

        public ReplayStream(IReadOnlyList<ReplaySegment> script, ReplayFaults faults, long startOffsetMs)
        {
            _faults = faults;
            // A segment that began before this stream's start (pause/resume boundary) is lost, not half-invented.
            _pending = script.Where(s => s.StartMs >= startOffsetMs).Select((s, i) => (s, i + 1)).ToList();
        }

        public ChannelReader<SttEvent> Events => _events.Reader;

        private bool InDrop(long ms) => _faults.DropFromMs is { } f && _faults.DropToMs is { } t && ms >= f && ms < t;
        private bool InError(long ms) => _faults.ErrorAtMs is { } a && ms >= a && ms < a + _faults.ErrorDurationMs;

        private async ValueTask EmitAsync(SttEvent e, CancellationToken ct)
        {
            if (_faults.DelayMs > 0) await Task.Delay(_faults.DelayMs, ct); // slows WriteAsync, which is how backpressure is exercised
            await _events.Writer.WriteAsync(e, ct);
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> pcm, long startMs, long endMs, CancellationToken ct)
        {
            if (_done) return;

            if (_faults.ErrorAtMs is { } errAt && endMs > errAt)
            {
                if (!_downSent) { _downSent = true; await EmitAsync(new SttStatus("Down"), ct); }
                if (endMs >= errAt + _faults.ErrorDurationMs && !_upSent)
                {
                    _upSent = true;
                    await EmitAsync(new SttGap(errAt, errAt + _faults.ErrorDurationMs, "provider"), ct);
                    await EmitAsync(new SttStatus("Ok"), ct);
                }
            }
            if (_faults.DropFromMs is { } dropFrom && _faults.DropToMs is { } dropTo && endMs >= dropTo && !_dropReported)
            {
                _dropReported = true;
                await EmitAsync(new SttGap(dropFrom, dropTo, "provider"), ct);
            }

            foreach (var (seg, key) in _pending.ToList())
            {
                var lost = InDrop(seg.StartMs) || InDrop(seg.EndMs - 1) || InError(seg.StartMs) || InError(seg.EndMs - 1);
                if (lost) { if (endMs >= seg.EndMs) _pending.Remove((seg, key)); continue; }

                if (seg.StartMs < endMs && endMs < seg.EndMs)
                {
                    // Partials at fixed audio steps: a growing prefix of the words, revision increases per step.
                    var step = (endMs - seg.StartMs) / PartialStepMs;
                    if (step > 0 && (!_partialsSent.TryGetValue(key, out var sent) || step > sent))
                    {
                        _partialsSent[key] = step;
                        var words = seg.Text.Split(' ');
                        var take = Math.Clamp((int)Math.Ceiling(words.Length * (double)(endMs - seg.StartMs) / (seg.EndMs - seg.StartMs)), 1, words.Length - 1 < 1 ? 1 : words.Length - 1);
                        await EmitAsync(new SttPartial(key, (int)step, seg.StartMs, string.Join(' ', words.Take(take))), ct);
                    }
                }
                else if (endMs >= seg.EndMs)
                {
                    await EmitAsync(new SttFinal(key, seg.StartMs, seg.EndMs, seg.Text, 0.9), ct);
                    _pending.Remove((seg, key));
                    if (seg.Correction is not null) { _corrections[key] = seg.Correction; _correctionDue[key] = seg.EndMs + 1500; }
                }
            }

            foreach (var (key, due) in _correctionDue.ToList())
            {
                if (endMs < due) continue;
                _correctionDue.Remove(key);
                if (_corrections.Remove(key, out var corrected)) await EmitAsync(new SttCorrection(key, corrected), ct);
            }
        }

        public ValueTask CompleteAsync(CancellationToken ct)
        {
            _done = true;
            _events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _done = true;
            _events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
