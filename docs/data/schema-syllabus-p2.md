# Schema: Syllabus module (Phase 2, slice 1) - DRAFT PROPOSAL, nothing implemented

Author: database-architect. Date: 2026-10-28 (revision 2, reconciled with the revised contract and `docs/security/p2-contract-rereview.md` D-01..D-14, R2-13, R2-14). Status: design only. No migration, entity, DbContext or test exists for this module; no DDL below has been executed against any database (every SQL block is a sketch to be run in a scratch DB first, see section 14). Implements the DRAFT contract `docs/API_CONTRACTS_P2.md` + `src/shared/contracts/syllabus.v1/` (both can still change; schema, fixtures and this file must move together). Nothing here authorizes a real provider, a Google write, real student documents or any gate (G1-G5) change.

Sources read: `API_CONTRACTS_P2.md`, `syllabus.v1/*.schema.json` (syllabus, create request, calendar event, item constraints, common defs), `ai/syllabus-extraction-design.md` (sections 3, 7, 12, 14), `data/schema-m1-m2.md`, `data/m1-review.md`, `architecture/backend-foundation.md` (incl. addenda), Ai migrations `A001_Ai` / `A002_BudgetReservationAndPendingDedupe`, `AiDbContext`, Lectures `M006_LecturesIntegrity` and `LecturesDbContext` (idempotency/audit pattern), and the `Cross_schema_foreign_keys_are_exactly_the_allow_listed_set` test in `PersistenceTests.cs`.

## 0. Changes from v0 of this doc

v0 pre-dated the contract revision. Contract text used here is `docs/API_CONTRACTS_P2.md` as it stood when this revision was written; another agent is applying pre-freeze edits to it, so every place where I depend on text that is not yet in the contract is marked **(contract dependency)**. Lead decisions given in the brief for this revision are cited as "Lead decision".

| Ref | v0 | Now |
|---|---|---|
| D-01 | `?calendarEvents=remove\|keep`, `SET NULL (syllabus_id)`, `orphaned_at`, `stamp_orphaned` trigger, `*_keep` audit action, orphan TTL | Removed entirely. `DELETE` has no parameters and always deletes events. Event FKs are `ON DELETE CASCADE` to syllabus and item. No orphan state exists. |
| R2-12, R2-13 | `UNLOGGED syllabus.job_inputs` holds upload bytes | Removed. **No upload byte or pasted text is ever stored in Postgres.** Bytes live on a tmpfs/ephemeral volume shared by a trusted orchestrator and an untrusted sandboxed parser that has no DB credentials (3.3). |
| D-03, R2-04 | Snippets were offsets only; canonical text "not separately purgeable"; no activity clock | `snippet_text` (nullable), `snippet_context` (nullable) stored on items and unclaimed dates; purge nulls both and deletes the canonical text; clock `last_activity_at` moved only by student writes, absolute cap `created_at + 365 d`; `retention_*` columns. |
| R2-11 | `sync_failed` unrepresentable without `synced_revision` | CHECK now matches the contract's three-way rule (3.4). |
| D-02 | `revision` was both content revision and ETag basis | New `row_version` column on `syllabi` and `items`, bumped by a trigger on every UPDATE, is the ETag basis. `revision` (items) is content-only and is what `synced_revision` refers to. |
| D-04 | canonical text NFKC | NFC. Snippets are verbatim; cue matching uses a separate matching-only fold in code (not stored). |
| D-05 | flags list lacked two flags | `injection_suspect`, `supersession_cue` added (`ck_items_flags`), and `low_text_quality` was already present. |
| D-07 | no notice / retention / batch / hidden columns | `notice_version`, `notice_attested`, `notice_attested_at`, `hidden_content_dropped`, `batch_approve_allowed`, retention columns (3.1). |
| D-08 | `confidence_reason varchar(160)` | `varchar(200)` (wire max; `syllabus-item.confidenceReason` maxLength 200). |
| D-09 | audit lacked create and export | `syllabus_created`, `exported` added; `*_keep` / `*_remove` delete actions replaced by `syllabus_deleted`. |
| D-10, R2-14 | `A003` added `scope_kind` only | A003 adds a `pool` budget dimension to `ai.ai_usage` and to `ai.global_spend`; caps are per pool (11). Syllabus writes get their own rate-limit bucket (11, rate limits). |
| D-11 | `event_limit_reached` missing from sync item errors | Added. |
| D-12 | syllabus lock only; item rows not locked by sync | Full sync item-locking rule and lock order (6). |
| D-06, R2-10 | `idempotency_keys` unique `(user, route, key_hash)`, every row cascades with the syllabus; retry after delete creates a new syllabus | Unique `(user, method, route_template, key_hash)`; fingerprint covers the concrete path; create-route rows survive their syllabus as 24 h content-free tombstones so replay after delete is `404` as the contract states; lookup order defined (3.8, 6). |
| D-13, R2-16 | `end - start <= 600` | Units written per column; CHECKs aligned to the schema (3.4). |
| D-14 | `purge_after = created + 7 d` for failed/needs_text | Kept, now labelled a data-design behaviour the contract and UX do not yet state. **(contract dependency)** |
| Open Q10 | Wire id codec | Proposal in section 13: `WireId` in `Tether.SharedKernel`, strict single canonical form. |
| Open Q1 | Offset unit | Closed: UTF-16 code units, sliced in .NET only (contract row 38). |

## 1. Assumptions, scope, dependencies

- PostgreSQL 16 (compose is 16.15). I rely on one PG15+ feature: `UNIQUE ... NULLS NOT DISTINCT` (fine on 16). The `ON DELETE SET NULL (column_list)` use from v0 is gone with `keep`.
- **Processing topology (Lead decision, R2-12).** A trusted **orchestrator** (the API/worker process) owns all DB access. An untrusted **parser worker** (separate process or container: no network, no DB credentials, no secrets, read-only root, non-root, bounded CPU/memory/time) reads upload bytes from a **tmpfs/ephemeral volume** shared with the orchestrator, and returns only canonical text and counts over stdout/IPC. The orchestrator validates size, TextPolicy and page/char caps on the output before it touches the database. Postgres never sees upload bytes or pasted-text bytes (pasted text enters the database only as `canonical_texts.text` after canonicalization, exactly like extracted text).
- New module `Syllabus`, schema `syllabus`, one `SyllabusDbContext` (`internal`), history table `syllabus.__ef_migrations_history`, migrations prefixed `S` (like `M`, `A`, `T`). The module may reference only `Tether.SharedKernel` and `Tether.Courses.Contracts` (`ICourseOwnership`), per `ModuleBoundaryTests`. All cross-module integrity is DB-only raw SQL.
- Conventions inherited (not redefined): uuid PK (`Ids.New()` = UUIDv7), `user_id uuid NOT NULL` on every table, composite `(id, user_id)` unique targets and composite FKs for same-schema children, text + CHECK for enums (no PG enums), `timestamptz` for instants, EF global query filter `UserId == CurrentUserId` on every entity plus the `OwnershipSaveChangesInterceptor`, snake_case names, `ix_/ux_/fk_/ck_` prefixes, `internal` migration classes, never create `xmin` as a column.
- Wall-clock rule (contract section 2): the student's dates are stored as `date`, `time` (no seconds) and an IANA zone text. **No `timestamptz` is used for any student-read date or time.** `timestamptz` only for instants (`created_at`, `synced_at`, ...).
- Interpretations I made where the request wording is loose: "uncertain-date notes" = `unknown_reason` + `item_alternatives` (conflicting candidates) + `unclaimed_dates` ("dates we noticed but did not turn into items"). "Audit of decisions" = the content-free `decision_audit` table. "Sync records" = `sync_runs` / `sync_run_items`.
- Dependent teams: backend-engineer (entities, job worker, sweeper, id codec), ai-agent-engineer (document scope in gateway), google-integration (itemKey reuse), security-engineer (section 12 items), devops-engineer (backup, sweeper config), qa-engineer (section 14).
- Scope boundary: no source or migration files created. `docs/architecture/backend-foundation.md` and `PersistenceTests.cs` need edits when this is implemented (section 9); I did not make them.

## 2. Entity overview and deletion graph

```
identity.users
  `-- courses.courses                      (existing; fk_courses_user_id CASCADE)
        |-- syllabus.syllabi               (NEW cross-schema FK, CASCADE)
        |     |-- syllabus.canonical_texts       1:1   SOURCE (immutable)
        |     |-- syllabus.extraction_jobs       queue state (NO bytes; bytes are on a tmpfs volume, 3.3)
        |     |-- syllabus.items                 DERIVED + student-authored (snippet text/context purgeable)
        |     |     |-- syllabus.item_alternatives
        |     |     `-- syllabus.item_proposals  original AI proposal, immutable
        |     |-- syllabus.unclaimed_dates       DERIVED (deterministic scan)
        |     |-- syllabus.sync_runs -> sync_run_items
        |     `-- syllabus.idempotency_keys
        `-- syllabus.calendar_events           (NEW cross-schema FK to courses, CASCADE;
                                                syllabus_id and item_id both CASCADE: no orphans exist)
syllabus.decision_audit                        append-only, content-free, NO FKs (survives deletes)
syllabus.idempotency_keys                      create-route rows: no syllabus FK, course FK CASCADE, 24 h tombstones (3.8)
ai.ai_usage                                    scope generalised by A003 (section 11), no FK by design
```

Invariant 5 mapping: **source** = `canonical_texts` (+ content hash on `syllabi`; the transient upload bytes are not in the database); **derived** = `items`, `item_alternatives`, `unclaimed_dates`, counts; **student decisions/edits** = columns on `items` plus `decision_audit`; **provenance** = `syllabi` provenance columns and `item_proposals`.

## 3. Tables (DDL sketches)

Conventions in the sketches: `-- EF` marks what the EF model declares (HasCheckConstraint, HasIndex, alternate keys); `-- RAW` marks migration SQL EF cannot express.

### 3.1 `syllabus.syllabi`

```sql
CREATE TABLE syllabus.syllabi (
  id                uuid PRIMARY KEY,
  user_id           uuid NOT NULL,
  course_id         uuid NOT NULL,
  title             varchar(200),                         -- optional, student text; not logged
  input_kind        varchar(8)  NOT NULL,                 -- upload | paste
  file_type         varchar(4),                           -- pdf | docx | txt | null (paste)
  status            varchar(12) NOT NULL,                 -- queued extracting proposing ready needs_text partial failed
  status_reason     varchar(24),
  ai_source         varchar(5)  NOT NULL,                 -- mock | model
  demo_source       boolean     NOT NULL,
  original_retained boolean     NOT NULL DEFAULT false,
  content_sha256    char(64)    NOT NULL,                 -- hash of uploaded bytes / pasted text; duplicate detection only
  page_count        smallint,
  char_count        integer     NOT NULL DEFAULT 0,
  unreadable_pages  smallint[]  NOT NULL DEFAULT '{}',
  hidden_content_removed integer NOT NULL DEFAULT 0,      -- includes stripped tag/bidi/format characters if the contract adopts R2-06(b) (contract dependency)
  hidden_content_dropped boolean NOT NULL DEFAULT false,  -- wire document.hiddenContentDropped
  injection_suspect boolean     NOT NULL DEFAULT false,
  batch_approve_allowed boolean NOT NULL DEFAULT true,    -- wire batchApproveAllowed; written by the persistence step (see notes)
  notice_version    varchar(32) NOT NULL,                 -- wire notice.version (syllabus-common noticeVersion)
  notice_attested   boolean     NOT NULL,                 -- wire notice.attested, const true
  notice_attested_at timestamptz NOT NULL,                -- wire notice.attestedAt
  unprocessed_ranges jsonb      NOT NULL DEFAULT '[]',    -- [{fromPage,toPage,reason}] counts/pages only
  dropped_invalid   integer     NOT NULL DEFAULT 0,       -- not derivable from items
  term_start        date,
  term_end          date,
  default_timezone  varchar(64) NOT NULL,                 -- IANA; allow-list membership is app-side
  extractor_version varchar(32),
  prompt_version    varchar(64),
  schema_version    varchar(32),
  model             varchar(64),                          -- 'mock' | model id; null until proposing
  row_version       integer     NOT NULL DEFAULT 1,       -- concurrency token and ETag basis; bumped by trigger on EVERY update (including job progress)
  created_at        timestamptz NOT NULL,
  updated_at        timestamptz NOT NULL,
  -- retention (section 8). The clock is moved ONLY by student-initiated writes, never by GET/poll or job progress.
  last_activity_at  timestamptz NOT NULL,                 -- set = created_at on insert; see 8 for the exact list of writers
  canonical_text_purge_at timestamptz,                    -- wire retention.canonicalTextPurgeAt; = LEAST(last_activity_at + 180 d, created_at + 365 d); NULL once purged
  canonical_text_purged_at timestamptz,                   -- non-NULL = purged; wire retention.canonicalTextPurged = (this IS NOT NULL)
  purge_after       timestamptz,                          -- terminal-empty debris only (failed / needs_text with no items), see section 8
  CONSTRAINT ux_syllabi_id_user_id UNIQUE (id, user_id),
  CONSTRAINT ux_syllabi_id_user_course UNIQUE (id, user_id, course_id),   -- composite FK target for items
  CONSTRAINT ck_syllabi_input_kind CHECK (input_kind IN ('upload','paste')),
  CONSTRAINT ck_syllabi_file_type  CHECK (file_type IS NULL OR file_type IN ('pdf','docx','txt')),
  CONSTRAINT ck_syllabi_file_vs_kind CHECK ((input_kind = 'paste') = (file_type IS NULL)),
  CONSTRAINT ck_syllabi_status CHECK (status IN ('queued','extracting','proposing','ready','needs_text','partial','failed')),
  CONSTRAINT ck_syllabi_status_reason CHECK (status_reason IS NULL OR status_reason IN
     ('no_text_layer','unreadable_file','password_protected','too_long','budget_limited','provider_unavailable','timeout','invalid_output','disabled')),
  CONSTRAINT ck_syllabi_reason_vs_status CHECK (
     (status IN ('queued','extracting','proposing','ready') AND status_reason IS NULL)
     OR (status IN ('needs_text','partial','failed') AND status_reason IS NOT NULL)),
  CONSTRAINT ck_syllabi_needs_text_reason CHECK (status <> 'needs_text' OR status_reason = 'no_text_layer'),
  CONSTRAINT ck_syllabi_ai_source CHECK (ai_source IN ('mock','model')),
  CONSTRAINT ck_syllabi_mock_is_demo CHECK (ai_source <> 'mock' OR demo_source),         -- contract 3.1
  CONSTRAINT ck_syllabi_original_not_retained CHECK (original_retained = false),          -- decision 5 (provisional)
  CONSTRAINT ck_syllabi_sha CHECK (content_sha256 ~ '^[0-9a-f]{64}$'),
  CONSTRAINT ck_syllabi_pages CHECK (page_count IS NULL OR page_count BETWEEN 1 AND 60),
  CONSTRAINT ck_syllabi_chars CHECK (char_count BETWEEN 0 AND 50000),
  CONSTRAINT ck_syllabi_counts CHECK (hidden_content_removed >= 0 AND dropped_invalid >= 0),
  CONSTRAINT ck_syllabi_unprocessed_json CHECK (jsonb_typeof(unprocessed_ranges) = 'array' AND jsonb_array_length(unprocessed_ranges) <= 60),
  CONSTRAINT ck_syllabi_term CHECK (term_start IS NULL OR term_end IS NULL OR term_end >= term_start),
  CONSTRAINT ck_syllabi_row_version CHECK (row_version >= 1),
  CONSTRAINT ck_syllabi_notice CHECK (notice_attested = true AND char_length(notice_version) >= 1),
  CONSTRAINT ck_syllabi_hidden CHECK (hidden_content_removed = 0 OR hidden_content_dropped),     -- R2-06(a): count > 0 implies dropped
  CONSTRAINT ck_syllabi_batch CHECK (NOT batch_approve_allowed OR (NOT injection_suspect AND NOT hidden_content_dropped)),   -- low_text_quality is items-derived, app-maintained
  CONSTRAINT ck_syllabi_retention CHECK (
     (canonical_text_purged_at IS NULL AND canonical_text_purge_at IS NOT NULL AND canonical_text_purge_at <= created_at + interval '365 days')
     OR (canonical_text_purged_at IS NOT NULL AND canonical_text_purge_at IS NULL)),
  CONSTRAINT ck_syllabi_activity CHECK (last_activity_at >= created_at)
);
-- RAW (migration S002): composite FK to Courses, see 9
```

