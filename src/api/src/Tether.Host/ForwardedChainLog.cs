using System.Net;
using System.Net.Sockets;

namespace Tether.Host;

/// <summary>
/// ADR-022 R6 measurement for Run A: what the forwarded-header chain in front of the API LOOKS like, so the Lead can rule on which hop to trust
/// before anyone but the Product Owner is invited. Logged for the first <see cref="StartupSamples"/> non-health requests after start, then at
/// most once per <see cref="Interval"/>. Only the SHAPE is logged: how many X-Forwarded-For entries there are and, for each, whether it is
/// private or public and IPv4 or IPv6 (or unparsable), plus the same for the connecting peer and whether X-Forwarded-Proto and
/// X-Forwarded-Host are present. Never an address, a host name or a header value. On by default outside Development
/// (<c>Diagnostics:ForwardedChainLog:Enabled</c>); it reads headers only and changes nothing about how the request is handled.
/// </summary>
internal sealed class ForwardedChainLog(TimeProvider clock, ILogger<ForwardedChainLog> logger)
{
    public const string EnabledKey = "Diagnostics:ForwardedChainLog:Enabled";
    public const int StartupSamples = 20;
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    public const int MaxEntries = 16;

    private readonly Lock _gate = new();
    private int _samples;
    private DateTimeOffset _nextAt = DateTimeOffset.MinValue;

    /// <summary>True for the first 20 calls, then once per hour.</summary>
    public bool ShouldLog()
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (_samples < StartupSamples)
            {
                _samples++;
                if (_samples == StartupSamples) _nextAt = now + Interval;
                return true;
            }
            if (now < _nextAt) return false;
            _nextAt = now + Interval;
            return true;
        }
    }

    public void Observe(HttpContext ctx)
    {
        if (ctx.Request.Path.StartsWithSegments("/health") || !ShouldLog()) return;
        var entries = ctx.Request.Headers["X-Forwarded-For"]
            .SelectMany(v => (v ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToList();
        var shape = string.Join(",", entries.Take(MaxEntries).Select(Classify)) + (entries.Count > MaxEntries ? ",..." : "");
        logger.LogInformation(
            "Forwarded chain (ADR-022 R6 measurement): {EntryCount} X-Forwarded-For entries [{Shape}]; connecting peer {Peer}; X-Forwarded-Proto present: {HasProto}; X-Forwarded-Host present: {HasHost}",
            entries.Count, shape, Classify(ctx.Connection.RemoteIpAddress),
            ctx.Request.Headers.ContainsKey("X-Forwarded-Proto"), ctx.Request.Headers.ContainsKey("X-Forwarded-Host"));
    }

    /// <summary>"private-v4", "public-v6", ... or "unparsable". Ports and brackets are accepted ("1.2.3.4:5678", "[2001:db8::1]:443").</summary>
    internal static string Classify(string raw)
    {
        var s = raw.Trim();
        if (IPAddress.TryParse(s, out var ip)) return Classify(ip);
        return IPEndPoint.TryParse(s, out var ep) ? Classify(ep.Address) : "unparsable";
    }

    internal static string Classify(IPAddress? ip)
    {
        if (ip is null) return "none";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            var isPrivate = b[0] == 10 || b[0] == 127 || b[0] == 0
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127); // shared address space (carrier-grade NAT), used inside cloud networks
            return (isPrivate ? "private" : "public") + "-v4";
        }
        var isPrivate6 = IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal || ip.Equals(IPAddress.IPv6None);
        return (isPrivate6 ? "private" : "public") + "-v6";
    }
}
