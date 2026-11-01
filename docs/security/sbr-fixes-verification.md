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

# Second pass: SBV-01, SBV-02 and the SBR-06 notice (commits f789acb, b6a51bc)

Reviewer: security-engineer. Date: 2026-11-01. HEAD `f789acb`, working tree clean at start. Commits reviewed: `f789acb` (SBV-01 canonical-snippet enforcement, SBV-02 cap-drop counting) and `b6a51bc` (the dropped-dates notice, spec `docs/ux/dropped-dates-notice.md`). New IDs continue the **SBV-** prefix.

**Method.** I read both diffs, the current `Jobs.cs` (`ProcessAsync`, `PersistAsync`), `MockRuleExtractor.cs` (`Extract`, `FindDates`, `Classify`, `AddUnclaimed`, `SyllabusLimits`), `DocumentText.Canonicalize`, `ItemService.cs` (hand-entered items), the new tests, `Review.tsx`, `copy.ts`, `syllabusDb.ts`, `MockProvider.tsx` and the offset-unit rules in `docs/API_CONTRACTS_P2.md` (row 38) and `docs/data/schema-syllabus-p2.md`. I ran **no** .NET or web test, build, app or dev stack. I ran one read-only `node -e` normalization probe (below). The test counts in both commit messages (145 syllabus, 333 AI, 22 architecture, 870 contract, 608 integration; vitest 2983) are the authors' claims. Nothing in `src/`, `tests/` or `.github/` was changed.

## Result

**New findings: 0 CRITICAL, 0 HIGH, 1 MEDIUM, 2 LOW.** Nothing blocks a push. The new MEDIUM (SBV-05) blocks production.

| ID | Verdict | Register status |
|---|---|---|
| SBV-01 LOW | **Verified.** Every stored snippet and context is cut by the server from the canonical text and checked ordinally against what the extractor sent. Offsets are bounded, surrogate pairs are not split, and there is only one write path | FIXED-VERIFIED |
| SBV-02 LOW | **Verified.** Every cap path in `Apply` and in the mock is counted, and the total is clamped to 1,000 | FIXED-VERIFIED |
| SBR-06 LOW | **Partial, stays OPEN.** The notice is a real, visible trace of backstop drops, and I agree that it should not show a count. But the student still has no list of unclaimed dates in the web app. So a demoted item's date, which the fix kept "so its date stays visible", is in fact visible nowhere | OPEN |

## SBV-01: verified

