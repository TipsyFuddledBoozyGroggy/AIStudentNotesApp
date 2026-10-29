using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Tether.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real Host against a throwaway database (cc_test_*) on the compose Postgres.
/// The server comes from ConnectionStrings__Default (set by docker-compose.dev.yml for the dotnet service).
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Origin = "http://localhost:3000";
    private readonly string _adminConnection =
        Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? throw new InvalidOperationException("ConnectionStrings__Default must point at the compose postgres.");
    public string DbName { get; } = "cc_test_" + Guid.NewGuid().ToString("N");

    /// <summary>Controllable clock injected as the app TimeProvider (starts at real time).</summary>
    public TestClock Clock { get; } = new();

    protected virtual string EnvironmentName => "Development";
    /// <summary>The gateway uses the FIRST probe registered for a scope kind, so fixtures that register a fake document probe (AiScopeTests) need the real Syllabus probe out of the way.</summary>
    protected virtual bool KeepRealSyllabusProbe => false;
    protected virtual IEnumerable<KeyValuePair<string, string?>> Settings => [];
    /// <summary>AllowedHosts for the test host (M1R-N02: "*" is refused outside Development). Null leaves the shipped appsettings value in force.</summary>
    protected virtual string? AllowedHostsValue => "localhost;api.example.test";
    /// <summary>Test-only root secret (B1-11). Random-looking, not a documented sample. Override with null to exercise the missing-secret paths.</summary>
    protected virtual string? RootSecretValue => "p3Qw9ZsXk2VbN7mLr4TyHc8JdFaE5uGiOo1WqSzAeRtYvBnMxCkDlPhUjIgKfLb0";

    public string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_adminConnection) { Database = DbName, Pooling = false }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Security:AllowedOrigins:0", Origin);
        if (AllowedHostsValue is { } ah) builder.UseSetting("AllowedHosts", ah);
        if (RootSecretValue is { } rs) builder.UseSetting("Security:RootSecret", rs);
        builder.UseSetting("RateLimiting:AuthPermitPerMinute", "100000");
        builder.UseSetting("RateLimiting:ApiPermitPerMinute", "100000");
        builder.UseSetting("RateLimiting:WritePermitPerMinute", "100000");
        builder.UseSetting("RateLimiting:AnonymousPermitPerMinute", "100000");
        // Syllabus routes have their own buckets (API_CONTRACTS_P2 section 11); the shared fixture lifts them like the general ones.
        foreach (var k in new[] { "SyllabusWritePermitPerMinute", "SyllabusReadPermitPerMinute", "SyllabusSyncPerSyllabusPerMinute", "SyllabusSyncPerUserPerMinute", "SyllabusBatchPerUserPerMinute", "SyllabusExportPerUserPerMinute" })
            builder.UseSetting("RateLimiting:" + k, "100000");
        builder.UseSetting("Syllabus:Worker:PollMilliseconds", "40");
        foreach (var (k, v) in Settings) builder.UseSetting(k, v);
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            if (!KeepRealSyllabusProbe) s.RemoveAll<Tether.SharedKernel.Ai.IAiDocumentScopeProbe>();
            s.AddSingleton<TimeProvider>(Clock);
            s.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>();
        });
    }

    public async Task InitializeAsync()
    {
        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_adminConnection) { Pooling = false }.ConnectionString);
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand($"create database \"{DbName}\"", admin);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Fails loudly if the app is not actually using the throwaway database.</summary>
    public void AssertUsesTestDatabase()
    {
        var cs = Services.GetRequiredService<IConfiguration>().GetConnectionString("Default");
        Assert.Contains(DbName, cs);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_adminConnection) { Pooling = false }.ConnectionString);
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand($"drop database if exists \"{DbName}\" with (force)", admin);
        await cmd.ExecuteNonQueryAsync();
    }

    public HttpClient NewClient(bool withOrigin = true)
    {
        var c = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        if (withOrigin) c.DefaultRequestHeaders.Add("Origin", Origin);
        return c;
    }

    public static bool IsSessionCookie(string setCookie) =>
        setCookie.StartsWith("cc.session=", StringComparison.Ordinal)
        || setCookie.StartsWith("__Host-cc.session=", StringComparison.Ordinal);

    public async Task<long> ScalarAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    public async Task<string?> TextAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToString(await cmd.ExecuteScalarAsync());
    }

    public async Task ExecAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Signs in via the dev login and returns a client carrying the session cookie.</summary>
    public async Task<HttpClient> LoginAsync(string email)
    {
        var c = NewClient();
        var res = await c.PostAsJsonAsync("/api/v1/dev/login", new { email });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var setCookie = res.Headers.GetValues("Set-Cookie").Single(IsSessionCookie);
        c.DefaultRequestHeaders.Add("Cookie", setCookie.Split(';')[0]);
        return c;
    }
}

public static class Json
{
    public static async Task<JsonElement> BodyAsync(this HttpResponseMessage r)
        => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    public static async Task AssertProblemAsync(this HttpResponseMessage r, HttpStatusCode status, string code)
    {
        Assert.Equal(status, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var b = await r.BodyAsync();
        Assert.Equal(code, b.GetProperty("code").GetString());
        Assert.Equal((int)status, b.GetProperty("status").GetInt32());
        Assert.Equal("https://errors.tether.invalid/" + code, b.GetProperty("type").GetString());
        Assert.False(string.IsNullOrEmpty(b.GetProperty("traceId").GetString()));
        var raw = b.GetRawText();
        Assert.DoesNotContain("StackTrace", raw, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Lets a test choose the connection's remote IP (TestServer has none) via the X-Test-Remote-Ip header.</summary>
internal sealed class TestRemoteIpStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (ctx, nxt) =>
        {
            if (ctx.Request.Headers.TryGetValue("X-Test-Remote-Ip", out var ip))
                ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
            await nxt();
        });
        next(app);
    };
}
