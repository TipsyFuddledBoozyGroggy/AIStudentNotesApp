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
**Next:** B3 AI summaries (TL;DR, I Lost Track) on MockAi; frontend wiring to the hub; Opus security review of B2 before the audio socket is treated as done.