- **One write path.** `PersistAsync` (`Jobs.cs:149-183`) now stores only `kept.Items[*].Snippet` and `kept.Unclaimed`. Both come out of `SyllabusLimits.TryCanonical`, which replaces `Text` and `ContextText` with `canonical.Substring(...)` (`MockRuleExtractor.cs:529-532`). Demoted items use the same sliced `trace` (`:553-555,569-570`). grep finds no other `SnippetText =` or `ContextText =` writer in the module. Hand-entered items (`ItemService.cs:113-120`) carry no snippet.
- **The canonical string is the right one.** `ProcessAsync` reads `canonical_texts.text` once (`Jobs.cs:83`). It passes that same .NET string to the extractor and to `Apply` (`:87-88`). The offsets and the slice are therefore in the same UTF-16 string. The contract says offsets are UTF-16 code units, sliced in .NET and never in SQL (contract row 38), and the code follows it.
- **Bounds.** `Fits(s)` already requires `0 <= ContextStart <= Start < End <= ContextEnd`, a non-empty snippet and context, the column caps in code points, the span caps (1,200 / 3,000 units), and precision `exact` or `line`. `TryCanonical` adds `End <= canonical.Length` and `ContextEnd <= canonical.Length` (`:527`). Together these bound all four offsets to `[0, Length]` before any `Substring`. So nothing can throw, and the test `SBV01_offsets_beyond_the_canonical_text_are_dropped_and_counted_not_thrown` covers it.
- **Code-point boundaries.** `IsBoundary` rejects a cut between a high and a low surrogate at all four offsets (`:513-514,528`). The stored canonical text cannot contain a lone surrogate: `Canonicalize` strips the `Surrogate` category and U+FFFD (`DocumentText.cs:42`), and a lone surrogate in the input makes `Normalize` throw first (B1V-07, open and unchanged). So "not inside a pair" is the same as "on a code-point boundary". Test: `SBV01_a_window_that_splits_a_surrogate_pair_is_dropped_even_if_the_texts_agree`.
- **Ordinal comparison.** `string.Equals(..., StringComparison.Ordinal)` is used for both the snippet and the context (`:531`). Culture, case and normalization-insensitive matches are not possible. A context forged at the same length is caught by `SBV01_a_forged_context_is_dropped_too_even_when_the_snippet_matches`.
- **A forged snippet leaves no trace.** An item whose snippet is not canonical is dropped and counted, and it is **not** demoted (`trace` is null). That is correct: otherwise the forged words would come back as a "date we noticed".
- **Integration test** (`SyllabusForgedSnippetTests`, stand-in `ForgingExtractor`). It forges one item's snippet text at the right offsets and adds an unclaimed date that runs past the end of the text. It asserts `droppedInvalid == 2`, that no stored row contains "moved", and that every stored snippet and context equals the SQL `substr` at its offsets. The SQL comparison is valid only because the fixture is ASCII (PostgreSQL `substr` counts code points), and the test says so. The unit tests cover astral text. On the old code both forged rows would have been stored, so the test would fail (judged by reading, not watched).
- **UTF-16 offsets vs NFC.** The equality check does not depend on normalization: it compares the stored string with itself at its own offsets. Npgsql's UTF-8 round trip is lossless for text with no lone surrogates. But the stored text is **not always NFC**, although the contract and the `Canonicalize` doc comment say it is. `Canonicalize` normalizes **before** it strips format characters, marks and spaces (`DocumentText.cs:24` then `:40-77`), and nothing normalizes again afterwards. Probe (RAN, `node -e`, same Unicode algorithm as .NET ICU): `"e​́"` is NFC; with the U+200B removed, `"é"` is **not** NFC (it composes to U+00E9). For SBV-01 this fails safe: a real extractor that quotes the NFC form gets its snippet rejected and counted, not stored. See SBV-04.
- **What SBV-01 does not cover** (unchanged requirements before a real extractor, `syllabus-built-review.md` "Requirements"): TextPolicy on the model-written `title`, `written.dateText`, `timeText` and `confidenceReason`. The orchestrator checks only their lengths (`Fits(ProposedItem)`). The mock runs `TextPolicy.TryValidate` on its own titles (`MockRuleExtractor.cs:414`), but a real extractor would not be forced to. This is not a new row: it is already a listed precondition. The extractor also still chooses the context window: see SBV-06.

## SBV-02: verified

Every path where a proposal can be lost to a cap now increments a counter:

| Path | Where | Counted |
|---|---|---|
| Items beyond `maxItems` given to the orchestrator | `Apply` `:548,561` | yes (`overflow`) |
| Unclaimed dates beyond 200 given to the orchestrator | `Apply` `:558,561` | yes (`overflow`) |
| Demoted item with no room left in the unclaimed list | `Apply` `:568` | yes (already in `droppedItems.Count`) |
| Mock policy item past the item cap | `Extract` `:93-94` | yes |
| Mock TBA item past the cap | `:101-102` | yes |
| Mock dated item past the cap | `:115` | yes (was already) |
| Mock recurring item without a date, past the cap | `:133` | yes (was silent) |
| Mock unclaimed date past 200 | `AddUnclaimed` `:471` | yes; the `Take(200)` truncation is gone |

