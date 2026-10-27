using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace CollegeCompanion.IntegrationTests.Infrastructure;

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

    protected virtual string EnvironmentName => "Development";
    protected virtual IEnumerable<KeyValuePair<string, string?>> Settings => [];

    public string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_adminConnection) { Database = DbName, Pooling = false }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Security:AllowedOrigins:0", Origin);
        builder.UseSetting("RateLimiting:AuthPermitPerMinute", "100000");
        foreach (var (k, v) in Settings) builder.UseSetting(k, v);
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

    /// <summary>Signs in via the dev login and returns a client carrying the session cookie.</summary>
    public async Task<HttpClient> LoginAsync(string email)
    {
        var c = NewClient();
        var res = await c.PostAsJsonAsync("/api/v1/dev/login", new { email });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var setCookie = res.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("cc.session=", StringComparison.Ordinal));
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
        Assert.Equal("https://errors.collegecompanion.app/" + code, b.GetProperty("type").GetString());
        Assert.False(string.IsNullOrEmpty(b.GetProperty("traceId").GetString()));
        var raw = b.GetRawText();
        Assert.DoesNotContain("StackTrace", raw, StringComparison.OrdinalIgnoreCase);
    }
}
