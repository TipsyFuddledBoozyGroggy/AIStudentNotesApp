# API Contracts - Phase 2, slice 1: Syllabus import (`syllabus.v1`)

**Status:** DRAFT v1, not frozen. Authored 2026-10-28 (backend-engineer, for the Lead Architect); revised the same day to fold in the security review `docs/security/p2-contract-review.md` (all 8 High and most Medium findings; see section 13 rows 25-39 and the disposition table appended to that review). Nothing here is implemented: there is no Syllabus module, no endpoint, no migration and no AI document seam yet. This file and `src/shared/contracts/syllabus.v1/` are the only artifacts, and they are verified only by schema and fixture contract tests (section 12). The M2 contract `docs/API_CONTRACTS.md` (lecture.v1, speech.v1) is frozen and is **not** modified by this file.

Items marked **provisional** are Lead Architect decisions recorded for this draft where the decision is owned by the Product Owner. They are defaults to build against, not approvals; the Product Owner may overturn them (section 14). The mock provider is the only provider: nothing here authorizes a real Claude call, a Google write, real student documents or any gate (G1-G5) change.

Freeze rule once frozen: additive changes only within `v1`; a breaking change needs `syllabus.v2` and an ADR. Until then any part may change, but schema, fixtures, tests and this document must change together.

Sources reconciled: `product/phase2-syllabus-import.md` (P), `ai/syllabus-extraction-design.md` (A, section 13 proposed shapes), `ux/phase2-syllabus-review.md` (U), `API_CONTRACTS.md` and `architecture/backend-foundation.md` (conventions). Conflicts and the choice made are in section 13.

## 1. Decisions recorded for this draft

| # | Decision | Owner | Status |
|---|---|---|---|
| 1 | Extraction progress is **REST polling**. No SignalR hub, no new realtime surface (ADR-020d: single instance). The syllabus resource carries `pollAfterSeconds`. | Lead Architect | decided |
| 2 | Ids use the same format as existing resources: opaque `[A-Za-z0-9_-]{1,64}`, server-assigned, prefixed (`syl_`, `itm_`, `syn_`, `evt_`, `unc_`). Clients must not parse them. | Lead Architect | decided |
| 3 | New contract folder `syllabus.v1/`, because `lecture.v1` is frozen. | Lead Architect | decided |
| 4 | Backend will be a **new Syllabus module** (not created here). The AI module will get a **document-scope seam** (A section 3 gap 1; not created here). | Lead Architect | decided |
| 5 | The **original uploaded file is not retained**. Transient upload bytes live only in a non-logged ephemeral store and are deleted when extraction ends and in any case within 1 hour (section 5.1). Canonical extracted text, items and snippets are stored; the canonical text is purged on delete or after a provisional 180 days from last student activity (section 5.3). `Syllabus.originalRetained` is the constant `false`. | Product Owner (P2-D4, D11) | **provisional, PO decision pending** |
| 6 | Calendar is **mock only**. `ICalendarProvider` has one implementation; startup refuses any other provider while PO-6 is open. Every response says `provider: "mock"` and `demoCalendar: true`. Real Google Calendar is **not** an additive change to this contract: it needs a new `calendar.v1` contract plus a security re-review (section 15). | Product Owner (PO-6) | provisional, PO decision pending |
| 7 | Default timed-event length is **60 minutes**. | Product Owner | **provisional, PO decision pending** |
| 8 | Several syllabi per course are allowed (proposal: 5 per course, 50 per user). | Product Owner | provisional, PO decision pending |
| 9 | Limits (section 10) are proposals: 10 MB, 60 pages, about 50,000 extracted characters. | Product Owner / devops | provisional, PO decision pending |
| 10 | Approve and calendar sync are **separate commands** (P2-D7). | Product Owner | provisional, PO decision pending |
| 11 | Undo of approve or skip **never expires** (P2-D8). | Product Owner | provisional, PO decision pending |
| 12 | Recurring items sync as individual occurrences (P2-D6), at most 60 per item. | Product Owner | provisional, PO decision pending |

## 2. Conventions (inherited, plus the normative rules below)

Base path `/api/v1`, session cookie plus Origin check, user-scoped: a foreign or missing id is `404 not_found`, never 403. Errors are RFC 9457 ProblemDetails with a stable `code` (`syllabus-problem.schema.json`; the lecture.v1 codes plus section 9). `detail` is a fixed server string per `code`, never exception text, model output, document content, a filename or a detected file type; `errors` keys are field names and its values come from a fixed vocabulary that never echoes the rejected value. Lists are `{items, nextCursor}` with `?cursor&limit`, server maximum 200. `ETag` is the opaque weak tag; `If-Match` is required on item writes (`428 precondition_required` when absent, `412 precondition_failed` on mismatch; lecture.v1 codes, not `409 conflict`).

Dates the student reads are **wall-clock**: `YYYY-MM-DD`, `HH:MM` and an IANA zone id (`America/New_York`). No UTC and no offsets appear anywhere except instants (`createdAt`, `syncedAt`). UTC conversion happens only at a real Calendar write, in a future provider's code, and is out of scope. All-day items carry no time and do not shift.

### 2.1 Request handling (normative)

- **Unknown members are rejected.** Request DTOs are separate from entities and are never bound onto them. Unknown JSON members, and duplicate member names (`JsonDocumentOptions.AllowDuplicateProperties = false` or equivalent; verify on the pinned version), are `400 validation_failed`. Posting any read-only field (`origin`, `confidence`, `flags`, `syncState`, `userId`, `courseId`, `syllabusId`, `itemKey`, `revision`, `etag`, `edited`, `snippet`) is `400`, never silently ignored. Every request schema has `additionalProperties: false`; red-team fixtures `*--readonly-*` exist for each.
- **Evaluation order** for every route, so no error leaks the existence or state of a resource the caller does not own: (1) authentication `401`; (2) Origin/CSRF `403 csrf_failed`; (3) header syntax (`Idempotency-Key` well-formed, else `400`; `If-Match` present, else `428`); (4) **ownership chain** `(userId, courseId, syllabusId, itemId)` through user-scoped queries, failing as `404 not_found`; (5) per-user and per-route limits `429 rate_limited` and quota `409` (`extraction_in_progress`, `syllabus_limit_reached`), decided from counters only, before any body is read; (6) body read with streaming caps (`413`, `415`); (7) validation `400`; (8) state `409` / precondition `412`. A caller who does not own the course therefore never sees `409`, `413`, `415` or `429` caused by someone else's data.
- **Nested ids.** An item id is valid only within the syllabus in the path, and a syllabus id only within its course and owner. A body id (`itemEtags` and `itemRevisions` keys, `itemIds`, `existingSyllabusId`) that is not in that syllabus gets the same outcome as a nonexistent id: `404` for a path id, per-item `not_found` inside a `200` result. Foreign, nonexistent and other-syllabus ids are indistinguishable in status, body and (as far as practicable) timing. Child rows carry `user_id` and are filtered by the global query filter. Lookups for duplicates are `(userId, courseId, contentSha256)` and the unique index includes `user_id`. A route-by-route test table (every route, two users with overlapping ids, plus a same-user mismatched parent) is required (section 12).
- **CSRF/Origin** apply to every state-changing route, **including the multipart upload and every DELETE**: exact ordinal Origin match, a missing Origin is rejected, and a required custom header is checked. `multipart/form-data` is a CORS-safelisted type, so the Idempotency-Key header being "non-simple" must not be relied on as the defense.
- **Ids** are at least 128 bits from a CSPRNG (or GUID/ULID) and never sequential. `itemKey` is an idempotency basis only; no endpoint accepts it as an identifier.
- **Idempotency-Key** (format `syllabus-common.idempotencyKey`, 8-128 URL-safe characters, else `400`). Scope is `(user, method, route template, key)`, with a unique index on `(user_id, method, route_template, key_hash)`; the stored fingerprint covers the concrete path (so the `syllabusId`), a canonical hash of the body and, for multipart, the file hash plus metadata. Reusing a key with a different fingerprint is `400 validation_failed`. Keys are kept 24 hours; the stored record holds ids and hashes only, never titles or a response body, and is deleted with the syllabus. **Replay semantics:** a replay re-runs authentication, Origin and ownership (so a deleted syllabus is `404`), then returns the operation's **original outcome** with `Idempotency-Replayed: true`. For creates that is the resource's current representation; for `calendar-sync` it is the original result as produced at `completedAt`, rebuilt from the sync run records. **Current state may differ**: if the student skipped an item after the first call and the client retries the old key, the replayed body still says `created` while the item is `skipped` and its event gone. Clients must treat a replayed body as history and re-read items and events for state. No operation is re-executed on replay.
- **Revision vs ETag.** `revision` is the content revision; it bumps only when content (kind, title, notes, dates, times, zone) changes, and `syncedRevision` refers to it. The `etag` changes on **any** mutation of the item (content, decision, `syncState`) and includes the resource id, so it is never accepted for another item. A stale approve/skip pair from two tabs therefore yields `412` for the loser.
- **Atomicity.** Decision and sync handlers re-check `approved`, `when.status == resolved` and the unchanged `revision` inside the transaction that locks the item rows (`SELECT ... FOR UPDATE` or a concurrency token), never on a value read earlier. Tests race PATCH, decision and sync (section 12).

