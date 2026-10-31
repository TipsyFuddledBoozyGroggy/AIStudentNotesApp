# Make notes after Stop, the syllabus Calendar-step rail, and the Demo-source notice as a Note

Status: AI SPECIALIST DESIGN, 2026-10-31. Author: adhd-ux-specialist (design lead, round 2). Audience: Lead Architect, Product Owner, frontend-engineer, qa-engineer, security-engineer, legal-compliance-reviewer.

**Nothing here is human-validated (gate G4 NOT_RUN).** No student, clinician, screen-reader or real-device evidence exists for any choice below. Tags: [J] design judgement, [C] project constraint (CLAUDE.md invariants 2, 3, 7, 9; frozen names; the three-live-region rule), [E] standard knowledge recalled, not re-fetched.

**Gate G1 stays BLOCKED.** PO-18c and PO-18d are Product Owner *risk acceptances for the personal-use scope (PO-16)*, not clearance: counsel has not reviewed any consent, recording, disclosure or Demo-source wording. Every string in this document that discloses what is recorded, sent, kept or deleted is marked **DRAFT (G1)** and must render with the visible "(Draft wording.)" suffix the existing Demo notice uses. The Demo-source wording is **unchanged word for word**; only its presentation changes (PO-18c).

What I read: `docs/ux/redesign-round2.md` in full (direction, Slice 0 to 7 results, every Lead decision), `redesign-round2-review.md`, `language-guide.md` (binding), `design-direction.md` (sections 1, 4, 5.4 to 5.7, 7, 8), `m2-live-lecture-screens.md` (0, 3, 11, decisions), `phase2-syllabus-review.md` (5.1, 6, 9 to 11), `docs/DECISIONS.md` PO-17 to PO-18d, `docs/API_CONTRACTS.md` section 17, `docs/architecture/notes-after-stop.md`; the code of `components/lecture` (`LectureScreen`, `Notices`, `StatusBar`, `Announcer`, `QuickRecapPanel`, `TldrPanel`, `Transcript` handle, `ports`, `realRuntime`, `copy.ts`), `components/syllabus/Review.tsx`, `Sync.tsx`, `syllabus/copy.ts`, `globals.css` (notes, panels, lecture grid, desk template, calendar step), the tests that pin the Demo notice and the live regions (`skinBlocks.test.ts`, `LectureRedesign.test.tsx`, `LectureScreen.test.tsx`, `tokens.test.ts`, `e2e/lecture.spec.ts`, `geometry.spec.ts`); and the captures `lecture-stopped--1280--night`, `lecture-live--1280--paper`, `lecture-live--390--paper--viewport`, `lecture-paused--1280--paper`, `lecture-mismatch--1280--paper`, `lecture-disconnected--390--night--viewport`, `syllabus-review--1280--paper`, `syllabus-review--390--paper`. I ran no browser, server or test and edited no source file; this document is the only file written.

Assumptions and dependencies: the backend half of PO-18d is as commit `0a60d51` and contract section 17 describe (mock provider only; `POST`/`GET /sessions/{id}/notes`; statuses `Pending | Ready | Unavailable | BudgetLimited | NotNeeded`; `source: mock | model`; `finalSummaryRevision`; `truncated`; `409 invalid_state` before any press; `Idempotency-Key` required). The Final summary reaches the client as a `summary.revision` with `status: "Final"` on the hub while joined, and through `GET /summary` otherwise (open question 3). Frontend-engineer builds; qa-engineer validates; security-engineer reviews that no new data or token path is added (none is: the press carries an empty body and a key).

---

## 0. The three decisions in one paragraph each

**Make notes (8a).** After Stop, a plain block titled "Notes" sits at the top of the transcript column: one sentence that says what the press does (DRAFT, G1), the one filled button "Make notes", and a "What is sent?" disclosure. The press is the only thing that sends the transcript to the AI after Stop; nothing is automatic, nothing is prompted, nothing counts down. Pending is a sentence, not a spinner; the student can leave and come back; one status announcement at the press and one when the notes are ready. Ready turns the block into a lecture sheet: serif section headings, sans bullets at 17/28, every bullet cited with time links into the transcript below, the "Demo notes" tag (unchanged Demo summary presentation) when `source = mock`, a coverage line, a "stops at" sentence when truncated, and one line that says what happened to the TL;DR. A new press is offered only after `Unavailable` or `BudgetLimited`.

**Calendar-step rail (8c).** From 1280px the Calendar step leaves the flow and takes a reserved 20rem track to the right of the item groups, sticky under the header, on the ledger tint: heading "Calendar step", the counts, the one filled "Add N approved items to the demo calendar", its reason. Because the track is reserved, no item target can ever scroll under it (SC 2.5.8 holds by construction) and "one fill always in view" holds on desktop. Below 1280px, at 400% zoom and on phones the step stays in the flow after the groups, as built in slice 6. DOM order is unchanged (groups, then the step), so the skip link, the h2 target and the Tab order are untouched.

**Demo-source notice (8b).** The notice becomes a one-line Note under the bar: outlined info glyph, the heading word for word, "Details" as a disclosure button; the body (unchanged) opens below the line. The Demo tag leaves the bar at every width. Safety notes (Paused, Mismatch, Offline, Fatal) take a state-tint fill so a safety fact and an information fact never look like the same thing; notes under the bar are 24px apart. Recording status (pill, word, shape, minutes, position) does not change by one pixel.

---

## 1. Make notes after Stop (PO-18d, option B) — slice 8a

### 1.1 Principles applied
- **Nothing automatic** [C invariant 7, B3-06]: no press on Stop, no press on reopen, no prompt ("Want notes?"), no badge on the course page, no reminder, no timer. The entry point is static text and a button. If the student never presses, the lecture stays a transcript and a Draft TL;DR, and the app never mentions it again.
- **Truth before the press** [C invariant 2 spirit, language guide 8]: the sentence beside the button says what will be sent and to what. It is draft wording until counsel has seen it (G1).
- **One region, one fill** (design-direction 1.1): "Make notes" is the only filled button in the transcript column while it is shown. The Stopped note keeps "Back to course" as a secondary; the rail keeps its own controls.
- **Return without penalty** (principle 5): the block shows the true state on every visit from `GET /notes`; leaving while Pending costs nothing; coming back shows the notes if they are there. No "you left" message.
- **Serif is synthesis, sans is verbatim** (PO-17a): section headings in the synthesis face; bullets in the verbatim face so a page of notes is readable in every skin (deviation reasoning in 1.6).

