# Student Experience: Dashboard, Course Creation, Live Lecture Workspace

Status: DESIGN PROPOSAL (no code executed or verified). Owner: adhd-ux-specialist. Version 0.1.

## 0. Assumptions, dependencies, open questions

Assumptions
- Web, responsive (phone, tablet, laptop). Phase 1 scope; planner and study mode only appear as placeholders.
- ADHD presents differently per person. Defaults are calm, and every prompt, reminder and density setting is student-adjustable.
- Research claims here are design hypotheses, not findings. Validate in usability testing.

Dependent teams
- Frontend: components and tokens. Backend/Speech: transcript event contract (partial/final, revisions). AI: TL;DR and answer length. Security: consent wording. QA: acceptance tests.

Open questions for Lead Architect / Product Owner
1. Is a recording-rules confirmation (university/local policy) shown once per course, or each session? Proposed: once per course, editable, plus a one-line reminder at each start.
2. Is raw audio retained? Proposed UI assumes transcript only, with no audio stored, until Security confirms.
3. Is the transcript chunk/event cadence sufficient for the 2/5/10-minute recovery window to be timestamp-based?

## 1. Cognitive-load rules

1. One primary action per screen, visually dominant. Everything else is secondary or tucked away.
2. Show at most 3 items in any "next" list, and 1 as the headline next step. "Show more" is always available and never auto-expands.
3. Chunks: sentences under about 20 words; bullets under 5 per group; summaries under 5 lines by default.
4. Plain language, active voice, present tense. No guilt words ("overdue", "behind", "failed", "you missed"). Use "Ready when you are", "Pick up here".
5. No auto-moving content outside the transcript pane. TL;DR updates in place, never reflows the page or steals focus.
6. Every state states what happened and the next action (no dead ends).
7. Nothing is lost on interruption: drafts (course form), the lecture session, scroll position and the open answer persist locally and server-side, and resume with "Welcome back. Pick up where you left off?" (dismissible).
8. No timers, streaks, badges, red alerts or countdown pressure. Color is never the only signal.
9. Notifications are off by default except safety-critical in-session status (recording, connection). Students choose reminders, timing and channel.
10. Never infer distraction from camera, mic content, mouse activity or inactivity. All support is student-initiated (I Lost Track) or opt-in scheduled prompts.
11. Defaults over decisions: forms prefill; optional fields collapsed.
12. Consistent placement: the same control is always in the same place across sessions.

## 2. Engagement modes (student-chosen, changeable mid-lecture, per-course default)

| Mode | Behavior |
|---|---|
| Quiet (default) | Transcript and TL;DR only. No suggestions, no sounds, no prompts. I Lost Track is always available. |
| Balanced | Quiet plus discreet clarification chips (max 1 visible, section 5.7) and an optional end-of-session recap offer. |
| Active | Balanced plus optional check-in questions the student can dismiss, at a cadence they set (e.g., every 10/15/20 min, or off). Never blocks content. |

Changing mode is a visible segmented control labeled "Help level", with plain descriptions. Mode never changes without the student's action.

## 3. Dashboard

Purpose: "What should I do next?" in under 5 seconds.

```
+--------------------------------------------------+
| College Companion            [Settings] [Account]|
+--------------------------------------------------+
| Good afternoon.                                  |
|                                                  |
| NEXT STEP                                        |
| +----------------------------------------------+ |
| | Biology 101 lecture                          | |
| | Ready to start when class begins.            | |
| | [ Start lecture ]      (secondary: Not now)  | |
| +----------------------------------------------+ |
|                                                  |
| YOUR COURSES                                     |
| [Biology 101] [Calculus II] [Psych 210]          |
| [ + Add a course ]                               |
| (shows up to 6 cards; "See all courses" beyond)  |
|                                                  |
| Later (collapsed): Planner · Study mode          |
+--------------------------------------------------+
```

