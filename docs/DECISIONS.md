# Decisions

Status: **Proposed** = Lead Architect decision within existing authority, reversible. **Needs PO** = requires Product Owner approval; nothing depending on it is enabled.

## Architect decisions (cross-document conflicts resolved)

| ID | Decision | Rationale / source |
|---|---|---|
| ADR-001 | Modular monolith: ASP.NET Core under `src/api`, six modules, one Postgres DB (schema per module). | backend-foundation.md |
| ADR-002 | Cross-user or unknown resource ids return **404**, never 403. | backend-foundation, schema-m1-m2, QA question |
| ADR-003 | Only **final** transcript segments are persisted. Partials live on the wire/in memory. | schema-m1-m2 |
| ADR-004 | Audio goes client -> **our server** (binary WebSocket, ticket handshake) -> STT provider. Provider keys never reach the browser. | speech-pipeline, backend-foundation |
| ADR-005 | Transcript/summary events go downstream over SignalR `/hubs/lecture`, protocol `lecture.v1`; audio socket is upstream only. | speech-pipeline, backend-foundation |
| ADR-006 | Cookie session (HttpOnly, same-site, CSRF + Origin checks); web and API share one origin (dev via proxy). SignalR authenticates with the same cookie. | backend-foundation, SECURITY.md open item |
| ADR-007 | Client reconnect ring buffer: **60 s** (product requirement; speech doc proposed 30 s). UI must state only what is truly buffered. | PRD D7 vs speech-pipeline |
| ADR-008 | Consent = per-course recording-rules attestation (once, before first session) **plus** explicit per-session start click recorded as an append-only consent event. | UX + SECURITY SEC-006..009 |
| ADR-009 | Summaries: Haiku rolling updates, Sonnet for catch-up. Opus off the live path. Model IDs/pricing to be verified with the claude-api reference before any budget is set. Mock `IAiClient` is the default. | live-lecture-ai-design |
| ADR-010 | Postgres full-text search for retrieval; no vector store in M1-M2. | ai design open question |
| ADR-011 | Q&A and clarification chips are P1 (after the slice); Quiet is the default engagement mode. | PRD |
| ADR-012 | Google Doc export is built against a mock Docs provider until a Google OAuth client exists. | PRD vs OAuth dependency |
| ADR-013 | Postgres row-level security deferred until security-engineer reviews it; EF filters + interceptor + composite FKs are mandatory. | schema-m1-m2 |

## Needs Product Owner approval (blocks real use, not mock-backed build)

| ID | Item | Blocks |
|---|---|---|
| PO-1 | ~~Install .NET SDK~~ RESOLVED: .NET 10 via Docker SDK image (`docker-compose.dev.yml`). | — |
| PO-2 | Recording-rules policy and legal wording (D1); third-party voice obligations. | Any real recording |
| PO-3 | STT vendor and spend cap (D3). Proposal: prototype Deepgram, Azure Speech as compliance alternative. Vendor facts in the speech doc are unverified. | Real transcription |
| PO-4 | Claude API key, per-session/user/day budgets, behavior at cap (D5). | Real Claude calls |
| PO-5 | Retention defaults (proposal: 180-day transcripts, 30-day deletion SLA), audio retention (proposal: none), ledger handling on deletion. | Production |
| PO-6 | Google OAuth client creation; identity provider; token key management. | Real sign-in, Docs |
| PO-7 | Minimum age / under-18 handling (D8); hosting region, pilot scope (D10). | Production |
| PO-8 | Latency targets (transcript <2 s, Quick recap <5 s p95): proposals only. | Perf gates |

## Added after Milestone 1 reviews (2026-10-27)

| ID | Decision | Source |
|---|---|---|
| ADR-014 | `courses.user_id` gets a DB-level FK to `identity.users` with `ON DELETE CASCADE`, added by a new Courses migration using raw SQL. EF gets no navigation, so no Courses->Identity assembly reference. Allow-listed in backend-foundation.md and guarded by a `pg_constraint` test. | docs/data/m1-review.md |
| ADR-015 | The Lectures module owns `consent_records` (consent exists to gate a recording session). | docs/data/m1-review.md |
| ADR-016 | Server-side session revocation via an `identity.sessions` table (session-id claim, one PK lookup per request); no user-wide epoch. Must land before any non-dev sign-in. | docs/security/m1-review.md F-01 |
| ADR-017 | Hard M2 gates: Origin/CSRF check must cover WebSocket/SignalR upgrades (F-04); log-canary test and no `LogError(ex)` with row values once transcripts exist (F-08). | docs/security/m1-review.md |

