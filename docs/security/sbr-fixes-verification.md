# SBR-06, SBR-07, SBR-10 fixes and the action pin bump: independent verification

Reviewer: security-engineer. Date: 2026-11-01. HEAD `033b79d`, working tree clean at start. Commits reviewed: `033b79d` (SBR-10, SBR-06, SBR-07) and `a7c4c94` (Trivy and CodeQL pin bump, `docs/security/action-pin-verification.md`). Severity per `docs/security/severity-policy.md`. New ID prefix **SBV-**. Source findings are in `docs/security/syllabus-fixes-rereview.md`.

**Method.** I read the full diffs (`git show`), the current code around every change, the new tests, the S001 migration CHECK constraints and the web client (grep). I also ran two read-only network checks: `git ls-remote` on `aquasecurity/trivy-action` and `github/codeql-action`, and `curl` of `action.yaml` at the new Trivy SHA from raw.githubusercontent.com. I ran **no** test, build, workflow, app or dev stack. The test counts in the commit message (135 syllabus, 333 AI, 22 architecture, 870 contract, 606 integration) are the author's claim. Nothing in `src/`, `tests/` or `.github/` was changed.

## Result

**New findings: 0 CRITICAL, 0 HIGH, 0 MEDIUM, 3 LOW.** Nothing blocks a push. SBR-01 stays a production blocker until a GitHub run record exists.

| ID | Verdict | Register status |
|---|---|---|
| SBR-10 LOW | **Verified.** All three fall-backs now set `Replayed` when the shared row carries the request's key. Deterministic tests exist for each path, plus a control test | FIXED-VERIFIED |
| SBR-06 LOW | **Partial.** The backend counts every unclaimed drop and demotes dropped items to unclaimed dates, and there is a red-team fixture. But the web client neither fetches `/unclaimed-dates` nor renders `droppedInvalid`, so the student still sees no trace. New residuals: SBV-01, SBV-02 | OPEN |
| SBR-07 LOW | **Verified** for the slot-holding problem: the read deadline is linked to the request token, the slot is released on every path, and there is a test. The Kestrel rate floor and the 408 branch have no test (TestServer has no Kestrel) | FIXED-VERIFIED |
| SBR-01 MEDIUM | **Pin half verified.** Trivy now points at the vendor-named safe immutable release, and I matched both new SHAs to GitHub myself. The inputs are compatible by reading. Never run on GitHub, so it stays a production blocker | FIXED-UNVERIFIED |

## SBR-10: verified

- **Read:** `CatchUpService.cs:128,138,171` now return `Shared(row, idempotencyKey)`. That helper (`:195-196`) sets `Replayed` only when the request has a key and the row's key matches it exactly (ordinal comparison).
- **Rows with another key, or no key,** are still shared without the header. That is correct: such a row is not a replay of this request's key.
- **No key-conflict gap.** The three queries filter on `WindowMinutes == windowMinutes`, so a same-key row found there always has the same window. A same-key request with a different window still reaches `KeyConflict`: it misses the coalesce query, the insert hits the unique key index, and `:165-166` finds the winner's row with the other window.
- **One-Pending-per-window** (`ux_catch_ups_pending`) means the `running` and `existing` fall-backs can only find one Pending row. So when that row is the same-key one, it is the one returned.
- **Tests (`CatchUpReplayRaceTests.cs`).** `CommandTrap` fires on the n-th catch_ups query that has an `ORDER BY`. The queries before the coalesce, `ExpireStalePendingAsync` (`:214`) and the key lookup (`:117`), have no `ORDER BY`. So the trap fires at the intended point. It cannot fire early, which would let the test pass vacuously through the lookup path.
  - Each of the three race tests asserts the replay header, the same id and one keyed row. On the old code each would fail on `Assert.Equal("true", ReplayHeader(loser))` (judged by reading, not watched).
  - The control test checks that sharing a keyless or other-key row carries no header.
- **Pre-existing, not new:** a keyed request that coalesces onto a different-key row never stores its own key (M2B3-05, "key binding on coalesced answer").

## SBR-06: partial

### What the commit fixed (read)
- `SyllabusLimits.Apply` (`MockRuleExtractor.cs:510-530`) counts item drops, unclaimed drops and the extractor's own drops. The total is clamped to the wire maximum of 1,000.
- A dropped item whose snippet fits is kept as an unclaimed date, unless it is a policy item or its span is already visible.
- The model-written title of a demoted item is never stored. The integration test asserts this both in the API page and by SQL.

