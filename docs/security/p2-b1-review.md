# P2-B1 Security Review: Syllabus module (mock-only)

Date: 2026-10-28. Reviewer: security-engineer (independent, read-only; no source file modified). Finding ids are `B1-nn`.
Baseline: `docs/API_CONTRACTS_P2.md`, `docs/security/p2-contract-review.md`, `-rereview.md`, `-delta.md`, CLAUDE.md invariants, `docs/PROGRESS.md` Iteration 9 known gaps.
Release recommendation: no Critical or High finding. The module is acceptable as a mock-only slice. Four Medium findings should be fixed or explicitly accepted before any real document, real model or real Calendar enters the picture. Gates G1 to G5 are unchanged by this review.

## What I did and did not verify

READ in full: `SyllabusModule.cs`, `SyllabusEndpoints.cs`, `SyllabusService.cs`, `ItemService.cs`, `SyncService.cs` (including `MockCalendar`), `SyllabusCore.cs` (including the `Sql` helper), `Support.cs`, `UploadIntake.cs`, `Jobs.cs`, `SyllabusProblems.cs`, `SyllabusScopeProbe.cs`, `SyllabusDbContext.cs`, `TextPolicy.cs`, `DocumentText.cs`, `Dtos.cs` (mapping), migration S002 in full, S001 only the audit/counter/FK sections, `Program.cs`, `OwnershipSaveChangesInterceptor`, `WireId`. Read the names and key assertions of the integration test list, `SyllabusStartupGuardTests`, the canary tests, and the NFC test. Read the `MockRuleExtractor` header and regex declarations only.

NOT verified:
- I did not run any test or build. The host `dotnet` has no SDK and there is no .NET SDK image locally; I did not pull one. I rely on the Lead's recorded run (91 syllabus + 454 integration pass). I did not mutate code to prove any test fails red; statements about tests below are by reading assertions only.
- Not read: rest of `MockRuleExtractor.cs` (extraction logic, snippet slicing), the bodies of most integration tests, `ICourseOwnership` implementation, `CreateUserScope`, the host `OriginCheckMiddleware`/cookie settings, S001 CHECK/trigger bodies other than those cited, `Entities.cs`, the web client (none exists for syllabus).
- Not assessed: behaviour on a real multi-instance deployment, backups, Npgsql's own log output for constraint failures (see B1-02), real DOCX/PDF handling (not built).
- Nothing was run against an external service.

## Controls checked and found implemented (evidence by reading)

