# Finish backlog (PROPOSAL)

**Status: PROPOSAL by product-manager, 2026-11-06. Nothing here is approved, built or run by this document. Order, sizes and owners are the PM's judgement; the Lead may reorder.** Basis: PO-20cb ("finish this app for me to start testing features. The legal questions we can figure out later"). Legal and risk items are deferred and are not a reason to pause building. This clears no gate (0 of 5 passed) and does not authorise real providers, spend, recording others, deploying, showing the app to anyone else, or changing G1 wording.

Sources read: CLAUDE.md, docs/PROGRESS.md (through iteration 12), docs/DECISIONS.md (PO-20ca, PO-20cb), docs/RELEASE_GATES.md, docs/product/pilot-plan.md (header and revision notes), docs/product/backlog.md, docs/DEVELOPMENT.md. I did not run anything. Facts about code come from file listings and PROGRESS.md, not a fresh build. "Verified" below means PROGRESS.md says the Lead re-ran it; the figures are as of its last entry (2026-11-02) and are not re-checked today.

---

## 1. Built today versus the definition of done

MVP definition (CLAUDE.md): sign in; create course; start/stop lecture voluntarily; streaming transcription; incrementally updated TL;DR; Quick recap (2/5/10 min); notes with source timestamps; deliberate export to a Google Doc; denied-mic and offline states; per-user data; tests and real-provider smoke test.

| MVP item | State today | Evidence / caveat |
|---|---|---|
| Sign in | Built, dev-only email sign-in ("Google sign-in arrives later") | `SignInView.tsx`; Google OAuth not built (no Google module in `src/api/src/Modules`) |
| Create course | Built | Courses module, E2E per PROGRESS |
| Start/stop lecture, consent, pause | Built on mock/replay | Lectures module; consent wording is DRAFT (G1) |
| Streaming transcription | Built against a replay "Demo source" only; no real STT adapter | PROGRESS iter 4-5, 12 |
| Incremental TL;DR | Built on mock AI (`source: mock`) | AI module |
| Quick recap 2/5/10 | Built on mock AI | Renamed from "I Lost Track" |
| Notes with timestamps | Built: "Make notes" after Stop (PO-18d) on mock AI | NotesPanel; no proof it can be saved or copied out (not found) |
| Export to Google Doc | NOT built. No Google/Docs/Drive module | Needs PO go for real Google; a local export is possible without it |
| Denied-mic / offline states | Built, covered by E2E | Not tested on real devices (G3) |
| Per-user data, delete/export | Built for courses, syllabus; lecture/transcript delete status unclear | PROGRESS iter 12; delete wording DRAFT |
| Automated tests | Yes; CI green on GitHub (sharded, ~22 min) | G5 still not passed |
| Real-provider smoke test | NOT done; no real STT or Claude adapter exists | Needs PO go |

Phase 2: syllabus paste/.txt upload, mock extraction, review/approve/skip/edit, "Demo calendar" sync/undo, delete, JSON export are built and verified on mock data. NOT built: PDF/DOCX intake wired (DOCX extractor stage A exists, unwired; PDF waits on PdfPig licence and sandbox desk answers), real Google Calendar (`calendar.v1` is a DRAFT contract), reminders, practice-test import, adaptive quizzes (design docs only).

Phase 3: nothing built (executive-function coach, concept evidence/knowledge map, spaced repetition, engagement modes). Quiet mode is in the invariants; Preferences panel and five themes x light/dark exist.

Honest gaps in what exists: Playwright was not re-run after every late change; no screen-reader or device pass; no human copy or neurodivergent validation (G3/G4); open security MEDIUMs block production only.

---

## 2. Shortest ordered path to a feature-complete app the PO can test locally (mock/synthetic data only)

"Feature-complete" here means every CLAUDE.md feature has a working, locally testable path, with real providers replaced by mocks behind the existing interfaces. All items are proposals; none needs a PO go because none touches real providers, terms, spend, recording others or G1 wording. Sizes: S under 1 day, M 1-3 days, L more.

