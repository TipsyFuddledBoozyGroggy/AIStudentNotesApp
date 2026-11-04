# Azure platform verification: Claude in Microsoft Foundry, Azure AI Speech, hosting and storage

Retrieval date for every source: **2026-11-01**. Prepared by `vendor-verification-researcher` (AI agent) for Product Owner decision PO-20aa (Azure for speech, hosting, storage and the Claude notes model; no AWS; no Google Cloud storage) as clarified by PO-20ab (student files stay in Google Docs/Drive).
**Not legal advice. Research and planning only. No Azure or Anthropic account was opened, no call was made, nothing was spent. G1 stays BLOCKED. G2 stays UNVERIFIED. No gate is marked PASS by this file.**

## How to read this file

- **VERIFIED-SUMMARY**: an official page (Microsoft Learn, Anthropic docs) was fetched and the claim appears in the extract. The fetch tool returns **model-written extracts of the page, not the raw page**, so a human must re-read the live page before quoting any row in a contract, memo or decision. Quotes below are the extract's quotes.
- **UNVERIFIED**: not retrieved, not stated on the pages read, only inferred, or the extract hid it.
- **CONTRADICTED**: two official sources disagree with each other, or an official page disagrees with a repo assumption.
- "Account-specific" means it can only be confirmed in the Product Owner's own Azure subscription (portal, Quota page, pricing calculator). Nothing here is a price or capacity commitment.
- Facts are in sections 1 to 4. Recommendations are only in section 7. Register effects are in section 5.
- Microsoft Learn pages show an `ms.date` (last substantive review) and an `updated_at`. Both are given where the extract showed them.

## 0. Bottom line

1. **Claude on Azure is real and current.** Claude Sonnet 5.5 and Haiku 5.5 are listed on Microsoft Foundry with deployment names `claude-sonnet-5-5` and `claude-haiku-5-5`, 1M-token context, 128K max output, GA in both hosting options (Learn, ms.date 2026-09-21).
2. **The contract is Anthropic's, not Microsoft's.** Microsoft says Anthropic is the "seller and operator" and "an independent data processor for prompts and outputs", Claude is a "Non-Microsoft Product", and use is "subject to the terms of use Anthropic provides". Microsoft's DPA covers only Microsoft's own Foundry/billing processing. So L-04 stays a review of **Anthropic's** Commercial Terms and DPA, plus Azure Marketplace terms. It does not become "Azure terms".
3. **Two things in Microsoft's and Anthropic's own pages disagree on Sonnet 5.5 residency.** Anthropic says Sonnet 5.5 supports only Global Standard deployments. Microsoft lists Sonnet 5.5 (Hosted on Azure) for Data Zone Standard (US). If the PO wants US-only processing, this must be settled in the real portal before any decision (section 1.4).
4. **A paid pay-as-you-go Azure subscription is required for Claude.** Free-trial, student, credit-only and CSP subscriptions are not supported for Claude. The "$200 free account" idea in `stt-vendor-terms-review.md` L-5 does **not** apply to Claude on Foundry. Default quota on pay-as-you-go for Sonnet 5.5 and Haiku 5.5 is 40 RPM, 40,000 uncached input tokens per minute, 8,000 output tokens per minute.
5. **Azure AI Speech real-time**: Microsoft's data page (ms.date 2026-03-31, unchanged since the earlier review) still says nothing is stored at rest for real-time speech to text. It still does not say whether data is used for training, and the price page text arrived with the dollar amounts masked. Diarization is off by default and uses temporarily retained voice characteristic signals.
6. **Postgres hardening for SB-01 is settable on Azure Database for PostgreSQL Flexible Server** (the parameters I checked are dynamic and not read-only on PG 16/17/18), but restore does **not** carry parameters over, and nothing proves the managed server's log is as quiet as the local canary result. It must be re-proved on a real server.
7. The AWS file `docs/vendors/aws-bedrock-transcribe-verification.md` is now moot as a direction (PO-20aa). It remains useful only as a record.

## 1. Claude in Microsoft Foundry

### 1.1 Sources

| ID | URL | Date on page | Used for |
|---|---|---|---|
| A1 | https://learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/use-foundry-models-claude | ms.date 2026-09-21, updated 2026-09-22 | prerequisites, subscription limits, deploy, troubleshooting |
| A2 | https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/claude-models | ms.date 2026-09-21 | models, features, regions summary |
| A3 | https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/claude-models-hosting-comparison | ms.date 2026-09-21 | Azure-hosted vs Anthropic-hosted |
| A4 | https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/claude-models/data-privacy | ms.date 2026-06-23, updated 2026-06-29 | data handling |
| A5 | https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/claude-models-quotas-limits | ms.date 2026-09-21 | quotas |
| A6 | https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/claude-models-billing | ms.date 2026-10-22, updated 2026-10-27 | CCU billing |
| A7 | https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-from-partners | ms.date 2026-09-21 | per-model parameters, region tables |
| A8 | https://platform.claude.com/docs/en/build-with-claude/claude-in-microsoft-foundry | no date shown | Anthropic's own Foundry page |
| A9 | https://platform.claude.com/docs/en/about-claude/pricing (section "Claude in Microsoft Foundry pricing") | no date shown | CCU, list prices |
| A10 | https://platform.claude.com/docs/en/manage-claude/api-and-data-retention | no date shown | retention and ZDR scope |
| A11 | https://support.claude.com/en/articles/15425695-covered-models | September 1, 2026 | Covered Models |

### 1.2 Models, IDs, limits (Sonnet 5.5 and Haiku 5.5 are the relevant ones)

| Claim | Status | Source |
|---|---|---|
| `claude-sonnet-5-5` and `claude-haiku-5-5` exist on Foundry, GA in **both** "Hosted on Azure" and "Hosted on Anthropic infrastructure". Other current IDs: `claude-opus-5-5`, `claude-opus-5-5-fast` (Azure only), `claude-opus-5`, `claude-opus-4-8`, `claude-sonnet-5`, `claude-haiku-4-5`; Fable 5 and 5.1 are Anthropic-hosted preview. | VERIFIED-SUMMARY | A2, A8 (2026-11-01) |
| Context window 1M tokens, max output 128K, for Sonnet 5.5 and Haiku 5.5. | VERIFIED-SUMMARY | A2, A7 |
| The `model` field in a request carries the **deployment name**, which defaults to the model ID. The deployment name "cannot be changed after creation". | VERIFIED-SUMMARY | A8, A1 |
| Endpoint shape `https://{resource}.services.ai.azure.com/anthropic/v1/messages`; auth by API key (`api-key` or `x-api-key`) or Microsoft Entra ID (scope `https://ai.azure.com/.default`). Header `anthropic-version: 2023-06-01`. | VERIFIED-SUMMARY | A1, A8 |
| .NET: NuGet package `Anthropic.Foundry`, client `AnthropicFoundryClient` with API-key or Entra identity credentials. The C# client builds the base URL from the resource name only. | VERIFIED-SUMMARY | A8 |
| Foundry does **not** return Anthropic's `anthropic-ratelimit-*` headers; manage limits through Azure Monitor. | VERIFIED-SUMMARY | A8 |
| `request-id` and `apim-request-id` response headers exist for support tracing. | VERIFIED-SUMMARY | A8 |
| Sonnet 5.5 and Haiku 5.5 are **not** Covered Models (Covered: Mythos 5.1, Fable 5.1, Mythos 5, Fable 5). Covered Models need 30-day retention and ZDR is not available for them, including on third-party platforms. | VERIFIED-SUMMARY | A11 (2026-09-01), A10 |
| Per-model parameter notes for Sonnet 5.5 and Haiku 5.5: `temperature` and `top_k` are **not supported**; `top_p` must be 0.99 (omit it to get the default); `thinking: {"type":"enabled"}` is "not supported"; `output_format` is supported "only for `thinking={"type":"adaptive"}`". | VERIFIED-SUMMARY | A7 |
| **CONTRADICTED within Microsoft's own pages**: A2's "Thinking and effort" table shows Sonnet 5.5 and Haiku 5.5 with `enabled` = Yes, while A7 says `thinking={"type":"enabled"}` is not supported. | CONTRADICTED | A2 vs A7. Test in a real deployment. |
| Repo impact: the AI gateway must not send `temperature`, `top_k`, or a `top_p` other than 0.99 to a Foundry Sonnet 5.5/Haiku 5.5 deployment. Not checked against the repo's client code in this task. | UNVERIFIED (repo code not read) | A7 |

