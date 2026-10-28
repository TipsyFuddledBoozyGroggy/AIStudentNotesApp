using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Tether.Lectures.Contracts;
using Tether.SharedKernel;
using Tether.Transcript.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tether.Transcript.Hub;

/// <summary>
/// SignalR hub for protocol lecture.v1 (API_CONTRACTS section 3): downstream events only. Clients send Join, Leave and Ping and
/// never transcript content. Server events are delivered to the group of the joined session as a single client method named
/// <c>event</c> whose argument is the envelope. Group names are built here from the authenticated user id and a session the
/// directory resolved for that user, so a connection can never be placed in someone else's group. A session that belongs to
/// someone else fails exactly like one that does not exist. Errors are <see cref="HubException"/> with a code from the shared
/// ProblemDetails vocabulary as the message, never exception text.
/// </summary>
[Authorize]
internal sealed class LectureHub(
    ISessionDirectory directory, TranscriptReader reader, PartialStore partials, HubConnections connections, JoinLimiter joins,
    ISessionActivity activity, RealtimeOptions options, TimeProvider clock, HubInvocationLimiter invocations,
    IEnumerable<Tether.Transcript.Contracts.ISummaryJoinSource> summaries) : Microsoft.AspNetCore.SignalR.Hub
{
    public const string ClientMethod = "event";
    public const string Protocol = "lecture.v1";

    public static string GroupName(Guid userId, Guid sessionId) => $"lec:{userId:N}:{sessionId:N}";

    private Guid UserId => AuthClaims.UserIdOf(Context.User) ?? throw new HubException("unauthenticated");

    public override Task OnConnectedAsync()
    {
        var userId = AuthClaims.UserIdOf(Context.User);
        var sid = AuthClaims.SessionIdOf(Context.User);
        if (userId is null || sid is null) { Context.Abort(); return Task.CompletedTask; }
        var conn = connections.Add(Context.ConnectionId, userId.Value, sid.Value, clock.GetUtcNow(), Context.Abort);
        // C-26: bounded concurrent connections per user. The newest connection is the one refused.
        if (connections.CountForUser(userId.Value) > options.MaxHubConnectionsPerUser)
        {
            connections.Remove(Context.ConnectionId);
            Context.Abort();
        }
        _ = conn;
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        invocations.Forget(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public async Task<object> Join(JsonElement request)
    {
        var (sessionId, lastSeq, lastSummaryRevision) = ParseJoin(request);
        var userId = UserId;
        if (!joins.TryAcquire(userId, sessionId)) throw new HubException("rate_limited");

        // Foreign and missing sessions are indistinguishable (C-09).
        var info = await directory.FindAsync(userId, sessionId, Context.ConnectionAborted) ?? throw new HubException("not_found");

        // Subscribe first, then read the backlog: an event committed in between is delivered live and deduplicated by (sessionId, seq).
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(userId, sessionId), Context.ConnectionAborted);
        connections.Joined(Context.ConnectionId, sessionId);
        activity.Touch(sessionId);

        string? fromSeq = null, toSeq = null;
        var truncated = false;
        var current = info.LastSeq;
        if (lastSeq <= info.LastSeq) // out-of-range lastSeq: current state, empty replay (C-12)
        {
            var page = await reader.ReadAfterAsync(userId, sessionId, lastSeq, options.ReplayCap, Context.ConnectionAborted);
            if (page.HasMore) truncated = true; // cap exceeded: replay nothing, the client pages GET /transcript
            else if (page.Events.Count > 0)
            {
                foreach (var e in page.Events) await Clients.Caller.SendAsync(ClientMethod, e, Context.ConnectionAborted);
                fromSeq = page.Events[0].Seq;
                toSeq = page.Events[^1].Seq;
                current = Math.Max(current, long.Parse(toSeq!, CultureInfo.InvariantCulture));
            }
        }
        foreach (var p in partials.Snapshot(userId, sessionId)) // seq null, latest per segment only
            await Clients.Caller.SendAsync(ClientMethod, p, Context.ConnectionAborted);

        // B3: report the newest summary revision and replay it when the client has not seen it (summary.* events are ephemeral).
        int? currentSummaryRevision = null;
        foreach (var source in summaries)
        {
            var snapshot = await source.LatestAsync(userId, sessionId, Context.ConnectionAborted);
            if (snapshot is null) continue;
            currentSummaryRevision = snapshot.Revision;
            if (snapshot.Revision > lastSummaryRevision)
                await Clients.Caller.SendAsync(ClientMethod, Envelope.Ephemeral("summary.revision", sessionId, clock.GetUtcNow(), snapshot.Payload), Context.ConnectionAborted);
            break;
        }

        return new JoinResponse(info.State.ToString(), current.ToString(CultureInfo.InvariantCulture), currentSummaryRevision,
            new ReplayInfo(fromSeq, toSeq, truncated));
    }

    public async Task Leave(JsonElement request)
    {
        var sessionId = ParseSessionOnly(request);
        var userId = UserId;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(userId, sessionId), Context.ConnectionAborted);
        connections.Left(Context.ConnectionId, sessionId);
    }

    public Task Ping()
    {
        foreach (var s in connections.SessionsOf(Context.ConnectionId)) activity.Touch(s); // heartbeat (C-17)
        return Task.CompletedTask;
    }

    // ---- strict parsing (additionalProperties:false on the wire contract) ----

    private static (Guid SessionId, long LastSeq, int LastSummaryRevision) ParseJoin(JsonElement r)
    {
        if (r.ValueKind != JsonValueKind.Object) throw new HubException("validation_failed");
        Guid? sid = null; long lastSeq = 0; var protocolOk = false; var lastRevision = 0;
        foreach (var p in r.EnumerateObject())
        {
            switch (p.Name)
            {
                case "sessionId" when p.Value.ValueKind == JsonValueKind.String && Guid.TryParse(p.Value.GetString(), out var g): sid = g; break;
                case "protocol" when p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() == Protocol: protocolOk = true; break;
                case "lastSeq" when p.Value.ValueKind == JsonValueKind.String
                    && long.TryParse(p.Value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var s)
                    && (p.Value.GetString() == "0" || !p.Value.GetString()!.StartsWith('0')): lastSeq = s; break;
                case "lastSummaryRevision" when p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var rev) && rev >= 0: lastRevision = rev; break;
                default: throw new HubException("validation_failed"); // unknown field or wrong type
            }
        }
        if (sid is null || !protocolOk) throw new HubException("validation_failed");
        return (sid.Value, lastSeq, lastRevision);
    }

    private static Guid ParseSessionOnly(JsonElement r)
    {
        if (r.ValueKind != JsonValueKind.Object) throw new HubException("validation_failed");
        Guid? sid = null;
        foreach (var p in r.EnumerateObject())
        {
            if (p.Name == "sessionId" && p.Value.ValueKind == JsonValueKind.String && Guid.TryParse(p.Value.GetString(), out var g)) sid = g;
            else throw new HubException("validation_failed");
        }
        return sid ?? throw new HubException("validation_failed");
    }
}

