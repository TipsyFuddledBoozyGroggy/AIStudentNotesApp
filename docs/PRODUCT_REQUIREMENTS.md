# Tether - Product Requirements (v0.1, DRAFT)

Status: proposal for Product Owner review. Nothing here is implemented or verified. Date: 2026-10-27.
Owner: Product Manager. Reviewers needed: Lead Architect, security-engineer, adhd-ux-specialist, learning-science, qa-engineer.

## 1. Personas
- **Student (primary):** college student, often ADHD or attention-challenged; attends lectures, loses the thread, needs low-friction recovery and low-pressure structure.
- **Authorized supporter (optional, later):** tutor/coach/parent the student explicitly invites with scoped, revocable access. Not in Milestones 1-2.
- **Administrator:** operates the service: user support, cost/usage visibility, deletion requests. No access to lecture content by default.

## 2. Prioritized scope

| Tier | Capability | Milestone |
|---|---|---|
| P0 MVP | Auth foundation, course model, dashboard, local dev, CI | M1 |
| P0 MVP | Consented mic capture, streaming transcript, live TL;DR, I Lost Track (2/5/10), timestamped notes, save study guide, deliberate export to Google Doc | M2 |
| P1 | Clarification suggestions; lecture Q&A labeling lecture-stated vs general knowledge (grounded, cited) | M2.5 (after slice is stable) |
| P1 | Quiet/Balanced/Active modes (Quiet as the default behavior in M2: no unsolicited prompts) | M2 default, full modes M3 |
| P2 | Syllabus intelligence with confirm-before-calendar-write; Google Calendar | Phase 2 |
| P2 | Practice test import, adaptive quizzes | Phase 2 |
| P3 | Executive-function coach, concept evidence tracking, spaced review, knowledge map | Phase 3 |
| Later | Authorized supporter access, mobile native, multi-language | Unscheduled |

Core vs delight: core = consent, transcript, TL;DR, Lost Track, saved notes. Delight = clarification nudges, Q&A, Docs formatting polish, themes.

## 3. Cross-cutting requirements
- Per-user ownership enforced server-side on every resource (cross-user access returns 404/403, tested).
- WCAG 2.2 AA target: keyboard operable, visible focus, screen-reader live-region announcements for recording state, reduced motion, color not sole signal.
- ADHD principles: one primary action per screen, no streaks/guilt/shaming copy, no unrequested diagnosis, no attention surveillance, interruptions opt-in.
- Privacy: raw transcripts not logged by default; source transcript stored separately from derived summaries with timestamps; delete and export available.
- AI never receives OAuth tokens; outputs schema-validated.
- Never claim completion without observable verification; mocked providers labeled as mocked in UI-free docs and test reports.

## 4. Milestone 1 - Foundation

### M1-S1 Sign in / sign out
As a student I want to sign in so my data is private.
- Triggers: open app unauthenticated. States: signed-out, signing-in, signed-in, error.
- AC1: Unauthenticated request to any API resource returns 401; UI redirects to sign-in.
- AC2: Successful sign-in lands on dashboard; sign-out ends the session and protected routes are blocked afterward.
- AC3: Provider failure/cancel shows plain-language error with retry, no data loss.
- AC4: Sign-in is fully keyboard/screen-reader operable.
- AC5: Basic Google identity scope only (no Docs/Calendar scope requested at sign-in; incremental consent later).
- Data: store minimal profile (id, email, display name). Dependencies: backend, frontend, security-engineer review.

### M1-S2 Create and manage courses
- AC1: Student creates a course with name (required, 1-120 chars), optional code, term, instructor.
- AC2: Student can edit, archive, and delete a course; delete requires confirmation stating what is removed.
- AC3: User A cannot read, list, edit, or delete user B's course (automated test).
- AC4: Duplicate names allowed but warned; empty/whitespace names rejected with inline accessible error.
- AC5: Offline/failed save keeps form input and shows retry.
- Dependencies: database-architect (schema, migration), backend, frontend.

