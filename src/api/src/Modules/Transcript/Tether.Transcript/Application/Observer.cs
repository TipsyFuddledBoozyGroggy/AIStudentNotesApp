using Tether.Lectures.Contracts;
using Tether.Transcript.Hub;
using Tether.Transcript.Relay;

namespace Tether.Transcript.Application;

/// <summary>
/// Reacts to committed session changes: publishes <c>session.state</c> (the source of truth for the recording indicator),
/// and on pause, stop or delete cuts the audio socket first, clears partials, and on delete drops hub connections (ADR-019).
/// </summary>
internal sealed class RealtimeLifecycleObserver(LiveFanout fanout, PartialStore partials, RelayRegistry relays, HubConnections hubs) : ISessionLifecycleObserver
{
    public async Task OnStateChangedAsync(SessionStateChange change, CancellationToken ct)
    {
        if (change.State != LectureState.Active)
        {
            partials.Clear(change.UserId, change.SessionId);
            await relays.CloseSessionAsync(change.SessionId, CloseCodes.InvalidState);
        }
        await fanout.PublishAsync(change.UserId, change.SessionId, EventMapper.ToEnvelope(change), ct);
    }

    public async Task OnDeletedAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        partials.Clear(userId, sessionId);
        await relays.CloseSessionAsync(sessionId, CloseCodes.InvalidState);
        hubs.AbortSession(userId, sessionId);
    }
}
