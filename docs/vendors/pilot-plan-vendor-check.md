# Pilot plan vendor check (sections 3.4, 4.5, 6, 11 of `docs/product/pilot-plan.md`)

**Date read: 2026-11-04.** Researcher: `vendor-verification-researcher`. No sign-ins, no account actions, no spend, no terms accepted, git untouched. Only public pages and the public Azure Retail Prices API were read.

**Status tags used in every row**
- VERIFIED-PRIMARY: the vendor's own page or API returned its raw text/JSON to me and I read the value there.
- VERIFIED-SUMMARY: the value came through a fetch tool that summarises the page (a small model), or from a secondary site. Re-read live before relying on it.
- UNVERIFIED: not confirmed in any source I could read. Includes my own inferences, which are labelled INFERENCE.

All prices are public **list/retail** prices in USD. Nothing here is the price shown to the PO's subscription. Account price, account quota and terms screens still need the PO's own portal (gate G2 stays UNVERIFIED).

---

## 0. Headline: what changed or contradicts the plan

| # | Plan statement | What the sources say | Impact |
|---|---|---|---|
| 1 | 3.4 / 6 / 11.1: "Speech-to-text price: UNVERIFIED, amounts masked" | The Azure Retail Prices API is not masked. Real-time speech to text, Standard (meter `S1 Speech To Text`): **$1.00 per hour**, identical in `westus` and `westus3`. VERIFIED-PRIMARY (section 1). | Plan can now state a list price. Stage 1 speech cost is small (about $0.50 to $1.00 for a 30 to 60 minute run). |
| 2 | 3.4: "Diarization stays OFF" (no price given) | An add-on meter `S1 Speech to Text Enhanced Feature Audio` is **$0.30 per hour**. The pricing page says enhanced features incl. diarization are "Real-time: $- per hour per feature". Mapping the $0.30 row to diarization is an INFERENCE. | Diarization on would be about $1.30 per hour (INFERENCE). Off is still the plan; this just prices the decision. |
| 3 | 3.1 item 4 and 12.5: Sonnet 5.5 and Haiku 5.5 v2 offer Data Zone in West US 3 | Microsoft's pages agree (quota page, region tables list Sonnet 5.5 **v2** Data Zone in `westus` and `westus3`). **Anthropic's own Foundry page says the opposite for Sonnet 5.5**: "Claude Sonnet 5.5 supports only Global Standard deployments" and "For Claude Sonnet 5.5, select Global." Unresolved two-vendor contradiction. | Do not plan on Sonnet 5.5 Data Zone (US-only, 1.1x). Haiku 5.5 Data Zone is consistently listed by both. Settle in the real portal. |
| 4 | Plan never separates "Hosted on Azure" from "Hosted on Anthropic" | Two hosting options. v2 "Hosted on Azure": "prompts and completions remain within Azure. Only usage metadata and content flagged by Anthropic's safety systems egress to Anthropic." v1 "Hosted on Anthropic infrastructure" runs on Anthropic's infrastructure. Microsoft's Global Standard region table lists only **v1** for `claude-sonnet-5-5` and `claude-haiku-5-5`; v2 appears only in the Data Zone table. Microsoft's quota page, by contrast, marks both models v2 "Yes" for Global Standard. | Data-handling story differs by version. Choose v2 deliberately and confirm it exists in the chosen region. Also a Microsoft-internal inconsistency to confirm in the portal. |
| 5 | 3.4 "Ceiling from the default quota" ($9.60 and $0.48 per hour) | Arithmetic is right (section 6) but it is **not a true ceiling**: it ignores the 1.1x multiplier, cache-write pricing, and cache reads (which do not count toward ITPM). Sonnet ceiling alone exceeds the whole $5 monthly Azure budget in about 31 minutes. | Add caveats; keep the app-side cap as the only real stop. |
| 6 | 4.5 "Haiku about 20 times cheaper" | True only for prompts up to 100,000 tokens. Above that Haiku 5.5 costs $0.50 / $2.50, so the ratio drops to 4x. | Add the qualifier. |
| 7 | 3.4 "Data Zone (US) carry a 1.1x multiplier" | Confirmed, and Anthropic adds it applies to all token categories incl. cache and to Haiku's over-100k tier. | OK, extend wording. |
| 8 | 3.6 "Acceptable Use Policy update takes effect November 12, 2026" | Anthropic's page shows effective Nov 12, 2026, and the document is now called the **Usage Policy** (earlier renamed from AUP). Content changes are known only from secondary coverage. | Rename in plan; flag specifics as UNVERIFIED. |
| 9 | 3.1 item 1 / Review Desk: quota 0 after upgrade | Microsoft's quota page lists **0** for every Claude row under "Free Trial" and **40 / 40,000 / 8,000** under "Pay-as-you-go". No page says how long a reclassification takes or whether a request is needed (section 3). | The 0 you saw was a free-trial reading and is expected; a post-upgrade 0 is not explained by any doc. |

