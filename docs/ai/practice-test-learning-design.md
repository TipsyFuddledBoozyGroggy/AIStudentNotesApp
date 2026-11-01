# Practice-Test Import and Adaptive Quiz: Learning Design (v0.1, DESIGN ONLY)

Status: proposal for Lead Architect and Product Owner review. Nothing is implemented. All thresholds below are **starting hypotheses to be tuned by measurement and expert review**, not validated values. Nothing here is a claim of efficacy; see section 12 and gate G4 in `docs/RELEASE_GATES.md` (neurodivergent suitability and learning-science effectiveness: NOT_RUN, claims and real-student use BLOCKED).

Related: `docs/PRODUCT_REQUIREMENTS.md` (P2 practice tests and adaptive quizzes; P3 concept evidence, spaced review), `docs/ai/live-lecture-ai-design.md` (Claim/basis/citation model, untrusted-input handling, IAiClient, budgets, mock client), `CLAUDE.md` invariants 5, 6, 7.

## 1. Scope, assumptions, dependencies
- Scope: import of practice material, item model, concept tagging, evidence model, review scheduling, feedback, session design, remediation, metrics. Phase 2 delivers import + quizzes + basic evidence capture; the knowledge map and fuller spaced scheduling are Phase 3 but the data model here is designed to carry both.
- Assumptions: student uploads their own material; the app does not distribute or republish it; AI calls go through `IAiClient` (no tools, structured output, validation, ledger) as in the live-lecture design; lexical/FTS retrieval for linking to course material.
- Dependents: ai-agent-engineer (prompts, schemas, evals), database-architect (entities), backend-engineer (import pipeline), neurodivergent-ux-specialist (copy, pacing), security-engineer (upload and copyright/privacy), qa-engineer, human-research-coordinator (validation).
- Smallest verifiable first deliverable (later): import a pasted or typed set of questions with a student-entered key, run a 5-minute self-quiz with self-rated confidence, store attempts only. No AI estimation and no mastery labels in the first slice.

## 2. What a student imports
| Source | Examples | Key available? | Handling |
|---|---|---|---|
| Past exam / practice test (PDF, image, doc, paste) | Instructor-provided old exams | Sometimes (separate key page) | Parse items; match key if present; else no key |
| Textbook / problem-set questions | End-of-chapter, odd-numbered answers in back | Partial | Key per item, item-level presence flag |
| Student-authored questions or flashcards | Own cards | Student's own answer | Treated as student-provided answer, not a verified key |
| App-generated questions from the student's notes | From lecture notes, study guide | No external key; derived from cited sources | Labelled generated; answer grounded in cited source |

Rules:
- Import is student-initiated, per course, with a review screen before anything is saved ("Here is what we found. Fix anything that looks wrong."). Parse errors (merged questions, missing options, figures not captured) are shown, never silently guessed.
- Items needing a figure/diagram that was not captured are flagged `needsFigure` and excluded from automatic grading.
- Source files are stored separately from parsed items and derived estimates (invariant 5), with file hash, upload time, and student-declared origin. Uploaded text is untrusted data (same delimited-block defense as live-lecture design section 8); a test containing "ignore previous instructions" must not alter behavior.
- Academic integrity: the app is for practice and study. It must not be marketed or tuned to solve a live graded assessment. Item import records an optional student attestation "this is practice material, not a currently open graded assessment" (wording for legal/UX review). The tutor does not complete items the student marks as currently graded. Reproducing protected test content: store per user only, never share across users, never use as cross-user training or retrieval corpus; legal-compliance-reviewer to confirm copyright/ToS position (open question Q3).
- Do not quote long verbatim spans of imported items in AI-generated explanations beyond what the item display itself shows to its owner.

## 3. Item model
Core item fields (see entity draft, section 11):
- `stem` (question text), `format`, `options[]` (for choice formats, with stable ids), `figureRef?`, `sourceDocId + location` (page/question number), `points?`.
- Formats (priority order): multiple choice (single), true/false, multi-select, short answer (numeric or short text), fill-in-blank, worked problem with steps, essay/free response. Phase 2 auto-feedback: choice, T/F, numeric short answer. Short text and essay: self-check against model-answer points and rubric; AI feedback marked as an estimate; manual-review path for ambiguity (section 9).
- `difficulty`: coarse 1-3 (recall / application / multi-step transfer), set initially by AI from item content and revised from observed performance only after enough attempts (section 8). Never shown as a judgement of the student.
- `bloomLikeLevel` optional tag (remember/apply/analyze) for variety in practice. Marked as an AI estimate.

