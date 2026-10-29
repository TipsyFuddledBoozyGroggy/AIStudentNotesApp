using System.Net;
using System.Net.Sockets;

namespace Tether.Host;

/// <summary>Validation of edge-facing configuration (M1R-N01, M1R-N02). All checks run at startup and name the offending key, never a secret.</summary>
internal static class EdgeConfig
{
    /// <summary>Narrowest-allowed breadth of a trusted proxy network: anything wider than a /8 (IPv4) or /32 (IPv6) is effectively "everyone".</summary>
    public const int MinIPv4Prefix = 8;
    public const int MinIPv6Prefix = 32;

    /// <summary>
    /// Parses <c>Security:TrustedProxies</c> (single addresses) and <c>Security:TrustedProxyNetworks</c> (CIDR). Refuses malformed entries, unspecified addresses
    /// and networks so wide that any client could pose as a trusted proxy and choose its own X-Forwarded-For (which also defeats per-IP limits).
    /// </summary>
    public static (IPAddress[] Proxies, IPNetwork[] Networks) ParseTrustedProxies(string[] proxies, string[] networks)
    {
        var addrs = new List<IPAddress>();
        for (var i = 0; i < proxies.Length; i++)
        {
            var raw = proxies[i];
            if (string.IsNullOrWhiteSpace(raw) || raw.Contains('/') || !IPAddress.TryParse(raw.Trim(), out var ip))
                throw new InvalidOperationException($"Security:TrustedProxies:{i} must be a single IP address (use Security:TrustedProxyNetworks for ranges).");
            if (IPAddress.Any.Equals(ip) || IPAddress.IPv6Any.Equals(ip) || IPAddress.Broadcast.Equals(ip) || IPAddress.None.Equals(ip))
                throw new InvalidOperationException($"Security:TrustedProxies:{i} must not be an unspecified or broadcast address.");
            addrs.Add(ip);
        }
        var nets = new List<IPNetwork>();
        for (var i = 0; i < networks.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(networks[i]) || !IPNetwork.TryParse(networks[i].Trim(), out var net))
                throw new InvalidOperationException($"Security:TrustedProxyNetworks:{i} must be a CIDR network such as 10.0.0.0/8.");
            // An IPv4-mapped IPv6 network (::ffff:a.b.c.d/n) is an IPv4 network of n - 96 bits.
            var mapped = net.BaseAddress.IsIPv4MappedToIPv6;
            var isV4 = net.BaseAddress.AddressFamily == AddressFamily.InterNetwork || mapped;
            var effective = mapped ? net.PrefixLength - 96 : net.PrefixLength;
            var min = isV4 ? MinIPv4Prefix : MinIPv6Prefix;
            if (effective < min)
                throw new InvalidOperationException($"Security:TrustedProxyNetworks:{i} is too wide (minimum prefix /{min}): trusting it would let any client set X-Forwarded-For.");
            nets.Add(net);
        }
        return ([.. addrs], [.. nets]);
    }

    /// <summary>"*" (or nothing) accepts any Host header, which enables host-header attacks; only Development may do that.</summary>
    public static void EnsureAllowedHosts(string? value, bool isDevelopment)
    {
        if (isDevelopment) return;
        var entries = (value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0 || entries.Any(e => e == "*"))
            throw new InvalidOperationException("AllowedHosts must list the public host name(s) (for example \"api.example.org\") outside Development; \"*\" or an empty value accepts any Host header.");
    }
}
