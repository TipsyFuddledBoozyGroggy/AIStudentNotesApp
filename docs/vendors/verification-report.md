# Vendor Verification Report: Speech-to-Text and Claude API

Retrieval date for every fact below: **2026-10-27** (unless a row says otherwise).
Authored by general-purpose agent acting as substitute for vendor-verification-researcher (agent not loaded).
Scope: fact-check only. No vendor is chosen, no account was created, nothing was spent. This file does not close the "Vendor and Claude cost/limits selection" gate in CLAUDE.md. That gate stays **UNVERIFIED** until account tier is confirmed and an authorized pilot is measured.

Evidence-quality key:
- **P** = official vendor page, fetched live.
- **P-sum** = official page, but the fetch tool returned a model-written summary rather than raw text. Re-open the URL before relying on exact wording.
- **S** = secondary or third-party. Not accepted as evidence; listed only as a lead.
- **X** = could not verify.

Nothing here is a measured performance number. Latency items are vendor claims only.

## 1. Biggest discrepancies (summary)

1. **STT cost ranking is wrong in the design.** `speech-pipeline.md` says Deepgram is "generally the cheapest" and AssemblyAI is "mid". Official pages show AssemblyAI Universal-Streaming English at **$0.15/hr**, against Deepgram Nova-3 monolingual at **$0.29/hr** (Pay As You Go). Deepgram's streaming rates are labelled "limited-time promotional", so they may rise.
2. **Azure price is unverifiable from the official page.** The Azure pricing page renders `$-` for every price. Only third-party sources say about $1.00/hr for real-time, so Azure is probably the most expensive, not "mid". Treat as X.
3. **Billing basis differs by vendor.** AssemblyAI bills the **time the WebSocket is open, including idle time**. Pause must close or idle-cut the socket or it still costs money. The design's idle cutoff is necessary, and "Pause stops sending frames" alone is not enough.
4. **Concurrency metrics are not comparable.** Deepgram PAYG: up to 150 concurrent streams. Azure S0: 100 concurrent (adjustable). AssemblyAI PAYG: 100 **new streams per minute**, which is a different metric. Free tiers are 1 concurrent (Azure F0) and 5 new streams per minute (AssemblyAI).
5. **Deepgram retention and training depend on a request flag.** Data is used for model improvement (the Model Improvement Partnership Program, MIP) unless every request carries `mip_opt_out=true`. One search snippet says opting out forfeits a 50% discount. The official MIP page fetched here does not mention a discount, so that conflict is unresolved. This directly affects the design's "vendor settings must disable training" requirement and the true cost.
6. **Azure is the only vendor with a documented "no data stored at rest" statement for real-time STT.** The design's "strongest enterprise/compliance story" is plausible on this point, but its DPA, FERPA and region terms were not retrieved (X).
7. **FERPA: no vendor page fetched mentions it.** FERPA and education terms are X for all three vendors and for Anthropic. This is a legal item for `legal-compliance-reviewer` and counsel.
8. **Anthropic: model IDs in the AI design are correct, but behavior changed.** On Opus 5.5 and Sonnet 5.5, thinking cannot be disabled (`disabled` returns 400). Forced `tool_choice` returns 400. Effort defaults to `medium` on Opus 5.5 and Haiku 5.5. The design is silent on all three, and it affects latency and output-token cost.
9. **Haiku 5.5 has a long-context price step that Opus 5.5 and Sonnet 5.5 do not.** It is $0.10/$0.50 per MTok up to 100K prompt tokens, and $0.50/$2.50 above that. The design's 4k input cap keeps it in the cheap band, but a "catch-up backlog" call over 100K tokens costs 5x per token.
10. **Prompt caching has a 512-token minimum on all three 5.5 models.** Shorter prefixes silently do not cache, with no error. The design's "static system prompt + schema" must exceed 512 tokens to benefit. The default TTL is 5 minutes.
11. **Anthropic retention statements conflict between two official pages.** The privacy-center article says inputs and outputs are deleted within 30 days. The API data-retention page says conversation content is "not retained by default" except Covered Models. Opus 5.5, Sonnet 5.5 and Haiku 5.5 are not listed as Covered Models on the retrieved text. ZDR needs Anthropic approval. This needs a Product Owner and legal read of the actual account settings.
12. **Student-data region.** Anthropic `inference_geo: "us"` costs **1.1x** on all token categories. Default is `global`. The design does not state a region requirement. PO-7 (hosting region) is open.

