using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tether.IntegrationTests;

// ---------------------------------------------------------------------------------------------
// F-01 / ADR-016: server-side sessions
// ---------------------------------------------------------------------------------------------
public class ServerSideSessionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Email() => $"s{Guid.NewGuid():N}@dev.local";

    [Fact]
    public async Task Logout_revokes_the_session_so_a_copied_cookie_stops_working()
    {
        var c = await factory.LoginAsync(Email());
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        // Same cookie replayed (as an attacker holding a copy would):
        await (await c.GetAsync("/api/v1/me")).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        await (await c.GetAsync("/api/v1/courses")).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Logout_revokes_only_that_session_not_the_users_other_devices()
    {
        var email = Email();
        var laptop = await factory.LoginAsync(email);
        var phone = await factory.LoginAsync(email);
        await laptop.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Soft_deleted_user_is_rejected_on_every_authenticated_route()
    {
        var email = Email();
        var c = await factory.LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/courses")).StatusCode);
        await factory.ExecAsync($"update identity.users set deleted_at = now() where email = '{email}'");
        await (await c.GetAsync("/api/v1/courses")).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        await (await c.GetAsync("/api/v1/me")).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cookie_whose_session_row_is_missing_or_revoked_is_rejected()
    {
        var email = Email();
        var missing = await factory.LoginAsync(email);
        var revoked = await factory.LoginAsync(email);
        Assert.Equal(2, await factory.ScalarAsync(
            $"select count(*) from identity.sessions s join identity.users u on u.id = s.user_id where u.email = '{email}'"));

        await factory.ExecAsync($"delete from identity.sessions where id in (select s.id from identity.sessions s join identity.users u on u.id = s.user_id where u.email = '{email}' order by s.created_at limit 1)");
        await factory.ExecAsync($"update identity.sessions set revoked_at = now() where user_id = (select id from identity.users where email = '{email}')");
        await (await missing.GetAsync("/api/v1/me")).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        await (await revoked.GetAsync("/api/v1/me")).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Sessions_are_owned_by_a_user_and_removed_when_the_user_is_purged()
    {
        var email = Email();
        await factory.LoginAsync(email);
        Assert.Equal(1, await factory.ScalarAsync(
            $"select count(*) from identity.sessions where user_id = (select id from identity.users where email = '{email}')"));
        await factory.ExecAsync($"delete from identity.users where email = '{email}'");
        Assert.Equal(0, await factory.ScalarAsync(
            $"select count(*) from identity.sessions where user_id not in (select id from identity.users)"));
    }
}

// ---------------------------------------------------------------------------------------------
// F-02: absolute lifetime cannot be extended by sliding refresh
// ---------------------------------------------------------------------------------------------
public class AbsoluteSessionLifetimeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Sliding_refresh_cannot_extend_the_14_day_absolute_lifetime()
    {
        var c = factory.NewClient();
        var login = await c.PostAsJsonAsync("/api/v1/dev/login", new { email = "abs@dev.local" });
        var cookie = login.Headers.GetValues("Set-Cookie").Single(ApiFactory.IsSessionCookie).Split(';')[0];
        var start = factory.Clock.GetUtcNow();
        var refreshes = 0;
        TimeSpan? diedAfter = null;

        // Use the session every 5 hours: past the sliding half-window (4h) so each response re-issues the cookie.
        for (var i = 0; i < 100 && diedAfter is null; i++)
        {
            factory.Clock.Advance(TimeSpan.FromHours(5));
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
            req.Headers.Add("Cookie", cookie);
            var res = await c.SendAsync(req);
            if (res.StatusCode == HttpStatusCode.Unauthorized) { diedAfter = factory.Clock.GetUtcNow() - start; break; }
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            if (res.Headers.TryGetValues("Set-Cookie", out var sc) && sc.SingleOrDefault(ApiFactory.IsSessionCookie) is { } fresh)
            {
                cookie = fresh.Split(';')[0];
                refreshes++;
            }
        }

        Assert.True(refreshes > 3, $"sliding refresh should have happened repeatedly (saw {refreshes})");
        Assert.NotNull(diedAfter);
        Assert.InRange(diedAfter!.Value, TimeSpan.FromDays(14), TimeSpan.FromDays(14) + TimeSpan.FromHours(5));
    }
}

// ---------------------------------------------------------------------------------------------
// F-03: rate limiting on all routes, per user; forwarded headers only from trusted proxies
// ---------------------------------------------------------------------------------------------
public sealed class ApiLimitFactory : ApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("RateLimiting:ApiPermitPerMinute", "5")];
}

