namespace Tether.Lectures.Contracts;

/// <summary>
/// Public port used by the audio ingest socket (B2) to redeem the single-use ticket from
/// <c>POST /sessions/{id}/audio-ticket</c> (API_CONTRACTS section 8, C-01/C-02).
/// </summary>
public interface IAudioTicketConsumer
{
    /// <summary>
    /// Atomically consumes the ticket. Returns true only if it was issued for this session to the current user and the
    /// current login, has not expired, was not invalidated, and the session is still Active. Every failure (bad, expired,
    /// used, foreign, invalidated, session not Active) returns the same <c>false</c>, so callers cannot distinguish them
    /// (they must answer with the same close code, 4401). The caller is responsible for the Origin check (F-04).
    /// </summary>
    Task<bool> ConsumeAsync(string ticket, Guid sessionId, CancellationToken ct = default);
}
