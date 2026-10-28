using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Tether.Lectures.Contracts;
using Tether.SharedKernel;
using Tether.Transcript.Application;
using Tether.Transcript.Domain;
using Tether.Transcript.Hub;
using Tether.Transcript.Stt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tether.Transcript.Relay;

/// <summary>WebSocket close codes used by the audio socket (API_CONTRACTS sections 4 and 10).</summary>
internal static class CloseCodes
{
    public const int Normal = 1000;
    public const int PolicyViolation = 1008;
    public const int InvalidPayload = 1007;
    public const int TooBig = 1009;
    public const int InternalError = 1011;
    /// <summary>Bad, expired, used, foreign or otherwise unusable ticket, and lost authentication. Always identical (C-01).</summary>
    public const int Unauthorized = 4401;
    /// <summary>Session is not Active any more, was deleted, or already has an audio socket.</summary>
    public const int InvalidState = 4409;
}

internal sealed record RelayServices(
    IServiceScopeFactory Scopes, RealtimeOptions Options, TimeProvider Clock, ISpeechToTextProvider Stt, PartialStore Partials,
    LiveFanout Fanout, ISessionActivity Activity, RelayRegistry Registry, ILoggerFactory Logs,
    IEnumerable<Tether.Transcript.Contracts.ITranscriptFinalObserver> FinalObservers);

/// <summary>Ephemeral audio chunk; PCM is held in memory only until the provider has it (no raw audio is persisted or logged).</summary>
internal sealed record AudioChunk(byte[] Pcm, long StartMs, long EndMs);

/// <summary>
/// One authenticated audio socket for one session. Responsibilities: bounded buffers with backpressure, server-derived timecodes,
/// a hard pause/stop cutoff, persistence and fan-out of provider events through the same seq counter as Lectures, and closing
/// on authentication loss, session state change or deletion. Neither audio bytes nor text are ever logged.
/// </summary>
internal sealed class RelayConnection
{
    private readonly WebSocket _ws;
    private readonly RelayServices _s;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<AudioChunk> _audio;
    private readonly Channel<SttEvent> _events = Channel.CreateUnbounded<SttEvent>(new UnboundedChannelOptions { SingleReader = true });
    private ISttStream? _stream;
    private volatile bool _cut;
    private int _closing;
    private long _samples;
    private long _acceptedMs;
    private int _segmentBase;
    private long _lastFrameTicks;
    private bool _silenceFlagged;
    private long _bucketTicks;
    private double _bucketBytes;
    private long _lastRateErrorTicks;
    private int _rateStrikes;
    private long _eventWindowTicks;
    private int _eventWindowCount;
    private (long From, long To)? _pendingGap;
    private (long From, long To)? _overflowGap;

    public Guid UserId { get; }
    public Guid SessionId { get; }
    public Guid AuthSessionId { get; }

    public RelayConnection(WebSocket ws, RelayServices services, Guid userId, Guid authSessionId, Guid sessionId)
    {
        _ws = ws; _s = services; UserId = userId; AuthSessionId = authSessionId; SessionId = sessionId;
        _log = services.Logs.CreateLogger("Tether.Transcript.Relay");
        _audio = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(Math.Max(1, services.Options.ChannelFrames))
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    }

    private long Now => _s.Clock.GetTimestamp();
    private double Seconds(long from, long to) => (double)(to - from) / _s.Clock.TimestampFrequency;

    // ---------- lifecycle ----------