| Control | Where | Result |
|---|---|---|
| Every route is user-scoped | Global query filter on all 13 entities (`SyllabusDbContext.cs:57-173`), `OwnershipSaveChangesInterceptor`, every raw SQL in `Sql` calls carries `user_id` (checked each of ~30 statements); course ownership via `ICourseOwnership` on Create, List and DemoCalendar | OK. No IDOR found on any of the 16 routes. Foreign and missing resources take the same code path to `NotFound()`. Nested ids are always checked with the path syllabus (`i.SyllabusId == syllabusId`). Batch item ids that belong to someone else come back as `not_found`, same as missing. |
| Mass assignment | Request DTOs are separate classes; `Strict` binder has `UnmappedMemberHandling=Disallow`, `AllowDuplicateProperties=false`, `RespectNullableAnnotations`; PATCH uses a whitelist `switch` with a 400 default (`ItemService.cs:321-371`) | OK |
| Upload limits | Streaming byte count in `ReadFileAsync` (10 MB), 1 MB text buffer, 50,000 code point cap before canonicalisation, 2 parts max, metadata 4 KB, 60 s read timeout, 200 KB/s min rate, one upload per user plus global bytes-in-flight, Kestrel cap lifted only on this route and only after auth, Origin, key, ownership and quota | OK (see B1-12 for a DoS nuance) |
| Type check | Magic bytes + extension + part content type must agree (415); filename read once and dropped; PDF/DOCX bytes hashed and discarded, never parsed, never written | OK |
| SQL injection | All SQL is parameterised (`@pN`), no concatenation; cursors are strictly parsed | OK |
| Header injection | Only server-built values reach `Location`, `ETag`, `Content-Disposition`, `Retry-After` | OK |
| Prompt injection | No model is called. `InjectionSuspect`, hidden-character counting and batch-approve disabling exist and feed `batchApproveAllowed`. Regexes have a 250 ms timeout and a timeout fails the job closed (`invalid_output`). | OK for the mock; real-model controls remain a P2-B3/G2 matter |
| Error leakage | `SyllabusProblem` uses fixed title/detail per code and closed error vocabulary; hostile field names mapped to `request`; global handler logs exception type and trace id only | OK |
| Logging | The only five log call sites in the module log SQLSTATE or exception type name only | OK by reading (but see B1-02) |
| Idempotency | Key scoped by (user, method, route, SHA-256(key)); HMAC fingerprint over route + path + body material; ring for rotation; replay does not re-execute; delete turns the create key into a tombstone; advisory lock on create; unique index race handled for create and sync | OK (gaps B1-07, B1-14) |
| Review before calendar write | Sync re-checks `Decision == approved`, `WhenStatus == resolved`, not policy, and the client-named revision, all on the row locked `FOR UPDATE`, and requires `confirm.provider == "mock"` and `acknowledgedDemo` (`SyncService.cs:127,171-172`). Approval of `please_check` items needs acknowledgement. Batch approve only for `looks_right`, unedited, non-suspect. | OK |
| ETag / If-Match | HMAC tag over kind, id, row version, created_at, restore epoch; constant-time compare; checked inside the locked transaction; 428 when missing; 412 on mismatch | OK |
| Non-mock guard | `SyllabusModule.cs:28-34`: ordinal string checks throw at startup for CalendarProvider and Extractor; fault injection refused outside Development/Test. Only one `ICalendarProvider` and one `ISyllabusExtractor` implementation exist; an architecture test (`ModuleBoundaryTests.cs:331`) pins the former. Tests: `Startup_refuses_any_calendar_provider_other_than_mock`, `..._a_model_extractor...`, `..._fault_injection_outside_development_and_test`. | OK. Residual: the guard is a configuration check, not a compile-time barrier; it is also evaded by setting the environment name to `Test` in production (B1-13). |
| Delete | One transaction, row lock, tombstone, audit, cascade; scan test with a canary over the whole schema (`SyllabusBatchAndRetentionTests.cs:226-240`) | OK for content; see B1-03 for reliability |

## Required controls from the contract reviews: implemented?

| Required control | Status |
|---|---|
| Origin exact match incl. multipart and DELETE, missing Origin rejected | Implemented (`SyllabusEndpoints.cs:30-40`, plus the host `OriginCheckMiddleware`). Test: `State_changing_routes_reject_a_missing_foreign_or_case_variant_origin...`, `Cross_origin_multipart_upload_is_rejected_before_any_byte_is_read`. |
| "A required custom header is checked" (contract line 43) | NOT implemented (B1-09). |
| Evaluation order (auth, Origin, syntax, ownership, idempotency, limits, body, validation, state) | Implemented and tested (`Missing_if_match_is_428_and_a_missing_idempotency_key_is_400_and_both_precede_ownership`, `Non_canonical_ids...`). |
| Server receipt time as consent record (R3-11) | Implemented (`SyllabusService.cs:200`). Test `Notice_and_attestation_are_stamped_by_the_server_not_the_client`. |
| Audit with ids/counts/time only; HMAC not plain hash (R3-12) | Implemented for create (`contentHmac`); CHECK constraint restricts it to `syllabus_created`. |
| Approval recorded per item and revision (contract line 184) | Partially: single decisions yes; batch approve no (B1-04). |
| Retention purge removes text, snippet text, context AND snippet offsets (contract line 172) | Partially: offsets are kept (B1-08). |
| Per-user buckets separate from lecture buckets (R2-14) | Implemented (`WithSeparateRateBucket`; test `Syllabus_writes_have_their_own_bucket...`). |
| Late writer after delete creates nothing (C-21) | Implemented (`PersistAsync` re-locks the syllabus). Test present. |
| No filenames or parser text in logs; log canary under fault injection | Not demonstrated (B1-02). |
| Export owner-scoped, no-store, attachment, nosniff, 6/min | Implemented (`SyllabusEndpoints.cs:277-293`). |

