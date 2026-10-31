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
| POST | `/sessions/{id}/quick-recap` | `{windowMinutes: 2\|5\|10}` | 200 `CatchUp{kind:"Extractive"}` immediately; AI version follows as `catchup.ready` | 400, 404, 409 unless Active/Paused, 429 |
| GET | `/sessions/{id}/catch-ups/{catchUpId}` | | 200 `CatchUp` (poll fallback) | 404 |
| POST | `/sessions/{id}/notes` | `{}` + `Idempotency-Key` (required) | 202 `Notes` (new press) or 200 `Notes` (shared or replayed); see section 17 | 400, 401, 403 `csrf_failed`, 404, 409 unless `Stopped`, 415, 429 |
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

`ISpeechToTextProvider`/`ISttStream` as sketched in `speech-pipeline.md` §5. `IAiSummarizer` (incremental: previous summary + new finals -> `SummaryRevision`, schema-validated, one repair retry) and `IQuickRecapRecapper` (extractive fallback always available). **M2 ships only `ReplaySttProvider` (fixture-driven, deterministic, fault injection) and `MockAi`.** A real adapter requires Product Owner authorization of PO-2/3/4 and gates G1 and G2; nothing in this contract authorizes real classroom audio.

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
- **Abuse limits (C-26):** hub `MaximumReceiveMessageSize` 4 KB; 3 concurrent hub connections per user; `quick-recap` 6/min per session with a per-session AI call budget (extractive always returned); maximum `limit` on every list.
- **AI output is untrusted plain text (C-28):** `text`, `tldr`, `bullets[]` are rendered as text nodes; no markdown, HTML or links. Summarizer inputs are delimited untrusted data; output is schema-validated with one repair retry; no tool access. Red-team fixtures (injection speech, markdown image URL, script payload) must render inert.
- **Audit (C-30):** each lifecycle transition, delete, export and repeated ticket failure writes an audit event (user id, session id, action, timestamp only).
- **Idempotency (C-32):** `Idempotency-Key` on session creation; a replay returns the original session without a second consent event.
- **Mock exporter (C-22):** writes nothing outside the database; exports are owner-scoped on read.
- **Later change of audio path (C-31):** a direct-to-vendor design is a new contract version and needs a new security review.
- **Tests (§6 additions):** two-user overlap test, Origin 403 on all channels, bundle grep for provider endpoints/keys, red-team rendering fixtures, replay cap, pause/stop cutoff, ticket replay.

## 11. Wire-format clarifications (Lead Architect, 2026-10-27)
Resolved from the schema pass (`src/shared/contracts/README.md` lists all 13 items). The schemas under `src/shared/contracts/lecture.v1/` are now the machine-readable source of truth where this prose is silent.
1. Hub events nest their payload: `{v, type, sessionId, seq, ts, payload:{...}}`.
2. Audio-socket frames use `type` as discriminator (`hello`, `ack`, `gap`, `error`, `pause`, `stop`).
3. All seq-like values are decimal strings (`lastSeq`, `currentSeq`, `replay.fromSeq/toSeq`, `basedOnSeq`, `Session.lastSeq`); this overrides `long` in §3 `Join`.
4. All objects are strict (`additionalProperties:false`); additive v1 changes update the schema first. `speaker` stays optional in schema but the M2 server never emits it (C-20).
5. `supersedes < revision`, `toMs >= fromMs` and `endMs >= startMs` cannot be expressed in the schema and need server-side tests. Length limits in the schemas (text 4000, tldr 2000, bullets 20x500, citations 50) are proposals.
6. `error.detail` is the constant server title for its code. Backend must define fixed strings for `consent_required`, `invalid_state` and `provider_unavailable`.
Items 4-13 of the README (id format, nullable fields, free-string `reason`) are accepted as written and remain editable until the first consumer ships.

