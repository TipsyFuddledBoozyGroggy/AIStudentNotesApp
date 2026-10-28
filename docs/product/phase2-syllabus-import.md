# Phase 2, Slice 1 - Syllabus import with student review, then mock Calendar sync

Status: DRAFT proposal for Product Owner and Lead Architect review. Nothing here is implemented or verified. Date: 2026-10-28.
Owner: Product Manager. Reviewers needed: Lead Architect, security-engineer, adhd-ux-specialist, backend-engineer, database-architect, ai-agent-engineer, qa-engineer, legal-compliance-reviewer (FERPA/privacy of uploaded documents).

Source basis: `CLAUDE.md` invariants 3, 4, 5, 8, 10; `docs/PRODUCT_REQUIREMENTS.md` section 6 ("Syllabus intelligence"); `docs/DECISIONS.md` (ADR-002, ADR-009, ADR-012, ADR-020d, PO-4, PO-5, PO-6); `docs/API_CONTRACTS.md` (style: `/api/v1` REST, user-scoped 404 for foreign ids, `Idempotency-Key`, ETag/`If-Match`, ProblemDetails codes, strict schemas under `src/shared/contracts/`); `docs/RELEASE_GATES.md` (0 of 5 gates passed).

## 1. Goal and smallest verifiable deliverable

A student attaches a syllabus to a course, sees a short, calm review list of what the AI thinks the course requires, fixes or discards anything wrong, approves what is right, and approved items appear in a **mock** calendar. The student stays in control at every step; nothing is written anywhere without an explicit approval, and uncertain dates are never guessed.

Smallest verifiable deliverable: with `MockAi` and `MockCalendarProvider` only, a student can upload a pasted-text syllabus fixture to a course, review 6+ proposed items (at least one "needs date"), approve a subset, see them in the mock calendar, re-run sync with no duplicates, edit one item (mock event updates), and undo one (mock event removed). Verified by automated API and Playwright tests plus a recorded manual run. No claim of real Calendar integration.

## 2. Assumptions and scope boundary

- Assumption A1: Courses, auth, cookie session, Origin/CSRF checks, ownership scoping and the Postgres-backed jobs table from M1/M2 exist and are reused. Syllabus is a new module (or sub-area) under the modular monolith (ADR-001); module placement is a Lead Architect decision.
- Assumption A2: All AI runs behind `IAiClient` with the mock as default (ADR-009). A real Claude call needs PO-4 and is not part of the verifiable deliverable here. Extraction quality on real syllabi is therefore **unmeasured** until a PO-authorized evaluation on real or licensed sample documents.
- Assumption A3: The calendar is behind an `ICalendarProvider` seam with a mock implementation that writes only to our database (same pattern as the mock Docs exporter, ADR-012, C-22). Real Google Calendar needs PO-6 and the incremental scope flow; out of scope.
- Assumption A4: Mock Calendar is visibly labeled "Demo calendar (not your Google Calendar)" everywhere, mirroring the M2 "Demo source" rule (API_CONTRACTS section 12.2), so it can never be mistaken for real sync.
- Assumption A5 (learning-science / ADHD review): showing a deadline list reduces planning load without raising anxiety; this is unvalidated (gate G4) and no effectiveness claim is made.
- In scope: upload (PDF, DOCX, pasted text), text extraction, AI proposal, review/edit/approve, mock sync with idempotency, edit and undo, delete and export of the syllabus and derived items.
- Out of scope: see section 11.

## 3. Personas and journey

- Student (primary): at term start, has a 10-40 page syllabus per course, finds it overwhelming, wants the dates in one place without having to read everything.
- Administrator: no access to syllabus content by default; sees only usage and failure counts without content.
- Supporter: not in this slice.

Journey: Course page -> "Add syllabus" -> choose file or paste -> "Reading your syllabus..." (cancelable) -> review screen (grouped, one section at a time) -> edit/skip/approve -> "Add N items to demo calendar" confirm -> result with undo -> done. The student can leave and return at any point; the draft review state persists.

## 4. Item model (product-level; contract details belong to the Lead Architect)

Each proposed item shows:
- `type`: `assignment`, `exam`, `reading`, `office_hours`, `policy`, `other` (closed vocabulary; unknown model output maps to `other`)
- `title` (text)
- `when`: one of `date`, `datetime`, `date_range`, `recurring`, `needs_date` (never null; "needs date" is a first-class state)
- `timezone` (explicit; see edge cases)
- `confidence`: `high`/`medium`/`low` bucket shown to the student; numeric score is internal. Wording must say "AI's guess", never "accuracy"
- `sourceSnippet` (verbatim text from the extracted syllabus, with page or offset reference) so the student can check
- `status`: `proposed`, `edited`, `approved`, `skipped`, `needs_date`, `synced`, `undone`
- `origin`: `ai` or `student` (items the student adds by hand)
- Policies (late work, attendance, grading) are informational: they appear in a "Good to know" list and have **no calendar date and are never synced**.

IDs: opaque, server-assigned, prefixed strings in the style used elsewhere (for example `seg_{n}`); exact format for `syllabus`, `item`, `sync` is a Lead Architect question (Q1).

## 5. Stories

Each story lists: value, triggers, states, error paths, accessibility, data handling, dependencies, numbered ACs. Common rules applying to all stories are in section 6.

