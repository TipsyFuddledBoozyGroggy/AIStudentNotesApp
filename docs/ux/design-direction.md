# Tether Visual and Interaction Design Direction (UI/UX refactor)

Status: DESIGN DECISION v1, 2026-10-29. Author: adhd-ux-specialist. Audience: frontend-engineer. Nothing here is built, rendered or tested by me.
**Gate G4 stays NOT_RUN: nothing below is human-validated.** Items marked [J] are design judgement, [E] are published findings recalled from memory (not re-fetched; verify before quoting externally), [C] are project constraints. Consent and recording wording stays DRAFT under G1 (BLOCKED); this refactor must not change its meaning.
Inputs read: CLAUDE.md (invariants 2, 7, 9), student-experience.md, m2-live-lecture-screens.md, m2-quick-recap-review.md headers, phase2-syllabus-review.md section 5, calendar-reminders.md sections 0-2, all of `src/web/src/app` and `components/` shell, status bar, transcript, quick-recap code, `settings.ts`, `copy.ts`, and e2e specs.
Method note: I had no shell in this session. Every contrast ratio below was computed by hand with the WCAG 2.x formula (below), to two decimals. Slice 0 must add a unit test that recomputes them (hand arithmetic can be wrong); a failing recomputation overrides this table.

## 1. Principles and tone

Concept: **the reading room**. Warm paper, dark ink, a serif voice for headings, one ink-blue accent, hairline rules instead of boxes, no decoration that carries no meaning. "Hip Atticus Finch" = a quiet, well-made study with good lighting, not a dashboard of badges.

Principles (each is a testable rule):
1. One primary action per screen, one per state. The primary button is the only filled accent element in its region.
2. Calm by default, rich on request. Everything beyond the next action sits behind a disclosure, the command palette or Preferences. Defaults are never more stimulating than the previous screen.
3. The lecture is sacred. During a lecture nothing moves, flashes, reorders or asks. Only the student's actions and safety notices change the layout.
4. Truth over comfort. Recording status is always visible and never softened (invariant 2).
5. Return without penalty. Every screen can be left and re-entered; state is saved; no counters of time away.
6. Student-owned. Look, density, sound, announcements and shortcuts are student settings. We never infer attention; no camera/mic-level/inactivity signals (invariant 7).
7. No gamification: no streaks, points, badges, levels, confetti, progress-shaming bars, red counters, "overdue" alarms, leaderboards. Progress is shown as plain text ("9 of 24 decided") only where it helps a decision.

Voice rules (apply to every string; `copy.ts` lint extends the existing forbidden list):
- Plain, concrete, adult. Say what is true now, then the next action. Under 20 words per sentence. Active voice.
- Warm without chirpiness: no exclamation marks, emoji, "Oops", "Awesome", "Don't worry", "Just".
- Dignified: second person, never childish, never clinical. Never name ADHD, "focus problems", "distracted", "behind", "overdue", "missed", "failed", "streak", "lazy".
- Errors blame the system, not the student, and always state whether anything was lost.
- A serious register with one allowed flourish: short declarative sentences. Humor is not used.

Ten before/after examples (before = current string; strings in e2e are frozen, see section 8 slice 6; these are proposals for non-frozen strings only):
| # | Before | After |
|---|---|---|
| 1 | Development sign-in. No password needed. Real Google sign-in comes later. | This is a development sign-in. Enter any school-style email. Google sign-in arrives later. |
| 2 | Ready when you are. Lecture capture is coming next. | Open this course to start a lecture. Nothing is recorded until you say so. |
| 3 | Coming later / Planner and study mode are not built yet. | (removed from the dashboard; roadmap lives under Preferences > About) |
| 4 | Loading your courses... | Getting your courses. |
| 5 | Pick how far back to catch up. The lecture keeps going. | How far back? The lecture keeps going. |
| 6 | The recap is taking longer than expected. What was said is below. | Still writing your recap. Here is what was said so far. |
| 7 | Summaries are paused for this lecture. Here's what was said. | A recap isn't available for this lecture. Here is what was said. |
| 8 | Something doesn't match. We turned the microphone off to be safe. Reload the state to continue. | The app and your microphone disagree, so the microphone is off. Nothing is being recorded. Choose Check again. |
| 9 | You have 3 overdue items! (anti-example, never built) | Three items are waiting for a decision. Open them when you want to. |
| 10 | Lines will appear here as the lecture goes on. | Words will appear here once the lecture starts. |

## 2. Design tokens

### 2.1 Contrast method
Relative luminance L = 0.2126 R + 0.7152 G + 0.0722 B, each channel c = v/255 then lin = c/12.92 if c <= 0.03928 else ((c + 0.055)/1.055)^2.4. Ratio = (L1 + 0.05)/(L2 + 0.05), lighter over darker (WCAG 2.2 SC 1.4.3 definition). Targets: text and text-like icons >= 4.5:1; large text (>= 24px, or >= 18.66px bold) and UI boundaries/focus/state shapes >= 3:1 (SC 1.4.11). **Hand-computed, unverified by tool.** Opacity modifiers (`text-ink/60`) are banned because they create unlisted pairs: tokens are plain colors, no `<alpha-value>`.

### 2.2 Roles and values
CSS variable names are the contract; hex values are the decision.

