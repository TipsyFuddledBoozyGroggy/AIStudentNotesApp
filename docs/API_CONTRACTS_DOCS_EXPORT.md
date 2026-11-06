# docs.export.v1 - export notes to a "Demo Doc" (DRAFT, mock only)

Status: **DRAFT proposal by google-integration, 2026-11-06, for finish-backlog item 3 (approved as a mock by the PO; see `docs/DECISIONS.md` PO-20cb and PO-20cc, which say real Google stays off).** Revised the same day to apply the security review `docs/security/docs-export-design-review.md` (findings DXD-01..DXD-13 in `docs/security/findings-register.md`) and the Lead's answers. Not frozen, not implemented, nothing run against it except the schema fixtures (section 10). The Lead owns the contract text; the Lead's decision is to freeze it as mock-only after the first green integration run. Design and reasoning: `docs/architecture/docs-export-demo-adr.md`. Schemas and fixtures: `src/shared/contracts/docs.export.v1/`. If the schemas and this document disagree, this document wins and the schema is a bug.

Conventions are inherited from `docs/API_CONTRACTS_P2.md` section 2 (base path `/api/v1`, session cookie plus exact Origin check on every state-changing route, user-scoped, foreign and missing ids both `404 not_found`, canonical ids, RFC 9457 problems with a stable `code`, `Idempotency-Key` format and scope, `additionalProperties: false`, end-anchor `(?![\s\S])`). Where this document is silent, P2 section 2 applies.

**Supersedes** the one-line placeholder `POST /sessions/{id}/export/doc` -> `202 {exportId}` in `docs/API_CONTRACTS.md` section 2 (the Lead replaces that row with a pointer here; this agent did not edit that file).

## 1. What this contract is for

The student has Ready notes for a Stopped lecture (lecture.v1 `notes`). They press "Save to a Demo Doc". The app shows **exactly what would be written** (preview, writes nothing), the student confirms, the server writes it to a stand-in "Demo Doc" held in our own database, and returns where it is. A later real Google Docs provider must fit behind the same routes and shapes, with the server-side changes listed honestly in the ADR section 9. **No token, no Google URL, no network, no AI appears anywhere in this contract.**

## 2. Resources

| Name | Schema | Notes |
|---|---|---|
| Preview request | `docs-preview-request` | `{notesId}` only. The client never sends text. |
| Preview | `docs-preview` | the document, counts, `notesTruncated`, `contentDigest`, existing document, allowed `actions`, `canWrite` |
| Export request | `docs-export-request` | the confirm: `notesId`, `contentDigest`, `mode`, `expectedExistingVersion`, `expectedExistingDocId`, `confirm` |
| Export record | `docs-export-record` | result of an attempt; success and provider failure are both `200` |
| State | `docs-state` | current document for the lecture plus the newest attempt |
| Demo Doc | `docs-demo-doc` | read-only view of the stand-in document |
| Problem | `docs-problem` | `code` from `docs-common#/$defs/errorCode` |

Ids: export `dxp_...`, Demo Doc `ddc_...`; **session ids are canonical lowercase hyphenated GUIDs** (the lecture routes bind `{id:guid}`; `docs-common#/$defs/sessionId`); notes ids as in lecture.v1. Server-assigned ids are at least 128 bits, never sequential. A session id in a path that is upper case, braced or `urn:` form is `400 validation_failed` before any lookup; it must not reach the same session by a second spelling.

## 3. Endpoints

| Method | Path | Request | Bucket | Success | Errors |
|---|---|---|---|---|---|
| POST | `/sessions/{id}/docs-export/preview` | `docs-preview-request` | `docs-read` | 200 `docs-preview` | 400, 401, 403 `csrf_failed`, 404, 409 `invalid_state`, 413, 415, 429 |
| POST | `/sessions/{id}/docs-export` | `docs-export-request` + `Idempotency-Key` **required** | `docs-write` | 200 `docs-export-record` (also for provider failure; `Idempotency-Replayed: true` on a replay) | 400, 401, 403, 404, 409 `invalid_state` / `retry_limit_reached`, 412 `precondition_failed`, 413, 415, 429 |
| GET | `/sessions/{id}/docs-export` | | `docs-read` | 200 `docs-state` | 401, 404 |
| GET | `/demo-docs/{docId}` | | `docs-read` | 200 `docs-demo-doc` | 401, 404 |
| DELETE | `/demo-docs/{docId}` | | `docs-write` | 204 (included, Lead decision Q3) | 401, 403, 404 |

