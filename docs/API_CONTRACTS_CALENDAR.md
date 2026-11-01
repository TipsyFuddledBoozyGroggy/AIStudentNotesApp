# API Contracts - Phase 2: Schedule / Calendar and Google sign-in (`calendar.v1`)

**Status: DRAFT, NOT frozen.** Authored 2026-10-28; revised 2026-10-29 for the security review `docs/security/calendar-v1-contract-review.md` (CV-01 to CV-28, S1 to S13), PO-9o and PO-10a to PO-10q. Nothing here is implemented: there is no Calendar module, endpoint, migration, OAuth client or Google call. The artifacts are this file, `src/shared/contracts/calendar.v1/` (26 schemas, `calendar-policy.json`, 70 valid and 138 invalid fixtures) and `src/api/tests/Tether.ContractTests/CalendarContractTests.cs`. They are verified only by schema, fixture and policy-file contract tests (section 17). Everything is **mock / fake Google only**; no section authorizes a real Google call, a real student account, or any gate change (G1 to G5 stay as recorded in `docs/RELEASE_GATES.md`).

`syllabus.v1` (frozen, mock-only; PO-10c) and `lecture.v1` are **not modified**. The frozen `calendar-sync` routes and the mock `syllabus.calendar_events` table are deprecated and stay mock-only until this contract is implemented, then are removed (PO-10g).

Values marked **PROPOSAL** are defaults pending devops or the Product Owner (PO-10h retention numbers, PO-10q quota numbers, the 400-day limit of PO-9o). They are configurable and not measured.

Inputs: `CLAUDE.md` invariants 3, 4, 8, 10; `docs/DECISIONS.md` PO-9 to PO-9o and PO-10a to PO-10q; `docs/architecture/google-calendar-integration.md`; `docs/security/google-tokens-and-data-residency.md`; `docs/security/calendar-v1-contract-review.md`; `docs/ux/calendar-reminders.md`; `docs/vendors/google-verification-report.md` (Google facts, fetched 2026-10-28, summarised-fetch evidence, re-open before quoting); `docs/security/p2-b1-review.md`; `API_CONTRACTS_P2.md` conventions.

Dependent teams: backend-engineer (Calendar module, plan/execute/record), database-architect (tables, section 12), frontend-engineer and neurodivergent-ux-specialist (preview, connect, settings, delete and disconnect dialogs), security-engineer (section 20), qa-engineer (fake Google server, section 17), devops-engineer (key custody, spend guard, limits). The Lead Architect owns freeze.

## 1. Decisions that bind this contract

| ID | Decision | Where it shows up |
|---|---|---|
| PO-9d | Google sign-in is the identity; the data-connect account must have the same `sub`. | sections 3, 5 |
| PO-9h | Scope `calendar.app.created` only. The app sees only events it created; **no conflict detection**. | sections 4, 11 |
| PO-9i | The student chooses the destination per account: Google Calendar (**one Tether-created calendar**, no per-course calendars, course in the title, for example `BIO 201 · Midterm exam`) **or** Tether's own calendar (per-course view plus one combined view). | sections 6, 8 |
| PO-9a, PO-10a | A deleted Tether event is left alone and Tether will **never recreate** it automatically. An event the student edited in Google is preserved: Tether never overwrites or removes it. | sections 9.3, 10 |
| PO-9l | Reminders on by default, pre-selected Gentle preset. Not human-validated (G4). | section 8.3 |
| PO-9m | Postgres is the working record; Google is the student's durable copy. | section 12 |
| PO-9n, PO-10n | A closed or suspended account: sign-in fails, tokens deleted, existing Tether sessions end at their normal absolute lifetime, Tether-side data follows the retention policy, no password fallback. Purge timing needs PO/legal. | section 5.3 |
| PO-9o | Option C: when entries are detached (syllabus deleted, or Google disconnected with "keep events") Tether deletes all content (titles, dates, course labels) immediately and keeps only a **content-free per-event record**: event id plus generation counter. The record is deleted when the student disconnects Google or deletes their account, with an upper limit of 400 days (PROPOSAL). | sections 9.3, 12, 15 |
| PO-10b | No added type word: title is `<course> · <item title>`. | section 8.1 |
| PO-10c | No `quiz` or `class_meeting` kind; quizzes use the assignment reminder timing. | section 8.3 |
| PO-10d | Switching destination moves nothing automatically; moving old entries is a separate confirmed run (reserved, not in this revision). | section 6 |
| PO-10e, PO-10o | A deleted event is re-added only by a deliberate, previewed student action; "let Tether manage this again" likewise (resume run). | section 8.6 |
| PO-10f, PO-10i | Reminder time is a whole hour 08:00 to 20:00; pause has manual resume only. | section 8.3 |
| PO-10j | Workspace accounts are allowed; a plain warning at connect; handle `admin_blocked` gracefully (G1 legal review pending). | section 5.2 |
| PO-10l | Transcript upload to Google is a per-session opt-in; knowledge estimates are not mirrored. Outside this contract (Docs/Drive). | section 22 |
| PO-10m | Deleting in Tether keeps Google copies by default and offers an explicit "also remove from Google" choice while the grant is valid; dialogs must say so. | sections 5.4, 15 |
| PO-10p | CSRF header name `X-Tether-CSRF`. | section 2 |
| PO-10q | Per-user daily Google budget plus a global soft limit well below the charged threshold; numbers PROPOSAL pending devops/PO. | section 14 |
| Invariants | Uncertain dates never produce events or reminders (server-enforced). Only student-approved items are written after an explicit preview and confirm. Idempotent. **AI never** sees tokens (invariant 3). | sections 8, 16 |

## 2. Conventions (inherited from `API_CONTRACTS_P2.md` section 2, restated where they matter)

- Base path `/api/v1`. Session cookie (`SameSite=Lax`, which is what lets the cross-site callback carry it; a test pins it) plus exact-match `Origin` check **and** the antiforgery header `X-Tether-CSRF` on every state-changing route except the OAuth callback (section 5.1) and, for the unauthenticated `signin/start`, Origin plus the binder cookie. JSON routes require `Content-Type: application/json` (`415` otherwise). A foreign or missing id is `404 not_found`, never 403. Errors are RFC 9457 ProblemDetails with a stable `code` (`calendar-problem.schema.json`), a fixed `detail` per code, never Google error text, never an email address.
- **Unknown or duplicate JSON members are `400 validation_failed`.** The server must bind with `JsonSerializerOptions.AllowDuplicateProperties = false` plus `UnmappedMemberHandling = Disallow` on the options actually used for typed binding. Read-only fields in a request (`state`, `etag`, `destination` on a preview, `provider`, any token) are `400`.
- **Evaluation order**: authentication `401`, Origin and CSRF header `403 csrf_failed`, syntax (`Idempotency-Key` well formed, `If-Match` present, canonical form of every id) `400/428`, ownership chain `404`, **idempotency lookup** by (user, method, route template, key), then rate limits `429`, then state preconditions (`destination_not_chosen`, `connection_required`, `run_in_progress`) `409`, body validation `400`, then plan comparison (`plan_changed`) `409`. A caller never sees `409`/`429` caused by someone else's data.
- **Id-bearing inputs (CV-16)**: a foreign id and a nonexistent id give the identical status and body. `PUT /schedule/settings` answers any unknown `courseOverrides` key with `400 validation_failed` and a fixed detail (not `404`). Per-item ids inside a valid syllabus give the per-item `not_found`. A `cursor` carries keyset values only (never a row lookup by id), is HMAC-bound to (user, filter, route), and a forged or foreign cursor is `400`.
- **Dates the student reads are wall-clock**: `YYYY-MM-DD`, `HH:MM`, IANA zone id. No UTC and no offsets except instants. UTC conversion happens only inside the Google adapter at the write. All-day `end.date` is **inclusive** on the wire; the adapter sends Google's exclusive end (+1 day).
- Ids, `itemKey`, timestamps, local date/time/zone, `title`, `textLine`, `flag`, `timezoneSource`, `syncState`, `idempotencyKey`, `noticeVersion` are **byte-identical copies** of the frozen `syllabus.v1` definitions (a test fails if they diverge). New id prefixes: `ent_`, `run_`.
- Weak `ETag` plus `If-Match` on `PUT /schedule/settings` (`428 precondition_required`, `412 precondition_failed`); the tag is an HMAC over kind, id, row version and restore epoch, compared in constant time. Idempotency fingerprints use an HMAC, never a plain hash.
- Lists are `{items, nextCursor}` with `?cursor&limit`, default 50, maximum 200.

