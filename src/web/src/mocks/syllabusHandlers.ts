import { http, HttpResponse } from "msw";
import * as db from "./db";
import { SCENARIOS, type Scenario } from "./syllabusData";
import * as sdb from "./syllabusDb";

// MSW handlers for syllabus.v1 (docs/API_CONTRACTS_P2.md section 5), mock calendar only. Faithful to the contract on the
// rules the web client depends on: Idempotency-Key format and replay, If-Match (428/412), unknown members rejected,
// owner-scoped 404, approve/needs-date/policy/acknowledgement rules, edit re-derivation (R2-07) and the
// approved-not-synced edit rule (6.2). Not modelled: rate limits beyond a scenario, multipart upload (F2), batch,
// sync, delete, export. Every response body is validated against the real schemas in syllabusHandlers.test.ts.
//
// Scenarios: seeded ids `syl_<scenario>` (ready, partial, needs-text, failed, hostile, purged, processing) via
// sdb.seedSyllabus. A paste create becomes "ready" unless its text contains `mock:<scenario>` (dev convenience; mock
// only). `mock:rate-limited` answers 429 and `mock:too-large` 413.

const P = "*/api/v1";
type Wire = Record<string, unknown>;

const DETAIL: Record<string, string> = {
  validation_failed: "The request was invalid.",
  unauthenticated: "Please sign in.",
  not_found: "Resource not found.",
  precondition_required: "An If-Match header is required.",
  precondition_failed: "The resource has changed. Reload and try again.",
  rate_limited: "Too many requests. Please slow down.",
  too_large: "The text is over the 50,000 character limit.",
  duplicate_syllabus: "This syllabus is already in this course.",
  extraction_in_progress: "Another syllabus is being read.",
  syllabus_limit_reached: "This course has reached its syllabus limit.",
  item_needs_date: "Add a date before approving.",
  item_not_syncable: "This item cannot be approved.",
  acknowledgement_required: "Confirm you checked this item.",
  invalid_state: "The syllabus is not ready.",
};

function problem(status: number, code: string, extra: Wire = {}) {
  return HttpResponse.json(
    { type: `https://errors.tether.invalid/${code}`, title: DETAIL[code] ?? "Error.", status, code, traceId: "00-mock", detail: DETAIL[code] ?? "Error.", ...extra },
    { status, headers: { "Content-Type": "application/problem+json" } },
  );
}
const invalid = (field: string, reason = "invalid") => problem(400, "validation_failed", { errors: { [field]: [reason] } });

const ID = /^(?:[a-z]{3}_)?[a-z0-9]+(?:-[a-z0-9]+)*(?![\s\S])/;
const DATE = /^(20[0-9]{2}|2100)-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])(?![\s\S])/;
const TIME = /^([01][0-9]|2[0-3]):[0-5][0-9](?![\s\S])/;
const ZONE = /^[A-Za-z]+(\/[A-Za-z0-9_+-]+){1,2}(?![\s\S])/;
const KEY = /^[A-Za-z0-9_-]{8,128}(?![\s\S])/;
const NOTICE = /^[A-Za-z0-9._-]{1,64}(?![\s\S])/;
const TS = /^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]+)?(Z|[+-][0-9]{2}:[0-9]{2})(?![\s\S])/;
const BAD_SINGLE_LINE = /[\u0000-\u001f\u007f-\u009f​‎‏‪-‮⁦-⁩﻿]|^\s|\s$|\s{2}/;
const EDITABLE_KINDS = new Set(["assignment", "exam", "reading", "office_hours", "other"]);

function user(): string | null {
  return db.currentUser()?.email ?? null;
}

async function readJson(request: Request): Promise<{ ok: true; body: Wire } | { ok: false }> {
  try {
    const v: unknown = await request.json();
    return v && typeof v === "object" && !Array.isArray(v) ? { ok: true, body: v as Wire } : { ok: false };
  } catch {
    return { ok: false };
  }
}

const unknownMember = (body: Wire, allowed: string[]) => Object.keys(body).find((k) => !allowed.includes(k));
const codePoints = (s: string) => [...s].length;

function ifMatch(request: Request, entity: Wire): Response | null {
  const h = request.headers.get("If-Match");
  if (!h) return problem(428, "precondition_required");
  if (h !== entity.etag) return problem(412, "precondition_failed");
  return null;
}

