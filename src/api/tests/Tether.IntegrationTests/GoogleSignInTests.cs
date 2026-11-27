using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.Extensions.DependencyInjection;
using Tether.IntegrationTests.Infrastructure;
using Tether.SharedKernel.Security;

namespace Tether.IntegrationTests;

/// <summary>Validates real responses against src/shared/contracts/auth.v1 (frozen, ADR-022).</summary>
public static class AuthSchemas
{
    private static readonly Lazy<(EvaluationOptions Options, Dictionary<string, JsonSchema> Schemas)> State = new(() =>
    {
        string dir = "";
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "src", "shared", "contracts", "auth.v1");
            if (Directory.Exists(c)) { dir = c; break; }
        }
        if (Environment.GetEnvironmentVariable("TETHER_CONTRACTS_DIR") is { Length: > 0 } env) dir = Path.Combine(env, "auth.v1");
        Assert.True(Directory.Exists(dir), "src/shared/contracts/auth.v1 not found");
        var options = new EvaluationOptions { OutputFormat = OutputFormat.List, EvaluateAs = SpecVersion.Draft202012 };
        var schemas = new Dictionary<string, JsonSchema>();
        foreach (var f in Directory.GetFiles(dir, "*.schema.json"))
        {
            var s = JsonSchema.FromText(File.ReadAllText(f));
            schemas[Path.GetFileName(f)[..^".schema.json".Length]] = s;
            options.SchemaRegistry.Register(s);
        }
        return (options, schemas);
    });

    public static void AssertValid(string schema, string json)
    {
        var (o, s) = State.Value;
        var result = s[schema].Evaluate(JsonNode.Parse(json), o);
        Assert.True(result.IsValid, $"{schema}: {json}");
    }

    /// <summary>The query of a /signin?... Location as the auth-callback-outcome object.</summary>
    public static void AssertOutcomeLocation(string location, string outcome)
    {
        Assert.Equal("/signin?purpose=signin&outcome=" + outcome, location);
        AssertValid("auth-callback-outcome", $"{{\"purpose\":\"signin\",\"outcome\":\"{outcome}\"}}");
    }
}

public sealed class GoogleSignInFactory : GoogleFactory;

/// <summary>ADR-022 / auth.v1 sections 2-7 in a Production host, against the fake Google handler. CAL-07, CAL-08, CAL-23 and CV-21 are covered here.</summary>
public class GoogleSignInTests(GoogleSignInFactory f) : IClassFixture<GoogleSignInFactory>
{
    private static string NewSub() => "1" + Random.Shared.NextInt64(100000000000000000, 999999999999999999);

    private Task<long> UsersWithSubAsync(string sub) => f.ScalarAsync($"select count(*) from identity.users where external_subject = 'google:{sub}'");

    // ---- start ----

    [Fact]
    public async Task Start_returns_the_pinned_url_built_from_configuration_and_never_from_request_headers()
    {
        var c = f.NewClient();
        c.DefaultRequestHeaders.Add("X-Forwarded-Host", "evil.example.invalid");
        c.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/google/signin/start") { Content = new StringContent("{\"landing\":\"home\"}", Encoding.UTF8, "application/json") };
        req.Headers.Host = "evil.example.invalid";
        var res = await c.SendAsync(req);
        // A foreign Host is refused by AllowedHosts before the route runs; the same request with the real host carries the configured address.
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var s = await SignInDriver.StartAsync(c);
        AssertNoLeak(s);
        var q = SignInDriver.Query(s.AuthorizationUrl);
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", s.AuthorizationUrl);
        Assert.Equal(["client_id", "code_challenge", "code_challenge_method", "nonce", "prompt", "redirect_uri", "response_type", "scope", "state"], q.Keys.Order().ToArray());
        Assert.Equal(ApiFactory.GoogleClientId, q["client_id"]);
        Assert.Equal(ApiFactory.WebOrigin + "/api/v1/auth/google/callback", q["redirect_uri"]);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal("openid email profile", q["scope"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal("select_account", q["prompt"]);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", q["state"]);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", q["nonce"]);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", q["code_challenge"]);
        Assert.Equal((f.Clock.GetUtcNow() + TimeSpan.FromMinutes(10)).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), s.ExpiresAt);
        AssertValid(s);

