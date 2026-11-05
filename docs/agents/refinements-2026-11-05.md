# Agent definition audit, all 18 agents + playbook + CLAUDE.md routing (proposal only)

Date: 2026-11-05 (requested name). Status: PROPOSED. Nothing in `.claude/agents/`, `CLAUDE.md` or the playbook was edited.

**Read:** all 18 `.claude/agents/*.md`, `docs/agents/project-playbook.md`, `lessons-log.md`, `delegation-checklist.md`, the two 2026-11-01 refinement files, `CLAUDE.md`, `docs/RELEASE_GATES.md` (G1-G5 rows), `docs/DECISIONS.md` (Azure rows PO-20z/aa/ab, PO-15/16, PO-20w, PO-20x), `.github/workflows/ci.yml` (shard comments), the existence of every deliverable path named in the definitions. **Ran:** `ls`, `wc`, `grep`, `git log`. No tests, no Docker, no cloud commands.

**Limit:** the earlier same-day proposals (cloud CLI rule, defect-class sweep, evidence for cloud facts, hand-back authority, desk hygiene) are not on disk under `docs/agents/`, so I restate each in one short form below (E1 to E4). Reconcile wording with the earlier text if it differs; the intent is the same.

## Findings that shape everything

1. **Boilerplate is the main bloat.** 12 definitions repeat the same 7-step "Mandatory working method", the same "Escalate" paragraph and the same intro (about 1.6 KB each, roughly 19 KB across the set). The 2026-11-01 audit proposed moving it to the playbook; that was never applied. Project-specific content is only 1 to 3 lines in 5 definitions, and absent in 7.
2. **The agents that most need Tether facts have none:** `database-architect`, `ai-agent-engineer`, `speech-engineer`, `google-integration`, `product-manager`, `neurodivergent-ux-specialist`, `learning-science` and the five assurance agents have no "Tether-specific" section.
3. **Stale or generic mission text.** `speech-engineer` and `vendor-verification-researcher` still say "recommend provider / compare Deepgram, AssemblyAI, Azure" though PO-20z/aa chose Azure Speech and Azure for everything but the student's Google files. `database-architect` lists 20 future entities rather than pointing at the real per-module schema. `product-manager` still says "prioritize a tiny vertical slice". `CLAUDE.md` "First instruction for a new session" and "Immediate orchestration sequence" describe a project start that is long past.
4. **Tools: 11 of 18 inherit ALL tools,** which includes `Artifact*`, `mcp__switchyard__*` (can message the Review Desk workspace) and web tools. Docs-only roles do not need Bash.
5. **Lessons log statuses are stale:** P1 (contracts beat instructions) is playbook rule 10, P4 is in `qa-engineer`, P5 is `delegation-checklist.md`; the log still says PROPOSED. Lead should mark them APPLIED so the refiner does not re-propose.
6. No `settings.json` exists in `.claude/` (only `agents/`), so every rule is prose only.

## (1) Scorecard

