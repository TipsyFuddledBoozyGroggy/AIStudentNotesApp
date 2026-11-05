# Delete wording: one lecture, a course, everything

**Status: DRAFT - NOT APPROVED.** Author: `neurodivergent-ux-specialist`, 2026-11-05. Authorised by PO-20bq ("draft new delete-everything wording as DRAFT for PO approval"; drafting only, nothing ships). This document changes no app text. Every consent, recording and Demo-source string quoted below is G1 wording and stays exactly as it is until the Product Owner records a decision in `docs/DECISIONS.md`. Nothing here is human-validated (G4). It is not legal advice; the legal rows it cites are provisional ratings by the Lead, not reviewed by counsel.

What I did: read-only review of the files named below. Commands run: none. Tests run: none. Line numbers are from the working tree on 2026-11-05 (clean, at `ecb929d`).

Design system: "reading room, second edition" (`docs/ux/redesign-round2.md`). Every control named here is an existing primitive: `.dialog` + `.dialog-scrim`, `.sheet`, `.note`, `.btn-primary`, `.btn-secondary`, `.btn-tertiary`, `.btn-danger`, `.label`, the syllabus `ConfirmDialog`, the existing announcer. No new token, CSS or live region.

---

## 1. What exists today

### 1.1 The one delete dialog that is built (syllabus)

`src/web/src/components/syllabus/copy.ts:451-460`:

```
export const DELETE_COPY = {
  button: "Delete this syllabus",
  heading: "Delete this syllabus?",
  body: "This deletes the syllabus, the items Tether found and your decisions. Its items in the demo calendar are removed too.",
  copies: "Copies you downloaded are not deleted.",
  confirm: "Delete syllabus",
  keep: "Keep it",
  busy: "Deleting...",
  done: "The syllabus was deleted.",
} as const;
```

Shown through `src/web/src/components/syllabus/ConfirmDialog.tsx` (safe choice "Keep it" is the filled button and takes focus; Escape cancels; page behind is `inert`; focus returns to the opener). Related strings in the same file:

- `:170` `purged: "The original wording was removed on schedule. Titles, dates and your decisions are kept."`
- `:240` `purgedSnippet: "The original wording was removed on schedule."`
- `:462-469` `EXPORT_COPY` (`"Download a copy (JSON)"`, `"The original file is not kept, so it is not in the copy."`)
- `:476` `gone: "This syllabus is no longer here. It may have been deleted in another tab."`
- `src/web/src/components/syllabus/Processing.tsx:29` "Stop reading" also deletes the syllabus (`DELETE cancels the running read; nothing is kept`).

### 1.2 Delete-related sentences inside G1 wording (quoted, not touched)

`src/web/src/components/lecture/copy.ts:47` and `:49` (CONSENT_COPY bullets):

```
"Audio is not kept. The transcript is kept until you delete it. (Draft: retention is not final.)",
"Deleting a lecture here does not remove copies already held by those services or by Google.",
```

`src/web/src/components/lecture/copy.ts:240` (NOTES_COPY.whatIsSentBody):

```
"The whole transcript of this lecture, including words from anyone else who spoke. The AI service may keep a copy under its terms. Deleting this lecture here does not remove that copy. (Draft wording.)",
```

Same bullet in design: `docs/ux/m2-live-lecture-screens.md:56`.

### 1.3 Sentences that mention deletion in error states

- `src/web/src/components/AllCoursesPage.tsx:49`, `DashboardView.tsx:116`, `DashboardPage.tsx:22`: `Nothing was deleted. Try again when you like.`
- `src/web/src/components/CourseDetailPage.tsx:47`: `It may have been removed, or it belongs to another account.` / `Nothing was deleted. Try again in a moment.`

### 1.4 What does NOT exist (the main finding)

