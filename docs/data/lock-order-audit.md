# Lock-order audit: writers versus cascade deletes (session, course, user)

Date: 2026-11-01. Author: database-architect (read-only audit). Trigger: lesson L-LOCKORDER and the lessons-log entry of 2026-11-01 (a transcript write chosen as the deadlock victim of a session delete closed the socket 1011 instead of 4409; fixed in `TranscriptWriter.AppendAsync`, reproduced by `RelayDeleteDeadlockTests`).

Status of this document: analysis from reading code and migrations. Nothing here was run: no dev stack, no PostgreSQL, no tests, no `EXPLAIN`, no SQL logging. Every "deadlock possible" below is derived from the code and PostgreSQL's documented lock rules, not observed. Where an answer depends on something I could not see, it says so.

Severity calibration: ratings use `docs/security/severity-policy.md` (CRITICAL / HIGH / MEDIUM / LOW) and are PROVISIONAL AI ratings. They are calibrated to the stated personal-use scope (one student, one instance, ADR-020d, no production deployment): every race below needs two actions on the same session, course or syllabus inside a window of milliseconds, and every victim's transaction rolls back cleanly, so no finding can lose or corrupt committed data. Nothing is rated HIGH or CRITICAL. If the scope widens to many real users, re-rate F-1 and F-3 upward.

## 1. Result in one paragraph

The transcript writer was the only writer that took a child-adjacent lock and then needed `FOR KEY SHARE` on a cascade parent while a delete was in progress, and it is fixed. The same shape exists in three more places: the Final-summary append (`ai.lecture_notes ... FOR UPDATE`, then an insert into `ai.summary_revisions`), the Syllabus writers that hold the syllabus row and then insert a row that references `courses.courses` (`calendar_events`, `idempotency_keys`), and, as the other side of those cycles, the course purge itself, which has no deadlock retry at all (the session delete and the syllabus delete do). Five other pairs are safe by lock compatibility or by single-lock statements. One more depends on a statement order I could not verify. No account-deletion path exists in code yet.

## 2. Lock rules this analysis relies on (PostgreSQL documented behavior)

1. A child insert or update of a foreign-key column runs an RI check that takes `FOR KEY SHARE` on the referenced parent row. `FOR KEY SHARE` conflicts only with `DELETE` and `FOR UPDATE` (and key-changing updates). It does NOT conflict with a normal `UPDATE` (`FOR NO KEY UPDATE`), so a state change on `lectures.lecture_sessions` does not block writers.
2. A `DELETE` of a parent row locks it exclusively, then its `ON DELETE CASCADE` RI triggers delete child rows, each taking that row's lock and waiting if another transaction holds it.
3. So any transaction that holds a lock another transaction's cascade needs, and then inserts a row whose FK points at the parent being deleted, closes a cycle. PostgreSQL aborts one waiter with 40P01 after `deadlock_timeout` (default 1 s, assumed, not read from the compose files). The later waiter is normally the victim (see the reasoning in `RelayDeleteDeadlockTests`).
4. Composite FKs `(session_id, user_id)` reference the unique index `ux_lecture_sessions_id_user_id`. The unique index on `user_id` is partial (`ux_lecture_sessions_one_live_per_user`) and is not an FK-key index, so a state update stays `FOR NO KEY UPDATE`. I did not verify this against a live server.
5. A deadlock victim's whole transaction is rolled back. A child row written by a victim is never kept, so these races cannot leave orphan or half-written rows.

## 3. FK and ON DELETE inventory (from EF configurations and migrations)

Cascade parents and what hangs off them. `->` means "references". All composite FKs are `(x_id, user_id)` unless stated.

