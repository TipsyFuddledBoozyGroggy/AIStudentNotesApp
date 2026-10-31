# After Stop: move to the lecture's own page (PO-19)

Status: AI SPECIALIST DESIGN, 2026-11-01. Author: adhd-ux-specialist. Audience: Lead Architect, Product Owner, frontend-engineer, qa-engineer.

**Nothing here is human-validated (gate G4 NOT_RUN).** No student, screen-reader or real-device evidence exists for any choice below. Tags: [J] design judgement, [C] project constraint (CLAUDE.md invariant 7; the three-live-region rule; focus rules in `LectureScreen.tsx`; `docs/ux/language-guide.md`, binding for every string). **Gate G1 is not touched:** no string below discloses recording, consent, retention or the Demo source; the Stop confirm wording, `STOPPED_COPY`, `ConsentStart.tsx`, `ConsentDialog.tsx` and the Demo note are byte-identical after this work. The new strings are normal copy, not DRAFT.

Read: `docs/DECISIONS.md` PO-19; `language-guide.md`; `redesign-round2.md` (dialog primitive, slice 8a deviation 8/9, slice 8x hub-race notes and the Join limit); `make-notes-rail-demo-notice.md` section 1; `m2-live-lecture-screens.md` (Stop flow, lines 18, 140, 279); code: `LectureScreen.tsx`, `Notices.tsx` (`StopDialog`, `StoppedPanel`), `StartFlow.tsx`, `LectureStartPage.tsx`, `LectureReopenPage.tsx`, `useLectureSession.ts` (`stop`, queued stop, `focusIntent`, `PENDING_MAX_MS`), `useNotes.ts`, `settings.ts`, `lib/design/prefs.ts`, `PrefsProvider.tsx`, `PreferencesPanel.tsx`, `prefsCopy.ts`, `ui/Switch.tsx`, `syllabus/storage.ts`, `AuthProvider.tsx`, `lib/copyLint.ts`, `e2e/lecture.spec.ts` (`stopLecture`), `e2e/notes.spec.ts`. No browser, server or test was run; no source file was edited.

Assumptions: the live route is `/courses/{id}/lecture` (`StartFlow` renders consent, blocked or `LectureScreen` in one history entry); the reopen route `/lectures/{sessionId}` renders the same `LectureScreen` from `GET /sessions/{id}`; `runtime.sessionId` is available on the live screen; `AuthProvider` exposes the user id. Dependent teams: frontend-engineer builds; qa-engineer validates. No API, contract or backend change.

---

## 0. The decisions in one paragraph

The redirect notice is **a separate dialog that opens when the student's own Stop reaches `Stopped`**, not a line in the Stop confirm and not a dialog on a timer. The Stop confirm stays exactly as it is (one safety question, "Keep recording" focused). When the server confirms the stop the student asked for, the Stopped screen renders behind a small dialog: "This lecture has its own page", two sentences, the "Don't show this again" checkbox, "Open the lecture's page" (primary, focused) and "Stay here". Nothing counts down, nothing auto-closes, nothing navigates without a press. Once the box is checked and the page opened, later stops replace the URL directly the moment `Stopped` arrives, and focus lands on the "Lecture stopped" heading on the lecture's page with one status announcement. System stops (auto-stop, hard cap, abandoned, Failed), queued offline stops and stale arrivals never open a dialog or move the page; the Stopped note on the live route carries a plain link "Open this lecture's page" instead. The choice is a user-scoped preference, kept across sign-out, reversible with one switch in Preferences.

---

## 1. Sequencing (decision 1)

