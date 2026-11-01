using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tether.SharedKernel;
using Tether.Syllabus.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;

namespace Tether.Syllabus.Application;

/// <summary>Limits and switches. Every default is a contract proposal (section 10 and 11), not a measured value.</summary>
internal sealed class SyllabusOptions
{
    public int SyllabiPerCourse { get; init; } = 5;
    public int SyllabiPerUser { get; init; } = 50;
    public int CreationsPerDay { get; init; } = 10;
    public int MaxItems { get; init; } = 300;
    public int MaxStudentItems { get; init; } = 100;
    public int MaxAiItems { get; init; } = 200;
    public int MaxEventsPerSyllabus { get; init; } = 1000;
    public int MaxEventsPerUser { get; init; } = 5000;
    public int MaxEventsPerCall { get; init; } = 500;
    public int MaxOccurrencesPerItem { get; init; } = 60;
    public long UploadMaxBytes { get; init; } = 10L * 1024 * 1024;
    public long BytesInFlight { get; init; } = 100L * 1024 * 1024;
    public int PasteBodyBytes { get; init; } = 1024 * 1024;
    public int JsonBodyBytes { get; init; } = 16 * 1024;
    public int MetadataBytes { get; init; } = 4 * 1024;
    public int UploadReadSeconds { get; init; } = 60;
    /// <summary>
    /// SBR-07: deadline for reading a paste body (at most <see cref="PasteBodyBytes"/>), which holds the user's create slot and 1 MB of the
    /// bytes-in-flight budget while it is read. Proposal, not measured: 1 MB in 30 s is about 35 KB/s.
    /// </summary>
    public int PasteReadSeconds { get; init; } = 30;
    /// <summary>SBR-07: Kestrel minimum data rate for a paste body (bytes per second after a 5 s grace). Proposal, not measured.</summary>
    public int PasteMinBytesPerSecond { get; init; } = 16 * 1024;
    /// <summary>SB-09: how long a same-key retry of a create waits for the user's create slot before it is 429 (proposal, not measured).</summary>
    public int CreateGateWaitMilliseconds { get; init; } = 10_000;
    public int WritePermitPerMinute { get; init; } = 20;
    public int ReadPermitPerMinute { get; init; } = 120;
    public int SyncPerSyllabusPerMinute { get; init; } = 6;
    public int SyncPerUserPerMinute { get; init; } = 12;
    public int BatchPerUserPerMinute { get; init; } = 12;
    public int ExportPerUserPerMinute { get; init; } = 6;
    public int LeaseSeconds { get; init; } = 60;
    public int MaxAttempts { get; init; } = 3;
    public int WorkerPollMilliseconds { get; init; } = 300;
    public int MaintenanceSeconds { get; init; } = 300;
    public bool WorkerEnabled { get; init; } = true;
    public int MockStageDelayMilliseconds { get; init; }
    public string? CalendarFault { get; init; }
    public int DebrisDays { get; init; } = 7;
    public int IdleDays { get; init; } = 180;
    public int AbsoluteDays { get; init; } = 365;
    /// <summary>B1-04: newest sync/remove runs kept per syllabus (older runs, their items and their replay keys are pruned on write). Bounds a user to SyllabiPerUser x this.</summary>
    public int SyncRunsPerSyllabus { get; init; } = 20;
    /// <summary>B1-04: retention of decision_audit rows. PROPOSAL pending Product Owner decision (default 400 days, about 13 months).</summary>
    public int AuditRetentionDays { get; init; } = 400;
    /// <summary>B1-04: per-user cap for the high-volume audit actions (synced, sync_removed, exported, batch_approved summary). Decision rows (approved, skipped, undecided) are never dropped.</summary>
    public int AuditNoisyCapPerUser { get; init; } = 20000;