Behavior
- Next step selection (deterministic, explainable): resume an in-progress session first; else the course the student pinned; else the most recently used course; else "Add your first course". A "Why this?" link states the rule. The student can change it.
- Empty state: one sentence plus one button ("Add your first course"). No tutorials carousel.
- Course cards: name, last session date, one action ("Open"). No counts of "unfinished" items.
- Planner/Study entries are collapsed and labeled "Coming later" until built. Never show fake data.
- Reminders area: absent unless the student enabled reminders.

## 4. Course creation

One screen, one required field.

```
Add a course
Course name *        [ Biology 101            ]
Optional (you can add later)  [v Show options]
   Instructor, term, meeting days/times, syllabus upload (later)
Recording rules
   [ ] I checked my school's and instructor's rules about
       recording or transcribing lectures. (What does this mean?)
Help level   (o) Quiet  ( ) Balanced  ( ) Active
[ Save course ]     [ Cancel ]
```

- Draft autosaves; leaving and returning restores it.
- The rules checkbox is not required to save a course, but it is required (as a once-per-course confirmation) before the first lecture start. Wording is Security's to approve. The "What does this mean?" text is plain language and says "We are not a legal advisor; ask your instructor if unsure."
- Inline validation after blur, never on each keystroke; error text next to the field with `aria-describedby`, and an error summary receives focus on failed submit.
- Success: focus moves to the new course page heading; status "Biology 101 added." Next action: "Start a lecture".

## 5. Live lecture workspace

Workspace tabs (course page): **Lecture Now** | Live TL;DR | Study Guide | Ask Claude. During a live lecture, Lecture Now is the default and TL;DR plus Ask are available as panels instead of separate navigation so the student does not lose the transcript.

### 5.1 Layout

Desktop / wide tablet (focus mode default):
```
+--------------------------------------------------------------+
| (REC) Recording · 00:42:10   [Pause] [Stop]   Help: Quiet v  |
+------------------------------+-------------------------------+
| TRANSCRIPT                   | TL;DR            (updated 1m) |
| ...older lines (muted)       | - Mitosis has 4 phases        |
| ...latest line (emphasis)    | - Prophase: chromosomes form  |
| [Jump to live]               | [ I LOST TRACK ]  big, fixed  |
|                              |  Last: (2) (5) (10) min       |
| Ask: [_______________]       | Answer area                   |
+------------------------------+-------------------------------+
```

Phone / narrow (single column, bottom sheet):
- Top: sticky status bar (recording state, Pause, Stop).
- Middle: TL;DR card (collapsible, 3 bullets by default), then transcript.
- Bottom: sticky large I Lost Track button; Ask opens a bottom sheet.
- Transcript and TL;DR never both fully expanded at once on phone; expanding one collapses the other (student can override).

Focus mode (toggle "Focus", default on during a session): hides tabs, dashboard chrome and non-essential controls; leaves status bar, transcript, TL;DR, I Lost Track, Ask. Exiting focus is one button, state preserved. Responsive: on narrow screens focus mode is the only layout.

### 5.2 Consent-first start

Pre-start screen (no mic access requested before the student acts):
```
Start Biology 101 lecture
We'll turn your microphone on and write what's said into a live transcript.
- You can pause or stop any time.
- A "Recording" indicator stays on screen the whole time.
- Only start if recording is allowed in this class.
[ ] I confirm recording is allowed here (shown only if not confirmed for this course)
[ Start recording ]   [ Not now ]
```
- Browser mic permission prompt appears only after "Start recording".
- Start is a deliberate button; no auto-start, no scheduled auto-start.
- Focus on pre-start heading when opened; on success focus moves to the status region heading "Recording" so context is announced, then Tab reaches controls in logical order.

### 5.3 Recording status (always visible, never color-only)

