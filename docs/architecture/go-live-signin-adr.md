# ADR-022 (accepted by the Lead, pending build): invite-only Google sign-in, API in Production

Date 2026-11-27. Converged from `proposals/go-live-signin-{opus,fable}.md` and both critiques under the Lead's rulings R1-R12.
Implements PO-20cg step 1 and S1/S4 in `docs/product/go-live-stories.md`. Nothing here is built. No gate changes.
**[V]** = checked in the repository, **[A]** = assumed and must be proven by a test or by Run A.

**Defects and clarifications found in the rulings (none reverses a ruling):**
- **C1 (R4).** The PO setup script must generate the key-ring certificate **only when `DP_CERT_PFX` does not yet exist** (it checks secret
  names, never values). Rotation is an explicit `-RotateKeyRingCertificate`, which moves the old value to `DP_CERT_PFX_PREVIOUS`.
  Without this rule, re-running the script to rotate the Google secret would replace the certificate and sign everyone out.
- **C2 (R10).** "Record the sign-in time" needs no new column. `identity.sessions.created_at` is written once at sign-in and the sliding
  refresh never touches it [V Session.cs, SessionPolicy.cs]. This ADR makes that a contract rule, so ADR-021's recent-sign-in check reads it.
- **C3 (R7, R10).** Two outcomes beyond `not_invited` and `school_account` are needed:
  - `account_closed`: a user row with `deleted_at` set.
  - `rate_limited`: the callback is a top-level navigation, so a 429 JSON body would show raw JSON to the student. The callback always answers 302.
- **C4 (R2).** If the `access` input has a default, it should be `team`. A careless run then puts Entra back, which is the safe direction. Owner: Fable.

## 1. Contract `auth.v1`
Lands as `docs/API_CONTRACTS_AUTH.md` plus `src/shared/contracts/auth.v1/` (schemas plus `fixtures/valid|invalid`). It is frozen before the
web and infra builds start. The sign-in part of the draft `calendar.v1` (section 5.1) now refers here. Shared definitions are copied and
equality-tested (J2).

Base path `/api/v1`. Errors are the existing ProblemDetails with a stable `code` [V Problems]. Unknown or duplicate JSON members give 400
`validation_failed`. No email, `sub`, code, state or Google text ever appears in a response body or `Location`.

| Route | Auth and checks | Request | Success | Errors |
|---|---|---|---|---|
| `GET /auth/methods` | anonymous | none | 200 `{"google": bool, "devLogin": bool}` | 429 |
| `POST /auth/google/signin/start` | anonymous; allow-listed `Origin` (middleware [V]); `auth-start` limits | `{"landing":"home"}` (`landing` enum; v1 has only `home`; `additionalProperties:false`) | 200 `{"purpose":"signin","authorizationUrl":"https://accounts.google.com/o/oauth2/v2/auth?...","expiresAt":"<instant>"}` plus the binder cookie | 400 `validation_failed`, 403 `csrf_failed`, 415, 429 `rate_limited` (also when the live-state cap is reached), 404 when Google is not configured (Development only) |
| `GET /auth/google/callback` | state plus binder; `auth-callback` limits | query `state`, `code`, `error` only; everything else is ignored, `error_description` is never read | always **302**, relative `Location`, binder cleared (see outcomes) | POST/PUT/OPTIONS give 405 |
| `POST /auth/logout`, `GET /me` | unchanged [V] | | | |
| `POST /dev/login` | mapped only in Development with dev login on [V] | unchanged | | 404 everywhere else |

`purpose` is an open enum: `signin` now; `reauth` (ADR-021) and `connect` (PO-9d) later. The callback dispatches on the purpose stored in the row.

**Authorization URL (pinned parameters, server-built):** `client_id`, `redirect_uri` (from configuration), `response_type=code`,
`scope=openid email profile`, `state`, `nonce`, `code_challenge`, `code_challenge_method=S256`, `prompt=select_account`. It never carries
`access_type`, `include_granted_scopes`, `login_hint` or `hd`.

**Callback outcomes.** `Location` is always `/signin?purpose=signin&outcome=<code>` (closed set):