    public static SyllabusOptions From(IConfiguration c) => new()
    {
        SyllabiPerCourse = c.GetValue("Syllabus:SyllabiPerCourse", 5),
        SyllabiPerUser = c.GetValue("Syllabus:SyllabiPerUser", 50),
        WritePermitPerMinute = c.GetValue("RateLimiting:SyllabusWritePermitPerMinute", 20),
        ReadPermitPerMinute = c.GetValue("RateLimiting:SyllabusReadPermitPerMinute", 120),
        SyncPerSyllabusPerMinute = c.GetValue("RateLimiting:SyllabusSyncPerSyllabusPerMinute", 6),
        SyncPerUserPerMinute = c.GetValue("RateLimiting:SyllabusSyncPerUserPerMinute", 12),
        BatchPerUserPerMinute = c.GetValue("RateLimiting:SyllabusBatchPerUserPerMinute", 12),
        ExportPerUserPerMinute = c.GetValue("RateLimiting:SyllabusExportPerUserPerMinute", 6),
        BytesInFlight = c.GetValue("Syllabus:BytesInFlight", 100L * 1024 * 1024),
        LeaseSeconds = c.GetValue("Syllabus:LeaseSeconds", 60),
        WorkerEnabled = c.GetValue("Syllabus:Worker:Enabled", true),
        WorkerPollMilliseconds = Math.Max(20, c.GetValue("Syllabus:Worker:PollMilliseconds", 300)),
        MaintenanceSeconds = Math.Max(5, c.GetValue("Syllabus:MaintenanceIntervalSeconds", 300)),
        MockStageDelayMilliseconds = c.GetValue("Syllabus:Mock:StageDelayMilliseconds", 0),
        PasteReadSeconds = Math.Clamp(c.GetValue("Syllabus:PasteReadSeconds", 30), 1, 120),
        CalendarFault = c["Syllabus:Mock:Faults:Calendar"],
        SyncRunsPerSyllabus = Math.Max(1, c.GetValue("Syllabus:SyncRunsPerSyllabus", 20)),
        AuditRetentionDays = Math.Max(1, c.GetValue("Syllabus:AuditRetentionDays", 400)),
        AuditNoisyCapPerUser = Math.Max(1, c.GetValue("Syllabus:AuditNoisyCapPerUser", 20000)),
    };
}

/// <summary>
/// Server secrets, HKDF sub-keys of the per-environment root secret (B1-11). The explicit <c>Security:ETagKey</c>, <c>Security:IdempotencyHmacKey</c>,
/// <c>Security:IdempotencyHmacKeyPrevious</c> and <c>Security:SyllabusAuditHmacKey</c> settings (base64, >= 32 bytes) remain optional overrides.
/// </summary>
internal sealed class SyllabusKeys(byte[] etag, byte[][] idempotencyRing, byte[] audit, string restoreEpoch)
{
    public byte[] ETag { get; } = etag;
    /// <summary>Active key first, previous key second (rotation overlap, N-04).</summary>
    public byte[][] IdempotencyRing { get; } = idempotencyRing;
    public byte[] Audit { get; } = audit;
    public string RestoreEpoch { get; } = restoreEpoch;

    public static SyllabusKeys From(IConfiguration c, Tether.SharedKernel.Security.DerivedKeys derived)
    {
        static byte[] Override(IConfiguration c, string name, Func<byte[]> derive)
        {
            if (c[name] is not { Length: > 0 } b64) return derive();
            var k = Convert.FromBase64String(b64);
            return k.Length >= 32 ? k : throw new InvalidOperationException($"{name} must decode to at least 32 bytes.");
        }
        var ring = new List<byte[]> { Override(c, "Security:IdempotencyHmacKey", () => derived.Derive(Tether.SharedKernel.Security.DerivedKeys.Labels.Idempotency)) };
        if (c["Security:IdempotencyHmacKeyPrevious"] is { Length: > 0 }) ring.Add(Override(c, "Security:IdempotencyHmacKeyPrevious", () => throw new InvalidOperationException()));
        return new SyllabusKeys(
            Override(c, "Security:ETagKey", () => derived.Derive(Tether.SharedKernel.Security.DerivedKeys.Labels.ETag)), [.. ring],
            Override(c, "Security:SyllabusAuditHmacKey", () => derived.Derive(Tether.SharedKernel.Security.DerivedKeys.Labels.SyllabusAudit)),
            c["Syllabus:RestoreEpoch"] ?? "0");
    }
}

internal static class Hashing
{
    public static string Sha256Hex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));
    public static string Sha256Hex(string s) => Sha256Hex(Encoding.UTF8.GetBytes(s));
    public static byte[] Sha256(string s) => SHA256.HashData(Encoding.UTF8.GetBytes(s));
    public static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
}

