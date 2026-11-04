# Tether pilot plan (staged, plain English)

**Status: DRAFT proposal for the Product Owner (PO). Nothing here is approved, built or spent.** It was agreed in outline with the PO in chat; this file writes it down. It is planning only. It is not legal advice and it clears no release gate.

**Who this is for:** Tether is a personal, non-commercial tool for the PO's son, who is the only intended user (PO-15, `docs/DECISIONS.md`). Legal questions are kept in `docs/legal/legal-register.md` so they can be revisited if that ever changes.

**How to read the evidence tags.** Every fact names the repo file it came from. "UNVERIFIED" means the repo says it has not been confirmed. "Estimate" means arithmetic I did from figures in the vendor docs; it is not a quote. The vendor pages in the repo were read through a fetch tool that returns summaries, so a human must re-read the live page before relying on any number (`docs/vendors/azure-platform-verification.md`, "How to read this file").

---

## 1. The plan on one page

| Stage | What it is | Data allowed | Real services | Gate effect |
|---|---|---|---|---|
| 0 (now) | Build and test on mock data | Mock and synthetic only | None | None; all gates unchanged |
| 1 | Try the real speech and Claude services on fake material | Synthetic or licensed lecture audio, fake syllabi, invented notes | Azure AI Speech, Claude on Azure | Produces evidence toward G2 (does not pass it) |
| 2 | The son uses it on his own study material | His own typed notes and his own practice questions. No lecture audio. No other person's voice | Same as Stage 1, plus his real (own) text | Touches G2, G3, G4 lightly; passes none |
| 3 | Live lecture recording | Real lecture audio | Same | **BLOCKED by G1. This plan does not weaken it.** |

Each step needs a written PO "go" before it starts (section 10, decision D4). No step starts because an earlier step "worked".

**Gate register today (`docs/RELEASE_GATES.md`):** G1 BLOCKED, G2 UNVERIFIED, G3 NOT_RUN (classroom audio part BLOCKED on G1), G4 NOT_RUN, G5 BLOCKED / NOT_VERIFIED. Gates passed: 0 of 5. Nothing in this plan may be reported as PASS.

---

## 2. Stage 0: mock data only (where we are now)

**Goal.** Keep building and testing the product without touching a real service.

**Where the project stands (`docs/PROGRESS.md`, Iteration 12).** Everything runs on mock or synthetic data. No real recording, AI, Google or Azure call exists in the code. The PO has opened an Azure free-trial account and taken screenshots; nothing is deployed and no terms were accepted (`docs/vendors/azure-platform-verification.md`, evidence sections; PO-20bd, PO-20an).

**Entry conditions.** None; this is the current state.

**What is built or changed.** Nothing new is required for this plan. Work continues on the items already in `docs/PROGRESS.md`.

**What the PO decides or does.** Nothing for Stage 0 itself. The Stage 1 decisions in section 10 can be answered whenever the PO is ready.

**Cost.** None. The Azure account shows amount due $0.00 and no charges (PO-20bd).

**Success measures.**
- The existing automated suites stay green (PROGRESS records many consecutive green GitHub runs).
- No real provider is called: the mock default stays on (`azure-platform-verification.md`, recommendation 3).

**STOP conditions.** Any real credential, real lecture audio or real student document appears in the repo, logs or test fixtures.

**Safety.** Mock data only, so nothing to pause or delete beyond normal test cleanup.

**Exit to Stage 1.** The PO has answered D1 to D6 in section 10 and a safe spend cap is configured and tested (Stage 1 entry list below).

**Gates touched.** None changed. G1 BLOCKED, G2 UNVERIFIED, G3 and G4 NOT_RUN, G5 BLOCKED.

---

## 3. Stage 1: real services on synthetic or licensed content only

**Goal.** Find out, with fake material, whether the real speech-to-text and Claude services are good enough, fast enough and cheap enough, and whether the app's safety limits really work. This is the "measured authorized pilot" that G2 asks for (`docs/RELEASE_GATES.md`, G2 row). It is not a student trial.

### 3.1 Entry conditions (all must be true)

