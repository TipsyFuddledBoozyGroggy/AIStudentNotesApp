# Phase 2 Uploads and Vendors: Counsel-Review Brief

**Status: DRAFT issue-spotting brief. NOT LEGAL ADVICE. NOT LEGALLY APPROVED. Does not clear any gate.**
Author: legal-compliance-reviewer (AI agent; not a lawyer). Date: 2026-10-28.
Gates touched: G1 (consent/policy/privacy) and G2 (vendor terms). Both stay as recorded in `docs/RELEASE_GATES.md` (G1 BLOCKED, G2 UNVERIFIED). This file changes no gate status.

Purpose: give qualified counsel, campus authorities and the Product Owner an organized list of questions about Phase 2 (syllabus import, practice-test import) before any real student document is uploaded or any real model vendor is called. Every statement below about what the law or a vendor "says" is either (a) a fact quoted from a repo document, with its path, or (b) a question or hypothesis marked **VERIFY**. No statute, case or vendor term is asserted as settled. Legal citations appear only where I am reasonably confident of the name, and are still marked VERIFY.

## 0. Inputs read and what they say

| Source (repo path) | Relevant content |
|---|---|
| `CLAUDE.md` | Five gates; recording/consent gate BLOCKED; AI-only verdicts never close human gates; safe work = synthetic/licensed data, draft text, source-based research. |
| `docs/RELEASE_GATES.md` (updated 2026-10-27) | 0 of 5 gates passed. G1 BLOCKED, G2 UNVERIFIED. Records FERPA/DPA terms unverified for all four STT vendors. |
| `docs/legal/recording-compliance-gap-report.md` | Prior substitute-author gap report (recording focus). Lists FERPA, minors, copyright and vendor questions as unverified. Wording unapproved. |
| `docs/product/phase2-syllabus-import.md` | P2-D4 (retention of original file), P2-D5 (upload notice and attestation), P2-D13 (pilot on synthetic only vs real syllabi), P2-D14 (minimum age, PO-7). AC4 of upload story: one-sentence notice before upload, no pre-ticked box, "DRAFT pending legal". Section 12 items 1, 2, 5. S10 AC5: deletion does not reach copies already at the AI provider. Limits: 10 MB / 60 pages proposed. |
| `docs/ai/syllabus-extraction-design.md` | Section 1 and open question 3: syllabi are "usually not student education records" but carry instructor names, emails, phones and may be copyrighted; whether sending to a model vendor is acceptable is open. No OCR and no native Claude PDF input in v1. Hidden content dropped before forwarding. No logging of content. Fixtures synthetic or licensed. `AiProviderGuard` refuses any provider except `mock`. |
| `docs/ai/practice-test-learning-design.md` | Section 4 (academic integrity attestation "practice material, not a currently open graded assessment"; tutor does not complete items marked graded; per-user-only storage, no cross-user retrieval or training); section 15 (uploaded tests may be copyrighted or instructor-confidential); open questions 3 and 4 in section 16. |
| `docs/vendors/verification-report.md` | Anthropic retention statements from two official pages conflict (30 days vs "not retained by default except Covered Models"); ZDR needs Anthropic approval; DPA text not read; `inference_geo` default global, US-only is 1.1x; fetches were summary-level so pages must be re-opened before quoting. No parsing library/service evaluated. |
| `docs/DECISIONS.md` | PO-4 (Claude key/budgets), PO-5 (retention defaults), PO-7 (minimum age D8; hosting region and pilot scope D10), PO-2 (recording-rules policy). |

Observation for the Lead Architect: PO-7 is described in `DECISIONS.md` as minimum age plus hosting region/pilot scope; the vendor report uses PO-7 for hosting region only. Both meanings apply to this brief.

Current code reality (per the design docs, not re-audited here): Phase 2 is design/contract/fixture only; mock extractor only; no real vendor call possible; no real student document should be in the system.

## 1. Copyright and licensing position (questions, not conclusions)

