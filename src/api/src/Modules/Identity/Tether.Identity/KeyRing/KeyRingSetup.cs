using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Tether.Identity.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.Identity.KeyRing;

/// <summary>
/// Cookie key ring (ADR-022 R4, closes M1-F07). Keys live in PostgreSQL (<c>identity.data_protection_keys</c>) so sign-ins survive restarts and
/// redeploys. Each key's secret is wrapped with the built-in <c>ProtectKeysWithCertificate</c>: a database copy or backup alone cannot mint a
/// cookie. <c>UnprotectKeysWithAnyCertificate</c> lists the current and any previous certificate, so a rotation keeps existing keys readable.
/// No hand-written crypto. Development may use a plain directory (<c>DataProtection:KeysPath</c>) or the database without a certificate.
/// Messages name the configuration key, never a value.
/// </summary>
internal static class KeyRingSetup
{
    public const string CertificateKey = "DataProtection:Certificate";
    public const string PreviousCertificatesKey = "DataProtection:PreviousCertificates";
    public const string KeysPathKey = "DataProtection:KeysPath";
    public const int MinRsaBits = 2048;

    public static void Configure(IServiceCollection services, IConfiguration config, bool isDevelopment, ICollection<string> warnings)
    {
        var dp = services.AddDataProtection().SetApplicationName("Tether");
        var keysPath = config[KeysPathKey];
        if (isDevelopment && !string.IsNullOrWhiteSpace(keysPath))
            dp.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        else
        {
            dp.PersistKeysToDbContext<IdentityDbContext>();
            if (!string.IsNullOrWhiteSpace(keysPath))
                warnings.Add($"{KeysPathKey} is ignored outside Development: the key ring is stored in the database, wrapped with {CertificateKey}.");
        }

        var current = LoadOptional(config[CertificateKey], CertificateKey);
        if (current is null)
        {
            if (!isDevelopment)
                throw new InvalidOperationException($"{CertificateKey} is required outside Development: a base64 PKCS#12 (PFX) certificate with an RSA private key of at least {MinRsaBits} bits, without a password, from the managed secret store.");
            return;
        }
        var previous = config.GetSection(PreviousCertificatesKey).GetChildren()
            .Where(c => !string.IsNullOrWhiteSpace(c.Value))
            .Select(c => Load(c.Value, $"{PreviousCertificatesKey}:{c.Key}"))
            .ToArray();
        dp.ProtectKeysWithCertificate(current);
        dp.UnprotectKeysWithAnyCertificate([current, .. previous]);
    }

    private static X509Certificate2? LoadOptional(string? base64, string key) => string.IsNullOrWhiteSpace(base64) ? null : Load(base64, key);

    /// <summary>Loads a password-less PFX from base64. Refuses anything without an RSA private key of at least <see cref="MinRsaBits"/> bits.</summary>
    internal static X509Certificate2 Load(string? base64, string key)
    {
        byte[] raw;
        try { raw = Convert.FromBase64String((base64 ?? "").Trim()); }
        catch (FormatException) { throw new InvalidOperationException($"{key} is not valid base64."); }
        X509Certificate2 cert;
        try
        {
            // Ephemeral: the private key stays in memory (the container file system is not a key store). Proven on Linux by KeyRingTests.
            // macOS does not support it. On Windows (a developer who sets a certificate locally) the combination with XML decryption is
            // UNVERIFIED; Development normally runs without a certificate.
            var flags = OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
            cert = X509CertificateLoader.LoadPkcs12(raw, password: null, flags);
        }
        catch (CryptographicException) { throw new InvalidOperationException($"{key} is not a password-less PKCS#12 (PFX) certificate."); }
        if (!cert.HasPrivateKey)
            throw new InvalidOperationException($"{key} has no private key.");
        using (var rsa = cert.GetRSAPrivateKey())
        {
            if (rsa is null || rsa.KeySize < MinRsaBits)
                throw new InvalidOperationException($"{key} must hold an RSA private key of at least {MinRsaBits} bits.");
        }
        return cert;
    }
}
