# docs.export.v1 - export notes to a "Demo Doc" (DRAFT, mock only)

Status: **DRAFT proposal by google-integration, 2026-11-06, for finish-backlog item 3 (approved as a mock by the PO; see `docs/DECISIONS.md` PO-20cb and PO-20cc, which say real Google stays off).** Not frozen, not implemented, nothing run against it except the schema fixtures (section 11). The Lead owns the contract text and decides when it freezes. Design and reasoning: `docs/architecture/docs-export-demo-adr.md`. Schemas and fixtures: `src/shared/contracts/docs.export.v1/`. If the schemas and this document disagree, this document wins and the schema is a bug.

Conventions are inherited from `docs/API_CONTRACTS_P2.md` section 2 (base path `/api/v1`, session cookie plus exact Origin check on every state-changing route, user-scoped, foreign and missing ids both `404 not_found`, canonical ids, RFC 9457 problems with a stable `code`, evaluation order, `Idempotency-Key` format and scope, `additionalProperties: false`, end-anchor `(?![\s\S])`). Where this document is silent, P2 section 2 applies.

**Supersedes** the one-line placeholder `POST /sessions/{id}/export/doc` -> `202 {exportId}` in `docs/API_CONTRACTS.md` section 2 (the Lead should replace that row with a pointer here; this agent did not edit that file).

## 1. What this contract is for

The student has Ready notes for a Stopped lecture (lecture.v1 `notes`). They press "Save to a Demo Doc". The app shows **exactly what would be written** (preview, writes nothing), the student confirms, the server writes it to a stand-in "Demo Doc" held in our own database, and returns where it is. A later real Google Docs provider must fit behind the same routes and shapes (ADR section 9). **No token, no Google URL, no network, no AI appears anywhere in this contract.**

## 2. Resources

| Name | Schema | Notes |
|---|---|---|
| Preview request | `docs-preview-request` | `{notesId}` only. The client never sends text. |
| Preview | `docs-preview` | the document, counts, `contentDigest`, existing document, allowed `actions`, `canWrite` |
| Export request | `docs-export-request` | the confirm: `notesId`, `contentDigest`, `mode`, `expectedExistingVersion`, `confirm` |
| Export record | `docs-export-record` | result of an attempt; success and provider failure are both `200` |
| State | `docs-state` | current document for the lecture plus the newest attempt |
| Demo Doc | `docs-demo-doc` | read-only view of the stand-in document |
| Problem | `docs-problem` | `code` from `docs-common#/$defs/errorCode` |

Ids: export `dxp_...`, Demo Doc `ddc_...`, session and notes ids as in lecture.v1. All ids are server-assigned, at least 128 bits, never sequential.

## 3. Endpoints

| Method | Path | Request | Success | Errors |
|---|---|---|---|---|
| POST | `/sessions/{id}/docs-export/preview` | `docs-preview-request` | 200 `docs-preview` | 400, 401, 403 `csrf_failed`, 404, 409 `invalid_state`, 413, 415, 429 |
| POST | `/sessions/{id}/docs-export` | `docs-export-request` + `Idempotency-Key` **required** | 200 `docs-export-record` (also for provider failure; `Idempotency-Replayed: true` on a replay) | 400, 401, 403, 404, 409 `invalid_state` / `export_in_progress` / `retry_limit_reached`, 412 `precondition_failed`, 413, 415, 429 |
| GET | `/sessions/{id}/docs-export` | | 200 `docs-state` | 401, 404 |
| GET | `/demo-docs/{docId}` | | 200 `docs-demo-doc` | 401, 404 |
| DELETE | `/demo-docs/{docId}` | | 204 (OPTIONAL, recommended; open question Q3) | 401, 403, 404 |

`preview` is a POST only because it carries a body; it is safe to repeat and is rate limited as a read. It has no `Idempotency-Key`.

## 4. Behaviour (normative)

### 4.1 Evaluation order (confirm)