function touch(e: NonNullable<ReturnType<typeof sdb.find>>): void {
  sdb.recount(e);
  e.syllabus = { ...e.syllabus, etag: sdb.nextEtag(e.syllabus.id as string), updatedAt: new Date().toISOString() };
  sdb.saveSyllabusDb();
}

function realDate(ymd: string): boolean {
  if (!DATE.test(ymd)) return false;
  const [y, m, d] = ymd.split("-").map(Number);
  const dt = new Date(Date.UTC(y, m - 1, d));
  return dt.getUTCMonth() === m - 1 && dt.getUTCDate() === d;
}

/** Shared by the JSON (paste) and multipart (upload) create. `upload` carries the file facts the mock needs. */
function createSyllabus(email: string, courseId: string, key: string, body: Wire, upload?: { fileType: "txt" | "pdf" | "docx"; text: string | null; fingerprint: string }): Response {
    const sorted = JSON.stringify(body, Object.keys(body).sort());
    const fingerprint = `${courseId}|${upload ? `upload|${upload.fingerprint}|` : ""}${sorted}|${JSON.stringify(body.attestation ?? null)}`;
    const prior = sdb.idemGet(email, key);
    if (prior) {
      if (prior.fingerprint !== fingerprint) return invalid("Idempotency-Key", "reused");
      const e = sdb.find(email, prior.syllabusId);
      if (!e) return problem(404, "not_found"); // replay after delete (tombstone)
      return HttpResponse.json(e.syllabus, { status: 202, headers: { "Idempotency-Replayed": "true", Location: `/api/v1/syllabi/${prior.syllabusId}` } });
    }
    const extra = unknownMember(body, ["title", "text", "termStart", "termEnd", "defaultTimezone", "allowDuplicate", "noticeVersion", "attestation"]);
    if (extra) return invalid(extra, "unknown_member");
    if (upload && "text" in body) return invalid("text", "not_allowed"); // the metadata part has no text
    const text = upload ? (upload.text ?? "") : body.text;
    if (typeof text !== "string" || (!upload && text.length < 1)) return invalid("text", "required");
    if (text.includes("mock:rate-limited")) return problem(429, "rate_limited", { retryAfterSeconds: 30 });
    if (codePoints(text) > 50000 || text.includes("mock:too-large")) return problem(413, "too_large");
    if (text.includes("\u0000")) return invalid("text");
    if (typeof body.defaultTimezone !== "string" || !ZONE.test(body.defaultTimezone)) return invalid("defaultTimezone");
    if (typeof body.noticeVersion !== "string" || !NOTICE.test(body.noticeVersion)) return invalid("noticeVersion");
    const att = body.attestation as Wire | undefined;
    if (!att || typeof att !== "object" || att.accepted !== true || typeof att.acceptedAt !== "string" || !TS.test(att.acceptedAt) || unknownMember(att, ["accepted", "acceptedAt"]))
      return invalid("attestation");
    for (const k of ["termStart", "termEnd"] as const) if (body[k] != null && (typeof body[k] !== "string" || !realDate(body[k] as string))) return invalid(k);
    if (body.title != null && (typeof body.title !== "string" || BAD_SINGLE_LINE.test(body.title) || codePoints(body.title) > 200)) return invalid("title");
    if (sdb.anyInFlight(email)) return problem(409, "extraction_in_progress");
    if (sdb.listForCourse(email, courseId).length >= 5) return problem(409, "syllabus_limit_reached");
    const dup = sdb.findDuplicate(email, courseId, sdb.textHash(upload ? upload.fingerprint : text));
    if (dup && body.allowDuplicate !== true) return problem(409, "duplicate_syllabus", { existingSyllabusId: dup });
    const m = /mock:([a-z-]+)/.exec(text);
    const outcome: Scenario = m && (SCENARIOS as string[]).includes(m[1]) ? (m[1] as Scenario) : "ready";
    const s = upload ? sdb.createFromUpload(email, courseId, body, { fileType: upload.fileType, text: upload.text, hash: sdb.textHash(upload.fingerprint) }, outcome) : sdb.createFromPaste(email, courseId, body, outcome);
    sdb.idemPut(email, key, fingerprint, s.id as string);
    return HttpResponse.json(s, { status: 202, headers: { Location: `/api/v1/syllabi/${s.id as string}` } });
}

