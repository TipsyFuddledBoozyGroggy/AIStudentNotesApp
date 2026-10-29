# Redesign review (against design-direction.md v1)

Status: DESIGN REVIEW, 2026-10-29. Author: adhd-ux-specialist. This is judgement from looking at 22 screenshots in `src/web/test-results/redesign-screens/` (paths below are relative to that folder) and reading `redesign-progress.md`. It is **not validation**: no keyboard, screen-reader, device or student testing was done by me. **G4 stays NOT_RUN**; G1 stays BLOCKED (consent wording is draft and I changed none).
Looked at: signin, dashboard, dashboard-empty, courses, course-new, course-detail, consent, lecture-live, lecture-lost-track, lecture-focus, lecture-paused, lecture-disconnected, lecture-mismatch, lecture-stopped, lecture-mic-denied, preferences, palette; at 390 and 1280; in all four looks at least for lecture-live, sign-in, dashboard (paper, night, fog, dusk sampled across screens). Not opened: ui-kit, lecture-focus at 390 paper/dusk, the remaining permutations (assume same tokens).
Caveat on the evidence: the lecture screenshots are the dev harness (synthetic data, harness controls strip, "Fixture:" caption). Time reads 00:00 throughout. The `lecture-live` fixture shows no I lost track control at all (see Question A).

## Overall verdict
Yes on "sleek, serious-academic, not in the way" for the non-lecture screens: paper background, serif headings, one ink-blue fill per screen, hairline lists, no badges or gamification. Sign-in, dashboard, course detail and mic-blocked are calm and genuinely good. Night desk and Dusk are comfortable, not cold; Fog is the least distinct from Paper (a quiet grey-warm, fine, but it is a small change; that is acceptable for a look whose job is "less").
The live lecture screen is **good on desktop, too heavy on phone and in safety states**. The recording state is obvious and not alarming; the one filled element is the right one (the usual I lost track window). The problems are vertical space on phones, a few leftover rough edges (a stray "!", orphaned citation chips, two exits on Stopped), and a WCAG A-level label problem the engineer already found.

## Must fix

**M1. Catch-up result on phone hides its own exit and the lecture.**
Screens: `lecture-lost-track--390--night--viewport.png`, `lecture-lost-track--390--fog--viewport.png`. Sheet starts at about y=255 of 844 and covers about 70% of the view; "Got it, back to lecture" is cut off at the bottom edge, and only one transcript line shows. Why it matters: an ADHD student who just regained the thread has to scroll inside a sheet to dismiss it, while the lecture keeps going; the moment of re-entry should have one visible, cheap way out (Principle 5, return without penalty). Fix: put a visible text button "Back to lecture" in the sheet's top row (right of the "Here's where you are" heading, 44px, same handler as "Got it"), keep the bottom one, and cap the sheet at `max-height: 60dvh` with its own scroll so at least 3 transcript lines stay visible. Same for the known desktop limitation (long result scrolls inside the sticky card): put the close control in the card header there too.

**M2. WCAG 2.5.3 Label in Name (Level A) on I lost track window buttons and citations.** (engineer escalated; I rule here.)
Screens: `lecture-lost-track--1280--paper.png` (buttons "Last 5 min (your usual)", chips "00:09"). Why: voice-control users (also students with RSI or motor needs) say what they see. Fix, and **I approve changing the frozen names**: visible text "Last 2 minutes / Last 5 minutes (your usual) / Last 10 minutes" (spell out "minutes"; "min" is jargon-lite and not read the same everywhere), accessible names start with the visible text: "Last 5 minutes, your usual, catch up". Citation chips: visible "00:09", name "00:09, go to transcript line" (not "From 0 minutes 9 seconds..."). Update the frozen list in design-direction.md section 8, the unit tests and e2e selectors in the same PR; tell qa-engineer. On phone three buttons must still fit: keep "Last 2 min" visible wording only if it can be a different, shorter pair; otherwise allow two lines (already wraps today).

**M3. Stray "!" in the mismatch heading.**
Screen: `lecture-mismatch--1280--paper.png`: heading renders as "! Something doesn't match" next to a warning triangle. It breaks the no-exclamation voice rule and doubles the icon. Fix: remove the text glyph; keep the triangle (decorative, `aria-hidden`). Check the same pattern in other panels.

