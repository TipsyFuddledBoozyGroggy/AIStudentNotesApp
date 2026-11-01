# Agent lessons log

One entry per defect, failed run, wrong report or repeated re-explanation that a better agent definition or playbook could have prevented. Written by `agent-refiner` (proposals) and accepted by the Lead (who applies changes to `.claude/agents/*.md` and `docs/agents/project-playbook.md`).

Entry format:
`### YYYY-MM-DD <short title>` / **Symptom** / **Root cause** / **Which agent(s)** / **What would have prevented it** / **Status** (PROPOSED, APPLIED in <commit>, REJECTED with reason).

### 2026-11-01 Syllabus create failed on UTC browsers (found by first GitHub CI run)
- **Symptom:** real-stack e2e `syllabus.spec.ts` got 400 on create; passed locally.
- **Root cause:** client maps a UTC browser to `Etc/UTC`; server allow-list lacked it; dev machine is US-zoned so it never showed.
- **Agents:** frontend-engineer (client helper), backend-engineer (allow-list), qa-engineer (tests not run in UTC).
- **Prevention:** playbook rule "test zone/locale-dependent code in UTC"; QA definition: pin and vary the timezone in e2e.
- **Status:** APPLIED (playbook L-UTC; Playwright zone pinned, explicit UTC test).

### 2026-11-01 Phone status bar wrapped on Linux (CI geometry failure)
- **Root cause:** ~8px slack at 390px; Linux text shaping slightly wider.
- **Agents:** frontend-engineer.
- **Prevention:** playbook L-BAR: keep >= 16px slack in tight rows; do not tune to the Windows render only.
- **Status:** APPLIED.

### 2026-11-01 Gitleaks 3666 false positives; CodeQL red
- **Root cause:** full-history scan of test literals; CodeQL upload impossible on a private repo (fixed by making the repo public); C# buildless restore failed on .NET 10.
- **Agents:** devops-engineer (workflows authored without a run), production-readiness-auditor.
- **Prevention:** playbook L-GITLEAKS / L-CODEQL; devops definition: dry-run locally what can be run (gitleaks full history), state plainly what only a GitHub run can prove.
- **Status:** APPLIED.

### 2026-11-01 Slice 7 agent reported the dev server "still running" after it had exited
- **Prevention:** playbook rule 2 and L-STATUS (verify state with a command before reporting).
- **Status:** APPLIED.

### 2026-11-01 B2PushTests 4401 vs 4409 on a slow CI runner
- **Symptom:** one integration test failed only on GitHub (`Expected 4409, Actual 4401`).
- **Root cause:** test race; `hello` is fire-and-forget, archive committed before ticket redemption. Product behaviour (4401 for any ticket failure) is the documented contract.
- **Agents:** backend-engineer / qa-engineer (tests written without waiting for the first ack).
- **Prevention:** playbook L-HELLO. Open question: does the frontend treat 4401 on first hello as "session not available"? (not checked)
- **Status:** APPLIED (4 tests now wait for acks; deterministic reproduction `B2ArchiveHelloRaceTests`).

### 2026-11-01 axe target-size failure only on the Linux runner (slice 8c)
- **Root cause:** sticky site header paired with page targets by axe; the 200px scroll sampling missed the bad window on Windows. The defect existed on Windows too (found with 2-3px sampling).
- **Agents:** frontend-engineer (header), qa-engineer (sampling too sparse to prove absence).
- **Prevention:** playbook L-STICKY; a deterministic "header straddle" geometry check now exists.
- **Status:** APPLIED. Residual: QA definition line on stating the sampling step when claiming absence (PROPOSED P4 in `refinements/2026-11-01-first-ci-day-incidents.md`).

### 2026-11-01 Backend agent refused a Lead instruction that contradicted the contract (good outcome)
- **Symptom:** the Lead's prompt told `backend-engineer` to add bare `UTC` to the syllabus zone allow-list; `API_CONTRACTS_P2.md` 2.3 (commit 96a1caf) says `Etc/UTC` is the only UTC id and bare `UTC` is `400`. The agent refused.
- **Root cause:** the Lead prompt conflicted with the contract (delegation process, not an agent defect). Prompt text itself was not available to me; evidence is the contract diff and the Lead's report.
- **Agents:** none at fault; backend-engineer behaved correctly.
- **Prevention:** playbook item "contracts and decisions beat instructions" (P1) so this holds for every agent; Lead delegation checklist (P5).
- **Status:** PROPOSED (`refinements/2026-11-01-first-ci-day-incidents.md`).

