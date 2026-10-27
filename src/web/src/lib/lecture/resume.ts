import { OFFLINE_LABEL } from "./indicator";
import { compareSeq } from "./seq";
import type { HubEvent, JoinRequest, JoinResponse, Seq, TranscriptPage } from "./types";

/** Contract section 3: back off 0/2/5/10/30 s with jitter. */
export const BACKOFF_MS = [0, 2000, 5000, 10000, 30000] as const;
/** Proposal: +/-20 % of the base delay. Zero stays zero so the first retry is immediate. */
export const JITTER_RATIO = 0.2;
/** Proposal: page size for GET /transcript fallback. The contract fixes no maximum `limit`. */
export const PAGE_LIMIT = 200;
const MAX_PAGES = 1000;

export type ResumeStatus = "idle" | "connecting" | "connected" | "reconnecting" | "stopped" | "failed";

export interface ResumeSnapshot {
  status: ResumeStatus;
  attempt: number;
  /** Delay before the next attempt, when reconnecting. */
  nextDelayMs: number | null;
  /** Set while not connected so the UI can show it verbatim. */
  offlineLabel: typeof OFFLINE_LABEL | null;
  /** Last join was truncated and we are/were paging via REST. */
  catchingUpViaRest: boolean;
  lastError: string | null;
}

export interface ResumeDeps {
  sessionId: string;
  /** Open the hub transport. Rejects on failure. */
  connect(): Promise<void>;
  join(req: JoinRequest): Promise<JoinResponse>;
  /** GET /sessions/{id}/transcript?afterSeq&limit */
  fetchTranscript(afterSeq: Seq | null, limit: number): Promise<TranscriptPage>;
  /** Current resume point: contiguous frontier from the reducer, and last accepted summary revision. */
  resumePoint(): { lastSeq: Seq | null; lastSummaryRevision: number | null };
  /** Called after a successful Join, before any REST paging. */
  onJoined(response: JoinResponse, lastSeqSent: Seq | null): void;
  /** Events from the REST fallback, ascending by seq. */
  onEvents(events: HubEvent[]): void;
  /** After REST paging completes: everything through this seq is considered caught up. */
  onFastForward(throughSeq: Seq): void;
  onStatus(snapshot: ResumeSnapshot): void;
  setTimeout(fn: () => void, ms: number): unknown;
  clearTimeout(handle: unknown): void;
  /** [0,1) */
  random(): number;
  /** Errors that retrying cannot fix (default: not_found, unauthenticated, csrf_failed, consent_required). */
  isFatal?(err: unknown): boolean;
}

const FATAL_CODES = new Set(["not_found", "unauthenticated", "csrf_failed", "consent_required"]);
const defaultIsFatal = (err: unknown): boolean =>
  typeof err === "object" && err !== null && "code" in err && FATAL_CODES.has(String((err as { code: unknown }).code));

export function backoffDelay(attempt: number, random: () => number): number {
  const base = BACKOFF_MS[Math.min(attempt, BACKOFF_MS.length - 1)];
  if (base === 0) return 0;
  const jitter = (random() * 2 - 1) * JITTER_RATIO; // [-0.2, 0.2)
  return Math.max(0, Math.round(base * (1 + jitter)));
}

export class ResumeController {
  private status: ResumeStatus = "idle";
  private attempt = 0;
  private nextDelayMs: number | null = null;
  private timer: unknown = null;
  private generation = 0;
  private catchingUpViaRest = false;
  private lastError: string | null = null;

  constructor(private readonly deps: ResumeDeps) {}

  snapshot(): ResumeSnapshot {
    const connected = this.status === "connected";
    return {
      status: this.status,
      attempt: this.attempt,
      nextDelayMs: this.nextDelayMs,
      offlineLabel: connected || this.status === "idle" || this.status === "stopped" ? null : OFFLINE_LABEL,
      catchingUpViaRest: this.catchingUpViaRest,
      lastError: this.lastError,
    };
  }

