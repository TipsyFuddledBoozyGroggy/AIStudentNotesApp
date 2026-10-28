# API Contracts - Phase 2, slice 1: Syllabus import (`syllabus.v1`)

**Status:** DRAFT v1, not frozen. Authored 2026-10-28 (backend-engineer, for the Lead Architect). Nothing here is implemented: there is no Syllabus module, no endpoint, no migration and no AI document seam yet. This file and `src/shared/contracts/syllabus.v1/` are the only artifacts, and they are verified only by schema and fixture contract tests (section 12). The M2 contract `docs/API_CONTRACTS.md` (lecture.v1, speech.v1) is frozen and is **not** modified by this file.

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
| 5 | The **original uploaded file is not retained**. Only canonical extracted text, items and snippets are stored. `Syllabus.originalRetained` is the constant `false`. | Product Owner (P2-D4) | **provisional, PO decision pending** |
| 6 | Calendar is **mock only**. `ICalendarProvider` has one implementation; startup refuses any other provider while PO-6 is open. Every response says `provider: "mock"` and `demoCalendar: true`. | Product Owner (PO-6) | provisional, PO decision pending |
| 7 | Default timed-event length is **60 minutes**. | Product Owner | **provisional, PO decision pending** |
| 8 | Several syllabi per course are allowed (proposal: 5 per course, 50 per user). | Product Owner | provisional, PO decision pending |
| 9 | Limits (section 10) are proposals: 10 MB, 60 pages, about 50,000 extracted characters. | Product Owner / devops | provisional, PO decision pending |
| 10 | Approve and calendar sync are **separate commands** (P2-D7). | Product Owner | provisional, PO decision pending |
| 11 | Undo of approve or skip **never expires** (P2-D8). | Product Owner | provisional, PO decision pending |
| 12 | Recurring items sync as individual occurrences (P2-D6), at most 60 per item. | Product Owner | provisional, PO decision pending |

## 2. Conventions (inherited, not redefined)

Base path `/api/v1`, session cookie plus Origin check, user-scoped: a foreign or missing id is `404 not_found`, never 403. Errors are RFC 9457 ProblemDetails with a stable `code` (`syllabus-problem.schema.json`; the lecture.v1 codes plus section 9). `detail` is a fixed server string per `code`, never exception text, model output or document content. Lists are `{items, nextCursor}` with `?cursor&limit`, server maximum 200. `ETag` is the opaque weak tag; `If-Match` is required on item writes (`428 precondition_required` when absent, `412 precondition_failed` on mismatch; lecture.v1 codes, not `409 conflict`). `Idempotency-Key` is stored 24 h per user and per route; a replay returns the original result with `Idempotency-Replayed: true`; reuse of a key with a different body is `400 validation_failed` (B1 behaviour, API_CONTRACTS section 14.4).

Dates the student reads are **wall-clock**: `YYYY-MM-DD`, `HH:MM` and an IANA zone id (`America/New_York`). No UTC and no offsets appear anywhere except instants (`createdAt`, `syncedAt`). UTC conversion happens only at a real Calendar write, in google-integration code, and is out of scope. All-day items carry no time and do not shift.

All text from documents or the model (`title`, `snippet.text`, `notes`) is untrusted plain text: render as text nodes, no markdown, HTML or links (C-28). Snippet text and offsets are **sliced by the server** from the stored canonical text, never copied from model output.

## 3. Resources

Authoritative shapes are the schemas; this is a map.