### 3.1 Answer provenance (invariant 6)
Every answer attached to an item carries an explicit `answerBasis` enum. These are different things and must never share a label or UI treatment:

| `answerBasis` | Meaning | Certainty shown to student |
|---|---|---|
| `answer_key_imported` | Key was in the uploaded material and student confirmed it matched the item | "Answer key (from your file)". Treated as authoritative for scoring, but see below |
| `student_entered` | Student typed the answer | "Your answer key" |
| `instructor_confirmed` | Student attests instructor gave this answer (optional) | "From your instructor (you told us)" |
| `ai_estimate` | Model's best answer, no key | "AI estimate, not verified" plus confidence band |
| `none` | No answer available | "No answer available yet" |

- Key reliability: an imported key can be misparsed or wrong (known errata). Each key answer carries `keyMatchConfidence` from parsing (item-to-key alignment). Low alignment means key is shown as "possible match, please check". A student can flag "this key looks wrong"; flagged items are excluded from evidence until resolved.
- AI estimate fields: `estimatedAnswer`, `estimateConfidence` (low/medium/high bands, not a percentage, since model-stated probabilities are not calibrated; calibration to be tested in evals), `rationale` as `Claim[]` with `basis` and citations to course material when available (`lecture` | `general_knowledge` | `uncertain`), `agreementChecks` (optional: two independent generations agree or not; disagreement forces `low`).
- Rules:
  - Items with `ai_estimate` or `none` are never auto-marked "correct/incorrect" in the way keyed items are. After a student attempt the screen says "Your answer matches / differs from the AI's estimate. The estimate may be wrong. Check your notes or ask your instructor." Result is stored as `consistentWithEstimate`, not `correct`.
  - Evidence weight of attempts on estimate-only items is much lower than keyed items (section 5) and a mismatch alone never produces a "needs review" signal unless the estimate is `high` confidence and grounded in cited course material, and even then it is a low-weight flag.
  - AI estimate never overwrites or is silently promoted to a key. Promotion requires student action ("I confirmed this answer") which changes the basis to `student_entered`.
  - Open-text essay scoring is always an estimate against rubric criteria.

## 4. Concept tagging with provenance
- Concept = a course-scoped, student-editable unit ("Le Chatelier's principle", "Bayes' theorem", not whole chapters). Concept inventory is built from syllabus topics, lecture notes/summaries, uploaded course documents, and imported tests (the practice test is converted into a **concept inventory** of what it covers, not a copy of its answers).
- Each item gets 1-3 `ItemConceptLink`s: `{conceptId, source: ai_proposed | student_confirmed | student_added, linkConfidence, evidence[]}`. Evidence is a list of references with provenance: `{kind: lecture_segment | note | doc_chunk | syllabus_topic, id, sessionId?/docId, timecode or page, retrievedAt}`. Links are proposed from lexical retrieval over the user's own material, validated server-side (ids exist, belong to the user and course), same as live-lecture design section 4.
- Unlinked or low-confidence tags are shown as "Not sure which topic this is" and stay out of mastery estimates until the student confirms or the concept is verified by repeated consistent tagging. Student can retag any item in one step.
- Prerequisites: optional `concept_edge(prereq -> concept)`, AI-proposed and student-editable. Used only to suggest an easier warm-up item, never to block access.
- Coverage view (benefit of inventory-first approach): "Your practice test touched 14 topics; 9 appear in your lecture notes; 5 do not (maybe not covered yet, or not captured)." Syllabus dates link topics to exam dates for prioritization (section 7).

## 5. Evidence model for mastery
Principle: the app keeps **evidence**, and derives a transparent, revisable **estimate** from it. Estimates are labelled estimates, show their ingredients, and are never framed as a trait or diagnosis.