### 2.2 TextPolicy (normative, one implementation, applied at write time and at every sink)

Every student-entered or document-derived text field goes through one `TextPolicy` on write, from the student path and the AI path alike: `title` (items, syllabus), `notes`, `confidenceReason`, `written.dateText`/`timeText`, `snippet.text`, `snippet.context.text` and pasted `text`.

- **Normalization:** Unicode NFC; whitespace runs collapse to one space (`title`, `dateText`, `timeText`, `confidenceReason`); lines in `notes`, snippet and context are LF-normalized; leading and trailing space removed.
- **Rejected on student-supplied fields (`400 validation_failed`):** NUL; CR and any C0/C1 control (so **no CR/LF/tab in titles**; `notes` allows LF only); DEL; U+2028/2029; unpaired surrogates; all bidi marks, embeddings, overrides and isolates (U+200E/200F, U+202A-202E, U+2066-2069, U+061C); zero-width and other format characters (Cf: U+200B-200D, U+2060-2064, U+FEFF, U+00AD, U+180E, U+FFF9-FFFB); tag characters; more than 4 consecutive combining marks. Lengths are counted in code points: `title` 200, `notes` 500 and at most 10 lines.
- **Document-derived text** (canonical text, snippets, AI titles) is **canonicalized by stripping** the same characters rather than failing the whole document (NUL in a pasted `text` is rejected), so the stored value always satisfies the schema. The schemas carry the same character classes as patterns.
- **Stored value is plain text, trusted nowhere.** It is **escaped at each sink**: HTML-escaped for any HTML sink (including a Google Calendar `description`), RFC 5545 TEXT escaping plus 75-octet folding for ICS, formula-prefix neutralization (`=`, `+`, `-`, `@`, tab, CR) for CSV, and no markdown in Docs. The Web UI renders text nodes only (C-28). Snippet and notes text must not flow into any calendar description without a separate contract and review. Strings such as `=HYPERLINK(...)` or `<img onerror>` are therefore legal stored text (fixtures `*--redteam-formula-title-as-text`, `*--redteam-html-title-as-text`) and are inert because of escaping, not because they are rejected. A URL the output sanitizer removed from model text cannot be re-added by the student into a field that reaches a sink, because sinks escape rather than link.
- Rejection fixtures: RLO, zero-width, isolates, tag characters, CRLF, NUL, tab, DEL, line separator, C1, 10 combining marks, leading/trailing space, bidi/CR/NUL/too-many-lines in notes.

### 2.3 Date and derivation rules (normative; server tests)

`localDate` is limited to the years 2000-2100 and must be a real calendar date (Feb 29 only in leap years). `termStart`/`termEnd` must satisfy `termEnd >= termStart` and a span of at most 400 days, else `400`. A stated zone abbreviation (for example "ET") is mapped only when consistent with the region of `defaultTimezone`; otherwise the zone is `timezone_assumed` and the item is `please_check`. A nonexistent local time (spring-forward gap) is `dst_ambiguous` and `please_check`. Recurrence requires `from <= until`, `until - from` at most 400 days, at most 20 `exceptions`, and occurrences are generated by code and **capped at 60 when written**, not only at sync. `endDate >= date` for every item. All-day `endDate` on the wire is **inclusive**; a real provider whose end is **exclusive** converts at its boundary (section 15, `calendar.v1` entry criteria).

## 3. Resources

Authoritative shapes are the schemas; this is a map.

| Resource | Schema | Notes |
|---|---|---|
| Syllabus | `syllabus.schema.json` | status, reason, `pollAfterSeconds`, `aiSource`, `demoSource`, `demoCalendar`, `originalRetained:false`, document facts, `batchApproveAllowed`, `notice` (version and attestation), `retention`, `context` (term and default zone), provenance, counts |
| Item | `syllabus-item.schema.json` | see section 4 |
| Unclaimed date | `syllabus-unclaimed-date.schema.json` | date-like span no item covers ("Dates we noticed but did not turn into items"); never auto-promoted |
| Calendar event | `syllabus-calendar-event.schema.json` | mock event, database only |
| Pages | `syllabus-page`, `syllabus-item-page`, `syllabus-unclaimed-date-page`, `syllabus-demo-calendar-page` | `{items, nextCursor}` |
| Problem | `syllabus-problem.schema.json` | |

### 3.1 Extraction status

`status` is one of `queued`, `extracting`, `proposing`, `ready`, `needs_text`, `partial`, `failed`.

- `queued`, `extracting`, `proposing` are in progress: `pollAfterSeconds` is an integer (suggest 2, backing off to 5), `statusReason` is null. All other statuses have `pollAfterSeconds: null`. The UX three steps map to `queued`/`extracting` (got file, finding dates) and `proposing` (getting items ready).
- `ready`: items available, `statusReason: null`.
- `needs_text`: no usable text layer; `statusReason` is `no_text_layer`. The student may paste text (a new syllabus) or enter items by hand. No OCR in this slice.
- `partial`: some pages or chunks processed; `statusReason` explains (`budget_limited`, `timeout`, `provider_unavailable`, ...) and `unprocessedRanges` and `document.unreadablePages` name the pages. The UI must disclose them; a partial result is never presented as complete.
- `failed`: nothing usable; `statusReason` is one of `unreadable_file`, `password_protected`, `too_long`, `provider_unavailable`, `timeout`, `invalid_output`, `disabled`, `budget_limited`.
- `statusReason` values: `no_text_layer`, `unreadable_file`, `password_protected`, `too_long`, `budget_limited`, `provider_unavailable`, `timeout`, `invalid_output`, `disabled`.
- Cancel is `DELETE /syllabi/{id}` (nothing is kept). There is no `cancelled` status.
- `aiSource: "mock"` forces `demoSource: true` (same visible "Demo source" rule as API_CONTRACTS section 12.2). Only Development/Test may produce `aiSource: "model"`, and only after PO-4 and G2 are cleared; the contract does not authorize it.

## 4. Item

Fields: `id`, `syllabusId`, `courseId`, `itemKey`, `origin` (`ai` | `student`), `kind`, `title`, `notes`, `when`, `recurrence`, `confidence`, `confidenceReason`, `flags[]`, `snippet`, `edited`, `decision`, `syncState`, `syncedRevision`, `revision`, `etag`, `createdAt`, `updatedAt`.

