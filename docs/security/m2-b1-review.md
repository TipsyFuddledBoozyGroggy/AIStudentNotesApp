# M2 increment B1 security review (Lectures module)

Reviewer: security-engineer. Date: 2026-10-27. Scope: commit 0dbbcca "Add Lectures module B1" against `docs/API_CONTRACTS.md` (frozen v1; sections 8, 10, 14) and `docs/security/m2-contract-review.md`.

**No gate is cleared by this review.** G1 (recording/consent), G2, G3, G4 and G5 are unchanged. The consent text is DRAFT and nothing here approves it.

Method labels: **READ** = code reading only. **RAN** = executed. Existing suite RAN in Docker (sdk:10.0, network `aistudentnotesapp_default`, contracts mounted at `/shared/contracts`): ContractTests 99/99, ArchitectureTests 12/12, IntegrationTests 177/177 pass. My probes RAN from a scratch copy of `src/api` outside the repo (`...\scratchpad\api\tests\Tether.IntegrationTests\ProbeTests.cs`), against a throwaway `cc_test_*` database created by the test factory. The repo working tree is unchanged by this review (git status clean).

## Verdict

No Critical or High findings. No release-blocking defect for the B1 scope (REST state machine, consent registry, tickets). Four Medium findings should be fixed in B2 before the audio socket ships, because B2 is what turns them from data-integrity issues into recording-consent issues.

What I checked and found sound (READ unless marked):
- Consent gating: a version is accepted only if in the server registry (C-13); invalid or missing consent is 403 `consent_required`, and course ownership is checked first so a foreign course probe learns nothing. RAN: P1 control returned 403 for an unregistered version.
- Consent events: Start, Pause, Resume, Stop are each written in the same SaveChanges as the state change, seq bump and audit event. DELETE of an Active/Paused session writes a Stop consent event plus `session.stop` and `session.delete` audit events. RAN (P4): kinds `Start,Stop`, audit `session.start,audio_ticket.issued,audio_ticket.rejected,session.stop,session.delete`, `consent_records` kept (1), tickets 0.
- Ticket: 256-bit CSPRNG, base64url, only SHA-256 stored, unique index on hash and on session_id, 30 s lifetime, bound to user and login session id, single use by row delete (concurrent loser gets the concurrency exception), pause/stop/delete remove it, all failures return the same `false`. RAN (P4): a foreign user consuming another user's ticket gets `false` and does not burn it (row still present).
- Cross-user access (C-09): every entity has a `UserId == CurrentUserId` query filter plus the ownership interceptor. RAN (P4): another user's DELETE returns 404, same as missing.
- Ticket endpoint is rate limited per user. RAN (P5): 6 x 200 then 429.
- Error and log leakage (F-08): problems use a fixed vocabulary; the exception handler logs type and trace id only; EF Update and Database.Command loggers are silenced in `Program.cs`. READ. Existing canary tests pass (RAN, not re-derived by me).
- Concurrency: one-live-session-per-user partial unique index (C-18); xmin token on sessions; retry loops of 3 on transition, delete and issue. READ.

## Findings

### B1-01 Medium. Course delete leaves recordable sessions behind (known gap, ADR-019, reproduced)
Evidence (RAN, P5): delete the course (204), then `POST /sessions/{id}/resume` returns 200, `audio-ticket` returns 200, and `GET /sessions/{id}` returns 200. Resume and ticket issuance never re-check that the course still exists or is owned. A session whose course the student deleted can be resumed and receive an audio ticket, i.e. recording continues under a consent that was attested for a course that no longer exists, and the transcript data survives the course purge the student asked for.
Fix: ADR-019 FK cascade in B2 (lecture_sessions.course_id to courses ON DELETE CASCADE). Until then, `TransitionAsync(Resume)` and `IssueTicketAsync` should call `ICourseOwnership.IsOwnedByAsync` and return 404. Add an integration test that fails today. Hub/socket must close on cascade delete (already listed in section 14 as open).

