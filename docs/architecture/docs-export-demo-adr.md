# ADR (proposed): Export notes to a "Demo Doc" stand-in behind a Docs provider interface

Status: **PROPOSED by google-integration, 2026-11-06. Not accepted, not built.** Lead: assign an ADR number and add a row to `docs/DECISIONS.md`. Scope: finish-backlog item 3, approved by the PO as a mock ("Export to Google Doc on a Demo Doc mock", PO-20cb, PO-20cc). Contract: `docs/API_CONTRACTS_DOCS_EXPORT.md` and `src/shared/contracts/docs.export.v1/`. Read first: `CLAUDE.md` invariants 3, 4, 10, `docs/agents/project-playbook.md`, `docs/architecture/notes-after-stop.md`, `docs/API_CONTRACTS_P2.md` section 6.3 (the Demo calendar pattern this copies).

Facts below come from reading the repo files named; nothing was built or run except a schema-fixture check (contract doc section 10). Anything about Google is UNVERIFIED and marked.

## 1. Context

- The MVP says a student can "deliberately export notes to a Google Doc". Today there is no Google module, no tokens, no OAuth. Item 2 already gives a local Markdown file and Copy (`notesExport.ts`, client side, no network).
- The Demo calendar already proves the pattern: review, deliberate confirm, idempotent write to a mock held in our database, undo, per-item outcomes (`API_CONTRACTS_P2.md` 6.3).
- `API_CONTRACTS.md` has only a placeholder row `POST /sessions/{id}/export/doc` -> `202 {exportId}`.
- Real Google (OAuth, consent screen, school-managed account refusal by `hd`, which is UNVERIFIED, PO-20w) needs a separate PO go and stays OFF.

## 2. Decision

1. Add a small **Docs** module with two routes that matter: **preview** (what would be written; writes nothing) and **confirm** (writes, idempotent). Plus a state read, a Demo Doc read, and an optional Demo Doc delete.
2. The destination is a **`DemoDocsProvider`** implementing `IDocsProvider`. It stores the "document" as a row in our own database table owned by the user. No network, no Google SDK, no credentials.
3. The server builds the document from the **stored notes** of the authenticated user. The client sends ids and a digest, never text. The confirm is bound to exactly what was previewed by an HMAC `contentDigest`.
4. Student choice, not server guess, decides **update vs new document** when notes changed.
5. Tokens, OAuth and Google never enter the Docs module's public types; the AI module has no path to either.

## 3. Alternatives considered

| Option | Why not |
|---|---|
| Put export in the Ai module | The Ai module must stay free of any destination code; a later Google provider would end up beside the AI client, against invariant 3. |
| Client builds the document and POSTs it | Client text could forge "notes" (SBV-01, L-FORGE) and would make the digest meaningless. Item 2 builds Markdown client-side only because nothing leaves the device. |
| Reuse `calendar.v1` or `syllabus.v1` routes | Different resource, different idempotency basis; `syllabus.v1` is frozen. |
| Accept `provider: "google_docs"` in v1 now | Makes a real write expressible before a PO go. A startup guard plus a const enum is safer; widening is a one-line additive change later (section 9). |
| Store only the Markdown string | Loses structure (heading, bullet, citation times) the real provider needs, and makes update/dedupe digest depend on rendering. |

## 4. Module, boundaries, AI isolation

```text
Tether.Docs.Contracts   IDocsProvider, DocumentModel, DocRef, ProviderResult (no token type, no HttpClient)
Tether.Docs             endpoints, DocsExportService, document builder, DemoDocsProvider (internal), persistence
Tether.Ai.Contracts     INotesSnapshotReader  (NEW, read-only: FindReadyNotesAsync(userId, sessionId) -> snapshot)
(later, NOT built)      Tether.Google.Docs    GoogleDocsProvider : IDocsProvider, references Docs.Contracts only
```