| Role (var) | Light "Paper" | Dark "Night desk" | Quiet-light "Fog" | Quiet-dark "Dusk" |
|---|---|---|---|---|
| canvas (--c-canvas) | #F5F2EA | #14161B | #EEEDE8 | #14161B |
| surface (--c-surface) | #FFFFFF | #1C1F26 | #F6F5F1 | #1C1F26 |
| raised (--c-raised) | #FFFFFF | #242831 | #F6F5F1 | #242831 |
| sunken (--c-sunken) | #ECE8DE | #0F1115 | #E4E2DB | #0F1115 |
| ink (--c-ink) | #1C1B19 | #ECEAE4 | #2B2A27 | #D6D4CE |
| muted (--c-muted) | #57534A | #A3A7B3 | #5E5B54 | #A3A7B3 |
| accent (--c-accent) | #2E3C8C | #A9B8FF | #3F4A6B | #B4BCD6 |
| on-accent (--c-on-accent) | #FFFFFF | #10131F | #FFFFFF | #10131F |
| accent-subtle (--c-accent-subtle) | #E6E8F5 | #242831 | #F6F5F1 | #242831 |
| recording (--c-recording) | #B42318 | #FF8A7A | #A12B20 | #FF8A7A |
| recording-subtle | #FCEBE8 | #242831 (stripe carries color) | #F6F5F1 | #242831 |
| success (--c-success) | #1B6B3A | #6FD39A | #1B6B3A | #6FD39A |
| warning (--c-warning) | #7A4300 | #F2B84B | #7A4300 | #F2B84B |
| danger (--c-danger) | #9F1D24 | #FF8F98 | #9F1D24 | #FF8F98 |
| success/warning-subtle | #E7F3EA / #FBF0D9 | #242831 | #F6F5F1 | #242831 |
| border-strong (--c-border-strong, inputs, control edges) | #8A8579 | #7A7F90 | #7F7B70 | #7A7F90 |
| border-hair (--c-border-hair, decorative only, never the sole boundary) | #DAD5C8 | #2E333D | #D6D4CC | #2E333D |
| focus (--c-focus) | #2E3C8C | #A9B8FF | #3F4A6B | #B4BCD6 |

Rules: "recording" is the only red in the product and is used only for live-capture state. "danger" is for destructive/failed actions and is never used for recording. Accent is never used for status. Warning amber is used for notices that need attention but are not harmful (Demo tag, Not connected). Every status always has a shape and a word.

### 2.3 Contrast table (every pair used; ratio = hand-computed)
Light, backgrounds canvas #F5F2EA (L 0.8885), surface #FFFFFF (1.0), sunken #ECE8DE (0.8082):
| Foreground on background | Ratio | Need | Pass |
|---|---|---|---|
| ink #1C1B19 on surface / canvas / sunken | 17.21 / 15.38 / 14.07 | 4.5 | yes |
| muted #57534A on surface / canvas / sunken | 7.67 / 6.85 / 6.26 | 4.5 | yes |
| accent #2E3C8C on surface / canvas / sunken | 9.81 / 8.77 / 8.02 | 4.5 | yes |
| accent on accent-subtle #E6E8F5; ink on accent-subtle | 8.05; 14.10 | 4.5 | yes |
| on-accent #FFFFFF on accent (primary button) | 9.81 | 4.5 | yes |
| recording #B42318 on surface / canvas | 6.58 / 5.88 | 4.5 | yes |
| recording on recording-subtle #FCEBE8; white on recording | 5.69; 6.58 | 4.5 | yes |
| success #1B6B3A on surface / canvas / success-subtle #E7F3EA | 6.54 / 5.85 / 5.73 | 4.5 | yes |
| warning #7A4300 on surface / canvas / warning-subtle #FBF0D9 | 7.99 / 7.14 / 7.06 | 4.5 | yes |
| danger #9F1D24 on surface / canvas | 7.85 / 7.02 | 4.5 | yes |
| border-strong #8A8579 vs surface / canvas | 3.68 / 3.29 | 3.0 | yes |
| focus #2E3C8C vs canvas / surface | 8.77 / 9.81 | 3.0 | yes |

Dark, backgrounds canvas #14161B (L 0.0080), surface #1C1F26 (0.0137), raised #242831 (0.0211):
| Foreground | on canvas / surface / raised | Need | Pass |
|---|---|---|---|
| ink #ECEAE4 | 15.04 / 13.71 / 12.27 | 4.5 | yes |
| muted #A3A7B3 | 7.53 / 6.86 / 6.14 | 4.5 | yes |
| accent #A9B8FF | 9.47 / 8.63 / 7.72 | 4.5 | yes |
| on-accent #10131F on accent (button) | 9.68 | 4.5 | yes |
| recording #FF8A7A | 7.90 / 7.20 / 6.45 | 4.5 | yes |
| success #6FD39A | 9.88 / 9.00 / 8.05 | 4.5 | yes |
| warning #F2B84B | 10.11 / 9.17 / 8.25 | 4.5 | yes |
| danger #FF8F98 | 8.30 / 7.56 / 6.77 | 4.5 | yes |
| border-strong #7A7F90 | 4.54 / 4.14 / 3.70 | 3.0 | yes |
| focus = accent | 9.47 / 8.63 / 7.72 | 3.0 | yes |

Quiet-light Fog, backgrounds canvas #EEEDE8 (L 0.8457), surface #F6F5F1 (0.9125):
ink #2B2A27 12.24 / 13.16; muted #5E5B54 5.78 / 6.21; accent #3F4A6B 7.46 / 8.01; white on accent 8.74; recording #A12B20 6.22 / 6.68, white on recording 7.29; success #1B6B3A on canvas 5.58; warning #7A4300 on canvas 6.81; danger #9F1D24 on canvas 6.70; border-strong #7F7B70 3.61 / 3.87. All pass.
Quiet-dark Dusk (canvas/surface as Dark): ink #D6D4CE 12.21 / 11.13; accent #B4BCD6 9.57 / 8.72; muted and status colors as Dark (rows above). All pass.
Not listed means not allowed: any new pair needs a row (add to the Slice 0 test).
Modes also required: `prefers-contrast: more` maps ink to #000000 (light) / #FFFFFF (dark), border-hair = border-strong; `forced-colors: active` uses system colors for text, borders and focus (`Highlight`), and every status keeps its shape and word.

### 2.4 Quiet / low-stimulation look (distinct from the "Help: Quiet" engagement level)
UI label: **Low-stimulation look** (avoids confusion with the existing read-only "Help: Quiet" label, which is an engagement level). Attribute `data-quiet="true"` on `<html>`. It is a separate palette (Fog/Dusk) plus rules: no card shadows or tints (borders only); one accent element per screen; no entrance transitions at all; TL;DR shows 1 bullet until "Show more"; decorative rules removed; hover states change border, not fill; serif headings drop to the body sans at the same weight is NOT done (hierarchy is kept). Safety surfaces (recording indicator, Not connected, mic notices) are never reduced in prominence. Whether quiet palettes actually reduce load for any student is an untested hypothesis.

