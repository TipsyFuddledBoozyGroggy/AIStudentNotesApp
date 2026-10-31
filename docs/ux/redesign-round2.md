# Tether UI/UX refactor, round 2: critical review, design point of view, token architecture, plan

Status: AI SPECIALIST DESIGN, 2026-10-30. Author: adhd-ux-specialist (design lead for this round). Audience: Product Owner, Lead Architect, frontend-engineer, qa-engineer.
**Nothing here is human-validated (gate G4 NOT_RUN).** No student, clinician, screen-reader, real-device or classroom evidence exists for any claim below. Items are tagged [J] design judgement or untested hypothesis, [E] published or standard knowledge recalled from memory and not re-fetched (verify before quoting externally), [C] project constraint (CLAUDE.md invariants 2, 7, 9; frozen test names; gate G1).
**Gate G1 (BLOCKED):** I change no recording-consent, Demo-source or Draft wording. Where I think presentation or wording should change I say "recommend only" and it waits for the Product Owner and counsel.
What I looked at: 44 PNGs in `src/web/test-results/redesign-screens/` (18 screens x 390/1280 x Paper/Night desk/Fog/Dusk, sampled) and 16 syllabus screenshots in the session scratchpad (`shots/01` to `16`); the source under `src/web/src/app`, `components/` (shell, dashboard, course, lecture incl. `ConsentDialog.tsx`, syllabus, ui), `lib/design/{tokens,prefs}.ts`, `tailwind.config.ts`, `globals.css`, `layout.tsx`; and the documents named in the brief. I executed no code and ran no tests; every ratio in section 2.4 is hand-computed with the WCAG 2.x formula (method in 2.4) and must be re-run by the existing `tokens.test.ts` before anything ships. Where I checked my arithmetic against the shipped test's values (ink on surface 17.21, muted on surface 7.67) the numbers agree.
Caveat on the evidence: the lecture screenshots come from the dev harness (synthetic data, "Fixture:" caption, time at 0 min). The teacher-permission attestation pop-up (PO-16, `ConsentDialog.tsx`) landed after the screenshot run, so section 1.5 reviews it from code, not pixels.

---

## 1. Critical review of what is built today

### 1.0 The one-paragraph verdict
Round 1 delivered a disciplined, honest, accessible product: one filled action per region, a truthful recording pill, calm safety states, no gamification, a real token system with a tested contrast matrix, and copy that follows the language guide. Those are rare and must be kept. What it is not yet is *distinctive*. Taken as a whole the screens read as a careful generic SaaS admin app wearing a serif hat: a left rail with two links, a bordered-button header, white rounded cards on a tinted page, uppercase tracked eyebrows, 2px bordered buttons by the dozen. The "reading room" is present in the palette and the headings and absent from the layout and the component language. The weight problem is not colour, it is boxes and borders: nearly every unit of content is a card, nearly every action is a bordered button, and the lecture screen draws a large empty white rectangle around three lines of transcript. The round-2 brief asks for bolder and still calm; the way to get both is to remove containers, not to add decoration.

### 1.1 Dashboard (`dashboard--1280--paper.png`, `dashboard--390--paper.png`, `dashboard-empty--*`)
Works: date eyebrow and serif `Hi, {name}.` set the voice at once; one filled button; the course list is a single divided list; "How a lecture works" is a disclosure and closes after the first lecture; the empty state is one sentence and one action.
Generic or heavy:
- Four container styles on one screen: the accent-striped Next step card, the bordered course list, the bordered "How a lecture works" box, and the rail's white current-item pill. Everything is boxed, so nothing stands out.
- The left rail spends 240px (200 rail + 40 gap) on two links. A rail is a pattern for six or more destinations [J]. It makes the app look like an admin console and pushes the content column to start at x=320.
- Header: a bordered "Search or jump Ctrl K", a quiet "Preferences", the display name, a bordered "Sign out". Two bordered controls compete with the page's one primary. The display name is a debugging artefact at header scale ("Screens-mvvquxfm").
- Course rows repeat "No lectures yet" three times (review N7, still present) and the row's "Open" button repeats the whole-row affordance.
- The Next step card says "Most recently added / Calculus II / Open this course..." and the first list row is Calculus II again, one line below. Two chances to do the same thing, one scroll apart.
- Phone (`dashboard--390--paper.png`): fine order, but the card's inner text sits 41px from the edge while the page text sits at 16px (themes-spec A12), and the orphaned "yet" wraps on every row.

### 1.2 Course page, All courses, Add a course (`course-detail--1280--paper.png`, `courses--1280--paper.png`, `course-new--1280--paper.png`)
Works: course page hierarchy (back link, serif h1, meta line, success line with check and words); the form's "Only the name is needed. Your answers are saved on this device as you type."; one primary per screen.
Generic or heavy:
- Course page has two equal cards (Lecture with an accent stripe, Syllabus without) and two back links ("<- Courses" and "Back to dashboard", frozen). The Lecture card's body sentence is the same promise the dashboard already made.
- All courses: a search field for three items, a boxed list, a "Back to dashboard" link under it. The h1 and "Add a course" do not share a baseline (themes-spec A6).
- Add a course: the whole form is a card inside the page although it is the only thing on the page; the card border adds nothing but a second left edge.
- Five different content widths across these screens (themes-spec A3) is the single most visible inconsistency on desktop.

### 1.3 Live lecture, idle and live (`lecture-live--1280--paper.png`, `lecture-live--1280--night.png`, `lecture-live--390--paper--viewport.png`, `lecture-focus--1280--paper.png`)
Works: the pill (shape, word, minutes, static); Pause/Stop in fixed places with icons; the usual Quick recap window as the only fill; transcript hanging timestamps at a readable measure; gap dividers as hair rules with words; TL;DR hierarchy; the phone dock with three equal targets; Night desk keeps hierarchy without glare.
Generic or heavy:
- The transcript is a bordered white box of fixed viewport height. With three lines it is 80% empty white rectangle (`lecture-live--1280--paper.png`, box from y=266 to y=824). The heading "Transcript" floats above the box while the rail's first card top aligns with the heading, not the box (A9). The box border is the strongest line on the screen and carries no meaning: the transcript is the page, it should not be framed as a widget [J].
- The Demo notice panel is the first and widest thing under the bar on every lecture screen (full width, two lines, amber stripe). It must stay visible and non-dismissible [C, G1]; its *presentation* as the largest panel on the screen is a round-1 choice, not a gate requirement. Recommend only (section 6, Q2).
- The right rail is three stacked cards of the same style (Quick recap, catch-up result when open, TL;DR). Visually the rail and the transcript box are peers; the brief says the lecture is sacred and the transcript should dominate.
- "Lecture settings" is link-style between bordered buttons; the pill is ~42px and the buttons ~48px (A8); the Demo tag sits as a fifth item in the bar.
- Focus mode (`lecture-focus--1280--paper.png`): the dock overhangs the 52rem column by ~15px each side (A11); the Demo notice stays full width while the column is 52rem, so Focus narrows the lecture but not the notice; the collapsed TL;DR card plus "More" is a third box style (px-4 py-3 vs p-5).
- Phone live (`lecture-live--390--paper--viewport.png`): two-row bar at ~112px (M5 met). The collapsed TL;DR sits above the transcript with its heading clipped by the sticky bar in the capture (the card top is hidden behind the bar at scroll 0). The transcript box plus dock leaves ~430px of reading height; acceptable, but the box border again spends pixels on framing.
- Atkinson Hyperlegible's slashed zero reads as a texture in timestamp columns and dates ("2Ø26", "ØØ:Ø1"). This is the face doing its job (character distinction) [E], but at tabular density it is noisy; whether the font exposes an unslashed-zero stylistic set is unverified (section 2.5).

### 1.4 Lecture safety states (`lecture-paused--1280--paper.png`, `lecture-disconnected--390--night--viewport.png`, `lecture-mismatch--1280--paper.png`, `lecture-stopped--1280--night.png`, `lecture-mic-denied--1280--paper.png`)
Works: the panel sits directly under the bar; stripe plus shape plus word; the only fill in Paused is "Resume recording"; "Your microphone is off. Nothing is being heard or written."; mic-blocked is a model calm screen; the Stop dialog keeps focus on the safe action; the mismatch heading no longer carries a "!".
Generic or heavy:
- Paused: two stacked striped panels (Paused, then Demo notice collapsed) before the transcript. The Paused panel's heading is serif xl, the Demo heading is bold sans: two voices for two notices.
- Disconnected on phone: the bar grows to three rows (~145px): the pill "Not recording 0 min" is wider than the live pill, so "Not connected" wraps to its own row, then Pause (disabled, dashed) / Stop / Lecture settings. The dashed disabled style on the Pause button is loud for a control that simply cannot be used now.
- Mismatch: the body sentence repeats the heading word for word ("The app and your microphone disagree" twice). The bar shows a dashed "Resume", "Not available at the moment" as loose text, Stop, Focus, Lecture settings, and the Demo tag wraps to a second bar row even at 1280.
- Stopped (`lecture-stopped--1280--night.png`): "Export to Google Docs" is a dashed disabled button floating mid-column with a three-line explanation; the search field is 436px against a 736px transcript (A10); the TL;DR card is now the only rail content so the rail looks abandoned; the Stopped panel is a large box for one sentence and one button.
- Mic-blocked: the serif h1 next to a 24px icon is slightly top-heavy; otherwise keep.

### 1.5 Consent page and the teacher-permission pop-up (`consent--1280--paper.png`, `consent--390--fog.png`; `ConsentStart.tsx`, `ConsentDialog.tsx`, `CONSENT_DIALOG_COPY`)
Works: page, not dialog; single column; Draft tag at the top; the primary is the only fill; "Not now" is a quiet exit; the pop-up renders only while open so the box always starts unchecked; `inert` on the rest of the page; focus to the checkbox; Escape and Cancel start nothing.
Observations (G1: recommend only, wording untouched):
- There are now two consent surfaces in sequence: the page ("Start recording") and then the modal ("Did your teacher say you can record?" with its own checkbox and "Confirm and start recording"). The page's primary button is labelled "Start recording" but it opens a dialog (it correctly carries `aria-haspopup="dialog"`). A sighted student reads a promise the button does not keep. Recommend: the Product Owner and counsel decide whether the page button becomes "Continue" / "Next: confirm permission" or whether the page and the pop-up merge into one surface. Not a design decision I can take.
- In the pop-up the consequential action is filled and "Cancel" is a quiet text button, while the body says "Not sure? Choose Cancel." The recommended path for an unsure student is the least visible control. Recommend: Cancel as a bordered secondary (presentation only). Compare the Stop and Delete dialogs, where the *safe* action is filled: three dialogs, two conventions.
- The page shows "Draft" five times (tag plus four "Draft:" prefixes) and the demo bullet last (review S6, still open). Presentation question for counsel.
- The rail and "Search or jump" stay visible on a decision page (review N4).

### 1.6 Quick recap (`lecture-quick-recap--1280--paper.png`, `lecture-quick-recap--1280--fog.png`, `lecture-quick-recap--390--night--viewport.png`)
Works: the three windows in stable order; the usual one filled; the result heading takes focus; "Close recap" top-right and "Back to lecture" at the bottom; coverage line; Demo summary stamp; hanging citation timestamps; "Most recent" lead; Also mentioned as a plain list.
Generic or heavy:
- Desktop: the result renders inside the 360px rail card. "Here is what was said" wraps to two lines next to "Close recap"; "Most recent: The chain rule, right after the power rule." wraps to two lines at 20px; the card grows to ~830px tall and the TL;DR drops below the fold (`lecture-quick-recap--1280--paper.png`, TL;DR starts at y=1073). The student asked for a short recap and gets a tall narrow column to the side of the thing they were reading. The reading surface is in the wrong place [J].
- The window buttons are stacked full-width in the rail (3 x 56-64px = ~190px of button) and repeated as a horizontal row in Focus mode. Two layouts for one control.
- The filled usual button shows an underline on hover/focus (review N1, still present).
- Phone sheet: fine at 60dvh; the heading plus "Close recap" plus the coverage line plus the Demo stamp occupy ~150px before the first content line.

### 1.7 Syllabus flow (`shots/01-create-paste-1280.png`, `03-review-1280.png`, `03-review-390.png`, `04-edit-open-1280.png`, `06-one-at-a-time-1280.png`, `07-sync-confirm-1280.png`, `11-delete-dialog-1280.png`)
Works: honest Demo lines; "We found 6 items. 0 approved, 0 skipped, 6 not decided." as plain text; week grouping with the first open group; "Review one at a time" is the calmest screen in the app (one card, Item 1 of 6, Approve / Skip / Edit, Back / Next, decide later); the Calendar step is the only fill and it is disabled with a reason until something is approved; the delete dialog fills the safe action; the edit form keeps the row in place.
Generic or heavy (this is the heaviest screen in the product):
- The list view at 1280 is ~2,000px tall for six items; at 390 it is ~3,800px (`03-review-390.png`). Each item card shows title, kind tag, date, "Date has passed", recurrence line, confidence word with icon, decision word with icon, a hint sentence when "Please check", "See where this came from", and three buttons. Six items produce 18 bordered buttons plus two "Approve the clear ones" buttons plus toolbar buttons: about 25 bordered controls on one screen. The student's job is binary per item; the screen presents it as a form.
- Above the first item sit: h1, two Demo stamps with sentences, the counts line, "1 needs a date. Show them", timezone, two buttons, "Group by" segmented, "Collapse all", "More ways to decide". Nine things before the first decision.
- The serif group heading "Week of Aug 24 to 30, 1 item, 1 not decided" is good; everything under it is a stack of same-weight white cards with 24px padding, so the week rhythm is lost.
- "Approve" is bordered secondary on every row, so no row has a visual primary; "Edit" is a quiet accent link beside two bordered buttons, which reads as a third button style.
- The Add syllabus page (`01-create-paste-1280.png`) is good: one column, a segmented control, a large textarea, options behind a disclosure. The Draft notice checkbox is a striped panel inside the form: honest, slightly heavy, G1.
- Sync confirm (`07-sync-confirm-1280.png`) is calm and correct: list, "Not added: ..." line, one fill. Keep.

### 1.8 Preferences and palette (`preferences--1280--paper.png`, `palette--1280--paper.png`)
Works: labelled segmented controls, switch with On/Off words, "Low-stimulation look" hint sentence, the sample sentence, the in-panel status line, About disclosures.
Generic or heavy: the panel is a 750px card pushed into the page flow above the dashboard, so the dashboard the student is restyling scrolls ~1,000px down (review S7, ruled: change to a side panel). Theme / Text size / Reading spacing / Density are four segmented controls of three different widths stacked left; the eye walks a ragged edge. The palette is clean; its scrim at .55 is slightly faint in Night desk (N8).

### 1.9 Cross-cutting
- **Boxes.** Card, panel, list box, transcript box, dialog, rail pill, segmented control, kbd chip, tag: nine bordered shapes. The design-direction promised "hairline rules instead of boxes"; the build has hairline *boxes*.
- **Accent spread.** The ink-blue is used for the primary fill (correct), links, quiet buttons, the rail marker, citation times, step numerals, the Focus toggle's pressed tint, "Lecture settings", "Why this?" and chevrons. One accent per region is honoured for fills but not for text, so screens read as blue-flecked rather than ink-and-paper.
- **Eyebrows.** Uppercase tracked labels ("NEXT STEP", "LOOK", "BEFORE YOU START", "HOW TO TURN IT ON") are a generic dashboard pattern and are slower to read than mixed case for many readers [E, commonly reported, weak evidence]. They also collide with the serif voice.
- **Buttons are the only hierarchy tool.** Primary filled, secondary 2px border, quiet accent text, danger border: four styles, but the secondary is used for almost everything, so weight is uniform.
- **Alignment.** The 17 geometry findings in themes-spec section 5.1 (five content widths, inset drift, uneven header gaps, 42 vs 48px bar controls, column tops) are all still visible. None of T1 to T6 has started.
- **What is genuinely strong and must survive this round:** the token discipline (no opacity modifiers, cascade test, 1,464 passing contrast assertions), the recording pill, the three-live-region rule, the Quick recap stable order and usual-window fill, the language, the empty states, the one-at-a-time syllabus review, the safe-action-filled Stop and Delete dialogs.

---

## 2. Design point of view: the reading room, second edition

### 2.0 The idea in one line
**Paper is the surface; ink is the voice; the serif speaks for the lecture, the sans speaks for the student.** Remove the boxes, let content sit on the page separated by rules and space, reserve the one white "sheet" for the thing being read right now, and give every screen one clear primary and a quiet margin.

