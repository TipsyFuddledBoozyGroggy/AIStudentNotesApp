import { ApiError, type ApiClient, type LectureSessionDto } from "@/lib/api";
import { HubTransport } from "@/lib/lecture/realtime/hubTransport";
import { AudioStreamer, type AudioLink, type AudioLinkEvent, type AudioLinkState, type SocketLike } from "@/lib/lecture/realtime/audioStreamer";
import { startWorkletCapture } from "@/lib/lecture/realtime/capture";
import type { MicTrack } from "./micSource";
import type { LectureBackend, LectureRuntime, StartError } from "./ports";

/** Restartable audio port: every start() after an end() builds a fresh streamer (new socket, new single-use ticket). */
export class AudioController implements AudioLink {
  private cur: AudioStreamer | null = null;
  private readonly listeners = new Set<(e: AudioLinkEvent) => void>();
  private offTrack: (() => void) | null = null;

  constructor(private readonly makeStreamer: () => AudioStreamer) {}

  start(track: unknown): void {
    if (this.cur && this.cur.state() !== "ended") return;
    const s = this.makeStreamer();
    this.cur = s;
    s.subscribe((e) => {
      if (this.cur === s) for (const l of [...this.listeners]) l(e);
    });
    const t = track as MicTrack;
    this.offTrack?.();
    this.offTrack = t.onEnded(() => s.end("teardown")); // a dead device must stop the stream too
    s.start(track);
  }

  end(control: "pause" | "stop" | "teardown"): void {
    this.offTrack?.();
    this.offTrack = null;
    this.cur?.end(control);
  }

  state(): AudioLinkState {
    return this.cur?.state() ?? "idle";
  }

  subscribe(cb: (e: AudioLinkEvent) => void): () => void {
    this.listeners.add(cb);
    return () => this.listeners.delete(cb);
  }
}

export interface RealRuntimeDeps {
  api: ApiClient;
  /** Override for tests; defaults to the browser WebSocket on the page origin. */
  createSocket?: (path: string) => SocketLike;
}

export function createRealRuntime(deps: RealRuntimeDeps, session: Pick<LectureSessionDto, "id" | "demoSource">): LectureRuntime {
  const { api } = deps;
  const sessionId = session.id;
  const hub = new HubTransport({ sessionId, fetchTranscript: (a, l) => api.getTranscriptPage(sessionId, a, l) });
  const socketUrl = `/ingest/sessions/${encodeURIComponent(sessionId)}/audio`;
  const createSocket =
    deps.createSocket ??
    ((path: string) => {
      const proto = window.location.protocol === "https:" ? "wss" : "ws";
      return new WebSocket(`${proto}://${window.location.host}${path}`) as unknown as SocketLike;
    });
  const audio = new AudioController(
    () =>
      new AudioStreamer({
        getTicket: () => api.createAudioTicket(sessionId),
        createSocket: () => createSocket(socketUrl),
        startCapture: (track, onFrame) => {
          const stream = (track as MicTrack).stream;
          if (!stream) return Promise.reject(new Error("no stream"));
          return startWorkletCapture(stream, onFrame);
        },
        setTimeout: (fn, ms) => setTimeout(fn, ms),
        clearTimeout: (h) => clearTimeout(h as ReturnType<typeof setTimeout>),
        now: () => Date.now(),
        random: Math.random,
      }),
  );
  return {
    sessionId,
    demoSource: session.demoSource === true,
    transport: {
      subscribe: hub.subscribe,
      connect: hub.connect,
      join: hub.join,
      fetchTranscript: hub.fetchTranscript,
      onDisconnect: hub.onDisconnect,
    },
    actions: {
      pause: async () => void (await api.pauseSession(sessionId)),
      resume: async () => void (await api.resumeSession(sessionId)),
      stop: async () => void (await api.stopSession(sessionId)),
    },
    audio,
    // Hub only: the screen ends audio and the mic itself after a deferred unmount check (StrictMode safe).
    dispose: () => void hub.dispose(),
  };
}

/** Real backend for the start flow. One Idempotency-Key per start attempt; reused after a network failure. */
export function createRealBackend(deps: RealRuntimeDeps): LectureBackend {
  let key: string | null = null;
  return {
    async startSession({ courseId, consent }) {
      key ??= crypto.randomUUID();
      try {
        const session = await deps.api.createSession({ courseId, consent, idempotencyKey: key });
        key = null;
        return { ok: true, runtime: createRealRuntime(deps, session) };
      } catch (e) {
        let error: StartError = "network";
        if (e instanceof ApiError && !e.isNetwork) {
          if (e.code === "consent_required" || e.status === 403) error = "consent_required";
          else if (e.code === "invalid_state" || e.status === 409) error = "invalid_state";
          if (error !== "network" || e.status < 500) key = null; // a definitive answer: next press is a new attempt
        }
        return { ok: false, error };
      }
    },
  };
}