### Options weighed
| Option | What the student sees | Why not / why |
|---|---|---|
| A. Fold into the Stop confirm | One dialog: "Stop and save this lecture?" plus a sentence about the move plus the checkbox | One dialog is fewer steps, and it is the cheapest build. Against: the Stop confirm is a **safety question** (microphone off, recording ends) and today has one idea and two buttons. Adding a navigation paragraph and a checkbox gives it three ideas, lengthens the moment of decision, and puts a checkbox into a dialog pattern that elsewhere in this app means consent (`ConsentDialog`). The checkbox would also persist on "Keep recording", which is confusing. The confirm is also absent on every system stop, so A cannot cover those anyway. Rejected [J]. |
| B. Second dialog before the stop runs | Stop confirm, then the notice, then the stop | Two dialogs in a row to do one thing; and the stop (mic off) must not wait for an information dialog. Rejected. |
| **C. Notice when the stop the student asked for is confirmed** | Stop confirm; "Stopping..." in the bar (as today); the Stopped screen with the notice over it | The notice opens at the same moment today's code moves focus to the Stopped heading (`focusIntent === "stopped-heading"` resolving on `kind === "Stopped"`), which the focus rules already count as the student's own action completing. The Stop confirm is untouched. One extra press, only until the box is checked. **Recommended.** |

### Exact sequence (option C)
1. Student presses **Stop** (bar, Paused note or Mismatch note) → the existing `StopDialog` opens, unchanged.
2. Student presses **Stop** in it → `s.stop()` as today: mic off at once, `pending = "stopping"`, `focusIntent = "stopped-heading"`. The redirect **arms** at this press only if the stop is not queued (`stopQueued` false) and no fatal state exists.
3. While `pending === "stopping"` the bar says "Stopping..." as today. No dialog yet.
4. `Stopped` arrives while armed (within `PENDING_MAX_MS`, the existing 3 s bound; after it `pending` clears and the arm is dropped):
   - **flag not set:** the Stopped screen renders; the **move notice** opens; focus goes to its primary button. `focusIntent` is cleared without focusing the heading.
   - **flag set:** no dialog. `router.replace('/lectures/{sessionId}')` fires in that same effect. The live entry is replaced.
5. If the stop fails (`fail()` → `actionError` note) or `Stopped` never arrives in the window: nothing opens, nothing moves, the screen shows what is true, and the Stopped note (when it comes) shows the link.

Never: a dialog or a navigation from a system event. Auto-stop after 30 min paused, the 3 h hard cap, `abandoned`, server `Failed`, a queued offline stop completing later, and a reopen/reload of an already-stopped session all keep today's behaviour plus the link in the Stopped note (section 5). Reason [C]: a modal opening is a focus move, and system changes never move focus; a student reading a transcript when the hard cap fires must not be carried to another page.

---

## 2. Copy (decision 2)

New registry `STOP_MOVE_COPY` in `components/lecture/copy.ts`, linted by `copy.test.ts` (FORBIDDEN list, 20 words a sentence). Checked by hand: no exclamation marks, no "just", "still", "right now", "behind", "missed", "lost", "you should"; every subject is the app, the page or the student's own press. These strings do **not** touch consent, recording or Demo wording, so they are normal copy (not DRAFT). G4 applies as to everything.

| Key | Exact string | Role |
|---|---|---|
| `heading` | `This lecture has its own page` | h2, `id="stop-move-h2"`, `display text-xl`, labels the dialog |
| `body` | `Your transcript, summary and notes are kept on it. Reloading that page shows them again.` | first paragraph |
| `bodyHere` | `This screen is for starting a lecture. Reloading here shows the start screen instead.` | second paragraph, `text-muted`; the truthful cost of staying |
| `checkbox` | `Don't show this again` | native checkbox label (PO-19 wording) |
| `checkboxHint` | `Next time, the lecture's page opens as soon as the lecture stops.` | `text-sm text-muted`, the checkbox's `aria-describedby` |
| `open` | `Open the lecture's page` | primary button; receives focus on open |
| `stay` | `Stay here` | secondary button; Escape does the same |
| `STOPPED_COPY.openPage` | `Open this lecture's page` | link in the Stopped note on the live route only (section 5) |
| `ANNOUNCE.movedAfterStop` | `Lecture stopped. This is the lecture's own page.` | the single status announcement, on the reopen page, once |

Preferences strings go in `PREFS_COPY` (`prefsCopy.ts`, linted by `ThemePicker.test.tsx`):

