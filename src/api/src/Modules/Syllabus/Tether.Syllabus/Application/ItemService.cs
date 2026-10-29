using System.Text.Json;
using Tether.SharedKernel;
using Tether.Syllabus.Domain;
using Tether.Syllabus.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Tether.Syllabus.Application;

internal static class ItemRules
{
    public static readonly string[] EditableKinds = ["assignment", "exam", "reading", "office_hours", "other"];
    public static readonly string[] DateDerivedFlags =
        ["year_inferred", "relative_date", "weekday_mismatch", "date_not_verifiable", "outside_term", "conflicting_sources", "timezone_assumed", "until_from_term", "range_end_inferred", "dst_ambiguous"];
    public const string StudentReason = "Entered by you.";
    public const string EditedAiReason = "You changed this date, so the original wording no longer supports it.";
}

/// <summary>Items: list, get, add by hand, edit, delete and the approve / skip / undo commands (single and batch).</summary>
internal sealed class ItemService(SyllabusCore core, SyllabusService syllabi, MockCalendar calendar)
{
    // ---------------------------------------------------------------- read

    public async Task<IResult> ListAsync(Guid syllabusId, string? cursor, int limit, string? decision, string? kind, string? needsDate, CancellationToken ct)
    {
        var syl = await core.FindSyllabusAsync(syllabusId, ct);
        if (syl is null) return SyllabusProblem.NotFound();
        if (decision is not null && decision is not ("undecided" or "approved" or "skipped")) return SyllabusProblem.Invalid("decision");
        if (kind is not null && kind is not ("assignment" or "exam" or "reading" or "office_hours" or "policy" or "other")) return SyllabusProblem.Invalid("kind");
        bool? nd = null;
        if (needsDate is not null) { if (needsDate is "true") nd = true; else if (needsDate is "false") nd = false; else return SyllabusProblem.Invalid("needsDate"); }
        DateOnly cd = default; Guid cid = default; var hasCursor = false;
        if (cursor is not null) { if (!Cursor.TryDecode(cursor, out cd, out cid)) return SyllabusProblem.Invalid("cursor"); hasCursor = true; }

        var q = core.Db.Items.AsNoTracking().Where(i => i.SyllabusId == syllabusId);
        if (decision is not null) q = q.Where(i => i.Decision == decision);
        if (kind is not null) q = q.Where(i => i.Kind == kind);
        if (nd is true) q = q.Where(i => i.WhenStatus == "needs_date");
        if (nd is false) q = q.Where(i => i.WhenStatus != "needs_date");
        if (hasCursor) q = q.Where(i => i.SortDate > cd || (i.SortDate == cd && i.Id.CompareTo(cid) > 0));
        var rows = await q.OrderBy(i => i.SortDate).ThenBy(i => i.Id).Take(limit + 1).ToListAsync(ct);   // needs-date items sort last
        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        var items = await core.RepresentItemsAsync(rows, ct);
        return Results.Json(new Page<ItemResponse>(items, more ? Cursor.Encode(rows[^1].SortDate, rows[^1].Id) : null), JsonBody.Out);
    }