- **No double count.** The mock now hands over at most 200 unclaimed dates and at most `MaxAi()` = 200 items. So the orchestrator's overflow is 0 for them, unless `MaxAiItems` is configured below 200, and in that case only `Apply` counts those items.
- **Clamp.** The sum is a `long`, a negative extractor count is floored at 0, and `Math.Clamp(dropped, 0, 1000)` uses `MaxDroppedInvalid`. Test: 700 + 700 overflow gives 1,000.
- **Tests.** 201 identical items and 201 unclaimed dates in `Apply`. The mock with 201 unclaimed dates. The mock with policy, TBA and recurring lines past a full cap, with an under-cap control showing that those three lines are items otherwise. An integration test with 201 unclaimed dates gives `unclaimedDates 200` and `droppedInvalid 1`.
- **Guards that are not caps.** `AddUnclaimed` still returns silently when a hit is out of its line or has a space at either end (`:467-470`). `FindDates` trims `MonthDay` hits, and the ISO and numeric patterns cannot match spaces. So I believe these guards are unreachable (READ). `p.Alternatives.Take(3)` trims alternatives of a kept item, not proposals. Neither is a drop of a date.

## SBR-06: does the notice without a count satisfy it?

**Partly. I accept the no-count decision, but SBR-06 stays OPEN.**

- **Agreed: no count.** `droppedInvalid` is an upper bound that counts a demoted item twice (as a drop and as an unclaimed date), and it is capped. "N dates could not be read" (my original fix text) would be false in the demotion case, and the student could not act on the number anyway. The constant two-sentence note is accurate. I withdraw the count from the SBR-06 fix. The `data-dropped-invalid` attribute puts the number in the DOM, but it is the student's own data on their own page, so there is no exposure.
- **Verified for what it does (READ).** The note renders iff `counts.droppedInvalid > 0`, on the Review list only, as two constant strings with no server text. It has no live region, role or focus change. Unit tests cover the spec's checklist items 1-7 and 9, plus axe on the unit render. The MSW marker `mock:dropped-invalid` exists only in the mock DB, which loads only when `NEXT_PUBLIC_API_MOCKING=enabled` (`MockProvider.tsx:5`). The harness e2e and the axe runs at 390/1280 were not done (the commit says so).
- **Not satisfied: the student still has no list of unclaimed dates.** grep of `src/web/src/components` finds no use of `unclaimed` (it appears only in `types.ts`, `parse.ts` and mocks), and nothing calls `GET /syllabi/{id}/unclaimed-dates`. The SBR-06 fix had two parts: count the drop, and keep a dropped item's date visible as an unclaimed date. The second part is stored in the database but appears nowhere in the app. The note tells the student to check the syllabus, which is the honest fallback, but it gives no help locating the date Tether actually saw.
- **The broader gap is worse than SBR-06 and is now SBV-05 (MEDIUM).** The note covers only drops. Dates the extractor deliberately classed as unclaimed are not drops: `droppedInvalid` stays 0, so **no note appears and the dates are shown nowhere**. With the mock that is the common case, not an edge: any dated line without a recognised keyword (`Classify` returns null, `:142-152`), for example "Presentations Oct 21, 2026", and every second date on a claimed line ("Quiz 1 due Oct 21, also Nov 3"). With a real extractor, hostile syllabus text could steer a real deadline into `Unclaimed` instead of `Items`. That would hide it with no drop, no count and no notice: exactly the "one deadline vanishes" scenario SBR-06 was about, by a route the notice does not cover.
- **What closes SBR-06 and SBV-05:** render "Dates we noticed but did not turn into items" on the Review screen (UX spec section 6 already reserves the link). Or, if the PO rules that list out of Phase 2 scope (UX open question 2), show a count-free note whenever `counts.unclaimedDates > 0`, and record that ruling. Either way, add a test with `droppedInvalid == 0` and `unclaimedDates > 0`.

## New findings

