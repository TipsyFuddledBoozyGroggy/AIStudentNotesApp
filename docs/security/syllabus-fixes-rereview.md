# Syllabus fixes (SB-01..SB-10), CI and PO-19 redirect: independent security re-review

Reviewer: security-engineer. Date: 2026-10-31. HEAD `a0e9f0b`, working tree clean at start. Commits reviewed: `cf2451a` (SB-01, CI matrix), `dc13b11` (SB-06, hub-socket fix), `ca0c123` (SB-02, 04, 05, 07, 08, 09, 10), `a0e9f0b` (PO-19 redirect after Stop). Severity per `docs/security/severity-policy.md`. New ID prefix **SBR-**.

**Method: read only.** I used `git show <sha> --stat` and full diffs, read the changed source and tests, and grep. I ran **no** test, build, script, workflow, probe or app (the dev API and a dev server were in use by another agent). Every verdict is READ, NOT RUN. Where a disposition says "failed on old code", that is the author's claim; I read the test and judged that it would fail without the fix, I did not watch it fail. Nothing in `src/`, `tests/`, `.github/` or `docker-compose*.yml` was changed.

## Result

**New findings: 0 CRITICAL, 0 HIGH, 1 MEDIUM, 9 LOW.** Nothing blocks a push. SBR-01 (and SB-01, still open) block production.

| ID | Verdict | Register status |
|---|---|---|
| SB-01 MEDIUM | **Partial.** Compose settings and canary are correct as read; never run in CI; production DB settings do not exist yet; residuals in SBR-04, SBR-05 | OPEN |
| SB-02 LOW | **Verified** (overflow fixed). New gaps in the backstop: SBR-06 | FIXED-VERIFIED |
| SB-03 LOW | **Not fixed** (deferred to UX/PO, unchanged) | OPEN |
| SB-04 LOW | **Verified** | FIXED-VERIFIED |
| SB-05 LOW | **Verified** for the mock. Calendar hook placement is wrong for a real provider: SBR-08 | FIXED-VERIFIED |
| SB-06 LOW | **Verified** for the main scenario (per-user keys, sign-out, syllabus delete, legacy keys). Residuals: SBR-09 | FIXED-VERIFIED |
| SB-07 LOW | **Verified** for browsers that send Fetch Metadata. Real browsers through the proxy not verified | FIXED-VERIFIED |
| SB-08 LOW | **Partial.** Decoder tests verified; the canary now drives multipart and a 415, but still not PATCH title/notes, sync, export or a failing persist | OPEN |
| SB-09 LOW | **Verified.** No deadlock found. New slot-holding gap: SBR-07 | FIXED-VERIFIED |
| SB-10 LOW | **Verified.** Test seam is null in production | FIXED-VERIFIED |

## Verdicts in detail

### SB-01 (MEDIUM): partial
- **Read:** `docker-compose.yml:15-32` passes `log_error_verbosity=terse`, `log_min_error_statement=panic`, `log_statement=none`, `log_duration=off`, `log_min_duration_statement=-1`, `log_min_duration_sample=-1`, `log_parameter_max_length=0` and `log_parameter_max_length_on_error=0` on the command line. Command-line `-c` options take precedence over `postgresql.conf` and `postgresql.auto.conf`, so an existing `pgdata` volume cannot undo them. Port binding is loopback only. `docker-compose.dev.yml` does not override the Postgres command.
- **Canary script** (`docs/ops/db-log-canary.sh`): active mode is sound. It refuses to pass unless the ERROR line for `ck_len` is in the log (`:42-46`), so an empty or broken log cannot read as clean, and the exit codes are 0/1/2. Passive mode greps the whole container log for `CANARY[-_]`. `DevelopmentLogPipelineCanaryTests` deliberately violates a CHECK with a `FAIL-CANARY-02-...` value (`LogPipelineCanaryTests.cs`, "A failing row"), so on default settings the passive scan does catch a real `Failing row contains` line. That is a good design.
- **Not verified:** the author's "red on default, green on hardened" run; no CI run exists (G5 NOT_RUN).
- **Still open:**
  - A managed production DB parameter group with the same settings. No deployment exists, so this is a recorded prerequisite.
  - An app role that is not a superuser. In dev, `cc_dev` is the superuser, and these parameters can be changed per session by a superuser.
  - SBR-04 (the gate script sits under `docs/`) and SBR-05 (`terse` does not strip values from primary messages).