## 12. Decisions from the M2 UX pass (Lead Architect, 2026-10-27)
1. `session.state.reason` vocabulary (v1, additive): `user`, `auto_pause_no_audio`, `auto_stop_paused_timeout`, `hard_cap`, `abandoned`. Clients treat unknown values as "ended by the system".
2. The replay provider is visible: `Session` responses and the status bar carry a "Demo source" indication while `ReplaySttProvider`/`MockAi` is active, so mock transcripts can never be mistaken for a real recording. (Schema field to be added to `session.json` by backend in B2; until then the web app derives it from configuration.)
3. No summary-retry endpoint in M2. The Failed state keeps the last good revision and shows no Try again button.
4. Typed-notes fallback after mic denial is out of M2 scope; the denied state offers instructions and a way back only.
5. The `Alt+L`, `Alt+P`, `Alt+J` shortcuts are unverified against screen-reader and browser conflicts: ship them off by default until device testing (G3) says otherwise.

## 13. Decisions from the F1 client pass (Lead Architect, 2026-10-27)
1. `GET /sessions/{id}/transcript?afterSeq&limit` returns **all persisted events** after `afterSeq` (`transcript.final`, `transcript.correction`, `gap`, `session.state`) in seq order as `{events, hasMore}`, so truncated-replay paging cannot lose corrections or gaps. Default `limit` 200, server maximum 500. (Supersedes "finalized segments only" in §2; the schema for the response must be added by backend in B2.)
2. The extractive and AI versions of one catch-up share one `catchUpId`. An AI version may replace the extractive one; the reverse never happens.
3. Non-retryable join errors: `not_found`, `unauthenticated`, `csrf_failed`, `consent_required`. All others retry with backoff.
4. Server `Failed` is shown as Stopped with a "ended by the system" explanation.
5. Disconnected with a live mic: Mismatch (stop capture) if the last known server state is not Active, otherwise NotConnected. Jitter is +/-20%.
6. `seq` starts at 1; a value of `0` is invalid (backend to enforce, schema to be tightened to a positive decimal string).
7. Shared red-team fixtures move to `src/shared/contracts/fixtures/redteam/` so both stacks use identical strings (open task).
8. Schemas keep `minLength` beside `$ref`; web keeps Ajv `strictTypes: false`.

## 14. Decisions from the B1 backend pass (Lead Architect, 2026-10-27)
Accepted as implemented: (1) session start consumes `seq` 1 and every `session.state` change takes the next seq, so the Transcript module must share the per-session counter row in B2; (2) one consent record per session start with `course_attested=true`, reusable per-course attestation deferred; (3) invalid or missing consent is 403 `consent_required` (checked after course ownership, so probes learn nothing); (4) idempotent replay returns 201 with the session's current state and an `Idempotency-Replayed: true` header, key reuse with a different body is 400 `validation_failed`, keys expire after 24 h; (5) lists are `{items, nextCursor}`; (6) audio-ticket 403 only if the recorded consent text version is no longer registered; (7) placeholder routes (transcript empty 200, summary/catch-ups 404, notes 409 `invalid_state`); (8) ticket rate limit keyed per user. Open for B2: ticket consume on an authenticated socket with Origin check, per-event-rate cap on seq, Failed/abandonment/auto-stop jobs, closing live connections on delete (ADR-019 covers data cleanup).

## 15. Implementation notes from the B2 backend pass (backend-engineer, 2026-10-28)
Mock-backed only. Everything below was implemented and tested against `ReplaySttProvider`; no vendor code or key exists, and nothing here clears G1, G2 or any other gate. Items marked **DEVIATION** or **DECISION NEEDED** are for the Lead Architect.