export const syllabusHandlers = [
  http.post(`${P}/courses/:courseId/syllabi`, async ({ params, request }) => {
    const email = user();
    if (!email) return problem(401, "unauthenticated");
    const key = request.headers.get("Idempotency-Key");
    if (!key || !KEY.test(key)) return invalid("Idempotency-Key", key ? "invalid" : "required");
    const courseId = String(params.courseId);
    if (!ID.test(courseId)) return invalid("courseId", "not_canonical");
    if (!db.findCourse(courseId)) return problem(404, "not_found");
    const ctype = request.headers.get("Content-Type") ?? "";
    if (ctype.startsWith("multipart/form-data")) return createFromMultipart(email, courseId, key, request);
    if (!ctype.startsWith("application/json")) return problem(415, "unsupported_type");
    const parsed = await readJson(request);
    if (!parsed.ok) return problem(400, "validation_failed");
    return createSyllabus(email, courseId, key, parsed.body);
  }),

  http.get(`${P}/courses/:courseId/syllabi`, ({ params }) => {
    const email = user();
    if (!email) return problem(401, "unauthenticated");
    const courseId = String(params.courseId);
    if (!ID.test(courseId)) return invalid("courseId", "not_canonical");
    if (!db.findCourse(courseId)) return problem(404, "not_found");
    return HttpResponse.json({ items: sdb.listForCourse(email, courseId), nextCursor: null });
  }),

  http.get(`${P}/syllabi/:id`, ({ params }) => {
    const email = user();
    if (!email) return problem(401, "unauthenticated");
    const id = String(params.id);
    if (!ID.test(id)) return invalid("id", "not_canonical");
    const e = sdb.find(email, id);
    if (!e) return problem(404, "not_found");
    const body = e.syllabus; // the client sees the state at this poll; the next poll sees the next step
    sdb.advance(e);
    return HttpResponse.json(body, { headers: { ETag: body.etag as string } });
  }),

  http.get(`${P}/syllabi/:id/items`, ({ params }) => {
    const email = user();
    if (!email) return problem(401, "unauthenticated");
    const id = String(params.id);
    if (!ID.test(id)) return invalid("id", "not_canonical");
    const e = sdb.find(email, id);
    if (!e) return problem(404, "not_found");
    const sorted = [...e.items].sort((a, b) => String((a.when as Wire).date ?? "9999").localeCompare(String((b.when as Wire).date ?? "9999")) || String(a.id).localeCompare(String(b.id)));
    return HttpResponse.json({ items: sorted, nextCursor: null });
  }),

  http.get(`${P}/syllabi/:id/items/:itemId`, ({ params }) => {
    const email = user();
    if (!email) return problem(401, "unauthenticated");
    const e = sdb.find(email, String(params.id));
    const it = e?.items.find((i) => i.id === params.itemId);
    if (!it) return problem(404, "not_found");
    return HttpResponse.json(it, { headers: { ETag: it.etag as string } });
  }),

  http.patch(`${P}/syllabi/:id/items/:itemId`, async ({ params, request }) => {
    const email = user();
    if (!email) return problem(401, "unauthenticated");
    if (!ID.test(String(params.id)) || !ID.test(String(params.itemId))) return invalid("id", "not_canonical");
    const e = sdb.find(email, String(params.id));
    const idx = e?.items.findIndex((i) => i.id === params.itemId) ?? -1;
    if (!e || idx < 0) return problem(404, "not_found");
    const it = e.items[idx];
    const pre = ifMatch(request, it);
    if (pre) return pre;
    const parsed = await readJson(request);
    if (!parsed.ok) return problem(400, "validation_failed");
    const body = parsed.body;
    const extra = unknownMember(body, ["kind", "title", "notes", "date", "endDate", "time", "endTime", "allDay", "timezone"]);
    if (extra) return invalid(extra, "unknown_member");
    if (Object.keys(body).length === 0) return problem(400, "validation_failed");
    if (it.kind === "policy") return problem(409, "item_not_syncable");
    if ("kind" in body && (typeof body.kind !== "string" || !EDITABLE_KINDS.has(body.kind))) return invalid("kind");
    if ("title" in body && (typeof body.title !== "string" || body.title.length < 1 || BAD_SINGLE_LINE.test(body.title) || codePoints(body.title) > 200)) return invalid("title");
    if ("date" in body && (typeof body.date !== "string" || !realDate(body.date))) return invalid("date");
    if ("endDate" in body && body.endDate !== null && (typeof body.endDate !== "string" || !realDate(body.endDate))) return invalid("endDate");
    for (const k of ["time", "endTime"] as const) if (k in body && body[k] !== null && (typeof body[k] !== "string" || !TIME.test(body[k] as string))) return invalid(k);
    if ("allDay" in body && typeof body.allDay !== "boolean") return invalid("allDay");
    if ("timezone" in body && (typeof body.timezone !== "string" || !ZONE.test(body.timezone))) return invalid("timezone");
    if ("notes" in body && body.notes !== null && (typeof body.notes !== "string" || codePoints(body.notes) > 500)) return invalid("notes");

    const when = { ...(it.when as Wire) };
    const dateFields = ["date", "endDate", "time", "endTime", "allDay", "timezone"].filter((k) => k in body);
    if (when.status === "needs_date" && "date" in body) {
      when.status = "resolved";
      when.unknownReason = null;
      when.alternatives = [];
      when.timezone = (body.timezone as string | undefined) ?? (e.syllabus.context as Wire).defaultTimezone;
    }
    if (when.status === "needs_date" && dateFields.some((k) => k !== "timezone")) return problem(409, "item_needs_date");
    for (const k of dateFields) when[k] = body[k];
    if (when.allDay === true) {
      when.time = null;
      when.endTime = null;
    }
    if (when.endTime != null && when.time == null) return invalid("endTime");
    if (when.endDate != null && when.date != null && String(when.endDate) < String(when.date)) return invalid("endDate");
    const next: Wire = { ...it, when, edited: true };
    for (const k of ["kind", "title", "notes"]) if (k in body) next[k] = body[k];
    if (dateFields.length > 0) {
      when.derivation = "student_entered";
      when.timezoneSource = "student";
      // R2-07: an AI item whose date or time the student changed is "please_check"; a student item may stay probably_right.
      next.confidence = it.origin === "ai" ? "please_check" : "probably_right";
      next.confidenceReason = "You entered this date.";
    }
    // 6.2: approval belongs to the revision that was approved; an approved, unsynced item goes back to undecided.
    if (it.decision === "approved" && it.syncState === "not_synced") next.decision = "undecided";
    if (it.syncState === "synced") next.syncState = "changed_since_sync";
    next.revision = (it.revision as number) + 1;
    next.etag = sdb.nextEtag(it.id as string);
    next.updatedAt = new Date().toISOString();
    e.items[idx] = next;
    touch(e);
    return HttpResponse.json(next, { headers: { ETag: next.etag as string } });
  }),

  http.post(`${P}/syllabi/:id/items/:itemId/decision`, async ({ params, request }) => {
    const email = user();
    if (!email) return problem(401, "unauthenticated");
    if (!ID.test(String(params.id)) || !ID.test(String(params.itemId))) return invalid("id", "not_canonical");
    const e = sdb.find(email, String(params.id));
    const idx = e?.items.findIndex((i) => i.id === params.itemId) ?? -1;
    if (!e || idx < 0) return problem(404, "not_found");
    const it = e.items[idx];
    const pre = ifMatch(request, it);
    if (pre) return pre;
    const parsed = await readJson(request);
    if (!parsed.ok) return problem(400, "validation_failed");
    const body = parsed.body;
    const extra = unknownMember(body, ["decision", "acknowledgeCheck"]);
    if (extra) return invalid(extra, "unknown_member");
    if (body.decision !== "approved" && body.decision !== "skipped" && body.decision !== "undecided") return invalid("decision");
    if ("acknowledgeCheck" in body && typeof body.acknowledgeCheck !== "boolean") return invalid("acknowledgeCheck");
    if (body.decision === "approved") {
      if (it.kind === "policy") return problem(409, "item_not_syncable");
      if ((it.when as Wire).status !== "resolved") return problem(409, "item_needs_date");
      if (it.confidence === "please_check" && body.acknowledgeCheck !== true) return problem(409, "acknowledgement_required");
    }
    if (it.decision === body.decision) return HttpResponse.json(it, { headers: { ETag: it.etag as string } });
    const next: Wire = { ...it, decision: body.decision, etag: sdb.nextEtag(it.id as string), updatedAt: new Date().toISOString() };
    if (body.decision !== "approved") {
      next.syncState = "not_synced"; // withdrawing removes the mock event in the same transaction (6.1)
      next.syncedRevision = null;
      if (e.events) delete e.events[it.id as string];
    }
    e.items[idx] = next;
    touch(e);
    return HttpResponse.json(next, { headers: { ETag: next.etag as string } });
  }),
  ...f2Handlers(),
];

