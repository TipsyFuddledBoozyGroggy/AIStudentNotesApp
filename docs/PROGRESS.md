# Progress

## Iteration 0 — Design round (2026-10-27)
**Completed:** design documents only. No application code, no tests run, no builds.

| Agent | Output |
|---|---|
| product-manager | `PRODUCT_REQUIREMENTS.md` |
| adhd-ux-specialist | `ux/student-experience.md` |
| backend-engineer | `architecture/backend-foundation.md` |
| database-architect | `data/schema-m1-m2.md` |
| ai-agent-engineer | `ai/live-lecture-ai-design.md` |
| speech-engineer | `architecture/speech-pipeline.md` |
| security-engineer | `SECURITY.md` |
| qa-engineer | `qa/test-strategy.md` |
| Lead Architect | `ARCHITECTURE.md`, `DECISIONS.md`, `DEVELOPMENT_ROADMAP.md`, this file |

**Known issues:** no .NET SDK on the dev machine (backend blocked). Specialist docs not yet reconciled field-by-field; vendor/pricing/model facts unverified; latency, token and cost figures are estimates.
**Next:** PO decision on PO-1; scaffold devops + frontend (mock-backed); backend after SDK.

## Iteration 1 — Milestone 1 foundation (2026-10-27)
**Completed (verified by Lead Architect re-running):**
- Web (`src/web`): Next.js dashboard/course creation; 20 unit tests pass; lint, typecheck, build pass; `npm audit` 0 vulnerabilities (scoped postcss override on `next`).
- API (`src/api`, .NET 10 via Docker): Identity + Courses, cookie auth, dev login, Origin CSRF, ownership filters; build 0 warnings; 7 architecture + 60 integration tests pass.
- Real-stack E2E (`src/web/e2e`): 11/11 Playwright tests pass on chromium against the real API and Postgres, including axe with color-contrast (0 violations) and cross-user denial.
- Infra: Postgres compose, dotnet SDK container, `api` service on 127.0.0.1:5080, `npm run dev:real`. CI workflow exists but has never run.

**Agents:** frontend-engineer, devops-engineer, backend-engineer, security-engineer (review), database-architect (review), qa-engineer.

**Known issues / open findings:** security F-01..F-08 (`docs/security/m1-review.md`; F-01, F-02, F-03 block non-dev use); DB review items (`docs/data/m1-review.md`: FK migration, delete-race 500, Down leaves schemas, schema doc drift incl. semesters/quiet_mode/purge_after not in code). Dev login limited to 10/min/IP slows repeat E2E runs. Not covered: Firefox/WebKit, mobile reflow, screen-reader pass.
**Decided:** PO-1 resolved: .NET 10 via Docker. ADR-014..017 in DECISIONS.md.
**Next:** backend fix pass for F-01..F-03, F-05..F-07, ADR-014 migration, delete race, Down cleanup; then API_CONTRACTS.md and Milestone 2 start (mock STT/AI).

## Iteration 2 — M1 security/DB fix pass (2026-10-27)
**Fixed (test-first; verified by Lead Architect re-run):** F-01 server-side sessions (M003), F-02 absolute lifetime, F-03 rate limiting + trusted-proxy forwarded headers, F-05 opaque ETag, F-06 headers on 500/429 + Permissions-Policy + HSTS outside Dev, F-07 `__Host-` cookie + persisted key path, ADR-014 FK (M004), delete-race handling.
**Results:** build 0 warnings; 8 architecture + 90 integration tests pass; Playwright 11/11 on the hardened API.
**Not independently re-reviewed:** security-engineer has not yet re-reviewed these fixes.
**Open:** F-04 and F-08 (M2 gates); SEC-005 lockout; session-row retention job; cookie keys not encrypted at rest and no compose volume for them; deploy needs `Security:ETagKey` and trusted-proxy config; EF history tables prevent dropping schemas in `Down` (decision: shared history schema?); schema doc drift (semesters, quiet_mode, purge_after).
**Next:** security re-review; API_CONTRACTS.md; Milestone 2 against mock STT/AI.

