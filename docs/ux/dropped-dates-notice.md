# Dropped-dates notice on the syllabus Review screen (`counts.droppedInvalid`)

Owner: adhd-ux-specialist. Status: SPEC, 2026-11-01, not built, not human-validated (gate G4 NOT_RUN). Implements the web half of security finding SBR-06 (`docs/security/syllabus-fixes-rereview.md`, `docs/security/findings-register.md`). Backend half landed in commit 033b79d. Depends on: `docs/ux/redesign-round2.md` (Note primitive 2.7, invariants 3.3), `docs/ux/language-guide.md` (binding), `src/web/src/lib/copyLint.ts` (lint), `docs/API_CONTRACTS_P2.md` (syllabus.v1, FROZEN).

Gate G1 (recording consent and Demo wording): **not touched**. The notice carries no consent, recording or Demo wording, and it is inserted after both `DemoLine`s, never between or before them. `copy.test.ts`'s Demo pins are unaffected.

## 1. What the backend sends (read, not assumed)

`Syllabus.counts.droppedInvalid` (integer, 0 to 1000, required) is written once when extraction persists (`Jobs.cs:143-149`, `MockRuleExtractor.cs` `SyllabusLimits.Apply`). It is the sum of:

1. proposals the extractor itself threw away (`output.DroppedInvalid`);
2. proposed items the orchestrator's length backstop dropped (a field did not fit its column);
3. proposed unclaimed dates the backstop dropped.

An item dropped under (2) whose **verbatim snippet** fits is kept as an unclaimed date (never a policy item). It is then **both** counted in `droppedInvalid` **and** present in `unclaimedDates` (`GET /syllabi/{id}/unclaimed-dates`, contract name "Dates we noticed but did not turn into items").

Consequences for the UI:

- The number is an **upper bound with double counting**, capped at 1000. "N dates could not be read" (the SBR-06 draft wording) is not true: not every drop is a date, and a demoted item is still visible once the unclaimed list is rendered.
- The web already parses the field (`src/web/src/lib/syllabus/parse.ts:216`, cap 1000). Nothing renders it. The MSW mock always sets 0 (`syllabusDb.ts` `recount`).
- There is **no unclaimed-dates UI in `src/web` today** (grep: only `types.ts`, `parse.ts`, mocks). This spec works before and after that list exists (section 6).

## 2. Decisions

| Question | Decision | Why |
|---|---|---|
| Show at all? | Yes, whenever the Review **list** renders and `droppedInvalid > 0`. | A deadline tool must say when its list may be short of the source. Truth over comfort (language guide principle 8). |
| Zero state | Render nothing. No DOM node, no placeholder, no spacing. | Quiet by default; the common case has no notice. |
| Form | One **Note** (`div.note.note-notice`), same shape as the existing partial note: a bold lead sentence and one plain sentence. Not a heading, not a region, not a live region, not dismissible, no fill. | Matches the three notes already on this screen; the design system allows no new `aria-live` (3.3 item 4). A completeness fact about deadlines is not dismissible (2.7 Note rule). |
| Show the number? | **No.** The sentence names the fact, not a count. The count stays in the export JSON (`counts`) and may be exposed as `data-dropped-invalid` for tests, never as visible text. | The number is overstated by design (section 1) and capped. A count invites a search for "the 3" that the student cannot finish, because nobody can say which 3. The action is the same for 1 and for 40: compare the list against the syllabus. Stating "some" is the exact truth; stating "3" is not. |
| Singular / plural | Not needed in the primary wording. A counted fallback is written in section 4 in case the Product Owner overrules; it pluralises "part / parts" and "an item / items". | |
| Stripe kind | `note-notice` (amber stripe, no fill), like the partial and hostile notes. | Same class of fact as the partial note: the list may not be the whole syllabus. Amber is this system's heads-up; red is only live capture (3.3 item 5). |
| Placement | In the `max-w-prose` block, **directly after the partial note (if any) and before the hostile / suspect note**, so the order is: h1, Demo lines, partial, dropped, hostile or suspect, purged, items error, then the summary. | Partial and dropped are both completeness facts and read as one thought; hostile is an accuracy fact; purged is retention. Keeps the "read this first" layer before "We found N items". |
| Where else | Nowhere else. Not on the course card, the dashboard, one-at-a-time, the calendar confirm or result, the processing screen, or `NotRead`. | Quiet mode (phase2 spec section 11; language guide section 6: facts about the review only where the student decides). |
| Tone | No subject that can be blamed: not the student, not the file, not "the AI". Says where the truth is (the syllabus) and what to check (dates not shown). | Language guide principles 3, 7, 8, 9. Never implies Tether is certain of anything. |

## 3. Wording (DRAFT, for `REVIEW_COPY` in `src/web/src/components/syllabus/copy.ts`)

```ts
/** SBR-06: counts.droppedInvalid > 0. No count is shown (docs/ux/dropped-dates-notice.md section 2). */
dropped: "Parts of this syllabus could not be turned into items.",
droppedBody: "Your syllabus is the complete list. Check it for dates that are not shown here.",
```