### 1.1 Facts and assumptions
- Documents in scope: (a) syllabi, usually instructor-authored, sometimes institution-templated; (b) past exams and instructor-written practice tests, possibly confidential or reused across terms; (c) textbook end-of-chapter and problem-set questions, authored by publishers/authors.
- The student uploads; Tether stores per user, does not republish, and sends text to a model vendor to extract dates (syllabus) or to build concept inventory, items and possibly AI-estimated answers (practice tests).
- Assumption (unverified): Tether is operated by a party that is not the school.

### 1.2 Issues to put to counsel (all VERIFY)
1. **Who owns what.** Instructor, institution (campus IP policy / work-for-hire norms), and publisher may each hold rights in these documents. Which of them can restrict a student from copying the document into a third-party service?
2. **Copying into Tether is reproduction; sending to a vendor is a further copy/transmission.** Is each step a separate rights question? Does it matter that the student, not Tether, initiates it? Is Tether exposed to secondary or contributory theories as the tool provider (VERIFY; relevant US copyright concepts include reproduction rights, fair use under 17 U.S.C. 107 and safe-harbor notice-and-takedown under 17 U.S.C. 512; counsel to confirm which, if any, are applicable to this product and operator).
3. **Fair use / private study.** Can a personal, non-redistributed study use of a syllabus or a handful of textbook questions be treated as a favorable fair-use case, and does AI processing by a vendor change that? Counsel's view needed; I do not assume the answer. Factors that the design already supports: per-user storage only, no cross-user retrieval or training corpus, deletion available. Factors that cut the other way (to be assessed): whole-document copying (exams), commercial operator, effect on market for publisher problem banks.
4. **Syllabus vs exam vs textbook risk tiers.** Hypothesis for counsel: syllabi are lowest risk (factual schedule data, widely distributed to enrolled students), instructor exams highest (confidentiality, reuse), textbook questions in between (publisher rights). Counsel to confirm or reject this tiering, because it would justify different product treatment (for example, syllabus import first, exam/textbook import later or text-paste only).
5. **Contract and license overlays.** Course-site terms, LMS terms, publisher e-text licenses and the syllabus itself may prohibit copying or uploading to third-party or AI services. The syllabus-extraction design notes some instructors prohibit uploading to AI services. How should the product handle a document that carries a restriction notice (detect, warn, ignore)? Is detection feasible or should the attestation carry the burden?
6. **Derived outputs.** Extracted dates and concept lists are mostly facts or student-edited data; generated question variants or AI-estimated answers are closer to derivative content. Which derived outputs can be stored, exported (CSV/ICS/JSON) or shown later without reproducing protected text? The design stores `sourceSnippet` verbatim: how long may snippets be before they become a concern?
7. **Exports and original-file return.** S10 AC4 returns the original upload on request to the same student. Counsel to confirm this is low risk or constrain it.
8. **Terms of service allocation.** Should Tether's ToS state that the student represents they have the right to upload and indemnifies, and is that enforceable and appropriate for a student audience (VERIFY; also see attestation options in section 5)?
9. **Takedown handling.** If an instructor or publisher complains, what is the intake path and can Tether delete a specific document across user accounts via content hash (privacy tension with per-user isolation)? Needs a designed procedure and counsel review of whether a registered designated agent is needed (VERIFY).

### 1.3 Position for now
No position is taken that uploading or vendor processing of any third-party-authored document is permitted. Until counsel responds, treat all real instructor/publisher documents as not cleared. Only synthetic, student-authored-by-the-tester, or expressly licensed/permissioned documents are allowed in tests (see section 7.3).

## 2. FERPA applicability (question set)

Statement of method: I do not claim FERPA does or does not apply to the school, to Tether, or to any record. Reference points to verify with counsel: FERPA, 20 U.S.C. 1232g; implementing regulations, 34 CFR Part 99 (VERIFY current text, especially the definitions of "education records," the school-official exception in 34 CFR 99.31(a)(1) and the rights-transfer rule at the postsecondary level; confirm exact subsections yourself). Also consult the US Department of Education's student-privacy guidance on contractors and online services (VERIFY current documents and dates).