| Resource | Schema | Notes |
|---|---|---|
| Syllabus | `syllabus.schema.json` | status, reason, `pollAfterSeconds`, `aiSource`, `demoSource`, `demoCalendar`, `originalRetained:false`, document facts, `context` (term and default zone), provenance, counts |
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
- **`confidence`** is a **server-derived enum** `looks_right`, `probably_right`, `please_check`. There is no numeric score on the wire (the schema forbids the field); `confidenceReason` is a short fixed-vocabulary explanation. `looks_right` requires an explicit full date, a verified snippet and no conflict flag. A resolved item with `tentative_language`, `weekday_mismatch`, `table_layout_risk` or `outside_term` is `please_check`.
- **`flags`** vocabulary: `year_inferred`, `relative_date`, `timezone_assumed`, `time_missing`, `conflicting_sources`, `tentative_language`, `table_layout_risk`, `section_ambiguous`, `range_end_inferred`, `weekday_mismatch`, `date_not_verifiable`, `until_from_term`, `low_text_quality`, `duplicate_candidate`, `outside_term`, `dst_ambiguous`. Server flags win; the model cannot remove them.
- **`snippet`**: `{text, start, end, page, precision}`; `precision` is `exact` (offsets into the stored canonical text; `end - start` equals the text length, tested) or `line` (redaction prevented an exact match; offsets cover the line range). Null only for `origin: "student"`; required for `origin: "ai"`.
- **`decision`** is `undecided`, `approved`, `skipped`. **`syncState`** is `not_synced`, `synced`, `changed_since_sync`, `sync_failed`.
- Cross-field rules enforced by the schema (and re-checked by the server): a `needs_date` or `not_applicable` item can never be `approved`; any `syncState` other than `not_synced` requires `decision: "approved"` and a `syncedRevision`; `kind: "policy"` if and only if `when.status: "not_applicable"`; student items have no snippet and never `policy`; `allDay` forbids times; `endTime` requires `time`.
- **`edited`** is true once the student changed any value. The original AI proposal remains in provenance (invariant 5) and is not on the wire in v1 ("Reset to what we found" is deferred, section 15).
- Default length: a timed item with no `endTime` becomes a 60 minute event (decision 7); the event carries `endDerived: true`. A timed event crossing midnight is allowed.

### 4.1 `itemKey` (idempotency basis)

`itemKey = "ik_" + first 32 characters of base64url(SHA-256(UTF-8("tether.syllabus.item.v1\n" + syllabusId + "\n" + itemId)))`, no padding. Derived from (syllabusId, itemId) only, so it never changes on edit, undo or re-approval, and a future Google provider reuses it as its idempotency key (`extendedProperties` or equivalent). Contract tests recompute it for every fixture. Recurring occurrences use `itemKey + ":" + occurrenceDate`.

## 5. REST endpoints

All user-scoped; a foreign or missing id is `404 not_found`. Ownership of the course/syllabus is verified before any file bytes are read.

| Method | Path | Request | Success | Errors |
|---|---|---|---|---|
| POST | `/courses/{courseId}/syllabi` | multipart: `file` + `metadata` (`syllabus-create-request`, no `text`), **or** JSON `syllabus-create-request` with `text`. `Idempotency-Key` **required** | 202 `Syllabus` (`queued`) + `Location` | 400, 404, 409 `duplicate_syllabus` / `extraction_in_progress` / `syllabus_limit_reached`, 413 `too_large`, 415 `unsupported_type`, 429 |
| GET | `/courses/{courseId}/syllabi?cursor&limit` | | 200 page of `Syllabus` | 404 |
| GET | `/syllabi/{id}` | | 200 `Syllabus` + ETag (poll target) | 404 |
| DELETE | `/syllabi/{id}?calendarEvents=remove\|keep` | the query parameter is **required** | 204; deletes items, canonical text, revisions, approvals, jobs, sync records; cancels a running job | 400, 404 |
| GET | `/syllabi/{id}/items?cursor&limit&decision&kind&needsDate` | | 200 page of `Item` | 400, 404 |
| POST | `/syllabi/{id}/items` | `syllabus-item-create-request`, optional `Idempotency-Key` | 201 `Item` (`origin: "student"`) | 400, 404, 409 `invalid_state` while not `ready`/`partial` |
| GET | `/syllabi/{id}/items/{itemId}` | | 200 `Item` + ETag | 404 |
| PATCH | `/syllabi/{id}/items/{itemId}` | `syllabus-item-update-request` (JSON merge-patch) + `If-Match` | 200 `Item` | 400, 404, 412, 428, 409 `item_not_syncable` (policy) |
| DELETE | `/syllabi/{id}/items/{itemId}` | `If-Match` | 204; only `origin: "student"` items; removes any mock event | 404, 409 `student_item_only`, 412, 428 |
| POST | `/syllabi/{id}/items/{itemId}/decision` | `syllabus-item-decision-request` + `If-Match` | 200 `Item` | 404, 409 `item_needs_date` / `item_not_syncable` / `acknowledgement_required`, 412, 428, 503 |
| POST | `/syllabi/{id}/decisions` | `syllabus-decision-batch-request` (explicit ids and etags) | 200 `syllabus-decision-batch-result` (per item) | 400, 404 |
| POST | `/syllabi/{id}/calendar-sync` | `syllabus-calendar-sync-request` + `Idempotency-Key` **required** | 200 `syllabus-calendar-sync-result` | 400, 404, 409 `invalid_state`, 429, 503 |
| POST | `/syllabi/{id}/calendar-sync/remove` | `syllabus-calendar-remove-request` | 200 `syllabus-calendar-remove-result` | 400, 404 |
| GET | `/courses/{courseId}/demo-calendar?from&to&cursor&limit` | `from`, `to` are `YYYY-MM-DD` | 200 page of `Calendar event` | 400, 404 |
| GET | `/syllabi/{id}/unclaimed-dates?cursor&limit` | | 200 page of `Unclaimed date` | 404 |
| GET | `/syllabi/{id}/export` | | 200 `syllabus-export` (owner-scoped, on demand) | 404 |