## Findings

### Medium

**B1-01 (Medium) Syllabus delete has no deadlock retry, and the extraction reaper takes locks in the opposite order**
- `SyllabusService.cs:302-323` (`DeleteCoreAsync`/`DeleteAsync`) vs `Jobs.cs:183-203` (`ReapAsync`).
- Delete locks the syllabus row, then its cascade touches `extraction_jobs`. `ReapAsync` updates `extraction_jobs` first (line 189/193) and then updates `syllabi` (line 198). Everywhere else (SetStage, Fail, Persist, Sync, Items) the order is syllabus then job, which is consistent with delete. A delete issued while a crashed job is being reaped to `failed/timeout` can deadlock (SQLSTATE 40P01). The global handler turns that into a 500, and the student's deletion control fails. The Iteration 9 "deadlock fix" (retry on 40P01) was applied to `Lectures/SessionService.cs:296-312` only; `grep` finds no 40P01 handling in the Syllabus module.
- Impact: failed deletion is a privacy-control failure (the user can retry, but a bare 500 gives no hint). Window is narrow (lease expiry with attempts exhausted).
- Fix: lock the syllabus row first in `ReapAsync` (select the dead jobs, `FOR UPDATE` the syllabi in id order, then update jobs), and wrap `DeleteCoreAsync` in the same bounded 40P01/40001 retry the Lectures module uses. Add a test that holds a job-row lock and issues DELETE.
- Test status: no syllabus test covers this; the claimed red-first regression test is for Lectures.

**B1-02 (Medium) No log-canary test through the real log pipeline; Npgsql's own logging is not filtered**
- `Program.cs:26-28` silences only `ExceptionHandlerMiddleware`, `EntityFrameworkCore.Update` and `EntityFrameworkCore.Database.Command`. The Syllabus module executes raw ADO commands through `Sql` (`SyllabusCore.cs:19-65`) and EF reads. A PostgreSQL error such as a CHECK/unique violation carries `Detail` text ("Failing row contains (...)", key values). I could not verify whether Npgsql or `EntityFrameworkCore.Query` logs such an exception object at Error under the default provider, so I cannot say whether document text can reach logs. The existing canaries (`SchemaScan`, response bodies, filename canary) look at the database and HTTP bodies, not at captured log output. The contract asked for a "log canary under fault injection" (line 274).
- Fix: add an integration test that attaches a capturing `ILoggerProvider` to the real host, then drives (a) a forced constraint violation on `items`/`canonical_texts` with a canary title and text, (b) an extraction exception, (c) the 40P01 path, (d) the upload route with a canary filename, and asserts the canary appears in no log record, including exception `ToString()`. Add `Npgsql` and `Microsoft.EntityFrameworkCore.Query` to the log filters (set to a level that cannot print exception details) until that test exists.

**B1-03 (Medium) Batch approvals are not individually audited**
- `ItemService.cs:253` writes a per-item audit row only when `Action != "approve"`; for approve only a count row `batch_approved` is written (line 272). The contract requires approval to be recorded with user, item id, revision and time (API_CONTRACTS_P2 line 184, 217). With batch approve the record of which item and which revision the student approved before a calendar write does not exist. `sync_run_items` records items and outcomes but not the approved revision.
- Impact: weak evidence for "review before calendar write", and a gap for the future real Calendar confirm record (entry criterion 6).
- Fix: audit `approved` per item with `Revision` in the batch path (the `ck_decision_audit_action` constraint already allows it) and keep the summary row. Add a test asserting one audit row per approved item after a batch call.

**B1-04 (Medium) Unbounded growth of `sync_runs`, `sync_run_items` and `decision_audit` per user**
- `SyncService.cs:198-208`, `Jobs.cs:260-262` (sweeps cover idempotency keys, finished jobs, counters only). Every successful sync call, even `nothing_to_do`, inserts a run, up to 100 run items and an append-only audit row. Allowed rate is 12 syncs/min/user (6/min/syllabus) and 6 exports/min each writing an audit row, with no cap per syllabus and no retention. One authenticated user can add roughly 1,200 `sync_run_items` rows per minute for as long as the syllabus lives, and audit rows can never be deleted by the app (`trg_decision_audit_append_only`).
- Fix: keep only the latest N runs per syllabus (the replay needs only runs within the 24 h key window), prune on the maintenance sweep, and cap audit write rate for `exported` and `synced` (for example coalesce repeated identical actions within a minute). Decide an audit retention period with the Product Owner and give `syllabus.purge_evidence` a production caller (account deletion is not implemented; the only calls are in tests). Until then audit rows and counters outlive the user (they are content-free, but keyed by user id).

