using System.Globalization;
using System.Security.Cryptography;
using Tether.Ai.Contracts;
using Tether.Docs.Contracts;
using Tether.Docs.Domain;
using Tether.Docs.Persistence;
using Tether.Lectures.Contracts;
using Tether.SharedKernel;
using Tether.SharedKernel.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Tether.Docs.Application;

/// <summary>
/// The Docs export: preview (writes nothing), confirm (one transaction per attempt), state, Demo Doc read and delete.
/// LOCK ORDER of every writer, no exceptions (DXD-01, DXD-14, DXD-16, L-LOCKORDER): (1) the session row FOR KEY SHARE through
/// <see cref="IDocsSessionSource.LockAndReadAsync"/> (null = the lecture is gone = 404, the C-21 re-check); (2) the advisory lock on (user, session);
/// (3) the advisory lock on the user, after (2), so the per-user document cap is race-free across lectures; (4) docs rows in the order doc_exports,
/// idempotency_keys, demo_docs (inside the provider call), doc_targets, audit_events. A deadlock (40P01/40001) repeats the WHOLE attempt without
/// raising <c>attempt</c>. No AI client, no token, no network is reachable from this class (invariant 3).
/// </summary>
internal sealed class DocsExportService(
    DocsDbContext db, ICurrentUser user, IDocsSessionSource sessions, INotesSnapshotReader notes, IDocsProvider provider,
    DocsKeys keys, DocsOptions opt, TimeProvider clock, ILogger<DocsExportService> log)
{
    private Guid Me => user.UserId ?? throw new InvalidOperationException("Authenticated user required.");

    private DateTimeOffset Now() => new(clock.GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero); // microseconds, like PostgreSQL

    private static string UserSessionLock(Guid user, Guid session) => "docs:session:" + user.ToString("N") + ":" + session.ToString("N");
    private static string UserLock(Guid user) => "docs:user:" + user.ToString("N");

    // ------------------------------------------------------------------------------------------------ ownership (step 4)

    public async Task<bool> OwnsAsync(Guid sessionId, CancellationToken ct) => await sessions.ReadAsync(sessionId, ct) is not null;

    // ------------------------------------------------------------------------------------------------ preview

    private sealed record Prepared(DocsSessionFacts Facts, NotesSnapshot Notes, BuiltDocument Built, string Digest);

    /// <summary>Step 9 for preview and confirm alike: state, then the notes id (404 unless it is the CURRENT press, DXD-12), then Ready, then build.</summary>
    private IResult? Prepare(DocsSessionFacts facts, NotesSnapshot? snapshot, string notesId, out Prepared? prepared)
    {
        prepared = null;
        if (facts.State != LectureState.Stopped) return DocsProblem.InvalidState();
        if (snapshot is null || !string.Equals(snapshot.NotesId, notesId, StringComparison.Ordinal)) return DocsProblem.NotFound();
        if (!string.Equals(snapshot.Status, "Ready", StringComparison.Ordinal)) return DocsProblem.InvalidState();
        var built = DocumentBuilder.Build(facts, snapshot);
        var stats = built.Stats;
        if (stats.Dropped + stats.Shortened + stats.CitationsDropped > 0)
            log.LogInformation("Docs build adjusted: dropped {Dropped}, shortened {Shortened}, citations dropped {Citations}", stats.Dropped, stats.Shortened, stats.CitationsDropped);
        prepared = new Prepared(facts, snapshot, built, ContentDigest.Compute(keys.Digest, facts.SessionId, snapshot.NotesId, built.Document));
        return null;
    }

    private static IReadOnlyList<string> Actions(bool blocked, DocTarget? target, bool upToDate) =>
        blocked ? [] : target is null ? ["create"] : upToDate ? [] : ["update", "create_another"];

    public async Task<IResult> PreviewAsync(Guid sessionId, string notesId, CancellationToken ct)
    {
        var facts = await sessions.ReadAsync(sessionId, ct);
        if (facts is null) return DocsProblem.NotFound();
        var snapshot = await notes.FindCurrentAsync(sessionId, ct);
        if (Prepare(facts, snapshot, notesId, out var p) is { } problem) return problem;

        var target = await db.Targets.AsNoTracking().FirstOrDefaultAsync(t => t.SessionId == sessionId && t.Provider == DocsConstants.Provider, ct);
        var blocked = p!.Built.TooLarge || p.Built.NothingToExport;
        var upToDate = target is not null && ContentDigest.Equal(target.LastDigest, p.Digest);
        var actions = Actions(blocked, target, upToDate);
        var existing = target is null ? null
            : new ExistingDto(WireId.Format(DocsConstants.ExportPrefix, target.LastExportId), DocRefDto.Of(target.DocId, target.Version), target.UpdatedAt, upToDate);
        var dto = new PreviewDto(true, sessionId.ToString("D"), p.Notes.NotesId, p.Notes.Truncated, p.Notes.Source == "mock" ? "mock" : "model", DestinationDto.Demo,
            DocumentDto.Of(p.Built.Document.Title, p.Built.Document.Blocks), new CountsDto(p.Built.Sections, p.Built.Bullets, p.Built.Citations), p.Digest, existing, actions,
            !blocked, p.Built.TooLarge ? "too_large" : p.Built.NothingToExport ? "nothing_to_export" : null, Now());
        return Results.Json(dto, JsonBody.Out);
    }

    // ------------------------------------------------------------------------------------------------ state and Demo Doc reads

    public async Task<IResult> StateAsync(Guid sessionId, CancellationToken ct)
    {
        if (await sessions.ReadAsync(sessionId, ct) is null) return DocsProblem.NotFound();
        var target = await db.Targets.AsNoTracking().FirstOrDefaultAsync(t => t.SessionId == sessionId && t.Provider == DocsConstants.Provider, ct);
        var last = await db.Exports.AsNoTracking().Where(e => e.SessionId == sessionId).OrderByDescending(e => e.CompletedAt ?? e.CreatedAt).ThenByDescending(e => e.Id).FirstOrDefaultAsync(ct);
        return Results.Json(new StateDto(sessionId.ToString("D"), target is null ? null : DocRefDto.Of(target.DocId, target.Version), last is null ? null : ExportRecordDto.Of(last)), JsonBody.Out);
    }

    public async Task<IResult> ReadDocAsync(Guid docId, CancellationToken ct)
    {
        var d = await db.Docs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == docId, ct);
        if (d is null) return DocsProblem.NotFound();
        var blocks = DemoDocsProvider.ReadBlocks(d.Blocks);
        return Results.Json(new DemoDocDto(WireId.Format(DocsConstants.DocPrefix, d.Id), DocsConstants.Provider, true, DocumentDto.Of(d.Title, blocks), d.Version, d.CreatedAt, d.UpdatedAt), JsonBody.Out);
    }

    // ------------------------------------------------------------------------------------------------ retry wrapper

    private static PostgresException? Pg(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException) if (e is PostgresException p) return p;
        return null;
    }

    private static readonly HashSet<string> SessionForeignKeys = ["fk_demo_docs_session_user", "fk_doc_exports_session_user", "fk_doc_targets_session_user"];

    /// <summary>23503 from one of the three session FKs only: the lecture vanished mid-write (DXD-14). Any other 23503 is a server bug and stays an exception.</summary>
    internal static bool IsSessionGone(Exception ex) => Pg(ex) is { SqlState: PgErrors.ForeignKeyViolation, ConstraintName: { } c } && SessionForeignKeys.Contains(c);

    /// <summary>23505 on the idempotency key: another request took the key between our lookup and insert (DXD-15). The whole attempt is repeated; the repeat compares fingerprints.</summary>
    internal static bool IsKeyConflict(Exception ex) => Pg(ex) is { SqlState: "23505", ConstraintName: "ux_docs_idempotency_keys_scope" };

    private async Task<IResult> WithRetryAsync(Func<Task<IResult>> attempt, string what)
    {
        for (var repeat = 0; ; repeat++)
        {
            try { return await attempt(); }
            catch (Exception ex) when (IsSessionGone(ex)) { db.ChangeTracker.Clear(); return DocsProblem.NotFound(); }
            catch (Exception ex) when (PgErrors.IsDeadlockOrSerialization(ex) || IsKeyConflict(ex))
            {
                db.ChangeTracker.Clear();
                if (repeat >= opt.DeadlockRepeats)
                {
                    log.LogWarning("Docs {Operation} gave up after {Repeats} repeats ({ExceptionType})", what, repeat, Pg(ex)?.SqlState ?? "unknown");
                    return DocsProblem.Unavailable();
                }
                log.LogInformation("Docs {Operation} repeats the whole attempt ({State})", what, Pg(ex)?.SqlState ?? "unknown");
                await Task.Delay(Random.Shared.Next(5, 40 * (repeat + 1)));   // jitter
            }
        }
    }

    /// <summary>Starts the attempt's transaction and takes locks (1) to (3). Null session = the lecture is gone.</summary>
    private async Task<(IDbContextTransaction Tx, DocsSessionFacts? Facts)> LockAsync(Guid sessionId, CancellationToken ct)
    {
        var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var facts = await sessions.LockAndReadAsync(tx.GetDbTransaction(), sessionId, ct);                                  // (1) session row FOR KEY SHARE
            if (facts is null) return (tx, null);
            var sql = new Sql(db);
            await sql.ExecAsync("SELECT pg_advisory_xact_lock(hashtextextended(@p0, 0))", ct, UserSessionLock(Me, sessionId));  // (2) (user, session)
            await sql.ExecAsync("SELECT pg_advisory_xact_lock(hashtextextended(@p0, 0))", ct, UserLock(Me));                    // (3) the user, always after (2)
            return (tx, facts);
        }
        catch { await tx.DisposeAsync(); throw; }
    }

    // ------------------------------------------------------------------------------------------------ confirm

    public Task<IResult> ConfirmAsync(Guid sessionId, string idempotencyKey, ConfirmRequest req, CancellationToken ct) =>
        WithRetryAsync(() => ConfirmOnceAsync(sessionId, idempotencyKey, req, ct), "confirm");

    private async Task<IResult> ConfirmOnceAsync(Guid sessionId, string key, ConfirmRequest req, CancellationToken ct)
    {
        var me = Me;
        // Read before the transaction: the notes reader uses its own connection, and a connection must never be held while waiting for another.
        var snapshot = await notes.FindCurrentAsync(sessionId, ct);
        var (tx, facts) = await LockAsync(sessionId, ct);
        await using var _ = tx;
        if (facts is null) return DocsProblem.NotFound();
        var sql = new Sql(db);
        var now = Now();

        // ---- the Idempotency-Key, looked up under the lock so a second request for this lecture sees the winner ----
        var keyHash = Hashing.Sha256(key);
        var material = Hashing.Material("POST", "/sessions/" + sessionId.ToString("D") + "/docs-export", req.Canonical());
        await sql.ExecAsync("DELETE FROM docs.idempotency_keys WHERE user_id = @p0 AND created_at < @p1",
            ct, me, now - TimeSpan.FromHours(opt.KeyHours));    // this user's keys older than 24 h: an expired key is a new key, and the table stays bounded
        var rec = await db.IdempotencyKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Method == "POST" && k.RouteTemplate == Routes.ConfirmTemplate && k.KeyHash == keyHash, ct);
        DocExport? prior = null;
        if (rec is not null)
        {
            if (!keys.IdempotencyRing.Any(k => CryptographicOperations.FixedTimeEquals(Hashing.Hmac(k, material), rec.FingerprintHmac)))
                return DocsProblem.Invalid("Idempotency-Key", "reused");     // same key, different body or lecture: never the other request's record
            prior = await db.Exports.FirstOrDefaultAsync(e => e.Id == rec.ExportId, ct)
                ?? throw new InvalidOperationException("An idempotency record names an export that does not exist.");
            if (prior.Status == ExportStatus.Succeeded)
                return new ReplayResult(ExportRecordDto.Of(prior));            // identical to the first answer, marked as a replay
            if (prior.Attempt >= DocsConstants.MaxAttempts) return DocsProblem.RetryLimit();
        }

        // ---- step 9: state and preconditions (all of them inside the locks; a 4xx here stores nothing) ----
        if (Prepare(facts, snapshot, req.NotesId, out var p) is { } problem) return problem;
        if (!ContentDigest.Equal(req.ContentDigest, p!.Digest)) return DocsProblem.PreconditionFailed();

        var target = await db.Targets.FirstOrDefaultAsync(t => t.SessionId == sessionId && t.Provider == DocsConstants.Provider, ct);
        var expectedOk = target is null
            ? req.ExpectedVersion is null && req.ExpectedDocId is null
            : req.ExpectedDocId == target.DocId && req.ExpectedVersion == target.Version;       // the DOCUMENT the student reviewed, not only its version (DXD-05)
        if (!expectedOk) return DocsProblem.PreconditionFailed();

        var blocked = p.Built.TooLarge || p.Built.NothingToExport;
        var upToDate = target is not null && ContentDigest.Equal(target.LastDigest, p.Digest);
        var unchanged = !blocked && req.Mode == "update" && upToDate;          // same content, same document: nothing is written
        if (!unchanged && !Actions(blocked, target, upToDate).Contains(req.Mode)) return DocsProblem.InvalidState();
        if (!unchanged && req.Mode != "update" && await db.Docs.CountAsync(ct) >= opt.MaxDocsPerUser) return DocsProblem.InvalidState();   // under the user lock: race-free (DXD-16)

        // ---- writes, in the fixed order: doc_exports, idempotency_keys, demo_docs (in the provider), doc_targets, audit_events ----
        var attempt = prior is null ? 1 : prior.Attempt + 1;
        var notesGuid = Guid.ParseExact(p.Notes.NotesId[3..], "N");
        var exp = prior ?? new DocExport { Id = Ids.New(), UserId = me, SessionId = sessionId, CreatedAt = now };
        exp.NotesId = notesGuid; exp.Provider = DocsConstants.Provider; exp.Mode = req.Mode; exp.ContentDigest = p.Digest; exp.Attempt = attempt;
        exp.Status = ExportStatus.Succeeded; exp.Outcome = unchanged ? "unchanged" : req.Mode == "update" ? "updated" : "created";   // provisional; final below
        exp.DocId = null; exp.DocVersion = null; exp.ErrorCode = null; exp.Retryable = null; exp.RetryAfterSeconds = null; exp.CompletedAt = null;
        if (prior is null) db.Exports.Add(exp);
        await db.SaveChangesAsync(ct);                                                              // doc_exports first (the key references it)
        if (prior is null)
        {
            db.IdempotencyKeys.Add(new DocIdempotencyKey
            {
                UserId = me, Method = "POST", RouteTemplate = Routes.ConfirmTemplate, KeyHash = keyHash,
                FingerprintHmac = Hashing.Hmac(keys.IdempotencyRing[0], material), ExportId = exp.Id, CreatedAt = now,
            });
            await db.SaveChangesAsync(ct);                                                          // then idempotency_keys
        }

        ProviderResult<DocRef>? result = null;
        if (!unchanged)
        {
            var write = new DocsWriteRequest(me, exp.Id, p.Built.Document);
            await tx.CreateSavepointAsync("docs_provider", ct);                                     // a failed provider call undoes only the document change
            result = req.Mode == "update"
                ? await provider.UpdateAsync(new DocRef(target!.DocId, target.Version), target.Version, write, ct)
                : await provider.CreateAsync(write, ct);
            if (result.Succeeded) await tx.ReleaseSavepointAsync("docs_provider", ct);
            else await tx.RollbackToSavepointAsync("docs_provider", ct);
        }

        string action;
        if (result is { Succeeded: false, Failure: { } failure })
        {
            exp.Status = ExportStatus.Failed; exp.Outcome = "failed";
            exp.ErrorCode = failure.Code; exp.Retryable = failure.Retryable; exp.RetryAfterSeconds = failure.RetryAfterSeconds is { } s ? Math.Clamp(s, 1, 3600) : null;
            if (req.Mode == "update" && target is not null) { exp.DocId = target.DocId; exp.DocVersion = target.Version; }   // a failed update keeps the existing document at its unchanged version
            action = AuditActions.Failed;
        }
        else if (unchanged)
        {
            exp.DocId = target!.DocId; exp.DocVersion = target.Version;
            action = AuditActions.Unchanged;
        }
        else
        {
            var docRef = result!.Value!;
            exp.DocId = docRef.DocId; exp.DocVersion = docRef.Version;
            action = req.Mode == "update" ? AuditActions.Updated : AuditActions.Created;
        }
        exp.CompletedAt = now;
        await db.SaveChangesAsync(ct);

        if (exp.Status == ExportStatus.Succeeded && !unchanged)
        {
            if (target is null)
                db.Targets.Add(new DocTarget { UserId = me, SessionId = sessionId, Provider = DocsConstants.Provider, DocId = exp.DocId!.Value, LastDigest = p.Digest, Version = exp.DocVersion!.Value, LastExportId = exp.Id, UpdatedAt = now });
            else
            {
                target.DocId = exp.DocId!.Value; target.Version = exp.DocVersion!.Value; target.LastDigest = p.Digest; target.LastExportId = exp.Id; target.UpdatedAt = now;
            }
            await db.SaveChangesAsync(ct);                                                          // then doc_targets
        }

        db.Audit.Add(new DocAuditEvent
        {
            Id = Ids.New(), UserId = me, SessionId = sessionId, ExportId = exp.Id, Action = action, Provider = DocsConstants.Provider, ErrorCode = exp.ErrorCode,
            NotesRevision = p.Notes.Revision, BlockCount = p.Built.Document.Blocks.Count, Attempt = attempt, At = now,
        });
        await db.SaveChangesAsync(ct);                                                              // then audit_events (content free)
        await tx.CommitAsync(ct);
        var blockCount = p.Built.Document.Blocks.Count;
        log.LogInformation("Docs export {Action} attempt {Attempt} count {Count}", action, attempt, blockCount);
        return Results.Json(ExportRecordDto.Of(exp), JsonBody.Out);
    }

    /// <summary>A 200 body that is a replay: the stored record, with <c>Idempotency-Replayed: true</c>.</summary>
    private sealed class ReplayResult(ExportRecordDto record) : IResult
    {
        public Task ExecuteAsync(HttpContext http)
        {
            http.Response.Headers["Idempotency-Replayed"] = "true";
            return Results.Json(record, JsonBody.Out).ExecuteAsync(http);
        }
    }

    // ------------------------------------------------------------------------------------------------ student delete of a Demo Doc

    public Task<IResult> DeleteDocAsync(Guid docId, CancellationToken ct) => WithRetryAsync(() => DeleteDocOnceAsync(docId, ct), "delete");

    private async Task<IResult> DeleteDocOnceAsync(Guid docId, CancellationToken ct)
    {
        var me = Me;
        var found = await db.Docs.AsNoTracking().Where(d => d.Id == docId).Select(d => new { d.SessionId }).FirstOrDefaultAsync(ct);
        if (found is null) return DocsProblem.NotFound();           // foreign, missing and deleted look the same
        var sessionId = found.SessionId;
        var (tx, facts) = await LockAsync(sessionId, ct);           // the same lock order as confirm
        await using var _ = tx;
        if (facts is null) return DocsProblem.NotFound();           // the lecture went; the cascade took the document
        var result = await provider.DeleteAsync(new DocRef(docId, 0), ct);
        if (!result.Succeeded) return DocsProblem.NotFound();       // deleted by another request while we waited
        var sql = new Sql(db);
        await sql.ExecAsync("DELETE FROM docs.doc_targets WHERE user_id = @p0 AND session_id = @p1 AND provider = @p2 AND doc_id = @p3", ct, me, sessionId, DocsConstants.Provider, docId);
        db.Audit.Add(new DocAuditEvent
        {
            Id = Ids.New(), UserId = me, SessionId = sessionId, ExportId = null, Action = AuditActions.DocDeleted, Provider = DocsConstants.Provider,
            NotesRevision = null, BlockCount = 0, Attempt = 1, At = Now(),
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.LogInformation("Docs demo document deleted by the student");
        return Results.NoContent();
    }
}
