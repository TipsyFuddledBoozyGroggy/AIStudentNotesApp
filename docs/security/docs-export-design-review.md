# Security review: Demo Doc export design (item 3, docs.export.v1, mock only)

Reviewer: security-engineer. Date: 2026-11-05 (system date; the reviewed documents are dated 2026-11-06, so the machine clock and the authors' dates disagree by a day). HEAD `ddc1c53`, working tree clean before this review. Register rows: **DXD-01..DXD-13** in `docs/security/findings-register.md` (Part II, "Demo Doc export design review"). Severity vocabulary per `docs/security/severity-policy.md`; ratings calibrated to the personal, one-user, mock-only scope (PO-15/16, PO-20cb, PO-20cc). This review clears no gate.

**Re-review 2026-11-05 (commit f7aa2ef): see section 6.** DXD-01..DXD-13 are FIXED-CONTRACT-ONLY. There are four new LOW rows (DXD-14..DXD-17). Backend implementation may start.

## 0. What I read and what I ran

**READ:** `docs/architecture/docs-export-demo-adr.md`, `docs/API_CONTRACTS_DOCS_EXPORT.md`, `docs/product/docs-export-tasks.md`, all 8 schemas and the fixture list in `src/shared/contracts/docs.export.v1/` (fixture text sampled); `CLAUDE.md` invariants; `docs/agents/project-playbook.md`; `docs/DECISIONS.md` ADR-020..020d, PO-20aw, PO-20by, PO-20ca, PO-20cb, PO-20cc; legal row L-14; `RootSecret.cs` (DerivedKeys labels), `SeparateRateBucket.cs`, `OwnershipSaveChangesInterceptor.cs`, `Tether.Host/Program.cs` (limiter chain lines 79-130, header middleware 157-175), `SyllabusModule.cs` (startup guards), Syllabus idempotency and digest code (`SyllabusService.cs`, `Support.cs`, `ItemService.cs` by grep), `Lectures.Contracts/SessionPorts.cs`, `SessionCutoff.cs` header, `AiScope.cs`, `ModuleBoundaryTests.cs` allow-lists (grep), `PersistenceTests.cs` cross-schema FK pin (grep), lecture.v1 `notes.schema.json` (`truncated`), web `notesExport.ts` (truncated line, grep).

**RAN (read-only):** `git status`, `git show --name-only ddc1c53` and a grep of that commit for personal names, user folders, e-mail addresses and key prefixes (no hit; author is the GitHub noreply address), greps over `src/api/src` listed in the rows, and the register's section 6 per-status count recipe before and after the edit. **Not run:** no build, no test, no schema validation, no app. Nothing is implemented, so every finding is design-stage.

## 1. Result

0 CRITICAL, 0 HIGH. 2 MEDIUM (DXD-01, DXD-02), 11 LOW. Nothing here blocks a push. The design is sound in its main choices: server-built document, client sends ids only, HMAC digest bound confirm, const `provider`, explicit `acknowledgedDemo`, 404 for foreign ids, content-free audit, text-only rendering, cascade with the lecture, startup guard on `Docs:Provider`. The defects are gaps and contradictions an implementer would otherwise fill in ad hoc, chiefly the write transaction and lock order (DXD-01) and an overclaim that a real Google provider changes nothing on the server side (DXD-02).

## 2. Required changes BEFORE implementation (contract, ADR, task text)

1. **DXD-01 Write transaction and lock order (MEDIUM).** State one model for the demo: a single transaction per attempt (then `pending`, the 60 s reaper, `export_in_progress` and B24 are real-provider-only and must be labelled so, or removed), or a two-phase model (then specify both transactions). For every Docs writer transaction (confirm, student delete, any job): the **first** lock is the session row `FOR KEY SHARE` through a new `Lectures.Contracts` port that takes the caller's `DbTransaction` (same pattern as `ISessionSequencer`); null means `404` and is the C-21 re-check. Then `pg_advisory_xact_lock` on (user, session) to serialise the first `create`, when no `doc_targets` row exists to lock. Then docs rows. "Via the FK" is not enough: a `SELECT ... doc_targets FOR UPDATE` before the first child insert takes the locks in the reverse order to the cascade delete (L-LOCKORDER). The reaper updates one table per transaction. Map `23503` (session gone mid-write) to `404` and the idempotency `23505` to a replay, never a 500. A deadlock retry repeats the whole attempt without increasing `attempt`. Tests: `CommandTrap` plus `pg_blocking_pids` for confirm against lecture delete, against course purge, and student delete against lecture delete; tolerant of losing the race (L-VICTIM).
2. **DXD-03 Rate buckets.** No "30/min read bucket" exists. A POST without `SeparateRateBucket` metadata lands in the shared `w:` write bucket (`Program.cs:127-129`) that Quick recap uses (R2-14). Contract section 6 and task B43: preview gets `WithSeparateRateBucket("docs-read", n)`, confirm and delete get `"docs-write"` (10/min), as Syllabus does (`SyllabusEndpoints.cs:25-26`). Add a body read deadline and a minimum data rate (playbook section 5), plus a test that preview cannot exhaust the write bucket.
3. **DXD-04 Idempotency order and retry.** Section 4.1 puts the fingerprint check (step 5) before the body read (step 7), which is impossible. Change it to: look up by key at step 5, compare the fingerprint after the body read and before validation (the Syllabus order). A same-key retry after a failed attempt is a new write: it passes step 6 and re-runs steps 8-9 (state, digest, expected version and document, mode), so it can return `409` or `412`. A key is stored only together with an export row (a `4xx` stores nothing). Same key sent at the same time: the second request waits, then replays the winner. B23's "409 or the same replay" must name one outcome (playbook: first, coalesced and replayed paths identical). New test: two users with the same key and the same body run independently.
4. **DXD-05 Bind the document, not only its version.** Add `expectedExistingDocId` to `docs-export-request` (required, `null` only with `create`), plus fixtures. Today a confirm previewed against document A, version 1, can update a different document B at version 1 after a delete or create in another tab. Harmless in the demo. In Google it would overwrite the wrong file, and adding a required field after the contract is frozen is a breaking change.
5. **DXD-06 Digest key.** Name a new HKDF label (for example `docs-content-digest.v1` in `DerivedKeys.Labels`). Never reuse the idempotency ring or the audit key. Prefix the HMAC input with the label and include `sessionId`. Name the canonical serializer (property order, number and string escaping, NFC). State that the digest is an equality check on content, never proof of authorization.
6. **DXD-07 Fault injection gating.** The Docs guard refuses startup outside the allowed environments if **any** `Docs:Demo:Faults:*` key is present, not only `Enabled=true` (as `SyllabusModule.cs:35-37` does). It uses one allowed set: the repo has two today (Syllabus `Development|Test`, Ai and Transcript `Development|Test|Testing`), so pick the shared helper or the stricter one. The header is ignored when the flag is off (no 400 that reveals the feature). Faults fire inside `DemoDocsProvider`. The `slow` fault is capped and honours cancellation, so it cannot hold the session lock and a pooled connection without limit. Tests for `Production`, `Staging` and `Testing` with the flag set.
7. **DXD-08 Module boundary and AI isolation.** Pin the public surface of `Tether.Ai.Contracts` (only `INotesSnapshotReader` and its snapshot record) and of `Tether.Docs.Contracts` with `AssertPublicSurface`, and add both to the contracts-do-not-depend loop. Add `Tether.Ai -> Tether.Ai.Contracts` to the allow-list. `Tether.Ai` and `Tether.Ai.Contracts` reference no `Tether.Docs*` or `Tether.Google*`. The reader takes the ambient user and refuses a different `userId` argument (sibling of M2B3-11). It returns notes fields only: no segment ids, transcript text, prompt material or AI client types. Name where the builder gets the lecture title, course name and zone: `SessionInfo` has none of them, and `Courses.Contracts` is not in the proposed allow-list. Use an additive `SessionInfo` field or an explicit `Courses.Contracts` entry, not a quiet widening during the build.
8. **DXD-09 Persistence.** Task B03 must name the new cross-schema FKs to add to the `PersistenceTests` pin (one per `docs` table that references `lectures.lecture_sessions`, plus any `identity` FK). `docs.idempotency_keys` has no `session_id` and no FK, so nothing cascades it: add a composite FK `(export_id, user_id)` to `doc_exports` with cascade, or a session FK. Use no `RESTRICT` between docs tables (an immediate check inside a cascade can fail the lecture delete). Every raw SQL statement carries a `user_id` predicate, because `OwnershipSaveChangesInterceptor` sees EF-tracked saves only. Add a source-scan test like the bulk-call ban.
9. **DXD-10 Deletion and audit.** Add `docs.audit_events` to the evidence that full account erasure removes (PO-20aw), and mark it as subject to the open PO-20ca follow-up and L-14. Either implement the NAS-01 claim in 4.1 (Docs implements `ISessionLifecycleObserver` and keeps its own tombstone, applied on all five routes) or delete the claim. The mock's cascade already removes a racing export, so deleting the claim is acceptable for the demo.
10. **DXD-12 One answer for the wrong notes id.** Preview gives `404` and confirm gives `409` (4.1 step 9 against 4.2). Use `404` in both, decided only by comparing with the session's current notes id. Never look up a `notesId` on its own. Fix the fixtures' `ses_...` session ids: lecture routes use GUIDs (`{id:guid}`). Pin the canonical form so `{BRACED}` or upper-case GUIDs do not reach the same session (B39).
11. **DXD-13 Truncated notes.** The builder (4.2, B3) drops `notes.truncated` and `coveredToMs`. The Markdown export already says where the notes stop (`notesExport.ts:122`). The Demo Doc must carry the same line, or it looks complete when it is not (L-DROP class).

## 3. Nice-to-haves (do not block implementation)

- DXD-11: give the `label` fields (`destination.label`, `docRef.label`) a pattern or a `const` (no control or bidi characters). In v1, have the client parser reject the `external` `docRef` branch: the schema allows it with `provider: demo_docs`. Validate the `[docId]` route param against the id pattern before building the API path (client-side path traversal). The new page inherits the missing web CSP (M2C-29).
- DXD-03 residue: make the 200-documents-per-user cap race-free under the same advisory lock. Worst-case storage is about 400 blocks x 500 code points x 4 bytes, roughly 0.8 MB per document and 160 MB per user, and dev login makes users freely (PPR-03).
- DXD-06 residue: a root-secret rotation makes every stored `last_digest` stale, so `upToDate` turns false and an `update` rewrites identical content. Accept it and write it down, or compare the blocks as well.
- DXD-10 residue: documents left behind by `create_another` cannot be listed or deleted from the UI, only removed with the lecture. List them in state, or let Q3 delete cover them.

## 4. What a real `GoogleDocsProvider` changes (DXD-02, MEDIUM, precondition)

ADR section 9 says the evaluation order, idempotency, retry rules and audit are "unchanged". On the server they are not:
- The provider call leaves the database transaction. Then `pending`, the reaper, `export_in_progress` and orphan recovery become real. A reaped export whose Google call succeeded late duplicates the document unless the Google side is idempotent. That pre-generated file ids work for Docs and that `requiredRevisionId` behaves as needed are both UNVERIFIED. No lock may be held across the network call.
- Tokens: the provider resolves them inside itself from the encrypted store (GR-2..GR-7). It must assert `DocsWriteRequest.UserId` equals the ambient user and look tokens up user-scoped (confused deputy). Tokens and Google file ids never go in logs, the AI module or responses.
- Scope: `drive.file` only. `documents` reaches every Doc the student has. Choosing it needs a PO decision and a security re-review. Sensitivity class and verification status are UNVERIFIED.
- GR-1 (institution-managed account visibility, MEDIUM, open) applies directly. G1 applies too: lecture-derived content leaves the app for a third party.
- Content: insert plain text only, with no link styles or auto-linking (Google's behaviour UNVERIFIED). Notes are AI output and can carry injected phishing text under the "Made by Tether" line.
- Build `openUrl` on the server from the file id, never from a URL Google returns. Open it with `rel="noopener noreferrer"`.
- Fixed egress hosts, `AllowAutoRedirect=false`, no environment proxy (CV-27 precedent), a global kill switch for provider writes, and lifecycle handling: course purge or lecture delete during an in-flight call leaves a Google document the app has forgotten. Copy must say the document stays.

## 5. Questions for the Lead (not the PO; none blocks the mock)

- Q-A: Demo write model: single transaction (my recommendation) or two-phase? (DXD-01)
- Q-B: Where do lecture title, course name and zone come from: additive `SessionInfo` or `Courses.Contracts`? (DXD-08)
- Q-C: Which shared environment set do fault guards use, `Development|Test` or `Development|Test|Testing`? The same sweep touches Ai and Transcript, which are outside this item. (DXD-07)

No question for the PO arises from this review: a real provider already needs its own PO go (tasks P2).

## 6. Re-review of design rev 2 (commit f7aa2ef, 2026-11-05)

**READ:** the revised `docs/API_CONTRACTS_DOCS_EXPORT.md`, `docs/architecture/docs-export-demo-adr.md`, `docs/product/docs-export-tasks.md`, all 8 schemas, the fixture list, the `docs/API_CONTRACTS.md` row change, and the diff `ddc1c53..f7aa2ef`. I also re-read `SyllabusService.cs:257-275` and grepped the Ai and Transcript callers of `ISessionDirectory`. **RAN:** ajv 2020 (`src/web/node_modules/ajv`, strict off, `date-time` not checked) in a scratch script outside the repo: 20 valid and 42 invalid fixtures, all as expected. Three probes: an `update` with `expectedExistingDocId: null` is rejected; a `dxp_` id in that field is accepted (nit); an `external` docRef with `provider: demo_docs` is accepted, so the client-side rejection is the guard. Also RAN: the register's section 6 count recipe. **Not run:** any build, .NET test or app.

### 6.1 Disposition of DXD-01..DXD-13

All 13 rows are **FIXED-CONTRACT-ONLY**. The defect is closed in the text, but nothing is built, so each stays unresolved until the task tests it names exist and pass. Per-row evidence is in the register.

| Row | Where it is fixed | Tests that will close it |
|---|---|---|
| DXD-01 | contract 4.3, ADR 2 item 4a and 5 (single transaction; session `FOR KEY SHARE` port, then advisory lock, then docs rows; 23503/23505 mapping; deadlock retry) | B23, B23a, B24, B25c, B25d |
| DXD-02 | ADR 9 "Correction of the first draft" | a real provider's own tests and a security re-review (separate PO go) |
| DXD-03 | contract 3, 4.1, 6 (`docs-read`/`docs-write` buckets, body deadline) | B43 (cap residual: DXD-16) |
| DXD-04 | contract 4.1 step 5/7, 4.4 | B15, B19, B23, B25a, B25b, B27 (residual: DXD-15) |
| DXD-05 | `expectedExistingDocId` in the schema and contract 4.3 | B20a, B22, F03 |
| DXD-06 | `docs-common#/$defs/contentDigest`, ADR 7 | B09, B09b |
| DXD-07 | contract 4.8, ADR 7 | B32, B32a, B32b |
| DXD-08 | ADR 4 (pinned surfaces, user-bound reader, additive `SessionInfo`) | B01, B02a (side effect: DXD-17) |
| DXD-09 | ADR 5 "Persistence rules" | B03, B03a, B05a (conflict: DXD-14) |
| DXD-10 | contract 4.7 | B04, B05, B05b, B35 |
| DXD-11 | `label` const, contract 7, F1 | F01, F04a, F16-F18 |
| DXD-12 | contract 2 and 4.1, `$defs/sessionId` | B11, B12a, B39 |
| DXD-13 | `notesTruncated`, contract 4.2 | B09a, F18a |

### 6.2 New findings (all LOW, OPEN)

- **DXD-14:** the fixed docs-row order (`idempotency_keys` before `doc_exports`) conflicts with the new FK `(export_id, user_id)` from `idempotency_keys` to `doc_exports`. The blanket rule "23503 -> 404" would show the failure as a calm 404. Fix: put `doc_exports` first, or make the FK deferrable, and map to 404 only a 23503 from the named session FKs.
- **DXD-15:** "23505 on the key -> replay of the winner" skips the fingerprint check. The key scope does not include the session and the advisory lock is per lecture, so one key sent for two lectures at the same time would replay the other lecture's record. Fix: compare fingerprints (equal: replay; different: 400), as Syllabus does.
- **DXD-16:** the 200-document cap is said to be race-free "under the same advisory lock", but that lock is per (user, session). Fix: add a per-user lock, in a fixed position after the session lock, or use a per-user counter row.
- **DXD-17:** the additive `SessionInfo.Title` also reaches the Ai module (`AiGateway`, `NotesService`, `CatchUpService`, `SummaryEngine` all call `ISessionDirectory`). Fix: a narrow Docs-only port, or a test that Ai never reads `Title`.

### 6.3 Checked and acceptable

The demo now has no pending state, reaper or `export_in_progress`, and the reserved code stays in the schema. Faults are injected inside a savepoint. NAS-01 is replaced by the session lock re-check. Ai and Transcript keep their own fault-guard environment sets (Lead decision Q-C): this is a repo-wide inconsistency outside the item, not a Docs defect. Leftover `create_another` documents can be deleted but are not listed (accepted nice-to-have). One nit, not a finding: `expectedExistingDocId` uses the generic id pattern rather than a `ddc_` pattern.

### 6.4 Can backend implementation start?

**Yes.** There are 0 CRITICAL and 0 HIGH findings, and the eleven must-fix items from section 2 are in the contract and task text. DXD-01 and DXD-02 are MEDIUM and FIXED-CONTRACT-ONLY, so they block production, not local work. Before backend writes the migration and the write path:

1. Fold the four new LOW text fixes into the contract and tasks: DXD-14 row order and narrow 23503 mapping, DXD-15 fingerprint on 23505, DXD-16 per-user cap lock, DXD-17 title port or test.
2. Or, if the Lead prefers not to revise the text again, treat them as backend acceptance requirements.

DXD-14 matters most: if it is followed literally, every first confirm fails. I re-review the built routes, tests and log canary when the backend reports; FIXED-VERIFIED needs those tests to have been read and, where possible, run.