**M4. Phone safety state pushes the transcript below the fold.**
Screen: `lecture-disconnected--390--night.png`. Bar is four stacked rows (pill; "Not connected"; Pause / Stop / "Not available right now"; Lecture settings; Demo tag) about 290px, then two notice panels and the TL;DR before the transcript starts near y=770 of 844. "Trying to reconnect. Trying again..." repeats itself. The status is honest, but the student sees almost no content. Fix: (a) on phones drop the "Demo source" tag from the bar (the full Demo notice already says it; keep the tag on desktop), (b) put the connection word on the pill row, (c) show "Not available right now" as a small line under the buttons only when disabled, or as the aria-disabled button's `title`/description, not inline, (d) when a safety panel is open, collapse the Demo notice to its heading plus "Details" disclosure (the Demo label must stay visible; only the body collapses), (e) fix the duplicate "Trying..." copy. Target: pill, controls and first transcript line visible at 390x844 in every state.

**M5. Phone sticky bar is too tall in the normal state.**
Screen: `lecture-live--390--paper.png` / `...--fog--viewport.png`. Three rows (about 150px, 18% of the screen) are fixed above the transcript, plus the dock below. Same fix as M4(a): remove the Demo tag from the sticky area on phones and put Lecture settings into the second row beside Stop as a quiet text button "Settings". Target: bar at most 2 rows, at most 112px.

## Should fix

**S1. Citation chips look cluttered and orphan onto their own lines.**
Screens: `lecture-lost-track--1280--dusk.png`, `--390--night--viewport.png`. Each bullet and the "Right now" sentence end with a bordered box; at 1280 the chip wraps alone under its bullet, so one three-line result shows five boxes. Fix: render citations as inline link-style timestamps (underlined, tabular, accent colour), placed at the start of the line ("00:02  Power rule: bring the exponent down.") in a hanging grid like the transcript; keep a 44px hit area with padding (`min-height:44px` as inline-flex, negative margin to avoid growing the row). This also matches the transcript's timestamp column.

**S2. Stopped state has two exits and a dead control.**
Screen: `lecture-stopped--1280--night.png`. "Leave" in the bar and "Back to course" in the panel do the same thing; "Pause updates" still shows on a finished lecture. Fix: keep the panel button "Back to course" (primary-styled secondary), drop "Leave" when the panel is showing; hide "Pause updates" and Focus when Stopped (nothing is live). Add a plain line "Notes will appear here once this feature is built" only if a notes route exists; otherwise nothing (no teasers).

**S3. Mic-blocked browser list has no visible expand affordance.**
Screens: `lecture-mic-denied--1280--paper.png`, `--390--dusk.png`. "Chrome / Safari / Firefox" read as headings, not buttons. Fix: show the chevron the Disclosure primitive uses elsewhere and the label "How to turn it on in Chrome". Everything else on this screen is exactly right.

**S4. Palette command "Start screen for Biology 101" is ambiguous.**
Screen: `palette--1280--night.png`. "Start screen" can read as "start recording" (invariant 2). Fix: "Open the lecture screen for Biology 101 (asks first)"; keep the "Lecture" tag. It already does not start recording; the words should say so.

**S5. Dashboard "Next step" claims more than it knows, and "How a lecture works" never goes away.**
Screens: `dashboard--1280--paper.png`, `dashboard--390--dusk.png`. The card names Calculus II (the first course) as the "Next step" and duplicates the first list row; the explainer takes a third of the page on every visit. Fix: for returning users the card title is "Start a lecture" with the course picked by most recent lecture, else show no card and let the course list be the primary; wrap "How a lecture works" in a Disclosure that is open only until the first lecture was started, then closed (remember per device). Empty state (`dashboard-empty--390--paper.png`) is good as is.

**S6. Consent page: the demo bullet is last, the first sentence says the opposite.**
Screens: `consent--1280--paper.png`, `consent--390--fog.png`. "We turn on your microphone and write what is said..." then, six bullets later, "while Tether runs on its demo source, the transcript is a fixed test script". A student can tick the box having misread what will happen. "Draft:" repeats four times plus the Draft tag, which is noisy. Fix (presentation only, meaning unchanged, counsel-reviewed text stays word for word): when `source=mock`, show the demo bullet first, inside the same list, with the Demo tag; show the single "Draft" tag at the top and let the per-bullet "Draft:" prefixes stay (do not edit them without legal-compliance-reviewer). Raise with Lead as Question B before any wording moves.

**S7. Preferences surface duplication and placement (see rulings below).** Screen: `preferences--1280--dusk.png`: the panel pushes the dashboard down about 1,000px, so changing Theme or Text size shows the effect only after scrolling away from the control.

**S8. Elapsed time ticks every second in the pill.**
Screens: any `lecture-live`. My own spec put elapsed in the pill; I now think a seconds counter is a movement cue in the corner of the eye (design hypothesis, untested), against "nothing moves" (Principle 3). Fix: default to minutes ("12 min") with a Preferences switch "Show seconds"; keep the `elapsed` test id; a unit test update is justified only for the displayed format.

## Nice to have

