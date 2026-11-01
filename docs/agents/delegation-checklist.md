# Delegation checklist (Lead Architect)

Every delegation prompt:
1. Task and files the agent owns (and files it must not touch).
2. Acceptance criteria and the exact checks to run.
3. Contract/decision refs that govern the task (file and section). Read them before writing the instruction; if the instruction would change a frozen contract, change the contract text first and say so.
4. Do NOT restate playbook facts (ports, Docker command, Playwright configs, rate limits, no-commit). Write "playbook sections 2 and 5 apply" and give only what differs for this task.
5. Open decisions the agent must not decide (point at DECISIONS.md numbers).
6. After a push that touches CI, read the GitHub run and report the conclusion (playbook section 2).
