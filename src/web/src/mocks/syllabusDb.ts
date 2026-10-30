import { buildItems, buildSyllabus, finishedScenario, type Scenario } from "./syllabusData";

// In-memory syllabus state for the MSW mock (dev + tests), scoped per signed-in email like db.ts. Mock only: there is
// no extraction; a scenario decides what a syllabus becomes. Persisted to localStorage only when the browser worker
// enables it.

type Wire = Record<string, unknown>;

interface Entry {
  syllabus: Wire;
  items: Wire[];
  /** Scenario the syllabus turns into once polling has advanced it past "proposing". */
  outcome: Scenario;
  /** Poll steps left before it finishes (0 = finished). */
  stepsLeft: number;
  ownerEmail: string;
  /** Fields applied on top of the finished scenario (an upload's fileType, a PDF that ends failed/disabled). */
  override?: Wire;
  /** Mock demo calendar: one event per synced item, keyed by item id (recurrences are not expanded in the mock). */
  events?: Record<string, Wire>;
}

interface State {
  entries: Record<string, Entry>;
  /** `${email}|${key}` -> fingerprint and resulting syllabus id. */
  idem: Record<string, { fingerprint: string; syllabusId: string }>;
  /** `${email}|sync|${key}` -> fingerprint and the original sync result body (a replay returns it as history). */
  syncIdem?: Record<string, { fingerprint: string; syllabusId: string; body: Wire }>;
  seq: number;
}

const KEY = "cc.mockdb.syllabus.v1";
let persist = false;
let state: State = { entries: {}, idem: {}, seq: 100 }; // seq starts above the seeded "-1" etags

export function enableSyllabusPersistence(): void {
  persist = true;
  try {
    const raw = window.localStorage.getItem(KEY);
    if (raw) state = JSON.parse(raw) as State;
  } catch {
    /* ignore corrupt mock state */
  }
}

export function saveSyllabusDb(): void {
  if (!persist) return;
  try {
    window.localStorage.setItem(KEY, JSON.stringify(state));
  } catch {
    /* ignore */
  }
}

export function resetSyllabusDb(): void {
  state = { entries: {}, idem: {}, syncIdem: {}, seq: 100 };
  saveSyllabusDb();
}

const STEPS = ["queued", "extracting", "proposing"] as const;

export function recount(e: Entry): void {
  const items = e.items.filter((i) => i.kind !== "policy");
  const c = (pred: (i: Wire) => boolean) => items.filter(pred).length;
  e.syllabus.counts = {
    total: items.length,
    undecided: c((i) => i.decision === "undecided"),
    approved: c((i) => i.decision === "approved"),
    skipped: c((i) => i.decision === "skipped"),
    needsDate: c((i) => (i.when as Wire).status === "needs_date"),
    synced: c((i) => i.syncState === "synced"),
    droppedInvalid: 0,
    unclaimedDates: 0,
  };
}

function finish(e: Entry): void {
  const s = e.syllabus;
  const done = buildSyllabus(s.id as string, s.courseId as string, e.outcome, s.createdAt as string);
  e.syllabus = {
    ...done,
    context: s.context,
    notice: s.notice,
    inputKind: s.inputKind,
    fileType: s.fileType,
    contentSha256: s.contentSha256,
    etag: `W/"${s.id as string}-${++state.seq}"`,
    updatedAt: new Date().toISOString(),
    ...(e.override ?? {}),
  };
  e.items = e.override?.status === "failed" ? [] : buildItems(s.id as string, s.courseId as string, e.outcome);
  recount(e);
}

/** Seeds a syllabus in a given scenario for `email` (tests and dev). Id: `syl_<scenario>` unless given. */
export function seedSyllabus(email: string, courseId: string, scenario: Scenario, id = `syl_${scenario}`, createdAt?: string): Wire {
  const syllabus = buildSyllabus(id, courseId, scenario, createdAt);
  const e: Entry = { syllabus, items: buildItems(id, courseId, scenario), outcome: finishedScenario(scenario), stepsLeft: scenario === "processing" ? STEPS.length : 0, ownerEmail: email };
  recount(e);
  state.entries[id] = e;
  saveSyllabusDb();
  return syllabus;
}