    /// <summary>Called once the hello succeeded and the socket was registered.</summary>
    public async Task RunAsync(byte[] buffer, CancellationToken requestAborted)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, _cts.Token);
        var ct = linked.Token;
        List<Task> workers = [];
        try
        {
            await using (var scope = _s.Scopes.CreateUserScope(UserId, AuthSessionId))
            {
                // B2-04: the relay is registered now, so any later pause/stop/delete reaches it through the observer. A pause that
                // committed before registration did not, so read the state once, before a provider stream exists.
                var info = await scope.ServiceProvider.GetRequiredService<ISessionDirectory>().FindAsync(UserId, SessionId, ct);
                if (info is null || info.State != LectureState.Active) { await CutoffAndCloseAsync(CloseCodes.InvalidState); return; }
                var writer = scope.ServiceProvider.GetRequiredService<TranscriptWriter>();
                _acceptedMs = await writer.LoadAcceptedMsAsync(SessionId, ct);
                _segmentBase = await writer.MaxSegmentNoAsync(SessionId, ct);
            }
            _samples = _acceptedMs * RealtimeOptions.SampleRate / 1000;
            _stream = _s.Stt.Open(_acceptedMs);
            _lastFrameTicks = _bucketTicks = Now;
            _bucketBytes = BurstBytes;
            await PublishAsync(Envelope.Ephemeral("stt.status", SessionId, _s.Clock.GetUtcNow(), new SttStatusPayload("Ok")), ct);

            workers.Add(Task.Run(() => ForwardProviderEventsAsync(ct), CancellationToken.None));
            workers.Add(Task.Run(() => ConsumeAudioAsync(ct), CancellationToken.None));
            workers.Add(Task.Run(() => PumpEventsAsync(ct), CancellationToken.None));
            workers.Add(Task.Run(() => MonitorAsync(ct), CancellationToken.None));

            await ReceiveLoopAsync(buffer, ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException) { /* peer went away or we closed */ }
        catch (Exception ex)
        {
            _log.LogWarning("Audio relay failed: {ExceptionType}", ex.GetType().Name); // type only: never text, audio or row values
            await CloseAsync(CloseCodes.InternalError);
        }
        finally
        {
            _cut = true;
            _audio.Writer.TryComplete();
            _events.Writer.TryComplete();
            await ReleaseAsync(); // save progress, then leave the registry BEFORE draining workers so a quick reconnect is not refused (B2-05)
            _cts.Cancel();
            try { await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { /* workers log their own type-only failures */ }
            if (_stream is not null) await _stream.DisposeAsync();
            _s.Partials.Clear(UserId, SessionId);
            if (_ws.State is WebSocketState.Open or WebSocketState.CloseReceived) await CloseAsync(CloseCodes.Normal);
        }
    }

    private int _released;

    /// <summary>
    /// Saves the accepted-audio position, then removes this relay from the registry. Order matters: a new relay for the same
    /// session loads that position, and may register the moment this one is gone. Runs once.
    /// </summary>
    private async Task ReleaseAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) == 1) return;
        await SaveProgressAsync();
        _s.Registry.Remove(this);
    }

    private async Task SaveProgressAsync()
    {
        try
        {
            await using var scope = _s.Scopes.CreateUserScope(UserId, AuthSessionId);
            await scope.ServiceProvider.GetRequiredService<TranscriptWriter>().SaveAcceptedMsAsync(UserId, SessionId, _acceptedMs, CancellationToken.None);
        }
        catch (Exception ex) { _log.LogInformation("Audio progress not saved: {ExceptionType}", ex.GetType().Name); } // session may be deleted
    }

    /// <summary>Pause/stop/delete cutoff (C-14): discard unsent frames, ignore later ones, close the provider stream, close the socket.</summary>
    public async Task CutoffAndCloseAsync(int code)
    {
        _cut = true;
        _audio.Writer.TryComplete();
        while (_audio.Reader.TryRead(out _)) { /* discard unsent audio; the client ring buffer is never replayed */ }
        _s.Partials.Clear(UserId, SessionId);
        if (_stream is not null) { try { await _stream.CompleteAsync(CancellationToken.None); } catch (Exception) { /* closing anyway */ } }
        await ReleaseAsync(); // gone from the registry before the client can see the close
        await CloseAsync(code);
    }

    public async Task CloseAsync(int code)
    {
        if (Interlocked.Exchange(ref _closing, 1) == 1) return;
        await ReleaseAsync();
        try
        {
            if (!await _send.WaitAsync(TimeSpan.FromSeconds(2))) { _ws.Abort(); return; }
            try
            {
                if (_ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var t = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await _ws.CloseOutputAsync((WebSocketCloseStatus)code, null, t.Token);
                }
            }
            finally { _send.Release(); }
        }
        catch (Exception) { _ws.Abort(); }
        _cts.CancelAfter(TimeSpan.FromSeconds(3)); // do not wait forever for the peer's close reply
    }

    // ---------- upstream ----------

    private double BurstBytes => RealtimeOptions.SampleRate * 2 * _s.Options.MaxRateFactor * _s.Options.RateBurstSeconds;

    private bool TryTakeBudget(int bytes)
    {
        var now = Now;
        var rate = RealtimeOptions.SampleRate * 2 * _s.Options.MaxRateFactor; // bytes per second (~1.1x real time, C-25)
        _bucketBytes = Math.Min(BurstBytes, _bucketBytes + Seconds(_bucketTicks, now) * rate);
        _bucketTicks = now;
        if (_bucketBytes < bytes) return false;
        _bucketBytes -= bytes;
        return true;
    }

    private async Task ReceiveLoopAsync(byte[] buf, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
        {
            var (type, len, oversize) = await ReceiveMessageAsync(_ws, buf, ct);
            if (type == WebSocketMessageType.Close) return;
            if (oversize) { await CloseAsync(CloseCodes.TooBig); return; }

            if (type == WebSocketMessageType.Text)
            {
                if (len > RealtimeOptions.MaxTextBytes) { await CloseAsync(CloseCodes.TooBig); return; }
                // Only pause and stop are accepted (C-15). Resume is REST only. The control only ends this stream; the REST
                // state machine stays authoritative, so a mismatch never changes the session, it just closes the socket.
                if (!TryParseControl(buf.AsSpan(0, len), out var control)) { await CloseAsync(CloseCodes.PolicyViolation); return; }
                await CutoffAndCloseAsync(CloseCodes.Normal);
                _ = control;
                return;
            }

            if (len < RealtimeOptions.HeaderBytes + 2 || len > RealtimeOptions.MaxFrameBytes || (len - RealtimeOptions.HeaderBytes) % 2 != 0)
            {
                await CloseAsync(len > RealtimeOptions.MaxFrameBytes ? CloseCodes.TooBig : CloseCodes.InvalidPayload);
                return;
            }
            if (_cut) continue; // frames after the cutoff are ignored

            var frameSeq = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(0, 4)); // little-endian; advisory fields are not trusted
            var pcmLen = len - RealtimeOptions.HeaderBytes;
            if (!TryTakeBudget(pcmLen))
            {
                if (++_rateStrikes > 50) { await CloseAsync(CloseCodes.PolicyViolation); return; }
                if (Seconds(_lastRateErrorTicks, Now) >= 1 || _lastRateErrorTicks == 0)
                {
                    _lastRateErrorTicks = Now;
                    await SendJsonAsync(new { type = "error", code = "rate_limited" }, ct);
                }
                continue;
            }

            // Timecodes come from accepted sample counts, never from the client clock (C-19).
            var startMs = _acceptedMs;
            _samples += pcmLen / 2;
            _acceptedMs = _samples * 1000 / RealtimeOptions.SampleRate;
            var chunk = new AudioChunk(buf.AsSpan(RealtimeOptions.HeaderBytes, pcmLen).ToArray(), startMs, _acceptedMs);

            var accepted = _audio.Writer.TryWrite(chunk);
            if (!accepted)
            {
                // Backpressure: stop reading from the socket (TCP pushes back on the client) for a bounded time, then drop and report a gap.
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(_s.Options.Backpressure);
                try { await _audio.Writer.WriteAsync(chunk, wait.Token); accepted = true; }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* dropped below */ }
                catch (ChannelClosedException) { return; }
            }
            if (!accepted)
            {
                _pendingGap = _pendingGap is { } g ? (g.From, chunk.EndMs) : (chunk.StartMs, chunk.EndMs);
                continue;
            }
            if (_pendingGap is { } pg) { _pendingGap = null; _events.Writer.TryWrite(new SttGap(pg.From, pg.To, "provider")); }

            _lastFrameTicks = Now;
            _silenceFlagged = false;
            _s.Activity.Touch(SessionId);
            await SendJsonAsync(new { type = "ack", frameSeq }, ct);
        }
    }

    private static bool TryParseControl(ReadOnlySpan<byte> utf8, out string type)
    {
        type = "";
        try
        {
            var reader = new Utf8JsonReader(utf8);
            using var doc = JsonDocument.ParseValue(ref reader);
            var obj = doc.RootElement;
            if (obj.ValueKind != JsonValueKind.Object) return false;
            var props = obj.EnumerateObject().ToList();
            if (props.Count != 1 || props[0].Name != "type" || props[0].Value.ValueKind != JsonValueKind.String) return false;
            type = props[0].Value.GetString()!;
            return type is "pause" or "stop";
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Reads one whole message into <paramref name="buf"/>; flags a message larger than the buffer.</summary>
    internal static async Task<(WebSocketMessageType Type, int Length, bool Oversize)> ReceiveMessageAsync(WebSocket ws, byte[] buf, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf, count, buf.Length - count), ct);
            if (r.MessageType == WebSocketMessageType.Close) return (WebSocketMessageType.Close, 0, false);
            count += r.Count;
            if (r.EndOfMessage) return (r.MessageType, count, false);
            if (count >= buf.Length) return (r.MessageType, count, true);
        }
    }

    // ---------- provider side ----------

    private async Task ConsumeAudioAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var chunk in _audio.Reader.ReadAllAsync(ct))
            {
                if (_cut) continue;
                await _stream!.WriteAsync(chunk.Pcm, chunk.StartMs, chunk.EndMs, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogWarning("STT write failed: {ExceptionType}", ex.GetType().Name);
            _events.Writer.TryWrite(new SttStatus("Down"));
        }
    }

    private async Task ForwardProviderEventsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var e in _stream!.Events.ReadAllAsync(ct)) await _events.Writer.WriteAsync(e, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.LogWarning("STT read failed: {ExceptionType}", ex.GetType().Name); }
    }

    private bool AllowPersisted()
    {
        var now = Now;
        if (Seconds(_eventWindowTicks, now) >= 1) { _eventWindowTicks = now; _eventWindowCount = 0; }
        return ++_eventWindowCount <= _s.Options.EventsPerSecond; // C-10: bounded event rate
    }

    private async Task PumpEventsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var e in _events.Reader.ReadAllAsync(ct))
            {
                if (_cut) continue;
                await HandleAsync(e, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogWarning("Transcript pump failed: {ExceptionType}", ex.GetType().Name); // type only: row values may sit in the message
            await CutoffAndCloseAsync(CloseCodes.InternalError);
        }
    }

    private async Task HandleAsync(SttEvent e, CancellationToken ct)
    {
        switch (e)
        {
            case SttPartial p:
            {
                var seg = _segmentBase + p.SegmentKey;
                var env = Envelope.Ephemeral("transcript.partial", SessionId, _s.Clock.GetUtcNow(),
                    new PartialPayload(EventMapper.SegmentId(seg), p.Revision, p.StartMs, p.Text));
                if (_cut) return;
                _s.Partials.Set(UserId, SessionId, seg, env);
                await PublishAsync(env, ct);
                break;
            }
            case SttFinal f:
            {
                var seg = _segmentBase + f.SegmentKey;
                if (!AllowPersisted()) { Overflow(f.StartMs, f.EndMs); return; }
                await FlushOverflowAsync(ct);
                await PersistAsync(x => { x.Kind = TranscriptEventKind.Final; x.SegmentNo = seg; x.StartMs = f.StartMs; x.EndMs = f.EndMs; x.Text = Trim(f.Text); x.Confidence = f.Confidence; x.Provider = _s.Stt.Name; }, seg, ct);
                break;
            }
            case SttCorrection c:
            {
                var seg = _segmentBase + c.SegmentKey;
                if (!AllowPersisted()) return;
                int cur;
                await using (var scope = _s.Scopes.CreateUserScope(UserId, AuthSessionId))
                {
                    var w = scope.ServiceProvider.GetRequiredService<TranscriptWriter>();
                    if (!await w.FinalExistsAsync(SessionId, seg, ct)) return; // never correct a segment that was not finalised
                    cur = await w.CurrentRevisionAsync(SessionId, seg, ct);
                }
                await PersistAsync(x => { x.Kind = TranscriptEventKind.Correction; x.SegmentNo = seg; x.Revision = cur + 1; x.Supersedes = cur; x.Text = Trim(c.Text); }, null, ct);
                break;
            }
            case SttGap g:
                await PersistAsync(x => { x.Kind = TranscriptEventKind.Gap; x.FromMs = g.FromMs; x.ToMs = Math.Max(g.FromMs, g.ToMs); x.GapReason = g.Reason; }, null, ct);
                break;
            case FlushOverflow:
                if (_overflowGap is not null && AllowPersisted()) await FlushOverflowAsync(ct);
                break;
            case SttStatus st:
                await PublishAsync(Envelope.Ephemeral("stt.status", SessionId, _s.Clock.GetUtcNow(), new SttStatusPayload(st.State)), ct);
                if (st.State == "Down") await SendJsonAsync(new { type = "error", code = "provider_unavailable" }, ct);
                break;
        }
    }

    private static string Trim(string t) => t.Length <= 4000 ? t : t[..4000];

    private void Overflow(long from, long to) => _overflowGap = _overflowGap is { } g ? (g.From, Math.Max(g.To, to)) : (from, to);

    private async Task FlushOverflowAsync(CancellationToken ct)
    {
        if (_overflowGap is not { } g) return;
        _overflowGap = null;
        await PersistAsync(x => { x.Kind = TranscriptEventKind.Gap; x.FromMs = g.From; x.ToMs = g.To; x.GapReason = "provider"; }, null, ct);
    }

    private async Task PersistAsync(Action<TranscriptEvent> fill, int? clearPartialOfSegment, CancellationToken ct)
    {
        Envelope? env;
        await using (var scope = _s.Scopes.CreateUserScope(UserId, AuthSessionId))
            env = await scope.ServiceProvider.GetRequiredService<TranscriptWriter>().AppendAsync(UserId, SessionId, fill, ct);
        if (env is null)
        {
            // The session is gone or no longer Active: nothing was kept (the seq allocation rolled back). Stop immediately.
            await CutoffAndCloseAsync(CloseCodes.InvalidState);
            return;
        }
        if (clearPartialOfSegment is { } seg) _s.Partials.Remove(UserId, SessionId, seg);
        await PublishAsync(env, ct);
        if (env.Type == "transcript.final") NotifyFinalObservers(long.Parse(env.Seq!, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// B3: tells interested modules (the AI summarizer) that a final is committed. Observers only record that work exists and
    /// return at once; this call can never wait on AI, and an observer fault never reaches the audio path.
    /// </summary>
    private void NotifyFinalObservers(long seq)
    {
        foreach (var o in _s.FinalObservers)
        {
            try { o.OnFinalPersisted(UserId, SessionId, seq); }
            catch (Exception ex) { _s.Logs.CreateLogger("Relay").LogWarning("Final observer failed: {ExceptionType}", ex.GetType().Name); }
        }
    }

    private Task PublishAsync(Envelope env, CancellationToken ct) => _s.Fanout.PublishAsync(UserId, SessionId, env, ct);

    // ---------- watchdog ----------

    private async Task MonitorAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_s.Options.RelayCheck);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await using var scope = _s.Scopes.CreateUserScope(UserId, AuthSessionId);
                var sp = scope.ServiceProvider;
                if (!await sp.GetRequiredService<IAuthSessionValidator>().IsValidAsync(UserId, AuthSessionId, ct))
                {
                    await CutoffAndCloseAsync(CloseCodes.Unauthorized);
                    return;
                }
                var info = await sp.GetRequiredService<ISessionDirectory>().FindAsync(UserId, SessionId, ct);
                if (info is null || info.State != LectureState.Active)
                {
                    await CutoffAndCloseAsync(CloseCodes.InvalidState); // also covers a course purge that removed the session by cascade
                    return;
                }
                _events.Writer.TryWrite(new FlushOverflow()); // a dropped-for-rate range must still be reported even if no later event arrives
                if (!_silenceFlagged && Seconds(_lastFrameTicks, Now) >= _s.Options.SilenceGap.TotalSeconds)
                {
                    _silenceFlagged = true; // marks the point where audio stopped arriving without inventing a duration
                    _events.Writer.TryWrite(new SttGap(_acceptedMs, _acceptedMs, "network"));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.LogWarning("Relay watchdog failed: {ExceptionType}", ex.GetType().Name); }
    }

    // ---------- downstream control frames ----------

    private async Task SendJsonAsync(object frame, CancellationToken ct)
    {
        if (_closing == 1 || _ws.State != WebSocketState.Open) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, JsonSerializerOptions.Web);
        try
        {
            await _send.WaitAsync(ct);
            try
            {
                if (_ws.State == WebSocketState.Open) await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }
            finally { _send.Release(); }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException) { }
    }
}

