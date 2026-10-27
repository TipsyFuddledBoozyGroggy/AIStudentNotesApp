# Device, Assistive-Technology and Acoustic Validation Plan

Authored by qa-engineer acting as substitute for device-accessibility-validation (agent not loaded). No manual or device testing has been performed.

Status: PLAN ONLY. Date: 2026-10-27. Gate: "Screen-reader, real-device and classroom audio QA" = **NOT RUN** (CLAUDE.md assurance gates). Nothing in this document is a pass. Only attributable human results (section 7) can change a cell, and only a human reviewer can move the gate in `docs/RELEASE_GATES.md`.

Note: the agent file lists `docs/validation/*` deliverables; this task was scoped to this single file. Splitting into `docs/validation/` is a follow-up for the Lead Architect.

## 1. Scope and sources

- Criteria: `docs/ux/student-experience.md` section 6 (WCAG 2.2 AA, live-region rules) and section 9 criteria 5-9, 15-22 (A11y 18-22 are the manual-gated ones).
- Strategy: `docs/qa/test-strategy.md` M1-09, M2-02, M2-10, M2-11, M2-19; release gate requires a manual keyboard/screen-reader pass.
- Audio: `docs/architecture/speech-pipeline.md` sections 10-11.
- Testability today: M1 screens exist in `src/web/src/app` (`/signin`, `/` dashboard, `/courses`, `/courses/new`, `/courses/[id]`). The lecture workspace, consent start, recording status, transcript, TL;DR, I Lost Track and denied-mic screens are **not implemented**; their scripts are written now so they are ready, marked NOT-YET-TESTABLE.

## 2. Automated evidence (exists vs NOT RUN)

Source: `src/web/e2e/courses.spec.ts`, `playwright.config.ts`. Existence of spec files is verified by reading; **I did not execute them in this task**, so run results are not asserted here.

| Check | Exists in repo | Executed by me now | Notes |
|---|---|---|---|
| Playwright, Chromium (Desktop Chrome) only | Yes, 1 project | NO | Firefox/WebKit/Edge/mobile projects absent |
| 11 e2e tests: UI sign-in, empty dashboard, persistence, sign-out, 401 redirect, cross-user (bob/alice), keyboard course creation, axe x4 pages | Yes | NO | Last pass record: not verified here |
| axe-core incl. color-contrast on /signin, empty dashboard, /courses/new, course detail + populated dashboard | Yes | NO | Automated catches only a subset of WCAG issues |
| Keyboard-only course creation (Chromium) | Yes | NO | Does not cover SR semantics |
| Firefox, WebKit, Edge, mobile emulation, touch | NOT RUN (not configured) | - | |
| 320px / 400% zoom reflow, text spacing, target size, reduced-motion, forced-colors automated checks | NOT RUN (not present) | - | Could be added cheaply (viewport 320x256, `emulateMedia({reducedMotion, forcedColors})`) |
| Lighthouse a11y | NOT RUN | - | |
| Any screen reader (NVDA, JAWS, VoiceOver, TalkBack) | NOT RUN | - | No tester, no results |
| Real-device, physical phone/tablet | NOT RUN | - | |
| Lecture workspace axe/live-region tests | NOT RUN, NOT-YET-TESTABLE | - | UI not built |
| Acoustic / STT fixtures | NOT RUN | - | No adapters, no fixtures in repo |

Automated results complement, never replace, manual AT testing.

## 3. Device / browser / AT matrix

Status vocabulary: NOT RUN, BLOCKED, NOT-YET-TESTABLE, IN_PROGRESS. `PASS` is reserved for cells with attached attributable results (section 7) and reviewer sign-off. Every cell below is currently NOT RUN (M1) or NOT-YET-TESTABLE (workspace).

Record exact versions per run (OS build, browser, AT, AT-browser pairing, hardware vs emulator). Emulator/simulator runs are labelled and never satisfy the real-device gate.

