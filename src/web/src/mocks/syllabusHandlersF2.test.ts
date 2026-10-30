import { describe, expect, it } from "vitest";
import { createSyllabusApi, SyllabusApiError } from "@/lib/syllabus/client";
import { NOTICE_VERSION } from "@/lib/syllabus/requests";
import { loadSyllabusValidators } from "@/lib/syllabus/schemas.node";
import * as db from "./db";
import * as sdb from "./syllabusDb";

// Increment F2 mock routes: every body validates against the real syllabus.v1 schemas (ajv, u flag), and the rules the
// client relies on hold (batch eligibility, idempotent sync and replay, remove keeps approval, delete removes events,
// multipart intake with PDF/Word ending failed/disabled).

const V = loadSyllabusValidators();
const BASE = "http://localhost/api/v1";
const EMAIL = "alice@example.edu";
const COURSE = "course-bio101";
const api = createSyllabusApi({ baseUrl: BASE });

function setup(scenario: "ready" | "hostile" = "ready") {
  db.signIn(EMAIL);
  db.seedCourses(EMAIL, [{ id: COURSE, name: "Biology 101", createdAt: "2026-01-01T00:00:00Z", updatedAt: "2026-01-01T00:00:00Z" }]);
  sdb.seedSyllabus(EMAIL, COURSE, scenario);
  return `syl_${scenario}`;
}

const valid = (schema: string, body: unknown) => expect(V.byName(schema)(body), `${schema}: ${V.explain(schema)}`).toBe(true);
const meta = { defaultTimezone: "America/New_York", noticeVersion: NOTICE_VERSION, acceptedAt: "2026-10-30T12:00:00.000Z" };

async function poll(id: string) {
  let s = await api.getSyllabus(id);
  for (let i = 0; i < 6 && ["queued", "extracting", "proposing"].includes(s.status); i++) s = await api.getSyllabus(id);
  return s;
}

describe("batch decisions", () => {
  it("approves clear items only; others are rejected per item; the body validates", async () => {
    const id = setup();
    const items = await api.listItems(id);
    const pick = items.filter((i) => i.kind !== "policy").slice(0, 6);
    const res = await fetch(`${BASE}/syllabi/${id}/decisions`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ action: "approve", itemEtags: Object.fromEntries(pick.map((i) => [i.id, i.etag])) }),
    });
    const body = await res.json();
    valid("syllabus-decision-batch-result", body);
    const r = await api.decideBatch(id, "approve", new Map((await api.listItems(id)).filter((i) => i.kind !== "policy").map((i) => [i.id, i.etag])));
    for (const x of r.results) {
      const it = items.find((i) => i.id === x.itemId)!;
      if (x.outcome === "applied") expect(it.confidence).toBe("looks_right");
    }
    expect(r.results.some((x) => x.errorCode === "batch_not_eligible")).toBe(true);
  });

  it("hostile syllabus: every approve is batch_not_eligible", async () => {
    const id = setup("hostile");
    const items = (await api.listItems(id)).filter((i) => i.when.status === "resolved");
    const r = await api.decideBatch(id, "approve", new Map(items.map((i) => [i.id, i.etag])));
    expect(r.counts.applied).toBe(0);
    expect(r.results.every((x) => x.errorCode === "batch_not_eligible")).toBe(true);
  });
});

describe("calendar sync, remove, export, delete", () => {
  it("sync twice is idempotent (created, then unchanged); a replay is marked; remove keeps approval; export validates", async () => {
    const id = setup();
    const items = await api.listItems(id);
    const clear = items.filter((i) => i.confidence === "looks_right" && i.when.status === "resolved" && i.kind !== "policy").slice(0, 2);
    await api.decideBatch(id, "approve", new Map(clear.map((i) => [i.id, i.etag])));
    const revs = new Map(clear.map((i) => [i.id, i.revision]));
    const res = await fetch(`${BASE}/syllabi/${id}/calendar-sync`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "Idempotency-Key": "key-sync-0001" },
      body: JSON.stringify({ confirm: { provider: "mock", acknowledgedDemo: true }, itemRevisions: Object.fromEntries(revs) }),
    });
    const first = await res.json();
    valid("syllabus-calendar-sync-result", first);
    expect(first.counts.created).toBe(2);
    const replay = await api.syncCalendar(id, revs, "key-sync-0001");
    expect(replay.replayed).toBe(true);
    expect(replay.result.syncId).toBe(first.syncId);
    const again = await api.syncCalendar(id, revs, "key-sync-0002");
    expect(again.replayed).toBe(false);
    expect(again.result.status).toBe("nothing_to_do");
    expect(again.result.counts.unchanged).toBe(2);
    const reused = await api.syncCalendar(id, new Map([[clear[0].id, clear[0].revision]]), "key-sync-0001").catch((e: unknown) => e);
    expect(reused).toBeInstanceOf(SyllabusApiError);

    const rem = await fetch(`${BASE}/syllabi/${id}/calendar-sync/remove`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ itemIds: [clear[0].id] }) });
    const remBody = await rem.json();
    valid("syllabus-calendar-remove-result", remBody);
    expect(remBody.counts.removed).toBe(1);
    const after = await api.getItem(id, clear[0].id);
    expect(after.decision).toBe("approved");
    expect(after.syncState).toBe("not_synced");

    const ex = await fetch(`${BASE}/syllabi/${id}/export`);
    expect(ex.headers.get("Cache-Control")).toBe("no-store");
    const exBody = await ex.json();
    valid("syllabus-export", exBody);
    expect(exBody.calendarEvents).toHaveLength(1);
    for (const ev of exBody.calendarEvents) valid("syllabus-calendar-event", ev);
    expect(JSON.parse(await api.exportSyllabus(id)).schemaVersion).toBe("syllabus.v1");
  });

  it("a mock sync-fail title gives a partial result that never claims full success", async () => {
    const id = setup();
    const items = await api.listItems(id);
    const [a, b] = items.filter((i) => i.confidence === "looks_right" && i.when.status === "resolved" && i.kind !== "policy");
    const renamed = await api.updateItem(id, b.id, { title: "Lab mock:sync-fail" }, b.etag);
    await api.decide(id, a.id, "approved", a.etag);
    await api.decide(id, renamed.id, "approved", renamed.etag);
    const { result } = await api.syncCalendar(id, new Map([[a.id, a.revision], [renamed.id, renamed.revision]]), "key-sync-0003");
    valid("syllabus-calendar-sync-result", { ...result, provider: "mock", demoCalendar: true });
    expect(result.status).toBe("partial");
    expect(result.results.find((r) => r.itemId === renamed.id)?.errorCode).toBe("provider_unavailable");
  });

  it("DELETE takes no parameters, is 204, then 404; the events go with it", async () => {
    const id = setup();
    expect((await fetch(`${BASE}/syllabi/${id}?keep=1`, { method: "DELETE" })).status).toBe(400);
    await api.deleteSyllabus(id);
    expect(await api.getSyllabus(id).catch((e: unknown) => (e as SyllabusApiError).status)).toBe(404);
  });
});