| Situation | Screen | API | Evidence |
|---|---|---|---|
| Delete one lecture | **none** | `DELETE /sessions/{id}` | `SessionEndpoints.cs:25`; grep of `src/web/src` for `deleteSession|deleteCourse|method: "DELETE"` matches only `lib/syllabus/client.ts:219` |
| Hide (archive) a course | **none** | `DELETE /courses/{id}` | `CourseEndpoints.cs:124-159` |
| Delete a course | **none** | `DELETE /courses/{id}?purge=true` | `CourseEndpoints.cs:139-176` |
| Delete a syllabus | built | `DELETE /syllabi/{id}` | section 1.1 |
| Delete everything (account) | **none** | **none** | `DECISIONS.md` ADR-020b "User deletion has no endpoint yet"; `findings-register.md:712` NEW-01; PO-20ax parks it until before real use; sweep test `DeletionAndExportSweepTests.cs:534` "There is no account-deletion API yet" |
| Export a lecture / export all | **none** (button disabled) | **none** | `LectureScreen.tsx:345-351` "Not built yet. Nothing is sent anywhere."; sweep test `:514-516` probes `/export` routes and requires 404 |

`docs/ux/student-experience.md:251` planned "Delete this lecture's transcript" reachable from the lecture page. It was never built. `src/web/src/components/ui/UiKit.tsx:91` shows a `.btn-danger` labelled "Delete" as a kit sample only.

So the student today can delete a syllabus and nothing else. The consent bullet "The transcript is kept until you delete it" describes a control that has no screen.

---

## 2. What the legal rows say is wrong or misleading

| Row | What it says (quoted from `docs/legal/legal-register.md`) | What that means for wording |
|---|---|---|
| **L-07** (MEDIUM, DEFERRED) `:17` | "Deletion duty when a copy sits in the student's own Google account; wording for delete dialogs and retention (SEC-014, PO-5). PO-15c: deleting Tether data or the Tether account never deletes anything already in the student's Google account. Dialog wording deferred." | Any delete dialog must say that Google copies stay. The current syllabus dialog says only "Copies you downloaded are not deleted"; nothing says what happens to exported Docs or Calendar entries (nothing is exported yet, so the sentence is not yet false, but it will be the day export ships). Security `GR-2` (`findings-register.md:556`): "Deleting in Tether does not remove Google copies; users may assume it does." `CVR-N07` (`:546`): account deletion does not say which calendar action applies. |
| **L-08** (MEDIUM, DEFERRED) `:18` | "Retention, deletion deadline, export and privacy-law duties by jurisdiction; privacy policy text (C7)." Evidence needed: "Approved privacy policy matching actual backups." | No wording may name a deletion deadline or say "gone from our backups" until a retention schedule and backups exist. Today there are **no backups at all** (`docs/ops/production-readiness-audit.md:35`: "No backup script, schedule, encryption, RPO/RTO, or restore procedure in repo... ABSENT; drill NOT RUN"). `SECURITY.md:31` SEC-014 "<=30 days incl. backups" is a design target, not a fact. |
| **L-14** (MEDIUM, DEFERRED) `:24` | "Retention of consent and audit evidence versus a student's deletion rights." | PO-20aw keeps content-free consent, audit and AI-usage rows after a lecture or course delete and removes them only on full erasure. The dialog must say that something stays, in plain words, or "delete" over-promises. |
| **F-M3** (`docs/legal/anthropic-terms-review.md:47`) | "Lecture and exam content sent to Claude persists at Anthropic for at least 30 days after deletion in Tether. Tether deletion controls (SEC-014, L-07, L-08) cannot reach this copy. User-facing privacy text must say so." | The 30-day figure is for Anthropic direct; the chosen path is Claude on Azure (PO-20aa) and is **UNVERIFIED** (`DECISIONS.md:194`). Azure AI Speech retention is also UNVERIFIED (L-05). Wording cannot state a number. |
| **PO-20bj** (3) | "Before Stage 2 the delete wording (L-07) is fixed first." | This draft is that work item. It is not finished until the PO approves and the screens exist. |
| `docs/legal/lawyer-questions-and-risk-acceptance.md:80` | "The delete message might promise more than is true." | The test for every sentence below: section 7 maps each one to code. |

