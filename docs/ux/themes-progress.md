# Themes (BL-001) progress

Owner: frontend-engineer. Spec: docs/ux/themes-spec.md. Date: 2026-10-29. Scope: `src/web` only. Nothing is committed.
G4 stays NOT_RUN (no human, screen-reader or device testing). G1 stays BLOCKED: no recording, consent, Demo-source or
"may be missing" wording was touched.
Lead decisions applied: Q-PO1 label "Course portal" (code id `portal`, no third-party name or asset anywhere);
Q-PO2 all four skins planned, Paper stays the default; Q-LEAD1 44px controls; Q-LEAD2 no per-theme theme-color meta.

Status: **T0 DONE and green. T1 IMPLEMENTED 2026-10-30 as redesign-round2 slice 1** (results, test changes and
deviations recorded in `docs/ux/redesign-round2.md` under "Slice 1 results"; not committed). **Redesign-round2 slice 2
(primitives v2) IMPLEMENTED 2026-10-31** ("Slice 2 results" in the same document): A5 (Preferences switch-row insets,
`.hover-row`) and Segmented equal option widths are done there; the `.tag` padding stays (consent Draft tag, gate G1) and
the 48px consent buttons stay (G1). **T2-T6 NOT STARTED** (T2 needs slice 2's Segmented widths, now in place).

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
- **T2** Preferences: Theme radiogroup (5 cards, swatches can use nested `data-skin`/`data-theme`, which the CSS already
  supports), rename legend "Theme" -> "Colour mode" (update `AppShell.test.tsx` status "Dark theme on." -> spec's
  "Dark mode on." and record it), Reset look (use `lookDefaults()`), Preview card, status messages, rollback hides the control;
  Portal/Calm review. Lecture settings popover keeps Colour mode + Low-stimulation only.
- **T3** Gamer: Grid texture switch (CSS done), no-glow checks. **T4** Glass: Solid surfaces switch (CSS done), enter
  animation, emulateMedia fallback tests, backdrop-filter DOM scan. **T5** lecture pass over 10 looks (harness specs loop over
  skins; three live regions). **T6** CI matrix (no CI records exist).
- Extend `e2e-screens/screens.spec.ts` LOOKS to skins x modes at 390/1280 with axe; review screenshots by eye.
- Copy for theme descriptions must follow language-guide.md (strings are in spec 3.2) and go through copy lint.

## How to resume
1. `cd src/web`; confirm `npm run dev:real` is on :3000 and the API on :5080.
2. Harness e2e needs :3100: run a stdlib TCP forwarder 3100 -> 3000 (one-line `net` pipe in node) or let
   `playwright.harness.config.ts` start its own server.
3. Run the four checks above after each slice; screenshots: `npx playwright test -c playwright.screens.config.ts`.
4. To see a theme today without UI: in devtools, `localStorage.setItem("tether.prefs.v1", JSON.stringify({skin:"gamer",theme:"dark"}))`
   and reload (skins: paper, portal, gamer, calm, glass; add `texture:true` for Gamer grid, `solid:true` for Glass). Rollback:
   build with `NEXT_PUBLIC_THEMES=off` (forces Paper).
