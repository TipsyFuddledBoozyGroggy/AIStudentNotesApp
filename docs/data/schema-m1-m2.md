# Schema M1-M2 (PostgreSQL 16 / EF Core) - PROPOSAL, not yet implemented

Status: design only. No migrations exist. Open questions are in section 9.

## 1. Conventions
- PK: `id uuid` (app-generated UUIDv7 for index locality). Every table has `user_id uuid NOT NULL` (FK users, ON DELETE CASCADE) except `users`. Timestamps `timestamptz` UTC; `created_at` on all, `updated_at` on mutable tables. `xmin`-based concurrency token (`row_version`) on mutable rows.
- Composite-FK ownership: child tables FK `(parent_id, user_id)` -> parent `UNIQUE(id, user_id)`. The DB itself rejects a child pointing at another user's parent.
- Enums stored as text with CHECK constraints (cheaper to migrate than PG enums).
- Source (immutable, append-only) tables are separated from derived (regenerable) tables.

## 2. Core tables
**users**: id, external_subject (unique, from IdP), email (citext, unique), display_name, quiet_mode bool default false, created_at, deleted_at (null = active), purge_after.

**semesters**: id, user_id, name, starts_on, ends_on. UNIQUE(user_id, name).

**courses**: id, user_id, semester_id (composite FK), code, title, instructor null, created_at, archived_at. UNIQUE(user_id, semester_id, code).

**consent_records** (append-only): id, user_id, course_id, kind (`mic_recording`,`transcript_storage`,`google_export`), granted bool, policy_version, rules_ack_text_version (university/local recording rules acknowledgement), client_info jsonb (no IP by default), created_at. Withdrawal = new row, never update.

**lecture_sessions**: id, user_id, course_id (composite FK), consent_id (composite FK, NOT NULL, must be kind=mic_recording granted - enforced in service + trigger-free CHECK impossible, so verified in domain and covered by test), title, state (`created`,`recording`,`paused`,`ended`,`failed`), started_at, ended_at, paused_total_ms, stt_provider, audio_retained bool default false (raw audio NOT stored in M1-M2; column reserved, CHECK (audio_retained = false) until Product Owner/Security approve), last_seq bigint default 0, row_version. CHECK (ended_at IS NULL OR ended_at >= started_at).
Idx: (user_id, course_id, started_at DESC); partial (user_id) WHERE state IN ('recording','paused') with UNIQUE to allow only one active session per user.

**lecture_session_events** (append-only state log): id, user_id, session_id, from_state, to_state, reason, created_at. Gives the visible pause/stop audit trail.

## 3. Source of truth: transcript
**transcript_segments** (append-only; finals immutable): id, user_id, session_id (composite FK), seq bigint, revision int default 0, is_final bool, start_ms int, end_ms int, text, speaker_label null, provider, provider_segment_id null, confidence real null (0-1), language null, created_at.
- CHECK end_ms >= start_ms, confidence BETWEEN 0 AND 1, seq > 0.
- Partial/final model: partials are NOT persisted by default (kept in memory/SignalR). Only finals are inserted, so UNIQUE(session_id, seq) holds. If partial persistence is later required, use `revision` with UNIQUE(session_id, seq, revision) and a partial index on is_final.
- Idempotent ingest: UNIQUE(session_id, seq) with INSERT ... ON CONFLICT DO NOTHING handles reconnect replays. Corrections (provider re-finalization) insert a new row with `supersedes_id`; reads use `NOT EXISTS superseder`.
- Idx: UNIQUE(session_id, seq); (session_id, start_ms) for "last N minutes" catch-up range scans; (user_id, created_at) for retention sweeps.
- Optional later: GIN tsvector on text, only when search is a story.

## 4. Derived data (regenerable, versioned, with provenance)
**summary_revisions**: id, user_id, session_id, revision int, kind (`tldr`,`catchup`), content text, source_seq_from, source_seq_to (CHECK to >= from), model, prompt_version, ai_usage_id (FK ledger), is_current bool, created_at. UNIQUE(session_id, kind, revision); partial UNIQUE(session_id, kind) WHERE is_current. Never updated in place; new revision supersedes. Composite FK to segments by range is validated in the service (FK on source_seq_to against (session_id, seq)).

**note_sections** (study-guide notes): id, user_id, course_id, session_id null, position int, heading, body_md, origin (`ai`,`user`), edited_by_user bool, current_version int, deleted_at, row_version. Student edits are first-class; AI regeneration never overwrites a user-edited section (writes a new version row).
**note_section_versions**: id, user_id, section_id, version, body_md, origin, model null, prompt_version null, created_at. UNIQUE(section_id, version).
**note_section_sources** (provenance): id, user_id, section_version_id, session_id, seq_from, seq_to, start_ms, end_ms (denormalized for display). Idx(section_version_id), (session_id, seq_from).

**catch_up_requests**: id, user_id, session_id, window_minutes smallint CHECK IN (2,5,10), requested_at, window_start_ms, window_end_ms, seq_from, seq_to, status (`pending`,`ready`,`failed`), summary_revision_id null, latency_ms null, error_code null, client_request_id uuid. UNIQUE(user_id, client_request_id) for idempotent clicks. Idx(user_id, session_id, requested_at DESC).