    public async Task<IResult> GetAsync(HttpContext http, Guid syllabusId, Guid itemId, CancellationToken ct)
    {
        if (await core.FindSyllabusAsync(syllabusId, ct) is null) return SyllabusProblem.NotFound();
        var item = await core.Db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.SyllabusId == syllabusId, ct);
        if (item is null) return SyllabusProblem.NotFound();
        http.Response.Headers.ETag = core.ETags.ForItem(item);
        return Results.Json(await core.RepresentItemAsync(item, ct), JsonBody.Out);
    }

    // ---------------------------------------------------------------- add by hand

    public async Task<IResult> CreateAsync(HttpContext http, Guid syllabusId, string? idemKey, CancellationToken ct)
    {
        var db = core.Db; var uid = core.Uid;
        var syl0 = await core.FindSyllabusAsync(syllabusId, ct);
        if (syl0 is null) return SyllabusProblem.NotFound();
        IdemHit? hit = null;
        if (idemKey is not null) hit = await syllabi.LookupKeyAsync(Routes.ItemTemplate, idemKey, ct);
        var (bytes, failure) = await JsonBody.ReadBytesAsync(http, core.Options.JsonBodyBytes, ct);
        if (failure == JsonBody.Failure.TooLarge) return SyllabusProblem.TooLarge();
        if (failure == JsonBody.Failure.Unsupported) return SyllabusProblem.Unsupported();
        if (!JsonBody.TryBind<ItemCreateRequest>(bytes!, out var req) || req is null) return SyllabusProblem.InvalidBare();
        var material = SyllabusService.Material("POST", Routes.ItemTemplate, "/syllabi/" + Ids2.Syl(syllabusId) + "/items", req.Kind, req.Title, req.Notes, req.Date, req.EndDate, req.Time, req.EndTime,
            req.AllDay is null ? null : req.AllDay.Value ? "1" : "0", req.Timezone);
        if (hit is not null) return await ReplayItemAsync(http, syllabusId, hit, material, ct);

        // validation (step 8)
        if (!ItemRules.EditableKinds.Contains(req.Kind)) return SyllabusProblem.Invalid("kind");
        if (!TextPolicy.TryValidate(req.Title, TextKind.SingleLine, 200, out var title)) return SyllabusProblem.Invalid("title");
        string? notes = null;
        if (req.Notes is not null) { if (!TextPolicy.TryValidate(req.Notes, TextKind.Notes, 500, out notes)) return SyllabusProblem.Invalid("notes"); }
        if (!Local.TryDate(req.Date, out var date)) return SyllabusProblem.Invalid("date");
        DateOnly? endDate = null; TimeOnly? time = null, endTime = null;
        if (req.EndDate is not null) { if (!Local.TryDate(req.EndDate, out var ed) || ed < date) return SyllabusProblem.Invalid("endDate"); endDate = ed; }
        if (req.Time is not null) { if (!Local.TryTime(req.Time, out var t)) return SyllabusProblem.Invalid("time"); time = t; }
        if (req.EndTime is not null) { if (!Local.TryTime(req.EndTime, out var t)) return SyllabusProblem.Invalid("endTime"); endTime = t; }
        var allDay = req.AllDay ?? false;
        if (allDay && (time is not null || endTime is not null)) return SyllabusProblem.Invalid("allDay", "conflict");
        if (endTime is not null && time is null) return SyllabusProblem.Invalid("endTime", "conflict");
        if (req.Timezone is not null && !ZoneAllowList.Contains(req.Timezone)) return SyllabusProblem.Invalid("timezone", "not_allowed");

        var now = core.Now();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await core.LockSyllabusAsync(syllabusId, ct)) { await tx.RollbackAsync(ct); return SyllabusProblem.NotFound(); }
        if (idemKey is not null)
        {
            // B1-06: decide the key under the syllabus lock. A simultaneous create with the same key has committed by now (replay it); an
            // expired record is removed so the new insert cannot hit the unique index.
            var keyHash = Hashing.Sha256(idemKey);
            await core.Sql.ExecAsync("DELETE FROM syllabus.idempotency_keys WHERE user_id = @p0 AND method = 'POST' AND route_template = @p1 AND key_hash = @p2 AND expires_at <= @p3",
                ct, uid, Routes.ItemTemplate, keyHash, now);
            var winner = await syllabi.LookupKeyAsync(Routes.ItemTemplate, idemKey, ct);
            if (winner is not null) { await tx.RollbackAsync(ct); return await ReplayItemAsync(http, syllabusId, winner, material, ct); }
        }
        var syl = await db.Syllabi.AsNoTracking().FirstAsync(s => s.Id == syllabusId, ct);
        if (syl.Status is not ("ready" or "partial")) { await tx.RollbackAsync(ct); return SyllabusProblem.InvalidState(); }   // hand entry only into ready/partial (R3-13)
        var total = await db.Items.CountAsync(i => i.SyllabusId == syllabusId, ct);
        var students = await db.Items.CountAsync(i => i.SyllabusId == syllabusId && i.Origin == "student", ct);
        if (total >= core.Options.MaxItems || students >= core.Options.MaxStudentItems) { await tx.RollbackAsync(ct); return SyllabusProblem.InvalidState(); }

        var flags = new List<string>(); var confidence = "probably_right";
        var zone = req.Timezone ?? syl.DefaultTimezone;
        if (time is not null && Local.IsInDstGap(date, time.Value, zone)) { flags.Add("dst_ambiguous"); confidence = "please_check"; }
        var id = Ids.New();
        var item = new SyllabusItem
        {
            Id = id, UserId = uid, SyllabusId = syllabusId, CourseId = syl.CourseId, ItemKey = Ids2.KeyOf(syllabusId, id), Origin = "student", Kind = req.Kind,
            Title = title, Notes = notes, WhenStatus = "resolved", DateLocal = date, EndDateLocal = endDate, TimeLocal = allDay ? null : time, EndTimeLocal = allDay ? null : endTime,
            AllDay = allDay, Timezone = zone, TimezoneSource = req.Timezone is null ? "course_default" : "student", Derivation = "student_entered",
            Confidence = confidence, ConfidenceReason = ItemRules.StudentReason, Flags = [.. flags], Edited = false, Decision = "undecided", SyncState = "not_synced",
            Revision = 1, CreatedAt = now, UpdatedAt = now,
        };
        try
        {
            db.Items.Add(item);
            await db.SaveChangesAsync(ct);
            if (idemKey is not null)
                db.IdempotencyKeys.Add(new IdempotencyRecord
                {
                    Id = Ids.New(), UserId = uid, CourseId = syl.CourseId, SyllabusId = syllabusId, Method = "POST", RouteTemplate = Routes.ItemTemplate,
                    KeyHash = Hashing.Sha256(idemKey), Fingerprint = syllabi.Fingerprint(material), ResultKind = "item", ResultId = id, CreatedAt = now, ExpiresAt = now.AddHours(24),
                });
            await core.TouchActivityAsync(syllabusId, now, ct);
            await core.Sql.ExecAsync("UPDATE syllabus.syllabi SET purge_after = NULL WHERE id = @p0 AND user_id = @p1 AND purge_after IS NOT NULL", ct, syllabusId, uid);   // never swept: it holds student work
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (idemKey is not null && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_idempotency_keys_scope" })
        {
            // the same key was committed for ANOTHER syllabus of this user between our check and our insert (the syllabus lock does not cover that)
            await tx.RollbackAsync(ct); db.ChangeTracker.Clear();
            var winner = await syllabi.LookupKeyAsync(Routes.ItemTemplate, idemKey, ct);
            return winner is not null ? await ReplayItemAsync(http, syllabusId, winner, material, ct) : SyllabusProblem.InvalidBare();
        }
        var saved = await db.Items.AsNoTracking().FirstAsync(i => i.Id == id, ct);
        http.Response.Headers.ETag = core.ETags.ForItem(saved);
        http.Response.Headers.Location = $"/api/v1/syllabi/{Ids2.Syl(syllabusId)}/items/{Ids2.Itm(id)}";
        return Results.Json(await core.RepresentItemAsync(saved, ct), JsonBody.Out, statusCode: 201);
    }

    private async Task<IResult> ReplayItemAsync(HttpContext http, Guid syllabusId, IdemHit hit, string material, CancellationToken ct)
    {
        if (!syllabi.FingerprintMatches(hit.Record, material)) return SyllabusProblem.InvalidBare();
        if (hit.Record.ResultId is not { } rid) return SyllabusProblem.NotFound();
        var existing = await core.Db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == rid && i.SyllabusId == syllabusId, ct);
        if (existing is null) return SyllabusProblem.NotFound();
        http.Response.Headers["Idempotency-Replayed"] = "true";
        http.Response.Headers.ETag = core.ETags.ForItem(existing);
        http.Response.Headers.Location = $"/api/v1/syllabi/{Ids2.Syl(syllabusId)}/items/{Ids2.Itm(existing.Id)}";
        return Results.Json(await core.RepresentItemAsync(existing, ct), JsonBody.Out, statusCode: 201);
    }

    // ---------------------------------------------------------------- shared locked mutation scaffold

    private sealed record Locked(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction Tx, SyllabusItem Item);

    /// <summary>Opens a transaction, locks the syllabus row (single lock order: syllabus then item), loads the item tracked and checks the ETag.</summary>
    private async Task<(Locked? L, IResult? Error)> OpenAsync(Guid syllabusId, Guid itemId, string ifMatch, CancellationToken ct)
    {
        var tx = await core.Db.Database.BeginTransactionAsync(ct);
        if (!await core.LockSyllabusAsync(syllabusId, ct)) { await tx.RollbackAsync(ct); return (null, SyllabusProblem.NotFound()); }
        var item = await core.Db.Items.FirstOrDefaultAsync(i => i.Id == itemId && i.SyllabusId == syllabusId, ct);
        if (item is null) { await tx.RollbackAsync(ct); return (null, SyllabusProblem.NotFound()); }
        if (!core.ETags.Matches(ifMatch, core.ETags.ForItem(item))) { await tx.RollbackAsync(ct); return (null, SyllabusProblem.PreconditionFailed()); }
        return (new Locked(tx, item), null);
    }

    // ---------------------------------------------------------------- decision (single)

    public async Task<IResult> DecideAsync(HttpContext http, Guid syllabusId, Guid itemId, string ifMatch, CancellationToken ct)
    {
        if (await core.FindSyllabusAsync(syllabusId, ct) is null || !await core.Db.Items.AsNoTracking().AnyAsync(i => i.Id == itemId && i.SyllabusId == syllabusId, ct))
            return SyllabusProblem.NotFound();
        var (bytes, failure) = await JsonBody.ReadBytesAsync(http, core.Options.JsonBodyBytes, ct);
        if (failure == JsonBody.Failure.TooLarge) return SyllabusProblem.TooLarge();
        if (failure == JsonBody.Failure.Unsupported) return SyllabusProblem.Unsupported();
        if (!JsonBody.TryBind<DecisionRequest>(bytes!, out var req) || req is null) return SyllabusProblem.InvalidBare();
        if (req.Decision is not ("undecided" or "approved" or "skipped")) return SyllabusProblem.Invalid("decision");

        var (l, err) = await OpenAsync(syllabusId, itemId, ifMatch, ct);
        if (err is not null) return err;
        await using var tx = l!.Tx;
        var syl = await core.Db.Syllabi.AsNoTracking().FirstAsync(s => s.Id == syllabusId, ct);
        var now = core.Now();
        (string Outcome, string? Code) r;
        try { r = await ApplyDecisionAsync(syl, l.Item, req.Decision, req.AcknowledgeCheck == true, batch: false, now, ct); }
        catch (CalendarUnavailableException) { await tx.RollbackAsync(ct); return SyllabusProblem.ProviderUnavailable(); }
        if (r.Outcome == "rejected") { await tx.RollbackAsync(ct); return SyllabusProblem.Conflict(r.Code!); }
        if (r.Outcome == "applied")
        {
            core.Audit(syllabusId, req.Decision switch { "approved" => "approved", "skipped" => "skipped", _ => "undecided" }, now, itemId, l.Item.Revision);
            l.Item.UpdatedAt = now;
            await core.TouchActivityAsync(syllabusId, now, ct);
            await core.Db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await RespondItemAsync(http, itemId, ct);
    }

    private async Task<IResult> RespondItemAsync(HttpContext http, Guid itemId, CancellationToken ct, int status = 200)
    {
        core.Db.ChangeTracker.Clear();
        var item = await core.Db.Items.AsNoTracking().FirstAsync(i => i.Id == itemId, ct);
        http.Response.Headers.ETag = core.ETags.ForItem(item);
        return Results.Json(await core.RepresentItemAsync(item, ct), JsonBody.Out, statusCode: status);
    }

    /// <summary>Applies one decision to a LOCKED, tracked item. Returns applied, unchanged or rejected with the item error code. Never writes a calendar.</summary>
    private async Task<(string Outcome, string? Code)> ApplyDecisionAsync(SyllabusDoc syl, SyllabusItem item, string decision, bool ack, bool batch, DateTimeOffset now, CancellationToken ct)
    {
        if (decision == "approved")
        {
            if (item.Kind == "policy" || item.WhenStatus == "not_applicable") return ("rejected", "item_not_syncable");
            if (item.WhenStatus != "resolved") return ("rejected", "item_needs_date");        // enforced inside the locked transaction, on the locked row
            if (batch)
            {
                var eligible = syl.BatchApproveAllowed && item.Confidence == "looks_right" && !item.Edited && item.SnippetText is not null;
                if (!eligible) return ("rejected", "batch_not_eligible");
            }
            else if (item.Confidence == "please_check" && !ack) return ("rejected", "acknowledgement_required");
            if (item.Decision == "approved") return ("unchanged", null);
            item.Decision = "approved";
            return ("applied", null);
        }
        if (item.Decision == decision) return ("unchanged", null);
        if (item.SyncState != "not_synced")
        {
            // Withdrawing a synced item removes its mock events in the same transaction; if that fails the whole command fails (503).
            await calendar.RemoveItemEventsAsync(item.Id, ct);
            item.SyncState = "not_synced"; item.SyncedRevision = null;
        }
        item.Decision = decision;
        return ("applied", null);
    }

    // ---------------------------------------------------------------- batch

    public async Task<IResult> BatchAsync(Guid syllabusId, BatchRequest req, Dictionary<Guid, string> etagsById, CancellationToken ct)
    {
        var db = core.Db;
        var syl = await core.FindSyllabusAsync(syllabusId, ct);
        if (syl is null) return SyllabusProblem.NotFound();
        var decision = req.Action switch { "approve" => "approved", "skip" => "skipped", _ => "undecided" };
        var present = await db.Items.AsNoTracking().Where(i => i.SyllabusId == syllabusId && etagsById.Keys.Contains(i.Id)).Select(i => new { i.Id, i.SortDate }).ToListAsync(ct);
        var known = present.ToDictionary(p => p.Id, p => p.SortDate);
        // deterministic processing order (sort date, id), never JSON member order
        var order = etagsById.Keys.OrderBy(k => known.TryGetValue(k, out var d) ? d : DateOnly.MaxValue).ThenBy(k => k).ToList();
        var results = new List<BatchResultItem>();
        int applied = 0, unchanged = 0, rejected = 0, appliedApprovals = 0;
        foreach (var id in order)
        {
            if (!known.ContainsKey(id)) { results.Add(new BatchResultItem(Ids2.Itm(id), "rejected", "not_found", null)); rejected++; continue; }
            var (l, err) = await OpenAsync(syllabusId, id, etagsById[id], ct);       // each item is its own transaction
            if (err is not null)
            {
                var code = err is SyllabusProblem { Code: "precondition_failed" } ? "precondition_failed" : "not_found";
                results.Add(new BatchResultItem(Ids2.Itm(id), "rejected", code, null)); rejected++; continue;
            }
            await using var tx = l!.Tx;
            var now = core.Now();
            try
            {
                var syl2 = await db.Syllabi.AsNoTracking().FirstAsync(s => s.Id == syllabusId, ct);
                var r = await ApplyDecisionAsync(syl2, l.Item, decision, ack: false, batch: req.Action == "approve", now, ct);
                if (r.Outcome == "rejected") { await tx.RollbackAsync(ct); db.ChangeTracker.Clear(); results.Add(new BatchResultItem(Ids2.Itm(id), "rejected", r.Code, null)); rejected++; continue; }
                if (r.Outcome == "applied")
                {
                    l.Item.UpdatedAt = now;
                    core.Audit(syllabusId, req.Action switch { "approve" => "approved", "skip" => "skipped", _ => "undecided" }, now, id, l.Item.Revision);   // B1-03: one row per item and revision, approvals included
                    await core.TouchActivityAsync(syllabusId, now, ct);
                    await db.SaveChangesAsync(ct);
                    applied++; if (req.Action == "approve") appliedApprovals++;
                }
                else unchanged++;
                await tx.CommitAsync(ct);
                db.ChangeTracker.Clear();
                var fresh = await db.Items.AsNoTracking().FirstAsync(i => i.Id == id, ct);
                results.Add(new BatchResultItem(Ids2.Itm(id), r.Outcome, null, await core.RepresentItemAsync(fresh, ct)));
            }
            catch (CalendarUnavailableException)
            {
                await tx.RollbackAsync(ct); db.ChangeTracker.Clear();
                results.Add(new BatchResultItem(Ids2.Itm(id), "rejected", "provider_unavailable", null)); rejected++;
            }
        }
        if (appliedApprovals > 0)
        {
            await core.AuditNoisyAsync(syllabusId, "batch_approved", core.Now(), appliedApprovals, ct);
            await db.SaveChangesAsync(ct);
        }
        return Results.Json(new BatchResult(results, new BatchCounts(applied, unchanged, rejected)), JsonBody.Out);
    }

    // ---------------------------------------------------------------- edit

    public async Task<IResult> PatchAsync(HttpContext http, Guid syllabusId, Guid itemId, string ifMatch, CancellationToken ct)
    {
        var db = core.Db;
        if (await core.FindSyllabusAsync(syllabusId, ct) is null || !await db.Items.AsNoTracking().AnyAsync(i => i.Id == itemId && i.SyllabusId == syllabusId, ct))
            return SyllabusProblem.NotFound();
        var (bytes, failure) = await JsonBody.ReadBytesAsync(http, core.Options.JsonBodyBytes, ct);
        if (failure == JsonBody.Failure.TooLarge) return SyllabusProblem.TooLarge();
        if (failure == JsonBody.Failure.Unsupported) return SyllabusProblem.Unsupported();
        if (!JsonBody.TryBindMembers(bytes!, out var m) || m is null || m.Count == 0) return SyllabusProblem.InvalidBare();

        var parsed = ParsePatch(m, out var invalid);
        if (invalid is not null) return invalid;

        var (l, err) = await OpenAsync(syllabusId, itemId, ifMatch, ct);
        if (err is not null) return err;
        await using var tx = l!.Tx;
        var item = l.Item;
        if (item.Kind == "policy" || item.WhenStatus == "not_applicable") { await tx.RollbackAsync(ct); return SyllabusProblem.Conflict("item_not_syncable"); }
        var syl = await db.Syllabi.AsNoTracking().FirstAsync(s => s.Id == syllabusId, ct);
        var now = core.Now();
        var bad = await ApplyPatchAsync(item, syl, parsed!, now, ct);
        if (bad is not null) { await tx.RollbackAsync(ct); db.ChangeTracker.Clear(); return bad; }
        await core.TouchActivityAsync(syllabusId, now, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await RespondItemAsync(http, itemId, ct);
    }

    private sealed class Patch
    {
        public string? Kind; public bool HasKind;
        public string? Title; public bool HasTitle;
        public string? Notes; public bool HasNotes;
        public DateOnly? Date; public bool HasDate;
        public DateOnly? EndDate; public bool HasEndDate;
        public TimeOnly? Time; public bool HasTime;
        public TimeOnly? EndTime; public bool HasEndTime;
        public bool AllDay; public bool HasAllDay;
        public string? Timezone; public bool HasTimezone;
    }

    private static Patch? ParsePatch(Dictionary<string, JsonElement> m, out IResult? invalid)
    {
        invalid = null;
        var p = new Patch();
        foreach (var (key, v) in m)
        {
            var isNull = v.ValueKind == JsonValueKind.Null;
            var isString = v.ValueKind == JsonValueKind.String;
            switch (key)
            {
                case "kind":
                    if (!isString || !ItemRules.EditableKinds.Contains(v.GetString()!)) { invalid = SyllabusProblem.Invalid("kind"); return null; }
                    p.Kind = v.GetString(); p.HasKind = true; break;
                case "title":
                    if (!isString || !TextPolicy.TryValidate(SafeString(v), TextKind.SingleLine, 200, out var t)) { invalid = SyllabusProblem.Invalid("title"); return null; }
                    p.Title = t; p.HasTitle = true; break;
                case "notes":
                    if (isNull) p.Notes = null;
                    else if (!isString || !TextPolicy.TryValidate(SafeString(v), TextKind.Notes, 500, out var n)) { invalid = SyllabusProblem.Invalid("notes"); return null; }
                    else p.Notes = n;
                    p.HasNotes = true; break;
                case "date":
                    if (!isString || !Local.TryDate(SafeString(v), out var d)) { invalid = SyllabusProblem.Invalid("date"); return null; }   // a date cannot be cleared
                    p.Date = d; p.HasDate = true; break;
                case "endDate":
                    if (isNull) p.EndDate = null;
                    else if (!isString || !Local.TryDate(SafeString(v), out var ed)) { invalid = SyllabusProblem.Invalid("endDate"); return null; }
                    else p.EndDate = ed;
                    p.HasEndDate = true; break;
                case "time":
                    if (isNull) p.Time = null;
                    else if (!isString || !Local.TryTime(SafeString(v), out var tm)) { invalid = SyllabusProblem.Invalid("time"); return null; }
                    else p.Time = tm;
                    p.HasTime = true; break;
                case "endTime":
                    if (isNull) p.EndTime = null;
                    else if (!isString || !Local.TryTime(SafeString(v), out var et)) { invalid = SyllabusProblem.Invalid("endTime"); return null; }
                    else p.EndTime = et;
                    p.HasEndTime = true; break;
                case "allDay":
                    if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) { invalid = SyllabusProblem.Invalid("allDay"); return null; }
                    p.AllDay = v.GetBoolean(); p.HasAllDay = true; break;
                case "timezone":
                    if (!isString || !ZoneAllowList.Contains(SafeString(v))) { invalid = SyllabusProblem.Invalid("timezone", "not_allowed"); return null; }
                    p.Timezone = v.GetString(); p.HasTimezone = true; break;
                default:
                    invalid = SyllabusProblem.InvalidBare(); return null;     // read-only or unknown member: 400, never silently ignored
            }
        }
        return p;
    }

    private static string? SafeString(JsonElement v)
    {
        try { return v.GetString(); } catch (InvalidOperationException) { return null; }   // lone surrogate in an escaped string
    }

    private async Task<IResult?> ApplyPatchAsync(SyllabusItem it, SyllabusDoc syl, Patch p, DateTimeOffset now, CancellationToken ct)
    {
        var needsDate = it.WhenStatus == "needs_date";
        if (needsDate && !p.HasDate && (p.HasEndDate || p.HasTime || p.HasEndTime || p.HasAllDay)) return SyllabusProblem.Invalid("date", "required");
        var newDate = p.HasDate ? p.Date : it.DateLocal;
        var newEnd = p.HasEndDate ? p.EndDate : it.EndDateLocal;
        var newTime = p.HasTime ? p.Time : it.TimeLocal;
        var newEndTime = p.HasEndTime ? p.EndTime : it.EndTimeLocal;
        var newAllDay = p.HasAllDay ? p.AllDay : it.AllDay;
        var newTz = p.HasTimezone ? p.Timezone : it.Timezone;
        if (p.HasAllDay && p.AllDay && !p.HasTime && it.TimeLocal is not null) newTime = null;   // switching to all-day drops the stored times
        if (p.HasAllDay && p.AllDay && !p.HasEndTime && it.EndTimeLocal is not null) newEndTime = null;
        if (newAllDay && (newTime is not null || newEndTime is not null)) return SyllabusProblem.Invalid("allDay", "conflict");
        if (newEndTime is not null && newTime is null) return SyllabusProblem.Invalid("endTime", "conflict");
        if (newEnd is not null && newDate is not null && newEnd < newDate) return SyllabusProblem.Invalid("endDate", "out_of_range");
        if (it.RecByDay is not null && (newAllDay || newTime is null)) return SyllabusProblem.Invalid("time", "conflict");
        if (it.RecByDay is not null && p.HasDate && newDate > it.RecUntil) return SyllabusProblem.Invalid("date", "out_of_range");

        var dateFieldsChanged = newDate != it.DateLocal || newEnd != it.EndDateLocal || newTime != it.TimeLocal || newEndTime != it.EndTimeLocal
            || newAllDay != it.AllDay || (p.HasTimezone && newTz != it.Timezone);
        var kindChanged = p.HasKind && p.Kind != it.Kind;
        var titleChanged = p.HasTitle && p.Title != it.Title;
        var notesChanged = p.HasNotes && p.Notes != it.Notes;
        if (!dateFieldsChanged && !kindChanged && !titleChanged && !notesChanged) return null;          // no-op: nothing changes, not even the ETag

        if (kindChanged) it.Kind = p.Kind!;
        if (titleChanged) it.Title = p.Title!;
        if (notesChanged) it.Notes = p.Notes;
        if (dateFieldsChanged)
        {
            if (needsDate)
            {
                var alts = await core.Db.Alternatives.Where(a => a.ItemId == it.Id).ToListAsync(ct);
                core.Db.Alternatives.RemoveRange(alts);
                it.WhenStatus = "resolved"; it.UnknownReason = null;
            }
            it.DateLocal = newDate; it.EndDateLocal = newEnd; it.TimeLocal = newTime; it.EndTimeLocal = newEndTime; it.AllDay = newAllDay;
            it.Timezone = newTz ?? syl.DefaultTimezone;
            it.Derivation = "student_entered"; it.TimezoneSource = "student";
            if (it.RecByDay is not null) { it.RecStartTime = newTime; it.RecEndTime = newEndTime; if (p.HasDate) it.RecFrom = newDate; }
            var flags = it.Flags.Where(f => !ItemRules.DateDerivedFlags.Contains(f)).ToList();
            if (newTime is not null && newDate is not null && Local.IsInDstGap(newDate.Value, newTime.Value, it.Timezone!)) flags.Add("dst_ambiguous");
            it.Flags = [.. flags];
            if (it.Origin == "ai")
            {
                // the snippet no longer supports the value (R2-07)
                it.Confidence = "please_check"; it.ConfidenceReason = ItemRules.EditedAiReason;
            }
            else
            {
                it.Confidence = flags.Contains("dst_ambiguous") || flags.Any(f => f is "tentative_language" or "supersession_cue" or "injection_suspect" or "low_text_quality") ? "please_check" : "probably_right";
                it.ConfidenceReason = ItemRules.StudentReason;
            }
        }
        it.Edited = true;
        it.Revision += 1;
        // Approval belongs to the revision that was approved (reconciliation row 17).
        if (it.Decision == "approved")
        {
            if (it.SyncState == "synced" || it.SyncState == "changed_since_sync" || (it.SyncState == "sync_failed" && it.SyncedRevision is not null)) it.SyncState = "changed_since_sync";
            else { it.Decision = "undecided"; it.SyncState = "not_synced"; it.SyncedRevision = null; }
        }
        it.UpdatedAt = now;
        return null;
    }

    // ---------------------------------------------------------------- delete (student items only)

    public async Task<IResult> DeleteAsync(Guid syllabusId, Guid itemId, string ifMatch, CancellationToken ct)
    {
        if (await core.FindSyllabusAsync(syllabusId, ct) is null || !await core.Db.Items.AsNoTracking().AnyAsync(i => i.Id == itemId && i.SyllabusId == syllabusId, ct))
            return SyllabusProblem.NotFound();
        var (l, err) = await OpenAsync(syllabusId, itemId, ifMatch, ct);
        if (err is not null) return err;
        await using var tx = l!.Tx;
        if (l.Item.Origin != "student") { await tx.RollbackAsync(ct); return SyllabusProblem.Conflict("student_item_only"); }
        var now = core.Now();
        core.Audit(syllabusId, "item_deleted", now, itemId, l.Item.Revision);
        await core.Db.SaveChangesAsync(ct);
        // the item delete cascades to its mock events
        await core.Sql.ExecAsync("DELETE FROM syllabus.items WHERE id = @p0 AND user_id = @p1", ct, itemId, core.Uid);
        await core.TouchActivityAsync(syllabusId, now, ct);
        await tx.CommitAsync(ct);
        core.Db.ChangeTracker.Clear();
        return Results.NoContent();
    }
}