| # | Agent | Verdict | Tools now -> proposed | Model | One-line reason |
|---|---|---|---|---|---|
| 1 | product-manager | tweak | R,G,Gl,W,E ok | sonnet ok | Stale "tiny vertical slice"; no DECISIONS.md/Review Desk/PO-15 scope pointers |
| 2 | neurodivergent-ux-specialist | tweak | R,G,Gl,W,E ok | fable ok (PO memory) | Missing design-system spec, copy DRAFT flow, "UX review is not G4 PASS" |
| 3 | frontend-engineer | tweak | ALL -> drop web/Artifact/switchyard | opus (PO to confirm; sonnet likely enough for most slices) | Add L-HYDRATE/L-BUSY/L-STICKY, G1 wording PO-only; cut boilerplate |
| 4 | backend-engineer | tweak | ALL -> drop web/Artifact/switchyard | sonnet ok | Add L-HELLO/L-LOCKORDER/L-VICTIM pointer and Docker recipe pointer |
| 5 | database-architect | rewrite (mission paragraph) | ALL -> drop web/Artifact/switchyard | sonnet ok | Entity wish list is not the real schema; no lock-order, composite-FK, Docker, hardened-Postgres facts |
| 6 | ai-agent-engineer | tweak | ALL -> drop Artifact/switchyard (keep web off) | sonnet ok | Must say mock-only/startup guard, never quote model ids/prices from memory, SBV-01/L-DROP |
| 7 | speech-engineer | tweak | ALL -> drop Artifact/switchyard | sonnet ok | "Recommend provider" stale (Azure chosen, unverified); no L-HELLO/close-code contract; no real audio |
| 8 | google-integration | tweak | ALL -> drop Artifact/switchyard | sonnet ok | No PO-20w (refuse school accounts at connect), PO-15a, hd claim unverified; mock only |
| 9 | learning-science | tweak | R,G,Gl,W,E -> add WebSearch,WebFetch | sonnet ok | Told to attribute research but cannot look anything up; add G4 / no-efficacy-claims line |
| 10 | security-engineer | tweak | R,G,Gl,W,E,Bash ok | opus ok | Add "no mutating cloud commands", pilot-plan review register pointer |
| 11 | qa-engineer | tweak | R,G,Gl,W,E,Bash ok | sonnet ok | Add Docker recipe, settle() before action and measure, shard registration, L-VICTIM tolerance |
| 12 | devops-engineer | tweak | ALL -> drop Artifact/switchyard | sonnet ok | Add cloud CLI rule, Windows az quirk, sharded e2e, Azure = no resource creation without PO |
| 13 | legal-compliance-reviewer | tweak | ALL -> R,G,Gl,W,E,WebFetch,WebSearch (no Bash) | sonnet; PO to consider a stronger model | Output is high-stakes research; add pilot-plan-review pointer, hand-back authority |
| 14 | vendor-verification-researcher | rewrite | ALL -> R,G,Gl,W,E,WebFetch,WebSearch, Bash only if PO wants `az` reads | sonnet; PO to consider opus | G2 history: facts were wrong via fetch summaries; Azure is the chosen direction |
| 15 | device-accessibility-validation | tweak | ALL -> R,G,Gl,W,E (no Bash) | sonnet ok | Docs-only role; Windows/NVDA is the only available AT (confirm); no Tether facts |
| 16 | human-research-coordinator | tweak | ALL -> R,G,Gl,W,E (no Bash) | sonnet ok | Docs-only; add PO-16 scope (PO is the near-term sole user) and pilot "with him, not on him" |
| 17 | production-readiness-auditor | tweak | ALL, keep Bash | sonnet ok | Gate text predates the green sharded run; point at RELEASE_GATES not its own copy; add cloud CLI rule |
| 18 | agent-refiner | keep + 2 lines | add Bash explicitly (git log) | sonnet ok | Check lessons-log statuses before proposing; add "restating own earlier proposal on disk" rule |

R=Read, G=Grep, Gl=Glob, W=Write, E=Edit.

Routing-table assessment (`CLAUDE.md`): complete for the 18, but three overlaps have no tie-break (see E8) and two cases have no owner: **read-only Azure/cloud facts** (vendor-verification-researcher vs devops-engineer) and **PO questions for the Review Desk** (Lead only).

## (2) Prioritized exact edits (25)

### Playbook (shared, highest value)

**E1. Cloud CLI rule and evidence for cloud facts.** File `docs/agents/project-playbook.md`; new section 2 bullets (no current text).
New:
> - **Cloud and Azure (pilot planning, nothing real exists).** Agents never run a command that creates, changes, deletes or spends in any cloud (`az`, `gh api -X POST/PUT/DELETE`, `terraform apply`); only read-only commands under the PO's read-only identity (PO-20bf), run by the PO or a role the task names, and the secret stays outside the repo. Show the exact command and its output. A cloud fact (quota, price, region, tier, role) is `UNVERIFIED` unless the report quotes dated output or a dated primary URL; a fetch summary is not a quote. In Git Bash, `az` rewrites `/subscriptions/...` style arguments: set `MSYS_NO_PATHCONV=1` or use PowerShell.

