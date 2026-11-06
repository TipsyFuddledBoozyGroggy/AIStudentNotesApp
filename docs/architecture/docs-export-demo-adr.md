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
4. Student choice, not server guess, decides **update vs new document** when notes changed. The confirm names the document it reviewed by id **and** version (`expectedExistingDocId`, `expectedExistingVersion`, DXD-05).
4a. **One database transaction per attempt for the demo (Lead decision Q-A).** The demo provider writes inside it. `pending` states, a reaper and `export_in_progress` exist only for a future real provider (section 9).
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

Allow-lists (`ModuleBoundaryTests`), each a reviewed change (DXD-08):
- `Tether.Docs` -> `Tether.SharedKernel`, `Tether.Lectures.Contracts`, `Tether.Ai.Contracts`, `Tether.Docs.Contracts`. Nothing else (no `Tether.Courses.Contracts`).
- `Tether.Ai` -> `Tether.Ai.Contracts` (new entry) and implements `INotesSnapshotReader`.
- `Tether.Ai.Contracts` and `Tether.Docs.Contracts` are added to the "contracts do not depend on anything" loop, and their **public surface is pinned with `AssertPublicSurface`**: `Ai.Contracts` exposes only `INotesSnapshotReader` and its snapshot record; `Docs.Contracts` only `IDocsProvider`, `DocsWriteRequest`, `DocumentModel`, `DocRef`, `ProviderResult` and their members.
- **User-bound reader.** `INotesSnapshotReader.FindReadyAsync(sessionId, ct)` takes the **ambient user** (`ICurrentUser`/`UserScope`) and has no `userId` parameter; an implementation that is handed a scope for a different user returns null (sibling of M2B3-11). It returns notes fields only (sections, bullets, citations as `startMs`, `source`, `truncated`, `coveredToMs`, Final summary content, `notesId`): no segment ids, transcript text, prompt material or AI client types.
- **Where the title and date come from (Lead decision Q-B; narrowed by DXD-17).** `SessionInfo` is **not changed**, because `ISessionDirectory` and `SessionInfo` are also read by the Ai module (`AiGateway`, `NotesService`, `CatchUpService`, `SummaryEngine`) and a new `Title` there would reach Ai. Instead `Tether.Lectures.Contracts` gains one narrow port, `IDocsSessionSource`, with two methods: `ReadAsync(sessionId)` (no lock, for preview and state) and `LockAndReadAsync(DbTransaction, sessionId)` (`FOR KEY SHARE`, for writers). Both return `DocsSessionFacts(SessionId, State, StartedAt, Title, TimeZone?)` (`TimeZone` only if the session already stores one) or null, use the ambient user, and refuse another user. `Tether.Docs` already references `Lectures.Contracts`, so **no new allow-list edge**; `Tether.Ai` does not reference the port (architecture test). The course name is deliberately left out of the demo title (it would need a `Courses.Contracts` edge); adding it later is an explicit reviewed edge. backend-engineer confirms whether the session stores a zone; if not, the title carries the UTC date labelled UTC.
- New tests: Docs references no `Tether.Ai` (implementation), `Tether.Identity`, `Tether.Host`, no `Microsoft.Extensions.Http`/`System.Net.Http` types in its sources, no Google assembly; `Tether.Ai` and `Tether.Ai.Contracts` reference no `Tether.Docs*` or `Tether.Google*`; no type in `Docs.Contracts` has a member whose name contains `token`, `secret`, `credential`, `refresh`.
- **Invariant 3:** the notes reader returns stored notes only; the Docs module never calls an AI client; the document builder is deterministic code. Nothing the Docs module stores or logs goes into a prompt. The AI gateway keeps its mock-only startup guard.
- **Invariant 4/idempotency:** nothing here is a syllabus write; the same preview-then-confirm discipline applies.
- **Invariant 10:** no credentials; `Docs:Provider` must equal `demo_docs` or startup fails (same style as the AI provider guard).

