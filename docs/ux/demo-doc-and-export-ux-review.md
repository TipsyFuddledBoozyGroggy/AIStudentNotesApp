# UX review: Save to a Demo Doc (item 3), Export notes / Copy notes (item 2), delete screens wording

**Status: REVIEW with RECOMMENDATIONS. Nothing here is approved.** Author: `neurodivergent-ux-specialist`, 2026-11-05. Scope: finish-backlog items 2 and 3 (PO-20cb, PO-20cc: mock only, real Google OFF) and the built delete screens (PO-20by, PO-20bz). This document changes no app text and no code. Every user-facing string proposed below is a **recommendation for Product Owner approval as DRAFT wording**; my review moves the built strings from "DRAFT, unreviewed" to "reviewed", it does not clear G1 or G4, and it does not touch consent, recording or Demo-source wording (G1). Nothing is human-validated (G4 NOT_RUN); G3 (screen reader, real device) is NOT_RUN, so every screen-reader statement below is a design hypothesis with its reason, not a test result.

What I did: read-only review of the files listed in section 0. Commands run: none. Tests run: none. Line numbers are from the working tree at `2100828` (clean).

Design system: "reading room, second edition" (`docs/ux/redesign-round2.md`). Every control named here already exists (`.dialog` + `.dialog-scrim`, `.sheet`, `.note`, `.help`, `.btn-primary/secondary/tertiary`, `.link`, the `DocsDialog` and `ConfirmDialog` shells, the existing announcer). No new token, CSS, live region or primitive is proposed.

---

## 0. Files read

| File | What it is |
|---|---|
| `src/web/src/components/docs/copy.ts` | `DOCS_COPY`, item 3 strings (section 4 of the tasks doc, plus "derived") |
| `src/web/src/components/docs/DocsExport.tsx` | the dialog flow and every state transition |
| `src/web/src/components/docs/DocsDialog.tsx` | dialog shell: portal, `inert` sweep, focus, Tab trap |
| `src/web/src/components/docs/DemoDocPage.tsx`, `DocBlocks.tsx`, `DocsExportEntry.tsx` | the Demo Doc page, blocks as text, mount point |
| `src/web/src/components/docs/copy.test.ts`, `DocsExport.test.tsx` (part) | what is pinned and how the status region is read in tests |
| `src/web/src/components/lecture/copy.ts` | `NOTES_EXPORT_COPY` (item 2) and neighbours |
| `src/web/src/components/lecture/NotesPanel.tsx` | `NotesExport` rendering and the item 3 mount |
| `src/web/src/components/deleteCopy.ts`, `deleteFlow.ts`, `lecture/LectureDelete.tsx`, `lecture/LectureReopenPage.tsx` (part) | delete wording and flows |
| `src/web/src/components/syllabus/ConfirmDialog.tsx`, `lecture/Announcer.tsx`, `src/web/src/lib/copyLint.ts` | shared dialog, the three live regions, the lint |
| `docs/product/docs-export-tasks.md` section 4 and 10 | the DRAFT UI states this build follows |
| `docs/API_CONTRACTS_DOCS_EXPORT.md` 4.1-4.7, 5 | what the server guarantees (needed for every "nothing was saved" claim) |
| `docs/ux/delete-wording-draft.md`, `docs/ux/language-guide.md` 3.9 and 9 | the approved DRAFT delete strings and the copy rules |
| `docs/RELEASE_GATES.md`, `CLAUDE.md` invariants 7-9, `docs/DECISIONS.md` PO-20by, PO-20bz, PO-20cb, PO-20cc | gates and decisions quoted below |

---

## 1. Summary of decisions

