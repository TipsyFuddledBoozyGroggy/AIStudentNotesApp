# Go-live sign-in: Google OIDC, invite-only, API in Production (proposal, senior-engineer-fable)

Date: 2026-11-27. Status: **PROPOSAL, independent draft for cross-critique. Nothing here is built, deployed or run.** Scope: PO-20cg step 1 (Google sign-in replaces dev login in Azure, Entra gate comes off, Production settings). Out of scope: export-all and delete-account (architect, PO-20ax), real Speech and Claude, Docs/Calendar connect (PO-9d; kept possible, not designed). No gate status changes.

Legend: **[V]** = I read it in this repository's code or docs today. **[A]** = assumption, not verified (no call was made to Google or Azure). Decisions respected: PO-6, PO-9d, PO-9j, PO-10n, PO-15a, PO-20ax, PO-20cf, PO-20cg, ADR-016.

## 0. The shape in one paragraph

The browser only ever talks to the web origin. The sign-in page asks the API (through the existing Next proxy) for a Google authorization URL, the browser goes to Google and comes back to `/api/v1/auth/google/callback` on the web origin, the API exchanges the code, validates the ID token, checks the invite list, finds or creates the user by Google `sub`, and calls the existing ADR-016 `SignInAsync`. No Google token is stored. Flow state lives in a small database table plus a `__Host-` binder cookie, exactly as the reviewed draft contract `calendar.v1` section 5.1 already specifies [V]. Azure runs the API as Production from the first cut-over run, still behind Entra; a second run, chosen by the PO, takes Entra off after an outside check proves dev login is gone.

## 1. OIDC flow

### 1.1 Contract `auth.v1` (new, small, frozen before any build)

Sign-in is already specified inside the DRAFT `calendar.v1` (routes, binder cookie, callback rules, tests CAL-01 and CAL-23) [V: docs/API_CONTRACTS_CALENDAR.md section 5; schemas in src/shared/contracts/calendar.v1]. That contract is large, unfrozen and labelled mock-only. I propose lifting the sign-in part, unchanged where possible, into `docs/API_CONTRACTS_AUTH.md` + `src/shared/contracts/auth.v1/` and freezing it; `calendar.v1` then refers to it (copied definitions with an equality test, the house rule J2 [V]).

| Route | Auth | Result |
|---|---|---|
| `GET /api/v1/auth/options` | none | 200 `{"google": bool, "devLogin": bool}`. New. Tells the web which sign-in to show (section 6). |
| `POST /api/v1/auth/google/signin/start` | none; Origin check (existing middleware covers every POST [V: HostServices.cs]) | Body `{"landing":"home"}`. 200 `{purpose:"signin", authorizationUrl, expiresAt}` and sets the binder cookie. 429 calm. Not mapped (404) when Google is not configured. |
| `GET /api/v1/auth/google/callback` | state + binder | Always 302 to the web origin: `/` on success, else `/signin?purpose=signin&outcome=<code>`. Never a body, never an error page. |
| `POST /api/v1/auth/logout`, `GET /api/v1/me` | unchanged [V] | |
| `POST /api/v1/dev/login` | unchanged, Development only [V] | |

Outcome codes used by sign-in (closed set): `signed_in`, `access_denied` (student pressed Cancel at Google), `state_invalid`, `provider_unavailable`, and one **new** code `not_invited`. Also fixed in `auth.v1`: the session cookie is `SameSite=Lax` (code is Lax, API_CONTRACTS.md says Strict; Lax is needed for the return from Google; this closes finding M2C-08 as a documentation fix [V: findings-register]).

Rejected: (a) a `GET /signin/google` link that redirects straight to Google. Simpler, works without JavaScript, but a GET start cannot be Origin-checked, so any page can push a browser into a flow; the draft contract already chose POST and was security-reviewed. (b) Building inside `calendar.v1` as is: it would ship a production path under a contract that says "mock and fake-Google only".

### 1.2 The authorization request

Pinned parameters, built only by the server: `client_id`, `redirect_uri`, `response_type=code`, `scope=openid email profile`, `state`, `nonce`, `code_challenge`, `code_challenge_method=S256` (always explicit; Google defaults to `plain` [V: vendor report 9.8]), `prompt=select_account`. Not sent: `access_type` (so no refresh token is issued), `include_granted_scopes`, `login_hint`, `hd`. Endpoints are constants in code (authorize, token, JWKS URLs from vendor report 9.1 [V]); no runtime discovery fetch and no configuration key can point them elsewhere.

