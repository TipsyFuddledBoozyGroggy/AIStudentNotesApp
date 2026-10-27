# Milestone 1 Backend Security Review

Reviewer: security-engineer. Commit reviewed: e5739f9. Scope: `src/api` against SEC-001..005, 019, 027..035, 036 in `docs/SECURITY.md` and `docs/architecture/backend-foundation.md`.

Method legend: **READ** = verified by reading code only. **RAN** = exercised by a test I executed. No source was modified.

Test run (RAN): `docker compose -f docker-compose.yml -f docker-compose.dev.yml run --rm dotnet dotnet test src/api/CollegeCompanion.slnx` gave 7/7 architecture and 60/60 integration tests passing. These tests confirm the existing positive behavior only. They do not cover the findings below.

## Release position

No Critical findings. Two High findings (F-01, F-02) must be fixed before any non-dev use, meaning any real Google login or any deployment reachable by someone other than the developer. M1 as a dev-only slice is acceptable to continue. Do not describe SEC-004 or SEC-036 as met.

## Findings

### F-01 High: logout and account deletion do not revoke sessions (SEC-004)
- Evidence: `src/api/src/Modules/Identity/CollegeCompanion.Identity/Endpoints/IdentityEndpoints.cs:30-34` (logout only calls `SignOutAsync`, which deletes the client cookie). `IdentityModule.cs:46-67` (stateless data-protection cookie, no server-side ticket store).
- Effect: a copied or stolen cookie stays valid after logout until it expires. `OnValidatePrincipal` (`IdentityModule.cs:58-66`) never checks the user, so a soft-deleted user (`DeletedAt`) keeps working on `/courses`. Only `/me` re-checks, at `IdentityEndpoints.cs:46`. Verified by READ.
- Lightest acceptable fix: add `identity.sessions(id uuid pk, user_id, created_at, revoked_at)`. Put the session id in a claim at sign-in. In `OnValidatePrincipal`, do a PK lookup and reject if the row is missing or revoked, if `created_at` is older than 14 days, or if the user is deleted. Logout sets `revoked_at` for that one session. Cost is one indexed read per request, about half a day of work.
- Cheaper alternative: a `session_epoch int` on `users`, carried as a claim and bumped on logout. This is less code but signs the student out on all their devices, so I do not recommend it.
- Timing: must land before any non-dev use. In dev-only M1 with seeded users the impact is nil, so it does not need to block M1 completion.

### F-02 High: the 14-day absolute session lifetime is probably not enforced (SEC-004)
- Evidence: `IdentityModule.cs:54-55,60-61`. The check uses `IssuedUtc`, but with `SlidingExpiration = true` the cookie handler re-issues the ticket and resets `IssuedUtc` on each refresh. This is from my knowledge of ASP.NET Core's `CookieAuthenticationHandler`. **READ only; I did not run it.** A session used at least once per 4 hours would never expire.
- Fix: store an immutable `absolute_expires_at` in the `AuthenticationProperties` at sign-in, or use the `identity.sessions.created_at` check from F-01, and add a test with `FakeTimeProvider` that proves a refreshed cookie still dies at 14 days. The current `Production_session_cookie_*` test asserts only config values.

### F-03 High: SEC-036 rate limiting is not met
- Evidence: `Program.cs:25-36` defines one policy, `auth`. It is attached only to `/dev/login` (`IdentityEndpoints.cs:38`). There is no global or per-user limiter. `/courses` writes, `/auth/logout` and `/health/ready` (which opens a DB connection on every call, `HostServices.cs:40-46`) are unlimited. READ.
- Also, the partition key is `RemoteIpAddress` (`Program.cs:29`). No `ForwardedHeaders` configuration exists. Behind a reverse proxy, which the foundation doc assumes, all users share one bucket. If forwarded headers are later enabled carelessly, the key becomes spoofable.
- SEC-005 (lockout and back-off) has no implementation yet. It is vacuous while only dev login exists, but it must be built with Google auth.
- Fix: add a global per-user limiter (fall back to IP when anonymous, for example 120 per minute) and a stricter write policy. Configure `ForwardedHeaders` with explicit `KnownProxies`. Rate limit or cache the readiness check.

### F-04 Medium: Origin-only CSRF is adequate for M1 but not sufficient to sign off SEC-031
Evidence: `HostServices.cs:18-34` and `Program.cs:79`. Verified by READ, and the existing tests exercise it (RAN).