- **`kind`** is one of `assignment`, `exam`, `reading`, `office_hours`, `policy`, `other`. `policy` is AI-assigned only, informational ("Good to know"), has no date and is never approved or synced. The AI design's finer kinds are mapped by the server: `quiz` -> `exam`, `project_milestone` and `other_deadline` -> `assignment`, `class_meeting` and `no_class` -> `other` (provisional; the finer kind is not on the wire).
- **`when`** (strict): `status` is `resolved`, `needs_date` or `not_applicable`.
  - `written` holds what the document says (`dateText`, `timeText`, `parts{year,month,day,weekday}`), null when nothing was written. This is evidence for display, not the value used.
  - `date`, `endDate`, `time`, `endTime`, `timezone` are the **server-derived** local values. `timezoneSource` is `stated_in_source`, `course_default` or `student`. `derivation` is `explicit_full`, `year_from_term`, `relative_to_anchor` or `student_entered`.
  - `resolved` requires `date`, `timezone`, `derivation`; `unknownReason` is null.
  - `needs_date` (first-class): `date`, `endDate`, `time`, `endTime`, `derivation` are null, `allDay` is false, and `unknownReason` is set: `not_stated`, `tba`, `relative_unresolvable`, `conflicting`, `ambiguous_format`, `table_unreadable`, `date_not_verifiable`. For `conflicting`, `alternatives` lists up to 3 candidate dates; the server never picks one.
  - `not_applicable` is used only by `policy`.
  - Relative dates (`relative_to_anchor`) are resolved only against a term the **student** supplied at creation; a term the AI read from the text is not trusted. Confidence is capped at `probably_right`.
- **`recurrence`** (weekly only): `byDay`, `startTime`, `endTime`, `from`, `until`, `untilSource` (`stated`, `term_end`, `student`), `exceptions[]` (dates). `when.date` is the first occurrence.
- **`confidence`** is a **server-derived enum** `looks_right`, `probably_right`, `please_check`. There is no numeric score on the wire (the schema forbids the field); `confidenceReason` is a short fixed-vocabulary explanation. `looks_right` requires an explicit full date, a verified snippet and no conflict flag. A resolved item with `tentative_language`, `weekday_mismatch`, `table_layout_risk`, `outside_term`, `injection_suspect`, `supersession_cue` or `low_text_quality` is `please_check` (schema-enforced for the last three). Confidence is **re-derived on every edit**: a student-entered date is never `looks_right` (schema-enforced) and is not batch-eligible. `looks_right` is a statement that the value was derived from the text, **not** that the text is true; its label must not read as "verified".
- **`flags`** vocabulary: `year_inferred`, `relative_date`, `timezone_assumed`, `time_missing`, `conflicting_sources`, `tentative_language`, `table_layout_risk`, `section_ambiguous`, `range_end_inferred`, `weekday_mismatch`, `date_not_verifiable`, `until_from_term`, `low_text_quality`, `duplicate_candidate`, `outside_term`, `dst_ambiguous`, `injection_suspect`, `supersession_cue`. Server flags win; the model cannot remove them.
- **`snippet`**: `{text, start, end, page, precision, context}`; `precision` is `exact` (offsets into the stored canonical text; `end - start` equals the text length, tested) or `line` (redaction prevented an exact match; offsets cover the line range). Null only for `origin: "student"`; required for `origin: "ai"`. **Offsets are UTF-16 code unit offsets** into the canonical text (the .NET `string` index), never code point or byte offsets, and **all slicing is done in .NET, never in SQL** (PostgreSQL `substr` counts code points, so an emoji before the snippet would shift every later offset; fixture `syllabus-item--snippet-emoji-before` and a contract test pin this). `context` is the server-sliced **context window** (the full line or paragraph around the snippet, at most 1,500 UTF-16 code units, with `start <= snippet.start` and `snippet.end <= end`); it is how the student judges a verbatim snippet now that the original is gone. It is `null` only after the canonical text has been purged (section 5.3). There is no "view in document" and none will be added (section 13 row 27).
- **`decision`** is `undecided`, `approved`, `skipped`. **`syncState`** is `not_synced`, `synced`, `changed_since_sync`, `sync_failed`.
- Cross-field rules enforced by the schema (and re-checked by the server): a `needs_date` or `not_applicable` item can never be `approved`; any `syncState` other than `not_synced` requires `decision: "approved"` and a `syncedRevision`; `kind: "policy"` if and only if `when.status: "not_applicable"`; student items have no snippet and never `policy`; `allDay` forbids times; `endTime` requires `time`.
- **`edited`** is true once the student changed any value. The original AI proposal remains in provenance (invariant 5) and is not on the wire in v1 ("Reset to what we found" is deferred, section 15).
- Fine-grained rule: the item **title and kind are our wording** (a paraphrase), the snippet is the evidence. The review row shows title and snippet together; a deterministic server check flags a `kind` whose keywords contradict the snippet as `please_check` (flag only).
- Default length: a timed item with no `endTime` becomes a 60 minute event (decision 7); the event carries `endDerived: true`. A timed event crossing midnight is allowed.

### 4.1 `itemKey` (idempotency basis)

`itemKey = "ik_" + first 32 characters of base64url(SHA-256(UTF-8("tether.syllabus.item.v1\n" + syllabusId + "\n" + itemId)))`, no padding. Derived from (syllabusId, itemId) only, so it never changes on edit, undo or re-approval, and a future Google provider reuses it as its idempotency key (`extendedProperties` or equivalent). Contract tests recompute it for every fixture. Recurring occurrences use `itemKey + ":" + occurrenceDate`. `itemKey` is an idempotency basis only; it is never an identifier any endpoint accepts and never an authorization token.

## 5. REST endpoints

All user-scoped; a foreign or missing id is `404 not_found`. Ownership of the course/syllabus is verified before any file bytes are read.

| Method | Path | Request | Success | Errors |
|---|---|---|---|---|
| POST | `/courses/{courseId}/syllabi` | multipart: exactly one `file` part + one `metadata` part of at most 4 KB (`syllabus-create-request`, no `text`), **or** JSON `syllabus-create-request` with `text`. `noticeVersion` and `attestation` required. `Idempotency-Key` **required** | 202 `Syllabus` (`queued`) + `Location` | 400, 404, 409 `duplicate_syllabus` / `extraction_in_progress` / `syllabus_limit_reached`, 413 `too_large`, 415 `unsupported_type`, 429 |
| GET | `/courses/{courseId}/syllabi?cursor&limit` | | 200 page of `Syllabus` | 404 |
| GET | `/syllabi/{id}` | | 200 `Syllabus` + ETag (poll target) | 404 |
| DELETE | `/syllabi/{id}` | none (no query parameters) | 204; hard delete of everything in section 5.2, including the mock events; cancels a running job | 404 |
| GET | `/syllabi/{id}/items?cursor&limit&decision&kind&needsDate` | | 200 page of `Item` | 400, 404 |
| POST | `/syllabi/{id}/items` | `syllabus-item-create-request`, optional `Idempotency-Key` | 201 `Item` (`origin: "student"`) | 400, 404, 409 `invalid_state` while not `ready`/`partial` |
| GET | `/syllabi/{id}/items/{itemId}` | | 200 `Item` + ETag | 404 |
| PATCH | `/syllabi/{id}/items/{itemId}` | `syllabus-item-update-request` (JSON merge-patch) + `If-Match` | 200 `Item` | 400, 404, 412, 428, 409 `item_not_syncable` (policy) |
| DELETE | `/syllabi/{id}/items/{itemId}` | `If-Match` | 204; only `origin: "student"` items; removes any mock event | 404, 409 `student_item_only`, 412, 428 |
| POST | `/syllabi/{id}/items/{itemId}/decision` | `syllabus-item-decision-request` + `If-Match` | 200 `Item` | 404, 409 `item_needs_date` / `item_not_syncable` / `acknowledgement_required`, 412, 428, 503 |
| POST | `/syllabi/{id}/decisions` | `syllabus-decision-batch-request` (`itemEtags`: object keyed by item id) | 200 `syllabus-decision-batch-result` (per item) | 400, 404 |
| POST | `/syllabi/{id}/calendar-sync` | `syllabus-calendar-sync-request` (`itemRevisions`: object keyed by item id) + `Idempotency-Key` **required** | 200 `syllabus-calendar-sync-result` | 400, 404, 409 `invalid_state`, 429, 503 |
| POST | `/syllabi/{id}/calendar-sync/remove` | `syllabus-calendar-remove-request` | 200 `syllabus-calendar-remove-result` | 400, 404 |
| GET | `/courses/{courseId}/demo-calendar?from&to&cursor&limit` | `from`, `to` are `YYYY-MM-DD` | 200 page of `Calendar event` | 400, 404 |
| GET | `/syllabi/{id}/unclaimed-dates?cursor&limit` | | 200 page of `Unclaimed date` | 404 |
| GET | `/syllabi/{id}/export` | | 200 `syllabus-export` (owner-scoped, on demand; headers in section 8) | 404, 429 |