`preview` is a POST only because it carries a body; it is safe to repeat. It has no `Idempotency-Key`.

## 4. Behaviour (normative)

### 4.1 Evaluation order (confirm; same as `API_CONTRACTS_P2.md` 2.1 / the Syllabus order)

1 authentication `401`; 2 Origin/CSRF `403 csrf_failed`; 3 syntax: `Idempotency-Key` well formed else `400`, canonical ids; 4 **ownership** `(user, session)` through the user-scoped session directory, else `404`; 5 **idempotency lookup by key only** `(user, method, route template, key hash)`: a miss continues, a hit marks the request a *candidate replay* (the fingerprint cannot be compared yet, because it covers the body); 6 per-user rate limit `429` from counters, before any body is read; 7 body read under a **read deadline and a minimum data rate** (playbook section 5) and a 2 KB cap (`413`), `Content-Type: application/json` (`415`); then, for a candidate replay, **compare the fingerprint** (same: replay or retry per 4.4; different: `400 validation_failed`) before validation; 8 validation `400`; 9 state and preconditions: `409 invalid_state` (session not `Stopped`, notes not `Ready`, mode not allowed), `412 precondition_failed` (digest, `expectedExistingVersion`, `expectedExistingDocId`).

A caller who does not own the session never sees `409`, `412`, `413`, `415` or `429` caused by someone else's data. A `notesId` that is not the session's **current** notes id is `404 not_found` in **both** preview and confirm (DXD-12); it is decided only by comparing with the session's current notes id and a `notesId` is never looked up on its own.

**Rate buckets (DXD-03).** Preview, state and Demo Doc read use `WithSeparateRateBucket("docs-read", n)`; confirm and DELETE use `WithSeparateRateBucket("docs-write", 10 per minute per user)`, as Syllabus does. Without this metadata a POST lands in the shared `w:` write bucket that Quick recap uses. A test must show preview cannot exhaust `docs-write` or the live-lecture bucket.

### 4.2 Preview (writes nothing)

- Builds the document **on the server** from the stored notes of the authenticated user: title, one `heading` level 2 per notes section, one `bullet` per bullet with its citations reduced to `{startMs}`, the Final summary revision's content when this press made it and it is `Ready`, and a closing source `paragraph` ("Made by Tether...", with the notes `source` wording). **If `notes.truncated` is true, a further closing paragraph says where the notes stop** (from `coveredToMs`, same sentence family as the Markdown export, `notesExport.ts`), and `notesTruncated` is `true` (DXD-13, L-DROP). No segment id, no transcript text beyond what the notes already contain, no token.
- **Title source.** From the lecture title (fallback "Lecture notes") and the lecture start date in the lecture's stored time zone when the session carries one, else the UTC date with the word UTC. These come from a narrow Docs-only port in `Lectures.Contracts` (ADR section 4, Lead decision Q-B; `SessionInfo` is not changed, DXD-17); the course name is **not** in the demo title.
- Writes **no row** (rate counters aside), calls **no provider** and **no AI**, writes **no audit row**. `previewOnly` is the constant `true`.
- `409 invalid_state` if the session is not `Stopped` or the notes are not `Ready` (fixed `detail`).
- `contentDigest` is defined in `docs-common#/$defs/contentDigest`: HMAC-SHA-256 under a key derived with the **new HKDF label `docs-content-digest.v1`** (added to `DerivedKeys.Labels`; the idempotency and audit keys are never reused), over `label LF sessionId LF canonical JSON {v:1, notesId, title, blocks}`. Canonical JSON: UTF-8, no whitespace, member order as the schemas list them, strings NFC with only quote, backslash and `\u00XX` escapes, integers in decimal. It is an equality check on content, never proof of authorization. A root-secret rotation makes every stored digest stale once (`upToDate` turns false and an `update` rewrites identical content); accepted and written down.
- `existing` is the document this app already keeps for this lecture (`docRef`, `lastExportedAt`) and `upToDate` (digest equals the digest last written). `actions`: no existing -> `["create"]`; existing and not up to date -> `["update","create_another"]`; existing and up to date -> `[]`. `canWrite` false with `blockedReason` `too_large` (more than 400 blocks) or `nothing_to_export` gives `actions: []`. **Nothing is ever truncated silently.**

