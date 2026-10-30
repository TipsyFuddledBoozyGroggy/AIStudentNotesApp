import { describe, expect, it } from "vitest";
import { loadSyllabusValidators } from "@/lib/syllabus/schemas.node";
import { createSyllabusApi, SyllabusApiError } from "@/lib/syllabus/client";
import { NOTICE_VERSION } from "@/lib/syllabus/requests";
import * as db from "./db";
import { SCENARIOS } from "./syllabusData";
import * as sdb from "./syllabusDb";

// Faithfulness of the MSW syllabus mock to syllabus.v1: every body it returns validates against the real schemas
// (ajv, u flag), and the contract rules the client relies on are enforced.

const V = loadSyllabusValidators();
const BASE = "http://localhost/api/v1";
const EMAIL = "alice@example.edu";
const COURSE = "course-bio101";
const api = createSyllabusApi({ baseUrl: BASE });

function setup() {
  db.signIn(EMAIL);
  db.seedCourses(EMAIL, [{ id: COURSE, name: "Biology 101", createdAt: "2026-01-01T00:00:00Z", updatedAt: "2026-01-01T00:00:00Z" }]);
}

async function raw(path: string, init: RequestInit = {}) {
  const res = await fetch(`${BASE}${path}`, { ...init, headers: { "Content-Type": "application/json", ...init.headers } });
  return { res, body: (await res.json().catch(() => null)) as Record<string, unknown> | null };
}

function expectValid(schema: string, body: unknown) {
  expect(V.byName(schema)(body), `${schema}: ${V.explain(schema)}`).toBe(true);
}

const pasteBody = (text = "Week 1: Quiz 1 due Sept 14.") => ({
  text,
  defaultTimezone: "America/New_York",
  noticeVersion: NOTICE_VERSION,
  attestation: { accepted: true, acceptedAt: "2026-10-30T12:00:00.000Z" },
});

describe("syllabus mock: every scenario is schema-valid", () => {
  it.each(SCENARIOS)("scenario %s: syllabus, page, items and each item validate", async (scenario) => {
    setup();
    sdb.seedSyllabus(EMAIL, COURSE, scenario);
    const id = `syl_${scenario}`;
    const one = await raw(`/syllabi/${id}`);
    expect(one.res.status).toBe(200);
    expectValid("syllabus", one.body);
    const page = await raw(`/courses/${COURSE}/syllabi`);
    expectValid("syllabus-page", page.body);
    const items = await raw(`/syllabi/${id}/items`);
    expectValid("syllabus-item-page", items.body);
    for (const it of (items.body?.items as unknown[]) ?? []) expectValid("syllabus-item", it);
    // and the strict client parser accepts them
    await expect(api.getSyllabus(id)).resolves.toMatchObject({ id });
    await expect(api.listItems(id)).resolves.toBeInstanceOf(Array);
  });

  it("scenario facts: hostile blocks batch, purged has null snippets, partial names unread pages", async () => {
    setup();
    for (const s of ["hostile", "purged", "partial"] as const) sdb.seedSyllabus(EMAIL, COURSE, s);
    const hostile = await api.getSyllabus("syl_hostile");
    expect(hostile.batchApproveAllowed).toBe(false);
    expect(hostile.document.injectionSuspect).toBe(true);
    expect((await api.listItems("syl_hostile")).every((i) => i.confidence === "please_check")).toBe(true);
    const purged = await api.getSyllabus("syl_purged");
    expect(purged.retention.canonicalTextPurged).toBe(true);
    expect((await api.listItems("syl_purged")).every((i) => i.snippet?.text === null && i.snippet.context === null)).toBe(true);
    const partial = await api.getSyllabus("syl_partial");
    expect(partial.status).toBe("partial");
    expect(partial.unprocessedRanges).toEqual([{ fromPage: 5, toPage: 6, reason: "budget_limited" }]);
  });
});

