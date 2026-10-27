import * as b from "./builders";
import { compareSeq } from "./seq";
import type { HubEvent, JoinRequest, JoinResponse, PersistedEvent, Seq, TranscriptFinalEvent, TranscriptPage } from "./types";
import { isPersistedEvent } from "./types";

/**
 * Synthetic stand-in for the lecture hub + REST transcript endpoint, for UI work and tests.
 * Deterministic: no timers, no randomness. It replays scripted events and can inject faults
 * into what subscribers receive; the server-side log (`log`) always stays correct, so a
 * client that resumes (Join with lastSeq / REST paging) can recover from every fault.
 * NOT a simulation of the real server's behavior beyond the contract's replay rules.
 */

export type Fault =
  | { kind: "duplicate"; index: number }
  /** Deliver the event at `index` after the one at index+1. */
  | { kind: "swapWithNext"; index: number }
  /** Deliver nothing for the event at `index` (it remains in the server log). */
  | { kind: "drop"; index: number };

export type Handler = (event: HubEvent) => void;

export const REPLAY_CAP = 500; // contract section 10 (C-10/C-27)

export interface MockLectureSourceOptions {
  sessionId: string;
  events: HubEvent[];
  faults?: Fault[];
  replayCap?: number;
}

export class MockLectureSource {
  readonly sessionId: string;
  /** Server truth: every event in emission order. */
  readonly log: HubEvent[];
  private cursor = 0;
  private handlers = new Set<Handler>();
  private connected = true;
  private faults: Fault[];
  private replayCap: number;
  private held: HubEvent | null = null;
  private heldAfter = -1;

  constructor(opts: MockLectureSourceOptions) {
    this.sessionId = opts.sessionId;
    this.log = [...opts.events];
    this.faults = opts.faults ?? [];
    this.replayCap = opts.replayCap ?? REPLAY_CAP;
  }

  subscribe(h: Handler): () => void {
    this.handlers.add(h);
    return () => this.handlers.delete(h);
  }

  get isConnected(): boolean {
    return this.connected;
  }

  /** Simulates a network drop: subsequent emissions are not delivered (but are logged server-side). */
  dropConnection(): void {
    this.connected = false;
  }

  /** Reconnect transport only; the client must still Join to receive missed events. */
  reconnect(): void {
    this.connected = true;
  }

  get remaining(): number {
    return this.log.length - this.cursor;
  }

  /** Deliver the next scripted event (applying faults). Returns false when the script is exhausted. */
  step(): boolean {
    if (this.cursor >= this.log.length) return false;
    const i = this.cursor++;
    const ev = this.log[i];
    const f = this.faults.filter((x) => x.index === i);
    if (f.some((x) => x.kind === "drop")) {
      this.flushHeld(i);
      return true;
    }
    if (f.some((x) => x.kind === "swapWithNext") && i + 1 < this.log.length) {
      this.held = ev;
      this.heldAfter = i + 1;
      return true;
    }
    this.deliver(ev);
    if (f.some((x) => x.kind === "duplicate")) this.deliver(ev);
    this.flushHeld(i);
    return true;
  }

  private flushHeld(i: number): void {
    if (this.held && this.heldAfter === i) {
      const h = this.held;
      this.held = null;
      this.deliver(h);
    }
  }

  playAll(): void {
    while (this.step());
  }

  /** Append a live event to the server log and deliver it (no fault injection). */
  push(event: HubEvent): void {
    this.log.push(event);
    this.cursor = this.log.length;
    this.deliver(event);
  }

  private deliver(e: HubEvent): void {
    if (!this.connected) return;
    for (const h of [...this.handlers]) h(e);
  }

  private persisted(): PersistedEvent[] {
    return this.log.filter(isPersistedEvent).sort((a, b) => compareSeq(a.seq, b.seq));
  }

  /** Hub Join: foreign/unknown session fails identically (reject with not_found). */
  async join(req: JoinRequest): Promise<JoinResponse> {
    if (req.sessionId !== this.sessionId) throw Object.assign(new Error("not found"), { code: "not_found" });
    const all = this.persisted();
    const currentSeq: Seq = all.length ? all[all.length - 1].seq : "0";
    const after = req.lastSeq;
    const out = after === undefined ? all : all.filter((e) => compareSeq(e.seq, after) > 0);
    // Out-of-range lastSeq (beyond current): empty replay, current state (C-12).
    const truncated = out.length > this.replayCap;
    const replay = truncated ? [] : out;
    const summaries = this.log.filter((e) => e.type === "summary.revision");
    const lastSummary = summaries.length ? (summaries[summaries.length - 1] as Extract<HubEvent, { type: "summary.revision" }>) : null;
    this.connected = true;
    for (const e of replay) this.deliver(e);
    if (lastSummary && (req.lastSummaryRevision ?? 0) < lastSummary.payload.revision) this.deliver(lastSummary);
    const stateEvents = all.filter((e) => e.type === "session.state");
    const state = stateEvents.length ? (stateEvents[stateEvents.length - 1] as Extract<PersistedEvent, { type: "session.state" }>).payload.state : "Active";
    return {
      state,
      currentSeq,
      currentSummaryRevision: lastSummary ? lastSummary.payload.revision : null,
      replay: {
        fromSeq: replay.length ? replay[0].seq : null,
        toSeq: replay.length ? replay[replay.length - 1].seq : null,
        truncated,
      },
    };
  }

  /** GET /sessions/{id}/transcript?afterSeq&limit. Finals only, as in the contract. */
  async fetchTranscript(afterSeq: Seq | null, limit: number): Promise<TranscriptPage> {
    const finals = this.persisted().filter((e): e is TranscriptFinalEvent => e.type === "transcript.final");
    const rest = afterSeq === null ? finals : finals.filter((e) => compareSeq(e.seq, afterSeq) > 0);
    return { events: rest.slice(0, limit), hasMore: rest.length > limit };
  }
}

/** A short synthetic lecture: Active, three finals, a correction, a gap, a summary, then Stopped. */
export function scriptedLecture(sessionId: string): HubEvent[] {
  return [
    b.sessionStateEvent(sessionId, "1", "Active"),
    b.sttStatusEvent(sessionId, "Ok"),
    b.partialEvent(sessionId, 1, 1, "so the deriv"),
    b.finalEvent(sessionId, "2", 1, "So the derivative of x squared is two x."),
    b.finalEvent(sessionId, "3", 2, "The power rule says bring the exponent down."),
    b.correctionEvent(sessionId, "4", 1, 2, "So the derivative of x squared is 2x."),
    b.gapEvent(sessionId, "5", 5000, 8000, "network"),
    b.finalEvent(sessionId, "6", 3, "Now let us look at the chain rule.", 9000),
    b.summaryEvent(sessionId, 1, "Derivatives and the power rule.", { basedOnSeq: "6" }),
    b.sessionStateEvent(sessionId, "7", "Stopped"),
  ];
}