| Outcome | When (first matching rule wins, in this order) | Created |
|---|---|---|
| `rate_limited` | the callback limiter rejected the request (before anything else) | nothing |
| `state_invalid` | the state is missing, unknown, expired or consumed, or the binder is missing or its hash differs (the row is burned) | nothing |
| `access_denied` | `error=access_denied` with a valid state | nothing |
| `provider_unavailable` | any other `error`; a token endpoint failure or timeout, a non-2xx or malformed response, or an unavailable JWKS; any ID-token rule failing; any unexpected exception | nothing |
| `not_invited` | `email_verified` is not `true`, or the normalised email is not on the invite list | nothing |
| `school_account` | the `hd` claim is present and `HostedDomainPolicy=refuse` (checked **after** the invite check, R7) | nothing |
| `account_closed` | a user with this subject has `deleted_at` set (ADR-021) | nothing |
| `signed_in` | otherwise: find the user by subject or create one, refresh email and name, then `SignInAsync` (revokes the session the browser carried, new session row, new cookie [V]) | user (first time) and session |

The return path follows R10. Before calling start, the web stores `{path, at}` in `sessionStorage` (key `tether.signin.return`). On
`signed_in` the sign-in page reads it and clears it. It uses the path only if it is under 10 minutes old and passes a structural check:
one leading `/`, no `//`, no `\`, no control characters, decoded once and re-checked, at most 200 characters, not under `/api`, `/hubs`
or `/ingest`. Otherwise it uses the landing default (`home` gives `/`). The server never accepts a return URL.

**Cookies:**

| Cookie | Name outside Development / in Development | Attributes | Lifetime and value |
|---|---|---|---|
| Session (unchanged [V]) | `__Host-cc.session` / `cc.session` | HttpOnly; Secure (always outside Development); SameSite=Lax; Path=/; no Domain | Browser-session cookie. Sliding 8 h; 14 d absolute from the `sessions` row (ADR-016). SameSite is **Lax**; `API_CONTRACTS.md` is corrected (closes M2C-08). |
| Binder | `__Host-cc.signin` / `cc.signin` | HttpOnly; Secure; SameSite=Lax; Path=/; no Domain | Max-Age=600. Value: 32 random bytes, base64url. Replaced by every start; expired by every callback response. |

**Flow state (R5).** Table `identity.oauth_states`:
- Columns: `state_hash` bytea PK (SHA-256 of a 32-byte random state), `binder_hash` (SHA-256), `purpose`, `landing`, `created_at`,
  `expires_at` (+10 min), `consumed_at`; index on `expires_at`.
- Single use: `UPDATE ... SET consumed_at=now WHERE state_hash=@h AND consumed_at IS NULL AND expires_at>now RETURNING binder_hash, purpose, landing`.
  The binder is then compared in constant time. A mismatch leaves the row consumed.
- Nonce = b64url(HMAC-SHA256(`Derive("oauth-nonce.v1")`, state)). PKCE verifier = b64url(HMAC-SHA256(`Derive("oauth-pkce.v1")`, state)),
  43 characters. Both are new `DerivedKeys.Labels`; nothing secret is stored.
- Each start deletes expired rows. Starts beyond 3,000 live rows answer 429.

**ID token rules:**
- Validated with `JsonWebTokenHandler` (package `Microsoft.IdentityModel.JsonWebTokens`, pinned).
- Algorithm RS256 only. Issuer `https://accounts.google.com` or `accounts.google.com` [A]. Audience = client id; `azp` = client id when present.
- `exp` and `iat` required, 2 min skew, time from the app `TimeProvider`. Nonce compared in constant time.
- `sub` is 1-200 printable ASCII. Granted `scope` must be exactly the three sign-in scopes (Google may return the userinfo URIs [A]).
- JWKS comes from the constant URL `https://www.googleapis.com/oauth2/v3/certs`. It is cached by its cache header (floor 5 min, ceiling
  24 h) and refetched for an unknown `kid` at most once per 5 min.
- Token endpoint is the constant `https://oauth2.googleapis.com/token` (client_secret_post plus `code_verifier`).
- Named `HttpClient` `google-oidc`: no redirects, no proxy, 10 s timeout, 64 KB response cap, no retry, only those two hosts. Its log
  categories are filtered to Warning.
