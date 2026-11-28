# Architecture (consolidated)

**Updated 2026-11-27.** This file is the index and the reconciled view; detail lives in the documents below. Where they conflict, `DECISIONS.md` wins, and the code wins over any design document that predates it.

## Index

| Area | Document |
|---|---|
| Product scope, stories | `PRODUCT_REQUIREMENTS.md`, `product/` (go-live stories, finish backlog, pilot plan) |
| UX and accessibility | `ux/student-experience.md`, `ux/language-guide.md`, `ux/design-direction.md` |
| Backend, auth, REST, SignalR, jobs | `architecture/backend-foundation.md` |
| Realtime transport | `architecture/m2-b2-realtime.md` |
| Speech pipeline | `architecture/speech-pipeline.md` (vendor facts superseded in part; see `RELEASE_GATES.md`) |
| Live lecture AI | `ai/live-lecture-ai-design.md` |
| Notes after Stop | `architecture/notes-after-stop.md` |
| Syllabus extraction | `ai/syllabus-extraction-design.md` |
| Document reader (DOCX now, PDF later) | `architecture/document-reader.md` |
| Demo Doc export | `architecture/docs-export-demo-adr.md` |
| Google Calendar (later, real) | `architecture/google-calendar-integration.md` |
| Sign-in and go-live (ADR-022) | `architecture/go-live-signin-adr.md` |
| Account export and delete (ADR-021, proposed) | `architecture/account-export-delete.md` |
| Data model | `data/schema-m1-m2.md`, `data/schema-syllabus-p2.md`, `data/lock-order-audit.md` |
| Contracts | `API_CONTRACTS.md` (`lecture.v1`, frozen), `API_CONTRACTS_P2.md` (`syllabus.v1`, frozen for mock use), `API_CONTRACTS_DOCS_EXPORT.md` (`docs.export.v1`, mock only), `API_CONTRACTS_AUTH.md` (`auth.v1`, frozen), `API_CONTRACTS_CALENDAR.md` (`calendar.v1`, draft) |
| Security requirements and findings | `SECURITY.md` (SEC-001..041 with implementation status), `security/findings-register.md` |
| Hosting and deploy | `../infra/README.md` |
| Test strategy and CI | `qa/test-strategy.md`, `ops/ci-matrix.md` |

## Shape

```
Browser (Next.js app)
  ├─ REST /api/v1 (session cookie, CSRF)          ─┐
  ├─ SignalR /hubs/lecture (lecture.v1 events)     ├─> Web server (Next.js): pages, and a same-origin proxy for /api, /hubs, /ingest
  └─ audio WebSocket /ingest (PCM16 16 kHz, ticket)─┘                 │
                                                                      v
API (ASP.NET Core, one process, modular monolith)
  Modules: Identity | Courses | Lectures | Transcript | Ai | Syllabus | Docs     (Tether.Host composes them; Tether.SharedKernel)
  Providers behind interfaces, all stand-ins today:
    ISpeechToTextProvider = replay ("Demo source")   IAiClient = mock   ISyllabusExtractor = mock
    ICalendarProvider = mock ("Demo calendar")       IDocsProvider = demo_docs ("Demo Doc")
  Background work: hosted services (summaries, Quick recap, notes, syllabus reading, idle end, retention purge); ingest never waits on AI
  PostgreSQL (EF Core): one schema per module; source transcripts and documents stored apart from derived summaries and notes
```

- The browser talks only to the web server, so cookies and origin checks work on one origin. The web server forwards `/api`, `/hubs` and `/ingest` to the API.
- Each module owns its schema and migrations. Architecture tests (`src/api/tests/Tether.ArchitectureTests`) keep module internals private.
- Configuration chooses each provider, and the API refuses to start with anything but the stand-in values. No real provider adapter exists in the code.

## Hosted environment (trial)

```
Internet ──> tether-web (Container Apps, the only public entry)
               access=team:    Microsoft (Entra) sign-in in front, then Google sign-in
               access=invited: Google sign-in plus Tether's invite list is the gate
               access=closed:  ingress internal only (emergency stop)
             └─> tether-api (Container Apps, internal ingress only, ASPNETCORE_ENVIRONMENT=Production)
                   └─> PostgreSQL Flexible Server (TLS required, automatic backups 7 days)
Container logs ──> Log Analytics (daily cap)
```

- Deployed only by `.github/workflows/deploy-azure-dev.yml`: manual, GitHub OIDC to a deploy identity scoped to one resource group, and each run waits for the Product Owner's approval.
- In Azure the API always runs as Production. Development login cannot start there.
- Secrets are Container Apps secrets, set from GitHub environment secrets that the PO's scripts store. Key Vault is not used, because the deploy identity cannot create role assignments (`infra/main.bicep` header).
- The Data Protection key ring is kept in PostgreSQL and protected by a certificate, so sign-ins survive restarts.
- Images are public packages on ghcr.io; nothing secret is in an image.
- The whole procedure, the access modes and the checks before and after each deploy: `infra/README.md`.

## Contract ownership
- Transcript final-segment and summary-revision events: the **Transcript module is the sole producer**; speech and AI consume them. Contracts are versioned JSON Schemas in `src/shared/contracts/` with valid and invalid fixtures. API and web tests validate against the same files.
- A frozen contract changes only by a new version or a recorded amendment in its contract document.
- The session-consent rule (a session's consent record must be a granted microphone consent) is not expressible as a DB constraint. It is enforced in the Lectures module; see SEC-006 and SEC-007 in `SECURITY.md` for what is tested.

## Invariants
The ten invariants in `CLAUDE.md` map to the SEC requirements. `SECURITY.md` records what is built and tested for each requirement, and `security/findings-register.md` holds the open findings. Neither clears a release gate.
