import type { SessionState } from "./types";

/** Contract C-16 / section 9. Label shown when the hub is not connected. */
export const OFFLINE_LABEL = "not connected, audio not being saved";

/** The subset of MediaStreamTrack the indicator needs, so tests need no browser. */
export interface TrackLike {
  readyState: "live" | "ended";
  enabled: boolean;
  muted: boolean;
}

export type HubConnection = "connected" | "disconnected";

export type MismatchReason =
  | "track-live-while-server-not-active" // mic hot but server says Paused/Stopped/Failed: stop capture
  | "server-active-without-live-track" // server says Active but no live local audio
  | "server-active-track-muted"; // server says Active, track live but muted/disabled

export type RecordingIndicator =
  | { kind: "Recording" }
  | { kind: "Paused" }
  | { kind: "Stopped"; failed: boolean }
  | { kind: "Mismatch"; reason: MismatchReason; stopCapture: boolean }
  | { kind: "NotConnected"; label: typeof OFFLINE_LABEL; localTrackLive: boolean };

export interface IndicatorInput {
  /** Last known server state from session.state / Join / REST; null before it is known. */
  serverState: SessionState | null;
  connection: HubConnection;
  /** Local capture track, or null when no capture exists. */
  track: TrackLike | null;
}

/**
 * Combines server state with the local track (C-16). The visible indicator is never driven by
 * a button press alone. Precedence: a hot mic that the server does not expect is always a
 * Mismatch (stop capture), even when disconnected and the server state is stale; then
 * NotConnected; then the agreeing states; otherwise Mismatch.
 */
export function deriveRecordingIndicator({ serverState, connection, track }: IndicatorInput): RecordingIndicator {
  const live = track !== null && track.readyState === "live";
  const flowing = live && track.enabled && !track.muted;

  if (live && serverState !== null && serverState !== "Active") {
    return { kind: "Mismatch", reason: "track-live-while-server-not-active", stopCapture: true };
  }
  if (connection === "disconnected" || serverState === null) {
    return { kind: "NotConnected", label: OFFLINE_LABEL, localTrackLive: live };
  }
  switch (serverState) {
    case "Active":
      if (!live) return { kind: "Mismatch", reason: "server-active-without-live-track", stopCapture: false };
      if (!flowing) return { kind: "Mismatch", reason: "server-active-track-muted", stopCapture: false };
      return { kind: "Recording" };
    case "Paused":
      return { kind: "Paused" };
    case "Stopped":
      return { kind: "Stopped", failed: false };
    case "Failed":
      return { kind: "Stopped", failed: true };
  }
}

/** Text for a polite live region. Stable per kind so unchanged states are not re-announced. */
export function indicatorAnnouncement(i: RecordingIndicator): string {
  switch (i.kind) {
    case "Recording":
      return "Recording";
    case "Paused":
      return "Recording paused";
    case "Stopped":
      return i.failed ? "Recording ended unexpectedly" : "Recording stopped";
    case "Mismatch":
      return i.stopCapture ? "Warning: microphone is still on. Stopping capture." : "Warning: recording status does not match your microphone";
    case "NotConnected":
      return OFFLINE_LABEL;
  }
}