export function createFromPaste(email: string, courseId: string, body: Wire, outcome: Scenario): Wire {
  const id = `syl_${(++state.seq).toString(36)}${Math.random().toString(36).slice(2, 10)}`;
  const syllabus = buildSyllabus(id, courseId, "processing", new Date().toISOString());
  syllabus.context = { termStart: (body.termStart as string | undefined) ?? null, termEnd: (body.termEnd as string | undefined) ?? null, defaultTimezone: body.defaultTimezone };
  syllabus.notice = { version: body.noticeVersion, attested: true, attestedAt: new Date().toISOString() }; // server receipt time (R3-11)
  syllabus.contentSha256 = textHash(body.text as string);
  const e: Entry = { syllabus, items: [], outcome: finishedScenario(outcome), stepsLeft: STEPS.length, ownerEmail: email };
  state.entries[id] = e;
  saveSyllabusDb();
  return syllabus;
}

/** A multipart upload. Plain text follows the paste path; PDF and Word end `failed`/`disabled` (no parser, as on the server). */
export function createFromUpload(email: string, courseId: string, meta: Wire, file: { fileType: "txt" | "pdf" | "docx"; text: string | null; hash: string }, outcome: Scenario): Wire {
  const s = createFromPaste(email, courseId, { ...meta, text: file.text ?? "" }, outcome);
  const e = state.entries[s.id as string];
  e.syllabus = { ...e.syllabus, inputKind: "upload", fileType: file.fileType, contentSha256: file.hash };
  if (file.fileType !== "txt") e.override = { status: "failed", statusReason: "disabled", pollAfterSeconds: null, document: { ...(e.syllabus.document as Wire), charCount: 0 } };
  saveSyllabusDb();
  return e.syllabus;
}

/** DELETE: everything goes, the demo calendar events included (contract 5.2). Idempotency records stay as tombstones. */
export function remove(email: string, id: string): boolean {
  const e = find(email, id);
  if (!e) return false;
  delete state.entries[id];
  saveSyllabusDb();
  return true;
}

export function syncIdemGet(email: string, key: string) {
  return state.syncIdem?.[`${email}|sync|${key}`];
}
export function syncIdemPut(email: string, key: string, fingerprint: string, syllabusId: string, body: Wire): void {
  state.syncIdem = { ...(state.syncIdem ?? {}), [`${email}|sync|${key}`]: { fingerprint, syllabusId, body } };
  saveSyllabusDb();
}

/** Owner-scoped lookup: a foreign id is indistinguishable from a missing one. */
export function find(email: string, id: string): Entry | undefined {
  const e = state.entries[id];
  return e && e.ownerEmail === email ? e : undefined;
}

export function listForCourse(email: string, courseId: string): Wire[] {
  return Object.values(state.entries)
    .filter((e) => e.ownerEmail === email && e.syllabus.courseId === courseId)
    .map((e) => e.syllabus)
    .sort((a, b) => String(b.createdAt).localeCompare(String(a.createdAt)));
}

export function anyInFlight(email: string): boolean {
  return Object.values(state.entries).some((e) => e.ownerEmail === email && e.stepsLeft > 0);
}

/** One poll advances an in-progress syllabus by one step (deterministic for tests). */
export function advance(e: Entry): void {
  if (e.stepsLeft <= 0) return;
  e.stepsLeft -= 1;
  if (e.stepsLeft === 0) finish(e);
  else {
    const status = STEPS[STEPS.length - e.stepsLeft];
    e.syllabus = { ...e.syllabus, status, pollAfterSeconds: status === "proposing" ? 3 : 2, etag: `W/"${e.syllabus.id as string}-${++state.seq}"` };
  }
  saveSyllabusDb();
}

export function nextEtag(id: string): string {
  return `W/"${id}-${++state.seq}"`;
}

export function textHash(text: string): string {
  let h1 = 0x811c9dc5;
  let h2 = 0x01000193;
  for (let i = 0; i < text.length; i++) {
    h1 = Math.imul(h1 ^ text.charCodeAt(i), 16777619) >>> 0;
    h2 = Math.imul(h2 ^ text.charCodeAt(i), 2246822519) >>> 0;
  }
  return (h1.toString(16).padStart(8, "0") + h2.toString(16).padStart(8, "0")).repeat(4);
}

export function findDuplicate(email: string, courseId: string, hash: string): string | undefined {
  return Object.values(state.entries).find((e) => e.ownerEmail === email && e.syllabus.courseId === courseId && e.syllabus.contentSha256 === hash)?.syllabus.id as string | undefined;
}

export function idemGet(email: string, key: string) {
  return state.idem[`${email}|${key}`];
}
export function idemPut(email: string, key: string, fingerprint: string, syllabusId: string): void {
  state.idem[`${email}|${key}`] = { fingerprint, syllabusId };
  saveSyllabusDb();
}
