# Themes (BL-001) progress

Owner: frontend-engineer. Spec: docs/ux/themes-spec.md. Date: 2026-10-29. Scope: `src/web` only. Nothing is committed.
G4 stays NOT_RUN (no human, screen-reader or device testing). G1 stays BLOCKED: no recording, consent, Demo-source or
"may be missing" wording was touched.
Lead decisions applied: Q-PO1 label "Course portal" (code id `portal`, no third-party name or asset anywhere);
Q-PO2 all four skins planned, Paper stays the default; Q-LEAD1 44px controls; Q-LEAD2 no per-theme theme-color meta.

Status: **T0 DONE and green. T5 DONE (slice 8, 2026-10-31). T1 IMPLEMENTED 2026-10-30 as redesign-round2 slice 1** (results, test changes and
deviations recorded in `docs/ux/redesign-round2.md` under "Slice 1 results"; not committed). **Redesign-round2 slice 2
(primitives v2) IMPLEMENTED 2026-10-31** ("Slice 2 results" in the same document): A5 (Preferences switch-row insets,
`.hover-row`) and Segmented equal option widths are done there; the `.tag` padding stays (consent Draft tag, gate G1) and
the 48px consent buttons stay (G1). **Redesign-round2 slice 3 (lecture screen) IMPLEMENTED 2026-10-31** ("Slice 3 results"
in the same document): transcript as the sheet with the 5rem margin track for timestamps from 1280, the Quick recap result
as a sheet above the transcript on desktop, the rail as a ledger, safety panels as notes, serif synthesis line; the
landmark-rect gate and the lecture harness were extended and stay green; the Demo-source notice and tag are unchanged (G1,
PO deferral). **Redesign-round2 slice 4 (Today, course page, forms, sign-in) IMPLEMENTED 2026-10-31** ("Slice 4 results"
in the same document): the Desk margin column now holds labels (date, "Next step", "Your courses", "Lecture", "Syllabus")
from 1024; courses are ledger rows; Add a course and sign-in put the h1 above one sheet; no cards or panels remain on those
screens. The landmark gate's 12th landmark is now Today's Next step section (the first card is gone) and Calm's allowance
covers only the Add a course field (recorded there). **T2 IMPLEMENTED 2026-10-31 as redesign-round2 slice 5** ("Slice 5
results" in the same document): Preferences side panel from 1024, Theme radiogroup (5 cards, nested-skin swatches),
"Theme" legend -> "Colour mode" (`AppShell.test.tsx` "Dark theme on." -> "Dark mode on.", recorded), Reset look via
`lookDefaults()`, Preview card, status messages, rollback hides the control; Portal and Calm finished on the screens built so
far (paint only); consent card carved out of skin card decoration (non-Paper skins, G1). **Redesign-round2 slice 6
(syllabus review density) IMPLEMENTED 2026-10-31** ("Slice 6 results" in the same document): items as ledger rows with one
bordered button each, group heads as sans label-style disclosures, the calendar step as a Note in the flow (built sticky,
reverted after an axe target-size failure), the Add syllabus form on the Sheet template around the unchanged Draft notice (G1), the confirm dialog's
buttons at 44px (the `48px` ratchet entry for `ConfirmDialog.tsx` removed), notes and ledgers in the batch / sync / list
screens; the geometry gate now measures the review page at the 7 Paper widths. **T3 + T4 IMPLEMENTED 2026-10-31 as
redesign-round2 slice 7** ("Slice 7 results" in the same document): Gamer "Grid texture" and Glass "Solid surfaces"
switches in Preferences (only for their theme, directly under the theme cards, persisted, announced once through the
existing status line, strings in the linted `PREFS_COPY`); the whole lecture route (consent and mic-blocked included) has no
page decoration and an opaque header (`main[data-route="lecture"]`); a T0 bug fixed (every media fallback left the Glass
blur running behind an opaque bar; now `!important`); forced colours clear the page layer for Gamer-grid and Glass-dark;
new `e2e-screens/gamer-glass.spec.ts` (emulateMedia + CDP reduced-transparency fallbacks, backdrop-filter / translucency
DOM scan, no-glow scan, real-DOM contrast over every decoration stop, axe with the switches on, Low-stimulation captures per
skin, report-only frame trace); landmark gate with the switches on and off. The Glass enter animation was NOT built
(redesign-round2 2.6 forbids transforms and limits opacity to the scrim; recorded there). **T6 NOT STARTED; T5 was NOT STARTED at this point** (T5's
lecture pass over 10 looks can reuse slice 3's harness tests). **Redesign-round2 slices 8b and 8c IMPLEMENTED 2026-10-31**
("Slice 8b results" / "Slice 8c results" in `redesign-round2.md`; spec `make-notes-rail-demo-notice.md`; not committed):
8b (PO-18c) renders the lecture Demo-source notice as a one-line Note with its unchanged body behind "Details" in every
state, removes the Demo tag from the bar at every width (the bar is one 56px row at 1280 in every lecture fixture now),
fills the Paused / Offline / Mismatch notes with their state tint (Fatal stays unfilled: there is no `danger-subtle` role)
and sets notes under the bar 24px apart; wording byte-identical, recording pill unchanged, consent files untouched. 8c
(PO-18b) moves the syllabus Calendar step into a reserved 20rem sticky track beside the groups from 1280 (in the flow
below), with a scroll-scan geometry test. Themes impact: paint only; the landmark gate (14 looks) stays green; no skin
rule touches the new note fill, the Demo note or the review rail. **Redesign-round2 slice 8a IMPLEMENTED 2026-10-31**
("Slice 8a results" in `redesign-round2.md`; PO-18d option B; not committed): after Stop a "Notes" block in the transcript
column offers one "Make notes" press (DRAFT disclosure, G1 BLOCKED), polls while Pending and turns into a Ready lecture
sheet with cited bullets; the TL;DR row captions a Final revision. Themes impact: paint only. The Ready sheet reuses
`.lecture-sheet`, `.synthesis` (h3 follows each skin's heading face), `.hang-row` and `.cite`; no new CSS rule and no skin
rule; a new geometry test holds the Ready sheet's x / y / width equal across the 14 looks at 390 and 1280 and checks that
in every look the h3 differs from the 17/28 bullets by face or size; `lecture-notes-ready` is captured in the skin looks.
**T5 IMPLEMENTED 2026-10-31 as redesign-round2 slice 8** ("Slice 8 results" in `redesign-round2.md`; not committed): the
lecture family (live, paused, stopped, stopped with notes ready, Quick recap result, disconnected, mismatch, mic denied,
the real consent page, Focus mode, the real reopen route) under the ten looks at 390 and 1280 in the new
`e2e-screens/lecture-looks.spec.ts`, with axe (color-contrast included) and computed-style invariants (transcript metrics,
face and surface; the synthesis size floor; pill and bar boxes and colours; opaque lecture text surfaces; live regions;
landmark boxes equal to Paper light; Low-stimulation never dims the pill or removes the safety stripe). Changes, the same in
every theme: lecture times right-aligned in the margin track from 1280; the collapsed TL;DR line 20/28 with a 56px reserve
below 640; the empty-transcript line on the words' edge at 1280 (one defect the pass found). Two findings left for the Lead:
Paper + Low-stimulation (Fog / Dusk) flattens the status tints; a dev-only StrictMode hub race on the reopen route.

## T0 token plumbing (DONE)
Files changed:
- `src/web/src/lib/design/tokens.ts`: roles `chrome`, `on-chrome`, `grid`; `SKINS`; 8 new palettes (portal, gamer, calm, glass x
  light/dark) beside Paper / Night desk / Fog / Dusk; `PALETTE_LOOKS`, `paletteFor()`, chrome/grid/Glass-composite pair lists.
- `src/web/src/app/globals.css`: 12 palette blocks generated from tokens.ts (all 176 new values cross-checked against the
  spec's paste-ready CSS: 0 mismatches); per-skin structure (radii, shadows, heading face, durations, Calm card padding,
  Glass gradient and translucent header, Gamer grid texture); Low-stimulation overlay for every skin; `[data-chrome]` header
  with all Glass fallbacks (no backdrop-filter support, reduced transparency, contrast more, forced colours, < 1024px, lecture
  routes, Solid surfaces, Low-stimulation); `body::before` page decoration (never on lecture routes); per-skin card decoration
  (Portal top strip, Gamer corner ticks, Glass highlight), Gamer mono for eyebrow/tag/timestamps only; `.display` uses
  `--font-head/--head-weight/--head-track`.
- `src/web/tailwind.config.ts`: colours `chrome`, `on-chrome`, `grid`; radii from variables; `font-serif`/`font-head` = `--font-head`.
- `src/web/src/app/layout.tsx`: next/font Newsreader renamed to `--f-newsreader`; Source Sans 3, JetBrains Mono, Inter added
  (`preload: false`, all confirmed in the installed Next 15.5.28 catalog); `<html data-themes="on|off">` from
  `NEXT_PUBLIC_THEMES`; `colorScheme: "light dark"`; themeColor reads token values (no raw hex in components).
- `src/web/src/lib/design/prefs.ts`: additive fields `skin` (default "paper"), `texture`, `solid`; `themesEnabled()`;
  `htmlAttributes` sets `data-skin/data-texture/data-solid` and forces Paper when themes are off; `LOOK_KEYS`/`lookDefaults()` for Reset.
- `src/web/public/prefs-init.js`: same fields before first paint; honours `data-themes="off"` (keeps the stored skin).
- `src/web/src/components/AppShell.tsx`: header `bg-surface` -> `data-chrome` (Paper's chrome = surface, so Paper looks the same).

Behaviour now: the app looks unchanged by default (Paper). Setting `skin` in `tether.prefs.v1` (or `data-skin` on `<html>`)
themes the whole app; verified by screenshots of sign-in in paper/portal/gamer/glass with no new console errors. There is
**no UI to choose a theme yet** (that is T2).

Tests added/changed (no existing assertion weakened):
- `tokens.test.ts` rewritten and extended: resolves the globals.css cascade (specificity + order) for all 12 palettes, for
  nested theme swatches, and for `<html>` with no attributes; full matrix per palette (every text role on every background
  >= 4.5, on-accent/accent and on-chrome/chrome >= 4.5, boundaries >= 3); header pairs on chrome for non-remapped skins; Gamer
  grid pairs; Glass header composited at alpha .72 over both gradient stops; gradient stops within the tested luminance
  range; CSS Glass values equal the tested ones; spec 2.3 hand values within 0.02; new guard: no raw hex colours in `.tsx`.
  Mutation check: changing one portal-dark value in globals.css made 2 tests fail; restored.
- `prefs.test.ts`: new fields, unknown skin -> Paper, Reset keys, init-script parity for 5 more cases, rollback flag.

Contrast result: **every pair passes AA as specified; no token had to change.** Tightest: Gamer light accent on sunken
4.78; Paper border-strong vs sunken 3.01 (pre-existing). Spec hand values: 77 of 80 within 0.02; three doc roundings differ
by 0.03-0.04 (gamerDark warning 9.10 -> 9.13, calmDark on-chrome 12.20 -> 12.24, glassLight on-chrome 15.20 -> 15.16);
the test lists recomputed values with the doc value in a comment.

Deviations from the spec (T0):
1. Fog/Dusk selectors also match `<html>` with no `data-skin` (script blocked), so the Low-stimulation look still works then.
2. In-header colour remap (ink, muted, accent, focus, borders -> on-chrome) applies only to Portal and Gamer (dark bar in light
   mode). Paper/Calm/Glass keep accent focus rings in the header (tested >= 3:1 on chrome and on the Glass composite), so Paper
   stays identical.
3. Calm does not set `--gap-block: 2rem` (spec 2.5 code) because rule R11 allows only `--pad-card` as a layout exception.
4. Calm gets `--dur-*: 0ms` (spec 2.5 motion row; missing from the paste-ready block).
5. `prefers-contrast: more` overrides use `!important` so no skin can weaken them.
6. Theme UI string "Course portal" is not shipped yet (no UI in T0).

## Test results after T0 (actual runs, 2026-10-29)
| Check | Command | Result |
|---|---|---|
| Lint | `npm run lint` (src/web) | 0 errors, 1 warning (baseline `_label` in e2e/lecture.spec.ts) |
| Typecheck | `npm run typecheck` | clean |
| Unit (src/shared mounted) | `docker run --rm -v "$PWD/..":/repo -v tether-web-nm:/repo/web/node_modules -w /repo/web node:22 sh -c 'npm ci ... && npx vitest run'` | **25 files, 1464 passed** (baseline 737; +727 mostly the new contrast/cascade matrix) |
| Harness e2e | `npx playwright test -c playwright.harness.reuse.config.ts` (3100 -> 3000 TCP forwarder, scratchpad script) | **47 passed** |
| Courses e2e | `npx playwright test -c playwright.reuse.config.ts e2e/courses.spec.ts` | **11 passed** |
| Geometry / screenshots+axe across themes | not written yet (T1/T5) | NOT RUN |
| Lecture e2e (real stack) | | NOT RUN: fake microphone hangs on this machine (unchanged) |

No existing test was edited. The "Theme" -> "Colour mode" legend rename is T2 and was not done; `AppShell.test.tsx`
("Dark theme on.") is unchanged.

## NOT done (resume here, in order)
- **T1 layout system**: done as redesign-round2 slice 1 (see that document, "Slice 1 results"). Left for later slices: A5
  (Preferences switch-row insets, slice 2 owns `components/ui`), the Preferences side panel (slice 5), the `.tag` padding
  (the consent page's Draft tag, gate G1; slice 2's Stamp), the 48px buttons inside the consent surfaces (G1).
- **T2** DONE (redesign-round2 slice 5; see that document). Left: the Gamer "Grid texture" and Glass "Solid surfaces"
  switches (shown only for their theme) are slice 7; choosing Gamer or Glass in the picker works today with the shipped
  palettes and T0 CSS.
- **T3** DONE (slice 7): Grid texture switch, no-glow checks. **T4** DONE (slice 7) except the enter animation (not built,
  see slice 7 deviations): Solid surfaces switch, emulateMedia / CDP fallback tests, backdrop-filter DOM scan, a report-only
  frame-time trace (headless, not a real-device measurement). **T5** DONE (slice 8): lecture pass over 10 looks in
  `e2e-screens/lecture-looks.spec.ts` (three live regions included). **T6** CI matrix (no CI records exist).
- `e2e-screens/screens.spec.ts` captures Portal and Calm (light + dark) on 12 key screens at 390/1280 with axe since
  slice 5, and since slice 7 also Gamer with the grid on and Glass frosted (light + dark); Glass with Solid surfaces on is
  axe-checked on 5 key screens in `gamer-glass.spec.ts`.
- Theme copy lives in `src/components/prefsCopy.ts` and is linted by `ThemePicker.test.tsx` (done in slice 5).

## How to resume
1. `cd src/web`; confirm `npm run dev:real` is on :3000 and the API on :5080.
2. Harness e2e needs :3100: run a stdlib TCP forwarder 3100 -> 3000 (one-line `net` pipe in node) or let
   `playwright.harness.config.ts` start its own server.
3. Run the four checks above after each slice; screenshots: `npx playwright test -c playwright.screens.config.ts`.
4. To see a theme today without UI: in devtools, `localStorage.setItem("tether.prefs.v1", JSON.stringify({skin:"gamer",theme:"dark"}))`
   and reload (skins: paper, portal, gamer, calm, glass; add `texture:true` for Gamer grid, `solid:true` for Glass). Rollback:
   build with `NEXT_PUBLIC_THEMES=off` (forces Paper).