public sealed class WriteLimitFactory : ApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("RateLimiting:WritePermitPerMinute", "2")];
}

public sealed class AnonLimitFactory : ApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("RateLimiting:AnonymousPermitPerMinute", "3")];
}

public sealed class TrustedProxyFactory : ApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [new("RateLimiting:AuthPermitPerMinute", "3"), new("Security:TrustedProxies:0", "10.0.0.5")];
}

public class ApiRateLimitTests
{
    public class PerUser(ApiLimitFactory factory) : IClassFixture<ApiLimitFactory>
    {
        [Fact]
        public async Task Authenticated_reads_are_limited_per_user_with_retry_after_and_users_do_not_share_buckets()
        {
            var alice = await factory.LoginAsync("rl-alice@dev.local");
            var bob = await factory.LoginAsync("rl-bob@dev.local");
            for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("/api/v1/courses")).StatusCode);
            var limited = await alice.GetAsync("/api/v1/courses");
            await limited.AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited");
            Assert.True(limited.Headers.Contains("Retry-After"));
            Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync("/api/v1/courses")).StatusCode);
        }
    }

    public class Writes(WriteLimitFactory factory) : IClassFixture<WriteLimitFactory>
    {
        [Fact]
        public async Task Unsafe_methods_have_a_stricter_per_user_budget_than_reads()
        {
            var c = await factory.LoginAsync("rl-writer@dev.local");
            for (var i = 0; i < 2; i++)
                Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/v1/courses", new { name = "c" + i })).StatusCode);
            await (await c.PostAsJsonAsync("/api/v1/courses", new { name = "c3" }))
                .AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited");
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/courses")).StatusCode);
        }

        // Regression (E2E flake): hub negotiate POSTs used to spend the write budget, so reconnects/page loads made the next
        // audio-ticket, Pause or session create answer 429 and the transcript stayed empty.
        [Fact]
        public async Task Hub_negotiate_does_not_spend_the_write_budget_but_is_still_limited_by_the_general_per_user_bucket()
        {
            var c = await factory.LoginAsync("rl-negotiate@dev.local");
            for (var i = 0; i < 8; i++)
                Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/hubs/lecture/negotiate?negotiateVersion=1", null)).StatusCode);
            for (var i = 0; i < 2; i++)
                Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/v1/courses", new { name = "n" + i })).StatusCode); // budget of 2 still intact
            await (await c.PostAsJsonAsync("/api/v1/courses", new { name = "n3" })).AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited"); // control: other writes still limited
        }
    }

    public class ConsentReversal(WriteLimitFactory factory) : IClassFixture<WriteLimitFactory>
    {
        // Regression (E2E flake): with the write budget spent, Stop came back 429 and the lecture stayed live (ADR-019b).
        [Fact]
        public async Task Own_pause_and_stop_are_never_429_even_when_the_write_and_general_budgets_are_spent()
        {
            var c = await factory.LoginAsync("rl-reversal@dev.local");
            var course = (await (await c.PostAsJsonAsync("/api/v1/courses", new { name = "r" })).BodyAsync()).GetProperty("id").GetString();
            var start = await c.PostAsJsonAsync($"/api/v1/courses/{course}/sessions", new { title = "t", consent = new { acknowledged = true, textVersion = "draft-1" } });
            Assert.Equal(HttpStatusCode.Created, start.StatusCode);
            var id = (await start.BodyAsync()).GetProperty("id").GetString();
            await (await c.PostAsJsonAsync("/api/v1/courses", new { name = "r3" })).AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited"); // control: budget is spent
            Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"/api/v1/sessions/{id}/pause", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"/api/v1/sessions/{id}/stop", null)).StatusCode);
        }
    }

    public class Anonymous(AnonLimitFactory factory) : IClassFixture<AnonLimitFactory>
    {
        [Fact]
        public async Task Anonymous_callers_including_the_readiness_probe_are_limited_per_ip()
        {
            var c = factory.NewClient();
            for (var i = 0; i < 3; i++) Assert.NotEqual(HttpStatusCode.TooManyRequests, (await c.GetAsync("/health/ready")).StatusCode);
            await (await c.GetAsync("/health/ready")).AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited");
        }
    }
}