### 2.1 Record classes
| Class | Example | Preliminary questions |
|---|---|---|
| A. Instructor-distributed course document | Syllabus, assignment sheet as handed to all students | Is it an education record at all (it is not "directly related to" a particular student in the ordinary case)? Does it become one when held by a student's account? Does it contain personal data of the instructor (name, email, phone, office hours) that has separate privacy treatment? |
| B. Student-annotated or student-personalized version | Syllabus with student notes, accommodation notes, or a graded exam with the student's answers and marks | Likely "directly related to a student." Is it maintained by the institution (FERPA concept) once the student uploads it to a third-party tool? Counsel to decide how FERPA treats a record the student themselves places with a vendor. |
| C. Graded or returned work, grade policies containing individual grades | Returned midterm with score; screenshot of gradebook | Same questions as B; higher sensitivity. Consider excluding by design (reject/strip) in Phase 2. |
| D. Instructor exams, textbook questions | Confidential or published assessment content | Mainly a copyright/integrity issue (sections 1, 3), but may contain no student PII. Confirm FERPA is not the controlling frame. |
| E. Derived data | Student's attempts, confidence ratings, mastery estimates (practice-test design) | These are learning records generated in Tether, not institution records. Counsel to say whether FERPA is relevant at all, and which state student-privacy laws or general privacy statutes are (VERIFY by jurisdiction). |

### 2.2 Core threshold questions for counsel
1. Does FERPA reach Tether at all? FERPA obligations generally attach to educational agencies and institutions receiving covered federal funds. If Tether is not acting for the school and the student voluntarily uploads their own copies, is FERPA inapplicable to Tether's handling, with privacy governed by other law and by contract/policy? (Hypothesis only. Counsel to confirm.)
2. If a school adopts Tether (campus pilot, accessibility office referral, institutional license), does that change the answer? Would the school then need to treat Tether as a "school official" with a "legitimate educational interest" under direct control, using records only for authorized purposes, per its annual notification? What contract terms would the school require (use limitation, no redisclosure, deletion on request, breach notice, audit)? (VERIFY the exact conditions.)
3. Does Tether's use of Anthropic (and any parsing service) as a subprocessor fit within any such arrangement, and would the school need to approve subprocessors by name?
4. Does a student's own act of uploading change who is "disclosing"? Can a student consent to disclosure of their own education records to a vendor (FERPA consent provisions, VERIFY) and what form of consent is valid (written, signed, dated, identifying records and recipient)? Is an in-product click sufficient? Does this matter if FERPA does not apply to Tether?
5. Directory information, student ID numbers or emails embedded in uploaded documents: policy to redact, reject or accept?
6. Parental access: postsecondary rights generally rest with the student; for under-18 students or dual-enrollment/high-school contexts the answer may differ (see section 6). Counsel to state rule per pilot setting.
7. Does the institution consider student-side AI tools "third parties" it can restrict by policy for coursework? This is a policy question (section 3 and checklist), not necessarily a FERPA one.

### 2.3 Design defaults that reduce exposure regardless of answer (proposals, not compliance)
- Reject or warn on documents that appear to contain grades or student identifiers in Phase 2; do not ask for them.
- No admin access to document content (already a design statement).
- Minimum necessary text to vendor: extracted text only, hidden content and metadata dropped (already in syllabus design); no student name/email in the prompt.

## 3. Academic integrity and institutional policy risks for practice-test import

### 3.1 Risks to put to counsel and campus authorities
1. **Currently open graded assessments.** A student could upload a live take-home exam or online quiz and use the tutor to answer it. Design today: optional attestation "this is practice material, not a currently open graded assessment"; tutor does not complete items the student marks as graded. Self-attestation is bypassable. Questions: is attestation sufficient as a reasonable control, what is Tether's exposure if it facilitates cheating, and would the school expect more (for example blocking import of anything resembling LMS exam pages, rate limits, honor-code notice)?
2. **Detecting a live assessment.** Technically unreliable. Should the product avoid any promise of detection and say so, to avoid creating a false sense of control?
3. **Instructor exam reuse.** Past exams may be re-used by the instructor. Uploading, sharing or studying from them may violate a course or honor-code rule even if the file is lawfully in the student's hands. Per-user isolation helps but does not remove the issue.
4. **Institutional AI-use policies.** Many institutions and individual instructors have course-level AI rules (VERIFY for each pilot campus; the checklist asks for them). The product's presence in a course where AI is banned, and any "AI-estimated answer" feature, may conflict. Does the product need a per-course AI-allowed acknowledgement?
5. **Accommodation and disclosure implications** if the product is used by students with disability accommodations: tool use inside an exam is typically governed by the accessibility office, not the product.
6. **Marketing claims.** The design states the app must not be marketed or tuned to solve a live graded assessment. Counsel to review all marketing, store listings and onboarding copy for implied permission.
7. **Estimated vs keyed answers.** Mislabeling AI-estimated answers as authoritative could mislead a student toward wrong answers on real work. Already handled by provenance labels (`practice-test-learning-design.md` section 4). Counsel to say whether a disclaimer is also needed (accuracy/consumer-protection angle, VERIFY).
8. **Evidence retention for integrity disputes.** If a school investigates, what might it request from Tether (attestations, upload timestamps, hashes)? Is the product allowed to retain integrity attestations after deletion of content (content-free audit metadata is the design default)? Counsel to confirm minimal retention.

