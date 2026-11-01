# M2 Live Lecture Screens and States

Status: DESIGN PROPOSAL v0.1, 2026-10-27. Owner: neurodivergent-ux-specialist. Audience: frontend-engineer. Nothing here is prototyped, built or tested.

**Validation status (gate G4: NOT HUMAN-VALIDATED).** Layouts, copy, thresholds and defaults below are design hypotheses. No claim is made that they help neurodivergent students. Treat every behavior as changeable after moderated testing with students and expert review. **Consent and recording copy is DRAFT pending G1 (BLOCKED)**; it is placeholder text for the mock-backed build, not legal wording, and must not be shown to real students.

Sources: `docs/API_CONTRACTS.md` (frozen v1: §3 events, §9 pause UX, §10 indicator and unattended limits, §11 wire format), `docs/ux/student-experience.md` (rules and base layout; this file refines its §5 for M2), `docs/PRODUCT_REQUIREMENTS.md` §5. Where this file is silent, student-experience.md applies. Numeric thresholds quoted from the contract are proposals, configurable, not measured.

Assumptions and dependencies
- M2 runs on `ReplaySttProvider` and `MockAi` only. UI must not imply real recording of a class. A visible, non-dismissible "Demo source: replayed test audio" tag is shown in the status bar while the replay provider is active (Lead Architect to confirm the flag source; suggested: `GET /sessions/{id}` extension or build config).
- Open question for Lead Architect: the contract has no event that says "auto-paused" distinct from user pause. This doc relies on `session.state.payload.reason` being a free string; frontend needs fixed reason values (proposed: `user`, `auto_pause_no_audio`, `auto_stop_paused_timeout`, `hard_cap`, `abandoned`). Request additive clarification of `reason` values; until then, treat unknown reasons as "ended by the system".
- Quiet mode is the only mode built in M2 (PRD §5 M2-S8). The Help level control is not shown in M2 beyond a read-only "Help: Quiet" label.

## 0. Shared rules (apply to every screen)

State source of truth
- Visible recording state = f(`session.state` from hub, local `MediaStreamTrack.readyState/enabled`, hub connection). Never derived from a button press alone (contract §9, C-16).
- Pause/Stop: stop the local track first, then render Paused/Stopped. Between press and confirmation show "Pausing..." (no countdown) for at most 3 s; if REST fails, show the error banner and keep showing the true state.

Persistent status bar (all states, all layouts, including focus mode and 400% zoom)
- Sticky top; contains: state icon + text label, elapsed active time (visible text, `aria-hidden`), connection text, Pause/Resume, Stop. Never color-only. Shapes: Recording = filled circle + "Recording"; Paused = two bars + "Paused"; Not saving = outlined circle with slash + "Not recording"; Stopped = square + "Stopped".
- `document.title` prefix: "Recording | ", "Paused | ", "Not recording | " (title updates are not announced).

Live-region architecture (exactly three regions, mounted once at page load, never re-created)
1. `status-region`: `role="status"` (polite, atomic). Recording state changes, saved, reconnected, summary Delayed/Failed, catch-up ready, gap filled.
2. `alert-region`: `role="alert"`. Only: auto-pause, auto-stop, mic lost mid-session, mic/server mismatch, hub disconnect while Active. Each message fires once per episode; repeats of the same condition are suppressed until it clears.
3. `finals-region`: inside transcript `role="log"`, `aria-live="off"` by default. Student-enabled "Announce new transcript lines" switches it to polite and batches finals at most once per 30 s (text: "N new lines", then the lines, max 5 lines; remainder available by navigation). Default off. Never announce `transcript.partial` or `transcript.correction` text; never announce timestamps, elapsed time or `gap` markers inline (gaps are announced through status-region once, see §7).
- Message queue: one announcement per 2 s minimum spacing; alert preempts status; identical text within 10 s is dropped.
- TL;DR panel and catch-up panel are `aria-live="off"`. Optional (default off) "Announce TL;DR updates" = one polite "TL;DR updated" at most per 60 s.
- No update to transcript, TL;DR or banners may move focus or change scroll position.

Reduced motion (all screens)
- Honor `prefers-reduced-motion` and the in-app Reduce motion toggle. No animation is required to understand any state. Default behavior is already non-animated: no pulsing REC dot, no sliding panels, no smooth scrolling, "Updated" tags appear and disappear instantly. Scroll jumps are instant.

