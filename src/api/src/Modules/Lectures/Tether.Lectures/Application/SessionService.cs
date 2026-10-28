using System.Security.Cryptography;
using System.Text;
using Tether.Courses.Contracts;
using Tether.Lectures.Domain;
using Tether.Lectures.Persistence;
using Tether.SharedKernel;
using Tether.SharedKernel.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tether.Lectures.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tether.Lectures.Application;

internal enum Transition { Pause, Resume, Stop }

/// <summary>Either a session row or the problem to return. Never carries exception text (C-24).</summary>
internal readonly record struct SessionOutcome(SessionRow? Row, IResult? Problem, bool Replayed = false)
{
    public static SessionOutcome Ok(SessionRow row, bool replayed = false) => new(row, null, replayed);
    public static SessionOutcome Fail(IResult problem) => new(null, problem);
}

/// <summary>
/// Session lifecycle (API_CONTRACTS sections 2, 8, 10). Every mutation runs in one SaveChanges transaction that also
/// writes the seq counter, the persisted session.state event, the consent event (SEC-007) and the audit event (C-30).
/// All reads go through the user-scoped query filter, so a foreign id behaves exactly like a missing one.
/// </summary>
internal sealed class SessionService(
    LecturesDbContext db, ICurrentUser currentUser, ICourseOwnership courses, ConsentTextRegistry registry, TimeProvider clock,
    LectureLimits limits, SessionActivityTracker activity, IEnumerable<ISessionLifecycleObserver> observers, IServiceProvider services,
    ILogger<SessionService> log)
{
    public const int MaxTitleLength = 200;
    public static readonly TimeSpan IdempotencyWindow = TimeSpan.FromHours(24);
    private const int MaxAttempts = 3;

    /// <summary>UTC now truncated to microseconds so responses match what PostgreSQL stores.</summary>
    private DateTimeOffset Now()
    {
        var t = clock.GetUtcNow();
        return new DateTimeOffset(t.Ticks - t.Ticks % 10, TimeSpan.Zero);
    }

    private Guid UserId => currentUser.UserId ?? throw new InvalidOperationException("Authenticated user required.");

    // ---------- reads ----------

    // Filtering, ordering and paging happen on the sessions query BEFORE the join: EF cannot translate predicates over
    // the projected SessionRow.
    private IQueryable<SessionRow> Rows(IQueryable<LectureSession> sessions) =>
        from s in sessions
        join c in db.Consents.AsNoTracking() on s.ConsentRecordId equals c.Id
        join k in db.Counters.AsNoTracking() on s.Id equals k.SessionId
        select new SessionRow(s, c.TextVersion, c.AcknowledgedAt, k.LastSeq);

    private bool Demo => services.GetService<IDemoSourceIndicator>()?.IsDemoSource ?? false;

    public async Task<SessionRow?> GetAsync(Guid id, CancellationToken ct) =>
        await Rows(db.Sessions.AsNoTracking().Where(s => s.Id == id)).FirstOrDefaultAsync(ct);

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct) => await db.Sessions.AsNoTracking().AnyAsync(s => s.Id == id, ct);

    public async Task<(List<SessionRow> Items, Guid? Next)> ListAsync(Guid courseId, Guid? after, int take, CancellationToken ct)
    {
        var q = db.Sessions.AsNoTracking().Where(s => s.CourseId == courseId);
        // Ids are UUIDv7 (time ordered), so id order is creation order and a stable keyset.
        if (after is { } a) q = q.Where(s => s.Id.CompareTo(a) < 0);
        var page = q.OrderByDescending(s => s.Id).Take(take + 1);
        var rows = (await Rows(page).ToListAsync(ct)).OrderByDescending(r => r.Session.Id).ToList();
        Guid? next = null;
        if (rows.Count > take) { rows.RemoveAt(rows.Count - 1); next = rows[^1].Session.Id; }
        return (rows, next);
    }

    // ---------- create ----------

    public async Task<SessionOutcome> CreateAsync(Guid courseId, string? title, ConsentRequest? consent, string? idempotencyKey, CancellationToken ct)
    {
        var userId = UserId;
        // 404 before anything else so a foreign course id leaks nothing about consent or sessions.
        if (!await courses.IsOwnedByAsync(userId, courseId, ct)) return SessionOutcome.Fail(Problems.NotFound("Course not found."));

        // C-13: the version must be one the server knows; anything else is not valid consent. This runs BEFORE any
        // idempotent replay so a request carrying invalid consent is never answered with a replayed success (B1-04).
        if (consent is not { Acknowledged: true } || !registry.IsAccepted(consent.TextVersion))
            return SessionOutcome.Fail(Problems.ConsentRequired());

        byte[]? keyHash = null, fingerprint = null;
        if (idempotencyKey is not null)
        {
            keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey));
            fingerprint = Fingerprint(courseId, title, consent.TextVersion, consent.Acknowledged);
            var replay = await TryReplayAsync(keyHash, fingerprint, ct);
            if (replay is { } r) return r;
        }

        if (await db.Sessions.AnyAsync(s => s.State == SessionState.Active || s.State == SessionState.Paused, ct))
            return SessionOutcome.Fail(Problems.InvalidState());

        var now = Now();
        var sessionId = Ids.New();
        var consentId = Ids.New();
        db.Consents.Add(new ConsentRecord
        {
            Id = consentId, CourseId = courseId, SessionId = sessionId, TextVersion = consent.TextVersion!,
            CourseAttested = true, AcknowledgedAt = now,
        });
        db.Sessions.Add(new LectureSession
        {
            Id = sessionId, CourseId = courseId, Title = title, State = SessionState.Active, StartedAt = now, ConsentRecordId = consentId, DemoSource = Demo,
        });
        db.Counters.Add(new SessionCounter { SessionId = sessionId, LastSeq = 1 });
        db.SessionEvents.Add(new SessionEvent { SessionId = sessionId, Seq = 1, State = SessionState.Active, OccurredAt = now });
        AddConsentEvent(consentId, sessionId, ConsentEventKind.Start, consent.TextVersion!, now);
        AddAudit(AuditActions.SessionStart, sessionId, now);
        if (keyHash is not null)
            db.IdempotencyKeys.Add(new IdempotencyRecord
            {
                Id = Ids.New(), KeyHash = keyHash, Fingerprint = fingerprint!, SessionId = sessionId, CreatedAt = now,
            });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lost a race: either the same Idempotency-Key was used concurrently, or another session went live (C-18).
            db.ChangeTracker.Clear();
            if (keyHash is not null && await TryReplayAsync(keyHash, fingerprint!, ct) is { } won) return won;
            return SessionOutcome.Fail(Problems.InvalidState());
        }

        return SessionOutcome.Ok((await GetAsync(sessionId, ct))!);
    }

    /// <summary>
    /// Collision-safe request fingerprint (B1-04): SHA-256 of a JSON array of the fields. JSON encoding is unambiguous
    /// (strings are quoted and escaped, null differs from empty), unlike a delimiter join.
    /// </summary>
    internal static byte[] Fingerprint(Guid courseId, string? title, string? textVersion, bool? acknowledged) =>
        SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new object?[] { courseId.ToString("N"), title, textVersion, acknowledged }));

    /// <summary>Replay returns the original session without a second consent event (C-32). A reused key with a different request is a 400.</summary>
    private async Task<SessionOutcome?> TryReplayAsync(byte[] keyHash, byte[] fingerprint, CancellationToken ct)
    {
        var rec = await db.IdempotencyKeys.FirstOrDefaultAsync(k => k.KeyHash == keyHash, ct);
        if (rec is null) return null;
        if (rec.CreatedAt < Now() - IdempotencyWindow)
        {
            db.IdempotencyKeys.Remove(rec); // expired (24 h): the key is free again
            await db.SaveChangesAsync(ct);
            return null;
        }
        if (!CryptographicOperations.FixedTimeEquals(rec.Fingerprint, fingerprint))
            return SessionOutcome.Fail(Problems.Validation("Idempotency-Key", "This key was already used for a different request."));
        var row = await GetAsync(rec.SessionId, ct);
        return row is null ? null : SessionOutcome.Ok(row, replayed: true);
    }

    // ---------- pause / resume / stop ----------

    /// <param name="systemReason">Set by server jobs and course lifecycle (auto-pause, auto-stop, hard cap, course_archived, course_deleted). User commands pass null. Only a user Resume is subject to the transition caps: Pause and Stop are never rate limited, capped or refused, because consent must stay reversible (ADR-019b, B2-01).</param>
    public async Task<SessionOutcome> TransitionAsync(Guid id, Transition t, CancellationToken ct, string? systemReason = null)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var s = await db.Sessions.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return SessionOutcome.Fail(Problems.NotFound("Session not found."));

            var (target, from, kind, action) = t switch
            {
                Transition.Pause => (SessionState.Paused, SessionState.Active, ConsentEventKind.Pause, AuditActions.SessionPause),
                Transition.Resume => (SessionState.Active, SessionState.Paused, ConsentEventKind.Resume, AuditActions.SessionResume),
                _ => (SessionState.Stopped, SessionState.Active, ConsentEventKind.Stop, AuditActions.SessionStop),
            };
            if (s.State == target) return SessionOutcome.Ok((await GetAsync(id, ct))!); // idempotent: no new event
            var allowed = s.State == from || (t == Transition.Stop && s.State == SessionState.Paused);
            if (!allowed) return SessionOutcome.Fail(Problems.InvalidState());

            if (t == Transition.Resume)
            {
                // Defense in depth for B1-01 (FK cascade removes the session with the course) and B1-08 (retired consent version).
                if (!await courses.IsOwnedByAsync(UserId, s.CourseId, ct)) return SessionOutcome.Fail(Problems.NotFound("Session not found."));
                var rc = await db.Consents.AsNoTracking().FirstAsync(c => c.Id == s.ConsentRecordId, ct);
                if (!registry.IsAccepted(rc.TextVersion)) return SessionOutcome.Fail(Problems.ConsentRequired());
            }

            var now = Now();
            if (systemReason is null && t == Transition.Resume && await OverTransitionCapAsync(id, now, ct))
                return SessionOutcome.Fail(Problems.ForStatus(429)); // B1-06
            var consent = await db.Consents.AsNoTracking().FirstAsync(c => c.Id == s.ConsentRecordId, ct);
            await ApplyStateAsync(s, target, kind, action, consent, now, systemReason, ct);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException
                                       || ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } })
            {
                db.ChangeTracker.Clear(); // another writer got there first; re-read and re-evaluate (idempotent)
                continue;
            }
            var result = (await GetAsync(id, ct))!;
            if (target != SessionState.Active) activity.Forget(id);
            await NotifyStateAsync(result, systemReason ?? "user", now, ct);
            return SessionOutcome.Ok(result);
        }
        return SessionOutcome.Fail(Problems.ConcurrencyConflict());
    }

    /// <summary>Writes state, seq, events and ticket invalidation (pause/stop) into the current unit of work.</summary>
    private async Task ApplyStateAsync(LectureSession s, SessionState target, ConsentEventKind kind, string action,
        ConsentRecord consent, DateTimeOffset now, string? systemReason, CancellationToken ct)
    {
        var counter = await db.Counters.FirstAsync(c => c.SessionId == s.Id, ct);
        counter.LastSeq++;
        s.State = target;
        s.StoppedAt = target == SessionState.Stopped ? now : null;
        // reason vocabulary (section 12.1): user, auto_pause_no_audio, auto_stop_paused_timeout, hard_cap, abandoned
        db.SessionEvents.Add(new SessionEvent { SessionId = s.Id, Seq = counter.LastSeq, State = target, Reason = systemReason ?? "user", OccurredAt = now });
        AddConsentEvent(consent.Id, s.Id, kind, consent.TextVersion, now);
        AddAudit(action, s.Id, now);
        if (target != SessionState.Active) // C-01: pause and stop invalidate the outstanding ticket
            db.AudioTickets.RemoveRange(await db.AudioTickets.Where(x => x.SessionId == s.Id).ToListAsync(ct));
    }

    // ---------- course archive / purge (B2-06, B2-07) ----------

    public const string ReasonCourseArchived = "course_archived";
    public const string ReasonCourseDeleted = "course_deleted";

    /// <summary>
    /// Stops every Active or Paused session of the course through the normal state machine (consent Stop event, audit, seq,
    /// ticket invalidation, observers close sockets), then for a purge also audits the deletion of each session the FK cascade
    /// is about to remove and drops live hub connections. Stop is never refused (ADR-019b).
    /// </summary>
    public async Task EndSessionsForCourseAsync(Guid courseId, bool purge, CancellationToken ct)
    {
        var reason = purge ? ReasonCourseDeleted : ReasonCourseArchived;
        var live = await db.Sessions.AsNoTracking()
            .Where(s => s.CourseId == courseId && (s.State == SessionState.Active || s.State == SessionState.Paused))
            .Select(s => s.Id).ToListAsync(ct);
        foreach (var id in live) await TransitionAsync(id, Transition.Stop, ct, reason);
        if (!purge) return;

        var now = Now();
        foreach (var id in await db.Sessions.AsNoTracking().Where(s => s.CourseId == courseId).Select(s => s.Id).ToListAsync(ct))
            AddAudit(AuditActions.SessionDelete, id, now);
        await db.SaveChangesAsync(ct);
        foreach (var id in live)
        {
            activity.Forget(id);
            await NotifyDeletedAsync(UserId, id, ct);
        }
    }

    // ---------- delete ----------

    /// <summary>
    /// C-21. Stops an Active/Paused session first (consent Stop event), then removes the session and everything that
    /// cascades from it. consent_records, consent_events and audit_events are content free and are kept.
    /// Transcript, summaries, notes and jobs (later increments) must be deleted in this same operation and their writers
    /// must check the session still exists.
    /// </summary>
    public async Task<IResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var s = await db.Sessions.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Problems.NotFound("Session not found.");
            var now = Now();
            if (s.State is SessionState.Active or SessionState.Paused)
            {
                var consent = await db.Consents.AsNoTracking().FirstAsync(c => c.Id == s.ConsentRecordId, ct);
                AddConsentEvent(consent.Id, s.Id, ConsentEventKind.Stop, consent.TextVersion, now);
                AddAudit(AuditActions.SessionStop, s.Id, now);
            }
            AddAudit(AuditActions.SessionDelete, s.Id, now);
            db.Sessions.Remove(s);
            try
            {
                await db.SaveChangesAsync(ct);
                activity.Forget(id);
                await NotifyDeletedAsync(s.UserId, id, ct);
                return Results.NoContent();
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                if (!await ExistsAsync(id, ct)) return Problems.NotFound("Session not found."); // lost a race to another delete
            }
        }
        return Problems.ConcurrencyConflict();
    }

    // ---------- audio ticket ----------

    public static readonly TimeSpan TicketLifetime = TimeSpan.FromSeconds(30);

    public async Task<(AudioTicketResponse? Ticket, IResult? Problem)> IssueTicketAsync(Guid sessionId, CancellationToken ct)
    {
        var authSid = currentUser.AuthSessionId;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var row = await GetAsync(sessionId, ct);
            if (row is null) return (null, Problems.NotFound("Session not found."));
            if (authSid is null) return (null, Problems.Unauthenticated());
            if (!registry.IsAccepted(row.TextVersion)) return (null, Problems.ConsentRequired());
            if (row.Session.State != SessionState.Active) return (null, Problems.InvalidState());
            if (!await courses.IsOwnedByAsync(UserId, row.Session.CourseId, ct)) return (null, Problems.NotFound("Session not found."));

            var now = Now();
            var token = WebEncoders256.NewToken();
            var hash = SHA256.HashData(Encoding.ASCII.GetBytes(token));
            // One outstanding ticket per session: replace the row in place (unique index on session_id enforces it).
            var existing = await db.AudioTickets.FirstOrDefaultAsync(t => t.SessionId == sessionId, ct);
            if (existing is null)
                db.AudioTickets.Add(new AudioTicket
                {
                    Id = Ids.New(), SessionId = sessionId, AuthSessionId = authSid.Value, TokenHash = hash, CreatedAt = now, ExpiresAt = now + TicketLifetime,
                });
            else
            {
                existing.AuthSessionId = authSid.Value; existing.TokenHash = hash; existing.CreatedAt = now; existing.ExpiresAt = now + TicketLifetime;
            }
            AddAudit(AuditActions.TicketIssued, sessionId, now);
            try
            {
                await db.SaveChangesAsync(ct);
                return (new AudioTicketResponse(token, now + TicketLifetime), null);
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException
                                       || ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } })
            {
                db.ChangeTracker.Clear(); // concurrent issue or pause/stop/delete; re-evaluate from the top
            }
        }
        return (null, Problems.ConcurrencyConflict());
    }

    // ---------- helpers ----------

    /// <summary>B1-06 / ADR-019b: applies to RESUME only. At most MaxTransitionsPerMinute pause/resume in the last minute, and MaxTransitionsPerSession events in total.</summary>
    private async Task<bool> OverTransitionCapAsync(Guid sessionId, DateTimeOffset now, CancellationToken ct)
    {
        var total = await db.SessionEvents.CountAsync(e => e.SessionId == sessionId, ct);
        if (total >= limits.MaxTransitionsPerSession) return true;
        var since = now - TimeSpan.FromMinutes(1);
        var recent = await db.SessionEvents.CountAsync(e => e.SessionId == sessionId && e.Seq > 1 && e.OccurredAt > since && e.State != SessionState.Stopped, ct);
        return recent >= limits.MaxTransitionsPerMinute;
    }

    private async Task NotifyStateAsync(SessionRow row, string reason, DateTimeOffset at, CancellationToken ct)
    {
        var change = new SessionStateChange(row.Session.UserId, row.Session.Id, row.Session.State.ToContract(), reason, row.LastSeq, at);
        foreach (var o in observers)
        {
            try { await o.OnStateChangedAsync(change, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Lifecycle observer failed: {ExceptionType}", ex.GetType().Name); }
        }
    }

    private async Task NotifyDeletedAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        foreach (var o in observers)
        {
            try { await o.OnDeletedAsync(userId, sessionId, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Lifecycle observer failed: {ExceptionType}", ex.GetType().Name); }
        }
    }

    private void AddConsentEvent(Guid consentRecordId, Guid sessionId, ConsentEventKind kind, string textVersion, DateTimeOffset now) =>
        db.ConsentEvents.Add(new ConsentEvent
        {
            Id = Ids.New(), ConsentRecordId = consentRecordId, SessionId = sessionId, Kind = kind, TextVersion = textVersion, OccurredAt = now,
        });

    private void AddAudit(string action, Guid sessionId, DateTimeOffset now) =>
        db.AuditEvents.Add(new AuditEvent { Id = Ids.New(), SessionId = sessionId, Action = action, OccurredAt = now });
}

internal static class WebEncoders256
{
    /// <summary>256 bits from the OS CSPRNG, base64url (43 chars, no padding).</summary>
    public static string NewToken() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
}