## Iteration 3 — Pre-M2 hardening and contract draft (2026-10-27)
**Done (Lead Architect re-ran the suite):** N-03 (login revokes carried session), N-04 (IPv6 /64 limiter keys), N-09 (ExecuteUpdate/ExecuteDelete ban, regex-based), F-08 (no exception object in logs, three framework loggers silenced, log-canary test shown to fail on the old code). Build 0 warnings; 10 architecture + 99 integration tests pass (run in the SDK container on the compose network with `ConnectionStrings__Default` set).
**Contract:** `API_CONTRACTS.md` draft v1 (ADR-018), NOT frozen. Security review `security/m2-contract-review.md` (C-01..C-32, read-only review, nothing run); High items folded into §8, mediums open.
**Caveats:** silencing EF save-failure loggers reduces diagnosability; N-09 scan is textual. F-04 (WebSocket Origin) still open until the hub exists. Playwright not re-run this iteration. Nothing committed at time of writing.
**Open PO decisions:** server-relayed audio, `speaker` labels in M2, auto-stop timings (C-17), in-flight audio wording (G1).
**Next:** triage mediums, freeze contract, write `src/shared/contracts` schemas, then M2 backend/frontend against mocks.

## Iteration 4 — M2 contract freeze, Lectures B1, first lecture UI (2026-10-27, reconstructed from git history after a session restart)
**Commits since iteration 3** (messages only; not re-reviewed line by line by the Lead in this session): contract v1 frozen with all contract-review findings folded in (`283c5b0`), `lecture.v1`/`speech.v1` JSON schemas + fixtures + contract tests (`660fb08`), M2 UX spec (`cfc35c4`), `lecture.v1` client library with validators, reducer, resume controller and mock source (`e5d2791`), Lectures module B1: sessions, consent registry, REST state machine, audio tickets (`0dbbcca`), B1 security review (`c2d96d7`, no Critical/High; 4 Medium) and fixes (`003c545`: cascade FKs, composite FKs, append-only evidence, safe idempotency), first live-lecture screens + dev harness (`587eaab`).
**Verified by Lead Architect at HEAD 587eaab (re-run after restart):** API build 0 warnings; 12 architecture + 99 contract + 199 integration tests pass; web 250 unit tests pass, lint and typecheck clean, `npm audit` 0 vulnerabilities; production `next build` succeeds.
**Not verified this session:** Playwright E2E not re-run since the M2 UI landed; CI has never been confirmed passing on GitHub (13+ commits unpushed); the B1 fixes were not independently re-reviewed.
**Known issue:** production build includes `/dev/lecture-harness/*` test fixture pages (being gated).
**Still no real capability:** no hub, no audio socket, no transcript persistence, no AI summaries, no real STT. Everything lecture-related is mock-backed or UI-only. All 5 release gates remain unpassed.
**In progress:** B2 (SignalR hub, ticketed audio WebSocket relay, replay STT with "Demo source" flag, transcript persistence, B1 deferrals incl. F-04 WebSocket Origin).
**Next:** B3 AI summaries (TL;DR, Quick recap) on MockAi; frontend wiring to the hub; Opus security review of B2 before the audio socket is treated as done.

## Iteration 5 — B3 AI module verification and review (2026-10-27/28, overnight, uncommitted tree)
**State found:** uncommitted B3 (`src/api/src/Modules/Ai`, `Tether.AiTests`, `Transcript.Contracts`) and frontend realtime wiring, not yet verified.
**Verified by Lead Architect re-run:** API build 0 warnings; 105 contract + 77 AI + 15 architecture + 303 integration tests pass; web 256 unit tests, typecheck and lint pass (1 unused-var lint warning in `e2e/lecture.spec.ts:64`).
**Fixed:** architecture and persistence tests did not know the Ai module or `Transcript.Contracts` (module-ref allow-list, rollback order, cross-schema FK allow-list now include `ai`).
**Playwright (real stack):** 15/16 on the first run. `lecture.spec.ts` "another user cannot read…" intermittently never receives the demo transcript when run after other lecture tests (passes alone and when paired with the denied-mic test). Cause not found; suspect leftover per-user hub/socket limits (`MaxAudioSocketsPerUser=1`) or timing. NOT yet diagnosed.
**Security review of B3:** `docs/security/m2-b3-review.md` (code reading only): 0 Critical, 0 High, 3 Medium, 5 Low. Fixes delegated to backend-engineer (B3-01..05, 07, 08); B3-06 (queued AI work after Pause/Stop/Delete) and B3-12 (PII redaction) need Product Owner decisions before any real adapter.
**Still true:** no real STT or Claude adapter; everything runs on mock/replay. All 5 release gates unpassed. Nothing committed.
**E2E flake lead (not fixed):** after the "pause turns the microphone off" test, "another user cannot read…" took 43s to see its first transcript line (vs ~3s alone), so the next session's audio socket appears to be refused or delayed while the previous socket is still registered. `RelayRegistry.TryAdd` (`Relay/RelayConnection.cs:521`) refuses when the user is at `MaxSocketsPerUser` (1) and the client then backs off. Real-world risk: a reconnect after a network blip may be refused until the old socket is reaped. Needs a backend look (replace-or-cutoff old socket for the same user, or prompt cleanup on client close) plus a regression test.

