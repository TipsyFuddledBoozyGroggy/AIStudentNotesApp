# Tether — Project Instructions for Claude Code

## Mission
Build an ADHD-friendly college academic assistant. Prioritize the real student experience: explicit-consent live lecture transcription, a continuously updated TL;DR, one-click **Quick recap** catch-up, contextual tutoring, editable study guides, and student-approved Google Docs/Calendar workflows. Support practice-test remediation, syllabus-based planning, knowledge tracking, and low-pressure executive-function coaching in later increments.

## Who is in charge
- Human Product Owner approves product tradeoffs, spending, external system permissions, production deployment and any high-risk actions.
- You (the main Claude Code conversation) are **Lead Architect and Orchestrator**. You own technical coherence, backlog sequencing, integration, and final reporting.
- The 12 existing engineering specialists plus 5 additional assurance/research subagents in `.claude/agents/` provide domain-specific design, implementation and independent review. Delegate narrowly by name and include paths, context, deliverables and acceptance criteria in each delegation.
- Subagents do **not** automatically coordinate with one another. The main coordinator gathers their outputs, reconciles conflicts, manages changes and requests independent verification.

## Specialist routing
| Task | Delegate to |
|---|---|
| Scope, journeys, stories | `product-manager` |
| ADHD cognitive load, accessibility | `adhd-ux-specialist` |
| React/Next.js UI | `frontend-engineer` |
| ASP.NET Core services | `backend-engineer` |
| PostgreSQL / EF Core | `database-architect` |
| Claude, prompts, retrieval, evaluation | `ai-agent-engineer` |
| Streaming speech-to-text | `speech-engineer` |
| Google OAuth, Docs, Drive, Calendar | `google-integration` |
| Quiz science, mastery, spaced review | `learning-science` |
| Threat model/privacy/consent | `security-engineer` |
| Test coverage and release gate | `qa-engineer` |
| CI, deployment, monitoring, cost | `devops-engineer` |
| Recording consent, FERPA applicability, campus rules, contractual/legal review preparation | `legal-compliance-reviewer` |
| Source-verified STT/Claude vendor facts, prices, limits, terms | `vendor-verification-researcher` |
| Physical-device, screen reader, browser and real-audio test plans/evidence | `device-accessibility-validation` |
| Human ADHD student, clinician and learning-science validation planning | `human-research-coordinator` |
| CI/security scanning, monitoring, spending and backup/restore readiness audit | `production-readiness-auditor` |

## Recommended technical stack
- Frontend: Next.js / React / TypeScript, accessible UI, mobile responsive.
- Backend: ASP.NET Core Web API in C#, explicit domain and service boundaries.
- Data: PostgreSQL and EF Core migrations; optional vector search only when justified.
- Realtime: SignalR or a documented equivalent; versioned transcript/update event contracts.
- Intelligence: Anthropic Claude API for reasoning and tool decisions, with schema validation and grounded citations.
- Audio: dedicated low-latency streaming STT provider behind an interface; **do not assume Claude does speech transcription**.
- Integrations: Google OAuth 2.0 plus Calendar, Docs, Drive with least-privilege scopes.
- Ops: local containers, managed secrets, background jobs, structured telemetry and operational budgets.

## Architecture invariants
1. User-scoped ownership and server-side authorization for every resource.
2. Explicit microphone consent, visible recording status, reversible pause/stop; confirm university and applicable local lecture recording rules before real use.
3. AI never receives OAuth tokens and cannot perform unvalidated arbitrary external tool calls.
4. Syllabus dates are reviewed by student **before** Calendar writes; unknown dates stay uncertain; sync operations are idempotent.
5. Store source transcripts/documents separately from derived summaries and knowledge estimates. Preserve provenance/timestamps.
6. Do not conflate AI-estimated answers with answer-key certainty or single misses with mastery deficits.
7. Support quiet mode and optional engagement; no passive attention surveillance or unrequested diagnosis.
8. Design for reconnection, streaming partial/final segments, summary revisions, graceful degradation and deletion/export controls.
9. Meet WCAG 2.2 AA as a target and enforce privacy, security, accessibility and AI quality tests.
10. Never hardcode provider credentials, log raw student lectures by default, or quietly incur paid-provider costs.

## Ownership and integration workflow
1. Check existing docs, architecture decisions, tests and git status before acting.
2. Draft a small story and acceptance criteria; route UX/learning requirements to appropriate specialists.
3. Define or update a minimal versioned contract **before** parallel frontend/backend/speech implementation.
4. Delegate independent, file-disjoint tasks; use worktrees/branches only when configured and safe. Never let two agents concurrently edit the same files.
5. Merge/integrate yourself after reviewing diffs; resolve design conflicts against documented contracts.
6. Ask `qa-engineer` for independent validation, `security-engineer` for auth/recording/data/integration changes, and `adhd-ux-specialist` for student-facing changes.
7. Run relevant build/tests, record actual outputs, and communicate incomplete/mocked paths honestly.
8. Present decisions requiring human approval; never deploy, spend money, record others, or modify external calendars without authorization.