### SB-02 (LOW): verified, with new gaps
- **Read:** `MockRuleExtractor.ContextWindow` bounds the total to 1,500 code points, steps by whole surrogate pairs, and moves unused budget to the other side. Trimming spaces only shrinks the window.
- `SyllabusLimits.Fits` re-checks title, dateText, timeText, confidenceReason, snippet and context lengths plus offset order. `Jobs.cs:145-148` drops items that do not fit.
- The integration test `SB02_a_3000_character_line...` and the 5 long-line unit cases exercise the fix.
- **Discrepancy with the disposition:** dropped *unclaimed dates* are filtered (`Jobs.cs:147`) but **not counted**. `droppedInvalid` adds only item drops (`:148`). Also, the web parses `counts.droppedInvalid` (`src/web/src/lib/syllabus/parse.ts:216`), but no component renders it (grep). Both points are in SBR-06.

### SB-04 (LOW): verified
- `SyllabusService.Material` (`:49-50`) writes each field as `len:value` and null as `~`, so the encoding is injective: a non-null `~` becomes `1:~`.
- `UploadIntake.MetadataCanonical` (`:130-131`) is versioned `metadata.v2`, keeps null distinct from empty, and includes `accepted`. It is nested as one length-prefixed field of the outer material (`SyllabusService.cs:96`). The paste material includes `accepted` too (`:107`).
- **Key compatibility:** a key stored before this change no longer matches, so a retry within 24 h gets 400 instead of a replay. That fails closed and affects dev data only. Acceptable.

### SB-05 (LOW): verified for the mock
- **Ordering** (read in `CourseEndpoints.cs:132-170`): `OnPurgingAsync` runs before `SaveChangesAsync` with the request token. `OnPurgedAsync` runs after the commit with `CancellationToken.None`, inside a try/catch that logs the exception type only. The Syllabus handler writes nothing before the commit (`CourseLifecycleHandler.cs:36-42`), so a rolled-back purge leaves the data intact.
- `JobCancellation` is a singleton (`SyllabusModule.cs:49`), so the scoped handler's `Cancel` reaches the runner's CTS and `IAiScopeCutoff` (`Support.cs:360-365`).
- **Gap-created syllabi:** found through surviving `syllabus_created` audit rows from the last 10 minutes (`:28`, `:52-54`). The Audit query is user-filtered (`SyllabusDbContext.cs:166`).
- **Audit rows:** one per gone syllabus. Syllabi already audited as deleted are skipped (`:61-65`).
- **FK race to 404:** `SyllabusService.cs:261` re-checks ownership after an FK violation. In PostgreSQL, a create racing the purge either commits first (and is then cascaded and audited) or fails the FK check after the delete commits. Not tested (the disposition says so).
- **Tests read:** `A_purge_mid_job_cancels...` (with a 3.5 s late-writer check) and `A_syllabus_created_in_the_purge_gap...` (a `PurgeGap` handler blocks after the pre-commit notice).
- **New issues:**
  - The calendar hook runs after the cascade has already deleted the event rows (SBR-08).
  - Info SBR-I6: the gap scan is not limited to the course.

### SB-06 (LOW): verified, residuals in SBR-09
- **Read in `storage.ts`:** keys are `tether.syllabus.u.<seg(userId)>.*`, and `seg` encodes dots, so one id cannot reach into another user's key space (there is a test for this). Each piece of clearing:
  - `setDraftOwner(me.id)` removes other users' keys and the legacy unscoped keys (`:66-71`).
  - `clearAllDrafts()` runs in a `finally` after `api.logout()` (`AuthProvider.tsx:42-46`), so a failed logout request still clears.
  - `clearSyllabusDrafts` runs on delete, Stop reading, and the "already gone" path (`Review.tsx:235,243`, `Processing.tsx:123,131`).
  - With no owner, nothing is read or written.
  - A pending debounced autosave cannot write after sign-out, because the owner is null.
- **Tests read (names):** `drafts.test.tsx` covers ownership, dot injection, clearAllDrafts, clearSyllabusDrafts, sign-in, delete, already-gone, Stop reading and create. There is no app-level sign-out test (only the unit test of `clearAllDrafts`).
- The remaining gaps are in SBR-09.