Touch and size
- Primary controls (Pause, Resume, Stop, Quick recap, Start recording, Try again) at least 48x48 CSS px (Quick recap 56). All others at least 44x44 where feasible (AA floor 24x24, SC 2.5.8). No drag-only interactions (SC 2.5.7).

Shortcuts (SC 2.1.4: remappable and disable-able; none single-key)
- `Alt+L` Quick recap, `Alt+P` Pause/Resume, `Alt+J` Jump to live. Listed under a "Keyboard help" button in the status bar. Pending conflict check with NVDA/JAWS/VoiceOver (device-accessibility-validation). Alt+P only works when the control is enabled; it never starts a first recording.

Copy rules: sentences under about 20 words, no guilt words, say what is true now plus the next action. All strings below are DRAFT.

---

## 1. Consent and start flow

Purpose: knowingly start capture. No mic access, no hub join and no audio ticket before "Start recording" is pressed.

Layout (single column, max width ~640px, no sidebar)
```
h1  Start lecture: Biology 101
    [DRAFT tag] Draft wording, not reviewed by counsel or your school.
p   We turn on your microphone and write what is said into a transcript.
ul  - A "Recording" indicator stays on screen the whole time.
    - You can pause or stop whenever you like.
    - Audio is not kept. The transcript is kept until you delete it. (DRAFT: retention per D2)
    - [DRAFT] Text is processed by a speech service and an AI service. (named providers inserted by G1/G2 outcome)
    - Deleting a lecture here does not remove copies already held by those services or by Google. (C-21 disclosure)
cb  [ ] I checked that recording and transcribing is allowed in this class.
    (What does this mean?)  -> disclosure: "Tether is not a legal advisor. Ask your instructor if you are unsure."
    [ Start recording ]   [ Not now ]
```
- Checkbox is unchecked by default and stays unchecked across visits unless the course-level attestation exists (SEC-008); then show it checked with text "You confirmed this for Biology 101 on <date>. Change" and a one-line reminder. The per-start consent still posts `consent:{acknowledged:true, textVersion}`.
- "Start recording" is disabled (`aria-disabled`, still focusable, with `aria-describedby` hint "Check the box above to continue") until the box is checked. Do not use the native `disabled` attribute so the hint stays reachable.
- Sequence on press: POST `/courses/{id}/sessions` -> state "Starting" -> `getUserMedia` -> POST `audio-ticket` -> open audio socket -> hub `Join`. Browser prompt appears only after the button press (permission spy test).
- Errors: `403 consent_required` -> "We need your confirmation before starting." focus to checkbox. `409 invalid_state` -> "You already have a lecture running. Open it or stop it first." with link. Network error -> inline retry, form state kept.

Keyboard order: heading (focus on open) -> DRAFT notice -> "What does this mean?" -> checkbox -> Start recording -> Not now. Enter/Space activate. Escape does nothing.

Screen reader: focus lands on h1. Checkbox label is the full sentence. On success focus moves to the status-bar heading "Recording" (`h2`, `tabindex=-1`) and status-region says "Recording started."

Acceptance criteria
1. A spy confirms zero `getUserMedia` calls, zero hub connections and zero audio-ticket requests before Start is pressed with the box checked.
2. Pressing Start with the box unchecked does nothing except move focus to the checkbox hint.
3. The DRAFT tag is present on every render of the consent text and contains the text "Draft".
4. `textVersion` sent equals the value in the server registry; unknown version -> `consent_required` handled (AC 403 path above).
5. Keyboard-only completion of the flow; focus order matches DOM; axe has no serious/critical issue.
6. Reflow at 320px with no horizontal scroll.

---

## 2. Live view (Active)

Layout: as `student-experience.md` §5.1 (desktop two columns, phone single column with sticky bottom Quick recap), with these M2 specifics.

