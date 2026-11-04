# Security and privacy review: pilot plan Stages 1 and 2

Prepared 2026-11-04 by `security-engineer` (AI agent) for the Lead Architect and the Product Owner (PO). Subject: `docs/product/pilot-plan.md` (DRAFT, untracked at the time of review), Stages 1 and 2 only. Stage 3 stays BLOCKED by G1 and is not reviewed here.

This is a review, not an approval. It clears no gate in `docs/RELEASE_GATES.md` and it is not legal advice. Ratings follow `docs/security/severity-policy.md` and are calibrated to the personal, non-commercial, one-user scope (PO-15/16); where I was unsure between two levels I took the higher one and say why.

## 0. What I read and what I ran

**Read (code and config):** `src/api/src/Modules/Ai/Tether.Ai/AiModule.cs`, `Provider/AiOptions.cs` (including `AiProviderGuard`), `Provider/AiClient.cs`, `Budget/Ledger.cs` (`GlobalSpend`, `UsageLedger`, `BudgetGate`, parts), `src/api/src/Modules/Transcript/Tether.Transcript/TranscriptModule.cs`, `Stt/SpeechToText.cs`, `src/api/src/Modules/Syllabus/Tether.Syllabus/SyllabusModule.cs` (guard lines), `src/api/src/Modules/Identity/Tether.Identity/Endpoints/IdentityEndpoints.cs` (dev login), `src/api/src/Tether.Host/Program.cs` lines 25-60, `appsettings.json`, `appsettings.Development.json`, `docker-compose.yml`, `docker-compose.dev.yml`, `.gitignore`, `.env.example`, `src/web/.env.example`, `src/web/package.json` (scripts), `src/web/next.config.mjs` (rewrites), `src/web/scripts/dev-real.mjs`, `.gitleaks.toml`, `.github/workflows/*` (grep only).
**Read (tests, names and bodies where cited):** `tests/Tether.AiTests/MockAiTests.cs` (`ProviderGuardTests`), `tests/Tether.IntegrationTests/B2FixTests.cs` (startup-refusal theory), `AiBudgetTests.cs` (test names), `LogPipelineCanaryTests.cs`, `AiNotesTests.cs` (`AiNotesLogCanaryTests`), `RealtimeLogCanaryTests.cs`, `LectureTicketAndIsolationTests.cs` (`LectureLogCanaryTests`) (class and test names only).
**Read (docs):** `docs/product/pilot-plan.md`, `docs/agents/project-playbook.md`, `docs/security/findings-register.md` (header, Part I, and the rows cited), `docs/security/severity-policy.md`, `docs/DECISIONS.md` PO-20am..PO-20be, `docs/vendors/azure-platform-verification.md` (retention, Speech, evidence and CLI sections).
**Ran (read-only, local):** `git ls-files`, `git check-ignore -v` on candidate secret paths, `git grep` for Azure endpoint/subscription patterns and for a personal first name, `git ls-files`/`git log --all` for committed images, `grep` over workflows for `secrets.`.
**Not run:** no test, build, container, dev server, network probe, `az` command or GitHub call. I did not check whether an `az` session is cached on the PO's machine, whether `next dev` is reachable from the LAN, or any Azure portal setting. Test names cited below as evidence were READ, NOT RUN today.

## 1. Short answer

Stage 1 is a reasonable next step **only after** the items marked "Stage 1" in section 9 are done. The app's mock-first design is sound today: every real provider is refused at startup (section 3). The gaps are what the pilot adds: there is no spend control at all for speech, the AI cap is not a true daily cap, the Azure account is now billing (PO-20be) while agents may still be able to act as Owner, audio fed through the microphone would send real household voices, and nothing technically stops real content from reaching a real provider. Stage 2 needs a feature that does not exist yet (Claude on his own typed notes), account export and deletion, a separate pilot database, and an honest statement that the PO's machine and the PO's agents can technically read the son's notes.

New findings in this review: 0 CRITICAL, **1 HIGH** (PPR-01), 9 MEDIUM (PPR-02..09, PPR-11), 3 LOW (PPR-10, 12, 13); the existing B1V-01 (MEDIUM) is re-rated as a Stage 1 precondition. They are not yet in `docs/security/findings-register.md` because this task allowed one file only; I own the register and will add them as PPR-01..PPR-13 when asked.

**PPR-01 is HIGH under the policy, so it blocks any push until it is resolved or the PO downgrades it in writing.** It is fixed by the PO on his own machine and needs no code (section 6).

## 2. Plan facts that are wrong or missing (corrections, not opinions)

