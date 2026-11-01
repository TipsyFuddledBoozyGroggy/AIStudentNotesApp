# Live Lecture AI Pipeline Design (v0.1, DESIGN ONLY — nothing implemented)
> **WARNING (2026-10-27):** vendor prices, limits and terms in this document are superseded where they conflict with `docs/vendors/verification-report.md` (see `docs/RELEASE_GATES.md`, gate G2). Reconcile before implementation.

Status: proposal for Lead Architect review. Model IDs: claude-opus-5-5, claude-sonnet-5-5, claude-haiku-5-5. Verify pricing and limits against the claude-api reference before setting budgets; numbers below are placeholders to be measured.

## 1. Scope and assumptions
- STT is a separate provider. This pipeline consumes only **final** transcript segments `{segmentId, sessionId, startMs, endMs, text, speakerLabel?, confidence?}`. Partial segments are never sent to Claude.
- Open contract questions for Lead Architect: (a) segment event schema owner (speech-engineer/backend); (b) where summary revisions are persisted (database-architect); (c) per-user budget defaults need Product Owner approval; (d) whether embeddings/vector search is justified for course-material retrieval (default: no, use lexical/Postgres FTS first).
- No labeling of student cognitive state. Copy says "catch up", never "you were distracted".

## 2. Workflows (logical, not autonomous agents)
Each is a stateless backend-invoked function: build bounded input, call `IAiClient`, validate, persist with provenance. No loops, no background self-directed agents.

| Workflow | Trigger | Model | Max out tokens |
|---|---|---|---|
| RollingSummaryUpdate | every N final segments or T seconds | haiku-5-5 (escalate to sonnet-5-5 on validation failure or low-confidence STT) | 700 |
| NotesConsolidation | session end / on demand | sonnet-5-5 | 3000 |
| QuickRecapCatchUp | user click | sonnet-5-5 (haiku for 2-min if latency > target) | 600 short + 1200 detail |
| ClarificationSuggestions | piggyback on summary update; user request | haiku-5-5 | 300 |
| TutorQA | user question | sonnet-5-5; opus-5-5 only for explicit "deep explain", budget permitting | 800 |

Opus is never on the live path by default.

## 3. Incremental TL;DR / notes update
State per session (stored separately from raw transcript, with revision numbers):
- `rollingSummary` (<= ~600 tokens): structured topics list + running TL;DR.
- `recentWindow`: last ~3 minutes of final segments (cap ~1500 tokens).
- `openThreads`: current topic, pending definitions/terms.

Update algorithm:
1. Buffer final segments; flush when >= 8 segments OR >= 90 s OR topic-shift heuristic (long pause, "okay next") — whichever first; min interval 45 s; single in-flight update per session (coalesce, never queue unbounded).
2. Input = prior `rollingSummary` + new segments (with ids/timecodes) + a small recent-window overlap for continuity.
3. Output is a **delta**: `{baseRevision, tldr, topicUpdates[], newTerms[], sourceSegmentIds[]}`. Server applies delta to produce revision N+1; stale `baseRevision` is rejected and retried once.
4. Every 10th revision or on session end, run compaction (NotesConsolidation) from summary + transcript chunks (map-reduce over 10-minute chunks) so drift/error accumulation is reset against source.
5. Input hard cap ~4k tokens/call; over-cap triggers oldest-window truncation, logged as a metric.
6. Use prompt caching for the static system prompt + schema (stable prefix first, volatile content last).
7. Client sees `summary.revised` events with revision number; revisions are append-only so UI can show "updated" without losing content.

## 4. Structured outputs and validation
- Use tool-use / structured-output with JSON Schema per workflow, versioned in `docs/ai/schemas/<name>.v1.json`; prompts in `docs/ai/prompts/<name>.v1.md`; version recorded in each ledger row.
- Backend validates: JSON schema, max lengths, enum values, every `citation.segmentId` exists in the session and belongs to the user, timecodes derived server-side from segment ids (model-supplied times are ignored), no URLs/markdown links unless allowlisted.
- On failure: one repair retry with the validation error (no transcript re-echo), then fall back to previous revision and surface "summary delayed".
- Core shared type: `Claim { text, basis: "lecture" | "general_knowledge" | "uncertain", citations: [{kind:"segment", segmentId} | {kind:"doc", docId, chunkId}] }`. `basis=lecture` requires >= 1 valid citation, else downgraded to `uncertain` by validator.

