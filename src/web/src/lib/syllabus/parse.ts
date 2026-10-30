import type {
  BatchResult,
  Confidence,
  Decision,
  Derivation,
  ExtractionStatus,
  Flag,
  ItemErrorCode,
  ItemKind,
  ItemWhen,
  Page,
  Recurrence,
  RemoveResult,
  RunStatus,
  Snippet,
  StatusReason,
  Syllabus,
  SyllabusItem,
  SyncOutcome,
  SyncResult,
  SyncState,
  TimezoneSource,
  UnknownReason,
  Weekday,
} from "./types";

// Strict parsers for syllabus.v1 response bodies (same philosophy as lib/lecture/aiPayloads.ts): every field the UI
// reads is checked against the contract's types, enums and cross-field rules; only known fields are copied (unknown
// members are dropped, never rendered); anything malformed returns null so the caller shows a calm error instead of a
// half-rendered page. These checks are a client guard, not TextPolicy: the server is authoritative (contract 2.2,
// R3-07), and server text is still rendered only as plain text nodes.

type Obj = Record<string, unknown>;
const isObj = (v: unknown): v is Obj => typeof v === "object" && v !== null && !Array.isArray(v);
const isStr = (v: unknown): v is string => typeof v === "string";
const isBool = (v: unknown): v is boolean => typeof v === "boolean";
const isInt = (v: unknown, min = 0, max = 2147483647): v is number => typeof v === "number" && Number.isInteger(v) && v >= min && v <= max;
const nullable = <T>(v: unknown, ok: (x: unknown) => x is T): v is T | null => v === null || ok(v);
const oneOf =
  <T extends string>(values: readonly T[]) =>
  (v: unknown): v is T =>
    typeof v === "string" && (values as readonly string[]).includes(v);

// Patterns from syllabus-common.schema.json, with the same end anchor rule (no `$`, which matches before a final LF).
const ID = /^(?:[a-z]{3}_)?[a-z0-9]+(?:-[a-z0-9]+)*(?![\s\S])/;
const ITEM_KEY = /^ik_[A-Za-z0-9_-]{32}(?![\s\S])/;
const LOCAL_DATE = /^(20[0-9]{2}|2100)-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])(?![\s\S])/;
const LOCAL_TIME = /^([01][0-9]|2[0-3]):[0-5][0-9](?![\s\S])/;
const ZONE = /^[A-Za-z]+(\/[A-Za-z0-9_+-]+){1,2}(?![\s\S])/;
const TIMESTAMP = /^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]+)?(Z|[+-][0-9]{2}:[0-9]{2})(?![\s\S])/;

export const isId = (v: unknown): v is string => isStr(v) && v.length <= 64 && ID.test(v);
export const isLocalDate = (v: unknown): v is string => isStr(v) && LOCAL_DATE.test(v);
export const isLocalTime = (v: unknown): v is string => isStr(v) && LOCAL_TIME.test(v);
export const isZone = (v: unknown): v is string => isStr(v) && v.length >= 3 && v.length <= 64 && ZONE.test(v);
const isTimestamp = (v: unknown): v is string => isStr(v) && TIMESTAMP.test(v);
const isEtag = (v: unknown): v is string => isStr(v) && v.length >= 1 && v.length <= 128;
/** Non-empty text with no C0/C1 controls except LF (a coarse client check; never a TextPolicy verdict). */
const isText = (max: number, multiline = false) => (v: unknown): v is string =>
  isStr(v) && v.length >= 1 && [...v].length <= max && !(multiline ? /[\u0000-\u0009\u000b-\u001f\u007f-\u009f]/ : /[\u0000-\u001f\u007f-\u009f]/).test(v);

