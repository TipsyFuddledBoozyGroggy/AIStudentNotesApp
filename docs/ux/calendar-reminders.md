# Calendar reminders for syllabus-derived events: UX decision

Author: neurodivergent-ux-specialist, 2026-10-28. Delegated decision from the Product Owner. Status: **DECISION (design), not built, not prototyped, not tested.**

**Nothing here is human-validated.** Gate G4 (neurodivergent suitability and learning-science effectiveness) stays **NOT_RUN**. Every number below is a design default to be tested, not a finding. Real Google behavior is [VERIFY] until the fake-server and smoke tests in `docs/architecture/google-calendar-integration.md` run.

Inputs read: `CLAUDE.md` (invariants 4, 7), `docs/ux/student-experience.md` (rule 9, quiet mode), `docs/ux/phase2-syllabus-review.md` (sections 6, 11, 12), `docs/architecture/google-calendar-integration.md` (sections 4, 5, 7, 11).

Evidence labels: **[E]** published research, cited from memory and not re-fetched this session (verify before quoting externally); **[J]** my design judgement; **[C]** product/contract constraint.

## 0. Scope and constraints

- [C] Reminders are Google Calendar event reminders on Tether-created events. Google delivers them. Tether sends no push, email or SMS of its own. They survive a Tether outage.
- [C] Real Calendar needs `calendar.v1`. `syllabus.v1` has no reminder field (contract row 13). This doc defines what `calendar.v1` and the settings screen must carry. It authorizes no Google write.
- [C] One Google account per user. Events live on an app-created calendar per course (proposal). Only approved, resolved-date items become events (invariant 4).
- [C] If the student deletes or edits an event in Google, Tether leaves it alone, reminders included, and never recreates it.
- [C] Without read scope Tether cannot see the student's other events, so it cannot pick free time slots.
- Dependent teams: google-integration (reminder overrides, [VERIFY] behavior on secondary calendars), backend (preferences, planner), frontend (settings screen), qa (section 9), human-research-coordinator (section 10), Security (preference data is low risk; no new scope).

## 1. Default: reminders are ON, in a gentle preset (overrules the proposal)

**Decision.** The default preset is **Gentle**, pre-selected and visible on the sync confirm step. The student can switch to Off or Extra runway there, or later in Settings, in one step.