## Iteration 5b — B3 security fixes (2026-10-28, uncommitted)
**Fixed by backend-engineer, re-run by Lead Architect:** B3-01 sanitizer (fixpoint, format chars, link forms), B3-02 budget reservation/settle (migration A002), B3-03 HTTP route tests (`AiRoutesTests`, `AiBudgetTests`), B3-04 stale Pending catch-ups, B3-07 prompt id escaping, B3-08 `source: mock|model` on the wire (schemas + fixtures updated). B3-05 partial (no-key dedupe and limiter pruning done; per-user limit, 200-per-session cap, key binding on coalesced response deferred).
**Results:** build 0 warnings; 107 contract + 105 AI + 15 architecture + 338 integration tests pass (single run; concurrency tests not repeated).
**Known gaps:** sanitizer turns "ASP.NET" into "[link removed]"; no NFKC normalisation; `catchup.schema.json` does not describe the real catch-up wire; hub Join summary replay untested; fixes not independently re-reviewed.
**Open for Product Owner:** B3-06 (AI work after Pause/Stop/Delete), B3-12 (PII redaction before vendor calls). Open for backend: audio-socket cap/reconnect flake (see Iteration 5).
**Next:** frontend calls to summary / quick-recap / catch-up, socket reaping fix, independent re-review, then commit once the Product Owner agrees.

## Iteration 6 — B3-06 (2026-10-28, PO decision: cancel and re-check)
Pause/Stop/Delete cancel in-flight AI calls (`SessionCutoff`), and the gateway re-checks the session is Active and owned right before each provider call. Verified by Lead re-run: 107 contract + 105 AI + 15 architecture + 353 integration pass.
**Behaviour change:** no provider call after Pause or Stop, so the "Final" summary on Stop is no longer produced (the spec was silent; the DB still allows the value). Quick recap on a Paused session returns the extractive answer only. A final summary after Stop needs a spec and a PO decision.
**Not yet re-reviewed** by security-engineer; tests were written alongside the code, not shown red first.

## Iteration 7 — E2E flake root cause, catch-up schema, UX must-fixes (2026-10-28)
**Flake root cause (supersedes the socket-cap hypothesis in Iteration 5):** per-user rate limits, not `RelayRegistry.TryAdd` (zero refusals logged over ~10 runs). Serial E2E exhausted audio tickets (6/min), writes (30/min; SignalR `negotiate` counted as a write) and the general 120/min bucket, so ticket/negotiate calls got 429 and the clients backed off 10-18s. Real bug found: the student's own Pause/Stop could be answered 429, against ADR-019b. Fixed in `Tether.Host/Program.cs` (own `POST /sessions/{id}/pause|stop` exempt from global limiters; hub negotiate exempt from the write bucket). Production limits otherwise unchanged; `docker-compose.dev.yml` raises dev-only limits for the serial E2E.
**Second, unrelated E2E bug:** `courses.spec.ts` "lost session (401)" shared the `signout` user with the sign-out test, which revokes that server session; gave it its own user.
**Schema:** `catchup.schema.json` now describes the real wire; 9 fixtures added; real-response validation test added.
**Verified by Lead re-run:** 116 contract + 105 AI + 15 architecture + 356 integration pass; web 325 unit tests; Playwright full serial run 19/20 before the courses fix, `courses.spec.ts` 11/11 after it (dev limits raised).
**Caveats:** E2E has not been run at production rate limits; the two new rate-limit regression tests were not shown failing on the old code; the TL;DR E2E takes ~1.6 min (waits for the 90s summary flush); `lecture.spec.ts` `beforeAll` lists all of alice's courses and grows each run.
**UX:** AI specialist review `docs/ux/m2-quick-recap-review.md`; must-fixes M1-M7 implemented (commit cbfb38b). Not human-validated.