## 2. Speech-to-text vendors

### 2.1 Deepgram

| Item | Verified fact | Source URL | Quality | Account-specific check needed |
|---|---|---|---|---|
| Streaming price | Nova-3 Monolingual: $0.0048/min ($0.29/hr) PAYG; $0.0042/min ($0.25/hr) Growth. Multilingual: $0.0058/min PAYG ($0.35/hr). Page labels these "Limited-time promotional rates on streaming". | https://deepgram.com/pricing | P-sum | Yes: plan, promo end date, contract |
| Add-ons (streaming) | Diarization $0.0020/min ($0.12/hr) PAYG; Keyterm Prompting $0.0013/min ($0.08/hr) PAYG | https://deepgram.com/pricing | P-sum | Yes |
| Concurrency | Streaming (WSS): up to 150 PAYG, up to 225 Growth | https://deepgram.com/pricing | P-sum | Yes: confirm on the actual account |
| Free credit | $200 on new PAYG accounts; page says about 43,000 minutes of Nova; does not expire | https://deepgram.com/pricing | P-sum | Yes |
| Latency claim | "optimized to deliver transcription latency in 300 milliseconds or less for streaming workloads"; Nova-3 described as "sub-300 ms". Typical ranges: network 20-200 ms, transcription 150-300 ms, total transcript latency 200-500 ms. Figures are approximate and "fluctuate". | https://developers.deepgram.com/docs/measuring-streaming-latency | P-sum | Vendor claim, not measured |
| Latency method | Latency = audio sent minus audio covered by an interim transcript. Use interim results only (finals are delayed by endpointing). `start`/`duration` are "not guaranteed" accurate at millisecond precision. Report p50/p95/p99. | same | P-sum | n/a |
| Protocol | Live connection over what appears to be WebSocket (page does not state it directly). `Results` messages carry `is_final` and `speech_final`. Word timestamps in seconds from stream start. KeepAlive messages exist for idle periods; no timeout values given. | https://developers.deepgram.com/docs/live-streaming-audio | P-sum | Audio format/sample-rate limits not on page (X) |
| Training / retention | Opt out with `mip_opt_out=true` on every request. Opted-out data "retained only for the duration necessary to process the request." Deepgram "stores fractional increments of data" for improvement for contractually included data. | https://developers.deepgram.com/docs/the-deepgram-model-improvement-partnership-program | P-sum | Yes: contract terms |
| MIP discount | A search snippet says opting out forfeits a 50% discount for PAYG, Growth and Enterprise. The MIP page fetched here does not mention a discount. | search result (snippet only) | S, conflicting | Yes. Resolve before costing |
| Default enrollment | MIP page does not say whether enrollment is default. A third-party blog says standard customers are not enrolled by default. | https://developers.deepgram.com/docs/the-deepgram-model-improvement-partnership-program | X for default state | Yes |
| Regions | EU endpoint `api.eu.deepgram.com` (processing in EU AWS regions; `/v1/listen` and `/v2/listen`, excluding Whisper models). AU endpoint `api.au.deepgram.com`. Default (US) region not confirmed from a primary page. | https://developers.deepgram.com/docs/data-privacy-compliance ; https://developers.deepgram.com/reference/regional-endpoints (search result only) | P-sum / S | Yes |
| Compliance | "GDPR ready"; SOC 2 Type 1 and Type 2 (report on request); HIPAA Business Associate, BAA on request to qualifying customers. | https://developers.deepgram.com/docs/data-privacy-compliance | P-sum | Yes |
| DPA | Only an AU-specific DPA is mentioned. General DPA not found on the page. | same | X | Request from vendor |
| FERPA / education terms | Not mentioned on the page. | same | X | Legal review |
| SLA | Not retrieved. | | X | Yes |

### 2.2 AssemblyAI

