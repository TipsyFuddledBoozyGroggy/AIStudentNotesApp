import { describe, expect, it } from "vitest";
import { loadFixtures } from "@/lib/lecture/schemas.node";
import { parseBatchResult, parseItem, parseItemPage, parseSyllabus, parseSyllabusPage, parseUnclaimed, parseUnclaimedPage } from "./parse";
import { batchDecisionBody, calendarSyncBody, createFromTextBody, decisionBody, itemCreateBody, newIdempotencyKey, NOTICE_VERSION, patchBody } from "./requests";
import { loadSyllabusSchemas, loadSyllabusValidators } from "./schemas.node";

// R3-07 (docs/API_CONTRACTS_P2.md sections 2.2, 12, 17 item 6): an ajv CODE-POINT validator (the `u` flag) runs in the
// web suite against the real syllabus.v1 schemas and the shared fixtures, before the web client consumes them.
// Result pinned below: under `u`, the five astral TextPolicy red-team fixtures VALIDATE (their exclusions are UTF-16
// surrogate-pair lookaheads, which match nothing in a code-point engine). The client therefore never treats schema
// validity as TextPolicy validity; the server TextPolicy is authoritative.

const cp = loadSyllabusValidators({ unicodeRegExp: true });
const utf16 = loadSyllabusValidators({ unicodeRegExp: false });
const names = new Set(cp.names());
const valid = loadFixtures("valid").filter((f) => names.has(f.schema));
const invalid = loadFixtures("invalid").filter((f) => names.has(f.schema));

/** Invalid fixtures a code-point engine accepts. Exactly these, no more: a new entry means a new engine gap to review. */
const CODE_POINT_ENGINE_GAP = [
  "syllabus-item-create-request--redteam-musical-format-control-title.json",
  "syllabus-item-create-request--redteam-private-use-plane-15-title.json",
  "syllabus-item-create-request--redteam-tag-char-title.json",
  "syllabus-item-create-request--redteam-variation-selector-supplement-title.json",
  "syllabus-item-update-request--redteam-tag-char-title.json",
].sort();

describe("syllabus.v1 schemas under ajv with the u flag (code-point engine, R3-07)", () => {
  it("registers and compiles every schema file with the u flag", () => {
    expect(cp.names().length).toBe(loadSyllabusSchemas().length);
    expect(cp.names().length).toBeGreaterThanOrEqual(20);
    for (const n of cp.names()) expect(typeof cp.byName(n)).toBe("function");
  });

  it("has fixtures to run", () => {
    expect(valid.length).toBeGreaterThan(60);
    expect(invalid.length).toBeGreaterThan(200);
  });

  it.each(valid.map((f) => [f.file, f] as const))("valid fixture passes: %s", (_n, f) => {
    expect(cp.byName(f.schema)(f.json), cp.explain(f.schema)).toBe(true);
  });

  it("every invalid fixture is rejected except the pinned astral TextPolicy rows", () => {
    const accepted = invalid.filter((f) => cp.byName(f.schema)(f.json)).map((f) => f.file).sort();
    expect(accepted).toEqual(CODE_POINT_ENGINE_GAP);
  });

  it("the gap is the engine, not the schema: a UTF-16 engine (no u flag) rejects all of them", () => {
    for (const f of invalid) expect([f.file, utf16.byName(f.schema)(f.json)]).toEqual([f.file, false]);
    for (const f of valid) expect([f.file, utf16.byName(f.schema)(f.json)]).toEqual([f.file, true]);
  });

  it("the astral rows really contain astral code points (so the pin is about R3-07, not a typo)", () => {
    for (const file of CODE_POINT_ENGINE_GAP) {
      const f = invalid.find((x) => x.file === file);
      expect(f, file).toBeDefined();
      expect(/[\u{10000}-\u{10FFFF}]/u.test(JSON.stringify(f!.json))).toBe(true);
    }
  });

  it("maxLength counts code points under ajv: 200 astral characters is valid (fixture), 201 is not", () => {
    const f = valid.find((x) => x.file === "syllabus-item-create-request--textpolicy-astral-200-code-points-title.json")!;
    const body = f.json as { title: string };
    expect(cp.byName(f.schema)(body)).toBe(true);
    expect(cp.byName(f.schema)({ ...body, title: body.title + "\u{1F600}" })).toBe(false);
  });

  it("explain() never echoes offending values", () => {
    cp.byName("syllabus-item-update-request")({ title: "SECRET-CANARY\n" });
    expect(cp.explain("syllabus-item-update-request")).not.toContain("SECRET-CANARY");
  });
});

