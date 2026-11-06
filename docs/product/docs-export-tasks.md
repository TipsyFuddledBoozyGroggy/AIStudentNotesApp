# Item 3 task breakdown: export notes to a Demo Doc (PROPOSAL)

Status: **PROPOSAL by google-integration, 2026-11-06.** Nothing here is built or run. Basis: finish-backlog item 3 (approved as a mock by the PO, PO-20cb and PO-20cc: real Google stays OFF; no tokens, no OAuth, no network). Contract: `docs/API_CONTRACTS_DOCS_EXPORT.md` + `src/shared/contracts/docs.export.v1/`. Design: `docs/architecture/docs-export-demo-adr.md`. **All user-facing wording below is DRAFT** until `neurodivergent-ux-specialist` reviews it. It is not consent, recording or Demo-source text (G1) and must not change any of that wording. **Revised 2026-11-06 to apply the security design review `docs/security/docs-export-design-review.md` (DXD-01..DXD-13) and the Lead's answers (Q-A single transaction, Q-B additive `SessionInfo`, Q-C `Development|Test`, Q1-Q7).**

Order: the Lead freezes (or accepts) the contract text first; then backend and frontend run in parallel against it (frontend on MSW). Item 2 (Notes export, built) is the dependency.

## 1. File ownership (disjoint)