| # | Item | What the PO can then do | Size | Owner | Depends on | Blocked-by |
|---|---|---|---|---|---|---|
| 1 | **One-command local start plus seeded demo data.** Script that starts Postgres + API + web (real stack, `dev:real`), checks health, and seeds a demo user, two courses, a finished demo lecture with notes, and a sample syllabus. Fix any DEVELOPMENT.md drift (it still says .NET "not installed"). | Start the app with one command and see content on first login instead of an empty app | S | devops-engineer (+ backend-engineer for the seed) | none | none |
| 2 | **Notes export that works without Google: Markdown/copy/download of notes with source timestamps.** Local file only, user-initiated, with citations kept. Doubles as the shape of the later Google Doc export. | Finish a demo lecture, press Export, get a file; check notes match transcript timestamps | S-M | frontend-engineer, backend-engineer if server-side | Notes after Stop (built) | none |
| 3 | **Export to Google Doc on a "Demo Doc" mock.** Same review-then-write pattern as the Demo calendar: preview, deliberate confirm, idempotent, no tokens near AI. Real Google stays off. | Complete the MVP "export to a Google Doc" journey end to end against a stand-in and see the confirm step and the error/retry states | M | google-integration (contract), backend-engineer, frontend-engineer | 2; contract for docs export (new, minimal, versioned) | Real Google needs PO go (section 3) |
| 4 | **Replay-lecture variety and a test-drive pack.** Several synthetic replay scripts (short, long, noisy, with a long pause, with a dropped network) selectable on the lecture screen, clearly labelled "Demo source". | Test TL;DR revisions, Quick recap 2/5/10, reconnection and degraded states without any microphone | M | speech-engineer, frontend-engineer | existing replay STT | none. Do not alter Demo-source or consent wording (G1) |
| 5 | **Calendar side of syllabus: reminders on the Demo calendar.** Map reviewed items to reminders, student chooses lead times, undo; uncertain dates stay uncertain. Build against the draft `calendar.v1` with a mock calendar only. | Approve syllabus items, set reminders, see them on a local "Demo calendar" view | M | google-integration (freeze mock-scope of contract), backend-engineer, frontend-engineer | syllabus flow (built); `calendar.v1` review | Real Calendar write needs PO go |
| 6 | **Wire DOCX syllabus upload (mock extractor).** Connect the stage A DOCX reader to the upload pipeline under the existing defences; hostile fixtures stay in CI. PDF excluded until the desk answers PdfPig licence and sandbox. | Upload a synthetic .docx syllabus and review it like pasted text | M | backend-engineer, security-engineer (review) | DocReader stage A (built) | DR-SANDBOX / DR-LICENCE only for PDF |
| 7 | **Practice-test import and quiz, keyed first, mock-graded.** Paste/upload a synthetic practice test with answer key; take it; see per-question feedback; misses create remediation prompts. Keep AI-estimated answers distinct from keyed answers; one miss is not a mastery deficit. | Take a quiz from an imported synthetic test and see feedback | L | learning-science (spec), backend-engineer, database-architect, frontend-engineer | design docs exist; practice-test scope "keyed first" is an open question, so this proposal assumes it | none to build; open question below |
| 8 | **Concept evidence and a simple knowledge view.** Evidence rows from quizzes and notes, shown as "seen / shaky / solid" with provenance, never a verdict from one miss. Optional, quiet-mode friendly. | See a concept list updating after a quiz | L | learning-science, database-architect, backend-engineer, frontend-engineer | 7 | none |
| 9 | **Spaced review queue.** Due-today list built from concept evidence, opt-in, low pressure, no streaks. | Open a short review session of due items | M | learning-science, backend-engineer, frontend-engineer | 8 | none |
| 10 | **Executive-function coach, mock-AI.** Weekly plan from syllabus deadlines plus study-time suggestions, student edits and accepts, no nagging, quiet mode honoured. | See a proposed week from the demo syllabus and adjust it | L | neurodivergent-ux-specialist (spec), ai-agent-engineer, frontend-engineer | 5, 8 | none |
| 11 | **Self-service data controls for lectures and account.** Delete a lecture and its transcript, export-all, delete-my-account, proven on a seeded account (PO-20bz screens-go). Content-free audit rows are not the deletion design until the PO resolves the PO-20ca conflict. | Delete a demo lecture or the whole demo account and verify nothing remains; export everything | M | backend-engineer, database-architect | seed from 1 | The retention/audit conflict (below) for the audit part only; the delete and export parts can proceed |
| 12 | **Mock-vs-real switch, visible and safe.** A single provider settings view showing each provider as mock/real, real disabled until configured, one-way "turn AI off" (pilot-plan P-SWITCH). | See at a glance that everything is running on mock and cannot call out | S-M | backend-engineer, frontend-engineer | none | Enabling real stays in section 3 |
| 13 | **Test pass of the whole product by the PO: a feedback log.** Simple in-repo checklist the PO ticks while testing (works / confusing / broken), with screenshots path. | Report issues in one place the agents can triage | S | product-manager | after 1-5 | none |
| 14 | **Hardening that affects daily use:** rerun full Playwright at production rate limits; keep CI green; fix flakes. | Fewer surprises while testing | M | qa-engineer | any | none |

Notes on order: 1 and 2 first because they unblock testing at once. 3 and 5 finish the MVP and Phase 2 stories on stand-ins. 4 improves what he can test of the core lecture loop. 7 to 10 are Phase 2/3 and can be cut or reordered by the PO; they are the largest items.

---

## 3. Needs a separate PO go (not part of the build list; not blocked by it)

None of these is started or authorised. Each needs its own written go on the Review Desk (D4).