| Item | Verified fact | Source URL | Quality | Account-specific check needed |
|---|---|---|---|---|
| Streaming price | Universal-3.6 Pro Realtime: $0.45/hr. Universal-Streaming English: $0.15/hr. Universal-Streaming Multilingual: $0.15/hr. | https://www.assemblyai.com/pricing | P-sum | Yes |
| Billing basis | Billed on session length, not audio sent: time the WebSocket is open. Idle time counts. | same | P-sum | n/a |
| Add-ons (streaming) | Diarization +$0.12/hr (`speaker_labels: true`, whole session). Keyterms: included on U3.6 Pro and Multilingual; +$0.04/hr on English. General prompting (beta) +$0.05/hr; Voice Focus +$0.10/hr (U3.6 Pro only). | same | P-sum | Yes |
| Rate limit | New streams per minute: Free 5; PAYG 100; higher via enterprise. This is not a concurrent-session cap. A concurrent-session figure was not found. | same | P-sum | Yes |
| Free tier | $50 credit, no card; concurrency stays capped until upgrade. | same | P-sum | Yes |
| Latency | The streaming page gives **no** latency figures. | https://www.assemblyai.com/docs/speech-to-text/universal-streaming | X | Benchmark needed |
| Protocol | `wss://streaming.assemblyai.com/v3/ws`. `Turn` messages with partial `transcript` and `end_of_turn: true` for finals; per-word `word_is_final`. Default mono 16-bit PCM; AAC and Opus also accepted. Session auto-closes after **3 hours**. | same | P-sum | n/a |
| Regions | Global default routes to nearest region; US and EU data-zone hosts. EU: `wss://streaming.eu.assemblyai.com/v3/ws`, AWS EU West 1 (Dublin). | https://assemblyai.com/docs/streaming/endpoints-and-data-zones (search result summary); https://assemblyai.com/docs/faq/do-you-offer-servers-in-the-eu | S/P-sum | Page not opened directly. Re-fetch |
| Streaming retention | "We offer zero data retention of audio and transcripts for our Streaming product" for opted-out customers; metadata kept for logging and billing. Default streaming retention for non-opted-out customers: not stated. | https://www.assemblyai.com/docs/data-retention-and-model-training.md | P-sum | Yes |
| Training | Opt-out stops training use: "AssemblyAI will not use your Customer Data to train its artificial intelligence and machine learning models." Opt-out is forward-looking only. Free users cannot opt out. A signed BAA auto-opts-out. Another page says only certain files are used for training, after PII redaction, and excluded for BAA, EU servers or opt-out. | https://www.assemblyai.com/docs/data-controls.md ; https://www.assemblyai.com/docs/faq/how-to-opt-out-of-data-sharing-for-our-model-improvement-program.md | P-sum | Yes: default state on a paid account |
| Async TTL | 1, 3, 7 or 30 days (async only; not relevant to streaming). | https://www.assemblyai.com/docs/data-controls.md | P-sum | n/a |
| Compliance | SOC 2 Type 1 and Type 2. A DPA is linked but terms not read. | https://www.assemblyai.com/docs/data-retention-and-model-training.md | P-sum | Yes |
| FERPA / education terms | Not mentioned. | | X | Legal review |
| SLA | Not retrieved. | | X | Yes |

### 2.3 Azure AI Speech