States: Not started, Requesting mic, Recording, Paused, Stopped, Mic blocked, Offline/reconnecting.
- Indicator = icon + text label + color, in a persistent top bar (sticky; never scrolls away, never hidden by focus mode).
- Pause: "Paused. Mic is off." Resume button replaces Pause (same position). Transcript shows a divider "Paused 10:14 to 10:20".
- Stop: opens a confirm dialog ("Stop and save this lecture? You can start a new one later.") with Stop (primary) / Keep recording. After stop: "Lecture saved. Notes are being built." with Next action "Review notes".
- Title also updates (document.title prefix "Recording") for tab-switchers.
- Closing the tab/leaving while recording: beforeunload warning plus mic stops on unload; session resumable as "Ended unexpectedly at 10:42. Review what we saved."

### 5.4 Streaming transcript

- Partial text appears in muted style and is replaced by final text in place (no line jumping; the line reserves height). Final lines get timestamps (mm:ss), shown subtly and available to screen readers.
- Autoscroll follows live only while the student is at the bottom. If they scroll up, autoscroll stops and a "Jump to live" button appears (no forced scrolling).
- Text size, line spacing and font are adjustable; default 16px minimum, line-height 1.5, max line length about 70 characters.
- Low-confidence words are not highlighted by default.

### 5.5 Persistent TL;DR

- Always on screen in focus mode, 3 bullets by default, with "Show more" for up to 7. Label "Updated 1 min ago" (text, not animated).
- Revisions swap content without animation by default; a subtle "Updated" text tag for 5s (not color-only; honors reduced motion). Option to "Pause updates" so the student can read in peace; "Catch up to latest" resumes.
- Sources: each bullet may reveal timestamp links to the transcript on request.

### 5.6 I Lost Track

- Largest control in the workspace, fixed position (bottom-right/bottom on phone; right panel top on desktop), high contrast, label "I lost track" (text, not icon-only), minimum 56x56 CSS px target.
- Flow: press the button. The window is the default last-used (5 min initially) and shown on the button group: `Last [2] [5] [10] min`. One press on a window runs recovery immediately; no extra confirmation. Keyboard shortcut: `Alt+L` (customizable; must not collide with screen reader keys, and is documented in a Help list).
- Response (short by default):
  1. "Here's where you are." One to two sentences.
  2. Up to 3 bullets: what was just covered.
  3. "Right now:" the current point in one sentence.
  4. `[ Show more detail ]` expands a fuller explanation with timestamp sources; `[ Got it, back to lecture ]` closes it and returns focus to the transcript.
- The transcript continues behind the panel; recovery does not pause recording. Panel is non-modal on desktop (side panel) and a bottom sheet on phone.
- If the lecture is shorter than the window: "You've only been here 3 minutes. Here's all of it."
- If the AI/network fails: show the raw last-N-minutes transcript excerpt, labeled "Summary isn't available right now. Here's what was said." (graceful degradation).
- Non-shaming copy only. No logging shown to the student as "times you lost track".

### 5.7 Ask and clarification suggestions

- Ask Claude input: always short by default ("Short answer" 1-3 sentences, then `Show more`). Detail expands in place; expansion state persists per answer.
- Answers cite transcript timestamps when they rely on the lecture; AI-estimated content is labeled "Based on the lecture" vs "General knowledge, not from your lecture".
- Discreet clarification suggestions (Balanced/Active only): a single small chip below the TL;DR such as "Unclear: 'osmosis' (at 12:03). Ask about it?" Max 1 visible, never animated, never takes focus, no sound, auto-dismisses silently after 2 minutes, dismiss with a visible `x`, and "Don't suggest this kind" setting. Quiet mode shows none.
- Typing in Ask never pauses the transcript. Draft question persists if the panel closes.

### 5.8 Denied-mic state

Triggered when permission is denied or no device.
```
Microphone is blocked
We can't hear the lecture yet. Nothing has been recorded.
How to turn it on:  [Browser steps for Chrome / Safari / Firefox]
[ Try again ]   [ Paste or type notes instead ]
```
- Role: status text in a focused region; focus lands on "Try again".
- Fallback: manual note entry still feeds TL;DR and Ask (flag "from typed notes").
- No scolding, no repeated prompts.

