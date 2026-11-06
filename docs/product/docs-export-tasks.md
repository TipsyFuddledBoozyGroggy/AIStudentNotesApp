# Item 3 task breakdown: export notes to a Demo Doc (PROPOSAL)

Status: **PROPOSAL by google-integration, 2026-11-06.** Nothing here is built or run. Basis: finish-backlog item 3 (approved as a mock by the PO, PO-20cb and PO-20cc: real Google stays OFF; no tokens, no OAuth, no network). Contract: `docs/API_CONTRACTS_DOCS_EXPORT.md` + `src/shared/contracts/docs.export.v1/`. Design: `docs/architecture/docs-export-demo-adr.md`. **All user-facing wording below is DRAFT** until `neurodivergent-ux-specialist` reviews it. It is not consent, recording or Demo-source text (G1) and must not change any of that wording.

Order: the Lead freezes (or accepts) the contract text first; then backend and frontend run in parallel against it (frontend on MSW). Item 2 (Notes export, built) is the dependency.

## 1. File ownership (disjoint)

| Owner | Files (create unless marked edit) |
|---|---|
| Lead / google-integration (contract; nobody else edits) | `src/shared/contracts/docs.export.v1/**`, `docs/API_CONTRACTS_DOCS_EXPORT.md`, `docs/architecture/docs-export-demo-adr.md`, this file; Lead edits: the `docs/API_CONTRACTS.md` placeholder row, `docs/DECISIONS.md` row, `.github/workflows/ci.yml` shard list (add `e2e/docs-export.spec.ts` to exactly one shard, flows-b suggested), `docs/PROGRESS.md` |
| backend-engineer | `src/api/src/Modules/Docs/Tether.Docs/**`, `src/api/src/Modules/Docs/Tether.Docs.Contracts/**` (new, with migrations); `src/api/src/Modules/Ai/Tether.Ai.Contracts/**` (new) and the minimal `Tether.Ai` edit that implements `INotesSnapshotReader` (edit); `src/api/src/Tether.Host/HostServices.cs`, `Program.cs`, `appsettings*.json` (edit: register module, map routes, `Docs:*` settings); `src/api/Tether.slnx` (edit); `docker-compose.dev.yml` (edit: one env var for the Development-only fault flag, if Q5 is accepted); tests: `src/api/tests/Tether.DocsTests/**` (new), `src/api/tests/Tether.ContractTests/DocsExportContractTests.cs` (new), `src/api/tests/Tether.IntegrationTests/DocsExport*.cs` (new), `src/api/tests/Tether.ArchitectureTests/ModuleBoundaryTests.cs` (edit: allow-lists and Docs rules only) |
| frontend-engineer | `src/web/src/lib/docs/**` (new: types, parse/validators, client, idempotency key), `src/web/src/components/docs/**` (new: dialog, Demo Doc page body, copy, tests), `src/web/src/app/demo-docs/[docId]/page.tsx` (new), `src/web/src/mocks/docsHandlers.ts` + `docsDb.ts` (new) and one registration line in `src/web/src/mocks/handlers.ts` (edit), one mount point in `src/web/src/components/lecture/NotesPanel.tsx` (edit: a button beside Export notes / Copy notes and the dialog mount; no copy added to `lecture/copy.ts`), `src/web/e2e/docs-export.spec.ts` (new), `src/web/e2e/users.ts` (edit only if a new user key is needed) |
| database-architect (review only) | reads the `docs` migration and the lock order; no file ownership |
| security-engineer (review only) | reads routes, tests, log canary; owns the findings register |

Backend and frontend share no file. The only shared surfaces are the schemas (Lead-owned, read-only to both) and the CI shard line (Lead).

## 2. backend-engineer tasks

