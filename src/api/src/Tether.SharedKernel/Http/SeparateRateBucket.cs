using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Tether.SharedKernel.Http;

/// <summary>
/// Endpoint metadata that moves a route out of the general per-user and write budgets into its own named per-user bucket
/// (docs/API_CONTRACTS_P2.md section 11, R2-14): syllabus traffic, including a buggy client polling in a loop, can never use up the 30/min
/// write budget that Pause, Stop and "Quick recap" depend on. The host's limiter reads it. Metadata rather than a path pattern so a
/// renamed route cannot silently fall back into the shared budget.
/// </summary>
public sealed class SeparateRateBucketMetadata(string bucket, int permitsPerMinute)
{
    public string Bucket { get; } = bucket;
    public int PermitsPerMinute { get; } = permitsPerMinute;
}

public static class SeparateRateBucketExtensions
{
    public static TBuilder WithSeparateRateBucket<TBuilder>(this TBuilder builder, string bucket, int permitsPerMinute)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new SeparateRateBucketMetadata(bucket, permitsPerMinute));

    public static SeparateRateBucketMetadata? SeparateRateBucket(this HttpContext ctx) =>
        ctx.GetEndpoint()?.Metadata.GetMetadata<SeparateRateBucketMetadata>();
}
