import type { DocBlock, DocsExportRecord, DocsMode, DocsRecordError } from "@/lib/docs/types";

// In-memory stand-in for the Docs module (docs.export.v1, Demo Doc mock), used only by docsHandlers.ts in tests and the
// dev mock mode. Synthetic data only; NOT the real backend. Sessions are seeded by tests (seedDocsSession); a session id
// that was never seeded is 404, like a foreign one. The document builder follows the shape the real builder emits (a
// level 1 title heading, a level 2 heading per section, bullets with {startMs}, a closing source paragraph and, for
// truncated notes, a closing "where the notes stop" paragraph). The digest is a deterministic stand-in, not an HMAC.

export interface DocsSessionSeed {
  sessionId: string;
  owner: string;
  notesId: string;
  title: string | null;
  startedAt: string;
  state?: "Stopped" | "Active" | "Paused";
  notesStatus?: "Ready" | "Pending";
  source?: "mock" | "model";
  truncated?: boolean;
  coveredToMs?: number;
  sections: { heading: string; bullets: { text: string; startMs: number[] }[] }[];
}

interface Session extends Required<Omit<DocsSessionSeed, "title">> {
  title: string | null;
}
export interface StoredDoc {
  docId: string;
  owner: string;
  sessionId: string;
  title: string;
  blocks: DocBlock[];
  version: number;
  createdAt: string;
  updatedAt: string;
  originExportId: string;
}
interface Target {
  docId: string;
  lastDigest: string;
  version: number;
  lastExportId: string;
  updatedAt: string;
}
export interface StoredExport {
  owner: string;
  record: DocsExportRecord;
}
interface KeyRow {
  fingerprint: string;
  exportId: string;
}

const sessions = new Map<string, Session>();
const docs = new Map<string, StoredDoc>();
const targets = new Map<string, Target>();
const exportsById = new Map<string, StoredExport>();
const keys = new Map<string, KeyRow>();
let counter = 0;
let clock = Date.parse("2026-11-06T10:00:00Z");

export function resetDocsMock(): void {
  sessions.clear();
  docs.clear();
  targets.clear();
  exportsById.clear();
  keys.clear();
  counter = 0;
  clock = Date.parse("2026-11-06T10:00:00Z");
}

export function seedDocsSession(seed: DocsSessionSeed): void {
  sessions.set(seed.sessionId, {
    state: "Stopped",
    notesStatus: "Ready",
    source: "mock",
    truncated: false,
    coveredToMs: 60000,
    ...seed,
  });
}

/** Change a seeded session (title edit, new notes press, state) to drive stale and 409 cases in tests. */
export function updateDocsSession(sessionId: string, patch: Partial<Omit<DocsSessionSeed, "sessionId" | "owner">>): void {
  const s = sessions.get(sessionId);
  if (s) sessions.set(sessionId, { ...s, ...patch });
}

export const docsMock = {
  session: (owner: string, id: string) => {
    const s = sessions.get(id);
    return s && s.owner === owner ? s : undefined;
  },
  doc: (owner: string, id: string) => {
    const d = docs.get(id);
    return d && d.owner === owner ? d : undefined;
  },
  docCount: (owner?: string) => [...docs.values()].filter((d) => owner === undefined || d.owner === owner).length,
  target: (sessionId: string) => targets.get(sessionId),
  key: (owner: string, key: string) => keys.get(`${owner}\n${key}`),
  exportRecord: (id: string) => exportsById.get(id),
  exportCount: () => exportsById.size,
};

const hex = (n: number, len: number) => n.toString(16).padStart(len, "0").slice(-len);
function guid(): string {
  counter += 1;
  const h = hex(counter, 12);
  return `0192f0a1-0000-4000-8000-${h}`;
}
export const nowIso = () => {
  clock += 1000;
  return new Date(clock).toISOString();
};

/** A deterministic 43-character base64url stand-in for the HMAC digest (FNV-1a over the canonical text, stretched). */
function digestOf(text: string): string {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
  let out = "";
  let h = 0x811c9dc5;
  for (let round = 0; out.length < 43; round++) {
    for (let i = 0; i < text.length; i++) {
      h ^= text.charCodeAt(i) + round;
      h = Math.imul(h, 0x01000193) >>> 0;
    }
    for (let k = 0; k < 5 && out.length < 43; k++) out += alphabet[(h >>> (k * 6)) & 63];
  }
  return `cd_${out}`;
}

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
const mmss = (ms: number) => {
  const s = Math.floor(ms / 1000);
  return `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(s % 60).padStart(2, "0")}`;
};

export interface Built {
  title: string;
  blocks: DocBlock[];
  sections: number;
  bullets: number;
  citations: number;
  digest: string;
}

