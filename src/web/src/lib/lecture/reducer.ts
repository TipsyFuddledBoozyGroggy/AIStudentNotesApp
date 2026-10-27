import { compareSeq, maxSeq, nextSeq } from "./seq";
import type {
  CatchUpPayload,
  GapReason,
  HubEvent,
  JoinResponse,
  Seq,
  SessionState,
  SttState,
  SummaryJobState,
  SummaryRevisionPayload,
  TranscriptFinalEvent,
} from "./types";

/**
 * Pure lecture-state reducer. No I/O, no clocks, no mutation of inputs.
 *
 * Persisted events (session.state, transcript.final, transcript.correction, gap) are deduped on
 * (sessionId, seq) with seq compared as BigInt. They may arrive out of order; every handler is
 * order-independent (corrections that arrive before their final are kept and merged later).
 * Ephemeral events (seq null) are never deduped by seq: partials and summaries use revisions.
 */

export interface TranscriptSegment {
  segmentId: string;
  seq: Seq;
  startMs: number;
  endMs: number;
  /** Original final text, never overwritten (provenance, C-28 / contract section 3). */
  originalText: string;
  provider: string;
  confidence?: number;
  /** Latest correction, if any. Display text is `correction?.text ?? originalText`. */
  correction?: { revision: number; text: string; seq: Seq };
  /** Every correction received, ascending by revision. */
  corrections: { revision: number; supersedes: number; text: string; seq: Seq }[];
  /** Finals only: safe to announce to assistive tech (throttling is the UI's job). */
  announceable: true;
}

export interface PartialSegment {
  segmentId: string;
  revision: number;
  startMs: number;
  text: string;
  /** Partials are never announced to assistive tech (contract section 3). */
  announceable: false;
}

export interface GapRecord {
  seq: Seq;
  fromMs: number;
  toMs: number;
  reason: GapReason;
}

export interface LectureState {
  sessionId: string | null;
  sessionState: SessionState | null;
  sessionStateReason: string | null;
  /** seq of the session.state event that produced sessionState (older ones are ignored). */
  sessionStateSeq: Seq | null;
  sttStatus: SttState | null;
  summaryStatus: { state: SummaryJobState; lagSeconds: number } | null;
  summary: SummaryRevisionPayload | null;
  /** Finals keyed by segmentId. Use selectSegments() for ordered display. */
  segments: Record<string, TranscriptSegment>;
  /** Corrections that arrived before their final. */
  pendingCorrections: Record<string, { revision: number; supersedes: number; text: string; seq: Seq }[]>;
  partials: Record<string, PartialSegment>;
  gaps: GapRecord[];
  catchUps: Record<string, CatchUpPayload>;
  lastError: { code: string; detail: string } | null;
  /** Persisted seqs applied (dedupe set). */
  seen: Record<Seq, true>;
  /** Highest seq such that every seq <= it has been applied (null before any baseline). */
  contiguousSeq: Seq | null;
  /** Highest persisted seq ever seen. maxSeq > contiguousSeq means holes: request replay. */
  maxSeq: Seq | null;
  /** True after fastForward(): non-final persisted events in the skipped range may be missing. */
  possiblyMissedNonFinalEvents: boolean;
}

export function initialLectureState(sessionId: string | null = null): LectureState {
  return {
    sessionId,
    sessionState: null,
    sessionStateReason: null,
    sessionStateSeq: null,
    sttStatus: null,
    summaryStatus: null,
    summary: null,
    segments: {},
    pendingCorrections: {},
    partials: {},
    gaps: [],
    catchUps: {},
    lastError: null,
    seen: {},
    contiguousSeq: null,
    maxSeq: null,
    possiblyMissedNonFinalEvents: false,
  };
}

export type LectureAction =
  | { kind: "event"; event: HubEvent }
  | { kind: "events"; events: HubEvent[] }
  /** Result of Hub Join. Sets the baseline so seq holes can be detected. */
  | { kind: "joined"; response: JoinResponse; lastSeqSent: Seq | null }
  /** After REST paging fallback: treat everything through `throughSeq` as caught up. */
  | { kind: "fastForward"; throughSeq: Seq }
  | { kind: "reset"; sessionId: string | null };

