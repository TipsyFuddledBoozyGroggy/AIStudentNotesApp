# Legal and privacy register

Status: **PROVISIONAL ratings by the Lead Architect, not legal advice, not reviewed by counsel.** Policy: `docs/security/severity-policy.md` (CRITICAL tell the PO immediately; HIGH blocks push; MEDIUM blocks production or real users; LOW noted). A row is closed only with written evidence from an accountable human (name and date). Question detail lives in `docs/legal/phase2-uploads-and-vendors.md` (C and U ids), `docs/legal/recording-compliance-gap-report.md` and `docs/security/google-tokens-and-data-residency.md` section 3.7.

Statuses: OPEN, IN-REVIEW (human working on it), CLOSED (evidence attached), ACCEPTED (PO decision recorded), NOT-APPLICABLE (rationale).

| ID | Sev | Status | Concern | Blocks | Evidence needed |
|---|---|---|---|---|---|
| L-01 | MEDIUM | OPEN | GR-1 / security register: a transcript or study guide copied into an institution-managed Google account is visible to the institution; third-party voices leave Tether's deletion reach. PO ruled 2026-10-29 (PO-12a) to keep HIGH until counsel reviews. Scope decision PO-14 (2026-10-29): Google accounts assumed personally owned; not yet enforced in code, DOWNGRADED HIGH to MEDIUM by the PO on 2026-10-29 (PO-14a, same reason as GR-1): school-managed accounts are out of scope, and the app must detect and refuse them (enforcement not built, `hd` claim presence unverified; test on the real test account). Counsel should still confirm the personal-account-only position. | PRODUCTION / real Google connection | Enforcement of personal-account-only sign-in with tests; counsel written view on the personal-account position |
| L-02 | MEDIUM | OPEN | Recording consent law and campus recording policy for the pilot jurisdiction (one-party vs all-party, instructor permission, third-party voices). Gate G1. | PRODUCTION / real recording | Jurisdiction memo; campus written policy; approved notice with version, reviewer, date |
| L-03 | MEDIUM | OPEN | FERPA applicability: student-initiated model vs school-adopted model; school-official or service-provider status; contract needed (C3, U2). Gate G1. | PRODUCTION / any school pilot | FERPA memo per model |
| L-04 | MEDIUM | OPEN | Anthropic commercial terms and DPA: retention, training use, subprocessors, zero-data-retention need (C6). Gate G2. | First real Claude call | Reviewed terms with version and date; PO signing decision |
| L-05 | MEDIUM | OPEN | Speech-to-text vendor terms (none reviewed with dated sources). Gate G2. | First real transcription | Dated vendor terms assessment, counsel review |
| L-06 | MEDIUM | OPEN | Google as a recipient: API Services User Data Policy / limited use, OAuth verification, privacy policy naming Google, controller or processor roles. | Real Google connection by students | Counsel view; verified OAuth app; privacy policy |
| L-07 | MEDIUM | OPEN | Deletion duty when a copy sits in the student's own Google account; wording for delete dialogs and retention (SEC-014, PO-5). | PRODUCTION | Counsel-approved wording and retention schedule |
| L-08 | MEDIUM | OPEN | Retention, deletion deadline, export and privacy-law duties by jurisdiction; privacy policy text (C7). | PRODUCTION | Approved privacy policy matching actual backups |
| L-09 | MEDIUM | OPEN | Minors and age (PO-7, C8): age gate, parental consent, Google account age rules, school-managed accounts. | Any real user pilot | Written age policy; PO-7 decision |
| L-10 | MEDIUM | OPEN | Copyright and contract exposure of copying instructor syllabi and exams into Tether and to a model vendor (C1, C2, U3). | Real document upload | Counsel memo; instructor permission per course |
| L-11 | MEDIUM | OPEN | Upload notice and attestation wording (P2-D5, C4). DRAFT text only. | Any upload UI to real users | Approved wording with version, reviewer, date |
| L-12 | MEDIUM | OPEN | Academic-integrity exposure for open graded assessments (C5) and campus honour-code rules (U1). | Practice-test import | Counsel and campus written views; PO decision |
| L-13 | MEDIUM | OPEN | Cross-border storage location of Google and Tether data and its privacy-law effect. | PRODUCTION | Hosting region decision (PO-7/D10) and counsel view |
| L-14 | MEDIUM | OPEN | Retention of consent and audit evidence versus a student's deletion rights. | PRODUCTION | Counsel view; retention schedule |
| L-15 | MEDIUM | OPEN | Complaint, takedown and breach-notification procedure with a named contact (C10). | PRODUCTION | Procedure document, named contact |
| L-16 | MEDIUM | OPEN | Accessibility and accommodations: disability-office pathway and accommodation exam rules (U4); disability-related claims and clinical language (no diagnosis, G4). | Accessibility positioning, school pilot | Campus written guidance; claims review |
| L-17 | LOW | OPEN | Open-source licence inventory for parsing libraries and fonts (C9). No parser chosen yet; fonts via next/font. | SHIPPING | Licence inventory |
| L-18 | LOW | OPEN | Third-party names: the Course portal theme avoids a vendor's product name, logo and assets; confirm no vendor marks in code, assets or docs. | NONE | Quick counsel or reviewer confirmation |
| L-19 | LOW | OPEN | The PO's personal Google address appears in committed docs (`DECISIONS.md` PO-9k, Google verification report; security RN-07). Privacy housekeeping; already in pushed history. | NONE | PO decision whether to scrub going forward |
| L-20 | LOW | OPEN | Research use of uploads or sessions requires IRB or ethics determination (U7, G4 study). | Any research use | IRB determination |
| L-21 | LOW | OPEN | School records-retention holds that could conflict with deletion (U6), only if a school is custodian. | School-adopted model | Registrar statement |

## Before-push check for the Lead
1. This must print nothing before a push (today it prints nothing, so this register does not block a push):
   ```
   grep -E '^\| L-[0-9]+ \| (CRITICAL|HIGH) \| (OPEN|IN-REVIEW) \|' docs/legal/legal-register.md
   ```
2. Also run the security register check in `docs/security/findings-register.md` section 6. Both must be clean before any push.
3. Before production: also no OPEN or IN-REVIEW MEDIUM in this register.

## Counts (2026-10-29, provisional)
CRITICAL 0 | HIGH 0 | MEDIUM 16 (L-01 to L-16) | LOW 5 (L-17 to L-21).
