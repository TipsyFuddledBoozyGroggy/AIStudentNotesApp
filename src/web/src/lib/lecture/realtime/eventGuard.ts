import { parseCatchUp, parseSummary } from "../aiPayloads";
import type { HubEvent, JoinResponse, PersistedEvent, TranscriptPage } from "../types";

// Structural guard for data arriving from the network. The JSON Schemas under src/shared/contracts remain the
// source of truth and are enforced in tests (eventGuard.test.ts runs every valid fixture through this guard);
// Ajv is not run in the browser because it compiles code with `new Function` (unsafe-eval under a strict CSP).
// This guard checks the envelope, the type discriminator and the field types the UI reads. Unknown extra
// fields are ignored, never rendered.

const isObj = (v: unknown): v is Record<string, unknown> => typeof v === "object" && v !== null && !Array.isArray(v);
const isStr = (v: unknown): v is string => typeof v === "string";
const isInt = (v: unknown): v is number => typeof v === "number" && Number.isInteger(v);
const isSeq = (v: unknown): v is string => isStr(v) && /^[1-9][0-9]{0,18}$/.test(v);

const REASONS = new Set(["paused", "network", "device", "provider"]);
const SESSION_STATES = new Set(["Active", "Paused", "Stopped", "Failed"]);

function payloadOk(type: string, p: Record<string, unknown>): boolean {
  switch (type) {
    case "session.state":
      return isStr(p.state) && SESSION_STATES.has(p.state) && (p.reason === undefined || isStr(p.reason));
    case "transcript.partial":
      return isStr(p.segmentId) && isInt(p.revision) && isInt(p.startMs) && isStr(p.text);
    case "transcript.final":
      return isStr(p.segmentId) && isInt(p.startMs) && isInt(p.endMs) && isStr(p.text) && isStr(p.provider);
    case "transcript.correction":
      return isStr(p.segmentId) && isInt(p.revision) && isStr(p.text) && isInt(p.supersedes);
    case "gap":
      return isInt(p.fromMs) && isInt(p.toMs) && isStr(p.reason) && REASONS.has(p.reason);
    case "summary.revision":
      return isInt(p.revision) && isStr(p.tldr) && Array.isArray(p.bullets) && p.bullets.every(isStr) && isInt(p.coveredFromMs) && isInt(p.coveredToMs);
    case "summary.status":
      return isStr(p.state) && typeof p.lagSeconds === "number";
    case "catchup.ready":
      return isStr(p.catchUpId) && isStr(p.text) && (p.kind === "Extractive" || p.kind === "Ai");
    case "stt.status":
      return isStr(p.state);
    case "error":
      return isStr(p.code) && isStr(p.detail);
    default:
      return false;
  }
}

const PERSISTED = new Set(["session.state", "transcript.final", "transcript.correction", "gap"]);

export function isHubEvent(v: unknown): v is HubEvent {
  if (!isObj(v) || v.v !== 1 || !isStr(v.type) || !isStr(v.sessionId) || !isStr(v.ts) || !isObj(v.payload)) return false;
  if (PERSISTED.has(v.type) ? !isSeq(v.seq) : v.seq !== null) return false;
  return payloadOk(v.type, v.payload);
}

/**
 * Network entry point for hub events. Runs the structural guard, then re-parses AI payloads (summary.revision,
 * catchup.ready) with the strict parsers used for REST: unknown fields are dropped and a malformed nested field
 * makes the whole event null, so the caller ignores it silently (no state change, no announcement).
 */
export function parseHubEvent(v: unknown): HubEvent | null {
  if (!isHubEvent(v)) return null;
  if (v.type === "summary.revision") {
    const payload = parseSummary(v.payload);
    return payload ? { ...v, payload } : null;
  }
  if (v.type === "catchup.ready") {
    const payload = parseCatchUp(v.payload);
    return payload ? { ...v, payload } : null;
  }
  return v;
}

export function isPersistedEvent(v: unknown): v is PersistedEvent {
  return isHubEvent(v) && PERSISTED.has(v.type);
}

export function isTranscriptPage(v: unknown): v is TranscriptPage {
  return isObj(v) && typeof v.hasMore === "boolean" && Array.isArray(v.events) && v.events.length <= 500 && v.events.every(isPersistedEvent);
}

export function isJoinResponse(v: unknown): v is JoinResponse {
  if (!isObj(v) || !isStr(v.state) || !SESSION_STATES.has(v.state) || !isStr(v.currentSeq) || !/^[0-9]{1,19}$/.test(v.currentSeq)) return false;
  if (!(v.currentSummaryRevision === null || isInt(v.currentSummaryRevision))) return false;
  const r = v.replay;
  return isObj(r) && typeof r.truncated === "boolean" && (r.fromSeq === null || isStr(r.fromSeq)) && (r.toSeq === null || isStr(r.toSeq));
}