| Owner | Files (create unless marked edit) |
|---|---|
| Lead / google-integration (contract; nobody else edits) | `src/shared/contracts/docs.export.v1/**`, `docs/API_CONTRACTS_DOCS_EXPORT.md`, `docs/architecture/docs-export-demo-adr.md`, this file; Lead edits: the `docs/API_CONTRACTS.md` placeholder row, `docs/DECISIONS.md` row, `.github/workflows/ci.yml` shard list (add `e2e/docs-export.spec.ts` to exactly one shard, flows-b suggested), `docs/PROGRESS.md` |
| backend-engineer | `src/api/src/Modules/Docs/Tether.Docs/**`, `src/api/src/Modules/Docs/Tether.Docs.Contracts/**` (new, with migrations); `src/api/src/Modules/Ai/Tether.Ai.Contracts/**` (new) and the minimal `Tether.Ai` edit that implements `INotesSnapshotReader` (edit); `src/api/src/Modules/Lectures/Tether.Lectures.Contracts/SessionPorts.cs` (edit: additive `SessionInfo.Title`, `TimeZone` if the session stores one, and a port that takes the caller's `DbTransaction` and returns the session `FOR KEY SHARE`) plus its implementation in `Tether.Lectures` (edit, only that port and the two fields); `src/api/src/Tether.SharedKernel/Security/RootSecret.cs` or wherever `DerivedKeys.Labels` lives (edit: add label `docs-content-digest.v1`); `src/api/src/Tether.Host/HostServices.cs`, `Program.cs`, `appsettings*.json` (edit: register module, map routes with the `docs-read` / `docs-write` buckets, `Docs:*` settings); `src/api/Tether.slnx` (edit); `docker-compose.dev.yml` (edit: the Development-only fault flag); tests: `src/api/tests/Tether.DocsTests/**` (new), `src/api/tests/Tether.ContractTests/DocsExportContractTests.cs` (new), `src/api/tests/Tether.IntegrationTests/DocsExport*.cs` (new), `src/api/tests/Tether.ArchitectureTests/ModuleBoundaryTests.cs` (edit: allow-lists, pinned surfaces and Docs rules only), `PersistenceTests.cs` (edit: the cross-schema FK pin only) |
| frontend-engineer | `src/web/src/lib/docs/**` (new: types, parse/validators, client, idempotency key), `src/web/src/components/docs/**` (new: dialog, Demo Doc page body, copy, tests), `src/web/src/app/demo-docs/[docId]/page.tsx` (new), `src/web/src/mocks/docsHandlers.ts` + `docsDb.ts` (new) and one registration line in `src/web/src/mocks/handlers.ts` (edit), one mount point in `src/web/src/components/lecture/NotesPanel.tsx` (edit: a button beside Export notes / Copy notes and the dialog mount; no copy added to `lecture/copy.ts`), `src/web/e2e/docs-export.spec.ts` (new), `src/web/e2e/users.ts` (edit only if a new user key is needed) |
| database-architect (review only) | reads the `docs` migration and the lock order; no file ownership |
| security-engineer (review only) | reads routes, tests, log canary; owns the findings register |

Backend and frontend share no file. The only shared surfaces are the schemas (Lead-owned, read-only to both) and the CI shard line (Lead).

## 2. backend-engineer tasks

| # | Task | Acceptance tests (name them `DX-B..`) |
|---|---|---|
| B1 | Module skeleton, boundaries, `Docs:Provider` startup guard (anything but `demo_docs` fails startup) | B01 ModuleBoundaryTests: Docs references only SharedKernel, Lectures.Contracts, Ai.Contracts, Docs.Contracts (no Courses.Contracts); Ai references only its own Contracts and no Docs/Google; `Ai.Contracts` and `Docs.Contracts` are in the contracts-do-not-depend loop and their public surfaces are pinned with `AssertPublicSurface` (Ai.Contracts: only `INotesSnapshotReader` and its snapshot record); no HttpClient/System.Net.Http/Google type in Docs sources; no member named token/secret/credential/refresh in Docs.Contracts. B02 startup fails with `Docs:Provider=google_docs`. B02a the notes reader has no `userId` parameter and returns null when the ambient scope is another user; its snapshot has no segment id, transcript text or AI type. |
| B2 | Migration `docs` schema (5 tables, composite FKs with cascade, unique indexes, no `RESTRICT` between docs tables) | B03 `PersistenceTests` pin names the three new cross-schema FKs (`demo_docs`, `doc_exports`, `doc_targets` -> `lectures.lecture_sessions`) and any identity FK. B03a `idempotency_keys` cascades through composite FK `(export_id, user_id)`. B04 deleting a session removes exports, targets, Demo Docs and idempotency rows, keeps audit rows. B05 purging a course does the same for all its sessions. B05a source-scan test: every raw SQL string in Docs has a `user_id` predicate (like the bulk-call ban). B05b a full account erasure test lists `docs.audit_events` among the tables removed (PO-20aw; retention otherwise subject to PO-20ca/L-14). |
| B3 | `INotesSnapshotReader` + document builder (deterministic; title from `SessionInfo.Title` with fallback and date in the stored zone or UTC labelled UTC, sections, bullets, citations as `{startMs}`, Final summary when this press made it, closing source paragraph, **a closing paragraph saying where the notes stop when `truncated`**) and HMAC digest under HKDF label `docs-content-digest.v1` | B06 builder output equals a golden fixture and passes `docs-preview` schema. B07 no segment id, transcript text outside notes, or token in output. B08 over 400 blocks gives `canWrite=false`, `too_large`, nothing cut (count asserted). B09 same notes give the same digest; changing the lecture title, the session id or the notes id changes it; a different label or key changes it; the canonical serializer is pinned by a golden digest vector (NFC, member order, escapes). B09a truncated notes: `notesTruncated=true`, the extra paragraph present, Demo Doc shows it (DXD-13). B09b the digest key is not the idempotency or audit key (test on the derived-key labels). |
| B4 | Preview route (`docs-read` bucket) | B10 writes no row except counters, calls no provider, writes no audit row (spy on provider and DbContext). B11 `409` if not Stopped or notes not Ready; **`404` for a `notesId` that is not the session's current notes id, identical to a foreign session's 404 in status, body and shape**. B12 `existing` and `actions` for none / existing stale / existing up to date. B12a upper-case, braced or `urn:` session ids are `400` before any lookup; the same GUID in canonical form reaches the session. |
| B5 | Confirm route + `DemoDocsProvider` + idempotency + retry (`docs-write` bucket; ONE transaction per attempt; Lead decision Q-A) | B13 first confirm creates exactly one Demo Doc, `version=1`, `outcome=created`. B14 same key and body again: same record, `Idempotency-Replayed: true`, row count unchanged. B15 same key, different body: `400` (fingerprint compared after the body read, before validation). B16 new key, same digest, `update`: `unchanged`, version stays. B17 changed title then `update`: same `docId`, `version=2`, `updated`. B18 `create_another`: new `docId`, old doc untouched, pointer moved. B19 wrong digest: `412`, nothing written, **no idempotency key stored**. B20 wrong `expectedExistingVersion`: `412`. **B20a `expectedExistingDocId` of another document at the same version: `412` (DXD-05); previewed against A v1, then A deleted and B created at v1, confirm for A: `412`, B untouched.** B21 `mode` not in actions: `409`. B22 missing `acknowledgedDemo` / real provider / missing `expectedExistingDocId`: `400`. **B23 two concurrent confirms with the SAME key: exactly one document, both get the same body, the second with `Idempotency-Replayed: true` (one named outcome).** B23a two concurrent confirms with DIFFERENT keys and `create` for one lecture: one wins; the other gets `412` or `409 invalid_state` (the mode is no longer allowed), never two documents. B24 provider write and record are in one transaction: injected failure after the document insert rolls the document back (savepoint) and stores the failed export row. B25 key replay after lecture delete: `404`. B25a two users, same key, same body: independent. B25b a `4xx` at steps 8-9 stores no key. B25c `23503` (session deleted mid-write) is `404` and `23505` on the key is a replay, never `500`. **B25d lock order: with `CommandTrap` and `pg_blocking_pids`, confirm against lecture delete, confirm against course purge and student delete against lecture delete take session first, then advisory lock, then docs rows; no ordering where docs rows are locked before the session; the tests tolerate losing the race (L-VICTIM: record, roll back, repeat on fresh data) and a deadlock retry repeats the whole attempt without raising `attempt`.** |
| B6 | Failure and retry states; fault injection | B26 injected `provider_unavailable`: `200`, `status=failed`, `retryable`, `retryAfterSeconds`, nothing written, existing doc unchanged, key stored with the failed row. B27 same key and body after the fault is cleared: succeeds, `attempt=2`, one document; **if the notes changed in between the retry returns `412`/`409` (steps 8-9 re-run)**. B28 fifth failed attempt: `retry.allowed=false`; sixth send `409 retry_limit_reached`. B29 injected `rate_limited` carries the provider seconds capped at 3600. B30 `provider_rejected` is not retryable. B31 (removed: there is no `pending` state or reaper in the demo; `export_in_progress` is never emitted; a test asserts that). **B32 startup fails with ANY `Docs:Demo:Faults:*` key present (not only `Enabled=true`) in `Production`, `Staging` and `Testing`; allowed only in `Development` and `Test`.** B32a header is ignored with no `400` when the flag is off. B32b the `slow` fault is capped at 5 s, honours cancellation, and a test shows it cannot hold the session lock past the cap. |
| B7 | State, Demo Doc read, delete (Q3 yes) | B33 `GET state` before and after. B34 `GET /demo-docs/{id}` returns blocks identical to the preview. B35 delete: 204, target cleared, audit `doc_deleted`, next export is `create`; a `create_another` leftover can be deleted by its id. |
| B8 | Authorization / IDOR | B36 route-by-route table: two users with overlapping ids on all five routes give `404` for the other's session/doc/export, and no `409/412/413/415/429` leaks (P2 section 2.1 order). B37 missing Origin on POST/DELETE: `403 csrf_failed`. B38 unauthenticated: `401`. B39 non-canonical ids (`DDC_`, upper-case GUID, braces): `400`. |
| B9 | Privacy / logging / rates | B40 log canary: a notes bullet, the lecture title, the digest and a fake token string are never in logs on success, `412`, provider failure, replay, delete. B41 audit row has exactly the content-free columns (reflection test: no text column). B42 no AI call is made (AI usage table unchanged, mock client call count zero) on preview and confirm. **B43 `docs-read` and `docs-write` are separate buckets: preview cannot exhaust `docs-write` or the live-lecture/Quick recap bucket and vice versa; the 11th confirm in a minute is `429` with `Retry-After`; body over 2 KB `413`; a slow body trips the read deadline and the data-rate floor.** B43a the 201st Demo Doc for a user is refused under the advisory lock (race-free). |
| B10 | Contract tests | B44 `DocsExportContractTests`: all 20 valid fixtures pass and all 42 invalid are rejected; `id`/`timestamp`/`idempotencyKey` identical to syllabus; error-code table equals schema; no token-like property names. |

Run recipe: `docs/agents/project-playbook.md` section 2 (docker sdk container, stop the dev API first, pooled connection string). Report real counts.

## 3. frontend-engineer tasks

| # | Task | Acceptance tests (name them `DX-F..`) |
|---|---|---|
| F1 | `lib/docs`: types, strict parsers mirroring the schemas (reject unknown fields, as `lib/syllabus/parse.ts`; **reject the `external` `docRef` branch in v1**), client (preview, confirm, state, read, delete), Idempotency-Key generator (one key per confirm attempt; **reused on Retry**), `[docId]` route parameter validated against the id pattern before building an API path | F01 parsers accept every valid fixture and reject every invalid fixture (read the JSON from `src/shared/contracts/docs.export.v1/fixtures`). F02 a body that does not parse shows the calm "could not show this" state, never partial data. F03 client never sends text, title or document; the confirm sends `expectedExistingDocId` and `expectedExistingVersion` from the preview it showed. F04 Retry reuses the same key and body; a new preview makes a new key. F04a a docId such as `../x` or `ddc_%2e` never reaches a request path. |
| F2 | Mock handlers (MSW) implementing the contract incl. idempotency, update/create_another, faults | F05 handler unit tests mirror B13-B21 and B26-B30 against the same fixtures. |
| F3 | Dialog (states in section 4) mounted from the Notes sheet; the button shows only when notes are `Ready` | F06 button absent for Pending/Unavailable/BudgetLimited/NotNeeded. F07 preview shows nothing "saved" and no success wording before confirm. F08 confirm disabled until the preview loaded and (if a document exists) a choice is made. F09 busy: control `aria-busy`, double press sends one request. F10 each state in section 4 renders its draft text and the right buttons. F11 stale (412) refetches the preview and says nothing was saved. F12 focus moves to the dialog heading on open and back to the button on close; Escape closes without writing; no new `aria-live` (use the existing announcer only). F13 copy lint passes; no raw hex, tokens, new CSS. F14 works in quiet mode and low-stimulation (no motion needed). F15 axe: no violations on each state, 44 px targets, five themes x light/dark geometry gate unchanged. |
| F4 | Demo Doc page `/demo-docs/[docId]` | F16 renders title and blocks as text only (a block containing `<img onerror>` or `[x](javascript:...)` shows as literal text); citations shown as lecture times; banner says it is a practice copy. F17 foreign/missing doc id shows the same calm not-found as a missing lecture. F18 no `dangerouslySetInnerHTML`. F18a a notes-truncated document shows its closing "where the notes stop" paragraph. |
| F5 | Real-stack e2e `e2e/docs-export.spec.ts` (settle for hydration, wait for not `aria-busy`; serial) | F19 seeded Stopped lecture with Ready notes: preview -> confirm -> success -> open Demo Doc shows the same bullets. F20 second export, same notes: "already matches", nothing written. F21 forced failure (Q5 header) then Retry succeeds with one document. F22 delete the lecture: Demo Doc URL is not found. F23 a second user cannot open the first user's Demo Doc URL. |

Before reporting: `npx.cmd tsc --noEmit -p .`, `npx.cmd eslint .`, `npx.cmd vitest run`, and the e2e spec alone with a reuse config (playbook sections 2 and 4). Do not run `next build` while the dev server is up.

## 4. Exact UI states (DRAFT wording; plain, calm, no alarm, no streaks, no time pressure)

Vocabulary: "Demo Doc" always; never "Google Doc" or "Google" anywhere in this flow. A Demo Doc is "a practice copy kept inside Tether". Notes are "estimates" when `notesSource` is `mock` (reuse the existing Demo notes line from `lecture/copy.ts`; do not reword it).

**0. Entry (on the Notes sheet, beside Export notes and Copy notes)**
- Button: `Save to a Demo Doc`
- Help line: `A Demo Doc is a practice copy kept inside Tether. Nothing is sent to Google.`
- If one exists (from state): `Saved to a Demo Doc on {date}.` with link `Open Demo Doc`.

**1. Preview (dialog, nothing saved yet)**
- Heading: `Check before saving`
- Line 1: `This is exactly what will be saved. Nothing has been saved yet.`
- Where: `Where: a Demo Doc (a practice copy inside Tether, not Google Docs)`
- Title: `Title: {title}`
- Count: `{n} sections, {m} bullets. Each bullet keeps its lecture time.`
- The notes estimate line (existing wording) when mock.
- A read-only, scrollable, labelled region showing the blocks as text (heading, bullets with `[mm:ss]`).
- Buttons: `Save to Demo Doc` (primary), `Not now` (tertiary). Loading: `Getting your preview...` (no spinner animation under quiet mode).
- Preview error (could not load): `We could not show the preview. Nothing was saved.` + `Try again` + `Close`.

**1b. Preview with an earlier Demo Doc, notes changed**
- Line: `You saved these notes to a Demo Doc on {date}. The notes or title have changed since.`
- Radio group, no default selected, legend `What would you like to do?`
  - `Update that Demo Doc. Its contents will be replaced with what you see here.`
  - `Make a second Demo Doc. The first one stays as it is.`
- `Save to Demo Doc` stays disabled until one is chosen (hint: `Choose one to continue.`).

**1c. Preview, already matches**
- `Your Demo Doc already matches these notes. There is nothing to save.` Buttons: `Open Demo Doc`, `Close`. No write button.

**1d. Preview, too large / nothing to export**
- `These notes are too long for one Demo Doc. Nothing was cut and nothing was saved. You can still use Export notes.`
- `There is nothing in these notes to save yet.`

**2. Confirm (in flight)**
- Button text changes to `Saving...`, `aria-busy`, other controls inert; the dialog stays open. Announced once via the existing announcer: `Saving to a Demo Doc.`

**3. Success**
- created: `Saved to a Demo Doc.` updated: `Your Demo Doc was updated.` unchanged: `Nothing to update. It already matched.`
- Then: `This copy stays inside Tether. It is deleted when you delete this lecture.`
- Buttons: `Open Demo Doc` (primary), `Close`.

**4. Stale (precondition failed)**
- `The notes changed while you were looking. Here is the new version. Nothing was saved yet.` -> preview re-renders; student confirms again.

**5. Error with retry (provider_unavailable / rate_limited)**
- `That did not save. Nothing was saved and nothing was lost. You can try again.`
- If `retryAfterSeconds`: `Please wait about {n} seconds, then try again.` (the button is enabled; the wait is advice, not a lock, no countdown animation).
- Buttons: `Try again` (same key and body), `Close`.
- After the last attempt: `We stopped trying after a few attempts. Your notes are fine. You can start again from the preview, or use Export notes.` Buttons: `Start again`, `Close`.

**6. Error, not retryable (provider_rejected, unknown)**
- `This Demo Doc could not be saved. Your notes are fine. You can still use Export notes or Copy notes.` Button: `Close`.

**7. (Reserved for a real provider; the demo never shows it.) Another save running (409 export_in_progress)**
- `A save is already running. Give it a moment, then check again.` Button: `Check again`.

**8. Lecture gone / not yours (404)**
- `This lecture is not available.` Button: `Close`. (Same wording as the lecture screens for a missing lecture; no hint whether it exists.)

**9. Demo Doc page**
- Banner: `Demo Doc: a practice copy kept inside Tether. It is not in Google Docs.`
- Body: title, then the blocks as text. Link: `Back to the lecture`. Included (Q3 yes): `Delete this Demo Doc` with a confirm using the existing delete dialog pattern; text `Delete this Demo Doc? Your notes and lecture are not affected.`
- Not found: same calm not-found as other pages.

**10. After deleting the lecture (existing delete screens, not in this item)**
- The existing lecture delete text is owned by the delete flow (PO-20by draft). If it mentions exports, it must say Demo Docs are deleted with the lecture. Flag only; do not edit that copy here.

## 5. Open questions and PO decisions

For the Lead (technical, default given):
- Q1 Contract freeze: **Lead decision: yes, freeze `docs.export.v1` as mock-only after the first green integration run.**
- Q2 `provider` as a const: **Lead decision: const** (one-line widening later; see ADR section 9 for what a real provider also changes on the server).
- Q3 Student-initiated Demo Doc delete: **Lead decision: yes, included** (DELETE `/demo-docs/{docId}`, `docs-write` bucket, B35).
- Q4 Student-editable title: **Lead decision: no.**
- Q5 e2e provider failure: **Lead decision: Development-only request header** gated by `Docs:Demo:Faults:Enabled`, set in `docker-compose.dev.yml` and the CI compose (devops agrees the CI line).
- Q6 Button placement: **Lead decision: Notes sheet only.**
- Q7 Final summary in the document: **Lead decision: yes** when the notes press made it and it is Ready.
- Q-A Demo write model: **Lead decision: single transaction for the demo**; `pending`, reaper and `export_in_progress` are real-provider only (ADR section 9).
- Q-B Title/date source: **Lead decision: the smallest boundary change**, stated: additive `SessionInfo.Title` (and `TimeZone` only if the session already stores one) in `Lectures.Contracts`; no new allow-list edge; the course name is not in the demo title. Open for backend: does the session store a zone? If not, the title carries the UTC date labelled UTC.
- Q-C Fault guards: **Lead decision: `Development|Test`** for Docs (the Syllabus set); Ai and Transcript are not touched.
- Q8 Audit retention for `docs.audit_events` stays tied to the open PO-20ca/D-RETENTION/L-14 question; the table is on the PO-20aw erasure list. Default: keep content-free rows.

For the Product Owner (via the Lead; none blocks the mock):
- P1 Nothing in this item needs a new PO go: it is covered by PO-20cb and PO-20cc (mock only, real Google OFF). I do not quote any PO answer beyond those rows.
- P2 **A real `GoogleDocsProvider` is NOT built and needs its own written PO go** (external system permissions, OAuth consent screen, real sign-in, school-managed account refusal test with the `hd` claim, scope choice `drive.file` versus `documents`, vendor-verified facts). Do not start it from this ADR.
- P3 Wording review: all section 4 text is DRAFT and goes to `neurodivergent-ux-specialist`. Whether "Demo Doc" is the right name for the PO's son is a product wording call.

## 6. Out of scope here

Real Google, OAuth, tokens, Drive picking, import from Docs, sharing, folders, automatic or scheduled export, student-edited document bodies, G1 wording, pdf/docx export, reminders (item 5), account deletion (item 11).