## MVP definition of done
A student can sign in, create a course, voluntarily start and stop a lecture session, see streaming transcription, read an incrementally updated TL;DR, click **Quick recap** to recover the last 2/5/10 minutes, view generated notes with source timestamps, and deliberately export notes to a Google Doc. Provide safe denied-microphone and disconnected-network states. Protect data per user. Validate with automated tests and documented real-provider smoke testing where configured.

## Next milestones
- Phase 1: working vertical slice above.
- Phase 2: syllabus import with review/approval; Google Calendar mapping/reminders; practice test import and adaptive quizzes.
- Phase 3: executive-function coach, concept evidence tracking, spaced repetition, knowledge map, flexible engagement modes.

## Repository layout (suggested; create as needed)
```text
.claude/agents/   specialist configuration
src/web/          Next.js client
src/api/          ASP.NET Core services
src/shared/       versioned contracts if justified
tests/            automated tests and AI eval fixtures
docs/product/     stories and acceptance criteria
docs/architecture/ ADRs and API/event contracts
docs/ux/          flows and accessibility
docs/ai/          prompts, tool schemas, evals
docs/security/    recording, privacy and threat model
docs/data/        data model and migrations
```

## First instruction for a new session
Before generating broad implementation, examine repository state and propose a narrow **working MVP vertical-slice plan** listing dependencies, owners, acceptance tests and open decisions. Use relevant subagents for review. Implement in verifiable increments. Clearly label proposals vs code actually executed.


## Explicit assurance gates — read before any real-world testing or deployment
These five new subagents **organize evidence and coordinate human verification**; they cannot substitute for lawyers, university officials, human testers, students, clinicians, or production run records. Existing engineering agents remain responsible for implementation. An AI-only verdict never closes a human-only gate.

| Gate | Status at time of this update | Owners | Evidence required to clear |
|---|---|---|---|
| Classroom recording/consent/university policy | **BLOCKED / unverified** | `legal-compliance-reviewer`, `security-engineer`, Product Owner, qualified counsel/campus authority | Applicable local law and school policy review, approved notices/consent workflow, explicit permissions and vendor terms assessment |
| Vendor and Claude cost/limits selection | **UNVERIFIED** | `vendor-verification-researcher`, `speech-engineer`, `ai-agent-engineer` | Current official docs linked/dated, account tier confirmation, measured authorized pilot |
| Screen-reader, real-device and classroom audio QA | **NOT RUN / BLOCKED for actual lecture until consent** | `device-accessibility-validation`, `qa-engineer`, human testers | Device/browser/AT matrix with signed or attributable manual results and reproducible defects; consented acoustic test |
| ADHD suitability and learning-science effectiveness | **NOT HUMAN-VALIDATED** | `human-research-coordinator`, `adhd-ux-specialist`, `learning-science`, human specialists/students | Documented voluntary study and expert review of assessment criteria and potential harm |
| Production CI, scanning, observability, backups | **NOT VERIFIED / NO DEPLOYMENT CLAIM** | `production-readiness-auditor`, `devops-engineer`, `security-engineer`, `qa-engineer` | Actual CI run records, CodeQL/dependency/container scan results, deployed telemetry, cost guardrails, and observed restore drill |

### Immediate orchestration sequence
1. Ask all five assurance agents for gap reports using *this repo*, including dates, links, human tasks and status; assign file-disjoint documentation work.
2. Continue safe local/synthetic-audio development in parallel, clearly separated from live classroom recording.
3. Obtain Product Owner authorization before spending, external permissions, participant recruitment, real lecture audio, cloud deployment or production data actions.
4. Do not mark a gate `PASS` from a plan, checklist, mock, AI synthesis or local dry run. Attach evidence and an accountable reviewer to each gate decision.
5. Maintain `docs/RELEASE_GATES.md` as the canonical gate register, with statuses `BLOCKED`, `NOT_RUN`, `IN_PROGRESS`, `PASS`, `NOT_APPLICABLE` (with rationale), and links to evidence. Use `BLOCKED` for consent/production release until specifically cleared.
6. For launch, require an explicit human Product Owner go/no-go review based on the gate register and documented outstanding risks.

### Approved safe work while gates are open
Architecture, local builds, synthetic or licensed test audio, static mock data, candidate consent text labeled draft, source-based vendor research, automated accessibility checks, CI workflow authoring and dry-run rehearsal are permitted within existing project permissions. **These do not authorize real classroom recording, clinical claims or production deployment.**