```
[Status bar]  (o) Recording  00:42  Connected    [Pause] [Stop]   Help: Quiet
[Transcript region, role=log]        | [TL;DR panel, complementary]
  seg lines, timestamps mm:ss        |   TL;DR (3 bullets, Show more)
  muted partial line (reserved ht)   |   "Updated at 00:41, revision 7"
  Gap markers                        |   [Hold updates]
  [Jump to live] (when scrolled up)  |  [ QUICK RECAP ]  Last: (2)(5)(10) min
                                     |  Catch-up result area
```
- Transcript: finals append; the partial line is one reserved-height row in muted style, replaced in place by the final with the same `segmentId` (no reflow above). Corrections replace text in place and add a visually subtle "edited" text tag with `title`-independent accessible name (keeps original in data).
- Dedupe on `(sessionId, seq)`; ignore `summary.revision` with `revision <= current`.
- TL;DR: shows `tldr` plus up to 3 bullets (Show more up to 7), rendered as plain text nodes only (C-28; no markdown, links or HTML). "Updated at mm:ss (revision n)". Citations are optional timestamp buttons ("From 12:03") that scroll the transcript instantly and move focus to the target line.
- Hold updates: toggle button (`aria-pressed`); while on, new revisions are buffered and a text label "Newer summary available" appears; "Show latest" applies. Default off.
- Elapsed time counts active time only (contract §3 timecodes exclude paused time).

Keyboard order (DOM = visual): skip links (Skip to transcript, Skip to Quick recap) -> status bar (Pause/Resume, Stop, Help label, Focus toggle) -> transcript region (scrollable, `tabindex=0`, labeled "Transcript") -> Jump to live (if shown) -> TL;DR panel (Hold updates, Show more, citations) -> Quick recap window radios -> Quick recap button -> Ask (post-M2, placeholder hidden). Arrow keys scroll the transcript when focused; `End` jumps to live.

Screen reader: landmarks `header` (status bar), `main`, `region` "Transcript", `complementary` "TL;DR". Each final is an `article`-less paragraph with a visually-subtle but programmatic timestamp ("12 minutes 3 seconds").

Acceptance criteria
1. Replaying a 30 s fixture with partials and finals produces zero live-region mutations in status/alert regions and none in the transcript when announce is off.
2. Partial-to-final replacement changes the `getBoundingClientRect().top` of lines above by 0 px.
3. Scrolling up stops autoscroll and shows Jump to live; `Alt+J` and the button both resume it; focus is unchanged by incoming events.
4. A TL;DR revision with markdown/HTML/URL payload (red-team fixture) renders as inert literal text.
5. A TL;DR revision never changes scroll position or `document.activeElement`.
6. Status bar is visible and not clipped at 320px and at 400% zoom; sticky bars do not hide focused elements (scroll-padding; SC 2.4.11).
7. With reduced motion on, no CSS animation or transition runs (computed style test).

---

## 3. Paused state

PO direction (contract §9): clear "recording is paused" state, Resume button, plus Stop.

Recommendation: a **persistent in-page panel, not a blocking dialog.**
Reasons (hypotheses, unvalidated):
1. Pause is a state the student is in, not a question they must answer; a modal implies urgency and is dismissible, which would hide the state. A panel cannot be dismissed and stays true for as long as the state lasts.
2. Students may want to read the existing transcript and TL;DR or use Quick recap while paused; a modal blocks that and the focus trap adds cost for keyboard and screen-reader users.
3. Modal-on-pause plus modal-on-stop would stack dialogs and increase interruption.
4. Persistence matters for safety: a quiet panel plus the status bar and the title prefix keep the "not recording" fact visible after the student switches tabs or returns after an interruption.

Layout: the panel sits directly under the status bar, full width, above the transcript (seen first at every breakpoint; it pushes content down rather than overlaying it).
```
+--------------------------------------------------------------+
| [||] Recording is paused                                      |
| Your microphone is off. Nothing is being heard or written.    |   (DRAFT)
| [ Resume recording ]   [ Stop lecture ]                       |
| Paused since 10:14 · Will stop on its own after 30 min paused |
+--------------------------------------------------------------+
```
- The status-bar Pause button becomes "Resume" in the same position (rule 12). The panel duplicates Resume and Stop so the dominant action is one large Resume button. Primary = Resume; Stop secondary. Panel text is a single heading plus two sentences; do not add the clause about audio already sent until G1 supplies wording (placeholder slot `[G1: wording about audio sent just before pause]`, hidden in build until provided).
- Transcript shows an inline divider "Paused 10:14 to now" (updates to "Paused 10:14 to 10:20" on resume). Divider is static text.
- Mic indicator: the local track is stopped. The Mic state line says "Microphone off" only after the track `ended/enabled=false` is observed (C-16). If the track is still live while server says Paused, see §8.
- Resume: POST `/resume` then new `audio-ticket` (old ticket invalid; ring buffer never replayed, C-14), reacquire mic if the track was released. Failure states reuse §5 (mic) and §6 (offline). Resume is never automatic (not on focus, tab return or reconnect).
- Stop: confirm dialog (the only modal in this flow) "Stop and save this lecture?" -> Stop (primary) / Keep paused. Escape = Keep paused. Focus returns to Stop on cancel.