## 3. Resource model

| Resource | Meaning | Schema |
|---|---|---|
| Connection | The one Google data connection (status, masked account, granted scopes, calendar state, counts, last revocation result). Never a token, the Google `sub`, a calendar id or a remote event id. | `calendar-google-connection` |
| Settings | Destination (`unset` until chosen), reminders, how many events remain in Google. | `calendar-settings` |
| Entry | One event on the student's schedule (one per item occurrence) on exactly one destination. | `calendar-entry` |
| Preview | The exact plan a write would execute. Read-only. | `calendar-preview` |
| Action preview | The plan for a removal, reminder update, disconnect or resume. Read-only. | `calendar-action-preview` |
| Run | A write, removal, reminder-update or resume; asynchronous; has a state machine (section 9.2). | `calendar-run` |

## 4. OAuth scope matrix and consent flow

| Request | Scope string(s) | Purpose | Notes |
|---|---|---|---|
| Sign-in | `openid email profile` | Identity. Key is `iss` + `sub`, never the email. `hd` is captured here for the Workspace warning. | No data scope at sign-in. `prompt=select_account`. |
| Connect calendar | `openid https://www.googleapis.com/auth/calendar.app.created` | Create the one Tether calendar and manage events on it. | `openid` is included so the ID token's `sub` can be compared with the signed-in `sub`. **`include_granted_scopes` is not sent** (CV-10): the returned `scope` is then exactly the requested set and the allowlist check is exact. `access_type=offline`; `prompt=select_account consent` (a refresh token is returned only on first grant, and the account must be chosen, not auto-picked: CV-04); `login_hint` is the stored `sub`, not the email (CV-27). |
| Anything else | not allowed | | `calendar`, `calendar.events*`, `*.readonly`, `drive*`, `documents`: rejected by an allowlist test and by the callback (`scope_unexpected`). Docs/Drive get their own contract. |

Consent flow: (1) sign in with Google; (2) the student chooses a destination (`PUT /schedule/settings`); (3) choosing Google shows the plain-language pre-screen (what Tether creates, that Google becomes a recipient, that a Workspace/institution account is visible to the institution, that nothing is written until they confirm, that events stay in Google if they disconnect, and that the next sign-in will ask for consent again), whose version the client sends as `noticeVersion` to `POST connect/start`; (4) Google's consent screen; (5) callback redirects back with an outcome code; (6) nothing is written yet. `access_denied` is a calm outcome ("Nothing was changed"). Partial grants map to `scope_missing`. Copy belongs to neurodivergent-ux-specialist; the notice text is DRAFT until legal-compliance-reviewer reviews it.

Google behaviours this relies on: scope accepts calendar and event CRUD on app-created calendars (VERIFIED, vendor report 1.1 to 1.3); that the app cannot read the primary calendar (NOT FOUND in docs, test T2); sensitivity class of the scope (NOT FOUND, read the Cloud Console picker); whether an `openid`-only ID token carries `hd` (NOT FOUND, hence capture at sign-in); the exact admin-block error code (NOT FOUND, T9).

## 5. Google sign-in and data-connect API surface

All routes are under `/api/v1`.

| Route | Auth | Body / result | Rate-limit class |
|---|---|---|---|
| `POST /api/v1/auth/google/signin/start` | none, Origin plus binder | `calendar-google-signin-start-request` to `calendar-google-auth-start-response` (200) | `auth_start` |
| `POST /api/v1/auth/google/connect/start` | session | `calendar-google-connect-start-request` (carries `noticeVersion`) to `calendar-google-auth-start-response` (200) | `auth_start` |
| `GET /api/v1/auth/google/callback` | state-bound (5.1) | 302 to an allowlisted route with `?purpose=&outcome=` (`calendar-google-callback-outcome`) | `auth_callback` |
| `GET /api/v1/integrations/google/connection` | session | `calendar-google-connection` (200, ETag) | `schedule_read` |
| `POST /api/v1/integrations/google/connection/disconnect-preview` | session | `calendar-google-disconnect-preview-request` to `calendar-action-preview` kind `disconnect` (200) | `schedule_preview` |
| `POST /api/v1/integrations/google/connection/disconnect` | session, `Idempotency-Key` | `calendar-google-disconnect-request` to `calendar-google-connection` (202, status `disconnecting`) | `schedule_write` |

### 5.1 Start and callback (normative)

- Start is **POST**, never a GET link. The server builds the URL: fixed host and path, exact-match registered redirect URI, `response_type=code`, **PKCE `code_challenge_method=S256` always sent explicitly** (Google defaults to `plain`), 256-bit `state`, `nonce`. The client sends only a `landing` route key (plus `product: calendar` and `noticeVersion` on connect); it can never name scopes, a redirect URI or a client id. The pinned parameter set: `client_id, redirect_uri, response_type, scope, state, nonce, code_challenge, code_challenge_method, access_type, prompt, login_hint`.
- `state` is stored hashed, single use, expires in 10 minutes, bound to purpose, requested scopes, `noticeVersion` and either the session id (connect) or, for sign-in, a **binder cookie** (S2): a `__Host-` name distinct from the session cookie, `HttpOnly; Secure; SameSite=Lax; Path=/; Max-Age=600`, 256-bit random value of which only the hash is stored, set on every start response and replaced by the newest start (an older tab gets the calm `state_invalid`), required even when a session cookie exists, cleared on callback success and failure, constant-time compare. PKCE verifier is held server-side encrypted.
- **The callback (S3)**: `GET` only (`POST`/`OPTIONS` are `405`); exempt from the Origin check by exact route; it performs no state change before state and binder/session validation; the response is a 302 with `Cache-Control: no-store` and `Referrer-Policy: no-referrer`; a Google `error=` is read only after state is valid and mapped to the closed outcome set, `error_description` is ignored; `Sec-Fetch-Dest: document` and `Sec-Fetch-Mode: navigate` are required where the headers are present (defence in depth, not authority). `code`, `state`, `error_description` and `id_token` are redacted in proxy logs, Kestrel/HttpLogging and telemetry URL attributes (the query is dropped on this route).
- The callback verifies state, binder or session, expiry and single use **before any token exchange**; any failure is the generic `state_invalid`. It then exchanges the code, verifies the ID token (`iss`, `aud`, `exp`, RS256 via pinned JWKS, `nonce`), and compares the token's `scope` with the exact requested set.
- **Sign-in**: find or create the user by `iss` + `sub` only, issue a **new session id**, clear the binder cookie, store `hd` presence on the identity record. `signed_in`.
- **Connect**: the ID token `sub` must equal the session user's stored `sub` (read from the database, never the client). **Mismatch (CV-04)**: store nothing and drop the tokens from memory. Tether **does not call revoke** when the returned `sub` already belongs to another Tether user or to this user's other identity, because revoking would destroy that user's grant (shared lab computer). Revoke is called only when the `sub` is unknown to Tether. The mismatching `sub` and email are never logged. Outcome `account_mismatch`. `scope_missing` and `scope_unexpected` (CV-10): **store nothing**, drop the tokens from memory, revoke only under the same rule. Success stores the refresh token (envelope-encrypted, AAD bound to user, connection and `sub`), records `noticeVersion` as the grant's consent text version with a content-free audit event and the **server** receipt time, outcome `connected`.
- **No Google write happens at callback time.** The post-callback target is a server-side allowlist keyed by `landing`.
- Egress: host allowlist (`accounts.google.com`, `oauth2.googleapis.com`, `www.googleapis.com`), `AllowAutoRedirect = false`, no proxy from the environment, no runtime discovery fetch (authorize, token, revoke and JWKS URLs are pinned), JWKS refresh by `kid` is rate-limited, `Authorization` redacted at the handler (CV-27).

### 5.2 Account rules

- One Google account per user and one Tether user per `sub` (unique index). The data account must equal the signed-in account. A different account is a different Tether identity; accounts are never merged. Email is display only; `email_verified` is not used for linking.
- Workspace (institution-managed) accounts are **allowed** (PO-10j). `workspaceAccount` comes from the `hd` claim captured at sign-in (an `openid`-only connect token may lack it: CV-22). The UI shows a plain warning at connect that the institution can see and manage Google content. `admin_blocked` is a callback outcome from the consent-time `error=` value only, and is unreachable in practice until test T9 verifies the value; tests must not claim otherwise.
- `needs_reauth` is resolved by the **connect** flow again, not by sign-in alone (sign-in has no data scope).

