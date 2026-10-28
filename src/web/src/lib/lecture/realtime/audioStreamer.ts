import { backoffDelay } from "../resume";
import { encodeFrame, FRAME_MS, MAX_FRAME_BYTES } from "./audioFrame";
import { RingBuffer } from "./ringBuffer";

/** What the UI needs to know about the upstream audio socket. */
export type AudioLinkState = "idle" | "connecting" | "streaming" | "reconnecting" | "ended";

/**
 * Why the link ended for good (no further retries):
 * - "stopped": we closed it (pause, stop or teardown).
 * - "auth": signed out (ticket request was 401).
 * - "conflict": server cut it off (close 4409) or refused a ticket because the session is not Active / not ours.
 * - "rejected": the server closed for a policy or format reason that retrying will not fix, or capture failed.
 */
export type AudioEndReason = "stopped" | "auth" | "conflict" | "rejected";

export type AudioLinkEvent =
  | { kind: "state"; state: AudioLinkState }
  | { kind: "ended"; reason: AudioEndReason }
  /** Client ring buffer overflowed: audio older than 30 s was dropped (never silently). */
  | { kind: "dropped"; ms: number }
  /** No working link for about 60 s while capturing (contract section 4.5): the owner should auto-pause. */
  | { kind: "offline_timeout" };

/** Port the lecture screen uses. Restartable: each start() after an end() opens a fresh socket with a fresh ticket. */
export interface AudioLink {
  /** Begin capture and streaming for this live track. Idempotent while running. */
  start(track: unknown): void;
  /** Ends capture and the socket. `pause`/`stop` also tell the server (best effort; REST stays authoritative). */
  end(control: "pause" | "stop" | "teardown"): void;
  subscribe(cb: (e: AudioLinkEvent) => void): () => void;
  state(): AudioLinkState;
}

export interface SocketLike {
  readonly readyState: number;
  readonly bufferedAmount: number;
  binaryType: string;
  onopen: (() => void) | null;
  onmessage: ((e: { data: unknown }) => void) | null;
  onclose: ((e: { code: number }) => void) | null;
  onerror: (() => void) | null;
  send(data: string | ArrayBufferView | ArrayBuffer): void;
  close(code?: number): void;
}

export interface AudioStreamerDeps {
  /** Rejects with an object carrying `status` (HTTP) when the ticket is refused. */
  getTicket(): Promise<{ ticket: string }>;
  createSocket(): SocketLike;
  /** Starts capture; the callback gets exact 200 ms Int16 frames. Returns a stopper. */
  startCapture(track: unknown, onFrame: (pcm: Int16Array) => void): Promise<() => void>;
  setTimeout(fn: () => void, ms: number): unknown;
  clearTimeout(h: unknown): void;
  now(): number;
  random(): number;
}

const OPEN = 1;
export const RING_FRAMES = 150; // 30 s
export const OFFLINE_TIMEOUT_MS = 60_000;
/** Server allows ~1.1x real time with a 5 s burst; stay below with margin. */
export const SEND_RATE = 1.05;
export const BURST_MS = 4000;
export const MAX_BUFFERED_BYTES = 128 * 1024;
const DRAIN_INTERVAL_MS = 50;
const MAX_POLICY_RETRIES = 3;

/**
 * Upstream audio over the ticketed WebSocket (speech.v1). Capture feeds a bounded ring buffer; a paced drain
 * sends frames while the socket is open, `bufferedAmount` is small (backpressure) and the byte-rate budget allows.
 * Audio older than the ring is dropped and reported, never queued without bound. The ring is cleared on
 * pause/stop and never replayed after a resume (C-14). Reconnects always use a fresh single-use ticket.
 * Single use: after it ends, create a new instance.
 */
export class AudioStreamer implements AudioLink {
  private readonly listeners = new Set<(e: AudioLinkEvent) => void>();
  private st: AudioLinkState = "idle";
  private readonly ring = new RingBuffer<Uint8Array>(RING_FRAMES);
  private stopCapture: (() => void) | null = null;
  private socket: SocketLike | null = null;
  private helloSent = false;
  private connectionGen = 0;
  private running = false;
  private frameSeq = 0;
  private capturedFrames = 0;
  private attempt = 0;
  private retryTimer: unknown = null;
  private drainTimer: unknown = null;
  private offlineTimer: unknown = null;
  private offlineFired = false;
  private sentMs = 0;
  private openedAt = 0;
  private holdUntil = 0;
  private policyRetries = 0;
  private hadAck = false;

