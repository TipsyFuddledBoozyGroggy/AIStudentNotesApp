using Tether.SharedKernel;
using Tether.Syllabus.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Tether.Syllabus.Application;

internal sealed class CalendarUnavailableException : Exception;

internal sealed record EventSpec(DateOnly? Occurrence, DateOnly StartDate, TimeOnly? StartTime, string Timezone, DateOnly EndDate, TimeOnly? EndTime, bool EndDerived, bool AllDay);

/// <summary>
/// The ONLY calendar provider: a database-only mock (decision 6, PO-6). It exists behind the interface so a real provider needs a new
/// contract (calendar.v1) and a security review, not a code swap; startup refuses any other provider name (SyllabusModule).
/// Fault injection ("unavailable") is allowed only in Development/Test and fails startup elsewhere.
/// </summary>
internal interface ICalendarProvider { string Name { get; } }

internal sealed class MockCalendar(SyllabusCore core) : ICalendarProvider
{
    public string Name => "mock";
    private bool Faulty => core.Options.CalendarFault == "unavailable";

    public async Task<int> RemoveItemEventsAsync(Guid itemId, CancellationToken ct)
    {
        if (Faulty) throw new CalendarUnavailableException();
        return await core.Sql.ExecAsync("DELETE FROM syllabus.calendar_events WHERE item_id = @p0 AND user_id = @p1", ct, itemId, core.Uid);
    }

    public bool FaultInjected => Faulty;

    private static DayOfWeek Dow(string code) => code switch
    {
        "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday,
        "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, _ => DayOfWeek.Sunday,
    };

    private static EventSpec Timed(DateOnly date, TimeOnly start, TimeOnly? endTime, DateOnly? endDate, string tz, DateOnly? occurrence)
    {
        var baseDate = endDate ?? date;
        if (endTime is { } et)
        {
            if (endDate is null && et < start) baseDate = date.AddDays(1);          // crosses midnight
            return new EventSpec(occurrence, date, start, tz, baseDate, et, false, false);
        }
        var e = start.AddMinutes(60);                                                  // default length 60 minutes (decision 7)
        if (e < start) baseDate = baseDate.AddDays(1);
        return new EventSpec(occurrence, date, start, tz, baseDate, e, true, false);
    }

    /// <summary>The events one resolved item writes: one for a single item, one per occurrence (at most the cap) for a weekly recurrence.</summary>
    public static List<EventSpec> Build(SyllabusItem it, int maxOccurrences)
    {
        var tz = it.Timezone!;
        var specs = new List<EventSpec>();
        if (it.RecByDay is not null)
        {
            var days = it.RecByDay.Select(Dow).ToHashSet(); var ex = it.RecExceptions.ToHashSet();
            for (var d = it.RecFrom!.Value; d <= it.RecUntil!.Value && specs.Count < maxOccurrences; d = d.AddDays(1))
                if (days.Contains(d.DayOfWeek) && !ex.Contains(d)) specs.Add(Timed(d, it.RecStartTime!.Value, it.RecEndTime, null, tz, d));
        }
        else if (it.AllDay) specs.Add(new EventSpec(null, it.DateLocal!.Value, null, tz, it.EndDateLocal ?? it.DateLocal.Value, null, false, true));
        else specs.Add(Timed(it.DateLocal!.Value, it.TimeLocal!.Value, it.EndTimeLocal, it.EndDateLocal, tz, null));
        return specs;
    }

