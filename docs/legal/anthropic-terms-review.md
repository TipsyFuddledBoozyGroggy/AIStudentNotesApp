# Anthropic terms review (L-04 / C6)

**Status: DRAFT, NOT LEGAL ADVICE. NOT LEGALLY APPROVED. No gate is marked PASS by this document.**
Prepared by: `legal-compliance-reviewer` (AI agent). Review date: 2026-10-30. Counsel / accountable human reviewer: ______ (not yet named). Approval date: ______.
Scope: PO-15, PO-15f, PO-15g. Tether is non-commercial, built for the Product Owner's son. Tether would send lecture transcript excerpts, syllabi and practice tests to the Claude API; Tether data is stored in Google and AWS (L-13, noted not resolved).
Ratings are PROVISIONAL until qualified counsel confirms. A concern is closed only by written evidence from a named, accountable human.

## Sources (all fetched 2026-10-30 with WebFetch; each fetch returns a model-generated extract of the page, not the raw page, so quotes should be re-checked against the live page by a human before reliance)

| ID | URL | Date shown on page |
|---|---|---|
| S1 | https://www.anthropic.com/legal/commercial-terms | Effective June 17, 2025 |
| S2 | https://www.anthropic.com/legal/data-processing-addendum | Effective Feb 24, 2025 |
| S3 | https://privacy.claude.com/en/articles/7996866-how-long-do-you-store-my-organization-s-data (redirect from privacy.anthropic.com) | July 1, 2026 |
| S4 | https://privacy.claude.com/en/articles/7996868-is-my-data-used-for-model-training | August 18, 2026 |
| S5 | https://privacy.claude.com/en/articles/8956058-i-have-a-zero-data-retention-agreement-with-anthropic-what-products-does-it-apply-to | None given ("Updated over 2 weeks ago") |
| S6 | https://www.anthropic.com/legal/consumer-terms | Effective Oct 8, 2025 |
| S7 | https://www.anthropic.com/legal/aup | Shown as effective Nov 12, 2026 (after the fetch date; see F-M7) |
| S8 | https://platform.claude.com/docs/en/manage-claude/data-residency | None given |
| S9 | https://platform.claude.com/docs/en/manage-claude/api-and-data-retention | None given |
| S10 | https://www.anthropic.com/subprocessors (redirects to https://trust.anthropic.com/subprocessors) | NOT VERIFIED, content did not load |

## Verified facts (per the extracts above)

1. Commercial Terms are "not for consumer use"; the customer is "you or the organization, company, or other entity that you represent". Consumer Terms exclude API and Console use: the Commercial Terms govern "any Anthropic API key, the Anthropic Console" (S1, S6).
2. Customer retains rights to Inputs and owns Outputs; "Anthropic may not train models on Customer Content from Services" (S1 Section B). Commercial products are not used for training by default; exceptions are feedback submitted by thumbs up/down and explicit opt-in (S4).
3. Standard API retention: inputs and outputs automatically deleted from the backend within 30 days. Longer for customer-controlled services such as the Files API, by agreement such as ZDR, to enforce the Usage Policy, or by law. Chats flagged by automated trust and safety systems: inputs and outputs up to 2 years; classifier scores up to 7 years (S3).
4. The DPA is incorporated into the Commercial Terms (S1 Section C). Customer is controller, Anthropic is processor (S2 B.1). General authorisation for Schedule 4 subprocessors, notice of additions, 15 days to object (S2 C.1, C.3). EU SCCs, UK and Swiss addenda incorporated (S2 I). Return or deletion within 30 days after termination (S2 H.1). Breach notice within 48 hours (S2 G.1). Annual external audits; SOC 2 on written request (S2 F.1).
5. Zero data retention (ZDR): not an automatic setting. Claude API and Claude Code for Enterprise customers "can request it, subject to Anthropic's approval" (S5). Exceptions remain for law, misuse and safety classifier results (S5). Batch API (29-day retention) and Files API are not ZDR-eligible; Messages API and inline PDF and prompt caching are (S9).
6. Covered Models (Claude Fable 5.1, Mythos 5.1, Fable 5, Mythos 5 per S9) require 30-day retention, so ZDR is not available for them unless expressly authorised. Claude Sonnet 5.5 and Haiku 5.5 were not listed as Covered Models in the extract (S9 line "Covered Models"; re-check live).
7. Data residency: `inference_geo` of `us` keeps inference in US infrastructure at 1.1x price; default is `global`. Workspace geo (storage at rest) currently only `us` and cannot be changed after creation (S8).
8. Commercial Terms: customer warrants it "has all rights and permissions required to submit Inputs" (L.1) and defends Anthropic against third-party claims relating to Inputs (K.2). Liability cap is fees paid in the previous 12 months (L.3). Terms can change on 30 days notice (M.3). California law and San Francisco arbitration for customers outside the EEA, Switzerland and UK (J, M.7) (S1).
9. Usage Policy (S7 extract): "Education and credentials" is a High-risk Area for admissions, grading exams that decide admission, graduation or licensure, academic misconduct determinations, and suspension or expulsion; general educational explanation is excluded. Under-18 users of API products require extra guidelines and applicable laws. Privacy section prohibits sharing others' personal information without consent. Consumer-facing chatbots must disclose that users are talking to AI.