  constructor(private readonly deps: AudioStreamerDeps) {}

  state(): AudioLinkState {
    return this.st;
  }

  subscribe(cb: (e: AudioLinkEvent) => void): () => void {
    this.listeners.add(cb);
    return () => this.listeners.delete(cb);
  }

  /** Frames waiting to be sent (test/diagnostic). */
  get queued(): number {
    return this.ring.length;
  }

  start(track: unknown): void {
    if (this.running || this.st === "ended") return;
    this.running = true;
    this.armOffline();
    this.setState("connecting");
    void this.deps
      .startCapture(track, (pcm) => this.onPcm(pcm))
      .then((stop) => {
        if (!this.running) stop();
        else this.stopCapture = stop;
      })
      .catch(() => this.finish("rejected"));
    this.connect();
  }

  end(control: "pause" | "stop" | "teardown"): void {
    if (!this.running) return;
    if (control !== "teardown" && this.socket && this.socket.readyState === OPEN && this.helloSent) {
      try {
        this.socket.send(JSON.stringify({ type: control }));
      } catch {
        /* REST is authoritative */
      }
    }
    this.finish("stopped");
  }

  // --- capture -----------------------------------------------------------------------------------
  private onPcm(pcm: Int16Array): void {
    if (!this.running) return;
    const offsetMs = this.capturedFrames * FRAME_MS;
    this.capturedFrames++;
    const bytes = encodeFrame(this.frameSeq++ >>> 0, offsetMs, pcm);
    if (bytes.byteLength > MAX_FRAME_BYTES) return; // cannot happen with 200 ms frames; never send an oversize frame
    const dropped = this.ring.push(bytes);
    if (dropped > 0) this.emit({ kind: "dropped", ms: dropped * FRAME_MS });
  }

  // --- connection --------------------------------------------------------------------------------
  private connect(): void {
    if (!this.running) return;
    const gen = ++this.connectionGen;
    this.helloSent = false;
    this.hadAck = false;
    this.setState(this.attempt > 0 ? "reconnecting" : "connecting");
    this.deps
      .getTicket()
      .then(({ ticket }) => {
        if (gen !== this.connectionGen || !this.running) return;
        this.open(ticket, gen);
      })
      .catch((err: unknown) => {
        if (gen !== this.connectionGen || !this.running) return;
        const status = typeof err === "object" && err !== null && "status" in err ? Number((err as { status: unknown }).status) : 0;
        if (status === 401) return this.finish("auth");
        if (status === 403 || status === 404 || status === 409) return this.finish("conflict");
        this.scheduleRetry(status === 429 ? 10_000 : 0);
      });
  }

  private open(ticket: string, gen: number): void {
    let ws: SocketLike;
    try {
      ws = this.deps.createSocket();
    } catch {
      return this.scheduleRetry(0);
    }
    ws.binaryType = "arraybuffer";
    this.socket = ws;
    ws.onopen = () => {
      if (gen !== this.connectionGen) return;
      try {
        ws.send(JSON.stringify({ type: "hello", version: "speech.v1", ticket, sampleRate: 16000, encoding: "pcm_s16le", channels: 1 }));
      } catch {
        return;
      }
      this.helloSent = true;
      this.openedAt = this.deps.now();
      this.sentMs = 0;
      this.startDrain(gen);
    };
    ws.onmessage = (e) => {
      if (gen !== this.connectionGen) return;
      this.onServerText(e.data);
    };
    ws.onerror = () => undefined; // the close event follows and carries the code
    ws.onclose = (e) => {
      if (gen !== this.connectionGen) return;
      this.onClosed(e.code);
    };
  }