Notes
- `demoCalendar: true` and `provider: "mock"` are constants on the wire and are **not stored** on `syllabi`; the provider CHECK lives on `calendar_events` (3.9). `pollAfterSeconds` is computed (null unless status in the three in-progress values).
- `counts`: only `dropped_invalid` is stored. `proposed` and `unknownDate` are `COUNT(*)` over `items` (at most about 300 rows per syllabus, indexed by `syllabus_id`), so counts are always exact and cannot drift. This answers contract Q3: exact is cheap; no eventually-consistent counter needed. The list endpoint (page of 50) does one grouped query for the page, not N queries.
- `content_sha256` is kept after the original is discarded; it is a fingerprint of a possibly copyrighted document, not content. Duplicate detection (409 `duplicate_syllabus` unless `allowDuplicate`) is `ix_syllabi_dup` plus the app check inside the advisory lock (section 6).
- `status` is the single product status. The job table (3.3) holds queue mechanics only, so there is one source of truth for "what the student sees".
- Limits (5 per course, 50 per user): not a DB constraint; counted in the create transaction under `pg_advisory_xact_lock(hashtextextended('syllabus.create:' || user_id, 0))`. Do **not** implement the per-day creation quota (10/day, contract section 11) by counting `syllabi` rows: a delete would hand quota back. Count the append-only `decision_audit` rows with `action = 'syllabus_created'` for the user in the window instead (survive deletion, written in mock mode, content-free; index `ix_decision_audit_user_action_time`). This also answers R2-19: `ai.ai_usage` has no rows in mock mode, so it cannot be the quota source. The count and the new audit row are taken inside the same per-user advisory lock as the create.
- **`row_version` and ETag (D-02).** Explicit integer (not `xmin`), per `m1-review.md` section 3, so tags survive a restore. A BEFORE UPDATE trigger `syllabus.bump_row_version()` sets `NEW.row_version := OLD.row_version + 1` on every UPDATE, so no handler can forget it and every mutation (content, decision, `sync_state`, job progress) changes the ETag. EF: `Property(x => x.RowVersion).IsConcurrencyToken().ValueGeneratedOnAddOrUpdate()` (store-generated, read back with `RETURNING`; handlers never write it). The wire ETag is `HMAC(Security:ETagKey, "syllabus"|wireId|row_version)` computed at the API edge, so it includes the resource id and is never accepted for another resource; rotating the key needs no data migration. There is no stored `etag` column. `syllabi` has no content `revision`: the contract's `revision` exists on items only.
- **`batch_approve_allowed`.** Written by the job's persistence step in the same transaction that inserts items: `NOT injection_suspect AND NOT hidden_content_dropped AND NOT EXISTS (item with 'low_text_quality')`. `ck_syllabi_batch` makes the document-level part unforgeable; the items-derived part is a server test (contract dependency: R2-06(c)). Pasted text can set `injection_suspect` (cue scan) but never `hidden_content_*`.
- **Notice and attestation.** Three columns, content-free, written in the create transaction; the paired content-free audit row is `decision_audit` action `syllabus_created` (10). `notice_attested` is `NOT NULL` with `CHECK = true`, mirroring the schema const.
- **Retention columns** are explained in section 8. `canonical_text_purge_at` is NOT NULL until purged and capped at `created_at + 365 days` by `ck_syllabi_retention`, so a bug in the activity logic cannot extend retention past the absolute maximum.

Indexes
```sql
CREATE INDEX ix_syllabi_user_course_created ON syllabus.syllabi (user_id, course_id, id DESC);   -- GET /courses/{id}/syllabi keyset (UUIDv7 order, as ix_courses_user_id_id)
CREATE INDEX ix_syllabi_dup ON syllabus.syllabi (user_id, course_id, content_sha256);
CREATE INDEX ix_syllabi_purge_after ON syllabus.syllabi (purge_after) WHERE purge_after IS NOT NULL;   -- sweeper: terminal-empty debris
CREATE INDEX ix_syllabi_text_purge_due ON syllabus.syllabi (canonical_text_purge_at) WHERE canonical_text_purged_at IS NULL;   -- retention purge job
```

### 3.2 `syllabus.canonical_texts` (source, immutable)

```sql
CREATE TABLE syllabus.canonical_texts (
  syllabus_id       uuid PRIMARY KEY,
  user_id           uuid NOT NULL,
  text              text NOT NULL,            -- NFC canonical text (D-04: NFC, not NFKC, so snippets stay verbatim); the offset space. Contains instructor PII (unredacted); redaction is model-input-only
  utf16_length      integer NOT NULL,         -- length in UTF-16 code units, the offset unit (OPEN QUESTION 1)
  lines             jsonb NOT NULL,           -- [{id:"L0001",page,start,end}] (<=~2,000 entries); derivable from text+extractor_version but stored so offsets never shift under a scanner change
  pages             jsonb NOT NULL,           -- [{page,start,end}]
  extractor_version varchar(32) NOT NULL,
  text_sha256       char(64) NOT NULL,
  created_at        timestamptz NOT NULL,
  CONSTRAINT fk_canonical_texts_syllabus_user FOREIGN KEY (syllabus_id, user_id)
     REFERENCES syllabus.syllabi (id, user_id) ON DELETE CASCADE,
  CONSTRAINT ck_canonical_texts_len CHECK (utf16_length BETWEEN 1 AND 50000 AND char_length(text) <= utf16_length),
  CONSTRAINT ck_canonical_texts_json CHECK (jsonb_typeof(lines) = 'array' AND jsonb_typeof(pages) = 'array')
);
-- RAW: immutable (invariant 5). UPDATE and TRUNCATE rejected; DELETE allowed (cascade / user right to delete).
CREATE FUNCTION syllabus.forbid_source_update() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN RAISE EXCEPTION 'syllabus.% is immutable', TG_TABLE_NAME USING ERRCODE = '55000'; END $fn$;
CREATE TRIGGER trg_canonical_texts_immutable BEFORE UPDATE ON syllabus.canonical_texts
  FOR EACH ROW EXECUTE FUNCTION syllabus.forbid_source_update();
```
- One row per syllabus in this slice (re-extraction is deferred, contract section 15). When re-extraction arrives, change the PK to `(syllabus_id, extraction_no)` additively; items then need `extraction_no`. Not built now.
- `char_length(text) <= utf16_length` is the only DB-checkable relation (code points never exceed UTF-16 units). The real offset arithmetic is tested in .NET (section 14), never by SQL `substr`, because PostgreSQL counts code points and .NET counts UTF-16 units; an emoji in a syllabus would shift every later offset between the two.
- The text is sliced for `snippet.text` and `snippet.context` in application code at **extraction time** (`string.Substring(start, end - start)`, UTF-16 offsets, .NET only), never copied from model output (contract section 2), and the slices are stored on the item (3.4) because the retention purge removes this table's row while the contract keeps serving the item.
- **Purge = `DELETE` of this row** (plus nulling the snippet columns, section 8), not an `UPDATE` to NULL. The immutability trigger blocks UPDATE and allows DELETE, so the purge needs no exception path, and a deleted row cannot be mistaken for a partly redacted one. Dead tuples (and TOAST chunks) remain on disk until autovacuum and page reuse, so "purged" means logically deleted; the notice must not promise physical erasure. Backup roll-off bounds the rest (section 12).
- Normalization: the stored text is NFC (contract 2.2). Cue matching for `supersession_cue` and similar uses a **matching-only** fold (casefold plus NFKC) computed in memory from this text and never stored, so offsets are unaffected.
- `char_length(text) <= utf16_length` remains the only DB-checkable relation. Offsets are UTF-16 code units (contract row 38); SQL never slices.

### 3.3 `syllabus.extraction_jobs` (state only; no bytes)

```sql
CREATE TABLE syllabus.extraction_jobs (
  id               uuid PRIMARY KEY,
  user_id          uuid NOT NULL,
  syllabus_id      uuid NOT NULL,
  state            varchar(10) NOT NULL,           -- queued | running | succeeded | failed   (cancel = delete the syllabus; no 'cancelled')
  attempt          smallint NOT NULL DEFAULT 0,
  available_at     timestamptz NOT NULL,
  lease_owner      varchar(64),
  lease_expires_at timestamptz,
  heartbeat_at     timestamptz,
  stage            varchar(12),                    -- same vocabulary as syllabi.status for in-progress states
  chunks_total     smallint,
  chunks_done      smallint,
  job_cap_micros   bigint NOT NULL,                -- per-job AI cap (3x single-call estimate); informational copy, ledger is authoritative
  last_error_code  varchar(40),                    -- code only, never exception text
  created_at       timestamptz NOT NULL,
  started_at       timestamptz,
  finished_at      timestamptz,
  CONSTRAINT fk_extraction_jobs_syllabus_user FOREIGN KEY (syllabus_id, user_id)
     REFERENCES syllabus.syllabi (id, user_id) ON DELETE CASCADE,
  CONSTRAINT ck_extraction_jobs_state CHECK (state IN ('queued','running','succeeded','failed')),
  CONSTRAINT ck_extraction_jobs_lease CHECK ((state = 'running') = (lease_owner IS NOT NULL AND lease_expires_at IS NOT NULL)),
  CONSTRAINT ck_extraction_jobs_chunks CHECK (chunks_done IS NULL OR chunks_total IS NULL OR chunks_done BETWEEN 0 AND chunks_total),
  CONSTRAINT ck_extraction_jobs_cap CHECK (job_cap_micros >= 0)
);
-- "1 extraction in flight per user" enforced by the database, not only by a rate limiter (contract 409 extraction_in_progress):
CREATE UNIQUE INDEX ux_extraction_jobs_one_in_flight ON syllabus.extraction_jobs (user_id) WHERE state IN ('queued','running');
CREATE INDEX ix_extraction_jobs_claim ON syllabus.extraction_jobs (available_at) WHERE state = 'queued';        -- FOR UPDATE SKIP LOCKED claim
CREATE INDEX ix_extraction_jobs_lease ON syllabus.extraction_jobs (lease_expires_at) WHERE state = 'running';   -- reaper
CREATE INDEX ix_extraction_jobs_finished ON syllabus.extraction_jobs (finished_at) WHERE finished_at IS NOT NULL; -- sweeper
CREATE INDEX ix_extraction_jobs_syllabus ON syllabus.extraction_jobs (syllabus_id);
```
- **Upload bytes are not in this database (Lead decision, R2-12, R2-13).** The v0 `UNLOGGED job_inputs` table is removed. Its residual risks (heap/TOAST pages live until vacuum and reuse, plain `pg_dump` and volume snapshots include unlogged files, parameter logging could write a 10 MB `bytea` into logs, per-job 10 MB of DB disk and memory pressure) do not apply when the bytes never reach Postgres. This also resolves R2-12: the parser needs no DB access.
- **Where the bytes live instead (design for backend-engineer and devops-engineer).**
  - A per-deployment **tmpfs (or otherwise ephemeral, non-persistent, not-snapshotted) volume** mounted into two containers: the orchestrator (read/write) and the parser worker (read-only input directory plus a small scratch directory). Never a named volume that survives container recreation, never object storage, never the DB, never logged.
  - The file name is a random server id (`job_id`), never the client file name; permissions 0600 for the orchestrator uid, the parser gets a read-only bind of exactly one file. The orchestrator deletes the file when the job reaches any terminal state, on `DELETE`, and a sweeper deletes anything older than the 1 hour ceiling (contract 5.1 item 9). A restart empties tmpfs: an in-flight job whose bytes vanished fails with `unreadable_file`/`timeout` and the student re-uploads (same behaviour the v0 crash-truncation had).
  - A **global cap on bytes in flight** (for example in-flight jobs x 10 MB, bounded by the tmpfs size limit) is a tmpfs `size=` mount option plus the one-in-flight-per-user partial unique index below; filling the volume yields `503`, never a DB problem.
  - Optional (R2-13): per-job AES-GCM key held only in orchestrator memory so a stray copy of the volume is unreadable. Not required by this design; security-engineer to decide.
  - **Pasted text** is posted as JSON, canonicalized by the orchestrator (TextPolicy, NFC), and then is `canonical_texts.text` immediately; it needs no parser and no byte file, but follows the same job path.