## Findings

### CRITICAL
None identified.

### HIGH
None identified. Rationale: no real personal data, third-party material or Anthropic call exists in the pushed repository for this review to rate against; confirm with the Lead that no real Claude call has been made. If one has, re-rate F-M1 to HIGH.

### MEDIUM (block the first real Claude call and production unless noted)

- **F-M1. Whether a non-commercial individual developer is on Commercial Terms, and whether that fits the intended use.** The Commercial Terms say they are not for consumer use. The Consumer Terms point API key use to the Commercial Terms (S1, S6). So API-key use of Tether appears to fall under Commercial Terms even if Tether is non-commercial and the account holder is an individual; the Consumer Terms impose no general non-commercial rule, only on evaluation access. NOT VERIFIED: how the Console sign-up flow presents the terms to an individual account, and whether an individual who uses the API only for a family project is treated as a "Customer" for DPA purposes. Counsel and the PO should confirm by reading the acceptance screen at Console sign-up (a human action) and capturing the version shown.
- **F-M2. Lecture transcripts contain third-party voices and content (instructor and classmates).** The customer warrants rights to submit Inputs and indemnifies Anthropic (S1 L.1, K.2). The Usage Policy bars sharing personal information of others without consent (S7). This overlaps L-02, L-10 and PO-15b (counsel not consulted). Open question for counsel: does sending transcript excerpts, syllabi and exams to Claude breach the warranty or instructor and course copyright terms.
- **F-M3. Retention is 30 days by default, and up to 2 years for flagged content.** Lecture and exam content sent to Claude persists at Anthropic for at least 30 days after deletion in Tether. Tether deletion controls (SEC-014, L-07, L-08) cannot reach this copy. User-facing privacy text must say so. Counsel to advise whether 30 days is acceptable.
- **F-M4. Zero data retention is not available by default and its eligibility is uncertain for a solo non-commercial account.** ZDR is requested and approved by Anthropic (S5), described for enterprise-type customers. NOT VERIFIED: whether Anthropic would approve it for this account. PO decision: proceed on default retention, or request ZDR first. Do not use Batch or Files API if ZDR is wanted (S9). Avoid Covered Models if ZDR is wanted (S9).
- **F-M5. Subprocessor list NOT VERIFIED.** The subprocessor page redirects to the Trust Center, whose content did not load (S10). Human action: open https://trust.anthropic.com/subprocessors, save a dated copy, and subscribe to change notices. The DPA objection window is 15 days (S2 C.3); a missed notice means a missed objection.
- **F-M6. Cross-border facts (L-13, noted not resolved).** Default inference is `global`; US-only costs 1.1x; storage at rest is US only (S8). Tether data also sits in Google and AWS (PO-15f). The EU SCCs are incorporated for transfers from the EEA (S2 I); whether any privacy law restricts the PO's own transfers is for counsel under L-13. Technical option for the engineers: set `inference_geo` explicitly rather than rely on the default, only after the PO decides on the 1.1x cost.
- **F-M7. Source-date anomaly.** The Usage Policy extract shows an effective date of Nov 12, 2026, which is after today (2026-10-30), implying a published upcoming version. NOT VERIFIED which version applies today. Re-fetch after Nov 12 and compare.
- **F-M8. Usage Policy "Education" high-risk area.** Tether must not make academic-misconduct, admission or graded-exam determinations. Practice tests and tutoring look like general educational help, which is excluded (S7), but L-12 safeguards should be mapped to this clause. Counsel to confirm that practice-test grading by Claude, if any, is not "grading exams that determine ... graduation".
- **F-M9. Minors.** The student is described as starting college and may be under 18 (L-09, PO-7). The Usage Policy requires extra guidance for minors using API-based products (S7). The linked Anthropic article on minors and education organisations returned HTTP 404 (not verified). If the son is under 18, treat this as needing counsel before the first call.
- **F-M10. Opt-in and feedback training paths.** Training only occurs through feedback or opt-in (S4). Tether engineering must not add any feedback, rating or "improve the model" calls. Also keep any Console data-sharing or feedback settings off. Human to confirm the Console privacy settings screen (S5 mentions Settings > Privacy Controls).