Allow-lists (`ModuleBoundaryTests`), each a reviewed change:
- `Tether.Docs` -> `Tether.SharedKernel`, `Tether.Lectures.Contracts` (session ownership/state via `ISessionDirectory`), `Tether.Ai.Contracts`.
- `Tether.Ai` implements `INotesSnapshotReader` and references nothing of Docs.
- New tests: Docs references no `Tether.Ai` (implementation), `Tether.Identity`, `Tether.Host`, no `Microsoft.Extensions.Http`/`System.Net.Http` types in its sources, no Google assembly; `Tether.Ai` references no `Tether.Docs*`; no type in `Docs.Contracts` has a member whose name contains `token`, `secret`, `credential`, `refresh`.
- **Invariant 3:** the notes reader returns stored notes only; the Docs module never calls an AI client; the document builder is deterministic code. Nothing the Docs module stores or logs goes into a prompt. The AI gateway keeps its mock-only startup guard.
- **Invariant 4/idempotency:** nothing here is a syllabus write; the same preview-then-confirm discipline applies.
- **Invariant 10:** no credentials; `Docs:Provider` must equal `demo_docs` or startup fails (same style as the AI provider guard).

## 5. Data model (schema `docs`; database-architect to review the migration; names are proposals)

| Table | Columns (all rows carry `user_id`) | Notes |
|---|---|---|
| `docs.demo_docs` | `id`, `user_id`, `session_id`, `title`, `blocks jsonb`, `version int`, `created_at`, `updated_at`, `origin_export_id` UNIQUE | the stand-in document. FK `(session_id, user_id)` -> `lectures.lecture_sessions` ON DELETE CASCADE. The UNIQUE on `origin_export_id` is the provider-side idempotency guard (4.4 of the contract). Content is derived data, separate from the transcript (invariant 5). |
| `docs.doc_exports` | `id`, `user_id`, `session_id`, `notes_id`, `provider`, `mode`, `status` (`pending`/`succeeded`/`failed`), `outcome`, `content_digest` (HMAC), `doc_id` nullable, `doc_version` nullable, `attempt`, `error_code`, `retryable`, `retry_after_seconds`, `created_at`, `completed_at` | one row per confirm key. FK cascade as above. |
| `docs.doc_targets` | PK `(user_id, session_id, provider)`, `doc_id`, `last_digest`, `version`, `last_export_id`, `updated_at` | the one current document per lecture (a `create_another` moves the pointer to the new document; the older one stays reachable by its record only). FK cascade. Row lock target for writers. |
| `docs.idempotency_keys` | `user_id`, `method`, `route_template`, `key_hash`, `fingerprint_hmac`, `export_id`, `created_at` | unique `(user_id, method, route_template, key_hash)`, 24 h; reuse the SharedKernel mechanism if one exists, else module-local. |
| `docs.audit_events` | `id`, `user_id`, `session_id` (no FK), `export_id`, `action`, `provider`, `error_code`, `notes_revision`, `block_count`, `attempt`, `at` | content-free. Kept after delete (retention question open, PO-20ca). |

Concurrency (L-LOCKORDER): a writer takes the session key-share via the FK, then locks `doc_targets` for `(user, session)` (or an advisory lock on the same pair), in that order only; a cascade delete locks session first. Treat `40P01`/`40001` as retryable (up to 3), and prove the order with a `CommandTrap` plus `pg_blocking_pids` test, not sleeps. Writers re-check the session exists inside the transaction (C-21) and honour the delete tombstone.

## 6. Flow and states

```text
Notes Ready (Stopped lecture)
   |  student presses "Save to a Demo Doc"
   v
PREVIEW  POST .../docs-export/preview  -> document, counts, digest, existing?, actions   (writes nothing)
   |  student reads, picks a mode if a document exists, presses the confirm
   v
CONFIRM  POST .../docs-export  (Idempotency-Key, digest, mode, expectedExistingVersion, acknowledgedDemo)
   |   412 digest/version moved ---------------------------> back to PREVIEW (fresh)
   |   409 export_in_progress ----> "give it a moment" ----> GET state
   v
 pending --> succeeded (created | updated | unchanged)
        \--> failed (retryable | permanent) --same key, same body--> pending (attempt+1, max 5)
```