### 2.5 Type
- Headings: **Newsreader** (Google Fonts, OFL, variable with optical size), via `next/font/google` (self-hosted at build, no runtime request). Weights 500-600 only. Used for h1-h3 and the wordmark, never for UI controls, timestamps or dense data.
- Body, UI, transcript: **Atkinson Hyperlegible Next** (Braille Institute, OFL, designed for character distinction). If the installed Next 15.x font catalog does not list it, use **Atkinson Hyperlegible** (400/700); frontend verifies at build and records which one shipped. Tabular figures on for timestamps (`font-variant-numeric: tabular-nums`).
- Reading setting "Dyslexia-friendly reading": loads **OpenDyslexic** (OFL; `@fontsource/opendyslexic` via `next/font/local`, loaded only when enabled) and applies wider spacing (letter 0.04em, word 0.12em, line-height 1.75). [E] controlled studies have not shown consistent benefit of dyslexia-specific typefaces for most readers (e.g., Marinus et al. 2016; Wery and Diliberto 2017, recalled, verify); it is offered purely as personal preference, not as a treatment. The default face is already a legibility-first design.
- Scale (rem; root 16px; body 17px): xs 0.8125 (13), sm 0.9375 (15), base 1.0625 (17), lg 1.25, xl 1.5, 2xl 1.875, 3xl 2.375. Line-height: body 1.55, UI 1.4, headings 1.2. Max measure 68ch for prose and transcript. Text-size setting S/M/L sets root 15/16/18px; all rem, 200% zoom and 400% reflow must not clip.
- Minimum text size is 13px (timestamps, tags only); never below.

### 2.6 Spacing, radii, elevation, motion
- Spacing: 4px base: 1=4, 2=8, 3=12, 4=16, 5=20, 6=24, 8=32, 10=40, 12=48, 16=64. Density "Comfortable" (default) uses space 4-6 between blocks; "Compact" uses 2-3 (controls keep >= 32px, primary controls keep 48px).
- Radii: control 8px, card 12px, sheet/dialog 16px, pill 9999px (recording pill, tags only).
- Elevation: 0 flat (default, hairline border), 1 card `0 1px 2px rgba(28,27,25,.06)`, 2 popover/palette `0 8px 24px rgba(28,27,25,.14)`, 3 dialog `0 16px 40px rgba(28,27,25,.2)` with scrim `rgba(20,22,27,.55)`. Dark: no shadows; raised surface plus border-hair. Quiet: elevation 0 only (dialogs keep scrim).
- Motion: durations 0 / 120 / 200 ms; easing `cubic-bezier(.2,0,0,1)`; only opacity and transform (translateY <= 8px); no looping or pulsing animation anywhere (the recording dot is static); no parallax, auto-scroll animation or skeleton shimmer (use static placeholder blocks). `prefers-reduced-motion` or the in-app Reduce motion setting sets all durations to 0 (keep the existing `[data-reduce-motion]` and media-query overrides). Quiet look: all 0.

### 2.7 Paste-ready config
```ts
// tailwind.config.ts
import type { Config } from "tailwindcss";
const v = (n: string) => `var(--c-${n})`;
export default {
  content: ["./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        canvas: v("canvas"), surface: v("surface"), raised: v("raised"), sunken: v("sunken"),
        ink: v("ink"), muted: v("muted"), accent: v("accent"), "on-accent": v("on-accent"),
        "accent-subtle": v("accent-subtle"), recording: v("recording"), "recording-subtle": v("recording-subtle"),
        success: v("success"), "success-subtle": v("success-subtle"), warning: v("warning"),
        "warning-subtle": v("warning-subtle"), danger: v("danger"), line: v("border-strong"), hair: v("border-hair"), focus: v("focus"),
      },
      fontFamily: { sans: ["var(--font-body)", "system-ui", "sans-serif"], serif: ["var(--font-head)", "Georgia", "serif"] },
      fontSize: { xs: ["0.8125rem", "1.4"], sm: ["0.9375rem", "1.4"], base: ["1.0625rem", "1.55"], lg: ["1.25rem", "1.4"],
        xl: ["1.5rem", "1.2"], "2xl": ["1.875rem", "1.2"], "3xl": ["2.375rem", "1.2"] },
      borderRadius: { control: "8px", card: "12px", sheet: "16px" },
      boxShadow: { 1: "var(--shadow-1)", 2: "var(--shadow-2)", 3: "var(--shadow-3)" },
      transitionDuration: { fast: "var(--dur-fast)", base: "var(--dur-base)" },
      maxWidth: { prose: "68ch" },
      minHeight: { target: "44px", primary: "48px" }, minWidth: { target: "44px", primary: "48px" },
    },
  },
} satisfies Config;
```
```css
/* globals.css (tokens). The pre-hydration script in layout sets data-theme to the RESOLVED "light"|"dark" and data-quiet. */
:root { --c-canvas:#F5F2EA; --c-surface:#FFFFFF; --c-raised:#FFFFFF; --c-sunken:#ECE8DE; --c-ink:#1C1B19; --c-muted:#57534A;
  --c-accent:#2E3C8C; --c-on-accent:#FFFFFF; --c-accent-subtle:#E6E8F5; --c-recording:#B42318; --c-recording-subtle:#FCEBE8;
  --c-success:#1B6B3A; --c-success-subtle:#E7F3EA; --c-warning:#7A4300; --c-warning-subtle:#FBF0D9; --c-danger:#9F1D24;
  --c-border-strong:#8A8579; --c-border-hair:#DAD5C8; --c-focus:#2E3C8C;
  --shadow-1:0 1px 2px rgba(28,27,25,.06); --shadow-2:0 8px 24px rgba(28,27,25,.14); --shadow-3:0 16px 40px rgba(28,27,25,.2);
  --dur-fast:120ms; --dur-base:200ms; color-scheme:light; }
[data-theme="dark"] { --c-canvas:#14161B; --c-surface:#1C1F26; --c-raised:#242831; --c-sunken:#0F1115; --c-ink:#ECEAE4; --c-muted:#A3A7B3;
  --c-accent:#A9B8FF; --c-on-accent:#10131F; --c-accent-subtle:#242831; --c-recording:#FF8A7A; --c-recording-subtle:#242831;
  --c-success:#6FD39A; --c-success-subtle:#242831; --c-warning:#F2B84B; --c-warning-subtle:#242831; --c-danger:#FF8F98;
  --c-border-strong:#7A7F90; --c-border-hair:#2E333D; --c-focus:#A9B8FF; --shadow-1:none; --shadow-2:none; --shadow-3:none; color-scheme:dark; }
[data-quiet="true"] { --c-canvas:#EEEDE8; --c-surface:#F6F5F1; --c-raised:#F6F5F1; --c-sunken:#E4E2DB; --c-ink:#2B2A27; --c-muted:#5E5B54;
  --c-accent:#3F4A6B; --c-accent-subtle:#F6F5F1; --c-recording:#A12B20; --c-recording-subtle:#F6F5F1; --c-success-subtle:#F6F5F1;
  --c-warning-subtle:#F6F5F1; --c-border-strong:#7F7B70; --c-border-hair:#D6D4CC; --c-focus:#3F4A6B;
  --shadow-1:none; --shadow-2:none; --shadow-3:none; --dur-fast:0ms; --dur-base:0ms; }
[data-theme="dark"][data-quiet="true"] { --c-canvas:#14161B; --c-surface:#1C1F26; --c-raised:#242831; --c-sunken:#0F1115; --c-ink:#D6D4CE;
  --c-accent:#B4BCD6; --c-accent-subtle:#242831; --c-recording:#FF8A7A; --c-recording-subtle:#242831; --c-success-subtle:#242831;
  --c-warning-subtle:#242831; --c-border-strong:#7A7F90; --c-border-hair:#2E333D; --c-focus:#B4BCD6; }
:focus-visible { outline: 3px solid var(--c-focus); outline-offset: 2px; box-shadow: 0 0 0 5px var(--c-canvas); } /* two-tone ring */
```
Note: a Quiet-dark `--c-on-accent` inherits #10131F from the dark block; Quiet-light `--c-on-accent` stays #FFFFFF. Add the "no dark flash" inline script (reads `tether.prefs.v1`, falls back to `prefers-color-scheme`, sets both attributes before paint). Fonts are declared in `layout.tsx` with `variable: "--font-body"` / `"--font-head"`.

