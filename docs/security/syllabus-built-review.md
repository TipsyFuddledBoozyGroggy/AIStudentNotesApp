# Syllabus feature as built: independent security review (mock only)

Reviewer: security-engineer. Date: 2026-10-31. HEAD `d65ffaa`, working tree clean at start. Severity per `docs/security/severity-policy.md`. ID prefix **SB-**.

**Method: read only.** I read the code, tests, migrations, contract and prior reviews, and used grep. I ran **no** test, build, migration, probe or app (the dev API and a dev server were in use by another agent, and the Lead asked for read-only). Every statement below is "READ" unless marked otherwise. Nothing in `src/` or `tests/` was changed.

Scope: `src/api/src/Modules/Syllabus/Tether.Syllabus` (SyllabusEndpoints, SyllabusService, ItemService, SyncService and MockCalendar, UploadIntake, Jobs, Support, SyllabusCore and Sql, TextPolicy, DocumentText, MockRuleExtractor, SyllabusScopeProbe, SyllabusModule, Dtos, migrations S001/S002), `Tether.Host/Program.cs` and `HostServices.cs` (limiter, headers, Origin), `docs/API_CONTRACTS_P2.md`, the syllabus.v1 schema tests, and the web client (`src/web/src/components/syllabus`, `src/web/src/lib/syllabus`, `src/web/src/mocks/syllabus*`) for upload, delete and export.

## Result

**0 CRITICAL, 0 HIGH, 1 MEDIUM, 9 LOW.** Nothing here blocks a push. SB-01 blocks production.

Authorization, delete, the byte-discard rule, the startup guards and the web plain-text rendering hold up when read. The findings are about a log sink outside the app, edge cases that fail closed, and things that must change before a real parser, a real AI extractor or real Google Calendar is added.

## Findings

### SB-01 MEDIUM: PostgreSQL server log can capture syllabus text on any CHECK violation
- **Where:** S001 CHECKs on `syllabus.items`, `unclaimed_dates`, `canonical_texts`, `calendar_events` (for example `S001_Syllabus.cs:230-233`, `281-283`); `docker-compose.yml:4-14` (stock `postgres:16-alpine`, no logging settings); `Program.cs:38-46` (app-side filters only).
- **Scenario:** a CHECK violation (SQLSTATE 23514) on a row that holds text makes PostgreSQL write `DETAIL: Failing row contains (...)` to the **server** log at ERROR under the default `log_error_verbosity = default`. That row can hold `snippet_text`, `context_text`, `title`, `written_date_text`, or for `canonical_texts` the whole document. The app filters and the log canary (`LogPipelineCanaryTests`) cover app loggers only. The DB server log is outside them, and syllabus delete (contract 5.2, "query every table, cache, queue and temp path") does not reach it. Today this needs an app/DB mismatch, i.e. a bug. SB-02 shows such mismatches happen (that one is a varchar overflow, 22001, which carries no DETAIL). A real extractor makes them more likely.
- **Why MEDIUM:** I was unsure between LOW and MEDIUM. The impact (document excerpts, possibly instructor contact details, in a log that delete never cleans) and the low cost of the fix decided it. The Postgres behaviour is from the PostgreSQL docs and I did not reproduce it (UNVERIFIED, same caveat as earlier reviews). It applies to lecture transcript tables too.
- **Fix:** set `log_error_verbosity = terse` (drops DETAIL) and keep `log_parameter_max_length_on_error = 0` and `log_statement = none` in every environment, including the compose file. Record this as a deployment prerequisite next to B1V-05. Add a CI canary that forces a CHECK violation on a canary row and scans the container log. Owners: devops-engineer, database-architect.

### SB-02 LOW: a long line with a date in the middle fails the whole extraction
- **Where:** `MockRuleExtractor.cs:421` (snippet window, at most 580 UTF-16 units) and `:433` (context `start - 700 .. end + 700`).
- **Scenario:** a pasted or uploaded text with a line longer than 1,500 code points (common when text is copied out of a PDF with no line breaks) and a date more than about 700 characters from both ends gets a context of up to about 1,980 code points. That exceeds `varchar(1500)` / `ck_items_snippet_text`, so `PersistAsync` throws. The generic catch in `Jobs.cs:91-95` then fails the syllabus with `invalid_output`. It fails closed and only affects the student's own syllabus. I found no long-line test (`grep 1500` in `Tether.SyllabusTests`: none).
- **Fix:** bound the context in code points: `(1500 - snippet)/2` on each side, on rune boundaries. The orchestrator should re-check every field length before insert (required for a real extractor anyway, see the requirements below). Add a 3,000-character single-line fixture.