### 5.1 What counts as evidence
An `EvidenceEvent` is created per scored interaction and stores raw facts, never just a summary:
- Attempt on a keyed item: correct / incorrect / partial (with rubric partial credit for multi-step or multi-select).
- Attempt on an estimate-only item: consistent / inconsistent with estimate (low weight).
- Self-rated confidence (before seeing feedback), recorded separately from correctness: e.g. "Not sure / Think so / Sure". Optional, one tap; never required.
- Response mode: free recall vs recognition (choice), hints used, "show answer" before attempt (not counted as an attempt), time on item, session timing context (timed/untimed).
- Source and spacing: how long since last exposure to the concept (lecture, previous attempt), which item variant.
- Student self-report: "I got this one wrong because I misread it" (excludes or down-weights the event), "key looks wrong", "I guessed".
- Not evidence: viewing an explanation, skipping, an item flagged with a bad key or missing figure, an attempt abandoned mid-session, or attempts during a student-declared interruption.

Weighting hypotheses (tune with data): free recall > recognition; keyed > estimate-only; untimed/relaxed > timed; first attempt after a delay > immediate repeat; same exact item reattempt counts less than a new item on the same concept (answer memorization is not concept mastery); hint-assisted success counts partially.

### 5.2 Minimum evidence before "needs review"
The system uses states, not scores shown as grades. States are per concept:

| State | Meaning | Minimum evidence (initial hypothesis, to be validated) |
|---|---|---|
| `no_evidence` | Nothing yet | 0 weighted events |
| `early_signal` | Something seen; not enough to say | 1-2 events or a single miss |
| `worth_a_look` | Suggestion to revisit, soft | at least 3 weighted events across at least 2 distinct items and at least 2 distinct days (or 2 sessions), with the majority incorrect/partial, **and** at least 2 of these on keyed items |
| `building` | Mixed or improving | mixed results, or recent trend positive |
| `looking_solid` | Consistent success | at least 3 weighted correct events across at least 2 distinct items, across at least 2 separate days, including at least one free-recall or delayed success |

Notes:
- The label shown to the student for `worth_a_look` is "Worth another look", not "weak", "failed" or "needs remediation". The internal enum name `needs_review` may exist in code but copy is the soft form. Product Owner sign-off on wording (Q5).
- "Looking solid" always shows "based on N questions over D days". The app never says "mastered". No percent-mastery number is displayed in Phase 2; if a number is later added it must come with its ingredients and an uncertainty indication.
- Estimator approach: start with a simple, explainable rule set (above) with exponentially decaying weights, not an opaque model. A Bayesian/knowledge-tracing-style model (named in the literature, for example Bayesian Knowledge Tracing and Elo-style item response models, **citation needs verification**) may be evaluated offline later against the rule baseline, only if it improves prediction on human-collected data and remains explainable.
- Every state transition stores `reasons[]` (which events) so "Why am I seeing this?" can be answered in one tap.

### 5.3 Single misses
A single incorrect answer: records an event, may give immediate neutral feedback and a retry/hint, schedules a normal low-priority resurfacing of that concept (the item returns later in the spaced queue), but changes the state at most to `early_signal`. It is never described to the student as a gap. Same for two misses on the same exact item within one session (could be one misconception or one misread).

### 5.4 Guessing
- Multiple-choice: chance-level success is weak evidence. Use confidence: a correct answer with "Not sure" gets a lower weight than "Sure"; a confident wrong answer is flagged as a **possible misconception** candidate (high-value feedback), still not a deficit by itself.
- Need repeated success on distinct items and at least one non-choice/free-recall success for `looking_solid`, to reduce the effect of lucky guesses.
- Student can tap "I guessed" after answering; stored as a flag.
- Avoid penalizing guessing in feedback ("No penalty for guessing here").

### 5.5 Time pressure
- Default quiz is untimed, with no visible countdown. Timed mode is opt-in, labelled "exam rehearsal", and only offered once the student has some prior untimed practice on the topic (suggestion, not a gate).
- Timed events are stored with `timed=true` and carry lower weight toward concept state (errors under time pressure may reflect speed or anxiety rather than knowledge); they feed a separate "pacing" view available only to the student, framed as practice for pacing, never as a deficit.
- Time on item is stored for scheduling hints only, never to infer attention or ability (no attention surveillance, invariant 7). Long idle time is not treated as an error.

