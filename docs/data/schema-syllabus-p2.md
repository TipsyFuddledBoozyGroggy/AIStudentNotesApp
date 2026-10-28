# Schema: Syllabus module (Phase 2, slice 1) - DRAFT PROPOSAL, nothing implemented

Author: database-architect. Date: 2026-10-28. Status: design only. No migration, entity, DbContext or test exists for this module; no DDL below has been executed against any database (every SQL block is a sketch to be run in a scratch DB first, see section 14). Implements the DRAFT contract `docs/API_CONTRACTS_P2.md` + `src/shared/contracts/syllabus.v1/` (both can still change; schema, fixtures and this file must move together). Nothing here authorizes a real provider, a Google write, real student documents or any gate (G1-G5) change.

Sources read: `API_CONTRACTS_P2.md`, `syllabus.v1/*.schema.json` (syllabus, create request, calendar event, item constraints, common defs), `ai/syllabus-extraction-design.md` (sections 3, 7, 12, 14), `data/schema-m1-m2.md`, `data/m1-review.md`, `architecture/backend-foundation.md` (incl. addenda), Ai migrations `A001_Ai` / `A002_BudgetReservationAndPendingDedupe`, `AiDbContext`, Lectures `M006_LecturesIntegrity` and `LecturesDbContext` (idempotency/audit pattern), and the `Cross_schema_foreign_keys_are_exactly_the_allow_listed_set` test in `PersistenceTests.cs`.

## 1. Assumptions, scope, dependencies

- PostgreSQL 16 (compose is 16.15). I rely on PG15+ features: `ON DELETE SET NULL (column_list)` and `UNIQUE ... NULLS NOT DISTINCT`. Both are PG15+, so fine on 16; EF/Npgsql cannot scaffold the first, so it is raw SQL (as all cross-schema FKs already are).
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
        |     |-- syllabus.extraction_jobs       queue state
        |     |     `-- syllabus.job_inputs      UNLOGGED, transient bytes
        |     |-- syllabus.items                 DERIVED + student-authored
        |     |     |-- syllabus.item_alternatives
        |     |     `-- syllabus.item_proposals  original AI proposal, immutable
        |     |-- syllabus.unclaimed_dates       DERIVED (deterministic scan)
        |     |-- syllabus.sync_runs -> sync_run_items
        |     `-- syllabus.idempotency_keys
        `-- syllabus.calendar_events           (NEW cross-schema FK to courses, CASCADE;
                                                syllabus_id / item_id SET NULL, see 7)
syllabus.decision_audit                        append-only, content-free, NO FKs (survives deletes)
ai.ai_usage                                    scope generalised by A003 (section 11), no FK by design
```

Invariant 5 mapping: **source** = `canonical_texts` (+ content hash on `syllabi`, and the transient `job_inputs`); **derived** = `items`, `item_alternatives`, `unclaimed_dates`, counts; **student decisions/edits** = columns on `items` plus `decision_audit`; **provenance** = `syllabi` provenance columns and `item_proposals`.

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
  hidden_content_removed integer NOT NULL DEFAULT 0,
  injection_suspect boolean     NOT NULL DEFAULT false,
  unprocessed_ranges jsonb      NOT NULL DEFAULT '[]',    -- [{fromPage,toPage,reason}] counts/pages only
  dropped_invalid   integer     NOT NULL DEFAULT 0,       -- not derivable from items
  term_start        date,
  term_end          date,
  default_timezone  varchar(64) NOT NULL,                 -- IANA; allow-list membership is app-side
  extractor_version varchar(32),
  prompt_version    varchar(64),
  schema_version    varchar(32),
  model             varchar(64),                          -- 'mock' | model id; null until proposing
  revision          integer     NOT NULL DEFAULT 1,       -- concurrency token / ETag basis
  created_at        timestamptz NOT NULL,
  updated_at        timestamptz NOT NULL,
  purge_after       timestamptz,                          -- see section 8
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
  CONSTRAINT ck_syllabi_revision CHECK (revision >= 1)
);
-- RAW (migration S002): composite FK to Courses, see 9
```

Notes
- `demoCalendar: true` and `provider: "mock"` are constants on the wire and are **not stored** on `syllabi`; the provider CHECK lives on `calendar_events` (3.9). `pollAfterSeconds` is computed (null unless status in the three in-progress values).
- `counts`: only `dropped_invalid` is stored. `proposed` and `unknownDate` are `COUNT(*)` over `items` (at most about 300 rows per syllabus, indexed by `syllabus_id`), so counts are always exact and cannot drift. This answers contract Q3: exact is cheap; no eventually-consistent counter needed. The list endpoint (page of 50) does one grouped query for the page, not N queries.
- `content_sha256` is kept after the original is discarded; it is a fingerprint of a possibly copyrighted document, not content. Duplicate detection (409 `duplicate_syllabus` unless `allowDuplicate`) is `ix_syllabi_dup` plus the app check inside the advisory lock (section 6).
- `status` is the single product status. The job table (3.3) holds queue mechanics only, so there is one source of truth for "what the student sees".
- Limits (5 per course, 50 per user): not a DB constraint; counted in the create transaction under `pg_advisory_xact_lock(hashtextextended('syllabus.create:' || user_id, 0))`. Do **not** implement the per-day creation quota (10/day, contract section 11) by counting `syllabi` rows: a delete would hand quota back. Use the HTTP limiter plus `ai.ai_usage` rows (survive deletion, section 11).
- `revision` and ETag: explicit integer (not `xmin`), per `m1-review.md` section 3, so tags survive a restore. EF: `Property(x => x.Revision).IsConcurrencyToken()`, incremented in the handler. **There is no stored `etag` column**: the wire ETag is `HMAC(Security:ETagKey, "syllabus"|id|revision)`, computed at the API edge like the existing opaque weak ETags, so rotating the key never needs a data migration. If the Lead Architect prefers a stored column, it would be redundant data that can disagree with `revision`.

Indexes
```sql
CREATE INDEX ix_syllabi_user_course_created ON syllabus.syllabi (user_id, course_id, id DESC);   -- GET /courses/{id}/syllabi keyset (UUIDv7 order, as ix_courses_user_id_id)
CREATE INDEX ix_syllabi_dup ON syllabus.syllabi (user_id, course_id, content_sha256);
CREATE INDEX ix_syllabi_purge_after ON syllabus.syllabi (purge_after) WHERE purge_after IS NOT NULL;   -- sweeper
```

