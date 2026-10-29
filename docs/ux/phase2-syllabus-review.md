# Phase 2 UX: Syllabus Import, Student Review, Mock Calendar Sync

**This is an AI specialist design (adhd-ux-specialist), 2026-10-28. It is NOT human ADHD-student, clinician or screen-reader-user validation.** Layouts, thresholds, defaults and copy are design hypotheses. Nothing here is prototyped, built, rendered or tested. Gate "ADHD suitability and learning-science effectiveness" stays NOT HUMAN-VALIDATED. All copy is draft and needs the plain-language check plus human review. Privacy and retention wording is draft pending Security.

## 0. Scope, assumptions, dependencies

Sources read: `docs/PRODUCT_REQUIREMENTS.md` section 6 (syllabus bullet, line 135), `docs/ux/student-experience.md` (rules, WCAG, saving/resuming), `docs/ux/m2-live-lecture-screens.md` (live-region architecture, shortcuts, demo tag pattern), `docs/ux/m2-quick-recap-review.md` (copy and focus lessons), `src/web/src/components/CourseDetailPage.tsx` and `CourseDetailView.tsx` (course page layout: `h1` with focus on load, `card` sections with an `h2`, `btn-primary` / `btn-secondary`, `space-y-6`).

Scope: syllabus upload or paste, extraction processing state, student review of extracted items, and a **mock** Calendar sync. No Google connection and no external write exist in this increment. Everything the student sees about sync must say it is a demo.

Assumptions
- Extraction is done by a mock or Claude-backed service behind a boundary the Lead Architect defines. The UI only needs: a syllabus record with a status, and a list of items. Item fields assumed: `id`, `title`, `type`, `date` (nullable), `time` (nullable), `confidence`, `sourceSnippet`, `sourceLocation` (page or null), `decision`, `syncState`.
- The PRD rule applies: uncertain or unknown dates stay "needs date"; nothing goes to a calendar until the student approves (item by item or in a batch); sync is idempotent; edit and undo exist.
- Syllabus content is untrusted text. The UI renders extracted titles and snippets as plain text only (no HTML, no auto-links, no instructions in the text acted on).
- Quiet mode is the only mode assumed. This feature adds no notifications, nudges, counters of unfinished work or reminders unless the student asks.

Dependent teams: Frontend (components), Backend (syllabus and item endpoints, idempotent sync key), AI (extraction schema, confidence, snippets), Google-integration (later: real Calendar), Security (retention, upload limits, disclosure copy), QA (section 12 criteria), Learning Science (none now).

Questions for the Lead Architect / Product Owner are in section 13.

## 1. Cognitive-load rules for this feature

1. One primary action per screen. Upload: "Read my syllabus". Review: "Add approved items to the calendar". Confirm: "Add to demo calendar".
2. Nothing is pre-approved. The default decision is "Not decided". The default state never triggers anything.
3. Show one group at a time by default. A student sees a week's items, not 30 rows.
4. Never shame or pressure: no "overdue", "you still have 12 undecided", red counts, deadlines on the review screen itself, or time-to-finish estimates.
5. "Needs a date" is a normal state, not an error. It is findable and calm.
6. Every decision is reversible until sync, in one control, in the same place.
7. Nothing is lost when the student leaves: decisions save as they are made; edits in progress are kept as drafts.
8. Plain words for confidence. No percentages in the default view.

## 2. Entry point from a course

Placement: a second `card` section on the course page, below "Lecture" and above "Back to dashboard". Same markup pattern as the Lecture card: `section` + `h2` + one sentence + one button. Route: `/courses/{id}/syllabus`.

```
Course page (Biology 101)
+--------------------------------------------------+
| Syllabus                                  (h2)   |
| Pull exam and assignment dates out of your       |
| syllabus. You check each one before anything     |
| is added to a calendar.                          |
| [ Add your syllabus ]                            |
+--------------------------------------------------+
```

State variants of the card (one sentence + at most one button; same position):

| Syllabus state | Sentence | Button |
|---|---|---|
| None | "Pull exam and assignment dates out of your syllabus. You check each one before anything goes on a calendar." | Add your syllabus |
| Reading | "We're reading your syllabus. You can leave. It will be here when you come back." | See progress |
| Ready, nothing decided | "We found 24 items. Ready when you are." | Review items |
| Partly decided | "You've decided on 9 of 24 items. Pick up where you left off." | Continue review |
| All decided, not synced | "All items are decided. Ready to add the approved ones to the demo calendar?" | Review and add |
| Synced (demo) | "12 items added to the demo calendar. Nothing was sent to Google." | View items |
| Failed / unreadable | "We couldn't read this file. You can try another one." | Try again |

Rules
- No badge, dot or count on the dashboard. The card appears only on the course page. Dashboard "next step" is not changed by this feature unless Product decides otherwise (question 7).
- "Welcome back" resume text is a plain sentence, dismissible, not a toast.
- Focus: navigating from the card lands on the target page `h1` (`tabindex="-1"`, focus on load, same as `CourseDetailView`).

## 3. Upload / paste flow

One screen. Two ways in, shown as a simple choice, not a wizard.

```
h1  Add your syllabus
p   Biology 101. We'll find dates for exams, assignments and readings.
    You'll check everything before it goes anywhere.

[DEMO tag] Demo mode: items are found by a test routine, not a real AI model. (shown while the mock extractor is active)

Choose how:   (o) Upload a file   ( ) Paste text        (radio group "How to add your syllabus")

 Upload:  [ Choose a file ]   PDF, Word or plain text. Up to 10 MB.   (limit is a proposal, see Q3)
          or drop a file here (optional; never the only way)
          Selected: BIO101-syllabus.pdf (2.1 MB)  [ Remove ]
 Paste:   Label "Syllabus text"  [ multi-line, resizable, 12 rows ]   "Paste as much as you have."

First day of classes (optional)  [ date ]   "Helps us turn 'Week 5' into a date. Skip it if unsure."

Privacy note (DRAFT, Security to approve):
 "Your syllabus text is read by [service names]. You can delete it any time on the next page."

[ Read my syllabus ]     [ Cancel ]
```

