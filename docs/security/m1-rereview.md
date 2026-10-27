# Milestone 1 Security Re-review (fix pass 043de13)

Reviewer: security-engineer, acting as an independent reviewer. HEAD is e1c1727, and the fixes were made in 043de13 by backend-engineer. The appendix in `m1-review.md` was not taken on trust. No file under `src/` or `tests/` was modified.

Method legend: **READ** = verified by reading code. **RAN** = verified by something I executed. Paths are relative to `src/api/src/`.

## What I ran
- The full test suite: `docker compose -f docker-compose.yml -f docker-compose.dev.yml run --rm dotnet dotnet test src/api/Tether.slnx`. Result: 8/8 architecture tests and 90/90 integration tests passed.
- Live curl probes against the dev API at `http://localhost:5080` (Development configuration).
- Side effect of the probes: they created dev-DB rows. These are sessions for alice and bob, plus a user `mallory@evil.example` created through `/dev/login`. No courses were created.

## Release position
- I am not marking any release gate as passed.
- No Critical findings.
- F-01 and F-02 are really fixed, so the two High blockers from the first review are closed.
- Non-dev use is still blocked. The items listed under "Before any non-dev use" below must land first. The main ones are N-01 (proxy and forwarded-header configuration) and the key-protection residuals.

## Verdict per claimed fix

| Finding | Verdict | Evidence |
|---|---|---|
| F-01 session revocation | **Fixed** (new Low N-03) | See F-01 detail below. |
| F-02 absolute lifetime | **Fixed** | See F-02 detail below. |
| F-03 rate limiting / proxies | **Partially fixed** | The per-user, per-write and per-IP-anonymous limiters work, and so does the trusted-proxy happy path (RAN, `SecurityHardeningTests.cs:141-222`). Residuals are N-01, N-04 and N-05. SEC-005 lockout is still not built. |
| F-05 ETag leaks xmin | **Fixed** (Info N-07) | `Application/ETagService.cs:15-31` builds an HMAC-SHA256 over `id\|xmin`, truncated to 128 bits and compared with `FixedTimeEquals`. The length-mismatch early exit leaks nothing useful. Update re-reads the row, checks If-Match, then updates conditionally on xmin (`CourseEndpoints.cs:94-117`). After a restart with a random key, tags simply stop matching and clients get 412; this fails safe. READ + RAN (`EtagTests`). |
| F-06 headers | **Partially fixed** | Headers are set in `OnStarting` and appear on 500 (RAN, `SecurityHeadersOn500Tests`) and on 404, 401 and 429 (RAN live and in the suite). HSTS is emitted only when `Request.IsHttps` (`Program.cs:109`), so behind a TLS-terminating proxy without TrustedProxies it is never sent (N-02, READ). `AllowedHosts: "*"` is unchanged (`appsettings.json:9`). `AddServerHeader=false` (`Program.cs:21`) was READ only. |
| F-07 cookie / DP keys | **Partially fixed** | See F-07 detail below. |
| Data: FK/cascade, races | **Fixed** | See data detail below. |

### F-01 detail (RAN live, READ)
- Probe: I logged in, copied the cookie, then logged out. The copied cookie then got 401 on `/me`, `/courses` and an unmatched route. On `/health/ready` it was treated as anonymous and the server sent a delete-cookie. A second session for the same user stayed valid.
- A cookie altered in the middle of its payload got 401. Altering only the last character still got 200, but that is base64 padding bits decoding to identical bytes, not a weakness.
- `SessionPolicy.cs:28-36` checks, on every authenticate:
  - the session id (`sid`),
  - that `sid` and `uid` belong together,
  - `revoked_at`,
  - `expires_at`,
  - `users.deleted_at`.
- The `sid` is tamper-proof because it sits inside the data-protection-encrypted ticket. Even with leaked DP keys, a forger also needs a live `sid` (UUIDv7 with 74 random bits) that matches the user.
- Session fixation: each login creates a new row and id (`IdentityEndpoints.cs:81-91`).
- Logout racing an in-flight request: a request that already passed validation completes. A sliding refresh issued after logout points at a revoked row, so it dies on the next request. This one-request window is acceptable (READ, not run).

### F-02 detail (RAN suite, READ)
- `expires_at` is written only in `SessionPolicy.Create` (`SessionPolicy.cs:18`). A grep finds no other writer.
- The cookie handler and the session check share the injected `TimeProvider` (`IdentityModule.cs:55,77`).
- `AbsoluteSessionLifetimeTests` refreshes the cookie every 5 hours and dies within [14d, 14d+5h].
- Residual: `expires_at` uses the app clock, so multi-instance clock skew applies (Info).

### F-07 detail (READ only)
- In non-dev, the cookie is `__Host-cc.session`, with `SecurePolicy=Always`, `Path=/` and no Domain (`IdentityModule.cs:61-66`). That is satisfiable over HTTPS.
- In Production I verified only the options (`SecurityHardeningTests.cs:336-347`). I did not observe a real Production `Set-Cookie`, because no Production login path exists yet.
- DataProtection keys:
  - They are persisted only if `DataProtection:KeysPath` is set (`IdentityModule.cs:51-52`).
  - They are not encrypted at rest.
  - No compose volume exists for them.
  - Production only logs a warning if the path is missing (`Program.cs:89-90`).

### Data detail (RAN suite, READ)
- `M004_CoursesUserFk.cs` adds the FK with cascade. The `Session` FK also cascades (`IdentityDbContext.cs:38`).
- Purge and archive load the row through the user query filter before `Remove` or the update (`CourseEndpoints.cs:126-147`). The interceptor rejects foreign originals (`OwnershipSaveChangesInterceptor.cs:39-43`). The race path re-reads through the filter (`:151-156`).
- I found no path to another user's course.
- Logout's `ExecuteUpdate` (`IdentityEndpoints.cs:36-37`) bypasses the interceptor. It is keyed by the `sid` from the authenticated, encrypted cookie, so it is safe. However, it is the first bulk-update API in the codebase, and it is not guarded by any test (N-09).

