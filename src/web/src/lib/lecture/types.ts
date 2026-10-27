// Hand-written TypeScript mirror of src/shared/contracts/lecture.v1/*.schema.json.
// The schemas are the source of truth (docs/API_CONTRACTS.md section 11); the contract test in
// schemas.test.ts validates fixtures against the schemas, not against these types.

/** Decimal-string int64. Compare only with BigInt (see seq.ts). */
export type Seq = string;

export type SessionState = "Active" | "Paused" | "Stopped" | "Failed";
export type GapReason = "paused" | "network" | "device" | "provider";
export type SttState = "Ok" | "Degraded" | "Down";
export type SummaryJobState = "Pending" | "Running" | "Delayed" | "Failed";
export type WindowMinutes = 2 | 5 | 10;

export type ErrorCode =
  | "validation_failed"
  | "unauthenticated"
  | "consent_required"
  | "csrf_failed"
  | "not_found"
  | "invalid_state"
  | "concurrency_conflict"
  | "precondition_required"
  | "precondition_failed"
  | "rate_limited"
  | "provider_unavailable";

export interface Citation {
  segmentId: string;
  startMs: number;
}

interface Envelope<T extends string, S extends Seq | null, P> {
  v: 1;
  type: T;
  sessionId: string;
  seq: S;
  ts: string;
  payload: P;
}

export interface SummaryRevisionPayload {
  revision: number;
  basedOnSeq: Seq;
  tldr: string;
  bullets: string[];
  coveredFromMs: number;
  coveredToMs: number;
  status: "Draft" | "Final";
  citations: Citation[];
}

export interface CatchUpPayload {
  catchUpId: string;
  windowMinutes: WindowMinutes;
  text: string;
  citations: Citation[];
  kind: "Extractive" | "Ai";
}

export type SessionStateEvent = Envelope<"session.state", Seq, { state: SessionState; reason?: string }>;
export type TranscriptPartialEvent = Envelope<
  "transcript.partial",
  null,
  { segmentId: string; revision: number; startMs: number; text: string; speaker?: string }
>;
export type TranscriptFinalEvent = Envelope<
  "transcript.final",
  Seq,
  {
    segmentId: string;
    startMs: number;
    endMs: number;
    text: string;
    confidence?: number;
    speaker?: string;
    provider: string;
  }
>;
export type TranscriptCorrectionEvent = Envelope<
  "transcript.correction",
  Seq,
  { segmentId: string; revision: number; text: string; supersedes: number }
>;
export type GapEvent = Envelope<"gap", Seq, { fromMs: number; toMs: number; reason: GapReason }>;
export type SummaryRevisionEvent = Envelope<"summary.revision", null, SummaryRevisionPayload>;
export type SummaryStatusEvent = Envelope<
  "summary.status",
  null,
  { state: SummaryJobState; lagSeconds: number }
>;
export type CatchUpReadyEvent = Envelope<"catchup.ready", null, CatchUpPayload>;
export type SttStatusEvent = Envelope<"stt.status", null, { state: SttState }>;
export type ErrorEvent = Envelope<"error", null, { code: ErrorCode; detail: string }>;

export type HubEvent =
  | SessionStateEvent
  | TranscriptPartialEvent
  | TranscriptFinalEvent
  | TranscriptCorrectionEvent
  | GapEvent
  | SummaryRevisionEvent
  | SummaryStatusEvent
  | CatchUpReadyEvent
  | SttStatusEvent
  | ErrorEvent;

/** Events that carry a non-null seq and are persisted/replayed. */
export type PersistedEvent =
  | SessionStateEvent
  | TranscriptFinalEvent
  | TranscriptCorrectionEvent
  | GapEvent;

export interface JoinRequest {
  sessionId: string;
  protocol: "lecture.v1";
  lastSeq?: Seq;
  lastSummaryRevision?: number;
}

export interface JoinResponse {
  state: SessionState;
  currentSeq: Seq;
  currentSummaryRevision: number | null;
  replay: { fromSeq: Seq | null; toSeq: Seq | null; truncated: boolean };
}

export interface TranscriptPage {
  events: TranscriptFinalEvent[];
  hasMore: boolean;
}

export function isPersistedEvent(e: HubEvent): e is PersistedEvent {
  return e.seq !== null;
}