### M1-S3 Dashboard
- AC1: Shows course list and one primary action ("Start lecture" on a chosen course, or "Add course" if none).
- AC2: Empty state explains next step in one sentence; loading and error states exist.
- AC3: Works at 320px width and with keyboard only; no axe critical/serious violations.
- AC4: No streaks, counters of missed work, or guilt language.

### M1-S4 Local dev and CI
- AC1: Fresh clone to running web + API + Postgres using documented commands on Windows (verified by a person other than the author, or recorded run).
- AC2: No secrets committed; sample env file with placeholders; provider keys optional (fake providers by default).
- AC3: CI runs build, unit tests, lint, and accessibility check on every PR and fails on errors.
- AC4: CI includes authorization test for cross-user access.
- Dependencies: devops-engineer, qa-engineer.

## 5. Milestone 2 - Live lecture vertical slice (highest priority)

### M2-S1 Recording consent and start
As a student I must knowingly start capture so I never record unintentionally.
- Triggers: student presses "Start lecture" on a course. States: pre-start, awaiting-browser-permission, recording, paused, stopped, denied, error.
- AC1: Before first capture, a consent screen states what is captured, where audio/transcript goes (named STT provider), retention, and a reminder to confirm the university/instructor/local recording rules. Student must affirmatively check/accept; acceptance stored with timestamp and version.
- AC2: No audio is requested or sent before consent and the explicit Start press (test: no getUserMedia call, no network audio traffic).
- AC3: While capturing, a persistent visible and screen-reader-announced "Recording" indicator is shown; Pause and Stop are always reachable by keyboard.
- AC4: Pause stops audio transmission (verifiable); Resume is explicit. Stop ends the session and finalizes transcript.
- AC5: Browser permission denied: a plain explanation, how to re-enable, and an alternative (typed notes / paste text) with no dead end. No repeated nagging prompts.
- AC6: Only the authenticated owner can start/read a session.
- Data: audio not persisted by default (decision D2). Dependencies: speech-engineer, security-engineer, frontend, backend.

### M2-S2 Streaming transcript
- AC1: Partial segments display within target p95 latency (target <= 2 s from speech to text; to be validated, D6) and are replaced by final segments without scroll jumps or duplicate text.
- AC2: Each final segment stored with session-relative start/end timestamps and sequence number.
- AC3: Transcript view is a screen-reader-friendly region that does not announce every partial update (opt-in verbosity).
- AC4: Student can scroll back without being forced to the bottom; "Jump to live" control is available.
- AC5: Provider outage or unsupported audio shows status and continues local session state; no crash.
- Dependencies: STT provider choice (D3), versioned event contract from Lead Architect.

### M2-S3 Disconnected / recovery
- AC1: On network loss, a calm "Reconnecting" banner appears; the already-received transcript stays visible and usable.
- AC2: Client buffers audio for a bounded window (default proposed 60 s, D7) and resumes; on reconnect, missing segments are filled or a visible "gap from mm:ss to mm:ss" marker is shown. Never silently drop.
- AC3: Reload mid-session restores the session and transcript from server; student chooses to resume or stop.
- AC4: Duplicate events after reconnect do not duplicate transcript text (idempotent by sequence).
- AC5: If recovery fails, student can stop and save what exists.

### M2-S4 Live TL;DR
- AC1: TL;DR updates incrementally (proposed every ~60-90 s or on topic change, tunable) and shows "updated at mm:ss" and revision number.
- AC2: Updates do not steal focus, auto-scroll, or flash; screen reader receives polite, throttled announcement only if enabled.
- AC3: Every TL;DR bullet traces to transcript timestamp range (provenance).
- AC4: Student can pin/freeze the TL;DR to stop updates.
- AC5: If the model fails or times out, last good TL;DR remains with "could not update" note; transcript continues.
- AC6: Output is schema-validated; nothing asserted that is absent from transcript (eval fixtures by ai-agent-engineer).