### SB-03 LOW: the web client always sends `acknowledgeCheck: true` when approving a "Please check" item
- **Where:** `ItemRow.tsx:83-84`, `OneAtATime.tsx:84`, `requests.ts:27-28`. Server gate: `ItemService.cs:228`.
- **Scenario:** the server's `acknowledgement_required` gate cannot tell a deliberate acknowledgement from an ordinary Approve click. Items flagged `injection_suspect`, `tentative_language` or `dst_ambiguous`, or from a document with hidden content, are one click away from approval. Mitigations that exist: one click per item, the source quote opens by default for please_check (`ItemRow.tsx:65`), batch approve is refused server-side for those items, and the calendar is a mock. This is a documented UX decision ("redesign-round2-review 4"), not an accident.
- **Fix:** none needed for the mock. Before any real calendar write, the acknowledgement must come from a separate, explicit student action for please_check and injection-suspect items, or be recorded in the calendar.v1 server confirm record (CV-11). Owners: adhd-ux-specialist, product-manager.

### SB-04 LOW: multipart fingerprint metadata is `|`-joined, conflates null and empty, and leaves out `attestation.accepted`
- **Where:** `UploadIntake.cs:122-126` (the canonical metadata string); `SyllabusService.cs:103` and `:93` (`accepted` absent on both paths).
- **Scenario:** B1-07 length-prefixed the outer material, but the inner metadata string still joins with `|`. A title such as `X|ts=2026-08-24` with no term, and title `X` with `termStart = "2026-08-24|ts="`, give the same string. The fingerprint is compared before validation (step 7), so the second request replays the first instead of getting 400. Null and empty title, null and empty term values, and `accepted` true or false also collide. Only the same user's own key is affected and nothing leaks; the wrong outcome is a replay instead of a 400.
- **Fix:** build the metadata canonical form with `SyllabusService.Material(...)` (length-prefixed, null distinct) and add `accepted`. Extend the B1-07 test to the multipart path.

### SB-05 LOW: course purge deletes syllabi by FK cascade only (Syllabus has no course lifecycle handler)
- **Where:** `S002_SyllabusIntegrity.cs:22-28` (cascades); `CourseEndpoints.cs:134,162` call `ICourseLifecycleHandler`, which only Lectures implements (`LecturesModule.cs:30`); grep finds no handler in Syllabus.
- **Scenario:** a course purge removes syllabi, items, text and mock events by cascade, but does not call `JobCancellation.Cancel` / `IAiScopeCutoff` and writes no `syllabus_deleted` audit row. With the mock this is safe: job writers re-check the syllabus, and the mock makes no AI call. With a real extractor, an in-flight AI call would run on and spend budget. With real Google, the provider events would be orphaned (calendar.v1 `hardDeleteOn: course_delete` needs a trigger, and a DB cascade cannot call Google).
- **Fix:** add a Syllabus `ICourseLifecycleHandler`. `OnPurgingAsync` cancels jobs and AI scopes and writes the delete audit rows. Later it also creates the calendar.v1 removal intents. This is required before P2-B3, a real extractor, or Google.

### SB-06 LOW: pasted syllabus text and item edit drafts stay in localStorage indefinitely
- **Where:** `CreateForm.tsx:107,132-148` (key `tether.syllabus.draft.{courseId}`, the full text up to 50,000 characters); `ItemEditForm.tsx:23,106` (key `edit.{syllabusId}.{itemId}`); `storage.ts`.
- **Scenario:** the draft is keyed by course, not by user. It is removed only after a successful create, and not on sign-out, course delete or syllabus delete. Item edit drafts survive deletion of their syllabus. On a shared or lab computer the next user of the browser profile can read them. Syllabus text is usually course material, but it can include instructor contact details and the student's own notes.
- **Fix:** use sessionStorage, or clear `tether.syllabus.*` on sign-out and on syllabus/course delete, with a short TTL. Tell the student "kept on this device until you add it". Owner: frontend-engineer.