Verdict on sufficiency:
- For M1 REST it is sufficient. SEC-031 explicitly allows "SameSite plus custom header/Origin check". Here:
  - Unsafe methods require an exact-match allow-listed Origin and fail closed on a missing or `null` Origin.
  - The allow-list is empty by default (`appsettings.json`), so production fails closed.
  - Cookies are SameSite=Lax.
  - JSON-only binding returns 415 for form posts (tested), so form-based CSRF is blocked.
  - The check also covers anonymous endpoints, which blocks login CSRF.
  - Browsers always send Origin on cross-origin and same-origin POST/PUT/DELETE.
- Gaps that stop me calling it complete:
  1. GET and OPTIONS are skipped (`HostServices.cs:22-24`). WebSocket and SignalR upgrades are GETs that carry the cookie. Cross-site WebSocket hijacking is not covered. SEC-032 and the M2 audio ingest and hub must check Origin themselves. This is a hard M2 gate.
  2. Same-site sibling subdomains are an issue: SameSite does not separate them, and they would send the cookie, but they are blocked here by the exact Origin match. This holds only if the allow-list never contains wildcard or sibling origins.
  3. There is no Referer, `Sec-Fetch-Site` or custom-header second layer. Cheap hardening: also require `Sec-Fetch-Site` of `same-origin` or `none` when the header is present, and a custom header such as `X-Requested-With: cc` on unsafe requests. A custom header forces a CORS preflight, which is denied because no CORS policy exists.
  4. Non-browser clients without Origin are rejected. This is acceptable but should be documented.
- The foundation doc specifies an antiforgery token (`backend-foundation.md:50`). The implementation deviates by using Origin only. Record this as an ADR.

### F-05 Medium: ETag leaks Postgres `xmin`
- Evidence: `CoursesDbContext.cs:34` (`IsRowVersion` maps to `xmin`) and `CourseEndpoints.cs:35` (`ETag => "{Version}"`). The value is the global transaction id. Any user can infer database-wide write rate, and can correlate their own write timing with other tenants' activity. Verified by READ.
- Fix: use a per-row `int version` column incremented in the app, or return an HMAC/opaque hash of `(id, version)`. Do this before there are more entities.
- 404 and If-Match uniformity is otherwise good. For a foreign id, `Update` returns 404 before it evaluates If-Match or the body (`CourseEndpoints.cs:94-103`), and the test at `CourseTests.cs:243-268` covers that. Foreign and unknown ids use the same single filtered query, so there is no structural timing difference. I did not measure timing statistically.

### F-06 Medium: security headers are incomplete and dropped on 500s (SEC-033)
- Evidence: `Program.cs:68-76`. These are missing: Permissions-Policy (microphone for self), HSTS and HTTPS redirection (none configured), and `Server` banner suppression. The headers middleware sits after `UseExceptionHandler` (`Program.cs:42`). The exception handler clears response headers when it re-executes, so error 500 responses probably lose nosniff, CSP and `no-store`. READ only, probably.
- Also, `AllowedHosts: "*"` (`appsettings.json:9`) disables Host header filtering.
- The API's `default-src 'none'` is fine for JSON. The web app (Next.js) needs its own CSP and Permissions-Policy, and I have not reviewed it.
- Fix: register the header middleware before `UseExceptionHandler`. Add `UseHsts` for non-development, a Permissions-Policy header, set `AddServerHeader = false`, and require explicit `AllowedHosts` in production. Add a header test on a 500 response.

### F-07 Medium: cookie hardening and data-protection keys
- Evidence: `IdentityModule.cs:48-53`. The cookie is HttpOnly, SameSite=Lax and Secure=Always outside Development (verified by READ and by tests, RAN). Gaps:
  - No `__Host-` prefix. Add it in non-dev; the cookie is already Path=/ and Secure.
  - No `AddDataProtection().PersistKeysTo...` configuration. In a container, keys are ephemeral, so every restart invalidates all sessions, and multiple instances cannot read each other's cookies. Keys also must be protected at rest. This is a devops dependency for the SEC-019 and token-encryption story.
- Dev cookie is `SameAsRequest`, so over plain http it is sent without Secure. Acceptable for localhost only.