| Parent | Child -> FK name | ON DELETE | Source |
|---|---|---|---|
| `identity.users` | `identity.sessions.user_id`; `courses.courses.user_id`; `lectures.lecture_sessions.user_id` (`fk_lecture_sessions_user_id`); `ai.ai_usage.user_id` (`fk_ai_usage_user_id`); `syllabus.idempotency_keys.user_id` | CASCADE | M003, M004, M006, A001, S002 |
| `courses.courses` | `lectures.lecture_sessions.course_id` (`fk_lecture_sessions_course_id`) | CASCADE | M006 |
| `courses.courses` | `syllabus.syllabi (course_id,user_id)` (`fk_syllabi_course_user`); `syllabus.calendar_events (course_id,user_id)`; `syllabus.idempotency_keys (course_id,user_id)` | CASCADE | S002 |
| `lectures.lecture_sessions` | `session_events`, `session_counters`, `audio_tickets`, `idempotency_keys` (`fk_*_session_user`) | CASCADE | M006 (the single-column EF FKs from M005 are also CASCADE) |
| `lectures.lecture_sessions` | `transcript.transcript_events`, `transcript.audio_progress` | CASCADE | T001 |
| `lectures.lecture_sessions` | `ai.summary_revisions`, `ai.catch_ups`, `ai.lecture_notes` | CASCADE | A001, A005 |
| `lectures.consent_records` | `lecture_sessions.consent_record_id`, `consent_events.consent_record_id` | RESTRICT (consent records are never deleted by a session delete) | M005, M006 |
| none (no FK by design) | `lectures.audit_events`, `consent_records.session_id`, `consent_events.session_id`, `ai.ai_usage.session_id`, `ai.global_spend` | n/a: survive deletes | LecturesDbContext, Ledger.cs |
| `syllabus.syllabi` | `canonical_texts`, `extraction_jobs`, `items (syllabus_id,user_id,course_id)`, `unclaimed_dates`, `sync_runs`, `idempotency_keys`, `calendar_events` | CASCADE | S001 |
| `syllabus.items` | `item_proposals`, `item_alternatives`, `calendar_events (item_id,user_id)` | CASCADE | S001 |
| `syllabus.sync_runs` | `sync_run_items` | CASCADE | S001 |

Cascade depth: user -> course -> session -> {counter, events, tickets, keys, transcript, progress, summaries, catch-ups, notes}; user -> course -> syllabus -> {text, jobs, items -> {proposals, alternatives, calendar events}, runs -> items, keys}. There is no FK from any `ai` table to a session other than the three above, so the budget ledger takes no session lock (intentional, `Entities.cs` says deleting a lecture must not refund budget).

## 4. Delete and purge paths (the "opposite order" side)

| Path | Lock sequence | Retry on 40P01 | Caller on being the victim |
|---|---|---|---|
| `SessionService.DeleteAsync` (single `SaveChanges`: `db.Sessions.Remove`, plus audit/consent inserts) | session row (X) -> cascade children, trigger order | Yes, `MaxAttempts` 3, jitter 20-120 ms, `ExistsAsync` check, then `Problems.ConcurrencyConflict()` (409) if it still fails | Handled. Covered by `RealtimeAudioTests.Deleting_a_session_that_deadlocks...` |
| `CourseEndpoints.Delete ?purge=true` (`db.Courses.Remove`, one `SaveChanges`) | pre-commit handlers (Lectures: Stop each live session, write `session.delete` audit rows, in-memory AI tombstone; Syllabus: cancel jobs) -> course row (X) -> cascade: sessions (and their children), syllabi (and theirs), calendar_events, idempotency_keys | NO. Only `DbUpdateConcurrencyException` is caught | Unhandled `DbUpdateException` (40P01) -> 500. See F-1 |
| `SyllabusService.DeleteCoreAsync` | syllabus row `FOR UPDATE` -> tombstone UPDATE of idempotency_keys -> audit insert -> `DELETE syllabi` -> cascade | Yes, 4 retries, back-off `20-120 ms * (attempt+1)` | Handled. Covered by `SyllabusB1FixTests.B1_01_*` (job row writer; reaper lock order) |
| Syllabus debris/retention (`SyllabusMaintenance`) | calls `DeleteCoreAsync`; `PurgeTextAsync` takes the syllabus row `FOR UPDATE` first, then items/unclaimed/text | Debris: via `DeleteCoreAsync`. `PurgeTextAsync`: no retry, but the outer loop catches and logs, next tick retries | Safe: same syllabus-first order as the writers |
| Account deletion | Does not exist. `IdentityEndpoints` maps only `/me`, `/auth/logout`, `/dev/login`. `purge_evidence(p_user_id)` and M006's equivalent are the only prepared hooks ("account deletion owns the call") | n/a | See F-6 |
| `ExpiredRowSweeper` (tickets, idempotency keys) | two single-statement `DELETE`s, no parent lock | n/a | Safe, see section 6 |

## 5. Findings

IDs are local to this document. "Test" = an existing test that exercises the exact interleaving (not merely the happy path).