- N1. Selected I lost track button shows an underline on the label in `lecture-lost-track--1280--paper.png` / `--dusk.png` (hover or focus state captured). Underline on a filled button reads as a link; keep the underline for hover on text links only, use the focus ring for buttons.
- N2. "Right now:" sentence has very loose line spacing when a chip sits inline (`lecture-lost-track--1280--paper.png`); with S1 fixed, set line-height 1.45 there.
- N3. Sign-in shows eyebrow "TETHER" under a wordmark that already says Tether (`signin--1280--night.png`). Drop the eyebrow.
- N4. Consent and course pages keep the left rail and Search button; on consent (a decision page) hide the rail for fewer exits. Low priority.
- N5. Two back links on course detail ("← Courses", "Back to dashboard") and on All courses (`courses--390--fog.png`). Frozen; remove one when e2e is updated.
- N6. Fog primary buttons (slate) are subtle next to Paper's indigo (`course-detail--1280--fog.png`). Accepted by design (quiet look); ask students (Q4).
- N7. Dashboard row meta "No lectures yet" repeats on every row; show it once or only when there are lectures to count.
- N8. Palette scrim in Night desk is faint so page text peeks around the dialog (`palette--1280--night.png`); darken the scrim to about .65 in dark looks.

## Checks against the brief

- **One clear primary action in a lecture:** yes when I lost track is present (only filled element, `lecture-lost-track--1280--paper.png`); in Paused the only filled element is "Resume recording" (`lecture-paused--1280--paper.png`); mismatch fills "Check again". Correct per region.
- **Recording status obvious, not alarming:** pill is shape + word + time, red text on a pale tint, never animated; Paused is amber, Not recording is neutral; honest in the disconnected state ("Not recording" plus "Audio is not being saved"). Keep.
- **TL;DR / transcript hierarchy:** desktop reads transcript first (left, large) and TL;DR as a calm card (right). Phone puts a collapsed one-liner above the transcript (`lecture-live--390--paper.png`): good, but it sits below a 190px Demo notice, so the first screen is mostly boilerplate (see M4/M5).
- **Tone rules:** copy mostly passes (no exclamation marks except M3, no shaming words). Good lines: "Nothing has been recorded.", "Your microphone is off. Nothing is being heard or written.", "How far back? The lecture keeps going." Weak lines: "Reload the state to continue" (mismatch), "Trying to reconnect. Trying again..." (disconnected).
- **Demo-source labelling:** present and unmissable: bar tag + notice + "Demo summary" tag in the catch-up result (`lecture-lost-track--1280--paper.png`). Never dismissible. Keep; compress, do not remove.
- **Empty and error states:** dashboard-empty is a model (one sentence, one action). Mic-blocked is calm and tells whether anything was recorded. Not inspected: loading and not-found.
- **Mobile touch targets:** buttons look at least 44px; I lost track window buttons look 56-64px as specified. Not measured by me (axe reports 0 violations per the engineer; I did not run it).
- **Dark and low-stimulation looks:** Night and Dusk keep hierarchy and the recording colour (salmon) reads without glare. Dusk's periwinkle fill with dark text is fine. No problems seen.
- **Consistency across screens:** one button style, one card style, one heading voice, same top bar placement. Inconsistency: "Lecture settings" (lecture) vs "Preferences" (everywhere else), link-styled in the lecture bar while the neighbours are bordered buttons.

## Rulings on the engineer's deviations