- The DB therefore holds only job **state**: `extraction_jobs` has no payload column and `last_error_code` is a code, never exception text. Hand-off between orchestrator and parser carries `job_id`, never bytes, through the filesystem path and stdout/IPC; the parser process has no DB connection string, no network namespace and no secrets (architecture test: the parser assembly references neither EF/Npgsql nor HTTP stacks).
- The **orchestrator's** job worker (not the parser) is a system actor (no `ICurrentUser`): it must set the user explicitly and run under a named `IgnoreQueryFilters` service (section 9, boundary-test allow-list), never by widening a request-scoped context.
- "Late job cannot recreate data" (contract section 7, extends C-21) is enforced by the schema: every row the job writes (`canonical_texts`, `items`, `unclaimed_dates`, `item_proposals`) has an FK chain to `syllabi`. PostgreSQL takes `FOR KEY SHARE` on the parent during the insert, so a concurrent delete either waits or the insert fails with SQLSTATE 23503. The worker maps 23503 (and "0 rows updated" on its own job row) to "cancelled, stop and write nothing". The worker also takes `SELECT ... FOR UPDATE` on its syllabus row for each persistence step, the same lock the DELETE handler takes (section 7).

### 3.4 `syllabus.items`

```sql
CREATE TABLE syllabus.items (
  id            uuid PRIMARY KEY,
  user_id       uuid NOT NULL,
  syllabus_id   uuid NOT NULL,
  course_id     uuid NOT NULL,                  -- denormalised for course-level queries; cannot drift (composite FK below)
  item_key      char(35) NOT NULL,              -- 'ik_' + 32 chars; computed by the app at insert, never recomputed or changed
  origin        varchar(7)  NOT NULL,           -- ai | student
  kind          varchar(12) NOT NULL,           -- assignment exam reading office_hours policy other
  title         varchar(200) NOT NULL,
  notes         varchar(500),
  -- when: what the document says (evidence)
  written_date_text varchar(120),
  written_time_text varchar(60),
  written_year  smallint, written_month smallint, written_day smallint,
  written_weekday char(2),
  -- when: server-derived local values (wall-clock, no UTC)
  when_status   varchar(14) NOT NULL,           -- resolved | needs_date | not_applicable
  date_local    date,
  end_date_local date,
  time_local    time(0),
  end_time_local time(0),
  all_day       boolean NOT NULL DEFAULT false,
  timezone      varchar(64),
  timezone_source varchar(14),                  -- stated_in_source | course_default | student
  derivation    varchar(20),                    -- explicit_full | year_from_term | relative_to_anchor | student_entered
  unknown_reason varchar(24),
  -- recurrence (weekly only); all null = none
  rec_by_day    text[],                         -- MO..SU
  rec_start_time time(0), rec_end_time time(0),
  rec_from      date, rec_until date,
  rec_until_source varchar(8),                  -- stated | term_end | student
  rec_exceptions date[] NOT NULL DEFAULT '{}',
  confidence    varchar(14) NOT NULL,
  confidence_reason varchar(200) NOT NULL,      -- D-08: equals the wire maxLength 200 (code points); OPEN QUESTION 7: code vs text
  flags         text[] NOT NULL DEFAULT '{}',
  -- snippet: offsets (UTF-16 code units into canonical_texts.text) AND the server-sliced text and context, stored at extraction time
  -- so they can be served after the canonical text is purged (D-03). text/context are NULL after the retention purge (section 8).
  snippet_start integer, snippet_end integer, snippet_page smallint, snippet_precision varchar(5),
  snippet_text    varchar(600),                 -- wire snippet.text (code points, = schema maxLength); NULL after purge or for origin=student
  context_text    varchar(1500),                -- wire snippet.context.text (code points); NULL after purge
  context_start   integer, context_end integer, -- wire snippet.context.start/end (UTF-16 code units); NULL with context_text
  edited        boolean NOT NULL DEFAULT false,
  decision      varchar(9)  NOT NULL DEFAULT 'undecided',
  sync_state    varchar(18) NOT NULL DEFAULT 'not_synced',
  synced_revision integer,
  revision      integer NOT NULL DEFAULT 1,     -- wire `revision`: CONTENT revision only (kind, title, notes, dates, times, zone, recurrence); what approve/sync name; what synced_revision refers to
  row_version   integer NOT NULL DEFAULT 1,     -- ETag basis (D-02): bumped by trigger on EVERY update incl. decision and sync_state; never on the wire
  created_at    timestamptz NOT NULL,
  updated_at    timestamptz NOT NULL,
  sort_date     date GENERATED ALWAYS AS (COALESCE(date_local, DATE '9999-12-31')) STORED,   -- keyset: (sort_date, id), needs_date sorts last
  CONSTRAINT ux_items_id_user_id UNIQUE (id, user_id),
  CONSTRAINT ux_items_item_key UNIQUE (item_key),
  CONSTRAINT fk_items_syllabus_user_course FOREIGN KEY (syllabus_id, user_id, course_id)
     REFERENCES syllabus.syllabi (id, user_id, course_id) ON DELETE CASCADE,
  -- enums
  CONSTRAINT ck_items_origin CHECK (origin IN ('ai','student')),
  CONSTRAINT ck_items_kind CHECK (kind IN ('assignment','exam','reading','office_hours','policy','other')),
  CONSTRAINT ck_items_when_status CHECK (when_status IN ('resolved','needs_date','not_applicable')),
  CONSTRAINT ck_items_tz_source CHECK (timezone_source IS NULL OR timezone_source IN ('stated_in_source','course_default','student')),
  CONSTRAINT ck_items_derivation CHECK (derivation IS NULL OR derivation IN ('explicit_full','year_from_term','relative_to_anchor','student_entered')),
  CONSTRAINT ck_items_unknown_reason CHECK (unknown_reason IS NULL OR unknown_reason IN
     ('not_stated','tba','relative_unresolvable','conflicting','ambiguous_format','table_unreadable','date_not_verifiable')),
  CONSTRAINT ck_items_confidence CHECK (confidence IN ('looks_right','probably_right','please_check')),   -- no numeric column exists
  CONSTRAINT ck_items_flags CHECK (flags <@ ARRAY['year_inferred','relative_date','timezone_assumed','time_missing','conflicting_sources',
     'tentative_language','table_layout_risk','section_ambiguous','range_end_inferred','weekday_mismatch','date_not_verifiable',
     'until_from_term','low_text_quality','duplicate_candidate','outside_term','dst_ambiguous','injection_suspect','supersession_cue']::text[] AND cardinality(flags) <= 18),   -- D-05: the 18 wire flags
  CONSTRAINT ck_items_flags_force_check CHECK (when_status <> 'resolved' OR confidence = 'please_check' OR NOT (flags && ARRAY['tentative_language','weekday_mismatch','table_layout_risk','outside_term','injection_suspect','supersession_cue','low_text_quality']::text[])),   -- contract section 4 (resolved items); the schema enforces the last three
  CONSTRAINT ck_items_student_never_looks_right CHECK (derivation IS DISTINCT FROM 'student_entered' OR confidence <> 'looks_right'),
  CONSTRAINT ck_items_decision CHECK (decision IN ('undecided','approved','skipped')),
  CONSTRAINT ck_items_sync_state CHECK (sync_state IN ('not_synced','synced','changed_since_sync','sync_failed')),
  CONSTRAINT ck_items_item_key_shape CHECK (item_key ~ '^ik_[A-Za-z0-9_-]{32}$'),
  -- contract rules, enforced by the database
  CONSTRAINT ck_items_no_approve_without_date CHECK (when_status = 'resolved' OR decision <> 'approved'),               -- needs_date / not_applicable can never be approved
  CONSTRAINT ck_items_sync_requires_approved CHECK (
     (sync_state = 'not_synced' AND synced_revision IS NULL)
     OR (sync_state IN ('synced','changed_since_sync') AND decision = 'approved' AND synced_revision IS NOT NULL)
     OR (sync_state = 'sync_failed' AND decision = 'approved')),   -- R2-11 (contract now allows it): synced_revision is NULL for a never-synced item, an integer for one that synced before
  CONSTRAINT ck_items_policy_iff_not_applicable CHECK ((kind = 'policy') = (when_status = 'not_applicable')),
  CONSTRAINT ck_items_student_rules CHECK (origin = 'ai' OR (kind <> 'policy' AND when_status = 'resolved' AND snippet_start IS NULL)),   -- student: no snippet, date required (reconciliation 18)
  CONSTRAINT ck_items_resolved_fields CHECK (when_status <> 'resolved' OR (date_local IS NOT NULL AND timezone IS NOT NULL AND derivation IS NOT NULL AND unknown_reason IS NULL)),
  CONSTRAINT ck_items_needs_date_fields CHECK (when_status <> 'needs_date' OR
     (date_local IS NULL AND end_date_local IS NULL AND time_local IS NULL AND end_time_local IS NULL AND derivation IS NULL AND NOT all_day AND unknown_reason IS NOT NULL)),
  CONSTRAINT ck_items_not_applicable_fields CHECK (when_status <> 'not_applicable' OR     -- ASSUMPTION: policy carries no date fields and no recurrence
     (date_local IS NULL AND end_date_local IS NULL AND time_local IS NULL AND end_time_local IS NULL AND derivation IS NULL AND NOT all_day AND unknown_reason IS NULL AND rec_by_day IS NULL)),
  CONSTRAINT ck_items_tz_pair CHECK ((timezone IS NULL) = (timezone_source IS NULL)),
  CONSTRAINT ck_items_all_day CHECK (NOT all_day OR (time_local IS NULL AND end_time_local IS NULL)),
  CONSTRAINT ck_items_end_time CHECK (end_time_local IS NULL OR time_local IS NOT NULL),
  CONSTRAINT ck_items_end_date CHECK (end_date_local IS NULL OR (date_local IS NOT NULL AND end_date_local >= date_local)),
  CONSTRAINT ck_items_written_parts CHECK ((written_month IS NULL OR written_month BETWEEN 1 AND 12) AND (written_day IS NULL OR written_day BETWEEN 1 AND 31)
     AND (written_year IS NULL OR written_year BETWEEN 1900 AND 2200) AND (written_weekday IS NULL OR written_weekday IN ('MO','TU','WE','TH','FR','SA','SU'))),
  CONSTRAINT ck_items_snippet CHECK (
     (origin = 'ai' AND snippet_start IS NOT NULL AND snippet_end IS NOT NULL AND snippet_precision IN ('exact','line')   -- snippet_page is nullable on the wire (page oneOf null)
        AND snippet_start >= 0 AND snippet_end > snippet_start AND snippet_end - snippet_start <= 1200)                    -- 600 code points can be 1200 UTF-16 units (R2-16); exact length is a .NET assertion
     OR (origin = 'student' AND snippet_start IS NULL AND snippet_end IS NULL AND snippet_page IS NULL AND snippet_precision IS NULL
         AND snippet_text IS NULL AND context_text IS NULL AND context_start IS NULL AND context_end IS NULL)),
  CONSTRAINT ck_items_snippet_text CHECK (snippet_text IS NULL OR char_length(snippet_text) BETWEEN 1 AND 600),
  CONSTRAINT ck_items_context CHECK (
     (context_text IS NULL AND context_start IS NULL AND context_end IS NULL)                                             -- purged (or student item)
     OR (context_text IS NOT NULL AND snippet_text IS NOT NULL AND char_length(context_text) BETWEEN 1 AND 1500
         AND context_start IS NOT NULL AND context_end IS NOT NULL AND context_start >= 0 AND context_start <= snippet_start
         AND snippet_end <= context_end AND context_end - context_start <= 3000)),                                       -- 1500 code points <= 3000 units
  CONSTRAINT ck_items_recurrence CHECK (
     (rec_by_day IS NULL AND rec_start_time IS NULL AND rec_end_time IS NULL AND rec_from IS NULL AND rec_until IS NULL AND rec_until_source IS NULL AND cardinality(rec_exceptions) = 0)
     OR (rec_by_day IS NOT NULL AND cardinality(rec_by_day) BETWEEN 1 AND 7 AND rec_by_day <@ ARRAY['MO','TU','WE','TH','FR','SA','SU']::text[]
         AND rec_start_time IS NOT NULL AND rec_from IS NOT NULL AND rec_until IS NOT NULL AND rec_until >= rec_from
         AND rec_until_source IN ('stated','term_end','student') AND cardinality(rec_exceptions) <= 20 AND kind <> 'policy')),
  CONSTRAINT ck_items_text_len CHECK (char_length(title) >= 1),
  CONSTRAINT ck_items_revision CHECK (revision >= 1 AND (synced_revision IS NULL OR synced_revision BETWEEN 1 AND revision) AND revision <= 2147483647),
  CONSTRAINT ck_items_row_version CHECK (row_version >= revision)           -- every content change is also a row change; cheap sanity check
);
-- RAW: BEFORE UPDATE trigger syllabus.bump_row_version() on items AND syllabi: NEW.row_version := OLD.row_version + 1 (always, whatever the handler wrote).
-- The handler bumps `revision` itself, only when content columns change; a trigger cannot know the intent.
CREATE INDEX ix_items_syllabus_sort ON syllabus.items (user_id, syllabus_id, sort_date, id);      -- GET /syllabi/{id}/items keyset, serves ORDER BY
CREATE INDEX ix_items_course_date ON syllabus.items (user_id, course_id, date_local) WHERE date_local IS NOT NULL;   -- course-level reads (future "what's due"), not needed by slice-1 endpoints; drop if unused
```
Notes
- **Contract rules as CHECKs**: `needs_date` cannot be approved (`ck_items_no_approve_without_date`, which also covers `policy`), `policy` iff `not_applicable`, `sync_state` implies approved + `synced_revision`, student items have no snippet and never policy, `allDay` forbids times, `endTime` needs `time`, `endDate >= date`, `resolved` requires date/zone/derivation, `needs_date` nulls the date fields. Not expressible in SQL and left to server tests (contract section 12): real calendar validity (Feb 30 cannot happen in a `date` column, so the DB does help here), zone allow-list, `approve` of `please_check` needing `acknowledgeCheck`, `looks_right` rules, `derivation` correctness.
- `ck_items_not_applicable_fields` and the "student items are always `resolved`" rule are my reading of the contract text, not schema-enforced there. Verify against `syllabus-item.schema.json` before freezing (open question 11).
- `time(0)`: HH:MM only, maps to `TimeOnly`. `date`: `DateOnly`. A midnight-crossing event is stored on `calendar_events` (end date after start date); `items` store the item's own times only.
- **Snippet (D-03, Lead decision).** v0 stored offsets only and sliced at read time. That cannot serve `snippet.text` or `snippet.context` once the canonical text has been purged, so the slices are now stored: `snippet_text`, `context_text`, `context_start`, `context_end` next to the offsets. This is a deliberate **second copy of document text** in a derived table; it is the price of the contract's retention behaviour, and it is why the purge (section 8) must clear these columns together with the canonical row. Consequences: (1) `snippet_text` and `context_text` are slices produced once in .NET at extraction (never by SQL, never from model text) and are not recomputed; (2) the contract's "`end - start` equals text length" for `exact` is a .NET assertion at insert plus a data test (14), not a CHECK, because the DB cannot count UTF-16 units; (3) the loose CHECK `end - start <= 1200` only guards against absurd rows; (4) cross-table range (`snippet_end <= utf16_length`) is a test, optionally a deferred constraint trigger. Offsets (`snippet_start`, `snippet_end`) are not content and stay after the purge; whether the wire exposes them as nullable is a contract question (R2-04, section 13). `snippet_page` is nullable because the wire page is nullable (v0 NOT NULL was wrong).
- **Provenance**: the original AI proposal is not recoverable from `items` after an edit, so it is written once to `item_proposals` (3.5) at extraction time.
- **Keyset order**: `(sort_date, id)` where `sort_date` is a stored generated column so the planner can use an ordinary b-tree and the row comparison `(sort_date, id) > ($1, $2)` works without NULL special-casing. Recurring items sort by first occurrence (`date_local`), matching the contract. Filters `decision`, `kind`, `needsDate` are applied inside a syllabus (at most about 300 rows): no extra index. `needsDate` = `when_status = 'needs_date'`.
- `item_key` global `UNIQUE` is intentional: it is a SHA-256 over (syllabusId, itemId) so it is unique anyway, and the constraint catches a bug that reuses a key. It must be computed from the **wire** ids; if the wire id codec (open question 10) changes, keys already stored stay valid because they are stored, never recomputed.
- Item cap (200 AI + student, export cap 300) is an app rule inside the syllabus row lock.
- **Edit semantics (contract 6.2, D-02).** One UPDATE with `WHERE id = @id AND user_id = @uid AND row_version = @expected` (the `If-Match` ETag decodes to the `row_version` the client saw). A content edit also sets `revision = revision + 1`; a decision, skip, undo or `sync_state` change does not touch `revision`. The trigger bumps `row_version` in every case, so a stale approve/skip pair from two tabs (both holding `row_version = 5`) lets the first win (`row_version` becomes 6) and the second match 0 rows, which a second read inside the same transaction distinguishes as 412 (row exists) from 404 (does not). `synced_revision` and the sync request's `itemRevisions` compare against `revision`, never `row_version`, so a decision made after review does not invalidate a sync of the unchanged content, while a content edit does.
- **Edit of a resolved AI item (R2-07) (contract dependency).** The schema carries `derivation`; the data design requires the handler to set `derivation = 'student_entered'` and `edited = true` on any change to date, time, end, zone or recurrence of an AI item, which `ck_items_student_never_looks_right` then turns into a cap on confidence. A server test (not a CHECK) covers that the handler does so.
- **`sync_failed` for a never-synced item (R2-11, now in the contract).** The pre-freeze contract makes it representable: `sync_failed` requires `decision = 'approved'`, `syncedRevision` is `null` for a never-synced item and an integer for one that synced earlier; `synced` and `changed_since_sync` always have an integer; `not_synced` always has null. `ck_items_sync_requires_approved` encodes exactly that three-way rule, and `ck_items_revision` already allows a NULL `synced_revision`. A failed first sync therefore leaves `sync_state = 'sync_failed'`, `synced_revision = NULL`; a failed re-sync of a synced item keeps its previous `synced_revision`. The failure detail stays in `sync_run_items`.
- `kind` mapping from the AI's finer kinds happens in code, so no finer kind is stored (contract Q1: if the finer kinds are restored, widen `ck_items_kind` additively and add `ai_kind`; the schema permits it).