Deferred: row-level security (ADR-013), revisited at M003 start with security-engineer.

## Added at M2 contract draft (2026-10-27)

| ID | Decision | Source |
|---|---|---|
| ADR-018 | M2 wire contract is `docs/API_CONTRACTS.md` (v1, frozen for M2 mock-backed work 2026-10-27): audio is server-relayed over a ticketed WebSocket, transcript events go only over SignalR `lecture.v1`, `seq` is gap-free for persisted events only (partials carry `seq: null`), field name `revision`, event `catchup.ready`. Supersedes conflicting parts of backend-foundation.md and speech-pipeline.md. PO confirmed server-relay (option A) for M2 on 2026-10-27; direct-to-vendor may be revisited as a second adapter after G1/G2. | docs/API_CONTRACTS.md |
| ADR-019 | Lectures data cleanup uses the ADR-014 pattern: DB-level FKs with `ON DELETE CASCADE` from `lectures.lecture_sessions.user_id` to `identity.users` and from `course_id` to the courses table, added by a Lectures migration in raw SQL; the cross-schema FK allow-list and its `pg_constraint` test are extended to exactly these entries. This closes the user-deletion and course-purge orphan gaps found in B1. Live sockets/hub connections must still be closed by B2 on delete. | B1 report 2026-10-27 |

## Added after the M2 B2 security review (2026-10-28, docs/security/m2-b2-review.md)

| ID | Decision | Source |
|---|---|---|
| ADR-019b | **Pause and Stop are never rate limited, capped or refused** (Lead Architect). Consent must stay reversible, so neither the 12-per-minute cap, the 200-per-session cap, the 3 h hard cap nor any per-session limit may block a student from pausing or stopping. Only **Resume** (and session start, which has its own one-live-session rule) is subject to the transition cap, and a refused Resume returns 429 `rate_limited` with the session left unchanged. System transitions (auto-pause, auto-stop, hard cap, course archive or purge) bypass the cap too. | B2-01 |
| ADR-020 | Two cross-schema FKs added by B2, and the sequencer port. `transcript.transcript_events` and `transcript.audio_progress` reference `lectures.lecture_sessions (id, user_id)` with composite FKs `fk_transcript_events_session_user` and `fk_audio_progress_session_user`, both `ON DELETE CASCADE`, so deleting a session, a course (ADR-019) or a user removes their transcript rows. The cross-schema FK allow-list is now five entries (ADR-014's one, ADR-019's two, these two) and the `pg_constraint` test pins exactly those. `ISessionSequencer.AllocateAsync(DbTransaction, ...)` lets Transcript take the next per-session `seq` inside its own transaction from the Lectures counter row, which keeps `seq` gap-free and makes a late write after pause, stop or delete roll back. | B2 report, B2-14 |
| ADR-020b | Course lifecycle port. `Tether.Courses.Contracts.ICourseLifecycleHandler` is called by Courses before it archives or purges a course. Lectures implements it: archive and purge stop every live session of the course (consent Stop event, audit, `session.state` with reason `course_archived` or `course_deleted`, sockets closed by push), and purge also audits `session.delete` for each session the cascade removes. User deletion has no endpoint yet and must use the same path when it is built. Login-session revocation is pushed the same way through `IAuthSessionRevocationObserver` (SharedKernel, implemented by Transcript). | B2-03, B2-06, B2-07 |
| ADR-020c | `demoSource` is stored on the session when it is created (`lectures.lecture_sessions.demo_source`, migration M007, existing rows backfilled to true because the replay provider was the only source) and is never recomputed from the current provider. Replay fault injection (`Speech:Replay:Faults:*`) and the scripted transcript (`Speech:Replay:Script`) make the process refuse to start outside Development, Test or Testing. Any `Speech:Provider` other than `replay` already refuses to start in every environment, because no real provider is implemented or authorized (G1/G2 open). | B2-08, B2-09 |
| ADR-020d | **Single instance only** for the realtime stack until a backplane and shared state exist. See docs/architecture/m2-b2-realtime.md, "Single-instance limits". | B2 review, multi-instance section |