// ---------------------------------------------------------------------------------------------------------------------
// Increment F2: multipart create, delete, batch decisions, mock calendar sync and remove, export. Same faithfulness rule:
// every body is validated against the real schemas in syllabusHandlers.test.ts. Mock-only scenario switches: an item
// title containing "mock:sync-fail" does not go through at sync (provider_unavailable).

const MAX_FILE = 10 * 1024 * 1024;

interface Part {
  name: string;
  filename: string | null;
  bytes: Uint8Array;
}

/**
 * Minimal multipart/form-data reader for the mock (binary-safe via latin1). Request.formData() is not used: under
 * vitest's jsdom globals Node's multipart parser fails, and the mock must behave the same in tests and the browser.
 */
async function readParts(request: Request): Promise<Part[] | null> {
  const m = /boundary=(?:"([^"]+)"|([^;]+))/i.exec(request.headers.get("Content-Type") ?? "");
  const boundary = m?.[1] ?? m?.[2];
  if (!boundary) return null;
  const raw = new Uint8Array(await request.arrayBuffer());
  if (raw.length > MAX_FILE + 64 * 1024) return null;
  let latin = "";
  for (let i = 0; i < raw.length; i += 8192) latin += String.fromCharCode(...raw.subarray(i, i + 8192));
  const chunks = latin.split(`--${boundary}`);
  if (chunks.length < 3 || !chunks[chunks.length - 1].startsWith("--")) return null;
  const parts: Part[] = [];
  for (const c of chunks.slice(1, -1)) {
    const sep = c.indexOf("\r\n\r\n");
    if (!c.startsWith("\r\n") || sep < 0 || !c.endsWith("\r\n")) return null;
    const headers = c.slice(2, sep);
    const cd = /content-disposition:\s*form-data;\s*name="([^"]*)"(?:;\s*filename="([^"]*)")?/i.exec(headers);
    if (!cd) return null;
    const content = c.slice(sep + 4, c.length - 2);
    parts.push({ name: cd[1], filename: cd[2] ?? null, bytes: Uint8Array.from(content, (ch) => ch.charCodeAt(0)) });
  }
  return parts;
}

