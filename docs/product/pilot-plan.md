# Tether pilot plan (staged, plain English) - revision 3

**Status: DRAFT. The PO has now answered the main questions on the Review Desk (2026-11-04/05, recorded in `docs/DECISIONS.md` PO-20bg onward), but no stage is started, nothing is built for Stage 1, nothing is deployed, no terms are accepted, no quota is requested and nothing is spent.** It is planning only. It is not legal advice and it clears no release gate. Revision 3 dated 2026-11-05 (revision 2: 2026-11-04). No lawyer, school, clinician or student has reviewed it. Answers recorded here are the PO's; they are not approvals of work, and each stage still needs its own written PO go (D4).

**Who this is for:** Tether is a personal, non-commercial tool for the PO's son, who is the only intended user (PO-15, `docs/DECISIONS.md`). Legal questions are kept in `docs/legal/legal-register.md` so they can be revisited if that ever changes.

**How to read the evidence tags.** Every fact names the repo file it came from. "UNVERIFIED" means the repo says it has not been confirmed. "Estimate" means arithmetic from figures in the vendor docs; it is not a quote. "Inference" means a reviewer's reading that no page states outright. Vendor pages were mostly read through a fetch tool that summarises, so a human must re-read the live page before relying on any number (`docs/vendors/azure-platform-verification.md`, "How to read this file"; `docs/vendors/pilot-plan-vendor-check.md`, section 0).

**Gate register today (`docs/RELEASE_GATES.md`):** G1 BLOCKED, G2 UNVERIFIED, G3 NOT_RUN (classroom audio part BLOCKED on G1), G4 NOT_RUN, G5 BLOCKED / NOT_VERIFIED. Gates passed: 0 of 5. Stage 3 is BLOCKED. Nothing in this plan may be reported as PASS.

**Azure sign-in for agents (PO-20bf).** Agents now use a **read-only Azure identity (Reader role only)**, with its secret kept outside the repo. Agents do not use the PO's own Owner sign-in. This is the PO's recorded decision; whether the old Owner session is fully gone from this machine, and whether the Foundry keys need regenerating, is still UNVERIFIED (security finding PPR-01, section 9, D17). Nothing in this plan lets an agent deploy, accept terms, create resources or spend.

---

## Changes made in revision 3 (PO answers, 2026-11-04/05)

Source: the PO's stored Review Desk answers and two screenshot reads (below). Each answer is recorded in `docs/DECISIONS.md` (PO-20bg to PO-20bn). Where the plan before this revision had a proposal, the PO's answer replaces it. Where the PO did not answer, the item stays open.

**PO answers merged**

