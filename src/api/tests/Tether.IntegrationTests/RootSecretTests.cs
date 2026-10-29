using System.Security.Cryptography;
using System.Text;
using Tether.IntegrationTests.Infrastructure;
using Tether.SharedKernel.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tether.IntegrationTests;

/// <summary>B1-11 / docs/security/google-tokens-and-data-residency.md section 1.3: one root secret per environment, HKDF-SHA256 sub-keys, fail closed outside Development.</summary>
public class RootSecretTests
{
    private sealed class Env(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Cfg(params (string, string?)[] kv) =>
        new ConfigurationBuilder().AddInMemoryCollection(kv.Select(x => new KeyValuePair<string, string?>(x.Item1, x.Item2))).Build();

    private static string B64(int bytes, byte? fill = null) =>
        Convert.ToBase64String(fill is { } f ? Enumerable.Repeat(f, bytes).ToArray() : RandomNumberGenerator.GetBytes(bytes));

    private static DerivedKeys Resolve(string env, string? secret, string? devPath = null) =>
        RootSecret.Resolve(Cfg(("Security:RootSecret", secret), ("Security:DevRootSecretPath", devPath)), new Env(env));

    private static string TempPath() => Path.Combine(Path.GetTempPath(), "tether-test-" + Guid.NewGuid().ToString("N"), "dev-root-secret");

    [Fact]
    public void Derivation_is_deterministic_32_bytes_and_matches_an_independent_RFC5869_computation()
    {
        var root = RandomNumberGenerator.GetBytes(32);
        var keys = Resolve("Production", Convert.ToBase64String(root));
        var again = Resolve("Production", Convert.ToBase64String(root));
        foreach (var label in new[] { DerivedKeys.Labels.ETag, DerivedKeys.Labels.Idempotency, DerivedKeys.Labels.SyllabusAudit, DerivedKeys.Labels.OAuthState })
        {
            var k = keys.Derive(label);
            Assert.Equal(32, k.Length);
            Assert.Equal(k, again.Derive(label));
            // HKDF-SHA256 by hand: PRK = HMAC(zero salt, IKM); T(1) = HMAC(PRK, info | 0x01).
            var prk = HMACSHA256.HashData(new byte[32], root);
            Assert.Equal(HMACSHA256.HashData(prk, Encoding.UTF8.GetBytes(label).Append((byte)1).ToArray()), k);
        }
    }

    [Fact]
    public void Labels_separate_keys_and_a_different_root_changes_every_key()
    {
        var a = Resolve("Production", B64(32));
        var b = Resolve("Production", B64(32));
        var labels = new[] { DerivedKeys.Labels.ETag, DerivedKeys.Labels.Idempotency, DerivedKeys.Labels.SyllabusAudit, DerivedKeys.Labels.OAuthState };
        Assert.Equal(4, labels.Select(l => Convert.ToHexString(a.Derive(l))).Distinct().Count());
        foreach (var l in labels) Assert.NotEqual(a.Derive(l), b.Derive(l));
        Assert.Equal(["etag.v1", "idempotency.v1", "syllabus-audit.v1", "oauth-state.v1"], labels);
        Assert.Throws<ArgumentException>(() => a.Derive(""));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]       // B1-13: the environment-name escape must not unlock the fallback
    [InlineData("Testing")]
    [InlineData("anything-else")]
    public void Outside_Development_a_missing_secret_stops_startup(string env)
    {
        foreach (var missing in new string?[] { null, "", "   " })
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Resolve(env, missing));
            Assert.Contains("Security:RootSecret", ex.Message);
        }
    }

    [Fact]
    public void Outside_Development_a_short_secret_stops_startup_and_exactly_32_bytes_is_accepted()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve("Production", B64(31)));
        Assert.Contains("32", ex.Message);
        Assert.Equal(32, Resolve("Production", B64(32)).Derive("x.v1").Length);
        Assert.Equal(32, Resolve("Production", B64(64)).Derive("x.v1").Length);
    }

    [Fact]
    public void Outside_Development_an_undecodable_secret_stops_startup_without_echoing_it()
    {
        const string junk = "not*base64*at*all*but-long-enough-0123456789";
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve("Production", junk));
        Assert.DoesNotContain(junk, ex.Message);
    }

    [Theory]
    [InlineData("zeros")]
    [InlineData("ones")]
    [InlineData("changeme")]
    [InlineData("change-me")]
    [InlineData("sample")]
    [InlineData("placeholder")]
    [InlineData("devsecret")]
    public void Outside_Development_a_known_sample_value_stops_startup(string kind)
    {
        var secret = kind switch
        {
            "zeros" => B64(32, 0),
            "ones" => B64(32, 0xFF),
            _ => Convert.ToBase64String(Encoding.UTF8.GetBytes(("REPLACE-" + kind + "-" + kind + "-" + kind + "-" + kind).PadRight(40, 'x'))),
        };
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve("Production", secret));
        Assert.Contains("sample", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, ex.Message);
    }

    [Fact]
    public void The_documented_sample_values_are_refused_exactly()
    {
        foreach (var sample in RootSecret.KnownSampleValues)
            Assert.Throws<InvalidOperationException>(() => Resolve("Production", sample));
    }

    [Fact]
    public void Development_generates_persists_and_reuses_a_secret_and_warns_without_printing_it()
    {
        var path = TempPath();
        try
        {
            var first = Resolve("Development", null, path);
            Assert.True(File.Exists(path));
            var stored = File.ReadAllText(path).Trim();
            Assert.True(Convert.FromBase64String(stored).Length >= 32);
            var second = Resolve("Development", null, path);
            Assert.Equal(first.Derive("etag.v1"), second.Derive("etag.v1")); // persisted, so stable across restarts
            Assert.NotEmpty(first.Warnings);
            Assert.All(first.Warnings.Concat(second.Warnings), w => { Assert.Contains("Security:RootSecret", w); Assert.DoesNotContain(stored, w); });
            if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void Development_with_an_explicit_secret_uses_it_and_does_not_touch_the_disk_or_warn()
    {
        var path = TempPath();
        var keys = Resolve("Development", B64(32), path);
        Assert.False(File.Exists(path));
        Assert.Empty(keys.Warnings);
    }

    [Fact]
    public void Development_still_rejects_a_present_but_bad_secret()
    {
        Assert.Throws<InvalidOperationException>(() => Resolve("Development", B64(8), TempPath()));
        Assert.Throws<InvalidOperationException>(() => Resolve("Development", B64(32, 0), TempPath()));
    }
}

public sealed class NoSecretProductionFactory : ApiFactory
{
    protected override string EnvironmentName => "Production";
    protected override string? RootSecretValue => null;
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Auth:DevLogin:Enabled", "false")];
}

public sealed class SampleSecretStagingFactory : ApiFactory
{
    protected override string EnvironmentName => "Staging";
    protected override string? RootSecretValue => Convert.ToBase64String(new byte[32]);
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Auth:DevLogin:Enabled", "false")];
}

public class RootSecretStartupTests
{
    [Fact]
    public async Task The_host_refuses_to_start_in_Production_without_a_root_secret()
    {
        await using var f = new NoSecretProductionFactory();
        await ((IAsyncLifetime)f).InitializeAsync();
        var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("Security:RootSecret", ex.ToString());
        await ((IAsyncLifetime)f).DisposeAsync();
    }

    [Fact]
    public async Task The_host_refuses_to_start_in_Staging_with_a_sample_secret()
    {
        await using var f = new SampleSecretStagingFactory();
        await ((IAsyncLifetime)f).InitializeAsync();
        var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("sample", ex.ToString(), StringComparison.OrdinalIgnoreCase);
        await ((IAsyncLifetime)f).DisposeAsync();
    }
}

public sealed class NoSecretDevelopmentFactory : ApiFactory
{
    public static readonly string DevPath = Path.Combine(Path.GetTempPath(), "tether-devfactory-" + Guid.NewGuid().ToString("N"), "dev-root-secret");
    public LogCanaryTests.CapturingProvider Logs { get; } = new();
    protected override string? RootSecretValue => null;
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Security:DevRootSecretPath", DevPath)];
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s => s.AddSingleton<ILoggerProvider>(Logs));
    }
}

public class RootSecretDevelopmentStartupTests(NoSecretDevelopmentFactory f) : IClassFixture<NoSecretDevelopmentFactory>
{
    [Fact]
    public async Task Development_boots_without_a_secret_logs_a_warning_and_never_logs_the_secret()
    {
        var c = await f.LoginAsync("devsecret@dev.local"); // boots the host
        Assert.Equal(System.Net.HttpStatusCode.OK, (await c.GetAsync("/api/v1/me")).StatusCode);
        var stored = File.ReadAllText(NoSecretDevelopmentFactory.DevPath).Trim();
        var lines = f.Logs.Lines.ToArray();
        Assert.Contains(lines, l => l.Contains("[Warning]") && l.Contains("Security:RootSecret"));
        Assert.DoesNotContain(lines, l => l.Contains(stored, StringComparison.Ordinal));
        Directory.Delete(Path.GetDirectoryName(NoSecretDevelopmentFactory.DevPath)!, true);
    }
}