- The access token and ID token are dropped after validation. No Google token is stored or logged; nothing reaches the AI (invariant 3).

**User mapping:**
- `users.external_subject = "google:" + sub`, unique [V index]. The unique index on `email` becomes a plain index (migration).
- Email (lower-cased) and `name` (control and format characters stripped, at most 200 characters, fallback `DisplayNameFrom`) are display
  only and refreshed at every sign-in. New column `hosted_domain_present` (bool null). The domain itself is never stored.
- Dev login looks its user up by `external_subject = "dev:" + email`, no longer by email [V it uses email today].
- Outside Development, the `SessionPolicy` validity query also requires that the subject does not start with `dev:` (same join, both overloads).

**Key ring (R4):**
- `PersistKeysToDbContext` with `IdentityDbContext : IDataProtectionKeyContext` (table `identity.data_protection_keys`).
- Protection: `ProtectKeysWithCertificate(current)` plus `UnprotectKeysWithAnyCertificate(current, previous...)`, both loaded from base64
  PFX (no password) with `X509CertificateLoader`.
- Required outside Development. `DataProtection:KeysPath` is honoured in Development only.
- The certificate is generated by the PO script: RSA 3072, 10 years, in memory, piped to `gh`.
- **Time box:** if loading the certificate and unprotecting on the Linux runner is not proven green within about half a day, stop and tell
  the Lead. The fallback is plain XML plus a written PO risk acceptance (M1-F07 stays open).

## 2. App-to-infra interface
**Configuration (API).** The Bicep sets every value. "Secret" means a Container Apps secret referenced through `secretRef`.

| Config key | Env var | Container Apps secret | Value comes from | Set by |
|---|---|---|---|---|
| (environment) | `ASPNETCORE_ENVIRONMENT` | | literal `Production` (R1) | Bicep (Fable) |
| `ConnectionStrings:Default` | `ConnectionStrings__Default` | `db-connection` | server FQDN, **new database name parameter** (R8, for example `tether_live`), GitHub `TETHER_DB_ADMIN_PASSWORD` | Bicep; password set once by `set-deploy-secrets.ps1` [V] |
| `Security:RootSecret` | `Security__RootSecret` | `root-secret` | GitHub `TETHER_ROOT_SECRET` [V] | existing |
| `Security:AllowedOrigins:0` | `Security__AllowedOrigins__0` | | `webOrigin` [V] | Bicep |
| `AllowedHosts`, `Database:MigrateOnStartup` | as today [V] | | literals | Bicep |
| `Auth:Google:ClientId` | `Auth__Google__ClientId` | (plain value; it is public in the authorization URL) | GitHub `GOOGLE_CLIENT_ID` | PO setup script |
| `Auth:Google:ClientSecret` | `Auth__Google__ClientSecret` | `google-client-secret` | GitHub `GOOGLE_CLIENT_SECRET` (hidden prompt, R12) | PO setup script |
| `Auth:Google:RedirectUri` | `Auth__Google__RedirectUri` | | `'${webOrigin}/api/v1/auth/google/callback'` | Bicep |
| `Auth:Google:Admission` | `Auth__Google__Admission` | | literal `allowlist` | Bicep |
| `Auth:Google:HostedDomainPolicy` | `Auth__Google__HostedDomainPolicy` | | param, default `refuse` (R7, pending the PO) | Bicep |
| `Auth:Invite:Emails` | `Auth__Invite__Emails` | `invite-emails` | GitHub `TETHER_INVITED_EMAILS`; comma-separated, 1-100 entries | `set-invites.ps1` (GitHub **and** Container Apps secret, then an API restart, R3); a deploy re-applies it |
| `DataProtection:Certificate` | `DataProtection__Certificate` | `dp-cert` | GitHub `DP_CERT_PFX` | PO setup script (C1) |
| `DataProtection:PreviousCertificates:0` | `DataProtection__PreviousCertificates__0` | `dp-cert-previous` (only when set) | GitHub `DP_CERT_PFX_PREVIOUS` | PO setup script on rotation |
| `RateLimiting:AuthStart*`, `AuthCallback*` | not set | | defaults in code (below) | (none) |
| `Auth:DevLogin:Enabled` | **never set** in Azure | | `appsettings.json` default `false` [V] | (none) |