### SB-07 (LOW): verified for Fetch Metadata browsers
- **Read:** `IsCrossSiteRead` (`SyllabusEndpoints.cs:70-78`) is an endpoint filter on export only (`:60-61`). It runs before the handler, so before the id and ownership checks, the audit row and the export budget. It refuses each of these:
  - `Sec-Fetch-Site` repeated (`Count > 1`);
  - a combined value (`same-origin, cross-site` is one value that is not an exact match);
  - any value other than exactly `same-origin` or `none` (case-sensitive), including `cross-site`, `same-site` (a sibling subdomain) and the wrong case;
  - an `Origin` that is present but not allow-listed, including the literal `null`.
- **Cross-site GET navigation** carries `Sec-Fetch-Site: cross-site` in current browsers and is refused.
- **Missing Fetch Metadata** is still served: older browsers and non-browser clients. That is the accepted residual (audit noise and budget; the response cannot be read cross-origin).
- **Next.js proxy:** `next.config.mjs` uses `rewrites()`, which forwards the browser's request headers. The browser computes `Sec-Fetch-Site` against the page origin (`:3000`), which is what matters. Not verified in a real browser.
- **Residual, Info SBR-I2:** `none` is also sent when a link is opened from outside the browser (mail or chat apps).
- **Test read:** `SB07_export_refuses...`.

### SB-08 (LOW): partial
- **Read:** 9 malformed-file cases give 415 with nothing created, and a UTF-16BE astral file reaches ready (`SyllabusBuiltReviewFixTests.cs:152-182`).
- **Log canary:** it now drives a multipart upload with a filename sentinel and a lone-surrogate 415.
- **Still missing** from the review's list: PATCH title/notes, sync, export and a failing persist.
- **New nit, Info SBR-I8:** `WaitTerminalAsync` returns silently after 10 s without asserting a terminal state, so a stuck worker would make the upload leg of the canary vacuous.

### SB-09 (LOW): verified; no deadlock found
- **Read:** `UploadGate.EnterAsync` (`Support.cs:310-335`) is entered after the ownership check, the key lookup and the quota check (`SyllabusService.cs:64-85`), and before any body byte. An upload reserves 10 MB and a paste 1 MB. `Leave` runs in a `finally` only after a successful enter (`:131`).
- **How waiting works:**
  - A different key gets 429 at once.
  - A same-key request waits on the holder's `TaskCompletionSource`. The wait uses one monotonic deadline for the whole call, a waiter cap of 8 per slot, and the request token, so a cancelled waiter decrements the count in its `finally`.
- **Deadlock:** waiters hold no transaction and no pooled connection while they wait. EF opens a connection only per query unless a transaction is active, and none is active before `CommitCreateAsync`. The holder takes the advisory lock only inside `CommitCreateAsync`. I found no lock-order cycle.
- **Quota TOCTOU:** `CommitCreateAsync` re-checks the quotas under the per-user advisory lock (`:195`). So a request that passed the outer quota check before another create committed still gets 409 or 429 there. The outer check is an early filter only.
- **429:** the response carries `Retry-After: 60` (`SyllabusProblems.cs:92`).
- **Test read:** `SB09_a_paste_holds...`, which uses a stalling body.
- **New:** SBR-07 (a paste body has no data-rate floor while it holds the slot; pastes now share the 100 MB budget with uploads).

### SB-10 (LOW): verified
- **Read:** `DeleteOnceAsync` with `debrisDueBy` re-checks, after `LockSyllabusAsync` and in the same transaction, `status IN ('failed','needs_text')`, a due `purge_after`, and no student item (`SyllabusService.cs:345-351`).
- An item insert that commits before the lock is visible under READ COMMITTED. An insert that comes after the lock needs `FOR KEY SHARE` on the syllabus row, which conflicts with `FOR UPDATE`, so it waits and then fails the FK check.
- The job is cancelled only for a syllabus that was actually deleted (`Jobs.cs:275`).
- **`BeforeDebrisDelete`:** it is `internal` on the singleton `SyllabusMaintenance` (`Jobs.cs:245`). Its only assignment is in `SyllabusBuiltReviewFixTests.cs:392`, which resets it in a `finally`. Production code never sets it, so it is null in production. `InternalsVisibleTo` names only the test assemblies.
- **Observation, Info SBR-I1:** the selection query (`Jobs.cs:259-263`) has no status filter, unlike the re-check.

### SB-03 (LOW): not fixed
Unchanged and deferred to neurodivergent-ux-specialist and product-manager. It is still required before any real calendar write.

## New findings

