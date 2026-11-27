using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tether.Host;
using Tether.IntegrationTests.Infrastructure;

namespace Tether.IntegrationTests;

public sealed class CallbackFloodFactory : GoogleFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> ExtraSettings =>
    [
        new("RateLimiting:AuthCallbackPermitPerMinute", "5"),
        new("RateLimiting:AuthCallbackInvalidPermitPerMinute", "3"),
    ];
}

/// <summary>O1: forged callbacks, whatever cookie they carry, spend only the invalid bucket; real returns keep the valid budget.</summary>
public class GoogleSignInCallbackFloodTests(CallbackFloodFactory f) : IClassFixture<CallbackFloodFactory>
{
    private static string RandomToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public async Task A_flood_with_random_well_formed_binders_cannot_starve_a_real_return()
    {
        var outcomes = new List<string>();
        for (var i = 0; i < 40; i++)
        {
            var res = await SignInDriver.CallbackAsync(f.NewClient(), $"state={RandomToken()}&code=x", "__Host-cc.signin=" + RandomToken());
            outcomes.Add(SignInDriver.Location(res));
        }
        Assert.Equal(3, outcomes.Count(o => o.EndsWith("state_invalid", StringComparison.Ordinal)));
        Assert.Equal(37, outcomes.Count(o => o.EndsWith("rate_limited", StringComparison.Ordinal)));
        Assert.Equal(0, f.Fake.TokenCalls);

        // The valid budget (5 a minute here) is untouched by the flood: five real returns in a row all sign in.
        for (var i = 0; i < 5; i++)
        {
            var (ok, _, _) = await f.SignInAsync("1" + Random.Shared.NextInt64(100000000000000000, 999999999999999999), ApiFactory.InvitedEmail);
            Assert.Equal("/signin?purpose=signin&outcome=signed_in", SignInDriver.Location(ok));
        }

        // A mismatched binder on a LIVE state is invalid too: it burns that state and spends the (now exhausted) invalid bucket.
        var c = f.NewClient();
        var live = await SignInDriver.StartAsync(c);
        var mismatch = await SignInDriver.CallbackAsync(c, $"state={live.State}&code=x", "__Host-cc.signin=" + RandomToken());
        Assert.Equal("/signin?purpose=signin&outcome=rate_limited", SignInDriver.Location(mismatch));
        var hex = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(live.State))).ToLowerInvariant();
        Assert.Equal(1L, await f.ScalarAsync($"select count(*) from identity.oauth_states where encode(state_hash, 'hex') = '{hex}' and consumed_at is not null"));
    }
}

public sealed class JwksFactory : GoogleFactory;

/// <summary>O5: stale Google keys are trusted for at most 24 hours past the cache expiry while Google's key endpoint fails.</summary>
public class GoogleJwksStalenessTests(JwksFactory f) : IClassFixture<JwksFactory>
{
    private static string Sub() => "1" + Random.Shared.NextInt64(100000000000000000, 999999999999999999);
    private async Task<string> SignInAsync() => SignInDriver.Location((await f.SignInAsync(Sub(), ApiFactory.InvitedEmail)).Response);

    [Fact]
    public async Task Stale_keys_work_within_the_grace_fail_after_it_and_a_cold_cache_without_keys_fails()
    {
        f.Fake.JwksFails = true;
        Assert.EndsWith("provider_unavailable", await SignInAsync()); // never fetched: no keys at all

        f.Fake.JwksFails = false;
        f.Clock.Advance(TimeSpan.FromMinutes(2)); // past the one-minute pause after a failed fetch
        Assert.EndsWith("signed_in", await SignInAsync()); // cache filled; the fake says max-age 6 hours

        f.Fake.JwksFails = true;
        f.Clock.Advance(TimeSpan.FromHours(7)); // expired one hour ago; the refetch fails
        Assert.EndsWith("signed_in", await SignInAsync());

        f.Clock.Advance(TimeSpan.FromHours(24)); // now 25 hours past the expiry
        Assert.EndsWith("provider_unavailable", await SignInAsync());

        f.Fake.JwksFails = false;
        f.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.EndsWith("signed_in", await SignInAsync()); // recovers once Google answers again
    }
}