### Low

**B1-05 (Low) `DELETE /syllabi/{id}` cancels another user's running job before the ownership check**
- `SyllabusService.cs:319-323`: `jobs.Cancel(id)` runs before `DeleteCoreAsync` verifies ownership. `JobCancellation.Cancel` (`Support.cs:267-272`) cancels the in-process CTS and calls `IAiScopeCutoff.Cancel(Syllabus, id)` keyed by syllabus id only. Any authenticated user who knows (or has seen in a URL or log) a foreign syllabus id can interrupt that user's extraction; repeated calls push the job through the attempt cap to `failed/timeout`. The response is still the uniform 404, so no existence oracle, but there is a cross-user side effect.
- Fix: cancel only after `LockSyllabusAsync` succeeded for this user (or pass user id into the cancel key). Add a cross-user test: B deletes A's id while A's job runs; A's job must complete.

**B1-06 (Low) Idempotency edge cases: item-create race returns 500, expired-key reuse fails**
- `ItemService.cs:121-129`: no handler for unique violation `ux_idempotency_keys_scope`. Two simultaneous item creates with the same key can yield one 500 instead of a replay (the create and sync paths handle this; item create does not, and no test covers it). Also `LookupKeyAsync` treats an expired record as absent (`SyllabusService.cs:41`) but only syllabus create deletes expired rows before inserting (line 173); item create and sync inserting a key within the window between expiry and the 5 minute sweep hit the unique index (item create: 500, sync: 400 `validation_failed`).
- Fix: catch the unique violation and replay or return 409 in item create; delete the expired row under the lock in all three routes, or include expiry in the unique scope.

**B1-07 (Low) Fingerprint material is delimiter-joined without escaping**
- `SyllabusService.cs:88`, `ItemService.cs:70`: `|` joins unvalidated strings (`Title`, `TermStart`, ...). Two different requests can map to the same material, so a key reused with a different body may replay the first response instead of 400. Same user, same route, same key only; no cross-user effect. Fix: length-prefix each field or hash a canonical JSON.

**B1-08 (Low) Retention purge keeps snippet offsets (contract says remove them)**
- `Jobs.cs:277-289` nulls snippet text, context text and context offsets but keeps `snippet_start/end` ("keeps the offsets"). API_CONTRACTS_P2 line 172 says the purge removes "every snippet.context and every snippet.text (and the snippet offsets)". The wire form (`Mapping.Snippet`, `Dtos.cs`) hides them once text is null, so the exposure is storage only, and offsets alone do not reveal text. Also `written_date_text`/`written_time_text` (up to 120/60 characters of document text) and `item_proposals.proposal` (title) survive; the contract lists `written.dateText` and titles as surviving, so those are documented. Fix: either null the offsets or amend the contract and data doc to say they are kept; record the decision.

**B1-09 (Low) The contract's custom CSRF header does not exist**
- `SyllabusEndpoints.cs:30-40`. The contract says "a required custom header is checked"; only Origin is checked (known gap from Iteration 9; the header name is undefined). Origin exact match plus the host `OriginCheckMiddleware` is a sound primary defence for browser callers and fails closed when `Security:AllowedOrigins` is empty, so I rate this Low. Leaves no defence-in-depth if an allowed origin is ever compromised or if a cookie-authenticated non-browser flow appears.
- Fix: Lead to name the header (for example `X-Tether-CSRF` bound to the session) in the contract, enforce it in this filter and in the host middleware, and add the test. Until then amend the contract so it does not claim a control that is absent.