Rendered:

```
|  Parts of this syllabus could not be turned into items.              (bold, 17/28, ink)
|  Your syllabus is the complete list. Check it for dates that are not shown here.
```

Checks done against the lint (`copyLint.ts` FORBIDDEN and FORBIDDEN_SYLLABUS): no "missing", "lost", "fail", "error", "wrong", "incomplete", "remaining", "you need to / should / must", "make sure", exclamation marks, numbers or percent. Longest sentence 9 words (limit 20). "Check it" follows the existing imperative pattern ("Check the month and day", "Please check each date").

Why these words:

- "Parts of this syllabus" is true for all three drop sources and is also true when the items list is empty (the empty state says "No dates or deadlines were found"; this note says parts could not become items; neither contradicts the other).
- "could not be turned into items" names the outcome in the screen's own noun ("Review your syllabus items", "We found N items") without the mechanism.
- "Your syllabus is the complete list" states the source of truth in six words. It sits comfortably beside the partial note ("Tether read only part of this syllabus"): the syllabus is complete; Tether's read is not.
- "Check it for dates that are not shown here" is the one next action. It is open-ended because the task is open-ended; the spec does not pretend otherwise.

Not chosen, with reasons: "N dates could not be read. Check the original." (SBR-06 draft) overstates (section 1), collides with the existing "Pages that could not be read" which means something else, and hands the student a count they cannot resolve. "Some dates may be left out" implies loss the system cannot confirm. "Tether found more than it could keep" contradicts the empty-state sentence.

## 4. Counted fallback (only if the Product Owner overrules section 2)

```ts
droppedCounted: (n: number) =>
  n === 1 ? "1 part of this syllabus could not be turned into an item." : `${n} parts of this syllabus could not be turned into items.`,
```

If used, `n` must be labelled as an upper bound somewhere the student can read it, or the sentence is false in the demoted-item case. The spec's recommendation is to not use it.

## 5. Component behaviour (for `Review.tsx`)

```tsx
{syllabus.counts.droppedInvalid > 0 && (
  <div className="note note-notice mt-6 space-y-1" data-dropped-notice="">
    <p className="font-semibold">{C.dropped}</p>
    <p>{C.droppedBody}</p>
  </div>
)}
```

- Insert between the `status === "partial"` block and the `hiddenContentDropped || injectionSuspect` block.
- Rendered only in list mode (the one-at-a-time, confirm and result modes return before this block, as for the existing notes).
- Shown with the empty state too (`items && d.length === 0`): the student learns that the syllabus had parts Tether could not keep before choosing "try another version".
- No state, no handlers, no effect. Never focused by the app (the h1 keeps focus on mount). Nothing announced through `useAnnouncer`.
- Survives `tick` re-reads (it reads the `syllabus` prop, which the page owns); if the page re-fetches the syllabus, the count is whatever the server last said.
- No new CSS. `.note`, `.note-notice`, `.space-y-1`, `.font-semibold`, `.mt-6` exist. No skin rule, no Low-stimulation rule (there is no fill to strip), forced colours keep the existing `.note { border: 1px solid CanvasText }`.

## 6. Interaction with the unclaimed-dates list ("Dates we noticed but did not turn into items")

Today: no link, no action in the note (the list does not exist in the web).

When that list ships on the Review screen (separate spec; owner frontend-engineer with product-manager), the note may take **one** tertiary action, the Note's maximum of two not reached:

```
|  Parts of this syllabus could not be turned into items.
|  Your syllabus is the complete list. Check it for dates that are not shown here.   See dates we noticed
```

- `button.btn-tertiary.btn-sm`, visible label `See dates we noticed` (accessible name identical; WCAG 2.5.3), shown only when `counts.unclaimedDates > 0` and the list is rendered on this page.
- Behaviour mirrors "Show them": expands the list's disclosure if it has one and moves focus to its heading. No announcement.
- The sentence does not change. The syllabus stays the primary place to check; the noticed-dates list is a helper, because it holds only the drops whose snippet fit, not every drop.

## 7. Accessibility and quiet mode

- WCAG 2.2 AA targets: 1.3.1 and 1.3.2 (reading order = DOM order = visual order; plain paragraphs), 1.4.3 (ink on canvas, already in the contrast matrix), 1.4.1 (stripe plus words; never colour alone), 1.4.4 / 1.4.10 (68ch measure, wraps at 320px and 400% zoom like the other notes), 2.4.3 (no focus change), 4.1.3 (not a status message; nothing to announce). Future action: 2.5.8 (44px tertiary), 2.5.3.
- Screen reader: read in sequence after the Demo lines, as two sentences. No role, no `aria-live`, no heading (parity with the partial note, which keeps heading navigation at h1 then the group h2s).
- Exactly three live regions stay exactly three; no `role="status"` or `role="alert"` inside the note.
- Reduced motion: nothing moves. High contrast / forced colours: existing `.note` outline.
- Quiet mode: static text on the one screen where the student decides; no badge, count, nudge, notification or repetition on the card or dashboard; nothing inferred about the student.
- Low-stimulation: unchanged (no fill to strip; stripe, bold lead and words remain).