| Item | Verified fact | Source URL | Quality | Account-specific check needed |
|---|---|---|---|---|
| Real-time price | The official page shows "Real-time Transcription: $- per hour" (placeholder). Not verifiable. Third-party sources suggest about $1.00/hr standard, $0.30/hr per enhanced feature (diarization), $1.20/hr custom. | https://azure.microsoft.com/en-us/pricing/details/speech/ | X (official). S for the figures | Yes. Use the Azure pricing calculator or sales |
| Retail Prices API | Query on prices.azure.com with my filter returned zero items. Not conclusive. | https://prices.azure.com/api/retail/prices | X | Retry with correct filter |
| Free tier | Standard real-time: 5 audio hours free per month (shared with Custom; batch not supported). | https://azure.microsoft.com/en-us/pricing/details/cognitive-services/speech-services/ | P-sum | Yes |
| Commitment tiers | 2,000, 10,000 and 50,000 hrs/month tiers exist; prices shown as `$-`. | same | P-sum for existence; X for price | Yes |
| Concurrency | Real-time STT: 100 concurrent requests (default, adjustable via support request) for base endpoint and for each custom endpoint, STT and translation combined. Free F0: 1, not adjustable. Real-time diarization max audio length: 240 minutes per session. 429s may occur during autoscaling; retry logic required. | https://learn.microsoft.com/en-us/azure/ai-services/speech-service/speech-services-quotas-and-limits (page updated 2026-10-23) | P | Yes: limit cannot be viewed in portal; needs a support request |
| Retention (real-time) | "For real-time speech to text, audio input is processed only on the Azure's server memory, and no data is stored at rest." "Microsoft does not retain or store the data provided by customers" for real-time STT, fast transcription, pronunciation assessment and speech translation. Batch output may sit in Microsoft storage if no storage is specified. | https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/speech-service/speech-to-text/data-privacy-security | P | Yes: confirm against contract |
| Diarization data | Voice characteristic signals are retained temporarily for labelling and discarded afterwards. Does not support speaker identity enrollment. | same | P | n/a |
| Customer duties | Page states the customer must obtain all necessary permissions for processing audio, and that audio and transcripts may be regulated under communications laws. Relevant to the consent gate. | same | P | Legal review |
| Training on customer data | Not addressed on that page. | | X | Yes |
| Latency | Not retrieved. | | X | Benchmark needed |
| Regions | Not retrieved. Quotas page says resources in different regions can be created to spread load. | quotas page above | X (list of STT regions) | Yes |
| DPA / FERPA / SLA | Not retrieved. Page links to the Microsoft Trust Center. | | X | Legal review |
| Containers | Speech containers can run in the customer's own environment (no audio sent to the cloud service). Not evaluated further. | privacy page above | P | Option for review |

## 3. Anthropic Claude API

Model facts were taken from the live docs. The `claude-api` skill's cached table (dated 2026-10-06) was used only as a cross-check and agrees with every figure below.