### 3.5 `syllabus.item_proposals` (original AI proposal, immutable provenance)

```sql
CREATE TABLE syllabus.item_proposals (
  item_id         uuid PRIMARY KEY,
  user_id         uuid NOT NULL,
  syllabus_id     uuid NOT NULL,
  proposal        jsonb NOT NULL,              -- title, kind, when, recurrence, flags, confidence as first validated; no raw model output
  validator_version varchar(32) NOT NULL,
  prompt_version  varchar(64)  NOT NULL,
  schema_version  varchar(32)  NOT NULL,
  model           varchar(64)  NOT NULL,       -- 'mock' | model id
  created_at      timestamptz  NOT NULL,
  CONSTRAINT fk_item_proposals_item_user FOREIGN KEY (item_id, user_id) REFERENCES syllabus.items (id, user_id) ON DELETE CASCADE,
  CONSTRAINT ck_item_proposals_json CHECK (jsonb_typeof(proposal) = 'object')
);
-- RAW: BEFORE UPDATE trigger syllabus.forbid_source_update() (same function as 3.2)
CREATE INDEX ix_item_proposals_syllabus ON syllabus.item_proposals (syllabus_id);
```
Only `origin = 'ai'` items get a row. This is where "Reset to what we found" (deferred) would read from. The row contains document-derived text (title), so it follows the syllabus lifecycle exactly (cascade) and is excluded from logs.

### 3.6 `syllabus.item_alternatives` (conflicting candidates, max 3)

```sql
CREATE TABLE syllabus.item_alternatives (
  item_id  uuid NOT NULL,
  ordinal  smallint NOT NULL,
  user_id  uuid NOT NULL,
  date_local date NOT NULL,
  time_local time(0),
  PRIMARY KEY (item_id, ordinal),
  CONSTRAINT fk_item_alternatives_item_user FOREIGN KEY (item_id, user_id) REFERENCES syllabus.items (id, user_id) ON DELETE CASCADE,
  CONSTRAINT ck_item_alternatives_ordinal CHECK (ordinal BETWEEN 1 AND 3)
);
```
Rule "alternatives only when `unknown_reason = 'conflicting'`" is cross-table. Optional hardening if wanted: stored generated column `is_conflicting` on `items`, `UNIQUE (id, is_conflicting)`, and `alternatives.is_conflicting boolean NOT NULL DEFAULT true CHECK (is_conflicting)` with FK `(item_id, is_conflicting)`. Then the student setting a date (item becomes `resolved`) fails unless the handler first deletes alternatives in the same transaction, which it must do anyway. I have not run this pattern on PG16 (FK targets on a generated column should be fine); treat as optional. Default: enforce in the handler and test.

### 3.7 `syllabus.unclaimed_dates` (derived, deterministic scan)

```sql
CREATE TABLE syllabus.unclaimed_dates (
  id uuid PRIMARY KEY, user_id uuid NOT NULL, syllabus_id uuid NOT NULL,
  snippet_start integer NOT NULL, snippet_end integer NOT NULL, snippet_page smallint, snippet_precision varchar(5) NOT NULL,
  snippet_text varchar(600), context_text varchar(1500), context_start integer, context_end integer,   -- the wire unclaimed date carries a full snippet incl. context; NULL after the retention purge
  created_at timestamptz NOT NULL,
  CONSTRAINT fk_unclaimed_dates_syllabus_user FOREIGN KEY (syllabus_id, user_id) REFERENCES syllabus.syllabi (id, user_id) ON DELETE CASCADE,
  CONSTRAINT ck_unclaimed_dates_span CHECK (snippet_start >= 0 AND snippet_end > snippet_start AND snippet_end - snippet_start <= 1200 AND snippet_precision IN ('exact','line')),
  CONSTRAINT ck_unclaimed_dates_text CHECK ((snippet_text IS NULL OR char_length(snippet_text) BETWEEN 1 AND 600)
     AND ((context_text IS NULL AND context_start IS NULL AND context_end IS NULL)
          OR (context_text IS NOT NULL AND snippet_text IS NOT NULL AND char_length(context_text) BETWEEN 1 AND 1500
              AND context_start BETWEEN 0 AND snippet_start AND context_end >= snippet_end AND context_end - context_start <= 3000)))
);
CREATE INDEX ix_unclaimed_dates_syllabus ON syllabus.unclaimed_dates (user_id, syllabus_id, snippet_start, id);
```
Stored rather than recomputed so a later scanner version cannot change what the student was shown (open question 8). Never auto-promoted; no link to items.

### 3.8 `syllabus.sync_runs`, `syllabus.sync_run_items`, `syllabus.idempotency_keys`