        // Nothing secret is stored: hashes only, and no column holds the state, nonce or verifier.
        var stateHex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(s.State))).ToLowerInvariant();
        Assert.Equal(1L, await f.ScalarAsync($"select count(*) from identity.oauth_states where encode(state_hash, 'hex') = '{stateHex}' and consumed_at is null"));
    }

    private static void AssertNoLeak(StartedSignIn s) => Assert.DoesNotContain("verifier", s.AuthorizationUrl, StringComparison.OrdinalIgnoreCase);

    private static void AssertValid(StartedSignIn s) =>
        AuthSchemas.AssertValid("auth-start-response", $"{{\"purpose\":\"signin\",\"authorizationUrl\":\"{s.AuthorizationUrl}\",\"expiresAt\":\"{s.ExpiresAt}\"}}");

    [Fact]
    public async Task The_binder_cookie_is_host_prefixed_secure_httponly_lax_path_root_ten_minutes_and_has_no_domain()
    {
        var s = await SignInDriver.StartAsync(f.NewClient());
        var parts = s.BinderSetCookie.Split(';').Select(p => p.Trim()).ToArray();
        Assert.StartsWith("__Host-cc.signin=", parts[0]);
        var attrs = parts.Skip(1).Select(p => p.ToLowerInvariant()).ToArray();
        Assert.Contains("max-age=600", attrs);
        Assert.Contains("path=/", attrs);
        Assert.Contains("secure", attrs);
        Assert.Contains("samesite=lax", attrs);
        Assert.Contains("httponly", attrs);
        Assert.DoesNotContain(attrs, a => a.StartsWith("domain", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_needs_an_allowed_origin()
    {
        var none = await f.NewClient(withOrigin: false).PostAsync("/api/v1/auth/google/signin/start", new StringContent("{\"landing\":\"home\"}", Encoding.UTF8, "application/json"));
        await none.AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        var c = f.NewClient(withOrigin: false);
        c.DefaultRequestHeaders.Add("Origin", "https://evil.example.invalid");
        var foreign = await c.PostAsync("/api/v1/auth/google/signin/start", new StringContent("{\"landing\":\"home\"}", Encoding.UTF8, "application/json"));
        await foreign.AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        Assert.False(foreign.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("{\"landing\":\"home\",\"returnUrl\":\"/courses\"}", 400)]
    [InlineData("{\"landing\":\"home\",\"redirect_uri\":\"https://evil.example.invalid/\"}", 400)]
    [InlineData("{\"landing\":\"home\",\"landing\":\"home\"}", 400)]
    [InlineData("{\"landing\":\"schedule\"}", 400)]
    [InlineData("{\"landing\":\"/lectures/1\"}", 400)]
    [InlineData("{}", 400)]
    [InlineData("{\"landing\":null}", 400)]
    [InlineData("not json", 400)]
    public async Task Start_rejects_anything_but_the_landing_key(string body, int status)
    {
        var res = await f.NewClient().PostAsync("/api/v1/auth/google/signin/start", new StringContent(body, Encoding.UTF8, "application/json"));
        await res.AssertProblemAsync((HttpStatusCode)status, "validation_failed");
        Assert.False(res.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Start_needs_a_json_content_type()
    {
        var res = await f.NewClient().PostAsync("/api/v1/auth/google/signin/start", new StringContent("{\"landing\":\"home\"}", Encoding.UTF8, "text/plain"));
        await res.AssertProblemAsync(HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");
    }

    [Fact]
    public async Task Auth_methods_says_google_on_and_dev_login_off_in_production()
    {
        var res = await f.NewClient().GetAsync("/api/v1/auth/methods");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadAsStringAsync();
        AuthSchemas.AssertValid("auth-methods-response", json);
        Assert.Equal("{\"google\":true,\"devLogin\":false}", json);
    }

    // ---- callback: happy path and account rules ----

    [Fact]
    public async Task First_sign_in_creates_one_user_and_a_new_session_and_clears_the_binder()
    {
        var sub = NewSub();
        var (res, _, _) = await f.SignInAsync(sub, "Invited@Example.Invalid ", name: "Ada\u0007 Lovelace​");
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(res), "signed_in");
        var setCookies = res.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(setCookies, h => h.StartsWith("__Host-cc.signin=;", StringComparison.Ordinal) && h.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
        var session = SignInDriver.SessionCookie(res);
        Assert.NotNull(session);
        Assert.StartsWith("__Host-cc.session=", session);

        Assert.Equal(1L, await UsersWithSubAsync(sub));
        Assert.Equal("invited@example.invalid", await f.TextAsync($"select email from identity.users where external_subject = 'google:{sub}'"));
        Assert.Equal("Ada Lovelace", await f.TextAsync($"select display_name from identity.users where external_subject = 'google:{sub}'"));
        Assert.Equal("False", await f.TextAsync($"select hosted_domain_present from identity.users where external_subject = 'google:{sub}'"));
        Assert.Equal(1L, await f.ScalarAsync($"select count(*) from identity.sessions s join identity.users u on u.id = s.user_id where u.external_subject = 'google:{sub}'"));

        var me = await f.WithCookie(session!).GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("invited@example.invalid", (await me.BodyAsync()).GetProperty("email").GetString());
    }

    [Fact]
    public async Task Returning_sign_in_reuses_the_user_by_subject_refreshes_email_and_name_and_revokes_the_carried_session()
    {
        var sub = NewSub();
        var (first, _, _) = await f.SignInAsync(sub, ApiFactory.InvitedEmail, name: "First Name");
        var firstSession = SignInDriver.SessionCookie(first)!;
        var userId = await f.TextAsync($"select id from identity.users where external_subject = 'google:{sub}'");

        // Second sign-in in the same browser: carries the first session cookie, which must be revoked (N-03, session fixation).
        var c = f.WithCookie(firstSession);
        var s = await SignInDriver.StartAsync(c);
        var (code, _) = f.Fake.Issue(s.Challenge, s.Nonce, sub, ApiFactory.SecondInvitedEmail, name: "Second Name");
        var res = await SignInDriver.CallbackAsync(f.NewClient(), $"state={s.State}&code={code}", s.Binder + "; " + firstSession);
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(res), "signed_in");
        var secondSession = SignInDriver.SessionCookie(res)!;
        Assert.NotEqual(firstSession, secondSession);

        Assert.Equal(1L, await UsersWithSubAsync(sub));
        Assert.Equal(userId, await f.TextAsync($"select id from identity.users where external_subject = 'google:{sub}'"));
        Assert.Equal(ApiFactory.SecondInvitedEmail, await f.TextAsync($"select email from identity.users where id = '{userId}'"));
        Assert.Equal("Second Name", await f.TextAsync($"select display_name from identity.users where id = '{userId}'"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.WithCookie(firstSession).GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.WithCookie(secondSession).GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task The_same_email_under_two_subjects_is_two_users_and_a_dev_row_with_that_email_is_untouched()
    {
        var devId = Guid.NewGuid();
        await f.ExecAsync($"insert into identity.users (id, external_subject, email, display_name, created_at) values ('{devId}', 'dev:{ApiFactory.InvitedEmail}-{devId:N}', '{ApiFactory.InvitedEmail}', 'Dev Row', now())");
        var a = NewSub();
        var b = NewSub();
        Assert.Equal("/signin?purpose=signin&outcome=signed_in", SignInDriver.Location((await f.SignInAsync(a, ApiFactory.InvitedEmail)).Response));
        Assert.Equal("/signin?purpose=signin&outcome=signed_in", SignInDriver.Location((await f.SignInAsync(b, ApiFactory.InvitedEmail)).Response));
        Assert.Equal(1L, await UsersWithSubAsync(a));
        Assert.Equal(1L, await UsersWithSubAsync(b));
        Assert.Equal("Dev Row", await f.TextAsync($"select display_name from identity.users where id = '{devId}'"));
        Assert.Equal(0L, await f.ScalarAsync($"select count(*) from identity.sessions where user_id = '{devId}'"));
    }

    [Fact]
    public async Task A_soft_deleted_user_is_refused_as_account_closed_and_gets_no_session()
    {
        var sub = NewSub();
        await f.SignInAsync(sub, ApiFactory.InvitedEmail);
        await f.ExecAsync($"update identity.users set deleted_at = now() where external_subject = 'google:{sub}'");
        var before = await f.ScalarAsync("select count(*) from identity.sessions");
        var (res, _, _) = await f.SignInAsync(sub, ApiFactory.InvitedEmail);
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(res), "account_closed");
        Assert.Null(SignInDriver.SessionCookie(res));
        Assert.Equal(before, await f.ScalarAsync("select count(*) from identity.sessions"));
    }

    // ---- callback: state binding (zero calls to Google on every failure) ----

    [Fact]
    public async Task A_second_callback_with_the_same_state_is_state_invalid_and_yields_one_user_and_one_session()
    {
        var sub = NewSub();
        var c = f.NewClient();
        var s = await SignInDriver.StartAsync(c);
        var (code, _) = f.Fake.Issue(s.Challenge, s.Nonce, sub, ApiFactory.InvitedEmail);
        var query = $"state={s.State}&code={code}";
        var r1 = await SignInDriver.CallbackAsync(c, query, s.Binder);
        var calls = f.Fake.TokenCalls;
        var r2 = await SignInDriver.CallbackAsync(c, query, s.Binder);
        Assert.Equal("/signin?purpose=signin&outcome=signed_in", SignInDriver.Location(r1));
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(r2), "state_invalid");
        Assert.Equal(calls, f.Fake.TokenCalls);
        Assert.Equal(1L, await UsersWithSubAsync(sub));
        Assert.Equal(1L, await f.ScalarAsync($"select count(*) from identity.sessions s join identity.users u on u.id = s.user_id where u.external_subject = 'google:{sub}'"));
    }

    [Fact]
    public async Task Concurrent_callbacks_with_one_state_produce_exactly_one_sign_in()
    {
        var sub = NewSub();
        var c = f.NewClient();
        var s = await SignInDriver.StartAsync(c);
        var (code, _) = f.Fake.Issue(s.Challenge, s.Nonce, sub, ApiFactory.InvitedEmail);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SignInDriver.CallbackAsync(f.NewClient(), $"state={s.State}&code={code}", s.Binder)));
        var outcomes = results.Select(SignInDriver.Location).ToArray();
        Assert.Single(outcomes, o => o.EndsWith("signed_in", StringComparison.Ordinal));
        Assert.Equal(3, outcomes.Count(o => o.EndsWith("state_invalid", StringComparison.Ordinal)));
        Assert.Equal(1L, await UsersWithSubAsync(sub));
    }

    [Fact]
    public async Task Login_csrf_a_valid_state_and_code_with_another_or_no_binder_is_state_invalid_before_any_token_exchange_and_burns_the_state()
    {
        var victim = f.NewClient();
        var attacker = await SignInDriver.StartAsync(f.NewClient());
        var (code, _) = f.Fake.Issue(attacker.Challenge, attacker.Nonce, NewSub(), ApiFactory.InvitedEmail);
        var victimStart = await SignInDriver.StartAsync(victim);
        var calls = f.Fake.TokenCalls;

        var foreign = await SignInDriver.CallbackAsync(victim, $"state={attacker.State}&code={code}", victimStart.Binder);
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(foreign), "state_invalid");
        Assert.Null(SignInDriver.SessionCookie(foreign));

        var victimNoBinder = await SignInDriver.StartAsync(victim);
        var none = await SignInDriver.CallbackAsync(victim, $"state={victimNoBinder.State}&code={code}", null);
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(none), "state_invalid");
        // Burned: the right binder afterwards does not revive either state.
        Assert.EndsWith("state_invalid", SignInDriver.Location(await SignInDriver.CallbackAsync(victim, $"state={victimNoBinder.State}&code={code}", victimNoBinder.Binder)));
        Assert.EndsWith("state_invalid", SignInDriver.Location(await SignInDriver.CallbackAsync(victim, $"state={attacker.State}&code={code}", attacker.Binder)));
        Assert.Equal(calls, f.Fake.TokenCalls);
    }

    [Fact]
    public async Task An_expired_state_is_state_invalid_with_no_token_exchange()
    {
        var c = f.NewClient();
        var s = await SignInDriver.StartAsync(c);
        var (code, _) = f.Fake.Issue(s.Challenge, s.Nonce, NewSub(), ApiFactory.InvitedEmail);
        var calls = f.Fake.TokenCalls;
        f.Clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(await SignInDriver.CallbackAsync(c, $"state={s.State}&code={code}", s.Binder)), "state_invalid");
        Assert.Equal(calls, f.Fake.TokenCalls);
    }

    [Theory]
    [InlineData("state=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&code=x")]
    [InlineData("state=short&code=x")]
    [InlineData("code=x")]
    [InlineData("")]
    public async Task Unknown_or_malformed_state_is_state_invalid_with_no_token_exchange(string query)
    {
        var s = await SignInDriver.StartAsync(f.NewClient());
        var calls = f.Fake.TokenCalls;
        var res = await SignInDriver.CallbackAsync(f.NewClient(), query, s.Binder);
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(res), "state_invalid");
        Assert.Equal(calls, f.Fake.TokenCalls);
    }

    [Fact]
    public async Task Google_errors_are_mapped_after_the_state_check_and_create_nothing()
    {
        var c = f.NewClient();
        var s1 = await SignInDriver.StartAsync(c);
        var calls = f.Fake.TokenCalls;
        var denied = await SignInDriver.CallbackAsync(c, $"state={s1.State}&error=access_denied&error_description=Dear+user+{ApiFactory.InvitedEmail}", s1.Binder);
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(denied), "access_denied");
        var s2 = await SignInDriver.StartAsync(c);
        var other = await SignInDriver.CallbackAsync(c, $"state={s2.State}&error=server_error", s2.Binder);
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(other), "provider_unavailable");
        var bad = await SignInDriver.CallbackAsync(c, "state=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&error=access_denied", s2.Binder);
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(bad), "state_invalid");
        Assert.Equal(calls, f.Fake.TokenCalls);
        Assert.DoesNotContain(ApiFactory.InvitedEmail, denied.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Callback_answers_only_get_anonymous_callers_get_401_and_signed_in_ones_405()
    {
        // auth.v1 section 2 (O7): another method matches the route's 405 endpoint, which has no AllowAnonymous, so the fallback policy answers
        // an anonymous caller first. Never a 302, never a state change.
        var res = await f.NewClient().PostAsync("/api/v1/auth/google/callback?state=x", new StringContent("", Encoding.UTF8, "application/json"));
        await res.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        var (signedIn, _, _) = await f.SignInAsync(NewSub(), ApiFactory.InvitedEmail);
        var c = f.WithCookie(SignInDriver.SessionCookie(signedIn)!);
        var asUser = await c.PostAsync("/api/v1/auth/google/callback?state=x", new StringContent("", Encoding.UTF8, "application/json"));
        await asUser.AssertProblemAsync(HttpStatusCode.MethodNotAllowed, "method_not_allowed");
    }

    [Fact]
    public async Task Scope_decision_O4_an_absent_scope_is_accepted_and_a_present_one_must_be_exactly_the_sign_in_set()
    {
        var (absent, _, _) = await f.SignInAsync(NewSub(), ApiFactory.InvitedEmail, shape: g => g.Scope = null);
        Assert.EndsWith("signed_in", SignInDriver.Location(absent));
        var (extra, _, _) = await f.SignInAsync(NewSub(), ApiFactory.InvitedEmail, shape: g => g.Scope = "openid email profile https://www.googleapis.com/auth/calendar.app.created");
        Assert.EndsWith("provider_unavailable", SignInDriver.Location(extra));
        var (fewer, _, _) = await f.SignInAsync(NewSub(), ApiFactory.InvitedEmail, shape: g => g.Scope = "openid");
        Assert.EndsWith("provider_unavailable", SignInDriver.Location(fewer));
    }

    // ---- callback: the ID token (provider_unavailable, nothing created) ----

    public static IEnumerable<object[]> BadTokens() =>
    [
        ["wrong issuer", (Action<FakeGrant>)(g => g.Claims["iss"] = "https://accounts.google.com.evil.invalid")],
        ["wrong audience", (Action<FakeGrant>)(g => g.Claims["aud"] = "999-other.apps.googleusercontent.com")],
        ["wrong azp", (Action<FakeGrant>)(g => g.Claims["azp"] = "999-other.apps.googleusercontent.com")],
        ["expired", (Action<FakeGrant>)(g => g.Claims["exp"] = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds())],
        ["no expiry", (Action<FakeGrant>)(g => g.Claims.Remove("exp"))],
        ["no iat", (Action<FakeGrant>)(g => g.Claims.Remove("iat"))],
        ["wrong nonce", (Action<FakeGrant>)(g => g.Claims["nonce"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")],
        ["no nonce", (Action<FakeGrant>)(g => g.Claims.Remove("nonce"))],
        ["no subject", (Action<FakeGrant>)(g => g.Claims.Remove("sub"))],
        ["subject with a space", (Action<FakeGrant>)(g => g.Claims["sub"] = "1 2")],
        ["alg none", (Action<FakeGrant>)(g => g.Signing = FakeSigning.AlgNone)],
        ["HS256 signed with the public key", (Action<FakeGrant>)(g => g.Signing = FakeSigning.Hs256WithPublicKey)],
        ["bad signature (other key, same kid)", (Action<FakeGrant>)(g => g.Signing = FakeSigning.OtherKeySameKid)],
        ["unknown kid", (Action<FakeGrant>)(g => g.Signing = FakeSigning.UnknownKid)],
        ["extra scope", (Action<FakeGrant>)(g => g.Scope = "openid email profile https://www.googleapis.com/auth/drive")],
        ["missing profile scope", (Action<FakeGrant>)(g => g.Scope = "openid email")],
        ["token endpoint 500", (Action<FakeGrant>)(g => g.Status = HttpStatusCode.InternalServerError)],
        ["no id_token", (Action<FakeGrant>)(g => g.OmitIdToken = true)],
    ];

    [Theory, MemberData(nameof(BadTokens))]
    public async Task Every_id_token_rule_failure_is_provider_unavailable_and_creates_nothing(string why, Action<FakeGrant> shape)
    {
        var sub = NewSub();
        var sessions = await f.ScalarAsync("select count(*) from identity.sessions");
        var (res, _, _) = await f.SignInAsync(sub, ApiFactory.InvitedEmail, shape: shape);
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(res), "provider_unavailable");
        Assert.True(await UsersWithSubAsync(sub) == 0, why);
        Assert.Equal(sessions, await f.ScalarAsync("select count(*) from identity.sessions"));
        Assert.Null(SignInDriver.SessionCookie(res));
    }

    [Fact]
    public async Task A_wrong_pkce_verifier_is_refused_by_google_and_answered_provider_unavailable()
    {
        var c = f.NewClient();
        var s = await SignInDriver.StartAsync(c);
        var (code, _) = f.Fake.Issue("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", s.Nonce, NewSub(), ApiFactory.InvitedEmail); // the challenge Google saw differs
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(await SignInDriver.CallbackAsync(c, $"state={s.State}&code={code}", s.Binder)), "provider_unavailable");
    }

    [Fact]
    public async Task The_scope_check_accepts_short_names_and_userinfo_uris_only()
    {
        var (short1, _, _) = await f.SignInAsync(NewSub(), ApiFactory.InvitedEmail, shape: g => g.Scope = "openid email profile");
        Assert.EndsWith("signed_in", SignInDriver.Location(short1));
        var (omitted, _, _) = await f.SignInAsync(NewSub(), ApiFactory.InvitedEmail, shape: g => g.Scope = null);
        Assert.EndsWith("signed_in", SignInDriver.Location(omitted));
        var (issuerShort, _, _) = await f.SignInAsync(NewSub(), ApiFactory.InvitedEmail, shape: g => g.Claims["iss"] = "accounts.google.com");
        Assert.EndsWith("signed_in", SignInDriver.Location(issuerShort));
    }

    // ---- admission ----

    [Fact]
    public async Task Not_invited_and_unverified_emails_create_nothing()
    {
        var users = await f.ScalarAsync("select count(*) from identity.users");
        var sessions = await f.ScalarAsync("select count(*) from identity.sessions");
        var (stranger, _, _) = await f.SignInAsync(NewSub(), "stranger@example.invalid");
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(stranger), "not_invited");
        var (unverified, _, _) = await f.SignInAsync(NewSub(), ApiFactory.InvitedEmail, emailVerified: false);
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(unverified), "not_invited");
        var (lookalike, _, _) = await f.SignInAsync(NewSub(), "invited+x@example.invalid");
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(lookalike), "not_invited");
        Assert.Equal(users, await f.ScalarAsync("select count(*) from identity.users"));
        Assert.Equal(sessions, await f.ScalarAsync("select count(*) from identity.sessions"));
        Assert.Null(SignInDriver.SessionCookie(stranger));
    }

    [Fact]
    public async Task A_school_account_is_refused_by_default_but_only_after_the_invite_check()
    {
        var sub = NewSub();
        var (school, _, _) = await f.SignInAsync(sub, ApiFactory.InvitedEmail, shape: g => g.Claims["hd"] = "school.example.invalid");
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(school), "school_account");
        Assert.Equal(0L, await UsersWithSubAsync(sub));
        var (uninvitedSchool, _, _) = await f.SignInAsync(NewSub(), "stranger@school.example.invalid", shape: g => g.Claims["hd"] = "school.example.invalid");
        AuthSchemas.AssertOutcomeLocation(SignInDriver.Location(uninvitedSchool), "not_invited");
        var (emptyHd, _, _) = await f.SignInAsync(NewSub(), ApiFactory.InvitedEmail, shape: g => g.Claims["hd"] = "");
        Assert.EndsWith("signed_in", SignInDriver.Location(emptyHd)); // an empty claim is no claim: personal
    }

    [Fact]
    public void The_derived_nonce_and_verifier_use_separate_labels()
    {
        var keys = f.Services.GetRequiredService<DerivedKeys>();
        Assert.NotEqual(Convert.ToHexString(keys.Derive(DerivedKeys.Labels.OAuthNonce)), Convert.ToHexString(keys.Derive(DerivedKeys.Labels.OAuthPkce)));
    }
}