### SB-07 LOW: export is a state-changing GET reachable by cross-site top-level navigation
- **Where:** `SyllabusEndpoints.cs:277-293` (writes an `exported` audit row, `:287`); Origin is checked only for unsafe methods (`SyllabusEndpoints.cs:33`, `HostServices.cs` OriginCheckMiddleware); cookie `SameSite=Lax` (`IdentityModule.cs:64`).
- **Scenario:** a link or redirect on another site to `/api/v1/syllabi/{id}/export` sends the cookie (Lax allows top-level GET). The browser downloads the file to the victim's own device, writes an audit row and uses the per-user export budget. The attacker cannot read the response and needs the 128-bit id, so the impact is audit noise and budget only.
- **Fix:** refuse `Sec-Fetch-Site` values other than `same-origin` / `none` on export (and ideally on every `/api/v1` GET), or accept the risk in writing. Covered as a second CSRF layer by M1-F04.

### SB-08 LOW: test gaps on the upload decoder and the syllabus log canary
- **Where:** `SyllabusUploadTests.cs` (UTF-16LE with BOM only, `:65-68`; invalid UTF-8 `:106`); `LogPipelineCanaryTests.cs:107-116` (paste only); `SyllabusUploadTests.cs:176-184` (filename canary scans the DB, not logs).
- **Missing:** a text **file** with a UTF-16 lone surrogate, odd byte length, UTF-16BE BOM, UTF-32 BOM, and CESU-8 bytes. If `UnicodeEncoding(..., throwOnInvalidBytes: true)` ever passed a lone surrogate, `DocumentText.Canonicalize` would throw under ICU (B1V-07) and the request would be a 500. I believe it throws (DecoderFallbackException), but that is not tested. The log canary does not drive multipart (with a filename canary), PATCH title/notes, sync, export, or a failing persist.
- **Fix:** add these cases; reference B1V-02.

### SB-09 LOW: the paste path bypasses the upload gate
- **Where:** `SyllabusService.cs:69` (gate only when `multipart`); `SyllabusEndpoints.cs:111-115` (1 MB cap for JSON).
- **Scenario:** JSON creates of up to 1 MB have no per-user concurrency limit and no bytes-in-flight budget, only the 20/min write bucket. Each one costs a few MB of memory (body, string, rune list in `Canonicalize`) before the 50,000 code-point check refuses it. Doing real damage needs many accounts, which dev login does not offer.
- **Fix:** put JSON creates through the same `UploadGate` (or a paste gate) before the body is read.

### SB-10 LOW: debris purge does not re-check "still debris" under the lock
- **Where:** `Jobs.cs:250-254` (select in one transaction) then `Jobs.cs:265` → `SyllabusService.DeleteCoreAsync` (`SyllabusService.cs:332-347`, which checks only existence).
- **Scenario:** `NOT EXISTS (student item)` and `purge_after <= now` are evaluated outside the delete transaction. No path can add a student item to a failed syllabus today: hand entry needs ready or partial (`ItemService.cs:104`), and nothing moves a failed syllabus back. A future retry, `needs_text` hand entry (contract R3-13) or a re-extract would turn this into deletion of student work.
- **Fix:** give `DeleteCoreAsync` a debris mode that re-checks both conditions after `LockSyllabusAsync`, plus a race test.

## Observations (Info, not findings)
- SB-I1: `If-Match: "a", "b"` on one header line is one StringValues entry in Kestrel, so the answer is 412, not the contract's 400. It is still never accepted. Contract wording, not a defect.
- SB-I2: a create-key tombstone expires 24 h after the key was created, not after the delete (contract 2.1/5.2 say "until its 24 hour expiry"). After that, the same key and body would create again. The web keeps keys in memory only (`CreateForm.tsx:127,199-202`).
- SB-I3: after the retention purge, AI titles (up to 120 characters of each dated line, verbatim), `written.dateText`/`timeText` and `item_proposals.proposal` remain. This follows contract 5.3, but 200 titles approximate the schedule text. For PO D4/D11.
- SB-I4: export omits canonical text, sync history and audit (P2B1-19). The ETag, notice and `contentSha256` (P2B1-20) are in it. Other users' data cannot appear: every query is filtered by the user and the syllabus id.

## What I checked and found sound (READ, not run)

