# Development Roadmap

**Updated 2026-11-27.** This page is the plan and where each part stands. The detailed history is in `PROGRESS.md`; decisions are in `DECISIONS.md`. Nothing here changes a release gate (`RELEASE_GATES.md` is the canonical register; 0 of 5 gates passed).

Status words: **Done** (built, tested, in CI), **Built, not verified** (built and tested locally, not yet proven where it runs), **Designed** (a design or ADR exists, no code), **Not started**.

Every provider is a built-in stand-in today: the "Demo source" replays a fixed script instead of real speech-to-text, AI output comes from a mock client, the syllabus extractor is a mock, and calendar entries and exported documents go to a "Demo calendar" and a "Demo Doc" inside Tether's own database. The API refuses to start with any other provider setting.

## Where each milestone stands

| Milestone | Status | What exists | Where |
|---|---|---|---|
| M1 Foundation | **Done** (2026-10-27) | Repo, local stack (docker compose Postgres, API in the .NET SDK image), Next.js web app, ASP.NET Core modular monolith, development login, courses, EF Core migrations, unit/integration/Playwright harnesses | `PROGRESS.md` iterations 1 to 3 |
| M2 Live lecture slice (the MVP) | **Done on demo providers** | Consent before capture, visible recording status with Pause/Resume/Stop, streaming transcript, rolling TL;DR, Quick recap of 2/5/10 minutes, notes after Stop with source timestamps, reopen a finished lecture, safe denied-microphone and lost-connection states, Markdown export and copy, export to a Demo Doc | `PROGRESS.md` iterations 4 to 12; `API_CONTRACTS.md` (`lecture.v1`), `API_CONTRACTS_DOCS_EXPORT.md` |
| Phase 2, slice 1: syllabus import | **Done on the mock extractor** | Paste a syllabus or upload a plain-text file; approve, skip or edit each proposed item; only approved items can be added to the Demo calendar; uncertain dates stay uncertain; "Dates we noticed" lists dates that did not become items; server-side checks against a lying extractor | `API_CONTRACTS_P2.md` (`syllabus.v1`), `ai/syllabus-extraction-design.md` |
| Document reader | **Stage A built, not wired** | A DOCX text extractor with hostile-input tests; not connected to uploads. PDF waits on the PO's licence and sandbox answers | `architecture/document-reader.md` |
| Finish backlog (PO-20cc) | Items 1 to 3 **Done** (2026-11-05); 4 to 14 **Not started** | One-command start with demo data, notes export, Demo Doc export. The rest was paused when the PO made the cloud proof of concept the priority (PO-20cf) | `product/finish-backlog.md` |
| Cloud proof of concept (PO-20cf) | **Done** (2026-11-26) | Azure Container Apps and PostgreSQL Flexible Server, deployed by a manual GitHub Actions workflow that the PO approves, behind Microsoft (Entra) sign-in, mock providers | `infra/README.md`; `PROGRESS.md` iteration 13 |
| Go-live: invite-only sign-in (PO-20cg) | See the go-live table below | | `product/go-live-stories.md`, `architecture/go-live-signin-adr.md` (ADR-022) |

## Go-live (PO-20cg), in order

| Story | Status | Notes |
|---|---|---|
| S1 Invite-only Google sign-in replaces the test login; API in Production | **Built, not verified** | Deployed in Run A on 2026-11-27 behind the Entra gate (run 46083406628). Waiting on the PO's own Google sign-in test. Open finding GLS-01 (sign-in start budget) waits on the Run A measurement |
| S4 Cut-over: Entra gate off, rollback | **Built, not verified** | The `invited`, `team` (rollback) and `closed` (emergency stop) access modes exist in the deploy workflow. Runs B (`invited`), R (timed rollback to `team`) and B' (`invited`) are next |
| S2 Export all my data | **Designed** | `architecture/account-export-delete.md` (ADR-021, proposed). Must exist before anyone other than the PO uses Tether (PO-20ax) |
| S3 Delete my account | **Designed** | Same ADR and the same PO-20ax condition |
| S5 Real speech and Claude replace the demo | **Not started** | Azure AI Speech and Claude on Azure (PO-20z, PO-20aa). No real call until the PO accepts the vendor terms in person (PO-20bl). Caps in the story: speech 120 minutes a month, AI USD 5 a month, across all users |

## Next, in order
1. The PO tests Google sign-in on the hosted site (still behind Entra).
2. Read the forwarded-header measurement and rule on GLS-01.
3. Runs B, R and B' (each approved by the PO).
4. Account-wide export and delete (S2, S3) before inviting anyone else.
5. Real providers (S5) after the PO accepts the terms.
6. Back to the finish backlog (items 4 to 14), in the order the PO approved.

## Later (from the product plan, not started)
- Real Google Docs export and Google Calendar writes with least-privilege scopes (each needs its own PO go and a contract change; `API_CONTRACTS_CALENDAR.md` is a draft).
- PDF and Word syllabus files through the document reader.
- Practice-test import and adaptive quizzes (`ai/practice-test-learning-design.md`), concept evidence, spaced review, a knowledge map.
- Low-pressure executive-function coaching and selectable engagement modes.
- Open public sign-up (own domain, privacy policy, Google brand verification). Not authorized now (PO-20cg).

## Working rules
- The Lead orders the work and integrates it. Hard design and implementation go to the two senior engineers as a pair; `qa-engineer` verifies independently (see `CLAUDE.md`).
- No two agents edit the same files at the same time. Contracts are frozen before parallel web and API work starts.
- Real providers, spending beyond the USD 25 a month budget, deploys, recording other people and G1 wording each need the PO's own go.
