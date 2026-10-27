# Backend Foundation (ASP.NET Core Modular Monolith)

Status: PROPOSAL (design only, no code executed). Owner: backend-engineer. Reviewers: security-engineer, database-architect, frontend-engineer, speech-engineer.

## 1. SDK / runtime decision and dependencies

- Recommendation: **.NET 10 (LTS)** (released Nov 2025, supported to Nov 2028). .NET 9 is STS and ends support earlier; greenfield should start on LTS. Fallback: .NET 8 LTS only if a package blocks.
- The dev machine has the dotnet host but **no SDK**. Nothing below can be built, tested or have EF migrations generated until SDK 10 is installed (`winget install Microsoft.DotNet.SDK.10`) or builds run in Docker (`mcr.microsoft.com/dotnet/sdk:10.0`). Needs Product Owner/devops approval to install.
- SDK-dependent items: `global.json` pinning, `dotnet ef` tooling (needs `dotnet-ef` global tool), solution (`.slnx`) creation, all tests, OpenAPI generation (`Microsoft.AspNetCore.OpenApi`), contract-test generation for frontend.
- Packages (all pending approval; free/OSS): EF Core + Npgsql, `Microsoft.AspNetCore.SignalR` (in-box), Serilog or built-in OpenTelemetry, xUnit + `Microsoft.AspNetCore.Mvc.Testing` + Testcontainers (Docker is available).

## 2. Solution layout (`src/api`)

```
src/api/
  Tether.slnx
  global.json
  Directory.Build.props         (nullable, warnings-as-errors, analyzers)
  src/
    Tether.Host/          composition root, Program.cs, hubs, middleware, ProblemDetails, DI wiring
    Tether.SharedKernel/  Result types, IClock, ids, pagination, domain event base, ICurrentUser
    Modules/
      Identity/    {.Domain,.Application,.Infrastructure,.Api}
      Courses/     ...
      Lectures/    ...   (sessions, lifecycle, consent record)
      Transcript/  ...   (segments, sequence, ingest)
      AI/          ...   (summaries, catch-up, Claude port)
      Integrations/...   (Google; Phase 1 export stub only)
  tests/
    *.UnitTests, *.IntegrationTests (WebApplicationFactory + Testcontainers Postgres), *.ContractTests
```

Rules: a module exposes only `*.Contracts` interfaces (public) to others; Domain/Application/Infrastructure are `internal`. Architecture test (NetArchTest) enforces no cross-module references to internals. One PostgreSQL database, **one schema per module** (`identity`, `courses`, `lectures`, `transcript`, `ai`, `integrations`), one `DbContext` per module, **no cross-schema FKs except the allow-list below**. In-process integration events via an outbox table per module (`outbox_messages`) dispatched by a hosted service.

**Cross-schema FK allow-list (ADR-014).** Exactly one database-level cross-schema FK exists: `fk_courses_user_id` on `courses.courses(user_id)` -> `identity.users(id)` `ON DELETE CASCADE`, added by Courses migration `M004_CoursesUserFk` with raw SQL. It is DB-only (no EF navigation, no Courses -> Identity assembly reference), so the schema keeps the integrity guarantee and purging a user removes their courses. Identity must be migrated before Courses and rolled back after it. Test `Cross_schema_foreign_keys_are_exactly_the_allow_listed_set` reads `pg_constraint` and fails if any other cross-schema FK appears; add a new entry here and in that test in the same change. `identity.sessions` (M003) is same-schema and holds a normal FK to `identity.users`.

Migration Down methods drop their tables and constraints but not their schemas, because each schema hosts its own `__ef_migrations_history` table (see `src/api/README.md`).

## 3. Module boundaries

| Module | Owns | Exposes (Contracts) | Depends on |
|---|---|---|---|
| Identity | users, external logins, dev auth, sessions | `ICurrentUser`, `IUserDirectory` | none |
| Courses | course CRUD | `ICourseOwnership.IsOwnedBy(userId, courseId)` | Identity |
| Lectures | lecture session lifecycle (start/pause/resume/stop), consent record, recording state | `ILectureSessionReader`, events `SessionStarted/Paused/Resumed/Stopped` | Courses |
| Transcript | segments (partial/final), per-session monotonic `seq`, ingest port, retention/deletion | `ITranscriptReader.GetWindow(session, from, to)`, `ISegmentIngest`, event `SegmentFinalized` | Lectures |
| AI | summary revisions, catch-up, Claude port, job queue, prompt versions | `ISummaryReader`, `IAiProvider` (port) | Transcript, Lectures |
| Integrations | Google tokens (encrypted), export commands (idempotent), outbox | `IDocExporter` port | Identity, AI |

