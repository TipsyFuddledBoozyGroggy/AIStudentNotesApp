import { http, HttpResponse } from "msw";
import { isDocId, isSessionId } from "@/lib/docs/parse";
import { isIdempotencyKey, parseConfirmBody, parsePreviewRequest } from "@/lib/docs/requests";
import type { DocsMode, DocsRecordError } from "@/lib/docs/types";
import * as db from "./db";
import {
  buildDocument,
  deleteDoc,
  docRefOf,
  docsMock,
  failRecord,
  newestExport,
  newExportRecord,
  nowIso,
  rememberKey,
  storeExport,
  writeDoc,
} from "./docsDb";

// MSW stand-in for docs.export.v1 (Demo Doc mock, docs/API_CONTRACTS_DOCS_EXPORT.md), faithful to the contract's
// evaluation order (4.1): 401; 400 for a malformed key or id; 404 ownership; idempotency lookup by key; 415 / 400 for the
// body; then for a candidate replay the fingerprint (same body: replay or retry, different: 400); then state and
// preconditions (a) to (g). A provider failure is a 200 record. Faults come from the X-Demo-Docs-Fault header with the
// same values the Development backend honours. Synthetic data only; NOT the real backend. Sessions are seeded with
// docsDb.seedDocsSession; an unseeded session id is 404.

const P = "*/api/v1";
const MAX_DOCS = 200;
const MAX_ATTEMPTS = 5;
export const DEMO_DOCS_FAULT_HEADER = "X-Demo-Docs-Fault";

const DETAIL: Record<string, string> = {
  too_large: "The request was too large.",
  unsupported_media_type: "The content type of the request is not supported.",
  retry_limit_reached: "Too many attempts. Start again.",
};

function problem(status: number, code: string) {
  return HttpResponse.json(
    { type: `https://errors.tether.invalid/${code}`, title: code.replace(/_/g, " "), status, code, traceId: "mock-trace", ...(DETAIL[code] ? { detail: DETAIL[code] } : {}) },
    { status, headers: { "Content-Type": "application/problem+json" } },
  );
}
const me = () => db.currentUser()?.email ?? null;

function fault(raw: string | null): DocsRecordError | null {
  const [kind, arg] = (raw ?? "").trim().toLowerCase().split(":", 2);
  const n = arg !== undefined && /^\d+$/.test(arg) ? Number(arg) : null;
  if (kind === "unavailable") return { code: "provider_unavailable", retryable: true, retryAfterSeconds: 5 };
  if (kind === "rate_limited") return { code: "rate_limited", retryable: true, retryAfterSeconds: Math.min(3600, Math.max(1, n ?? 30)) };
  if (kind === "rejected") return { code: "provider_rejected", retryable: false, retryAfterSeconds: null };
  return null;
}

async function readJson(request: Request): Promise<{ ok: true; body: unknown } | { ok: false; res: Response }> {
  if (!(request.headers.get("Content-Type") ?? "").toLowerCase().startsWith("application/json")) return { ok: false, res: problem(415, "unsupported_media_type") };
  const text = await request.text();
  if (text.length > 2048) return { ok: false, res: problem(413, "too_large") };
  try {
    return { ok: true, body: JSON.parse(text) as unknown };
  } catch {
    return { ok: false, res: problem(400, "validation_failed") };
  }
}

function actionsFor(existing: boolean, upToDate: boolean, canWrite: boolean): DocsMode[] {
  if (!canWrite) return [];
  if (!existing) return ["create"];
  return upToDate ? [] : ["update", "create_another"];
}