### 3.2 Product treatments for counsel to compare (none adopted)
| Option | Description | Trade-off |
|---|---|---|
| T1 Attestation only | Optional checkbox/statement at import | Lowest friction; weak control; legal value limited |
| T2 Attestation + tutor refusal on flagged items | As in current design | Protects the honest path; relies on honest flag |
| T3 Keyed-only quizzes from student-pasted/typed items first | Phase 2 limited to items the student enters; no file import of exams | Lowest copyright/integrity exposure; less convenience (matches recommendation 1 in practice-test design section 16) |
| T4 Per-course "AI permitted" acknowledgement | Student states course policy permits AI study tools | Surfaces policy; adds friction; still self-report |
| T5 Block exam-like import entirely until campus approval | Only syllabus import in pilot | Safest; defers practice-test value |

## 4. Vendor terms to verify (questions for `vendor-verification-researcher`)

Scope: facts only. I assert none of the answers. Each row names primary sources to open; per `vendors/verification-report.md`, earlier fetches were summary-level, so each page must be re-read in full, with URL, version/effective date and check date recorded. Contract-bearing answers (DPA text, ZDR approval) need Product Owner and counsel, not just research.

### 4.1 Anthropic API (Claude)
| # | Question | Primary sources to check | Notes |
|---|---|---|---|
| A1 | Default retention of API inputs/outputs for the exact models Tether will use (Fast `claude-haiku-5-5`, Balanced `claude-sonnet-5-5`), including what "Covered Models" means and whether they apply. | `platform.claude.com/docs/en/manage-claude/api-and-data-retention`; `privacy.claude.com/en/articles/7996866-how-long-do-you-store-my-organization-s-data` | The report found these two pages in apparent conflict (30 days vs not retained by default). Needs reconciliation and, ideally, written confirmation from Anthropic for the account. |
| A2 | Retention extensions: usage-policy enforcement, legal requirements, trust and safety review; how long and who can see content. | Same pages; Anthropic usage policy and commercial terms | Relevant to student documents containing sensitive content. |
| A3 | Is zero data retention available to this account type, what approval is needed, which APIs/features are covered (Messages, structured outputs, Files API, PDF input, batch)? | `privacy.claude.com/en/articles/8956058-...`; Anthropic Sales | Report says approval-based and per organization. Cost/lead time to PO. |
| A4 | Is customer content used to train models (including via feedback programs or opt-ins) under the commercial terms applicable to API use? | `anthropic.com/legal/commercial-terms` (current version and effective date); privacy center | Report cites Section B language; confirm the signed/accepted terms version. |
| A5 | DPA: existence, current text, subprocessor list and change-notice mechanism, breach notification time, audit rights, deletion on termination, standard contractual clauses if relevant. | Anthropic legal/trust center pages (DPA, subprocessors list) | DPA text was not read in the report. |
| A6 | Does Anthropic offer education-specific terms, FERPA-oriented commitments or a school-official-style addendum? If not, what does it say about use with student records? | Anthropic trust center, commercial terms, education pages | Do not assume. |
| A7 | Data location: workspace geography (at rest) and `inference_geo` options and cost multiplier; applicable to each model; whether any subprocessors are outside the US. | `platform.claude.com/docs/en/manage-claude/data-residency` | Region 1.1x cost noted in report; PO-7 decision. |
| A8 | Files API and PDF input retention differ from plain text? | Files API and PDF docs | Design does not use them in v1, but verify before any future change. |
| A9 | Acceptable-use restrictions relevant to education, minors, or processing of third-party copyrighted content; whether users under 18 may be served via an operator app. | Anthropic usage policy; commercial terms; age requirements | Relevant to section 6. |
| A10 | Deletion: can customer content be deleted on request before the default period; what does the console or API provide; is there a deletion SLA? | Privacy center; support/Sales | Ties to S10 AC5 (copies at provider). |
| A11 | If routed via a cloud partner (Bedrock, Vertex, Foundry) because a campus has that agreement, the partner's retention/region/terms. | Each partner's docs | Report says not evaluated. |
| A12 | Indemnification or IP terms offered for outputs and customer content, if any. | Commercial terms | Relevant to section 1. |