**B1-10 (Low) NFC normalisation is a no-op under `InvariantGlobalization`, and the test is a tautology**
- `TextPolicy.cs:20`, `Directory.Build.props:10`, `TextAndIdTests.cs:174-176`. The test expects `Café` when normalisation is unavailable and `Café` when available, so it passes in both environments and cannot fail red. Student titles are therefore stored as submitted (NFC and NFD forms distinct), which defeats exact-match duplicate detection and the R2-01 intent of canonical comparison. `NormalizationAvailable` is not read anywhere else (no startup warning). Impact is limited because the mock calendar is local and the user can only deceive themselves; it matters once titles go to Google or are compared.
- Fix: decide ICU (set `InvariantGlobalization=false` and install `libicu` in the image) or reject non-ASCII combining sequences that are not already NFC with a managed normaliser; make the test assert NFC and fail when unavailable; log a startup warning in non-Development when `NormalizationAvailable` is false.

**B1-11 (Low) Key material falls back to random per-process keys, and only one of three is warned about**
- `Support.cs:80-91`, `Program.cs:90-91`. `Security:IdempotencyHmacKey` and `Security:SyllabusAuditHmacKey` silently default to random bytes. After a restart or on a second instance, idempotent retries within 24 h get 400 instead of a replay, and the audit HMAC of a content hash can never be verified or correlated. The host warns only about `ETagKey`.
- Fix: fail startup (or at least warn) outside Development when any of the three keys is absent; document rotation.

**B1-12 (Low) Upload gate lets 10 users starve all uploads**
- `Support.cs:239-259`: each accepted upload reserves the full 10 MB against a 100 MB global budget, so ten authenticated users each holding a slow connection (200 KB/s minimum, 60 s read timeout) block uploads for everyone else. Also `Create` consumes the gate for replayed keys. Fix: reserve incrementally, shorten the grace period, or add a per-IP concurrency cap; accept for a single-tenant pilot.

**B1-13 (Low) Extraction persistence is not fenced by lease or attempt, and there is no heartbeat during extraction**
- `Jobs.cs:54-89,121-180,170-173`. `ProcessAsync` never extends the lease between the stage updates and `PersistAsync`; `PersistAsync` does not check the job is still `running` with this lease owner, nor that the syllabus is still in `proposing`. If an extraction exceeds `LeaseSeconds` (60 s), the reaper re-queues it and a second worker can run the same syllabus; both can persist, producing duplicate items, or a late result can flip a reaper-failed syllabus back to `ready`. The mock finishes in milliseconds (regex timeout 250 ms), so this is latent; it becomes real with a model call. Also the non-Development guard keys on environment name: setting `ASPNETCORE_ENVIRONMENT=Test` in production re-enables fault injection (`SyllabusModule.cs:32-34`). Fix: persist only `WHERE state='running' AND lease_owner=@me`, count existing items before insert, heartbeat in the stages, and require an explicit extra setting for fault injection.

**B1-14 (Low) DST-gap check silently passes when host tzdata is missing**
- `Support.cs:183-191` returns false when the zone is unknown to the host. In a minimal container without tzdata, `dst_ambiguous` is never raised and a nonexistent local time can be approved and synced. The allow-list is independent of tzdata by design, so this mismatch is real. Fix: fail startup (or log a warning) if any allow-listed zone is missing from `TimeZoneInfo`, and add a test.

**B1-15 (Low) Audit immutability and purge are not tamper-resistant against the application role**
- `20261028194922_S002_SyllabusIntegrity.cs:65-82`. The append-only trigger is bypassed by `set_config('syllabus.evidence_purge','on', true)`, which any session running as the app role (for example after a SQL injection) can call; `purge_evidence` is not `SECURITY DEFINER` and the app role owns the table. This is acceptable as accident protection, not as evidence protection. Fix before production: separate migration role from runtime role, give the runtime role no `TRIGGER`/`ALTER`, and make `purge_evidence` `SECURITY DEFINER` with a pinned `search_path`.

### Info

