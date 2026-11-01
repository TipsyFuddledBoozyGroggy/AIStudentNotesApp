# Refinement: first GitHub CI day incidents (2026-11-01)

Status: PROPOSED. Author: agent-refiner. Evidence read: `git log` and `git show` for 4a842e9, 96a1caf, f8b45f3, 20f06f2, 55318f0, 6e1c520; `docs/ops/ci-first-run-e2e-diagnosis.md`; the playbook and lessons log as of d570477; all 18 `.claude/agents/*.md`. Not available to me: the delegation prompts and agent handbacks. Where an item depends on them, it says so. I ran no tests and no workflows; I ran `gh repo view` (read-only) and greps.

Important ordering fact: the playbook, the lessons log and the Tether-specific lines in five definitions arrived in f8b45f3 (08:46), AFTER incidents 1 to 3 (4a842e9 08:22, 96a1caf 08:37). So for incidents 1 to 6 the main fixes are already APPLIED. This file records what is still open.

## Per-incident attribution

| # | Incident | Cause class | Already applied | Still open |
|---|---|---|---|---|
| 1 | UTC browser syllabus create 400 | (b) playbook lacked it at the time; (a) QA/frontend/backend definitions lacked it | L-UTC, frontend and QA lines, e2e zone pinned plus UTC test | None. See "siblings" below |
| 2 | Phone status bar 3 rows on Linux | (b) | L-BAR, frontend line | None. A fixed number (16px) is a heuristic; the CI geometry gate is the real control |
| 3 | gitleaks 3666 plus CodeQL red | (a) devops had no "run what you can locally" rule; (e/f) gh was unavailable, so a run could not be read | L-GITLEAKS, L-CODEQL, devops line; gh now authenticated | See P3 (after-push check) |
| 4 | B2PushTests 4401 vs 4409 | (b) | L-HELLO, 4 tests fixed, deterministic repro | Frontend open question (P6) |
| 5 | axe target-size vs sticky header, Linux only | (b) for the rule; (c)-like for QA: absence claimed from sparse sampling | L-STICKY, straddle check | P4 (QA line) |
| 6 | Dev server reported running after it exited | (b) rule did not exist; later covered by playbook rule 2 and L-STATUS | Rule 2, L-STATUS | P2 (wording) |
| 7 | Backend told to add bare `UTC`, refused | (d) Lead prompt conflicted with the contract; the agent behaved correctly. No definition defect | none | P1 (make refusal behaviour explicit and shared), P5 (Lead checklist) |
| 8 | Lead re-stated environment facts in every prompt | (d) Lead process; playbook section 2 now holds the facts | Playbook section 2 | P5 (Lead checklist: point at sections, send only deltas) |

### Incident 7 in detail

Evidence: commit 96a1caf adds to `docs/API_CONTRACTS_P2.md` section 2.3 "For UTC the only accepted id is `Etc/UTC` ... bare `UTC`, `GMT`, `Etc/GMT` ... are `400` (`not_allowed`)", and adds only `"Etc/UTC"` to `ZoneAllowList` (Support.cs line 256). The playbook section 5 repeats it. So the instruction to add bare `UTC` contradicted a contract that existed when the agent read it. The agent's refusal was correct and is the desired behaviour; I found no evidence of an agent fault. The closest existing rule is working-method step 3 ("document the question for the Lead instead of silently deciding another team's contract"), which covers agents deciding, not the Lead instructing against the contract. Making that explicit is cheap and protects the case where the next agent does follow the instruction. I cannot see the delegation prompt, so I cannot say whether the Lead wrote the instruction before or after the contract text existed; the fix on the Lead side is the checklist in P5.

## Proposed edits

### P1. Playbook section 1, add item 10 (shared by all agents)

Old: (end of section 1, after item 9)