## CSRF and other probes (RAN live, Development)
- Unsafe requests to `/courses` were rejected with 403 for each of these:
  - Origin `null`
  - Origin `:3001`
  - Origin `https://localhost:3000`
  - Origin `http://localhost:3000.evil.com`
  - a Referer header only, with no Origin
- `X-HTTP-Method-Override: DELETE` on a GET had no effect: it returned 404, and no override middleware exists.
- GET `/auth/logout` returned 405.
- A form-encoded POST and a `text/plain` POST both returned 415 even with a good Origin.
- Origin `HTTP://LOCALHOST:3000` and `http://localhost:3000/` are accepted (`HostServices.cs:10`). This is harmless because browsers serialize Origin in lowercase without a slash (Info N-11).
- Anonymous GET on POST-only `/dev/login` returns 401, which closes the F-10 open item.
- The F-04 WebSocket gap is unchanged and remains an M2 gate (ADR-017).

## New findings

| ID | Severity | Finding | Fix |
|---|---|---|---|
| N-01 | Medium | Forwarded-header config is not validated (`Program.cs:33-46`). With `TrustedProxyNetworks` such as `0.0.0.0/0` or `::/0`, any direct client can spoof X-Forwarded-For and get a fresh limiter bucket per request, which bypasses the login and anonymous limits. If the proxy is not configured but a proxy exists, all anonymous traffic, including login, shares one 60/min bucket, so one attacker can lock everyone out of login. READ; the code paths are covered by tests, but I did not run the broad-network case. | Fail startup on networks broader than a sane prefix or outside private ranges. In non-dev, require either explicit `TrustedProxies` or an explicit "directly exposed" flag. |
| N-02 | Medium | HSTS depends on `IsHttps` (`Program.cs:109`), which is false behind a TLS terminator unless N-01 is configured correctly. `AllowedHosts: "*"` is still in place. READ. | Emit HSTS when it is configured for the deployment, not per request. Require explicit `AllowedHosts` in Production (fail startup on `*`). |
| N-03 | Low | Logging in again, even as a different user, does not revoke the session the browser already held. RAN: the old alice cookie was still 200 after the same browser logged in as bob. | In `SignInAsync`, revoke the current `sid` if the request carries one. |
| N-04 | Low | The anonymous and auth limiters key on the full IP (`Program.cs:68,75`), so one IPv6 /64 gives unlimited buckets. READ. | Key IPv6 on /64. |
| N-05 | Low | Authentication runs before the limiter (`Program.cs:147-148`). Every request carrying a genuine but revoked or expired cookie costs one DB read before 429. Origin-rejected requests are not limited at all (they are cheap). READ. | Accept for M1. Add proxy/WAF limits, or cache negative session lookups briefly. |
| N-06 | Low | Expired and revoked session rows are never deleted. This is already deferred, and it causes table growth with no direct exposure. | Add a retention job before production. |
| N-07 | Info | A weak ETag is accepted in If-Match, but RFC 9110 requires strong comparison there. `*` skips the concurrency check. | Consider strong tags. Document the `*` behavior. |
| N-08 | Info | `Security:ETagKey` and DataProtection keys come from plain config or filesystem. Production only warns when they are missing. | In Production, fail closed when `DataProtection:KeysPath` (or a KMS) is missing. Source both from the secret store. |
| N-09 | Info | No architecture test bans `ExecuteUpdate`/`ExecuteDelete` on `IUserOwned` sets (F-11 limit 1 is still open). `ModuleBoundaryTests.cs:100-113` covers only `IgnoreQueryFilters` and the raw-SQL methods. | Add the ban, with an allow-list for `identity.sessions`. |
| N-10 | Info | The logout vs in-flight request window is one request. | Document it. No change. |
| N-11 | Info | Origin matching ignores case and a trailing slash. | Optionally use exact ordinal matching. |

## Before any non-dev use (real Google login or a reachable deployment)
1. N-01 and N-02. A deployment-specific proxy, TLS and host configuration with startup validation.
2. F-07 residuals and N-08:
   - persistent DataProtection keys on a protected volume or a KMS, encrypted at rest;
   - fail closed in Production when the keys are missing;
   - verify the real `__Host-` `Set-Cookie` over HTTPS.
3. N-03 (small change).
4. SEC-005 lockout and back-off, plus SEC-028 audit events for login, logout and revocation, delivered together with Google OAuth. A security review of the OAuth flow covering state, PKCE, scopes and token storage.
5. N-06 session retention job.

## Before Milestone 2 (transcripts, hubs)
- The ADR-017 gates: an Origin check on WebSocket/SignalR upgrades (F-04), and the F-08 log canary plus removal of `LogError(ex)` that can carry row values (`Program.cs:133-134`, unchanged).
- N-09, N-04, and the F-04 second CSRF layer.

## What I did not check
- Real reverse-proxy or TLS deployment, a Production `Set-Cookie` on the wire, and the Kestrel `Server` header (TestServer cannot show it).
- Broad `TrustedProxyNetworks` or IPv6 behavior by running. Partition-limiter memory under load. Any load, fuzz or timing tests.
- The logout vs in-flight race, by running.
- Multi-instance behavior: key sharing, ETag key, clock skew.
- The Google OAuth flow. The Next.js app's CSP and Permissions-Policy.
- Secret scanning, dependency or vulnerability scanning, CI workflow, DB roles and RLS.
- The Playwright suite (not run).