| Key | Exact string |
|---|---|
| `lecturesLegend` | `Lectures` |
| `stopMoveLabel` | `Ask before opening the lecture's page after Stop` |
| `stopMoveHint` | `On: a short note explains the move and waits for your choice. Off: the page opens on its own after Stop.` |
| `stopMoveMsg.on` | `Ask before opening the lecture's page: on.` |
| `stopMoveMsg.off` | `Ask before opening the lecture's page: off.` |

Wireframe of the dialog (`.dialog-scrim` + `.dialog`, 28rem, bottom sheet below 640, centred from 640):
```
+--------------------------------------------------+
| This lecture has its own page                    |  h2
| Your transcript, summary and notes are kept on   |
| it. Reloading that page shows them again.        |
| This screen is for starting a lecture. Reloading |  muted
| here shows the start screen instead.             |
| [ ] Don't show this again                        |  44px row
|     Next time, the lecture's page opens as soon  |  hint
|     as the lecture stops.                        |
|              [[ Open the lecture's page ]] [ Stay here ] |
+--------------------------------------------------+
```

---

## 3. Behaviour (decision 3)

- **Modal, one at a time.** The move notice is the second and last modal in the lecture flow; it can never be open together with the Stop confirm (the confirm closed on the press that armed it). Same primitive as `StopDialog`: `role="dialog"`, `aria-modal="true"`, `aria-labelledby="stop-move-h2"`, Tab cycles **all** focusables inside (the existing trap queries `button` only; it must include the checkbox: `input, button`).
- **Open:** focus on "Open the lecture's page". No announcement at open (the dialog name carries it). The Stopped screen behind it is the truthful state (bar "Stopped", the Stopped note, the Notes block if the AI port exists).
- **Enter / primary:** if the checkbox is checked, save the flag (section 4) first; then `router.replace('/lectures/{sessionId}')`; write the one-shot arrival marker (below). The dialog unmounts with the page.
- **Stay here / Escape:** close; the checkbox value is **discarded** (the flag means "next time move without asking", which is not what a student who chose to stay has said). Focus goes to the Stopped heading `#stopped-h2`, where it goes today after Stop (the opener, the confirm's Stop button, no longer exists). The Stopped note shows the link, so the move is one press away at any time.
- **Backdrop press:** nothing, as the existing dialog (a thumb above a bottom sheet must not dismiss a choice).
- **Reduce motion / Low-stimulation:** no entry or exit animation exists to remove; the scrim stays (it is what makes the dialog modal); paint-only differences across the ten looks; the recording pill behind it is untouched (it reads "Stopped").
- **Arrival on the reopen page.** `LectureStartPage` writes `sessionStorage['tether.lecture.arrivedFromStop.v1'] = sessionId` immediately before `router.replace`. `LectureReopenPage` reads and **removes** it on mount; if it equals the session id, it passes `arrivedFromStop` to `LectureScreen`, which (a) sets the initial `focusIntent` to `"stopped-heading"` so focus lands on `#stopped-h2` when the screen shows `Stopped` (the existing rule), and (b) calls `announcer.status(ANNOUNCE.movedAfterStop)` **once**. A later reload has no marker: no focus move, no announcement, the plain reopen behaviour. Exactly one status announcement in the whole sequence; `role=alert` is not used; **three live regions** stay three [C] (the dialog, the link and the switch add no `aria-live`).
- **Back button after the replace:** the live route's entry is gone, so Back goes to the page before it, normally the course page `/courses/{id}` (pressing "Start recording" never navigated: consent and live share one entry). Back never shows the start screen for a stopped lecture. Forward is empty.
- **If the move does not finish:** `router.replace` is client-side; the reopen page's own states cover what can go wrong: `GET /sessions/{id}` 404 → "We can't find that lecture" + "Back to dashboard"; other failure → "We couldn't load this lecture"; 401 → `useRequireUser` sends the student to sign in. Nothing is deleted in any path: the stop was confirmed by the server before the move began. No new error wording. If the hub Join is rate-limited (slice 8x finding: more than 10 Joins a minute) the bar reads "Not recording" until a retry succeeds; `focusIntent` then resolves when `Stopped` arrives. Pre-existing; noted for the Lead (a Stopped hint from the REST session would close it).
- **Stale session list:** the course page lists sessions from the server on mount; the new stopped session is there when the student returns with Back or "Back to course". The redirect reads nothing from a list.
- **Phone (390 / 320):** the dialog is a bottom sheet (existing `items-end` below 640); the checkbox row is a 44px target; the two buttons wrap to full width at 320; no horizontal overflow; the sheet is under 70% of the viewport height at 320 with text size Large (five short lines plus controls).
- **Keyboard order inside:** heading (not focusable) → checkbox → "Open the lecture's page" → "Stay here" → back to the checkbox.

---

## 4. "Don't show this again" persistence (decision 4)

- **It is a preference, not a draft.** It holds no student content, so SB-06's privacy reasons (purge other users' keys, clear on sign-out) do not apply in full. But it changes **navigation**, which is more consequential than paint, so it must not leak from one student to the next on a shared computer the way `tether.prefs.v1` (device-wide) would. Decision: **user-scoped, device-local, kept across sign-out.**
- **Key:** `tether.lecture.u.<seg(userId)>.stopMove.v1`, value `"direct"`. Absent = ask (default). `seg()` is the dot-free segment used by `syllabus/storage.ts`, so one id cannot address another's key. Helpers `loadStopMove(userId)` / `saveStopMove(userId, direct)` in `components/lecture/settings.ts`, `try/catch` around every access like the rest of that file.
- **Not cleared on sign-out** [J]: clearing would re-ask every session, which contradicts "once checked the dialog never opens again" and costs the student a press for no privacy gain. Not purged by `setDraftOwner` either (not content). Not part of `LOOK_KEYS`, so "Reset look to defaults" leaves it alone (its hint already says lecture settings stay).
- **No user id known** (auth not loaded, storage blocked): read as "ask"; the checkbox still shows, but a save that cannot land means the dialog returns next time. Acceptable and silent; the Preferences switch shows the true stored state.
- **Reversible in Preferences:** `PreferencesPanel` gets a third section `h3.label` **"Lectures"** between Look and About, with one `Switch`: label `PREFS_COPY.stopMoveLabel`, hint `stopMoveHint`, `checked = !direct` (On = ask, the default), announcing `stopMoveMsg.on/off` through the panel's existing status line (it is never mounted on lecture routes, so the three-region rule holds). Turning it On removes the key. The lecture settings popover does **not** get a copy (one place to change it; the popover is for things that act during the lecture).