### P2-S1 Add a syllabus to a course
Value: get the course's dates in without retyping. Triggers: "Add syllabus" on a course page (empty-state and course menu). States: idle, choosing, uploading, uploaded, rejected, failed, canceled.
- AC1: Student can supply a syllabus as one PDF, one DOCX, or pasted text (min 1 non-blank character, max length per section 7). One syllabus per upload; a course may hold several (for example revised version). Re-upload creates a new version, never overwrites silently.
- AC2: Allowed types are checked server-side by content signature, not only by extension or MIME header; others return 415 `unsupported_type` with plain-language copy listing what is accepted.
- AC3: Size and page limits are enforced server-side (section 7); over-limit returns 413 `too_large` with the limit named, a suggestion to paste the relevant section, and no partial data kept.
- AC4: Before upload the screen states in one sentence what happens to the file (named processors: our server, the AI provider when real; stored until the student deletes it or per PO-5; not shared). Wording is DRAFT pending legal/privacy review. No dark-pattern consent; no pre-ticked box.
- AC5: Upload shows progress and is cancelable; cancel removes anything stored. Network loss during upload keeps the chosen file name and shows Retry; no silent partial upload.
- AC6: A foreign or missing `courseId` returns 404 (ADR-002); the file is never read before ownership is verified.
- AC7: Keyboard-only and screen-reader operable: file input has a visible label, drag-and-drop is optional and never the only path, upload status is announced via a polite live region, focus returns predictably after completion or error.
- AC8: Upload is idempotent with `Idempotency-Key`; a retried request does not create a second syllabus.
- Data: original file bytes and extracted text are **source** records (invariant 5), owned by the user and course, separate from derived items. Original file retention is an open decision (P2-D4).
- Dependencies: Courses (exists), new Syllabus module, database-architect, security-engineer (upload hardening).

### P2-S2 Extract text from the syllabus
Value: trustworthy input for the AI and a source the student can check. Triggers: successful upload or paste. States: queued, extracting, extracted, partial, no_text, failed.
- AC1: Text extraction runs as a background job (never blocks a request thread) behind an `ITextExtractor` seam; status is pollable via REST and the UI shows plain progress ("Reading your syllabus").
- AC2: Extracted text is stored as a source record with page/offset mapping, so any later `sourceSnippet` can be located and verified verbatim.
- AC3: Scanned/image-only PDFs: if extraction yields little or no text (threshold to be set by backend, for example under N characters per page), the state is `no_text` with the message "This looks like a scan, so I can't read it yet", and two actions: paste the text, or upload a text-based version. **OCR is not part of this slice** (see non-goals). The student is never shown an empty review list as if the syllabus had no dates.
- AC4: Partially extractable documents (some pages text, some scans) produce `partial`: the review screen discloses "N pages could not be read" and names the pages.
- AC5: Password-protected, corrupt, or malformed files fail with a specific, calm message and no stack trace; the file is not retained after failure beyond a short cleanup window (proposed 24 h, to be configured).

> **SUPERSEDED by docs/API_CONTRACTS_P2.md (P2-30, 2026-10-28; Lead decision, provisional where PO-owned):** Criterion 5's 24 h cleanup window is replaced: upload bytes are deleted when extraction ends and in any case within 1 hour, are never written to Postgres, and live only on an ephemeral volume (contract 5.1 item 9). The text above is left as written for history and is not the current requirement.
- AC6: Parsing is hardened: extraction runs in a bounded sandbox (time limit, memory limit, no network, no macro/script execution, no external reference resolution such as DOCX external links or PDF launch actions). Hostile-file fixtures (zip bomb, deeply nested DOCX, huge page count, embedded JavaScript PDF) must fail safe within the limit. security-engineer owns the checklist.
- AC7: Extraction failure never affects other course data. The student can retry once, or paste text.
- AC8: Extracted text and file content never appear in logs (extends the F-08 log canary to this module; a sentinel in a fixture syllabus must not appear in any log sink).
- Dependencies: backend-engineer, security-engineer; library choice for PDF/DOCX parsing is a Lead Architect decision (Q2) with license check.