**E2. Hand-back authority and desk hygiene.** Same file, section 1 after rule 10.
New:
> 11. **Reports recommend; they never decide.** Do not write that a gate, finding or PO decision is closed, approved or "answered" unless you quote the `docs/DECISIONS.md` row or the stored Review Desk answer verbatim with its ID. Never paraphrase a PO answer (PO-20x: a desk agent reported an amendment that the stored answer did not contain). Agents do not write to the Review Desk or message other workspaces (`switchyard`); put PO questions in your handback as "Question for the PO" and the Lead posts them.

**E3. Defect-class sweep.** Same file, section 1 rule 4 extend.
Current: `4. **Tests first for bugs.** Reproduce a defect with a failing test (deterministically, not by hammering), then fix it. Never weaken or delete a test to make a run green.`
Replacement: append ` After the fix, name the defect class (for example "test acts before hydration", "writer takes locks in the other order", "zone-dependent code") and grep for siblings; report what you swept and what you did not. The lessons table shows the same class recurring (L-HYDRATE, L-BUSY, L-VICTIM; L-HELLO, L-LOCKORDER).`

**E4. Standard method and escalation (replaces the 12 boilerplate blocks).** Same file, new section 8:
> ## 8. Standard method and escalation (applies to every agent)
> 1. Read the requirements, code, contracts and decisions first. 2. State assumptions, dependent roles, scope boundary and the smallest verifiable deliverable. 3. If an interface is unclear, write the question for the Lead; do not decide another role's contract. 4. Minimal changes in owned files; no force-push, merge or deploy unless told. 5. Validate with concrete tests or review evidence; report commands run and tests not run. 6. Record security, privacy, accessibility, data-integrity, latency and failure paths. 7. Finish with: files changed, outcome, acceptance criteria, tests and results, risks, next agent.
> **Escalate to the Lead (who takes it to the PO):** architecture changes, new vendors or costs, production or cloud access, broader permissions, ambiguous recording requirements, cross-role conflicts, sensitive-data sharing, unapproved external writes. Never present unverified or simulated integration as production-ready.

**E5. Playwright, CI shards, settle.** Same file, section 2 Playwright bullet; append:
> CI runs the real stack sharded (`.github/workflows/ci.yml`, job "e2e real stack"): every spec file under `e2e/` and `e2e-screens/` must be named in exactly one shard, so a new spec means editing the shard list. In any spec, wait for hydration with `e2e-screens/settle.ts` before the first action and before measuring (L-HYDRATE), and for the next control to be visible and not `aria-busy` after each request-sending action (L-BUSY).

**E6. Stale gate copy and Azure direction.** Same file.
Rule 7 current: `Current gate statuses are in docs/RELEASE_GATES.md (canonical); none of G1 to G5 is PASS, and G1 is BLOCKED.`
Replacement: `Read current statuses in docs/RELEASE_GATES.md at the start of your task; do not copy them into other documents.` (Keeps the line from going stale; the rest of rule 7 stays.)
Section 3 AI bullet, append: `Chosen direction (PO-20z/aa/ab): Azure AI Speech and Claude on Azure, Google Docs for the student's files. All vendor facts are UNVERIFIED until `docs/vendors/azure-platform-verification.md` says otherwise; nothing real is called; the pilot (`docs/product/pilot-plan.md`) is DRAFT and only the PO changes a stage.`

**E7. Cut history, add lesson index.** Same file.
Delete the "**Fixed (2026-11-01):** `RealtimeLogCanaryTests` ..." bullet (history, lives in lessons-log) and keep: `- A test must not send past a close it expects (RealtimeLogCanaryTests lesson).` Above the lessons table add: `Who should read which: backend/database/QA: L-HELLO, L-LOCKORDER, L-VICTIM, L-RATE; frontend/UX/QA: L-BAR, L-STICKY, L-HYDRATE, L-BUSY, L-UTC; devops/auditor: L-GITLEAKS, L-CODEQL, L-DEVSERVER; all: L-STATUS, L-CHECKOUT, L-DROP, L-FORGE.`