### F-1  Course purge has no deadlock retry, and its pre-commit side effects survive a failed purge — MEDIUM (provisional; LOW under strict personal-use calibration)

- Pair: `CourseEndpoints.Delete` (purge) against any writer that is inside a cycle with a cascade child or parent of the course: F-2 and F-3 below, and the already-fixed transcript writer.
- Lock order of the purge: course row -> `lecture_sessions` rows -> their children; course row -> `syllabi` rows -> their children; then `calendar_events`, `idempotency_keys`. The first four FK triggers on `courses` fire in constraint creation order (M006 sessions, then S002 syllabi, calendar_events, idempotency_keys). I inferred that order from migration order; I did not confirm trigger names on a live server.
- Deadlock possible: yes, with the writers in F-2 and F-3 (not with the transcript writer in practice: `EndSessionsForCourseAsync` Stops every Active/Paused session first, after which `AllocateAsync` returns null after a lock-only `UPDATE` of the counter, and no insert follows).
- If the purge is the victim: `SaveChangesAsync` throws `DbUpdateException` wrapping `PostgresException 40P01`; only `DbUpdateConcurrencyException` is caught, so the request returns 500. The purge itself rolled back (course intact), but the pre-commit handlers already committed: live sessions are Stopped (consent Stop events), a content-free `session.delete` audit row exists for every session of the course (`EndSessionsForCourseAsync`, `SaveChangesAsync` before the notify), in-flight AI calls are cancelled and the sessions are tombstoned in `SessionCutoff` (in memory, until restart). The audit rows therefore claim a deletion that did not happen, and a client retry writes a second set of `session.delete` rows for the same sessions. The same happens today on a 409; the syllabus handler deliberately avoids this by only cancelling pre-commit.
- Test: none for the course purge as victim. `RealtimeAudioTests.Purging_the_course_closes_the_audio_socket_via_the_watchdog` and `B2FixTests.Purging_a_course_writes_a_Stop_consent_event...` cover the happy path only.
- Smallest fix: wrap the `SaveChangesAsync` in `Delete` in a 3-attempt loop on 40P01/40001 (clear the change tracker, reload the course with the query filter, `Remove` again, jitter), exactly like `SessionService.DeleteAsync`; if still failing return `Problems.ConcurrencyConflict()` (409), never 500. The pre-commit handlers need not rerun (they are idempotent and already ran). Optional second step: write the pre-commit `session.delete` audit rows only inside the purge transaction or skip rows whose audit already exists (the post-commit gap audit already uses an "already audited" filter; the pre-commit one does not).

### F-2  Final-summary append: `ai.lecture_notes FOR UPDATE`, then insert into `ai.summary_revisions` — LOW

- Pair: `SummaryStore.AppendAsync(row, base, ct, pressId)` (called by `SummaryEngine.FinalAsync`/compaction with `status == "Final"`) against `SessionService.DeleteAsync` or the course purge.
- Lock order, writer: `ai.lecture_notes` press row (`FOR UPDATE`, in a transaction) -> insert `ai.summary_revisions` (FK check: `FOR KEY SHARE` on `lectures.lecture_sessions` row). Delete: session row (X) -> cascade rows including `ai.lecture_notes` (needs the press row lock the writer holds). That is the same cycle as the transcript bug with the press row playing the counter row.
- Deadlock possible: yes, when a delete of a Stopped session starts while a "Make notes" press is inside this append, a window of a few milliseconds after a long provider call. A single-session delete announces `OnDeletedAsync` (which cancels in-flight AI) only AFTER the commit, so nothing cuts the append off first. The course purge announces before commit, which narrows but does not close the window (an append already inside its transaction continues).
- If the writer is the victim: 40P01 is raised from `SaveChangesAsync` as `DbUpdateException`. `AppendAsync` catches only `UniqueViolation` and `ForeignKeyViolation`, so it propagates through `FinalAsync` and `NotesRunner.RunAsync` to `NotesRunner.WorkAsync`'s generic catch: logged by type, `MarkUnavailableAsync` (a no-op on a deleted press). Nothing is lost (the session is being deleted anyway); no wrong close code; one noisy "Notes job failed: DbUpdateException" warning.
- If the delete is the victim: retried by `DeleteAsync`; on retry the writer has committed or its transaction waits and then gets 23503 -> `SessionGone`. Fine.
- Without `pressId` (rolling summary, compaction): no transaction, a single `INSERT`, one lock (`KEY SHARE`). No cycle. Safe.
- Test: none. `NotesMigrationGuardTests`/`PersistenceTests` only retry migrations.
- Smallest fix: add a bounded retry on 40P01/40001 inside `SummaryStore.AppendAsync` (re-run the whole method: the retry re-takes the press lock, then gets 23503 -> `SessionGone`, or succeeds). Alternative, "parent first": `SELECT 1 FROM lectures.lecture_sessions WHERE id=@s FOR KEY SHARE` before the press `FOR UPDATE`. That removes the cycle outright (the writer then holds nothing while it waits) but reads another module's schema from raw SQL, which I do not recommend; retry stays inside the AI module.

