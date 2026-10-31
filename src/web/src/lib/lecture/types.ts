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
  /** mock = deterministic demo text, not a language model. Absent on older servers. */
  source?: AiSource;
}

export type AiSource = "mock" | "model";
export type ClaimBasis = "lecture" | "general_knowledge" | "uncertain";
export type CatchUpAiState = "Pending" | "Ready" | "NotNeeded" | "Unavailable" | "BudgetLimited";

export interface CatchUpClaim {
  text: string;
  basis: ClaimBasis;
  citations: Citation[];
}
export interface CatchUpDetail extends CatchUpClaim {
  whyItMatters?: string;
}
export interface CatchUpTerm {
  term: string;
  definition?: string;
  basis: ClaimBasis;
  citations: Citation[];
}

export interface CatchUpPayload {
  catchUpId: string;
  windowMinutes: WindowMinutes;
  text: string;
  citations: Citation[];
  kind: "Extractive" | "Ai";
  /** Only on kind=Ai. */
  source?: AiSource;
  /** Server fields beyond catchup.schema.json (reported to the Lead Architect); optional and checked before use. */
  aiState?: CatchUpAiState;
  coveredFromMs?: number;
  coveredToMs?: number;
  nowDiscussing?: CatchUpClaim;
  shortCatchUp?: CatchUpClaim[];
  detail?: CatchUpDetail[];
  keyTerms?: CatchUpTerm[];
  clarifyingQuestions?: string[];
  confidence?: "low" | "medium" | "high";
  gaps?: string[];
}

/** Notes made by one deliberate press after Stop (PO-18d; lecture.v1/notes.schema.json, API_CONTRACTS section 17). */
export type NotesStatus = "Pending" | "Ready" | "Unavailable" | "BudgetLimited" | "NotNeeded";
export type FinalSummaryStatus = "Ready" | "Unavailable" | "BudgetLimited" | "NotNeeded";

export interface NotesBullet {
  /** Untrusted plain text (C-28). */
  text: string;
  /** 1 to 8, each a stored final segment of the same session. */
  citations: Citation[];
}

export interface NotesSection {
  /** Untrusted plain text (C-28). */
  heading: string;
  bullets: NotesBullet[];
}

export interface LectureNotes {
  notesId: string;
  revision: number;
  status: NotesStatus;
  /** Only on Ready. */
  source: AiSource | null;
  requestedAt: string;
  generatedAt: string | null;
  coveredFromMs: number | null;
  coveredToMs: number | null;
  /** Only Ready has sections (at least one). */
  sections: NotesSection[];
  /** The Final summary revision made by the same press; set exactly when finalSummaryStatus is Ready. */
  finalSummaryRevision: number | null;
  /** Only on Ready (null otherwise). */
  finalSummaryStatus: FinalSummaryStatus | null;
  truncated: boolean;
}

/** Body of POST /sessions/{id}/notes (notes-request.schema.json): exactly this one member. */
export interface NotesRequest {
  noticeVersion: string;
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

/** GET /sessions/{id}/transcript returns every persisted event after afterSeq (API_CONTRACTS section 13.1). */
export interface TranscriptPage {
  events: PersistedEvent[];
  hasMore: boolean;
}

export function isPersistedEvent(e: HubEvent): e is PersistedEvent {
  return e.seq !== null;
}