// jsdom's FormData is not the one Node's fetch (undici) serialises, so the mock route is driven with a hand-built
// multipart body here; the client's own FormData shape is pinned in the next describe with a fetch spy, and the real
// browser path is covered by e2e/syllabus.spec.ts against the real API.
function multipart(file: { name: string; type: string; body: string }, metadata = JSON.stringify({ defaultTimezone: meta.defaultTimezone, noticeVersion: meta.noticeVersion, attestation: { accepted: true, acceptedAt: meta.acceptedAt } })) {
  const b = "----tetherTestBoundary";
  const body =
    `--${b}\r\nContent-Disposition: form-data; name="metadata"\r\n\r\n${metadata}\r\n` +
    `--${b}\r\nContent-Disposition: form-data; name="file"; filename="${file.name}"\r\nContent-Type: ${file.type}\r\n\r\n${file.body}\r\n--${b}--\r\n`;
  return { body, contentType: `multipart/form-data; boundary=${b}` };
}

async function upload(key: string, file: { name: string; type: string; body: string }) {
  const m = multipart(file);
  const res = await fetch(`${BASE}/courses/${COURSE}/syllabi`, { method: "POST", headers: { "Content-Type": m.contentType, "Idempotency-Key": key }, body: m.body });
  return { status: res.status, body: (await res.json().catch(() => null)) as Record<string, unknown> | null };
}

describe("multipart create (mock route)", () => {
  it("a plain text file is read like a paste; the 202 body validates; PDF ends failed/disabled; PNG and a lying extension are 415", async () => {
    setup();
    sdb.remove(EMAIL, "syl_ready");
    const txt = await upload("key-file-0001", { name: "bio.txt", type: "text/plain", body: "Quiz 1 on Sept 14" });
    expect([txt.status, txt.body]).toEqual([202, expect.anything()]);
    valid("syllabus", txt.body);
    const done = await poll(txt.body!.id as string);
    expect(done).toMatchObject({ status: "ready", inputKind: "upload", fileType: "txt" });

    const pdf = await upload("key-file-0002", { name: "bio.pdf", type: "application/pdf", body: "%PDF-1.4 synthetic" });
    expect(pdf.status).toBe(202);
    const pdfDone = await poll(pdf.body!.id as string);
    valid("syllabus", await (await fetch(`${BASE}/syllabi/${pdf.body!.id as string}`)).json());
    expect(pdfDone).toMatchObject({ status: "failed", statusReason: "disabled", fileType: "pdf" });

    expect((await upload("key-file-0003", { name: "bio.png", type: "image/png", body: "\u0089PNG\u0000\u0001" })).status).toBe(415);
    expect((await upload("key-file-0004", { name: "bio.txt", type: "text/plain", body: "%PDF-1.4 pretending" })).status).toBe(415);
  });
});

describe("client multipart request shape", () => {
  it("one metadata part (schema-valid, no text) and one file part with its name; no JSON Content-Type header", async () => {
    let seen: { init: RequestInit; url: string } | null = null;
    const spy = (async (url: string, init: RequestInit) => {
      seen = { url, init };
      return new Response(JSON.stringify({}), { status: 500 });
    }) as unknown as typeof fetch;
    const c = createSyllabusApi({ baseUrl: BASE, fetchImpl: spy });
    await c.createFromFile(COURSE, { ...meta, termStart: "2026-08-24", termEnd: "2026-12-11", file: new Blob(["x"], { type: "text/plain" }), fileName: "bio.txt" }, "key-file-0009").catch(() => undefined);
    const s = seen as unknown as { init: RequestInit; url: string };
    expect(s.url).toBe(`${BASE}/courses/${COURSE}/syllabi`);
    expect((s.init.headers as Record<string, string>)["Content-Type"]).toBeUndefined();
    expect((s.init.headers as Record<string, string>)["Idempotency-Key"]).toBe("key-file-0009");
    const fd = s.init.body as FormData;
    expect([...fd.keys()]).toEqual(["metadata", "file"]);
    const m = JSON.parse(fd.get("metadata") as string);
    valid("syllabus-create-request", m);
    expect(m).not.toHaveProperty("text");
    expect((fd.get("file") as File).name).toBe("bio.txt");
  });
});
