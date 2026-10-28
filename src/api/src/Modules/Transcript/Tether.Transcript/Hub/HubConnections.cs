using System.Collections.Concurrent;
using Tether.Lectures.Contracts;
using Tether.SharedKernel;
using Tether.Transcript.Application;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tether.Transcript.Hub;

internal sealed class HubConnection(string connectionId, Guid userId, Guid authSessionId, DateTimeOffset connectedAt, Action abort)
{
    public string ConnectionId { get; } = connectionId;
    public Guid UserId { get; } = userId;
    public Guid AuthSessionId { get; } = authSessionId;
    public DateTimeOffset ConnectedAt { get; } = connectedAt;
    public ConcurrentDictionary<Guid, byte> Sessions { get; } = new();
    public void Abort() { try { abort(); } catch (Exception) { /* connection already gone */ } }
}

/// <summary>Live hub connections, bound to the login session they were opened with (C-06).</summary>
internal sealed class HubConnections
{
    private readonly ConcurrentDictionary<string, HubConnection> _all = new();

    public HubConnection Add(string id, Guid userId, Guid authSid, DateTimeOffset now, Action abort) =>
        _all[id] = new HubConnection(id, userId, authSid, now, abort);

    public void Remove(string id) => _all.TryRemove(id, out _);
    public void Joined(string id, Guid sessionId) { if (_all.TryGetValue(id, out var c)) c.Sessions[sessionId] = 0; }
    public void Left(string id, Guid sessionId) { if (_all.TryGetValue(id, out var c)) c.Sessions.TryRemove(sessionId, out _); }
    public int CountForUser(Guid userId) => _all.Values.Count(c => c.UserId == userId);
    public IReadOnlyList<Guid> SessionsOf(string id) => _all.TryGetValue(id, out var c) ? c.Sessions.Keys.ToList() : [];
    public IReadOnlyList<HubConnection> Snapshot() => _all.Values.ToList();

    /// <summary>Aborts every connection opened with the revoked login session (B2-03).</summary>
    public void AbortAuthSession(Guid userId, Guid authSessionId)
    {
        foreach (var c in _all.Values.Where(c => c.UserId == userId && c.AuthSessionId == authSessionId)) c.Abort();
    }

    /// <summary>Aborts every connection of the user that joined the session (session or course deleted).</summary>
    public void AbortSession(Guid userId, Guid sessionId)
    {
        foreach (var c in _all.Values.Where(c => c.UserId == userId && c.Sessions.ContainsKey(sessionId))) c.Abort();
    }
}

/// <summary>Delivers events to the per-user, per-session group.</summary>
internal sealed class LiveFanout(IHubContext<LectureHub> hub)
{
    public Task PublishAsync(Guid userId, Guid sessionId, Envelope envelope, CancellationToken ct = default) =>
        hub.Clients.Group(LectureHub.GroupName(userId, sessionId)).SendAsync(LectureHub.ClientMethod, envelope, ct);
}

/// <summary>
/// C-06: every <c>Realtime:RevalidateSeconds</c> (60 s) each hub connection is re-checked: login session still valid,
/// connection younger than the maximum lifetime, and every joined session still exists for its owner. Covers logout,
/// session revocation, user deletion and course purge (which removes sessions by FK cascade without telling us).
/// </summary>
internal sealed class HubReaper(HubConnections connections, IServiceScopeFactory scopes, RealtimeOptions options, TimeProvider clock,
    ILogger<HubReaper> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.HubRevalidate);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken)) await SweepAsync(stoppingToken);
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        foreach (var c in connections.Snapshot())
        {
            try
            {
                if (clock.GetUtcNow() - c.ConnectedAt >= options.MaxHubConnectionAge) { c.Abort(); continue; }
                await using var scope = scopes.CreateUserScope(c.UserId, c.AuthSessionId);
                var sp = scope.ServiceProvider;
                if (!await sp.GetRequiredService<IAuthSessionValidator>().IsValidAsync(c.UserId, c.AuthSessionId, ct)) { c.Abort(); continue; }
                var directory = sp.GetRequiredService<ISessionDirectory>();
                foreach (var s in c.Sessions.Keys)
                    if (await directory.FindAsync(c.UserId, s, ct) is null) { c.Abort(); break; }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning("Hub revalidation failed: {ExceptionType}", ex.GetType().Name); // type only
            }
        }
    }
}