No arithmetic errors were found in the plan's cost examples (section 6). Assumption gaps are listed there.

---

## 1. (a) Azure AI Speech price, free tier, diarization

### 1.1 Exact queries (public, no auth)

Base: `https://prices.azure.com/api/retail/prices`, run with `curl -G --data-urlencode` so OData quoting is exact. Date run: 2026-11-04.

| # | `$filter` | Result |
|---|---|---|
| Q1 | `serviceName eq 'Cognitive Services' and contains(productName,'Speech')` | `Count: 0`, `Items: []`. **The service was renamed**; this filter from the task brief now returns nothing. |
| Q2 | `contains(productName,'Speech')` (no service filter) | Rows returned. Showed `serviceName: "Foundry Tools"`, `serviceFamily: "AI + Machine Learning"`, `productName: "Azure Speech"`. Other distinct productNames: `Custom Speech`, `Azure Speech - Disconnected`, `Translator Speech`, `Bing Speech`. |
| Q3 | `serviceName eq 'Foundry Tools' and productName eq 'Azure Speech' and armRegionName eq 'westus'` | `Count: 159`, `NextPageLink: null` (single page). |
| Q4 | `serviceName eq 'Foundry Tools' and productName eq 'Azure Speech' and armRegionName eq 'westus3'` | `Count: 157`, `NextPageLink: null`. |
| Q5 | `contains(productName,'Claude') or contains(meterName,'Claude') or contains(skuName,'Claude')` | `Count: 0`. Claude is billed as a Marketplace CCU meter and does not appear in the Retail API under that name. |

Status for rows below: **VERIFIED-PRIMARY** (raw JSON read by me). Currency USD, `type: Consumption`, `tierMinimumUnits: 0`.

### 1.2 Rows (West US 3 = `westus3`, West US = `westus`)

| Meter (`meterName`) | `skuName` | Unit | West US 3 | West US | Effective from | Meaning |
|---|---|---|---|---|---|---|
| S1 Speech To Text | S1 | 1 Hour | **$1.00** | **$1.00** | 2024-10-01 (W3) / 2018-11-01 (W) | Real-time speech to text, Standard. Portal "S0" appears as meter "S1" (INFERENCE from the meter name; the retail row is not labelled "real-time"). meterId `3bd267f0-01d5-4348-ba67-70e2473f0c27` (West US 3). |
| S1 Speech to Text Enhanced Feature Audio | S1 | 1 Hour | **$0.30** | **$0.30** | 2022-10-01 | Add-on per audio hour. Matches the pricing page's "enhanced add-on features ... per hour per feature" structure. Diarization mapping is INFERENCE. meterId `4c8e8932-3657-5cd0-bdb9-dfeb3820d202`. |
| Free Speech To Text | Free | 1 Hour | $0.00 | $0.00 | 2024-10-01 / 2018-05-07 | The F0 meter; the 5-hour allowance itself is not in the API. |
| Free Conversation Transcription | Free | 1 Hour | $0.00 | $0.00 | | F0 only. |
| S1 Speech to Text Batch | S1 | 1 Hour | $0.18 | $0.18 | 2024-05-01 | Batch, not real-time. |
| Fast Transcription | Fast Transcription | 1 Hour | $0.36 | $0.36 | 2024-11-01 | File-based, not live. |
| Fast Transcription Promo | Fast Transcription Promo | 1 Hour | $0.10 | $0.10 | 2026-09-01 | Promotional meter; terms and end date not visible. UNVERIFIED beyond the row. |
| S1 Custom Speech To Text | S1 | 1 Hour | $1.20 | $1.20 | 2023-10-01 / 2024-10-01 | Custom model. Not needed. |
| Emb Speech to Text | Emb Speech to Text | 1 Hour | $1.00 | $1.00 | 2026-11-01 | Embedded; not relevant. |

Commitment-tier rows (2K to 100K hours per month) exist; not relevant to this pilot.

### 1.3 Pricing page and quotas page

