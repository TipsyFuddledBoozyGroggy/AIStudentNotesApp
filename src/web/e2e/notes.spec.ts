import { test, expect, type APIRequestContext, type Page } from "@playwright/test";
import { uid } from "./helpers";
import { stateFor } from "./users";

// Slice 8a (PO-18d option B, API_CONTRACTS section 17) on the REAL stack: API + Postgres, the backend's deterministic
// ReplaySttProvider for the transcript and its MOCK AI provider for the notes and the Final summary (no language model,
// no vendor, no real classroom audio; the fake Chromium microphone only paces the replay). It does not wait for the live
// 90 s TL;DR job: the Final summary comes from the press itself.
test.describe.configure({ mode: "serial" });
test.use({ storageState: stateFor("notes") });

const ORIGIN = "http://localhost:3000";
const DEMO_LINE_1 = "Demo replay segment one";
const KEY_RE = /^[A-Za-z0-9_-]{1,64}$/;
const started: string[] = [];

async function postWithRetry(ctx: APIRequestContext, path: string, data?: unknown) {
  for (let i = 0; i < 16; i++) {
    const res = await ctx.post(`${ORIGIN}${path}`, { headers: { Origin: ORIGIN }, data });
    if (res.status() !== 429) return res;
    await new Promise((r) => setTimeout(r, 5000));
  }
  throw new Error("still rate limited");
}

test.afterEach(async ({ request }) => {
  for (const id of started.splice(0)) await postWithRetry(request, `/api/v1/sessions/${id}/stop`).catch(() => undefined);
});

const course = { id: "", name: `Notes e2e ${uid()}` };
test.beforeAll(async ({ playwright }) => {
  const ctx = await playwright.request.newContext({ baseURL: ORIGIN, storageState: stateFor("notes"), extraHTTPHeaders: { Origin: ORIGIN } });
  const res = await postWithRetry(ctx, "/api/v1/courses", { name: course.name });
  expect(res.status()).toBe(201);
  course.id = ((await res.json()) as { id: string }).id;
  await ctx.dispose();
});

const transcript = (page: Page) => page.getByRole("log", { name: "Transcript lines" });
const dialog = (page: Page) => page.getByRole("dialog", { name: "Did your teacher say you can record?" });

async function startDemoLecture(page: Page): Promise<string> {
  await page.goto(`/courses/${course.id}/lecture`);
  await expect(page.getByRole("heading", { level: 1, name: `Start lecture: ${course.name}` })).toBeVisible();
  await page.getByRole("button", { name: "Start recording", exact: true }).click();
  await dialog(page).getByRole("checkbox", { name: "My teacher said I can record this class." }).check();
  const created = page.waitForResponse((r) => /\/sessions$/.test(r.url()) && r.request().method() === "POST");
  await dialog(page).getByRole("button", { name: "Confirm and start recording" }).click();
  const res = await created;
  expect(res.status()).toBe(201);
  const body = (await res.json()) as { id: string; demoSource?: boolean };
  expect(body.demoSource).toBe(true); // the replay source, not a real speech service
  started.push(body.id);
  return body.id;
}