### SBR-01 MEDIUM: the Trivy action is pinned to a SHA nobody cross-checked, and it can poison CI caches
- **Where:** `.github/workflows/ci.yml:392-414` (`aquasecurity/trivy-action@6c175e9c...  # v0.30.0`, three steps, runs on every code PR and on push to `main`); pins described in `ci.yml:5-7` and `docs/ops/ci-matrix.md` ("read from the public tag refs with `git ls-remote`").
- **Scenario:** a SHA read from a mutable tag is only as trustworthy as the tag was at the moment it was read. My recollection, which I have **not** verified, is that `aquasecurity/trivy-action` had its release tags force-pushed to malicious commits in a 2026 supply-chain incident. If the recorded SHA is one of those commits, or the action fetches an unpinned Trivy binary at run time, the step runs attacker code on the runner. Repository secrets are not exposed (there are none, and the token is `contents: read`). But a JavaScript or composite action receives the runtime cache token, and on `main` it can write the shared caches that the other jobs restore without integrity checks:
  - `~/.nuget/packages`, which is restored as-is (no lock file);
  - `~/.cache/ms-playwright` (the Chromium binary).

  The npm cache is protected by lockfile integrity hashes. Nothing is deployed, so the impact today is CI integrity. Severity: I was unsure between LOW and MEDIUM and took MEDIUM, because a poisoned build or test environment undermines every G5 result this CI is meant to produce.
- **Fix:**
  - vendor-verification-researcher confirms every pinned SHA against the publisher's release commit or advisory (Aqua's advisory for Trivy) and records date and link.
  - Pin the Trivy binary version and its checksum (`version:` input plus checksum), or run Trivy from a digest-pinned container image.
  - Give `image-scan` no cache access: it already uses no `actions/cache`. Consider running it in a separate workflow with no `push` trigger on `main`.
  - Owners: devops-engineer, vendor-verification-researcher.

### SBR-02 LOW: the .NET vulnerable-package gate fails open
- **Where:** `ci.yml:351-356`.
- **Scenario:** the step has no `shell: bash`, so GitHub runs it with `bash -e {0}` and **without** `pipefail`. The documented default for `bash` is `-eo pipefail` only when the shell is named explicitly; this is from GitHub's documentation and UNVERIFIED by a run. `dotnet list package --vulnerable ... 2>&1 | tee audit.txt` takes tee's exit status. If the vulnerability query fails (the feed is unreachable, or a project cannot be evaluated), `audit.txt` has no "High" line and `! grep` passes. The gate reports green without having checked anything.
- **Fix:** add `shell: bash` (which gives pipefail) or `set -o pipefail`. Also require a positive marker, for example fail unless the output contains "has no vulnerable packages" or a vulnerability table for every project, or parse `--format json`.

### SBR-03 LOW: gitleaks is downloaded and run without checksum verification
- **Where:** `ci.yml:330-334`.
- **Scenario:** `curl | tar` from a GitHub release URL with no hash check. A replaced release asset runs with access to the full-history checkout. This is acknowledged in `ci-matrix.md` but had no register row. Impact is the same class as SBR-01 (CI integrity, no secrets).
- **Fix:** verify the published `checksums.txt` signature, or pin a SHA-256 in the workflow; or use a digest-pinned container image.

### SBR-04 LOW: the SB-01 gate script lives under `docs/`, so edits to it are classified as docs-only
- **Where:** `ci.yml:58` (`^(docs/|\.claude/)|\.md$` means docs-only); `codeql.yml` has `paths-ignore: docs/**`; the script is `docs/ops/db-log-canary.sh`.
- **Scenario:** a change that only edits the canary script, for example one that makes it `exit 0`, skips the `api` and `e2e-real` jobs that run it. The PR shows green (skipped jobs report success), and the weakened gate is first exercised on a later code PR, where nobody looks at it. A PR can also edit `ci.yml` itself, so the real control is review plus branch protection (SEC-040). Still, an executable gate should never be classed as documentation.
- **Fix:** move it to `scripts/ops/` (or exclude `docs/ops/**` from the docs-only pattern). Add the shell scripts to CODEOWNERS once that exists.