### 4.3 Confirm and write (demo: ONE transaction per attempt)

- Body: `confirm: {provider: "demo_docs", acknowledgedDemo: true}`. Nothing is written without it. The provider is fixed (a const); a real provider is not expressible.
- **Lock order inside the single transaction (DXD-01, DXD-14, DXD-16, L-LOCKORDER; every Docs writer follows it, no exceptions):** (1) the session row `FOR KEY SHARE` through a narrow Docs-only port in `Lectures.Contracts` that takes the caller's `DbTransaction` and returns the session facts (`null` = the session is gone = `404`, the C-21 re-check); (2) `pg_advisory_xact_lock` on `(user, session)`, which serialises the first `create` and makes a second request for the same lecture wait; (3) a **per-user** `pg_advisory_xact_lock` on the user (DXD-16), always after (2), taken by **every** Docs writer (confirm, student delete, any job) so the 200-document cap and the per-user count are race-free across lectures; (4) docs rows in the fixed order `doc_exports`, `idempotency_keys`, `demo_docs`, `doc_targets`, `audit_events` (DXD-14: `idempotency_keys` references `doc_exports` through its composite FK, so the parent row is inserted first; the alternative is a deferrable FK). Lock order is therefore: session row, then (user,session) advisory, then user advisory, then rows. Two lectures of one user serialise on the user lock only after each holds its own session lock, so there is no cycle; the cascade delete locks the session row first and then children, the same direction. A `SELECT ... FOR UPDATE` on `doc_targets` before the session lock is forbidden (reverse of the cascade delete).
- Inside the transaction: the server rebuilds the document from the stored notes, recomputes the digest and compares it in constant time with `contentDigest`; different -> `412`, nothing written (the transaction rolls back, and the failed state check stores no key). `expectedExistingVersion` and `expectedExistingDocId` must equal the current document's version and id (both `null` when there is none), else `412`; this binds the confirm to the **document the student reviewed**, not only its version (DXD-05). `mode` must be in the actions a preview would list now, else `409 invalid_state`.
- The demo provider writes inside the same transaction (a savepoint around the provider call). On a provider failure (fault injection only) the savepoint rolls back the document change and the export row is stored as `failed` together with the idempotency key and an audit row, then committed.
- **Same content, same document:** `mode: update` with a digest equal to the last written digest returns `outcome: "unchanged"` and writes no document change (version stays).
- **Edited notes = update vs new document is the student's explicit choice.** `update` replaces the content of the document this app made and bumps `version`; `create_another` makes a second document, leaves the first untouched and moves the lecture's current-document pointer; the server never picks. "Edited" means anything that changes the digest: new notes content, the lecture title, or a changed builder version.
- Database errors (DXD-14, DXD-15): `23503` is mapped to `404` **only** when the violated constraint is one of the named session FKs (`demo_docs`, `doc_exports`, `doc_targets` -> `lectures.lecture_sessions`, i.e. the session vanished mid-write); any other `23503` (for example `idempotency_keys` -> `doc_exports` from a wrong insert order) is a server bug: `500` with a content-free log, never a calm `404`. `23505` on the idempotency key **compares the fingerprint first**: equal -> replay of the winner's record; different -> `400 validation_failed` (the key scope does not include the session, so the same key sent for two lectures at the same time must never replay the other lecture's record). A deadlock (`40P01`/`40001`) repeats the **whole attempt** (up to 3 times) and does not increase `attempt`.
- Response `200 docs-export-record`: `status: "succeeded"` with `outcome` `created` | `updated` | `unchanged`, or `status: "failed"` with `outcome: "failed"`, `error` and `retry`.
- **No `pending` state, no reaper and no `export_in_progress` in the demo** (one transaction; a second request waits on the advisory lock). Those are real-provider mechanics (ADR section 9). The schema keeps the reserved `export_in_progress` code so a real provider does not change the wire.