/// <summary>O6: the user insert and the session insert are one transaction.</summary>
public class GoogleSignInTransactionTests(GoogleSignInFactory f) : IClassFixture<GoogleSignInFactory>
{
    [Fact]
    public async Task A_failure_after_the_user_row_is_written_leaves_no_user_and_no_session_behind()
    {
        var sub = "1" + Random.Shared.NextInt64(100000000000000000, 999999999999999999);
        var fn = "fail_session_" + sub;
        _ = f.NewClient(); // starts the host, which runs the migrations
        // The trigger makes the session insert fail for this one subject only.
        await f.ExecAsync(
            $"create function identity.{fn}() returns trigger language plpgsql as $f$ begin " +
            $"if exists (select 1 from identity.users u where u.id = new.user_id and u.external_subject = 'google:{sub}') then " +
            "raise exception 'test: session insert refused'; end if; return new; end $f$; " +
            $"create trigger {fn} before insert on identity.sessions for each row execute function identity.{fn}();");
        try
        {
            var (res, _, _) = await f.SignInAsync(sub, ApiFactory.InvitedEmail);
            AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(res), "provider_unavailable");
            Assert.Equal(0L, await f.ScalarAsync($"select count(*) from identity.users where external_subject = 'google:{sub}'"));
        }
        finally { await f.ExecAsync($"drop trigger {fn} on identity.sessions; drop function identity.{fn}();"); }
    }
}

/// <summary>O2 (ADR-022 R6): the forwarded-chain measurement logs shape only, the first 20 requests and then once an hour.</summary>
public class ForwardedChainLogTests(ForwardedChainLogTests.Factory f) : IClassFixture<ForwardedChainLogTests.Factory>
{
    public sealed class Factory : GoogleFactory
    {
        public LogCanaryTests.CapturingProvider Logs { get; } = new();
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddSingleton<ILoggerProvider>(Logs));
        }
    }

    private static readonly string[] Addresses = ["203.0.113.77", "10.9.8.7", "2001:db8:abcd::77", "198.51.100.23", "100.64.1.2", "fd12:3456::9"];

    [Fact]
    public async Task Only_the_shape_of_the_chain_is_logged_never_an_address()
    {
        var c = f.NewClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/methods");
        req.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.77, 10.9.8.7:51000, [2001:db8:abcd::77]:443, garbage-host.example.invalid");
        req.Headers.TryAddWithoutValidation("X-Forwarded-For", "100.64.1.2, fd12:3456::9");
        req.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        req.Headers.TryAddWithoutValidation("X-Test-Remote-Ip", "198.51.100.23");
        await c.SendAsync(req);
        await Task.Delay(100);

        var lines = f.Logs.Lines.ToArray();
        var chain = Assert.Single(lines, l => l.Contains("Forwarded chain", StringComparison.Ordinal) && l.Contains("6 X-Forwarded-For entries", StringComparison.Ordinal));
        Assert.Contains("[public-v4,private-v4,public-v6,unparsable,private-v4,private-v6]", chain);
        Assert.Contains("connecting peer public-v4", chain);
        Assert.Contains("X-Forwarded-Proto present: True", chain);
        Assert.Contains("X-Forwarded-Host present: False", chain);
        foreach (var a in Addresses.Append("garbage-host").Append("51000"))
            Assert.DoesNotContain(lines, l => l.Contains(a, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Twenty_samples_after_start_then_at_most_one_an_hour()
    {
        var clock = new TestClock();
        var log = new ForwardedChainLog(clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<ForwardedChainLog>.Instance);
        Assert.Equal(20, Enumerable.Range(0, 25).Count(_ => log.ShouldLog()));
        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.False(log.ShouldLog());
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(log.ShouldLog());
        Assert.False(log.ShouldLog());
    }

    [Theory]
    [InlineData("8.8.8.8", "public-v4")]
    [InlineData("172.20.0.1", "private-v4")]
    [InlineData("172.32.0.1", "public-v4")]
    [InlineData("192.168.1.1:8080", "private-v4")]
    [InlineData("127.0.0.1", "private-v4")]
    [InlineData("::ffff:10.0.0.1", "private-v4")]
    [InlineData("::1", "private-v6")]
    [InlineData("fe80::1", "private-v6")]
    [InlineData("2606:4700::1111", "public-v6")]
    [InlineData("unknown", "unparsable")]
    public void Entries_are_classified_without_keeping_the_value(string raw, string expected) => Assert.Equal(expected, ForwardedChainLog.Classify(raw));
}