| ID | Platform / device | Browser | AT | Priority | M1 screens | Lecture workspace |
|---|---|---|---|---|---|---|
| C1 | Windows 11 desktop | Firefox latest | NVDA latest | P1 | NOT RUN | NOT-YET-TESTABLE |
| C2 | Windows 11 desktop | Chrome latest | NVDA latest | P1 | NOT RUN | NOT-YET-TESTABLE |
| C3 | Windows 11 desktop | Chrome latest | JAWS (licensed, current) | P1 | NOT RUN (BLOCKED: license/tester) | NOT-YET-TESTABLE |
| C4 | Windows 11 desktop | Edge latest | NVDA or Narrator | P2 | NOT RUN | NOT-YET-TESTABLE |
| C5 | macOS | Safari latest | VoiceOver | P1 | NOT RUN (BLOCKED: no Mac confirmed) | NOT-YET-TESTABLE |
| C6 | macOS | Chrome / Firefox | VoiceOver | P3 | NOT RUN | NOT-YET-TESTABLE |
| C7 | iPhone (physical) | Safari | VoiceOver | P1 | NOT RUN (BLOCKED: device) | NOT-YET-TESTABLE |
| C8 | iPad (physical) | Safari | VoiceOver | P2 | NOT RUN | NOT-YET-TESTABLE |
| C9 | Android phone (physical) | Chrome | TalkBack | P1 | NOT RUN (BLOCKED: device) | NOT-YET-TESTABLE |
| C10 | Android tablet | Chrome | TalkBack | P3 | NOT RUN | NOT-YET-TESTABLE |
| K1 | Windows desktop | Chrome, Edge, Firefox | none, keyboard only | P1 | NOT RUN | NOT-YET-TESTABLE |
| K2 | macOS | Safari, Chrome | none, keyboard only (Safari needs Option-Tab setting) | P2 | NOT RUN | NOT-YET-TESTABLE |
| K3 | iPad + hardware keyboard | Safari | none | P3 | NOT RUN | NOT-YET-TESTABLE |
| V1 | Any desktop | Chrome, Firefox | 400% zoom / 320 CSS px reflow, text spacing override | P1 | NOT RUN | NOT-YET-TESTABLE |
| V2 | Any desktop + phone | All | `prefers-reduced-motion: reduce` (OS setting) | P1 | NOT RUN | NOT-YET-TESTABLE |
| V3 | Windows | Edge, Firefox, Chrome | Forced colors / Windows contrast themes | P1 | NOT RUN | NOT-YET-TESTABLE |
| V4 | macOS / iOS | Safari | Increase Contrast, Reduce Motion | P2 | NOT RUN | NOT-YET-TESTABLE |
| V5 | Android | Chrome | Font size max, display size, Remove animations | P2 | NOT RUN | NOT-YET-TESTABLE |
| V6 | Phone portrait and landscape; tablet | All above | Touch target, orientation (SC 1.3.4) | P1 | NOT RUN | NOT-YET-TESTABLE |

Minimum viable gate set (proposal for Product Owner approval): C1, C2, C5 or C7, C9, K1, V1, V2, V3. JAWS (C3) is strongly preferred but depends on licence; if absent, record gap as an accepted risk by the PO, not as a pass.

Pairing note: record the real pairing, not the family. Example "NVDA 20xx.x + Firefox 1xx", not "NVDA".

## 4. Manual test scripts

General preparation (every run)
1. Fresh test accounts via dev sign-in (alice-style seeded users, plus an empty user). No real student data.
2. Run API + web via documented `docs/DEVELOPMENT.md` flow. Record git commit SHA.
3. Default AT settings (verbosity, browse/focus mode behaviour) unless the step says otherwise. Note non-default settings.
4. Start screen recording only of your own test screen if desired; do not capture others.
5. For every step: record Observed result and one of PASS / FAIL / BLOCKED / N/A in the results template. Any deviation: file a defect (section 7).

### 4.1 Screen-reader script: M1 screens (applies to C1-C10; adapt gestures)

Gestures: desktop uses Tab, heading/landmark/form-field quick keys; mobile uses swipe, rotor (iOS) or reading controls (TalkBack), explore-by-touch.

S1 Sign-in (`/signin`)
1. Load page. Expect: document title meaningful; single h1 "Sign in"; landmarks reachable (main).
2. Navigate to every control. Expect: every field has a spoken name, role and state; required and error info announced.
3. Submit empty/invalid. Expect: error identified in text, tied to the field, focus handling per student-experience 4 (summary receives focus), announced once.
4. Sign in with valid dev account using password-manager autofill if available (SC 3.3.8). Expect: no cognitive puzzle; landing on dashboard announces h1 "Hi, ...".
5. Failed sign-in. Expect: non-shaming error, no stack trace, announced once.

