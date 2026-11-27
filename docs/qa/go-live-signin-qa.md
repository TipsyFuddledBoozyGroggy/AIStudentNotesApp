# QA report: invite-only Google sign-in (ADR-022, stories S1 and S4)

Date: 2026-11-27. Tester: qa-engineer agent (independent of the two builders). Subject: the uncommitted working tree at the time of the run.
Out of scope on instruction: the workflow's `access=closed` path and its preflight exemption, and `.github/workflows/ci.yml` (both were being changed
by another engineer during this run; `infra/scripts/tests/preflight-cases.sh` moved under me, see D8).
Real Google was not called, no `az` or `gh` was run, nothing was sent to Azure. No gate changes; this report does not clear G1 to G5.

## 1. Summary

| Area | Result | Evidence |
|---|---|---|
| dotnet build and all 7 API test projects | PASS, 2958 / 2958 | section 4.1 |
| Web `tsc --noEmit` | PASS (exit 0) | section 4.2 |
| Web `eslint .` | PASS, 0 errors, 1 pre-existing warning (`e2e/lecture.spec.ts:65`) | section 4.2 |
| Web `vitest run` | PASS, 69 files, 3480 / 3480 | section 4.2 |
| Playwright `e2e/signin.spec.ts` | PASS, 7 / 7, run against the local Production stack (the dev stack was not running) | section 4.3 |
| `infra/scripts/tests/smoke-cases.sh` (offline container) | PASS, 72 / 72 | section 4.4 |
| `infra/scripts/tests/preflight-cases.sh` | Last two runs 66 / 66. First run 58 passed, 1 failed (file was being edited). Not stable evidence | D8 |
| Local Production stack + `smoke.sh` rehearsal | PASS, all six probes | section 5.1 |
| Manual hostile probes (dev login, cookie, callback, replay, Host spoof, logs, DB) | PASS | section 5.2 to 5.6 |
| Startup refusals (16 cases) | PASS, each names only the key, no value echoed | section 5.7 |
| PO scripts read as a non-specialist | Mostly clear; no secret or address printed; 2 low usability findings | section 6 |
| S1 items 1 to 16 | 14 VERIFIED (some only at FakeGoogle level), 1 FAILED in part (item 16, D1), human/real-Google parts NOT VERIFIED | section 7 |
| S4 local parts | Local checks VERIFIED; everything that needs Azure NOT VERIFIED | section 8 |

Defects: 0 High, 1 Medium, 4 Low, 3 Info.

## 2. Defects

### D1 (Medium) One anonymous client can lock every invitee out of sign-in start (S1 item 16)
- Repro (local Production stack): send 700 concurrent `POST /api/v1/auth/google/signin/start` with an allowed `Origin`, JSON body `{"landing":"home"}`, no cookie.
  `seq 1 700 | xargs -P 30 -I{} curl ... /api/v1/auth/google/signin/start` gave `300 x 200` and `400 x 429`. A normal visitor sent immediately after got
  `429 rate_limited`. The limit is global (300 a minute) and a request with no binder cookie is not charged to any per-browser bucket, so the attacker
  needs no cookie and only about 5 requests a second to keep everyone out. The same flood leaves live states behind (680 unconsumed after the run; the
  cap is 3000, also answered 429), so a sustained flood fills the cap too.
- Why it matters: the site is public (anyone who finds the address); the invite list does not protect the start route. No data exposure, only denial of
  sign-in. The unbound-callback flood is handled correctly (see 5.4); the start route has no equivalent separation.
- Where: `src/api/src/Modules/Identity/Tether.Identity/Google/SignInLimiter.cs` (`_start = Window(Read(config, StartKey, 300))`, `TryStart`),
  `Google/GoogleSignInEndpoints.cs:75` and `:92` (live-state cap).
- Note: ADR-022 R6 chose global buckets because Azure shows one client address. That trade-off is real; this finding records its cost. Options are for the
  architect (for example a lower start budget for requests without a binder, or a proof-of-work-free cheap guard); not a QA decision.
- Test gap: `GoogleSignInRateLimitTests` proves the per-binder and unbound-callback buckets and the live-state cap, but not that a start flood leaves
  headroom for a real person.

