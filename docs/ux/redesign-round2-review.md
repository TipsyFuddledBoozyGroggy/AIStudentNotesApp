# Tether redesign round 2: design review of slices 0 to 5 and verdicts on the open questions

Status: AI SPECIALIST DESIGN, 2026-10-31. Author: adhd-ux-specialist (design lead for round 2). Audience: Lead Architect, Product Owner, frontend-engineer, qa-engineer.
**Nothing here is human-validated (gate G4 NOT_RUN).** No student, clinician, screen-reader or real-device evidence exists for any verdict below; every call is design judgement [J] unless tagged [C] (project constraint) or [E] (standard knowledge, not re-fetched).
**Gate G1 (BLOCKED, PO-17b):** consent, recording and Demo-source wording *and presentation* are unchanged by this review. Where the fix for a finding lives on the G1 side I say "recommend only" and the item waits for the Product Owner and counsel. I touched no source file; the only file written is this one.

What I looked at: `docs/ux/redesign-round2.md` in full (direction, every "Slice N results" section, every "Lead decisions" section); the committed captures in `src/web/test-results/redesign-screens/` named in each verdict (read as images, not re-generated; no browser, dev server or test was run); `src/web/src/components/prefsCopy.ts`, `PreferencesPanel.tsx` (hint prop), `ThemePicker.test.tsx` (the string pins), `lib/copyLint.ts` (`FORBIDDEN`), `syllabus/ItemRow.tsx`, `syllabus/OneAtATime.tsx`, `syllabus/copy.ts` (`ITEM_COPY`), `lib/syllabus/client.ts` / `requests.ts` (the `acknowledgeCheck` flag), `lecture/TldrPanel.tsx` (the two-line reserve), `app/globals.css` (`.note`, `.panel`, `.transcript-time`, the 1280 lecture margin rules), and `docs/ux/themes-spec.md` section 3.2 (the pinned strings) and `docs/ux/language-guide.md`.

Priority marks: **must** = truth, accessibility, lint or consistency failure, or a thing slices 6 to 9 would otherwise build wrong; **should** = my design decision, do it unless a cost appears; **nice** = optional polish.

---

## 0. Verdicts at a glance

| # | Question | Verdict | Mark |
|---|---|---|---|
| 1.1 | Collapsed TL;DR note reserves a second line | **Keep the reserve.** Stability of the transcript beats an empty line. Shrink the cost on phones (20/28 in the collapsed note below 640). Do not switch the preview to the sans. | should |
| 1.2 | Lecture margin timestamps: left or right | **Right-align** timestamps and citation times against the sheet (`text-align: end` in the margin track from 1280). Desk labels stay left. Rule: *labels start on the page edge; data that belongs to a line hugs the line.* | should |
| 1.3 | Paused/Stopped 4px note stripe over the Demo panel's 6px stripe | Note-only fix: **24px gap** between a safety note and the Demo panel, and **state-tint fill** on the three safety note variants (notice, safety, danger), not on neutral. The Demo panel itself: recommend only (G1). | should |
| 1.4 | Mismatch bar wraps to two rows at 1280 | **Accept the wrap for now**; it is a safety state and the Demo tag is G1. Non-G1 lever if wanted: the disabled Resume's reason becomes `aria-describedby` only while the Mismatch note is shown (the note carries the reason and the next action). | nice |
| 2.1 | "Your courses" as margin label vs serif 2xl h2 | **Margin label, as built.** Serif stays with synthesis and page titles. Amend 2.1's table example. | should (keep) |
| 2.2 | Ledger 16px outward bleed | **Keep the left bleed, stop the right edge at the column.** The container's right edge is the one hard edge on the page; nothing else crosses it. Phone stays full-bleed. | should |
| 3.1 | Theme descriptions (all five) | **Replace all five** with strings that name paint only (section 3.1); add one panel sentence that says what a theme does and does not change. Sync `themes-spec.md` 3.2 and `ThemePicker.test.tsx` in the same change. | must |
| 3.2 | Low-stimulation hint | **Replace**: the current string contains "just" (fails `FORBIDDEN` if it were in the registry; it is a prop literal, so it is not linted) and "softer colours" is true on Paper only. Move it into `PREFS_COPY`. | must |
| 3.3 | Preferences side panel: reflow (built) or overlay | **Reflow, as built.** Verify the reflow is instant (no width/padding transition). Accept two header rows at 1024 to 1279 while open. | should (keep) / must (verify) |
| 4 | Separate acknowledgement step for "Please check" approvals | **No separate step.** The automatic `acknowledgeCheck` with the hint as the button's description is the right ADHD/low-pressure design. Two tightenings: source quote before the hint and the actions (slice 6 row order); no *filled* Approve for a "Please check" item in one-at-a-time. | must (no step) / should (tightenings) |
| 5 | Overall match and the three biggest gaps | Built result matches the direction closely where it was allowed to (section 5). Gaps: syllabus review (slice 6, unbuilt); the Demo-source presentation on lecture screens (G1, PO Q2); the safety-state screens as a set. | see 5 |

