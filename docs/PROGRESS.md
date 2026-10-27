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