/// <summary>Opaque weak ETags. The tag is an HMAC over the resource id, row_version, created_at and the restore epoch (R3-14), so a tag
/// is never accepted for another resource and survives neither a restore nor a guess. Compared in constant time.</summary>
internal sealed class SyllabusETag(SyllabusKeys keys)
{
    public string ForSyllabus(SyllabusDoc s) => Tag("syl", s.Id, s.RowVersion, s.CreatedAt);
    public string ForItem(SyllabusItem i) => Tag("itm", i.Id, i.RowVersion, i.CreatedAt);

    private string Tag(string kind, Guid id, int rowVersion, DateTimeOffset createdAt)
    {
        var mac = Hashing.Hmac(keys.ETag, $"{kind}|{id:N}|{rowVersion}|{createdAt.UtcTicks}|{keys.RestoreEpoch}");
        return $"W/\"{WebEncoders.Base64UrlEncode(mac, 0, 16)}\"";
    }

    public bool Matches(string? presented, string current)
    {
        if (string.IsNullOrWhiteSpace(presented)) return false;
        var a = Encoding.ASCII.GetBytes(Normalize(presented));
        var b = Encoding.ASCII.GetBytes(Normalize(current));
        return CryptographicOperations.FixedTimeEquals(a, b);

        static string Normalize(string tag) => (tag.Trim().StartsWith("W/", StringComparison.Ordinal) ? tag.Trim()[2..] : tag.Trim()).Trim('"');
    }
}

internal static class ItemKey
{
    /// <summary>"ik_" + first 32 characters of base64url(SHA-256(UTF-8("tether.syllabus.item.v1\n" + syllabusId + "\n" + itemId))), no padding (section 4.1). Inputs are the WIRE ids.</summary>
    public static string Derive(string syllabusWireId, string itemWireId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("tether.syllabus.item.v1\n" + syllabusWireId + "\n" + itemWireId));
        return "ik_" + WebEncoders.Base64UrlEncode(hash)[..32];
    }
}

internal static class Ids2
{
    public const string Syllabus = "syl", Item = "itm", Sync = "syn", Event = "evt", Unclaimed = "unc";
    public static string Syl(Guid id) => WireId.Format(Syllabus, id);
    public static string Itm(Guid id) => WireId.Format(Item, id);
    public static string Course(Guid id) => WireId.Format(null, id);
    public static string KeyOf(Guid syllabusId, Guid itemId) => ItemKey.Derive(Syl(syllabusId), Itm(itemId));
}

/// <summary>Strict parsing of wall-clock values. Manual (no culture, no trailing-whitespace tolerance), so "2026-01-01\n" never parses.</summary>
internal static class Local
{
    public static bool TryDate(string? s, out DateOnly d)
    {
        d = default;
        if (s is null || s.Length != 10 || s[4] != '-' || s[7] != '-') return false;
        for (var i = 0; i < 10; i++) if (i is not (4 or 7) && !char.IsAsciiDigit(s[i])) return false;
        var y = int.Parse(s.AsSpan(0, 4), CultureInfo.InvariantCulture);
        var m = int.Parse(s.AsSpan(5, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(s.AsSpan(8, 2), CultureInfo.InvariantCulture);
        if (y is < 2000 or > 2100 || m is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(y, m)) return false;
        d = new DateOnly(y, m, day);
        return true;
    }

    public static bool TryTime(string? s, out TimeOnly t)
    {
        t = default;
        if (s is null || s.Length != 5 || s[2] != ':') return false;
        if (!char.IsAsciiDigit(s[0]) || !char.IsAsciiDigit(s[1]) || !char.IsAsciiDigit(s[3]) || !char.IsAsciiDigit(s[4])) return false;
        var h = (s[0] - '0') * 10 + (s[1] - '0'); var m = (s[3] - '0') * 10 + (s[4] - '0');
        if (h > 23 || m > 59) return false;
        t = new TimeOnly(h, m);
        return true;
    }

    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string Time(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);
    public static string? DateOrNull(DateOnly? d) => d is { } v ? Date(v) : null;
    public static string? TimeOrNull(TimeOnly? t) => t is { } v ? Time(v) : null;
    public static string Weekday(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "MO", DayOfWeek.Tuesday => "TU", DayOfWeek.Wednesday => "WE", DayOfWeek.Thursday => "TH",
        DayOfWeek.Friday => "FR", DayOfWeek.Saturday => "SA", _ => "SU",
    };

    /// <summary>
    /// A wall-clock time that does not exist (spring-forward gap) in <paramref name="zone"/>. Fails closed (B1-14): a zone the host cannot resolve (missing tzdata)
    /// throws instead of answering false, because "not in a gap" would let a nonexistent local time through unflagged.
    /// </summary>
    public static bool IsInDstGap(DateOnly d, TimeOnly t, string zone)
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(zone); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException("The host cannot resolve a time zone from the allow-list: install tzdata in the runtime image. DST-gap detection cannot run without it.", ex);
        }
        return tz.IsInvalidTime(d.ToDateTime(t, DateTimeKind.Unspecified));
    }
}