1 authentication `401`; 2 Origin/CSRF `403 csrf_failed`; 3 syntax: `Idempotency-Key` well formed else `400`, canonical ids; 4 **ownership** `(user, session)` through the user-scoped session directory, else `404` (a session whose delete or course purge has been announced is `404` even before the purge commits, as in `notes-after-stop.md` NAS-01); 5 **idempotency lookup** `(user, method, route template, key hash)`: same fingerprint is a replay or a retry (4.4), a different fingerprint is `400 validation_failed`; 6 rate limit `429` from counters, before any body is read; 7 body read with a 2 KB cap (`413`) and `Content-Type: application/json` (`415`); 8 validation `400`; 9 state `409` (session not `Stopped`, notes not `Ready`, `notesId` not the session's current notes, mode not allowed) and preconditions `412`.

A caller who does not own the session never sees `409`, `412`, `413`, `415` or `429` caused by someone else's data.

### 4.2 Preview (writes nothing)

- Builds the document **on the server** from the stored notes of the authenticated user: title, one `heading` level 2 per notes section, one `bullet` per bullet with its citations reduced to `{startMs}`, the Final summary revision's content when this notes press made it and it is `Ready`, and one closing `paragraph` that names the source ("Made by Tether...", with the notes `source` wording). No segment id, no transcript text beyond what the notes already contain, no token.
- Writes **no row** (rate counters aside), calls **no provider** and **no AI**, writes **no audit row**. `previewOnly` is the constant `true`.
- `409 invalid_state` if the session is not `Stopped` or the notes are not `Ready` (fixed `detail`). A `notesId` that is not the session's current notes: `404 not_found`.
- `contentDigest` = `"cd_" + first 43 chars of base64url(HMAC-SHA-256(server key, canonical JSON {v:1, notesId, title, blocks}))`. HMAC, not a plain hash.
- `existing` is the document this app already keeps for this lecture (`docRef`, `lastExportedAt`) and `upToDate` (`contentDigest` equals the digest last written). `actions`: no existing -> `["create"]`; existing and not up to date -> `["update","create_another"]`; existing and up to date -> `[]` (nothing to do). `canWrite` false with `blockedReason` `too_large` (more than 400 blocks) or `nothing_to_export` (no bullets) gives `actions: []`. **Nothing is ever truncated silently.**

### 4.3 Confirm and write

- The body must carry `confirm: {provider: "demo_docs", acknowledgedDemo: true}`. Nothing is written without it. The provider is fixed; a real provider is not expressible.
- The server rebuilds the document from the stored notes, recomputes the digest and compares it in constant time with `contentDigest`. Different -> `412 precondition_failed`, nothing written; the client shows a fresh preview. `expectedExistingVersion` must equal the current version of the existing document (or be `null` when there is none), else `412`.
- `mode` must be in the actions a preview would list now, else `409 invalid_state`. `create` with an existing document, or `update`/`create_another` without one, is `409 invalid_state`.
- **Same notes content, same document:** a new key with `mode: update` while the digest equals the last written digest returns `outcome: "unchanged"`, writes nothing, version stays.
- **Edited notes = update vs new document is the student's explicit choice** (`update` replaces the content of the document this app made and bumps `version`; `create_another` makes a second document and leaves the first untouched; the server never picks for the student). "Edited" means anything that changes the digest: new notes content, the lecture title (it is in the document title), or a changed builder version.
- The write is one transaction: export row, provider call, document row, target pointer, content-free audit row. The Demo Doc is "written" by an insert or update of one row in `docs.demo_docs`, owned by the user.
- Response `200 docs-export-record`. `status: "succeeded"` with `outcome` `created` | `updated` | `unchanged`, or `status: "failed"` with `outcome: "failed"`, `error` and `retry`.

### 4.4 Idempotency and retry

- Key scope `(user, method, route template, key hash)`, fingerprint covers the concrete session path and a canonical hash of the body, stored as an HMAC. Kept 24 hours. The stored record holds ids and hashes only, never a response body, title or text.
- **Same key and same body after success:** the original record with `Idempotency-Replayed: true`. **No second document and no second version.** The record may be stale by design (later edits are not reflected). After the lecture is deleted a replay is `404`, never the original response.
- **Same key and same body after a failed attempt:** the write is attempted again (nothing was written, so it cannot duplicate); `attempt` increases. At attempt 5 failed, `retry.allowed` is false and a further send is `409 retry_limit_reached`; the student starts over from a new preview. A different body under the same key is `400`.
- **Provider-side idempotency:** the provider call receives the `exportId` as its request key; the mock enforces it with a unique index so a crash between "document written" and "record updated" cannot produce two documents.
- A concurrent second request for the same `(user, session)` while one is `pending` is `409 export_in_progress` with `Retry-After`. A `pending` export older than 60 seconds is reaped to `failed` (`provider_unavailable`, retryable).

### 4.5 Failure and retry states (inside a 200 record)

| `error.code` | retryable | `retryAfterSeconds` | Meaning |
|---|---|---|---|
| `provider_unavailable` | true | small (5) | the stand-in (or later Google) did not answer; nothing written |
| `rate_limited` | true | honour the provider value (cap 3600) | slow down |
| `provider_rejected` | false | null | the provider refused permanently |
| `reconnect_required`, `doc_changed_outside`, `quota_exceeded` | per code | | **RESERVED for a real provider; the demo never emits them** |

A failed first attempt has `docRef: null`. A failed `update` keeps `docRef` of the existing document at its unchanged version. A failed attempt never changes the existing document (the update is one transaction).

### 4.6 Authorization

Every route resolves through user-scoped queries; composite foreign keys `(session_id, user_id)`; a `ddc_` id is valid only for its owner. IDOR tests per route are required (section 9). The Demo Doc route needs the same session cookie; there is no public or shareable link.

### 4.7 Deletion

Deleting a lecture, or purging its course, deletes its exports, targets, Demo Docs and idempotency records in the same transaction (composite FK cascade, ADR-020 precedent), so a late job cannot recreate them (`API_CONTRACTS.md` C-21: writers re-check the session exists). The content-free audit rows (4.8) are kept, following the existing consent/usage-row rule, and the retention question stays open as PO-20ca left it. A Demo Doc is ours, so "deleted with the lecture" is true **for the demo only**; for a real provider the document in Google is **not** deleted (C-21, PO-15c) and the copy must say so (ADR section 9).

### 4.8 Audit, logging, privacy

- Audit row (content-free): `id`, `user_id`, `session_id` (no FK, so it outlives the lecture), `export_id`, `action` (`export_created` | `export_updated` | `export_unchanged` | `export_failed` | `doc_deleted`), `provider`, `error_code` (nullable), `notes_revision`, `block_count`, `attempt`, `at`. **No title, no text, no digest, no doc body, no token.** Previews write none.
- Logs carry id hashes, counts, codes and durations. Never the title, block text, `contentDigest`, request body, cookies or any token. The log-canary tests must cover every new failure path.
- **Tokens never near AI.** The Docs module does not reference the AI client or any token store; the notes reader is read-only; the AI module does not reference the Docs module (ADR section 4).

## 5. Error codes

lecture.v1 codes keep their meaning: `validation_failed` 400, `unauthenticated` 401, `csrf_failed` 403, `not_found` 404, `invalid_state` 409, `precondition_failed` 412, `rate_limited` 429 (`Retry-After`), `provider_unavailable` 503 (only for infrastructure failure outside the provider call, for example the database; provider failures are inside a 200 record). Additions:

| code | HTTP | Meaning | detail (fixed) |
|---|---|---|---|
| `too_large` | 413 | body over 2 KB | The request was too large. |
| `export_in_progress` | 409 | another export for this lecture is running | A save is already running. |
| `retry_limit_reached` | 409 | 5 failed attempts under this key | Too many attempts. Start again. |

`detail` is a fixed string per code, never exception text, provider text, a title or notes text.

## 6. Limits and rates (proposals, configurable, not measured; devops and the PO own the values)

Body 2 KB. Document 400 blocks, title 200, block text 500 code points, citations 8 per bullet. Preview shares the 30/min read bucket; confirm has a **docs write bucket** of 10/min per user, separate from the 30/min live-lecture bucket and the syllabus bucket. Per user: at most 200 Demo Docs and 5 attempts per key.

## 7. Time and text

Instants are RFC 3339. The document carries no local dates in the demo; the title is built by the server from the lecture title, course name and start date in the lecture's stored zone. All text is untrusted plain text, one line, TextPolicy applied at write and at every sink; the web renders it as text (no HTML, no Markdown, no links).

## 8. What this contract does not do

No Google OAuth, scope, token, URL fetch or network call. No import from a Doc. No student-edited title or body (open question Q4). No sharing, permissions or export to Drive folders. No partial or per-section export. No automatic export: only a confirm creates or changes a document.

## 9. Contract tests required (not written)

- `DocsExportContractTests` (own file, like `CalendarContractTests`): register the folder, every valid fixture passes, every invalid fixture is rejected; `id`, `timestamp`, `idempotencyKey` byte-identical to `syllabus-common`; no schema property named `token`, `access`, `refresh`, `code`, `secret`, `segmentId`; error codes equal `docs-common#/$defs/errorCode`; the code table in section 5 equals the schema; doc/schema drift on the endpoint table.
- Semantic checks JSON Schema cannot express: digest recomputation, mode vs existing, constant-time compare, block cap, TextPolicy rejects RLO, zero-width, isolates, tag characters, trailing LF.

## 10. Verification status

Schemas validated against 19 valid and 34 invalid fixtures with ajv 2020 (strict off, `date-time` format not checked, patterns checked) in a scratch script on 2026-11-06; **no .NET contract test exists or was run**. No server, client or provider exists. Everything above is proposal.

## 11. Fixtures

`src/shared/contracts/docs.export.v1/fixtures/valid|invalid/{schema}--{case}.json`. Every invalid fixture is one mutation of a valid one.
