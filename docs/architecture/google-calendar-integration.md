# Google Calendar integration: design plan (PROPOSAL)

Status: **PROPOSAL, nothing here is implemented.** Author: google-integration, 2026-10-28. No Google Cloud project, OAuth client, API call or code change was made to produce it. It does not clear any gate (legal, G1-G5) and does not authorize a Google write. Open items are marked **[PO]** (Product Owner), **[UNVERIFIED]** (not confirmed against current official docs) or **[VERIFY]** (confirm on first real run).

Inputs read: `CLAUDE.md` (invariants 3, 4; gates), `docs/API_CONTRACTS_P2.md` (decision 6, 6.3, 15, 17), `docs/DECISIONS.md` (ADR-012, PO-6), `docs/PROGRESS.md` (Iteration 9), `docs/SECURITY.md` (SEC-020, SEC-021, SEC-027), `docs/security/p2-contract-delta.md`, `docs/legal/phase2-uploads-and-vendors.md` (line 126: Google only after student-approved sync, separate review), `docs/data/schema-syllabus-p2.md`, and `src/api/src/Modules/Syllabus/Tether.Syllabus/Application/SyncService.cs`. I did not read every file under `src/shared/contracts/syllabus.v1/` (I used the file list and the contract text).

## 0. Assumptions, scope boundary, smallest deliverable

- `syllabus.v1` is frozen and mock-only. Real Calendar is **not additive** (contract 6 decision 6, section 15): it needs a new `calendar.v1` contract (or `syllabus.v2`) plus a security re-review. The mock constants `demoCalendar`, `confirm.provider`, `calendar-event.demo` cannot carry a real provider.
- PO-6 (Google OAuth client, identity provider, token key management) is open. Sign-in today is `UnconfiguredGoogleIdentityProvider` (Identity module).
- Smallest verifiable deliverable: a `calendar.v1` contract + a **fake-Google-HTTP-server-backed adapter** passing contract tests, with no real Google call. A real-account smoke test is a later, PO-approved step (section 8).
- Out of scope: Docs/Drive export, reading the student's existing events, push notifications/watch channels, practice-test or lecture calendar items.

## 1. Existing provider interface and what a real adapter must implement

Today (`SyncService.cs`):
- `ICalendarProvider` is only `string Name { get; }`. It is a marker. All behavior lives on the concrete `MockCalendar`, which `SyncService` takes directly (constructor parameter `MockCalendar calendar`), and writes to `syllabus.calendar_events` in the same DB transaction as item state.
- `MockCalendar` surface used: `Build(item, maxOccurrences)` (static, pure; produces `EventSpec` list: occurrence date, start/end date and time, timezone, `EndDerived` for the 60-minute default, `AllDay`), `UpsertAsync(item, specs, now)` returning (inserted, updated, unchanged, removed), `RemoveItemEventsAsync(itemId)`, `FaultInjected`, and `CalendarUnavailableException`.
- `SyncService` owns: idempotency key + fingerprint, rate gate, per-syllabus lock, item row locks in sort order, re-check on the **locked** row (`decision == approved`, `WhenStatus == resolved`, not `policy`, revision match -> `precondition_failed`), event caps (500/call, 1,000/syllabus, per-user), `SyncRun` and `SyncRunItem` records, replay of the original result.
- Startup refuses any provider name other than `mock` (SyllabusModule).

Problem: a real provider performs network I/O that cannot sit inside the DB transaction holding row locks and an advisory lock, and cannot be rolled back. The mock's "write and commit atomically" model does not transfer.

Proposed interface (**proposal**, name `ICalendarGateway`, in a new `Tether.Calendar` module so Syllabus depends only on a contract, as Courses/Identity do):

```text
Task<CalendarTarget>   EnsureAppCalendarAsync(userId, courseId, ct)            // create-or-find the app-owned calendar
Task<UpsertOutcome>    UpsertEventAsync(target, EventSpec, ownershipKey, ct)   // create (deterministic id) or patch
Task<DeleteOutcome>    DeleteEventAsync(target, remoteEventId, ct)
Task<RemoteEvent?>     GetEventAsync(target, remoteEventId, ct)               // for drift detection
Task<DisconnectResult> DisconnectAsync(userId, ct)
ConnectionStatus       (connected | not_connected | needs_reauth | revoked | admin_blocked)
```
`EventSpec`/`Build` stay provider-neutral and are reused unchanged (they already carry timezone, all-day and derived end). The adapter holds the Google HTTP client; Syllabus never sees tokens (section 3). `MockCalendar` is kept as the Development/Test implementation; the startup guard changes from "name must be mock" to "name must be in an allowlist and `google` additionally requires PO-6 closed + production-mode config".