### 1.3 Hosting options and who processes data

| Topic | Hosted on Azure | Hosted on Anthropic infrastructure | Status / source |
|---|---|---|---|
| Where inference runs | Microsoft's wording: "processed on Azure infrastructure, including request ingress, API services, and GPU inference". Anthropic's wording: "Anthropic-operated service running on Azure infrastructure". | "Anthropic-operated service running on Anthropic infrastructure"; data "might be processed outside of Azure including outside of your selected Azure region". | VERIFIED-SUMMARY, A3, A4, A8 |
| Seller, operator, data processor | Anthropic | Anthropic | VERIFIED-SUMMARY, A3, A4. Anthropic: "Anthropic acts as an independent processor for Microsoft." (A8) |
| Which terms govern retention | "Anthropic's Data Processing Addendum" and "Anthropic's Commercial Terms of Service" (link `https://aka.ms/anthropic_tandc`, **not opened**) | same | VERIFIED-SUMMARY, A3. Whether the aka.ms text is identical to anthropic.com/legal/commercial-terms (reviewed 2026-10-30): UNVERIFIED |
| Microsoft's own processing | Billing, usage, customer contact and transaction data under the "Microsoft Products and Services Data Protection Addendum and applicable Marketplace terms". Microsoft "might share" contact, transaction and usage information with Anthropic. | same | VERIFIED-SUMMARY, A4 |
| What leaves Azure | Anthropic: "prompts and completions remain within Azure. Only usage metadata and content flagged by Anthropic's safety systems egress to Anthropic." Microsoft: "Automatic safeguards flag content that might be sent to Anthropic Trust & Safety for review. Anthropic personnel review customer content on an exceptions-only basis." | n/a (all on Anthropic) | VERIFIED-SUMMARY, A8, A4. The **retention of flagged content** and where it is stored: UNVERIFIED |
| Data at rest | "stored in the selected Azure geography" | not stated | VERIFIED-SUMMARY, A4 |
| Processing scope | "Global" or "DataZone" deployment (see 1.4) | Global Standard only | VERIFIED-SUMMARY, A3 |
| SLA | "Anthropic is the operator and provides any SLA." No SLA document or number found. | same | VERIFIED-SUMMARY (absence), A3 |
| Support | Microsoft Support for both | same | VERIFIED-SUMMARY, A3 |
| Training on customer data | **Not stated on any Microsoft page read.** Anthropic's Commercial Terms say Anthropic may not train on Customer Content (reviewed on anthropic.com, 2026-10-30). Whether that clause is in the version Foundry presents: UNVERIFIED. | same | UNVERIFIED for the Foundry path |
| Retention length | Anthropic's retention page says it "covers ... Claude in Microsoft Foundry ... where Anthropic is the data processor" (A10). The direct-API figure (30 days, flagged content longer) is in `anthropic-terms-review.md` S3. An explicit statement of the retention period **for Foundry Sonnet 5.5** was not found. | same | The page-scope statement is VERIFIED-SUMMARY; the 30-day figure applying to Foundry is **UNVERIFIED (inference)** |
| Zero data retention | A1 says ZDR is set at the Azure **subscription** level on Foundry, "Anthropic independently manages data retention for Claude on Azure, so Microsoft can't change this setting". Eligibility remains Anthropic's approval (`anthropic-terms-review.md` F-M4). | same | VERIFIED-SUMMARY, A1; approval for this account UNVERIFIED |
| Abuse monitoring by Microsoft | Not mentioned on the pages read. A2: "Foundry doesn't provide built-in content filtering for Claude models at deployment time." | not mentioned | UNVERIFIED (whether Microsoft retains any prompt data for abuse monitoring) |
| Logging in your subscription | Azure Monitor and Log Analytics are available. Anthropic: "Enabling logging does not provide Microsoft or Anthropic access to your content beyond what's necessary for billing and service operation." Anthropic also recommends logging at least 30 days. | same | VERIFIED-SUMMARY, A8. Invariant 10 (no raw lecture logging) still governs: metadata only. |

Weaker, non-official evidence seen in search results and **not relied on**: Microsoft Q&A threads (community-level) say Azure OpenAI "zero data retention" guarantees do not apply to Anthropic models, and that retention details for Claude on Foundry are not defined in Microsoft's documentation. That matches the gap above but is not an official statement.

### 1.4 Deployment types, regions and the Sonnet 5.5 conflict

| Claim | Status | Source |
|---|---|---|
| Deployment types: **Global Standard** (all Claude models and versions) and **Data Zone Standard (US)** (Hosted on Azure only, for some models). Microsoft on Global: "Microsoft processes prompts and responses in any Azure region where you deploy the model." On Data Zone: "United States (data processed anywhere within the US)". | VERIFIED-SUMMARY | A7, A2 |
| Anthropic: Data Zone Standard "keeps inference within the United States and is equivalent to setting `inference_geo: "us"`" and "applies the same **1.1x** pricing multiplier". | VERIFIED-SUMMARY | A8, A9 |
| Data Zone Standard (US) list per Microsoft: Hosted on Azure `claude-haiku-4-5`, `claude-haiku-5-5`, `claude-opus-5-5`, `claude-opus-5-5-fast`, `claude-opus-5`, `claude-opus-4-8`, `claude-sonnet-5`, **`claude-sonnet-5-5`**. | VERIFIED-SUMMARY | A2 |
| **Anthropic**: "Claude Sonnet 5.5 supports only Global Standard deployments" and "For Claude Sonnet 5.5, select **Global**." | VERIFIED-SUMMARY | A8 |
| **CONTRADICTED**: A2, A5 (quota rows "claude-sonnet-5-5 Data Zone Standard (US)") and A7 (Data Zone table lists `claude-sonnet-5-5` version 2) say Sonnet 5.5 has a US Data Zone option. A8 says it does not. Additionally A7's **Global Standard** table lists `claude-sonnet-5-5` and `claude-haiku-5-5` only as **version 1** (Hosted on Anthropic), not version 2, which would mean Azure-hosted Sonnet 5.5 exists **only** in Data Zone, the opposite of A8. | CONTRADICTED | A2, A5, A7 vs A8 |
| Consequence: whether a **US-only, Azure-hosted** Sonnet 5.5 can be had, and whether a Global Standard Sonnet 5.5 is Azure- or Anthropic-hosted, is **unknown** until the PO's portal shows the deploy options. Haiku 5.5 Azure-hosted does list Data Zone (US) in both A2 and A7; A8 makes no exception for it. | UNVERIFIED (account-specific) | n/a |
| US regions where partner Claude models are listed (Global tab, version 1): `centralus`, `eastus`, `eastus2`, `northcentralus`, `southcentralus`, `westcentralus`, `westus`, `westus3` (not `westus2`, no Canadian region). Data Zone tab: the same eight regions. Europe lists only `swedencentral`. | VERIFIED-SUMMARY | A7 (2026-09-21) |
| The Foundry resource and project must be "in a supported deployment location". | VERIFIED-SUMMARY | A1 |
| Billing country must be one where Anthropic sells the models; Anthropic's "Supported Regions Policy" may apply (`https://aka.ms/supported_anthropic_regions`, not opened). | VERIFIED-SUMMARY / link UNVERIFIED | A1 |

