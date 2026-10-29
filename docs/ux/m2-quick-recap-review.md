# M2 Review: I Lost Track and TL;DR UI

**This is an AI specialist review (adhd-ux-specialist), 2026-10-28. It is NOT human ADHD-student, clinician or screen-reader-user validation.** Findings are design hypotheses from reading code and docs. Nothing was run, rendered or tested with a screen reader or device. Gate "ADHD suitability and learning-science effectiveness" stays NOT HUMAN-VALIDATED. All copy below is draft wording and still needs the plain-language review in `copy.test.ts` plus human review.

## Scope and sources

Reviewed (read only, no source edited):
- `src/web/src/components/lecture/LostTrackPanel.tsx`, `useLostTrack.ts`, `TldrPanel.tsx`, `LectureScreen.tsx`, `copy.ts` (also `Announcer.tsx`, `src/app/globals.css`, `lib/lecture/types.ts` for context)
- Against `docs/ux/student-experience.md` (sections 1, 5.1, 5.5, 5.6, 6, 9) and `docs/PRODUCT_REQUIREMENTS.md` (M2-S4, M2-S5, M2-S8)

Assumptions: the layout in `LectureScreen.tsx` is the layout that ships (a two-column grid at `lg`, one column below). Server payload shape is as in `lib/lecture/types.ts`.

## What is already good (keep)

- One press runs recovery; no confirm step; no auto retry; fresh idempotency key per press (`useLostTrack.ts`).
- Non-shaming, calm core copy: "Pick how far back to catch up. The lecture keeps going.", "Nothing was lost.", "Got it, back to lecture".
- Honest degraded states: extractive "What was said" fallback with a plain reason (matches UX 5.6 and PRD M2-S5 AC7). The AI version never gets replaced by the extractive one (`mergeCatchUp`).
- Window buttons are 56px, grouped with `aria-label`, `aria-disabled` while busy (stays focusable), visible text "Last 2 min" is contained in the accessible name (SC 2.5.3).
- Result heading takes focus after the student's own press; TL;DR updates never move focus; `aria-live="off"` on TL;DR; opt-in TL;DR announcement.
- "Hold updates" exists (PRD M2-S4 AC4). Demo labelling is permanent and non-dismissible.
- Quiet mode: nothing unsolicited, no timers, no streak or count of presses. Compatible as built.

## MUST-FIX

### M1. I Lost Track is not "always visible"; on phone it sits below the whole transcript
Refs: `LectureScreen.tsx:164-181`; PRD M2-S5 AC1; UX 5.1 (sticky bottom button on phone, top of right panel on desktop).
Below `lg` the grid is one column and `Transcript` renders first, so the panel is after a long, growing list. A student who has lost track must scroll through the thing they cannot follow. On desktop the right column is not sticky, so a long result or page scroll can also push it away.
Fix (Frontend): below `lg`, render a sticky bottom bar holding the three window buttons (heading collapsed to a visible label "I lost track"), and show the result as a bottom sheet or an in-flow card above the transcript. On `lg`, make the right column `sticky` with `max-height` and `overflow-y:auto`. Keep DOM order matching visual order. Respect scroll-padding for the sticky bar (SC 2.4.11).

### M2. No skip link or shortcut to I Lost Track (and TL;DR)
Refs: `LectureScreen.tsx:139-141` (only "Skip to transcript"); `copy.ts:164` `skipToTldr` is defined but unused; shortcut handler `LectureScreen.tsx:92-107` has only Alt+P and Alt+J; UX AC10; PRD M2-S5 AC6 ("documented shortcut plus button").
Fix: add skip links "Skip to I lost track" and "Skip to TL;DR". Add `Alt+L` (opens/focuses the window group, does not start a catch-up on its own) behind the existing shortcuts setting, and list it in `SETTINGS_COPY.shortcutList`. Note: the shortcut setting is off by default, so the skip link is the primary keyboard path.
Replacement copy:
- `TRANSCRIPT_COPY.skipToLostTrack`: "Skip to I lost track"
- `SETTINGS_COPY.shortcutList`: "Alt+P pauses or resumes. Alt+J jumps to the live transcript. Alt+L goes to I lost track."