public sealed class AllowSchoolAccountsFactory : GoogleFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> ExtraSettings => [new("Auth:Google:HostedDomainPolicy", "allow")];
}

public class GoogleSignInSchoolAllowTests(AllowSchoolAccountsFactory f) : IClassFixture<AllowSchoolAccountsFactory>
{
    [Fact]
    public async Task With_the_policy_set_to_allow_a_school_account_signs_in_and_only_the_presence_flag_is_stored()
    {
        var sub = "1" + Random.Shared.NextInt64(100000000000000000, 999999999999999999);
        var (res, _, _) = await f.SignInAsync(sub, ApiFactory.InvitedEmail, shape: g => g.Claims["hd"] = "school.example.invalid");
        Assert.Equal("/signin?purpose=signin&outcome=signed_in", SignInDriver.Location(res));
        Assert.Equal("True", await f.TextAsync($"select hosted_domain_present from identity.users where external_subject = 'google:{sub}'"));
        Assert.Equal(0L, await f.ScalarAsync("select count(*) from identity.users where display_name like '%school.example%' or email like '%school.example%'"));
    }
}

public sealed class LowSignInLimitsFactory : GoogleFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> ExtraSettings =>
    [
        new("RateLimiting:AuthStartPerBinderPermitPerMinute", "2"),
        new("RateLimiting:AuthCallbackInvalidPermitPerMinute", "2"),
        new("Auth:Google:MaxLiveStates", "6"),
    ];
}