- **Interface names.** Plan 3.2 says speech goes "behind `ISttProvider`" and Claude "behind `ILlmProvider`". Neither exists. The seams are `ISpeechToTextProvider` (`src/api/src/Modules/Transcript/Tether.Transcript/Stt/SpeechToText.cs:10`) and `IAiClient` (`src/api/src/Modules/Ai/Tether.Ai/Provider/AiClient.cs:15`). The syllabus extractor has its own independent guard (`SyllabusModule.cs:34`). (LOW, PPR-10.)
- **"Mock stays the default; real is switched on by explicit configuration."** True today for both seams (section 3), but the current guards refuse every real provider unconditionally. The pilot needs **new** guards; deleting the current refusal tests to make room would break playbook rule 1.4 ("never weaken or delete a test"). They must be replaced by tests for the new conditions in section 3.2.
- **Stage 2 "Real Claude on his own text".** No AI workflow takes typed notes or practice questions. `AiWorkflow` is `RollingSummary, Compaction, QuickRecap, SyllabusExtract, Notes, FinalSummary` (`AiClient.cs:29`); the lecture `Notes` workflow reads a transcript. Stage 2 therefore needs a new feature with a contract, a budget pool, deletion and export coverage, log canaries and a security review (PPR-09). The plan should list it under 4.3 as new build.
- **Keys "in a local key store" (3.2).** No such store is defined. `.env.example` says "Prefer `dotnet user-secrets` for the API", but the playbook says there is no host .NET SDK and the API runs in a container (`docker-compose.dev.yml:19-45`), so user-secrets does not apply. `.env.example` also names `CLAUDE_PROVIDER`, `STT_PROVIDER` and `ANTHROPIC_API_KEY`, which no code reads (the code reads `Ai:Provider`, `Speech:Provider`, `Ai:Anthropic:ApiKey`, i.e. env `Ai__Provider` etc.). See PPR-06.
- **"A visible switch returns the app to mock mode at once" (3.7, 7.1).** The provider is chosen at startup; there is no runtime switch. A UI toggle that can turn a paid provider **on** would be a new attack surface. The switch must be one-way (section 3.3).

## 3. Is the mock-versus-real switch fail-safe?

### 3.1 Today (DONE, cite)

| Property | Evidence |
|---|---|
| AI provider defaults to mock when unset | `AiOptions.cs:148` (`c["Ai:Provider"] ?? "mock"`); test `ProviderGuardTests.Mock_is_the_default_and_starts` (`MockAiTests.cs:105`) |
| Any non-mock AI provider is refused at startup, even with key and budgets; the key never appears in the error | `AiOptions.cs:197-205`; tests `MockAiTests.cs:107-130` (`DoesNotContain("sk-ant-test")`) |
| Only the mock AI client is registered | `AiModule.cs:35-36` |
| Speech provider other than `replay` is refused at startup | `TranscriptModule.cs:50-52`; test `B2FixTests.cs:347` (`Speech:Provider=deepgram` in Production fails startup) |
| Scripted transcript and fault injection refused outside Development/Test | `TranscriptModule.cs:43-49`, `AiOptions.cs:206-209` |
| Syllabus extractor and calendar provider must be `mock` | `SyllabusModule.cs:32,34` |
| Dev login refused outside Development | `IdentityModule.cs:29-32` |
| CI holds no provider secret | `grep secrets. .github/workflows` finds nothing (RAN) |

So today the app **cannot** call a paid provider. That stops being true the moment adapters exist; the new guard is the control that matters.

### 3.2 Required rules for the new guard (NOT DONE; owners backend-engineer, speech-engineer, ai-agent-engineer; review by me)

1. **One explicit stage setting** (proposal: `Pilot:Stage`, values 0, 1, 2; absent means 0). Stage 0 allows only mock/replay. Each real seam is allowed only in the stage that needs it: Stage 1 real AI and real speech; Stage 2 real AI for the new typed-notes workflow only, **real speech refused**, syllabus extractor still mock (section 5).
2. **The `Testing`/`Test` environments refuse every real provider regardless of other settings**, so no test run, local or CI, can spend. Add a test.
3. **A real provider needs, all at once:** an explicit provider name from a fixed list (for example `azure-foundry`, `azure-speech`), an endpoint and key read from the secret file (section 6.2), all three AI caps set explicitly (already required, `AiOptions.cs:201`), the new global daily cap (PPR-03), the new speech caps (PPR-02), and explicitly configured prices (the defaults in `AiOptions.cs:150-151` are list prices without the 1.1x Data Zone multiplier).
4. **A ceiling check:** refuse to start if any configured cap, or the sum of the pool caps, exceeds a PO-set ceiling (`Pilot:MaxMonthlyUsd`, D1). Today the defaults add up to $60 a month (lecture $50, `AiOptions.cs:113`, plus syllabus $10, `AiOptions.cs:28`) against a $5 Azure budget.
5. **No key in any message, log line or exception** (extend the existing `DoesNotContain` assertions to the new key names and to the endpoint).
6. **One startup line, metadata only,** saying which real providers and stage are active, and a UI marker. The existing markers already distinguish mock from real: `IDemoSourceIndicator` from `ISpeechToTextProvider.IsDemo` (`TranscriptModule.cs:66-67,123-126`) and `AiSource.Of(model)` (`AiClient.cs:98-102`). Stage 1 should add a plain "Pilot: synthetic material only" marker so a screenshot cannot be mistaken for real use.
7. Replace, do not delete, `Even_with_key_and_budget_a_real_provider_is_refused_because_no_adapter_exists` and the `B2FixTests` speech case: they become "refused when Stage is 0", "refused in Testing", "refused above the ceiling", "speech refused in Stage 2".