---

## 5. The Notes block and in-flight Make notes across the replace (decision 5)

- **Both routes render the same `NotesPanel`**; the server holds the truth. Across `router.replace` the live screen unmounts: `useNotes` sets `mounted = false`, the poll stops, no further `setState`. The reopen page mounts a fresh `useNotes`: one `GET /notes`, then the state as the server has it (Pending resumes polling; Ready renders; nothing announced for a state read on mount, slice 8a deviation 4). **No POST anywhere in the move** (the existing spy test "unmount and reopen" already covers this shape; extend it with the redirect prop).
- **A press in flight when the student moves** ("Stay here", then Make notes, then the link): the POST still lands; the unmounted hook ignores the answer; the reopen page's mount GET shows Pending or Ready. The Idempotency-Key is dropped with the unmount; a new press on the lecture's page is a new intent, and the server's one-pending-press rule applies as today. The Pending announcement is not repeated on the new page.
- **Direct path (flag set):** the live route may issue one `GET /notes` in the render before the replace. Accepted: a GET only, no layout the student sees for more than a frame. Do not special-case it.
- **Behind the dialog:** the Notes block is visible under the scrim; it is the real state and nothing in it is reachable while the dialog is open.
- **Reload on the reopen route:** no marker, no dialog, no announcement, no focus move; the Notes block shows the server state via GET, as `e2e/notes.spec.ts` already asserts.
- **Stopped note link (live route only).** `StoppedPanel` gains `openPageHref?: string`; when present it renders a `next/link` styled `btn-tertiary` after "Back to course": `Open this lecture's page`. Provided by `StartFlow` for every Stopped and Failed rendering on the live route; the reopen page passes nothing, so the link never appears on the page it points to. This is the path for "Stay here", for every system stop and for a queued offline stop.