### 5.3 Closed, suspended, revoked and blocked accounts

Google sends no push signal and at refresh does not distinguish a deleted account from a revoked grant (`invalid_grant`).
- **Classification is closed and machine-readable** in `calendar-policy.json` `googleErrorMap` (CAL-24). A token is deleted **only** on `invalid_grant` from the refresh endpoint **confirmed by one retry after a short delay** (no loop). `invalid_client`, `unauthorized_client`, `accessNotConfigured`, `SERVICE_DISABLED`, quota or billing reasons, 5xx, decrypt failures and **any unrecognised 403 reason** never delete a token; they fail the run with `provider_unavailable` or `remote_rejected` and leave the connection unchanged.
- **Fleet circuit breaker (PROPOSAL)**: if more than a configured share of at least N connections hit a terminal error inside 10 minutes, Tether stops deleting tokens, alerts, and requires operator action. This stops one operational mistake from forcing a mass reconnect.
- On a confirmed `invalid_grant`: ciphertext deleted, connection `needs_reauth`, no further Google call, non-terminal runs `failed`; `scheduled` entries stay.
- A closed or suspended account cannot complete Google sign-in. Existing sessions end at their normal absolute lifetime (PO-10n, ADR-016) because Tether learns lazily and `invalid_grant` cannot tell closure from a benign revoke; support can deactivate a user. Tether-side data follows the retention policy; purge timing and any non-Google recovery path are open (Q4).
- Testing-status clients expire refresh tokens after 7 days; the fake must reproduce `invalid_grant` so the same path is exercised.

### 5.4 Disconnect (student-initiated)

`POST disconnect-preview` (read-only) returns the plan; `POST disconnect` then carries `eventsAction`, and for `remove_events` the `entryCount` and `previewDigest` of that plan (CV-08). The disconnect dialog must state that **events remain in Google** unless the student chooses `remove_events`, that Tether cannot remove events it no longer holds content for, and that the next sign-in will ask for consent again (revocation removes all scopes granted to the project, including the sign-in grant).
1. `keep` (default; PO-10m): scheduled entries are **hard-deleted** (all content), leaving only the content-free per-event records, which are themselves deleted at disconnect (PO-9o). The Google copies are the student's.
2. `remove_events`: Tether deletes the events it wrote and the student has not edited (`If-Match` on delete). Edited events are never deleted (PO-10a). Content-free records cannot be checked for edits, so Tether does not delete those events (section 21, Q14).

Order: removal runs **before** revocation, then revocation at `https://oauth2.googleapis.com/revoke`, then the ciphertext row and content-free records are deleted **regardless of the revocation result**, then `not_connected`. The 202 body reports `revocation: confirmed | unconfirmed` (`unconfirmed`: copy "remove Tether in your Google account settings"). A replayed disconnect `Idempotency-Key` returns the **current** connection resource with `Idempotency-Replayed: true`, and the UI uses a fresh key per click (CV-19).

**Exits from `disconnecting` (CV-02)**: terminal token failure falls through to `not_connected` (remaining entries hard-deleted, count shown, copy "we can no longer remove them; delete them in Google"); the student can choose `keep` at any time; a 24 h deadline falls through to `not_connected`. **Account deletion never blocks on Google**: one bounded removal pass (about 5 minutes, PROPOSAL), then the token row, schedule rows, content-free records and the user are deleted regardless.

## 6. Destination and settings

`GET /api/v1/schedule/settings` returns `calendar-settings` with an `ETag`. `PUT /api/v1/schedule/settings` replaces it (`If-Match` required). `destination` is `unset` until the student chooses and never returns to `unset`. The response includes `eventsInGoogle` so the UI can say how many events remain in Google.

- **`tether`**: entries live only in Postgres, shown in per-course and combined views. No Google, no reminders.
- **`google`**: entries are mirrored as events on **one Tether-created calendar** named `Tether`; the connection must be `connected`, else `409 connection_required`.
- **Switching destination moves nothing automatically (PO-10d, CV-20).** New approved items go to the new destination; entries keep their own `destination`. Removal, resume and disconnect act on entries by their own destination regardless of the current setting; reminder-updates act on Google entries only and work while connected even when the current destination is `tether`. A `PUT` that changes the destination while a run is active is `409 run_in_progress`. Moving old entries is a separate confirmed run, reserved for a later revision.
- **Saving settings never writes to Google.** Existing Google events change reminders only through a confirmed `reminder-updates` run.
- Data written to Google, stated in the pre-screen: the composed title, local date/time/zone, popup reminders, a fixed description and opaque markers.

## 7. Error model

| HTTP | `code` | When |
|---|---|---|
| 400 | `validation_failed` | syntax, unknown/duplicate member, non-canonical id, unknown course key, forged cursor, bad `Idempotency-Key`, key reuse with a different fingerprint |
| 401 | `unauthenticated` | no session |
| 403 | `csrf_failed` | Origin or `X-Tether-CSRF` missing or wrong |
| 404 | `not_found` | foreign or missing id (identical body and status) |
| 409 | `invalid_state` | the operation does not apply in the current state |
| 409 | `concurrency_conflict` | lost a race inside the transaction |
| 409 | `connection_required` | destination `google` but the connection is not `connected`; `remove_events` without a token |
| 409 | `destination_not_chosen` | destination is `unset` |
| 409 | `plan_changed` | the recomputed plan differs from `previewDigest`, the confirmed `entryCount` or the restated reminder basis |
| 409 | `run_in_progress` | the user already has a run in `planned` or `executing`; or a Syllabus change to an item with an active intent (8.2) |
| 412 / 428 | `precondition_failed` / `precondition_required` | `If-Match` on settings |
| 429 | `rate_limited` | a Tether bucket or the per-user daily budget (section 14); `Retry-After` and `retryAfterSeconds` |
| 503 | `provider_unavailable` | Google unreachable on a synchronous read |

Per-item codes inside run results (`entryErrorCode`): `item_needs_date`, `item_not_syncable`, `item_not_approved`, `precondition_failed`, `not_found`, `provider_unavailable`, `invalid_state`, `event_limit_reached`, `connection_required`, `provider_rate_limited`, `remote_rejected`, `remote_conflict`, `left_alone_edited`, `left_alone_deleted`. `left_alone_*` accompany only the outcome `left_alone`.

Error hygiene: no Google message, exception text, scope list, email or id is ever in a `detail` or `errors` value. Raw Google signals map to the closed vocabulary through section 13.3.

## 8. Schedule endpoints

### 8.1 Reading

| Route | Result |
|---|---|
| `GET /api/v1/schedule/entries` | `calendar-entry-page`. Query `courseId, from, to, destination, state, cursor, limit`. No `courseId` is the combined view. |
| `GET /api/v1/schedule/entries/{entryId}` | `calendar-entry` with `ETag` |
| `GET /api/v1/schedule/export` | `calendar-export`, `attachment`, `no-store`, `nosniff`, class `schedule_export` |

An entry exposes the composed `title` (`<course label> · <item title>`, no added type word: PO-10b), local date/time **with the IANA timezone**, `timezoneSource`, uncertainty `flags`, `endDerived`, `destination`, `state`, `syncedRevision`, `reminders`, and for Google `google.generation`. It never exposes the Google event id, calendar id, markers or fingerprints. `start.date` is required and non-null: **an item without a resolved date cannot be an entry.** The composed title is re-checked with TextPolicy at the adapter boundary (reject, never truncate silently; grapheme-safe truncation to 200 code points is shown in the preview), because the course label comes from another module.

### 8.2 Preview then confirm

`POST /api/v1/schedule/previews` (`calendar-preview-request`, read-only, no Google call, class `schedule_preview`) returns `calendar-preview`: `entries[]` (title, local date and time **and timezone**, all-day flag, `endDerived`, `timezoneSource`, syllabus `flags`, `action`, reminders with local `fireAt`, `skippedReminders`), `excluded[]` (`needs_date`, `not_approved`, `policy`, `revision_changed`, `not_found`, `event_limit_reached`; **uncertain dates appear here, never as entries**), `leftAlone[]`, `target` (`{kind: tether}` or `{kind: google, accountEmailMasked, calendarName: "Tether", calendarState}`; `missing_in_google` means a new calendar would be created, stated on the confirm screen), `reminders`, `counts` and `previewDigest`.