S2 Dashboard (`/`) empty user
1. Heading list. Expect: h1; next-step region; "Add your first course" is the single primary action, spoken as link/button correctly.
2. Landmark list. Expect: header, main, no duplicate unnamed landmarks.
3. "Coming later" items are not presented as live functionality; no fake data.
4. Activate "Add your first course". Expect: focus lands on the new page's heading or first logical element; page change announced (title or heading).

S3 Dashboard populated
1. Course cards: each reads name, last session info, one action; link names distinguishable out of context (not "Open" x6 without course name).
2. Up to 6 cards; "See all courses" present if over. No unfinished-work counts.
3. "Why this?" link, if present, explains the rule and is reachable.

S4 Course creation (`/courses/new`)
1. Tab through. Expect: "Course name" required stated; optional section disclosure exposes expanded/collapsed state.
2. Recording-rules checkbox: label read in full, help text ("What does this mean?") reachable and associated.
3. Help level group: radio-group semantics, group name announced, arrow keys switch.
4. Blur empty required field. Expect: error announced after blur, not per keystroke; `aria-describedby` read.
5. Submit invalid. Expect: error summary receives focus and is read. Submit valid. Expect: status "course added" announced and focus moves to course heading.
6. Refresh mid-draft. Expect: draft restored and announced or discoverable (student-experience 4, criterion 3).

S5 Courses list and detail (`/courses`, `/courses/[id]`)
1. Headings structure h1 then logical h2s; "Your courses" reachable.
2. Detail page: next-action "Start a lecture" shows only if implemented; otherwise placeholder is honest.
3. Open another user's course URL (use a second account). Expect: "We can't find that course" heading, no data leak, focus/announcement sensible.
4. Sign out. Expect: redirect to /signin, announced via title/heading; back button does not reveal data.

S6 Cross-cutting
1. Skip link(s) present and first in tab order (when implemented; record N/A if not).
2. Tab order matches visual order at desktop and mobile.
3. No unexpected live-region chatter during idle 2 minutes.

### 4.2 Screen-reader script: lecture workspace (NOT-YET-TESTABLE until UI exists)

Exit criteria from student-experience section 6 and criteria 5-9, 15-19.

W1 Consent-first start
1. Open pre-start screen. Expect: focus on heading; no mic prompt yet (criterion 5, use browser permission indicator).
2. Rules-confirmation checkbox read in full if shown. Activate "Start recording"; browser permission prompt appears only now.
3. On success: focus moves to "Recording" status heading; status announced once; Tab order reaches Pause, Stop, Help level.

W2 Recording status
1. Recording, Paused, Stopped, Mic blocked, Offline each announced once via the single polite `role="status"`; critical mic loss uses `role="alert"` once.
2. Indicator has text, not only color (check in forced colors, V3).
3. Elapsed time ticker is not announced every second (`aria-hidden`); readable on demand.
4. `document.title` prefix "Recording" present.

W3 Pause / Stop
1. Pause: spoken "Paused. Mic is off."; Resume replaces in same position; focus stays on the control.
2. Stop opens confirm dialog: focus trapped, Escape closes, focus returns to Stop; Stop (primary) / Keep recording read correctly.
3. After stop: "Lecture saved" announced; next action "Review notes".

W4 Transcript
1. 30-minute session (use synthetic stream, section 5) with default settings: transcript lines are NOT announced (criterion 19). Count announcements per status change (must be <= 1).
2. Enable "Announce new transcript lines": announcements batched about every 30s, finals only, never partials.
3. Partial to final replacement: no focus movement, no announcement of partials, no scroll jump for lines above (criterion 8).
4. Scroll up with keyboard/virtual cursor: autoscroll stops; "Jump to live" appears, operable by keyboard and AT.
5. Timestamps available to AT on lines but not read as noise by default.

W5 TL;DR
1. Landmark `complementary` reachable by landmark navigation.
2. Updates do not move focus or scroll (criterion 17). "TL;DR updated" polite announcement at most once per 60s and only if enabled.
3. "Pause updates" holds content; "Catch up to latest" resumes. "Show more" expands up to 7.
4. Source timestamp links reveal on request and are keyboard operable.