### F-08 Low/Medium: error and log leakage (SEC-027, SEC-029)
- SEC-029 is OK: 500 responses are generic with only a traceId (`Program.cs:57-62`; READ).
- Evidence for the concern: `Program.cs:59-60` calls `LogError(ex, ...)`. Npgsql `PostgresException` messages and details can contain row values (for example "Key (...)=(...) already exists"). That is harmless for courses today, but once transcripts exist it can put student text in logs. There is no redaction filter and no canary test for SEC-027.
- Fix: log `ex.GetType().Name` plus a sanitized `SqlState` for database exceptions. Add the SEC-027 canary test before transcripts exist (M2 gate).
- `Microsoft.EntityFrameworkCore.Database.Command` is at Warning, so SQL is not logged. Confirm that `EnableSensitiveDataLogging` stays off (grep: not enabled).
- No audit log exists for auth events (SEC-028): logins, logouts and ownership violations are not recorded. Gap.

### F-09 Low: dev login gating (verified OK, with residual notes)
- Evidence: `IdentityModule.cs:28-31` throws at startup if `Auth:DevLogin:Enabled` is true outside Development. The endpoint is only mapped when enabled (`IdentityEndpoints.cs:36-39`). The seed is double-guarded (`IdentityModule.cs:91`). `appsettings.json` defaults it to false. Tests: `DevLoginStartupGuardTests` for Production and Staging, plus the production route-absence test (RAN, pass).
- Residual risks:
  1. The guard depends on `ASPNETCORE_ENVIRONMENT`. ASP.NET defaults to Production when unset, which is safe. Danger is anyone deploying with `docker-compose.dev.yml`, whose `api` service sets `Development`. It binds to 127.0.0.1 only (`docker-compose.dev.yml:23-24`), which is good. Keep that, and add a CI check that the dev override is never used for deployment.
  2. In Development, `/dev/login` creates a user for any email with no password (`IdentityEndpoints.cs:57-67`). Dev DB rows could collide with real emails if a dev database is ever promoted. Never reuse a dev database.
  3. The compose default Postgres password `cc_dev_only_password` is documented as dev only. SEC-019 CI secret scan was not run by me.

### F-10 Info: 401 vs 404 on unknown paths (verified OK)
- Anonymous requests to an unknown path get 401, and signed-in requests get a ProblemDetails 404. The fallback policy covers unmatched endpoints, so route enumeration by anonymous callers is blocked. The test at `AuthBoundaryTests.cs:64-70` passes (RAN). In production, `/api/v1/dev/login` is 401 and indistinguishable from any other unknown path.
- Residual: a signed-in user can distinguish a real route from an unknown one. That is acceptable. Anonymous method-mismatch cases (for example GET on a POST-only route) were not tested; I expect 401 but did not verify.

### F-11 Info: IDOR and ownership (verified OK)
- Evidence: the global query filter is at `CoursesDbContext.cs:38`. Anonymous maps to `Guid.Empty`, which matches no rows. The interceptor (`OwnershipSaveChangesInterceptor.cs:25-45`) stamps owner on insert, rejects foreign or reassigned owners on update and delete, and rejects anonymous writes. The owner is never taken from client input (`CourseEndpoints.cs:44-49`); `Client_supplied_owner_or_id_is_ignored` passes. Ids are UUIDv7 (`Ids.New()`). `grep` for `IgnoreQueryFilters`, `ExecuteUpdate`, `ExecuteDelete`, `FromSql` in `src/api/src` returned nothing. Cursor pagination cannot cross users because the filter applies, and the test passes. A test asserts that every user-owned entity has a query filter (`PersistenceTests.cs:103`).
- Limits:
  1. The interceptor does not cover `ExecuteUpdate`, `ExecuteDelete` or raw SQL, and these would bypass the filter. Add an architecture test banning those APIs on owned sets.
  2. SEC-002 asks for nested ids (a session in another course) and hub group joins. These do not exist yet and are not assessable. The composite FK `(course_id, user_id)` groundwork exists (`CoursesDbContext.cs:36`).
  3. `CourseOwnership` (`CoursesModule.cs:41-48`) is correct, but nothing consumes it yet.