| Item | Verified fact | Source URL | Quality | Account-specific check needed |
|---|---|---|---|---|
| Model IDs | `claude-opus-5-5`, `claude-sonnet-5-5`, `claude-haiku-5-5` (dateless IDs are pinned snapshots). | https://platform.claude.com/docs/en/models/overview | P | No |
| Base pricing per MTok (in / out) | Opus 5.5 $4 / $20. Sonnet 5.5 $2 / $10. Haiku 5.5 $0.10 / $0.50 (prompts up to 100,000 tokens), $0.50 / $2.50 (over 100,000). | https://platform.claude.com/docs/en/about-claude/pricing | P | Confirm in console billing |
| Cache pricing | 5-min write 1.25x base; 1-hour write 2x; read 0.05x on Opus 5.5 and Sonnet 5.5 ($0.20 and $0.10/MTok); read 0.1x on Haiku 5.5 ($0.01/MTok up to 100K). | https://platform.claude.com/docs/en/build-with-claude/prompt-caching | P-sum | No |
| Cache TTL and minimum | Default TTL 5 minutes, refreshed free on each use; 1-hour TTL via `"ttl": "1h"`. Minimum cacheable prompt: 512 tokens for Opus 5.5, Sonnet 5.5, Haiku 5.5. Shorter prompts are processed uncached with no error. | same | P-sum | No |
| Batch | 50% off input and output. | pricing page | P | No |
| Context / output | Context window 1M tokens and max output 128K for all three. Batch API can reach 300K output with the `output-300k-2026-03-24` beta header. | models overview | P | No |
| Long-context pricing | Claude 4.6+ models except Haiku 5.5 include the full 1M window at standard pricing. Haiku 5.5 steps up above 100K prompt tokens. | pricing page | P | No |
| Thinking / effort | Opus 5.5: adaptive, always on, default effort `medium`. Sonnet 5.5: adaptive, default `high`; `disabled` returns 400, `between_tools` is the off-switch. Haiku 5.5: adaptive, default `medium`. | models overview; skill reference | P / S (skill) | No |
| Forced tool choice | `tool_choice` `any`/`tool` returns 400 on Opus 5.5 and Sonnet 5.5 (use `auto` + `strict: true` or structured outputs). | claude-api skill reference (cached 2026-10-06) | S | Not re-verified against live docs |
| Rate limits (tier-based) | Standard tiers. Start: Opus/Sonnet/Haiku 5.5 each 1,000 RPM, 2,000,000 ITPM, 400,000 OTPM. Build: 5,000 RPM, 5M ITPM, 1M OTPM. Scale: 10,000 RPM, 10M ITPM, 2M OTPM. Only uncached input counts toward ITPM (cache reads excluded for these models). New orgs may start in a lower Evaluation tier. Limits are shared across `inference_geo` values. | https://platform.claude.com/docs/en/api/rate-limits | P | **Yes: PO must read the actual tier on the Console limits page** |
| Spend caps | Monthly cap: Start $500, Build $1,000, Scale $200,000. At cap, requests return 429 with `enforced_spend_limit_reached` and no `retry-after`, so SDK retries fail. A self-set limit returns 400. | rate-limits page | P | Yes |
| Retention (standard) | Privacy-center article: inputs and outputs deleted within 30 days of receipt or generation by default; longer for Files API use, Usage Policy enforcement, legal requirements, or other agreements. | https://privacy.claude.com/en/articles/7996866-how-long-do-you-store-my-organization-s-data | P-sum | Yes |
| Retention (API page) | API data-retention page says conversation content is "not retained by default" except Covered Models, which require 30-day retention. This does **not** match the 30-day statement above, so the two official pages conflict or use different scopes. Covered Models are Fable 5.1, Mythos 5.1, Fable 5 and Mythos 5. | https://platform.claude.com/docs/en/manage-claude/api-and-data-retention | P (read via grep of saved text, not full page) | **Yes. Check Console > Settings > Privacy Controls** |
| Zero data retention | Available for eligible APIs "subject to Anthropic's approval", per organization, via Sales. Not available for Covered Models. Consumer plans excluded. | https://privacy.claude.com/en/articles/8956058-i-have-a-zero-data-retention-agreement-with-anthropic-what-products-does-it-apply-to | P-sum | Yes: PO contacts Sales |
| Training on customer data | Commercial Terms Section B: "Anthropic may not train models on Customer Content from Services." API page: retained data is never used for training without express permission. | https://www.anthropic.com/legal/commercial-terms | P-sum | Confirm current terms version signed |
| DPA | Commercial Terms Section C says data is processed under the DPA. DPA text not read. | same | X (terms) | Legal review |
| FERPA / education terms | Not found in any fetched Anthropic page. | | X | Legal review |
| Regions | `inference_geo`: `"global"` (default) or `"us"` (US-only inference). Supported on Claude 4.6+ models. Workspace geo (data at rest): only `"us"` available. | https://platform.claude.com/docs/en/manage-claude/data-residency | P | PO decision (PO-7) |
| Region pricing | `inference_geo: "us"` is **1.1x** on input, output, cache writes and cache reads. Global is standard price. | same | P | n/a |
| Priority Tier | Not supported on Opus 5.5 or Sonnet 5.5 (per skill reference). Not re-verified live. | claude-api skill reference | S | Only if SLA capacity is wanted |
| Cloud-provider alternatives | Same model IDs on Bedrock (`anthropic.` prefix), Foundry, Google Cloud, Claude Platform on AWS. Partner-operated platforms have their own retention, region and price terms. Not evaluated. | models overview | P | Only if a campus cloud agreement applies |

### Illustrative cost arithmetic (ESTIMATE, not measured)
Using the design's own sizing (a 60-minute lecture, about 40 summary updates at 3k input and 500 output tokens, on Haiku 5.5, no caching, global geo): 120K input tokens x $0.10/MTok = $0.012, plus 20K output tokens x $0.50/MTok = $0.010, so about $0.02 per lecture hour for rolling summary. Thinking tokens, retries, Sonnet catch-ups and consolidation are excluded and billed as output. Treat as order-of-magnitude only. The STT line item is likely larger than the summary line item: STT list prices above range from about $0.15 to $0.29 per hour (Azure unknown).

## 4. Design-doc claims vs verified facts

Status key: OK = consistent with verified fact. DISCREPANCY = contradicted. UNVERIFIED = still not confirmed. STALE-RISK = true but incomplete or likely to change.