| # | Task | Acceptance tests (name them `DX-B..`) |
|---|---|---|
| B1 | Module skeleton, boundaries, `Docs:Provider` startup guard (anything but `demo_docs` fails startup) | B01 ModuleBoundaryTests: Docs references only SharedKernel, Lectures.Contracts, Ai.Contracts, Docs.Contracts; Ai references no Docs; no HttpClient/System.Net.Http/Google type in Docs sources; no member named token/secret/credential/refresh in Docs.Contracts. B02 startup fails with `Docs:Provider=google_docs`. |
| B2 | Migration `docs` schema (5 tables, composite FKs with cascade, unique indexes) | B03 persistence allow-list passes. B04 deleting a session removes exports, targets, Demo Docs and idempotency rows, keeps audit rows. B05 purging a course does the same for all its sessions. |
| B3 | `INotesSnapshotReader` + document builder (deterministic; title, sections, bullets, citations as `{startMs}`, Final summary when this press made it, closing source paragraph) and HMAC digest | B06 builder output equals a golden fixture and passes `docs-preview` schema. B07 no segment id, transcript text outside notes, or token in output. B08 over 400 blocks gives `canWrite=false`, `too_large`, nothing cut (count asserted). B09 the same notes always give the same digest; changing the lecture title changes it; a different user key changes it. |
| B4 | Preview route | B10 writes no row except counters, calls no provider, writes no audit row (spy on provider and DbContext). B11 `409` if not Stopped or notes not Ready; `404` for a notes id that is not the session's. B12 `existing` and `actions` for none / existing stale / existing up to date. |
| B5 | Confirm route + `DemoDocsProvider` + idempotency + retry | B13 first confirm creates exactly one Demo Doc, `version=1`, `outcome=created`. B14 same key and body again: same record, `Idempotency-Replayed: true`, row count unchanged. B15 same key, different body: `400`. B16 new key, same digest, `update`: `unchanged`, version stays. B17 changed title then `update`: same `docId`, `version=2`, `updated`. B18 `create_another`: new `docId`, old doc untouched, target pointer moved. B19 wrong digest: `412`, nothing written. B20 wrong `expectedExistingVersion`: `412`. B21 `mode` not in actions: `409`. B22 missing `acknowledgedDemo`/real provider: `400`. B23 concurrent two confirms for one lecture: one wins, other gets `409 export_in_progress` or the same replay, never two documents (race test, deterministic with `CommandTrap`). B24 crash between provider write and record update then retry with the same key: still one document (unique `origin_export_id`). B25 key replay after lecture delete: `404`. |
| B6 | Failure and retry states | B26 injected `provider_unavailable`: `200`, `status=failed`, `retryable`, `retryAfterSeconds`, nothing written, existing doc unchanged. B27 same key and body after the fault is cleared: succeeds, `attempt=2`, one document. B28 fifth failed attempt: `retry.allowed=false`; sixth send `409 retry_limit_reached`. B29 injected `rate_limited` carries the provider seconds capped at 3600. B30 `provider_rejected` is not retryable. B31 a `pending` older than 60 s is reaped to failed/retryable. B32 fault injection fails startup outside Development/Test. |
| B7 | State, Demo Doc read, optional delete (Q3) | B33 `GET state` before and after. B34 `GET /demo-docs/{id}` returns blocks identical to the preview. B35 delete: 204, target cleared, audit `doc_deleted`, next export is `create`. |
| B8 | Authorization / IDOR | B36 route-by-route table: two users with overlapping ids on all five routes give `404` for the other's session/doc/export, and no `409/412/413/415/429` leaks (P2 section 2.1 order). B37 missing Origin on POST/DELETE: `403 csrf_failed`. B38 unauthenticated: `401`. B39 non-canonical ids (`DDC_`, braces): `400`. |
| B9 | Privacy / logging | B40 log canary: a notes bullet, the lecture title, the digest and a fake token string are never in logs on success, `412`, provider failure, replay, delete. B41 audit row has exactly the content-free columns (reflection test: no text column). B42 no AI call is made (AI usage table unchanged, mock client call count zero) on preview and confirm. B43 limits: body over 2 KB `413`; 11th confirm in a minute `429` with `Retry-After`. |
| B10 | Contract tests | B44 `DocsExportContractTests`: all 19 valid fixtures pass and all 34 invalid are rejected; `id`/`timestamp`/`idempotencyKey` identical to syllabus; error-code table equals schema; no token-like property names. |

Run recipe: `docs/agents/project-playbook.md` section 2 (docker sdk container, stop the dev API first, pooled connection string). Report real counts.

## 3. frontend-engineer tasks