Notes
- **Upload.** Content is sniffed by signature (PDF text layer, DOCX, plain text), never trusted by extension or MIME header. Over-limit or wrong type is refused before storing anything. File bytes are held only for the extraction job (decision 5) and discarded when it ends or fails (proposal: hard cleanup at 1 h). Because the original is not kept, retrying a failed extraction needs a new upload; after `ready` the canonical text and items persist.
- **Duplicate content.** Identical content hash in the same course returns `409 duplicate_syllabus` with `existingSyllabusId` in the problem body, unless the request sets `allowDuplicate: true`.
- **One extraction in flight per user** (`409 extraction_in_progress`); at most 5 syllabi per course (`409 syllabus_limit_reached`).
- **Term and zone.** `termStart`/`termEnd` are optional and student-supplied at creation; `defaultTimezone` is required (browser or profile value, validated against a server allow-list). Changing them later is deferred (delete and re-add).
- **Delete.** `calendarEvents=remove` also removes this syllabus's mock events; `keep` leaves them in the demo calendar, orphaned from the syllabus (they keep their `itemKey`, and are removable only in bulk by a later explicit action; deferred). Omitting the parameter is `400 validation_failed` so the choice is always explicit. Copies already sent to an AI provider (when real) and exported files are not deleted by this call; the UI must say so.
- **Pagination of items** is by `(effective date, id)`; items needing a date sort last.

## 6. Commands and rules

### 6.1 Decisions (approve, skip, undo)

- `decision: "approved"` requires `when.status: "resolved"`; otherwise `409 item_needs_date` (or `409 item_not_syncable` for `policy`). Enforced on the server, not only in the UI. Approving never writes to any calendar.
- Approving a `please_check` item needs `acknowledgeCheck: true`, else `409 acknowledgement_required`.
- Approval is recorded in the content-free audit log (user id, item id, revision, timestamp) and applies to the item `revision` that `If-Match` named.
- Undo (`undecided`) and skip (`skipped`) never expire and never time out.
- Setting an item that is `synced`, `changed_since_sync` or `sync_failed` to `undecided` or `skipped` **also removes its mock events in the same transaction** and returns `syncState: "not_synced"`. If the removal fails, the whole command fails with `503 provider_unavailable` and nothing changes. The calendar therefore never holds an item the student has withdrawn.
- Batch (`POST /syllabi/{id}/decisions`): explicit `{itemId, etag}` pairs (1 to 100), no "all" form, duplicate ids are `400`. `approve` is accepted only for `resolved` `looks_right` items; every other item is `rejected` with `batch_not_eligible` (or `item_needs_date`). A stale etag is `rejected` with `precondition_failed`. The response is 200 with per-item results and counts; it never claims success when any item was rejected. `skip` and `undecide` follow the same removal rule as above.

### 6.2 Edit

- Allowed on any non-`policy` item: `kind` (not to `policy`), `title`, `notes`, `date`, `endDate`, `time`, `endTime`, `allDay`, `timezone`. Setting `date` on a `needs_date` item makes it `resolved` with `derivation: "student_entered"`, `timezoneSource: "student"`, `edited: true`, and it stays `undecided`. A date cannot be cleared.
- Student edits are exempt from source-derivability checks (the student is the authority) but still need real calendar dates and `endDate >= date`.
- Editing an **approved, not yet synced** item returns it to `undecided` (approval belongs to the revision that was approved). Editing a **synced** item keeps it `approved`, sets `syncState: "changed_since_sync"` and does **not** touch the calendar; the next explicit `calendar-sync` shows and applies the change (`outcome: "updated"`).
- Every edit bumps `revision` and the etag.

