# Tether Themes Spec (BL-001): Canvas, Gamer, Calm, Glass, each light and dark

Status: DESIGN DECISION v1, 2026-10-29. Author: adhd-ux-specialist. Audience: frontend-engineer, qa-engineer, device-accessibility-validation.
**Gate G4 stays NOT_RUN: nothing here is human-validated.** G1 stays BLOCKED: no consent, recording or Demo-source wording is touched.
Labels: [E] published or standard knowledge, recalled from memory and NOT re-fetched in this session (verify before quoting outside the team; I cite no URLs I could not check); [J] design judgement or untested hypothesis; [C] project constraint.
Method note: I had no shell. Every ratio below is hand-computed with the WCAG 2.x formula and may contain an arithmetic slip. The implementation adds a unit test that recomputes every pair (section 2.3); **the test wins over this table**. No code, screenshot run or browser was executed by me; the audit (section 5) is from viewing 12 existing screenshots in `src/web/test-results/redesign-screens/` plus reading source.
Inputs read: design-direction.md, redesign-progress.md, redesign-review.md, language-guide.md, CLAUDE.md invariants 2, 7, 9, backlog BL-001, `lib/design/{tokens,contrast,prefs}.ts`, `public/prefs-init.js`, `tailwind.config.ts`, `app/globals.css`, `layout.tsx`, `AppShell.tsx`, `PreferencesPanel.tsx`, `ui/Switch.tsx`.

## 0. Decisions in one screen

1. Five **themes** (skins): `paper` (existing default, "the reading room"), `portal` (BL-001 "Canvas"), `gamer`, `calm`, `glass`. Each x `light | dark`. A theme is **paint only**: colour, radius, shadow, heading typeface, decorative texture. It never changes boxes, spacing, order, copy, ARIA or behaviour.
2. Model: **Theme x Colour mode** (System/Light/Dark), plus overlays: Low-stimulation look, Reduce motion, and two theme-specific switches (Grid texture for Gamer, Solid surfaces for Glass). Density, Text size, Reading spacing and Focus mode stay orthogonal.
3. Naming risk for the PO: "Canvas" is also the name of a third-party product. Code id is `portal` (also avoids a clash with the `--c-canvas` page-background token). UI label is **an open question for the PO** (Q-PO1); default label until answered: "Course portal". No logo, name, mark, colour value or asset of any LMS vendor is used.
4. Two new colour roles (`chrome`, `on-chrome`) and one decorative role (`grid`); the contrast test matrix grows accordingly. Existing 19 roles and their tests are unchanged.
5. Alignment is fixed **before** themes: one spacing scale, one container system, named page templates, 44px control height, optical-alignment rules, geometry tests (section 5, slice T1).
6. Rollback: one build-time flag `NEXT_PUBLIC_THEMES=off` forces `paper` and hides the Theme control; stored choices are kept and ignored.

## 1. Research-backed principles (state of practice as I know it, 2026)

Honest ground rule [J]: I know of no study I can verify showing that any colour theme reduces ADHD symptoms or raises learning. ADHD is a diverse set of experiences [C]. Themes are offered as **personal preference and comfort**, never as treatment or recommendation. No string says otherwise.

