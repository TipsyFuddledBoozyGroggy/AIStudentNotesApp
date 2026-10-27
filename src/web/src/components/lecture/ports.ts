import type { HubEvent, JoinRequest, JoinResponse, Seq, TranscriptPage } from "@/lib/lecture";

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

export interface LectureRuntime {
  sessionId: string;
  transport: LectureTransport;
  actions: SessionActions;
  /** True while a replay/mock provider is active (contract section 12.2). */
  demoSource: boolean;
}

export type StartError = "consent_required" | "invalid_state" | "network";
export type StartResult = { ok: true; runtime: LectureRuntime } | { ok: false; error: StartError };

export interface LectureBackend {
  startSession(input: { courseId: string; consent: { acknowledged: true; textVersion: string } }): Promise<StartResult>;
}