## 8. Acceptance checklist

Frontend and QA tick these; evidence is a test name or a capture, not a sentence.

1. `droppedInvalid: 0` on ready, partial, hostile and purged fixtures: no `[data-dropped-notice]`, no text "Parts of this syllabus".
2. `droppedInvalid: 1` and `droppedInvalid: 1000`: the same two sentences, byte-identical; no digit, `%` or "percent" anywhere inside the note.
3. The note is `div.note.note-notice` with no `role`, no `aria-live`, no heading element, no button (until section 6 ships), no close control.
4. DOM order: after the partial note when `status === "partial"`; before the hostile note when `hiddenContentDropped`; always before the "Review summary" region and after both Demo lines.
5. Empty state plus `droppedInvalid > 0`: "No dates or deadlines were found in this syllabus." and the note both render.
6. On mount the h1 has focus and the announcer log is empty; after Approve and Undo the log holds only the item messages (the note adds nothing).
7. Not rendered in one-at-a-time, confirm, result, `SyllabusCard`, `NotRead`, `Processing` with the same syllabus object.
8. axe: 0 violations with the note present (unit, and screens run at 390 and 1280 in Paper, Fog and one skin).
9. `copy.test.ts` lint passes with the two new strings (automatic via `collectStrings`); add one assertion that `REVIEW_COPY.dropped` and `droppedBody` contain no digits.
10. Live-region count test (existing) still reports three.

## 9. Test expectations

Unit (`Review.test.tsx`, Vitest + Testing Library, existing `mount` helper):

- `Review` takes `syllabus` as a prop, so a test can spread the fetched syllabus with `counts: { ...syllabus.counts, droppedInvalid: 2 }` and render directly. No mock change is needed for the component tests. The mock's `recount` resets `droppedInvalid` to 0, which only matters for flows that re-fetch the syllabus.
- Cases: checklist items 1 to 7 and 9; one axe run with the note present.
- `copy.test.ts`: the no-digits assertion (checklist 9).

Harness e2e (`playwright.harness.*`, MSW):

- One scenario or override with `droppedInvalid > 0` (`syllabusDb.ts` `Entry.override` exists, but `finish()` runs `recount` after spreading it, so `recount` must preserve `droppedInvalid` and `unclaimedDates` from the override; a small mock change for frontend-engineer).
- Assert: note visible at 390 and 1280, after the Demo lines, before "We found"; axe clean; still visible under Low-stimulation; not visible on the course card for the same syllabus.

Real-stack e2e: **not feasible today**. The mock extractor only drops on a bug, and the backend's `SyllabusRereviewFixTests` uses a stand-in extractor. A dev-only trigger is a backend decision (open question 4). Until then the real-stack syllabus e2e asserts only the zero state (no note on an ordinary paste).

Not covered by any automated test: whether the two sentences are understood by students (G4). Add to the human-research question list: "After reading this note, what would you do next, and where would you look?"

## 10. Risks and failure paths

- **Security / privacy:** the note renders two constant strings; no server text, no snippet, no count. Nothing new reaches the page from the syllabus beyond what `parse.ts` already validates.
- **Data integrity:** the note is derived from one integer the server wrote at persist time; it cannot drift from the server. If `parse.ts` rejects the syllabus (count over cap), the page shows its existing error and no note.
- **Overstatement in the other direction:** the note says "parts" even when a single proposal was dropped. Accepted: "parts" is a small plural about the syllabus, not a claim about a count.
- **Notice fatigue:** worst case four stacked notes (partial, dropped, hostile, purged). Rare; each is a true fact; fixed order keeps them readable. Revisit merging partial and dropped into one note if G4 testers report fatigue (Lead question 1).
- **Latency:** none. **Failure paths:** none new.

## 11. Open questions

For the Product Owner:

1. **No count (recommended) or count.** Section 2 omits the number because it is an upper bound with double counting. Security's SBR-06 draft said "N dates could not be read". A visible trace without a number is proposed as meeting the finding; needs security-engineer agreement and a line in the findings register.
2. **Is the unclaimed-dates list ("Dates we noticed but did not turn into items") in Phase 2 scope for the web?** It exists in the contract and backend, not in `src/web`. Section 6 reserves one action for it. If it is out of scope, the note stays as section 3 and the student checks the syllabus only.
3. **Confirm quiet-mode placement:** the note appears on the Review list only, never on the course card or dashboard.
4. **Dev-only fixture:** should backend-engineer add a dev-environment trigger (for example a marker line the mock extractor turns into an oversize title) so the real-stack e2e can exercise `droppedInvalid > 0`? Cost: a dev-only code path that must be unreachable in production.

For the Lead Architect:

1. Separate notes in fixed order versus folding the dropped sentence into the partial note when both show. Separate is specified; one line to fold later.
2. The `data-dropped-invalid` attribute: include for e2e or omit. Spec says optional, never visible.

G1: nothing here touches consent, recording status or Demo wording. G4: the two sentences are not human-validated.