### B1-02 Medium. Consent and audit evidence is not append-only and not tamper-evident
Evidence (RAN, P2): as the application DB role, `UPDATE lectures.consent_events SET kind='Stop'`, `DELETE FROM consent_events` and `DELETE FROM audit_events` all succeed. SEC-007 requires consent events as a record of start/stop; the code treats them as append-only but the database does not enforce it, and the app role is the table owner.
Fix: in B2 or M006, add `BEFORE UPDATE OR DELETE` triggers on `consent_records`, `consent_events` and `audit_events` that raise, with a documented SECURITY DEFINER purge path for user deletion and retention expiry; run the app as a non-owner role with INSERT/SELECT only on those tables. Record the retention period for this evidence (see B1-09). The Down migration drops these tables outright; mark M005 Down as dev-only.

### B1-03 Medium. No DB-level tie between a child row's user_id and its session's user_id
Evidence (RAN, P2): the database accepted an `audio_tickets` row with `user_id` = user B for user A's session, and `UPDATE lecture_sessions SET user_id` to another user. The unique index `ux_lecture_sessions_id_user_id` was created (READ) but no child table uses it as a composite foreign key, so it is unused. App-layer filters and the interceptor are the only guard; M1 deferred row-level security (ADR-013).
Fix: composite FKs `(session_id, user_id) -> lecture_sessions(id, user_id)` on session_events, session_counters, audio_tickets, idempotency_keys, and `(consent_record_id, user_id) -> consent_records(id, user_id)` on lecture_sessions and consent_events (add the unique key to consent_records). Add a `pg_constraint` test. Revisit ADR-013 RLS at M003 as already planned; the composite FKs are cheap now.

### B1-04 Medium. Idempotency fingerprint is ambiguous and replay bypasses consent validation
Evidence (RAN, P1): the fingerprint is `courseId|title|textVersion|acknowledged` joined by `|` with no escaping. Request 1: title `T|draft-1`, version `draft-1` (201). Request 2 with the same key: title `T`, version `draft-1|draft-1` (an unregistered version, which alone returns 403 as the control shows) returns 201 with `Idempotency-Replayed: true`. The replay path runs before the consent check, so a request carrying invalid consent is answered as success.
Impact: low data exposure (same user, own session, no new session or consent record), but it violates the stated rule that invalid consent is never accepted, and a client could believe a bad consent was honoured.
Fix: hash a canonical encoding (length-prefixed fields or JSON), and validate consent syntax (registered version and `acknowledged`) before consulting the replay record, or include the validation result in the replay decision. Add a test with the delimiter collision.

### B1-05 Low. Audit table amplification and attacker-chosen session ids from ticket consume
Evidence (RAN, P3): 200 garbage `ConsumeAsync` calls wrote 200 `audio_ticket.rejected` rows, each carrying the caller-supplied session id (a random GUID), including implausible-shape tickets that never touch the hasher. Consume has no rate limit yet (B2 socket). Self-pollution only (rows are user-scoped), but it is unbounded write growth from one cookie and dilutes the audit signal.
Fix: in B2 apply a per-user limiter on socket upgrade; coalesce rejected audits (one per user per session per minute) and write `SessionId` only when the session exists for the user. Do not write an audit row for shape-invalid tickets, or count them in a metric instead.

### B1-06 Low. Transition spam grows rows 3x per call, bounded only by the generic write limit
Evidence (READ): each pause/resume adds a session_event, consent_event and audit_event. Write limit is 30/min per user, so roughly 130k rows per user per day. Section 14 lists the per-event seq rate cap as open for B2.
Fix: B2 per-session transition cap (for example 12/min and a hard maximum per session), returning 429, plus the 3 h hard cap job.

### B1-07 Low. Consent text immutability is by convention only
Evidence (READ): `ConsentRecord` stores `TextVersion` only. `ConsentTextRegistry` holds text in memory; editing `draft-1` wording in place would silently change what historical records attest. No text hash is stored.
Fix: store `SHA-256(text)` in `consent_records`/`consent_events`, and add a test asserting registry text for each retained version matches a committed hash. When counsel-approved text replaces the draft, add a new version and keep `draft-1` as non-acceptable for new sessions but resolvable for audit.

