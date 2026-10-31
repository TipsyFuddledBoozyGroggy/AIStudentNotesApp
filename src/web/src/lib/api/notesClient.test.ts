import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { createRealAi } from "@/components/lecture/realRuntime";
import { parseNotes } from "@/lib/lecture";
import { loadValidatorsFromDisk } from "@/lib/lecture/schemas.node";
import { server } from "@/mocks/server";
import { ApiError } from "./errors";
import { createApiClient } from "./client";

// Slice 8a: the notes routes on the API client and the real runtime, against the MSW stand-in (mocks/notesHandlers.ts),
// whose bodies are themselves checked against the real schema here so the mock cannot drift from the contract.

const base = "http://localhost/api/v1";
const api = createApiClient({ baseUrl: base });
const signIn = () => api.devLogin("alice@dev.local");
const v = loadValidatorsFromDisk(undefined, { unicodeRegExp: true });
const schemaValid = (b: unknown) => v.byName("notes")(b) === true;

async function raw(path: string, init: RequestInit) {
  return fetch(`${base}${path}`, { credentials: "include", ...init });
}

describe("notes routes on the API client", () => {
  it("POST sends exactly {noticeVersion} as JSON with the Idempotency-Key header", async () => {
    await signIn();
    let seen: { key: string | null; type: string | null; body: unknown } | null = null;
    server.use(
      http.post(`${base}/sessions/:id/notes`, async ({ request }) => {
        seen = { key: request.headers.get("Idempotency-Key"), type: request.headers.get("Content-Type"), body: await request.json() };
        return HttpResponse.json({}, { status: 202 });
      }),
    );
    await api.postNotes("ses_1", "notes-notice-draft-1", "key-123");
    expect(seen).toEqual({ key: "key-123", type: "application/json", body: { noticeVersion: "notes-notice-draft-1" } });
  });

  it("202 + Location for a new attempt, then GET polls it to Ready; every body is schema-valid", async () => {
    await signIn();
    const res = await raw("/sessions/ses_n1/notes", { method: "POST", headers: { "Content-Type": "application/json", "Idempotency-Key": "k1" }, body: JSON.stringify({ noticeVersion: "notes-notice-draft-1" }) });
    expect(res.status).toBe(202);
    expect(res.headers.get("Location")).toBe("/api/v1/sessions/ses_n1/notes");
    const pending = await res.json();
    expect(schemaValid(pending)).toBe(true);
    expect(pending.status).toBe("Pending");
    const ready = await api.getNotes("ses_n1");
    expect(schemaValid(ready)).toBe(true);
    expect(ready).toMatchObject({ status: "Ready", source: "mock", finalSummaryStatus: "Ready" });
    // The Final revision the press made is now the latest summary.
    expect(await api.getSummary("ses_n1")).toMatchObject({ status: "Final", revision: 3 });
  });

  it("the same key replays the same press with 200 + Idempotency-Replayed; a new key while it runs or after Ready starts nothing", async () => {
    await signIn();
    const first = (await api.postNotes("ses_notes_pending", "notes-notice-draft-1", "k1")) as { notesId: string };
    const replay = await raw("/sessions/ses_notes_pending/notes", { method: "POST", headers: { "Content-Type": "application/json", "Idempotency-Key": "k1" }, body: JSON.stringify({ noticeVersion: "notes-notice-draft-1" }) });
    expect(replay.status).toBe(200);
    expect(replay.headers.get("Idempotency-Replayed")).toBe("true");
    expect(((await replay.json()) as { notesId: string }).notesId).toBe(first.notesId);
    const other = await raw("/sessions/ses_notes_pending/notes", { method: "POST", headers: { "Content-Type": "application/json", "Idempotency-Key": "k2" }, body: JSON.stringify({ noticeVersion: "notes-notice-draft-1" }) });
    expect(other.status).toBe(200);
    expect(other.headers.get("Idempotency-Replayed")).toBeNull();
    expect(((await other.json()) as { notesId: string; revision: number }).revision).toBe(1);
  });

  it("a new attempt only after Unavailable or BudgetLimited (revision + 1)", async () => {
    await signIn();
    expect(await api.postNotes("ses_notes_budget", "notes-notice-draft-1", "a")).toMatchObject({ status: "BudgetLimited", revision: 1 });
    expect(await api.postNotes("ses_notes_budget", "notes-notice-draft-1", "b")).toMatchObject({ status: "Pending", revision: 2 });
    expect(await api.postNotes("ses_notes_unavailable", "notes-notice-draft-1", "c")).toMatchObject({ status: "Pending", revision: 1 });
    expect(await api.getNotes("ses_notes_unavailable")).toMatchObject({ status: "Unavailable" });
    expect(await api.postNotes("ses_notes_unavailable", "notes-notice-draft-1", "d")).toMatchObject({ status: "Pending", revision: 2 });
    expect(await api.postNotes("ses_notes_notneeded", "notes-notice-draft-1", "e")).toMatchObject({ status: "NotNeeded" });
  });

  it.each([
    ["an empty body", "{}", "application/json", "k", 400],
    ["an extra member", JSON.stringify({ noticeVersion: "notes-notice-draft-1", windowMinutes: 5 }), "application/json", "k", 400],
    ["an unknown notice id", JSON.stringify({ noticeVersion: "notes-notice-final" }), "application/json", "k", 400],
    ["no Idempotency-Key", JSON.stringify({ noticeVersion: "notes-notice-draft-1" }), "application/json", null, 400],
    ["a bad Idempotency-Key", JSON.stringify({ noticeVersion: "notes-notice-draft-1" }), "application/json", "bad key!", 400],
    ["text/plain", JSON.stringify({ noticeVersion: "notes-notice-draft-1" }), "text/plain", "k", 415],
  ])("the mock refuses %s like the server", async (_n, body, type, key, status) => {
    await signIn();
    const headers: Record<string, string> = { "Content-Type": type };
    if (key) headers["Idempotency-Key"] = key;
    expect((await raw("/sessions/ses_n2/notes", { method: "POST", headers, body })).status).toBe(status);
  });

  it.each([
    ["ses_notes_409", 409, "invalid_state"],
    ["ses_notes_429", 429, "rate_limited"],
    ["ses_missing", 404, "not_found"],
  ])("POST %s maps to %i %s", async (id, status, code) => {
    await signIn();
    const err = await api.postNotes(id, "notes-notice-draft-1", "k").catch((e) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(err).toMatchObject({ status, code });
  });
});

describe("createRealAi notes", () => {
  it("getNotes: null before any press (409), a parsed body after, 404 rejects", async () => {
    await signIn();
    const ai = createRealAi(api, "ses_n3");
    expect(await ai.getNotes!()).toBeNull();
    const pressed = await ai.requestNotes!("key-1");
    expect(pressed.status).toBe("Pending");
    expect((await ai.getNotes!())?.status).toBe("Ready");
    await expect(createRealAi(api, "ses_missing").getNotes!()).rejects.toMatchObject({ status: 404 });
  });

  it("requestNotes sends the DRAFT notice id from copy.ts", async () => {
    await signIn();
    let body: unknown = null;
    server.use(
      http.post(`${base}/sessions/:id/notes`, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ nope: true }, { status: 202 });
      }),
    );
    await createRealAi(api, "ses_1").requestNotes!("k").catch(() => undefined);
    expect(body).toEqual({ noticeVersion: "notes-notice-draft-1" });
  });

  it("a malformed body rejects (calm error) instead of rendering half a sheet", async () => {
    await signIn();
    server.use(http.get(`${base}/sessions/:id/notes`, () => HttpResponse.json({ status: "Ready", sections: [{ heading: "<img>" }] })));
    await expect(createRealAi(api, "ses_1").getNotes!()).rejects.toMatchObject({ code: "bad_response" });
    server.use(http.post(`${base}/sessions/:id/notes`, () => HttpResponse.json({ nope: true }, { status: 202 })));
    await expect(createRealAi(api, "ses_1").requestNotes!("k")).rejects.toMatchObject({ code: "bad_response" });
  });

  it("a Final summary revision read over GET /summary parses (status Final)", async () => {
    await signIn();
    const ai = createRealAi(api, "ses_n4");
    await ai.requestNotes!("k");
    await ai.getNotes!();
    expect(await ai.getSummary()).toMatchObject({ status: "Final", revision: 3, source: "mock" });
    expect(parseNotes(await api.getNotes("ses_n4"))?.finalSummaryRevision).toBe(3);
  });
});