Why I overrule "off by default, opt-in":
1. [J] The students this serves often struggle to estimate time and to start early. A calendar full of deadlines with no lead-up leaves exactly the gap Tether exists to close. An opt-in buried in a confirm step is likely to be skipped. Research on default effects shows people mostly stay with defaults (Johnson and Goldstein 2003, organ donation; [E], general behavior, not ADHD-specific). I treat this as a hypothesis for this population.
2. [E] ADHD is associated with difficulty estimating and anticipating time and with weaker prospective memory (Barkley's executive-function model; Toplak and Tannock on time perception; [E], recalled, verify). External cues at the point of need are a standard accommodation. [J] I infer that an event with no lead-up cue is of limited use.
3. [J] The harm of this default is bounded. It is not a Tether notification. The student explicitly approved each item and pressed a button that names the reminder setting. Three or fewer reminders per day, none late at night, none repeating. One control turns everything off.
4. [J] The risk is real: some students will find any reminder noisy. That is why Off is one tap, why the preset is shown (never hidden) on the confirm step, and why this decision must be tested (section 10).

**Conflict I am flagging, not hiding.** `student-experience.md` rule 9 says notifications are off by default, and `phase2-syllabus-review.md` set reminders to None. Those were about Tether notifications and the mock. This decision treats an explicit, student-triggered calendar write with a shown reminder setting as different. **The Product Owner must confirm this reading.** If the PO disagrees, the fallback is Off by default with Gentle one tap away. That fallback needs no other change in this doc.

Quiet mode (invariant 7) governs Tether's own prompts. It does not mute Google. The UI states: "Reminders come from Google Calendar, not from Tether."

## 2. Presets and lead times

Three presets only. The student picks a preset, not a rule set.

| Preset | Meaning |
|---|---|
| Off | No reminders on Tether events. Events still appear on the calendar. |
| **Gentle (default)** | One early "runway" reminder and one next-day reminder for high-stakes items. Nothing in the final 24 hours. |
| Extra runway | Gentle plus an earlier cue and a morning-of cue. Maximum 3 per item. |

Lead times (day-based reminders fire at the student's reminder time, default **09:00 local**; see quiet hours below):

| Item type | Gentle | Extra runway |
|---|---|---|
| Exam | 7 days before, 1 day before | 14 days before, 7 days before, morning of |
| Assignment, project, other | 3 days before, 1 day before | 7 days before, 3 days before, morning of |
| Quiz | 2 days before | 3 days before, morning of |
| Reading | none | 1 day before |
| Class meeting (recurring) | none | none |

Reasons:
- [J] **Runway vs last-minute.** Early reminders let the student begin multi-step work. Last-minute alerts ("due in 2 hours") are most useful to someone already at risk and most likely to cause panic in someone anxious. Gentle has no sub-24-hour alert. The student opts into a morning-of cue through Extra runway. This is a hypothesis to test (section 10, Q3).
- [J] Class meetings get no reminder from Tether. Recurring class events would generate dozens of alerts a term and duplicate what the student's phone already does for classes they chose to add. Preset Off/Gentle/Extra never touch them.
- [J] Readings are low-stakes and frequent. Gentle skips them to protect the cap.
- [J] Quizzes are usually short-notice. A single 2-day cue avoids doubling up on a week that already has other items.
- Method is **popup only** (notification on the student's Google Calendar app or browser). No email by default; email adds an inbox burden. [VERIFY] popup behaves normally on an app-created secondary calendar, and that the Google Calendar mobile app delivers it.
- Minutes before: Google stores reminders as minutes before the event start. For all-day events that is measured from 00:00 of that day, so "3 days before at 09:00" is 4,320 − 540 = 3,780 minutes. The planner computes this from the event start and the student's reminder time. Google's maximum reminder lead is 4 weeks ([VERIFY], recalled), so 14 days is within range. Test DST days explicitly.
- **Quiet hours.** No reminder is planned between 21:00 and 08:00 local. A timed event at 07:00 with a "1 day before" reminder fires the previous day at the reminder time, never at 07:00. The student can change the reminder time (08:00 to 20:00) on the settings screen.

## 3. Alert-fatigue caps

- **Per item: maximum 2 in Gentle, 3 in Extra runway.** No repeats, no snooze chains, no escalation.
- **Per day: maximum 3 Tether reminders across all courses** (counted by the day the reminder fires, in the student's time zone).
- When more than 3 would land on one day, the planner drops reminders in this order until 3 remain: reading, quiz, assignment early cue, exam early cue. Exam reminders on the day-before slot are kept last. Dropping is deterministic and shown on the confirm preview: "To keep Tue, Oct 14 quiet, we skipped 2 reminders."
- [J] Tether cannot cap reminders from the student's own events or other apps. The cap is for Tether only.
- Caps are re-evaluated at every sync (new items, moved dates). A dropped reminder never silently reappears on a different day.
- Rationale: [E] alert fatigue (too many alerts reduce response to all) is well documented in clinical alert and notification research; the specific numbers 2, 3, 3 are [J].

## 4. Time-blindness aids

1. **Event title makes the type and course visible at a glance.** Format: `BIO 201 · Midterm exam`. Type is part of the title text (Exam, Due, Quiz, Reading), never only a color or icon. The title is fixed and does not hold a countdown, because a static title goes stale.
2. **Relative wording ("due in 3 days") is shown only where Tether renders it fresh**: the course card sentence "Next up: Midterm exam, in 3 days (Tue, Oct 14)." Computed at view time. Static text, no badge, no count, no color alarm. One next item per course. It shows the next approved synced item only.
3. **Google's own popup shows the title and the start time**, which Tether does not control. The event description carries a plain line: "Added by Tether from your syllabus. Starts Tue, Oct 14 at 10:00 AM." and a link back to the review screen. No guilt copy, no countdown.
4. **Work-session blocks (study time) are NOT in v1.** Decision: deadlines plus runway reminders only. Reasons: (a) without read scope Tether cannot find free time, and picking times blind would collide with classes and work; (b) a block the student does not do is a visible failure on their calendar, which risks shame; (c) it multiplies event volume against the 500/1,000 caps; (d) no validation yet. Backlog candidate after human research: an optional per-item "Add a study block" where the student picks the day and time, with no reminder on the block itself, and with the same tone rules. [E] Implementation intentions (concrete when-and-where plans) have helped children with ADHD follow through in small studies (Gawrilow and Gollwitzer 2008; [E], verify). That supports a student-chosen block later. It does not support auto-scheduled blocks.
5. All-day items in Google have no time. The confirm preview states "All day" and the reminder time of day, so the student is not surprised by a 09:00 alert.

## 5. Tone and wording rules

Apply to event titles, descriptions, the settings screen, preview and result copy.

1. Plain, neutral, factual. Grade 8 or lower, sentences under about 20 words.
2. Banned words and patterns (copy test): overdue, late, behind, missed, last chance, urgent, hurry, don't forget, you forgot, you haven't, still, only N days, running out of time, falling behind, deadline looming, streak, keep it up, perfect, exclamation marks, all caps, countdown language in static text.
3. No streaks, points, badges, completion rates, "you've handled N reminders", or reminder-acknowledgement tracking. Tether does not learn whether a reminder was seen or acted on.
4. No follow-up if an item passes. No "you missed this" message. Past items are simply past.
5. No reminder content about the student's performance or workload. Only what, which course, when.
6. Settings copy describes what happens, not what the student should do. "Gentle: a heads-up a week before exams and a day before." not "Stay on top of your exams."
7. Never imply Tether watches the student. No attention or activity inference drives reminders (invariant 7).

Draft strings (all DRAFT, for `copy.ts`; the plain-language and tone test runs on them):
- `rem.h1`: "Calendar reminders"
- `rem.lead`: "Reminders come from Google Calendar, not from Tether. They only apply to events Tether added."
- `rem.preset.off`: "Off. No reminders."
- `rem.preset.gentle`: "Gentle. A heads-up early, and one the day before for exams and assignments."
- `rem.preset.extra`: "Extra runway. More notice, and a morning-of reminder."
- `rem.summary`: "Exams: 7 days and 1 day before. Assignments: 3 days and 1 day before. Quizzes: 2 days before."
- `rem.time.label`: "Reminder time"
- `rem.time.hint`: "Reminders fire at this time. We don't send any between 9 pm and 8 am."
- `rem.capNote`: "We send at most 3 reminders on any one day."
- `rem.saved`: "Saved. 14 events already in Google keep their old reminders."
- `rem.update`: "Update those too"
- `rem.off.applied`: "Reminders are off. We removed them from 14 events."
- `rem.pause`: "Pause reminders until"
- `rem.googleMute`: "You can also mute the calendar in Google: open Google Calendar settings, choose the Tether calendar, then Notifications."
- `rem.skippedDay`: (date, n) => `To keep ${date} quiet, we skipped ${n} reminders.`
- `rem.uncertain`: "Items with no clear date have no event and no reminder."

## 6. The settings screen (one screen, low effort)

Route `/settings/reminders`. Also shown as a compact panel on the sync confirm step ("Reminders: Gentle. Change") with the same radio group. One `h1`, one `main`.

```
h1  Calendar reminders
p   Reminders come from Google Calendar, not from Tether. They only apply to events Tether added.

Reminders      (o) Off   (o) Gentle   ( ) Extra runway          (radio group, "Reminders")
  Gentle. A heads-up early, and one the day before for exams and assignments.
  Exams: 7 days and 1 day before. Assignments: 3 days and 1 day before. Quizzes: 2 days before.

Reminder time  [ 9:00 AM v ]   We don't send any between 9 pm and 8 am.
We send at most 3 reminders on any one day.

[ Save ]      Saved. 14 events already in Google keep their old reminders.  [ Update those too ]

Courses  (disclosure, collapsed)
  Biology 101      [ Use my setting v ]    (Use my setting / Off / Gentle / Extra runway)
  History 210      [ Off v ]

Pause reminders until [ date ]   [ Pause ]       (shown as "Paused until Nov 3. Resume")

How to mute in Google instead (disclosure, plain text)
```

Behavior (defaults, not options):
- **Off, Pause and per-course Off apply to existing Tether events immediately.** The student's own explicit action, and the lowest-harm direction. Result line states the count removed or "We'll finish removing these when Google is reachable." if offline. Never a silent partial state.
- **Any other change (preset, time, per-course non-Off) applies to future syncs only.** Existing events keep their reminders until the student presses "Update those too" (explicit, because it is an external write). That button is off the keyboard path until a change exists.
- Changing the preset never rewrites an event the student edited in Google. Those are listed as "Left alone: 2 events you changed in Google."
- Per-course override sits under a closed disclosure; the default view is one decision. A course set to Off shows "Off" as text next to the name.
- Pause is until a date (max 60 days) or until the student resumes. Pausing is applied by removing reminders from existing events and withholding them from new ones; Resume restores them on the next sync. [J] A pause that expires on its own and restores reminders needs a background job; ship Resume as a manual button first. Open item for backend.
- The "mute in Google instead" disclosure matters: it works even if Tether is down or disconnected (the PO fact that everything lives in Google).
- Settings save on press of Save. The previous value is kept after a failure with "Couldn't save. Try again." Unsaved radio changes survive an accidental navigation as a local draft ("We kept your changes.").
- Focus: after Save, focus stays on Save and the polite status region reads one message. Disclosures use native `aria-expanded` buttons.
- Disconnecting Google removes the mapping; reminders on kept events are Google's from then on and Tether states: "Reminders on events you kept stay in Google."

## 7. Quiet mode and per-course overrides

- Tether's engagement levels (Quiet, Balanced, Active) do not change the reminder preset. They govern Tether's own prompts only. Reason: reminders are a Google-side artifact the student chose in an explicit step, and changing them when a lecture-mode switch changes would be a surprising external write.
- Quiet mode (the default) therefore means: Tether adds no extra nudge about reminders, no "reminders are off" prompt, no review-your-reminders prompt, no weekly summary. Balanced/Active may later offer an opt-in single weekly summary. Not designed here.
- A student who wants silence everywhere picks Off. A student who wants only certain courses picks per-course Off.
- Precedence: Pause > per-course setting > global preset. Shown in one sentence on the screen, not as a diagram.
- No setting is inferred from behavior. Tether never lowers or raises reminders because the student seems stressed, absent or distracted.

## 8. Uncertain dates, rescheduled and removed items

**Uncertain dates (decision: never remind, no in-app nudge beyond what exists).**
- An item without a resolved date has no Google event, therefore no reminder (invariant 4). This is enforced server-side; the UI cannot override.
- Tether adds **no new "confirm this date" nudge**. The existing static line on the review screen ("3 need a date" with "Show them") and the course card is the only surface. A new nudge would be a pressure signal about unfinished work.
- "Probably right" and "Please check" items the student approved and resolved get normal reminders. The student made the call; the preview shows the date.
- An item whose date is still relative ("Week 5") and unresolved stays out.

**Rescheduled items (student edits date in Tether).**
- Item becomes "Changed since it was added". Nothing in Google changes until the student confirms the update (contract 5; no auto-sync after edits).
- On confirm, the event patch moves the event and the planner recomputes reminders from the new date. Reminders already past are not fired. Tether adds no "this moved" notification. A single line is appended to the event description: "Date updated Oct 20."
- If the new date makes a runway reminder impossible (for example 3 days before when the item is now in 2 days), that reminder is skipped, not squeezed in. The preview says so: "No 3-day reminder, since this is now 2 days away."
- If the student moved or edited the event in Google first, Tether leaves it and its reminders alone and shows "You changed this in Google. We left it alone." (integration doc section 4).

**Removed or deleted in Google.** Tether never recreates it and never reminds about it. Tether cancels no other reminder as a result.

**Cancelled in syllabus (student skips or removes the item).** Event removal follows the existing remove flow. Reminders vanish with the event.

## 9. Accessibility (WCAG 2.2 AA) and acceptance criteria

Settings UI requirements ([J] targets; to be audited, not claimed):
- Radio group is a native `fieldset` with `legend` "Reminders"; arrow keys move; selection never navigates or saves (SC 3.2.2). Each radio's visible label contains the preset name; description linked with `aria-describedby`.
- Reminder-time `select` is native, labeled, 44 px high. No custom time picker. Per-course `select`s are native with the course name in the accessible name ("Reminders for Biology 101").
- Targets at least 44 by 44 px (SC 2.5.8 floor 24 px). No dragging, no gesture-only controls (SC 2.5.7).
- Text contrast 4.5:1, controls and focus indicators 3:1, focus not obscured (SC 2.4.11), no state by color alone (SC 1.4.1: "Paused until Nov 3" and "Off" are words).
- 320 px reflow and 400% zoom without horizontal scroll; 200% text resize; text spacing survives (SC 1.4.4, 1.4.10, 1.4.12). No hover-only content.
- Status messages use the single polite `role="status"` region; each save produces one message; `role="alert"` only for a failed save.
- Errors are text beside the control with a suggestion (SC 3.3.1, 3.3.3). Saving is reversible: Off and Pause can be undone by the same control (SC 3.3.4).
- No timeouts or auto-dismissing toasts (SC 2.2.1). Reduced motion and forced colors: no animation present.
- Consistent location of Settings link and help (SC 3.2.6). No re-asking for known data (SC 3.3.7).

Acceptance criteria for qa-engineer (automated unless noted; fixtures use the mock/fake-Google server, never real Google):
1. A new sync confirm step shows Gentle pre-selected, with the exact summary sentence from section 5, and the Add button's label names the item count. Reminder preset is visible without opening any disclosure.
2. Gentle plan for a fixture (1 exam, 1 assignment, 1 quiz, 1 reading, 1 class meeting): exam has exactly 2 reminders (7 d, 1 d), assignment 2 (3 d, 1 d), quiz 1 (2 d), reading 0, class meeting 0.
3. Extra runway plan: no item exceeds 3 reminders; reading has 1; class meeting still 0.
4. Off produces events with an explicit empty reminder override (no calendar-default reminders leak in) [VERIFY on fake server and in smoke test].
5. Per-day cap: a fixture putting 6 reminders on one day yields exactly 3 for that day, in the drop order of section 3, and the preview lists the skipped count. Cap holds across two courses.
6. Quiet hours: no planned reminder fires between 21:00 and 08:00 local in any fixture, including a timed event at 07:00 and a DST spring-forward day. The reminder-time `select` allows only 08:00 to 20:00.
7. All-day and timed events compute the correct minutes-before (4,320 − 540 = 3,780 for all-day 3 days at 09:00); the value never exceeds Google's stated maximum.
8. Only popup method is written; no email reminder is written (assert on payload).
9. An item with no resolved date: zero events, zero reminders, zero calls to the calendar gateway (negative test), and no new nudge string appears in any view.
10. Reschedule: editing a date does not change Google until the student confirms; after confirm the event moves, reminders are recomputed from the new date, past ones are skipped, and the description gains one "Date updated" line.
11. A student-edited or student-deleted event in Google is not patched or recreated by any preset change or reschedule, and appears in the "Left alone" list.
12. Off, Pause and per-course Off remove reminders from all existing Tether events for the scope chosen and report the count; if the gateway is unavailable the state reads as pending, not done.
13. Preset change (other than Off), time change, or per-course non-Off change does not touch existing events until "Update those too" is pressed.
14. Precedence: Pause beats per-course beats global (table test).
15. Copy test over every string in section 5 and all reminder-related event titles and descriptions: none of the banned words/patterns, no exclamation marks, sentences under about 20 words, grade 8 or lower.
16. No streak, count of handled reminders, acknowledgement tracking or "reminders you've seen" field exists in the data model or UI (schema and DOM assertion).
17. Quiet mode check with default settings across a full session: Tether produces zero push, email, sound, badge or dashboard count related to reminders. Network spy shows no Tether-originated notification channel.
18. The course-card "Next up ... in N days" line is computed at render (changing the clock changes the text), is static text with no badge or color alarm, and shows one item.
19. Settings keyboard-only: every control reachable and operable, no trap, focus never hidden under sticky bars at 320 px and 400% zoom; one status announcement per save.
20. axe: zero serious/critical violations on the settings screen and the confirm-step panel in every state (Off, Gentle, Extra, Paused, per-course open, save error).
21. Draft persists across navigation and a failed save keeps the prior saved value.
22. Manual, human-only, NOT RUN: NVDA, JAWS, VoiceOver (macOS and iOS), TalkBack on the settings and confirm flow; a real-account check that Google actually fires a popup at the planned time on desktop and mobile (smoke test, PO approval required). An AI/axe pass does not close these.

## 10. Questions for real students and clinicians (human-research-coordinator)

Recruitment, consent and any compensation need Product Owner approval. Results are design evidence only and must not be presented as clinical claims.

Neurodivergent students (varied presentations, include some who say they dislike reminders):
1. Would you want reminders on your syllabus events turned on before you've seen them, or would you rather choose? What would make you turn them off in the first week?
2. Which lead times help you start (3 days? a week? two weeks?) and which feel like pressure? Does it differ for exams and assignments?
3. Is a morning-of cue helpful, neutral, or stressful? Is it worse if it is the first cue you see?
4. How many Tether reminders in a day before you stop reading them? Does the cap of 3 feel right? Does a Google notification from Tether feel different from your other alerts?
5. Do you want a study-time block placed on your calendar? Who should choose the time? What does it feel like when one goes unfinished?
6. Is "BIO 201 · Midterm exam" enough to tell at a glance what a popup means? What wording would you want in the event?
7. Which time of day would you accept (our default 9 am)? Is it different for evening or night people?
8. After an interruption, can you find where to mute everything in under a minute? Can you tell what is paused and what is not?
9. If a date changes, what do you want to happen to the reminder? Do you want to be told?
10. Does the "mute in Google instead" route make sense to you? Would you use it?
11. Does any phrase feel judging, nagging or too cheery?

Clinicians and learning-science reviewers:
12. Is Gentle (no sub-24-hour alert) appropriate, or do some presentations need a last-hour cue? Is there risk from an neurodivergent-and-anxiety combination?
13. Is the default-on choice defensible, or does it risk reminder resistance? What evidence exists for default-on reminders in this population?
14. Are the time-blindness aids sufficient (relative text in-app, fixed titles in Google)? Is there harm from dropping reminders silently when the daily cap applies, given the preview note?
15. Are there populations (for example students with anxiety, trauma around deadlines, or sensory sensitivity to notifications) for whom Gentle should not be the default?

Measures to propose to the researchers: first-week opt-out rate, setting changes, self-reported pressure/calm, deadlines noticed in time. Never collected passively in production without separate consent.

## 11. Dependencies and open questions for the Lead Architect / Product Owner

1. PO: confirm the default-on reading of rule 9 (section 1), or choose the fallback.
2. `calendar.v1` must carry: preset, reminder time, per-course override, pause, per-event reminder overrides, and the cap/skipped-reminder data in the preview. I did not decide this contract.
3. Google behaviors to verify: popup reminders on a secondary calendar, empty override via `reminders.useDefault=false`, 4-week maximum, delivery on the mobile app, and that Tether's later patch does not clobber a student-edited reminder.
4. Pause auto-resume needs a background job. Not decided; ship manual Resume first.
5. Preferences data model is `calendar_prefs` (integration doc section 6) extended with per-course rows. database-architect to decide; my need is that preference changes do not trigger writes without the section 6 rules.
6. Reading-level script and copy tests have not been run on these strings.

## Not done

No prototype, render, axe run, screen-reader or device pass, no student or clinician review, no real Google call, no code. G4 NOT_RUN.

## Summary

1. Reminders are ON by default in a pre-selected, visible Gentle preset (overrules the proposal; PO must confirm the reading of rule 9, fallback is Off).
2. Gentle: exam 7d and 1d, assignment 3d and 1d, quiz 2d, reading and class meetings none; Extra runway adds earlier and morning-of cues; max 2 per item (3 in Extra), max 3 per day, none from 21:00 to 08:00, popup only.
3. Time-blindness aids are fixed typed titles, a freshly computed "in N days" line in-app only, and runway reminders; study-time blocks are deferred (no free/busy visibility, unvalidated).
4. One settings screen (Off/Gentle/Extra, reminder time, per-course overrides, pause, "mute in Google"); Off and Pause apply to existing events at once, other changes apply to future syncs until "Update those too"; uncertain dates never get events or reminders and no new nudge; reschedules wait for the student's confirm.
5. All numbers are design judgement, nothing is human-validated (G4 NOT_RUN); 22 acceptance criteria and 15 research questions are listed for QA and human-research-coordinator.