### 5.6 No diagnosis
The app makes no statements about the student's neurodivergent, learning disability, anxiety, intelligence, or attention. It does not label the student ("careless", "weak at math"). Wording is about the material and the next step. Patterns like "many misses late in session" may only be offered as a neutral, student-opt-in observation ("Shorter sessions might feel better: want 5 minutes next time?") and never as a cause. Any suggestion of clinical relevance is out of scope; refer to the campus accessibility office for disability support only if the student asks.

### 5.7 Separate tracks, transparently
Correctness evidence, self-rated confidence, and calibration are stored and displayed separately. Calibration insight ("you felt sure on 4 you missed") is offered only after enough events (hypothesis: 10 or more confidence-rated events across at least 3 sessions) and as an optional, framed-as-useful reflection, not a score.

## 6. Retrieval practice and scheduling (low pressure)
Evidence-based principles to apply (established findings in the cognitive-science literature, **each marked as needing citation verification before any external claim**): retrieval practice (testing effect), spaced/distributed practice, interleaving, worked examples for novices with fading toward problem solving, feedback after attempts, desirable difficulty balanced against success experience. Specific effect sizes are deliberately not quoted here.

Design decisions:
- Retrieval before re-reading: the default is "try to recall first, then see the answer". The student can always choose "show me" without penalty.
- Worked examples first for new/low-evidence concepts: show a worked example, then a completion problem (fill the next step), then an independent problem (gradual scaffolding / fading). Offered when a concept is `early_signal`/`worth_a_look`, or when the student asks.
- Hints ladder (optional, student-requested): nudge → relevant lecture/note excerpt with timecode → worked step → answer. Using hints is allowed and never shamed.
- Scheduling policy (v0 hypothesis, simple and explainable):
  - Initial expanding intervals per concept after exposure: same day (short), next day, ~3 days, ~7 days, ~14 days, then longer. Intervals shorten after a miss (a miss moves the concept to a shorter step, not back to zero) and lengthen after delayed success. Exact interval ladder is tunable and unvalidated.
  - Exam-aware compression: if the syllabus (student-approved dates only; unknown dates stay uncertain) gives an exam date, cap intervals so the last review lands before it and raise priority for topics in scope with `worth_a_look`/`no_evidence` status. Without a date, use the normal ladder.
  - Priority ordering for a session: (1) due items on exam-scoped topics, (2) due items with `worth_a_look`, (3) due items consolidating `building`, (4) one "easy win" warm-up item (confident prior success) to start and one at the end where possible, (5) new material. Interleave 2-3 concepts per session rather than blocks of one, but keep blocked practice for brand-new concepts.
  - Never present an unbounded backlog. If many items are due, the app shows only today's chosen set; overdue items are quietly re-planned ("We've rearranged your reviews") with no "overdue" counters, red badges, or guilt copy. Missed days simply shift the plan.
  - Daily load cap (student-adjustable): default a small number of items; new-item cap separate from review cap.
- No streaks, no loss framing, no leaderboards, no punitive resets. Optional gentle progress: "You've looked at 12 questions this week" (opt-in), described as activity, not as requirement.
- Reminders: off by default; opt-in; student picks time and frequency; one reminder max per day; easy snooze, "skip this week" and global mute; reminders contain no performance information ("A 5-minute review is ready if you want it"). Calendar blocks are created only through the existing student-approved calendar workflow.

## 7. Alignment with syllabus and student preferences
- Syllabus topics and approved exam/quiz dates (invariant 4) feed priority only when approved; uncertain dates are excluded or shown as "date not confirmed".
- Student preferences (stored, editable, per course): session length, items per session, reminder opt-in and times, timed mode allowed, show confidence prompts, show hints by default, preferred formats, quiet mode, exam-countdown visibility off by default.
- Student can mark a topic "not on my exam" or "already know this" (self-declaration lowers priority; it is not mastery evidence).