**Upload.**
- The type comes from magic bytes (`UploadIntake.cs:25-32`). Extension and part type must agree, and `application/octet-stream` or an empty value is tolerated (`:164-174`).
- The 10 MB cap is counted while reading, never from Content-Length (`:141`). Per-request `MaxRequestBodySize` is 10 MB + 64 KB, `MinDataRate` is 200 KB/s with a 10 s grace, and the read timeout is 60 s (`SyllabusEndpoints.cs:105-110`, `UploadIntake.cs:61-63`). Text is capped at 1 MB in memory.
- Request shape: exactly one `file` and one `metadata` part, a duplicate is 400, at most 2 sections, 8 headers and 4 KB of headers, metadata at most 4 KB. Unknown JSON members are refused (strict binder, `Support.cs:327-337`). A `text` or `filename` member in metadata is 400.
- Decoding: UTF-8, or UTF-16 LE/BE with a BOM, all strict. NUL is rejected after decoding, and no charset is guessed (`:35-50`).
- PDF and DOCX bytes are hashed and dropped (`:148-150`). grep finds no `File.`, `FileStream`, temp path, `EnableBuffering`, `ReadFormAsync` or `IFormFile` anywhere under `src/api/src` except RootSecret, so the API writes nothing to disk. The job fails them with `disabled` (`Jobs.cs:76-82`); test `A_valid_pdf_or_docx_is_accepted_at_intake...` checks that no `canonical_texts` row exists.
- The filename is read for its extension only. It is never logged, stored or echoed: DB scan test `The_client_filename_is_never_stored_anywhere`, and the 415 body is checked in `:114`.
- Per-user single upload and the bytes-in-flight budget (`Support.cs:284-303`); the starvation case is P2B1-12.

**Globalization.**
- `InvariantGlobalization=false` (`Directory.Build.props:11`), and startup fails without working NFC (`SyllabusModule.cs:29`, `TextPolicy.cs:20-27`).

**TextPolicy.**
- Category-based rules (`TextPolicy.cs:76-164`).
- The five astral red-team fixtures that pass ajv under `u` (`schemas.test.ts:20-26`) are tag characters, plane-15 private use, U+1D173 and the U+E0100 supplement. The server rejects them:
  - the four create fixtures through `Every_invalid_redteam_title_and_notes_fixture_is_rejected_by_the_server_TextPolicy` (`TextAndIdTests.cs:103-121`, which selects `redteam` + `title.json`);
  - the update fixture (tag character) by the same `TryValidate` call in PATCH (`ItemService.cs:359`) and by the inline case `a\U000E0041b` (`TextAndIdTests.cs:128`).
- The web never trusts a schema pass: the ajv validators are imported only in tests (grep), the runtime parsers accept TextPolicy-only violations by design (`schemas.test.ts:120-131`), and every syllabus text renders through `PlainText`, a text node with bidi and controls stripped.

**Delete.**
- One transaction: syllabus row lock, create key re-keyed to a content-free tombstone (`result_id`/`course_id` NULL, CHECK `ck_idempotency_keys_shape`), audit row, then delete (`SyllabusService.cs:332-347`).
- FK cascades remove `canonical_texts`, `extraction_jobs`, `items`, then `item_proposals`, `item_alternatives` and `calendar_events`, plus `unclaimed_dates`, `sync_runs`, `sync_run_items`, and the item and sync idempotency keys (via `syllabus_id`).
- What survives: `decision_audit` (ids, counts, times, HMAC) and `creation_counters`, both allowed by the contract.
- Ownership is checked before `jobs.Cancel` (`:352`).
- Replay after delete: create gives 404 (`:272`); item and sync keys cascade away, so a replay finds no record and the syllabus lookup gives 404.
- The retention purge nulls snippet and context text and deletes the canonical text in one transaction (`Jobs.cs:314-337`); offsets stay (P2B1-08).
- Account deletion: the DB cascades, but there is no endpoint and `purge_evidence` has no caller (NEW-01).

**Export.**
- The response has `no-store`, `nosniff` and `attachment` (`SyllabusEndpoints.cs:290`), plus the global CSP/no-referrer headers (`Program.cs:159-176`).
- Per-user 6/min limit, checked after the ownership check.
- The web checks `isExportOf(body, id)` and downloads through a Blob; it never renders the export.