| Item | Value | URL | Date read | Status |
|---|---|---|---|---|
| Real-time Standard price on the web page | Masked: "Real-time Transcription: $- per hour." Explains why earlier fetches failed. The Retail API (above) is the unmasked source. | https://azure.microsoft.com/en-us/pricing/details/cognitive-services/speech-services/ | 2026-11-04 | VERIFIED-SUMMARY |
| Free tier allowance | "5 audio hours free per month", shared between Standard and Custom; Batch not supported on free. | same | 2026-11-04 | VERIFIED-SUMMARY |
| Does the 5 free hours apply to an S0 resource? | The quotas page says "For the Free (F0) pricing tier, see the monthly allowances on the pricing page", i.e. the allowance is an F0 feature. The Retail API shows no free quantity on the S1 meter (`tierMinimumUnits: 0`, `$1.00`). | quotas page; Retail API | 2026-11-04 | Allowance is F0-tied: VERIFIED-PRIMARY (quotas wording). Whether an S0 resource also gets 5 free hours: **UNVERIFIED** (assume no). |
| F0 concurrent real-time requests | **1**, "This limit isn't adjustable." | https://learn.microsoft.com/en-us/azure/ai-services/speech-service/speech-services-quotas-and-limits (page dated 2026-10-02) | 2026-11-04 | VERIFIED-PRIMARY |
| S0 concurrent real-time requests (base model) | **100 default**, adjustable; combined with speech translation requests. Existing value cannot be seen in portal/CLI; verify via support request. | same | 2026-11-04 | VERIFIED-PRIMARY |
| Max audio length for real-time diarization | 240 minutes per session (S0). | same | 2026-11-04 | VERIFIED-PRIMARY |
| F0 to S0 switch | "the change of the corresponding quotas might take up to several hours." (Speech only, not Claude.) | same | 2026-11-04 | VERIFIED-PRIMARY |
| Quota increase form requires work/org email | Personal email domains (hotmail, outlook, gmail) "aren't accepted"; "You can request quota increases only on behalf of your own organization." | same | 2026-11-04 | VERIFIED-PRIMARY. Relevant: an individual PO may not be able to raise Speech quota. Default 100 is far above need. |
| Billing granularity (per second vs per minute) | Not stated on any page I read. | n/a | 2026-11-04 | UNVERIFIED |
| Latency, training on audio, SLA, DPA | Not covered by this check. Still open (plan 11.2, 11.3). | n/a | | UNVERIFIED |

### 1.4 Answer to (a)

Standard real-time speech to text is **$1.00 per audio hour** in West US and West US 3 (list). Free F0 allowance is 5 hours per month with 1 concurrent stream. Diarization add-on is probably **$0.30 per audio hour** on top (INFERENCE), so about $1.30 per hour with it on.

---

## 2. (b) Claude on Foundry price, CCU, Data Zone multiplier

