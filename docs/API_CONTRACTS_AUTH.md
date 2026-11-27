# API contract auth.v1: invite-only Google sign-in

**Status: FROZEN 2026-11-27 for the go-live build (ADR-022, `docs/architecture/go-live-signin-adr.md`).** Schemas and fixtures:
`src/shared/contracts/auth.v1/`. Where the schemas and this document disagree, this document wins and the schema is a bug.
Additive changes (a new `purpose` such as `reauth` for ADR-021 or `connect` for PO-9d, a new `landing` key) change this document
and the schemas first. This contract replaces the sign-in part of the draft `calendar.v1` section 5.1; `connect` stays there.

## 1. Conventions

- Base path `/api/v1`. Errors are the existing ProblemDetails with a stable `code` and the fixed `detail` for that code.
- Unknown or duplicate JSON members are `400 validation_failed`. JSON bodies need `Content-Type: application/json` (`415` otherwise).
- Unsafe methods need an allow-listed `Origin` (`403 csrf_failed`), as everywhere in the API.
- No response body and no `Location` ever carries an email address, a Google subject, an authorization code, a state, a nonce, a
  token or Google error text.
- Every API response already carries `Cache-Control: no-store` and `Referrer-Policy: no-referrer`.

## 2. Routes

| Route | Auth and checks | Request | Success | Errors |
|---|---|---|---|---|
| `GET /auth/methods` | anonymous | none | 200 `auth-methods-response` `{"google": bool, "devLogin": bool}` | 429 `rate_limited` |
| `POST /auth/google/signin/start` | anonymous; Origin; `auth-start` limits | `auth-signin-start-request` `{"landing":"home"}` | 200 `auth-start-response` `{"purpose":"signin","authorizationUrl","expiresAt"}` and the binder cookie | 400 `validation_failed`; 403 `csrf_failed`; 415; 429 `rate_limited` (also when 3,000 live sign-in states exist); 404 when Google is not configured (Development only: outside Development the API does not start without it) |
| `GET /auth/google/callback` | state plus binder cookie; `auth-callback` limits | query `state`, `code`, `error` (all else ignored; `error_description` never read) | always `302`, relative `Location: /signin?purpose=signin&outcome=<code>`, binder cookie expired | another method: `401 unauthenticated` for an anonymous caller (the route's 405 endpoint has no anonymous access, so the fallback policy answers first), `405 method_not_allowed` for a signed-in one |
| `POST /auth/logout` | unchanged | | 204 | |
| `GET /me` | session | | 200 `{id, email, displayName}` | 401 `unauthenticated` |
| `POST /dev/login` | mapped only in Development with `Auth:DevLogin:Enabled=true` | `{email}` | 204 | not mapped everywhere else: an anonymous request gets 401 `unauthenticated` from the fallback policy (an authenticated one gets 404) |

`devLogin` in `auth/methods` is true exactly when `/dev/login` is mapped. `google` is true exactly when the two Google routes are mapped.

## 3. Start

The server builds the authorization URL. Host and path are fixed: `https://accounts.google.com/o/oauth2/v2/auth`. The pinned parameter
set is exactly:

`client_id`, `redirect_uri` (the configured `Auth:Google:RedirectUri`), `response_type=code`, `scope=openid email profile`, `state`,
`nonce`, `code_challenge`, `code_challenge_method=S256`, `prompt=select_account`.

Never sent: `access_type`, `include_granted_scopes`, `login_hint`, `hd`. No request header (`Host`, `X-Forwarded-*`) influences any of them.

- `state`: 32 random bytes, base64url without padding (43 characters).
- `nonce`: base64url of HMAC-SHA256 under the derived key `oauth-nonce.v1` over the state.
- PKCE verifier: base64url of HMAC-SHA256 under the derived key `oauth-pkce.v1` over the state (43 characters). The challenge is S256 of it.
- Nonce and verifier are derived, never stored, and the verifier never leaves the server.
- Stored row `identity.oauth_states`: SHA-256 of the state, SHA-256 of the binder value, `purpose`, `landing`, `created_at`,
  `expires_at` (+10 minutes), `consumed_at`.
- `expiresAt` in the response equals `expires_at`. Each start deletes expired rows.

## 4. Callback outcomes

`Location` is always the relative `/signin?purpose=signin&outcome=<code>`. The first matching row wins:

| Outcome | When | Created |
|---|---|---|
| `rate_limited` | The request's budget is spent (section 8): a VALID return (live state, matching binder) over the valid budget, or any other callback over the small invalid budget. A valid return refused this way leaves its state live, so a retry can still succeed. | nothing |
| `state_invalid` | The state is missing, unknown, expired or already consumed, or the binder cookie is missing or its hash differs. A live state with a missing or different binder is consumed (burned). These requests spend the invalid budget. | nothing |
| `access_denied` | `error=access_denied` with a valid state (the person pressed Cancel at Google). | nothing |
| `provider_unavailable` | Any other `error` value. Also: the token endpoint failed, timed out, returned a non-2xx or malformed response, or no ID token; the JWKS is unavailable; any ID-token rule (section 5) failed; any unexpected exception. | nothing |
| `not_invited` | `email_verified` is not `true`, or the trimmed, lower-cased email is not on the invite list. | nothing |
| `school_account` | The `hd` claim is present and `Auth:Google:HostedDomainPolicy=refuse` (the default). Checked after the invite check. | nothing |
| `account_closed` | The user with this subject has `deleted_at` set (ADR-021). | nothing |
| `signed_in` | Otherwise. Find the user by subject, or create one; refresh the email and display name; revoke the session the browser carried; create a new session row and cookie. The user and session writes are one transaction, so no other outcome leaves a row behind. | user (first time), session |

The access token and ID token are dropped once validated; no Google token is stored or logged.

The return path stays in the browser. Before calling start, the web stores `{path, at}` in `sessionStorage` under
`tether.signin.return`. On `signed_in` the sign-in page reads and clears it, and navigates to it only if all of these hold:
- it is under 10 minutes old;
- it starts with exactly one `/`, with no `//` and no `\`;
- it has no control characters;
- decoded once, it still passes these checks;
- it is at most 200 characters;
- it is not under `/api`, `/hubs` or `/ingest`.

Otherwise the page uses the landing default (`home` is `/`).

## 5. ID token rules

- RS256 only. Keys come from `https://www.googleapis.com/oauth2/v3/certs`, cached by its cache header (floor 5 minutes, ceiling 24
  hours) and refetched for an unknown `kid` at most once per 5 minutes. When a refetch fails after the cache expired, the old keys
  are used for at most 24 hours past that expiry; after it, sign-in answers `provider_unavailable` until the key endpoint answers.
  A failed refetch is retried at most once a minute.