**Authorization.**
- Every route resolves the syllabus through the user query filter first. Every entity has a filter (`SyllabusDbContext.cs:57-172`), and raw SQL names `user_id` explicitly.
- Nested ids in batch, sync and remove maps go through canonical-form checks and de-duplication (`SyllabusEndpoints.cs:189-196,210-216,228-234`) and are looked up within the syllabus. A foreign id is per-item `not_found`.
- ETags are an HMAC with a constant-time compare, re-checked after the row lock.
- Idempotency scope is user + method + route + key hash, and the fingerprint covers the concrete path.
- Origin: exact ordinal match, a missing header refused, on multipart and every unsafe route (`SyllabusEndpoints.cs:30-40`, plus the host middleware).
- Separate per-user read and write buckets, plus sync, batch and export gates.
- Startup refuses a calendar provider or extractor other than `mock` and refuses fault injection outside Development/Test (`SyllabusModule.cs:31-37`; the env-name escape is P2B1-13).

**Injection.**
- Hostile text reaches titles only after `MakeTitle` → `TryValidate`, and becomes the mock event title (≤200 characters) only through an approved item.
- Notes and snippets never reach events (`SyncService.cs:76-89`).
- `injection_suspect` (from hidden content or `InjectionRx`) turns batch approve off server-side (`Jobs.cs:176-182`, `ItemService.cs:223-227`) and forces please_check.
- The web renders all of this as plain text: no `dangerouslySetInnerHTML`, `innerHTML` or `href` built from data in the syllabus components (grep).

**Logging.**
- Module logs carry exception type or SQLSTATE only (`Jobs.cs:93,95,267,272,355,373`; `SyllabusService.cs:256`).
- Gaps: SB-01, SB-08 and B1V-01 (a provider-scoped config bypass).

## What must change when real components arrive