internal sealed record ReplayInfo(string? FromSeq, string? ToSeq, bool Truncated);
internal sealed record JoinResponse(string State, string CurrentSeq, int? CurrentSummaryRevision, ReplayInfo Replay);

/// <summary>Runs every hub invocation inside the caller's identity and keeps failures content free.</summary>
internal sealed class HubUserFilter(ILogger<HubUserFilter> log, HubInvocationLimiter invocations) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        context.ServiceProvider.GetRequiredService<UserScope>().Set(context.Context.User);
        // B2-02: every invocation counts, before the method runs and so before any database access.
        if (!invocations.TryAcquire(context.Context.ConnectionId)) throw new HubException("rate_limited");
        try
        {
            return await next(context);
        }
        catch (HubException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log.LogWarning("Hub method failed: {ExceptionType}", ex.GetType().Name); // type only (F-08)
            throw new HubException("internal_error");
        }
    }

    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        context.ServiceProvider.GetRequiredService<UserScope>().Set(context.Context.User);
        return next(context);
    }
}

/// <summary>
/// C-26 and B2-02: at most N Joins per minute per (user, session) and M per user across all session ids, checked before any
/// database access, so random ids cannot be used to hammer the database. Bounded memory (idle keys are swept, the tables are capped).
/// </summary>
internal sealed class JoinLimiter
{
    public const int MaxTrackedKeys = 20000;
    private readonly SlidingWindowCounter<(Guid, Guid)> _pairs;
    private readonly SlidingWindowCounter<Guid> _users;

    public JoinLimiter(RealtimeOptions options, TimeProvider clock)
    {
        _pairs = new(options.JoinPermitPerMinute, TimeSpan.FromMinutes(1), clock, MaxTrackedKeys * 3 / 4);
        _users = new(options.JoinPermitPerUserPerMinute, TimeSpan.FromMinutes(1), clock, MaxTrackedKeys / 4);
    }

    public int TrackedKeys => _pairs.TrackedKeys + _users.TrackedKeys;

    public bool TryAcquire(Guid userId, Guid sessionId) => _users.TryAcquire(userId) && _pairs.TryAcquire((userId, sessionId));
}

/// <summary>B2-02: caps every hub invocation per connection (Join, Leave, Ping) before the hub method, and so before any database access.</summary>
internal sealed class HubInvocationLimiter(RealtimeOptions options, TimeProvider clock)
{
    private readonly SlidingWindowCounter<string> _perConnection = new(options.HubInvocationsPerMinute, TimeSpan.FromMinutes(1), clock, 10000);
    public int TrackedKeys => _perConnection.TrackedKeys;
    public bool TryAcquire(string connectionId) => _perConnection.TryAcquire(connectionId);
    public void Forget(string connectionId) => _perConnection.Forget(connectionId);
}