### 3.2 `syllabus.canonical_texts` (source, immutable)

```sql
CREATE TABLE syllabus.canonical_texts (
  syllabus_id       uuid PRIMARY KEY,
  user_id           uuid NOT NULL,
  text              text NOT NULL,            -- NFKC canonical text; the offset space. Contains instructor PII (unredacted); redaction is model-input-only
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
- The text is sliced for `snippet.text` in application code (`string.Substring(start, end - start)`), never copied from model output (contract section 2).
- Not separately purgeable in v1: purging it alone would orphan every snippet offset. Deleting the syllabus deletes it.

### 3.3 `syllabus.extraction_jobs` and `syllabus.job_inputs`

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

-- RAW: transient upload bytes (decision 5: original NOT retained). UNLOGGED on purpose.
CREATE UNLOGGED TABLE syllabus.job_inputs (
  job_id     uuid PRIMARY KEY,
  user_id    uuid NOT NULL,
  content    bytea NOT NULL,                      -- upload bytes or pasted text as UTF-8
  byte_size  integer NOT NULL,
  created_at timestamptz NOT NULL,
  CONSTRAINT fk_job_inputs_job FOREIGN KEY (job_id) REFERENCES syllabus.extraction_jobs (id) ON DELETE CASCADE,
  CONSTRAINT ck_job_inputs_size CHECK (byte_size BETWEEN 1 AND 10485760 AND byte_size = octet_length(content))
);
CREATE INDEX ix_job_inputs_created ON syllabus.job_inputs (created_at);
```
- Why `UNLOGGED`: unlogged tables are not written to WAL, so with PITR/WAL archiving the original file never lands in WAL or replicas, and `pg_basebackup` excludes unlogged relation files. After a crash PostgreSQL truncates them, which the design treats as a normal failure ("upload again"). That is the closest PostgreSQL gives to "held only for the extraction job". `pg_dump` does include unlogged data by default; the backup job must use `--no-unlogged-table-data` (devops item). Cleanup: row deleted when the job reaches any terminal state; hard TTL 1 h by sweeper (contract note).
- Alternative (open question 2): keep bytes in process memory or a temp file. Simpler and equally "not retained", but a restart mid-extraction then loses the job with no durable trace, and a temp file is a second data store to secure and purge. I recommend the unlogged table; security-engineer to decide.
- The composite `(job_id)` FK to jobs only; `user_id` here is for the query filter and isolation tests, and a CHECK-less copy could disagree. Make it airtight with the composite form instead: add `UNIQUE (id, user_id)` on jobs and FK `(job_id, user_id)`. I show it simple above; use the composite in the migration.
- The job worker is a system actor (no `ICurrentUser`): it must set the user explicitly and run under a named `IgnoreQueryFilters` service (section 9, boundary-test allow-list), never by widening a request-scoped context.
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
  confidence_reason varchar(160) NOT NULL,      -- OPEN QUESTION 7: code vs text
  flags         text[] NOT NULL DEFAULT '{}',
  -- snippet: offsets only. Text is sliced from canonical_texts at read time.
  snippet_start integer, snippet_end integer, snippet_page smallint, snippet_precision varchar(5),
  edited        boolean NOT NULL DEFAULT false,
  decision      varchar(9)  NOT NULL DEFAULT 'undecided',
  sync_state    varchar(18) NOT NULL DEFAULT 'not_synced',
  synced_revision integer,
  revision      integer NOT NULL DEFAULT 1,     -- wire `revision`; ETag basis; also what approve/sync name (syncedRevision)
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
     'until_from_term','low_text_quality','duplicate_candidate','outside_term','dst_ambiguous']::text[] AND cardinality(flags) <= 16),
  CONSTRAINT ck_items_decision CHECK (decision IN ('undecided','approved','skipped')),
  CONSTRAINT ck_items_sync_state CHECK (sync_state IN ('not_synced','synced','changed_since_sync','sync_failed')),
  CONSTRAINT ck_items_item_key_shape CHECK (item_key ~ '^ik_[A-Za-z0-9_-]{32}$'),
  -- contract rules, enforced by the database
  CONSTRAINT ck_items_no_approve_without_date CHECK (when_status = 'resolved' OR decision <> 'approved'),               -- needs_date / not_applicable can never be approved
  CONSTRAINT ck_items_sync_requires_approved CHECK (
     (sync_state = 'not_synced' AND synced_revision IS NULL)
     OR (sync_state <> 'not_synced' AND decision = 'approved' AND synced_revision IS NOT NULL)),
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
     (origin = 'ai' AND snippet_start IS NOT NULL AND snippet_end IS NOT NULL AND snippet_page IS NOT NULL AND snippet_precision IN ('exact','line')
        AND snippet_start >= 0 AND snippet_end > snippet_start AND snippet_end - snippet_start <= 600)
     OR (origin = 'student' AND snippet_start IS NULL AND snippet_end IS NULL AND snippet_page IS NULL AND snippet_precision IS NULL)),
  CONSTRAINT ck_items_recurrence CHECK (
     (rec_by_day IS NULL AND rec_start_time IS NULL AND rec_end_time IS NULL AND rec_from IS NULL AND rec_until IS NULL AND rec_until_source IS NULL AND cardinality(rec_exceptions) = 0)
     OR (rec_by_day IS NOT NULL AND cardinality(rec_by_day) BETWEEN 1 AND 7 AND rec_by_day <@ ARRAY['MO','TU','WE','TH','FR','SA','SU']::text[]
         AND rec_start_time IS NOT NULL AND rec_from IS NOT NULL AND rec_until IS NOT NULL AND rec_until >= rec_from
         AND rec_until_source IN ('stated','term_end','student') AND cardinality(rec_exceptions) <= 20 AND kind <> 'policy')),
  CONSTRAINT ck_items_text_len CHECK (char_length(title) >= 1),
  CONSTRAINT ck_items_revision CHECK (revision >= 1 AND (synced_revision IS NULL OR synced_revision BETWEEN 1 AND revision))
);
CREATE INDEX ix_items_syllabus_sort ON syllabus.items (user_id, syllabus_id, sort_date, id);      -- GET /syllabi/{id}/items keyset, serves ORDER BY
CREATE INDEX ix_items_course_date ON syllabus.items (user_id, course_id, date_local) WHERE date_local IS NOT NULL;   -- course-level reads (future "what's due"), not needed by slice-1 endpoints; drop if unused
```
Notes
- **Contract rules as CHECKs**: `needs_date` cannot be approved (`ck_items_no_approve_without_date`, which also covers `policy`), `policy` iff `not_applicable`, `sync_state` implies approved + `synced_revision`, student items have no snippet and never policy, `allDay` forbids times, `endTime` needs `time`, `endDate >= date`, `resolved` requires date/zone/derivation, `needs_date` nulls the date fields. Not expressible in SQL and left to server tests (contract section 12): real calendar validity (Feb 30 cannot happen in a `date` column, so the DB does help here), zone allow-list, `approve` of `please_check` needing `acknowledgeCheck`, `looks_right` rules, `derivation` correctness.
- `ck_items_not_applicable_fields` and the "student items are always `resolved`" rule are my reading of the contract text, not schema-enforced there. Verify against `syllabus-item.schema.json` before freezing (open question 11).
- `time(0)`: HH:MM only, maps to `TimeOnly`. `date`: `DateOnly`. A midnight-crossing event is stored on `calendar_events` (end date after start date); `items` store the item's own times only.
- **Snippet**: offsets only. `snippet.text` is a function of `canonical_texts.text`. Two consequences: no second copy of document text in a derived table, and the contract's "end - start equals text length" becomes true by construction for `exact`. Cross-table range check (`snippet_end <= utf16_length`) cannot be a CHECK; test it (14) and optionally add a deferred constraint trigger.
- **Provenance**: the original AI proposal is not recoverable from `items` after an edit, so it is written once to `item_proposals` (3.5) at extraction time.
- **Keyset order**: `(sort_date, id)` where `sort_date` is a stored generated column so the planner can use an ordinary b-tree and the row comparison `(sort_date, id) > ($1, $2)` works without NULL special-casing. Recurring items sort by first occurrence (`date_local`), matching the contract. Filters `decision`, `kind`, `needsDate` are applied inside a syllabus (at most about 300 rows): no extra index. `needsDate` = `when_status = 'needs_date'`.
- `item_key` global `UNIQUE` is intentional: it is a SHA-256 over (syllabusId, itemId) so it is unique anyway, and the constraint catches a bug that reuses a key. It must be computed from the **wire** ids; if the wire id codec (open question 10) changes, keys already stored stay valid because they are stored, never recomputed.
- Item cap (200 AI + student, export cap 300) is an app rule inside the syllabus row lock.
- Edit semantics (contract 6.2) become one UPDATE with `WHERE id = @id AND user_id = @uid AND revision = @expected`, `SET revision = revision + 1, updated_at = @now`. Row count 0 distinguishes 404 from 412 by a second read inside the same transaction.
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
  snippet_start integer NOT NULL, snippet_end integer NOT NULL, snippet_page smallint NOT NULL, snippet_precision varchar(5) NOT NULL,
  created_at timestamptz NOT NULL,
  CONSTRAINT fk_unclaimed_dates_syllabus_user FOREIGN KEY (syllabus_id, user_id) REFERENCES syllabus.syllabi (id, user_id) ON DELETE CASCADE,
  CONSTRAINT ck_unclaimed_dates_span CHECK (snippet_start >= 0 AND snippet_end > snippet_start AND snippet_end - snippet_start <= 600 AND snippet_precision IN ('exact','line'))
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
     ('item_needs_date','item_not_syncable','item_not_approved','batch_not_eligible','acknowledgement_required','precondition_failed','not_found','provider_unavailable','invalid_state')),
  CONSTRAINT ck_sync_run_items_occ CHECK (occurrences BETWEEN 0 AND 60)
);
CREATE INDEX ix_sync_runs_syllabus ON syllabus.sync_runs (user_id, syllabus_id, created_at DESC);

CREATE TABLE syllabus.idempotency_keys (
  id uuid PRIMARY KEY, user_id uuid NOT NULL,
  syllabus_id uuid NOT NULL,                          -- every key is scoped to a syllabus (create-syllabus inserts the key in the same transaction as the new syllabus)
  route varchar(24) NOT NULL,                         -- create_syllabus | create_item | calendar_sync
  key_hash bytea NOT NULL,                            -- SHA-256 of the client key; the key itself is never stored
  fingerprint bytea NOT NULL,                         -- hash of method+route+body; mismatch -> 400 validation_failed
  result_kind varchar(12) NOT NULL,                   -- syllabus | item | sync_run
  result_id uuid NOT NULL,
  created_at timestamptz NOT NULL,
  CONSTRAINT fk_idempotency_keys_syllabus_user FOREIGN KEY (syllabus_id, user_id) REFERENCES syllabus.syllabi (id, user_id) ON DELETE CASCADE,
  CONSTRAINT ck_idempotency_keys_route CHECK (route IN ('create_syllabus','create_item','calendar_sync')),
  CONSTRAINT ck_idempotency_keys_result CHECK (result_kind IN ('syllabus','item','sync_run'))
);
CREATE UNIQUE INDEX ux_idempotency_keys_user_route_key ON syllabus.idempotency_keys (user_id, route, key_hash);   -- 24 h per user and per route
CREATE INDEX ix_idempotency_keys_created_at ON syllabus.idempotency_keys (created_at);                           -- sweeper
```
- Same pattern as `lectures.idempotency_keys` (hashed key, fingerprint, swept by age), plus a `route` column because the contract scopes keys "per user and per route". A replay returns the **original result** by loading `result_id`: the sync body is rebuilt from `sync_runs` + `sync_run_items`, so no response JSON (which could carry titles) is stored. For `create_syllabus` and `create_item` the replay returns the resource's current representation with `Idempotency-Replayed: true` (open question 3: contract says "original result"; current state of the same resource is the honest, cheap reading).
- Concurrency: the key INSERT is in the same transaction as the work; the unique index serialises two simultaneous requests with the same key (the loser blocks, then gets the unique violation, rolls back its own work and returns the winner's result).
- Deleting the syllabus deletes its keys; a retry of the same `Idempotency-Key` after a delete then creates a new syllabus. Accepted: delete is a deliberate act.
- `sync_runs` history is syllabus-scoped and dies with it. The mock events (3.9) are what must outlive a syllabus under `keep`, not the run log.

### 3.9 `syllabus.calendar_events` (mock calendar; idempotent by itemKey)

```sql
CREATE TABLE syllabus.calendar_events (
  id              uuid PRIMARY KEY,
  user_id         uuid NOT NULL,
  course_id       uuid NOT NULL,
  syllabus_id     uuid,                          -- NULL once orphaned by calendarEvents=keep
  item_id         uuid,                          -- NULL once the item is gone
  provider        varchar(4) NOT NULL DEFAULT 'mock',
  item_key        char(35) NOT NULL,
  occurrence_date date,                          -- recurrence: one row per occurrence (max 60); null otherwise
  title           varchar(200) NOT NULL,         -- snapshot, so keep-orphans stay displayable
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
  orphaned_at     timestamptz,
  created_at      timestamptz NOT NULL,
  updated_at      timestamptz NOT NULL,
  CONSTRAINT ck_calendar_events_provider CHECK (provider = 'mock'),                       -- PO-6 open: only mock is expressible
  CONSTRAINT ck_calendar_events_kind CHECK (kind IN ('assignment','exam','reading','office_hours','other')),   -- policy is never synced
  CONSTRAINT ck_calendar_events_item_key CHECK (item_key ~ '^ik_[A-Za-z0-9_-]{32}$'),
  CONSTRAINT ck_calendar_events_all_day CHECK (NOT all_day OR (start_time IS NULL AND end_time IS NULL)),
  CONSTRAINT ck_calendar_events_times CHECK (all_day OR start_time IS NOT NULL),
  CONSTRAINT ck_calendar_events_end CHECK (end_date > start_date OR (end_date = start_date AND (end_time IS NULL OR start_time IS NULL OR end_time >= start_time))),
  CONSTRAINT ck_calendar_events_orphan CHECK ((syllabus_id IS NULL) = (orphaned_at IS NOT NULL)),
  CONSTRAINT ck_calendar_events_rev CHECK (synced_revision >= 1),
  -- IDEMPOTENCY: one event per (item, occurrence). NULLS NOT DISTINCT makes the non-recurring case (occurrence_date NULL) unique too.
  CONSTRAINT ux_calendar_events_idem UNIQUE NULLS NOT DISTINCT (user_id, provider, item_key, occurrence_date),
  CONSTRAINT ux_calendar_events_id_user UNIQUE (id, user_id)
);
-- RAW (S002): composite FKs, see 7 for why SET NULL (syllabus_id) and why course cascade
```
Indexes
```sql
CREATE INDEX ix_calendar_events_course_start ON syllabus.calendar_events (user_id, course_id, start_date, id);   -- GET /courses/{id}/demo-calendar?from&to, keyset by (start_date, id)
CREATE INDEX ix_calendar_events_syllabus ON syllabus.calendar_events (syllabus_id) WHERE syllabus_id IS NOT NULL;  -- remove-by-syllabus, withdraw-by-item
CREATE INDEX ix_calendar_events_item ON syllabus.calendar_events (item_id) WHERE item_id IS NOT NULL;
CREATE INDEX ix_calendar_events_orphaned ON syllabus.calendar_events (orphaned_at) WHERE orphaned_at IS NOT NULL;   -- optional TTL sweep
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

1. Every entity above has `UserId` and the EF filter `x.UserId == CurrentUserId` (including `job_inputs` if mapped, `item_proposals`, `sync_run_items`, `idempotency_keys`). `decision_audit` too.
2. `OwnershipSaveChangesInterceptor` applies unchanged.
3. Composite `(id, user_id)` FKs on every child (shown above): the DB rejects an item under another user's syllabus, an alternative under another user's item, an event under another user's course. `items.course_id` is pinned to the syllabus's course by the three-column FK.
4. Background job and sweeper are the only `IgnoreQueryFilters` users; both live in two named classes added to the grep-style architecture test allow-list.
5. 404-not-403 is an API concern; every query takes `user_id` first in its index so a foreign id returns zero rows.
6. RLS: not adopted yet (still the open item from `m1-review.md` 7.7). All tables are RLS-ready (`user_id` on each, no cross-user joins). Nothing here forces the decision.

## 5. Source vs derived, provenance

| Data | Table | Mutability | Provenance |
|---|---|---|---|
| Document facts, content hash | `syllabi` | status/progress columns mutate | `extractor_version`, `prompt_version`, `schema_version`, `model`, `created_at` |
| Canonical text, lines, pages | `canonical_texts` | immutable (trigger) | `extractor_version`, `text_sha256` |
| Original AI proposal | `item_proposals` | immutable | validator/prompt/schema/model versions |
| Current item | `items` | student-editable, `revision` | `origin`, `edited`, `derivation`, `timezone_source` |
| Unclaimed date spans | `unclaimed_dates` | immutable in practice | scanner version via `syllabi.extractor_version` |
| Decisions | `items.decision` + `decision_audit` | audit append-only | user, item, revision, time |
| Mock events | `calendar_events` | upserted by sync | `synced_revision`, `synced_at` |

The model never writes `decision`, `sync_state`, `confidence` (server-derived), or `flags` it cannot remove; the only write path for AI output is the job's persistence step through the validators, and `decision` defaults to `undecided` in the DDL.

## 6. Concurrency and locking rules

- Per-syllabus serialisation point: `SELECT ... FROM syllabus.syllabi WHERE id = @id AND user_id = @uid FOR UPDATE`. Taken by: job persistence steps, DELETE, calendar-sync, calendar-sync/remove, batch decisions. Item PATCH and single decisions rely on optimistic `revision` and do not take it (they touch one row), except when they must remove a mock event (withdraw), where they take it.
- Create: advisory xact lock per user (limits, duplicate check, one-in-flight) then insert syllabus + job + job_input + idempotency key in one transaction; `ux_extraction_jobs_one_in_flight` is the backstop that turns a lost race into SQLSTATE 23505, mapped to 409 `extraction_in_progress`.
- Lock ordering to avoid deadlocks: syllabus row, then items (ordered by id), then events. DELETE: syllabus row first.

## 7. Delete, purge and `calendarEvents=remove|keep`

Deletion is **hard and immediate** for syllabi (contract section 5: "deletes items, canonical text, revisions, approvals, jobs, sync records"). I deliberately do not use the 7-day soft-delete tombstone from `schema-m1-m2.md` section 7: a tombstone would keep copyrighted/PII document text after the student asked for deletion. Consequence: no undo of a syllabus delete (the UI must say so) and restore-from-backup limits (section 12).

`DELETE /syllabi/{id}?calendarEvents=remove|keep` (parameter mandatory, 400 if absent, so it is a handler argument, never a default):

```sql
BEGIN;
SELECT 1 FROM syllabus.syllabi WHERE id = @id AND user_id = @uid FOR UPDATE;          -- 0 rows -> 404; also blocks a running job's next write
-- remove:
DELETE FROM syllabus.calendar_events WHERE syllabus_id = @id AND user_id = @uid;
-- keep: nothing explicit; the FK action below orphans them (trigger stamps orphaned_at)
INSERT INTO syllabus.decision_audit (..., action) VALUES (..., 'syllabus_deleted_remove' | 'syllabus_deleted_keep');
DELETE FROM syllabus.syllabi WHERE id = @id AND user_id = @uid;                        -- cascades to everything in 2 except events
COMMIT;
```
Event FKs (RAW, migration S002):
```sql
ALTER TABLE syllabus.calendar_events
  ADD CONSTRAINT fk_calendar_events_syllabus_user FOREIGN KEY (syllabus_id, user_id)
      REFERENCES syllabus.syllabi (id, user_id) ON DELETE SET NULL (syllabus_id),       -- PG15+: nulls only syllabus_id, keeps user_id
  ADD CONSTRAINT fk_calendar_events_item_user FOREIGN KEY (item_id, user_id)
      REFERENCES syllabus.items (id, user_id) ON DELETE SET NULL (item_id),
  ADD CONSTRAINT fk_calendar_events_course_user FOREIGN KEY (course_id, user_id)
      REFERENCES courses.courses (id, user_id) ON DELETE CASCADE;                       -- CROSS-SCHEMA, allow-list entry

CREATE FUNCTION syllabus.stamp_orphaned() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
  IF OLD.syllabus_id IS NOT NULL AND NEW.syllabus_id IS NULL AND NEW.orphaned_at IS NULL THEN NEW.orphaned_at := now(); END IF;
  RETURN NEW;
END $fn$;
CREATE TRIGGER trg_calendar_events_orphan BEFORE UPDATE ON syllabus.calendar_events
  FOR EACH ROW EXECUTE FUNCTION syllabus.stamp_orphaned();
```
- **keep** therefore means: event rows survive with `syllabus_id`/`item_id` NULL, `orphaned_at` set, title/time snapshot and `item_key` intact. They stay visible in the course demo calendar (same query; orphans are included) and the unique constraint still prevents a second event for that `item_key`. Re-uploading the same file creates a new syllabus id and therefore new item keys, so it will not collide with orphans (the demo calendar would show both; that is what "keep" asks for).
- **remove** deletes this syllabus's events before the syllabus. It is not a separate cascade so the choice stays visible in code and audit.
- Safeguards: (a) PostgreSQL runs user triggers for RI actions, so the stamp trigger should fire for the SET NULL update, and `ck_calendar_events_orphan` is checked after BEFORE triggers; **this interaction is unverified, test it first** (section 14, D-07). Fallback if it fails: the DELETE handler runs `UPDATE ... SET orphaned_at = now()` for `keep` before deleting, and the CHECK is dropped. (b) I deliberately did not add a CHECK tying `item_id` to `syllabus_id`: during a cascade the items delete (setting `item_id` NULL) can run before the syllabus SET NULL, and a CHECK would fail mid-cascade.
- Student item DELETE (`origin = 'student'` only): handler deletes the item's events first (both modes of the same transaction), then the item.
- Course archive is soft (`archived_at`): syllabi, items, events are untouched (open question 9: should an archived course hide its demo events from the calendar read? Query-level, not schema). Course `?purge=true` cascades syllabi (through `fk_syllabi_course_user`) **and** events (through `fk_calendar_events_course_user`), so orphaned `keep` events do not outlive their course. Account deletion: `identity.users` -> `courses` -> both chains, so `keep` never survives account deletion.
- Not deleted by this call (UI must say so): copies already sent to a provider (none while mock), exported JSON files, and rows in backups for the backup window.

## 8. Retention and `purge_after`

| Data | Retention proposal | Mechanism |
|---|---|---|
| Uploaded bytes (`job_inputs`) | until job terminal; hard TTL 1 h | delete on terminal state + sweeper |
| `extraction_jobs` | 24 h after `finished_at` | sweeper (`ix_extraction_jobs_finished`) |
| `idempotency_keys` | 24 h | sweeper (`ix_idempotency_keys_created_at`) |
| `syllabi` in `failed` / `needs_text` with no items | 7 days after created (proposal) so debris does not accumulate; student can retry by new upload | `purge_after = created_at + 7 d` set when status becomes terminal-empty; cleared never |
| `syllabi` `ready`/`partial` and all children | until student deletes, course purge, or account deletion. No automatic expiry **until PO decides P2-D4 / PO-5** | `purge_after` stays NULL |
| `calendar_events` (attached) | with the syllabus / course | cascade |
| `calendar_events` (orphaned by `keep`) | indefinite as drafted. Recommend a TTL or dropping `keep` (contract Q2); schema supports either via `orphaned_at` | optional sweeper on `ix_calendar_events_orphaned` |
| `decision_audit` | content-free; retention is a counsel/PO decision (same as lectures `audit_events`, gate G1) | `syllabus.purge_evidence(user)` |
| `ai.ai_usage` (document scope) | with the user (no content) | `fk_ai_usage_user_id` cascade |

Sweeper: `SyllabusSweeper` hosted service modelled on Lectures `ExpiredRowSweeper` (config `Syllabus:SweepIntervalSeconds`, default 300, minimum 30). Each sweep is bounded batches (`DELETE ... WHERE id IN (SELECT id ... LIMIT 500)`), and is a named `IgnoreQueryFilters` user. If a future retention policy purges a `ready` syllabus, the sweeper must use the **remove** semantics (do not leave titles behind in orphaned events without an explicit student choice). That is a proposal for the PO decision, not a default I can approve.

## 9. Cross-schema FK allow-list and test changes

New DB-level cross-schema FKs (both `ON DELETE CASCADE`, both composite, both raw SQL in `S002_SyllabusIntegrity`, no EF navigation, no assembly reference):

| Constraint | From | To |
|---|---|---|
| `fk_syllabi_course_user` | `syllabus.syllabi (course_id, user_id)` | `courses.courses (id, user_id)` (uses the existing `ux_courses_id_user_id`) |
| `fk_calendar_events_course_user` | `syllabus.calendar_events (course_id, user_id)` | `courses.courses (id, user_id)` |

I deliberately add **no** direct `user_id -> identity.users` FK: the composite course FK already forces `user_id` to equal the course owner and the user cascade reaches syllabi through `courses` (`fk_courses_user_id`). That keeps the allow-list at two additions. (Lectures added both; if the Lead Architect prefers symmetry, `fk_syllabi_user_id` and `fk_calendar_events_user_id` are harmless extra entries.) A003 adds none (the ledger deliberately has no scope FK).

`ON DELETE SET NULL (col)` FKs and the job/idempotency FKs are same-schema, so they are not in the allow-list test.

Required changes when implemented (not done now):
1. `PersistenceTests.Cross_schema_foreign_keys_are_exactly_the_allow_listed_set`: the expected list is sorted by `conname` (database collation). Insert in sorted position: `("fk_calendar_events_course_user", "syllabus", "courses", "c")` after `fk_audio_progress_session_user`, and `("fk_syllabi_course_user", "syllabus", "courses", "c")` after `fk_summary_revisions_session_user`. Update the explanatory comment ("ADR-014 ... ADR-0xx syllabus"). Because `ORDER BY con.conname` follows the DB collation, assert on a set or `OrderBy(StringComparer.Ordinal)` to avoid collation surprises.
2. `docs/architecture/backend-foundation.md`: addendum "allow-list is now seven entries" and a new ADR number for the Syllabus FKs; module table row for Syllabus (depends on Courses); migration-order rule: Identity, Courses, Lectures, Transcript, Ai, **Syllabus** (needs Courses, and Ai for runtime only).
3. `ModuleBoundaryTests` hand-maintained constants (`m1-review.md` 7.6): add `Tether.Syllabus` to the module list, the allowed project references (SharedKernel, `Tether.Courses.Contracts`, `Tether.Ai.Contracts` if the document seam is a contract), and the `IgnoreQueryFilters` allow-list (job worker, sweeper).
4. Test data: fixtures that create `syllabus.*` rows need real `courses.courses` rows first (same lesson as the earlier FK).
5. `Program.cs` migration order list: add Syllabus after Courses.

## 10. Migration plan (S-prefixed, one logical change each, all `internal`)

1. `S001_Syllabus` (EF-generated, hand-checked): schema, tables 3.1-3.9 minus raw-only objects, same-schema composite FKs, CHECKs, ordinary indexes, `sort_date` generated column (`HasComputedColumnSql(..., stored: true)`), `NULLS NOT DISTINCT` unique (`HasIndex().AreNullsDistinct(false)` if the pinned Npgsql version supports it, else raw SQL; verify). Remove any scaffolded `xmin` (none expected: revision is an int). Class `internal`.
2. `S002_SyllabusIntegrity` (raw SQL, like Lectures `M006`): the two cross-schema FKs, the `SET NULL (col)` FKs, `UNLOGGED job_inputs`, triggers (`forbid_source_update`, `stamp_orphaned`, audit append-only), `syllabus.purge_evidence(uuid)`. Requires Identity and Courses migrated first; fails by design on orphans (none can exist, tables are new).
3. `S003_DecisionAudit` may be folded into S001 (it has no FK); keep separate only if the audit retention decision slips. Prefer one fewer migration: fold it, and put its triggers in S002.
4. `A003_AiScope` in the Ai module (section 11).

`decision_audit` sketch (content-free, copy of the Lectures pattern):
```sql
CREATE TABLE syllabus.decision_audit (
  id uuid PRIMARY KEY, user_id uuid NOT NULL,
  syllabus_id uuid NOT NULL,                    -- NOT a FK: survives syllabus deletion
  item_id uuid,                                 -- NOT a FK
  item_revision integer,
  action varchar(32) NOT NULL,                  -- approved skipped undecided batch_approved synced sync_removed item_deleted syllabus_deleted_remove syllabus_deleted_keep
  occurred_at timestamptz NOT NULL,
  CONSTRAINT ck_decision_audit_action CHECK (action IN ('approved','skipped','undecided','batch_approved','synced','sync_removed','item_deleted','syllabus_deleted_remove','syllabus_deleted_keep'))
);
CREATE INDEX ix_decision_audit_user_time ON syllabus.decision_audit (user_id, occurred_at);
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
  ADD COLUMN scope_id   uuid;
UPDATE ai.ai_usage SET scope_id = session_id;                       -- backfill; table is small (cost ledger rows), single statement
ALTER TABLE ai.ai_usage ALTER COLUMN scope_id SET NOT NULL;
ALTER TABLE ai.ai_usage ALTER COLUMN session_id DROP NOT NULL;      -- syllabus rows have no session
ALTER TABLE ai.ai_usage
  ADD CONSTRAINT ck_ai_usage_scope_kind CHECK (scope_kind IN ('lecture','syllabus')),
  ADD CONSTRAINT ck_ai_usage_scope_session CHECK ((scope_kind = 'lecture' AND session_id = scope_id) OR (scope_kind = 'syllabus' AND session_id IS NULL));
ALTER TABLE ai.ai_usage DROP CONSTRAINT ck_ai_usage_workflow;       -- recreate, as A002 did for the outcome list
ALTER TABLE ai.ai_usage ADD CONSTRAINT ck_ai_usage_workflow CHECK (
     (scope_kind = 'lecture'  AND workflow IN ('RollingSummary','Compaction','LostTrack'))
  OR (scope_kind = 'syllabus' AND workflow = 'SyllabusExtract'));
CREATE INDEX ix_ai_usage_scope_created ON ai.ai_usage (user_id, scope_kind, scope_id, created_at);
```
- No new FK on `scope_id`, on purpose: same rationale as today ("deleting a lecture must not hand the student their daily budget back"): deleting a syllabus must not refund its spend either. Rows die with the user (`fk_ai_usage_user_id`). The cross-schema allow-list is unchanged by A003.
- `ix_ai_usage_user_created` already serves the per-user daily cap and the durable per-day extraction quota (count of `SyllabusExtract` rows per user per day). Per-job cap = `SUM(est_cost_micros) WHERE scope_kind='syllabus' AND scope_id = @job_or_syllabus`. Which id is the scope id (job vs syllabus) is a backend decision; I recommend the **syllabus id**, so cap and delete-cancel logic share one key, and one extraction per syllabus in this slice.
- EF: `AiUsageRow` gains `ScopeKind`/`ScopeId`; the write path dual-writes `SessionId` for lecture rows during the transition; query filter unchanged. Snapshot regenerated; class `internal`; no `xmin`.
- The gateway scope abstraction (replace `SessionId`/`ISessionDirectory`/`LectureState.Active` with scope kind + id + "still live and owned" check + cutoff lease) is backend-engineer/ai-agent-engineer's contract. The DB requirement it implies: the "still live" check for a syllabus is `EXISTS` on `syllabus.syllabi` + job `state = 'running'` read through a Syllabus contract port, never a cross-module table read.
- Reservations, reaper, `global_spend`, `catch_ups`, `summary_revisions` are unchanged.

`A004_AiScopeContract` (later, after all code reads `scope_id`): drop `session_id`, `ix_ai_usage_session_created`, `ck_ai_usage_scope_session`; keep `scope_id`. Not proposed for now.

Rollback of A003
- Safe only before any `scope_kind = 'syllabus'` row exists. `Down` must therefore start with a guard: `DO $$ BEGIN IF EXISTS (SELECT 1 FROM ai.ai_usage WHERE scope_kind <> 'lecture') THEN RAISE EXCEPTION 'A003 cannot be rolled back: syllabus-scope ledger rows exist (forward-fix instead)'; END IF; END $$;` then restore `session_id NOT NULL` (all remaining rows have it set), recreate the old workflow CHECK, drop the new CHECKs, index and columns.
- Deleting or re-labelling syllabus ledger rows to make Down succeed is **not allowed**: it would erase spend evidence and refund the budget.
- Deploy order: A003 applied and the dual-writing Ai code deployed **before** the Syllabus job starts writing scope rows. Old Ai code against the new table keeps working during the window (`scope_kind` defaults to `lecture`, `scope_id` NOT NULL has no default, so old code that omits it would fail: write `scope_id` in the same release as A003, or give `scope_id` a temporary default expression via a trigger `scope_id := session_id`. Open question 12.)

## 12. Security, privacy, failure paths

- Data minimisation: originals never persisted (only transient unlogged bytes); no raw model output stored (`item_proposals` holds validated structure); no prompts/completions anywhere; the ledger stays content-free. Titles/notes/snippet offsets are document-derived and follow the syllabus lifecycle.
- Canonical text is stored **unredacted** (the redactor runs on model input only; design V3 needs the original offsets). It can contain instructor names, emails and phone numbers. Security-engineer to confirm that is acceptable at rest, and whether column-level encryption or only volume encryption is expected. Not in logs, not in error `detail`.
- Untrusted text: length CHECKs on `title`/`notes`; no markdown/HTML interpretation at the DB. `title` pattern restrictions from the contract (`[^\u0000-...]`) belong in the API validator; I would add `title !~ '[\x00-\x1f]'` as a CHECK only after reading the exact contract pattern.
- Failure modes covered by schema: concurrent delete vs job (FK), duplicate sync (unique), double create (unique), approve of undated item (CHECK), cross-user references (composite FKs), stuck leases (reaper index), unlogged-table truncation after crash (job fails calmly, student re-uploads), orphaned event bookkeeping (trigger + CHECK, to be verified).
- Backups: deletes are hard, so a restore from a backup taken before a deletion resurrects the deleted syllabus until the application re-applies it. There is no tombstone table in the same database that survives a restore (it would be restored too). Mitigation for the restore runbook: re-run deletions from the content-free application log of `syllabus_deleted_*` audit events exported outside the database. Needs devops and security to agree; recorded as a known limitation, not solved here. `--no-unlogged-table-data` for dumps.
- Not stored anywhere in this module: OAuth tokens (none; mock provider), student calendar contents, filenames (only `file_type`).

## 13. Open questions

1. **Offset unit.** The contract says offsets index the stored canonical text but not the unit. .NET `string` offsets are UTF-16 code units; PostgreSQL `substr` counts code points. They diverge on emoji/astral characters. Recommend: contract states UTF-16 code units (or canonicalisation replaces astral characters), and all slicing is in .NET. Needs backend-engineer + contract owner.
2. **Transient upload bytes**: unlogged table (recommended), memory, or temp file. Security-engineer decision.
3. **Idempotent replay body**: current representation of the same resource (proposed) vs a stored original body. Contract wording is "original result".
4. **`calendarEvents=keep`** (contract Q2): keep indefinitely, TTL, or remove the option from v1. Schema supports all.
5. **Retention of ready syllabi and decision audit**: P2-D4 / PO-5 / counsel (G1). `purge_after` stays NULL until decided.
6. **Account deletion** must call `syllabus.purge_evidence` and `lectures.purge_evidence`; who owns that job (no account-deletion job exists yet, `m1-review.md` section 1).
7. **`confidenceReason`**: store the fixed-vocabulary text (as drafted) or a code mapped to text at the API (better for localisation and for tests, needs a contract decision).
8. **Unclaimed dates**: stored (proposed) vs recomputed on read.
9. **Course archive** vs demo calendar visibility (query-level).
10. **Wire id format**: the contract says prefixed opaque ids (`syl_`, `itm_`, ...) and `itemKey` hashes the wire ids; DB ids are uuid. Is there an existing codec for `ses_`/`crs_` ids, or are those just uuid strings today? I found no codec in `src/api`. A codec must be stable and reversible, and `item_key` is stored so it survives codec changes.
11. **Contract gaps to confirm**: `not_applicable` item field nullity (policy); student items always `resolved`; whether `needs_date` items can be `please_check` only; item `notes` max 500 and `title` max 200 as drafted in `syllabus-common`.
12. **A003 transition**: a trigger default for `scope_id` vs same-release code change (section 11).
13. **Where job queue lives**: `syllabus.extraction_jobs` (proposed) vs a shared `ai.jobs` named in `backend-foundation.md` section 8 (no such table exists in A001/A002).
14. Product limits that are only app-enforced today (5/50 syllabi, 300 items, 60 occurrences): confirm none should become DB triggers.
15. RLS decision (carried from `m1-review.md`) still open.

## 14. Verification plan (all NOT RUN; nothing exists to run)

Method: Testcontainers or the compose Postgres 16 with real migrations, never EF in-memory; every constraint test is an INSERT/UPDATE that must raise the named constraint. Scratch DBs created and dropped by the test, never `college_companion_dev`.

DDL and constraints
- D-01 Table-driven negative tests, one per CHECK in 3.1, 3.4, 3.9 (needs_date approved, policy approved, `sync_state` without approved, originalRetained true, provider `google`, mock source without demo, all-day with time, end before start, student item with snippet, AI item without snippet, bad flag value, 4 alternatives, recurrence missing days).
- D-02 Migration up/down on an empty DB and on a DB with data: Identity -> Courses -> Lectures -> Transcript -> Ai(A003) -> Syllabus up; reverse down; `pending migrations` empty; no table left except history tables.
- D-03 Re-scaffold probe yields an empty migration; no `public` migration types; no `xmin`.
- D-04 `pg_constraint` allow-list test updated (section 9) and green; also assert exactly which FKs use `SET NULL` (`confdeltype = 'n'`) in the syllabus schema.
- D-05 Immutability: UPDATE on `canonical_texts` and `item_proposals` raises 55000; DELETE via cascade succeeds. Append-only on `decision_audit`: UPDATE/DELETE/TRUNCATE raise; `syllabus.purge_evidence(user)` removes only that user's rows.
- D-06 `NULLS NOT DISTINCT` unique: two non-recurring events for one `item_key` fail; recurrence rows with different `occurrence_date` coexist; same `item_key` for two users impossible by construction (assert).
- D-07 **Orphan stamping**: delete syllabus with `keep` and assert event rows have `syllabus_id NULL`, `orphaned_at NOT NULL`, snapshot intact. This is the unverified trigger/RI interaction; if it fails, use the documented fallback.

Isolation
- I-01 Two-user fixture: composite FKs reject an item under B's syllabus, an alternative under B's item, an event for B's course, a job for B's syllabus (assert SQLSTATE 23503).
- I-02 Every `DbSet` returns zero cross-user rows through the EF filter; `IgnoreQueryFilters` appears only in the two named classes (architecture test).
- I-03 HTTP: foreign or missing id returns 404 for all 19 routes (contract section 12 "ownership 404 for every route").

Deletion and recovery
- X-01 Delete with `remove`: zero rows left for the syllabus in every table in section 2, including events; audit row `syllabus_deleted_remove` remains.
- X-02 Delete with `keep`: events remain, children gone; course purge afterwards removes the orphans; user hard-delete leaves zero rows in all schemas except `decision_audit` until `purge_evidence` runs, then zero.
- X-03 Delete during a running job (inject a pause between stages): the worker's next write fails 23503 / zero-row update and writes nothing; no resurrected rows (extends C-21). Same with course purge.
- X-04 Idempotency: sync x3 sequentially and x20 concurrently yields exactly one event per item and the first call's outcomes are `created`, later `unchanged`; edit then sync yields `updated`; withdraw removes the event in the same transaction and a forced failure rolls back both; remove then re-add yields exactly one.
- X-05 `Idempotency-Key`: replay returns the original result and sets the replay header; same key + different body is 400; same key by another user is independent; two concurrent identical creates produce one syllabus and one job.
- X-06 One extraction in flight: a second queued/running job for the same user violates `ux_extraction_jobs_one_in_flight`; after the first finishes the second is allowed.
- X-07 Backup/restore drill (devops): `pg_dump --no-unlogged-table-data`, restore into scratch DB, compare row counts for all syllabus tables, confirm `job_inputs` empty, confirm the documented "deleted-after-backup resurrects" limitation and the re-apply procedure.
- X-08 A003: backfill equals `session_id` for every existing row; old and new workflows pass the CHECK; `Down` is rejected when a syllabus-scope row exists (negative test) and succeeds when none; sums per user/day unchanged before and after.

Data integrity of content
- C-01 For every `origin='ai'` item (fixtures S1-S4 with astral characters added): `0 <= start < end <= utf16_length` and the sliced text equals the stored fixture snippet; `exact` snippets equal the verbatim line span.
- C-02 No document text, titles, snippets or notes appear in logs, `last_error_code`, or `ai_usage` (log canary extended to this module, contract section 12).
- C-03 Keyset pagination: 300 items with 40 `needs_date`, pages of 50 by `(sort_date, id)`: no gaps, no duplicates, needs-date last, stable under concurrent insert.

Performance (measure, do not assume; targets are proposals)
- P-01 Seed 200 users x 50 syllabi x 200 items: `EXPLAIN (ANALYZE, BUFFERS)` of items keyset page, syllabi list, demo-calendar range read, sync upsert of 100 items (up to 60 occurrences each is 6,000 rows, so sync must batch), syllabus DELETE cascade. Expect index scans on `ix_items_syllabus_sort`, `ix_syllabi_user_course_created`, `ix_calendar_events_course_start`. Target p95 under 20 ms for the three reads (same bar as `schema-m1-m2.md` section 8), cascade delete of a 300-item syllabus under 200 ms. Record actuals; none exist yet.

## 15. Not done

No DbContext, entity, migration, test, seed, or edit to `backend-foundation.md` / `PersistenceTests.cs` / `ModuleBoundaryTests.cs`. No SQL executed. The orphan trigger/`SET NULL (col)`/`NULLS NOT DISTINCT`/generated-column interactions are PostgreSQL features I expect to behave as described but have not run. No gate is affected; this is a design for the Lead Architect's review, with security-engineer review needed for sections 3.3, 8, 12 before implementation.