export function buildDocument(s: Session): Built {
  const d = new Date(s.startedAt);
  const title = `${s.title ?? "Lecture notes"} - ${MONTHS[d.getUTCMonth()]} ${d.getUTCDate()}, ${d.getUTCFullYear()} UTC`;
  const blocks: DocBlock[] = [{ kind: "heading", level: 1, text: title }];
  let bullets = 0;
  let citations = 0;
  for (const sec of s.sections) {
    blocks.push({ kind: "heading", level: 2, text: sec.heading });
    for (const b of sec.bullets) {
      bullets += 1;
      citations += b.startMs.length;
      blocks.push({ kind: "bullet", text: b.text, citations: b.startMs.map((startMs) => ({ startMs })) });
    }
  }
  blocks.push({
    kind: "paragraph",
    text:
      s.source === "mock"
        ? "Made by Tether from your lecture notes. Test routine, not an AI model: treat as estimates and check them against the transcript."
        : "Made by Tether from your lecture notes. Written by an AI model: they may be wrong, so check them against the transcript.",
  });
  if (s.truncated) blocks.push({ kind: "paragraph", text: `These notes stop at ${mmss(s.coveredToMs)}. Later parts of the lecture are not included.` });
  const digest = digestOf(`${s.sessionId}\n${JSON.stringify({ v: 1, notesId: s.notesId, title, blocks })}`);
  return { title, blocks, sections: s.sections.length, bullets, citations, digest };
}

export function docRefOf(docId: string, version: number) {
  return { docId, provider: "demo_docs" as const, label: "Demo Doc" as const, version, openKind: "in_app" as const, openPath: `/demo-docs/${docId}`, openUrl: null };
}

export function rememberKey(owner: string, key: string, fingerprint: string, exportId: string): void {
  keys.set(`${owner}\n${key}`, { fingerprint, exportId });
}

/** Writes (or re-writes, for a retry) the export row. */
export function storeExport(owner: string, record: DocsExportRecord): void {
  exportsById.set(record.exportId, { owner, record });
}

export function newExportRecord(a: {
  sessionId: string;
  notesId: string;
  mode: DocsMode;
  digest: string;
  attempt: number;
  createdAt: string;
  exportId?: string;
}): DocsExportRecord {
  return {
    exportId: a.exportId ?? `dxp_${guid()}`,
    sessionId: a.sessionId,
    notesId: a.notesId,
    provider: "demo_docs",
    demoDoc: true,
    mode: a.mode,
    status: "succeeded",
    outcome: "unchanged",
    contentDigest: a.digest,
    docRef: null,
    attempt: a.attempt,
    createdAt: a.createdAt,
    completedAt: null,
    error: null,
    retry: { allowed: false, attemptsLeft: Math.max(0, 5 - a.attempt) },
  };
}

export function failRecord(r: DocsExportRecord, error: DocsRecordError, existingRef: DocsExportRecord["docRef"]): DocsExportRecord {
  return {
    ...r,
    status: "failed",
    outcome: "failed",
    docRef: existingRef,
    completedAt: nowIso(),
    error,
    retry: { allowed: error.retryable && r.attempt < 5, attemptsLeft: Math.max(0, 5 - r.attempt) },
  };
}

/** The provider write itself (create, update or create_another). Returns the record it completes. */
export function writeDoc(owner: string, s: Session, built: Built, r: DocsExportRecord): DocsExportRecord {
  const at = nowIso();
  const t = targets.get(s.sessionId);
  if (r.mode === "update" && t) {
    const doc = docs.get(t.docId)!;
    if (t.lastDigest === built.digest) return { ...r, outcome: "unchanged", docRef: docRefOf(doc.docId, doc.version), completedAt: at };
    doc.title = built.title;
    doc.blocks = built.blocks;
    doc.version += 1;
    doc.updatedAt = at;
    targets.set(s.sessionId, { docId: doc.docId, lastDigest: built.digest, version: doc.version, lastExportId: r.exportId, updatedAt: at });
    return { ...r, outcome: "updated", docRef: docRefOf(doc.docId, doc.version), completedAt: at };
  }
  const docId = `ddc_${guid()}`;
  docs.set(docId, { docId, owner, sessionId: s.sessionId, title: built.title, blocks: built.blocks, version: 1, createdAt: at, updatedAt: at, originExportId: r.exportId });
  targets.set(s.sessionId, { docId, lastDigest: built.digest, version: 1, lastExportId: r.exportId, updatedAt: at });
  return { ...r, outcome: "created", docRef: docRefOf(docId, 1), completedAt: at };
}

export function newestExport(owner: string, sessionId: string): DocsExportRecord | null {
  let out: DocsExportRecord | null = null;
  for (const e of exportsById.values()) if (e.owner === owner && e.record.sessionId === sessionId) out = e.record;
  return out;
}

export function deleteDoc(owner: string, docId: string): boolean {
  const d = docs.get(docId);
  if (!d || d.owner !== owner) return false;
  docs.delete(docId);
  const t = targets.get(d.sessionId);
  if (t?.docId === docId) targets.delete(d.sessionId);
  return true;
}

/** Deleting a lecture cascades to its exports, target, Demo Docs and keys (contract 4.7). */
export function deleteDocsSession(sessionId: string): void {
  sessions.delete(sessionId);
  targets.delete(sessionId);
  for (const [id, d] of docs) if (d.sessionId === sessionId) docs.delete(id);
  for (const [id, e] of exportsById) {
    if (e.record.sessionId !== sessionId) continue;
    exportsById.delete(id);
    for (const [k, row] of keys) if (row.exportId === id) keys.delete(k);
  }
}
