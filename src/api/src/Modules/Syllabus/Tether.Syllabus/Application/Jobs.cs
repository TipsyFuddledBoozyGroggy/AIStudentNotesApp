using System.Globalization;
using System.Text.Json;
using Tether.SharedKernel;
using Tether.Syllabus.Domain;
using Tether.Syllabus.Extraction;
using Tether.Syllabus.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Tether.Syllabus.Application;

/// <summary>
/// The extraction job runner (the "orchestrator" of the mock slice). It claims queued jobs with FOR UPDATE SKIP LOCKED, holds a lease with a
/// heartbeat, runs the deterministic mock extractor and persists the result in ONE transaction that re-checks the syllabus still exists
/// (a late result can never recreate deleted data, C-21). A crashed worker's lease expires and the reaper re-queues the job, so the work is
/// resumable; nothing is written before the persistence step, so a retry cannot duplicate items. It runs as a system actor: it learns the user
/// from the job row and runs everything else inside that user's scope (no query-filter bypass anywhere).
/// </summary>
internal sealed class ExtractionRunner(IServiceScopeFactory scopes, SyllabusOptions opt, TimeProvider clock, ISyllabusExtractor extractor,
    JobCancellation cancels, ILogger<ExtractionRunner> log)
{
    private readonly string _owner = "w-" + Guid.NewGuid().ToString("N")[..12];

    private DateTimeOffset Now() { var t = clock.GetUtcNow(); return new DateTimeOffset(t.Ticks - t.Ticks % 10, TimeSpan.Zero); }

    /// <summary>The claim a worker holds on a job. B1-13: every write is fenced by owner AND attempt, so a worker whose lease was reaped and re-claimed writes nothing.</summary>
    private sealed record Lease(Guid JobId, string Owner, int Attempt);

    /// <summary>Locks the job row (after the syllabus row: the one lock order) and reports whether this worker still holds it.</summary>
    private static async Task<bool> HoldsLeaseAsync(SyllabusCore core, Lease lease, CancellationToken ct) =>
        await core.Sql.ScalarAsync<int?>("""
            SELECT 1 FROM syllabus.extraction_jobs WHERE id = @p0 AND user_id = @p1 AND state = 'running' AND lease_owner = @p2 AND attempt = @p3 FOR UPDATE
            """, ct, lease.JobId, core.Uid, lease.Owner, lease.Attempt) is not null;

    /// <summary>Processes at most one queued job. Returns true if a job was claimed.</summary>
    public async Task<bool> RunOnceAsync(CancellationToken stop)
    {
        await ReapAsync(stop);
        (Guid JobId, Guid UserId, Guid SyllabusId, int Attempt)? claimed;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SyllabusDbContext>();
            var sql = new Sql(db);
            var now = Now();
            await using var tx = await db.Database.BeginTransactionAsync(stop);
            var rows = await sql.RowsAsync("""
                UPDATE syllabus.extraction_jobs SET state = 'running', lease_owner = @p0, lease_expires_at = @p1, heartbeat_at = @p2,
                    started_at = COALESCE(started_at, @p2), attempt = attempt + 1, stage = 'extracting'
                WHERE id = (SELECT id FROM syllabus.extraction_jobs WHERE state = 'queued' AND available_at <= @p2 ORDER BY available_at FOR UPDATE SKIP LOCKED LIMIT 1)
                RETURNING id, user_id, syllabus_id, attempt
                """, stop, _owner, now.AddSeconds(opt.LeaseSeconds), now);
            await tx.CommitAsync(stop);
            claimed = rows.Count == 0 ? null : ((Guid)rows[0][0]!, (Guid)rows[0][1]!, (Guid)rows[0][2]!, Convert.ToInt32(rows[0][3]));
        }
        if (claimed is not { } c) return false;
        await ProcessAsync(new Lease(c.JobId, _owner, c.Attempt), c.UserId, c.SyllabusId, stop);
        return true;
    }

    private async Task ProcessAsync(Lease lease, Guid userId, Guid syllabusId, CancellationToken stop)
    {
        await using var scope = scopes.CreateUserScope(userId);
        var core = scope.ServiceProvider.GetRequiredService<SyllabusCore>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
        cancels.Register(syllabusId, cts);
        var ct = cts.Token;
        try
        {
            var syl = await core.Db.Syllabi.AsNoTracking().FirstOrDefaultAsync(s => s.Id == syllabusId, ct);
            if (syl is null) return;                                                   // deleted: nothing to do, nothing written
            if (!await SetStageAsync(core, lease, syllabusId, "extracting", ct)) return;
            await Task.Delay(opt.MockStageDelayMilliseconds, ct);
            if (syl.InputKind == "upload" && syl.FileType is "pdf" or "docx")
            {
                // No parser exists in this increment (real DOCX/PDF parsing and the sandboxed parser worker are P2-B3). The bytes were hashed and
                // discarded at intake, so the contract-conformant outcome is a failed syllabus with the existing reason "disabled".
                await FailAsync(core, lease, syllabusId, "disabled", ct);
                return;
            }
            var text = await core.Db.CanonicalTexts.AsNoTracking().FirstOrDefaultAsync(t => t.SyllabusId == syllabusId, ct);
            if (text is null) { await FailAsync(core, lease, syllabusId, "unreadable_file", ct); return; }
            if (!await SetStageAsync(core, lease, syllabusId, "proposing", ct)) return;
            await Task.Delay(opt.MockStageDelayMilliseconds, ct);
            var input = new ExtractorInput(text.Text, syl.TermStart, syl.TermEnd, syl.DefaultTimezone, syl.HiddenContentDropped, syl.InjectionSuspect);
            var output = extractor.Extract(input);
            await PersistAsync(core, lease, syl, input, output, ct);
        }
        catch (OperationCanceledException) { /* deleted while running, or shutting down: the lease expiry re-queues a shutdown case */ }
        catch (Exception ex)
        {
            log.LogWarning("Syllabus extraction failed with {ExceptionType}", ex.GetType().Name);      // type only, never text
            try { core.Db.ChangeTracker.Clear(); await FailAsync(core, lease, syllabusId, "invalid_output", CancellationToken.None); }
            catch (Exception ex2) { log.LogWarning("Syllabus failure handling failed with {ExceptionType}", ex2.GetType().Name); }
        }
        finally { cancels.Unregister(syllabusId); }
    }

    /// <summary>Moves the product status and the job stage together under the syllabus row lock; false if the syllabus is gone.</summary>
    private async Task<bool> SetStageAsync(SyllabusCore core, Lease lease, Guid syllabusId, string stage, CancellationToken ct)
    {
        var now = Now();
        await using var tx = await core.Db.Database.BeginTransactionAsync(ct);
        if (!await core.LockSyllabusAsync(syllabusId, ct) || !await HoldsLeaseAsync(core, lease, ct)) { await tx.RollbackAsync(ct); return false; }
        await core.Sql.ExecAsync("UPDATE syllabus.syllabi SET status = @p0, updated_at = @p1, purge_after = NULL WHERE id = @p2 AND user_id = @p3", ct, stage, now, syllabusId, core.Uid);
        await core.Sql.ExecAsync("""
            UPDATE syllabus.extraction_jobs SET stage = @p0, heartbeat_at = @p1, lease_expires_at = @p2 WHERE id = @p3 AND user_id = @p4
            """, ct, stage, now, now.AddSeconds(opt.LeaseSeconds), lease.JobId, core.Uid);
        await tx.CommitAsync(ct);
        return true;
    }

    private async Task FailAsync(SyllabusCore core, Lease lease, Guid syllabusId, string reason, CancellationToken ct)
    {
        var now = Now();
        await using var tx = await core.Db.Database.BeginTransactionAsync(ct);
        if (!await core.LockSyllabusAsync(syllabusId, ct) || !await HoldsLeaseAsync(core, lease, ct)) { await tx.RollbackAsync(ct); return; }
        // debris: purged 7 days after the last job state change (data section 8); a failed syllabus never holds student work
        await core.Sql.ExecAsync("UPDATE syllabus.syllabi SET status = 'failed', status_reason = @p0, updated_at = @p1, purge_after = @p2 WHERE id = @p3 AND user_id = @p4",
            ct, reason, now, now.AddDays(opt.DebrisDays), syllabusId, core.Uid);
        await core.Sql.ExecAsync("""
            UPDATE syllabus.extraction_jobs SET state = 'failed', last_error_code = @p0, finished_at = @p1, lease_owner = NULL, lease_expires_at = NULL, stage = NULL
            WHERE id = @p2 AND user_id = @p3
            """, ct, reason, now, lease.JobId, core.Uid);
        await tx.CommitAsync(ct);
    }

    private async Task PersistAsync(SyllabusCore core, Lease lease, SyllabusDoc syl, ExtractorInput input, ExtractorOutput output, CancellationToken ct)
    {
        var canonical = input.Text;
        var db = core.Db; var uid = core.Uid; var now = Now();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // the DELETE handler takes this same lock: it either waits for this step or this step finds the syllabus gone and writes nothing
        if (!await core.LockSyllabusAsync(syl.Id, ct)) { await tx.RollbackAsync(ct); return; }
        // B1-13: persist only while this worker still owns the job (same owner and attempt), the syllabus is still being extracted, and nothing was persisted yet
        if (!await HoldsLeaseAsync(core, lease, ct)
            || await core.Sql.ScalarAsync<int?>("SELECT 1 FROM syllabus.syllabi WHERE id = @p0 AND user_id = @p1 AND status IN ('extracting','proposing')", ct, syl.Id, uid) is null
            || await db.Items.AsNoTracking().AnyAsync(i => i.SyllabusId == syl.Id && i.Origin == "ai", ct))
        { await tx.RollbackAsync(ct); return; }
        try
        {
            var items = new List<SyllabusItem>(); var alts = new List<ItemAlternative>(); var proposals = new List<ItemProposal>();
            // SB-02 / SBR-06: the orchestrator does not trust the extractor's field lengths. An item or unclaimed date that would overflow a
            // column is dropped and counted (droppedInvalid, items and unclaimed dates alike) instead of failing the whole syllabus with
            // invalid_output; a dropped item whose verbatim snippet fits is kept as an unclaimed date so its date stays visible.
            // SBV-01: the extractor's snippet and context text are never stored as given. Apply verifies them against the canonical text at
            // their offsets (within the text, on code-point boundaries, ordinal equal) and returns the slices it cut; a mismatch is a counted drop.
            // SBV-06: the stored context window is chosen by the server (the whole canonical line around the snippet, or a bounded window
            // of a very long line), never by the extractor, so a narrowed context cannot hide a word such as "NOT" next to a quoted date.
            // SBV-02: the overflow beyond the item and unclaimed caps is counted as well.
            // SBV-12: an item whose resolved date or recurrence is not written in its stored quote is dropped and counted the same way (its
            // quote is kept as an unclaimed date); the claimed date is never stored. This runs inside Apply, before the flags below.
            // SBV-10: the context-based flags (supersession cue, tentative language, injection, low text quality, weekday mismatch, outside
            // the term, DST gap) are re-derived by the server from the stored quote and the server-chosen context and UNIONED with the
            // extractor's flags (an extractor can add a flag, never remove one); the confidence is then lowered to what the confidence rule
            // gives for the merged flags, never raised. SBV-14: the confidence reason is always the server's (fixed vocabulary), never the
            // extractor's text. SBV-11: a policy item with a schema-forcing flag is please_check. The proposal row below keeps what the
            // extractor itself said (provenance: its flags and confidence; its reason text is not kept, it is not validated structure).
            var kept = SyllabusLimits.Apply(output, opt.MaxAiItems, canonical);
            var doc = ServerFlags.Document(input);
            var unclaimedFitting = kept.Unclaimed;
            var droppedInvalid = kept.DroppedInvalid;
            foreach (var proposal in kept.Items)
            {
                var p = ServerFlags.Rederive(proposal, input, doc);
                var id = Ids.New();
                items.Add(new SyllabusItem
                {
                    Id = id, UserId = uid, SyllabusId = syl.Id, CourseId = syl.CourseId, ItemKey = Ids2.KeyOf(syl.Id, id), Origin = "ai", Kind = p.Kind, Title = p.Title,
                    WrittenDateText = p.DateText, WrittenTimeText = p.TimeText, WrittenYear = (short?)p.Year, WrittenMonth = (short?)p.Month, WrittenDay = (short?)p.Day,
                    WrittenWeekday = p.Weekday, WhenStatus = p.WhenStatus, DateLocal = p.Date, EndDateLocal = p.EndDate, TimeLocal = p.Time, EndTimeLocal = p.EndTime,
                    AllDay = p.AllDay, Timezone = p.Timezone, TimezoneSource = p.TimezoneSource, Derivation = p.Derivation, UnknownReason = p.UnknownReason,
                    RecByDay = p.Recurrence?.ByDay, RecStartTime = p.Recurrence?.Start, RecEndTime = p.Recurrence?.End, RecFrom = p.Recurrence?.From, RecUntil = p.Recurrence?.Until,
                    RecUntilSource = p.Recurrence?.UntilSource, RecExceptions = [], Confidence = p.Confidence, ConfidenceReason = p.ConfidenceReason, Flags = [.. p.Flags],
                    SnippetStart = p.Snippet.Start, SnippetEnd = p.Snippet.End, SnippetPage = null, SnippetPrecision = p.Snippet.Precision, SnippetText = p.Snippet.Text,
                    ContextText = p.Snippet.ContextText, ContextStart = p.Snippet.ContextStart, ContextEnd = p.Snippet.ContextEnd,
                    Edited = false, Decision = "undecided", SyncState = "not_synced", Revision = 1, CreatedAt = now, UpdatedAt = now,
                });
                var ord = (short)1;
                foreach (var a in p.Alternatives.Take(3)) alts.Add(new ItemAlternative { ItemId = id, UserId = uid, Ordinal = ord++, DateLocal = a.Date, TimeLocal = a.Time });
                proposals.Add(new ItemProposal
                {
                    ItemId = id, UserId = uid, SyllabusId = syl.Id, ValidatorVersion = extractor.Version, PromptVersion = "none", SchemaVersion = "syllabus.extract.v1", Model = extractor.Model, CreatedAt = now,
                    Proposal = JsonSerializer.Serialize(new { kind = proposal.Kind, title = proposal.Title, status = proposal.WhenStatus, date = Local.DateOrNull(proposal.Date), flags = proposal.Flags, confidence = proposal.Confidence }),
                });
            }
            db.Items.AddRange(items);
            await db.SaveChangesAsync(ct);                       // parents first: the DB enforces the FK chain
            db.Alternatives.AddRange(alts); db.Proposals.AddRange(proposals);
            db.Unclaimed.AddRange(unclaimedFitting.Select(u => new UnclaimedDate
            {
                Id = Ids.New(), UserId = uid, SyllabusId = syl.Id, SnippetStart = u.Start, SnippetEnd = u.End, SnippetPage = null, SnippetPrecision = u.Precision,
                SnippetText = u.Text, ContextText = u.ContextText, ContextStart = u.ContextStart, ContextEnd = u.ContextEnd, CreatedAt = now,
            }));
            await db.SaveChangesAsync(ct);

            // SBV-10: the document-level signals the server read itself count too, so an extractor cannot clear them either
            var suspect = syl.InjectionSuspect || output.InjectionSuspect || doc.InjectionSuspect;
            var batch = !suspect && !syl.HiddenContentDropped && !output.LowTextQuality && !doc.LowTextQuality && !items.Any(i => i.Flags.Contains("low_text_quality"));
            await core.Sql.ExecAsync("""
                UPDATE syllabus.syllabi SET status = 'ready', status_reason = NULL, injection_suspect = @p0, batch_approve_allowed = @p1, dropped_invalid = @p2,
                    extractor_version = @p3, model = @p4, schema_version = 'syllabus.extract.v1', updated_at = @p5, purge_after = NULL
                WHERE id = @p6 AND user_id = @p7
                """, ct, suspect, batch, droppedInvalid, extractor.Version, extractor.Model, now, syl.Id, uid);
            await core.Sql.ExecAsync("""
                UPDATE syllabus.extraction_jobs SET state = 'succeeded', finished_at = @p0, lease_owner = NULL, lease_expires_at = NULL, stage = NULL
                WHERE id = @p1 AND user_id = @p2
                """, ct, now, lease.JobId, uid);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            await tx.RollbackAsync(ct);                          // deleted concurrently (23503): stop and write nothing
        }
    }

    /// <summary>Expired leases: re-queue (resumable) until the attempt cap, then fail the syllabus with "timeout". Set-based, explicit user ids.</summary>
    public async Task ReapAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SyllabusDbContext>();
        var sql = new Sql(db); var now = Now();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // B1-01: lock order is syllabus row first, then job row, as in DELETE, SetStage, Fail and Persist. Take the syllabus locks of every expired lease
        // in id order, then touch only jobs of the syllabi locked here (a lease that expires after this select is handled by the next pass).
        var locked = (await sql.RowsAsync("""
            SELECT id FROM syllabus.syllabi WHERE id IN (SELECT syllabus_id FROM syllabus.extraction_jobs WHERE state = 'running' AND lease_expires_at < @p0)
            ORDER BY id FOR UPDATE
            """, ct, now)).Select(r => (Guid)r[0]!).ToArray();
        if (locked.Length == 0) { await tx.CommitAsync(ct); return; }
        await sql.ExecAsync("""
            UPDATE syllabus.extraction_jobs SET state = 'queued', lease_owner = NULL, lease_expires_at = NULL, available_at = @p0
            WHERE state = 'running' AND lease_expires_at < @p0 AND attempt < @p1 AND syllabus_id = ANY(@p2)
            """, ct, now, opt.MaxAttempts, locked);
        var dead = await sql.RowsAsync("""
            UPDATE syllabus.extraction_jobs SET state = 'failed', lease_owner = NULL, lease_expires_at = NULL, finished_at = @p0, last_error_code = 'timeout', stage = NULL
            WHERE state = 'running' AND lease_expires_at < @p0 AND attempt >= @p1 AND syllabus_id = ANY(@p2) RETURNING syllabus_id, user_id
            """, ct, now, opt.MaxAttempts, locked);
        foreach (var r in dead)
            await sql.ExecAsync("""
                UPDATE syllabus.syllabi SET status = 'failed', status_reason = 'timeout', updated_at = @p0, purge_after = @p1
                WHERE id = @p2 AND user_id = @p3 AND status IN ('queued','extracting','proposing')
                """, ct, now, now.AddDays(opt.DebrisDays), (Guid)r[0]!, (Guid)r[1]!);
        await tx.CommitAsync(ct);
    }
}