## Product Owner decisions on Google Calendar / Google suite (2026-10-28)

| ID | Decision | Status |
|---|---|---|
| PO-9 | Tether uses the Google suite (Calendar now; Docs/Drive for notes and study guides). | PO decided; design in progress |
| PO-9a | If a student deletes a Tether-created event in Google, Tether leaves it alone (never recreates it) and may tell the student. | PO decided |
| PO-9b | Reminder defaults are delegated to `neurodivergent-ux-specialist` (audience: neurodivergent students). Output: `docs/ux/calendar-reminders.md`. Not human-validated (G4). | Delegated, pending |
| PO-9c | Token key management is delegated to `security-engineer`. Output: `docs/security/google-tokens-and-data-residency.md`. | Delegated, pending |
| PO-9d | One Google account per user, and it must be the same account the student signs in with (Google sign-in is the identity). Students get access to all pertinent content Tether creates, in their own Google account. Implication: the dev-login identity gets replaced by Google OIDC sign-in (PO-6). | PO decided |
| PO-9e | All content Tether creates should live in one or more Google apps so it survives a Tether outage. This amends the Postgres-centred model (invariant 5 and deletion/export controls) and needs an ADR plus CLAUDE.md wording once security and legal input is in. | PO intent; amendment pending |
| PO-9f | Planning assumes legal clearance exists so the PO can use the app end to end. This is a planning assumption only: gates in `RELEASE_GATES.md` stay as recorded until evidence exists. | Planning assumption, no gate change |
| PO-9h | The app only needs to see events it creates (no conflict detection against other events). Supports the `calendar.app.created` scope proposal. | PO decided |
| PO-9i | The student chooses where their schedule lives: Google Calendar or Tether's own calendar. Tether calendar: per-course view plus one combined view. Google Calendar: used as ordinary Google Calendar Confirmed: no separate per-course Google calendars; one calendar, course shown in event titles (e.g. "BIO 201 · Midterm exam"). | PO decided |
| PO-9j | Google account belongs to the student. A student without one must create one for now. | PO decided |
| PO-9k | Designated dev/test Google account: [dev Google account, address kept out of the public repo] (PO's). No credentials are shared or stored. Using it for real calls still needs a Google Cloud project and OAuth client created by the PO, the control list in `docs/security/google-tokens-and-data-residency.md`, and PO go-ahead for the first real call. | PO designated; no external call made |
| PO-9g | Still open (reduced): reminders on/off by default (calendar-reminders.md), disconnect behaviour (events presumed kept), ICS interim, Google Cloud project/OAuth client creation, and security doc questions Q1-Q7 (storage model, transcript opt-in, university accounts, pilot key custody, delete defaults, closed accounts). | Open |
| PO-9l | Reminders are ON by default (visible pre-selected Gentle preset), per `docs/ux/calendar-reminders.md`. Not human-validated (G4). | PO decided |
| PO-9m | Storage model accepted: Postgres is the working record, Google is the student's durable copy. | PO decided |
| PO-9n | If a student's Google account is closed or suspended, they can no longer use Tether, and Tether must no longer have access to that account or its data (tokens deleted; retention/purge of the student's Tether-side data to be specified and confirmed with the PO). | PO decided; purge details pending |
| PO-9o | Detached calendar entries (syllabus deleted, or Google disconnected with "keep events"): option C. Delete all content (titles, dates, course labels) immediately and keep only a content-free record per Google event (event id plus generation counter), so a re-created event never reuses a deleted event's id and Tether can still remove its own events later. Record is deleted when the student disconnects Google or deletes their account, with an upper limit proposed at 400 days (matches audit retention; proposal, not approved). Supersedes the earlier option A choice (2026-10-29). Contract revision pending. | PO decided (2026-10-29) |

## Delegated picks (2026-10-29): PO said "your pick" on the open calendar/security items
These are Lead Architect choices under delegation. Reversible. They approve no spending, external permission or gate.