---

## 6. Acceptance criteria (frontend and QA)

Unit (`vitest`):
1. `StopDialog` renders byte-identical markup and strings; `STOP_DIALOG_COPY`, `STOPPED_COPY` (except the new `openPage` key), consent files and the Demo note show no diff.
2. `copy.test.ts`: `STOP_MOVE_COPY`, `STOPPED_COPY.openPage` and `ANNOUNCE.movedAfterStop` pass the FORBIDDEN lint and the 20-word rule, pinned word for word to section 2; `ThemePicker.test.tsx` lint covers the five new `PREFS_COPY` strings.
3. `LectureScreen.test.tsx`: Stop → confirm → fake runtime reaches `Stopped` within the pending window → a `role="dialog"` named "This lecture has its own page" is open, `document.activeElement` is "Open the lecture's page", the Stopped heading was **not** focused, the status region received nothing; Escape closes it and focuses `#stopped-h2`; "Stay here" with the box checked saves **no** key; "Open the lecture's page" calls the `onOpenPage` prop once, and with the box checked `loadStopMove(userId)` is `"direct"` afterwards.
4. Flag set: Stop → confirm → `Stopped` → `onOpenPage` called once in the same tick as the state change, no dialog rendered at any point.
5. Never opens: `Stopped` with reason `auto_stop_paused_timeout`, `hard_cap`, `abandoned`; server `Failed`; a queued stop (`connection` disconnected at the press) that completes later; `Stopped` arriving after `PENDING_MAX_MS`; a screen mounted already Stopped. In each case the Stopped note shows the link when `openPageHref` is given and not when it is absent.
6. `arrivedFromStop`: focus lands on `#stopped-h2` once `kind === "Stopped"`, the status region receives `Lecture stopped. This is the lecture's own page.` exactly once; without the prop neither happens; the marker is removed from `sessionStorage` on first read.
7. Tab inside the dialog cycles checkbox → primary → secondary → checkbox, both directions; backdrop click changes nothing.
8. `useNotes` spy: zero `requestNotes` across Stop → dialog → open → unmount → remount with Pending from the mock; the remount issues one GET and resumes the poll.
9. `settings.ts`: key shape `tether.lecture.u.<seg>.stopMove.v1`; dot escaping; throwing storage never throws out; `saveStopMove(id, false)` removes the key; `PreferencesPanel` switch reads `!direct`, writes through the helper, announces `stopMoveMsg`; "Reset look to defaults" leaves the key.
10. Live regions: exactly three on the Stopped screen with the dialog open and after "Stay here" (existing count test extended); the Preferences panel adds none to lecture routes.

Harness (`playwright.harness.reuse.config.ts`, fake runtime, `--workers=1`):
11. `live` fixture: Stop → confirm → dialog visible; axe (wcag2a/aa/21aa/22aa/best-practice) 0 violations with the dialog open at **390 and 1280 in all ten looks** (add the state to `lecture-looks.spec.ts`, driven through the UI, not a test-only prop); the dialog box is on the 4px grid, every control at least 44px, no horizontal overflow at 320 with text size Large; the recording pill behind it unchanged.
12. Low-stimulation on and off: dialog geometry equal within 1px; reduce motion: no transition on the dialog or scrim.