    /// <summary>Idempotent upsert keyed by (user, provider, itemKey, occurrence). Returns (inserted, updated, unchanged) counts and removes stale occurrences.</summary>
    public async Task<(int Inserted, int Updated, int Unchanged, int Removed)> UpsertAsync(SyllabusItem it, List<EventSpec> specs, DateTimeOffset now, CancellationToken ct)
    {
        if (Faulty) throw new CalendarUnavailableException();
        int ins = 0, upd = 0, same = 0;
        foreach (var s in specs)
        {
            var rows = await core.Sql.RowsAsync("""
                INSERT INTO syllabus.calendar_events (id, user_id, course_id, syllabus_id, item_id, provider, item_key, occurrence_date, title, kind, all_day, start_date,
                    start_time, start_timezone, end_date, end_time, end_derived, synced_revision, synced_at, created_at, updated_at)
                VALUES (@p0, @p1, @p2, @p3, @p4, 'mock', @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p17, @p17)
                ON CONFLICT ON CONSTRAINT ux_calendar_events_idem DO UPDATE SET title = EXCLUDED.title, kind = EXCLUDED.kind, all_day = EXCLUDED.all_day,
                    start_date = EXCLUDED.start_date, start_time = EXCLUDED.start_time, start_timezone = EXCLUDED.start_timezone, end_date = EXCLUDED.end_date,
                    end_time = EXCLUDED.end_time, end_derived = EXCLUDED.end_derived, synced_revision = EXCLUDED.synced_revision, synced_at = EXCLUDED.synced_at,
                    updated_at = EXCLUDED.updated_at
                WHERE (calendar_events.title, calendar_events.kind, calendar_events.all_day, calendar_events.start_date, calendar_events.start_time,
                       calendar_events.start_timezone, calendar_events.end_date, calendar_events.end_time, calendar_events.end_derived)
                  IS DISTINCT FROM (EXCLUDED.title, EXCLUDED.kind, EXCLUDED.all_day, EXCLUDED.start_date, EXCLUDED.start_time,
                       EXCLUDED.start_timezone, EXCLUDED.end_date, EXCLUDED.end_time, EXCLUDED.end_derived)
                RETURNING (xmax = 0) AS inserted
                """, ct, Ids.New(), core.Uid, it.CourseId, it.SyllabusId, it.Id, it.ItemKey.Trim(), s.Occurrence, it.Title, it.Kind, s.AllDay, s.StartDate, s.StartTime,
                s.Timezone, s.EndDate, s.EndTime, s.EndDerived, it.Revision, now);
            if (rows.Count == 0) same++; else if ((bool)rows[0][0]!) ins++; else upd++;
        }
        // keep the recorded revision current without changing the outcome
        await core.Sql.ExecAsync("UPDATE syllabus.calendar_events SET synced_revision = @p0 WHERE item_id = @p1 AND user_id = @p2 AND synced_revision <> @p0", ct, it.Revision, it.Id, core.Uid);
        // occurrences that no longer exist (the recurrence shrank) are removed
        var keep = specs.Where(s => s.Occurrence is not null).Select(s => s.Occurrence!.Value).ToArray();
        var removed = await core.Sql.ExecAsync(
            "DELETE FROM syllabus.calendar_events WHERE item_id = @p0 AND user_id = @p1 AND occurrence_date IS NOT NULL AND NOT (occurrence_date = ANY(@p2))", ct, it.Id, core.Uid, keep);
        if (it.RecByDay is null)
            removed += await core.Sql.ExecAsync("DELETE FROM syllabus.calendar_events WHERE item_id = @p0 AND user_id = @p1 AND occurrence_date IS NOT NULL", ct, it.Id, core.Uid);
        return (ins, upd, same, removed);
    }
}

/// <summary>Mock calendar sync, remove and the demo-calendar read.</summary>
internal sealed class SyncService(SyllabusCore core, SyllabusService syllabi, MockCalendar calendar, RateGate rates)
{
    private static string OverallStatus(int okChanged, int unchanged, int problems)
    {
        var successes = okChanged + unchanged;
        if (problems == 0) return okChanged == 0 ? "nothing_to_do" : "all_succeeded";
        return successes == 0 ? "failed" : "partial";
    }

    private static string NotEligibleCode(SyllabusItem it) =>
        it.Kind == "policy" || it.WhenStatus == "not_applicable" ? "item_not_syncable" : it.WhenStatus != "resolved" ? "item_needs_date" : "item_not_approved";

    // ---------------------------------------------------------------- sync