async function createFromMultipart(email: string, courseId: string, key: string, request: Request): Promise<Response> {
  const parts = await readParts(request);
  if (!parts) return problem(400, "validation_failed");
  // Exactly one file part and one metadata part (contract 5.1 item 2).
  const metaParts = parts.filter((p) => p.name === "metadata");
  const fileParts = parts.filter((p) => p.name === "file");
  if (parts.length !== 2 || metaParts.length !== 1 || fileParts.length !== 1) return problem(400, "validation_failed");
  const [metaPart] = metaParts;
  const [file] = fileParts;
  if (metaPart.bytes.length > 4096) return problem(400, "validation_failed");
  let body: Wire;
  try {
    const v: unknown = JSON.parse(new TextDecoder().decode(metaPart.bytes));
    if (!v || typeof v !== "object" || Array.isArray(v)) return problem(400, "validation_failed");
    body = v as Wire;
  } catch {
    return problem(400, "validation_failed");
  }
  if (file.bytes.length > MAX_FILE) return problem(413, "too_large");
  const bytes = file.bytes;
  const head = String.fromCharCode(...bytes.slice(0, 5));
  const name = file.filename?.toLowerCase() ?? "";
  const ext = name.includes(".") ? name.slice(name.lastIndexOf(".")) : "";
  let fileType: "txt" | "pdf" | "docx";
  if (head === "%PDF-") fileType = "pdf";
  else if (head.startsWith("PK\u0003\u0004")) fileType = "docx";
  else if (bytes.some((b) => b === 0)) return problem(415, "unsupported_type");
  else fileType = "txt";
  if (ext && ext !== `.${fileType}`) return problem(415, "unsupported_type"); // extension and signature must agree
  let text: string | null = null;
  if (fileType === "txt") {
    try {
      text = new TextDecoder("utf-8", { fatal: true, ignoreBOM: false }).decode(bytes);
    } catch {
      return problem(415, "unsupported_type");
    }
    if (!text) return problem(415, "unsupported_type");
  }
  return createSyllabus(email, courseId, key, body, { fileType, text, fingerprint: `${fileType}|${bytes.length}|${sdb.textHash(text ?? String(bytes.length))}` });
}