Required sync restructure (proposal): split into **plan -> commit-intent -> execute -> record**.
1. Under the existing transaction/locks: validate eligibility, compute the exact write plan, persist a `sync_run` in `planned` state with per-occurrence intents, commit and release locks.
2. Execute remote calls outside the transaction, per occurrence, recording each result (outbox pattern, a Postgres job like the extraction jobs).
3. Record final outcomes and item `sync_state`. Replay returns the stored run, as today.
A crash between 2 and 3 is safe because every remote operation is idempotent (section 4).

## 2. OAuth 2.0 flow, scopes, incremental consent

**Flow (proposal):** authorization-code flow, server-side web client, confidential client secret held server-side, PKCE (S256) anyway, `state` bound to the server session + user + purpose and single-use with 10-minute expiry, exact-match redirect URI, `access_type=offline`, `prompt=consent` only when no refresh token is held, `include_granted_scopes=true` for incremental authorization. Tokens exchanged server-side only; the browser sees only a success/failure redirect. Rationale: SEC-021 (PKCE, state, exact-match redirect). Whether PKCE adds value for a confidential client is [UNVERIFIED]; it costs little, keep it.

**Separate from sign-in.** Sign-in requests identity scopes only (`openid email profile`), per PO-6. Calendar access is a distinct, student-initiated "Connect Google Calendar" action from the syllabus review screen, not at sign-up (incremental authorization). A student who signs in with a different identity provider can still connect Calendar. The Google account used for Calendar may differ from the sign-in account; store the Google account subject (`sub`) and show the email in the UI so the student sees which account is connected.