### 4.4 Idempotency and retry

- Key scope `(user, method, route template, key hash)`; two users sending the same key and body run independently (test). Fingerprint covers the concrete session path and the canonical body, stored as an HMAC. Kept 24 hours; the record holds ids and hashes only, never a response body, title or text.
- **A key is stored only together with an export row.** A request that fails at steps 1-9 with a `4xx` stores nothing, so a `412` or `409` leaves the key free.
- **Same key and body after success:** the original record with `Idempotency-Replayed: true`; no second document, no second version; may be stale by design. After the lecture is deleted a replay is `404`, never the original response.
- **Same key and body after a failed attempt:** a **new write** that passes steps 6 and 8-9 again (so it can return `409` or `412` if the notes, the document or the mode changed), re-attempts the write and increases `attempt` up to 5. After the fifth failed attempt `retry.allowed` is false and a further send is `409 retry_limit_reached`; the student starts over from a new preview. A different body under the same key is `400`.
- **Two requests with the same key at the same time:** the second waits on the advisory lock, then returns the winner's record as a replay (`Idempotency-Replayed: true`). One outcome only; the first, coalesced and replayed paths return identical bodies. If the two requests are for different lectures (so they do not share the lecture lock), the loser of the `23505` race compares fingerprints and gets `400`, never the other lecture's record.
- **Provider-side idempotency (demo):** the document insert is keyed by the `exportId` (unique `origin_export_id`).

### 4.5 Failure and retry states (inside a 200 record)

| `error.code` | retryable | `retryAfterSeconds` | Meaning |
|---|---|---|---|
| `provider_unavailable` | true | small (5) | the stand-in (or later Google) did not answer; nothing written |
| `rate_limited` | true | honour the provider value (cap 3600) | slow down |
| `provider_rejected` | false | null | the provider refused permanently |
| `reconnect_required`, `doc_changed_outside`, `quota_exceeded` | per code | | **RESERVED for a real provider; the demo never emits them** |

A failed first attempt has `docRef: null`. A failed `update` keeps `docRef` of the existing document at its unchanged version. A failed attempt never changes the existing document.

### 4.6 Authorization

Every route resolves through user-scoped queries; composite foreign keys `(session_id, user_id)`; a `ddc_` id is valid only for its owner. IDOR tests per route are required. The Demo Doc route needs the same session cookie; there is no public or shareable link. The notes reader (ADR section 4) uses the ambient user and refuses a different `userId` argument.

### 4.7 Deletion and erasure

- Deleting a lecture, or purging its course, deletes its exports, targets, Demo Docs and idempotency records in the same transaction through composite foreign keys with `ON DELETE CASCADE` (ADR-020 precedent; `docs.idempotency_keys` cascades through a composite FK `(export_id, user_id)` to `doc_exports`). No `RESTRICT` between docs tables. A write that races the delete either completes first and is removed by the cascade, or finds the session gone at the `FOR KEY SHARE` and gets `404`. **There is no separate tombstone** (the NAS-01 claim is deleted, DXD-10). A replay after delete is `404`.
- **Content-free audit rows (`docs.audit_events`, 4.8) outlive the lecture** and are **on the list of tables full account erasure must remove (PO-20aw)**; their retention after a lecture delete is subject to the open PO-20ca follow-up and L-14. Until answered they are kept.
- A Demo Doc is ours, so "deleted with the lecture" is true **for the demo only**; for a real provider the document in Google is **not** deleted (C-21, PO-15c) and the copy must say so.
- Student `DELETE /demo-docs/{docId}` removes that document; if it was the current one the pointer is cleared and the next export is `create`. Documents left behind by `create_another` are reachable through their export records and this delete.

### 4.8 Audit, logging, privacy

