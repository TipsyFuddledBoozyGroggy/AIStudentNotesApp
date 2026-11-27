using System.Net;
using System.Net.Http.Json;
using Tether.IntegrationTests.Infrastructure;

namespace Tether.IntegrationTests;

/// <summary>
/// ADR-022 section 2: the startup refusals outside Development (each names its key, never a value), the startup revocation (R3, R8), the
/// dev-user rule in the session check, and the key ring across two hosts over one database (R4).
/// </summary>
public class ProductionStartupTests
{
    private sealed class Prod(IEnumerable<KeyValuePair<string, string?>> settings, bool dropGoogleDefaults = false, string env = "Production") : ApiFactory
    {
        protected override string EnvironmentName => env;
        protected override IEnumerable<KeyValuePair<string, string?>> GoogleSettings => dropGoogleDefaults ? [new("Security:AllowedOrigins:1", WebOrigin)] : base.GoogleSettings;
        protected override IEnumerable<KeyValuePair<string, string?>> Settings => settings;
    }

    private static async Task<List<string>> StartupFailureAsync(ApiFactory f)
    {
        await ((IAsyncLifetime)f).InitializeAsync();
        try
        {
            var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
            var chain = new List<string>();
            for (Exception? e = ex; e is not null; e = e.InnerException) chain.Add(e.Message);
            return chain;
        }
        finally { await ((IAsyncLifetime)f).DisposeAsync(); }
    }

    public static IEnumerable<object[]> Refusals() =>
    [
        ["Auth:Google:ClientId", "", "Auth:Google:ClientId"],
        ["Auth:Google:ClientId", "not-a-client-id.example.invalid", "Auth:Google:ClientId"],
        ["Auth:Google:ClientSecret", "", "Auth:Google:ClientSecret"],
        ["Auth:Google:RedirectUri", "http://app.example.test/api/v1/auth/google/callback", "Auth:Google:RedirectUri"],
        ["Auth:Google:RedirectUri", "https://app.example.test/api/v1/auth/google/other", "Auth:Google:RedirectUri"],
        ["Auth:Google:RedirectUri", "https://app.example.test/api/v1/auth/google/callback?x=1", "Auth:Google:RedirectUri"],
        ["Auth:Google:RedirectUri", "https://unlisted.example.invalid/api/v1/auth/google/callback", "Auth:Google:RedirectUri"],
        ["Auth:Google:RedirectUri", "", "Auth:Google:RedirectUri"],
        ["Auth:Google:Admission", "", "Auth:Google:Admission"],
        ["Auth:Google:Admission", "open", "Auth:Google:Admission"],
        ["Auth:Google:HostedDomainPolicy", "warn", "Auth:Google:HostedDomainPolicy"],
        ["Auth:Invite:Emails", "", "Auth:Invite:Emails"],
        ["Auth:Invite:Emails", "someone@example.invalid,not-an-address", "Auth:Invite:Emails entry 2"],
        ["Auth:Invite:Emails", "someone@example.invalid, SOMEONE@example.invalid", "Auth:Invite:Emails entry 2"],
        ["Auth:Invite:Emails", string.Join(",", Enumerable.Range(1, 101).Select(i => $"p{i}@example.invalid")), "Auth:Invite:Emails"],
        ["DataProtection:Certificate", "", "DataProtection:Certificate"],
        ["DataProtection:Certificate", "AAAA", "DataProtection:Certificate"],
    ];

