# API Contracts — Milestone 2 (live lecture slice)

**Status:** FROZEN v1 for M2 mock-backed work as of 2026-10-27 (security review C-01..C-32 folded in at §8 and §10; numeric limits are proposals; wording and legal items remain open under G1), authored by Lead Architect 2026-10-27. Nothing here is implemented. It reconciles `architecture/backend-foundation.md` §6-7 and `architecture/speech-pipeline.md` §4-6, which disagreed. Where they conflict, this file wins. Vendor facts are out of scope (see `vendors/verification-report.md`, gate G2).

Freeze rule: additive changes only within `v1`. Any breaking change needs a new version and an ADR. Schemas will live in `src/shared/contracts/` and be contract-tested by both stacks before parallel frontend/backend work starts.

## 1. Channels

| Channel | Purpose | Auth |
|---|---|---|
| REST `/api/v1` | commands, queries, catch-up fallback | session cookie + Origin check (existing) |
| SignalR `/hubs/lecture`, protocol `lecture.v1` | downstream only: state, transcript, summary, catch-up events | session cookie; Origin check on negotiate and upgrade (**F-04, ADR-017 gate**) |
| WebSocket `/ingest/sessions/{id}/audio`, protocol `speech.v1` | upstream audio only | single-use ticket + Origin check + session ownership + `Active` state |

Resolved conflicts:
- **Audio is server-relayed** (browser -> our host -> STT adapter). Provider credentials never reach the browser (invariant 10). Direct-to-vendor is not offered.
- **Transcript events travel on the SignalR hub only**, not on the audio socket. The audio socket carries only `ack`, `error` and `gap` control frames back.
- **The audio ticket** comes from `POST /sessions/{id}/audio-ticket` (200 `{ticket, expiresAt}`, 30 s, single use, bound to user, session and Origin). It is sent in the first WS text message, never in a URL.
- Event name is `catchup.ready` (lowercase), field name is `revision` everywhere (not `rev`).

## 2. REST (M2 additions)

Errors, `Idempotency-Key`, ETag and `If-Match` follow `backend-foundation.md` §5. Every endpoint is user-scoped; a foreign or missing id is `404 not_found`.

| Method | Path | Request | Success | Errors |
|---|---|---|---|---|
| POST | `/courses/{courseId}/sessions` | `{title?, consent:{acknowledged:true, textVersion}}` | 201 `Session` + Location | 403 `consent_required`, 404, 409 `invalid_state` (another Active/Paused session) |
| GET | `/sessions/{id}` | | 200 `Session` | 404 |
| GET | `/courses/{courseId}/sessions?cursor&limit` | | 200 page of `Session` | 404 |
| POST | `/sessions/{id}/pause` · `/resume` · `/stop` | | 200 `Session` (idempotent when already in target state) | 404, 409 |
| DELETE | `/sessions/{id}` | | 204; deletes transcript and all derived data | 404 |
| POST | `/sessions/{id}/audio-ticket` | | 200 `{ticket, expiresAt}` | 403 `consent_required`, 409 unless `Active` |
| GET | `/sessions/{id}/transcript?afterSeq&limit` | | 200 `{events: TranscriptFinal[], hasMore}` | 404 |
| GET | `/sessions/{id}/summary?revision` | | 200 `SummaryRevision` | 404 if none yet |
| POST | `/sessions/{id}/lost-track` | `{windowMinutes: 2\|5\|10}` | 200 `CatchUp{kind:"Extractive"}` immediately; AI version follows as `catchup.ready` | 400, 404, 409 unless Active/Paused, 429 |
| GET | `/sessions/{id}/catch-ups/{catchUpId}` | | 200 `CatchUp` (poll fallback) | 404 |
| GET | `/sessions/{id}/notes` | | 200 `Notes` (after Stopped) | 404, 409 until generated |
| POST | `/sessions/{id}/export/doc` | `{}` + `Idempotency-Key` | 202 `{exportId}`; M2 uses a mock exporter, real Google is M3 | 409, 404 |