## 3. Layout and navigation architecture

App shell (non-lecture routes):
- Top bar, 56px, not sticky on phone, sticky on >= 1024: wordmark "Tether" (Newsreader 600, link to `/`), command button "Search or jump (Ctrl K)", Preferences button, visible `Sign out` button (name preserved). The user's display name is shown from >= 768 only.
- Primary destinations: **Today** (`/`), **Courses** (`/courses`). Schedule and Study appear in the nav only when their routes exist (no "coming later" teasers). >= 1024: left rail 200px (labels always, no icon-only). < 1024: destinations are items in a top-bar menu button "Menu" (no bottom tab bar; it would collide with the Quick recap dock and thumbs).
- Content column: max 72rem; reading pages (notes, consent, syllabus review) max `prose` (68ch) centered. Dashboard is a single column on phone, 2 columns (8/4) from 1024.
- Always visible: skip link, wordmark, Sign out, Preferences. Hidden by default: roadmap, secondary actions (overflow "More"), advanced preferences, raw confidence numbers, long explanations (disclosures).

Lecture routes (`/courses/[id]/lecture`, `/lectures/[id]`): the site header is **replaced** by the lecture bar so only one sticky bar exists (today two stack and cost about 100px at 320px).
- Lecture bar (sticky, 56px desktop; wraps to 2 rows <= 480px): recording pill (shape + word "Recording"/"Paused"/"Not recording"/"Stopped", elapsed, connection), Pause/Resume, Stop, Focus toggle, Preferences. Wordmark collapses to a "Leave" link back to the course (a leave while recording asks nothing: recording continues server-side per contract; the link label is "Course (recording continues)" while Active). [Question for Lead Architect: confirm leaving the page while recording keeps the session or stops capture; today capture is in-page, so this link must open the Stop dialog instead. Until confirmed, render the link only when not Active/Paused.]
- >= 1024: two columns, transcript (fluid, max 68ch text inside a wider region) | right rail 360px with, top to bottom, Quick recap dock (sticky), catch-up result, TL;DR card. < 1024: single column; TL;DR card is a collapsed one-line summary above the transcript (expand in place); Quick recap is a bottom dock (existing behavior and ids kept) with `padding-bottom: env(safe-area-inset-bottom)`.
- Breakpoints: 360, 640, 1024, 1440. 320px must work without horizontal scroll.

## 4. Component inventory (states and attention behaviors)

Common: all interactive controls >= 44px (primary 48px; Quick recap window buttons 56-64px), visible text label (icon-only buttons forbidden except close "x" with a name), focus ring from 2.7, press state = border/background change only (no scale).

