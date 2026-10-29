# Front-end redesign progress (design-direction.md v1)

Owner: frontend-engineer. Started 2026-10-29. Scope: `src/web` only. Nothing here is human-validated (G4 NOT_RUN);
consent/recording wording is unchanged DRAFT (G1 BLOCKED). No screen-reader, real-device or human testing was done.

## How tests were run (applies to every slice)
- Lint / typecheck on the host in `src/web`: `npm run lint`, `npm run typecheck`.
- Unit tests in `node:22` (host Node is 22.14, vitest needs >= 22.22.2). Same mount as the Lead's command (only `src/web`),
  but `node_modules` sits on a named Docker volume so the host's darwin `node_modules` are not replaced:
  `docker run --rm -v "$PWD":/app -v tether_web_linux_nm2:/app/node_modules -w /app node:22 sh -c 'npm ci --no-audit --no-fund >/dev/null 2>&1; npx vitest run'`
- Real-stack e2e: a `dev:real` server was already listening on :3000 when I started (not started by me; I did not stop it).
  `playwright.config.ts` refuses an existing server, so I added `playwright.reuse.config.ts` (same config, `reuseExistingServer: true`):
  `npx playwright test -c playwright.reuse.config.ts`.

## Baseline (before any change)
- Lint: 1 warning (`_label` unused in e2e/lecture.spec.ts). Typecheck: clean.
- Unit: 219 passed; `schemas.test.ts` cannot load in this mount (needs `src/shared`, file-level error, as in the Lead's baseline).
  With `src/shared` also mounted, `schemas.test.ts` runs and its syllabus fixtures fail: pre-existing, contracts-side, not touched.
- Found: `CourseForm.test.tsx` "saves via the API, clears the draft" failed 1 of 2 baseline runs. Cause is a real race, see slice 0.

## Slice 0: tokens, fonts, no-flash script, contrast test (DONE)
Changed / added:
- `src/web/src/lib/design/tokens.ts` (four palettes as data), `contrast.ts` (WCAG formula), `tokens.test.ts`, `prefs.ts`, `prefs.test.ts`.
- `src/web/tailwind.config.ts`: section 2.7 tokens (colours without `<alpha-value>`, type scale, radii, shadows, durations).
- `src/web/src/app/globals.css`: four palette blocks, shadows/durations, density, text size, `prefers-contrast: more`, two-tone focus ring.
- `src/web/src/app/layout.tsx`: `next/font/google` **Atkinson Hyperlegible Next** (body, it IS in the installed Next 15.5.28 catalog) and
  **Newsreader** (headings, opsz axis); both OFL, self-hosted at build.
- `src/web/public/prefs-init.js`: no-flash script, loaded synchronously in `<head>`.
- `src/web/src/components/CourseForm.tsx`: 4-line bug fix (see below).
- `src/web/playwright.reuse.config.ts` (see above).

Contrast test (`tokens.test.ts`) recomputes, for each of Paper / Night desk / Fog / Dusk: every text role (ink, muted, accent,
recording, success, warning, danger) on every background role (canvas, surface, raised, sunken, and the four subtle tints)
>= 4.5:1; on-accent on accent >= 4.5:1; every boundary role (border-strong, focus, accent, recording, warning, danger) vs every
plain background >= 3:1. That is stricter than the doc's table (a full matrix, not only listed pairs). **All pass.** It also parses
`globals.css` and resolves the cascade per palette, asserting it equals `tokens.ts`; and checks 21 of the doc's hand values are
within 0.02 (they are; worst difference 0.01). Also guards: no opacity modifiers on colour utilities; `aria-live=` only in
`Transcript.tsx` and `TldrPanel.tsx` (the latter is `off`).

Deviations from the doc (with reasons):
1. **Doc bug fixed**: the paste-ready Dusk block (`[data-theme=dark][data-quiet=true]`) omits `--c-muted`, so Dusk would inherit
   Fog's `#5E5B54`: 2.67:1 on the Dusk canvas (fails AA). Every palette block now lists every role; the cascade test would catch a regression.
2. The no-flash script is a static same-origin file (`public/prefs-init.js`), not inline: the existing security guard
   `plainText.test.tsx` bans `dangerouslySetInnerHTML` in the source tree, and I did not weaken it. Side benefit: no inline script for a future CSP.
3. Pre-existing bug fixed in `CourseForm.tsx` (behaviour change, flagged): when a save finished within the 400 ms autosave
   debounce, the pending timer re-wrote the cleared draft, so the student would later see "We restored your draft" for a course
   already saved. A `saved` ref now stops that timer. The flaky test then passed 3/3 runs.
4. OpenDyslexic: **deferred**. No `@fontsource/*` package is installed and it is not in the next/font Google catalog; per the Lead's
   decision I did not download font files.

Results: lint 1 warning (baseline one); typecheck clean; unit **580 passed, 1 skipped** (the raw-colour guard, enabled in slice 1),
schemas.test.ts file error as baseline. e2e `courses.spec.ts`: first run 9 passed / 2 failed (sign-in navigation timed out while the
long-running dev server recompiled the new layout); immediate rerun **11/11 passed**. Watching for recurrence.

## Slice 1: primitives (DONE)
Changed / added:
- `globals.css` component layer: `.btn`, `.btn-primary` (filled accent), `.btn-secondary`, `.btn-quiet`, `.btn-danger` (never filled),
  `.btn-sm`, aria-disabled look (dashed border, sunken, muted text; stays focusable), `.btn-steady` (busy without a grey flash, for
  I lost track), `.field` (+ `aria-invalid` border), `.label`, `.help`, `.card`, `.panel` + stripes (`neutral`, `notice` = warning,
  `safety` = recording, `danger`), `.tag` (`warning`, `neutral`, `accent`), `.eyebrow`, `.display` (Newsreader), `.link`.
  Hover/press change border, underline or fill only (no scale, no glow). Low-stimulation look: no shadows, hover changes border.
  Forced colors: control borders use `CanvasText`.
- `src/web/src/components/ui/`: `Switch.tsx` (role=switch + visible On/Off word), `Segmented.tsx` (radiogroup, one tab stop,
  arrow keys), `Disclosure.tsx` (`<details>` + chevron, 44px row), `UiKit.tsx`, `primitives.test.tsx`.
- `src/web/src/app/dev/ui-kit/page.dev.tsx`: dev-only page (404 in production, same pattern as the harness).
- Every raw Tailwind palette class in `src/` was mapped to a token (slate/blue/amber/red/white); the guard test is now enabled.
- `next.config.mjs`: `devIndicators: false` (the floating "N" badge overlapped content in demos; error overlays still show).
- Harness: new fixture `live-lost-track` (deterministic fake AI routes, `source: "mock"` so it is demo-labelled) for screenshots.
- Verification tooling: `playwright.screens.config.ts` + `e2e-screens/screens.spec.ts` (screenshots + axe per look/width),
  `playwright.harness.reuse.config.ts`. For the harness e2e I ran a stdlib-only local TCP forwarder 3100 -> 3000 (scratchpad
  script, not committed) so the spec's hard-coded :3100 reaches the already-running dev server; I did not start a second `next dev`
  in the same directory.

Regression found and fixed in this slice: the harness test "Paused panel needs no scrolling at 320px" failed after the type scale
grew to 17px body. Fixed by pulling two section 3/5.4 items forward: the site header is hidden while a live lecture screen is
mounted (`body:has([data-lecture-root]) [data-site-header]`), the lecture bar is now `top-0`, and safety panels (offline,
paused, mismatch, stopped, fatal) render directly under the bar, before the Demo notice. No test was edited.

Results: lint 1 warning (baseline); typecheck clean; unit **586 passed**; e2e courses **11/11**; harness e2e **32/32**.
Screenshots: `ui-kit`, `signin`, `lecture-live` at 390/1280 x 4 looks (first pass). axe on those: only best-practice
`page-has-heading-one` on the harness live screen (the live lecture screen has no h1; addressed in slice 4).

## Slice 2: shell, Preferences, preference store (DONE)
Changed / added:
- `src/web/src/components/AppShell.tsx` (replaces `SiteHeader.tsx`, deleted): top bar 56px (sticky from 1024px only):
  wordmark "Tether" (Newsreader), "Menu" disclosure below 1024px, "Preferences", display name from 768px, "Sign out" (same
  name, always visible when signed in). Left rail 200px from 1024px with Today (`/`) and Courses (`/courses`), `aria-current`.
  No bottom tab bar; no Schedule/Study teasers (routes do not exist). `main#main` kept (skip link target unchanged).
- `src/web/src/components/PrefsProvider.tsx` + `PreferencesPanel.tsx`: Look = Theme (System/Light/Dark), Low-stimulation look,
  Text size S/M/L, Reading spacing Standard/Wider, Density Comfortable/Compact, Reduce motion; About = roadmap ("What is coming
  later") and "What Demo source means". Applies instantly, saved to `tether.prefs.v1`; Reduce motion migrates from
  `tether.lecture.settings.v1` (never deleted).
- `layout.tsx`: `PrefsProvider` > `MockProvider` > `AuthProvider` > `AppShell`.
- `AppShell.test.tsx` (5 tests): names, `aria-current`, signed-out state, Preferences focus/apply/save/Escape/focus return,
  restore on load, axe (structure), and "the shell adds no live regions".

Deviations:
1. Preferences is one in-flow, non-modal panel under the top bar at every width (pushes content down; Escape or Close returns
   focus to the button). The doc asks for a side panel >= 1024 and a `/preferences` page on phones; I did not add a route
   (the brief said not to change routes). Not linkable as a result.
2. Preference changes are **not announced** through a status region. The switch/radio state change is already exposed by the
   control itself, and adding a status region to the shell would break the lecture's "exactly three live regions" rule
   (the shell is mounted on lecture routes too). Flag for adhd-ux-specialist.
3. On the live lecture screen the site header, rail and Preferences panel are hidden by CSS (`:has([data-lecture-root])`); the
   consent, loading and not-found states on the same route keep the site header (so Sign out and the wordmark stay there).

Results: lint 1 warning (baseline); typecheck clean; unit **591 passed**; harness e2e **32/32**; courses e2e first run 9/11
(the same two navigation waits as slice 0, right after a root-layout change in the long-lived dev server), rerun **11/11**.
The keyboard e2e (Tab from document start to "Course name" within 10 presses) passes with the new header + rail
(8 presses at 1280px). Note: plain `playwright.config.ts` runs wipe `test-results/`; my reuse configs now write to their own
subfolders so screenshots under `test-results/redesign-screens/` survive.

## Slice 3: sign-in, dashboard, course pages (DONE)
Changed: `SignInView.tsx`, `DashboardView.tsx`, `DashboardPage.tsx`, `CourseCard.tsx`, `AllCoursesPage.tsx`, `CourseDetailView.tsx`,
`CourseDetailPage.tsx`, `CourseForm.tsx`, `NewCoursePage.tsx`.
- Sign-in: centred card, serif h1, error text with an icon + words. Copy example 1 applied.
- Dashboard: date eyebrow, serif `Hi, {name}.`, Next step card (accent rule, one primary), "Your courses" as one divided list with
  code/term meta, "Add a course" beside the heading; "How a lecture works" (3 plain steps, no links, not a list so the course
  list stays the only list) in a right column from 1024px (8/4), below on phones. "Coming later" moved to Preferences > About
  (copy example 3). Static loading placeholder (no shimmer). Copy examples 2 and 4 applied.
- Course page: "<- Courses" link, serif h1 (still focused on arrival), success line with a check icon + words, Lecture card with
  the only primary "Start a lecture". "Back to dashboard" kept (frozen).
- Course form: card, "(required)" muted, options as a quiet button, error summary as a danger panel; Tab order unchanged.
- All courses: `CourseCard` takes `headingLevel` (h2 there) - fixes a pre-existing axe best-practice `heading-order` (h1 -> h3).

Results: lint 1 warning (baseline); typecheck clean; unit **591 passed**; courses e2e **11/11** (first run this time).
Screenshots + axe (wcag2a/aa, 21a/aa, 22aa, best-practice) for signin, dashboard-empty, dashboard, courses, course-new,
course-detail at 390 and 1280 in Paper, Night desk, Fog, Dusk: 48 screenshots, **0 violations** after the heading fix.

## Slice 4: lecture shell (DONE)
Changed: `StatusBar.tsx` (lecture bar), `Notices.tsx` (all panels, Stop dialog, settings popover, Demo notice), `LectureScreen.tsx`,
`ConsentStart.tsx`, `MicBlocked.tsx`, `StartFlow.tsx`, `LectureStartPage.tsx`, `LectureReopenPage.tsx`, `globals.css`.
- One sticky bar: the site header, rail and Preferences panel are hidden while `[data-lecture-root]` is mounted; the lecture
  uses the full width (`[data-shell]` padding removed) with its own 72rem content column.
- Recording pill: shape + word + elapsed in one pill; Recording = recording red on its tint, Paused = warning amber, Not recording
  = ink on surface with the slashed circle, Stopped = muted square. Never animated. Connection = dot/slashed dot + word.
- Pause/Resume and Stop keep their exact names and places; they are now secondary buttons with icons, so the only filled button
  in a Paused state is "Resume recording" in the panel (one primary per region).
- Panels (Paused, Mismatch, Offline, Stopped, Fatal, Audio skipped, action error) use the Panel stripe + icon + words; they sit
  directly under the bar. Stop dialog: "Keep recording/Keep paused" is now the filled (safe) button and keeps initial focus.
- `Lecture settings` (name kept: the harness test clicks that text) is a popover in the bar with Theme and Low-stimulation look,
  the lecture checkboxes, the shortcut list and "Help: Quiet". Its "Reduce motion" checkbox now writes the site-wide preference
  (one source of truth; the old lecture key still counts if it was on).
- Leave: a "Leave" button in the bar **only when the session is Stopped/Failed or a fatal panel shows**; hidden while Active,
  Paused or not yet known (Lead decision 1).
- The live screen now has exactly one h1 (`Lecture: {course}`, visually hidden; the bar carries the state). Course name comes
  from StartFlow, or the session title on reopen. This fixed the axe best-practice `page-has-heading-one`.
- Consent page restyled as a card (wording untouched, Draft tag kept, Start stays aria-disabled until checked).
  Mic blocked: card, icon, serif h1, browser help as a one-open-at-a-time list.

## Slice 5: transcript, TL;DR, I lost track, Focus mode (DONE, with one verification gap)
Changed: `Transcript.tsx`, `TldrPanel.tsx`, `LostTrackPanel.tsx`, `LectureScreen.tsx`, new `useWide.ts`, `globals.css`.
- Transcript: serif heading; lines as a hanging-timestamp grid (tabular figures), 68ch measure, 1.6 line height; gap and pause
  markers as centred hair-rule dividers (same text); partial row kept as one reserved, muted italic row; a cited line gets an
  accent tint when focused. Its viewport height now fits between the sticky bar and the dock (`100dvh - 22rem` phones,
  `100vh - 15rem` desktop, smaller in Focus mode) so follow-live never parks the newest line behind the dock.
- TL;DR: serif heading, larger one-line summary, quiet bullets, citation buttons, "Updated ..." as a small muted footer, hold
  control as a quiet button. New `collapsible` mode used on phones and in Focus mode: placed **above** the transcript, showing
  the heading and the one-line TL;DR with "More"/"Less" (aria-label "More of the TL;DR", aria-expanded) to expand in place. The
  Demo summary tag and summary status note are never collapsed away.
- I lost track: only the usual window is filled (primary); the other two are secondary. All three use `.btn-steady`, so a busy
  request no longer flashes them grey. Phone: three equal buttons in a bottom dock with safe-area padding; desktop: a sticky
  card in a 360px right rail. Result: serif "Here's where you are", larger "Right now", quiet disclosure buttons; "Got it, back to
  lecture" is secondary (one primary per region).
- Focus mode: `Focus` toggle (aria-pressed, remembered in `tether.prefs.v1`, never automatic) in the bar from 1024px: single
  centred 52rem column, TL;DR collapsed above the transcript, I lost track becomes a centred bottom dock, bar unchanged. No opacity
  or filter is used anywhere (tested). On phones the layout is already single-column, so the toggle is not shown there (deviation).

New tests: `LectureRedesign.test.tsx` (6: one h1, frozen bar names, Leave rule for Active/Paused/Stopped, Focus toggle incl. live
region count and axe, dock button hierarchy) and `e2e-harness/redesign.spec.ts` (7 browser tests: phone dock in view while
scrolling + skip link + >= 56px, newest line not behind the dock, focused control not covered by bar or dock, Focus mode pressed /
remembered on reload / axe, Focus never dims via opacity or filter, one h1 + pill visible + no horizontal scroll at 320px in all four
looks, Low-stimulation look has no transitions or shadows under the lecture root).

**Verification gap (environment, not code) - real-stack lecture e2e could not run.** `e2e/lecture.spec.ts` is serial; its first
test waits for the microphone and `getUserMedia` never resolves on this machine's automation Chromium, so the other 8 tests are
skipped ("did not run"). I proved this is not the redesign: a throwaway probe calling `getUserMedia({audio:true})` on a static file
(`/mockServiceWorker.js`, no app code) timed out in a fresh context, with the fake-device flags, with `grantPermissions(['microphone'])`
and with the full Chromium build (devices do enumerate). Likely a macOS-level audio/permission block for the automation browser.
I did not pull a Linux browser image (no network beyond npm/fonts). **Correction**: in my first run I misread the output as 8 passed;
they had not run. The phone I lost track acceptance is instead covered by the harness tests above (fake mic). Someone must run
`npx playwright test -c playwright.reuse.config.ts e2e/lecture.spec.ts` on a machine where Chromium's fake microphone works.

axe finding escalated, NOT fixed: `label-content-name-mismatch` (WCAG 2.5.3 Label in Name, Level A) on the I lost track window
buttons (visible "Last 5 min (your usual)" vs frozen name "Catch up on the last 5 minutes, your usual choice") and on the catch-up
citation buttons (visible "00:01" vs name "From 0 minutes 1 second, go to transcript line"). Both names are specified by the design
doc and pinned by unit/e2e tests (frozen list), so changing them needs a Lead/UX decision. A voice-control user saying "click Last 5
min" may not activate the button. Suggested fix: name = visible text first, e.g. "Last 5 min, catch up (your usual choice)" and
"00:01, go to transcript line"; update the frozen list and tests in the same change. The screenshot runner records this rule
separately as `knownEscalated` and never hides it.

Results (end of slice 5): lint 1 warning (baseline); typecheck clean; unit **597 passed**; harness e2e **32/32 + 7/7 new**;
courses e2e 11/11 (earlier in the slice); lecture e2e **not runnable here** (see gap).

## Slice 7: command palette and course search (DONE; done before slice 6)
Changed / added: `CommandPalette.tsx` (+ `CommandPalette.test.tsx`), `AppShell.tsx`, `AllCoursesPage.tsx`, `AppShell.test.tsx` (+2 tests).
- "Search or jump" button (from 640px; inside the Menu on phones) and Ctrl/Cmd+K (the only default-on shortcut; it has the visible
  button equivalent). Ignored while a live lecture screen is mounted.
- Modal combobox + listbox: focus stays in the input (Tab does nothing), arrows move, Enter runs, Escape closes and focus returns
  to the opener; active option = left accent bar + tint + bold (not colour alone; the tint alone is invisible in Night desk).
  Result count in its own status line, debounced 500 ms, only while the palette is open (so the lecture's three-region rule holds).
- Commands: Today, All courses, Add a course, each course, "Start screen for {course}" (navigates to the **consent page only**),
  Open Preferences, light/dark/system theme, low-stimulation look on/off, Focus mode default on/off. There is **no** command
  that starts, pauses, resumes or stops recording (unit-tested).
- All courses: "Search your courses" field (every word must match name, code, term or instructor), "N of M match." helper text,
  "No course matches that. Try fewer words." Not a live region.

Results: unit **603 passed** at the end of the slice; courses e2e 9/11 then **11/11** on rerun (same post-edit pattern); the
keyboard e2e still reaches "Course name" within 10 Tabs (now 8 at 1280px: skip link, wordmark, Search or jump, Preferences,
Sign out, Today, Courses, field). Screenshots `palette` + axe clean.

## Slice 6: copy pass (PARTIAL, deliberately narrow)
- Added `components/lecture/copy.test.ts`: `copy.ts` claimed a copy lint existed but none did. It walks every string in `copy.ts`
  (calling templated ones) and forbids exclamation marks, emoji, "Oops/Awesome/Don't worry", minimising "just" ("just now" is
  allowed), ADHD/focus problems/distracted/lazy, behind/overdue/missed/streak, lost focus/inactive/idle/away, and the dishonest
  offline phrases from m2 section 6; plus no sentence over 30 words. All current copy passes.
- Applied doc examples 1-5 and 10 (1-4 in slice 3). Example 5: "How far back? The lecture keeps going." Example 10: "Words will
  appear here once the lecture starts."
- **Not applied** (each would require editing an existing unit test that pins today's wording; I chose not to edit tests tonight):
  example 6 (recap slow, pinned in `LostTrackPanel.test.tsx` "taking longer than expected"), 7 (budget, pinned "Summaries are paused
  for this lecture."), 8 (mismatch, pinned "We turned the microphone off to be safe."). The doc allows these with test updates in the
  same change; they are small and can follow with a UX review. No frozen string was changed. Consent and recording copy unchanged (G1).

## Slices 8 and 9: NOT STARTED
- 8 (review/notes shell with tabs, per-lecture search, export placeholder) needs a real notes/review screen; `/lectures/[id]` today
  reopens the live screen. Not attempted tonight.
- 9 (syllabus review and schedule shells) waits for those routes to exist in the web app.

## Final verification (end of session, 2026-10-29)
| Check | Command | Result |
|---|---|---|
| Lint | `npm run lint` (src/web) | 0 errors, 1 warning (the baseline `_label` in e2e/lecture.spec.ts) |
| Typecheck | `npm run typecheck` | clean |
| Unit | container command above | **612 passed** (baseline 219; +393 new, mostly the contrast matrix). `schemas.test.ts` file error unchanged from baseline |
| Harness e2e (fake mic, no API) | `npx playwright test -c playwright.harness.reuse.config.ts` (with the 3100 -> 3000 forwarder) | **39 passed** (32 existing + 7 new) |
| Courses e2e (real API) | `npx playwright test -c playwright.reuse.config.ts e2e/courses.spec.ts` | **11 passed** |
| Lecture e2e (real API + mic) | `npx playwright test -c playwright.reuse.config.ts e2e/lecture.spec.ts` | **1 failed, 8 did not run**: environment, getUserMedia never resolves here (see slice 5). NOT verified. |
| Screens + axe | `npx playwright test -c playwright.screens.config.ts` | 18 screens x 390/1280 x Paper/Night desk/Fog/Dusk = **144 captures, 0 axe violations** (wcag2a/aa, 21a/aa, 22aa, best-practice), **0 horizontal overflow at 320px**; `label-content-name-mismatch` recorded on 16 captures (lecture-lost-track, lecture-focus): escalated, see slice 5 |

Screenshots: `src/web/test-results/redesign-screens/` (gitignored): 176 PNGs (`{screen}--{width}--{look}.png`, plus
`--viewport.png` phone captures for lecture screens because full-page captures misplace the fixed dock) and `axe-report.json`.
Note: any run of the plain `playwright.config.ts` wipes `test-results/` including these.

Unit-test edits: **none of the pre-existing tests were edited.** New test files only: `lib/design/tokens.test.ts`, `lib/design/prefs.test.ts`,
`components/ui/primitives.test.tsx`, `components/AppShell.test.tsx`, `components/CommandPalette.test.tsx`,
`components/lecture/LectureRedesign.test.tsx`, `components/lecture/copy.test.ts`, `e2e-harness/redesign.spec.ts`.

### What is shippable for the demo
Slices 0-5 and 7, slice 6 partially: tokens and four looks, fonts, Preferences, shell, sign-in, dashboard, course pages, consent,
mic-blocked, the full live lecture screen (bar, panels, transcript, TL;DR, I lost track, Focus mode), command palette and course
search. Everything is mock/demo-backed as before (Demo source and Demo summary labels are untouched and visible).

### What is not
- Slice 8 (review/notes tabs, export placeholder) and slice 9 (syllabus/schedule shells): not started.
- Copy examples 6-8 (need test updates), OpenDyslexic (deferred: no vetted package), Preferences as a linkable page on phones.
- Real-stack lecture e2e not run on this machine (environment). Needs a run where Chromium's fake microphone works.
- WCAG 2.5.3 label-in-name on I lost track window and citation buttons: escalated, frozen names.
- Known limitation: on desktop a long catch-up result scrolls inside the sticky I lost track card, so "Got it" may need a scroll
  inside that card (its heading takes focus on open).
- No screen-reader, real-device, forced-colors-on-Windows, 400%-zoom-with-real-content or human testing. G4 NOT_RUN; G1 BLOCKED.
  Nothing here is evidence that the design helps any student.

### Demo commands
1. API (already running for this session): `cd /Users/jamie/Dev/AIStudentNotesApp && docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d api`
2. Web: `cd /Users/jamie/Dev/AIStudentNotesApp/src/web && npm ci && npm run dev:real` (a dev:real server was already on :3000 during
   this session; if so, skip this step). Open http://localhost:3000/signin and sign in with any `name@school.edu`-style email.
3. Walk: dashboard -> Preferences (try Dark and Low-stimulation look) -> Ctrl+K "bio" -> course -> Start a lecture -> consent ->
   Start recording (browser asks for the microphone; the transcript is the backend's replayed demo script, not your voice) ->
   Focus -> I lost track -> Pause/Resume -> Stop.
4. Without the API or a microphone: http://localhost:3000/dev/lecture-harness/live-lost-track (and the other fixtures listed at
   /dev/lecture-harness), and http://localhost:3000/dev/ui-kit. Dev-only routes; 404 in production builds.

---
# Round 2 (Lead requests + docs/ux/redesign-review.md), 2026-10-29

## R2-1. WCAG 2.5.3 Label in Name: FIXED (Lead authorised changing these frozen names only)
New accessible names (visible text now starts the name):
| Control | Visible text | Old name | New name |
|---|---|---|---|
| I lost track window | `Last 2 min` | `Catch up on the last 2 minutes` | `Last 2 min: catch up on the last 2 minutes` |
| (usual window) | `Last 5 min (your usual)` | `Catch up on the last 5 minutes, your usual choice` | `Last 5 min (your usual): catch up on the last 5 minutes` |
| Catch-up citation | `00:09` | `From 0 minutes 9 seconds, go to transcript line` | `00:09, go to transcript line` |
Files: `components/lecture/copy.ts` (`windowLabel`, `windowUsualLabel`), `LostTrackPanel.tsx` (citation label).
The review's M2 proposed slightly different wording ("Last 5 minutes, your usual, catch up", visible "minutes" spelled out);
I followed the Lead's explicit names. Spelling out "minutes" in the visible text is left as an open UX item.
Tests changed because they pinned exactly these names (no other assertion changed):
- `src/components/lecture/LostTrackPanel.test.tsx`: `press()` regex, the three "count is 3" / "5 min" / "10 min" regexes,
  the aria-disabled check, the usual-choice focus checks (`/your usual choice/` -> `/\(your usual\)/`), the three exact-name
  label tests (lines ~307-311), and the citation regex `^From .* go to transcript line$` -> `^\d\d:\d\d, go to transcript line$`.
- `e2e/lecture.spec.ts`: line ~205 (`/^Catch up on the last/` count) and line ~273 (usual-choice regex). Line ~191
  (`catchUpButton`) was NOT edited: Playwright's default name match is a case-insensitive substring, so it still matches.
- My own new tests: `LectureRedesign.test.tsx`, `e2e-harness/redesign.spec.ts`, `e2e-screens/screens.spec.ts`.
Regression guard: `e2e-harness/redesign.spec.ts` now runs, on 8 fixtures (live-lost-track with a result open, live, paused,
stopped, disconnected, mismatch, consent, mic-denied), a custom check that every control with aria-label/labelledby contains
its visible text, plus axe `label-content-name-mismatch`. **Mutation check**: with the old window name restored, the test failed;
with the new name, it passed. The screenshot runner no longer exempts this rule (0 exemptions).
`design-direction.md` section 8 (frozen list) still shows the old names: I may not edit that doc; the Lead or UX should update it.

## R2-2. Desktop catch-up result no longer scrolls inside the sticky card: FIXED
While a result is open, `#lecture-lost-track` (desktop, not docked) becomes `position: static` with no max height, so the whole
result and "Got it" are reached by normal page scroll. Same DOM and focus behaviour; class list unchanged (the unit test that
checks `fixed` / `lg:sticky` is untouched). Plus review M1's top-row "Back to lecture" (below).

## R2-3. Review fixes applied
- **M1** Phone sheet capped at `60dvh` (was 70vh); "Back to lecture" (quiet button, same action as "Got it") in the result's top
  row, beside "Here's where you are"; the bottom "Got it, back to lecture" stays.
- **M2** see R2-1.
- **M3** Mismatch heading: the text "! " removed; the warning triangle icon (aria-hidden) remains. No other panel had it.
- **M4/M5** Phone lecture bar: Demo tag hidden below 640px (the Demo notice heading still says "Demo source: these words are not
  from your microphone"; tag kept at 640px+ where the e2e checks it), tighter padding, elapsed now "N min" so the pill and the
  connection word share one row, "Not available right now" is screen-reader-only on phones (still the disabled button's
  description) and visible from 640px. Result: 2 rows, about 112px, at 390px (`lecture-live--390--paper--viewport.png`).
  While a safety panel (paused, mismatch, stopped, fatal, offline) is showing, the Demo notice collapses its body behind
  "Details"; its heading stays visible.
- **S1** Citations are link-style timestamps (accent, underlined, tabular, 44px hit area) in a hanging left column for
  "Right now", the short recap bullets and detail; inline link style elsewhere.
- **S2** Stopped/Failed: the bar's "Leave" is removed entirely (the panels already offer "Back to course"; this supersedes the
  earlier Leave-when-stopped rule), Focus toggle hidden, and "Pause updates" hidden (unless updates were already paused).
- **S3** Mic-blocked help rows: chevron + "How to turn it on in Chrome/Safari/Firefox" as underlined accent summaries.
- **S4** Palette: "Open the lecture screen for {course} (asks first)".
- **S5** Next step card shows its basis ("Most recently used" / "Most recently added") above the course name; "How a lecture
  works" is a disclosure, open until any course has a lecture, then closed; the student's own choice is remembered on the device.
- **S8** Recording time shows whole minutes by default ("0 min", "12 min"); "Show seconds in the recording time" checkbox in
  Lecture settings (stored in `tether.prefs.v1` as `showSeconds`). `elapsed` test id unchanged.
- **Ruling 2** Preferences confirmation: a `role="status"` line inside the open Preferences panel only ("Dark theme on.",
  "Reduce motion on.", ...; identical text within 10 s dropped), also visible. It is not mounted when the panel is closed and the
  panel is never shown on the lecture screen; the lecture "exactly three live regions" unit test still passes.
- **Ruling 3** Lecture settings on phones says "Focus mode is for wider screens. On a phone the layout is already one column."
- **Ruling 5 / copy 6-8** applied: 6 "Still writing your recap. Here is what was said so far."; 7 (review wording) "Recaps are
  paused for this lecture. Here is what was said."; 8 mismatch body "The app and your microphone disagree, so the microphone is off.
  Nothing is being recorded. Choose Check again." Tests edited only for the pinned wording:
  `LostTrackPanel.test.tsx` ("taking longer than expected" -> new 6; "Summaries are paused for this lecture." -> "Recaps are paused
  for this lecture."), `LectureScreen.test.tsx` ("We turned the microphone off to be safe." -> "The app and your microphone
  disagree, so the microphone is off."). Copy lint passes.
- **Question A** (why `lecture-live` had no I lost track): a **harness fixture artifact**. The fixture predated the AI routes and
  passed no `ai`; the real runtime always provides them (`realRuntime.ts`, `ai: createRealAi(...)`). Fixed by giving the fixture
  the same deterministic fake AI. In the real app the panel is only absent if the runtime has no AI routes, which does not occur today.
- Extra: an unlayered `[hidden] { display: none !important }` so the `hidden` attribute always beats display utilities.

Not applied, left open (with reason):
- Consent bullet reorder (S6): **not done**: consent wording/order is gate G1; needs Product Owner + counsel.
- Mismatch `bodyMicOff` still ends "Reload the state to continue." and the offline banner still reads "Trying to reconnect.
  Trying again..." (review M4e): recording-status text not on the Lead's list; needs a wording decision.
- Nice-to-haves N1-N8, Preferences as a right-side panel from 1024px, renaming "Lecture settings" to "Preferences" (later, per ruling 4).

## R2-4. Slice 8 (narrow) and slice 9
- Slice 8, finished lectures only (`kind === "Stopped"`): "Search this lecture" field above the transcript (client-side filter of
  lines already loaded; every word must match; markers hidden while searching; "N of M lines match."; "No line matches that.
  Try fewer words.") and an "Export to Google Docs" button that is aria-disabled with the description "Not built yet. Nothing is
  sent anywhere. When it exists it will ask you first." No API calls added. Not done: Notes / Transcript / TL;DR-history tabs,
  because there is no notes data or notes API in the web app yet; adding tabs with nothing behind them would be a teaser.
- Slice 9: **not applicable yet**: the web app has no syllabus or schedule routes; building them means new routes and API calls,
  which this brief excludes.

## R2-5. Real-stack lecture e2e in a Linux container: NOT DONE, left for qa-engineer
No Playwright/browser image is present locally (only `node:22`, dotnet sdk, postgres). Running it in `node:22` would need
`playwright install --with-deps` (apt packages plus a browser download) and the stack reachable under an Origin the API accepts
(`http://localhost:3000`); the apt/image network pulls are outside what this brief allows. On the host it still fails the same way
(1 failed on the microphone wait, 8 did not run). qa-engineer should run `e2e/lecture.spec.ts` in the Playwright Linux image with
host networking, or on a machine where Chromium's fake microphone works.

## Round 2 final results
| Check | Result |
|---|---|
| Lint | 0 errors, 1 warning (baseline) |
| Typecheck | clean |
| Unit (container) | **616 passed** (schemas.test.ts file error unchanged from baseline) |
| Harness e2e | **47 passed** (32 original + 7 redesign + 8 label-in-name) |
| Courses e2e | **11 passed** (one earlier run in this round: 9/11 with the usual first-run-after-edit navigation waits; rerun 11/11) |
| Lecture e2e | 1 failed, 8 did not run: environment (microphone), unchanged |
| Screens + axe | 144 captures (18 screens x 2 widths x 4 looks) + 32 phone viewport shots = 176 PNGs, **0 violations, 0 exemptions**, no 320px overflow |

### Demo steps (unchanged commands)
1. API: `cd /Users/jamie/Dev/AIStudentNotesApp && docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d api`
2. Web: `cd /Users/jamie/Dev/AIStudentNotesApp/src/web && npm run dev:real` (skip if :3000 is already up), open http://localhost:3000/signin.
3. Dashboard -> Preferences (Dark, Low-stimulation; watch the confirmation line) -> Ctrl+K "bio" -> course -> Start a lecture ->
   consent -> Start recording (the transcript is the backend's replayed demo script) -> Focus -> I lost track (try the top-row
   "Back to lecture") -> Pause/Resume -> Stop -> "Search this lecture".
4. No API/mic: http://localhost:3000/dev/lecture-harness/live-lost-track (and the other fixtures), http://localhost:3000/dev/ui-kit.
   (Renamed on 2026-10-29: now `/dev/lecture-harness/live-quick-recap`; see the next section.)

## Quick recap rename (2026-10-29, Product Owner rule)
The feature formerly called "I lost track" is now **Quick recap** everywhere in `src/web`, including code. Clean rename, no alias
(nothing is deployed). Sections above this one are history and keep the old name.

**What changed (src/web only)**
- Files: `LostTrackPanel.tsx` -> `QuickRecapPanel.tsx`, `LostTrackPanel.test.tsx` -> `QuickRecapPanel.test.tsx`,
  `useLostTrack.ts` -> `useQuickRecap.ts` (plain `mv`, nothing staged).
- Identifiers: `LostTrackPanel`/`LostTrackPanelProps`/`focusLostTrack`/`useLostTrack`/`UseLostTrackOptions`/`LostTrackErrorKind`/
  `lostTrackErrorKind` -> `QuickRecap*`/`quickRecap*`; `LOST_TRACK_COPY` -> `QUICK_RECAP_COPY`; `skipToLostTrack` -> `skipToQuickRecap`;
  port/client methods `startLostTrack` -> `startQuickRecap`, `postLostTrack` -> `postQuickRecap`.
- Wire: client route `POST /api/v1/sessions/{id}/lost-track` -> `/quick-recap` (client.ts, MSW handler, aiClient tests, lecture e2e
  route matchers). The request body and response shapes are unchanged (no `lostTrack*` JSON fields existed in the client).
- DOM ids / test ids: `#lecture-lost-track` -> `#lecture-quick-recap` (skip link href, CSS selector); `lt-*` ids and test ids
  (`lt-h2`, `lt-result-h3`, `lt-said`, `lt-more`, `lt-usual`, `lt-working`, `lt-error`) -> `qr-*`; local variable `lt` -> `qr`.
- Storage key: `tether.lecture.lostTrackWindow.v1` -> `tether.lecture.quickRecapWindow.v1` (no migration: nothing deployed; a
  dev browser falls back to the default 5 min once).
- Harness fixture `live-lost-track` -> `live-quick-recap`; screenshot name `lecture-lost-track` -> `lecture-quick-recap`.
- Visible copy: heading "Quick recap"; skip link "Skip to Quick recap"; shortcut help "Alt+L goes to Quick recap."; dashboard
  "How a lecture works" step 3 now reads "Want a recap? Press Quick recap." / "You get the last 2, 5 or 10 minutes back. The
  lecture keeps going." Comments in globals.css, ports.ts, settings.ts, client.ts, aiPayloads.ts, prefs.ts and fixtures updated.
- Label in Name (WCAG 2.5.3): the window buttons' names never contained the feature name ("Last 5 min (your usual): catch up on the
  last 5 minutes"), so they are unchanged and visible text still starts each name; the panel heading and skip link are plain text.
- New guard: `copy.test.ts` FORBIDDEN list gained `["old recovery name", /\b(lost track|lost the thread)\b/i]` (+1 unit test).

**Tests edited (only to follow the rename; no assertion weakened)**
- `src/components/lecture/QuickRecapPanel.test.tsx` (renamed file): describe names, heading name, skip-link name, `startQuickRecap`
  mocks, `#lecture-quick-recap`, `qr-working`/`qr-error` test ids, storage key.
- `src/components/lecture/LectureRedesign.test.tsx`: two test names, `startQuickRecap` in the fake AI.
- `src/components/lecture/copy.test.ts`: one new forbidden pattern (added, not edited).
- `src/lib/api/aiClient.test.ts`: test name, MSW routes `/quick-recap`, `postQuickRecap`/`startQuickRecap`.
- `e2e-harness/redesign.spec.ts`: test name, fixture URL, skip-link name, `#lecture-quick-recap`, label-in-name loop id.
- `e2e-screens/screens.spec.ts`: screen name and fixture URLs, two comments.
- `e2e/lecture.spec.ts`: three test names, route regexes `/quick-recap$`, course name prefix, skip-link name, `qr-error` test id.

**Proof**
- Before: `grep -rniE "lost[-_ ]?track|lost the thread"` over src/web (excluding node_modules, .next, test-results, e2e/.auth): 120
  lines in 22 files. After: **0**. Remaining unrelated "lost": `e2e/users.ts` `lostSession` and courses e2e "lost session (401)"
  (signed-out session, not the feature), fixture id `paused-mic-lost` and `ANNOUNCE` key `mic-lost` (device, internal only).
- Old harness URL `/dev/lecture-harness/live-lost-track` now renders "Unknown fixture."; the new one renders the panel.

**Results (2026-10-29)**
| Check | Result |
|---|---|
| Lint | 0 errors, 1 warning (baseline: unused `_label` in e2e/lecture.spec.ts) |
| Typecheck | clean |
| Unit (container, command as briefed) | **617 passed** (616 + 1 new guard); `schemas.test.ts` file error unchanged (it reads `../shared`, which that command does not mount) |
| Harness e2e (reuse config via a 3100->3000 forwarder) | **47 passed** |
| Courses e2e (reuse config) | **11 passed** |
| Screens + axe | 144 captures + 32 phone viewport shots = 176 PNGs, **0 violations** |
| Lecture e2e (real stack) | NOT RUN: environment (microphone), and the API route only matches once the backend rename lands |

Side note, not caused by this change: mounting `src/shared` read-only into the container makes `schemas.test.ts` load, and then
369 of its 486 cases fail with "Unknown lecture.v1 schema: syllabus*" (the web validator registry only knows lecture.v1 schemas but
the shared fixtures folder now also holds syllabus/calendar fixtures). The `quick-recap-request` fixtures pass. Owner: Lead /
backend (contracts) to decide whether that test should filter fixtures by registered schema.

**Open questions for the Lead**
- Alt+L shortcut: kept (behaviour change not in scope), but "L" was the mnemonic for "lost". Options: keep, or move to Alt+R
  ("recap"). Shortcuts are off by default and untested with screen readers.
- `QUICK_RECAP_COPY.windowLabel` "catch up on the last N minutes" and `invalid_state` "nothing new to catch up on": kept (listed below).

### Other language to review (NOT changed; for the adhd-ux-specialist language guide)
Recording/consent/Demo-source text is excluded (gate G1). No exclamation marks and no "you should" found in student-facing strings.
| Where | String | Why it may need review |
|---|---|---|
| `components/lecture/copy.ts` QUICK_RECAP_COPY.windowLabel / windowUsualLabel | "...: catch up on the last N minutes" (accessible name) | "catch up" can imply being behind; part of the 2.5.3 name, change together with the label-in-name test |
| copy.ts QUICK_RECAP_COPY.coversShort / coversShortBrief | "You have only been here N minutes. Here is all of it." / "You have only been here a short while." | "only been here" can read as a remark on arriving late |
| copy.ts QUICK_RECAP_COPY.resultHeading | "Here's where you are" | mildly implies the student was lost; probably fine |
| copy.ts QUICK_RECAP_COPY.errors.invalid_state | "This lecture has ended, so there is nothing new to catch up on." | "catch up" |
| copy.ts QUICK_RECAP_COPY.errors rate_limited / network / generic | "...Nothing was lost." | "lost" (reassurance about data, not the student); consistent phrasing |
| `AllCoursesPage.tsx:42`, `DashboardView.tsx:104`, `DashboardPage.tsx:22`, `CourseDetailPage.tsx:47` | "Nothing is lost. Try again when you are ready." / "...in a moment." | same "lost" reassurance |
| copy.ts SUMMARY_STATUS_COPY.delayed | "The summary is catching up." | about the system, low risk |
| copy.ts SUMMARY_STATUS_COPY.failed; `lib/lecture/indicator.ts:73` | "could not update just now"; "Recording ended unexpectedly" | system failure wording, not addressed to the student |
| copy.ts (session notices) signedOutBody / unavailableBody / skipped | "...the last moments may be missing" | system data gap, not blame; recording-adjacent, so possibly gate G1 |
| `PreferencesPanel.tsx:80` sample sentence | "the lecture keeps going while you catch up." | "catch up" |
| `CourseForm.tsx:183` | "You already have a course with this name. You can still save it." | neutral; listed for completeness |

---
# Round 3: language guide (docs/ux/language-guide.md section 8.1, A1-A30), 2026-10-29

Base: the working tree already contained the "Quick recap" rename made by someone else (`QuickRecapPanel.tsx`,
`useQuickRecap.ts`, `QuickRecapPanel.test.tsx`, heading "Quick recap", skip link "Skip to Quick recap", `qr-*` test ids). I built
on it and did not revert anything. A1, A2 (first line) and A7 were already done there.

## Applied (file: string before -> after)
All in `src/web/src/components/lecture/copy.ts` unless noted.
- A2 `DashboardView.tsx`: "You get the last 2, 5 or 10 minutes back." -> "It shows what was said in the last 2, 5 or 10 minutes. The lecture keeps going."
- A3 `resultHeading` "Here's where you are" -> "Here is what was said"; guide section 4: `said` "What was said" -> "Word for word" (so it does not repeat the heading); `pending` -> "A short recap is being written. The words themselves are below."
- A4/A27 `rightNow` "Right now" -> "Most recent"; mock text `mocks/handlers.ts` and `harness/fixtures.tsx`: "Right now: ..." -> "Most recent: ..."
- A5 `PreferencesPanel.tsx`: "...while you catch up." -> "...while you read."
- A6 window names: "Last N min: catch up on the last N minutes" -> "Last N min: recap of the last N minutes" (usual: "Last N min (your usual): recap of the last N minutes"). Visible text still starts the name; the label-in-name browser check passes.
- A8 + Lead decision: shortcut **Alt+L -> Alt+R** (`LectureScreen.tsx` `KeyL` -> `KeyR`), help text "Alt+R goes to Quick recap.", comment in `QuickRecapPanel.tsx`. Still OFF by default; conflict check with NVDA/JAWS/VoiceOver not run.
- A9 "You have only been here N minutes." -> "The lecture has run N minutes. Here is all of it."
- A10 **deviation**: the guide proposes "The lecture has only just begun. Here is all of it." but its own checklist (section 9 item 2) and our copy lint ban "just"; used "The lecture began less than a minute ago. Here is all of it."
- A11 "Earlier catch-up, last N min" -> "Earlier recap, last N min"
- A12 "Got it, back to lecture" -> "Back to lecture". The round-2 top-row button (previously "Back to lecture") is now **"Close recap"** so the two buttons have different names; same action.
- A13 "Worth knowing" -> "Also mentioned"
- A14 budget -> "A recap is not available for this lecture. Here is what was said." (replaces round 2's "Recaps are paused...")
- A15/A16 errors: "Nothing was lost." -> "Nothing was changed." (rate limit, network, generic); "when you are ready" -> "when you like"; `invalid_state` "nothing new to catch up on" -> "nothing new to recap" (keeps the pinned "This lecture has ended").
- A17 `DashboardPage.tsx`, `AllCoursesPage.tsx`, `DashboardView.tsx`: "Nothing is lost. Try again when you are ready." -> "Nothing was deleted. Try again when you like."
- A18 `CourseDetailPage.tsx`: "Nothing is lost. Try again in a moment." -> "Nothing was deleted. Try again in a moment."
- A19 `delayed` -> "The summary is updating more slowly than usual. The transcript is not affected."
- A20 kept ("Still writing your recap...").
- A21 mismatch heading and announcement "Something doesn't match" -> "The app and your microphone disagree" (announcement: "The app and your microphone disagree, so the microphone is off.").
- A22 `bodyMicOff` "Reload the state to continue." -> "Choose Check again."
- A23 offline `retrying` "Trying to reconnect. Trying again..." -> "Trying to reconnect." (the banner sentence "Not connected. Audio is not being saved." is untouched).
- A24 `helpQuiet` "Help: Quiet" -> "Prompts: Quiet" (and a comment in `lib/design/prefs.ts`).
- A25 `AppShell.tsx` palette keywords "focus distraction" -> "focus fewer panels".
- A26 `lib/api/client.ts`: fallback `Request failed (${status}).` -> "That did not work. Nothing was changed." (no status code can reach the UI).
- A28 `mocks/handlers.ts`: "Catch-up not found." -> "Recap not found."
- A29 kept. A30 `MockProvider.tsx`: "Loading..." -> "Getting things ready."
- Guide checklist "right now" (not in the A-list, same rule): `unavailable` recap notice "Summary isn't available right now. Here's what was said." -> "A recap is not available at the moment. Here is what was said."; status-bar reason "Not available right now" -> "Not available at the moment"; `useCourses.ts` / `DashboardPage.tsx` "We can't reach the server right now." -> "...at the moment."
- Copy lint (`copy.test.ts`) extended: forbids "right now", "Nothing is/was lost", "Here's where you are", "You have only been here", "catch up on", and raw error text ("Request failed", "status code", "(500)"-style codes).

## Tests edited (copy changed with them, as the Lead allowed)
- `src/components/lecture/QuickRecapPanel.test.tsx`: test data "Right now: limits." -> "Most recent: limits."; every `catch up on the last` name pattern -> `recap of the last`; heading "Here's where you are" -> "Here is what was said"; Unavailable/BudgetLimited notice texts; generic error "Nothing was lost." -> "Nothing was changed."; "Got it, back to lecture" -> "Back to lecture" (and that test's title); Alt+L -> Alt+R (two test titles and the key in both keyboard tests); "You have only been here 3 minutes..." -> "The lecture has run 3 minutes. Here is all of it."; "Earlier catch-up" -> "Earlier recap"; Delayed text.
- `src/components/lecture/LectureScreen.test.tsx`: "Help: Quiet" -> "Prompts: Quiet" (assertion and title); region name / alert prefix "Something doesn't match" -> "The app and your microphone disagree" (6 places).
- `src/components/lecture/LectureRedesign.test.tsx`: window-name regexes.
- `e2e/lecture.spec.ts`: `catchUpButton` name (line ~191) -> `recap of the last N minutes`; count regex (~205); result heading (~209); "Back to lecture" (~221); usual-window regex (~273).
- `e2e-harness/redesign.spec.ts`, `e2e-screens/screens.spec.ts`: window regexes and result heading.
- `src/lib/lecture/schemas.test.ts` (see below).

## Gate G1: listed, NOT touched
`CONSENT_COPY` (incl. "Draft" bullets), `PAUSED_COPY` (incl. "Will stop on its own after 30 min paused"), `STOPPED_COPY`,
`BLOCKED_COPY` (incl. "could not confirm that nothing was saved"), `FATAL_COPY` ("the last moments may be missing"),
`AUDIO_NOTE_COPY` ("may be missing from the transcript"), `DEMO_NOTICE_COPY`, `DEMO_SOURCE_TAG`, `DEMO_SUMMARY_COPY`,
the offline banner sentence "Not connected. Audio is not being saved.", recording-state `ANNOUNCE` lines (started, paused,
resumed, back online, mic stopped, auto-paused/stopped, signed out, unavailable). `ConsentStart.tsx` and `MicBlocked.tsx` text.
Note: A21-A23 (mismatch heading/announcement, mic-off body, reconnect line) are microphone-state notices; I applied them because
they are in the A-list you asked for and are outside the guide's own G1 list (section 8.4). Revert them if you consider them G1.

## schemas.test.ts now runs with src/shared mounted
The shared `fixtures/` folder also holds syllabus.v1 and calendar.v1 fixtures, which the web (lecture.v1) validator does not
register. `schemas.node.ts` gained `otherContractSchemaNames()`; the test now runs the fixtures of registered lecture schemas and
adds a check that **every** fixture belongs either to lecture.v1 or to another family's schema file, so a mistyped lecture
fixture cannot be skipped silently. Run with `-v src/shared:/repo/src/shared:ro` (its real relative path `../shared` from
`src/web`): **118 passed**. Without the mount it still cannot load (same as baseline).

## Round 3 results
| Check | Result |
|---|---|
| Lint | 0 errors, 1 warning (baseline) |
| Typecheck | clean |
| Unit, src/web only (Lead's form) | 619 passed; schemas.test.ts file error (needs src/shared) |
| Unit, with src/shared mounted read-only | **25 files, 737 passed, 0 failed** |
| Harness e2e | **47 passed** |
| Courses e2e | **11 passed** (first run) |
| Screens + axe | 18 screens (lecture-live-lost-track is now `lecture-quick-recap`) x 2 widths x 4 looks = **144 captures, 0 violations, 0 exemptions**; 176 PNGs |
| Lecture e2e | not re-run this round (unchanged environment block, see round 2) |

Demo steps: unchanged from round 2, except the shortcut is now Alt+R (off by default, Lecture settings) and the result's exits are
"Close recap" (top) and "Back to lecture" (bottom).