Focus and screen reader
- Student pressed Pause (button or `Alt+P`): after the server confirms and the track is stopped, move focus to the "Resume recording" button in the panel (the pressed control was replaced, so focus would otherwise be lost). Announce via status-region: "Recording is paused. Microphone is off." Do not also announce the panel heading (panel heading is not live).
- Pause caused by the system (auto-pause, §9): do not move focus (the student may be typing elsewhere); announce through alert-region once (see §9).
- Resume: focus goes to the status-bar Pause button; status-region says "Recording resumed." Panel is removed from DOM.
- Panel markup: `section` with `aria-labelledby` its h2 (not `role=dialog`, not `role=alert`, not live). Reachable via a landmark and by Tab as the first content after the status bar.
- Reload while Paused restores the panel from `GET /sessions/{id}`.

Acceptance criteria
1. In `Paused`, the text "Recording is paused" is visible within every breakpoint and in forced-colors mode, with a Resume and a Stop button, without scrolling at 320px/400% zoom.
2. The local audio track is stopped (`readyState === "ended"` or released) before the Paused panel renders; test with a stubbed track that stays live -> mismatch warning (§8), not the panel.
3. After Pause via keyboard, `document.activeElement` is the Resume button in the panel; after Resume, it is the status-bar Pause button.
4. Exactly one status announcement per pause and one per resume; no announcements on transcript or timer.
5. No `transcript.final` is rendered or requested after pause (fixture: events with `startMs` beyond the pause offset are not produced; UI ignores any received while Paused and shows the §8 warning if it detects it).
6. The panel cannot be dismissed, persists across tab switch and reload, and Resume requires a click/Enter (no auto-resume on reconnect).
7. Stop confirm traps focus, closes on Escape, returns focus correctly.

---

## 4. Quick recap

Layout: as student-experience §5.6; large fixed control, `Last (2)(5)(10) min` radio group (`aria-label="How far back"`, default 5, remembered per student). Pressing a window runs immediately. The button is available in Active and Paused states and disabled (with reason text) in Stopped. When hub is disconnected the button still works for the already-received transcript (see §6).

Flow: POST `/quick-recap {windowMinutes}` -> 200 `CatchUp{kind:"Extractive"}` rendered at once -> `catchup.ready{kind:"Ai", catchUpId}` upgrades the same panel.
1. Extractive result (immediately): label "Quick view: lines from the last 5 minutes" (kind Extractive; plain text; citations as timestamps). Shows up to ~5 lines, newest last, with "Show all lines in this window".
2. While the AI version is pending: static text "Making a clearer summary..." (no spinner required; if a spinner is used it must stop under reduced motion and be `aria-hidden`).
3. On `catchup.ready` with matching `catchUpId`: replace in place with "Here is where you are": 1-2 sentences, up to 3 bullets, "Right now:" line, citations. Keep the extractive text available behind "Show the raw lines". If the student expanded or is reading the panel, do not move focus or scroll; update the label text and announce once via status-region "A clearer summary is ready." (not the content).
4. Poll fallback `GET /catch-ups/{id}` after 10 s with no `catchup.ready`; if still none by 20 s show "A clearer summary isn't ready. The quick view above is still good." plus "Try again" (429 -> "Give it a moment, then try again.").
5. Window longer than session: "You have only been here 3 minutes. This is all of it."
6. Gaps inside the window are disclosed: "Audio was not available 00:10 to 00:14." (from `gap` events).
- Panel: side panel (non-modal) on desktop, bottom sheet on phone. Recording continues behind it. Buttons: "Show more detail", "Got it, back to lecture".
- Copy is non-shaming; no counters of use are displayed.

Focus and screen reader
- On open (press): focus moves to the panel heading (`tabindex=-1`, h2 "Catching up: last 5 minutes"). The extractive content is read by navigation, not announced as live.
- "Got it" closes and returns focus to the transcript's latest line container (Jump to live position); Escape closes and returns focus to the Quick recap button. Opening again with a different window re-runs and re-focuses the heading.
- A second press while a request is in flight is ignored (button `aria-busy`); no duplicate panels.

