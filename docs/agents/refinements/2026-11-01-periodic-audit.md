# Periodic audit of all 18 agent definitions (2026-11-01)

Status: PROPOSED. Author: agent-refiner. Read: all `.claude/agents/*.md`, the playbook, the lessons log, `docs/RELEASE_GATES.md` (one line), `docs/ops/ci-matrix.md` (greps), `.github/workflows/codeql.yml` (grep). Ran: Glob/ls to check each path named below, `git grep` for leftovers, a name-versus-filename comparison for all 18 files, and `gh repo view --json visibility` (read-only). Not run: any test or workflow.

## Clean results

- Rename leftovers: `git grep adhd-ux` returns nothing. `CLAUDE.md`, all definitions and the playbook use `neurodivergent-ux-specialist`. The word "ADHD" still appears in docs/ux, `PromptLibrary.cs`, `LanguageGuard.cs`, `copyLint.ts` and tone fixtures; these look like intentional banned-word or history references (not checked line by line; not an agent-definition matter). Note: the `CLAUDE.md` snapshot injected into my session start still showed the old agent name and "ADHD-friendly"; the file on disk is already renamed, so a Lead session started before d570477 should be restarted so its routing table matches.
- Every definition's `name:` equals its filename (18 of 18).
- Playbook paths exist: `tokens.ts`, `copyLint.ts`, `.gitleaks.toml`, `docs/security/severity-policy.md`, `docs/legal/legal-register.md`, `docs/ux/redesign-round2.md`, the four `*.reuse`/screens Playwright configs.
- Sizes: 2.5 to 3.9 KB; all under about 4 KB. `security-engineer` (3.86 KB) and `agent-refiner` (3.65 KB) have no room; any addition there needs a cut.

## Ranked findings

### 1. (HIGH value, easy) Stale deliverable paths in five assurance definitions, one self-contradiction

The directories do not exist; the real ones differ. A new agent following its definition creates a second, parallel docs tree.

| Definition | Says | Reality |
|---|---|---|
| legal-compliance-reviewer | `docs/compliance/RECORDING_AND_PRIVACY_REVIEW.md`, `docs/compliance/UNIVERSITY_POLICY_CHECKLIST.md` (`docs/compliance` absent) | `docs/legal/` (`recording-compliance-gap-report.md`, `legal-register.md`, ...). The same file's severity paragraph already says `docs/legal/legal-register.md`: self-contradiction |
| production-readiness-auditor | `docs/operations/PRODUCTION_READINESS.md`, `CI_SECURITY_GAPS.md`, `OBSERVABILITY_AND_COST.md`, `BACKUP_RESTORE_DRILL.md` (`docs/operations` absent) | `docs/ops/` (`production-readiness-audit.md`, `ci-matrix.md`) |
| device-accessibility-validation | `docs/validation/...` (absent) | `docs/qa/device-accessibility-plan.md` exists |
| human-research-coordinator | `docs/research/HUMAN_VALIDATION_PLAN.md` etc. | `docs/research/human-validation-plan.md` (only file) |
| vendor-verification-researcher | `docs/vendors/PROVIDER_DECISION_MATRIX.md`, `CLAUDE_API_FACT_CHECK.md`, `MEASUREMENT_PROTOCOL.md` | `docs/vendors/verification-report.md`, `google-verification-report.md` |

Proposed edit (each file, "Required deliverables" section): replace the file list with the existing file(s) plus "extend these; create a new file under the same folder only if the topic is new". Exact new text, one per file:
- legal: `Required deliverables: update docs/legal/recording-compliance-gap-report.md and docs/legal/legal-register.md (extend, do not fork). Each draft is marked NOT LEGALLY APPROVED and has reviewer/name/approval fields and release-gate status.`
- prod-readiness: `Required deliverables: extend docs/ops/production-readiness-audit.md and docs/ops/ci-matrix.md with actual commands, run URLs, outcomes and evidence references, or explicit NOT RUN.`
- device-a11y: `Required deliverables: extend docs/qa/device-accessibility-plan.md (matrix, scripts, lecture-hall protocol, issue register referencing actual evidence); split into new files under docs/qa/ only when it grows.`
- human-research: `Required deliverables: extend docs/research/human-validation-plan.md; add session script, expert rubric and validation log files beside it.`
- vendors: `Required deliverables: extend docs/vendors/verification-report.md; add the decision matrix and measurement protocol beside it. Label unknown, verified, measured and estimated values.`

Size change is neutral or slightly smaller. Decision-right check: none altered.

### 2. (MEDIUM) Contradictory role sentence in 12 specialist definitions

"Your role is advisory and implementation-focused only within your specialty." is in the 12 engineering definitions. For `frontend-engineer`, `backend-engineer`, `devops-engineer` and others, the work in git history is implementation, and the sentence ("advisory") conflicts with "Own..." and with the playbook, which has them editing code and running tests. It is ambiguous rather than harmful, but it will confuse an agent asked to implement.

Old: `Your role is advisory and implementation-focused only within your specialty.`
New: `Work only within your specialty and the files your task assigns; implement when the task says so, otherwise advise.`
Applies to: ai-agent-engineer, backend-engineer, database-architect, devops-engineer, frontend-engineer, google-integration, learning-science, neurodivergent-ux-specialist, product-manager, qa-engineer, security-engineer, speech-engineer. (learning-science, ux, product-manager have only Read/Grep/Glob/Write/Edit, so for them "advise" is the practical mode.) Same size.