### 4.2 Document-parsing library or service
No parser has been chosen or evaluated in the repo (extraction is described as a local, resource-limited worker; OCR and native PDF input are deferred). Questions apply to whichever candidate emerges:
| # | Question | Sources to check |
|---|---|---|
| P1 | Is it an in-process/self-hosted library (no document leaves our infrastructure) or a hosted service? If hosted, treat as an additional processor: all rows in 4.1 apply. | Vendor docs; architecture decision record |
| P2 | License of each library (permissive vs copyleft/AGPL or commercial restrictions for server use), and whether license obligations are met. | Project license files; package metadata; counsel/OSS review |
| P3 | Known vulnerabilities and maintenance status (security rather than legal, route to `security-engineer`/`production-readiness-auditor`). | Advisory databases, release notes |
| P4 | For any OCR option (deferred): where processing happens, retention, training use, subprocessors, DPA. | Vendor docs |
| P5 | Does the library phone home (telemetry, remote font or resource fetching)? Must be none for hostile-file safety and privacy. | Source review, sandbox network-deny test |

### 4.3 Other vendors the Phase 2 data might reach
Hosting/object storage and database provider (where original files and text live: P2 open question Q6), logging/telemetry provider, email, and Google (only after student-approved calendar sync; separate review). Each needs the same questions: retention, training, subprocessors, region, DPA, deletion.

### 4.4 Output requested from `vendor-verification-researcher`
A dated table per question: verified fact (quoted, with page URL, version and check date), quality rating (full page read vs summary), whether it is account-specific, and an explicit "not found" where applicable. Do not infer. Hand to counsel for contract implications. Closing G2 remains a human decision.

## 5. DRAFT upload notice and attestation wording options (P2-D5)

**ALL TEXT IN THIS SECTION IS DRAFT. NOT LEGALLY APPROVED. Do not ship. Bracketed items are placeholders pending decisions.** UX rules from the syllabus doc apply: plain language, no pre-ticked box, no dark patterns, screen-reader operable, calm tone. Distinguish three different things: (1) browser/device permission (not relevant for uploads), (2) permission to use the document (rights and institutional rules), (3) consent to vendor processing.

### 5.1 Notice text options (shown before upload)
- **N1 (short, current AC4 style).** "We read your file on our server, and [when AI features are turned on] send its text to [AI provider] to find dates. We keep it until you delete it [or for N days]. We don't share it with other students." Trade-off: fits one sentence; omits vendor retention, copyright caution, deletion limits.
- **N2 (layered).** Short line as N1 plus a "What happens to your file" disclosure with: what we store (original file / text only, [per P2-D4]); who processes it (named providers, [location]); how long the provider may keep text ([VERIFY from section 4: placeholder]); what deletion does and does not reach (copies already at the provider and exports are not deleted by our delete, per S10 AC5); who can see it (not staff by default); how to delete or export. Trade-off: more complete and defensible; longer, so more reading for an ADHD audience. Mitigate with progressive disclosure and plain headings.
- **N3 (document-specific caution).** Adds: "Course materials may be protected by copyright or by your school's or instructor's rules. Some courses don't allow uploading materials to AI tools. Check yours first. You're responsible for uploading only what you're allowed to." Trade-off: puts the risk on the student in plain language; may deter uploads and does not, by itself, shift legal responsibility (counsel to assess).
- **N4 (exam/test-specific).** "Use this for practice, not for graded work you're being assessed on right now. Don't upload exams you're not supposed to have." Trade-off: aligns with integrity design; can read as accusatory; tone needs adhd-ux review.