## 5. Data model (schema `docs`; database-architect to review the migration; names are proposals)

| Table | Columns (all rows carry `user_id`) | Notes |
|---|---|---|
| `docs.demo_docs` | `id`, `user_id`, `session_id`, `title`, `blocks jsonb`, `version int`, `created_at`, `updated_at`, `origin_export_id` UNIQUE | the stand-in document. FK `(session_id, user_id)` -> `lectures.lecture_sessions` ON DELETE CASCADE. The UNIQUE on `origin_export_id` is the provider-side idempotency guard. Content is derived data, separate from the transcript (invariant 5). No FK to `doc_exports` (the link is logical; both cascade from the session, which avoids a cycle). |
| `docs.doc_exports` | `id`, `user_id`, `session_id`, `notes_id`, `provider`, `mode`, `status` (`succeeded`/`failed`; `pending` is reserved for a real provider), `outcome`, `content_digest` (HMAC), `doc_id` nullable, `doc_version` nullable, `attempt`, `error_code`, `retryable`, `retry_after_seconds`, `created_at`, `completed_at` | one row per confirm key. FK `(session_id, user_id)` cascade. |
| `docs.doc_targets` | PK `(user_id, session_id, provider)`, `doc_id`, `last_digest`, `version`, `last_export_id`, `updated_at` | the one current document per lecture (`create_another` moves the pointer). FK `(session_id, user_id)` cascade. |
| `docs.idempotency_keys` | `user_id`, `method`, `route_template`, `key_hash`, `fingerprint_hmac`, `export_id`, `created_at` | unique `(user_id, method, route_template, key_hash)`, 24 h. It has no `session_id`, so it cascades through a composite FK `(export_id, user_id)` -> `docs.doc_exports` ON DELETE CASCADE (DXD-09). Reuse the SharedKernel mechanism if one exists, else module-local. |
| `docs.audit_events` | `id`, `user_id`, `session_id` (no FK), `export_id`, `action`, `provider`, `error_code`, `notes_revision`, `block_count`, `attempt`, `at` | content-free. **On the list of tables full account erasure must remove (PO-20aw)**; retention after a lecture delete is subject to the open PO-20ca follow-up and L-14. |

**Persistence rules (DXD-09).** The cross-schema FK pin in `PersistenceTests` lists exactly the new FKs to `lectures.lecture_sessions`: `demo_docs`, `doc_exports`, `doc_targets` (three), plus any `identity` FK. No `RESTRICT` anywhere between docs tables (an immediate check inside a cascade can fail the lecture delete). `OwnershipSaveChangesInterceptor` sees EF-tracked saves only, so **every raw SQL statement carries a `user_id` predicate**, and a source-scan test (like the bulk-call ban) enforces it.

**Concurrency and lock order (DXD-01, DXD-14, DXD-16, L-LOCKORDER, Q-A).** One transaction per confirm, student delete and any job, always in this order: (1) the session row `FOR KEY SHARE` through a narrow Docs-only port in `Lectures.Contracts` that takes the caller's `DbTransaction` and returns the session facts (`null` = the session is gone = `404`, the C-21 re-check); (2) `pg_advisory_xact_lock` on `(user, session)`, which serialises the first `create` and makes a second request for the same lecture wait; (3) a **per-user** `pg_advisory_xact_lock` on the user (DXD-16), always after (2), taken by **every** Docs writer (confirm, student delete, any job) so the 200-document cap and the per-user count are race-free across lectures; (4) docs rows in the fixed order `doc_exports`, `idempotency_keys`, `demo_docs`, `doc_targets`, `audit_events` (DXD-14: `idempotency_keys` references `doc_exports` through its composite FK, so the parent row is inserted first; the alternative is a deferrable FK). Lock order is therefore: session row, then (user,session) advisory, then user advisory, then rows. Two lectures of one user serialise on the user lock only after each holds its own session lock, so there is no cycle; the cascade delete locks the session row first and then children, the same direction. "Via the FK" is not enough, and a `doc_targets FOR UPDATE` before the session lock is forbidden. Error mapping (DXD-14, DXD-15): `23503` -> `404` only for the named session FKs, any other `23503` is a `500` bug; `23505` on the idempotency key compares fingerprints (equal replay, different `400`). A deadlock (`40P01`/`40001`) repeats the whole attempt, up to 3 times, without increasing `attempt`. Tests use `CommandTrap` plus `pg_blocking_pids` for confirm against lecture delete, confirm against course purge, student delete against lecture delete, and **two lectures of one user confirming at once** (per-user lock), and **tolerate losing the race** (L-VICTIM: record, roll back, repeat on fresh data; never assume which side loses).

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
   |   (a concurrent request for the same lecture waits on the advisory lock, then replays the winner)
   v
 (one transaction) --> succeeded (created | updated | unchanged)
                 \--> failed (retryable | permanent) --same key, same body--> new write (re-runs state, digest and
                                                       version/doc checks; attempt+1, max 5; may be 409 or 412)
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