**Transport and wire details the contract left open**
1. Hub server events are delivered as one client method named `event` whose single argument is the section 11.1 envelope. Hub methods: `Join(request)`, `Leave({sessionId})`, `Ping()`. Failures are `HubException` whose message is only an error code (`not_found`, `validation_failed`, `rate_limited`, `unauthenticated`, `internal_error`). A foreign session fails identically to a missing one.
2. `Join` parsing is strict (unknown field, wrong type, a `lastSeq` that is numeric, signed or has leading zeros, or a `protocol` other than `lecture.v1` all give `validation_failed`). Absent `lastSeq` means 0. Order is: subscribe to the group, then read the backlog, so the client must dedupe on `(sessionId, seq)`. If more than `Realtime:ReplayCap` (default 500) events are pending, `replay.truncated=true` and **nothing** is replayed (the client pages `GET /transcript`); a `lastSeq` greater than the current seq gives an empty replay. The latest partial per segment (seq null) is replayed after the backlog. `stt.status` is ephemeral and is not replayed. `currentSummaryRevision` is always `null` until the AI increment.
3. WebSockets transport only, `MaximumReceiveMessageSize` 4 KB, 3 concurrent connections per user (the newest is aborted), `Join` limited to 10/min per (user, session), maximum connection age 1 h, re-validation every `Realtime:RevalidateSeconds` (60): login session still valid, age, and every joined session still exists for its owner. The last check is how a course purge (FK cascade, no event) closes connections.
4. Origin: one middleware covers `/hubs/*` and `/ingest/*` on every method. A missing, `null`, repeated or non-exact (case or trailing slash) Origin gives HTTP 403 `csrf_failed` before any upgrade or auth work.
5. Audio frame header integers are **little-endian** (the contract does not say; the web client must match). `frameSeq` is only echoed in `ack`; `captureOffsetMs` is ignored. Timecodes come from accepted sample counts, continue across pause/resume (`transcript.audio_progress`), and exclude paused time.
6. Server text frames: `ack {frameSeq}` for every accepted frame; `error {code}` with `rate_limited` or `provider_unavailable`. Frames beyond about 1.1x real time are dropped, not queued (burst budget `Realtime:RateBurstSeconds`, default 5); more than 50 strikes closes 1008.
7. Close codes: **4401** for every ticket failure and when the login session is lost mid-stream; **4409** for a second socket on the same session and for the server-side cutoff on pause, stop, delete, course purge or a session that is no longer Active; 1007 malformed hello/frame, 1008 policy (hello timeout, binary before hello, `resume` or unknown control, rate abuse), 1009 oversize, 1000 after a client `pause`/`stop` control, 1011 internal failure.
8. **DEVIATION from section 4.1:** a ticket for a session that is not Active closes with 4401, not 4409, because C-01 requires every ticket failure to be indistinguishable. 4409 is used only after the ticket was accepted.
9. A client `pause`/`stop` control frame only ends that stream (cutoff plus close 1000). It does not change the session; REST stays authoritative (C-15).
10. The pause/stop/delete cutoff (C-14) is enforced twice: the relay discards queued audio, ignores later frames and closes the provider stream, **and** every persisted write allocates its seq in a transaction that re-checks the session is Active, so a late write is rejected by the database even if the relay lags.

**Persistence and seq**
11. `transcript.transcript_events` (Final, Correction, Gap) and `transcript.audio_progress` are the source store. The seq is taken from `lectures.session_counters` in the same transaction as the insert (`ISessionSequencer`), so seq is gap-free across `session.state` and transcript events. Corrections use revision = previous + 1 with `supersedes` = previous (the original final is revision 1). Segment ids are `seg_{n}` and are not reused across resumes.
12. `GET /sessions/{id}/transcript` merges both sources by seq, default limit 200, maximum 500, `afterSeq` a non-negative integer (400 otherwise), 404 for foreign or missing. Schema `transcript-page.schema.json` was added.
13. Event rate cap `Realtime:EventsPerSecond` (default 50) per session: excess persisted events are not silently lost, they are reported as one `gap` (reason `provider`). A 60 s silence (`Realtime:SilenceGapSeconds`) writes one `gap` (reason `network`) with `fromMs == toMs`, because no duration is known.
14. `session.state.reason` is now `user` for student commands (section 12.1 vocabulary); the Start event has none. System transitions use `auto_pause_no_audio`, `auto_stop_paused_timeout`, `hard_cap`.

**Demo source (section 12.2)**
15. `Session` responses now include `demoSource: boolean`, true while `ReplaySttProvider` is active (always, in M2). `session.schema.json` gained an optional `demoSource` (additive). That edit, the new `transcript-page.schema.json` and three valid and three invalid fixtures are in `src/shared/contracts`, outside the stated src/api scope, because the schemas are `additionalProperties:false` and the contract assigned both to B2. Every final carries `provider: "replay-demo"`. Startup refuses any `Speech:Provider` other than `replay`.

