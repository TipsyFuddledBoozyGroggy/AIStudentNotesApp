# E8: proposed CLAUDE.md diff (NOT APPLIED)

Status: PROPOSED, for the Product Owner to see before the Lead applies anything. `CLAUDE.md` has not been edited. Source: `docs/agents/refinements-2026-11-05.md`, E8. Diff generated against `CLAUDE.md` at HEAD ecb929d.

## Plain-English summary

1. **Tie-break line under the routing table.** Adds one paragraph saying who wins when two agents overlap (legal reading vs vendor facts, CI authoring vs audit, automated vs manual accessibility, study design vs pedagogy) and that PO questions go through the Lead to the Review Desk, never from a subagent. No row of the table changes.
2. **"First instruction for a new session".** Replaces "propose an MVP vertical-slice plan" (the slice is built) with "read PROGRESS, RELEASE_GATES and DECISIONS, run git status, continue the active slice". Keeps the "label proposals vs code actually executed" sentence.
3. **"Immediate orchestration sequence", step 1.** Struck through as done (substitute gap reports exist). Steps 2 to 6, including "do not mark a gate PASS from a plan..." and the launch go/no-go, are untouched.

## What is NOT changed

- No consent, recording, G1 or Demo-source wording. No architecture invariant. No gate status, owner or evidence text.
- The gate table is **kept as is**. E8 suggested replacing it with a pointer to `docs/RELEASE_GATES.md` only if that file is current. I checked and it is not, so I do not recommend the replacement yet:
  - Header says "Last updated: 2026-10-27", while the G5 row was edited later (2026-11-01 run).
  - G2 says "no vendor chosen" but DECISIONS PO-20z/aa record Azure AI Speech as the preferred direction.
  - G3 says "11 tests"; `src/web/e2e` now has 6+ more spec files (about 43 `test(` calls there alone) plus `e2e-screens`.
  - Its "Open reconciliation" section still treats the assurance agents as not loaded.
  Recommended order: refresh `RELEASE_GATES.md` (Lead, with `production-readiness-auditor` evidence), then, if the PO wants, replace the CLAUDE.md table with a pointer in a second small diff. Until then the CLAUDE.md table (short, status-only, all BLOCKED/UNVERIFIED/NOT RUN) does not contradict the register on any status.

## Unified diff

```diff
--- a/CLAUDE.md
+++ b/CLAUDE.md
@@ -31,6 +31,8 @@
 | CI/security scanning, monitoring, spending and backup/restore readiness audit | `production-readiness-auditor` |
 | Audit agent definitions after an incident; propose project-specific refinements (never edits agents itself) | `agent-refiner` |
 
+Tie-breaks: vendor terms/DPA legal reading -> `legal-compliance-reviewer`; vendor facts, prices, quotas and read-only Azure checks -> `vendor-verification-researcher`; CI/workflow authoring -> `devops-engineer`, independent audit of it -> `production-readiness-auditor`; automated a11y in CI -> `qa-engineer`, manual AT/device -> `device-accessibility-validation`; study design with real people -> `human-research-coordinator`, pedagogy content -> `learning-science`. Questions for the PO go to the Lead, who posts them on the Review Desk; no subagent does.
+
 ## Recommended technical stack
 - Frontend: Next.js / React / TypeScript, accessible UI, mobile responsive.
 - Backend: ASP.NET Core Web API in C#, explicit domain and service boundaries.
@@ -87,7 +89,7 @@
 ```
 
 ## First instruction for a new session
-Before generating broad implementation, examine repository state and propose a narrow **working MVP vertical-slice plan** listing dependencies, owners, acceptance tests and open decisions. Use relevant subagents for review. Implement in verifiable increments. Clearly label proposals vs code actually executed.
+Read docs/PROGRESS.md, docs/RELEASE_GATES.md and docs/DECISIONS.md, run git status, then continue the active slice. The MVP slice is built; do not re-plan it. Clearly label proposals vs code actually executed.
 
 
 ## Explicit assurance gates — read before any real-world testing or deployment
@@ -102,7 +104,7 @@
 | Production CI, scanning, observability, backups | **NOT VERIFIED / NO DEPLOYMENT CLAIM** | `production-readiness-auditor`, `devops-engineer`, `security-engineer`, `qa-engineer` | Actual CI run records, CodeQL/dependency/container scan results, deployed telemetry, cost guardrails, and observed restore drill |
 
 ### Immediate orchestration sequence
-1. Ask all five assurance agents for gap reports using *this repo*, including dates, links, human tasks and status; assign file-disjoint documentation work.
+1. ~~Ask all five assurance agents for gap reports~~ Done (substitute reports exist; see "Open reconciliation" in docs/RELEASE_GATES.md). Re-run them as the real agents only when asked.
 2. Continue safe local/synthetic-audio development in parallel, clearly separated from live classroom recording.
 3. Obtain Product Owner authorization before spending, external permissions, participant recruitment, real lecture audio, cloud deployment or production data actions.
 4. Do not mark a gate `PASS` from a plan, checklist, mock, AI synthesis or local dry run. Attach evidence and an accountable reviewer to each gate decision.
```

Apply with: `git apply` after saving the diff block, or by hand; the Lead should review before committing.