### 1.5 Subscription eligibility (new, important for a personal project)

| Claim | Status | Source |
|---|---|---|
| Claude on Foundry needs "a paid Azure subscription with a billing account in a country or region where Anthropic offers the models". **Not supported**: Cloud Solution Provider subscriptions; South Korea enterprise accounts; "Azure subscriptions that don't have an active pay-as-you-go billing method (for example, student, free trial, or startup credit-based accounts)"; "Sponsored subscriptions that only use Azure credits" (a card on file is charged instead). | VERIFIED-SUMMARY | A1, A7, A3 |
| The first deployment requires accepting **Azure Marketplace** terms ("Agree and Proceed", choose an industry) and Marketplace permissions (Owner or Contributor include them). | VERIFIED-SUMMARY | A1, A7 |
| Consequence: the "free account" cost shape in `stt-vendor-terms-review.md` L-5 is available for Speech but **not for Claude**. Claude spend is real money from the first token. | VERIFIED-SUMMARY (derived) | A1 |
| Who may hold the subscription and accept Marketplace/Anthropic terms: the adult PO, per the earlier M-2 finding. Whether an individual (not an organisation) may buy through Marketplace for this use: UNVERIFIED. | UNVERIFIED | n/a |

### 1.6 Contract stack (Anthropic commercial terms vs Microsoft terms)

| Claim | Status | Source |
|---|---|---|
| Claude is a Non-Microsoft Product under the Microsoft Product Terms: "Models from partners and community that Anthropic sells and operates are Non-Microsoft Products under the Product Terms." | VERIFIED-SUMMARY | A2, A7 |
| "Your use of the Claude models is subject to the terms of use Anthropic provides for Claude models and APIs." | VERIFIED-SUMMARY | A3, A4 |
| Anthropic's Commercial Terms and DPA are named as governing data processing for the Anthropic-hosted option (A4) and as governing retention for both options (A3). Microsoft's DPA is named for Microsoft's own Foundry/billing/Marketplace processing. | VERIFIED-SUMMARY | A3, A4 |
| Which document wins in a conflict between Anthropic's terms, the Marketplace terms and the Microsoft Product Terms: **not stated on the pages read.** | UNVERIFIED | n/a |
| Compliance evidence: "Refer to Anthropic Trust Center" (`https://trust.anthropic.com/`), not opened in this pass. | UNVERIFIED | A3 |
| Anthropic's HIPAA statement for Foundry is irrelevant to scope (no PHI) and is not used. | n/a | A10 |
| The earlier F-M1 concern (an individual, non-commercial customer under terms marked "not for consumer use") still applies. A new variable: purchase is via Azure Marketplace on a pay-as-you-go card account. | carried over | `anthropic-terms-review.md` |

### 1.7 Pricing model and quotas

| Claim | Status | Source |
|---|---|---|
| Billing unit is the **Claude Consumption Unit (CCU)**, "fixed price, published on the Claude offer on Azure Marketplace" (A6). Anthropic states **$0.01 USD per CCU** (100 CCU = $1.00) (A9). Metered hourly, invoiced monthly in arrears, no prepaid credits, MACC-eligible. Tokens are rated at Anthropic's per-model rates then converted. | VERIFIED-SUMMARY | A6, A9 |
| "CCU is a change in billing format, not in price" (A6 FAQ). Foundry token rates are "the same as Claude API pricing" (A9). | VERIFIED-SUMMARY | A6, A9 |
| Anthropic list prices (per million tokens): **Sonnet 5.5** base input $2, 5-minute cache write $2.50, 1-hour cache write $4, cache hit $0.10, output $10. **Haiku 5.5** (prompts up to 100,000 tokens) input $0.10, cache write $0.125 / $0.20, cache hit $0.01, output $0.50; (prompts over 100,000 tokens) input $0.50, output $2.50. | VERIFIED-SUMMARY (numbers appear on the official page; re-read live before use) | A9, 2026-11-01 |
| Data Zone (US) deployments: "same 1.1x pricing multiplier" on all token categories; on Haiku 5.5 it also applies to the over-100k tier. | VERIFIED-SUMMARY | A9 |
| Price actually shown to **this** subscription in the Foundry portal Pricing tab and Azure Cost Management: **UNVERIFIED, account-specific.** Any cost estimate built from the list prices above is an **estimate**, not a quote. | UNVERIFIED | A6 |
| Default quota is **per subscription**, shared across regions for Global Standard, shared per data zone for Data Zone Standard. Measured in RPM, uncached input tokens per minute (ITPM) and output tokens per minute (OTPM); cache reads do not count toward ITPM; cache creation does. | VERIFIED-SUMMARY | A5 |
| **Pay-as-you-go defaults, Sonnet 5.5 and Haiku 5.5** (Global Standard and Data Zone Standard US, Hosted on Azure): **40 RPM, 40,000 ITPM, 8,000 OTPM**. (For comparison the table shows Sonnet 4.6 / Haiku 4.5 at 80 / 80,000 / 16,000.) | VERIFIED-SUMMARY | A5 (ms.date 2026-09-21) |
| Enterprise and MCA-E defaults for the same models: 10,000 RPM, 10,000,000 ITPM, 2,000,000 OTPM. Free trial: 0 for every Claude model (and free trial subscriptions are unsupported anyway). Fable 5 and 5.1 default to 0 on pay-as-you-go. | VERIFIED-SUMMARY | A5 |
| Increase: submit `https://aka.ms/oai/stuquotarequest`; "evaluated individually and aren't guaranteed to be approved". Whether it accepts a personal email address: UNVERIFIED. (The separate Azure Speech increase form states "Applications submitted with a personal email address ... aren't accepted"; do not assume the Claude form differs.) | VERIFIED-SUMMARY / UNVERIFIED | A5; Speech quotas page |
| Whether 40 RPM / 40,000 ITPM is enough for rolling summaries plus Quick recap for one student: **not assessed**. A rolling-summary prompt re-sends a transcript window each time, so ITPM, not RPM, is the likelier limit. This needs the measured pilot. | UNVERIFIED | engineering note |