Acceptance criteria
1. The extractive result renders before any `catchup.ready` event (fixture with AI delayed 15 s).
2. `catchup.ready` for a stale `catchUpId` is ignored; for the current id it replaces text without changing focus or scroll.
3. Each of 2/5/10 min maps to the correct transcript span in a fixture (cross-check with backend), including the shorter-than-window message.
4. Works with keyboard only via `Alt+L` and Tab; skip link reaches it in one Tab stop.
5. With `MockAi` failing, the panel stays on the extractive view with the explanatory line and a Try again; no error toast, no focus loss.
6. AI text with markdown/HTML/URL renders inert (red-team fixture).
7. Pausing, reconnecting or receiving transcript events does not close the panel.
8. Quiet mode: pressing the button is the only trigger; no prompt, suggestion, sound or nudge appears anywhere.

---

## 5. Mic denied (or no device)

Triggers: `NotAllowedError`, `NotFoundError`, track `ended` by device loss while Active (the latter is a mid-session failure, below).

Start-time denial layout (replaces the live view, session not started or immediately stopped by the client):
```
h1 Microphone is blocked
p  We can't hear the lecture yet. Nothing has been recorded.
   How to turn it on: [Chrome] [Safari] [Firefox] (disclosures, one open at a time)
   [ Try again ]   [ Type or paste notes instead ]   [ Back to course ]
```
- If a session was already created, call `/stop` or `/delete` (client choice: stop, so consent metadata stays consistent) and say "Nothing was recorded" only after the server returns success; otherwise say "We could not confirm; check your lectures list."
- Focus lands on the heading; "Try again" is the first control. No automatic retry, no repeated prompts; "Try again" only calls `getUserMedia` on press.
- Typed-notes fallback goes to a notes view labeled "From typed notes" (M2 stub: stores nothing server-side if not supported; Lead Architect to confirm; do not imply TL;DR works until it does).

Mid-session mic loss (device unplugged, permission revoked): alert-region once: "The microphone stopped. Recording is paused." State becomes a Paused-like panel with heading "Microphone stopped", Resume (retries mic) and Stop. Server is told via REST pause. Gap reason `device`.

Acceptance criteria
1. Denied permission shows the blocked screen with Try again and typed-notes buttons; no dead end.
2. There is no loop: no `getUserMedia` call occurs without a user press after denial.
3. No transcript or hub events are rendered for a session whose mic never opened.
4. Alert fires exactly once for mid-session loss.
5. Raw `DOMException` messages never appear in UI or console output.

---

## 6. Disconnected / offline

Rule from C-16: hub disconnect while Active shows "not connected, audio not being saved."

Banner (persistent, in status bar area, under it if wide; text + icon; `role` none, announced by alert-region once per episode):
- Within 5 s of hub loss: "Not connected. Audio is not being saved." (DRAFT) plus "Trying to reconnect." The status bar state label switches to "Not recording". The local track is stopped when the client has been offline > 60 s (client auto-pause, contract §4.5); before that the client may keep the track open but the label must still read "Not connected" and never "Recording".
- Reconnect backs off 0/2/5/10/30 s with jitter (silent, no repeated alerts). Show only static "Trying again..." text, not countdowns.
- Honest-copy rule: do not say "still recording on your device" or "we will fill it in" (the ring buffer is bounded and gaps are possible; Speech engineer to confirm what is claimed). After reconnect: status-region "Back online. Recording is on." only if hub state is Active and mic track live; otherwise show §3 or §8.
- Gap handling: `gap` events add marker rows "Audio not available 00:10 to 00:14 (connection)" to the transcript; never fabricated text. Reasons map to words: paused / connection / device / speech service.
- On reconnect: hub `Join{lastSeq}`; replayed finals are deduped; if `truncated:true`, page `GET /transcript` showing "Loading earlier lines" (static) and keep position.
- Offline and Quick recap: works on already-received lines; if none available, "I need a connection to catch you up. Try again." with Try again.
- Actions available offline: Stop (queued: shows "Stopping... will finish when you are back online"; the local mic is stopped immediately), Resume disabled with reason.

Keyboard/SR: banner is a `div` with text, not focusable; focus unchanged. One alert, then silent.