### 3.3 The kill switch

Define it as: stop the pilot compose project and start the normal dev stack (no secret file mounted, Stage absent, so mock). Nothing in the UI can turn a real provider on. A runtime "AI off" switch is fine if it can only reduce what is allowed. Write the two commands into the pilot runbook before Stage 1 (NOT DONE).

## 4. Spend: what must be true before the first real call

The plan is right that Azure budgets only email (PO-20bd, PO-20be) and that the app must enforce the cap. What the app has, and what it lacks:

**Has (DONE, READ, NOT RUN today):** a database-backed reserve-then-settle ledger checked before every AI call (`Ledger.cs:164-194`, `BudgetGate`), so the counter survives a restart (`ai.global_spend`, `Ledger.cs:16-41`); atomic monthly cap (`TryAddAsync`, `Ledger.cs:27-33`); tests `Parallel_reservations_against_a_cap_of_one_call_admit_exactly_one`, `The_global_monthly_cap_holds_under_parallel_load_from_many_users`, `When_settling_fails_the_reservation_stays_counted_and_further_paid_calls_stop`, `When_the_ledger_cannot_be_written_no_provider_call_is_made` (`tests/Tether.IntegrationTests/AiBudgetTests.cs`). The adapter contract already says a spend-cap 429 is terminal, never retried in a loop (`AiClient.cs:13`).

**Lacks:**

- **MEDIUM PPR-02: no spend control for speech.** `BudgetGate` covers AI calls only. Nothing limits real-time speech minutes. The Foundry resource the CLI found is S0 (billed per use, `azure-platform-verification.md` CLI section), and the speech price is unknown (plan 3.4). Fix before Stage 1: a server-side speech allowance (minutes per session, per day, per month) checked before a real stream opens and while it runs, stored in the database so it survives restart, with a test. Consider a separate Speech resource on the free F0 tier for Stage 1 (5 audio hours a month, 1 stream, `azure-platform-verification.md` 2.2); whether F0 refuses rather than bills past its quota is UNVERIFIED and must be confirmed on the portal. Rated MEDIUM, not HIGH, because Stage 1 audio is short fixture files and the PO watches the run.
- **MEDIUM PPR-03: the AI cap is not a daily cap.** Caps are per scope, per user per UTC day, and per pool per month (`AiOptions.cs:8-9`, `Ledger.cs:176-180`). Dev login creates a new user for any new email (`IdentityEndpoints.cs:83-93`), so the per-user daily cap is not a ceiling; the only global ceiling is monthly and per pool. Fix before Stage 1: a global daily cap across all pools and users, set from D1; the ceiling check in 3.2 rule 4; prices configured at the highest rate that can apply (Data Zone 1.1x; Haiku's over-100k-token rate if a prompt can exceed 100k); and the plan's own test (3.5 item 5): cap set very low, calls stop as `BudgetLimited`, restart, still stopped. The counter is an estimate from configured prices and provider-reported tokens; the PO's invoice is the truth (plan 3.8 item 2 is right).
- **Azure-side backstops (recommended, UNVERIFIED):** deploy with the smallest capacity the portal allows, if a capacity setting exists for Claude on Foundry; keep the $5 budget email; pay-as-you-go has no spending limit (my understanding, not re-verified today).

## 5. Which register blockers apply to Stage 1 versus later

| Register row | What it is | Stage 1 | Stage 2 | Reason |
|---|---|---|---|---|
| SBV-19, SBV-20, SBV-21, SBV-22 (MEDIUM) | Server does not check a real extractor's recurrence derivation, weekday evidence, alternatives, or times | **Not a precondition if the syllabus extractor stays mock** (today's guard, `SyllabusModule.cs:34`) | Same: keep the extractor mock in Stage 2 | Stage 2 bans school syllabi (plan 4.1); "his own plans" do not need the extractor. If the PO wants the real extractor in Stage 1 on fake syllabi, these four must be fixed first or the measurement will report checked results that are not checked |
| SBV-23..SBV-27 (LOW, OPEN); SBV-28 (LOW, DEFERRED) | Further extractor checks | Not a precondition | Not a precondition | Required before a real extractor sees real syllabi (later than Stage 2) |
| B1V-01 (MEDIUM) | Provider-scoped log config re-enables content-bearing categories | **Precondition** (cheap; the stage's own log measure, plan 3.5 item 7, is meaningless without it) | Precondition | Real SDKs add new categories (HttpClient, Azure.Core, the Speech SDK's own log file) that are not in the code filter list (`Program.cs:38-46`) |
| NAS-04, NAS-R01 (MEDIUM) | Prompt injection and grounding gaps in notes | Measure, not precondition (synthetic input) | Not a precondition for his own text; the "AI-generated" label part of NAS-04 is | Injection in his own notes is self-inflicted; wrong notes are R6 in the plan |
| NAS-05 (MEDIUM) | What the student was told before content was sent | Not a precondition | **Precondition** for the new workflow: record the `noticeVersion` of the approved Stage 2 warning (D8) | Same mechanism as `Ai:Notes:NoticeVersions` |
| NAS-R02 (MEDIUM) | Queued notes press can start hours late | Not a precondition | Not a precondition (no lecture notes in Stage 2) | |
| NEW-01 (MEDIUM, DEFERRED) | No account deletion or export-all | Not needed (synthetic) | **Precondition** (PO-20ax) | |
| SB-01 (MEDIUM) | Postgres server log can hold row text | Met by the dev compose flags (`docker-compose.yml:15-32`) | **Precondition:** the pilot database must carry the same flags | |
| M2C-29 (MEDIUM) | No CSP; no test that a provider key is absent from the web bundle | **Precondition: the bundle test only** | Same | Keys must live only in the API container |
| SEC-PUBLIC (MEDIUM, DEFERRED) | Public repo maps open findings | Not a precondition | **PO must revisit** (the row says it blocks real data) | Local-only use lowers the impact; the decision should be on record |
| M1-F04, M1-F07, M1-F08b, M2C-08, NEW-02, GR-*, CV-* | Auth hardening, Google, calendar | Not preconditions | Not preconditions, provided Google stays off (D12) and the app is loopback-only (PPR-04) | |
| NEW-03 | Scan evidence | Recommended: dependency and licence check of each new SDK package (`Anthropic.Foundry`, the Speech SDK) before it is added; L-17 inventory | Same | |