function advanceContiguous(state: LectureState): Seq | null {
  let c = state.contiguousSeq;
  if (c === null) return null;
  for (;;) {
    const n = nextSeq(c);
    if (!state.seen[n]) return c;
    c = n;
  }
}

function markSeen(state: LectureState, seq: Seq): LectureState {
  const seen = { ...state.seen, [seq]: true as const };
  let contiguousSeq = state.contiguousSeq;
  // With no baseline the first persisted event becomes the baseline (seq may start at 0 or later).
  if (contiguousSeq === null) contiguousSeq = seq;
  const next: LectureState = { ...state, seen, maxSeq: maxSeq(state.maxSeq, seq), contiguousSeq };
  return { ...next, contiguousSeq: advanceContiguous(next) };
}

function withCorrections(seg: TranscriptSegment, extra: { revision: number; supersedes: number; text: string; seq: Seq }[]) {
  if (extra.length === 0) return seg;
  const all = [...seg.corrections];
  for (const c of extra) if (!all.some((x) => x.seq === c.seq)) all.push(c);
  all.sort((a, b) => a.revision - b.revision);
  return { ...seg, corrections: all, correction: all.length ? all[all.length - 1] : undefined };
}

function applyEvent(state: LectureState, e: HubEvent): LectureState {
  if (e.v !== 1) return state;
  if (state.sessionId !== null && e.sessionId !== state.sessionId) return state; // (sessionId, seq) scoping
  const base = state.sessionId === null ? { ...state, sessionId: e.sessionId } : state;

  if (e.seq !== null) {
    if (base.seen[e.seq]) return base; // duplicate
  }

  switch (e.type) {
    case "session.state": {
      let s = markSeen(base, e.seq);
      if (s.sessionStateSeq === null || compareSeq(e.seq, s.sessionStateSeq) > 0) {
        s = { ...s, sessionState: e.payload.state, sessionStateReason: e.payload.reason ?? null, sessionStateSeq: e.seq };
        if (e.payload.state !== "Active") s = { ...s, partials: {} }; // C-11: cleared on pause/stop
      }
      return s;
    }
    case "transcript.final": {
      const p = e.payload;
      let s = markSeen(base, e.seq);
      const existing = s.segments[p.segmentId];
      if (existing) return s; // finals are immutable; a second final for one segment is ignored
      const pending = s.pendingCorrections[p.segmentId] ?? [];
      const seg = withCorrections(
        {
          segmentId: p.segmentId,
          seq: e.seq,
          startMs: p.startMs,
          endMs: p.endMs,
          originalText: p.text,
          provider: p.provider,
          ...(p.confidence !== undefined ? { confidence: p.confidence } : {}),
          corrections: [],
          announceable: true,
        },
        pending,
      );
      const { [p.segmentId]: _drop1, ...restPartials } = s.partials;
      const { [p.segmentId]: _drop2, ...restPending } = s.pendingCorrections;
      void _drop1;
      void _drop2;
      s = { ...s, segments: { ...s.segments, [p.segmentId]: seg }, partials: restPartials, pendingCorrections: restPending };
      return s;
    }
    case "transcript.correction": {
      const p = e.payload;
      const s = markSeen(base, e.seq);
      const c = { revision: p.revision, supersedes: p.supersedes, text: p.text, seq: e.seq };
      const seg = s.segments[p.segmentId];
      if (seg) return { ...s, segments: { ...s.segments, [p.segmentId]: withCorrections(seg, [c]) } };
      const list = s.pendingCorrections[p.segmentId] ?? [];
      return { ...s, pendingCorrections: { ...s.pendingCorrections, [p.segmentId]: [...list, c] } };
    }
    case "gap": {
      const s = markSeen(base, e.seq);
      if (s.gaps.some((g) => g.seq === e.seq)) return s;
      return { ...s, gaps: [...s.gaps, { seq: e.seq, ...e.payload }] };
    }
    case "transcript.partial": {
      const p = e.payload;
      if (base.segments[p.segmentId]) return base; // already final
      const cur = base.partials[p.segmentId];
      if (cur && p.revision <= cur.revision) return base;
      if (base.sessionState !== null && base.sessionState !== "Active") return base; // paused/stopped: no partials
      return {
        ...base,
        partials: {
          ...base.partials,
          [p.segmentId]: { segmentId: p.segmentId, revision: p.revision, startMs: p.startMs, text: p.text, announceable: false },
        },
      };
    }
    case "summary.revision": {
      if (base.summary && e.payload.revision <= base.summary.revision) return base;
      return { ...base, summary: e.payload };
    }
    case "summary.status":
      return { ...base, summaryStatus: { state: e.payload.state, lagSeconds: e.payload.lagSeconds } };
    case "stt.status":
      return { ...base, sttStatus: e.payload.state };
    case "catchup.ready": {
      const cur = base.catchUps[e.payload.catchUpId];
      // An AI recap is never replaced by a later extractive one for the same id.
      if (cur && cur.kind === "Ai" && e.payload.kind === "Extractive") return base;
      return { ...base, catchUps: { ...base.catchUps, [e.payload.catchUpId]: e.payload } };
    }
    case "error":
      return { ...base, lastError: { code: e.payload.code, detail: e.payload.detail } };
    default:
      return base; // unknown type: ignore (forward compatibility)
  }
}