## 5. "Quick recap"
Input: window `W in {2,5,10}` minutes (default 5, or the last used), selected by `endMs - W*60000`, plus rollingSummary for context if W is short. Segment text capped per window (10 min ~ 2.5k tokens typical).

Prompt requirements (v1):
- Plain, calm tone, no judgement about attention.
- Output schema `QuickRecapResult`:
  - `nowDiscussing`: <= 25 words, "what is being discussed right now" (from the last ~60 s).
  - `shortCatchUp`: 2-3 bullets, <= 20 words each, shown by default.
  - `detail`: expandable; ordered `[{timecodeSegmentId, point, whyItMatters?}]`, max 8.
  - `keyTerms`: <= 5 `{term, lectureDefinition?, basis}`.
  - `suggestedClarifyingQuestions`: <= 3, phrased for the student to ask the instructor or tutor.
  - `confidence` and `gaps` (e.g. "audio unclear 12:03-12:40").
- Rule: if transcript is sparse or low-confidence, say so rather than infer.
- Latency target: first visible content < 3 s (stream `nowDiscussing` and `shortCatchUp` first; use streaming with incremental schema parsing or two sequential small calls: short, then detail on expand — preferred for cost since detail is lazy).
- Fallback (no AI): extractive display of the last W minutes of transcript with the last rolling summary, labelled as such.

## 6. Clarification suggestions
Generated from open threads: undefined terms, skipped steps, contradictions with earlier segments, referenced-but-unseen slides. Output `{question, reason, segmentIds[]}` max 3, de-duplicated against previously shown items. Optional and dismissible; quiet mode suppresses proactive display (still generated on demand only).

## 7. Q&A (tutor)
Retrieval: lexical/FTS over this user's session transcript chunks and authorized course documents (authorization enforced in the retrieval query by userId/courseId, never by the model). Top-k chunks (k<=8, ~3k tokens) with ids.
Answer schema: `{answerParts: Claim[], followUps[], limitations}`. Prompt rules:
- Lecture-stated claims cite segment timecodes (rendered `[12:34]`); document claims cite `docId + chunk/page`.
- Anything not in sources is `general_knowledge` and shown with a distinct label ("Not from your lecture"). Unknown/ambiguous → `uncertain` and say so.
- Do not present AI-estimated answers as answer-key certainty.
UI mapping owned by frontend/neurodivergent-ux; this doc defines the data only.

## 8. Prompt-injection and tool defenses
- Transcript text, uploaded docs, and retrieved chunks are **untrusted data**. They are placed in delimited, per-request randomly-tagged blocks (`<untrusted_source id=...>`) in the user turn; system prompt states these never contain instructions and must not alter output format, tools, or behavior.
- Pre-model: strip control chars, cap sizes, redact obvious PII/secrets (emails, phone numbers, tokens patterns) where not needed; flag injection-like strings for telemetry only (not as a sole defense).
- Model has **no tools** for the live workflows (pure structured generation). Any future tool is: allowlisted name, JSON-schema args, server-side authz by authenticated user (never model-supplied userId), read-only unless user-confirmed in UI, idempotent, rate-limited, audited.
- No OAuth tokens, API keys, other users' ids, or Google client in model context or prompts. Calendar/Docs writes occur only through backend endpoints triggered by explicit user consent; model may at most propose a draft payload that the backend validates and the student approves.
- Output handling: render as text/safe markdown, no HTML, no auto-fetched URLs/images, citations resolved server-side.
- Logs: no raw transcript/prompt logging by default; store ids, token counts, hashes, schema versions. A debug-capture flag requires explicit opt-in and a TTL.