### F-3  Syllabus writers hold the syllabus row, then insert a row that references the course — LOW (may be MEDIUM with F-1)

- Pair: `SyncService.SyncAsync` (inserts `calendar_events` through `MockCalendar.UpsertAsync`, `sync_runs`, `idempotency_keys`) and `ItemService.CreateAsync` with an Idempotency-Key (inserts `idempotency_keys`) against `CourseEndpoints.Delete ?purge=true`.
- Lock order, writer: `syllabus.syllabi` row (`FOR UPDATE` via `LockSyllabusAsync`) -> (sync) item rows `FOR UPDATE` -> insert `calendar_events` / `idempotency_keys`, whose FKs include `-> courses.courses (id, user_id)` (`KEY SHARE` on the course row). Purge: course row (X) -> cascade to the same syllabus row. The documented "syllabus then job/item" order protects the syllabus delete and the extraction job, but the course sits above the syllabus and the writer's FK to the course takes it AFTER the syllabus lock.
- Deadlock possible: yes, student syncs or hand-adds an item with a key in the same instant they purge the course (the pre-commit syllabus handler cancels extraction jobs only, nothing blocks these two writers).
- If the writer is the victim: `SyncAsync` catches only a unique violation on `ux_idempotency_keys_scope`; `ItemService.CreateAsync` the same. 40P01 propagates: 500 on that one request, transaction rolled back, no partial calendar rows. The course is going away anyway.
- If the purge is the victim: F-1.
- Plain `SyllabusService.CommitCreateAsync` (create): holds an advisory lock and counters, inserts the new syllabus (`KEY SHARE` on course) and holds no row the purge needs. No cycle; a purge that commits first yields 23503, which is already mapped to 404 (SB-05).
- Extraction `PersistAsync`/`SetStageAsync`/`FailAsync`/`ReapAsync`: syllabus row, then job row, then items and children whose FK targets are `syllabi` (already locked by the same transaction). No FK to `courses`, so no cycle with the purge. Safe.
- Test: none for purge versus sync/item writers. The syllabus-delete versus job-writer cases are covered (`SyllabusB1FixTests`).
- Smallest fix: F-1's retry makes the purge side safe. For the writer side, translate 40P01 into the same answer the writer gives when the syllabus is gone (404) or into a retry once at the endpoint (re-run, the second attempt finds no syllabus -> `NotFound`). I would not change the lock order: acquiring the course row first (`SELECT ... FROM courses.courses ... FOR KEY SHARE`) would also work but crosses the module boundary the same way as F-2's alternative.

### F-4  `TransitionAsync` (pause/stop/resume) versus `DeleteAsync` — LOW, depends on an unverified EF statement order

- Pair: `SessionService.TransitionAsync` (user pause/stop, `SessionLifecycleJob` auto-pause/auto-stop/hard cap, course archive) against `DeleteAsync` of the same session.
- Transition `SaveChanges` updates `session_counters` (`last_seq++`), updates `lecture_sessions` (`NO KEY UPDATE`, xmin concurrency token), inserts `session_events`/`consent_events`/`audit_events`, deletes `audio_tickets`. If EF emits the session UPDATE before the counter UPDATE the lock order matches the delete (session, then counter) and nothing can deadlock. If it emits the counter UPDATE first, the order is counter -> session, the opposite of the delete's session -> counter, which is the transcript cycle again. EF orders unrelated updates by its modification-command comparer (table name, then key) after a topological sort over FK edges. That would put `lecture_sessions` before `session_counters` alphabetically, which is the safe order, but I did not confirm this on the EF Core version in use (version not read) and no test or log asserts it.
- If Transition is the victim: it catches only `DbUpdateConcurrencyException` and unique violations (then `continue`s in its 3-attempt loop). 40P01 propagates: user pause/stop returns 500; `SessionLifecycleJob.TickAsync` does not catch around the call (the per-tick wrapper I did not read), so the rest of that tick is skipped and the next tick retries. The student's consent state is not changed by the failed attempt (rolled back), and a retry of Stop succeeds, but a failed Pause/Stop returning 500 is the exact failure mode ADR-019b says must not happen ("Pause and Stop are never refused").
- Transcript writer versus Transition: no cycle in either EF order (the writer's `KEY SHARE` does not conflict with Transition's `NO KEY UPDATE`; both lock the counter in the same direction).
- Test: none.
- Smallest fix: add `IsDeadlockOrSerialization(ex)` to the existing `catch ... continue;` condition in `TransitionAsync` (one line; it already loops 3 times and re-reads from the top, and a re-read of a deleted session returns the existing 404). Separately, confirm the emitted statement order with a SQL log or a `CommandTrap` assertion.

