using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Buffers.Binary;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Tether.IntegrationTests;

/// <summary>Test factory preset for realtime tests: fast watchdogs, large audio burst budget, tiny job interval noise.</summary>
public class RealtimeFactory : ApiFactory
{
    protected virtual IEnumerable<KeyValuePair<string, string?>> Extra => [];

    protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        new KeyValuePair<string, string?>[]
        {
            new("Realtime:RevalidateSeconds", "1"),
            new("Realtime:RelayCheckSeconds", "1"),
            new("Realtime:RateBurstSeconds", "600"),
            new("Lectures:LifecycleJobSeconds", "3600"),
            new("RateLimiting:AudioTicketPermitPerMinute", "1000"),
            new("Realtime:UpgradePermitPerMinute", "1000"),
            new("Realtime:JoinPermitPerMinute", "1000"),
        }.Concat(Extra);
}

/// <summary>Cookie-authenticated realtime clients over the in-memory TestServer (SignalR client and raw audio WebSocket).</summary>
public static class Rt
{
    public static string CookieOf(HttpClient actor) => actor.DefaultRequestHeaders.GetValues("Cookie").Single();

    public static HubConnection Hub(ApiFactory f, string cookie, string? origin = ApiFactory.Origin) =>
        new HubConnectionBuilder().WithUrl(new Uri(f.Server.BaseAddress, "/hubs/lecture"), o =>
        {
            o.Transports = HttpTransportType.WebSockets;
            o.HttpMessageHandlerFactory = _ => f.Server.CreateHandler();
            o.WebSocketFactory = async (ctx, ct) =>
            {
                var c = f.Server.CreateWebSocketClient();
                c.ConfigureRequest = r =>
                {
                    var h = r.Headers;
                    if (origin is not null) h["Origin"] = origin;
                    h["Cookie"] = cookie;
                };
                return await c.ConnectAsync(ctx.Uri, ct);
            };
            if (origin is not null) o.Headers["Origin"] = origin;
            o.Headers["Cookie"] = cookie;
        }).Build();

    /// <summary>A hub connection that records every <c>event</c> argument it receives.</summary>
    public sealed class HubProbe(HubConnection conn) : IAsyncDisposable
    {
        public HubConnection Conn { get; } = conn;
        public List<JsonElement> Events { get; } = [];
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static async Task<HubProbe> StartAsync(ApiFactory f, string cookie, string? origin = ApiFactory.Origin)
        {
            var p = new HubProbe(Hub(f, cookie, origin));
            p.Conn.On<JsonElement>("event", e => { lock (p.Events) p.Events.Add(e.Clone()); });
            p.Conn.Closed += _ => { p.Closed.TrySetResult(); return Task.CompletedTask; };
            await p.Conn.StartAsync();
            return p;
        }

        public async Task<JsonElement> JoinAsync(string sessionId, string? lastSeq = null) =>
            lastSeq is null
                ? await Conn.InvokeAsync<JsonElement>("Join", new { sessionId, protocol = "lecture.v1" })
                : await Conn.InvokeAsync<JsonElement>("Join", new { sessionId, protocol = "lecture.v1", lastSeq });

        public List<JsonElement> Snapshot() { lock (Events) return [.. Events]; }
        public List<JsonElement> Of(string type) => Snapshot().Where(e => e.GetProperty("type").GetString() == type).ToList();

        public async ValueTask DisposeAsync() { try { await Conn.DisposeAsync(); } catch (Exception) { } }
    }

