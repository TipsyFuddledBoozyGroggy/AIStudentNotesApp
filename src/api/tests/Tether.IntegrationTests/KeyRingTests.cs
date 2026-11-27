using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tether.Identity.KeyRing;
using Tether.Identity.Persistence;

namespace Tether.IntegrationTests;

/// <summary>Test certificates in the format the PO script writes (ADR-022 R4, C1): base64 PKCS#12, no password, RSA.</summary>
public static class TestCertificates
{
    private static readonly Lazy<string> CurrentLazy = new(() => NewPfxBase64(2048, "tether-dp-test-current"));
    private static readonly Lazy<string> OtherLazy = new(() => NewPfxBase64(2048, "tether-dp-test-other"));
    public static string Current => CurrentLazy.Value;
    public static string Other => OtherLazy.Value;

    public static string NewPfxBase64(int bits, string cn, string? password = null, DateTimeOffset? notAfter = null)
    {
        using var rsa = RSA.Create(bits);
        var req = new CertificateRequest($"CN={cn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var from = DateTimeOffset.UtcNow.AddDays(-1);
        using var cert = req.CreateSelfSigned(from, notAfter ?? from.AddYears(10));
        return Convert.ToBase64String(cert.Export(X509ContentType.Pkcs12, password));
    }

    public static string PublicOnlyBase64(string pfxBase64)
    {
        using var full = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(pfxBase64), null);
        using var pub = X509CertificateLoader.LoadCertificate(full.Export(X509ContentType.Cert));
        return Convert.ToBase64String(pub.Export(X509ContentType.Pkcs12));
    }
}

/// <summary>
/// ADR-022 R4 time box: the built-in certificate protection of the database key ring works on this (Linux) runner. Each test builds a bare
/// Data Protection stack over its own throwaway database, so "restart" means a brand-new service provider over the same rows.
/// </summary>
public sealed class KeyRingTests : IAsyncLifetime
{
    private readonly string _admin = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? throw new InvalidOperationException("ConnectionStrings__Default must point at the compose postgres.");
    private readonly string _db = "cc_test_" + Guid.NewGuid().ToString("N");
    private string Cs => new NpgsqlConnectionStringBuilder(_admin) { Database = _db, Pooling = false }.ConnectionString;