| ID | Pick | Notes |
|---|---|---|
| PO-10a | Events a student edits in Google are preserved ("edited in Google"); Tether never overwrites or removes them. | Extends PO-9a (deleted events left alone). |
| PO-10b | No added type word in event titles: `<course> · <item title>`. | Matches the PO example. |
| PO-10c | `syllabus.v1` stays frozen. No `quiz` or `class_meeting` kind now; quizzes use the assignment reminder timing until a reviewed additive change. | Revisit with a security delta. |
| PO-10d | Switching destination (Google vs Tether calendar) moves nothing automatically. New approved items go to the new destination; moving old entries is a separate, confirmed run. | |
| PO-10e | A student may re-add a deleted event only by deliberate action in a preview. Never automatic. | |
| PO-10f | Reminder time is a whole hour, 08:00 to 20:00. | |
| PO-10g | The mock calendar table and frozen `calendar-sync` routes stay mock-only and are deprecated; remove once `calendar.v1` is implemented. | |
| PO-10h | Retention: 20 sync runs per syllabus, audit 400 days, content-free event record at most 400 days (all proposals, configurable). | |
| PO-10i | Reminder pause is manual resume in v1; no auto-resume job. | |
| PO-10j | University-managed (Workspace) Google accounts are allowed. Show a plain warning at connect and at transcript opt-in; handle admin-blocked gracefully. | Legal to review (G1). |
| PO-10k | Pilot key custody: KEK in the host secret store (dev: ignored local config), KMS later with PO approval of vendor and cost. | Security doc Q5. |
| PO-10l | Transcript upload to Google is a per-session student opt-in. Knowledge estimates are not mirrored to Google by default. | Security doc Q2, Q3. |
| PO-10m | Deleting in Tether keeps the Google copies by default and offers an explicit "also remove from Google" choice while the grant is valid. Dialogs must say so. | Security doc Q6. |
| PO-10n | Closed or suspended Google account: sign-in fails, tokens are deleted, existing Tether sessions end at their normal absolute lifetime. Tether-side data follows the retention policy, no password fallback. Purge timing needs PO/legal. | Refines PO-9n. |
| PO-10o | Students get a "let Tether manage this again" action only for entries whose content-free record still exists, via the reconnect flow with preview. | |
| PO-10p | Custom CSRF header name `X-Tether-CSRF` (finding B1-09). | |
| PO-10q | Google quota: per-user daily budget plus a global soft limit well below Google's charged threshold; exact numbers for devops to set and PO to approve. | Review CV-06. |

## Language rule and feature rename (2026-10-29)

| ID | Decision | Status |
|---|---|---|
| PO-11 | Sensitive-language rule: wording around attention and recall must not frame the student as failing, lacking or at fault. Applies to all UI copy, AI prompts and outputs, notifications, docs and feature names. Guide: `docs/ux/language-guide.md`. | PO decided |
| PO-11a | The one-click catch-up feature, previously named "I Lost Track", is now **Quick recap**. Renamed everywhere: UI, AI prompts, docs, CLAUDE.md, agent definitions, code, REST route (`/lost-track` -> `/quick-recap`), wire fields (`lostTrack*` -> `quickRecap*`), persisted workflow value (`LostTrack` -> `QuickRecap`, migration) and frozen `lecture.v1` schema names (old -> new recorded in `src/shared/contracts/RENAMES.md`). No alias kept: nothing was deployed and there was no real data. The neutral `catchup` resource names are unchanged. | PO decided; implementation in progress |
| PO-12 | Security severity policy: CRITICAL = tell the PO immediately; HIGH = resolve before pushing to git; MEDIUM = resolve before production; LOW = note in the register. See `docs/security/severity-policy.md`. | PO decided (2026-10-29) |
| PO-12a | GR-1 (transcript or study guide copied into an institution-managed Google account) stays **HIGH / OPEN** until qualified counsel has reviewed it. No downgrade. A push to git stays blocked while it is open. Ruling made 2026-10-29 (option 1). | PO decided |
| PO-13 | The severity policy (CRITICAL tell the PO immediately; HIGH blocks push; MEDIUM blocks production; LOW noted) also applies to legal, privacy and compliance concerns. Register: `docs/legal/legal-register.md` (ratings provisional until counsel confirms). A legal item closes only with written evidence from an accountable human. Same commit and push protections as security: the Lead checks both registers before any push. | PO decided (2026-10-29) |
| PO-14 | Scope for now: all Google accounts used with Tether are assumed to be **personally owned**. School-managed (Workspace for Education or other organisation) accounts are out of scope. To make that true rather than assumed, the app must detect an organisation account at sign-in (the ID token `hd` claim marks a hosted domain; whether it is present with only the `openid` scopes is UNVERIFIED, test T-hd on the real test account) and refuse to connect Google data with a plain explanation. This is a design requirement for the sign-in and connect code, not yet built. The rating of GR-1 / L-01 is unchanged until the PO explicitly downgrades it in writing. | PO scope decision (2026-10-29); downgrade not yet requested |
| PO-14a | **Written downgrade by the Product Owner (2026-10-29):** GR-1 (security register) and L-01 (legal register) are downgraded from HIGH to MEDIUM. Reason: school-managed Google accounts are out of scope (PO-14), so the risk of a transcript or study guide becoming visible to an institution does not arise for supported accounts. It remains a MEDIUM and blocks production or any real Google connection until the app detects and refuses organisation accounts (`hd` claim; presence UNVERIFIED) with tests, and counsel has confirmed the personal-account position. Supersedes the HIGH ruling in PO-12a. | PO decided |