### 1.2 Where it lives
In the `Stopped` state only (server state `Stopped`; **not** `Failed`, which the server answers with 409), the Notes block is the first element of the transcript column, before the "Review this lecture" search toolbar and the transcript sheet. It starts on the transcript track at 1280 like the toolbar does (`xl:col-start-2`); when Ready it becomes a `.lecture-sheet` spanning the margin and transcript tracks like the recap sheet, so citation times hang in the margin. It renders on the live screen after Stop and on the reopen route (`/lectures/{id}`), which renders the same `LectureScreen`. It never renders in Active, Paused, Mismatch, Offline or Fatal.

Why not inside the Stopped note: the note states a safety fact ("Lecture stopped. Your transcript is saved.") and offers the exit; putting the AI decision in it would mix a fact with an offer and give the note two actions. Why above the search field: notes are what the student wants after class; search is a tool for the transcript and belongs beside it. [J]

### 1.3 States and the exact strings
All strings go into a new `NOTES_COPY` in `components/lecture/copy.ts` (linted by `copy.test.ts`). Strings marked DRAFT (G1) carry the visible suffix "(Draft wording.)" exactly as `DEMO_NOTICE_COPY.body` does. Nothing is human-validated (G4).

| Key | Exact string | Role | G1 |
|---|---|---|---|
| `heading` | `Notes` | h2, `id="notes-h2"`, `tabIndex -1` | |
| `intro` | `Pressing Make notes sends this lecture's transcript to the AI service to write notes and a final summary. Nothing is sent unless you press it. (Draft wording.)` | body text under the heading, ink, 17/28 | **DRAFT** |
| `make` | `Make notes` | the primary button (PO-18d name) | |
| `whatIsSent` | `What is sent?` | disclosure summary (`aria-expanded`), tertiary style | |
| `whatIsSentBody` | `The whole transcript of this lecture, including words from anyone else who spoke. The AI service may keep a copy under its terms. Deleting this lecture here does not remove that copy. (Draft wording.)` | disclosure body, sm | **DRAFT** |
| `pending` | `Writing notes from the transcript. You can leave this page and come back. The notes will be here when they are ready.` | replaces the intro and the button while Pending | |
| `pendingAnnounce` | `Writing notes. You can leave and come back.` | status region, once per press | |
| `readyAnnounce` | `Notes ready.` | status region, once per Ready | |
| `covers` | `Covers ${from} to ${to}.` | coverage line (`coveredFromMs` to `coveredToMs`) | |
| `truncated` | `These notes stop at ${to}. The lecture ran longer than the notes can cover. The transcript after ${to} is kept below.` | shown only when `truncated: true` | |
| `finalDone` | `The TL;DR was updated to its final version.` | when `finalSummaryRevision` is a number | |
| `finalKept` | `The TL;DR keeps its last version from the lecture.` | when `finalSummaryRevision` is null on Ready | |
| `notNeeded` | `This transcript has no words, so there are no notes to make. Nothing was sent.` | NotNeeded (final, no button) | |
| `notNeededAnnounce` | `No notes to make. Nothing was sent.` | status region, once | |
| `unavailable` | `Notes could not be made this time. Your transcript is saved. You can press Make notes again.` | Unavailable, button offered again | |
| `budget` | `Notes are not available for this lecture at the moment. Your transcript is saved. You can try again later.` | BudgetLimited, button offered again | |
| `unavailableAnnounce` | `Notes are not available. Your transcript is saved.` | status region, once per Unavailable or BudgetLimited | |
| `demoTag` | `Demo notes` | tag word beside `DEMO_SUMMARY_COPY.note` ("Made by a test routine, not an AI model.") when `source = mock` | **DRAFT** (new Demo word; open question 2) |
| `errors.rate_limited` | `One moment. Try again in about a minute. Nothing was changed.` | 429 (per-minute or attempt cap) | |
| `errors.invalid_state` | `Notes can't be made for this lecture. Your transcript is saved.` | 409 on POST (a race: the session left Stopped) | |
| `errors.not_found` | `We can't find this lecture. Go back to the course and open it again.` | 404 | |
| `errors.network` | `We could not reach the server. Try again when you like.` | status 0 (no "nothing was changed": the request may have landed; the retry reuses the key and the truth comes from GET) | |
| `errors.generic` | `That did not work. You can try again when you like.` | any other failure | |
| `TLDR_COPY.final` | `Final summary` | sm muted caption in the TL;DR row when the shown revision has `status: "Final"` | |

Checked against the language guide: no exclamation marks, no "just", "behind", "missed", "lost", "right now", no status codes, every sentence under 20 words, the subject is the app, the transcript or the student's own press. The one word to watch in G4 is "allowance", which I avoided; the budget message hides the reason as the recap's does (guide A14).

### 1.4 Wireframes (desktop 1280, Paper; `=` sheet, `.` ledger tint, `|` stripe, `[[ ]]` the one fill, `_x_` tertiary)

Stopped, before any press:
```
[ [] Stopped 48 min ] . Connected                                                       _Lecture settings_
| []  Lecture stopped                                 (note-neutral, unfilled; focus lands on this h2 after Stop)
|     Your transcript is saved.
|     [ Back to course ]
                                                      (24px)
| (i) Demo source: these words are not from your microphone    _Details_       (one-line Note, 8b)

margin | Notes                                                           | ..... TL;DR .....................
       | Pressing Make notes sends this lecture's transcript to the AI   | . Derivatives and the power rule.
       | service to write notes and a final summary. Nothing is sent    | . - Power rule ...  - Chain rule ...
       | unless you press it. (Draft wording.)                           | . From 00:02
       | [[ Make notes ]]    > _What is sent?_                            | . Updated at 48:12 (revision 9)
       |
       | Search this lecture                            [ Export to Google Docs ]
       | [_____________________________________]        Not built yet. Nothing is sent anywhere. ...
       | 212 lines.
       |
       | ==== Transcript ====================================================
 00:01 | = So the derivative of x squared is two x.
```