Notes
- **Duplicate content.** Identical content hash in the same course returns `409 duplicate_syllabus` with `existingSyllabusId` in the problem body, unless the request sets `allowDuplicate: true`. The lookup is `(userId, courseId, contentSha256)`; a duplicate check never reveals another user's upload. `allowDuplicate: true` counts as a new extraction against the daily quota and the AI budget.
- **Quotas.** One extraction in flight per user (`409 extraction_in_progress`, enforced by a unique partial index on `(user_id)` for statuses `queued`, `extracting`, `proposing`); at most 5 syllabi per course and 50 active per user (`409 syllabus_limit_reached`). Syllabus creation is limited to 10 per day per user, counted at creation and **never refunded** on cancel or failure.
- **Term and zone.** `termStart`/`termEnd` are optional and student-supplied at creation (rules in section 2.3); `defaultTimezone` is required (browser or profile value, validated against a server allow-list). Changing them later is deferred (delete and re-add).
- **Notice and attestation.** `noticeVersion` and `attestation` (`accepted: true`, `acceptedAt`) are required on create and stored on the syllabus as `notice` (`version`, `attested`, `attestedAt`) with a content-free audit row (user, syllabus id, content hash, version, time; no text). The notice and attestation wording is a **DRAFT placeholder owned by the Product Owner and counsel (P2-D5)**; the UI must not imply Tether verified that the student may upload the document. The only document-type sanity check is a **warning** ("this does not look like a syllabus"); nothing classifies the student or the document into sensitive categories, and disability or health information is never logged or inferred.
- **Pagination of items** is by `(effective date, id)`; items needing a date sort last.
- **No malware scan.** No uploaded file is ever served, rendered or downloaded back, so v1 needs no AV scan; the risk is parser exploitation (section 5.1). If original download is ever added it needs AV and its own review (section 15).

### 5.1 Upload and extraction hardening (normative; the extractor may not merge without the hostile fixtures in section 12)

1. **Sniff, do not trust.** The type is decided by magic bytes (PDF `%PDF-`, DOCX = ZIP with `[Content_Types].xml` and `word/document.xml`, plain text). An extension, `Content-Type` and signature that disagree are `415 unsupported_type`. The client filename is discarded at parse time: never stored, logged, echoed in `detail`, or used for a path.
2. **Streaming caps, request shape.** The 10 MB limit is enforced **while reading** (Kestrel `MaxRequestBodySize` per endpoint, `MinRequestBodyDataRate`, request timeout), never by trusting `Content-Length`. Exactly one `file` part and one `metadata` part (at most 4 KB); anything else is `400`. The request is rejected before buffering. A global cap bounds concurrent uploads in flight.
3. **ZIP/DOCX.** Caps on entry count, per-entry and total uncompressed bytes, expansion ratio (zip bomb), path depth; any `..` or absolute path rejects the file. Macro or VBA parts (`vbaProject.bin`, macro-enabled content types), OLE and embedded packages reject the file. Relationships with `TargetMode="External"` are ignored and never fetched.
4. **XML (DOCX parts).** DTDs prohibited (`DtdProcessing.Prohibit`), `XmlResolver = null`, entity-expansion and character limits (`MaxCharactersInDocument`); a DOCTYPE or external entity (XXE, billion laughs) rejects the file. Verify on the chosen parser.
5. **PDF.** Never rendered. `/OpenAction`, `/Launch`, `/URI`, `/JavaScript` and `/EmbeddedFile` are ignored (never followed or extracted). Page count is read from the page tree and compared with 60 **before** text extraction; decompression is bounded. Encrypted becomes `failed` with `password_protected`. Hidden text (render mode 3, background-colored, off-page, under about 1 pt) is **dropped before the AI sees it** and counted in `document.hiddenContentRemoved`.
6. **Plain text and paste.** NUL rejects; UTF-8 or UTF-16 with BOM only. Pasted text is treated **exactly like a file**: same TextPolicy canonicalization (section 2.2), limits, retention (5.3), deletion and hash.
7. **Output cap.** Extraction stops at 50,000 characters and the page cap **during** extraction, not after.
8. **Isolation.** Parsing runs in a separate process or container with **no network**, a read-only filesystem except a scratch directory, non-root, and bounded CPU, memory and wall time (60 s). A worker crash or timeout is `failed` with `unreadable_file` or `timeout`; parser exceptions are mapped to codes before logging and never logged or returned as text.
9. **Transient bytes.** The uploaded bytes live only in a **non-logged ephemeral store** excluded from backups and WAL/replication (recommended implementation: a PostgreSQL `UNLOGGED` table, `docs/data/schema-syllabus-p2.md`, dumps taken with `--no-unlogged-table-data`; tmpfs is the alternative; never object storage with versioning, never a job payload row, never a log). They are deleted when the worker finishes (success, failure, cancel), on `DELETE`, and by a sweeper with a hard ceiling of **within 1 hour**, tested with a killed worker. This is the only retention statement for bytes: the earlier 24 h figure in P AC5 is superseded and the conflict is resolved to 1 hour. Because the original is not kept, retrying a failed extraction needs a new upload; after `ready` the canonical text and items persist (5.3). An UNLOGGED table is still part of the database: its residual risk (live on disk until deleted, included by a plain `pg_dump`) is accepted only with those backup settings and is a security-engineer sign-off item.
10. **Extraction cache.** None across users. Content hash, any extraction cache and idempotency basis are scoped by `(userId)`; there is **no cross-user** reuse of extraction results, canonical text or items. A shared cache of model output needs its own review and is out of scope for v1.

### 5.2 Delete (exhaustive, one transaction)

`DELETE /syllabi/{id}` hard-deletes in one transaction: items, revisions, approvals, unclaimed dates, canonical text and lines, snippets and context, extraction jobs and their ephemeral bytes, idempotency records, the dedupe hash row, sync runs, and **the mock calendar events (always)**. The `keep` option is dropped from v1: there are no orphaned events. A running job is cancelled; every job writer re-checks the syllabus still exists, so a late result cannot recreate data (extends C-21, with a late-writer test). Course archive/delete and account delete cascade the same way and are tested. Only content-free rows survive (AI usage ledger totals with scope id and cost; audit rows with ids, counts and times), and they are listed in the data model. Backups roll off on the platform's schedule (target at most 30 days including backups; a restore can resurrect a deleted syllabus until deletions are re-applied, see `docs/data/schema-syllabus-p2.md`). Copies already sent to a model vendor (when real) or exported files are not deleted by this call, and the delete confirmation must say so. Test: delete, then query every table, cache, queue and temp path for the syllabus id and its content hash.

