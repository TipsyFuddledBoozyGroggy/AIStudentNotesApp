# AWS path verification: Claude on Amazon Bedrock and Amazon Transcribe Streaming

Retrieval date for every source: **2026-11-01** (today). Prepared by `vendor-verification-researcher` (AI agent) for the Product Owner decision PO-20f ("plan on AWS").
**Not legal advice. Planning and research only. No account was opened, no AWS or Anthropic call was made, nothing was spent. G2 stays UNVERIFIED and G1 stays BLOCKED. No gate is marked PASS by this file.**

## How to read this file

- Status words: **VERIFIED** = the official page text was read on the retrieval date. **VERIFIED-SUMMARY** = official page, but the fetch tool returns a model-written extract, so a human must re-read the live page before quoting it in a contract or memo (this applies to most rows; quotes are the extract's quotes). **UNVERIFIED** = not retrieved, not stated on the pages read, or only inferred. **CONTRADICTED** = two official sources disagree or the page disagrees with a repo assumption (none found in the strict sense; see section 8 for tensions).
- "Account-specific" means the value can only be confirmed in the Product Owner's own AWS account (console, Service Quotas, Bedrock model access screen). Nothing in this file is a price or capacity commitment.
- Facts (sections 1 to 6) are kept apart from recommendations (section 9). Register effects are in section 7.
- Prices: this file does not invent any. Where a page gave a figure I quote it with its source and flag how reliable it is.

## 0. Bottom line (short)

1. **Claude on Bedrock is real and current.** Sonnet 5.5 and Haiku 5.5 are listed for Bedrock, with model IDs of the form `anthropic.claude-sonnet-5-5` on the new Messages-API endpoint (`bedrock-mantle`). Access for Sonnet 5.5 and Haiku 5.5 is "See Access" (account-specific), not "Open" like Sonnet 5.
2. **Bedrock handling differs from the direct API in a way that helps:** AWS operates the inference stack, Anthropic has no access to it, and AWS says inputs and outputs are not used to train and not shared with model providers. Retention is controlled by an AWS-side `data_retention_mode`, not by the direct API's 30-day rule. But Sonnet 5.5's own allowed modes were **not** verified, and AWS abuse detection can still retain data for some models.
3. **The Anthropic contract does not go away.** AWS publishes "Anthropic on Bedrock - Commercial Terms of Service" (version August 18, 2025): a contract between Anthropic and the customer, "not for consumer use". The earlier F-M1 concern (individual non-commercial account under commercial terms) carries over.
4. **Amazon Transcribe Streaming is a workable STT option, but its default data posture is worse than Azure's on paper.** AWS Service Terms 50.3 let AWS use and store Transcribe content to improve the service, including outside the region, unless the account sets an AWS Organizations AI-services opt-out policy. Azure real-time STT stores nothing at rest per Microsoft (earlier review). Price is only partly verified. Latency is unmeasured.
5. **Net effect on the register:** L-04 needs a re-review against the AWS-hosted Anthropic terms; L-05 gains a third candidate with an opt-out step; L-13 gets simpler (one cloud, region choice) but gains a cross-region-inference question; G2 stays UNVERIFIED with a different evidence list.

## 1. Claude on Amazon Bedrock

### 1.1 Two integration surfaces (important for engineering)

| Claim | Status | Source and date |
|---|---|---|
| Anthropic documents two Bedrock surfaces. **(a) "Claude in Amazon Bedrock"** (Opus 4.7 and later): Messages API at `https://bedrock-mantle.{region}.api.aws/anthropic/v1/messages`, standard SSE streaming, same request shape as Anthropic's API, "AWS-managed infrastructure with zero operator access (Anthropic personnel have no access to the inference infrastructure)". **(b) Legacy**: `InvokeModel` and `Converse` on `bedrock-runtime`, ARN-versioned IDs, AWS event-stream encoding. | VERIFIED-SUMMARY | https://platform.claude.com/docs/en/build-with-claude/claude-in-amazon-bedrock and https://platform.claude.com/docs/en/build-with-claude/claude-on-amazon-bedrock-legacy , 2026-11-01 |
| The legacy page says Sonnet 5.5, Haiku 5.5, Sonnet 5, Opus 5.5 etc. "are reachable through `InvokeModel` on `bedrock-runtime`" and "served by the same infrastructure as the Claude in Amazon Bedrock endpoint", but omits them from its ID table "because they do not have ARN-versioned model IDs". | VERIFIED-SUMMARY | legacy page, 2026-11-01 |
| The two endpoints have **separate quotas** even for the same model. | VERIFIED-SUMMARY | https://docs.aws.amazon.com/bedrock/latest/userguide/quotas.html , 2026-11-01 |
| Anthropic also offers **Claude Platform on AWS**: operated by **Anthropic** (not AWS), billed through AWS Marketplace, follows the first-party API retention policy, subject to "Anthropic's data use terms". It says "Organizations ... that need AWS to be the sole data processor, should use Claude in Amazon Bedrock." | VERIFIED-SUMMARY | https://platform.claude.com/docs/en/build-with-claude/claude-platform-on-aws , 2026-11-01 (last 3,093 characters not read) |

Consequence (fact): "plan on Bedrock" and "plan on Claude Platform on AWS" are different data-handling choices. PO-20f says Bedrock. Everything below is Bedrock unless labelled.

### 1.2 Model IDs and the inference-profile approach

| Claim | Status | Source and date |
|---|---|---|
| Mantle-endpoint model IDs carry an `anthropic.` prefix and no version suffix: `anthropic.claude-sonnet-5-5`, `anthropic.claude-haiku-5-5`, `anthropic.claude-sonnet-5`, `anthropic.claude-haiku-4-5`, `anthropic.claude-opus-5-5`, `anthropic.claude-fable-5-1`, etc. | VERIFIED-SUMMARY | claude-in-amazon-bedrock page, 2026-11-01 |
| Access column: Sonnet 5, Opus 4.8, Opus 4.7, Haiku 4.5, Fable 5 and 5.1 are "Open". **Sonnet 5.5 and Haiku 5.5 say "See Access"**, meaning "Amazon Bedrock sets access criteria for each Claude model individually ... check Amazon Bedrock model access in the AWS console." | VERIFIED-SUMMARY | same page. **Whether this account can use Sonnet 5.5 / Haiku 5.5 is account-specific and UNVERIFIED.** |
| Two endpoint types: **Global** (dynamic routing across regions, no premium) and **Regional** ("resolves to the single AWS region you specify, for data-residency requirements", "10 percent pricing premium over global"). To route across a geography use an inference profile (US, EU, JP, AU). Regions marked "In-region only" support single-region routing without a profile. | VERIFIED-SUMMARY | same page, 2026-11-01 |
| Global endpoint is available for Sonnet 5.5, Haiku 5.5, Sonnet 5, Haiku 4.5, Opus 5.5 and others. | VERIFIED-SUMMARY | same page |
| Legacy runtime pattern: newer models are called through inference profiles. "The inference profile ID is the base model ID with a prefix" (`global.`, `us.`, `eu.`, `jp.`, `apac.`), e.g. `us.anthropic.claude-sonnet-4-5-20250929-v1:0`. Passing the bare base ID fails with HTTP 400 ("on-demand throughput isn't supported. Retry your request with the ID or ARN of an inference profile"). | VERIFIED-SUMMARY | legacy page, 2026-11-01 |
| The exact `bedrock-runtime` `InvokeModel` ID string and profile prefixes for **Sonnet 5.5 and Haiku 5.5** | **UNVERIFIED** (the legacy table stops at Opus 4.6 / Sonnet 4.6 / Haiku 4.5). Authoritative list per Anthropic: https://docs.aws.amazon.com/bedrock/latest/userguide/inference-profiles-support.html (not read). |
| US geographic profile example: "us-east-1, us-east-2, and us-west-2 for a US profile". Anthropic's region table also shows `us-west-1` and `ca-central-1` with "US" in their endpoint types; how that maps to profile destinations is not explained. | VERIFIED-SUMMARY / UNVERIFIED for the exact US destination set for Sonnet 5.5 | https://docs.aws.amazon.com/bedrock/latest/userguide/cross-region-inference.html and the Anthropic page above |
| The AWS page https://docs.aws.amazon.com/bedrock/latest/userguide/models-regions.html now just says its content moved to "Regional availability" (`models-region-compatibility.md`) and the model cards. The per-model AWS regional table for Sonnet 5.5 **was not read**. | UNVERIFIED (AWS-side regional list) | 2026-11-01 |

**Repo note (fact):** this repo's model ID `claude-sonnet-5-5` is the direct-API ID. On Bedrock the equivalent is `anthropic.claude-sonnet-5-5` (mantle) with an optional `global.`/`us.` profile prefix on the runtime surface. They are not interchangeable strings. The ID must be provider-scoped configuration.

### 1.3 Regions

| Claim | Status | Source |
|---|---|---|
| Claude in Amazon Bedrock regions table lists 27 regions. US-relevant: `us-east-1` (Global, US, In-region only), `us-east-2` (Global, US, In-region only), `us-west-2` (Global, US, In-region only), `us-west-1` (Global, US), `ca-central-1` (Global, US). | VERIFIED-SUMMARY | claude-in-amazon-bedrock page, 2026-11-01 |
| The table does **not** say which models are in which region. It says the global endpoint covers the listed models and that Fable 5.1 regional is `us-east-1` only. Whether Sonnet 5.5 has an in-region (single-region) endpoint in a given US region is **UNVERIFIED**. | UNVERIFIED | same |

### 1.4 Data handling: Bedrock versus Anthropic's direct API

| Topic | Bedrock (AWS-operated) | Direct Anthropic API (from `docs/legal/anthropic-terms-review.md`, 2026-10-30) |
|---|---|---|
| Who sees prompts | AWS: "Model providers don't have any access to those [Model Deployment] accounts ... they don't have access to Amazon Bedrock logs or to customer prompts and completions." (VERIFIED-SUMMARY; https://docs.aws.amazon.com/bedrock/latest/userguide/data-protection.html) | Anthropic is processor under its DPA |
| Training use | "will not use any inputs to or outputs from Amazon Bedrock to train Amazon Nova, Amazon Titan, or any third-party models"; "not shared with any model providers" (VERIFIED-SUMMARY; https://aws.amazon.com/bedrock/faqs/ , page has only a 2026 copyright, no date). Also the Anthropic-on-Bedrock terms: "Anthropic may not train models on Customer Content from Services" (VERIFIED-SUMMARY; https://aws.amazon.com/legal/bedrock/third-party-models/ version August 18, 2025) | Not trained by default (Anthropic S1, S4) |
| Retention default | Set per AWS Region by a `data_retention_mode` (`none`, `default`, `aws_review`, legacy `provider_data_share`, `inherit`). "Default means the data retention policy of the model applies ... AWS may retain the data for safety and abuse-prevention purposes. The model provider does not receive it." (VERIFIED-SUMMARY; https://docs.aws.amazon.com/bedrock/latest/userguide/data-retention.html) | 30 days, flagged content up to 2 years |
| Zero data retention | Account-wide `none` is a setting you control; if a model requires retention and you set `none`, "Amazon Bedrock will block the request". For ZDR on models that require retention, contact the AWS account manager; "ZDR eligibility for Claude models is managed by Anthropic." Accounts approved show `"none"` in the model's `allowed_modes`. | ZDR by Anthropic approval |
| Which Claude models force retention | Only **Fable 5 and Fable 5.1** are named: "all traffic will be retained for up to 30 days for automated offline abuse detection. Classifier-flagged traffic will be subject to potential human review performed by AWS." "There is no data retention change to Claude models released before Claude Fable 5." Retained data stays at AWS and "not shared with third-party model providers". (VERIFIED-SUMMARY; https://docs.aws.amazon.com/bedrock/latest/userguide/abuse-detection.html) | Fable/Mythos listed as Covered Models in Anthropic S9 |
| Sonnet 5.5 / Haiku 5.5 retention | **UNVERIFIED.** Not named in the abuse-detection or retention pages' model-specific lists. Whether their `allowed_modes` include `none` can only be read from `GET /v1/models/anthropic.claude-sonnet-5-5` in the account. Whether Sonnet 5.5 counts as "released before Claude Fable 5" is not stated. | Sonnet 5.5 and Haiku 5.5 not Covered Models (per earlier review, re-check) |
| Abuse detection | "Amazon Bedrock may use automated abuse detection mechanisms ... In certain limited circumstances ... we may also store or review model inputs or outputs." Service Terms 50.12.2 (via extract): for certain models Bedrock "stores Service inputs and outputs for up to 30 days". Service Terms 1.24.1: "Amazon Bedrock abuse detection mechanisms may apply". CSAM detection may store and review flagged items. AWS may "request information about your use" and may suspend access. | Anthropic trust-and-safety on its side |
| Storage region | "encrypted and stored at rest in the AWS Region where you are using Amazon Bedrock" (FAQ). With cross-region inference "retained inputs and outputs are stored in destination regions". | `inference_geo` us at 1.1x; storage at rest US only |
| Cross-region data path | Geographic profile keeps processing "within geographic boundaries"; global profile routes "worldwide". "All data transmitted during cross-Region operations remains on the AWS network ... encrypted in transit." CloudTrail records `additionalEventData.inferenceRegion`. (VERIFIED-SUMMARY; cross-region-inference page) | default `global` |
| Prompt/response logging | Invocation logging is optional; Anthropic: "Turning on this service does not give AWS or Anthropic any access to your content" and recommends keeping activity logs at least 30 days. Logging raw lectures would breach repo invariant 10 unless scoped; keep metadata-only. | n/a |
| DPA / GDPR | AWS: UK GDPR Addendum "is part of the AWS Service Terms and applies automatically"; Swiss Addendum likewise; Service Terms "include the SCCs" (VERIFIED-SUMMARY; https://aws.amazon.com/compliance/gdpr-center/). Anthropic-on-Bedrock terms incorporate the Anthropic DPA (same AWS page). | Anthropic DPA incorporated into Commercial Terms |
| Business Associate / BAA | Bedrock FAQ lists "HIPAA eligibility" but "doesn't mention a BAA" in the extract. The Claude Platform page recommends Bedrock for "HIPAA-ready" needs. Not relevant to Tether's scope (no PHI) but recorded. | n/a |
| Data Privacy FAQ | "As a customer, you own your customer content." "We will not move or replicate your content outside of your chosen AWS Region(s), except as necessary to provide the services you initiated". The page says nothing about Bedrock or Transcribe specifically, nothing about service improvement or an AI opt-out, and has no date (https://aws.amazon.com/compliance/data-privacy-faq/ ). | n/a |

Not verified: whether the AWS Customer Agreement text (not read) says anything additional about content; the AWS subprocessor list; whether AWS human reviewers could ever see Sonnet 5.5 traffic.

### 1.5 Contract stack (Anthropic commercial terms versus AWS terms)

| Claim | Status | Source |
|---|---|---|
| AWS hosts a section titled **"Anthropic on Bedrock - Commercial Terms of Service"** (Version: August 18, 2025). Contract is "between Anthropic and the Customer". Anthropic Usage Policy, Supported Regions Policy, Service Specific Terms, Data Processing Addendum and Model Pricing Page are incorporated by reference. "Consumer terms for Claude.ai do not apply." "Services under these Terms are not for consumer use." Entity: Anthropic Ireland, Limited for EEA/CH/UK, Anthropic, PBC elsewhere. | VERIFIED-SUMMARY | https://aws.amazon.com/legal/bedrock/third-party-models/ , 2026-11-01 (read 100,000 of 222,568 characters only) |
| Customers keep rights to Inputs and own Outputs; "Anthropic may not train models on Customer Content from Services". | VERIFIED-SUMMARY | same |
| The page says to review these terms "as well as the AWS Service Terms". The extract does **not** mention the AWS Customer Agreement and does **not** state which document wins in a conflict. Anthropic's integration clause supersedes agreements "between the parties" (Anthropic and customer only). | VERIFIED-SUMMARY for the text; **precedence UNVERIFIED** | same |
| AWS Service Terms (Last Updated October 30, 2026) 1.23: for AI/ML services "(including third-party models)" it is "your responsibility to ensure that your use complies with all applicable laws, rules, and regulations." 1.24.2: services powered by Bedrock "may use cross-region inference". | VERIFIED-SUMMARY | https://aws.amazon.com/service-terms/ (first 100,000 of 322,998 characters, plus characters 100,000 to 200,000 and 200,000 to 300,000; the final 22,998 not read) |
| Whether the Anthropic Bedrock terms version (Aug 18, 2025) covers Sonnet 5.5 and the Fable/retention changes, or whether a newer version exists. | UNVERIFIED | the page version predates Sonnet 5.5 by an unknown margin |
| Billing: "It also does not explain how Anthropic fees, which are payable under the Model Pricing Page, relate to AWS billing." Anthropic's own pages say Bedrock billing is AWS-native. | VERIFIED-SUMMARY (gap) | third-party-models page; claude-platform-on-aws comparison table ("Billing: AWS (native service)") |
| Anthropic's commercial-terms details previously reviewed (warranty L.1, indemnity K.2, liability cap, 30-day change notice, SF arbitration) were read on Anthropic's own page, not on the AWS-hosted copy. Whether the AWS-hosted version is identical is **UNVERIFIED**. | UNVERIFIED | `docs/legal/anthropic-terms-review.md` |

### 1.6 Pricing model, quotas, limits

| Claim | Status | Source |
|---|---|---|
| Bedrock pricing is per token, varies by provider and model. Tiers: Standard, Flex, Priority, Reserved. Batch inference for select Anthropic models at "a 50% lower price compared to on-demand". Priority is 75% above Standard, Flex 50% below Standard (as extracted). Anthropic Provisioned Throughput requires contacting the account team. | VERIFIED-SUMMARY | https://aws.amazon.com/bedrock/pricing/ , 2026-11-01 (first 100,000 of 118,308 characters) |
| **Per-token prices for Claude Sonnet 5.5 and Haiku 5.5: none shown in the extract.** One dated note: "Starting October 7, 2026, cache read pricing for Claude Sonnet 5.5 is reduced by 50% from the prior rate." Prior rate not shown. | **UNVERIFIED (price)**; the cache-read note is VERIFIED-SUMMARY | same |
| Regional endpoints carry a 10% premium over global (Anthropic); Geographic CRIS standard pricing and Global "approximately 10% savings" (AWS). The two statements agree in direction. | VERIFIED-SUMMARY | claude-in-amazon-bedrock page; cross-region-inference page |
| Context window: Sonnet 5.5, Haiku 5.5 and others have a 1M-token window "on Amazon Bedrock" (legacy page). Request payload limit 20 MB. | VERIFIED-SUMMARY | legacy page |
| Mantle quotas (Anthropic page): "Default quota is 2 million input tokens per minute (TPM). You can request up to 5 million input TPM and 500,000 output TPM without additional Anthropic approval. AWS enforces requests-per-minute (RPM) limits on the Bedrock side; contact AWS support for RPM adjustments." | VERIFIED-SUMMARY | claude-in-amazon-bedrock page |
| AWS quota pages: mantle has separate per-model input and output TPM quotas, **no RPM quota** ("does not enforce requests-per-minute (RPM) quotas"); cached-read tokens do not count against input TPM; the input check counts input tokens plus `max_tokens`; increases go through the AWS Support limit-increase form, not the Service Quotas console; "Approval depends on whether your existing usage justifies the increase". Runtime counts input and output together. "The quotas that apply to your account might be lower than the default values ... depending on regional factors, account age, usage history, payment history". | VERIFIED-SUMMARY | https://docs.aws.amazon.com/bedrock/latest/userguide/quotas.html and quotas-mantle.html , 2026-11-01 |
| **TENSION:** Anthropic's page says AWS enforces RPM limits; AWS's mantle page says the mantle endpoint does not enforce RPM. The runtime endpoint may. Not a hard contradiction (different endpoints), but unresolved for the mantle path. | UNVERIFIED which applies | both pages above |
| Actual applied quotas for a new personal account (Sonnet 5.5, Haiku 5.5) | **UNVERIFIED, account-specific.** Read Service Quotas, search "Bedrock Mantle". | n/a |
| Because `max_tokens` is added to the input reservation, a large `max_tokens` on a small account can throttle early. | VERIFIED-SUMMARY (quotas-mantle.html) | engineering implication only |

### 1.7 Feature support relevant to this repo

| Claim | Status |
|---|---|
| Supported on both Bedrock surfaces: Messages API, prompt caching, thinking, tool use, citations, structured outputs. | VERIFIED-SUMMARY (both Anthropic Bedrock pages) |
| **Not supported:** URL sources and Files API, server-side tools (code execution, web search, web fetch), MCP connector and Agent Skills, Message Batches / Models / Admin / Usage and Cost endpoints, Managed Agents, server-side `fallbacks`, top-level automatic `cache_control` (use explicit breakpoints). | VERIFIED-SUMMARY |
| Mid-conversation system messages available via InvokeModel for Sonnet 5.5 and Haiku 5.5 (not Sonnet 5). | VERIFIED-SUMMARY |
| .NET: NuGet package `Anthropic.Bedrock`; mantle client `AnthropicBedrockMantleClient` (SigV4) or the standard client with `BaseUrl` `https://bedrock-mantle.{region}.api.aws/anthropic` and bearer token only. Legacy warning: the latest SDK sends API version `2026-10-21` by default including through Bedrock, and ARN-versioned legacy models "don't support `2026-10-21` on Bedrock ... can return a 400". | VERIFIED-SUMMARY |
| Auth: Bedrock service role (recommended), IAM assumed roles (12-hour max), bearer tokens (12-hour max, "least preferred"). | VERIFIED-SUMMARY |
| Whether this repo's existing `AiGateway` features (batch use? citations? PDF?) need anything on the unsupported list. | UNVERIFIED (not checked in this task) |

## 2. Amazon Transcribe Streaming (AWS speech-to-text)

Reminder: Bedrock's Claude endpoints take text and images; the pages read describe Messages, InvokeModel and Converse, not audio streaming. Nothing read contradicts "Bedrock does not do streaming speech-to-text", but this pass did not search for an audio-input model, so the absence is not proven. Treat Amazon Transcribe as the AWS STT candidate.

### 2.1 Protocol, audio, limits, regions

| Claim | Status | Source and date |
|---|---|---|
| Streaming options: SDKs (preferred), HTTP/2, WebSockets, console. | VERIFIED-SUMMARY | https://docs.aws.amazon.com/transcribe/latest/dg/streaming.html , 2026-11-01 |
| Audio formats for streaming: FLAC; Opus in an Ogg container; PCM signed 16-bit little-endian ("does not include WAV"). Lossless (FLAC, PCM) recommended. | VERIFIED-SUMMARY | same |
| 16,000 Hz sample rate recommended; chunk size 50 to 200 ms when selectable; uniform chunks; send silence as zero bytes. | VERIFIED-SUMMARY | same |
| Streaming not supported for all languages; see the "Data input" column of the supported-languages table. **English (US) streaming and other languages not checked in this pass.** | UNVERIFIED (language list not read) | same |
| Streaming endpoints in US regions: `us-east-1`, `us-east-2`, `us-west-2`, each with FIPS variants. No streaming endpoint for `us-west-1` (batch only). Also `ca-central-1` and others. | VERIFIED-SUMMARY | https://docs.aws.amazon.com/general/latest/gr/transcribe.html , 2026-11-01 |
| Default streaming quotas per region: 25 concurrent streams (adjustable), 25 transactions per second for StartStreamTranscription and the WebSocket variant (adjustable). | VERIFIED-SUMMARY | same |
| Maximum session duration: the errors section says a hard limit exists and "cannot be increased"; the FAQ says the service "can accommodate open connections up to eight hours long." The exact figure in the API reference was not read. | VERIFIED-SUMMARY (8 hours per FAQ); API reference UNVERIFIED | streaming.html; https://aws.amazon.com/transcribe/faqs/ |
| Partial (interim) results and stabilization: **not covered in the pages read**; this pass did not verify them. | UNVERIFIED | n/a |
| Word-level timestamps and finals: diarization output shows `start_time`/`end_time` per item in batch output; streaming result shape not read. | UNVERIFIED for streaming | diarization.html |
| Latency: AWS gives chunk-size guidance only. **No AWS-published latency number was found.** | UNVERIFIED; must be measured | streaming.html |
| Browser access: WebSocket requires a SigV4 presigned URL (`wss://transcribestreaming.{region}.amazonaws.com:8443/stream-transcription-websocket?...`, `X-Amz-Expires=300` in the example). A browser would need temporary credentials. | VERIFIED-SUMMARY (diarization.html example) | engineering implication only |
| SLA for Transcribe or Bedrock | UNVERIFIED (not retrieved) | n/a |

### 2.2 Data use, retention, opt-out

| Claim | Status | Source and date |
|---|---|---|
| Transcribe is an "AI Service" in Service Terms section 50. Section 50.3: AWS "may use and store AI Content processed by" Transcribe "to develop and improve" the service, and "may store that content outside the region where you use it". Excludes Transcribe Medical. AWS may also use non-personal AI Content to develop AWS and affiliate ML/AI technologies. | VERIFIED-SUMMARY | https://aws.amazon.com/service-terms/ , Last Updated October 30, 2026 |
| Opt-out: "configuring an AI services opt-out policy using AWS Organizations". Opting out "deletes all of the associated historical content" shared before, limited to content "not required to provide service functions". The preference is recorded in every region. | VERIFIED-SUMMARY | https://docs.aws.amazon.com/organizations/latest/userguide/orgs_manage_policies_ai-opt-out.html , 2026-11-01; Service Terms 50.3 |
| Transcribe FAQ: "You may opt out of having your content used to improve and develop the quality of Amazon Transcribe" through an Organizations opt-out policy; "Any content processed by Amazon Transcribe is encrypted and stored at rest in the AWS region." The FAQ extract does not say whether streaming audio or transcripts are stored or for how long. | VERIFIED-SUMMARY; **streaming retention UNVERIFIED** | https://aws.amazon.com/transcribe/faqs/ , 2026-11-01 |
| The Transcribe data-protection page (https://docs.aws.amazon.com/transcribe/latest/dg/data-protection.html) contains only generic shared-responsibility guidance, TLS 1.2 minimum, and PrivateLink, and **no retention or training statement.** | VERIFIED | 2026-11-01 |
| The opt-out is an AWS Organizations feature. The extracts did not say whether a standalone single account can use it or whether it must be done from an Organizations management account. | **UNVERIFIED** | n/a |
| The opt-out policy's supported-service list page was not opened; Transcribe is covered by the FAQ and 50.3, which is enough to treat it as supported pending a human check. | UNVERIFIED (list) | n/a |
| Tension with the Data Privacy FAQ promise not to move content "outside of your chosen AWS Region(s), except as necessary to provide the services you initiated" versus 50.3's right to store AI Content outside the region for improvement. Not a proven contradiction (different purposes), but a counsel question for L-13. | TENSION | data-privacy-faq; Service Terms 50.3 |
| Comparison: Azure real-time STT "no data is stored at rest" (VERIFIED in `docs/legal/stt-vendor-terms-review.md`, page dated 2026-03-31); Azure training-on-data statement not found. | carried over | that file |

### 2.3 Diarization and biometric privacy

| Claim | Status | Source |
|---|---|---|
| Speaker diarization ("partitioning") is available in **streaming** (console toggle, HTTP/2 header `x-amzn-transcribe-show-speaker-label: true`, WebSocket `show-speaker-label=true`). Distinguishes "a maximum of 30 unique speakers", labels `spk_0` to `spk_29`. | VERIFIED-SUMMARY | https://docs.aws.amazon.com/transcribe/latest/dg/diarization.html , 2026-11-01 |
| Diarization has no separate surcharge: "included" in standard pricing per the extract. | VERIFIED-SUMMARY | https://aws.amazon.com/transcribe/pricing/ |
| The diarization page says nothing about voiceprints, voice biometrics, speaker identification of known people, retention of speaker embeddings, or consent. | VERIFIED-SUMMARY (absence on the page read) | diarization.html |
| Service Terms: "The provided text contains no biometric terms for Transcribe"; the biometric notice in 50.9.3 "applies only to Amazon Rekognition". | VERIFIED-SUMMARY (extract of characters 100,000 to 200,000; sections not read in full) | Service Terms |
| Whether Transcribe diarization creates biometric identifiers under any state law (for example Illinois BIPA) | **UNVERIFIED and for counsel** | n/a |
| Contrast: AssemblyAI has a Biometric Addendum and Azure documents temporary voice-characteristic signals (earlier review). AWS pages read are silent. Silence is not clearance. | carried over | `docs/legal/stt-vendor-terms-review.md` H-3 |

### 2.4 Pricing

| Claim | Status | Source |
|---|---|---|
| Billing in one-second increments, "no minimum applied"; two-channel audio billed for total duration only. | VERIFIED-SUMMARY | https://aws.amazon.com/transcribe/pricing/ , 2026-11-01 (page states no effective date) |
| Free tier: 60 minutes per month for 12 months from the first transcription request; does not roll over. | VERIFIED-SUMMARY | same |
| Streaming rate: the extract's only streaming figure is "Streaming transcription is priced at $0.01 per minute" in a US East (N. Virginia) worked example. **The tiered rate table was not in the extract, and I could not confirm that figure.** It does not match my unsourced recollection of earlier AWS list prices (which was higher), and a summarising fetch tool may have mis-read an example. **Do not budget from it.** A human must read the live page in the region dropdown. | **UNVERIFIED (treat the number as a lead only)** | same |
| Content redaction is a paid add-on; custom language models and toxicity detection are add-ons with prices not extracted. | VERIFIED-SUMMARY | same |
| Rates for other regions | UNVERIFIED | same |

## 3. Azure AI Speech (US) lead versus Amazon Transcribe Streaming (terms and facts only, not a performance ranking)

| Topic | Azure AI Speech, US region (earlier reviews) | Amazon Transcribe Streaming (this file) |
|---|---|---|
| Default data use | Real-time: "no data is stored at rest"; training statement not found (UNVERIFIED) | AWS may use and store content to improve service, possibly outside the region, **unless** AI-services opt-out policy is set (VERIFIED-SUMMARY) |
| Opt-out mechanism | none needed for storage; no training statement | AWS Organizations AI services opt-out policy; deletes prior stored copies |
| Streaming retention | None at rest (VERIFIED, 2026-03-31 page) | Not stated for streaming (UNVERIFIED) |
| Protocols | WebSocket SDK (earlier verification report) | SDK, HTTP/2, WebSocket (SigV4 presigned) |
| Diarization in streaming | Supported; voice characteristic signals "temporarily retained", customer biometric duties (VERIFIED) | Supported, up to 30 speakers, no extra price; biometric position not stated (UNVERIFIED) |
| US regions | Several (eastus, eastus2, westus2, westus3, centralus) | us-east-1, us-east-2, us-west-2 (+FIPS) |
| Price | Unverified | Free tier verified; rate unverified (see 2.4) |
| Account friction | Microsoft or GitHub account, card, phone | AWS account (the PO already uses AWS); same account as Bedrock |
| SLA | Not read | Not read |
| DPA | Microsoft DPA not read | AWS UK/Swiss addenda automatic, SCCs in Service Terms; base DPA text not read |
| Benchmark clause | Not seen | Not checked (AWS Customer Agreement not read) |

Fact-only reading: on retention-by-default Azure is stronger on paper; on operational simplicity (one account, one IAM model, one bill, PO knows it) AWS is stronger. Neither ranking is a performance claim.

## 4. What changes for security and engineering if AWS (fact-flavoured implications)

- Credentials: AWS IAM roles, not API keys. Repo invariant 10 (no hardcoded credentials) applies; bearer tokens are documented as least preferred and are 12-hour max.
- The `bedrock-mantle` surface and `bedrock-runtime` surface have different quotas, endpoint URLs, ID formats and SDK classes. The adapter should choose one and record which.
- Region choice is now a first-class data-handling decision: single region, geographic profile (US), or global.
- Account-wide retention mode is set per region by API only ("At launch, there is no console UI"). It needs a deliberate, recorded step.
- AWS invocation logging and CloudTrail: metadata only; do not enable prompt/response body logging for lecture content.

## 5. Evidence that still requires the Product Owner's actual AWS account

1. Bedrock model access screen: screenshot that `anthropic.claude-sonnet-5-5` and `anthropic.claude-haiku-5-5` are granted (access criteria "See Access").
2. `GET /v1/models/anthropic.claude-sonnet-5-5` response (or the Haiku one) showing `data_retention.allowed_modes` and effective `mode`, plus the account's `data_retention` setting in the chosen region.
3. Service Quotas values for "Bedrock Mantle input/output tokens per minute" for both models in the chosen region.
4. Transcribe Service Quotas (concurrent streams) in the chosen region, and the live pricing page with the region dropdown.
5. Which AWS Organizations setup holds the AI-services opt-out policy, and its effective-policy query.
6. The Anthropic-on-Bedrock terms version the account actually accepts (screen or Artifact copy), dated.

## 6. Source index (all retrieved 2026-11-01)

- https://docs.aws.amazon.com/bedrock/latest/userguide/data-protection.html
- https://docs.aws.amazon.com/bedrock/latest/userguide/abuse-detection.html
- https://docs.aws.amazon.com/bedrock/latest/userguide/data-retention.html
- https://docs.aws.amazon.com/bedrock/latest/userguide/cross-region-inference.html
- https://docs.aws.amazon.com/bedrock/latest/userguide/quotas.html and https://docs.aws.amazon.com/bedrock/latest/userguide/quotas-mantle.html
- https://docs.aws.amazon.com/bedrock/latest/userguide/models-regions.html (moved page; content not read)
- https://aws.amazon.com/bedrock/faqs/ (no date; 2026 copyright)
- https://aws.amazon.com/bedrock/pricing/ (no date; Oct 7, 2026 cache-read note)
- https://aws.amazon.com/legal/bedrock/third-party-models/ (Anthropic section version August 18, 2025)
- https://aws.amazon.com/service-terms/ (Last Updated October 30, 2026; 1.23, 1.24, 50.3, 50.12 seen via extracts; characters 0 to 300,000 of 322,998 read, none of the last 22,998)
- https://aws.amazon.com/compliance/data-privacy-faq/ (no date)
- https://aws.amazon.com/compliance/gdpr-center/ (no date)
- https://docs.aws.amazon.com/organizations/latest/userguide/orgs_manage_policies_ai-opt-out.html
- https://docs.aws.amazon.com/transcribe/latest/dg/data-protection.html
- https://docs.aws.amazon.com/transcribe/latest/dg/streaming.html
- https://docs.aws.amazon.com/transcribe/latest/dg/diarization.html
- https://docs.aws.amazon.com/general/latest/gr/transcribe.html
- https://aws.amazon.com/transcribe/pricing/ and https://aws.amazon.com/transcribe/faqs/
- https://platform.claude.com/docs/en/build-with-claude/claude-in-amazon-bedrock
- https://platform.claude.com/docs/en/build-with-claude/claude-on-amazon-bedrock-legacy
- https://platform.claude.com/docs/en/build-with-claude/claude-platform-on-aws (last 3,093 characters not read)
- Repo: `docs/agents/project-playbook.md`, `docs/legal/anthropic-terms-review.md`, `docs/legal/stt-vendor-terms-review.md`, `docs/DECISIONS.md` (PO-20f).

**Not read (explicit):** AWS Customer Agreement; AWS DPA body; Transcribe supported-languages table, partial-results/stabilization and streaming API reference; `inference-profiles-support.html`; `models-region-compatibility`; `bedrock-runtime` quotas page; AWS subprocessor and Anthropic subprocessor lists; any SLA; any AWS benchmark clause; the Bedrock "model-lifecycle" (retirement dates) page. Any claim about those is UNVERIFIED.

## 7. Register effect if the PO goes AWS (facts about the repo's rows; none is closed)

Current rows (from `docs/legal/legal-register.md`): L-04 Anthropic terms (OPEN, MEDIUM provisional); L-05 STT vendor terms (IN-REVIEW, MEDIUM); L-13 cross-border storage (OPEN, MEDIUM, "data will live in Google and AWS").

**L-04 (Anthropic terms) - changes**
- The governing document set changes from Anthropic's website terms (S1 to S9 in `anthropic-terms-review.md`) to the AWS-hosted "Anthropic on Bedrock - Commercial Terms of Service" (Aug 18, 2025) plus AWS Service Terms plus AWS Customer Agreement. F-M1 (individual, non-commercial, "not for consumer use") **still applies** because the AWS-hosted terms say the same. The review must be redone against the hosted text; do not assume the two are identical.
- F-M3/F-M4 (retention, ZDR) change shape: retention is AWS-side and model-dependent; account-level `none` is available where the model allows it; Claude ZDR eligibility is "managed by Anthropic". Direct-API statements (30 days, 2 years flagged, Anthropic approval for ZDR) no longer describe this path.
- F-M5 (subprocessors) and the DPA: Anthropic's docs say AWS operates the stack and Anthropic has no access, yet the hosted terms incorporate Anthropic's DPA (Anthropic as processor). Which entity is processor for Bedrock inference is UNVERIFIED. Ask counsel.
- F-M6 (`inference_geo`) is replaced by endpoint/profile choice (regional 10% premium, global, geographic profile).
- F-M7 to F-M10 (Usage Policy version anomaly, education high-risk area, minors, training opt-in) still apply because the Usage Policy is incorporated.
- New: Anthropic's page says Sonnet 5.5 and Haiku 5.5 access is account-specific. Status stays OPEN; NO-GO on real third-party content stands.

**L-05 (STT terms) - changes**
- Add Amazon Transcribe as a third candidate. New terms risk: content use for improvement is **on unless the AI-services opt-out policy is applied**, and the extract allows storage outside the region. This is the main reason the earlier "Azure lowest terms risk" finding is not overturned by what was read.
- Add to the "Requested human actions": read the AWS Customer Agreement and AWS DPA, the Transcribe API reference (partial results, max session), and confirm the opt-out can be applied to this account.
- Diarization: keep OFF by default (same as the earlier recommendation); AWS pages are silent on biometric status, which is not clearance.
- Status recommendation: stays IN-REVIEW. Do not move to a vendor choice.

**L-13 (cross-border storage) - changes**
- If AWS holds Claude and STT, the question narrows: pick one US region (Bedrock regions with US endpoints include us-east-1, us-east-2, us-west-2; Transcribe streaming includes the same three) and decide regional versus US profile versus global routing. Retained data goes to the destination region under cross-region inference.
- Open tension: Data Privacy FAQ promise not to move content out of the chosen region versus Service Terms 50.3 for Transcribe. Unresolved.
- Google (Docs/Calendar) storage remains a separate question.

**G2 (vendor and Claude cost/limits selection) - changes**
- Stays **UNVERIFIED**. The candidate set changes (Claude via Bedrock; STT: Azure vs Transcribe vs Deepgram/AssemblyAI). Evidence list becomes section 5 above plus an authorized, consented/synthetic-audio pilot per `docs/vendors/verification-report.md` and `MEASUREMENT_PROTOCOL.md`. No AWS price or quota is verified for the account. Nothing here authorizes spend.

## 8. Tensions and contradictions found

| # | Item | Status |
|---|---|---|
| T-1 | Anthropic: "Default quota is 2 million input TPM" versus AWS: quotas "might be lower ... account age, usage history". | Not a contradiction in kind; the applied value is UNVERIFIED for a new account. |
| T-2 | Anthropic: "AWS enforces requests-per-minute (RPM) limits" versus AWS: the mantle endpoint "does not enforce requests-per-minute (RPM) quotas". | UNVERIFIED which applies. |
| T-3 | AWS-hosted Anthropic terms incorporate Anthropic's DPA, but Anthropic states AWS is the sole processor with zero Anthropic access. | UNVERIFIED role mapping; counsel question. |
| T-4 | Data Privacy FAQ (no cross-region movement) versus Service Terms 50.3 (may store Transcribe AI Content outside the region). | TENSION, counsel question. |
| T-5 | Bedrock pages say "no data retention change" for Claude models released before Fable 5, yet do not say whether Sonnet 5.5 is before or after Fable 5. | UNVERIFIED for Sonnet 5.5. |
| T-6 | Pricing page extract: Transcribe streaming "$0.01 per minute"; credibility of that figure not established. | UNVERIFIED. |
| T-7 | Sonnet 5.5 cache-read price "reduced by 50%" from Oct 7, 2026 with no base prices shown: any older cost table in the repo for Sonnet 5.5 would be stale. | Informational. |

No strict CONTRADICTED statuses assigned: the sources read do not directly negate each other or a repo claim; the repo's direct-API retention and `inference_geo` statements simply do not transfer to Bedrock.

## 9. Recommendations (not facts, not decisions)

1. **Keep the provider-neutral interfaces** from `verification-report.md`. Add an `LlmProvider` adapter with configuration `{ provider: "anthropic-direct" | "bedrock-mantle" | "bedrock-runtime", modelId, region, endpointKind }`. Do not reuse `claude-sonnet-5-5` as a universal string.
2. **If the PO wants Bedrock for Claude:** choose the `bedrock-mantle` Messages-API surface and the .NET `Anthropic.Bedrock` mantle client (same request shape as the existing direct client), in a single US region, with IAM role credentials. Treat `global` routing as a decision with a written reason; prefer a US profile or a single region until L-13 is answered, accepting the stated 10% regional premium as a documented unknown until the live pricing is read.
3. **Before the first real call:** fix the retention mode explicitly in the chosen region and record the `GET /v1/models/...` output; if the model cannot be set to `none`, the PO must accept AWS-side retention in writing. Keep Batch, Files API and URL sources out of the design (not supported on Bedrock anyway).
4. **STT:** do not drop Azure on the strength of PO familiarity alone. Run Amazon Transcribe Streaming as a third candidate in the authorized, synthetic-audio benchmark (section 5 of the earlier report). Treat AWS's AI-services opt-out as a startup precondition (analogous to the Deepgram `mip_opt_out` recommendation): do not open real audio streams until the effective-policy query shows opt-out.
5. **Simplest AWS-only story if the PO prefers one cloud:** Bedrock (Claude, mantle) plus Transcribe Streaming in the same US region, one AWS account, one IAM boundary. This reduces vendor count from two or three to one for terms review, but it concentrates risk on one account holder (an adult, per the earlier M-2 finding) and on a quota that is unproven.
6. **Counsel questions to add** to the L-04 list: Which terms rank first among the Anthropic-on-Bedrock terms, Service Terms and Customer Agreement; who is processor for Bedrock inference; does AWS human review under `aws_review` matter for any model we choose; are Transcribe diarization outputs biometric under applicable law; can a single standalone account apply the AI-services opt-out.
7. Ask the human to re-read every VERIFIED-SUMMARY row at the live URL before it is quoted in any decision memo.

## 10. Requested human action

1. Product Owner: confirm the AWS account holder is an adult and that the account is the intended billing owner.
2. Product Owner (with the account open, no spend yet): capture the Bedrock Model access screen, the `data_retention` and model `allowed_modes` output, the Service Quotas values, and the Transcribe pricing page for the chosen region. Save dated copies as evidence.
3. Product Owner or counsel: read the hosted Anthropic-on-Bedrock terms and the AWS Customer Agreement and record the accepted version and date.
4. Counsel: the questions in recommendation 6.
5. `devops-engineer` and `speech-engineer`: no implementation until the PO confirms the plan; mock providers only (playbook rule 6).

## 11. Gate status after this file

- G1 Recording/consent: **BLOCKED** (unchanged).
- G2 Vendor and Claude selection: **UNVERIFIED** (unchanged). Candidate set widened; account evidence and a measured, authorized pilot are still missing. No AWS price or capacity is committed.
- L-04 OPEN, L-05 IN-REVIEW, L-13 OPEN: unchanged by this file; see section 7 for required follow-ups. Nothing is PASS.