/// <summary>Server allow-list of IANA zones (contract: "validated against a server allow-list"). Independent of the host's tzdata.</summary>
internal static class ZoneAllowList
{
    private static readonly HashSet<string> Zones = new(StringComparer.Ordinal)
    {
        "America/New_York", "America/Chicago", "America/Denver", "America/Phoenix", "America/Los_Angeles", "America/Anchorage",
        "America/Detroit", "America/Indiana/Indianapolis", "America/Boise", "America/Juneau", "America/Adak", "America/Halifax",
        "America/St_Johns", "America/Toronto", "America/Vancouver", "America/Edmonton", "America/Winnipeg", "America/Regina",
        "America/Mexico_City", "America/Bogota", "America/Lima", "America/Santiago", "America/Sao_Paulo", "America/Argentina/Buenos_Aires",
        "America/Caracas", "America/Puerto_Rico", "America/Panama", "America/Havana", "America/Jamaica",
        "Pacific/Honolulu", "Pacific/Auckland", "Pacific/Guam", "Pacific/Fiji", "Pacific/Pago_Pago",
        "Europe/London", "Europe/Dublin", "Europe/Lisbon", "Europe/Paris", "Europe/Berlin", "Europe/Madrid", "Europe/Rome",
        "Europe/Amsterdam", "Europe/Brussels", "Europe/Zurich", "Europe/Vienna", "Europe/Stockholm", "Europe/Oslo", "Europe/Copenhagen",
        "Europe/Helsinki", "Europe/Warsaw", "Europe/Prague", "Europe/Athens", "Europe/Istanbul", "Europe/Moscow", "Europe/Kyiv",
        "Asia/Tokyo", "Asia/Seoul", "Asia/Shanghai", "Asia/Hong_Kong", "Asia/Singapore", "Asia/Kolkata", "Asia/Dubai", "Asia/Karachi",
        "Asia/Dhaka", "Asia/Bangkok", "Asia/Jakarta", "Asia/Manila", "Asia/Taipei", "Asia/Jerusalem", "Asia/Riyadh", "Asia/Tehran",
        "Africa/Cairo", "Africa/Lagos", "Africa/Nairobi", "Africa/Johannesburg", "Africa/Casablanca", "Africa/Accra",
        "Australia/Sydney", "Australia/Melbourne", "Australia/Brisbane", "Australia/Perth", "Australia/Adelaide",
        "Atlantic/Reykjavik",
        // UTC for a browser that reports UTC (web sends the Area/Location form). Bare "UTC"/"GMT" and Etc/GMT* stay out: the contract
        // (ianaZone) is Area/Location, and a fixed-offset or legacy alias is not a place a student studies in.
        "Etc/UTC",
    };

    public static bool Contains(string? zone) => zone is not null && Zones.Contains(zone);

    /// <summary>Startup check (B1-14): every allow-listed zone must resolve on this host, or the DST-gap check would be unreliable.</summary>
    public static void EnsureHostSupportsAll(Func<string, bool>? resolves = null)
    {
        resolves ??= static z =>
        {
            try { TimeZoneInfo.FindSystemTimeZoneById(z); return true; }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return false; }
        };
        var missing = Zones.Where(z => !resolves(z)).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"The host is missing tzdata for {missing.Length} allow-listed time zone(s): {string.Join(", ", missing)}. Install tzdata in the runtime image; DST-gap detection cannot run without it.");
    }
}