### 5.3 Retention of canonical text (provisional, PO-owned)

The canonical extracted text is the stored source record (invariant 5). It is purged when the syllabus is deleted and otherwise **after a provisional 180 days from the student's last activity** on that syllabus (view, edit, decision, sync); the date is `retention.canonicalTextPurgeAt` and moves with activity. This is recorded as a follow-up of the open Product Owner decisions D4/D11 (and PO-5). A background purge job runs the purge and is tested with time shifting. Purging removes the canonical text and every `snippet.context`; the short `snippet.text` stays as the student's approval evidence until the syllabus is deleted (open for the PO to shorten). The notice must say the text of the document is kept until deleted or purged. Pasted text is treated exactly like a file.

## 6. Commands and rules

### 6.1 Decisions (approve, skip, undo)

- `decision: "approved"` requires `when.status: "resolved"`; otherwise `409 item_needs_date` (or `409 item_not_syncable` for `policy`). Enforced on the server **inside the locked decision transaction** (section 2.1), not only in the UI or on a value read earlier. Approving never writes to any calendar.
- Approving a `please_check` item needs `acknowledgeCheck: true`, else `409 acknowledgement_required`. `acknowledgeCheck` is a client-asserted flag that proves only that the client sent it.
- Choosing one of a `conflicting` item's `alternatives` is only done by PATCH of `date`; it is never an approve.
- Approval is recorded in the content-free audit log (user id, item id, revision, timestamp) and applies to the item `revision` that `If-Match` named.
- Undo (`undecided`) and skip (`skipped`) never expire and never time out.
- Setting an item that is `synced`, `changed_since_sync` or `sync_failed` to `undecided` or `skipped` **also removes its mock events in the same transaction** and returns `syncState: "not_synced"`. If the removal fails, the whole command fails with `503 provider_unavailable` and nothing changes. The calendar therefore never holds an item the student has withdrawn. (Mock only: a real provider needs a `pending_removal` state, section 15.)
- **Batch** (`POST /syllabi/{id}/decisions`): `itemEtags` is a JSON object keyed by item id (1 to 100 entries) with the etag the student saw, no "all" form. Because ids are object keys, **duplicate ids are not representable**, and a duplicate member name is `400` (section 2.1), so one id cannot be approved and skipped in one call. Ids that are not in the path syllabus and owner give the same per-item `not_found` as nonexistent ids. **Each item is its own transaction**; the response lists every outcome, and a mid-batch failure never leaves an event for a withdrawn item. `approve` is accepted only for `resolved` `looks_right` items **of a document whose `batchApproveAllowed` is true**; otherwise the item is `rejected` with `batch_not_eligible` (or `item_needs_date`). A stale etag is `rejected` with `precondition_failed`. The response is 200 with per-item results and counts and never claims success when any item was rejected. `skip` and `undecide` follow the same removal rule as above. Per-user (not only per-syllabus) rate limits apply to batch calls (section 11).
- **Hostile-but-verbatim documents.** A document with `injectionSuspect: true`, `hiddenContentRemoved > 0` (`hiddenContentDropped`) or any `low_text_quality` has `batchApproveAllowed: false` (schema-enforced), and every item from it is `please_check`: batch approve returns `batch_not_eligible` for the whole syllabus and each item needs an individual, acknowledged approve. See section 8.

### 6.2 Edit

- Allowed on any non-`policy` item: `kind` (not to `policy`), `title`, `notes`, `date`, `endDate`, `time`, `endTime`, `allDay`, `timezone`. Merge-patch rules: absent = unchanged; `null` is legal only on fields the schema marks nullable (`notes`, `endDate`, `time`, `endTime`); `title: null`, `kind: null` and `date: null` are `400` (a date cannot be cleared). Setting `date` on a `needs_date` item makes it `resolved` with `derivation: "student_entered"`, `timezoneSource: "student"`, `edited: true`, and it stays `undecided`. `timezone` alone on a `needs_date` item does not change `when.status`. Edits to a `policy` item are `409 item_not_syncable`.
- Student edits are exempt from source-derivability checks (the student is the authority) but still need real calendar dates in the 2000-2100 window and `endDate >= date`. Text edits pass TextPolicy (section 2.2).
- Editing an **approved, not yet synced** item returns it to `undecided` (approval belongs to the revision that was approved). Editing a **synced** item keeps it `approved`, sets `syncState: "changed_since_sync"` and does **not** touch the calendar; the next explicit `calendar-sync` shows and applies the change (`outcome: "updated"`).
- Content edits bump `revision`; every mutation changes the etag (section 2.1).

### 6.3 Calendar sync (mock)

- Request names `itemRevisions` (item id to the revision the student reviewed) and carries `confirm: {provider: "mock", acknowledgedDemo: true}`. Nothing is written without it. Only items that are `approved`, `resolved` and at the named revision are eligible; every other id yields `outcome: "not_eligible"` (or `failed` with `precondition_failed` when the revision moved) with `errorCode` (`item_not_approved`, `item_needs_date`, `item_not_syncable`, `precondition_failed`, `not_found`) and **zero writes**. The check and the write happen in one transaction that locks the item rows (`SELECT ... FOR UPDATE` or a concurrency token), so a concurrent edit, skip or withdraw cannot leave an event for a withdrawn item.
- Outcomes per item: `created`, `updated` (an existing event for the same `itemKey` changed), `unchanged`, `failed`, `not_eligible`. Overall `status`: `all_succeeded`, `partial`, `failed`, `nothing_to_do`. HTTP is 200 for every outcome; the result never reports full success when any item failed. A failed item stays `approved` with `syncState: "sync_failed"`; a retry does not duplicate the others.
- **Caps.** At most 1,000 events per syllabus and a per-user ceiling (section 10) are enforced at sync; an item whose occurrences would exceed them fails with `event_limit_reached` and writes nothing for that item. Sync accepts up to 100 items per call and writes at most 500 events per call; the rest are `failed` with `event_limit_reached` and can be retried in another call. The export caps equal these write caps.
- **Idempotent.** The mock event for an item is keyed by `itemKey` (and the occurrence date for recurrences). Running sync any number of times, or replaying the same `Idempotency-Key`, yields one event per item (`unchanged` on repeat). A replay returns the original body with `Idempotency-Replayed: true`; its states can be stale by design (section 2.1).
- `calendar-sync/remove` deletes mock events and returns items to `approved` / `not_synced` (the decision is kept). Remove then re-add produces exactly one event. Outcomes: `removed`, `not_present`, `failed`. Ids outside the path syllabus are per-item `not_found`.
- Events are stored with provider, event id, item id, `syncedRevision` and time, in the database only (C-22). Fault injection (timeout, 500, rate limit) is allowed only in Development/Test and fails startup elsewhere (same rule as `Speech:Replay:Faults`).

## 7. Real-time and background work

No hub. A background job (existing Postgres jobs pattern) runs extraction; the client polls `GET /syllabi/{id}`. The job has a **lease with heartbeat and a reaper** (reuse the reservation-reaper pattern), so a crashed or stuck worker releases the user's single in-flight slot; a unique partial index on `(user_id)` for `queued`/`extracting`/`proposing` prevents two concurrent creates from starting two jobs. The daily quota counts at creation and is never refunded on cancel or failure. A global cap on concurrent extractions and on queue depth answers `429 rate_limited` (or `503`) when full. Deleting a syllabus or its course cancels the job, and every writer re-checks the syllabus still exists so a late job cannot recreate data (extends C-21). Course archive and purge cascade with DB-level `ON DELETE CASCADE` (database-architect to confirm the allow-list entry).

## 8. Privacy, security and logging requirements (normative for implementation)