Why this is calmer, not just prettier (ADHD cognitive-load reasoning, all [J] unless marked):
1. **Fewer edges, fewer decisions.** Every border is a boundary the eye has to classify ("is this a button, a card, a field?"). Removing framing around non-interactive content reduces the number of visual objects per screen by roughly half on the dashboard and syllabus review (counted from the screenshots: 14 to 7 bordered shapes above the fold on the dashboard). Visual clutter is a documented load factor in general HCI [E, broad], and the student-experience rules already ask for "one primary action, visually dominant".
2. **One sheet.** A single white reading surface on tinted paper gives an unambiguous answer to "where do I look": the transcript during a lecture, the recap when it is open, the consent text before starting, the item being edited in the syllabus. Attention has one anchor.
3. **Typography does the signposting that colour did.** With accent reserved for the one primary and for focus, the serif/sans split carries meaning: serif = what the lecture or the app concluded (headings, TL;DR line, "Most recent" sentence, summary bullets' lead), sans = verbatim words, controls, data. A student learns in one session that serif text is synthesis and can be skimmed or trusted accordingly. This also makes the Demo summary and AI-estimate stamps easier to place: they always sit beside serif text.
4. **A visible margin.** Reading templates get a left margin column where labels and timestamps hang (the transcript already does this). Hanging labels keep the text column's left edge clean, which helps re-entry after an interruption: the eye finds the same edge every time (Principle 5, return without penalty).
5. **Nothing moves; the student moves things.** Motion stays at zero for anything the student did not do.

### 2.1 Typography
Faces are unchanged (both already shipped and OFL): **Newsreader** (variable, optical size) and **Atkinson Hyperlegible Next**. What changes is the role split and the scale.
- Serif, Newsreader, weight 500 for display and 600 for section heads, `font-optical-sizing: auto`, tracking -0.01em at 30px and above, 0 below. Used for: h1 display, h2 section heads, the TL;DR one-line summary, the Quick recap "Most recent" sentence, group heads in the syllabus, the wordmark, dialog headings. Never for controls, data, timestamps, transcript or body prose.
- Sans, Atkinson Hyperlegible Next, 400 and 700. Used for everything else. Tabular figures for any column of numbers. Transcript 17/28.
- Labels replace eyebrows: sentence case, sans 15/24, muted, no tracking, no uppercase. ("Next step", "Before you start", "How to turn it on".) [J] mixed case is read faster than all caps for most readers; it also stops the labels fighting the serif.
- Scale (root 16px, body 17px; every line box a multiple of 4px so blocks sit on the spacing grid, themes-spec R1):

| Token | Size / line | Use |
|---|---|---|
| display | 2.75rem / 3rem (44/48); phone 2.125rem / 2.5rem (34/40) | h1 on Today, course page, consent, syllabus review |
| 2xl | 1.875rem / 2.25rem (30/36) | h2 section heads ("Your courses") |
| xl | 1.5rem / 2rem (24/32) | h2 inside a sheet (TL;DR, Quick recap), serif summary line, dialog heading |
| lg | 1.25rem / 1.75rem (20/28) | lead sentences, item titles |
| base | 1.0625rem / 1.75rem (17/28) | body, transcript, controls |
| sm | 0.9375rem / 1.5rem (15/24) | labels, meta, helper text |
| xs | 0.8125rem / 1.25rem (13/20) | stamps, footers; never below |

Max measure 68ch for prose and transcript; 44ch for lead sentences in a sheet. Text size S/M/L and Reading spacing stay as built.
Slashed zero: keep in the transcript timestamp column and anywhere digits are data (distinction is the point) [J]; for headings and dates, check whether Atkinson Hyperlegible Next exposes an unslashed zero via a stylistic set and, if it does, apply it to `.display` and meta lines only. Unverified; frontend checks the font's feature table. If not available, leave as is.

### 2.2 Colour system
Keep every shipped value (all tested) and change how they are *used*; add one role.
- **Accent is spent on three things only:** the single primary fill per region, the focus ring, and citation/time links inside reading surfaces. Links in prose become ink text with a 2px accent underline (hover: accent text). Quiet buttons become ink text, underline on hover. The current-nav marker becomes a 3px ink underline. Step numerals and chevrons become muted. This takes blue out of roughly thirty places per screen and leaves it where the eye should go.
- **New role `ledger`**: a surface tint between canvas and surface for list rows and the lecture rail, so lists can sit on the page without a box: Paper `#FAF8F3`, Night desk `#181B21`; Fog, Dusk and the four other skins set `ledger = surface` (no new pairs there).
- **Reading surface (`surface`)** is reserved for sheets: transcript, open recap, consent text, forms, the item being edited, dialogs. Cards on canvas with white fills disappear from the dashboard and course pages.
- **Status colours unchanged** (recording is the only red; warning amber for Demo/Draft/Not connected; danger never for recording) [C].
- Dark modes: no shadows, raised step plus hair rule; unchanged.

### 2.3 Spacing rhythm, layout grid
- Scale: 4, 8, 12, 16, 24, 32, 48, 64, 96 (themes-spec R1). Vertical rhythm: 8 inside a unit, 24 between related units, 48 between sections, 32 page top (24 on phone). Half steps and arbitrary px are banned except the existing allowlist.
- Container 72rem; gutters 16 / 24 / 32 at <640 / 640-1023 / >=1024. Breakpoints 360, 640, 1024, 1536.
- **Header replaces the rail.** One 56px bar: wordmark, then the two destinations as text tabs ("Today", "Courses", current = bold + 3px ink underline), then on the right: "Search or jump" (quiet, kbd hint), "Preferences" (quiet), "Sign out" (quiet, name frozen). The display name moves into Preferences > Account. A rail returns only when there are four or more destinations [J]. Below 1024 the destinations stay in the bar as tabs (two words fit at 320px); the "Menu" button goes away. Keyboard order: skip link, wordmark, Today, Courses, Search, Preferences, Sign out, main. (Today "Course name" is reached in 8 Tabs; this is 7.)
- **Three templates** (left-aligned to the column start, never centred except Auth):
  - *Desk* (Today, course page, All courses): margin column 5rem from 1024 (hanging labels) | content 1fr, max 68ch for prose, lists full width.
  - *Sheet* (consent, Add a course, Add syllabus, review one at a time): one white sheet, 40rem max, on canvas; h1 above the sheet, not inside it.
  - *Lecture*: bar 56px; margin 5rem (timestamps hang here from 1280) | transcript 1fr (text max 68ch) | aside 22.5rem; gap 40; container 80rem from 1536. Focus mode: margin | transcript only, 52rem.
  - *Auth*: 28rem centred, top offset 96px, no header actions except Preferences.
- Shared edges (R3): wordmark left = content left; rightmost header action ink = container right; first sheet or ledger top in each column shares one y.
- Fixed/sticky layers (lecture bar, dock, side panel) use the same column width and gutters as the content under them (fixes A11).

### 2.4 Contrast arithmetic for the one new role
Method: relative luminance L = 0.2126 R + 0.7152 G + 0.0722 B with channel c = v/255, lin = c/12.92 if c <= 0.03928 else ((c + 0.055)/1.055)^2.4; ratio = (L_lighter + 0.05) / (L_darker + 0.05). Targets: text >= 4.5:1 (SC 1.4.3), boundaries and focus >= 3:1 (SC 1.4.11). Hand-computed; `tokens.test.ts` must recompute and wins on any disagreement.

`ledger` light `#FAF8F3`: channels 250, 248, 243 -> lin 0.9560, 0.9387, 0.8963 -> L = 0.2032 + 0.6714 + 0.0647 = **0.9393**. Foreground luminances derived from the shipped ratios on surface (L 1.0): ink 0.0110, muted 0.0869, accent 0.0570, recording 0.1096, success 0.1106, warning 0.0814, danger 0.0838, border-strong 0.2353.

| Foreground on ledger `#FAF8F3` | Ratio | Need | Pass |
|---|---|---|---|
| ink `#1C1B19` | 16.21 | 4.5 | yes |
| muted `#57534A` | 7.23 | 4.5 | yes |
| accent `#2E3C8C` (text and focus) | 9.24 | 4.5 / 3 | yes |
| recording `#B42318` | 6.20 | 4.5 | yes |
| success `#1B6B3A` | 6.16 | 4.5 | yes |
| warning `#7A4300` | 7.53 | 4.5 | yes |
| danger `#9F1D24` | 7.40 | 4.5 | yes |
| border-strong `#8A8579` (boundary) | 3.47 | 3 | yes |

`ledger` dark `#181B21`: channels 24, 27, 33 -> lin 0.00914, 0.01096, 0.01521 -> L = 0.00194 + 0.00784 + 0.00110 = **0.01088**. Foreground luminances from the shipped ratios on Night canvas (L 0.0080): ink 0.8223, muted 0.3867, accent 0.4993, recording 0.4082, success 0.5230, warning 0.5364, danger 0.4314, border-strong 0.2133.

| Foreground on ledger `#181B21` | Ratio | Need | Pass |
|---|---|---|---|
| ink `#ECEAE4` | 14.33 | 4.5 | yes |
| muted `#A3A7B3` | 7.17 | 4.5 | yes |
| accent `#A9B8FF` | 9.02 | 4.5 / 3 | yes |
| recording `#FF8A7A` | 7.53 | 4.5 | yes |
| success `#6FD39A` | 9.41 | 4.5 | yes |
| warning `#F2B84B` | 9.63 | 4.5 | yes |
| danger `#FF8F98` | 7.91 | 4.5 | yes |
| border-strong `#7A7F90` | 4.33 | 3 | yes |

No other new colour is introduced. Ink-text links with an accent underline, ink quiet buttons and the ink nav underline use already-tested pairs (ink on canvas 15.38 / surface 17.21 light; 15.04 / 13.71 dark). Existing tight margin to keep watching: border-strong vs sunken 3.01 (Paper).

### 2.5 Iconography
A 20px grid, 1.75px stroke, round caps, `aria-hidden`, always beside a word (icon-only controls stay forbidden except a named close). The set is deliberately small so icons stay recognisable: filled circle (Recording), two bars (Paused), square (Stopped), slashed circle (Not recording / mic off), small dot (Connected), chevron right/down (disclosure), check (done, Looks right), question mark in circle (Please check), info circle (Demo notices), warning triangle (safety notices), pause/play/stop glyphs on the bar buttons, microphone-slash (blocked). Twelve glyphs. State glyphs are filled; informational glyphs are outlined. No illustrations, no emoji, no decorative icons on cards.

### 2.6 Motion rules
Durations 0 / 120 / 160ms, easing `cubic-bezier(.2,0,0,1)`, property allow-list: `color`, `background-color`, `border-color`, `text-decoration-color`, `opacity` (scrim only). **No transforms anywhere** (v1 allowed translateY <= 8px; removing it removes the last moving part). Disclosures open instantly. The recap result appears instantly and focus moves to its heading. Reduce motion (OS or in-app), Low-stimulation look and Calm set every duration to 0. Nothing loops, pulses or shimmers; the recording dot is static [C].

### 2.7 Component language
| Component | Definition | Replaces |
|---|---|---|
| **Sheet** | White `surface`, 1px hair border, radius `--r-sheet` on desktop (16) and square-edged full-bleed on phone, padding 24 (32 on Calm). Exactly one per screen. Holds what is being read or filled in. | `.card` for transcript, consent, forms, recap result, dialogs |
| **Ledger** | A list on `ledger` tint, rows separated by hair rules, no outer border on canvas; row padding 16 vertical, text inset equals the column's left edge. Whole row is the link when it has one action; the trailing action is a quiet "Open" with a chevron. | boxed course list, syllabus item cards, sync lists |
| **Note** | Inline non-modal panel: 4px left stripe (warning, recording, danger, neutral), 16 padding, no outer border on canvas, heading in sans 700 lg, one or two sentences, at most two actions. Not dismissible when it states a safety fact. | `.panel` |
| **Pill** | Recording status: unchanged markup, 44px tall, 2px border in the state colour, state tint fill, shape + word + minutes. | unchanged |
| **Stamp** | Demo / Draft / Demo summary: one style, xs 700, warning text, 1px warning border, warning-subtle fill, radius 4, always followed by its sentence in sm muted on the same line where it fits. | three tag variants and the ad-hoc Demo summary box |
| **Margin label** | sm muted sans in the 5rem margin column (date, "Next step", week, timestamps). Stacks above its content below 1024. | eyebrows |
| **Button tiers** | Primary (filled accent, one per region); Secondary (2px `border-strong`, surface); Tertiary (ink text, 44px hit area, underline on hover/focus; replaces `.btn-quiet` accent text); Danger (2px danger border, never filled). Busy: label kept, width fixed, `aria-busy`. Disabled: `aria-disabled`, muted text, 2px hair border, solid (not dashed), reason text or description. | dashed disabled style |
| **Link** | Ink text, 2px accent underline, offset 0.2em; hover: accent text. Citation times: accent text, tabular, underlined, 44px hit area. | accent links |
| **Field** | Label above (sm 700), 2px `border-strong`, 44px, helper below, error with icon + words; sits on a sheet. | unchanged |
| **Segmented / Switch / Disclosure** | As built; Disclosure's chevron goes muted; Segmented options share one width per group so stacked groups align (fixes the ragged Preferences column). | |
| **Dialog** | Sheet with scrim .6 light / .7 dark, max 28rem, serif xl heading, **safe action filled** for destructive and stop confirms, deliberate action filled for consent (with Cancel as a bordered secondary); one dialog at a time. | |
| **Side panel** | Preferences from 1024: fixed right panel 24rem on `surface`, hair left border, non-modal, Escape closes, page stays visible and interactive; below 1024 in-flow as today. | in-flow panel |

---

## 3. One token architecture, five looks

### 3.1 Layers
Themes are skins on one layout. The architecture that already exists in `tokens.ts` and `globals.css` is right; this section names it as a contract, adds the `ledger` role and component tokens, and lists what may never vary.

**Layer A, palette roles** (per skin x mode, set in the palette blocks of `globals.css`, oracle `tokens.ts`): `--c-canvas --c-surface --c-raised --c-sunken --c-ledger` (new) `--c-ink --c-muted --c-accent --c-on-accent --c-accent-subtle --c-recording --c-recording-subtle --c-success --c-success-subtle --c-warning --c-warning-subtle --c-danger --c-border-strong --c-border-hair --c-focus --c-chrome --c-on-chrome --c-grid`. Every block lists every role (no leakage). Status roles are shared per mode across skins [C].

**Layer B, structure** (per skin, mode-independent unless stated; set in the skin blocks): `--font-head --head-weight --head-track --r-control --r-card --r-sheet --shadow-1 --shadow-2 --shadow-3 --scrim --page-bg --blur --chrome-glass --card-deco --dur-fast --dur-base`. Calm may set `--pad-card` (the single sanctioned layout exception, Comfortable density only).

**Layer C, component tokens** (set once in `:root` from A and B; components consume only these and Layer A roles; skins may override only the rows marked "skin may set"):

| Token | Default | Skin may set |
|---|---|---|
| `--sheet-bg` / `--sheet-border` / `--sheet-radius` | surface / border-hair / `--r-sheet` | radius only |
| `--ledger-bg` / `--ledger-rule` | ledger / border-hair | no |
| `--note-stripe-w` | 4px | no |
| `--btn-radius` / `--btn-border-w` | `--r-control` / 2px | radius only |
| `--link-ink` / `--link-line` | ink / accent | no |
| `--label-ink` | muted | no |
| `--pill-h` / `--control-h` / `--control-h-lg` / `--control-h-xl` | 44 / 44 / 56 / 64 px | no |
| `--focus-w` / `--focus-offset` | 3px / 2px | no |
| `--margin-col` / `--aside-w` / `--lecture-aside-w` | 5rem / 20rem / 22.5rem | no |
| `--stamp-*` | warning roles, radius 4 | no |

Rule R11 stands and is extended: a skin block may contain only Layer A and B variables and decorative `background-image` / `box-shadow` on `.card` and `[data-chrome]`. A lint test greps the skin blocks for any `padding|margin|gap|width|height|display|position|order|font-size|line-height` and fails.

### 3.2 The exact CSS-variable contract frontend implements
```css
/* Layer A: one block per skin x mode; every role present; tokens.test.ts is the oracle. Paper adds --c-ledger:#FAF8F3 (light) / #181B21 (dark); Fog, Dusk, portal, gamer, calm, glass set --c-ledger: var(--c-surface) literally as the hex of their surface (no var() inside palette blocks, so the cascade parser stays simple). */
:root, [data-skin="paper"] { /* existing 22 roles */ --c-ledger: #FAF8F3; }
:root[data-theme="dark"], [data-skin="paper"][data-theme="dark"] { /* existing */ --c-ledger: #181B21; }
/* Layer B: per skin, unchanged from themes-spec 2.5, plus --scrim per mode. */
:root { --scrim: rgba(20,22,27,.6); } :root[data-theme="dark"] { --scrim: rgba(0,0,0,.7); }
/* Layer C: once, in :root, never in a skin block except the rows allowed in 3.1. */
:root { --sheet-bg: var(--c-surface); --sheet-border: var(--c-border-hair); --sheet-radius: var(--r-sheet);
  --ledger-bg: var(--c-ledger); --ledger-rule: var(--c-border-hair); --note-stripe-w: 4px;
  --btn-radius: var(--r-control); --btn-border-w: 2px; --link-ink: var(--c-ink); --link-line: var(--c-accent);
  --label-ink: var(--c-muted); --control-h: 44px; --control-h-lg: 56px; --control-h-xl: 64px; --pill-h: 44px;
  --focus-w: 3px; --focus-offset: 2px; --margin-col: 5rem; --aside-w: 20rem; --lecture-aside-w: 22.5rem; }
/* Overlays, in this order (later wins): Low-stimulation, Solid/Texture switches, prefers-contrast, forced-colors, Reduce motion, lecture-route rules. Existing blocks stay. */
```
Tailwind: add `ledger: v("ledger")` to colours; `.sheet`, `.ledger`, `.note`, `.stamp`, `.label`, `.btn-tertiary` component classes consume Layer C only. `tokens.ts`: `ROLES` gains `"ledger"`, `BACKGROUND_ROLES` gains `"ledger"` (so the full text-on-background matrix covers it in all 12 palettes), `PLAIN_BACKGROUNDS` gains `"ledger"` for the 3:1 boundary checks.

### 3.3 What must never change between themes (asserted by tests)
1. **Lecture text contrast and metrics:** transcript on `surface` in `ink`, body face, 17/28, 68ch, hanging 3.25rem timestamp column, no texture, gradient or blur under it; TL;DR and recap text on opaque surfaces only.
2. **Recording indicator:** same markup, 44px, shape + word + minutes, colours only from `recording`/`recording-subtle` (Recording), `warning` (Paused), `ink` on `surface` (Not recording), `muted` (Stopped); never animated; visible at 320px, in Focus mode and under every skin. The lecture bar is opaque `surface`, 56px desktop, <= 112px phone, no chrome roles.
3. **Layout geometry:** container widths, gutters, templates, control heights, button order, dock size and order (2, 5, 10), focus ring 3px + 2px offset + canvas halo, scroll padding. The themes-spec 5.3 "set of rects of 12 landmark elements equal within 1px across all 10 looks" test is the gate (Calm card padding excepted).
4. **Semantics and behaviour:** DOM order, roles, names, ids, exactly three live regions, no new `aria-live`, no theme-driven focus moves.
5. **Status meaning:** red only for live capture; amber for Demo/Draft/Not connected; danger for destructive and failures.
6. **Safety surfaces under Low-stimulation:** never reduced in prominence.
What may vary: hue of every role, accent, radii, shadow, heading face in h1/h2, canvas decoration outside sheets (gradient, grid), card decoration (Portal strip, Gamer ticks, Glass highlight), header chrome, Quick recap dock fill style (opaque).

---

## 4. Prioritised, sliced implementation plan

Themes slices T1 to T6 (themes-progress.md) are folded in so nothing is done twice: T0 is done; **T1 = slice 1**, **T2 = slice 5**, **T3 + T4 = slice 7**, **T5 = slice 8**, **T6 = slice 9**. Every slice depends on slice 0 (tokens) and is shippable alone. Frozen names and ids (design-direction section 8 as amended by redesign-progress R2-1 and round 3) are untouched unless a slice says so, with the test edit in the same change.

Rules for every slice: existing unit (1,464), harness e2e (47) and courses e2e (11) stay green; axe with wcag2a/aa/21a/aa/22aa/best-practice on every touched screen in Paper, Night desk, Fog, Dusk (plus the skins from slice 5 on); copy lint (`copy.test.ts`) extended with any new string; report commands actually run; nothing marked done from a screenshot alone.

| Slice | Content | Files likely touched | Acceptance criteria | Tests | Risk |
|---|---|---|---|---|---|
| **0 Token contract v2** | `ledger` role in all 12 palettes; Layer C component tokens; `--scrim` per mode; display size in the type scale; line boxes on the 4px grid; `font-feature-settings` probe for unslashed zero; accent-usage and skin-block lints | `lib/design/tokens.ts`, `globals.css` (palette blocks, `:root`), `tailwind.config.ts`, `tokens.test.ts` | App looks unchanged; `ledger` passes the full matrix (section 2.4 values within 0.02); skin blocks contain no layout properties; `grep` finds no `text-accent` on non-primary, non-citation text after slice 2 (ratchet: count now, fail on increase) | `tokens.test.ts` matrix + cascade for 12 palettes; new `skinBlocks.test.ts` lint; `prefs.test.ts` unchanged | Low. Arithmetic error in 2.4: the test wins |
| **1 Layout system (= T1)** | Header tabs replace the rail; templates Desk / Sheet / Lecture / Auth; gutters; 44px control height incl. bar; optical start/end utilities; `scrollbar-gutter: stable`; fixes A1-A14; geometry helper and spec | `AppShell.tsx`, `globals.css`, `LectureScreen.tsx` (grid), `StatusBar.tsx` (heights), `SignInView.tsx`, `ConsentStart.tsx` (template only), `e2e-screens/geometry.ts`, `geometry.spec.ts` | Geometry suite passes for Paper at 320/390/768/1024/1280/1440/1920; one content left edge per template; no horizontal scroll at 320; Tab reaches "Course name" in <= 8 presses; `Sign out`, `Preferences`, skip link unchanged | `geometry.spec.ts`; spacing scan; `AppShell.test.tsx` (update rail assertions, keep names); courses e2e; axe | Medium: removing the rail changes `aria-current` placement and the keyboard e2e count; `AppShell.test.tsx` asserts a rail today |
| **2 Primitives v2** | `.sheet`, `.ledger`, `.note`, `.stamp`, `.label`, `.btn-tertiary`, link style, disabled style (solid), dialog scrim; eyebrow -> label; Segmented equal widths; ui-kit page | `globals.css` components layer, `components/ui/*`, `UiKit.tsx`, `primitives.test.tsx`, `dev/ui-kit` | Every primitive in 4 palettes x all states on the ui-kit page with 0 axe violations; targets >= 44; focus ring visible on each; no `uppercase` eyebrow left in `src/` | `primitives.test.tsx`; axe on ui-kit; visual snapshots (review aid) | Low |
| **3 Lecture screen** | Transcript as the sheet (heading inside as its header row, no fixed empty height: `min-height` 12rem, grows to the viewport bound only when content exceeds it); rail as a ledger (windows row + TL;DR), Demo notice as a Note at column width; **recap result opens as a sheet in the transcript column above the transcript** (desktop), bottom sheet on phone unchanged; bar controls 44px, "Lecture settings" as a tertiary in the same style as its neighbours; Stopped toolbar as a grid; Focus dock = column width; collapsed TL;DR as a Note | `Transcript.tsx`, `QuickRecapPanel.tsx`, `TldrPanel.tsx`, `LectureScreen.tsx`, `StatusBar.tsx`, `Notices.tsx` (classes only), `globals.css` lecture rules | Pill, bar names, ids, test ids, three live regions, skip links unchanged; with a result open the first transcript line is still visible at 1280 and at least 3 lines at 390; result heading takes focus; "Close recap" and "Back to lecture" both present; follow-live still parks the newest line above the dock; Focus never uses opacity/filter; at 320 pill + controls + first line visible in live, paused, disconnected, mismatch, stopped | `LectureScreen.test.tsx`, `QuickRecapPanel.test.tsx`, `LectureRedesign.test.tsx` (unchanged assertions), `e2e-harness/redesign.spec.ts` extended: result-open geometry at 1280 and 390, label-in-name loop; axe on 8 fixtures x 4 looks; lecture e2e by qa-engineer on a machine with a working fake mic | High value, medium risk: moving the result into the transcript column is a layout change during a lecture (student-initiated, allowed); the transcript height maths in `globals.css` must be re-derived; the harness test "Paused panel needs no scrolling at 320" must stay green |
| **4 Today, course, forms** | Desk template on Today (margin date label, Next step as a Note-less block with the one primary, courses as a ledger, "How a lecture works" as a disclosure under the list); course page: one primary, Syllabus as a ledger row; All courses: ledger; Add a course / Add syllabus: Sheet template; sign-in: drop "TETHER" eyebrow | `DashboardView.tsx`, `CourseCard.tsx`, `CourseDetailView.tsx`, `AllCoursesPage.tsx`, `CourseForm.tsx`, `NewCoursePage.tsx`, `SignInView.tsx`, `syllabus/CreateForm.tsx` | All frozen names and headings unchanged; one filled button per screen; "No lectures yet" shown once or not at all; snapshots at 375/1024 | `DashboardView.test.tsx`, `CourseForm.test.tsx`, `CourseDetailView.test.tsx`, courses e2e, axe | Low |
| **5 Preferences side panel + Theme picker (= T2)** | Right side panel from 1024 (in-flow below); legend "Theme" -> "Colour mode", new "Theme" radiogroup of 5 cards with nested `data-skin` swatches, Reset look, preview card, status messages per themes-spec 3.2; Portal and Calm reviewed by eye | `PreferencesPanel.tsx`, `AppShell.tsx`, `PrefsProvider.tsx`, `lib/design/prefs.ts` (no schema change), `globals.css` | Choosing a theme applies at once, persists, no flash on reload, announced once; page stays visible while choosing at 1280; panel at 320 has no sideways scroll; `NEXT_PUBLIC_THEMES=off` hides the control; copy for theme descriptions passes the lint | `AppShell.test.tsx` ("Dark theme on." -> "Dark mode on.", recorded), new `ThemePicker.test.tsx` (one tab stop, arrows, Escape, focus return), `prefs.test.ts` init parity, axe per skin x mode on Today + panel | Medium: `AppShell.test.tsx` and `LectureRedesign.test.tsx` pin the "Theme" legend |
| **6 Syllabus review density** | Ledger rows per item: line 1 title (lg 700) + date + "Date has passed" (muted); line 2 kind stamp + confidence (only when not "Looks right") + decision word; actions: Approve (secondary), Skip (tertiary), Edit (tertiary); "See where this came from" as a disclosure chevron; week heads as margin labels + serif; toolbar compressed to counts line + Group by + "Review one at a time" (tertiary) with "More ways to decide" holding batch and Collapse all; Calendar step as the sticky bottom Note on desktop; one-at-a-time unchanged | `syllabus/Review.tsx`, `ItemRow.tsx`, `SyllabusList.tsx`, `Batch.tsx`, `Sync.tsx` (classes only), `syllabus/copy.ts` (no wording change) | Six-item list <= 1,200px at 1280 and <= 2,400px at 390; every item still exposes Approve, Skip, Edit, source and confidence by name; "Please check" hint and reason still visible; Demo lines unchanged (G1); keyboard focus map (UX 9) unchanged | `Review.test.tsx`, `SyllabusFlow.test.tsx`, `F2.test.tsx`; syllabus e2e; axe; geometry on the row grid | Medium: many pinned names (`addDateName`, `approveName`...) stay; hiding "Looks right" by default is a disclosure choice to confirm with learning-science |
| **7 Gamer + Glass (= T3 + T4)** | Grid texture switch UI, Solid surfaces switch UI, `emulateMedia` fallback tests, backdrop-filter DOM scan, performance trace | `PreferencesPanel.tsx`, `globals.css` (small), `e2e-screens/*` | Texture off by default and never under lecture text; no element with `backdrop-filter` contains forbidden content; all fallbacks verified by computed style | Playwright `emulateMedia({reducedMotion, forcedColors, contrast})`; DOM scan; axe per mode | Low (CSS already shipped in T0) |
| **8 Lecture pass under 10 looks (= T5)** | Loop harness fixtures over skins x modes; assert section 3.3 invariants; copy lint extension for any new string | `e2e-harness/redesign.spec.ts`, `e2e-screens/screens.spec.ts`, `copy.test.ts` | All "must not vary" items asserted; landmark-rect equality across looks; live regions = 3; label-in-name passes | As listed | Low |
| **9 CI matrix (= T6)** | Unit + harness + geometry + axe for the PR subset (Paper, Portal dark, Gamer dark, Calm light, Glass light), nightly full matrix, bundle budget | `.github/workflows/*` (devops-engineer), docs | CI run records exist and are linked from `docs/RELEASE_GATES.md`; none exist today | CI itself | Depends on devops; no deployment claim |

Dependency note: slices 1 and 2 can run in parallel after 0 if they do not share files (slice 1 owns `AppShell.tsx` and the page templates; slice 2 owns `globals.css` components layer and `components/ui/*`; both touch `globals.css`, so sequence them or split the file by `@layer`). Slice 3 needs 1 and 2. Slice 5 needs 1 (side panel) and 2 (segmented widths). Slice 6 needs 2. Slices 7 to 9 need 5.

What this plan does **not** do: change any consent, Demo-source or Draft string (G1); add routes or API calls; add a `/preferences` route (needs Lead approval); build notes/review tabs (no data yet); claim any human validation.

---

## 5. Before / after wireframes, four highest-impact screens

Legend: `=` sheet (white reading surface), `.` ledger tint, `|` hair rule, `[ ]` secondary button, `[[ ]]` primary (the only fill), `_text_` tertiary / link, `>` chevron, margin column at the left on desktop.

### 5.1 Live lecture, desktop 1280, Recording

Before (`lecture-live--1280--paper.png`):
```
[(o) Recording 0 min] . Connected   [|| Pause] [[] Stop] [[] Focus]  Lecture settings  (Demo source: replayed test audio)
+-- Demo source: these words are not from your microphone ----------------------------------------+
|   The transcript is a fixed test script ... (Draft wording.)                                     |
+--------------------------------------------------------------------------------------------------+
Transcript                                                   +-- Quick recap ------------------+
+------------------------------------------------------+     | How far back? ...                |
| 00:01  So the derivative of x squared is two x.      |     | [ Last 2 min ]                   |
| 00:02  The power rule says bring the exponent down.  |     | [[ Last 5 min (your usual) ]]    |
|   --- Audio not available 00:05 to 00:08 ---         |     | [ Last 10 min ]                  |
| 00:09  Now let us look at the chain rule.            |     +----------------------------------+
|                                                      |     +-- TL;DR -------------------------+
|                 (empty white, ~550px)                |     | Derivatives and the power rule.  |
|                                                      |     | - Power rule ...  - Chain rule   |
|                                                      |     | [From 00:02]  Updated 00:09 (r1) |
+------------------------------------------------------+     | _Pause updates_                  |
                                                             +----------------------------------+
```
After:
```
[(o) Recording 0 min] . Connected      [|| Pause] [[] Stop]  _Focus_  _Preferences_   Demo source: replayed test audio
| Demo source: these words are not from your microphone   _Details_            <- Note at column width, 1 line, G1 body behind Details (recommend only)
                                                                                 (wording unchanged)
margin | ==== Transcript ============================================ | ..... Quick recap ...............
       | =                                                          = | . How far back? Lecture keeps going.
 00:01 | = So the derivative of x squared is two x.                 = | . [Last 2 min] [[Last 5 min, usual]] [Last 10 min]
 00:02 | = The power rule says bring the exponent down.             = | .................................
       | =   ---- Audio not available 00:05 to 00:08 (connection) --= | ..... TL;DR ......................
 00:09 | = Now let us look at the chain rule.                       = | . Derivatives and the power rule.   (serif xl)
       | = (partial row, muted italic)                              = | . - Power rule: bring the exponent down.
       | ===== (sheet ends where the text ends; grows with content) = | . - Chain rule comes next.
       |                                                              | . Updated 00:09, revision 1   _Pause updates_
```
Changes: one sheet (the transcript) and a quiet ledger rail; timestamps hang in the margin from 1280 (inside the sheet below that, as today); Demo notice one line with Details; windows in one row (the usual one filled and taller); TL;DR line in serif; bar controls all 44px, settings and Focus as tertiary in the same style. Frozen: all names, ids, test ids, three skip links, `role=log`, `data-segment-id`.

### 5.2 Quick recap result

Before, desktop (`lecture-quick-recap--1280--paper.png`): the result grows the 360px rail card to ~830px; "Here is what / was said" wraps; TL;DR falls below the fold.
After, desktop:
```
[(o) Recording 12 min] . Connected   [|| Pause] [[] Stop]  _Focus_ _Preferences_
margin | ==== Here is what was said =============================  _Close recap_ = | ..... Quick recap ......
       | = Covers the last 5 minutes.   Demo summary: Made by a test routine... = | . [Last 2] [[Last 5, usual]] [Last 10]
 12:03 | = Most recent: The chain rule, right after the power rule.    (serif xl) = | ........................
 11:58 | = The derivative of x squared is two x.                                  = | ..... TL;DR ............
 12:00 | = Power rule: bring the exponent down.                                   = | . Derivatives and ...
       | = Also mentioned                                                         = | .
       | = - Audio was not available from 12:05 to 12:08.                         = |
       | = _Show more detail_   _Show the raw lines_        [ Back to lecture ]  = |
       | =========================================================================== |
       | ==== Transcript (still live, 3+ lines visible, follow-live on) ============ |
 12:09 | = Now let us look at the chain rule.                                      = |
```
After, phone 390 (bottom sheet, unchanged mechanics, lighter head):
```
| Here is what was said          _Close recap_ |
| Covers the last 5 min.  Demo summary: ...   |   <- one line, stamp + sentence
| 12:03  Most recent: The chain rule, right    |
|        after the power rule.        (serif)  |
| 11:58  The derivative of x squared is two x. |
| 12:00  Power rule: bring the exponent down.  |
| _Show more detail_      [ Back to lecture ]  |
```
Why: the recap is the thing being read at that moment, so it takes the sheet at reading width; the transcript stays visible beneath (Principle 5, cheap way back). Focus still moves to the heading; "Close recap" stays top-right; the rail keeps the windows so a second press is one reach away.

### 5.3 Today (dashboard), desktop

Before (`dashboard--1280--paper.png`): rail | date eyebrow, serif Hi, striped Next step card with "Most recently added / Calculus II / sentence / [[Open course]] / > Why this?", "Your courses" + [Add a course], boxed list (3 x "Open"), right column "How a lecture works" box.
After:
```
Tether   Today   Courses                              _Search or jump  Ctrl K_   _Preferences_   _Sign out_
                 -----
margin        | Hi, Alice.                                                  (display serif 44/48)
Friday,       |
October 30    |
              |
Next step     | Calculus II                                                 (serif xl)
              | Open this course to start a lecture. Nothing is recorded until you say so.
              | [[ Open course ]]   _Why this?_
              |
Your courses  | ...................................................................... _Add a course_
              | . Calculus II        MATH 152 . Fall 2026                             Open >
              | .|-------------------------------------------------------------------------
              | . History of Law     HIST 340 . Fall 2026                             Open >
              | .|-------------------------------------------------------------------------
              | . Biology 101        BIO 101 . Fall 2026                              Open >
              | ......................................................................
              | > How a lecture works                                       (disclosure, closed after first lecture)
```
Changes: no rail, no cards; labels hang in the margin; one fill; ledger rows are whole-row links with a tertiary "Open"; "No lectures yet" dropped (shown as "Last lecture Oct 27" only when there is one); "How a lecture works" moves under the list as a closed disclosure for returning students. Frozen: `Hi, {name}.`, "Your courses", "Next step", "Open course", "Add a course", "See all courses", "Why this?", "Add your first course to get started.".

### 5.4 Syllabus review, desktop

Before (`shots/03-review-1280.png`): ~2,000px, nine controls above the first item, each item a padded card with 3 buttons and 6 to 8 lines.
After:
```
margin          | Review your syllabus items                                       (display)
                | Demo calendar: nothing here goes to Google Calendar.  Demo: items are found by a test routine, not a real AI model.   (stamps + sentences, one line each, wording unchanged)
                | We found 6 items. 0 approved, 0 skipped, 6 not decided.   1 needs a date. _Show them_
                | Group by (Week | Type)      _Review one at a time_      > More ways to decide
                |
Week of         | Office hours                      Tue, Aug 25, 2:00 to 3:00 PM   Date has passed     [Approve] _Skip_ _Edit_
Aug 24 to 30    | . Office hours . Repeats weekly . Probably right . Not decided            > See where this came from
1 not decided   |.|-------------------------------------------------------------------------------------------------------
                |
Week of         | Quiz 1 in class                   Mon, Sep 14, all day           Date has passed     [Approve] _Skip_ _Edit_
Sep 14 to 20    | . Exam . Not decided                                                       > See where this came from
2 not decided   |.|-------------------------------------------------------------------------------------------------------
                | Problem set 1 due                 Fri, Sep 18, 11:59 PM          Date has passed     [Approve] _Skip_ _Edit_
                | . Assignment . Not decided                                                 > See where this came from
                |.|-------------------------------------------------------------------------------------------------------
                |   _Approve the clear ones in this week (2)_
                |
Week of         | Lab report 2 due (tentative)      Fri, Oct 16, all day           Date has passed     [Approve] _Skip_ _Edit_
Oct 12 to 18    | . Assignment . ? Please check . Not decided
                | . One detail here is unclear. Please check. Approving means you checked the date against your syllabus.
                | . From your syllabus: "Lab report 2 due October 16, 2026 (tentative)"       (open by default for Please check)
                |
(sticky bottom) | [[ Add 0 approved items to the demo calendar ]]  Approve an item to continue.            <- Note, column width
```
Changes: ledger rows, one bordered button per row, confidence shown only when it is not "Looks right" (the icon and word remain available in the row's meta and in one-at-a-time; confirm with learning-science), week labels hang in the margin, the Calendar step is a sticky bottom Note so the only fill is always in view. Frozen: all accessible names, Demo wording (G1), "Please check" wording, focus map.

---

## 6. Open questions for the Product Owner (kept to four)

1. **Header tabs instead of a left rail.** With two destinations I want the rail gone and "Today / Courses" in the header. This contradicts design-direction v1 section 3, which I wrote. Approve the change, or keep the rail and I will slim it instead.
2. **Demo-source notice presentation on lecture screens (G1).** Wording stays word for word. May it render as a one-line Note with the body behind "Details" at all times (today it collapses only while a safety panel shows), and may the Demo tag leave the bar on desktop as it already does on phones? This needs your and counsel's yes because it changes how much of the notice is visible by default.
3. **Two consent surfaces.** The "Before you start" page's primary is labelled "Start recording" but opens the teacher-permission pop-up. Options: relabel the page button (counsel), merge page and pop-up into one surface (counsel plus design), or keep both and accept the mismatch. I also recommend Cancel in the pop-up become a bordered secondary (presentation only). Your call with counsel; I will not touch it otherwise.
4. **Serif as the voice of synthesis.** I propose that AI or app conclusions (TL;DR line, "Most recent", summary leads) are set in the serif and everything verbatim or interactive in the sans, as a product-wide convention. It is a visible identity choice with no evidence behind it beyond judgement; it is reversible by a token. Comfortable with that as the house style?

---

## 7. Risks, evidence status, and what is not claimed
- Every ratio in section 2.4 is hand arithmetic; `tokens.test.ts` is the source of truth. If it disagrees, change the `ledger` hex, not the foregrounds.
- The recap-in-the-transcript-column layout (slice 3) is the boldest interaction change; it must be checked by qa-engineer with the real-stack lecture e2e on a machine where Chromium's fake microphone works (still NOT RUN as of themes-progress.md).
- Removing the rail and eyebrows, the serif-for-synthesis rule, the ledger tint, hanging margin labels, and hiding "Looks right" by default are all [J]. The questions for real students in design-direction section 9, redesign-review, and themes-spec section 7 still stand and gain: "Does the white sheet tell you where to read?", "Did you notice serif means a summary?", "Is the recap opening above the transcript helpful or startling?".
- G1 remains BLOCKED; G4 remains NOT_RUN; nothing in this document is evidence that any screen helps any student. No code was changed by me.

## Slice 0 results

Status: IMPLEMENTED in `src/web`, 2026-10-30, by frontend-engineer. Not committed. Product Owner decisions of 2026-10-31 applied: header tabs are slice 1 (untouched here); serif = synthesis / sans = verbatim is defined at token level only; consent screens, `ConsentDialog`, `ConsentStart`, `Notices` and all consent / Demo wording are untouched (G1).

### What landed
- **Layer A**: role `ledger` in all 12 palettes (`tokens.ts`, `globals.css`). Paper `#FAF8F3`, Night desk `#181B21`; Fog, Dusk, Portal, Gamer, Calm and Glass set `ledger` to their own `surface` hex literally (no `var()` in palette blocks, asserted). `BACKGROUND_ROLES` and `PLAIN_BACKGROUNDS` gained `ledger`, so every text role is tested on it in all 12 palettes and every boundary role at 3:1. Tailwind: `ledger: v("ledger")`.
- **Layer B**: unchanged structure, plus `--scrim` per mode: `:root` `rgba(20,22,27,.6)`, `:root[data-theme="dark"]` `rgba(0,0,0,.7)` (was `.55` in both modes). This is the one visible token-only change in the slice: dialog and command-palette backdrops are slightly darker, including behind the teacher-permission pop-up. Nothing inside any dialog changed. If counsel wants the consent backdrop byte-identical, revert the light value to `.55` (one line).
- **Layer C** in `:root` with the exact defaults of section 3.2: sheet, ledger, note stripe, button radius/border, link, label, `--pill-h` / `--control-h` / `--control-h-lg` / `--control-h-xl` (44 / 44 / 56 / 64), `--focus-w` / `--focus-offset` (3 / 2), `--margin-col` / `--aside-w` / `--lecture-aside-w` (5rem / 20rem / 22.5rem), `--stamp-*`, plus the PO's two type roles `--font-synthesis: var(--font-head)` and `--font-verbatim: var(--font-body), system-ui, sans-serif` (Tailwind `font-synthesis` / `font-verbatim`). `:focus-visible` now consumes `--focus-w` / `--focus-offset` (computed values identical: 3px ring, 2px offset, 5px canvas halo). No component consumes any other Layer C token yet (slices 1 and 2 do); the oracle for the contract is `tokens.ts` (`LAYER_B_VARS`, `LAYER_C_TOKENS` with `skinMaySet`, `SCRIM`, `TYPE_SCALE`, `DISPLAY_PHONE`).
- **Display size token**: `--text-display` / `--leading-display` = 2.125rem / 2.5rem below 640px, 2.75rem / 3rem from 640px; Tailwind `text-display` consumes them. No h1 uses it yet (they keep `text-3xl`), so nothing moved.
- **Type scale on the 4px grid**: `--type-xs ... --type-2xl` with `--leading-*` (13/20, 15/24, 17/28, 20/28, 24/32, 30/36) declared and tested (every line box a multiple of 4px). **Not wired into Tailwind's `fontSize` in this slice**: switching `text-base` from 1.55 (26.35px) to 1.75rem (28px) moves every line of every screen, which contradicts the slice 0 acceptance "App looks unchanged" and the instruction that geometry must not regress. Themes-spec T1 (= slice 1) owns "4px type scale line-heights"; slice 1 flips the Tailwind values to the tokens and re-derives the transcript height maths. A test pins the shipped Tailwind sizes so the flip is a deliberate edit.
- **Lints / never-varies tests** (`skinBlocks.test.ts`, new, 55 tests): palette blocks contain no `var()` and declare `--c-ledger` 12 times; every palette block declares every role (no leakage, in `tokens.test.ts`); skin blocks declare only Layer B variables, `skinMaySet` Layer C rows and Calm's `--pad-card: 2rem`; no rule scoped to a skin sets padding, margin, gap, width, height, display, position, order, font-size, line-height (property, `@apply` class or layout-named custom property); each Layer C token is declared exactly once in `:root` with its default and nowhere else except a permitted skin row; `--scrim` per mode; type scale on the grid; focus ring from the tokens and `Highlight` under forced colours; `.btn` / `.field` 44px = `--control-h`, Quick recap 56 / 64 = `--control-h-lg` / `-xl`; transcript 17px body, 68ch, 3.25rem column, and no skin rule may target `.transcript*` (except Gamer's `.transcript-time` font only), `[data-lecture-root]`, `#lecture-*`, `state-label`, `.panel`, `.btn`, `.field`; page decoration removed on lecture routes; no `@keyframes`, no `animate-*`, every `animation` declaration is a reduce-motion neutraliser; no `[data-quiet` rule mentions a safety surface; `[data-chrome]` never reaches into the lecture root and the lecture route forces the opaque header; no raw hex in stylesheets outside the palette blocks (Glass gradient stops and the forced black/white excepted); **ratchets** (count today, fail on increase, per file): `text-accent` (13 in 9 files), half-step spacing classes (14 in 8 files), arbitrary lengths outside the allowlist 44/48/56/64px, `3.25rem_`, `0.2em` (30 in 13 files). Existing guards (no opacity modifiers, no raw Tailwind palette colours, no hex in `.tsx`, `aria-live` allow-list) unchanged.

### Contrast results (recomputed by `tokens.test.ts`; the recomputation wins)
Every pair in section 2.4 passes and is within 0.02 of the hand value; **no token value had to change**. Recomputed (hand value in brackets where it differs by 0.01): ledger light L = 0.9393 (matches). ink 16.22 (16.21), muted 7.22 (7.23), accent 9.24, recording 6.19 (6.20), success 6.16, warning 7.52 (7.53), danger 7.40, border-strong 3.46 (3.47). Ledger dark L = 0.0109 (hand 0.01088). ink 14.34 (14.33), muted 7.17, accent 9.02, recording 7.53, success 9.41, warning 9.64 (9.63), danger 7.91, border-strong 4.32 (4.33). The full matrix (7 text roles x 9 backgrounds, 6 boundary roles x 5 plain backgrounds, filled pairs, chrome, grid, Glass composites) passes in all 12 palettes. Tightest margins unchanged: Paper border-strong vs sunken 3.01, Gamer light accent on sunken 4.78. Section 2.4's hand arithmetic stands; the 0.01 differences are rounding.

### Unslashed-zero probe (section 2.1)
Checked by parsing the GSUB feature list of the self-hosted woff2 files `next/font` produced (`.next/static/media`, node script, no dependency). Atkinson Hyperlegible Next as served (latin and latin-ext subsets) exposes `ccmp frac locl pnum tnum` only: **no `zero` feature and no stylistic set**, so there is no unslashed zero to switch on. Per the spec, left as is. Caveat: this is the Google-Fonts-served subset; the upstream release may carry more features, which would need a `next/font/local` build to use (not done, not in scope).

### Test and verification record (actual runs, 2026-10-30, `src/web`)
| Check | Command | Result |
|---|---|---|
| Typecheck | `npx tsc --noEmit` | clean |
| Lint | `npx eslint src e2e e2e-harness e2e-screens` | 0 errors, 1 pre-existing warning (`_label` in `e2e/lecture.spec.ts`) |
| Unit, before any change | `npx vitest run` | 34 files, **1733 passed** (the brief said 1726; 1733 is what this tree ran) |
| Unit, after | `npx vitest run` | 35 files, **1962 passed**, 0 failed, no existing assertion edited (+229: ledger in the matrix, the ledger hand values, `skinBlocks.test.ts`) |
| Audit | `npm audit` | 14 moderate, all `minimatch` via eslint plugins (dev dependencies), pre-existing; lockfile untouched |
| Screens + axe, 4 looks x 2 widths | `npx playwright test -c playwright.screens.config.ts --workers=1` (API on :5080, web started by the config) | **18 passed (4.7 min), 0 axe violations in 144 captures**; 176 PNGs compared pixel by pixel with the previous set (copied to a scratch folder before the run): 120 byte-identical; 48 differ only in the run-specific display name `Screens-<run>` (header cluster x 651-1098 / y 6-49 and the `Hi, {name}.` h1) and 8 by 11 to 668 anti-aliased pixels inside hover underlines or hairlines (crops inspected, same state in both). The 8 `palette` captures differ over the whole scrim area, which is the intended `--scrim` change (.55 to .6 light, .7 dark). No other screen changed. |

Not run: harness e2e (47) and courses e2e (11) were not re-run for this slice (no component, markup or behaviour changed; the unit suite and the screens run cover the CSS); lecture e2e with a fake microphone remains NOT RUN on this machine; geometry suite does not exist yet (slice 1).

### Open for the Lead
1. Keep `--scrim` light at .6 (spec) or revert to .55 so the consent pop-up backdrop stays byte-identical under G1? One line either way.
2. Confirm that the 4px type-scale line boxes are applied in slice 1 (as T1 says) and not here; the tokens and the grid test are in place, Tailwind still carries the shipped values.
3. The brief's unit baseline (1726) did not match this tree (1733 before the change); nothing was removed.

## Summary
1. Round 1 is honest, accessible and well-tokenised, but generic: a two-link rail, bordered buttons everywhere, white cards on paper, uppercase eyebrows, a framed and mostly empty transcript, a recap squeezed into a 360px rail, and a syllabus review with about 25 bordered controls.
2. Direction: "the reading room, second edition": paper is the surface, one white sheet per screen for what is being read, lists as rule-separated ledgers, labels and timestamps hanging in a margin column, accent spent only on the one primary, focus and citations, serif for synthesis and sans for verbatim and controls, zero transforms.
3. Tokens: all shipped colours kept; one new role `ledger` (Paper `#FAF8F3` / `#181B21`, equal to surface elsewhere) with every pair hand-computed and passing; Layer A/B/C contract written so the five looks remain skins over one geometry, with an explicit never-varies list and tests.
4. Plan: ten slices (0 tokens, 1 layout = T1, 2 primitives, 3 lecture + recap, 4 Today/course/forms, 5 Preferences + Theme picker = T2, 6 syllabus density, 7 Gamer + Glass = T3/T4, 8 lecture under 10 looks = T5, 9 CI = T6), each with files, acceptance criteria, tests and risk.
5. Four questions for the Product Owner: drop the rail, Demo notice presentation (G1), the two-step consent labelling (G1), and serif-as-synthesis as house style. Nothing here is human-validated.

### Lead decision on the scrim (2026-10-31)
The spec's per-mode scrim (.6 light / .7 dark) was reverted to the previous .55 in both modes, because the consent pop-up shares the backdrop and PO-17b keeps consent presentation unchanged (gate G1). The per-mode variable stays so it can be raised later with the PO and counsel. The type-scale deferral to slice 1 is confirmed.

## Slice 1 results (layout system = themes T1)

Status: IMPLEMENTED in `src/web`, 2026-10-30, by frontend-engineer. Not committed. PO-17 applied: header tabs replace the rail; serif roles untouched (slice 2 applies them); consent screens, `ConsentDialog`, `ConsentStart`'s content, the Demo-source notice and every consent / recording string are unchanged (gate G1): only the container that the consent card sits in moved with the template (below).

### What landed
- **Header tabs instead of the rail** (`AppShell.tsx`): one bar, `min-height` 56px: wordmark in a fixed 7rem slot, then `nav[aria-label="Primary"]` with "Today" and "Courses" as links (`aria-current="page"` on the current one; `.nav-tab`: ink text, current = bold + 3px ink underline drawn as an inset shadow, no accent), then on the right "Search or jump" (quiet, `kbd` hint from 768px), "Preferences" (quiet) and "Sign out" (quiet; name frozen) with one `gap-2` and `.optical-end` on the last. The "Menu" button, the left rail and the header display name are gone; the name shows in Preferences > Account ("Signed in as {name}."). Keyboard order: skip link, wordmark, Today, Courses, Search, Preferences, Sign out, main (Course name is the 8th Tab on Add a course). The first tab's ink sits at container + 112px at every width, which from 1024px is the Desk column start. On phones the actions wrap to a second row from 390px of layout width and to a third below that ("Search or jump" + "Preferences" + "Sign out" need 342px).
- **Templates** (`AppShell.tsx` sets `data-template` on `<main>` from the route via `templateFor()`; `[data-column-start]` marks the content column): *Desk* (Today, course page, All courses, syllabus routes): from 1024px a grid `var(--margin-col)` 5rem | content, 2rem gap; the margin column is empty until slice 4 hangs labels in it. *Sheet* (Add a course, consent, lecture reopen): the component sets `max-w-sheet` (40rem), left-aligned to the column; the h1 stays inside the card until slice 4. *Auth* (sign-in): `max-w-narrow` (28rem) centred, 96px from the top, "TETHER" eyebrow removed (A14). Gutters 16 / 24 / 32 on the header, the Preferences panel and the shell. The Preferences in-flow card is 40rem and left-aligned to the container (slice 5 makes it a side panel).
- **Lecture template** (`LectureScreen.tsx`, `StatusBar.tsx`, `Notices.tsx` slot): a gutter wrapper (16 / 24 / 32) around a column whose box is the column (no padding of its own): transcript 1fr | aside `var(--lecture-aside-w)` 22.5rem, `gap-10`, 72rem, 80rem from 1536px (`max-w-lecture`); Focus: one `max-w-focus` (52rem) column, and the Focus dock uses the same token, so its edges equal the column's (A11). The bar, the notice slot and the columns share container and gutters (R3). The 5rem margin track for hanging timestamps is not added yet: an empty track would narrow the transcript for nothing; slice 3 adds it with the timestamps.
- **Type scale flipped to the 4px grid** (`tailwind.config.ts` `fontSize`: 13/20, 15/24, 17/28, 20/28, 24/32, 30/36, 38/44; body `line-height: var(--leading-base)`). Transcript lines 17/28 with the timestamp on the text's first baseline (`items-baseline`, no 0.2em nudge); the partial row reserves one 28px line; `ml-16` for the 3.25rem + 0.75rem hang.
- **Transcript heading inside its panel** (A9): `.transcript-panel` (border, surface) holds a `.transcript-panel-head` row with the h2 and the scrollable `#lecture-transcript` log below it (id, role, name, `tabIndex`, `data-segment-id`, live region and `data-partial-row` unchanged). Height maths re-derived (comment in `globals.css`): desktop log = 100vh - 16rem (bar 57 + grid top 16 + Demo notice 94 + panel head 57 + bottom rule 1 + root padding 32), Focus 100vh - 26rem, phone 100dvh - 23rem (the harness test "newest line is not hidden behind the dock" still pins log height <= dock top - bar bottom).
- **44px controls**: bar Pause / Stop / Focus / Lecture settings (were 48), Paused panel Resume / Stop (were 56), Mismatch, Stopped, Stop dialog, Fatal, Mic-blocked and "Back to lecture" buttons (were 48) now take `.btn`'s 44px; `min-w-target` (44) on Pause / Stop. The pill stays 44. Quick recap windows stay 56 / 64. Tailwind `minHeight.primary` / `minWidth.primary` (48) removed. The 48px buttons inside `ConsentStart` and `ConsentDialog` are untouched (G1) and so is the syllabus `ConfirmDialog` (slice 6).
- **Optical alignment** (A1 / A7): `.btn` carries `--btn-px` (1.25rem; `.btn-sm` / `.btn-quiet` 0.75rem); `.optical-start` / `.optical-end` pull a text button's ink onto the column edge by padding + border. Used on the header's last action, "Add a course" (dashboard and All courses), "See all courses", "Back to dashboard" (x2), "Show options", the TL;DR "More" / "Show more" / "Pause updates", and the recap's "Close recap" / "Show earlier lines" / "Show more detail". The `-ml-3` / `-mr-2` / `-mt-1` / `px-0` hacks are gone.
- **Card stripe as paint** (A4): `.card-accent` draws the 4px accent stripe with a pseudo-element (`--note-stripe-w`), so the Next step and Lecture cards keep the 24 + 1px text inset of every other card, and no skin's card shadow or texture hides the stripe.
- **Baseline rows** (A6): "Your courses" + "Add a course" and "All courses" + "Add a course" use `items-baseline`; the row is a fixed 44 / 48px from 360px (new Tailwind screen `xs`) so a heading face's metrics cannot change its height (R10; measured: 2px drift under Inter and the monospace face without it). Preferences heading + Close and the transcript head use `items-baseline` too.
- **Stopped toolbar grid** (A10): search field `1fr` | "Export to Google Docs" `auto` (level with the field), the export reason below across both columns.
- **Phone meta** (A12): `text-pretty` on the course row meta; "No lectures yet" hidden below 640px (a real last-lecture date always shows); row inset `px-card` (24 + 1).
- **Off-scale classes** (A13): all `*.5` spacing classes removed except `.tag`'s padding (the consent page's Draft tag, G1; slice 2's Stamp), the consent pop-up checkbox nudge (G1) and the Demo summary stamp (slice 2). `min-h-[1.6rem]`, `leading-[1.6]`, `pt-[0.2em]`, `ml-[4rem]`, `h-[50vh]` / `min-h-[12rem]` (now CSS on `.transcript`), `max-w-[640px]`, `max-w-[52rem]`, `min-h-[48px]`, `min-w-[48px]`, `max-w-md` gone from the touched files. Ratchets in `skinBlocks.test.ts` tightened: half-step 3 files (was 8), arbitrary lengths 11 files (was 13; `48px` left the allowlist and the three files that still carry it, both consent files and the syllabus dialog, are pinned).
- **`scrollbar-gutter: stable`** on `html` (R10). Consequence worth knowing: a page too short to scroll shows an empty 15px gutter on browsers with classic scrollbars; fixed layers (dock, dialogs) span the viewport while flow content is laid out in the viewport minus the gutter. In real browsers both agree whenever a scrollbar is present or absent; only Playwright's default headless mode (scrollbars hidden, gutter still reserved) shows a 15px mismatch, which is why `geometry.spec.ts` launches Chromium with scrollbars visible and emulates a phone (overlay scrollbars) below 640px.
- **Lecture bar under every skin**: Gamer's monospace Demo tag made the bar wrap to a second row at 1280 (the geometry gate caught it), which breaks 3.3 item 2 ("56px desktop under every skin"). Inside `[data-lecture-status-bar]` a `.tag` now keeps the body face in every skin (an unskinned rule, not a skin block); Gamer's mono stays on tags elsewhere.
- **Geometry gate** (`e2e-screens/geometry.ts`, `geometry.spec.ts`): helpers `box`, `ink` (text-node client rects), `baseline` (zero-size probe beside the first text node, inside a wrapper so flex buttons align on the text), `px`, `contentEdges`, `controls`, `offGrid`. Spec: (1) Paper at 320 / 390 / 768 / 1024 / 1280 / 1440 / 1920 on Today, All courses, course page, Add a course, sign-in: wordmark ink left = container left; last header action ink right = container right; first heading (Desk) or first sheet (Sheet) on the column start, Desk column = container + 112 from 1024, Sheet 40rem, Auth centred at +96; every control >= 44 x 44 (checkbox / radio measured by its label row), prose links >= 24; header actions one height and one centre; heading + action baselines equal when on one row; card ink inset = pad + 1; header 56px from 768, two rows from 390, three below; no horizontal overflow; on-grid report for `.card` / `.btn` / `.field` / `.panel` padding and gaps (0 offenders, enforced). (2) Lecture (harness, standard and Focus) at the same widths: bar 57px from 1280 and <= 113 from 640 / 390, pill and bar controls 44 with one centre, bar content edges = column edges, Focus dock box = Focus column box, transcript panel top = Quick recap top, aside 360 after a 40px gap, container 1152 / 1280 within the gutters, transcript 17 / 28, targets, no overflow. (3) Twelve landmarks (container, header, nav, main, first card, primary button, field, transcript panel, Quick recap card, TL;DR card, recording pill, Focus dock) equal within 1px across the 10 looks (5 skins x 2 modes) at 390 and 1280, with two stated allowances: Calm's card padding moves what sits inside a card by 8px and changes card / main height; a wider heading face may add `.display` lines and a landmark may then grow by at most those lines (the "How a lecture works" summary wraps in monospace in the 1280 aside: +20px on `main`; slice 4 moves that disclosure to the full column).

### Recorded test changes (each with its reason)
- `skinBlocks.test.ts` "the shipped Tailwind sizes are untouched in this slice" **replaced** by "Tailwind's fontSize equals the type-scale tokens; 3xl is 38/44; body leading is the base token": slice 0 pinned the old values so the flip would be a deliberate edit; this slice is that edit (spec slice 1 row, themes T1, Lead confirmation under "Slice 0 results" item 2). Two tests added (transcript 17/28 + baseline, layout-system CSS). Ratchet baselines lowered (see above); `48px` removed from the arbitrary-length allowlist.
- `AppShell.test.tsx`: **no existing assertion changed**. The test named `rail` queries `navigation[name="Primary"]`, which the header tabs still are, so the rail test passes as written. Three tests added: tab order and no Menu / rail / header name; Preferences > Account; `templateFor()` and `data-template` / `[data-column-start]`.
- The "Theme" legend is untouched (slice 5 renames it; `AppShell.test.tsx` "Dark theme on." and `LectureRedesign.test.tsx` unchanged).
- No e2e string, id, role or test id changed; the harness and real-stack specs run unedited.

### Deviations and things left for later slices
- Consent (`ConsentStart`): only the container changed (`card max-w-sheet`, left-aligned, 40rem = the former 640px); content, wording, tag, buttons (48px) and the pop-up are byte-identical (G1). The "h1 above the sheet" part of the Sheet template is not applied to consent (a visible change to the consent surface) nor yet to Add a course (slice 4).
- Demo-source notice: its panel is untouched; its slot shares the lecture gutters (24px at 640-1023 and 32px from 1024 instead of 16px), the same move every element under the bar makes. The Demo tag stays in the bar on desktop (open question Q2, G1); with it the bar is two rows between 640 and about 1170px (as before the slice) and one 56px row from there; without the tag it would be one row from 1024 (not verified: every harness fixture carries the tag).
- Phone lecture bar: two rows (<= 112) from 390px of layout width; three rows (157px) at 360-389 (Pause / Stop / Lecture settings need 350px); four rows (189px) at 320 (the pill and "Connected" cannot share a 288px row). Same wrapping as before the slice; the pill stays visible at 320 (harness test).
- Phone header: two rows from 390px, three rows at 320-389 (the three quiet actions need 342px). A narrower phone treatment (for example dropping the kbd hint already happens; shortening nothing, since the names are frozen) is a design question for the Lead.
- A5 (Preferences switch-row insets) belongs to `components/ui` (slice 2). The Preferences in-flow card aligns to the container's left edge, not the Desk column (two left edges on Today while it is open); slice 5's side panel removes the question.
- Desk's 5rem margin column is visually an indent until slice 4 fills it; the lecture's margin track waits for slice 3.
- `.tag` keeps `px-2.5 py-0.5` (G1: the Draft tag on the consent page), so A13 is complete except for that class and the two G1 nudges.
- "Add a course" became a quiet (tertiary-style) button on Today and All courses, per the wireframe `_Add a course_`; it was a bordered secondary.

### Test and verification record (actual runs, 2026-10-30, `src/web`)
| Check | Command | Result |
|---|---|---|
| Typecheck | `npx tsc --noEmit` | clean |
| Lint | `npx eslint src e2e e2e-harness e2e-screens` | 0 errors, 1 pre-existing warning (`_label` in `e2e/lecture.spec.ts`) |
| Unit | `npx vitest run` | 35 files, **1967 passed**, 0 failed (baseline 1962; +5: three `AppShell` tests, two `skinBlocks` tests, one `skinBlocks` test replaced as recorded above) |
| Audit | `npm audit` | 14 moderate, all `minimatch` via eslint plugins (dev dependencies), pre-existing; lockfile untouched |
| Geometry | `npx playwright test -c playwright.screens.config.ts e2e-screens/geometry.spec.ts --workers=1` (API on :5080, `npm run dev:real` on :3000) | **4 passed** (55 s) on the final code. The first runs failed and drove fixes, which is the gate's job: tabs shifting with the wordmark's face (fixed slot), Gamer's mono Demo tag wrapping the bar (body face in the bar), heading rows growing 2px per face (fixed-height rows), the bar's gutters inside the 72rem box while the column had them outside (content-box containers), Calm's 8px inner shift and the monospace wrap of "How a lecture works" (explicit allowances). |
| Screens + axe, 4 looks x 2 widths | `npx playwright test -c playwright.screens.config.ts e2e-screens/screens.spec.ts --workers=1` (previous set copied to a scratch folder first) | **18 passed** (2.9 min), **0 axe violations in 144 captures**, 176 PNGs. All 176 differ from the previous set byte for byte, as expected: every line box moved with the type-scale flip, the rail is gone and the container widened. |
| Harness e2e | `npx playwright test -c playwright.harness.config.ts --workers=1` (54 in this tree: 47 plus the consent-dialog spec) | **54 passed** (1.1 min); repeated on the final code through `playwright.harness.reuse.config.ts`: **54 passed** |
| Real-stack e2e | `npx playwright test -c playwright.reuse.config.ts e2e/courses.spec.ts e2e/lecture.spec.ts e2e/syllabus.spec.ts --workers=1` (reuse config: a `dev:real` server was already on :3000) | courses **11 passed**, syllabus **3 passed**; lecture **1 failed, 8 did not run** (serial): the consent page and the teacher-permission dialog work, then `POST /api/v1/courses/{id}/sessions` returns **403** (expected 201); reproduced alone on a freshly started dev server. The response carries no body in the trace and the API container log is silent; it is API-side state or policy, outside `src/web`, for backend / qa. The lecture spec was already NOT RUN on this machine before this slice (themes-progress.md). |

Two earlier runs of the real-stack e2e and of the screens spec failed on every signed-in page with the app stuck on "Getting your courses." That was not the layout: `playwright.harness.config.ts` starts its own `next dev -p 3100` in the same directory, and two dev servers sharing one `.next/` corrupted the :3000 server (`ENOENT .next/server/app/page.js`, `Invariant: Expected clientReferenceManifest`). After a restart with a clean `.next/` the same specs passed as recorded. Running the harness through its `.reuse` config, or never beside `dev:real`, avoids it; the default harness config also writes to `test-results/`, which Playwright wipes, taking `test-results/redesign-screens/` with it (the screens run was repeated last for that reason).

### Screenshots viewed (new set against the scratch copy of the old one)
- `dashboard--1280--paper`: tabs "Today / Courses" beside the wordmark with the 3px ink underline on Today; the three quiet actions end on the container edge; the content column starts at 168 (container 56 + 112) with the date, h1, striped card (text inset 25), "Your courses" and "Add a course" on one baseline, and 24px row insets. The empty margin column reads as a wide left margin, not a hole.
- `dashboard--390--paper`: wordmark + tabs on row one, the three actions on row two; course rows without "No lectures yet"; the long test name wraps the h1 (a real first name does not).
- `lecture-live--1280--paper`: one 56px bar, pill and buttons 44 and centred; the transcript panel now carries its heading and its top is level with the Quick recap card (both at y 227); columns 752 | 40 | 360.
- `lecture-focus--1280--paper`: the dock's border edges equal the transcript panel's (217 to 1048): A11 closed.
- `lecture-stopped--1280--paper`: search field across the column, "Export to Google Docs" level with the field on the column edge, the reason below (A10).
- `lecture-live--390--paper--viewport`: three-row bar in this capture, because the screens spec keeps Playwright's hidden scrollbars (375px of layout for a nominal 390); the geometry spec, with a phone emulated, measures two rows at 390.
- `consent--1280--paper`: the card is byte-for-byte the same inside; it now sits on the column's left edge (56) at 640px instead of centred (G1 respected: container only).
- `signin--1280--paper`: 28rem card centred, 96px below the header, no "TETHER" eyebrow; the header shows only the wordmark and Preferences when signed out.

### Open for the Lead
1. Phone header: three rows at 320-389 (two from 390). Accept for now (slice 5 may move Preferences; the names are frozen), or decide a narrower treatment.
2. Lecture e2e: the 403 on session creation needs backend / qa eyes; nothing in this slice touches that request.
3. Container model: this slice reads R2 as "72rem of content with the gutters outside" (content-box containers); the round-1 build had the gutters inside the 72rem. The geometry spec pins the new reading.
4. `scrollbar-gutter: stable` shows an empty 15px gutter on short pages (sign-in) in browsers with classic scrollbars; R10's trade, flagged so no one reads it as a bug.

## Slice 2 results (primitives v2)

Status: IMPLEMENTED in `src/web`, 2026-10-31, by frontend-engineer. Not committed. PO-17 applied: serif = synthesis / sans = verbatim roles are consumed where the primitives define them (`.stamp`, `.label`, `.note` body are verbatim face; dialog and sheet headings keep `.display`); header tabs were slice 1. **Gate G1:** `ConsentDialog.tsx`, `ConsentStart.tsx`, `Notices.tsx`'s Demo-source notice and every consent / Demo / Draft string are untouched (git shows no diff on the two consent files). Where a shared class was restyled (`.link`, the disabled `.btn`), a CSS carve-out keyed on attributes the consent components already carry (`section:has(> #consent-h1)`, `[data-consent-dialog-overlay]`) restores the pre-slice declarations; where the consent surfaces use a legacy class (`.btn-quiet`, `.eyebrow`, `.tag`), that class is unchanged and now used only there (pinned by `skinBlocks.test.ts`). The new dialog treatment lives on `.dialog` / `.dialog-scrim`, which the consent pop-up does not receive; it keeps `bg-[var(--scrim)]` at .55. `e2e-harness/consent-look.spec.ts` asserts the consent page's and pop-up's computed values (scrim .55, raised 28rem box, 30px heading, dashed border-strong disabled confirm, accent "Cancel" / "Not now", uppercase 13px eyebrow, Draft tag) and, by contrast, the v2 look on the Stop dialog.

### What landed (`globals.css` components layer, `components/ui/*`, `UiKit.tsx`, class swaps)
- **Layer B**: `--scrim-dialog` per mode (`:root` `rgba(20,22,27,.6)`, dark `rgba(0,0,0,.7)`; `SCRIM_DIALOG` in `tokens.ts`, `LAYER_B_VARS` gains `scrim-dialog`). `--scrim` stays .55 for the consent pop-up (Lead decision above). Consumed only by `.dialog-scrim` (asserted).
- **`.sheet`**: `--sheet-bg` / `--sheet-border` / `--sheet-radius` / `--pad-card`; below 640px square-edged (radius 0, no side borders); `.sheet-bleed` opts into the 16px phone gutter bleed. Not yet applied to any screen (slices 3 / 4 / 6).
- **`.ledger`** / `.ledger-row` / `.ledger-row-link` / `.ledger-row-end`: `--ledger-bg`, rows parted by `--ledger-rule`, 16px row padding, the tint bleeds 16px outward (R8) so row text sits on the column edge; the whole row is one link via `::after` on the title (one tab stop, one name), the trailing "Open" + chevron is `aria-hidden`; hover and focus-within tint `sunken` (Low-stimulation: 2px border-strong inset instead of fill).
- **`.note`** (`-neutral` / `-notice` / `-safety` / `-danger`, `.note-title`): `--note-stripe-w` stripe painted by `::before`, 16px padding (text 20px from the outer edge), no outer border, stripe colour from border-strong / warning / recording / danger. Variant names mirror `.panel-*` so slices 3 and 6 can swap mechanically. `.panel` is unchanged and still used (Demo-source notice, G1; safety panels, slice 3).
- **`.stamp`** / `.stamp-line`: the four `--stamp-*` tokens, xs 700 in `--font-verbatim`, 28px tall, 8px side padding (no half-step classes); the sentence beside it sm muted. **Not applied to any Demo / Draft tag** (G1: Demo-source and Draft presentation unchanged); `.tag` stays as is.
- **`.label`**: sentence case, `--font-verbatim` 15/24 regular, `--label-ink`, no tracking, `text-transform: none`. Replaces `.eyebrow` on Today (date, "Next step"), Preferences ("Look", "About", "Account"), Mic-blocked ("How to turn it on") and the Lecture settings popover ("Look", "During the lecture"). `.eyebrow` remains only on the consent page ("Before you start", G1) and once on the ui-kit as a labelled legacy sample. The form field label was `.label` before; it is now **`.field-label`** (same declarations, 16 call sites in 8 files renamed; the Field row says "unchanged").
- **`.btn-tertiary`**: ink text, transparent border and fill, `--btn-px` 0.75rem, underline on hover and `:focus-visible`. All non-consent `.btn-quiet` call sites (14 files) swapped; `.btn-quiet` (accent) is now the consent surfaces' button only. `.btn` takes `border-radius: var(--btn-radius)` and `border-width: var(--btn-border-w)` (computed 8px / 2px as before).
- **Disabled**: `.btn[aria-disabled="true"]` is `border-solid border-hair bg-sunken text-muted` (was dashed border-strong); no opacity anywhere on `.btn`. Consent carve-out keeps dashed border-strong (G1).
- **`.link`**: `--link-ink` text, 2px `--link-line` underline at 0.2em, hover accent text, weight 600 kept so links stay findable when scanning [J]. Consent carve-out keeps accent text with a 1px text-coloured underline. Disclosure summaries (`components/ui/Disclosure.tsx`, Mic-blocked's help list) use `.link`; the "How a lecture works" step numerals are muted (2.2).
- **Chevron**: shared `Chevron` in `components/ui/Disclosure.tsx` (also re-exported from `syllabus/icons.tsx`), muted, two glyphs swapped by state instead of a rotation: **no transform remains in `src/`** (2.6; lint added).
- **Dialog**: `.dialog-scrim` + `.dialog` (sheet tokens, `max-w-narrow` 28rem, `shadow-3`, `display text-xl` heading) on the lecture Stop dialog, the syllabus `ConfirmDialog` (its 48px buttons left for slice 6) and the command palette (scrim only; its box is a search sheet, unchanged).
- **A5 / R8**: `.hover-row` (`-mx-2 px-2`, `width: calc(100% + 1rem)`) on `Switch`, so Preferences switch rows align their text with the legends and their On/Off word with the Close button's edge. **Segmented**: `inline-grid auto-cols-fr grid-flow-col`, so the options of a group share one width.
- **Gamer** mono now also covers `.label` and `.stamp` (paint only; skin lint still green). Forced colours: `.sheet`, `.dialog`, `.stamp` borders, `.note` outline and ledger rules in `CanvasText`.
- **UI kit** (`/dev/ui-kit`, `UiKit.tsx`, `UI_KIT_STRINGS` exported for the copy lint): type roles (synthesis / verbatim / label), button tiers incl. three disabled tiers, busy, small and optical-start, fields, one sheet, a ledger of three rows, four notes, stamps and a stamp line, an in-place (non-modal) dialog sample, Switch / two Segmented groups / Disclosure, status text, and a "Still in use" block (card, panel, tag, and the two consent-only legacy classes so they stay under axe and contrast in every look).

### Tests added or changed (each with its reason)
- `tokens.ts`: `PRIMITIVE_PAIRS` (40 named fg / bg pairs the primitives draw, with 4.5 or 3); `tokens.test.ts` runs them in all 12 palettes (+480 assertions). All pass; every pair was already inside the full matrix, this names which primitive depends on which pair.
- `skinBlocks.test.ts`: `--scrim-dialog` per mode and single consumer; each primitive consumes its Layer C tokens (sheet, ledger, note, stamp, label, link, tertiary, btn radius / border, disabled solid, dialog, hover-row, segmented); G1 carve-outs and legacy declarations pinned; `.eyebrow` / `.btn-quiet` call sites pinned to the consent files (+ ui-kit sample); `uppercase` only in the `.eyebrow` rule; no `<label className="label">`; no transform utility or property; **text-accent ratchet tightened**: `DashboardView.tsx` (2), `MicBlocked.tsx` (1), `Disclosure.tsx` (1) removed from the baseline; the rest unchanged (globals.css 3 = `.btn-quiet`, `.tag-accent`, the consent link carve-out).
- `primitives.test.tsx`: Switch hover-row, Segmented equal columns, Disclosure link summary + two-glyph chevron (no rotate), controlled Chevron, UI kit presence of every primitive and state, aria-disabled (focusable) with a reason in all three tiers, `.field-label` on every `<label>`, axe (structure), and the copy lint (`FORBIDDEN`, 30-word sentences) over `UI_KIT_STRINGS`.
- `e2e-harness/consent-look.spec.ts` (new, 3 tests): G1 computed-look pins described above, plus the v2 Stop dialog by contrast.
- `e2e-screens/screens.spec.ts`: the `ui-kit` screen is captured in **all 12 looks** (Paper, Night desk, Fog, Dusk + portal / gamer / calm / glass x light / dark) at 390 and 1280 with axe; other screens keep the four looks. `e2e-screens/geometry.ts` `offGrid` also measures `.sheet`, `.ledger-row`, `.note`, `.stamp`, `.dialog`; `geometry.spec.ts` gains "ui-kit: 44px targets, visible focus ring on every primitive, on the 4px grid, five skins at 390 and 1280".
- **No existing assertion was changed or removed.** No e2e string, id, role or test id changed.

### Deviations (recorded)
- Spec acceptance "no `uppercase` eyebrow left in `src/`" and slice 0's "no `text-accent` on non-primary, non-citation text after slice 2" are met **except on the consent surfaces** (G1 keeps `.eyebrow`, the accent "Not now" / "Cancel", the accent link and the "What does this mean" summary) and the lecture citation / recap files (slice 3). Pinned, not hidden.
- `.stamp` is defined and shown but not applied to any Demo / Draft tag (G1); `.tag` keeps `px-2.5 py-0.5` (half-step ratchet unchanged).
- `.sheet` / `.ledger` / `.note` are not applied to any screen (slices 3, 4, 6); the ui-kit is the proof.
- `.link` weight 600 is a judgement call [J] (spec gives none).
- The ledger bleeds 16px outward by default (R8 reading of "text inset equals the column's left edge"); slice 4 may set `--ledger-inset` per use.
- The `.btn-quiet` -> `.btn-tertiary` and `.eyebrow` -> `.label` swaps touch files later slices own (dashboard, lecture, syllabus); they are class-name swaps only, no markup or wording change.

### Test and verification record (actual runs, 2026-10-31, `src/web`)
| Check | Command | Result |
|---|---|---|
| Typecheck | `npx tsc --noEmit` | clean |
| Lint | `npx eslint src e2e e2e-harness e2e-screens` | 0 errors, 1 pre-existing warning (`_label` in `e2e/lecture.spec.ts`) |
| Unit, before any change | `npx vitest run` | 35 files, **1967 passed** (= baseline) |
| Unit, after | `npx vitest run` | 35 files, **2432 passed**, 0 failed (+465: 480 primitive pairs, 13 skinBlocks, 11 primitives, minus the 3 replaced ui-kit/axe tests folded into the new file's structure; no existing assertion edited) |
| Audit | `npm audit` | 14 moderate, all `minimatch` via eslint plugins (dev dependencies), pre-existing; lockfile untouched |
| Screens + axe | `npx playwright test -c playwright.screens.config.ts e2e-screens/screens.spec.ts --workers=1` (API on :5080, clean `dev:real` on :3000; the previous set copied to a scratch folder first) | **18 passed (3.3 min), 0 axe violations in 160 captures** (17 screens x 4 looks x 2 widths + ui-kit x 12 looks x 2 widths), 192 PNGs. Pixel diff against the before set (see "Screenshots viewed") |
| Geometry | `npx playwright test -c playwright.screens.config.ts e2e-screens/geometry.spec.ts --workers=1` | the 4 slice-1 tests **passed** (54 s); the new ui-kit test failed its first run on two real findings and passed after the fixes (below): prose and ledger links measured 22px tall (now `padding-block: 0.25rem` on `.link` / `.ledger-row-link`, 30px, consent carve-out resets it), and the test focused `tabindex="-1"` segmented options, which have no ring by design (test now skips programmatic targets) |
| Geometry, final code | same command | **5 passed** (57 s): the 4 slice-1 tests and the ui-kit test |
| Harness e2e (reuse config) | `npx playwright test -c playwright.harness.reuse.config.ts --workers=1` (3100 -> 3000 forwarder; never the plain harness config beside a dev server) | **56 passed, 1 failed** on the first run: the failure was my new `consent-look.spec.ts` matching `rounded-sheet` with `\bsheet\b` (test bug, fixed to a hyphen-aware boundary); re-run of that spec **3 passed**. The 54 pre-existing harness tests all passed unedited (47 + the consent-dialog spec + label-in-name loop). |
| Real-stack e2e | `npx playwright test -c playwright.reuse.config.ts e2e/courses.spec.ts e2e/syllabus.spec.ts e2e/lecture.spec.ts --workers=1` (reuse config because a `dev:real` server was already on :3000; the plain config would start a second one) | **23 passed (2.7 min)**: courses 11, lecture 9, syllabus 3. The lecture spec, NOT RUN / 403 in earlier slices on this machine, passed in full this time (fake microphone, consent, pause / resume / stop, reconnect, mic denied, Quick recap, TL;DR). |
| ui-kit re-capture after the link hit-area fix | `SCREENS=ui-kit npx playwright test -c playwright.screens.config.ts e2e-screens/screens.spec.ts --workers=1` | **1 passed, 0 axe violations in 24 captures** (12 looks x 2 widths); 192 PNGs in `test-results/redesign-screens/` |

Not run: nothing from the brief's list. Not verified: screen-reader, real-device and human review (gate G4 NOT_RUN); the Low-stimulation ledger hover (border instead of fill) and the ledger `:has()` hover rules are covered by CSS assertions, not by a browser interaction test; `forced-colors` additions are not browser-tested (slice 7 owns `emulateMedia`); the eight skin x mode ui-kit captures other than Paper, Night desk and Gamer dark were axe-checked but not viewed one by one.

### Screenshots viewed (new set against the scratch copy of the before set, pixel-diffed in headless Chromium)
- `consent--1280--paper` and `consent--390--*` (all 4 looks): the only differing pixels are the header's three quiet actions (1280: x 841-1208 / y 22-35; 390: the second header row), which are now ink `.btn-tertiary`. **Nothing inside the consent card differs: 0 px** (eyebrow still uppercase, Draft tag, accent "What does this mean?" and "Not now", 48px buttons). G1 held.
- `lecture-live--1280--*`: 31 rows of pixels differ, all in "Lecture settings" (bar) and "Pause updates" (TL;DR card), accent -> ink; the Demo-source notice, pill, bar, transcript and recap are pixel-identical. `lecture-live--390--*`: the same two controls plus the TL;DR "More".
- `dashboard--1280--paper`: page 8px taller (two labels at 15/24 instead of the 13/20 eyebrow); date and "Next step" sentence case muted; header actions, "Add a course" and "Why this?" in ink (link underline accent); step numerals muted; the chevron muted and not rotated. `dashboard--390--paper`: same, 8px taller.
- `ui-kit--1280--paper`, `ui-kit--390--paper`: every section renders; the ledger tint bleeds 16px left of the column (R8, by design) and is full-bleed on the phone with the `.sheet-bleed` sheet; the in-place dialog sample sits on the .6 scrim; disabled tiers are solid hair-bordered sunken; stamps 28px with their sentence on one line.
- `ui-kit--1280--gamer-dark`: mono labels and stamps (skin rule), .7 scrim, every pair legible; `night`, `portal-*`, `calm-*`, `glass-*` captured and axe-clean (not viewed one by one).

Dev-server note (same failure mode slice 1 recorded): the first full screens run on 2026-10-31 returned 18 failures that were not the UI: the `:3000` dev server had gone into the `Invariant: Expected clientReferenceManifest` / `Jest worker encountered 2 child process exceptions` state (its log shows the invariant at the first `GET /` after a session restart), so every page but sign-in and the ui-kit came back as the `/_error` page, and the ui-kit was served without its CSS (axe then reported `target-size` on 20px unstyled buttons and `color-contrast` in dark looks). An axe run against a healthy server showed 0 violations on the ui-kit at 1280 and 44px boxes. The broken server (PID 34568) was stopped, `.next/` removed and one `npm run dev:real` restarted; every browser result below is from that clean server. No second `next dev` was started in `src/web`; the harness runs through the `.reuse` config over a 3100 -> 3000 TCP forwarder (`scratchpad/forward3100.mjs`).

## Slice 3 results (lecture screen)

Status: IMPLEMENTED in `src/web`, 2026-10-31, by frontend-engineer. Not committed. Every row of the verification record below is an actual run on the final code; nothing here is human-validated (gate G4 NOT_RUN) and gate G1 stays BLOCKED.

### PO-17 and gate G1 handling
- Serif is the voice of synthesis: the TL;DR one-line summary and the recap's "Most recent" sentence are set in `--font-synthesis` (the skin's heading face, weight 500, 24/32); every verbatim line, control, timestamp and notice stays in the sans. Sheet headings ("Transcript", "Here is what was said") keep `.display`.
- **Unchanged (G1, PO deferral):** `ConsentDialog.tsx`, `ConsentStart.tsx`, the Demo-source notice (`DemoNotice`: `.panel panel-notice`, info icon, bold sans heading, body / "Details" behaviour, wording) and the Demo tag in the bar (`.tag tag-warning`, `hidden sm:inline-flex`). The spec's one-line Note with Details (section 5.1) and moving the tag off the bar (Q2) are NOT implemented. The notice keeps its slot under the bar at column width; only what sits around it changed. `Notices.tsx` keeps its one `text-accent` (the notice's "Details" summary) in the ratchet for that reason.
- Recording status: pill markup, size, colours and position untouched; the bar's Pause / Stop stay bordered secondaries in their places; nothing moves, flashes or reorders during a lecture except what the student opens (the recap sheet).
- Quick recap: one press, no confirm, stable order 2 / 5 / 10, usual window the only fill.

### What landed (`src/web`: `LectureScreen.tsx`, `Transcript.tsx`, `QuickRecapPanel.tsx`, `TldrPanel.tsx`, `Notices.tsx` (classes only, Demo notice excluded), `useQuickRecap.ts` (type export), `copy.ts` (one string), `globals.css` lecture rules)
- **Transcript as the sheet.** `.transcript-panel` now carries `.lecture-sheet` (sheet tokens `--sheet-bg` / `--sheet-border` / `--sheet-radius`), the heading inside as `.sheet-head` (A9 kept), and **no fixed height**: the log has `min-height: 12rem` and a `max-height` equal to the old viewport bounds (desktop `100vh - 16rem`, Focus `100vh - 26rem`, phone `100dvh - 23rem`), so the sheet ends where the words end and scrolls only when the words would reach the page bottom or the dock. The inset is a fixed 24px (not `--pad-card`), per themes-spec 4 (Calm keeps the transcript at 24); below 640 the sheet is square-edged and full-bleed, so its 16px inset is the page gutter.
- **The margin track from 1280.** The lecture grid is `5rem | 1fr | 22.5rem` (gap 40) in the standard layout and `5rem | 1fr` in Focus; the left column is a CSS subgrid, so a sheet spans the margin and the transcript tracks while the collapsed TL;DR note and the Stopped toolbar start on the transcript track. A lecture sheet paints its surface with a `::before` from the transcript track (`--sheet-start` = margin + 40) and lets its timestamps and citation times hang in the margin, on the page; the heading and the words sit at margin + 40 + 24 + 1 = 145px from the container's left edge in both sheets (`--hang-text`). Transcript lines keep `grid-cols-[3.25rem_...]` and `max-w-prose` below 1280 (the skinBlocks pins stand) and switch to `margin-col | 1fr` with `max-width: 68ch + 145px` at 1280. A 1px end padding keeps the log's scrollbar inside the painted border. Cost: at 1280 the words run about 607px (64ch) instead of 638px.
- **Quick recap result as a sheet above the transcript (desktop).** `useQuickRecap` moved from the panel to `LectureScreen`, which renders the window buttons in the rail (or dock) and, from 1024 (`useWide`), the result as `<QuickRecapResult level={2} sheet>` in the transcript column directly above the transcript (DOM order = visual order; an `h2`). Below 1024 the result renders inside the dock as before (`level={3}`, bottom sheet up to 60dvh); the page then gains 60dvh of scroll room under its columns (`:has(#lecture-quick-recap[data-has-result])`), because the transcript no longer has a fixed height that made the page tall enough to bring lines up under the bar. Result layout per wireframe 5.2: heading and "Close recap" in the sheet head; coverage line and the Demo summary stamp on one `.stamp-line`; "Most recent" as a `.hang-row` (time in the lead column / margin, sentence in `.synthesis text-xl`); claims as hanging rows; "Also mentioned" as a plain list; one footer row with "Show more detail" (tertiary) left and "Back to lecture" (secondary) right; the detail opens **below** that row so the buttons never move. Focus still moves to the heading; "Close recap" and "Back to lecture" both close and return focus to the log; one result at a time.
- **The rail as a ledger.** `[data-lecture-rail].lecture-rail` (ledger tint, no outward bleed so its right edge is the bar's control edge, 16px row inset, hair rule between rows) holds `#lecture-quick-recap` and `#lecture-tldr` as its two rows; the rail, not the panel, is `position: sticky` under the bar from 1024 (`top: 4.5rem`, `max-height: calc(100vh - 7rem)`, scrolls inside only when the TL;DR is opened wide). Below 1024 the wrapper is `display: contents`, so the dock stays a fixed bottom bar and the TL;DR sits above the transcript as before.
- **Windows in one row**, the usual one filled and taller (56 / 64 unchanged). The usual label is two lines, "Last 5 min" + "(your usual)" in a smaller weight (new string `windowUsualNote`, the same text `windowUsual` joins), so it no longer breaks as "Last 5 min / (your / usual)" in a 104px column (seen in the before captures at 375). Accessible names unchanged; textContent is still "Last 5 min (your usual)".
- **Rail titles.** "Quick recap" and "TL;DR" are `.rail-title` (sans 15/24 bold ink) in the rail, the dock and the note; the hint stays sm muted under the title. See deviation 1.
- **Serif = synthesis.** `.synthesis` (`--font-synthesis`, weight 500, the skin's heading tracking) on the TL;DR one-line summary (24/32, `line-clamp-2` while collapsed) and the recap's "Most recent" sentence. TL;DR bullets are a plain `list-disc` with muted markers. TL;DR "From 00:02" source buttons and every recap citation are `.cite` (accent from `var(--c-accent)`, tabular, 1px underline, 2px on hover / focus, 44px hit area from `--control-h` through inner padding plus equal negative margin, so a 28px row does not grow). `text-accent` is gone from `QuickRecapPanel.tsx`, `TldrPanel.tsx` and the Focus toggle.
- **Collapsed TL;DR as a Note** (phone and Focus): `#lecture-tldr` is `.note note-neutral` with the rail title, the synthesis line and "More" / "Less" (names, ids, `aria-controls`, `aria-live="off"` unchanged).
- **Safety panels as notes:** Paused (`note-notice`), Mismatch (`note-safety`), Stopped (`note-neutral`), Offline banner (`note-notice`), Fatal (`note-danger`), action error, audio-skipped and the Quick recap error (`note-notice`); their headings are `.note-title` (sans 700 lg) instead of serif xl, so every notice speaks in one voice with the Demo heading and the serif stays with synthesis. Shape + word kept (`PanelIcon`), `data-panel` hooks kept. The Demo-source notice stays `.panel panel-notice` (G1).
- **Bar.** The Focus toggle is a tertiary (`aria-pressed` gives bold plus a 3px ink underline through `.btn-tertiary[aria-pressed="true"]`; the icon swaps as before) instead of a secondary with an accent tint. Pause / Stop / "Lecture settings" / the pill / the Demo tag are unchanged.
- **States.** Inside the lecture root a filled primary answers hover with a 1px inner on-accent edge instead of the generic button underline (review N1), scoped to `[data-lecture-root]` so the consent surfaces keep their hover (G1); `.cite` hover and focus thicken the underline; the pressed tertiary above. The sheet, the rail and the notes have no hover (they are not links).
- **Stopped toolbar** keeps the slice-1 grid and starts on the transcript track at 1280 (field left edge = sheet surface edge).

### Deviations from the spec, with reasons
1. **Rail titles are sans 15/24 bold, not serif xl** (2.1 lists "h2 inside a sheet (TL;DR, Quick recap)" at xl). The TL;DR heading would sit directly above the serif xl synthesis line at the same size; two serif lines of one size compete, and the serif is the voice of synthesis (PO-17), so the app's own captions take the sans. The wireframes draw both rail headings as divider-style labels (`..... TL;DR .....`). Sheet headings ("Transcript", "Here is what was said") keep `.display text-xl`. [J]
2. **Notice headings are `.note-title` (sans 700 lg), not serif xl.** Spec 2.7 Note says "heading in sans 700 lg"; 1.4 complained about two voices (serif Paused next to the bold-sans Demo heading). With the Demo heading frozen (G1), one voice for notices means sans. [J]
3. **The Demo summary tag is now a Stamp** (`.stamp` plus its sentence; wording and the `demo-summary-tag` test id byte-identical; still always shown for `source === "mock"`). Slice 2 left it for this slice; it labels AI output and is not the Demo-source notice the PO froze. If the PO reads G1 as covering it, the revert is one component (`DemoSummaryTag`). Flagged in the report.
4. **Timestamps hang left-aligned on the container edge**, about 65px from the words, not right-aligned against the sheet border: every hanging label in the Desk template starts on the container edge, so the lecture does the same. [J] It reads as a book margin; if students find the times too far from their lines, right-aligning inside the margin is one declaration (`text-align: end` on `.transcript-time` at 1280).
5. **Both lecture sheets use a fixed 24px inset**, not `--pad-card`: themes-spec 4 keeps the transcript at 24 under Calm (the landmark gate forbids Calm moving the transcript), and the recap sheet follows so the two sheets share one text edge in every skin.
6. **The result sheet is not height-capped on desktop.** The old rail card grew to about 830px; the sheet is about 430px for the fixture, and the page scrolls when a recap is long (student-initiated). "First transcript line visible at 1280" holds at 1280 x 800 (harness test); the TL;DR cannot fall below the fold because the rail is sticky.
7. **In Focus mode the desktop result also opens in the column** (the spec names the standard layout only): one desktop behaviour, and the Focus dock stays a slim bar instead of growing to 60dvh over the transcript.
8. **The primary hover change is lecture-scoped**: a product-wide `.btn-primary:hover` would change the consent primary's hover (G1); slice 4 decides product-wide.
9. Spec 5.1's Demo notice as a one-line Note with Details and the Demo tag off the bar: **not done** (PO deferral, G1).

### Recorded test changes (each with its reason)
- `QuickRecapPanel.test.tsx` "M1: the panel is a fixed bottom bar below lg and a sticky column from lg": the `lg:sticky` assertion on `#lecture-quick-recap` **replaced** by: the panel is `fixed` below lg and `lg:static` inside `[data-lecture-rail].lecture-rail`, whose next row is `#lecture-tldr`; the sticky rule itself is pinned in `skinBlocks.test.ts`. Reason: the spec makes the whole rail the ledger, and a sticky child inside a wrapper of its own height cannot stick, so the wrapper sticks.
- `skinBlocks.test.ts` ratchets tightened (lower counts only): `text-accent` LectureScreen 2 -> 1 (the skip links' focus style), QuickRecapPanel 1 -> 0; half-step QuickRecapPanel 1 -> 0 (the Demo summary `px-2.5` is gone); arbitrary lengths QuickRecapPanel 6 -> 1 (`max-h-[60dvh]`), TldrPanel 1 -> 0. New describe "lecture screen (redesign-round2 slice 3)" (7 tests): sheet tokens and fixed inset, the max-height bounds, the 1280 margin rules and the screen's matching grid tracks, the rail ledger / sticky / title, `.synthesis` and `.cite`, the pressed tertiary and the lecture-scoped primary hover, and that the safety surfaces are notes while the Demo-source notice, its heading, its Details summary, its body and the bar's Demo tag are byte-identical in source (G1).
- `LectureRedesign.test.tsx`: 4 tests added (the result sheet in the column before the transcript and outside the rail, heading focused, synthesis / hang-row / cite classes, the stamp, Close recap returns focus; the rail as one ledger with the two-line usual label and the serif summary; Focus: TL;DR note plus the tertiary pressed toggle; the transcript sheet, the Paused note and the unchanged Demo notice). No existing assertion changed.
- `e2e-screens/geometry.ts`: `landmark()` counts `.synthesis` lines beside `.display` (the synthesis role follows the skin's heading face, so a wider face may add a line to the TL;DR landmark: the same allowance for the same reason); `offGrid` also measures `.lecture-sheet` and `.rail-row`; `box()` retries once after 150ms when a present, visible element returns no box, and reports count / visibility / url when it still fails. Seen on the landmark tests: `#lecture-quick-recap` returned no box when read within three round trips of the server-rendered "Fixture:" text becoming visible, then counted 1 and visible; the SSR HTML contains every landmark, forty isolated probes across the 10 looks (standard and Focus, both widths) always had the box, and a mutation observer saw no remount in isolation. Recorded as observed, not explained.
- `e2e-screens/geometry.spec.ts` lecture test, new checks at every width: from 1280 the sheet box starts on the column, the first timestamp on the column's left edge, the words and the "Transcript" heading at column + 145; below 1280 the timestamp sits at the sheet's inset (16, or 24 + 1).
- `e2e-harness/redesign.spec.ts`: 2 tests added (1280 x 800: the result sheet in the transcript column, above it, not in the rail; first line, TL;DR and the usual window in view; the citation time on the sheet's left edge and the synthesis lead on the transcript's text edge; Close recap returns focus. 390 x 844 with phone emulation: the result inside the dock; after parking the first line under the bar, at least three lines sit between the bar and the sheet; Close recap in view). No existing harness assertion changed.
- No e2e string, id, role, test id or real-stack assertion changed.

### Test and verification record (actual runs, 2026-10-31, `src/web`; dev server `npm run dev:real` on :3000 started clean by the previous agent, TCP forwarder on :3100 for the harness, API on :5080; no second `next dev` was started)
| Check | Command | Result |
|---|---|---|
| Typecheck | `npx tsc --noEmit` | clean (run after every source change; last run on the final code) |
| Lint | `npx eslint src e2e e2e-harness e2e-screens` | 0 errors, 1 pre-existing warning (`_label` in `e2e/lecture.spec.ts`) |
| Unit, before any change | `npx vitest run` (targeted, lecture + ui + skinBlocks) | 236 passed, 1 failed: the M1 `lg:sticky` assertion, recorded above |
| Unit, final code | `npx vitest run` | 35 files, **2443 passed**, 0 failed (baseline 2432; +11: 4 `LectureRedesign` tests, 7 `skinBlocks` tests; 1 assertion replaced as recorded) |
| Audit | `npm audit` | 14 moderate, all `minimatch` through the eslint toolchain (dev dependencies), pre-existing; lockfile untouched |
| Harness e2e, first pass | `npx playwright test -c playwright.harness.reuse.config.ts --workers=1` | 57 passed, 2 failed: both my new tests, both real findings (citation column at the text edge; phone scroll room), fixed as recorded under "What the gates caught" |
| Harness e2e, final code | same | **59 passed (1.1 min)**: the 57 pre-existing tests unedited plus the 2 new result-open tests |
| Geometry | `npx playwright test -c playwright.screens.config.ts e2e-screens/geometry.spec.ts --workers=1` | first full run: **3 passed, 2 failed** (the landmark tests: a null box on a present, visible element, see the `box()` note); second: **4 passed, 1 failed** (390 landmarks: the collapsed TL;DR line's face-dependent wrap, fixed with the two-line reserve); one run died in `beforeAll` on the API dev login (API-side, not the UI); **final code: 5 passed (1.0 min)** |
| Screens + axe, 4 looks x 2 widths (+ ui-kit x 12 looks) | `npx playwright test -c playwright.screens.config.ts e2e-screens/screens.spec.ts --workers=1` (previous set copied to a scratch folder first) | **18 passed (3.2 min), 0 axe violations** in all captures, 192 PNGs; then the eight lecture screens re-captured on the final code with `SCREENS=lecture-...`: **8 passed (1.3 min), 0 axe violations** |
| Real-stack e2e | `npx playwright test -c playwright.reuse.config.ts e2e --workers=1` | **23 passed (2.7 min)**: courses 11, **lecture 9**, syllabus 3; unedited; the lecture spec passed on this slice's layout (fake microphone, consent, pause / resume / stop, reconnect, mic denied, Quick recap in the new sheet, TL;DR) |

Not run: nothing from the brief's list. Not verified: screen-reader, real-device and human review (gate G4 NOT_RUN); the hover states (`.cite`, the pressed tertiary, the lecture primary's inner edge) are asserted as CSS and seen only in the ui-kit style of review, not captured; Low-stimulation and forced-colours renderings of the new notes and the ledger rail are axe-checked in Fog / Dusk captures but not viewed one by one in forced colours (slice 7 owns `emulateMedia`).

### Screenshots viewed (final set against the scratch copy of the before set; pixel-diffed in headless Chromium, every PNG)
- **Pixel diff, 192 PNGs:** consent (8), courses, course page, Add a course, palette, mic-denied and all 24 ui-kit captures are **byte-identical** (G1 held to the pixel on the consent screens). Dashboard and Preferences differ only in the run-specific display name (`Screens-<run>`, about 2,000 px in one 131 x 28 region and the Account line); sign-in Fog differs by 28 anti-aliased pixels on a hairline. Every lecture capture differs wholesale (the layout change) and most are shorter pages (the transcript no longer has a fixed height; the recap page at 1280 went from 1612 to 1056px tall).
- `lecture-live--1280--paper`: one 56px bar; the transcript sheet starts at the margin + 40 with its heading inside; timestamps hang on the container edge; the sheet ends under the last line; the rail is a quiet ledger (title, hint, three windows in one row with the usual filled and two-lined, then TL;DR as a serif line, plain bullets, "From 00:02" as an accent link). The transcript sheet and the rail share a top edge.
- `lecture-quick-recap--1280--paper`: the result sheet sits above the transcript in the same column with "Close recap" top right, coverage and the Demo summary stamp on one line, "Most recent" in the serif with its time in the margin, two hanging claims, "Also mentioned", and "Back to lecture" bottom right; the transcript's first line is at y 703 of an 800 viewport; the rail is unchanged beside it. The old rail column of 830px is gone.
- `lecture-paused--1280--paper`, `lecture-mismatch--1280--paper`, `lecture-stopped--1280--night`: notes with stripe, shape and a sans bold heading; the Demo-source panel under them is unchanged (two stripe styles stacked, as recorded); the Stopped toolbar starts on the sheet's surface edge. Mismatch's bar wraps to two rows at 1280 because of the Demo tag plus "Not available at the moment" (pre-existing, slice 1 record, G1).
- `lecture-focus--1280--paper`: the pressed Focus tertiary (check, bold, ink underline); the TL;DR note on the transcript track; the sheet with the margin; the dock on the column with the two-line usual label.
- `lecture-live--390--paper--viewport`: two-row bar (three in this capture: hidden scrollbars give 375px of layout, as slice 1 recorded); the transcript sheet is square and full-bleed with its 16px inset; the dock's usual button reads "Last 5 min / (your usual)" and "Last 10 min" holds one line (both wrapped mid-phrase before). Visible cost: with the page scrolled to the log, the collapsed TL;DR note shows only its stripe and the empty reserved second line under the bar; in Paper the fixture summary is one line, so the reserve is visible when the note is scrolled half under the bar.
- `lecture-quick-recap--390--paper--viewport`: the bottom sheet with title, windows, "Here is what was said" + Close recap, coverage, the stamp (its sentence wraps to the next line at this width), the serif "Most recent" with a hanging time, two claims; the transcript's first line stays above it.
- `lecture-disconnected--390--night--viewport`, `lecture-paused--390--paper--viewport`: pill and bar as before; the Paused divider inside the sheet; the banner / Paused note sit above the capture's scroll position (unchanged slot).

### Open for the Lead
1. **Reserved second line in the collapsed TL;DR note** (phone, Focus): keeps every skin's note the same height and stops a growing summary moving the transcript mid-lecture, at the cost of an empty line under a one-line summary. The alternative is the sans face for the collapsed preview (no face variance, no reserve) at the cost of PO-17's serif in that one place. Judgement call; one class either way.
2. **Demo summary tag as a Stamp** (deviation 3): confirm it is outside the G1 freeze, or say so and it reverts in one component.
3. **Timestamps left-aligned on the container edge** (deviation 4) versus right-aligned against the sheet: a one-declaration change once a student has looked at it (G4).
4. `box()` retry in `geometry.ts`: a null box on a present, visible `#lecture-quick-recap` within three round trips of load, reproducible in the landmark test and never in forty isolated probes. Noted for qa-engineer; the retry keeps the gate honest (a missing or invisible element still fails with count / visibility / url in the message).

### What the gates caught (fixed in this slice, kept here because the gate earned it)
- **Citation times at the text edge, not in the margin (1280).** The first harness run measured the recap's citation 145px right of the sheet's left edge: the hanging rows sit inside `.sheet-body`, which carries the 145px start padding at 1280. Fix: `.sheet-body .hang-row` steps back by `--hang-text` so its lead column is the margin track. Harness test now green.
- **Phone scroll room with the bottom sheet open.** With the transcript no longer a fixed-height box the page was too short to bring the lines under the bar while the 60dvh sheet covered them (document 1347px tall for an 844px viewport that needed 507px of travel). Fix: 60dvh of bottom padding under the columns while `#lecture-quick-recap[data-has-result]` is present below 1024 (`:has()`). The remaining miss was the test's own doing (it computed the scroll from the bar's pre-stick bottom); two passes fix that.
- **Landmark gate at 390: the collapsed TL;DR line follows the skin's heading face.** Gamer, Calm and Glass wrapped "Derivatives and the power rule." to two serif/mono lines where Paper used one, so the transcript sheet below the note sat 32px lower in those skins (the TL;DR landmark itself passed through the heading-line allowance; the landmark under it may not move). Fix: the collapsed line reserves two lines (`line-clamp-2` plus `min-h-16`), so the note is 64px of summary in every skin, and a summary revision that grows from one line to two no longer moves the transcript during a lecture (Principle 3). Cost: a one-line TL;DR shows an empty second line while collapsed; most real summaries fill both at 24px in a 322px column.

### Lead decisions on slice 3 (2026-10-31)
- **Demo summary tag (G1 edge):** reverted to its pre-redesign presentation (warning-coloured tag, same wording and test id). PO-17b keeps Demo-source presentation unchanged, so the Stamp treatment is not applied to it. `DemoSummaryTag` is a `<span>` so it nests in the stamp line; the half-step ratchet allows its one `px-2.5` in `QuickRecapPanel.tsx`. The slice 3 test asserting a `.stamp` child now asserts the tag text.
- **Open items from the agent's report, left open:** reserved second line under a one-line TL;DR in the collapsed note; timestamp alignment in the margin (G4 question); the geometry spec's one-retry on a null `boundingBox` (qa-engineer to look); Paused/Stopped screens still stack the 4px note stripe over the 6px Demo panel stripe (G1).
- **Verified by the Lead after the revert:** tsc clean; eslint 0 errors; vitest 2443 passed; real-stack e2e 23/23; reuse harness 59/59; geometry 5/5. Screens + axe (0 violations in 192 captures) is the agent's run, not re-run by the Lead.

## Slice 4 results (Today, course page, forms, sign-in)

Status: IMPLEMENTED in `src/web`, 2026-10-31, by frontend-engineer. Not committed. Every row of the verification record below is an actual run on the final code; nothing here is human-validated (gate G4 NOT_RUN) and gate G1 stays BLOCKED. Baseline before any change: `npx vitest run` 35 files, **2443 passed**. The previous screenshot set was copied to the session scratchpad (`before/`, 192 PNGs) before any change. Gate G1: `ConsentDialog.tsx`, `ConsentStart.tsx`, the Demo-source notice and tags are not touched; on the course page only the layout around the Lecture sentence changes, its wording is byte-identical.

### Recorded test changes (each with its reason)
- `e2e-screens/geometry.spec.ts`, shell checks for the Sheet and Auth templates: the selector `main .card` **replaced** by `main .sheet`, because the form pages no longer have a card (spec: "Add a course / Add syllabus: Sheet template"; 2.7 Sheet replaces `.card` for forms). Sheet pages also check that the h1 is on the column start (it now sits above the sheet) and that below 640 the sheet is full-bleed (column - 16) with the field on the column edge. The Auth check measures the 28rem block (h1 + sentence + sheet) for the 96px offset and centring, and the sheet for centring.
- `e2e-screens/geometry.spec.ts`, Today check (5) "card text inset = pad + 1 on the striped card": **replaced**, because the Next step card is gone (spec 5.3 "no rail, no cards"). New `marginChecks`: every margin label's ink starts on the container edge from 1024 (on the column below), stays clear of the column, and shares its first baseline with its section's body or action; ledger row text sits on the column edge. The same margin check runs on the course page.
- `e2e-screens/geometry.spec.ts` landmark set: the 12th landmark `card` (`main .card` on Today) **replaced** by `nextStep` (the Next step section), because Today has no card. The Calm allowance (card padding 32) now covers only the Add a course field, the one landmark still inside a padded sheet; `primary`, `card` and `main` lost the allowance (tighter: Calm may no longer move anything on Today, and does not).
- `e2e-screens/geometry.ts` `baseline()`: the zero-size probe is now inserted **before** the first text node instead of after it. Its doc comment already promised "the first text line's alphabetic baseline"; after the node, a wrapped text node reported its last line. No existing caller measured wrapped text (headings and button labels on one line), so their values are unchanged; the margin labels wrap in the 5rem column ("Saturday, / October 31") and exposed it.
- Added, no existing assertion changed: `DashboardView.test.tsx` (3: ledger rows with one link each and no "No lectures yet"; one filled button, margin labels, disclosure after the list; calm loading / error note), `CourseDetailView.test.tsx` (1: margin labels, one filled button, Syllabus ledger row, unchanged Lecture sentence, axe), `CourseForm.test.tsx` (1: h1 above the sheet names the form; note, not panel), new `SignInView.test.tsx` (1, with axe), new `slice4Copy.test.tsx` (12: the language-guide lint over every rendered string of Today, the course page and the form, and the literals of All courses and sign-in, which had no copy lint before), `skinBlocks.test.ts` (3 tests: the Desk section CSS contract incl. the label's negative end margin, the page sheet, no card / panel / eyebrow left in the nine slice-4 files).

### What the gates caught
- **Landmark gate at 1280: a wrapped margin label grew its row by the heading face.** The date ("Saturday, / October 31") and, in Gamer's monospace, "Your courses" wrap in the 5rem margin; baseline-aligned in a grid row, their second line hung below the h1 / action and grew the row by 3 to 20px depending on the face (main 817 in Paper, 820 to 837 elsewhere). Fix: from 1024 a margin label carries `margin-block-end: -3rem`, so it no longer sizes its row (a grid item contributes its margin box) and its extra lines hang beside what follows. Landmarks equal again in all 10 looks.

### What landed (`src/web`: `DashboardView.tsx`, `DashboardPage.tsx`, `CourseCard.tsx`, `AllCoursesPage.tsx`, `CourseDetailView.tsx`, `CourseDetailPage.tsx`, `CourseForm.tsx`, `SignInView.tsx`, `syllabus/SyllabusCard.tsx`, `globals.css`, `lib/design/tokens.ts`)
- **Desk margin filled** (`.desk-section` / `.desk-label` / `.desk-action` / `.desk-body`, grid areas, DOM order = reading order). Below 1024 the label sits above its content (the action, when there is one, on the label's row, same baseline). From 1024, inside `main[data-template="desk"]`, the section steps back by the margin column plus its 2rem gap: the label's ink starts on the container edge (where the lecture timestamps hang), keeps 16px clear of the column, shares its first baseline with the body (or with the action), and does not size its row (see "What the gates caught"). Labels hung: the date beside `Hi, {name}.`, "Next step", "Your courses" (Today); "Lecture", "Syllabus" (course page). All `.label` (sans 15/24 muted), all still `h2` where they were headings.
- **Today** (wireframe 5.3): no cards, no aside column. Greeting in `text-display` (44/48, 34/40 on phones). Next step is a plain block: the basis line ("Most recently added"), the course name in the serif xl, the sentence, the one filled button, "Why this?". "Your courses" + "Add a course" on one row, then the courses as a `.ledger` of rows; "See all courses" under it; "How a lecture works" moved from the right column to under the list as an unboxed disclosure (chevron + link-style summary, numerals muted, still open until the first lecture and remembered per device). Loading: the greeting row ("Your dashboard", frozen) and "Getting your courses." (the boxed static placeholder is gone). Error: a `note-notice` with the message, the "Nothing was deleted" sentence and one Try again.
- **Course rows** (`CourseCard`, used on Today and All courses): `li.ledger-row`, the course name is the row's one link (`.ledger-row-link`, stretched over the row, one tab stop, name = course name), meta beside it (code, term, and "Last lecture: Oct 27" only when there is one: **"No lectures yet" is gone**), a visual-only "Open" + chevron (`aria-hidden`). `flex-nowrap` so "Open" stays on the name's line on phones and the meta wraps under the name.
- **All courses**: h1 in `text-display` with "Add a course" on its baseline; search field narrowed to `max-w-narrow` (28rem, was Tailwind's `max-w-md`, same width); the list as a ledger; the empty sentence is now the language guide's row 14, "No courses yet. Add one when you want to."; error as a note.
- **Course page**: no cards. Back link, h1 in `text-display`, meta, the success line; "Lecture" section (its sentence byte-identical and still muted, the one filled button "Start a lecture"); "Syllabus" section as one ledger row: the sentence, then the action as the row's one link with a chevron (same link names: "Add your syllabus", "See progress", "Review items", ...); "Back to dashboard" under it. Not found / could not load: h1 in `text-display`, unchanged otherwise.
- **Add a course** (Sheet template): the h1 and its sentence on the canvas above the sheet; the form is the one `.sheet` (`aria-labelledby` still the h1, so the form keeps the name "Add a course"); error summary, restored-draft line and save error are notes (`note-danger`, `note-neutral`, `note-notice`) instead of panels.
- **Sign-in** (Auth template): the same shape: h1 and the development sentence on the canvas, the sheet holds the Email field and the button. Still 28rem centred, 96px from the top.
- **`.sheet-page`** (new modifier): below 640 the page's one sheet is full-bleed (-16px) with 16px inline padding, so the field's edge equals the h1's edge (R8, review A12). `.sheet-bleed` is untouched (its declarations are pinned).
- **Contrast**: five named pairs added to `PRIMITIVE_PAIRS` for what these screens draw beyond the primitives (focus ring on the focused row's sunken tint, the row chevron on ledger and on the hover tint, the form's danger summary title on the sheet, the success line on canvas); all pass in all 12 palettes (+60 assertions). No colour value changed.

### Deviations from the spec, with reasons
1. **"Your courses" is a margin label, not a serif 2xl h2.** 2.1's table names "Your courses" as the 2xl example; wireframe 5.3, written for this screen, hangs it in the margin beside "Next step". I followed the wireframe so Today has one label voice and one serif h1. It stays an `h2` with the frozen name. [J]
2. **The basis line "Most recently added / used" is kept** (the wireframe drops it). It was review S5's fix so the next step does not read as a recommendation the app cannot back. It sits on the margin label's baseline, so "Next step" and the basis read as one line of labels. [J]
3. **"Why this?" sits under the button, not beside it** (the wireframe draws them on one line). A native disclosure beside a button would open its text inside a narrow flex item; under the button it opens across the column.
4. **All courses has no margin label.** The spec names none for it, and its h1 already says what the list is; a "Courses" label beside "All courses" would repeat it. The margin there is an indent, as before.
5. **Add syllabus (`syllabus/CreateForm.tsx`) is not moved to the Sheet template.** The brief scoped slice 4 to Today, All courses, the course page, the course form and sign-in; the syllabus routes are slice 6's, and the Add syllabus form holds the Draft notice checkbox (G1).
6. **The course create form only.** There is no course edit form in the app; nothing to restyle.
7. **The h1 on the Sheet and Auth pages stays `text-3xl`** (38/44); `text-display` (44/48) is used on the Desk pages (Today, course page, All courses, the course-not-found page), per 2.1's "display: h1 on Today, course page, consent, syllabus review". Consent is G1 and untouched.
8. **The primary's hover is not changed product-wide** (slice 3 left the decision to this slice): the lecture-scoped inner edge stays lecture-only, because a product-wide `.btn-primary:hover` rule would also restyle the consent page's primary (G1). The underline hover remains elsewhere.

### Test and verification record (actual runs, 2026-10-31, `src/web`; the clean `npm run dev:real` already on :3000, the TCP forwarder on :3100, the API on :5080; no second `next dev` was started and the plain harness config was not used)
| Check | Command | Result |
|---|---|---|
| Unit, before any change | `npx vitest run` | 35 files, **2443 passed** (= baseline) |
| Typecheck | `npx tsc --noEmit` | clean (after every change; last on the final code) |
| Lint | `npx eslint src e2e e2e-harness e2e-screens` | 0 errors, 1 pre-existing warning (`_label` in `e2e/lecture.spec.ts`) |
| Unit, final code | `npx vitest run` | 37 files, **2524 passed**, 0 failed (+81: 60 contrast pairs, 12 copy lint, 3 skinBlocks, 3 DashboardView, 1 CourseDetailView, 1 CourseForm, 1 SignInView); no existing unit assertion edited |
| Audit | `npm audit` | 14 moderate, all `minimatch` through the eslint toolchain (dev dependencies), pre-existing; lockfile untouched |
| Geometry, first run | `npx playwright test -c playwright.screens.config.ts e2e-screens/geometry.spec.ts --workers=1` | 3 passed, 2 failed: the landmark gate at 1280 (wrapped margin labels growing their row by face) and the Paper shell test (the margin baseline check reading the last line of a wrapped label: the `baseline()` helper fix). Both recorded above |
| Geometry, final code | same | **5 passed (1.0 min)** |
| Screens + axe, 4 looks x 2 widths (+ ui-kit x 12 looks) | `npx playwright test -c playwright.screens.config.ts e2e-screens/screens.spec.ts --workers=1` (previous set copied to the scratchpad first) | **18 passed (3.3 min)**; `axe-report.json`: **160 captures, 0 violations**; 192 PNGs |
| Harness e2e | `npx playwright test -c playwright.harness.reuse.config.ts --workers=1` | **59 passed (1.1 min)**, unedited |
| Real-stack e2e | `npx playwright test -c playwright.reuse.config.ts e2e --workers=1` | **23 passed (2.8 min)**: courses 11, lecture 9, syllabus 3; unedited (frozen strings, ids and test ids all held) |

Not verified: screen-reader, real-device and human review (gate G4 NOT_RUN); the ledger row hover and focus tint seen only as CSS (no hover capture); the `forced-colors` rendering of the new sections (slice 7 owns `emulateMedia`); the skin x mode looks other than the four captured looks are covered for these screens by the landmark gate only, not viewed.

### Screenshots viewed (new set against the scratch copy of the before set; byte and pixel compared)
- **Byte compare, 192 PNGs:** 114 identical. Changed: dashboard (8), dashboard-empty (8), courses (8), course-detail (8), course-new (8), signin (8): this slice. palette (8) and preferences (8): they open over Today, so the page behind them changed. lecture-quick-recap (12): **not this slice**: the Demo summary tag is back to its pre-redesign look (the Lead's revert after slice 3's capture, commit c46e9b9), confirmed by eye at 390. lecture-mic-denied 1280 fog / dusk: 25 and 38 pixels differing by 1 to 2 levels on rounded corners (rendering noise). Consent captures: byte-identical (G1).
- `dashboard--1280--paper` / `--night`: date wrapped in the margin ("Saturday, / October 31") beside the 44px greeting; "Next step" on the basis line's baseline; serif course name, one filled button, "Why this?"; "Your courses" with "Add a course" on the column's right edge; three ledger rows with name, meta and "Open >" on one line; "How a lecture works" open under the list (the test user has no lecture). No boxes. Night keeps the hierarchy; the ledger tint is a quiet step above canvas.
- `dashboard--390--paper`: labels stacked above their content; ledger full-bleed. The first capture showed "Open >" dropping under "History of Law"; fixed with `flex-nowrap` (the meta now wraps under the name); `courses--390--paper` confirms.
- `dashboard-empty--1280--paper`: margin date, "Next step" beside "Add your first course to get started.", the one primary, "Why this?", the open disclosure.
- `course-detail--1280--paper` / `--390--paper`: back link, display h1, meta, the green success line; "Lecture" in the margin beside the unchanged sentence and "Start a lecture"; "Syllabus" beside one ledger row with "Add your syllabus >" at its end (under the sentence on phones); "Back to dashboard".
- `course-new--1280--paper` / `--390--fog`: h1 and sentence on the canvas, the sheet holds the field, Show options, Save course / Cancel; on the phone the sheet is square and full-bleed with the field on the 16px gutter.
- `signin--1280--paper` / `--390--paper`: same shape, centred 28rem block 96px down; full-bleed sheet on the phone.
- `courses--1280--paper`: display h1 with "Add a course" on its baseline, the 28rem search field, the ledger, "Back to dashboard". The margin column is an indent here (deviation 4).

### Open for the Lead
1. Deviation 1 ("Your courses" as a margin label, following the wireframe over 2.1's table) and deviation 2 (basis line kept): judgement calls, one class each to reverse.
2. The ledger tint bleeds 16px past the column on both sides (slice 2's R8 reading), so on Today and All courses its right edge sits 16px beyond "Add a course" and the header's last action. Consistent with the ui-kit; say if the right side should stop at the column.
3. Add syllabus on the Sheet template is left for slice 6 (deviation 5).

### Lead decisions on slice 4 (2026-10-31)
Deviations 1 and 2 (margin label "Your courses"; "Most recently added" line kept) accepted. The ledger keeps its 16px outward bleed (R8) for now. Add syllabus on the Sheet template stays for slice 6. Verified by the Lead on the final code: tsc clean; eslint 0 errors; vitest 2524; real-stack e2e 23/23; reuse harness 59/59; geometry 5/5; ConsentDialog/ConsentStart/Notices have no diff. Screens + axe (0 violations in 160 captures) is the agent's run.