### 5.9 Offline / reconnecting state

- Banner (persistent, text + icon): "Connection lost. Still recording on your device; we'll catch up when you're back." Only claim local buffering if Speech/Frontend verify it. Otherwise: "Connection lost. Transcript is paused. Audio isn't being saved." Must reflect truth (open question for Speech Engineer).
- On reconnect: "Back online. Filled in 00:10 to 00:14." Missing portions marked as gaps in the transcript ("Gap 00:10 to 00:14") instead of silently omitted.
- I Lost Track offline: uses cached transcript if available; else states plainly it needs a connection, with a "Try again" and raw-transcript fallback.
- Retries use backoff without user spam; no repeated alerts after the first (see live-region rules).

## 6. WCAG 2.2 AA requirements (target)

Structure and landmarks
- Landmarks: `header`, `main`, `complementary` (TL;DR), `region` named (transcript). Single `h1` per view. Skip link "Skip to transcript" and "Skip to I lost track".
- Order in DOM matches visual order.

Live regions (avoid screen reader spam)
- Transcript container is NOT `aria-live` by default. Use `role="log"` with `aria-live="off"` by default; the student may opt in to "Announce new transcript lines" with a throttle (batch every 30s of final lines only; never partials).
- Status changes (recording, paused, offline, saved) use a single polite `role="status"` region; critical failures (mic lost mid-session) use `role="alert"` once.
- TL;DR: `aria-live="off"`; a short polite announcement "TL;DR updated" at most once per 60s, and only if the student enabled it. Otherwise the content is reachable by landmark.
- Suppress duplicate announcements; do not announce timestamps/elapsed time ticking. Elapsed time is visible text with `aria-hidden` on the ticker, readable on demand.
- Recovery response panel: on open, focus moves to its heading (`tabindex="-1"`); the content is read as the student navigates, not as a live dump.

Focus management
- Focus visible: 2px+ outline with 3:1 contrast, not obscured by sticky bars (SC 2.4.11/2.4.12; use scroll-padding).
- Opening Stop dialog: trap focus, Escape closes, focus returns to Stop. Closing recovery panel returns focus to I Lost Track (or the transcript if "Got it").
- No focus movement on transcript or TL;DR updates.
- Mode changes keep focus on the control.

Keyboard
- All functions operable without pointer; no keyboard traps. Documented shortcuts are single-modifier combos, remappable/disable-able (SC 2.1.4). Suggested: `Alt+L` Lost Track, `Alt+P` Pause/Resume, `Alt+J` Jump to live, `Alt+K` Ask. Verify against screen reader and browser conflicts.
- Segmented controls use radio-group semantics with arrow keys; 2/5/10 window is a radio group with `aria-label="How far back"`.
- Dragging is never required (SC 2.5.7); target size at least 44x44 (AA minimum 24x24, SC 2.5.8), primary controls 56px.

Visual
- Text contrast 4.5:1, UI components 3:1; high-contrast theme and dark theme; respects `prefers-contrast` and `forced-colors`.
- Reflow at 320px width and 400% zoom without horizontal scroll (no two-dimensional scrolling required for text). Text resizable to 200%.
- Reduced motion: honor `prefers-reduced-motion` and an in-app "Reduce motion" toggle; no animation required for state; no flashing; no auto-scrolling animation (instant jumps only).
- Don't rely on color; recording indicator includes text and shape.
- Consistent help location and no cognitive tests (SC 3.3.8 accessible authentication: no puzzle/transcription for sign-in; allow password managers). Redundant entry avoided in forms (3.3.7).
- Time limits: none imposed on the student; session timeout warns and offers extension and preserves work.
- Captions note: the transcript itself serves as the text alternative for the live lecture; live audio from the instructor is not ours to caption on their behalf, so this is documented as scope.

Forms and errors
- Visible labels, programmatic names, `autocomplete` attributes, errors identified in text, error summary focusable, suggestions for correction.