- **B1-16 Rate limiting state is per process.** `RateGate` (`Support.cs:219-237`) is in memory, so per-syllabus, batch and export limits multiply by instance count. The host bucket (`syllabus-write`/`syllabus-read`) is also per process. Fine for a single instance; document it.
- **B1-17 Delete is subject to the 20/min write bucket.** Unlike consent reversal (ADR-019b), deletion can be answered 429 under a flood from the same account. Consider exempting DELETE like Pause/Stop.
- **B1-18 Approval persists across a student edit of a synced item.** `ItemService.cs:435-439`: editing an approved item that was already synced leaves `Decision=approved` (`changed_since_sync`), including an AI item that has been downgraded to `please_check` without a new acknowledgement. Matches reconciliation row 17 and the student made the edit, but the real-Calendar confirm record should show which revision was confirmed (ties to B1-03).
- **B1-19 Export covers the stated scope.** Items, snippets, unclaimed dates and mock events with the same caps as the write caps. It excludes the canonical text (purged on a clock, not exportable), sync run history and audit rows. This matches the contract, but a data-subject export request would need a separate answer for audit/sync history.
- **B1-20 `content_sha256` of the original document survives text purge until syllabus delete** and is returned on the wire. It is the user's own hash; the contract allows it. Note it for counsel (it can confirm possession of a known file).

## Test assessment (what actually proves each control)

Probably effective (assertions target the failure itself, by reading): route-by-route IDOR table with two users and same-user mismatched parent; Origin matrix incl. multipart; canonical-id form; strict binder (unknown, read-only, duplicate members); delete scan with a canary and negative control; late-writer-after-delete; concurrent sync/patch/stale-tab pair; per-user bucket separation; startup refusal for non-mock calendar, non-mock extractor, and fault injection in Production; idempotent replay, tombstone and key-on-other-syllabus.

Not shown to fail red or missing:
- NFC test (B1-10) cannot fail.
- `The_only_calendar_provider_registered_is_the_mock` checks the container registration, not the startup guard; the guard is covered by the three `Startup_refuses_*` tests, which would pass in any environment where the factory throws for another reason only if the message also contains the key name (they do assert the message, so they are reasonably specific).
- No test for: custom CSRF header (absent), log canary through the real pipeline (B1-02), delete under reaper/job-row-lock deadlock (B1-01), cross-user job cancel via DELETE (B1-05), concurrent identical item-create (B1-06), per-item audit for batch approve (B1-03), unbounded sync-run growth (B1-04), lease fencing (B1-13), missing tzdata (B1-14), key-material warnings (B1-11).
- I did not mutate the code to verify any test goes red; treat all "probably effective" claims as unconfirmed.

## Summary

| Severity | Count | Ids |
|---|---|---|
| Critical | 0 | |
| High | 0 | |
| Medium | 4 | B1-01, B1-02, B1-03, B1-04 |
| Low | 11 | B1-05 to B1-15 |
| Info | 5 | B1-16 to B1-20 |

Top issues, in fix order:
1. B1-01 syllabus delete can 500 on a deadlock with the reaper and has no retry (deletion is a privacy control).
2. B1-02 no log-canary through the real log pipeline; Npgsql and EF Query log categories are unfiltered, so document text reaching logs is neither prevented nor tested.
3. B1-03 batch approvals leave no per-item, per-revision record, weakening "review before calendar write".
4. B1-04 sync runs and audit rows grow without bound and audit has no retention or production purge caller.
5. B1-05 foreign DELETE cancels another user's running extraction before the ownership check.
6. B1-09 and B1-10: the undefined CSRF header and the tautological NFC test are still open from Iteration 9; neither is exploitable today.

No finding blocks continued mock-only development. None of this review clears gates G1 to G5; real documents, real Claude calls and real Calendar writes remain NO-GO.

Next agent: backend-engineer for B1-01 to B1-06 and B1-13; devops-engineer for B1-10 (ICU), B1-11 and B1-14; qa-engineer for the missing tests listed above; Lead Architect to name the CSRF header and decide audit retention with the Product Owner.

## B1 fixes (backend-engineer, 2026-10-28)