### SBV-04 LOW: the canonical text is not guaranteed NFC, although the contract says it is
- **Where:** `DocumentText.cs:24` (NFC first), then `:40-77` and `:80-97` (strip Cf, variation selectors, fillers, orphan or excess marks; collapse spaces), with no second normalization. Contract section 2.2 (`API_CONTRACTS_P2.md:56`) and the doc comment at `DocumentText.cs:18` say "The result is NFC".
- **Proof (RAN, `node -e`):** `"e​́".normalize("NFC") === itself` is true. After removing the U+200B, `"é"` is not NFC (it normalizes to U+00E9). By reading, `Canonicalize` removes U+200B (Cf, not ZWJ/ZWNJ) and keeps U+0301 (a mark after a letter). I did not run the .NET path.
- **Impact:** no security bypass. The SBV-01 check compares the stored text with itself. But (a) a real extractor or any server-side search that works on NFC text will fail to match these spans. Under SBV-01 those snippets are rejected and counted, so a legitimate date can fall into "parts could not be turned into items". (b) The stored text breaks a documented invariant that later code may rely on, for example dedupe or hashing of canonical text. The trigger is a zero-width or format character between a letter and a combining mark, which is common in PDF-copied text.
- **Fix:** normalize to NFC once more after stripping and collapsing, before the text is stored and before any offset is computed. Add a `Canonicalize` test with `"e​́"`, and assert `IsNormalized(FormC)` on the output for every red-team fixture. Owner: backend-engineer.

### SBV-05 MEDIUM: unclaimed dates are stored but never shown, and no notice covers them
- **Where:** `Jobs.cs:179-183` stores them. `GET /syllabi/{id}/unclaimed-dates` exists. `src/web/src/components` never fetches or renders them (grep). `Review.tsx:351-357` shows a note only for `droppedInvalid > 0`.
- **Scenario:** a date that the extractor classes as unclaimed (mock: any dated line with no recognised keyword, and every extra date on a claimed line; real model: anything a hostile line steers there) is shown nowhere, and the Review screen gives no hint that it exists. For a deadline tool, that is silent loss of a date the system actually found. It is reachable today with the mock and ordinary syllabi.
- **Why MEDIUM:** I was unsure between LOW and MEDIUM. It is reachable now with ordinary input, not only through a hostile edge case, and it breaks the completeness promise the SBR-06 work depends on. The fix is small. It does not block a push. It must be fixed before production or real students.
- **Fix:** render the unclaimed-dates list on Review, or a count-free note when `counts.unclaimedDates > 0` (PO scope ruling, UX open question 2). Tests: `droppedInvalid 0` with `unclaimedDates > 0` shows the trace; the real-stack e2e pastes "Presentations Oct 21, 2026" and finds it on Review. Owners: frontend-engineer, neurodivergent-ux-specialist, product-manager.

### SBV-06 LOW: the extractor still chooses the context window, so a verbatim quote can be cut to hide a negation
- **Where:** `TryCanonical` accepts the extractor's `ContextStart` and `ContextEnd` once they are canonical (`MockRuleExtractor.cs:523-534`). `Fits` allows `ContextStart == Start` and `ContextEnd == End`.
- **Scenario (real extractor only):** from "The final is NOT on Dec 9", a steered model returns the snippet "Dec 9" with the context set to "on Dec 9". Both are verbatim slices, so SBV-01 passes them, and Review shows "on Dec 9" as the student's evidence. The mock always uses the full line (`ContextWindow`), so this cannot happen today.
- **Fix before a real extractor:** the orchestrator computes the context itself from the snippet offsets: the containing line or lines, bounded as `ContextWindow` does. It ignores the extractor's context offsets, or rejects proposals whose context is narrower than that window. Add a test with a narrowed context. Owners: backend-engineer, ai-agent-engineer.

## Observations (Info, not findings)