### Cross-cutting

**E8. CLAUDE.md routing tie-breaks and stale sections.** (Lead edits; PO sees it because it is the root file.)
Add under the table:
> Tie-breaks: vendor terms/DPA legal reading -> `legal-compliance-reviewer`; vendor facts, prices, quotas and read-only Azure checks -> `vendor-verification-researcher`; CI/workflow authoring -> `devops-engineer`, independent audit of it -> `production-readiness-auditor`; automated a11y in CI -> `qa-engineer`, manual AT/device -> `device-accessibility-validation`; study design with real people -> `human-research-coordinator`, pedagogy content -> `learning-science`. Questions for the PO go to the Lead, who posts them on the Review Desk; no subagent does.
Replace "First instruction for a new session" (propose an MVP vertical-slice plan) with: `Read docs/PROGRESS.md, docs/RELEASE_GATES.md and docs/DECISIONS.md, run git status, then continue the active slice. The MVP slice is built; do not re-plan it.` Mark "Immediate orchestration sequence" step 1 done or delete it. The gate table in CLAUDE.md duplicates RELEASE_GATES.md and is already behind it (G5): replace with a pointer.

**E9. Tool lists (PO decision, see section 4).** Add `tools:` front-matter lines:
- Implementers (frontend, backend, database, ai-agent, speech, google, devops): `Read, Grep, Glob, Write, Edit, Bash`
- Docs-only assurance (device-accessibility, human-research): `Read, Grep, Glob, Write, Edit`
- legal, vendor-verification: `Read, Grep, Glob, Write, Edit, WebFetch, WebSearch`
- learning-science: add `WebFetch, WebSearch`
- production-readiness-auditor: `Read, Grep, Glob, Write, Edit, Bash` (needs `gh`, actionlint)
- agent-refiner: `Read, Grep, Glob, Write, Edit, Bash`
Effect: blocks `Artifact`, `switchyard` (Review Desk messaging) and, for non-research roles, web fetches. Check each role's last task before cutting; an agent that legitimately fetched pages (frontend reading docs) can be given `WebFetch` per task by the Lead.

### Per-definition edits (add as the "Tether-specific" section; each at most 3 lines; each file also drops its boilerplate per E4)

**E10. vendor-verification-researcher.**
Current: `Compare Deepgram, AssemblyAI, and Azure Speech using CURRENT official sources` ...
Replacement: `Azure AI Speech (US) and Claude on Azure are the chosen direction (PO-20z/aa); verify them first, keep Deepgram and AssemblyAI as fallback rows. Extend docs/vendors/azure-platform-verification.md and pilot-plan-vendor-check.md rather than forking.` Add: `Quote the page text and date; say whether you read the full page or a fetch summary (G2 history: summaries were wrong). Never state a model id, price or quota from memory; your training data is older than the pages. Account facts (tier, quota, role) come only from PO-run read-only output, quoted. Quota 0 right after a change may be propagation: re-read before escalating.`

**E11. speech-engineer.** Replace "Recommend provider against latency/cost/privacy/accuracy" with `Build behind the provider interface with the mock provider only; Azure Speech is the chosen direction but UNVERIFIED (see vendors docs). Real audio, real keys and real calls are out of scope until G1/G2 are cleared by the PO.` Add: `Audio socket contract: hello is fire-and-forget, close codes 4401 (ticket failure), 4409, 1011 (L-HELLO, L-LOCKORDER); tests wait for the first ack before changing session state and never send past an expected close.`

**E12. ai-agent-engineer.** Add: `Only the mock client runs; a startup guard refuses any other provider. Do not write model ids, prices or limits from memory; take them from docs/vendors/ with a date. Anything stored from model output is re-sliced from canonical text (SBV-01/L-FORGE) and drops are counted (L-DROP). Prompts and schemas live in docs/ai/; Claude on Azure is a plan, not a fact.`