export const docsHandlers = [
  http.post(`${P}/sessions/:id/docs-export/preview`, async ({ params, request }) => {
    const owner = me();
    if (!owner) return problem(401, "unauthenticated");
    const id = String(params.id);
    if (!isSessionId(id)) return problem(400, "validation_failed");
    const s = docsMock.session(owner, id);
    if (!s) return problem(404, "not_found");
    const read = await readJson(request);
    if (!read.ok) return read.res;
    const req = parsePreviewRequest(read.body);
    if (!req) return problem(400, "validation_failed");
    if (s.state !== "Stopped") return problem(409, "invalid_state");
    if (req.notesId !== s.notesId) return problem(404, "not_found");
    if (s.notesStatus !== "Ready") return problem(409, "invalid_state");
    const built = buildDocument(s);
    const tooLarge = built.blocks.length > 400;
    const blockedReason = tooLarge ? "too_large" : built.bullets === 0 ? "nothing_to_export" : null;
    const t = docsMock.target(id);
    const doc = t ? docsMock.doc(owner, t.docId) : undefined;
    const existing = t && doc ? { exportId: t.lastExportId, docRef: docRefOf(doc.docId, doc.version), lastExportedAt: t.updatedAt, upToDate: t.lastDigest === built.digest } : null;
    return HttpResponse.json({
      previewOnly: true,
      sessionId: id,
      notesId: s.notesId,
      notesTruncated: s.truncated,
      notesSource: s.source,
      destination: { provider: "demo_docs", label: "Demo Doc", isDemo: true },
      document: { title: built.title, blocks: built.blocks.slice(0, 400) },
      counts: { sections: Math.min(400, built.sections), bullets: Math.min(400, built.bullets), citations: Math.min(3200, built.citations) },
      contentDigest: built.digest,
      existing,
      actions: actionsFor(existing !== null, existing?.upToDate ?? false, blockedReason === null),
      canWrite: blockedReason === null,
      blockedReason,
      generatedAt: nowIso(),
    });
  }),

  http.post(`${P}/sessions/:id/docs-export`, async ({ params, request }) => {
    const owner = me();
    if (!owner) return problem(401, "unauthenticated");
    const id = String(params.id);
    const key = request.headers.get("Idempotency-Key");
    if (!key || !isIdempotencyKey(key) || !isSessionId(id)) return problem(400, "validation_failed");
    const s = docsMock.session(owner, id);
    if (!s) return problem(404, "not_found");
    const candidate = docsMock.key(owner, key);
    const read = await readJson(request);
    if (!read.ok) return read.res;
    const fingerprint = `${id}\n${JSON.stringify(read.body)}`;
    if (candidate && candidate.fingerprint !== fingerprint) return problem(400, "validation_failed");
    const body = parseConfirmBody(read.body);
    if (!body) return problem(400, "validation_failed");

    const prior = candidate ? docsMock.exportRecord(candidate.exportId)?.record : undefined;
    if (prior && prior.status === "succeeded") return HttpResponse.json(prior, { headers: { "Idempotency-Replayed": "true" } });
    if (prior && (prior.attempt >= MAX_ATTEMPTS || !prior.retry.allowed)) return problem(409, "retry_limit_reached");

    // (a) to (g), in the contract's order. A failed check stores nothing and leaves the key free.
    if (s.state !== "Stopped") return problem(409, "invalid_state");
    if (body.notesId !== s.notesId) return problem(404, "not_found");
    if (s.notesStatus !== "Ready") return problem(409, "invalid_state");
    const built = buildDocument(s);
    if (body.contentDigest !== built.digest) return problem(412, "precondition_failed");
    const t = docsMock.target(id);
    const doc = t ? docsMock.doc(owner, t.docId) : undefined;
    if ((doc?.docId ?? null) !== body.expectedExistingDocId || (doc?.version ?? null) !== body.expectedExistingVersion) return problem(412, "precondition_failed");
    const unchanged = t !== undefined && t.lastDigest === built.digest;
    const tooLarge = built.blocks.length > 400 || built.bullets === 0;
    const modeOk = !tooLarge && (body.mode === "create" ? !doc : body.mode === "update" ? !!doc : !!doc && !unchanged);
    if (!modeOk) return problem(409, "invalid_state");
    if (body.mode !== "update" && docsMock.docCount(owner) >= MAX_DOCS) return problem(409, "invalid_state");

    const base = newExportRecord({
      sessionId: id,
      notesId: s.notesId,
      mode: body.mode,
      digest: built.digest,
      attempt: prior ? prior.attempt + 1 : 1,
      createdAt: prior?.createdAt ?? nowIso(),
      exportId: prior?.exportId,
    });
    const f = fault(request.headers.get(DEMO_DOCS_FAULT_HEADER));
    const record = f ? failRecord(base, f, doc ? docRefOf(doc.docId, doc.version) : null) : writeDoc(owner, s, built, base);
    storeExport(owner, record);
    rememberKey(owner, key, fingerprint, record.exportId);
    return HttpResponse.json(record);
  }),

  http.get(`${P}/sessions/:id/docs-export`, ({ params }) => {
    const owner = me();
    if (!owner) return problem(401, "unauthenticated");
    const id = String(params.id);
    if (!isSessionId(id)) return problem(400, "validation_failed");
    if (!docsMock.session(owner, id)) return problem(404, "not_found");
    const t = docsMock.target(id);
    const doc = t ? docsMock.doc(owner, t.docId) : undefined;
    return HttpResponse.json({ sessionId: id, target: doc ? docRefOf(doc.docId, doc.version) : null, lastExport: newestExport(owner, id) });
  }),

  http.get(`${P}/demo-docs/:docId`, ({ params }) => {
    const owner = me();
    if (!owner) return problem(401, "unauthenticated");
    const docId = String(params.docId);
    if (!isDocId(docId)) return problem(400, "validation_failed");
    const d = docsMock.doc(owner, docId);
    if (!d) return problem(404, "not_found");
    return HttpResponse.json({
      docId: d.docId,
      provider: "demo_docs",
      demoDoc: true,
      document: { title: d.title, blocks: d.blocks },
      version: d.version,
      createdAt: d.createdAt,
      updatedAt: d.updatedAt,
    });
  }),

  http.delete(`${P}/demo-docs/:docId`, ({ params }) => {
    const owner = me();
    if (!owner) return problem(401, "unauthenticated");
    const docId = String(params.docId);
    if (!isDocId(docId)) return problem(400, "validation_failed");
    return deleteDoc(owner, docId) ? new HttpResponse(null, { status: 204 }) : problem(404, "not_found");
  }),
];