### 2026-11-01 Lead re-stated environment facts in delegation prompts
- **Root cause:** facts lived only in the Lead's head until f8b45f3; playbook section 2 now holds them. Remaining risk is the Lead copying them again and drifting.
- **Agents:** none (Lead process).
- **Prevention:** `docs/agents/delegation-checklist.md` (P5): point at playbook sections, send only task deltas.
- **Status:** PROPOSED.

### 2026-11-01 Dev server reported "still running" after exit: wording follow-up
- **Prevention:** tighten playbook item 2 to require quoting the check used (P2). Entry above remains APPLIED for the base rule.
- **Status:** PROPOSED (follow-up only).

### 2026-11-01 Periodic audit of 18 definitions
- **Findings (ranked, details in `refinements/2026-11-01-periodic-audit.md`):** (1) stale deliverable paths in 5 assurance definitions, plus a self-contradiction in the legal one; (2) "advisory" role sentence contradicts implementing roles in 12 definitions; (3) identical method and escalation boilerplate in 12 definitions belongs in the playbook; (4) repo visibility contradiction (gh says PUBLIC; codeql.yml comment, diagnosis doc and L-CODEQL assume private); (5) gate statuses duplicated in the playbook and stale in RELEASE_GATES G5; (6) five playbook lessons lack log entries; (7) no `adhd-ux-specialist` leftovers; all 18 names match filenames.
- **Status:** PROPOSED.
- **Open (not a definition change):** does the web client treat close code 4401 on first hello as "session not available" (L-HELLO question)? Route to `frontend-engineer`.

### Pre-log entries (provenance from the findings register and the Lead; added 2026-11-01 so the log indexes every playbook lesson)
- **L-DEVSERVER:** running `next build` or a second `next dev` corrupted `.next`; dev server returned 500 on every page. Seen several times during the redesign slices.
- **L-RATE:** dev login (10/min/IP) and per-user limits caused false 429s when specs ran back to back; the hub Join limit is configured under `Realtime:*`, not `RateLimiting__*`.
- **L-CHECKOUT:** a `git checkout` of a file that held another agent's uncommitted work was blocked by the permission guard before it ran.
- **L-DROP:** SBR-06 (a backstop silently dropped data, hiding deadlines); SBV-02 (cap drops uncounted).
- **L-FORGE:** SBV-01 (extractor-written snippet text could be stored as a quote).

### 2026-11-01 Lead committed an agent's unfinished files with `git add -A`
- **Symptom:** commit 569bb6b contained the SBV-04 tests and a partly finished `DocumentText.cs` while the backend agent was still working; HEAD briefly had failing tests. The Lead's check (`git show --stat | grep src/`) missed it because `--stat` abbreviates long paths.
- **Root cause:** Lead process: `git add -A` with agents running, and a check that could not see the problem.
- **Prevention:** delegation checklist item 7: while any agent is running, add files by explicit path; verify a commit with `git show --name-only`. Not an agent-definition change.
- **Status:** APPLIED (checklist). Fixed forward in 7cff155; nothing was pushed in between.

### 2026-11-01 Update on L-HELLO open question
- The question 'does the web client treat 4401 on the first hello as session not available?' was answered by reading the code and adding six unit tests (commit 126bea0): yes. The related hardening (cap on repeated 4401) is PO-20af.

### 2026-11-01 Deleting a streaming session sometimes closed the socket 1011 instead of 4409 (found by CI)
- **Symptom:** `RealtimeAudioTests.Deleting_a_streaming_session_...` failed once in CI (Expected 4409, Actual 1011); passed many times locally.
- **Root cause (real product bug):** a transcript write and a session delete take the same two Postgres locks in opposite order (counter row then session row, versus session row then counter row). Postgres aborts one side with a deadlock error; when the writer was aborted, the relay closed 1011 instead of 4409. Which side loses is timing.
- **Fix:** the transcript writer retries on 40P01/40001 (up to 3 attempts); the retry sees the committed delete and the relay closes 4409. Real errors still close 1011.
- **Agents:** backend-engineer / database-architect (lock order across modules), qa-engineer (a rare flake was the only signal).
- **Prevention:** playbook L-LOCKORDER: a child-table writer that also touches a parent-adjacent counter row must treat deadlock (40P01) as retryable, like the cascade delete does. Follow-up: audit the other cascaded writers (AI summary revisions, catch-ups, notes).
- **Status:** APPLIED (deterministic reproduction `RelayDeleteDeadlockTests`).

### 2026-11-01 Lock-order audit (follow-up to the 1011/4409 deadlock)
- An audit that cannot verify an ordering must say so and leave the finding open; here it guessed F-4 was safe and the engineer's CommandTrap showed it was a real cycle. Prefer recording the actual statement order (CommandTrap) over inference. Status: APPLIED (docs/data/lock-order-audit.md Resolution section).