## 6. Keys, the Azure account and the PO's device

### 6.1 HIGH PPR-01: agents may be able to act as Owner on a subscription that now bills

**Finding.** The read-only CLI check of 2026-11-04 was run by an agent with "PO signed in" (`azure-platform-verification.md`, CLI section). The Azure CLI keeps its sign-in in the user profile and reuses it for later commands. Since PO-20be the subscription is pay-as-you-go with "billing live", and the PO's role is Owner (same file, evidence table). If that session is still cached, any agent shell on this machine can run commands that create deployments, request quota, or print the Foundry resource's keys into its transcript. Agents read untrusted web pages (vendor research), so a prompt-injection chain to such a command is plausible. Nothing technical stops it; only agent instructions do. **UNVERIFIED:** I did not check whether a session is cached (I was told not to touch the cloud account).
**Why HIGH:** unsure between MEDIUM and HIGH; the exposure exists now, not at a future stage, and spend is uncapped on the Azure side.
**Fix (PO, minutes, no code):** (1) `az logout` after every PO-attended check, and confirm with `az account show` that nothing is signed in. (2) For future read-only checks, a separate identity with **Reader** on the one resource group only (Reader has read actions only, so it cannot list keys or deploy; confirm in the portal), used by agents through a separate `AZURE_CONFIG_DIR` so the PO's own Owner sign-in is never in the agents' profile. (3) Regenerate both keys of the existing Foundry resource if there is any chance they were printed in an agent session (agent transcripts are stored locally and sent to Anthropic). Close the finding with the PO's dated note of (1) and either (2) or a decision that agents get no Azure access at all.

### 6.2 MEDIUM PPR-06: where the keys live

