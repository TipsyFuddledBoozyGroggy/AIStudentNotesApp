# Notes and the final summary after Stop (PO-18d)

Status: implemented on the **mock provider only** (backend and contract half), 2026-10-31, backend-engineer. Not committed. The wire contract is `docs/API_CONTRACTS.md` section 17 and `src/shared/contracts/lecture.v1/notes*.schema.json`. Nothing here clears G1 (consent and disclosure wording) or G2 (vendor, cost, limits). The startup guard still refuses any AI provider other than `mock`. The frontend half (the "Make notes" button and the notes view) is not built.

## 1. Rule

1. Once a lecture is Paused, Stopped or deleted, **nothing is sent to an AI provider automatically** (B3-06 stands). Stop does not flush, does not run a final pass and does not make a Final summary.
2. After Stop, the **only** thing that may send the already-recorded transcript to the AI is a **deliberate student press**: `POST /sessions/{id}/notes`.
3. One press makes **both** the lecture notes (sections of bullets, every bullet cited to a stored segment) **and** the Final summary revision (`summary.revision.status = "Final"`).
4. The press is user-scoped, audited (content-free), idempotent, rate limited, and cancelled by Delete or a course purge. It is not cancelled by Stop: it only exists after Stop, and a stopped session cannot resume.
5. Delete removes the press, the notes and the Final revision (FK cascade). It cannot recall a request already handed to a vendor, and it does not delete anything already exported to Google (PO-15c).

## 2. States

Session (unchanged):

```text
Active <-> Paused -> Stopped        (Failed: server only, not implemented)
   |          |         |
   +--------- + ------- +--> deleted (any state)
```

AI permission per session state:

```text
Active   : live workflows (rolling summary, compaction, Quick recap). No press accepted (409).
Paused   : nothing. No press accepted (409).
Stopped  : nothing, UNLESS a press is Pending -> only the Notes and FinalSummary workflows, only for that press.
deleted  : nothing. In-flight calls cancelled.
```

Press (`ai.lecture_notes.state`, also the wire `status`):

```text
                 press accepted
                       |
     no finals --------+-------- AI off / breaker open / queue full
         |             |                   |
     NotNeeded      Pending           Unavailable
                       |
     +--------+--------+-----------+------------------+
     |        |                    |                  |
   Ready   BudgetLimited      Unavailable      (row deleted: Delete or purge)
            (pre-flight per-press  (provider error, timeout,
             cap, or a lecture-     invalid output after one repair,
             pool cap on a call)    stale > 600 s, restart)
```

Only `Unavailable` and `BudgetLimited` allow a new press (a new row, `revision + 1`, at most 5). `Ready` and `NotNeeded` are final for the session; a later press returns them with 200.

## 3. Request lifecycle

1. **HTTP** (`AiEndpoints.PostNotes`): Origin middleware (403), authentication (401), JSON content type (415), body exactly `{}` (400), required `Idempotency-Key` (400).
2. **Service** (`NotesService.RequestAsync`, user scope):
   1. `ISessionDirectory.FindAsync(user, session)`: null -> 404. State not `Stopped` -> 409.
   2. Expire this session's stale `Pending` press (older than `Ai:Notes:StaleSeconds`).
   3. Same key -> return that press, 200, `Idempotency-Replayed: true`.
   4. Newest press `Pending`, `Ready` or `NotNeeded` -> return it, 200 (no audit, no call).
   5. Per-user limiter (6/min) and attempt cap (5 per session) -> 429.
   6. **Audit** `notes.requested` through the new `ISessionAuditLog` port (Lectures contract) into append-only `lectures.audit_events`. A failure here is a 500 and nothing is created.
   7. Decide the initial state: no finals -> `NotNeeded`; AI disabled or breaker open -> `Unavailable`; advisory lecture-pool budget check fails -> `BudgetLimited`; else `Pending`.
   8. Insert the row. Unique violations (same key, same revision, one-pending index) -> hand back the winner, 200. FK violation (deleted meanwhile) -> 404.
   9. `Pending` -> enqueue on the bounded notes queue (full -> `Unavailable`). Answer 202 + `Location`.