export function lectureReducer(state: LectureState, action: LectureAction): LectureState {
  switch (action.kind) {
    case "event":
      return applyEvent(state, action.event);
    case "events":
      return action.events.reduce(applyEvent, state);
    case "joined": {
      const { response } = action;
      let s = state;
      if (action.lastSeqSent !== null && s.contiguousSeq === null) {
        s = { ...s, contiguousSeq: action.lastSeqSent, maxSeq: maxSeq(s.maxSeq, action.lastSeqSent) };
      }
      s = { ...s, maxSeq: maxSeq(s.maxSeq, response.currentSeq) };
      // Server state is authoritative at join; the stream's session.state events refine it by seq.
      if (s.sessionState === null) s = { ...s, sessionState: response.state };
      return s;
    }
    case "fastForward": {
      return {
        ...state,
        contiguousSeq: state.contiguousSeq !== null && compareSeq(state.contiguousSeq, action.throughSeq) > 0 ? state.contiguousSeq : action.throughSeq,
        maxSeq: maxSeq(state.maxSeq, action.throughSeq),
        possiblyMissedNonFinalEvents: true,
      };
    }
    case "reset":
      return initialLectureState(action.sessionId);
  }
}

/** True when persisted events are missing below the highest seq seen (ask the server to replay). */
export function hasSeqHoles(state: LectureState): boolean {
  return state.maxSeq !== null && state.contiguousSeq !== null && compareSeq(state.maxSeq, state.contiguousSeq) > 0;
}

/** Resume point for Join / GET /transcript?afterSeq: the contiguous frontier, not the max. */
export function resumeSeq(state: LectureState): Seq | null {
  return state.contiguousSeq;
}

/** Finals ordered by startMs then seq; display text applies the latest correction. */
export function selectSegments(state: LectureState): (TranscriptSegment & { text: string; corrected: boolean })[] {
  return Object.values(state.segments)
    .sort((a, b) => a.startMs - b.startMs || compareSeq(a.seq, b.seq))
    .map((s) => ({ ...s, text: s.correction?.text ?? s.originalText, corrected: s.correction !== undefined }));
}

/** The only transcript content that may be announced to assistive tech. */
export function selectAnnounceable(state: LectureState) {
  return selectSegments(state);
}

export function selectPartials(state: LectureState): PartialSegment[] {
  return Object.values(state.partials).sort((a, b) => a.startMs - b.startMs);
}

export type { TranscriptFinalEvent };