Behavior
- File input is a native `<input type="file">` with a visible label and a visible selected-file line. Drag and drop is an optional extra (SC 2.5.7).
- "Read my syllabus" is `aria-disabled` (still focusable) with hint "Choose a file or paste text first" until there is input. Not native `disabled`.
- The "First day of classes" field is optional, collapsed under "Add options" by default (rule: optional fields collapsed). If entered, relative weeks ("Week 5") can be resolved and shown as items needing a quick check, never silently trusted.
- Validate file type and size on selection, not on submit. Error appears beside the field with `aria-describedby` (copy in section 8).
- Pasted text draft autosaves locally (debounced); returning restores it with "We kept what you pasted."
- Press: the form posts once; the button becomes "Reading..." (`aria-disabled`, text only) and the page moves to the processing state. A double press does not create two syllabi (idempotency key per submit).
- Only one active syllabus per course in this increment. If one exists, the page shows "You already have a syllabus for this course" with "Replace it" (confirm: "Replace your syllabus? Your decisions on the old items will be removed. Nothing already added to the demo calendar changes.") and "Keep it". See Q6.

> **SUPERSEDED by docs/API_CONTRACTS_P2.md (P2-30, 2026-10-28; Lead decision, provisional where PO-owned):** The Replace flow is superseded: several syllabi per course are allowed (proposal 5 per course, 50 per user). "Replace" becomes add another syllabus, then delete the old one (contract row 5 of section 13). The `upload.replace.confirm` copy below is not used. The text above is left as written for history and is not the current requirement.

Keyboard order: `h1` (focus) -> method radio group -> file/paste control -> optional options disclosure -> privacy note -> Read my syllabus -> Cancel.

## 4. Processing state (calm, can leave and return)

Design hypothesis: indeterminate spinners and fake percentages create waiting anxiety. Use plain text about what is happening and what the student can do. No percentage, no countdown, no elapsed timer, no animation.

```
h1  Reading your syllabus
p   We're looking for dates and deadlines.
p   You can leave this page. We'll keep going and it will be waiting for you.
    [ Back to Biology 101 ]
Step in words (text only, updates in place):  "Step 2 of 3: finding dates"
   1. Got your file    2. Finding dates    3. Getting items ready
p (after about 20 s) "This one is taking a bit longer. Nothing is wrong."   (threshold is a proposal)
```

Behavior
- Steps are plain text with a done marker (check icon + word "Done") for finished ones. Not a progress bar.
- Polling or push is hidden. The page does not flash or reflow when status changes; only the step text changes in place.
- Screen reader: on arrival focus is on the `h1`. The status region announces "Reading your syllabus." once. On completion the status region says "Your items are ready." once, and focus does NOT move (the student may be elsewhere in the page). A button "Review items" appears in the same place as "Back to Biology 101", which stays.
- If the student left: the course card shows "Ready to review" (section 2). No push notification, email or sound by default. A student who wants one can enable "Tell me when my syllabus is ready" in settings (off by default; delivery channel is a Product decision, Q8).
- Cancel: "Stop reading" link (secondary). Copy: "Stop reading this syllabus? Nothing is saved from it." Confirm / Keep going.
- Slow or stuck: after the proposed ceiling (Q3) move to the failed state with "Try again" (section 8). Do not retry silently in a loop.
- Offline: "You're offline. We'll keep trying when you're back. Your file is saved here." only if true; otherwise "You're offline. Choose your file again when you're back."

## 5. Review screen

### 5.1 Structure

```
h1  Review your syllabus items
[DEMO tag, non-dismissible]  Demo mode: nothing here goes to Google Calendar.

Summary (text, one line):  We found 24 items.  9 approved · 3 skipped · 12 not decided
Note (only if any): 3 need a date. [ Show them ]          (a link-button, not an alert)

Group by:  (o) Week   ( ) Type            [ Review one at a time ]       (radio group + button)

Week 1 (Aug 25-31)  · 3 items · 3 decided                    [v Collapse]
  [item rows]
Week 2 (Sep 1-7)    · 2 items · 1 not decided                [> Expand]
...
Needs a date · 3 items                                       [> Expand]
  ...

(sticky summary/action bar)
  9 approved   [ Add 9 approved items to the demo calendar ]
```

- Real calendar weeks for groups; label "Week 3 (Sep 8-14)" using dates, because "Week 3" means different things to different syllabi. Items with no date go into the final group "Needs a date".
- Group by Type: Exams, Assignments, Quizzes, Readings, Projects, Other, Needs a date. Same row design. Grouping choice is remembered per viewer (local setting).
- Default expansion: the first group that has any "Not decided" items is expanded; all others collapsed with a one-line summary of counts. "Expand all" / "Collapse all" button provided. Expand state is not announced beyond `aria-expanded`.
- Groups are `h2` buttons in disclosure pattern: heading containing a `<button aria-expanded aria-controls>`. Count text is part of the button's accessible name ("Week 2, 2 items, 1 not decided").
- Up to about 30 items is the design target. Beyond 40 items show a note at the top: "This is a long list. Taking it one week at a time can help." with no other change.
- Past dates: item shows "Date has passed" text (neutral, no color alarm), still decidable. Group under their real week.

### 5.2 Item row (default, collapsed detail)

Desktop/tablet wide: single row, grid. Phone: stacked card (section 10).

```
[ ] Midterm exam                      Exam      Tue, Oct 14 · 10:00 AM     Probably right     [Approve] [Skip] [Edit]
    From your syllabus: "Midterm exam, Tuesday October 14 in class"  (shown on request; always shown if confidence is "Please check")
```

Fields and treatment
- Title: plain text, `break-words`, max 2 lines then "Show full title" is not needed; wraps.
- Type: text chip ("Exam"). Never color-only.
- Date and time: formatted text with weekday ("Tue, Oct 14"), time or "All day". Time zone line once at the top of the review: "Times are in your time zone: America/New_York." with "Change" link (Q5).
- Confidence: a text label with a shape icon, three levels. Wording avoids the number/percent in default view:
  - High: "Looks right" (check icon)
  - Medium: "Probably right" (circle icon)
  - Low: "Please check" (question icon)
  Raw value (for example 0.62) is available under "Details" for those who want it, labeled "How sure we are: about 60 in 100". Tooltip-only information is never used.
