import { describe, expect, it } from "vitest";
import { loadFixtures } from "@/lib/lecture/schemas.node";
import { batchAllowed, batchEligible, clearItems, inDemoCalendar, reviewOrder, syncCandidates } from "./eligibility";
import { isExportOf, parseItem, parseRemoveResult, parseSyllabus, parseSyncResult } from "./parse";
import { calendarRemoveBody, createFromTextBody, createMetadataBody, NOTICE_VERSION } from "./requests";
import { loadSyllabusValidators } from "./schemas.node";
import type { Syllabus, SyllabusItem } from "./types";

// Increment F2: request builders, result parsers and the client-side eligibility mirrors. ajv runs with the u flag
// (code-point engine, R3-07) over the real syllabus.v1 schemas and the shared fixtures.

const cp = loadSyllabusValidators({ unicodeRegExp: true });
const valid = loadFixtures("valid");
const invalid = loadFixtures("invalid");
const fx = (file: string) => valid.find((f) => f.file === file)!.json;

describe("F2 request builders are schema-valid", () => {
  const meta = { defaultTimezone: "America/New_York", noticeVersion: NOTICE_VERSION, acceptedAt: "2026-10-30T12:00:00.000Z" };

  it("the multipart metadata part has no text and passes syllabus-create-request", () => {
    const v = cp.byName("syllabus-create-request");
    const m = createMetadataBody(meta);
    expect(m).not.toHaveProperty("text");
    expect(v(m), cp.explain("syllabus-create-request")).toBe(true);
    expect(v(createMetadataBody({ ...meta, termStart: "2026-08-24", termEnd: "2026-12-11" }))).toBe(true);
    expect(new TextEncoder().encode(JSON.stringify(createMetadataBody({ ...meta, termStart: "2026-08-24", termEnd: "2026-12-11" }))).length).toBeLessThan(4096);
  });

  it("paste body sends both term dates when given", () => {
    const b = createFromTextBody({ ...meta, text: "Quiz 1 Sept 14", termStart: "2026-08-24", termEnd: "2026-12-11" });
    expect(b).toMatchObject({ termStart: "2026-08-24", termEnd: "2026-12-11" });
    expect(cp.byName("syllabus-create-request")(b)).toBe(true);
  });

  it("remove body: unique ids, 1 to 100", () => {
    const b = calendarRemoveBody(["itm_01", "itm_01", "itm_02"]);
    expect(b).toEqual({ itemIds: ["itm_01", "itm_02"] });
    expect(cp.byName("syllabus-calendar-remove-request")(b)).toBe(true);
    expect(() => calendarRemoveBody([])).toThrow();
  });
});

describe("F2 result parsers over the shared fixtures", () => {
  const P: Record<string, (v: unknown) => unknown> = {
    "syllabus-calendar-sync-result": parseSyncResult,
    "syllabus-calendar-remove-result": parseRemoveResult,
  };
  const pv = valid.filter((f) => P[f.schema]);
  const pi = invalid.filter((f) => P[f.schema]);

  it("has fixtures", () => {
    expect(pv.length).toBeGreaterThanOrEqual(4);
  });

  it.each(pv.map((f) => [f.file, f] as const))("accepts valid %s", (_n, f) => {
    expect(P[f.schema](f.json)).not.toBeNull();
  });

  it("rejects the invalid fixtures that break a field the UI reads", () => {
    const accepted = pi.filter((f) => P[f.schema](f.json) !== null).map((f) => f.file);
    // Any accepted one must only differ by members the parser drops (it never forwards unknown members).
    for (const file of accepted) expect(file).toMatch(/extra|unknown/);
  });

  it("refuses a non-mock provider or a missing demo flag", () => {
    const r = fx("syllabus-calendar-sync-result--partial.json") as Record<string, unknown>;
    expect(parseSyncResult({ ...r, provider: "google" })).toBeNull();
    expect(parseSyncResult({ ...r, demoCalendar: false })).toBeNull();
  });

  it("export check: syllabus.v1 export of the asked syllabus only", () => {
    const e = fx("syllabus-export--basic.json") as { syllabus: { id: string } };
    expect(cp.byName("syllabus-export")(e)).toBe(true);
    expect(isExportOf(e, e.syllabus.id)).toBe(true);
    expect(isExportOf(e, "syl_other")).toBe(false);
    expect(isExportOf({ ...e, schemaVersion: "syllabus.v2" }, e.syllabus.id)).toBe(false);
  });
});

describe("eligibility mirrors (UI offers only; the server decides)", () => {
  const ready = parseSyllabus(fx("syllabus--ready.json")) as Syllabus;
  const looks = parseItem(fx("syllabus-item--exam-looks-right.json")) as SyllabusItem;

  it("a clear item is resolved, looks right, unedited, undecided and has readable source wording", () => {
    expect(batchEligible(looks)).toBe(true);
    expect(batchEligible({ ...looks, confidence: "probably_right" })).toBe(false);
    expect(batchEligible({ ...looks, confidence: "please_check" })).toBe(false);
    expect(batchEligible({ ...looks, edited: true })).toBe(false);
    expect(batchEligible({ ...looks, decision: "approved" })).toBe(false);
    expect(batchEligible({ ...looks, when: { ...looks.when, status: "needs_date" } })).toBe(false);
    expect(batchEligible({ ...looks, snippet: looks.snippet && { ...looks.snippet, text: null } })).toBe(false);
  });

  it("never offered for a suspect, hidden-content or purged syllabus", () => {
    expect(batchAllowed(ready)).toBe(ready.batchApproveAllowed);
    expect(batchAllowed({ ...ready, document: { ...ready.document, injectionSuspect: true } })).toBe(false);
    expect(batchAllowed({ ...ready, document: { ...ready.document, hiddenContentDropped: true } })).toBe(false);
    expect(batchAllowed({ ...ready, retention: { canonicalTextPurged: true, canonicalTextPurgeAt: null } })).toBe(false);
    expect(clearItems({ ...ready, batchApproveAllowed: false }, [looks])).toEqual([]);
  });

  it("sync candidates are approved and dated; in-calendar needs written events", () => {
    const approved = { ...looks, decision: "approved" as const };
    expect(syncCandidates([looks, approved]).map((i) => i.decision)).toEqual(["approved"]);
    expect(inDemoCalendar({ ...approved, syncState: "synced", syncedRevision: 1 })).toBe(true);
    expect(inDemoCalendar({ ...approved, syncState: "sync_failed", syncedRevision: null })).toBe(false);
    expect(inDemoCalendar({ ...approved, syncState: "sync_failed", syncedRevision: 2 })).toBe(true);
  });

  it("one-at-a-time order: by date, Needs a date last, no policies", () => {
    const a = { ...looks, id: "itm_a", when: { ...looks.when, date: "2026-10-20" } };
    const b = { ...looks, id: "itm_b", when: { ...looks.when, date: "2026-09-01" } };
    const n = { ...looks, id: "itm_n", when: { ...looks.when, status: "needs_date" as const, date: null } };
    const p = { ...looks, id: "itm_p", kind: "policy" as const };
    expect(reviewOrder([n, a, p, b]).map((i) => i.id)).toEqual(["itm_b", "itm_a", "itm_n"]);
  });
});