### 1.8 Feature gaps compared with the direct API

| Feature | Foundry status | Source |
|---|---|---|
| Streaming (SSE) | Supported | A2 |
| Tool use, client-executed custom tools | Supported ("Custom tools plus Anthropic-defined bash, text editor, computer use, memory") | A2 |
| Structured outputs (`output_config.format` JSON schema, and `strict: true` tool use) | Listed as supported on both hosting versions. Caveat from A7: `output_format` only with adaptive thinking on Sonnet 5.5/Haiku 5.5. | A2, A7 |
| Citations (including `search_result` blocks) | Supported | A2 |
| Prompt caching (5 minute, 1 hour, automatic) | Supported. Minimum cacheable prompt for Sonnet 5.5/Haiku 5.5: not stated on the pages read (Mythos pages state 512 or 2048). | A2 |
| Token counting API | Supported | A2 |
| PDF input | Supported as base64 or URL (Hosted on Azure accepts base64 or URL images) | A2 |
| Context editing, tool search, fine-grained tool streaming | Supported (beta headers) | A2 |
| Message Batches API | **Not supported** | A8 |
| Files API | **Not supported on Hosted on Azure**; available on Hosted on Anthropic | A8, A2 |
| Models API, Admin API, Compliance API, Managed Agents, Advisor tool, server-side `fallbacks` | **Not supported** | A8 |
| Code execution, Agent Skills, programmatic tool calling, web search/fetch beyond `web_search_20250305` / `web_fetch_20250910` | **Not supported on Hosted on Azure** (400 error by design) | A8 |
| Mid-conversation `role:"system"`, per-turn effort controls, `task_budget` | Beta | A2 |
| Built-in content filtering | None at deployment time | A2 |

Not checked: whether this repo's `AiGateway`/client uses anything on the "not supported" list (Batches, Files API, `temperature`). That is for `ai-agent-engineer`.

## 2. Azure AI Speech (real-time, US regions)

### 2.1 Sources

| ID | URL | Date on page |
|---|---|---|
| S1 | https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/speech-service/speech-to-text/data-privacy-security | ms.date 2026-03-31, updated 2026-08-26 |
| S2 | https://learn.microsoft.com/en-us/azure/ai-services/speech-service/speech-to-text | ms.date 2026-10-28, updated 2026-11-01 |
| S3 | https://learn.microsoft.com/en-us/azure/ai-services/speech-service/speech-services-quotas-and-limits | ms.date 2026-10-02 |
| S4 | https://learn.microsoft.com/en-us/azure/ai-services/speech-service/regions | ms.date 2026-10-28, updated 2026-11-01 |
| S5 | https://learn.microsoft.com/en-us/azure/ai-services/speech-service/get-started-stt-diarization | ms.date 2026-01-30 (last 22,843 characters not read) |
| S6 | https://azure.microsoft.com/en-us/pricing/details/cognitive-services/speech-services/ | no date; **prices arrived masked** |

### 2.2 Facts

| Topic | Claim | Status |
|---|---|---|
| Storage | "For real-time speech to text, audio input is processed only on the Azure's server memory, and no data is stored at rest. All data in-transit are encrypted." And: "When doing real-time speech to text, fast transcription, pronunciation assessment, and speech translation, Microsoft does not retain or store the data provided by customers." (S1) | VERIFIED-SUMMARY. Same text as the earlier review: **no change since 2026-10-30**. |
| Batch | Output may sit in Microsoft storage if you name none; `timeToLive` and a delete API exist. Not used by Tether live path. | VERIFIED-SUMMARY (S1) |
| Training on customer audio | S1 does not state it. S2 says the base model is trained "with data Microsoft owns" and that custom speech lets you tailor it; neither says customer audio is excluded. | **UNVERIFIED** (still the earlier gap) |
| Diarization | Off by default. When enabled the engine "extracts unique voice characteristics signals" that are "used and temporarily retained for the sole purpose of annotating" speakers and then discarded. No "speaker identity recognition enrollment or the ability to track unique speakers across multiple audio files". (S1) | VERIFIED-SUMMARY |
| Biometric duty | "If you are using Microsoft products or services to process Biometric Data, you are responsible for: (i) providing notice ...; (ii) obtaining consent ...; and (iii) deleting the Biometric Data" (S1). Whether diarization output counts as Biometric Data under any law: for counsel. | VERIFIED-SUMMARY / counsel |
| Customer duty | You "are required to obtain all necessary permissions for processing of the data" and audio "may also be regulated under various communications laws". (S1) | VERIFIED-SUMMARY (unchanged) |
| Real-time interim and final | SDK events: interim (`Transcribing`) text grows as audio arrives; final (`Transcribed`) once per utterance with punctuation (S5). S2: "Instant transcription with intermediate results for live audio inputs." | VERIFIED-SUMMARY |
| Real-time diarization | `ConversationTranscriber`. Speakers appear as "Guest-1, Guest-2". Interim results show `Speaker ID=Unknown` unless the property `SpeechServiceResponse_DiarizeIntermediateResults` is set to "true". REST for short audio does not support real-time diarization. SDK samples in Python, C#, JavaScript, Java, C++ and Go. (S5) | VERIFIED-SUMMARY |
| Speaker count | "can identify up to 35 different speakers" (S2, stated under the fast transcription/diarization overview, so whether it applies to **real-time** is unclear). | UNVERIFIED for real-time |
| Real-time diarization session cap | "240 minutes per session" for real-time diarization. (S3) | VERIFIED-SUMMARY |
| Concurrency | Real-time STT: base-model endpoint **1 (Free F0, not adjustable)** or **100 (Standard S0, default, adjustable)**; counted together with speech translation. Increase through a support form; the existing value is not visible in the portal. (S3) | VERIFIED-SUMMARY |
| Channels | Real-time STT can transcribe up to two channels independently. (S2) | VERIFIED-SUMMARY |
| Regions (real-time transcription = yes) | US: `centralus`, `eastus`, `eastus2`, `northcentralus`, `southcentralus`, `westcentralus`, `westus`, `westus2`, `westus3` (S4, 2026-10-28). "Azure Speech stores and processes speech data in the region where you create your resource." (Voice Live is an exception.) | VERIFIED-SUMMARY |
| Newer options | Azure LLM Speech and MAI-Transcribe-2 exist as **fast (file) transcription** models; `mai-transcribe-2-streaming` is listed as **preview** and only through Voice Live in some regions (S2, S4). Not a real-time STT option for this project without a separate review. | VERIFIED-SUMMARY |
| Pricing | The pricing page text returned "$-" for the real-time hourly rate, diarization add-on and commitment tiers. What is readable: the free tier is **5 audio hours per month**, shared by Standard and Custom real-time, not batch; diarization is an enhanced add-on "billed per hour per feature"; commitment tiers exist at 2,000 / 10,000 / 50,000 hours per month. (S6) | Amounts **UNVERIFIED** (not in the extract). No price is stated in this file. |
| Latency | No published latency number on the pages read. | UNVERIFIED, must be measured (`MEASUREMENT_PROTOCOL.md`) |
| Browser access, token lifetime, JS SDK specifics | Not re-checked in this pass; see `verification-report.md`. | UNVERIFIED here |
| Microsoft DPA, subprocessor list, SLA | Not read in this pass (and only an index page earlier). Azure AI Speech is a Microsoft product (unlike Claude), so the Microsoft DPA should apply; not confirmed. | UNVERIFIED |