## 8. Item difficulty and adaptation
- Initial difficulty from AI estimate (1-3). Observed difficulty adjusted only when an item has enough attempts across enough students, which does not apply in a single-user, per-user-content system. So per-item difficulty is **per-student adaptive**: adapt the next item based on recent success in this session (aim for varied difficulty with a high but not trivial success rate; target range is a hypothesis, to be set with experts).
- Adaptation rules: after two consecutive unsuccessful attempts in a session, offer an easier or scaffolded item or a worked example, plus the option to stop ("Want to switch to something easier, take a break, or stop here?"). After three consecutive successes, step up. No hidden lockouts.
- Adaptation is bounded and explainable ("Chosen because it's due" / "A step up from the last one"), and the student can always override (pick a topic, pick an item).

## 9. Grading, partial credit, rubrics, manual review
- Keyed choice/numeric: deterministic comparison server-side (units, tolerance, equivalent forms defined per item). The model does not grade keyed items.
- Multi-select: partial credit rule documented per item (e.g. proportion of correct choices selected with penalty for wrong selections, floor 0). Rule shown to student.
- Multi-step/worked problems: rubric of steps with partial credit per step; if keyed, final answer deterministic; step feedback is an AI estimate labelled as such.
- Short text / essay: rubric with criteria (content points, correct use of term, reasoning) and levels. Rubric sources: imported marking scheme if present (`rubric_imported`), else AI-drafted from cited course material, labelled `ai_drafted_rubric`. Student can edit the rubric. Scores are always `ai_estimated`; feedback quotes the student's own words against criteria.
- Manual review triggers (route to "self-check with model answer and checklist" instead of a verdict): low model confidence, two independent gradings disagree beyond one rubric level, rubric criteria not supported by cited sources, response contains non-answer or off-topic text, student disputes the grade, or the item is flagged ambiguous or has a questionable key. In these cases the student sees the checklist and can self-assess; the result then counts as `self_assessed` evidence, low weight.
- Dispute control: "I think this is right" is available on every AI-graded or estimate-mismatched result; it excludes the event from evidence pending student judgement and logs for eval.
- Rubrics and expert review cases are produced as fixtures (section 12).

## 10. Feedback wording, session design, neurodivergent-friendly pacing
### 10.1 Feedback principles
Specific, brief, about the task, forward-looking, neutral tone, no comparison with others, no praise-as-ability ("you're so smart") and no blame. Structure: (1) result in neutral words, (2) the key idea or the step where the answer diverges, (3) one next step. Explanations cite course material with timecode or page where available (`basis` label as in the live-lecture design).

Wording examples (draft; neurodivergent-ux-specialist to own final copy):
| Situation | Draft copy |
|---|---|
| Keyed correct | "That matches the answer key. The key idea: ..." |
| Keyed incorrect | "Not quite. The answer key says B. Here is the step where it can go sideways: ..." |
| Estimate-only match | "This matches our AI estimate. The estimate isn't verified, so worth a quick check in your notes." |
| Estimate-only mismatch | "This differs from our AI estimate. The estimate could be the one that's off. Want to check your notes [12:34] or mark which you trust?" |
| Confident wrong | "Plenty of people pick this one, it sounds right. Here is what separates them: ..." (no claim about 'plenty of people' without data: use "This is an easy one to mix up" only if backed by the misconception library; otherwise omit.) |
| Second miss | "Let's try a different angle. Want a worked example, a hint, or to move on?" |
| Stop | "Nice stopping point. Your progress is saved." |
Avoid: "wrong!", "you failed", "weak area", "you should have", "only", "still", "overdue", "streak broken", "behind", "again?".

### 10.2 Session design
- Default session: about 5 minutes, 4-6 items, a clear visible end ("5 of 5 done" or "stop any time"). Presets: 2 min, 5 min, 10 min; longer sessions are opt-in with a built-in break suggestion. No forced continuation; "one more?" is an optional choice with the default being finish.
- Quick re-entry: opening the quiz screen shows one primary button ("Start 5 minutes") that resumes where the student left off, restoring in-progress item and state; no setup required. Interrupted sessions are saved per answer; returning shows "Pick up where you left off" or "Start fresh".
- One question per screen, minimal chrome, large targets, keyboard accessible, no auto-advance timers, no countdown, no motion by default (reduced-motion respected), no sound. Progress shown as simple count, not a bar that implies being behind.
- Front-load a success: start with a warm-up retrievable item when possible.
- Pausing at any time is fine; no penalty. Skipping an item is fine and not evidence.
- Quiet mode (invariant 7): no proactive prompts, no reminders, no badges; quizzes only when the student opens them. Engagement is optional in all modes; the app does not infer attention or "disengagement" from behavior. Mode choices for notification density belong to the engagement mode picker (Phase 3; coordinate with neurodivergent-ux-specialist).
- Accessibility: WCAG 2.2 AA target; text alternatives for figures where they can be extracted; do not rely on colour for correct/incorrect; screen reader announcements for results; formulas readable (MathML/LaTeX alt text) to be scoped (open question).