Edited-notes rule, stated once: **same digest -> `unchanged`, nothing written. Different digest with an existing document -> the student chooses `update` (same document, version+1, replaces its content) or `create_another` (new document, old one untouched). `create` is only for the first export.** Today notes are final once `Ready` (one press per session, `notes-after-stop.md` section 2), so the digest moves only when the lecture title changes or the builder version changes; the rule is there so the contract does not change when notes become editable.

## 7. `IDocsProvider` (proposal; C# sketch, contracts assembly)

```csharp
public interface IDocsProvider
{
    string ProviderId { get; }                      // "demo_docs"
    Task<ProviderResult<DocRef>> CreateAsync(DocsWriteRequest request, CancellationToken ct);
    Task<ProviderResult<DocRef>> UpdateAsync(DocRef existing, int expectedVersion, DocsWriteRequest request, CancellationToken ct);
    Task<ProviderResult> DeleteAsync(DocRef existing, CancellationToken ct);   // lecture/course delete and the optional student delete
}
// DocsWriteRequest: UserId (opaque, for provider-side lookup), RequestKey (= exportId), Document (title + blocks). No token, no HttpClient, no cookie.
// ProviderResult: Succeeded(value) | Retryable(code, retryAfter) | Permanent(code). Codes are the contract's recordErrorCode values; provider text is never carried.
```

`DemoDocsProvider` (mock): create = INSERT into `docs.demo_docs` keyed by `origin_export_id` (second call returns the first row); update = UPDATE where `version = expectedVersion`; delete = DELETE. It is `internal`, registered only when `Docs:Provider = demo_docs`. **Fault injection** (timeout, 500, rate limit, permanent reject, slow) is allowed in Development/Test only and fails startup elsewhere (same rule as `Speech:Replay:Faults` and `API_CONTRACTS_P2.md` 6.3). Proposed trigger for the real-stack e2e: a Development-only request header honoured only when `Docs:Demo:Faults:Enabled=true` (open question Q5).

## 8. Security, privacy, deletion (summary; the contract doc is normative)

- Per-user authorization on every route; 404 for foreign ids; composite FKs; route-by-route IDOR tests with two users and overlapping ids.
- Digest is an HMAC with a server-held key; confirm compares in constant time; the key never appears in logs or responses.
- Audit rows and logs are content-free (contract 4.8). Log canary tests cover every new failure path.
- All text is untrusted plain text, TextPolicy at write and at every sink (L-FORGE, SBV-01): the document is **re-derived server-side from stored notes**; nothing from the request body is stored as document text.
- The Demo Doc page renders as text only: no `dangerouslySetInnerHTML`, no Markdown, no links.
- Deletion: cascade with the lecture and the course purge, same transaction; replay after delete is 404. The UI must not say a demo deletion also removes anything in Google (see 9).
- Severity calibration: personal tool for the PO's son (PO-15/16); a mock holds no new external data. The security review the Lead owes is the standard one for new routes (authz, idempotency, log canary).

## 9. How a real `GoogleDocsProvider` would slot in later (NOT built; NOT authorised; needs a PO go)

Nothing in this section has been built, run or verified. Google facts are UNVERIFIED until `vendor-verification-researcher` quotes dated primary sources.

**Unchanged:** all five routes, all request and response shapes, the evaluation order, the digest, the idempotency and retry rules, the modes, the audit row, the deletion hook, `IDocsProvider`, the module boundaries. The frontend flow and states are unchanged.