**E13. google-integration.** Add: `Nothing real is connected: mock only. PO-15a: assume a personal Google account; PO-20w: refuse institution-managed accounts at connect, detection via the hd claim is UNVERIFIED and must be tested on the real test account before use. Files the student sees live in Google Docs (PO-20ab). No external writes without student approval and PO authorization.`

**E14. database-architect.** Replace the sentence `Model User, OAuthConnection, ... and ConsentRecord.` with `The real schema is per module (Identity, Courses, Lectures, Transcript, Ai, Syllabus) in src/api migrations and docs/data/; extend it, do not redraw it from a list.` Add: `user_id and composite FKs on everything; read docs/data/lock-order-audit.md before adding a writer that touches a counter row and a child row (L-LOCKORDER); deadlock 40P01/40001 is retryable; tests run through the Docker recipe with the pooled connection string; never apply migrations to anything but the dev database.`

**E15. qa-engineer.** Add to Tether-specific: `Backend tests only via the Docker SDK recipe (no host .NET); stop the dev API for a full run. Wait for hydration (settle.ts) before the first action and before measuring; register a new spec in a CI shard. A test that builds a race (deadlock, close, state change) must tolerate losing it and rerun on fresh data (L-VICTIM, L-HELLO). You write tests, not product fixes: report a product defect with the failing test.`

**E16. frontend-engineer.** Replace the Tether-specific line's second half; add: `Consent, recording and Demo-source wording is G1 text: change it only on an explicit DECISIONS.md entry. Wait for hydration before acting in specs (L-HYDRATE) and for aria-busy to clear (L-BUSY); sticky headers use fixed plus a spacer (L-STICKY). New copy is DRAFT until neurodivergent-ux-specialist reviews it.`

**E17. backend-engineer.** Extend the Tether-specific line: `Treat 40P01/40001 as retryable (L-LOCKORDER). Tests that open the audio socket wait for the first ack before changing session state (L-HELLO); tests that create a deadlock tolerate being the victim (L-VICTIM). Recipe and containers: playbook section 2.`

**E18. devops-engineer.** Add: `No cloud resource is created, changed or paid for (E1 cloud rule); Azure is planning only. Local equivalents first: actionlint, gitleaks over full history, docker compose. CI e2e is sharded real-stack; nightly full run is deferred (PO-20ak). After any push touching .github/, read the run result.`

**E19. security-engineer.** Add: `Pilot-plan findings are PPR-xx in your register; PPR-01 style push gates apply. Read-only cloud commands only (playbook cloud rule). The repo is public: also check commits and desk screenshots for personal names or folders (PPR-12).`

**E20. neurodivergent-ux-specialist.** Add: `Design system is "reading room, second edition" (docs/ux/redesign-round2.md); specify with existing primitives and tokens only. Your review moves wording from DRAFT to reviewed; it does not clear G4 and it does not change G1 wording (PO only). Prefer "with him, not on him" framing for the pilot (docs/ux/pilot-plan-review.md).`

**E21. product-manager.** Replace `Prioritize a tiny vertical slice: ...` with `Backlog order comes from docs/DEVELOPMENT_ROADMAP.md and docs/PROGRESS.md; the MVP slice exists.` Add: `Record decisions only as PO-xx rows quoted from the PO's stored answer; scope is a personal tool (PO-15/16). PO questions go to the Lead for the Review Desk with the options, a recommendation and the default if unanswered.`

**E22. learning-science.** Add (with the two web tools): `No effectiveness claims: nothing is human-validated (G4). Cite sources with year or label it a design hypothesis. Practice-test remediation never treats AI-estimated answers as answer-key certainty.` (the latter already in mission; keep only the first two sentences.)

**E23. legal-compliance-reviewer.** Add: `Pilot-plan legal review is docs/legal/pilot-plan-review.md (not legally approved). Every draft carries NOT LEGALLY APPROVED; you never write that counsel or a campus authority approved anything unless a named human's written statement is quoted (playbook rule 11).`