`Session`: `{id, courseId, title, state: "Active"|"Paused"|"Stopped"|"Failed", startedAt, stoppedAt?, lastSeq, consent:{textVersion, acknowledgedAt}, etag}`.
State machine: `Active <-> Paused -> Stopped`; `Failed` only by server (abandoned, 10 min no heartbeat). Paused accepts no audio and produces no transcript. The consent record (ADR-015) is written in the same transaction as the session.

## 3. Hub `lecture.v1`

Client -> server: `Join {sessionId, protocol:"lecture.v1", lastSeq?, lastSummaryRevision?}` returning `{state, currentSeq, currentSummaryRevision, replay:{fromSeq, toSeq, truncated}}`; `Leave {sessionId}`; `Ping`. Join of a foreign session fails identically to a missing one. Clients never send transcript content.

Envelope on every server event: `{v:1, type, sessionId, seq|null, ts}`.

**`seq`** is a per-session int64 assigned in the same transaction that persists the event. It is **gap-free for persisted events** (`session.state`, `transcript.final`, `transcript.correction`, `gap`). Ephemeral events (`transcript.partial`, `summary.*`, `stt.status`, `catchup.ready`, `error`) carry `seq: null`. Clients must not invent seq and dedupe on `(sessionId, seq)`.

| Event | Payload | Notes |
|---|---|---|
| `session.state` | `{state, reason?}` | source of truth for the visible recording indicator |
| `transcript.partial` | `{segmentId, revision, startMs, text, speaker?}` | replaces the earlier revision of the same segment; only the latest is replayed on join |
| `transcript.final` | `{segmentId, startMs, endMs, text, confidence?, speaker?, provider}` | immutable, persisted. `segmentId` is server-assigned `seg_{n}` |
| `transcript.correction` | `{segmentId, revision, text, supersedes:revision}` | keeps the original final and its provenance |
| `gap` | `{fromMs, toMs, reason: "paused"\|"network"\|"device"\|"provider"}` | notes show "audio unavailable", never fabricated text |
| `summary.revision` | `{revision, basedOnSeq, tldr, bullets[], coveredFromMs, coveredToMs, status: "Draft"\|"Final", citations:[{segmentId, startMs}]}` | monotonic; client ignores `revision <= current` |
| `summary.status` | `{state: "Pending"\|"Running"\|"Delayed"\|"Failed", lagSeconds}` | transcript is never blocked by it |
| `catchup.ready` | `{catchUpId, windowMinutes, text, citations[], kind: "Extractive"\|"Ai"}` | |
| `stt.status` | `{state: "Ok"\|"Degraded"\|"Down"}` | |
| `error` | `{code, detail}` | non-fatal; `code` from the shared ProblemDetails table |

Timecodes: `startMs`/`endMs` are offsets from session start **excluding paused time**, derived from captured sample counts, not wall clock. Only finals are persisted, summarized and cited. Reconnect: client backs off 0/2/5/10/30 s with jitter and calls `Join` with `lastSeq`; server replays finals with `seq > lastSeq` (cap 5000, else `truncated:true` and the client pages `GET /transcript`). Accessibility rule for the client: never announce partials to assistive tech; announce finals throttled.

## 4. Audio socket `speech.v1`

1. Client opens the socket, then sends JSON `hello {version:"speech.v1", ticket, sampleRate:16000, encoding:"pcm_s16le", channels:1, resumeFromFrame?}`. Server closes with code 4401 on a bad ticket, 4403 on a bad Origin, 4409 if the session is not Active.
2. Binary frames: 12-byte header (uint32 `frameSeq`, uint64 `captureOffsetMs`) plus PCM. `frameSeq` is the audio frame counter, unrelated to event `seq`.
3. Client control JSON: `pause`, `resume`, `stop` (mirror the REST state; REST is authoritative).
4. Server JSON: `ack {frameSeq}`, `gap {fromMs,toMs,reason}`, `error {code}`.
5. Client ring buffer 30 s, bounded; overflow drops oldest and reports a `gap`. Server keeps a bounded per-session channel (~5 s) and applies backpressure, then a `gap`. After ~60 s without a link the client auto-pauses and shows the offline state.
6. No raw audio is persisted. Idle timeout and hard session cap (default 3 h) are configuration, owned by devops for cost control.

## 5. STT and AI provider seams (server internal, not wire)