- Audit row (content-free): `id`, `user_id`, `session_id` (no FK, so it outlives the lecture), `export_id`, `action` (`export_created` | `export_updated` | `export_unchanged` | `export_failed` | `doc_deleted`), `provider`, `error_code` (nullable), `notes_revision`, `block_count`, `attempt`, `at`. **No title, text, digest, doc body or token.** Previews write none.
- Logs carry id hashes, counts, codes and durations. Never the title, block text, `contentDigest`, request body, cookies or any token. The log-canary tests cover every new failure path.
- **Tokens never near AI.** The Docs module does not reference the AI client or any token store; the notes reader is read-only and user-bound; the AI module does not reference the Docs module.
- **Fault injection** (Development-only header, ADR section 7): the host refuses startup in any environment other than `Development` or `Test` if **any** `Docs:Demo:Faults:*` key is present, not only `Enabled=true`. The header is ignored (no `400`) when the flag is off. Faults fire inside `DemoDocsProvider`; the `slow` fault is capped (5 s) and honours cancellation so it cannot hold the session lock and a pooled connection without limit.

## 5. Error codes

lecture.v1 codes keep their meaning: `validation_failed` 400, `unauthenticated` 401, `csrf_failed` 403, `not_found` 404, `invalid_state` 409, `precondition_failed` 412, `rate_limited` 429 (`Retry-After`), `provider_unavailable` 503 (only for infrastructure failure outside the provider call, for example the database; provider failures are inside a 200 record). Additions:

| code | HTTP | Meaning | detail (fixed) |
|---|---|---|---|
| `too_large` | 413 | body over 2 KB | The request was too large. |
| `retry_limit_reached` | 409 | 5 failed attempts under this key | Too many attempts. Start again. |
| `export_in_progress` | 409 | **RESERVED, real provider only; the demo never emits it** | A save is already running. |

`detail` is a fixed string per code, never exception text, provider text, a title or notes text.

## 6. Limits and rates (proposals, configurable, not measured; devops and the PO own the values)

Body 2 KB with a read deadline and a minimum data rate. Document 400 blocks, title 200, block text 500 code points, citations 8 per bullet. Buckets `docs-read` (n per minute, proposal 60) and `docs-write` (10 per minute per user), both separate from the shared `w:` bucket. Per user: at most 200 Demo Docs (the cap is checked under the **per-user advisory lock**, taken after the session lock by every writer, so it is race-free across lectures; DXD-16) and 5 attempts per key. Worst-case storage is about 0.8 MB per document and 160 MB per user (400 blocks x 500 code points x 4 bytes x 200); dev login creates users freely (PPR-03), so the cap matters.

## 7. Time and text

Instants are RFC 3339. All text is untrusted plain text, one line, TextPolicy applied at write and at every sink; the web renders it as text (no HTML, no Markdown, no links). The label of the demo destination is the constant "Demo Doc"; the client parser rejects the `external` `docRef` branch in v1 and validates a `[docId]` route parameter against the id pattern before building an API path.

## 8. What this contract does not do

No Google OAuth, scope, token, URL fetch or network call. No import from a Doc. No student-edited title or body (Lead decision Q4: no). No sharing, permissions or Drive folders. No partial or per-section export. No automatic export: only a confirm creates or changes a document.

## 9. Tests required (not written)

- `DocsExportContractTests` (own file, like `CalendarContractTests`): register the folder, every valid fixture passes, every invalid one is rejected; `id`, `timestamp`, `idempotencyKey` byte-identical to `syllabus-common`; no schema property named `token`, `access`, `refresh`, `code`, `secret`, `segmentId`; error codes equal `docs-common#/$defs/errorCode`; section 5 table equals the schema.
- Semantic checks JSON Schema cannot express: digest recomputation (label, session id, canonical form), mode vs existing document, constant-time compare, block cap, TextPolicy (RLO, zero-width, isolates, tag characters, trailing LF), canonical GUID form.

## 10. Verification status

Schemas validated against 20 valid and 42 invalid fixtures with ajv 2020 (strict off, `date-time` format not checked, patterns checked) in a scratch script on 2026-11-06 after the security-review changes; **no .NET contract test exists or was run**. No server, client or provider exists. Everything above is proposal.

## 11. Fixtures

`src/shared/contracts/docs.export.v1/fixtures/valid|invalid/{schema}--{case}.json`. Every invalid fixture is one mutation of a valid one.
