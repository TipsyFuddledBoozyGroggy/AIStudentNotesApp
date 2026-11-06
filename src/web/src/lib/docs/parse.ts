import type {
  BlockedReason,
  DemoDoc,
  DocBlock,
  DocDocument,
  DocRef,
  DocsErrorCode,
  DocsExisting,
  DocsExportRecord,
  DocsMode,
  DocsPreview,
  DocsProblem,
  DocsRecordError,
  DocsState,
  RecordErrorCode,
} from "./types";

// Strict parsers for docs.export.v1 response bodies (finish-backlog item 3, task F1). Unlike the syllabus parsers, an
// unknown member is REFUSED at every level (the contract is additionalProperties: false everywhere and a stray member such
// as a token or a title in a record must never reach a component). The `external` docRef branch is refused too: it is
// reserved for a real provider that is not built or authorised (contract section 7). Anything malformed returns null and
// the caller shows a calm "could not show this" state, never partial data. These checks are a client guard, not
// TextPolicy: the server is authoritative, and text is still rendered only as plain text nodes.

type Obj = Record<string, unknown>;
const isObj = (v: unknown): v is Obj => typeof v === "object" && v !== null && !Array.isArray(v);
const isStr = (v: unknown): v is string => typeof v === "string";
const isBool = (v: unknown): v is boolean => typeof v === "boolean";
const isInt = (v: unknown, min: number, max: number): v is number => typeof v === "number" && Number.isInteger(v) && v >= min && v <= max;
const oneOf =
  <T extends string>(values: readonly T[]) =>
  (v: unknown): v is T =>
    typeof v === "string" && (values as readonly string[]).includes(v);

/** Exactly these members: every required one present, nothing else. */
function exact(o: Obj, required: readonly string[], optional: readonly string[] = []): boolean {
  for (const k of required) if (!(k in o)) return false;
  for (const k of Object.keys(o)) if (!required.includes(k) && !optional.includes(k)) return false;
  return true;
}

// Patterns from docs-common.schema.json, with the same end anchor (no `$`, which matches before a final LF).
const GUID = "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}";
const SESSION_ID = new RegExp(`^${GUID}(?![\\s\\S])`);
const EXPORT_ID = new RegExp(`^dxp_${GUID}(?![\\s\\S])`);
const DOC_ID = new RegExp(`^ddc_${GUID}(?![\\s\\S])`);
const OPEN_PATH = new RegExp(`^/demo-docs/ddc_${GUID}(?![\\s\\S])`);
const NOTES_ID = /^nt_[0-9a-f]{32}(?![\s\S])/;
const DIGEST = /^cd_[A-Za-z0-9_-]{43}(?![\s\S])/;
const TIMESTAMP = /^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]+)?(Z|[+-][0-9]{2}:[0-9]{2})(?![\s\S])/;
// C0, DEL, NEL, LINE SEPARATOR and PARAGRAPH SEPARATOR (the last two built from code points so no editor folds them).
const LS_PS = String.fromCharCode(0x2028, 0x2029);
const ONE_LINE = new RegExp(`^[^\\u0000-\\u001f\\u007f\\u0085${LS_PS}]+(?![\\s\\S])`);
const PROBLEM_TYPE = /^https:\/\/errors\.tether\.invalid\/[a-z_]+(?![\s\S])/;
const PROBLEM_DETAIL = /^[^\u0000-\u001f]*(?![\s\S])/;
const ERROR_KEY = /^[A-Za-z][A-Za-z0-9_.[\]-]{0,63}(?![\s\S])/;

export const isSessionId = (v: unknown): v is string => isStr(v) && SESSION_ID.test(v);
export const isExportId = (v: unknown): v is string => isStr(v) && EXPORT_ID.test(v);
export const isDocId = (v: unknown): v is string => isStr(v) && DOC_ID.test(v);
export const isNotesId = (v: unknown): v is string => isStr(v) && NOTES_ID.test(v);
export const isContentDigest = (v: unknown): v is string => isStr(v) && DIGEST.test(v);
const isTimestamp = (v: unknown): v is string => isStr(v) && TIMESTAMP.test(v);
const cps = (s: string) => [...s].length;
const isLine = (max: number) => (v: unknown): v is string => isStr(v) && cps(v) >= 1 && cps(v) <= max && ONE_LINE.test(v);
const isTitle = isLine(200);
const isBlockText = isLine(500);
const isVersion = (v: unknown): v is number => isInt(v, 1, 2147483647);
const MS_MAX = 9007199254740991;

