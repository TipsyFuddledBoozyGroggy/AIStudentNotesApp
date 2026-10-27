using System.Net;
using System.Net.Http.Json;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tether.IntegrationTests;

public class AuthBoundaryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // The complete anonymous allowlist (SEC-001). Anything else must require authentication.
    private static readonly HashSet<string> AnonymousAllowlist =
    [
        "POST /api/v1/dev/login",
        "POST /api/v1/auth/logout",
        "GET /health/live",
        "GET /health/ready",
    ];

    private static IEnumerable<(string Method, string Path, RouteEndpoint Endpoint)> Routes(IServiceProvider sp) =>
        sp.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(m => (m, e.RoutePattern.RawText!, e)));

    [Fact]
    public void Only_allowlisted_endpoints_are_anonymous_and_all_others_inherit_the_fallback_policy()
    {
        factory.AssertUsesTestDatabase();
        var anonymous = Routes(factory.Services)
            .Where(r => r.Endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(r => $"{r.Method} {r.Path}").ToHashSet();
        Assert.Equal(AnonymousAllowlist.OrderBy(x => x), anonymous.OrderBy(x => x));

        var opts = factory.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        Assert.NotNull(opts.FallbackPolicy);
        Assert.Contains(opts.FallbackPolicy!.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task Every_non_allowlisted_endpoint_returns_401_when_anonymous()
    {
        var client = factory.NewClient();
        var checkedCount = 0;
        foreach (var (method, path, _) in Routes(factory.Services))
        {
            if (AnonymousAllowlist.Contains($"{method} {path}")) continue;
            var url = path.Replace("{id:guid}", Guid.NewGuid().ToString());
            Assert.DoesNotContain("{", url);
            var res = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
            {
                Content = method is "POST" or "PUT" ? JsonContent.Create(new { name = "x" }) : null,
            });
            await res.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
            checkedCount++;
        }
        Assert.True(checkedCount >= 6, $"expected at least 6 protected endpoints, enumerated {checkedCount}");
    }

    [Fact]
    public async Task Unknown_route_is_a_problem_details_404_when_signed_in_and_401_when_anonymous()
    {
        // The fallback policy also guards unmatched paths, so anonymous callers cannot probe which routes exist.
        await (await factory.NewClient().GetAsync("/api/v1/nope")).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        var alice = await factory.LoginAsync("alice@dev.local");
        await (await alice.GetAsync("/api/v1/nope")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Me_returns_profile_for_signed_in_user_and_logout_is_idempotent()
    {
        var alice = await factory.LoginAsync("alice@dev.local");
        var res = await alice.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var b = await res.BodyAsync();
        Assert.Equal("alice@dev.local", b.GetProperty("email").GetString());
        Assert.Equal("Alice", b.GetProperty("displayName").GetString());
        Assert.True(Guid.TryParse(b.GetProperty("id").GetString(), out _));

        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.NewClient().PostAsync("/api/v1/auth/logout", null)).StatusCode);
    }

    [Fact]
    public async Task Seeded_users_exist_with_distinct_ids()
    {
        var a = await (await (await factory.LoginAsync("alice@dev.local")).GetAsync("/api/v1/me")).BodyAsync();
        var b = await (await (await factory.LoginAsync("bob@dev.local")).GetAsync("/api/v1/me")).BodyAsync();
        Assert.NotEqual(a.GetProperty("id").GetString(), b.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Garbage_session_cookie_is_treated_as_anonymous()
    {
        var c = factory.NewClient();
        c.DefaultRequestHeaders.Add("Cookie", "cc.session=not-a-real-cookie");
        await (await c.GetAsync("/api/v1/courses")).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Dev_login_validates_email()
    {
        var c = factory.NewClient();
        foreach (var bad in new object[] { new { email = "nope" }, new { email = "" }, new { } })
        {
            var res = await c.PostAsJsonAsync("/api/v1/dev/login", bad);
            await res.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
            Assert.True((await res.BodyAsync()).GetProperty("errors").TryGetProperty("email", out _));
        }
    }

    [Fact]
    public async Task Login_sets_httponly_samesite_lax_session_cookie_without_body_token()
    {
        var res = await factory.NewClient().PostAsJsonAsync("/api/v1/dev/login", new { email = "alice@dev.local" });
        var cookie = Assert.Single(res.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("cc.session=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Security_headers_are_present()
    {
        var res = await factory.NewClient().GetAsync("/health/live");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("frame-ancestors 'none'", res.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("no-referrer", res.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task Health_endpoints_are_anonymous_and_ready_checks_the_database()
    {
        var c = factory.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health/ready")).StatusCode);
    }
}