/// <summary>Fixed-window counters with an injectable clock, for the per-user and per-syllabus limits the host limiter cannot key on.</summary>
internal sealed class RateGate(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, (long Window, int Count)> _w = new();

    /// <summary>True if the call is allowed (and counted). One minute windows.</summary>
    public bool TryAcquire(string key, int limit)
    {
        var window = clock.GetUtcNow().ToUnixTimeSeconds() / 60;
        var allowed = false;
        _w.AddOrUpdate(key, _ => { allowed = limit > 0; return (window, 1); }, (_, cur) =>
        {
            if (cur.Window != window) { allowed = limit > 0; return (window, 1); }
            if (cur.Count >= limit) { allowed = false; return cur; }
            allowed = true; return (window, cur.Count + 1);
        });
        if (_w.Count > 20_000) foreach (var kv in _w) if (kv.Value.Window < window - 1) _w.TryRemove(kv.Key, out _);
        return allowed;
    }
}

/// <summary>
/// R3-05 and SB-09: the create gate for BOTH create paths (multipart upload and pasted-text JSON). One create body in flight per user and a
/// global bytes-in-flight budget; each accepted create reserves its full wire cap (10 MB for an upload, 1 MB for a paste) before any body
/// byte is read. A request with a DIFFERENT Idempotency-Key while the user's slot is taken is refused at once (429). A request with the
/// SAME key is a retry of the create in flight: it waits (bounded in time and count, its body still unread) and then enters, so it becomes
/// the contract's replay instead of a 429 (API_CONTRACTS_P2 2.1, candidate replays). In-process only, like JobCancellation (ADR-020d).
/// </summary>
internal sealed class UploadGate(SyllabusOptions options)
{
    /// <summary>Same-key retries that may wait for one user's slot at a time; more are refused at once.</summary>
    public const int MaxWaitersPerUser = 8;

    private sealed class Slot(string key, long reserved)
    {
        public string Key { get; } = key;
        public long Reserved { get; } = reserved;
        public int Waiters { get; set; }
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly object _lock = new();
    private readonly Dictionary<Guid, Slot> _users = [];
    private long _reserved;

    /// <summary>True when the caller holds the user's slot (and must call <see cref="Leave"/>); false means 429.</summary>
    public async Task<bool> EnterAsync(Guid userId, string idempotencyKey, long reserveBytes, CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + options.CreateGateWaitMilliseconds;   // monotonic; a real wait, not the app clock
        while (true)
        {
            Slot held;
            lock (_lock)
            {
                if (!_users.TryGetValue(userId, out held!))
                {
                    if (_reserved + reserveBytes > options.BytesInFlight) return false;
                    _users[userId] = new Slot(idempotencyKey, reserveBytes);
                    _reserved += reserveBytes;
                    return true;
                }
                if (!string.Equals(held.Key, idempotencyKey, StringComparison.Ordinal) || held.Waiters >= MaxWaitersPerUser) return false;
                held.Waiters++;
            }
            try
            {
                var left = TimeSpan.FromMilliseconds(deadline - Environment.TickCount64);
                if (left <= TimeSpan.Zero) return false;
                try { await held.Released.Task.WaitAsync(left, ct); }
                catch (TimeoutException) { return false; }
            }
            finally { lock (_lock) held.Waiters--; }
        }
    }

    /// <summary>Test introspection: whether a create of this user currently holds the gate.</summary>
    internal bool IsHeld(Guid userId) { lock (_lock) return _users.ContainsKey(userId); }

    public void Leave(Guid userId)
    {
        Slot? s;
        lock (_lock)
        {
            if (!_users.Remove(userId, out s)) return;
            _reserved -= s.Reserved;
        }
        s.Released.TrySetResult();
    }
}

/// <summary>Lets DELETE cancel a running extraction in this process (every job writer also re-checks the syllabus exists).</summary>
internal sealed class JobCancellation(IServiceProvider services)
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();
    public void Register(Guid syllabusId, CancellationTokenSource cts) => _running[syllabusId] = cts;
    public void Unregister(Guid syllabusId) => _running.TryRemove(syllabusId, out _);
    public void Cancel(Guid syllabusId)
    {
        // in-flight AI calls for this scope are cut off and their budget hold released (no-op when no AI module is registered)
        (services.GetService(typeof(Tether.SharedKernel.Ai.IAiScopeCutoff)) as Tether.SharedKernel.Ai.IAiScopeCutoff)?.Cancel(Tether.SharedKernel.Ai.AiScopeKinds.Syllabus, syllabusId);
        if (_running.TryGetValue(syllabusId, out var c)) { try { c.Cancel(); } catch (ObjectDisposedException) { } }
    }
}