`DemoDocsProvider` (mock): create = INSERT into `docs.demo_docs` keyed by `origin_export_id` (second call returns the first row); update = UPDATE where `version = expectedVersion`; delete = DELETE. It is `internal`, registered only when `Docs:Provider = demo_docs`, and it **participates in the confirm transaction** through an internal unit of work (a savepoint around the call). That is why it is not a pattern for the real provider (section 9).

**Fault injection (DXD-07, Lead decisions Q5 and Q-C).** The e2e trigger is a Development-only request header, honoured only when `Docs:Demo:Faults:Enabled=true` (set in `docker-compose.dev.yml` and the CI compose; devops agrees the CI line). The Docs startup guard refuses to start in any environment other than **`Development` or `Test`** (the Syllabus set, `SyllabusModule.cs` style) if **any** `Docs:Demo:Faults:*` key is present, not only `Enabled=true`. Ai and Transcript keep their own sets and are not touched. When the flag is off the header is ignored (no `400`, so nothing reveals the feature). Faults (`unavailable`, `rate_limited`, `rejected`, `slow`) fire inside `DemoDocsProvider`; `slow` is capped at 5 s and honours cancellation so it cannot hold the session lock and a pooled connection without limit. Tests: startup fails with the flag set in `Production`, `Staging` and `Testing`.

**Digest key (DXD-06).** A new HKDF label `docs-content-digest.v1` goes into `DerivedKeys.Labels`; the idempotency-ring and audit keys are never reused. HMAC input is `label LF sessionId LF canonical JSON`, canonical form defined in `docs-common#/$defs/contentDigest`. The digest is an equality check on content, never proof of authorization. A root-secret rotation stales every stored digest once (an `update` then rewrites identical content); accepted and recorded.

## 8. Security, privacy, deletion (summary; the contract doc is normative)

- Per-user authorization on every route; 404 for foreign ids and for a `notesId` that is not the session's current notes (in preview and confirm alike, DXD-12); composite FKs; route-by-route IDOR tests with two users and overlapping ids; canonical GUID session ids so a second spelling cannot reach the same session.
- Digest is an HMAC under its own HKDF label (section 7); confirm compares in constant time; the key never appears in logs or responses.
- Rate buckets `docs-read` and `docs-write` are separate (DXD-03); bodies have a read deadline and a minimum data rate.
- Audit rows and logs are content-free (contract 4.8). Log canary tests cover every new failure path.
- All text is untrusted plain text, TextPolicy at write and at every sink (L-FORGE, SBV-01): the document is **re-derived server-side from stored notes**; nothing from the request body is stored as document text. Truncated notes say so in the document (DXD-13).
- The Demo Doc page renders as text only: no `dangerouslySetInnerHTML`, no Markdown, no links; the client rejects the `external` `docRef` branch in v1 and validates the `[docId]` route parameter before building a path. The page inherits the missing web CSP (M2C-29).
- Deletion: cascade with the lecture and the course purge, same transaction; no separate tombstone (the earlier NAS-01 claim is deleted, DXD-10); a write racing a delete either finishes first and is cascaded, or gets `404` at the session lock. Replay after delete is 404. `docs.audit_events` is on the full-erasure list (PO-20aw) and subject to the open PO-20ca/L-14 follow-up. The UI must not say a demo deletion also removes anything in Google (see 9).
- Severity calibration: personal tool for the PO's son (PO-15/16); a mock holds no new external data. The security design review is `docs/security/docs-export-design-review.md` (0 CRITICAL, 0 HIGH, 2 MEDIUM, 11 LOW); this revision applies its 11 must-fix items.