### F-5  Budget reservation: any exception in `ReserveAsync` opens the global AI breaker — LOW

- Pair: `BudgetGate.ReserveAsync` (advisory lock per user -> `ai.global_spend` row upsert -> insert `ai.ai_usage`, one transaction) against `UsageLedger.SettleAsync` and `ReservationReaper` (existing `ai_usage` row `UPDATE` -> `ai.global_spend` upsert).
- Lock order: Reserve takes `global_spend` then inserts a NEW `ai_usage` row nobody else can hold; Settle takes an existing `ai_usage` row then `global_spend`. Because Reserve's `ai_usage` row is brand new, no transaction can wait on it, so there is no cycle between them. Two Settles lock different `ai_usage` rows then the same single `global_spend` row in the same order: no cycle. Reaper versus Settle on the same row: same order, the second updates 0 rows (`where outcome = 'reserved'`). `RecordAsync` does `SaveChanges` and `AdjustAsync` as two separate statements/transactions: no multi-lock transaction. No session FK on the ledger: no session-delete interaction. Deadlock possible: not on current paths.
- What is not safe: `AiGateway.RunAsync` wraps `ReserveAsync` in `catch (Exception)`: any failure, including a one-off 40P01, 40001, or a dropped connection, calls `health.RecordLedgerFault()`, which opens BOTH breaker lanes for `Ai:Breaker:OpenSeconds` (default 60 s) for every user ("fail closed"). One transient DB error therefore disables AI for a minute. `SettleAsync` already retries 3 times; `ReserveAsync` does not.
- Test: none for a transient failure in Reserve.
- Smallest fix: in `AiGateway.RunAsync`, retry `ReserveAsync` once (jitter) when the exception chain is 40P01/40001 before calling `RecordLedgerFault`.

### F-6  Account deletion does not exist; cascade from `identity.users` would reach every table above — INFO (design watch item, not a finding)

- `fk_ai_usage_user_id` and every user-owned table cascade from `identity.users`. A future "delete my account" that is a single `DELETE FROM identity.users` would hit the user's sessions, courses, syllabi, ledger rows and idempotency keys in one cascade, with no pre-commit step that stops recording or cancels AI. A three-way cycle is possible on paper: user delete holds the user row and waits on an `ai_usage` row held by `SettleAsync`; that Settle waits on `global_spend`, held by a `ReserveAsync` of the same user, which waits on `KEY SHARE` of the user row for its `ai_usage` insert.
- Recommendation when it is built: do not rely on one big cascade. Stop recording, cancel AI and jobs, delete sessions and syllabi through the same paths that already retry (session delete, `DeleteCoreAsync`), then delete the user row last, with the same bounded 40P01 retry. Add a deterministic `CommandTrap`/`pg_blocking_pids` test per path, as `RelayDeleteDeadlockTests` does.

### F-7  Adjacent, not lock order: FK violation after a parent was deleted returns 500 — LOW

- `SessionService.CreateAsync` catches only unique violations. If the course is purged between `IsOwnedByAsync` and the insert, the FK to `courses.courses` fails with 23503 and the request returns 500 instead of 404. `IssueTicketAsync` has the same gap for a session deleted between the read and the insert. Syllabus create (SB-05), catch-ups, notes, summary appends and the progress save already map 23503 to "gone".
- Fix: catch `ForeignKeyViolation` in those two methods and return the existing not-found problem.