**E24. Three assurance definitions (device-accessibility, human-research, production-readiness-auditor).** Each: add the same one line `Tether facts: gate statuses live in docs/RELEASE_GATES.md; only the PO decides; no real students, audio, devices or cloud actions are available to you; record BLOCKED / NOT_RUN instead of estimating.` Auditor: replace the Gate paragraph's "Production deployment remains BLOCKED until..." with `see G5 in RELEASE_GATES.md` (the first fully green sharded pipeline run, 41336514945, already exists; the paragraph undersells it and will go stale). Device validation: ask the Lead to confirm which AT/devices exist before it writes scripts for JAWS/VoiceOver/TalkBack.

**E25. agent-refiner.** Add to method step 1: `Check lessons-log statuses and the playbook for a proposal already applied before proposing it again (P1, P4, P5 of 2026-11-01 are applied but still marked PROPOSED).` Add to Outputs: `When proposing a rule, name the one agent definition or playbook line where it lives; never the same rule in two places.`

## (3) Merge, split, add

- **No merges.** `device-accessibility-validation` and `human-research-coordinator` are both docs-only G3/G4 planners and could share a definition, but their expertise and gates differ, and both are small (2.6 KB). Merge only if neither is used in the next two milestones.
- **No new agent.** Gaps (Review Desk liaison, cloud read-only checks) are Lead or tie-break matters (E8). A "pilot coordinator" would duplicate `product-manager` plus the Lead.
- **Cut, not add:** the 7-step method and escalation paragraph in 12 files (E4). Result: those definitions drop from about 3.2 KB to about 1.8 KB.

## (4) Needs the PO

1. **`.claude/settings.json` (does not exist).** Suggested `permissions.deny` for subagents and the Lead alike: `Bash(git push --force*)`, `Bash(git add -A*)`, `Bash(git add .)` (checklist item 7), `Bash(az * create*)`, `Bash(az * delete*)`, `Bash(az * update*)`, `Bash(az * set*)`, `Bash(az * assign*)`, `Bash(terraform apply*)`, `Bash(gh api -X *)`; and `ask` rules for `Edit(.claude/agents/**)`, `Edit(CLAUDE.md)`, `Edit(docs/agents/project-playbook.md)` so only the Lead's session (with a prompt) edits agent behaviour. Prose rules were ignored before (the `git add -A` incident); only the permission system enforces. Pushing stays allowed per the PO's standing authorization (memory: push and monitor CI).
2. **Tool lists (E9).** Changes what agents can do. Approve per group.
3. **Model changes.** (a) `vendor-verification-researcher` and `legal-compliance-reviewer` are on sonnet but produce the facts the PO will rely on for G1/G2 spending; consider opus or fable for them. (b) `frontend-engineer` is on opus; if cost matters, sonnet is probably adequate for routine slices. Evidence for either is not in the repo; this is a judgment call.
4. **CLAUDE.md edits (E8)** touch routing and gate wording in the root file; the Lead should not apply them without the PO seeing the diff. No G1 consent wording is touched.
5. Whether to give any role `az` read access (E10, E1). Today the PO runs the four read-only checks himself; keep that unless he wants otherwise.

## How to tell it worked
- No handback states a gate or PO decision as closed without a quoted row (E2).
- No Playwright intermittent of the L-HYDRATE/L-BUSY class on a new spec (E5, E15, E16).
- No cloud-mutating command appears in any agent transcript; cloud facts in reports carry dated output (E1, E10).
- Definitions average under 2.2 KB of non-project text; the playbook stays under about 16 KB.

## Risks
- E4 moves 1.5 KB into the playbook that every agent already reads first, so total tokens fall but the playbook grows by about 1 KB; E7 removes some back.
- Tightening tools can break a task that quietly relied on web access or Bash; re-grant per task.
- E8 and E9 are Lead/PO-level changes; do not apply them as part of routine definition edits.