  private onServerText(data: unknown): void {
    if (typeof data !== "string") return;
    let msg: { type?: unknown; code?: unknown };
    try {
      msg = JSON.parse(data) as { type?: unknown; code?: unknown };
    } catch {
      return;
    }
    if (msg.type === "ack") {
      if (!this.hadAck) {
        this.hadAck = true;
        this.attempt = 0;
        this.policyRetries = 0;
        this.offlineFired = false;
        this.setState("streaming");
      }
      this.armOffline(); // an ack proves the link is alive
    } else if (msg.type === "error" && msg.code === "rate_limited") {
      this.holdUntil = this.deps.now() + 1000; // slow down briefly; frames wait in the ring
    }
    // `gap` and `provider_unavailable` are reflected by hub events; the audio socket carries no transcript.
  }

  private onClosed(code: number): void {
    this.stopDrain();
    this.socket = null;
    this.helloSent = false;
    if (!this.running) return;
    if (code === 4409) return this.finish("conflict");
    if (code === 1007 || code === 1009) return this.finish("rejected");
    if (code === 1008 && ++this.policyRetries > MAX_POLICY_RETRIES) return this.finish("rejected");
    // 4401 (ticket or login lost), 1006, 1011, a close we did not ask for, hello timeout: reconnect with a new ticket.
    // The new ticket request reveals the truth over REST (401 = signed out, 409 = not Active).
    this.scheduleRetry(0);
  }

  private scheduleRetry(minDelayMs: number): void {
    if (!this.running) return;
    const delay = Math.max(minDelayMs, backoffDelay(this.attempt, this.deps.random));
    this.attempt++;
    this.setState("reconnecting");
    this.deps.clearTimeout(this.retryTimer);
    this.retryTimer = this.deps.setTimeout(() => {
      this.retryTimer = null;
      this.connect();
    }, delay);
  }

  // --- paced drain + backpressure ------------------------------------------------------------------
  private startDrain(gen: number): void {
    this.stopDrain();
    const tick = () => {
      if (gen !== this.connectionGen || !this.running) return;
      this.drain();
      this.drainTimer = this.deps.setTimeout(tick, DRAIN_INTERVAL_MS);
    };
    this.drainTimer = this.deps.setTimeout(tick, 0);
  }

  private stopDrain(): void {
    if (this.drainTimer !== null) this.deps.clearTimeout(this.drainTimer);
    this.drainTimer = null;
  }

  private drain(): void {
    const ws = this.socket;
    if (!ws || ws.readyState !== OPEN || !this.helloSent) return;
    const now = this.deps.now();
    if (now < this.holdUntil) return;
    const allowedMs = (now - this.openedAt) * SEND_RATE + BURST_MS;
    while (this.ring.length > 0 && ws.bufferedAmount < MAX_BUFFERED_BYTES && this.sentMs + FRAME_MS <= allowedMs) {
      const f = this.ring.peek()!;
      try {
        ws.send(f);
      } catch {
        return;
      }
      this.ring.shift();
      this.sentMs += FRAME_MS;
    }
  }

  // --- offline watchdog ------------------------------------------------------------------------------
  private armOffline(): void {
    if (this.offlineTimer !== null) this.deps.clearTimeout(this.offlineTimer);
    this.offlineTimer = this.deps.setTimeout(() => {
      this.offlineTimer = null;
      if (!this.running || this.offlineFired) return;
      this.offlineFired = true;
      this.emit({ kind: "offline_timeout" });
    }, OFFLINE_TIMEOUT_MS);
  }

  // --- teardown ----------------------------------------------------------------------------------------
  private finish(reason: AudioEndReason): void {
    const was = this.running;
    this.running = false;
    this.connectionGen++;
    this.stopDrain();
    for (const h of [this.retryTimer, this.offlineTimer]) if (h !== null) this.deps.clearTimeout(h);
    this.retryTimer = this.offlineTimer = null;
    this.stopCapture?.();
    this.stopCapture = null;
    this.ring.clear(); // never replayed after resume (C-14)
    const ws = this.socket;
    this.socket = null;
    if (ws) {
      ws.onopen = ws.onmessage = ws.onclose = ws.onerror = null;
      try {
        ws.close(1000);
      } catch {
        /* already closed */
      }
    }
    this.setState("ended");
    if (was) this.emit({ kind: "ended", reason });
  }

  private setState(s: AudioLinkState): void {
    if (this.st === s) return;
    this.st = s;
    this.emit({ kind: "state", state: s });
  }

  private emit(e: AudioLinkEvent): void {
    for (const l of [...this.listeners]) l(e);
  }
}