### F-8  Three copies of `IsDeadlockOrSerialization` — INFO

`TranscriptWriter` (private), `SessionService` (private) and `Syllabus.Pg` (internal) are identical. Every proposed fix above needs a fourth or fifth caller. Smallest consolidation: one helper in `Tether.SharedKernel` (new file, outside this audit's mandate to edit) and replace the copies. Not required for correctness.

## 6. Pairs examined and judged safe (with the reason)

| Writer | Locks held in order | Why no cycle with a cascade delete |
|---|---|---|
| `TranscriptWriter.AppendAsync` (counter UPDATE -> `transcript_events` insert) | counter row, then `KEY SHARE` session | Real cycle, FIXED: 3 attempts on 40P01/40001, retry sees the committed delete and `AllocateAsync` returns null (relay closes 4409). `fill` actions are pure, so a retry is safe. Covered by `RelayDeleteDeadlockTests` (writer victim) and `RealtimeAudioTests` (delete victim). |
| `TranscriptWriter.SaveAcceptedMsAsync` (`audio_progress` upsert) | one row; FK `KEY SHARE` on insert only | Single lock. A deleted session makes the insert fail 23503; the relay's call site catches everything (`RelayConnection` line 158) and logs the type. |
| Rolling summary and compaction (`AppendAsync` without `pressId`) | single INSERT, `KEY SHARE` session | Single lock; unique violation -> `Stale`, FK violation -> `SessionGone`. |
| `CatchUpService` insert, `CatchUpStates.ExpireAsync`, `CatchUpRunner.ProcessAsync` | one row per statement; the insert takes `KEY SHARE` | Single lock per statement; 23503 mapped to NotFound in the service, swallowed (`catch (DbUpdateException)`) in the runner. Startup `SweepOrphansAsync` is a multi-row UPDATE, but it runs once before serving (I did not verify host start ordering). |
| `NotesService.RequestAsync` (+ `audit.RecordNotesRequestedAsync`), `NotesStore.*` | audit insert (no FK) in its own statement; one `lecture_notes` insert or UPDATE | Single lock per statement; 23503 mapped to NotFound; `CompleteReadyAsync` is guarded by `state = 'Pending'`. |
| `SessionService.IssueTicketAsync`, `AudioTicketService` consume | ticket row (update or insert), audit insert | Insert takes `KEY SHARE` only; update changes no FK column; nothing else held. (23503 gap is F-7.) |
| `SessionService.EndSessionsForCourseAsync` | Stop transitions in separate transactions, then audit rows | Not concurrent with the purge statement; but see F-1 for what survives a failed purge. |
| `ExpiredRowSweeper` | two autocommit multi-row DELETEs, no parent lock | Deleting child rows needs no parent lock; a delete waits on at most one sweeper statement, and the sweeper waits on nothing outside its own statement. |
| `ExtractionRunner` claim/stage/fail/persist, `ReapAsync`, `PurgeTextAsync`, `SyllabusMaintenance` | syllabus row first (`ORDER BY id` in the reaper), then job/item rows | One declared order, matching the syllabus delete; tested for delete-versus-job-writer and reaper order (`B1_01_*`). No FK from these writes to `courses`. |
| `GlobalSpend`, `UsageLedger.RecordAsync/SettleAsync`, `ReservationReaper` | see F-5 | Same direction everywhere; `ai_usage` has no session FK. `SettleAsync` retries 3 times on any exception. |

## 7. Coverage and priorities

| Finding | Existing deterministic test | Proposed test (same method as `RelayDeleteDeadlockTests`: `CommandTrap` plus `pg_blocking_pids`, no sleeps) |
|---|---|---|
| F-1 purge as victim | none | Hold the course row path with a writer from F-3, assert 204 and exactly one set of `session.delete` rows |
| F-2 Final append | none | Trap the `summary_revisions` insert, start `DELETE /sessions/{id}`, release after the delete's deadlock check; assert 204, no rows, press ends cleanly |
| F-3 sync/item key vs purge | none | Trap the `calendar_events` insert, start purge, assert 204 on purge and a non-500 answer on the writer |
| F-4 transition vs delete | none | First log or assert the emitted statement order; if counter-first, trap and race a Stop against a Delete |
| F-5 reserve transient | none | Fault-inject one 40001 in `ReserveAsync`, assert the breaker stays closed |
| F-7 FK to 404 | none | Purge between ownership check and insert via a trap |

Suggested order (smallest, highest value first): F-1 retry (and 409 instead of 500), F-4 one-line condition, F-2 retry in `SummaryStore`, F-5 single retry in the gateway, F-3 endpoint-level handling, F-7 two catches. None of these touches a migration or a contract; F-1 changes only the failure path of `DELETE /courses/{id}?purge=true`.

## 8. What I read versus what I could not verify

Read in full: `docs/agents/project-playbook.md` L-LOCKORDER row, `docs/agents/lessons-log.md` entry, `docs/security/severity-policy.md`; `TranscriptWriter.cs`; `RelayDeleteDeadlockTests.cs`; `SessionService.cs`; `SessionPortsService.cs`; `LecturesDbContext.cs`; `AiDbContext.cs`; `TranscriptDbContext.cs`; `SummaryStore.cs`; `Ledger.cs` (GlobalSpend, UsageLedger, BudgetGate); `ReservationReaper.cs`; `AiGateway.cs`; `CatchUpService.cs`; `NotesService.cs`; `CourseEndpoints.cs` (Delete and Update); Lectures and Syllabus `CourseLifecycleHandler.cs`; `Jobs.cs` (runner, maintenance); `SyncService.cs` (sync and remove); `SyllabusService.cs` (create commit, delete); `CourseLifecycleHandler` for Lectures.

Read in part: `ItemService.cs` (create and the lock scaffold; the decision and batch paths only by grep), `SyllabusCore.cs` (lock and audit helpers), `SummaryEngine.cs` (the `AppendAsync` call sites and catch blocks), `RelayConnection.cs` (`PersistAsync`, the progress save and the exception handling around it), `SessionLifecycleJob.cs` (the call site only), `AudioTicketService.cs` and `ExpiredRowSweeper.cs` (statements only), `SessionCutoff.cs` (`OnDeletedAsync`), `RealtimeAudioTests.cs` (the delete deadlock test), migrations by `grep` for `REFERENCES`/`ON DELETE` (S002 and M006 sections read).

Not read: the full DDL of the migrations (FK list is from grep and from the files I opened, not from `pg_constraint`); `SyllabusEndpoints.cs`, `UploadIntake.cs`, `Dtos.cs`, the extractor; `MockCalendar.UpsertAsync` body; `LectureHub.cs` and the STT code; the host's global exception handler (so "500" above means an unhandled exception reaching ASP.NET Core's default problem handling; I did not confirm the mapping); the compose and CI settings for `deadlock_timeout`, connection pooling or isolation level (READ COMMITTED assumed).