| # | Task | Acceptance tests (name them `DX-F..`) |
|---|---|---|
| F1 | `lib/docs`: types, strict parsers mirroring the schemas (reject unknown fields, as `lib/syllabus/parse.ts`), client (preview, confirm, state, read, delete), Idempotency-Key generator (one key per confirm attempt; **reused on Retry**) | F01 parsers accept every valid fixture and reject every invalid fixture (read the JSON from `src/shared/contracts/docs.export.v1/fixtures`). F02 a body that does not parse shows the calm "could not show this" state, never partial data. F03 client never sends text, title or document. F04 Retry reuses the same key and body; a new preview makes a new key. |
| F2 | Mock handlers (MSW) implementing the contract incl. idempotency, update/create_another, faults | F05 handler unit tests mirror B13-B21 and B26-B30 against the same fixtures. |
| F3 | Dialog (states in section 4) mounted from the Notes sheet; the button shows only when notes are `Ready` | F06 button absent for Pending/Unavailable/BudgetLimited/NotNeeded. F07 preview shows nothing "saved" and no success wording before confirm. F08 confirm disabled until the preview loaded and (if a document exists) a choice is made. F09 busy: control `aria-busy`, double press sends one request. F10 each state in section 4 renders its draft text and the right buttons. F11 stale (412) refetches the preview and says nothing was saved. F12 focus moves to the dialog heading on open and back to the button on close; Escape closes without writing; no new `aria-live` (use the existing announcer only). F13 copy lint passes; no raw hex, tokens, new CSS. F14 works in quiet mode and low-stimulation (no motion needed). F15 axe: no violations on each state, 44 px targets, five themes x light/dark geometry gate unchanged. |
| F4 | Demo Doc page `/demo-docs/[docId]` | F16 renders title and blocks as text only (a block containing `<img onerror>` or `[x](javascript:...)` shows as literal text); citations shown as lecture times; banner says it is a practice copy. F17 foreign/missing doc id shows the same calm not-found as a missing lecture. F18 no `dangerouslySetInnerHTML`. |
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

**7. Another save running (409 export_in_progress)**
- `A save is already running. Give it a moment, then check again.` Button: `Check again`.

**8. Lecture gone / not yours (404)**
- `This lecture is not available.` Button: `Close`. (Same wording as the lecture screens for a missing lecture; no hint whether it exists.)

**9. Demo Doc page**
- Banner: `Demo Doc: a practice copy kept inside Tether. It is not in Google Docs.`
- Body: title, then the blocks as text. Link: `Back to the lecture`. Optional (Q3): `Delete this Demo Doc` with a confirm using the existing delete dialog pattern; text `Delete this Demo Doc? Your notes and lecture are not affected.`
- Not found: same calm not-found as other pages.

**10. After deleting the lecture (existing delete screens, not in this item)**
- The existing lecture delete text is owned by the delete flow (PO-20by draft). If it mentions exports, it must say Demo Docs are deleted with the lecture. Flag only; do not edit that copy here.

## 5. Open questions and PO decisions

For the Lead (technical, default given):
- Q1 Contract id and freeze: `docs.export.v1` DRAFT now; freeze as "mock-only" like `syllabus.v1` after the backend/frontend pass? Default: freeze after the first green integration run.
- Q2 `provider` as a const (safer, one-line widening later, as written) versus an enum with a server-side refusal now. Default: const.
- Q3 Student-initiated Demo Doc delete: include? Default: include (small, and lets the PO reset while testing).
- Q4 Student-editable title in the preview: Default: no; title is built from the lecture title, course and date.
- Q5 How the real-stack e2e forces a provider failure: a Development-only header gated by `Docs:Demo:Faults:Enabled`, set in `docker-compose.dev.yml` and the CI compose. Default: yes. Needs `devops-engineer` agreement on the CI compose line.
- Q6 Button placement: Notes sheet only, or also the reopen page? Default: wherever Export notes appears (same component).
- Q7 Include the Final summary in the document? Default: yes when the notes press made it and it is Ready, as item 2 does.
- Q8 Audit retention for `docs.audit_events` stays tied to the open PO-20ca/D-RETENTION question. Default: keep content-free rows.

For the Product Owner (via the Lead; none blocks the mock):
- P1 Nothing in this item needs a new PO go: it is covered by PO-20cb and PO-20cc (mock only, real Google OFF). I do not quote any PO answer beyond those rows.
- P2 **A real `GoogleDocsProvider` is NOT built and needs its own written PO go** (external system permissions, OAuth consent screen, real sign-in, school-managed account refusal test with the `hd` claim, scope choice `drive.file` versus `documents`, vendor-verified facts). Do not start it from this ADR.
- P3 Wording review: all section 4 text is DRAFT and goes to `neurodivergent-ux-specialist`. Whether "Demo Doc" is the right name for the PO's son is a product wording call.

## 6. Out of scope here

Real Google, OAuth, tokens, Drive picking, import from Docs, sharing, folders, automatic or scheduled export, student-edited document bodies, G1 wording, pdf/docx export, reminders (item 5), account deletion (item 11).