### 5.2 Attestation options
| Option | Draft wording | Trade-offs |
|---|---|---|
| X1 No attestation; notice only | (N2 or N3 shown, upload proceeds) | Least friction; weakest record; relies on ToS acceptance elsewhere |
| X2 Single checkbox, unticked by default | "I have the right to upload this and to have it processed by [AI provider] as described, and it is not a graded assessment I'm taking right now." | Creates a record per upload; compound statement mixes rights, vendor consent and integrity. Counsel to decide whether combining is wise. |
| X3 Separate, specific attestations by document type | Syllabus: "My school or instructor lets me use this with study tools." Exam/test: "This is practice material, not a graded assessment I'm taking now, and I'm allowed to have and use it." Textbook items: "I'm using a small number of questions from my own copy for private study." | Fits the risk tiers (section 1.2 item 4); more screens and more wording to approve; false precision about rules students may not know. |
| X4 Once-per-course acknowledgement | One attestation when the first document is added to a course, restated on change | Less friction; weaker per-document record; staleness risk |
| X5 Informational "I understand" | "I've read how my file is handled" (acknowledgement, not a rights claim) | Records notice, avoids the student making rights representations they cannot verify; weaker allocation of responsibility |

Recording points for counsel and security: store attestation text version, timestamp, user, document hash; no raw content. Attestation does not make anything lawful (same caution as the recording gap report); the UI must not imply the product verified permission.

### 5.3 Vendor-processing consent wording (separate from rights attestation)
**DRAFT, NOT LEGALLY APPROVED.** "To find dates, we send the text of this file to [AI provider]. [Per their current terms, they may keep it for up to [N] days / they do not keep it: VERIFY]. They don't use it to train their models [VERIFY, from contract]. You can choose not to: paste only the dates, or add items by hand." Trade-off: offering a no-AI path (already in design: paste, manual) keeps the choice meaningful. Counsel to decide whether consent is needed, informational notice suffices, or school-level agreement supersedes.

### 5.4 Deletion and limits wording (S10 AC5 support)
**DRAFT.** "Deleting this removes your file, the text we read and the items we found from Tether. It can't remove copies already sent to [AI provider] under their retention period, or files you exported. [Add N days if verified]." Needs verification of provider retention before any number is shown.

## 6. Retention, deletion, export, minors and age (PO-7)

### 6.1 Retention, deletion, export: questions
1. P2-D4 / PO-5: store the original file or only extracted text and items? Counsel and security preference unknown. Hypothesis: text-only has lower copyright and breach exposure, but loses "return the original upload on request" (S10 AC4) and re-extraction. Decide with legal input.
2. Default retention period for each class (originals, canonical text, items, attestations, audit metadata, job logs, backups). PO-5 proposal: 180-day transcripts, 30-day deletion SLA. What deletion SLA is consistent with any promised notice, and with backup rotation windows (devops to state actual backup retention)?
3. Deletion reaching vendors: which vendor deletion actions exist (section 4 A10) and can Tether trigger them? If not, notice must say so.
4. Right-to-delete and access/export obligations under applicable state privacy laws, and any institutional requirement, for the pilot locations (VERIFY by jurisdiction; I do not assume any law applies). Need an intake channel, identity verification process and response times.
5. Legal holds and integrity investigations: can the product delete on student request while an institutional inquiry is pending? Who decides? (Tension with FERPA records-retention if the school is the custodian; VERIFY.)
6. Course archive, account closure, and graduation: what happens to uploads when a student leaves the school or the course is removed?
7. Export contents: the exported original and snippets may contain third-party copyrighted text; counsel to confirm acceptable (section 1.2 item 7).
8. Deleting data derived from a document that others contributed to: not applicable while storage is per-user only; revisit if any cross-user feature is proposed (currently prohibited by design).
9. Subject requests handled by an admin without content access (S10 AC7): confirm that identity checking can be done without seeing content.