## 11. Misconception remediation loop
1. Detect candidate: confident-wrong answer, or the same wrong distractor chosen across 2 or more distinct items on the concept, or a student-reported confusion. A single wrong choice yields at most a "possible" tag.
2. Match to a misconception tag: distractors in imported items are mapped (AI-proposed, student-visible) to a short, course-scoped misconception statement ("Confuses precision with accuracy"). Tags are hypotheses, marked `ai_proposed`, revised by evidence; a library of known misconceptions per subject is an open question (curated vs generated).
3. Feedback: contrast the misconception with the correct idea, with a cited course source and a brief worked example.
4. Check: a new item (different surface features) that discriminates the misconception from the correct idea, offered soon (same session if student agrees, else next day).
5. Delay check: a later spaced item on the same concept; a delayed correct response counts toward `looking_solid`.
6. Close: if the misconception tag shows no recurrence over 2 or more spaced checks, it moves to `not_recently_seen`; it is not deleted from history.
7. Escalation: if it persists after 2 remediation cycles, suggest human help (office hours/tutor/instructor), offer a pre-written question to ask (as in live-lecture clarification suggestions). No clinical statements.
Misconception content must be grounded or flagged `general_knowledge`/`uncertain`.

## 12. Metrics and validation (NOT claims of efficacy)
**All metrics below are instrumentation and quality checks for the product. None demonstrates that the app improves learning, exam scores, or outcomes for neurodivergent students.** No such claim may appear in the product, marketing, or reports until the human-research gate (G4 in `docs/RELEASE_GATES.md`, currently NOT_RUN) is cleared by human-run, ethically reviewed validation per the human-research-coordinator plan (`research/human-validation-plan.md` is a substitute-author draft that closes nothing).

### 12.1 Product and safety metrics (descriptive only)
- Sessions started/completed, median session length, voluntary stop rate, return after a gap (reported as descriptive, not a success target; avoid optimizing for time-in-app).
- Opt-out rates: reminders disabled, quiet mode used, sessions shortened. Rising opt-outs signal harm/annoyance.
- Student-reported helpfulness and stress (optional one-tap, rarely shown).
- Disputes and "key looks wrong" rate; per-feature.
- Accessibility defects found in manual testing (gate G3).

### 12.2 Technical quality metrics (AI evals, section 13)
Key parsing accuracy, answer-key alignment accuracy, AI-estimate accuracy by confidence band (calibration), erroneous-feedback rate, tag precision/recall vs expert labels, grading agreement with expert graders on rubric items, groundedness/citation support, injection resistance, wrong-credit and wrong-"needs review" rates.

### 12.3 Learning-validity questions for the human study (to be designed by humans)
Do estimated states predict later performance on unseen items? Do delays, confidence ratings and weighting improve prediction? Are `worth_a_look` suggestions perceived as accurate and not stigmatizing? Do students feel more or less anxious? Comparisons against reasonable alternatives (e.g. self-study without the app) require a proper study design, power analysis and ethics review by qualified humans; this document sets no numeric targets.

