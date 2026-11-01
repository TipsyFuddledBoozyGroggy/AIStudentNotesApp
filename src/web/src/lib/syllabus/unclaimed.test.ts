import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { server } from "@/mocks/server";
import { buildUnclaimed } from "@/mocks/syllabusData";
import { INVALID_PAGES, ROW_LINE, VALID_PAGES } from "@/mocks/unclaimedFixtures";
import { createSyllabusApi, SyllabusApiError, SyllabusParseError } from "./client";
import { parseUnclaimed, parseUnclaimedPage } from "./parse";
import { loadSyllabusValidators } from "./schemas.node";

// PO-20b (docs/ux/unclaimed-dates-list.md 9, 11): the unclaimed-date page parser, the list and create client methods.

const V = loadSyllabusValidators();
const BASE = "http://localhost/api/v1";
const api = createSyllabusApi({ baseUrl: BASE });

describe("parseUnclaimedPage over the web fixtures", () => {
  it.each(Object.entries(VALID_PAGES))("valid %s: schema-valid and accepted", (_n, body) => {
    expect(V.byName("syllabus-unclaimed-date-page")(body), V.explain("syllabus-unclaimed-date-page")).toBe(true);
    const p = parseUnclaimedPage(body);
    expect(p).not.toBeNull();
    expect(p!.items).toHaveLength((body.items as unknown[]).length);
  });

  it.each(Object.entries(INVALID_PAGES))("invalid %s: refused by the parser", (_n, body) => {
    expect(parseUnclaimedPage(body)).toBeNull();
  });

  it("the schema refuses the invalid fixtures too (the parser is not looser than the contract)", () => {
    for (const [name, body] of Object.entries(INVALID_PAGES)) expect([name, V.byName("syllabus-unclaimed-date-page")(body)]).toEqual([name, false]);
  });

  it("copies only id and snippet; a row is never null-snippeted", () => {
    expect(Object.keys(parseUnclaimed(ROW_LINE)!).sort()).toEqual(["id", "snippet"]);
    expect(parseUnclaimed({ id: "unc_1" })).toBeNull();
    expect(parseUnclaimed(null)).toBeNull();
  });
});

describe("api.listUnclaimedDates", () => {
  it("asks for limit=200, follows one nextCursor (two requests), and returns the rows in order", async () => {
    const urls: string[] = [];
    const rows = buildUnclaimed("syl_a", 5);
    server.use(
      http.get(`${BASE}/syllabi/:id/unclaimed-dates`, ({ request }) => {
        const u = new URL(request.url);
        urls.push(u.search);
        return HttpResponse.json(u.searchParams.get("cursor") ? { items: rows.slice(3), nextCursor: null } : { items: rows.slice(0, 3), nextCursor: "next1" });
      }),
    );
    const out = await api.listUnclaimedDates("syl_a");
    expect(out.map((r) => r.id)).toEqual(rows.map((r) => r.id));
    expect(urls).toEqual(["?limit=200", "?limit=200&cursor=next1"]);
  });

  it("stops after two pages and refuses more than 200 rows in total rather than showing them cut short", async () => {
    let calls = 0;
    server.use(
      http.get(`${BASE}/syllabi/:id/unclaimed-dates`, () => {
        calls++;
        return HttpResponse.json({ items: buildUnclaimed("syl_a", 150), nextCursor: "again" });
      }),
    );
    await expect(api.listUnclaimedDates("syl_a")).rejects.toBeInstanceOf(SyllabusParseError);
    expect(calls).toBe(2);
  });

  it("one bad row refuses the page; a malformed id is never put in a URL", async () => {
    server.use(http.get(`${BASE}/syllabi/:id/unclaimed-dates`, () => HttpResponse.json(INVALID_PAGES["extra member on a row"])));
    await expect(api.listUnclaimedDates("syl_a")).rejects.toBeInstanceOf(SyllabusParseError);
    await expect(api.listUnclaimedDates("../x")).rejects.toBeInstanceOf(SyllabusApiError);
  });
});

describe("api.createItem", () => {
  it("POSTs exactly the create body with the Idempotency-Key and parses the 201 as an item", async () => {
    let seen: { key: string | null; body: unknown; type: string | null } | null = null;
    server.use(
      http.post(`${BASE}/syllabi/:id/items`, async ({ request }) => {
        seen = { key: request.headers.get("Idempotency-Key"), body: await request.json(), type: request.headers.get("Content-Type") };
        return HttpResponse.json({ nope: true }, { status: 201 });
      }),
    );
    await expect(api.createItem("syl_a", { kind: "other", title: "T", date: "2026-10-05", allDay: true, time: null }, "key-12345678")).rejects.toBeInstanceOf(SyllabusParseError);
    expect(seen).toEqual({ key: "key-12345678", body: { kind: "other", title: "T", date: "2026-10-05", allDay: true, time: null }, type: "application/json" });
  });

  it("refuses a malformed key locally (no request)", async () => {
    let calls = 0;
    server.use(
      http.post(`${BASE}/syllabi/:id/items`, () => {
        calls++;
        return HttpResponse.json({}, { status: 201 });
      }),
    );
    await expect(api.createItem("syl_a", { kind: "other", title: "T", date: "2026-10-05", allDay: true, time: null }, "short")).rejects.toMatchObject({ status: 400 });
    expect(calls).toBe(0);
  });
});
