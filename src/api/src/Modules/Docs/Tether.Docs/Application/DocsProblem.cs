using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Tether.Docs.Application;

/// <summary>
/// RFC 9457 problems for docs.export.v1 (docs-problem.schema.json). <c>detail</c> is a FIXED string per code: never exception text, provider text,
/// a title or notes text. <c>errors</c> keys are field names and values come from a closed vocabulary that never echoes the rejected value.
/// </summary>
internal sealed class DocsProblem(int status, string code, int? retryAfterSeconds = null, IReadOnlyDictionary<string, string[]>? errors = null, string? textKey = null) : IResult
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public const string TypeBase = "https://errors.tether.invalid/";
    private const string MediaTypeText = "validation_failed:media";

    private static readonly Dictionary<string, (string Title, string Detail)> Fixed = new()
    {
        ["validation_failed"] = ("One or more fields are invalid.", "The request was invalid."),
        ["unauthenticated"] = ("Please sign in.", "Please sign in."),
        ["csrf_failed"] = ("Request origin was not accepted.", "Request origin was not accepted."),
        ["not_found"] = ("Resource not found.", "Resource not found."),
        ["invalid_state"] = ("Not available in this state.", "This action is not available in the current state."),
        ["precondition_failed"] = ("The resource has changed. Reload and try again.", "The resource has changed. Reload and try again."),
        ["rate_limited"] = ("Too many requests.", "Too many requests. Please slow down."),
        ["provider_unavailable"] = ("Service unavailable.", "A required service is temporarily unavailable."),
        ["too_large"] = ("The request was too large.", "The request was too large."),
        ["retry_limit_reached"] = ("Too many attempts.", "Too many attempts. Start again."),
        // 415 has no code of its own in docs-problem#/code (contract gap, reported to the Lead): same wire code as 400, neutral fixed wording.
        [MediaTypeText] = ("Unsupported media type.", "The content type of the request is not supported."),
    };

    public int Status => status;
    public string Code => code;

    public Task ExecuteAsync(HttpContext http)
    {
        var (title, detail) = Fixed.TryGetValue(textKey ?? code, out var f) ? f : ("Error.", "The request could not be completed.");
        var body = new Body
        {
            Type = TypeBase + code, Title = title, Status = status, Detail = detail, Code = code,
            TraceId = http.TraceIdentifier, Errors = errors, RetryAfterSeconds = retryAfterSeconds,
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
    }

    public static IResult NotFound() => new DocsProblem(404, "not_found");
    public static IResult Invalid(string field, string reason = "invalid") =>
        new DocsProblem(400, "validation_failed", errors: new Dictionary<string, string[]> { [SafeKey(field)] = [reason] });
    public static IResult InvalidBare() => new DocsProblem(400, "validation_failed");
    public static IResult InvalidState() => new DocsProblem(409, "invalid_state");
    public static IResult RetryLimit() => new DocsProblem(409, "retry_limit_reached");
    public static IResult PreconditionFailed() => new DocsProblem(412, "precondition_failed");
    public static IResult TooLarge() => new DocsProblem(413, "too_large");
    public static IResult UnsupportedMediaType() => new DocsProblem(415, "validation_failed", textKey: MediaTypeText);
    public static IResult Unavailable() => new DocsProblem(503, "provider_unavailable", retryAfterSeconds: 5);
    public static IResult Csrf() => new DocsProblem(403, "csrf_failed");

    private static string SafeKey(string field)
    {
        if (field.Length is >= 1 and <= 64 && char.IsAsciiLetter(field[0])
            && field.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.' or '[' or ']' or '-')) return field;
        return "request";
    }
}