- Source snippet: progressive disclosure. A button "See where this came from" (`aria-expanded`) reveals the quote with page number when known: `From your syllabus, page 3: "..."`. Low-confidence rows have it open by default. The quote is plain text, max about 200 characters with "Show more" if longer; the match words may be bold but the meaning never relies on bold.

> **SUPERSEDED by docs/API_CONTRACTS_P2.md (P2-30, 2026-10-28; Lead decision, provisional where PO-owned):** "See where this came from" shows the server-sliced snippet with its context window (`snippet.context`); there is no "view in document" and none will be added because the original is not retained (contract row 27). After the retention purge `snippet.text` and `snippet.context` are null and the UI says the original wording was removed on schedule (4, 5.3). The snippet and context are untrusted plain text, and batch approve shows both for every selected item. The text above is left as written for history and is not the current requirement.
- Decision status text: "Not decided" (no text styling beyond normal), "Approved" (check icon + word), "Skipped" (dash icon + word). Status is text first.
- Per-row actions: Approve, Skip, Edit. Buttons are at least 44px high, 48px on phone. Each has a unique accessible name: "Approve Midterm exam", "Skip Midterm exam", "Edit Midterm exam".

### 5.3 Decisions: approve, skip, undo

- Approve: sets "Approved". The Approve button is replaced in the same position by "Undo" (its name: "Undo approval of Midterm exam"). Skip is replaced the same way.
- Skip: sets "Skipped". Skipped items are collapsed visually to one line (still present, not hidden) with "Undo".
- Focus: stays on the control that was pressed; since the control becomes "Undo" in the same position, focus is not lost. A single polite status message: "Approved: Midterm exam." No count updates are announced.
- Undo has no time limit. It returns the item to "Not decided". There is no auto-expiring toast.
- "Not decided" items can be left that way. Leaving the screen with undecided items is fine and not warned about. They are simply not synced.
- Needs-date items cannot be approved until they have a date. Their actions are "Add date", "Keep without a date" and "Skip". "Keep without a date" saves it to the course list in Tether only (never sent to a calendar). Whether Tether has a list to hold it is Q9; until then offer only "Add date" and "Skip".

### 5.4 Batch decisions (optional, never default)

- Per group: a secondary button "Approve the clear ones in this week (2)". It only applies to items that are dated AND "Looks right" AND not already decided. The count is in the label. Items marked "Probably right" or "Please check" are never included.
- Whole screen: "Approve all clear items (14)" in an overflow menu "More ways to decide". Pressing opens an inline summary, not a modal: "This will approve 14 items that have a date and look right. You can undo it." with `Approve 14 items` / `Cancel`. The result says "Approved 14 items. Undo this." The "Undo this" button restores exactly those items' previous states and stays available until the next decision.
- No "Approve all" that includes uncertain items. No "Skip all".

### 5.5 One at a time mode

Hypothesis: some students are overwhelmed by lists; others want the list. Both are available; the student chooses and the choice is remembered.

```
h1  Review one at a time           Item 3 of 24            [ Back to the list ]
Midterm exam
Exam · Tue, Oct 14 · 10:00 AM      Probably right
From your syllabus, page 3: "Midterm exam, Tuesday October 14 in class"
[ Approve ]  [ Skip ]  [ Edit ]
[ Back ]                          [ Next, decide later ]
```

- Text position "Item 3 of 24" (no progress bar, no "N left" language).
- Approve and Skip decide and advance. "Next, decide later" advances without deciding. "Back" returns to the previous item with its decision shown.
- Order: by date, with "Needs a date" items last, not mixed in.
- Focus: after a decision or navigation, focus moves to the new item's heading (`tabindex="-1"`), and the status region says the item title once. Escape or "Back to the list" returns to the list with focus on that item's row.
- End: "That's all of them. 9 approved, 3 skipped, 12 not decided." Buttons: "Review the ones left" (only if any) and "Go to calendar step" (primary if any approved). No confetti, no streaks.

### 5.6 Inline edit

- Edit opens in place, inside the row, as a small form (not a modal), pushing following rows down. Focus moves to the first field ("Title").
- Fields: Title (text), Type (select: Exam, Quiz, Assignment, Reading, Project, Other), Date (native `type="date"` plus visible format hint "Example: 2026-10-14"; also accept typing), Time (native `type="time"`, optional, with "All day" checkbox that is checked when no time).
- Actions: `Save` (primary) and `Cancel`. Enter in a field saves; Escape cancels. Cancel discards edits to that item.
- Validation after blur or on save, never per keystroke. Error text appears next to the field with `aria-describedby`; an error summary is not needed for a single-row form but focus goes to the first invalid field on failed save. Messages: "Enter a date like 2026-10-14." / "That date isn't real. Check the month and day."
- Save: row returns to read view, focus returns to the "Edit" button, status "Saved: Midterm exam." The edit marks the item "You edited this" (text) so the student knows the date is theirs rather than extracted. A `Reset to what we found` link restores the extracted values.
- Editing never changes the decision except: saving a date on a needs-date item moves it out of "Needs a date" and leaves it "Not decided". It does not approve.
- A draft edit survives accidental navigation (kept locally); returning shows the row open with "We kept your changes."
- Editing an approved item that has already been synced marks it "Changed since it was added" and offers "Update the demo calendar" at the sync step.

> **SUPERSEDED by docs/API_CONTRACTS_P2.md (P2-30, 2026-10-28; Lead decision, provisional where PO-owned):** Still correct for a synced item (stays approved, `changed_since_sync`, calendar untouched until the next explicit sync). Not covered here: editing an approved item that has NOT been synced returns it to "Not decided" because approval belongs to the revision that was approved, and editing the date or time of an AI item lowers its confidence to "Please check" (contract 6.2, 4, rows 17 and 48). The text above is left as written for history and is not the current requirement.

### 5.7 "Needs a date" items

Principle: surfaced, but never a warning.
- Count appears once in the summary line: "3 need a date." with a link-button "Show them" that expands the final group and moves focus to its heading.
- The group is last in Week view, and last in Type view. It is never promoted into a banner or modal and never blocks the sync.
- Each row says why in plain words: "No date found" / "The syllabus says 'to be announced'" / "Needs your check: 'Week 5' (needs the first day of classes)". If the optional "First day of classes" was not given and relative weeks are present, offer once: "Add the first day of classes to fill in 'Week 5' style dates." with a date field.
- The AI never guesses a date to fill the gap. A suggestion derived from a relative week is shown as "Please check", never "Looks right".
- Skipping all needs-date items is a normal outcome.

