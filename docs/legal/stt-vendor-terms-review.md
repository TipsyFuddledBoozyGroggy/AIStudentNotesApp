# STT Vendor Terms Review (Legal Register L-05)

Fetch date for every source below: **2026-10-30** unless a row says otherwise.
Scope: Deepgram, AssemblyAI, Azure AI Speech, as listed in `docs/vendors/verification-report.md` (retrieved 2026-10-27). Context: Tether is a non-commercial app for one student (PO-15).
Status: research for counsel and the Product Owner. **Not legal advice. No gate is marked PASS. Gate G2 stays BLOCKED/UNVERIFIED.** No account was created and nothing was spent.

Evidence labels: **VERIFIED** = read on the official page on the fetch date. **VERIFIED-SUMMARY** = official page, but the fetch tool returned a model-written summary, so re-read the page before quoting it in a contract or legal memo. **UNVERIFIED** = not retrieved or not readable. Anything in quotation marks is the tool's quotation of the page, not a reviewed verbatim copy, unless it is marked VERIFIED.

## 1. Bottom line

1. All three vendors' terms assume a business customer. None says whether a single person using the API for personal, non-commercial use is a normal customer. AssemblyAI is the most explicit: the license is "for the internal business purposes of Customer".
2. The biggest practical risk is **default training and retention**. Deepgram enrolls every request in model improvement unless every request carries `mip_opt_out=true`. AssemblyAI trains on data by default for plans that allow it, and a free account cannot opt out. Azure real-time STT stores nothing at rest, per Microsoft.
3. Lecture audio includes **other people's voices** (the lecturer, classmates). Every vendor puts the consent burden on Tether's operator. The consent gate (G1) is still BLOCKED, so no vendor can be cleared for real lecture audio.
4. Deepgram and AssemblyAI both ban "competitive" benchmarking in their terms. The planned cost/latency benchmark must be read against that language first.

## 2. Findings

### CRITICAL