### M3. Remembered default window is missing
Refs: `LostTrackPanel.tsx:234-246`; PRD M2-S5 AC1 ("default 5; remembered"); decision D11.
All three buttons are identical primary buttons, so the student must choose under stress, and nothing is remembered. Fix: mark the last-used window (default 5) visually as the first and largest choice (text marker, not color-only), for example the label "Last 5 min (usual)". Persist in `settings.ts` localStorage alongside the other per-viewer settings. Do not auto-run anything on load.
Replacement copy:
- `LOST_TRACK_COPY.windowUsual`: (m) => `Last ${m} min (your usual)`; accessible name `Catch up on the last ${m} minutes, your usual choice`.

### M4. The "What was said" fallback can be a wall of text, at the moment of highest overload
Refs: `LostTrackPanel.tsx:195-203` (`r.text` rendered whole with `whitespace-pre-wrap`); UX rule 3 (chunks), UX 5.6; PRD AC7.
A 10-minute extractive window is roughly 1,000+ words. The same path also runs in Pending, Unavailable and BudgetLimited states, so it is the most common degraded view. Open question for backend: does the server cap `text`? If not, the UI must.
Fix: show only the last ~5 lines (or ~120 words) by default, with a "Show earlier" button (`aria-expanded`), split into short lines with timestamps, and keep the raw text accessible. Keep the 3 citation buttons.
Replacement copy:
- `LOST_TRACK_COPY.saidNote`: "These are the words from the lecture, newest last. Not a summary."
- `LOST_TRACK_COPY.showEarlier`: "Show earlier words"
- `LOST_TRACK_COPY.hideEarlier`: "Show fewer words"

### M5. Short lecture, empty window and gaps are not disclosed
Refs: `LostTrackPanel.tsx:88` (`C.covers` always says the requested window); `types.ts:83-84` (`coveredFromMs`/`coveredToMs` are available but unused); PRD M2-S5 AC3 and AC8; UX 5.6.
A student 3 minutes into a lecture who pressed "Last 10 min" is told "Covers the last 10 minutes". Also, an empty `text` (nothing said, or paused) renders a blank block. Gaps are only shown when the AI returns `gaps`; the extractive view discloses none.
Fix: compute the real span from `coveredFromMs/coveredToMs`; if shorter than requested show the short-lecture line; if empty show the empty line; surface transcript gap/pause markers that fall in the window (needs Frontend and Backend to confirm where this data lives; document the question for the Lead Architect if not in the payload).
Replacement copy:
- `covers(m)` stays for the full case. Add `coversShort: (n) => \`You have only been here ${n} minutes. Here is all of it.\``
- `emptyWindow`: "Nothing was said in this time. If you paused, the lecture may have been quiet."  (replace if Backend can distinguish paused from silent: "The recording was paused for part of this time.")
- `windowGap`: (from, to) => `Some audio is missing from ${from} to ${to}.`

### M6. "Recap limit" and "catch-ups at once" copy leaks system mechanics and sounds like a reprimand
Refs: `copy.ts:218` (`budget`), `copy.ts:238` (`rate_limited`); UX rule 4; PRD M2-S5 AC5.
"The recap limit for this lecture has been reached" exposes cost mechanics and reads as the student's fault. "That is a lot of catch-ups at once" is mildly judgmental about using the feature, which is the opposite of the intent.
Replacement copy:
- `budget`: "Summaries are paused for this lecture. Here's what was said."
- `errors.rate_limited`: "One moment. Try again in about a minute. Nothing was lost."

### M7. Double announcement on a successful press, and silence while working
Refs: `useLostTrack.ts:92-93` (`status(catchUpReady)` plus focus moves to the heading `Result` `LostTrackPanel.tsx:71-76`); `LostTrackPanel.tsx:247-251` (`working` text is not announced); `Announcer.tsx` (single polite region, correct).
A screen reader hears "Here's where you are. What was said is ready." from the status region and then the focused heading "Here's where you are". That is the "announcement spam" UX rule 19 forbids. Conversely, pressing a window button gives no spoken feedback until the result arrives. (`aria-busy` on a group is not reliably announced.)
Fix: announce once when starting ("Getting what was said."), and when the result appears rely on the focus move to the heading, not an extra status. Keep `recapReady` only if the student has not already moved on (see S2).
Replacement copy:
- `LOST_TRACK_COPY.working`: "Getting what was said..." (keep; also send through `announcer.status` once per press)
- Remove `catchUpReady` status, or shorten to "Catch-up is ready." only when focus could not be moved.

## SHOULD-FIX