- **SBV-I4:** `Precision` (`exact` or `line`) is still the extractor's word. The text is now canonical, so it is only a label, but a real extractor's `line` should be checked against `LineSpans`.
- **SBV-I5:** the extractor's unclaimed list is not deduplicated by span (`Apply` dedupes only demoted items). A real extractor could fill the 200 slots with copies of one span. That is crowding, not loss, and the overflow is counted.
- **SBV-I6:** a snippet may still start or end inside a grapheme cluster (between a base and its combining mark, or inside a ZWJ emoji). It is verbatim and rendered as text, and it is cosmetic only.

## What I ran and what I did not verify (second pass)

- **Ran (read-only):** `git show --stat` and full diffs of `f789acb` and `b6a51bc`; one `node -e` NFC probe.
- **Not run:** any .NET or web test, typecheck, build, the app, the dev stack, the harness e2e or the axe runs at width.
- **Not verified:** the authors' test counts; that the new tests fail on the old code (judged by reading); the .NET `Canonicalize` output for the SBV-04 input (inferred from the code and the JS probe); Npgsql round-trip byte identity (from the DB-09 design test, not re-run).

# Third pass: the 'Dates we noticed' build (commit 400f1cc, PO-20b / SBV-05)

Reviewer: security-engineer. Date: 2026-11-01. HEAD `400f1cc`, working tree clean at start; `400f1cc` and four earlier commits are not on `origin/main` yet. Spec: `docs/ux/unclaimed-dates-list.md` section 12 (and checklist section 10). Scope calibrated to the personal, non-commercial tool (PO-15/16); every issue is reported anyway.

**Method.** I read the full diff of `400f1cc` for `UnclaimedDates.tsx`, `UnclaimedAddForm.tsx`, `Review.tsx`, `ItemEditForm.tsx`, `storage.ts`, `copy.ts`, `lib/syllabus/{client,parse,requests,types}.ts`, `mocks/{syllabusHandlers,syllabusDb,unclaimedFixtures}.ts`, `.github/workflows/ci.yml`, the new e2e `e2e/syllabus-unclaimed.spec.ts`, and the test names and key assertions in `UnclaimedDates.test.tsx`, `Review.test.tsx`, `drafts.test.tsx`, `schemas.test.ts`, `unclaimed.test.ts`, `syllabusUnclaimedHandlers.test.ts`. I also read `PlainTextSpan.tsx`, `plainText.ts`, `Announcer.tsx`, `MockProvider.tsx`, `next.config.mjs`, `AuthProvider.tsx` (sign-out), `ItemService.cs` (create), `SyllabusEndpoints.cs` (unclaimed route), `Jobs.cs:179-183`, the unclaimed-date and problem schemas, and `API_CONTRACTS_P2.md` rows 38, 42 and 133. I ran **no** test, typecheck, lint, build, app or dev stack. Nothing in `src/`, `tests/` or `.github/` was changed.

## Result

**New findings: 0 CRITICAL, 0 HIGH, 0 MEDIUM, 3 LOW (SBV-07, SBV-08, SBV-09). Nothing blocks a push.**

| ID | Verdict | Register status |
|---|---|---|
| SBV-05 MEDIUM | **Fixed.** Review fetches and renders the unclaimed dates whenever `counts.unclaimedDates > 0` and the text is not purged, including `droppedInvalid == 0`. Tests that would fail on the old code exist at unit and real-stack level | OPEN to FIXED-VERIFIED (READ, NOT RUN) |
| SBR-06 LOW | **Fixed with SBV-05.** A demoted item's date is now visible; the dropped Note gets "See dates we noticed" | OPEN to FIXED-VERIFIED (READ, NOT RUN) |

**Why FIXED-VERIFIED and not "OPEN pending a run".** The register's FIXED-VERIFIED means I read the fix and a test that would fail without it; SBV-01 and SBV-02 were closed the same way. That holds here: `Review.test.tsx` 'no items but noticed dates' and `UnclaimedDates.test.tsx` need the section to exist, and `e2e/syllabus-unclaimed.spec.ts` pastes keyword-free dated lines through the real API and expects 200 rows. **Condition:** the flows-a shard that now runs this spec has never run on GitHub. After the push, read that run (`gh run list`, `gh run view <id> --log-failed`). If the spec fails for a product reason (not L-RATE or environment), reopen SBV-05 and SBR-06.