### 2.3 What changed versus `docs/legal/stt-vendor-terms-review.md` (2026-10-30)

- **No change** in the data-at-rest, biometric, customer-duty or region statements; the privacy page still carries ms.date 2026-03-31.
- **New**: the five-hour monthly Free tier allowance and the commitment tiers were seen; the F0 tier has a hard 1-concurrent-stream limit; quota increases require an organisational email.
- **New**: real-time diarization has a 240-minute session cap; interim speaker labels are "Unknown" unless a property is set.
- **New**: LLM Speech/MAI options exist but are file-based or preview.
- **Still open**: training use, price, DPA, SLA, latency.

## 3. Hosting and storage on Azure (high level)

### 3.1 Sources

| ID | URL | Date on page |
|---|---|---|
| H1 | https://learn.microsoft.com/en-us/azure/container-apps/overview | ms.date 2026-07-07 |
| H2 | https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview | ms.date 2026-10-09 |
| H3 | https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets | ms.date 2026-09-04 |
| H4 | https://learn.microsoft.com/en-us/azure/postgresql/overview (fetched via /flexible-server/overview) | ms.date 2026-01-09, updated 2026-09-05 |
| H5 | https://learn.microsoft.com/en-us/azure/postgresql/backup-restore/concepts-backup-restore | ms.date 2026-07-05 |
| H6 | https://learn.microsoft.com/en-us/azure/postgresql/security/security-data-encryption | ms.date 2026-09-18 |
| H7 | https://learn.microsoft.com/en-us/azure/postgresql/monitor/concepts-logging | ms.date 2026-07-13 |
| H8 | https://learn.microsoft.com/en-us/azure/postgresql/parameters/parameters-reporting-logging-what-log and .../parameters-reporting-logging-when-log | ms.date 2026-07-10 |
| H9 | https://learn.microsoft.com/en-us/azure/storage/common/storage-service-encryption | ms.date 2026-08-11 |
| H10 | https://learn.microsoft.com/en-us/azure/key-vault/general/soft-delete-overview | ms.date 2026-04-10 |

**Not examined at all in this pass**: Azure App Service (so no App Service vs Container Apps comparison), Blob Storage backup/soft-delete/versioning, Key Vault pricing and SKU choice, Azure Front Door/WAF, private networking cost, any hosting price, Azure SLAs.

### 3.2 Container Apps (fits an API plus web containers)

| Claim | Status | Source |
|---|---|---|
| Serverless container platform; HTTP/TCP ingress; autoscaling by HTTP, CPU/memory or KEDA scalers; "Most applications can scale to zero" (apps scaling on CPU or memory cannot). | VERIFIED-SUMMARY | H1 |
| HTTP ingress supports HTTP/1.1, HTTP/2, **WebSocket** and gRPC, TLS 1.2/1.3 terminated at ingress, and states "Request time out is 240 seconds". Session affinity ("sticky sessions") exists. | VERIFIED-SUMMARY | H2 |
| Whether the 240-second timeout cuts an **idle or long-lived WebSocket/SignalR** connection is **not stated** on the page. The repo's audio relay and SignalR hub need reconnect handling anyway (invariant 8). | UNVERIFIED | H2 |
| Secrets can be a **Key Vault reference** using a managed identity with the role Key Vault Secrets User. Without a version in the URI, the app picks up a new version "within 30 minutes" and active revisions that use it in an environment variable are restarted automatically. Raw secret values in a Container Apps secret are discouraged for production. Roles such as Container Apps Contributor can list secret values. | VERIFIED-SUMMARY | H3 |
| Container logs go to Azure Log Analytics (monitoring feature). What the API writes to stdout becomes retained platform logs, so the log-hygiene rules in the playbook apply there too. | VERIFIED-SUMMARY (feature) / policy implication is engineering | H1 |

### 3.3 Azure Database for PostgreSQL Flexible Server

| Claim | Status | Source |
|---|---|---|
| Fully managed; Burstable, General Purpose, Memory Optimized tiers; stop/start (billing for compute stops; stays stopped up to seven days); built-in PgBouncer on port 6432; TLS 1.2+ enforced by default; private access through a virtual network. | VERIFIED-SUMMARY | H4 |
| US regions listed (Intel compute): Central US, East US, East US 2, North Central US, South Central US, West Central US, West US, West US 2, West US 3. Zone-redundant HA: yes for Central US, East US, East US 2, South Central US, West US 2, West US 3; no for North Central US, West Central US, West US. **New zone-redundant HA deployments are "temporarily blocked"** (marker `$`) in East US, East US 2, South Central US and West US 2. Geo-redundant backup: yes for all US regions listed **except West US 3** (listed ❌). | VERIFIED-SUMMARY (table as extracted; re-read live) | H4 |
| Encryption at rest: "always encrypts all data at rest", including "system and user databases, server logs, write-ahead log segments, and backups"; AES-256 via Azure storage encryption; **service-managed keys are the default**. Customer-managed keys (Key Vault or Managed HSM) can be chosen **only at server creation** and cannot be reverted. | VERIFIED-SUMMARY | H4, H6 |
| CMK downside: if the key becomes unreachable the server goes **Inaccessible** and denies all connections (within about 60 minutes). Requires soft delete, purge protection, managed identity and key rotation discipline. | VERIFIED-SUMMARY | H6 |
| Backups: daily snapshot plus continuous WAL archiving; default retention **7 days**, up to **35**; AES-256 encrypted; zone-redundant storage where zones exist; geo-redundant option **only at creation**. "The delay RPO ... can be up to five minutes" in general; geo-restore RPO up to one hour. "The overall recovery time usually takes from few minutes up to a few hours." | VERIFIED-SUMMARY | H5 |
| Restore always creates a **new server**; the original is not overwritten. "The values of all parameters on the original server aren't automatically applied to the new server." Firewall rules, private endpoints, logins and alerts are also not copied. Public-access and private-VNet servers cannot swap on restore. | VERIFIED-SUMMARY | H5 |
| Backups cannot be exported; deleting a server **deletes its backups**; a resource lock is advised. A deleted server can be restored by a documented procedure. | VERIFIED-SUMMARY | H5 |
| Long-term retention up to 10 years through Azure Backup (logical `pg_dump`, weekly/monthly/yearly, max 1 TiB, restores as files). Burstable tier and SSDv2 storage do not support on-demand backups. | VERIFIED-SUMMARY | H5 |
| Any backup/restore **drill** has been run: **no**. The PITR claim is vendor documentation, not evidence for G5. | status | G5 NOT_RUN |

### 3.4 What SB-01 needs on a managed server

Repo baseline (from `docker-compose.yml` and `docs/DEVELOPMENT.md`): `log_error_verbosity=terse`, `log_min_error_statement=panic`, `log_statement=none`, `log_duration=off`, `log_min_duration_statement=-1`, `log_min_duration_sample=-1`, `log_parameter_max_length=0`, `log_parameter_max_length_on_error=0`.