| # | Question | Decision | Priority |
|---|---|---|---|
| 1 | Three lines name Google only to deny it | Keep the denial **once**, in the entry help line. Make the preview "Where" line and the page banner positive statements. | SHOULD |
| 2 | Retry line "That did not save. Nothing was saved and your notes are fine. You can try again." | "Nothing was saved" **can be false** after a lost answer. Split into two lines: one for a server-confirmed failure, one for an unknown outcome. Replace "your notes are fine" with the house phrase "not affected". | MUST |
| 3 | Strings marked "derived" | Reviewed one by one in section 5. Most are fine. Changes: `file.model` ("wrong"), `NOTES_EXPORT_COPY.help` and `.saved` (vague), `page.version` (technical), `page.deleteBody` (add the way forward). | SHOULD / NICE |
| 4 | Lecture delete should name Demo Docs | Yes. The student was told a Demo Doc is a separate copy with its own page; the delete body must name it. One unconditional phrase ("and any Demo Docs") avoids a state fetch. Course delete list likewise. | MUST (lecture), SHOULD (course) |
| 5 | 409 `invalid_state` on confirm shows the permanent dead end | Reload the preview and say what happened, as the 412 path already does. A second 409 in the same opening falls back to the permanent line. | MUST |
| 6 | Dialog `inert` sweep vs the announcer | Hypothesis with a spec reason: the `inert` sweep hides the status region from assistive technology while a dialog is open, so "Saving to a Demo Doc." and the delete-problem lines are probably silent. Focus movement already covers the result states. Fix by exempting the announcer regions from the sweep, not by adding a live region. Add to the G3 manual script. | SHOULD (code), MUST (G3 script item) |
| 7 | Cognitive load, calm tone, no pressure, no "!" | Passes the lint and the guide. Load is moderate; trims proposed. No exclamation marks anywhere. | SHOULD / NICE |

Open question for the PO (not a change): whether "Demo Doc" is the right noun when "Demo" on the same sheet already means "made by a test routine" (section 8).

---

## 2. Decision 1: the three lines that name Google

Built today (`docs/copy.ts:18, 27, 78`):

```
entry.help     "A Demo Doc is a practice copy kept inside Tether. Nothing is sent to Google."
preview.where  "Where: a Demo Doc (a practice copy inside Tether, not Google Docs)"
page.banner    "Demo Doc: a practice copy kept inside Tether. It is not in Google Docs."
```

The tasks doc contradicts itself: section 4 opens with "never 'Google Doc' or 'Google' anywhere in this flow" and then writes Google into three of its own strings. So the question is real and the build was right to flag it.

**Decision: keep one denial, in the entry help line; make the other two positive.**

Reasons:

