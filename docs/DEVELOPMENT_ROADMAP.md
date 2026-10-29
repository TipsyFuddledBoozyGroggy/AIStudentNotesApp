# Development Roadmap

Milestones per the master prompt. Status is honest: only design exists.

## Milestone 1 — Foundation (current)
| Track | Owner | Depends on | Status |
|---|---|---|---|
| Repo scaffold, local dev (docker compose Postgres), CI skeleton | devops-engineer | none | not started |
| Web: Next.js + TS + Tailwind, dashboard, course create, a11y baseline | frontend-engineer | UX doc; mock API | not started |
| API: solution, Identity (dev login), Courses CRUD, EF + M001-M003 | backend-engineer, database-architect | **PO-1 .NET SDK** | blocked |
| Security review of auth + ownership | security-engineer | API skeleton | not started |
| Test harness (Vitest, Playwright, xUnit) | qa-engineer | scaffolds | not started |

## Milestone 2 — Live lecture slice (highest priority)
Contract freeze (`API_CONTRACTS.md`) -> mock STT replay provider + mock AI -> sessions/consent -> transcript stream -> rolling TL;DR -> Quick recap -> notes with timestamps -> mock-backed Docs export. Real STT/Claude only after PO-2/3/4.

## Later
M3 Google (needs PO-6), M4 adaptive study, M5 coaching, M6 hardening.

## Parallelism rules
Frontend works against an MSW mock of the REST/SignalR contract while the backend is blocked. No two agents edit the same files; frontend owns `src/web`, backend owns `src/api`, devops owns root infra files and `.github`.