## Scope and legal answers (2026-10-30)

| ID | Decision | Status |
|---|---|---|
| PO-15 | Tether is **not a commercial application**. It is built for the Product Owner's son. Legal questions are kept in `docs/legal/legal-register.md` so they can be revisited if the scope ever changes. Legal reports to the PO use CRITICAL / HIGH / MEDIUM / LOW groups. Items the PO said to ignore are marked DEFERRED, not closed, and still block production. | PO decided |
| PO-15a | For now assume the Google account is personal. Supersedes PO-10j (Workspace accounts allowed); PO-14 stands. | PO decided |
| PO-15b | Per-session transcript opt-in plus warning is enough for now; no separate notice. Copying third-party voices into the student's Google account needs no extra notice for now. | PO decided; counsel not consulted |
| PO-15c | If the student deletes data in Tether, or deletes their Tether account, nothing already copied to their Google account is deleted by Tether. (Refines PO-10m: the optional "also remove from Google" choice applies to a deliberate item delete, not to account deletion.) | PO decided |
| PO-15d | Transcript speakers are treated the same. The backend AI is expected to recognise when a student is asking a question rather than the instructor speaking. Requirement for `ai-agent-engineer`; no diarization-based identity handling. | PO decided; not built |
| PO-15e | Risk ranking for uploads (syllabus lower than textbook lower than exam) is correct; planned academic-integrity safeguards are adequate. | PO decided |
| PO-15f | Data will be stored in Google and in AWS. | Fact recorded |
| PO-15g | Review now: Anthropic commercial terms and DPA (C6, blocks first real Claude call), speech-to-text vendor terms, document-parsing licences (C9) when a library is proposed. | Assigned |
| PO-16 | Scope and consent for now: Tether will probably be used only by the Product Owner, partly as a way to learn about AI tools and see which approaches work best. Recording consent is a **pop-up where the student attests that the teacher gave consent**; nothing more for now (supersedes the consent-wording deferral for the pop-up itself; fuller wording stays DEFERRED). Agents still work to their fullest ability and legal still reports every issue it finds, but CRITICAL and HIGH ratings are calibrated to this personal, non-commercial scope. Ratings are re-rated in the register with a stated reason and stay provisional. Gate G1 is **not** cleared by this: it is a PO risk acceptance for personal use, recorded in `docs/legal/legal-register.md` L-02. | PO decided (2026-10-30) |


## UI/UX refactor round 2 (2026-10-31)

| ID | Decision | Status |
|---|---|---|
| PO-17 | UX refactor round 2 (`docs/ux/redesign-round2.md`, written by neurodivergent-ux-specialist on the fable model): header tabs ("Today / Courses") replace the left rail. | PO decided |
| PO-17a | Serif type is the voice of synthesis (summary lines, "Most recent", headings); transcript and interactive text stay sans. Reversible by a token. | PO decided |
| PO-17b | Recording-consent screens and Demo-source presentation are unchanged for now (gate G1); the spec's two consent recommendations are DEFERRED, not closed, and stay for the PO and counsel. | PO decided |
| PO-17c | `neurodivergent-ux-specialist` and `frontend-engineer` run on the `fable` model. | PO decided |

## Decisions on the redesign open items (2026-10-31)