test("Make notes after Stop: one deliberate POST, Pending then Ready, cited bullets, reload, replay, Final summary, private to the owner", async ({ page, browser }) => {
  test.setTimeout(180_000);
  const posts: { key: string | null; body: string | null; type: string | null }[] = [];
  let gets = 0;
  page.on("request", (r) => {
    if (!/\/sessions\/[^/]+\/notes$/.test(r.url())) return;
    if (r.method() === "POST") posts.push({ key: r.headers()["idempotency-key"] ?? null, body: r.postData(), type: r.headers()["content-type"] ?? null });
    if (r.method() === "GET") gets++;
  });

  const sid = await startDemoLecture(page);
  await expect(transcript(page)).toContainText(DEMO_LINE_1);
  await expect(page.locator("[data-segment-id]").first()).toBeVisible(); // at least one saved (final) line
  await expect(page.locator("#lecture-notes")).toHaveCount(0); // never during a lecture
  expect(gets + posts.length).toBe(0);

  // Stop: nothing is sent automatically (B3-06); the mount asks GET once and learns "not pressed yet" (409).
  const mountGet = page.waitForResponse((r) => r.url().endsWith(`/sessions/${sid}/notes`) && r.request().method() === "GET");
  await page.getByRole("button", { name: "Stop", exact: true }).first().click();
  await page.getByRole("dialog").getByRole("button", { name: "Stop", exact: true }).click();
  await expect(page.getByTestId("state-label")).toHaveText("Stopped");
  started.splice(started.indexOf(sid), 1);
  expect((await mountGet).status()).toBe(409);
  // PO-19 (stop-redirect.md criterion 18): the confirmed Stop opens the move notice; "Open the lecture's page" replaces the
  // live route with /lectures/{id}, whose fresh screen asks GET once more (409). No POST anywhere in the move. The press
  // below is made on the lecture's own page (was: on the live route).
  const getsBeforeMove = gets;
  const moveGet = page.waitForResponse((r) => r.url().endsWith(`/sessions/${sid}/notes`) && r.request().method() === "GET");
  await page.getByRole("dialog", { name: "This lecture has its own page" }).getByRole("button", { name: "Open the lecture's page" }).click();
  await expect(page).toHaveURL(new RegExp(`/lectures/${sid}$`));
  expect((await moveGet).status()).toBe(409);
  await expect(page.getByTestId("state-label")).toHaveText("Stopped");
  expect(gets).toBeGreaterThan(getsBeforeMove); // a fresh GET on the lecture's page
  console.log(`[notes e2e] GET /notes: ${getsBeforeMove} on the live route, ${gets - getsBeforeMove} after the move; POST: ${posts.length}`);
  expect(posts).toHaveLength(0);
  const notes = page.locator("#lecture-notes");
  await expect(notes.getByRole("heading", { level: 2, name: "Notes" })).toBeVisible();
  await expect(notes).toContainText("Nothing is sent unless you press it. (Draft wording.)");
  await page.waitForTimeout(1500);
  expect(posts).toHaveLength(0);

  // The press: exactly one POST, JSON, the DRAFT notice id, a fresh key; 202 for a new attempt.
  const pressed = page.waitForResponse((r) => r.url().endsWith(`/sessions/${sid}/notes`) && r.request().method() === "POST");
  await page.getByRole("button", { name: "Make notes" }).click();
  const res = await pressed;
  expect(res.status()).toBe(202);
  expect(res.headers()["location"]).toBe(`/api/v1/sessions/${sid}/notes`);
  const first = (await res.json()) as { notesId: string; status: string };
  expect(posts).toHaveLength(1);
  expect(posts[0]!.key).toMatch(KEY_RE);
  expect(posts[0]!.type).toMatch(/^application\/json/);
  expect(JSON.parse(posts[0]!.body!)).toEqual({ noticeVersion: "notes-notice-draft-1" });
  expect(["Pending", "Ready"]).toContain(first.status);
  console.log(`[notes e2e] the press answered 202 ${first.status}`);
  if (first.status === "Pending") await expect(page.getByTestId("notes-pending")).toBeVisible();
  await expect(page.locator("#notes-h2")).toBeFocused();

  // Ready from the poll: the sheet, the Demo notes tag (mock provider), cited bullets.
  await expect(page.locator("#lecture-notes.lecture-sheet")).toBeVisible({ timeout: 90_000 });
  await expect(notes.getByTestId("demo-notes-tag")).toContainText("Demo notes");
  await expect(notes.getByTestId("notes-stamp")).toContainText(/^Covers \d\d:\d\d to \d\d:\d\d\./);
  await expect(page.getByRole("button", { name: "Make notes" })).toHaveCount(0);
  expect(posts).toHaveLength(1);
  const cite = notes.getByRole("button", { name: /^\d\d:\d\d, go to transcript line$/ }).first();
  await cite.click();
  await expect(page.locator("[data-segment-id]:focus")).toHaveCount(1);

  // The wire state behind it: Ready, the Final summary made by the same press, readable as the latest GET /summary.
  const wire = (await (await page.request.get(`${ORIGIN}/api/v1/sessions/${sid}/notes`)).json()) as {
    notesId: string;
    status: string;
    source: string;
    finalSummaryStatus: string | null;
    finalSummaryRevision: number | null;
  };
  expect(wire).toMatchObject({ notesId: first.notesId, status: "Ready", source: "mock" });
  expect(wire.finalSummaryStatus).toBe("Ready");
  const summary = (await (await page.request.get(`${ORIGIN}/api/v1/sessions/${sid}/summary`)).json()) as { status: string; revision: number };
  expect(summary).toMatchObject({ status: "Final", revision: wire.finalSummaryRevision });
  await expect(page.locator("#lecture-tldr").getByTestId("tldr-final")).toHaveText("Final summary", { timeout: 30_000 });

  // A second press (another key, the same student) starts nothing: the same notes come back with 200.
  const again = await page.request.post(`${ORIGIN}/api/v1/sessions/${sid}/notes`, {
    headers: { Origin: ORIGIN, "Idempotency-Key": `again-${uid()}` },
    data: { noticeVersion: "notes-notice-draft-1" },
  });
  expect(again.status()).toBe(200);
  expect(((await again.json()) as { notesId: string }).notesId).toBe(first.notesId);

  // Reopen and reload: the notes are read back with GET, never a POST, and the TL;DR shows the Final revision. (The live
  // route /courses/{id}/lecture is the start flow: reloading it shows the consent page, as before this slice, so the
  // stopped lecture is reopened on its own route, /lectures/{id}, and that page is reloaded.)
  const uiPostsBefore = posts.length;
  for (const step of ["reopen", "reload"] as const) {
    const get = page.waitForResponse((r) => r.url().endsWith(`/sessions/${sid}/notes`) && r.request().method() === "GET");
    if (step === "reopen") await page.goto(`/lectures/${sid}`);
    else await page.reload();
    expect((await get).status(), step).toBe(200);
    await expect(page.locator("#lecture-notes.lecture-sheet")).toBeVisible();
    await expect(page.locator("#lecture-notes").getByTestId("demo-notes-tag")).toBeVisible();
    await expect(page.locator("#lecture-tldr").getByTestId("tldr-final")).toHaveText("Final summary");
    await expect(page.getByRole("button", { name: "Make notes" })).toHaveCount(0);
  }
  await page.waitForTimeout(1500);
  expect(posts.length).toBe(uiPostsBefore);

  // Another user can neither read the notes nor open the lecture (404 in the UI).
  const bob = await browser.newContext({ storageState: stateFor("bob"), extraHTTPHeaders: { Origin: ORIGIN } });
  expect((await bob.request.get(`${ORIGIN}/api/v1/sessions/${sid}/notes`)).status()).toBe(404);
  const bobPost = await bob.request.post(`${ORIGIN}/api/v1/sessions/${sid}/notes`, {
    headers: { Origin: ORIGIN, "Idempotency-Key": `bob-${uid()}` },
    data: { noticeVersion: "notes-notice-draft-1" },
  });
  expect(bobPost.status()).toBe(404);
  const bobPage = await bob.newPage();
  await bobPage.goto(`/lectures/${sid}`);
  await expect(bobPage.getByRole("heading", { name: "We can't find that lecture" })).toBeVisible();
  await expect(bobPage.locator("#lecture-notes")).toHaveCount(0);
  await bob.close();
});