### S1. Result layout pushes TL;DR out of view
Refs: `LectureScreen.tsx:172-180`; `LostTrackPanel.tsx:257-270`; UX 5.5 ("always on screen in focus mode"). The inline result expands above TL;DR in the same column; with detail open TL;DR is pushed well below the fold. Fix with M1 (bounded scroll region or sheet). On phone, per UX 5.1 only one of transcript or recap fully expanded at once.

### S2. AI result swaps content under a reading student
Refs: `useLostTrack.ts:68-75` (`accept`), `LostTrackPanel.tsx:96-109`, `LostTrackPanel.tsx:195-204`. While the student reads the extractive text, the AI version replaces it in place, changing line breaks beneath their eyes and announcing "Recap ready." (UX rule 5, PRD M2-S4 AC2 spirit). Hypothesis: reflow during reading is costly for many ADHD readers.
Fix: keep the extractive view and show a small button "Recap is ready. Show it." that swaps on request (no focus move). Auto-swap only if the student has not scrolled or interacted (optional).
Copy: `recapReady`: "A short recap is ready." Button: "Show the recap". Pending notice: "A short recap is on its way. Here is what was said in the meantime." (replace `pending`). Slow: "The recap is taking a while. Here is what was said. You can keep reading or try again." Note `slow` currently has no retry action; the student can press a window button again, so say so.

### S3. Too much metadata in the default result
Refs: `LostTrackPanel.tsx:96-133`. Claims are unbounded (`claims.map`), each with a basis tag and up to 4 citation buttons at 44px (`Cites` slices 4), plus `confidence` and `gaps` above the fold. UX 5.6 says up to 3 bullets; PRD AC2 says <= 5 bullets, with timestamps.
Fix: cap default bullets at 3 (rest under "Show more detail"); show 1 citation per bullet (earliest) with the rest in detail; move `confidence` into detail; keep `gaps` visible only when non-empty and rename. Mapping order: UX asks (1) where you are, (2) what was covered, (3) right now. Current order is Right now first. Raise with Product: for a lost student "Right now" first may be better; if kept, the UX doc should be updated, not silently diverged.
Copy:
- `gaps` heading "Worth knowing" (ambiguous) -> "Parts that may be missing"
- `confidence`: (c) => `How sure we are: ${c}` with c shown as "not very", "fairly", "very" rather than raw enum values.
- `keyTerms`: "Key terms" (keep). `questions`: "Questions you could ask" (keep).

### S4. Demo label wording
Refs: `copy.ts:191-194`, `LostTrackPanel.tsx:12-19`, `TldrPanel.tsx:32`. Label is permanent and visible: good. "Made by a test routine, not an AI model." does not tell the student the consequence. It is rendered with `: ` glued to the tag.
Copy: tag "Demo summary"; note "Written by a test script, not an AI. It may not match what was said." Render as two lines or a single sentence ("Demo summary. Written by..."), not a colon splice.

### S5. TL;DR "Updated at" line is ambiguous jargon
Refs: `copy.ts:172`, `TldrPanel.tsx:58`. "Updated at 12:03 (revision 4)" - 12:03 is lecture time but reads like clock time; "revision" is internal. PRD M2-S4 AC1 requires time plus revision number, so keep both in plainer words.
Copy: `updated: (t, v) => \`Covers the lecture up to ${t}. Version ${v}.\``

### S6. TL;DR hold control is a confusing pressed toggle
Refs: `TldrPanel.tsx:73-91`, `copy.ts:173-176`. A button labelled "Hold updates" with `aria-pressed` leaves the state ambiguous ("is it holding?"), and the student gets no visible state text. UX 5.5 names "Pause updates" / "Catch up to latest".
Fix: swap the label instead of `aria-pressed` and show state text.
Copy: off-state "Pause updates"; on-state "Resume updates"; state text while held "Updates are paused. You are reading the summary from ${t}."; `newer` "A newer summary is ready."; `showLatest` "Show the newest summary" (this should also resume updates, or say it does not; today `showLatest` keeps hold on, which is a subtle inconsistency to confirm).

### S7. TL;DR source links are panel-level, not per bullet
Refs: `TldrPanel.tsx:59-67` (up to 5 `From mm:ss` buttons under the list); PRD M2-S4 AC3 ("Every TL;DR bullet traces to transcript timestamp range"). The student cannot tell which source belongs to which bullet, and 5 secondary buttons add visual noise. Needs a contract change (bullets are plain strings): QUESTION FOR LEAD ARCHITECT: can `summary.revision` carry per-bullet citations? Interim UI: collapse the buttons behind a "Where this came from" disclosure.
Copy: disclosure "Where this came from"; link text "From 03:12" (keep).