/// <summary>Internal marker: ask the pump to report a pending rate-overflow gap.</summary>
internal sealed record FlushOverflow : SttEvent;

/// <summary>Live audio sockets by session. One socket per session and a per-user cap (C-18). In process: single instance only.</summary>
internal sealed class RelayRegistry(RealtimeOptions options)
{
    private readonly ConcurrentDictionary<Guid, RelayConnection> _bySession = new();

    /// <summary>Test seam: awaited after a ticket was consumed and before the relay is registered (B2-04). Always null in production.</summary>
    internal Func<Guid, Task>? AfterTicketConsumed { get; set; }

    public bool Contains(Guid sessionId) => _bySession.ContainsKey(sessionId);

    public bool TryAdd(RelayConnection c)
    {
        if (_bySession.Values.Count(x => x.UserId == c.UserId) >= options.MaxSocketsPerUser) return false;
        return _bySession.TryAdd(c.SessionId, c);
    }

    public void Remove(RelayConnection c) => _bySession.TryRemove(new KeyValuePair<Guid, RelayConnection>(c.SessionId, c));

    public Task CloseSessionAsync(Guid sessionId, int code) =>
        _bySession.TryGetValue(sessionId, out var c) ? c.CutoffAndCloseAsync(code) : Task.CompletedTask;

    /// <summary>B2-03: closes every audio socket opened with the revoked login session.</summary>
    public Task CloseAuthSessionAsync(Guid userId, Guid authSessionId, int code) =>
        Task.WhenAll(_bySession.Values.Where(c => c.UserId == userId && c.AuthSessionId == authSessionId).Select(c => c.CutoffAndCloseAsync(code)));
}