### B1-08 Low. Resume does not re-validate the consent version
Evidence (READ): `IssueTicketAsync` re-checks `registry.IsAccepted(row.TextVersion)` (contract 14.6) but `TransitionAsync(Resume)` does not. If a version is retired (for example when G1 text changes), a Paused session can resume and then be denied only at ticket time. The result is safe because the ticket is the gate, but state and consent disagree.
Fix: apply the same check on Resume and return 403 `consent_required`. Add a test using a registry with a retired version.

### B1-09 Low / policy. Retention and erasure of consent evidence is undecided
Evidence (READ): `consent_records`, `consent_events` and `audit_events` carry `user_id` and `course_id` with no FK to `identity.users` or courses. ADR-019 only cascades `lecture_sessions`. After user deletion these rows would remain indefinitely with the deleted user's id. Session id is deliberately not an FK (survives delete) which is the right call for evidence, but the retention period and the user-erasure behaviour need a decision (candidate: keep content-free evidence for a fixed period, then purge by the SECURITY DEFINER path in B1-02; on account deletion, retain pseudonymised or delete per counsel).
Fix: Product Owner and `legal-compliance-reviewer` to decide; document in `docs/security/`. Do not mark user-delete cleanup done until the test covers these three tables.

### B1-10 Low. Expired ticket rows and idempotency rows are never swept
Evidence (RAN, P3): after 5 h the expired ticket row is still stored (1). Idempotency rows are only removed when the same key is looked up again (READ). Rows are hashed and small, and tickets are replaced on next issue or removed on pause/stop/delete, so exposure is limited; growth is bounded per session for tickets, unbounded for idempotency keys.
Fix: B2 background sweeper for `audio_tickets.expires_at < now` and `idempotency_keys.created_at < now - 24h`; add indexes on `expires_at` and `(user_id, created_at)` for the sweep.

### B1-11 Info. Misleading comment, weak check constraint, two limiter registrations
- `AudioTicketService` summary says a wrong-user attempt "also burns" the ticket. RAN (P4): it does not (query filter hides the row), which is the safer behaviour. Correct the comment so nobody "fixes" it.
- `ck_lecture_sessions_stopped_at` forbids stopped_at on Active/Paused but does not require it for Stopped. Add `state <> 'Stopped' OR stopped_at IS NOT NULL`.
- Ticket limiter is fixed-window 6/min, which allows a 12-ticket burst across a window boundary; acceptable. `AddRateLimiter` is called in both `LecturesModule` and `Program.cs`; options merge, and the 429 behaviour was RAN (P5), so no action beyond a comment.
- Title is stored unvalidated beyond length (control characters, bidi). Not a vulnerability for JSON output; the web client must render as text only.

### B1-12 Info. Items correctly deferred to B2 (confirm before the socket ships)
Origin check at ticket consume (F-04), closing live hub/socket connections on delete or pause, logout drops connections, hub 60 s revalidation, Failed/abandonment/auto-stop jobs, the per-event seq cap, and a real-DB test that Delete racing an Active session with in-flight transcript writes leaves no orphaned transcript rows (writers must check the session exists, as the code comment says). The delete-vs-active race itself is correct for B1 data: xmin concurrency token and a 3-attempt retry; the Stop consent event and audit are written in the same transaction as the delete (RAN, P4). Not RAN: a concurrent delete versus pause stress test.

## The 8 documented gaps
User-delete and course-purge orphans: confirmed, now ADR-019, reproduced as B1-01 and B1-09. I did not independently enumerate the other gaps listed in the B1 report because the report itself was not in the commit; if it lists items not covered above, send it and I will map them.

## Not run
Concurrent delete/pause/consume stress with parallel connections; migration up/down against a populated database; log capture of the F-08 canary beyond the existing tests; real browser cookie/Origin behaviour; any provider or microphone path (none exists yet).

## Release criteria impact
Open Medium items B1-01 to B1-04 go on the risk register. Recommend they close before B2 merges the audio socket. No change to any gate in `docs/RELEASE_GATES.md`.