Workflow-only GitHub secrets: the existing `AZURE_*`, `EASYAUTH_CLIENT_ID`/`EASYAUTH_CLIENT_SECRET` (needed for `team`) and the optional
`ALLOWED_IP_RANGES`. Preflight fails when any Google, invite or certificate value is missing or malformed, in every access mode (R1).
Values are masked, and the invite list is reported by count.

**Startup refusals (outside Development; each message names the key, never a value):**
- Existing: dev login enabled; `AllowedHosts` set to `*` or empty; RootSecret missing, short or a sample value [V].
- New:
  - Google client id missing or not matching `^[0-9]+-[a-z0-9]+\.apps\.googleusercontent\.com$`; client secret empty.
  - `RedirectUri` not absolute https, path not exactly `/api/v1/auth/google/callback`, with a query or fragment, or its origin not in `AllowedOrigins`.
  - `Admission` not `allowlist`; `HostedDomainPolicy` not `refuse` or `allow`.
  - Invite list empty, over 100 entries, with a malformed entry or with a duplicate.
  - Certificate missing, not PFX, without a private key, or RSA under 2048 bits.

**Startup actions (Production):**
- Revoke every live session of a `dev:` user.
- Revoke every live session of a `google:` user whose stored email is not on the list.
- Log the counts only; `allow` mode logs a warning.

**Limits (R6, defaults in code):**
- Start: a global bucket of 300/min, plus 10/min per binder hash when a binder cookie is present.
- Callback with a binder: 300/min global plus 10/min per binder. Callback without a binder: a separate 30/min bucket.
- Live state rows capped at 3,000.
- Forwarded headers stay untrusted. Run A adds a one-off, content-free log of the `X-Forwarded-For` entry count and whether each entry is
  private or public (shape only, never a value). The Lead rules on trust before anyone but the PO is invited.

**Access modes (workflow input `access`, R2):**

| Mode | Web ingress | Easy Auth | Gate |
|---|---|---|---|
| `team` | public | on | Entra in front of a Production API with Google |
| `invited` | public | off (declared with `platform.enabled=false`) | Google plus the Tether invite list |
| `closed` | internal | unchanged | none needed |

The optional IP layer applies on top of either public mode. Gate-only runs skip the image build and refuse to run unless the live API
image equals this commit's (or `release_sha`). `release_sha` must be at or after the go-live tag.

**Outside smoke probes.** Each probe sends `Origin: <web origin>` where the method is unsafe. A 429 means retry until the deadline, then
fail closed (R11).
- `closed`: `GET /signin` must not answer 2xx.
- `team`: `GET /signin` answers 302 to the Entra login or 401 (as today). Azure-side reads: API env `Production`, no `Auth__DevLogin__Enabled`,
  Easy Auth enabled, live image as expected.
- `invited`: the same Azure-side reads with Easy Auth **disabled**, then from outside, in order:

| # | Probe | Pass | Breach (ingress closes) |
|---|---|---|---|
| 1 | `POST /api/v1/dev/login` with JSON `{"email":"smoke@example.invalid"}` | 404 | any 2xx or 400 |
| 2 | `GET /api/v1/auth/methods` | 200, `{"google":true,"devLogin":false}` | `devLogin:true` |
| 3 | `GET /api/v1/me`; `POST /hubs/lecture/negotiate` | 401 both | 2xx |
| 4 | `POST /api/v1/auth/google/signin/start` with `{"landing":"home"}` | 200; URL starts with `https://accounts.google.com/o/oauth2/v2/auth?`; `redirect_uri` equals the expected value (compared, never printed); `response_type=code`, `code_challenge_method=S256`, `scope=openid email profile`; `Set-Cookie: __Host-cc.signin` with Secure, HttpOnly, SameSite=Lax, Path=/, Max-Age=600 and no Domain | any other URL host |
| 5 | `GET /api/v1/auth/google/callback?state=<43 random chars>&code=x`, with the binder from step 4 | 302, `Location` exactly `/signin?purpose=signin&outcome=state_invalid` (relative); the binder is expired | 2xx; an absolute `Location` is a failure |
| 6 | `GET /signin` | 200 with `Strict-Transport-Security` | (only now is a 2xx correct) |