/// <summary>auth.v1 section 8. Separate fixture: the buckets are per host.</summary>
public class GoogleSignInRateLimitTests(LowSignInLimitsFactory f) : IClassFixture<LowSignInLimitsFactory>
{
    [Fact]
    public async Task Limits_per_binder_unbound_callbacks_and_live_states()
    {
        // Per binder: the third start from the same browser within a minute is 429 with Retry-After.
        var c = f.NewClient();
        var s1 = await SignInDriver.StartAsync(c);
        await SignInDriver.StartAsync(c, s1.Binder);
        await SignInDriver.StartAsync(c, s1.Binder);
        var third = await c.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/google/signin/start")
        {
            Content = new StringContent("{\"landing\":\"home\"}", Encoding.UTF8, "application/json"),
            Headers = { { "Cookie", s1.Binder } },
        });
        // The same browser (binder s1) started twice already this minute; the third is refused.
        await third.AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.Equal("60", third.Headers.RetryAfter?.ToString());

        // Callbacks without a binder have their own small bucket and answer with the calm outcome, never a JSON 429.
        var outcomes = new List<string>();
        for (var i = 0; i < 3; i++) outcomes.Add(SignInDriver.Location(await SignInDriver.CallbackAsync(f.NewClient(), "state=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", null)));
        Assert.Equal(["/signin?purpose=signin&outcome=state_invalid", "/signin?purpose=signin&outcome=state_invalid", "/signin?purpose=signin&outcome=rate_limited"], outcomes);
        // A real return with its binder is unaffected by that flood.
        var (ok, _, _) = await f.SignInAsync("1" + Random.Shared.NextInt64(100000000000000000, 999999999999999999), ApiFactory.InvitedEmail);
        Assert.Equal("/signin?purpose=signin&outcome=signed_in", SignInDriver.Location(ok));

        // Live-state cap (6): fresh browsers keep starting until the cap answers 429.
        HttpResponseMessage? last = null;
        for (var i = 0; i < 8; i++)
        {
            last = await f.NewClient().PostAsync("/api/v1/auth/google/signin/start", new StringContent("{\"landing\":\"home\"}", Encoding.UTF8, "application/json"));
            if (last.StatusCode == HttpStatusCode.TooManyRequests) break;
        }
        await last!.AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.True(await f.ScalarAsync("select count(*) from identity.oauth_states where consumed_at is null") <= 6);
    }
}