---

## 1. Slice 3: lecture screen

### 1.1 The reserved second line under a one-line TL;DR (collapsed note, phone and Focus)

Seen in `lecture-focus--1280--paper.png` (the note "TL;DR / Derivatives and the power rule." with about 40px of empty surface under the line, "More" top right) and `lecture-live--390--paper--viewport.png` (the note half under the bar: stripe and empty area at the top of the viewport). Code: `TldrPanel.tsx:72` `line-clamp-2 min-h-16`.

**Verdict: keep the reserve. Mark: should.**

Why, in ADHD cognitive-load terms [J]:
- A summary revision that grows from one line to two would move the transcript sheet down 32px mid-lecture. That is a change the student did not make, directly above the text they are reading. It costs a re-fixation and a "what changed?" check every time it happens, and it happens on exactly the surfaces (Focus, phone) a student chose because they wanted fewer things to happen. Principle 3 (nothing moves unless the student moved it) outranks whitespace.
- An empty line under a short sentence is read as space, not as missing content; it is only visible while the note is in view and the summary is short, and on Paper the fixture summary is unusually short. Most real one-line summaries at 24px wrap in a 322px column anyway (the agent's record says the same).
- Switching the collapsed preview to the sans would remove face variance and the reserve, but it would also remove the one place in Focus mode where the serif marks "this is the app's synthesis, not the lecturer's words". PO-17's rule earns its keep precisely there. Do not do this.

Refinements:
- **should**: below 640, set the collapsed synthesis line at 20/28 (`text-lg`) with a 56px reserve (`min-h-14`) instead of 24/32 + 64px. More summaries fill both lines on a phone, the blank is 8px smaller, and the landmark gate is unaffected (it compares looks, not widths). From 640 keep 24/32 + 64.
- **nice**: if G4 participants call the gap a bug, the fallback is `line-clamp-1` + `min-h-8` (a one-line preview by definition; "More" opens the rest). Keep that in the G4 question list rather than building it now.

### 1.2 Timestamp alignment in the lecture margin

Seen in `lecture-live--1280--paper.png`: times at the container edge (x 57), words at x 202; about 105px between a time and its line. `lecture-quick-recap--1280--paper.png`: citation times likewise on the container edge, ~105px from the claims they cite. CSS: the 1280 block at `globals.css:1154-1201` (`--hang-gap` = 2.5rem + 1.5rem + 1px = 65px; the margin track is 5rem).

**Verdict: right-align times and citation times against the sheet. Mark: should.** One declaration block at 1280 (frontend confirms selectors):

```css
@media (min-width: 1280px) {
  [data-lecture-root] .transcript-time,
  [data-lecture-root] .hang-row > :first-child { text-align: end; justify-self: end; }
}
```

Reasoning [J]:
1. **Proximity is the binding.** A timestamp is data that belongs to one line; the only thing that tells the eye which line is distance. At 105px the time sits further from its line than the lines sit from each other (41px pitch with the paragraph gap). Right-aligning brings the time to 65px from the words with no other change, and a column of tabular `HH:MM` is the same width on every row, so the left edge stays straight either way; alignment only changes where the column sits.
2. **Scan-back after a recap.** The student reads "Most recent: ... 00:09" in the recap, then looks for 00:09 in the transcript. Scanning a time column that touches the text block is a single vertical sweep; scanning one that floats 105px away and then jumping right is two moves per candidate line. Citation times are also links with a 44px hit area; nearer the claim is the natural "read, then press" path.
3. **Return after interruption.** Either alignment gives the eye a fixed left landmark. Right-aligned, the landmark is the sheet's edge with the time flush to it, which is also the landmark below 1280 (times inside the sheet). One mental model across widths.
4. **Why Desk labels stay left.** "Next step", "Your courses", the date and "Lecture" are headings for a block, not data for a line; they start where a book's shoulder head starts (the page edge). Marginal line numbers in verse and legal texts are set flush to the text block for the same proximity reason [E, typographic convention]. The two jobs justify two alignments; the rule to write down is: **labels start on the page edge; data that belongs to a line hugs the line.**

Consequences to record: `e2e-screens/geometry.spec.ts` currently asserts "the first timestamp on the column's left edge" at 1280 (slice 3 record); that assertion changes to "the first timestamp's ink ends at margin + 5rem" in the same commit. Gamer's monospace times are wider; assert the time's ink stays inside the margin track in every skin (slice 8).

**nice**: close the remaining gap further. Let the lead column run to 12px short of the sheet border (lead column `calc(var(--margin-col) + 2.5rem - 0.75rem)`, column gap `calc(0.75rem + 1.5rem + 1px)`): time ink ends 37px from the words. Only after the right-align has been looked at by a student (G4).

### 1.3 Paused / Stopped: a 4px note stripe stacked over the Demo panel's 6px stripe

Seen in `lecture-paused--1280--paper.png` (amber 4px stripe on the Paused note, 16px below it the Demo panel's amber 6px stripe plus 1px border and radius) and `lecture-stopped--1280--night.png` (grey 4px stripe over the amber 6px one). CSS: `.note::before` width `--note-stripe-w` 4px, no border; `.panel` `border-left-width: 6px` plus a full border.

G1 constrains the Demo panel, so only the note can move. Two stripes of one colour and two widths 16px apart read as a rendering inconsistency, not as two notices.

**Verdict, note-only. Mark: should.**
1. **24px between a safety note and the panel under it** (related units, rhythm 2.3), not 16. The Demo panel keeps its slot and its own look (G1); only the note's bottom margin grows.
2. **State-tint fill on the three safety variants**: `.note-notice` fills `warning-subtle`, `.note-safety` fills `recording-subtle`, `.note-danger` fills `danger-subtle`; `.note-neutral` (Stopped, TL;DR, audio notes) stays unfilled. The stripe stays (shape + word + stripe is the safety triple, unchanged). The pill already pairs a state tint with its state colour, so the Paused note becomes "the pill's language, larger", and the Demo panel stays "a white box with a stripe": different kinds, so they no longer compete. This holds whichever way the PO decides Q2 (if the Demo notice later becomes a 4px Note too, the fill is what keeps the two apart). Contrast: `ink` and `muted` on each `*-subtle` and the stripe colour on its own subtle go into `PRIMITIVE_PAIRS` and must pass in all 12 palettes before this lands; if a pair fails, drop the fill for that variant rather than changing a token. Low-stimulation must not strip these fills (3.3 item 6: safety surfaces never reduced).
3. Not the stripe width: making the note 6px to match would produce one broken-looking stripe; making it 2px would weaken the safety cue.

If the fill is judged too strong after a look, the 24px gap alone is the acceptable minimum.

**Recommend only (G1, presentation, wording untouched):** the Demo panel in its collapsed form (heading + "Details") rendered as a Note (4px stripe, no outer border) so the two notices speak one visual language; the Mismatch body sentence still repeats its heading word for word ("The app and your microphone disagree" twice, `lecture-mismatch--1280--paper.png`); both wait for the PO and counsel.

### 1.4 The Mismatch bar wraps to two rows at 1280

Seen in `lecture-mismatch--1280--paper.png`: pill "Not recording 0 min" (wider than the Recording pill), Connected, Resume (disabled, with the reason "Not available at the moment" as loose text beside it), Stop, Focus, Lecture settings; the Demo tag drops to a second row. Without the ~190px of reason text the row fits at 1152 (rough sum of the ink widths plus gaps, about 1,100px); the tag is G1.

**Verdict: accept the wrap for now. Mark: nice (the lever below).**
- It is a safety state; the screen already changes (red note, filled "Check again"). A second bar row of 42px in a state where the transcript is not growing costs little, and the Pause/Resume and Stop positions do not change (3.3 item 3 holds, checked in the capture).
- Non-G1 lever if the Lead wants one row: while a safety note is displayed under the bar, the disabled control's reason is exposed as `aria-describedby` on the button and **not** rendered as visible bar text; the note directly below states the reason and the next action in full ("so the microphone is off ... Choose Check again."). A sighted keyboard user who tabs to the disabled Resume sees the red note 60px below; a screen-reader user hears the description. Spec 2.7 Disabled ("reason text *or* description") is satisfied. Do not apply this in states without a note under the bar.
- When the PO decides Q2 (tag off the bar on desktop) the wrap disappears on its own; do not spend more on it before then.

---

## 2. Slice 4: Today, course page, forms

### 2.1 "Your courses" as a margin label, not a serif 2xl heading

Seen in `dashboard--1280--paper.png`: "Your courses" sm muted in the margin, "Add a course" on its baseline at the column's right edge, the ledger below; the serif voice on the page is "Hi, {name}." and the course name under Next step.

**Verdict: keep the margin label (the Lead's acceptance stands). Mark: should (keep).**
- "Your courses" is a wayfinding label, not synthesis and not a page title. Under PO-17 the serif marks what the app or the lecture concluded; a serif 2xl "Your courses" would be a third serif voice competing with the h1 and the Next step name for no meaning gain.
- It stays an `h2` with the frozen name (confirmed in the slice 4 record), so heading navigation is unchanged.
- Amend 2.1's table: the 2xl row's example becomes the syllabus week head ("Week of Sep 14 to 20") and section heads on long reading pages; Desk section labels are margin labels. I will fold that into the direction document when the Lead asks for the next revision; no code follows from it.

### 2.2 The ledger's 16px outward bleed

Seen in `dashboard--1280--paper.png` and `courses--1280--paper.png`: ledger tint from x 152 to 1225; the column starts at 168; "Add a course", "Sign out" and every "Open >" end at 1208. `course-detail--1280--paper.png`: the Syllabus row's tint ends 16px past "Add your syllabus >". `preferences--1280--portal-light.png`: in Portal the ledger is a white card, so the overshoot is a card edge, not a tint. `dashboard--1280--calm-light.png`: rounded ends, same overshoot.

**Verdict: keep the left bleed, stop the right edge at the column. Mark: should.**
- The left bleed does its job: row text sits on the column edge (R8), and the tint stepping 16px into the margin reads as the text block of a book page, which the margin labels already imply.
- On the right there is nothing to bleed into. The container's right edge is the one hard edge on every screen (header actions, "Add a course", "Close"); the ledger is the only element in the app that crosses it, and it does so on every list. One exception per screen is one more edge the eye has to classify (section 2.0 of the direction: fewer edges, fewer decisions).
- Consequence: the trailing "Open >" ends 16px inside the tint (at 1192), which is an ordinary list-row inset; in Portal the list card gets a symmetric 16px inner inset on both sides, which is what a list card is expected to look like.
- Phone (`dashboard--390--paper.png`): full-bleed both sides, unchanged.
- Implementation hint (frontend confirms): `.ledger { margin-inline: -1rem 0; width: calc(100% + 1rem); }` from 1024 inside Desk; `.ledger-row-end` keeps its 16px end padding; the ui-kit sample and `geometry.spec.ts` "ledger row text sits on the column edge" are unchanged; add "ledger box right edge = container right".

---

## 3. Slice 5: Preferences and themes

### 3.1 Theme descriptions: replace all five

Problem: a theme is paint only (colour, radius, shadow, heading face, header chrome, decoration; the landmark gate proves nothing moves), yet "Course portal" promises "a familiar course-site layout" and "plain cards" on screens that no longer have cards; "Gamer" says "a dark first feel" although Colour mode is a separate control and Gamer light exists; "Glass" says "a frosted top bar" without saying it is solid below 1024 and during a lecture. Truth over comfort (language guide principle 8).

Constraints checked against: `lib/copyLint.ts` `FORBIDDEN` (no "!", no "just", no "behind", no "streak", no "right now", ...); `ThemePicker.test.tsx:195` (no focus / attention / concentrat / calm(s|ing) you / help(s) / recommended / best / treat / adhd / symptom); no "canvas"; sentences under 20 words; themes-spec 3.2 "no clinical words, no 'recommended'".

**Replacement strings (exact; `prefsCopy.ts` `THEMES[].description`). Mark: must.**

| id | Current | Replacement |
|---|---|---|
| paper | Warm paper and ink. The default. | `Cream page, dark ink, serif headings. The default.` |
| portal | A familiar course-site layout: dark top bar, light page, plain cards. | `The look of a course website: dark top bar, light grey page, small corners.` |
| gamer | Technical look with sharp edges and a dark first feel. No game mechanics. | `Sharp corners and monospace headings. No game mechanics.` |
| calm | Quiet colours, soft shapes, extra room. | `Muted green and grey, rounded shapes, no shadows.` |
| glass | A frosted top bar and soft layers. Text always sits on solid panels. | `Frosted top bar on wide screens, soft gradient page. Text and the lecture always sit on solid surfaces.` |

Notes on each: Paper names its three visible facts. Portal keeps the familiarity cue ("the look of") without claiming layout or cards; "small corners" is the radius change a student can see. Gamer drops "dark first" (Colour mode is separate) and keeps the promise that matters ("No game mechanics"); when slice 7 ships the Grid texture switch, the first sentence may become `Sharp corners, monospace headings, an optional faint grid.` and not before. Calm drops "extra room" because the extra padding applies at Comfortable density only and never to the lecture sheet. Glass says where the frost is and where it is not; "always" is backed by the backdrop-filter DOM scan.

**One sentence above the cards (new `PREFS_COPY.themeHint`, rendered as the radiogroup's `aria-describedby`). Mark: should.**
`A theme changes colours, corners and heading type. Layout, words and controls stay the same.`
This answers the "does it change the layout?" question once, for every card, and is true by the landmark gate.

**Coupled edits (same change, or the suite goes red):** `docs/ux/themes-spec.md:202` must carry the new strings in quotes (`ThemePicker.test.tsx:197-199` asserts the spec file contains each description); `ThemePicker.test.tsx:59-61` pins the Portal description literally and changes with it. The test name "the spec's strings word for word" stays truthful once the spec is updated. I did not edit either file (brief: write only this review); the Lead assigns the two-file change to frontend-engineer with this table as the acceptance text.

### 3.2 Low-stimulation hint: replace and move into the registry

Current (`PreferencesPanel.tsx:58`, a prop literal, outside `PREFS_COPY`, so the copy lint never sees it): "Softer colours, no shadows, no movement. Recording status stays just as clear."
- "just as clear" matches `FORBIDDEN` "chirpy filler" `\bjust\b(?! now)`. It passes today only because the string is not in the linted registry.
- "Softer colours" is true on Paper (Fog / Dusk swap) and false on the other four skins, where the overlay strips shadow, gradient, texture, blur, ticks and transitions but keeps the palette (themes-spec 3.4). The spec's own string (3.2) names texture and blur, which exist only in Gamer and Glass. Neither string is true everywhere.

**Replacement (exact). Mark: must.**
`Removes shadows, texture, gradients and movement. On Paper the colours soften too. Recording status is never dimmed.`
Three sentences, 17 words, every clause true on every skin; the recording sentence states the safety invariant (3.3 item 6) without "just". Put it in `PREFS_COPY.lowStimHint` and pass it as the prop, so `ThemePicker.test.tsx` "copy" lints it; update `themes-spec.md:204` to the same string.

**Slice 7 heads-up, same class of problem. Mark: must before slice 7 builds it.** The spec's Grid texture hint "Adds a faint grid behind the page. Never behind lecture text." contains "behind" twice, which `FORBIDDEN` "guilt or pressure" `\b(behind|overdue|missed|streak)\b` rejects. Replacement: `Adds a faint grid under the page. Never under lecture text.` The Solid surfaces hint "Replaces the frosted bar with a solid one. Your device setting is respected too." passes as written.

### 3.3 Side panel: reflow (built) or overlay

Seen in `preferences--1280--paper.png` and `--portal-light.png`: panel 24rem on the right, Today reflowed to a 768px column, every row's "Open >" and "Add a course" visible; `preferences--390--calm-dark.png`: in-flow sheet under the header, Today below it.

**Verdict: reflow, as built (the Lead's decision stands). Mark: should (keep); must (verify the reflow is instant).**
- The reason to have a side panel at all was "see the page you are restyling". An overlay at 1280 hides the right third of Today, including the controls a student would look at to judge a theme (row ends, the primary's neighbours). Reflow keeps the whole page in view, which is the slice's acceptance.
- It is student-initiated and reversed on Close, so Principle 3 holds; it never runs on lecture routes (asserted).
- **must verify:** the `padding-inline-end` change is not transitioned (the 2.6 property allow-list has no width or padding, so a transition would already violate it; one computed-style assertion in `skins.spec.ts` closes it).
- 1024 to 1279: the header wraps to two rows while the panel is open. Accept; one behaviour across desktop widths is worth more than a special case, and the state is temporary.
- Keyboard and screen reader: region, no trap, focus to the heading on open, Escape and Close return focus to the Preferences button: correct for a non-modal panel. Not human-tested (G4).

---

## 4. The approve flow for "Please check" items

What is built (read, not run): `ItemRow.tsx:61` and `OneAtATime.tsx:84` send `acknowledgeCheck: true` whenever an item whose confidence is `please_check` is approved; the server requires it (`client.ts:55`); the Approve button carries `aria-describedby` to the hint `"Approving this means you checked the date against your syllabus."` (`copy.ts:173`), which is visible under the row (list) and above the buttons (one-at-a-time); the source quote opens by default for `please_check` (`ItemRow.tsx:43`); the "Please check" word has a question-mark glyph; the confidence reason is shown; Approve becomes Undo in place and Undo never expires; nothing reaches Google Calendar without the separate Calendar step, which lists approved items and asks again (invariant 4).

**Verdict: no separate acknowledgement step. Mark: must (do not add one).**
Reasoning [J]:
- An extra step (checkbox, confirm dialog, second press) adds a decision and an interruption to a task that repeats 6 to 24 times. Repeated confirmations train a click-through habit within the first few items, after which the acknowledgement is theatre and the friction remains. For a student with limited attention budget that is the worst trade: cost every time, protection never.
- The thing the acknowledgement is supposed to secure, that the student *looked at the date*, is secured by presentation, not by a gate: the source line is open by default, the confidence word has a shape, the reason is stated, and the hint is attached to the button a screen reader will read. That is the "explicit next action" pattern, not a gate.
- The stakes are low and reversible: Undo in place, no calendar write until a separate reviewed confirm, and the item can be edited. Low stakes plus a reversible action is exactly where the student-experience rules say not to interrupt.
- A modal here would also be the only confirm in the app whose purpose is to make the student attest to something, which is the pattern the consent pop-up uses for a legal reason. Spending it on a date would dilute the one place it matters.

The current wording is enough; it describes what approval records in plain words and does not instruct. Keep `checkHint` as is (no sentence rewrite needed).

**Two tightenings. Mark: should.**
1. **Order inside a "Please check" row (slice 6 ledger row).** Today in the list the reading order is title, meta, "Please check", reason, [Approve Skip Edit], hint, then the source quote: the thing the student is asked to check comes last, after the buttons. Make the order: title and date; "Please check" + reason; the source quote (open); the hint; the actions. One-at-a-time already has this order (`OneAtATime.tsx:155-200`); the list should match. Put it in slice 6's acceptance criteria.
2. **No filled Approve for a "Please check" item in one-at-a-time.** `OneAtATime.tsx:190` fills Approve for every item. For an item the app itself marked uncertain, a filled Approve pulls the eye toward approving before checking and quietly contradicts invariant 6 (an estimate is not certainty). For `please_check` only, render Approve as a secondary (same name, same position, same `aria-describedby`) so the strongest thing in the card is the source quote. "Looks right" and "Probably right" keep the filled Approve.

**nice:** after a `please_check` approval, the status message could stay `Approved: {title}.`; no addition needed. Do not add "You can undo" to it; the Undo button in place says it.

G4 question (already in the language guide, item 11): is "Please check" calm or anxious for real students; the verdict above does not depend on the answer, the hint wording might.

---

## 5. Overall: how close is the build to the round-2 direction

### 5.1 Honest critique
Where the implementers were free to follow the direction, they followed it closely, and in several places the build is better than the wireframe:
- The lecture screen is now the one-sheet reading room I drew: transcript as the sheet with its heading inside, ending where the words end; timestamps hanging in a margin from 1280; the rail a quiet ledger with the three windows in one row, the usual one filled and two-lined; the TL;DR line in the serif; the recap opening **above the transcript in the same column** (the boldest ask) with the transcript's first line still visible, and it passed the real-stack lecture e2e (fake microphone) on this layout, which no earlier slice had managed on that machine.
- Today has no cards, no rail, no eyebrows; margin labels, one filled button, a ledger with one link per row, the disclosure under the list. The course page and the forms follow the Sheet template; sign-in lost its eyebrow. The phone layout (`dashboard--390--paper.png`) is clean and the ledger is full-bleed as drawn.
- Primitives are exactly the component language of 2.7, with an audited ui-kit under axe in 12 looks, no transform anywhere, tertiary buttons in ink, links in ink with an accent line, a solid disabled style, one dialog look with the safe action filled.
- Preferences became a side panel, the theme picker is a single-tab-stop radiogroup with mini-page swatches, Portal and Calm are paint only and the landmark gate proves it.
- Things I did not ask for and would now not give back: the geometry gate, the landmark-equality gate across ten looks, the skin-block lint, the copy lint over every new string, the G1 computed-style pins (`consent-look.spec.ts`), and the habit of recording every deviation with its reason.

Where it falls short of the direction:
- **The lecture screen's first and widest element is still a bordered white Demo panel** (`lecture-live--1280--paper.png`, 1152px wide, two lines, 6px stripe, radius). The direction drew it as a one-line Note with Details. This is G1 (PO Q2), deferred by PO-17b, not an implementation gap, but it is the single biggest reason the lecture does not yet read as "one sheet on paper"; the sheet is the second box on the page, not the first.
- **The syllabus review is untouched** (`syllabus-review--1280--paper.png`): white item cards, three bordered controls per item, a "Calendar step" card, nine things above the first decision. It is the heaviest screen in the product and a Phase 2 primary flow; slice 6 has not started.
- **The safety states are half-translated**: notes above a panel with two stripe systems, a disabled Resume with loose reason text, the Stopped state with a disabled Export button mid-column and a rail reduced to one TL;DR block. Each is small; together they are the set of screens a student sees when something is wrong, which is when load matters most.
- **The serif rule is applied with two readings**: sheet headings ("Transcript", "Here is what was said") are serif, rail titles and note titles are sans, the collapsed note's line is serif. The agent's deviations 1 and 2 are sound, but the rule as written ("serif = synthesis") does not cover "Transcript". Write the rule as: *serif for titles of reading surfaces and for synthesis; sans for captions, controls, labels, data and verbatim.* No code change.
- **Calm and Gamer blunt the synthesis cue**: `--font-synthesis` follows the skin's heading face, so in Calm (body face headings) the TL;DR line differs from a transcript line by size and weight only, and in Gamer it is monospace. Acceptable, but slice 8's invariant list should state the floor: synthesis text differs from verbatim text by at least size (24 vs 17) in every skin.
- Small inconsistencies still visible: "Add a course" is a tertiary on Today while "Review one at a time" is a secondary; "Back to dashboard" / "Back to course" sit as orphan tertiaries at page bottoms; the All courses search field is 28rem beside a full-width ledger; the phone header is three rows at 320 to 389 (frozen names).

### 5.2 The three biggest remaining gaps
1. **Syllabus review density (slice 6).** Unbuilt; the direction's 5.4 wireframe stands. Add to its acceptance: the row order of section 4.1, no filled Approve on a "Please check" item, the Calendar step as a sticky bottom Note at column width (one fill always in view), and Add syllabus on the Sheet template. I would run slice 6 **before** 7 to 9: it changes what a student sees every week; the switches and the CI matrix change what we can prove.
2. **Demo-source presentation on lecture screens (G1, PO Q2).** Nothing the implementers can do. For the PO and counsel, the smallest presentation changes with wording byte-identical, in order of effect: (a) the panel rendered as a Note (4px stripe, no outer border, same text); (b) body behind "Details" at all times, not only during safety states; (c) the Demo tag out of the bar on desktop as it already is on phones. Each is reversible by one class.
3. **The safety-state set** (Paused, Mismatch, Not connected, Stopped, Fatal) as one designed family: the fills and gap of 1.3, the reason-as-description of 1.4, and for Stopped: the Export placeholder below the transcript rather than beside the search field, the rail's TL;DR as the only rail content is fine but its "Pause updates" is correctly gone; nothing else. And the largest unknown, which is not a design gap but must be said in the same breath: **no human has looked at any of this** (G4 NOT_RUN); the G4 question list below is the real next step.

### 5.3 What I would change in slices 7 to 9
**Slice 7 (Gamer and Glass switches, = T3 + T4)**
- Strings: Grid texture hint replaced as in 3.2 (lint); both switch hints and the Gamer description variant live in `PREFS_COPY` / `THEMES`, never as prop literals (add a lint: no `hint="` literal in `components/`).
- Placement: the theme-specific switch directly under the theme cards and only for the chosen theme (spec order). Its appearance moves the controls below it; student-initiated, instant, acceptable; the status line stays in the sticky head so the announcement is always in view.
- Add the synthesis floor to the Gamer checks: `.synthesis` in mono must still be 24/32 and must not be `.transcript-time`'s face rule by accident (skin lint already restricts `.transcript-time` to font-family; extend the thought to `.synthesis`: size and line height never set by a skin).
- Glass: the `emulateMedia` fallback tests the spec lists, plus one capture of the Low-stimulation overlay on **each** skin, *viewed*, since slices 3 to 5 all recorded "not viewed one by one".

**Slice 8 (lecture pass under 10 looks, = T5)**
- Assert, beyond 3.3: right-aligned time ink inside the margin track in every skin (1.2); safety note fill pairs and the 24px gap (1.3); TL;DR note height equal across skins at both widths (exists) and the 20/28 phone variant (1.1); synthesis vs verbatim size floor; no visible reason text in the bar while a safety note is shown, with the description present (1.4, if adopted); forced-colours rendering of notes and the ledger rail viewed, not only axe-checked.
- Loop the Quick recap result sheet (open) through the ten looks too; slice 3 captured it in four.

**Slice 9 (CI matrix, = T6)**
- PR subset as planned (Paper, Portal dark, Gamer dark, Calm light, Glass light) with unit, harness, geometry, landmark and axe; nightly full 10-look screens run; the copy lint and the skin-block lint in the PR gate; bundle budget.
- Serialize anything that uses the API dev login or cache its token: the slice 5 run hit the 10-per-minute limit, and a flaky gate is a gate people stop reading.
- Attach the screens as artefacts and link run records from `docs/RELEASE_GATES.md`; no gate turns PASS from a local run (CLAUDE.md gate rules).

---

## 6. Recommend-only register (G1; wording and presentation unchanged by this review)
1. Demo panel on lecture screens as a one-line Note with Details; tag off the bar on desktop (direction Q2).
2. Demo panel collapsed form as a `.note` (4px stripe, no border) so safety notes and the Demo notice share one notice language (1.3).
3. Mismatch note body repeating the heading sentence (1.3; wording).
4. Consent pop-up "Cancel" as a bordered secondary; the consent page's "Start recording" button opening a dialog (direction Q3).
5. The consent card losing non-Paper card decoration (slice 5 carve-out) is the conservative reading and I agree with it.

## 7. Questions for real students and clinicians (G4; add to the existing lists)
1. In the lecture, do the times feel attached to their lines (right-aligned) or do you want them further away?
2. In Focus mode, is the empty line under a short TL;DR a gap you notice, and does it bother you?
3. When recording is paused, does the tinted Paused block feel like information or like pressure to resume?
4. On a "Please check" item, did you read the quoted syllabus line before pressing Approve? Would a checkbox have changed that? Would it have annoyed you?
5. Does "The look of a course website" tell you what Course portal is, and did you expect it to move anything?
6. With Preferences open beside the page, did the page narrowing feel like you did it, or like something happened?

## 8. What I did and did not do
- Read every section of `redesign-round2.md` including Slice 0 to 5 results and all Lead decisions; read the captures listed above as images; read the code and tests named in each verdict.
- Ran no browser, dev server, test or build (per the brief; the dev login is rate-limited). Measurements quoted from captures are read off the images and the agents' own geometry records; none is a new measurement.
- Edited no source file and no other document. The coupled edits in 3.1 and 3.2 (`themes-spec.md` lines 202 and 204, `ThemePicker.test.tsx` lines 59-61 and 197-199) are listed so the Lead can assign them with the code change; they are not done.
- Nothing here is human-validated (G4 NOT_RUN). Gate G1 remains BLOCKED; every G1 item above is recommend-only.

## Summary
1. Keep the TL;DR reserve (shrink it on phones), right-align lecture times against the sheet, give safety notes a state tint and a 24px gap above the Demo panel, accept the Mismatch wrap until the PO decides Q2.
2. Keep "Your courses" as a margin label; stop the ledger's right edge at the column, keep the left bleed.
3. Replace all five theme descriptions and the Low-stimulation hint with the exact strings in sections 3.1 and 3.2 (the current hint contains "just" and is unlinted; "Course portal" promises a layout); add one sentence saying a theme changes colours, corners and heading type only; sync `themes-spec.md` and `ThemePicker.test.tsx` in the same change; fix the Grid texture hint ("behind") before slice 7.
4. No separate acknowledgement step for "Please check" approvals; reorder the row so the source precedes the hint and the actions, and do not fill Approve for an uncertain item in one-at-a-time.
5. The build matches the direction closely wherever it was allowed to; the three gaps are the unbuilt syllabus review (run slice 6 before 7 to 9), the Demo-source presentation on lecture screens (PO Q2, G1), and the safety-state screens as a family. No human has seen any of it.