    public static async Task<bool> WaitAsync(Func<Task<bool>> condition, int timeoutMs = 8000)
    {
        var end = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < end)
        {
            if (await condition()) return true;
            await Task.Delay(25);
        }
        return await condition();
    }

    public static Task<bool> WaitAsync(Func<bool> condition, int timeoutMs = 8000) => WaitAsync(() => Task.FromResult(condition()), timeoutMs);

    // ---------------- audio socket ----------------

    public sealed class AudioClient : IAsyncDisposable
    {
        private readonly WebSocket _ws;
        private readonly List<string> _texts = [];
        private readonly Task _receiver;
        private readonly TaskCompletionSource<int> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int FramesSent { get; private set; }

        private AudioClient(WebSocket ws)
        {
            _ws = ws;
            _receiver = Task.Run(ReceiveAsync);
        }

        public static async Task<AudioClient> ConnectAsync(ApiFactory f, string cookie, string sessionId, string? origin = ApiFactory.Origin)
        {
            var c = f.Server.CreateWebSocketClient();
            c.ConfigureRequest = r =>
            {
                var h = r.Headers;
                if (origin is not null) h["Origin"] = origin;
                if (cookie.Length > 0) h["Cookie"] = cookie;
            };
            return new AudioClient(await c.ConnectAsync(new Uri($"ws://localhost/ingest/sessions/{sessionId}/audio"), CancellationToken.None));
        }

        private async Task ReceiveAsync()
        {
            var buf = new byte[16 * 1024];
            try
            {
                while (true)
                {
                    var r = await _ws.ReceiveAsync(buf, CancellationToken.None);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        _closed.TrySetResult((int)(_ws.CloseStatus ?? 0));
                        try { await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); } catch (Exception) { }
                        return;
                    }
                    lock (_texts) _texts.Add(Encoding.UTF8.GetString(buf, 0, r.Count));
                }
            }
            catch (Exception) { _closed.TrySetResult(-1); }
        }

        public List<JsonElement> Frames() { lock (_texts) return _texts.Select(t => JsonDocument.Parse(t).RootElement.Clone()).ToList(); }
        public List<JsonElement> FramesOf(string type) => Frames().Where(f => f.GetProperty("type").GetString() == type).ToList();

        public Task SendTextAsync(string json) => _ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

        public Task HelloAsync(string ticket, string? raw = null) =>
            SendTextAsync(raw ?? JsonSerializer.Serialize(new { type = "hello", version = "speech.v1", ticket, sampleRate = 16000, encoding = "pcm_s16le", channels = 1 }));

        public Task SendBinaryAsync(byte[] data) => _ws.SendAsync(data, WebSocketMessageType.Binary, true, CancellationToken.None);

        public static byte[] Frame(uint frameSeq, int ms = 250, ulong captureOffsetMs = 0, byte fill = 0)
        {
            var pcm = ms * 32;
            var b = new byte[12 + pcm];
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0, 4), frameSeq);
            BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(4, 8), captureOffsetMs);
            if (fill != 0) b.AsSpan(12).Fill(fill);
            return b;
        }

        public async Task SendAudioAsync(int frames, int ms = 250, byte fill = 0, ulong bogusOffset = 0)
        {
            for (var i = 0; i < frames; i++)
            {
                await SendBinaryAsync(Frame((uint)FramesSent++, ms, bogusOffset, fill));
                await Task.Delay(1);
            }
        }

        /// <summary>The close status the server sent, or null if none arrived in time.</summary>
        public async Task<int?> WaitCloseAsync(int timeoutMs = 8000)
        {
            var done = await Task.WhenAny(_closed.Task, Task.Delay(timeoutMs));
            return done == _closed.Task ? _closed.Task.Result : null;
        }

        public async ValueTask DisposeAsync()
        {
            try { _ws.Abort(); _ws.Dispose(); await _receiver.WaitAsync(TimeSpan.FromSeconds(2)); } catch (Exception) { }
        }
    }

    /// <summary>Plain GET with the headers a WebSocket upgrade carries, for pre-upgrade rejections that return an HTTP status.</summary>
    public static async Task<HttpResponseMessage> UpgradeGetAsync(ApiFactory f, string path, string? origin, string? cookie)
    {
        var c = f.NewClient(withOrigin: false);
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.TryAddWithoutValidation("Connection", "Upgrade");
        req.Headers.TryAddWithoutValidation("Upgrade", "websocket");
        req.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");
        req.Headers.TryAddWithoutValidation("Sec-WebSocket-Key", Convert.ToBase64String(Guid.NewGuid().ToByteArray()));
        if (origin is not null) req.Headers.TryAddWithoutValidation("Origin", origin);
        if (cookie is not null) req.Headers.TryAddWithoutValidation("Cookie", cookie);
        return await c.SendAsync(req);
    }
}

public static class HttpAssert
{
    public static void Status(HttpStatusCode expected, HttpResponseMessage r) => Assert.Equal(expected, r.StatusCode);
}