**C-1. Real lecture audio cannot go to any vendor while G1 (recording consent) is BLOCKED.**
- Azure: "you are responsible ... to obtain all necessary permissions for processing of the data" and audio "may also be regulated under various communications laws" (https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/speech-service/speech-to-text/data-privacy-security, 2026-10-30, VERIFIED, page dated 2026-03-31). Its transparency note adds that speech to text "is not intended to be used for covert audio surveillance" and asks for "all necessary and appropriate consents from users" (https://learn.microsoft.com/en-us/legal/cognitive-services/speech-service/speech-to-text/transparency-note, 2026-10-30, VERIFIED).
- Deepgram: you must "have all rights needed to license your content to Deepgram" (ToS §3.5, https://deepgram.com/terms, 2026-10-30, VERIFIED-SUMMARY).
- AssemblyAI: customer confirms it holds all rights and consents (ToS §2.7, https://www.assemblyai.com/legal/terms-of-service, 2026-10-30, VERIFIED-SUMMARY).
- Consequence: the terms put the legal exposure on the app operator. Synthetic or licensed audio is fine for evaluation. Real classroom audio is not, until G1 clears with qualified counsel and campus authority.

### HIGH

**H-1. Deepgram: training on your content is the default, and the license is perpetual.**
- Deepgram's data page says "Requests participate in the Model Improvement Program (MIP) by default, and Deepgram retains them." Audio and transcripts are "retained to improve our models" by default (https://developers.deepgram.com/trust-security/your-data, 2026-10-30, VERIFIED-SUMMARY).
- ToS §3.2 grants Deepgram a license to Your Content to improve services and "train and test its Models", described as "irrevocable, perpetual, transferable, sublicensable", surviving termination. ToS §3.3: after opt-out "any new content you submit won't be used to train our models" (https://deepgram.com/terms, last updated 2026-08-06, VERIFIED-SUMMARY).
- Opt-out is per request (`mip_opt_out=true`), forward-looking only, and opted-out data is "retained only for the duration necessary to process the request" (https://developers.deepgram.com/docs/the-deepgram-model-improvement-partnership-program, 2026-10-30, VERIFIED-SUMMARY).
- Risks: one request without the flag is enrolled, and enrolled data stays licensed to Deepgram. Whether the ToS §3.2 perpetual license overrides the opt-out for data already submitted is UNVERIFIED. The adapter must hard-code the flag and fail closed. A test must assert it on every connection.
- Pricing effect of opt-out: the MIP page does not mention pricing (VERIFIED-SUMMARY). The earlier 50 percent discount claim in the verification report remains UNVERIFIED.

**H-2. AssemblyAI: default training, free tier cannot opt out, and terms say business use only.**
- ToS: customers grant a license to use Customer Data "to train AssemblyAI's artificial intelligence and machine learning models", with opt-out "to the extent applicable to Customer's pricing plan". De-identified data is treated as not being Customer Data and may be used "for any purpose" (https://www.assemblyai.com/legal/terms-of-service, effective 2026-07-01, VERIFIED-SUMMARY).
- Opt-out is a dashboard toggle (Owner or Admin). Paid customers can opt out at no cost. "Free users cannot opt out." Changes are forward-looking only (https://www.assemblyai.com/docs/faq/how-to-opt-out-of-data-sharing-for-our-model-improvement-program, 2026-10-30, VERIFIED-SUMMARY).
- Streaming zero retention applies only when opted out: "If you are opted out of model training, we offer zero data retention of audio and transcripts for our Streaming product." Default streaming retention is not stated. Metadata is kept for logging and billing (https://www.assemblyai.com/docs/data-retention-and-model-training, 2026-10-30, VERIFIED-SUMMARY).
- Consequence: the free $50 credit tier is unsuitable for any real student audio. Use the free tier with synthetic audio only.
- "Internal business purposes of Customer" (ToS) leaves a personal, non-commercial account in a gray zone. Whether an individual may open an account for this use is UNVERIFIED. Ask AssemblyAI support in writing.

**H-3. Biometric and diarization consent duties (AssemblyAI, Azure).**
- AssemblyAI: opting into speaker diarization or other biometric features subjects the customer to the Biometric Addendum (ToS §2.5, VERIFIED-SUMMARY). The addendum makes the customer "responsible for compliance with Biometric Data Laws", including notice and "informed written consent ... where legally required", effective 2026-05-26 (https://www.assemblyai.com/legal/biometric-data-addendum, 2026-10-30, VERIFIED-SUMMARY). The addendum text read does not mention diarization by name, so its applicability to diarization is UNVERIFIED. AssemblyAI's FAQ says embeddings are not kept after transcription (search-result summary only, https://www.assemblyai.com/legal/biometric-notice-and-consent-faqs, UNVERIFIED because the page was not opened).
- Azure: voice characteristic signals are "temporarily retained" for labelling and then discarded. The customer is responsible for notice, consent and deletion of Biometric Data (data-privacy page above, VERIFIED).
- Consequence: diarization of a lecture records and processes voice characteristics of non-consenting people. Keep diarization off by default until counsel advises, since the MVP does not need speaker identity.

### MEDIUM

**M-1. Benchmarking restrictions.**
- Deepgram ToS §2.4 lists "competitive uses, including model training, benchmarking, and developing competing products" as prohibited (VERIFIED-SUMMARY).
- AssemblyAI ToS §2.4 prohibits using the services "to build competing products or run competitive benchmarking" (VERIFIED-SUMMARY).
- A private cost/latency evaluation by a prospective customer is probably not "competitive", and the point is UNVERIFIED. Do not publish benchmark results. Ask each vendor to confirm in writing that internal evaluation is allowed before running `MEASUREMENT_PROTOCOL.md`.

**M-2. Eligibility and age.**
- Deepgram: user must be at least 18, or the minimum age to consent in their location, whichever is higher (ToS §1.1, VERIFIED-SUMMARY). The Deepgram privacy policy says services are not intended for under-13s (last updated 2021-10-26, VERIFIED-SUMMARY, old).
- AssemblyAI: ToS does not address age (VERIFIED-SUMMARY). Privacy policy: no knowing collection from under-16s (modified 2026-05-26, VERIFIED-SUMMARY).
- Azure: the free-account page states no age requirement. It requires a non-prepaid credit card, a phone number, and a Microsoft or GitHub account, and the offer is for new Azure customers only (https://azure.microsoft.com/en-us/free/, 2026-10-30, VERIFIED-SUMMARY).
- Consequence: the account holder must be the adult Product Owner, not the student, unless the student is confirmed to meet each vendor's age rules. The student's age is not in this repo. UNVERIFIED for the student.

**M-3. Unilateral change and termination rights.**
- Deepgram may change, suspend or terminate "either with or without notice". Credits expire when the account closes (ToS §1.4, §5.4, VERIFIED-SUMMARY).
- AssemblyAI may change the agreement at any time, and continued use counts as acceptance (ToS §9.6, VERIFIED-SUMMARY).
- Consequence: terms can change under a running account. Re-check terms on a schedule and keep the provider interface replaceable.

**M-4. Liability, warranty and dispute terms are consumer-hostile.**
- Both are "AS IS". Deepgram caps liability at the greater of 12 months of fees or $100, requires indemnity from the customer, and uses California law with individual AAA arbitration and a class waiver. There is a 30-day arbitration opt-out by email to legalnotices@deepgram.com (ToS §7-§12, VERIFIED-SUMMARY). AssemblyAI caps direct damages at 12 months of fees, with Delaware law and New York courts (ToS §5, §9.8, VERIFIED-SUMMARY).
- Consequence: a data incident gives the operator little recourse. If the Product Owner wants the Deepgram arbitration opt-out, the deadline runs from account acceptance, so decide before accepting.

**M-5. No DPA, SLA or FERPA/education terms obtained for any vendor.**
- Deepgram: DPA is by request. The privacy policy says DPAs are signed with customers and often use the EU SCCs. A general DPA was not found (search result, https://deepgram.com/privacy, UNVERIFIED). The MSA PDF (https://static.deepgram.com/business/MSA_20240315.pdf, dated 2024-03-15) could not be read by the fetch tool, so its terms are UNVERIFIED. It is also an enterprise form and may not apply to a self-serve account.
- AssemblyAI: ToS does not address retention periods or subprocessors. A DPA is linked but not read (UNVERIFIED).
- Azure: the Microsoft DPA (English edition dated 2026-05-22 per https://www.microsoft.com/licensing/docs/view/Microsoft-Products-and-Services-Data-Protection-Addendum-DPA, VERIFIED-SUMMARY, index page only) was not read. Training on customer data and SLA are not stated on the pages read (UNVERIFIED).
- FERPA or education-specific terms: not found on any page read for any vendor. Whether FERPA applies to a student's own personal notes app is a question for counsel.

**M-6. Subprocessors and onward transfer.**
- Deepgram's list (https://deepgram.com/privacy/subprocessors, 2026-10-30, VERIFIED-SUMMARY) shows these processors for audio: Amazon (AWS), Baseten, Cloudflare, Coval, Oracle (OCI). Anthropic and OpenAI are listed for "Interactive Text & Audio" (general AI systems and LLMs). The page shows no locations and no last-updated date. Deepgram's terms also say third-party hosting and LLM providers are outside its control and it disclaims responsibility for their retention (ToS §6.1).
- AssemblyAI: list lives at a Vanta trust page. The fetch returned no content (UNVERIFIED).
- Azure: speech data is stored and processed in the region where the resource is created, except Voice Live (https://learn.microsoft.com/en-us/azure/ai-services/speech-service/regions, page dated 2026-10-26, VERIFIED). Real-time STT is listed in US regions including `eastus`, `eastus2`, `westus2`, `westus3`, `centralus`. The Microsoft subprocessor list was not read (UNVERIFIED).

**M-7. Azure retention is the strongest on paper, but it is not training-free proof.**
- "For real-time speech to text, audio input is processed only on the Azure's server memory, and no data is stored at rest." And "Microsoft does not retain or store the data provided by customers" for real-time STT (VERIFIED, page dated 2026-03-31). Batch output may be stored in Microsoft storage if no storage is specified. Local SDK logging is optional and would be a Tether-side leak path if enabled.
- The page does not state whether data is used to train models (UNVERIFIED).
- Price remains unverifiable from the official page (see verification report).

### LOW

- **L-1. Deepgram privacy policy is old** (2021-10-26) and says Deepgram may share information with third parties for marketing; it states it does not sell personal information. It also says it "cannot modify or delete Customer Data except at the request of our customer", so deletion requests go through the account holder (VERIFIED-SUMMARY).
- **L-2. AssemblyAI async retention** (not used for live streaming): audio without TTL or BAA deletion starts at 24 hours and finishes within 48 hours; final transcription artifacts delete from 30 days. This matters only if Tether adds file upload transcription (VERIFIED-SUMMARY).
- **L-3. Deepgram prohibits presenting audio output as human-created** and requires legally required AI-generated notices (ToS §2.6, VERIFIED-SUMMARY). Tether's UI already labels AI output; low impact.
- **L-4. Azure containers option:** Speech containers run in the customer's environment and send no audio to the cloud service (VERIFIED, privacy page above). Not evaluated for cost or operational burden; mention only as a data-minimization option if counsel rejects every cloud path.
- **L-5. Azure free account** ends after $200 or 30 days and then disables services unless upgraded, with no automatic charge (VERIFIED-SUMMARY). This is the safest cost shape for a non-commercial project, but the real-time STT price is still unverified.

## 3. Vendor comparison (terms only; not a performance or price ranking)

| Topic | Deepgram | AssemblyAI | Azure AI Speech |
|---|---|---|---|
| Training on your data by default | Yes unless `mip_opt_out=true` on every request (VERIFIED-SUMMARY) | Yes unless account opt-out; plan-dependent; free cannot opt out (VERIFIED-SUMMARY) | Not stated on pages read (UNVERIFIED) |
| Streaming retention when opted out | Duration of request (VERIFIED-SUMMARY) | Zero retention of audio and transcripts, metadata kept (VERIFIED-SUMMARY) | No data stored at rest (VERIFIED) |
| Streaming retention by default | "Retained to improve our models" (VERIFIED-SUMMARY) | Not stated (UNVERIFIED) | No data stored at rest (VERIFIED) |
| Personal-use fit | ToS silent on personal vs commercial; 18+ (VERIFIED-SUMMARY) | "Internal business purposes" (VERIFIED-SUMMARY) | Free account for new customers; card required (VERIFIED-SUMMARY) |
| Benchmarking clause | Competitive benchmarking prohibited | Competitive benchmarking prohibited | Not seen on pages read (UNVERIFIED) |
| Biometric/diarization duties | Not seen (UNVERIFIED) | Biometric Addendum applies if diarization opted in | Customer duties for biometric data |
| Subprocessor list read | Yes, no locations | No (fetch failed) | No |
| DPA read | No | No | No (index page only) |
| SLA read | No | No | No |
| US data region choice | Not confirmed (default region UNVERIFIED) | Global default; US/EU zones (search summary only) | Yes, per-resource region (VERIFIED) |

## 4. Recommended shortlist (with caveats)

This is a terms-risk shortlist only. It does not choose a vendor. Selection also needs measured accuracy, latency and cost (`docs/vendors/`), which have not been run.

1. **Azure AI Speech (real-time), US region. Lowest terms risk found.**
   Caveats: price unverified; training-on-data statement and DPA not read; Microsoft account setup needs a card; no pilot has been run. Before any use, read the DPA and the Azure subprocessor list and confirm the no-storage statement in the actual subscription's terms.
2. **AssemblyAI (paid account with account-level training opt-out enabled).**
   Caveats: never use the free tier for real audio; confirm in writing that a personal, non-commercial account is permitted; confirm default streaming retention; read the DPA and subprocessor list; keep diarization off until the Biometric Addendum question is answered; confirm internal benchmarking is allowed.
3. **Deepgram (only with `mip_opt_out=true` enforced in code and tested).**
   Caveats: default is training and retention; perpetual-license wording in ToS §3.2 needs counsel's read; per-request flag is a single point of failure; decide on the arbitration opt-out before accepting; confirm internal benchmarking is allowed; MSA and DPA not read.

Fallback design: keep the STT provider interface vendor-neutral, treat each vendor's no-training setting as a startup precondition (refuse to open a stream if the setting cannot be asserted), and keep an Azure container or local path as the last-resort option if counsel rejects cloud processing of lecture audio.

## 5. Not verified (explicit)

- Any vendor's SLA and uptime terms.
- Deepgram MSA text, general DPA, default US region, and whether ToS §3.2 survives opt-out for prior data.
- AssemblyAI subprocessor list, DPA, default streaming retention, whether Biometric Addendum covers diarization, whether personal accounts are allowed.
- Microsoft DPA text, Azure subprocessor list, Azure training-on-data statement, Speech-specific service terms.
- Whether any campus agreement changes terms or availability.
- FERPA, state biometric and wiretap law applicability. That is for counsel.
- The student's age and whether any vendor's age rule is met.
- Fetch limitation: most pages came through a summarizing fetch tool. Re-open each URL before quoting terms.

## 6. Requested human actions

1. Product Owner: decide which adult will be the account holder for any vendor (M-2).
2. Product Owner or counsel: email Deepgram and AssemblyAI to confirm (a) personal, non-commercial accounts are allowed, (b) internal evaluation benchmarking is allowed, (c) default streaming retention, (d) DPA availability for self-serve accounts. Keep the replies as evidence.
3. Counsel: review the Biometric Data Addendum, the Deepgram ToS §3.2 license, the Microsoft DPA, and the consent duties in C-1 and H-3.
4. Product Owner: do not start any paid or free vendor account until counsel confirms the account holder and until the Deepgram arbitration opt-out window (30 days from acceptance) has been weighed.
5. `speech-engineer`: add a startup precondition that fails closed when the no-training setting is not asserted (Deepgram flag; AssemblyAI opt-out confirmed in the dashboard).

## 7. Gate status

- G1 Recording/consent: **BLOCKED** (unchanged).
- G2 Vendor selection: **UNVERIFIED** (unchanged). This review narrows risks but leaves price, SLA, DPA, FERPA and measured performance open.
- L-05 in `docs/legal/legal-register.md`: recommend moving to IN_PROGRESS with this file as evidence of the first pass. **No item is marked PASS.** Closing L-05 needs counsel's dated review of the open items in section 5.

## 8. Source index (all fetched 2026-10-30)

- https://deepgram.com/terms (last updated 2026-08-06)
- https://deepgram.com/privacy (last updated 2021-10-26)
- https://deepgram.com/privacy/subprocessors
- https://developers.deepgram.com/trust-security/your-data
- https://developers.deepgram.com/docs/the-deepgram-model-improvement-partnership-program
- https://static.deepgram.com/business/MSA_20240315.pdf (unreadable)
- https://www.assemblyai.com/legal/terms-of-service (effective 2026-07-01)
- https://www.assemblyai.com/legal/privacy-policy (2026-05-26)
- https://www.assemblyai.com/legal/biometric-data-addendum (2026-05-26)
- https://www.assemblyai.com/docs/data-retention-and-model-training
- https://www.assemblyai.com/docs/faq/how-to-opt-out-of-data-sharing-for-our-model-improvement-program
- https://www.assemblyai.com/legal/subprocessors (redirected to a Vanta page that returned no content)
- https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/speech-service/speech-to-text/data-privacy-security (2026-03-31)
- https://learn.microsoft.com/en-us/legal/cognitive-services/speech-service/speech-to-text/transparency-note (2026-03-31)
- https://learn.microsoft.com/en-us/azure/ai-services/speech-service/regions (2026-10-26)
- https://www.microsoft.com/licensing/docs/view/Microsoft-Products-and-Services-Data-Protection-Addendum-DPA (index only)
- https://azure.microsoft.com/en-us/free/
- Search-result summaries only (not opened, treat as leads): https://www.assemblyai.com/legal/biometric-notice-and-consent-faqs
