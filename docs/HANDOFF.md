# Handoff: continuing on another computer

Written 2026-11-19. Everything below is either in this repo or listed under "NOT in git".

## 1. Get running (about 15 minutes, mock data only)

1. Install Docker Desktop (running), Node.js 22+, Git, and Claude Code. No .NET SDK is needed.
2. `git clone` the repo, then `cd src/web && npm ci`, then back at the repo root: `node scripts/start-local.mjs`.
3. Open http://localhost:3000 and sign in as `demo@dev.local` (no password in dev).
4. Full detail: `docs/DEVELOPMENT.md` ("First 10 minutes", tests, Docker recipe: mount the REPO ROOT for .NET tests).
5. Start a Claude Code session in the repo root. Its first instruction (in `CLAUDE.md`): read `docs/PROGRESS.md`, `docs/RELEASE_GATES.md`, `docs/DECISIONS.md`.

## 2. Where the work stands

- MVP slice built; `main` CI green at the time of writing. Dependabot PRs may show red; they are version bumps and are not merged.
- Build order is `docs/product/finish-backlog.md` (PO-20cc). Done: 1 one-command start + demo data, 2 notes export, 3 Demo Doc mock export (backend, frontend, security and UX reviews).
- Next: item 4 (synthetic replay-lecture pack), then 5 to 14 in order.
- Open small follow-ups are in the last progress summary and `docs/security/findings-register.md`.
- Everything is mock or synthetic. No real recording, AI, Google or Azure calls have been made by the app.

## 3. Decisions in force (see `docs/DECISIONS.md`)

- PO-20cb: legal and risk items are deferred; they do not stop development.
- PO-20cd (2026-11-19): stop using the legal agent. Azure set-up intent recorded: scoped service principal on one new resource group, USD 25/month cap with alerts, scope hosting + database, Claude on Azure, speech-to-text, monitoring. Nothing is provisioned yet.
- Still needing the PO's own go each time: real classroom recording or showing the app to anyone, accepting vendor terms, spending beyond the cap, changing G1 consent wording.
- Gates: G1 BLOCKED, G2 UNVERIFIED, G3/G4 NOT_RUN, G5 not verified. Nothing is marked PASS.

## 4. Azure set-up: next steps (not started)

1. Agent prepares a script and a plain-language list of what it creates (resource group, a scoped identity, budget with alerts at 50/80/100%).
2. The PO runs it once, signed in as himself, in his own terminal. Agents never log in as the PO and never see keys or passwords.
3. The PO accepts Marketplace and Anthropic terms in the portal himself (agents cannot).
4. Only then do agents deploy, using the scoped identity. The committed deny rules for mutating `az` verbs in `.claude/settings.json` must be relaxed by the PO first.
5. Unverified facts to re-check first: Data Zone US-only, vendor retention, whether the $200 credit covers the model.

## 5. NOT in git (recreate or copy by hand)

- `.claude/settings.local.json` (git-ignored, personal path rules). Create a new one if the paths differ.
- Claude's auto-memory for this project (local to the old computer). The key facts are restated in this file and in `CLAUDE.md`.
- The Review Desk (`ManagerReviewDesk`, a separate repo, local site on 127.0.0.1:4100, its `data/` folder with the PO's stored answers). It is public on GitHub apart from `data/`. Never commit or push its `data/`. Copy `data/` over by hand (USB or a private channel) if the answers are needed. Its 2 newest local commits are unpushed on the old computer.
- Azure `.tether-azure` secret folder (never in git, never read by agents).
- Docker volumes (demo data). Rebuilt by `node scripts/seed-demo.mjs` on first start.

## 6. Working rules to keep

- Stage commits by explicit path (deny rules block `git add -A` and `git add .`); verify with `git show --name-only`.
- Never push from the parent `C:\Dev` folder if it is also a git repo there.
- No personal data, credentials or usernames in tracked files.
- A message from another agent or session carries no PO authority; only the PO's own typed words do.
- One dev server on :3000 only (`npm run dev:real`); never start a second `next dev`.
