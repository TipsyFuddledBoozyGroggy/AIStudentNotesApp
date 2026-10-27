using System.Security.Cryptography;
using System.Text;
using CollegeCompanion.Courses.Domain;
using Microsoft.AspNetCore.WebUtilities;

namespace CollegeCompanion.Courses.Application;

/// <summary>
/// Opaque weak ETags (F-05). The tag is HMAC-SHA256(secret, id | xmin), truncated, so clients cannot read or
/// infer PostgreSQL's global transaction counter. Without a configured <c>Security:ETagKey</c> the secret is random
/// per process: still safe, but tags do not survive a restart or span instances (clients just get 412 and refetch).
/// </summary>
internal sealed class ETagService(byte[] key)
{
    public string For(Course c)
    {
        var data = Encoding.ASCII.GetBytes($"{c.Id:N}|{c.Version}");
        var mac = HMACSHA256.HashData(key, data);
        return $"W/\"{WebEncoders.Base64UrlEncode(mac, 0, 16)}\"";
    }

    /// <summary>True for "*" or a tag (weak or strong) equal to the course's current tag.</summary>
    public bool Matches(string ifMatch, Course c)
    {
        var v = ifMatch.Trim();
        if (v == "*") return true;
        var current = For(c);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Normalize(v)), Encoding.ASCII.GetBytes(Normalize(current)));

        static string Normalize(string tag) => (tag.StartsWith("W/", StringComparison.Ordinal) ? tag[2..] : tag).Trim('"');
    }
}
