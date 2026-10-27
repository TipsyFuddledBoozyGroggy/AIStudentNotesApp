using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Tether.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tether.IntegrationTests.Infrastructure;

namespace Tether.IntegrationTests;

// N-04: IPv6 callers share one bucket per /64; IPv4 is keyed per address.
public class ClientIpKeyTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:ffff:ffff:ffff:ffff", "2001:db8:1:2::/64")]
    public void Keys_ipv4_per_address_and_ipv6_per_64(string ip, string expected) =>
        Assert.Equal(expected, ClientIpKey.For(IPAddress.Parse(ip)));

    [Fact]
    public void Different_64s_get_different_keys() =>
        Assert.NotEqual(ClientIpKey.For(IPAddress.Parse("2001:db8:1:2::1")), ClientIpKey.For(IPAddress.Parse("2001:db8:1:3::1")));

    [Fact]
    public void Missing_address_is_a_single_unknown_bucket() => Assert.Equal("unknown", ClientIpKey.For(null));
}

public class IpV6RateLimitTests(IpV6RateLimitTests.Factory f) : IClassFixture<IpV6RateLimitTests.Factory>
{
    public sealed class Factory : ApiFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
            [new("RateLimiting:AuthPermitPerMinute", "2")];
    }

    [Fact]
    public async Task Rotating_within_one_64_does_not_escape_the_login_limit()
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var c = f.NewClient();
            c.DefaultRequestHeaders.Add("X-Test-Remote-Ip", $"2001:db8:9:9::{i + 1:x}");
            statuses.Add((await c.PostAsJsonAsync("/api/v1/dev/login", new { email = "v6@dev.local" })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[3]);
    }
}

// N-03: a fresh login revokes the session the browser already carried.
public class LoginRevokesPriorSessionTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Old_cookie_is_dead_after_same_browser_logs_in_as_someone_else()
    {
        var alice = await f.LoginAsync("alice-n03@dev.local");
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("/api/v1/me")).StatusCode);
        var oldCookie = alice.DefaultRequestHeaders.GetValues("Cookie").Single();

        // Same browser (still sending alice's cookie) signs in as bob.
        var res = await alice.PostAsJsonAsync("/api/v1/dev/login", new { email = "bob-n03@dev.local" });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var replay = f.NewClient();
        replay.DefaultRequestHeaders.Add("Cookie", oldCookie);
        await (await replay.GetAsync("/api/v1/me")).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");

        // And the new cookie works.
        var bobCookie = res.Headers.GetValues("Set-Cookie").Single(ApiFactory.IsSessionCookie).Split(';')[0];
        var bob = f.NewClient();
        bob.DefaultRequestHeaders.Add("Cookie", bobCookie);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync("/api/v1/me")).StatusCode);
    }
}

// F-08: a failing request whose exception carries row values must not put them in any log.
public class LogCanaryTests(LogCanaryTests.Factory f) : IClassFixture<LogCanaryTests.Factory>
{
    public sealed class CapturingProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public ILogger CreateLogger(string categoryName) => new L(this, categoryName);
        public void Dispose() { }
        private sealed class L(CapturingProvider p, string cat) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> fmt)
            {
                var scalars = state is IEnumerable<KeyValuePair<string, object?>> kv
                    ? string.Join(";", kv.Select(x => $"{x.Key}={x.Value}")) : "";
                p.Lines.Enqueue($"{cat} [{level}] {fmt(state, ex)} | {scalars} | {ex}");
            }
        }
    }

    public sealed class Factory : ApiFactory
    {
        public CapturingProvider Logs { get; } = new();
        // Worst case: Npgsql configured to echo row data in exception messages ("Include Error Detail"), which devs commonly enable.
        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
            [new("ConnectionStrings:Default", ConnectionString + ";Include Error Detail=true")];
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddSingleton<ILoggerProvider>(Logs));
        }
    }

    [Fact]
    public async Task Sentinel_in_a_failing_row_never_reaches_the_logs()
    {
        const string sentinel = "CANARY-7f3a9c1e-lecture-text";
        var c = await f.LoginAsync("canary@dev.local");
        // Makes the INSERT fail with a PostgresException whose Detail echoes the row ("Failing row contains ...").
        await f.ExecAsync($"alter table courses.courses add constraint ck_canary check (name not like '%{sentinel}%')");

        var res = await c.PostAsJsonAsync("/api/v1/courses", new { name = "Bio " + sentinel });
        await res.AssertProblemAsync(HttpStatusCode.InternalServerError, "internal_error");
        Assert.DoesNotContain(sentinel, await res.Content.ReadAsStringAsync());

        var all = f.Logs.Lines.ToArray();
        Assert.Contains(all, l => l.Contains("Unhandled", StringComparison.Ordinal)); // the failure itself was logged
        Assert.DoesNotContain(all, l => l.Contains(sentinel, StringComparison.Ordinal));
    }
}