## Iteration 8 — B3 re-review fixes (2026-10-28)
Security re-review `docs/security/m2-b3-rereview.md`: 0 Critical/High, 1 Medium (N-03), 4 Low, 5 Info; earlier B3 fixes confirmed (B3-05 partial).
**Fixed:** N-03 own Pause/Stop get a separate per-user bucket (`RateLimiting:ConsentReversalPermitPerMinute`, default 600) via endpoint metadata instead of a path regex; N-01 tombstones stop late summary events re-creating scheduler state; N-02 settle-on-every-exit plus `ReservationReaper` (`Ai:Reaper:IntervalSeconds`, `GraceSeconds`; startup sweep assumes single instance, ADR-020d); N-04 regex timeouts and 1s deadline in sanitizer and redactor (fail closed to "[content removed]"); N-05 startup warning for raised rate limits outside Development, zero permits rejected; N-06 hub `summary.revision`/`catchup.ready` go through strict parsers.
**Verified by Lead re-run:** 116 contract + 113 AI + 15 architecture + 370 integration pass; web 336 unit tests; Playwright full serial run 20/20 against the recreated API (dev-only raised rate limits).
**Not red-first:** N-02 reaper tests (compile error only). **Deferred:** partial index on `ai_usage` for reaper sweeps (needs migration A003, database-architect); N-07 prompt-side Cf/tag characters; N-08 `AiSource.Of`; N-10 B3-05 leftovers. Fixes not independently re-reviewed.
**For G1 consent copy:** a request already sent to a vendor when a student pauses cannot be recalled.

## Iteration 9 — Phase 2 slice 1 on mock data (2026-10-28, end of session)
**Done and committed:** Phase 2 design docs (syllabus stories, extraction design, review UX, practice-test learning design); `syllabus.v1` contract frozen for MOCK-ONLY implementation after three security passes (`docs/security/p2-contract-review.md`, `-rereview.md`, `-delta.md`); data design rev 3; legal brief for counsel (`docs/legal/phase2-uploads-and-vendors.md`); A003 AI scope + per-pool budgets; Syllabus module P2-B1 (paste + plain-text upload, mock extractor, mock calendar, delete/export/purge); delete-vs-writer deadlock fix (SQLSTATE 40P01 retry; regression test fails with a 500 on the old code).
**Verified by Lead re-run at the end:** 91 syllabus + 122 AI + 574 contract + 19 architecture + 454 integration pass, 0 warnings. Web (last run before Phase 2 backend): 336 unit tests, Playwright 20/20 with dev-only raised rate limits.
**Not done / not verified:** no security review of the Syllabus module code yet (contract only); no frontend for syllabus; PDF/DOCX parsing, sandboxed parser, tmpfs, ingest worker (P2-B3) not built; log-canary test through the real log pipeline missing; NFC normalisation is a no-op under `InvariantGlobalization=true` (needs ICU, devops/Lead decision); the custom CSRF header named in the contract is undefined; migration SQL verified only via the integration tests; CI has never run on GitHub.
**Open for the Product Owner:** P2-D1..D14 (esp. D4 retain original, D5 upload notice/attestation, D13 synthetic-only pilot), counsel assignment, PO-7 (age vs region ambiguity), practice-test scope (keyed quizzes first?), final summary after Stop, owner for gate G4, item text surviving the purge, audit HMAC.
**Gates:** all five unchanged (G1 BLOCKED, G2 UNVERIFIED, G3-G5 NOT_RUN/not verified). Real documents, real Claude calls and real Calendar writes remain NO-GO.
**Next:** security review of the Syllabus module; syllabus frontend per `docs/ux/phase2-syllabus-review.md`; ICU decision; P2-B3 parser sandbox with hostile-file fixtures.