| ID | Decision | Status |
|---|---|---|
| PO-18 | Push the redesign slices 0-6 to origin/main. | Done (4cacf5d..7dd222e) |
| PO-18a | Non-Paper themes give the consent screen no page decoration (no Gamer grid, no Glass gradient) and an opaque header. Consent card, wording and pop-up unchanged. | PO accepted |
| PO-18b | Syllabus review: implement a reserved-space rail so "one fill always in view" works without a sticky bar over targets (SC 2.5.8). Design by `neurodivergent-ux-specialist` (fable) first. | PO decided; not built |
| PO-18c | Follow the `fable` design review's recommendation for the Demo-source notice on lecture screens (`docs/ux/redesign-round2-review.md` section 6; also redesign-round2.md): render it as a one-line Note with the body behind "Details", and the Demo tag leaves the bar on desktop as it already does on phones. Wording is unchanged word for word; the recording status stays always visible. Supersedes the PO-17b deferral for this item only. Counsel not consulted; gate G1 is not cleared by this (PO risk acceptance for the personal-use scope, PO-16). | PO decided; not built |
| PO-18d | Final summary and notes after Stop: **option B**. A deliberate student press ("Make notes") after Stop is the only thing that sends the already-recorded transcript to the AI once a lecture has stopped; nothing is sent automatically on Stop (B3-06 stands). One press covers both the final summary and the notes. Built on the mock provider first; wording stays a draft; consent/disclosure wording is G1. | PO decided; not built |
| PO-18e | Acknowledged: NAS-03 moved the notes permission window from press time to job start plus a queue-age limit. With NAS-R02 fixed, the longest time from a press to a finished job is about 44 minutes at the defaults. | PO acknowledged |

## Redirect after Stop (2026-11-01)

| ID | Decision | Status |
|---|---|---|
| PO-19 | When a lecture is stopped, the browser moves from the live route (`/courses/{id}/lecture`) to that lecture's reopen route (`/lectures/{id}`), replacing the history entry, so a reload or a later return still shows the transcript, summary and notes. A dialog tells the student about the move first and has a "Don't show this again" checkbox; once checked the dialog no longer opens and the move happens directly. Wording is student-facing UX (language guide); it is not consent/recording/Demo wording (G1). | PO decided; not built |

## Review Desk responses (2026-11-01)

Source: the Product Owner's answers in the local Review Desk (23 items; the desk is kept outside the repo). Each row is a PO decision unless marked otherwise. Detail and the reasoning offered at the time are in the findings and legal registers.