Pending (after the press; the button is gone, the text changed, focus is on "Notes"):
```
margin | Notes
       | Writing notes from the transcript. You can leave this page and come back.
       | The notes will be here when they are ready.
```
No spinner, no percent, no elapsed time, no "still". Height change is the student's own action.

Ready:
```
margin | ==== Notes ==========================================================  | ..... TL;DR ..............
       | = Covers 00:00 to 48:12.   [Demo notes: Made by a test routine, not an AI model.]  | . Final summary
       | = The TL;DR was updated to its final version.                         | . Derivatives, the power rule
       | =                                                                     | .   and the chain rule.
       | = Derivatives and the power rule                   (h3, synthesis xl) | . - ...
 00:01 | = The derivative of x squared is two x.                      _00:02_  | . Updated at 48:12 (revision 10)
 00:02 | = The power rule: bring the exponent down, lower it by one.           |
       | =                                                                     |
       | = The chain rule                                   (h3)               |
 00:09 | = The chain rule handles a function inside a function.  _00:11_ _00:14_ |
       | ======================================================================
       | Search this lecture ...
       | ==== Transcript ====
```
Each bullet is a `.hang-row`: its first citation time is the lead (margin from 1280, 3.25rem column inside the sheet below), further citations follow the text inline as `.cite` buttons. Truncated adds one sentence under the coverage line. Unavailable / BudgetLimited render their sentence as a `note note-notice` above the button, which is shown again.

Phone 390, Stopped before the press (one column, in DOM order):
```
[ [] Stopped 48 min ] . Connected
 _Lecture settings_
| [] Lecture stopped / Your transcript is saved. / [ Back to course ]
| (i) Demo source: these words are not from your microphone   _Details_
| TL;DR                                         _More_
| Derivatives and the power rule.
Notes
Pressing Make notes sends this lecture's transcript to the AI service to write notes
and a final summary. Nothing is sent unless you press it. (Draft wording.)
[[ Make notes ]]
> _What is sent?_
Search this lecture  [__________]   [ Export to Google Docs ]
==== Transcript ====
```
Ready on the phone: the Notes sheet is square-edged and full-bleed like the transcript sheet (`.lecture-sheet` below 640), citations in the 3.25rem lead column. If the Quick recap dock is present (it is, in Stopped, as built today) the page keeps its `pb-44` clearance; nothing here changes the dock.

### 1.5 Behaviour
- **Mount (Stopped):** one `GET /notes`. `409 invalid_state` means no press yet: show the entry point. `200` shows the state. `404`: show `errors.not_found` in place of the block. Network failure on this GET: show the entry point with a `note note-notice` "We could not check for notes. Try again when you like." (new key `checkFailed`) and a tertiary "Try again" that repeats the GET; the primary is still offered (a press is safe: the server replays any existing press with 200).
- **Press:** one `POST /notes` with body `{}` and a fresh `Idempotency-Key` (the existing lecture key helper). While in flight: `aria-busy="true"`, label unchanged, width fixed (`.btn-steady`), a second activation does nothing. Response `202`/`200` with `Pending`: enter Pending. With `Ready`, `NotNeeded`, `Unavailable`, `BudgetLimited`: render that state directly (a replay or an immediate decision).
- **Pending:** poll `GET /notes` every 3 s for the first minute, then every 10 s, until the status leaves Pending or the component unmounts; the server marks a lost job `Unavailable` after 600 s, so the client never needs its own timeout. No visible timer. The TL;DR row keeps working; the transcript keeps working.
- **Ready:** render; announce once; fetch `getSummary()` once if `finalSummaryRevision` is set and the TL;DR currently shown is not Final (hub delivery may already have done it); never move focus, never scroll.
- **Retry rule:** the button is rendered again only when the newest status is `Unavailable` or `BudgetLimited`; a new press takes a new key (new attempt, `revision + 1`). `Ready` and `NotNeeded` are final: no button, ever, for that session.
- **Press problems** (429, 404, 409, network, 5xx): render `errors.*` as a `note note-notice` directly under the button (`tabIndex -1`), move focus to it (the student caused it; the ActionProblem pattern), keep the button. A network failure keeps the same key for the next press of the same intent; any answered error discards it.
- **Delete** (elsewhere): the block is gone with the session; nothing to design here. The "What is sent?" body already says vendor copies are not removed (DRAFT).
- **Export to Google Docs** stays the disabled placeholder beside the search field; later it exports the notes; out of scope.