| # | Design-doc claim (file) | Verified finding | Status |
|---|---|---|---|
| 1 | Deepgram "Typically sub-300 ms partials; strong" (speech-pipeline §9) | Vendor states 300 ms or less transcription latency, 200-500 ms total; it is a claim, not measured, and excludes our capture and relay hops. Docs say finals are delayed by endpointing. | UNVERIFIED (claim only) |
| 2 | AssemblyAI "Low, but historically higher than Deepgram; improving" | No latency figures on AssemblyAI docs. Comparison not verifiable. | UNVERIFIED |
| 3 | Azure "Low to moderate; good with SDK" | No latency data retrieved. | UNVERIFIED |
| 4 | Deepgram cost "Per-minute, generally the cheapest of the three at list price" | Per-minute: true ($0.0048/min). Cheapest: **false**. AssemblyAI streaming English is $0.15/hr vs Deepgram $0.29/hr. Deepgram streaming price is promotional. | **DISCREPANCY** |
| 5 | AssemblyAI cost "Per-hour, mid" | $0.15/hr (English), $0.45/hr (U3.6 Pro). Per-hour: true. "Mid": depends on model; cheapest option on lists if English streaming model is used. | **DISCREPANCY** |
| 6 | Azure cost "Per-hour, mid; free tier; commitment tiers" | Per-hour: true. Free tier: 5 hrs/month, verified. Commitment tiers exist, verified. Price not verifiable from official page; third-party about $1/hr would make it the highest. | UNVERIFIED / likely **DISCREPANCY** on "mid" |
| 7 | Deepgram "Keyterm/keyword boosting" | Keyterm Prompting is a paid streaming add-on ($0.08/hr PAYG). | STALE-RISK (cost omitted) |
| 8 | AssemblyAI "Word boost / keyterms" | Keyterms included on U3.6 Pro and Multilingual; +$0.04/hr on English. | STALE-RISK (cost omitted) |
| 9 | Azure "Phrase lists, custom speech models (best customization, more effort)" | Custom endpoints exist (quota and $1.20/hr custom figure from third party). Phrase-list and "best customization" claims not checked. | UNVERIFIED |
| 10 | Diarization (streaming): Deepgram "available, quality varies"; AssemblyAI "available/limited"; Azure "conversation transcription, more setup" | All three offer streaming diarization at extra cost: Deepgram +$0.12/hr, AssemblyAI +$0.12/hr, Azure ~$0.30/hr (third party). Azure real-time diarization max 240 min per session. Quality is not assessed. | STALE-RISK (cost omitted); quality UNVERIFIED |
| 11 | Deepgram "Offers opt-out of model-improvement data; check retention" | Opt-out is real, but it is per request (`mip_opt_out=true`), not an account setting. Discount side-effect is conflicting. | OK with caveat |
| 12 | AssemblyAI "Check defaults and zero-retention options" | Zero retention for streaming exists for opted-out customers. Default state not stated. Free tier cannot opt out. | OK with caveat |
| 13 | Azure "enterprise terms, regional processing, no-logging options; strongest enterprise/compliance story" | Real-time STT: no data stored at rest, verified. Regional processing, DPA, "no-logging options" not verified. "Strongest" is a judgment. "Likely campus Microsoft agreements" is unknown. | PARTIAL / UNVERIFIED |
| 14 | Integration: Deepgram WebSocket from .NET; AssemblyAI WebSocket; Azure official .NET SDK | AssemblyAI is WebSocket (verified). Deepgram appears WebSocket (not stated directly). Azure SDK is referenced on quotas page ("Speech SDK"); .NET support not separately checked. | UNVERIFIED |
| 15 | Audio format 16 kHz mono PCM16 LE sent to vendor (§2, §4) | AssemblyAI default is mono 16-bit PCM (verified). Deepgram and Azure format support not retrieved. | UNVERIFIED for 2 of 3 |
| 16 | "Hard max session length (e.g., 3 h)" (§7) | Matches AssemblyAI's 3-hour auto-close. Azure real-time diarization caps at 240 min per session. | OK |
| 17 | "Idle timeout: stop vendor stream if no audio for N minutes (cost control)" (§7) | AssemblyAI bills open-connection time including idle. Idle cutoff is required, and Pause should close the vendor socket rather than only stop sending frames. | OK; design should be tightened |
| 18 | "Vendor settings must disable training on customer data and any retention" (§8) | Achievable on all three paths with per-vendor mechanics: Deepgram per-request flag; AssemblyAI account opt-out (paid only); Azure no storage at rest for real-time. | OK, mechanics differ |
| 19 | No concurrency figures in design | Deepgram 150 (PAYG), Azure 100 (default), AssemblyAI 100 new streams/min. See §2. Free tiers are far lower (Azure 1, AssemblyAI 5/min). | Gap in design |
| 20 | Model IDs: `claude-opus-5-5`, `claude-sonnet-5-5`, `claude-haiku-5-5` (AI design status line) | All three exist exactly as written. | OK |
| 21 | "Verify pricing and limits ... numbers are placeholders" (AI design) | Verified: see §3. | OK (now filled) |
| 22 | RollingSummary on Haiku 5.5 with escalation to Sonnet 5.5; Opus never on live path | Prices and IDs valid. Haiku 5.5 default effort `medium`; thinking cannot be fully disabled on Opus/Sonnet 5.5. Effort and thinking settings are not specified in the design. | STALE-RISK (latency/cost levers missing) |
| 23 | "Use tool-use / structured-output with JSON Schema" (AI design §4) | Forced `tool_choice` returns 400 on Opus 5.5 and Sonnet 5.5 (skill reference, not re-verified live). Use structured outputs (`output_config.format`) or `auto` + `strict`. | STALE-RISK |
| 24 | "Use prompt caching for the static system prompt + schema" (§3 step 6) | Valid, but needs a prefix of at least 512 tokens on all three models, else silently uncached. Cache reads are 0.05x (Opus, Sonnet) and 0.1x (Haiku). Default TTL 5 min suits the 45-90 s update cadence. | OK with caveat |
| 25 | "Input hard cap ~4k tokens/call" (§3 step 5) | Keeps Haiku 5.5 in its under-100K price band. | OK |
| 26 | Illustrative sizing: "40 updates x ~3k in / 500 out on Haiku" (§10) | About $0.02 per lecture hour before thinking tokens and retries (estimate in §3). | OK (estimate only) |
| 27 | Per-user and global budget caps "Product Owner approved" (§10) | Anthropic monthly spend cap by tier: Start $500, Build $1,000. At cap, requests return 429 with no `retry-after`, so the design's retry/backoff would loop uselessly. Design should treat `enforced_spend_limit_reached` as a terminal state. | **DISCREPANCY** (handling gap) |
| 28 | Retry on 429/5xx honoring retry-after (§9) | See row 27. Not every 429 has `retry-after`. | **DISCREPANCY** (partial) |
| 29 | `ai_usage` ledger tracks `cachedTokens` (§10) | API returns `cache_read_input_tokens` and `cache_creation_input_tokens`. Cache writes cost 1.25x/2x and count toward ITPM, so the ledger needs both fields to be accurate. | STALE-RISK |
| 30 | No Anthropic retention/ZDR statement in design | Standard retention is 30 days per privacy page; ZDR needs Anthropic approval; pages conflict (see §1 item 11). | Gap in design |
| 31 | No region requirement in design | `inference_geo` default `global`; US-only costs 1.1x. | Gap in design (PO-7) |
| 32 | PO-3: "Vendor facts in the speech doc are unverified" (DECISIONS.md) | Still true for latency, Azure price, FERPA/DPA, SLA. Partly verified for Deepgram and AssemblyAI price and Azure retention. | Partly closed |
| 33 | PO-4: Claude key and budgets (DECISIONS.md) | Tier and monthly spend cap determine feasible budgets. Needs the actual console reading. | Open, needs PO |