`gate_verified=true` is written only when all six pass. No probe follows the URL to Google.

## 3. Build split and ownership (no file has two owners)
**Opus:**
- Contract: `docs/API_CONTRACTS_AUTH.md`; `src/shared/contracts/auth.v1/**`; `src/api/tests/Tether.ContractTests/AuthContractTests.cs`; the
  pointer note in `docs/API_CONTRACTS_CALENDAR.md` 5.1; the SameSite line in `docs/API_CONTRACTS.md`.
- API: everything under `src/api/src/Modules/Identity/Tether.Identity/**`:
  - new `Google/` (options, provider, sign-in flow, invite list), `KeyRing/` (certificate loading, DP setup) and `Domain/OAuthState.cs`;
  - changed `Domain/User.cs`, `Persistence/IdentityDbContext.cs` plus migration `M00x_GoogleSignIn` (state table, key table, email index,
    `hosted_domain_present`), `ExternalIdentity.cs`, `Endpoints/IdentityEndpoints.cs`, `IdentityModule.cs`, `SessionPolicy.cs` and the csproj;
  - also `src/api/src/Tether.SharedKernel/Security/RootSecret.cs` (two labels) and `src/api/src/Tether.Host/Program.cs` (limiter policies, log filter).
- API tests: `src/api/tests/Tether.IntegrationTests/` new `GoogleSignIn*Tests.cs`, `FakeGoogle.cs`, `KeyRingTests.cs`,
  `ProductionStartupTests.cs`, plus updates to `LogPipelineCanaryTests.cs` and `Infrastructure/ApiFactory.cs`.
- Web: `src/web/src/components/SignInView.tsx`, `AuthProvider.tsx`, `src/web/src/lib/api/{client,types,index}.ts`,
  `src/web/src/mocks/handlers.ts`, the Google mark asset and the web tests; a Playwright spec for the outcome states and the local proxy 302.