## 9. How a real `GoogleDocsProvider` would slot in later (NOT built; NOT authorised; needs a PO go)

Nothing in this section has been built, run or verified. Google facts are UNVERIFIED until `vendor-verification-researcher` quotes dated primary sources.

**Correction of the first draft (DXD-02).** The first draft said the evaluation order, idempotency and retry rules and the audit were "unchanged" for a real provider. That was wrong on the server. A real provider **does change server behaviour**, even though the routes and shapes do not change:

- The provider call **leaves the database transaction**. The demo's single transaction (Q-A) cannot be kept. The real flow is two-phase: transaction 1 stores the export row `pending` with the idempotency key and releases every lock; the network call runs with **no lock and no pooled connection held across it**; transaction 2 stores the outcome. So `pending`, the 60-second reaper, `export_in_progress` (reserved in the schema for exactly this), orphan recovery and a stale-`pending` rule become real, each with its own tests. The advisory-lock wait of the demo becomes an `export_in_progress` answer.
- A reaped export whose Google call succeeded late **duplicates the document** unless the Google side is idempotent. That pre-generated file ids work for Docs and that `requiredRevisionId` behaves as needed are both UNVERIFIED. The `exportId` in the file's app properties lets the orphan be found.
- The provider must assert `DocsWriteRequest.UserId` equals the ambient user and look tokens up user-scoped (confused deputy).
- Fixed egress hosts, `AllowAutoRedirect=false`, no environment proxy (CV-27 precedent), a global kill switch for provider writes, and lifecycle handling: a lecture delete or course purge during an in-flight call leaves a Google document the app has forgotten (the copy must say the document stays).
- The idempotency, retry, mode and audit rules keep their meaning but get new tests for the two-phase path.

**Unchanged on the wire and in the UI:** all five routes, all request and response shapes, the digest, the modes, `expectedExistingDocId`/`expectedExistingVersion`, the record and state shapes, `IDocsProvider`'s signature and the module boundaries. The frontend flow and states are unchanged.

**The complete wire diff (additive relaxations, Lead to approve at that time):**
1. `docs-common#/$defs/provider` enum `["demo_docs"]` -> `["demo_docs","google_docs"]`.
2. `destination.isDemo` and record `demoDoc` constants `true` -> boolean (the UI already branches on them for wording); the `label` constant "Demo Doc" widens to a safe-pattern label.
3. `confirm.acknowledgedDemo` becomes required only when `provider` is `demo_docs` (schema `if/then`).
4. Nothing else: `docRef` already has the `external` branch (`openUrl` allowlisted to `https://docs.google.com/document/d/{id}/edit`, **built on the server from the file id, never taken from a URL Google returns**, opened with `rel="noopener noreferrer"`), `recordErrorCode` already lists `reconnect_required`, `doc_changed_outside`, `quota_exceeded`, and `blockedReason` and `actions` are provider-neutral.