- `iss` is `https://accounts.google.com` or `accounts.google.com`. `aud` is the client id. `azp`, when present, is the client id.
- `exp` and `iat` are required, with a 2-minute skew. `nonce` equals the derived nonce (constant-time compare).
- `sub` is 1 to 200 printable ASCII characters.
- The granted `scope` in the token response, when present, is exactly the sign-in set: `openid`, plus `email` or
  `https://www.googleapis.com/auth/userinfo.email`, plus `profile` or `https://www.googleapis.com/auth/userinfo.profile`; anything more
  or less is `provider_unavailable`. An absent `scope` member is accepted (decided 2026-11-27): Tether uses only the validated ID
  token's claims and drops the access token unread, so a grant it cannot see is never used.
- The token request goes to `https://oauth2.googleapis.com/token`: `grant_type=authorization_code`, `code`, the configured
  `redirect_uri`, `client_id`, `client_secret` and `code_verifier`.

## 6. Accounts

- Key: `users.external_subject = "google:" + sub` (unique). Email and display name are for display only, refreshed at every sign-in.
- The same email with a different `sub` is a different user. Accounts are never linked by email.
- `hosted_domain_present` records whether `hd` was present; the domain itself is never stored.
- `sessions.created_at` is the sign-in time, written once per sign-in and never changed by the sliding refresh. ADR-021's
  recent-sign-in check reads it.
- Outside Development, sessions of `dev:` users are not valid.

## 7. Cookies

| Cookie | Name outside Development / in Development | Attributes | Lifetime and value |
|---|---|---|---|
| Session | `__Host-cc.session` / `cc.session` | HttpOnly; Secure (always outside Development); SameSite=Lax; Path=/; no Domain | Browser-session cookie. Sliding 8 hours; 14-day absolute limit enforced by the `sessions` row (ADR-016). |
| Binder | `__Host-cc.signin` / `cc.signin` | HttpOnly; Secure; SameSite=Lax; Path=/; no Domain | `Max-Age=600`. Value: 32 random bytes, base64url. Replaced by every start; expired by every callback response. |

SameSite is **Lax**, not Strict: the return from Google is a cross-site top-level GET, and Lax is what lets the binder and session
cookies travel on it.

## 8. Limits (defaults in code, configurable under `RateLimiting:`)

| Route | Buckets |
|---|---|
| start | global 300/min (`AuthStartPermitPerMinute`), plus 10/min per binder hash when the request carries a binder cookie (`AuthStartPerBinderPermitPerMinute`). The global permit is taken first, so per-binder partitions are bounded by it. |
| callback, valid (the state row is live and the binder cookie matches it) | global 300/min (`AuthCallbackPermitPerMinute`), plus 10/min per binder (`AuthCallbackPerBinderPermitPerMinute`) |
| callback, anything else (bad or unknown state, missing or mismatched binder) | its own 30/min bucket (`AuthCallbackInvalidPermitPerMinute`). A forged cookie value cannot reach the valid budget, so a flood of forged callbacks never starves real returns. Telling the two apart costs one primary-key read, before any budget is spent. |
| state rows | at most 3,000 live rows (`Auth:Google:MaxLiveStates`); start answers 429 above it |

Forwarded headers are not trusted (ADR-022 R6), so in Azure "global" means every visitor shares the bucket. For the Lead's ruling on
which hop to trust, the API logs the SHAPE of the chain outside Development (`Diagnostics:ForwardedChainLog:Enabled`, default on): the
number of `X-Forwarded-For` entries and, per entry, private or public and IPv4 or IPv6, plus the same for the connecting peer. That is
logged for the first 20 requests after start (health probes excluded), then at most once an hour. It never logs an address. Rejected starts are
`429 rate_limited` with `Retry-After`. Rejected callbacks are the `302 rate_limited` outcome.