**B1 deferrals closed**
16. Ticket-reject audits: one row per user, per session that exists for that user (or one for "no such session"), per minute. A failed `hello` counts per user and IP (`Realtime:FailedHelloPerMinute`, default 10) and a blocked user gets 429 before the upgrade. Upgrade limiter 10/min per user. The expired-ticket and idempotency sweeper plus indexes already shipped in M006.
17. Transition caps: 12 pause/resume per minute per session and 200 events per session (`Lectures:MaxTransitionsPerMinute`, `Lectures:MaxTransitionsPerSession`) give 429 `rate_limited`. Stop and system transitions are never blocked.
18. Unattended-session job (`SessionLifecycleJob`, hosted, `Lectures:LifecycleJobSeconds` default 30): auto-pause after 300 s with no hub `Ping` or audio frame, auto-stop after 1800 s paused, hard cap 10800 s since start. **These are the contract defaults and remain UNCONFIRMED Product Owner decisions** (section 7). **DECISION NEEDED:** the "Failed / abandoned (10 min no heartbeat)" transition in section 2 conflicts with the 5 min auto-pause and 30 min paused auto-stop (an Active session can never be silent for 10 min), so it was not implemented.
19. Closure: delete closes the audio socket (4409) and aborts hub connections that joined it; pause/stop close the socket; login-session loss closes both within their check intervals (hub 60 s default, socket `Realtime:RelayCheckSeconds` default 5 s). A course purge is detected by those periodic checks, not pushed.
20. The cross-schema FK allow-list is now five entries (two new, `ON DELETE CASCADE`, composite `(session_id, user_id)`): `fk_transcript_events_session_user` and `fk_audio_progress_session_user` to `lectures.lecture_sessions`. Proposed as ADR-020; see `docs/architecture/m2-b2-realtime.md`. Migrate Transcript after Lectures.

**Deferred or not done**
- The `Failed`/abandoned state (item 18). Gap reasons `device` and `paused` are never emitted by the server (the client reports device loss through pause).
- Multi-instance deployment: hub groups, partial store, relay registry, activity tracker and the limiters are in-process. Scaling out needs a backplane and shared state (devops/architecture decision).
- The in-memory limiter dictionaries (`JoinLimiter`, `FailedHelloLimiter`) are not pruned by key; growth is bounded by the distinct users and sessions seen, acceptable for M2.
- Real STT, Claude, Google and the frontend are out of scope.

## 16. Changes after the B2 security review (backend-engineer, 2026-10-28)
The frozen v1 wire contract is unchanged. No schema file changed. Item 1 replaces section 15 item 17.

1. **Supersedes section 15 item 17 (B2-01, ADR-019b).** Pause and Stop are never rate limited, capped or refused. The 12-per-minute and 200-per-session caps apply to user **Resume** only; a refused Resume is 429 `rate_limited` and leaves the session as it was.
2. **Logout pushes (B2-03).** `POST /auth/logout`, and a new login that replaces the cookie's old login session, close that login session's audio socket (4401) and abort its hub connections immediately (tests assert under 1 s with the polling intervals set to one hour), so no audio is accepted and no Join or replay is served after sign-out. The polling in section 15 item 19 stays as a safety net for revocations that bypass Identity.
3. **No audio after an acknowledged pause (B2-04).** After the ticket is consumed and the relay is registered, the relay re-reads the session state before it opens the provider stream. A pause that committed in between closes the socket with 4409 and no frame is acked.
4. **Fast reconnect (B2-05).** The relay saves its audio position and leaves the registry before it sends a close or drains its workers, so a client that reconnects right after a close is not refused with 4409 for "already has an audio socket".
5. **Archive and purge (B2-06, B2-07, ADR-020b).** `DELETE /courses/{id}` (archive) stops a live session of that course. `?purge=true` stops it, then deletes. In both cases the Stop consent event is written, `session.state` carries the new reasons `course_archived` or `course_deleted` (additive to section 12.1; the schema allows any string up to 100 characters and clients already treat unknown reasons as "ended by the system"), sockets close by push, and purge audits `session.delete`.
6. **Hub limits (B2-02).** Every hub invocation counts against `Realtime:HubInvocationsPerMinute` per connection (default 120) before the method runs. `Join` also counts per user across all session ids, `Realtime:JoinPermitPerUserPerMinute` (default 30), on top of the per (user, session) limit of section 15 item 3. The excess gets `HubException` `rate_limited`. `HubUserFilter` now really runs: it was only registered in DI, which SignalR ignores, so section 15's "unexpected errors become `internal_error`" was not in effect before this pass.
7. **`demoSource` (B2-09).** The value is stored when the session is created and returned unchanged for the life of the session. Same field, same schema.
8. **Wire details the review noted (B2-13), left as implemented, contract unchanged.** (a) A hub error reaches `HubConnection.invoke` as a message that ENDS with the code, for example `... HubException: not_found`; clients must match the suffix. (b) A hello timeout closes the TCP connection without a close frame the client can rely on (it may see 1006); clients treat any close before the first ack as "reconnect with a new ticket". (c) Hub `Ping` counts as activity for the idle auto-pause (C-17); this needs Product Owner confirmation and is unchanged.
9. **Configuration.** New keys: `Realtime:JoinPermitPerUserPerMinute`, `Realtime:HubInvocationsPerMinute`. `Speech:Replay:Faults:*` and `Speech:Replay:Script` fail startup outside Development, Test or Testing.