### D2 (Low) Contract says `405` for non-GET on the callback; the deployed-style stack answers `401`
- Repro: `curl --cacert <cert> -X POST -H "Origin: https://localhost:3443" https://localhost:3443/api/v1/auth/google/callback` returns `401` (anonymous,
  fallback policy), not `405` as `docs/API_CONTRACTS_AUTH.md` section 2 states. `Callback_answers_only_get` passes in the suite, so the test host and the
  Production stack differ here or the test asserts something looser. Harmless (still refused), but the contract and the behaviour disagree.

### D3 (Low) Copy of the web production bundle includes the mock-service handlers
- `docker exec tether-poc-prod-web-1 grep -rhoE ".{60}dev/login.{40}" /app/.next/static` shows a mock handler (`google:!1,devLogin:!0` and a `/dev/login`
  POST) inside a shipped static chunk, next to the real client method. It is dormant (the page showed only the Google button and `/dev/login` is
  absent on the API), but mock fixtures in the production client bundle are dead weight and a future misconfiguration risk. Pre-existing pattern, not
  introduced by this change; `src/web/src/mocks/handlers.ts` was edited by it.

### D4 (Low) `set-google-signin.ps1` and `set-invites.ps1` hide `gh` errors completely
- `Set-GitHubSecret` reads and discards stderr, so a failed `gh secret set` says only "failed (exit code N)". A non-specialist cannot tell "not logged in"
  from "environment missing" from "no permission". `scripts/azure/set-google-signin.ps1:105-107` and `scripts/azure/set-invites.ps1:116-119`.
  (Deliberate to avoid leaking anything, but the exit code alone is not actionable. Printing a fixed hint such as "run gh auth status" would be safe.)

### D5 (Low) The first-time instruction needs a value the PO may not have at hand
- `az login --tenant <your tenant id>` (header of `set-google-signin.ps1`, line 38) does not say where the tenant id comes from. Also `-ShowRedirectAddressOnly`
  needs the az and gh sign-ins first, which the usage block only implies.

### D6 (Low) Invite addresses appear on the az command line while the script runs
- `set-invites.ps1:242`: `az containerapp secret set --secrets "invite-emails=<comma list>"`. The list is visible to other processes on the PO's own machine for the
  duration of the call (not in any file, log or script output; az masks secret values in its own command log). Accepted practice for `az`; recorded
  because the question was asked. Not run, so the cmd and `.cmd` batch quoting of `=` and `,` through `az.cmd` is NOT VERIFIED.

### D7 (Info) Noise and unverified behaviour
- Every API start logs `Cannot load library libgssapi_krb5.so.2` (chiseled image, Npgsql Kerberos probe). Harmless.
- Starting the API against a database whose key ring was written with another certificate logs `XmlKeyManager ... Unable to retrieve the decryption key`
  with the key id (not key material). I did not establish whether it starts and mints a new key (silently signing everyone out) or refuses. The tests
  cover rotation with a previous certificate (`KeyRingTests`); the wrong-certificate-and-no-previous case is NOT VERIFIED here. The accidental run also
  wrote a second key into the throwaway database.
- `/auth/methods`, `/signin` etc. behave as designed under spoofed `Host`/`X-Forwarded-*` (5.5).

### D8 (Info) `preflight-cases.sh` moved while I ran it
- First run 58 passed, 1 failed (the failing case name was cut off by my `tail`); later runs 66 / 66. Another engineer was editing `infra/scripts/preflight.sh`
  and tests at that time (`close-web.sh` appeared in the tree). Treat 66 / 66 as the state at the end of the run, not a stable result.

### Test gaps (no defect)
- S1 item 14 (no password field, no sign-up page): no negative assertion anywhere; the e2e "page offers exactly what the API says" checks the Email field only.
- S1 item 8 "the back button does not show my data": no test (browser behaviour).
- S1 item 1 "land on my empty dashboard": covered only against mocked web tests plus API-level FakeGoogle; no browser test of the post-callback landing.

## 3. What the FakeGoogle tests really assert (S1 mapping)

Read `GoogleSignInTests.cs`, `KeyRingTests.cs`, `ProductionStartupTests.cs`, `LogPipelineCanaryTests.cs` (new class `GoogleSignInLogPipelineCanaryTests`),
`FakeGoogle.cs`. All pass (803 integration tests). They assert real database state and real HTTP responses, not only the mock:

| S1 item | Test that asserts it | Real assertion |
|---|---|---|
| 1 | `First_sign_in_creates_one_user_and_a_new_session_and_clears_the_binder` | one `identity.users` row keyed `google:<sub>`, email normalised, name sanitised, one session, binder cookie expired, `/me` returns 200 |
| 2, 3 | `Returning_sign_in_reuses_the_user_by_subject...`, `The_same_email_under_two_subjects_is_two_users...` | same user id, email refreshed, old session revoked (fixation), same email under two subjects gives two users, dev row untouched |
| 4, 7, 12 | `Not_invited_and_unverified_emails_create_nothing`, `A_school_account_is_refused_by_default_but_only_after_the_invite_check`, `A_soft_deleted_user_is_refused_as_account_closed...` | row and session counts unchanged |
| 5, 6 | `Google_errors_are_mapped_after_the_state_check...`, `Every_id_token_rule_failure_is_provider_unavailable...`, `A_wrong_pkce_verifier...` | outcomes and "nothing created"; zero token calls on state failures |
| 6 (replay) | `A_second_callback_with_the_same_state...`, `Concurrent_callbacks_with_one_state...`, `Login_csrf...`, `An_expired_state...` | one user, one session, token endpoint not called |
| 10 | `Start_returns_the_pinned_url...never_from_request_headers`, `The_binder_cookie_is_host_prefixed_secure...`, `The_derived_nonce_and_verifier_use_separate_labels` | exact parameter set, S256, host-independent, cookie attributes |
| 6, 10 logs | `No_identity_token_code_or_state_value_reaches_any_log_category` | Trace logging, success plus three refusals, sentinels for email, subject, name, secret, code, state, nonce, binder, session, access token never in any record |
| 9 | `KeyRingTests` (restart, wrong certificate, rotation), `Production_startup_revokes_dev_and_uninvited_sessions...reads_cookies_minted_by_another_host` | cookie protected before a restart reads after it; key wrapped by certificate |
| 11, 13, 14 (start side) | `Auth_methods_says_google_on_and_dev_login_off_in_production`, `Production_refuses_to_start...`, scope tests (`extra scope`, `missing profile scope`) | devLogin false, refusal to start, exact scope |
| 16 | `GoogleSignInRateLimitTests` | per-binder, unbound callbacks, live-state cap; a real return is unaffected by a callback flood. Does NOT cover a start flood (D1) |

## 4. Suites: commands and real output

### 4.1 API (dotnet SDK 10 container, shared dev Postgres; dev API was already stopped)
```
docker run --rm --network aistudentnotesapp_default -e "ConnectionStrings__Default=Host=cc-postgres-dev;Port=5432;Database=college_companion_dev;Username=cc_dev;Password=cc_dev_only_password" -v "<repo>:/repo" -w /repo/src/api mcr.microsoft.com/dotnet/sdk:10.0 dotnet test Tether.slnx
```
Build succeeded with no errors. Results: DocsTests 74, AiTests 333, DocReaderTests 133, ContractTests 984, ArchitectureTests 38, SyllabusTests 593,
IntegrationTests 803. Every line "Passed! - Failed: 0, Skipped: 0". Total 2958 / 2958. Machine was otherwise idle (the load-sensitive test note in docs/DEVELOPMENT.md did not trigger).

### 4.2 Web (from `src/web`)
- `npx tsc --noEmit -p .` exit 0.
- `npx eslint .` : `1 problem (0 errors, 1 warning)`, `e2e/lecture.spec.ts 65:40 '_label' is defined but never used` (not part of this change).
- `npx vitest run` : `Test Files 69 passed (69)`, `Tests 3480 passed (3480)`.