const STATUSES = ["queued", "extracting", "proposing", "ready", "needs_text", "partial", "failed"] as const satisfies readonly ExtractionStatus[];
const REASONS = [
  "no_text_layer",
  "unreadable_file",
  "password_protected",
  "too_long",
  "budget_limited",
  "provider_unavailable",
  "timeout",
  "invalid_output",
  "disabled",
] as const satisfies readonly StatusReason[];
const KINDS = ["assignment", "exam", "reading", "office_hours", "policy", "other"] as const satisfies readonly ItemKind[];
const CONFIDENCES = ["looks_right", "probably_right", "please_check"] as const satisfies readonly Confidence[];
const DECISIONS = ["undecided", "approved", "skipped"] as const satisfies readonly Decision[];
const SYNC_STATES = ["not_synced", "synced", "changed_since_sync", "sync_failed"] as const satisfies readonly SyncState[];
const DERIVATIONS = ["explicit_full", "year_from_term", "relative_to_anchor", "student_entered"] as const satisfies readonly Derivation[];
const UNKNOWN_REASONS = [
  "not_stated",
  "tba",
  "relative_unresolvable",
  "conflicting",
  "ambiguous_format",
  "table_unreadable",
  "date_not_verifiable",
] as const satisfies readonly UnknownReason[];
const TZ_SOURCES = ["stated_in_source", "course_default", "student"] as const satisfies readonly TimezoneSource[];
const WEEKDAYS = ["MO", "TU", "WE", "TH", "FR", "SA", "SU"] as const satisfies readonly Weekday[];
const FLAGS = [
  "year_inferred",
  "relative_date",
  "timezone_assumed",
  "time_missing",
  "conflicting_sources",
  "tentative_language",
  "table_layout_risk",
  "section_ambiguous",
  "range_end_inferred",
  "weekday_mismatch",
  "date_not_verifiable",
  "until_from_term",
  "low_text_quality",
  "duplicate_candidate",
  "outside_term",
  "dst_ambiguous",
  "injection_suspect",
  "supersession_cue",
] as const satisfies readonly Flag[];
const ITEM_ERRORS = [
  "item_needs_date",
  "item_not_syncable",
  "item_not_approved",
  "batch_not_eligible",
  "acknowledgement_required",
  "precondition_failed",
  "not_found",
  "provider_unavailable",
  "invalid_state",
  "event_limit_reached",
] as const satisfies readonly ItemErrorCode[];

const intArray = (v: unknown, min: number, max: number): v is number[] => Array.isArray(v) && v.every((x) => isInt(x, min, max));