**The complete wire diff (additive relaxations, Lead to approve at that time):**
1. `docs-common#/$defs/provider` enum `["demo_docs"]` -> `["demo_docs","google_docs"]`.
2. `destination.isDemo` and record `demoDoc` constants `true` -> boolean (the UI already branches on them for wording).
3. `confirm.acknowledgedDemo` becomes required only when `provider` is `demo_docs` (schema `if/then`).
4. Nothing else: `docRef` already has the `external` branch (`openUrl` allowlisted to `https://docs.google.com/document/d/{id}/edit`), `recordErrorCode` already lists `reconnect_required`, `doc_changed_outside`, `quota_exceeded`, and `blockedReason` and `actions` are provider-neutral.

**New work outside this contract (each needs its own PO go and review):**
- A `Tether.Google.Docs` assembly with `GoogleDocsProvider : IDocsProvider`. It resolves the user's connection and refresh token **inside itself** from the encrypted server-side store designed in `docs/architecture/google-calendar-integration.md` section 3; no token type crosses `IDocsProvider`; no token reaches the AI module, the logs, the browser or any response (invariant 3). The Docs module still references only `Docs.Contracts`.
- Connect flow, consent UI and disconnect: the existing `calendar.v1` connection surface would need an additive Docs product (its fixtures today reject a Drive product), plus refusal of institution-managed accounts at connect (PO-20w; `hd` claim detection UNVERIFIED, test on the real test account first) and a personal-account assumption (PO-15a). Real sign-in is a separate PO go.
- **Scope (UNVERIFIED, to confirm against current Google docs):** least privilege for a create-and-update-own-files export is `https://www.googleapis.com/auth/drive.file` (per-file, files the app creates); `https://www.googleapis.com/auth/documents` is the alternative if `drive.file` cannot do the Docs `batchUpdate` calls needed. Neither `drive` nor `drive.readonly` is needed because the student does not pick existing Drive files for export. The OAuth consent screen, verification status and the sensitive/non-sensitive classification of each scope must be checked by `vendor-verification-researcher` and `security-engineer` before any request.
- Mapping: `CreateAsync` = create a Google Doc (pre-generated file id so a retry cannot duplicate, UNVERIFIED that the Drive API supports it for Docs) then insert content in one `documents.batchUpdate`; store `exportId` in the file's app properties so an orphan can be found after a crash; `UpdateAsync` = `batchUpdate` guarded by the Docs `requiredRevisionId` write control (UNVERIFIED) against the revision stored at the last write, so **a student's own edits in Google are never overwritten**: on mismatch return `doc_changed_outside` and the UI offers `create_another`; `DeleteAsync` on lecture delete is a no-op by design (C-21, PO-15c: the Google document stays).
- Rate limits and errors: map 429/403 `rateLimitExceeded`/5xx to `rate_limited`/`provider_unavailable` with `Retry-After`; 401/403 after revocation to `reconnect_required`; no raw Google error text is stored or returned.
- Copy: the deletion and "where is it" sentences must come from the provider (`demoDoc` false: "This stays in your Google account if you delete the lecture here"). New G1-adjacent wording needs the normal UX review; nothing here changes consent or Demo-source wording.
- Visible ownership: every document carries the closing "Made by Tether" paragraph and the app property, so the app can say what it owns.

## 10. Risks

1. A mock that always succeeds hides real latency, quota, revocation and edited-document cases (finish-backlog risk 4). Mitigation: fault injection, the reserved error codes, and `doc_changed_outside` in the contract from day one.
2. The document structure may need rework for real Docs formatting (headings, lists, links). Mitigation: structured blocks, not Markdown strings.
3. Retention/audit conflict (PO-20ca) is still open; audit rows are kept content-free until answered.
4. Cross-module seam (`Ai.Contracts`) is new; keep it read-only and one method.
5. Nothing here has been security-reviewed.

## 11. Open questions

See the handback list in the delegation report and `docs/product/docs-export-tasks.md` section 5. Decisions the Lead/PO must make: Q1 contract number and freeze; Q2 `provider` const vs enum; Q3 student-initiated Demo Doc delete; Q4 student-editable title; Q5 e2e fault trigger; Q6 where the button lives; Q7 whether the Final summary is included in the document by default; Q8 audit retention.