- The AI sees only canonical text (as untrusted data) plus validated server context (term dates, IANA zone). Never tokens, the student's calendar, ids, names, filenames or other documents; no tools. The Calendar provider never receives AI output without student approval.
- **Hostile-but-verbatim content.** Prompt-injection text in a document is inert (A section 8), but `snippet.text` of a hostile document may still be a false-yet-verbatim date ("Exam 1: Oct 19 (moved from Oct 12)", "the final is NOT on Dec 9"). Trust is not the mitigation. Required, in order: (a) `injectionSuspect`, `hiddenContentDropped` or `low_text_quality` caps every item at `please_check` and sets `batchApproveAllowed: false`; (b) **hidden text is dropped before the AI sees it** (not merely counted); (c) the **server-sliced context window** (`snippet.context`) lets the student read the surrounding line; there is no "view in document"; (d) a deterministic **negation/supersession** cue list ("not", "moved", "changed", "instead", "formerly", "no longer", "cancelled", "no later than") sets the `supersession_cue` flag and forces `please_check`; (e) mandatory individual approval for anything not `looks_right`. Red-team fixtures (misleading verbatim date, hidden text, supersession) are required before the AI seam merges (section 12).
- **Budgets (when a real model exists).** The syllabus workflow has a **separate budget pool** (scope `SyllabusExtract`), not shared with lecture features, with a ceiling for the pool and a protected floor for live-lecture workflows so syllabus load cannot starve them; a per-user daily spend cap (not only a count), a per-job cap, a global kill switch and spend alert, and the whole-job pre-flight check from A section 9. `allowDuplicate` counts as a new extraction. Sign-up throttling is an input. Exhaustion surfaces as `partial`/`failed` with `budget_limited`, never silently. Real Claude stays off until PO-4 and G2.
- **Logging.** Logs carry id hashes, sizes, counts, versions and validation codes. Never document text, snippets, context, titles (including the syllabus `title` supplied at create), notes, prompts, completions, **filenames** (multipart `Content-Disposition`, form-file models) or **parser exception text** (PDF/XML libraries embed object text, entity names and paths in messages; exceptions are mapped to codes before logging). `ProblemDetails.errors` never echoes a rejected value. EF sensitive-data logging stays off; job payloads, the AI usage ledger and OpenTelemetry attributes carry no item text. The log canary (C-23) applies: a canary placed in file content (a PDF stream, a DOCX table cell, a paste, a filename), in a syllabus title and in an item title/notes must not appear in any log, trace or metric sink at Trace level with EF sensitive logging on, including under fault injection (parser crash, XXE attempt, invalid AI output, DB failure). This canary test is a required check before the Syllabus module merges.
- **Export.** Owner-scoped, generated on demand, never a public or signed URL; JSON only; headers `Cache-Control: no-store`, `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff`; rate-limited (6 per minute per user). Export holds items, snippets and events (snippets can carry instructor emails and phones that the AI never saw); it holds no original file because none is retained. Future ICS/CSV exports inherit TextPolicy escaping and need their own review.
- **Audit.** A content-free audit event (user, ids, counts, time; SEC-028) is written for create, approve, sync, remove, delete and export.
- **Dropped content.** Hidden content (DOCX `vanish` runs, comments, metadata, PDF hidden text) is dropped and counted in `document.hiddenContentRemoved`.

## 9. Error codes

lecture.v1 codes keep their meaning: `validation_failed` (400), `unauthenticated` (401), `csrf_failed` (403), `not_found` (404), `invalid_state` (409), `concurrency_conflict` (409), `precondition_required` (428), `precondition_failed` (412), `rate_limited` (429, `Retry-After`), `provider_unavailable` (503). Syllabus additions:

| code | HTTP | Meaning |
|---|---|---|
| `unsupported_type` | 415 | content signature is not PDF, DOCX or plain text |
| `too_large` | 413 | over the byte, page or character limit (limit named in `detail`) |
| `duplicate_syllabus` | 409 | same content already in this course (`existingSyllabusId`) |
| `extraction_in_progress` | 409 | the user already has an extraction running |
| `syllabus_limit_reached` | 409 | per-course or per-user syllabus cap |
| `item_needs_date` | 409 | approve or sync of a `needs_date` item |
| `item_not_syncable` | 409 | approve, sync or kind change on a `policy` item, or sync of an ineligible item |
| `item_not_approved` | 409 / per item | sync of an item that is not `approved` |
| `acknowledgement_required` | 409 | approving `please_check` without `acknowledgeCheck: true` |
| `batch_not_eligible` | per item | batch approve of an item that is not `resolved` and `looks_right` |
| `student_item_only` | 409 | delete of an AI item (use skip) |
| `event_limit_reached` | per item | sync would exceed the per-call, per-syllabus or per-user event cap (section 6.3); nothing is written for that item |

Per-item codes inside 200 results (`itemErrorCode`): `item_needs_date`, `item_not_syncable`, `item_not_approved`, `batch_not_eligible`, `acknowledgement_required`, `precondition_failed`, `not_found`, `provider_unavailable`, `invalid_state`, `event_limit_reached`.

Error hygiene: `detail` is a fixed string per code; no parser or library text, no detected-type echo, identical `404` for foreign and missing ids, and a parser failure is a `failed` syllabus with `unreadable_file`, never a `500` body.

## 10. Limits (proposals, configurable, not measured; devops and PO own the values)

| Limit | Proposal |
|---|---|
| Upload size | 10 MB |
| Pages | 60 (schema `pageCount` maximum) |
| Extracted or pasted characters | about 50,000 (schema maximum 50,000; over it is `too_large`) |
| Items per syllabus | 300 items per syllabus: at most 200 AI items plus at most 100 student items (export cap equals this) |
| Events | 1,000 events per syllabus, per-user ceiling 5,000, 500 written per sync call (export cap equals the per-syllabus cap) |
| Unclaimed dates | 200 per syllabus |
| Syllabi | 5 per course, 50 active per user |
| Metadata part | 4 KB; exactly one `file` part |
| Transient upload bytes | deleted on completion, failure, cancel or delete, ceiling within 1 hour |
| Canonical text retention | until delete, or provisional 180 days from last student activity (PO-owned) |
| Batch size | 100 item ids |
| Recurrence occurrences synced | 60 per item |
| Extraction time | 60 s extract, 120 s AI stage (A and P) |
| Page `limit` | default 50, maximum 200 |

## 11. Rate limits (proposals)

