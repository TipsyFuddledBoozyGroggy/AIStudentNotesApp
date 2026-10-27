using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Tether.SharedKernel.Http;

/// <summary>RFC 9457 body with the contract extensions (<c>code</c>, <c>traceId</c>, <c>errors</c>).</summary>
public sealed class ApiProblemBody
{
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required int Status { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Detail { get; init; }
    public required string Code { get; init; }
    public string? TraceId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
}

/// <summary>Writes application/problem+json. Never includes stack traces, tokens or request content.</summary>
public sealed class ApiProblemResult(int status, string code, string title, string? detail = null,
    IReadOnlyDictionary<string, string[]>? errors = null) : IResult
{
    public const string TypeBase = "https://errors.tether.invalid/";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task ExecuteAsync(HttpContext httpContext)
    {
        var body = new ApiProblemBody
        {
            Type = TypeBase + code, Title = title, Status = status, Detail = detail, Code = code,
            TraceId = httpContext.TraceIdentifier, Errors = errors,
        };
        httpContext.Response.StatusCode = status;
        return httpContext.Response.WriteAsJsonAsync(body, Json, "application/problem+json");
    }
}

public static class Problems
{
    public static IResult Unauthenticated() => new ApiProblemResult(401, "unauthenticated", "Please sign in.");
    public static IResult NotFound(string title = "Resource not found.") => new ApiProblemResult(404, "not_found", title);
    public static IResult Validation(IReadOnlyDictionary<string, string[]> errors, string title = "One or more fields are invalid.")
        => new ApiProblemResult(400, "validation_failed", title, errors: errors);
    public static IResult Validation(string field, string message) =>
        Validation(new Dictionary<string, string[]> { [field] = [message] }, message);
    public static IResult CsrfFailed() => new ApiProblemResult(403, "csrf_failed", "Request origin was not accepted.");
    public static IResult PreconditionRequired() => new ApiProblemResult(428, "precondition_required", "An If-Match header is required.");
    public static IResult PreconditionFailed() => new ApiProblemResult(412, "precondition_failed", "The resource has changed. Reload and try again.");
    public static IResult ConcurrencyConflict() => new ApiProblemResult(409, "concurrency_conflict", "The resource was changed by another request.");

    /// <summary>Generic problem for framework-generated status codes (404 unknown route, 405, 429, ...).</summary>
    public static IResult ForStatus(int status) => status switch
    {
        400 => new ApiProblemResult(400, "validation_failed", "The request was invalid."),
        401 => Unauthenticated(),
        403 => new ApiProblemResult(403, "forbidden", "You do not have access."),
        404 => NotFound(),
        405 => new ApiProblemResult(405, "method_not_allowed", "Method not allowed."),
        413 => new ApiProblemResult(413, "payload_too_large", "The request body is too large."),
        415 => new ApiProblemResult(415, "unsupported_media_type", "Unsupported media type."),
        429 => new ApiProblemResult(429, "rate_limited", "Too many requests. Please slow down."),
        _ => new ApiProblemResult(status, "error", "The request could not be completed."),
    };
}