## 13. Evaluation sets (to build under `tests/ai-evals/`, synthetic or consented/redacted only)
1. **Parsing/alignment set**: messy exams (two-column, merged items, missing options, figures, key on separate page, key with errata, mismatched numbering). Gold: items, key alignment.
2. **Answer-estimate set**: items with known answers, plus ambiguous and flawed items (no correct answer, two correct, outdated convention). Metric: accuracy by confidence band; must abstain/mark low on flawed items; never emit certainty language.
3. **Provenance-integrity set**: tests that check estimates are never labelled as key (UI/data assertions), estimate-only mismatch never produces "needs review" alone.
4. **Erroneous-feedback set**: student answers that are correct but in different form (equivalent fractions, units, synonyms), almost-correct, partially correct, off-topic; check no false "incorrect", no fabricated explanation, no wrong step blamed. Includes poisoned keys to test dispute flow.
5. **Remediation-quality set**: cases with confident-wrong and distractor patterns; expert-rated criteria: correct diagnosis of the idea (not the person), grounded in course material, appropriate scaffolding, non-shaming tone, brevity, actionable next step, new discriminating item truly different in surface form.
6. **Tone/shame lint set**: list of forbidden phrasing and patterns; automated check plus human review samples.
7. **Evidence-model simulation set (deterministic)**: scripted event sequences with expected state transitions: single miss, 2 misses same item, guess correct, timed misses, repeated items, key-flagged items, long gaps, backlog after absence. Pass when the rules in 5.2-5.5 are obeyed exactly.
8. **Scheduling simulation**: backlog collapse, exam in 3 days with 80 due items, missed week, no exam date, uncertain date; assert bounded session size, no "overdue" copy, last review before approved exam.
9. **Essay grading set**: expert-graded responses with rubric levels, adversarial answers (rubric keyword stuffing, copied question, prompt injection inside the answer); metrics: agreement, manual-review trigger precision, injection resistance.
10. **Academic integrity set**: attempts to use the tutor to answer a flagged live graded assessment; expected behavior per the policy decided in Q4.

Expert review: a small panel (instructors/learning scientists; plus students and a neurodivergent-informed clinician reviewer for tone) rates a sample of outputs per release on a published rubric; inter-rater agreement recorded. Reviewer availability and Product Owner authorization are human tasks (gate G4).

## 14. Draft data entities (proposal for database-architect; not a schema)
All user-scoped (`userId`, `courseId`), with server-side authorization on every query.
- `PracticeSource`: id, userId, courseId, kind (past_exam | textbook | student_authored | generated), fileRef/hash, declaredOrigin, integrityAttestation, importedAt, parseStatus, deletedAt.
- `PracticeItem`: id, sourceId, ordinal/location, stem, format, options[] (id, text), figureRef, needsFigure, difficulty (+ difficultyBasis), points, parseConfidence, flags (ambiguous, keyDisputed), supersedesItemId.
- `ItemAnswer`: id, itemId, answerBasis (enum, section 3.1), answerValue, keyMatchConfidence, estimateConfidenceBand, rationaleClaims (Claim[] with citations), promptVersion/model/mock flag, createdAt, studentConfirmedAt?.
- `Rubric` / `RubricCriterion`: id, itemId, origin (imported | ai_drafted | student_edited), criteria[] (description, levels, points), version.
- `Concept`: id, userId, courseId, name, description, origin, mergedInto?, archivedAt.
- `ConceptEdge`: prereqConceptId, conceptId, origin.
- `ItemConceptLink`: itemId, conceptId, origin (ai_proposed | student_confirmed | student_added), linkConfidence, evidenceRefs[] (kind, id, locator, retrievedAt).
- `Misconception`: id, courseId, conceptId, statement, origin, status.
- `DistractorMisconceptLink`: itemId, optionId, misconceptionId, origin, confidence.
- `QuizSession`: id, userId, courseId, startedAt, endedAt, plannedItems, mode (relaxed | timed), plannedMinutes, endReason (finished | stopped | interrupted), preferencesSnapshot.
- `Attempt`: id, sessionId, itemId, responseValue, startedAt, submittedAt, hintsUsed, shownAnswerFirst, timed, selfConfidence (nullable), guessedFlag, scoreResult (correct | partial | incorrect | consistent_with_estimate | inconsistent_with_estimate | self_assessed | ungraded), scoreBasis (keyed | estimate | rubric_ai | self), credit (0-1), disputed.
- `EvidenceEvent`: id, userId, conceptId, attemptId, weight, weightInputs (jsonb: mode, timed, basis, spacing, novelty), excluded (bool + reason), createdAt. Append-only.
- `ConceptEstimate`: id, userId, conceptId, state (enum 5.2), computedAt, algorithmVersion, evidenceCount, distinctItems, distinctDays, reasons[] (event ids). Derived and recomputable from events; stored separately from source material (invariant 5).
- `ReviewSchedule`: id, userId, conceptId or itemId, dueAt, intervalStep, scheduleVersion, examAnchorDate?, lastOutcome, snoozedUntil, suppressed.
- `StudyPreferences`: userId, courseId?, sessionMinutes, itemsPerSession, timedAllowed, confidencePromptsOn, remindersOptIn, reminderTimes, quietMode, dailyCap.
- `FeedbackRecord`: attemptId, text-template id/version, citationRefs, promptVersion, model, reportedProblem.
- Deletion/export: per-source and per-course deletion cascades to items, answers, evidence and estimates; export of attempts and estimates as student-readable file. AI ledger rows (no content) follow the existing `ai_usage` design.
- Logging: no raw item text or student responses in logs by default.