Still deferred, not done in this pass: B2-10 (append-only triggers for transcript tables), B2-11 (unbounded provider event channel, strike decay, provider write failure handling, unique final-segment index), B2-12 (per-row try/catch in the lifecycle job), user-account deletion while streaming (no endpoint exists), retention of consent evidence after purge (B1-09, Product Owner), and the single-instance limits in docs/architecture/m2-b2-realtime.md.

## 17. Notes and the final summary after Stop (PO-18d, backend-engineer, 2026-10-31)
ADDITIVE to the frozen v1: one new route (`POST /sessions/{id}/notes`), the real body of the existing `GET /sessions/{id}/notes` placeholder, two new schemas (`lecture.v1/notes.schema.json`, `lecture.v1/notes-request.schema.json`) with valid and invalid fixtures. Nothing existing changed shape. No hub event (the client polls GET, like a catch-up). Mock provider only; nothing here clears G1, G2 or any other gate, and the student-facing wording is a draft (G1).

**The rule.** Once a session is Paused, Stopped or deleted, nothing is sent to an AI provider automatically (B3-06 stands: gateway Active re-check, `SessionCutoff`). After Stop, the ONLY thing that may send the already-recorded transcript to the AI is a deliberate student press, `POST /sessions/{id}/notes`. One press produces BOTH the lecture notes and the final summary revision (`summary.revision.status = "Final"`). Stop itself never produces a Final revision.