| Repo parameter | Azure Flexible Server (PG 16 tables; PG 17 and 18 tables agree on type) | Status |
|---|---|---|
| `log_error_verbosity=terse` | Allowed `terse, default, verbose`; **default `default`**; dynamic | VERIFIED-SUMMARY (settable, **not default**) |
| `log_min_error_statement=panic` | Allowed includes `panic`; **default `error`** (so the failing statement text **is** logged by default); dynamic | VERIFIED-SUMMARY (settable, **not default**) |
| `log_statement=none` | Allowed `none, ddl, mod, all`; default `none`; dynamic | VERIFIED-SUMMARY |
| `log_duration=off` | default `off`; dynamic | VERIFIED-SUMMARY |
| `log_min_duration_statement=-1` | default `-1`; dynamic | VERIFIED-SUMMARY |
| `log_min_duration_sample=-1` | default `-1`; dynamic (PG 14 and later) | VERIFIED-SUMMARY |
| `log_parameter_max_length=0` | Allowed `-1` to `1073741823`; **default `-1` (no truncation)**; dynamic | VERIFIED-SUMMARY (settable, **not default**) |
| `log_parameter_max_length_on_error=0` | default `0`; dynamic | VERIFIED-SUMMARY |
| `log_min_messages` | default `warning`; dynamic (not in the repo list; relevant to what else is written) | VERIFIED-SUMMARY |
| `log_transaction_sample_rate` | read-only, `0` | VERIFIED-SUMMARY |

Other facts relevant to SB-01:

- Azure sends logs through **diagnostic settings** to Azure Monitor Logs, Event Hubs or Azure Storage; the log table `PostgreSQLLogs` includes **Message, Detail, ColumnName, SchemaName, DatatypeName** fields. "You can configure most, but not all, Postgres logging parameters." Any role with read access to the server can download logs; "logs might contain sensitive information, such as credentials." Log Analytics keeps data free for 31 days, then charges. Messages over 65 KB are not captured. (H7, VERIFIED-SUMMARY)
- Changing server parameters on the original server is not enough after a restore: **re-apply the logging parameters to every restored or replica server** (H5). Put this in the restore runbook.
- `pgaudit` is optional and off unless enabled; its effect on statement text was not examined. **Query Store / pg_stat_statements** (query text capture) was **not examined**: a second possible leak path for lecture text that the local canary does not test. UNVERIFIED.
- **What was NOT proved**: that a managed server with these parameters omits the `DETAIL: Failing row contains (...)` line and that the `Detail` column in `PostgreSQLLogs` stays empty. The repo's canary script (`docs/ops/db-log-canary.sh`) takes a local container name and cannot read a managed server's log as written. `docs/ops/ci-matrix.md` already says a deployed database needs the same settings and "is not covered by CI". UNVERIFIED until a canary run against a real server.
- Terminology: the repo docs say "parameter group", which is AWS RDS wording. On Azure they are **server parameters**.

### 3.5 Blob Storage and Key Vault

| Claim | Status | Source |
|---|---|---|
| Azure Storage encrypts all data at rest with 256-bit AES (GCM for uploaded objects), "enabled for all storage accounts. You can't disable Azure Storage encryption", default key is Microsoft-managed; customer-managed keys (Key Vault or Managed HSM) and per-container/blob encryption scopes are options; optional infrastructure (double) encryption. No extra cost for encryption. | VERIFIED-SUMMARY | H9 |
| Blob soft delete, versioning, immutability, replication/redundancy options, region list, and price: **not examined.** | UNVERIFIED | n/a |
| Key Vault: soft delete is on by default for new vaults and cannot be turned off; retention 7 to 90 days (default 90) set only at creation; **purge protection is not enabled by default** and is "recommended when using keys for encryption"; purge needs a special role. Soft-deleting a vault removes RBAC role assignments, which must be recreated on recovery. | VERIFIED-SUMMARY | H10 |
| Key Vault SKUs, key vs secret operation pricing, throttling limits, and whether the repo's `ITokenCipher` KEK design maps to Key Vault wrap/unwrap calls: **not examined.** | UNVERIFIED | n/a |
| Whether the app needs Blob Storage at all: PO-20r discards uploaded originals and PO-20ab keeps the student's files in Google Docs. No document in the repo reviewed in this pass names a Tether-side blob need. | UNVERIFIED (design question) | DECISIONS.md |

## 4. Mismatches to flag between sources

| # | Item | Status |
|---|---|---|
| T-1 | Sonnet 5.5 Data Zone (US) availability: Anthropic A8 says Global only; Microsoft A2/A5/A7 list a Data Zone option. A7's Global table lists Sonnet 5.5 and Haiku 5.5 only as version 1. | **CONTRADICTED** |
| T-2 | Thinking `enabled` for Sonnet 5.5/Haiku 5.5: A2 table says Yes; A7 says not supported. | **CONTRADICTED** |
| T-3 | Where "Hosted on Azure" runs: A8 "Anthropic-operated service running on Azure infrastructure"; A2 "run on Azure infrastructure end-to-end". Not opposed, but they describe the operator differently. Who has operator access to prompts inside Azure is **not** stated. | UNVERIFIED |
| T-4 | Anthropic says prompts and completions "remain within Azure"; Microsoft says Anthropic personnel review content "on an exceptions-only basis". Compatible, but the flagged-content path leaves Azure to Anthropic's systems (A8) and its retention is unstated. | UNVERIFIED |
| T-5 | Microsoft's DPA is named only for Microsoft's own processing. A reader may assume it covers prompts; it does not, per A3/A4. | VERIFIED-SUMMARY (clarification) |
| T-6 | Earlier docs say Azure's free account is the safe cost shape; Claude on Foundry does not accept free/credit-only subscriptions. | CONTRADICTED vs `stt-vendor-terms-review.md` L-5 for Claude (not for Speech) |

## 5. What changes in the legal rows and gate G2

These are **recommended wording and evidence changes** for the Lead and `legal-compliance-reviewer`. I changed no register file.

**L-04 (Anthropic terms and DPA), currently DEFERRED, NO-GO for real content.** Should stay NO-GO. What changes:
- The governing documents become: Anthropic Commercial Terms and DPA as presented through Azure Marketplace (link `aka.ms/anthropic_tandc`, to be opened and saved), the Azure Marketplace purchase terms, and the Microsoft Product Terms (Claude is a Non-Microsoft Product). Microsoft's DPA covers Microsoft's own processing only. Precedence is unstated.
- F-M1 (individual, non-commercial) still applies, plus a new question: can an individual buy Claude through Marketplace on a card-backed pay-as-you-go account.
- F-M3/F-M4 (retention, ZDR): Anthropic's retention policy applies to Foundry (A10); ZDR is Anthropic's approval and is a subscription-level setting on Foundry; Sonnet 5.5/Haiku 5.5 are not Covered Models (A11). The "30 days" figure for Foundry is inferred, not stated.
- F-M5 (subprocessors): the question now includes whether Microsoft/Azure appears on Anthropic's subprocessor list for the Hosted-on-Azure option (list still not read).
- F-M6 (`inference_geo`) maps to **Global Standard vs Data Zone Standard (US) at 1.1x**, subject to the Sonnet 5.5 conflict (T-1).
- New: no explicit statement of training use on Microsoft pages; new: Azure-side abuse monitoring unknown; new: the flagged-content path.
- F-M7 to F-M10 (Usage Policy, education high-risk area, minors, feedback/opt-in) are unchanged.
- The Bedrock-specific content in `anthropic-terms-review.md` (and the whole AWS verification file) no longer describes the chosen path.