public class ForwardedHeadersTests
{
    private static async Task<List<HttpStatusCode>> LoginsFrom(ApiFactory f, string remoteIp, bool distinctXff)
    {
        var results = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/dev/login") { Content = JsonContent.Create(new { email = "fh@dev.local" }) };
            req.Headers.Add("Origin", ApiFactory.Origin);
            req.Headers.Add("X-Test-Remote-Ip", remoteIp);
            req.Headers.Add("X-Forwarded-For", distinctXff ? $"203.0.113.{i + 1}" : "203.0.113.1");
            results.Add((await f.NewClient().SendAsync(req)).StatusCode);
        }
        return results;
    }

    public class Trusted(TrustedProxyFactory factory) : IClassFixture<TrustedProxyFactory>
    {
        [Fact]
        public async Task Trusted_proxy_forwarded_client_ips_get_their_own_rate_limit_buckets()
            => Assert.All(await LoginsFrom(factory, "10.0.0.5", distinctXff: true), s => Assert.Equal(HttpStatusCode.NoContent, s));

        [Fact]
        public async Task Untrusted_peer_cannot_spoof_its_client_ip()
        {
            var r = await LoginsFrom(factory, "10.9.9.9", distinctXff: true);
            Assert.Equal(HttpStatusCode.TooManyRequests, r[3]);
        }
    }

    public class NoneConfigured(LowRateLimitFactory factory) : IClassFixture<LowRateLimitFactory>
    {
        [Fact]
        public async Task Forwarded_headers_are_ignored_when_no_trusted_proxy_is_configured()
        {
            var r = await LoginsFrom(factory, "10.0.0.5", distinctXff: true);
            Assert.Equal(HttpStatusCode.TooManyRequests, r[3]);
        }
    }
}

// ---------------------------------------------------------------------------------------------
// F-05: ETag must not expose xmin
// ---------------------------------------------------------------------------------------------
public class EtagTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Etag_is_an_opaque_weak_tag_that_does_not_reveal_xmin_and_keeps_if_match_semantics()
    {
        var c = await factory.LoginAsync("etag@dev.local");
        var created = await (await c.PostAsJsonAsync("/api/v1/courses", new { name = "E" })).BodyAsync();
        var id = created.GetProperty("id").GetString()!;
        var xmin = await factory.TextAsync($"select xmin::text from courses.courses where id = '{id}'");

        var get = await c.GetAsync($"/api/v1/courses/{id}");
        var etag = get.Headers.ETag!;
        Assert.True(etag.IsWeak);
        Assert.Matches(new Regex("^[A-Za-z0-9_-]{16,}$"), etag.Tag.Trim('"'));
        Assert.NotEqual(xmin, etag.Tag.Trim('"'));
        Assert.Equal(etag.ToString(), (await c.GetAsync($"/api/v1/courses/{id}")).Headers.ETag!.ToString()); // stable

        // Old raw-xmin style tag no longer matches.
        var legacy = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/courses/{id}") { Content = JsonContent.Create(new { name = "X" }) };
        legacy.Headers.TryAddWithoutValidation("If-Match", $"\"{xmin}\"");
        await (await c.SendAsync(legacy)).AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");

        var ok = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/courses/{id}") { Content = JsonContent.Create(new { name = "Y" }) };
        ok.Headers.TryAddWithoutValidation("If-Match", etag.ToString());
        var res = await c.SendAsync(ok);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.NotEqual(etag.ToString(), res.Headers.ETag!.ToString());
    }

    [Fact]
    public async Task Etags_of_different_courses_are_not_comparable()
    {
        var c = await factory.LoginAsync("etag2@dev.local");
        var a = (await (await c.PostAsJsonAsync("/api/v1/courses", new { name = "A" })).BodyAsync()).GetProperty("id").GetString();
        var b = (await (await c.PostAsJsonAsync("/api/v1/courses", new { name = "B" })).BodyAsync()).GetProperty("id").GetString();
        var ea = (await c.GetAsync($"/api/v1/courses/{a}")).Headers.ETag!.ToString();
        var eb = (await c.GetAsync($"/api/v1/courses/{b}")).Headers.ETag!.ToString();
        Assert.NotEqual(ea, eb);
    }
}

