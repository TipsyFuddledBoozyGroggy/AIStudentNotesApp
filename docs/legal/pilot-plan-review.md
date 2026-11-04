# Pilot plan: legal and privacy review notes (plain English)

**NOT LEGALLY APPROVED. DRAFT research notes for a parent (the Product Owner, "PO") and, if he chooses, a qualified lawyer. I am an AI assistant, not a lawyer. This clears no gate, authorises nothing, and does not tell anyone to accept any terms.**

| Field | Value |
|---|---|
| Document reviewed | `docs/product/pilot-plan.md` (DRAFT staged pilot plan) |
| Prepared by | `legal-compliance-reviewer` (AI agent), 2026-11-04 |
| Human reviewer name | ______________________ (unassigned) |
| Qualified counsel (if any) | ______________________ (none named; PO-20ai) |
| Approval / date | NOT APPROVED. Blank until a named human fills it in. |
| Jurisdiction assumptions | United States; PO and son's state and the school's state are NOT in the repo (UNVERIFIED). Nothing below is a state-law conclusion. |
| Release-gate status | G1 BLOCKED. G2 UNVERIFIED. Nothing here moves any gate. Stage 3 stays BLOCKED. |

**Evidence tags used throughout**
- **[REPO]** taken from a file in this repository (file named).
- **[PRIMARY]** read from a public primary source on 2026-11-04 (URL given). The reading tool returns a machine summary of the page, not the raw page, so a human must re-read the live page before relying on any wording or date.
- **[UNVERIFIED]** not confirmed in any source I read.

---

## Top 5 findings

1. **Stages 1 and 2 are mostly well fenced, but "his own material" is too loose.** The plan bans instructor slides, handouts, exams, textbook text and school syllabi (plan 4.1) but does not say what to do about notes he typed *while looking at* slides or a lecture, notes that paraphrase the instructor closely, shared study-group notes, or practice questions "made himself" that copy an exam's style or wording. Those are the realistic leaks. A tighter definition is proposed in section 1.
2. **The son's age is an open fact the plan treats as settled in places.** The plan's own D7 says it is open; the register (L-09, PO-20k) says "an adult"; the Anthropic review (F-M9) says he may be under 18. Anthropic's usage policy defines a minor as under 18 and adds duties for products that let minors use the API directly. The PO needs to establish and write down his date of birth status before Stage 2 and, safest, before accepting any terms.
3. **Accepting Marketplace/Anthropic terms is a real contract signed by the PO personally, with a card and no vendor-enforced spend cap.** Anthropic's Commercial Terms say the services "are not for consumer use" and the customer warrants it has rights to everything it submits. Whether a card-holding individual using it for a family member fits those terms is UNVERIFIED and is the plan's own open item (F-M1). This is a pre-Stage-1 matter, not only pre-Stage-2: the terms bind from first electronic consent or first access.
4. **The 12 November 2026 Anthropic policy update could not be diffed.** The live policy page shows an effective date of Nov 12, 2026, but I could not obtain the old-versus-new text. I cannot say it changes nothing. Current text (privacy, minors, "education and credentials" high-risk list) does not obviously block this plan, but a human must read the version in force on the day the PO decides.
5. **The plan and the legal register have gaps that should be written down before "go".** The register's L-04, L-05 and L-13 still describe the older Anthropic-direct/Bedrock/AWS picture or are marked DEFERRED; there is no row for the Azure Marketplace/Microsoft contract stack, the AUP update, the licence terms of "licensed" test audio, real voices in test audio, or whether the PO may see his adult son's notes. See section 5.

---

## 1. Do Stages 1 and 2 avoid recording anyone, other people's data, and copyrighted instructor material?

### Stage 1 (synthetic or licensed content)

