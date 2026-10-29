using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Tether.SharedKernel.Security;

/// <summary>
/// Purpose keys derived from the one per-environment root secret with HKDF-SHA256 (docs/security/google-tokens-and-data-residency.md 1.3, B1-11).
/// Holds key material: never log it, never put it in a message.
/// </summary>
public sealed class DerivedKeys
{
    /// <summary>Fixed, versioned HKDF "info" labels. A new version of one label does not change the others.</summary>
    public static class Labels
    {
        public const string ETag = "etag.v1";
        public const string Idempotency = "idempotency.v1";
        public const string SyllabusAudit = "syllabus-audit.v1";
        public const string OAuthState = "oauth-state.v1";
    }

    private readonly byte[] _root;
    internal DerivedKeys(byte[] root, IReadOnlyList<string> warnings) { _root = root; Warnings = warnings; }

    /// <summary>Startup warnings to log once the host has a logger. Never contain key material.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>A fresh 32-byte key for <paramref name="label"/>. Deterministic for the same root secret and label.</summary>
    public byte[] Derive(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("A derivation label is required.", nameof(label));
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, _root, 32, salt: null, info: Encoding.UTF8.GetBytes(label));
    }
}

/// <summary>
/// Resolves <c>Security:RootSecret</c> (base64, at least 32 bytes decoded). Outside Development the host fails to start when it is missing,
/// too short, not base64, or a known sample/placeholder value. Only the environment named exactly "Development" may generate one
/// (B1-13: no "Test"/"Testing" escape); the generated dev secret is persisted so ETags and idempotency keys survive a restart.
/// </summary>
public static class RootSecret
{
    public const string ConfigKey = "Security:RootSecret";
    public const string DevPathConfigKey = "Security:DevRootSecretPath";
    public const int MinBytes = 32;

    /// <summary>Documented sample values that must never be used outside Development (base64). Plus any single repeated byte and any text containing a placeholder word.</summary>
    public static IReadOnlyList<string> KnownSampleValues { get; } =
    [
        Convert.ToBase64String(new byte[32]),
        Convert.ToBase64String(Enumerable.Repeat((byte)0xFF, 32).ToArray()),
        Convert.ToBase64String(Encoding.ASCII.GetBytes("00000000000000000000000000000000")),
        Convert.ToBase64String(Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef")),
        Convert.ToBase64String(Encoding.ASCII.GetBytes("changeme-changeme-changeme-chang")),
        Convert.ToBase64String(Encoding.ASCII.GetBytes("REPLACE_ME_REPLACE_ME_REPLACE_ME_")),
    ];

    private static readonly string[] PlaceholderWords = ["changeme", "change-me", "change_me", "replace", "sample", "example", "placeholder", "devsecret", "dev-secret", "password", "secret"];

    public static DerivedKeys Resolve(IConfiguration config, IHostEnvironment env)
    {
        var configured = config[ConfigKey];
        if (!string.IsNullOrWhiteSpace(configured))
            return new DerivedKeys(Validate(configured.Trim()), []);

        if (!env.IsDevelopment())
            throw new InvalidOperationException($"{ConfigKey} is required outside Development: set it to a random secret of at least {MinBytes} bytes, base64 encoded, from the managed secret store (one per environment).");

        var path = config[DevPathConfigKey];
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tether", "dev-root-secret");
        var key = LoadOrCreateDevSecret(path);
        return new DerivedKeys(key, [$"{ConfigKey} is not set: using a generated development secret persisted outside the repository ({path}). Set {ConfigKey} for any shared or deployed environment; it is required outside Development."]);
    }

    private static byte[] Validate(string base64)
    {
        byte[] key;
        try { key = Convert.FromBase64String(base64); }
        catch (FormatException) { throw new InvalidOperationException($"{ConfigKey} is not valid base64."); }
        if (key.Length < MinBytes)
            throw new InvalidOperationException($"{ConfigKey} must decode to at least {MinBytes} bytes (got {key.Length}).");
        if (IsSample(base64, key))
            throw new InvalidOperationException($"{ConfigKey} is a known sample or placeholder value. Generate a random secret (for example `openssl rand -base64 48`).");
        return key;
    }

    private static bool IsSample(string base64, byte[] key)
    {
        if (KnownSampleValues.Contains(base64, StringComparer.Ordinal)) return true;
        if (key.All(b => b == key[0])) return true;
        var text = Encoding.UTF8.GetString(key).ToLowerInvariant();
        return PlaceholderWords.Any(w => text.Contains(w, StringComparison.Ordinal));
    }

    private static byte[] LoadOrCreateDevSecret(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (File.Exists(path))
            {
                try { return Validate(File.ReadAllText(path).Trim()); }
                catch (InvalidOperationException) { throw new InvalidOperationException($"The persisted development secret at {path} is invalid; delete the file to regenerate it."); }
                catch (IOException) { Thread.Sleep(20); continue; }
            }
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                var key = RandomNumberGenerator.GetBytes(48);
                using (var fs = new FileStream(path, options)) fs.Write(Encoding.ASCII.GetBytes(Convert.ToBase64String(key)));
                return key;
            }
            catch (IOException) { /* another process won the race: read its file on the next pass */ }
        }
        throw new InvalidOperationException($"Could not create or read the development secret at {path}.");
    }
}

public static class RootSecretExtensions
{
    /// <summary>Resolves the root secret at builder time (so a bad or missing secret stops startup) and registers <see cref="DerivedKeys"/>.</summary>
    public static Microsoft.AspNetCore.Builder.WebApplicationBuilder AddRootSecret(this Microsoft.AspNetCore.Builder.WebApplicationBuilder builder)
    {
        var keys = RootSecret.Resolve(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(keys);
        return builder;
    }
}