1. **Inline Preferences panel (no side panel, no `/preferences` route): ACCEPT for this demo, CHANGE next.** The in-flow panel works but moves the page it is changing. Next: from 1024px a right-side non-modal panel (fixed, 360px, content stays visible); on phone keep it in-flow for now. A `/preferences` route is nice (linkable, restores on reload) but needs Lead approval for routing; not blocking.
2. **Preference changes not announced: CHANGE (small).** The engineer is right that the shell must not add a live region on the lecture screen. Fix without breaking the "three regions" rule: render a `role="status"` line **inside the open Preferences panel only** (not mounted when closed, and the panel is already hidden on lecture routes), one plain message per change ("Dark theme on."), no timers, identical text within 10 s dropped. Add a test that a lecture screen still has exactly three live regions. Also show the same sentence as visible text under the control so sighted users get confirmation.
3. **Focus toggle only from 1024px: ACCEPT.** Phones are already single column with a collapsed TL;DR; a toggle that changes nothing would be clutter. Add one line in Lecture settings on phones: "Focus mode is for wider screens. On a phone the layout is already one column." so the missing control is explained.
4. **"Lecture settings" name kept: ACCEPT now, rename later.** The e2e harness clicks that text. Later (with M2's test pass) rename to "Preferences" so one concept has one name (WCAG 3.2.6 consistent help), and make it a bordered secondary button in the bar like its neighbours.
5. **Copy examples 6-8: APPLY, with test edits.** A test that pins a sentence is testing copy, so editing it alongside the copy is legitimate, provided no behaviour assertion changes and no frozen test id or button name changes. Specifically:
   - Example 6 (`LostTrackPanel.test.tsx`, "taking longer than expected"): change the expected string to "Still writing your recap. Here is what was said so far." Justified: present tense, and tells the student something is happening.
   - Example 7 (budget): **adjust my own wording.** "A recap isn't available for this lecture" claims permanence; if a budget pause can lift, it is untrue. Use "Recaps are paused for this lecture. Here is what was said." (keeps "paused", drops the contraction "Here's"). Ask backend-engineer/ai-agent-engineer whether the pause can end (Question D). Update the pinned "Summaries are paused for this lecture." assertion only.
   - Example 8 (mismatch): apply as written but keep the button name "Check again": "The app and your microphone disagree, so the microphone is off. Nothing is being recorded. Choose Check again." Update the pinned "We turned the microphone off to be safe" assertion. Justified: "Reload the state to continue" is jargon and contradicts the actual button.
   Re-run `copy.test.ts` (the lint) after the change.

## What is genuinely good (do not change)

- The paper/ink palette and serif headings; sign-in, dashboard hierarchy, course detail (`signin--390--paper.png`, `dashboard--1280--paper.png`, `course-detail--1280--fog.png`).
- One filled button per region, including making "Resume recording" the only fill when Paused (`lecture-paused--1280--paper.png`).
- The recording pill: shape + word + time, static, red only for recording, amber for Paused, neutral for Not recording (all four looks).
- Safety panels sit directly under the bar and say what is true ("Your microphone is off. Nothing is being heard or written.").
- Hanging-timestamp transcript at readable measure, gap and pause markers as quiet hairline dividers (`lecture-live--1280--paper.png`).
- Mic-blocked screen, dashboard empty state, "Add a course" form with "Only the name is needed. Your answers are saved on this device as you type." (`course-new--390--paper.png`).
- Command palette is navigation only and the active row has a bar + bold, not tint alone.
- Preferences wording: "Softer colours, no shadows, no movement. Recording status stays just as clear."; the live sample sentence; On/Off words beside switches (`preferences--1280--dusk.png`).
- Roadmap moved out of the dashboard into Preferences > About; no "coming later" teasers.
- The engineer's honesty: found my Dusk `--c-muted` omission, stopped at the label-in-name conflict, recorded the lecture e2e gap rather than claiming a pass.

## Questions

For the Lead / team:
- A. Why does the `lecture-live` fixture show no I lost track control? If it is absent when the AI service is unavailable, the student needs a visible reason in its place ("Catch-up is not available right now"), because the control is the screen's main promise.
- B. May the consent bullets be reordered (demo bullet first) without legal re-review? I think yes (order, not meaning); counsel should confirm.
- C. Leave-while-recording: confirm that "Leave" hidden during Active/Paused is intentional; students will try the browser back button. What happens then?
- D. Can the budget pause for summaries end during a lecture (affects example 7)?

For real students (via human-research-coordinator; vary attention patterns, include students who dislike all of this):
1. On a phone, with the catch-up result open, can you tell at a glance how to get back to the lecture, and do you still want to see the live transcript behind it?
2. Is a ticking seconds counter in the Recording pill calming, neutral or distracting? Would you rather see minutes?
3. Is "Last 5 minutes (your usual)" clear, or would you want to pick the usual in the moment only?
4. Does Fog feel meaningfully calmer than Paper, or the same? Does Dusk feel better or worse than Night desk after 20 minutes?
5. Does the Demo source notice feel honest or like noise? Would one line plus "Details" be enough?
6. Do citation timestamps help you check the summary, or do you never use them?
7. Does a "Next step" card on the dashboard help you start, or feel like it is telling you what to do?
8. When the connection drops, what do you need on screen first: reassurance, the status, or the transcript?

## Summary
1. Non-lecture screens and the mic-blocked/paused states are calm, serious and on-brief; keep them.
2. Must fix: phone catch-up sheet hides its exit (M1), label-in-name Level A (M2, I approve renaming the frozen names), stray "!" (M3), phone bar and disconnected state too tall (M4, M5).
3. Should fix: citation chip clutter, Stopped duplicates, mic-blocked affordance, palette "Start screen" wording, dashboard next-step honesty, consent bullet order, seconds ticking.
4. Rulings: accept inline Preferences and Focus >= 1024 now; add an in-panel status line for preference changes; keep "Lecture settings" until a later rename; apply copy 6, 8 as written and 7 as "Recaps are paused" with the matching test edits.
5. All of this is design judgement from screenshots; G4 NOT_RUN, G1 BLOCKED, no student, device or screen-reader evidence exists.