## 5. Could not verify (explicit list)

- Any **measured** streaming latency for any vendor. All latency items are claims or absent.
- **Azure** real-time STT list price (official page shows placeholders), diarization price, commitment tier prices, STT regions, latency, SLA, training-on-data statement, DPA text.
- **AssemblyAI** latency figures, concurrent-session cap (only new-streams-per-minute found), SLA, default (non-opted-out) streaming retention, DPA terms. The EU data-zone page was seen only through search results.
- **Deepgram** audio format and sample-rate limits, default US region, general DPA, retention duration page, SLA, whether MIP opt-out removes a discount (conflicting), whether enrollment is default.
- **FERPA** or education-specific terms for Deepgram, AssemblyAI, Azure Speech and Anthropic. Also any "school official" or data-processing language for education customers.
- Anthropic: the **actual tier, spend cap, retention setting and ZDR status of the Product Owner's account**. The two official retention statements conflict. Forced `tool_choice` and Priority Tier exclusions came only from the cached skill reference. The DPA text was not read.
- Provider **SLA/uptime** commitments for all vendors.
- Whether a campus has an existing Microsoft, Google or other agreement that changes terms or prices.
- Fetch limitation: most pages were returned as model-written summaries, and the Azure pricing and Anthropic API-retention pages were only partly read (truncated). Re-open the URLs before quoting exact terms in a contract or legal memo.