const MODES = ["create", "update", "create_another"] as const satisfies readonly DocsMode[];
const isMode = oneOf(MODES);
const RECORD_CODES = [
  "provider_unavailable",
  "rate_limited",
  "provider_rejected",
  "reconnect_required",
  "doc_changed_outside",
  "quota_exceeded",
] as const satisfies readonly RecordErrorCode[];
const ERROR_CODES = [
  "validation_failed",
  "unauthenticated",
  "csrf_failed",
  "not_found",
  "invalid_state",
  "precondition_failed",
  "rate_limited",
  "provider_unavailable",
  "too_large",
  "unsupported_media_type",
  "export_in_progress",
  "retry_limit_reached",
] as const satisfies readonly DocsErrorCode[];
const BLOCKED = ["too_large", "nothing_to_export"] as const satisfies readonly BlockedReason[];

function parseBlock(v: unknown): DocBlock | null {
  if (!isObj(v)) return null;
  if (v.kind === "heading") {
    if (!exact(v, ["kind", "level", "text"]) || (v.level !== 1 && v.level !== 2) || !isBlockText(v.text)) return null;
    return { kind: "heading", level: v.level, text: v.text };
  }
  if (v.kind === "bullet") {
    if (!exact(v, ["kind", "text", "citations"]) || !isBlockText(v.text)) return null;
    const c = v.citations;
    if (!Array.isArray(c) || c.length < 1 || c.length > 8) return null;
    const citations: { startMs: number }[] = [];
    for (const x of c) {
      if (!isObj(x) || !exact(x, ["startMs"]) || !isInt(x.startMs, 0, MS_MAX)) return null;
      citations.push({ startMs: x.startMs });
    }
    return { kind: "bullet", text: v.text, citations };
  }
  if (v.kind === "paragraph") {
    if (!exact(v, ["kind", "text"]) || !isBlockText(v.text)) return null;
    return { kind: "paragraph", text: v.text };
  }
  return null;
}

function parseDocument(v: unknown): DocDocument | null {
  if (!isObj(v) || !exact(v, ["title", "blocks"]) || !isTitle(v.title)) return null;
  if (!Array.isArray(v.blocks) || v.blocks.length < 1 || v.blocks.length > 400) return null;
  const blocks: DocBlock[] = [];
  for (const b of v.blocks) {
    const p = parseBlock(b);
    if (!p) return null;
    blocks.push(p);
  }
  return { title: v.title, blocks };
}

/** The in_app (Demo Doc) branch only. The `external` branch (a Google URL) is refused in v1. */
export function parseDocRef(v: unknown): DocRef | null {
  if (!isObj(v) || !exact(v, ["docId", "provider", "label", "version", "openKind", "openPath", "openUrl"])) return null;
  if (!isDocId(v.docId) || v.provider !== "demo_docs" || v.label !== "Demo Doc" || !isVersion(v.version)) return null;
  if (v.openKind !== "in_app" || v.openUrl !== null || !isStr(v.openPath) || !OPEN_PATH.test(v.openPath)) return null;
  // The path must name this document; a path to another id is refused rather than followed.
  if (v.openPath !== `/demo-docs/${v.docId}`) return null;
  return { docId: v.docId, provider: "demo_docs", label: "Demo Doc", version: v.version, openKind: "in_app", openPath: v.openPath, openUrl: null };
}

function parseExisting(v: unknown): DocsExisting | null {
  if (!isObj(v) || !exact(v, ["exportId", "docRef", "lastExportedAt", "upToDate"])) return null;
  const docRef = parseDocRef(v.docRef);
  if (!docRef || !isExportId(v.exportId) || !isTimestamp(v.lastExportedAt) || !isBool(v.upToDate)) return null;
  return { exportId: v.exportId, docRef, lastExportedAt: v.lastExportedAt, upToDate: v.upToDate };
}

const PREVIEW_KEYS = [
  "previewOnly",
  "sessionId",
  "notesId",
  "notesTruncated",
  "notesSource",
  "destination",
  "document",
  "counts",
  "contentDigest",
  "existing",
  "actions",
  "canWrite",
  "blockedReason",
  "generatedAt",
] as const;