```sql
CREATE TABLE syllabus.sync_runs (
  id uuid PRIMARY KEY, user_id uuid NOT NULL, syllabus_id uuid NOT NULL,
  provider varchar(4) NOT NULL,                       -- mock
  kind varchar(6) NOT NULL,                           -- sync | remove
  status varchar(15) NOT NULL,                        -- all_succeeded partial failed nothing_to_do
  requested integer NOT NULL,
  created_at timestamptz NOT NULL,
  CONSTRAINT ux_sync_runs_id_user UNIQUE (id, user_id),
  CONSTRAINT fk_sync_runs_syllabus_user FOREIGN KEY (syllabus_id, user_id) REFERENCES syllabus.syllabi (id, user_id) ON DELETE CASCADE,
  CONSTRAINT ck_sync_runs_provider CHECK (provider = 'mock'),
  CONSTRAINT ck_sync_runs_kind CHECK (kind IN ('sync','remove')),
  CONSTRAINT ck_sync_runs_status CHECK (status IN ('all_succeeded','partial','failed','nothing_to_do')),
  CONSTRAINT ck_sync_runs_requested CHECK (requested BETWEEN 1 AND 100)
);
CREATE TABLE syllabus.sync_run_items (
  run_id uuid NOT NULL, item_id uuid NOT NULL, user_id uuid NOT NULL,
  outcome varchar(14) NOT NULL,                       -- created updated unchanged failed not_eligible removed not_present
  error_code varchar(32),
  occurrences integer NOT NULL DEFAULT 0,
  PRIMARY KEY (run_id, item_id),
  CONSTRAINT fk_sync_run_items_run_user FOREIGN KEY (run_id, user_id) REFERENCES syllabus.sync_runs (id, user_id) ON DELETE CASCADE,
  CONSTRAINT ck_sync_run_items_outcome CHECK (outcome IN ('created','updated','unchanged','failed','not_eligible','removed','not_present')),
  CONSTRAINT ck_sync_run_items_error CHECK (error_code IS NULL OR error_code IN
     ('item_needs_date','item_not_syncable','item_not_approved','batch_not_eligible','acknowledgement_required','precondition_failed','not_found','provider_unavailable','invalid_state','event_limit_reached')),
  CONSTRAINT ck_sync_run_items_occ CHECK (occurrences BETWEEN 0 AND 60)
);
CREATE INDEX ix_sync_runs_syllabus ON syllabus.sync_runs (user_id, syllabus_id, created_at DESC);
CREATE TABLE syllabus.idempotency_keys (
  id uuid PRIMARY KEY, user_id uuid NOT NULL,
  course_id uuid NOT NULL,                            -- every key is course-scoped: the FK below is what lets create-route rows outlive a deleted syllabus yet die with the course / account
  syllabus_id uuid,                                   -- non-create routes: the path syllabus (FK, cascade). create_syllabus rows: NULL (no FK, so deleting the syllabus leaves a tombstone)
  method varchar(6) NOT NULL,                         -- POST
  route_template varchar(64) NOT NULL,                -- 'POST /courses/{courseId}/syllabi' | 'POST /syllabi/{id}/items' | 'POST /syllabi/{id}/calendar-sync' (templates, never concrete paths)
  key_hash bytea NOT NULL,                            -- SHA-256 of the client key; the key itself is never stored
  fingerprint bytea NOT NULL,                         -- HMAC-SHA-256(server key, method | route_template | CONCRETE path (so the syllabus id) | canonical body hash | for multipart: file hash + metadata); HMAC so a stored value is not a plain document hash
  result_kind varchar(12) NOT NULL,                   -- syllabus | item | sync_run
  result_id uuid NOT NULL,
  result_deleted_at timestamptz,                      -- create_syllabus only: set by the DELETE handler = tombstone; replay then answers 404
  created_at timestamptz NOT NULL,
  CONSTRAINT fk_idempotency_keys_course_user FOREIGN KEY (course_id, user_id) REFERENCES courses.courses (id, user_id) ON DELETE CASCADE,   -- CROSS-SCHEMA, third allow-list entry (section 9)
  CONSTRAINT fk_idempotency_keys_syllabus_user FOREIGN KEY (syllabus_id, user_id) REFERENCES syllabus.syllabi (id, user_id) ON DELETE CASCADE,   -- NULL syllabus_id skips the check (MATCH SIMPLE)
  CONSTRAINT ck_idempotency_keys_route CHECK (method = 'POST' AND route_template IN ('POST /courses/{courseId}/syllabi','POST /syllabi/{id}/items','POST /syllabi/{id}/calendar-sync')),
  CONSTRAINT ck_idempotency_keys_result CHECK (result_kind IN ('syllabus','item','sync_run')),
  CONSTRAINT ck_idempotency_keys_shape CHECK ((result_kind = 'syllabus') = (syllabus_id IS NULL) AND (result_deleted_at IS NULL OR result_kind = 'syllabus')),
  CONSTRAINT ck_idempotency_keys_hash CHECK (octet_length(key_hash) = 32 AND octet_length(fingerprint) = 32)
);
CREATE UNIQUE INDEX ux_idempotency_keys_scope ON syllabus.idempotency_keys (user_id, method, route_template, key_hash);   -- contract 2.1 scope; 24 h
CREATE INDEX ix_idempotency_keys_created_at ON syllabus.idempotency_keys (created_at);                                    -- sweeper
CREATE INDEX ix_idempotency_keys_result ON syllabus.idempotency_keys (result_id) WHERE result_kind = 'syllabus';           -- DELETE handler stamps tombstones
```
- Same pattern as `lectures.idempotency_keys` (hashed key, fingerprint, swept by age). Scope now matches the contract (D-06): `(user, method, route template, key)`. The same key on another syllabus therefore **finds the row**; the stored fingerprint covers the concrete path, so it mismatches and the answer is `400 validation_failed`, not the wrong object. Rows hold ids and hashes only: no title, no response body.
- **Replay returns the original outcome by id, not a stored body.** `calendar_sync`: rebuilt from `sync_runs` + `sync_run_items` (the original result as produced at `completedAt`). `create_item` and `create_syllabus`: the resource's current representation (contract: "the resource's current representation"); open question 3 is closed by the contract.
- **Lookup order (R2-10; matches the pre-freeze contract 2.1).** (4) ownership chain; **(5) idempotency lookup by `(user_id, method, route_template, key_hash)`, header only, no body** (a hit with equal fingerprint skips the rest, a hit with a different fingerprint is `400`, a miss continues); (6) counters (`429`) and the quota `409`s (`extraction_in_progress`, `duplicate_syllabus`, `syllabus_limit_reached`) apply **only on a miss** (contract: a hit skips them; the Kestrel body-rate and the per-IP limiter still bound replay floods), so a retry of an in-flight or finished create is a replay, not a `409`; (7) body read with streaming caps, and when a row was found the body is hashed (streamed, size-capped, then discarded) to compare the fingerprint: equal gives the replay, different gives `400`; (8)/(9) as before. Concurrency: the key INSERT is in the same transaction as the work; the unique index serialises two simultaneous identical requests, the loser rolls back and reads the winner's row (replay).
- **Replay after delete (R2-10, contract: `404`).** Non-create routes carry the syllabus in the path, so ownership fails first (`404`) and the cascade removing their rows is irrelevant. For `create_syllabus` the path is the course, which still exists, so the key row must outlive the syllabus or a late network retry of the original `POST` would silently create a new syllabus. Hence `syllabus_id IS NULL` on create rows (no FK to cascade), `result_id` carries the syllabus uuid, and the DELETE handler sets `result_deleted_at`. A replay that finds `result_deleted_at IS NOT NULL` (or a `result_id` with no syllabus row) answers `404 not_found`. The tombstone holds a key hash, an HMAC fingerprint and an id, is content-free, dies after 24 h by the sweeper, and dies at once with the course or user (FK cascade). Contract 5.2 lists "idempotency records" among deleted things; it must say "idempotency records except a 24 h content-free tombstone of create keys" (contract dependency). The alternative, deleting everything and accepting that a late retry re-creates, is what v0 did and what the contract rejects.

- `sync_runs` history is syllabus-scoped and dies with it, as do the mock events (3.9): nothing about a syllabus outlives it except the content-free rows listed in section 7.

### 3.9 `syllabus.calendar_events` (mock calendar; idempotent by itemKey)

```sql
CREATE TABLE syllabus.calendar_events (
  id              uuid PRIMARY KEY,
  user_id         uuid NOT NULL,
  course_id       uuid NOT NULL,
  syllabus_id     uuid NOT NULL,                 -- events die with the syllabus (CASCADE); no orphan state exists
  item_id         uuid NOT NULL,                 -- events die with the item (CASCADE)
  provider        varchar(4) NOT NULL DEFAULT 'mock',
  item_key        char(35) NOT NULL,
  occurrence_date date,                          -- recurrence: one row per occurrence (max 60); null otherwise
  title           varchar(200) NOT NULL,         -- event body copied from the item at sync time (not an orphan snapshot); gone with the event
  kind            varchar(12)  NOT NULL,
  all_day         boolean NOT NULL,
  start_date      date NOT NULL,
  start_time      time(0),
  start_timezone  varchar(64) NOT NULL,
  end_date        date NOT NULL,
  end_time        time(0),
  end_derived     boolean NOT NULL,
  synced_revision integer NOT NULL,
  synced_at       timestamptz NOT NULL,
  created_at      timestamptz NOT NULL,
  updated_at      timestamptz NOT NULL,
  CONSTRAINT ck_calendar_events_provider CHECK (provider = 'mock'),                       -- PO-6 open: only mock is expressible
  CONSTRAINT ck_calendar_events_kind CHECK (kind IN ('assignment','exam','reading','office_hours','other')),   -- policy is never synced
  CONSTRAINT ck_calendar_events_item_key CHECK (item_key ~ '^ik_[A-Za-z0-9_-]{32}$'),
  CONSTRAINT ck_calendar_events_all_day CHECK (NOT all_day OR (start_time IS NULL AND end_time IS NULL)),
  CONSTRAINT ck_calendar_events_times CHECK (all_day OR start_time IS NOT NULL),
  CONSTRAINT ck_calendar_events_end CHECK (end_date > start_date OR (end_date = start_date AND (end_time IS NULL OR start_time IS NULL OR end_time >= start_time))),
  CONSTRAINT ck_calendar_events_rev CHECK (synced_revision >= 1),
  -- IDEMPOTENCY: one event per (item, occurrence). NULLS NOT DISTINCT makes the non-recurring case (occurrence_date NULL) unique too.
  CONSTRAINT ux_calendar_events_idem UNIQUE NULLS NOT DISTINCT (user_id, provider, item_key, occurrence_date),
  CONSTRAINT ux_calendar_events_id_user UNIQUE (id, user_id)
);
-- EF (S001), same-schema composite FKs, all CASCADE (no SET NULL, no orphans):
--   fk_calendar_events_syllabus_user (syllabus_id, user_id, course_id) -> syllabi (id, user_id, course_id) ON DELETE CASCADE   -- pins course_id to the syllabus course
--   fk_calendar_events_item_user     (item_id, user_id)     -> items   (id, user_id) ON DELETE CASCADE
-- RAW (S002), CROSS-SCHEMA: fk_calendar_events_course_user (course_id, user_id) -> courses.courses (id, user_id) ON DELETE CASCADE
```
Indexes
```sql
CREATE INDEX ix_calendar_events_course_start ON syllabus.calendar_events (user_id, course_id, start_date, id);   -- GET /courses/{id}/demo-calendar?from&to, keyset by (start_date, id)
CREATE INDEX ix_calendar_events_syllabus ON syllabus.calendar_events (syllabus_id);   -- remove-by-syllabus; FK-cascade support index
CREATE INDEX ix_calendar_events_item ON syllabus.calendar_events (item_id);   -- withdraw-by-item; FK-cascade support index
```
Idempotent write (sketch, not tested):
```sql
INSERT INTO syllabus.calendar_events (...) VALUES (...)
ON CONFLICT ON CONSTRAINT ux_calendar_events_idem DO UPDATE
   SET title = EXCLUDED.title, start_date = EXCLUDED.start_date, ..., synced_revision = EXCLUDED.synced_revision, synced_at = EXCLUDED.synced_at, updated_at = EXCLUDED.updated_at
   WHERE (calendar_events.title, calendar_events.start_date, calendar_events.start_time, calendar_events.start_timezone, calendar_events.end_date, calendar_events.end_time, calendar_events.all_day)
         IS DISTINCT FROM (EXCLUDED.title, EXCLUDED.start_date, EXCLUDED.start_time, EXCLUDED.start_timezone, EXCLUDED.end_date, EXCLUDED.end_time, EXCLUDED.all_day)
RETURNING id, (xmax = 0) AS inserted;
-- row returned & inserted -> created; returned & not inserted -> updated; no row returned -> unchanged
```
Running it any number of times, or concurrently, yields one row per `(item_key, occurrence_date)`; the unique constraint is the idempotency guarantee, the `Idempotency-Key` table only provides replay of the response. Withdraw (skip/undecide) and the item `decision` change commit in the same transaction as the event DELETE (contract 6.1), so a failure rolls both back.

## 4. Query filters, ownership, and isolation (defence in depth)

1. Every entity above has `UserId` and the EF filter `x.UserId == CurrentUserId` (including `item_proposals`, `sync_run_items`, `idempotency_keys`). `decision_audit` too.
2. `OwnershipSaveChangesInterceptor` applies unchanged.
3. Composite `(id, user_id)` FKs on every child (shown above): the DB rejects an item under another user's syllabus, an alternative under another user's item, an event under another user's course. `items.course_id` is pinned to the syllabus's course by the three-column FK.
4. Background job and sweeper are the only `IgnoreQueryFilters` users; both live in two named classes added to the grep-style architecture test allow-list.
5. 404-not-403 is an API concern; every query takes `user_id` first in its index so a foreign id returns zero rows.
6. RLS: not adopted yet (still the open item from `m1-review.md` 7.7). All tables are RLS-ready (`user_id` on each, no cross-user joins). Nothing here forces the decision.

## 5. Source vs derived, provenance

| Data | Table | Mutability | Provenance |
|---|---|---|---|
| Document facts, content hash | `syllabi` | status/progress columns mutate | `extractor_version`, `prompt_version`, `schema_version`, `model`, `created_at` |
| Canonical text, lines, pages | `canonical_texts` | immutable (trigger); deleted by the retention purge | `extractor_version`, `text_sha256` |
| Original AI proposal | `item_proposals` | immutable | validator/prompt/schema/model versions |
| Current item | `items` | student-editable, `revision` | `origin`, `edited`, `derivation`, `timezone_source` |
| Unclaimed date spans | `unclaimed_dates` | immutable in practice | scanner version via `syllabi.extractor_version` |
| Decisions | `items.decision` + `decision_audit` | audit append-only | user, item, revision, time |
| Snippet text and context | `items`, `unclaimed_dates` | slices of the canonical text frozen at extraction; nulled by the retention purge |
| Mock events | `calendar_events` | upserted by sync | `synced_revision`, `synced_at` |

The model never writes `decision`, `sync_state`, `confidence` (server-derived), or `flags` it cannot remove; the only write path for AI output is the job's persistence step through the validators, and `decision` defaults to `undecided` in the DDL.

## 6. Concurrency and locking rules

Lock order, always: **user advisory lock (create / event ceiling only) -> syllabus row -> item rows (ascending id) -> events**. Nothing takes them in another order, so no cycle exists.