### Fits() tightening: correct
- The new checks match S001 exactly:
  - `ck_items_snippet` and `ck_unclaimed_dates_span`: `snippet_end - snippet_start <= 1200` and precision `exact` or `line`;
  - `ck_items_context` and `ck_unclaimed_dates_text`: `context_start >= 0` and `context_end - context_start <= 3000`.
- Before this change, a proposal that broke these failed the whole syllabus at `SaveChanges` with `invalid_output`. Now it is dropped and counted, which is better.
- The mock only emits `"exact"` (`MockRuleExtractor.cs:431,464`), so no current output is newly rejected.
- `Fits` requires context on an unclaimed date, although the DB allows none. That is stricter than the DB and harmless.

### Can model-written text leak through the snippet path? Yes in principle, but the demotion opens no new route (SBV-01)
- The `Apply` doc comment says "the snippet is a verbatim, server-sliced span of the canonical text, never model text". Nothing enforces that:
  - `PersistAsync` stores `u.Text`, `u.ContextText`, `p.Snippet.Text` and `p.Snippet.ContextText` exactly as the extractor returned them (`Jobs.cs:161-162,178-179`).
  - `Fits` checks lengths and offset order. It does not check that `Text == canonical[Start..End]`, nor that `End` and `ContextEnd` are within the canonical text.
  - The invariant holds today only because the mock slices the text itself (`MockRuleExtractor.cs:427-431`).
- **The demotion does not widen the channel.** An extractor can already put any text in `Unclaimed` directly, and kept items store their snippets the same way. What the demotion changes is the reasoning: the SBR-06 fix relies on an invariant that a real extractor would not provide on its own.
- **Impact with a real extractor:** hostile syllabus text could get model-written words shown in Review as a verbatim quote of the student's own syllabus, for example a fake "Exam moved to ..." line. React renders it as text, so this is an integrity problem, not XSS.

### Still open in SBR-06
- **No student-visible trace.** grep of `src/web/src/components` finds no use of `unclaimed` or `droppedInvalid`, and there is no client call to `/syllabi/{id}/unclaimed-dates`. The commit message admits the `droppedInvalid` rendering is still to do. But the unclaimed dates themselves, which the fix relies on as the trace, are not shown either.
- **Overflow beyond the caps is still dropped silently** (SBV-02):
  - `Apply` takes `Items.Take(maxItems)` and `Unclaimed.Take(200)`, and the excess is not counted.
  - The mock also skips recurring items beyond the cap without counting them (`:125`), and truncates its unclaimed list at 200 (`:129`).
  - So "EVERY drop is counted" in the doc comment is not true at the caps.

## SBR-07: verified (slot holding); Kestrel path not tested

- **Read deadline** (`Support.cs:416-438`):
  - The read uses a linked CTS: the request token plus `CancelAfter(PasteReadSeconds)`, which defaults to 30 s and is clamped to 1..120 from config.
  - An `OperationCanceledException` becomes `TimedOut` only when the deadline fired and the request token did not. A client abort therefore still propagates as cancellation.
  - The CTS is disposed with `using`.
