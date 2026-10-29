import {
  MockLectureSource,
  initialLectureState,
  lectureReducer,
  nextSeq,
  maxSeq,
  type HubEvent,
  type LectureState,
  type Seq,
} from "@/lib/lecture";
import { sessionStateEvent } from "@/lib/lecture/builders";
import type { LectureAi, LectureRuntime } from "./ports";

export interface HarnessRuntimeOptions {
  sessionId?: string;
  /** Server log: replayed on Join. */
  events: HubEvent[];
  /** Live events delivered one by one via controls.step(). */
  script?: HubEvent[];
  online?: boolean;
  demoSource?: boolean;
  /** REST failures to inject. */
  failActions?: { pause?: boolean; resume?: boolean; stop?: boolean };
  /** When true, REST succeeds but the matching session.state event is never delivered (mismatch tests). */
  swallowStateEvents?: boolean;
  /** AI routes stand-in. Absent: no Quick recap panel. */
  ai?: LectureAi;
}

export interface HarnessControls {
  source: MockLectureSource;
  /** Deliver the next scripted live event; false when exhausted. */
  step(): boolean;
  push(e: HubEvent): void;
  /** Simulate a network drop (transport reports it). */
  drop(): void;
  /** Allow connects to succeed again (the controller retries on its backoff). */
  restore(): void;
  /** Reducer state for `events`, for initialLecture. */
  initialLecture: LectureState;
}

/**
 * Synthetic stand-in for the real hub, built on MockLectureSource. REST actions append
 * session.state events with the next seq, exactly as the server would. NOT the real backend.
 */
export function createHarnessRuntime(o: HarnessRuntimeOptions): { runtime: LectureRuntime; controls: HarnessControls } {
  const sessionId = o.sessionId ?? "ses_harness";
  const source = new MockLectureSource({ sessionId, events: o.events });
  let online = o.online ?? true;
  const dropListeners = new Set<() => void>();
  let seq: Seq = "0";
  for (const e of o.events) if (e.seq !== null) seq = maxSeq(seq, e.seq);
  const script = [...(o.script ?? [])];

  const changeState = (state: "Active" | "Paused" | "Stopped", reason: string) => {
    seq = nextSeq(seq);
    const ev = sessionStateEvent(sessionId, seq, state, reason);
    if (o.swallowStateEvents) source.log.push(ev);
    else source.push(ev);
  };

  const runtime: LectureRuntime = {
    sessionId,
    demoSource: o.demoSource ?? true,
    transport: {
      subscribe: (h) => source.subscribe(h),
      connect: async () => {
        if (!online) throw new Error("offline");
        source.reconnect();
      },
      join: (r) => source.join(r),
      fetchTranscript: (a, l) => source.fetchTranscript(a, l),
      onDisconnect: (cb) => {
        dropListeners.add(cb);
        return () => dropListeners.delete(cb);
      },
    },
    ai: o.ai,
    actions: {
      pause: async () => {
        if (o.failActions?.pause) throw new Error("fail");
        changeState("Paused", "user");
      },
      resume: async () => {
        if (o.failActions?.resume) throw new Error("fail");
        changeState("Active", "user");
      },
      stop: async () => {
        if (o.failActions?.stop) throw new Error("fail");
        changeState("Stopped", "user");
      },
    },
  };

  const initialLecture = lectureReducer(initialLectureState(sessionId), { kind: "events", events: o.events });

  return {
    runtime,
    controls: {
      source,
      initialLecture,
      step() {
        const e = script.shift();
        if (!e) return false;
        if (e.seq !== null) seq = maxSeq(seq, e.seq);
        source.push(e);
        return true;
      },
      push: (e) => {
        if (e.seq !== null) seq = maxSeq(seq, e.seq);
        source.push(e);
      },
      drop() {
        online = false;
        source.dropConnection();
        dropListeners.forEach((l) => l());
      },
      restore() {
        online = true;
      },
    },
  };
}