/** docs-preview. `expectedSessionId`, when given, must be the session the body is about. */
export function parsePreview(v: unknown, expectedSessionId?: string): DocsPreview | null {
  if (!isObj(v) || !exact(v, PREVIEW_KEYS)) return null;
  if (v.previewOnly !== true || !isSessionId(v.sessionId) || !isNotesId(v.notesId) || !isBool(v.notesTruncated)) return null;
  if (expectedSessionId !== undefined && v.sessionId !== expectedSessionId) return null;
  if (v.notesSource !== "mock" && v.notesSource !== "model") return null;
  const d = v.destination;
  if (!isObj(d) || !exact(d, ["provider", "label", "isDemo"]) || d.provider !== "demo_docs" || d.label !== "Demo Doc" || d.isDemo !== true) return null;
  const document = parseDocument(v.document);
  if (!document) return null;
  const c = v.counts;
  if (!isObj(c) || !exact(c, ["sections", "bullets", "citations"]) || !isInt(c.sections, 0, 400) || !isInt(c.bullets, 0, 400) || !isInt(c.citations, 0, 3200)) return null;
  if (!isContentDigest(v.contentDigest) || !isTimestamp(v.generatedAt) || !isBool(v.canWrite)) return null;
  let existing: DocsExisting | null = null;
  if (v.existing !== null) {
    existing = parseExisting(v.existing);
    if (!existing) return null;
  }
  const a = v.actions;
  if (!Array.isArray(a) || a.length > 2 || !a.every(isMode) || new Set(a).size !== a.length) return null;
  if (v.blockedReason !== null && !oneOf(BLOCKED)(v.blockedReason)) return null;
  // Cross-field rules the contract states in prose (4.2). A body that breaks them is not shown: the UI would otherwise
  // offer a write the server will refuse, or hide a reason.
  if (!v.canWrite && (v.blockedReason === null || a.length !== 0)) return null;
  if (v.canWrite && v.blockedReason !== null) return null;
  if (existing === null && a.some((m) => m !== "create")) return null;
  if (existing !== null && a.includes("create")) return null;
  return {
    previewOnly: true,
    sessionId: v.sessionId,
    notesId: v.notesId,
    notesTruncated: v.notesTruncated,
    notesSource: v.notesSource,
    destination: { provider: "demo_docs", label: "Demo Doc", isDemo: true },
    document,
    counts: { sections: c.sections, bullets: c.bullets, citations: c.citations },
    contentDigest: v.contentDigest,
    existing,
    actions: [...(a as DocsMode[])],
    canWrite: v.canWrite,
    blockedReason: v.blockedReason as BlockedReason | null,
    generatedAt: v.generatedAt,
  };
}

const RECORD_KEYS = [
  "exportId",
  "sessionId",
  "notesId",
  "provider",
  "demoDoc",
  "mode",
  "status",
  "outcome",
  "contentDigest",
  "docRef",
  "attempt",
  "createdAt",
  "completedAt",
  "error",
  "retry",
] as const;

function parseRecordError(v: unknown): DocsRecordError | null {
  if (!isObj(v) || !exact(v, ["code", "retryable", "retryAfterSeconds"])) return null;
  if (!oneOf(RECORD_CODES)(v.code) || !isBool(v.retryable)) return null;
  if (v.retryAfterSeconds !== null && !isInt(v.retryAfterSeconds, 1, 3600)) return null;
  return { code: v.code, retryable: v.retryable, retryAfterSeconds: v.retryAfterSeconds as number | null };
}

/** docs-export-record. `expectedSessionId`, when given, must be the session the record is about. */
export function parseExportRecord(v: unknown, expectedSessionId?: string): DocsExportRecord | null {
  if (!isObj(v) || !exact(v, RECORD_KEYS)) return null;
  if (!isExportId(v.exportId) || !isSessionId(v.sessionId) || !isNotesId(v.notesId) || v.provider !== "demo_docs" || v.demoDoc !== true) return null;
  if (expectedSessionId !== undefined && v.sessionId !== expectedSessionId) return null;
  if (!isMode(v.mode) || (v.status !== "succeeded" && v.status !== "failed")) return null;
  if (!oneOf(["created", "updated", "unchanged", "failed"] as const)(v.outcome)) return null;
  if (!isContentDigest(v.contentDigest) || !isInt(v.attempt, 1, 5) || !isTimestamp(v.createdAt)) return null;
  if (v.completedAt !== null && !isTimestamp(v.completedAt)) return null;
  let docRef: DocRef | null = null;
  if (v.docRef !== null) {
    docRef = parseDocRef(v.docRef);
    if (!docRef) return null;
  }
  let error: DocsRecordError | null = null;
  if (v.error !== null) {
    error = parseRecordError(v.error);
    if (!error) return null;
  }
  const r = v.retry;
  if (!isObj(r) || !exact(r, ["allowed", "attemptsLeft"]) || !isBool(r.allowed) || !isInt(r.attemptsLeft, 0, 4)) return null;
  if (v.status === "failed") {
    if (v.outcome !== "failed" || error === null || v.completedAt === null) return null;
  } else if (v.outcome === "failed" || error !== null || docRef === null || r.allowed !== false) return null;
  return {
    exportId: v.exportId,
    sessionId: v.sessionId,
    notesId: v.notesId,
    provider: "demo_docs",
    demoDoc: true,
    mode: v.mode,
    status: v.status,
    outcome: v.outcome,
    contentDigest: v.contentDigest,
    docRef,
    attempt: v.attempt,
    createdAt: v.createdAt,
    completedAt: v.completedAt as string | null,
    error,
    retry: { allowed: r.allowed, attemptsLeft: r.attemptsLeft },
  };
}