**L-05 (STT vendor terms), OPEN.** Stays OPEN. Azure AI Speech is the direction, so the Azure column of `stt-vendor-terms-review.md` becomes the primary subject: still needed are the Microsoft DPA text, the training-on-data statement, the price, an SLA, and counsel's view on diarization as biometric. The Amazon Transcribe comparison and the "pending the Bedrock/Transcribe clarification" wording in `legal-register.md` line 42 are obsolete. Keep diarization OFF by default (PO-15d already treats all speakers the same).

**L-13 (cross-border storage), OPEN.** The question narrows to Azure region choice for: (a) the Foundry resource and Claude deployment (Global vs Data Zone US), (b) the Speech resource (its region is where audio is processed), (c) Postgres (region plus whether geo-redundant backup pairs it to another US region), (d) Key Vault, (e) any Blob account. Plausible US regions common to Speech, Claude partner models and Postgres: `eastus`, `eastus2`, `centralus`, `southcentralus`, `westus` (Speech STT, Claude tables and Postgres lists all contain them; `westus2` has Speech and Postgres but no Claude in the A7 Global table; `westus3` has Postgres without geo-redundant backup). That intersection is a fact from the tables read, not a recommendation of a region. Google Docs/Drive/Calendar remain Google-held by PO-20ab, so a cross-provider flow remains; counsel view still needed.

**Gate G2 (vendor and Claude cost/limits selection).** Stays **UNVERIFIED**. Candidate set is now narrow: Claude via Foundry (Sonnet 5.5, Haiku 5.5) and Azure AI Speech real-time. Evidence still missing is in section 6. No spend or capacity is committed. G1 (consent) is unaffected and stays **BLOCKED**.

## 6. Evidence that needs the Product Owner's real Azure account (no spend implied until you decide)

1. Portal deployment screen for `claude-haiku-5-5` and `claude-sonnet-5-5`: which hosting versions and region scopes (Global, Data Zone US) are offered, and the Model version dropdown. This resolves T-1.
2. Foundry Quota page for the subscription: actual RPM / ITPM / OTPM for both models.
3. Foundry portal Pricing tab for each Claude model and Azure Cost Management view; Azure pricing calculator for Speech real-time and diarization (the amounts were masked in my fetch).
4. The Marketplace terms screen shown at "Agree and Proceed" (save with date), and the full text behind `aka.ms/anthropic_tandc`. **Clarification (PO-20an, 2026-11-01):** viewing the deploy options and accepting terms are separate steps. The PO's agreement covers opening the account and taking screenshots only. Do NOT click "Agree and Proceed" or accept any Marketplace or Anthropic terms for this evidence; capture the screen and stop. Whether the model options and terms text are visible without accepting is UNVERIFIED; if they are not, record that instead of accepting.
5. The subscription type (must be pay-as-you-go) and billing country; whether the subscription has ZDR or data-retention enabled (A1 troubleshooting row).
6. Speech resource: region, tier (F0 vs S0), quota (support request shows the current concurrency).
7. A Postgres flexible server in a scratch resource group (no real data): parameter set applied, then a canary run reading the diagnostic log (`PostgreSQLLogs`, `Detail` column), plus a PITR drill and a note whether parameters survived the restore.
8. Key Vault: SKU, soft-delete retention, purge protection setting, and a managed-identity wrap/unwrap test.

## 7. Recommendations (not facts, not decisions)

1. **Keep the provider interfaces.** `ILlmProvider` configuration should carry `{ provider: "anthropic-direct" | "foundry", deploymentName, endpoint, deploymentScope: "global" | "datazone-us", hosting: "azure" | "anthropic" }`. The model ID and the deployment name are separate strings; request parameters must be per-provider (no `temperature`, `top_k`, or `top_p` other than 0.99 on Foundry Sonnet 5.5/Haiku 5.5, no Batches or Files API).
2. **Prefer Hosted on Azure** where it is offered, because Microsoft states data at rest stays in the selected geography and Data Zone (US) is available. Treat Sonnet 5.5 as unproven for US-only until T-1 is settled in the portal. Haiku 5.5 is listed with a US Data Zone by both vendors' pages, so the rolling-summary path (ADR-009) is the lower-risk first target. Do not pick the Anthropic-hosted option for real content without a written reason, since its pages say data may leave Azure and the region.
3. **Treat "no free tier for Claude" as a budget fact.** Before the first Claude call, set an Azure budget alert and the existing per-session AI cap (PO-20t) and keep the mock default (startup guard) until the PO authorises spend.
4. **Speech:** keep Azure real-time STT behind the existing `ISttProvider`. Keep diarization off. Make "diarization off" and "no SDK logging" startup preconditions. Do not enable SDK transcription logging (local leak path, `stt-vendor-terms-review.md` M-7).
5. **Hosting shape for a one-user app:** Container Apps (scale to zero, Key Vault references via managed identity) plus Postgres Flexible Server (Burstable, 35-day retention, resource lock) is a plausible small-footprint match, but I have not compared it with App Service, and have no prices. Test the 240-second ingress timeout against a real SignalR/WebSocket session before choosing.
6. **Do not provision Blob Storage unless a concrete need exists.** PO-20r and PO-20ab leave no obvious Tether-side file store.
7. **SB-01 deployment checklist** (answers PO-20p): set the eight parameters above through IaC (not by hand), re-apply them after any restore or replica creation, run the canary against the managed server's diagnostic log, and add Query Store/pg_stat_statements to the review. Keep Log Analytics access narrow, since any server reader can download logs.
8. **Customer-managed keys for Postgres add an outage mode** (server goes Inaccessible if the key is lost) and are irreversible after creation. For a one-user pilot, service-managed keys (the default, AES-256) with a Key Vault used only for the app's token KEK looks proportionate. The PO decides; this ties to open item Q5 in `docs/security/google-tokens-and-data-residency.md`.
9. Ask a human to re-read every VERIFIED-SUMMARY row on the live page before quoting it.

## 8. Docs that assume AWS or Google storage and need a decision or update

Grep of the repo (docs, config, code) on 2026-11-01. **No AWS, Bedrock, S3, RDS or Google Cloud storage reference exists in any `.yml/.yaml/.json/.ts/.tsx/.cs/.sh/.toml/.env.example` file** (a files-with-matches grep for `AWS|Amazon|Bedrock|S3|KMS|RDS|Google and AWS|stored in Google` over those extensions returned no files; an earlier broader pattern that also included `Transcribe` and `Google Drive` matched many `src/shared/contracts/fixtures` JSON files, which I did not inspect, so treat "no cloud-vendor reference in code" as likely but not fully checked). All the AWS-related hits I inspected are in docs:

