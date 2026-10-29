import type { CatchUpPayload, HubEvent, JoinRequest, JoinResponse, Seq, SummaryRevisionPayload, TranscriptPage, WindowMinutes } from "@/lib/lecture";
import type { AudioLink } from "@/lib/lecture/realtime/audioStreamer";

/** Hub + REST-paging transport. The harness wraps MockLectureSource to satisfy this; the real hub comes later. */
export interface LectureTransport {
  subscribe(handler: (e: HubEvent) => void): () => void;
  /** Opens the transport; rejects when it cannot. */
  connect(): Promise<void>;
  join(req: JoinRequest): Promise<JoinResponse>;
  fetchTranscript(afterSeq: Seq | null, limit: number): Promise<TranscriptPage>;
  /** Transport reported a drop. */
  onDisconnect(cb: () => void): () => void;
}

/** REST control actions. State changes are observed as hub events, never assumed from the response. */
export interface SessionActions {
  pause(): Promise<void>;
  resume(): Promise<void>;
  stop(): Promise<void>;
}

/**
 * REST AI surface (summary, Quick recap, catch-up poll). Failures reject with ApiError (status 0 = network).
 * Results are already parsed and plain-data only; the UI renders them as text nodes.
 */
export interface LectureAi {
  /** Latest summary revision; null when there is none yet (404). */
  getSummary(): Promise<SummaryRevisionPayload | null>;
  /** One press = one idempotencyKey. */
  startQuickRecap(windowMinutes: WindowMinutes, idempotencyKey: string): Promise<CatchUpPayload>;
  /** null when the catch-up is unknown (404). */
  getCatchUp(catchUpId: string): Promise<CatchUpPayload | null>;
}

export interface LectureRuntime {
  sessionId: string;
  transport: LectureTransport;
  actions: SessionActions;
  /** AI routes. Absent: the Quick recap panel is not shown and no summary is fetched over REST. */
  ai?: LectureAi;
  /** True while a replay/mock provider is active (contract section 12.2). */
  demoSource: boolean;
  /** Upstream audio (real stack only). Absent in the harness. */
  audio?: AudioLink;
  /** Releases the transport and audio. Called when the screen unmounts. */
  dispose?(): void;
}

export type StartError = "consent_required" | "invalid_state" | "network";
export type StartResult = { ok: true; runtime: LectureRuntime } | { ok: false; error: StartError };

export interface LectureBackend {
  startSession(input: { courseId: string; consent: { acknowledged: true; textVersion: string } }): Promise<StartResult>;
}