### SBR-05 LOW: `terse` hides DETAIL but not values embedded in primary error messages; the canary covers CHECK only
- **Where:** `docker-compose.yml:15-32`, `docs/ops/db-log-canary.sh:31-37`.
- **Scenario:** some SQLSTATEs put the input value in the primary message, which `terse` keeps. Examples: 22P02 `invalid input syntax for type ...: "<value>"`, invalid enum input, date/time parse errors, and 42601 `syntax error at or near "<token>"`. This is from the PostgreSQL message catalogue and UNVERIFIED by a probe. Npgsql sends typed parameters, so today these errors need text that reaches a server-side cast (`::uuid`, `::date`, or a `text` column compared to a typed one), and I found no such path in the Syllabus SQL. A future raw SQL cast of user text would leak into the server log with no canary to notice.
- **Fix:** add a 22P02 probe (`SELECT $1::uuid` with the canary) to the active mode, and record a code rule that student text is never cast server-side. Production also needs a non-superuser app role (see SB-01).

### SBR-06 LOW: the length backstop can hide items silently
- **Where:** `Jobs.cs:145-148`; web `src/web/src/lib/syllabus/parse.ts:216` (parsed, never rendered).
- **Scenario:** an item that fails `SyllabusLimits.Fits` disappears from the review list. The only trace is `counts.droppedInvalid`, which no screen shows, and dropped unclaimed dates are not even counted. With the mock this needs a bug. With a real extractor, hostile syllabus text could steer the model into an oversize field for one item ("restate the following 300 times..."), and the student would never see that a date was there. That matters for a deadline tool.
- **Fix:**
  - Count unclaimed drops too.
  - Show "N dates could not be read. Check the original." in Review.
  - Prefer keeping a dropped item as an unclaimed date (its snippet offsets are already validated) over removing it.
  - Add a fixture where a hostile line yields an oversize title and assert that the student sees a trace.
  - Owners: backend-engineer, frontend-engineer, ai-agent-engineer (before a real extractor).

### SBR-07 LOW: a paste holds the per-user create slot and 1 MB of global budget with no data-rate floor or read timeout
- **Where:** `SyllabusEndpoints.cs:131-135` (the JSON branch only raises `MaxRequestBodySize`); `Support.cs:405-421` (`ReadBytesAsync`, no timeout); compare multipart `:128-129` (200 KB/s, 10 s grace) and `UploadReadSeconds = 60`.
- **Scenario:** after SB-09, a paste reserves the slot before its body is read. At Kestrel's default minimum rate (240 B/s after a 5 s grace; Kestrel docs, UNVERIFIED by probe), a 1 MB body can take about 70 minutes. During that time the same user's other creates get 429. Uploads, which already hold 10 MB slots (P2B1-12), now also starve pastes, which previously never hit the global budget. Many accounts are needed, which dev login does not offer.
- **Fix:** apply the same `MinDataRate` (or a lower one sized for 1 MB) and a read deadline to the JSON branch, linked to `ct`. Extend P2B1-12 to name pastes.

### SBR-08 LOW: the SB-05 "real provider hook" runs after the cascade has deleted the provider's event records
- **Where:** `CourseLifecycleHandler.cs:70` calls `MockCalendar.RemoveCourseEventsAsync` (`SyncService.cs:36`) post-commit. The calendar rows (and in calendar.v1, the provider event ids) were removed by the FK cascade inside the purge transaction.
- **Scenario:** the disposition presents this call as "the hook where a real provider would queue removal intents". At that point there is nothing left to look up, so a real Google integration built on this hook would orphan every event of a purged course, which is the exact SB-05 / CV-01 risk. Today it is a no-op on the mock, which is harmless.
- **Fix:** before Google, capture removal intents **inside** the purge transaction: an outbox row written by a `BEFORE DELETE` trigger on the course, or by `OnPurgingAsync` into a table that does not cascade, consumed post-commit. Add a test that a purged course with a synced event leaves exactly one removal intent. Owners: google-integration, backend-engineer.

### SBR-09 LOW: SB-06 residuals (expired session, course delete, no TTL, no disclosure)
- **Where:** `AuthProvider.tsx:31` (on 401 the owner becomes null but nothing is cleared); no `clearSyllabusDrafts` or course-draft clearing on course archive or purge (grep finds no caller in course components); `storage.ts` has no TTL; the syllabus `copy.ts` has no "kept on this device" text (only `CourseForm.tsx:130` has one).
- **Scenario:**
  - On a shared computer, a student whose session expired (no sign-out) leaves up to 50,000 characters of pasted text in localStorage until somebody next signs in to Tether in that profile. Anyone with the profile can read it from devtools before that.
  - Deleting or purging a course leaves its paste draft.
  - Nothing tells the student the text stays on the device.

  Clearing on 401 would destroy a draft whose session merely expired mid-typing, so this is a tradeoff to make on purpose.
