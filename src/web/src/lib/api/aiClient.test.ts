import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { server } from "@/mocks/server";
import { createRealAi } from "@/components/lecture/realRuntime";
import { ApiError } from "./errors";
import { createApiClient } from "./client";

const base = "http://localhost/api/v1";
const api = createApiClient({ baseUrl: base });
const signIn = () => api.devLogin("alice@dev.local");

describe("AI routes on the API client", () => {
  it("GET summary returns the body; 404 is an ApiError", async () => {
    await signIn();
    expect(await api.getSummary("ses_1")).toMatchObject({ revision: 2, source: "mock" });
    await expect(api.getSummary("ses_missing")).rejects.toMatchObject({ status: 404 });
  });

  it("POST lost-track sends the Idempotency-Key header and a closed body", async () => {
    await signIn();
    let seen: { key: string | null; body: unknown } | null = null;
    server.use(
      http.post(`${base}/sessions/:id/lost-track`, async ({ request }) => {
        seen = { key: request.headers.get("Idempotency-Key"), body: await request.json() };
        return HttpResponse.json({ catchUpId: "cu_1", windowMinutes: 2, text: "t", citations: [], kind: "Extractive" });
      }),
    );
    await api.postLostTrack("ses_1", 2, "key-123");
    expect(seen).toEqual({ key: "key-123", body: { windowMinutes: 2 } });
  });

  it.each([
    ["ses_ended", 409],
    ["ses_limited", 429],
    ["ses_missing", 404],
  ])("%s maps to status %i", async (id, status) => {
    await signIn();
    const err = await api.postLostTrack(id, 5, "k").catch((e) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).status).toBe(status);
  });
});

describe("createRealAi", () => {
  it("parses bodies; reads return null on 404", async () => {
    await signIn();
    const ai = createRealAi(api, "ses_1");
    expect((await ai.getSummary())?.revision).toBe(2);
    expect((await ai.startLostTrack(5, "k")).aiState).toBe("Pending");
    expect((await ai.getCatchUp("cu_x"))?.kind).toBe("Ai");
    expect(await ai.getCatchUp("cu_missing")).toBeNull();
    expect(await createRealAi(api, "ses_nosummary").getSummary()).toBeNull();
  });

  it("a malformed body rejects instead of rendering half a payload", async () => {
    await signIn();
    server.use(http.post(`${base}/sessions/:id/lost-track`, () => HttpResponse.json({ nope: true })));
    await expect(createRealAi(api, "ses_1").startLostTrack(5, "k")).rejects.toMatchObject({ code: "bad_response" });
  });
});