export function parseSyllabus(v: unknown): Syllabus | null {
  if (!isObj(v)) return null;
  const d = v.document;
  const ctx = v.context;
  const c = v.counts;
  const r = v.retention;
  if (!isObj(d) || !isObj(ctx) || !isObj(c) || !isObj(r)) return null;
  if (
    !isId(v.id) ||
    !isId(v.courseId) ||
    !nullable(v.title, isText(200)) ||
    (v.inputKind !== "upload" && v.inputKind !== "paste") ||
    !nullable(v.fileType, oneOf(["pdf", "docx", "txt"] as const)) ||
    !oneOf(STATUSES)(v.status) ||
    !nullable(v.statusReason, oneOf(REASONS)) ||
    !nullable(v.pollAfterSeconds, (x): x is number => isInt(x, 1, 60)) ||
    (v.aiSource !== "mock" && v.aiSource !== "model") ||
    !isBool(v.demoSource) ||
    v.demoCalendar !== true ||
    v.originalRetained !== false ||
    !isEtag(v.etag) ||
    !isTimestamp(v.createdAt) ||
    !isTimestamp(v.updatedAt) ||
    !isBool(v.batchApproveAllowed)
  )
    return null;
  if (
    !nullable(d.pageCount, (x): x is number => isInt(x, 1, 60)) ||
    !isInt(d.charCount, 0, 50000) ||
    !intArray(d.unreadablePages, 1, 60) ||
    !isInt(d.hiddenContentRemoved) ||
    !isBool(d.injectionSuspect) ||
    !isBool(d.hiddenContentDropped)
  )
    return null;
  if (!nullable(ctx.termStart, isLocalDate) || !nullable(ctx.termEnd, isLocalDate) || !isZone(ctx.defaultTimezone)) return null;
  const countCaps = { total: 300, undecided: 300, approved: 300, skipped: 300, needsDate: 300, synced: 300, droppedInvalid: 1000, unclaimedDates: 200 } as const;
  if (!(Object.keys(countCaps) as (keyof typeof countCaps)[]).every((k) => isInt(c[k], 0, countCaps[k]))) return null;
  const n = v.notice;
  if (!isObj(n) || !isStr(n.version) || n.attested !== true || !isTimestamp(n.attestedAt)) return null;
  if (!isBool(r.canonicalTextPurged) || !nullable(r.canonicalTextPurgeAt, isTimestamp)) return null;
  if (!Array.isArray(v.unprocessedRanges)) return null;
  const ranges: Syllabus["unprocessedRanges"] = [];
  for (const x of v.unprocessedRanges) {
    if (!isObj(x) || !isInt(x.fromPage, 1) || !isInt(x.toPage, 1) || !oneOf(REASONS)(x.reason)) return null;
    ranges.push({ fromPage: x.fromPage, toPage: x.toPage, reason: x.reason });
  }

  // Cross-field rules the UI depends on (schema allOf).
  const inProgress = v.status === "queued" || v.status === "extracting" || v.status === "proposing";
  if (inProgress !== (v.pollAfterSeconds !== null)) return null;
  if (inProgress && v.statusReason !== null) return null;
  if (v.status === "ready" && v.statusReason !== null) return null;
  if (v.status === "needs_text" && v.statusReason !== "no_text_layer") return null;
  if ((v.status === "partial" || v.status === "failed") && v.statusReason === null) return null;
  if (v.aiSource === "mock" && v.demoSource !== true) return null;
  if (v.inputKind === "paste" && v.fileType !== null) return null;
  if ((d.hiddenContentRemoved >= 1) !== d.hiddenContentDropped) return null;
  if ((d.injectionSuspect || d.hiddenContentDropped || r.canonicalTextPurged) && v.batchApproveAllowed) return null;
  if (r.canonicalTextPurged !== (r.canonicalTextPurgeAt === null)) return null;

  return {
    id: v.id,
    courseId: v.courseId,
    title: v.title,
    inputKind: v.inputKind,
    fileType: v.fileType,
    status: v.status,
    statusReason: v.statusReason,
    pollAfterSeconds: v.pollAfterSeconds,
    aiSource: v.aiSource,
    demoSource: v.demoSource,
    demoCalendar: true,
    originalRetained: false,
    document: {
      pageCount: d.pageCount,
      charCount: d.charCount,
      unreadablePages: [...d.unreadablePages],
      hiddenContentRemoved: d.hiddenContentRemoved,
      injectionSuspect: d.injectionSuspect,
      hiddenContentDropped: d.hiddenContentDropped,
    },
    unprocessedRanges: ranges,
    context: { termStart: ctx.termStart, termEnd: ctx.termEnd, defaultTimezone: ctx.defaultTimezone },
    counts: {
      total: c.total as number,
      undecided: c.undecided as number,
      approved: c.approved as number,
      skipped: c.skipped as number,
      needsDate: c.needsDate as number,
      synced: c.synced as number,
      droppedInvalid: c.droppedInvalid as number,
      unclaimedDates: c.unclaimedDates as number,
    },
    etag: v.etag,
    createdAt: v.createdAt,
    updatedAt: v.updatedAt,
    batchApproveAllowed: v.batchApproveAllowed,
    retention: { canonicalTextPurged: r.canonicalTextPurged, canonicalTextPurgeAt: r.canonicalTextPurgeAt },
  };
}