`ISpeechToTextProvider`/`ISttStream` as sketched in `speech-pipeline.md` §5. `IAiSummarizer` (incremental: previous summary + new finals -> `SummaryRevision`, schema-validated, one repair retry) and `ILostTrackRecapper` (extractive fallback always available). **M2 ships only `ReplaySttProvider` (fixture-driven, deterministic, fault injection) and `MockAi`.** A real adapter requires Product Owner authorization of PO-2/3/4 and gates G1 and G2; nothing in this contract authorizes real classroom audio.

## 6. Contract tests required before parallel work

- JSON Schema for every event and REST DTO in `src/shared/contracts/`; both stacks validate against it.
- Hub: foreign-session join denied; replay with gap; `revision` monotonicity; slow summarizer does not delay `transcript.final`.
- Audio socket: bad/replayed ticket rejected; wrong Origin rejected (F-04); non-Active rejected.
- Log canary: a sentinel transcript string never appears in logs (F-08).

## 7. Open items for the Product Owner
- Consent text and `textVersion` content (G1, decisions D1/D2/D8/D10); M2 uses clearly labeled DRAFT text.
- ~~Confirm server-relayed audio~~ CONFIRMED by PO 2026-10-27.
- Session auto-stop timing and hard cap defaults (10 min heartbeat, 3 h).
- Pause wording: PO has no preference; draft wording is used until counsel review (G1).

## 8. Security requirements folded in from review (docs/security/m2-contract-review.md)
Normative for implementation. IDs refer to that review. Medium/low items (C-03..C-05, C-08..C-12, C-15..C-20, C-22, C-24, C-26..C-32) remain open in the review file and must be triaged before freeze.

- **Audio ticket (C-01, C-02):** 256-bit CSPRNG value, stored hashed, consumed atomically on `hello`. At consume time re-check session state, ownership, Origin and the session cookie; cookie user must equal ticket user. One outstanding ticket per session. Pause, stop, delete and logout invalidate it. No CORS allow headers on the ticket endpoint. Every failure returns the same response.
- **Hub connection lifetime (C-06):** bind each connection to its cookie session id and re-validate it every 60 s. Maximum connection lifetime 1 h. Session delete or revocation drops the connection.
- **Hub CSRF/authz (C-07, F-04):** WebSockets transport only. Exact ordinal Origin match on negotiate and upgrade; a missing or `null` Origin is rejected with HTTP 403 before the upgrade. `[Authorize]` on the hub class and per-call session ownership resolution. Group names are built server-side only.
- **Consent (C-13):** `textVersion` is validated against a server registry. Per-course attestation (SEC-008). A consent event is stored for every start, pause, resume and stop (SEC-007). Startup guard: no real STT provider may be enabled unless G1 is recorded `PASS` in the gate register by a human. M2 stays on `ReplaySttProvider`.
- **Pause/stop cutoff (C-14):** on pause or stop, discard unsent server-channel frames atomically, ignore frames past the cutoff offset, close the vendor stream, and never replay the client ring buffer on resume. Student-facing wording about audio already sent needs PO and counsel review (G1).
- **Deletion (C-21):** `DELETE /sessions/{id}` removes transcript, summaries, catch-ups, notes, export records and pending jobs. Writers must check the session still exists, so a late job cannot recreate data. Deleting an Active session stops it first. Only content-free consent metadata is kept. The UI must disclose that vendor and Google copies are not deleted by this call.
- **Log canary (C-23):** a required merge check at Trace level across all sinks, with a sentinel in fixtures, exception messages and request bodies, plus fault injection. It also asserts tickets and cookies never appear. `error.detail` comes from a fixed vocabulary, never exception text.
- **Ingest limits (C-25):** max frame size, byte-rate cap (~1.1x real time), per-user socket cap, per-user STT minutes budget and a global kill switch. Defaults are configuration, values pending PO budget (PO-2/3/4).

## 9. Pause UX (PO direction 2026-10-27)
When a session is Paused the UI shows a clear paused state stating that recording is paused, with a Resume button (and Stop). The recording indicator is driven by `session.state` and the local mic track, never by the button alone. Wording is DRAFT; final text, including any note about audio sent just before the pause, remains a G1 item for the Product Owner and counsel. Detailed layout (persistent panel vs dialog, focus handling, screen-reader announcement) is delegated to `adhd-ux-specialist`.