## Checks against spec section 12

| Check | Result (READ) |
|---|---|
| Untrusted snippet and context render as text only | **Pass.** Every piece goes through `<PlainText>` (a React text node after stripping C0/C1 controls and bidi characters). No `dangerouslySetInnerHTML`, `innerHTML`, markdown, link detection or data-built `href` in any changed file (grep). The text also goes into `aria-label`s and the shared announcer as React-escaped strings. Bidi characters are not stripped there, but the server already strips them from the canonical text (`DocumentText.Canonicalize`, `TextPolicy.IsBidiControl`), as for `ItemRow` titles |
| Date in bold by offsets | **Pass.** `a = start - ctx.start`, `b = end - ctx.start`; bold only if `0 <= a < b <= ctx.text.length` and `ctx.text.slice(a, b) === snippet.text`. Offsets are UTF-16 code units (contract row 38), the unit `String.prototype.slice` uses. The bold text equals `snippet.text` by construction, so even a coincidental match cannot bold the wrong words. No regex runs over the text |
| Offset mismatch fallback | **Pass.** On a mismatch the page shows the snippet blockquote, then "The line around it:" and the context blockquote, with no `strong`. When `context.text === snippet.text` it shows one quote. Tests: 'offsets that disagree with the line', 'context equal to the date' |
| Very long lines | **Pass.** `whitespace-pre-line break-words`; a 1,500-character fixture (`rowLongLine`); the e2e checks no horizontal scroll at 390px. Parser caps: snippet 600, context 1,500 (`parseSnippet`) |
| No HTML sink | **Pass**, by grep and by reading. Gap: no test feeds HTML-, formula- or bidi-looking text into this section (SBV-08 a) |
| Prefilled title, edited by the student | **Pass.** `prefillTitle` applies `plainText`, collapses whitespace, trims, and cuts at 200 code points at the last space (hard cut when there is none). The field is an ordinary editable input. Nothing is sent until the student presses Add item |
| What POST /syllabi/{id}/items sends | **Pass.** `itemCreateBody` returns exactly `{kind, title, date, allDay, time}`, with `time: null` when all day. It sends no `timezone`, no `notes` and no link to the unclaimed id. `kind` comes from the editable kinds select. The title length is checked in code points on the trimmed value, then whitespace is collapsed, which can only shorten it. Tests: `schemas.test.ts` (ajv against `syllabus-item-create-request`, exact keys), `unclaimed.test.ts`, `UnclaimedDates.test.tsx` (`toEqual` on the body), e2e (`postDataJSON` toEqual) |
| Idempotency-Key | **Pass.** A fresh `newIdempotencyKey()` per press. It is reused only when the last attempt with the identical body fingerprint failed on the network, and is discarded after any answered response. A malformed key is refused before any request. The server decides the key under the syllabus lock (`ItemService.cs:93-102`). Test: 'network: values and the Idempotency-Key are kept for the retry; an answered error discards the key'. See SBV-I7 |
| Length cap, 200 code points | **Pass.** Client `validate` counts code points; the server re-validates through TextPolicy; the mock rejects more than 200 |
| Body is exactly the contract fields | **Pass** (above). The request schema is `additionalProperties: false`, and the mock rejects unknown members |
| localStorage keys user-scoped, ids only | **Pass.** `tether.syllabus.u.<user>.unclaimed.<syl>.<unc>` holds `{"s":"hidden"}` or `{"s":"added","itemId":...}`. `isState` accepts exactly those shapes, and `readDraftsUnder` reads only the signed-in owner's prefix. A test asserts that no snippet text appears anywhere in `localStorage`. The add-form draft (`unclaimed-add.`) holds title, date and time, including the prefilled syllabus line, which the spec allows (SBV-07) |
| Cleared on sign-out and delete | **Pass.** Sign-out calls `clearAllDrafts()`, which removes all `u.` keys (`AuthProvider.tsx:45`). Syllabus delete calls `clearSyllabusDrafts`, which now names `edit.`, `unclaimed.` and `unclaimed-add.` separately; the test covers the `s1` vs `s10` prefix trap. Not cleared on canonical-text purge (SBV-07) or course delete (existing SBR-09) |
| Parser strictness | **Pass with a residual.** `parseUnclaimed` refuses any row member other than `id` and `snippet`, a null snippet and a bad id. `parseUnclaimedPage` refuses any page member other than `items` and `nextCursor`. One bad row refuses the whole page, and more than 200 rows in total is refused. Inside `snippet` and `context`, unknown members are ignored rather than refused (SBV-08 b) |
| MSW handlers never ship | **Pass; the mechanism is unchanged.** The new handlers join the existing set, which loads only through `import("@/mocks/browser")` behind `NEXT_PUBLIC_API_MOCKING === "enabled"` (`MockProvider.tsx:5`). That flag is set only in `.env.development` and `.env.example`. No production module imports `@/mocks` (grep). A production build may still emit the lazy chunk, but it is never loaded; not new |
| No request to a Google host | **Pass.** No Google host in any changed source file (grep). The e2e records every request whose hostname matches `/google/i` and asserts the list is empty. `copy.test.ts` keeps 'google' out of the copy |
| No model text in the path (SBV-01) | **Pass.** Unclaimed `SnippetText` and `ContextText` are server slices of the canonical text (`Jobs.cs:179-183`, via `SyllabusLimits.Apply` / `TryCanonical`, verified in the second pass). Demoted items keep no model title. The client derives the prefilled title from those slices. The created item's title is what the student sends, and the row shows the server's 201 body. No server change in this commit |
| No new aria-live | **Pass.** No `aria-live`, `role=status` or `role=alert` in the new components (grep). Announcements use `useAnnouncer()`, and `aria-busy` is only on the Add item button. Tests: `liveInside()` is empty in every state, and the Review tests assert no live ancestor |
| 409 invalid_state ambiguity | **LOW (SBV-09)**, below |