**P2-B3 real parser and sandbox (contract 5.1 items 3-9).**
- The API stops discarding bytes. It must relay the stream to a separate ingest worker and never mount the tmpfs volume itself.
- One fresh sandbox per job: no network, no DB credentials, seccomp, non-root, 60 s, log driver `none`, result through a file or pipe, never stdout.
- Full DOCX check (`word/document.xml`), zip-bomb, path, macro and OLE caps, DTD prohibited, PDF actions ignored, page cap before extraction, hidden text dropped and counted.
- Bytes deleted on finish, on `DELETE` (including a job cancelled by SB-05's handler), and by a sweeper within 1 hour, tested with a killed worker. Swap and core dumps disabled.
- The orchestrator re-validates parser output as untrusted.
- `UploadGate` and `JobCancellation` are per process. Multi-instance needs a shared gate and a cancellation signal.
- Hostile-file fixtures must exist before merge (P2C-01, P2C-02, P2D-R3-03..06).

**Real AI extractor.**
- PO-4, G1 notice and G2 vendor terms first. The startup guard stays until then.
- The orchestrator must re-validate every proposed field in `PersistAsync` instead of trusting the extractor:
  - TextPolicy on title and dateText;
  - kind and flag enums;
  - dates within the term and the 2000-2100 window;
  - snippet text equal to the canonical slice at its offsets;
  - lengths (SB-02);
  - at most 200 items.
- Confidence may never be raised above what the server's own checks allow.
- Wire in `IAiScopeCutoff` on syllabus delete (done) and on course purge (SB-05).
- Per-user and global budget with a floor for live lectures (P2C-23).
- Red-team fixtures: instruction text, fake dates, hidden-text payloads.
- A disclosure that text sent to a vendor cannot be recalled.

**Real Google Calendar (calendar.v1).**
- Do not do external calls inside the DB transaction that holds the syllabus lock. Today `ItemService.ApplyDecisionAsync` and `SyncService.SyncAsync` call the mock inside the transaction; the real provider must use the calendar.v1 run/intent model.
- Delete, withdraw and course purge must queue Google removals (SB-05; CV-01, CV-07).
- AI-derived titles can carry URLs, emails or phone numbers from a hostile syllabus. Google auto-links them, so apply the CV-17 composed-title/body allowlist and escaping.
- Acknowledgement and confirm records per SB-03 and CV-11.
- The startup guard is replaced only after a security review of the provider registration.

## Not verified
- Any test run. Earlier figures (99 syllabus tests, and so on) are the authors' claims or my 2026-10-29 run, not today.
- PostgreSQL server-log behaviour (SB-01), `UnicodeEncoding` lone-surrogate behaviour (SB-08), and Kestrel's If-Match splitting (SB-I1) are from documentation and reading, not probes.
- Web unit tests and E2E, browser download behaviour, the `Sec-Fetch-Site` values real browsers send.
- Multi-instance behaviour of the in-memory gates.

## Disposition (backend-engineer, 2026-10-31)

Appended by backend-engineer for the Lead Architect. Findings above are unchanged. Mock only: no real extractor, AI vendor, parser or calendar was added. Scope was `src/api` and the additive notes in `docs/API_CONTRACTS_P2.md`; `src/web`, the compose files, `.github` and `docs/ops` were not touched. `docs/security/findings-register.md` was not edited (the Lead updates it). "Failed on old code" means the new test was run against the old behaviour (the fix temporarily reverted in the working tree, then restored) and failed.

| ID | Disposition | What changed | Tests (failed on old code?) |
|---|---|---|---|
| SB-01 | DEFERRED (not this agent) | Owned by devops-engineer (compose Postgres logging settings) and database-architect. | none here |
| SB-02 | FIXED (pending independent verification) | `MockRuleExtractor.ContextWindow` bounds the context to 1,500 code points in total, snippet included. It grows one code point at a time (half the remaining budget on each side, unused budget moves to the other side), so it never splits a surrogate pair and the bounds stay UTF-16 offsets. As defence in depth, `PersistAsync` re-checks every proposed field against the column limits (`SyllabusLimits.Fits`: title 200, dateText 120, timeText 60, confidenceReason 200, snippet 600, context 1,500 code points). An item or unclaimed date that would overflow is dropped and counted in `droppedInvalid` instead of failing the whole syllabus. | `ExtractorTests`: 5 long-line cases (1,501 to 20,000 characters; date in the middle and near both ends), astral characters at 4 cut alignments, several dates on one line, short-line control, column guard. Integration `SB02_a_3000_character_line...` (ready, context exactly 1,500). **Yes**: 6 unit cases and the integration test failed on old code. The astral test passed on old code because the old UTF-16 window of about 1,980 units is under 1,500 code points when most characters are astral. |
| SB-03 | DEFERRED (not this agent) | Web/UX decision (adhd-ux-specialist, product-manager); requirement before any real calendar write. | none |
| SB-04 | FIXED (pending independent verification) | `UploadIntake.MetadataCanonical` uses the length-prefixed `SyllabusService.Material` (versioned `metadata.v2`), keeps null distinct from empty, and includes `attestation.accepted`. The paste fingerprint now includes `accepted` too. Effect: create keys stored before this change no longer match a retry, so a retry within 24 h gets 400. Dev data only. | Unit `Metadata_canonical_form...`; integration `SB04_a_multipart_retry...` (title/term separator collision, null vs empty title, accepted true vs false: 400; identical retry still replays) and `SB04_a_paste_retry_with_attestation_accepted_false...`. **Yes**, all three failed on old code. |
| SB-05 | FIXED (pending independent verification) | New `SyllabusCourseLifecycleHandler` (scoped `ICourseLifecycleHandler`). **Before the commit:** cancels every syllabus job of the course through `JobCancellation` (which calls `IAiScopeCutoff`). Nothing is written, so a rolled-back purge leaves the data intact. **After the commit** (the authority): it cancels again, including syllabi created in the gap (found by their surviving `syllabus_created` audit row), writes one content-free `syllabus_deleted` audit row for each syllabus that is now gone, and calls `MockCalendar.RemoveCourseEventsAsync`. That call normally finds 0 rows after the cascade; it is the hook where a real provider would queue removal intents. No create-key tombstone is written: live keys cascade with the course, and a tombstone written before the commit would answer 404 for a syllabus that survives a rolled-back purge (schema doc section 6, X-02). The `IAiScopeCutoff` tombstone is not needed: the SharedKernel cutoff has no syllabus tombstone, the scope probe fails closed once the syllabus is gone, and the post-commit cancel cuts off anything that started in the gap. Also: a create whose insert fails on the course FK because the course was purged after the ownership check now answers 404, not 500. | Integration `SyllabusCoursePurgeTests`: purge while a job is `extracting`, plus a ready syllabus with a synced mock event. Checks: at least 2 cutoffs per syllabus, 1 `syllabus_deleted` row each, zero rows in 9 tables, and none after 3.5 s (late writer). Second test: a syllabus created in the purge gap is cut off and audited after the commit. **Yes**, both failed with the handler unregistered. The 404-instead-of-500 FK path is **not tested** (the timing cannot be forced without a hook). |
| SB-06 | DEFERRED (not this agent) | Frontend-engineer owns it. | none |
| SB-07 | FIXED (pending independent verification), contract clarified additively | The export GET stays a GET; the web client is unchanged and needs no wire change. A route filter answers `403 csrf_failed` when `Sec-Fetch-Site` is present and not exactly `same-origin` or `none`, when that header is repeated or combined, or when `Origin` is present and not allow-listed. The check runs before the id and ownership checks, and a refused request writes no audit row and spends no export budget. `docs/API_CONTRACTS_P2.md` section 8 (Export) and the section 5 table now list 403 (additive). Residual: a browser that sends no Fetch Metadata is still served (accepted residual of the review: audit noise and budget, response unreadable). Other GETs have no side effects and are unchanged. | Integration `SB07_export_refuses...`: cross-site, same-site, wrong case, combined value, foreign Origin and `null` Origin give 403 with no audit row; same-origin, none and absent give 200 and 3 audit rows; a foreign user's cross-site request is 403 and same-origin is 404. **Yes**, failed on old code. Real browser `Sec-Fetch-Site` values through the Next.js proxy are **not verified**. |
| SB-08 | FIXED (tests added; the decoder was already correct) | No product change. | Unit: 13 malformed files (UTF-16LE and BE lone high and low surrogates, a reversed pair, odd byte length for both, CESU-8, UTF-8 lone surrogate, overlong UTF-8 after a BOM, NUL, UTF-32LE and BE BOMs, BOM only) are refused without throwing; LE, BE, UTF-8 and UTF-8-with-BOM round trip. Integration `SB08_text_files...`: 9 malformed uploads give 415 (never 500) and create nothing; a UTF-16BE file with an astral character is ready. The log canary (`DevelopmentLogPipelineCanaryTests`) now also drives a multipart text upload with a new filename sentinel through the worker, and a lone-surrogate upload (415). **No**: these are coverage tests and passed on old code too, which confirms the review's belief that the decoder throws (now tested, not assumed). |
| SB-09 | FIXED (pending independent verification), contract clarified additively | `UploadGate.EnterAsync` is now the create gate for both paths. It holds one slot per user and the global bytes-in-flight budget; an upload reserves 10 MB and a paste 1 MB, before the body is read. It is entered at the end of step 6, after the quota checks. A different key while the slot is taken is 429 at once. A same-key retry waits (at most 10 s, `CreateGateWaitMilliseconds`, and at most 8 waiters per user) and then continues to the normal replay. This keeps the contract's concurrent-identical-create replay (existing tests `Two_simultaneous_identical_creates...` and `SyllabusCreateRaceTests`). Uploads change slightly: a concurrent same-key upload now waits instead of getting an immediate 429. Section 11 of the contract has an additive note. | Unit: 5 gate tests (different key refused at once, same key waits then enters, wait timeout and waiter cap, shared bytes budget, cancelled waiter). Integration `SB09_a_paste_holds...`: a stalled paste holds the gate; another paste and an upload by the same user get 429; another user is unaffected; a same-key retry waits, then replays the same id. **Yes**, the integration test failed with the paste path ungated (the gate unit tests exercise the gate itself, which existed before). |
| SB-10 | FIXED (pending independent verification) | `SyllabusService.DeleteCoreAsync(..., debrisDueBy)`: after the row lock the debris purge re-checks status `failed` or `needs_text`, `purge_after` not null and due, and no student item. If any condition fails it rolls back and deletes nothing. The job is cancelled only for a syllabus that was actually deleted. `SyllabusMaintenance.BeforeDebrisDelete` is an internal test seam (null in production). | Integration `SB10_the_debris_purge_re_checks...`: four failed syllabi are selected; in the race window one gains a student item, one has its `purge_after` moved out and one is moved back to `extracting`. Those three survive with no delete audit; the fourth is still purged. **Yes**, failed on old code. |

Wire, schema and migration: **no** schema, migration, DTO or wire-shape change. Two additive contract notes: export can now answer 403 `csrf_failed` (an existing code), and the create gate is described in section 11. Not verified: multi-instance behaviour (the gate and job cancellation are still per process, ADR-020d), real browsers' Fetch Metadata through the proxy, and the FK-to-404 path of a create racing a purge.