## 15. Privacy, security, failure paths
- Uploaded tests may contain copyrighted or instructor-confidential content; user-scoped only, deletable, not used for cross-user features, not sent to providers beyond the needed calls; security-engineer to review vendor retention terms before real use (links to gate G2 and compliance review).
- Student responses and confidence ratings are sensitive learning data; excluded from logs; minimal retention options.
- AI unavailable or budget limited: keyed items still work (deterministic grading); estimates and rubric feedback show "AI feedback unavailable right now"; scheduling is rule-based and does not depend on AI. Importing falls back to manual entry/paste.
- Parse failure: nothing saved until the student reviews; partial imports allowed.
- Data integrity: evidence events are append-only; estimates are recomputable; algorithm version recorded so changes do not silently rewrite history.

## 16. Open questions for the Product Owner
1. Scope of first slice: keyed-only quizzes with manual/paste import first (my recommendation), or include PDF/image parsing and AI estimates at launch?
2. Is AI-estimated answering acceptable at all in Phase 2, given the trust risk, or should estimate-only items be restricted to "check against your notes" without showing a verdict?
3. Copyright/ToS: are students permitted to upload instructor exams and textbook questions for private study? Needs `legal-compliance-reviewer` input; any per-user-only storage constraints?
4. Academic integrity policy: how should the app treat items the student marks as part of a currently open graded assessment (refuse tutor help, warn, or only attestation)?
5. Wording approval for state labels ("Worth another look", "Looking solid"), and whether any numeric or visual mastery indicator may be shown in Phase 2.
6. Timed "exam rehearsal" mode: include in Phase 2, or defer?
7. Reminder policy: reminders off by default and opt-in only (recommended); are push/email channels in scope, and who pays for them?
8. Default session length and daily cap (proposal: 5 minutes, 4-6 items); acceptable to make these student-adjustable with no minimums?
9. Misconception library: curated by experts per subject (cost, time), AI-generated only with student-visible hypothesis labels, or deferred?
10. Math/science content: formulas, figures and handwritten work are in scope? (affects parsing cost and accessibility)
11. Budget: approval of per-user AI cost caps for import parsing, estimates and feedback (see budgets in live-lecture design); who approves model tier for grading?
12. Human validation: who authorizes the Stage 1 expert review and the student/clinician participants, and which institutional ethics body applies (gate G4 owner is currently unassigned)?
13. Should data from a student's quizzes ever be visible to anyone else (instructor, parent, accessibility office)? Recommended default: never, without explicit per-case student consent.

## 17. Citations needing verification
Not cited as fact in this document; to be confirmed with real sources before external use: testing effect/retrieval practice; spacing effect and expanding intervals (the evidence on expanding vs equal spacing is mixed to my recollection, **verify**); interleaving; worked-example and fading effects (cognitive load theory, Sweller and colleagues); feedback timing and content (Hattie and Timperley; Black and Wiliam); hypercorrection effect for high-confidence errors; Bayesian Knowledge Tracing (Corbett and Anderson); item response theory/Elo rating for education. Effect sizes are intentionally omitted. Evidence specific to neurodivergent populations is not asserted here and requires a literature review by the human-research track.

## 18. Not done
No code, prompts, schemas, thresholds validated, eval fixtures, or provider calls exist. Thresholds in sections 5, 6 and 8 are untested hypotheses. No human review has occurred; gate G4 remains NOT_RUN.