W6 I Lost Track
1. Skip link "Skip to I lost track" works; shortcut Alt+L works and does not collide with the AT or browser (record collisions per AT: NVDA, JAWS, VoiceOver, TalkBack/hardware keyboard, Firefox Alt-menu behaviour).
2. 2/5/10 window is a radio group "How far back"; arrow keys work; activating a window runs recovery immediately.
3. On open: focus moves to the response heading (`tabindex=-1`); content read by navigation, not auto-dumped.
4. "Show more detail" operable; state persists. "Got it, back to lecture" returns focus to transcript; closing otherwise returns focus to I Lost Track.
5. Failure path (take API offline): raw excerpt with the plain message shown and announced once.
6. Recovery does not pause recording.

W7 Denied mic (C-matrix: set browser permission to Block beforehand)
1. Focus lands on "Try again"; status text read; browser-specific steps readable.
2. "Paste or type notes instead" reachable and usable by AT; no prompt loop.

W8 Offline / reconnect
1. Disable network. Expect banner within 5s, announced once; no repeated alerts during backoff.
2. Restore. Expect "Back online. Filled in..." announced once; gap marker readable.

W9 Ask and clarification chip (Balanced/Active only)
1. Typing in Ask does not interrupt AT or transcript. Draft persists on close/reopen.
2. Chip never takes focus, never animates, dismiss button named, auto-dismiss silent. Quiet mode shows none.

### 4.3 Keyboard-only script (K1-K3), no AT running

Tools: unplug or disable pointer. Verify for every screen listed.
1. Tab and Shift+Tab reach every interactive element in logical order; no trap (SC 2.1.2); nothing reachable that is hidden.
2. Focus indicator visible on every element, at least 2px, 3:1 contrast, never hidden under sticky bars (SC 2.4.11/2.4.12).
3. Enter/Space activate buttons; Space for checkbox; arrows for radio groups; Escape closes dialogs.
4. Full M1 journey: sign in, add first course, view dashboard, open course, sign out.
5. Workspace (NOT-YET-TESTABLE): start, pause, resume, Alt+L, Alt+P, Alt+J, Alt+K, stop, dialog focus return, Skip links. Verify shortcuts can be remapped/disabled (SC 2.1.4).
6. Safari: confirm "Press Tab to highlight each item" setting state recorded (macOS Option-Tab difference).

### 4.4 Visual and preference scripts

V1 Reflow / zoom
1. At 1280 wide, set browser zoom to 400% (equivalent 320 CSS px). Also narrow window 320 px. No horizontal scrolling for text content, no loss of content or function, no overlapping.
2. Apply text-spacing bookmarklet or user stylesheet (line-height 1.5, paragraph 2x, letter 0.12em, word 0.16em). No clipped text.
3. Text-only 200% resize. Recording indicator and primary actions remain visible (criterion 6, NOT-YET-TESTABLE for workspace).

V2 Reduced motion
1. OS setting on. Verify no non-essential animation on M1 screens (transitions, scroll behaviors).
2. Workspace: TL;DR swap without animation; "Updated" tag text not color-only; "Jump to live" instant.
3. In-app "Reduce motion" toggle (if built) works independently.

V3 Forced colors / high contrast
1. Windows contrast theme (Aquatic, Desert, Dusk, Night sky) and Edge/Firefox forced colors. All controls, focus rings, checkboxes, radio selected state, error text and recording indicator remain distinguishable; no information lost (icon plus text).
2. Repeat with `prefers-contrast: more` on macOS/iOS (V4).

V6 Touch / orientation (physical phone and tablet)
1. Primary controls at least 44 CSS px, I Lost Track 56 px (workspace); minimum 24 px (SC 2.5.8) for all.
2. Portrait and landscape both usable (SC 1.3.4). Pinch zoom not disabled (check viewport meta).
3. Layout at 320 px width; bottom sheet and sticky bars do not cover focused field or keyboard-opened input.
4. Dragging never required (SC 2.5.7).

### 4.5 Per-browser smoke (Chrome, Edge, Firefox, Safari mac, iOS Safari, Android Chrome)

