# Ruling: flagged policy rows and the open source quote (SBV-I18)

Status: DESIGN DECISION, 2026-11-02. Author: neurodivergent-ux-specialist. Scope: `src/web/src/components/syllabus/ItemRow.tsx` only. Not human-validated (gate G4 NOT_RUN). No source was edited.

Trigger: since the SBV-11 fix, a policy row with a supersession, injection or low-quality flag is `please_check`. `ItemRow.tsx:65` opens "See where this came from" for every `please_check` row, so flagged policy rows open their quote while other policy rows stay closed. Policy rows hide confidence, reason and hint (`ItemRow.tsx:165, 190, 263-275`), so nothing on the row says why.

## 1. Ruling

**(a) Flagged policy rows stay closed, like every other policy row.** The toggle remains, so the quote is one press away.

Why:
- The open-by-default quote exists to put "what is to be checked" before Approve (redesign-round2-review 4). A policy row has no Approve, Skip or decision. Open text with no action is load without a next step.
- In an injection-suspect or low-quality document every policy row would open at once, in a group whose note says "here for reference". That is the opposite of the group's purpose.
- The document-level notices (`REVIEW_COPY.hostile`, `REVIEW_COPY.suspect`) already tell the student to check each date; policies have no date.
- A single unexplained difference between rows ("why is this one open?") is the kind of ambiguity the guide asks us to remove.

**(b) No per-row cue.** A cue on a reference-only row would be the one place a policy row speaks about confidence, which the design deliberately hides for policies. If the Product Owner or a reviewer wants one anyway, use the DRAFT line in section 2, not an icon alone and not "Please check".

## 2. DRAFT cue (only if wanted; not recommended)

Shown as a `text-sm text-muted` line above the toggle, policy rows with `confidence === "please_check"` only:

> Tether is less sure about this line. You can read it word for word.

Lint check by reading `src/web/src/lib/copyLint.ts` (`FORBIDDEN` and `FORBIDDEN_SYLLABUS`): no "!", emoji, "just", clinical or attention words, "right now", fault words, "missing", backlog words, urgency, certainty claims, numbers or percent, "error", praise. Two sentences, 7 and 7 words (limit 20). Subject is Tether, not the student (guide 1.2). "Less sure" is a plain statement of AI uncertainty (guide 1.8), not a number (contract 13 row 2). The lint test (`copy.test.ts`) would run on it automatically once it is a key in `ITEM_COPY`.

## 3. Implementation note for the frontend engineer

One expression changes. `isPolicy` is already defined at `ItemRow.tsx:62`.

- `ItemRow.tsx:65`: `useState(item.confidence === "please_check")` becomes `useState(!isPolicy && item.confidence === "please_check")`.
- Update the JSDoc on the component (line 48-55) with one clause: "the quote opens by default only on a decidable `please_check` row; policy rows always start closed."
- Tests: `Review.test.tsx:102` ("'Please check' rows show their source open by default ...") stays as is (Lab report 2 is an assignment). Add one case: a `please_check` policy row renders "See where this came from" with `aria-expanded="false"`, no "Please check" text, no reason, no hint, and the toggle still opens it. The current fixtures have no such row (`mocks/syllabusData.ts:71` is `looks_right`, `HOSTILE` has no policy), so either add a flagged policy spec to `HOSTILE` or mount `ItemRow` directly with a hand-built item. If a spec is added to `HOSTILE`, check no hostile test counts rows (`Review.test.tsx:270` counts the default fixture only).
- Spec text: `docs/ux/phase2-syllabus-review.md` acceptance item 13 ("Low-confidence rows show their source snippet open") now means decidable rows; I will fold that one-line amendment into the spec when the change lands, so it is not edited here.
- Run `npx.cmd tsc --noEmit -p .`, `npx.cmd eslint .`, `npx.cmd vitest run` in `src/web` and report counts.

Not touched: the server rule that flagged policies are `please_check` (SBV-11) is correct and stays; the `ack` on approve (`ItemRow.tsx:83`) never fires for a policy.

## 4. Questions for a human reviewer (G4 NOT_RUN)

Run only through `human-research-coordinator` with voluntary, consented participants.

1. With all policy quotes closed, do students open any of them? Which ones, and why?
2. In a document with the hidden-text notice, do students expect the policy rows to show anything extra, or is the page-level notice enough?
3. Show the DRAFT cue (section 2) on one policy row. Does "less sure" read as calm information or as a warning? Does it make the student want to do something there is no control for?
4. Does a policy row that opens by itself (the current behaviour) read as "important" or as "something is wrong"?

## 5. What I read and did not run

Read: `ItemRow.tsx`, `copy.ts`, `copyLint.ts`, `copy.test.ts` (lint wiring), `Review.test.tsx` (lines 98-115, 264-313), `mocks/syllabusData.ts` (lines 60-99), `Review.tsx:547`, `docs/ux/phase2-syllabus-review.md` 5.2-5.6 and item 13, `docs/ux/language-guide.md`, `docs/ux/redesign-round2.md:894`, `docs/security/sbr-fixes-verification.md` SBV-I18. Ran nothing; no source edited.
