# Recording / Consent / University Policy: Gap Report

Authored by security-engineer acting as substitute for legal-compliance-reviewer (agent not loaded). Not legal advice; not reviewed by counsel.

Date: 2026-10-27. Gate: "Classroom recording/consent/university policy" (CLAUDE.md, Explicit assurance gates).
**Recommended gate status: BLOCKED (unverified).** Nothing in this document establishes that any recording, transcription, or processing is lawful or compliant.

## 1. Code reality (inspected from repo docs, not re-audited line by line)
- Milestone 1 only (auth, courses). No microphone capture, no audio upload, no STT, no Claude calls exist.
- Therefore every consent/recording control in SEC-006..SEC-018 is DESIGNED ONLY, not implemented, not tested.

## 2. Enforced vs. designed only
| Item | Source | Status |
|---|---|---|
| Per-session consent before mic (SEC-006) | SECURITY.md | Designed only |
| Append-only consent events (SEC-007) | SECURITY.md | Designed only |
| Per-course permission attestation (SEC-008) | SECURITY.md | Designed only; wording unapproved |
| Recording indicator, one-click stop (SEC-009) | SECURITY.md | Designed only |
| Denied-mic safe state, idle auto-end (SEC-010/011) | SECURITY.md | Designed only |
| No raw audio persistence (SEC-012, D2) | SECURITY.md, PRD | Proposal; D2 undecided; nothing to enforce yet |
| Deletion, retention, export (SEC-014..016) | SECURITY.md | Designed only |
| Processor contracts (SEC-017) | SECURITY.md | Unverified; no vendor selected |
| Real-student use blocked until D1 resolved | PRD | Policy statement, not a technical control |

Note: a student attestation does not make recording lawful. It records a claim and does not verify permission.

## 3. Unverified (all items)
- Which campus(es), states, and countries apply (PRD D10 hosting/pilot scope is open).
- Applicable recording-consent law, campus recording policy, and instructor permission rules.
- Whether FERPA applies to the institution, to Tether as operator, and to these records.
- Treatment of third-party voices (instructor, classmates), minors (D8 open), and accommodations pathways.
- Vendor terms for STT, Anthropic, Google; none reviewed with dated sources.
- Adequacy of any consent wording.

## 4. Human-only tasks (accountable reviewer to be named)
1. Qualified counsel: jurisdiction analysis, consent-law opinion, notice and attestation wording, privacy policy, terms, vendor DPA review.
2. Campus officials (registrar, general counsel, academic affairs): written recording policy, pilot permission, whether a school-official/processor arrangement is needed.
3. Disability/accessibility services office: accommodation process for recording, how approved accommodations interact with the product, instructor notification rules.
4. Instructors of any pilot course: written permission; terms on lecture IP and sharing.
5. Product Owner: decide D1, D2, D8, D10; authorize any real recording.

## 5. Sources to consult (to be fetched and dated by a human or the vendor researcher; none verified here)
- State recording statutes for each pilot location: one-party vs all-party consent; rules for in-person vs remote participants who sit in other states (the strictest rule may apply).
- Federal wiretap law (18 U.S.C. 2511) and any state analogues.
- FERPA (20 U.S.C. 1232g; 34 CFR Part 99): applicability, school official exception, directory vs education records, parental rights (age 18+/postsecondary transfer of rights).
- Campus: student handbook, academic/classroom recording policy, syllabus language, disability-accommodation policy, IT acceptable use, research/IRB rules if user studies are run.
- Copyright/instructor IP and campus IP policy on lecture materials.
- Minors/dual-enrollment: state law, COPPA relevance (under 13 unlikely, confirm), campus rules.
- If non-US users: GDPR/UK GDPR or other regimes (confirm scope with counsel).
- State student-privacy and biometric/voiceprint laws (confirm whether voice data triggers any).

## 6. DRAFT consent-notice outline (NOT LEGALLY APPROVED; draft for counsel)
1. What is captured: microphone audio of the lecture, including other people's voices; live transcript derived from it.
2. Who processes it: named STT provider and Anthropic (placeholders until approved), and Google only when the student exports.
3. Whether audio is stored (placeholder pending D2) and how long transcripts are kept; how to delete.
4. You are responsible for confirming university, instructor, and local-law permission before recording; Tether does not verify this.
5. Others may be captured; do not record where not permitted; do not share transcripts without permission.
6. You can pause or stop at any time; the indicator shows state.
7. Technical microphone permission is separate from recording permission and from vendor-processing consent.
8. Contact, withdrawal, and deletion route.
Reviewer/approval fields: Counsel: [TBD] Date: [TBD]; Product Owner: [TBD]; Campus authority: [TBD].

## 7. Questions for vendor research
- Does the vendor retain audio or transcripts? Default period, zero-retention option, and whether it requires an enterprise plan?
- Is customer data used for training or evaluation by default; opt-out mechanism and written confirmation?
- Storage and processing region; subprocessors; breach notice terms.
- Does a DPA exist; does it support FERPA school-official language or a similar commitment?
- Does the vendor impose terms on recording consent or restrict education or minor use?
- Deletion on request and its SLA, including backups and logs.
- Anthropic retention terms for the API, safety-review retention, and whether lecture content is sent.
- Google scope sensitivity and verification requirements for Docs/Drive.

## 8. Evidence checklist
| # | Evidence | Reviewer | Status |
|---|---|---|---|
| 1 | Jurisdiction inventory for pilot campus (dated sources) | [TBD counsel] | Not started |
| 2 | Campus recording policy review and written permission | [TBD campus official] | Not started |
| 3 | Disability office consultation notes | [TBD] | Not started |
| 4 | Instructor written permission (per pilot course) | [TBD] | Not started |
| 5 | FERPA applicability memo | [TBD counsel] | Not started |
| 6 | Approved consent and attestation wording | [TBD counsel/PO] | Draft only |
| 7 | Vendor terms assessment (STT, Anthropic) with URLs and check dates | [TBD vendor researcher + counsel] | Not started |
| 8 | Decisions D1, D2, D8, D10 recorded | [TBD Product Owner] | Open |
| 9 | Implemented controls SEC-006..011 with passing tests | [TBD qa-engineer] | Not implemented |
| 10 | Privacy policy and retention/deletion text | [TBD counsel] | Not started |

## 9. Conclusion
No recording, consent workflow, or processing arrangement is established as lawful or compliant. Real classroom recording remains BLOCKED. Synthetic or expressly authorized controlled test audio is permitted.

Note: the deliverable paths in the agent brief (docs/compliance/...) differ from the path requested here; the Lead Architect should reconcile.
