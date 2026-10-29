using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Tether.Syllabus.Application;

/// <summary>
/// RFC 9457 problems for syllabus.v1 (syllabus-problem.schema.json). <c>detail</c> is a FIXED string per code: never exception text,
/// model output, document content, a filename or a detected file type. <c>errors</c> keys are field names and values come from a
/// closed vocabulary that never echoes the rejected value (API_CONTRACTS_P2 section 2 and 9).
/// </summary>
internal sealed class SyllabusProblem(int status, string code, string? existingSyllabusId = null, int? retryAfterSeconds = null,
    IReadOnlyDictionary<string, string[]>? errors = null) : IResult
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public const string TypeBase = "https://errors.tether.invalid/";

    private static readonly Dictionary<string, (string Title, string Detail)> Fixed = new()
    {
        ["validation_failed"] = ("One or more fields are invalid.", "The request was invalid."),
        ["unauthenticated"] = ("Please sign in.", "Please sign in."),
        ["csrf_failed"] = ("Request origin was not accepted.", "Request origin was not accepted."),
        ["not_found"] = ("Resource not found.", "Resource not found."),
        ["invalid_state"] = ("Not available in this state.", "This action is not available in the current state."),
        ["concurrency_conflict"] = ("Changed by another request.", "The resource was changed by another request."),
        ["precondition_required"] = ("An If-Match header is required.", "An If-Match header is required."),
        ["precondition_failed"] = ("The resource has changed.", "The resource has changed. Reload and try again."),
        ["rate_limited"] = ("Too many requests.", "Too many requests. Please slow down."),
        ["provider_unavailable"] = ("Service unavailable.", "A required service is temporarily unavailable."),
        ["unsupported_type"] = ("Unsupported file type.", "The content is not a PDF, DOCX or plain text file."),
        ["too_large"] = ("Too large.", "The request is over the size, page or character limit."),
        ["duplicate_syllabus"] = ("Duplicate syllabus.", "This syllabus is already in the course."),
        ["extraction_in_progress"] = ("Extraction in progress.", "An extraction is already running."),
        ["syllabus_limit_reached"] = ("Syllabus limit reached.", "The syllabus limit was reached."),
        ["item_needs_date"] = ("Item needs a date.", "The item has no date yet."),
        ["item_not_syncable"] = ("Item cannot be used here.", "The item cannot be approved, synced or changed this way."),
        ["item_not_approved"] = ("Item not approved.", "The item is not approved."),
        ["acknowledgement_required"] = ("Acknowledgement required.", "Check the item and confirm before approving."),
        ["batch_not_eligible"] = ("Not eligible for batch approval.", "The item is not eligible for batch approval."),
        ["student_item_only"] = ("Student items only.", "Only items you added can be deleted."),
    };

    public static string DetailFor(string code) => Fixed.TryGetValue(code, out var f) ? f.Detail : "The request could not be completed.";

    public int Status => status;
    public string Code => code;

    public Task ExecuteAsync(HttpContext http)
    {
        var (title, detail) = Fixed.TryGetValue(code, out var f) ? f : ("Error.", "The request could not be completed.");
        var body = new Body
        {
            Type = TypeBase + code, Title = title, Status = status, Detail = detail, Code = code,
            TraceId = http.TraceIdentifier, Errors = errors, RetryAfterSeconds = retryAfterSeconds, ExistingSyllabusId = existingSyllabusId,
        };
        http.Response.StatusCode = status;
        if (retryAfterSeconds is { } ra) http.Response.Headers.RetryAfter = ra.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return http.Response.WriteAsJsonAsync(body, Json, "application/problem+json");
    }

    private sealed class Body
    {
        public required string Type { get; init; }
        public required string Title { get; init; }
        public required int Status { get; init; }
        public required string Detail { get; init; }
        public required string Code { get; init; }
        public required string TraceId { get; init; }
        public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
        public int? RetryAfterSeconds { get; init; }
        public string? ExistingSyllabusId { get; init; }
    }

    // ---- factories (one place, so the wire vocabulary stays closed) ----
    public static IResult NotFound() => new SyllabusProblem(404, "not_found");
    public static IResult Invalid(string field, string reason = "invalid") =>
        new SyllabusProblem(400, "validation_failed", errors: new Dictionary<string, string[]> { [SafeKey(field)] = [reason] });
    public static IResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new SyllabusProblem(400, "validation_failed", errors: errors);
    public static IResult InvalidBare() => new SyllabusProblem(400, "validation_failed");
    public static IResult PreconditionRequired() => new SyllabusProblem(428, "precondition_required");
    public static IResult PreconditionFailed() => new SyllabusProblem(412, "precondition_failed");
    public static IResult InvalidState() => new SyllabusProblem(409, "invalid_state");
    public static IResult Conflict(string code, string? existing = null) => new SyllabusProblem(409, code, existingSyllabusId: existing);
    public static IResult RateLimited(int retryAfter = 60) => new SyllabusProblem(429, "rate_limited", retryAfterSeconds: retryAfter);
    public static IResult TooLarge() => new SyllabusProblem(413, "too_large");
    public static IResult Unsupported() => new SyllabusProblem(415, "unsupported_type");
    public static IResult ProviderUnavailable() => new SyllabusProblem(503, "provider_unavailable");
    public static IResult Csrf() => new SyllabusProblem(403, "csrf_failed");

    /// <summary>Error keys must match the schema pattern; anything else is mapped to a neutral key so a hostile member name is never echoed.</summary>
    private static string SafeKey(string field)
    {
        if (field.Length is >= 1 and <= 64 && char.IsAsciiLetter(field[0])
            && field.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.' or '[' or ']' or '-')) return field;
        return "request";
    }
}
