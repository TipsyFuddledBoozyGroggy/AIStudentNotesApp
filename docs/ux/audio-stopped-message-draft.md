# Audio stopped after repeated refused connections: message DRAFT

Status: **DRAFT, NOT SHIPPED.** This is G1 / consent-sensitive wording. It must go through `neurodivergent-ux-specialist`, `legal-compliance-reviewer` / `security-engineer` and the Product Owner before any of it reaches `copy.ts`. Nothing here changes the app. Author: frontend-engineer, 2026-11-01, for PO-20af (Review Desk T-4401).

## What ships today (no new copy)

PO-20af caps repeated 4401 closes on the audio socket. After 3 closes in a row that never reached an ack, the streamer ends with the existing `"rejected"` reason (`src/web/src/lib/lecture/realtime/audioStreamer.ts`, `MAX_UNACKED_4401`). The existing `"rejected"` handling in `useLectureSession.ts` runs: the microphone track is stopped. The server still says Active, so the screen shows the existing mismatch panel with the existing text (`MISMATCH_COPY.bodyMicOff` in `src/web/src/components/lecture/copy.ts`):

> The server shows a recording, but your microphone is not on. Nothing is being recorded here. Choose Check again.

and the existing announcement `ANNOUNCE.mismatch`. That text was not written for this case: it says nothing about why audio stopped, and "Choose Check again" re-reads state but does not restart audio.

## Facts the message must stay within (from the code, 2026-11-01)

| Question | Answer from the code | Source |
|---|---|---|
| Is the mic still capturing after the stop? | **No.** `finish()` stops capture, then `useLectureSession` stops the track on `"rejected"`. | `audioStreamer.ts` `finish`; `useLectureSession.ts` "ended" handler |
| Was audio captured during the failing attempts? | **Possibly yes.** Capture runs while reconnecting; frames wait in a 30 s client ring buffer (150 x 200 ms). | `audioStreamer.ts` `RING_FRAMES`, `onPcm` |
| What happens to that buffered audio? | Discarded in memory when the stream ends (`ring.clear()`); never sent later and never replayed (C-14). Never written to disk by the client. | `audioStreamer.ts` `finish` |
| Did any of it reach the server? | **Unknown from the client.** The drain starts when the socket opens, before the ack, so frames may be sent before the 4401 arrives. Whether the server discards frames on a socket whose hello it refused needs backend confirmation. | `audioStreamer.ts` `open` / `startDrain` |
| Was earlier audio transcribed? | Yes, if an earlier connection was acked; that transcript is kept. The cap resets on each ack. | `onServerText` |
| Is the session paused on the server? | **No, not by this path.** The client does not call pause; the server's no-audio auto-pause (5 min) applies. | `useLectureSession.ts` (only `offline_timeout` and mic loss call `pause()`) |

So: "Nothing was recorded" is **not supported** and must not be used. "Audio is not being saved." is true for *now* (after the stop), matching the existing framing in `ANNOUNCE.disconnected` / `OFFLINE_COPY.banner`.

## Proposed wording (DRAFT, for review only)

Heading (proposal): `Audio stopped`

Body (proposal): `The connection kept refusing audio, so the microphone is off. Audio is not being saved. Your transcript so far is kept.`

Announcement (proposal, would reuse an existing live region, no new one): `Audio stopped. The microphone is off.`

Action (proposal, not built): a press-to-restart path (Pause then Resume, which gets a fresh ticket) rather than "Check again". Resume is never automatic.

Notes on the draft: says whether the mic is capturing (it is not); "Audio is not being saved." only in the present tense, where it is true; makes no claim about the last up to 30 s captured during retries; calm, no alarm, no blame, no certainty about cause.

## Questions for review

Legal / security:
1. Is it acceptable to say nothing about the up to 30 s captured in memory during retries and then discarded, or must the message say it existed and was discarded?
2. Backend to confirm: does the server drop every binary frame received on a socket whose hello it refused (4401)? Until confirmed, no wording may imply none of it reached the server.
3. Should the client also call pause on this path, so the server stops showing Active instead of waiting for the 5 min no-audio auto-pause?

UX (`neurodivergent-ux-specialist`):
4. Is "The connection kept refusing audio" understandable, or should the cause be left out ("Audio stopped, so the microphone is off.")?
5. Should this get its own panel, or keep the mismatch panel with a new body? Which action helps most: Resume, Check again, or Back to course?
6. Does "Your transcript so far is kept." over-promise if the transcript has not yet loaded?

Product Owner:
7. Is a cap of 3 closes (ends within seconds) right, versus letting the 60 s watchdog decide?
8. Approve or reject the final wording after 1 to 6 are answered; record it as a decision before it ships.