    public async Task<IResult> SyncAsync(HttpContext http, Guid syllabusId, string idemKey, SyncRequest req, Dictionary<Guid, int> revs, CancellationToken ct)
    {
        var db = core.Db; var uid = core.Uid; var o = core.Options;
        var syl0 = await core.FindSyllabusAsync(syllabusId, ct);
        if (syl0 is null) return SyllabusProblem.NotFound();
        var hit = await syllabi.LookupKeyAsync(Routes.SyncTemplate, idemKey, ct);
        if (!rates.TryAcquire("sync:syl:" + syllabusId, o.SyncPerSyllabusPerMinute) || !rates.TryAcquire("sync:usr:" + uid, o.SyncPerUserPerMinute))
            return SyllabusProblem.RateLimited();       // limits also apply to candidate replays
        if (req.Confirm.Provider != "mock" || !req.Confirm.AcknowledgedDemo) return SyllabusProblem.Invalid("confirm");
        var material = SyllabusService.Material("POST", Routes.SyncTemplate, "/syllabi/" + Ids2.Syl(syllabusId) + "/calendar-sync",
            string.Join(",", revs.OrderBy(k => k.Key).Select(k => $"{Ids2.Itm(k.Key)}:{k.Value}")), "mock", "1");
        if (hit is not null)
        {
            if (!syllabi.FingerprintMatches(hit.Record, material) || hit.Record.ResultId is not { } runId) return SyllabusProblem.InvalidBare();
            http.Response.Headers["Idempotency-Replayed"] = "true";
            return await ReplayAsync(syllabusId, runId, ct) ?? SyllabusProblem.NotFound();
        }
        if (syl0.Status is not ("ready" or "partial")) return SyllabusProblem.InvalidState();

        var now = core.Now();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (!await core.LockSyllabusAsync(syllabusId, ct)) { await tx.RollbackAsync(ct); return SyllabusProblem.NotFound(); }
            // B1-06: an expired record for this key is removed here (under the lock) so the insert below cannot hit the unique index
            await core.Sql.ExecAsync("DELETE FROM syllabus.idempotency_keys WHERE user_id = @p0 AND method = 'POST' AND route_template = @p1 AND key_hash = @p2 AND expires_at <= @p3",
                ct, uid, Routes.SyncTemplate, Hashing.Sha256(idemKey), now);
            var ids = revs.Keys.ToArray();
            // lock the item rows in the defined processing order (sort date, then id) before checking anything
            var found = (await core.Sql.RowsAsync("SELECT id FROM syllabus.items WHERE syllabus_id = @p0 AND user_id = @p1 AND id = ANY(@p2) ORDER BY sort_date, id FOR UPDATE", ct, syllabusId, uid, ids))
                .Select(r => (Guid)r[0]!).ToList();
            var items = await db.Items.Where(i => found.Contains(i.Id)).ToListAsync(ct);   // read AFTER the lock: the latest committed rows
            var byId = items.ToDictionary(i => i.Id);
            var order = found.Concat(ids.Except(found).Order()).ToList();

            await core.Sql.ExecAsync("SELECT pg_advisory_xact_lock(hashtextextended(@p0, 0))", ct, "syllabus.events:" + uid);
            var sylEvents = await core.Sql.ScalarAsync<long>("SELECT count(*) FROM syllabus.calendar_events WHERE syllabus_id = @p0 AND user_id = @p1", ct, syllabusId, uid);
            var userEvents = await core.Sql.ScalarAsync<long>("SELECT count(*) FROM syllabus.calendar_events WHERE user_id = @p0", ct, uid);
            var written = 0;
            var results = new List<SyncResultItem>(); var runItems = new List<SyncRunItem>();
            int created = 0, updated = 0, unchanged = 0, failed = 0, notEligible = 0;
            var runId = Ids.New();

            void Add(Guid id, string key, string outcome, string? code, int count, string syncState)
            {
                results.Add(new SyncResultItem(Ids2.Itm(id), key, outcome, code, count, syncState));
                runItems.Add(new SyncRunItem { RunId = runId, ItemId = id, UserId = uid, Outcome = outcome, ErrorCode = code, Occurrences = count });
                switch (outcome) { case "created": created++; break; case "updated": updated++; break; case "unchanged": unchanged++; break; case "failed": failed++; break; default: notEligible++; break; }
            }

            foreach (var id in order)
            {
                var key = Ids2.KeyOf(syllabusId, id);
                if (!byId.TryGetValue(id, out var it)) { Add(id, key, "not_eligible", "not_found", 0, "not_synced"); continue; }
                // re-check on the LOCKED row, never on a value read earlier
                if (it.Kind == "policy" || it.WhenStatus != "resolved" || it.Decision != "approved") { Add(id, key, "not_eligible", NotEligibleCode(it), 0, it.SyncState); continue; }
                if (it.Revision != revs[id]) { Add(id, key, "failed", "precondition_failed", 0, it.SyncState); continue; }
                var specs = MockCalendar.Build(it, o.MaxOccurrencesPerItem);
                var existing = await core.Sql.ScalarAsync<long>("SELECT count(*) FROM syllabus.calendar_events WHERE item_id = @p0 AND user_id = @p1", ct, id, uid);
                if (sylEvents - existing + specs.Count > o.MaxEventsPerSyllabus || userEvents - existing + specs.Count > o.MaxEventsPerUser || written + specs.Count > o.MaxEventsPerCall)
                {
                    it.SyncState = "sync_failed"; it.UpdatedAt = now;
                    Add(id, key, "failed", "event_limit_reached", 0, it.SyncState); continue;      // nothing is written for this item
                }
                try
                {
                    var r = await calendar.UpsertAsync(it, specs, now, ct);
                    sylEvents += specs.Count - existing; userEvents += specs.Count - existing;
                    written += r.Inserted + r.Updated;
                    var outcome = existing == 0 ? "created" : (r.Inserted + r.Updated + r.Removed > 0 ? "updated" : "unchanged");
                    var stateChanged = it.SyncState != "synced" || it.SyncedRevision != it.Revision;
                    it.SyncState = "synced"; it.SyncedRevision = it.Revision;
                    if (stateChanged) it.UpdatedAt = now;
                    Add(id, key, outcome, null, specs.Count, it.SyncState);
                }
                catch (CalendarUnavailableException)
                {
                    it.SyncState = "sync_failed"; it.UpdatedAt = now;                  // synced_revision keeps its last written value (null if never synced)
                    Add(id, key, "failed", "provider_unavailable", 0, it.SyncState);
                }
            }
            var status = OverallStatus(created + updated, unchanged, failed + notEligible);
            db.SyncRuns.Add(new SyncRun { Id = runId, UserId = uid, SyllabusId = syllabusId, Provider = "mock", Kind = "sync", Status = status, Requested = ids.Length, CreatedAt = now });
            await db.SaveChangesAsync(ct);
            db.SyncRunItems.AddRange(runItems);
            db.IdempotencyKeys.Add(new IdempotencyRecord
            {
                Id = Ids.New(), UserId = uid, CourseId = syl0.CourseId, SyllabusId = syllabusId, Method = "POST", RouteTemplate = Routes.SyncTemplate,
                KeyHash = Hashing.Sha256(idemKey), Fingerprint = syllabi.Fingerprint(material), ResultKind = "sync_run", ResultId = runId, CreatedAt = now, ExpiresAt = now.AddHours(24),
            });
            await core.AuditNoisyAsync(syllabusId, "synced", now, created + updated + unchanged, ct);
            await core.TouchActivityAsync(syllabusId, now, ct);
            await db.SaveChangesAsync(ct);
            await PruneRunsAsync(syllabusId, ct);
            await tx.CommitAsync(ct);
            return Results.Json(new SyncResult(WireId.Format(Ids2.Sync, runId), Ids2.Syl(syllabusId), "mock", true, status, now, results, new SyncCounts(created, updated, unchanged, failed, notEligible)), JsonBody.Out);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_idempotency_keys_scope" })
        {
            await tx.RollbackAsync(ct); db.ChangeTracker.Clear();
            var winner = await syllabi.LookupKeyAsync(Routes.SyncTemplate, idemKey, ct);
            if (winner?.Record.ResultId is { } rid && syllabi.FingerprintMatches(winner.Record, material))
            {
                http.Response.Headers["Idempotency-Replayed"] = "true";
                return await ReplayAsync(syllabusId, rid, ct) ?? SyllabusProblem.NotFound();
            }
            return SyllabusProblem.InvalidBare();
        }
    }

