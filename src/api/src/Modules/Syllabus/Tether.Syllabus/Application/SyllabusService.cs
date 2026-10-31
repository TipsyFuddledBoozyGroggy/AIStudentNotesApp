using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tether.Courses.Contracts;
using Tether.SharedKernel;
using Tether.Syllabus.Domain;
using Tether.Syllabus.Extraction;
using Tether.Syllabus.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Tether.Syllabus.Application;

internal static class Routes
{
    public const string CreateTemplate = "POST /courses/{courseId}/syllabi";
    public const string ItemTemplate = "POST /syllabi/{id}/items";
    public const string SyncTemplate = "POST /syllabi/{id}/calendar-sync";
}

/// <summary>An idempotency lookup result.</summary>
internal sealed record IdemHit(IdempotencyRecord Record, bool Tombstone);

/// <summary>Create, read, list and delete of syllabi, plus the idempotency lookup shared with item create and sync.</summary>
internal sealed class SyllabusService(SyllabusCore core, ICourseOwnership courses, UploadGate uploads, JobCancellation jobs,
    ILogger<SyllabusService> log, KnownNotices notices)
{
    // ---------------------------------------------------------------- idempotency

    public static bool ValidKey(string? key) =>
        key is { Length: >= 8 and <= 128 } && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    public async Task<IdemHit?> LookupKeyAsync(string route, string key, CancellationToken ct)
    {
        var kh = Hashing.Sha256(key);
        var rec = await core.Db.IdempotencyKeys.AsNoTracking().FirstOrDefaultAsync(r => r.Method == "POST" && r.RouteTemplate == route && r.KeyHash == kh, ct);
        if (rec is null) return null;
        if (rec.ExpiresAt <= core.Now()) return null;               // expired rows are swept; an expired key is a new key
        return new IdemHit(rec, rec.TombstonedAt is not null);
    }

    /// <summary>
    /// Fingerprint material (B1-07): every field is length-prefixed and null is distinct from empty, so no two different field lists can
    /// produce the same string (a plain "|" join let "X|Y"+"Z" collide with "X"+"Y|Z").
    /// </summary>
    public static string Material(params string?[] fields) =>
        string.Join("|", fields.Select(f => f is null ? "~" : f.Length.ToString(CultureInfo.InvariantCulture) + ":" + f));

    public byte[] Fingerprint(string material) => Hashing.Hmac(core.Keys.IdempotencyRing[0], material);

    public bool FingerprintMatches(IdempotencyRecord rec, string material) =>
        core.Keys.IdempotencyRing.Any(k => CryptographicOperations.FixedTimeEquals(Hashing.Hmac(k, material), rec.Fingerprint));

    // ---------------------------------------------------------------- create

    public async Task<IResult> CreateAsync(HttpContext http, string idemKey, Guid courseId, bool multipart, CancellationToken ct)
    {
        var uid = core.Uid;
        var coursePath = "/courses/" + Ids2.Course(courseId) + "/syllabi";
        // step 4: ownership chain
        if (!await courses.IsOwnedByAsync(uid, courseId, ct)) return SyllabusProblem.NotFound();
        // step 5: idempotency lookup by header only (no body needed)
        var hit = await LookupKeyAsync(Routes.CreateTemplate, idemKey, ct);
        var candidate = hit is not null;
        // step 6: limits decided from counters only, before any body is read
        if (multipart && !uploads.TryEnter(uid)) return SyllabusProblem.RateLimited();
        try
        {
            if (!candidate)
            {
                var quota = await CheckQuotasAsync(courseId, ct);
                if (quota is not null)
                {
                    // An identical create with this key may have committed between the step-5 lookup and this check (its key and its job
                    // commit together, so if its job is visible here its key is too). Such a request is a candidate replay and skips the
                    // quotas (contract P2 2.1: "gets the replay ... not 409 extraction_in_progress"); anything else keeps the refusal.
                    hit = await LookupKeyAsync(Routes.CreateTemplate, idemKey, ct);
                    candidate = hit is not null;
                    if (!candidate) return quota;
                }
            }
            // step 7: body (streaming caps)
            CreateSyllabusRequest req; string material; string contentSha; string? uploadText; string? fileType; SniffKind kind = SniffKind.Text;
            if (multipart)
            {
                var intake = await UploadIntake.ReadAsync(http, core.Options, ct);
                if (intake.Error is not null) return intake.Error;
                req = intake.Metadata!; contentSha = intake.FileSha256!; kind = intake.Kind; uploadText = intake.DecodedText;
                fileType = kind switch { SniffKind.Pdf => "pdf", SniffKind.Docx => "docx", _ => "txt" };
                material = Material("POST", Routes.CreateTemplate, coursePath, "file", contentSha, intake.MetadataCanonical);
            }
            else
            {
                var (bytes, failure) = await JsonBody.ReadBytesAsync(http, core.Options.PasteBodyBytes, ct);
                if (failure == JsonBody.Failure.TooLarge) return SyllabusProblem.TooLarge();
                if (failure == JsonBody.Failure.Unsupported) return SyllabusProblem.UnsupportedMediaType();
                if (!JsonBody.TryBind<CreateSyllabusRequest>(bytes!, out var r) || r is null) return SyllabusProblem.InvalidBare();
                if (r.Text is null) return SyllabusProblem.Invalid("text", "required");     // exactly one of file or text
                req = r; uploadText = r.Text; fileType = null; contentSha = "";
                material = Material("POST", Routes.CreateTemplate, coursePath, "paste", Hashing.Sha256Hex(r.Text), r.Title, r.TermStart, r.TermEnd, r.DefaultTimezone, r.AllowDuplicate ? "1" : "0", r.NoticeVersion, r.Attestation.AcceptedAt);
            }
            // candidate replay: compare the fingerprint now (before validation); its bytes are discarded
            if (hit is not null)
            {
                if (!FingerprintMatches(hit.Record, material)) return SyllabusProblem.InvalidBare();
                return await ReplayCreateAsync(http, hit, ct);
            }
            // step 8: validation
            var invalid = Validate(req, multipart, out var title, out var termStart, out var termEnd);
            if (invalid is not null) return invalid;

            CanonicalDocument? doc = null;
            if (uploadText is not null && (!multipart || kind == SniffKind.Text))
            {
                if (uploadText.Contains('\0')) return SyllabusProblem.Invalid("text", "invalid");
                if (TextPolicy.CodePoints(uploadText) > 50_000) return SyllabusProblem.TooLarge();
                doc = DocumentText.Canonicalize(uploadText);
                if (doc.Text.Length == 0) return SyllabusProblem.Invalid("text", "required");
                if (doc.CodePointCount > 50_000) return SyllabusProblem.TooLarge();
                if (!multipart) contentSha = Hashing.Sha256Hex(doc.Text);
            }
            return await CommitCreateAsync(http, courseId, idemKey, req, title, termStart, termEnd, multipart, fileType, contentSha, doc, Fingerprint(material), ct);
        }
        finally { if (multipart) uploads.Leave(uid); }
    }

    private async Task<IResult?> CheckQuotasAsync(Guid courseId, CancellationToken ct)
    {
        var o = core.Options;
        if (await core.Db.Jobs.AsNoTracking().AnyAsync(j => j.State == "queued" || j.State == "running", ct)) return SyllabusProblem.Conflict("extraction_in_progress");
        if (await core.Db.Syllabi.AsNoTracking().CountAsync(s => s.CourseId == courseId, ct) >= o.SyllabiPerCourse) return SyllabusProblem.Conflict("syllabus_limit_reached");
        if (await core.Db.Syllabi.AsNoTracking().CountAsync(ct) >= o.SyllabiPerUser) return SyllabusProblem.Conflict("syllabus_limit_reached");
        var day = DateOnly.FromDateTime(core.Now().UtcDateTime);
        var used = await core.Db.Counters.AsNoTracking().Where(c => c.Day == day).Select(c => (int?)c.Created).FirstOrDefaultAsync(ct) ?? 0;
        if (used >= o.CreationsPerDay) return SyllabusProblem.RateLimited(3600);
        return null;
    }

    private IResult? Validate(CreateSyllabusRequest req, bool multipart, out string? title, out DateOnly? termStart, out DateOnly? termEnd)
    {
        title = null; termStart = null; termEnd = null;
        if (req.Title is not null)
        {
            if (!TextPolicy.TryValidate(req.Title, TextKind.SingleLine, 200, out var t)) return SyllabusProblem.Invalid("title");
            title = t;
        }
        if (!ZoneAllowList.Contains(req.DefaultTimezone)) return SyllabusProblem.Invalid("defaultTimezone", "not_allowed");
        if ((req.TermStart is null) != (req.TermEnd is null)) return SyllabusProblem.Invalid("termStart", "required");
        if (req.TermStart is not null)
        {
            if (!Local.TryDate(req.TermStart, out var ts)) return SyllabusProblem.Invalid("termStart");
            if (!Local.TryDate(req.TermEnd, out var te)) return SyllabusProblem.Invalid("termEnd");
            if (te < ts || te.DayNumber - ts.DayNumber > 400) return SyllabusProblem.Invalid("termEnd", "out_of_range");
            termStart = ts; termEnd = te;
        }
        if (!notices.IsKnown(req.NoticeVersion)) return SyllabusProblem.Invalid("noticeVersion", "not_allowed");
        if (!req.Attestation.Accepted) return SyllabusProblem.Invalid("attestation", "required");
        // The client's acceptedAt is advisory only: shape and a skew bound; the consent record is the server's receipt time (R3-11).
        if (!DateTimeOffset.TryParse(req.Attestation.AcceptedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var claimed)
            || !LooksLikeTimestamp(req.Attestation.AcceptedAt) || (claimed - core.Now()).Duration() > TimeSpan.FromHours(24))
            return SyllabusProblem.Invalid("attestation.acceptedAt");
        return null;
    }

    private static bool LooksLikeTimestamp(string s) =>
        System.Text.RegularExpressions.Regex.IsMatch(s, @"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]+)?(Z|[+-][0-9]{2}:[0-9]{2})(?![\s\S])", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private async Task<IResult> CommitCreateAsync(HttpContext http, Guid courseId, string idemKey, CreateSyllabusRequest req, string? title, DateOnly? termStart,
        DateOnly? termEnd, bool multipart, string? fileType, string contentSha, CanonicalDocument? doc, byte[] fingerprint, CancellationToken ct)
    {
        var db = core.Db; var uid = core.Uid; var o = core.Options;
        var now = core.Now();
        var syllabusId = Ids.New();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await core.Sql.ExecAsync("SELECT pg_advisory_xact_lock(hashtextextended(@p0, 0))", ct, "syllabus.create:" + uid);
            // Re-check the key under the lock (two simultaneous identical creates).
            var existing = await LookupKeyAsync(Routes.CreateTemplate, idemKey, ct);
            if (existing is not null)
            {
                await tx.RollbackAsync(ct);
                return await ReplayOrMismatchAsync(http, existing, fingerprint, ct);
            }
            await core.Sql.ExecAsync("DELETE FROM syllabus.idempotency_keys WHERE user_id = @p0 AND method = 'POST' AND route_template = @p1 AND key_hash = @p2 AND expires_at <= @p3",
                ct, uid, Routes.CreateTemplate, Hashing.Sha256(idemKey), now);

            var quota = await CheckQuotasAsync(courseId, ct);
            if (quota is not null) { await tx.RollbackAsync(ct); return quota; }
            if (!req.AllowDuplicate && !string.IsNullOrEmpty(contentSha))
            {
                var dup = await db.Syllabi.AsNoTracking().Where(s => s.CourseId == courseId && s.ContentSha256 == contentSha).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
                if (dup is { } d) { await tx.RollbackAsync(ct); return SyllabusProblem.Conflict("duplicate_syllabus", Ids2.Syl(d)); }
            }
            // The durable daily counter: counted at creation, never refunded (a rolled-back create does not count).
            var day = DateOnly.FromDateTime(now.UtcDateTime);
            var counted = await core.Sql.ScalarAsync<short?>("""
                INSERT INTO syllabus.creation_counters (user_id, day, created) VALUES (@p0, @p1, 1)
                ON CONFLICT (user_id, day) DO UPDATE SET created = syllabus.creation_counters.created + 1
                WHERE syllabus.creation_counters.created < @p2 RETURNING created
                """, ct, uid, day, o.CreationsPerDay);
            if (counted is null) { await tx.RollbackAsync(ct); return SyllabusProblem.RateLimited(3600); }

            var inputKind = multipart ? "upload" : "paste";
            var doc0 = doc;
            var s = new SyllabusDoc
            {
                Id = syllabusId, UserId = uid, CourseId = courseId, Title = title, InputKind = inputKind, FileType = fileType, Status = "queued",
                AiSource = "mock", DemoSource = true, OriginalRetained = false, ContentSha256 = contentSha,
                CharCount = doc0?.CodePointCount ?? 0, HiddenContentRemoved = doc0?.HiddenRemoved ?? 0, HiddenContentDropped = (doc0?.HiddenRemoved ?? 0) > 0,
                InjectionSuspect = doc0?.InjectionSuspect ?? false,
                NoticeVersion = req.NoticeVersion, NoticeAttested = true, NoticeAttestedAt = now,   // the server's receipt time, never the client's
                TermStart = termStart, TermEnd = termEnd, DefaultTimezone = req.DefaultTimezone,
                ExtractorVersion = MockRuleExtractor.ExtractorVersion, SchemaVersion = "syllabus.extract.v1", Model = "mock",
                CreatedAt = now, UpdatedAt = now, LastActivityAt = now,
                CanonicalTextPurgeAt = now.AddDays(o.IdleDays),
            };
            s.BatchApproveAllowed = !s.InjectionSuspect && !s.HiddenContentDropped;
            db.Syllabi.Add(s);
            await db.SaveChangesAsync(ct);
            if (doc0 is not null)
            {
                var spans = DocumentText.LineSpans(doc0.Text);
                db.CanonicalTexts.Add(new CanonicalText
                {
                    SyllabusId = syllabusId, UserId = uid, Text = doc0.Text, Utf16Length = doc0.Utf16Length,
                    Lines = JsonSerializer.Serialize(spans.Select((l, i) => new { id = "L" + (i + 1).ToString("D4", CultureInfo.InvariantCulture), page = (int?)null, start = l.Start, end = l.End })),
                    Pages = "[]", ExtractorVersion = MockRuleExtractor.ExtractorVersion, TextSha256 = Hashing.Sha256Hex(doc0.Text), CreatedAt = now,
                });
            }
            db.Jobs.Add(new ExtractionJob { Id = Ids.New(), UserId = uid, SyllabusId = syllabusId, State = "queued", AvailableAt = now, CreatedAt = now, JobCapMicros = 0 });
            db.IdempotencyKeys.Add(new IdempotencyRecord
            {
                Id = Ids.New(), UserId = uid, CourseId = courseId, Method = "POST", RouteTemplate = Routes.CreateTemplate, KeyHash = Hashing.Sha256(idemKey),
                Fingerprint = fingerprint, ResultKind = "syllabus", ResultId = syllabusId, CreatedAt = now, ExpiresAt = now.AddHours(24),
            });
            // content-free audit: ids, version, time and an HMAC (never a plain content hash) of the content hash
            core.Audit(syllabusId, "syllabus_created", now, contentHmac: Hashing.Hmac(core.Keys.Audit, contentSha), noticeVersion: req.NoticeVersion);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            if (pg.SqlState == PostgresErrorCodes.UniqueViolation && pg.ConstraintName == "ux_extraction_jobs_one_in_flight") return SyllabusProblem.Conflict("extraction_in_progress");
            if (pg.SqlState == PostgresErrorCodes.UniqueViolation && pg.ConstraintName == "ux_idempotency_keys_scope")
            {
                var winner = await LookupKeyAsync(Routes.CreateTemplate, idemKey, ct);
                if (winner is not null) return await ReplayOrMismatchAsync(http, winner, fingerprint, ct);
            }
            log.LogWarning("Syllabus create failed with SQLSTATE {SqlState}", pg.SqlState);   // code only, never row data
            throw;
        }

        var created = await db.Syllabi.AsNoTracking().FirstAsync(x => x.Id == syllabusId, ct);
        http.Response.Headers.ETag = core.ETags.ForSyllabus(created);
        http.Response.Headers.Location = "/api/v1/syllabi/" + Ids2.Syl(syllabusId);
        return Results.Json(await core.RepresentAsync(created, ct), JsonBody.Out, statusCode: 202);
    }

    private async Task<IResult> ReplayOrMismatchAsync(HttpContext http, IdemHit hit, byte[] fingerprint, CancellationToken ct) =>
        CryptographicOperations.FixedTimeEquals(hit.Record.Fingerprint, fingerprint) ? await ReplayCreateAsync(http, hit, ct) : SyllabusProblem.InvalidBare();

    private async Task<IResult> ReplayCreateAsync(HttpContext http, IdemHit hit, CancellationToken ct)
    {
        // A tombstone means the syllabus was deleted: never the original response, never re-executed. Indistinguishable from any missing resource.
        if (hit.Tombstone || hit.Record.ResultId is not { } id) return SyllabusProblem.NotFound();
        var s = await core.FindSyllabusAsync(id, ct);
        if (s is null) return SyllabusProblem.NotFound();
        http.Response.Headers["Idempotency-Replayed"] = "true";
        http.Response.Headers.ETag = core.ETags.ForSyllabus(s);
        http.Response.Headers.Location = "/api/v1/syllabi/" + Ids2.Syl(id);
        return Results.Json(await core.RepresentAsync(s, ct), JsonBody.Out, statusCode: 202);
    }

    // ---------------------------------------------------------------- read

    public async Task<IResult> GetAsync(HttpContext http, Guid id, CancellationToken ct)
    {
        var s = await core.FindSyllabusAsync(id, ct);
        if (s is null) return SyllabusProblem.NotFound();
        http.Response.Headers.ETag = core.ETags.ForSyllabus(s);
        return Results.Json(await core.RepresentAsync(s, ct), JsonBody.Out);
    }

    public async Task<IResult> ListAsync(Guid courseId, string? cursor, int limit, CancellationToken ct)
    {
        if (!await courses.IsOwnedByAsync(core.Uid, courseId, ct)) return SyllabusProblem.NotFound();
        Guid? after = null;
        if (cursor is not null)
        {
            if (!Cursor.TryGuid(cursor, out var g)) return SyllabusProblem.Invalid("cursor");
            after = g;
        }
        var q = core.Db.Syllabi.AsNoTracking().Where(s => s.CourseId == courseId);
        if (after is { } a) q = q.Where(s => s.Id.CompareTo(a) < 0);
        var rows = await q.OrderByDescending(s => s.Id).Take(limit + 1).ToListAsync(ct);
        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        var counts = await core.CountsAsync([.. rows.Select(r => r.Id)], ct);
        var items = rows.Select(r => r.ToResponse(counts[r.Id], core.ETags)).ToList();
        return Results.Json(new Page<SyllabusResponse>(items, more ? Cursor.FromGuid(rows[^1].Id) : null), JsonBody.Out);
    }

    // ---------------------------------------------------------------- delete

    /// <summary>
    /// Hard, immediate and exhaustive (section 5.2, data section 7): one transaction that locks the syllabus row (which also blocks a running
    /// job's next write), re-keys the live create key into a content-free tombstone, writes the audit row and deletes the syllabus; the DB
    /// cascade removes items, text, snippets, jobs, runs, sync records and the mock events. Returns false if the syllabus does not exist.
    /// </summary>
    public static async Task<bool> DeleteCoreAsync(SyllabusCore core, Guid syllabusId, CancellationToken ct)
    {
        // B1-01: the cascade can meet a job-row writer in the opposite lock order; PostgreSQL aborts one side (40P01). The whole transaction
        // rolled back, so the delete is retried a bounded number of times (same approach as the Lectures session delete).
        for (var attempt = 0; ; attempt++)
        {
            try { return await DeleteOnceAsync(core, syllabusId, ct); }
            catch (Exception ex) when (Pg.IsDeadlockOrSerialization(ex) && attempt < 4)
            {
                core.Db.ChangeTracker.Clear();
                await Task.Delay(Random.Shared.Next(20, 120) * (attempt + 1), ct);
            }
        }
    }

    private static async Task<bool> DeleteOnceAsync(SyllabusCore core, Guid syllabusId, CancellationToken ct)
    {
        var uid = core.Uid; var now = core.Now();
        await using var tx = await core.Db.Database.BeginTransactionAsync(ct);
        if (!await core.LockSyllabusAsync(syllabusId, ct)) { await tx.RollbackAsync(ct); return false; }
        await core.Sql.ExecAsync("""
            UPDATE syllabus.idempotency_keys SET tombstoned_at = @p0, result_id = NULL, course_id = NULL
            WHERE user_id = @p1 AND result_kind = 'syllabus' AND tombstoned_at IS NULL AND result_id = @p2
            """, ct, now, uid, syllabusId);
        core.Audit(syllabusId, "syllabus_deleted", now);
        await core.Db.SaveChangesAsync(ct);
        await core.Sql.ExecAsync("DELETE FROM syllabus.syllabi WHERE id = @p0 AND user_id = @p1", ct, syllabusId, uid);
        await tx.CommitAsync(ct);
        core.Db.ChangeTracker.Clear();
        return true;
    }

    public async Task<IResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        // B1-05: ownership first (the query filter is user-scoped); only then interrupt a running extraction. A foreign or missing id has no side effect.
        if (await core.FindSyllabusAsync(id, ct) is null) return SyllabusProblem.NotFound();
        jobs.Cancel(id);
        return await DeleteCoreAsync(core, id, ct) ? Results.NoContent() : SyllabusProblem.NotFound();
    }
}

internal sealed class KnownNotices(IReadOnlyCollection<string> versions)
{
    public bool IsKnown(string v) => versions.Contains(v, StringComparer.Ordinal);
}

internal static class Cursor
{
    public static string FromGuid(Guid g) => g.ToString("N");
    public static bool TryGuid(string s, out Guid g) { g = default; return s.Length == 32 && Guid.TryParseExact(s, "N", out g) && g.ToString("N") == s; }

    public static string Encode(DateOnly d, Guid id) => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(Local.Date(d) + "|" + id.ToString("N")));
    public static bool TryDecode(string s, out DateOnly d, out Guid id)
    {
        d = default; id = default;
        try
        {
            var raw = Encoding.UTF8.GetString(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(s));
            var parts = raw.Split('|');
            if (parts.Length != 2) return false;
            var okDate = DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) || TryNine(parts[0], out d);
            return okDate && Guid.TryParseExact(parts[1], "N", out id);
        }
        catch (FormatException) { return false; }
    }
    private static bool TryNine(string s, out DateOnly d) { d = default; return s == "9999-12-31" && (d = new DateOnly(9999, 12, 31)) == d; }
}