| Component | Variants and states | Attention / behavior notes |
|---|---|---|
| Button | primary (filled accent), secondary (border-strong, surface), quiet text button, danger (border-danger text; never filled). States: default, hover, focus, active, busy (`aria-busy`, label unchanged plus "..." text, width fixed), aria-disabled with reason text (keeps focusable; keeps the existing pattern) | Exactly one primary per region. Destructive = secondary-style danger + confirm dialog. |
| Link | underlined always in prose; no color-only | Opens in same tab unless stated. |
| Input / textarea | label above, helper text below, error text with icon + word, border-strong 2px, focus ring. Placeholder is never the label | Save drafts locally (SC 3.3.7). |
| Checkbox / toggle (switch) | switch = track + knob + text "On"/"Off" beside it; `role=switch` | State never color-only. Used for every Preferences boolean. |
| Segmented control (radio group) | 2-4 options, text only | Used for theme, density, text size, engagement. |
| Tabs | text tabs, underline 3px + bold on selected, arrow-key roving, `role=tablist` | Max 4 tabs. Used on review screens (Notes / Transcript / Study guide when it exists). Panels preserve scroll. |
| Card | surface, hair border, radius 12, no shadow in quiet | Whole card is not a link unless it has exactly one action (then the title is the link and the card is padded, pointer area extended via `::after`). |
| Disclosure | `<details>`-style button with chevron + text; one open at a time only in help lists | Default for "Why this?", "Details", "Show more". |
| Panel (inline, non-modal) | neutral / notice (warning stripe) / recording-safety (recording stripe), heading + 1-2 sentences + <= 2 buttons | Pushes content, never overlays. Not dismissible when it states a safety fact (Paused, Not connected). |
| Toast | **not used** in lecture; elsewhere only as a persistent inline status line that stays until the next action (no auto-dismiss timers, SC 2.2.1 friendly). Announced via status region | Quiet look: same, no slide-in. |
| Dialog | modal, focus trap, Escape cancels, initial focus on the safe action, scrim; only for Stop and destructive confirms, consent is a page not a dialog | Max one dialog open; never stacked. |
| Empty state | heading (serif), one sentence, one primary action. No illustrations | Calm. Says what will appear and how it gets there. |
| Loading | static text "Getting ..." in place plus fixed-height placeholder; no spinner >200ms loops; if used, spinner is `aria-hidden` and stops under reduced motion | Layout does not shift when content arrives. |
| Error | panel with what happened, whether anything was lost, one action ("Try again"); `role=alert` only for new failures | Keeps form values. |
| Recording indicator | pill: shape + word + elapsed. Recording = filled circle, `--c-recording` text on recording-subtle; Paused = two bars, warning colors; Not recording = outlined slashed circle, danger-neutral ink; Stopped = square, muted. 20px glyph, never animated. Same pill in the page title prefix | Always in lecture bar, including focus mode, 320px and zoom (invariant 2). |
| TL;DR card | serif "TL;DR" h2, 1-line summary (lg), up to 3 bullets, "Show more", footer "Updated 12:03 (revision 7)" and the Demo summary tag when mock; Pause updates toggle; Newer-summary notice is text | Updates never move focus or scroll; in-place text swap, no highlight flash. Collapsed one-liner on phone. |
| Transcript line | `[mm:ss]` muted tabular timestamp (hang indent) + text; final = ink, partial = muted italic in the one reserved row; gap and pause dividers = hair rule + text; "edited" text tag | Reading comfort: 68ch, 1.55 line-height, no zebra. Existing `data-segment-id`, tabindex -1 preserved. |
| Catch-up panel | heading "Here's where you are" (focus target), coverage line, Right now sentence, <= 3 bullets, citation buttons ("12:03" with name "From 12 minutes 3 seconds, go to transcript line"), "Show more detail", "Show the raw lines", "Got it, back to lecture" | Side panel (>= 1024) / bottom sheet (phone), non-modal, recording continues. One result at a time, replaced in place. |
| Consent screen | single column, DRAFT tag always, bullets, one checkbox, Start recording (aria-disabled until checked) / Not now | Page, not dialog. No mic until Start. Draft wording unchanged by this refactor. |
| Command palette | dialog-style listbox, see section 6 | Navigation only; cannot start recording. |

Attention behaviors (decisions):
- **One thing at a time**: syllabus review defaults to the week-grouped list with "Review one at a time" (already designed); dashboard shows one Next step card, then courses.
- **Progressive disclosure**: tags, confidence numbers, citations count, revision numbers sit behind Details or a muted footer.
- **Calm defaults**: no sound, no badges, no auto-opening panels, no animation.
- **Focus mode** (lecture bar toggle "Focus", `aria-pressed`, remembered): hides the right rail's TL;DR card (collapses to a one-line "TL;DR: ..." chip row that expands in place), hides the left rail/site chrome, centers the transcript at 68ch, keeps the lecture bar and Quick recap dock. It does **not** lower contrast or opacity of any text ("dimming" is done by removing chrome and borders, not by washing out content, so AA holds). No auto-activation, no detection.
- **Quiet look** and **Help level** (Quiet/Balanced/Active, only Quiet exists in M2) are separate settings; Balanced/Active are not built.

## 5. Screen sketches

### 5.1 Sign-in (`/signin`, centered card, no app nav)
```
+----------------------------------------------+
|  Tether                       (serif 600)     |
|  Sign in                      (h1, serif)     |
|  This is a development sign-in. Enter any     |
|  school-style email. Google sign-in arrives   |
|  later.                                       |
|  Email  [______________________________]      |
|  (error text with icon below the field)       |
|  [ Sign in ]            (only primary)        |
+----------------------------------------------+
```
Preserve: h1 "Sign in", label "Email", button "Sign in" (`disabled` while busy as today), error `role=alert`.

### 5.2 Dashboard (Today, `/`)
```
Top bar:  Tether    [Search or jump  Ctrl K] [Preferences] [Sign out]
Hi, Alice.                                         (h1, serif 3xl)
+-- Next step ---------------------------------------------+
| Biology 101                                               |
| Open this course to start a lecture. Nothing is recorded  |
| until you say so.                                         |
| [ Open course ]            > Why this?                    |
+-----------------------------------------------------------+
Your courses                                     [Add a course]
 Biology 101        BIO 201 · Fall 2026          >
 History of Law     HIST 340 · Fall 2026         >
 [See all courses]     (only if > MAX_DASHBOARD_COURSES)
```
Empty: "Add your first course to get started." + primary link "Add your first course". Loading/error as in 4. Next-step card is the only filled accent button. Preserve: h1 `Hi, {name}.`, headings "Your courses", "Next step", link "Add your first course", "Add a course", "See all courses", `Why this?`.

### 5.3 Course page (`/courses/[id]`)
```
< Courses
BIO 201 Biology 101               (h1, focused on arrival)
Fall 2026 · Dr. Rivera
(status line if just added: "Biology 101 added.")
+-- Lecture -----------------------------------------------+
| Nothing is recorded until you choose to start.            |
| [ Start a lecture ]                                       |
+-----------------------------------------------------------+
Recent lectures   (list: date, length, "Review notes")      <- when API exists
Syllabus          [ Add a syllabus ]  (Phase 2 placeholder, shown only when route exists)
```
Preserve: h1 = course name with focus on load, "Start a lecture" link to `/courses/{id}/lecture`, "Back to dashboard" (becomes the "< Courses"/back link; keep one link named "Back to dashboard" until e2e is updated).