describe("syllabus mock: create, poll, idempotency", () => {
  it("paste create is 202 queued, polls through the steps to ready, each body schema-valid", async () => {
    setup();
    const created = await raw(`/courses/${COURSE}/syllabi`, { method: "POST", headers: { "Idempotency-Key": "key-aaaaaaaa" }, body: JSON.stringify(pasteBody()) });
    expect(created.res.status).toBe(202);
    expectValid("syllabus", created.body);
    expect(created.body?.status).toBe("queued");
    const id = created.body?.id as string;
    const seen: string[] = [];
    for (let i = 0; i < 5; i++) {
      const s = await raw(`/syllabi/${id}`);
      expectValid("syllabus", s.body);
      seen.push(s.body?.status as string);
    }
    expect(seen).toEqual(["queued", "extracting", "proposing", "ready", "ready"]);
    const items = await raw(`/syllabi/${id}/items`);
    expectValid("syllabus-item-page", items.body);
    expect((items.body?.items as unknown[]).length).toBeGreaterThan(5);
  });

  it("same key and body is a replay (one syllabus); same key with another body is 400; missing or bad key is 400", async () => {
    setup();
    const h = { "Idempotency-Key": "key-bbbbbbbb" };
    const a = await raw(`/courses/${COURSE}/syllabi`, { method: "POST", headers: h, body: JSON.stringify(pasteBody()) });
    const b = await raw(`/courses/${COURSE}/syllabi`, { method: "POST", headers: h, body: JSON.stringify(pasteBody()) });
    expect(b.res.headers.get("Idempotency-Replayed")).toBe("true");
    expect(b.body?.id).toBe(a.body?.id);
    expect(sdb.listForCourse(EMAIL, COURSE)).toHaveLength(1);
    const c = await raw(`/courses/${COURSE}/syllabi`, { method: "POST", headers: h, body: JSON.stringify(pasteBody("Other text")) });
    expect(c.res.status).toBe(400);
    expectValid("syllabus-problem", c.body);
    expect((await raw(`/courses/${COURSE}/syllabi`, { method: "POST", body: JSON.stringify(pasteBody()) })).res.status).toBe(400);
    expect((await raw(`/courses/${COURSE}/syllabi`, { method: "POST", headers: { "Idempotency-Key": "short" }, body: JSON.stringify(pasteBody()) })).res.status).toBe(400);
  });

  it("rejects unknown and read-only members, and a missing attestation", async () => {
    setup();
    const post = (body: unknown, key: string) => raw(`/courses/${COURSE}/syllabi`, { method: "POST", headers: { "Idempotency-Key": key }, body: JSON.stringify(body) });
    expect((await post({ ...pasteBody(), status: "ready" }, "key-cccccccc")).res.status).toBe(400);
    const noAtt: Record<string, unknown> = { ...pasteBody() };
    delete noAtt.attestation;
    expect((await post(noAtt, "key-dddddddd")).res.status).toBe(400);
  });

  it("too much text is 413, one extraction in flight is 409, foreign course is 404 (all schema-valid problems)", async () => {
    setup();
    const post = (body: unknown, key: string, course = COURSE) => raw(`/courses/${course}/syllabi`, { method: "POST", headers: { "Idempotency-Key": key }, body: JSON.stringify(body) });
    const big = await post(pasteBody("x".repeat(50001)), "key-eeeeeeee");
    expect(big.res.status).toBe(413);
    expectValid("syllabus-problem", big.body);
    await post(pasteBody(), "key-ffffffff");
    const second = await post(pasteBody("Different text"), "key-gggggggg");
    expect(second.res.status).toBe(409);
    expect(second.body?.code).toBe("extraction_in_progress");
    expect((await post(pasteBody(), "key-hhhhhhhh", "course-nope")).res.status).toBe(404);
  });

  it("a foreign syllabus is 404, never 403", async () => {
    setup();
    sdb.seedSyllabus("bob@example.edu", COURSE, "ready", "syl_bobs");
    const r = await raw(`/syllabi/syl_bobs`);
    expect(r.res.status).toBe(404);
    expectValid("syllabus-problem", r.body);
  });
});