**DONE:** `.gitignore:1-10` ignores `.env`, `.env.*` (except `.env.example`), `*.key`, `*.pem`, `*.pfx`, `secrets/`, `secrets.json`, `appsettings.*.local.json`; `git check-ignore -v` confirmed `.env`, `.env.local`, `src/api/.env`, `secrets.json` and `appsettings.Development.local.json` are ignored (RAN). gitleaks runs with all default rules on (`.gitleaks.toml:12-13`); the allowlists are scoped to test literals.
**Problem.** A git-ignored file in the repo folder is still inside the folder that is bind-mounted into the API container (`docker-compose.dev.yml:25-26`, `.:/repo`) and readable by every agent working in the repo. Environment variables passed to a container are shown by `docker inspect`, which agents run routinely. Whether gitleaks' default rules recognise an Azure AI services key is UNVERIFIED.
**Fix before Stage 1:**
- Keep the endpoint and key in one file **outside the repo** (for example under the PO's user profile), mounted into the API container as a compose secret file and read by path (for example `Ai:Foundry:ApiKeyFile`). Use a pilot compose override that also lives outside the repo, or one in the repo that only names a path variable.
- Correct `.env.example`: remove the unused `CLAUDE_PROVIDER`/`STT_PROVIDER`/`ANTHROPIC_API_KEY` names and the user-secrets advice, and state the real setting names. A person setting `CLAUDE_PROVIDER=mock` today believes something the code ignores.
- Never `NEXT_PUBLIC_` anything provider-related; the web never needs a key because audio goes through the server relay (M2C-29 bundle test).
- Add a gitleaks custom rule for the Azure key shape and prove it with a scratch file holding a fake key of that shape (not a real key).
- Rotation: Azure gives two keys. Regenerate after Stage 1, before Stage 2, and immediately after any exposure (chat paste, agent transcript, screenshot, log). Write the rotation steps into the runbook.
- Optional: Entra ID sign-in instead of keys is supported by the Foundry .NET client (`azure-platform-verification.md` line 51), which removes the long-lived key, at the cost of a token flow inside the container. Not required for a local pilot.

### 6.3 The Azure account

| Item | Status |
|---|---|
| Adult PO is the only subscription holder and Owner; no other role assignments | UNVERIFIED (role Owner observed; other assignments not listed) |
| MFA on the PO's Microsoft sign-in (authenticator or passkey, not SMS) | UNVERIFIED (Microsoft enforces MFA for Azure sign-in as far as I know; confirm in the account security page) |
| No diagnostic settings that ship request/response logs to Log Analytics or storage for the Foundry or Speech resource | UNVERIFIED (what Claude-on-Foundry resource logs contain is not documented in the repo) |
| Budget alert email reaches an inbox the PO reads | UNVERIFIED |
| The auto-created Foundry resource (S0) is either the one used or removed, so there are no idle billable keys | NOT DONE (PO decision) |

### 6.4 The PO's device

- **MEDIUM PPR-04: the web dev server listens on all network interfaces.** `dev:real` runs `next dev` with no host (`src/web/scripts/dev-real.mjs:8`); Next.js listens on all interfaces by default (its CLI documentation, not re-verified today, not probed). It proxies `/api`, `/hubs` and `/ingest` to the API (`next.config.mjs:18-23`). Dev login needs only an email (`IdentityEndpoints.cs:76-96`). A device on the home network could sign in as the son, or spend the cap. The Origin check does not stop a non-browser client, which can set any Origin. API and DB are loopback-only already (`docker-compose.dev.yml:24`, `docker-compose.yml:38`). Fix before Stage 1: `next dev -H 127.0.0.1` for the pilot, and the son uses the app on the PO's machine itself, not from another device. MEDIUM because it needs someone on the home network.
- **MEDIUM PPR-05: the pilot would share the dev database with tests and agents.** The playbook says integration tests share the dev database and agents stop, start and recreate these containers. Real pilot data there can be read by agents (and so sent to Anthropic through Claude Code, under terms this repo has not reviewed), wiped by a test or `docker compose down -v`, and mixed with test spend in the same ledger. Fix: a separate pilot compose project with its own container names, its own volume, a non-default database password (not `cc_dev_only_password`), the SB-01 Postgres flags, and an export taken before and after each session. Required for Stage 2; strongly recommended for Stage 1 so the cost counter is clean.
- **Agents and real content.** In Stage 2, agents must not read the pilot database, its logs, or screenshots of his notes. Claude Code permission deny rules for the pilot container, volume and secret file are the PO's to set; whether any exist is UNVERIFIED (no project `.claude/settings.json` is tracked).
- **Disk encryption** (Windows device encryption on Windows 11 Home) for the machine holding the pilot volume: UNVERIFIED.
- **MEDIUM PPR-11: "no one, not even the PO" (plan 4.4) is a promise the setup cannot keep.** The app runs in the PO's Windows account, under the PO's Docker, with a database password the PO holds, next to agents the PO runs. The PO can technically read every note. The family decision is fine, but the Stage 2 warning (D8) must say this plainly, or the setup must change (for example the son's own Windows account and a pilot stack only he starts). Rated MEDIUM because a false privacy statement to the only user is a real-user harm.

## 7. What leaves the machine in each stage

| Destination | Stage 0 (now) | Stage 1 | Stage 2 |
|---|---|---|---|
| Azure AI Speech (region of the resource) | nothing | fixture audio only (section 8) | **nothing**: real speech refused by the guard |
| Claude on Foundry | nothing | synthetic transcript windows, fake syllabus text only if the extractor guard is lifted (not recommended), fixed system prompts | his typed notes and questions, through the new workflow only |
| Anthropic (outside Azure) | nothing | content flagged by Anthropic's safety systems, and usage metadata (`azure-platform-verification.md` line 67) | same, but now his text |
| Microsoft (billing, metrics) | nothing | token counts, audio minutes, resource metadata | token counts |
| Google | nothing | nothing (no Google code; calendar provider guarded, `SyllabusModule.cs:32`) | nothing unless D12 |
| Next.js usage telemetry | anonymous metadata (LOW PPR-13: `NEXT_TELEMETRY_DISABLED` is set nowhere in the repo; set it for the pilot) | same | same |
| Font host at build time | build-time font fetch (register RN-06) | same | same |
| Anthropic through Claude Code (agents) | repo content only | anything an agent reads: logs, DB rows, screenshots | **must be nothing of his** (PPR-05) |
| GitHub (public) | code and docs | must never include fixture outputs with endpoints, resource names, invoices or screenshots | must never include his content |

The plan's measure 3.5 item 8 (network review) needs a method: other traffic on the same machine (browsers, Claude Code) will appear too, so the check must be per container. Owner devops-engineer; UNVERIFIED what is feasible on Docker Desktop.

## 8. Keeping Stage 1 synthetic in fact, not only in intent

- **MEDIUM PPR-07: microphone audio would send real voices.** If Stage 1 plays lecture audio into the browser microphone, the microphone also picks up whoever is in the room. That is another person's voice sent to Azure, which is G1 territory, and recording controls are not built (plan section 5). Fix: in Stage 1 the real speech adapter accepts audio only from a **server-side fixture source**: the server streams an allow-listed fixture file through the real provider; the browser microphone path to the real provider is refused. In Stage 2, real speech is refused entirely (3.2 rule 1). Diarization off and the Speech SDK's own audio and file logging off, as the plan says, with a test that the adapter sets them.
- **MEDIUM PPR-08: nothing technical stops real content.** "Synthetic only" is a rule the PO follows, not something the app checks. Fix: a fixture manifest outside the source tree (id, origin, licence, SHA-256, a unique planted phrase per fixture). In Stage 1 the real AI path accepts only scopes whose source is a registered fixture (a fixture-fed session, or document text containing a registered marker), and refuses anything else. The planted phrases double as the log canary (plan 3.5 item 7). Self-made or licensed audio must be stored with its licence; do not commit licensed audio to the public repo unless the licence allows redistribution.

## 9. Precondition checklists

Tags: **DONE** (code or test cited, READ, NOT RUN today unless stated), **NOT DONE**, **UNVERIFIED** (may be true; no evidence in the repo or not checkable by me).

### 9.1 Stage 1 (real services, synthetic or licensed content only)

| # | Precondition | Status | Evidence or owner |
|---|---|---|---|
| 1.1 | PPR-01 closed: no cached Owner session reachable by agents; Reader-only identity or no agent Azure access | NOT DONE / UNVERIFIED | PO |
| 1.2 | MFA on the PO's Azure sign-in; PO sole Owner | UNVERIFIED | PO |
| 1.3 | PO decisions D1 (cap), D3 (terms, read in person), D4 (written go), D5 (region) | NOT DONE | PO; D2 done by PO-20be, read-only verification pending (UNVERIFIED) |
| 1.4 | Key and endpoint in a secret file outside the repo, mounted as a compose secret, read by path | NOT DONE | devops-engineer, backend-engineer (PPR-06) |
| 1.5 | Secret file names are git-ignored | DONE | `.gitignore:1-10`; `git check-ignore -v` (RAN) |
| 1.6 | gitleaks recognises the Azure key shape | UNVERIFIED | devops-engineer (custom rule, fake-key proof) |
| 1.7 | `.env.example` corrected (real setting names, no user-secrets advice) | NOT DONE | devops-engineer |
| 1.8 | CI holds no provider secret | DONE | workflows grep for `secrets.`: none (RAN) |
| 1.9 | Test: no provider key or endpoint in the web bundle | NOT DONE | frontend-engineer (M2C-29 part) |
| 1.10 | Mock and replay are the defaults; non-mock refused today | DONE | `AiOptions.cs:148,197-205`; `TranscriptModule.cs:50-52`; `MockAiTests.cs:105-130`; `B2FixTests.cs:347` |
| 1.11 | New guard per section 3.2 (stage setting, Testing refuses, explicit caps and prices, ceiling check, no key in messages, startup line, replacement tests) | NOT DONE | backend-engineer, speech-engineer, ai-agent-engineer |
| 1.12 | AI cap checked before every call and held across restart | DONE (mechanism) | `Ledger.cs:164-194`, `Ledger.cs:16-41`; `AiBudgetTests` (4 tests named in section 4) |
| 1.13 | Restart test for the cap (plan 3.5 item 5) | NOT DONE | qa-engineer |
| 1.14 | Global daily AI cap across pools and users, sized to D1; prices at the highest applicable rate | NOT DONE | backend-engineer (PPR-03) |
| 1.15 | Speech minutes cap (session, day, month), database-backed, tested | NOT DONE | speech-engineer, backend-engineer (PPR-02) |
| 1.16 | Provider cap 429 is terminal; throttling shows a message, no retry loop | DONE at interface contract (`AiClient.cs:13`); adapter NOT DONE | ai-agent-engineer |
| 1.17 | Existing log canaries pass | DONE (authored; CI per playbook) | `LogPipelineCanaryTests` (Development and production-like), `AiNotesLogCanaryTests`, `RealtimeLogCanaryTests`, `LectureLogCanaryTests` |
| 1.18 | B1V-01 fixed; filters and canaries for the adapters' new log categories and their error paths (400, 429, 500, timeout) | NOT DONE | backend-engineer |
| 1.19 | Speech SDK audio/file logging off and diarization off, asserted by a test | NOT DONE | speech-engineer |
| 1.20 | No Azure diagnostic settings shipping request/response content | UNVERIFIED | PO |
| 1.21 | Real speech accepts server-side fixture audio only; microphone path to the real provider refused | NOT DONE | speech-engineer, backend-engineer (PPR-07) |
| 1.22 | Fixture manifest with planted phrases; real AI accepts registered fixtures only | NOT DONE | ai-agent-engineer, qa-engineer (PPR-08) |
| 1.23 | Syllabus extractor stays mock (or SBV-19..22 fixed first) | DONE today | `SyllabusModule.cs:34`; keep it |
| 1.24 | Web dev server bound to 127.0.0.1; API and DB loopback | API and DB DONE (`docker-compose.dev.yml:24`, `docker-compose.yml:38`); web NOT DONE | frontend-engineer, devops-engineer (PPR-04) |
| 1.25 | Separate pilot compose project, volume and DB password | NOT DONE (recommended for Stage 1) | devops-engineer (PPR-05) |
| 1.26 | Pause and Stop close the real speech stream and cancel an in-flight Claude call | UNVERIFIED for real adapters (session cutoff exists for the mock, `AiModule.cs:52-54`) | speech-engineer, ai-agent-engineer, qa-engineer |
| 1.27 | Adapters keep TLS certificate validation on; no custom validation callback | NOT DONE (no adapter) | backend-engineer; I review |
| 1.28 | Dependency and licence check of each new SDK package | NOT DONE | devops-engineer, legal-compliance-reviewer (L-17) |
| 1.29 | Kill-switch and key-rotation steps written in the runbook | NOT DONE | devops-engineer |
| 1.30 | Public repo: no images committed; no endpoints, resource names or subscription ids committed | DONE as of today | `git ls-files` and `git log --all`: 0 image files; `git grep`: only the placeholder `{resource}` (RAN) |
| 1.31 | Deletion and export | Not required for Stage 1 (synthetic) | |

### 9.2 Stage 2 (his own typed material; no audio)

Every Stage 1 item stays true, plus:

| # | Precondition | Status | Evidence or owner |
|---|---|---|---|
| 2.1 | Stage 1 exit evidence (plan 3.8), invoice matched to the counter | NOT DONE | PO, qa-engineer |
| 2.2 | Real speech refused in Stage 2 by the guard | DONE today (replay only, `TranscriptModule.cs:50-52`); stage rule NOT DONE | backend-engineer |
| 2.3 | Typed-notes/practice-question workflow built with contract, own budget pool, user-scoped storage, deletion and export coverage, log canary, `noticeVersion` of the D8 warning; security review passed | NOT DONE | Lead (contract), backend-engineer, ai-agent-engineer; me (PPR-09) |
| 2.4 | Export-all and delete-account built and tested | NOT DONE | backend-engineer (NEW-01, PO-20ax) |
| 2.5 | Separate pilot database, non-default password, SB-01 flags, export before and after sessions | NOT DONE | devops-engineer (PPR-05; SB-01 flags exist for dev, `docker-compose.yml:15-32`) |
| 2.6 | B1V-01 fixed; planted-phrase canary re-run on the pilot stack | NOT DONE | backend-engineer, qa-engineer |
| 2.7 | Agents denied the pilot container, volume, logs and secret file; no screenshots of his notes | UNVERIFIED | PO |
| 2.8 | Device encryption on; son uses the PO's machine locally; web loopback-only | UNVERIFIED / NOT DONE | PO (PPR-04) |
| 2.9 | Warning text (D8) covers vendor retention, that delete does not recall vendor copies, and that the PO's machine and agents can technically read his notes | NOT DONE | PO, neurodivergent-ux-specialist (PPR-11) |
| 2.10 | F-M1, F-M3, F-M4, F-M9 answered in writing; D7 (age); D11 (ZDR) decided | NOT DONE | PO, legal-compliance-reviewer |
| 2.11 | SEC-PUBLIC revisited and recorded by the PO | NOT DONE | PO |
| 2.12 | "AI-generated" label on output (NAS-04 part) | UNVERIFIED | frontend-engineer |
| 2.13 | Syllabus extractor still mock | DONE today | `SyllabusModule.cs:34` |
| 2.14 | Google off unless D12 | DONE today | no Google write code; `SyllabusModule.cs:32` |
| 2.15 | Keys rotated after Stage 1 | NOT DONE | PO |

## 10. Vendor retention: what the family should assume

Sources are repo documents; the underlying pages were read through a summarising fetch tool and are not re-verified here.

- **Claude on Foundry.** Anthropic's retention page says it covers Claude in Microsoft Foundry (`azure-platform-verification.md` line 73). The 30-day default and the longer period (up to 2 years) for content flagged by safety systems are the direct-API figures (`anthropic-terms-review.md` S3, cited by plan 4.2); that they apply to Foundry is an **inference, UNVERIFIED**. Prompts "remain within Azure" except flagged content and usage metadata, which go to Anthropic, where staff may review it "on an exceptions-only basis" (line 67). No training on customer content is in Anthropic's commercial terms; whether the version Foundry presents says the same is UNVERIFIED (line 72). Zero data retention is Anthropic's approval, not a setting (line 74; D11). Whether Microsoft keeps any Claude prompt data for abuse monitoring is UNVERIFIED (line 75).
- **Azure Speech real-time.** Microsoft says real-time audio is processed in memory and not stored at rest (line 168). Training on customer audio is not stated (line 170, UNVERIFIED). Diarization off.
- **What to assume:** anything sent to Claude may be kept for about 30 days, possibly up to 2 years if flagged, may be seen by Anthropic staff in exceptional cases, and cannot be recalled by deleting it in Tether. In Stage 2 he should not put passwords, health details, or other people's personal information in his notes. This is a plain-language working assumption for the warning, not a legal statement.

## 11. Findings in this review

| ID | Severity | Finding | Fix (short) | Stage |
|---|---|---|---|---|
| PPR-01 | **HIGH** | Agent shells may hold the PO's Owner Azure CLI sign-in on a now-billing subscription (UNVERIFIED whether cached) | `az logout`; Reader-only identity in a separate config dir, or no agent Azure access; regenerate keys if exposed | Now (blocks push per policy until resolved or downgraded in writing) |
| PPR-02 | MEDIUM | No spend control for speech | Database-backed speech minutes caps; consider F0 | 1 |
| PPR-03 | MEDIUM | AI cap is per user and per pool, not a global daily cap; defaults sum to $60/month | Global daily cap from D1, ceiling check, high-side prices | 1 |
| PPR-04 | MEDIUM | Web dev server on all interfaces plus passwordless dev login (not probed) | `-H 127.0.0.1`; local use only | 1 |
| PPR-05 | MEDIUM | Pilot would share the dev DB with tests and agents | Separate pilot compose project, volume, password | 1 recommended, 2 required |
| PPR-06 | MEDIUM | Key location undefined; in-repo `.env` readable by agents and mounted; env vars visible in `docker inspect`; `.env.example` names unread settings | Secret file outside the repo, mounted and read by path; fix `.env.example`; gitleaks rule; rotation | 1 |
| PPR-07 | MEDIUM | Microphone input in Stage 1 would send real voices | Server-side fixture audio only; real speech refused in Stage 2 | 1 |
| PPR-08 | MEDIUM | No technical synthetic-only interlock | Fixture manifest and marker check on the real AI path | 1 |
| PPR-09 | MEDIUM | Stage 2's typed-notes workflow does not exist and is not in the plan's build list | Contract, pool, storage, deletion/export, canary, review | 2 |
| PPR-11 | MEDIUM | "No one, not even the PO" is not technically true on the PO's machine | Say so in D8, or separate account and stack | 2 |
| B1V-01 (existing) | MEDIUM | Re-rated as a Stage 1 precondition, with new adapter log categories | as register | 1 |
| PPR-10 | LOW | Plan names non-existent interfaces | Use `ISpeechToTextProvider`, `IAiClient` | doc |
| PPR-12 | LOW | A personal first name appears in tracked file paths: `docs/security/findings-register.md:191`, `docs/ux/redesign-progress.md:264,265,374,375` (`git grep`, RAN) | Replace with `<repo>`; never commit Azure resource names (portal-generated names can contain a person's name), endpoints, subscription or tenant ids, invoices or screenshots; keep them in the secret file | any |
| PPR-13 | LOW | Next.js telemetry not disabled | `NEXT_TELEMETRY_DISABLED=1` for the pilot | 1 |
## 12. Changes I require in the pilot plan

1. 3.1: add "PPR-01 closed" as the first entry condition, and note it applies now, not only at Stage 1.
2. 3.1 item 3 and 3.5 item 5: the cap is a **global daily** cap across pools and users plus a monthly ceiling, and a separate **speech minutes** cap; both enforced in the app and tested across a restart.
3. 3.2: correct the interface names; replace "keys stay in a local key store" with the concrete design in 6.2; add the new guard (3.2 of this review) and say the existing refusal tests are replaced, not deleted.
4. 3.2: Stage 1 audio is server-side fixture audio only; the microphone never feeds a real provider in Stages 1 and 2. Add the fixture manifest and synthetic interlock.
5. 3.2: the syllabus extractor stays mock in Stages 1 and 2 unless SBV-19..SBV-22 are fixed first.
6. 3.2 and 4.3: loopback-only web server; separate pilot compose project, database and password; agents never read pilot data.
7. 3.7 and 7.1: the "visible switch" is one-way (real to mock); nothing in the UI can turn a paid provider on.
8. 4.3: list the typed-notes workflow as new build needing contract and security review.
9. 4.2: add B1V-01 fixed, SB-01 flags on the pilot database, SEC-PUBLIC revisited, keys rotated after Stage 1.
10. 4.4 and section 9: the warning must say the PO's machine and agents can technically read his notes, unless the setup is changed.
11. 3.5 item 8: assign the egress check method to devops-engineer.

## 13. Next agents

- **PO:** PPR-01 now; MFA and role check; D1, D3, D4, D5; decide the fate of the auto-created S0 resource.
- **backend-engineer / speech-engineer / ai-agent-engineer:** new guard, caps, fixture-only audio, interlock, B1V-01, adapter log tests.
- **devops-engineer:** secret file and pilot compose project, `.env.example`, gitleaks rule, runbook, egress method.
- **frontend-engineer:** loopback binding, bundle key test, pilot marker.
- **qa-engineer:** restart cap test, planted-phrase canary, pause/stop with real adapters.
- **security-engineer (me):** add PPR rows to the register; review the guard and adapters before the first real call.