3. **Job** (`NotesRunner.ProcessAsync`, own queue and worker, user scope):
   1. Reload the row; stop unless still `Pending`.
   2. Read the stored finals (corrections applied, never partials), oldest first, at most 6,000.
   3. Plan: parts of at most 12,000 prompt characters, at most 10 parts (a longer lecture is cut at the end, `truncated`). Plan the final-summary chunks too.
   4. **Per-press cap**: the estimated cost of all planned calls (the gateway's own estimate: input estimate plus full output allowance) must be within `Ai:Notes:PerRequestMicroUsd`; otherwise a `budget_denied` ledger row, `BudgetLimited`, no call.
   5. For each part: `AiGateway.RunAsync` (workflow `Notes`, `PressId`), validated by `NotesValidator` (closed shape, lengths, citations resolved against THIS part only, uncited bullets dropped, empty sections dropped, nothing grounded -> rejected, one repair), `OutputSanitizer`, `LanguageGuard` (log-only).
   6. Then `SummaryEngine.FinalAsync`: the compaction re-derivation over the same finals with workflow `FinalSummary`, appended as a `Final` revision (published as `summary.revision` to any joined client).
   7. Store `Ready` with sections, covered range, source seq range, model, prompt version, final revision. A failure stores `Unavailable` or `BudgetLimited`. A save that fails because the row was deleted stores nothing.
4. **Gateway** (`AiGateway.IsActiveAsync`, before the reservation AND again immediately before the vendor call): for `Notes`/`FinalSummary` the session must be `Stopped` for this user AND `NotesPressProbe` must find this press `Pending`, of this user and session, younger than the stale limit. Every other lecture workflow still requires `Active`. Lookup errors refuse. Calls run under a `SessionCutoff` lease on the lecture scope, so Delete (and now a course purge of a stopped session) cancels them; the hold is released.
5. **Reads** (`GET /notes`): directory check (404), expire stale, newest attempt or 409.

## 4. Data

`ai.lecture_notes` (migration A005): `id, session_id, user_id, revision, state, idempotency_key, requested_at, completed_at, sections jsonb, covered_from_ms, covered_to_ms, source_from_seq, source_to_seq, truncated, final_summary_revision, model, prompt_version`. Composite FK `(session_id, user_id)` to `lectures.lecture_sessions` ON DELETE CASCADE (`fk_lecture_notes_session_user`, added to the cross-schema FK allow-list). Unique `(session_id, revision)`, unique `(user_id, session_id, idempotency_key)`, partial unique `(session_id) WHERE state = 'Pending'`. CHECKs: state vocabulary, revision > 0, key length, range, "only Ready has content", "Pending has no completion time". EF query filter on the current user. `ck_ai_usage_workflow` gains `Notes` and `FinalSummary` (lecture pool). A005 Down refuses once ledger rows for the new workflows exist (same policy as A003). The press audit uses the existing `lectures.audit_events` (append-only trigger, survives session delete).

## 5. Failure modes

| Case | Result |
|---|---|
| Press on Active, Paused, Failed | 409, nothing created, audited or sent |
| Foreign or missing session | 404, same body |
| Double click / concurrent presses | one row, one generation; others get it with 200 |
| Same key replayed | same press, 200, `Idempotency-Replayed` |
| No transcript text | `NotNeeded`, no call, no ledger row |
| Lecture-pool cap reached | `BudgetLimited` (at press time, or from the gateway on a call) |
| Per-press estimate over cap | `BudgetLimited`, `budget_denied` ledger row, no call |
| Provider error, timeout, invalid output after one repair | `Unavailable` |
| Delete or course purge mid-call | call cancelled, hold released, all rows gone, audit row kept |
| Restart with a press Pending | startup sweep -> `Unavailable`; the gateway refuses it anyway after 600 s |
| Final summary fails, notes succeed | notes `Ready`, `finalSummaryRevision: null` (no retry: Ready is final) |
| Lecture longer than 10 parts | notes `Ready`, `truncated: true`, `coveredToMs` shows where it stops |

## 6. What a real provider adds later (not done)

- A real `IAiClient` adapter (G2 cleared, PO-4, Anthropic terms/DPA C6). The guard refuses it today.
- Real token counts instead of the chars/4 estimate; the per-press cap and the lecture caps must be set from measured figures.
- Consent and disclosure copy for "Make notes": the press sends the whole stored transcript of the lecture (including other voices, PO-15b/B3-12) to the vendor; a call already sent cannot be recalled by Delete; vendor retention terms. G1 owner.
- Prompt evals for the notes prompt (grounding, injection, length); the mock says nothing about model behaviour.
- Multi-instance: the queue, limiter, cutoff leases and startup sweep are in-process (ADR-020d).

## 7. Open questions for security review

1. Is the lecture pool right, or should notes have their own pool so live TL;DR spend cannot starve them and notes cannot drain the live allowance?
2. Is `StaleSeconds = 600` the right lifetime for the gateway permission? A long lecture with ten parts at 30 s each fits; a slower real provider may not.
3. The audit row is written only when a press starts an attempt (not for replays or presses answered with ready notes). Is that the intended audit granularity?
4. Should the audit write and the press insert be one transaction? Today they are two contexts: an audit row can exist for a press whose insert then lost a race (it was still a real press).
5. Course purge now notifies observers for every purged session, not only live ones (to cancel notes calls). Hub connections joined to a purged stopped session are now closed by push instead of the 60 s revalidation; confirm this is wanted.
6. The Final summary is published on the hub to clients still joined to a stopped session. Fine for the owner; confirm no other surface consumes it.
7. `FinalSummary` chunks reuse the rolling-summary prompt (`rolling-summary.v2`) as compaction does; a dedicated final prompt would need its own version and eval.
