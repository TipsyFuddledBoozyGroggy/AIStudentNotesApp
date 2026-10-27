import type {
  CatchUpPayload,
  GapEvent,
  GapReason,
  HubEvent,
  SessionState,
  SessionStateEvent,
  SttState,
  SummaryRevisionPayload,
  TranscriptCorrectionEvent,
  TranscriptFinalEvent,
  TranscriptPartialEvent,
} from "./types";

// Small typed constructors for tests and MockLectureSource. Synthetic data only.
const TS = "2026-10-27T14:03:05.250Z";
const env = { v: 1 as const, ts: TS };

export const sessionStateEvent = (sessionId: string, seq: string, state: SessionState, reason?: string): SessionStateEvent => ({
  ...env, type: "session.state", sessionId, seq, payload: reason ? { state, reason } : { state },
});

export const finalEvent = (sessionId: string, seq: string, n: number, text: string, startMs = n * 1000): TranscriptFinalEvent => ({
  ...env, type: "transcript.final", sessionId, seq,
  payload: { segmentId: `seg_${n}`, startMs, endMs: startMs + 900, text, provider: "replay" },
});

export const partialEvent = (sessionId: string, n: number, revision: number, text: string): TranscriptPartialEvent => ({
  ...env, type: "transcript.partial", sessionId, seq: null,
  payload: { segmentId: `seg_${n}`, revision, startMs: n * 1000, text },
});

export const correctionEvent = (sessionId: string, seq: string, n: number, revision: number, text: string): TranscriptCorrectionEvent => ({
  ...env, type: "transcript.correction", sessionId, seq,
  payload: { segmentId: `seg_${n}`, revision, text, supersedes: revision - 1 },
});

export const gapEvent = (sessionId: string, seq: string, fromMs: number, toMs: number, reason: GapReason = "network"): GapEvent => ({
  ...env, type: "gap", sessionId, seq, payload: { fromMs, toMs, reason },
});

export const summaryEvent = (sessionId: string, revision: number, tldr: string, over: Partial<SummaryRevisionPayload> = {}): HubEvent => ({
  ...env, type: "summary.revision", sessionId, seq: null,
  payload: { revision, basedOnSeq: "1", tldr, bullets: [], coveredFromMs: 0, coveredToMs: 1000, status: "Draft", citations: [], ...over },
});

export const summaryStatusEvent = (sessionId: string, state: "Pending" | "Running" | "Delayed" | "Failed", lagSeconds = 0): HubEvent => ({
  ...env, type: "summary.status", sessionId, seq: null, payload: { state, lagSeconds },
});

export const sttStatusEvent = (sessionId: string, state: SttState): HubEvent => ({
  ...env, type: "stt.status", sessionId, seq: null, payload: { state },
});

export const catchUpEvent = (sessionId: string, over: Partial<CatchUpPayload> = {}): HubEvent => ({
  ...env, type: "catchup.ready", sessionId, seq: null,
  payload: { catchUpId: "cu_1", windowMinutes: 5, text: "Recap", citations: [], kind: "Extractive", ...over },
});