function eventFor(it: Wire, syllabusId: string): Wire {
  const w = it.when as Wire;
  const allDay = w.allDay === true;
  const time = (w.time as string | null) ?? null;
  const endTime = (w.endTime as string | null) ?? (time ? `${String((Number(time.slice(0, 2)) + 1) % 24).padStart(2, "0")}${time.slice(2)}` : null);
  return {
    eventId: `evt_${String(it.id).replace(/^itm_/, "")}`,
    syllabusId,
    itemId: it.id,
    itemKey: it.itemKey,
    occurrenceDate: null,
    title: it.title,
    kind: it.kind,
    allDay,
    start: { date: w.date, time: allDay ? null : time, timezone: w.timezone },
    end: { date: (w.endDate as string | null) ?? w.date, time: allDay ? null : endTime },
    endDerived: !allDay && w.endTime == null,
    syncedRevision: it.revision,
    syncedAt: new Date().toISOString(),
    demo: true,
  };
}

function owned(params: Record<string, unknown>): { email: string; e: NonNullable<ReturnType<typeof sdb.find>> } | Response {
  const email = user();
  if (!email) return problem(401, "unauthenticated");
  const id = String(params.id);
  if (!ID.test(id)) return invalid("id", "not_canonical");
  const e = sdb.find(email, id);
  if (!e) return problem(404, "not_found");
  return { email, e };
}