### LOW

- F-L1. Terms can change on 30 days notice (S1 M.3); record the version accepted and re-check on a schedule.
- F-L2. Liability cap is 12 months of fees (S1 L.3), so the PO bears most of the risk of a data incident; relevant mostly if scope grows.
- F-L3. Breach notice from Anthropic is 48 hours (S2 G.1); Tether needs its own notification path (L-15, DEFERRED).
- F-L4. Prompt caching and inline PDF are ZDR-eligible; no engineering change needed (S9).
- F-L5. The Consumer Terms age rule (18 or the local age of consent) applies to claude.ai, not API (S6); Commercial Terms give no age rule (S1).
- F-L6. If the PO later adds other users or a school pilot, re-open: an entity may need its own DPA and the controller/processor roles change.

## Not verified / could not fetch

- Subprocessor list and locations (S10).
- Whether ZDR would be granted to this account (S5).
- Anthropic minors and education API article (404).
- Any individual-account sign-up wording.
- Full Commercial Terms Service-Specific Terms, and the Schedule 4 DPA contents.
- Actual Console settings for this account.

## Questions for counsel

1. Is an individual developer, using the API for a non-commercial family project, bound by the Commercial Terms and DPA, and does the "business customers only / not for consumer use" language create any risk?
2. Does sending third-party speech (instructor, classmates) and instructor-authored syllabi and exams to Claude breach the Section L.1 warranty, instructor copyright or campus policy?
3. Is default 30-day retention (up to 2 years if flagged) acceptable, or must ZDR be requested?
4. Are Anthropic's role as processor and the incorporated SCCs enough for the PO's own transfers, given Google and AWS storage (L-13)?
5. If the student is under 18, what consent is needed?
6. Does the Usage Policy "education" high-risk clause reach practice-test feedback?
7. What notice must Tether give the student about Claude processing and Anthropic retention?

## Recommendation (not a decision, not an approval)

**Go/no-go for the first real Claude call: NO-GO until the PO decides and records the items below.** Synthetic or PO-authored test content may be sent to Claude once the PO accepts the Commercial Terms for the account, since that involves no third-party voices or real lecture content. Sending real lecture transcript excerpts, real syllabi or real practice tests stays blocked until at least F-M1, F-M2, F-M4, F-M9 are answered in writing by the PO or counsel.

**PO decisions needed:**
1. Confirm the account holder and that the Console acceptance of the Commercial Terms is recorded (version and date).
2. Choose: default 30-day retention, or request ZDR first (and then avoid Covered Models, Batch and Files API).
3. Choose: `inference_geo` `us` at 1.1x cost, or `global` default (ties to L-13).
4. Confirm the student's age (L-09) before real content.
5. Decide whether to name a counsel or accountable human for L-04, and subscribe to subprocessor notices.
6. Accept in writing the interim risk that real third-party content is sent under PO-15b without counsel review, or keep to synthetic content.

**Register effect:** L-04 stays OPEN, MEDIUM (provisional). This document does not close L-04, L-02, L-10, L-13 or any gate in `docs/RELEASE_GATES.md`. The G2 vendor gate remains unverified.

| Role | Name | Date | Signature / evidence link |
|---|---|---|---|
| Qualified counsel | ______ | ______ | ______ |
| Product Owner (decision) | ______ | ______ | ______ |
