# Proposal (Opus): invite-only Google sign-in in Azure, API in Production

Status: **PROPOSAL** for the senior pairing protocol (step 1, written without seeing the other proposal). Nothing here is built.
Date: 2026-11-27. Scope: PO-20cg item 1 (Google sign-in replaces dev login in Azure, then the Entra gate comes off) and the
production settings it needs. Out of scope: export-all and delete-account (architect), real Speech and Claude, Docs/Calendar consent.
Tags: **[code]** = read in the repo at commit f4e422f; **[doc]** = stated in a repo document; **[assumed]** / **[UNVERIFIED]** = not checked.

## 0. Starting facts that shape the design
- The sign-in surface is already specified: `docs/API_CONTRACTS_CALENDAR.md` section 5.1/5.2 and `src/shared/contracts/calendar.v1`
  (POST `signin/start` with a binder cookie, GET `callback`, state hashed and single use, CAL-07/08/23) [doc]. It is DRAFT, not frozen.
  This proposal implements its **sign-in purpose only** and moves it into a small `auth.v1` contract; `connect` stays for later (PO-9d).
- `identity.users` has a **unique index on `email`** as well as on `external_subject` [code, IdentityDbContext.cs]. That contradicts CAL-23
  ("same email, different `sub` = distinct users") and will collide the first time the PO signs in with Google using the email he typed
  into dev login. A migration is required.
- `DerivedKeys` already has the label `oauth-state.v1` [code, RootSecret.cs]; HKDF purpose keys from `Security:RootSecret` are the house pattern.
- Mock providers are allowed in Production; only fault injection and scripted transcripts are refused outside Development/Test [code,
  AiModule/TranscriptModule/DocsModule/SyllabusModule]. The Bicep sets none of those keys [code]. So Production does not break the mocks.
- The API sees every caller as one IP address (no trusted proxies configured) [code + main.bicep comment]. Per-IP limits are global.
- Next rewrites `/api`, `/hubs`, `/ingest` to `http://tether-api`, frozen at build time [code, next.config.mjs, Dockerfile]. The API receives
  `Host: tether-api` over plain http. Nothing in a request tells the API its public origin reliably.
- The OriginCheckMiddleware runs **before routing** for every unsafe method [code]. A POST without an allowed Origin is 403, not 404.

## 1. OIDC flow
**Choice: authorization code + PKCE (S256) + client secret, server-side, hand-rolled flow, library-validated ID token.**

Contract `auth.v1` (new `docs/API_CONTRACTS_AUTH.md`, schemas in `src/shared/contracts/auth.v1/`, base `/api/v1`):

| Route | Auth | Request / response |
|---|---|---|
| `GET /auth/methods` | anonymous | `{"google": bool, "devLogin": bool}` (`no-store`). The web's only source for which sign-in to show. |
| `POST /auth/google/signin/start` | anonymous, Origin check, `auth-start` limit | body `{"landing":"home","returnPath"?: string}` (unknown members 400). 200 `{"purpose":"signin","authorizationUrl","expiresAt"}` + binder cookie. |
| `GET /auth/google/callback` | state + binder | Always 302, relative `Location`, `Cache-Control: no-store`, `Referrer-Policy: no-referrer`. Success: `<returnPath or />?purpose=signin&outcome=signed_in`; failure: `/signin?purpose=signin&outcome=<code>`. POST/OPTIONS 405. |
| `POST /auth/logout`, `GET /me` | unchanged | unchanged |

Outcome set for sign-in: `signed_in`, `access_denied` (user cancelled or Google refused a non-test user), `not_invited`, `state_invalid`,
`provider_unavailable`. `not_invited` is new versus calendar.v1. The start response reuses `calendar-google-auth-start-response` (copied,
equality-tested as calendar.v1 J2 does). `POST /dev/login` stays mapped only when dev login is enabled (unchanged).