### F-12 Info: not-yet-implemented requirements
- SEC-030: there is no CORS policy at all. That is safe by default (the browser blocks cross-origin reads) as long as the web app is served same-site through a proxy. If the dev setup needs CORS (web on :3000, API on :5080), add an explicit allow-list with credentials, never a wildcard.
- SEC-032 (hubs), SEC-034 beyond course DTO validation (64 KB body cap exists, `Program.cs:15`; validation of course fields tested), SEC-035 (no outbound fetches exist) and SEC-041 (a CI gate) were not assessable or not reviewed.
- Google OAuth (`UnconfiguredGoogleIdentityProvider`) was not reviewed: state/PKCE/nonce, scope and token storage.

## What I did not check
- Statistical timing differences between 404 variants.
- Whether the refreshed cookie actually resets `IssuedUtc` (F-02 is from knowledge of the framework, not a run).
- The 500-response header-drop behavior (F-06) by running.
- The web app, CI workflow, secret scan and git history for secrets (SEC-019), dependency vulnerability scanning, and the `.slnx` package versions.
- Any load or fuzz testing, and the Docker or Postgres configuration beyond the compose files above.

## Recommended order
1. F-01 and F-02 together, as one sessions-table change with tests. Gate: before any real login.
2. F-03 global rate limiting and forwarded headers.
3. F-06, F-07 and F-05 hardening, small changes.
4. F-04 second CSRF layer, plus an Origin check on hub and WebSocket upgrades as an M2 gate.
5. F-08 canary test and audit log events before transcripts are stored.

## Appendix: remediation status (backend-engineer, after commit 66a09e3)

Each fix was written test-first: tests were added, run red (21 failed / 69 passed of 90), then the code changed and the suite run green (7/7 architecture, 90/90 integration, then 8/8 after one guard test was added). Tests are in `src/api/tests/CollegeCompanion.IntegrationTests/SecurityHardeningTests.cs` and `RaceTests.cs`. Not independently re-reviewed by security-engineer yet.

| Finding | Status | Evidence / notes |
|---|---|---|
| F-01 session revocation (ADR-016) | Fixed | `identity.sessions` (migration `M003_Sessions`), `cc:sid` claim, PK lookup per request in `OnValidatePrincipal`; rejects missing, revoked, expired sessions and soft-deleted users; logout revokes only that session. Tests `ServerSideSessionTests`. |
| F-02 absolute lifetime | Fixed | `expires_at` is set once at sign-in; test with a controllable `TimeProvider` refreshes the cookie every 5h and proves the session dies at 14 days (`AbsoluteSessionLifetimeTests`). The cookie handler now uses the injected `TimeProvider`. |
| F-03 rate limiting / forwarded headers | Fixed, with residuals | Global per-user limiter, stricter per-user write bucket, per-IP anonymous limiter (covers `/health/ready`), configurable; `ForwardedHeaders` only from `Security:TrustedProxies`/`TrustedProxyNetworks`, ForwardLimit 1, otherwise middleware absent. Residuals: authentication now runs before the limiter, so a flood of bogus cookies costs one DB read each (upstream/proxy limits still advised); SEC-005 lockout/back-off still not built (needs Google auth). |
| F-04 Origin/CSRF | Deferred | Not in this task. ADR-017 remains an M2 gate (WebSocket/SignalR Origin check). |
| F-05 ETag leaks xmin | Fixed | Weak HMAC-SHA256 tag over (id, xmin), truncated; If-Match semantics kept (428, 412, `*`). Needs `Security:ETagKey` for multi-instance; otherwise random per process (warning logged). |
| F-06 headers | Fixed, with residuals | Permissions-Policy added; all headers set in `OnStarting` so they appear on 500/429; HSTS over https outside Development; Kestrel `Server` banner off (not testable under TestServer). Residual: `AllowedHosts: "*"` unchanged, require explicit hosts in production config (devops). No HTTPS redirect added (TLS terminates at the proxy). |
| F-07 cookie / data-protection keys | Fixed, with residuals | `__Host-cc.session` outside Development (Development keeps `cc.session`: `__Host-` needs `Secure`, and dev runs over http). `DataProtection:KeysPath` persists keys (test checks key file is written); warning when unset in non-Development. Residual: keys are not encrypted at rest and compose has no volume yet (devops: mount a volume or use a certificate/KMS). |
| F-08 log leakage / audit log | Deferred | M2 gate (ADR-017). Not touched. |
| F-09 dev login gating | No change | Verified OK in review. |
| Expired session cleanup | Deferred | Expired or revoked `identity.sessions` rows are never deleted. Add a retention job later. |
