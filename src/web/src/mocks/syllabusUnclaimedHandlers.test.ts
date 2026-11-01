import { describe, expect, it } from "vitest";
import { createSyllabusApi } from "@/lib/syllabus/client";
import { loadSyllabusValidators } from "@/lib/syllabus/schemas.node";
import { NOTICE_VERSION } from "@/lib/syllabus/requests";
import * as db from "./db";
import { buildUnclaimed, SCENARIOS } from "./syllabusData";
import * as sdb from "./syllabusDb";

// PO-20b: the MSW unclaimed-dates page and student item create are faithful to syllabus.v1 (every body validates
// against the real schemas) and to the server rules the UI depends on (409 invalid_state outside ready/partial and past
// the student-item cap, Idempotency-Key replay, unknown members refused).

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

const valid = (schema: string, body: unknown) => expect(V.byName(schema)(body), `${schema}: ${V.explain(schema)}`).toBe(true);
const create = (body: Record<string, unknown>, key = `key-${Math.random().toString(36).slice(2, 12)}`, id = "syl_ready") =>
  raw(`/syllabi/${id}/items`, { method: "POST", headers: { "Idempotency-Key": key }, body: JSON.stringify(body) });
const BODY = { kind: "other", title: "Bring a calculator on Oct 5", date: "2026-10-05", allDay: true, time: null };

describe("GET /syllabi/{id}/unclaimed-dates (mock)", () => {
  it.each(SCENARIOS)("scenario %s keeps counts.unclaimedDates 0 and an empty, schema-valid page", async (s) => {
    setup();
    sdb.seedSyllabus(EMAIL, COURSE, s);
    expect((await api.getSyllabus(`syl_${s}`)).counts.unclaimedDates).toBe(0);
    const r = await raw(`/syllabi/syl_${s}/unclaimed-dates`);
    expect(r.res.status).toBe(200);
    valid("syllabus-unclaimed-date-page", r.body);
    expect(r.body?.items).toEqual([]);
  });

  it("seeded rows: the count follows them, pages are schema-valid, cursors chain, limit is 1..200", async () => {
    setup();
    sdb.seedSyllabus(EMAIL, COURSE, "ready");
    sdb.seedUnclaimed(EMAIL, "syl_ready", buildUnclaimed("syl_ready", 200));
    const s = await raw(`/syllabi/syl_ready`);
    valid("syllabus", s.body);
    expect((s.body?.counts as Record<string, number>).unclaimedDates).toBe(200);
    const p1 = await raw(`/syllabi/syl_ready/unclaimed-dates?limit=150`);
    valid("syllabus-unclaimed-date-page", p1.body);
    for (const row of p1.body?.items as unknown[]) valid("syllabus-unclaimed-date", row);
    expect(p1.body?.items).toHaveLength(150);
    const p2 = await raw(`/syllabi/syl_ready/unclaimed-dates?limit=150&cursor=${p1.body?.nextCursor as string}`);
    expect(p2.body?.items).toHaveLength(50);
    expect(p2.body?.nextCursor).toBeNull();
    expect((await raw(`/syllabi/syl_ready/unclaimed-dates?limit=201`)).res.status).toBe(400);
    expect((await raw(`/syllabi/syl_ready/unclaimed-dates?cursor=zz`)).res.status).toBe(400);
    expect(await api.listUnclaimedDates("syl_ready")).toHaveLength(200);
  });

  it("a paste with mock:unclaimed-200 finishes with 200 rows; mock:unclaimed with 3", async () => {
    setup();
    for (const [marker, n, key] of [["mock:unclaimed-200", 200, "key-u200aaaa"], ["mock:unclaimed", 3, "key-u3aaaaaa"]] as const) {
      const c = await raw(`/courses/${COURSE}/syllabi`, {
        method: "POST",
        headers: { "Idempotency-Key": key },
        body: JSON.stringify({ text: `Dates ${marker}`, defaultTimezone: "America/New_York", noticeVersion: NOTICE_VERSION, attestation: { accepted: true, acceptedAt: "2026-10-30T12:00:00.000Z" } }),
      });
      const id = c.body?.id as string;
      let s = c.body;
      for (let i = 0; i < 5 && s?.status !== "ready"; i++) s = (await raw(`/syllabi/${id}`)).body;
      valid("syllabus", s);
      expect((s?.counts as Record<string, number>).unclaimedDates).toBe(n);
      expect(await api.listUnclaimedDates(id)).toHaveLength(n);
    }
  });

  it("another user's syllabus is 404", async () => {
    setup();
    sdb.seedSyllabus("bob@example.edu", COURSE, "ready", "syl_bobs");
    const r = await raw(`/syllabi/syl_bobs/unclaimed-dates`);
    expect(r.res.status).toBe(404);
    valid("syllabus-problem", r.body);
  });
});

describe("POST /syllabi/{id}/items (mock)", () => {
  it("201 is a schema-valid student item (no snippet, probably_right, undecided, student_entered) and the counts follow", async () => {
    setup();
    sdb.seedSyllabus(EMAIL, COURSE, "ready");
    const before = await api.getSyllabus("syl_ready");
    const r = await create(BODY);
    expect(r.res.status).toBe(201);
    valid("syllabus-item", r.body);
    expect(r.body).toMatchObject({ origin: "student", snippet: null, confidence: "probably_right", decision: "undecided", kind: "other" });
    expect(r.body?.when).toMatchObject({ status: "resolved", date: "2026-10-05", allDay: true, derivation: "student_entered", timezone: "America/New_York" });
    const after = await api.getSyllabus("syl_ready");
    expect(after.counts.total).toBe(before.counts.total + 1);
    expect((await api.listItems("syl_ready")).some((i) => i.id === r.body?.id)).toBe(true);
  });

  it("same key and body replays the same item; another body with that key is 400; unknown members are 400", async () => {
    setup();
    sdb.seedSyllabus(EMAIL, COURSE, "ready");
    const a = await create(BODY, "key-replay01");
    const b = await create(BODY, "key-replay01");
    expect(b.res.headers.get("Idempotency-Replayed")).toBe("true");
    expect(b.body?.id).toBe(a.body?.id);
    expect((await create({ ...BODY, title: "Other" }, "key-replay01")).res.status).toBe(400);
    const extra = await create({ ...BODY, fromUnclaimedDateId: "unc_1" });
    expect(extra.res.status).toBe(400);
    valid("syllabus-problem", extra.body);
    expect((await create({ ...BODY, date: "" })).res.status).toBe(400);
    expect((await create({ kind: "other", title: "No date" })).res.status).toBe(400);
  });

  it("409 invalid_state outside ready/partial, and the same code at the 100-student-item cap", async () => {
    setup();
    sdb.seedSyllabus(EMAIL, COURSE, "processing");
    const r = await create(BODY, undefined, "syl_processing");
    expect(r.res.status).toBe(409);
    expect(r.body?.code).toBe("invalid_state");
    valid("syllabus-problem", r.body);

    sdb.seedSyllabus(EMAIL, COURSE, "ready");
    for (let i = 0; i < 100; i++) expect((await create({ ...BODY, title: `Item ${i}` })).res.status).toBe(201);
    const capped = await create({ ...BODY, title: "One more" });
    expect(capped.res.status).toBe(409);
    expect(capped.body?.code).toBe("invalid_state");
  });
});
