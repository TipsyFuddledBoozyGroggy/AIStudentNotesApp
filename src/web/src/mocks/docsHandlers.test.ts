import { beforeEach, describe, expect, it } from "vitest";
import { createDocsApi, DocsApiError, type DocsApi } from "@/lib/docs/client";
import { confirmBody, newIdempotencyKey } from "@/lib/docs/requests";
import { loadDocsValidators } from "@/lib/docs/schemas.node";
import * as db from "./db";
import { deleteDocsSession, docsMock, resetDocsMock, seedDocsSession, updateDocsSession } from "./docsDb";
import { DEMO_DOCS_FAULT_HEADER } from "./docsHandlers";

// DX-F05: the MSW stand-in for docs.export.v1 behaves like the contract (mirrors backend tests B13-B21 and B26-B30), and
// every body it returns validates against the real schemas, so component tests on top of it test the real shapes.

const SID = "0192f0a1-b2c3-4d5e-8f9a-0b1c2d3e4f01";
const NOTES = "nt_0192f0a1b2c34d5e8f9a0b1c2d3e4f52";
const ajv = loadDocsValidators();
const BASE = "http://localhost/api/v1";

/** A client whose confirm can carry the Development fault header (the real client never sends it). */
function apiWithFault(faultFor: () => string | null): DocsApi {
  return createDocsApi({
    baseUrl: BASE,
    fetchImpl: (input, init) => {
      const f = faultFor();
      const headers = new Headers(init?.headers);
      if (f && init?.method === "POST" && String(input).endsWith("/docs-export")) headers.set(DEMO_DOCS_FAULT_HEADER, f);
      return fetch(input, { ...init, headers });
    },
  });
}

let fault: string | null = null;
const api = apiWithFault(() => fault);

function seed(over: Partial<Parameters<typeof seedDocsSession>[0]> = {}) {
  seedDocsSession({
    sessionId: SID,
    owner: "ana@dev.local",
    notesId: NOTES,
    title: "Cell division",
    startedAt: "2026-11-05T15:00:00Z",
    sections: [{ heading: "Mitosis", bullets: [{ text: "Mitosis has four phases.", startMs: [2000] }, { text: "Prophase comes first.", startMs: [4000, 6000] }] }],
    ...over,
  });
}

const status = async (p: Promise<unknown>) => {
  try {
    await p;
    return 200;
  } catch (e) {
    return e instanceof DocsApiError ? `${e.status} ${e.code}` : "other";
  }
};

beforeEach(() => {
  resetDocsMock();
  fault = null;
  db.signIn("ana@dev.local");
  seed();
});