## 6. Calm "sync to calendar" confirmation (mock/demo)

Mock/demo labeling is permanent, text, and cannot be dismissed, same pattern as the lecture demo tag: "Demo calendar. Nothing is sent to Google until you connect it." It appears on the review screen, confirm step and result. It is not removable by settings. It disappears only when the real Google integration is connected, and then a different, explicit permission step replaces it (out of scope here, Google-integration + Security).

### 6.1 Confirm step (separate screen or in-page panel with its own `h1` focus)

```
h1  Add approved items to the demo calendar
[DEMO] Demo only. Nothing is sent to Google Calendar. Your Google account is not connected.

You approved 12 items:
  Week 3 (Sep 8-14)
    - Reading quiz 1 · Wed, Sep 10 · All day
  ...
  (grouped, collapsed after the first two groups; "Show all 12")

Not added: 9 not decided, 3 skipped, 3 need a date.   (text; no pressure)

Reminders (optional)   ( ) None   ( ) 1 day before   ( ) 1 hour before   (radio group, default None)

> **SUPERSEDED by docs/API_CONTRACTS_P2.md (P2-30, 2026-10-28; Lead decision, provisional where PO-owned):** The reminders radio group is removed: no reminder field exists in v1 and the schema rejects it (contract row 13). The same applies to `sync.reminders.*` copy and acceptance item 23. The text above is left as written for history and is not the current requirement.
   "Reminders are your choice. We don't add any unless you pick one."

[ Add 12 items to the demo calendar ]     [ Back to review ]
```

