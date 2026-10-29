import type {
  CatchUpAiState,
  CatchUpClaim,
  CatchUpDetail,
  CatchUpPayload,
  CatchUpTerm,
  Citation,
  ClaimBasis,
  SummaryRevisionPayload,
} from "./types";

// Parsers for REST AI payloads (GET /summary, POST /quick-recap, GET /catch-ups/{id}). Same philosophy as
// realtime/eventGuard.ts: check what the UI reads, copy only known fields (extras are dropped, never rendered),
// and return null for anything malformed so a bad body is treated as "unavailable", never half-rendered.

const isObj = (v: unknown): v is Record<string, unknown> => typeof v === "object" && v !== null && !Array.isArray(v);
const isStr = (v: unknown): v is string => typeof v === "string";
const isInt = (v: unknown): v is number => typeof v === "number" && Number.isInteger(v) && v >= 0;

const BASES = new Set<string>(["lecture", "general_knowledge", "uncertain"]);
const AI_STATES = new Set<string>(["Pending", "Ready", "NotNeeded", "Unavailable", "BudgetLimited"]);

function citations(v: unknown): Citation[] | null {
  if (!Array.isArray(v)) return null;
  const out: Citation[] = [];
  for (const c of v) {
    if (!isObj(c) || !isStr(c.segmentId) || !isInt(c.startMs)) return null;
    out.push({ segmentId: c.segmentId, startMs: c.startMs });
  }
  return out;
}

function claim(v: unknown): CatchUpClaim | null {
  if (!isObj(v) || !isStr(v.text) || !isStr(v.basis) || !BASES.has(v.basis)) return null;
  const c = citations(v.citations);
  return c ? { text: v.text, basis: v.basis as ClaimBasis, citations: c } : null;
}

function list<T>(v: unknown, one: (x: unknown) => T | null): T[] | undefined {
  if (!Array.isArray(v)) return undefined;
  return v.map(one).filter((x): x is T => x !== null);
}

export function parseSummary(v: unknown): SummaryRevisionPayload | null {
  if (!isObj(v)) return null;
  const c = citations(v.citations);
  if (
    !c ||
    !isInt(v.revision) ||
    v.revision < 1 ||
    !isStr(v.basedOnSeq) ||
    !isStr(v.tldr) ||
    !Array.isArray(v.bullets) ||
    !v.bullets.every(isStr) ||
    !isInt(v.coveredFromMs) ||
    !isInt(v.coveredToMs) ||
    (v.status !== "Draft" && v.status !== "Final")
  )
    return null;
  return {
    revision: v.revision,
    basedOnSeq: v.basedOnSeq,
    tldr: v.tldr,
    bullets: v.bullets as string[],
    coveredFromMs: v.coveredFromMs,
    coveredToMs: v.coveredToMs,
    status: v.status,
    citations: c,
    ...(v.source === "mock" || v.source === "model" ? { source: v.source } : {}),
  };
}

export function parseCatchUp(v: unknown): CatchUpPayload | null {
  if (!isObj(v)) return null;
  const c = citations(v.citations);
  if (
    !c ||
    !isStr(v.catchUpId) ||
    !/^[A-Za-z0-9_-]{1,64}$/.test(v.catchUpId) ||
    (v.windowMinutes !== 2 && v.windowMinutes !== 5 && v.windowMinutes !== 10) ||
    !isStr(v.text) ||
    (v.kind !== "Extractive" && v.kind !== "Ai")
  )
    return null;
  const out: CatchUpPayload = { catchUpId: v.catchUpId, windowMinutes: v.windowMinutes, text: v.text, citations: c, kind: v.kind };
  if (v.source === "mock" || v.source === "model") out.source = v.source;
  if (isStr(v.aiState) && AI_STATES.has(v.aiState)) out.aiState = v.aiState as CatchUpAiState;
  if (isInt(v.coveredFromMs)) out.coveredFromMs = v.coveredFromMs;
  if (isInt(v.coveredToMs)) out.coveredToMs = v.coveredToMs;
  const now = claim(v.nowDiscussing);
  if (now) out.nowDiscussing = now;
  const short = list(v.shortCatchUp, claim);
  if (short) out.shortCatchUp = short;
  const detail = list(v.detail, (x): CatchUpDetail | null => {
    const base = claim(x);
    if (!base) return null;
    const why = (x as Record<string, unknown>).whyItMatters;
    return isStr(why) ? { ...base, whyItMatters: why } : base;
  });
  if (detail) out.detail = detail;
  const terms = list(v.keyTerms, (x): CatchUpTerm | null => {
    if (!isObj(x) || !isStr(x.term) || !isStr(x.basis) || !BASES.has(x.basis)) return null;
    const tc = citations(x.citations);
    if (!tc) return null;
    return { term: x.term, basis: x.basis as ClaimBasis, citations: tc, ...(isStr(x.definition) ? { definition: x.definition } : {}) };
  });
  if (terms) out.keyTerms = terms;
  if (Array.isArray(v.clarifyingQuestions)) out.clarifyingQuestions = v.clarifyingQuestions.filter(isStr);
  if (v.confidence === "low" || v.confidence === "medium" || v.confidence === "high") out.confidence = v.confidence;
  if (Array.isArray(v.gaps)) out.gaps = v.gaps.filter(isStr);
  return out;
}