**The press.**
1. User-scoped: a foreign or missing session is `404 not_found` with the same body. Authenticated (401), Origin-checked like every unsafe method (403 `csrf_failed`), JSON only (415), body exactly `{}` (400 `validation_failed`).
2. Only a `Stopped` session: `Active`, `Paused` and `Failed` give `409 invalid_state`, and nothing is created, audited or sent.
3. `Idempotency-Key` is REQUIRED (1 to 64 of `[A-Za-z0-9_-]`, else 400). The same key again returns the same press with **200** and `Idempotency-Replayed: true`, whatever its status now. Keys are scoped to the user and session and are kept with the press.
4. One generation per session. A press while another is `Pending`, or after notes are `Ready` (or `NotNeeded`), returns that press with **200** and starts nothing (no audit row, no call). Only after `Unavailable` or `BudgetLimited` does a new press start a new attempt (`revision` + 1), at most 5 attempts per session (`Ai:Notes:MaxAttemptsPerSession`, then 429). New presses are limited to 6 per minute per user (`Ai:Notes:PerMinutePerUser`, 429 `rate_limited`), on top of the general write bucket. Concurrent presses start exactly one generation (one-pending unique index; the losers get the winner's press with 200).
5. A press that starts an attempt answers **202** with the `Notes` resource and `Location: /api/v1/sessions/{id}/notes`. Its status is normally `Pending`; it can already be `NotNeeded` (no transcript text: nothing is sent anywhere), `BudgetLimited` (the lecture-pool budget is exhausted) or `Unavailable` (AI off, breaker open, queue full).
6. Audited: every press that starts an attempt writes one content-free `notes.requested` row (user id, session id, time) to the append-only `lectures.audit_events` BEFORE anything can be sent. That row outlives the session, like `session.delete`.
7. Cancelled by Delete (and by a course purge), not by Stop (a stopped session cannot resume, so no later state change exists). Delete cancels an in-flight provider call at once and the FK cascade removes the press, the notes and the Final summary revision. A request already handed to a vendor cannot be recalled (same note as B3-06 in the B3 re-review), and **vendor copies and anything already exported to Google are not deleted by Delete** (C-21, PO-15c); the UI must say so (G1 wording).

**`Notes` resource** (`notes.schema.json`, strict): `{notesId: "nt_<32 hex>", revision, status: "Pending"|"Ready"|"Unavailable"|"BudgetLimited"|"NotNeeded", source: "mock"|"model"|null, requestedAt, generatedAt|null, coveredFromMs|null, coveredToMs|null, sections: [{heading, bullets: [{text, citations: [{segmentId, startMs}]}]}], finalSummaryRevision|null, truncated}`. Every field is always present. Only `Ready` carries content: `source`, `generatedAt`, `coveredFromMs/ToMs` set and at least one section; every other status has `source:null`, `generatedAt:null`, empty `sections`, `finalSummaryRevision:null`, `truncated:false` (enforced by the schema's if/then/else). Every bullet has 1 to 8 citations, each a stored final segment of the same session with the stored `startMs` (never a model value). `heading` and `text` are untrusted plain text (C-28). `source:"mock"` marks the deterministic mock on the wire (B3-08 class). `finalSummaryRevision` points to the `Final` revision made by the same press (read it with `GET /summary?revision=N`); null if that step failed while the notes succeeded. `truncated:true` means the lecture was longer than the notes input limit and the END is not covered (`coveredToMs` says where it stops).

**`GET /sessions/{id}/notes`** returns the newest attempt (poll while `Pending`), `404` foreign or missing, `409 invalid_state` while the student has not pressed yet (the B1 placeholder status is kept). A press whose job was lost (restart) is shown `Unavailable` after at most `Ai:Notes:StaleSeconds` (600); at startup all `Pending` presses become `Unavailable`.

**Server rules (normative).** The gateway runs the two after-Stop workflows (`Notes`, `FinalSummary`) only if, re-read immediately before every provider call, the session is `Stopped` for this user AND this press is `Pending`, belongs to this user and session, and is younger than the stale limit. Active, Paused, deleted, someone else's, no press, a finished press, an expired press or another session's press all refuse (fail closed, no spend). A live workflow (rolling summary, compaction, Quick recap) still needs an `Active` session even if a press exists. Notes are made from stored finals only (corrections applied), in parts of at most 12,000 prompt characters, at most 10 parts; uncited bullets are dropped, sections left empty are dropped, a part with nothing grounded is rejected (one repair attempt); text is sanitised (`OutputSanitizer`) and language-checked (log-only, as elsewhere). Spend is held in the LECTURE pool (per session, per user-day, per month, the same caps as live AI) per call, plus a per-press cap on the estimated cost of all parts and the final summary together (`Ai:Notes:PerRequestMicroUsd`, placeholder 300,000 micro-USD): a press over it makes no call and ends `BudgetLimited`. All figures are proposals (G2). Notes are stored apart from the transcript (`ai.lecture_notes`, invariant 5) with the seq range, model and prompt version that produced them, and are deleted with the session.

**Deviations and decisions for the Lead Architect.** (a) `NotNeeded` is a fifth status beyond the four in the PO-18d brief (mirrors catch-up `aiState: NotNeeded`): a stopped lecture with no transcript text sends nothing. (b) `Idempotency-Key` is required here (it is optional on `quick-recap`). (c) A press after `Ready` returns 200 with the existing notes rather than 409, so a double press is harmless. (d) Notes share the lecture pool's per-session cap with the live TL;DR, so a heavy live session can leave too little for notes (`BudgetLimited`); a separate `notes` pool is the alternative (open question in `docs/architecture/notes-after-stop.md`).