### 5.4 Live lecture (most important, >= 1024)
```
[ (o) Recording 12:42  Connected ] [Pause] [Stop]   [Focus] [Prefs]   Demo source: replayed test audio
---------------------------------------------------------------------------------------
Transcript                                              | +- Quick recap ---------------+
 (log, scrolls; newest at bottom; follow-live)          | | How far back? Lecture keeps    |
 12:03  The mitochondria produce ATP through...         | | going.                          |
 12:15  ...which is why the inner membrane...           | | [Last 2 min][Last 5 min (your  |
 --- Paused 12:20 to 12:24 ---                          | |  usual)   ][Last 10 min]        |
 12:25  Now consider the electron transport chain       | +-------------------------------+
 (partial, muted italic, one reserved row)              | +- Here's where you are --------+
                         [ Jump to live ] (when up)     | | (catch-up result, if opened)  |
                                                        | +-------------------------------+
                                                        | +- TL;DR ----------------------+
                                                        | | Cells make ATP in mitochondria|
                                                        | | - ...  - ...   [Show more]   |
                                                        | | Updated 12:03 (revision 7)    |
                                                        | +-------------------------------+
```
Phone (375): lecture bar (2 rows: pill+elapsed / Pause Stop Focus), then collapsed TL;DR one-liner, transcript (full width, height = viewport minus bars), fixed bottom dock "Quick recap" with three window buttons (usual one 64px, others 56px), TL;DR expands as an in-flow card, result opens as a bottom sheet over the dock (max 70vh, scrollable, non-modal).
Focus mode: right rail and TL;DR removed from view (TL;DR becomes one chip row above the transcript); dock and bar remain.
Paused / mismatch / offline / mic-blocked / stopped panels keep the slot directly under the lecture bar (existing behavior), restyled with the Panel component (recording/warning stripe + shape + word). Mic blocked: calm full-screen with h1 "Microphone is blocked", "Nothing has been recorded.", [Try again] primary, "Back to course". Stop dialog keeps initial focus on "Keep recording"/"Keep paused".
Preserve: all ids and names in section 8.

### 5.5 Quick recap result
Opening the result moves focus to its h2 (existing). Layout: coverage line (muted), then "Right now:" sentence (lg), then up to 3 bullets with citation time buttons, then a disclosure row [Show more detail] [Show the raw lines], then primary [Got it, back to lecture]. Quick-view (extractive) state shows "Quick view" label, last 5 lines, "Still writing your recap." as static text. Demo summary tag stays visible and not dismissible when `source === "mock"`. No counts, no "you used this N times".

### 5.6 Session review / notes (`/lectures/[sessionId]`)
```
< BIO 201     Lecture, Oct 27, 10:02 to 10:51        [Export to Google Docs]  (later; secondary)
(tabs)  Notes | Transcript | TL;DR history
Notes (generated, provenance shown)
  ## Cellular respiration                         From 12:03 [go]
  - ...
  Search this lecture [_______]      Text size  Density
```
Default tab Notes (what the student wants after class); Transcript tab keeps `role=log` semantics off (it is static here: a normal list, `aria-live` none). Export is deliberate, with its own confirm step (invariant 4-style approval for external writes). Stopped lecture banner "Lecture stopped. Your transcript is saved." remains. Not-found keeps h1 "We can't find that lecture".

### 5.7 Placeholders (Phase 2; layout only, copy per the existing docs)
Syllabus review (`phase2-syllabus-review.md` section 5): h1 "Review your syllabus items", Demo tag, one-line counts, group disclosures (week/type), item rows with Approve / Skip / Edit and unique names, sticky summary bar with one primary "Add N approved items to the demo calendar". Uses Card, Disclosure, Segmented control, Panel; statuses are text + icon; the sticky bar is the only filled accent button.
Schedule / calendar choice (`calendar-reminders.md` section 2): one screen, Segmented control "Reminders: Off / Gentle / Extra runway" (Gentle preselected and visible per that decision, pending PO confirmation), a plain preview list "To keep Tue, Oct 14 quiet, we skipped 2 reminders.", note "Reminders come from Google Calendar, not from Tether.", primary "Add to Google Calendar" (never before explicit student approval).

## 6. Feature-rich without clutter

| Feature | Decision | Accessibility requirements |
|---|---|---|
| Command palette | Ctrl/Cmd+K, plus a visible "Search or jump" button (so touch and screen-reader users reach it). Items: go to course, go to Courses/Today, open Preferences, toggle Focus/Quiet look/theme, "Open lecture screen for <course>" (opens the consent screen only). It cannot start, pause, resume or stop recording. | `role=dialog` + combobox/listbox pattern, focus trap, Escape closes and restores focus, results announced via the status region as "N results" (debounced 500 ms), no auto-open, every command also exists in the UI. |
| Search | Courses (client-side) now; per-lecture transcript and notes search on the review screen. | Labeled input, results count in text, no results message, no search-as-you-type scroll jumps. |
| Keyboard shortcuts | Keep existing Alt+P / Alt+J / Alt+L, **OFF by default** (existing setting), remappable list in Preferences; Ctrl/Cmd+K is the only default-on shortcut and has a visible button equivalent. No single-key shortcuts (SC 2.1.4). | Listed in a "Keyboard help" disclosure; conflicts checked with NVDA/JAWS/VoiceOver by device-accessibility-validation (NOT_RUN). |
| Density | Comfortable (default) / Compact via `data-density`; changes spacing tokens only; primary controls and Quick recap never shrink. | Compact still >= 32px controls with >= 8px gaps (SC 2.5.8 spacing exception avoided by size). |
| Preferences panel | One sheet (non-modal side panel >= 1024, full-screen page `/preferences` on phone so it is linkable): Look (Theme System/Light/Dark, Low-stimulation look, Text size S/M/L, Reading spacing, Dyslexia-friendly reading, Density, Reduce motion), Lecture (Announce new transcript lines, Announce TL;DR updates, Focus mode default, Alt+ shortcuts, usual Quick recap window), Account (Sign out), About (roadmap, version, "Demo source" explanation). Changes apply instantly and are saved locally (`tether.prefs.v1`; existing `tether.lecture.settings.v1` and `quickRecapWindow.v1` keys keep working and are migrated, never deleted). | Each setting is a labeled switch or segmented control; changes announced once ("Dark theme on"); no setting is only reachable by keyboard shortcut or hover; preview text sample for Text size/Reading font. |
| Engagement level | Visible read-only "Help: Quiet" stays in the Lecture section of Preferences (not in the bar) until Balanced/Active exist. | n/a |