1. The PO has decided to upgrade the Azure account to pay-as-you-go (desk item T-AZ-UPGRADE, **unanswered**). The reason: Claude on Foundry needs a paid pay-as-you-go subscription; free-trial and credit-only subscriptions are not supported (`azure-platform-verification.md` 1.5). The CLI check of the PO's account showed every Claude quota row at 0 (same file, CLI section).
2. The PO has decided to accept Anthropic's terms through Azure Marketplace himself, in person. "Agree and Proceed" is a separate step from looking at the options, and nothing authorises it yet (PO-20an). The terms text behind `aka.ms/anthropic_tandc` has not been read (`azure-platform-verification.md` 1.6, section 9).
3. A hard daily spend cap is chosen by the PO (D1) and **enforced by the app before every call**, not only by Azure email alerts. Reason: the budget on the account is described as "$5 USD monthly, alerts at $3 and $5, email only" (PO-20bd), and Azure bills Claude "hourly, invoiced monthly in arrears" (`azure-platform-verification.md` 1.7), so Azure alone will not stop a runaway. The app already has a per-session AI cap placeholder (PO-20t: left as a placeholder "until a priced pilot") and a designed per-user daily cap (`docs/ai/live-lecture-ai-design.md`, line 91). Stage 1 is the "priced pilot" PO-20t refers to. The cap must be proven by a test that it stops calls (see 3.5).
4. The PO has chosen a region. His stated preference is West US or West US 3 (PO-20bd). That preference authorises nothing by itself. In West US 3 the account's CLI check showed Sonnet 5.5 and Haiku 5.5 version 2 offering Global and Data Zone deployments; whether the Data Zone there is US-only is UNVERIFIED (`azure-platform-verification.md`, CLI section).
5. The Speech resource is created and its region noted. Speech stores and processes data in the region of the resource (`azure-platform-verification.md` 2.2). Whether the Foundry resource the CLI found also serves Speech is UNVERIFIED; the doc lists "Speech resource: region, tier (F0 vs S0), quota" as still to capture (section 6, item 6).
6. Only the PO (an adult) holds the Azure subscription and accepts terms (`stt-vendor-terms-review.md` M-2; `azure-platform-verification.md` 1.5).

### 3.2 What is built or changed

- Real providers are put behind the interfaces that already exist: speech behind `ISttProvider`, Claude behind `ILlmProvider` (`azure-platform-verification.md`, recommendations 1 and 4). Mock stays the default; real is switched on by explicit configuration.
- The Claude client must follow the Foundry limits: no `temperature`, no `top_k`, `top_p` only 0.99, no Batches or Files API (`azure-platform-verification.md` 1.2, 1.8). Whether the current client already complies is UNVERIFIED ("repo code not read"); `ai-agent-engineer` must check.
- Speech: diarization (speaker labelling) stays OFF and SDK transcription logging stays off (`azure-platform-verification.md`, recommendation 4; `stt-vendor-terms-review.md` H-3, M-7).
- Logs hold metadata only (counts, timings, cost), never raw transcript or note text (`CLAUDE.md` invariant 10).
- Keys stay in a local key store. Azure Key Vault is production only (PO-20ao).
- The app runs on the PO's machine. No Azure hosting, database or Key Vault is created for this stage (PO-20ao, PO-20ba: "nothing is deployed"). Where the app runs is decision D6.
- Synthetic test material: licensed or self-made lecture audio, fake syllabi, invented notes. Real syllabi stay out (PO-20l: "synthetic or own documents only until a policy exists").

### 3.3 What the PO must decide or do himself

- Upgrade Azure to pay-as-you-go: yes or no (D2).
- Accept Marketplace and Anthropic terms himself, only after he has read them (D3). No agent may click "Agree and Proceed" (PO-20an).
- Choose the daily spend cap (D1).
- Confirm the region (D5).
- Give a written "go" for the start of Stage 1 (D4).

### 3.4 Cost estimate (ESTIMATES; figures only from the vendor docs)

