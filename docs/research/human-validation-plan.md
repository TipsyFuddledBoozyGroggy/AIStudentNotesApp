# Tether Human Validation Plan

Status: PLANNING DRAFT, 2026-10-27. Authored by learning-science acting as substitute for human-research-coordinator (agent not loaded). No humans contacted. No studies run. No results exist.

Gate (CLAUDE.md "Explicit assurance gates"): ADHD suitability and learning-science effectiveness = **NOT HUMAN-VALIDATED**. Recommended register status: `BLOCKED` for any claim or real-student use; `NOT_RUN` for all stages below. This plan cannot move the gate to PASS.

## 1. Scope and sources
Sources read: CLAUDE.md (assurance gates), docs/ux/student-experience.md (Quiet default, no streaks/timers, Balanced chip max 1, test 24: 5 ADHD-identifying students, recruiting needs Product Owner approval), docs/PRODUCT_REQUIREMENTS.md (section 8 measures "to validate, not claimed", section 9 non-goals: no diagnosis or attention monitoring; section 10 assumptions: recap may reduce note-taking, 2/5/10 window unvalidated), docs/ai/live-lecture-ai-design.md (section 12 eval plan; mock CI evals, manual real-provider evals).
Feedback from this work is design evidence, not clinical evidence.

## 2. Stages
All stages require Product Owner written authorization before recruiting, spending, or using any real participant. Each stage has a go/no-go by a named human reviewer.

### Stage 0: Preparation (AI-assisted, no humans)
- Draft SESSION_SCRIPT, EXPERT_REVIEW_RUBRIC, VALIDATION_LOG templates (separate files; not created here).
- Build synthetic or licensed lecture fixtures only. No real classroom audio (recording gate BLOCKED).

### Stage 1: Expert review
Reviewers (placeholders, none engaged): clinician(s) with ADHD expertise; ADHD coach or disability-services specialist; learning scientist; accessibility specialist.
Review targets:
- UX: Quiet default, prompt/reminder copy, Quick recap flow, chip behavior, absence of pressure mechanics.
- AI-eval criteria: TL;DR faithfulness, Quick recap recap quality, clarification chip quality, quiz scoring, partial credit, mastery thresholds, spaced-review assumptions, "single miss is not a gap" rule.
- Harm/risk statements and the claims list in section 6.
Output: rubric-scored written reviews logged in VALIDATION_LOG; roadmap changes tracked for each adverse finding.

### Stage 2: Moderated usability sessions
- Participants: adult college students who choose to join; ADHD disclosure optional and never required or recorded unless separately consented. Include varied attention profiles and assistive-tech users.
- Material: synthetic or licensed lectures, or participant-consented material only. No third-party voices, no live classes.
- Tasks: start/pause/stop consented session, recover context after scripted interruption (2/5/10 min), find "what to do next", adjust help level, dismiss chips, deliberately export.
- Observation: time-on-task, errors, unprompted comments, post-task debrief. Short sessions with breaks; participant can stop anytime without losing compensation.
- Size: PRD target is at least 5 ADHD-identifying students; this is formative only, not statistical.

### Stage 3: Later pilot (not authorized; separate plan required)
Small, single-course, opt-in, only after recording/consent gate clears and ethics determination is in hand. Needs its own protocol, analysis plan, and stop criteria review.