**`previewDigest` specification (CV-12)**: HMAC-SHA-256 with a key derived by HKDF from the root secret, label `calendar-preview-digest.v1`, current plus previous key (ring). Input is a **length-prefixed UTF-8** encoding of the version label, user id, plan kind, syllabus id, connection id and remote calendar id (or `none`), destination, preset, reminder time, pause, effective per-course overrides, and for every entry its item key, occurrence, action, composed title, start/end with zone, `endDerived`, `generation`, reminders (minutes and `fireAt`), plus the exclusions, left-alone and skipped-reminder lists and the counts. Compare with `CryptographicOperations.FixedTimeEquals`. Nothing is stored and no expiry is needed because **the server recomputes the plan at write**: the digest is a consistency check, not an authorisation token, so a valid digest is never proof of consent to something the server did not recompute. A cross-user or cross-syllabus digest yields `plan_changed` or `404`, never a write. The same construction binds removal, reminder-update, disconnect and resume plans (machine-readable list in `calendar-policy.json`).

`POST /api/v1/schedule/runs` (`calendar-run-request`, `Idempotency-Key`, 202 `calendar-run`, class `schedule_write`) carries `itemRevisions`, `previewDigest` and `confirm {destination, entryCount, reminderPreset, acknowledged: true}`. The run row stores the digest, confirm, item revisions and the **server** receipt time, plus a content-free audit event (ids, counts). The server recomputes the plan inside the atomic transaction that locks the item rows; any difference is `409 plan_changed` with **zero writes**. Nothing is written without this request.

**Eligibility is re-checked at execution time (CV-07).** Eligibility (`decision == approved`, `when.status == resolved`, kind not `policy`, revision equal to the reviewed one) is checked on the locked rows when the run is planned and **again before each Google write**: the worker re-reads the item and the run row (not cancelled); otherwise the intent becomes `not_eligible` with no Google call. Syllabus decisions, edits and deletes on an item that has a `planned` or `executing` intent are refused with `409 run_in_progress` (a requirement on the Syllabus integration; `syllabus.v1` itself is unchanged). After sync, a withdrawal or edit never writes to Google by itself: the item shows `changed_since_sync` and the UI offers a removal or update preview. Calendar reads syllabus items through a port; Syllabus never sees tokens.

### 8.3 Reminders (Gentle default, popup only)

Rules from `docs/ux/calendar-reminders.md`, as the planner must implement and the fixtures encode:
- Presets `off`, `gentle` (default, pre-selected and visible on the confirm step), `extra_runway`. Gentle: exam 7 days and 1 day before; assignment/other 3 days and 1 day before; reading none; `office_hours` (recurring meetings) none. **Quizzes use the assignment timing (PO-10c); no `quiz` kind exists.** Extra runway adds earlier and morning-of cues, at most 3 per item.
- Method **popup** only. Minutes before are `0..40320`. An explicit empty override list with `useDefault=false` is written for "off" (Google behaviour NOT FOUND in docs, T5).
- Reminder time defaults to 09:00 and is a **whole hour from 08:00 to 20:00** (PO-10f), so no reminder fires in the quiet hours 21:00 to 08:00. Timed event 14:00, 7 days before at 09:00 is 7*1440 + 300 = 10380 minutes; all-day, 3 days before at 09:00 is 3*1440 - 540 = 3780 (all-day offset convention unverified, T5; DST days tested).
- At most 3 reminders on any one local day across all courses; the planner drops deterministically and reports `dailyCapSkips`. Which of several day-before cues to drop is not specified by UX (open). Past or too-close reminders are skipped, not squeezed in.
- Precedence: pause, course override, preset. Pause is until a date (maximum 60 days) with **manual resume** only (PO-10i).
- Destination `tether` has **no reminders**. Uncertain items never get reminders because they never get events.

`POST /api/v1/schedule/reminder-update-previews` then `POST /api/v1/schedule/reminder-updates` change reminders on existing Google events. The update request carries `previewDigest` and `basis` (the preset, time, pause and overrides the preview was computed from); any difference from the current settings is `409 plan_changed`. Off and pause use the same route (the UI may present save and confirm as one step with the count shown). Events in state `edited_in_google` or `deleted_in_google` are skipped (`leftAlone`). `events.update` is a full replace (UNVERIFIED, general Google behaviour; vendor report 6.1 covers `patch`, which overwrites arrays), so reminders are sent only in this run or a confirmed write, after the drift check in 9.3.

### 8.4 Removal

`POST /api/v1/schedule/removal-previews` (read-only) then `POST /api/v1/schedule/removals` (`calendar-removal-request`, `Idempotency-Key`, 202 run of kind `remove`): up to 100 item ids of the path syllabus, with the preview's `previewDigest` and the confirmed `entryCount`. Tether entries are deleted. For Google, Tether deletes events it wrote that the student has not edited, sending `If-Match`. `edited_in_google` entries are **not deleted** and return as `left_alone` / `left_alone_edited` (PO-10a). `deleted_in_google` entries are dropped (`not_present`). A failed remote delete keeps the mapping and is retryable. The removal dialog must state that Google copies of anything not removed remain in Google.

### 8.5 Runs

| Route | Result |
|---|---|
| `GET /api/v1/schedule/runs/{runId}` | `calendar-run` (poll; 2 s backing off to 10 s; never a blocking spinner) |
| `GET /api/v1/schedule/runs/active` | the run in `planned` or `executing`, else `404 not_found` |
| `POST /api/v1/schedule/runs/{runId}/cancel` | naturally idempotent; stops remaining intents; written entries stay; `409 invalid_state` if finished |

### 8.6 Resume (deliberate re-add and "manage this again")