## 7. Saving and resuming

- Course draft: local autosave on change (debounced), cleared on save.
- Lecture session: session id stored server-side; refresh/reconnect reattaches to the session and shows "Welcome back." Unsent Ask draft and open recovery panel state restored.
- Never lose transcript on UI crashes: error boundary shows "Something went wrong in the display. Your transcript is safe." only if true; otherwise honest message.

## 8. Privacy and safety touchpoints (for Security review)

- Visible recording indicator; reversible pause/stop; no hidden capture.
- Delete and export reachable from the lecture page ("Delete this lecture's transcript").
- No camera access. No behavior or attention analytics from the UI. Engagement telemetry limited to aggregate, non-content, opt-in.
- Transcript not written to client logs by default.

## 9. Usability acceptance criteria

Each is checkable by QA with a script or moderated session.

Dashboard and course
1. New user sees exactly one primary button (Add your first course). Time to add a course with only a name is under 30 seconds for 90% of test participants.
2. Dashboard shows at most 1 next step plus at most 6 course cards and no unfinished-work counts.
3. Course draft survives a page refresh.
4. Form errors are announced, associated with fields, and the summary takes focus.

Lecture workspace
5. Mic permission prompt never appears before pressing "Start recording"; verified with a permission spy.
6. Recording indicator (text plus icon) is visible within every layout and state, including focus mode and 400% zoom.
7. Pause turns the mic off within 1s and the UI says so; Stop requires one confirm; no transcript is received after pause (backend test).
8. Partial-to-final replacement does not shift layout of lines above it, and does not move focus.
9. Scrolling up halts autoscroll; "Jump to live" appears and works via keyboard.
10. I Lost Track is reachable by one Tab-stop skip link and one shortcut; the response appears in under 5s at p90 (target to confirm with AI team), with a fallback raw excerpt within 1s if AI fails.
11. Recovery response default shows at most 1-2 sentences plus 3 bullets; "Show more detail" is keyboard operable and persisted.
12. 2, 5 and 10 minute windows each map to the correct transcript span (verified by fixture); shorter lectures handled with the plain message.
13. Quiet mode shows zero suggestions and zero prompts across a 60-minute fixture; Balanced shows at most 1 chip at a time.
14. Mode change mid-session takes effect without interrupting recording or moving focus.
15. Denied-mic state offers retry and typed-note fallback; no mic prompt loop; no console error leakage.
16. Offline: banner shown within 5s of disconnect; gap markers appear on reconnect; no claim of local buffering unless implemented.
17. TL;DR updates never change scroll position or focus; "Pause updates" holds content.

Accessibility
18. Automated axe run: zero serious/critical violations across all states; manual NVDA+Firefox/Chrome, JAWS+Chrome, VoiceOver+Safari (macOS and iOS), TalkBack passes the lecture flow.
19. A 30-minute screen-reader session with default settings produces no more than 1 announcement per status change and no transcript line announcements unless enabled.
20. Whole flow completable by keyboard only, with no trap, visible focus never hidden by sticky bars.
21. With `prefers-reduced-motion`, no non-essential animation occurs; with forced-colors and a high-contrast theme, all controls remain distinguishable.
22. Touch targets at least 44px for primary controls on phone; layout works at 320px.

Cognitive and tone
23. Copy review: no shaming language, sentences under about 20 words, reading level around grade 8 or below.
24. Moderated test with at least 5 students who identify as ADHD (varied attention patterns; recruiting approved by Product Owner): participants can state "what to do next" on the dashboard and recover context after an interruption without help. Treat results as design evidence, and iterate.

## 10. Deferred and handoffs

- Planner and study mode component notes: later increment.
- Next agents: frontend-engineer (components and tokens from sections 5 and 6), security-engineer (consent copy, retention), speech-engineer (offline buffering truth, event cadence), qa-engineer (turn section 9 into tests).
- Not tested: nothing here has been prototyped or user-tested.