## 9. IAiClient abstraction (design)
Provider-neutral interface in the backend domain layer (C#):
- `Task<AiResult<TOut>> CompleteAsync<TOut>(AiRequest<TIn> req, CancellationToken ct)` where request carries: `workflow`, `promptVersion`, `modelTier` (Fast/Balanced/Deep, mapped to model ids in config), `system`, `untrustedBlocks[]`, `outputSchema`, `maxOutputTokens`, `userId`, `sessionId`, `idempotencyKey`, `deadline`.
- `IAsyncEnumerable<AiChunk>` streaming variant for QuickRecap/Q&A.
- `AiResult` includes parsed value, usage (input/cached/output tokens), model, latency, stop reason, `Degraded` flag.
- Implementations: `AnthropicAiClient` (key from managed secrets; retries with jittered backoff on 429/5xx/overloaded honoring retry-after; timeout per workflow), `MockAiClient`.
- `MockAiClient` (dev/tests, default when no key configured so no silent spend): deterministic, seeded by hash of (workflow, promptVersion, input); returns schema-valid canned/extractive output (e.g., first sentence per segment cluster, ids echoed); supports fault injection (timeout, 429, malformed JSON, schema-violating citations, injection-echo) for tests. Clearly labelled `mock` in responses and UI dev banner.
- A decorator chain: Budget → Ledger → Validation/Repair → Retry/CircuitBreaker → provider.

## 10. Budgets and usage ledger
- Ledger table (owner: database-architect): `ai_usage(id, userId, sessionId, workflow, promptVersion, model, inputTokens, cachedTokens, outputTokens, estCostMicros, latencyMs, outcome[ok|repaired|invalid|degraded|error|budget_denied], createdAt)`. No content stored.
- Budgets (config, Product Owner approved): per-session soft cap, per-user daily cap, global monthly cap with alerts at 50/80/100%. Pre-call estimate from input size; check before call.
- Priority under pressure: user-initiated (Quick recap, Q&A) > rolling summary > clarification suggestions > consolidation. Degrade order: lengthen update interval → drop clarifications → Haiku-only → extractive fallback. Show user a neutral message with remaining-budget copy; never silently upgrade to Opus.
- Illustrative sizing to validate by measurement: a 60-min lecture at ~40 summary updates x ~3k in / 500 out on Haiku, plus a few Sonnet catch-ups. Record real figures in eval runs.

## 11. Graceful degradation
- Circuit breaker opens after repeated failures; transcript capture and storage are independent of AI and continue.
- States emitted to client: `ai.available | ai.delayed | ai.unavailable | ai.budget_limited`.
- Unavailable: show live transcript, last good summary (timestamped "as of 12:40"), extractive recent-window view for Quick recap, "AI summary will catch up when available". On recovery, run a catch-up summary update over the unsummarized backlog via chunked map-reduce (bounded batches), not one huge call.
- Idempotency key per (session, baseRevision) prevents duplicate revisions/charges on retries.

## 12. Eval plan (docs/ai/evals/, fixtures in tests/ai-evals/)
Fixtures (synthetic or consented/redacted only; never real student recordings): JSONL transcripts with timecodes and gold annotations.
- Types: clean lecture; noisy STT (misheard terms, fillers, dropped words); tangents/announcements; multiple speakers/Q&A from class; ambiguous/overloaded concepts; very long (90 min); sparse/silent; wrong-then-corrected statements.
- Metrics and gates (thresholds TBD from baseline):
  - Summarization: key-point recall vs gold (rubric, LLM-judge calibrated with human spot-check), no-fabrication rate, drift after N incremental revisions vs one-shot summary of full transcript.
  - Grounding: citation validity (id exists), citation support (cited segment entails claim), `basis` label accuracy, hallucination rate on questions whose answer is absent (must answer uncertain/not-in-lecture).
  - Quick recap: nowDiscussing correctness vs last 60 s, window adherence (no content outside W), length/format compliance, brevity of default view.
  - Uncertainty: abstains/flags on garbled audio sections.
  - Injection resistance: transcripts/docs containing "ignore previous instructions", format-hijack, fake system tags, requests to reveal prompts/tokens, requests to call tools, exfil links, and lecturer-legitimate imperative phrases ("write this down"). Pass = output schema unchanged, no instruction followed, no URL emitted, no secrets (canary strings in system prompt never appear).
  - Robustness: schema-valid rate, repair rate, p50/p95 latency, cost per lecture-hour.
- Execution: deterministic mock runs in CI (schema, validator, degradation, fault injection); real-provider evals run manually/scheduled behind explicit budget flag, results archived with model id, prompt version, cost. Prompt or model changes require eval comparison before merge.

## 13. Dependencies and handoffs
backend-engineer (IAiClient, queueing, events), database-architect (ledger, revisions), speech-engineer (final-segment contract), security-engineer (injection/redaction/log review), qa-engineer (eval CI gate), neurodivergent-ux-specialist (copy, labels), devops-engineer (secrets, cost alerts), learning-science (clarification quality).

## 14. Not done
No prompts, schemas, code, measurements or provider calls exist yet; all figures are untested estimates.
