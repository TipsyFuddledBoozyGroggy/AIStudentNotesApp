# Architecture (consolidated)

Detail lives in the specialist documents; this file is the index and the reconciled view. Where they conflict, `DECISIONS.md` wins.

| Area | Document |
|---|---|
| Product scope, stories | `PRODUCT_REQUIREMENTS.md` |
| UX / accessibility | `ux/student-experience.md` |
| Backend, auth, REST, SignalR, jobs | `architecture/backend-foundation.md` |
| Data model | `data/schema-m1-m2.md` |
| Speech pipeline | `architecture/speech-pipeline.md` |
| Live lecture AI | `ai/live-lecture-ai-design.md` |
| Security requirements (SEC-001..041) | `SECURITY.md` |
| Test strategy | `qa/test-strategy.md` |

## Shape
```
Browser (Next.js)
  ├─ mic -> AudioWorklet -> PCM16 16k -> WS (audio, ticket auth) ──> API: SpeechIngest
  ├─ SignalR /hubs/lecture (lecture.v1) <── transcript, summary.revision, catchup.ready
  └─ REST (cookie, CSRF): courses, sessions, quick-recap, notes
API (modular monolith): Identity | Courses | Lectures | Transcript | AI | Integrations
  ISpeechToTextProvider (mock | real)   IAiClient (mock | Claude)
  Postgres: finals (source) separate from summary_revisions / notes (derived)
  Jobs: Postgres-backed table + hosted service; ingest never awaits Claude
```

## Contract ownership
- Transcript final-segment event and summary revision event: **backend module Transcript is the sole producer**; speech and AI consume. Contracts are versioned (`lecture.v1`) and live in `API_CONTRACTS.md` (to be written from backend-foundation + speech-pipeline when Milestone 2 starts; not yet reconciled field-by-field).
- Rule not expressible as a DB constraint: a session's consent record must be a granted mic consent. Enforced in the domain layer; needs a test.

## Invariants
Mapped from CLAUDE.md to SEC requirements; see `SECURITY.md`. Nothing is implemented yet.
