# Speech Pipeline Design (v0 PROPOSAL, not implemented)
> **WARNING (2026-10-27):** vendor prices, limits and terms in this document are superseded where they conflict with `docs/vendors/verification-report.md` (see `docs/RELEASE_GATES.md`, gate G2). Reconcile before implementation.

Status: design only. No code written or executed. Vendor facts below come from general knowledge and are NOT verified; re-check pricing, retention and DPA terms before any decision. Claude does not transcribe audio; it only consumes finalized transcript text downstream.

## 1. Scope and dependencies
- Owner: speech-engineer. Contract co-owners: backend-engineer (hub/socket, persistence), frontend-engineer (capture UI), security-engineer (consent/retention), adhd-ux-specialist (status UI), qa-engineer.
- Smallest deliverable: mock provider + event contract + replay tests, then one real-provider smoke test after PO approval.

## 2. Data flow
```
Mic -> getUserMedia -> AudioContext -> AudioWorklet (resample 16 kHz mono, PCM16 LE)
    -> 100 ms frames (3200 B) batched to 200 ms binary messages
    -> WebSocket (wss) -> ASP.NET Core ingest endpoint (auth, session ownership check)
    -> ISpeechToTextProvider.StreamAsync -> vendor streaming API (or Mock/Replay)
    -> normalized TranscriptEvent -> transcript store (final only) + realtime fan-out to client
    -> finalized segments -> summary/TL;DR service (Claude, text only)
```
Raw audio exists only in memory (browser ring buffer, server channel, vendor socket). Never written to disk or logs.

## 3. Browser capture
- Start only from an explicit user click on "Start lecture"; show a pre-start consent panel (recording-policy reminder: confirm university and local rules; requires checkbox acknowledgement stored with session as `consentAckAt`). No auto-start, no capture on page load.
- `getUserMedia({audio:{channelCount:1, echoCancellation:true, noiseSuppression:true, autoGainControl:true}})`. Make noise suppression a setting; vendors sometimes do better with raw audio, so A/B in the test plan.
- AudioWorklet (not ScriptProcessor) downsamples from device rate (usually 48 kHz) to 16 kHz, converts float32 to PCM16 LE. Fallback if AudioWorklet unavailable: show unsupported-browser message (no silent degrade).
- Persistent visible indicator (text + icon, not color alone, `aria-live=polite` status): Idle / Listening / Paused / Reconnecting / Stopped / Error. Pause stops sending frames and releases nothing until Stop; Stop releases tracks (`track.stop()`) so the browser mic indicator goes off.
- Denied mic (`NotAllowedError`): explain how to re-enable, offer manual notes / paste-transcript / upload-later fallback, never loop prompts. `NotFoundError`: suggest connecting a mic. `NotReadableError`: another app holds it.
- Device change: listen to `devicechange` and track `ended`. On loss, auto-pause, show "Mic disconnected", offer device picker; resume only on user click. Never silently switch to a different device.
- Tab hidden/throttled: AudioWorklet keeps running; warn about sleep/lock screen on mobile (capture may halt); detect stalled frame clock and flag a gap.

## 4. Transport recommendation: raw WebSocket (binary), not SignalR
- Audio is a high-rate binary upstream; SignalR binary (MessagePack) adds framing/serialization overhead, hub invocation semantics, and per-message size/backpressure tuning with little benefit. Raw WebSocket gives simpler backpressure (`bufferedAmount`), lower overhead, and easier replay testing.
- Use SignalR (or SSE) for the downstream transcript/summary fan-out to clients per CLAUDE.md realtime guidance; this keeps one consistent app realtime channel. Alternatively return transcript events on the same socket (open decision for Lead Architect; default: same socket for transcript events during MVP, SignalR for summaries).
- Auth: short-lived single-use ticket obtained via authenticated REST (`POST /sessions/{id}/audio-ticket`), passed in the first text message or `Sec-WebSocket-Protocol`, never in the URL query (log leakage). Server verifies the session belongs to the user.
- Protocol (`speech.v1`): first message JSON `hello {version, sessionId, sampleRate:16000, encoding:"pcm_s16le", channels:1, resumeFromSeq}`; then binary frames prefixed with a 12-byte header (uint32 seq, uint64 captureOffsetMs); control JSON `pause|resume|stop`; server JSON `ack {seq}`, `error`, and transcript events. Unknown fields ignored; version negotiated.