### 1.1 Cross-cutting practice
| # | Claim | Tag |
|---|---|---|
| P1 | Semantic design tokens (role names, not colours) with per-theme values are the standard way to ship multi-theme UIs; components must never use raw colours. Tether already does this (`--c-*`, no opacity modifiers). | [E] |
| P2 | WCAG 2.2 AA: text 4.5:1 (1.4.3), UI boundaries and focus 3:1 (1.4.11), focus not obscured (2.4.11), focus appearance (2.4.13, AAA, used as a goal), target size min 24px (2.5.8; we use 44), no more than 3 flashes (2.3.1). Contrast is measured against the worst background pixel the text can sit on. | [E] |
| P3 | Respect user-agent preferences as media queries: `prefers-color-scheme`, `prefers-contrast`, `prefers-reduced-motion`, `forced-colors`. `prefers-reduced-transparency` exists but support was partial/behind flags when I last knew; treat it as progressive enhancement and also give an in-app switch. | [E] |
| P4 | Layout platform features that are widely supported by 2026 and reduce misalignment: CSS Grid + subgrid, container queries, `:has()`, logical properties, `dvh`, `text-wrap: balance|pretty`, `scrollbar-gutter: stable`, `color-mix()`. Use them; do not use `light-dark()` here because modes are attribute-driven. | [E] |
| P5 | Dark UIs avoid pure white text on pure black (glare, halation for some readers, notably some people with astigmatism); dark-on-light is often read faster in bright rooms; preference is individual and depends on ambient light. So both modes exist and the default is System. Pure black/white only under `prefers-contrast: more`. | [E] weak, recalled |
| P6 | Familiar patterns lower learning cost (Jakob's law, Nielsen). This is the only reason to ship an LMS-like theme. | [E] |
| P7 | Tether-specific: one filled accent control per region, recording status never softened, no gamification, no passive attention inference. | [C] |

### 1.2 Per style
**Canvas / course portal.** Common LMS pattern [J]: dark global top bar, light grey page, white cards with a coloured top strip, plain blue links, small corner radii, dense lists, system-like sans type. We borrow the *pattern*, with our own colours (deep slate bar, a blue not tied to any vendor), our own icons and no wordmark other than "Tether". Familiarity may help students who live in an LMS all day [J, untested]. Risk: it looks like homework; students may dislike it (Q3).
**Gamer.** Common practice in game and developer UIs [J]: dark-first, thin 1px rules, small radii, monospace for labels and numbers, one saturated accent on near-black. Restrained: accent colour is used for text, 1px edges and the primary fill only. **No glow, bloom, pulsing, flicker, chromatic split, scanline animation or neon gradients** (sensory load, 2.3.1 risk). Monospace is poor for long prose [J, widely held], so it is limited to headings, eyebrows, timestamps and tags; body and transcript stay in the hyperlegible face. **No points, levels, streaks, XP, badges, ranks, progress bars as rewards, leaderboards or any pressure mechanic** [C]; "game" is a visual register only. A faint grid texture is allowed, OFF by default, and never sits behind transcript text.
**Calm.** Lowest stimulation: desaturated sage and stone, one soft accent, large radii, no shadows, no texture, 0ms transitions, extra card padding. Whether low stimulation helps any given student is an untested hypothesis [J]. Calm differs from the existing "Low-stimulation look": Calm is a full theme with its own palette and roominess; the overlay is a switch that strips effects from any theme.
**Glass.** Apple introduced its translucent "Liquid Glass" material with the OS 26 releases announced at WWDC 2025 [E, recalled]. Reviewers and accessibility advocates reported legibility and contrast problems when translucent controls sit over varying content, and Apple kept or added Reduce Transparency / Increase Contrast style controls and adjusted the material in later builds [E, recalled, verify details before quoting]. Known problems and this spec's mitigations:
| Known problem | Mitigation here (testable) |
|---|---|
| Contrast over a variable background | Translucency only on the site header (and the scrim behind dialogs). Header content is short UI labels. Every header text colour is contrast-tested on its **opaque fallback** AND on the translucent colour composited over both gradient stops (section 2.3). |
| Body, transcript, TL;DR, recap, forms, consent text over blur | **Forbidden.** All of these sit on opaque `surface` panels in every theme. The lecture bar is opaque in every theme. A test asserts no element with `backdrop-filter` contains `[data-segment-id]`, `#lecture-tldr`, `#lecture-quick-recap`, `input`, `textarea` or `main` content. |
| Reduced transparency | `prefers-reduced-transparency: reduce`, `prefers-contrast: more`, `forced-colors: active`, in-app **Solid surfaces** switch, Low-stimulation look, `@supports not (backdrop-filter)`, viewports < 1024px and all lecture routes use the opaque fallback (`--c-chrome`). |
| Motion, parallax and specular shimmer | None. No refraction, no moving highlights, no gradient animation. Static 1px inner highlight only. |
| Performance on low-end devices | At most 2 simultaneous `backdrop-filter` elements (header, scrim), blur radius 20px max, none below 1024px, none on lecture routes. Budget in section 6. |
| Edge legibility (translucent over translucent) | No stacked glass: panels are opaque; only the header is glass. |

## 2. Tokens

### 2.1 Role model (extends design-direction.md 2.2; names are the contract)
Existing roles unchanged: canvas, surface, raised, sunken, ink, muted, accent, on-accent, accent-subtle, recording, recording-subtle, success, success-subtle, warning, warning-subtle, danger, border-strong, border-hair, focus.
New colour roles: **chrome** (site header background, opaque; the translucent Glass colour is derived), **on-chrome** (text and control edges inside the header), **grid** (decorative background under the page grid texture; equals canvas where unused).
New structural variables (no contrast pairs): `--r-control --r-card --r-sheet`, `--shadow-1..3`, `--font-head`, `--head-weight`, `--head-track`, `--page-bg`, `--chrome-glass`, `--blur`, `--dur-fast --dur-base`.
Rules: status colours are shared per mode across themes (light: Paper values; dark: Night values) so the "only red is recording" rule holds in every theme. Inside `[data-chrome]` the focus ring uses `on-chrome` (so it stays 3:1 on a dark header). Controls inside the header use `on-chrome` text and 2px `on-chrome` borders on a transparent fill. The recording pill and the lecture bar never use chrome roles.

### 2.2 Shared status values
Light (Canvas, Gamer, Calm, Glass): recording #B42318 on recording-subtle #FCEBE8; success #1B6B3A on #E7F3EA; warning #7A4300 on #FBF0D9; danger #9F1D24. Dark: recording #FF8A7A, success #6FD39A, warning #F2B84B, danger #FF8F98; each subtle = that theme's `raised`.

### 2.3 Contrast
Formula (WCAG 2.x): L = 0.2126 R + 0.7152 G + 0.0722 B, channel c = v/255, lin = c/12.92 if c <= 0.03928 else ((c+0.055)/1.055)^2.4; ratio = (L_light + 0.05) / (L_dark + 0.05). Targets: text >= 4.5, boundary/focus/fill-edge >= 3.
**Each row lists the worst case for that palette** (the lightest dark-mode surface or darkest light-mode surface it can sit on: `sunken` in light, `raised` in dark; the full matrix is what the test enforces). Foreground roles: ink, muted, accent (text and focus), on-accent on accent (primary button), recording, success, warning, danger (text), border-strong (boundary vs plain backgrounds), on-chrome on chrome.
| Palette | worst bg | ink | muted | accent | on-accent/accent | recording | success | warning | danger | border-strong | on-chrome/chrome |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Paper, Night desk, Fog, Dusk | unchanged | already enforced by `tokens.test.ts` (chrome = surface, on-chrome = ink; add rows to the test) | | | | | | | | | |
| Portal light | sunken .803 | 12.32 | 5.92 | 5.61 | 6.90 | 5.35 | 5.31 | 6.49 | 6.38 | 3.34 | 12.97 |
| Portal dark | raised .0223 | 12.20 | 6.58 | 6.49 | 8.00 | 6.34 | 7.93 | 8.10 | 6.65 | 3.35 | 16.05 |
| Gamer light | sunken .762 | 14.19 | 5.92 | 4.78 | 6.18 | 5.09 | 5.06 | 6.18 | 6.07 | 3.55 | 14.90 |
| Gamer dark | raised .0143 | 13.98 | 6.35 | 9.65 | 11.08 | 7.13 | 8.91 | 9.10 | 7.48 | 3.56 | 17.00 |
| Calm light | sunken .780 | 10.77 | 5.17 | 4.94 | 6.25 | 5.20 | 5.17 | 6.31 | 6.20 | 3.50 | 12.78 |
| Calm dark | raised .0235 | 10.93 | 5.92 | 7.45 | 9.31 | 6.23 | 7.79 | 7.97 | 6.54 | 3.31 | 12.20 |
| Glass light | sunken .755 | 12.47 | 5.81 | 5.41 | 7.06 | 5.04 | 5.01 | 6.12 | 6.01 | 3.78 | 15.20 |
| Glass dark | raised .0276 | 11.48 | 6.00 | 5.64 | 7.75 | 5.90 | 7.38 | 7.55 | 6.20 | 3.25 | 15.20 |
Extra pairs the test must also cover (not in the table):
- Gamer: ink, muted, accent, recording, success on `grid` (light #E1E7EE: muted 6.15, accent 4.97; dark #141C27: muted 6.67, accent 10.1).
- Glass translucent header: on-chrome, accent and muted on `chrome` composited at alpha .72 over each gradient stop (light stops #DCE6F7 and #ECE6F7, dark stops #0F1322 and #171B30); hand estimate >= 6.3 for all. The gradient stops must stay within the luminance range of `canvas`..`sunken`; the test asserts this.
- Every theme: `focus` vs every plain background >= 3 (focus = accent: table above), `on-chrome` vs `chrome` >= 3 for borders.
- `prefers-contrast: more`: ink to #000000 (light) / #FFFFFF (dark), border-hair = border-strong, Glass opaque, Gamer texture off (all themes already exceed AA, so this only adds margin).
Tightest margins to watch (the test may tip them): Gamer light accent on sunken 4.78, Calm light accent on sunken 4.94, Glass light success on sunken 5.01. If any fails by recomputation, darken that foreground, not the background.

### 2.4 Palettes (paste-ready, every role listed per block)
The hex value is the decision; unlisted pairs are not allowed.
```css
/* Order in globals.css after the existing Paper/Fog/Dusk blocks. Skin selectors carry no :root so a Preferences
   preview element with data-skin/data-theme can scope them. Paper blocks gain ", [data-skin="paper"]" selectors, and Fog/Dusk
   are scoped to [data-skin="paper"] (see 3.4) so they cannot override other skins. */
[data-skin="portal"] { /* light */
  --c-canvas:#F2F4F7; --c-surface:#FFFFFF; --c-raised:#FFFFFF; --c-sunken:#E4E8ED; --c-ink:#1B2733; --c-muted:#4A5866;
  --c-accent:#0A5AA8; --c-on-accent:#FFFFFF; --c-accent-subtle:#E3EEF9; --c-recording:#B42318; --c-recording-subtle:#FCEBE8;
  --c-success:#1B6B3A; --c-success-subtle:#E7F3EA; --c-warning:#7A4300; --c-warning-subtle:#FBF0D9; --c-danger:#9F1D24;
  --c-border-strong:#6F7F8F; --c-border-hair:#D5DCE3; --c-focus:#0A5AA8; --c-chrome:#1E3347; --c-on-chrome:#FFFFFF; --c-grid:#F2F4F7; }
[data-skin="portal"][data-theme="dark"] {
  --c-canvas:#10161C; --c-surface:#18202A; --c-raised:#202A36; --c-sunken:#0B1015; --c-ink:#E6ECF2; --c-muted:#A3B0BE;
  --c-accent:#6DB3F2; --c-on-accent:#07182A; --c-accent-subtle:#202A36; --c-recording:#FF8A7A; --c-recording-subtle:#202A36;
  --c-success:#6FD39A; --c-success-subtle:#202A36; --c-warning:#F2B84B; --c-warning-subtle:#202A36; --c-danger:#FF8F98;
  --c-border-strong:#6B7B8C; --c-border-hair:#26313D; --c-focus:#6DB3F2; --c-chrome:#0B1015; --c-on-chrome:#E6ECF2; --c-grid:#10161C; }
[data-skin="gamer"] {
  --c-canvas:#EEF1F5; --c-surface:#FFFFFF; --c-raised:#FFFFFF; --c-sunken:#DDE3EA; --c-ink:#0D1520; --c-muted:#455566;
  --c-accent:#006A85; --c-on-accent:#FFFFFF; --c-accent-subtle:#DDF1F7; --c-recording:#B42318; --c-recording-subtle:#FCEBE8;
  --c-success:#1B6B3A; --c-success-subtle:#E7F3EA; --c-warning:#7A4300; --c-warning-subtle:#FBF0D9; --c-danger:#9F1D24;
  --c-border-strong:#66778A; --c-border-hair:#D2DAE3; --c-focus:#006A85; --c-chrome:#0D1B2A; --c-on-chrome:#E8EEF5; --c-grid:#E1E7EE; }
[data-skin="gamer"][data-theme="dark"] {   /* the intended look: dark first */
  --c-canvas:#0A0E14; --c-surface:#101722; --c-raised:#172030; --c-sunken:#060A0F; --c-ink:#E8EEF5; --c-muted:#94A3B5;
  --c-accent:#4FD8F0; --c-on-accent:#04141A; --c-accent-subtle:#172030; --c-recording:#FF8A7A; --c-recording-subtle:#172030;
  --c-success:#6FD39A; --c-success-subtle:#172030; --c-warning:#F2B84B; --c-warning-subtle:#172030; --c-danger:#FF8F98;
  --c-border-strong:#66778A; --c-border-hair:#1F2A38; --c-focus:#4FD8F0; --c-chrome:#060A0F; --c-on-chrome:#E8EEF5; --c-grid:#141C27; }
[data-skin="calm"] {
  --c-canvas:#EEF0EA; --c-surface:#F7F8F4; --c-raised:#F7F8F4; --c-sunken:#E2E6DD; --c-ink:#26302B; --c-muted:#55605A;
  --c-accent:#2F6B57; --c-on-accent:#FFFFFF; --c-accent-subtle:#E3EDE7; --c-recording:#B42318; --c-recording-subtle:#FCEBE8;
  --c-success:#1B6B3A; --c-success-subtle:#E7F3EA; --c-warning:#7A4300; --c-warning-subtle:#FBF0D9; --c-danger:#9F1D24;
  --c-border-strong:#6E7B73; --c-border-hair:#D8DDD3; --c-focus:#2F6B57; --c-chrome:#F7F8F4; --c-on-chrome:#26302B; --c-grid:#EEF0EA; }
[data-skin="calm"][data-theme="dark"] {
  --c-canvas:#151A18; --c-surface:#1D2321; --c-raised:#252C29; --c-sunken:#101412; --c-ink:#DCE3DE; --c-muted:#9DAAA3;
  --c-accent:#8FC7B0; --c-on-accent:#0E1A15; --c-accent-subtle:#252C29; --c-recording:#FF8A7A; --c-recording-subtle:#252C29;
  --c-success:#6FD39A; --c-success-subtle:#252C29; --c-warning:#F2B84B; --c-warning-subtle:#252C29; --c-danger:#FF8F98;
  --c-border-strong:#6F7D75; --c-border-hair:#2C3431; --c-focus:#8FC7B0; --c-chrome:#1D2321; --c-on-chrome:#DCE3DE; --c-grid:#151A18; }
[data-skin="glass"] {   /* canvas is the DARKEST gradient stop on purpose: tests use the worst case */
  --c-canvas:#DCE6F7; --c-surface:#FFFFFF; --c-raised:#FFFFFF; --c-sunken:#D9E2F1; --c-ink:#172033; --c-muted:#475569;
  --c-accent:#0A4FC9; --c-on-accent:#FFFFFF; --c-accent-subtle:#E4ECFB; --c-recording:#B42318; --c-recording-subtle:#FCEBE8;
  --c-success:#1B6B3A; --c-success-subtle:#E7F3EA; --c-warning:#7A4300; --c-warning-subtle:#FBF0D9; --c-danger:#9F1D24;
  --c-border-strong:#62718C; --c-border-hair:#C9D5EA; --c-focus:#0A4FC9; --c-chrome:#F4F7FD; --c-on-chrome:#172033; --c-grid:#DCE6F7; }
[data-skin="glass"][data-theme="dark"] {   /* canvas is the LIGHTEST gradient stop */
  --c-canvas:#171B30; --c-surface:#1E2338; --c-raised:#272D47; --c-sunken:#0F1322; --c-ink:#E9ECF8; --c-muted:#A4ACC8;
  --c-accent:#8EA2FF; --c-on-accent:#0B1030; --c-accent-subtle:#272D47; --c-recording:#FF8A7A; --c-recording-subtle:#272D47;
  --c-success:#6FD39A; --c-success-subtle:#272D47; --c-warning:#F2B84B; --c-warning-subtle:#272D47; --c-danger:#FF8F98;
  --c-border-strong:#6F7BA3; --c-border-hair:#2E3554; --c-focus:#8EA2FF; --c-chrome:#121629; --c-on-chrome:#E9ECF8; --c-grid:#171B30; }
```
Paper additions: `--c-chrome` = `--c-surface`, `--c-on-chrome` = `--c-ink`, `--c-grid` = `--c-canvas` (light and dark, Fog and Dusk too).

### 2.5 Structure per theme (non-colour)
| Token | Paper | Portal | Gamer | Calm | Glass |
|---|---|---|---|---|---|
| radius control / card / sheet | 8 / 12 / 16 | 4 / 6 / 8 | 2 / 4 / 6 | 12 / 16 / 24 | 12 / 20 / 28 |
| card edge | 1px hair | 1px hair + 4px top strip in accent (inset shadow, no layout) | 1px border-hair; 1px accent corner ticks (decorative `::before`, `aria-hidden` by CSS only) | 1px hair | 1px hair + 1px inner top highlight |
| shadow-1 (light / dark) | existing / none | `0 1px 3px rgba(23,32,51,.14)` / none | none / none | none / none | `0 8px 24px rgba(40,60,120,.10)` / `0 8px 24px rgba(0,0,0,.35)` |
| heading face (weight, tracking) | Newsreader (600, -0.01em) | Source Sans 3 (600, 0) | JetBrains Mono (600, -0.02em) | body face, Atkinson Hyperlegible Next (600, 0) | Inter (600, -0.02em) |
| eyebrow / tag / timestamp face | body | body | JetBrains Mono, tabular | body | body |
| body, transcript, forms | Atkinson Hyperlegible Next in every theme [J: one legibility-first body face everywhere; only headings change] | | | | |
| page background | canvas | canvas | canvas + optional grid | canvas | static gradient canvas to lighter stop, behind header and gutters only |
| header | surface, 1px hair | `chrome` dark slate | `chrome` near-black, 1px accent bottom edge | surface, 1px hair | translucent chrome (blur 20px) with solid fallback |
| motion | 120/200ms | 120ms colour only | 120ms border/colour only | 0ms | 200ms opacity + translateY <= 8px on dialog/panel enter |
Heading fonts load via `next/font/google` with `preload: false` except the default, so a student only downloads the face of the active theme (browsers fetch an `@font-face` only when text uses it [E]; confirm in the Network panel). Font names must be confirmed in the installed Next 15.5.x catalog at build; fall back to `system-ui` stacks if a name is missing. Rename the existing `--font-head` next/font variable to `--f-newsreader` and let each theme set `--font-head`:
```css
:root { --font-head: var(--f-newsreader), Georgia, serif; --head-weight:600; --head-track:-0.01em; --r-control:8px; --r-card:12px; --r-sheet:16px; --blur:0px; --page-bg:none; --chrome-glass:var(--c-chrome); }
[data-skin="portal"] { --font-head: var(--f-source-sans), var(--font-body), system-ui, sans-serif; --head-track:0; --r-control:4px; --r-card:6px; --r-sheet:8px; --shadow-1:0 1px 3px rgba(23,32,51,.14); }
[data-skin="gamer"]  { --font-head: var(--f-jetbrains-mono), ui-monospace, monospace; --head-track:-0.02em; --r-control:2px; --r-card:4px; --r-sheet:6px; --shadow-1:none; --shadow-2:none; --shadow-3:none; }
[data-skin="calm"]   { --font-head: var(--font-body), system-ui, sans-serif; --head-track:0; --r-control:12px; --r-card:16px; --r-sheet:24px; --shadow-1:none; --shadow-2:none; --shadow-3:none; --pad-card:2rem; --gap-block:2rem; }
[data-skin="glass"]  { --font-head: var(--f-inter), var(--font-body), system-ui, sans-serif; --head-track:-0.02em; --r-control:12px; --r-card:20px; --r-sheet:28px; --blur:20px;
  --shadow-1:0 8px 24px rgba(40,60,120,.10); --chrome-glass:rgba(244,247,253,.72);
  --page-bg:linear-gradient(160deg,#DCE6F7 0%,#ECE6F7 100%); }
[data-skin="glass"][data-theme="dark"] { --shadow-1:0 8px 24px rgba(0,0,0,.35); --chrome-glass:rgba(18,22,41,.72); --page-bg:linear-gradient(160deg,#0F1322 0%,#171B30 100%); }
[data-skin="gamer"][data-texture="true"] { --page-bg:linear-gradient(var(--c-grid) 1px,transparent 1px) 0 0/32px 32px, linear-gradient(90deg,var(--c-grid) 1px,transparent 1px) 0 0/32px 32px; }
[data-skin="portal"][data-theme="dark"], [data-skin="calm"][data-theme="dark"], [data-skin="gamer"][data-theme="dark"] { --shadow-1:none; --shadow-2:none; --shadow-3:none; }
```
Calm's `--pad-card:2rem` only applies at Comfortable density (the `[data-density="compact"]` block comes later and wins). Both values are steps of the shared 4px scale.
Rules that make the CSS above safe:
```css
body::before { content:""; position:fixed; inset:0; z-index:-1; background:var(--page-bg); pointer-events:none; } /* decoration: never under panels' text */
[data-chrome] { background:var(--chrome-glass); color:var(--c-on-chrome); --c-focus:var(--c-on-chrome); }
@supports (backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px)) {
  [data-skin="glass"]:not([data-solid="true"]):not([data-quiet="true"]) [data-chrome] { -webkit-backdrop-filter:blur(var(--blur)) saturate(1.4); backdrop-filter:blur(var(--blur)) saturate(1.4); }
}
@media (prefers-reduced-transparency: reduce), (prefers-contrast: more), (forced-colors: active), (max-width: 1023px) {
  [data-chrome] { background:var(--c-chrome); -webkit-backdrop-filter:none; backdrop-filter:none; }
}
html:has([data-lecture-root]) [data-chrome], [data-solid="true"] [data-chrome] { background:var(--c-chrome); -webkit-backdrop-filter:none; backdrop-filter:none; }
[data-quiet="true"][data-skin][data-theme] { --shadow-1:none; --shadow-2:none; --shadow-3:none; --page-bg:none; --dur-fast:0ms; --dur-base:0ms; }
@media (forced-colors: active) { [data-skin] { --page-bg:none; } .decorative, [data-skin] ::before, [data-skin] ::after { background-image:none; } }
@media (prefers-contrast: more) { [data-skin] { --page-bg:none; } }
```
Dark-mode edges: in dark themes the opaque panels separate from the canvas by a 1px `border-hair` plus `raised` vs `canvas` step (no reliance on shadow). `border-hair` stays decorative; `border-strong` is the only sole boundary for inputs.

### 2.6 Tailwind additions (paste into `tailwind.config.ts`, `theme.extend`)
```ts
colors: { /* existing... */ chrome: v("chrome"), "on-chrome": v("on-chrome"), grid: v("grid") },
borderRadius: { control: "var(--r-control)", card: "var(--r-card)", sheet: "var(--r-sheet)" },
fontFamily: { sans: ["var(--font-body)", "system-ui", "sans-serif"], head: "var(--font-head)" /* .display uses it */ },
// line-heights on the 4px grid (at 100% text size): 13/20, 15/24, 17/28, 20/28, 24/32, 30/36, 38/44
fontSize: { xs: ["0.8125rem","1.25rem"], sm: ["0.9375rem","1.5rem"], base: ["1.0625rem","1.75rem"], lg: ["1.25rem","1.75rem"],
            xl: ["1.5rem","2rem"], "2xl": ["1.875rem","2.25rem"], "3xl": ["2.375rem","2.75rem"] },
maxWidth: { prose: "68ch", content: "72rem", form: "40rem", narrow: "28rem", wide: "80rem" },
```
`.display` becomes `font-family: var(--font-head); font-weight: var(--head-weight); letter-spacing: var(--head-track);`. `.btn`, `.card`, `.field`, `.panel`, `.tag` already use `rounded-control|card`; shadows already use `--shadow-*`. No component gets a per-theme class.

## 3. How themes combine with existing options

### 3.1 Model
`look = skin x mode`, then overlays in this fixed order (later wins): (1) skin x mode palette, (2) Low-stimulation overlay, (3) Solid surfaces / Grid texture, (4) OS `prefers-contrast: more`, (5) `forced-colors`, (6) Reduce motion (in-app or OS), (7) lecture-route overrides (no blur).
| Existing option | Behaviour with themes |
|---|---|
| Looks Paper, Night desk | = skin `paper` x light / dark. Unchanged. |
| Looks Fog, Dusk | = `paper` + Low-stimulation. Kept for compatibility and tests. On the other four skins the overlay does **not** swap palette (their colours are already tested); it strips shadows, gradient, texture, blur, transitions and corner ticks. |
| Colour mode System / Light / Dark | Orthogonal. Default System (OS `prefers-color-scheme`). Stored key stays `theme`; **UI label becomes "Colour mode"**; new key `skin` holds the theme. |
| Low-stimulation look | Overlay on any skin (3.4). Safety surfaces never reduced. |
| Density, Text size, Reading spacing | Orthogonal, never theme-dependent. |
| Dyslexia-friendly font (deferred) | When on, `--font-head` = the body face in every skin (Gamer mono headings off) and the spacing preset applies. |
| Focus mode | Layout only; no theme effect (4). |
| Reduce motion | Zero durations in every skin; Glass enter animations become instant. |

### 3.2 Preferences UI (Look section; strings follow language-guide.md; no exclamation marks, no clinical words, no "recommended")
Order: Theme, Colour mode, Low-stimulation look, theme-specific switch (only when relevant), Text size, Reading spacing, Density, Reduce motion, preview, Reset.
| Control | Type and label | Options / hint text |
|---|---|---|
| Theme | radiogroup of 5 cards (1 column on phones, 2 from 640px), legend "Theme". Each card >= 64px tall, whole card is the 44px+ target, shows 3 swatch chips (canvas, surface, accent; `aria-hidden`) + name + one line. Arrow keys move and select (radio semantics, one tab stop). | Paper: "Cream page, dark ink, serif headings. The default." Course portal (label "Course portal", Q-PO1): "The look of a course website: dark top bar, light grey page, small corners." Gamer: "Sharp corners, monospace headings, an optional faint grid. No game mechanics." (first sentence changed in redesign-round2 slice 7, when the Grid texture switch shipped) Calm: "Muted green and grey, rounded shapes, no shadows." Glass: "Frosted top bar on wide screens, soft gradient page. Text and the lecture always sit on solid surfaces." One sentence above the cards (the radiogroup's description): "A theme changes colours, corners and heading type. Layout, words and controls stay the same." (Strings replaced 2026-10-31 per redesign-round2-review 3.1: a theme is paint only.) |
| Colour mode | segmented, legend "Colour mode" | System / Light / Dark. Hint: "System follows your device." |
| Low-stimulation look | switch | "Removes shadows, texture, gradients and movement. On Paper the colours soften too. Recording status is never dimmed." (Replaced 2026-10-31 per redesign-round2-review 3.2: true on every skin, no "just"; lives in `PREFS_COPY.lowStimHint` so the copy lint covers it.) |
| Grid texture | switch, shown only when Theme = Gamer, default Off | "Adds a faint grid under the page. Never under lecture text." (Replaced 2026-10-31 per redesign-round2-review 3.2, for slice 7: the old string's "behind" fails the copy lint. Shipped in redesign-round2 slice 7, 2026-10-31, directly under the theme cards: label "Grid texture", status lines "Grid texture on." / "Grid texture off."; Gamer's first sentence now names the grid.) |
| Solid surfaces | switch, shown only when Theme = Glass, default Off | "Replaces the frosted bar with a solid one. Your device setting is respected too." (Shipped in redesign-round2 slice 7, 2026-10-31, word for word, directly under the theme cards: label "Solid surfaces", status lines "Solid surfaces on." / "Solid surfaces off.") |
| Reset | secondary button "Reset look to defaults" at the end of Look | Sets Theme Paper, Colour mode System, Low-stimulation off, texture off, solid off, Text size Medium, Reading spacing Standard, Density Comfortable, Reduce motion off. No confirm (reversible). Status line: "Look reset to defaults." Lecture settings and shortcuts are not touched. |
Live preview: changes already apply to the whole app instantly (existing behaviour, reversible). Add one **Preview card** inside Preferences (static, `aria-hidden` except its caption) that shows, with the current look: a heading, two transcript lines with timestamps, a primary and secondary button, an input, the Recording pill and a Paused pill. It exists so a student sees lecture-relevant elements without leaving Preferences (the panel is hidden on lecture routes). Hover/focus on a theme card does not preview; selecting does (radio). The existing in-panel `role="status"` line announces one plain message per change ("Calm theme on.", "Dark mode on.", "Grid texture on."), same 10-second duplicate drop, and is never mounted on the lecture screen (three live regions unchanged [C]). Dependency from redesign-review ruling 1: move Preferences to a right side panel from 1024px so the page stays visible while choosing; if that slips, T2 still ships with the in-flow panel.
The lecture's "Lecture settings" popover keeps **Colour mode** and **Low-stimulation look** only; the Theme control is not offered mid-lecture (no sudden restyle during a lecture; BL-001 says themes are chosen in Preferences).

### 3.3 Persistence, first paint, system defaults
- `tether.prefs.v1` gains additive fields `skin` ("paper" default), `texture` (false), `solid` (false). `parsePrefs` validates each with `pick`, unknown values fall back to defaults, no version bump (old builds ignore extra fields, so rollback is safe). Storage blocked: defaults, choice applies for the visit.
- `public/prefs-init.js` (synchronous, same-origin, existing pattern) additionally sets `data-skin`, `data-texture`, `data-solid`; if `<html data-themes="off">` (set from the build flag) it forces `data-skin="paper"`. `prefs.test.ts` already runs this file and compares it with `htmlAttributes`; extend both. No inline script.
- Defaults: skin never inferred from the device; mode from `prefers-color-scheme`; Reduce motion honours `prefers-reduced-motion` via CSS even when the in-app switch is off; `prefers-reduced-transparency`, `prefers-contrast`, `forced-colors` are applied in CSS and **do not overwrite stored choices** (the student's Solid surfaces switch stays as they set it).
- `forced-colors: active`: skins contribute no colour; control borders `CanvasText`, focus `Highlight`, texture/gradient/corner ticks removed; statuses keep shape and word.
- `<meta name="color-scheme" content="light dark">` plus existing `themeColor` pair; no flash because attributes are set before body paints.

### 3.4 Selector changes this causes (frontend, with tests updated in the same PR)
`PALETTE_SELECTORS` in `tokens.ts` gains the eight skin blocks; Fog and Dusk selectors become `:root[data-skin="paper"][data-quiet="true"]` and `:root[data-skin="paper"][data-theme="dark"][data-quiet="true"]` so they cannot override another skin; Paper light/dark gain the `[data-skin="paper"]` alias. `ROLES` gains `chrome`, `on-chrome`, `grid`; `TEXT_ROLES` gains `on-chrome` (on `chrome`); a new `GLASS_COMPOSITES` and `GRID_PAIRS` list holds the extra pairs of 2.3. The cascade test resolves, for each of 10 `skin x mode` combinations, the effective value of every role and checks it equals `tokens.ts`.

## 4. The live lecture screen under every theme
**Must not vary (asserted by tests):**
- Recording pill: same markup, 20px glyph shape + word + time, colours only from `recording / recording-subtle` (Recording), `warning` (Paused), `ink on surface` (Not recording), `muted` (Stopped). Never animated, never smaller than 44px tall, same position, visible at 320px and in Focus mode (invariant 2).
- Lecture bar: opaque `surface`, 1px bottom edge, sticky, same height (56px; two rows <= 112px on phones), no blur, no gradient, no chrome roles, no texture behind it.
- Transcript: body face, 17px base, line-height 28px (on grid), 68ch measure, hanging timestamp column with tabular figures, opaque `surface` panel with a visible border, `ink` text, `muted` timestamps, no zebra, no texture or blur under it, reserved partial row unchanged, `role="log"` and `data-segment-id` unchanged.
- TL;DR hierarchy: h2, one-line summary (lg), up to 3 bullets, "Updated ..." footer; Demo summary tag and Demo notice visible and not dismissible; one filled accent control per region (the usual Quick recap window; Resume recording when Paused).
- Quick recap: window buttons 56px (usual 64px), 8px gaps, same order; result heading takes focus; no opacity or filter on any text (Focus mode never dims [C]).
- Live regions: exactly three; themes add none; decorative CSS (corner ticks, grid, highlights) is `::before/::after` only and never in the accessibility tree.
- Focus ring: 3px `focus` + 2px offset + `canvas` halo in every theme; in the (hidden-on-lecture) header `on-chrome`.
**May vary:** palette hue, accent, radii, panel shadow, heading typeface in h2s (TL;DR, Transcript, Quick recap), canvas background outside panels (gradient/grid), button corner shape, the 4px Portal top strip on cards, Gamer corner ticks on cards, Glass inner highlight on panels, Quick recap dock fill style (still opaque).
**Per-theme lecture notes:** Gamer: mono headings only; timestamps may use mono (still tabular, same column width 3.25rem, test checks the column does not shift); no grid under the dock. Glass: header is hidden on lecture routes, so the screen is effectively opaque Glass colours with large radii; blur is off by rule. Calm: extra card padding is OFF for the transcript panel (it keeps 24px) so the viewport height maths in `globals.css` does not change. Portal: the dark chrome is not used (header hidden on lecture routes); the lecture bar stays surface-coloured.

## 5. Layout and alignment audit

Evidence limits [C]: screenshots exist only at 390 and 1280 and only for Paper/Night/Fog/Dusk (all share one grid, so findings apply to every theme). Pixel positions are read from screenshots (+/- 2px). Not captured: 320, 768, 1024, 1440, 1920, scrollbars (headless captures have none), tablet landscape. Every finding below needs a geometry test (5.3) to confirm exact numbers.

### 5.1 Findings (file = in `src/web/test-results/redesign-screens/`)
| ID | Finding | Evidence | Rule that fixes it |
|---|---|---|---|
| A1 | Header right cluster has uneven gaps (Search to Preferences text 20px, to name 27px, to Sign out 17px) because quiet buttons carry invisible padding. Signed-out header: "Preferences" visible right edge at x=1188 while the container edge is 1200. | `dashboard--1280--paper.png`, `signin--1280--paper.png` | R6 optical alignment; header actions use one `gap-2` and `.optical-end` on the last item. |
| A2 | Wordmark text centre is about 3px above the buttons' centre. | `signin--1280--paper.png` | R7 baseline: wordmark `line-height:1` box centred with `items-center` plus 2px cap-height nudge via `padding-top`, or align on first baseline. |
| A3 | Five different content widths and left edges: lists 880 at x=320, form card 749 at x=320, consent card 640 at x=440 (centred), Preferences panel 750 at x=265, sign-in card 448 at x=416. | `courses--1280--paper.png`, `course-new--1280--paper.png`, `consent--1280--paper.png`, `preferences--1280--paper.png`, `signin--1280--paper.png` | R2 templates: left edge is always the content column's left edge (x=320 with rail); narrow templates are left aligned, not centred. |
| A4 | Text inset inside bordered boxes differs: list rows 21px, Next step card 28px (4px stripe + 24), side card 25px. | `dashboard--1280--paper.png` | R8: inset = `--pad-card` + 1px border everywhere (25px); the accent stripe is an inset shadow, not a border. |
| A5 | Preferences: switch rows are inset 8px from the legends above them (text x=299 vs 291); the On/Off + track ends at x=982 while the Close button ends at 990. | `preferences--1280--paper.png` | R8: rows with hover backgrounds use `-mx-2 px-2` so text aligns with siblings and the hover area bleeds outward. |
| A6 | Section header rows centre the heading and its action; baselines differ (h2 "Your courses" vs "Add a course" button label differ about 6px; All courses h1 centre y=107 vs button centre y=112). | `dashboard--1280--paper.png`, `courses--1280--paper.png` | R7: `items-baseline` for heading + action rows. |
| A7 | Quiet text buttons and links sit 2px off their column (Back to dashboard x=322 vs 320, Show options 347 vs 345) and "Cancel" starts 26px after "Save course" instead of 12px. | `courses--1280--paper.png`, `course-new--1280--paper.png` | R6: `.btn-quiet` start/end variants with negative margin equal to padding + border; button groups use `gap-3` measured edge to edge of the visible ink. |
| A8 | Control heights: primary buttons render 44px (design-direction.md says 48); in the lecture bar the Recording pill is about 42px while Pause/Stop/Focus are about 47-48px. | `dashboard--1280--paper.png`, `lecture-live--1280--paper.png` | R4: one control height 44, large 56 (Quick recap windows, usual 64); pill and bar buttons both 44. This supersedes the "primary 48" line in design-direction.md section 4. |
| A9 | Two-column lecture: the right rail's first card top (y=225) aligns with the "Transcript" heading, but the transcript panel starts at y=266, so the first boxes do not share a top edge. | `lecture-live--1280--paper.png` | R3: first bordered box in every column shares one top edge; move the Transcript heading inside the panel as its card header. |
| A10 | Stopped screen toolbar: "Export to Google Docs" floats at x=528 mid-column, its helper wraps in a 260px column, the search field is 436 wide against a 736 transcript, and the TL;DR card top (388) lines up with the button, not the search label (417). | `lecture-stopped--1280--paper.png` | R3 + R9: toolbar is a grid row with search field full column width and the action right-aligned to the column edge; helper below. |
| A11 | Focus mode: column spans x=240-1040 while the bar and Demo notice span 80-1200; the Quick recap dock spans 225-1055, overhanging the column by 15px each side. | `lecture-focus--1280--paper.png` | R2: dock width and gutters equal the column (`max-w-[52rem]`, same `px`). |
| A12 | Phone: row meta wraps with an orphan ("yet"); the visible gaps in the header (Menu to Preferences 27px, Preferences to Sign out 17px) are uneven; card text inset 41px from the viewport vs 16px page gutter for other text. | `dashboard--390--paper.png` | R6, R8; meta uses `text-wrap: pretty` and hides "No lectures yet" below 480px. |
| A13 | Off-scale values in source: `px-2.5` (10px) on header quiet buttons, `py-0.5`, `min-h-[1.6rem]` (25.6px), `pt-[0.2em]`, body line-height 1.55 (26.35px) and transcript 1.6 (27.2px) are not multiples of 4. | `globals.css` lines 87-88, 283, 297; `AppShell.tsx` lines 145, 156 | R1: scale only; line-heights 20/24/28/32/36/44. |
| A14 | Sign-in card is vertically placed at an arbitrary y=137 and repeats the wordmark in an eyebrow ("TETHER") under a wordmark. | `signin--1280--paper.png` | R2: top offset 96px; drop the eyebrow (review N3). |
| A15 | Page width at large screens is capped at 72rem for everything; at 1920 this leaves 384px margins and the bar's bottom rule is full bleed while content is not (unverified, not captured). | no screenshot | R2: reading pages 72rem, lecture 80rem from 1536px; verify at 1440/1920. |
| A16 | No `scrollbar-gutter: stable`: on systems with classic scrollbars the container shifts ~8-15px between short and long pages (unverified, headless has no scrollbars). | none | R10. |
| A17 | Touch targets: header, rail, rows, switches and dock measure >= 44px by eye; TL;DR "More" and citation timestamps were not measured. | `lecture-live--1280--paper.png` | Geometry test measures every interactive element (5.3). |
What lines up well already (keep): wordmark and rail share x=80; rail top = main top (y=89); dashboard main and side cards share a top and the 8/4 split with 40px gutter; transcript hanging column; dock buttons equal widths and 8px gaps; phone page gutters are 16px and cards reach x=374.

### 5.2 One grid and spacing system, shared by every theme
- **R1 Scale.** Base 4px. Allowed spacing: 4, 8, 12, 16, 20, 24, 32, 40, 48, 64, 96. Fixed sizes: control 44, large control 56 (Quick recap usual 64), header 56, rail 200, aside 20rem (lecture aside 22.5rem), icon 20, border 1/2. Half-step classes (`.5`) and arbitrary pixel values are banned except an allowlist (`border`, `outline`, `ring`, `min-h-[44px]`, `grid-cols-[3.25rem_...]`). Baseline: every text line box is a multiple of 4px (type scale 13/20, 15/24, 17/28, 20/28, 24/32, 30/36, 38/44); this is a rhythm grid on line boxes, not a strict baseline grid [J].
- **R2 Container and templates.** Container `max-w-content` (72rem; 80rem from 1536px on lecture) centred. Gutters 16px (<640), 24px (640-1023), 32px (>=1024). Rail 200 + 40 gap from 1024. Templates, all left-aligned to the column start: *Fill* (lists, 100%), *Split* (main 1fr + aside 20rem, gap 40, from 1024; stacked below), *Form* (`max-w-form` 40rem: add course, consent, Preferences), *Prose* (68ch), *Auth* (`max-w-narrow` 28rem, centred in the viewport because no rail, top offset 96px). Breakpoints 360 / 640 / 1024 / 1536; 320 must not scroll horizontally.
- **R3 Shared edges.** Wordmark left = rail left = container left. Header right edge, page-action buttons and cards share the container right edge (+/-1px). The first bordered box of each column shares one top edge. Sticky/fixed layers (dock, bar, dialogs) use the same column width and gutter as the content they sit over.
- **R4 Control sizes.** Buttons, fields, switches, rail items, rows with actions: min height 44, same height in a row. Primary is not taller than secondary. Quick recap windows 56 (usual 64). Tags 28 (not a target unless interactive). Icon + text: icon box 20, `gap-2`, centred on the first line (`items-start` + 2px when text wraps).
- **R5 Cards.** One padding `--pad-card` (24 Comfortable, 16 Compact, Calm 32) + 1px border; list rows inside a card use the same horizontal inset (row padding is vertical only: 16). Heights follow content; sibling cards in one row stretch (`align-items: stretch`) only when marked `data-equal`.
- **R6 Optical alignment.** Visible ink, not the box, sits on the shared edge: text-only buttons carry `.optical-start` / `.optical-end` (margin = horizontal padding + border, 14px at `btn`, 12px at `btn-sm`). Gaps between adjacent buttons are measured between visible edges, 12px.
- **R7 Baselines.** Heading + action rows: `items-baseline`. Label/field/help: label above (gap 4), field, help below (gap 4), groups 24 apart, all on the column's left edge. Eyebrow to heading gap 4. Mixed-size inline groups use `items-baseline`.
- **R8 Insets.** Text inside any bordered surface starts at `--pad-card` + border. Hover backgrounds on rows bleed outward (`-mx-2 px-2`), text never moves.
- **R9 Toolbars.** A toolbar is a 2-column grid: field/primary content `1fr`, action `auto`, right action ends on the column edge; helper text sits below, never beside.
- **R10 Stability.** `html { scrollbar-gutter: stable; }`; reserve heights for loading placeholders; fonts use `size-adjust` fallback (next/font does this) so a theme switch does not reflow boxes; heading faces may change line breaks but not box heights (fixed `line-height` token).
- **R11 Themes cannot move boxes.** A theme may not set `padding`, `margin`, `gap`, `width`, `height`, `border-width` (except 1px/2px already defined), `display`, `position` or `order`. It may set colour, radius, shadow, font-family/weight/tracking, `background`, decorative pseudo-elements. Calm's roomier `--pad-card` is the single sanctioned exception (a scale step).

### 5.3 Automated alignment tests (Playwright; screenshots are not enough)
- `e2e-screens/geometry.ts` helper: `box(locator)` via `getBoundingClientRect`, `ink(locator)` (text range rect), `baseline(locator)` (inject a zero-height inline-block, read its y), `px(locator, prop)` via `getComputedStyle`.
- Assertions per screen (run for every `skin x mode`, widths 320, 390, 768, 1024, 1280, 1440, 1920): (1) wordmark left = rail left = container left (+/-1); (2) rightmost header action ink right = container right (+/-1); (3) `main` first heading left = `[data-column-start]` (+/-1) on every page; (4) every `button, [role=button], [role=switch], [role=radio], a.btn, input, select` is >= 44x44, except links inside prose >= 24 high with 8px spacing (log, then enforce); (5) in `[data-card]` text ink left = card left + pad + 1; (6) sibling controls in a row have equal height and equal vertical centre (+/-1); (7) heading+action rows share a baseline (+/-1); (8) no horizontal overflow (`scrollWidth <= clientWidth`) at 320; (9) fixed/sticky layers' left/right = column edges; (10) on-grid check: every `[data-card]`, `.btn`, `.field`, `.panel` computed padding/margin/gap/height % 4 === 0 except allowlist (ratchet: report first, fail on regression).
- **Theme independence:** run the same suite for all 10 looks and also compare the *set of rects* of 12 landmark elements across themes: container, header, rail, main, first card, primary button, field, transcript panel, Quick recap card, TL;DR card, recording pill, dock. Expected equal within 1px except Calm's card padding (and heading-driven text wrap inside cards, compared by landmark not by text).
- Lint/unit: a source scan fails on `\b(p|m|gap|space-[xy])[xytblr]?-\d+\.5\b` and on arbitrary px values outside the allowlist.
- Visual snapshots (`toHaveScreenshot`, `animations: "disabled"`, `await document.fonts.ready`, Linux container baselines only, `maxDiffPixelRatio` 0.002): 6 key screens (sign-in, dashboard, course-new, consent, lecture-live, lecture-quick-recap open) x 10 looks x 3 widths (320, 768, 1280) = 180, plus Low-stimulation on lecture-live x 5 skins, plus Gamer texture on and Glass solid on. Snapshots are a review aid; geometry and axe assertions are the gate.

## 6. Implementation plan (frontend-engineer), shippable slices
Frozen e2e names and test ids in design-direction.md section 8 are unaffected: this spec changes no text, role, id, order or ARIA, except: Preferences legend "Theme" becomes "Colour mode" for the light/dark control (check `AppShell.test.tsx` and `LectureRedesign.test.tsx` for the pinned "Theme" legend; update in the same PR) and a new "Theme" legend for skins.
| Slice | Content | Acceptance criteria | Tests |
|---|---|---|---|
| T0 Token plumbing (invisible) | `tokens.ts`: Skin, 8 skin palettes, new roles, selectors; `globals.css` blocks (2.4, 2.5) with Paper unchanged; `prefs.ts` + `prefs-init.js` fields `skin/texture/solid` and `data-themes` flag; Tailwind additions (2.6); next/font variables renamed; rename `.display` to variables | App looks identical with `skin=paper`; setting `data-skin` by hand in devtools themes the app with no console errors; flag `off` forces paper | Extended `tokens.test.ts`: full matrix per palette (all text roles on all backgrounds, boundaries 3:1, on-accent, on-chrome/chrome, GRID_PAIRS, GLASS_COMPOSITES), cascade equals data for 10 looks, hand-values within 0.02 of 2.3; `prefs.test.ts` incl. init script parity; no raw colours guard still green |
| T1 Layout system | R1-R11: type scale, line-heights, container/templates, gutters, control height, optical utilities, header gap fix, Preferences/consent/form templates left aligned, transcript heading inside panel, Stopped toolbar grid, Focus dock width, `scrollbar-gutter`; fix A1-A14 | Geometry suite (5.3) passes for Paper at 7 widths; 320 no horizontal scroll; existing unit 619+ and harness 47 and courses 11 e2e still pass (no selector edited except where a spacing class is renamed) | `geometry.spec.ts`, spacing scan, axe all screens, snapshots updated by humans after review |
| T2 Preferences UI + Portal + Calm | Theme radiogroup, Colour mode relabel, Reset, Preview card, status messages; Portal and Calm styles (strip, header chrome, card top strip); Calm roominess | Choosing a theme applies at once, persists across reload with no flash, announced once; Reset works; Low-stimulation strips Portal strip and shadows; Preferences on phone at 320 does not scroll sideways | Component tests (keyboard: one tab stop, arrows select, Escape closes), axe per skin x mode on dashboard + Preferences, contrast test, no-flash test (load with stored dark portal, first paint attribute set before body) |
| T3 Gamer | Mono headings via `next/font` (preload false), corner ticks, header edge, Grid texture switch, no glow | Texture off by default and absent from lecture panels; mono limited to headings, eyebrow, tags, timestamps; no animation | axe per mode, snapshot, test that `--page-bg` is `none` when `data-texture` is not `"true"` and in Low-stimulation |
| T4 Glass | Gradient `body::before`, translucent header with fallbacks, Solid surfaces switch, enter animation 200ms | All fallbacks in 2.5 verified by test (reduced-transparency, contrast more, forced colors, <1024px, lecture, no-support); no element with backdrop-filter contains forbidden content | Playwright `emulateMedia({reducedMotion, forcedColors, contrast})` computed-style checks; DOM scan for blur ancestors; Chrome performance trace at 4x CPU throttle on dashboard scroll (target p95 frame <= 16.7ms, [J] target) |
| T5 Lecture pass | Verify section 4 under 10 looks: pill, bar, transcript, TL;DR, dock, panels; Focus mode; phone 320 | All "must not vary" items asserted; live-region count 3; label-in-name passes; Focus never uses opacity/filter | Existing harness redesign tests extended to loop over skins; axe on each lecture fixture x 10 looks; geometry equality across themes |
| T6 Matrix in CI | Snapshot and axe jobs, bundle budget check | CI runs unit + harness + geometry for all looks (nightly full matrix, PR subset: Paper, Portal dark, Gamer dark, Calm light, Glass light) | CI run records are the evidence; none exist yet |
Performance budget [J targets, measure and adjust]: added global CSS <= 4KB gzip; each theme heading face <= 60KB woff2 transferred, and only the active theme's face downloads; body face unchanged; no extra JS beyond prefs fields (< 1KB); `backdrop-filter` on <= 2 elements, 20px max, never < 1024px or on lecture routes; no animated gradients; `body::before` is one static composited layer.
Rollback: `NEXT_PUBLIC_THEMES=off` (build-time) sets `<html data-themes="off">`; init script forces `data-skin="paper"`, the Theme control and its extra switches are not rendered, stored skins stay in storage. Also each slice is independent: removing one skin's CSS block and its radio card leaves others working (unknown stored skin falls back to Paper).
Copy: all new strings in `copy.ts`-style registry and covered by `copy.test.ts` (no exclamation marks, none of the forbidden words). Do not describe any theme as helping attention or focus.

## 7. Risks, open decisions, questions

Risks: (1) Hand-computed contrast may be wrong; the test wins, and three margins are thin (2.3). (2) Five themes x two modes x overlays multiply the test matrix and the maintenance cost; ship Portal and Calm first, Gamer and Glass behind the same flag if capacity is short. (3) Gamer and Glass are the highest sensory-load options even in restrained form; they are opt-in and never default. (4) Glass legibility depends on browser support for `backdrop-filter` and the reduced-transparency query, which I could not verify; the opaque fallback is the real baseline. (5) Heading font changes can shift line breaks and expose latent layout bugs (that is also why R10/R11 exist). (6) Font names in the Next catalog are unverified (build check). (7) The "Canvas" name may raise a trademark or confusion issue (Q-PO1; `legal-compliance-reviewer` can look at the label only, not recording matters). (8) Themes could be read as "ADHD modes"; copy avoids that. (9) Preferences still pushes content down until it becomes a side panel (A5, review ruling 1). (10) 320-1920 behaviour is unmeasured today.
Open decisions: Q-PO1 label for the portal theme ("Canvas" as named in BL-001 vs "Course portal"); Q-PO2 should Gamer and Glass ship in the first release or after T2 feedback; Q-LEAD1 supersede "primary 48px" with 44 (A8); Q-LEAD2 theme-color meta per theme (skipped here).
Questions for real students (via human-research-coordinator; include varied attention patterns and students who dislike all of this; G4 NOT_RUN, nothing here is validated):
1. Which theme and mode could you read a transcript in for 50 minutes, and which tired your eyes first?
2. Does the Gamer theme feel focused or loud? Does the grid help, distract or go unnoticed?
3. Does the Course portal look make the app feel familiar, or like homework?
4. Is Calm calming, dull, or hard to tell apart from Paper? Does the extra room help you find things?
5. Is anything on Glass harder to read, or does the frosted bar bother you? Would you turn Solid surfaces on?
6. Did you find Theme and Colour mode in Preferences without help? Was Reset easy to find and trust?
7. Did the recording indicator stay as clear in every theme?
8. Did changing the theme ever surprise or annoy you mid-task?

## Summary
1. Five paint-only themes (Paper, Canvas/portal, Gamer, Calm, Glass), each light and dark, are defined with full token sets, 8 paste-ready palette blocks and hand-computed contrast (test recompute wins).
2. Model is Theme x Colour mode with overlays (Low-stimulation, Reduce motion, Grid texture, Solid surfaces); Fog and Dusk stay as Paper plus Low-stimulation; first paint stays flash-free via the existing init script.
3. Glass is made safe by design: translucency only on the site header, opaque fallbacks for every preference and all lecture routes, and no text over blur; Gamer has no game mechanics, glow or default texture.
4. The lecture screen keeps pill, bar, transcript, TL;DR, Quick recap and three live regions identical across themes; only colour, radii, heading face and decoration vary.
5. The audit found 17 alignment issues (five content widths, inconsistent insets, uneven header gaps, mismatched control heights, two-column tops, Focus dock overhang), fixed by one 4px grid, five page templates, optical-alignment rules and Playwright geometry tests across 10 looks and 7 widths.
6. Seven ordered slices (T0 to T6) with a build-flag rollback; nothing is human-validated (G4 NOT_RUN), nothing was run by me, and the "Canvas" theme name needs a PO decision.