- [REPO] Stage 1 allows "synthetic or licensed lecture audio, fake syllabi, invented notes", no real syllabi (plan 3.2; PO-20l).
- **No one is recorded live.** [REPO] The plan states the microphone use is test audio only and G1 untouched (plan 3.9). The app has no real recording path yet (plan section 2, `docs/PROGRESS.md`). This holds as written.
- **Gaps to close before Stage 1 "go":**
  - "Licensed" audio is not automatically safe. The licence must allow copying it to a third-party cloud service and processing by AI (terms vary; e.g. some open lecture licences forbid commercial use or derivatives). Which audio will be used is not in the plan. [UNVERIFIED]
  - Real lecturers' voices in licensed audio are still real people's voices. They were published by them under a licence, which is different from a classroom. Prefer text-to-speech or voices of people who have agreed in writing.
  - "Self-made" audio: if the PO, the son or a family member reads a script, only people who have agreed should be heard. A friend or classmate's voice is not "synthetic".
  - "Fake syllabi" must be invented, not edited copies of a real school syllabus.
  - Azure Speech: diarization (speaker labelling) stays off per plan 3.2. [REPO] `stt-vendor-terms-review.md` H-3, M-7. Training on Azure Speech audio is [UNVERIFIED] (plan section 11, item 2), so even test audio should be content the PO is content to have a vendor see.
- **Proposed plan edit:** add a one-line rule in 3.2: "Every audio or text file used in Stage 1 is recorded in a list with source, licence, and who is heard; only files on the list may be used."

### Stage 2 (son's own typed notes and practice questions)

- [REPO] Allowed: notes he typed, questions he wrote, practice questions he made, his plans. Not allowed: instructor slides, handouts, exams, textbook text, school syllabi, recordings, anyone else's writing (plan 4.1). Microphone off (plan 4.3).
- **This avoids recording anyone and avoids uploading other people's files, as written.** It does not fully avoid other people's material or data, for these reasons:
  1. **Notes taken from slides or lectures.** A student's notes of a lecture often reproduce the instructor's wording, slides, examples or exam hints. Whether that is the son's "own" work for copyright or school-contract purposes is a question for counsel or the school and is not answered in the repo (L-10 is DEFERRED; Anthropic review F-M2). [REPO]
  2. **Practice questions that mirror an exam, quiz or textbook.** "Made himself" should not include retyped, lightly reworded, or remembered instructor or textbook questions.
  3. **Other people's personal data inside his notes.** Names of classmates, instructor remarks about named students, group-chat text, emails, a friend's health or disability details. The Anthropic usage policy bars sharing others' personal information without consent. [PRIMARY: https://www.anthropic.com/legal/aup, read 2026-11-04]
  4. **His own sensitive data.** Notes may mention his own disability, accommodations, or health. That is his data, but it goes to vendors and sits in logs-adjacent systems. The plan does not name this. A plain warning (plan section 9) should say it.
  5. **Shared or collaborative notes.** Anything a classmate wrote or that came from a shared drive is not his own.
  6. **Confidentiality rules.** Some courses have rules about not sharing course materials (honour code, syllabus terms, NDAs for clinical/lab courses). [UNVERIFIED for his school; L-12, U1 in the register concern honour codes.]
- **Tighter definition proposed for plan 4.1** (for the PO to adopt or change):
  - "His own material is text he composed in his own words, not copied, pasted, retyped or closely paraphrased from any instructor, textbook, slide, handout, exam, classmate or website.
  - It contains no other person's name or personal details unless that person has agreed.
  - When unsure, it does not go in. Doubt is a STOP (the plan already says so for consent)."
- **Also decide** whether a typed *summary of a lecture he attended* is allowed. The plan's wording says "notes he typed" and so would allow it; I recommend the PO decide this explicitly (it is the closest thing to lecture content that Stage 2 can contain) and, until counsel or the school says otherwise, treat it as not allowed or limited to his own reflections and questions. This is a recommendation for the PO, not a legal conclusion.

---

## 2. The son's age and vendor age rules

