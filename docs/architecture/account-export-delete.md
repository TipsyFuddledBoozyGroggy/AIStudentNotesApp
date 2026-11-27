# ADR-021 (proposed): "Download a copy of your data" and "Delete everything"

**Status: PROPOSED, 2026-11-27, architect.** Nothing here is built. The Lead allocates the ADR number (the last one in `docs/DECISIONS.md` is ADR-020d) and owns the contract text once accepted. Scope: export-all and account erasure for a signed-in user with an ADR-016 server-side session. Google sign-in itself is designed by the senior pair in parallel; section 9 lists what this design needs from it.

Drivers: PO-20ax (both must be built and tested before any other user or any real data), PO-20cg (invite-only Google sign-in next), PO-20aw (content-free evidence stays on lecture/course delete and goes on account erasure), PO-15c (nothing in the student's Google account is deleted by Tether, including on account deletion), PO-9n/PO-10n (Google tokens deleted), PO-10m (the "also remove from Google" choice is for item deletes only, refined by PO-15c), PO-20by (delete-everything is two steps and a sign-out, no typing; strings approved as DRAFT only), invariants 1, 5 and 8.

Legend: **[V]** verified by reading the code or config at the path given (no test was run for this document); **[A]** assumption the builders must confirm.

## 1. Inventory: what exists

| Area | What exists | Where |
|---|---|---|
| User row | `identity.users` with a `deleted_at` column that nothing sets; unique indexes on `email` and `external_subject` [V] | `Identity/Domain/User.cs`, `M001_Users.cs:35-47` |
| Login sessions | `identity.sessions`, FK to users ON DELETE CASCADE; `SessionPolicy.IsValidAsync` rejects revoked, expired **and soft-deleted users** on every request and on hub/socket revalidation [V] | `M003_Sessions.cs`, `SessionPolicy.cs:25-45` |
| Live-connection cut-off | Logout revokes one session, then calls every `IAuthSessionRevocationObserver`; Transcript's observer aborts hub connections and closes audio sockets (4401) for that login session [V] | `IdentityEndpoints.cs:52-66`, `Transcript/Application/Observer.cs:36-43` |
| User cascade (ADR-014/019/020) | FKs to `identity.users` ON DELETE CASCADE: `courses.courses`, `lectures.lecture_sessions`, `ai.ai_usage`, `docs.audit_events`, `syllabus.idempotency_keys`; everything else hangs off sessions, courses or syllabi with composite (id, user_id) cascades [V] | `M004_CoursesUserFk.cs`, `M006_LecturesIntegrity.cs:27`, `A001_Ai.cs:150`, `D001_Docs.cs:119`, `S002_SyllabusIntegrity.cs:23-28` |
| Evidence purge | `lectures.purge_evidence(user)` (audit_events, consent_events, then consent_records; fails while the user still has sessions, so it must run **after** the user delete) and `syllabus.purge_evidence(user)` (decision_audit, creation_counters). Both are append-only-trigger bypasses scoped to the transaction. **No production caller** (findings register NEW-01) [V] | `M006_LecturesIntegrity.cs:79-88`, `S002_SyllabusIntegrity.cs:76-83` |
| Tables with no user FK by design | `lectures.consent_records`, `consent_events`, `audit_events`; `syllabus.decision_audit`, `creation_counters`; `ai.global_spend` (no owner at all: the monthly cap) [V] | sweep test allow-lists `DeletionAndExportSweepTests.cs:28-41`, `A001_Ai.cs:131` |
| Session delete | Writes consent Stop + audit if live, removes the session (cascade), retries on deadlock, then `NotifyDeletedAsync` closes sockets and hubs [V] | `SessionService.cs:333-370`, `Observer.cs:23-28` |
| Course delete | Archive or `?purge=true`; `ICourseLifecycleHandler` stops live sessions before, and cuts late joiners after commit (NAS-01). ADR-020b: user deletion "must use the same path" [V] | `CourseEndpoints.cs:124-210`, `ICourseLifecycleHandler.cs` |
| Syllabus | `DELETE /syllabi/{id}` hard-deletes; retention job purges canonical text at 180 days from last student write, 365 absolute, 7-day debris, audit at 400 days (proposal) [V] | `Syllabus/Application/Jobs.cs:252-360`, `API_CONTRACTS_P2.md` |
| Exports today | `GET /syllabi/{id}/export` (JSON, owner-scoped, CSRF guard on Fetch Metadata, 6/min); client-only Markdown notes file for one lecture; Demo Doc export to an in-database stand-in (`docs.demo_docs`) [V] | `API_CONTRACTS_P2.md:143,219-220`, `web/.../lecture/notesExport.ts`, `API_CONTRACTS_DOCS_EXPORT.md` |
| Sweep test | Discovers every table; after `delete from identity.users` plus the two purge calls (run as separate statements), nothing mentions the user; a planted table without a cascade fails the sweep [V, not run] | `DeletionAndExportSweepTests.cs:519-596` |
| UX | DRAFT strings for "Delete everything" (sheet, then dialog, then signed-out page) and the "Your data" section; Q-B1 asks for one transaction [V] | `docs/ux/delete-wording-draft.md` 5.4-6.3 |
| Infra facts | API max 1 replica; Postgres PITR backups 7 days; Log Analytics 30 days [V] | `infra/main.bicep:252,207,406` |

**Missing:** any endpoint for export-all or erasure; a production caller of the purge functions; one-transaction erasure; a user-wide connection cut-off; crash recovery; a Settings / "Your data" page (no route exists, `src/web/src/app/`); the sweep's "every schema" list omits `docs` and the erasure fixture never creates a Demo Doc, so docs tables are not proven populated before the "gone" check; the test probe `GET /me/export` must change when the export route exists. No Google token storage exists (sweep asserts no `integrations` schema and no secret-like columns) [V].

## 2. Decision summary

1. **Export is synchronous and streamed**: `POST /api/v1/me/export` returns a ZIP built on the fly from one read-only snapshot. Nothing is written to disk or blob storage. A background job is deferred until a real account hits the limits (section 3.4).
2. **Erasure is immediate, in two committed steps with a durable marker**: step A locks the account out and records an erasure-queue row; step B deletes everything in **one transaction** (user cascade + both purge functions + module participants). Step B runs inline in the request and again from a worker until done, so a crash cannot leave a half-deleted account visible to anyone.
3. **No typing to confirm.** Server-enforced confirmation body, Origin check, a live-lecture refusal, and a **recent sign-in** (10 minutes) requirement for erasure only (section 6; PO question 1).
4. Modules contribute through two SharedKernel ports, so Identity never learns another module's tables (backend-foundation module rule; precedent: `ISessionSequencer.AllocateAsync(DbTransaction, ...)` in ADR-020).

```
public interface IAccountDataExporter      { string Module { get; } Task WriteAsync(DbTransaction tx, Guid userId, IExportSink sink, CancellationToken ct); }
public interface IAccountErasureParticipant { int Order { get; } Task OnErasingAsync(Guid userId, CancellationToken ct);            // after step A: stop live work; idempotent; must not throw
                                              Task PurgeAsync(DbTransaction tx, Guid userId, CancellationToken ct); }           // inside step B, after the user delete
```

## 3. Export: "Download a copy of your data"

### 3.1 What each module contributes (all rows filtered by the server-side user id; no id comes from the client)

| Module | Included | Provenance kept (invariant 5) |
|---|---|---|
| Identity | display name, email, account created time, export time; content-free account events (section 6.4) | `createdAt` |
| Courses | every course incl. archived ("hidden") ones: name, code, term, instructor, colour | `createdAt`, `updatedAt`, `archivedAt` |
| Lectures | each session: title, state, started/stopped, `demoSource`; consent records and Start/Pause/Resume/Stop events (versions and times, no text); lecture audit actions | consent `textVersion`, event times |
| Transcript | final segments and corrections in seq order, gaps with reason; corrections keep the original final | `seq`, `segmentNo`, `startMs`/`endMs`, `provider`, `confidence`, `occurredAt`, `supersedes` |
| AI | every TL;DR/summary revision (Draft and Final), Quick recaps, notes (sections with citations) | `revision`, `basedOnSeq`, source seq range, covered ms range, citations to segment ids and times, `model`, `promptVersion`, `noticeVersion`, `createdAt`; each item labelled **"made by AI from your transcript"** |
| AI usage | ledger rows (workflow, tokens, cost estimate, outcome, time) | `model`, `promptVersion` |
| Syllabus | per syllabus the existing `syllabus-export` shape (items, decisions, snippets, unclaimed dates, mock calendar events) **plus the canonical text if not yet purged**; decision audit | snippet offsets, `aiSource`, extractor version, decisions and times; purged text shown as "removed on {date} by the 180-day rule" |
| Docs | Demo Docs (blocks), export attempts and outcomes, docs audit | `origin_export_id`, `content_digest`, notes revision |
| Calendar | nothing separate: only the mock `syllabus.calendar_events`, already in Syllabus. No real Calendar exists [V] | n/a |

**Deliberately excluded, and why:** login sessions and audio tickets (bearer-secret material and hashes); idempotency keys and fingerprint HMACs (internal, keyed); `session_counters`, `audio_progress`, extraction jobs, sync runs, budget reservations (operational state, no meaning to the student); AI proposal history `item_proposals`/`item_alternatives` (the current items and decisions are included; revisit if "Reset to what we found" ships); `ai.global_spend` (not the student's); audio (never stored [A: confirm in speech pipeline]); partial transcript segments (never persisted); anything at a vendor or in the student's Google account (Tether does not hold it). The README in the ZIP lists these in plain words.

**Classification is enforced by a test, not by memory:** an architecture test discovers every table (like `DataSweep`) and fails unless each table is in exactly one of `exported` or `excludedWithReason`. A new table therefore cannot silently miss the export.

### 3.2 Format: one ZIP, readable part plus machine part

```
tether-data-2026-11-27.zip
  README.txt                 what this is, how to open it, what is not included, export time, format version
  index.html                 your courses and lectures, links to the pages below
  courses/c01-<slug>/l001-2026-10-14-<slug>.html   one page per lecture: notes, final summary, recaps, transcript with [mm:ss], gaps as "audio unavailable"
  courses/c01-<slug>/syllabus-1.html               items, dates, your decisions, the text Tether read
  records.html               when you recorded, consent versions, how much AI was used
  data/account.json  data/courses.json  data/lectures/<sessionId>.json  data/syllabi/<syllabusId>.json  data/docs.json  data/records.json
  manifest.json              written LAST: format "tether.export.v1", generatedAt, counts per kind, SHA-256 and size per file, "complete": true
```

- **Readable part is HTML**, because headings and lists work with screen readers and it opens in any browser. Every student, AI and syllabus string is HTML-encoded on the server and passed through the same control/bidi stripping as the screen (`plainText`, C-28). Each page has `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">`, no script, no external resource, no generated link except relative links between pages. URLs inside AI text or snippets stay plain text (avoids the DXB-01 auto-link problem). Calm layout: one column, large line height, the redesign's reading-room type scale inlined, no colour-only meaning, timestamps as text.
- **File and folder names are generated** (`c01`, `l001`, date, ASCII slug `[a-z0-9-]{0,40}`); a student title never becomes a raw path (no zip-slip, no reserved Windows names).
- **Machine part is JSON**, camelCase, ids in their wire form, UTC ISO-8601 times, `additionalProperties: false` schemas in `src/shared/contracts/account.v1/`. A maximal-data export must validate.
- Rejected alternatives: one giant JSON (unreadable for the student); Markdown (auto-linking and viewer differences, DXB-01); PDF (heavy dependency, poor accessibility without tagging work).

### 3.3 Sync, consistency, delivery

- One `REPEATABLE READ, READ ONLY` transaction shared by all exporters (one connection; module DbContexts join it), so the ZIP is a consistent snapshot. A lecture that is recording is exported up to that moment.
- The response streams: headers first (`200`, `Content-Type: application/zip`, `Content-Disposition: attachment; filename="tether-data-YYYY-MM-DD.zip"`, `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`), then entries as they are produced, so the 240 s ingress idle timeout is not hit [A: async ZIP writing on a non-seekable stream in .NET 10; else buffer in memory under the cap].
- The web client calls it with a same-origin `fetch` (POST, so the existing Origin middleware applies), receives a Blob and saves it. A stream cut mid-way makes `fetch` fail, and a ZIP without `manifest.json` is incomplete by definition; the client then says "The download did not finish. Nothing was changed. Try again."

### 3.4 Limits (proposals)

| Limit | Value | Behaviour |
|---|---|---|
| Pre-check before any byte is sent | at most 200 MB of stored text and 20,000 transcript segments per lecture x lectures, computed by `count`/`sum(length)` in the snapshot | over: `413 export_too_large`, nothing streamed. Escape hatch = a later background job (v2), not built |
| Wall time | 120 s server budget | over: stream aborted, the client shows the retry line |
| Rate | 3 per hour per user, 1 in flight per user, 2 in flight globally (single replica) | `429 rate_limited` with `Retry-After` |
| Expected size [A] | a semester of 40 one-hour lectures is roughly 5-15 MB uncompressed | well inside the cap |

## 4. Delete everything: semantics and order

### 4.1 The sequence

**Preconditions (checked in this order, nothing written on failure):** signed in (401); Origin allowed (403 `csrf_failed`); body is exactly `{"confirm":{"deleteEverything":true}}` (400); the current login session started within the last 10 minutes (403 `reauth_required`); no lecture is Active or Paused (409 `invalid_state`, "Stop the lecture first"). Same rule as the course: Delete is hidden while recording (PO-20by).

**Step A, one short transaction in Identity:** set `users.deleted_at = now`; set `revoked_at` on **all** the user's login sessions and return their ids; insert `identity.erasure_queue (user_id PK, requested_at, next_pass_at = now, passes = 0)`. This table has **no FK to users on purpose**: it must outlive the user row. From commit on, `SessionPolicy` refuses every request, hub join and socket revalidation for this user [V: the `deleted_at` check exists].

**After A commits (best effort, never blocks):** call every `IAuthSessionRevocationObserver` for each revoked session id (existing path: hubs aborted, sockets closed with 4401 on every device); then each participant's `OnErasingAsync`: Lectures stops any session that raced in after the precheck (the ADR-020b stop path) and forgets in-memory activity; AI cancels queued or running jobs for the user [A: needs a cancel hook; late writers already fail on the FKs]; Syllabus cancels extraction jobs (existing late-writer re-check).

**Step B, one transaction (pass 1 runs inline in the request; later passes from the worker):**
1. `DELETE FROM identity.users WHERE id = @u` (no-op if already gone). Cascades: login sessions, courses, lecture sessions and every child, transcript, AI revisions/recaps/notes, `ai.ai_usage`, syllabi and every child, docs rows, docs audit.
2. Participants' `PurgeAsync` in `Order`: Lectures `select lectures.purge_evidence(@u)`; Syllabus `select syllabus.purge_evidence(@u)`; a future Integrations participant deletes token rows (section 4.3).
3. `UPDATE identity.erasure_queue SET passes = passes + 1, next_pass_at = now + 10 min`.

Deadlock or serialization failure (in-flight writers racing the cascade, as in `SessionService.DeleteAsync`): roll back, retry up to 3 times with jitter, then leave it to the worker.

**Worker** (`BackgroundService` in Identity, every 60 s and at start-up): for queue rows with `next_pass_at <= now`, run step B. After **two** successful passes, delete the queue row. Pass 2 exists because the five FK-less evidence tables could receive a row from a request that passed authentication just before step A; both purge functions are idempotent, so repeating is safe.

### 4.2 Idempotency and crash safety

| Failure | Result |
|---|---|
| Crash before A commits | nothing happened; the student is still signed in and can retry |
| Crash after A, before B | locked out; the worker finishes within about a minute of restart |
| B fails 3 times | the request answers 202 `deleting`; the worker retries; a queue row older than 1 hour logs `erasure.stalled` (content free) for the operator |
| Response lost, client retries | the session is revoked, so the retry gets 401. The client treats a 401 **after pressing Delete** as "account closed" and shows the deleted page (section 7) |
| Two tabs press Delete | step A takes the user row lock; the second sees the session revoked (401) |

### 4.3 What stays, and Google

- **In Tether's database: nothing about the user** after pass 2. PO-20aw: consent, audit and AI-usage rows are removed on full erasure. The only survivor is `ai.global_spend`, a monthly total with no user in it, so the $5 monthly cap is not refunded. **Consequence:** the per-user daily AI cap and the 10-per-day syllabus counter reset for a new account; acceptable while invite-only, revisit for open sign-up.
- **Backups:** PITR keeps up to 7 days [V]; a restore to an earlier point would bring an erased account back. Mitigation: at completion, log `erasure.completed` with `subjectHmac = HMAC-SHA256(DerivedKeys "erasure-ledger", userId)`, never the raw id; the restore runbook (to be written with the G5 restore drill) re-erases any restored user whose HMAC appears in the last 8 days of logs. No backup promise appears in copy until then (L-08).
- **Google (PO-15c):** nothing in the student's Google account is touched; no "also remove from Google" choice is offered on account erasure. This supersedes the "same choice offered" cell in `docs/security/google-tokens-and-data-residency.md:194`. **Tokens (PO-9n/PO-10n):** none are stored today [V]. When a Google data connection exists, its participant calls Google's revocation endpoint best effort and deletes the token rows in step B **whatever the revocation outcome**. Revoking access never deletes Google content.
- **Vendors:** mock only today. When real Speech or Claude are on, their retention is outside Tether's control and stays UNVERIFIED in copy (PO-20bz).

### 4.4 Re-sign-in afterwards

After pass 1 the user row is gone, so a Google sign-in with the same `sub` creates a **new, empty account with a new id**; the unique `email`/`external_subject` indexes no longer collide. Between A and pass 1 (normally under a second), sign-in must refuse calmly: "This account is being deleted. Try again in a minute." Pass 2 uses the old id, so it can never touch the new account.

## 5. Versioned API contract `account.v1` (proposed; the Lead freezes it as `docs/API_CONTRACTS_ACCOUNT.md`)

Conventions: `docs/API_CONTRACTS_P2.md` section 2 (base `/api/v1`, cookie plus exact Origin on unsafe methods, RFC 9457 problems with a stable `code`, `additionalProperties: false`). No route takes a user id.

| Method | Path | Request | Bucket | Success | Errors |
|---|---|---|---|---|---|
| GET | `/me/data-summary` | | read | 200 `account-data-summary` | 401, 429 |
| POST | `/me/export` | `{}` | `account-export` (3/h, 1 in flight) | 200 `application/zip` (section 3.2) | 401, 403 `csrf_failed`, 413 `export_too_large`, 415, 429 |
| POST | `/me/erasure` | `account-erasure-request` | `account-erasure` (3 per 10 min) | 200 `{state:"deleted"}` or 202 `{state:"deleting"}`; both clear the cookie | 400 `validation_failed`, 401, 403 `csrf_failed` / `reauth_required`, 409 `invalid_state`, 415, 429 |

```
account-data-summary:   { courses, lectures, syllabi, demoDocs: int, liveLecture: { sessionId, courseId } | null,
                          signInFreshUntil: date-time | null, lastExportAt: date-time | null, googleCopiesPossible: false }
account-erasure-request:{ confirm: { deleteEverything: true } }      (const true; anything else is 400)
```

New codes `reauth_required` and `export_too_large` are additive in `v1`. `googleCopiesPossible` is the `const false` until a real Google provider ships, and drives the conditional "What stays" line. The probe `GET /me/export` in the sweep test becomes 405; the test is updated, not deleted.

## 6. Security

1. **Ownership (invariant 1):** the user id comes only from the validated session; exporters and participants receive it as a parameter and every query filters on it. Test: two users with overlapping data; Bob's canaries never appear in Alice's ZIP.
2. **CSRF:** both POSTs pass the existing `OriginCheckMiddleware`; the export also applies the syllabus Fetch-Metadata rule (`Sec-Fetch-Site` must be `same-origin` or `none`). The erasure body constant stops a bare form post.
3. **Recent sign-in for erasure only:** the current login session's `created_at` must be within 10 minutes. With Google this is one click on the account chooser, no typing, and it defeats a stolen cookie or a laptop left open. Export does not need it: anyone holding the cookie can already read everything through the API.
4. **Audit, content free:** `identity.account_events (id, user_id FK cascade, action export|erasure_requested, at, counts jsonb)`; removed with the account. Logs: `account.export` and `erasure.*` with counts, bytes, duration and outcome only; **no user id, email, title or text**; exception logging is type-only as elsewhere. A canary test with fault injection checks the logs.
5. **No secrets in the ZIP:** test asserts no session id, ticket hash, idempotency hash, cookie or token-like string, and no other user's id.
6. **Rate limits** as in section 5, in their own buckets so a large export never throttles a live lecture's Stop (ADR-019b).

## 7. UI entry points and copy

- **Where:** a new `/settings` page linked from `AppShell` next to "Sign out", with the "Your data" section from `delete-wording-draft.md` 5.4: "Download a copy of your data" (`.btn-secondary`) and "Delete everything" (`.btn-danger`, never filled). Also link "Your data" from the course page's existing section.
- **Download:** busy label "Preparing your copy...", then "Your copy is downloading." A calm one-liner for each failure, always ending "Nothing was changed." No progress bar that can stall.
- **Delete:** step 1 is the `.sheet` from 5.4 (Keep my account focused; add "your Demo Docs" to the list and "Want a copy first? Download your data."); if `signInFreshUntil` has passed, Continue says "Sign in with Google again to continue" and returns to step 2 (sign-in pair supplies the return path); step 2 is the `ConfirmDialog` "Delete everything and sign out?"; then the public signed-out page "Your Tether data was deleted." For a 202: "Your account is closed. Tether is finishing the delete. You do not need to do anything." (the 6.3 line, now a promise the worker keeps). While a lecture records, the Delete button is replaced by "Stop your lecture first." with a link.
- Copy changes beyond the approved DRAFT strings (Demo Docs line, re-sign-in line, 202 line) need the PO's approval; the consent text is untouched; no backup or vendor-retention promise (L-08, PO-20bz).

## 8. Test plan

- **Seeded test account only (pilot-plan F6):** a synthetic seed (extends the demo seed from finish-backlog item 1) with two courses, an archived course, a stopped lecture with notes, recaps and a Demo Doc, a live lecture, a syllabus with decisions and mock events, and canary strings including `<script>`, `javascript:` links, bidi controls and a title like `../../x`. Never real notes; the deployed POC check uses the same seed.
- **Contract:** `account.v1` schemas and fixtures; a maximal export validates file by file; manifest hashes match.
- **Export integration:** every populated table appears or is excluded with a reason (discovery test); owner scoping; HTML escaping and the CSP meta; generated names only; 413 before any byte; 429; 401; 403 for a foreign Origin and cross-site Fetch Metadata; snapshot consistency while a lecture writes; an aborted stream leaves no temp file.
- **Erasure integration (extend `DeletionAndExportSweepTests`):** through the endpoint, not raw SQL; the fixture also creates a Demo Doc; `docs` joins the schema list; after the response and pass 2 the sweep finds nothing and no canary anywhere (only `ai.global_spend` allowed); hub and audio socket on two login sessions close with 4401; 409 when live; 403 `reauth_required` on a 15-minute-old session; fault injection between A and B and inside B (the worker completes); deadlock retry under concurrent transcript writes; a late FK-less row inserted between passes is removed by pass 2; retry gets 401; re-sign-in with the same subject gives a new empty account; the other user is byte-identical before and after; no outbound HTTP (PO-15c on the mock stack); log canary.
- **Web:** vitest for the settings page, sheet, dialog (focus, Escape keeps, busy, failures), signed-out page; Playwright end to end on the seed; axe checks. Screen-reader and device checks stay G3 manual items.

## 9. Dependencies on the Google sign-in design

(1) A safe same-origin `returnTo` so "sign in again" lands back on step 2. (2) Sign-in refuses a user whose `deleted_at` is set, and otherwise creates a new user when no row has that `sub`. (3) Sign-in stores no Google tokens; if it does, it adds an erasure participant. (4) If a Tether-side invite list holding emails is added, the PO decides whether erasure removes the invite (question 3). (5) Dev login keeps working in tests so the 10-minute rule can be exercised.

## 10. Files (proposed), build order, size, risk

**Files:** `docs/API_CONTRACTS_ACCOUNT.md`, `src/shared/contracts/account.v1/` (schemas, fixtures); SharedKernel: the two ports and `IExportSink`; Identity: `Account/AccountEndpoints.cs`, `Account/ErasureService.cs`, `Account/ErasureWorker.cs`, `Account/ExportArchiveWriter.cs` (ZIP and HTML), migration `M00x_AccountErasure` (`erasure_queue`, `account_events`); one exporter per module (Courses, Lectures, Transcript, Ai, Syllabus reusing its export DTOs, Docs); erasure participants in Lectures, Syllabus, Ai; Transcript reuses its revocation observer. Web: `app/settings/page.tsx`, `app/deleted/page.tsx`, `components/account/{YourData,DeleteEverything,DownloadData}.tsx`, `deleteCopy.ts`, `lib/api.ts`, `AppShell.tsx`. Tests: `AccountErasureTests.cs`, `AccountExportTests.cs`, updates to `DeletionAndExportSweepTests.cs`, `AccountContractTests.cs`, an architecture test for table classification and module boundaries, web tests.

**Build order:** (1) Lead accepts this ADR and freezes `account.v1`. (2) Erasure backend with the sweep extension; it is the riskier half and the sweep already exists. (3) Export backend. (4) Web, both flows. (5) QA on the seeded account, then the senior review and the Lead's integration. The sign-in pair's items in section 9 are needed before (4) ships but not before (2).

**Rough size:** erasure backend 4-5 engineer-days; export backend 5-7; web 3; QA and fixes 3. About three weeks for the pair at a steady pace.

**Riskiest part:** completeness of erasure over time. Every new table or module (Google sign-in, real Docs, Calendar, integrations) must either cascade from the user or join as a participant, and the FK-less evidence tables are the gap. The discovery-based sweep through the real endpoint, run in CI, is the control; it must never be weakened to pass. Second: HTML built from AI and student text, mitigated by encoding plus CSP plus the canary fixture.

**What would change these decisions:** a real account near the 200 MB cap or the 120 s budget (move export to a background job with a short-lived, owner-scoped download); multi-replica hosting (the worker needs a lease, ADR-020d); legal advice requiring retention of consent evidence after erasure (that would reverse PO-20aw for erasure).

## 11. Open questions for the PO (plain words)

1. **Sign in again before deleting?** Before "Delete everything", should Tether ask the student to sign in with Google again (usually one click), so someone using a laptop left open cannot delete the account? Recommended: yes. If no, the steps stay exactly as approved: a page, a dialog, then signed out.
2. **Coming back after deleting.** A student who deletes their account can sign in again later and gets a new, empty account, because they are still on your Google test-user list. Is that what you want, or should deleting also mean they need a new invite from you?
3. **New wording.** Three new lines are needed beyond the approved draft: Demo Docs in the "what gets deleted" list, "Sign in with Google again to continue", and "Your account is closed. Tether is finishing the delete." OK to use them as DRAFT?
4. **Closed Google accounts (PO-9n).** If a student's Google account is closed, they cannot sign in to delete anything themselves. Should you have a simple way (a script you run) to delete that student's Tether data, and after how many days?
