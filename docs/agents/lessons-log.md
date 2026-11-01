# Agent lessons log

One entry per defect, failed run, wrong report or repeated re-explanation that a better agent definition or playbook could have prevented. Written by `agent-refiner` (proposals) and accepted by the Lead (who applies changes to `.claude/agents/*.md` and `docs/agents/project-playbook.md`).

Entry format:
`### YYYY-MM-DD <short title>` / **Symptom** / **Root cause** / **Which agent(s)** / **What would have prevented it** / **Status** (PROPOSED, APPLIED in <commit>, REJECTED with reason).

### 2026-11-01 Syllabus create failed on UTC browsers (found by first GitHub CI run)
- **Symptom:** real-stack e2e `syllabus.spec.ts` got 400 on create; passed locally.
- **Root cause:** client maps a UTC browser to `Etc/UTC`; server allow-list lacked it; dev machine is US-zoned so it never showed.
- **Agents:** frontend-engineer (client helper), backend-engineer (allow-list), qa-engineer (tests not run in UTC).
- **Prevention:** playbook rule "test zone/locale-dependent code in UTC"; QA definition: pin and vary the timezone in e2e.
- **Status:** APPLIED (playbook L-UTC; Playwright zone pinned, explicit UTC test).

### 2026-11-01 Phone status bar wrapped on Linux (CI geometry failure)
- **Root cause:** ~8px slack at 390px; Linux text shaping slightly wider.
- **Agents:** frontend-engineer.
- **Prevention:** playbook L-BAR: keep >= 16px slack in tight rows; do not tune to the Windows render only.
- **Status:** APPLIED.

### 2026-11-01 Gitleaks 3666 false positives; CodeQL red
- **Root cause:** full-history scan of test literals; CodeQL upload impossible on a private repo; C# buildless restore failed on .NET 10.
- **Agents:** devops-engineer (workflows authored without a run), production-readiness-auditor.
- **Prevention:** playbook L-GITLEAKS / L-CODEQL; devops definition: dry-run locally what can be run (gitleaks full history), state plainly what only a GitHub run can prove.
- **Status:** APPLIED.

### 2026-11-01 Slice 7 agent reported the dev server "still running" after it had exited
- **Prevention:** playbook rule 2 and L-STATUS (verify state with a command before reporting).
- **Status:** APPLIED.
