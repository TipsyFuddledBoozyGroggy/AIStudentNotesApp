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

export const syllabusHandlers = [
  http.post(`${P}/courses/:courseId/syllabi`, async ({ params, request }) => {
    const email = user();
    if (!email) return problem(401, "unauthenticated");
    const key = request.headers.get("Idempotency-Key");
    if (!key || !KEY.test(key)) return invalid("Idempotency-Key", key ? "invalid" : "required");
    const courseId = String(params.courseId);
    if (!ID.test(courseId)) return invalid("courseId", "not_canonical");
    if (!db.findCourse(courseId)) return problem(404, "not_found");
    if (!(request.headers.get("Content-Type") ?? "").startsWith("application/json")) return problem(415, "unsupported_type");
    const parsed = await readJson(request);
    if (!parsed.ok) return problem(400, "validation_failed");
    const body = parsed.body;
    const fingerprint = `${courseId}|${JSON.stringify(body, Object.keys(body).sort())}|${JSON.stringify(body.attestation ?? null)}`;
    const prior = sdb.idemGet(email, key);
    if (prior) {
      if (prior.fingerprint !== fingerprint) return invalid("Idempotency-Key", "reused");
      const e = sdb.find(email, prior.syllabusId);
      if (!e) return problem(404, "not_found"); // replay after delete (tombstone)
      return HttpResponse.json(e.syllabus, { status: 202, headers: { "Idempotency-Replayed": "true", Location: `/api/v1/syllabi/${prior.syllabusId}` } });
    }
    const extra = unknownMember(body, ["title", "text", "termStart", "termEnd", "defaultTimezone", "allowDuplicate", "noticeVersion", "attestation"]);
    if (extra) return invalid(extra, "unknown_member");
    const text = body.text;
    if (typeof text !== "string" || text.length < 1) return invalid("text", "required");
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
    const dup = sdb.findDuplicate(email, courseId, sdb.textHash(text));
    if (dup && body.allowDuplicate !== true) return problem(409, "duplicate_syllabus", { existingSyllabusId: dup });
    const m = /mock:([a-z-]+)/.exec(text);
    const outcome: Scenario = m && (SCENARIOS as string[]).includes(m[1]) ? (m[1] as Scenario) : "ready";
    const s = sdb.createFromPaste(email, courseId, body, outcome);
    sdb.idemPut(email, key, fingerprint, s.id as string);
    return HttpResponse.json(s, { status: 202, headers: { Location: `/api/v1/syllabi/${s.id as string}` } });
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
    }
    e.items[idx] = next;
    touch(e);
    return HttpResponse.json(next, { headers: { ETag: next.etag as string } });
  }),
];