- [REPO] Conflicting statements: `legal-register.md` L-09 and `DECISIONS.md` PO-20k call the only user "the PO's son, an adult". `stt-vendor-terms-review.md` M-2 says the student's age is "not in this repo" (as quoted in plan section 12 item 3). `anthropic-terms-review.md` F-M9 says he "may be under 18" and recommends counsel before the first call if so. `DECISIONS.md` PO-16 says the user is probably the PO alone. PO-7 (minimum age) is still open for production.
- **What the PO needs to establish and record, in writing, with date:**
  1. The son's date of birth status: is he 18 or older today, and on the day each stage would start? (A person starting college can be 17.)
  2. Who the user is: the son only, or the PO as well (PO-16 versus PO-15 conflict).
  3. Whether the son's Google account (if ever used) is a personal account and what age rules apply to it (L-09 mentions Google account age rules; not researched here). [UNVERIFIED]
  4. Whether the son has agreed to use the tool and to the PO paying and being the contract holder.
- **What the vendor sources say (what I could read):**
  - Anthropic usage policy: a minor is "any individual under the age of 18 years old, regardless of jurisdiction"; organisations that let minors interact directly with products built on the API must follow additional guidelines and all applicable laws; age eligibility for Anthropic's own products sits in its Terms of Service, not the policy. [PRIMARY: https://www.anthropic.com/legal/aup, read 2026-11-04]
  - Anthropic Commercial Terms page: no age requirement stated. [PRIMARY: https://www.anthropic.com/legal/commercial-terms, read 2026-11-04]
  - Microsoft requires a paid subscription with a valid payment method. It does not (on the page I read) set an age for the *end user* of an app. [PRIMARY: https://learn.microsoft.com/en-us/azure/ai-foundry/foundry-models/how-to/use-foundry-models-claude, page dated 2026-09-21, read 2026-11-04]. Microsoft's own account-holder age rules: [UNVERIFIED].
- **If he is 18+:** the vendor age rules above are not triggered by the end user. The plan can then stop describing him as a possible minor. [Inference, not a legal conclusion.]
- **If he is under 18:** treat as needing counsel before the first real-content call (F-M9, [REPO]); do not start Stage 2 until the PO has a written view.
- **A separate parent question:** if the PO wants to read or help with his adult son's notes (plan D13), that is a family decision and also a privacy one. In the US, rights over *school* education records generally pass from parent to student when the student turns 18 or attends a postsecondary school (FERPA). This concerns school records, not the son's own notes, and I have not read the primary text for this review. [UNVERIFIED here; counsel question L-03.] The point for the plan: do not let anything from the school's own records flow into Tether on the PO's say-so.

---

## 3. What accepting the Azure Marketplace / Anthropic terms means for a card-holding individual (and what to read first)

This section tells the PO what to read. It does not tell him to click.

**What the sources say**
- [PRIMARY: Microsoft Learn page above, read 2026-11-04] To deploy Claude, the PO reads "the Azure Marketplace terms, select[s] an industry, and select[s] **Agree and Proceed**". A paid Azure subscription with a valid payment method and a billing country where Anthropic offers the models is required; free trial, student and credit-only accounts are not supported; "if you have an account with a credit card on file, the credit card will be charged". Anthropic's Supported Regions Policy may apply (aka.ms/supported_anthropic_regions, not opened).
- [PRIMARY: https://www.anthropic.com/legal/commercial-terms, read 2026-11-04] "Services under these Terms are not for consumer use." The customer represents it "has all rights and permissions required to submit Inputs". Customer is responsible for evaluating Outputs. The terms take effect on the earlier of first electronic consent or first access to the Services. Anthropic may update terms, generally effective 30 days after posting or notice. Anthropic "may not train models on Customer Content from Services" (Section B of that page). Data handling is under a Data Processing Addendum. Retention periods are not stated on that page.
- [REPO] `azure-platform-verification.md` 1.6: the text behind `aka.ms/anthropic_tandc` has **not been opened**; whether it equals the commercial terms page, and which document wins in a conflict, is UNVERIFIED. 1.5 and plan section 11 item 4: whether an individual may buy through Marketplace for this use is UNVERIFIED.
- [REPO] `anthropic-terms-review.md` F-M1 (individual, non-commercial customer vs "not for consumer use"), F-M2 (third-party content and the rights warranty, plus a customer indemnity noted from S1 K.2), F-M4 (zero data retention not available by default), F-M9 (minors).

**What it plausibly means in plain terms (my reading, not legal advice, to be confirmed)**
- The PO becomes the contracting "customer" personally, not the son. He is responsible for what is sent, including anything the son types (warranty of rights to inputs; possible indemnity).
- The PO pays by card. The $5 budget on the account sends email only; Azure bills Claude hourly and invoices monthly in arrears. [REPO plan 3.1 item 3, `azure-platform-verification.md` 1.7]. Only the app-side cap in the plan actually stops spend.
- Terms can change on 30 days' notice; the PO would need to re-check them.
- A personal, family-use buyer may be outside what the vendor intends ("not for consumer use"). The vendor could in principle refuse service or end the account. Consequences for refunds, liability and disputes: [UNVERIFIED].

**What the PO should read before deciding (checklist; save a dated copy of each)**
1. The Marketplace terms screen shown at deploy time, in full, including who the contracting parties are (Microsoft, Anthropic, or both) and what the "industry" choice means for the PO (answer truthfully; if no option fits, that is itself a question to ask).
2. The text behind `https://aka.ms/anthropic_tandc` (Commercial Terms and DPA as presented through Azure), compared with `anthropic.com/legal/commercial-terms`.
3. In those: the consumer/business restriction; the age clause (if any); the customer warranty and indemnity; limits on liability; governing law and any arbitration clause; suspension and termination; retention periods and whether they apply on Azure; subprocessors; what happens if Anthropic's Usage Policy is breached.
4. The Microsoft Product Terms and Data Protection Addendum for the Foundry/Speech resources, and Anthropic's supported-regions policy for the PO's billing country.
5. The Anthropic Usage Policy in force that day (section 4 below).
6. Whether Anthropic classifies the chosen model as a Covered Model. [PRIMARY: https://support.claude.com/en/articles/15425695-covered-models, read 2026-11-04] That page lists Claude Fable 5, Fable 5.1, Mythos 5 and Mythos 5.1 (not Sonnet 5.5 or Haiku 5.5) as Covered Models, with a 30-day minimum retention and no zero data retention. Whether that list is current for the PO's chosen models: [UNVERIFIED]; re-read the live page.
7. The spend picture: Azure's cap behaviour (alerts only), and the app-side cap (plan 3.1 item 3).
8. Whether to ask a lawyer first. Even a short paid consult on "individual buying Claude via Azure Marketplace for a family member's study use" is a PO decision (D10 already covers lawyers for Stage 3; this is a separate, earlier use of one).

**Plan edit:** plan Stage 1 entry item 2 says the PO accepts terms himself, and exit item 4 says F-M1/F-M2/F-M4/F-M9 are answered *before Stage 2*. Because the contract binds at Stage 1, the plan should say F-M1 and F-M9 are answered before the PO accepts anything. (The existing Anthropic review already says synthetic content is acceptable only after the PO records Commercial Terms acceptance [REPO, register L-04].)

---

## 4. Anthropic Acceptable Use Policy update effective 2026-11-12

- [REPO] `azure-platform-verification.md` evidence section: a banner on the Foundry model pages says Anthropic is updating its Acceptable Use Policy effective November 12 (2026). Plan 3.6 lists changed terms as a STOP condition.
- [PRIMARY: https://www.anthropic.com/legal/aup, read 2026-11-04] The page now shows an effective date of "Nov 12, 2026", audience "All users", and links to a version history. It was not possible to read the version history or an old-versus-new comparison, and the page does not describe a separate upcoming change. **What changed in the update: UNVERIFIED.** (Today is 2026-11-04, so the date is in the future relative to my read; the page may be showing the incoming version.)
- [PRIMARY: https://www.anthropic.com/news/updating-our-usage-policy, read 2026-11-04] That announcement is about a 2024 change (renaming to "Usage Policy", effective 2024-06-06), not 2026. It does not explain the 2026 update.
- **Current text relevant to this plan** [PRIMARY, AUP page]:
  - Privacy: bars "sharing personal information without consent" and collecting private information (health, biometric data) without permission. Relevant to Stage 2 (others' names in notes) and to any voice/biometric use (diarization off helps).
  - Minors: minor = under 18; extra guidelines for products where minors interact directly with the API.
  - "Education and credentials" is a high-risk area covering admissions decisions, grading assessments that determine admission or graduation (multiple-choice testing excluded), and academic-misconduct determinations, requiring qualified human review and AI disclosure. Tether's practice questions and AI-estimated answers are not graduation decisions, and the repo already marks answers as AI-estimated (CLAUDE.md invariant 6). **I read this as not triggered, but that is my reading of a summary; confirm.** Tether must never be used to judge whether a student cheated or to produce official grades.
- **Does it change the plan?** Not on what I could read. Three things for the PO: (a) read the version in force on the day he decides (if before Nov 12, read what takes effect Nov 12 too); (b) keep a dated copy; (c) the plan's rule "no diagnosis, no clinical claims, no attention surveillance" (CLAUDE.md invariant 7) is consistent with the privacy and biometric language, but emotion-inference-type features should be checked against the new text before any are built. [UNVERIFIED for the new text]

---

## 5. Gaps between the plan and `docs/legal/legal-register.md`

Register ratings are the Lead Architect's provisional ones. I propose, not make, changes. I edited no other file.

| # | Gap | Register row | Suggested follow-up (provisional rating) |
|---|---|---|---|
| G-1 | Plan treats the son as an adult (4.2 refers to the age question but D7 is open); register L-09 says "an adult" as a scope boundary, DEFERRED. Not established in writing. | L-09 | Record the PO's written age statement. If under 18, raise to HIGH (pick the higher when unsure under the severity policy). Until known, rate MEDIUM and note "unconfirmed". |
| G-2 | The plan's Stage 1 requires the PO to accept Marketplace/Anthropic terms; register L-04 still says the Azure path is not reviewed and F-M1/F-M2/F-M4/F-M9 are open, and ties the NO-GO to "real content". Plan places F-M1/F-M9 after Stage 1. | L-04 | Plan and register should both say: terms are read and F-M1 and F-M9 answered before acceptance. Stays MEDIUM, blocks first real Claude call. |
| G-3 | No register row for the Microsoft/Azure contract stack: Marketplace terms, Microsoft Product Terms, DPA, whether an individual may buy, precedence between documents. | none (L-04 mentions only a fresh review needed) | New row: MEDIUM, OPEN, blocks first real Azure call. |
| G-4 | No register row for the Anthropic policy update effective 2026-11-12 (Foundry banner). | none | New row: LOW or MEDIUM, OPEN; closed only by a named human reading the version in force. |
| G-5 | Licence terms and real voices in "licensed" Stage 1 test audio are not tracked. | none | New row: LOW/MEDIUM, OPEN, blocks use of any licensed audio. |
| G-6 | Stage 2 "his own notes" can include lecture-derived content and others' personal data; L-10 covers uploaded syllabi/exams only and is DEFERRED; no row for notes containing classmates' or instructor's information. | L-10, L-02 | Extend L-10 text (or add a row): MEDIUM, blocks Stage 2 until the PO adopts the tighter definition in section 1. |
| G-7 | Who can see the son's notes (plan D13) and parent access to an adult child's data; no register row. | none (L-03 FERPA DEFERRED) | New row: LOW, OPEN; closed by a recorded family decision. |
| G-8 | Plan says deletion in Tether does not recall vendor copies and sets the user warning (section 9) as DRAFT. Register L-07 (delete dialog wording) and L-08 (retention policy) are DEFERRED and block "PRODUCTION"; Stage 2 is real use by a real person, which the severity policy treats as blocked at MEDIUM ("MEDIUM blocks production or real users"). | L-07, L-08, L-14 | These DEFERRED MEDIUM rows block real users under the severity policy. Either the PO records a risk acceptance for Stage 2 (status ACCEPTED, named, dated) or the wording is done first. The plan does not say which. |
| G-9 | Register L-13 still names AWS in its main text; PO-20aa/ab replaced it with Azure. Cross-border/residency of Azure Speech and Foundry (Data Zone US-only) is UNVERIFIED. | L-13 | Tidy L-13; keep OPEN. |
| G-10 | Register footer counts say "2026-11-01" and treat several rows as pre-Azure; the Anthropic review (`anthropic-terms-review.md`) is stale on Azure per plan section 12 item 4. | counts line | Refresh after any change. |

Items consistent with the plan: L-02 (recording law, G1) BLOCKED/ACCEPTED for personal mock-only use; L-01 (school-managed Google accounts refused at connect) matches plan 4.2 item 4; L-10 matches "no school documents"; L-20 research use deferred matches plan section 13.

---

## 6. Items that remain human-only gates (none can be cleared by an AI)

| Item | Why human-only | Who | Status |
|---|---|---|---|
| G1 classroom recording: school policy, instructor written permission, local recording law, FERPA applicability, final consent wording, named G1 reviewer | Needs a school official, an instructor, and ideally counsel | PO, school, counsel | BLOCKED. Plan section 5 stays correct. |
| Reading and accepting Marketplace/Anthropic terms | Contract signature by the PO; PO-20an forbids agents from accepting | PO (and counsel if he wants) | Not authorised, not done |
| Pay-as-you-go upgrade and spend cap amount (T-AZ-UPGRADE, D1, D2) | Spending decision | PO | Open |
| Son's age statement and his freely given agreement | Facts only the family knows | PO and son | Open |
| Adopting the "own material" definition and the warning wording | Product/family decision; wording is not counsel-approved | PO | Draft |
| Confirming the live AUP version, Covered Model status, retention periods, region/Data Zone | Needs a human to read live pages and the PO's portal | PO or delegate | UNVERIFIED |
| Whether to consult a lawyer on the individual-buyer question and on Stage 3 | PO decision (D10) | PO | Open |
| G2 sign-off (vendor/cost), G3 real-device tests, G4 human validation, G5 production readiness | Evidence and named reviewers required | See `docs/RELEASE_GATES.md` | UNVERIFIED / NOT_RUN / BLOCKED |
| Closing any register row | Written evidence from a named accountable human | Named reviewer | None closed |

---

## What should change in the plan before the PO says "go"

1. Add to plan 3.2 a source-and-licence list for all Stage 1 audio and documents; no real person's voice without their written agreement; no edited copies of real syllabi.
2. Replace plan 4.1 with the tighter "own material" definition (section 1) and decide explicitly whether lecture-derived notes are allowed. Add a Stage 2 STOP: another person's name or personal details found in his notes.
3. Move the age check (D7) and F-M1/F-M9 to before the PO accepts any terms, i.e. before Stage 1, not after it.
4. Add to section 3.3 a "read first" step listing the items in section 3 of this review, with the instruction to save dated copies. Keep the existing rule that no agent accepts terms.
5. Add the AUP update (2026-11-12) as a dated read-and-record task, not only a STOP condition, and add the Covered Model/retention check (live page) to the warning wording in plan section 9, since the plan's "30 days" figure is a direct-API inference.
6. State in plan 4.2 how the DEFERRED MEDIUM rows (L-07, L-08, L-14) are handled for Stage 2: PO-recorded acceptance or wording done first.
7. Add the register rows suggested in section 5 (G-3 to G-7), through the Lead or the register owner.
8. Mention that the plan's cost cap is the only spend control that will actually stop calls; Azure's budget sends email only.

## Not done / limits

- No lawyer reviewed anything. No state or school-specific law, policy or FERPA primary text was read in this pass.
- Primary sources were fetched through a summarising tool on 2026-11-04; wording, dates and the Covered Models list must be re-read live by a human. I did not open `aka.ms/anthropic_tandc`, the Marketplace terms screen, Microsoft's DPA/Product Terms, Anthropic's consumer terms, or Anthropic's supported-regions page.
- I read only the specified plan, the register, the playbook, and targeted lines of `anthropic-terms-review.md`, `azure-platform-verification.md` and `DECISIONS.md`.
- No git commands run, no other file changed.