- **Fix:**
  - A short TTL (for example 7 days, checked on read).
  - Clear the course's paste draft on course archive or purge.
  - Show "Kept on this device until you add it or sign out".
  - Add an app-level sign-out test, including logout failure.
  - Owner: frontend-engineer, with neurodivergent-ux-specialist for the copy.

### SBR-10 LOW: the same-key Quick recap race returns the winner without `Idempotency-Replayed`; this is the "4 vs 5" flake and it is a real product race
- **Where:** `src/api/src/Modules/Ai/Tether.Ai/CatchUp/CatchUpService.cs:115-126` (also `:131-136` and `:167-169`).
- **Scenario:** request R2 looks up its key at `:117` before R1 commits. R1 then commits its Pending row, and R2's coalesce query at `:123-126` finds that row and returns `Ok` **without** `Replayed`. Every assertion before the header count passed in the failing run (all 200, one id, one row), which matches this path exactly. Load only widens the window between `:117` and `:123`; the logic gap exists on an idle machine. The `:134-136` and `:167-169` fall-backs behave the same way when the found row carries the request's key.
- **Impact:** no second charge and no data mix-up (the queries are session-scoped and user-checked). A client relying on the header to know it got a replay is misinformed. Contract conformance, not confidentiality.
- **Fix:** in all three fall-backs, set `Replayed: true` when `found.IdempotencyKey == idempotencyKey`. Add a deterministic test with a hook between the key lookup and the coalesce query. Owner: ai-agent-engineer or backend-engineer.

## The other flaky tests
- **`RealtimeLogCanaryTests` ObjectDisposedException: undetermined.** No stack trace was recorded. The test closes the audio socket (1011) and the hub while background pumps may still log or cancel. The product has this pattern elsewhere: `JobCancellation.Cancel` already swallows ODE from a disposed CTS (`Support.cs:364`). It could be a teardown race in the audio pump or a test-host disposal. The next occurrence must capture the stack trace. If the frame is product code (a pump or lifecycle job), treat it as a defect, because an unobserved teardown exception in the recording path is the class that leaves server and screen disagreeing about recording state.
- **Migration rollback 40P01: test harness, not product.** The Syllabus Down deadlocks with the test host's own worker polling `extraction_jobs`. Production never rolls back under a running worker. The retry is limited to 40P01/40001 on transactional migrations (`notes-after-stop-rereview.md`). I agree. Related product behaviour (cascade versus job writer, B1-01) is already handled by bounded retries in `DeleteCoreAsync`.

## PO-19 redirect after Stop and the Make-notes follow-ups (a0e9f0b, dc13b11): no finding
- **No open redirect.** The target is `/lectures/${encodeURIComponent(sessionId)}` (`LectureStartPage.tsx:68`, `StartFlow.tsx` `openPageHref`), with `sessionId` taken from the server-created runtime. The fixed prefix and the encoding rule out `//host`, `..` and scheme injection. `<Link>` is internal.
- **Arrival marker** (`settings.ts:90-110`): sessionStorage (per tab), one key, removed on every read, and true only for an exact id match. A stale marker (the move failed after it was written) causes at most one extra focus move and announcement on that lecture's page.
- **Preference key** `tether.lecture.u.<seg(userId)>.stopMove.v1`: user-scoped, with dots encoded as in SB-06. The value is a fixed string, and anything else reads as "ask". It holds no content.
- **Info SBR-I4:** the key name holds the opaque user id and survives sign-out, so a shared profile shows that an account id used Tether. Acceptable; optionally clear it on sign-out for consistency with SB-06.
- **Recording safety:** the move fires only when the client shows Stopped, the server state is Stopped, there is no fatal state, and the reason is the user's own Stop (`LectureScreen.tsx`, `userStopReason`). Unmount stops the track and ends the audio after one tick (`useLectureSession.ts:99-111`). Consent files and the Stop confirm are untouched (diff stat).
- **Hub transport** (`hubTransport.ts:62-132`): the epoch guard closes superseded connections. Late frames from a superseded connection are ignored (`:75`), and `dispose` detaches synchronously. I found no path where a newer caller joins a stale socket. This removes the orphaned-socket count against the per-user hub cap.