### S8. "Delayed" summary status uses the word "behind"
Refs: `copy.ts:197`. UX rule 4 forbids "behind" (it is about the system here, but the word is on the banned-feel list and the lint may flag it later).
Copy: `delayed`: "The summary is catching up. The transcript is not affected."; `failed`: "The summary could not update just now. The transcript is not affected." (PRD AC5 requires a "could not update" note); keep `asOf`: "Showing the summary as of 12:03."

### S9. Error and stale result shown together
Refs: `useLostTrack.ts:82-83`, `LostTrackPanel.tsx:252-270`. `start` clears `error` but not `result`, so after a failed second press the previous catch-up (for another window) sits under the new error with no label. Fix: label the old result "Earlier catch-up (last 5 min)" or dismiss it on a new press. Copy: `previousLabel`: (m) => `Earlier catch-up, last ${m} min`.

## NICE-TO-HAVE

- N1. Heading "TL;DR" reads poorly in some screen readers. Visible: "TL;DR: summary so far".
- N2. Escape closes the result and returns focus to the window group (UX 6 says closing returns focus to I Lost Track; today "Got it" goes to transcript, which matches UX 5.6, so keep that and add Escape to the I Lost Track button).
- N3. After a citation click the transcript takes focus and the recap stays; offer "Back to recap" (focus returns to result heading) so the student does not lose their place (supports interruption recovery, UX rule 7).
- N4. Preserve the open recap across reload (UX 7: "open recovery panel state restored"). Not implemented; decide if M2 scope.
- N5. Result card uses `border-2 border-slate-700`; a lighter treatment may reduce visual weight. Check against high contrast rather than removing contrast.
- N6. Optional thumbs "Was this helpful?" (PRD success metric >= 80%) must be opt-in, one tap, no pop-up. Not present; fine for now.
- N7. Show "Last [2][5][10] min" window group once, labelled with the heading, so the three buttons are not each read as a primary action (UX shows one dominant control). Consider one large "I lost track" button that reveals the three choices only if no usual window is set.

## Checks against requirements (summary)

| Item | Status | Ref |
|---|---|---|
| One press, no confirm | Meets | useLostTrack |
| Default 5, remembered | Gap | M3 |
| Always visible / sticky | Gap | M1 |
| Keyboard path (skip link, shortcut) | Gap | M2 |
| Short lecture, gap disclosure | Gap | M5 |
| Raw fallback on failure | Meets, but wall of text | M4 |
| 1-2 sentences + bullets <= 3/5 | Partly (unbounded bullets) | S3 |
| Timestamps | Meets for lost track; panel-level only for TL;DR | S7 |
| Recording not interrupted | Meets (no pause call) | |
| No judgmental copy | Mostly; fix two strings | M6 |
| TL;DR no focus steal / no reflow | Meets for focus; result swap reflow | S2 |
| TL;DR freeze/pin | Meets, label unclear | S6 |
| TL;DR failure note | Meets, wording | S8 |
| Quiet mode: nothing unsolicited | Meets | |
| Live regions: one status region | Meets, but double announcement | M7 |
| 44px+ targets, 320px | Likely meets (min-h 44-56; wrap) - not measured | |
| Reduced motion, forced colors | Meets in CSS; no animation present | |
| Demo labelling | Meets; wording | S4 |

## Open questions for the Lead Architect / Product Owner

1. Does the server cap `CatchUpPayload.text` length for the extractive view (M4)?
2. Can `catchup` and `summary.revision` payloads expose pauses/gaps in the window and per-bullet citations (M5, S7)?
3. Result order: "Right now" first (current) or after "what was covered" (UX doc)? Product call (S3).
4. Is the sticky/bottom-sheet layout in scope for M2, or deferred to M3 (M1)? Recommend M2: it is the core feature.

## Not tested

No render, no unit tests run, no axe, no NVDA/JAWS/VoiceOver/TalkBack, no device or real-student testing. Contrast and target sizes were judged from class names and `globals.css` only. Human validation per `human-research-coordinator` and `device-accessibility-validation` remains required.

## Next agents

frontend-engineer (M1-M7, S1-S9 implementation; copy strings in `copy.ts`), backend-engineer (M4, M5 payload), qa-engineer (tests for AC items in the table), human-research-coordinator (moderated session on the recovery flow), device-accessibility-validation (screen reader pass on M7 and focus behavior).