**Claude list prices per million tokens (MTok)** (`azure-platform-verification.md` 1.7, source A9; PO's screenshots agree, PO-20bd):
- Sonnet 5.5: $2 input, $10 output.
- Haiku 5.5: $0.10 input, $0.50 output, for prompts up to 100,000 tokens. Over 100,000 tokens: $0.50 input, $2.50 output.
- Data Zone (US) deployments carry a 1.1x multiplier.
- Billing is in Claude Consumption Units at $0.01 each, after use, no prepaid credits.
- The price shown to this subscription in the portal is UNVERIFIED and may differ from list.

**Arithmetic example (hypothetical token counts chosen only to show the sums; they are not measurements).** One test session using 500,000 input tokens and 50,000 output tokens:
- Sonnet 5.5: $1.00 + $0.50 = about $1.50. With the 1.1x Data Zone multiplier about $1.65.
- Haiku 5.5 (under 100k per prompt): $0.05 + $0.025 = about $0.075. With 1.1x about $0.083.

Rolling summaries re-send a transcript window each time, so input tokens will likely dominate (`azure-platform-verification.md` 1.7, last row). The real token counts are what Stage 1 measures.

**Ceiling from the default quota (derived estimate).** Pay-as-you-go default for both models is 40 requests per minute, 40,000 uncached input tokens per minute and 8,000 output tokens per minute (`azure-platform-verification.md` 1.7). If a model ran flat out for one hour at those limits: input 2.4 MTok and output 0.48 MTok. Sonnet 5.5 would cost about $4.80 + $4.80 = about $9.60 per hour; Haiku 5.5 about $0.24 + $0.24 = about $0.48 per hour. This is a rate limit, not a spend cap, and Azure may grant a different quota. Quota is UNVERIFIED for this account until it is upgraded; today it is 0.

**Speech-to-text price: UNVERIFIED.** The Microsoft pricing page arrived with dollar amounts masked, so no hourly price is stated anywhere in the repo (`azure-platform-verification.md` 2.2, Pricing row). What is readable: a free tier of 5 audio hours per month shared by real-time Standard and Custom, with Free (F0) limited to 1 concurrent stream (same file, 2.2). The PO must see the price in the Azure pricing calculator before approving any speech spend (section 6, item 3 of that file).

**Everything else:** no Azure hosting, database or Key Vault price is known (`azure-platform-verification.md` 9). Stage 1 as planned does not use them.

**Stage 1 total:** cannot be stated until the speech price is seen. The Claude part is bounded by whatever daily cap the PO sets.

### 3.5 Success measures (what "works" means; the PO may change the numbers)

Targets below are proposals to be fixed with `qa-engineer` and `ai-agent-engineer` before the run. They are not requirements already in the repo.
1. **Notes accuracy.** Use at least a small set of synthetic lectures with a written reference answer (key points, dates, terms). Record what share of reference points appear in the notes and how many statements are not supported by the transcript. Each note statement must trace back to a transcript timestamp (`CLAUDE.md` MVP definition).
2. **Transcript quality.** Word error rate on the synthetic or licensed audio against its known script, for at least a clean and a noisy sample.
3. **Latency.** Time from stopping to speak to the transcript line appearing; time from clicking Quick recap to the recap showing. Report the median and the slowest runs. The repo has no published vendor latency figure (`azure-platform-verification.md` 2.2), so these are measured, not assumed. The measurement protocol file `MEASUREMENT_PROTOCOL.md` is referenced by `stt-vendor-terms-review.md` M-1; check where it lives before use.
4. **Cost per session.** Real token and speech-hour counts times the price the PO sees; compare with the Section 3.4 estimates.
5. **Cap works.** A test where the daily cap is set very low shows that calls stop ("BudgetLimited") and that the cap holds after an app restart. The existing design ends a press as `BudgetLimited` before any call if the estimate does not fit (`docs/API_CONTRACTS.md`, line 235).
6. **Throttle behaviour.** What happens at the 40 requests / 40,000 input-token limits: a clear message, no loop of retries.
7. **No raw content in logs.** Search app logs and any Azure diagnostic logs for a planted unique phrase from the synthetic material; the count must be zero.
8. **Data stays in the agreed services.** Network review shows traffic only to the chosen Azure speech and Claude endpoints (plus Google only if the PO has separately approved a sign-in test, PO-20an). Any other destination fails the stage.
9. **Foundry questions settled with evidence:** Sonnet 5.5 US-only availability, the thinking-parameter contradiction (T-2), the real quota and price screens, and what the terms screens say (`azure-platform-verification.md` section 4 and 6).

### 3.6 STOP conditions

Stop and switch back to mock mode if any of these happen:
- Anyone proposes using real lecture audio, a real syllabus or another person's voice or document.
- The daily cap is reached, or spend appears that the app's own counter does not explain.
- Any raw content appears in a log, an error report or a screenshot.
- Traffic goes to a service the PO did not approve.
- Terms change under the account (Anthropic can change terms on 30 days' notice, `anthropic-terms-review.md` F-L1; a banner on the account already says an Acceptable Use Policy update takes effect November 12, 2026, `azure-platform-verification.md`, evidence section).
- The PO is unsure about anything consent-related. Doubt means stop.

### 3.7 Safety

- A visible switch returns the app to mock mode at once.
- Pause and Stop already exist in the lecture flow (see `docs/PROGRESS.md`); the pilot checklist must confirm they close the speech connection and cancel any Claude call in flight (`docs/API_CONTRACTS.md`, line 223).
- Delete removes Tether's copy. It does **not** recall what a vendor already received, and does not delete anything already exported to Google (PO-15c; `docs/API_CONTRACTS.md` line 223). For Stage 1 this is harmless because the content is synthetic.
- Export: "export all my data" and "delete my account" are parked until before real use (PO-20ax). They are not needed for synthetic data. They are needed before Stage 2.

### 3.8 Exit criteria to Stage 2

All of these, written down with evidence:
1. Every Section 3.5 measure was run and recorded, including the failures.
2. The cap test passed, and the PO has seen the actual Azure bill for the period and it matches the app's counter within a range the PO accepts (D1).
3. Speech price and Claude price are known from the PO's own portal, not only from list prices.
4. The questions the Anthropic review says must be answered before real content is sent are answered in writing by the PO or counsel: F-M1 (individual, non-commercial customer under terms that say "not for consumer use"), F-M2 (third-party content), F-M4 (zero data retention) and F-M9 (age and minors) (`docs/legal/anthropic-terms-review.md`, recommendation; this is its list, I have not re-judged it). For Stage 2 content that is the son's own, F-M2 is mainly about not pasting others' material.
5. "Export all my data" and "delete my account" are built and tested (PO-20ax).
6. The PO gives a written Stage 2 "go" (D4).

### 3.9 Gates touched

- **G2 (vendor and cost selection): UNVERIFIED.** Stage 1 produces the evidence G2 asks for ("measured authorized pilot"). It is still not PASS until a named human reviewer signs it.
- **G5 (production readiness): BLOCKED / NOT_VERIFIED.** Nothing is deployed in Stage 1; it does not move.
- **G1: BLOCKED.** Untouched because no real lecture audio is used.
- G3 NOT_RUN, G4 NOT_RUN.

---

## 4. Stage 2: the son uses it on his own study material

**Goal.** See whether Tether is actually useful and comfortable for him, using only things he wrote or made himself, with no lecture audio.

### 4.1 What counts as "his own material"

Allowed: notes he typed, questions he wrote, practice questions he made himself, his own plans.
Not allowed: instructor slides, handouts, exams, textbook text, syllabi copied from a school, recordings of anyone, anyone else's writing. Reason: copying instructor documents raises copyright and contract questions (L-10, `docs/legal/legal-register.md`; PO-20l says "synthetic or own documents only until a policy exists"), and Anthropic's terms make the customer responsible for rights to what it submits (`anthropic-terms-review.md` F-M2).

### 4.2 Entry conditions

1. Stage 1 exit criteria met and the PO's written "go" (D4).
2. Section 3.8 item 4 answered, especially F-M1 and the age question. Note: the repo holds conflicting hints about the son's age: L-09 says "the only user is the PO's son, an adult" (`legal-register.md`), while `stt-vendor-terms-review.md` M-2 says the student's age is "not in this repo". The PO should confirm (D7).
3. Export-all and delete-account work (PO-20ax).
4. The Google account to be used, if any, is personal, not a school-managed one (PO-15a, PO-20w). A real Google sign-in needs its own explicit PO go and the sign-in permission scope settled first (PO-20an, PO-20ap). Google export is optional and off unless he asks for it.
5. A plain-language warning is shown to him before first use of the real services: his text is sent to Claude on Azure; Anthropic keeps inputs and outputs for up to 30 days by default and up to 2 years if its safety systems flag them (`anthropic-terms-review.md` S3 and F-M3; the 30-day figure for Foundry is an inference, `azure-platform-verification.md` 1.3); deleting in Tether does not delete those copies. The wording is a DRAFT for the PO to approve, not counsel-approved (see section 9).
6. He has agreed, freely, to try it, and knows he can stop at any time without giving a reason.

### 4.3 What is built or changed

- Real Claude on his own text. Speech-to-text is not used; the microphone stays off in this stage.
- Quiet mode and optional engagement stay on by default (`CLAUDE.md` invariant 7). No passive attention tracking and no diagnosis.
- Practice questions are marked as AI-estimated, not answer-key certain; a single miss is not a "gap" (`CLAUDE.md` invariant 6).
- A simple, private way for him to report "this was wrong / this felt bad" that does not need a screenshot of his notes.
- Hosting: still on the PO's machine unless the PO decides otherwise (D6). Deployment to Azure is not authorised and would need G5 work first.

### 4.4 What the PO decides or does

- Written go for Stage 2 (D4); confirm his age (D7).
- Approve or edit the warning wording (D8).
- Decide who may look at his notes. Default proposal: no one, not even the PO, unless the son chooses to share. This is a family decision outside the repo; it is listed so that it is decided on purpose.
- Raise or keep the daily cap (D1).

### 4.5 Cost estimate (ESTIMATES)

The same Claude list prices as 3.4 apply. Typed notes and practice questions are text only, so there is no speech cost in this stage. Use the cost per session measured in Stage 1 times the number of sessions the PO expects per day, and compare with the daily cap. Example of how to read it: if Stage 1 measured $X per session, a day of N sessions costs N x X. I give no numbers here because none have been measured. Haiku 5.5 is about 20 times cheaper than Sonnet 5.5 on list input price ($0.10 versus $2 per MTok) and the same ratio on output ($0.50 versus $10); that ratio is arithmetic from the list prices.

### 4.6 Success measures

Proposals; the PO and `human-research-coordinator` should confirm.
1. He uses it of his own accord on the days he planned to, and says whether it saved effort or added it (his own words, recorded by him or the PO with his permission).
2. Notes and recaps are judged accurate by him against his own source text; every wrong or invented statement he finds is logged and counted.
3. Quick recap or note generation completes within the latency range measured in Stage 1; any longer wait is logged.
4. Cost per day stays at or under the cap; actual Azure invoice matches the app counter.
5. Zero raw note content in logs (re-run the Stage 1 planted-phrase test on the real stack).
6. No stress signal he reports lasts: he can stop and delete at any time and it works the first time.
7. Export of all his data and delete-everything are exercised once on purpose, with a check that the data is gone from Tether and that the PO understands what remains elsewhere (vendor copies, Google).

### 4.7 STOP conditions

- He says he wants to stop, is upset, anxious or overwhelmed by the tool, or feels watched. Stop that day; revisit only if he asks.
- Anyone else's material or voice shows up in the data.
- Cost over the cap, or a bill the counter cannot explain.
- Any data leaves the agreed services (Azure speech/Claude, and Google only if he chose to export).
- The tool seems to give wrong information that he might act on (for example a wrong deadline). Dates are never written to a calendar without his review (`CLAUDE.md` invariant 4).
- Anyone wants to turn this into a claim about learning outcomes, disability or health. Stage 2 is personal use, not a study (G4).
- Any consent doubt.

### 4.8 Safety

- He can pause or stop any session and use "delete everything" at any time. Deleting in Tether does not remove vendor copies (up to 30 days at Anthropic, longer if flagged) or anything already exported to Google (PO-15c). This must be said to him in plain words.
- Export of all his data must work before this stage starts (PO-20ax).
- Content-free evidence rows (consent, audit, AI usage) stay after a session or course delete and are removed on full account erasure (PO-20aw). These rows hold no note text.

### 4.9 Exit criteria to Stage 3

Stage 3 is not a normal "next step". It cannot start from Stage 2 results alone. It needs the G1 evidence in section 5, plus a fresh written PO go (D4) and the PO's confirmation that Stage 2 raised no unresolved harm.

### 4.10 Gates touched

- **G2: UNVERIFIED.** Real-account cost and quota evidence accumulates; still not PASS.
- **G3: NOT_RUN.** Automated checks only exist today. If he uses a screen reader or other assistive tool, a human test of that is G3 work and must be arranged separately (PO-20al defers a real screen-reader test to when G3 work is authorised).
- **G4: NOT_RUN.** His use is personal, not a validated study. It must not be described as clinical or as evidence of effectiveness. G4's own evidence rule: a documented voluntary study with expert review (`RELEASE_GATES.md`).
- **G5: BLOCKED / NOT_VERIFIED.** Local use only; no deployment.
- **G1: BLOCKED.** No lecture audio.

---

## 5. Stage 3: live lecture recording (BLOCKED)

**Status: BLOCKED by gate G1.** No date, no estimate and no build plan are given here on purpose.

**Why it is blocked (`docs/RELEASE_GATES.md`, G1; `docs/legal/recording-compliance-gap-report.md`).**
- No consent or recording control is built in code; the consent items in the security docs are design only.
- Applicable recording law, school policy, FERPA applicability, third-party voices and age are unreviewed.
- The recording gap report is by a substitute author and is not legal advice.
- Speech vendors put the duty to obtain permission on the operator: Azure says the customer must "obtain all necessary permissions" and that audio may be regulated by communications laws (`stt-vendor-terms-review.md` C-1).

**What the PO has said so far.** PO-16 records a pop-up where the student attests that the teacher consented, and says plainly that this does **not** clear G1; it is "a PO risk acceptance for personal use". PO-20ai records that no counsel or campus contact is named yet and the project stays mock-only and personal until the scope changes. PO-20ar parks the audio-stopped wording until real recording is planned.

**What would have to be true before Stage 3 is even discussed (all human tasks, none done):**
1. The school's recording policy has been read by the PO and, as the gap report says, confirmed with the school and, where required, the accessibility office (gap report section 4).
2. The instructor has given permission for this use, in writing.
3. Local law has been checked by a qualified person. The PO has said no counsel is named (PO-20ai); naming one or deciding to proceed without one is a PO decision to be recorded, not assumed.
4. Final consent wording is approved. The current text is a draft for counsel (gap report section 6).
5. The PO has made decisions D1, D2, D8 and D10 as the gate register lists them (`RELEASE_GATES.md`, G1 row). I have not found the text of these four decisions in the files I read; see section 12.
6. A named accountable reviewer exists for G1 (the register shows `unassigned`).
7. Speech vendor terms for live audio are reviewed: training on audio is UNVERIFIED for Azure Speech; the Microsoft DPA and subprocessor list are unread; diarization stays off (`stt-vendor-terms-review.md`; `azure-platform-verification.md` 2.2).
8. Real-device and classroom-audio testing is planned (G3), and neurodivergent suitability checks are planned (G4).

Until every item has attached evidence, G1 stays BLOCKED and the app must refuse real recording. This plan does not authorise any shortcut.

---

## 6. Cost summary (estimates only)

| Item | Figure | Source | Status |
|---|---|---|---|
| Sonnet 5.5 | $2 input / $10 output per MTok | `azure-platform-verification.md` 1.7 (A9), PO-20bd | Official list price; account price UNVERIFIED |
| Haiku 5.5, prompts up to 100k tokens | $0.10 input / $0.50 output per MTok | same | same |
| Haiku 5.5, prompts over 100k tokens | $0.50 / $2.50 per MTok | same | same |
| Data Zone (US) multiplier | 1.1x | same | same |
| Billing | Hourly, invoiced monthly in arrears, no prepaid credits, $0.01 per CCU | same | Official |
| Speech-to-text | Price not known | `azure-platform-verification.md` 2.2 | **UNVERIFIED** |
| Speech free tier | 5 audio hours per month; F0 allows 1 concurrent stream | same | Official, re-read live |
| Existing account budget | $5 USD per month, alerts at $3 and $5, email only, expires 2026-12-31 | PO-20bd | Alerts only |
| Hosting, database, Key Vault, logging | No price known | `azure-platform-verification.md` 9 | **UNVERIFIED**; not used in Stages 1 and 2 as planned |

---

## 7. Safety rules that apply in every stage

1. One switch back to mock mode.
2. Pause, stop and delete exist and are checked, not assumed.
3. No raw lecture or note text in logs by default (`CLAUDE.md` invariant 10).
4. The AI never receives sign-in tokens and cannot make unvalidated outside calls (`CLAUDE.md` invariant 3).
5. Syllabus dates are reviewed by the student before any Calendar write; unknown dates stay uncertain (`CLAUDE.md` invariant 4).
6. Quiet mode and optional engagement; no attention surveillance; no diagnosis (`CLAUDE.md` invariant 7).
7. The PO or the son can stop at any time, with no reason needed.
8. A STOP condition ends the session first and is discussed afterwards.

---

## 8. One-page risk table

Likelihood and impact are my plain judgement for a one-user personal tool (calibrated as the playbook says, `docs/agents/project-playbook.md` item 9). They are not measured.

| # | Risk | Stage | Likelihood | Impact | What reduces it | Source |
|---|---|---|---|---|---|---|
| R1 | Recording someone without permission | 3 | Low now (blocked) | High | G1 stays BLOCKED; no audio in Stages 1 and 2 | `RELEASE_GATES.md` |
| R2 | Unexpected Azure/Claude bill | 1, 2 | Medium | Medium | App-side daily cap, tested; Azure alerts; start on Haiku; low quota | `azure-platform-verification.md` 1.7 |
| R3 | Terms the PO has not read or cannot judge (Anthropic via Marketplace; individual buyer unclear) | 1, 2 | Medium | Medium | PO reads terms before accepting; F-M1 answered in writing | `anthropic-terms-review.md`; `azure-platform-verification.md` 1.5 |
| R4 | Vendor keeps or trains on data longer than the PO expects | 1, 2 | Unknown | Medium | Synthetic content in Stage 1; plain warning in Stage 2; consider zero-data-retention request (Anthropic approval needed) | `anthropic-terms-review.md` F-M3, F-M4; `azure-platform-verification.md` 1.3 |
| R5 | Raw content leaks into logs or error reports | 1, 2 | Medium | High | Metadata-only logging; planted-phrase test; diarization and SDK logging off | `CLAUDE.md` invariant 10; `azure-platform-verification.md` 3.4 |
| R6 | Wrong AI notes or dates acted on | 1, 2 | Medium | Medium | Source timestamps on notes; student review before any calendar write; accuracy measure | `CLAUDE.md` invariants 4 and 6 |
| R7 | The tool adds stress for the son | 2 | Unknown | High to him | Quiet mode, optional use, stop at any time, STOP rule on distress | `CLAUDE.md` invariant 7 |
| R8 | Quota too low (40 requests/min, 40,000 input tokens/min) for rolling summaries | 1 | Medium | Low (slow, not harmful) | Measure in Stage 1; Haiku for rolling summaries; quota request not guaranteed | `azure-platform-verification.md` 1.7 |
| R9 | Region/availability surprise (Sonnet 5.5 US-only; thinking-parameter contradiction) | 1 | Medium | Low | Settle on the real portal during Stage 1 | `azure-platform-verification.md` section 4 |
| R10 | Delete does not reach vendor or Google copies | 2 | Certain by design | Medium | Say so in plain words; short-lived copies; avoid exporting unless wanted | PO-15c; `API_CONTRACTS.md` line 223 |
| R11 | Own notes contain someone else's content (slides, textbook) | 2 | Medium | Medium | Clear rule in 4.1; no uploads of school documents | L-10; PO-20l |
| R12 | Scope creep: friends or classmates want to use it | any | Low | High | Revisit all DEFERRED legal items first | PO-15; PO-20u |
| R13 | Unmaintained safeguards or drift between docs | any | Medium | Low | Re-read live vendor pages; keep register current | `azure-platform-verification.md` "How to read" |
| R14 | Claims of benefit (learning, disability) made without validation | 2 | Low | High | No claims; G4 stays NOT_RUN | `RELEASE_GATES.md` G4 |
| R15 | Account misuse (card on file, keys leaked) | 1, 2 | Low | Medium | Keys in local key store only, never committed; adult PO holds the subscription | PO-20ao; `stt-vendor-terms-review.md` M-2 |

---

## 9. Draft "plain warning" topics for Stage 2 (not approved text)

For the PO to turn into words. Counsel has not reviewed any of this (PO-15b: "counsel not consulted").
- What is sent: the text he enters goes to Claude on Azure, and nothing else leaves the computer, apart from an optional export to his own Google account.
- How long: up to 30 days at Anthropic by default, longer if flagged for safety (direct-API figures; Foundry stated period UNVERIFIED).
- Deleting in Tether does not delete those copies or anything already in Google.
- He can stop, pause and delete at any time and does not have to give a reason.
- The notes can be wrong; check anything important against his own source.

---

## 10. Decisions for the PO

| ID | Decision | Needed by | Repo status |
|---|---|---|---|
| D1 | **Daily spend cap amount** (and whether the cap is per day per user, with a separate monthly ceiling). Suggest a very small first number, chosen by the PO. The repo gives no amount; PO-20t left it a placeholder. | Before Stage 1 | Open |
| D2 | **Upgrade Azure to pay-as-you-go: yes or no** (desk item T-AZ-UPGRADE). Claude cannot run on the current free trial. | Before Stage 1 | **Unanswered** |
| D3 | Accept Marketplace / Anthropic terms himself, after reading them (or choose not to use Claude on Azure) | Before Stage 1 | Not authorised (PO-20an) |
| D4 | **Who decides stage changes** (proposal: the PO alone, in writing, one stage at a time; agents may recommend but never decide) | Now | Open |
| D5 | Region: West US or West US 3 (preference recorded), and Global versus Data Zone deployment | Before Stage 1 | Preference only (PO-20bd) |
| D6 | Where the app runs in Stages 1 and 2 (the PO's machine, or a deployed Azure setup). Plan assumes his machine; no deployment is authorised | Before Stage 1 | Open |
| D7 | The son's age, and whether any vendor age rule applies | Before Stage 2 | Repo has conflicting notes (section 12) |
| D8 | Approve or edit the plain warning wording for Stage 2 | Before Stage 2 | Draft only |
| D9 | **The school-rules check:** whether and when the PO will read the school's recording policy and ask the school (and instructor, and accessibility office if relevant). Not needed for Stages 1 and 2. Required before any Stage 3 talk | Before Stage 3 | Open (PO-20ai: no one named) |
| D10 | Whether a lawyer will ever be consulted, or the PO accepts the risk himself and records it | Before Stage 3 | Open (PO-20ai) |
| D11 | Whether to ask Anthropic about zero data retention (their approval, not a setting we control) | Before Stage 2 | Open |
| D12 | Whether Google sign-in/export is used at all in Stage 2 (separate explicit go needed) | Before Stage 2 | Not authorised (PO-20an) |
| D13 | Who may see the son's notes in Stage 2 | Before Stage 2 | Open (family decision) |

---

## 11. Open unknowns (all UNVERIFIED unless stated)

1. Azure Speech price per hour (amounts masked on the official page). `azure-platform-verification.md` 2.2.
2. Whether Azure Speech trains on customer audio; Microsoft DPA, subprocessor list and SLA not read. Same file, 2.2 and 9.
3. Real-time speech latency; no vendor figure exists in the repo. Same file, 2.2.
4. Whether an individual may buy Claude through Azure Marketplace on a card-backed account. Same file, 1.5.
5. The content of the terms behind `aka.ms/anthropic_tandc`, and which document wins in a conflict. Same file, 1.6.
6. Whether Foundry's retention for Claude is the 30 days stated for the direct API. Same file, 1.3.
7. Whether Data Zone in West US 3 is US-only, and the account's real quota and price after upgrade. Same file, CLI section.
8. Whether Microsoft keeps any Claude prompt data for abuse monitoring; retention of flagged content. Same file, 1.3 and section 4.
9. Whether the Foundry resource that exists also serves Speech, and Speech region/tier/quota. Same file, section 6 item 6.
10. Whether the repo's current Claude client obeys Foundry's parameter limits. Same file, 1.2 and 10.
11. Whether 40 requests/min and 40,000 input tokens/min suffice for rolling summaries plus Quick recap. Same file, 1.7.
12. Recording law, school policy, FERPA applicability and instructor permission. `recording-compliance-gap-report.md` section 3.
13. Neurodivergent suitability and learning-science effectiveness. `RELEASE_GATES.md` G4.
14. The son's age (section 10, D7).
15. The text of PO decisions D1, D2, D8, D10 as named in the G1 row of `RELEASE_GATES.md`: not found in the files I read (section 12).

---

## 12. Contradictions and gaps found while writing this plan

These are for the Lead and the PO. I changed no other file.
1. **Free account versus Claude.** `stt-vendor-terms-review.md` L-5 calls the Azure free account "the safest cost shape"; `azure-platform-verification.md` 1.5 and T-6 say Claude on Foundry does not work on free-trial or credit-only subscriptions. The newer file is right for Claude; L-5 is right only for Speech.
2. **Who the user is.** PO-16 says Tether will "probably be used only by the Product Owner"; PO-15 and this task say it is for his son. The legal ratings are calibrated "personal, non-commercial" either way, but the account holder, the "student" in consent wording and the age question depend on it.
3. **Son's age.** `legal-register.md` L-09 calls the only user "an adult"; `stt-vendor-terms-review.md` M-2 says the student's age is not in the repo.
4. **Storage direction in older documents.** `anthropic-terms-review.md` (lines 5 and 50) still says data is stored "in Google and AWS" and covers direct Anthropic and Bedrock only; PO-20aa/ab replaced that with Azure plus Google Docs. `azure-platform-verification.md` section 8 lists these fixes as not yet made.
5. **Sonnet 5.5 Data Zone.** Anthropic's page says Global only; Microsoft's pages list a Data Zone. The account's CLI check shows version 2 of both models offers Global and Data Zone, so Microsoft's pages hold for this account. The older text in `azure-platform-verification.md` 1.4 and T-1 still reads as CONTRADICTED/UNVERIFIED; the evidence section at the end of the same file supersedes it, but T-1 was not updated. Whether that Data Zone is US-only is still UNVERIFIED.
6. **Thinking setting (T-2)** is still contradicted between two Microsoft pages and can only be settled on a real deployment.
7. **Dates in the register.** `RELEASE_GATES.md` says "Last updated 2026-10-27" but its G5 row cites events of 2026-11-01. The register header is stale.
8. **PO-3 versus PO-20z.** PO-3 (Deepgram first) was superseded by PO-20z/PO-20aa (Azure first). PO-20z itself says PO-3 was "not changed" pending an answer, while PO-3's row now carries the updated note. Consistent in effect, untidy in text.
9. **Gate G1 decisions D1, D2, D8, D10** are named in `RELEASE_GATES.md` but I did not find their definitions in the documents I read (the PRD and security files were not all opened). The gate-register labels D1..D13 here are my own, not those. Do not confuse them.
10. **Stage 2 needs things marked "parked".** Export-all and delete-account are parked "until before real use" (PO-20ax). Stage 2 is real use of the son's own content, so those must be built first; the plan treats this as an entry condition.
11. **Consent pop-up (PO-16) versus the CLAUDE.md invariant** to confirm university rules before real use. PO-16 itself says it does not clear G1, so there is no conflict in status, but a reader could mistake the pop-up for clearance. This plan treats it as not clearing anything.
12. **Pilot key storage.** PO-20am says Key Vault for the token key "production only"; PO-20ao confirms the pilot keeps a local key store. Consistent; noted because Stage 1 and 2 rely on it.

---

## 13. What this plan does NOT authorise

- No real classroom or lecture recording, and no recording of any other person. Stage 3 is blocked by G1.
- No clinical, diagnostic, treatment or learning-outcome claim about the son or anyone else (G4 NOT_RUN).
- No production deployment, and no cloud hosting, database, Key Vault or Blob storage creation (G5 BLOCKED).
- No spend of any kind without the PO's written decision, including the pay-as-you-go upgrade, speech use and Claude use.
- No acceptance of Marketplace, Anthropic or any other terms by an agent (PO-20an).
- No real Google sign-in, Calendar write or Docs export without the PO's separate explicit go (PO-20an).
- No use of real school documents (syllabi, exams, slides) (PO-20l, L-10).
- No recruitment of other participants, no sharing with classmates, no research use (L-20, PO-20u).
- No marking of any gate as PASS from this plan, a checklist, a mock or a dry run (`CLAUDE.md`).
- No legal conclusion: nothing here says that any recording, vendor or use is lawful or compliant.

---

## 14. Glossary

- **Stage**: one step of the pilot; each needs the PO's written go.
- **Gate (G1 to G5)**: a checkpoint in `docs/RELEASE_GATES.md`. Passes only with evidence and a named human reviewer.
- **G1**: recording, consent and school policy. **G2**: vendor choice, cost and limits. **G3**: real-device, screen-reader and audio testing. **G4**: suitability for neurodivergent students and learning-science effectiveness. **G5**: production readiness (CI, scanning, monitoring, backups).
- **STT**: speech-to-text, turning spoken audio into written text.
- **Claude**: the AI model used for notes, summaries and recaps.
- **Sonnet 5.5, Haiku 5.5**: two Claude models; Haiku is the smaller, cheaper one.
- **Foundry / Azure AI Foundry**: Microsoft's service that hosts Claude on Azure.
- **Marketplace terms**: the buying terms shown by Azure when a Claude model is first deployed; Anthropic's own terms also apply.
- **Pay-as-you-go**: an Azure subscription billed for what you use. Claude needs this; a free trial does not work.
- **MTok**: one million tokens. A token is a small piece of text (roughly a short word or part of one).
- **CCU**: Claude Consumption Unit, how Azure bills Claude, $0.01 each.
- **Quota / RPM / ITPM / OTPM**: Azure's rate limits: requests per minute, input tokens per minute, output tokens per minute. They slow you down; they are not a spend cap.
- **Data Zone (US)**: an Azure deployment type meant to keep processing within the United States, at a 1.1x price multiplier.
- **Diarization**: labelling who is speaking. Kept off because it processes voice characteristics.
- **Quick recap**: one click to catch up on the last 2, 5 or 10 minutes.
- **TL;DR**: a short summary that updates as the session goes on.
- **Synthetic content**: made up or self-made material, or audio licensed for testing, with no real person's private content.
- **Mock mode**: the app using fake stand-ins instead of real services.
- **Zero data retention (ZDR)**: an arrangement where the AI vendor does not keep inputs or outputs; needs Anthropic's approval.
- **DPA**: data processing addendum, a contract on how a vendor handles personal data.
- **FERPA**: US law on student education records; whether it applies here is a question for counsel (L-03, deferred).
- **UNVERIFIED**: not confirmed in any source the repo holds.
- **Review Desk**: the PO's local site for answering decisions (`C:\Dev\ManagerReviewDesk`); T-AZ-UPGRADE is an item there.