function f2Handlers() {
  return [
    http.delete(`${P}/syllabi/:id`, ({ params, request }) => {
      const o = owned(params);
      if (o instanceof Response) return o;
      if (new URL(request.url).search) return invalid("query", "not_allowed"); // DELETE takes no parameters
      sdb.remove(o.email, String(params.id));
      return new HttpResponse(null, { status: 204 });
    }),

    http.post(`${P}/syllabi/:id/decisions`, async ({ params, request }) => {
      const o = owned(params);
      if (o instanceof Response) return o;
      const parsed = await readJson(request);
      if (!parsed.ok) return problem(400, "validation_failed");
      const body = parsed.body;
      const extra = unknownMember(body, ["action", "itemEtags"]);
      if (extra) return invalid(extra, "unknown_member");
      if (body.action !== "approve" && body.action !== "skip" && body.action !== "undecide") return invalid("action");
      const etags = body.itemEtags as Wire | undefined;
      if (!etags || typeof etags !== "object" || Array.isArray(etags)) return invalid("itemEtags");
      const ids = Object.keys(etags);
      if (ids.length < 1 || ids.length > 100) return invalid("itemEtags", "out_of_range");
      if (ids.some((k) => !ID.test(k))) return invalid("itemEtags", "not_canonical");
      const { e } = o;
      const s = e.syllabus;
      const allowed = s.batchApproveAllowed === true;
      const results: Wire[] = [];
      const counts = { applied: 0, unchanged: 0, rejected: 0 };
      const reject = (itemId: string, code: string, item: Wire | null) => {
        counts.rejected++;
        results.push({ itemId, outcome: "rejected", errorCode: code, item });
      };
      for (const itemId of ids) {
        const idx = e.items.findIndex((i) => i.id === itemId);
        if (idx < 0) {
          reject(itemId, "not_found", null);
          continue;
        }
        const it = e.items[idx];
        if (etags[itemId] !== it.etag) {
          reject(itemId, "precondition_failed", it);
          continue;
        }
        const target = body.action === "approve" ? "approved" : body.action === "skip" ? "skipped" : "undecided";
        if (target === "approved") {
          const w = it.when as Wire;
          if (w.status !== "resolved") {
            reject(itemId, it.kind === "policy" ? "item_not_syncable" : "item_needs_date", it);
            continue;
          }
          const snippet = it.snippet as Wire | null;
          if (!allowed || it.confidence !== "looks_right" || it.edited === true || !snippet || snippet.text == null) {
            reject(itemId, "batch_not_eligible", it);
            continue;
          }
        }
        if (it.decision === target) {
          counts.unchanged++;
          results.push({ itemId, outcome: "unchanged", errorCode: null, item: it });
          continue;
        }
        const next: Wire = { ...it, decision: target, etag: sdb.nextEtag(itemId), updatedAt: new Date().toISOString() };
        if (target !== "approved") {
          next.syncState = "not_synced";
          next.syncedRevision = null;
          if (e.events) delete e.events[itemId];
        }
        e.items[idx] = next;
        counts.applied++;
        results.push({ itemId, outcome: "applied", errorCode: null, item: next });
      }
      touch(e);
      return HttpResponse.json({ results, counts });
    }),

    http.post(`${P}/syllabi/:id/calendar-sync`, async ({ params, request }) => {
      const key = request.headers.get("Idempotency-Key");
      const o = owned(params);
      if (o instanceof Response) return o;
      if (!key || !KEY.test(key)) return invalid("Idempotency-Key", key ? "invalid" : "required");
      const parsed = await readJson(request);
      if (!parsed.ok) return problem(400, "validation_failed");
      const body = parsed.body;
      const extra = unknownMember(body, ["itemRevisions", "confirm"]);
      if (extra) return invalid(extra, "unknown_member");
      const confirm = body.confirm as Wire | undefined;
      if (!confirm || confirm.provider !== "mock" || confirm.acknowledgedDemo !== true || unknownMember(confirm, ["provider", "acknowledgedDemo"])) return invalid("confirm");
      const revs = body.itemRevisions as Wire | undefined;
      if (!revs || typeof revs !== "object" || Array.isArray(revs)) return invalid("itemRevisions");
      const ids = Object.keys(revs);
      if (ids.length < 1 || ids.length > 100) return invalid("itemRevisions", "out_of_range");
      if (ids.some((k) => !ID.test(k) || typeof revs[k] !== "number" || !Number.isInteger(revs[k]) || (revs[k] as number) < 1)) return invalid("itemRevisions");
      const { email, e } = o;
      const sid = String(params.id);
      const fingerprint = `${sid}|${JSON.stringify(Object.entries(revs).sort())}`;
      const prior = sdb.syncIdemGet(email, key);
      if (prior) {
        if (prior.fingerprint !== fingerprint) return invalid("Idempotency-Key", "reused");
        return HttpResponse.json(prior.body, { headers: { "Idempotency-Replayed": "true" } });
      }
      if (e.syllabus.status !== "ready" && e.syllabus.status !== "partial") return problem(409, "invalid_state");
      e.events ??= {};
      const results: Wire[] = [];
      const counts = { created: 0, updated: 0, unchanged: 0, failed: 0, notEligible: 0 };
      const order = [...ids].sort((a, b) => {
        const da = String(((e.items.find((i) => i.id === a)?.when as Wire | undefined)?.date as string | undefined) ?? "9999");
        const dbb = String(((e.items.find((i) => i.id === b)?.when as Wire | undefined)?.date as string | undefined) ?? "9999");
        return da.localeCompare(dbb) || a.localeCompare(b);
      });
      for (const itemId of order) {
        const idx = e.items.findIndex((i) => i.id === itemId);
        const it = idx >= 0 ? e.items[idx] : null;
        const itemKey = it ? it.itemKey : `ik_${"A".repeat(32)}`;
        if (!it) {
          counts.notEligible++;
          results.push({ itemId, itemKey, outcome: "not_eligible", errorCode: "not_found", eventCount: 0, syncState: "not_synced" });
          continue;
        }
        const w = it.when as Wire;
        if (it.kind === "policy" || w.status !== "resolved" || it.decision !== "approved") {
          counts.notEligible++;
          const code = it.kind === "policy" ? "item_not_syncable" : w.status !== "resolved" ? "item_needs_date" : "item_not_approved";
          results.push({ itemId, itemKey, outcome: "not_eligible", errorCode: code, eventCount: 0, syncState: it.syncState });
          continue;
        }
        if (it.revision !== revs[itemId]) {
          counts.failed++;
          results.push({ itemId, itemKey, outcome: "failed", errorCode: "precondition_failed", eventCount: 0, syncState: it.syncState });
          continue;
        }
        if (String(it.title).includes("mock:sync-fail")) {
          counts.failed++;
          e.items[idx] = { ...it, syncState: "sync_failed", etag: sdb.nextEtag(itemId), updatedAt: new Date().toISOString() };
          results.push({ itemId, itemKey, outcome: "failed", errorCode: "provider_unavailable", eventCount: 0, syncState: "sync_failed" });
          continue;
        }
        const existing = e.events[itemId];
        const ev = eventFor(it, sid);
        const outcome = !existing ? "created" : existing.syncedRevision === it.revision && existing.title === it.title ? "unchanged" : "updated";
        if (outcome === "unchanged") counts.unchanged++;
        else if (outcome === "created") counts.created++;
        else counts.updated++;
        if (outcome !== "unchanged") e.events[itemId] = ev;
        if (it.syncState !== "synced" || it.syncedRevision !== it.revision)
          e.items[idx] = { ...it, syncState: "synced", syncedRevision: it.revision, etag: sdb.nextEtag(itemId), updatedAt: new Date().toISOString() };
        results.push({ itemId, itemKey, outcome, errorCode: null, eventCount: 1, syncState: "synced" });
      }
      const ok = counts.created + counts.updated + counts.unchanged;
      const problems = counts.failed + counts.notEligible;
      const status = problems === 0 ? (counts.created + counts.updated === 0 ? "nothing_to_do" : "all_succeeded") : ok === 0 ? "failed" : "partial";
      const out = { syncId: `syn_${(Date.now() % 1e9).toString(36)}${Math.random().toString(36).slice(2, 8)}`, syllabusId: sid, provider: "mock", demoCalendar: true, status, completedAt: new Date().toISOString(), results, counts };
      sdb.syncIdemPut(email, key, fingerprint, sid, out);
      touch(e);
      return HttpResponse.json(out);
    }),

    http.post(`${P}/syllabi/:id/calendar-sync/remove`, async ({ params, request }) => {
      const o = owned(params);
      if (o instanceof Response) return o;
      const parsed = await readJson(request);
      if (!parsed.ok) return problem(400, "validation_failed");
      const body = parsed.body;
      const extra = unknownMember(body, ["itemIds"]);
      if (extra) return invalid(extra, "unknown_member");
      const ids = body.itemIds;
      if (!Array.isArray(ids) || ids.length < 1 || ids.length > 100 || new Set(ids).size !== ids.length || ids.some((x) => typeof x !== "string" || !ID.test(x))) return invalid("itemIds");
      const { e } = o;
      e.events ??= {};
      const results: Wire[] = [];
      const counts = { removed: 0, notPresent: 0, failed: 0 };
      for (const itemId of ids as string[]) {
        const idx = e.items.findIndex((i) => i.id === itemId);
        const it = idx >= 0 ? e.items[idx] : null;
        if (!it) {
          counts.failed++;
          results.push({ itemId, itemKey: `ik_${"A".repeat(32)}`, outcome: "failed", errorCode: "not_found", eventCount: 0, syncState: "not_synced" });
          continue;
        }
        if (e.events[itemId]) {
          delete e.events[itemId];
          counts.removed++;
          e.items[idx] = { ...it, syncState: "not_synced", syncedRevision: null, etag: sdb.nextEtag(itemId), updatedAt: new Date().toISOString() };
          results.push({ itemId, itemKey: it.itemKey, outcome: "removed", errorCode: null, eventCount: 1, syncState: "not_synced" });
        } else {
          counts.notPresent++;
          results.push({ itemId, itemKey: it.itemKey, outcome: "not_present", errorCode: null, eventCount: 0, syncState: it.syncState });
        }
      }
      touch(e);
      const status = counts.failed > 0 ? (counts.removed + counts.notPresent > 0 ? "partial" : "failed") : counts.removed === 0 ? "nothing_to_do" : "all_succeeded";
      return HttpResponse.json({ syllabusId: String(params.id), provider: "mock", status, results, counts });
    }),

    http.get(`${P}/syllabi/:id/export`, ({ params }) => {
      const o = owned(params);
      if (o instanceof Response) return o;
      const { e } = o;
      const body = { schemaVersion: "syllabus.v1", exportedAt: new Date().toISOString(), syllabus: e.syllabus, items: e.items, unclaimedDates: [], calendarEvents: Object.values(e.events ?? {}) };
      return HttpResponse.json(body, { headers: { "Cache-Control": "no-store", "Content-Disposition": 'attachment; filename="syllabus-export.json"', "X-Content-Type-Options": "nosniff" } });
    }),
  ];
}