| Item | Why it needs a go | Related |
|---|---|---|
| Real speech provider (Azure Speech) | spends money, needs account steps, quota; caps in pilot-plan: speech 10 min/session, 30/day, 120/month, fixture audio only | PO-20bo, G2 |
| Real Claude (Foundry/Marketplace) calls | spends money (AI cap $0.25/day, $5/month), terms acceptance, who prices the account is unverified | PO-20bk..bn, G2 |
| Accepting any vendor terms or Marketplace terms | PO accepts himself; no agent does | P-LAWYER |
| Real Google sign-in, Docs, Calendar | external system permissions, OAuth consent screen, school-managed accounts refused at connect (`hd` detection untested) | "separate go for the Google sign-in test" |
| Recording others / classroom audio | G1 BLOCKED, unchanged by PO-20cb | G1 |
| Using real lectures, real syllabi or any real person's data | synthetic only today | PO-20au |
| Showing the app to his son or anyone | own yes, plain-language text, D14 | pilot-plan |
| Deploying anywhere | no deployment; Stage 1-2 local only | D6, G5 |
| Changing G1 consent / Demo-source wording | approved text does not exist; draft only (D8 open) | PO-20ca |
| Deleting the Foundry resource, PdfPig licence, sandbox, Azure account steps | PO-owned desk items | PROGRESS "Waiting on the PO" |

Deferred legal items (do not block building): L-07, L-08, L-14, SEC-PUBLIC, O-TWO-FACTS, D-RETENTION follow-up, the lawyer step.

---

## 4. What the PO can test today (exact steps; proposal, not re-run by me)

Prerequisites: Docker Desktop and Node 22. Everything is mock/replay; no microphone or real provider is used.

Mock-only UI (fastest, no backend):
1. `cd C:\Dev\AIStudentNotesApp\src\web`, then `npm install` if needed, then `npm run dev`. Open http://localhost:3000. The page runs on the in-browser MSW mock (per package.json; I did not confirm the mock default or port).

Real stack (API + Postgres):
1. In the repo root: `cp .env.example .env` (Windows: `Copy-Item .env.example .env`), then `docker compose up -d postgres` and wait for `docker compose ps` to show healthy.
2. Start the API: `docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d api` (listens on 127.0.0.1:5080).
3. In `src/web`: `npm run dev:real`. Open the URL it prints (normally http://localhost:3000).
4. Sign in on `/signin` with any school-style email, for example `me@school.edu` (development sign-in; the dev login is rate-limited to 10 per minute).
5. Create a course (`/courses/new`).
6. Courses > the course > Lecture: start a session, accept the (DRAFT) consent step, and choose the Demo source. Watch the transcript fill, the TL;DR revise, and press Quick recap (2, 5, 10 minutes). Pause, resume, then Stop; use "Make notes" to see timestamped notes. Try denying or disabling the microphone permission and toggling offline in the browser dev tools to see the denied and disconnected states.
7. Syllabus: Courses > the course > Syllabus. Paste synthetic syllabus text or upload a `.txt`. Review items (approve, skip, undo, edit, needs-date), sync to the "Demo calendar", remove, undo, export JSON, and delete.
8. Preferences: switch themes (five looks, light and dark) and quiet mode.

Not testable today: Google Doc export, real transcription, real AI wording, reminders, PDF/DOCX upload, quizzes, knowledge map, spaced review, coach.

Gotchas from the repo: API tests collide with the running dev API (stop `aistudentnotesapp-api-1` first); the real-stack E2E needs the dev-only raised rate limits; the UI shows "Demo source" or "Demo calendar" labels on purpose.

---

## 5. Open questions for the PO (Lead posts them; recommendation and default if unanswered)

1. **Order of items 7-10 versus polish.** Recommend: build 1-6, 11, 12 first, then 7, 8, 9, 10 in order. Default: this order.
2. **Practice-test scope.** Options: keyed tests only (recommended), or also AI-generated quizzes. Default: keyed only, mock-graded.
3. **Audit rows versus "delete the row" (PO-20ca follow-up).** Does deleting a lecture or syllabus also drop consent and AI-usage rows (conflicts with PO-20aw), and is a 365-day cap wanted? Recommend: keep content-free consent and usage rows for now; default: build delete of content only, leave the audit question open as it already is.
4. **Local export format** (item 2): Markdown (recommended), plain text, or .docx. Default: Markdown.
5. **Final summary after Stop** (open since iteration 6): recommend no separate final summary since notes after Stop cover it. Default: none.

## 6. Ranked risks

1. Mock success gives false confidence about real STT/Claude quality, latency and cost; the first real-provider run could reveal rework. Mitigation: item 12 labelling; section 3 smoke test when the PO gives a go.
2. Phase 3 items (8-10) carry learning-science claims that nobody has validated (G4); keep them labelled experimental and non-diagnostic.
3. Scope growth: items 7-10 are large; the app can be "complete" for MVP testing after 1-6 and 12.
4. Google export built only against a stand-in may need rework when the real API and consent screen are used.
5. Building while the legal questions are deferred could embed assumptions (retention, delete behaviour); keep deletion behind one interface.
6. Testing load: full E2E is slow (~22 min CI) and rate-limit sensitive.
7. Dev docs drift (DEVELOPMENT.md says .NET SDK missing).

## 7. What I did not verify

I did not run any build, test or server, did not confirm the web mock default or port, did not confirm that lecture transcripts have a delete path, and did not check whether a local notes export already exists (the grep of NotesPanel found none). Read DECISIONS only to PO-20cb; `docs/product/phase2-syllabus-import.md` and other `docs/ux` and `docs/ai` specs were not opened.