## 3. Ethics and institutional approval (human determination required)
Do not assume exemption. A qualified human (IRB/ethics office or institution's designee) must decide for each stage:
- Whether Stage 1 (expert opinion) and Stage 2 (usability) count as human-subjects research or quality improvement; determination in writing.
- Stage 3 pilot: IRB/ethics review very likely required, plus institutional/instructor/disability-services permission and campus recording-policy clearance.
- Minors (dual enrollment): exclude until D8 age policy and additional review exist.
- Data protection: FERPA/GDPR applicability, retention, vendor (STT, Claude) data terms for participant material; legal-compliance-reviewer input.
- Affiliation: if the Product Owner has no institutional home, identify an independent review route. [PLACEHOLDER: reviewing body]

## 4. Recruitment, consent, compensation (all placeholders; nothing authorized)
- Recruitment channels: [PLACEHOLDER, Product Owner to approve]. No autonomous outreach; no targeting by presumed diagnosis.
- Eligibility: adult enrolled student; voluntary; no academic relationship (instructor/grader) with the researcher.
- Consent: plain-language form [PLACEHOLDER, reviewed by ethics body/counsel]; covers purpose, what is recorded (screen/audio of the session, if any), storage, deletion on request, right to withdraw, that Tether is unvalidated and not a treatment. Separate optional consent for ADHD disclosure and for recording the session.
- Compensation: [PLACEHOLDER amount/form; budget requires Product Owner approval]. Not conditional on completion or favorable feedback.
- Accommodations: format, breaks, captions, screen-reader support, remote/in-person options.
- Privacy: pseudonymous participant IDs; minimal identifying data; separate key storage; no raw transcripts in logs.

## 5. Harm monitoring and stop criteria
Possible harms: increased anxiety or shame, false confidence from wrong summaries, reduced active note-taking, distraction/overload, privacy exposure, over-reliance, recording of non-consenting others.
Monitoring: facilitator check-ins, post-session wellbeing item, incident log, named safety contact and referral resources [PLACEHOLDER].
Stop a session immediately if the participant requests it, shows distress, or any non-consented person/audio is captured.
Pause the stage and escalate to Product Owner and accountable reviewer if:
- any participant reports meaningful distress attributable to the product;
- a confirmed privacy or consent breach occurs;
- an AI output causes a participant to act on materially wrong information (e.g., wrong deadline or concept) that was presented with unwarranted confidence;
- two or more participants independently report pressure/shame from the interface;
- an expert reviewer flags a harm risk as unacceptable.
Resume only with written human sign-off and a recorded fix.

## 6. Claims the product MUST NOT make until validated
- No clinical, diagnostic, treatment, therapeutic, or "ADHD-proven/clinically validated" claims. Tether does not diagnose or screen.
- No effectiveness claims: improves grades, retention, focus, attention, executive function, or reduces ADHD symptoms.
- No "evidence-based for ADHD" wording; at most "designed with ADHD-related friction in mind (not yet validated)".
- No claim that summaries are accurate or complete without citations/uncertainty labels; no legal-compliance claims.
- No mastery/knowledge-gap claims from a single miss; AI-estimated grading is labeled as estimate, distinct from answer-key results.
- No testimonials or user quotes unless from real, consented participants.

## 7. Measures
Formative, descriptive only; no inferential claims at these sample sizes.
- Task success and assistance needed (Quick recap, next-action identification).
- Time-to-usable-recap and time-on-task (PRD target 5 s is system latency, measured separately).
- Perceived helpfulness (PRD target 80% helpful is unvalidated).
- Attention burden/cognitive load (e.g., short self-report, brief interview).
- Pressure/shame/autonomy perceptions of prompts and reminders.
- Trust calibration and false confidence: participant rating vs. TL;DR accuracy against source.
- Active note-taking behavior with and without recaps (tests PRD section 10 assumption).
- Accessibility defects found by AT users.
- Expert rubric scores (TL;DR accuracy, quiz remediation quality, scoring thresholds, spaced-review assumptions) with inter-rater notes.
- Adverse events and stop-criteria triggers.
Analysis: thematic coding by two coders where possible; adverse findings become dated roadmap items with owner, response, and status in VALIDATION_LOG.

## 8. Evidence checklist (all NOT_RUN)
| Item | Evidence required | Accountable reviewer | Status |
|---|---|---|---|
| Product Owner authorization to recruit/spend | Written approval with budget | [PLACEHOLDER: Product Owner] | NOT_RUN |
| Ethics/institutional determination per stage | Letter or protocol number | [PLACEHOLDER: ethics reviewer] | NOT_RUN |
| Consent form approved | Approved version and date | [PLACEHOLDER] | NOT_RUN |
| Clinician review of UX and harm statements | Signed rubric | [PLACEHOLDER: clinician] | NOT_RUN |
| Learning scientist review of quiz scoring/mastery/spacing | Signed rubric | [PLACEHOLDER: learning scientist] | NOT_RUN |
| Accessibility specialist review | Signed rubric | [PLACEHOLDER] | NOT_RUN |
| Synthetic fixtures prepared | Fixture list and licenses | [PLACEHOLDER: qa-engineer] | NOT_RUN |
| Usability sessions completed (n and dates) | VALIDATION_LOG entries | [PLACEHOLDER] | NOT_RUN |
| Harm log and stop-criteria review | Incident log reviewed | [PLACEHOLDER] | NOT_RUN |
| Findings triaged to roadmap | Linked backlog items | [PLACEHOLDER: Lead Architect] | NOT_RUN |
| Claims audit of UI/marketing copy against section 6 | Review record | [PLACEHOLDER] | NOT_RUN |
| Pilot plan (Stage 3) | Separate approved protocol | [PLACEHOLDER] | NOT_RUN |

## 9. Open items for Lead Architect
- Canonical filename: agent brief says HUMAN_VALIDATION_PLAN.md; this file is human-validation-plan.md per the request. Reconcile.
- SESSION_SCRIPT.md, EXPERT_REVIEW_RUBRIC.md, VALIDATION_LOG.md not yet created.
- Add this gate's evidence link to docs/RELEASE_GATES.md.