/// <summary>
/// Retention purge (180 days from the last student write, 365 days absolute), the 7 day debris purge, and the content-free housekeeping
/// sweeps (idempotency keys at 24 h, finished job rows at 24 h, creation counters after 2 days). Runs as a system actor with explicit user
/// ids in every statement; deletion of debris uses the same code path as DELETE, in the owner's scope.
/// </summary>
internal sealed class SyllabusMaintenance(IServiceScopeFactory scopes, TimeProvider clock, SyllabusOptions opt, ILogger<SyllabusMaintenance> log)
{
    private DateTimeOffset Now() { var t = clock.GetUtcNow(); return new DateTimeOffset(t.Ticks - t.Ticks % 10, TimeSpan.Zero); }

    public sealed record Result(int TextsPurged, int DebrisDeleted, int KeysSwept, int JobsSwept, int CountersSwept, int AuditPurged = 0);

    /// <summary>Test seam (SB-10 race test): runs after the debris candidates are selected and before any is deleted. Null in production.</summary>
    internal Func<CancellationToken, Task>? BeforeDebrisDelete { get; set; }

    public async Task<Result> RunOnceAsync(CancellationToken ct)
    {
        var now = Now();
        List<(Guid Id, Guid User)> due, debris;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SyllabusDbContext>();
            var sql = new Sql(db);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            due = (await sql.RowsAsync("""
                SELECT id, user_id FROM syllabus.syllabi WHERE canonical_text_purged_at IS NULL AND canonical_text_purge_at <= @p0 ORDER BY canonical_text_purge_at LIMIT 100
                """, ct, now)).Select(r => ((Guid)r[0]!, (Guid)r[1]!)).ToList();
            debris = (await sql.RowsAsync("""
                SELECT s.id, s.user_id FROM syllabus.syllabi s WHERE s.purge_after <= @p0
                  AND NOT EXISTS (SELECT 1 FROM syllabus.items i WHERE i.syllabus_id = s.id AND i.user_id = s.user_id AND i.origin = 'student')
                ORDER BY s.purge_after LIMIT 100
                """, ct, now)).Select(r => ((Guid)r[0]!, (Guid)r[1]!)).ToList();
            await tx.CommitAsync(ct);
        }
        int purged = 0, deleted = 0;
        if (debris.Count > 0 && BeforeDebrisDelete is { } hook) await hook(ct);
        foreach (var (id, user) in debris)
        {
            try
            {
                await using var scope = scopes.CreateUserScope(user);
                var core = scope.ServiceProvider.GetRequiredService<SyllabusCore>();
                // SB-10: the delete re-checks "still debris" under the row lock; only a syllabus that still qualifies has its job cancelled and is deleted
                if (await SyllabusService.DeleteCoreAsync(core, id, ct, debrisDueBy: now)) { scope.ServiceProvider.GetRequiredService<JobCancellation>().Cancel(id); deleted++; }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Debris purge failed with {ExceptionType}", ex.GetType().Name); }
        }
        foreach (var (id, user) in due)
        {
            try { if (await PurgeTextAsync(id, user, now, ct)) purged++; }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Retention purge failed with {ExceptionType}", ex.GetType().Name); }
        }
        int keys, jobs, counters;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SyllabusDbContext>();
            var sql = new Sql(db);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            keys = await sql.ExecAsync("DELETE FROM syllabus.idempotency_keys WHERE expires_at <= @p0", ct, now);
            jobs = await sql.ExecAsync("DELETE FROM syllabus.extraction_jobs WHERE finished_at IS NOT NULL AND finished_at <= @p0", ct, now.AddHours(-24));
            counters = await sql.ExecAsync("DELETE FROM syllabus.creation_counters WHERE day <= @p0", ct, DateOnly.FromDateTime(now.UtcDateTime).AddDays(-2));
            await tx.CommitAsync(ct);
        }
        var auditPurged = await PurgeAuditAsync(now, ct);
        return new Result(purged, deleted, keys, jobs, counters, auditPurged);
    }

    /// <summary>
    /// B1-04: retention purge of decision_audit (content-free rows) older than <see cref="SyllabusOptions.AuditRetentionDays"/> (default 400 days: a PROPOSAL pending
    /// Product Owner approval). The append-only trigger allows a DELETE only inside a transaction that sets syllabus.evidence_purge, the switch purge_evidence uses.
    /// Bounded batches so one pass cannot hold a long lock.
    /// </summary>
    private async Task<int> PurgeAuditAsync(DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-opt.AuditRetentionDays); var total = 0;
        for (var pass = 0; pass < 10; pass++)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SyllabusDbContext>();
            var sql = new Sql(db);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await sql.ScalarAsync<string>("SELECT set_config('syllabus.evidence_purge', 'on', true)", ct);
            var n = await sql.ExecAsync("DELETE FROM syllabus.decision_audit WHERE id IN (SELECT id FROM syllabus.decision_audit WHERE occurred_at < @p0 LIMIT 5000)", ct, cutoff);
            await sql.ScalarAsync<string>("SELECT set_config('syllabus.evidence_purge', 'off', true)", ct);
            await tx.CommitAsync(ct);
            total += n;
            if (n < 5000) break;
        }
        return total;
    }

    /// <summary>Deletes the canonical text and nulls every snippet text and context (and keeps the offsets), then stamps the syllabus. One transaction.</summary>
    private async Task<bool> PurgeTextAsync(Guid id, Guid user, DateTimeOffset now, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SyllabusDbContext>();
        var sql = new Sql(db);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var locked = await sql.ScalarAsync<int?>("SELECT 1 FROM syllabus.syllabi WHERE id = @p0 AND user_id = @p1 AND canonical_text_purged_at IS NULL FOR UPDATE", ct, id, user);
        if (locked is null) { await tx.RollbackAsync(ct); return false; }
        await sql.ExecAsync("""
            UPDATE syllabus.items SET snippet_text = NULL, context_text = NULL, context_start = NULL, context_end = NULL
            WHERE syllabus_id = @p0 AND user_id = @p1 AND (snippet_text IS NOT NULL OR context_text IS NOT NULL)
            """, ct, id, user);
        await sql.ExecAsync("""
            UPDATE syllabus.unclaimed_dates SET snippet_text = NULL, context_text = NULL, context_start = NULL, context_end = NULL
            WHERE syllabus_id = @p0 AND user_id = @p1 AND (snippet_text IS NOT NULL OR context_text IS NOT NULL)
            """, ct, id, user);
        await sql.ExecAsync("DELETE FROM syllabus.canonical_texts WHERE syllabus_id = @p0 AND user_id = @p1", ct, id, user);
        await sql.ExecAsync("""
            UPDATE syllabus.syllabi SET canonical_text_purged_at = @p0, canonical_text_purge_at = NULL, batch_approve_allowed = false, updated_at = @p0
            WHERE id = @p1 AND user_id = @p2
            """, ct, now, id, user);
        await tx.CommitAsync(ct);
        return true;
    }
}

internal sealed class ExtractionWorkerService(ExtractionRunner runner, SyllabusOptions opt, ILogger<ExtractionWorkerService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!opt.WorkerEnabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var ran = await runner.RunOnceAsync(stoppingToken);
                if (!ran) await Task.Delay(opt.WorkerPollMilliseconds, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                log.LogWarning("Extraction loop failed with {ExceptionType}", ex.GetType().Name);
                try { await Task.Delay(1000, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }
}

internal sealed class MaintenanceService(SyllabusMaintenance maintenance, SyllabusOptions opt, ILogger<MaintenanceService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!opt.WorkerEnabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(opt.MaintenanceSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await maintenance.RunOnceAsync(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Syllabus maintenance failed with {ExceptionType}", ex.GetType().Name); }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}