Acceptance criteria
1. Banner text present within 5 s of simulated disconnect, in forced-colors and at 320px.
2. State label never reads "Recording" while the hub is disconnected.
3. One alert announcement per disconnect episode regardless of retry count (spy on alert-region mutations).
4. Gap markers appear for `gap` events and nothing is rendered between them as text.
5. After reconnect with duplicate/overlapping replay, the transcript has no duplicate `segmentId`s.
6. The string "still recording on your device" is absent from the bundle (lint).

---

## 7. Summary Delayed / Failed

Driven by `summary.status{state, lagSeconds}`. The transcript is never blocked by it.
- Pending/Running: no extra UI beyond "Updated at ..." text.
- Delayed: under the TL;DR, static text "The summary is running behind. The transcript is up to date." (DRAFT) Last good TL;DR stays. No lag counter ticking; show lag in minutes only on request ("Details").
- Failed: "Couldn't update the summary. Your transcript is safe. The last summary is from 00:36." with "Try again" only if the contract adds a retry endpoint (not in v1; otherwise omit the button and say "It will try again automatically."). Quick recap keeps working (extractive).
- Recovery: when a `summary.revision` arrives, the note disappears; a status-region "Summary updated" is sent only if Announce TL;DR is on.
- No red styling; use neutral note style with an info icon and text.

Acceptance criteria
1. Delayed/Failed show the notes, keep the previous TL;DR text and do not alter transcript flow (slow-summarizer fixture).
2. Notes use `status-region` at most once per transition and not at all when Quiet announce settings are default (verify default is silent for Delayed; failure of summary is not safety critical).
3. No focus movement; no modal.

---

## 8. Mic/server state mismatch warning

When local track state and `session.state` disagree: (a) server Paused/Stopped but track live; (b) server Active but track ended/muted; (c) transcript or audio ack received while Paused.
- Immediately stop capture (stop all tracks, close audio socket) and show a prominent panel above content (same slot as the Paused panel), alert-region once:
  "Something doesn't match. We turned the microphone off to be safe. Reload the state to continue." (DRAFT)
  Buttons: "Check again" (refetch `GET /sessions/{id}`, rebuild state), "Stop lecture".
- The status bar shows "Not recording" until `Check again` yields consistent state. Resume only through the normal Resume flow.
- Never auto-recover into capture.

Acceptance criteria
1. A test that keeps the track live after a server-driven pause triggers the warning and ends the track within 1 s.
2. Warning is not color-only, appears in all breakpoints, and is announced once.
3. Capture cannot restart without a user press.

---

## 9. Auto-pause and auto-stop notices

Contract §10 (proposals, configurable): Active with no frames for 60 s emits a `gap`; auto-pause after 5 min; Paused auto-stops after 30 min; hard cap 3 h total elapsed. Heartbeat = hub Ping and frame arrival.
- 60 s gap (no audio arriving): neutral inline note in the transcript and status bar sub-label: "No audio for 1 minute. Check your microphone or connection." No alert, no sound (hypothesis: a calm note beats an interruption; re-evaluate in testing). Status-region once.
- Auto-pause at 5 min no audio: panel as §3 but heading "Recording was paused automatically", sentence "No audio was heard for 5 minutes, so we paused to be safe. Nothing is being recorded." (DRAFT) Resume / Stop. alert-region once (state change the student did not request). No focus move. This is not inferred distraction; it is a technical no-audio timeout and copy must say so ("no audio was received").
- Paused auto-stop at 30 min: "Lecture stopped automatically after 30 minutes paused. Your transcript is saved." Primary: "Review notes". Focus: if focus was inside the Paused panel (which is removed), move focus to the new Stopped heading; otherwise leave.
- Hard cap 3 h: "This lecture reached the 3 hour limit and was stopped. Your transcript is saved." Same handling.
- Failed (server abandoned, 10 min no heartbeat): "This lecture ended unexpectedly at 10:42. Here is what we saved." with Review notes.
- Tab-return: no popups on return; the panels above are already in place and the title prefix shows the state.
- A student-visible countdown is not shown (rule 8). The 30-minute rule is stated in static text in the Paused panel only.