**Fable:**
- `infra/main.bicep`, `infra/poc.bicepparam`, `.github/workflows/deploy-azure-dev.yml`, `infra/scripts/{preflight,smoke,api-signin-mode}.sh`.
- `scripts/azure/set-google-signin.ps1` (client id, hidden-prompt secret, certificate per C1, prints the redirect address) and
  `scripts/azure/set-invites.ps1` (R3: service-principal guard, counts only, list in `%USERPROFILE%\.tether\`).
- `infra/README.md`; the Production variant of the local compose stack; preflight case tests.

**Lead:** `docs/DECISIONS.md`, `docs/PROGRESS.md`, `docs/RELEASE_GATES.md`, `docs/security/findings-register.md`.

Order: (1) freeze `auth.v1`; (2) API and key ring (time box first); (3) web and (4) infra in parallel against section 2; (5) cross-review,
QA, local Production rehearsal; (6) PO steps, then Runs A, B, R, B' (R9). About three senior-days before the PO's part.

**Integration points the Lead checks:**
1. Every env var and secret name in section 2 matches, by grep across `main.bicep` and the API options binding.
2. The redirect address is the same string in Bicep, the API startup check, the smoke probe and the value the PO script prints.
3. The smoke probe expectations (cookie name and attributes, methods JSON, the exact `Location`, dev login 404) match the API tests that
   assert the same values.
4. The certificate format the script writes (base64 PFX, no password, RSA 3072) loads in `KeyRingTests` from a PFX generated the same way.
5. The invite list format (comma-separated) parses identically: the API normalises it (trim, lower case) and the script writes it already normalised.
6. `api-signin-mode.sh` reads exactly `ASPNETCORE_ENVIRONMENT` and the absence of `Auth__DevLogin__Enabled`.
7. Migrations run cleanly on the new empty database (local compose with a fresh database name), and the old database is untouched.
8. The smoke probe's request volume stays under the anonymous, start and callback budgets.

**Tests (no call to Google in CI):**
- `FakeGoogle` replaces only the `google-oidc` primary handler (`ConfigureTestServices`). It checks the client secret, `redirect_uri` and
  S256(verifier), and signs ID tokens with a test RSA key. No configuration key can point the app at another issuer.
- Required:
  - Start-URL parameter set, with a spoofed `Host`/`X-Forwarded-Host` that changes nothing.
  - Origin 403; binder attributes in a Production factory.
  - Happy path: a new session id; the binder cleared.
  - Double or concurrent callback gives one user and one session.
  - Replayed, expired, tampered and foreign-binder state give `state_invalid` with **zero** calls to the fake.
  - `access_denied`; every ID-token failure (iss, aud, azp, exp, nonce, `alg:none`, HS256 signed with the public key, unknown `kid`, bad
    signature, extra scope); `not_invited`; unverified email; `school_account` (after the invite check); `account_closed`. Each creates nothing.
  - Same email with two subjects gives two users; a `dev:` row is untouched.
  - The callback limiter answers 302 `rate_limited`.
  - Every startup refusal; startup revocation.
  - Key ring: a cookie from host 1 works on host 2 over the same database; the stored XML holds no plaintext key; the previous certificate
    still decrypts; an expired certificate's behaviour is pinned.
  - Log canary at Trace over the whole flow: no email, `sub`, code, state, nonce, token, client secret or PFX.
- Web: vitest and axe per state; the `sessionStorage` path checks.

## 4. Rejected alternatives (one line each)
- ASP.NET `AddOpenIdConnect`/`AddGoogle`: redirect address built from request headers (internal host, http); runtime discovery; state in
  Data Protection cookies; cannot do "POST start returns a URL".
- Google Identity Services button or One Tap: the ID token goes to browser JavaScript, plus a third-party script.
- Easy Auth's Google provider: identity would reach the internal API as headers forwarded by the proxy.
- Google's test-user list alone: one console click from open sign-up; untestable; no Tether message; no session revocation.
- An invite table in the database: no admin role or UI, and the PO cannot reach the database.
- Hashed invite emails: guessable, and the secret store is already the trust boundary.
- A server-side return URL or route list: open-redirect surface, and the web's routes duplicated in the API.
- A hand-written AES-GCM key wrapper (no custom crypto). Azure Files for keys (plaintext on a share). Blob plus Key Vault (needs role
  assignments the deploy identity cannot create; later, with Q5).
- A Development branch in the Azure template, or a rollback to a dev-login release (account takeover once Google users exist).
- Trusting `X-Forwarded-For` now (a wrong hop count lets clients choose their own bucket).
- Purging the old dev rows by SQL (it would run ahead of ADR-021's erasure; a new database avoids it).

## 5. Residual risks
1. **Sign-in denial of service.** Until forwarded-header trust is decided, one client can flood the global start bucket and stall *new*
   sign-ins (signed-in users are unaffected). S1.16 is only partly met; the Lead decides before inviting others.
2. **Certificate on Linux** [A]: if the time box fails, plain XML with a PO risk acceptance (M1-F07 MEDIUM, PO-13).
3. **Contributor access exposes every secret** [V main.bicep header]: root secret, client secret, certificate and the database password
   alike. The certificate protects only against database-only leaks.
4. **An absent `hd` is treated as personal**, which fails open. The invite list is the backstop. T-hd is not run; the PO has not
   confirmed R7.
5. **Two invite lists to keep in step.** Google's test users stay a manual console step [A no supported API]. Erasure cannot remove an
   invite held outside the database (ADR-021 question).
6. **`set-invites.ps1` restarts the API.** Live hub and audio connections drop briefly, and a lecture in progress pauses itself [V README].
   The script warns first.
7. **Platform behaviour proven only in Azure:** Easy Auth turned off in place, a 302 through the proxy behind Easy Auth, the Google screens
   test users see, and the exact `scope` and `iss` strings [A]. Run A and the smoke probes 4 and 5 cover these.
8. **The new database starts empty.** The PO's earlier test courses are not visible after Run A; the PO is told before Run A.