- The list shown is exactly what would be written. Item titles, dates and times are those the student sees in review.
- "Not added" line is informational, neutral, no color emphasis and no link to "finish".
- Press once. Button becomes "Adding..." (`aria-disabled`). Idempotent: a double press or retry produces no duplicates (each item carries a stable sync key; contract is Backend's).

### 6.2 Result

```
h1  Done. 12 items are in the demo calendar.
p   This is a demo. Nothing was sent to Google Calendar.
p   You can change or remove any of these later.
[ See the demo calendar ]   [ Back to Biology 101 ]
```

- Focus lands on the result `h1`. The status region is not also used (avoids the double announcement flagged in `m2-quick-recap-review.md` M7).
- Partial result: "Added 10 of 12. 2 didn't go through." with a list of the two, each with `Try again` and reason in words. Successful ones are not repeated or lost. Never report full success when any failed.
- Re-run (idempotent): items already added show "Already in the demo calendar". Changed items show "Changed since added: update?" with a checkbox defaulting to checked only if the student pressed "Update" on the confirm step; nothing updates silently.
- Removing: each synced item has "Remove from the demo calendar" with Undo. Wording about the real calendar waits until Google is connected.
- No celebration animation, sound or streak. Reduced motion is moot because nothing animates.

## 7. Saving, resuming, interruption recovery

- Every decision, edit and group-by choice is saved on action (server for decisions and edits; local for view settings, drafts, and expansion state). No "Save" button for decisions.
- Returning to the review: the student returns to the same group and item (scroll position and the focused row restored where practical), with "Welcome back. Pick up where you left off?" dismissible.
- Network failure on a decision: row shows "Couldn't save this one. Try again." with a retry button next to it; the visible state is the real state, not the optimistic one. No global red banner.
- Session expiry: preserve local drafts, prompt sign-in, restore after.
- Deleting: "Delete this syllabus" at the bottom of the review and in the course card overflow. Confirm: "Delete this syllabus and the items we found? Items already added to the demo calendar stay there until you remove them." Delete and export reachable (export: Q10).

> **SUPERSEDED by docs/API_CONTRACTS_P2.md (P2-30, 2026-10-28; Lead decision, provisional where PO-owned):** The delete copy that says items already added to the demo calendar "stay there" is wrong now: there is no keep option, and delete always removes the mock events (contract 5.2, row 25). Same for `delete.confirm` below. The text above is left as written for history and is not the current requirement.

## 8. Error and edge states

Each: what happened, whether anything was saved, the next action. Non-shaming. Focus rule: for errors caused by the student's own press, focus goes to the error heading or message (`tabindex="-1"`); background failures use the status region once.

| State | Heading and copy | Actions |
|---|---|---|
| Unreadable or scanned PDF (no text layer) | "We couldn't read the words in this file." / "It may be a scan or a photo. Try a version where you can select the text, or paste the text instead." | Paste text instead (primary), Choose another file |
| Password-protected file | "This file is locked." / "Remove the password and add it again, or paste the text." | Choose another file, Paste text |
| Wrong file type | "We can't use that kind of file." / "Use a PDF, Word or plain text file." | Choose another file |
| Too large | "That file is too big." / "The limit is 10 MB. Try a smaller version or paste the text." | Choose another file, Paste text |
| Too much pasted text | "That's a lot of text." / "We can read up to about 50,000 characters. Paste the part with dates." (limits are proposals, Q3) | Edit text |
| Empty or too short | "There isn't enough text to read yet." | Back to editing |
| No items found | "We didn't find any dates or deadlines." / "Some syllabi list them in a table or an image, which can be hard to read. You can try another version, or add items yourself later." | Try another version (primary), Delete this syllabus. "Add an item yourself" is offered only if manual add exists (Q9) |
| Only needs-date items found | "We found 5 items, but none had a clear date." / "Add dates yourself, or skip them." | Review items |
| Extraction failed (server) | "Something went wrong while reading." / "Nothing was saved from this file. You can try again." | Try again, Back to course |
| Extraction slow (over ceiling) | "This is taking longer than usual." / "You can leave. We'll keep trying, or you can try again." | Back to course, Try again |
| Offline during upload | "You're offline." / "Your syllabus wasn't sent. Try again when you're back online." | Try again |
| Sync item failed | "2 items didn't go through." (list with reason) | Try again per item |
| Session expired | "Please sign in again. What you decided is saved." | Sign in |
| Limit/budget reached (if the AI cost cap applies) | "Reading new syllabi is paused for now. Your other items are safe." (no cost jargon) | Back to course |

Rules: error text never names internal mechanics (tokens, rate limits, models). Do not blame the file author or student ("you uploaded the wrong file" is banned). No retry loops without a student press. The pasted text and chosen options are kept across an error.

## 9. Keyboard and screen-reader behavior

Landmarks and headings
- `main` contains one `h1` per view: "Add your syllabus", "Reading your syllabus", "Review your syllabus items", "Review one at a time", "Add approved items to the demo calendar", "Done. 12 items are in the demo calendar."
- Week/type groups are `h2` containing the disclosure button. Item titles are `h3` in the list (so heading navigation steps through items). In one-at-a-time mode the item title is the `h2`.
- Items are an unordered list per group (`ul` with `li`) so a screen reader announces "list, 3 items". Each `li` has an accessible name from its title via `aria-labelledby`. Decision: a list of fields is used rather than a `<table>` because rows contain inline editing and mixed actions; if Frontend prefers a real `<table>` it needs `<th scope>` headers, a caption, and the same button names and focus rules.
- The sticky summary bar is a `region` named "Review summary", not a live region.

Live regions: reuse the single polite `role="status"` region from the lecture screens, mounted once. Rules:
- Announce: "Reading your syllabus." (once), "Your items are ready." (once), "Approved: <title>." / "Skipped: <title>." / "Undone: <title>." / "Saved: <title>.", batch results ("Approved 14 items."), "Added 12 items to the demo calendar." only if focus did not move to the result heading.
- Never announce: running counts ("9 approved") on each change, progress ticks, expansion state beyond `aria-expanded`, polling updates.
- Minimum 2 s spacing between messages; identical text within 10 s dropped. `role="alert"` only for save failure after a student press and for session expiry, once per episode.

Keyboard
- Whole flow operable with keyboard only, no traps. Tab order follows DOM and visual order. Focus visible (2px+, 3:1) and never hidden under sticky bars (use `scroll-padding-bottom` for the bottom bar and `scroll-padding-top` for the top). 
- Disclosure buttons: Enter/Space toggle. Group-by is a radio group (arrow keys move and select, per native radio behavior).
- Row actions are normal buttons in Tab order (3 per row). To limit Tab cost across 30 rows, provide a skip link "Skip to calendar step" and, per expanded group, no roving tabindex (predictability over cleverness). Hypothesis: if keyboard users find 90 stops too many, add a "Review one at a time" mode (already offered) rather than custom key handling.
- Shortcuts: none required. If added later they follow SC 2.1.4 (remappable, off by default). Suggestion only: none now.
- Escape: closes inline edit (cancel), exits one-at-a-time mode, collapses nothing else.
- Focus map: Approve/Skip pressed -> stays on the same position (becomes Undo). Edit -> first field. Save/Cancel -> Edit button. Show them (needs date) -> the group heading. Batch approve -> the "Undo this" button. Mode switch -> heading of new view. Delete confirm dialog: trap focus, Escape cancels, focus returns to Delete.
- Dialogs: only for delete and replace confirmations (destructive). All other confirmations are inline.

Screen-reader reading order for a row (example): "Midterm exam, heading level 3. Exam. Tuesday October 14, 10 AM. Probably right. Not decided. Button: Approve Midterm exam. Button: Skip Midterm exam. Button: Edit Midterm exam. Button: See where this came from, collapsed."

## 10. Mobile and narrow layouts

- Single column from 320px. No horizontal scroll at 400% zoom.
- Each item is a stacked card: title, then "Exam · Tue, Oct 14 · 10:00 AM", then confidence label, then status, then a full-width action row with Approve and Skip (48px high) and Edit as a text button. "See where this came from" below.
- Bottom sticky bar holds only the primary action: "Add 9 approved items to the demo calendar". It becomes `aria-disabled` with text "Approve an item to continue" when none approved. The bar never covers focused content (scroll padding).
- Groups are collapsed except the first with undecided items; this keeps the page short.
- Batch and group-by controls sit behind a "View options" disclosure on phone to keep the first screen to: heading, demo tag, one summary line, first group.
- Inline edit becomes a full-width form in the card; native date/time pickers are used on touch.
- One-at-a-time mode is the suggested default prompt on phone only as a text suggestion ("Prefer one at a time?"), never forced.
- Landscape and tablet use the table-like row layout when width >= 768px.

## 11. WCAG 2.2 AA and quiet-mode considerations

WCAG targets (all hypotheses until audited)
- 1.3.1 headings, lists, labelled groups; 1.3.2 DOM order = visual; 1.3.5 `autocomplete` not needed on these fields; 1.4.1 no color-only (confidence, status, type); 1.4.3 / 1.4.11 text 4.5:1, controls and icons 3:1; 1.4.4 and 1.4.10 reflow at 320px / 400%; 1.4.12 text spacing survives; 1.4.13 no hover-only content (snippets are click, not hover).
- 2.1.1 keyboard; 2.1.4 no single-key shortcuts; 2.2.1 no time limits (undo never expires, processing has no session timer); 2.2.2 nothing auto-updates except text-only status; 2.3.3 no animation by default; 2.4.1 skip link; 2.4.3 focus order; 2.4.6 descriptive headings; 2.4.7 and 2.4.11 focus visible and not obscured; 2.5.3 visible label contained in the accessible name (e.g. visible "Approve" within "Approve Midterm exam"); 2.5.7 no dragging required (file drop is optional); 2.5.8 targets >= 44px (24px floor).
- 3.2.2 no unexpected context change on selection (group-by, expansion, radio changes never navigate); 3.3.1 / 3.3.3 errors identified with suggestions; 3.3.2 labels and format hints; 3.3.4 reversible (undo) for data that affects calendars; 3.3.7 no redundant entry (file or pasted text and first-day-of-class are kept); 4.1.2 name/role/value on disclosures (`aria-expanded`), radio groups, status.
- Reduced motion / forced colors / high contrast: no animation exists; icons are SVG with `currentColor` and visible borders so forced-colors keeps them distinct.
- Reading level: grade 8 or lower, sentences under about 20 words, checked with the existing `copy.test.ts` pattern.

Quiet mode (the only mode at this stage; compatibility)
- No unsolicited prompts, nudges, sounds, reminders or notifications. The only passive surface is the single course-card sentence (section 2), which is static text.
- No count of undecided items on the dashboard, no streak, no "finish your review" prompts, no deadline countdowns. Calendar reminders default to None.
- Balanced/Active (later) could offer an opt-in single weekly "dates you decided on" summary. Not designed here; student-controlled only.
- No attention inference: nothing measures time on page, hesitation or abandonment.

## 12. Acceptance criteria (frontend and QA)

Entry and upload
1. The course page shows a "Syllabus" section below "Lecture", with exactly one button, whose label and sentence match the state table in section 2 for each of the 7 states.
2. The upload page has one `h1`, focus lands on it, and "Read my syllabus" is `aria-disabled` (focusable, not native disabled) with a describing hint until input exists.
3. A file over the limit, wrong type, or locked file is refused at selection time with the exact text from section 8 beside the field and an `aria-describedby` link; the page is not reloaded and pasted text is kept.
4. Pasted text drafts survive a refresh and show "We kept what you pasted."
5. A double press of "Read my syllabus" creates exactly one syllabus (verified by request count and idempotency key).

Processing
6. The processing page has no spinner, percentage, countdown or elapsed timer (asserted by DOM query); steps are text with a "Done" word for finished steps.
7. Leaving the page and returning shows the correct state; the course card reads "Reading" then "Ready" without a manual refresh of server data beyond navigation.
8. On completion the status region announces "Your items are ready." once and focus does not move.
9. No notification, sound or popup is produced by completion unless the student enabled it (test with default settings).

Review
10. A 30-item fixture with 3 undated items renders with only the first group that has undecided items expanded; all other groups are collapsed, each with an accessible name containing item count and undecided count.
11. Default decision of every item is "Not decided"; no item is pre-approved (asserted over the fixture).
12. The summary line reads "We found 30 items. 0 approved · 0 skipped · 30 not decided" before action, and "3 need a date." with a "Show them" button that expands the Needs-a-date group and focuses its heading.
13. Each row shows title, type, date/time (or "All day"), a text confidence label from {"Looks right","Probably right","Please check"}, and decision status as text. No percentage is visible by default. Low-confidence rows show their source snippet open; others show it only after "See where this came from" (`aria-expanded` toggles).
14. Approving an item replaces its Approve button with "Undo" in the same position, focus stays on that control, and exactly one status message is announced. Undo works with no time limit, and returns the item to "Not decided".

> **SUPERSEDED by docs/API_CONTRACTS_P2.md (P2-30, 2026-10-28; Lead decision, provisional where PO-owned):** KEPT: undo of approve and skip never expires (contract decision 11, WCAG 2.2.1). This is the one place the contract confirms this document unchanged. The text above is left as written for history and is not the current requirement.
15. Needs-date items have no Approve button (or it is `aria-disabled` with the hint "Add a date first"); "Add date" opens the inline edit; saving a date leaves the item "Not decided".
16. Batch "Approve the clear ones" includes only items that are dated AND "Looks right" AND undecided; the button label count equals the number approved; "Undo this" restores prior states exactly. No batch action can approve "Probably right", "Please check" or undated items (negative test).
17. Inline edit: opens in place with focus on Title; Enter saves, Escape cancels; errors appear after blur/save only, with text and `aria-describedby`; saved row is marked "You edited this"; focus returns to Edit; "Reset to what we found" restores extracted values.
18. One-at-a-time mode: shows "Item N of M" as text; Approve/Skip advance; "Next, decide later" does not change the decision; Back shows prior decision; Escape returns to the list focused on that row; no progress bar present.
19. Decisions persist across a reload (server-side); view settings (group-by, mode) persist locally.
20. A failed decision save leaves the visible state matching the stored state and shows a retry beside the row.

Sync (mock)
21. The demo tag with text "Demo" is present on review, confirm and result screens, is not dismissible, and no string implies a Google Calendar write has occurred. A network spy asserts zero requests to any Google host.
22. The confirm screen lists exactly the approved items (and nothing undecided/skipped/undated), with a count in the primary button label that equals that list length.
23. Reminder option defaults to "None".
24. Pressing the primary sync button twice creates each item once; running the sync a second time shows "Already in the demo calendar" for unchanged items and never duplicates.
25. Partial failure shows "Added N of M" and lists failures with per-item retry; the success heading is not shown.
26. Result heading takes focus; the status region does not also announce the same outcome.

Errors
27. Each row in the section 8 table is reachable by a test (fixtures: scanned PDF, locked file, oversize, empty, no items, only undated items, server failure, offline) and the heading, copy, actions and focus target match.
28. No error message contains the words: overdue, behind, failed (as accusation of the student), missed, your fault, wrong file; and none names tokens, rate limits or model names.

Accessibility
29. axe: zero serious/critical violations on upload, processing, review (list and one-at-a-time), edit-open, confirm, result and every error state.
30. Keyboard only: upload -> review -> approve 3 -> edit 1 -> sync completes with no trap and focus never hidden by sticky bars (SC 2.4.11 check at 320px and 400% zoom).
31. Live-region discipline: in a 30-item review of approvals by screen reader, each action produces at most one announcement and no running counts.
32. Reflow at 320px and 400% zoom without horizontal scroll; targets >= 44px for row actions; text resize 200% without loss.
33. `prefers-reduced-motion` and forced-colors: no animation present; confidence/status icons distinguishable.
34. Manual passes (NOT RUN, human-only, per `device-accessibility-validation`): NVDA+Firefox/Chrome, JAWS+Chrome, VoiceOver macOS+iOS Safari, TalkBack on the review flow. An AI/axe pass does not close this.

Cognitive and tone
35. Copy review: no shaming language, sentences under about 20 words, reading level around grade 8 or below, matched against the strings in section 14.
36. Quiet mode check: with default settings across a full session, zero unsolicited prompts, sounds, badges, counts on the dashboard or notifications.
37. Moderated test with students who identify as ADHD (recruiting needs Product Owner approval; handled by `human-research-coordinator`): can say what they decided and what is left after being interrupted for 5 minutes, and can find a "needs a date" item without help. Results are design evidence only.

## 13. Open questions for the Lead Architect / Product Owner

1. Contracts: need a versioned syllabus/item contract (fields in section 0, `decision` and `syncState` values, per-item edit history for "Reset to what we found", idempotent sync key). I did not decide this.
2. Does extraction return a numeric confidence or an enum? The UI uses three labels; mapping thresholds are an AI-team decision and should be evaluated (do not label a guess "Looks right").
3. Limits: file types, 10 MB, 50,000 characters, and the processing ceiling are my proposals. Security, Backend and the cost owner should set them.
4. Is the source file kept after extraction, or only the text and snippets? Affects the privacy note, deletion copy and "Delete this syllabus" scope. Security.
5. Time zone: use the browser's, a per-account setting, or per-course? Drives the "Times are in your time zone" line and DST edge cases.
6. One syllabus per course, or several (for example an updated version mid-term)? The replace flow here assumes one.
7. Should the dashboard "next step" ever surface "Review syllabus items"? I recommend no (it risks a nag), but it is a Product decision.
8. Is an opt-in "tell me when it's ready" channel in scope (email, in-app only)? New channel needs Security and Product approval.
9. Is there a place in Tether to keep items "without a date" or items added by hand? Without it, those actions are omitted.
10. Export/delete scope for syllabus data: confirm alignment with the delete/export requirement in the architecture invariants.
11. Real Google flow later: scope request moment, account display, wording that replaces the demo tag. Owner: Google-integration with Security. Not designed here.
12. Default event duration for items with a time (draft: 1 hour, editable) and all-day for date-only. Product.
13. How should duplicate or conflicting items (same exam listed twice, "Final exam" and "Final exam, see registrar") be shown? Draft idea: a "Possible duplicate" text note on the second item; needs AI-team input.

## 14. Draft copy strings (all DRAFT, for `copy.ts`)

Course card
- `syllabus.card.title`: "Syllabus"
- `syllabus.card.none`: "Pull exam and assignment dates out of your syllabus. You check each one before anything goes on a calendar."
- `syllabus.card.none.button`: "Add your syllabus"
- `syllabus.card.reading`: "We're reading your syllabus. You can leave. It will be here when you come back."
- `syllabus.card.reading.button`: "See progress"
- `syllabus.card.ready`: (n) => `We found ${n} items. Ready when you are.`
- `syllabus.card.ready.button`: "Review items"
- `syllabus.card.partial`: (d, n) => `You've decided on ${d} of ${n} items. Pick up where you left off.`
- `syllabus.card.partial.button`: "Continue review"
- `syllabus.card.allDecided`: "All items are decided. Ready to add the approved ones to the demo calendar?"
- `syllabus.card.allDecided.button`: "Review and add"
- `syllabus.card.synced`: (n) => `${n} items added to the demo calendar. Nothing was sent to Google.`
- `syllabus.card.synced.button`: "View items"
- `syllabus.card.failed`: "We couldn't read this file. You can try another one."
- `syllabus.card.failed.button`: "Try again"

Upload
- `upload.h1`: "Add your syllabus"
- `upload.intro`: "We'll find dates for exams, assignments and readings. You'll check everything before it goes anywhere."
- `upload.demoTag`: "Demo mode: items are found by a test routine, not a real AI model."
- `upload.method.legend`: "How to add your syllabus"
- `upload.method.file`: "Upload a file"
- `upload.method.paste`: "Paste text"
- `upload.file.button`: "Choose a file"
- `upload.file.hint`: "PDF, Word or plain text. Up to 10 MB."
- `upload.file.selected`: (name, size) => `Selected: ${name} (${size})`
- `upload.file.remove`: "Remove"
- `upload.paste.label`: "Syllabus text"
- `upload.paste.hint`: "Paste as much as you have."
- `upload.paste.kept`: "We kept what you pasted."
- `upload.options.toggle`: "Add options"
- `upload.firstDay.label`: "First day of classes (optional)"
- `upload.firstDay.hint`: "Helps us turn 'Week 5' into a date. Skip it if unsure."
- `upload.privacy`: "DRAFT: Your syllabus text is read by [service names]. You can delete it any time."
- `upload.submit`: "Read my syllabus"
- `upload.submit.hint`: "Choose a file or paste text first."
- `upload.submit.busy`: "Reading..."
- `upload.cancel`: "Cancel"
- `upload.replace.confirm`: "Replace your syllabus? Your decisions on the old items will be removed. Nothing already added to the demo calendar changes."

Processing
- `process.h1`: "Reading your syllabus"
- `process.body`: "We're looking for dates and deadlines."
- `process.leave`: "You can leave this page. We'll keep going and it will be waiting for you."
- `process.back`: "Back to Biology 101"
- `process.step`: (n) => `Step ${n} of 3`
- `process.steps`: ["Got your file", "Finding dates", "Getting items ready"]
- `process.done`: "Done"
- `process.slow`: "This one is taking a bit longer. Nothing is wrong."
- `process.status.start`: "Reading your syllabus."
- `process.status.ready`: "Your items are ready."
- `process.cancel`: "Stop reading"
- `process.cancel.confirm`: "Stop reading this syllabus? Nothing is saved from it."
- `process.cancel.keep`: "Keep going"
- `process.offline`: "You're offline. We'll keep trying when you're back."

Review
- `review.h1`: "Review your syllabus items"
- `review.demoTag`: "Demo mode: nothing here goes to Google Calendar."
- `review.summary`: (n, a, s, u) => `We found ${n} items. ${a} approved · ${s} skipped · ${u} not decided`
- `review.needsDate`: (n) => `${n} need a date.`
- `review.needsDate.show`: "Show them"
- `review.tz`: (tz) => `Times are in your time zone: ${tz}.`
- `review.groupBy.legend`: "Group by"
- `review.groupBy.week`: "Week"
- `review.groupBy.type`: "Type"
- `review.oneByOne`: "Review one at a time"
- `review.expandAll` / `review.collapseAll`: "Expand all" / "Collapse all"
- `review.group.needsDate`: (n) => `Needs a date · ${n} items`
- `review.group.name`: (name, n, u) => `${name}, ${n} items, ${u} not decided`
- `review.long`: "This is a long list. Taking it one week at a time can help."
- `review.past`: "Date has passed"
- `review.welcomeBack`: "Welcome back. Pick up where you left off?"
- `item.conf.high`: "Looks right"
- `item.conf.medium`: "Probably right"
- `item.conf.low`: "Please check"
- `item.conf.detail`: (pct) => `How sure we are: about ${pct} in 100`
- `item.source.toggle`: "See where this came from"
- `item.source.from`: (page) => page ? `From your syllabus, page ${page}:` : "From your syllabus:"
- `item.allDay`: "All day"
- `item.status.none`: "Not decided"
- `item.status.approved`: "Approved"
- `item.status.skipped`: "Skipped"
- `item.edited`: "You edited this"
- `item.reset`: "Reset to what we found"
- `item.approve`: (t) => `Approve ${t}` (visible text "Approve")
- `item.skip`: (t) => `Skip ${t}` (visible "Skip")
- `item.edit`: (t) => `Edit ${t}` (visible "Edit")
- `item.undo`: (kind, t) => `Undo ${kind} of ${t}` (visible "Undo")
- `item.status.approvedMsg`: (t) => `Approved: ${t}.`
- `item.status.skippedMsg`: (t) => `Skipped: ${t}.`
- `item.status.undoneMsg`: (t) => `Undone: ${t}.`
- `item.status.savedMsg`: (t) => `Saved: ${t}.`
- `item.saveFailed`: "Couldn't save this one. Try again."
- `item.needsDate.none`: "No date found"
- `item.needsDate.tba`: "The syllabus says 'to be announced'"
- `item.needsDate.relative`: (w) => `Needs your check: '${w}' (needs the first day of classes)`
- `item.needsDate.addHint`: "Add a date first"
- `item.addDate`: "Add date"
- `item.err.dateFormat`: "Enter a date like 2026-10-14."
- `item.err.dateReal`: "That date isn't real. Check the month and day."
- `item.save` / `item.cancel`: "Save" / "Cancel"
- `item.changedSinceAdded`: "Changed since it was added"
- `batch.group`: (n) => `Approve the clear ones in this week (${n})`
- `batch.menu`: "More ways to decide"
- `batch.all`: (n) => `Approve all clear items (${n})`
- `batch.explain`: (n) => `This will approve ${n} items that have a date and look right. You can undo it.`
- `batch.go`: (n) => `Approve ${n} items`
- `batch.done`: (n) => `Approved ${n} items.`
- `batch.undo`: "Undo this"
- `oneByOne.h1`: "Review one at a time"
- `oneByOne.position`: (i, n) => `Item ${i} of ${n}`
- `oneByOne.next`: "Next, decide later"
- `oneByOne.back`: "Back"
- `oneByOne.exit`: "Back to the list"
- `oneByOne.end`: (a, s, u) => `That's all of them. ${a} approved, ${s} skipped, ${u} not decided.`
- `oneByOne.reviewLeft`: "Review the ones left"
- `oneByOne.toCalendar`: "Go to calendar step"
- `mobile.suggest`: "Prefer one at a time?"
- `bar.primary`: (n) => `Add ${n} approved items to the demo calendar`
- `bar.disabledHint`: "Approve an item to continue"
- `delete.link`: "Delete this syllabus"
- `delete.confirm`: "Delete this syllabus and the items we found? Items already added to the demo calendar stay there until you remove them."

> **SUPERSEDED by docs/API_CONTRACTS_P2.md (P2-30, 2026-10-28; Lead decision, provisional where PO-owned):** Superseded: no keep option; delete removes the mock events too (contract row 25). The text above is left as written for history and is not the current requirement.

Sync (mock)
- `sync.demoTag`: "Demo calendar. Nothing is sent to Google until you connect it."
- `sync.confirm.h1`: "Add approved items to the demo calendar"
- `sync.confirm.demo`: "Demo only. Nothing is sent to Google Calendar. Your Google account is not connected."
- `sync.confirm.lead`: (n) => `You approved ${n} items:`
- `sync.confirm.showAll`: (n) => `Show all ${n}`
- `sync.confirm.notAdded`: (u, s, d) => `Not added: ${u} not decided, ${s} skipped, ${d} need a date.`
- `sync.reminders.legend`: "Reminders (optional)"

> **SUPERSEDED by docs/API_CONTRACTS_P2.md (P2-30, 2026-10-28; Lead decision, provisional where PO-owned):** Removed (see the note on the reminders radio group above). The text above is left as written for history and is not the current requirement.
- `sync.reminders.none` / `.day` / `.hour`: "None" / "1 day before" / "1 hour before"
- `sync.reminders.hint`: "Reminders are your choice. We don't add any unless you pick one."
- `sync.go`: (n) => `Add ${n} items to the demo calendar`
- `sync.go.busy`: "Adding..."
- `sync.back`: "Back to review"
- `sync.result.h1`: (n) => `Done. ${n} items are in the demo calendar.`
- `sync.result.demo`: "This is a demo. Nothing was sent to Google Calendar."
- `sync.result.later`: "You can change or remove any of these later."
- `sync.result.view`: "See the demo calendar"
- `sync.result.back`: "Back to Biology 101"
- `sync.partial`: (k, n) => `Added ${k} of ${n}. ${n - k} didn't go through.`
- `sync.retry`: "Try again"
- `sync.already`: "Already in the demo calendar"
- `sync.update`: "Changed since added: update?"
- `sync.remove`: "Remove from the demo calendar"

Errors (headings and bodies from the section 8 table are the strings; ids: `err.unreadable`, `err.locked`, `err.type`, `err.size`, `err.pasteLong`, `err.empty`, `err.noItems`, `err.onlyUndated`, `err.server`, `err.slow`, `err.offline`, `err.syncItem`, `err.session`, `err.budget`).

## 15. Not tested, next steps

Not done: no prototype, no render, no axe, no screen reader, no device pass, no student or clinician review, no reading-level script run. Contrast and target sizes are specified, not measured. Mock extraction behavior is assumed, not observed.

Next agents: Lead Architect (contract and decisions, section 13), frontend-engineer (components, `copy.ts`), backend-engineer (syllabus/item/sync endpoints, idempotency), ai-agent-engineer (confidence and snippet schema, extraction eval), security-engineer (upload limits, retention, disclosure copy), qa-engineer (turn section 12 into tests), device-accessibility-validation (AC 34), human-research-coordinator (AC 37).