Acceptance criteria
1. Fixtures for each reason produce the matching copy and a single alert/status announcement; unknown `reason` falls back to "ended by the system".
2. Auto-pause does not take focus; auto-stop focus move happens only when the focused element is removed.
3. No dialog opens for any of these notices.
4. Copy contains no words implying attention judgment (lint list: distracted, lost focus, inactive, idle, away).

---

## 10. Quiet mode (M2 default and only mode)

- Allowed UI: status bar, transcript, TL;DR, Quick recap, student-initiated panels, safety-critical notices in §3, §5, §6, §8, §9.
- Not allowed: suggestions, chips, check-ins, sounds, vibration, browser notifications, toasts, badges, auto-opening panels, "welcome back" popups beyond the static restored state, attention prompts, any content derived from camera, mic level, pointer activity or inactivity.
- Settings available in M2: Announce new transcript lines (off), Announce TL;DR updates (off), Reduce motion, text size/spacing, theme (including high contrast). Settings persist locally and are applied without leaving the lecture.
- The Help level label is read-only text "Quiet" until Balanced/Active exist.

Acceptance criteria
1. Across a 60-minute fixture replay, the number of non-user-initiated overlays, chips, toasts and notifications is zero; only state banners in §3/§5/§6/§8/§9 appear when their conditions occur.
2. No `Notification`, `navigator.vibrate` or `AudioContext` output is invoked (spy).
3. A grep of source confirms no use of camera APIs.

---

## 11. Cross-cutting WCAG 2.2 AA checklist for these screens

- 1.4.3/1.4.11 contrast: text 4.5:1, indicators and focus rings 3:1, in light, dark and forced-colors.
- 1.4.10 reflow at 320px; 1.4.4 resize to 200%; 1.4.12 text spacing overrides do not clip.
- 2.1.1/2.1.2 keyboard with no trap (only the Stop confirm is modal); 2.4.3 focus order; 2.4.7/2.4.11/2.4.12 focus visible and not obscured by sticky status bar or bottom sheet; 2.5.8 target size.
- 3.2.2/3.2.6 consistent placement of Pause/Resume, Stop, Quick recap; 3.3.7 no redundant re-entry (consent attestation reuse, kept notes draft).
- 4.1.3 status messages via the three regions only; no other `aria-live` anywhere.
- 2.2.1 no time limit imposed except documented auto-stop rules, which are stated in static text and preserve the transcript.
- Axe: zero serious/critical issues in every state in §1-§9 using fixtures; manual NVDA/JAWS/VoiceOver/TalkBack passes are tracked by `device-accessibility-validation` (NOT RUN).

## 12. Handoffs, not done, risks

- frontend-engineer: build a state-fixture harness (one fixture per section state) so QA can render each state without live audio.
- qa-engineer: convert the acceptance criteria per section into Playwright/axe/live-region-spy tests; add the copy lint lists.
- Lead Architect: decide (1) `session.state.reason` vocabulary, (2) replay-provider flag exposed to UI, (3) whether a summary retry endpoint exists, (4) typed-notes fallback scope in M2.
- legal-compliance-reviewer / Product Owner (G1): all consent, pause and delete-disclosure wording; hidden placeholder for "audio sent just before pause".
- human-research-coordinator (G4): test the Paused-panel-vs-dialog choice, the silent 60 s note, the alert for auto-pause, and Quiet defaults with students, including varied attention patterns.
- Not done: no code, no prototype, no screen-reader run, no human validation. Every threshold and string here is unvalidated.

## Implementation decisions (Lead Architect, 2026-10-27, after F2)
Unvalidated by humans (G4); all copy DRAFT (G1). Accepted from the F2 report: (1) resume = REST resume then mic request, re-pause on mic failure; (2) pending pause clears after 3 s and shows true state; (3) server-driven pause/stop with a system reason stops the track and shows the section 9 panel, not the mismatch panel; (4) mismatch panel stays latched until "Check again", which never restarts capture; (5) Stop always asks for confirmation, initial focus on "Keep recording"; (6) mic denied stops the session and Try again starts a new one; (7) disconnect alert only after reconnecting/failed, not on first connect. Open for neurodivergent-ux-specialist / design: sticky header plus status bar eating space at 320px and 400% zoom, two-column layout at lg being cramped, and whether the finals live region (role=log with nested aria-live) matches "three regions". Deferred to F3: Quick recap, summary Delayed/Failed UI, 60 s no-audio note, notes, "Loading earlier lines", gap announcements, Focus toggle.
