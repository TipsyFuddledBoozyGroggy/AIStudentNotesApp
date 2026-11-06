using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tether.Docs.Persistence.Migrations
{
    /// <summary>
    /// D001 (hand-written raw SQL, like Syllabus S001/S002): the "docs" schema for the Demo Doc export (docs.export.v1, mock only).
    /// Five tables, all with user_id. Three of them (demo_docs, doc_exports, doc_targets) reference lectures.lecture_sessions through the
    /// COMPOSITE FK (session_id, user_id) ON DELETE CASCADE, so deleting a lecture or purging its course removes them in the same
    /// transaction. idempotency_keys has no session_id: it cascades through the composite FK (export_id, user_id) to doc_exports (DXD-09).
    /// audit_events has no session FK on purpose (it outlives the lecture, content free) and an identity FK so full account erasure removes it
    /// (PO-20aw; retention after a lecture delete is the open PO-20ca/L-14 question). No RESTRICT anywhere between docs tables.
    /// Requires Identity and Lectures migrated first. Down is dev-only.
    /// </summary>
    [DbContext(typeof(DocsDbContext))]
    [Migration("20261105120000_D001_Docs")]
    internal partial class D001_Docs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE SCHEMA IF NOT EXISTS docs;

                CREATE TABLE docs.demo_docs (
                    id               uuid        NOT NULL PRIMARY KEY,
                    user_id          uuid        NOT NULL,
                    session_id       uuid        NOT NULL,
                    title            varchar(200) NOT NULL,
                    blocks           jsonb       NOT NULL,
                    version          integer     NOT NULL,
                    created_at       timestamptz NOT NULL,
                    updated_at       timestamptz NOT NULL,
                    origin_export_id uuid        NOT NULL,
                    CONSTRAINT ck_demo_docs_version CHECK (version >= 1),
                    CONSTRAINT ck_demo_docs_blocks CHECK (jsonb_typeof(blocks) = 'array' AND jsonb_array_length(blocks) BETWEEN 1 AND 400),
                    CONSTRAINT fk_demo_docs_session_user FOREIGN KEY (session_id, user_id) REFERENCES lectures.lecture_sessions (id, user_id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX ux_demo_docs_origin_export_id ON docs.demo_docs (origin_export_id);
                CREATE UNIQUE INDEX ux_demo_docs_id_user_id ON docs.demo_docs (id, user_id);
                CREATE INDEX ix_demo_docs_user_session ON docs.demo_docs (user_id, session_id);

                CREATE TABLE docs.doc_exports (
                    id                  uuid        NOT NULL PRIMARY KEY,
                    user_id             uuid        NOT NULL,
                    session_id          uuid        NOT NULL,
                    notes_id            uuid        NOT NULL,
                    provider            varchar(16) NOT NULL,
                    mode                varchar(16) NOT NULL,
                    status              varchar(12) NOT NULL,
                    outcome             varchar(12) NOT NULL,
                    content_digest      varchar(64) NOT NULL,
                    doc_id              uuid        NULL,
                    doc_version         integer     NULL,
                    attempt             integer     NOT NULL,
                    error_code          varchar(32) NULL,
                    retryable           boolean     NULL,
                    retry_after_seconds integer     NULL,
                    created_at          timestamptz NOT NULL,
                    completed_at        timestamptz NULL,
                    CONSTRAINT ck_doc_exports_provider CHECK (provider = 'demo_docs'),
                    CONSTRAINT ck_doc_exports_mode CHECK (mode IN ('create', 'update', 'create_another')),
                    CONSTRAINT ck_doc_exports_status CHECK (status IN ('succeeded', 'failed')),
                    CONSTRAINT ck_doc_exports_outcome CHECK (outcome IN ('created', 'updated', 'unchanged', 'failed')),
                    CONSTRAINT ck_doc_exports_status_outcome CHECK ((status = 'failed') = (outcome = 'failed')),
                    CONSTRAINT ck_doc_exports_attempt CHECK (attempt BETWEEN 1 AND 5),
                    CONSTRAINT ck_doc_exports_error CHECK ((status = 'failed') = (error_code IS NOT NULL)),
                    CONSTRAINT ck_doc_exports_error_code CHECK (error_code IS NULL OR error_code IN ('provider_unavailable', 'rate_limited', 'provider_rejected', 'reconnect_required', 'doc_changed_outside', 'quota_exceeded')),
                    CONSTRAINT ck_doc_exports_retry_after CHECK (retry_after_seconds IS NULL OR retry_after_seconds BETWEEN 1 AND 3600),
                    CONSTRAINT ck_doc_exports_doc_version CHECK (doc_version IS NULL OR doc_version >= 1),
                    CONSTRAINT fk_doc_exports_session_user FOREIGN KEY (session_id, user_id) REFERENCES lectures.lecture_sessions (id, user_id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX ux_doc_exports_id_user_id ON docs.doc_exports (id, user_id);
                CREATE INDEX ix_doc_exports_user_session_created ON docs.doc_exports (user_id, session_id, created_at);

                CREATE TABLE docs.doc_targets (
                    user_id        uuid        NOT NULL,
                    session_id     uuid        NOT NULL,
                    provider       varchar(16) NOT NULL,
                    doc_id         uuid        NOT NULL,
                    last_digest    varchar(64) NOT NULL,
                    version        integer     NOT NULL,
                    last_export_id uuid        NOT NULL,
                    updated_at     timestamptz NOT NULL,
                    PRIMARY KEY (user_id, session_id, provider),
                    CONSTRAINT ck_doc_targets_provider CHECK (provider = 'demo_docs'),
                    CONSTRAINT ck_doc_targets_version CHECK (version >= 1),
                    CONSTRAINT fk_doc_targets_session_user FOREIGN KEY (session_id, user_id) REFERENCES lectures.lecture_sessions (id, user_id) ON DELETE CASCADE
                );

                CREATE TABLE docs.idempotency_keys (
                    user_id          uuid        NOT NULL,
                    method           varchar(6)  NOT NULL,
                    route_template   varchar(64) NOT NULL,
                    key_hash         bytea       NOT NULL,
                    fingerprint_hmac bytea       NOT NULL,
                    export_id        uuid        NOT NULL,
                    created_at       timestamptz NOT NULL,
                    CONSTRAINT ux_docs_idempotency_keys_scope PRIMARY KEY (user_id, method, route_template, key_hash),
                    CONSTRAINT ck_docs_idempotency_keys_hash CHECK (octet_length(key_hash) = 32 AND octet_length(fingerprint_hmac) = 32),
                    CONSTRAINT fk_docs_idempotency_keys_export_user FOREIGN KEY (export_id, user_id) REFERENCES docs.doc_exports (id, user_id) ON DELETE CASCADE
                );
                CREATE INDEX ix_docs_idempotency_keys_export ON docs.idempotency_keys (export_id, user_id);

                CREATE TABLE docs.audit_events (
                    id             uuid        NOT NULL PRIMARY KEY,
                    user_id        uuid        NOT NULL,
                    session_id     uuid        NOT NULL,
                    export_id      uuid        NULL,
                    action         varchar(16) NOT NULL,
                    provider       varchar(16) NOT NULL,
                    error_code     varchar(32) NULL,
                    notes_revision integer     NULL,
                    block_count    integer     NOT NULL,
                    attempt        integer     NOT NULL,
                    at             timestamptz NOT NULL,
                    CONSTRAINT ck_docs_audit_events_action CHECK (action IN ('export_created', 'export_updated', 'export_unchanged', 'export_failed', 'doc_deleted')),
                    CONSTRAINT fk_docs_audit_events_user FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE CASCADE
                );
                CREATE INDEX ix_docs_audit_events_user_session ON docs.audit_events (user_id, session_id, at);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS docs.audit_events;
                DROP TABLE IF EXISTS docs.idempotency_keys;
                DROP TABLE IF EXISTS docs.doc_targets;
                DROP TABLE IF EXISTS docs.doc_exports;
                DROP TABLE IF EXISTS docs.demo_docs;
                """);
        }
    }
}