### 4.3 Playwright `e2e/signin.spec.ts`
The dev stack was not running and its API has no Google settings (the spec's bogus-callback test skips without them). I ran the spec against the local Production
stack instead, through a throwaway config that wraps `playwright.poc.config.ts` with `baseURL=https://localhost:3443` and `ignoreHTTPSErrors` (file kept in the
scratchpad, not in the repo). Result: `7 passed (1.4s)`. This is the real Next proxy plus the real API in Production, not the dev server.

### 4.4 Infra case tests
- `docker build -t tether-smoke-rehearsal:local -f infra/local/rehearsal.Dockerfile infra/local` then
  `docker run --rm --network none -v "<repo>:/repo:ro" -w /repo tether-smoke-rehearsal:local infra/scripts/tests/smoke-cases.sh` :
  `smoke cases: 72 passed, 0 failed` (includes the three rehearsal-mode cases: six probes pass and nothing is written; a breach is reported and nothing is
  written; any gate other than google is refused).
- `bash infra/scripts/tests/preflight-cases.sh` : `66 passed, 0 failed` (see D8 for the earlier 58 / 1).

## 5. Local Production stack (docker-compose.poc-prod.yml) by hand

Setup: `bash infra/local/make-local-prod-env.sh <folder outside the repo>` (throwaway TLS cert, root secret, key-ring certificate),
`docker compose --env-file <env> -f docker-compose.poc-prod.yml up -d --build`. All four containers came up (postgres and web healthy). API log:
`Hosting environment: Production`, migrations applied to the empty `tether_live` database including `M010_GoogleSignIn`, `Google sign-in is on; the invite list has 1 entries.`,
`Startup revocation: 0 dev-login sessions and 0 sessions of people no longer invited.`

### 5.1 `docker compose ... run --rm rehearsal` (smoke.sh, rehearsal mode)
```
PASS  wait, Microsoft sign-in layer gone: no Microsoft sign-in layer in front
PASS  probe 1, dev login absent: HTTP 401 unauthenticated with no cookie: the dev-login route does not exist
PASS  probe 2, sign-in methods: google true, devLogin false
PASS  probe 3, anonymous refused: HTTP 401 for both
PASS  probe 4, Google sign-in start: Google authorize URL with this site's callback, code, S256, sign-in scopes; binder cookie attributes correct
PASS  probe 5, callback with unknown state: HTTP 302 to /signin?purpose=signin&outcome=state_invalid; binder expired
PASS  probe 6, sign-in page: HTTP 200 with Strict-Transport-Security
Rehearsal: all six invited probes passed. Nothing was written to the workflow outputs.
```
Exit 0.

### 5.2 Dev login unreachable, methods
- `GET /api/v1/auth/methods` : `{"google":true,"devLogin":false}`.
- `POST /api/v1/dev/login` with a valid Origin and body: 401 `unauthenticated`, byte-for-byte the same answer as for a made-up path (`/api/v1/nonexistent/route`). Without
  an Origin: 403 `csrf_failed`. The route is not mapped (S1 item 11 says "404 or absent"; it is absent, the anonymous fallback answers 401, as the contract states).
- Starting the Production image with `Auth__DevLogin__Enabled=true`: refuses with `Auth:DevLogin:Enabled is true but the environment is not Development. Refusing to start.`

### 5.3 Binder cookie (from `POST .../signin/start`)
`set-cookie: __Host-cc.signin=<43 chars>; max-age=600; path=/; secure; samesite=lax; httponly` : `__Host-` prefix, no Domain, Secure, HttpOnly, SameSite=Lax, 10 minutes.
Expiry on callback: `__Host-cc.signin=; expires=Thu, 01 Jan 1970 ...; path=/; secure; samesite=lax; httponly`. Response headers carry `cache-control: no-store`,
`referrer-policy: no-referrer`, HSTS, restrictive CSP.

### 5.4 Callback behaviour (all `302`, relative `Location`, binder expired)
| Request | Location |
|---|---|
| unknown state `bogus` | `/signin?purpose=signin&outcome=state_invalid` |
| valid state, no binder cookie, `error=access_denied` | `state_invalid` (and the state is burned) |
| same state replayed afterwards, with binder | `state_invalid` |
| fresh state + binder, `error=access_denied` | `access_denied` |
| the same state replayed | `state_invalid` |
| fresh state + binder, `error=server_error&error_description=<marker>` | `provider_unavailable` (marker never echoed) |
| 80 unbound callbacks in a burst | 46 `state_invalid`, 34 `rate_limited`; a start still returned 200 |
| after 120 unbound callbacks exhaust their bucket, a real return with its binder | `access_denied` (not starved) |

`POST /callback` : 401 (D2). Start with no Origin 403; extra JSON member 400; `text/plain` 415.

### 5.5 Host and forwarded-header spoofing
`redirect_uri` stayed exactly `https%3A%2F%2Flocalhost%3A3443%2Fapi%2Fv1%2Fauth%2Fgoogle%2Fcallback` under `Host: evil.example.invalid`, `X-Forwarded-Host`,
`X-Forwarded-Proto: http`, `Forwarded: host=...;proto=http`, `X-Original-Host`, through the TLS front, and `X-Forwarded-Host` / `-Proto` sent straight to the API
inside the network. A bare `Host: evil.example.invalid` straight at the API gave 400 (`AllowedHosts`).

### 5.6 Logs, database, bundle
- API log (476 lines, all containers' logs also scanned): the only sign-in lines are `Google sign-in finished: <outcome>`. No `@`, no `state=`, no `code=`, no
  `error_description` marker, none of the actual state, nonce, challenge or binder values from my four starts, no client secret placeholder, no database password,
  no root-secret prefix, no certificate prefix. web, tls and postgres logs also clean.
- Database after all refused and replayed flows: `identity.users` 0 rows, `identity.sessions` 0 rows, `identity.oauth_states` only hashes (`state_hash`,
  `binder_hash` bytea). `identity.data_protection_keys`: 1 key, XML contains `EncryptedData` and no plaintext key value (certificate-wrapped).
- API restart (`docker restart`): key count stayed 1, `/auth/methods` still 200.
- Web bundle (`/app` in the web container): zero hits for the placeholder client secret, placeholder client id, or the rehearsal invite address. D3 for the mock handlers.

### 5.7 Startup refusals (Production image, one value changed at a time, message and leak check)
Every case exited with `Unhandled exception. System.InvalidOperationException` naming the key only. No value I supplied (sentinel secret, malformed id,
malformed invite, root-secret and certificate prefixes) appeared in any output.
- no / malformed client id; no client secret
- no, malformed (entry number only), duplicate invite list
- no certificate; not base64; base64 but not a PFX
- no root secret; too-short root secret (`must decode to at least 32 bytes (got 11)`)
- http redirect; redirect origin not in `AllowedOrigins`; no redirect
- admission `open`; dev login enabled in Production
The baseline with all valid values started and listened on :8080.

## 6. PO scripts read as a non-specialist who works in cmd

Both scripts parse with 0 errors in Windows PowerShell 5.1 (`[Parser]::ParseFile`). I did not run any `az` or `gh` step. I did run the local-only paths of
`set-invites.ps1` against a throwaway folder outside the repo: `-List` (prints a count and the file path only), a bad address (error says "Entry 1", not the address),
a list folder inside the repo (refused, including a relative `.tether` when started from the repo folder), nothing to do, removing the last address (refused with the
reason and the `access = closed` hint), and the same address in `-Add` and `-Remove` (refused). All print "Nothing was changed."

- Steps clear? Yes for the main flow: one cmd line (`set "AZURE_CONFIG_DIR=%USERPROFILE%\.azure-po" && powershell -ExecutionPolicy Bypass -File ...`), numbered
  STEP 1 to 4 with exact console menu names, a "Press Enter when done" pause, a `YES` confirmation before anything is stored, "Cancelled. Nothing changed." on anything else,
  and every failure message ends with whether anything was changed. Weak spots: D4, D5.
- Could either print a secret or address? `set-google-signin.ps1`: no. It prints the PO's own Azure account name, the public redirect address, and secret names. The
  secret and the generated certificate go to `gh` on standard input, never to the screen, a file or an argument. `set-invites.ps1`: prints counts and the list file path,
  never an address (errors use entry numbers). D6 for the az command line.
- Hidden prompt? Yes: `Read-Host -AsSecureString` for the client secret, wiped from memory after use. The Client ID prompt is visible, which is fine (an identifier).
- Anything inside the repository folder? `set-google-signin.ps1` writes no file at all. `set-invites.ps1` writes only `%USERPROFILE%\.tether\invited-emails.txt` and
  hard-refuses any folder inside the repo (tested). Nothing was written into the repo by me either (`git status` shows only the existing changes plus this report).
- NOT VERIFIED: actual `gh`/`az` behaviour, PS 7 behaviour, `az.cmd` quoting of the comma list, the hidden prompt under a real console paste.

## 7. S1 criteria walk

Legend: VERIFIED = observed or asserted by a test I ran. Parts needing real Google, Azure or a human are listed separately as NOT VERIFIED.

| # | Criterion | Verdict | Evidence / gap |
|---|---|---|---|
| 1 | First sign-in creates one account, lands signed in | VERIFIED at API level (FakeGoogle); NOT VERIFIED with real Google and in a browser | `First_sign_in_creates_one_user...`; `SignInView` vitest for signed_in refresh and return |
| 2 | Returning sign-in, no second account | VERIFIED (FakeGoogle) | `Returning_sign_in_reuses_the_user_by_subject...` |
| 3 | Key is Google subject, not email | VERIFIED | same-subject new email keeps the user; same email two subjects gives two users |
| 4 | Not invited: refused, nothing created, plain message, no list shown | VERIFIED (Tether side) | test counts; live stack: users 0 after refusals; copy `SignInView.tsx:27-30` matches the story. NOT VERIFIED: Google's own test-user refusal screen |
| 5 | Cancel at Google | VERIFIED | live `access_denied` redirect; copy "You cancelled. Nothing was changed..." |
| 6 | Google error, bad or replayed state, timeout: calm message, content-free log | VERIFIED | live `state_invalid`, `provider_unavailable`, replay; unit and canary tests incl. token failures; logs scanned (5.6). Timeout covered by the suite, not hand-run |
| 7 | Personal accounts only (`hd`) | VERIFIED against FakeGoogle (refused after invite check, creates nothing). NOT VERIFIED: that real Google sends `hd` with sign-in-only scopes (T-hd); absent-`hd` policy is still the open PO-20w point | `A_school_account_is_refused...` |
| 8 | Sign out ends the session on the server | VERIFIED by existing suite (revocation before sign-out, sockets close within 1 s). NOT VERIFIED: back-button shows no data (browser) | `AuthBoundaryTests`, `B2FixTests`, `SecurityHardeningTests` pass |
| 9 | Survives restart | VERIFIED at integration level (key ring persisted and certificate-wrapped, cookie from another host reads; live restart kept the key). NOT VERIFIED: a real signed-in cookie across an Azure redeploy | KeyRingTests, ProductionStartupTests, 5.6 |
| 10 | Cookie, state, nonce, PKCE, fixed redirect, secret not leaked | VERIFIED | 5.3, 5.5, 5.6; start URL has `state`, `nonce`, `code_challenge_method=S256`, `prompt=select_account`, no `hd`, `login_hint`, `access_type` |
| 11 | Dev login unreachable, Production | VERIFIED locally; NOT VERIFIED on the deployed Azure site (needs the real run) | 5.1, 5.2 |
| 12 | Closed or suspended account: plain error, no password fallback | VERIFIED (provider error maps to the generic message; page shows only the Google button) | e2e + `A_soft_deleted_user...` |
| 13 | Sign-in scopes only; AI and web never get tokens | VERIFIED for scope (exact string; extra or missing scope refused). Token handling beyond tests NOT VERIFIED (tables not column-inspected); access-token sentinel absent from logs | `scope` tests, canary |
| 14 | No sign-up form or password field | VERIFIED by observation (page text, e2e methods test). No negative automated assertion (gap) | 5.2, `signin.spec.ts` |
| 15 | Accessibility of the sign-in, refused, cancelled, error screens | Automated part VERIFIED (axe on every outcome, focus lands on the heading, no live region, so announced once by focus). Screen reader and keyboard on real devices NOT VERIFIED (G3) | `SignInView.test.tsx`, `signin.spec.ts` |
| 16 | Rate limiting; a burst of failures does not lock out others | VERIFIED for failed or forged callbacks. FAILED for a start flood: one anonymous client takes the whole global start budget | D1 |

## 8. S4 local parts

| # | Criterion | Verdict |
|---|---|---|
| 1 | Order: S1 verified in Azure, outside check | NOT VERIFIED (needs Azure; this is the local rehearsal only) |
| 2 | Production settings | Locally VERIFIED: Production environment, dev login absent and refused if enabled, sessions and key ring in the database behind a certificate, Secure `__Host-` cookie, HTTPS-only redirect enforced and HSTS sent, data store and API publish no ports (compose network `internal`). "No default passwords" is NOT VERIFIED for Azure (local compose uses a throwaway password; `infra/main.bicep` sets `ASPNETCORE_ENVIRONMENT=Production`, no `Auth__DevLogin__Enabled`, same variable names as the compose file, read not deployed) |
| 3 | No open window | NOT VERIFIED (workflow, out of scope) |
| 4 | Fail closed on misconfiguration | The API half is VERIFIED: 16 misconfigurations refuse to start, so no app is served and no data is created. The web and ingress half is in the area I was told not to judge |
| 5 to 9 | Approvals, rollback, invitee notice, spend, evidence | NOT VERIFIED (human or Azure steps) |

## 9. Cleanup
`docker compose ... down -v` removed the four containers, the `tether-poc-prod_pgdata` volume and both networks. Two API containers that my own `docker run`
probes had left running after the client-side timeout were stopped and the backend network removed. The throwaway env folder was deleted. The pre-existing
`tether-poc-*` stack and `cc-postgres-dev` were left untouched. The dev API container (`aistudentnotesapp-api-1`) was already stopped before I started and was not restarted.