  /** Begin (or restart) connecting. The first attempt runs immediately (delay 0). */
  start(): void {
    this.cancelTimer();
    this.generation++;
    this.attempt = 0;
    this.set("connecting");
    void this.tryConnect(this.generation);
  }

  /** Transport reported a drop (or a heartbeat timeout). Schedules the next attempt. */
  notifyDisconnected(): void {
    if (this.status === "stopped" || this.status === "failed" || this.status === "idle") return;
    if (this.status === "reconnecting" || this.status === "connecting") return; // already handling it
    this.generation++;
    this.attempt = 0;
    this.scheduleRetry();
  }

  /** User left the session or it ended: no more reconnects. */
  stop(): void {
    this.cancelTimer();
    this.generation++;
    this.nextDelayMs = null;
    this.set("stopped");
  }

  private async tryConnect(gen: number): Promise<void> {
    try {
      await this.deps.connect();
      if (gen !== this.generation) return;
      const { lastSeq, lastSummaryRevision } = this.deps.resumePoint();
      const req: JoinRequest = {
        sessionId: this.deps.sessionId,
        protocol: "lecture.v1",
        ...(lastSeq !== null ? { lastSeq } : {}),
        ...(lastSummaryRevision !== null ? { lastSummaryRevision } : {}),
      };
      const res = await this.deps.join(req);
      if (gen !== this.generation) return;
      this.deps.onJoined(res, lastSeq);
      if (res.replay.truncated) {
        this.catchingUpViaRest = true;
        this.set("connecting");
        await this.pageTranscript(gen, lastSeq, res.currentSeq);
        if (gen !== this.generation) return;
        this.catchingUpViaRest = false;
      }
      this.attempt = 0;
      this.nextDelayMs = null;
      this.lastError = null;
      this.set("connected");
    } catch (err) {
      if (gen !== this.generation) return;
      this.catchingUpViaRest = false;
      // Never surface err.message: it could carry server text. Keep a code or generic label only.
      this.lastError = typeof err === "object" && err !== null && "code" in err ? String((err as { code: unknown }).code) : "connection_failed";
      if ((this.deps.isFatal ?? defaultIsFatal)(err)) {
        this.nextDelayMs = null;
        this.set("failed");
        return;
      }
      this.scheduleRetry();
    }
  }

  /** Page GET /transcript?afterSeq until hasMore is false, then fast-forward to currentSeq. */
  private async pageTranscript(gen: number, from: Seq | null, currentSeq: Seq): Promise<void> {
    let after = from;
    for (let page = 0; page < MAX_PAGES; page++) {
      const res = await this.deps.fetchTranscript(after, PAGE_LIMIT);
      if (gen !== this.generation) return;
      const events = [...res.events].sort((a, b) => compareSeq(a.seq, b.seq));
      if (events.length > 0) {
        this.deps.onEvents(events);
        after = events[events.length - 1].seq;
      }
      if (!res.hasMore) break;
      if (events.length === 0) throw new Error("transcript paging made no progress"); // avoid an infinite loop
    }
    this.deps.onFastForward(currentSeq);
  }

  private scheduleRetry(): void {
    const delay = backoffDelay(this.attempt, this.deps.random);
    this.attempt++;
    this.nextDelayMs = delay;
    this.set("reconnecting");
    const gen = this.generation;
    this.timer = this.deps.setTimeout(() => {
      this.timer = null;
      if (gen !== this.generation) return;
      this.nextDelayMs = null;
      this.set("connecting");
      void this.tryConnect(gen);
    }, delay);
  }

  private cancelTimer(): void {
    if (this.timer !== null) this.deps.clearTimeout(this.timer);
    this.timer = null;
  }

  private set(status: ResumeStatus): void {
    this.status = status;
    this.deps.onStatus(this.snapshot());
  }
}