### 3. (MEDIUM, saves about 1.5 KB per file) Duplicated boilerplate that belongs in the playbook

The 7-step "Mandatory working method" and the "Escalate to Lead Architect / Product Owner" paragraph are identical in 12 definitions (about 1.5 KB each, 18 KB total). Steps 4, 5 and 7 repeat playbook section 1 items 1, 2 and 3. Also each has a stray double blank line after "Read first".

Proposal (Lead decision): add the 7 steps and the escalate paragraph once to the playbook as section 0 "Standard method", and in each of the 12 definitions replace both blocks with one line: `Follow the standard method and escalation list in playbook section 0.` Benefit: a future rule changes in one place; frees space in the tight files (`security-engineer`). Risk: an agent that does not open the playbook loses the method. Mitigation: the "Read first" line already makes the playbook mandatory and is the first line of every definition. If the Lead prefers not to take that risk, do only the blank-line cleanup. I would apply this after the current slice, not mid-slice, so no running agent reads a half-edited definition.

### 4. (MEDIUM, factual conflict outside agent files) Repo visibility statements disagree

`gh repo view --json visibility` returns PUBLIC (I ran it). The playbook (public) and `docs/ops/ci-matrix.md` line 94 (public) agree. But `.github/workflows/codeql.yml` header still says "The repo is PRIVATE; code scanning on a private repo needs GitHub Advanced Security", `docs/ops/ci-first-run-e2e-diagnosis.md` calls it private, `ci-matrix.md` line 114 says "Unconfirmed for this repo", and L-CODEQL says upload "needs a public repo", which, if the repo is now public, means the CodeQL upload failure should no longer apply. I cannot tell from here whether the visibility changed after 4a842e9 or the comment is stale. Action for the Lead (no definition edit): re-run CodeQL and update the comment, or correct L-CODEQL. Why it matters to agents: `security-engineer` is told "the repo is public: flag anything committed", so an agent reading the stale comment sees a contradiction.

### 5. (MEDIUM) Gate statuses duplicated in the playbook will drift

Playbook item 7 restates gate statuses ("G5 is only authored, partly run"). `docs/RELEASE_GATES.md` is the canonical register and its G5 row still says "CI first pushed 2026-10-27 and not yet confirmed passing" (it was first run 2026-11-01, red then fixed). Three places already disagree in wording (CLAUDE.md "NOT VERIFIED", RELEASE_GATES "BLOCKED / NOT_VERIFIED", playbook "authored, partly run").

Old (playbook item 7): `G1 (recording/consent) is BLOCKED, G2 UNVERIFIED, G3/G4 NOT_RUN, G5 is only "authored, partly run".`
New: `Current gate statuses are in docs/RELEASE_GATES.md (canonical); none of G1 to G5 is PASS, and G1 is BLOCKED.`
Also ask the Lead to refresh the RELEASE_GATES G5 row (owner: Lead, evidence: the 2026-11-01 run records).

### 6. (LOW-MEDIUM) Playbook lessons with no lessons-log entry

L-DEVSERVER, L-RATE, L-CHECKOUT, L-DROP and L-FORGE are in the playbook table but not in the log, so the "every rule exists because something went wrong" claim cannot be traced for them. L-DROP and L-FORGE trace to SBR-06 and SBV-01 in `docs/security/findings-register.md`; L-DEVSERVER and L-RATE trace to the playbook's own described symptoms; I did not find a commit that records L-CHECKOUT's origin (not searched exhaustively). Proposal: the Lead adds one-line log entries marked "pre-log, provenance from register/Lead memory" so the log is the single index. I wrote none, because I would be inventing provenance.

### 7. (LOW) Rules never tied to an incident (candidates for deletion or verification)

- Playbook item 9 (scope calibration) rests on PO-15/16, not an incident; it is a Product Owner decision, keep.
- Definitions' "Tether-specific" blocks are all justified by 2026-11-01 incidents or by the severity policy. No deletion candidates there.
- The long mission paragraphs in non-incident agents (ai, database, google, speech) contain no Tether-specific facts (no module names, ports, contract versions). I propose no additions: no incident points to them, and adding rules without one is what this role should not do. They do not mention `calendar.v1` DRAFT or that the AI client is mock-only; the playbook section 3 covers both.

### 8. (LOW) Tool and model observations (escalate, no change proposed)

- `agent-refiner` has all tools although it must only edit `docs/agents/`; this is enforced by instruction, not by the tool list. Decision right belongs to the Lead; I flag, not change.
- `neurodivergent-ux-specialist`, `learning-science`, `product-manager` have no Bash. Their working-method step 5 ("report commands actually run") is satisfied by "none run"; no conflict.
- Model mix (opus: frontend, security; fable: ux; sonnet: the rest). The incident set shows no evidence that model choice caused a miss (the misses were missing facts and sparse tests). No change.

## What I did not do

I did not edit any `.claude/agents` file, `CLAUDE.md` or the playbook. All old/new text above is for the Lead to apply.