## 6. Requested human actions (Product Owner / legal)

1. Read the Anthropic Console limits page and Privacy Controls page and record tier, RPM/ITPM/OTPM, monthly spend cap, and retention setting (PO-4).
2. Decide whether to ask Anthropic Sales about ZDR and whether `inference_geo: "us"` (+10%) is required (PO-7).
3. Obtain Azure pricing through the Azure pricing calculator or a sales specialist, and DPA/region terms from Microsoft.
4. Request Deepgram's DPA and confirm in writing whether `mip_opt_out` changes pricing; request AssemblyAI's DPA and confirm default streaming retention on a paid account.
5. Send FERPA and education-use questions to `legal-compliance-reviewer` and counsel for all four vendors.
6. Authorize (or decline) a small synthetic-audio benchmark per vendor. No benchmark has been run.

## 7. Suggested benchmark inputs (proposal, not run)

Per `vendor-verification-researcher` scope: a 30-60 minute run per vendor using synthetic or consented audio only, with the same fixtures through each adapter. Record WER on STEM terms, interim-latency p50/p95/p99 using Deepgram's method (audio sent minus audio covered by interim), cost per lecture hour from the vendor invoice, behavior on idle and pause, and reconnect behavior. Hold a spend cap before any run.

## 8. Gate status

Vendor and Claude cost/limits selection: **UNVERIFIED** (unchanged). Facts above reduce uncertainty, but no account tier is confirmed, no pilot was measured, and no vendor was chosen. This report is AI-compiled research and cannot close a human-only gate.

## 9. Source index (all retrieved 2026-10-27)

- https://deepgram.com/pricing
- https://developers.deepgram.com/docs/live-streaming-audio
- https://developers.deepgram.com/docs/measuring-streaming-latency
- https://developers.deepgram.com/docs/the-deepgram-model-improvement-partnership-program
- https://developers.deepgram.com/docs/data-privacy-compliance
- https://www.assemblyai.com/pricing
- https://www.assemblyai.com/docs/speech-to-text/universal-streaming
- https://www.assemblyai.com/docs/data-controls.md
- https://www.assemblyai.com/docs/faq/how-to-opt-out-of-data-sharing-for-our-model-improvement-program.md
- https://www.assemblyai.com/docs/data-retention-and-model-training.md
- https://assemblyai.com/docs/streaming/endpoints-and-data-zones (search summary only)
- https://azure.microsoft.com/en-us/pricing/details/speech/
- https://azure.microsoft.com/en-us/pricing/details/cognitive-services/speech-services/
- https://learn.microsoft.com/en-us/azure/ai-services/speech-service/speech-services-quotas-and-limits
- https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/speech-service/speech-to-text/data-privacy-security
- https://platform.claude.com/docs/en/models/overview
- https://platform.claude.com/docs/en/about-claude/pricing
- https://platform.claude.com/docs/en/api/rate-limits
- https://platform.claude.com/docs/en/build-with-claude/prompt-caching
- https://platform.claude.com/docs/en/manage-claude/data-residency
- https://platform.claude.com/docs/en/manage-claude/api-and-data-retention
- https://privacy.claude.com/en/articles/7996866-how-long-do-you-store-my-organization-s-data
- https://privacy.claude.com/en/articles/8956058-i-have-a-zero-data-retention-agreement-with-anthropic-what-products-does-it-apply-to
- https://www.anthropic.com/legal/commercial-terms
- claude-api skill reference, cached table dated 2026-10-06 (cross-check only)