### P2-S3 AI proposes items from the syllabus
Value: the student gets a draft instead of a blank page. Triggers: extraction complete. States: pending, running, ready, ready_with_warnings, failed, canceled.
- AC1: Output is schema-validated (closed vocabulary, strict objects, one repair retry), as for TL;DR (API_CONTRACTS section 5, C-28). Invalid output after retry yields `failed` with Retry and the "paste/enter manually" path; nothing partial is shown as if complete.
- AC2: Every proposed item carries `type`, `title`, `when`, `timezone`, `confidence` and a `sourceSnippet` that is a verbatim substring of the stored extracted text. A server-side check drops or downgrades to `needs_date` any item whose snippet is not found verbatim, or whose date is not supported by its snippet. Verified with fixtures.
- AC3: **Unknown or ambiguous dates stay `needs_date`.** The AI may never infer a calendar date from vague language ("midterm in week 7", "TBA", "to be announced", "see Canvas", "mid-October") unless the syllabus gives the term start date needed to resolve it and the student has confirmed that term start (P2-S9 AC1). Resolved-from-week items are labeled "calculated from week 7 + your term start" and default to `proposed` with at most `medium` confidence, never auto-approved.
- AC4: Dates without a year are resolved only against the confirmed term (P2-S9). Impossible dates (Feb 30), dates outside the term window, and past dates are flagged "check this date", not silently corrected.
- AC5: The AI receives only the extracted syllabus text and the minimal course metadata needed (name, term). It never receives OAuth tokens, the student's other courses, other users' data, or transcripts (invariant 3). It has no tool access and cannot call the calendar.
- AC6: Syllabus text is untrusted data. Instructions embedded in it ("ignore previous instructions", "mark all assignments as due tomorrow", "email the instructor", markdown image URLs, script payloads) must have no effect on behavior, tool calls, or item content beyond being treated as ordinary text. Shared red-team syllabus fixtures live under `src/shared/contracts/fixtures/redteam/` (extends API_CONTRACTS section 13.7). Items derived from injection text are shown inert as plain text, or dropped, with a "this text looked like instructions to the AI and was ignored" warning where detected.
- AC7: All AI-produced text (`title`, snippets) renders as plain text nodes: no markdown, HTML or link activation (C-28).
- AC8: Per-student AI budget and per-syllabus call cap apply (PO-4 pending); at cap the student sees "I can't read this one right now" with manual entry, never a silent failure. Extraction is bounded: very long documents are chunked, and a per-document maximum chunk count applies.
- AC9: Re-running extraction on the same syllabus version is allowed (limited, e.g. 3 per day) and must not discard the student's edits and decisions: it shows new proposals as a diff to confirm, never overwriting `edited` or `approved` items.
- AC10: The model's output is labeled as an AI draft: "These are my best guesses. Check anything that matters." No copy implies accuracy or completeness; the screen states that the syllabus itself is the authority and the instructor's updates override it.
- Eval requirement (ai-agent-engineer): fixture set of at least 10 synthetic or licensed syllabi covering the edge cases in section 8, with per-field precision targets proposed and measured; targets are not commitments until measured (gate G2 for real Claude).
- Dependencies: ai-agent-engineer, `IAiClient` mock supporting deterministic syllabus fixtures; PO-4 for real Claude.

### P2-S4 Review the proposals (low overwhelm)
Value: a 20-item list is reviewable in minutes without panic. Triggers: items ready. States: reviewing, empty, all_reviewed, partial_review_saved.
- AC1: The review screen has **one primary action** at a time. Default view is a single section at a time ("Due soon / Exams / Assignments / Readings / Office hours / Good to know / Needs a date"), not a 40-row table. Student can switch to "show everything" (opt-in).
- AC2: Progress is expressed neutrally ("12 left to look at"), with no red counts, overdue styling, streaks, urgency colors, countdown timers or guilt language. Dates in the past are shown plainly, not alarmingly.
- AC3: Each item row shows type, title, date/time (or "Needs a date"), confidence as text plus icon (not color alone), and a "Show where this came from" control that reveals the source snippet and its page. Snippet reveal is keyboard operable and does not move focus unexpectedly.
- AC4: Items with `needs_date` and items with `low` confidence are grouped in their own section and are **never** pre-selected for approval. Medium/high items are also not auto-approved; approval is always a student action.
- AC5: Per-item actions: Approve, Edit, Skip ("not for my calendar"). Group actions: "Approve the high-confidence ones in this section" and "Skip the rest" are allowed, but the batch confirm lists exactly what will be added (count and first few titles) before anything is approved. No batch action can include `needs_date` items.
- AC6: Review state (including partially reviewed lists) persists server-side and survives reload, sign-out/in and device change; leaving never loses decisions or forces completion. No time pressure and no "you haven't finished" nagging notifications.
- AC7: Quiet by default: no sounds, toasts that steal focus, or auto-advance. Announcements are polite and limited to state changes the student caused.
- AC8: Works at 320 px width, keyboard-only, screen reader, 200% zoom, reduced motion, forced colors; no axe critical/serious issues. Row actions have accessible names that include the item title.
- AC9: Empty result ("I didn't find any dates") is a first-class state with plain explanation and the options to paste a different section, add items by hand, or finish with nothing; it must not read as a student failure.
- AC10: Long lists remain usable: lists over 20 items paginate or collapse by section; focus and scroll position are preserved after approving or skipping an item.
- Dependencies: adhd-ux-specialist review is mandatory before build starts; frontend-engineer.

### P2-S5 Edit or add an item
Value: fix the AI's mistakes quickly and capture items the AI missed. Triggers: Edit on a row, "Add an item". States: editing, saved, invalid, conflict.
- AC1: Student can edit type, title, date or date-time, end time, all-day, timezone, recurrence (S9), and notes. Student edits are stored as revisions separate from the AI's original proposal and source snippet (provenance preserved, invariant 5).
- AC2: Validation is inline and plain: required title (1-200 chars), valid date, end after start; errors are linked to fields and announced to assistive technology. Invalid input never silently becomes a different date.
- AC3: A `needs_date` item becomes dated only when the student supplies a date; it then moves to `edited`, still requiring approval.
- AC4: Student-added items have `origin=student`, no source snippet, and are flagged as such.
- AC5: Edits use `If-Match`/ETag; a concurrent change (second tab) returns 409 `conflict` with a calm "this changed elsewhere" message that preserves the student's input.
- AC6: Saving an edit to an already-synced item marks it "changed since added to calendar" and offers update (S8), never silently changes the calendar.
- AC7: Edit form is a dialog or inline panel with focus management (focus moves in on open, returns to the triggering control on close, Escape cancels); unsaved changes are preserved or confirmed, not lost.

