# Lawyer questions and PO risk-acceptance template

**NOT LEGALLY APPROVED. I am an AI assistant, not a lawyer. Nothing here is legal advice. No lawyer, school official or vendor has reviewed it. It clears no release gate (G1 stays BLOCKED, G2 UNVERIFIED) and authorises no stage, no spend, and no acceptance of any terms.**

| Field | Value |
|---|---|
| Prepared by | `legal-compliance-reviewer` (AI agent), 2026-11-05 (date of drafting; the session date shown to me was 2026-11-04) |
| Source documents read | `docs/legal/pilot-plan-review.md`, `docs/legal/legal-register.md`, `docs/product/pilot-plan.md` (first 458 of 758 lines; the rest not read), `docs/agents/project-playbook.md`, `docs/DECISIONS.md` rows PO-20d, PO-20bi, PO-20bl, `docs/security/findings-register.md` line 150 |
| Reviewer name / approval / date | ______________________ / NOT APPROVED / ______________ |
| Jurisdiction assumptions | United States. The state of the PO, his son and the school is NOT in the repo (UNVERIFIED). Nothing below is a state-law conclusion. |
| Origin | The PO's Review Desk note ("have the lawer write it") was read by the Lead as: prepare (A) questions for a real lawyer or the school and (B) a risk-acceptance template. If that reading is wrong, discard this file. |

Evidence tags: **[REPO]** from a repo file. **[UNVERIFIED]** not confirmed in any source I read. Vendor wording below comes from `docs/legal/pilot-plan-review.md`, which read vendor pages on 2026-11-04 through a summarising tool; a human must re-read the live pages.

---

# Part A. Questions for a lawyer (or the school)

**Say this first when you call:** "I am a parent building a personal study tool for my adult son. I would pay for AI services on my own card. No one else will use it. I want to know the risks before I click anything."

Questions are in priority order. 1 to 8 matter before the PO accepts any terms (Stage 1). 9 to 11 matter before Stage 2. 12 matters only if Stage 3 is ever considered.

## Before the PO accepts any terms