## CI workflows: what holds (Info SBR-I5, SBR-I7)
These were read only and never run on GitHub.
- **Trigger:** `pull_request` only, with no `pull_request_target` or `workflow_run`, so fork PRs get no secrets and a read-only token.
- **Permissions and checkout:** top-level `permissions: contents: read`. Each job restates its permissions; CodeQL adds `security-events: write` and `actions: read`. `persist-credentials: false` is set on every checkout.
- **Untrusted input:** no expression is interpolated into a `run:` script. The PR base SHA and the push `before` value go through `env:` (`ci.yml:43-46`). Changed file names are only echoed.
- **Pins:** every `uses:` is pinned to a full SHA (cross-checks pending, SBR-01).
- **Artifacts** (on failure only, 7 days): `api.log`, `web.log`, Playwright traces and screenshots. They hold synthetic data and dev-login cookies of an ephemeral CI API. That is acceptable as long as CI never touches real data. On a public repository, any signed-in GitHub user can download them.
- **Credentials:** the CI DB credentials are the compose dev defaults, not secrets.
- **Canary script, SBR-I7:** it interpolates its optional canary argument into SQL. The input is operator-only, so this is not an attack surface.
- **A PR can edit the workflow itself.** The docs-only skip and every check therefore depend on review and branch protection, which are not configured (SEC-040).

## Observations (Info, not findings)
- **SBR-I1:** the debris selection (`Jobs.cs:259-263`) lacks the re-check's status filter. A row that stops qualifying but keeps a due `purge_after` is re-selected on every pass. With `LIMIT 100`, 100 such rows would block real debris. This is unreachable today, because `SetStage` nulls `purge_after`, and only the SB-10 test creates that state by SQL. Add `AND s.status IN ('failed','needs_text')` to the selection.
- **SBR-I2:** export accepts `Sec-Fetch-Site: none`, which is also sent for links opened from outside the browser (mail or chat apps). This is the same accepted residual class as missing Fetch Metadata.
- **SBR-I3:** `next.config.mjs` says the session cookie is `SameSite=Strict`. `IdentityModule.cs:64` sets `Lax`. Fix the comment.
- **SBR-I4:** the PO-19 preference key holds the user id and survives sign-out (see above).
- **SBR-I5:** CI artifacts contain dev cookies and synthetic logs (see above).
- **SBR-I6:** `OnPurgedAsync` scans the user's `syllabus_created` rows from the last 10 minutes across **all** courses. Two concurrent course purges by the same user can each audit the other's gap syllabi, which gives duplicate `syllabus_deleted` rows. This is audit noise, not data loss. Joining on the course (for example, by recording `course_id` in the gap scan) would remove it.
- **SBR-I7:** the canary script puts its argument into SQL (operator input only).
- **SBR-I8:** `WaitTerminalAsync` in the log canary does not assert that a terminal state was reached.

## What I read and what I did not verify
- **Read:**
  - Full diffs of `ca0c123` (src and the canary test), `dc13b11` (AuthProvider, storage, CreateForm, ItemEditForm, Review, Processing, hubTransport), `cf2451a` (compose, canary script, ci.yml, codeql.yml, dependabot.yml, ci-matrix.md) and `a0e9f0b` (LectureScreen, LectureReopenPage, LectureStartPage, StartFlow, Notices, useLectureSession, settings, StopMoveDialog, PreferencesPanel, AppShell).
  - `CourseEndpoints.Delete`, `SyllabusService` create, commit and delete, `Jobs.cs` persist and maintenance, `Support.cs` gate, JobCancellation and JsonBody, `CatchUpService.StartAsync`, `RealtimeLogCanaryTests`.
  - The new integration tests for SB-05, 07, 08, 09 and 10.
  - The unit test names for the gate, drafts and preferences.
- **Not run:** any test, build, workflow, the canary script, the app, a browser. Test counts in the commit messages (131 syllabus, 600 integration x3, 2967 unit and so on) are the authors' claims.
- **Not verified:**
  - Real browsers' Fetch Metadata through the Next proxy.
  - Kestrel's default minimum data rate (SBR-07).
  - PostgreSQL message contents (SBR-05).
  - GitHub's default-shell flags (SBR-02).
  - Whether the pinned action SHAs match the publishers' release commits, and the Trivy incident I recall (SBR-01).
  - Multi-instance behaviour (gate and cancellation are per process, ADR-020d).
  - The web unit tests' content beyond their names.