### P2-S6 Approve items (per item or batch)
Value: explicit consent to write; the single gate between AI proposals and the calendar (invariant 4). Triggers: Approve on an item or batch. States: proposed -> approved; approved -> proposed (withdraw).
- AC1: Only items with a concrete date/time and status in {`proposed`, `edited`} can be approved. `needs_date` items cannot (400 `item_needs_date`), enforced server-side, not only in the UI.
- AC2: Approval is recorded with user id, item id, item revision, timestamp (audit event; content-free).
- AC3: Approval is reversible before sync: "Take back approval" returns the item to its prior state.
- AC4: Approving does **not** itself write to the calendar. A separate final step (S7) shows the destination and the list and requires a confirm. (This separation is deliberate: approval is review; sync is a write.)
- AC5: Batch approve accepts an explicit list of item ids with a precondition on each item's revision; if any item changed since the student saw it, that item is skipped and reported, and the rest proceed. Batch is not "approve everything" server-side without ids.
- AC6: Approval of an item whose `sourceSnippet` verification failed or whose date is flagged "check this date" requires the student to open Edit or tick an explicit acknowledgement. (Exact UX is an adhd-ux-specialist call.)

### P2-S7 Sync approved items to the mock calendar
Value: see the plan in a calendar view without touching real accounts. Triggers: "Add N items to demo calendar" confirm. States: ready, syncing, synced, partially_failed, failed.
- AC1: The confirm step names the destination ("Demo calendar, not Google Calendar"), lists items and times, states what will be written, and offers Cancel. Nothing is written before confirm; the Cancel path leaves items `approved`.
- AC2: Sync is idempotent: each item has a stable sync key derived from (user, syllabus, item id) so re-running sync, double-clicking, retries, or a replayed request with the same `Idempotency-Key` produce no duplicates. Verified by a test that runs sync three times and asserts exactly one mock event per item.
- AC3: Mock events are stored with provider, external mock event id, item id, item revision synced, and synced-at. No content outside the database (C-22 pattern).
- AC4: Only `approved` items with a concrete date sync. `needs_date`, `skipped`, `proposed`, and policy items are never sent; a test with each state asserts zero writes.
- AC5: Partial failure (injected fault) reports per-item results: succeeded items stay synced, failed items stay `approved` with a retry button; the overall result never claims full success. Retry does not duplicate the succeeded ones.
- AC6: Mock provider fault injection (timeout, 500, rate limit) is supported for tests and refuses to run outside Development/Test (same rule as `Speech:Replay:Faults`).
- AC7: A "demo calendar" view lists synced events grouped by week, with the same accessibility rules as S4 (no overdue alarm styling).
- AC8: Recurring items sync as one recurring event, or as individually listed occurrences, per P2-D6; whichever is chosen is idempotent by occurrence key.
- AC9: Offline during confirm: the button is disabled with an explanation or fails with Retry; no optimistic "success" is shown before server acknowledgement.
- AC10: The provider is behind `ICalendarProvider`; a startup guard refuses any provider other than `mock` while PO-6 is open (same pattern as ADR-020c).

### P2-S8 Edit and undo after sync
Value: mistakes and syllabus changes are cheap to fix. Triggers: edit a synced item, "Remove from calendar", "Undo" on the result. States: synced, update_pending, updated, removed, undone.
- AC1: After a sync, the result screen offers "Undo this" for the whole batch for a defined window (proposal 15 min of UI affordance), and each item has "Remove from calendar" at any time later.

> **SUPERSEDED by docs/API_CONTRACTS_P2.md (P2-30, 2026-10-28; Lead decision, provisional where PO-owned):** There is no 15 minute window. Undo of approve or skip never expires (contract decision 11); withdrawing a synced item removes its mock events in the same transaction (6.1). The text above is left as written for history and is not the current requirement.
- AC2: Undo removes only the mock events created by that batch/sync keys and returns those items to `approved` (not `proposed`), preserving the student's decisions. Undo is idempotent.
- AC3: Editing a synced item shows "Update calendar entry" as a separate explicit action that shows old vs new; it updates the same mock event (same sync key), never creating a second event.
- AC4: Remove then re-add produces exactly one event (key reuse), verified by test.
- AC5: Removing an item from the calendar never deletes the item from the review list or the source syllabus.
- AC6: Undo and update failures are reported accurately per item; state never shows "removed" when the mock delete failed.
- AC7: Copy is neutral: no "are you sure you want to give up on this" tone.