- Governance (P-GOVERN): only the PO decides stage changes, in writing on the Review Desk, one stage at a time. Agents recommend; they never decide (D4 answered; C4 resolved).
- Where it runs (P-WHERE): Stages 1 and 2 on the PO's own machine only; nothing deployed (D6 answered).
- Who the user is (P-WHO-IS-USER): the PO's son, 18 or older; a parent holds the accounts (D7 answered; the PO-15 / PO-16 note in 12B item 2 is resolved).
- Visibility (P-VISIBILITY): only the son sees his notes; he shows the PO what he chooses (D13 answered; C2 partly resolved, see the own-account item).
- His own yes (P-STUDENT-YES): adopted. Asked once; "not now" is a complete answer (D14 answered).
- Switch (P-SWITCH): one way. He can turn real AI off in the app; only the pilot setup turns it on (D18 answered; C1 resolved).
- Lecture notes (P-LECTURE-NOTES): not yet. His own reflections and questions only (D21 answered).
- Risk handling (P-RISK-ACCEPT): the delete wording is fixed first; the PO makes a dated, named risk acceptance for the rest (D22 answered).
- Lawyer (P-LAWYER): a short consult first if cheap; otherwise the PO's written risk acceptance. The PO accepts terms himself; no agent does (D23 answered).
- Own account (P-OWN-ACCOUNT): the text he reads must say plainly that the PC and agents can technically read files; a separate Windows account for him is considered later (D24 answered in part).
- Conflicts (P-CONFLICTS): the plan's recommendations are used for C1 to C7 (section 12A).
- Caps (P-CAP): AI **$0.25 per day and $5 per month** (the PO's choice). The speech-minutes cap is now **confirmed** (PO-20bo, 2026-11-05): 10 minutes per session, 30 minutes per day, 120 minutes per month, Stage 1 fixture audio only (D1 and D16: both parts answered). No build is authorised.
- Foundry resource (P-FOUNDRY-RESOURCE): the PO first chose "delete it now", then corrected it (typed in session, 2026-11-05): "lets not worry about deleting the foundry resource". **In force: keep it for now, no action; revisit if the pilot is dropped** (D25, PO-20bm). Deletion, if ever wanted, needs the PO's own owner login; the agents' Reader identity cannot and must not do it.

**Screenshot reads (Lead read the screenshots; images are not in the repo)**

- Subscription: plan "Azure Plan", role Owner, Costs card "Pay-as-you-go | November", 0 costs, "$200.00 in credit - Expires December 3, 2026" with $200 available. So the upgrade is done and the credit remains until 2026-12-03. **UNVERIFIED: whether the credit applies to Claude bought through Marketplace.** Do not assume it does; plan for real charges against the $5 cap.
- PO notes: Foundry has no "management center" in the left panel, and "view pricing" links to the Anthropic-side pricing page. This is evidence for the vendor open question (who prices and contracts Claude on this account), not a conclusion (section 11 item 21).

**Removed or replaced**

- The old placeholder for the daily cap amount (D1) and the "numbers are the PO's" wording are replaced by the answered figures.
- The earlier mention of a "$60 a month" total of default caps stays only as the security finding it came from (PPR-02); it is not a cap.
- The proposal that the plain-language text say "Nobody but you" unqualified is replaced by the wording in section 9.
- Open-decision labels for answered items are updated in section 10; remaining open questions are in section 11 and the end of section 10.

**Not changed:** no gate moves; Stage 3 stays BLOCKED by G1; nothing is PASS.

---

## Changes made in revision 2

Source reviews: legal (`docs/legal/pilot-plan-review.md`, "L"), UX (`docs/ux/pilot-plan-review.md`, "F" and section numbers), vendor facts (`docs/vendors/pilot-plan-vendor-check.md`, "V"), security (`docs/security/pilot-plan-review.md`, "S" = its section 12 items, "PPR" = its findings). Where a finding is listed as not applied, the reason is given.

**Applied**

- L1 and V-licence: Stage 1 now needs a source-and-licence list for every audio and text file; no real person's voice without written agreement; no edited copies of real syllabi (3.2).
- L2: Stage 2 "his own material" replaced by the tighter definition; lecture-derived typed notes made an explicit decision (D21); new STOP for other people's personal details (4.1, 4.7).
- L3: age decision (D7) and F-M1/F-M9 answers moved to before the PO accepts any terms, which is before Stage 1 (3.1, 3.3, 3.8, 10).
- L4: a "read first" list with dated copies added before terms are accepted; the rule that no agent accepts terms is kept (3.3).
- L5: the Usage Policy update is a dated read-and-record task, not only a STOP; Covered Model/retention live check added to the warning work (3.3, 3.6, 9).
- L6: how deferred MEDIUM legal rows are handled for Stage 2 is now a decision (D22) (4.2).
- L8: the plan says plainly that the app-side cap is the only spend control that actually stops calls (3.1, 3.4).
- F1, F8: the student's own yes (D14) added; age decision comes before it; his "no" and "not now" are complete answers (4.2, 10).
- F2, F13: usage-based success measures removed; replaced by product measures plus his optional verdict (4.6).
- F3: STOP conditions in Stage 2 are things he does or says, not how he seems; the PO sees daily cost totals only, as the proposal in D15 (4.4, 4.7, 10).
- F4, F10, F11, F14: full plain-language text for him, reachable later from Settings, with words to avoid (section 9).
- F5, UX section 5: "Report this" specified as local-only, content-free, one tap, and listed as new work needing a contract and security review (4.3, D20).
- F6: delete-all and export-all proven on a seeded test account before Stage 2, never on his real notes (4.2, 4.6).
- F7, F9, F12: "afterwards" means only if he raises it; nothing about him goes into the repo; he can turn the AI service off himself (section 7).
- UX sections 6 and 7: G4 can/cannot-say list (4.10) and the "with him, not on him" checklist (4.2a).
- Data warning moved before the ask (4.2).
- V1: Standard real-time speech-to-text list price $1.00 per audio hour in West US and West US 3 (Azure Retail Prices API, read 2026-11-04); diarization about $0.30 more per hour is an inference (3.4, 6, 11).
- V3: Sonnet 5.5 Data Zone is contradicted between vendor pages; the plan does not rely on it; Haiku 5.5 is the planned US-only choice (3.1, 3.4, 12).
- V4: the difference between "Hosted on Azure" (v2) and "Hosted on Anthropic infrastructure" (v1) is stated and v2 is preferred (3.1, 3.4).
- V5: the quota is a rate limit and not a spend cap; the "ceiling" is not a true ceiling; the $5 budget comparison is added (3.4).
- V6: the "about 20 times cheaper" Haiku claim carries its 100,000-token qualifier (4.5).
- V8: "Acceptable Use Policy" renamed "Usage Policy", effective 2026-11-12; changes are UNVERIFIED (3.6).
- V9: click path and permissions added (3.3).
- V2, V10, plus quota notes: the upgrade is done (PO-20be); a post-upgrade quota of 0 is explained by hypotheses only (3.1, 3.4, 11).
- S1 (PPR-01): "agent Azure access closed" is the first entry condition and applies now (3.1).
- S2 (PPR-02, PPR-03): speech-minutes cap plus a global daily cap and monthly ceiling, enforced in the app and tested across a restart (3.1, 3.5, D16).
- S3 (PPR-06, PPR-10): interface names corrected to `ISpeechToTextProvider` and `IAiClient`; keys in a secret file outside the repo; the new guard replaces, and does not delete, today's refusal tests (3.2).
- S4 (PPR-07, PPR-08): Stage 1 audio comes only from fixture files fed by the server; the microphone never feeds a real provider in Stages 1 and 2; fixture manifest and synthetic interlock (3.2).
- S5: no real syllabus extractor in Stages 1 and 2 (3.2, 4.3).
- S6 (PPR-04, PPR-05): web server on 127.0.0.1 only; separate pilot compose project, database and password; agents never read pilot data (3.2, 4.2).
- S7: the switch is one-way (real to mock) (3.7, 7, D18).
- S8 (PPR-09): the Stage 2 typed-notes feature is listed as new build (4.3, D19).
- S9: B1V-01 fixed, SB-01 flags on the pilot database, SEC-PUBLIC revisited, keys rotated after Stage 1 (3.1a, 4.2).
- S10 (PPR-11): the warning must say the PO's machine and agents can technically read his notes unless the setup changes (section 9, D24).
- S11: the network check method is assigned to `devops-engineer` (3.5).
- Security Stage 1 and Stage 2 precondition checklists copied in, condensed, with their DONE / NOT DONE / UNVERIFIED tags (3.1a, 4.2b).

**Deliberately not applied, or applied only as a proposal**

- L7 and security "add PPR rows to the register": those are edits to other files (the legal register and the findings register). This plan only lists them for the Lead (section 12).
- Legal's recommendation to treat lecture-derived notes as not allowed: kept as the recommended answer to D21, not decided here.
- UX: "Nobody but you" in the student text: kept as UX wrote it, with security's contrary point attached; the two are flagged for the PO (section 12, C2).
- Security: Entra ID sign-in instead of keys: optional and not needed for a local pilot; not added.
- Security: Azure-side capacity backstops: UNVERIFIED; mentioned, not relied on.
- PPR-12 (a personal name in tracked docs) was already fixed in the last commit; nothing to do in this file.
- Any numbers for D16 are proposals; the PO chooses.

---

## 1. The plan on one page

| Stage | What it is | Data allowed | Real services | Gate effect |
|---|---|---|---|---|
| 0 (now) | Build and test on mock data | Mock and synthetic only | None | None; all gates unchanged |
| 1 | Try the real speech and Claude services on fake material | Fixture audio files fed by the server only (synthetic or licensed, listed with their licence); invented notes; no real syllabus extractor | Azure AI Speech, Claude on Azure | Produces evidence toward G2 (does not pass it) |
| 2 | The son uses it on his own study material | His own typed notes and his own practice questions. No audio, no microphone, no real speech. No other person's voice | Claude on Azure only, plus his real (own) text | Touches G2, G3, G4 lightly; passes none |
| 3 | Live lecture recording | Real lecture audio | Same | **BLOCKED by G1. This plan does not weaken it.** |

Each step needs a written PO "go" on the Review Desk, one stage at a time (section 10, D4; PO-20bg). The PO alone decides. In Stage 2 the son's own yes (D14) is a separate decision, asked once, and his "no" or "not now" ends it. No step starts because an earlier step "worked". As of revision 3 no stage is started.

---

## 2. Stage 0: mock data only (where we are now)

**Goal.** Keep building and testing the product without touching a real service.

**Where the project stands (`docs/PROGRESS.md`, Iteration 12).** Everything runs on mock or synthetic data. No real recording, AI, Google or Azure call exists in the code. The app refuses every real provider at startup today (security review, section 3.1: `AiOptions.cs`, `TranscriptModule.cs`, `SyllabusModule.cs`; read, not run). The Azure account was upgraded to pay-as-you-go and billing is live (PO-20be); nothing else is authorised, nothing is deployed and no terms were accepted (`docs/vendors/azure-platform-verification.md`; PO-20an).

**Entry conditions.** None; this is the current state.

**What is built or changed.** Nothing new is required for this plan. Work continues on the items already in `docs/PROGRESS.md`.

**What the PO decides or does.** Nothing for Stage 0 itself. The decisions in section 10 can be answered whenever the PO is ready.

**Cost.** None from the app. The account is now billing (PO-20be) but nothing is deployed and no Claude terms are accepted, so no usage is expected. The account budget of $5 USD a month sends email only (PO-20bd). The subscription shows a $200 credit expiring 2026-12-03; whether it applies to Claude bought through Marketplace is UNVERIFIED, so plan for real charges.

**Success measures.**
- The existing automated suites stay green.
- No real provider is called: the mock default stays on.

**STOP conditions.** Any real credential, real lecture audio or real student document appears in the repo, logs or test fixtures.

**Safety.** Mock data only, so nothing to pause or delete beyond normal test cleanup.

**Exit to Stage 1.** The PO has answered the "before Stage 1" decisions in section 10 and the Stage 1 entry list (3.1 and 3.1a) is met, including a tested spend cap.

**Gates touched.** None changed. G1 BLOCKED, G2 UNVERIFIED, G3 and G4 NOT_RUN, G5 BLOCKED.

---

## 3. Stage 1: real services on synthetic or licensed content only

**Goal.** Find out, with fake material, whether the real speech-to-text and Claude services are good enough, fast enough and cheap enough, and whether the app's safety limits really work. This is the "measured authorized pilot" that G2 asks for (`docs/RELEASE_GATES.md`, G2 row). It is not a student trial.

### 3.1 Entry conditions (all must be true)

1. **Agent Azure access is closed (PPR-01, applies now, not only at Stage 1).** Agents use only the read-only identity recorded in PO-20bf. The PO's own Owner sign-in is not cached for agents (`az account show` shows nothing signed in after PO-attended checks). If the Foundry keys could have appeared in any agent session, they are regenerated. Status: identity done (PO-20bf); the rest UNVERIFIED (D17). The auto-created Foundry resource is kept for now with no action (PO-20bm); because it stays, regenerating its keys remains a live question. The Reader identity cannot and must not change it.
2. **The upgrade is done.** The Azure account is pay-as-you-go (PO-20be); the Lead read the PO's screenshots: plan "Azure Plan", role Owner, Pay-as-you-go for November, 0 costs, a $200 credit expiring 2026-12-03 ($200 available). **Whether that credit covers Claude through Marketplace is UNVERIFIED**; assume it does not. Reason it mattered: Claude on Foundry needs a paid pay-as-you-go subscription; free-trial and credit-only subscriptions are not supported (`azure-platform-verification.md` 1.5; vendor check 2.1). **Quota after upgrade is UNVERIFIED.** The earlier CLI zeros were read on the free trial, where Microsoft's quota page lists 0 for every Claude row; pay-as-you-go default is 40 requests per minute, 40,000 uncached input tokens per minute and 8,000 output tokens per minute (vendor check 2.1). No page says how long reclassification takes. If it still reads 0 after at least a day: re-read the Quota page (reads only), then open an Azure support request. Do not accept terms or deploy "to test it".
3. **The age decision (D7) is recorded, and F-M1 and F-M9 are answered, before the PO accepts anything.** D7 is answered: the user is the PO's son, 18 or older, and a parent holds the accounts (PO-20bi). Re-check his age on the day each stage starts. The terms bind from first electronic consent or first access (legal review, section 3). F-M1 (an individual, non-commercial buyer under terms that say "not for consumer use") is **still open**; F-M9 (age and minors) is answered in effect by D7 but the PO records that he read the age clause. F-M2 and F-M4 stay on the Stage 2 list (3.8).
4. **The PO has read the terms and decided to accept them himself, in person (D3; PO-20bl: no agent accepts terms).** A short lawyer consult comes first if it is cheap; otherwise the PO writes a risk acceptance (D23). "Agree and Proceed" is a separate step from looking at the options, and nothing authorises it yet (PO-20an). The text behind `aka.ms/anthropic_tandc` has not been read by anyone in the repo (`azure-platform-verification.md` 1.6). See the "read first" list in 3.3.
5. **Spend is capped by the app, not only by Azure email.** The account budget is "$5 USD monthly, alerts at $3 and $5, email only" (PO-20bd), and Azure bills Claude hourly, invoiced monthly in arrears (`azure-platform-verification.md` 1.7). The app must enforce, before every call: (a) a **global daily AI cap across all pools and users** plus a monthly ceiling; (b) a **speech-minutes cap** (per session, per day, per month) stored in the database so it survives a restart. The app today has a per-user and per-pool AI cap but no global daily cap and no speech cap at all (security PPR-02, PPR-03; defaults add up to $60 a month against a $5 budget). **The app-side cap is the only spend control that will actually stop calls.** It is proven by a test across a restart (3.5). **Numbers (PO-20bk, P-CAP; PO-20bo): AI $0.25 per day and $5 per month. Speech: 10 minutes per session, 30 minutes per day, 120 minutes per month, confirmed by the PO (PO-20bo); not yet built.** The monthly ceiling is the tighter one: 30 days at $0.25 would be $7.50, so the app must refuse at $5 for the month. The app's caps sit at the PO's figures even though a $200 credit may exist; the credit is not assumed to cover Claude.
6. **Region chosen (D5).** The stated preference is West US or West US 3 (PO-20bd). That authorises nothing. **Sonnet 5.5 Data Zone is contradicted between vendor pages** (Anthropic says Sonnet 5.5 supports only Global Standard; Microsoft's pages list a Data Zone; the account's CLI check also showed one). **The plan does not rely on it.** Haiku 5.5 Data Zone is listed consistently by both vendors; actual portal options for this account are UNVERIFIED. Whether any Data Zone is US-only for this account is UNVERIFIED.
7. **Choose the hosting version on purpose.** Claude on Foundry has "Hosted on Azure" (v2: prompts and completions remain within Azure; only usage metadata and content flagged by Anthropic's safety systems go to Anthropic) and "Hosted on Anthropic infrastructure" (v1). The plan prefers v2. Microsoft's own pages disagree on whether v2 exists in Global Standard in these regions (vendor check section 0, row 4). Statements like "Claude on Azure" and "data stays in the agreed services" are only clearly true for v2.
8. **The Speech resource is created and its region noted.** Speech processes data in the region of the resource (`azure-platform-verification.md` 2.2). Whether the existing Foundry resource also serves Speech is UNVERIFIED. The auto-created Foundry resource is **kept for now, no action; revisit if the pilot is dropped** (PO-20bm). Its keys and any idle billing stay UNVERIFIED (see 3.1 item 1). A new resource, if any, is created only after a written go (D4) and never by an agent.
9. **Only the PO (an adult, and a parent holding the accounts) holds the Azure subscription and accepts terms** (`stt-vendor-terms-review.md` M-2). MFA on his sign-in and "PO is the only Owner" are PO-ATTESTED (PO-20bu), not independently verified.
10. **The Stage 1 safety checklist in 3.1a is met.**

### 3.1a Security preconditions for Stage 1 (condensed from `docs/security/pilot-plan-review.md`, section 9.1)

Tags: DONE means code or test was read, not run today. NOT DONE and UNVERIFIED are open.

| # | Precondition | Status |
|---|---|---|
| 1.1 | PPR-01 closed: read-only identity only, no cached Owner session | Identity DONE (PO-20bf); rest UNVERIFIED |
| 1.2 | MFA on the PO's Azure sign-in; PO sole Owner | PO-ATTESTED (PO-20bu, typed on the desk 2026-11-05); not independently verified |
| 1.3 | PO decisions D1/D16 (caps), D3 (terms), D4 (written go), D5 (region), D7 (age) | Partly: D2 done (PO-20be); D7, D4 answered; AI and speech caps answered (PO-20bk, PO-20bo; not built); D3 not done; D5 preference only (West US 3 with Data Zone preferred, PO-20bs); no stage "go" given |
| 1.4 | Key and endpoint in a secret file outside the repo, mounted as a compose secret and read by path | NOT DONE |
| 1.5 | Secret file names are git-ignored | DONE |
| 1.6 | gitleaks recognises the Azure key shape (custom rule, proven with a fake key) | UNVERIFIED |
| 1.7 | `.env.example` corrected: real setting names, no user-secrets advice | NOT DONE |
| 1.8 | CI holds no provider secret | DONE |
| 1.9 | Test: no provider key or endpoint in the web bundle | NOT DONE |
| 1.10 | Mock and replay are the defaults; non-mock refused today | DONE |
| 1.11 | New guard (3.2 below), with replacement tests | NOT DONE |
| 1.12 | AI cap checked before every call and held across restart (mechanism) | DONE |
| 1.13 | Restart test for the cap | NOT DONE |
| 1.14 | Global daily AI cap sized to D16; prices set at the highest rate that can apply | NOT DONE |
| 1.15 | Speech minutes cap, database-backed, tested | NOT DONE |
| 1.16 | Provider cap 429 is terminal (contract DONE); adapter behaviour | Adapter NOT DONE |
| 1.17 | Existing log canaries pass | DONE (authored; CI per playbook) |
| 1.18 | B1V-01 fixed; canaries for the adapters' new log categories and error paths | NOT DONE |
| 1.19 | Speech SDK audio/file logging off and diarization off, asserted by a test | NOT DONE |
| 1.20 | No Azure diagnostic settings shipping request/response content | UNVERIFIED |
| 1.21 | Real speech accepts server-side fixture audio only; microphone path to the real provider refused | NOT DONE |
| 1.22 | Fixture manifest with planted phrases; real AI accepts registered fixtures only | NOT DONE |
| 1.23 | Syllabus extractor stays mock | DONE today; keep it |
| 1.24 | Web dev server bound to 127.0.0.1 (API and database already loopback) | Web NOT DONE |
| 1.25 | Separate pilot compose project, volume and database password | NOT DONE (recommended for Stage 1) |
| 1.26 | Pause and Stop close the real speech stream and cancel an in-flight Claude call | UNVERIFIED for real adapters |
| 1.27 | Adapters keep TLS certificate validation on | NOT DONE (no adapter) |
| 1.28 | Dependency and licence check of each new SDK package | NOT DONE |
| 1.29 | Kill-switch and key-rotation steps written in the runbook | NOT DONE |
| 1.30 | Public repo: no images, endpoints, resource names or subscription ids committed | DONE as of the review |
| 1.31 | `NEXT_TELEMETRY_DISABLED=1` for the pilot (PPR-13) | NOT DONE |

### 3.2 What is built or changed

- **Real providers go behind the interfaces that exist:** speech behind `ISpeechToTextProvider`, Claude behind `IAiClient`. (Earlier drafts named `ISttProvider` and `ILlmProvider`; those do not exist.) Mock stays the default; real is switched on only by explicit configuration.
- **New guard, replacing today's blanket refusal.** Today every real provider is refused unconditionally. The pilot needs a guard with: one explicit stage setting (absent means Stage 0, mock/replay only); every test environment refuses every real provider; a real provider needs an explicit name, endpoint and key from the secret file, all AI caps and prices set explicitly (at the highest rate that can apply, including the 1.1x Data Zone multiplier and Haiku's over-100k tier), the new global daily cap and speech caps, and a ceiling check that refuses start if any cap or the sum exceeds the PO's ceiling; no key in any message, log or exception; one metadata-only startup line; a visible "Pilot: synthetic material only" marker. **The existing refusal tests are replaced by tests for the new conditions, not deleted** (playbook rule on never weakening tests).
- **Stage 1 audio comes only from fixture files fed by the server.** The server streams an allow-listed fixture file through the real speech provider. The browser microphone path to a real provider is refused in Stages 1 and 2, because a microphone also picks up whoever is in the room, and that is another person's voice sent to a vendor (G1 territory) (PPR-07).
- **Synthetic-only is enforced, not only intended.** A fixture manifest outside the source tree holds, for each fixture: id, origin, licence, SHA-256 and a unique planted phrase. In Stage 1 the real AI path accepts only registered fixtures. The planted phrases double as the log test (PPR-08).
- **Source-and-licence list.** Every audio or text file used in Stage 1 is on a list with source, licence, and who is heard; only listed files may be used. The licence must allow copying to a cloud service and AI processing. Prefer text-to-speech or voices of people who agreed in writing; a published lecturer's voice is still a real person's voice. Self-made audio only features people who agreed. Do not commit licensed audio to the public repo unless the licence allows it.
- **No real syllabus extractor in Stages 1 and 2.** The extractor and the calendar provider stay mock. Fake syllabi may be used only with the mock extractor, so the real AI never sees them. Reason: the server does not yet check a real extractor's dates and weekdays (SBV-19 to SBV-22); a measurement would report checked results that are not checked. If the PO ever wants the real extractor in Stage 1, those four findings are fixed first.
- **The Claude client follows the Foundry limits:** no `temperature`, no `top_k`, `top_p` only 0.99, no Batches or Files API (`azure-platform-verification.md` 1.2, 1.8; vendor check section 2). Whether the current code complies is UNVERIFIED; `ai-agent-engineer` checks.
- **Speech:** diarization (speaker labelling) stays OFF and the Speech SDK's own logging stays off, with a test that the adapter sets them (`stt-vendor-terms-review.md` H-3, M-7).
- **Logs hold metadata only** (counts, timings, cost), never raw transcript or note text (`CLAUDE.md` invariant 10). B1V-01 (provider-scoped log config re-enables content categories) is fixed first, and the new adapters' log categories get canary tests.
- **Keys live in one secret file outside the repo**, mounted into the API container as a compose secret file and read by path. Not in `.env` inside the repo (readable by agents and visible to `docker inspect`). Nothing provider-related is ever `NEXT_PUBLIC_`. Rotate after Stage 1, before Stage 2, and at once after any exposure. Azure Key Vault is production only (PO-20ao).
- **The app runs on the PO's machine only.** No Azure hosting, database or Key Vault is created for this stage (PO-20ao, PO-20ba). The web server is bound to 127.0.0.1 and the son uses the PO's machine itself, not another device on the home network (PPR-04). A **separate pilot compose project, volume, database and non-default password** keeps pilot spend and data out of the dev database that tests and agents share (PPR-05). Where the app runs is answered (D6, PO-20bh): Stages 1 and 2 on the PO's own machine only; nothing deployed.
- **Agents never read pilot data**, logs or screenshots of it. Claude Code deny rules for the pilot container, volume and secret file are the PO's to set (UNVERIFIED whether any exist).
- **Public repo hygiene.** No endpoints, Azure resource names, subscription or tenant ids, invoices or screenshots are committed; portal-generated names can contain a person's name.

### 3.3 What the PO must decide or do himself

- Answer D7 (age) and the F-M1/F-M9 questions, then read before accepting anything. **Read-first list (save a dated copy of each):**
  1. The Azure Marketplace terms screen shown at deploy time, in full: who the contracting parties are and what the "industry" choice means for the PO (answer truthfully; if nothing fits, that is itself a question).
  2. The text behind `aka.ms/anthropic_tandc`, compared with `anthropic.com/legal/commercial-terms`.
  3. In those: the consumer/business restriction, any age clause, the customer warranty and indemnity (the PO becomes responsible for what is sent, including anything the son types), limits on liability, governing law and arbitration, suspension and termination, retention periods and whether they apply on Azure, subprocessors.
  4. Microsoft's Product Terms and Data Protection Addendum for the Foundry and Speech resources, and Anthropic's supported-regions policy for his billing country.
  5. The **Usage Policy** in force that day, and what takes effect 2026-11-12 (3.6).
  6. Whether the chosen model counts as a "Covered Model" (a live Anthropic support page listed other models on 2026-11-04; re-read it) and what retention follows.
  7. Whether to ask a lawyer first (D23).
- Click path, for the PO only: Foundry portal, Discover > Models > a Claude model > Deploy > Custom settings > read the Marketplace terms > choose an industry > **Agree and Proceed**. Alternative: subscribe to the "Claude Platform on Foundry" offer in Azure Marketplace. Permissions needed are in `models-from-partners` (Owner and Contributor include them). Browsing the catalogue does not require acceptance. No agent may click "Agree and Proceed" (PO-20an).
- Confirm the speech-minutes cap (D16; the AI caps are answered), confirm the region (D5), finish agent Azure access checks (D17), give a written "go" for Stage 1 on the Review Desk (D4). Whether to consult a lawyer first is answered as "short consult if cheap, else written risk acceptance" (D23); doing either is still his to do.

### 3.4 Cost estimate (ESTIMATES; list prices from the vendor docs, read 2026-11-04)

**Claude list prices per million tokens (MTok)** (`azure-platform-verification.md` 1.7; vendor check section 2, Anthropic pricing page read as raw text):
- Sonnet 5.5: $2 input, $10 output.
- Haiku 5.5: $0.10 input, $0.50 output, for prompts up to 100,000 tokens. Over 100,000 tokens: $0.50 input, $2.50 output.
- Data Zone (US) deployments carry a 1.1x multiplier on all token categories, including the over-100k tier. Sonnet 5.5 Data Zone is contradicted between vendor pages, so do not plan on it (3.1 item 6).
- Billing is in Claude Consumption Units at $0.01 each, after use, hourly metering, invoiced monthly in arrears, no prepaid credits.
- Cache writes cost more than base input and count toward the per-minute limit; cache reads are cheaper and do not.
- The price shown to this subscription in the portal is UNVERIFIED and may differ from list. The PO's note that "view pricing" leads to the Anthropic-side pricing page suggests price and terms may be set by Anthropic rather than Microsoft (evidence for the open vendor question, not a conclusion).
- **Credit.** A $200 credit expiring 2026-12-03 exists on the subscription. Whether it applies to Claude through Marketplace is UNVERIFIED. Plan for real charges against the $5 monthly cap.

**Arithmetic example (hypothetical token counts chosen only to show the sums; not measurements).** One test session using 500,000 input tokens and 50,000 output tokens (checked by the vendor researcher; arithmetic correct):
- Sonnet 5.5: $1.00 + $0.50 = about $1.50 (1.1x: $1.65, which may not be reachable).
- Haiku 5.5 (prompts up to 100k): $0.05 + $0.025 = about $0.075 (1.1x: about $0.083).

Rolling summaries re-send a transcript window each time, so input tokens will likely dominate. Stage 1 measures the real counts.

**Speech-to-text list price (new in revision 2).** Standard real-time speech-to-text is **$1.00 per audio hour** in West US and West US 3 (Azure Retail Prices API, raw JSON, read 2026-11-04; the web pricing page still masks the amount). A 30 to 60 minute run is about $0.50 to $1.00 at list. An enhanced add-on meter at **about $0.30 per hour** probably corresponds to diarization (**inference**, mapping unverified); diarization stays off, and on it would be about $1.30 per hour. Free (F0) tier: 5 audio hours a month shared by Standard and Custom, 1 concurrent stream, not adjustable. Whether a paid (S0) resource also gets the free hours is UNVERIFIED (assume not). Billing granularity (per second or per minute) is not documented, so short runs may round up. Account price is UNVERIFIED until the PO sees his own calculator or invoice. Speech quota defaults: 100 concurrent streams on S0; a quota increase may be refused for personal-email accounts (default far exceeds need).

**Quota is not a spend cap, and the "ceiling" below is not a true ceiling.** Pay-as-you-go default for both Claude models is 40 requests per minute, 40,000 uncached input tokens per minute, 8,000 output tokens per minute. Running flat out for an hour at those limits would be 2.4 MTok in and 0.48 MTok out: Sonnet 5.5 about $9.60, Haiku 5.5 about $0.48 per hour (arithmetic checked). Caveats: it ignores the 1.1x multiplier; cache reads do not count toward the limit, so a cache-heavy workload can move more input than that; bursts can use a minute's allowance at once; one Haiku prompt over 40,000 uncached tokens may itself be throttled (inference). At the Sonnet rate the account's whole $5 monthly budget is reached in about 31 minutes; at the Haiku rate in about 10 hours. The budget only sends email.

**Idle resources.** Nothing documents a standing charge for an empty Foundry resource, project or Speech S0 resource; billing is by usage. That is an **inference**, not a guarantee. Check Cost Management grouped by meter after one billing month. Log and alert add-ons can cost extra.

**Everything else:** no Azure hosting, database or Key Vault price is known (`azure-platform-verification.md` 9). Stage 1 as planned does not use them.

**Stage 1 total (estimate):** speech about $0.50 to $1.00 per 30 to 60 minute run at list, plus Claude bounded by whatever caps the PO sets (D16). Account prices still to be confirmed.

### 3.5 Success measures (what "works" means; the PO may change the numbers)

Targets below are proposals to be fixed with `qa-engineer` and `ai-agent-engineer` before the run. They are not requirements already in the repo. Stage 1 is synthetic, so per-session timings and counts here are fine; they are about the tool, not a person.
1. **Notes accuracy.** Use at least a small set of synthetic lectures with a written reference answer. Record what share of reference points appear in the notes and how many statements are not supported by the transcript. Each statement traces to a transcript timestamp (`CLAUDE.md` MVP definition).
2. **Transcript quality.** Word error rate on fixture audio against its known script, for at least a clean and a noisy sample.
3. **Latency.** Time from the audio being sent to the transcript line appearing; time from clicking Quick recap to the recap showing. Median and slowest runs. No vendor latency figure exists in the repo, so these are measured. `MEASUREMENT_PROTOCOL.md` is referenced by `stt-vendor-terms-review.md` M-1; check where it lives.
4. **Cost per session.** Real token and speech-hour counts times the price the PO sees; compare with the 3.4 estimates.
5. **Cap works.** With the global daily AI cap and the speech-minutes cap set very low, calls stop (`BudgetLimited`), and the cap holds after an app restart. The existing design ends a press as `BudgetLimited` before any call if the estimate does not fit (`docs/API_CONTRACTS.md`, line 235). The counter is an estimate; the invoice is the truth.
6. **Throttle behaviour.** At the 40-request / 40,000-token limits: a clear message, no retry loop (a spend-cap 429 is terminal in the adapter contract).
7. **No raw content in logs.** Search app logs and any Azure diagnostic logs for the planted phrase from each fixture; the count must be zero.
8. **Data stays in the agreed services.** Network review, **per container** (other traffic on the machine will otherwise appear), shows traffic only to the chosen Azure speech and Claude endpoints. Any other destination fails the stage. Method owner: `devops-engineer`; feasibility on Docker Desktop UNVERIFIED.
9. **Foundry questions settled with evidence on the real portal:** Sonnet 5.5 Data Zone, the thinking-parameter contradiction, v1 versus v2 availability, the real quota and price screens, what the terms screens say.

### 3.6 STOP conditions

Stop and switch back to mock mode if any of these happen:
- Anyone proposes using real lecture audio, a real syllabus, another person's voice or document, or a file not on the source-and-licence list.
- Any cap is reached, or spend appears that the app's own counter does not explain.
- Any raw content appears in a log, an error report or a screenshot.
- Traffic goes to a service the PO did not approve.
- Terms change under the account. Anthropic can change terms on 30 days' notice (`anthropic-terms-review.md` F-L1). Its **Usage Policy** (earlier called the Acceptable Use Policy) shows an effective date of 2026-11-12 and the Foundry account shows a banner about it. What changed is UNVERIFIED: only secondary coverage describes it, and no version history was retrieved. The PO reads the live policy and the banner before the first real call and keeps a dated copy. An agent never clicks through acceptance.
- The PO is unsure about anything consent-related. Doubt means stop.

### 3.7 Safety

- **The switch is one-way.** Stop the pilot compose project and start the normal dev stack (no secret file, no stage setting) and the app is back on mock. Nothing in the UI can turn a paid provider on. A runtime "AI off" control is fine because it can only reduce what is allowed. PO answer (P-SWITCH, PO-20bj): the son can turn real AI off in the app; only the pilot setup turns it on. The first-use "Turn the AI service on" button therefore records his permission inside a stage the server already allows; it never enables a provider. The two commands go into the runbook before Stage 1 (NOT DONE).
- Pause and Stop already exist in the lecture flow; the pilot checklist must confirm they close the speech connection and cancel any Claude call in flight (`docs/API_CONTRACTS.md`, line 223).
- Delete removes Tether's copy. It does **not** recall what a vendor already received, and does not delete anything already exported to Google (PO-15c). For Stage 1 this is harmless because the content is synthetic.
- Export-all and delete-account are parked until before real use (PO-20ax); they are not needed for synthetic data. They are needed before Stage 2.

### 3.8 Exit criteria to Stage 2

All of these, written down with evidence:
1. Every 3.5 measure was run and recorded, including the failures.
2. The cap test passed, and the PO has seen the actual Azure bill for the period and it matches the app's counter within a range he accepts (D16).
3. Speech price and Claude price are known from the PO's own portal, not only from list prices.
4. Written answers (PO or counsel) to the open vendor-terms questions: F-M2 (third-party content), F-M3 and F-M4 (retention, zero data retention). F-M1 and F-M9 were already answered before Stage 1 (3.1 item 3); re-confirm they still hold. These come from `docs/legal/anthropic-terms-review.md`; I have not re-judged them.
5. Export-all and delete-account are built and tested on a seeded test account (PO-20ax).
6. Keys are rotated.
7. The PO gives a written Stage 2 "go" (D4).

### 3.9 Gates touched

- **G2 (vendor and cost selection): UNVERIFIED.** Stage 1 produces the evidence G2 asks for. Not PASS until a named human reviewer signs.
- **G5: BLOCKED / NOT_VERIFIED.** Nothing is deployed; it does not move.
- **G1: BLOCKED.** Untouched, no real lecture audio and no microphone feeding a real provider.
- G3 NOT_RUN, G4 NOT_RUN.

---

## 4. Stage 2: the son uses it on his own study material

**Goal.** See whether Tether is actually useful and comfortable for him, using only things he wrote or made himself, with no audio. The plan is built to be **with him, not on him**.

### 4.1 What counts as "his own material"

His own material is text he composed in his own words, not copied, pasted, retyped or closely paraphrased from any instructor, textbook, slide, handout, exam, classmate or website. It contains no other person's name or personal details unless that person has agreed. When unsure, it does not go in.

Allowed: notes he typed in his own words, questions he wrote, practice questions he made himself, his own plans.
Not allowed: instructor slides, handouts, exams, textbook text, syllabi copied from a school, recordings of anyone, anyone else's writing, retyped or lightly reworded exam or textbook questions, shared or group notes, group-chat or email text, a friend's health or disability details.
**Decided (D21, PO-20bj): not yet.** A typed summary of a lecture he attended is not allowed. It is the closest thing to lecture content Stage 2 could hold. His own reflections and questions only, until counsel or the school says otherwise and the PO changes this in writing.
Reasons: copying instructor documents raises copyright and contract questions (L-10; PO-20l); Anthropic's terms make the customer responsible for rights to what it submits (F-M2); Anthropic's Usage Policy bars sharing others' personal information without consent; some courses have honour-code or confidentiality rules (UNVERIFIED for his school). His own sensitive details (his disability, accommodations, health) are his data, but they go to vendors; the warning in section 9 says so.

### 4.2 Entry conditions

1. Stage 1 exit criteria met and the PO's written "go" (D4).
2. D7 (age) was recorded before the terms were accepted: he is 18 or older (PO-20bi). If that ever turns out wrong, stop and get counsel's written view first (F-M9). If adult, the vendor age rules are not triggered by him as end user (legal inference).
3. The Stage 2 security checklist (4.2b) is met, including the new typed-notes feature, export-all and delete-account (proven on a seeded account, never on his real notes).
4. The Google account, if any, is personal, not school-managed (PO-15a, PO-20w). Google sign-in needs its own explicit PO go (D12); Google export is off unless he asks.
5. The deferred MEDIUM rows are handled as the PO decided (D22, PO-20bj): the L-07 delete dialog wording is **fixed first**; for the rest (L-08 retention policy, L-14, security row SEC-PUBLIC) the PO makes a **dated, named risk acceptance**, in writing, before Stage 2. Neither is done yet. The severity policy treats MEDIUM as blocking real users, so this acceptance is required, not optional.
6. **The plain-language text for him (section 9) is approved by the PO (D8) and shown to him first, before anyone asks him anything.** It is reachable later from Settings ("What happens to my notes"). The data warning comes before the ask.
7. **Only then, he is asked once (D14; adopted by the PO, PO-20bg).** He has said yes, freely. "Not now" and "No" are complete answers; he is not asked again. His yes can be withdrawn at any time by stopping, with no reason and no questions that day.
8. The PO has said, once and plainly: "This is my project, not your work. Using it or not changes nothing between us, and nothing about you is being measured or studied."

### 4.2a Check before the Stage 2 "go": is it with him or on him (from the UX review, section 7)

Each is a yes/no the PO answers in writing. Any "no" means the structure needs fixing first.
1. He read the section 9 text before anyone asked him anything.
2. He was asked once; "Not now" was a plain button and would have ended it.
3. He can turn the real service off himself, without asking anyone.
4. He can stop, and nobody asks why that day, or later unless he raises it.
5. Nobody sees his notes unless he shows them (D13 answered and told to him; see conflict C2).
6. He knows exactly what the PO can see (D15) and nothing else is collected about him.
7. No measure counts how often or when he uses it.
8. Every "report" goes to a list he controls, not to a person by default, and never to a vendor.
9. He will never be asked to delete his real notes to prove delete works.
10. Nothing about him is written into the public repository, an issue, CI output or the Review Desk.
11. The PO has said the one sentence in item 8 above.
12. Everyone involved knows Stage 2 proves nothing about neurodivergence, learning or health, and the plan says so in words he could read.

### 4.2b Security preconditions for Stage 2 (condensed from `docs/security/pilot-plan-review.md`, section 9.2)

Every Stage 1 item stays true, plus:

| # | Precondition | Status |
|---|---|---|
| 2.1 | Stage 1 exit evidence; invoice matched to the counter | NOT DONE |
| 2.2 | Real speech refused in Stage 2 by the guard (stage rule) | Replay-only DONE today; stage rule NOT DONE |
| 2.3 | Typed-notes workflow built with contract, own budget pool, user-scoped storage, deletion and export coverage, log canary, `noticeVersion` of the approved warning; security review passed | NOT DONE |
| 2.4 | Export-all and delete-account built and tested | NOT DONE |
| 2.5 | Separate pilot database, non-default password, SB-01 Postgres flags, export before and after sessions | NOT DONE |
| 2.6 | B1V-01 fixed; planted-phrase canary re-run on the pilot stack | NOT DONE |
| 2.7 | Agents denied the pilot container, volume, logs and secret file; no screenshots of his notes | UNVERIFIED |
| 2.8 | Device encryption on; he uses the PO's machine locally; web loopback-only | UNVERIFIED / NOT DONE |
| 2.9 | Warning covers vendor retention, that delete does not recall vendor copies, and that the PO's machine and agents can technically read his notes | NOT DONE |
| 2.10 | F-M1, F-M3, F-M4, F-M9 answered in writing; D7 and D11 decided | NOT DONE |
| 2.11 | SEC-PUBLIC revisited and recorded by the PO | NOT DONE |
| 2.12 | "AI-generated" label on output | UNVERIFIED |
| 2.13 | Syllabus extractor still mock | DONE today |
| 2.14 | Google off unless D12 | DONE today |
| 2.15 | Keys rotated after Stage 1 | NOT DONE |

### 4.3 What is built or changed

Two pieces are **new work that does not exist in the code today.** Each needs a versioned contract before building, a security review, and an owner (D19, D20). Neither is covered by Stage 1.

- **Typed-notes workflow (new build; D19).** Real Claude on his own typed notes and practice questions. Today's AI workflows are `RollingSummary, Compaction, QuickRecap, SyllabusExtract, Notes, FinalSummary` and none takes typed notes (security review, section 2). It needs a contract, its own budget pool, user-scoped storage, deletion and export coverage, a log canary, the approved warning's version recorded with each send, and a security review (PPR-09). It is an entry condition for Stage 2, not something done during it.
- **"Report this" (new build; D20).** A local-only, one-tap, content-free way to say an AI-written block was wrong or felt off, with no screenshot and no vendor call. Specification (UX review, section 5):
  - A plain "Report this" link at the end of every AI-written block (recap, summary group, answer, quiz feedback); not on his own text or verbatim transcript. Same place every time.
  - Opens three choices: "Not right", "Not useful", "Felt off". Optional one-line note, never required. Escape or Cancel saves nothing.
  - Stores block id, type, prompt version, choice, optional note, timestamp. **Not** the block text and **not** his source notes. User-scoped, same delete rules as everything else.
  - Goes to Tether's database only: no vendor call, no telemetry, no note text in logs. A test asserts zero outbound calls and no note text in logs. (Feedback or rating calls to the vendor are barred: `anthropic-terms-review.md` F-M10.)
  - After saving he sees "Saved." and nothing else: no thanks, no praise, no counter. No thumbs, stars, streaks or prompts after N uses.
  - A Settings page, "Things I reported", lists them; he can show or delete each. Nothing is sent to the PO unless he shows it. The same page holds his optional verdict question (4.6).
  - Accessibility: 44px targets, accessible name starts with the visible text, labelled choice group, focus returns to the link, no new live region.
  - Owners proposed: `frontend-engineer`, `backend-engineer`, `qa-engineer`; security review by `security-engineer`. The Lead decides the sequence.
- Speech-to-text is **refused** in this stage by the guard; the microphone is off. The syllabus extractor stays mock.
- Quiet mode and optional engagement stay on by default (`CLAUDE.md` invariant 7). No passive attention tracking and no diagnosis.
- Practice questions are marked as AI-estimated, not answer-key certain; a single miss is not a "gap" (`CLAUDE.md` invariant 6). AI-written output carries an "AI-generated" label.
- He can turn the AI service off for his own account from Settings, with one control, at once, with nothing deleted (see D18 for how this fits the one-way rule).
- Hosting: the PO's machine, pilot compose project, unless the PO decides otherwise (D6). Deployment to Azure is not authorised and would need G5 work first.

### 4.4 What the PO decides or does

- Written go for Stage 2 (D4); confirm his age (D7); approve or edit the section 9 text (D8).
- Who may see his notes is answered (D13, PO-20bg): only him; he shows the PO what he chooses. What the PO sees of his activity (D15) uses the plan's recommendation (PO-20bn): daily cost total and cap state only; the PO confirms when approving the text.
- Keep or change the caps (D16: AI $0.25 per day and $5 per month; speech cap proposed only).
- Say the one sentence in 4.2 item 8.

### 4.5 Cost estimate (ESTIMATES)

The same Claude list prices as 3.4 apply. Typed notes are text, so there is no speech cost in this stage. Use the cost per session measured in Stage 1 times the expected sessions a day and compare with the daily cap; no numbers are given because none have been measured. Haiku 5.5 is about 20 times cheaper than Sonnet 5.5 on list prices (input $0.10 versus $2, output $0.50 versus $10 per MTok) **only for prompts up to 100,000 tokens**; above that Haiku costs $0.50 / $2.50 and the ratio is about 4 times. Arithmetic from list prices (checked).

### 4.6 Success measures

Revision 2 removed measures that score his behaviour (days of use, errors counted as his job, stress tracking). Usage frequency is not a success measure and is not reported.

**4.6a Product measures (do not need him watched)**
1. **Accuracy.** He can mark any AI-written block "Not right" with one tap. The count of such marks per block type is a product measure, not a target for him; zero marks means nothing either way.
2. **Latency.** Quick recap and note generation complete within the Stage 1 range. Daily aggregates only.
3. **Cost.** Per day at or under the cap; invoice matches the counter. Daily totals only.
4. **Logs.** Zero raw note content in logs (planted-phrase test on the real stack).
5. **Delete and export.** Both were proven on a seeded account before the stage (4.2). If he uses delete during the stage, it worked the first time. Nobody schedules it or asks him to do it.
6. **Stop and the AI-off switch** each worked the first time he used them.

**4.6b His verdict (his words, his timing; optional)**
7. At any point, and at least once if he is willing, he answers one question in Settings: "Would you use this again?" with Yes / Not sure / No and an optional note. Stored in Tether only; his to show or not; never a prompt or banner.

### 4.7 STOP conditions

- He says stop, turns the AI service off, or stops using it and says so. Stop that day. Nobody raises it again unless he does. Not using it on any day or stretch is not a signal of anything and is never asked about.
- Anyone else's material or voice shows up. If another person's name or personal details are found in his notes, that is a STOP; see conflict C3 on how that is detected without anyone reading his notes.
- Cost over a cap, or a bill the counter cannot explain.
- Any data leaves the agreed services (Azure Claude, and Google only if he chose to export).
- The tool seems to give wrong information he might act on (for example a wrong deadline). Dates are never written to a calendar without his review (`CLAUDE.md` invariant 4).
- Anyone wants to turn this into a claim about learning outcomes, disability or health. Stage 2 is personal use, not a study.
- Any consent doubt.

### 4.8 Safety

- He can pause or stop any session and delete everything at any time. Deleting in Tether does not remove vendor copies (about 30 days at Anthropic by direct-API figures, longer if held for review) or anything already exported to Google (PO-15c). This is said to him in plain words (section 9).
- Export-all and delete-account must work before this stage (PO-20ax).
- Content-free evidence rows (consent, audit, AI usage) stay after a session or course delete and are removed on full account erasure (PO-20aw). These hold no note text. The PO sees daily totals only (D15), not per-session times.
- Honest limit: the pilot runs in the PO's Windows account, under his Docker, next to agents he runs, with a database password he holds. The PO can technically read every note (PPR-11). "No one, not even the PO" is therefore a family promise, not something the setup enforces, unless the setup changes (D24). PO answer (P-OWN-ACCOUNT, PO-20bn): the text he reads says this plainly; giving him his own Windows account is considered later and is not part of this plan's entry conditions.

### 4.9 Exit criteria to Stage 3

Stage 3 is not a normal "next step". It cannot start from Stage 2 results alone. It needs the G1 evidence in section 5, plus a fresh written PO go (D4) and the PO's confirmation that Stage 2 raised no unresolved harm.

### 4.10 Gates touched, and what Stage 2 can and cannot say

- **G2: UNVERIFIED.** Real-account cost and quota evidence accumulates; still not PASS.
- **G3: NOT_RUN.** If he uses a screen reader or other assistive tool, a human test is G3 work arranged separately (PO-20al).
- **G4: NOT_RUN.** G4 needs "a documented voluntary study with expert review" (`RELEASE_GATES.md`). Stage 2 is one person, in his own family, using a tool his parent built, with no protocol, no independent reviewer and no comparison. It clears nothing.
- **G5: BLOCKED / NOT_VERIFIED.** Local use only.
- **G1: BLOCKED.** No audio.

*Can be said, inside the project only:* "One user tried it on his own notes and chose to keep using it or stop" (design evidence, not a result); "N blocks were marked Not right and these prompt changes followed" (product evidence); latency, cost, log cleanliness, delete and stop working (engineering evidence toward G2 and G5 preparation, not PASS).
*Cannot be said anywhere:* anything about neurodivergent suitability, cognitive load, focus, attention, memory, executive function, learning, grades or wellbeing, positive or negative; anything about him as a person, any label or diagnosis; "validated" or "tested with a neurodivergent student"; that the project's wording choices are right because one person was comfortable with them; that Quiet mode "works" for neurodivergent students.
Human-only G4 tasks untouched by Stage 2: expert review by named clinicians and learning scientists; moderated sessions with consenting adult students under a written ethics determination; a named accountable reviewer (currently unassigned).

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
1. The school's recording policy has been read by the PO and confirmed with the school and, where required, the accessibility office (gap report section 4).
2. The instructor has given permission for this use, in writing.
3. Local law has been checked by a qualified person. No counsel is named (PO-20ai); naming one, or deciding to proceed without, is a PO decision to be recorded, not assumed.
4. Final consent wording is approved. The current text is a draft for counsel.
5. The PO has made the G1 decisions the register lists (`RELEASE_GATES.md`, G1 row, named D1, D2, D8, D10 there). Those are not this plan's D1 to D25 (section 12).
6. A named accountable reviewer exists for G1 (the register shows `unassigned`).
7. Speech vendor terms for live audio are reviewed: training on audio is UNVERIFIED for Azure Speech; the Microsoft DPA and subprocessor list are unread; diarization stays off.
8. Real-device and classroom-audio testing is planned (G3), and neurodivergent suitability checks are planned (G4).

Until every item has attached evidence, G1 stays BLOCKED and the app must refuse real recording. This plan does not authorise any shortcut.

---

## 6. Cost summary (estimates only)

| Item | Figure | Source | Status |
|---|---|---|---|
| Sonnet 5.5 | $2 input / $10 output per MTok | `azure-platform-verification.md` 1.7; vendor check 2 | Official list; account price UNVERIFIED |
| Haiku 5.5, prompts up to 100k tokens | $0.10 input / $0.50 output per MTok | same | same |
| Haiku 5.5, prompts over 100k tokens | $0.50 / $2.50 per MTok | same | same |
| Data Zone (US) multiplier | 1.1x on all token categories | same | Official; Sonnet 5.5 Data Zone itself contradicted, do not plan on it |
| Billing | Hourly, invoiced monthly in arrears, no prepaid credits, $0.01 per CCU | same | Official |
| Speech-to-text, Standard real-time | **$1.00 per audio hour**, West US and West US 3 | Azure Retail Prices API, read 2026-11-04 | List price; account price UNVERIFIED; billing granularity UNVERIFIED |
| Speech diarization add-on | About $0.30 per audio hour more | same | **Inference** (mapping unverified); stays off |
| Speech free tier | 5 audio hours per month; F0 allows 1 concurrent stream | Speech pricing and quota pages | Official summary; S0 eligibility UNVERIFIED |
| Claude default quota (pay-as-you-go) | 40 requests/min, 40,000 uncached input and 8,000 output tokens/min | Foundry quota page | A rate limit, not a spend cap; account value after upgrade UNVERIFIED |
| Existing account budget | $5 USD per month, alerts at $3 and $5, email only, expires 2026-12-31 | PO-20bd | Alerts only |
| Hosting, database, Key Vault, logging | No price known | `azure-platform-verification.md` 9 | **UNVERIFIED**; not used in Stages 1 and 2 as planned |
| Idle Foundry or Speech resource | No standing charge documented | vendor check 4 | **Inference**; confirm after a billing month |

---

## 7. Safety rules that apply in every stage

1. A one-way switch back to mock mode: real to mock only. Nothing in the UI can turn a paid provider on.
2. Pause, stop and delete exist and are checked, not assumed.
3. No raw lecture or note text in logs by default (`CLAUDE.md` invariant 10).
4. The AI never receives sign-in tokens and cannot make unvalidated outside calls (`CLAUDE.md` invariant 3).
5. Syllabus dates are reviewed by the student before any Calendar write; unknown dates stay uncertain (`CLAUDE.md` invariant 4). No real syllabus extractor runs in Stages 1 and 2.
6. Quiet mode and optional engagement; no attention surveillance; no diagnosis (`CLAUDE.md` invariant 7).
7. The PO or the son can stop at any time, with no reason needed.
8. A STOP condition ends the session first and is discussed afterwards. For the son, "afterwards" means when and if he brings it up.
9. The son can turn the real AI service off for his own account, in Settings, at once. Nothing is deleted when he does.
10. Nothing about the son (his words, reactions, usage, age, labels) goes into the repository, issue tracker, CI output or the Review Desk. Records about him stay on the PO's machine, under his control where they are about him.
11. Stage 1 audio comes only from server-fed fixture files; no microphone feeds a real provider in Stages 1 and 2.
12. The app-side caps are the only spend control that stops calls; Azure budgets only send email.
13. Agents use only the read-only Azure identity (PO-20bf) and never read pilot data.

---

## 8. One-page risk table

Likelihood and impact are plain judgement for a one-user personal tool (playbook item 9). They are not measured.

| # | Risk | Stage | Likelihood | Impact | What reduces it | Source |
|---|---|---|---|---|---|---|
| R1 | Recording someone without permission | 3 | Low now (blocked) | High | G1 stays BLOCKED; no audio in Stage 2; Stage 1 audio is server-fed fixtures only | `RELEASE_GATES.md` |
| R2 | Unexpected Azure/Claude/Speech bill | 1, 2 | Medium | Medium | Global daily and speech caps in the app, tested across a restart; Azure email alerts; start on Haiku; low quota | `azure-platform-verification.md` 1.7; PPR-02, PPR-03 |
| R3 | Terms the PO has not read or cannot judge (Anthropic via Marketplace; individual buyer unclear) | 1, 2 | Medium | Medium | Read-first list; F-M1 and F-M9 answered before accepting; optional short lawyer consult | `anthropic-terms-review.md`; legal review |
| R4 | Vendor keeps or trains on data longer than expected | 1, 2 | Unknown | Medium | Synthetic content in Stage 1; plain warning in Stage 2; consider zero data retention | `anthropic-terms-review.md` F-M3, F-M4 |
| R5 | Raw content leaks into logs or error reports | 1, 2 | Medium | High | Metadata-only logging; B1V-01 fixed; planted-phrase test; diarization and SDK logging off | `CLAUDE.md` invariant 10 |
| R6 | Wrong AI notes or dates acted on | 1, 2 | Medium | Medium | Source timestamps; review before any calendar write; "AI-generated" label | `CLAUDE.md` invariants 4, 6 |
| R7 | The tool adds stress for the son, or the pilot feels like being watched | 2 | Unknown | High to him | Student's own yes; usage not measured; PO sees daily cost only; stop and AI-off are his | UX review F1 to F3 |
| R8 | Quota too low for rolling summaries | 1 | Medium | Low | Measure in Stage 1; Haiku for rolling summaries | `azure-platform-verification.md` 1.7 |
| R9 | Region/availability surprise (Sonnet 5.5 Data Zone; v1 versus v2; thinking parameter) | 1 | Medium | Low | Settle on the real portal; plan on Haiku 5.5 | vendor check |
| R10 | Delete does not reach vendor or Google copies | 2 | Certain by design | Medium | Say so in plain words; avoid exporting unless wanted | PO-15c |
| R11 | Own notes contain someone else's content or personal details | 2 | Medium | Medium | Tighter definition in 4.1; no school documents; lecture-derived notes decision D21 | L-10; legal review |
| R12 | Scope creep: friends or classmates want to use it | any | Low | High | Revisit all DEFERRED legal items first | PO-15; PO-20u |
| R13 | Drift between docs | any | Medium | Low | Re-read live vendor pages; keep register current | playbook |
| R14 | Claims of benefit (learning, disability) made without validation | 2 | Low | High | No claims; G4 stays NOT_RUN | `RELEASE_GATES.md` G4 |
| R15 | Account misuse (keys leaked) | 1, 2 | Low | Medium | Key in a secret file outside the repo; rotation; gitleaks rule; adult PO holds the subscription | PPR-06 |
| R16 | An agent acts with the PO's Owner sign-in on a billing subscription | now | Unknown | High | Read-only identity only (PO-20bf); logout; regenerate keys if exposed | PPR-01 (HIGH, open until confirmed) |
| R17 | Microphone sends real voices to a vendor | 1 | Medium if mic used | High | Fixture-file audio only, mic refused | PPR-07 |
| R18 | Real content slips into a "synthetic" run | 1 | Medium | High | Fixture manifest and interlock; source-and-licence list | PPR-08 |
| R19 | A promise of privacy that the setup cannot keep (PO machine and agents can read notes) | 2 | Certain by design | Medium | Say so in the text; separate account later | PPR-11 |
| R20 | Another device on the home network reaches the dev server | 1, 2 | Low | Medium | Bind to 127.0.0.1; use the PO's machine only | PPR-04 |
| R21 | Licensed audio's licence forbids AI or cloud processing, or carries real voices | 1 | Medium | Low to Medium | Licence on the list before use | legal review |
| R22 | Usage Policy update on 2026-11-12 changes something relevant | 1, 2 | Unknown | Low | Read the live policy; dated copy; STOP rule | vendor check 5 |

---

## 9. Plain-language text for the student (DRAFT, not approved, not counsel-reviewed)

PO-20bp (2026-11-05): "approved as a draft" only. It stays DRAFT, is not final and is not shown to him. Any consent or recording wording stays G1, PO-only. D8 stays open.

For the PO to approve or edit (D8). Written by the UX specialist against the project's language guide: no exclamation marks, no clinical words, no "pilot / trial / test / participant", short sentences. Counsel has not reviewed any of it (PO-15b). Shown **once, before anyone asks him anything**, and always reachable from Settings under "What happens to my notes".

> **What happens to my notes**
>
> Tether can send the notes and questions you type to an AI service to write recaps and summaries. This page says what that means. You choose whether to turn it on.
>
> **What leaves this computer**
> Only the text you type into Tether. It goes to Claude, an AI run on Microsoft's Azure computers. Nothing else leaves, unless you choose to export a note to your own Google account.
>
> **How long they keep it**
> The AI company keeps what is sent for up to 30 days, then removes it. If their automatic checks hold something back for review, they may keep it longer. We cannot see or change what they keep.
>
> **Deleting**
> Delete in Tether removes Tether's copy at once. It cannot reach the copies the AI company already has, or anything you exported to Google.
>
> **Who can see your notes**
> Only you can open your notes in Tether. Your parent cannot see them unless you show them. [PO answer D13 applied.]
>
> One thing to know: this runs on your parent's computer. The computer, and the AI helper tools your parent uses on it, can technically read the files stored there. The setup does not stop them from looking. [P-OWN-ACCOUNT: required sentence; the PO may reword it, but the point must stay. A separate Windows account for you is being considered later.]
>
> **What your parent can see**
> The daily total of what the service cost, and whether the daily limit was reached. Not your notes, not when you used it, not what you reported.
>
> **Stopping**
> You can pause or stop at any time. You can turn the AI service off in Settings with one switch; Tether then uses its built-in stand-in instead. Nothing is deleted when you turn it off. You do not need to give a reason.
>
> **The notes can be wrong**
> Recaps and answers are the AI's best estimate. Check anything that matters against what you wrote. Tether never puts a date in a calendar without you reviewing it first.
>
> **Saying something is off**
> Every recap and answer has a "Report this" link. One tap marks it "Not right", "Not useful" or "Felt off". The marks stay in Tether. Nobody sees them unless you show them.
>
> **This is not a study**
> Nothing about you is being measured, scored or tracked. This page is about the service, not about you.
>
> [ Turn the AI service on ]   [ Not now ]

**Notes for the PO on this draft.**
- "Not now" is the secondary action and is complete. No "Are you sure?" follows it.
- The 30-day figure is the direct-API figure; the Foundry figure is UNVERIFIED (`azure-platform-verification.md` 1.3). If the PO cannot confirm it, change to "for a limited time, usually up to 30 days" and record why. Also re-check the Covered Model list (3.3 item 6) before the retention wording is final.
- Replace "your parent" with whatever he calls the PO; the point is the role.
- Words deliberately absent: pilot, trial, test, participant, feedback, study (except to deny it), neurodivergent, ADHD, focus, attention, help you, designed for you, safety flag.
- **Statements that are not safe to publish yet (security and vendor points, kept alongside the UX text; see section 12):**
  - Resolved by the PO (P-OWN-ACCOUNT): "Nobody but you" is replaced above by wording that says the PO's machine and agents can technically read the notes (PPR-11). The text is still a draft until the PO approves it (D8).
  - "Runs on Microsoft's Azure computers" is clearly true only for the "Hosted on Azure" version (v2); the other version runs on Anthropic's infrastructure (3.1 item 7).
  - Security's working assumption is that Anthropic staff can see flagged content "on an exceptions-only basis" and that flagged content may be kept up to 2 years (direct-API figure); the draft says only "keep it longer".
  - He should be told not to put passwords, health details or other people's personal information in his notes.
  - If the PO needs per-session cost data for debugging, the text must say so (D15).

**Words not to use with him:** pilot, trial, test, participant, study, feedback, neurodivergent, any diagnosis, focus, attention.

---

## 10. Decisions for the PO

D1 to D13 are this plan's own labels. They are not the G1 decisions named D1, D2, D8, D10 in `RELEASE_GATES.md` (section 12). D14 to D25 were added in revision 2. In revision 3, rows marked ANSWERED carry the PO's Review Desk answer and the `docs/DECISIONS.md` entry; an answer records a choice and authorises no work.

| ID | Decision | Needed by | Repo status |
|---|---|---|---|
| D1 | **Daily spend cap amount** (per day, with a separate monthly ceiling). See D16. | Before Stage 1 | **ANSWERED: $0.25 per day, $5 per month for AI (PO-20bk).** Speech cap not answered |
| D2 | Upgrade Azure to pay-as-you-go | Before Stage 1 | **ANSWERED: done (PO-20be).** Quota after upgrade UNVERIFIED |
| D3 | Accept Marketplace / Anthropic terms himself, after reading them and after D7, F-M1, F-M9 (or choose not to use Claude on Azure) | Before Stage 1 | **Rule ANSWERED (PO accepts himself; no agent does, PO-20bl).** Acceptance itself NOT done, not authorised (PO-20an) |
| D4 | **Who decides stage changes.** The PO alone, in writing on the Review Desk, one stage at a time. Agents may recommend but never decide. In Stage 2 the son's own yes (D14) is separate and his no ends it | Now | **ANSWERED (PO-20bg).** Conflict C4 resolved. No stage "go" given yet |
| D5 | Region: West US or West US 3 (preference recorded), and Global versus Data Zone. Plan on Haiku 5.5 Data Zone only; do not rely on Sonnet 5.5 Data Zone | Before Stage 1 | Preference only: West US 3 with Data Zone preferred for the first Claude test (PO-20bd, PO-20bs). Data Zone US-only UNVERIFIED; nothing deployed; still open |
| D6 | Where the app runs in Stages 1 and 2 | Before Stage 1 | **ANSWERED: the PO's own machine only; nothing deployed (PO-20bh)** |
| D7 | **The son's age** and whether the user is the son only or the PO too | Before Stage 1 | **ANSWERED: the son, 18 or older; a parent holds the accounts (PO-20bi).** Re-check on the day each stage starts |
| D8 | Approve or edit the section 9 text | Before Stage 2 | Draft only; open |
| D9 | The school-rules check: whether and when the PO reads the school's recording policy and asks the school, instructor, and accessibility office. Not needed for Stages 1 and 2 | Before Stage 3 | Open (PO-20ai) |
| D10 | Whether a lawyer will ever be consulted for Stage 3, or the PO accepts the risk and records it | Before Stage 3 | Open (PO-20ai) |
| D11 | Whether to ask Anthropic about zero data retention (their approval, not a setting we control) | Before Stage 2 | Open |
| D12 | Whether Google sign-in/export is used at all in Stage 2 (separate explicit go) | Before Stage 2 | Not authorised (PO-20an); open |
| D13 | Who may see the son's notes in Stage 2 | Before Stage 2 | **ANSWERED: only him; he shows the PO what he chooses (PO-20bg)** |
| D14 | **The student's own yes.** Asked once, after he has read the section 9 text; "not now" and "no" are complete answers; withdrawable at any time by stopping; never re-asked by the PO | Before Stage 2 | **ANSWERED: adopted (PO-20bg)** |
| D15 | **Usage metadata privacy.** What the PO may see of his activity: daily cost total and daily-cap state only; no per-session times, no counts of use, no reports unless he shows them; he sees everything the PO sees, on the same screen | Before Stage 2 | Plan's recommendation applied under P-CONFLICTS (PO-20bn); PO to confirm when approving the text |
| D16 | **Caps.** (a) Global daily AI cap; (b) monthly ceiling the app refuses to exceed; (c) speech-minutes cap | Before Stage 1 | **(a) $0.25 per day and (b) $5 per month ANSWERED (PO-20bk). (c) 10 min per session, 30 min per day, 120 min per month CONFIRMED (PO-20bo); Stage 1 fixture audio only; not built** |
| D17 | **Reader identity and agents' Azure access.** Agents use only the read-only (Reader) identity recorded in PO-20bf. Confirm the old Owner session is gone and whether keys need regenerating | Now | **Partly answered (PO-20bf).** Rest UNVERIFIED. The screenshots show the PO's own Owner role on the subscription, which is his, not an agent's |
| D18 | **One-way mock switch.** Real to mock only; he can turn real AI off in the app; only the pilot setup turns it on | Before Stage 1 | **ANSWERED (PO-20bj).** Conflict C1 resolved |
| D19 | **Who builds the Stage 2 typed-notes feature** (new work: contract, budget pool, storage, delete and export, log canary, security review) | Before Stage 2 build | Open; nothing is authorised to be built |
| D20 | **Who builds "Report this"** (new work: additive contract, user-scoped table, ownership tests, no-outbound-call test, security review) | Before Stage 2 build | Open; nothing is authorised to be built |
| D21 | Whether a typed summary of a lecture he attended is allowed in Stage 2 | Before Stage 2 | **ANSWERED: not yet; own reflections and questions only (PO-20bj)** |
| D22 | How the deferred MEDIUM legal rows (L-07, L-08, L-14) and SEC-PUBLIC are handled for Stage 2 | Before Stage 2 | **ANSWERED: fix the delete wording first; dated, named PO risk acceptance for the rest (PO-20bj).** Neither is done. PO-20bq: legal and UX agents draft new delete-everything wording as DRAFT for PO approval; nothing ships |
| D23 | Lawyer consult on "an individual buying Claude through Azure Marketplace for a family member's study use". Earlier than, and separate from, D10 | Before D3 | **ANSWERED: short consult first if cheap; otherwise the PO's written risk acceptance (PO-20bl).** Neither is done. PO-20bt (Lead's reading of "have the lawer write it"): the legal agent drafts the lawyer question list and a risk-acceptance template; not legal advice, not a lawyer; the PO still accepts terms himself |
| D24 | Separate the son's data from the PO's, or say plainly that the PO's machine and agents can technically read his notes | Before Stage 2 | **Part ANSWERED: say it plainly in the text; his own Windows account considered later (PO-20bn)** |
| D25 | What happens to the auto-created Foundry resource | Before Stage 1 | **ANSWERED: keep it for now, no action; revisit if the pilot is dropped (PO-20bm).** Corrects an earlier "delete it now". No pending action |