describe("syllabus mock: decisions and edits follow the contract", () => {
  async function items() {
    return api.listItems("syl_ready");
  }

  it("If-Match: missing is 428, stale is 412", async () => {
    setup();
    sdb.seedSyllabus(EMAIL, COURSE, "ready");
    const it = (await items()).find((i) => i.title === "Midterm exam")!;
    const no = await raw(`/syllabi/syl_ready/items/${it.id}/decision`, { method: "POST", body: JSON.stringify({ decision: "approved" }) });
    expect(no.res.status).toBe(428);
    const stale = await raw(`/syllabi/syl_ready/items/${it.id}/decision`, { method: "POST", headers: { "If-Match": 'W/"old"' }, body: JSON.stringify({ decision: "approved" }) });
    expect(stale.res.status).toBe(412);
    expectValid("syllabus-problem", stale.body);
  });

  it("approve, undo, skip; please_check needs acknowledgeCheck; needs_date and policy cannot be approved", async () => {
    setup();
    sdb.seedSyllabus(EMAIL, COURSE, "ready");
    const all = await items();
    const mid = all.find((i) => i.title === "Midterm exam")!;
    const approved = await api.decide("syl_ready", mid.id, "approved", mid.etag);
    expect(approved.decision).toBe("approved");
    expect(approved.etag).not.toBe(mid.etag);
    const undone = await api.decide("syl_ready", mid.id, "undecided", approved.etag);
    expect(undone.decision).toBe("undecided");

    const lab = all.find((i) => i.confidence === "please_check" && i.when.status === "resolved")!;
    await expect(api.decide("syl_ready", lab.id, "approved", lab.etag)).rejects.toMatchObject({ status: 409, code: "acknowledgement_required" });
    await expect(api.decide("syl_ready", lab.id, "approved", lab.etag, true)).resolves.toMatchObject({ decision: "approved" });

    const tba = all.find((i) => i.when.status === "needs_date")!;
    await expect(api.decide("syl_ready", tba.id, "approved", tba.etag, true)).rejects.toMatchObject({ code: "item_needs_date" });
    await expect(api.decide("syl_ready", tba.id, "skipped", tba.etag)).resolves.toMatchObject({ decision: "skipped" });
    const pol = all.find((i) => i.kind === "policy")!;
    await expect(api.decide("syl_ready", pol.id, "approved", pol.etag, true)).rejects.toBeInstanceOf(SyllabusApiError);
  });

  it("PATCH: date on a needs-date item resolves it (still undecided); editing an AI date drops to please_check; editing an approved item un-approves it", async () => {
    setup();
    sdb.seedSyllabus(EMAIL, COURSE, "ready");
    const all = await items();
    const tba = all.find((i) => i.when.status === "needs_date")!;
    const dated = await api.updateItem("syl_ready", tba.id, { date: "2026-12-09" }, tba.etag);
    expect(dated.when.status).toBe("resolved");
    expect(dated.when.derivation).toBe("student_entered");
    expect(dated.decision).toBe("undecided");
    expect(dated.confidence).toBe("please_check");
    expectValid("syllabus-item", (await raw(`/syllabi/syl_ready/items/${tba.id}`)).body);

    const mid = all.find((i) => i.title === "Midterm exam")!;
    const ok = await api.decide("syl_ready", mid.id, "approved", mid.etag);
    const edited = await api.updateItem("syl_ready", mid.id, { title: "Midterm exam (room 4)" }, ok.etag);
    expect(edited).toMatchObject({ decision: "undecided", edited: true, confidence: "looks_right", revision: 2 });
    const r = await raw(`/syllabi/syl_ready/items/${mid.id}`, { method: "PATCH", headers: { "If-Match": edited.etag }, body: JSON.stringify({ snippet: null }) });
    expect(r.res.status).toBe(400);
    expect((await raw(`/syllabi/syl_ready/items/${mid.id}`, { method: "PATCH", headers: { "If-Match": edited.etag }, body: JSON.stringify({ date: "2026-02-30" }) })).res.status).toBe(400);
  });
});