---

## 3. What is actually deleted and what is kept (from the code)

Read from `SessionService.cs:327-353`, `CourseEndpoints.cs:124-213`, the two `CourseLifecycleHandler.cs` files, migrations `M006_LecturesIntegrity.cs`, `A001_Ai.cs`, `A005_LectureNotes.cs`, `T001_Transcript.cs`, and the allow-lists in `DeletionAndExportSweepTests.cs:29-41` and `:534-542`. The sweep tests discover every table from `information_schema`, so this list is as complete as the schema on that date.

| Action | Deleted (content) | Kept (content-free) | Not reached at all |
|---|---|---|---|
| **Delete one lecture** `DELETE /sessions/{id}` | the session row; transcript lines (`transcript.transcript_events`); audio progress counter; audio tickets; idempotency keys; session events and counters; TL;DR revisions (`ai.summary_revisions`); Quick recaps (`ai.catch_ups`); notes and the Final summary (`ai.lecture_notes`). A live lecture is stopped first (consent Stop event). In-flight AI calls are cut off and refused for 10 minutes (`SessionCutoff.cs`). | `lectures.consent_records`, `consent_events`, `audit_events` (ids, versions, times, action names; holds the `session.delete` row itself); `ai.ai_usage` (tokens, cost, outcome; no session FK so the daily cap is not refunded). PO-20aw. | a request already handed to a vendor (`API_CONTRACTS.md:223`); any exported copy (none can exist yet); copies on the student's device. |
| **Hide a course** `DELETE /courses/{id}` | nothing | everything; `archived_at` set; live lectures stopped (reason `course_archived`); syllabi "stay readable and deletable" (`Syllabus/CourseLifecycleHandler.cs:33`). | n/a. **No reversal exists**: grep of `src/api/src/Modules/Courses` for `ArchivedAt = null|Unarchive|unarchive|restore` finds no un-archive route (only `#pragma warning restore` lines); do not promise "you can bring it back". |
| **Delete a course** `?purge=true` | the course; every lecture as above; every syllabus with its text, items, dates, decisions, runs, jobs, keys; its demo-calendar events (`MockCalendar.RemoveCourseEventsAsync`). | as for a lecture, plus `syllabus.decision_audit` (action, ids, counts; a `syllabus_deleted` row is written for each). | as above. |
| **Delete a syllabus** | text, items, dates, demo events, runs, jobs, keys; local drafts on this device (`storage.ts:80-100`). | `syllabus.decision_audit`, `syllabus.creation_counters`, `ai.ai_usage`. | downloaded JSON copies. |
| **Delete everything** (no endpoint; DB cascade from `identity.users` + `lectures.purge_evidence(user)` + `syllabus.purge_evidence(user)`) | every row above for every course; login sessions; the user row; `ai_usage` (FK to user); then the evidence rows and counters. Sweep test `:544`: "NOTHING may mention the user". | nothing, **once all three steps ran**. Today they are three separate SQL statements; no endpoint wraps them. | vendor copies (UNVERIFIED how long); Google copies (PO-15c: never deleted by Tether on account deletion); device copies; **backups: none exist today, so nothing to say yet**. |
| **Scheduled removal** (syllabus only) | canonical text, snippets and context at min(180 days after the student's last write, 365 days after creation), provisional and PO-owned (`API_CONTRACTS_P2.md:171-177, 260, 324`); failed/abandoned syllabi after 7 days. | titles, dates, decisions. | **No scheduled removal exists for transcripts.** PO-5 proposes 180 days; undecided. The consent bullet "kept until you delete it" is literally true today. |
| **Audio** | never stored: no table holds audio; `transcript.audio_progress` holds a millisecond count only (`DeletionAndExportSweepTests.cs:69`). | | |

Facts that stay UNVERIFIED or placeholders:

- **Model and speech providers.** Mock only today (`AiGateway` startup guard; `Speech:Provider` replay only, ADR-020c). When real: Anthropic direct says 30 days default, up to 2 years if flagged (F-M3, read 2026-10-30); Claude on Azure and Azure AI Speech: UNVERIFIED, nothing read. Wording below uses `[VENDOR RETENTION: UNVERIFIED]` where a number would go.
- **Backups.** None. When one exists, L-08 requires the privacy text to match it. Placeholder `[BACKUP WINDOW: not yet applicable]`.
- **Google.** Nothing connected (`LectureScreen.tsx:350`; `syllabus/copy.ts:388`). PO-15c and PO-10m fix the rule for later: copies stay; a deliberate item delete may offer "also remove from Google" while the grant is valid; account deletion never removes Google copies.

---

## 4. Tone rules applied here

From `docs/ux/language-guide.md` (sections 1, 3.2, 9) and `copyLint.ts`, plus three rules specific to deletion:

1. **State the fact, then the consequence, then the choice.** No reassurance before the fact.
2. **"It can't be undone" only where true, and nowhere soften it.** No "permanently" (over-promises against vendors and future backups); no "forever"; no "gone for good".
3. **Name what stays, in one short list.** A student deleting to feel safe deserves to know what remains. No euphemism ("some records"); say "a record that a lecture happened".
4. **The student owns it.** "Your" transcript, "your" decisions. Never "we will remove your data for you". For the pilot: with him, not on him (`docs/ux/pilot-plan-review.md`); nothing here reports a delete to anyone else, and the PO sees daily totals only (D15).
5. **Undo language only if true.** No delete here has an undo. The dialog's "Keep it" is the undo, before the fact. Say so with the button, not with a promise.
6. **No typing-to-confirm, no countdown, no second password.** A two-button dialog with the safe choice focused is enough for one item. "Delete everything" gets two steps and a sign-out, no typing.
7. **Sentences under 20 words, one idea each. No "!" , "just", "right now", "nothing is lost", "error", "failed", "immediately", "make sure".** Checked against `FORBIDDEN` and `FORBIDDEN_SYLLABUS` by eye; the frontend's `copy.test.ts` must confirm.
8. **Errors say whether anything changed.** Every failure line ends with "Nothing was deleted." when that is what the server guarantees (one transaction; `CourseEndpoints.cs:184` "never 500", 409 or 404 on a lost race).

---

## 5. Proposed wording (DRAFT - NOT APPROVED)

Strings are grouped as they would sit in a `DELETE_COPY`-style registry so the frontend can lint them. Items in `[square brackets]` are placeholders the PO or legal must fill or strike; items marked **(conditional)** render only when the condition in section 7 is true.

### 5.1 One lecture

Where: the stopped lecture's page, in the "Review this lecture" toolbar, as a `.btn-tertiary`. Not shown while the lecture is Active or Paused (Stop is the one action then; the API would stop it, but one choice at a time is kinder). Hidden in focus mode.

```
button:   "Delete this lecture"
heading:  "Delete this lecture?"
body:     "This deletes the transcript, the summary, any recaps and the notes."
cannot:   "It can't be undone."
kept:     "Tether keeps a record that a lecture happened: when it started, paused and stopped, and how much AI it used. None of the words stay."
vendorMock (conditional: no outside AI or speech service was used):
          "Nothing from this lecture was sent to an outside service."
vendorReal (conditional: a real service was used):
          "The speech and AI services got the words during the lecture. They may keep their own copy for a time. Tether can't delete that copy. [VENDOR RETENTION: UNVERIFIED - PO/legal to fill or strike]"
google (conditional: an export to Google exists for this lecture):
          "The copy you exported to Google Docs stays in your Google account. You can delete it there."
confirm:  "Delete lecture"
keep:     "Keep it"
busy:     "Deleting..."
done:     "The lecture was deleted."            (announcer status; focus moves to the course heading)
doneNote: "Deleted: {lecture title}."           (a .note on the course page, dismissable, no timer)
```

### 5.2 A course

Two actions exist in the API. I recommend exposing **only Delete** until an un-hide route exists, so there is one clear destructive action and no "which one did I mean" moment. If the PO wants Hide too, its wording is in 5.2b.

**5.2a Delete.** Where: course page, below the lectures and syllabi, `.btn-tertiary`. Unavailable (`aria-disabled` with `aria-describedby` reason) while a lecture is recording.

```
button:    "Delete this course"
blocked:   "A lecture is recording. Stop it first."
heading:   "Delete this course?"
body:      "This deletes the course and everything in it:"
list:      [
  "{n} lectures: transcripts, summaries, recaps and notes",     (singular form when n = 1)
  "{m} syllabi: the text, the items and your decisions",         (omit when m = 0)
  "its items in the demo calendar",                              (omit when none)
]
cannot:    "It can't be undone."
kept:      "Tether keeps a record that these lectures and syllabi existed, when, and how much AI they used. None of the words stay."
vendorMock / vendorReal / google: as in 5.1, with "this course" for "this lecture".
confirm:   "Delete course"
keep:      "Keep it"
busy:      "Deleting..."
done:      "The course was deleted."             (announcer; focus to the dashboard h1)
doneNote:  "Deleted: {course name}."
```

**5.2b Hide (archive), only if the PO wants it now.**

```
button:   "Hide this course"
heading:  "Hide this course?"
body:     "The course and everything in it stay saved. It leaves your lists."
back:     "[Un-hide route not built: do not promise 'You can bring it back' until it exists.]"
confirm:  "Hide course"
keep:     "Keep it"
done:     "The course is hidden."
```

### 5.3 Syllabus (existing; optional tightening)

The built dialog is close. Two optional changes, same registry, same tests:

```
body:   "This deletes the syllabus text, the items Tether found and your decisions. Its items in the demo calendar are removed too."
kept:   "Tether keeps a record that a syllabus existed and how much AI it used. None of the words stay."   (new line, for L-14 honesty)
copies: "Copies you downloaded are not deleted."   (unchanged)
```

### 5.4 Delete everything (account) - for when PO-20ax is built

Where: Settings, a "Your data" section (`.label` "Your data"), two rows: "Download a copy of your data" and "Delete everything". Until export-all exists, the download row is `aria-disabled` with the reason "Not built yet." (same pattern as `LectureScreen.tsx:349-351`). "Delete everything" is a `.btn-danger` (the one place the kit's destructive style is earned; it is never filled, `globals.css:515-518`).

**Step 1: a `.sheet` page, not a dialog, because it has to be read.**

```
h1:        "Delete everything?"
body:      "This deletes your Tether account and everything in it."
list:      [
  "every course, lecture, transcript, summary, recap and note",
  "every syllabus, its items and your decisions",
  "your settings and your sign-in",
  "the records of when you recorded and how much AI was used",
]
cannot:    "It can't be undone."
staysH2:   "What stays"
staysList: [
  google (conditional: anything was ever exported):
    "Anything you exported to Google Docs or Google Calendar. It is in your Google account. Only you can delete it there.",
  device:
    "Copies you downloaded to this device.",
  vendorMock (conditional): "Nothing was sent to an outside service, so nothing stays there.",
  vendorReal (conditional): "What the speech and AI services received. They may keep it for a time under their own terms. Tether can't delete it. [VENDOR RETENTION: UNVERIFIED]",
  backups: "[BACKUP WINDOW: not yet applicable. When a backup exists: 'Backup copies are deleted within N days.' N from the retention schedule (L-08).]",
]
copyFirst (conditional: export-all exists): "Want a copy first? Download your data."
next:      "Continue"              (.btn-secondary)
back:      "Keep my account"       (.btn-primary, focused on load)
```

**Step 2: the `.dialog` (same `ConfirmDialog` pattern).**

```
heading:  "Delete everything and sign out?"
body:     "Your account and all its data will be deleted. You will be signed out."
confirm:  "Delete everything"
keep:     "Keep my account"
busy:     "Deleting..."
```

**After:** a signed-out page (no account to return to).

```
h1:    "Your Tether data was deleted."
body:  "You are signed out."
stays: "Anything in your Google account or on this device is still yours." (conditional as above)
```

### 5.5 One shared explainer, so dialogs stay short

A `Disclosure` (existing) titled **"What a delete keeps"** under the Settings "Your data" label and linked from each dialog's kept line ("What a delete keeps" as a `.link`). Body:

```
"Deleting a lecture, a course or a syllabus removes all of its words from Tether."
"Tether keeps a short record that it existed: when, which kind, and how much AI it used. That record has no words in it."
"Deleting everything removes those records too."
"Tether never deletes anything from your Google account or from your device."
"[VENDOR RETENTION: UNVERIFIED - one sentence on what outside services keep, once the PO and legal have the facts.]"
```

---

## 6. Interaction, error and confirmation states

### 6.1 Dialog behaviour (all deletes; matches `ConfirmDialog.tsx` and redesign-round2 2.7 / line 198)

- `role="dialog"`, `aria-modal`, labelled by the heading, described by the body including the kept list (`<ul>` inside the description).
- Safe choice ("Keep it" / "Keep my account") is `.btn-primary`, first in DOM, **focused on open**. Delete is `.btn-secondary` (destructive is never filled). Enter on open therefore keeps.
- Escape = Keep. Tab cycles inside. Page behind `inert`. Focus returns to the opener on Keep.
- While the request is in flight: confirm button `aria-busy`, label "Deleting...", presses ignored (double-press protection, L-BUSY). Keep is also inert during the request (the server will answer; a cancelled request could still have deleted).
- No timer, no auto-close, no animation that reduced-motion would need to remove (scrim fade is already token-governed).
- Quiet mode and low-stimulation: same dialog; `.btn-danger` keeps its border in forced colours (`CanvasText`, redesign-round2 line 557).
- Touch: every button 44px (`.btn`). The dialog is bottom-anchored under 640px (existing).

### 6.2 After success

| Action | Focus goes to | Announcement (existing announcer, `status`) | Visible note |
|---|---|---|---|
| Lecture deleted | course page h1 | "The lecture was deleted." | `.note` "Deleted: {title}." with a Dismiss button; no timer |
| Course deleted | dashboard h1 | "The course was deleted." | `.note` "Deleted: {name}." |
| Syllabus deleted | "Add your syllabus" h1 (existing) | "The syllabus was deleted." (existing) | existing |
| Everything deleted | signed-out page h1 | none needed (new page) | the page itself |

Never navigate before the 204 arrives. Never show the note on a page the student did not land on.

### 6.3 Failures (one line each, no status codes)

| Server answer | Wording | Changed? |
|---|---|---|
| network / 5xx | "We could not reach the server. Nothing was deleted. Try again when you like." | no |
| 404 (already gone, another tab) | "This lecture is no longer here. It may have been deleted in another tab." then behave as deleted | yes, elsewhere |
| 409 (edited or raced) | "Something changed while deleting. Nothing was deleted. Try again." | no |
| 429 | "One moment. Try again in about a minute. Nothing was deleted." | no |
| 401 | "Please sign in again." / "Nothing was deleted." | no |
| live lecture when Delete pressed from a stale page | "This lecture is recording. Stop it first." | no |
| account erasure partial (if the three steps are not one transaction) | **must not happen**: backend question Q-B1 below. If it can, the only honest line is "Your lectures and courses were deleted. Some records could not be removed yet. Tether will finish this. You do not need to do anything." and that is a promise the code must keep. | partial |

Each failure keeps the dialog open with the message in the dialog's description (`aria-describedby` already points there) so a screen reader hears it in place; focus stays on the confirm button.

### 6.4 Keyboard and screen reader

- Opener accessible name equals its visible text ("Delete this lecture"), WCAG 2.5.3.
- The kept list is real list markup so "list, 3 items" is announced.
- The conditional vendor line must never be both variants; render exactly one.
- No new `aria-live` region; `announcer.status` is the one allowed path (playbook 4: exactly three live regions).

---

## 7. Claim -> evidence in code

No sentence may ship whose row says NOT BUILT or UNVERIFIED without the placeholder resolved.

| Proposed sentence | Claim | Evidence | State |
|---|---|---|---|
| "This deletes the transcript, the summary, any recaps and the notes." | cascade removes all four | `T001_Transcript.cs:76,79`; `A001_Ai.cs:145-149`; `A005_LectureNotes.cs:85-86` all `ON DELETE CASCADE`; sweep `Deleting_a_session_removes_every_derived_row...` (`:252`) | built, tested |
| "It can't be undone." | no undo route | no restore endpoint in `SessionEndpoints.cs` / `CourseEndpoints.cs`; hard delete | true |
| "Tether keeps a record that a lecture happened: when it started, paused and stopped, and how much AI it used." | consent/audit/usage rows survive | `SessionService.cs:329`; sweep allow-list `:29-35`; PO-20aw | built, tested |
| "None of the words stay." | survivors hold no content | sweep test asserts no canary in any survivor (`:573` "content leak in a survivor" fails the test) | built, tested |
| "Nothing from this lecture was sent to an outside service." (mock) | mock providers only | `AiGateway` startup guard refuses non-mock (playbook 3); ADR-020c replay only | true today; must be driven by `aiSource`/`demoSource` on the session, not hard-coded |
| "They may keep their own copy for a time. Tether can't delete that copy." (real) | vendor retention | `API_CONTRACTS.md:223` "cannot be recalled"; F-M3 Anthropic direct; Azure paths UNVERIFIED | **UNVERIFIED**: placeholder stays |
| "The copy you exported to Google Docs stays in your Google account." | PO-15c | no Google client exists; `LectureScreen.tsx:350` | **NOT BUILT**: conditional, never rendered today |
| "{n} lectures: transcripts, summaries, recaps and notes" | course purge cascades sessions | `M006:28-29` course FK cascade; sweep `:351` | built, tested |
| "{m} syllabi: the text, the items and your decisions" | course purge cascades syllabi | `Syllabus/CourseLifecycleHandler.cs:9-12`; sweep `:351` | built, tested |
| "its items in the demo calendar" | mock events removed | `CourseLifecycleHandler.cs:70` `RemoveCourseEventsAsync` | built (mock calendar) |
| "A lecture is recording. Stop it first." | UI gate | not built; API would stop it (`SessionService.cs:259-266`) | **NOT BUILT** (frontend) |
| "The course and everything in it stay saved. It leaves your lists." (Hide) | archive is soft | `CourseEndpoints.cs:145-148`; `List` filters `ArchivedAt == null` (`:73`) | built; **reversal unverified** |
| "This deletes your Tether account and everything in it." | user cascade | sweep `:534-544` via SQL, no endpoint | **NOT BUILT** (PO-20ax) |
| "the records of when you recorded and how much AI was used" (deleted on erasure) | evidence purge | `lectures.purge_evidence` (`M006:79-87`), `syllabus.purge_evidence` (`S002:76`); `ai_usage` FK to user (`A001:152`) | functions exist; **no caller** (NEW-01) |
| "You will be signed out." | login sessions cascade | `identity.sessions` in sweep `:531`; FK to user | DB path only |
| "Backup copies are deleted within N days." | backups | `production-readiness-audit.md:35`: none exist | **NOT APPLICABLE today**; placeholder |
| "Copies you downloaded to this device." | JSON export exists for syllabus | `EXPORT_COPY`; lecture export does not exist | true for syllabus only |
| "Want a copy first? Download your data." | export-all | sweep `:514-516` requires 404 | **NOT BUILT**; conditional |
| "Nothing was deleted." (409, network) | single transaction | `CourseEndpoints.cs:184-185`, `SessionService.cs:348-367`: rollback and retry, "never 500" | built |
| "It may have been deleted in another tab." | 404 after race | `CourseEndpoints.cs:216-221` | built |
| "The original wording was removed on schedule." (existing) | syllabus text purge | `API_CONTRACTS_P2.md:171-177` provisional | built; schedule PO-owned |

---

## 8. Open questions

### For the Product Owner (Lead to post; answers go to `docs/DECISIONS.md`)

- **Q-PO1.** Approve, amend or reject each string in section 5 as DRAFT wording. The G1 bullets in 1.2 are not changed by this document; do you want them re-opened alongside, or left for the counsel pass?
- **Q-PO2.** Expose course **Hide** now (5.2b) or only **Delete** (5.2a) until an un-hide route exists? My recommendation: Delete only.
- **Q-PO3.** Should Delete be hidden while a lecture is recording (my proposal), or allowed with "This also stops recording"?
- **Q-PO4.** "how much AI it used" tells the student that a cost record exists. Keep that phrase, or say "and some technical records"? I recommend keeping it: it is the truth and it is content-free.
- **Q-PO5.** Build order: delete-lecture and delete-course screens now (routes exist), with the account path when PO-20ax is scheduled? Or all three together before Stage 2?
- **Q-PO6.** Retention for transcripts (PO-5, 180-day proposal). Until decided, the consent bullet "kept until you delete it" is true and the explainer says nothing about a schedule. Decide before any scheduled purge is built.
- **Q-PO7.** Two steps and a sign-out for "Delete everything", no typing-to-confirm (section 4 rule 6). Acceptable?

### For legal (research support, not advice; via `legal-compliance-reviewer`)

- **Q-L1 (L-07).** Is "Tether never deletes anything from your Google account" plus "You can delete it there" sufficient disclosure for PO-15c, and does the deliberate-item "also remove from Google" option (PO-10m) need its own sentence when export ships?
- **Q-L2 (L-08).** Does any applicable law require a stated deletion deadline for one adult user of a personal tool? If so, the deadline sentence waits for the backup design.
- **Q-L3 (L-14).** Is keeping content-free consent, audit and usage rows after a delete, until account erasure, acceptable to state as "a record that a lecture happened"? Should the dialog say how long those rows live?
- **Q-L4 (F-M3, L-04, L-05).** Provide one sentence, with a dated source, on what Azure AI Speech and Claude on Azure retain and for how long, or confirm that the honest sentence is "may keep their own copy for a time" with no number.
- **Q-L5.** Does an exported Google Doc that contains other people's speech change the deletion duty (L-02 overlap)? Wording for that case is not drafted here.

### For engineering (not decisions; for the Lead to route)

- **Q-B1 (backend).** Account erasure must be one transaction: user cascade + `lectures.purge_evidence` + `syllabus.purge_evidence`. Otherwise the partial-failure line in 6.3 becomes necessary and I would rather it never exist.
- **Q-B2 (backend).** Is there an un-archive path? If not, Hide stays out (Q-PO2).
- **Q-F1 (frontend).** The vendor line must be driven by session facts (`demoSource`, `aiSource`), never by a build flag. Add the strings to the lecture `copy.ts` registry so `copyLint` runs on them.
- **Q-F2 (frontend).** Hidden-while-recording needs the live session state on the course page; confirm it is already fetched.

---

## 9. What this document does not do

- It does not change any file in `src/`, any existing copy string, or any G1 wording.
- It does not clear G1, G4, L-07, L-08 or L-14, and it is not a risk acceptance (PO-20bj D22 is still unwritten).
- It is not human-validated. The G4 question list in `docs/ux/language-guide.md` section 10 should gain: "Does 'It can't be undone' read as a warning or a threat? Does 'None of the words stay' feel honest or evasive?"
- It was not run against `copyLint` (the strings are not in code). The frontend's `copy.test.ts` is the check when they land.