### P2-S9 Term dates, timezone, recurrence and conflicts
Value: dates are right for the student's actual term and place. States: needs_term, term_confirmed, conflict_flagged.
- AC1: If the syllabus has relative or yearless dates ("Week 3", "Oct 14"), the review shows a "Confirm your term" step (term start and end, prefilled with the AI's reading and its snippet if found) before any calculated date is shown as concrete. Multiple candidate term dates (for example multiple sessions or "Fall A/Fall B", or last year's dates left in a reused syllabus) are presented as a choice, never silently picked.
- AC2: Timezone defaults to the student's profile or browser zone and is shown on every dated item; the student can change it per course. All-day items carry no time zone shift (a due date "Oct 14" remains Oct 14 for the student). DST boundaries tested (a 9:00 class across a DST change stays 9:00 local).
- AC3: Recurring items (weekly office hours, weekly quiz, "every Tuesday") are represented as a recurrence with an explicit start, end (term end) and weekday rule, shown in plain words ("Tuesdays 2:00-3:00 pm until Dec 8"). If the end or weekday is unclear, the recurrence stays `needs_date` rather than guessed. Exceptions ("no office hours Nov 25") are shown as a flagged note unless unambiguous.
- AC4: Conflicting dates for the same item within one syllabus (schedule table says Oct 14, policy text says Oct 16) produce **one** item with `conflict` flag and both snippets visible, status `needs_date`-like (cannot be approved until the student picks one). The AI never chooses.
- AC5: Overlapping events with other synced items are noted ("same time as Quiz 3") neutrally; this never blocks approval.
- AC6: Non-English or mixed-language syllabi: if language detection is low confidence, say so and show items as low confidence; no auto-translation in this slice.
- AC7: Date formats are interpreted by a documented rule (locale ambiguity, for example 03/04); ambiguous formats are flagged for confirmation.

### P2-S10 Delete and export syllabus data
Value: control over uploaded documents. Triggers: "Delete this syllabus", "Export", account or course deletion. States: stored, deleting, deleted.
- AC1: `DELETE` on a syllabus removes the original file (if stored), extracted text, proposed items, revisions, approvals, pending jobs and mock sync records. Writers re-check the syllabus exists so a late job cannot recreate data (extends C-21). Only content-free audit metadata remains.
- AC2: Deleting a syllabus asks whether to also remove its mock-calendar events; the default describes plainly what happens to each, and the student can keep items already on the demo calendar only if they choose.

> **SUPERSEDED by docs/API_CONTRACTS_P2.md (P2-30, 2026-10-28; Lead decision, provisional where PO-owned):** There is no choice and no `keep` option: `DELETE` takes no parameters and always removes the mock events and all derived data (contract 5.2, row 25). The confirmation must still say that copies already sent to a vendor and exported files are not deleted. The text above is left as written for history and is not the current requirement.
- AC3: Course archive or purge, and user deletion, cascade to syllabus data using DB-level FKs with `ON DELETE CASCADE` (ADR-014/019 pattern) and in-flight extraction/AI jobs are canceled.
- AC4: Export offers the student their items (title, type, date, status, source snippet) as JSON and an iCalendar (.ics) or CSV file, and the original upload on request. Export is owner-scoped and generated on demand.

> **SUPERSEDED by docs/API_CONTRACTS_P2.md (P2-30, 2026-10-28; Lead decision, provisional where PO-owned):** Export is JSON only, on demand, owner-scoped; ICS/CSV are deferred and there is no original to export because none is retained (contract section 8, row 21). The text above is left as written for history and is not the current requirement.
- AC5: The delete confirmation discloses that copies already sent to the AI provider (when real) and any exported files are not deleted by this action; wording pending legal review.
- AC6: Deletion works while a job is running; the job's result is discarded.
- AC7: Data-subject-style requests (admin-assisted deletion) require no admin access to document content.

### P2-S11 Safety, limits and failure states (cross-cutting story)
Value: the feature degrades safely and never traps or misleads the student.
- AC1: Offline: all review state already loaded stays usable; writes show a calm "You're offline. Your choices will not be saved yet" and retry on reconnect; nothing claims to be saved when it is not. Offline queuing of approvals is **not** in this slice.
- AC2: AI provider outage or budget cap: the student can still view the extracted text, add items by hand, and approve/sync those.
- AC3: Rate limits: per-user upload cap (proposal 10/day, 3 concurrent jobs), per-IP and per-session limits on extraction retries; excess returns 429 `rate_limited` with plain wording.
- AC4: All lists and exports have max `limit`; all IDs scoped by `(userId, resource)`; two-user overlap tests cover syllabus, items and sync records.
- AC5: Every stage has loading, empty, error, and success states with plain-language copy, no raw error codes, and no technical stack in UI.

## 6. Common ADHD-friendly and accessibility requirements (apply to all stories)

1. One primary action per screen; secondary actions visually quieter.
2. No streaks, no overdue red styling, no count-of-missed anything, no "you still have N things" reminders, no push or email nudges from this feature in this slice.
3. Chunking: never present more than about 7 items at once by default; sections collapse; "show more" is the student's choice.
4. Plain, short, non-judgmental copy. Uncertain things are described as "I'm not sure", not as errors.
5. Everything is saveable and resumable; leaving mid-review is normal.
6. Student controls pace: no timeouts on review screens, no auto-advance, no auto-approve, no auto-sync.
7. Confidence and status are conveyed by text and icon, not color alone.
8. WCAG 2.2 AA target; keyboard operable, visible focus, 24x24 px minimum targets, no drag-only interaction, reduced motion respected.
9. No diagnosis or attention tracking.
10. Neutral treatment of past dates (a syllabus uploaded mid-term has past items).

## 7. Limits (proposals, configurable, not measured; backend/devops own values)

- Max upload 10 MB, max 60 pages, max extracted text 200,000 characters, pasted text max 100,000 characters (device storage and AI cost bound).
- Max syllabi per course 5; per user 50 active.
- Max proposed items per syllabus 200 (excess flagged "too many to review; try a smaller section").
- Extraction time limit 60 s; AI stage time limit 120 s; both cancelable by the student.
- All values need Product Owner/devops confirmation because they affect cost (PO-4) and storage (PO-5).