1. **Can an individual buy Claude through Azure Marketplace for a family member's use?** Anthropic's Commercial Terms say "Services under these Terms are not for consumer use" [REPO: `pilot-plan-review.md` section 3, citing anthropic.com/legal/commercial-terms]. I am a private person with a card, using it for my son's study help. Does that fit, or could the vendor refuse service or end the account? What happens to money already paid? (Plan open item F-M1; UNVERIFIED.)
2. **What does "consumer use" mean in these terms, and which document controls?** The text behind `aka.ms/anthropic_tandc` has not been read by anyone in the repo [REPO: `azure-platform-verification.md` 1.6, as cited in the review]. Which document wins if the Microsoft Marketplace terms, Microsoft Product Terms and Anthropic terms disagree? Who exactly is the other party to the contract (Microsoft, Anthropic, or both)?
3. **What am I personally promising?** The customer warrants it has rights to everything it sends, and there may be an indemnity [REPO: F-M2]. If my son types something that is not his to share, am I the one responsible? How exposed am I, and what are the liability limits, governing law and any arbitration clause?
4. **Age and parent-held accounts.** My son is 18 or older [REPO: PO-20bi, the PO's own statement, not independently checked]. A parent holds the Azure and Google accounts. Is there any problem with a parent holding the vendor accounts for an adult user, with his agreement? Anthropic defines a minor as under 18 and adds duties where minors use API products directly [PRIMARY per review; re-read live]. Do any of those duties apply if he is 18 or older but I am the account holder? What should I write down to show his age and his agreement?
5. **Privacy between a parent and an adult child.** I can technically read his notes on my machine [REPO: pilot plan 4.8]. The plan promises only he sees them. Is there any legal issue with an adult child's study notes sitting on a parent's computer and parent-paid vendor accounts, even if the family promise is kept? (Not school records; I am not asking about FERPA here.)
6. **Data location, retention and training: please check these claims, I could not.** Each of these is **UNVERIFIED**:
   - Claude "Hosted on Azure" keeps prompts and answers inside Azure, with only usage data and content flagged by Anthropic's safety systems going to Anthropic [REPO: pilot plan 3.1 item 7].
   - A "Data Zone" keeps data in the US. Whether any Data Zone is US-only for this account is UNVERIFIED; the vendors' pages disagree for Sonnet 5.5 [REPO: pilot plan 3.1 item 6].
   - Anthropic keeps inputs for about 30 days (a direct-API figure; Azure may differ), longer if content is flagged for review, and zero data retention is not available by default [REPO: pilot plan 4.8; F-M4]. Anthropic's list of "Covered Models" showed a 30-day minimum retention; whether it applies to the models chosen is UNVERIFIED.
   - Anthropic "may not train models on Customer Content" [PRIMARY per review]. Whether Azure AI Speech trains on audio is UNVERIFIED [REPO: pilot plan section 11 item 2].
   Questions: what do the signed documents actually say about each? Is "flagged content goes to Anthropic" acceptable for a family's notes, and should I tell my son so in those words?
7. **Usage Policy update, effective 2026-11-12.** What changed is UNVERIFIED [REPO]. Does anything in the new policy (privacy, minors, "education" high-risk uses) affect a study-notes tool for one adult student?
8. **Is a short paid consult enough, or do I need more?** I want a cheap, scoped answer to questions 1 to 4 before I click "Agree and Proceed". If the answer is "it is unclear", what is the safest way to proceed or to stop?

## Before Stage 2 (my son's own typed notes)

9. **Deleting notes.** In Tether, delete removes Tether's copy only. It does not recall what a vendor already received (about 30 days, UNVERIFIED), and it does not delete anything already copied into his Google account [REPO: PO-15c; L-07]. Is that wording enough, and what exact words should the delete dialog use so it is not misleading? Does any law require more (a deletion deadline, export on request)? (Register rows L-07, L-08.)
10. **Keeping consent and audit records versus deleting everything.** The app keeps content-free rows (consent, audit, AI usage) after a delete until the whole account is erased [REPO: PO-20aw; L-14]. Is that a problem for one adult user?
11. **Public repository disclosure (SEC-PUBLIC).** The code repository is public. It contains the security and legal reviews (open findings with file and line, reproduction steps) and, in its history, the PO's commit author e-mail [REPO: findings-register line 150; L-19]. PO-20d: keep public for now, revisit before any real deployment or data. Questions: does publishing open findings and legal reviews create any legal exposure for me (for example if a flaw is exploited against real data)? Does it affect my position with any vendor? Is it wise to make it private before real notes go in?

## Only if Stage 3 (live lectures) is ever considered. Gate G1 is BLOCKED.

12. **Recording.** Which state's recording-consent law applies where the class is held (one-party or all-party consent), and what must the school and instructor agree to in writing before any real lecture audio is captured or sent to a vendor? Does FERPA apply to this school, to my son's own recordings, or to the tool (student-initiated use versus school-adopted use)? Can recording ever be allowed without the instructor's written permission? Nothing in this document answers these; the gap report is `docs/legal/recording-compliance-gap-report.md`. **Do not record anyone in a classroom until qualified humans have answered this and permissions are in hand.**

## Documents to bring

- `docs/product/pilot-plan.md` and `docs/legal/pilot-plan-review.md` (printed or on a screen). Remove anything you do not want a stranger to see; the repo itself is public.
- `docs/legal/legal-register.md` (rows L-04, L-05, L-07, L-08, L-09, L-13, L-14).
- Your own dated copies, saved before you go, of: the Azure Marketplace terms screen shown at deploy time (read, not accepted); the text behind `aka.ms/anthropic_tandc`; `anthropic.com/legal/commercial-terms`; the Anthropic Usage Policy as in force that day; Microsoft's Product Terms and Data Protection Addendum for Foundry and Speech; Anthropic's supported-regions policy for your billing country; the Covered Models page. [REPO: pilot plan 3.3 read-first list.]
- Your son's date of birth proof (you will not need to hand it over; know the answer) and his written agreement to use the tool, if you have one.
- Your school's recording policy and your syllabus rules only if Stage 3 comes up. Do not bring real lecture content.
- A one-line statement of what you will pay and the caps: AI $0.25 per day and $5 per month; speech cap proposed only [REPO: PO-20bk].

Questions on this page are mine, not a lawyer's. Ask the lawyer to cross out any that are wrong or to add what I missed.

---

# Part B. PO risk-acceptance template (DRAFT)

**NOT LEGALLY APPROVED. Not legal advice. Signing this does not make anything lawful, safe or approved. It only records that you read the risks and chose to go ahead.**

Fill this in only after you have read it, in your own words if you wish. Leave a line blank if you do not agree to it. An agent cannot sign or date this for you (playbook rule 11; PO-20bl).

| Field | Value |
|---|---|
| Product Owner name | ______________________ |
| Date signed | ______________________ |
| Stage covered | [ ] Stage 1 (fake material only)   [ ] Stage 2 (my son's own typed notes, local machine, no audio)   [ ] Other: ________ |
| Expires | [ ] on this date: ____________   [ ] when Stage ___ ends   [ ] at once if any "stop and re-sign" event below happens, whichever comes first |
| Lawyer consulted? | [ ] Yes, name and date: ______________   [ ] No, I chose to go ahead without |

## The open risks, in plain words

These are the legal register's open MEDIUM rows plus the public-repo item. Ratings are provisional and not confirmed by counsel (`docs/security/severity-policy.md`: MEDIUM blocks production or real users unless the PO accepts it in writing).

1. **L-07, deleting notes.** When you delete notes in Tether, the vendors may still hold a copy for a while (about 30 days is the best guess and is UNVERIFIED, and longer if the content was flagged), and copies already in a Google account stay there. The delete message might promise more than is true. [REPO: L-07 DEFERRED; PO-20bj says to fix the wording first.]
   - [ ] I accept this risk for the stage above.
   - [ ] I do not accept it. The delete wording must be fixed first.
2. **L-08, how long things are kept.** There is no approved policy for how long Tether keeps data, how fast it deletes, or what a person can ask for. There is no written privacy policy that matches how backups really work. [REPO: L-08 DEFERRED.]
   - [ ] I accept this risk for the stage above.
3. **L-14, keeping proof versus deleting.** Tether keeps content-free records (when consent was given, audit and AI-usage entries) even after notes are deleted, until the whole account is erased. No lawyer has said whether that is fine. [REPO: L-14 DEFERRED; PO-20aw.]
   - [ ] I accept this risk for the stage above.
4. **SEC-PUBLIC, the public code repository.** Anyone in the world can read the code, the security findings (with the exact place and how to reproduce them), the legal reviews, and the history (including my commit e-mail address). If real notes are ever stored, a flaw that is written up publicly is easier to attack. [REPO: findings-register line 150; PO-20d "keep public for now; revisit before any real deployment".]
   - [ ] I accept this risk for the stage above, and I confirm no real notes, names or keys are in the repo.
   - [ ] I do not accept it. The repo goes private first.

Other open legal rows (for example L-04 vendor terms, L-09 age, L-13 data location) are **not** covered by this form unless I list them here: ______________________ . Accepting terms with a vendor is a separate step that only I take, in person.

## What I am accepting

- That I understand the four risks above, in the plain words given, for the stage named, on my own machine, with one adult user (my son), no audio, and no school documents.
- That I, not an agent or a lawyer, chose to go ahead.
- That I will re-check this if any of the "stop and re-sign" events below happens.

## What this does NOT do

- It does **not** remove or reduce any risk. The risk is the same after I sign as before.
- It is **not** legal advice and was not written or reviewed by a lawyer.
- It does **not** clear gate G1 (classroom recording, consent, campus rules). G1 stays BLOCKED. It does not allow any real lecture audio, any microphone use with a real provider, or any recording of any other person.
- It does **not** clear G2 (vendor and cost), G3, G4 or G5, and does not close any register row. A row is closed only by written evidence from a named, accountable person.
- It does **not** accept any vendor terms, authorise any spending beyond the caps, or approve a stage. Stage "go" decisions stay separate, in writing, one stage at a time (PO-20bg).
- It does **not** bind or protect my son, the school or any vendor. It does not cover any other user.
- It does **not** cover anything not listed above, including rows L-04, L-09 and L-13.

## Stop and re-sign events (this acceptance ends and must be redone or dropped)

- Anyone other than my son gets access.
- Any real lecture audio, school document, or another person's material or voice is involved.
- The vendor terms or Usage Policy change, or a vendor says "consumer use" is not allowed.
- My son says stop or "not now".
- A real breach, wrong deletion, or leak of notes.
- The repo's public/private status or the hosting location changes.
- A lawyer or school official gives advice that contradicts what I accepted.

| Signed (PO) | Date | Reviewer (if a lawyer or other person reviewed) | Date |
|---|---|---|---|
| ______________________ | ____________ | ______________________ | ____________ |

*Status of this form: blank template. NOT SIGNED. NOT LEGALLY APPROVED.*