Run the K1 keyboard script (pointer on mobile) on M1 journey and record browser-specific defects: cookie/session behavior (SameSite, third-party cookie settings, Safari ITP), form autofill, focus ring rendering, `inert`/dialog behavior. Workspace extras (NOT-YET-TESTABLE): getUserMedia prompt, AudioWorklet availability, tab backgrounding, iOS screen lock halting capture (warn per speech-pipeline), permission revoke mid-session (M2-11).

## 5. Acoustic protocol (synthetic or licensed audio only; NO classroom recording)

Purpose: validate STT adapter, pipeline and UX behavior under lecture-hall-like conditions without capturing any real class. Nothing here authorizes recording any person. Real instructor-authorized hall testing stays BLOCKED until the legal/consent gate clears.

Status: NOT-YET-TESTABLE (no STT adapter, no audio socket; mock STT is script-driven by design and does not exercise acoustics). Real-provider steps additionally need Product Owner approval (spending, vendor terms).

5.1 Source material (no real people recorded without written consent)
- Text-to-speech renderings of synthetic lecture scripts (STEM-term heavy, humanities, fast/slow). Verify TTS license permits test use; store scripts, not vendor-restricted audio, where terms forbid.
- Licensed or openly licensed speech corpora with clear redistribution terms (record dataset name, version, license, URL, access date). Examples to evaluate, not assumed: LibriSpeech-style corpora (CC BY), public-domain recorded lectures with explicit license. Must be checked by the tester; none is verified here.
- Volunteer speakers only with written consent covering recording, use and deletion, and not in a classroom. This is optional and still needs PO sign-off.
- Never use student voices, class recordings, or office hours.

5.2 Degradation by processing (instead of a room)
Generate each clean source into conditions, deterministic and scripted so they are reproducible (record tool, version, seed, parameters):
| Condition | How | Target behavior to observe |
|---|---|---|
| A. Clean baseline | Source at -20 LUFS, 16 kHz mono | Reference WER, partial latency |
| B. Distance / reverberation | Convolve with licensed impulse responses (small lecture room, large hall) at near/mid/far | WER degradation, low-confidence flagging |
| C. Ambient noise | Mix licensed noise (HVAC, crowd murmur, keyboard typing, coughs, door slam) at SNR 20 / 10 / 5 / 0 dB | Hedged output, no fabricated terms (E5) |
| D. Multiple speakers / overlap | Mix 2 to 3 voices, plus a Q&A question from a distant voice | Diarization optional; attribution not fabricated |
| E. Level problems | Too quiet (-45 dB), clipping (+6 dB), DC offset | Mic-level indicator / "too quiet / clipping" guidance |
| F. Frequency/codec | Bandlimit 4 kHz telephone-like, MP3 low bitrate | Resampler correctness |
| G. Silence/long pauses | 5, 30, 120 s silence | Idle timeout behavior, no hallucinated text |

5.3 Playback into the real capture path (physical, no people)
Play the processed audio through a speaker at set distances (0.5, 2, 5 m) in an empty room you control, into the target device mic (laptop built-in, phone, USB headset, lavalier). Playing synthetic audio is not recording a person; do not run it in a shared classroom without the room owner's permission (still no recording of people). Record: device model, mic type, OS, browser, room description, speaker model, measured noise floor (SPL app, label as uncalibrated).
Alternative with no acoustics: inject audio via Chrome `--use-file-for-fake-audio-capture` / Playwright fake media device to test the browser pipeline deterministically (this tests plumbing, not microphones).

5.4 Network and device stress (with the same synthetic stream)
- Poor Wi-Fi: throttle (200 kbps, 1% to 10% loss, 500 ms RTT), 10s / 60s / full dropouts. Expect gap events, no duplicated finals, honest offline copy (criterion 16).
- Interruptions: unplug USB mic, revoke permission mid-session, lock phone screen, background the tab 5 min, incoming call on phone, switch Bluetooth audio device.
- Battery/thermal: 60-minute stream on a physical phone and laptop on battery; record start/end battery %, device temperature feel, throttling. Not a pass/fail until PO sets targets.
- Privacy indicators: confirm the OS/browser mic indicator and the in-app recording indicator both visible while recording and both cleared after stop; confirm no capture after Pause (browser indicator behaviour differs by browser: record).

