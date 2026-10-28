using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using Tether.Lectures.Contracts;
using Tether.SharedKernel;
using Tether.SharedKernel.Http;
using Tether.Transcript.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tether.Transcript.Relay;

/// <summary>
/// <c>GET /ingest/sessions/{id}/audio</c>, protocol speech.v1 (API_CONTRACTS section 4). Preconditions before the upgrade: the
/// host's Origin middleware has already answered 403 for a missing, null or foreign Origin (F-04), the fallback policy requires
/// the session cookie, and a per-user limiter bounds upgrades (B1-05). After the upgrade the first text message must be a
/// <c>hello</c> carrying the single-use ticket; every ticket failure closes with 4401 whatever the cause.
/// </summary>
internal static class AudioIngestEndpoint
{
    public const string UpgradePolicy = "ingest-upgrade";

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/ingest/sessions/{id:guid}/audio", HandleAsync).RequireRateLimiting(UpgradePolicy);

    private static async Task<IResult> HandleAsync(Guid id, HttpContext http, RelayServices s, FailedHelloLimiter failures)
    {
        var userId = AuthClaims.UserIdOf(http.User);
        var authSid = AuthClaims.SessionIdOf(http.User);
        if (userId is null || authSid is null) return Problems.Unauthenticated();
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (failures.IsBlocked(userId.Value, ip)) return Problems.ForStatus(429); // repeated failed hellos (C-04)
        if (!http.WebSockets.IsWebSocketRequest) return Problems.Validation("upgrade", "A WebSocket upgrade is required.");

        using var ws = await http.WebSockets.AcceptWebSocketAsync();
        var buffer = new byte[RealtimeOptions.MaxFrameBytes + 1];

        // 1. hello within the timeout, at most 1 KB; binary before hello closes the socket.
        using (var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted))
        {
            helloTimeout.CancelAfter(s.Options.HelloTimeout);
            (WebSocketMessageType Type, int Length, bool Oversize) msg;
            try { msg = await RelayConnection.ReceiveMessageAsync(ws, buffer, helloTimeout.Token); }
            catch (OperationCanceledException) { await CloseQuietly(ws, CloseCodes.PolicyViolation); return Results.Empty; }
            catch (WebSocketException) { return Results.Empty; }
            if (msg.Type == WebSocketMessageType.Close) return Results.Empty;
            if (msg.Type != WebSocketMessageType.Text) { await CloseQuietly(ws, CloseCodes.PolicyViolation); return Results.Empty; }
            if (msg.Oversize || msg.Length > RealtimeOptions.MaxHelloBytes) { await CloseQuietly(ws, CloseCodes.TooBig); return Results.Empty; }
            var ticket = ParseHello(buffer.AsSpan(0, msg.Length));
            if (ticket is null) { await CloseQuietly(ws, CloseCodes.InvalidPayload); return Results.Empty; }

            // 2. Redeem the ticket: single use, bound to this user, login and session, Session still Active (C-01/C-02).
            var consumer = http.RequestServices.GetRequiredService<IAudioTicketConsumer>();
            if (!await consumer.ConsumeAsync(ticket, id, http.RequestAborted))
            {
                failures.Record(userId.Value, ip);
                await CloseQuietly(ws, CloseCodes.Unauthorized);
                return Results.Empty;
            }
        }

        // 3. One socket per session and a per-user cap.
        var relay = new RelayConnection(ws, s, userId.Value, authSid.Value, id);
        if (!s.Registry.TryAdd(relay)) { await CloseQuietly(ws, CloseCodes.InvalidState); return Results.Empty; }
        await relay.RunAsync(buffer, http.RequestAborted);
        return Results.Empty;
    }

    /// <summary>Strict hello parse (the schema forbids extra fields). Returns the ticket, or null if malformed.</summary>
    internal static string? ParseHello(ReadOnlySpan<byte> utf8)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8);
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            string? ticket = null; bool type = false, version = false, rate = false, enc = false, ch = false;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "type" when p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() == "hello": type = true; break;
                    case "version" when p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() == "speech.v1": version = true; break;
                    case "ticket" when p.Value.ValueKind == JsonValueKind.String: ticket = p.Value.GetString(); break;
                    case "sampleRate" when p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var r) && r == 16000: rate = true; break;
                    case "encoding" when p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() == "pcm_s16le": enc = true; break;
                    case "channels" when p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var c) && c == 1: ch = true; break;
                    case "resumeFromFrame" when p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetUInt32(out _): break; // advisory only
                    default: return null;
                }
            }
            return type && version && rate && enc && ch && ticket is { Length: >= 16 and <= 512 } ? ticket : null;
        }
        catch (JsonException) { return null; }
    }

    private static async Task CloseQuietly(WebSocket ws, int code)
    {
        try
        {
            using var t = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived) await ws.CloseOutputAsync((WebSocketCloseStatus)code, null, t.Token);
        }
        catch (Exception) { ws.Abort(); }
    }
}

/// <summary>Failed hellos per user and per client IP in the last minute (C-04). Only counts; stores no ticket or content.</summary>
internal sealed class FailedHelloLimiter(RealtimeOptions options, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _hits = new();

    private static string UserKey(Guid u) => "u:" + u.ToString("N");
    private static string IpKey(string ip) => "ip:" + ip;

    public void Record(Guid userId, string ip)
    {
        Add(UserKey(userId));
        Add(IpKey(ip));
    }

    public bool IsBlocked(Guid userId, string ip) => Count(UserKey(userId)) >= options.FailedHelloPerMinute || Count(IpKey(ip)) >= options.FailedHelloPerMinute * 5;

    private void Add(string key)
    {
        var q = _hits.GetOrAdd(key, _ => new());
        lock (q) { Prune(q); q.Enqueue(clock.GetUtcNow()); }
    }

    private int Count(string key)
    {
        if (!_hits.TryGetValue(key, out var q)) return 0;
        lock (q) { Prune(q); return q.Count; }
    }

    private void Prune(Queue<DateTimeOffset> q)
    {
        var now = clock.GetUtcNow();
        while (q.Count > 0 && now - q.Peek() >= TimeSpan.FromMinutes(1)) q.Dequeue();
    }
}