- **408 handling:** a `BadHttpRequestException` with status 408 (Kestrel's minimum data rate) becomes `TimedOut`, but only when a deadline was given. So the PATCH and other JSON callers behave as before. The catch order is 413 first, then OCE, then 408, which is correct.
- **Response mapping:** `SyllabusService.cs:104` maps `TimedOut` to 400 `validation_failed`. Nothing is stored for the key, because the return happens before `CommitCreateAsync`, so a retry starts fresh.
- **Slot release on all paths:** the read is inside the `try` that starts after `EnterAsync` succeeds (`:85-86`). The `finally` (`:133`) covers TooLarge, Unsupported, TimedOut, a bind failure, a replay, a commit, client cancellation and any other exception, such as a 400 `BadHttpRequestException` when the client closes mid-body.
- **MinDataRate placement** (`SyllabusEndpoints.cs:137-138`): it is set in the handler before any body byte is read, the same way as the multipart branch (`:128-129`).
  - Kestrel counts the rate only while the app has a read pending. So a same-key waiter blocked in `EnterAsync` for up to 10 s does not trip it. This is from Kestrel's design as I understand it, UNVERIFIED by a probe.
  - The 30 s deadline starts only once the slot is held and the read begins.
  - On servers without the feature (IIS in-process, HTTP.sys, TestServer) only the deadline applies, as the comment says.
- **Test** (`SyllabusPasteReadDeadlineTests`): a body that stalls after 8 bytes, with a 1 s deadline. It asserts the slot is held, then freed no earlier than 0.9 s and no later than 10 s, that the next create gets 202, that the stalled request gets 400 `validation_failed`, and that one syllabus exists. On the old code the slot would not be freed within 10 s (judged by reading).
- **Not covered:** the Kestrel rate floor and the 408 branch. TestServer exposes no `IHttpMinRequestBodyDataRateFeature`, so neither has ever executed. See Observation SBV-I1.
- **Still open, as P2B1-12:** uploads and pastes share the 100 MB bytes-in-flight budget, so slow uploaders can starve pastes. That is the cross-user half of the original note. I updated the P2B1-12 row to name pastes.

## a7c4c94: action pins (SBR-01)

- **Diff:**
  - The 3 Trivy steps move from `6c175e9c...` (v0.30.0) to `57a97c7e7821a5776cebc9bb87c984fa69cba8f1` (v0.35.0).
  - CodeQL `init` and `analyze` move from `48ab28a6...` (v3.28.0) to `dd196fa9ce80b6bacc74ca1c32bd5b0ba22efca7` (v3.28.3).
  - grep finds no other use of the old SHAs outside `docs/security/`.
- **Independent cross-check (RAN, 2026-11-01):**
  - `git ls-remote` shows `refs/tags/v0.35.0 -> 57a97c7e...` (lightweight tag).
  - `refs/tags/v3.28.3^{} -> dd196fa9...`, so the annotated tag peels to that commit.
  - `refs/tags/v4.39.1^{} -> 9b3254085494b55a0f7c4c2b3d2d52cc712ce370`, which matches the researcher's recommended v4 SHA.
  - Agreement of a tag with a SHA today is weak evidence on its own. It agrees with the researcher's ancestry and immutable-release checks.
- **Input compatibility, the caveat in the commit message: closed by reading, not by a run.** `action.yaml` at `57a97c7e` declares every input that ci.yml passes: `scan-type`, `image-ref`, `scan-ref`, `severity`, `ignore-unfixed`, `exit-code` and `cache` (default `'true'`; ci.yml sets `false`).
  - Its nested actions are pinned by SHA: `setup-trivy@e6c2c5e3...` (commented "v0.2.4") and `actions/cache@0400d5f6...` (v4.2.4).
  - The default binary is `v0.69.3`, which the advisory names as safe. The default is fixed by the pinned action's own file, so the binary version is effectively pinned. Its download checksum is whatever setup-trivy does, which I did not audit.
- **CodeQL:**
  - v3.28.3 is the first release patched for GHSA-vqf5-2xx6-9wfm.
  - The languages are `csharp` and `javascript-typescript` (`codeql.yml:36-39`), not Java/Kotlin, so the advisory's conditions were not met here anyway.
  - The researcher recommended v4.39.1 because v3 is deprecated in December 2026, about one month from today. The commit took the "minimum acceptable" v3 option. That is SBV-03.
- **Cache half of SBR-01:** already mitigated in `c2fff7d`, read at `ci.yml:408-421`. `image-scan` runs on `pull_request` only, restores no cache, sets `cache: false`, and has `contents: read` with no checkout credentials.
  - Residual: the action still receives the runtime cache token. It can therefore write caches scoped to the PR, which no job on `main` restores.
  - The `setup-trivy` SHA carries a comment naming a tag that the advisory lists as unrestored. It is vendor-endorsed, not independently audited.
- **Stale text:** `ci.yml:5-7` and `docs/ops/ci-matrix.md:47` still say the SHAs were only read from tag refs. The researcher's recommendation 3 was not applied (part of SBV-03).
- **Disposition:** SBR-01 moves to FIXED-UNVERIFIED. It stays MEDIUM with a PRODUCTION gate until a GitHub run shows that the pins resolve and the scans execute (G5 NOT_RUN). Replacing the action with a checksum-verified binary or a digest-pinned image remains the follow-up for the token-exposure residual.

## New findings

### SBV-01 LOW: the "verbatim, server-sliced snippet" invariant is stated but not enforced
- **Where:**
  - `Jobs.cs:161-162,176-180` stores the extractor-supplied snippet and context text.
  - `SyllabusLimits.Fits` (`MockRuleExtractor.cs:490-497`) checks lengths and offset order only.
  - The claim is made in `MockRuleExtractor.cs:21-22,505-507`.
- **Scenario:** with a real extractor that returns snippet text, or that returns offsets and text that disagree, model-written text is stored and shown as a verbatim quote of the student's syllabus. Offsets beyond the canonical text are not rejected either. Hostile syllabus text can steer this. That matters for a deadline tool, where the quote is the student's evidence for checking a date. It is not reachable today (mock only).
- **Fix:**
  - In `PersistAsync`, ignore the extractor's `Text` and `ContextText`. Re-slice both from the canonical text using the validated offsets, or reject the proposal (counted in `droppedInvalid`) when the texts differ ordinally.
  - Require `End <= text.Length` and `ContextEnd <= text.Length`, and check surrogate boundaries.
  - Add a test with a stand-in extractor that returns a mismatching snippet and assert that the stored text is the canonical slice.
- **When:** required before any real extractor.
- **Owners:** backend-engineer, ai-agent-engineer.

### SBV-02 LOW: drops at the item and unclaimed caps are still uncounted
- **Where:**
  - `SyllabusLimits.Apply` (`MockRuleExtractor.cs:512,517`) uses `Take(maxItems)` and `Take(MaxUnclaimed)` and does not count the excess.
  - The mock skips recurring items beyond the cap without counting them (`:125`) and truncates its unclaimed list at 200 (`:129`).
- **Scenario:** hostile or very long syllabi lose dates beyond the caps with `droppedInvalid` unchanged. That contradicts the new doc comment, and it is the silent-loss class SBR-06 is about. It is low-likelihood with the mock.
- **Fix:** count the overflow, still clamped to the wire maximum, in both `Apply` and the mock. Add a test with 201 unclaimed dates and with `MaxAiItems + 1` items.

### SBV-03 LOW: CodeQL is on v3, which is deprecated in about a month; pin comments are stale
- **Where:** `codeql.yml:44,49` (v3.28.3); `ci.yml:5-7`; `docs/ops/ci-matrix.md:47`.
- **Scenario:** the GitHub changelog cited in `action-pin-verification.md` says v3 is deprecated in December 2026. I did not re-read that changelog. Once v3 is deprecated, the scan job may warn or stop working. That would remove the CodeQL evidence for G5 just as it is first needed.
  - The comments still describe the pins as read from tags only, which understates the verification that was actually done and will mislead the next pin review.
- **Fix:**
  - Move `init` and `analyze` to `9b3254085494b55a0f7c4c2b3d2d52cc712ce370  # v4.39.1` after reading the v4 release notes for changed inputs. I matched that SHA by `ls-remote` on 2026-11-01.
  - Update both comments to cite `docs/security/action-pin-verification.md`.
- **Owner:** devops-engineer.

## Observations (Info, not findings)

- **SBV-I1:** the Kestrel paste rate floor and the 408 branch in `ReadBytesAsync` have never executed (TestServer). On Kestrel a rate violation probably sends Kestrel's own 408 and closes the connection, so the client may see 408 rather than the app's 400. That is UNVERIFIED. Add one Kestrel-hosted test, or accept the difference in the contract. Separately, `PasteMinBytesPerSecond` (16 KB/s) is not bound from configuration, unlike `PasteReadSeconds`.
- **SBV-I2:** at 16 KB/s, or with 1 MB in 30 s (about 35 KB/s), a legitimate paste on a very slow uplink could time out. A 50,000-character paste is normally under 200 KB, so this is acceptable, but measure it with the device matrix (G3).
- **SBV-I3:** after a deadline cancel, Kestrel tries to drain or close the unread body. The stalled client then holds a connection, not the create slot, and the server-wide connection limits apply. No change is needed.

## What I ran and what I did not verify

- **Ran (read-only):**
  - `git show --stat` and full diffs of `033b79d` and `a7c4c94`.
  - `git ls-remote` against `aquasecurity/trivy-action` (`v0.35.0`) and `github/codeql-action` (`v3.28.3*`, `v4.*`).
  - `curl` of `action.yaml` at `57a97c7e`.
- **Not run:**
  - any .NET or web test, build, workflow, the app or the dev stack;
  - a Kestrel slow-body probe;
  - a GitHub Actions run.
- **Not verified:**
  - the author's test counts;
  - that the tests fail on the old code (judged by reading);
  - the setup-trivy code at `e6c2c5e3` and its download checksum handling;
  - the v4 release notes;
  - the deprecation date (from the researcher's source);
  - Kestrel's 408 response behaviour;
  - multi-instance behaviour (the gate is per process, ADR-020d).