### 6.2 Minors and age (PO-7 / P2-D14 / D8)
Open facts: the product has no age gate; the target is college students, but dual-enrollment and early-college students under 18 exist, and syllabus/test uploads are not currently age-addressed.
Questions for counsel and campuses:
1. Minimum age for account creation and uploads; should the product require 18+ (or state age of majority) and how is that verified without collecting excess data?
2. COPPA (15 U.S.C. 6501 et seq.; VERIFY) concerns children under 13; confirm it is not triggered by a college-focused product and what ToS language is appropriate.
3. For 13-17 users (dual enrollment): parental consent requirements, parental access to records (FERPA parental rights for K-12 vs postsecondary transfer; VERIFY how they apply to a student enrolled in both), and state minor-privacy laws (VERIFY by jurisdiction).
4. Anthropic's own age and usage restrictions for products serving minors (section 4 A9).
5. Content risk: a minor's uploaded documents could include more sensitive information (IEP or 504 notes, school-based health data). Should uploads reject such content? Is there a duty to treat such documents as higher sensitivity?
6. If under-18 is blocked, the sign-up flow needs a calm, non-shaming message and an accessible alternative path.
Decision needed from the Product Owner (PO-7) after counsel input: block under-18, allow with parental consent, or limit to specific pilot institutions with their own agreements.

## 7. Question tables, owners, evidence, and safe work

### 7.1 Questions for qualified counsel
| ID | Question | Owner | Evidence needed to close | Blocks |
|---|---|---|---|---|
| C1 | Does copying instructor syllabi into Tether and sending text to a model vendor create actionable copyright or contract exposure; what operator posture (ToS, notice-and-takedown, designated agent) is appropriate? (1.2 items 1-3, 8, 9) | Counsel; PO | Written counsel memo or email with scope and date | Real syllabus upload (P2-D13) |
| C2 | Is risk tiering syllabus < textbook < exam correct; should exams/textbook import be deferred or limited to student-typed items? (1.2 item 4, 3.2) | Counsel; PO | Written view and PO decision recorded in DECISIONS.md | Practice-test import scope |
| C3 | FERPA: does it reach Tether in a student-initiated model and in a school-adopted model; what contract is required for the latter? (2.2 items 1-4) | Counsel | FERPA applicability memo per model | G1; any school pilot |
| C4 | Is in-product consent/attestation adequate for any consent that counsel thinks is needed; which of N1-N4 and X1-X5 (or none) is acceptable? (5) | Counsel; PO; adhd-ux-specialist for tone | Approved wording with version id, reviewer name, date (fields below) | Shipping any upload UI to real users |
| C5 | Academic-integrity exposure for open graded assessments and adequacy of T1-T5. (3) | Counsel; PO | Written view; PO decision | Practice-test import |
| C6 | Vendor contract: review Anthropic commercial terms and DPA; acceptable retention, training and subprocessor terms; need for ZDR; any school-required addenda. (4.1) | Counsel; PO; vendor-verification-researcher (facts) | Reviewed DPA/terms with version/date; PO signing decision | G2; first real Claude call (PO-4) |
| C7 | Retention, deletion SLA, export and privacy-law obligations by pilot jurisdiction; privacy policy text. (6.1) | Counsel; PO; database-architect, devops-engineer | Privacy policy approval; documented retention schedule matching actual backups | Production; real data |
| C8 | Minors: age gate, parental consent, applicable laws. (6.2) | Counsel; PO | Written age policy; PO-7 decision recorded | Any real user pilot |
| C9 | Open-source license review of parsing libraries. (4.2 P2) | Counsel or OSS reviewer; backend-engineer | License inventory approved | Shipping |
| C10 | Complaint/takedown and incident procedures including breach notification duties. | Counsel; security-engineer | Procedure document and named contact | Production |