Ports (interfaces in module Contracts, adapters in Infrastructure): `ISpeechToTextProvider` (speech-engineer owns adapter), `ISummarizer`/`IAiProvider` (ai-agent-engineer owns prompts; AI never gets OAuth tokens), `IFileStorage`, `IGoogleDocsClient`, `IClock`. Each ships with a **Fake** adapter registered only when `Providers:X:Mode=Fake`; fakes log a startup warning and `/health/ready` reports `mode: fake`; docs never describe a fake as a working integration.

## 4. Auth foundation

Choice: **Google OIDC sign-in (authorization code + PKCE) issuing a first-party HttpOnly, Secure, SameSite=Lax session cookie** (ASP.NET Core cookie auth, sliding 8h, absolute 14d). **Server-side sessions (ADR-016):** the cookie carries only a session-id claim; `identity.sessions(id, user_id, created_at, expires_at, revoked_at)` is checked by primary key on every request, so logout revokes that session, the 14-day expiry cannot be extended by sliding refresh, and soft-deleted users are rejected. Cookie name is `__Host-cc.session` outside Development (`cc.session` in Development, which runs over plain http). Cookie-encryption keys persist to `DataProtection:KeysPath`. Rationale: single web client, no token in JS (XSS-safe), SignalR works with cookies for same-site. API and web served under same site (reverse proxy `/api`, `/hubs`) to avoid third-party cookie problems; CORS locked to configured origin with credentials. CSRF: antiforgery header (`X-CSRF-TOKEN` double-submit) required on all unsafe methods; SignalR negotiate validates Origin.

- Sign-in scopes: `openid email profile` only. Google Docs/Calendar scopes are requested later via a **separate incremental consent flow** in Integrations (least privilege); refresh tokens encrypted with Data Protection / KMS key, never exposed to AI or logs.
- Dev-only mode: `Auth:Mode=Dev` honored **only when `IHostEnvironment.IsDevelopment()`** (startup fails otherwise). `POST /api/v1/dev/login {email}` creates/uses a seeded user and issues the same cookie; header `X-Dev-User` also accepted in integration tests via test auth handler. Two seeded users (alice, bob) so cross-user denial tests are trivial.
- Authorization: global fallback policy = authenticated. Resource access through module `IOwnership` checks inside application handlers (not only controllers). Query filters: every owned entity has `UserId`, EF global query filter on `ICurrentUser.Id`. Foreign-owned or missing resource returns **404** (not 403) to avoid enumeration. Ids are GUIDv7.
- Rate limiting (built-in, SEC-036): a global limiter keyed per authenticated user (`RateLimiting:ApiPermitPerMinute`, default 120) plus a stricter per-user bucket for unsafe methods (`WritePermitPerMinute`, 30); unauthenticated traffic including `/health/*` is keyed per client IP (`AnonymousPermitPerMinute`, 60); login has its own IP policy. `X-Forwarded-For/Proto` are honoured only from `Security:TrustedProxies` / `TrustedProxyNetworks` (none configured means the headers are ignored). Endpoint-specific policies (`lost-track`, audio ingest) come with those endpoints.
- Response hardening (SEC-033): nosniff, Referrer-Policy, CSP, Permissions-Policy, no-store are applied in `OnStarting` so they also appear on 500/429 responses; HSTS is sent over https outside Development; ETags are opaque HMAC weak tags (`Security:ETagKey`), never raw `xmin`.
- Open decisions: Google Cloud OAuth client creation (Product Owner); account deletion/export endpoints (Phase 1 stretch).

## 5. Error format

RFC 9457 ProblemDetails via `AddProblemDetails` + `IExceptionHandler`. `Content-Type: application/problem+json`. Fields: `type` (`https://errors.tether.invalid/{code}`), `title`, `status`, `detail`, `instance`, plus extensions `code` (stable machine string), `traceId`, `errors` (field map for 400/422). No stack traces, tokens or transcript content in responses or logs.

| Status | code | When |
|---|---|---|
| 400 | `validation_failed` | model validation (FluentValidation or DataAnnotations) |
| 401 | `unauthenticated` | no/expired session |
| 403 | `consent_required` / `csrf_failed` | recording without consent; bad CSRF |
| 404 | `not_found` | missing or not owned |
| 409 | `invalid_state` / `concurrency_conflict` | e.g. pause a stopped session; ETag mismatch |
| 412/428 | `precondition_*` | `If-Match` on PUT |
| 429 | `rate_limited` | with `Retry-After` |
| 503 | `provider_unavailable` | AI/STT down (degraded, never fatal to transcription) |

Idempotency: unsafe POSTs that create effects accept `Idempotency-Key` header (stored 24h per user).

## 6. REST endpoints (all under `/api/v1`, all authenticated, all user-scoped)

### Milestone 1: Courses