### M2-S5 I Lost Track
As a distracted student I want one click to catch up.
- AC1: One primary button, always visible during recording, with choice of last 2, 5, or 10 minutes (default 5; remembered).
- AC2: Result appears in <= target 5 s p95 (D6), as: what was just being discussed (1-2 sentences), key points (<= 5 bullets), and "where we are now" line, each with timestamps.
- AC3: If session shorter than chosen window, uses available content and says so.
- AC4: Recovery does not pause or interrupt recording or the live transcript.
- AC5: Invoking it is not logged as a "failure" metric visible to the student; no judgmental copy.
- AC6: Works from keyboard (documented shortcut plus button) and screen reader.
- AC7: Offline/model failure: falls back to showing the raw transcript for that window plus retry.
- AC8: Gaps in the window are disclosed.

### M2-S6 Timestamped notes and study guide
- AC1: At stop, system generates notes organized by topic with source-timestamp links to transcript passages.
- AC2: Student can edit notes; edits are versioned apart from source transcript (source never mutated).
- AC3: Student explicitly saves a study guide to the course; it is listed under the course and survives reload.
- AC4: Generation failure keeps transcript and offers retry; nothing is lost.
- AC5: Student can delete session transcript and derived notes independently, with confirmation; export of notes as Markdown/text available (data export).

### M2-S7 Deliberate export to Google Doc
- AC1: Export is triggered only by an explicit button; shows destination account and what will be written, then confirms.
- AC2: Requests narrowest scope (create/own files only) at that moment, not at sign-in.
- AC3: Repeat export updates or creates per student choice; idempotent (no silent duplicate docs).
- AC4: Scope denied/revoked/token expired: clear message, notes remain in-app, retry path.
- AC5: Link to resulting Doc displayed; failure never reports success.
- AC6: AI component never sees tokens.

### M2-S8 Quiet default
- AC1: In M2 no unsolicited popups, sounds, or nudges occur; all assistance is student-initiated or passive-on-screen.

## 6. Later stories (outline only, P1+)
- Clarification suggestions: passive list of "possibly unclear" moments, never interrupting; student can dismiss; false-positive feedback.
- Lecture Q&A: answers labeled "From this lecture" (with timestamps) versus "General knowledge (not said in lecture)"; refuse to attribute to lecture when no support found.
- Syllabus intelligence: upload, extracted items shown in review table with confidence and source snippet; uncertain/unknown dates remain "needs date"; nothing written to Calendar until student approves each (or batch) item; idempotent sync keys; edit/undo.
- Practice tests/adaptive study: AI-estimated answers labeled as estimates versus answer-key; single misses are not mastery deficits (learning-science to define thresholds).
- Executive-function coach, knowledge map: opt-in, low-pressure, no diagnosis.
- Engagement modes: Quiet (nothing unsolicited), Balanced (occasional optional prompts), Active (frequent check-ins), per-course override and instant switch.

## 7. Edge cases
- Multiple tabs/devices starting the same session; second device is read-only or prompted.
- Very long lectures (3 h) and very short (<2 min); silence; background noise; multiple speakers; accented speech; non-English audio (out of scope, show "unsupported language/low confidence").
- Laptop sleep, tab backgrounding, mic unplugged, Bluetooth switch mid-session.
- Session abandoned without Stop: auto-close after inactivity, with notice, keep data.
- Course deleted during live session (block or confirm).
- Session token expiry mid-recording (refresh silently or preserve capture and prompt sign-in).
- Profanity/sensitive disclosures in lecture; other people's voices (consent of others, D1).
- Student dispute: AI summary is wrong; show source and allow edit.
- Provider cost cap hit mid-session: degrade to transcript-only with notice.

## 8. Success measures (to validate, not claimed)
- Activation: % of new users completing first consented session within 7 days.
- Lost Track: median time to usable recap <= 5 s; student-rated helpfulness (optional thumbs) >= 80% helpful.
- Reliability: sessions ending with complete transcript (no unmarked gaps) >= 95%.
- Accessibility: zero critical/serious axe issues; manual screen-reader pass on M2 flows.
- Trust: zero cross-user data access in tests; no recording before consent in tests.
- Guardrail: no engagement-maximizing metrics (streaks, time-in-app) as goals.

