using System.Net;
using System.Net.Http.Json;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tether.IntegrationTests;

public class CsrfTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> CookieOnlyClientAsync()
    {
        var alice = await factory.LoginAsync("alice@dev.local");
        var c = factory.NewClient(withOrigin: false);
        c.DefaultRequestHeaders.Add("Cookie", alice.DefaultRequestHeaders.GetValues("Cookie").Single());
        return c;
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Unsafe_request_without_origin_is_rejected(string method)
    {
        var c = await CookieOnlyClientAsync();
        var res = await c.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"/api/v1/courses/{Guid.NewGuid()}"));
        await res.AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
    }

    [Fact]
    public async Task Unsafe_request_from_foreign_origin_is_rejected_even_with_a_valid_session()
    {
        var alice = await factory.LoginAsync("alice@dev.local");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/courses") { Content = JsonContent.Create(new { name = "Evil" }) };
        req.Headers.Remove("Origin");
        req.Headers.Add("Origin", "https://evil.example");
        await (await alice.SendAsync(req)).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        var list = await (await alice.GetAsync("/api/v1/courses")).BodyAsync();
        Assert.Equal(0, list.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Login_from_foreign_origin_is_rejected()
    {
        var c = factory.NewClient(withOrigin: false);
        c.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        var res = await c.PostAsJsonAsync("/api/v1/dev/login", new { email = "alice@dev.local" });
        await res.AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        Assert.False(res.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Gets_do_not_need_an_origin()
    {
        var c = await CookieOnlyClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/courses")).StatusCode);
    }
}

public sealed class ProductionFactory : ApiFactory
{
    protected override string EnvironmentName => "Production";
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Auth:DevLogin:Enabled", "false")];
}

public sealed class ProductionWithDevLoginFactory : ApiFactory
{
    protected override string EnvironmentName => "Production";
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Auth:DevLogin:Enabled", "true")];
}

public sealed class StagingWithDevLoginFactory : ApiFactory
{
    protected override string EnvironmentName => "Staging";
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Auth:DevLogin:Enabled", "true")];
}

public sealed class LowRateLimitFactory : ApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("RateLimiting:AuthPermitPerMinute", "3")];
}

public class DevLoginEnvironmentTests(ProductionFactory prod) : IClassFixture<ProductionFactory>
{
    [Fact]
    public async Task Dev_login_endpoint_does_not_exist_in_production()
    {
        var res = await prod.NewClient().PostAsJsonAsync("/api/v1/dev/login", new { email = "alice@dev.local" });
        // Anonymous probes of any unmatched path get 401 (fallback policy); the point is: no cookie, no login.
        await res.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        Assert.False(res.Headers.Contains("Set-Cookie"));
        var routes = prod.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().Select(e => e.RoutePattern.RawText).ToList();
        Assert.DoesNotContain("/api/v1/dev/login", routes);
        Assert.Contains(routes, r => r!.StartsWith("/api/v1/courses"));
    }

    [Fact]
    public void Production_session_cookie_is_httponly_secure_lax()
    {
        var o = prod.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.True(o.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, o.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, o.Cookie.SameSite);
        Assert.Equal(TimeSpan.FromHours(8), o.ExpireTimeSpan);
        Assert.True(o.SlidingExpiration);
    }

    [Fact]
    public async Task Production_does_not_auto_migrate_or_seed_and_liveness_works()
    {
        Assert.Equal(HttpStatusCode.OK, (await prod.NewClient().GetAsync("/health/live")).StatusCode);
        await using var conn = new NpgsqlConnection(prod.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select count(*) from information_schema.schemata where schema_name in ('identity','courses')", conn);
        Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
    }
}

public class DevLoginStartupGuardTests
{
    [Theory]
    [InlineData(typeof(ProductionWithDevLoginFactory))]
    [InlineData(typeof(StagingWithDevLoginFactory))]
    public async Task Startup_fails_when_dev_login_is_enabled_outside_development(Type factoryType)
    {
        var f = (ApiFactory)Activator.CreateInstance(factoryType)!;
        await ((IAsyncLifetime)f).InitializeAsync();
        try
        {
            var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
            var chain = new List<string>();
            for (Exception? e = ex; e is not null; e = e.InnerException) chain.Add(e.Message);
            Assert.Contains(chain, m => m.Contains("Auth:DevLogin:Enabled", StringComparison.Ordinal)
                                        && m.Contains("not Development", StringComparison.Ordinal));
        }
        finally { await ((IAsyncLifetime)f).DisposeAsync(); }
    }
}

public class RateLimitTests(LowRateLimitFactory factory) : IClassFixture<LowRateLimitFactory>
{
    [Fact]
    public async Task Login_is_rate_limited_with_retry_after()
    {
        var c = factory.NewClient();
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/v1/dev/login", new { email = "alice@dev.local" })).StatusCode);
        var res = await c.PostAsJsonAsync("/api/v1/dev/login", new { email = "alice@dev.local" });
        await res.AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.True(res.Headers.Contains("Retry-After"));
    }
}