## 5. Provider abstraction (contract sketch, proposal)
```
interface ISpeechToTextProvider {
  Name; Capabilities {partials, diarization, wordConfidence, customVocabulary}
  Task<ISttStream> OpenAsync(SttOptions o, CancellationToken ct); // language, vocabulary hints, sampleRate
}
interface ISttStream : IAsyncDisposable {
  ValueTask WriteAudioAsync(ReadOnlyMemory<byte> pcm16, long captureOffsetMs, CT);
  IAsyncEnumerable<TranscriptEvent> ReadEventsAsync(CT);
  Task CompleteAsync(CT); // flush finals
}
```
Implementations: `MockSttProvider`/`ReplaySttProvider` (default in dev and CI), then one real adapter. No vendor types leak past the adapter. Credentials from managed secrets only.

### Mock/replay provider (deterministic)
- Input: a fixture JSONL script of events keyed to audio offset (e.g., "when 1,200 ms of audio received, emit partial P1 text X"). Time is driven by audio bytes received, not wall clock, so tests are deterministic and fast.
- Modes: `Scripted` (fixture), `EchoSynthetic` (generated STEM lecture text for demos), and fault injection (drop connection at offset N, delay finals, duplicate/reorder partials, emit low confidence, emit 429).
- Fixtures are synthetic or consented, no real student audio in the repo.
- Replay tests: feed the same fixture twice, assert byte-identical normalized event streams; assert reconnect at every offset yields no duplicate/lost finals.

## 6. Event contract (`transcript.v1`)
```
TranscriptEvent {
  type: "partial" | "final" | "status" | "gap" | "error",
  sessionId, segmentId (stable, server-assigned "seg_{n}"), revision (int, monotonic per segment),
  text, startMs, endMs   // offsets from session start, excludes paused time (see pausedRanges)
  wallClockStart?        // UTC for display only
  confidence? (0-1, nullable), speaker? ("S1", opaque label; NOT identity), words?[{w,startMs,endMs,conf?}]
  provider, providerModel
}
```
- Partial: mutable, replaces prior revision of same `segmentId`; UI must not announce each partial to screen readers (announce finals only, throttled).
- Final: immutable text for that segment; only finals are persisted, fed to summary, cited by notes. A later provider "correction" arrives as a new `revision` marked `supersedes` and keeps original provenance.
- Segment ID stability: the server assigns IDs; provider IDs are mapped. On reconnect, IDs continue from the last committed seq so the same audio never produces a second ID.
- `gap` event marks missing audio (pause excluded, network loss, device loss) with `fromMs,toMs,reason` so notes show "audio unavailable" instead of fabricated text.
- Timecodes: `captureOffsetMs` is derived from the count of samples captured (monotonic), not Date.now, to avoid drift. Pause intervals recorded in the session timeline.

## 7. Reconnection and buffering
- Client ring buffer: max 30 s (about 960 KB at 16 kHz PCM16), bounded. If socket `bufferedAmount` exceeds threshold, drop oldest and emit a local `gap`. Never grow unbounded.
- On disconnect: state "Reconnecting", exponential backoff with jitter (0.5 s to 10 s), resend unacked frames from last `ack` seq within the 30 s buffer, otherwise mark gap and continue. After about 60 s failure, auto-pause and show offline state; transcript so far remains readable; offer manual notes.
- Server: per-session bounded `Channel<byte[]>` (about 5 s); if the vendor is slow, apply backpressure then drop with `gap`. Vendor drop: reopen vendor stream once or twice, replaying a small overlap (about 1 s) with de-dup by offset; fail over to "transcription unavailable" status, not to a different vendor without approval.
- Idle timeout: stop vendor stream if no audio for N minutes (cost control). Hard max session length (e.g., 3 h) and a daily budget cap, configurable.