describe("request builders produce schema-valid bodies", () => {
  it("paste create (term dates both or neither: Lead decision 61)", () => {
    const base = { text: "Week 1: Quiz 1 due Sept 14.", defaultTimezone: "America/New_York", noticeVersion: NOTICE_VERSION, acceptedAt: "2026-10-30T12:00:00.000Z" };
    const v = cp.byName("syllabus-create-request");
    expect(v(createFromTextBody(base)), cp.explain("syllabus-create-request")).toBe(true);
    expect(v(createFromTextBody({ ...base, termStart: "2026-08-24", termEnd: "2026-12-11" }))).toBe(true);
    expect(v(createFromTextBody({ ...base, termStart: "2026-08-24" }))).toBe(false); // a lone date is invalid under the amended schema
    expect(v(createFromTextBody({ ...base, termEnd: "2026-12-11" }))).toBe(false);
    expect(createFromTextBody(base)).not.toHaveProperty("termStart");
    expect(createFromTextBody(base)).not.toHaveProperty("allowDuplicate");
  });

  it("decision, patch, batch and sync bodies", () => {
    expect(cp.byName("syllabus-item-decision-request")(decisionBody("approved", true))).toBe(true);
    expect(cp.byName("syllabus-item-decision-request")(decisionBody("undecided"))).toBe(true);
    expect(decisionBody("skipped")).toEqual({ decision: "skipped" });
    const patch = patchBody({ title: "Midterm exam", date: "2026-10-14", time: null, endTime: null, allDay: true, notes: undefined });
    expect(patch).toEqual({ title: "Midterm exam", date: "2026-10-14", time: null, endTime: null, allDay: true });
    expect(cp.byName("syllabus-item-update-request")(patch)).toBe(true);
    expect(() => patchBody({})).toThrow();
    const batch = batchDecisionBody("approve", new Map([["itm_01", 'W/"i1"'], ["itm_02", 'W/"i2"']]));
    expect(cp.byName("syllabus-decision-batch-request")(batch)).toBe(true);
    expect(cp.byName("syllabus-calendar-sync-request")(calendarSyncBody(new Map([["itm_01", 3]])))).toBe(true);
  });

  it("PO-20b: the student item create body is schema-valid with and without a time, and sends no timezone or notes", () => {
    const v = cp.byName("syllabus-item-create-request");
    const allDay = itemCreateBody({ kind: "other", title: "Bring a calculator on Oct 5 for the in-class exercise.", date: "2026-10-05", allDay: true, time: "09:00" });
    expect(allDay).toEqual({ kind: "other", title: "Bring a calculator on Oct 5 for the in-class exercise.", date: "2026-10-05", allDay: true, time: null });
    expect(v(allDay), cp.explain("syllabus-item-create-request")).toBe(true);
    const timed = itemCreateBody({ kind: "exam", title: "Quiz", date: "2026-10-05", allDay: false, time: "09:00" });
    expect(v(timed)).toBe(true);
    expect(Object.keys(timed).sort()).toEqual(["allDay", "date", "kind", "time", "title"]);
  });

  it("idempotency keys match the header schema", () => {
    for (let i = 0; i < 20; i++) expect(cp.byName("syllabus-idempotency-key")(newIdempotencyKey())).toBe(true);
  });
});

describe("strict parsers over the shared fixtures", () => {
  const P: Record<string, (v: unknown) => unknown> = {
    syllabus: parseSyllabus,
    "syllabus-item": parseItem,
    "syllabus-page": parseSyllabusPage,
    "syllabus-item-page": parseItemPage,
    "syllabus-decision-batch-result": parseBatchResult,
    "syllabus-unclaimed-date": parseUnclaimed,
    "syllabus-unclaimed-date-page": parseUnclaimedPage,
  };
  const pv = valid.filter((f) => P[f.schema]);
  const pi = invalid.filter((f) => P[f.schema]);

  it.each(pv.map((f) => [f.file, f] as const))("accepts valid %s", (_n, f) => {
    expect(P[f.schema](f.json)).not.toBeNull();
  });

  // The parser checks what the UI reads. It deliberately accepts: extra top-level members (dropped, see below), TextPolicy-only
  // violations (server-authoritative; render strips bidi and controls), and a field the UI never reads (contentSha256).
  // Inside snippet and context, unknown members are refused (SBV-08 b), so syllabus-item--context-extra-field.json is
  // rejected.
  const ACCEPTED_BY_DESIGN = [
    "syllabus--extra-field.json",
    "syllabus--redteam-trailing-lf-hash.json",
    "syllabus-item--bidi-confidence-reason.json",
    "syllabus-item--bidi-in-snippet.json",
    "syllabus-item--extra-field.json",
    "syllabus-item--numeric-score-extra.json",
    "syllabus-item--zero-width-title.json",
  ].sort();

  it("rejects every other invalid fixture", () => {
    expect(pi.length).toBeGreaterThan(80);
    expect(pi.filter((f) => P[f.schema](f.json) !== null).map((f) => f.file).sort()).toEqual(ACCEPTED_BY_DESIGN);
  });

  it("drops unknown members (they never reach a component)", () => {
    const item = parseItem(invalid.find((f) => f.file === "syllabus-item--numeric-score-extra.json")!.json) as unknown as Record<string, unknown>;
    const known = valid.find((f) => f.file === "syllabus-item--exam-looks-right.json")!.json as Record<string, unknown>;
    for (const k of Object.keys(item)) expect(k in known).toBe(true);
    const s = parseSyllabus(invalid.find((f) => f.file === "syllabus--extra-field.json")!.json) as unknown as Record<string, unknown>;
    expect(Object.keys(s).every((k) => k in (valid.find((f) => f.file === "syllabus--ready.json")!.json as object))).toBe(true);
  });

  it("returns null for non-objects and a page with one bad row", () => {
    expect(parseSyllabus("x")).toBeNull();
    expect(parseItem(null)).toBeNull();
    const good = valid.find((f) => f.file === "syllabus-item--exam-looks-right.json")!.json;
    expect(parseItemPage({ items: [good, { id: 1 }], nextCursor: null })).toBeNull();
    expect(parseItemPage({ items: [good], nextCursor: null })?.items).toHaveLength(1);
  });
});