## 7. Accessibility checklist for this design (WCAG 2.2 AA target; not yet audited by humans or on devices)

- Focus (2.4.7, 2.4.11, 2.4.13): two-tone 3px ring plus 2px offset, 3:1+ in all four palettes; sticky lecture bar (56px) and dock (up to 9rem) covered by `scroll-padding-top: 6rem` / `scroll-padding-bottom: 9rem` (reduces today's 10rem); programmatic-focus headings keep no ring (existing rule) but never on interactive elements.
- Target size (2.5.8, 2.5.5 as a goal): every control >= 44x44 except inline text links and tags (>= 24px with spacing); primary 48; Quick recap 56-64; dock buttons separated by >= 8px.
- Keyboard (2.1.1, 2.1.2, 2.4.3): DOM order = visual order; skip links on lecture: transcript, Quick recap, TL;DR (existing), plus site-level "Skip to main content"; no keyboard trap except Stop dialog, command palette and consent-free dialogs.
- Motion (2.3.3 as a goal, 2.2.2): none required; no auto-updating carousels; the only moving thing in a lecture is transcript text, scrolled instantly.
- Color not sole signal (1.4.1): every status = shape + word (recording pill, Approved/Skipped, confidence, Demo tag, Gap marker, Not connected). Charts do not exist.
- Reflow/zoom (1.4.4, 1.4.10, 1.4.12): 320 CSS px and 400% zoom without two-direction scroll except the transcript's own vertical region; rem units; text-spacing overrides must not clip (no fixed heights on text containers except the transcript viewport).
- Contrast: section 2.3 plus forced-colors and prefers-contrast handling.
- Consistent help and identification (3.2.6, 3.2.4): Preferences, Sign out, Pause/Resume, Stop, Quick recap occupy the same place on every screen they appear.
- Redundant entry (3.3.7) / accessible authentication (3.3.8): no re-typing; sign-in has no cognitive test; paste allowed.
- Screen-reader live-region policy (extends m2 section 0, unchanged in count): exactly three live regions (status polite, alert assertive, finals inside the transcript log, default off). Transcript lines are never announced unless the student enables "Announce new transcript lines"; then at most one batch per 30 s (up to 5 lines, "N new lines"); partials, corrections, timestamps, elapsed time and TL;DR text are never auto-announced; TL;DR "updated" is announced only when enabled, at most every 60 s. Palette results and preference changes use the status region, one message per action, minimum 2 s spacing, identical text within 10 s dropped. Visual restyling must not add `aria-live` anywhere else (lint: grep for `aria-live` allow-list).
- Landmarks: `header` (bar), `nav` "Primary", `main`, `region` "Transcript lines" log, `complementary` "TL;DR" and "Quick recap"; one `h1` per page.
- Saving/resuming: preferences, usual window, draft form values, expanded/collapsed disclosure state per screen, and transcript scroll "follow live" restore on reload; no data is lost on tab switch (existing).
- Human checks that remain NOT_RUN: NVDA/JAWS/VoiceOver/TalkBack, real devices, 400% zoom with real content, forced-colors in Windows, Reduce motion on iOS.

## 8. Implementation plan (ordered, independently shippable slices)

Frozen, must remain exactly (e2e and unit tests reference them): sign-in h1 "Sign in", label "Email", button "Sign in"/"Signing in..."; `Hi, {name}.` h1; "Your dashboard"; "Your courses"; "Add your first course to get started."; links "Add your first course", "Add a course", "See all courses", "Open course"; button "Sign out"; labels `/Course name/`, "Course code", "Term", "Instructor"; buttons "Show options"/"Hide options", "Save course"/"Saving...", "Try again"; h1 = course name (focused after create, URL `?added=1`); headings "We can't find that course" / "We can't find that lecture"; "Start a lecture", "Back to dashboard"; h1 `Start lecture: {course}`; consent checkbox label starting "I checked that recording and transcribing is allowed", "Draft" tag, button "Start recording" (aria-disabled, not `disabled`); `getByTestId` values `state-label` (Recording/Paused/Stopped), `connection` (Connected/Not connected), `demo-tag`, `demo-notice` (text contains "not from your microphone"), `elapsed`, `catchup-result` (+`data-kind`), `catchup-extract`, `catchup-notice`, `catchup-earlier`, `catchup-empty`, `demo-summary-tag` (contains "Demo summary"), `lt-working`, `lt-error` ("Try again in about a minute", "This lecture has ended"), `action-error`, `fatal-panel`, `audio-skipped`, `summary-status`, `tldr-paused`, `resume-mic-issue`, `consent-demo-bullet`; role `log` named "Transcript lines" focusable (`tabIndex 0`); ids `#lecture-transcript`, `#lecture-tldr`, `#lecture-quick-recap`, `#lt-usual`; `[data-segment-id]` (tabindex -1); header `aria-label` "Recording status" with `h2` state heading; buttons "Pause", "Resume", "Stop" (exact), dialog role with "Stop", "Keep recording"/"Keep paused"; headings "Recording is paused", "Lecture stopped", "Microphone is blocked", text "Nothing has been recorded.", "Not connected. Audio is not being saved.", button "Resume recording"; three window buttons named `Catch up on the last {2|5|10} minutes` (usual: `..., your usual choice`), citation buttons ending "go to transcript line", heading "Here's where you are", button "Got it, back to lecture"; links "Skip to transcript", "Skip to Quick recap", "Skip to TL;DR", "Skip to main content"; `document.title` prefixes "Recording | ", "Paused | ", "Not recording | ". Do not rename; presentation (classes, wrappers, icons) may change freely. Copy changes in section 1 apply only to non-frozen strings and update unit tests in the same PR.

| Slice | Content | Acceptance criteria | Test notes |
|---|---|---|---|
| 0 Tokens | `tailwind.config.ts`, CSS variables, `next/font` (Newsreader, Atkinson, optional local OpenDyslexic), pre-hydration theme script, `lib/contrast.ts` + unit test that recomputes every pair in 2.3 | App renders unchanged structure; all pairs >= required ratio; no `bg-slate-*`/`text-slate-*` left after slice 1; no FOUC in dark | Vitest contrast test (source of truth); Playwright screenshots per palette |
| 1 Primitives | Restyle `.btn*`, `.field`, `.card`; add Switch, Segmented, Disclosure, Panel, Tag, Dialog wrapper in `components/ui/` | All 4 palettes, all states; focus ring visible on each; targets >= 44 | Component unit tests; axe incl. color-contrast on a `/dev/ui-kit` page (dev only); visual snapshots |
| 2 Shell | `SiteHeader` to top bar + rail/menu, Preferences sheet + store (migrates existing keys), theme/quiet/density/text size | `Sign out` still visible; courses e2e passes unchanged; reload restores prefs; 320px no horizontal scroll | Existing `courses.spec.ts` (incl. axe), new prefs unit tests, keyboard tab-order test |
| 3 Auth + dashboard + courses | Restyle sign-in, dashboard, course pages, empty/loading/error states | Section 5.1-5.3 layouts; one primary per screen | Existing e2e + axe; snapshots 375/1024 |
| 4 Lecture shell | Lecture bar replaces site header on lecture routes; recording pill; Panel restyle for Paused/Offline/Mismatch/Blocked/Stopped; consent page restyle | Recording state visible at 320px/400%; only one sticky bar; no live-region count change | Existing `lecture.spec.ts`, `LectureScreen.test.tsx`, harness fixtures with axe on every state; live-region spy |
| 5 Transcript, TL;DR, Quick recap, Focus mode | Section 4/5.4/5.5 layouts, Focus toggle (persisted), dock restyle, right rail sticky | Partial-to-final replacement moves nothing above (existing AC); Focus keeps bar+dock, never reduces text contrast; dock never covers focus (scroll-padding) | Existing e2e (phone test), new Focus test (axe + tab order), zoom 400% snapshot, reduced-motion computed-style test |
| 6 Copy pass | Apply section 1 strings to non-frozen copy; extend copy lint (forbidden words list in section 1) | No frozen string changed; lint green | `copy.test.ts`, e2e unchanged |
| 7 Palette + search | Ctrl K palette (navigation only), course search | Cannot start recording from palette; Escape restores focus; status region result count | Unit + Playwright keyboard test; axe on open palette |
| 8 Review/notes shell | Tabs, search in lecture, export placeholder (disabled with reason until Google flow exists) | Provenance visible; export needs explicit confirm | Axe, keyboard, snapshot |
| 9 Phase 2 shells | Syllabus review and schedule layouts when their routes exist | Per phase2/calendar docs | Per those docs |

Rules for every slice: keep all existing unit and e2e tests green without edits to selectors; add visual snapshots at 375, 768, 1280 for light, dark and quiet-light; run axe with tags wcag2a, wcag2aa, wcag21aa, wcag22aa, best-practice; verify with real keyboard order; report commands actually run. Do not ship slice 5 if the phone e2e (Quick recap in viewport) fails.

## 9. Risks, evidence status and questions

Risks: (1) Hand-computed contrast may contain an arithmetic error; Slice 0's test is the check. (2) Serif headings plus a hyperlegible sans is a taste decision for the PO's brief; legibility benefit of Newsreader at small size is untested, so it is limited to headings. (3) Focus mode and Quiet look might hide information a student wants; both are optional and reversible. (4) Removing the stacked header changes the leave-page path during recording (open question, section 3). (5) OpenDyslexic license and file source must be confirmed by the engineer before bundling. (6) "Atkinson Hyperlegible Next" availability in the pinned `next/font` catalog is unverified. (7) A default-on Gentle reminders setting still awaits PO confirmation. (8) Dark mode glare vs light-mode readability varies per student; the default is System, not an assumption.

Evidence status: Judgement [J]: paper/ink concept, one-primary rule, Focus mode design, Quiet palettes, Comfortable density default, no toasts, serif headings. Weak or recalled [E]: dyslexia fonts show no consistent benefit; default effects from calendar-reminders.md. Project constraints [C]: recording visibility, three live regions, quiet engagement, no surveillance, frozen e2e names. Nothing is human-validated; G4 NOT_RUN.

Questions for real ADHD students (via human-research-coordinator; include varied attention patterns and students who do not want any of this):
1. Which palette (Paper, Night desk, Fog, Dusk) lets you read the transcript longest without strain, and why?
2. Does Focus mode feel calmer, or does hiding the TL;DR make you worry you will miss something?
3. Is the Quick recap dock easy to find and press mid-lecture without looking away for long? Do three windows help or add a decision?
4. Is the recording pill clear but not stressful? Would you want it smaller or larger?
5. Do serif headings help you find places on the page, or do they slow you down?
6. Which of these did you change in Preferences, and was anything hard to find or undo?
7. Does the Low-stimulation look feel too bare, or does it help you start?
8. Does the Ctrl K palette get used, or does it feel like extra surface to learn?
9. Which wording above feels kind, which feels cold or patronizing?
10. When you come back after being away, what do you need on screen first?

## Summary
1. Direction: "reading room" paper/ink look, Newsreader headings, Atkinson Hyperlegible body, one ink-blue accent, no gamification.
2. Tokens: four palettes (Paper, Night desk, Fog, Dusk) with CSS-variable and Tailwind config; every pair has a hand-computed ratio (no tool run; Slice 0 adds a recompute test).
3. Layout: top bar plus left rail, no bottom tab bar; lecture routes swap the site header for a single lecture bar, Focus mode removes chrome without lowering contrast.
4. Components, screens, palette, preferences and a11y checklist specified; live-region policy unchanged (three regions).
5. Plan: 10 shippable slices, with a frozen list of e2e/test-visible names that must not change.
6. Nothing is human-validated (G4 NOT_RUN); open items: leave-while-recording behavior, font catalog and OpenDyslexic licence, PO call on default reminders.
