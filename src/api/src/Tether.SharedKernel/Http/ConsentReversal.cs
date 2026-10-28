using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Tether.SharedKernel.Http;

/// <summary>
/// Endpoint metadata marking a route as a consent reversal (Pause or Stop, ADR-019b). The host's rate limiter reads it to put the
/// caller's own reversals in a separate, generous per-user bucket instead of the general and write budgets, so a student can never be
/// locked out of stopping a recording, while an abuser is still bounded (N-03). Metadata rather than a path pattern, so renaming a
/// route cannot silently widen or drop the exemption.
/// </summary>
public sealed class ConsentReversalMetadata
{
    public static readonly ConsentReversalMetadata Instance = new();
    private ConsentReversalMetadata() { }
}

public static class ConsentReversalExtensions
{
    public static TBuilder AsConsentReversal<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(ConsentReversalMetadata.Instance);

    /// <summary>True for a POST that was routed to an endpoint marked <see cref="ConsentReversalMetadata"/>. Needs routing to have run.</summary>
    public static bool IsConsentReversal(this HttpContext ctx) =>
        HttpMethods.IsPost(ctx.Request.Method) && ctx.GetEndpoint()?.Metadata.GetMetadata<ConsentReversalMetadata>() is not null;
}