### 7.2 Questions for campus authorities
(Tailor per intended campus; the companion campus checklist for recording is not yet in this repo. See also `docs/compliance/UNIVERSITY_POLICY_CHECKLIST.md` if/when produced; not verified to exist.)
| ID | Question | Owner (role to be named by campus) | Evidence needed to close | Blocks |
|---|---|---|---|---|
| U1 | Campus policy on students uploading course materials (syllabi, exams, slides) to third-party or AI tools; any instructor-level opt-in/opt-out; honor-code text. | Provost/academic affairs office; academic integrity office | Written policy URL with date or written statement from an authorized official | Pilot with real documents |
| U2 | Is a student-chosen tool a "school official" arrangement, or must the school sign a data agreement for any use it endorses? Procurement and vendor-risk review process? | Registrar (FERPA officer); IT security/procurement; general counsel | Written determination and, if required, executed agreement | Any school-endorsed pilot |
| U3 | Instructor permission for the pilot course(s): syllabus, exams, materials. | Pilot instructors; department chair | Signed or emailed permission per course | Real documents in a course |
| U4 | Disability/accessibility services: referral pathway, whether the tool may be recommended, interplay with accommodation exam rules. | Accessibility office | Meeting notes and written guidance | Accessibility positioning |
| U5 | Dual-enrollment and under-18 students on campus: policy, parental involvement. | Registrar; dual-enrollment office | Written policy | Allowing under-18 |
| U6 | Records retention and holds that could conflict with student-requested deletion (if school is custodian in any model). | Registrar; records office | Written statement | School-adopted model |
| U7 | IRB or research oversight if uploads are used in a study (ties to G4 plan). | IRB/ethics body (to be named, G4 task) | IRB determination | Any research use of uploads |

### 7.3 Safe work that can proceed meanwhile (does not clear any gate)
Permitted under `CLAUDE.md` "Approved safe work":
- Contract, schema, mock and fixture work for syllabus.v1 and practice-test import using the mock extractor and `MockAi` only; `AiProviderGuard` keeps real providers refused.
- Synthetic or licensed fixtures only: fictional courses/instructors, self-authored practice items, public-domain or openly licensed (record license and source per fixture), or documents for which written permission exists. No real student uploads. No real instructor exam or publisher content without written permission.
- Drafting UI states for notice/attestation using the DRAFT text above, flagged in code and UI as placeholder and not shipped to real users; build attestation storage (text version, timestamp, document hash) as a mechanism independent of wording.
- Building deletion, export, retention-configurable and no-content-logging controls and tests (these are needed regardless of counsel's answers). Keep retention as configuration so PO-5 can change it.
- Source-based research by `vendor-verification-researcher` per section 4; no accounts created, no spend.
- Hostile-file parsing hardening and sandbox tests with synthetic hostile files.
- Parser license inventory (facts only) for counsel.

Not authorized meanwhile: real student or instructor documents in the system, any real Anthropic or parsing-service call (PO-4/G2), declaring any wording final, telling students uploads are "allowed" or "safe", school pilots, recruiting.

## 8. Draft/approval fields

| Field | Value |
|---|---|
| Document status | DRAFT counsel-review brief. NOT LEGALLY APPROVED. |
| Prepared by | legal-compliance-reviewer (AI agent; not a lawyer) |
| Date prepared | 2026-10-28 |
| Primary sources re-checked online for this brief | None. Vendor facts are quoted from `docs/vendors/verification-report.md` only; legal references are from my background knowledge and marked VERIFY. |
| Reviewing counsel (name, bar/jurisdiction) | [TBD] |
| Counsel review date / outcome | [TBD] |
| Campus authority (name, title, institution) | [TBD] |
| Campus review date / outcome | [TBD] |
| Product Owner decision (P2-D4, P2-D5, P2-D13, P2-D14; PO-5, PO-7) | [TBD] |
| Approved notice/attestation version id | [none] |

## 9. Release-gate status (as of this brief)
- G1 Classroom recording / consent / university policy: **BLOCKED** (unchanged; this brief adds Phase 2 upload questions to the same gate, does not clear it).
- G2 Vendor and Claude cost / limits selection: **UNVERIFIED** (unchanged; section 4 lists the questions still unanswered).
- Phase 2 real-document upload: **NO-GO** until C1, C3, C4, C6 and U1/U3 evidence exists and the Product Owner records the decision. Synthetic/licensed fixtures with mock providers: **GO** within existing permissions.
- Real classroom recording: **BLOCKED** (unchanged).