### 6.3 Calendar sync (mock)

- Request names the item ids and carries `confirm: {provider: "mock", acknowledgedDemo: true}`. Nothing is written without it. Only items that are `approved` and `resolved` are eligible; every other id yields `outcome: "not_eligible"` with `errorCode` (`item_not_approved`, `item_needs_date`, `item_not_syncable`) and **zero writes**.
- Outcomes per item: `created`, `updated` (an existing event for the same `itemKey` changed), `unchanged`, `failed`, `not_eligible`. Overall `status`: `all_succeeded`, `partial`, `failed`, `nothing_to_do`. HTTP is 200 for every outcome; the result never reports full success when any item failed. A failed item stays `approved` with `syncState: "sync_failed"`; a retry does not duplicate the others.
- **Idempotent.** The mock event for an item is keyed by `itemKey` (and the occurrence date for recurrences). Running sync any number of times, or replaying the same `Idempotency-Key`, yields one event per item (`unchanged` on repeat). A replayed key returns the original body.
- `calendar-sync/remove` deletes mock events and returns items to `approved` / `not_synced` (the decision is kept). Remove then re-add produces exactly one event. Outcomes: `removed`, `not_present`, `failed`.
- Events are stored with provider, event id, item id, `syncedRevision` and time, in the database only (C-22). Fault injection (timeout, 500, rate limit) is allowed only in Development/Test and fails startup elsewhere (same rule as `Speech:Replay:Faults`).

## 7. Real-time and background work

No hub. A background job (existing Postgres jobs pattern) runs extraction; the client polls `GET /syllabi/{id}`. Deleting a syllabus or its course cancels the job, and every writer re-checks the syllabus still exists so a late job cannot recreate data (extends C-21). Course archive and purge cascade with DB-level `ON DELETE CASCADE` (database-architect to confirm the allow-list entry).

## 8. Privacy, security and logging requirements (normative for implementation)

- The AI sees only canonical text (as untrusted data) plus validated server context (term dates, IANA zone). Never tokens, the student's calendar, ids, names, filenames or other documents; no tools. The Calendar provider never receives AI output without student approval.
- Prompt-injection text in a document is inert (A section 8). `snippet.text` of a hostile document may still contain a false but verbatim date; the mitigation is the visible snippet and mandatory approval, not trust.
- Logs carry id hashes, sizes, counts, versions, validation codes. Never document text, snippets, titles, notes, prompts, completions or filenames (log canary extended to this module).
- Hidden content (DOCX `vanish` runs, comments, metadata) is dropped, counted in `document.hiddenContentRemoved`, and `document.injectionSuspect` drives a neutral notice only.
- Export and delete are owner-scoped. Export holds items, snippets and events; it holds no original file because none is retained.

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

Per-item codes inside 200 results (`itemErrorCode`): `item_needs_date`, `item_not_syncable`, `item_not_approved`, `batch_not_eligible`, `acknowledgement_required`, `precondition_failed`, `not_found`, `provider_unavailable`, `invalid_state`.

## 10. Limits (proposals, configurable, not measured; devops and PO own the values)

| Limit | Proposal |
|---|---|
| Upload size | 10 MB |
| Pages | 60 (schema `pageCount` maximum) |
| Extracted or pasted characters | about 50,000 (schema maximum 50,000; over it is `too_large`) |
| Items per syllabus | 200 AI items plus student items, export cap 300 |
| Syllabi | 5 per course, 50 active per user |
| Batch size | 100 item ids |
| Recurrence occurrences synced | 60 per item |
| Extraction time | 60 s extract, 120 s AI stage (A and P) |
| Page `limit` | default 50, maximum 200 |

## 11. Rate limits (proposals)