describe("DX-F05 docs mock handlers (contract 4.1 to 4.5)", () => {
  it("preview writes nothing and is schema-valid; first confirm creates one Demo Doc at version 1 (B13)", async () => {
    const p = await api.preview(SID, NOTES);
    expect(docsMock.docCount()).toBe(0);
    expect(p.actions).toEqual(["create"]);
    const { record } = await api.confirm(SID, confirmBody(p, "create"), newIdempotencyKey());
    expect(ajv.byName("docs-export-record")(record)).toBe(true);
    expect([record.status, record.outcome, record.docRef?.version]).toEqual(["succeeded", "created", 1]);
    expect(docsMock.docCount()).toBe(1);
    const doc = await api.getDemoDoc(record.docRef!.docId);
    expect(doc.document).toEqual(p.document);
    const state = await api.getState(SID);
    expect(ajv.byName("docs-state")(state)).toBe(true);
    expect(state.target?.docId).toBe(record.docRef!.docId);
  });

  it("same key and body replays the record with Idempotency-Replayed and writes nothing (B14); a different body is 400 (B15)", async () => {
    const p = await api.preview(SID, NOTES);
    const key = newIdempotencyKey();
    const first = await api.confirm(SID, confirmBody(p, "create"), key);
    const again = await api.confirm(SID, confirmBody(p, "create"), key);
    expect(again.replayed).toBe(true);
    expect(again.record).toEqual(first.record);
    expect(docsMock.docCount()).toBe(1);
    const other = { ...confirmBody(p, "create"), notesId: "nt_00000000000000000000000000000000" };
    expect(await status(api.confirm(SID, other, key))).toBe("400 validation_failed");
  });

  it("same notes later: the preview lists no action and update returns unchanged without a write (B16, 1c)", async () => {
    const p = await api.preview(SID, NOTES);
    await api.confirm(SID, confirmBody(p, "create"), newIdempotencyKey());
    const p2 = await api.preview(SID, NOTES);
    expect(p2.existing?.upToDate).toBe(true);
    expect(p2.actions).toEqual([]);
    const { record } = await api.confirm(SID, confirmBody(p2, "update"), newIdempotencyKey());
    expect([record.outcome, record.docRef?.version]).toEqual(["unchanged", 1]);
    expect(await status(api.confirm(SID, confirmBody(p2, "create_another"), newIdempotencyKey()))).toBe("409 invalid_state");
  });

  it("a changed title: update keeps the docId at version 2 (B17); create_another makes a second doc and moves the pointer (B18)", async () => {
    const p = await api.preview(SID, NOTES);
    const { record: c } = await api.confirm(SID, confirmBody(p, "create"), newIdempotencyKey());
    updateDocsSession(SID, { title: "Cell division, revised" });
    const p2 = await api.preview(SID, NOTES);
    expect(p2.actions).toEqual(["update", "create_another"]);
    const { record: u } = await api.confirm(SID, confirmBody(p2, "update"), newIdempotencyKey());
    expect([u.outcome, u.docRef?.docId, u.docRef?.version]).toEqual(["updated", c.docRef!.docId, 2]);
    updateDocsSession(SID, { title: "Cell division, third" });
    const p3 = await api.preview(SID, NOTES);
    const { record: a } = await api.confirm(SID, confirmBody(p3, "create_another"), newIdempotencyKey());
    expect(a.outcome).toBe("created");
    expect(a.docRef!.docId).not.toBe(c.docRef!.docId);
    expect(docsMock.docCount()).toBe(2);
    expect((await api.getDemoDoc(c.docRef!.docId)).version).toBe(2); // the first one is untouched
    expect((await api.getState(SID)).target?.docId).toBe(a.docRef!.docId);
  });

  it("a wrong digest or document is 412 and stores no key (B19, B20, B20a); a mode not listed is 409 (B21)", async () => {
    const p = await api.preview(SID, NOTES);
    const key = newIdempotencyKey();
    updateDocsSession(SID, { title: "Changed while looking" });
    expect(await status(api.confirm(SID, confirmBody(p, "create"), key))).toBe("412 precondition_failed");
    expect(docsMock.key("ana@dev.local", key)).toBeUndefined();
    const fresh = await api.preview(SID, NOTES);
    expect(await status(api.confirm(SID, confirmBody(fresh, "create"), key))).toBe(200); // the key stayed free
    updateDocsSession(SID, { title: "Again" });
    const p3 = await api.preview(SID, NOTES);
    const forged = { ...confirmBody(p3, "update"), expectedExistingDocId: "ddc_00000000-0000-4000-8000-000000000999" };
    expect(await status(api.confirm(SID, forged, newIdempotencyKey()))).toBe("412 precondition_failed");
    const stale = { ...confirmBody(p3, "update"), expectedExistingVersion: 7 };
    expect(await status(api.confirm(SID, stale, newIdempotencyKey()))).toBe("412 precondition_failed");
    expect(await status(api.confirm(SID, confirmBody(fresh, "create"), newIdempotencyKey()))).toBe("412 precondition_failed");
  });

  it("not Stopped or not Ready is 409; another notes id or another user's session is 404 (B11, B36)", async () => {
    updateDocsSession(SID, { state: "Active" });
    expect(await status(api.preview(SID, NOTES))).toBe("409 invalid_state");
    updateDocsSession(SID, { state: "Stopped", notesStatus: "Pending" });
    expect(await status(api.preview(SID, NOTES))).toBe("409 invalid_state");
    updateDocsSession(SID, { notesStatus: "Ready" });
    expect(await status(api.preview(SID, "nt_00000000000000000000000000000000"))).toBe("404 not_found");
    db.signIn("ben@dev.local");
    expect(await status(api.preview(SID, NOTES))).toBe("404 not_found");
    expect(await status(api.getState(SID))).toBe("404 not_found");
  });

  it("provider_unavailable is a 200 failed record with retry advice and writes nothing; the same key then succeeds once (B26, B27)", async () => {
    const p = await api.preview(SID, NOTES);
    const key = newIdempotencyKey();
    fault = "unavailable";
    const { record: f } = await api.confirm(SID, confirmBody(p, "create"), key);
    expect(ajv.byName("docs-export-record")(f)).toBe(true);
    expect([f.status, f.error?.code, f.error?.retryable, f.error?.retryAfterSeconds, f.retry.allowed, f.docRef]).toEqual(["failed", "provider_unavailable", true, 5, true, null]);
    expect(docsMock.docCount()).toBe(0);
    fault = null;
    const { record: ok } = await api.confirm(SID, confirmBody(p, "create"), key);
    expect([ok.outcome, ok.attempt, ok.exportId]).toEqual(["created", 2, f.exportId]);
    expect(docsMock.docCount()).toBe(1);
  });

  it("after the fifth failed attempt retry.allowed is false and a sixth send is 409 retry_limit_reached (B28)", async () => {
    const p = await api.preview(SID, NOTES);
    const key = newIdempotencyKey();
    fault = "unavailable";
    let last = (await api.confirm(SID, confirmBody(p, "create"), key)).record;
    for (let i = 2; i <= 5; i++) last = (await api.confirm(SID, confirmBody(p, "create"), key)).record;
    expect([last.attempt, last.retry.allowed, last.retry.attemptsLeft]).toEqual([5, false, 0]);
    expect(await status(api.confirm(SID, confirmBody(p, "create"), key))).toBe("409 retry_limit_reached");
  });

  it("rate_limited carries the provider seconds capped at 3600 (B29); provider_rejected is not retryable (B30)", async () => {
    const p = await api.preview(SID, NOTES);
    fault = "rate_limited:9999";
    const r = (await api.confirm(SID, confirmBody(p, "create"), newIdempotencyKey())).record;
    expect([r.error?.code, r.error?.retryAfterSeconds, r.retry.allowed]).toEqual(["rate_limited", 3600, true]);
    fault = "rejected";
    const x = (await api.confirm(SID, confirmBody(p, "create"), newIdempotencyKey())).record;
    expect([x.error?.code, x.error?.retryable, x.error?.retryAfterSeconds, x.retry.allowed]).toEqual(["provider_rejected", false, null, false]);
  });

  it("delete clears the target and the next export is create (B35); deleting the lecture removes everything (B04)", async () => {
    const p = await api.preview(SID, NOTES);
    const { record } = await api.confirm(SID, confirmBody(p, "create"), newIdempotencyKey());
    await api.deleteDemoDoc(record.docRef!.docId);
    expect(await status(api.getDemoDoc(record.docRef!.docId))).toBe("404 not_found");
    expect((await api.preview(SID, NOTES)).actions).toEqual(["create"]);
    const p2 = await api.preview(SID, NOTES);
    const { record: r2 } = await api.confirm(SID, confirmBody(p2, "create"), newIdempotencyKey());
    deleteDocsSession(SID);
    expect(await status(api.getDemoDoc(r2.docRef!.docId))).toBe("404 not_found");
    expect(docsMock.exportCount()).toBe(0);
  });

  it("truncated notes carry notesTruncated and a closing 'where the notes stop' paragraph (DXD-13)", async () => {
    seed({ truncated: true, coveredToMs: 570000 });
    const p = await api.preview(SID, NOTES);
    expect(ajv.byName("docs-preview")(p)).toBe(true);
    expect(p.notesTruncated).toBe(true);
    expect(p.document.blocks.at(-1)).toEqual({ kind: "paragraph", text: "These notes stop at 09:30. Later parts of the lecture are not included." });
  });

  it("over 400 blocks: canWrite false, too_large, the first 400 shown, and a confirm is 409", async () => {
    seed({ sections: [{ heading: "Long", bullets: Array.from({ length: 401 }, (_, i) => ({ text: `Point ${i + 1}.`, startMs: [i * 1000] })) }] });
    const p = await api.preview(SID, NOTES);
    expect(ajv.byName("docs-preview")(p)).toBe(true);
    expect([p.canWrite, p.blockedReason, p.actions, p.document.blocks.length]).toEqual([false, "too_large", [], 400]);
    expect(await status(api.confirm(SID, confirmBody({ ...p, actions: ["create"] }, "create"), newIdempotencyKey()))).toBe("409 invalid_state");
  });
});