## Iteration 10 — Resumed after work in another environment (2026-10-30, reconstructed from git history)
**What landed while this session was away** (commit messages and docs only; not reviewed line by line by the Lead): feature rename "I Lost Track" -> **Quick recap** across API, DB (A004), contracts and UI, with `LanguageGuard` and `docs/ux/language-guide.md` (d9263f2, 16300b0); web redesign "reading room" design system and app shell (7f30b45, `docs/ux/redesign-*.md`); Syllabus security findings B1-01,03,04,05,06,07,13 fixed red-first (c439398, review `docs/security/p2-b1-review.md`); Google Calendar integration design, token/data-residency security design, reminder UX and vendor verification (cd3a49a); DRAFT `calendar.v1` contract (not frozen) with security review and re-review (424a5e1, `docs/API_CONTRACTS_CALENDAR.md`); API hardening batch 1: log canary through the real pipeline, root-secret key derivation, working NFC, fail-closed tzdata, edge config validation (096ba8e, verified in `docs/security/batch1-verification.md`); security findings register and severity policy (PO-12), GR-1 downgraded to MEDIUM by PO-14a, legal register; themes T0 token plumbing for BL-001 (cfb6946, `docs/ux/themes-*.md`); Google Console evidence recorded (T1 partial).
**Verified by Lead re-run at HEAD on Windows (2026-10-30):** 99 syllabus + 241 AI + 830 contract + 22 architecture + 534 integration pass; web 1464 unit tests pass, typecheck clean, lint 0 errors (1 existing warning). One web test (`tokens.test.ts` live-region allow-list) failed only on Windows path separators; fixed (328871e). Playwright and the harness/screens specs were NOT re-run in this pass.
**Findings register state (per `docs/security/findings-register.md`, other environment):** CRITICAL 0, HIGH 0, MEDIUM and LOW many still open; MEDIUMs block production, not push.
**Still open from before:** P2-D1..D14 (D4, D5, D13 unanswered in DECISIONS.md), practice-test scope, final summary after Stop, G4 owner, P2-B3 parser sandbox, syllabus frontend, themes BL-001 T1-T6 (see `docs/ux/themes-progress.md` "NOT done").
**Gates:** unchanged (G1 BLOCKED, G2 UNVERIFIED, G3-G5 NOT_RUN/not verified).
**Picking up:** syllabus frontend (mock-only) per `docs/ux/phase2-syllabus-review.md`.

## Iteration 11 — Syllabus frontend F1+F2 and contract amendments (2026-10-30)
**Built (mock-only, "Demo calendar"):** typed client + strict parsers, ajv `u`-flag validators (R3-07; 5 astral red-team fixtures pass the schema under `u`, so a schema pass is never a TextPolicy pass), MSW mock faithful to the contract, course card + `/courses/{id}/syllabus`, paste and `.txt` upload, polled processing, review with approve/skip/undo/edit/needs-date, batch approve, one-at-a-time, Demo-calendar sync/remove/undo, delete, JSON export, several syllabi per course. Commits 89c009c (F1), af453e9 (F2).
**Found by the real-stack run and decided (Lead):** term dates are sent as a pair (contract amended, row 61, narrowing stated); item PATCH is `application/json` only (row 62); non-upload 415s get a neutral message; `If-Match: *` and lists are rejected (row 63); recurrence line shows the weekday once ("Repeats every Tuesday, Aug 25 to Dec 11.").
**Verified by Lead re-run:** backend 99 syllabus + 241 AI + 835 contract + 22 architecture + 538 integration pass (with the dev API stopped; see DEVELOPMENT.md); web 1726 unit tests, tsc clean, eslint 0 errors, npm audit 0; Playwright `e2e/syllabus.spec.ts` 3/3 against the real API (run by the agent and by the Lead before the final amendments; not re-run after them).
**Not verified:** screen reader, real device/phone, other browsers, human copy review (G4); security review of the upload/delete/export paths; the full Playwright suite was not re-run after Phase 2 UI work. Upload notice is a DRAFT placeholder pending counsel (P2-D5). Please-check approvals always send `acknowledgeCheck` (needs adhd-ux review). Recurrence wording uses full day names (judgement call). Mock checks term fields in a different order than the server (multi-error bodies could differ).
**Gates:** unchanged.
**Full real-stack Playwright after the amendments (API recreated on final code, `--workers=1`, dev-only raised rate limits):** run 1 = 20 passed, 1 failed (a syllabus spec; test name not captured), 2 did not run; runs 2 and 3 = 23/23 passed; the lecture + syllabus specs also passed 12/12 together. Treat run 1 as an unexplained first-run-after-recreate failure (suspect cold start); not reproduced in two reruns.