| File | What it assumes | Needed |
|---|---|---|
| `docs/vendors/aws-bedrock-transcribe-verification.md` | Whole file is the AWS path | Banner "superseded by PO-20aa"; keep as record |
| `docs/legal/anthropic-terms-review.md` (lines 5, 50, 79) | "Tether data is stored in Google and AWS", F-M6 and counsel question 4 | Add a Foundry addendum; correct the storage statement per PO-20ab |
| `docs/legal/legal-register.md` (lines 14, 15, 23, 42) | L-04/L-05/L-13 text partly annotated; the summary line 42 still says "pending the Bedrock/Transcribe clarification" | Update per section 5 |
| `docs/DECISIONS.md` PO-3, PO-15f, PO-20f, PO-20j, PO-20z | Already annotated "superseded/updated by PO-20aa" | Consistent; PO-20aa open item (2) is what this file answers |
| `docs/security/google-tokens-and-data-residency.md` (several) | Cloud KMS/vault decision (Q5) unanswered; option B with KMS-held KEK | PO decision: Azure Key Vault (PO-20ab implies it) |
| `docs/SECURITY.md:39`, `docs/data/schema-m1-m2.md:45,81`, `docs/architecture/backend-foundation.md:56`, `docs/architecture/google-calendar-integration.md:66,182`, `docs/security/m1-review.md:123`, `docs/security/m1-rereview.md:89,97`, `docs/ops/production-readiness-audit.md:37` | Generic "KMS / vault / certificate" for token and Data Protection keys (not AWS-specific) | Name Azure Key Vault once the PO answers Q5 |
| `docs/DEVELOPMENT.md:32`, `docs/ops/ci-matrix.md:108` | "parameter group" (AWS RDS term) | Say "server parameters" for Azure |
| `docs/legal/phase2-uploads-and-vendors.md:112` (A11) | Treats Bedrock/Vertex/Foundry as an exception when a campus requires a cloud partner | Foundry is now the primary path |
| `docs/vendors/verification-report.md:115` | Lists "Same model IDs on Bedrock, Foundry, Google Cloud" as alternatives | Add pointer to this file |
| `docs/vendors/google-verification-report.md` | Google APIs (Docs/Drive/Calendar), still in scope under PO-20ab | No change implied |
| Google Docs/Drive references (PO-9, PO-9e, PO-9m, `google-calendar-integration.md`, L-01, L-06, L-07, L-09) | Google holds the student's files | **Already resolved by PO-20ab** (files stay in Google Docs; "no Google storage" = no Google Cloud storage). No further decision needed unless the PO changes PO-20ab. |

## 9. Not verified or not read (explicit)

- The full text behind `https://aka.ms/anthropic_tandc`, Anthropic's subprocessor list, Anthropic's Trust Center, Marketplace offer terms, the Microsoft Products and Services DPA, the Microsoft Product Terms, and any SLA (Claude, Speech, Postgres, Container Apps).
- Any Azure price (Speech hourly, Postgres, Container Apps, Key Vault, Storage, Log Analytics). Claude list prices were read, but not the prices shown to this subscription.
- Whether Microsoft retains any Claude prompt data for abuse monitoring; retention of flagged content; Anthropic operator access inside Azure.
- Azure Speech: training-on-customer-data statement, real-time latency, browser token flow, the 35-speaker figure for real-time, whether diarization is biometric under any law.
- Azure App Service, Front Door, Blob backup/soft delete, Key Vault SKU and pricing, Query Store behaviour, `pgaudit` behaviour on this schema, actual managed-server log output, any backup/restore drill.
- Whether this repo's AI client conforms to Foundry's parameter limits.
- Everything account-specific in section 6.
- **Fetch limitation:** the web fetch tool returns model-written extracts of pages, not raw pages (several were truncated, for example the Postgres server-parameters index at 600,034 characters, of which only the first 100,000 were read, and the diarization quickstart's last 22,843). A human must re-read the live pages before quoting any row. Community Microsoft Q&A results were seen in search summaries only and are not relied on.

## 10. Requested human actions

1. Product Owner: confirm the adult account holder for the Azure subscription and that a pay-as-you-go subscription with a card is acceptable (Claude will not work on a free or credit-only subscription).
2. Product Owner (with the account open, no deployment yet): capture the items in section 6 as dated screenshots or exports. In particular settle T-1 (Sonnet 5.5 Data Zone) and the real quota and price screens.
3. Product Owner or counsel: open `https://aka.ms/anthropic_tandc`, save the version and date, and compare it with `anthropic.com/legal/commercial-terms` (reviewed 2026-10-30).
4. Counsel: the L-04 question list in section 5, plus whether diarization output is biometric data, plus precedence between Anthropic's terms, Marketplace terms and the Microsoft Product Terms.
5. Product Owner: answer open item Q5 (Azure Key Vault for the token KEK).
6. `ai-agent-engineer`: check the AI client against Foundry's parameter and feature limits (section 1.2, 1.8); no implementation of a real provider until the PO authorises spend (playbook rule 6).
7. `devops-engineer` and `security-engineer`: add the SB-01 managed-server checklist (section 3.4, recommendation 7) to the hosting plan; do not provision anything yet.

## 11. Gate status after this file

- **G1 Recording/consent: BLOCKED** (unchanged).
- **G2 Vendor and Claude selection: UNVERIFIED** (unchanged). The candidate set is narrower and better sourced; the Sonnet 5.5 residency conflict, Azure prices, quotas for this subscription, the DPA/Terms text and measured performance are all still missing. No price or capacity is committed.
- **G5 (production readiness)**: NOT_RUN. Postgres backup/restore and log hardening are documented features, not evidence.
- L-04 stays NO-GO for real content; L-05 OPEN; L-13 OPEN. Nothing is PASS.


## Evidence from the PO's account (2026-11-03, screenshots read locally; images not in the repo)

| Question | What the screens showed | Status |
|---|---|---|
| Subscription type | Free trial (billing banner: "Your free credit expires in 30 days. Upgrade to keep going with your account"); subscription Active, role Owner, cost 0.00 | OBSERVED |
| Claude eligibility | Claude models appear under "Available in my project"; Sonnet 5.5 and Haiku 5.5 are Generally Available, version 2, hosted on Azure, 1000k context, 128k output; fable-5-1 shows "Anthropic infrastructure" | OBSERVED |
| Deployment types / regions | Model pages show "No deployments found"; the types sit behind the Deploy button, which was not pressed. PO reports seeing West US and West US 3 and prefers them. No screenshot of Sonnet 5.5 deployable there | UNVERIFIED (preference recorded, PO-20bd) |
| Quota | No Quotas entry or Management center in the Foundry left menu on this account | NOT SEEN (expected: free trial quota is 0) |
| Price | "View pricing" opens Anthropic's public pricing page. Sonnet 5.5 $2 / $10 per million input / output tokens; Haiku 5.5 $0.10 / $0.50 (prompts up to 100,000 tokens); Foundry bills in Claude Consumption Units at $0.01 each, after use, no prepaid credits | OBSERVED (public list price, not an account quote) |
| Terms | Banner on model pages: Anthropic is updating its Acceptable Use Policy effective November 12 (2026). No Marketplace terms accepted | NOTE for L-04 |
| Cost so far | Amount due $0.00; no invoices; no charges this month | OBSERVED |
| Budget | 5_bucks_a_month, $5 USD monthly on the billing account, alerts at $3 and $5, email only, expires 2026-12-31 | OBSERVED |

Nothing was deployed and no terms were accepted. The upgrade to pay-as-you-go (needed before Claude can be deployed) is not done and not authorised; see desk item T-AZ-UPGRADE.