export function parseState(v: unknown, expectedSessionId?: string): DocsState | null {
  if (!isObj(v) || !exact(v, ["sessionId", "target", "lastExport"]) || !isSessionId(v.sessionId)) return null;
  if (expectedSessionId !== undefined && v.sessionId !== expectedSessionId) return null;
  let target: DocRef | null = null;
  if (v.target !== null) {
    target = parseDocRef(v.target);
    if (!target) return null;
  }
  let lastExport: DocsExportRecord | null = null;
  if (v.lastExport !== null) {
    lastExport = parseExportRecord(v.lastExport, v.sessionId);
    if (!lastExport) return null;
  }
  return { sessionId: v.sessionId, target, lastExport };
}

/** docs-demo-doc. `expectedDocId`, when given, must be the document that was asked for. */
export function parseDemoDoc(v: unknown, expectedDocId?: string): DemoDoc | null {
  if (!isObj(v) || !exact(v, ["docId", "provider", "demoDoc", "document", "version", "createdAt", "updatedAt"])) return null;
  if (!isDocId(v.docId) || v.provider !== "demo_docs" || v.demoDoc !== true || !isVersion(v.version)) return null;
  if (expectedDocId !== undefined && v.docId !== expectedDocId) return null;
  if (!isTimestamp(v.createdAt) || !isTimestamp(v.updatedAt)) return null;
  const document = parseDocument(v.document);
  if (!document) return null;
  return { docId: v.docId, provider: "demo_docs", demoDoc: true, document, version: v.version, createdAt: v.createdAt, updatedAt: v.updatedAt };
}

/** docs-problem. Used to read `code` and `retryAfterSeconds`; `detail` is never shown to the student. */
export function parseProblem(v: unknown): DocsProblem | null {
  if (!isObj(v) || !exact(v, ["type", "title", "status", "code", "traceId"], ["detail", "instance", "errors", "retryAfterSeconds"])) return null;
  if (!isStr(v.type) || !PROBLEM_TYPE.test(v.type)) return null;
  if (!isStr(v.title) || v.title.length < 1 || v.title.length > 200) return null;
  if (!isInt(v.status, 400, 599) || !oneOf(ERROR_CODES)(v.code)) return null;
  if (!isStr(v.traceId) || v.traceId.length < 1 || v.traceId.length > 64) return null;
  if ("detail" in v && (!isStr(v.detail) || v.detail.length > 200 || !PROBLEM_DETAIL.test(v.detail))) return null;
  if ("instance" in v && (!isStr(v.instance) || v.instance.length > 256)) return null;
  if ("errors" in v) {
    const e = v.errors;
    if (!isObj(e) || Object.keys(e).length > 20) return null;
    for (const [k, list] of Object.entries(e)) {
      if (!ERROR_KEY.test(k) || !Array.isArray(list) || list.length > 10 || !list.every((s) => isStr(s) && s.length <= 200)) return null;
    }
  }
  if ("retryAfterSeconds" in v && !isInt(v.retryAfterSeconds, 1, Number.MAX_SAFE_INTEGER)) return null;
  const out: DocsProblem = { type: v.type, title: v.title, status: v.status, code: v.code, traceId: v.traceId };
  if (typeof v.retryAfterSeconds === "number") out.retryAfterSeconds = v.retryAfterSeconds;
  return out;
}