    /// <summary>
    /// B1-04: keeps the newest <see cref="SyllabusOptions.SyncRunsPerSyllabus"/> runs of this syllabus (call inside the locked transaction, after the run rows are saved).
    /// Older runs go with their items (cascade) and with the idempotency keys that point at them, so a retry of a pruned key re-runs the idempotent sync.
    /// </summary>
    private Task PruneRunsAsync(Guid syllabusId, CancellationToken ct) => core.Sql.ExecAsync("""
        WITH old AS (SELECT id FROM syllabus.sync_runs WHERE syllabus_id = @p0 AND user_id = @p1 ORDER BY created_at DESC, id DESC OFFSET @p2),
             k AS (DELETE FROM syllabus.idempotency_keys WHERE user_id = @p1 AND result_kind = 'sync_run' AND result_id IN (SELECT id FROM old))
        DELETE FROM syllabus.sync_runs WHERE user_id = @p1 AND id IN (SELECT id FROM old)
        """, ct, syllabusId, core.Uid, core.Options.SyncRunsPerSyllabus);

    /// <summary>Rebuilds the ORIGINAL result as produced at completedAt from the sync run records. Item states may be stale by design.</summary>
    private async Task<IResult?> ReplayAsync(Guid syllabusId, Guid runId, CancellationToken ct)
    {
        var run = await core.Db.SyncRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId && r.SyllabusId == syllabusId, ct);
        if (run is null) return null;
        var rows = await core.Db.SyncRunItems.AsNoTracking().Where(r => r.RunId == runId).ToListAsync(ct);
        var current = await core.Db.Items.AsNoTracking().Where(i => rows.Select(r => r.ItemId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var ordered = rows.OrderBy(r => current.TryGetValue(r.ItemId, out var i) ? i.SortDate : DateOnly.MaxValue).ThenBy(r => r.ItemId).ToList();
        var results = ordered.Select(r => new SyncResultItem(Ids2.Itm(r.ItemId), Ids2.KeyOf(syllabusId, r.ItemId), r.Outcome, r.ErrorCode, r.Occurrences,
            r.Outcome is "created" or "updated" or "unchanged" ? "synced" : r.ErrorCode == "provider_unavailable" || r.ErrorCode == "event_limit_reached" ? "sync_failed"
                : current.TryGetValue(r.ItemId, out var c) ? c.SyncState : "not_synced")).ToList();
        return Results.Json(new SyncResult(WireId.Format(Ids2.Sync, runId), Ids2.Syl(syllabusId), "mock", true, run.Status, run.CreatedAt, results,
            new SyncCounts(results.Count(x => x.Outcome == "created"), results.Count(x => x.Outcome == "updated"), results.Count(x => x.Outcome == "unchanged"),
                results.Count(x => x.Outcome == "failed"), results.Count(x => x.Outcome == "not_eligible"))), JsonBody.Out);
    }

    // ---------------------------------------------------------------- remove

    public async Task<IResult> RemoveAsync(Guid syllabusId, RemoveRequest req, List<Guid> ids, CancellationToken ct)
    {
        var db = core.Db; var uid = core.Uid;
        var syl0 = await core.FindSyllabusAsync(syllabusId, ct);
        if (syl0 is null) return SyllabusProblem.NotFound();
        if (syl0.Status is not ("ready" or "partial")) return SyllabusProblem.InvalidState();
        var now = core.Now();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await core.LockSyllabusAsync(syllabusId, ct)) { await tx.RollbackAsync(ct); return SyllabusProblem.NotFound(); }
        var found = (await core.Sql.RowsAsync("SELECT id FROM syllabus.items WHERE syllabus_id = @p0 AND user_id = @p1 AND id = ANY(@p2) ORDER BY sort_date, id FOR UPDATE", ct, syllabusId, uid, ids.ToArray()))
            .Select(r => (Guid)r[0]!).ToList();
        var items = (await db.Items.Where(i => found.Contains(i.Id)).ToListAsync(ct)).ToDictionary(i => i.Id);
        var order = found.Concat(ids.Except(found).Order()).ToList();
        var results = new List<SyncResultItem>(); var runItems = new List<SyncRunItem>();
        int removed = 0, notPresent = 0, failed = 0;
        var runId = Ids.New();
        foreach (var id in order)
        {
            var key = Ids2.KeyOf(syllabusId, id);
            if (!items.TryGetValue(id, out var it))
            {
                results.Add(new SyncResultItem(Ids2.Itm(id), key, "failed", "not_found", 0, "not_synced")); runItems.Add(new SyncRunItem { RunId = runId, ItemId = id, UserId = uid, Outcome = "failed", ErrorCode = "not_found" }); failed++; continue;
            }
            try
            {
                var n = await calendar.RemoveItemEventsAsync(id, ct);
                var hadState = it.SyncState != "not_synced";
                if (hadState) { it.SyncState = "not_synced"; it.SyncedRevision = null; it.UpdatedAt = now; }   // the decision stays approved
                var outcome = n > 0 || hadState ? "removed" : "not_present";
                if (outcome == "removed") removed++; else notPresent++;
                results.Add(new SyncResultItem(Ids2.Itm(id), key, outcome, null, n, it.SyncState)); runItems.Add(new SyncRunItem { RunId = runId, ItemId = id, UserId = uid, Outcome = outcome, Occurrences = Math.Min(n, 60) });
            }
            catch (CalendarUnavailableException)
            {
                results.Add(new SyncResultItem(Ids2.Itm(id), key, "failed", "provider_unavailable", 0, it.SyncState)); runItems.Add(new SyncRunItem { RunId = runId, ItemId = id, UserId = uid, Outcome = "failed", ErrorCode = "provider_unavailable" }); failed++;
            }
        }
        var status = OverallStatus(removed, notPresent, failed);
        db.SyncRuns.Add(new SyncRun { Id = runId, UserId = uid, SyllabusId = syllabusId, Provider = "mock", Kind = "remove", Status = status, Requested = ids.Count, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        db.SyncRunItems.AddRange(runItems);
        await core.AuditNoisyAsync(syllabusId, "sync_removed", now, removed, ct);
        await core.TouchActivityAsync(syllabusId, now, ct);
        await db.SaveChangesAsync(ct);
        await PruneRunsAsync(syllabusId, ct);
        await tx.CommitAsync(ct);
        return Results.Json(new RemoveResult(Ids2.Syl(syllabusId), "mock", status, results, new RemoveCounts(removed, notPresent, failed)), JsonBody.Out);
    }

    // ---------------------------------------------------------------- demo calendar

    public async Task<IResult> DemoCalendarAsync(Guid courseId, string? from, string? to, string? cursor, int limit, Tether.Courses.Contracts.ICourseOwnership courses, CancellationToken ct)
    {
        if (!await courses.IsOwnedByAsync(core.Uid, courseId, ct)) return SyllabusProblem.NotFound();
        if (!Local.TryDate(from, out var f)) return SyllabusProblem.Invalid("from");
        if (!Local.TryDate(to, out var t) || t < f || t.DayNumber - f.DayNumber > 400) return SyllabusProblem.Invalid("to");
        DateOnly cd = default; Guid cid = default; var hasCursor = false;
        if (cursor is not null) { if (!Cursor.TryDecode(cursor, out cd, out cid)) return SyllabusProblem.Invalid("cursor"); hasCursor = true; }
        var q = core.Db.Events.AsNoTracking().Where(e => e.CourseId == courseId && e.StartDate >= f && e.StartDate <= t);
        if (hasCursor) q = q.Where(e => e.StartDate > cd || (e.StartDate == cd && e.Id.CompareTo(cid) > 0));
        var rows = await q.OrderBy(e => e.StartDate).ThenBy(e => e.Id).Take(limit + 1).ToListAsync(ct);
        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        return Results.Json(new Page<CalendarEventDto>([.. rows.Select(e => e.ToDto())], more ? Cursor.Encode(rows[^1].StartDate, rows[^1].Id) : null), JsonBody.Out);
    }
}