    [Theory, MemberData(nameof(Refusals))]
    public async Task Production_refuses_to_start_with_incomplete_or_unsafe_sign_in_configuration(string key, string value, string expected)
    {
        var chain = await StartupFailureAsync(new Prod([new(key, value)]));
        Assert.Contains(chain, m => m.Contains(expected, StringComparison.Ordinal));
        if (value.Length > 4) Assert.DoesNotContain(chain, m => m.Contains(value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Staging_is_treated_like_production_and_a_missing_google_configuration_refuses_to_start()
    {
        var chain = await StartupFailureAsync(new Prod([], dropGoogleDefaults: true, env: "Staging"));
        Assert.Contains(chain, m => m.Contains("Auth:Google:ClientId is required outside Development", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Development_without_google_configuration_starts_with_google_off_and_the_routes_unmapped()
    {
        var f = new Prod([], dropGoogleDefaults: true, env: "Development");
        await ((IAsyncLifetime)f).InitializeAsync();
        try
        {
            var c = f.NewClient();
            Assert.Equal("{\"google\":false,\"devLogin\":true}", await (await c.GetAsync("/api/v1/auth/methods")).Content.ReadAsStringAsync());
            var start = await c.PostAsJsonAsync("/api/v1/auth/google/signin/start", new { landing = "home" });
            await start.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated"); // not mapped: an unmatched route, like /dev/login in Production
        }
        finally { await ((IAsyncLifetime)f).DisposeAsync(); }
    }

    [Fact]
    public async Task Development_with_google_configured_offers_both_methods()
    {
        var dev = new Prod([], env: "Development");
        await ((IAsyncLifetime)dev).InitializeAsync();
        try
        {
            Assert.Equal("{\"google\":true,\"devLogin\":true}", await (await dev.NewClient().GetAsync("/api/v1/auth/methods")).Content.ReadAsStringAsync());
            var s = await SignInDriver.StartAsync(dev.NewClient());
            Assert.StartsWith("cc.signin=", s.BinderSetCookie); // unprefixed in Development (plain http), like the session cookie
        }
        finally { await ((IAsyncLifetime)dev).DisposeAsync(); }
    }
}

/// <summary>Host 1: Development with dev login (writes the rows a real database would hold before the switch).</summary>
public sealed class SeedingFactory : ApiFactory
{
    public HttpClient WithCookieDev(string cookie)
    {
        var c = NewClient();
        c.DefaultRequestHeaders.Add("Cookie", cookie);
        return c;
    }
}

/// <summary>R3/R8 and the R4 key ring across hosts: two more hosts over the SAME database as <see cref="SeedingFactory"/>.</summary>
public class StartupRevocationTests(SeedingFactory seed) : IClassFixture<SeedingFactory>
{
    private sealed class SecondHost : GoogleFactory;

    [Fact]
    public async Task Production_startup_revokes_dev_and_uninvited_sessions_keeps_invited_ones_and_reads_cookies_minted_by_another_host()
    {
        // Host 1 (Development): a dev-login session, plus Google-subject users with live sessions, one invited and one no longer invited.
        var devClient = await seed.LoginAsync("seed-dev@dev.local");
        Assert.Equal(HttpStatusCode.OK, (await devClient.GetAsync("/api/v1/me")).StatusCode);
        var invitedUser = Guid.NewGuid();
        var removedUser = Guid.NewGuid();
        await seed.ExecAsync($"""
            insert into identity.users (id, external_subject, email, display_name, created_at) values
              ('{invitedUser}', 'google:1110000000000000001', '{ApiFactory.InvitedEmail}', 'Kept', now()),
              ('{removedUser}', 'google:1110000000000000002', 'removed@example.invalid', 'Removed', now());
            insert into identity.sessions (id, user_id, created_at, expires_at) values
              ('{Guid.CreateVersion7()}', '{invitedUser}', now(), now() + interval '14 days'),
              ('{Guid.CreateVersion7()}', '{removedUser}', now(), now() + interval '14 days');
            """);

        // Host 2 (Production, same database): startup revocation runs.
        await using var prod = new SecondHost { DbName = seed.DbName, OwnsDatabase = false };
        _ = prod.NewClient(); // starts host 2
        Assert.Equal(0L, await prod.ScalarAsync("select count(*) from identity.sessions s join identity.users u on u.id = s.user_id where u.external_subject like 'dev:%' and s.revoked_at is null"));
        Assert.Equal(0L, await prod.ScalarAsync($"select count(*) from identity.sessions where user_id = '{removedUser}' and revoked_at is null"));
        Assert.Equal(1L, await prod.ScalarAsync($"select count(*) from identity.sessions where user_id = '{invitedUser}' and revoked_at is null"));

        // The dev cookie from host 1 is readable by host 2 (same key ring, same certificate). Even with its session row un-revoked by hand, a
        // dev user is never valid outside Development (SessionPolicy, R8). The cookie is renamed to the Production name to reach the check.
        var devCookie = devClient.DefaultRequestHeaders.GetValues("Cookie").Single();
        Assert.StartsWith("cc.session=", devCookie);
        await prod.ExecAsync("update identity.sessions s set revoked_at = null from identity.users u where u.id = s.user_id and u.external_subject like 'dev:%'");
        Assert.Equal(HttpStatusCode.Unauthorized, (await prod.WithCookie("__Host-" + devCookie).GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await seed.WithCookieDev(devCookie).GetAsync("/api/v1/me")).StatusCode); // same row, Development: valid

        // Key ring across hosts: a Google sign-in on host 2, then host 3 over the same database accepts that cookie.
        var (res, _, _) = await prod.SignInAsync("1110000000000000003", ApiFactory.InvitedEmail);
        var session = SignInDriver.SessionCookie(res)!;
        Assert.Equal(HttpStatusCode.OK, (await prod.WithCookie(session).GetAsync("/api/v1/me")).StatusCode);
        await using var third = new SecondHost { DbName = seed.DbName, OwnsDatabase = false };
        Assert.Equal(HttpStatusCode.OK, (await third.WithCookie(session).GetAsync("/api/v1/me")).StatusCode);
        var xml = await third.TextAsync("select string_agg(xml, '') from identity.data_protection_keys");
        Assert.Contains("EncryptedData", xml);
        Assert.DoesNotContain("<value>", xml);
    }
}