Start (server): 256-bit `state`, 256-bit binder value. Stored row `identity.oauth_states` (`state_hash` = HMAC(`oauth-state.v1`, state) PK,
`binder_hash`, `purpose`, `return_path`, `created_at`, `expires_at` = +10 min, `consumed_at`). **Nonce and PKCE verifier are derived, not
stored**: `nonce = b64url(HMAC(oauth-nonce.v1, state))`, `verifier = b64url(HMAC(oauth-pkce.v1, state))`, challenge = S256(verifier). A
database reader learns neither; nothing to encrypt; rotating the root secret only breaks sign-ins in flight. (Amends 5.1 "verifier held
server-side encrypted"; equivalent or stronger, and less code.) URL: fixed `https://accounts.google.com/o/oauth2/v2/auth`, pinned parameter
set `client_id, redirect_uri, response_type=code, scope="openid email profile", state, nonce, code_challenge, code_challenge_method=S256,
prompt=select_account`. No `access_type=offline` (no refresh token is wanted). Binder cookie `__Host-tether.signin` (unprefixed
`tether.signin` in Development, like the session cookie [code]): HttpOnly, Secure, SameSite=Lax (the callback is a cross-site top-level
GET), Path=/, Max-Age=600, replaced by each start. Opportunistic cleanup deletes expired rows on each start; a global cap of 2,000 live
rows answers 429 above it (per-IP caps are meaningless while the API sees one IP).

Callback order (no state change before step 2 passes): (1) GET only; read `state`, `code`, `error` only (ignore `error_description`,
`hd`, `authuser`, `prompt`, `scope` query). (2) Atomic `UPDATE oauth_states SET consumed_at=now WHERE state_hash=@h AND consumed_at IS
NULL AND expires_at>now RETURNING binder_hash, return_path`; constant-time compare with HMAC of the binder cookie; any miss is
`state_invalid` (the row stays burned). Clear the binder cookie on every outcome. (3) `error=access_denied` → `access_denied`; other
`error` → `provider_unavailable`. (4) Exchange the code at `https://oauth2.googleapis.com/token` (client_secret_post, `code_verifier`,
the same configured `redirect_uri`). (5) Validate the ID token. (6) Admission (section 3). (7) Find or create the user, then the existing
`IdentityEndpoints.SignInAsync` (revokes the cookie's previous session, creates a new session row and cookie: ADR-016, N-03) [code].

ID token validation with `Microsoft.IdentityModel.JsonWebTokens` (`JsonWebTokenHandler`): `ValidIssuers` = `https://accounts.google.com`
and `accounts.google.com` (Google documents both [assumed, re-check]); `ValidAudience` = client id; `azp`, when present, = client id;
`ValidAlgorithms` = RS256 only (rejects `none` and HS256 key confusion); keys from the pinned JWKS URL
`https://www.googleapis.com/oauth2/v3/certs`, cached by `Cache-Control`, refetched on an unknown `kid` at most once per 5 minutes; `exp`
and `iat` required, 2-minute skew; `nonce` = derived nonce (constant time); `sub` 1-255 printable ASCII. No discovery document fetch at run
time, no `tokeninfo` call. Named HttpClient `google-oidc`: `AllowAutoRedirect=false`, `UseProxy=false`, 10 s timeout, only the two hosts.

What is stored: the user row only (section 2). The access token and ID token are dropped after validation, never persisted, never logged.
Sign-in has no Google tokens to give anyone, so invariant 3 (AI never sees tokens) holds by construction.

Port: `IExternalIdentityProvider` becomes `Task<GoogleSignInResult> RedeemAsync(string code, string verifier, string expectedNonce, ct)`
returning `ExternalIdentity(Subject, Email, EmailVerified, DisplayName, HostedDomainPresent)` or a closed failure code. The real
`GoogleOidcProvider` replaces `UnconfiguredGoogleIdentityProvider` when `Auth:Google` is configured; the stub stays otherwise (local dev
without a client). The flow code (state, binder, admission, user mapping) lives in `Identity/Google/SignInFlow.cs` and is provider-agnostic,
so tests drive it through the real provider with a fake HTTP handler (section 9).

Rejected: **ASP.NET `AddOpenIdConnect` handler** (it builds `redirect_uri` from the request's scheme and Host, which here are `http` and
`tether-api`; it fetches discovery at run time; its state lives in Data Protection blobs and correlation cookies, coupling sign-in to the key
ring and making single use implicit; GET challenge versus the contract's POST start; it signs into its own scheme so the ADR-016 row needs
event plumbing; it cannot carry the later `connect` purpose that needs our own state table anyway). **Google Identity Services button /
One Tap** (ID token handed to browser JavaScript, third-party script and CSP, JavaScript origins). **Easy Auth's Google provider** (identity
would reach the internal API only as headers forwarded by Next, a spoofable trust path; not reproducible locally or in CI). **Fully
hand-rolled JWT checks** (signature and key handling belong in a maintained library). Cost of the choice: about 300 lines of flow code we own;
mitigated by the test list in section 9 and the senior review.

## 2. User mapping
- Key = `(provider, external_subject)` with `provider='google'` and the raw `sub`, unique. Migration `M00x_GoogleSignIn` adds `provider`
  (text, existing rows backfilled `'dev'`), `hosted_domain_present` (bool null; CAL 5.1 stores `hd` presence, refusal stays "at connect"
  per PO-20w/PO-20ae and is not built here), `last_sign_in_at`; replaces the unique `email` index with a plain one. Existing
  `external_subject` values (`dev:<email>`) are untouched.
- Email and display name are **display only**, refreshed from the token on every sign-in. Email changes therefore just update the row.
  `name` is trimmed, control and format characters stripped, capped at 100; fallback is the existing `DisplayNameFrom(email)` [code].
- `email_verified` is never used for linking; it is used for admission: an unverified email is `not_invited` (section 3).
- Existing dev-login users in the Azure database (synthetic data): **left inert, never linked by email** (token doc 2.7 [doc]). Outside
  Development, `SessionPolicy.IsValidAsync` also requires the user's provider to be `google`, so no `dev` session can survive the switch
  even if a key ring did. Deleting the inert rows waits for delete-account (PO-20ax); a PO question: "your Google login starts empty; keep
  or purge the old test data?". Rejected: email matching (account takeover if an email is reused); operator claim codes (no need for synthetic data).

## 3. Invite-only enforcement
**Both lists.** Google's Testing test-user list is the outer gate (PO-20cg). An **app-side allow-list** is the inner gate because:
one "Publish app" click in the Google console turns Testing into open sign-up for any Google account (up to the 100-user unverified cap
[doc, vendor report 7.4]); the app list is the only gate CI can prove; and it gives the PO a removal path that ends sessions (below).
- Where: GitHub environment secret `GOOGLE_ALLOWED_EMAILS` (comma-separated) → Container Apps secret → env `Auth__Google__AllowedEmails`.
  Never in the repo, never in a template output, masked in the workflow, and the API logs only the count. Max 100 entries, validated at startup.
- Who edits: the PO, with the new script (section 8). GitHub secrets cannot be read back, so the script keeps the list in a local file in
  the PO's profile (`%USERPROFILE%\.tether\google-invites.txt`, outside the repo) and re-sends the whole list. Changes apply on the next
  deploy (the PO approves every deploy anyway).
- Rule: `email_verified == true` AND lower-cased, trimmed email is in the list (exact match; no Gmail dot or plus folding). Checked on
  **every** sign-in, not only the first. `Auth:Google:Admission` must be `allowlist` (the only accepted value; open sign-up later is a new
  value plus a PO decision). Outside Development, Google config missing, list empty or admission not `allowlist` = refuse to start.
- Removal: at startup (Production) the API revokes sessions of users whose stored email is no longer on the list, and of any `dev` user;
  logs `Revoked {Count} sessions at startup` only.
- Refusal path: state consumed, binder cleared, **no user row, no session, no token kept**; 302 to `/signin?purpose=signin&outcome=not_invited`;
  a content-free log line `Google sign-in refused: {Reason}` with `Reason` in {`not_invited`,`email_unverified`}, no email, no `sub`.
- Rejected: Google list alone (one click from open; untestable); a database invitations table (needs an admin endpoint or DB access the PO
  does not have; new attack surface); hashed emails in the secret (anyone with Contributor reads the secret and the hash of a known email
  is trivial; adds friction for nothing).

## 4. Public origin, redirect URI, return paths
- `Auth:Google:RedirectUri` is **configuration**, set by Bicep from `webOrigin` (which it derives from the environment's default domain
  [code]) as `<web origin>/api/v1/auth/google/callback`. Never built from `Host`, `X-Forwarded-Host` or `X-Forwarded-Proto`. Startup
  check: absolute https (http only in Development and only for localhost), path exactly `/api/v1/auth/google/callback`, no query or
  fragment, and its origin equal to one entry of `Security:AllowedOrigins`. Bicep also outputs it so the PO script prints the same value.
- Locally the Google client already has `http://localhost:5080/api/v1/auth/google/callback` registered [doc, vendor report].
- The browser only ever talks to the web origin. The callback runs on the API through the Next proxy; the API answers with a **relative**
  `Location`, which Next passes through unchanged [assumed; the outside smoke check in section 7 proves it]. Set-Cookie through the proxy
  already works for dev login today [code + iteration 13].
- Return paths: the web passes `returnPath` from its own `?next=` only after its own check; the server is the authority: must start with a
  single `/`, no `//`, no `\`, no `%2F`/`%5C`, no control characters, at most 200 characters, and match a route allow-list (`/`,
  `/courses`, `/courses/{uuid}`, `/lectures/{uuid}`, `/syllabus`...). Anything else is 400 at start. It is stored in the state row and
  read back from there; the callback never reads a return URL from its query. No open redirect is possible because no client value
  reaches `Location` without matching the list.

## 5. Production mode in Azure
What changes when `ASPNETCORE_ENVIRONMENT=Production` [code unless marked]:
- `appsettings.Development.json` is no longer loaded (it carries `AllowedHosts: *`, dev login on, a localhost origin). `appsettings.json`
  defaults to dev login off; Bicep's env vars already override `AllowedHosts` (`tether-api;<internal fqdn>`, passes `EnsureAllowedHosts`)
  and `Security:AllowedOrigins:0`.
- `Auth__DevLogin__Enabled=true` **must not be sent**: with it the API refuses to start (fail closed). Bicep sends it only in Development.
- `Security:RootSecret` is already a required secret and passes the startup validation; keep the same value (ETags, idempotency, audit).
- Migrations run only with `Database:MigrateOnStartup=true`, which Bicep already sets. `SeedDevUsersAsync` is a no-op outside Development.
- Cookies: name `__Host-cc.session`, `SecurePolicy.Always`, so Secure is set even though the API sees http. HSTS from the API turns on.
- Mocks stay allowed; fault keys stay refused (none are set). Startup warning for `DataProtection:KeysPath` disappears with the change below.
- New: Google config and allow-list required outside Development (section 3).

**Data Protection key ring** (the cookie is unreadable after a restart today, so every deploy signs everyone out):

| Option | Cost | Security | Verdict |
|---|---|---|---|
| A. Ephemeral (today) | 0 | fine | Rejected: each deploy ends every session, mid-lecture included. |
| **B. Postgres table `identity.data_protection_keys`** (`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`), keys wrapped by an AES-256-GCM `IXmlEncryptor` keyed from `DerivedKeys` label `dataprotection-kek.v1` | 0 | A database copy or backup alone cannot mint cookies; the KEK lives apart from the data, matching the token doc's "backups useless without the KEK" rule [doc]. Root secret rotation = everyone signs out once (documented). | **Chosen.** No new Azure resource or permission; included in the 7-day PITR; one replica so no write races. |
| C. Azure Files volume + file system | cents [UNVERIFIED] | keys plaintext on a share readable with the storage key; SMB mount is a new startup failure mode | Rejected. |
| D. Blob + Key Vault wrap | cents [UNVERIFIED] | best custody | Deferred to production hardening: needs managed identity and role assignments the deploy identity cannot create, two SDKs, divergence from local/CI. |
| E. Drop Data Protection: cookie = random token hashed in `sessions` | 0 | removes the key ring entirely | Good long-term; rewrites the auth handler and hub/socket revalidation now. Not in this slice. |

Option B risk: a hand-written encryptor. Keep it tiny (format `v1|nonce|ciphertext|tag`, AAD `tether.dpkey.v1`), unit-test tamper,
wrong key and round-trip. If review rejects custom crypto, ship B without the encryptor and record the residual risk (ADR-016 still
requires a live session id for a forged cookie to work).

## 6. Web
- `SignInView` calls `GET /auth/methods` and renders: a **"Sign in with Google"** button when `google`; the dev form only when `devLogin`
  (so local and E2E keep working, Azure never shows it). If the call fails: the network message with a Try again button, no form.
  The dev form stays in the bundle; harmless, since the route does not exist in Production. Rejected: a `NEXT_PUBLIC_*` build flag
  (can drift from what the API actually allows; the API is the truth).
- Button: plain `<button type="button">`; on press `POST signin/start` with `returnPath`, then `location.assign(authorizationUrl)` after a
  client-side check that the URL starts with `https://accounts.google.com/`. Busy text "Opening Google...", `aria-busy`, disabled while
  busy. The G mark is a local static SVG, `aria-hidden`; accessible name = visible text. Google branding rules for the mark [UNVERIFIED].
- `AuthProvider`: on 401 navigate to `/signin?next=<current path>`; after `signed_in` strip the query with `router.replace`.
- Copy (draft; UX review). Lead: "Sign in" / "Tether uses your Google account. Only invited accounts can sign in for now."
  `access_denied`: "Sign-in was cancelled. Nothing changed. You can try again when you are ready."
  `not_invited`: "This Google account is not on the invite list yet. Nothing was saved. You can try a different Google account, or ask the
  person who invited you." `state_invalid`: "That sign-in took too long or was already used. Please try again."
  `provider_unavailable`: "We could not finish signing in with Google. Please try again in a few minutes." Unknown outcome value: same as
  `provider_unavailable` (closed set, the query is never echoed).
- WCAG 2.2 AA: message in a `role="status"` region (4.1.3), focus moved to the h1 on load with an outcome so screen readers read it
  (2.4.3); icon plus text, never colour alone (1.4.1); button at least 44 px (2.5.8 needs 24); visible, unobscured focus (2.4.7,
  2.4.11); no timers or auto-redirects on our page; 3.3.8 accessible authentication: our page has no cognitive test and Google's page
  allows password managers [assumed]; same help location on every state (3.2.6). axe checks per state in vitest and Playwright.

## 7. Cut-over without ever exposing dev login
Invariant kept everywhere: **API in Development ⇒ Entra gate required for public ingress** (unchanged), and **Google configured ⇒ API runs
Production** (so dev login cannot start). There is no parameter combination giving Development plus public plus no Entra.

Bicep: params `googleClientId`, `@secure googleClientSecret`, `@secure googleAllowedEmails`, `entraGate bool = true`. `googleOn` = all three
set; `apiEnv = googleOn ? 'Production' : 'Development'`; `entraActive = authOn && entraGate`; `gateOn = entraActive || googleOn`. New
guards: Google half-configured; `entraGate=false` without `googleOn`. `webAuth` is declared whenever `authOn` with `platform.enabled =
entraActive`, because an incremental deployment does **not** delete an omitted child resource [assumed ARM behaviour]: leaving it out
would leave Easy Auth on. API env adds `Auth__Google__*`, `Auth__Google__RedirectUri`, `Auth__Google__Admission=allowlist`. Outputs
add `googleRedirectUri`, `apiEnvironment`, `accessGate` values `google`, `google+ip`, `entra+google`.

Preflight gates: `none`, `entra`, `entra+ip`, `entra+google`, `google`, `google+ip`. Requires: Google client id matches
`^[0-9]+-[a-z0-9]+\.apps\.googleusercontent\.com$`, secret non-empty, allow-list 1-100 syntactically valid emails (masked one by one
before anything prints, like the IP list); `ENTRA_GATE=off` only with Google complete, else fail.

Smoke additions. From Azure (before opening anything in a gate change): API template env `ASPNETCORE_ENVIRONMENT == Production` and no
`Auth__DevLogin__Enabled` when the gate contains `google`; authConfig enabled state equals `entraActive`. From outside, gate `google`
(the page is meant to be public now, so "2xx on /signin = breach" is replaced by these, each with Origin = the web origin):
(a) `GET /api/v1/me` → 401; (b) `POST /api/v1/dev/login` with a JSON body → **404** (204 = breach; without Origin it would be 403
and prove nothing); (c) `GET /api/v1/auth/methods` → `devLogin:false, google:true`; (d) `POST signin/start` → 200, URL host
`accounts.google.com`, `redirect_uri` = expected value, `code_challenge_method=S256`, binder cookie `__Host-` + Secure + HttpOnly;
(e) `GET callback?state=x&code=y` → 302 with relative `Location: /signin?purpose=signin&outcome=state_invalid`; (f) HSTS present.
Any breach → existing fail-closed step makes the web internal. No call reaches Google (the smoke never follows the URL).
Gate `google+ip`: outside answers 403 as today; the Azure-side checks carry it. Gate `entra+google`: the existing 302/401 check.

Order of runs: **0** code merged, CI green. **1** PO: Google console redirect URI + test users, script stores the three secrets.
**2 Run A (`entra+google`)**: API becomes Production with Google; Entra stays in front, so nothing new is public. PO tests through Entra:
invited account works; an account on Google's test list but not on the app list sees `not_invited`; a non-test account sees `access_denied`;
the sign-in page has no dev form; a second deploy keeps him signed in (key ring); a lecture with hub and audio works. Lead records results.
**3** PO runs the script with `-DropEntraGate`. **4 Run B (`google`)**: because the gate type changes, pass 1 deploys with the web internal
(Easy Auth disabled, API unchanged), checks from Azure, then pass 2 opens the ingress and the outside checks run. Later runs with an
unchanged gate are one pass. **5** Optional cleanup: `create-signin-app.ps1 -Remove`, then a deploy removes the unused Entra secret.
Rollback: app defect → revert and redeploy (images are per commit); want the Entra wall back → `-RestoreEntraGate` and deploy
(`entra+google`; re-run `create-signin-app.ps1` first if step 5 happened); back to dev login → script `-Remove` (Google secrets gone),
deploy: API returns to Development, preflight requires Entra. Older images run on the new schema (migration is additive plus an index
change) [UNVERIFIED; check once in CI with the previous image].

## 8. The PO's own steps (cmd on Windows)
New script `scripts/azure/set-google-signin.ps1` (same pattern as `create-signin-app.ps1` [code]: own az profile, refuses a service
principal, YES prompt, every native call checked, secret piped straight into `gh secret set`, never shown, never written to disk).
1. Open a Command Prompt and run, one line at a time:
   `cd /d C:\Dev\AIStudentNotesApp` then `set "AZURE_CONFIG_DIR=%USERPROFILE%\.azure-po"` then `az login --tenant <your tenant id>`
   then `gh auth status` then `powershell -ExecutionPolicy Bypass -File .\scripts\azure\set-google-signin.ps1`.
2. The script prints one web address ending in `/api/v1/auth/google/callback` and waits. In a browser open the Google Cloud console,
   choose the project **tether-oauth-dev** at the top, open **Google Auth Platform → Clients → Tether**, under **Authorized redirect
   URIs** click **Add URI**, paste the address, **Save**. Keep the localhost address that is already there. (Google says changes can take
   a few minutes to apply [UNVERIFIED].)
3. Still in the console: **Audience → Test users → Add users**, type each invited person's Google address, **Save**. Leave the status at
   **Testing**. Do **not** click "Publish app".
4. Back in the script, press Enter. It asks for the **Client ID** (copy it from the Tether client page) and then the **Client secret**
   (typing is hidden; if the console no longer shows the old secret, use **Add secret** there and copy the new one [UNVERIFIED UI]).
   Never paste the secret into a chat.
5. It asks for the invited Google addresses (or reads them from `%USERPROFILE%\.tether\google-invites.txt`), shows only the count, asks
   YES, then stores `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `GOOGLE_ALLOWED_EMAILS` on the GitHub environment `azure-dev`.
6. Tell the Lead "Google secrets stored". Approve the deploy in GitHub when asked.
Later: add someone with `-Invite name@example.com` (and add them to Test users in step 3); remove with `-Uninvite ...`; then a deploy. Drop
the Microsoft wall with `-DropEntraGate`, bring it back with `-RestoreEntraGate`; remove everything with `-Remove`. Rotate the client
secret by running it again. The script never touches Azure resources; it only reads the environment's domain to print the address.

## 9. Tests (no call to Google in CI)
Fake: an in-process `FakeGoogle` (`HttpMessageHandler`) swapped in as the primary handler of the `google-oidc` client through
`WebApplicationFactory.ConfigureTestServices` only. It serves the token endpoint (checks code, client secret, `redirect_uri`, and that
`code_verifier` hashes to the challenge from the start URL) and a JWKS with a test RSA key, and mints ID tokens with any claims. The
Google URLs are constants; there is **no configuration key that points the app at another issuer**, so no setting can open a fake in Azure.
Required tests (CAL-07/08/23, CV-21 list included): start URL parameter set exactly (S256, no `access_type`, `redirect_uri` from config
even with a spoofed `Host`/`X-Forwarded-Host`); start without or with a foreign Origin 403; binder cookie attributes in a Production-
environment factory; happy path creates one user and a session with a new session id and clears the binder; double callback → one user,
one session, second `state_invalid`; replayed, expired (fake `TimeProvider`), tampered and foreign-binder state → `state_invalid` **with
zero calls to FakeGoogle**; `error=access_denied` with valid state → `access_denied`, nothing created; `error` with bad state →
`state_invalid`; ID token wrong iss/aud/azp/exp/nonce, `alg:none`, HS256 signed with the public key, unknown `kid`, bad signature →
`provider_unavailable`, nothing created; `not_invited` and `email_unverified` create nothing; same email two `sub`s → two users; dev user
with the same email as a Google user → distinct users (index dropped); email change updates the row; return-path hostile cases
(`//x`, `/\x`, `https://x`, `/%2F%2Fx`, control chars, 201 chars, unlisted route) → 400; 429 on the start budget; Production factory:
`/dev/login` 404, `/auth/methods` says `devLogin:false`, startup refuses missing Google config, empty list, bad redirect URI, redirect origin
not in AllowedOrigins, and dev login in `Production`/`Test`/`Testing`; `dev` sessions invalid outside Development; startup revocation of
uninvited users; key ring: a cookie from host 1 works on host 2 over the same database, stored XML contains no plaintext key, a wrong
root secret signs people out without a crash; **log canary** at Trace over the whole flow finds no email, `sub`, code, state, nonce,
token or client secret. Web: vitest per outcome state, methods fallback, axe; Playwright keeps dev login for E2E and covers the page states
with MSW. Infra: preflight case matrix (each gate, half-configured Google, `ENTRA_GATE=off` alone), bicep build/lint, actionlint.

Threats and controls: login CSRF → binder cookie + POST start + Origin; state replay → single-use atomic consume; code interception →
PKCE + confidential client; mix-up → one provider, pinned issuer; open redirect → server-side return-path list, relative Location;
session fixation → new session row and id at sign-in, old one revoked (N-03); token or code leakage → no-store, no-referrer, query
dropped from logs (`Hosting.Diagnostics` already silenced [code]), `System.Net.Http.HttpClient.google-oidc.*` filtered to Warning, Google
error bodies never logged; forged ID token → RS256-only via library, pinned JWKS, no redirects; host-header injection → redirect URI from
config; emails in logs, repo or public workflow log → none written, secrets masked, count only; state-table and sign-in floods → global
cap and the `auth` limits (residual: one shared IP means one client can use up everyone's sign-in budget; raise `auth-start` to about
30/min for invite-only and fix trusted proxies once measured in Azure; NEW-02 lockout becomes not applicable: no password exists);
Google project published by mistake → app allow-list; uninvited user keeps a session → startup revocation; dev login reachable →
Production + Bicep guard + smoke 404 check.

## 10. Files, order, size, risk
Order (contract first, then parallel file-disjoint work):
1. `docs/API_CONTRACTS_AUTH.md`, `src/shared/contracts/auth.v1/*` + fixtures, contract tests; calendar.v1 note that sign-in moved (~250).
2. API, one owner: `Identity/Google/{GoogleOidcOptions,GoogleOidcProvider,SignInFlow,AllowList,ReturnPath}.cs`, `Domain/{User,OAuthState}.cs`,
   `Persistence/IdentityDbContext.cs` + migration, `KeyRing/{IdentityKeyRing,RootSecretXmlEncryptor}.cs`, `ExternalIdentity.cs`,
   `Endpoints/IdentityEndpoints.cs`, `IdentityModule.cs`, `SessionPolicy.cs`, `SharedKernel/Security/RootSecret.cs` (labels),
   `Tether.Host/Program.cs` (log filter, `auth-start`/`auth-callback` policies), two package references (~900 lines + ~1,200 tests).
3. Web, other owner: `SignInView.tsx`, `AuthProvider`, `lib/api/{client,types}.ts`, MSW handlers, tests, logo asset (~300 + ~300).
4. Infra, after 2 lands: `infra/main.bicep`, `infra/poc.bicepparam`, `infra/scripts/{preflight,smoke}.sh`, `deploy-azure-dev.yml`,
   `infra/README.md`, `scripts/azure/set-google-signin.ps1` (~450).
5. Docs: ADR in `docs/DECISIONS.md`, findings register (CV-21 tests, NEW-02, M1-F09), `docs/PROGRESS.md`, gate register note.
Rough size: 3 to 4 pair-days including review. **Riskiest part: the gate switch in infra** (the smoke logic inverts "public page = breach",
Easy Auth removal under incremental deployment, and the two-pass sequencing). A fail-open there exposes the app; a bug in the OIDC code
at worst refuses sign-in. Hence Run A with Entra still in front, the Azure-side environment check before opening, and the 404 probe on
dev login. Second riskiest: callback ordering and ID token validation, covered by the zero-call-to-Google assertions.

## Summary: my three most important choices
1. **Hand-rolled code flow on the existing calendar.v1 sign-in contract, ID token checked by Microsoft's JWT library, nonce and PKCE
   verifier derived from the state with a root-secret key**: nothing secret is stored for a sign-in, the redirect URI comes only from
   configuration, and the same state table carries Docs/Calendar connect later.
2. **Two invite gates**: Google's test-user list plus an app allow-list from a PO-owned GitHub secret, checked on every sign-in against a
   verified email, with nothing created on refusal and sessions of removed people revoked at startup. Users are keyed by `sub`; the unique
   email index goes; dev users stay inert and cannot hold a session outside Development.
3. **Cut-over in two deploys with "Google configured ⇒ Production" hard-wired**: first Production + Google behind Entra for the PO's own
   test, then Entra off through an internal-first pass and an outside smoke check that proves dev login answers 404 and the redirect URI
   is right, with the existing fail-closed step. The key ring moves into Postgres, wrapped by a root-secret key, at no extra cost.