## 8. Privacy and retention
- No raw audio persistence by default, in our systems; opt-in debugging capture is out of scope for MVP.
- Vendor settings must disable training on customer data and any audio/transcript retention (zero-retention or opt-out flag); record the setting in config and verify in smoke test. Require DPA review and FERPA considerations (security-engineer).
- Logs: metadata only (sessionId, counts, latencies, error codes). Never log audio or transcript text by default.
- Transcripts stored separately from derived summaries (invariant 5); deletion and export per session including segment provenance.
- Others in the room: the UI states that the recording of other people's voices (instructor, classmates) may require permission; instructor/institution consent is the student's responsibility to confirm, and the product surfaces the policy reminder. Open question for PO/security.

## 9. Vendor comparison (unverified; re-check before decisions)
| | Deepgram (Nova-class streaming) | AssemblyAI (streaming) | Azure AI Speech |
|---|---|---|---|
| Latency | Typically sub-300 ms partials; strong | Low, but historically higher than Deepgram; improving | Low to moderate; good with SDK |
| Cost | Per-minute, generally the cheapest of the three at list price | Per-hour, mid | Per-hour, mid; free tier; commitment tiers |
| STEM vocabulary | Keyterm/keyword boosting | Word boost / keyterms | Phrase lists, custom speech models (best customization, more effort) |
| Diarization (streaming) | Available, quality varies | Available/limited | Conversation transcription, more setup |
| Privacy/retention | Offers opt-out of model-improvement data; check retention | Check defaults and zero-retention options | Microsoft enterprise terms, regional processing, no-logging options; strongest enterprise/compliance story (and likely campus Microsoft agreements) |
| Integration | WebSocket API, easy from .NET | WebSocket API | Official .NET SDK (nice fit for ASP.NET Core) |

Recommendation (pending PO approval, not a decision): prototype with **Deepgram** for latency and cost, keep **Azure Speech** as the compliance-first alternative if a campus or FERPA review prefers Microsoft terms. Decide by running the same noisy fixtures through each adapter (WER on STEM terms, partial latency p50/p95, cost per lecture hour, retention terms). Do not enable paid usage before PO approval.

## 10. Lecture hall audio guidance (for students and UX copy)
- Sit within the front half; phone or laptop mic pointed at the speaker, not in a bag. Prefer a wired or USB headset/lav or a small external mic over laptop built-ins.
- Avoid placing the device near fans, keyboards, or on a desk that transmits typing vibration.
- A live "mic level / too quiet / clipping" indicator plus a one-time 5-second test before starting. Warn if the level is persistently low.
- Capture constraints are tunable; STEM terms improve with course vocabulary hints (from syllabus/course title, user-editable).
- Expect lower accuracy with echo, distant speakers, and Q&A from the audience; show per-segment low-confidence styling and let students edit text.

## 11. Test plan (not yet run)
- Unit: header parsing, resampler correctness vs. reference, PCM16 clipping, ring buffer bounds.
- Replay: deterministic mock fixtures, reconnect/dup/gap fault injection, ordering and revision rules.
- Browser: denied permission, device unplug, tab backgrounding, permission revoked mid-session.
- Latency budget (proposal): mic-to-first-partial p95 under 1.5 s, final within 2 s of pause in speech; measure end-to-end with synthetic clock-stamped audio.
- Cost: minutes streamed per session vs. vendor invoice estimate; idle cutoff verified.
- Real-provider smoke test: only with PO approval, synthetic/consented audio, noisy hall recordings.
- Privacy: confirm no audio or text in logs, no audio on disk, vendor retention flag set.

## 12. Open decisions
1. **PO: STT vendor and paid usage/budget cap** (Deepgram vs Azure vs AssemblyAI).
2. PO/security: recording-consent policy wording; instructor-permission requirement; institution rules.
3. Lead Architect: transcript events on the audio socket vs SignalR; ticket auth approach.
4. Security: vendor DPA, FERPA, data-region requirements.
5. Product: diarization (labels only, optional) in MVP or later; partial-display policy for screen readers (adhd-ux-specialist).
6. Lead Architect: session length and daily minute caps.
