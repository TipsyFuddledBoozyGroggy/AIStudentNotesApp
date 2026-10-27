# Release Gates (canonical register)

Per `CLAUDE.md`: a gate is `PASS` only with attached evidence and an accountable human reviewer. Plans, mocks, AI synthesis and local dry runs never clear a gate. Statuses: `BLOCKED`, `NOT_RUN`, `IN_PROGRESS`, `PASS`, `NOT_APPLICABLE` (rationale required).

**Last updated:** 2026-10-27. **Gates passed: 0 of 5.** No deployment, real classroom recording, or clinical/effectiveness claim is authorized.

| # | Gate | Status | Why | Evidence on file | Human-only tasks | Accountable reviewer |
|---|---|---|---|---|---|---|
| G1 | Classroom recording / consent / university policy | **BLOCKED** | No consent or recording control exists in code (SEC-006..018 are design only). Law, campus policy, FERPA applicability, third-party voices and minors are unreviewed. | `legal/recording-compliance-gap-report.md` (substitute author; not legal advice) | Counsel and campus review; accessibility-office and instructor permission; Product Owner decisions D1, D2, D8, D10; approved consent text | _unassigned_ |
| G2 | Vendor and Claude cost / limits selection | **UNVERIFIED** (no vendor chosen, nothing spent) | Design-doc vendor facts were wrong or unverifiable in places; pages were read through fetch summaries; latency is vendor claims only; FERPA/DPA terms unverified for all four vendors. | `vendors/verification-report.md` (substitute author) | Re-open official pages before quoting terms; confirm Anthropic tier, spend cap, retention/ZDR status; Azure quote; DPAs; measured authorized pilot | _unassigned_ |
| G3 | Screen-reader, real-device and classroom-audio QA | **NOT_RUN** (classroom audio part **BLOCKED** on G1) | Only automated checks exist: chromium Playwright + axe, 11 tests, M1 screens. No manual AT or device testing. | `qa/device-accessibility-plan.md` (substitute author); `src/web/e2e` | Human testers run the NVDA / keyboard / zoom / reduced-motion / forced-colors scripts; confirm available hardware (JAWS, Mac, iOS, Android) | _unassigned_ |
| G4 | ADHD suitability and learning-science effectiveness | **NOT_RUN** (claims and real-student use **BLOCKED**) | No human validation of any kind. | `research/human-validation-plan.md` (substitute author) | Product Owner authorizes Stage 1 expert review and reviewers; name the ethics/institutional body for Stage 2 | _unassigned_ |
| G5 | Production CI, scanning, observability, backups | **BLOCKED / NOT_VERIFIED** | CI first pushed 2026-10-27 and not yet confirmed passing; no CodeQL/Dependabot/image scan; no deployment, backup/restore drill, secret vault, telemetry or cost budgets; keys not persisted. | `ops/production-readiness-audit.md` (self-audit by devops substitute) | Review first GitHub Actions run; branch protection; restore drill; deployed-telemetry and cost-guardrail records | _unassigned_ |

## Known facts that change earlier docs (do not rely on the older numbers)
`vendors/verification-report.md` contradicts parts of `architecture/speech-pipeline.md` and `ai/live-lecture-ai-design.md` (cheapest STT vendor and prices; AssemblyAI bills idle open-socket time so pause must close the socket; Deepgram training opt-out is per request; Anthropic spend-cap 429s carry no `retry-after`; thinking cannot be disabled on Opus/Sonnet 5.5; Haiku 5.5 price jumps above 100K prompt tokens; prompt caching needs a >=512-token prefix). Those docs now carry a pointer; reconcile them before Milestone 2 implementation.

## Open reconciliation (when the real assurance agents are loaded)
Reports were written by substitute agents because the five new agents were not loaded in the session. Re-run them as the real agents and move/rename outputs to the paths their definitions name (`docs/compliance/*`, `docs/research/HUMAN_VALIDATION_PLAN.md` and companions, `docs/validation/*`). Substitute reports do not close any gate.

## Safe work while gates are open
Per `CLAUDE.md`: local builds, synthetic or licensed test audio, mock data, draft consent text, source-based vendor research, automated accessibility checks, CI authoring and dry runs. Not authorized: real classroom recording, spending, recruiting, deployment, clinical claims.

## Launch
Requires an explicit Product Owner go/no-go on this register and the documented outstanding risks.
