import type { TrackLike } from "@/lib/lecture";

/**
 * Microphone seam. Screens depend on this interface only. F2 ships a fake and a thin
 * getUserMedia adapter that is NOT wired to any upload, hub or socket.
 * Raw DOMException messages are never exposed (UX section 5 AC5): only a coarse reason.
 */
export interface MicTrack extends TrackLike {
  /** Stops capture immediately and releases the device. Idempotent. */
  stop(): void;
  /** Fires when the device ends the track on its own (unplugged, permission revoked). Not fired by stop(). */
  onEnded(cb: () => void): () => void;
}

export type MicFailure = "denied" | "no_device" | "unavailable";
export type MicResult = { ok: true; track: MicTrack } | { ok: false; reason: MicFailure };

export interface MicSource {
  /** Must only be called from a user action (a press). Implementations must not retry on their own. */
  request(): Promise<MicResult>;
}

export class FakeMicTrack implements MicTrack {
  readyState: "live" | "ended" = "live";
  enabled = true;
  muted = false;
  stopCalls = 0;
  /** When true, stop() is ignored (simulates a track that stays hot). */
  ignoreStop = false;
  private listeners = new Set<() => void>();

  stop(): void {
    this.stopCalls++;
    if (this.ignoreStop) return;
    this.readyState = "ended";
  }
  onEnded(cb: () => void): () => void {
    this.listeners.add(cb);
    return () => this.listeners.delete(cb);
  }
  /** Simulates device loss: the browser ends the track and fires `ended`. */
  simulateDeviceLoss(): void {
    this.readyState = "ended";
    for (const l of [...this.listeners]) l();
  }
}

export class FakeMicSource implements MicSource {
  requestCount = 0;
  readonly tracks: FakeMicTrack[] = [];
  constructor(public outcome: "ok" | MicFailure = "ok") {}
  async request(): Promise<MicResult> {
    this.requestCount++;
    if (this.outcome !== "ok") return { ok: false, reason: this.outcome };
    const t = new FakeMicTrack();
    this.tracks.push(t);
    return { ok: true, track: t };
  }
}

/** Thin adapter over navigator.mediaDevices.getUserMedia. Not wired to anything in F2. */
export class BrowserMicSource implements MicSource {
  async request(): Promise<MicResult> {
    const md = typeof navigator === "undefined" ? undefined : navigator.mediaDevices;
    if (!md || typeof md.getUserMedia !== "function") return { ok: false, reason: "unavailable" };
    try {
      const stream = await md.getUserMedia({ audio: true });
      const track = stream.getAudioTracks()[0];
      if (!track) {
        stream.getTracks().forEach((t) => t.stop());
        return { ok: false, reason: "no_device" };
      }
      return { ok: true, track: wrapTrack(stream, track) };
    } catch (e) {
      const name = e instanceof Error ? e.name : "";
      if (name === "NotAllowedError" || name === "SecurityError") return { ok: false, reason: "denied" };
      if (name === "NotFoundError" || name === "OverconstrainedError") return { ok: false, reason: "no_device" };
      return { ok: false, reason: "unavailable" }; // never surface e.message
    }
  }
}

function wrapTrack(stream: MediaStream, track: MediaStreamTrack): MicTrack {
  return {
    get readyState() {
      return track.readyState;
    },
    get enabled() {
      return track.enabled;
    },
    get muted() {
      return track.muted;
    },
    stop() {
      stream.getTracks().forEach((t) => t.stop());
    },
    onEnded(cb) {
      track.addEventListener("ended", cb);
      return () => track.removeEventListener("ended", cb);
    },
  };
}