Existing global limits apply (120/min per user, 30/min writes). Additional: syllabus creation 10 per day per user, **1** extraction in flight per user (stricter than P's 3), `calendar-sync` 6 per minute per syllabus, `decisions` and item writes within the write bucket. Excess is `429 rate_limited` with `Retry-After`. AI budgets (PO-4) surface as `partial`/`failed` with `budget_limited`, never silent.

## 12. Contract tests (exist now)

`src/api/tests/Tether.ContractTests`: `ContractFixtureTests` registers `syllabus.v1` beside `lecture.v1` and validates every flat fixture named `{schema}--{case}.json` in `src/shared/contracts/fixtures/valid` (must pass) and `/invalid` (must be rejected). `SyllabusContractTests` checks: schema strictness, a fixture pair per schema, enum confidence with no numeric score, itemKey recomputation for every fixture and a pinned vector, exact-snippet offset arithmetic, approval invariants on every valid item, doc status text, every code and enum value named in this file, and that only `mock` is expressible. Server-side tests still needed (JSON Schema cannot express them): real calendar validity (Feb 30), `endDate >= date`, zone allow-list membership, duplicate ids in a batch, exactly one of file/text, ownership 404 for every route (two-user overlap), idempotent sync x3 yields one event, withdrawal removes the mock event, log canary, hostile-file fixtures. Red-team syllabus fixtures under `fixtures/redteam/` are not added by this draft.

## 13. Reconciliation (where P, A and U disagree)

Rule: choose the safer option; Lead Architect decisions in section 1 win.

| # | Topic | P (product) | A (AI design) | U (UX) | Choice |
|---|---|---|---|---|---|
| 1 | Item kinds | assignment, exam, reading, office_hours, policy, other | 9 kinds (quiz, project_milestone, class_meeting, no_class, other_deadline) | exam, quiz, assignment, reading, project, other | The P/Lead six. A's kinds mapped server-side (section 4); U's Quiz/Project labels cannot be shown as such (open question Q4). |
| 2 | Confidence | 3 buckets, numeric internal | enum | 3 labels, raw value under Details | Enum only, nothing numeric on the wire (Lead). U's "Details" number is dropped. |
| 3 | Item state | one `status` of 7 values | `review.state` + `calendar.approved` | `decision` + `syncState` | `decision` x `syncState` x `edited` x `origin` as separate fields; invalid combinations rejected by schema. |
| 4 | Job status | queued, extracting, extracted, partial, no_text, failed | queued, extracting_text, analyzing, ready_for_review, needs_text, partial, failed, cancelled | steps | Lead list: `queued`, `extracting`, `proposing`, `ready`, `needs_text`, `partial`, `failed`. Cancel is delete. |
| 5 | Syllabi per course | several | n/a | one, with Replace | Several (Lead). U's Replace flow becomes add another plus delete the old one; U must adapt. |
| 6 | Original file | stored as source record | stored by content hash | open (Q4) | Not retained (provisional D4): the safer data position. Costs: no "view in document", no original export, retry needs re-upload. |
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
| 24 | Delete with synced events | asks, default describes | n/a | demo events stay | Explicit required choice `calendarEvents=remove|keep`; no default. |

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

Questions for the Lead Architect arising from this draft: Q1 should the finer AI kinds (quiz, project) be restored as an additive `kind` values in v1 (U wants them)? Q2 is orphaning mock events with `calendarEvents=keep` acceptable or should `keep` be removed from v1? Q3 should `Syllabus.counts` be exact or eventually consistent? Q4 confirm 409 over 422 for state conflicts. Q5 confirm cascade FK allow-list entry for Syllabus tables (database-architect).

## 15. Deferred (not in this contract)

Re-extraction with a diff and edit preservation; changing term or zone after creation; "Reset to what we found" and AI-proposal history on the wire; promoting an unclaimed date to an item; viewing the stored canonical text; ICS/CSV export and original download; bulk removal of orphaned events; opt-in "ready" notifications; real Google Calendar (PO-6, needs its own contract and security review); real Claude (PO-4, G2); OCR; section selection; non-English handling beyond the low-confidence flag.

## 16. Verification status

Run: `dotnet test tests/Tether.ContractTests` in the .NET 10 SDK container (command in `src/shared/contracts/README.md`). Result is recorded in the hand-off, not claimed here as a gate outcome. Not run: any API, database, extraction, AI, UX or accessibility test; none exist. Schema-valid fixtures show the shapes are consistent, not that the product is correct. No gate is affected.
