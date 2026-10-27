# Decisions

Status: **Proposed** = Lead Architect decision within existing authority, reversible. **Needs PO** = requires Product Owner approval; nothing depending on it is enabled.

## Architect decisions (cross-document conflicts resolved)

| ID | Decision | Rationale / source |
|---|---|---|
| ADR-001 | Modular monolith: ASP.NET Core under `src/api`, six modules, one Postgres DB (schema per module). | backend-foundation.md |
| ADR-002 | Cross-user or unknown resource ids return **404**, never 403. | backend-foundation, schema-m1-m2, QA question |
| ADR-003 | Only **final** transcript segments are persisted. Partials live on the wire/in memory. | schema-m1-m2 |
| ADR-004 | Audio goes client -> **our server** (binary WebSocket, ticket handshake) -> STT provider. Provider keys never reach the browser. | speech-pipeline, backend-foundation |
| ADR-005 | Transcript/summary events go downstream over SignalR `/hubs/lecture`, protocol `lecture.v1`; audio socket is upstream only. | speech-pipeline, backend-foundation |
| ADR-006 | Cookie session (HttpOnly, same-site, CSRF + Origin checks); web and API share one origin (dev via proxy). SignalR authenticates with the same cookie. | backend-foundation, SECURITY.md open item |
| ADR-007 | Client reconnect ring buffer: **60 s** (product requirement; speech doc proposed 30 s). UI must state only what is truly buffered. | PRD D7 vs speech-pipeline |
| ADR-008 | Consent = per-course recording-rules attestation (once, before first session) **plus** explicit per-session start click recorded as an append-only consent event. | UX + SECURITY SEC-006..009 |
| ADR-009 | Summaries: Haiku rolling updates, Sonnet for catch-up. Opus off the live path. Model IDs/pricing to be verified with the claude-api reference before any budget is set. Mock `IAiClient` is the default. | live-lecture-ai-design |
| ADR-010 | Postgres full-text search for retrieval; no vector store in M1-M2. | ai design open question |
| ADR-011 | Q&A and clarification chips are P1 (after the slice); Quiet is the default engagement mode. | PRD |
| ADR-012 | Google Doc export is built against a mock Docs provider until a Google OAuth client exists. | PRD vs OAuth dependency |
| ADR-013 | Postgres row-level security deferred until security-engineer reviews it; EF filters + interceptor + composite FKs are mandatory. | schema-m1-m2 |

## Needs Product Owner approval (blocks real use, not mock-backed build)

| ID | Item | Blocks |
|---|---|---|
| PO-1 | ~~Install .NET SDK~~ RESOLVED: .NET 10 via Docker SDK image (`docker-compose.dev.yml`). | — |
| PO-2 | Recording-rules policy and legal wording (D1); third-party voice obligations. | Any real recording |
| PO-3 | STT vendor and spend cap (D3). Proposal: prototype Deepgram, Azure Speech as compliance alternative. Vendor facts in the speech doc are unverified. | Real transcription |
| PO-4 | Claude API key, per-session/user/day budgets, behavior at cap (D5). | Real Claude calls |
| PO-5 | Retention defaults (proposal: 180-day transcripts, 30-day deletion SLA), audio retention (proposal: none), ledger handling on deletion. | Production |
| PO-6 | Google OAuth client creation; identity provider; token key management. | Real sign-in, Docs |
| PO-7 | Minimum age / under-18 handling (D8); hosting region, pilot scope (D10). | Production |
| PO-8 | Latency targets (transcript <2 s, Lost Track <5 s p95): proposals only. | Perf gates |

## Added after Milestone 1 reviews (2026-10-27)

| ID | Decision | Source |
|---|---|---|
| ADR-014 | `courses.user_id` gets a DB-level FK to `identity.users` with `ON DELETE CASCADE`, added by a new Courses migration using raw SQL. EF gets no navigation, so no Courses->Identity assembly reference. Allow-listed in backend-foundation.md and guarded by a `pg_constraint` test. | docs/data/m1-review.md |
| ADR-015 | The Lectures module owns `consent_records` (consent exists to gate a recording session). | docs/data/m1-review.md |
| ADR-016 | Server-side session revocation via an `identity.sessions` table (session-id claim, one PK lookup per request); no user-wide epoch. Must land before any non-dev sign-in. | docs/security/m1-review.md F-01 |
| ADR-017 | Hard M2 gates: Origin/CSRF check must cover WebSocket/SignalR upgrades (F-04); log-canary test and no `LogError(ex)` with row values once transcripts exist (F-08). | docs/security/m1-review.md |

Deferred: row-level security (ADR-013), revisited at M003 start with security-engineer.

## Added at M2 contract draft (2026-10-27)

| ID | Decision | Source |
|---|---|---|
| ADR-018 | M2 wire contract is `docs/API_CONTRACTS.md` (v1, frozen for M2 mock-backed work 2026-10-27): audio is server-relayed over a ticketed WebSocket, transcript events go only over SignalR `lecture.v1`, `seq` is gap-free for persisted events only (partials carry `seq: null`), field name `revision`, event `catchup.ready`. Supersedes conflicting parts of backend-foundation.md and speech-pipeline.md. PO confirmed server-relay (option A) for M2 on 2026-10-27; direct-to-vendor may be revisited as a second adapter after G1/G2. | docs/API_CONTRACTS.md |