### 1.6 Reading order, density, type
- Order inside the Ready sheet: h2 "Notes"; stamp line (coverage, then the Demo notes tag when mock); the truncated sentence (when true); the TL;DR line; then sections in server order, each `h3` + `ul`. No "Close": there is nothing to close; the sheet is the record.
- Type: h2 `display text-xl` (title of a reading surface); h3 `.synthesis text-xl` (24/32; AI headings are synthesis); bullets verbatim face 17/28 on the 68ch measure; cites `.cite`; stamp line sm muted. Sections are 24px apart (`space-y-6`), bullets 8px (`space-y-2`); no rules between sections, no boxes per section (fewer edges).
- **Why bullets are sans, not serif [J]:** the synthesis face follows the skin's heading face, which is monospace in Gamer and the body face in Calm; a page of monospace prose is a poor reading surface, and the recap already sets its claims in the sans with only the lead in the serif. The sheet-level cues (serif headings, the Demo notes stamp, the coverage line) mark the whole sheet as synthesis. If G4 participants read sans bullets as "verbatim lecture", the alternative is `.synthesis text-lg` (20/28) on bullets, one class.
- Citations: every bullet has 1 to 8; the first is the lead time; the rest inline after the text as `.cite` buttons separated by 8px. Name: `"{mm:ss}, go to transcript line"` (existing recap pattern). Activation calls `transcript.focusSegment(segmentId)`; if the segment is not loaded (paging), behave as the recap does today. The transcript sheet below is the way back (scroll or Shift+Tab); no automatic return. G4 question 3.
- Demo labelling: when `source === "mock"`, the tag renders with the **unchanged Demo summary presentation** (same classes as `DemoSummaryTag`, the Lead's slice-3 revert) and the word `Demo notes` plus the unchanged sentence; test id `demo-notes-tag`; never hidden, never dismissible. `source === "model"` shows no tag (and cannot occur while the startup guard refuses real providers).

### 1.7 Keyboard, focus, live regions
- Tab order in Stopped (desktop): skip links; bar ("Lecture settings"); Stopped note ("Back to course"); Demo note ("Details"); **Notes** ("Make notes", "What is sent?"); search field; Export (aria-disabled, focusable); transcript log; rail (TL;DR controls). DOM order equals visual order in both layouts.
- Focus moves only on the student's action: after an accepted press, to `#notes-h2` (the pressed control is gone, so focus would otherwise fall to `body`); after a failed press, to the error note. Ready, Unavailable, BudgetLimited and NotNeeded arriving from the poll never move focus or scroll (system events).
- Live regions: **exactly three remain** [C]; the block uses `useAnnouncer().status(...)` for the four announcements in 1.3 and adds no `aria-live`, `role=status` or `role=alert` (the `tokens.test.ts` allow-list and the `LectureScreen.test.tsx` count stay as they are). No `aria-busy` on the section (it is not a status channel); only on the button while a request is in flight. "What is sent?" and the heading face change announce nothing.
- `aria-describedby` on "Make notes" points at the intro sentence, so a screen-reader user hears the disclosure with the button.
- Shortcuts: none added.

### 1.8 Quiet / low-stimulation
Nothing in the block is automatic, timed, animated or counted. Low-stimulation changes paint only; the "Make notes" fill stays a fill (one primary per region is layout, not decoration), the Demo notes tag keeps its tint (Demo labels are never dimmed, 3.3 item 6 by analogy). Reduce motion: no transitions exist to remove. Focus mode on after Stop: the block renders in the 52rem column the same way.

### 1.9 Acceptance criteria (frontend and QA)
1. The Notes block renders only when the lecture's server state is `Stopped`, as the first element of the transcript column, with h2 `Notes` (`#notes-h2`), the intro sentence ending "(Draft wording.)", the primary `Make notes` and the `What is sent?` disclosure whose body ends "(Draft wording.)". It does not render in Active, Paused, Mismatch, Offline, Fatal or Failed.
2. On mount in Stopped the client sends exactly one `GET /notes` and **no** `POST`; a unit test spies the `LectureAi` port across mount, Stop, reopen, Focus toggle, theme change and a 15-minute fake-timer run and asserts zero `requestNotes` calls without a click.
3. `Make notes` is the only `.btn-primary` inside the transcript column. One activation sends one `POST /notes` with body `{}` and an `Idempotency-Key`; a second activation while `aria-busy` sends nothing; the label and width do not change while busy.
4. After `Pending`: the button is not in the DOM, the pending sentence is, focus is on `#notes-h2`, the status region received `Writing notes. You can leave and come back.` once, and no element has a spinner, a percentage, an elapsed time or a new `aria-live`.
5. Polling: `GET /notes` repeats at most every 3 s and stops on unmount; on remount it starts from a fresh GET. Reload and the reopen route show the current state with no POST.
6. `Ready`: the section has class `lecture-sheet`; order is h2, stamp line, (truncated sentence), TL;DR line, sections; every bullet shows at least one citation button named `{mm:ss}, go to transcript line` whose activation focuses the transcript line with that `data-segment-id`; `source: mock` shows `demo-notes-tag` containing "Demo notes" and "Made by a test routine, not an AI model."; the status region received `Notes ready.` once; `document.activeElement` did not change; `scrollY` did not change.
7. `truncated: true` renders the "These notes stop at …" sentence with `coveredToMs` formatted; `finalSummaryRevision: null` renders `finalKept`, a number renders `finalDone`.
8. `NotNeeded` renders its sentence and no button; `Unavailable` and `BudgetLimited` render their sentence as `note note-notice` and the button again; a new press uses a different `Idempotency-Key`; after a 429 the message `One moment. Try again in about a minute. Nothing was changed.` shows, focus is on it, and the button remains.
9. The TL;DR row shows the caption `Final summary` when the shown revision has `status: "Final"`, without expanding, moving focus or announcing (unless "Announce TL;DR updates" is on, which keeps its existing 60 s rule).
10. Geometry at 1280 (Paper): the Notes sheet's painted surface starts on the transcript track (`--sheet-start`), its heading and text at the same x as the transcript heading (`--hang-text`), its lead citation ink inside the margin track; below 640 the sheet is square and full-bleed with the 16px inset; every citation button has a 44px hit area; no horizontal overflow at 320.
11. Serif/sans: `h3` carry `.synthesis` at 24/32; bullets are the verbatim face at 17/28; in every skin the heading differs from the bullet text by face or by size (landmark loop over the 10 looks on the new `lecture-notes-ready` fixture).
12. Live regions: exactly three in Stopped with notes Ready (existing count test extended to the new fixtures). axe (wcag2a/aa/21aa/22aa/best-practice): 0 violations on `lecture-stopped`, `lecture-notes-pending`, `lecture-notes-ready`, `lecture-notes-unavailable` in Paper, Night desk, Fog, Dusk at 390 and 1280, and the Ready screen in all 10 looks.
13. Copy lint passes over `NOTES_COPY`; every DRAFT string renders its "(Draft wording.)" suffix visibly (not `sr-only`); `copy.test.ts` pins the strings against this document.
14. Real-stack e2e on the mock provider: consent, record, Stop, `Make notes`, `Pending` then `Ready` within the poll, the Demo notes tag visible, a citation focuses a transcript line, a second `Make notes` does not exist, reload shows the notes without a POST (network log asserted).

---

## 2. Demo-source notice as a one-line Note (PO-18c) — slice 8b

### 2.1 Markup and semantics (wording byte-identical)
```html
<div class="{NOTICE_SLOT}">
  <section data-testid="demo-notice" aria-labelledby="demo-notice-h2" class="note note-notice demo-note">
    <span class="text-warning" aria-hidden="true"><!-- outlined info glyph, 22px, as today --></span>
    <h2 id="demo-notice-h2" class="note-title">Demo source: these words are not from your microphone</h2>
    <button type="button" class="btn-tertiary btn-sm demo-note-toggle"
            aria-expanded="false" aria-controls="demo-notice-body" aria-describedby="demo-notice-h2">Details</button>
    <p id="demo-notice-body" class="demo-note-body text-sm text-muted" hidden>
      The transcript is a fixed test script that plays as audio arrives. No real speech service is listening, so what you say is not turned into text. Audio is only used to time the script and is not kept. (Draft wording.)
    </p>
  </section>
</div>
```
- Layout: `.demo-note { display: grid; grid-template-columns: auto minmax(0,1fr) auto; grid-template-areas: "icon title toggle" ". body body"; column-gap: .75rem; align-items: baseline; }`. One line at 1280 (heading at 20/28 is about 540px); the heading wraps to two lines on phones and the button stays on the heading's first baseline at the end of the line, 44px hit area through padding and negative margin (the `.item-source-toggle` trick) so the line stays 28px.
- Why a button and not `<details>`: the body must open under the whole line, not inside the toggle's own box; `display: contents` on `<details>` is the only way to do that with native disclosure and its AT behaviour is uneven [E]. The TL;DR "More" and the syllabus source toggle already use `button[aria-expanded]`; one pattern.
- The body stays in the DOM with `hidden` (the string is always present for tests and for find-in-page); `hidden` toggles instantly; the open state lives in component state and survives Live → Paused → Stopped re-renders (the component stays mounted in its slot).
- The `compact` prop goes away: the body is behind Details in **every** state (PO-18c (b)). The heading is always visible, the section is never dismissible, there is no close control [C].
- Strings: `DEMO_NOTICE_COPY.heading` and `.body` unchanged; "Details" unchanged. `DEMO_SOURCE_TAG` is no longer rendered (2.2); the constant stays in `copy.ts` (open question 1).
- Accent: the old `<summary>` was the last `text-accent` in `Notices.tsx`; the tertiary button is ink, so the ratchet entry for the file drops to 0.

### 2.2 The tag leaves the bar
`StatusBar.tsx` no longer renders `<span data-testid="demo-tag">` at any width (it was already hidden below 640). The `demoSource` prop stays on `StatusBar` only if something else needs it; otherwise remove it. Consequence worth recording: the Mismatch bar at 1280 (pill, Connected, disabled Resume, the visible reason "Not available at the moment", Stop, Focus, Lecture settings) now fits one 56px row, which closes review 1.4 without the `aria-describedby` lever. The `document.title` prefixes are unchanged.

### 2.3 States
| Lecture state | Notice stack under the bar (DOM order, 24px apart) | Demo note |
|---|---|---|
| Live (Recording) | Demo note | heading + Details, collapsed |
| Paused | Paused note (filled warning tint, filled bars glyph, Resume primary + Stop) → Demo note | same; its open state is kept if the student opened it earlier |
| Mismatch | Mismatch note (filled recording tint, triangle) → Demo note | same |
| Disconnected | Offline banner (filled warning tint) → [Paused note if also paused] → Demo note | same |
| Stopped | Stopped note (neutral, **unfilled**) → Demo note → Notes block in the column | same |
| Fatal | Fatal note (filled danger tint) → Demo note | same |
| Focus mode | identical; the slot keeps the full container as today | same |
| 320px / 400% zoom | heading wraps to 2 to 3 lines; Details stays on the first line's end or wraps under the heading when fewer than 6ch remain; nothing clipped; no horizontal scroll | same |

Safety first: the stack order is unchanged (safety or state note, then the Demo note, then the audio-skipped note). The Demo note is never the first element after the bar in a safety state.

### 2.4 Fill, stripe and gap (review 1.3, applied)
- Add `.note-filled` to the four safety notes only: Paused and Offline `background: var(--c-warning-subtle)`, Mismatch `var(--c-recording-subtle)`, Fatal `var(--c-danger-subtle)`. The Stopped note (neutral) and the Demo note stay unfilled. Rule to write into 2.7: **a safety note is tinted and its glyph is filled; an information note is untinted and its glyph is outlined.** Colour is never the only cue (stripe + glyph + word remain).
- Stripe: unchanged 4px on both kinds; with the Demo panel gone, the 4px-over-6px stripe pair no longer exists.
- Gap: notes under the bar are 24px apart (the second and later slots take `mt-6`; the first keeps `mt-3` under the bar). The Demo note is one line, so Paused + Demo at 1280 is about 150 + 24 + 60px.
- Contrast pairs to add to `PRIMITIVE_PAIRS` and pass in all 12 palettes before this lands: `ink` and `muted` on `warning-subtle`, `recording-subtle`, `danger-subtle` (4.5); `border-strong` on each (3.0, the secondary button's border inside a filled note); the stripe colour on its own subtle (3.0). If a pair fails in any palette, drop the fill for that variant rather than change a token (review 1.3). Low-stimulation must not strip these fills (3.3 item 6); forced colours loses them and keeps the `CanvasText` outline, stripe, glyph and word.
- The transcript `max-height` bound (`100vh - 16rem` at desktop) was derived with a 106px Demo panel; with a 60px note the bound is 46px conservative. Re-derive and record, or keep 16rem and say so; the harness test "newest line is not hidden behind the dock" pins the phone bound and must stay green.

### 2.5 Focus and announcements
Details toggles instantly; focus stays on the button; nothing is announced (`aria-expanded` carries the state). The notice never receives focus on a state change and never moves focus. It is not a live region and adds none. In the Tab order it follows the state note's controls and precedes the column.

### 2.6 What tests must pin (and which existing assertions change)
Pin, new or re-pointed:
1. `demo-notice` present, visible, with the heading text byte-identical, in every harness fixture (live, paused, disconnected, mismatch, stopped, mic-denied where it renders today), at 320 / 390 / 1280, in Focus mode, in Paper, Night desk, Fog, Dusk and the 10 looks.
2. Inside it: an `h2` with the heading string; a button named "Details" with `aria-expanded` and `aria-controls` pointing to an element whose text is `DEMO_NOTICE_COPY.body` byte-identical; the body `hidden` by default and shown after one activation; no element with a name like close, dismiss or hide.
3. No `data-testid="demo-tag"` anywhere (`e2e/lecture.spec.ts:114` becomes `toHaveCount(0)`; `LectureScreen.test.tsx:19-20` asserts absence; `LectureRedesign.test.tsx:183` and `skinBlocks.test.ts:388` are deleted or inverted).
4. `skinBlocks.test.ts:378-389` rewritten: the Demo notice is `note note-notice demo-note`, the four safety notes carry `note-filled`, the Stopped note does not, and `copy.ts` still holds `DEMO_NOTICE_COPY` with the two strings (quote them in the test, from this document).
5. The `.panel` class is no longer used under `components/lecture`; its remaining users are the Add syllabus Draft notice (G1, slice 6 pin) and the ui-kit sample. The `text-accent` ratchet for `Notices.tsx` drops to 0.
6. Geometry: the bar is 57px (one row) at 1280 in every fixture including Mismatch and Disconnected; the notice slot's x and width equal the column's (existing check 312); the collapsed Demo note is at most 64px tall at 1280 and at most 112px at 320; consecutive notes under the bar are 24px apart.
7. Recording status: pill box (44px), classes, shape, word and minutes unchanged, asserted by the existing harness and geometry tests, plus a byte compare of the pill region in the before/after captures of all eight lecture screens.
8. Live regions: exactly three (existing tests, unedited).
9. `tokens.test.ts`: the new `PRIMITIVE_PAIRS` rows pass in all 12 palettes.
10. The harness test "Paused panel needs no scrolling at 320" stays green with the fill and the 24px gap.
Byte-identical and untouched: `ConsentStart.tsx`, `ConsentDialog.tsx`, `consent-look.spec.ts`, every consent capture.

---

## 3. Reserved-space rail for the Calendar step (PO-18b) — slice 8c

### 3.1 Why a reserved track, and why 1280
A bar stuck over a list of pointer targets obscures whichever row scrolls under it, which axe reports as `target-size` and SC 2.5.8 counts as a failure (slice 6 deviation 10, proven by the real-stack run). A **reserved grid track** cannot overlap anything: the rail has its own column, and `position: sticky` inside that column only ever moves the rail within space no row occupies. SC 2.4.11 (focus not obscured) holds for the same reason.

Width arithmetic (Paper, classic scrollbar 15px): at 1280 the layout width is 1265, container `min(72rem, 1265 - 64) = 1152`, Desk column `1152 - 112 = 1040`; a 20rem rail (`--aside-w`, 320px, already declared in Layer C and unused) plus a 40px gap leaves **680px** for the rows, which is more than slice 6's 636px (`max-w-prose`) and enough for the title line with its date and the three actions from 768. At 1024 the column is 833px and the rows would be 473px: too narrow for that line. So the rail starts at **1280**, the same breakpoint as the lecture's margin track; between 1024 and 1279 the step stays in the flow. At 1536 the rows are 808px (lists are full width in the Desk template; prose blocks keep `max-w-prose`).

### 3.2 Layout
From 1280, inside `Review`, the groups and the Calendar step share one grid:
```
.review-body            { display: grid; grid-template-columns: minmax(0, 1fr) var(--aside-w); column-gap: 2.5rem; align-items: start; }
.review-body > .groups  { min-width: 0; }                         /* DOM first: all group sections */
.review-rail            { position: sticky; top: 5rem; max-height: calc(100vh - 6rem); overflow-y: auto;
                          background: var(--ledger-bg); --ledger-inset: 1rem; }   /* DOM second: the Calendar step */
.review-rail .calendar-step::before { content: none; }           /* the tint is the edge; no stripe inside the rail */
.review-rail .calendar-step { padding: 1rem; }
```
Below 1280 `.review-body` is a plain block and the Calendar step is the `section.note.note-neutral.calendar-step` in the flow after the groups, as built (stripe, heading, button, reason). The sticky `top: 5rem` equals `html { scroll-padding-top: 5rem }` (header 56 + 24). The rail's right edge is the column's right edge (the header's "Sign out" ink), its top equals the first group head's top (R3 shared edge), its width 320, the gap 40.

The outer `max-w-prose` wrapper of `Review` becomes full column width; the h1, Demo lines, summary, time zone, toolbar, "Your data" and the links keep `max-w-prose` individually.

Wireframe, 1280:
```
        | Review your syllabus items                                   (display)
        | (Demo calendar) Demo mode: nothing here goes to Google Calendar.
        | (Demo) Demo mode: items are found by a test routine, not a real AI model.
        | We found 6 items. 2 approved, 0 skipped, 4 not decided.   1 needs a date. _Show them_
        | Times are in America/New_York.
        | Group by (Week | Type)   _Review one at a time_   _Expand all_
        | > _More ways to decide_
        |
        | v Week of Sep 14 to 20, 2 items, 1 not decided              | . Calendar step                 .
        | _Approve the clear ones in this week (1)_                   | . 2 approved, 0 skipped,        .
        | . Quiz 1 in class   Mon, Sep 14 · All day   [Undo] _Skip_ _Edit_ | .   4 not decided.               .
        | . Exam · Approved                > _See where this came from_ | . [[ Add 2 approved items to     .
        | .|----------------------------------------------------------- | .     the demo calendar ]]       .
        | . Problem set 1 due  Fri, Sep 18 · 11:59 PM  [Approve] _Skip_ _Edit_ | .                            .
        | . Assignment · Not decided       > _See where this came from_ | .  (sticky at 5rem; 320 wide)    .
        | > Week of Oct 12 to 18, 2 items, 2 not decided
        | > Needs a date, 1 item, 1 not decided
        |
        | Your data ...
```
Rail content, top to bottom: h2 "Calendar step" (`.note-title`, `#calendar-step-h`, `tabIndex -1`, the skip-link target, name frozen), one counts sentence, the filled button (full rail width, wraps to two lines at 17px, min 44px), the reason `Approve an item to continue.` while nothing is approved (`aria-describedby`, as built). Nothing else: no Demo line, no time zone, no progress bar.

Counts sentence (new `SYNC_COPY.railCounts(approved, skipped, undecided)`): `${a} approved, ${s} skipped, ${u} not decided.` It is the second half of the existing summary line in the same words, so the vocabulary stays one. It is plain text, not live; the status region already says "Approved: {title}." per press. It renders in the flow version too (one component, both widths), so what a phone student reads after the groups is what a desktop student reads in the rail.

Why the Demo calendar line and the time zone stay at the top [J]: they are read once before deciding; the two Demo lines keep the position and presentation slice 6 pinned (wording and tag unchanged, G1-adjacent), and repeating a sentence on one screen is noise. The button's own words carry "demo calendar" next to the action. If the PO wants the Demo calendar line beside the button as well, it is one more `DemoLine` in the rail with identical wording; I do not recommend it.

### 3.3 Phone, zoom, reflow
- Below 1280 (all phones, tablets, 1024 to 1279, and any desktop at 200% or 400% zoom, where 1280 CSS px become 640 or 320) the step is in the flow after the groups, exactly as built: no overlap, no sticky bar. "One fill always in view" is **not** met there and I claim nothing else; the skip link and "Review one at a time" (whose card carries its own calendar action) are the phone paths.
- 400% zoom on a 1280 viewport is 320px of layout: single column, no horizontal scroll (1.4.10), rows stack as slice 6 built them.
- Text spacing (1.4.12) in the rail: the button and the counts wrap; `max-height` with `overflow-y: auto` keeps a very tall rail usable rather than clipped.

### 3.4 Focus order, skip link, landmarks
- DOM order is unchanged: summary (with the focus-revealed "Skip to calendar step" button), toolbar, groups, Calendar step, Your data, links. Tab order therefore ends the list and then reaches the rail's button, then "Your data". For a sighted keyboard user focus jumps from the last row to the right-hand rail; that is the meaningful sequence (decide, then add) and the skip link already exists for the shortcut. 1.3.2 and 2.4.3 hold because the sequence the DOM gives is the task's sequence.
- "Skip to calendar step" still focuses `#calendar-step-h`; because the rail is sticky the heading is already in the viewport at 1280 and the page does not scroll; below 1280 `scroll-padding-top: 5rem` keeps it clear of the header as before.
- The Calendar step keeps `aria-labelledby="calendar-step-h"` (a region named "Calendar step"); not live.
- `scroll-padding-bottom` is not needed (nothing fixed at the bottom).

### 3.5 Acceptance criteria
1. From 1280 (Paper, classic scrollbar), `[data-calendar-step]` is inside `.review-rail`, `position: sticky`, `top` 80px, width 320 ± 1, its right edge equal to the column's right edge ± 1, its top equal to the first `.group-head`'s top ± 1, and the gap between the rows' right edge and the rail 40 ± 1.
2. At every scroll position from 0 to the page bottom (sampled every 200px) no `li.ledger-row` box, no button inside a row and no `.group-head` box intersects the rail's box; axe `target-size` reports nothing on the review page at 1280 x 900 and 1440 x 900 with the first group open and with all groups open (the real-stack syllabus e2e keeps its existing scan green).
3. Below 1280, at 1024, 768, 390 and 320, `[data-calendar-step]` is `static` or `relative`, after the last group in both DOM and visual order, with no rule positioning it; the page has no horizontal overflow at 320 and at 400% zoom of 1280.
4. The rail holds exactly: the `h2` "Calendar step", the counts sentence, one `.btn-primary` named `Add N approved items to the demo calendar`, and the reason while N = 0; it is the only filled button in `main` at every width (the segmented control's pressed option excepted, as today).
5. The counts sentence equals `${approved} approved, ${skipped} skipped, ${undecided} not decided.` and matches the top summary's numbers after every Approve, Skip, Undo, edit and batch; it is never inside a live region and nothing is announced because of it.
6. Keyboard: the "Skip to calendar step" button is unchanged in position and target; Tab from the last row's last control reaches the rail's button next; Shift+Tab returns; Escape and Enter behaviour of rows and dialogs is unchanged; `Review.test.tsx` focus map tests pass unedited.
7. Landmarks and names unchanged: `h2` "Calendar step", `aria-labelledby`, button name, `aria-describedby` on the disabled fill, `Approve an item to continue.`; `copy.test.ts` pins the new counts string.
8. Skins: the rail takes `--ledger-bg` and the ledger's Portal card edge / Calm radius rules exactly as `.lecture-rail` does (paint only); the landmark-rect gate (if the review page is added to it) sees the rail equal within 1px across the 10 looks at 1280.
9. axe 0 violations on `syllabus-review` at 390 and 1280 in the 8 captured looks; the two Demo lines are byte-identical in position and text (G1 pins stand).

---

## 4. Build plan: three file-disjoint slices

Order: **8b first** (small; it also changes `LectureScreen.tsx`, which 8a touches next), **8c in parallel with 8b** (syllabus files only), **8a after 8b**. Every slice: unit, harness, geometry, screens + axe on touched screens; copy lint over every new string; record commands actually run; nothing marked done from a capture alone; consent files show no diff.

| Slice | Content | Files likely touched | Tests (new or changed, with the reason) | Risk |
|---|---|---|---|---|
| **8b Demo notice as a Note** | `DemoNotice` → one-line `.note.note-notice.demo-note` with a `Details` button and a `hidden` body; `compact` prop removed; Demo tag removed from the bar; `.note-filled` on Paused / Offline / Mismatch / Fatal; 24px stack gap; transcript `max-height` sum re-derived or recorded | `lecture/Notices.tsx`, `lecture/StatusBar.tsx`, `lecture/LectureScreen.tsx` (prop, slot gaps), `app/globals.css` (`.demo-note`, `.note-filled`, slot gap, height comment), `lib/design/tokens.ts` (`PRIMITIVE_PAIRS`) | `skinBlocks.test.ts` 378-389 rewritten (PO-18c supersedes the G1 pin for this item), `text-accent` ratchet `Notices.tsx` 1 → 0; `LectureRedesign.test.tsx` 179-183 re-pointed; `LectureScreen.test.tsx` 19-20 → tag absent; `e2e/lecture.spec.ts` 114 → `toHaveCount(0)`; `tokens.test.ts` new pairs; `geometry.spec.ts` bar one row at 1280 in all fixtures, note height, 24px gaps; harness `lecture.spec.ts` unedited (320 Paused no-scroll); screens: all 8 lecture screens re-captured in 4 looks and viewed | Low. The only G1-sensitive step is already decided (PO-18c); wording pinned byte-identical |
| **8c Calendar-step rail** | `.review-body` grid from 1280 with the step in `.review-rail` (sticky, ledger tint); counts sentence; `Review`'s outer `max-w-prose` moved onto its text blocks | `syllabus/Review.tsx`, `syllabus/copy.ts` (`railCounts`), `app/globals.css` (`.review-body`, `.review-rail`, 1280 rules; the slice-6 "no positioning rule" comment replaced) | `skinBlocks.test.ts` "calendar step never positioned" → "positioned only as sticky inside `.review-rail` from 1280"; `geometry.spec.ts` review check: static below 1280, rail geometry and the no-intersection scan from 1280; `Review.test.tsx` + counts sentence, rail hooks, unchanged focus map; `copy.test.ts` new string; `e2e/syllabus.spec.ts` unedited (axe at 1280 x 900 must stay green); screens `syllabus-review` 8 looks x 2 widths | Low-medium: the grid wrapper changes `Review`'s structure; the row grid itself is untouched |
| **8a Make notes UI** | `NOTES_COPY`; `NotesPanel` (block → sheet) and `useNotes` (mount GET, press, poll, retry rules, key handling); `LectureAi` gains `requestNotes(key)` / `getNotes()`; strict `parseNotes` against `lecture.v1/notes.schema.json` fixtures; API client routes; MSW mock routes; harness fixtures `stopped-notes-pending`, `-ready`, `-ready-truncated`, `-unavailable`, `-not-needed`; TL;DR `Final summary` caption; the Demo notes tag | `lecture/copy.ts`, `lecture/NotesPanel.tsx` (new), `lecture/useNotes.ts` (new), `lecture/ports.ts`, `lecture/realRuntime.ts`, `lib/api/client.ts`, `lib/lecture/types.ts`, `lib/lecture/aiPayloads.ts`, `lecture/LectureScreen.tsx` (mount in Stopped), `lecture/TldrPanel.tsx`, `lecture/harness/fixtures.tsx`, `mocks/handlers.ts`, `app/globals.css` (only if the sheet needs a rule `.lecture-sheet` lacks), `e2e-screens/screens.spec.ts` (+ screens) | New `NotesPanel.test.tsx` (every state, the no-POST-without-click spy, focus moves, four announcements, key rules, three live regions); `aiPayloads.test.ts` (valid/invalid fixtures from `src/shared/contracts`); `LectureRedesign.test.tsx` (block first in the column in Stopped; sheet classes when Ready; serif h3 / sans bullets; cite focus); `copy.test.ts`; harness `redesign.spec.ts` (1280 and 390 geometry of the Ready sheet, focus after press, announcer spy); real-stack `e2e/lecture.spec.ts` (Stop → Make notes → Ready on the mock provider, reload without POST); screens `lecture-notes-*` in 4 looks, Ready in 10; geometry sheet edges | Medium: new wire parser and poll loop; the real-stack step needs the API with the notes routes up and the dev login under its 10/min limit (serialize suites, reuse the token) |

Not in these slices: tabs on the reopen page (design-direction 5.6), export of notes to Google Docs, any real AI provider, a `/preferences` route, any consent or Demo wording change, any change to the recording pill, the Quick recap dock's presence in Stopped (as built).

---

## 5. Open questions (only where a decision is needed)
1. **`DEMO_SOURCE_TAG` ("Demo source: replayed test audio")**: my reading of PO-18c is that it leaves the UI entirely at every width (it was already absent on phones) and the one-line Note's heading carries the Demo fact. The constant stays in `copy.ts` unused. Confirm, Product Owner, because the real-stack e2e assertion that the tag is visible is deleted.
2. **"Demo notes" tag word (DRAFT)**: a new Demo word beside the unchanged sentence, or reuse "Demo summary" verbatim on the notes sheet? I recommend "Demo notes" (truthful about what is labelled) and mark it G1 like every Demo string.
3. **Final summary delivery to the client**: contract 17 publishes the Final revision on the hub to joined clients; for a reopened Stopped session the client would read it through `GET /summary`. Lead Architect to confirm `getSummary()` returns the Final revision as the latest and that `status: "Final"` is on the wire in both paths (the client type already has it).

## 6. Questions for real students and clinicians (G4; add to the existing lists)
1. Before pressing "Make notes", did the sentence tell you what would be sent and to whom? Did "Nothing is sent unless you press it" read as reassurance or as pressure to press?
2. While notes were being written, did you want a progress sign, or was the sentence enough? Did you leave the page, and did coming back feel safe?
3. In the notes, did the serif headings and sans bullets read as "the app's summary" or as "what the lecturer said"? After following a time link into the transcript, how did you get back, and was that fine?
4. On the syllabus review at desktop width, did you notice the Calendar step on the right while you decided, and did the counts there help or distract?
5. Did the one-line Demo note with "Details" tell you enough, or did you want the explanation open?

## 7. Summary
1. **Make notes** is a plain "Notes" block at the top of the Stopped transcript column: one DRAFT disclosure sentence, the one filled "Make notes", a "What is sent?" disclosure; Pending is a sentence with one announcement and no spinner; Ready is a lecture sheet with serif section headings, sans cited bullets, a coverage line, a truncated sentence, a TL;DR line and the "Demo notes" tag in the unchanged Demo summary presentation; a new press only after Unavailable or BudgetLimited; three live regions, nothing automatic.
2. **The Calendar step rail** reserves a 20rem sticky track from 1280 (680px stay for the rows at 1280), so the one fill is always in view on desktop and can never cover a target; below 1280, at 400% zoom and on phones the step stays in the flow; DOM order, the skip link, the h2 target and all names are unchanged.
3. **The Demo-source notice** becomes a one-line Note with a "Details" button and the body hidden until opened, wording byte-identical; the Demo tag leaves the bar at every width; safety notes gain a state tint and notes under the bar sit 24px apart; the recording pill does not change.
4. Three file-disjoint slices (8b, 8c in parallel; then 8a) with the files, the test edits and their reasons, and acceptance criteria usable by frontend and QA.
5. G1 remains BLOCKED (PO-18c and PO-18d are risk acceptances, every disclosure string is DRAFT and says so on screen); G4 remains NOT_RUN; no source file was changed and no browser, server or test was run.

## Lead answers to the open questions (2026-10-31)
1. `DEMO_SOURCE_TAG` is retired from the lecture bar at every width (PO-18c: the tag leaves the bar on desktop as it already does on phones). The e2e visibility assertion for it is replaced by an assertion on the one-line Demo note. Wording byte-identical; recording status unchanged.
2. The notes tag reads **"Demo notes"** as a new DRAFT Demo word (the existing "Demo summary" stays on summaries); counsel-pending like all disclosure wording.
3. Yes: a reopened Stopped session reads the Final summary via `GET /summary` with `status: "Final"`; the backend fix pass must confirm that in a test.