`POST /api/v1/schedule/resume-previews` lists, for the named items, entries in `edited_in_google` (action `take_back`: the student's edit would be overwritten with the syllabus version) and `deleted_in_google` (action `readd`: a new event under the next `generation`). `POST /api/v1/schedule/resumptions` (`calendar-resume-request`, `Idempotency-Key`, 202 run of kind `resume`) carries the preview's digest and count. It is always a deliberate student action in a preview, never automatic (PO-10e, PO-10o). It works only for entries whose row still exists; once content is deleted (PO-9o) the content-free record alone cannot be resumed.

## 9. State machines

### 9.1 Connection

```
not_connected --connect callback ok, sub equal--> connected
connected --confirmed invalid_grant--> needs_reauth        (token ciphertext deleted)
connected --consent-time admin block (after T9)--> admin_blocked
needs_reauth | admin_blocked --connect callback ok--> connected
connected | needs_reauth | admin_blocked --disconnect--> disconnecting
disconnecting --removal finished, or student chooses keep, or terminal token failure, or 24 h deadline--> not_connected
```
Account deletion bypasses `disconnecting` (never blocks on Google).

### 9.2 Run (the sync run state machine)

```
(created) planned --worker lease--> executing --all intents terminal--> succeeded | partial | failed
planned --nothing to change--> nothing_to_do
planned | executing --student cancel--> cancelled
```
- **plan, commit intent, execute, record.** In one atomic transaction (item rows `FOR UPDATE`, per-user advisory lock) the server validates, recomputes the plan, checks `previewDigest`, inserts the run and one **per-occurrence** intent row, and commits. Google calls run **outside the transaction** from a background job (lease and heartbeat), each result recorded; a crash between execute and record is safe because every Google operation is idempotent (13.1). Before each write the worker re-checks eligibility (8.2).
- At most one active run per user (unique partial index). Terminal states never contain an outcome `retrying`.
- `succeeded`: no failures and something changed. `partial`: something changed and something failed. `failed`: nothing changed and something failed. `nothing_to_do`: nothing changed and nothing failed. `cancelled`: remaining intents not attempted.
- Transient failures retry inside the same run (exponential backoff with jitter, `Retry-After`, at most 5 attempts per operation, run deadline 1 hour). There is no retry endpoint: a later attempt is a new preview and confirm. A failed item stays `approved` with `syncState: sync_failed`; a first-ever failure has `syncedRevision: null`.
- Retention: the most recent 20 runs per syllabus are kept (PO-10h, PROPOSAL); intents are content-light and purged with their run.

### 9.3 Entry

```
(none) --create--> scheduled
scheduled --transient Google failure--> retry_later --retry ok--> scheduled
scheduled --student deletes in Google--> deleted_in_google     never recreated automatically
scheduled --student edits in Google--> edited_in_google        preserved, never overwritten or removed
scheduled --confirmed removal--> removal_pending --remote delete confirmed--> (row deleted)
deleted_in_google | edited_in_google --deliberate resume (previewed)--> scheduled (readd: next generation)
any --syllabus delete | course delete | course archive | account deletion | disconnect keep--> (row hard-deleted; content-free record kept per PO-9o)
```
There is no `detached` state. **Drift check (CV-03)**: before any write Tether does `events.get`. The `update` body is the **full resource returned by the get with only the managed fields substituted** (summary, start, end, description, reminders, status), never a freshly built partial body, so unmanaged additions (location, Meet link, colour, attendees) round-trip and a student's additions are also detected as an edit. Tether sends `If-Match: <etag from the get>` on `update` and `delete`; on `412` it re-gets once and re-evaluates. If test T7 shows `If-Match` is unsupported, a second cheap check (`updated` unchanged immediately before the write) is required and the residual window recorded. `last_written_fingerprint`, `remote_etag` and `remote_updated_at` are taken **from Google's response** to our insert or update, never from the request, and compared as canonical subsets of two Google-normalised views (for example Google adds `end.timeZone`). An etag change with an unchanged canonical subset is "unchanged, refresh etag", not "edited". `404`, `410` or `status: cancelled` is `deleted_in_google`. Whether `events.update` replaces the whole resource, and `If-Match` behaviour, are UNVERIFIED (T7). Tether never stores observed remote content, only a hash.

## 10. Idempotency (layers)

1. **Command**: `Idempotency-Key` (8 to 128 URL-safe chars) on runs, removals, reminder-updates, resumptions, disconnect. Scope is (user, method, route template, key), unique index on (`user_id`, `method`, `route_template`, `key_hash`); the fingerprint (an HMAC) covers the concrete path and a canonical hash of the body including `previewDigest` and `confirm`. Same key and fingerprint replays the stored run with `Idempotency-Replayed: true` and **no Google call**; a different fingerprint is `400`. The UI generates a fresh key per click. Keys are kept 24 hours.
2. **Deterministic Google event ids**: `base32hex(SHA-256("tether.calendar.event.v1\n" + remoteCalendarId + "\n" + itemKey + "\n" + (occurrenceDate or "-") + "\n" + generation))`, lowercase `a-v` and `0-9`, first 52 characters (Google: 5 to 1024 characters, `409` on duplicate: VERIFIED). Deliberately **unkeyed**. Pinned vector: remote calendar `cal_fake_0001@group.calendar.google.com`, item key `ik_zmzduAT9Xtu-VKSA5d29Tq_CIpIywcbD`, occurrence `2026-11-12`, generation 1 gives `blelvrn2dn22ig5gqu6s8nmdmbek7ts2orsj5230f3o7n1b98dig`.
3. **`generation` and the content-free record (CV-09, PO-9o)**: reuse of a deleted id is NOT FOUND in official docs, so an id is never re-inserted after Tether deleted the event. The generation counter lives in the **content-free per-event record** (`calendarTargetId`, `eventId`, `generation`, `lastUsedAt`; no title, date, course, item key or fingerprint) that survives entry deletion. The next free generation is the first whose derived id has no record (add-remove-add gives generation 2). Records are deleted at disconnect, account deletion and calendar-target deletion, with an upper limit of 400 days (PROPOSAL). A `409` on insert with no local record is `remote_conflict`, never an overwrite.
4. **Insert race and adoption (CV-14)**: on `409`, `events.get`; adopt as `unchanged` **only when an intent row from this user's confirmed run exists for that id and the remote canonical subset equals the intended fingerprint stored in Postgres**. Markers are never consulted; otherwise `remote_conflict`, no overwrite, no delete.
5. **One active run per user**, per-occurrence intent rows, per-user advisory lock.
6. Extended-property markers (`tether=1`, an opaque hash, `generation`; keys at most 44 characters) are **not a security boundary** (vendor report 4.1): private properties are scoped to the calendar copy, not the OAuth client. They hold no ids, names or document text, and **no Tether code reads `extendedProperties` for any decision** (grep test).
7. **Calendar creation (CV-18)**: `calendars.insert` has no client id. Tether records a `calendar_targets` intent row before the call and the id after; on an ambiguous retry (intent without id) it does not insert again silently: it stops and shows the missing/unknown calendar confirm. A per-user random token in the calendar description lets the student identify duplicates.

## 11. What is stored, what is written to Google

| Data | Postgres | Written to Google |
|---|---|---|
| Refresh token | envelope-encrypted ciphertext (AES-256-GCM, versioned KEK id, AAD = user, connection, `sub`); no access-token column | n/a |
| Entry (title, local date/time, zone, flags, reminders, state, `syncedRevision`) | yes, while the syllabus and connection exist | summary (the title), start and end (`dateTime` with IANA `timeZone`, or all-day `date`), reminders (`useDefault=false`, popup overrides), a fixed description |
| Event description | not stored | fixed template: "Added by Tether from your syllabus. Starts Tue, Oct 14 at 10:00 AM." plus a link built from the configured origin and a fixed path, **no query and no ids** (CV-17). No notes, snippet or contact details. |
| Remote event id, calendar id, generation, fingerprint, remote etag/updated | yes, never on the wire to the browser; after detach only the content-free record (event id, calendar target, generation, last-used time) | the event id; opaque markers |
| Item notes, snippets, syllabus text | in Syllabus only | **never** |
| Course label | yes | only inside the title |
| Student's other calendar events | **never read** | n/a |
| Audit events (connect, grant with `noticeVersion`, run created/finished, removal, disconnect with revocation result) | ids, counts, server time only; retention 400 days (PROPOSAL, PO-10h) | n/a |

**Positive event-body allowlist (CV-17)**: `id`, `summary`, `description`, `start`, `end`, `reminders`, `extendedProperties.private`, `status`. Never `attendees`, `conferenceData`, `attachments`, `source`, `guestsCanModify`, `location`. `sendUpdates=none`. Adapter method allowlist: `oauth2 token`, `oauth2 revoke`, `calendars.insert`, `calendars.get`, `events.insert`, `events.get`, `events.update` (get then update, not `patch`), `events.delete`. **Not allowed**: `events.list` in v1, `calendars.delete`, `calendarList.*`, `acls.*`, `permissions.*`. Machine-readable in `calendar-policy.json`.

## 12. Data model deltas (list only; database-architect owns the design)

`integrations` tables per the token doc (`google_connections` with unique `user_id` and unique `google_sub`, `google_grants` with the `noticeVersion` as consent text version, `oauth_states` with a cap on un-consumed rows per IP and binder). Calendar-module tables: `schedule_settings`, `schedule_entries`, `schedule_runs` (digest, confirm, revisions, server time), `schedule_intents` (per occurrence; state, attempts, last error code, no Google text), `calendar_targets`, and the content-free `event_records` (calendar target id, event id, generation, last-used time). Postgres is the working record (PO-9m). The deprecated mock `syllabus.calendar_events` table is removed once this is implemented (PO-10g). The exhaustive syllabus-delete scan test (`API_CONTRACTS_P2.md` 5.2) must be extended to scan `schedule_entries`, `schedule_runs`, `schedule_intents`, `calendar_targets`, `oauth_states` and idempotency rows for the syllabus id, item keys and content hashes; only audit rows (ids and counts) and `event_records` may survive.

## 13. Google call behaviour

### 13.1 Idempotent reconciliation per occurrence

| Local state | Remote state | Action |
|---|---|---|
| no entry | none | `events.insert` with the deterministic id; fingerprint taken from the response |
| entry, same fingerprint | exists, canonical subset equal | `unchanged` (refresh etag) |
| entry, fingerprint differs | exists, not edited | `events.get`, then full-resource `events.update` with `If-Match` |
| entry | exists, differs from last written | `edited_in_google`, no write |
| entry | `404`/`410`/`cancelled` | `deleted_in_google`, no write, no automatic recreate |
| insert | `409` | adoption rule in section 10 item 4 |
| occurrence no longer in plan | exists | delete (only events Tether wrote and the student did not edit) |

### 13.2 Rate-limit handling

Google: 10,000 requests per minute per project and 600 per user per project, undocumented limits on rapid writes to one calendar, time-based limits as `403` or `429`; the 1,000,000/day figure is a billing threshold (vendor report 6.9). Tether (all PROPOSAL, `calendar-policy.json` `limits`, pending devops/PO per PO-10q): a per-user token bucket of 120 Google requests per minute, 3 event writes per second per calendar, a **per-user daily Google budget** (3,000 writes) and a per-user cap of runs per day (20), **per-user fair queuing** (weighted round-robin) of the shared egress limiter, and a global soft limit below the billing threshold with an alert at 50%. Reaching the global limit **sheds the heaviest users first**; it never takes Calendar down for everyone. Budget exhaustion is `429 rate_limited` with `Retry-After`, never silent. Backoff `min(2^n + random_ms<=1000, 32 s)`, at most 5 attempts per operation.

### 13.3 Google error classification (closed; also `calendar-policy.json`)

| Signal | Tether |
|---|---|
| `invalid_grant` (refresh, confirmed by one retry) | the only signal that deletes the token: `needs_reauth` |
| `invalid_grant_unconfirmed` | retry once after a short delay |
| `invalid_client`, `unauthorized_client` | run `provider_unavailable`, operator alert, connection unchanged |
| `accessNotConfigured`, `SERVICE_DISABLED` | run `provider_unavailable`, operator alert, connection unchanged |
| `quotaExceeded` | `provider_unavailable`, spend alert, no blind retry |
| `rateLimitExceeded`, `userRateLimitExceeded` (and `429`) | retry with backoff; exhausted: `provider_rate_limited` |
| `insufficientPermissions` | `needs_reauth` without deleting; item `connection_required` |
| `unknown_403` | item `remote_rejected`; **unrecognised 403 reasons never delete a token** |
| `decrypt_failure` | run `provider_unavailable`, connection unchanged, alert |
| `http_5xx`, `timeout` | retry; exhausted: `provider_unavailable` |
| `event_404_410` | `deleted_in_google` (delete counts as success) |
| `calendar_404` | confirm via `calendars.get`; `missing_in_google`; a new calendar only on explicit confirm |
| `insert_409` | adoption rule (section 10) |
| `etag_412` | re-get once, re-evaluate |
| `http_400` | no retry; `remote_rejected` |
| `admin_policy_enforced` | treated as `needs_reauth` until T9 verifies the code (UNVERIFIED) |
| `access_denied_admin` | consent-time only: callback `admin_blocked` (UNVERIFIED) |

## 14. Rate-limit classes (PROPOSAL; devops and PO own the values)

| Class | Routes | Proposal |
|---|---|---|
| `auth_start` | both start routes | per-IP ceiling of about 300/min (a lecture hall behind one NAT must still sign in), per binder cookie 10/min, a global start-rate alarm, at most 20 un-consumed `oauth_states` per IP or cookie with a short TTL purge; the 21st is rejected before any database write; `429` shows the calm "try again in a moment" copy (CV-13) |
| `auth_callback` | callback | about 300/min per IP |
| `schedule_read` | GETs | the global 120/min read limit |
| `schedule_preview` | every `*-previews` route and `disconnect-preview` | 30/min per user |
| `schedule_write` | settings PUT, runs, cancel, removals, reminder-updates, resumptions, disconnect | 20/min per user, a separate bucket |
| `schedule_export` | export | 6/min per user |
| `google_egress` (internal) | outbound Google calls | section 13.2 |

Limits: 100 items per request, 500 events per run, 1,000 events per syllabus, 5,000 entries per user (syllabus.v1 caps), 60 occurrences per item, 100 results per run response, JSON bodies 16 KB. Items beyond the cap are `excluded` with `event_limit_reached`. Order: ascending effective date, then id.

## 15. Deletion and export semantics

| Action | Tether | Google |
|---|---|---|
| Removal run | rows deleted | events deleted unless the student edited them |
| Student deletes an event in Google | `deleted_in_google`, never recreated automatically | gone |
| Student edits an event in Google | `edited_in_google`, preserved | their edit stays |
| Delete a syllabus | entries for it are **hard-deleted** in the same transaction; only content-free records may remain | events **remain in Google** by default (PO-10m); the dialog must say so and offer "also remove from Google" first, executed via a removal run while the grant is valid |
| Delete or archive a course | same as syllabus delete for its entries | same |
| Disconnect `keep` | entries hard-deleted; content-free records deleted at disconnect | everything stays; dialog says so |
| Disconnect `remove_events` | entries removed except edited ones; records deleted | those events deleted before revocation |
| Account deletion | disconnect path with one bounded pass, then all schedule rows, token row and records deleted even if Google fails; never blocks | copies stay; the student is told |
| Token lost or confirmed `invalid_grant` | `needs_reauth` | untouched; "we can no longer remove copies in your Google account" |
| Closed Google account | tokens deleted; sessions end at normal lifetime; data follows retention (PO-10n) | n/a |
| Export | `calendar-export`: settings, connection summary, entries, recent runs; **no tokens, key ids, remote ids or fingerprints** | copies are the student's own |

Dialogs for delete, disconnect and account deletion must state that **events remain in Google** (machine-readable list `dialogsMustState` in `calendar-policy.json`).

## 16. Security requirements restated as testable rules

"Contract" = an executable test exists now in `CalendarContractTests`; "Required" = must exist before the Calendar module merges (none exist yet). A rule row that is deleted or reworded fails `Every_security_rule_row_exists_carries_its_required_phrases...`.

| ID | Rule | Test |
|---|---|---|
| CAL-01 | Only the scope vocabulary `openid email profile calendar.app.created` is expressible; start URLs request exactly the sign-in or connect set; sign-in sends `prompt=select_account`, connect sends `prompt=select_account consent` and `login_hint`; `include_granted_scopes` is never sent; PKCE is `S256`; the redirect is exact | Contract; Required: scope allowlist test over code and config |
| CAL-02 | No schema or fixture defines or contains a token, secret, `sub`, calendar id or remote event id (names matched case-insensitively as substrings) | Contract |
| CAL-03 | An item without a resolved date cannot be an entry or reminder; the preview lists it as `needs_date`; zero gateway calls for it | Contract (schema, fixtures); Required: negative test counting fake-Google requests |
| CAL-04 | A write, removal, reminder-update, resume and `remove_events` disconnect all carry `previewDigest` and a confirmed count; a plan difference is `409 plan_changed` with zero writes | Contract (schemas require the digest and count); Required: integration |
| CAL-05 | No Google write at callback, at settings save, after an item edit, or without a run request; eligibility is re-read before each Google write and a changed item becomes `not_eligible` with no call | Required: fake Google request counter, withdraw-during-run test |
| CAL-06 | Connect `sub` must equal the signed-in `sub`; on `account_mismatch` nothing is stored, and Tether does not call revoke when the `sub` belongs to another Tether user; `select_account` prevents auto-picking | Required: fake OIDC with two Tether users |
| CAL-07 | State is single use, expires in 10 minutes, bound to purpose, scopes and session or binder cookie; tamper, replay, cross-session and cross-user all give `state_invalid` before token exchange | Required |
| CAL-08 | Start routes are POST with Origin check and `X-Tether-CSRF`; no GET start exists; the binder cookie stops login CSRF; callback is GET only | Required |
| CAL-09 | Token ciphertext only; AAD bound to user, connection and `sub`; a row moved to another user fails to decrypt; token columns are absent from export and every response | Contract (schema), Required (cipher tests) |
| CAL-10 | AI never receives a token: `Tether.Ai` has no reference to the Calendar module; tokens are never in a prompt, log, trace, metric or export. The log-canary seeds `code`, `state`, `id_token`, a calendar id and an event title and asserts absence in request logging, proxy logs, HttpClient/OTel URLs, Npgsql/EF, `System.Net.Http`, the Google handler and ProblemDetails at Trace level, including under fault injection (`invalid_grant`, 5xx, timeout) | Required (architecture test plus canary) |
| CAL-11 | Cross-user access (preview, run, cancel, remove, resume, disconnect, entries, runs) is `404` with the same body as missing, including `courseOverrides` keys, `courseId`, removal ids and a forged `cursor` | Required: route table with two users and overlapping ids |
| CAL-12 | Per `Idempotency-Key`, replay returns the stored run without a Google call (disconnect returns the current resource); key reuse with a different body is `400`; a double submit yields one event set; a crash between execute and record resumes without duplicates; the UI uses a fresh key per click | Required (fake Google) |
| CAL-13 | Deterministic base32hex event id: pinned vector, alphabet, length; the content-free ledger makes add-remove-add produce generation 2 | Contract |
| CAL-14 | A deleted event is never recreated automatically (never recreate) and an edited event is never overwritten or removed; writes use `If-Match` and a full-resource update; adoption needs an intent row and the intended fingerprint; no code reads `extendedProperties` for a decision; `edited_in_google` is preserved | Required (fake Google drift cases, grep test) |
| CAL-15 | Reminders: popup only, at most 3 reminders per event, `0..40320` minutes, none in quiet hours, at most 3 per day, none on the Tether destination, none for uncertain items; assignment timing for quizzes | Contract (fixtures), Required (planner unit tests incl. DST) |
| CAL-16 | A token is deleted only on `invalid_grant` confirmed by one retry; a fleet circuit breaker stops mass deletion; no retry loop | Contract (policy file), Required |
| CAL-17 | Disconnect order is remove, then revoke, then delete the row; the row is deleted even when revocation fails; `disconnecting` exits on terminal token failure, student choosing keep, or the 24 h deadline; account deletion never blocks on Google | Contract (policy file), Required |
| CAL-18 | The composed title is re-checked by TextPolicy at the adapter boundary; the event body is a positive allowlist; the description link has no query and no ids; `sendUpdates=none` | Contract (policy file, fixtures), Required (adapter canary) |
| CAL-19 | Adapter method allowlist and host allowlist; `AllowAutoRedirect=false`, no proxy from the environment, no runtime discovery fetch | Contract (policy file), Required |
| CAL-20 | `AllowDuplicateProperties = false` and `UnmappedMemberHandling = Disallow` on the real binder | Contract (mechanism pinned); Required (real binder) |
| CAL-21 | The Testing-status 7-day token expiry and `invalid_grant` are reproduced by the fake and handled as CAL-16 | Required |
| CAL-22 | Dev/Test cannot reach real Google: `Providers:Google:Mode=Fake` is refused outside Development/Test when the real client is not configured, and the reverse; startup guard test | Required |
| CAL-23 | Identity: find-or-create by `iss` + `sub` only; same email with a different `sub` creates distinct users; a double callback yields one user and one session; the session id differs before and after sign-in; the binder cookie is cleared on use and on failure; a unique index prevents two users on one `sub`; wrong `iss`, `aud`, expiry or `nonce` is rejected before any user lookup | Required |
| CAL-24 | Token deletion classification is closed: only `invalid_grant` confirmed by retry deletes; `invalid_client`, `accessNotConfigured`, quota reasons, 5xx, decrypt failures and any unrecognised 403 never delete; a mass `invalid_client` deletes nothing; the table is `calendar-policy.json` | Contract (policy file), Required (fake Google, every signal maps to exactly one outcome) |
| CAL-25 | A per-user daily Google budget and a per-user cap on runs exist; the shared egress limiter is fair per user; the global limit sheds the heaviest users first and never disables Calendar for everyone; sign-in limits cannot lock out a NAT (values in `calendar-policy.json`, PROPOSAL) | Contract (policy file), Required |
| CAL-26 | Deletion: entries are hard-deleted on syllabus delete, course delete, course archive, account deletion and disconnect keep; only a content-free per-event record (event id, generation, no title, date or course) may survive, deleted at disconnect, account deletion, calendar-target deletion or 400 days (PROPOSAL); dialogs state that events remain in Google | Contract (policy file), Required (extended delete scan test) |

## 17. Tests: what exists, what does not

Exists (`CalendarContractTests`; command in `src/shared/contracts/calendar.v1/README.md`): every schema has valid and invalid fixtures and each is accepted or rejected; objects are strict; shared definitions equal `syllabus.v1`; forbidden property names (case-insensitive substring matcher with its own unit cases); token-shaped strings; scope and URL allowlists; callback pairing; events have a resolved date, a timezone and a `Course · Title` title with no type word; excluded items are never entries; reminder rules; preview and run count consistency; every digest-bound request requires `previewDigest`; disconnect shape rules; the connection reports revocation; the content-free record contains only event id and generation; the error-map, limits, retention, digest and event-body policy files; the event-id vector and add-remove-add; rule rows CAL-01 to CAL-26 anchored by id and required phrases; headings, decisions and CV/S references; enum and route documentation drift.

**Not run and not written**: everything marked Required in section 16; the fake Google and fake OIDC servers; DST and all-day reminder arithmetic; the real-account tests T1 to T12 (PO approval, throwaway account, Cloud project in Testing status). Nothing in this contract has touched a real Google API.

## 18. Judgement calls (mine, to be confirmed)

J1 Fixtures live in `calendar.v1/fixtures`. J2 Shared definitions are copied and a test asserts equality. J3 `destination` starts `unset`. J5 The preview is stateless. J6 Writes return 202 and a polled run. J7 Reminders only for Google. J8 Settings save never writes to Google. J11 No retry endpoint. J12 Unkeyed event id with a content-free generation record. J13 Connect requests `openid` for `sub` comparison. J14 Binder cookie for sign-in state. J15 Callback exempt from Origin, under the S3 conditions. J16 Superseded by CV-05: only a confirmed `invalid_grant` deletes. J17 Superseded by PO-10f. J19 Item notes never written. J21 `events.get` then full-resource `events.update`. J23 A deleted Tether calendar is recreated only on explicit confirm. J24 Run results reuse the frozen `syncState` values. J25 Caps reuse the syllabus numbers. J26 Daily-cap drop order for several day-before cues is unspecified by UX. **New in this revision**: J27 Content-free records are never used to delete events, because they hold no fingerprint and PO-10a forbids removing edited events (conflict with PO-9o's "can still remove its own events later", Q14). J28 Resume is one run kind covering both `take_back` and `readd`. J29 `moving old entries` (PO-10d) is reserved, not specified. J30 Digest binding list and policy file are machine-readable so tests can execute them. J31 `eventsInGoogle` in settings reports the number of scheduled Google events.

## 19. Conflicts between inputs

| ID | Conflict | Resolution here |
|---|---|---|
| X1 | Proposal: Google account may differ from sign-in. | PO-9d: same `sub`. |
| X2 | Proposal and UX: one calendar per course. | PO-9i: one calendar; course in title. |
| X3 | Proposal: reminders off by default. | PO-9l: Gentle on. |
| X4 | Proposal: markers visible "only to our client ID". | Vendor 4.1: not a security boundary. |
| X5 | Proposal: HMAC event id, `patch`. | Unkeyed id, `get` + full-resource `update`. |
| X6 | UX: type word in the title. | PO-10b: none. |
| X7 | UX: Quiz and Class meeting presets; `syllabus.v1` has neither. | PO-10c: assignment timing for quizzes; no new kind. |
| X8 | UX: Off and Pause apply immediately. PO: writes only after preview and confirm. | Same-click flow through a confirmed `reminder-updates` run. |
| X9 | UX: reminders on by default; Tether calendar cannot deliver them. | Reminders only for Google. |
| X10 | Token doc: `needs_reauth` fixed by sign-in. | Reconnect via connect flow. |
| X11 | Token doc: keep the working copy on account closure. PO-9n: no access to account or data. | PO-10n: tokens deleted now, sessions to normal lifetime, data per retention; purge timing open. |
| X12 | Vendor 8.10: revocation removes all scopes including the sign-in grant. | Disconnect revokes everything; dialog says the next sign-in asks consent again; session kept. |
| X13 | Review CV-01 (limited-window `detached` rows) vs PO-9o option C. | PO-9o wins: delete all content immediately; content-free record only. |
| X14 | PO-9o: record lets Tether "still remove its own events later". PO-10a: Tether never removes edited events. A content-free record cannot show an edit. | J27: records are not used to delete; Q14. |
| X15 | PO-10o: resume "only for entries whose content-free record still exists". After PO-9o the record holds no content to rebuild an entry. | Resume needs the entry row; record-only events cannot be resumed (Q15). |

## 20. Needs security-engineer review before freeze (re-review)

R1 The revised sign-in/connect URL set, `prompt=select_account consent`, `login_hint=<sub>` and the no-revoke-on-mismatch rule (CV-04, CV-10, S1). R2 Binder cookie specification (S2). R3 Callback conditions (S3). R4 `previewDigest` construction and its use for removal, reminder-update, disconnect and resume (S4, CV-08, CV-12). R5 Closed error classification, single-retry confirmation and the circuit breaker thresholds (CV-05, S5). R6 Content-free event record: confirm event id plus generation (and the calendar target id and last-used time needed to expire it) is acceptable as "content-free", especially the 400-day limit and that it outlives the syllabus (PO-9o, CV-01, CV-09). R7 J27 and Q14 (records not used for deletion). R8 Full-resource `update`, `If-Match` and fingerprint-from-response, all UNVERIFIED (CV-03, S9). R9 Adoption rule and markers never read (CV-14, S7). R10 Execution-time eligibility recheck and the Syllabus 409 requirement (CV-07). R11 Disconnect exits, bounded account deletion and revocation reporting (CV-02, CV-19, S6). R12 Log-canary scope (CV-15, S12). R13 Rate-limit design: per-user daily budget, fair share, shed-heaviest, NAT-safe sign-in limits (CV-06, CV-13). R14 Resume run as a new external write path (CV-23, PO-10o).

## 21. Open questions (Product Owner / Lead Architect)

Q1 Confirm that ledger-only events are never deleted by Tether (J27) or whether PO-9o intends deletion with an "edits cannot be checked" warning (Q14). Q2 Specify the "move old entries" run (PO-10d). Q3 Pause is manual (PO-10i). Q4 Closed-account purge timing and any non-Google recovery path (PO-10n). Q5 Day-before reminder drop order when more than 3 fall on a day. Q6 Run retention 20 per syllabus, audit 400 days, record 400 days are PROPOSAL (PO-10h). Q7 PO-10l transcript opt-in and PO-10j Workspace warning copy need legal review (G1). Q8 Whether the Syllabus integration will really refuse changes to items with an active intent (CV-07) or cancel the run. Q9 Per-user daily budget, run cap and thresholds need devops and PO (PO-10q). Q10 Cloud project, OAuth client, domain, verification ownership (PO-9g, PO-9k). Q11 Notice text for `noticeVersion` (legal). Q12 Whether `hd` is available at sign-in with `email profile` (CV-22; T9). Q13 Course archive: confirm "hard-delete entries" applies to archive too. Q14 See Q1. Q15 PO-10o wording: resume for record-only events cannot work without content (X15).

## 22. Compatibility

No change to `syllabus.v1`, `lecture.v1` or any existing code. The frozen `calendar-sync` routes stay mock-only and deprecated (PO-10g). Docs/Drive writes and transcript upload (PO-10l) are outside this contract. Additive changes to `calendar.v1` after freeze only.

## 23. Vocabulary (every enum in `calendar-common.schema.json`)

| Enum | Values |
|---|---|
| `destination` | `tether`, `google` |
| `settingsDestination` | `unset`, `tether`, `google` |
| `reminderPreset` | `off`, `gentle`, `extra_runway` |
| `courseReminderSetting` | `use_default`, `off`, `gentle`, `extra_runway` |
| `entryState` | `scheduled`, `retry_later`, `removal_pending`, `deleted_in_google`, `edited_in_google` |
| `connectionStatus` | `not_connected`, `connected`, `needs_reauth`, `admin_blocked`, `disconnecting` |
| `calendarState` | `not_created`, `ready`, `missing_in_google` |
| `eventsAction` | `keep`, `remove_events` |
| `revocation` | `confirmed`, `unconfirmed` |
| `runKind` | `write`, `remove`, `reminders`, `resume` |
| `actionPreviewKind` | `removal`, `reminders`, `disconnect`, `resume` |
| `resumeAction` | `readd`, `take_back` |
| `runState` | `planned`, `executing`, `succeeded`, `partial`, `failed`, `nothing_to_do`, `cancelled` |
| `outcome` | `created`, `updated`, `unchanged`, `removed`, `not_present`, `left_alone`, `retrying`, `failed`, `not_eligible` |
| `previewAction` | `create`, `update`, `unchanged` |
| `skippedReminderReason` | `daily_cap`, `already_past`, `too_close`, `beyond_google_limit`, `quiet_hours`, `course_off`, `paused` |
| `excludedReason` | `needs_date`, `not_approved`, `policy`, `revision_changed`, `not_found`, `event_limit_reached` |
| `leftAloneReason` | `edited_in_google`, `deleted_in_google` |
| `callbackOutcome` | `signed_in`, `connected`, `access_denied`, `account_mismatch`, `scope_missing`, `scope_unexpected`, `admin_blocked`, `state_invalid`, `provider_unavailable` |
| `googleScope` | `openid`, `email`, `profile`, `calendar.app.created` |
| `errorCode` | `validation_failed`, `unauthenticated`, `csrf_failed`, `not_found`, `invalid_state`, `concurrency_conflict`, `precondition_required`, `precondition_failed`, `rate_limited`, `provider_unavailable`, `connection_required`, `destination_not_chosen`, `plan_changed`, `run_in_progress` |
| `entryErrorCode` | `item_needs_date`, `item_not_syncable`, `item_not_approved`, `precondition_failed`, `not_found`, `provider_unavailable`, `invalid_state`, `event_limit_reached`, `connection_required`, `provider_rate_limited`, `remote_rejected`, `remote_conflict`, `left_alone_edited`, `left_alone_deleted` |

## 24. Review disposition (`docs/security/calendar-v1-contract-review.md`)

| Item | Resolution |
|---|---|
| CV-01 | Section 15, 9.3, 12, CAL-26: entries hard-deleted on syllabus/course/archive/account delete and disconnect keep; PO-9o content-free record only; scan test extended. Retention numbers PROPOSAL. |
| CV-02 | 5.4, 9.1: exits from `disconnecting`; account deletion never blocks; CAL-17. |
| CV-03 | 9.3, 13.1: full-resource update, `If-Match`, fingerprint from response, canonical subset. UNVERIFIED items marked (T7). |
| CV-04 | 5.1, CAL-06, URL fixtures and pinned test: `select_account`, no revoke for a known `sub`. |
| CV-05 | 5.3, 13.3, `calendar-policy.json`, CAL-16/24; circuit breaker PROPOSAL. |
| CV-06 | 13.2, 14, CAL-25; numbers PROPOSAL (PO-10q). |
| CV-07 | 8.2, CAL-05; Syllabus-side 409 is a requirement on the integration (Q8). |
| CV-08 | Wire change: `previewDigest` on removal, reminder-update, resume; `entryCount` plus digest on disconnect `remove_events`; five new preview/request schemas and routes. |
| CV-09 | Superseded by PO-9o option C: content-free record; 10 item 3. |
| CV-10 | `include_granted_scopes` removed from the URL set; `scope_unexpected`/`scope_missing` store nothing. |
| CV-11 | `noticeVersion` on connect start; run row stores digest, confirm and server time. |
| CV-12 | 8.2 and `calendar-policy.json` `previewDigest`. |
| CV-13 | 14 and policy `limits.auth`; values PROPOSAL. |
| CV-14 | 10 item 4, 10 item 6, CAL-14. Implementation grep test required. |
| CV-15 | 5.1 callback headers and redaction, CAL-10 widened canary. |
| CV-16 | 2 (id-bearing inputs), CAL-11. |
| CV-17 | 8.1, 11, CAL-18, policy event-body allowlist; title fixtures (RLO, leading ZWJ, over 200, HTML-looking plain text). |
| CV-18 | 10 item 7; implementation. |
| CV-19 | `revocation` field on the connection; fresh key per click; replay returns the current resource. |
| CV-20 | 6; `eventsInGoogle` in settings. |
| CV-21 | CAL-23; implementation tests. |
| CV-22 | 5.2: `hd` captured at sign-in; `admin_blocked` unreachable until T9. Workspace allowed with warning (PO-10j). |
| CV-23 | Resolved by PO-10a, PO-10e, PO-10o: section 8.6 resume run. |
| CV-24 | Case-insensitive forbidden-name matcher with unit cases; rule rows anchored by id and phrases; policy files executed; 11 new executable tests. |
| CV-25 | 5.3: sessions end at normal lifetime (PO-10n). |
| CV-26 | 2: `X-Tether-CSRF` (PO-10p), `Content-Type`, `SameSite=Lax` pinned by test (Required). |
| CV-27 | 5.1 egress rules; `login_hint` is the `sub`. |
| CV-28 | Unchanged: everything mock/fake; T1 to T12 NOT RUN. |
| S1 | Changed as CV-04/CV-10. S2 accepted with the cookie specification in 5.1. S3 accepted with the stated conditions. S4 as CV-12. S5 as CV-05. S6 kept: session continues, dialog states the consent re-prompt, `revocation` reported. S7 as CV-14. S8 via CAL-23. S9 as CV-03; `leftAlone` exposes ids, date and reason only. S10 as CV-17. S11 as CV-06. S12 as CV-15. S13 kept: the browser is not told which field changed and no remote content is stored. |