New:
```
10. **Contracts and decisions beat instructions.** If the task, including a Lead instruction, conflicts with a frozen contract, a `docs/DECISIONS.md` entry, an invariant or a gate, do not follow it silently. Do the part that does not conflict, quote the conflicting file and line, and ask. (Worked example: asked to add bare `UTC` to the zone allow-list, the backend agent correctly refused because `API_CONTRACTS_P2.md` 2.3 says `Etc/UTC` only.)
```
Conflicts: none. It reinforces CLAUDE.md "Lead owns contract text": the Lead can change the contract, not bypass it via a prompt.

### P2. Playbook section 1 item 2, tighten (incident 6)

Old: `Do not say a server is "still running" or a test "passes" without checking just now.`

New: `Do not say a server is "still running" or a test "passes" without checking just now, and quote the check you used (for example the HTTP status from \`curl\` on the port, or the test summary line). A process started earlier in your own session is unknown until re-checked.`

### P3. Playbook section 2 GitHub bullet, add after-push check (incident 3)

Old: `a workflow change is not proven until a GitHub run shows it.`

New: `a workflow change is not proven until a GitHub run shows it. After any push that touches \`.github/\`, \`.gitleaks.toml\` or CI-run test config, read the run result (\`gh run list\`, then \`gh run view <id> --log-failed\`) and report the conclusion; "authored, unproven" is the state until you have.`

(The Lead pushes, so this is mainly a Lead habit; devops-engineer reads it when asked to diagnose.)

### P4. `qa-engineer.md` Tether-specific, one added sentence (incident 5)

Old: `... Save the stack trace for any intermittent failure.`

New: `... Save the stack trace for any intermittent failure. When you claim a defect is absent from a sampled scan (scroll positions, widths, zones), state the sampling step; coarse sampling cannot prove absence.`

Size: +150 bytes, file stays under 3.5 KB.

### P5. New Lead-facing file `docs/agents/delegation-checklist.md` (incidents 7 and 8)

```
# Delegation checklist (Lead Architect)

Every delegation prompt: 
1. Task and files the agent owns (and files it must not touch).
2. Acceptance criteria and the exact checks to run.
3. Contract/decision refs that govern the task (file and section). Read them before writing the instruction; if the instruction would change a frozen contract, change the contract text first and say so.
4. Do NOT restate playbook facts (ports, Docker command, Playwright configs, rate limits, no-commit). Write "playbook sections 2 and 5 apply" and give only what differs for this task.
5. Open decisions the agent must not decide (point at DECISIONS.md numbers).
```
Reason: incident 8 (re-stated facts risk drifting from the playbook and bloat every prompt) and incident 7 (instruction written without the contract open). This file is Lead process, not an agent definition, so it sits under `docs/agents/`.

### P6. Open item, not a definition change

L-HELLO open question: does the web client treat close code 4401 on the first hello as "session not available" rather than "signed out"? Route as a task to `frontend-engineer` with a failing unit test first (playbook rule 4). Until checked, the log entry stays partly open.

## Siblings (same weakness elsewhere)

- Zone/locale: `google-integration` will own Calendar mapping with zones (calendar.v1 DRAFT). Playbook section 5 and L-UTC already apply to it; I do not propose a definition line (no incident there, and it would be speculation).
- Sparse sampling (P4): `device-accessibility-validation` also reports scan results; its existing rule "never claim a manual test was run" does not cover sampling density, but its scans are human-run, so no change.
- Unverified process state (P2): applies to every agent with Bash; handled in the playbook, not per definition.

## How to tell it worked

- P1: the next time a prompt conflicts with a contract, the handback quotes file and line instead of either complying or refusing without citation.
- P2: no future handback says "still running" without a quoted check.
- P3: no push to `.github/` leaves its run unread for more than the same session.
- P4: no QA claim "geometry stable" without a stated sampling step.
- P5: delegation prompts shrink and stop duplicating playbook facts; fewer contract-conflict instructions.

## Risks

- P1 could make an agent refuse a legitimate Lead override; mitigation: the text says do the non-conflicting part and ask, so the Lead can answer "contract updated" in one line.
- P2 and P3 add words to the playbook; both are single sentences tied to real incidents.
- Over-reliance on the 16px heuristic (incident 2) is unchanged; the CI geometry gate is the control.