/// <summary>Reads a JSON request body with a streaming cap and the strict serializer settings of API_CONTRACTS_P2 section 2.1.</summary>
internal static class JsonBody
{
    /// <summary>
    /// AllowDuplicateProperties=false is set on the options used for typed binding itself (not only JsonDocumentOptions): with default options a
    /// duplicate key in a Dictionary (itemEtags, itemRevisions) is silently collapsed, last one wins. Unmapped members (read-only fields, unknown
    /// names) are rejected. Nullable annotations are respected so a null for a non-nullable member is a binding error.
    /// </summary>
    public static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 16,
    };

    public static readonly JsonSerializerOptions Out = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default,
    };

    public enum Failure { None, TooLarge, Unsupported, Invalid, TimedOut }

    /// <summary>Exactly the media type application/json (parameters such as charset allowed). application/merge-patch+json and application/json-patch+json are NOT accepted.</summary>
    public static bool IsJson(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        var semi = contentType.IndexOf(';');
        return string.Equals((semi < 0 ? contentType : contentType[..semi]).Trim(), "application/json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads at most <paramref name="cap"/> bytes (counted while reading; Content-Length is never trusted). With <paramref name="readTimeout"/>,
    /// a body that is not complete in time is <see cref="Failure.TimedOut"/> (SBR-07); the request's own cancellation still propagates.
    /// </summary>
    public static async Task<(byte[]? Bytes, Failure Failure)> ReadBytesAsync(HttpContext http, int cap, CancellationToken ct, TimeSpan? readTimeout = null)
    {
        if (!IsJson(http.Request.ContentType)) return (null, Failure.Unsupported);
        if (http.Request.ContentLength is { } cl && cl > cap) return (null, Failure.TooLarge);
        using var deadline = readTimeout.HasValue ? CancellationTokenSource.CreateLinkedTokenSource(ct) : null;
        if (deadline is not null) deadline.CancelAfter(readTimeout!.Value);
        var token = deadline?.Token ?? ct;
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        try
        {
            int n;
            while ((n = await http.Request.Body.ReadAsync(buffer, token)) > 0)
            {
                if (ms.Length + n > cap) return (null, Failure.TooLarge);
                ms.Write(buffer, 0, n);
            }
        }
        catch (BadHttpRequestException b) when (b.StatusCode == 413) { return (null, Failure.TooLarge); }
        catch (OperationCanceledException) when (deadline is not null && !ct.IsCancellationRequested) { return (null, Failure.TimedOut); }
        // Kestrel's minimum data rate aborts a too-slow body with 408 (BadHttpRequestException); that is the same slow-body outcome
        catch (BadHttpRequestException b) when (b.StatusCode == 408 && deadline is not null) { return (null, Failure.TimedOut); }
        return (ms.ToArray(), Failure.None);
    }

    /// <summary>Typed binding. EVERY decoding or binding failure (lone surrogate, invalid UTF-8, CESU-8, wrong type, unknown or duplicate member) is Invalid, never an exception.</summary>
    public static bool TryBind<T>(byte[] body, out T? value) where T : class
    {
        value = null;
        if (body.Length == 0) return false;
        try { value = JsonSerializer.Deserialize<T>(body, Strict); return value is not null; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or DecoderFallbackException or ArgumentException or FormatException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Merge-patch / map style binding of a flat object: member name to raw JSON element (duplicates rejected).</summary>
    public static bool TryBindMembers(byte[] body, out Dictionary<string, JsonElement>? members)
    {
        members = null;
        if (!TryBind<Dictionary<string, JsonElement>>(body, out var m) || m is null) return false;
        members = m;
        return true;
    }
}