function parseWhen(v: unknown): ItemWhen | null {
  if (!isObj(v)) return null;
  if (!oneOf(["resolved", "needs_date", "not_applicable"] as const)(v.status)) return null;
  let written: ItemWhen["written"] = null;
  if (v.written !== null) {
    const w = v.written;
    if (!isObj(w) || !nullable(w.dateText, isText(100)) || !nullable(w.timeText, isText(50)) || !isObj(w.parts)) return null;
    written = { dateText: w.dateText, timeText: w.timeText };
  }
  if (
    !nullable(v.date, isLocalDate) ||
    !nullable(v.endDate, isLocalDate) ||
    !nullable(v.time, isLocalTime) ||
    !nullable(v.endTime, isLocalTime) ||
    !isBool(v.allDay) ||
    !nullable(v.timezone, isZone) ||
    !nullable(v.timezoneSource, oneOf(TZ_SOURCES)) ||
    !nullable(v.derivation, oneOf(DERIVATIONS)) ||
    !nullable(v.unknownReason, oneOf(UNKNOWN_REASONS)) ||
    !Array.isArray(v.alternatives) ||
    v.alternatives.length > 3
  )
    return null;
  const alternatives: ItemWhen["alternatives"] = [];
  for (const a of v.alternatives) {
    if (!isObj(a) || !isLocalDate(a.date) || !nullable(a.time, isLocalTime)) return null;
    alternatives.push({ date: a.date, time: a.time });
  }
  if (v.status === "resolved" && (v.date === null || v.timezone === null || v.timezoneSource === null || v.derivation === null || v.unknownReason !== null)) return null;
  if (
    v.status === "needs_date" &&
    (v.date !== null || v.endDate !== null || v.time !== null || v.endTime !== null || v.allDay || v.derivation !== null || v.unknownReason === null)
  )
    return null;
  if (v.status === "not_applicable" && (v.date !== null || v.time !== null || v.allDay || v.unknownReason !== null)) return null;
  if (v.allDay && (v.time !== null || v.endTime !== null)) return null;
  if (v.endTime !== null && v.time === null) return null;
  return {
    status: v.status,
    written,
    date: v.date,
    endDate: v.endDate,
    time: v.time,
    endTime: v.endTime,
    allDay: v.allDay,
    timezone: v.timezone,
    timezoneSource: v.timezoneSource,
    derivation: v.derivation,
    unknownReason: v.unknownReason,
    alternatives,
  };
}

function parseRecurrence(v: unknown): Recurrence | null | undefined {
  if (v === null) return null;
  if (
    !isObj(v) ||
    v.pattern !== "weekly" ||
    !Array.isArray(v.byDay) ||
    v.byDay.length < 1 ||
    !v.byDay.every(oneOf(WEEKDAYS)) ||
    !isLocalTime(v.startTime) ||
    !nullable(v.endTime, isLocalTime) ||
    !isLocalDate(v.from) ||
    !isLocalDate(v.until) ||
    !Array.isArray(v.exceptions) ||
    !v.exceptions.every(isLocalDate)
  )
    return undefined;
  return { pattern: "weekly", byDay: [...(v.byDay as Weekday[])], startTime: v.startTime, endTime: v.endTime, from: v.from, until: v.until, exceptions: [...(v.exceptions as string[])] };
}

function parseSnippet(v: unknown): Snippet | null | undefined {
  if (v === null) return null;
  if (!isObj(v)) return undefined;
  if (!nullable(v.text, isText(600, true)) || !nullable(v.start, isInt) || !nullable(v.end, (x): x is number => isInt(x, 1)) || !nullable(v.page, (x): x is number => isInt(x, 1, 60))) return undefined;
  if (v.precision !== "exact" && v.precision !== "line") return undefined;
  let context: Snippet["context"] = null;
  if (v.context !== null) {
    const c = v.context;
    if (!isObj(c) || !isText(1500, true)(c.text) || !isInt(c.start) || !isInt(c.end, 1)) return undefined;
    context = { text: c.text, start: c.start, end: c.end };
  }
  // R2-04: after the purge text, context and offsets are all null together; none survives alone.
  const purged = v.text === null;
  if (purged !== (context === null) || purged !== (v.start === null) || purged !== (v.end === null)) return undefined;
  return { text: v.text, start: v.start, end: v.end, page: v.page, precision: v.precision, context };
}