| Item | Value | URL | Date read | Status |
|---|---|---|---|---|
| Sonnet 5.5 base input / output | **$2 / MTok input, $10 / MTok output** | https://platform.claude.com/docs/en/about-claude/pricing (raw page text) | 2026-11-04 | VERIFIED-PRIMARY |
| Sonnet 5.5 cache | 5m write $2.50, 1h write $4, cache hit/refresh $0.10 per MTok (0.05x base) | same | 2026-11-04 | VERIFIED-PRIMARY |
| Haiku 5.5, prompts up to 100,000 tokens | **$0.10 input, $0.50 output** per MTok; 5m write $0.125, 1h write $0.20, hit $0.01 | same | 2026-11-04 | VERIFIED-PRIMARY |
| Haiku 5.5, prompts over 100,000 tokens | **$0.50 input, $2.50 output**; 5m write $0.625, 1h write $1, hit $0.05 | same | 2026-11-04 | VERIFIED-PRIMARY |
| CCU price | **$0.01 per CCU** ("One hundred (100) CCU represents $1.00 USD"), fixed. Microsoft's page says only "Fixed; published on the Claude offer on Azure Marketplace" (no number). Anthropic's page gives the number. | pricing page above; https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/claude-models-billing (updated 2026-10-27) | 2026-11-04 | VERIFIED-PRIMARY (Anthropic page). The Marketplace listing figure itself not seen by me. |
| Conversion | Tokens rated in USD at standard per-model rates, discount applied, then divided by $0.01. "CCU is a change in billing format, not in price." | Microsoft billing page | 2026-11-04 | VERIFIED-PRIMARY |
| Billing cadence and credits | Metered hourly, invoiced monthly in arrears, no prepaid CCU credits, no CCU balance or commitment. | both pages | 2026-11-04 | VERIFIED-PRIMARY |
| Data Zone (US) multiplier | **1.1x on all token pricing categories** (input, output, cache writes, cache reads), and on Haiku 5.5's over-100k tier. "Deployments hosted on Azure can use the US Data Zone Standard deployment type ... applies the same 1.1x pricing multiplier." | pricing page, "Inference geography" and data-residency text | 2026-11-04 | VERIFIED-PRIMARY |
| Per-account price shown in the portal | Foundry model card has a Pricing tab linking to the same Anthropic page; Foundry Monitor tab estimates exclude private-offer discounts. | Microsoft billing page | 2026-11-04 | Account price still UNVERIFIED (need PO's own portal / invoice). |
| Sonnet 5.5 Data Zone availability | Anthropic: "Claude Sonnet 5.5 supports only Global Standard deployments" and "For Claude Sonnet 5.5, select Global." Microsoft quota page lists Sonnet 5.5 Data Zone Standard (US) rows (v2 Yes); Microsoft region table lists `claude-sonnet-5-5` v2 Data Zone in `westus` and `westus3`. | https://platform.claude.com/docs/en/build-with-claude/claude-in-microsoft-foundry vs Microsoft pages | 2026-11-04 | **Contradicted between two primary sources.** Treat as unresolved. |
| Haiku 5.5 Data Zone availability | Listed as v2 Data Zone in `westus` and `westus3` (Microsoft region table). Anthropic's page lists no objection. | https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-from-partners | 2026-11-04 | VERIFIED-PRIMARY (listing). Actual portal option for this account: UNVERIFIED. |
| Global Standard, West US / West US 3 | `claude-sonnet-5-5` v1 and `claude-haiku-5-5` v1 ticked in `westus` and `westus3`. v2 is not in the Global table. | same | 2026-11-04 | VERIFIED-PRIMARY (contradicts Microsoft's own quota page, which marks v2 Global "Yes"). |
| Context and output | Sonnet 5.5 and Haiku 5.5: 1,000,000-token context, 128,000 max output tokens. Key parameters: `top_k`, `temperature`, `thinking={"type":"enabled"}` not supported; `top_p` must be 0.99; `output_format` only with adaptive thinking. Matches plan 3.2. | models-from-partners | 2026-11-04 | VERIFIED-PRIMARY |
| Not supported on Foundry | Message Batches API, Files API (hosted on Azure), Admin API, Models API, server-side `fallbacks`, code execution (hosted on Azure). | Anthropic Foundry page | 2026-11-04 | VERIFIED-PRIMARY |

Note on Anthropic's "Hosted on Anthropic infrastructure" (v1): this runs on Anthropic's infrastructure, not Azure. The plan's phrases "Claude on Azure" and "data stays in the agreed services" are only clearly true for **v2 Hosted on Azure**. Anthropic's page says for hosted-on-Azure deployments "prompts and completions remain within Azure. Only usage metadata and content flagged by Anthropic's safety systems egress to Anthropic." VERIFIED-PRIMARY.

### 2.1 Default quota and why a just-upgraded subscription may show 0

| Item | Value | URL | Date read | Status |
|---|---|---|---|---|
| Pay-as-you-go default, `claude-sonnet-5-5` and `claude-haiku-5-5` (Global Standard and Data Zone Standard US, v2; Global also v1) | **RPM 40, ITPM 40,000, OTPM 8,000** | https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/claude-models-quotas-limits (page dated 2026-09-21) | 2026-11-04 | VERIFIED-PRIMARY |
| Free Trial tab, same models | **0 / 0 / 0** on every Claude row | same | 2026-11-04 | VERIFIED-PRIMARY. Explains the CLI zeros recorded in `azure-platform-verification.md`. |
| Enterprise / MCA-E, same models | 10,000 RPM / 10,000,000 ITPM / 2,000,000 OTPM | same | 2026-11-04 | VERIFIED-PRIMARY (not the PO's tier) |
| Scope | Quota is managed per subscription; Global Standard deployments of the same model and version share one pool across regions; Data Zone Standard deployments share a pool per data zone. Only **uncached** input tokens count toward ITPM (cache creation counts; cache reads do not). | same | 2026-11-04 | VERIFIED-PRIMARY |
| "Yes" column meaning | "'Yes' means the combination supports quota allocation, but its default numeric limit might be zero." | same | 2026-11-04 | VERIFIED-PRIMARY |
| Does the default apply immediately on upgrade, or must it be requested? | The page says defaults are determined by subscription type; it says to submit the quota increase form only "to request quota beyond the default limits" (https://aka.ms/oai/stuquotarequest), "evaluated individually ... not guaranteed". **It says nothing about how long a free-trial to pay-as-you-go reclassification takes.** | same | 2026-11-04 | Default-by-type: VERIFIED-PRIMARY. Timing after upgrade: **UNVERIFIED**. Needing a request to get the default: no source says so. |
| Zero-quota error text | Troubleshooting row "Subscription eligibility error": "Your Azure subscription type or billing region isn't supported, or your subscription tier has a default quota of 0 for the model." Fix: "Confirm your subscription has an active pay-as-you-go billing method and a supported billing country/region." | https://learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/use-foundry-models-claude (dated 2026-09-21) | 2026-11-04 | VERIFIED-PRIMARY |
| Free-trial / credit-only unsupported | "Azure subscriptions that don't have an active pay-as-you-go billing method (for example, student, free trial, or startup credit-based accounts)" are unsupported; "If you have an account with a credit card on file, the credit card will be charged instead of Azure Credits" (for sponsored credit subscriptions). | same | 2026-11-04 | VERIFIED-PRIMARY |

**Why quota might still read 0 after the upgrade.** No source gives a cause, so these are hypotheses to test, all UNVERIFIED:
1. Propagation lag in the subscription-type classification. Related precedent: Microsoft says a Speech F0 to S0 change "might take up to several hours" (Speech page, section 1.3). For Claude, no number is published.
2. A stale client view: the CLI check was made before the upgrade (PO-20be), or the portal Quota page cached the old tier.
3. A missing step on the subscription side, for example the Marketplace SaaS offer not yet subscribed (see section 3) or the `Microsoft.SaaS` provider not registered. No page links quota to terms acceptance; I do not know if it matters.
4. The wrong deployment type or version being queried: Global Standard pools and Data Zone pools are separate, and only v2 or v1 rows with "Yes" support allocation.
5. Billing country/region not eligible (a different failure, but reported with the same "eligibility" wording).
6. Not a free-trial issue any more but a quota request genuinely needed: possible but unsupported by the page, which lists the default for pay-as-you-go as 40 / 40,000 / 8,000.

Suggested read-only human check, in order: wait at least a day after the upgrade, re-read the Quota page and the CLI usage listing for the chosen region (reads only), and if still 0, open an Azure support request citing the Microsoft page above. Do not accept terms or deploy to "test" it without a PO go (PO-20an).

---

## 3. (c) Is Marketplace / Anthropic terms acceptance required before deployment, and where is it shown?

| Item | Value | URL | Date read | Status |
|---|---|---|---|---|
| Terms are part of the first deployment | Portal steps: Discover > Models > pick a Claude model > **Deploy > Custom settings** > "Read the Azure Marketplace terms, select an industry, and select **Agree and Proceed** to accept the terms to subscribe to Azure Marketplace." | https://learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/use-foundry-models-claude | 2026-11-04 | VERIFIED-PRIMARY |
| Anthropic's wording | "On your first Claude deployment, review the Azure Marketplace terms, select an industry, and select Agree and Proceed to accept the terms and subscribe to the Azure Marketplace offer." | https://platform.claude.com/docs/en/build-with-claude/claude-in-microsoft-foundry | 2026-11-04 | VERIFIED-PRIMARY |
| Alternative entry | Subscribe from Azure Marketplace: select the **Claude Platform on Foundry** offer and accept it on the billing account, then deploy. Either path leads to CCU billing. | https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/claude-models-billing | 2026-11-04 | VERIFIED-PRIMARY |
| "Default settings" path | Docs say "Default settings" picks version 2 Hosted on Azure. Whether the terms screen also appears on that path is not stated. Assume yes (a Marketplace subscription is needed for any deployment). | use-foundry-models-claude | 2026-11-04 | UNVERIFIED |
| Permissions needed | Subscription: `Microsoft.MarketplaceOrdering/agreements/offers/plans/read` and `/sign/action`, `Microsoft.MarketplaceOrdering/offerTypes/publishers/offers/plans/agreements/read`, `Microsoft.Marketplace/.../agreements/read`, `Microsoft.SaaS/register/action` (one-time per subscription). Resource group: `Microsoft.SaaS/resources/read` and `/write`. Owner and Contributor include these. Also Contributor or Owner on the resource group to deploy. | https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-from-partners | 2026-11-04 | VERIFIED-PRIMARY |
| What looking costs | Browsing the catalog and reading the model card does not require acceptance; acceptance is a step inside Deploy. Nothing in the docs says the catalog view creates a subscription. | use-foundry-models-claude steps 1 to 5 | 2026-11-04 | VERIFIED-PRIMARY (step order) |
| Non-Microsoft terms | Partner models "are Non-Microsoft Products under the Product Terms"; "Anthropic acts as an independent processor for Microsoft. Customers using Claude through Microsoft Foundry are subject to Anthropic's data use terms." | models-from-partners; Anthropic Foundry page | 2026-11-04 | VERIFIED-PRIMARY |
| Text of the terms behind the "Agree and Proceed" screen / `aka.ms/anthropic_tandc` | Not read. It is shown only inside the portal flow. | n/a | | UNVERIFIED (plan 11.5 stands) |
| Individual purchase eligibility | "paid Azure subscription with a billing account in a country or region where Anthropic offers the models"; no statement either way about individuals versus organisations. Marketplace purchase may fail with "Marketplace purchase eligibility check failed" for country or SKU reasons. | models-from-partners | 2026-11-04 | Plan 11.4 stays UNVERIFIED |
| Zero data retention | Foundry troubleshooting: "Anthropic independently manages data retention for Claude on Azure, so Microsoft can't change this setting for you." New subscriptions have data retention enabled by default; ZDR is arranged with Anthropic. | use-foundry-models-claude | 2026-11-04 | VERIFIED-PRIMARY. Supports plan D11 wording. |

**Answer to (c).** Yes: the Marketplace terms are accepted at the first Claude deployment (or by subscribing to the "Claude Platform on Foundry" Marketplace offer). The screen is in the Foundry portal, **Deploy > Custom settings**, with an industry selector and the **Agree and Proceed** button. Nothing in the plan needs to change except noting the exact click path for the PO.

---

## 4. (d) Does a Foundry resource or project with no deployment carry a standing charge?

| Item | Value | URL | Date read | Status |
|---|---|---|---|---|
| Platform | "The Foundry platform is free to use and explore." "Individual features customers access and consume are billed at their normal rates." | https://azure.microsoft.com/en-us/pricing/details/ai-foundry/ | 2026-11-04 | VERIFIED-SUMMARY |
| Resource cost model | "When you create a Foundry resource, you pay for the Azure services you use ... Foundry resources run on Azure infrastructure and accrue costs when deployed." Pay-as-you-go (serverless) is billed by usage. Hosted fine-tuned models are the documented hourly-hosting exception ("applies even if the model is unused"). | https://learn.microsoft.com/en-us/azure/foundry/concepts/manage-costs (updated 2026-10-27) | 2026-11-04 | VERIFIED-PRIMARY |
| Claude | Metered hourly by token usage; no prepaid balance or commitment; no standing line item described. | claude-models-billing; Anthropic Foundry page | 2026-11-04 | VERIFIED-PRIMARY |
| Speech S0 resource | Retail rows are per-hour usage meters. Standing items exist only for custom models and endpoints (for example S1 Custom Speech Model Hosting Unit $1.29/day, Retail API); none apply unless created. | Retail API | 2026-11-04 | VERIFIED-PRIMARY |
| Explicit "an empty resource costs $0" sentence | **Not found on any page.** The conclusion is an inference from usage-based billing. | n/a | | Conclusion: INFERENCE, UNVERIFIED as an explicit guarantee |
| Other costs that can accrue | Azure Monitor Logs, alerting and other add-on services "incur extra costs" and appear outside the Foundry resource scope. Marketplace SaaS subscription fixed fee: not stated; CCU model implies none. | manage-costs | 2026-11-04 | VERIFIED-PRIMARY (add-ons); Marketplace fee UNVERIFIED |
| Hard spend limit | "Azure OpenAI doesn't currently provide this functionality" (hard limit); budgets are alerts. Supports the plan's app-side cap. | manage-costs | 2026-11-04 | VERIFIED-PRIMARY (Azure OpenAI wording; applying it to Claude is INFERENCE) |

**Answer to (d).** Nothing documents a standing charge for an empty Foundry resource or project. The plan can say "no deployment, no documented charge" but not "guaranteed zero". After the first month, check Cost Management grouped by Meter to confirm.

---

## 5. (e) Anthropic policy update effective 2026-11-12

| Item | Value | URL | Date read | Status |
|---|---|---|---|---|
| Effective date | **Nov 12, 2026.** Page presents three parts: "Universal Usage Standards", "High-risk Use Case Requirements", "Additional Use Case Guidelines" (consumer-facing chatbots and agents, products serving minors, agentic use). The page itself did not say what changed. | https://www.anthropic.com/legal/aup | 2026-11-04 | VERIFIED-SUMMARY |
| Name | The document is called the "Usage Policy" (renamed earlier from "Acceptable Use Policy"; Anthropic support article title "Updates to our Acceptable Use Policy (now 'Usage Policy') ..."). The plan's "Acceptable Use Policy" label is outdated. | WebSearch result titles | 2026-11-04 | VERIFIED-SUMMARY |
| Notice route and acceptance | Via Amazon Bedrock: AWS Health notice dated 2026-10-14; "Continued use of Anthropic models on Amazon Bedrock on or after November 12, 2026 ... constitutes acceptance of the updated terms." The notice says no change to availability, pricing, API behaviour or model IDs. For Foundry the equivalent text is only what the PO saw in his account banner (PO-20bd); I could not read it. | https://dev.classmethod.jp/en/articles/anthropic-usage-policy-update-bedrock-health/ | 2026-11-04 | VERIFIED-SUMMARY (secondary blog quoting the notice). Foundry wording: UNVERIFIED |
| Reported changes (secondary) | (1) "High-risk AI Recommendations" expanded from 7 to 11 areas; internal drafting, research, summarisation and analysis excluded unless output is ultimately given as advice or decisions for individuals. (2) New "High-risk Physical Actions" category (hardware control). (3) New prohibition on reselling, proxying or providing Claude access through unauthorised means. (4) Wider ban on law-enforcement and criminal-justice decisions. Other coverage: new rule against sustained cruelty to Claude; surveillance and election wording changes. Published 2026-10-08 per several outlets; previous version (2025-09-15) in force until Nov 12. | Classmethod article; search coverage | 2026-11-04 | **UNVERIFIED** as to wording (secondary sources; I did not retrieve the version history). |
| Relevance to Tether | Items touching Tether, to be read in the real policy by the PO or counsel: minors and "products serving minors" (ties to D7 age), "consumer-facing chatbots" guidance (applies if anyone but the PO's son uses it), whether study advice counts as a "recommendation to individuals" (likely no for summaries, per the reported exclusion), resale/proxy ban (does not apply to a personal tool, but would if shared). | n/a | | INFERENCE |

Recommendation: the 3.6 STOP bullet should read "Usage Policy update, effective 2026-11-12; PO to read the live policy and the Foundry account banner before first real call." Do not let an agent click through acceptance.

---

## 6. (f) Recomputation of the plan's cost examples

Inputs (all VERIFIED-PRIMARY, sections 1 and 2): Sonnet 5.5 $2 in / $10 out per MTok; Haiku 5.5 $0.10 / $0.50 (up to 100k prompts); Data Zone 1.1x; PAYG quota 40 RPM, 40,000 ITPM, 8,000 OTPM. Results below are **ESTIMATES**; token counts are the plan's hypothetical numbers.

### 6.1 Per-session example (plan 3.4): 500,000 input tokens, 50,000 output tokens

| Model | Input | Output | Total | With 1.1x | Plan says | Check |
|---|---|---|---|---|---|---|
| Sonnet 5.5 | 0.5 x $2 = $1.00 | 0.05 x $10 = $0.50 | **$1.50** | **$1.65** | $1.50 / $1.65 | Correct |
| Haiku 5.5 | 0.5 x $0.10 = $0.05 | 0.05 x $0.50 = $0.025 | **$0.075** | **$0.0825** | $0.075 / about $0.083 | Correct (rounded up) |

Assumption gaps: assumes all prompts are 100k or less for Haiku; no caching; Sonnet 5.5 Data Zone may not exist (section 0, row 3), so the $1.65 Sonnet figure may not be reachable. In CCU: $1.50 = 150 CCU; $0.075 = 7.5 CCU.

### 6.2 "Ceiling from the default quota" (plan 3.4), one hour flat out

| Quantity | Calc | Result | Plan | Check |
|---|---|---|---|---|
| Input per hour | 40,000 x 60 | 2.4 MTok | 2.4 MTok | Correct |
| Output per hour | 8,000 x 60 | 0.48 MTok | 0.48 MTok | Correct |
| Sonnet 5.5 | 2.4 x $2 + 0.48 x $10 = $4.80 + $4.80 | **$9.60** | $9.60 | Correct |
| Haiku 5.5 | 2.4 x $0.10 + 0.48 x $0.50 = $0.24 + $0.24 | **$0.48** | $0.48 | Correct |
| Sonnet 5.5, 1.1x (if Data Zone existed) | $9.60 x 1.1 | $10.56 | not stated | Omitted in plan |
| Haiku 5.5, 1.1x | $0.48 x 1.1 | $0.528 | not stated | Omitted in plan |

Assumption errors or omissions to flag:
1. **Not a true ceiling.** Only uncached input counts toward ITPM, and cache reads are excluded. A cache-heavy workload (rolling summaries re-sending a window are exactly that) can move more input than 2.4 MTok per hour. Cache reads cost 0.05x (Sonnet) or 0.1x (Haiku) of base input, so per-token cost is lower, but total spend is not bounded by the ITPM figure. Rough example (ESTIMATE): 40 requests per minute with 200,000-token cached prompts is 8 MTok/min of cache reads, about $0.80/min or $48/hour on Sonnet 5.5 at $0.10/MTok (before output). This is a theoretical bound set by RPM and context, not a forecast.
2. **Cache writes are billed above base** (Sonnet 5.5 5m write $2.50/MTok, Haiku $0.125/MTok) and do count toward ITPM. If all 2.4 MTok/hour were 5-minute cache writes: Sonnet $6.00 + $4.80 = $10.80; Haiku $0.30 + $0.24 = $0.54.
3. **Token-bucket bursts.** Per-minute limits can be spent in a burst; hourly arithmetic assumes steady use.
4. **Haiku over-100k prompts** cost $0.50 / $2.50. A single prompt of more than 40,000 uncached tokens may itself exceed the 40,000 ITPM limit and be throttled (INFERENCE from how the limits are defined; not tested).
5. **Budget context.** The PO's account budget is $5 per month (PO-20bd). At the stated Sonnet rate ($9.60/hr) $5 is reached in about 31 minutes; at Haiku ($0.48/hr) in about 10.4 hours. Budgets are email alerts only, so the app-side cap remains the only hard stop.
6. The plan's wording "Quota is UNVERIFIED for this account until it is upgraded; today it is 0" is out of date: the upgrade happened (PO-20be). It is now "UNVERIFIED, last read 0 on the free trial; re-read after upgrade" (section 2.1).

### 6.3 Speech (new, was blank in the plan)

| Scenario | Calc | Result | Status |
|---|---|---|---|
| Benchmark of 30 minutes, Standard, diarization off | 0.5 h x $1.00 | **$0.50** | ESTIMATE from VERIFIED list price |
| Benchmark of 60 minutes, Standard, diarization off | 1 h x $1.00 | **$1.00** | ESTIMATE |
| 60 minutes with diarization (INFERENCE that the $0.30 add-on is diarization) | 1 h x ($1.00 + $0.30) | **$1.30** | ESTIMATE, mapping unverified |
| Same 60 minutes on an F0 resource | within 5 free hours | **$0** but 1 stream only | F0 allowance VERIFIED-SUMMARY; stream limit VERIFIED-PRIMARY |
| One 5-hour week of lecture-length use on S0 (illustrative) | 5 x $1.00 | $5.00 | ESTIMATE; Stage 3 is blocked, so not a plan item |

Billing granularity (per second or per minute) is not documented in what I read, so short test runs could round up. UNVERIFIED.

Stage 1 total can now be bounded as: speech about $0.50 to $1.00 per 30 to 60 minute run (list), plus Claude, which is capped by the PO's chosen daily cap (D1). Plan 3.4's "Stage 1 total: cannot be stated until the speech price is seen" can be changed to "speech price now seen at list ($1.00/hr); Claude bounded by cap; account price still to confirm".

### 6.4 Section 4.5 ratio claim

| Claim | Calc | Result | Check |
|---|---|---|---|
| Haiku about 20x cheaper input | $2 / $0.10 | 20x | Correct for prompts up to 100k |
| Same on output | $10 / $0.50 | 20x | Correct for prompts up to 100k |
| Over 100k prompts | $2 / $0.50 and $10 / $2.50 | 4x and 4x | Plan omits this caveat |

---

## 7. Suggested edits to the plan (for Lead Architect; I did not edit it)

1. 3.4 and 6: replace "Speech-to-text price: UNVERIFIED" with "list price $1.00 per audio hour, Standard real-time, West US and West US 3 (Azure Retail Prices API, read 2026-11-04); account price still to confirm in the PO's calculator or invoice". Add a diarization add-on line, "about $0.30 per hour, mapping inferred".
2. 11.1: mark partly resolved (price). 11.2, 11.3 unchanged.
3. 3.1 item 1 and 3.4 quota paragraph: note the upgrade is done (PO-20be); re-read quota; add the section 2.1 hypotheses.
4. 3.1 item 4, 12.5 and D5: record the Anthropic versus Microsoft contradiction on Sonnet 5.5 Data Zone; prefer Haiku 5.5 for any US-only pilot.
5. 3.4: add "v2 Hosted on Azure vs v1 Hosted on Anthropic infrastructure" and prefer v2; note Microsoft's own pages disagree on whether v2 exists in Global Standard.
6. 3.4 ceiling paragraph: add the section 6.2 caveats and the $5 budget comparison.
7. 4.5: add "up to 100k prompts" to the 20x claim.
8. 3.6: rename to "Usage Policy" and point to the live policy.
9. 3.3 / D3: add the click path (Deploy > Custom settings > Agree and Proceed) and the permissions list.
10. 11 new item: confirm after one billing month that an idle Foundry resource and Speech S0 resource show $0 in Cost Management by Meter.

## 8. What I did not do or could not verify

- Did not sign in, open the Azure portal, view the PO's pricing/quota/terms screens, accept terms, deploy, or run `az` commands.
- Did not read the text behind `aka.ms/anthropic_tandc` or the Marketplace listing; the Marketplace CCU price figure is from Anthropic's page.
- Did not retrieve Anthropic's Usage Policy version history; change list is secondary.
- Fetch-tool results for the Speech pricing page, Foundry pricing page, Anthropic AUP page and the Classmethod article were model-summarised (VERIFIED-SUMMARY). The Learn pages, Anthropic Foundry/pricing pages and Retail API JSON were read as raw text.
- No measured latency or performance figures are claimed anywhere in this file.
- No gate status changes. G2 remains UNVERIFIED.

## 9. Source list (all read 2026-11-04)

- https://prices.azure.com/api/retail/prices (queries Q1 to Q5, section 1.1)
- https://azure.microsoft.com/en-us/pricing/details/cognitive-services/speech-services/
- https://learn.microsoft.com/en-us/azure/ai-services/speech-service/speech-services-quotas-and-limits
- https://learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/use-foundry-models-claude
- https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/claude-models-billing
- https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/claude-models-quotas-limits
- https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-from-partners
- https://learn.microsoft.com/en-us/azure/foundry/concepts/manage-costs
- https://azure.microsoft.com/en-us/pricing/details/ai-foundry/
- https://platform.claude.com/docs/en/build-with-claude/claude-in-microsoft-foundry
- https://platform.claude.com/docs/en/about-claude/pricing
- https://www.anthropic.com/legal/aup
- https://dev.classmethod.jp/en/articles/anthropic-usage-policy-update-bedrock-health/