    public async Task InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_admin) { Pooling = false }.ConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = new NpgsqlCommand($"create database \"{_db}\"", admin);
            await cmd.ExecuteNonQueryAsync();
        }
        await using var sp = Provider(null, isDevelopment: true);
        await sp.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_admin) { Pooling = false }.ConnectionString);
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand($"drop database if exists \"{_db}\" with (force)", admin);
        await cmd.ExecuteNonQueryAsync();
    }

    private ServiceProvider Provider(string? certificate, bool isDevelopment = false, params string[] previous)
    {
        var settings = new Dictionary<string, string?>();
        if (certificate is not null) settings[KeyRingSetup.CertificateKey] = certificate;
        for (var i = 0; i < previous.Length; i++) settings[$"{KeyRingSetup.PreviousCertificatesKey}:{i}"] = previous[i];
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<IdentityDbContext>(o => o.UseNpgsql(Cs, n => n.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.Schema)));
        KeyRingSetup.Configure(services, config, isDevelopment, new List<string>());
        return services.BuildServiceProvider();
    }

    private static IDataProtector Protector(IServiceProvider sp) => sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("keyring-test");

    private async Task<List<string>> StoredXmlAsync()
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select xml from identity.data_protection_keys", conn);
        var list = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(r.GetString(0));
        return list;
    }

    [Fact]
    public async Task A_payload_protected_before_a_restart_unprotects_after_it_and_the_stored_key_is_wrapped_by_the_certificate()
    {
        string protectedPayload;
        await using (var first = Provider(TestCertificates.Current))
            protectedPayload = Protector(first).Protect("session pointer");

        var xml = Assert.Single(await StoredXmlAsync());
        Assert.Contains("EncryptedData", xml, StringComparison.Ordinal);   // XML encryption with the certificate
        Assert.DoesNotContain("<value>", xml, StringComparison.Ordinal);   // the plain master-key element never reaches the table

        await using var second = Provider(TestCertificates.Current);
        Assert.Equal("session pointer", Protector(second).Unprotect(protectedPayload));
        Assert.Single(await StoredXmlAsync()); // the restarted stack reused the key instead of minting a new one
    }

    [Fact]
    public async Task Without_the_certificate_the_stored_key_cannot_be_used()
    {
        string protectedPayload;
        await using (var first = Provider(TestCertificates.Current))
            protectedPayload = Protector(first).Protect("session pointer");

        await using var wrong = Provider(TestCertificates.Other);
        Assert.ThrowsAny<CryptographicException>(() => Protector(wrong).Unprotect(protectedPayload));
    }

    [Fact]
    public async Task After_a_rotation_the_previous_certificate_still_unwraps_existing_keys()
    {
        string protectedPayload;
        await using (var first = Provider(TestCertificates.Current))
            protectedPayload = Protector(first).Protect("session pointer");

        await using var rotated = Provider(TestCertificates.Other, false, TestCertificates.Current);
        Assert.Equal("session pointer", Protector(rotated).Unprotect(protectedPayload));
    }

    [Fact]
    public async Task An_expired_certificate_still_unwraps_and_wraps_keys()
    {
        // Pinned behaviour: Data Protection does not check certificate validity dates, so an expired certificate does not sign anyone out.
        var expired = TestCertificates.NewPfxBase64(2048, "tether-dp-test-expired", notAfter: DateTimeOffset.UtcNow.AddMinutes(-5));
        string protectedPayload;
        await using (var first = Provider(expired))
            protectedPayload = Protector(first).Protect("session pointer");
        await using var second = Provider(expired);
        Assert.Equal("session pointer", Protector(second).Unprotect(protectedPayload));
    }

    [Fact]
    public void Outside_development_a_missing_certificate_refuses_to_start()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Provider(null, isDevelopment: false));
        Assert.Contains(KeyRingSetup.CertificateKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_without_a_certificate_keeps_working_with_plain_database_keys()
    {
        await using var dev = Provider(null, isDevelopment: true);
        var p = Protector(dev);
        Assert.Equal("x", p.Unprotect(p.Protect("x")));
    }

    [Theory]
    [InlineData("not base64 at all!", "not valid base64")]
    [InlineData("AAAA", "not a password-less PKCS#12")]
    public void Malformed_certificates_are_refused_with_the_key_name_only(string value, string expected)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => KeyRingSetup.Load(value, KeyRingSetup.CertificateKey));
        Assert.Contains(KeyRingSetup.CertificateKey, ex.Message, StringComparison.Ordinal);
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_certificate_without_its_private_key_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => KeyRingSetup.Load(TestCertificates.PublicOnlyBase64(TestCertificates.Current), KeyRingSetup.CertificateKey));
        Assert.Contains("no private key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_rsa_key_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => KeyRingSetup.Load(TestCertificates.NewPfxBase64(1024, "tether-dp-test-short"), KeyRingSetup.CertificateKey));
        Assert.Contains("at least 2048 bits", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pfx_exported_with_an_empty_password_loads_like_one_without()
    {
        using var cert = KeyRingSetup.Load(TestCertificates.NewPfxBase64(2048, "tether-dp-test-empty-pw", password: ""), KeyRingSetup.CertificateKey);
        Assert.True(cert.HasPrivateKey);
    }

    [Fact]
    public void A_pfx_with_a_real_password_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => KeyRingSetup.Load(TestCertificates.NewPfxBase64(2048, "tether-dp-test-pw", password: "pw-example"), KeyRingSetup.CertificateKey));
        Assert.Contains("password-less", ex.Message, StringComparison.Ordinal);
    }
}