export function parseItem(v: unknown): SyllabusItem | null {
  if (!isObj(v)) return null;
  const when = parseWhen(v.when);
  const recurrence = parseRecurrence(v.recurrence);
  const snippet = parseSnippet(v.snippet);
  if (!when || recurrence === undefined || snippet === undefined) return null;
  if (
    !isId(v.id) ||
    !isId(v.syllabusId) ||
    !isId(v.courseId) ||
    !isStr(v.itemKey) ||
    !ITEM_KEY.test(v.itemKey) ||
    (v.origin !== "ai" && v.origin !== "student") ||
    !oneOf(KINDS)(v.kind) ||
    !isText(200)(v.title) ||
    !nullable(v.notes, isText(500, true)) ||
    !oneOf(CONFIDENCES)(v.confidence) ||
    !isText(200)(v.confidenceReason) ||
    !Array.isArray(v.flags) ||
    !v.flags.every(oneOf(FLAGS)) ||
    !isBool(v.edited) ||
    !oneOf(DECISIONS)(v.decision) ||
    !oneOf(SYNC_STATES)(v.syncState) ||
    !nullable(v.syncedRevision, (x): x is number => isInt(x, 1)) ||
    !isInt(v.revision, 1) ||
    !isEtag(v.etag) ||
    !isTimestamp(v.createdAt) ||
    !isTimestamp(v.updatedAt)
  )
    return null;
  const flags = [...(v.flags as Flag[])];
  if (new Set(flags).size !== flags.length) return null;
  // Cross-field rules (syllabus-item.schema.json allOf).
  if (when.status !== "resolved" && v.decision === "approved") return null;
  if (v.syncState !== "not_synced" && v.decision !== "approved") return null;
  if ((v.syncState === "synced" || v.syncState === "changed_since_sync") && v.syncedRevision === null) return null;
  if (v.syncState === "not_synced" && v.syncedRevision !== null) return null;
  if ((v.kind === "policy") !== (when.status === "not_applicable")) return null;
  if (v.origin === "student" && (snippet !== null || v.kind === "policy")) return null;
  if (v.origin === "ai" && snippet === null) return null;
  if (flags.some((f) => f === "injection_suspect" || f === "supersession_cue" || f === "low_text_quality") && v.confidence !== "please_check") return null;
  if (when.derivation === "student_entered" && v.confidence === "looks_right") return null;
  if (v.origin === "ai" && when.derivation === "student_entered" && v.confidence !== "please_check") return null;
  return {
    id: v.id,
    syllabusId: v.syllabusId,
    courseId: v.courseId,
    itemKey: v.itemKey,
    origin: v.origin,
    kind: v.kind,
    title: v.title,
    notes: v.notes,
    when,
    recurrence,
    confidence: v.confidence,
    confidenceReason: v.confidenceReason,
    flags,
    snippet,
    edited: v.edited,
    decision: v.decision,
    syncState: v.syncState,
    syncedRevision: v.syncedRevision,
    revision: v.revision,
    etag: v.etag,
    createdAt: v.createdAt,
    updatedAt: v.updatedAt,
  };
}

function parsePage<T>(v: unknown, one: (x: unknown) => T | null, max = 200): Page<T> | null {
  if (!isObj(v) || !Array.isArray(v.items) || v.items.length > max) return null;
  if (!nullable(v.nextCursor, (x): x is string => isStr(x) && x.length >= 1 && x.length <= 256)) return null;
  const items: T[] = [];
  for (const x of v.items) {
    const p = one(x);
    if (!p) return null; // one bad row makes the page unusable: never show a list that silently lost rows
    items.push(p);
  }
  return { items, nextCursor: v.nextCursor };
}

export const parseSyllabusPage = (v: unknown) => parsePage(v, parseSyllabus);
export const parseItemPage = (v: unknown) => parsePage(v, parseItem);

export function parseBatchResult(v: unknown): BatchResult | null {
  if (!isObj(v) || !Array.isArray(v.results) || v.results.length > 100 || !isObj(v.counts)) return null;
  const c = v.counts;
  if (!isInt(c.applied) || !isInt(c.unchanged) || !isInt(c.rejected)) return null;
  const results: BatchResult["results"] = [];
  for (const x of v.results) {
    if (!isObj(x) || !isId(x.itemId) || !oneOf(["applied", "unchanged", "rejected"] as const)(x.outcome) || !nullable(x.errorCode, oneOf(ITEM_ERRORS))) return null;
    const item = x.item === null ? null : parseItem(x.item);
    if (x.item !== null && !item) return null;
    results.push({ itemId: x.itemId, outcome: x.outcome, errorCode: x.errorCode, item });
  }
  return { results, counts: { applied: c.applied, unchanged: c.unchanged, rejected: c.rejected } };
}