## New findings

### SBV-07 LOW: the add-form draft keeps a syllabus line on the device after the server purges the text
- **Where:** `UnclaimedAddForm.tsx:57-77`. On the first change to any field, `update()` writes the whole value set to `unclaimed-add.<syl>.<unc>`, including the prefilled Title, which is a syllabus line. `Review.tsx` stops rendering the section once `retention.canonicalTextPurged` is true, and nothing clears `unclaimed-add.` then.
- **Scenario:** the student opens Add as item, types a date and leaves. Later the server purges the canonical text and nulls the unclaimed snippets (`Jobs.cs:341`). The line stays in this browser's `localStorage`, unreachable from the UI, until sign-out or syllabus delete.
- **Why LOW:** it is the same user on their own device, it is the same class of data as the existing edit and paste drafts, and spec 12 accepts it. It does extend SBR-09 (no TTL; an expired session and course delete keep drafts) and slightly undercuts the retention promise.
- **Fix:** on a purged syllabus, remove the `unclaimed-add.<syl>.` drafts (optionally the `unclaimed.<syl>.` states too) and add a test. Owner: frontend-engineer.

### SBV-08 LOW: client residuals on the new path
- (a) No test renders hostile-looking text in the section: the `hostile` Review fixture has `unclaimedDates: 0`. The rendering is safe by construction (PlainText), but a section-level test would catch a regression such as added link detection. Use snippets like `<img src=x onerror=alert(1)>Oct 5` and `=HYPERLINK("https://x.invalid","Oct 5")`, and assert `textContent` and no element children besides `strong` and its `span`s.
- (b) `parseSnippet` (shared with items) ignores unknown members inside `snippet` or `context` instead of refusing them. Only named fields are copied, so nothing extra reaches the UI, but the parser is looser than the schema and the spec's "reject unknown members".
- (c) `listUnclaimedDates` stops after two pages and returns what it has, even if page 2 still carries a `nextCursor`, which is a silent cut (L-DROP). Unreachable with today's server: the client asks for `limit=200`, the cap is 200, and `SyllabusEndpoints.cs:279-283` sets a cursor only when more than `limit` rows exist. Fix: throw `SyllabusParseError` if a cursor remains after page 2.
- Owner: frontend-engineer.