## 8. Edge cases and expected behavior

| Case | Expected behavior |
|---|---|
| Scanned/image PDF | `no_text` state, paste or text-based re-upload offered, no OCR in slice (S2 AC3). |
| Mixed text and scan pages | `partial`, pages named (S2 AC4). |
| Multiple term dates / reused syllabus with old dates | Student picks term (S9 AC1); dates outside confirmed term flagged "check this date". |
| Timezone / DST / all-day | Explicit timezone shown; all-day does not shift; DST test (S9 AC2). |
| Recurring items | Recurrence with explicit start/end; unclear stays needs date; exceptions flagged (S9 AC3). |
| Conflicting dates within document | Single item, conflict flag, student must choose (S9 AC4). |
| Prompt injection inside the syllabus | Treated as inert data; no behavior change; red-team fixtures required (S3 AC6). |
| Very large file | 413 with limit named; offer to paste a section (S1 AC3); chunk cap for large allowed docs (S3 AC8). |
| Hostile file (zip bomb, macro, external links) | Sandboxed extraction fails safe (S2 AC6). |
| Syllabus later revised by instructor | New version upload; diff against previous items; student's decisions retained (S3 AC9). |
| Student uploads someone else's or a confidential document | Treated as the student's own upload; no sharing; delete available; wording on terms reviewed by legal (P2-D5). |
| Syllabus contains other people's personal data (instructor phone, TA names, grades, student IDs) | Not extracted as items except office-hours times; PII detection is not promised; text is never logged; privacy review (section 10). |
| Duplicate upload of same file | Detected by content hash, offered "you already added this", no duplicate items unless student insists. |
| Item with no title or only a date | Title generated from snippet context, marked as AI-made, editable. |
| Past dates | Shown plainly, can be skipped in one action, not flagged as overdue. |
| Course archived/deleted mid-job | Job canceled, data cascaded (S10 AC3). |
| Two tabs/devices | ETag conflict handling (S5 AC5); sync idempotent. |
| Student edits then re-extracts | Edits preserved (S3 AC9). |
| 20+ items | Sectioned review (S4 AC1, AC10). |
| Non-English / mixed | Low-confidence disclosure (S9 AC6). |
| Token/session expiry mid-review | Draft state is server-side; sign in again resumes (S4 AC6). |

## 9. Success measures (to validate, not claimed)

- Time from "Add syllabus" to a reviewed-and-synced list for a 20-item fixture syllabus (target to be measured in the manual pilot).
- % of AI-proposed items accepted without edit, and % of `needs_date` items correctly left undated, on the eval fixture set (measured by ai-agent-engineer; not a commitment until measured).
- Zero items synced without explicit approval (automated, hard guard).
- Zero duplicate mock events after repeated sync (automated, hard guard).
- Zero cross-user access in tests; zero syllabus text in logs (log canary).
- Student-rated overwhelm after review (optional, only within an authorized G4 study; no claims before then).
- Guardrail: no engagement metrics (return visits, time in feature) as goals.

## 10. Legal, privacy and learning-science flags for specialist review

1. FERPA/institutional status: syllabi are generally instructor-authored course documents, but a student's annotated version, grade policies or embedded personal data might be an education record; applicability is unreviewed. Owner: `legal-compliance-reviewer`, counsel. Does not clear any gate.
2. Copyright/terms: course materials may be copyrighted or restricted by the institution or instructor (some prohibit uploading to third-party AI services). The upload notice must not imply this is permitted. Counsel review of wording (P2-D5).
3. Third-party processing: when real, the syllabus text goes to the Claude API. Data-processing terms, no-training settings, retention and region are unverified (G2, `vendor-verification-researcher`). Mock-only in this slice.
4. Retention of original files and extracted text (PO-5) is undecided; the DB must support delete without orphaned copies (S10).
5. Minors and dual enrollment (PO-7): upload of a syllabus by under-18 users is unaddressed.
6. Learning science / ADHD: whether a prepared deadline list reduces anxiety or increases avoidance is unvalidated (G4). Review the "Good to know" policy section and "no urgency" design with `adhd-ux-specialist` and `learning-science`. Include a potential harm check: lists that surface many past-due items may increase shame.
7. AI accuracy risk: a wrong date silently trusted is a real student harm (missed exam). Mitigations are verbatim snippets, needs-date discipline, mandatory approval and neutral "check anything that matters" copy; effectiveness unmeasured.
8. Accessibility: review table/list patterns with real screen-reader testing (G3, not run).

## 11. Explicit non-goals

- No real Google Calendar, Google OAuth, scopes, or any external write. Blocked on PO-6.
- No real Claude call or paid provider use; mock AI only unless PO-4 is separately authorized.
- No OCR for scanned PDFs; no handwriting; no image or screenshot syllabi.
- No LMS (Canvas, Blackboard) import or sync; no reading syllabi from URLs.
- No automatic reminders, notifications, emails, or push; no "executive-function coach" nudges (Phase 3).
- No grade calculation, workload estimation, study planning or time-blocking.
- No automatic detection of instructor updates or syllabus change monitoring.
- No sharing syllabi or items with other students, supporters or administrators; no cross-student aggregation.
- No automatic approval, auto-sync, or "approve all" server action without explicit item ids.
- No auto-translation.
- No claim of legal compliance, accuracy rate, or clinical/effectiveness benefit.
- No sync of policy items; policies are informational only.
- No modification of the M2 frozen contract (`docs/API_CONTRACTS.md` v1); Phase 2 contract is a separate versioned file.