## 9. Explicit non-goals (Milestones 1-2)
- No syllabus parsing, Calendar writes, quizzes, coach, knowledge map, engagement mode picker.
- No supporter/parent access; no admin viewing of lecture content.
- No video capture, no speaker identification, no automatic recording on schedule.
- No audio retention by default; no sharing of notes with other students.
- No diagnosis or attention monitoring (camera, eye, or behavioral tracking).
- No native mobile apps (responsive web only); no offline-first full functionality (only bounded reconnect buffering).
- No claim of legal compliance; no real-student use until D1 is resolved.
- No LMS integration, no automatic Doc export.

## 10. Legal/privacy and learning-science assumptions for specialist review
- Recording rules vary by university and jurisdiction (one-party vs all-party consent); the app's reminder is not legal advice. security-engineer/counsel to review. Transcripts may be education records or contain third-party voices (FERPA/GDPR implications if institutional use).
- Third-party STT and Claude receive lecture content: data processing terms, no-training settings, and region need review.
- Students under 18 (dual enrollment) may exist: age gate decision.
- Assumption (learning-science to review): short recaps reduce re-entry cost without encouraging disengagement; auto-summaries may reduce active note-taking. Consider a prompt to add own notes.
- Assumption: 2/5/10 min windows are the right recovery granularity; validate with pilot.

## 11. Dependencies and sequencing
M1-S4 (dev/CI) and M1-S1 (auth) first, then S2/S3 in parallel. M2 requires: versioned transcript/summary event contract (Lead Architect), STT provider interface and choice (speech-engineer), consent record schema (database-architect/security-engineer), eval fixtures for TL;DR and Lost Track (ai-agent-engineer). Suggested M2 order: S1 -> S2 -> S3 -> S4 -> S5 -> S6 -> S7 (S8 is a constraint on all).

## 12. Ranked risks
1. Recording legality/consent misunderstanding (high impact). Mitigation: D1, consent flow, no real use until cleared.
2. STT latency/accuracy/cost on real lectures (high). Mitigation: provider spike with real classroom audio early.
3. Hallucinated or wrong summaries erode trust (high). Mitigation: grounding, timestamps, evals.
4. Cost overrun from continuous LLM calls (medium-high). Mitigation: update cadence, caps, budget alerts (D5).
5. Accessibility regressions in live-updating UI (medium). Mitigation: UX review, automated + manual checks.
6. Scope creep from later capabilities (medium). Mitigation: non-goals list.
7. Network instability in lecture halls (medium). Mitigation: M2-S3.

## 13. Open decisions for Product Owner (none are decided)
- D1: Policy for recording rules: required acknowledgement text, whether to block until student attests, who reviews legally, and whether pilot is limited to a known-permitted course.
- D2: Retain audio at all? Proposed: no; transcripts only, with a retention period (proposal: until student deletes).
- D3: Approve STT provider candidates and spend for a spike; budget ceiling per student-hour.
- D4: Auth method: Google sign-in only, or also email/password? Proposed: Google only for M1.
- D5: Monthly cost cap and behavior at cap (degrade to transcript-only proposed).
- D6: Approve latency targets (transcript <= 2 s, Lost Track <= 5 s p95).
- D7: Reconnect buffer duration (proposed 60 s) and gap-marker behavior.
- D8: Minimum age and handling of under-18 users.
- D9: Is Q&A (lecture-stated vs general) part of the first release or deferred to M2.5 (proposed deferred)?
- D10: Target hosting/region for data, and whether the pilot is single-institution.
- D11: Default Lost Track window (proposed 5 min) and TL;DR cadence.

## 14. Verification status
Nothing in this document has been tested or implemented. Acceptance criteria are proposals for qa-engineer to convert into automated and manual tests; items marked "target" need measurement before being treated as commitments.