| ID | Decision | Status |
|---|---|---|
| PO-20a | D-STOP: system-initiated stops (auto-stop, hard cap, abandoned, failed) do **not** move the page; they show a link. Only a student-pressed Stop redirects (PO-19). | PO decided |
| PO-20b | SBV-05: **build the unclaimed-dates list on the Review screen** (student sees each noticed date with its source line and can turn it into an item). Needs UX design, a frontend build and a security check. | PO decided; work started |
| PO-20c | L-19: scrub the dev Google address from current files; **no history rewrite**; accept it remains in git history. | PO decided; scrubbed in current files |
| PO-20d | SEC-PUBLIC: keep the repo public for now; revisit before any real deployment. | PO decided |
| PO-20e | L-04: stay on mock AI; no real Claude calls until the PO decides to proceed. | PO decided |
| PO-20f | L-05: PO is most familiar with **AWS Bedrock** and wants to plan on it. Bedrock hosts language models, not streaming speech-to-text (AWS's speech service is Amazon Transcribe). Needs clarification; a vendor verification of both is running. This is planning only; G2 stays UNVERIFIED and nothing real is called. | **Replaced for speech-to-text** by the PO's L-05 answer (2026-11-01 20:03Z: "Replace PO-20f and touch PO-15f and L-13. Azure AI speech"): Azure AI Speech (US) is the preferred first speech candidate (PO-20z). Whether any AWS use remains (Claude on Bedrock, hosting) is NOT decided; the PO was asked on the desk. PO-15f ("Data will be stored in Google and in AWS") and L-13 (region) were named to be touched, but no change has been specified yet, so both are unchanged. |
| PO-20g | G1-NOTES: keep the draft "Make notes" disclosure for mock use only; counsel review before any real AI. | PO decided |
| PO-20h | L-02: personal, mock-only until the PO has the instructor and campus recording rule in writing. No real recording before then. | PO confirmed |
| PO-20i | L-01: add a warning when the connected Google account looks institutional (does not block export). | Stands until PO-20w is confirmed |
| PO-20j | L-13: choose a **US region** for now and record it; specific AWS region to be chosen with the hosting decision. | PO decided |
| PO-20k | L-09: no age gate now; add one if other users appear. Scope boundary: the only user is the PO's son (an adult). | PO decided |
| PO-20l | L-10 and D13: synthetic or own documents only until a policy exists. | PO decided |
| PO-20m | SB-03: require an explicit confirm step that names what will be written, built before any real Google Calendar work. | PO decided; prerequisite recorded |
| PO-20n | SBR-08: fix calendar removal on course delete when the real calendar work starts; recorded as a prerequisite. | PO decided |
| PO-20o | SBR-01: replace the container-scan action with a checksum-pinned binary later; low urgency. | PO decided |
| PO-20p | SB-01: repeat the database log hardening as a deployment checklist item with the hosting decision. | PO decided |
| PO-20q | SEC-BRANCH: enable branch protection on `main` after the first fully green CI run, requiring the aggregate `ci result (all required jobs)` check, with admin bypass. The first green run exists (41336514945). | PO decided; **not yet applied** (the change to repository settings was blocked by the permission guard and needs the PO's go-ahead in the session) |
| PO-20r | D4: discard the original uploaded file after reading (current behaviour). | PO decided |
| PO-20s | D5: keep the draft upload notice and attestation for personal use; revisit before any other user. | PO decided |
| PO-20t | G2-CAP: leave the per-session AI cap placeholder until a priced pilot. | PO decided |
| PO-20u | L-03, L-06 to L-08, L-14 to L-16, L-20, L-21: the deferral stands (personal tool only); revisit if anyone else gets access. | PO confirmed |
| PO-20v | L-17 and L-18: do the licence inventory and the third-party-names check before any release. | PO decided |
| PO-20w | L-01 / GR-1: **refuse** institution-managed Google accounts for export (replaces the warn-only PO-20i). Matches PO-14a; the MEDIUM rating stays. The `hd` claim check is unverified and counsel has not confirmed the personal-account position. Not built; any warning task becomes a refusal task, with tests, before the real Google connection. | PO answered; **confirmation open**: the PO has been asked (a) whether this also replaces PO-10j (Workspace accounts allowed with a warning; note PO-15a already superseded PO-10j on 2026-10-30 by assuming a personal account, so the only open point here is PO-20i's warn-only wording) and (b) whether refusal is at export only or at connect. Feasibility is unproven: the `hd` claim with openid-only scopes is UNVERIFIED (T-hd, CV-22), so refusal is best-effort. Until confirmed, PO-20i and PO-10j stand and nothing is built. |
| PO-20x | UNCLAIMED-UX: accept all five defaults for the unclaimed-dates list (device-local Hide, prefilled title, type Other, collapsed by default, no count elsewhere). Matches the build in 400f1cc. **Amendment status (2026-11-01): a desk agent reported that the PO also asked for a recommended date offered for acceptance. That text is NOT in the stored desk answer (data/state.json holds only "Accept all five defaults...", submitted 19:59:27), and a later desk message said the idea was dropped. Unverified; the PO must say which.** This conflicts with the spec (the Date field starts empty and the client never parses dates, docs/ux/unclaimed-dates-list.md). A follow-up question is open on the desk (suggest only when the line has exactly one date; the student must confirm the year). **Not built until the PO answers.** | PO decided (five defaults); date suggestion unconfirmed, not built |
| PO-20y | AUTHOR-EMAIL: use GitHub's no-reply address for new commits in this repo. Applied as this repository's local git config on 2026-11-01. Old commits keep the personal address; no history rewrite. | PO decided; applied |
| PO-20z | L-05 follow-up: the PO's second answer prefers Azure AI Speech (US) as the first speech candidate (the AWS Bedrock-for-Claude question is separate and still open). Review of this answer is in progress on the desk. Nothing real is called; G2 stays UNVERIFIED. **Consistency note:** Azure is preferred for terms risk only; this differs from PO-3 (Deepgram first) and sits beside PO-20f (AWS Bedrock; Amazon Transcribe not yet compared). PO-3 and PO-20f are **not changed** until the PO answers the open desk question (is Azure first, or should Transcribe be compared first). Azure's no-training terms, DPA and price remain UNVERIFIED. | PO direction (Azure AI Speech US first for speech, replacing PO-20f for speech); open: AWS for Claude/hosting, and what to change in PO-15f and L-13 |