Could not verify without running anything: (a) the EF Core statement order in `TransitionAsync` (F-4); (b) FK trigger firing order on `courses` (F-1, F-3); (c) that a state UPDATE of `lecture_sessions` takes `FOR NO KEY UPDATE`; (d) that any of F-1 to F-5 reproduces, none was reproduced; (e) the actual `pg_locks` picture under load. No test was run; the "covered by" statements mean the test exists and targets that interleaving, not that I observed it pass.

## Resolution (2026-11-01, by the Lead after the fixes)

- **F-4 was real.** The audit could not verify EF's statement order and guessed it was safe. Recording the Stop batch with a CommandTrap (EF Core 10.0.14) shows one command in this order: DELETE audio_tickets, INSERT audit_events, INSERT consent_events, UPDATE lecture_sessions, UPDATE session_counters, INSERT session_events. With a ticket outstanding, Stop locks the ticket row and then the session row, the opposite of a session delete, so a deadlock cycle exists. Fixed (retry on 40P01/40001 inside `TransitionAsync`), reproduced deterministically before the fix.
- **Fixed, with deterministic tests (`LockOrderFixTests`):** F-1 (course purge retries, then 409; no duplicate `session.delete` audit rows), F-2 (Final append retries), F-4, F-5 (reserve retried once before a ledger fault), F-7 (a parent deleted between check and insert returns 404, not 500), F-8 (one shared `PgErrors` helper).
- **Not done:** F-3 (syllabus sync and item create vs purge: a deadlock loser still returns 500 for that one request; the F-1 retry covers the purge side). F-6 (account deletion) waits for that feature.
- **Known residuals:** the `session.delete` audit rows a failed purge writes before the delete survive a 409 (no longer duplicated; moving them into the purge transaction needs a cross-module change). The F-4 test depends on EF's batch order.