- **Syllabus row** `SELECT ... FROM syllabus.syllabi WHERE id = @id AND user_id = @uid FOR UPDATE`: taken by job persistence steps, DELETE, `calendar-sync`, `calendar-sync/remove` and batch decisions. Anything that may add or remove a mock event for a **single** item (single decision to `skipped`/`undecided` of a synced item, student item DELETE) takes the syllabus row `FOR SHARE` (conflicts with sync's `FOR UPDATE`, not with other single decisions). PATCH and single decisions that cannot touch events (unsynced item) take no syllabus lock; they rely on the item row lock taken by their own `UPDATE ... WHERE row_version = @expected`.
- **Full sync item-locking rule (D-12, TOCTOU, contract 2.1 "Atomicity" and 6.3).** In one transaction: (1) lock the syllabus row `FOR UPDATE`; (2) `SELECT id, decision, when_status, revision, ... FROM syllabus.items WHERE syllabus_id = @sid AND user_id = @uid AND id = ANY(@ids) ORDER BY sort_date, id FOR UPDATE` (this order is also the defined processing order, R2-15; ids not returned are per-item `not_found`); (3) **re-check on the locked rows, never on values read earlier**: `decision = 'approved'`, `when_status = 'resolved'`, `kind <> 'policy'`, and `revision = itemRevisions[id]`, each failure becoming its own per-item outcome with zero writes for that item; (4) take the per-user event-ceiling advisory lock `pg_advisory_xact_lock(hashtextextended('syllabus.events:' || @uid, 0))`, count the user's events (ceiling 5,000, R2-15) and the syllabus's events (1,000), and cap writes at 500 per call; items beyond a cap fail `event_limit_reached` with nothing written; (5) upsert events (3.9) and set `sync_state`/`synced_revision` on the locked item rows; (6) write `sync_runs`, `sync_run_items`, the `decision_audit` row and the idempotency record; commit. A skip, undo or PATCH that commits before step (2) is seen by step (3). One that starts after step (2) blocks on the item row lock (its `UPDATE` waits), then matches 0 rows because the trigger bumped `row_version` on sync's commit, and returns `412`; no event is left for a withdrawn item. `calendar-sync/remove` and batch decisions use steps (1)-(3) with their own rule.
- **Single decisions and PATCH** are optimistic on `row_version` (D-02): `UPDATE ... SET ... WHERE id=@id AND user_id=@uid AND row_version=@expected`; 0 rows is 412 or 404 as in 3.4.
- **Create:** advisory xact lock per user (`syllabus.create:`; limits, duplicate check, one-in-flight, `syllabus_created` audit count), then insert syllabus + job + idempotency key in one transaction. The orchestrator writes the upload bytes to the tmpfs volume before commit and removes them if the transaction rolls back. `ux_extraction_jobs_one_in_flight` is the backstop that turns a lost race into SQLSTATE 23505, mapped to 409 `extraction_in_progress`. When an idempotency row exists for the key none of these quota checks run (lookup order in 3.8).
- **Job persistence** steps lock the syllabus row `FOR UPDATE` (the DELETE handler's lock), so a delete either waits for the step or makes the step fail 23503/0 rows and stop.
- **Activity clock.** `last_activity_at` and `canonical_text_purge_at` are written only by the student-initiated write handlers listed in section 8, in the same UPDATE as the change itself, never by GET, poll, list, export or job progress.

## 7. Delete and purge

Deletion is **hard and immediate** for syllabi (contract section 5.2). I deliberately do not use the 7-day soft-delete tombstone from `schema-m1-m2.md` section 7: a tombstone would keep copyrighted/PII document text after the student asked for deletion. Consequence: no undo of a syllabus delete (the UI must say so) and restore-from-backup limits (section 12).

`DELETE /syllabi/{id}` has **no parameters** (Lead decision). It always removes the mock events and every derived row:

```sql
BEGIN;
SELECT 1 FROM syllabus.syllabi WHERE id = @id AND user_id = @uid FOR UPDATE;          -- 0 rows -> 404; also blocks a running job's next write
UPDATE syllabus.idempotency_keys SET result_deleted_at = now()
 WHERE user_id = @uid AND result_kind = 'syllabus' AND result_id = @id;                -- create-key tombstone (3.8)
INSERT INTO syllabus.decision_audit (..., action) VALUES (..., 'syllabus_deleted');
DELETE FROM syllabus.syllabi WHERE id = @id AND user_id = @uid;                        -- CASCADE: canonical_texts, extraction_jobs, items (-> item_proposals, item_alternatives, calendar_events), unclaimed_dates, sync_runs (-> sync_run_items), non-create idempotency keys, calendar_events (via the syllabus FK)
COMMIT;
-- after commit (orchestrator): cancel the running job, delete the job's byte file from the tmpfs volume
```
- Events are removed by `ON DELETE CASCADE` on both `fk_calendar_events_syllabus_user` and `fk_calendar_events_item_user`. There is no `keep`, no `SET NULL`, no `orphaned_at`, no `stamp_orphaned` trigger and no orphan CHECK, so the v0 "unverified trigger/RI interaction" no longer exists.
- Student item DELETE (`origin = 'student'` only): the item row delete cascades to its events; the handler takes the syllabus row `FOR SHARE` (6) and audits `item_deleted`.
- Course archive is soft (`archived_at`): syllabi, items, events are untouched (open question 9: should an archived course hide its demo events from the calendar read? Query-level, not schema). Course `?purge=true` cascades syllabi (through `fk_syllabi_course_user`), events (through `fk_calendar_events_course_user`) and create-key tombstones (through `fk_idempotency_keys_course_user`). Account deletion: `identity.users` -> `courses` -> all three chains.
- **Survivors, all content-free:** `decision_audit` rows (ids, counts, action, time) until `purge_evidence`; `ai.ai_usage` rows (scope id, cost); a 24 h create-key tombstone (hashes and ids). The `content_sha256` dedupe value dies with the syllabus row. A test scans every table for the syllabus id after delete (section 14).
- Not deleted by this call (UI must say so): copies already sent to a provider (none while mock), exported JSON files, and rows in backups for the backup window.

## 8. Retention and `purge_after`

Canonical-text retention (contract 5.3 with the Lead decision for this revision): the clock starts at `created_at`, is **extended only by student edits, decisions and syncs**, and has an **absolute maximum of 365 days from creation**. Default window: 180 days from `last_activity_at`.

`canonical_text_purge_at = LEAST(last_activity_at + 180 days, created_at + 365 days)`, computed in the same UPDATE that sets `last_activity_at = now()`. Writers that move the clock: item create, item PATCH, item DELETE, single decision, batch decision, `calendar-sync`, `calendar-sync/remove`. Writers that do **not**: `GET` of any resource (including the polling target), list, export, the demo-calendar read, job progress and status writes, the retention job itself, and the sweeper. `ck_syllabi_retention` rejects a purge time later than `created_at + 365 days`, so a handler bug cannot extend retention past the maximum. (The contract text still says "last activity (view, edit, decision, sync)" and has no maximum: contract dependency, R2-04.)

Retention purge job (hosted service, bounded batches, uses `ix_syllabi_text_purge_due`, a named `IgnoreQueryFilters` user), one transaction per syllabus, syllabus row `FOR UPDATE`:

```sql
UPDATE syllabus.items            SET snippet_text = NULL, context_text = NULL, context_start = NULL, context_end = NULL
 WHERE syllabus_id = @id AND user_id = @uid AND (snippet_text IS NOT NULL OR context_text IS NOT NULL);
UPDATE syllabus.unclaimed_dates  SET snippet_text = NULL, context_text = NULL, context_start = NULL, context_end = NULL
 WHERE syllabus_id = @id AND user_id = @uid AND (snippet_text IS NOT NULL OR context_text IS NOT NULL);
DELETE FROM syllabus.canonical_texts WHERE syllabus_id = @id AND user_id = @uid;      -- the text, lines and pages
UPDATE syllabus.syllabi SET canonical_text_purged_at = now(), canonical_text_purge_at = NULL WHERE id = @id AND user_id = @uid;
```
The item `UPDATE`s fire the `row_version` trigger, so every item ETag changes (correct: the representation changed) but `revision` does not. After the purge the wire shows `snippet.text` and `snippet.context` as null (contract dependency: `snippet.text` nullable, R2-04; the offsets stay in the row, whether the wire makes them nullable is open question 16), `retention.canonicalTextPurged: true` and `canonicalTextPurgeAt: null`. What survives the purge by design: items (titles, notes, `written_date_text`, dates), `item_proposals`, `content_sha256`, and counts. That residue is a Product Owner and counsel question, not a data-design one (R2-04); the notice text must not say "the document is deleted" while items remain. The purge is logical: pages are reclaimed by autovacuum and TOAST reuse, so physical erasure of dead tuples lags (section 12).

| Data | Retention proposal | Mechanism |
|---|---|---|
| Uploaded bytes (tmpfs volume, not in Postgres) | until job terminal; hard TTL 1 h | delete on terminal state + file sweeper on the volume |
| `extraction_jobs` | 24 h after `finished_at` | sweeper (`ix_extraction_jobs_finished`) |
| `idempotency_keys` (incl. create-key tombstones) | 24 h | sweeper (`ix_idempotency_keys_created_at`) |
| `canonical_texts`, `snippet_text`, `context_*` | 180 days from last student activity, absolute max 365 days from creation, or at delete | retention purge job above |
| `syllabi` in `failed` / `needs_text` with no items | 7 days after created (data-design behaviour **not stated in the contract or UX text**: contract dependency, add to section 10 or drop) so debris does not accumulate; student can retry by new upload | `purge_after = created_at + 7 d` set when status becomes terminal-empty; the sweeper deletes the whole syllabus |
| `syllabi` `ready`/`partial`, items, titles, notes and the rest | until student deletes, course purge, or account deletion. No automatic expiry **until PO decides P2-D4 / PO-5** (R2-04 item residue) | `purge_after` stays NULL |
| `calendar_events` | with the syllabus / course | cascade (no orphans exist) |
| `decision_audit` | content-free; retention is a counsel/PO decision (same as lectures `audit_events`, gate G1) | `syllabus.purge_evidence(user)` |
| `ai.ai_usage` (document scope) | with the user (no content) | `fk_ai_usage_user_id` cascade |

Sweeper: `SyllabusSweeper` hosted service modelled on Lectures `ExpiredRowSweeper` (config `Syllabus:SweepIntervalSeconds`, default 300, minimum 30). Each sweep is bounded batches (`DELETE ... WHERE id IN (SELECT id ... LIMIT 500)`), and is a named `IgnoreQueryFilters` user.

## 9. Cross-schema FK allow-list and test changes

New DB-level cross-schema FKs (all three `ON DELETE CASCADE`, composite, raw SQL in `S002_SyllabusIntegrity`, no EF navigation, no assembly reference):

| Constraint | From | To |
|---|---|---|
| `fk_syllabi_course_user` | `syllabus.syllabi (course_id, user_id)` | `courses.courses (id, user_id)` (uses the existing `ux_courses_id_user_id`) |
| `fk_calendar_events_course_user` | `syllabus.calendar_events (course_id, user_id)` | `courses.courses (id, user_id)` |
| `fk_idempotency_keys_course_user` | `syllabus.idempotency_keys (course_id, user_id)` | `courses.courses (id, user_id)` (lets create-key tombstones outlive a deleted syllabus yet die with the course, 3.8) |

I deliberately add **no** direct `user_id -> identity.users` FK: the composite course FK already forces `user_id` to equal the course owner and the user cascade reaches syllabi through `courses` (`fk_courses_user_id`). That keeps the allow-list at three additions. (Lectures added both; if the Lead Architect prefers symmetry, `fk_syllabi_user_id` and `fk_calendar_events_user_id` are harmless extra entries.) A003 adds none (the ledger deliberately has no scope FK).

No `SET NULL` FK exists in this module any more (D-01); the job, event, item and sync FKs to `syllabi`/`items` are same-schema, so they are not in the allow-list test.

Required changes when implemented (not done now):
1. `PersistenceTests.Cross_schema_foreign_keys_are_exactly_the_allow_listed_set`: the expected list is sorted by `conname` (database collation). Insert in sorted position: `("fk_calendar_events_course_user", "syllabus", "courses", "c")` after `fk_audio_progress_session_user`, and `("fk_syllabi_course_user", "syllabus", "courses", "c")` after `fk_summary_revisions_session_user`. Update the explanatory comment ("ADR-014 ... ADR-0xx syllabus"). Because `ORDER BY con.conname` follows the DB collation, assert on a set or `OrderBy(StringComparer.Ordinal)` to avoid collation surprises.
2. `docs/architecture/backend-foundation.md`: addendum "allow-list is now eight entries" and a new ADR number for the Syllabus FKs; module table row for Syllabus (depends on Courses); migration-order rule: Identity, Courses, Lectures, Transcript, Ai, **Syllabus** (needs Courses, and Ai for runtime only).
3. `ModuleBoundaryTests` hand-maintained constants (`m1-review.md` 7.6): add `Tether.Syllabus` to the module list, the allowed project references (SharedKernel, `Tether.Courses.Contracts`, `Tether.Ai.Contracts` if the document seam is a contract), and the `IgnoreQueryFilters` allow-list (job worker, sweeper).
4. Test data: fixtures that create `syllabus.*` rows need real `courses.courses` rows first (same lesson as the earlier FK).
5. `Program.cs` migration order list: add Syllabus after Courses.

## 10. Migration plan (S-prefixed, one logical change each, all `internal`)

1. `S001_Syllabus` (EF-generated, hand-checked): schema, tables 3.1-3.9 minus raw-only objects, same-schema composite FKs, CHECKs, ordinary indexes, `sort_date` generated column (`HasComputedColumnSql(..., stored: true)`), `NULLS NOT DISTINCT` unique (`HasIndex().AreNullsDistinct(false)` if the pinned Npgsql version supports it, else raw SQL; verify). Remove any scaffolded `xmin` (none expected: revision is an int). Class `internal`.
2. `S002_SyllabusIntegrity` (raw SQL, like Lectures `M006`): the three cross-schema FKs, triggers (`forbid_source_update`, `bump_row_version` on `syllabi` and `items`, audit append-only), `syllabus.purge_evidence(uuid)`. Requires Identity and Courses migrated first; fails by design on orphans (none can exist, tables are new).
3. `S003_DecisionAudit` may be folded into S001 (it has no FK); keep separate only if the audit retention decision slips. Prefer one fewer migration: fold it, and put its triggers in S002.
4. `A003_AiScope` in the Ai module (section 11).

`decision_audit` sketch (content-free, copy of the Lectures pattern):
```sql
CREATE TABLE syllabus.decision_audit (
  id uuid PRIMARY KEY, user_id uuid NOT NULL,
  syllabus_id uuid NOT NULL,                    -- NOT a FK: survives syllabus deletion
  item_id uuid,                                 -- NOT a FK
  item_revision integer,                        -- content revision at the time (create: NULL)
  content_sha256 char(64),                      -- syllabus_created only: the dedupe hash the contract asks the create audit to carry (content-free fingerprint; user-scoped; purged with the user by purge_evidence)
  notice_version varchar(32),                   -- syllabus_created only (contract 5 notes: audit carries the notice version)
  item_count integer,                           -- counts for batch_approved, synced, sync_removed, exported
  action varchar(32) NOT NULL,                  -- syllabus_created approved skipped undecided batch_approved synced sync_removed item_deleted syllabus_deleted exported   (D-09: create and export added; delete is one action now)
  occurred_at timestamptz NOT NULL,
  CONSTRAINT ck_decision_audit_action CHECK (action IN ('syllabus_created','approved','skipped','undecided','batch_approved','synced','sync_removed','item_deleted','syllabus_deleted','exported'))
);
CREATE INDEX ix_decision_audit_user_time ON syllabus.decision_audit (user_id, occurred_at);
CREATE INDEX ix_decision_audit_user_action_time ON syllabus.decision_audit (user_id, action, occurred_at);   -- creation quota count (3.1, R2-19)
-- RAW: BEFORE UPDATE/DELETE + BEFORE TRUNCATE triggers calling syllabus.forbid_evidence_change(), allowed only when current_setting('syllabus.evidence_purge', true) = 'on';
-- syllabus.purge_evidence(p_user_id uuid) is the single purge path, exactly as lectures.purge_evidence.
```
No `user_id` FK (same as lectures `audit_events`), so a user hard-delete does **not** cascade here: the account-deletion job must call `syllabus.purge_evidence(user)` (and `lectures.purge_evidence`). That is a required step in the account purge test (14, D-12). No titles, no snippets, no dates in this table.

Rollback safety
- Order: roll back Syllabus **before** Courses and Identity (the FKs reference them). `S002.Down` drops triggers, functions, then the FKs; `S001.Down` drops tables and does not drop the schema (same limitation as other modules: the schema hosts its history table; `m1-review.md` appendix item 5).
- Down is dev-only. Once any production data exists the policy is expand/contract and forward-fix; `Down` of `S001` destroys student data by definition.
- Everything is new tables, so applying S001/S002 cannot break running modules. A bad deploy can be handled by feature-flagging the endpoints off (module disabled) with the tables left in place.
- Re-scaffold probe after each: `dotnet ef migrations add Probe` yields an empty migration (no snapshot drift), then remove; guard against `public partial class` and `xmin` per `src/api/README.md`.
- `ux_extraction_jobs_one_in_flight`, `ux_calendar_events_idem` and the generated column are created on empty tables: no `CONCURRENTLY` needed. Later index additions on populated tables use `CREATE INDEX CONCURRENTLY` with `suppressTransaction`.
- A003 rollback specifics are in section 11.

## 11. AI usage / ledger generalisation: proposed `A003_AiScope` (outline only)

Gap (from `syllabus-extraction-design.md` section 3, and the code I read): `ai.ai_usage.session_id uuid NOT NULL` with no FK; `ck_ai_usage_workflow IN ('RollingSummary','Compaction','LostTrack')`; `ix_ai_usage_session_created`; the ledger sums per session for `PerSessionMicro` (`Ledger.cs:122`); reservations are `ai_usage` rows with outcome `reserved` (A002); `AiRequest.SessionId` and `AiGateway` check `LectureState.Active`. A document job has no lecture, so the ledger needs a scope.

Approach: **expand now, contract later**, because the ledger is spend evidence that must not be rewritten or lose rows.

`A003_AiScope` (expand):
```sql
ALTER TABLE ai.ai_usage
  ADD COLUMN scope_kind varchar(10) NOT NULL DEFAULT 'lecture',
  ADD COLUMN pool       varchar(10) NOT NULL DEFAULT 'lecture',   -- BUDGET POOL dimension (D-10, R2-14): daily caps, floors and the global ceiling are summed per pool
  ADD COLUMN scope_id   uuid;
UPDATE ai.ai_usage SET scope_id = session_id;                       -- backfill; table is small (cost ledger rows), single statement
ALTER TABLE ai.ai_usage ALTER COLUMN scope_id SET NOT NULL;
ALTER TABLE ai.ai_usage ALTER COLUMN session_id DROP NOT NULL;      -- syllabus rows have no session
ALTER TABLE ai.ai_usage
  ADD CONSTRAINT ck_ai_usage_scope_kind CHECK (scope_kind IN ('lecture','syllabus')),
  ADD CONSTRAINT ck_ai_usage_pool CHECK (pool IN ('lecture','syllabus') AND pool = scope_kind),   -- equal today; a separate column so a future workflow can share a pool without changing scope identity
  ADD CONSTRAINT ck_ai_usage_scope_session CHECK ((scope_kind = 'lecture' AND session_id = scope_id) OR (scope_kind = 'syllabus' AND session_id IS NULL));
ALTER TABLE ai.ai_usage DROP CONSTRAINT ck_ai_usage_workflow;       -- recreate, as A002 did for the outcome list
ALTER TABLE ai.ai_usage ADD CONSTRAINT ck_ai_usage_workflow CHECK (
     (scope_kind = 'lecture'  AND workflow IN ('RollingSummary','Compaction','LostTrack'))
  OR (scope_kind = 'syllabus' AND workflow = 'SyllabusExtract'));
CREATE INDEX ix_ai_usage_scope_created ON ai.ai_usage (user_id, scope_kind, scope_id, created_at);
CREATE INDEX ix_ai_usage_user_pool_created ON ai.ai_usage (user_id, pool, created_at);          -- per-user daily cap PER POOL (replaces the use of ix_ai_usage_user_created for cap checks)

-- Global monthly ceiling per pool: today ai.global_spend has PRIMARY KEY (period) and the Ledger upsert is ON CONFLICT (period).
ALTER TABLE ai.global_spend ADD COLUMN pool varchar(10) NOT NULL DEFAULT 'lecture';
ALTER TABLE ai.global_spend DROP CONSTRAINT global_spend_pkey, ADD PRIMARY KEY (period, pool);
ALTER TABLE ai.global_spend ADD CONSTRAINT ck_global_spend_pool CHECK (pool IN ('lecture','syllabus'));
```
- No new FK on `scope_id`, on purpose: same rationale as today ("deleting a lecture must not hand the student their daily budget back"): deleting a syllabus must not refund its spend either. Rows die with the user (`fk_ai_usage_user_id`). The cross-schema allow-list is unchanged by A003.
- Per-user daily cap: `ix_ai_usage_user_pool_created` (above). The durable per-day **creation** quota is NOT counted from `ai_usage` (mock mode writes no rows, R2-19); it is counted from `decision_audit` (3.1). Per-job cap = `SUM(est_cost_micros) WHERE scope_kind='syllabus' AND scope_id = @job_or_syllabus`. Which id is the scope id (job vs syllabus) is a backend decision; I recommend the **syllabus id**, so cap and delete-cancel logic share one key, and one extraction per syllabus in this slice.
- EF: `AiUsageRow` gains `ScopeKind`/`ScopeId`; the write path dual-writes `SessionId` for lecture rows during the transition; query filter unchanged. Snapshot regenerated; class `internal`; no `xmin`.
- The gateway scope abstraction (replace `SessionId`/`ISessionDirectory`/`LectureState.Active` with scope kind + id + "still live and owned" check + cutoff lease) is backend-engineer/ai-agent-engineer's contract. The DB requirement it implies: the "still live" check for a syllabus is `EXISTS` on `syllabus.syllabi` + job `state = 'running'` read through a Syllabus contract port, never a cross-module table read.
- **Budget pools (D-10, R2-14; contract section 8 "separate budget pool with a protected floor").** The per-user daily cap is `SUM(cost) WHERE user_id = @u AND pool = @p AND created_at >= day start`, evaluated per pool with its own configured cap (`Ai:Pools:syllabus:PerUserDailyMicro`, `Ai:Pools:lecture:PerUserDailyMicro`), so a large syllabus can exhaust only the syllabus pool for that student and never the lecture AI allowance. The same holds globally: `ai.global_spend` gets one row per `(period, pool)` and `GlobalSpend` reads and upserts with `ON CONFLICT (period, pool)` (code change in `Ledger.cs`, verify against the migration that created the PK name; I read the A001 SQL, not a running DB). The **protected floor** is a config value for the lecture pool that the syllabus pool can never borrow against: because pools are separate rows and caps, the floor is implicit (no shared remainder exists). If the Product Owner wants a *shared* hard monthly total with a lecture floor, that needs one more aggregate check and is an open question (17). Reservations (A002) carry `pool` through the same column. Unchanged otherwise: `catch_ups`, `summary_revisions`, the reaper.

`A004_AiScopeContract` (later, after all code reads `scope_id`): drop `session_id`, `ix_ai_usage_session_created`, `ck_ai_usage_scope_session`; keep `scope_id`. Not proposed for now.

Rollback of A003
- Safe only before any `scope_kind = 'syllabus'` row exists. `Down` must therefore start with a guard: `DO $$ BEGIN IF EXISTS (SELECT 1 FROM ai.ai_usage WHERE scope_kind <> 'lecture') THEN RAISE EXCEPTION 'A003 cannot be rolled back: syllabus-scope ledger rows exist (forward-fix instead)'; END IF; END $$;` then restore `session_id NOT NULL` (all remaining rows have it set), recreate the old workflow CHECK, drop the new CHECKs, index and columns.
- The `Down` guard also covers `ai.global_spend`: it refuses when any `pool <> 'lecture'` row exists, otherwise restores `PRIMARY KEY (period)` and drops `pool`.
- Deleting or re-labelling syllabus ledger rows to make Down succeed is **not allowed**: it would erase spend evidence and refund the budget.
- Deploy order: A003 applied and the dual-writing Ai code deployed **before** the Syllabus job starts writing scope rows. Old Ai code against the new table keeps working during the window (`scope_kind` defaults to `lecture`, `scope_id` NOT NULL has no default, so old code that omits it would fail: write `scope_id` in the same release as A003, or give `scope_id` a temporary default expression via a trigger `scope_id := session_id`. Open question 12.)

## 12. Security, privacy, failure paths

- **Data minimisation.** Originals are never persisted and **never reach Postgres** (tmpfs only, 3.3). No raw model output is stored (`item_proposals` holds validated structure); no prompts or completions anywhere; the ledger stays content-free. Document text that is stored: `canonical_texts.text` (purgeable), `snippet_text`/`context_text` (purgeable), and titles, notes and `written_*` text on items (kept until delete; R2-04 residue question). All of it follows the syllabus lifecycle and is excluded from logs.
- **Canonical text is stored unredacted** (the redactor runs on model input only; design V3 needs the original offsets). It can contain instructor names, emails and phone numbers, and so can `snippet_text`/`context_text`. Security-engineer to confirm that is acceptable at rest, and whether column-level encryption or only volume encryption is expected. Not in logs, not in error `detail`. **Logging for the DB role:** the application role used by this module must run with `log_statement = none` for parameters, `log_parameter_max_length = 0` and `log_min_duration_statement` without parameters (devops item; R2-13(3) no longer concerns upload bytes but the same hazard applies to canonical text and snippet writes). `auto_explain` stays off in production.
- **Untrusted text:** length CHECKs on `title`/`notes`; no markdown/HTML interpretation at the DB. Control-character and Unicode-property rules (R2-01, R2-02) belong in the single `TextPolicy` at write time, not in SQL; I will not duplicate a weaker list in a CHECK. A CHECK of `title !~ '[\x00-\x1f]'` is possible after the contract settles the final rule.
- **Normalization.** Stored text is NFC (contract 2.2); the database stores what the policy produced and does not normalize. A test inserts an NFC string and reads it back byte-identical (14).
- **Failure modes covered by schema:** concurrent delete vs job (FK), duplicate sync (unique), double create (unique), approve of undated item (CHECK), cross-user references (composite FKs), stuck leases (reaper index), stale-tab approve/skip (row_version CAS), retention extension bug (`ck_syllabi_retention`), tmpfs loss on restart (job fails `unreadable_file`/`timeout`, student re-uploads).
- **Backups.** Deletes are hard, so a restore from a backup taken before a deletion resurrects the deleted syllabus until the application re-applies it. There is no tombstone table in the same database that survives a restore (it would be restored too; the 24 h create-key tombstones do not help here). Mitigation for the restore runbook: re-run deletions from the content-free application log of `syllabus_deleted` audit events exported outside the database. The **retention purge has the same property**: a restored backup brings back purged snippet text and canonical text until the purge job runs again (it will, because `canonical_text_purge_at` is restored too and is in the past). Needs devops and security to agree; known limitation, not solved here. With uploads off the database there is no `--no-unlogged-table-data` requirement any more, and the byte volume is excluded from any snapshot (tmpfs) by construction; devops X-07 verifies it.
- **Ownership of the byte volume** (replaces the v0 UNLOGGED discussion): the volume is not a database object, so none of the schema guarantees apply to it. Controls are in 3.3 (random file name, mode 0600, read-only bind to the parser, 1 h sweeper, size-capped tmpfs). Residual: bytes live in RAM or swap while a job runs; the host must have swap encrypted or disabled (devops).
- **Rate limits (R2-14, backend/devops, not DB objects).** Syllabus routes get their **own** write and read buckets, separate from the global 30/min write bucket used by live-lecture writes and "I Lost Track", so a syllabus flood or buggy client cannot throttle a student mid-lecture. Per-user caps for `calendar-sync` and batch decisions need numbers (contract dependency, R2-15).
- Not stored anywhere in this module: OAuth tokens (none; mock provider), student calendar contents, filenames (only `file_type`), upload bytes.

## 13. Open questions

Closed since v0: offset unit (contract row 38: UTF-16, .NET slicing); transient upload bytes (Lead: tmpfs, no Postgres); `calendarEvents=keep` (removed); idempotent replay body (contract: original outcome, current representation for creates).

5. **Retention of ready syllabi, item residue and decision audit**: P2-D4 / PO-5 / counsel (G1). `purge_after` stays NULL for ready syllabi. Which item text (titles, notes, `written_date_text`, `item_proposals`) outlives the canonical-text purge is still a PO/counsel decision (R2-04).
6. **Account deletion** must call `syllabus.purge_evidence` and `lectures.purge_evidence`; who owns that job (no account-deletion job exists yet, `m1-review.md` section 1).
7. **`confidenceReason`**: store the fixed-vocabulary text (as drafted, `varchar(200)`) or a code mapped to text at the API (better for localisation and tests, needs a contract decision).
8. **Unclaimed dates**: stored (proposed) vs recomputed on read. They now also carry `snippet_text`/`context_*` and are purged with the rest.
9. **Course archive** vs demo calendar visibility (query-level).
10. **Wire id codec (Lead question: where does it live?).** Findings: no codec exists in `src/api`; existing resources put a bare `Guid` on the wire (System.Text.Json default, format `"D"`, lowercase hyphenated; the Courses list cursor uses `"N"`, which is a cursor, not an id). The pre-freeze contract fixes the wire form: optional 3-letter prefix plus `_`, then a lowercase hyphenated GUID (`Guid.ToString("D")`), at most 64 characters, any other spelling non-canonical and `400`. **Proposal:** a small `WireId` static class in `Tether.SharedKernel` (the only project every module may already reference, so no new dependency edge): `Format(string prefix, Guid id)` = `prefix + "_" + id.ToString("D")`; `TryParse(string expectedPrefix, string? s, out Guid id)` accepts only that exact shape (expected prefix, one underscore, 36 characters) and then **re-formats the parsed Guid and requires ordinal string equality with the input**, which rejects uppercase, braces, URN, missing hyphens and trailing characters in one rule (R2-05, no alias forms). A wrong prefix (an `itm_` id on a `syl_` route) is non-canonical for that position. Non-canonical gives `400 validation_failed` per contract; canonical-but-missing or foreign gives `404`/per-item `not_found`. Callers: **only the API edge** (route and body binders, JSON converters for response DTOs, the ETag HMAC input, and the `itemKey` hash input); domain, EF, DB and jobs use `Guid`. Batch/sync body keys are decoded once and then de-duplicated as `Guid`s. `item_key` is stored, never recomputed, so a later codec change cannot alter stored keys. Prefixed ids stay Syllabus-module-only unless the Lead wants Courses/Lectures migrated (that would change frozen lecture.v1 contracts: not proposed); `crs_`-prefixed course ids in the contract examples are therefore accepted only where the contract says so, otherwise the bare form. Owner: backend-engineer implements; contract owner keeps the single canonical form in 2.1.
11. **Contract gaps to confirm**: `not_applicable` item field nullity (policy); student items always `resolved`; whether `needs_date` items can be `please_check` only; item `notes` max 500 and `title` max 200.
12. **A003 transition**: a trigger default for `scope_id` vs same-release code change (section 11). Also the `global_spend` PK change needs a coordinated `Ledger.cs` change in the same release.
13. **Where job queue lives**: `syllabus.extraction_jobs` (proposed) vs a shared `ai.jobs` named in `backend-foundation.md` section 8 (no such table exists in A001/A002).
14. Product limits only app-enforced today (5/50 syllabi, 300 items, 60 occurrences, 1,000/5,000 events): confirm none should become DB triggers. The event ceilings are enforced under advisory/row locks (6).
15. RLS decision (carried from `m1-review.md`) still open.
16. **Offsets after the purge.** Contract dependency: if the wire makes `snippet.start`/`end` nullable after the purge (R2-04 suggests it), the rows keep the offsets anyway; if not, the wire still returns offsets into a text that no longer exists. Cosmetic, but decide before the freeze.
17. **Shared monthly ceiling with lecture floor.** Pools are independent (section 11). If the PO wants one overall monthly cap with a lecture reserve, add an aggregate check; not designed.
18. **`low_text_quality` and `batch_approve_allowed`** is an items-derived rule maintained by the persistence step; if items can change flags later (edit), recompute in the same transaction. Confirm the contract says items' flags are server-only after creation.
19. **Create audit carries `content_sha256`** (contract section 5 notes). It stays in the append-only table after the syllabus is deleted until `purge_evidence`, i.e. a fingerprint of a possibly copyrighted document survives deletion. Counsel/security to decide whether to drop it from the audit row or to HMAC it; the dedupe hash on `syllabi` itself dies with the syllabus.
20. **R2-16** (cap units: code points vs UTF-16 units) is a contract decision that changes CHECK limits in 3.4; the DB follows the schema as it stands.

## 14. Verification plan (all NOT RUN; nothing exists to run)

Method: Testcontainers or the compose Postgres 16 with real migrations, never EF in-memory; every constraint test is an INSERT/UPDATE that must raise the named constraint. Scratch DBs created and dropped by the test, never `college_companion_dev`. Test ids here are `DB-`, `I-`, `X-`, `C-`, `P-` (the `D-nn` ids in the security re-review are divergences, not tests).

DDL and constraints
- DB-01 Table-driven negative tests, one per CHECK in 3.1, 3.4, 3.9 (needs_date approved, policy approved, `sync_state` without approved, originalRetained true, provider `google`, mock source without demo, all-day with time, end before start, student item with snippet, AI item without snippet, bad flag value including a 19th flag, 4 alternatives, recurrence missing days, `hidden_content_removed > 0` with `hidden_content_dropped = false`, `batch_approve_allowed` with `injection_suspect`, `canonical_text_purge_at` beyond `created_at + 365 days`, `student_entered` with `looks_right`, `injection_suspect` flag on a `looks_right` resolved item, `confidence_reason` of exactly 200 accepted and 201 rejected, create-route idempotency row with a `syllabus_id`).
- DB-02 Migration up/down on an empty DB and on a DB with data: Identity -> Courses -> Lectures -> Transcript -> Ai(A003) -> Syllabus up; reverse down; `pending migrations` empty; no table left except history tables.
- DB-03 Re-scaffold probe yields an empty migration; no `public` migration types; no `xmin`.
- DB-04 `pg_constraint` allow-list test updated (section 9, three syllabus entries) and green; assert that **no** FK in the syllabus schema uses `SET NULL` (`confdeltype = 'n'` count is zero) and that every FK from `calendar_events` is `CASCADE`.
- DB-05 Immutability: UPDATE on `canonical_texts` and `item_proposals` raises 55000; DELETE via cascade and the retention purge succeed. Append-only on `decision_audit`: UPDATE/DELETE/TRUNCATE raise; `syllabus.purge_evidence(user)` removes only that user's rows.
- DB-06 `NULLS NOT DISTINCT` unique: two non-recurring events for one `item_key` fail; recurrence rows with different `occurrence_date` coexist.
- DB-07 **Row version (D-02):** two sessions read an item at `row_version = 5`; both try approve and skip with `WHERE row_version = 5`; exactly one updates (becomes 6), the other matches 0 rows; a `sync_state` change and a job-progress update on `syllabi` also bump it; `revision` changes only on content edits; a decision does not invalidate `itemRevisions` for an unchanged item.
- DB-08 **Purge:** time-shift `last_activity_at`; run the purge job; assert `canonical_texts` row gone, every `snippet_text`/`context_*` NULL in `items` and `unclaimed_dates`, `canonical_text_purged_at` set, `canonical_text_purge_at` NULL, items otherwise intact; assert a GET/list/export/poll never moves `last_activity_at`; assert an edit moves it; assert a syllabus 364 days old with fresh activity is still purged at 365 days from creation.
- DB-09 **Verbatim:** store a string with ligatures (U+FB01), fullwidth digits and a superscript; read back byte-identical (NFC, not NFKC); `snippet_text` equals the .NET slice of the stored text by UTF-16 offsets, with an emoji before the snippet.
- DB-10 **No bytes in Postgres:** after an upload, `pg_class`/`pg_stat_user_tables` shows no table with a `bytea` column in schema `syllabus`; search all syllabus tables for the upload's first bytes (canary) and find only the canonical text derived by the parser; kill the worker mid-job and confirm the tmpfs file is removed by the sweeper within 1 h.

Isolation
- I-01 Two-user fixture: composite FKs reject an item under B's syllabus, an alternative under B's item, an event for B's course, a job for B's syllabus, an idempotency key for B's course (SQLSTATE 23503).
- I-02 Every `DbSet` returns zero cross-user rows through the EF filter; `IgnoreQueryFilters` appears only in the named classes (job worker, sweeper, retention purge job; architecture test).
- I-03 HTTP: foreign or missing id returns 404 for all routes (contract section 12 "ownership 404 for every route").

Deletion and recovery
- X-01 Delete: zero rows left for the syllabus in every table in section 2, including events and `item_proposals`; audit row `syllabus_deleted` remains; a scan of all tables and of the tmpfs volume for the syllabus id and content hash finds only the audit/tombstone rows named in section 7.
- X-02 Course purge removes syllabi, events and create-key tombstones; user hard-delete leaves zero rows in all schemas except `decision_audit` until `purge_evidence` runs, then zero.
- X-03 Delete during a running job (inject a pause between stages): the worker's next write fails 23503 / zero-row update and writes nothing; no resurrected rows (extends C-21). Same with course purge.
- X-04 Idempotency of events: sync x3 sequentially and x20 concurrently yields exactly one event per item, first call `created`, later `unchanged`; edit then sync yields `updated`; withdraw removes the event in the same transaction and a forced failure rolls back both; remove then re-add yields exactly one.
- X-05 `Idempotency-Key`: replay returns the original outcome and sets the replay header; same key + different body is 400; same key on another syllabus (same user) is 400 (fingerprint includes the path), not the first syllabus; same key by another user is independent; two concurrent identical creates produce one syllabus and one job; a retry of an in-flight create is a replay, not `409 extraction_in_progress`; **delete then retry the original create returns 404 and creates nothing**; the tombstone is gone after 24 h and with the course.
- X-06 One extraction in flight: a second queued/running job for the same user violates `ux_extraction_jobs_one_in_flight`; after the first finishes the second is allowed.
- X-07 Backup/restore drill (devops): dump/restore into a scratch DB, compare row counts for all syllabus tables, confirm no upload bytes exist in the dump (the volume is not part of it), confirm the documented "deleted-after-backup resurrects" and "purged-text-after-restore returns until the purge job reruns" limitations and the re-apply procedure.
- X-08 A003: backfill equals `session_id` for every existing row; old and new workflows pass the CHECK; `pool` equals `scope_kind`; `Down` is rejected when a syllabus-scope row exists in `ai_usage` or `global_spend` (negative test) and succeeds when none; sums per user/day unchanged before and after.
- X-09 **Budget pools:** exhaust the syllabus pool for a user (and globally) and assert a lecture AI request for the same user still passes its daily and monthly checks; the reverse holds; per-pool sums use only that pool's rows.
- X-10 **Sync item locking (D-12):** deterministic interleavings with two connections: (a) skip commits between sync's read and its lock: the item is `not_eligible`/`precondition_failed`, no event; (b) skip starts after sync holds the item lock: the skip waits, then returns 412, the event exists and matches the approved revision; (c) PATCH racing sync: sync either writes the old revision (and PATCH then 412s) or sees the new revision and refuses; (d) two syncs of different syllabi for one user at the 5,000 ceiling: the advisory lock admits only one over the line; (e) call with 120 ids processes in `(sort_date, id)` order and fails the tail with `event_limit_reached`.

Data integrity of content
- C-01 For every `origin='ai'` item (fixtures S1-S4 with astral characters added): `0 <= start < end <= utf16_length` while the text exists, the stored `snippet_text` equals the .NET slice, and `exact` snippets equal the verbatim line span.
- C-02 No document text, titles, snippets or notes appear in logs, `last_error_code`, or `ai_usage` (log canary extended to this module, contract section 12), including with the DB role's statement logging at its configured setting.
- C-03 Keyset pagination: 300 items with 40 `needs_date`, pages of 50 by `(sort_date, id)`: no gaps, no duplicates, needs-date last, stable under concurrent insert.

Performance (measure, do not assume; targets are proposals)
- P-01 Seed 200 users x 50 syllabi x 200 items: `EXPLAIN (ANALYZE, BUFFERS)` of items keyset page, syllabi list, demo-calendar range read, sync upsert of 100 items (up to 60 occurrences each is 6,000 rows, so sync must batch to 500 per call), syllabus DELETE cascade, retention purge of one syllabus. Expect index scans on `ix_items_syllabus_sort`, `ix_syllabi_user_course_created`, `ix_calendar_events_course_start`, `ix_syllabi_text_purge_due`. Target p95 under 20 ms for the three reads (same bar as `schema-m1-m2.md` section 8), cascade delete of a 300-item syllabus under 200 ms. The items table is wider now (two text columns up to 600 + 1,500 characters x 300 rows per syllabus); measure row size and TOAST use. Record actuals; none exist yet.

## 15. Not done

No DbContext, entity, migration, test, seed, or edit to `backend-foundation.md` / `PersistenceTests.cs` / `ModuleBoundaryTests.cs`. No SQL executed, including the trigger, the `NULLS NOT DISTINCT` unique, the generated column, the `ON CONFLICT (period, pool)` change to `ai.global_spend`, and the nullable-composite-FK (MATCH SIMPLE) behaviour for create-key tombstones, which I expect to behave as described but have not run. Contract state when written: the pre-freeze schema and sections 2.1 already carry the idempotency lookup at step 5, nullable `snippet.text` with null context, the first-sync `sync_failed` rule and the canonical id form; the prose of 5.2 and 5.3 still said "idempotency records deleted", "snippet.text stays" and "last activity (view, ...)" with no 365-day maximum. Where this doc needs text the contract does not yet say it is marked **(contract dependency)**: the 24 h create-key tombstone exception (R2-10), the activity definition and 365-day maximum (R2-04), tag/bidi stripping counting toward `hiddenContentRemoved` (R2-06b), unit of caps (R2-16), the 7-day debris purge (D-14). Nothing in this file authorizes real documents, a real provider or a Google write, and no gate is affected. Security-engineer review is still needed for sections 3.3, 7, 8 and 12 before implementation.