5.5 Metrics (all numbers are targets from existing docs, provisional)
- Mic-to-first-partial p95 under 1.5 s; final within 2 s of speech pause (speech-pipeline section 11).
- WER on STEM terms per condition A-G, per vendor (same fixtures through each adapter).
- Low-confidence flag precision on condition C/D; gap marker accuracy within 1 s.
- Announcement counts during a 30-minute SR session (criterion 19).
- Retention flag verification and cost per audio hour (PO approval needed).
Report each condition with n, seed and sample file hashes; no claim of "works in lecture halls" from synthetic results. Synthetic results may only support "tested against synthetic degradations".

5.6 Still BLOCKED (not covered here)
Real lecture-hall testing with instructor authorization requires clearance by `legal-compliance-reviewer`/counsel and campus authority; consent wording; PO approval. Do not do it informally.

## 6. Defect severity (for the register)

- Sev1: blocks task completion for an AT/keyboard user (trap, unreachable primary action, mic status unperceivable), or privacy break (recording continues after stop/pause).
- Sev2: serious barrier with workaround, or WCAG A/AA failure on core flow.
- Sev3: moderate/cosmetic AA issue, inconsistent announcements.
- Sev4: enhancement/AAA.
Release gate (test-strategy section 6): no open Sev1/2.

## 7. Results template (copy one block per tester per cell per run)

Never edit a cell to PASS without the attributable block below being filled and a reviewer initial.

```
Run ID:                  (e.g., DA-2026-11-03-C1-01)
Cell ID(s):              (C1, K1, V3, ...)
Script / section:        (4.1 S1-S6, ...)
Result status:           PASS | FAIL | BLOCKED | NOT RUN   (PASS only if every step PASS/N/A)
Tester name or handle:   ____________   Role/AT experience: (e.g., daily NVDA user, 3 yrs; or sighted QA)
Is tester a daily AT user? Y/N   Compensation/consent for participation recorded? Y/N (human-research gate if recruited)
Date/time (with timezone):
Commit SHA / build:
Environment: OS+build ____  Browser+version ____  AT+version ____  AT-browser pairing ____
Hardware: physical device model ____ OR emulator/simulator ____ (label clearly)
AT settings changed from default:
Step-by-step results:    step id | PASS/FAIL/BLOCKED/N/A | observation
Evidence links:          screen recording / SR speech log / screenshots (no real student data, no transcript content of real people)
Defects filed:           IDs
Tester signature or attributable identifier (email/commit/issue author): ____________
Reviewer (human) name and date:  ____________
```

Defect record

```
Defect ID:               DA-###
Title:                   
Severity (1-4):          
WCAG SC (if any):        (e.g., 2.4.11, 4.1.3)
Cell/environment:        (full versions, as above)
Screen / state:          
Preconditions:           (account, data, settings, permission state)
Steps to reproduce:      1. ... 2. ...
Expected:                (cite student-experience section or criterion)
Actual:                  (verbatim SR output where relevant)
Reproducibility:         n of m attempts
Frequency across cells:  
Evidence:                
Workaround:              
Owner (team):            frontend-engineer | backend-engineer | speech-engineer ...
Status:                  Open | Fixed (commit) | Verified by (human, date) | Won't fix (PO rationale)
```

Results log (initially empty; no results exist)

| Run ID | Cell | Tester | Date | Status | Defects |
|---|---|---|---|---|---|
| (none) | - | - | - | NOT RUN | - |

## 8. Gate status and dependencies

- Recommended gate status: **NOT_RUN** for screen-reader/real-device/classroom audio. Acoustic classroom portion **BLOCKED** (consent). M1 manual testing is runnable now; workspace testing is blocked by missing UI.
- Dependencies: tester availability, JAWS license, Mac/iPhone/Android hardware, Product Owner decision on minimal cell set and any participant recruitment/compensation, legal clearance for any real recording.
- Open questions for Lead Architect: (1) `docs/RELEASE_GATES.md` is not present yet, so register this gate there; (2) whether to move this plan under `docs/validation/` per agent definition; (3) shortcut collisions for Alt+L/P/J/K on Firefox (Alt activates menu bar) and macOS (Option produces characters) must be decided before build; (4) confirm whether mic-loss paths rely on verified local buffering before UI copy ships.
- Suggested cheap automated additions (not done, would need frontend/QA work): Playwright projects for Firefox and WebKit, 320 px and `forcedColors`/`reducedMotion` emulation, Pixel/iPhone emulation (emulation labelled as such).