1. The expectation exists and must be met once. The backlog item is literally "Export to Google Doc on a Demo Doc mock", and a student who has heard "export to a Doc" will read "Doc" as Google Docs. Saying "Nothing is sent to Google" at the point where the expectation forms (the button's help line) is a privacy fact the student is owed (invariant 8: graceful degradation stated honestly). It is also true: no Google client exists.
2. Repeating a negation three times is load, not clarity. Each "not Google Docs" makes the reader flip a statement, and it primes the very word it denies. By the preview and the page the student has already pressed a button called "Save to a Demo Doc"; what they need there is orientation ("where does this live?"), which a positive sentence gives in fewer words.
3. The G1 consent bullet (`lecture/copy.ts:49`) already names Google in the one place that is legally load-bearing. It is not touched.

Recommended replacement strings (PO approval as DRAFT):

```
entry.help     (unchanged) "A Demo Doc is a practice copy kept inside Tether. Nothing is sent to Google."
preview.where  "Where: a Demo Doc, a practice copy kept inside Tether."
page.banner    "Demo Doc: a practice copy kept inside Tether."
```

Side effects the Lead must schedule (not UX files): `docs/product/docs-export-tasks.md` section 4 lines for "Where" and "Banner" (copy.test.ts pins every string against that section's backticks), and the test "never names Google except in the three lines" (`copy.test.ts:89-93`) becomes "except in the entry help line".

---

## 3. Decision 2: the retry line and whether "Nothing was saved" can be false

Built (`docs/copy.ts:63`): `"That did not save. Nothing was saved and your notes are fine. You can try again."`

Shown by `DocsExport.tsx` in four situations (`send`, lines 120, 147, 150):

| Path | Server guarantee | "Nothing was saved" |
|---|---|---|
| a. `200` record, `status: failed`, retryable (`provider_unavailable`, `rate_limited`) | contract 4.3: savepoint rolled back, "nothing written" | **true** |
| b. `429` | contract 4.1 step 6: refused before the body is read | **true** |
| c. `status === 0` (no answer), `>= 500`, `malformed_response` | contract 4.3: the write is **one transaction committed before the response**; a lost or unreadable answer after the commit means the Demo Doc **exists** | **can be false** |

So yes: after a lost network response the line can be false, and the student will meet the contradiction. Press Try again and the same key replays the stored record (contract 4.4, "Same key and body after success: the original record with `Idempotency-Replayed: true`"), so the next screen says "Saved to a Demo Doc." Press Close instead and the next page load shows "Saved to a Demo Doc on Nov 5." under a button whose dialog said nothing was saved. For a student who relies on the app's statements to decide what to do next, a sentence that is sometimes false is worse than one that says "we do not know".

The same pattern is already handled honestly elsewhere: `runDelete` (`deleteFlow.ts:8-10`) says "A network failure or a 5xx does not prove the delete did not commit" and probes once; `BLOCKED_COPY.bodyUnconfirmed` says "We could not confirm that nothing was saved". The Demo Doc flow should match.

**Decision: split the line. Path a and b keep a "nothing was saved" line; path c gets an "unknown outcome" line that promises only what the key guarantees.** Also replace "your notes are fine": it is a reassurance word where the house phrase is a fact ("not affected", as in `page.deleteBody`, `SUMMARY_STATUS_COPY.failed`).

Recommended strings (PO approval as DRAFT):

```
retry.body      "That did not save. Nothing was saved, and your notes are not affected. You can try again."
retry.unknown   "We did not get an answer, so we could not confirm whether it saved. Your notes are not affected. You can try again. It will not be saved twice."
retry.wait      (unchanged in meaning; see 7.3 for the minutes form)
```

Why "It will not be saved twice" is a promise the code keeps: same key and body is a replay after success (contract 4.4) and the key is reused on Try again (F04, `DocsExport.tsx:22-27`). It stops being true after 24 hours (key pruning) or after the lecture is deleted (`404`), both of which end in other states.

Engineering follow-ups (frontend-engineer; not wording):

- SHOULD: on path c, before showing `retry.unknown`, read `getState(sessionId)` once; if `lastExport.status === "succeeded"` and `completedAt` is after the attempt began, treat it as success. This is the `runDelete` probe pattern and would make the unknown line rare. If the probe itself fails, show `retry.unknown`.
- SHOULD: when the dialog closes from `retry.unknown`, refetch state so the entry line ("Saved to a Demo Doc on ...") is current without a page reload.
- The announcer call in `settle` must use the same split.

Lint check by eye: no "lost", no "!", longest sentence 14 words. `copy.test.ts:73-78` pins the current departure text and must change with the string.

---

## 4. Decision 4: lecture delete must name Demo Docs

Tasks section 10: "If it mentions exports, it must say Demo Docs are deleted with the lecture." The built body (`deleteCopy.ts:14`) is `"This deletes the transcript, the summary, any recaps and the notes."` It does not mention exports, so the letter of section 10 is met. The spirit is not:

- The success screen told the student the Demo Doc is a separate copy with its own page and its own delete ("This copy stays inside Tether. It is deleted when you delete this lecture.", `docs/copy.ts:58`). Having set it up as a separate thing, the delete dialog must name it, or "the notes" has to carry a meaning the student was taught it does not have. Delete-draft rule 3 ("name what goes and what stays, in one short list") applies.
- It is true: contract 4.7, cascade through composite FKs in the same transaction; B04 is the test.

**Decision: add "and any Demo Docs" unconditionally.** "Any" keeps the sentence true when none exists and needs no state fetch on the lecture page (the delete button has no `getState` today). A conditional line would be slightly kinder but adds a request and a loading state to a destructive dialog; not worth it.

Recommended strings (these amend PO-20by-approved DRAFT text, so they need the PO again; they stay DRAFT):

```
LECTURE_DELETE_COPY.body
  "This deletes the transcript, the summary, any recaps, the notes and any Demo Docs."

COURSE_DELETE_COPY.lectures(n)
  n = 1: "1 lecture: transcript, summary, recaps, notes and any Demo Docs"
  n > 1: "{n} lectures: transcripts, summaries, recaps, notes and any Demo Docs"
```

Also to update when approved: `docs/ux/delete-wording-draft.md` 5.1 and 5.2a (my file; I have not edited it in this task because the task is one output file), `deleteCopy.test.ts` pins, and the 5.5 explainer gains nothing (its first sentence already says "removes all of its words").

Flag, not a change: `docs.audit_events` rows (content-free: action, counts, times) outlive the lecture (contract 4.7-4.8). The kept line "Tether keeps a record that a lecture happened ... and how much AI it used" arguably covers "a record that a Demo Doc was made and deleted". I would not lengthen the kept line for it. **Question for the PO** via the Lead: is "a record that a lecture happened" acceptable cover for the Demo Doc audit rows, or should the line say "and whether a Demo Doc was made"? This sits with the open L-14 / PO-20ca retention question.

---

## 5. Decision 3: every string marked "derived"

### 5.1 `DOCS_COPY` (`docs/copy.ts`)

| Key | Text | Verdict |
|---|---|---|
| `entry.savedUndated` | "Saved to a Demo Doc." | Reviewed OK. Same words as `success.created`, which is fine on the entry line. |
| `preview.count` singular | "1 section, 1 bullet. Each bullet keeps its lecture time." | Reviewed OK. |
| `preview.regionLabel` | "What will be saved" | Reviewed OK as the accessible name of a focusable scroll region. |
| `page.deleteConfirm` | "Delete Demo Doc" | Reviewed OK; matches "Delete lecture", "Delete syllabus". |
| `page.deleted` | "The Demo Doc was deleted." | Reviewed OK. |
| `page.loading` | "Getting this Demo Doc." | Reviewed OK. |
| `page.notFound` | "We can't find that Demo Doc" | Reviewed OK (h1 pattern, no period, as the lecture page). |
| `page.loadError` | "We couldn't load this Demo Doc" | Wording OK. NICE: offer "Try again" beside "Back to dashboard" (the lecture equivalent does not either; a load error with no retry is a dead end). |
| `page.version(n, date)` | "Version 2, saved Nov 5." | "Version" is a technical count. NICE: `n = 1` "Saved {date}."; `n > 1` "Updated {date} (version {n})." The number stays visible so a student who chose Update can see it took. |
| `page.deleteBody` | "Your notes and lecture are not affected." | OK (section 4 text, split). NICE: add the way forward so the delete is not read as final: "Your notes and lecture are not affected. You can save a new Demo Doc from the notes later." Do **not** add "It can't be undone." here: true, but the copy can be remade in one press, so it would be alarm without information. |
| `retry.body` | see section 3 | MUST, section 3. |

### 5.2 `NOTES_EXPORT_COPY` (`lecture/copy.ts:271-295`), all new DRAFT

| Key | Text | Verdict |
|---|---|---|
| `help` | "Export notes saves them as a Markdown file on this device, with their times. Copy notes puts the same text on your clipboard. Nothing is sent anywhere." | "with their times" is ambiguous (times of what?). "Markdown" is jargon but honest (the file is `.md`). SHOULD: "Export notes saves a text file (Markdown) on this device, with the lecture times. Copy notes puts the same text on your clipboard. Nothing is sent anywhere." "Nothing is sent anywhere" is true: both act on client data only (`NotesPanel.tsx:31-33`). |
| `saved` | "Your notes file is ready." | Vague about where the file went; a student may look for it on screen. The browser decides the folder, so do not name one. SHOULD: "Your notes were saved as a file on this device." |
| `copied` | "Notes copied. You can paste them where you like." | Reviewed OK. |
| `saveFailed` | "This browser did not save the file. You can use Copy notes instead." | Reviewed OK: fact, then one next action. |
| `copyFailed` | "This browser did not allow copying. You can use Export notes instead." | Reviewed OK. |
| `file.untitled`, `course`, `date`, `covers`, `truncated`, `times`, `summaryHeading`, `summaryTimes`, `summaryNotMade`, `summaryNotLoaded`, `notesHeading`, `local` | | Reviewed OK. `local` ("Nothing was sent anywhere.") is true. |
| `file.model` | "AI-estimated notes: written by an AI model from the transcript. They may be wrong, so check them against the transcript." | "wrong" is on the language guide checklist ban list (section 9 item 2) though not in the lint. SHOULD: "AI-estimated notes: written by an AI model from the transcript. They are estimates, so check them against the transcript." |
| `file.mock` | "Demo notes: Made by a test routine, not an AI model. Treat them as estimates and check them against the transcript." | Reuses the Demo notes tag (which `NOTES_COPY` marks counsel-pending) and **adds** a second sentence. The addition is not Demo-source wording, but it sits inside a Demo line. **Not changed here; flagged for the counsel/G1 pass.** |

### 5.3 `deleteCopy.ts` derived strings

| Key | Text | Verdict |
|---|---|---|
| `COURSE_DELETE_COPY.bodyEmpty` | "This deletes the course." | Reviewed OK. |
| `COURSE_DELETE_COPY.lectures(1)` | "1 lecture: transcript, summary, recaps and notes" | OK as a singular; amend per section 4. |
| `COURSE_DELETE_COPY.syllabi(1)` | "1 syllabus: the text, the items and your decisions" | Reviewed OK. |
| `COURSE_DELETE_COPY.gone` | "This course is no longer here. It may have been deleted in another tab." | Reviewed OK. |
| `DELETE_PROBLEM_COPY.generic` | "That did not work. Nothing was deleted." | True for every path that reaches it (`runDelete`: a 4xx other than 404/401/409/429, or a non-`ApiError` thrown before a 204). Reviewed OK. |

---

## 6. Decision 5: 409 `invalid_state` on confirm

Today `send` (`DocsExport.tsx:136-151`) maps a `409` whose code is neither `retry_limit_reached` nor `export_in_progress` to `permanent`: "This Demo Doc could not be saved. Your notes are fine. You can still use Export notes or Copy notes."

What a `409 invalid_state` on confirm actually means (contract 4.1 step 9, in order): (a) session not Stopped; (b) wrong notes id is a 404, not this; (c) notes not Ready; (f) the `mode` is no longer allowed, which from this UI means **the lecture's Demo Doc changed under the student** (another tab created one, so `create` is now `update`/`create_another`; or the one they were updating was deleted); (g) the 200-document cap. Steps a and c cannot happen from the stopped lecture page. "A failed check stores nothing and leaves the key free."

So in practice the 409 the student can meet is "the state moved", exactly the situation the 412 path already handles well: reload the preview, say so, let the student decide again (F11). Sending them to Export notes instead is a dead end that abandons their chosen action for no reason, and "could not be saved" implies a permanence that is not there.

**Decision: on the first `409 invalid_state` in one dialog opening, reload the preview (as 412 does) with a state-change line; on a second `409 invalid_state` in the same opening, show `permanent`.** The second-strike rule ends the loop for the cap case (g), where the preview would otherwise keep offering `create`.

`C.stale` ("The notes changed while you were looking ...") is the right line for 412(d) and close enough for 412(e). It is not right for 409(f), where the notes did not change. Recommended new string (PO approval as DRAFT):

```
changedState  "This lecture's Demo Doc changed while you were looking. Here is the current version. Nothing was saved yet."
```

Same rendering as the stale note (`.note note-notice`, focus to it, one status announcement).

---

## 7. Decision 6: the dialog `inert` sweep and the announcer

### 7.1 What the code does

`DocsDialog.tsx:30-41` (and `ConfirmDialog.tsx:35-47`, `ConsentDialog.tsx`) portal the dialog to `<body>` and set `inert` on every other child of `<body>`. The Next.js app root is one of those children, and inside it live the only three allowed live regions (`Announcer.tsx:65-70`: `role="status"` and `role="alert"`, plus the finals region in the transcript log). While a dialog is open, `announcer.status(...)` therefore writes into a region that is inside an `inert` subtree.

### 7.2 Why that matters (hypothesis, spec-based, not tested)

The HTML `inert` attribute tells the user agent to ignore the subtree for focus, hit-testing, find-in-page **and the accessibility tree**; Chromium removes inert nodes from the tree it exposes to screen readers. A live region that is not in the accessibility tree does not fire live announcements. If that holds on the real devices, these calls are silent while the dialog is up:

| Call | Where | Covered another way? |
|---|---|---|
| `C.saving.announce` "Saving to a Demo Doc." | `DocsExport.tsx:130` | Partly: focus is on the Save button, whose name becomes "Saving..." with `aria-busy`. Some screen readers announce a focused control's name change, some do not. |
| success / retry / permanent / notAvailable / lastAttempt / inProgress | `settle`, `DocsExport.tsx:107-111` | **Yes**: `settle` moves focus to the message paragraph (`tabIndex=-1`), which screen readers read on focus. The live region is redundant here. |
| `C.stale` | `loadPreview(true)` | Yes, same focus move. |
| delete problem lines | `LectureDelete.tsx:45`, `DemoDocPage.tsx:133` | **No**: the text is added to the dialog's `aria-describedby` content and focus stays on the confirm button. A description is read on focus, not on change. |

The unit tests cannot see this: `DocsExport.test.tsx:78` reads `textContent` of the status region, and jsdom does not implement `inert` semantics, so the test passes whether or not a human hears anything.

### 7.3 Recommendation

- Do **not** add a live region inside the dialog (playbook 4: exactly three live regions). 
- SHOULD (frontend-engineer): make the announcer's two regions siblings of the dialog at `<body>` level (portal them from `AnnouncerProvider`, or render them with a marker attribute) and have all three `inert` sweeps skip elements carrying that marker. Add a unit test: "while a dialog is open, `[data-region="status"]` has no `[inert]` ancestor". The regions are `sr-only` and not landmarks, so the 12-landmark geometry gate is unaffected; confirm with the existing geometry test.
- SHOULD (frontend-engineer): for the two delete dialogs, move focus to the problem paragraph (`tabIndex=-1`) when it appears, as `settle` does, so the line is heard even if the region fix waits. Delete-draft 6.3 says "focus stays on the confirm button"; I amend that advice: focus the problem line, and keep the confirm button next in Tab order.
- MUST (device-accessibility-validation, G3 script): add "open Save to a Demo Doc, press Save, confirm that 'Saving to a Demo Doc.' and the result are spoken (NVDA + Chrome, NVDA + Firefox, VoiceOver + Safari)"; and "Delete this lecture with the API stopped: confirm the problem line is spoken". Until that is run, the claim in `DocsExport.tsx:48` ("Results are said once through the existing status region") is a hypothesis.

### 7.4 Other interaction notes on the dialog (reviewed OK unless marked)

- Focus on open goes to the heading (F12), not a button: right for a non-destructive dialog where the first job is reading.
- Tab trap includes the `tabIndex=0` preview region, so a keyboard user can scroll the blocks (WCAG 2.1.1). Radios drop out of the order while saving (`fieldset disabled`); Save and Not now stay focusable with `aria-disabled` so they remain discoverable.
- Escape is ignored while saving; the same rule as the delete dialogs. Acceptable: one transaction, a few hundred milliseconds in the demo.
- 44px targets: `.btn` buttons; the radio labels have `min-h-[44px]`.
- Reduced motion / quiet mode: "Getting your preview..." is text, no spinner; the scrim fade is token-governed. OK.
- The dialog heading becomes "Save to a Demo Doc" (the button's label) in result states (`DocsExport.tsx:395`). Reading "Save to a Demo Doc" above "Saved to a Demo Doc." is slightly odd. NICE: a result heading such as "Demo Doc" for non-preview states; low priority.

---

## 8. Decision 7: cognitive load, calm tone, no pressure, no exclamation

Checked against `copyLint.ts` FORBIDDEN by eye and the language guide section 9 checklist.

- Exclamation marks: none in `DOCS_COPY`, `NOTES_EXPORT_COPY` or `deleteCopy.ts`. The lint test enforces it for `DOCS_COPY`.
- Pressure: none. `retry.wait` is advice, not a lock, and there is no countdown (section 4 rule kept). No streaks, no timers, no auto-close.
- Tone: every error says what is true and what changed (checklist 7), except the retry line fixed in section 3. Subjects are the app, the file or the student's action.
- "With him, not on him" (pilot framing): nothing in these flows reports to anyone; every write is the student's own press after a preview; the only persistent trace is content-free audit rows. Consistent.

Load, where it can come down (all SHOULD or NICE):

1. Preview dialog has up to six lines of text before the content (line1, where, title, count, Demo tag, changed line). Keep them, but the "Where" line shortens per section 2 and the count line can follow the title directly. The changed line plus a two-option radio group is the heaviest state; it is also the one that most needs the student to stop and choose, so I would not trim it.
2. `retry.wait(n)`: "Please wait about {n} seconds, then try again." With `rate_limited` capped at 3600 the student could read "about 3600 seconds". SHOULD: match the existing rate line ("One moment. Try again in about a minute.") and format: `n < 90` "Try again in about {n} seconds."; otherwise "Try again in about {m} minutes." Drops the "Please", which the guide treats as an exception, not the default.
3. `lastAttempt.body`: "We stopped trying after a few attempts" reads as if the app retried on its own; the student pressed Try again up to five times. SHOULD: "This save did not go through after several tries. Your notes are not affected. You can start again from the preview, or use Export notes."
4. `permanent`: "Your notes are fine" to "Your notes are not affected." for the house phrase (SHOULD).
5. Two help paragraphs sit under the three output buttons (Export notes / Copy notes, then Save to a Demo Doc) and both say a version of "nothing is sent". That is deliberate (two different truths: "nowhere" versus "not to Google") and acceptable. NICE: if the PO later finds the sheet busy, the Demo Doc help could become a `Disclosure` "What is a Demo Doc?", but not before the PO has used it.

The name "Demo Doc": on the same Notes sheet, "Demo notes: Made by a test routine, not an AI model." uses "Demo" to mean *fake content*, while "Save to a Demo Doc" uses "Demo" to mean *practice destination for real content*. A student could read "Demo Doc" as "a Doc with fake notes in it". The contract fixes the label as a constant ("Demo Doc", contract section 7) and the tasks doc P3 already asks the PO. **Question for the PO** (via the Lead; no recommendation to change the code until answered): keep "Demo Doc", or use "Practice Doc" so "Demo" keeps its one meaning (test-routine output)? I lean to "Practice Doc" for the one-meaning reason, but it is a product noun and the PO's call; the Demo-source and Demo-notes wording themselves are G1 and are not in question.

---

## 9. Exact recommended replacement strings (all DRAFT, for PO approval)

Only changed or new strings are listed. Everything not listed stays as built.

```
DOCS_COPY
  preview.where      "Where: a Demo Doc, a practice copy kept inside Tether."
  page.banner        "Demo Doc: a practice copy kept inside Tether."
  retry.body         "That did not save. Nothing was saved, and your notes are not affected. You can try again."
  retry.unknown      "We did not get an answer, so we could not confirm whether it saved. Your notes are not affected. You can try again. It will not be saved twice."   (new key; status 0, >= 500, malformed_response)
  retry.wait(n)      n < 90:  "Try again in about {n} seconds."
                     n >= 90: "Try again in about {m} minutes."
  changedState       "This lecture's Demo Doc changed while you were looking. Here is the current version. Nothing was saved yet."   (new key; first 409 invalid_state on confirm)
  lastAttempt.body   "This save did not go through after several tries. Your notes are not affected. You can start again from the preview, or use Export notes."
  permanent          "This Demo Doc could not be saved. Your notes are not affected. You can still use Export notes or Copy notes."
  page.deleteBody    "Your notes and lecture are not affected. You can save a new Demo Doc from the notes later."   (NICE)
  page.version       n = 1: "Saved {date}."   n > 1: "Updated {date} (version {n})."   (NICE)

NOTES_EXPORT_COPY
  help               "Export notes saves a text file (Markdown) on this device, with the lecture times. Copy notes puts the same text on your clipboard. Nothing is sent anywhere."
  saved              "Your notes were saved as a file on this device."
  file.model         "AI-estimated notes: written by an AI model from the transcript. They are estimates, so check them against the transcript."

LECTURE_DELETE_COPY
  body               "This deletes the transcript, the summary, any recaps, the notes and any Demo Docs."

COURSE_DELETE_COPY
  lectures(1)        "1 lecture: transcript, summary, recaps, notes and any Demo Docs"
  lectures(n)        "{n} lectures: transcripts, summaries, recaps, notes and any Demo Docs"
```

Lint by eye: no "!", no "lost", "missed", "behind", "just", "right now", "wrong", "failed"; no sentence over 20 words (longest: 14). All strings must land in their registries and pass `copy.test.ts` / `deleteCopy.test.ts`; the pins against `docs-export-tasks.md` section 4 and `delete-wording-draft.md` must be updated by their owners in the same change.

Not touched, by rule: `CONSENT_COPY`, `CONSENT_DIALOG_COPY`, `DEMO_NOTICE_COPY`, `DEMO_SOURCE_TAG`, `NOTES_COPY.intro/whatIsSentBody/demoTag`, `DEMO_SUMMARY_COPY`, `NOTES_EXPORT_COPY.file.mock` (G1 or counsel-pending).

---

## 10. Prioritised change list

**MUST (truth, dead ends, honesty about deletion):**

| # | Change | Owner | Needs |
|---|---|---|---|
| M1 | Split the retry line: `retry.body` for server-confirmed failures (200-failed, 429), new `retry.unknown` for no answer / 5xx / unreadable answer (section 3). | frontend-engineer (code), PO (wording as DRAFT) | PO approval of both strings; `copy.test.ts:73-78` and tasks section 4 updated by the Lead |
| M2 | Lecture delete body names Demo Docs: "... the notes and any Demo Docs." (section 4). | PO (amends PO-20by DRAFT), frontend-engineer | PO approval; `deleteCopy.test.ts` pin; `delete-wording-draft.md` 5.1 |
| M3 | First `409 invalid_state` on confirm reloads the preview with `changedState`; second one in the same opening shows `permanent` (section 6). | frontend-engineer, PO (wording) | PO approval of `changedState`; a unit test for both strikes |
| M4 | G3 manual script gains: dialog-open announcements (saving, result) and the delete-problem line, NVDA/Chrome, NVDA/Firefox, VoiceOver/Safari (section 7.3). | device-accessibility-validation | none; it is a plan item |

**SHOULD (clarity, consistency, likely screen-reader gap):**

| # | Change | Owner |
|---|---|---|
| S1 | One Google denial only: `preview.where` and `page.banner` become positive (section 2). Update tasks section 4 and the "never names Google" test. | PO (wording), Lead (tasks doc), frontend-engineer |
| S2 | Announcer regions exempt from the `inert` sweeps in `DocsDialog`, `ConfirmDialog`, `ConsentDialog`; unit test that the status region has no `[inert]` ancestor while a dialog is open (section 7.3). | frontend-engineer |
| S3 | Delete dialogs focus the problem line when it appears (section 7.3). | frontend-engineer |
| S4 | Probe `getState` once on path c before showing `retry.unknown`; refetch state on close from that view (section 3). | frontend-engineer |
| S5 | "Your notes are fine" to "Your notes are not affected." in `lastAttempt.body` and `permanent`; `lastAttempt.body` reworded so it does not imply auto-retry (section 8). | PO (wording) |
| S6 | `retry.wait` as "Try again in about {n} seconds." with a minutes form at 90 s and above (section 8). | PO (wording), frontend-engineer |
| S7 | Course delete lecture lines add "notes and any Demo Docs" (section 4). | PO (amends PO-20by DRAFT) |
| S8 | `NOTES_EXPORT_COPY.help`, `.saved`, `.file.model` per section 5.2. | PO (wording) |

**NICE:**

| # | Change |
|---|---|
| N1 | `page.version`: "Saved {date}." / "Updated {date} (version {n})." |
| N2 | `page.deleteBody` adds "You can save a new Demo Doc from the notes later." |
| N3 | "Try again" on the Demo Doc page load-error state. |
| N4 | A result-state dialog heading that is not the button's label. |

---

## 11. Interaction and accessibility acceptance checklist (for QA and G3; not a pass record)

Save to a Demo Doc dialog:

1. Opening moves focus to the dialog heading; closing (Not now, Close, Escape) returns it to "Save to a Demo Doc".
2. Tab and Shift+Tab stay inside; the preview region is reachable and scrollable by keyboard.
3. Nothing is written before Save; the preview shows no "saved" words (F07).
4. With an earlier Demo Doc and changed notes, Save is `aria-disabled` and described by "Choose one to continue." until a radio is chosen.
5. While saving: Save reads "Saving...", is `aria-busy`, a second press sends nothing; Escape does nothing.
6. Each result moves focus to its message; the message states what is true and whether anything was saved.
7. A lost answer never produces the words "Nothing was saved" (M1).
8. A first `409 invalid_state` shows a fresh preview with the state-change line, not the permanent line (M3).
9. Screen reader hears "Saving to a Demo Doc." and the result while the dialog is open (hypothesis to test under G3; M4, S2).
10. No new `aria-live`, `role="status"` or `role="alert"` anywhere in `components/docs`.
11. 44px targets on every button, link and radio row; five themes x light/dark; quiet mode and low-stimulation unchanged; no motion required.
12. Google is named at most once, in the entry help line (S1).

Demo Doc page:

13. Banner is a `.note`, not dismissible; title and blocks render hostile text as literal text (F16, F18).
14. "Delete this Demo Doc" opens the shared `ConfirmDialog` with "Keep it" filled and focused; after delete, focus lands on "The Demo Doc was deleted." and a Back link follows.
15. Foreign, missing or malformed id gives the same calm not-found.

Delete dialogs (lecture, course):

16. Body names Demo Docs (M2, S7); kept line unchanged; "It can't be undone." present.
17. A problem line is focused when it appears (S3) and is spoken (G3).

Export notes / Copy notes:

18. Both act only on a press; the result is one line of text; a failure line takes focus and names the other option.

---

## 12. What this document does not do

- It edits no file under `src/`, no copy registry, no test, and not `docs/product/docs-export-tasks.md` or `docs/ux/delete-wording-draft.md` (the latter is mine; the amendments in section 4 go in once the PO answers).
- It does not clear G1, G3 or G4, and it is not a PO decision. Every string in section 9 is a recommendation for PO approval as DRAFT wording.
- It does not change, and did not read for change, any consent, recording or Demo-source string.
- It ran no tests and no commands; the `inert`/live-region finding is a hypothesis from the HTML specification and the code as read, to be confirmed under G3.