**New work outside this contract (each needs its own PO go and review):**
- A `Tether.Google.Docs` assembly with `GoogleDocsProvider : IDocsProvider`. It resolves the user's connection and refresh token **inside itself** from the encrypted server-side store designed in `docs/architecture/google-calendar-integration.md` section 3; no token type crosses `IDocsProvider`; no token or Google file id reaches the AI module, the logs, the browser or any response (invariant 3). The Docs module still references only `Docs.Contracts`.
- Connect flow, consent UI and disconnect: the existing `calendar.v1` connection surface would need an additive Docs product (its fixtures today reject a Drive product), plus refusal of institution-managed accounts at connect (PO-20w; `hd` claim detection UNVERIFIED, test on the real test account first; register row GR-1 is open) and a personal-account assumption (PO-15a). Real sign-in is a separate PO go. G1 applies: lecture-derived content leaves the app for a third party.
- **Scope (UNVERIFIED, to confirm against current Google docs):** the default is `https://www.googleapis.com/auth/drive.file` only (per-file, files the app creates). `https://www.googleapis.com/auth/documents` reaches every Doc the student has; choosing it needs a **PO decision and a security re-review**. Neither `drive` nor `drive.readonly` is needed. Sensitivity class and OAuth verification status of each scope must be checked by `vendor-verification-researcher` and `security-engineer` before any request.
- Mapping: `CreateAsync` = create a Google Doc (pre-generated file id, UNVERIFIED) then insert content in one `documents.batchUpdate`, **plain text only with no link styles or auto-linking** (Google's behaviour UNVERIFIED; notes are AI output and can carry injected phishing text under the "Made by Tether" line); `UpdateAsync` = `batchUpdate` guarded by the Docs `requiredRevisionId` write control (UNVERIFIED) against the revision stored at the last write, so **a student's own edits in Google are never overwritten**: on mismatch return `doc_changed_outside` and the UI offers `create_another`; `DeleteAsync` on lecture delete is a no-op by design (C-21, PO-15c).
- Rate limits and errors: map 429/403 `rateLimitExceeded`/5xx to `rate_limited`/`provider_unavailable` with `Retry-After`; 401/403 after revocation to `reconnect_required`; no raw Google error text is stored or returned.
- Copy: the deletion and "where is it" sentences must come from the provider (`demoDoc` false: "This stays in your Google account if you delete the lecture here"). New wording needs the normal UX review; nothing here changes consent or Demo-source wording.
- Visible ownership: every document carries the closing "Made by Tether" paragraph and the app property, so the app can say what it owns.

## 10. Risks

1. A mock that always succeeds hides real latency, quota, revocation and edited-document cases (finish-backlog risk 4). Mitigation: fault injection, the reserved error codes, `doc_changed_outside` in the contract from day one, and the honest list in section 9 of what changes on the server.
2. The document structure may need rework for real Docs formatting (headings, lists, links). Mitigation: structured blocks, not Markdown strings.
3. Retention/audit conflict (PO-20ca) is still open; audit rows are kept content-free until answered.
4. Cross-module seam (`Ai.Contracts`) is new; keep it read-only, user-bound and one method.
5. Security design review done (`docs/security/docs-export-design-review.md`); implementation not yet reviewed.

## 11. Lead decisions applied (2026-11-06) and what remains open

Applied: Q-A single transaction for the demo; Q-B a narrow Docs-only `IDocsSessionSource` port carrying title and zone (`SessionInfo` unchanged, DXD-17), no new allow-list edge, course name left out of the title; Q-C fault guard set `Development|Test`, Ai and Transcript untouched; Q1 freeze as mock-only after the first green run; Q2 `provider` const; Q3 student Demo Doc delete included; Q4 no editable title; Q5 Development-only header; Q6 notes sheet only; Q7 Final summary included.

Still open: Q8 audit retention (PO-20ca/L-14). No session time zone is stored (backend confirmed): the demo title carries the UTC date labelled UTC. Nice-to-haves from the review not applied as requirements: listing `create_another` leftovers in the state read (the Q3 delete reaches them through export records for now).