// ---------------------------------------------------------------------------------------------
// F-06: security headers (incl. 500s), HSTS outside Development
// ---------------------------------------------------------------------------------------------
public class SecurityHeaderTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static void AssertSecurityHeaders(HttpResponseMessage r)
    {
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", r.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("no-store", r.Headers.CacheControl!.ToString());
        Assert.Contains("microphone=(", r.Headers.GetValues("Permissions-Policy").Single());
    }

    [Fact]
    public async Task Success_and_error_responses_carry_security_headers_including_permissions_policy()
    {
        var c = await factory.LoginAsync("hdr@dev.local");
        AssertSecurityHeaders(await c.GetAsync("/api/v1/me"));
        AssertSecurityHeaders(await c.GetAsync("/api/v1/nope"));
        AssertSecurityHeaders(await factory.NewClient().GetAsync("/api/v1/me"));
    }

    [Fact]
    public async Task Development_does_not_send_hsts()
    {
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://api.example.test") });
        Assert.False((await c.GetAsync("/health/live")).Headers.Contains("Strict-Transport-Security"));
    }
}

public class SecurityHeadersOn500Tests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Unhandled_exceptions_still_return_security_headers_and_a_generic_problem()
    {
        var c = await factory.LoginAsync("boom@dev.local");
        await factory.ExecAsync("drop table courses.courses cascade"); // forces a PostgresException on /courses
        var res = await c.GetAsync("/api/v1/courses");
        await res.AssertProblemAsync(HttpStatusCode.InternalServerError, "internal_error");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("default-src 'none'", res.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("no-store", res.Headers.CacheControl!.ToString());
        Assert.True(res.Headers.Contains("Permissions-Policy"));
        Assert.DoesNotContain("courses", await res.Content.ReadAsStringAsync()); // no relation name leaks
    }
}

public class ProductionHeaderTests(ProductionFactory prod) : IClassFixture<ProductionFactory>
{
    [Fact]
    public async Task Hsts_is_sent_over_https_outside_development()
    {
        var c = prod.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://api.example.test") });
        var res = await c.GetAsync("/health/live");
        var hsts = res.Headers.GetValues("Strict-Transport-Security").Single();
        Assert.Contains("max-age=", hsts);
        Assert.Contains("includeSubDomains", hsts);
    }

    [Fact]
    public async Task Hsts_is_not_sent_over_plain_http()
    {
        Assert.False((await prod.NewClient().GetAsync("/health/live")).Headers.Contains("Strict-Transport-Security"));
    }

    // F-07
    [Fact]
    public void Production_session_cookie_uses_the_host_prefix_and_its_required_attributes()
    {
        var o = prod.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.Equal("__Host-cc.session", o.Cookie.Name);
        Assert.Equal(CookieSecurePolicy.Always, o.Cookie.SecurePolicy);
        Assert.Equal("/", o.Cookie.Path);
        Assert.Null(o.Cookie.Domain);
    }
}

public class DevelopmentCookieTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void Development_cookie_keeps_the_unprefixed_name_because_dev_runs_over_plain_http()
    {
        var o = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.Equal("cc.session", o.Cookie.Name);
    }
}

// ---------------------------------------------------------------------------------------------
// F-07: persisted data-protection keys
// ---------------------------------------------------------------------------------------------
public sealed class KeyPathFactory : ApiFactory
{
    public string KeysDir { get; } = Path.Combine(Path.GetTempPath(), "cc-dpkeys-" + Guid.NewGuid().ToString("N"));
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("DataProtection:KeysPath", KeysDir)];
}

public class DataProtectionKeyTests(KeyPathFactory factory) : IClassFixture<KeyPathFactory>
{
    [Fact]
    public async Task Keys_are_persisted_to_the_configured_directory()
    {
        try
        {
            await factory.LoginAsync("dp@dev.local"); // protecting the cookie forces key creation
            Assert.True(Directory.Exists(factory.KeysDir), "configured key directory was not created");
            Assert.NotEmpty(Directory.GetFiles(factory.KeysDir, "key-*.xml"));
        }
        finally { if (Directory.Exists(factory.KeysDir)) Directory.Delete(factory.KeysDir, true); }
    }
}