### 1.3 Where state, nonce and PKCE live

- `state`: 32 random bytes, base64url. The database stores only `SHA-256(state)` in `identity.oauth_states` (`state_hash` PK, `binder_hash`, `purpose`, `landing`, `created_at`, `expires_at` = +10 min, `consumed_at`). The `purpose` column exists from day one so the later connect flow reuses the table.
- Binder cookie: `__Host-cc.signin` (`cc.signin` in Development, same rule as the session cookie [V: IdentityModule.cs]), 32 random bytes, `HttpOnly; Secure; SameSite=Lax; Path=/; Max-Age=600`; only its hash is stored. Replaced by each new start; cleared by every callback, success or failure.
- `nonce` and the PKCE verifier are **derived, not stored**: `HMAC-SHA256(key, state || "nonce")` and `HMAC-SHA256(key, state || "pkce")`, key = `DerivedKeys.Derive("oauth-state.v1")`. That label is already reserved in code [V: RootSecret.cs `Labels.OAuthState`]. The row therefore holds no secret: a database read gives an attacker nothing to finish a flow with, and no ciphertext column or extra key is needed. The verifier never leaves the server.
- Single use is enforced in the database: `UPDATE ... SET consumed_at = now WHERE state_hash = @h AND consumed_at IS NULL AND expires_at > now`, one row affected or the callback stops with `state_invalid`. A double or concurrent callback yields one session (CAL-23).
- Clean-up: each start deletes expired rows (one indexed delete). No timer: a background loop would push the idle API above the Container Apps idle-billing threshold, the same reason the syllabus worker poll was slowed [V: main.bicep]. Steady-state size is bounded by rate limit times lifetime (section 9).