Scope: B1-01, B1-03, B1-04, B1-05, B1-06, B1-07, B1-13. Not touched: B1-02, B1-08 to B1-12, B1-14, B1-15. Work was test-first: the 14 regression tests in `src/api/tests/Tether.IntegrationTests/Syllabus/SyllabusB1FixTests.cs` were all run against the unfixed code and failed (13 of 14 in the first run, all 14 in the second; the one that passed once was the B1-01 delete deadlock test, whose outcome depends on which side PostgreSQL picks as the victim, then failed on rerun with the expected 500). Fixed code: 14 of 14 pass, and the file was run four times green. No migration was needed.

| Id | Fix | Red-first test(s) |
|---|---|---|
| B1-01 | `DeleteCoreAsync` retries up to 5 times on 40P01/40001 (chain walk, shared `Pg` helper), jittered back-off. `ReapAsync` now takes the syllabus row locks (id order) of all expired leases first and updates only jobs of the syllabi it locked, so its order is syllabus then job, the same as delete, SetStage, Fail and Persist. | `B1_01_Deleting_a_syllabus_that_deadlocks...` (500 before), `B1_01_The_reaper_takes_the_syllabus_lock_before_the_job_row...` (40P01 from the reaper before) |
| B1-03 | Batch approve, skip and undecide write one audit row per applied item with its revision; the `batch_approved` summary row stays. | `B1_03_Batch_approve_writes_one_audit_row_per_approved_item...` (0 rows before) |
| B1-04 | Newest 20 sync/remove runs per syllabus kept (`Syllabus:SyncRunsPerSyllabus`); older runs, their items and their replay keys are pruned inside the locked transaction (bound per user: syllabi per user x 20). High-volume audit actions (`synced`, `sync_removed`, `exported`, `batch_approved`) stop at `Syllabus:AuditNoisyCapPerUser` (default 20000) per user; decision rows (approved, skipped, undecided, created, deleted) are never dropped. Maintenance purges `decision_audit` older than `Syllabus:AuditRetentionDays` using the existing evidence-purge switch, in batches of 5000. | `B1_04_Only_the_newest_sync_runs...`, `B1_04_High_volume_audit_rows_stop_at_the_per_user_cap...`, `B1_04_Audit_retention_defaults_to_400_days...`, `B1_04_The_configured_audit_retention_period_is_honoured` |
| B1-05 | `DELETE` checks ownership (user-scoped lookup) before cancelling the job. | `B1_05_Another_users_delete...` (owner's job was cancelled before) |
| B1-06 | Item create decides the key under the syllabus lock (deletes an expired record, replays a committed winner) and also handles the unique violation for a same-key create on another syllabus. Sync deletes an expired record for its key under the lock before inserting. | three `B1_06_*` tests (500, 500, 400 before) |
| B1-07 | Fingerprint material is length-prefixed per field, null distinct from empty (`SyllabusService.Material`), for syllabus create (paste and file), item create and sync. In-flight 24 h keys from before this change no longer match their fingerprint (mock-only, acceptable). | `B1_07_A_key_reused_with_a_body_whose_fields_shift...` (201 replay before) |
| B1-13 | The runner records its claim (job id, owner, attempt). SetStage, Fail and Persist take the syllabus lock, then the job row `FOR UPDATE` and proceed only if the job is still `running` with the same owner and attempt; Persist also requires the syllabus to be `extracting` or `proposing` and to have no AI items yet. | `B1_13_A_result_from_a_worker_that_lost_its_lease...` (two cases; 2 items persisted before) |

PROPOSAL pending Product Owner: `decision_audit` retention of 400 days, and the per-user noisy-audit cap (20000) and runs-per-syllabus (20). All three are configuration values, not decisions.

Not done or still open within these findings:
- B1-13: no heartbeat during a long extraction call (SetStage extends the lease only at stage boundaries, which is enough for the mock) and fault injection is still keyed on the environment name; both need to be revisited with a model extractor.
- B1-04: `purge_evidence` still has no production caller (account deletion), and the audit purge scans `decision_audit` by `occurred_at` without a dedicated index (the existing index is `(user_id, occurred_at)`); add an index when the table is large.
- B1-04: once a user reaches the noisy-audit cap, later syncs and exports leave no audit row until the retention purge frees space. Evidence of approvals is unaffected.
- B1-01: after 5 deadlock retries the delete still returns 500.
- B1-06: the item-create race on a key shared across two different syllabi is handled in code but has no dedicated test.