## 10. Remaining review findings accepted (PO: "go with the defaults", 2026-10-27)
Numeric values are the reviewer's **proposals**, configurable, not measured. Maximums are fixed here; devops owns the configuration.

- **Origin (C-03, C-08, C-29):** checked before upgrade on all three channels; bad or missing Origin gets HTTP 403, exact ordinal match against one allowlist. Session cookie `SameSite=Strict`. CSP `connect-src` lists only the configured API/ingest origins; `Permissions-Policy: microphone=(self)`. No provider endpoint or key is reachable from the browser (a test greps the built bundle). Post-upgrade failures use 4401/4409.
- **Socket pre-auth (C-04, C-05):** `hello` within 5 s and at most 1 KB, else close; binary before `hello` closes; text max 4 KB; binary frame max 250 ms of PCM + 12-byte header; oversize closes 1009. Ticket issuance limited to 6/min per user and per session; failed `hello` counts per user and IP; bad, expired, used and foreign tickets give identical 4401.
- **Scoping (C-09, C-12, C-18, C-19):** every event read is keyed by `(userId, sessionId)`, never by `seq` alone; out-of-range `lastSeq` returns current state with empty replay; two-user test with overlapping `seq` and `segmentId`. Citations are validated against the same session and dropped if invalid; `segmentId` is meaningful only with `sessionId`. One recording per user via unique partial index on `(user_id) WHERE state IN ('Active','Paused')`; second socket for a session gets 4409. Server derives timecodes from accepted sample counts; `captureOffsetMs` is advisory.
- **seq (C-10, C-27):** per-session counter row updated in the persisting transaction (not a DB sequence), with a max event rate; `seq` is serialized as a string. Replay cap lowered to **500** with `truncated:true`; `Join` limited to 10/min per session.
- **Partials (C-11):** held per `(userId, sessionId)`, cleared on pause, stop and delete, never logged or persisted.
- **Control path (C-15):** the audio socket accepts only `pause` and `stop`. `resume` is REST only and needs a new ticket. REST wins on mismatch and the socket is closed.
- **Indicator (C-16):** computed from server state **and** local `MediaStreamTrack` state. Pause/stop stops the track before showing Paused/Stopped. Disagreement shows a prominent mismatch warning and stops capture. Hub disconnect shows "not connected, audio not being saved". Announced via a live region.
- **Unattended limits (C-17, proposals):** heartbeat = hub `Ping` and audio frame arrival. Active with no frames for 60 s emits a `gap`; after 5 min auto-pause. Paused auto-stops after 30 min. Hard cap 3 h of total elapsed time including paused.
- **Speaker (C-20):** `speaker` is off by default in M2 (never emitted), is on the G1 legal checklist, and no cross-session voice identification.
- **Errors (C-24):** `detail` comes from a fixed server vocabulary keyed by `code`; never exception text, input echo or model output.
- **Abuse limits (C-26):** hub `MaximumReceiveMessageSize` 4 KB; 3 concurrent hub connections per user; `lost-track` 6/min per session with a per-session AI call budget (extractive always returned); maximum `limit` on every list.
- **AI output is untrusted plain text (C-28):** `text`, `tldr`, `bullets[]` are rendered as text nodes; no markdown, HTML or links. Summarizer inputs are delimited untrusted data; output is schema-validated with one repair retry; no tool access. Red-team fixtures (injection speech, markdown image URL, script payload) must render inert.
- **Audit (C-30):** each lifecycle transition, delete, export and repeated ticket failure writes an audit event (user id, session id, action, timestamp only).
- **Idempotency (C-32):** `Idempotency-Key` on session creation; a replay returns the original session without a second consent event.
- **Mock exporter (C-22):** writes nothing outside the database; exports are owner-scoped on read.
- **Later change of audio path (C-31):** a direct-to-vendor design is a new contract version and needs a new security review.
- **Tests (§6 additions):** two-user overlap test, Origin 403 on all channels, bundle grep for provider endpoints/keys, red-team rendering fixtures, replay cap, pause/stop cutoff, ticket replay.