const RUN_STATUSES = ["all_succeeded", "partial", "failed", "nothing_to_do"] as const satisfies readonly RunStatus[];
const SYNC_OUTCOMES = ["created", "updated", "unchanged", "failed", "not_eligible"] as const satisfies readonly SyncOutcome[];
const isEventCount = (x: unknown): x is number => isInt(x, 0, 200);

/** syllabus-calendar-sync-result. provider and demoCalendar are mock constants; anything else is refused. */
export function parseSyncResult(v: unknown): SyncResult | null {
  if (!isObj(v) || !isObj(v.counts) || !Array.isArray(v.results) || v.results.length > 100) return null;
  if (!isId(v.syncId) || !isId(v.syllabusId) || v.provider !== "mock" || v.demoCalendar !== true || !oneOf(RUN_STATUSES)(v.status) || !isTimestamp(v.completedAt)) return null;
  const c = v.counts;
  if (!isInt(c.created) || !isInt(c.updated) || !isInt(c.unchanged) || !isInt(c.failed) || !isInt(c.notEligible)) return null;
  const results: SyncResult["results"] = [];
  for (const x of v.results) {
    if (!isObj(x) || !isId(x.itemId) || !isStr(x.itemKey) || !ITEM_KEY.test(x.itemKey) || !oneOf(SYNC_OUTCOMES)(x.outcome)) return null;
    if (!nullable(x.errorCode, oneOf(ITEM_ERRORS)) || !isEventCount(x.eventCount) || !oneOf(SYNC_STATES)(x.syncState)) return null;
    results.push({ itemId: x.itemId, itemKey: x.itemKey, outcome: x.outcome, errorCode: x.errorCode, eventCount: x.eventCount, syncState: x.syncState });
  }
  return {
    syncId: v.syncId,
    syllabusId: v.syllabusId,
    status: v.status,
    completedAt: v.completedAt,
    results,
    counts: { created: c.created, updated: c.updated, unchanged: c.unchanged, failed: c.failed, notEligible: c.notEligible },
  };
}

/** syllabus-calendar-remove-result. */
export function parseRemoveResult(v: unknown): RemoveResult | null {
  if (!isObj(v) || !isObj(v.counts) || !Array.isArray(v.results) || v.results.length > 100) return null;
  if (!isId(v.syllabusId) || v.provider !== "mock" || !oneOf(RUN_STATUSES)(v.status)) return null;
  const c = v.counts;
  if (!isInt(c.removed) || !isInt(c.notPresent) || !isInt(c.failed)) return null;
  const results: RemoveResult["results"] = [];
  for (const x of v.results) {
    if (!isObj(x) || !isId(x.itemId) || !isStr(x.itemKey) || !ITEM_KEY.test(x.itemKey)) return null;
    if (!oneOf(["removed", "not_present", "failed"] as const)(x.outcome) || !nullable(x.errorCode, oneOf(ITEM_ERRORS)) || !isEventCount(x.eventCount) || !oneOf(SYNC_STATES)(x.syncState)) return null;
    results.push({ itemId: x.itemId, outcome: x.outcome, errorCode: x.errorCode, syncState: x.syncState });
  }
  return { syllabusId: v.syllabusId, status: v.status, results, counts: { removed: c.removed, notPresent: c.notPresent, failed: c.failed } };
}

/**
 * The export body is downloaded as-is, never rendered. This checks only that it is the syllabus.v1 export of the
 * syllabus asked for (so a wrong body is not saved as the student's copy); the schema test covers the full shape.
 */
export function isExportOf(v: unknown, syllabusId: string): boolean {
  return isObj(v) && v.schemaVersion === "syllabus.v1" && isTimestamp(v.exportedAt) && isObj(v.syllabus) && v.syllabus.id === syllabusId && Array.isArray(v.items);
}