| Method | Path | Body / notes | Success | Errors |
|---|---|---|---|---|
| GET | `/me` | current user profile | 200 | 401 |
| POST | `/courses` | `{name, code?, term?, instructor?, color?}` | 201 + Location | 400 |
| GET | `/courses` | `?cursor&limit` own courses, non-archived default `?includeArchived` | 200 | 400 |
| GET | `/courses/{id}` | ETag | 200 | 404 |
| PUT | `/courses/{id}` | full replace, `If-Match` | 200 | 400,404,412 |
| DELETE | `/courses/{id}` | soft delete/archive; hard delete with `?purge=true` cascades to sessions/transcripts (deletion control) | 204 | 404,409 if live session |
| POST (dev only) | `/dev/login` | `{email}` | 204 | 404 outside dev |
| GET | `/auth/google/start`, `/auth/google/callback`, POST `/auth/logout` | OIDC flow | 302/204 | |

### Milestone 2: Sessions and catch-up

| Method | Path | Body / notes | Success | Errors |
|---|---|---|---|---|
| POST | `/courses/{courseId}/sessions` | `{title?, consent:{acknowledgedRecordingPolicy:true, version}}` creates in `Active`; consent is mandatory and stored with timestamp | 201 `{sessionId, hubUrl, protocolVersion, ingest:{...}}` | 403 `consent_required`, 404, 409 if another active session |
| GET | `/sessions/{id}` | state, timings, last seq | 200 | 404 |
| GET | `/courses/{courseId}/sessions` | list, cursor | 200 | |
| POST | `/sessions/{id}/pause` | idempotent | 200 | 409 |
| POST | `/sessions/{id}/resume` | | 200 | 409 |
| POST | `/sessions/{id}/stop` | idempotent; triggers final summary + notes job | 200 | |
| DELETE | `/sessions/{id}` | deletes transcript + derived data | 204 | |
| GET | `/sessions/{id}/transcript?afterSeq=&limit=` | finalized segments for catch-up after reconnect | 200 | 404 |
| GET | `/sessions/{id}/summary` | latest summary revision (+ `?rev=`) | 200 (404 if none yet) | |
| POST | `/sessions/{id}/lost-track` | `{windowMinutes: 2\|5\|10}` returns `202` with `{catchUpId}`; fast path returns deterministic extractive recap of last N min of final segments immediately in body (`extractive`), AI recap arrives by hub event `catchUp.ready`. Never blocked by summarization queue (own priority lane). | 200/202 | 400,409,429,503 degrade to extractive only |
| GET | `/sessions/{id}/catch-ups/{catchUpId}` | poll fallback | 200 | |

State machine: `Active <-> Paused -> Stopped` (terminal; `Failed` if abandoned, auto-stop after heartbeat timeout of 10 min). Pause means no audio accepted and no transcript produced; UI status is derived from the `session.state` event.

Audio ingest (Phase 1 option, to be fixed with speech-engineer): browser streams audio to the Host over a WebSocket `/ingest/sessions/{id}/audio` (cookie auth, consent gated, Active-only, binary frames with frame counter for gap detection) and the Host forwards to the STT adapter. Alternative where the browser talks to STT directly with short-lived scoped token is an open question for Lead Architect (privacy, cost).

## 7. SignalR hub contract

Hub: `/hubs/lecture` (`LectureHub`), protocol `lecture.v1`, JSON (MessagePack later). Auth via cookie; each connection must `Join` a session it owns; group `session:{id}`. Origin check on negotiate. Server-to-client only (clients send no transcript).

Client to server methods:

| Method | Args | Result |
|---|---|---|
| `Join` | `{sessionId, protocol:"lecture.v1", lastSeq?: long, lastSummaryRev?: int}` | `{state, currentSeq, currentSummaryRev, replay:{fromSeq,toSeq,truncated}}` then replayed events; errors map to HubException with ProblemDetails `code` |
| `Leave` | `{sessionId}` | |
| `Ping` | | heartbeat updates session liveness |

Server to client events. Every event envelope: `{v:1, type, sessionId, seq, ts}`. `seq` is a per-session monotonically increasing int64 assigned by Transcript module in a single transaction with persistence (gap-free for finalized events). Partial segments share the seq space as "revisions" of a stable `segmentId`.

