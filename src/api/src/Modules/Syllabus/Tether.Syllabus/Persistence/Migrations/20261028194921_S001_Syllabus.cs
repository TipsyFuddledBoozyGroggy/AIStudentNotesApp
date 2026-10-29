using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Syllabus.Persistence.Migrations
{
    /// <summary>
    /// S001: the Syllabus tables, copied from docs/data/schema-syllabus-p2.md revision 3 (sections 3.1 to 3.9 and 10), as raw SQL because
    /// EF cannot express most of the constraints (composite FKs on non-key columns, partial and NULLS NOT DISTINCT indexes, array and
    /// jsonb CHECKs, stored generated column). The EF model/snapshot maps the same tables and columns.
    /// Same-schema FKs only; the cross-schema FKs, triggers and functions are S002. Down is dev-only (drops the tables, not the schema,
    /// because the schema hosts its own history table).
    /// </summary>
    internal partial class S001_Syllabus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "syllabus");
            migrationBuilder.Sql("""
                CREATE TABLE syllabus.syllabi (
                  id                uuid PRIMARY KEY,
                  user_id           uuid NOT NULL,
                  course_id         uuid NOT NULL,
                  title             varchar(200),
                  input_kind        varchar(8)  NOT NULL,
                  file_type         varchar(4),
                  status            varchar(12) NOT NULL,
                  status_reason     varchar(24),
                  ai_source         varchar(5)  NOT NULL,
                  demo_source       boolean     NOT NULL,
                  original_retained boolean     NOT NULL DEFAULT false,
                  content_sha256    char(64)    NOT NULL,
                  page_count        smallint,
                  char_count        integer     NOT NULL DEFAULT 0,
                  unreadable_pages  smallint[]  NOT NULL DEFAULT '{}',
                  hidden_content_removed integer NOT NULL DEFAULT 0,
                  hidden_content_dropped boolean NOT NULL DEFAULT false,
                  injection_suspect boolean     NOT NULL DEFAULT false,
                  batch_approve_allowed boolean NOT NULL DEFAULT true,
                  notice_version    varchar(64) NOT NULL,
                  notice_attested   boolean     NOT NULL,
                  notice_attested_at timestamptz NOT NULL,
                  unprocessed_ranges jsonb      NOT NULL DEFAULT '[]',
                  dropped_invalid   integer     NOT NULL DEFAULT 0,
                  term_start        date,
                  term_end          date,
                  default_timezone  varchar(64) NOT NULL,
                  extractor_version varchar(32),
                  prompt_version    varchar(64),
                  schema_version    varchar(32),
                  model             varchar(64),
                  row_version       integer     NOT NULL DEFAULT 1,
                  created_at        timestamptz NOT NULL,
                  updated_at        timestamptz NOT NULL,
                  last_activity_at  timestamptz NOT NULL,
                  canonical_text_purge_at timestamptz,
                  canonical_text_purged_at timestamptz,
                  purge_after       timestamptz,
                  CONSTRAINT ux_syllabi_id_user_id UNIQUE (id, user_id),
                  CONSTRAINT ux_syllabi_id_user_course UNIQUE (id, user_id, course_id),
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
                  CONSTRAINT ck_syllabi_mock_is_demo CHECK (ai_source <> 'mock' OR demo_source),
                  CONSTRAINT ck_syllabi_original_not_retained CHECK (original_retained = false),
                  CONSTRAINT ck_syllabi_sha CHECK (content_sha256 ~ '^[0-9a-f]{64}$'),
                  CONSTRAINT ck_syllabi_pages CHECK (page_count IS NULL OR page_count BETWEEN 1 AND 60),
                  CONSTRAINT ck_syllabi_chars CHECK (char_count BETWEEN 0 AND 50000),
                  CONSTRAINT ck_syllabi_counts CHECK (hidden_content_removed >= 0 AND dropped_invalid >= 0),
                  CONSTRAINT ck_syllabi_unprocessed_json CHECK (jsonb_typeof(unprocessed_ranges) = 'array' AND jsonb_array_length(unprocessed_ranges) <= 60),
                  CONSTRAINT ck_syllabi_term CHECK (term_start IS NULL OR term_end IS NULL OR term_end >= term_start),
                  CONSTRAINT ck_syllabi_row_version CHECK (row_version >= 1),
                  CONSTRAINT ck_syllabi_notice CHECK (notice_attested = true AND char_length(notice_version) BETWEEN 1 AND 64 AND notice_attested_at = created_at),
                  CONSTRAINT ck_syllabi_hidden CHECK ((hidden_content_removed >= 1) = hidden_content_dropped),
                  CONSTRAINT ck_syllabi_batch CHECK (NOT batch_approve_allowed OR (NOT injection_suspect AND NOT hidden_content_dropped AND canonical_text_purged_at IS NULL)),
                  CONSTRAINT ck_syllabi_retention CHECK (
                     (canonical_text_purged_at IS NULL AND canonical_text_purge_at IS NOT NULL AND canonical_text_purge_at <= created_at + interval '365 days')
                     OR (canonical_text_purged_at IS NOT NULL AND canonical_text_purge_at IS NULL)),
                  CONSTRAINT ck_syllabi_activity CHECK (last_activity_at >= created_at)
                );
                CREATE INDEX ix_syllabi_user_course_created ON syllabus.syllabi (user_id, course_id, id DESC);
                CREATE INDEX ix_syllabi_dup ON syllabus.syllabi (user_id, course_id, content_sha256);
                CREATE INDEX ix_syllabi_purge_after ON syllabus.syllabi (purge_after) WHERE purge_after IS NOT NULL;
                CREATE INDEX ix_syllabi_text_purge_due ON syllabus.syllabi (canonical_text_purge_at) WHERE canonical_text_purged_at IS NULL;

                CREATE TABLE syllabus.canonical_texts (
                  syllabus_id       uuid PRIMARY KEY,
                  user_id           uuid NOT NULL,
                  text              text NOT NULL,
                  utf16_length      integer NOT NULL,
                  lines             jsonb NOT NULL,
                  pages             jsonb NOT NULL,
                  extractor_version varchar(32) NOT NULL,
                  text_sha256       char(64) NOT NULL,
                  created_at        timestamptz NOT NULL,
                  CONSTRAINT fk_canonical_texts_syllabus_user FOREIGN KEY (syllabus_id, user_id)
                     REFERENCES syllabus.syllabi (id, user_id) ON DELETE CASCADE,
                  CONSTRAINT ck_canonical_texts_len CHECK (utf16_length BETWEEN 1 AND 100000 AND char_length(text) BETWEEN 1 AND 50000 AND char_length(text) <= utf16_length),
                  CONSTRAINT ck_canonical_texts_json CHECK (jsonb_typeof(lines) = 'array' AND jsonb_typeof(pages) = 'array')
                );

                CREATE TABLE syllabus.extraction_jobs (
                  id               uuid PRIMARY KEY,
                  user_id          uuid NOT NULL,
                  syllabus_id      uuid NOT NULL,
                  state            varchar(10) NOT NULL,
                  attempt          smallint NOT NULL DEFAULT 0,
                  available_at     timestamptz NOT NULL,
                  lease_owner      varchar(64),
                  lease_expires_at timestamptz,
                  heartbeat_at     timestamptz,
                  stage            varchar(12),
                  chunks_total     smallint,
                  chunks_done      smallint,
                  job_cap_micros   bigint NOT NULL,
                  last_error_code  varchar(40),
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
                CREATE UNIQUE INDEX ux_extraction_jobs_one_in_flight ON syllabus.extraction_jobs (user_id) WHERE state IN ('queued','running');
                CREATE INDEX ix_extraction_jobs_claim ON syllabus.extraction_jobs (available_at) WHERE state = 'queued';
                CREATE INDEX ix_extraction_jobs_lease ON syllabus.extraction_jobs (lease_expires_at) WHERE state = 'running';
                CREATE INDEX ix_extraction_jobs_finished ON syllabus.extraction_jobs (finished_at) WHERE finished_at IS NOT NULL;
                CREATE INDEX ix_extraction_jobs_syllabus ON syllabus.extraction_jobs (syllabus_id);

                CREATE TABLE syllabus.items (
                  id            uuid PRIMARY KEY,
                  user_id       uuid NOT NULL,
                  syllabus_id   uuid NOT NULL,
                  course_id     uuid NOT NULL,
                  item_key      char(35) NOT NULL,
                  origin        varchar(7)  NOT NULL,
                  kind          varchar(12) NOT NULL,
                  title         varchar(200) NOT NULL,
                  notes         varchar(500),
                  written_date_text varchar(120),
                  written_time_text varchar(60),
                  written_year  smallint, written_month smallint, written_day smallint,
                  written_weekday char(2),
                  when_status   varchar(14) NOT NULL,
                  date_local    date,
                  end_date_local date,
                  time_local    time(0),
                  end_time_local time(0),
                  all_day       boolean NOT NULL DEFAULT false,
                  timezone      varchar(64),
                  timezone_source varchar(14),
                  derivation    varchar(20),
                  unknown_reason varchar(24),
                  rec_by_day    text[],
                  rec_start_time time(0), rec_end_time time(0),
                  rec_from      date, rec_until date,
                  rec_until_source varchar(8),
                  rec_exceptions date[] NOT NULL DEFAULT '{}',
                  confidence    varchar(14) NOT NULL,
                  confidence_reason varchar(200) NOT NULL,
                  flags         text[] NOT NULL DEFAULT '{}',
                  snippet_start integer, snippet_end integer, snippet_page smallint, snippet_precision varchar(5),
                  snippet_text    varchar(600),
                  context_text    varchar(1500),
                  context_start   integer, context_end integer,
                  edited        boolean NOT NULL DEFAULT false,
                  decision      varchar(9)  NOT NULL DEFAULT 'undecided',
                  sync_state    varchar(18) NOT NULL DEFAULT 'not_synced',
                  synced_revision integer,
                  revision      integer NOT NULL DEFAULT 1,
                  row_version   integer NOT NULL DEFAULT 1,
                  created_at    timestamptz NOT NULL,
                  updated_at    timestamptz NOT NULL,
                  sort_date     date GENERATED ALWAYS AS (COALESCE(date_local, DATE '9999-12-31')) STORED,
                  CONSTRAINT ux_items_id_user_id UNIQUE (id, user_id),
                  CONSTRAINT ux_items_item_key UNIQUE (item_key),
                  CONSTRAINT fk_items_syllabus_user_course FOREIGN KEY (syllabus_id, user_id, course_id)
                     REFERENCES syllabus.syllabi (id, user_id, course_id) ON DELETE CASCADE,
                  CONSTRAINT ck_items_origin CHECK (origin IN ('ai','student')),
                  CONSTRAINT ck_items_kind CHECK (kind IN ('assignment','exam','reading','office_hours','policy','other')),
                  CONSTRAINT ck_items_when_status CHECK (when_status IN ('resolved','needs_date','not_applicable')),
                  CONSTRAINT ck_items_tz_source CHECK (timezone_source IS NULL OR timezone_source IN ('stated_in_source','course_default','student')),
                  CONSTRAINT ck_items_derivation CHECK (derivation IS NULL OR derivation IN ('explicit_full','year_from_term','relative_to_anchor','student_entered')),
                  CONSTRAINT ck_items_unknown_reason CHECK (unknown_reason IS NULL OR unknown_reason IN
                     ('not_stated','tba','relative_unresolvable','conflicting','ambiguous_format','table_unreadable','date_not_verifiable')),
                  CONSTRAINT ck_items_confidence CHECK (confidence IN ('looks_right','probably_right','please_check')),
                  CONSTRAINT ck_items_flags CHECK (flags <@ ARRAY['year_inferred','relative_date','timezone_assumed','time_missing','conflicting_sources',
                     'tentative_language','table_layout_risk','section_ambiguous','range_end_inferred','weekday_mismatch','date_not_verifiable',
                     'until_from_term','low_text_quality','duplicate_candidate','outside_term','dst_ambiguous','injection_suspect','supersession_cue']::text[] AND cardinality(flags) <= 18),
                  CONSTRAINT ck_items_flags_force_check CHECK (when_status <> 'resolved' OR confidence = 'please_check' OR NOT (flags && ARRAY['tentative_language','weekday_mismatch','table_layout_risk','outside_term','injection_suspect','supersession_cue','low_text_quality']::text[])),
                  CONSTRAINT ck_items_student_never_looks_right CHECK (derivation IS DISTINCT FROM 'student_entered' OR confidence <> 'looks_right'),
                  CONSTRAINT ck_items_decision CHECK (decision IN ('undecided','approved','skipped')),
                  CONSTRAINT ck_items_sync_state CHECK (sync_state IN ('not_synced','synced','changed_since_sync','sync_failed')),
                  CONSTRAINT ck_items_item_key_shape CHECK (item_key ~ '^ik_[A-Za-z0-9_-]{32}$'),
                  CONSTRAINT ck_items_no_approve_without_date CHECK (when_status = 'resolved' OR decision <> 'approved'),
                  CONSTRAINT ck_items_sync_requires_approved CHECK (
                     (sync_state = 'not_synced' AND synced_revision IS NULL)
                     OR (sync_state IN ('synced','changed_since_sync') AND decision = 'approved' AND synced_revision IS NOT NULL)
                     OR (sync_state = 'sync_failed' AND decision = 'approved')),
                  CONSTRAINT ck_items_policy_iff_not_applicable CHECK ((kind = 'policy') = (when_status = 'not_applicable')),
                  CONSTRAINT ck_items_student_rules CHECK (origin = 'ai' OR (kind <> 'policy' AND when_status = 'resolved' AND snippet_start IS NULL)),
                  CONSTRAINT ck_items_resolved_fields CHECK (when_status <> 'resolved' OR (date_local IS NOT NULL AND timezone IS NOT NULL AND derivation IS NOT NULL AND unknown_reason IS NULL)),
                  CONSTRAINT ck_items_needs_date_fields CHECK (when_status <> 'needs_date' OR
                     (date_local IS NULL AND end_date_local IS NULL AND time_local IS NULL AND end_time_local IS NULL AND derivation IS NULL AND NOT all_day AND unknown_reason IS NOT NULL)),
                  CONSTRAINT ck_items_not_applicable_fields CHECK (when_status <> 'not_applicable' OR
                     (date_local IS NULL AND end_date_local IS NULL AND time_local IS NULL AND end_time_local IS NULL AND derivation IS NULL AND NOT all_day AND unknown_reason IS NULL AND rec_by_day IS NULL)),
                  CONSTRAINT ck_items_tz_pair CHECK ((timezone IS NULL) = (timezone_source IS NULL)),
                  CONSTRAINT ck_items_all_day CHECK (NOT all_day OR (time_local IS NULL AND end_time_local IS NULL)),
                  CONSTRAINT ck_items_end_time CHECK (end_time_local IS NULL OR time_local IS NOT NULL),
                  CONSTRAINT ck_items_end_date CHECK (end_date_local IS NULL OR (date_local IS NOT NULL AND end_date_local >= date_local)),
                  CONSTRAINT ck_items_written_parts CHECK ((written_month IS NULL OR written_month BETWEEN 1 AND 12) AND (written_day IS NULL OR written_day BETWEEN 1 AND 31)
                     AND (written_year IS NULL OR written_year BETWEEN 1900 AND 2200) AND (written_weekday IS NULL OR written_weekday IN ('MO','TU','WE','TH','FR','SA','SU'))),
                  CONSTRAINT ck_items_snippet CHECK (
                     (origin = 'ai' AND snippet_start IS NOT NULL AND snippet_end IS NOT NULL AND snippet_precision IN ('exact','line')
                        AND snippet_start >= 0 AND snippet_end > snippet_start AND snippet_end - snippet_start <= 1200)
                     OR (origin = 'student' AND snippet_start IS NULL AND snippet_end IS NULL AND snippet_page IS NULL AND snippet_precision IS NULL
                         AND snippet_text IS NULL AND context_text IS NULL AND context_start IS NULL AND context_end IS NULL)),
                  CONSTRAINT ck_items_snippet_text CHECK (snippet_text IS NULL OR char_length(snippet_text) BETWEEN 1 AND 600),
                  CONSTRAINT ck_items_context CHECK (
                     (context_text IS NULL AND context_start IS NULL AND context_end IS NULL)
                     OR (context_text IS NOT NULL AND snippet_text IS NOT NULL AND char_length(context_text) BETWEEN 1 AND 1500
                         AND context_start IS NOT NULL AND context_end IS NOT NULL AND context_start >= 0 AND context_start <= snippet_start
                         AND snippet_end <= context_end AND context_end - context_start <= 3000)),
                  CONSTRAINT ck_items_recurrence CHECK (
                     (rec_by_day IS NULL AND rec_start_time IS NULL AND rec_end_time IS NULL AND rec_from IS NULL AND rec_until IS NULL AND rec_until_source IS NULL AND cardinality(rec_exceptions) = 0)
                     OR (rec_by_day IS NOT NULL AND cardinality(rec_by_day) BETWEEN 1 AND 7 AND rec_by_day <@ ARRAY['MO','TU','WE','TH','FR','SA','SU']::text[]
                         AND rec_start_time IS NOT NULL AND rec_from IS NOT NULL AND rec_until IS NOT NULL AND rec_until >= rec_from
                         AND rec_until_source IN ('stated','term_end','student') AND cardinality(rec_exceptions) <= 20 AND kind <> 'policy')),
                  CONSTRAINT ck_items_text_len CHECK (char_length(title) >= 1),
                  CONSTRAINT ck_items_revision CHECK (revision >= 1 AND (synced_revision IS NULL OR synced_revision BETWEEN 1 AND revision) AND revision <= 2147483647),
                  CONSTRAINT ck_items_row_version CHECK (row_version >= revision)
                );
                CREATE INDEX ix_items_syllabus_sort ON syllabus.items (user_id, syllabus_id, sort_date, id);
                CREATE INDEX ix_items_course_date ON syllabus.items (user_id, course_id, date_local) WHERE date_local IS NOT NULL;

                CREATE TABLE syllabus.item_proposals (
                  item_id         uuid PRIMARY KEY,
                  user_id         uuid NOT NULL,
                  syllabus_id     uuid NOT NULL,
                  proposal        jsonb NOT NULL,
                  validator_version varchar(32) NOT NULL,
                  prompt_version  varchar(64)  NOT NULL,
                  schema_version  varchar(32)  NOT NULL,
                  model           varchar(64)  NOT NULL,
                  created_at      timestamptz  NOT NULL,
                  CONSTRAINT fk_item_proposals_item_user FOREIGN KEY (item_id, user_id) REFERENCES syllabus.items (id, user_id) ON DELETE CASCADE,
                  CONSTRAINT ck_item_proposals_json CHECK (jsonb_typeof(proposal) = 'object')
                );
                CREATE INDEX ix_item_proposals_syllabus ON syllabus.item_proposals (syllabus_id);

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

                CREATE TABLE syllabus.unclaimed_dates (
                  id uuid PRIMARY KEY, user_id uuid NOT NULL, syllabus_id uuid NOT NULL,
                  snippet_start integer NOT NULL, snippet_end integer NOT NULL, snippet_page smallint, snippet_precision varchar(5) NOT NULL,
                  snippet_text varchar(600), context_text varchar(1500), context_start integer, context_end integer,
                  created_at timestamptz NOT NULL,
                  CONSTRAINT fk_unclaimed_dates_syllabus_user FOREIGN KEY (syllabus_id, user_id) REFERENCES syllabus.syllabi (id, user_id) ON DELETE CASCADE,
                  CONSTRAINT ck_unclaimed_dates_span CHECK (snippet_start >= 0 AND snippet_end > snippet_start AND snippet_end - snippet_start <= 1200 AND snippet_precision IN ('exact','line')),
                  CONSTRAINT ck_unclaimed_dates_text CHECK ((snippet_text IS NULL OR char_length(snippet_text) BETWEEN 1 AND 600)
                     AND ((context_text IS NULL AND context_start IS NULL AND context_end IS NULL)
                          OR (context_text IS NOT NULL AND snippet_text IS NOT NULL AND char_length(context_text) BETWEEN 1 AND 1500
                              AND context_start BETWEEN 0 AND snippet_start AND context_end >= snippet_end AND context_end - context_start <= 3000)))
                );
                CREATE INDEX ix_unclaimed_dates_syllabus ON syllabus.unclaimed_dates (user_id, syllabus_id, snippet_start, id);

                CREATE TABLE syllabus.sync_runs (
                  id uuid PRIMARY KEY, user_id uuid NOT NULL, syllabus_id uuid NOT NULL,
                  provider varchar(4) NOT NULL,
                  kind varchar(6) NOT NULL,
                  status varchar(15) NOT NULL,
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
                  outcome varchar(14) NOT NULL,
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
                  course_id uuid,
                  syllabus_id uuid,
                  method varchar(6) NOT NULL,
                  route_template varchar(64) NOT NULL,
                  key_hash bytea NOT NULL,
                  fingerprint bytea NOT NULL,
                  result_kind varchar(12) NOT NULL,
                  result_id uuid,
                  tombstoned_at timestamptz,
                  created_at timestamptz NOT NULL,
                  expires_at timestamptz NOT NULL,
                  CONSTRAINT fk_idempotency_keys_syllabus_user FOREIGN KEY (syllabus_id, user_id) REFERENCES syllabus.syllabi (id, user_id) ON DELETE CASCADE,
                  CONSTRAINT ck_idempotency_keys_route CHECK (method = 'POST' AND route_template IN ('POST /courses/{courseId}/syllabi','POST /syllabi/{id}/items','POST /syllabi/{id}/calendar-sync')),
                  CONSTRAINT ck_idempotency_keys_result CHECK (result_kind IN ('syllabus','item','sync_run')),
                  CONSTRAINT ck_idempotency_keys_shape CHECK (
                     (result_kind = 'syllabus' AND syllabus_id IS NULL AND tombstoned_at IS NULL     AND course_id IS NOT NULL AND result_id IS NOT NULL)
                  OR (result_kind = 'syllabus' AND syllabus_id IS NULL AND tombstoned_at IS NOT NULL AND course_id IS NULL     AND result_id IS NULL)
                  OR (result_kind <> 'syllabus' AND syllabus_id IS NOT NULL AND tombstoned_at IS NULL AND course_id IS NOT NULL AND result_id IS NOT NULL)),
                  CONSTRAINT ck_idempotency_keys_hash CHECK (octet_length(key_hash) = 32 AND octet_length(fingerprint) = 32)
                );
                CREATE UNIQUE INDEX ux_idempotency_keys_scope ON syllabus.idempotency_keys (user_id, method, route_template, key_hash);
                CREATE INDEX ix_idempotency_keys_expires ON syllabus.idempotency_keys (expires_at);
                CREATE INDEX ix_idempotency_keys_result ON syllabus.idempotency_keys (result_id) WHERE result_kind = 'syllabus' AND tombstoned_at IS NULL;

                CREATE TABLE syllabus.calendar_events (
                  id              uuid PRIMARY KEY,
                  user_id         uuid NOT NULL,
                  course_id       uuid NOT NULL,
                  syllabus_id     uuid NOT NULL,
                  item_id         uuid NOT NULL,
                  provider        varchar(4) NOT NULL DEFAULT 'mock',
                  item_key        char(35) NOT NULL,
                  occurrence_date date,
                  title           varchar(200) NOT NULL,
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
                  CONSTRAINT ck_calendar_events_provider CHECK (provider = 'mock'),
                  CONSTRAINT ck_calendar_events_kind CHECK (kind IN ('assignment','exam','reading','office_hours','other')),
                  CONSTRAINT ck_calendar_events_item_key CHECK (item_key ~ '^ik_[A-Za-z0-9_-]{32}$'),
                  CONSTRAINT ck_calendar_events_all_day CHECK (NOT all_day OR (start_time IS NULL AND end_time IS NULL)),
                  CONSTRAINT ck_calendar_events_times CHECK (all_day OR start_time IS NOT NULL),
                  CONSTRAINT ck_calendar_events_end CHECK (end_date > start_date OR (end_date = start_date AND (end_time IS NULL OR start_time IS NULL OR end_time >= start_time))),
                  CONSTRAINT ck_calendar_events_rev CHECK (synced_revision >= 1),
                  CONSTRAINT ux_calendar_events_idem UNIQUE NULLS NOT DISTINCT (user_id, provider, item_key, occurrence_date),
                  CONSTRAINT ux_calendar_events_id_user UNIQUE (id, user_id),
                  CONSTRAINT fk_calendar_events_syllabus_user FOREIGN KEY (syllabus_id, user_id, course_id)
                     REFERENCES syllabus.syllabi (id, user_id, course_id) ON DELETE CASCADE,
                  CONSTRAINT fk_calendar_events_item_user FOREIGN KEY (item_id, user_id)
                     REFERENCES syllabus.items (id, user_id) ON DELETE CASCADE
                );
                CREATE INDEX ix_calendar_events_course_start ON syllabus.calendar_events (user_id, course_id, start_date, id);
                CREATE INDEX ix_calendar_events_syllabus ON syllabus.calendar_events (syllabus_id);
                CREATE INDEX ix_calendar_events_item ON syllabus.calendar_events (item_id);

                CREATE TABLE syllabus.decision_audit (
                  id uuid PRIMARY KEY, user_id uuid NOT NULL,
                  syllabus_id uuid NOT NULL,
                  item_id uuid,
                  item_revision integer,
                  content_hmac bytea,
                  notice_version varchar(64),
                  item_count integer,
                  action varchar(32) NOT NULL,
                  occurred_at timestamptz NOT NULL,
                  CONSTRAINT ck_decision_audit_action CHECK (action IN ('syllabus_created','approved','skipped','undecided','batch_approved','synced','sync_removed','item_deleted','syllabus_deleted','exported')),
                  CONSTRAINT ck_decision_audit_hmac CHECK (content_hmac IS NULL OR (action = 'syllabus_created' AND octet_length(content_hmac) = 32))
                );
                CREATE INDEX ix_decision_audit_user_time ON syllabus.decision_audit (user_id, occurred_at);

                CREATE TABLE syllabus.creation_counters (
                  user_id uuid NOT NULL, day date NOT NULL,
                  created smallint NOT NULL,
                  PRIMARY KEY (user_id, day),
                  CONSTRAINT ck_creation_counters CHECK (created BETWEEN 1 AND 10)
                );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS syllabus.creation_counters, syllabus.decision_audit, syllabus.calendar_events, syllabus.idempotency_keys,
                    syllabus.sync_run_items, syllabus.sync_runs, syllabus.unclaimed_dates, syllabus.item_alternatives, syllabus.item_proposals,
                    syllabus.items, syllabus.extraction_jobs, syllabus.canonical_texts, syllabus.syllabi;
                """);
        }
    }
}
