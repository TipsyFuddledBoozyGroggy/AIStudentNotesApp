using System.Net;
using System.Net.Sockets;

namespace Tether.Host;

/// <summary>
/// Rate-limit partition key for a client address (N-04). IPv4 is keyed per address; IPv6 per /64, because a single
/// subscriber is typically delegated a whole /64 and could otherwise mint unlimited buckets.
/// </summary>
public static class ClientIpKey
{
    public static string For(IPAddress? ip)
    {
        if (ip is null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip.ToString();
        Span<byte> b = stackalloc byte[16];
        ip.TryWriteBytes(b, out _);
        b[8..].Clear();
        return new IPAddress(b).ToString() + "/64";
    }
}