| Event | Payload | Semantics |
|---|---|---|
| `session.state` | `{state: Active\|Paused\|Stopped, reason?}` | recording status source of truth |
| `transcript.partial` | `{segmentId, rev, startMs, text, speaker?}` | ephemeral, replaces earlier rev of same segmentId; not replayed on reconnect except latest |
| `transcript.final` | `{segmentId, startMs, endMs, text, confidence?, speaker?}` | immutable once emitted; persisted; replayable |
| `transcript.correction` | `{segmentId, text}` | rare late STT fix; supersedes final |
| `summary.revision` | `{rev, basedOnSeq, tldr, bullets[], coveredFromMs, coveredToMs, status: Draft\|Final, citations:[{segmentId,startMs}]}` | monotonic `rev`; client ignores `rev <= current` |
| `summary.status` | `{state: Pending\|Running\|Delayed\|Failed, lagSeconds}` | graceful degradation indicator; transcript continues regardless |
| `catchup.ready` | `{catchUpId, windowMinutes, text, citations[], kind: Extractive\|Ai}` | response to I Lost Track |
| `stt.status` | `{state: Ok\|Degraded\|Down}` | provider health |
| `error` | `{code, detail}` | non-fatal notices |

Reconnect/resume: client keeps `lastSeq` (last final/state seq) and `lastSummaryRev`. On reconnect (SignalR automatic reconnect with backoff 0/2/5/10/30s plus jitter) client calls `Join` again. Server replays finals with `seq > lastSeq` (cap 5000, else `truncated:true` and client falls back to `GET /transcript?afterSeq=`), the latest summary revision if newer, and the latest partial. Duplicate delivery is allowed; client dedupes on `(sessionId, seq)` / `segmentId`. Clients must not invent seq. Protocol changes: additive within `v1`; breaking changes go to `lecture.v2` negotiated in `Join`. Event schema lives in `src/shared/contracts/lecture.v1.json` (JSON Schema) with contract tests in both stacks (frontend-engineer review).

Scale-out note: single instance in MVP; a Redis backplane is only added with approval.

## 8. Background jobs: summarization never blocks transcription

- Transcript ingest path is synchronous and minimal: validate, assign seq, insert segment, enqueue outbox row, push to hub. It never calls Claude.
- Summarization uses a **separate pipeline**: `SegmentFinalized` outbox message -> `SummaryScheduler` (hosted `BackgroundService`) coalesces per session using a debounce (e.g. every ~45s or 12 finalized segments, whichever first, configurable) and stores a single pending "summarize up to seq N" job per session (latest-wins, so backlog cannot grow). Worker drains via bounded `Channel<T>` with per-session single concurrency and global concurrency cap (default 4); durable jobs live in Postgres `ai.jobs` (lease + heartbeat via `FOR UPDATE SKIP LOCKED`) so restarts resume. No Hangfire/Quartz in MVP (avoid new dependency); revisit for scheduled reminders in Phase 2.
- Summaries are incremental: input = previous summary + new finalized segments since `basedOnSeq` (bounds token cost). Output validated against JSON schema; on invalid output, one repair retry then `summary.status=Failed` with last good revision kept.
- Resilience: per-call timeout (e.g. 30s), retries with exponential backoff and jitter (max 3), circuit breaker via `Microsoft.Extensions.Http.Resilience`, `CancellationToken` flowed from host shutdown and session stop. Budgets: per-user daily token ceiling and max jobs/min; exceeding returns `summary.status=Delayed` and never silently incurs overspend (needs Product Owner budget number).
- I Lost Track has a **separate high-priority lane** (dedicated small concurrency slot) and an extractive fallback so it works when Claude is down or slow.
- On `Stopped`: enqueue final summary + notes generation (timestamped citations) job.
- Observability: OpenTelemetry traces/metrics (segment ingest latency, summary lag, job queue depth, token usage); structured logs with session id and user id hash only; **no transcript text, audio, or tokens in logs** (log redaction test).

## 9. Testing plan (needs SDK 10 or Docker SDK image)

Integration (WebApplicationFactory + Testcontainers Postgres): auth boundary (401 anonymous, 404 cross-user for every resource endpoint, parametrized from route table), consent-required 403, state-machine 409s, Idempotency-Key replay, hub: join denied for foreign session, reconnect replay with gap, summary rev monotonicity, summarizer fake delay/failure does not delay `transcript.final` delivery (latency assertion), lost-track fallback when AI down, CSRF and Origin rejection, dev auth disabled outside Development, ProblemDetails shape. Contract tests: JSON Schema validation of events and OpenAPI diff vs frontend.

Not run: nothing has been executed; no SDK present.

## 10. Open questions for Lead Architect / Product Owner

1. Approve .NET 10 and SDK install (or Docker-only builds).
2. Audio path: server-relayed vs direct-to-STT token (privacy/cost; with speech-engineer and security-engineer).
3. Same-site hosting for cookie auth (devops: dev proxy, prod domain layout).
4. Google OAuth client and test-user list; consent text/versioning (security-engineer, university rules).
5. AI token budget per user/day; summarizer cadence defaults.
6. Hard-delete vs soft-delete retention periods (database-architect).
7. Stack confirmation: EF Core one-DbContext-per-module with schema-per-module (database-architect).