## 12. Dependencies

| Dependency | Needed for | Status |
|---|---|---|
| Courses module, auth, ownership scoping | S1 and all | exists (M1) |
| Postgres-backed jobs table and hosted service | S2, S3 | exists per architecture; confirm reuse |
| `IAiClient` with deterministic syllabus mock | S3 | needs extension (ai-agent-engineer) |
| `ITextExtractor` + parsing library choice | S2 | open (Q2) |
| `ICalendarProvider` + `MockCalendarProvider` | S7, S8 | new |
| Phase 2 API contract and schemas | all | to author (increment C1) |
| Log canary extended to Syllabus module | S2, S3 | new test |
| adhd-ux-specialist review of review screen | S4 | required before build |
| security-engineer review of upload/parse hardening and prompt injection | S1, S2, S3 | required before build |
| PO-4 (Claude), PO-5 (retention), PO-6 (Google OAuth) | real AI, retention policy, real calendar | all unresolved; none block mock-backed build, PO-5 blocks production |
| Gates G1-G5 | real use | all open; this slice authorizes no real-student use |

## 13. Open decisions for the Product Owner (none decided here)

Numbering is provisional (P2-D#); Lead Architect may fold these into `DECISIONS.md` under PO-#.

- P2-D1: Are PDF, DOCX and pasted text all required for slice 1, or may DOCX or PDF be deferred? (Proposal only: paste + text PDF first.)
- P2-D2: OCR for scanned syllabi: confirm it is out of scope for Phase 2 slice 1 and decide whether a later increment may add it (adds vendor/cost and PO-4/PO-3-like review).
- P2-D3: Confidence display: show `high/medium/low` text labels to the student, or show a simpler "check this" flag only? (adhd-ux input needed.)
- P2-D4: Retention of the original uploaded file versus keeping only extracted text and items; default retention period (links to PO-5).
- P2-D5: Upload notice wording and whether the student must attest they are allowed to upload the document (copyright/institution rules); counsel review needed.
- P2-D6: Recurring events in the mock calendar: single recurring event versus individual occurrences.
- P2-D7: Whether approving and syncing is a two-step flow (approve, then separate "add to calendar" confirm; this document's proposal) or one combined step per batch.
- P2-D8: Undo window length for batch undo (proposal 15 min) and whether per-item removal is permitted indefinitely.

> **SUPERSEDED by docs/API_CONTRACTS_P2.md (P2-30, 2026-10-28; Lead decision, provisional where PO-owned):** Provisional answer in the contract: undo never expires (decision 11). The text above is left as written for history and is not the current requirement.
- P2-D9: Which item types are in the first release (assignment, exam, reading, office hours; policies as read-only) versus deferring readings or office hours.
- P2-D10: Resolve week-based dates ("Week 7") into concrete dates if the student confirms term start (proposal here) or leave all week-based items as "needs date" in slice 1.
- P2-D11: Limits in section 7 (file size, pages, uploads per day) and per-student AI budget, which depend on PO-4/PO-5 spend decisions.
- P2-D12: Should the mock calendar remain visible to students in any non-demo environment, or be strictly Development/Test/demo labeled (and hidden when a real provider exists)?
- P2-D13: Is the first pilot using only synthetic/licensed sample syllabi (proposal) or real students' syllabi (needs G1-style privacy review and authorization)?
- P2-D14: Minimum age handling for uploads (PO-7 dependency).

## 14. Questions for the Lead Architect (contract and architecture, not decided here)

- Q1: ID format and prefixes for `syllabus`, `item`, `sync`; confirm user/course-scoped composite FKs per ADR-014/019 pattern.
- Q2: Module placement (new `Syllabus` module vs under Courses/Integrations), parsing libraries (license, maintenance), and sandbox mechanism for extraction.
- Q3: Whether Phase 2 contract lives in a new `docs/API_CONTRACTS_P2.md` and `src/shared/contracts/syllabus.v1/` (this document's assumption) given M2 v1 is frozen.
- Q4: Realtime vs polling for extraction/AI progress (ADR-020d: single instance only; proposal: polling to avoid new hub surface).
- Q5: Whether the AI module extends `IAiClient` or adds `ISyllabusExtractor` behind it; schema validation and repair-retry reuse.
- Q6: Where file bytes live (Postgres bytea vs filesystem vs object storage); affects backup and deletion guarantees (devops).
- Q7: Sync key derivation and the provider-agnostic external id mapping so the future Google provider reuses the same idempotency contract.

## 15. Proposed increment breakdown

Principle: contract first, then file-disjoint parallel work. Each increment produces observable verification (tests run, outputs recorded). Owners are proposals; the Lead Architect assigns. "Files" are proposed boundaries so no two agents edit the same paths.

| Inc | Name | Owner | Writes only to | Depends on | Observable verification |
|---|---|---|---|---|---|
| P2-C1 | Contract: REST, item/sync schemas, error codes, fixtures (valid and invalid), red-team syllabus fixtures | Lead Architect (draft) with backend-engineer | `docs/API_CONTRACTS_P2.md`, `src/shared/contracts/syllabus.v1/**`, `src/shared/contracts/fixtures/syllabus/**` | Q1, Q3, this doc | Schema validation tests pass on both stacks; fixtures valid/invalid as expected |
| P2-D1 | Data model and migrations (syllabus, source file/text, items, item revisions, approvals, sync records, mock events, audit), cascade FKs, allow-list update | database-architect | `docs/data/syllabus-p2.md`, Syllabus migration files | P2-C1 | Migration applies to clean DB; `pg_constraint` allow-list test updated and passing; cascade-delete test |
| P2-B1 | Backend: upload endpoints, extraction job, `ITextExtractor`, status polling, hardened parsing, ownership scoping, limits, log canary | backend-engineer | `src/api/**/Syllabus/**` (new module), `tests/api/**/Syllabus*` | P2-C1, P2-D1, security input | API tests incl. two-user overlap, hostile-file fixtures, scanned-PDF fixture, limits, log canary |
| P2-AI1 | AI: extraction prompt/schema, `MockAi` deterministic syllabus fixtures, verbatim-snippet verifier, needs-date discipline, injection hardening, eval harness | ai-agent-engineer | `docs/ai/syllabus-extraction.md`, `src/api/**/Ai/Syllabus*`, `tests/ai-evals/syllabus/**` | P2-C1 | Eval run reported with actual numbers on fixtures; red-team fixtures inert; no snippet-not-found item leaves AI stage |
| P2-B2 | Backend: review/edit/approve endpoints, ETag conflicts, batch approve rules, term/timezone/recurrence logic, `ICalendarProvider` + `MockCalendarProvider`, idempotent sync, undo/update, export/delete cascade | backend-engineer | `src/api/**/Syllabus/**` (continuation; sequential after P2-B1, not parallel with it) and `src/api/**/Calendar/**` | P2-B1, P2-AI1 | Tests: sync x3 yields one event per item; needs_date never synced; undo and update idempotent; fault injection partial failure |
| P2-UX1 | UX flow and copy for review/term/confirm/undo screens (DRAFT copy) | adhd-ux-specialist | `docs/ux/p2-syllabus-review.md` | this doc | Review notes against section 6; blocks P2-F1 start |
| P2-F1 | Frontend: add syllabus, progress, review (sectioned), edit dialog, approve, confirm, demo calendar view, undo, delete/export; all states | frontend-engineer | `src/web/**/syllabus/**`, `src/web/e2e/syllabus*` | P2-C1, P2-UX1; may build against contract fixtures before B1/B2 | Playwright + axe at 320 px and keyboard-only; states for denied, offline, empty, scan, failure |
| P2-SEC1 | Threat model and review: upload hardening, injection, data handling, deletion | security-engineer | `docs/security/p2-syllabus-review.md` | P2-C1; re-review after B1/B2/AI1 | Findings with IDs; high/critical fixed before merge |
| P2-QA1 | Test plan and independent validation; E2E journey; traceability to ACs | qa-engineer | `tests/e2e/**/syllabus*`, `docs/qa/p2-syllabus-test-plan.md` | all | AC-to-test traceability matrix; recorded run of full suite |
| P2-LEG1 | Privacy/FERPA/copyright issue list for uploaded documents (draft notice wording) | legal-compliance-reviewer | `docs/legal/p2-syllabus-privacy-notes.md` | this doc | Document only; does not clear G1 |
| P2-G3 | Manual screen-reader pass on review screen | device-accessibility-validation | `docs/qa/p2-syllabus-device-plan.md` | P2-F1 | Human-run results; NOT_RUN until a person executes them |

Suggested order: P2-C1 -> (P2-D1 | P2-UX1 | P2-AI1 | P2-SEC1 | P2-LEG1 in parallel, file-disjoint) -> P2-B1 -> P2-B2 and P2-F1 in parallel -> P2-QA1 -> P2-G3. P2-B1 and P2-B2 must not run concurrently (same module).

## 16. Ranked risks

1. A wrong date is trusted and a student misses an exam (high impact). Mitigation: verbatim snippets, needs-date discipline, mandatory approval, neutral "check anything that matters" copy, eval with measured numbers.
2. Prompt injection or hostile files via an untrusted document (high). Mitigation: sandboxed extraction, inert rendering, schema validation, no tool access, red-team fixtures, security review.
3. Overwhelm from a long, alarming list undermines the ADHD purpose (medium-high). Mitigation: sectioned review, neutral styling, ADHD review before build; unvalidated until G4.
4. Privacy/copyright of uploaded course documents and third-party AI processing (medium-high). Mitigation: mock-only now, legal review, delete/export, no logging of content.
5. Mock sync is mistaken for real sync (medium). Mitigation: "Demo calendar" labeling, startup guard, documentation says mocked.
6. Cost overrun from large documents (medium). Mitigation: size/chunk caps, per-student AI budget (PO-4).
7. Scanned syllabi are common and OCR is excluded, causing a dead-end (medium). Mitigation: clear paste/alternate path (S2 AC3); decision P2-D2.
8. Scope creep into reminders and planning (medium). Mitigation: non-goals.

## 17. Verification status

Nothing here has been implemented, run, or tested. All limits, thresholds, time windows and copy are proposals. No gate (G1-G5) is affected or cleared, and this slice authorizes no real-student data, real Claude spend, or real Calendar writes. Acceptance criteria are for qa-engineer to convert into automated and manual tests.