**Still needing the PO after revision 3 (updated 2026-11-05):** D5 (region and deployment type), D8 (approve the section 9 text; only "approved as a draft" so far, PO-20bp), D9 and D10 (Stage 3 questions), D11 (zero data retention), D12 (Google), D19 and D20 (who builds the new Stage 2 features), D15 confirmation, the Foundry keys question (D17), what the PO decided about the C:\Dev root folder staying a git repo (housekeeping item 5, outcome unknown; the other four items are PO-reported done, not verified, PO-20bv), approval of the E8 `CLAUDE.md` diff (PO-20br), F-M1, the actual lawyer consult or written risk acceptance (D23), the dated risk acceptance for D22, and every written stage "go" (D4).

---

## 11. Open unknowns (all UNVERIFIED unless stated)

1. Azure Speech price per hour: **partly resolved** (list $1.00 per hour, Retail API); account price and billing granularity still UNVERIFIED. Diarization add-on mapping is an inference.
2. Whether Azure Speech trains on customer audio; Microsoft DPA, subprocessor list and SLA not read. `azure-platform-verification.md` 2.2 and 9.
3. Real-time speech latency; no vendor figure exists. Same file, 2.2.
4. Whether an individual may buy Claude through Azure Marketplace on a card-backed account. Same file, 1.5.
5. The content of the terms behind `aka.ms/anthropic_tandc`, and which document wins in a conflict. Same file, 1.6.
6. Whether Foundry's retention for Claude is the 30 days stated for the direct API; whether a chosen model is a Covered Model. Same file, 1.3; legal review.
7. Whether Data Zone in West US 3 is US-only; Sonnet 5.5 Data Zone is contradicted between vendor pages; whether v2 exists in Global Standard in the chosen region (Microsoft's pages disagree with each other).
8. The account's real quota and price after the upgrade (last read 0 on the free trial; no page says how long reclassification takes). Hypotheses: propagation lag, stale client view, Marketplace offer not yet subscribed, wrong deployment type, billing country.
9. Whether Microsoft keeps any Claude prompt data for abuse monitoring; retention of flagged content.
10. Whether the existing Foundry resource also serves Speech, and Speech region, tier, quota; whether F0 refuses rather than bills past its free hours; whether S0 gets free hours.
11. Whether the repo's current Claude client obeys Foundry's parameter limits.
12. Whether 40 requests per minute and 40,000 input tokens per minute suffice for rolling summaries plus Quick recap.
13. Recording law, school policy, FERPA applicability and instructor permission. `recording-compliance-gap-report.md` section 3.
14. Neurodivergent suitability and learning-science effectiveness. `RELEASE_GATES.md` G4.
15. The son's age: answered as 18 or older (PO-20bi, D7); the PO's own statement, not independently checked; re-check each stage.
16. The text of the PO decisions named D1, D2, D8, D10 in the G1 row of `RELEASE_GATES.md`: not found (section 12).
17. What the Usage Policy effective 2026-11-12 changes (secondary coverage only).
18. Whether an idle Foundry or Speech S0 resource shows $0 in Cost Management by meter after a billing month.
19. Whether the old Owner Azure session is gone from this machine, whether MFA is on, whether anyone else holds a role, and whether any diagnostic settings ship request content (security 1.2, 1.20).
20. Whether gitleaks' default rules recognise an Azure key shape.
21. Who sets Claude's price and terms on this account. The PO's note: Foundry shows no "management center" in its left panel, and "view pricing" leads to the Anthropic-side pricing page, which hints at Anthropic-side price and terms. Evidence only; no conclusion.
22. Whether the $200 credit (expires 2026-12-03, $200 available) applies to Claude bought through Marketplace. Assume not.
23. The Foundry resource is kept for now (PO-20bm); whether it has idle billable charges or exposed keys is UNVERIFIED.
24. The speech-minutes cap (10 min per session, 30 min per day, 120 min per month) is confirmed by the PO (PO-20bo) but not built or tested.
25. UX question, no change made: at 390px width the lecture bar flashes from "Not connected" (137px tall) to "Connected" (105px) during hydration. Whether that layout shift is acceptable for a low-stimulation design is open for `neurodivergent-ux-specialist`.

---

## 12. Contradictions, gaps and conflicts between reviews

These are for the Lead and the PO. I changed no other file.

### A. Conflicts between the four reviews: RESOLVED BY THE PO (P-CONFLICTS, PO-20bn: "use the plan's recommendations")

The PO told the Review Desk to use the plan's recommendation for C1 to C7. Each is recorded below with the recommendation now in force. The other side of each conflict is kept in one line for the record. Where a recommendation depends on a later PO answer, that is stated.

- **C1. One-way switch versus an on/off control. Resolved by the PO.** In force: "on" only records his permission inside a stage the server already allows; the server (pilot setup) alone enables a paid provider; he can turn real AI off in the app (D18, PO-20bj). Security's rule (nothing in the UI can turn a paid provider on) stands.
- **C2. "Nobody but you" versus what the setup can enforce. Resolved by the PO.** In force: the text says plainly that the PC and agents can technically read files (P-OWN-ACCOUNT); a separate Windows account for him is considered later (D24, PO-20bn). UX's unqualified sentence is not used.
- **C3. A STOP on other people's details versus nobody reading his notes. Resolved by the PO under "use the plan's recommendations", with the plan's own reading:** nobody inspects his notes; the section 9 text tells him not to include other people's details; he is the one who notices and stops, and a STOP is something he does or says. Residual risk (R11) is carried and is covered by the dated risk acceptance in D22. The PO did not give a separate answer for C3 beyond the blanket instruction; if he wants a different mechanism he can say so.
- **C4. Who decides. Resolved by the PO.** In force: the PO alone decides stage changes, in writing, one stage at a time (D4, PO-20bg). The son's own yes is a separate decision asked once; his "no" or "not now" ends Stage 2 (D14).
- **C5. Retention and hosting wording in the student text. Resolved by the PO.** In force: the cautious wording (up to 30 days, may be kept longer if held for review; "Microsoft's Azure computers" only if the chosen hosting is the Azure-hosted version, v2). The 30-day figure and the hosting claim remain UNVERIFIED for this account and are checked before the text is approved (D8).
- **C6. How much the PO sees. Resolved by the PO.** In force: daily cost total and daily-cap state only (D15). If debugging ever needs per-session data, the text is changed first and the PO says so.
- **C7. Timing of the vendor-terms questions. Resolved by the PO.** In force: legal's earlier, stricter timing. The age (answered, PO-20bi) and F-M1 are settled before the PO accepts any terms; F-M2, F-M3 and F-M4 stay at Stage 1 exit. This adds work before the first real call.

"Resolved by the PO" means the choice is made. It does not mean the underlying legal, vendor or security facts are verified.

### B. Gaps and contradictions in the repo (carried from revision 1, updated)

1. **Free account versus Claude.** `stt-vendor-terms-review.md` L-5 calls the Azure free account "the safest cost shape"; `azure-platform-verification.md` 1.5 says Claude on Foundry does not work on free-trial subscriptions. The account is now pay-as-you-go (PO-20be). L-5 holds only for Speech F0.
2. **Who the user is.** Resolved by the PO (PO-20bi): the user is his son, 18 or older, and a parent holds the accounts. PO-16's "probably only the Product Owner" is superseded for this plan.
3. **Son's age.** Answered as 18 or older (PO-20bi). `anthropic-terms-review.md` F-M9 and `stt-vendor-terms-review.md` M-2 predate that answer; the PO's statement is not independently checked.
4. **Storage direction in older documents.** `anthropic-terms-review.md` still says data is stored "in Google and AWS" and covers direct Anthropic and Bedrock only; PO-20aa/ab replaced that with Azure plus Google Docs. Legal register L-13 still names AWS.
5. **Sonnet 5.5 Data Zone.** Contradicted between Anthropic's page and Microsoft's pages. The older text in `azure-platform-verification.md` is not fully updated. Unresolved until the real portal (3.1 item 6).
6. **Thinking setting (T-2)** is contradicted between two Microsoft pages and can only be settled on a real deployment.
7. **Dates in the register.** `RELEASE_GATES.md` says "Last updated 2026-10-27" but cites events of 2026-11-01. Header stale.
8. **PO-3 versus PO-20z.** Consistent in effect, untidy in text.
9. **Gate G1 decisions D1, D2, D8, D10** are named in `RELEASE_GATES.md` but their definitions were not found. This plan's D-numbers are its own. Do not confuse them.
10. **Stage 2 needs things marked "parked".** Export-all and delete-account are parked "until before real use" (PO-20ax); the plan makes them an entry condition.
11. **Consent pop-up (PO-16) versus the CLAUDE.md invariant** to confirm university rules. PO-16 says it does not clear G1; this plan treats it as clearing nothing.
12. **Pilot key storage.** PO-20am says Key Vault for the token key production only; PO-20ao confirms a local key store for the pilot. Revision 2 defines it as a secret file outside the repo.
13. **Plan wrongly named interfaces.** Fixed (`ISpeechToTextProvider`, `IAiClient`).
14. **Stage 1 quota note was stale** ("today it is 0"). Replaced by the post-upgrade UNVERIFIED wording.
15. **Open security finding PPR-01 is HIGH** under the severity policy and blocks any push until resolved or downgraded in writing by the PO. PO-20bf addresses the identity half; the PO's dated confirmation of the rest closes it.

### C. For the Lead (edits to other files that this plan does not make)

- Add register rows suggested by legal (Microsoft/Azure contract stack; Usage Policy update; licensed audio and real voices; Stage 2 notes with others' data; who can see an adult child's notes; refresh L-04, L-09, L-13).
- Add findings PPR-01 to PPR-13 to `docs/security/findings-register.md` (security-engineer offered).
- Update `.env.example` (PPR-06) and fix the stale items in B4, B5 and B7.

---

## 13. What this plan does NOT authorise

- No real classroom or lecture recording, and no recording of any other person. Stage 3 is blocked by G1.
- No microphone feeding a real provider in any stage.
- No clinical, diagnostic, treatment or learning-outcome claim about the son or anyone else (G4 NOT_RUN).
- No production deployment, and no cloud hosting, database, Key Vault or Blob storage creation (G5 BLOCKED).
- No spend of any kind without the PO's written decision, including speech use and Claude use. The pay-as-you-go upgrade is done (PO-20be); nothing else is authorised.
- No acceptance of Marketplace, Anthropic or any other terms by an agent (PO-20an, PO-20bl). The PO's recorded answers (PO-20bg to PO-20bn) are choices, not approvals of work: they do not authorise building Stage 1, deploying, requesting quota, spending, accepting terms, or starting any stage.
- No deletion or creation of Azure resources by an agent. The Foundry resource is kept for now with no action (PO-20bm); any deletion later is the PO's own action.
- No assumption that the $200 credit covers Claude through Marketplace.
- No agent use of the PO's Owner Azure sign-in; agents use only the read-only identity (PO-20bf).
- No real Google sign-in, Calendar write or Docs export without the PO's separate explicit go (PO-20an).
- No real school documents (syllabi, exams, slides) and no real syllabus extractor in Stages 1 and 2 (PO-20l, L-10).
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
- **Hosted on Azure (v2) / Hosted on Anthropic infrastructure (v1)**: two ways Claude can run on Foundry; only v2 keeps prompts within Azure.
- **Marketplace terms**: the buying terms shown by Azure when a Claude model is first deployed; Anthropic's own terms also apply.
- **Pay-as-you-go**: an Azure subscription billed for what you use. Claude needs this; a free trial does not work.
- **MTok**: one million tokens. A token is a small piece of text.
- **CCU**: Claude Consumption Unit, how Azure bills Claude, $0.01 each.
- **Quota / RPM / ITPM / OTPM**: Azure's rate limits: requests, input tokens and output tokens per minute. They slow you down; they are not a spend cap.
- **Data Zone (US)**: an Azure deployment type meant to keep processing within the United States, at a 1.1x price multiplier.
- **Diarization**: labelling who is speaking. Kept off because it processes voice characteristics.
- **Fixture file**: a pre-chosen audio or text file, listed with its source and licence, that the server feeds to a real service in Stage 1.
- **Usage Policy**: Anthropic's rules for acceptable use (earlier called the Acceptable Use Policy); an update takes effect 2026-11-12.
- **Reader identity**: an Azure sign-in that can only look, not change or spend (PO-20bf).
- **Quick recap**: one click to catch up on the last 2, 5 or 10 minutes.
- **TL;DR**: a short summary that updates as the session goes on.
- **Synthetic content**: made-up or self-made material, or audio licensed for testing, with no real person's private content.
- **Mock mode**: the app using fake stand-ins instead of real services.
- **Zero data retention (ZDR)**: an arrangement where the AI vendor does not keep inputs or outputs; needs Anthropic's approval.
- **DPA**: data processing addendum, a contract on how a vendor handles personal data.
- **FERPA**: US law on student education records; whether it applies here is a question for counsel (L-03, deferred).
- **UNVERIFIED**: not confirmed in any source the repo holds.
- **Inference**: a reviewer's reading that no source page states outright.
- **Review Desk**: the PO's local site for answering decisions.