Existing global limits apply (120/min per user, 30/min writes). Additional: syllabus creation 10 per day per user, **1** extraction in flight per user (stricter than P's 3), `calendar-sync` 6 per minute per syllabus **and** a per-user limit across all syllabi, `decisions` (batch) with a per-user limit as well as the write bucket, `export` 6 per minute per user, a global cap on concurrent uploads and extractions, and a Kestrel body-rate limit on the upload route. Excess is `429 rate_limited` with `Retry-After`. AI budgets (PO-4) surface as `partial`/`failed` with `budget_limited`, never silent.

## 12. Contract tests (exist now) and tests still required

`src/api/tests/Tether.ContractTests`: `ContractFixtureTests` registers `syllabus.v1` beside `lecture.v1` and validates every flat fixture named `{schema}--{case}.json` in `src/shared/contracts/fixtures/valid` (must pass) and `/invalid` (must be rejected). The harness reads the two folders non-recursively, so there is no `fixtures/redteam/` folder: red-team fixtures are flat and carry `redteam` in the case name (`*--redteam-*`). `SyllabusContractTests` checks: schema strictness, a fixture pair per schema, enum confidence with no numeric score, itemKey recomputation and a pinned vector, exact-snippet and context offset arithmetic in UTF-16 code units including an emoji before the snippet, approval invariants on every valid item, doc status text, every code and enum value named in this file, that only `mock` is expressible and no schema names Google or orphaned events, that every model-source fixture is labelled `shape-only`, the required red-team fixtures exist, batch and sync ids are object keys (duplicates unrepresentable) and duplicate member names fail to parse, the idempotency key format, cap constants against this file, and the presence of every normative statement in sections 2 and 5.

**Server-side tests still needed** (JSON Schema cannot express them; nothing here is implemented): real calendar validity (Feb 29 on a non-leap year, Feb 30), DST gap and overlap, term crossing New Year, inverted term, a 9999 date; `endDate >= date`; zone allow-list membership; exactly one of file/text; unknown and read-only members rejected on every request; the **route-by-route ownership table** (every route, two users with overlapping ids, plus a same-user mismatched parent syllabus) with identical status, body and timing for foreign and missing ids, and the evaluation order of section 2.1; cross-origin multipart POST and DELETE rejected; idempotent sync x3 yields one event; replay of a sync after a later skip returns the original body with `Idempotency-Replayed: true`; a key reused on another syllabus is `400`; user B reusing user A's key does not collide; withdrawal removes the mock event; concurrent PATCH/decision/sync and the stale-tab approve/skip pair; delete then scan every table, cache, queue and temp path (no document bytes or text after `ready`, `failed` and `DELETE`); purge job with time shifting; late-writer-after-delete; killed worker and the 1-hour sweeper; log canary under fault injection; forced-exception test that no parser text reaches `detail`; no Google or HTTP calendar client registered and outbound network denied in the test run; `aiSource: "model"` rejected unless both human gate flags are set by configuration; load test with simulated users against the syllabus budget pool; maximal-data export validates against its schema.

**Hostile-file fixtures required before the extractor merges** (binary, generated by the test project, not stored as schema fixtures): zip bomb, nested DOCX, XXE DOCX, billion-laughs XML, external-link DOCX, macro DOCX, JS PDF, encrypted PDF, huge page tree, Flate bomb, polyglot, a .txt that is binary, UTF-16 BOM text, NUL text, a body that lies about `Content-Length`, several `file` parts, a giant `metadata` part.

**Red-team syllabus fixtures required before the AI seam merges** (the schema-visible ones exist now as flat `*--redteam-*` fixtures): misleading verbatim date with supersession cue, hidden or tiny text, instruction-like text in a table cell, a page of binary noise.

## 13. Reconciliation (where P, A and U disagree)

Rule: choose the safer option; Lead Architect decisions in section 1 win.

| # | Topic | P (product) | A (AI design) | U (UX) | Choice |
|---|---|---|---|---|---|
| 1 | Item kinds | assignment, exam, reading, office_hours, policy, other | 9 kinds (quiz, project_milestone, class_meeting, no_class, other_deadline) | exam, quiz, assignment, reading, project, other | The P/Lead six. A's kinds mapped server-side (section 4); U's Quiz/Project labels cannot be shown as such (open question Q4). |
| 2 | Confidence | 3 buckets, numeric internal | enum | 3 labels, raw value under Details | Enum only, nothing numeric on the wire (Lead). U's "Details" number is dropped. |
| 3 | Item state | one `status` of 7 values | `review.state` + `calendar.approved` | `decision` + `syncState` | `decision` x `syncState` x `edited` x `origin` as separate fields; invalid combinations rejected by schema. |
| 4 | Job status | queued, extracting, extracted, partial, no_text, failed | queued, extracting_text, analyzing, ready_for_review, needs_text, partial, failed, cancelled | steps | Lead list: `queued`, `extracting`, `proposing`, `ready`, `needs_text`, `partial`, `failed`. Cancel is delete. |
| 5 | Syllabi per course | several | n/a | one, with Replace | Several (Lead). U's Replace flow becomes add another plus delete the old one; U must adapt. |
| 6 | Original file | stored as source record | stored by content hash | open (Q4) | Not retained (provisional D4): the safer data position. Costs: no original export, retry needs re-upload; the context window replaces any document viewer (row 27). Transient bytes: within 1 hour (row 28). |
| 7 | Undo window | batch undo 15 min | n/a | never expires | Never expires (Lead; also matches WCAG 2.2.1). |
| 8 | Undo of a synced item | returns to `approved`, event removed | n/a | "Remove from calendar" | Two commands. Withdraw (skip/undecide) removes the event and returns `not_synced`. `calendar-sync/remove` removes the event and keeps `approved`. The calendar never holds a withdrawn item. |
| 9 | Needs-date error | 400 `item_needs_date` | n/a | button disabled | `409 item_needs_date` (state conflict, within Lead's 409/422); server-enforced. |
| 10 | ETag conflict | 409 `conflict` | n/a | n/a | Existing codes: `412 precondition_failed`, `428 precondition_required`. |
| 11 | Caps | 10 MB, 60 pages, 200,000 extracted, 100,000 pasted | 10 MB, 40 pages, 24k single call, 72k max | 10 MB, 50,000 chars | 10 MB, 60 pages (Lead), **50,000 characters** (lowest, also bounds AI cost). Effective page cap is therefore usually reached by characters first; A's 40 pages not adopted. |
| 12 | Concurrent jobs | 3 | 1 in flight | n/a | 1 in flight per user (stricter). |
| 13 | Reminders | non-goal | n/a | optional radio, default None | None in v1. No reminder field exists (schema rejects it); U's confirm-step radio group must be removed. |
| 14 | Conflicting dates | one item, needs-date-like, both visible | two items, `conflicting_sources` | n/a | One item, `needs_date`, `unknownReason: conflicting`, up to 3 `alternatives` (cannot be approved until the student sets a date). |
| 15 | Batch approve | explicit ids with revision; "high-confidence" group action | n/a | client picks "clear ones" | Explicit ids and etags; server enforces `resolved` + `looks_right` for batch approve (`batch_not_eligible`). Single approve of other confidences is allowed (`please_check` needs acknowledgement). |
| 16 | Term dates | student confirms; "Confirm your term" step | course record dates, AI-read anchors | optional "First day of classes" at upload | Student-supplied `termStart`/`termEnd` at creation only; AI-read terms not trusted. Later change deferred. |
| 17 | Edit of approved item | stays marked "changed" | n/a | edit never changes decision | Unsynced approved: back to `undecided` (approval is of a revision). Synced: stays `approved`, `changed_since_sync`, calendar untouched until explicit sync. Deviates from U for the unsynced case; safer. |
| 18 | Student-added items | with `origin=student`, no date required? | n/a | Q9 open | Allowed, date **required** (no undated student items). |
| 19 | Default event length | n/a | n/a | 1 h | 60 minutes (Lead). |
| 20 | Time zone | profile or browser | course default + stated zone | Q5 open | Required `defaultTimezone` per syllabus (IANA); `timezoneSource` shows origin; ambiguous abbreviations never resolved. |
| 21 | Export | JSON plus ICS or CSV, plus original | n/a | scope open | JSON only now. ICS/CSV deferred; original impossible (not retained). |
| 22 | Progress | poll | optional SignalR events | polling hidden | Polling only (Lead). `syllabus.job.*` events from A section 13 not adopted. |
| 23 | Snippet size | n/a | model snippet <= 240 chars | display about 200 | Schema allows 600 server-sliced characters; the UI truncates. Offsets are absolute in the stored canonical text. |
| 24 | Delete with synced events | asks, default describes | n/a | demo events stay | Superseded by row 25: no choice, delete always removes the mock events. |

### 13.1 Lead decisions from the security review (2026-10-28)

Each row is a **Lead decision, provisional where PO-owned**. These are defaults to build against, not Product Owner approvals. Finding ids refer to `docs/security/p2-contract-review.md`.

| # | Topic | Lead decision (provisional where PO-owned) | Findings |
|---|---|---|---|
| 25 | Delete and calendar events | The calendar-events `keep` option on delete is dropped. `DELETE /syllabi/{id}` always removes the mock events and all derived data (section 5.2). Q2 is closed. | P2-20 |
| 26 | Canonical text retention | Purged on delete, otherwise after a **provisional 180 days from last student activity** (PO-owned; **open D4/D11 follow-up**, with PO-5). Pasted text is treated exactly like a file. | P2-24 |
| 27 | "View in document" | Gone for good. Reliance is on the server-sliced snippet with a context window (`snippet.context`). Open question 3 of the review is closed. | P2-08 |
| 28 | Upload hardening and bytes | Normative section 5.1: content sniffing over extension, streaming size caps, zip-bomb/entry-count limits, DOCX XXE and external-link rejection, PDF active content ignored, parser with no network in a bounded-resource worker, transient bytes only in a non-logged ephemeral store, deleted on completion or within 1 hour. The 1 h versus 24 h conflict is resolved to 1 hour everywhere. | P2-01, P2-02 |
| 29 | TextPolicy | One normative TextPolicy for every student- or document-derived text field, applied at write time and escaped at each sink (section 2.2). | P2-16 |
| 30 | Ownership and request order | Nested ids belong to the path syllabus and owner; order of 404/400/409/429 fixed (section 2.1); batch and sync ids are object keys so duplicates are unrepresentable; unknown and duplicate JSON members rejected. | P2-05, P2-12, P2-14 |
| 31 | Extraction cache | If any, scoped by user id; **no cross-user** content-hash cache. | P2-06, P2-07 |
| 32 | Hostile-but-verbatim | `injectionSuspect`, `hiddenContentDropped`, `supersession_cue` flags; batch approve excluded for suspect documents (`batchApproveAllowed`); server-sliced context window; hidden text dropped; `looks_right` is not "verified". | P2-08, P2-17, P2-19 |
| 33 | Notice and attestation | `noticeVersion` and `attestation` (boolean plus timestamp) are required in the create request and stored in `Syllabus.notice`; wording remains a PO/counsel placeholder. | P2-24 |
| 34 | AI budget | Separate budget pool for the syllabus workflow, not shared with lecture features (section 8). | P2-23 |
| 35 | Real Google Calendar | Needs a new `calendar.v1` contract and a security re-review; the mock-only guard stays; all-day end-date semantics and the sync-versus-export cap are aligned (sections 2.3, 6.3, 10, 15). | P2-18 |
| 36 | Idempotency, ETag, TOCTOU | Key scope `(user, method, route template, key)`; replay returns the original outcome with `Idempotency-Replayed`, state may differ; revision separated from etag; row lock at sync (section 2.1). | P2-10, P2-11 |
| 37 | Logging and CSRF | No filenames or parser exception text in logs; C-23 canary applies; Origin/CSRF on the multipart route and DELETE (sections 2.1, 8). | P2-25, P2-26 |
| 38 | Offset unit | Snippet and context offsets are UTF-16 code units in the canonical text; all slicing in .NET, never SQL (section 4). Closes open question 1 of `docs/data/schema-syllabus-p2.md`. | data doc |
| 39 | Transient store | A non-logged ephemeral store; an UNLOGGED table is the recommended implementation (dumps with `--no-unlogged-table-data`); security-engineer to sign off its residual risk (section 5.1 item 9). | P2-02 |


## 14. Open decisions (Product Owner items, unchanged)

Carried from `product/phase2-syllabus-import.md` section 13 without resolving them here. Where section 1 records a provisional default, the PO may overturn it.

- P2-D1 PDF, DOCX and pasted text all required for slice 1, or defer one? (this draft assumes all three are in the contract)
- P2-D2 OCR for scanned syllabi: out of scope now; may a later increment add it?
- P2-D3 Confidence display: three text labels, or a simpler "check this" flag only?
- P2-D4 Retention of the original file versus text-only, and default retention period (links to PO-5). **Provisional: not retained.**
- P2-D5 Upload notice wording and whether the student must attest they may upload the document (counsel review).
- P2-D6 Recurring events: single recurring event versus individual occurrences. **Provisional: occurrences, max 60.**
- P2-D7 Two-step approve then sync, or one combined step. **Provisional: two steps.**
- P2-D8 Undo window and per-item removal permanence. **Provisional: never expires.**
- P2-D9 Item types in the first release. **Provisional: the six kinds in section 4.**
- P2-D10 Resolve week-based dates when the student confirms term start, or leave all as needs-date. **Provisional: resolve against student-supplied term only.**
- P2-D11 Limits in section 10 and the per-student AI budget (PO-4/PO-5).
- P2-D12 Whether the mock calendar is visible in non-demo environments.
- P2-D13 Pilot with synthetic or licensed syllabi only, or real students' (needs G1-style review).
- P2-D14 Minimum age handling for uploads (PO-7).
- Also from the sources: PO-4 (spend), PO-5 (retention), PO-6 (real Calendar); A section 14 questions 3 (legal: sending syllabi to a model vendor), 4 (spend caps), 5 (scanned-PDF policy), 6 (retention), 7 (date format policy: month-first default), 8 (section handling); 60-minute default length; several syllabi per course.

Follow-ups recorded by the Lead decisions: **D4/D11** now also own the 180-day canonical-text retention (row 26), whether `snippet.text` should be purged with the context, and the transient-store choice; **P2-D5** owns the notice and attestation wording behind `noticeVersion`.

Questions for the Lead Architect arising from this draft: Q1 should the finer AI kinds (quiz, project) be restored as an additive `kind` values in v1 (U wants them)? Q2 is closed (row 25: `keep` removed). Q3 should `Syllabus.counts` be exact or eventually consistent? Q4 confirm 409 over 422 for state conflicts. Q5 confirm cascade FK allow-list entry for Syllabus tables (database-architect).

## 15. Deferred (not in this contract)

Re-extraction with a diff and edit preservation; changing term or zone after creation; "Reset to what we found" and AI-proposal history on the wire; promoting an unclaimed date to an item; viewing the stored canonical text; ICS/CSV export and original download; bulk removal of orphaned events; opt-in "ready" notifications; real Google Calendar (PO-6): needs a new `calendar.v1` contract (or `syllabus.v2`) because the mock constants (`demoCalendar`, `confirm.provider`, `calendar-event.demo`) cannot be widened additively, plus a security re-review. Entry criteria: (1) per-user OAuth tokens encrypted at rest, never sent to the AI, narrowest Calendar scope, preferably a dedicated app-created calendar; (2) a `pending_removal` state with retry and visible status so a Google outage or revoked token cannot block skipping or un-approving; (3) all-day end is **exclusive** in Google while this contract's `endDate` is **inclusive**, converted at the provider boundary; (4) per-occurrence writes (60 per item) with quota, partial-failure and retry-after handling, and the same cap for sync and export; (5) `itemKey` carried in `extendedProperties.private`, not as the event id; (6) the PO-6 decision, an incremental-authorization UX, a disconnect story for events already in the student's calendar, and a server-side confirm record (who, which items and revisions, what was shown, when); real Claude (PO-4, G2); OCR; section selection; non-English handling beyond the low-confidence flag.

## 16. Verification status

Run: `dotnet test tests/Tether.ContractTests` in the .NET 10 SDK container (command in `src/shared/contracts/README.md`). Result is recorded in the hand-off, not claimed here as a gate outcome. Statements about framework behavior (System.Text.Json duplicate-member handling, Postgres NUL handling, Kestrel limits, XML parser settings, Google field rules) are design requirements marked "verify" and have not been checked against pinned versions, except that the duplicate-member parse option is exercised by one contract test. Not run: any API, database, extraction, AI, UX or accessibility test; none exist. Schema-valid fixtures show the shapes are consistent, not that the product is correct. No gate is affected.
