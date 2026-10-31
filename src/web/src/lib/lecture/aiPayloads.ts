import type {
  CatchUpAiState,
  CatchUpClaim,
  CatchUpDetail,
  CatchUpPayload,
  CatchUpTerm,
  Citation,
  ClaimBasis,
  FinalSummaryStatus,
  LectureNotes,
  NotesBullet,
  NotesRequest,
  NotesSection,
  NotesStatus,
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

// --- Notes after Stop (PO-18d; lecture.v1/notes.schema.json, API_CONTRACTS section 17) ------------------------------
// STRICT: every rule of the schema the UI could be misled by is checked here (required members, value shapes, lengths in
// code points, the Ready / not-Ready if-then-else and the finalSummaryStatus / finalSummaryRevision pairing). Unknown
// members are dropped at every level (never rendered); anything else malformed returns null so the caller shows a calm
// error instead of half a sheet. notesContract.test.ts runs every notes fixture through this parser and through Ajv.

const NOTES_STATUSES = new Set<string>(["Pending", "Ready", "Unavailable", "BudgetLimited", "NotNeeded"]);
const FINAL_STATUSES = new Set<string>(["Ready", "Unavailable", "BudgetLimited", "NotNeeded"]);
const TIMESTAMP_RE = /^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]+)?(Z|[+-][0-9]{2}:[0-9]{2})$/;
const NOTES_ID_RE = /^nt_[0-9a-f]{32}$/;
const SEGMENT_ID_RE = /^seg_[0-9]+$/;
const REVISION_MAX = 2147483647;

const isMs = (v: unknown): v is number => isInt(v) && v <= Number.MAX_SAFE_INTEGER;
const isRevision = (v: unknown): v is number => isInt(v) && v >= 1 && v <= REVISION_MAX;
/** Length in code points (the schema's maxLength under a code-point engine). */
const cpLen = (s: string) => Array.from(s).length;
const has = (o: Record<string, unknown>, k: string) => Object.prototype.hasOwnProperty.call(o, k);

function notesCitations(v: unknown): Citation[] | null {
  if (!Array.isArray(v) || v.length < 1 || v.length > 8) return null;
  const out: Citation[] = [];
  for (const c of v) {
    if (!isObj(c) || !isStr(c.segmentId) || c.segmentId.length > 32 || !SEGMENT_ID_RE.test(c.segmentId) || !isMs(c.startMs)) return null;
    out.push({ segmentId: c.segmentId, startMs: c.startMs });
  }
  return out;
}

function notesBullet(v: unknown): NotesBullet | null {
  if (!isObj(v) || !isStr(v.text) || cpLen(v.text) < 1 || cpLen(v.text) > 500) return null;
  const citations = notesCitations(v.citations);
  return citations ? { text: v.text, citations } : null;
}

function notesSection(v: unknown): NotesSection | null {
  if (!isObj(v) || !isStr(v.heading) || cpLen(v.heading) < 1 || cpLen(v.heading) > 120) return null;
  if (!Array.isArray(v.bullets) || v.bullets.length < 1 || v.bullets.length > 12) return null;
  const bullets: NotesBullet[] = [];
  for (const b of v.bullets) {
    const one = notesBullet(b);
    if (!one) return null;
    bullets.push(one);
  }
  return { heading: v.heading, bullets };
}

const REQUIRED_NOTES_MEMBERS = [
  "notesId",
  "revision",
  "status",
  "source",
  "requestedAt",
  "generatedAt",
  "coveredFromMs",
  "coveredToMs",
  "sections",
  "finalSummaryRevision",
  "finalSummaryStatus",
  "truncated",
] as const;

export function parseNotes(v: unknown): LectureNotes | null {
  if (!isObj(v)) return null;
  for (const k of REQUIRED_NOTES_MEMBERS) if (!has(v, k)) return null;
  if (!isStr(v.notesId) || !NOTES_ID_RE.test(v.notesId)) return null;
  if (!isRevision(v.revision)) return null;
  if (!isStr(v.status) || !NOTES_STATUSES.has(v.status)) return null;
  if (!(v.source === null || v.source === "mock" || v.source === "model")) return null;
  if (!isStr(v.requestedAt) || !TIMESTAMP_RE.test(v.requestedAt)) return null;
  if (!(v.generatedAt === null || (isStr(v.generatedAt) && TIMESTAMP_RE.test(v.generatedAt)))) return null;
  if (!(v.coveredFromMs === null || isMs(v.coveredFromMs))) return null;
  if (!(v.coveredToMs === null || isMs(v.coveredToMs))) return null;
  if (!Array.isArray(v.sections) || v.sections.length > 50) return null;
  if (!(v.finalSummaryRevision === null || isRevision(v.finalSummaryRevision))) return null;
  if (!(v.finalSummaryStatus === null || (isStr(v.finalSummaryStatus) && FINAL_STATUSES.has(v.finalSummaryStatus)))) return null;
  if (typeof v.truncated !== "boolean") return null;

  const sections: NotesSection[] = [];
  for (const s of v.sections) {
    const one = notesSection(s);
    if (!one) return null;
    sections.push(one);
  }
  const status = v.status as NotesStatus;
  if (status === "Ready") {
    // Only Ready carries content: a source, a time, a covered range, at least one section and a Final summary status.
    if (v.source === null || v.generatedAt === null || v.coveredFromMs === null || v.coveredToMs === null) return null;
    if (sections.length < 1 || v.finalSummaryStatus === null) return null;
  } else if (
    v.source !== null ||
    v.generatedAt !== null ||
    v.coveredFromMs !== null ||
    v.coveredToMs !== null ||
    sections.length > 0 ||
    v.finalSummaryRevision !== null ||
    v.finalSummaryStatus !== null ||
    v.truncated !== false
  ) {
    return null;
  }
  // finalSummaryRevision is set exactly when the Final step is Ready.
  if ((v.finalSummaryStatus === "Ready") !== (v.finalSummaryRevision !== null)) return null;

  return {
    notesId: v.notesId,
    revision: v.revision,
    status,
    source: v.source,
    requestedAt: v.requestedAt,
    generatedAt: v.generatedAt as string | null,
    coveredFromMs: v.coveredFromMs as number | null,
    coveredToMs: v.coveredToMs as number | null,
    sections,
    finalSummaryRevision: v.finalSummaryRevision as number | null,
    finalSummaryStatus: v.finalSummaryStatus as FinalSummaryStatus | null,
    truncated: v.truncated,
  };
}

/** Body of POST /sessions/{id}/notes: exactly `{ noticeVersion }` (notes-request.schema.json; `{}` is refused). */
export function notesRequestBody(noticeVersion: string): NotesRequest {
  return { noticeVersion };
}