**Scope matrix** (fetched 2026-10-28 from https://developers.google.com/identity/protocols/oauth2/scopes, Calendar API v3 list; descriptions paraphrased by the fetch tool, page said it covers only part of the text, so sensitivity classification is [UNVERIFIED], check the Cloud Console consent-screen scope picker):

| Scope | What it allows | Verdict |
|---|---|---|
| `calendar.app.created` | Create secondary calendars and manage events **on them** | **Recommended (single scope).** Tether can only touch calendars it created; cannot read or edit the student's other events. |
| `calendar.events.owned` | Create/change/delete events on calendars the user owns | Fallback if app.created cannot do something we need; reaches the primary calendar, so broader. |
| `calendar.events` | View and edit events on **all** calendars | Reject: reads everything. |
| `calendar` | Full access incl. sharing and deleting calendars | Reject. |
| `calendar.calendarlist*`, `calendar.calendars*`, `*.readonly`, `freebusy`, `acls*` | Lists/settings/availability/sharing | Not needed. Conflict detection with the student's other events is **not offered** in v1 (it would need read scope); say so in UI. |

Justification: a dedicated app-created calendar gives (a) least privilege, (b) a clear "what the app owns" boundary (the whole calendar), (c) a one-step undo (delete the calendar or its events), (d) no merge with the student's personal events. Trade-offs to confirm [VERIFY]: whether `calendar.app.created` allows `calendars.insert`, `events.*` on that calendar, `calendars.delete`, and `calendarList.insert/patch` to make it visible and set its colour; whether reminders and `extendedProperties` work normally; whether an event the student drags to another calendar leaves our reach. Student-side: the secondary calendar appears in their Google Calendar list and can be toggled off, which is itself a good neurodivergent-friendly control (hide without losing).

**Granularity:** one app calendar per student, named `Tether - <course>`? or per course? **[PO]** Recommendation: **one calendar per course** (hide/delete a course's plan independently; matches `calendar_events.course_id`). Cost: more calendars; N+1 `calendars.insert`. Acceptable.

**Consent UI** (`neurodivergent-ux-specialist` + `frontend-engineer`, `security-engineer` review): pre-redirect screen in plain language: what Tether will create (a separate calendar), what it can never do (read your other events), that nothing is written until you confirm items, how to disconnect. Show Google's own consent screen after that. Handle `error=access_denied` as a normal, calm outcome ("Nothing was changed. You can still use the plan here."). Handle partial grant (scope not granted) by treating as not connected.

## 3. Token storage, encryption, key management, revocation, AI isolation

- **Storage:** new table `integrations.google_connections` (section 6): one active connection per (user, purpose). Store the refresh token only (access tokens are short-lived; hold in memory/short cache, never persisted). Encrypt with envelope encryption: per-row data key, AES-256-GCM (or ASP.NET Data Protection with a keyring in a vault/KMS per SEC-020), `key_id` and `key_version` stored for rotation; AAD binds ciphertext to `user_id` + connection id so a row copied between users fails to decrypt. Key material is **PO-6**: managed KMS/vault vs Data Protection keyring needs a decision and `devops-engineer` implementation. Dev uses a generated local key, never committed.
- **Never** in logs/traces/metrics/exports/error bodies (SEC-027). Add a log-canary test for token-shaped strings, as already planned for lecture text. Google `Authorization` headers are redacted at the HttpClient handler. The export and delete endpoints exclude token columns.
- **AI isolation (invariant 3):** the Calendar module lives in its own assembly with no reference to `Tether.Ai`; the architecture tests (already 19) add a rule: `Tether.Ai` and extraction code must not reference `Tether.Calendar` or the token store, and the Calendar module exposes no token-returning method at all (token use is internal to the HTTP handler). Prompts are built from the syllabus text only; a canary test asserts no prompt contains a seeded token string.
- **Refresh and expiry** (https://developers.google.com/identity/protocols/oauth2, fetched 2026-10-28): refresh tokens stop working on user revocation, 6 months of non-use, admin restriction (`admin_policy_enforced`), session-control expiry (`invalid_grant`), exceeding 100 live tokens per account per client (oldest silently invalidated), and **7 days in Testing publishing status for external users** (matters for the smoke test and any pre-verification pilot; section 9). On `invalid_grant`, mark the connection `needs_reauth`, stop all sync, surface a gentle reconnect prompt; do not retry in a loop.
- **Disconnect (user-visible, in Settings and the sync panel):** (1) call Google token revocation endpoint with the refresh token [VERIFY endpoint `https://oauth2.googleapis.com/revoke`]; (2) delete the encrypted token row regardless of revocation result; (3) ask "Also remove the Tether calendar from Google?" with default **no data loss surprises**: choices are *keep my calendar events* (events remain but become unmanaged, we forget the mapping) or *remove everything Tether created* (delete the app calendar). The removal choice needs a still-valid token, so it runs **before** revocation. (4) Items return to `not_synced`, `calendar_events` mapping rows removed, audit entry. Account deletion must run the same path (extends the existing delete/purge job).
- Multi-account: v1 allows one connected Google account per Tether user. Switching account = disconnect then connect. [PO] confirm.

## 4. Idempotent sync design

**Ownership marker.** Every event Tether writes carries `extendedProperties.private` (visible only to our client ID, [VERIFY] private-property scoping, and not to other apps/attendees):
`tether=1`, `tetherItem=<item public id>`, `tetherOcc=<yyyy-MM-dd or "">`, `tetherRev=<item revision synced>`, `tetherFp=<content fingerprint>`. No PII, no document text, no tokens. The Google `description` is minimal and plain: item title, a short "Added by Tether from your syllabus" line, and a link back to the review screen; HTML-escaped at the sink as contract 2.2 requires; no snippet text (it can contain instructor emails/phones).

**Deterministic event IDs.** Client-supplied IDs are supported (https://developers.google.com/workspace/calendar/api/guides/create-events, fetched 2026-10-28, which states client-generated IDs help prevent duplicate creation on partial failure but did not list the format). Format rules are [UNVERIFIED] and recalled from the events reference: lowercase base32hex characters `a-v` and `0-9`, 5 to 1024 characters, unique per calendar, **an ID of a deleted event cannot be reused** [VERIFY, 409]. Proposed ID: `base32hex(HMAC-SHA256(per-calendar-secret, itemId | occurrence | generation))` truncated to 52 chars, where `generation` is an integer on the mapping row that increments if we must recreate after deletion. Create is `events.insert` with that ID; `409 Conflict` means "already exists" -> fetch and treat as update (idempotent retry after a crash). No lookup-then-insert race.

**Reconciliation per occurrence (adapter, given planned intent and stored mapping row):**
| Local state | Remote state | Action |
|---|---|---|
| no mapping | none | insert with deterministic ID; store mapping |
| mapping, fingerprint same | exists, same `tetherFp` | `unchanged` |
| mapping, fingerprint differs | exists, remote **not edited by student** | patch (PATCH, only fields we own) |
| mapping | exists, remote differs from our last-written fingerprint (student edited) | **do not overwrite**; mark `conflict_remote_edited`, see below |
| mapping | 404/410 or `status=cancelled` (student deleted it) | do not recreate silently; record `deleted_remotely` |
| occurrence no longer in plan | exists | delete (only if marker `tether=1` matches) |
| unapproved/skipped/needs-date item | any | delete our events for that item (existing contract rule, 6.2) |

**Student edits in Calendar.** The app owns only events carrying our marker on our calendar. We detect drift by comparing the remote `updated` timestamp and the writable fields we manage to the stored `last_written_fingerprint` and `remote_etag`/`updated`. Default policy (**[PO]** to confirm): **preserve the student's edit**: Tether stops managing that occurrence (`sync_state` for the item shows `changed_since_sync` with a plain note "You changed this event in Google Calendar; we left it alone"), and the student may choose "Replace with the syllabus version" per item. Student moving an event to a different calendar or deleting it is respected, never re-created without a new explicit confirmation. Use `If-Match` with the event ETag on patches [VERIFY] to avoid a lost update race.

**Error handling** (quota guidance, https://developers.google.com/workspace/calendar/api/guides/quota, fetched 2026-10-28: 10,000 requests/min/project, 600/min/user/project, 1,000,000/day billing threshold; time-based quota errors return 403 or 429; use truncated exponential backoff `min(2^n + random_ms<=1000, 32-64s)`; spread traffic, avoid synchronized spikes). The fetched page did not mention `rateLimitExceeded` by that name; treat both 403 and 429 with reasons such as `rateLimitExceeded`/`userRateLimitExceeded` as retryable [VERIFY reasons].
- **429 / 403 rate reason:** honor `Retry-After` if present, else truncated exponential backoff with jitter, max 5 attempts per operation, then mark that occurrence `retry_later` and continue the run; the whole run is `partial`. A per-user token bucket well under 600/min (proposal: 120/min) so a 500-event sync spreads over minutes; the job runs in the background and the UI shows progress, not a spinner lock.
- **403 non-rate (insufficientPermissions, forbidden, calendar not ours):** not retried; connection moves to `needs_reauth` or `scope_missing`; user told how to fix.
- **401 / invalid_grant:** refresh once; on failure `needs_reauth`.
- **404:** on patch/delete of an event, treat the remote as already gone (delete = success; patch = `deleted_remotely`). 404 on the calendar itself: the student deleted our calendar -> `app_calendar_missing`; offer recreate (new generation) only after explicit confirm.
- **410 Gone:** same as 404 for events (deleted); for a sync-token call (not used in v1) a full resync would be required.
- **409:** on insert, existing ID, see above. **412:** ETag mismatch, refetch and re-evaluate once.
- **5xx / network / timeout:** retryable with the same backoff; `provider_unavailable` surfaces as in the mock.
- Caps: reuse contract caps (500 events/call, 1,000/syllabus, per-user). Use batch requests only if measured necessary [VERIFY batch support/limits]; default one request per event keeps failure attribution simple.

**Idempotency layers:** (1) `Idempotency-Key` + fingerprint on the command (existing); (2) deterministic remote IDs; (3) per-occurrence intent rows with state so a crashed run resumes (a lease/heartbeat job like extraction); (4) one active sync run per syllabus (unique partial index). A replayed key returns the stored run without a Google call.

**Time handling.** Send `start.dateTime` with an IANA `timeZone` for timed events (never a bare offset); all-day events use `start.date` and `end.date` where Google's end date is **exclusive** (contract section 35 already flags all-day end-date semantics to align: the adapter converts inclusive `EndDate` to exclusive +1 day; test it). Recurrence: write individual occurrence events (as the mock does, cap-bounded), **not** an RRULE series, so exceptions and per-occurrence edits stay simple. DST edge cases get explicit tests (spring-forward gap, midnight crossing).

## 5. Review-before-write gate

Existing guarantees stay (invariant 4): only `decision == approved && whenStatus == resolved && kind != policy` items are eligible; the sync command re-checks on the locked row and by `revision`; unknown/uncertain dates never reach the adapter. Additions for real Google:
1. **Server-built confirmation preview.** `POST .../calendar-sync/preview` (read-only, no Google call) returns the exact list the sync would write: title, local date/time **with timezone name**, all-day flag, derived-end flag ("1 hour assumed"), reminder settings, target calendar name and Google account email, counts, and items excluded and why. The sync command carries a `previewDigest` (hash of that plan); if the plan changed since the preview, the server rejects with `409 plan_changed` and the UI re-shows. This makes "what was shown is what is written" enforceable.
2. **Explicit confirm control** replaces the mock's `acknowledgedDemo`: the student ticks nothing implicit; the button text states the count ("Add 14 events to the 'Tether - BIO 201' calendar"). Never auto-sync after an edit: an edited approved item becomes `changed_since_sync` and waits for another confirm (consistent with the separation of approve and sync, decision 10).
3. **Undo / remove all.** "Remove from Google Calendar" per item and "Remove everything Tether added" per syllabus/course, using the existing remove route semantics, now deleting remote events by marker; results report `removed / not_present / failed` honestly. A failed remote delete leaves the mapping and item state unchanged and retryable (the mock already does this for `provider_unavailable`).
4. **No background or implicit writes**; no write at OAuth callback time; no write for items the student has not seen at the preview.
5. Audit log entry per run (counts, ids only, no text).

## 6. Data model deltas (list only; coordinate with `database-architect`)

- New schema `integrations`: `google_connections(id, user_id, google_sub, google_email_hash_or_masked, scopes_granted, refresh_token_ciphertext, key_id, key_version, status[connected|needs_reauth|revoked|admin_blocked], connected_at, last_refreshed_at, revoked_at)`; unique (user_id) active.
- `oauth_states(state_hash, user_id, purpose, pkce_verifier_ciphertext, expires_at, consumed_at)` with cleanup job.
- `calendar_targets(id, user_id, connection_id, course_id, remote_calendar_id, name, created_at, deleted_remotely_at)`.
- Extend `syllabus.calendar_events`: `provider` CHECK widened beyond `'mock'`, add `remote_event_id`, `remote_calendar_id`/`target_id`, `generation`, `last_written_fingerprint`, `remote_etag`, `remote_updated_at`, `remote_status[active|deleted_remotely|conflict_remote_edited|retry_later|pending]`, `last_error_code`, `last_attempt_at`. Unique (target_id, remote_event_id).
- Extend `sync_runs`: `provider`, states `planned|executing|completed`, `preview_digest`; extend `sync_run_items` with per-occurrence intents (or new `sync_intents`).
- `items.sync_state`: consider additional values (`conflict_remote_edited`, `deleted_remotely`) per contract additive rules; otherwise carry in `calendar_events.remote_status` and map to existing states.
- Reminder preferences: `calendar_prefs(user_id, reminders_enabled, minutes_before[])`, default off.
- Deletion/export: token rows excluded from export, purged on account deletion; retention for `sync_intents`.
- Contract artifacts: `calendar.v1` schemas, fixtures, `Tether.ContractTests` additions.

## 7. Failure, degraded states, neurodivergent-friendly defaults

States the UI must express in plain, blame-free language (`neurodivergent-ux-specialist` to own copy): not connected; connecting; connected (account + calendar shown); needs reconnect; Google unavailable (retry later, your plan is safe here); partly synced ("11 of 14 added; 3 will retry"); event edited in Google (left alone); calendar deleted in Google; quota slowdown ("this is taking a bit longer"). The syllabus plan and review keep working fully without Google. Never block review, approval or reading on Calendar health. Errors never show raw Google messages or tokens.

Reminder defaults (**proposal, [PO]/neurodivergent-ux to confirm**): **no reminders by default; explicit opt-in** in the confirm step. Calendar event reminders default to the calendar's own settings otherwise, so set `reminders.useDefault=false` with an empty override list on creation to avoid surprise notifications from the student's defaults [VERIFY behavior on a secondary calendar]. If opted in: at most two, e.g. 1 day before for exams/assignments (timed ones add 1 hour), none for lectures/readings, never nagging repeats, never a "you are late" notification, and a single "turn all Tether reminders off" control. All-day items default to the evening before, not midnight. Quiet mode (invariant 7) suppresses nothing in Google itself, so the UI says reminders live in Google Calendar.

## 8. Test plan (nothing run yet)

1. **Unit/property tests** on `Build` and the new mapper: ID generation (charset, length, determinism, generation bump), all-day exclusive end, DST gap/overlap, midnight crossing, derived 60-minute end, fingerprint stability, HTML escaping in description, no snippet in description.
2. **Adapter contract tests against a mock `ICalendarGateway`** reused by both `MockCalendar` and `GoogleCalendarGateway`: create/update/unchanged/delete, remote-edit conflict, deleted-remotely, partial failure, replay.
3. **Fake Google HTTP server** (in-process Kestrel or WireMock-style container in `tests/`): implements OAuth token/revoke and Calendar `calendars.insert/delete`, `events.insert/patch/delete/get` with real-shaped JSON and errors; injectable 401, 403 (insufficient and rate reasons), 404, 409, 410, 412, 429 with `Retry-After`, 5xx, timeouts, `invalid_grant`, partial scope grant, ID-reuse-after-delete. Assert: backoff schedule (fake clock), max attempts, no retry on non-retryable, resume after simulated crash between execute and record, no duplicate events after replays, tokens never in logs (canary), token not in any AI prompt, state/PKCE tamper rejection, redirect URI exact match, encrypted-at-rest check (DB column does not contain plaintext), AAD binding (row moved between users fails).
4. **Security tests:** scope allowlist test (only `calendar.app.created` + identity scopes may be requested); cross-user access (user B cannot sync, preview or disconnect for user A; 404 not 403); CSRF on connect/callback; architecture test (AI assembly cannot reference Calendar).
5. **Documented real-account smoke test (not run; needs PO approval):** use a **throwaway Google account created for testing, not a student's or personal account**, synthetic syllabus only. Cloud project in Testing status with that account as test user (refresh tokens expire after 7 days there). Steps: connect -> preview -> confirm 3 events (timed, all-day, midnight-crossing) -> verify in Google UI -> run sync again (expect `unchanged`, no duplicates) -> edit one event in Google -> re-sync (expect left alone) -> delete one in Google -> re-sync (expect `deleted_remotely`) -> revoke from the Google account page -> expect `needs_reauth` -> reconnect -> remove everything -> verify calendar empty/deleted -> disconnect. Record account used, date, commit SHA, quota usage, and observed deviations from [VERIFY] items. Evidence goes in `docs/qa/`; it does not claim production readiness.
6. Human gates stay separate: screen-reader and real-device QA of the connect and confirm screens (`device-accessibility-validation`).

## 9. Google OAuth app verification and consent screen

(All [UNVERIFIED] against current Google policy pages; `vendor-verification-researcher` to confirm with dated sources before any submission. Candidates: https://support.google.com/cloud/answer/13463073, https://developers.google.com/identity/protocols/oauth2/production-readiness/sensitive-scope-verification, https://developers.google.com/terms/api-services-user-data-policy.)
- Cloud project, OAuth consent screen (external user type), app name, logo, support email, authorized domains, privacy policy URL and terms URL on a verified domain, exact-match redirect URIs, HTTPS in production.
- Calendar scopes may be classified sensitive (and `calendar.app.created` may differ from `calendar`); the Cloud Console scope picker shows the real classification. If sensitive: verification review, justification, demo video, and possible delay; if restricted: security assessment (unlikely for Calendar, verify).
- Until verified: "Testing" status, up to a capped list of test users, "unverified app" warning, **7-day refresh token expiry**. Fine for the smoke test; unusable for real students. Production use of real student accounts is blocked until verification and the legal gate; this plan does not change that.
- Compliance: Google API Services User Data Policy "limited use" statements (no use of Calendar data for ads, no transfer to AI training; our events data is app-created, so low exposure); privacy policy must describe Google data, retention and deletion; an annual re-verification may apply [UNVERIFIED].
- Age/region: PO-7 (minimum age) interacts with Google account age rules.

## 10. Ordered work breakdown

| # | Step | Owner | PO approval? |
|---|---|---|---|
| 1 | PO decisions on section 11 (scope, per-course calendar, edit policy, reminders) | Product Owner | **Yes** |
| 2 | Verify scope/ID/quota/verification facts from dated primary sources; resolve [UNVERIFIED] list | vendor-verification-researcher | No |
| 3 | `calendar.v1` contract draft (preview, sync, remove, connection status, error codes) + ADR superseding ADR-012 for Calendar | Lead Architect, backend-engineer | Contract freeze: yes |
| 4 | Threat model for OAuth + token store + remote writes; key management design | security-engineer, devops-engineer | Key service choice: **yes** (cost, PO-6) |
| 5 | Data model migration for section 6 | database-architect | No |
| 6 | Consent, connect, confirm-preview, partial/failed state UX and copy | neurodivergent-ux-specialist, frontend-engineer | No |
| 7 | `ICalendarGateway` extraction, plan/execute/record refactor, `MockCalendar` conformance, architecture tests | backend-engineer | No |
| 8 | OAuth endpoints, encrypted token store, revoke/disconnect, state/PKCE | google-integration, backend-engineer | No (still no real Google client) |
| 9 | Google adapter against fake Google server; contract + failure tests | google-integration, qa-engineer | No |
| 10 | Security re-review of implemented code | security-engineer | No |
| 11 | Create Google Cloud project/OAuth client in Testing status, throwaway test account | Product Owner (account/billing/ownership), google-integration | **Yes** (external permission, PO-6) |
| 12 | Real-account smoke test per section 8.5, evidence recorded | google-integration, qa-engineer | **Yes** (external write) |
| 13 | Legal review of Google as processor/recipient (phase2 legal brief line 126), privacy policy, consent text | legal-compliance-reviewer, counsel | **Yes** |
| 14 | OAuth verification submission, domain, privacy policy | Product Owner, google-integration | **Yes** |
| 15 | Device/screen-reader QA of connect/confirm; production readiness audit (secrets, monitoring, quota alerts) | device-accessibility-validation, production-readiness-auditor | No |
| 16 | Go/no-go for real students | Product Owner | **Yes** |

Steps 1-10 are local and consistent with "approved safe work while gates are open". Step 11 onward touches external systems.

## 11. Open questions for the Product Owner

1. Approve `calendar.app.created` only (dedicated "Tether" calendar, no access to other events, no conflict detection against the student's events)? Or is seeing/avoiding conflicts with existing events a requirement (needs broader read scope and heavier verification)?
2. One calendar per course (recommended) or one for all courses?
3. When a student edits or deletes a Tether event in Google: leave it alone and say so (recommended), or overwrite on next sync?
4. Reminders: off by default and opt-in (recommended)? Any default for exams only?
5. Who owns the Google Cloud project, the domain, privacy policy and billing/verification contact (PO-6)? Is a throwaway test Google account acceptable for the smoke test?
6. Token key management: cloud KMS/vault (cost) versus ASP.NET Data Protection keyring for the first pilot (PO-6)?
7. One connected Google account per user, and may it differ from the sign-in account?
8. Disconnect default: keep events in Google or remove them?
9. Is Calendar sync in scope for the first real-student pilot, or does the pilot stay mock/ICS-style until legal (G1) and OAuth verification clear? Is ICS export (deferred in contract 15) an acceptable interim?
10. Accept that until OAuth verification completes, only listed test users can connect (with 7-day token expiry)?

## Key decisions needed from the Product Owner (summary)
Scope choice (`calendar.app.created` only) and whether conflict detection is required; per-course calendar; student-edit policy; reminder default (off/opt-in); Google Cloud ownership, test account and key-management choice (PO-6); disconnect default; and whether real-student Calendar sync waits for legal clearance and OAuth verification.