Rejected: (a) all flow state in one Data Protection encrypted cookie, no table. Less code and no migration, but replay protection then exists only in the browser, and an API restart between start and callback breaks sign-in whenever the key ring is lost. (b) Storing the verifier encrypted (the draft's wording): more code and one more key for no gain over derivation. (c) The ASP.NET `AddOpenIdConnect`/`AddGoogle` handler: see 1.5.

### 1.4 Callback order (nothing is written until step 7)

1. GET only. `Cache-Control: no-store` and `Referrer-Policy: no-referrer` are already on every API response [V: Program.cs].
2. Read `state` and the binder cookie; consume the row (above); compare the binder hash in constant time. Any failure: `state_invalid`. No call to Google has happened yet.
3. `error=access_denied` gives `access_denied`; any other `error` gives `provider_unavailable`. `error_description` is never read.
4. Exchange the code at the token endpoint (client secret, exact `redirect_uri`, derived verifier). 10 s timeout, no redirects followed, no proxy from the environment, 64 KB response cap, no retry.
5. Validate the ID token (1.5). The access token in the same response is dropped unread.
6. `email_verified` must be true and the email must be on the invite list (section 3), else `not_invited`.
7. Find or create the user by subject (section 2); update email and name if changed.
8. `IdentityEndpoints.SignInAsync` [V]: revokes whatever session the browser carried, creates a new `identity.sessions` row, issues the cookie. Clear the binder. 302.

### 1.5 ID token validation: library for the token, hand-written flow

- Token: `Microsoft.IdentityModel.JsonWebTokens` (`JsonWebTokenHandler.ValidateTokenAsync`), new package reference, exact version pinned at build [A: not in the shared framework; version to choose then]. Rules: algorithm RS256 only; issuer `https://accounts.google.com` or `accounts.google.com`; audience = our client id; signature against Google's JWKS; expiry required, 2 minutes clock skew, time from the app `TimeProvider`; then by hand: `nonce` equals the derived value (constant time), `azp` equals the client id when present, `sub` is 1 to 200 printable ASCII characters, granted `scope` is exactly the three sign-in scopes.
- JWKS: fetched from the pinned URL with a named `HttpClient`, cached per its cache header (floor 5 minutes, ceiling 24 hours), refreshed at most once per 5 minutes for an unknown `kid`.
- The token came straight from Google over TLS, so the signature check is defence in depth; it stays because the library makes it nearly free and it is what the tests exercise.

Rejected: (a) the ASP.NET OpenID Connect or Google handler. It builds `redirect_uri` from the request host and scheme, which here are the internal name and plain http [V: main.bicep `allowInsecure`, `AllowedHosts`], it keeps state in Data Protection cookies, it signs in to its own scheme, and it cannot express "POST start returns a URL" or the closed outcome redirects without overriding most of its events. More glue than the roughly 400 lines it would replace, and harder to test. (b) Hand-parsing the JWT: no. (c) `Google.Apis.Auth`: a Google dependency with its own certificate fetching that a fake issuer cannot stand in for.

### 1.6 What is stored, and the port

Stored per user: subject, email, display name, and one new nullable boolean "the `hd` claim was present" (the draft contract asks for it at sign-in; never the domain itself). **No Google access, refresh or ID token is stored or logged; nothing reaches the AI module** (invariant 3). No token revoke call is made: revoke removes every grant for the project, including a later Calendar grant [V: vendor report 8.10].

Port change (internal interface, free to change [V: ExternalIdentity.cs]): `Uri BuildAuthorizationUrl(state, nonce, codeChallenge)` and `Task<ExternalIdentity> CompleteSignInAsync(code, codeVerifier, expectedNonce, ct)` returning `ExternalIdentity(Subject, Email, EmailVerified, DisplayName, HostedDomainPresent)`. The unused `state` argument goes (state is checked before the provider is called). `UnconfiguredGoogleIdentityProvider` stays for "no client id configured"; then the two Google routes are not mapped and `auth/options` says `google:false`.

## 2. User mapping

- Key: `users.external_subject = "google:" + sub` (dev rows already use `dev:` [V]). The unique index on it exists [V: IdentityDbContext.cs]. Email is never the key.
- **A migration is needed first:** `users.email` has a unique index today [V]. The contract requires "same email, different `sub` = two users" (CAL-23), and a dev-login row with the PO's own address would otherwise make his first Google sign-in fail with a 500. Change it to a plain index, and make dev login look its user up by `external_subject = "dev:" + email` instead of by email [V: it uses email today].
- `email_verified`: must be true to sign in at all, because the invite list is keyed by email. False is answered `not_invited`.
- Email or name change at Google: same `sub`, so same user; the stored email and display name are refreshed at each sign-in. If the new address is not on the invite list the sign-in is refused until the PO updates the list; data is untouched.
- Display name: the `name` claim, control characters removed, cut to 200; fallback is the existing `DisplayNameFrom(email)` [V]. `picture` is ignored.
- Soft-deleted user (`deleted_at` set) signing in again: refused, nothing created. What the student should see belongs to the architect's delete-account design; **open**.
- **Existing dev-login users in the Azure database: never migrated, never matched by email.** Anyone could type any address into dev login, so linking a `dev:` row to the real Google account of the same address would hand that person attacker-written data (same conclusion as google-tokens-and-data-residency.md 2.7 [V]). They stay as unreachable rows (dev login is not mapped in Production [V]); a start-up step outside Development revokes every session that belongs to a `dev:` subject. The data is synthetic. If the PO prefers a clean database, that is a separate, explicit wipe, not part of this slice.
- PO-10n needs no code: a closed Google account fails at Google, sessions end at their 14-day limit, there is no password fallback. PO-15a / PO-20w: accounts are assumed personal; refusal of school accounts is at connect, so sign-in only records the `hd` flag.

Rejected: a separate `external_logins(provider, subject)` table (token doc 2.7). Right if a second provider ever arrives; one column is enough now and the prefix keeps that door open.

## 3. Invite-only enforcement: Google's list **and** an app-side list

Google's Testing list alone is not enough, for three reasons:
1. It is a publishing status, not an access control. All three sign-in scopes are non-sensitive [V: vendor report, console evidence], so one press of "Publish app" in the console would open sign-in to every Google account with no review step [A: inferred from "verification not required" for non-sensitive scopes]. A gate that one click removes is not a gate.
2. A non-listed person is stopped on Google's own "Access blocked" page and never returns to Tether [A], so Tether cannot give its own plain message, and a removed test user keeps a live Tether session.
3. The 100 cap counts over the lifetime of the project [V: vendor report].

So: **both**. Google's list decides who gets a token; Tether's list decides who gets an account.

- Where it lives: GitHub environment secret `TETHER_INVITED_EMAILS` (comma separated), copied to a Container Apps secret, read by the API as `Auth:Invite:Emails`. Never in the repository, never in a log (GitHub masks it; the API logs only the count). The PO keeps the source list in `secrets\invited-emails.txt` (the `secrets/` folder is git-ignored [V: .gitignore]) and pushes it with a script (section 8). A change takes one deploy, which is acceptable for at most 100 people and keeps every change deliberate and PO-approved.
- Matching: trimmed, lower-cased, exact. No Gmail dot or plus folding.
- Fail closed: outside Development the API refuses to start with Google configured and an empty list. There is no "open" value in code, so open sign-up cannot be switched on by configuration (house pattern: a flag is not a human gate [V: DocsModule.cs comment]).
- Removal bites at once: at start-up outside Development the API revokes the sessions of every user whose email is not on the list (one UPDATE, same step as the `dev:` revocation). The list only changes through a deploy, and a deploy restarts the API, so enforcement is exact without a per-request check.
- Refusal path: the check runs before find-or-create. No user row, no session row, the state row is consumed, the binder is cleared, the Google tokens are dropped, the log line carries the outcome code only. The browser lands on `/signin?purpose=signin&outcome=not_invited` (copy in section 6).
- PO-20ax: the list should hold only the PO's own account until export-all and delete-account exist. Preflight prints a warning (not an error) when it has more than one entry.

Rejected: (a) an `identity.invites` table with an admin screen: there is no admin role or UI, and it is the right design only when open sign-up gets close. (b) Hashes instead of addresses in the secret: unsalted hashes of email addresses are guessable, and the secret store is already the trust boundary for the database password.

## 4. Public origin, redirect URI, return URL

- New required key `Auth:Google:PublicOrigin`: an absolute origin, no path. Start-up refuses it outside Development unless it is `https` and is one of `Security:AllowedOrigins`. Bicep sets it from the same `webOrigin` variable that already feeds `Security__AllowedOrigins__0` [V: main.bicep], which comes from the environment's default domain, not from any request.
- `redirect_uri = PublicOrigin + "/api/v1/auth/google/callback"`, a constant path, the path the existing Google client already uses for localhost [V: vendor report addendum]. **Never from `Host` or `X-Forwarded-*`**: the API sees the internal host name and plain http, and it trusts no proxy in Azure [V: main.bicep "Deliberately NOT set"], so those headers are either wrong or attacker-chosen.
- The callback reaches the API through the existing rewrite `/api/:path*` [V: next.config.mjs; target baked at build by `API_PROXY_TARGET`, src/web/Dockerfile]. Every post-callback `Location` is `PublicOrigin` + a path from a server-side table keyed by `landing` (`home` -> `/`). The session cookie is set with `Secure` regardless of the http hop (`CookieSecurePolicy.Always` outside Development [V]).
- **There is no return-URL parameter anywhere.** The client can send only the `landing` enum; unknown query parameters are ignored. Version 1 always lands on home. If "return to where I was" is wanted later, keep the path in `sessionStorage` on the client and accept only a single-slash internal path matching a known route; not built now.
- [A] to prove before Azure: the Next standalone proxy passes a 302 with `Location` and `Set-Cookie` through unchanged. Proven locally with one Playwright request against the compose stack (`maxRedirects: 0` on the callback with a bad state; expect 302 to `/signin?...outcome=state_invalid`). No Google needed.
- The `code` travels in a query string. The API's request-URL logging is switched off in code [V: Program.cs log filters]; Next writes no access log [A]; whether the Container Apps ingress records query strings is [A]. The code is single use and useless without the client secret and the verifier.

## 5. Production mode in Azure

What changes when `ASPNETCORE_ENVIRONMENT=Production` (each checked in code):

| Area | Effect | Action |
|---|---|---|
| Dev login | Start-up throws if `Auth:DevLogin:Enabled` is true; shipped default is false; route not mapped [V] | Bicep drops the variable. The template keeps **no** Development branch (section 7). |
| `AllowedHosts` | `*` or empty refused outside Development [V: EdgeConfig.cs] | None: Bicep already sets the two internal names and the probes send that Host [V]. |
| `Security:RootSecret` | Required outside Development [V] | None: already supplied as a secret [V]. |
| Migrations | Run only with `Database:MigrateOnStartup=true` outside Development [V] | None: already set [V]. Dev-user seeding becomes a no-op [V]. |
| Mock providers | Replay speech, mock AI, mock syllabus and demo Docs all still start in Production; only fault-injection and script keys are refused, and none is set [V: the four module guards] | None. |
| Cookies | Name becomes `__Host-cc.session`, always `Secure` [V]; HSTS sent by the API [V] | Old dev cookies stop working; intended. |
| Rate limits | All callers reach the API from one address (no trusted proxy [V]), so per-IP buckets are shared by everyone | Known limit; see section 9. |

### Data Protection key ring (cookie keys must survive restarts; budget USD 25 a month in total)

| Option | Cost | Security | Notes |
|---|---|---|---|
| **A. PostgreSQL** (`PersistKeysToDbContext`, table `identity.data_protection_keys`), each key wrapped with AES-256-GCM under `DerivedKeys.Derive("dp-keyring.v1")` | USD 0, no new Azure resource | A database read or a backup alone does not yield cookie keys; the wrapping secret is the root secret already held as a Container Apps secret | One more package; about 110 lines for the wrapper pair; fully testable in CI. Rotating the root secret signs everyone out (already true for idempotency keys [V]). |
| B. Azure Files share mounted at the existing `DataProtection:KeysPath` seam | cents a month [A: not priced] | Keys are plain XML on a share reachable with the storage account key; finding M1-F07 stays open | No app code, but a new storage account, a mount the container cannot start without, and nothing testable outside Azure. |
| C. Blob storage + Key Vault (Microsoft's standard pattern) | cents a month [A] | Best | Needs a managed identity and role assignments the deploy identity cannot create [V: main.bicep header], so more PO scripts. The right move when Key Vault arrives for Google token keys (open item Q5). |
| D. Leave it | 0 | n/a | Every deploy signs everyone out. The PO asked for this to end. |

**Recommendation: A.** It costs nothing, closes M1-F07 (not encrypted at rest, Production only warns), and CI can prove it (sign in, rebuild the host on the same database, the cookie still works; with a different root secret it does not and the API still starts). Why wrap at all: a session cookie is only a pointer to a `sessions` row (ADR-016), so someone who can read the database and the key ring could forge a cookie for any live session; wrapped, a read-only leak cannot act as a user. `KeysPath`, when set, still wins (local compose uses it [V]). Caveat: the key table must exist before the first request, so a Production host must have run migrations; the existing no-migrate Production test fixture needs a look [V: CsrfAndConfigTests].

## 6. Web

- `SignInView` first calls `GET /auth/options`. `google:true` shows the Google button. `devLogin:true` shows today's email form under the heading "Developer sign-in (this computer only)". The API is the authority at run time; a build-time flag was rejected because one image would then lie in one of its two environments, and the route answers 404 anyway if the form were ever shown wrongly.
- One real `<button type="button">`: "Sign in with Google", Google mark as a decorative SVG (`aria-hidden`), following Google's button guidelines [A: not re-read]. On press: `POST signin/start`, check that the returned URL's origin is exactly `https://accounts.google.com`, then `window.location.assign`. Busy text "Opening Google..."; the button stays focusable (`aria-disabled`). No automatic redirect on page load: the student starts it.
- Text under the heading (DRAFT copy for the PO): "Tether is invite-only for now. Use the Google account you were invited with. Tether sees your name and email address from Google, nothing else." and, smaller, "No Google account? You need one to use Tether for now." (PO-9j) and "If Google says 'Access blocked', that account has not been invited yet."
- Messages, fixed strings chosen by the outcome code (an unknown code shows the last one). After showing, the query is removed with `router.replace("/signin")` and focus moves to the message:

| Outcome | Copy (DRAFT) | Role |
|---|---|---|
| `not_invited` | "This Google account is not on the invite list yet. Nothing was saved. If someone invited you, ask them to add this address, or try a different Google account." | alert |
| `access_denied` | "You stopped before finishing at Google, so you are not signed in. Nothing was saved. You can try again whenever you like." | status |
| `state_invalid` | "That sign-in took too long or was started in another tab. Please start again." | alert |
| `provider_unavailable` | "We could not finish signing you in with Google just now. Nothing was saved. Try again in a moment." | alert |
| 429 on start | "Too many tries in a short time. Wait a minute, then try again." | alert |
| network | existing "We can't reach the server. Try again in a moment." [V] | alert |

- WCAG 2.2 AA: errors in text with the existing icon pattern, never colour alone; visible focus; target at least 44 px; reflow at 400% through the existing auth template [V: SignInView comment]; no countdown is shown and the 10-minute limit costs nothing but a restart (2.2.1); no memory or puzzle step on Tether's side (3.3.8); page title stays "Sign in". Automated axe checks cover each state; **screen-reader checks by a person stay NOT RUN** (gate 3).
- `client.ts`: `getAuthOptions()`, `startGoogleSignIn()`. MSW mock mode answers `{google:false, devLogin:true}` so local mock work is unchanged.

## 7. Cut-over without ever exposing dev login

Principle: **the API's sign-in mode and the web's gate never change in the same deployment, and the template can no longer produce a Development API at all.**

Changes:
- `main.bicep`: the API is always `Production`; the `Auth__DevLogin__Enabled` line is deleted, not parameterised. New: `googleClientId`, secure `googleClientSecret`, secure `invitedEmails`, `publicGate` = `entra` (default) or `google`. Guards (`fail()` like today's [V]): Google values missing or half set; `publicGate=google` without them. `gateOn = entraOn || (publicGate == 'google' && googleOn)`. The `webAuth` resource stops being conditional: with `publicGate=google` it is deployed with `platform.enabled=false`, because an incremental deployment does not remove a resource whose condition turned false [A], so today's template could never take Entra off. The web app gets `dependsOn: [api]`. Output `accessGate` gains `google`.
- Workflow input `access`: `team` (Entra on; default), `invited` (Entra off, Google and the invite list are the gate), `closed` (web ingress internal; the emergency stop).
- `preflight.sh` additionally requires, in every mode: `GOOGLE_CLIENT_ID` (shape `<digits>-<letters and digits>.apps.googleusercontent.com`), `GOOGLE_CLIENT_SECRET` (present, 20+ characters), `TETHER_INVITED_EMAILS` (1 to 100 valid addresses, each masked, only the count printed). `team` still needs the Entra pair. Output `gate=none|entra|entra+ip|google`.
- New `infra/scripts/api-signin-mode.sh` (read-only, like `web-auth-active.sh` [V]): prints `google` only when the API app's newest revision is the ready one, it is the single active revision, and its environment shows Production with no dev-login variable. A run with `access=invited` **stops before deploying** unless this prints `google`. No automatic two-pass here: a person must have tried Google sign-in in between.
- `smoke.sh`, new expectations for `gate=google`, all from outside, in this order, no request to Google:
  1. `POST /api/v1/dev/login` with the right Origin and a well-formed synthetic address: must be **404**. 204 is a breach (close the ingress); 400 or 429 mean the route exists, also a breach.
  2. `GET /api/v1/auth/options`: 200 with `devLogin:false` and `google:true`. `devLogin:true` is a breach.
  3. `GET /api/v1/me`: 401. Hub negotiate: 401.
  4. `POST /api/v1/auth/google/signin/start`: 200; the URL starts with Google's authorize endpoint; its `redirect_uri` equals `https://$WEB_FQDN/api/v1/auth/google/callback` (compared, never printed); `code_challenge_method=S256`.
  5. `GET /signin`: 200. Only now is a 2xx correct, and only after 1 to 4 passed.
  `gate_verified=true` is written only after all five. For `gate=entra` the present checks stay, plus the Azure-side read that the API is Production.
- The fail-closed step stays as it is [V] and also covers `google`.

Order of runs:

| Step | Who | What | Result |
|---|---|---|---|
| 0 | Lead | Code merged, CI green, local rehearsal: compose stack with the API as Production and placeholder Google values; `smoke.sh` checks 1 to 5 pass against it | Nothing in Azure changed |
| 1 | PO | Section 8 steps 1 to 4 (redirect address, test users, two scripts) | Secrets in GitHub |
| 2 | PO approves | Run A, `access=team` | API is Production with Google sign-in, **still behind Entra**. Dev login no longer exists anywhere in Azure. |
| 3 | PO | Microsoft sign-in, then Google sign-in; a second, non-invited Google account is refused; sign out; sign in again after a restart | The only proof that the Console redirect address and test users are right |
| 4 | PO approves | Run B, `access=invited` | Entra off; outside checks 1 to 5; on any failure the ingress closes |
| 5 | PO, a week later | `create-signin-app.ps1 -Remove` | Entra registration gone; until then rollback to `team` is one run |

Rollback: from Run B, run again with `access=team` (Entra back on, Google sign-in stays) or `access=closed`. From Run A, fix forward; while it is broken nobody can sign in, which is safe. **Reverting to a commit that still has dev login must never be the first move:** run `access=closed` first, because the old template would switch the API to Development and the web ingress in one deployment with no guaranteed order. This goes in infra/README.md in capitals.

## 8. The PO's own steps (cmd on Windows; agents never see the secret)

1. **Redirect address.** Google Cloud Console, project `tether-oauth-dev`, Google Auth Platform, Clients, the client named Tether, "Authorized redirect URIs", Add URI. Paste the website address from the Azure portal (resource group `tether-dev`, container app `tether-web`, Application Url) and add `/api/v1/auth/google/callback` to the end. It must start with `https://` and have no trailing slash. Leave the localhost line. Save. Google says changes can take from five minutes to a few hours [A].
2. **Test users.** Same place, Audience, Test users, Add users: each Google account to let in. The page must still say "Testing". **Do not press "Publish app".**
3. **Client id and secret.** The console no longer shows an old secret [V: vendor report]; the downloaded client file kept as `.env.google.local` is the only copy. If it is lost: on the client page press "Add secret", download the file, then delete the old secret. Then, in cmd from the repository folder, after `gh auth login`:
   `powershell -ExecutionPolicy Bypass -File .\scripts\google\set-google-signin-secrets.ps1 -ClientFile .\.env.google.local`
   The script (same pattern as `create-signin-app.ps1` [V]: checks `gh`, asks for a typed YES, pipes values straight to `gh secret set --env azure-dev`, prints names only) stores `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET`, and warns if the file's redirect list lacks the callback path.
4. **Invite list.** Put the same addresses, one per line, in `secrets\invited-emails.txt`, then:
   `powershell -ExecutionPolicy Bypass -File .\scripts\google\set-invited-emails.ps1`
   It stores `TETHER_INVITED_EMAILS` and prints only how many. Adding a person later = step 2, edit the file, this script, one deploy. (Two lists to keep in step; Google offers no supported way to script its test-user list [A].)
5. GitHub, Actions, "Deploy Azure dev (POC)", Run workflow, Access = `team`, approve. Try it (table above, step 3). Testers will first see a Google screen saying the app is being tested, and press Continue [A].
6. Run workflow again with Access = `invited`, approve. Open the address in a private window: the Tether sign-in page appears with no Microsoft prompt.

## 9. Tests and threats

CI makes **no call to Google**. The real `GoogleIdentityProvider` runs in tests; only its `HttpClient` handler is replaced by `FakeGoogle`, which holds a test RSA key, serves the JWKS, and answers the token endpoint: it checks the client secret, the exact `redirect_uri` and the PKCE verifier against the S256 challenge from the start URL, then returns an ID token it signs. It throws on any other host. Tests drive the flow as a browser would: POST start, read `state`, `nonce` and the challenge out of the returned URL, ask the fake for a code, GET the callback with the binder cookie. Fixture: `ApiFactory` with `EnvironmentName = "Production"` [V: the pattern exists]. Rejected: swapping the whole port for a stub (it would skip the validation code, the part most worth testing) and a fake-issuer container with configurable endpoints (a production configuration seam that points sign-in at another issuer is a risk in itself).

| Threat | Defence | Test |
|---|---|---|
| Login CSRF (attacker plants their own code in the victim's browser) | State bound to the binder cookie; start is POST with Origin check | Callback with a valid state and code but no or another binder: `state_invalid`, no session. Start without Origin: 403. |
| State replay, double callback | Atomic consume in the database; 10-minute expiry | Second callback: `state_invalid`. Two at once: one session. Clock +11 min: `state_invalid`. |
| Open redirect | No URL input; `landing` enum; `Location` built from configuration | Unknown `landing`: 400. `returnUrl` and `X-Forwarded-Host` ignored; every `Location` starts with the public origin. |
| Session fixation | `SignInAsync` revokes the carried session and issues a new id [V] | Old session row revoked, new id, binder never becomes a session. |
| Forged or wrong token | 1.5 | Wrong `iss`, `aud`, `nonce`, `azp`; expired; `alg=none`; HS256 signed with the client secret; unknown `kid`; unverified email. Each refused with zero user rows. |
| Stolen code | PKCE S256 + client secret | The fake rejects a wrong verifier; the start URL carries S256. |
| Not invited | Section 3 | Zero user and session rows; after removal and restart the sessions are revoked. |
| Account mix-up | Section 2 | Same `sub` with a new email = same user; same email with a new `sub` = two users; a `dev:` row with that email is untouched. |
| Flooding | `auth_start` and `auth_callback` limiter policies (draft values 300 a minute per address, 10 a minute per binder, PROPOSAL [V: calendar-policy.json]); rejection before any database write; at most about 3,000 live state rows | 429 with `Retry-After`; row count unchanged. |
| Emails and secrets in logs | Outcome codes only | The existing log canary at Trace level, extended with sentinel email, `sub`, code, state, ID token and client secret. |
| Misconfiguration | Start-up guards | Half-set Google values, non-https or unlisted public origin, empty invite list, dev login in Production (exists [V]): each refuses to start. |

Known limits, stated plainly: (1) in Azure every visitor shares one rate-limit address, so someone can exhaust the start budget and stall new sign-ins for a minute at a time; signed-in students are unaffected. Fixing it needs a trusted-proxy design across two proxy hops; not in this slice. (2) No audit events for sign-in yet (M1-F08b stays open). (3) NEW-02 (lockout) can be closed as not applicable: there is no Tether password to guess. (4) Nothing here is checked against live Google or Azure until Run A.

Also: web unit tests for every state and for refusing a non-Google URL; Playwright for the outcome page and the proxy 302 check; preflight cases in bash (missing or malformed Google values, empty and oversize lists, `invited` without Google); `az bicep build` and lint; actionlint.

## 10. Files, order, size, risk

| Area | Files | Rough size |
|---|---|---|
| Contract | `docs/API_CONTRACTS_AUTH.md`, `src/shared/contracts/auth.v1/*`, contract tests; ADR in DECISIONS.md | 250 lines |
| API | Identity: `GoogleIdentityProvider.cs`, `GoogleSignInEndpoints.cs`, `OAuthStateStore.cs`, `InviteList.cs`, `KeyRingProtection.cs` (new); `ExternalIdentity.cs`, `IdentityModule.cs`, `IdentityEndpoints.cs`, `IdentityDbContext.cs`, one migration (state table, key table, email index, `hd` flag), csproj; Host: `Program.cs` (two limiter policies, key-ring warning text) | 900 lines + 1,000 of tests |
| Web | `SignInView.tsx`, `lib/api/client.ts` and types, MSW handlers, tests, one Playwright spec | 250 + 300 of tests |
| Infra | `main.bicep`, `poc.bicepparam`, `preflight.sh`, `smoke.sh`, `api-signin-mode.sh` (new), `deploy-azure-dev.yml`, `infra/README.md`, a Production variant of the compose stack, `scripts/google/*.ps1` (two, new) | 450 lines |

Build order: (1) freeze `auth.v1`, architect signs; (2) API sign-in with the fake, then (3) the key ring, same engineer (shared module file and migration); (4) web and (5) infra in parallel with 2, on disjoint files, by the other senior and the junior for copy and mocks; (6) cross-review, QA, local rehearsal; (7) PO steps, Run A, PO check, Run B. About three senior-days before the PO's part.

**Riskiest part: the cut-over logic, not the OIDC code.** `smoke.sh` today treats any 2xx as a breach; the new mode must accept a 2xx on `/signin`, and a mistake there fails open. That is why the dev-login probe runs first, why the template loses its Development branch, and why Run B refuses to start unless Azure already shows a Production API. Second: things only Azure can show (the exact redirect address, the 302 through the proxy behind Easy Auth, turning Easy Auth off in place). Third: the hand-written key-ring wrapper, small but cryptographic; it gets its own review.

## Summary: my three most important choices

1. **Two invite lists, not one.** Google's Testing list is one console click from open sign-up, so Tether keeps its own fail-closed list (a GitHub secret the PO edits by script), checked before anything is created and re-applied at every start.
2. **Hand-written flow on the already-reviewed contract, library only for token validation.** State in a database table plus a `__Host-` binder cookie, nonce and PKCE verifier derived from the root secret, redirect address built from configuration, no Google token stored. The framework handler was rejected because it builds the redirect from request headers that are wrong behind the proxy.
3. **Dev login is removed from the Azure template before the gate moves, in two separate PO-approved runs.** Production with Google behind Entra first, a human check, then Entra off only after an outside probe proves `/dev/login` answers 404. The key ring goes into PostgreSQL, wrapped, at no cost.