### SBV-09 LOW: one 409 code for "not ready" and "item cap reached"
- **Where:** `ItemService.cs:104` (status not ready/partial) and `:107` (`total >= MaxItems || students >= MaxStudentItems`) both return `SyllabusProblem.InvalidState()`. The contract table (`API_CONTRACTS_P2.md:133`) documents 409 `invalid_state` only "while not ready/partial". The mock mirrors the server. The client shows `ACTION_COPY.generic` for both and leaves `UNCLAIMED_COPY.form.limit` unused (as `copy.ts` states).
- **Rating, LOW:** no security or integrity effect. The caps are enforced server-side under the syllabus lock, nothing is written, and the student is told nothing changed. What remains is an undocumented server behaviour and a message that does not say why, which can cost the student a retry loop. The 100-student-item cap is unlikely to be reached on one syllabus.
- **Proposal (additive; the Lead owns contract text):** keep status 409 and add a distinct code `item_limit_reached`, parallel to the existing per-item `event_limit_reached`. That needs:
  - the `errorCode` enum in `syllabus-common.schema.json`;
  - contract row 133 and section 9;
  - `ItemService.cs:107`;
  - a contract test and a problem fixture;
  - the web mapping of the new code to `form.limit`.

  This client already copes: `ApiError.code` is a plain string, and unknown codes fall to the generic message. The change alters the response for an existing condition, so the Lead must confirm it is allowed under "syllabus.v1 frozen for mock-only". The minimum alternative is to document in row 133 that the caps also return `invalid_state`. Owners: Lead Architect, backend-engineer, frontend-engineer.

## Observations (Info, not findings)

- **SBV-I7:** a new key is sent after a network failure if the student edits the form, or closes and reopens it (the pending key lives in a ref that dies with the component). If the first attempt did reach the server, the student gets two items. Both are visible, undecided and deletable, and nothing goes to the Calendar without approval, so this is acceptable for v1. Persisting the key with the draft would remove the risk.
- **SBV-I8:** Hide and "added" are per device (a spec residual, Lead question 1). Hide is the student's choice and deletes nothing on the server, and the export still includes every unclaimed date.
- **SBV-I9:** SBV-06 (the extractor chooses the context window) is now on screen: the quote and the prefilled title both use `context.text`. The mock uses the full line, so it stays LOW. The fix is unchanged and must land before a real extractor.
- **SBV-I10:** `.github/workflows/ci.yml` changes only the flows-a spec list and a comment. No new action, permission, secret or trigger.
- **SBV-I11 (public repo):** the new fixtures and the e2e text are synthetic ("Art 210 syllabus (synthetic test text)", "Bring a calculator ..."). The diff contains no personal data, keys or real syllabus content (read).

## What I ran and what I did not verify (third pass)

- **Ran (read-only):** `git show --stat` and diffs of `400f1cc`; `git log origin/main..HEAD`; `git status --short`; grep/sed over tracked files; the register's section 6 per-status count recipe, before and after editing the register.
- **Not run:** vitest, tsc, eslint, any .NET test, the e2e specs, axe, screens or geometry, a production `next build`, or the app.
- **Not verified:**
  - the author's "vitest 3072" and "real-stack e2e pass locally";
  - that the flows-a shard passes on GitHub (it has never run);
  - that the tests fail on the old code (judged by reading);
  - whether a production build emits the MSW chunk (not built).