## 5. Cost ledger and tokens
**ai_usage_ledger** (append-only): id, user_id, session_id null, course_id null, feature (`tldr`,`catchup`,`notes`,`tutor`,`stt`), provider, model, input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, audio_seconds numeric null, cost_micro_usd bigint (integer micro-dollars, no floats), pricing_version, request_id (provider), status, latency_ms, created_at. No prompt/response bodies stored. Idx(user_id, created_at), (session_id), (feature, created_at). Daily/monthly budget enforced by service reading SUM per user; a `usage_budgets` table (user_id, period, cap_micro_usd) is deferred to M2 start.

**oauth_connections** (placeholder, populated in M2 Google work): id, user_id, provider (`google`), provider_subject, scopes text[], access_token_enc bytea, refresh_token_enc bytea, key_id text, token_expires_at, status (`active`,`revoked`,`error`), created_at, revoked_at. UNIQUE(user_id, provider). Encryption: AES-256-GCM application-level envelope via ASP.NET Data Protection or KMS key (`key_id` supports rotation); keys never in DB. EF value converter plus the columns are excluded from logging and from default `Include`. Never exposed to AI-facing code (separate repository/service; AI context builders have no reference to this DbSet).

## 6. Ownership enforcement (defense in depth)
1. EF Core global query filter on every user-owned entity: `e.UserId == _currentUser.Id` via a scoped `ICurrentUser` injected into DbContext; `IgnoreQueryFilters` only in a named admin/purge service, covered by a grep-style architecture test.
2. SaveChanges interceptor stamps `UserId` on insert and rejects modified/added entities whose UserId differs from the current user.
3. Composite FKs (section 1) so cross-user references are impossible at the DB level.
4. Postgres Row-Level Security as backstop (recommended, flagged for Security Engineer): `ALTER TABLE ... ENABLE/FORCE ROW LEVEL SECURITY` with policy `user_id = current_setting('app.user_id')::uuid`, set per connection via an interceptor (`set_config(..., true)` in transaction). App connects as non-owner role. Background jobs set the same GUC per job user.
5. API takes ids only after filter; unknown or foreign ids both return 404.
6. Isolation tests: two-user fixtures asserting every endpoint/DbSet returns zero cross-user rows.

## 7. Retention, deletion, export
- Raw audio: not stored. Transcript finals retained until user deletes, course archived+deleted, or optional per-course retention (default: keep until user deletes; configurable 30/90/365 days in `courses.transcript_retention_days`, null = indefinite). Daily job purges expired segments and cascades derived rows that cite only purged ranges (marks `note_section_sources` as `source_purged`).
- Deletion: user-initiated delete of a session/course = hard delete (cascade) with 7-day soft-delete tombstone (`deleted_at`, filtered by query filter) so accidental deletes are recoverable; then purged. Account deletion: `users.deleted_at` + `purge_after` (30 days), then cascade; ledger rows retained with user_id nulled to a hashed pseudonym only if Product Owner needs cost audit (decision needed), else deleted. Revoke and wipe oauth tokens immediately.
- Consent records and event log are retained after content deletion minimally (ids, timestamps) for compliance proof - confirm with Security Engineer.
- Export: per-user JSON (+ Markdown notes) streamed from source tables: user, courses, sessions, consents, segments, summary revisions, note versions with sources, catch-up requests, ledger totals. Excludes token ciphertext. Async job writes to short-lived signed download; export is itself logged.
- Backups: encrypted daily with PITR; deleted data persists in backups up to the backup window (document it, 14 days); restore drill monthly; restored DBs re-run purge on tombstones.

## 8. Migration plan
- Migrations in `src/api/.../Migrations`, additive, one logical change each:
  1. `M001_Identity_Courses` (users, semesters, courses, extensions citext).
  2. `M002_Consent_Sessions` (consent_records, lecture_sessions, events).
  3. `M003_Transcript` (transcript_segments).
  4. `M004_Derived` (summary_revisions, note_*, catch_up_requests).
  5. `M005_UsageLedger`, then `M006_OAuthConnections` (M2).
  6. `M007_RLS` policies (after tables stable).
- Rollback: every migration has a tested `Down`; after production data exists use expand/contract (add nullable, backfill, enforce) and forward-fix instead of Down. Large tables (segments) get indexes via `CREATE INDEX CONCURRENTLY` (raw SQL, `suppressTransaction`).
- Partitioning: not needed for M1-M2; revisit `transcript_segments` by hash(session_id) or range(created_at) if rows > ~50M.
- Seed data: dev-only two users, one semester, two courses, one 60-minute synthetic transcript (no real lectures). Never in production migrations.
- Validation: Testcontainers-Postgres tests for isolation, composite-FK rejection, idempotent segment replay, one-active-session, retention purge, cascade delete, restore check; benchmark: last-10-minute range query and ingest at 3 segments/sec/session with 200 concurrent sessions, target p95 < 20 ms.
- pgvector: not included; no course-grounded retrieval requirement before M3. Notes/transcripts retrieved by time/seq range.

## 9. Open questions for Lead Architect / Product Owner
1. Persist partial segments? (Design says no.)
2. Ledger retention after account deletion (pseudonymize vs delete).
3. RLS adoption (extra complexity vs backstop) - Security Engineer review.
4. Whether any raw audio retention is ever approved (column reserved, locked false).
5. IdP choice (affects users.external_subject) and KMS vs Data Protection for token keys.