Real stack (`playwright.reuse.config.ts`, mock provider, own e2e user `stopmove`):
13. Consent → record → Stop → confirm → dialog → "Open the lecture's page" → `page.url()` ends with `/lectures/{sid}`; `state-label` "Stopped"; the Notes block with "Make notes" present; `history.length` equal to before the stop; `page.goBack()` lands on `/courses/{id}` (not the start screen).
14. `page.reload()` on `/lectures/{sid}`: "Stopped", the Notes block, no dialog, no `data-region="status"` text about the move.
15. Second lecture, same user: Stop → confirm → check the box → "Open the lecture's page". Third lecture: Stop → confirm → URL becomes `/lectures/{sid3}` with **no dialog**; focus is on the "Lecture stopped" heading; the status region shows the announcement once.
16. Preferences: open the panel from the course page; the "Lectures" switch reads Off; turn it On; fourth lecture: the dialog is back. Switch Off from Preferences, fifth stop: no dialog.
17. "Stay here" path: after Stop → confirm → "Stay here", the URL is still the live route, focus is on `#stopped-h2`, the link "Open this lecture's page" is in the Stopped note and leads to `/lectures/{sid}` with `router.push` semantics (a Back from there returns to the live route's Stopped screen, which is what the student chose).
18. Existing specs: `e2e/lecture.spec.ts` `stopLecture()` gains the dialog step (press "Open the lecture's page" and assert the URL), with the reason in a comment; `e2e/notes.spec.ts` presses Make notes on the lecture's page after the move; `e2e/reopen.spec.ts` unchanged. No existing assertion loosened; each edit says why.

Not run by me: anything. Not verifiable here: screen-reader reading of the dialog and the announcement (G3), students' reading of the two sentences (G4).

---

## 7. Open questions
1. **Product Owner:** PO-19 says "when a lecture is stopped". This spec moves the page only for a stop the student pressed and confirmed, with system stops and queued offline stops getting the link instead (section 1). Confirm, or ask for the move on system stops too (I advise against: a page change on a system event moves focus and position without a press).
2. **Lead:** the one-shot `sessionStorage` marker is the mechanism for "focus on the Stopped heading plus one announcement, but not on reload". If `router.replace` can carry state in this Next version, that is equivalent; either is fine.
3. **Lead:** the hub Join limit can show "Not recording" on the lecture's page right after the move if a student reloads many times in a minute; the REST session already says `Stopped`. Out of scope here; recorded in slice 8x.

## 8. Questions for real students (G4; add to the existing lists)
1. After pressing Stop, did the note tell you where your lecture went and why? Did "Stay here" feel like a real choice?
2. Did you check "Don't show this again" the first time? If so, was the next stop's page change expected?
3. Did the sentence about reloading make sense, or did you not care about reloading at all?

## Summary
1. One new dialog, opened only when the student's own confirmed Stop reaches `Stopped` within the existing 3 s pending window; the Stop confirm is untouched; no timers, no automatic navigation, no system-triggered opening; system and offline stops get a plain link in the Stopped note.
2. Nine lecture strings and five Preferences strings, all plain, all outside G1, pinned for `copy.test.ts`.
3. Escape and "Stay here" keep the student on the live route with focus on the Stopped heading; "Open the lecture's page" replaces the history entry; Back goes to the course page; one status announcement on arrival; three live regions.
4. The flag is a user-scoped, device-local preference (`tether.lecture.u.<id>.stopMove.v1`), kept across sign-out, reversible with the "Ask before opening the lecture's page after Stop" switch in a new "Lectures" section of Preferences.
5. Notes state survives the move through the server (GET only, never a POST); 18 acceptance criteria across unit, harness and real-stack tests, including axe on the dialog in ten looks at 390 and 1280.

## Lead decisions (2026-11-01)
1. **Scope of the move:** accepted as specified. Only a student-pressed, confirmed Stop opens the dialog and moves the page. System stops (auto-stop, hard cap, abandoned, Failed), queued offline stops and already-stopped reopens never navigate on their own; the Stopped note on the live route gets the plain link "Open this lecture's page". PO-19 says "when a lecture is stopped"; an unprompted navigation on a system stop would surprise the student. To be confirmed with the PO.
2. **Marker:** `sessionStorage` one-shot marker accepted (router state does not survive the replace + reload cleanly).
3. **"Stay here" button** (not in the PO's wording; added by the design) accepted: it keeps the move optional and makes Escape safe. The link on the Stopped note is its fallback.
4. The hub Join rate-limit finding stays out of scope.
