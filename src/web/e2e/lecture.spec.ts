import { test, expect, type APIRequestContext, type Page } from "@playwright/test";
import { uid } from "./helpers";
import { stateFor } from "./users";

// Real stack (API + Postgres in docker, `npm run dev:real`). The transcript source is the backend's deterministic
// ReplaySttProvider ("Demo replay segment ..."): microphone audio only paces it. Nothing here is a real vendor
// or a real classroom recording. One lecture per user at a time, so these run serially as alice.
test.describe.configure({ mode: "serial" });
test.use({ storageState: stateFor("alice") });

const ORIGIN = "http://localhost:3000";
const DEMO_LINE_1 = "Demo replay segment one";
const startedSessions: string[] = [];

// A failed test must not leave a live session behind (one recording per user would then block the next test).
/** The API allows 30 writes per minute per user; wait out a 429 rather than leave a live session behind. */
async function postWithRetry(ctx: APIRequestContext, path: string, data?: unknown) {
  for (let i = 0; i < 16; i++) {
    const res = await ctx.post(`${ORIGIN}${path}`, { headers: { Origin: ORIGIN }, data });
    if (res.status() !== 429) return res;
    await new Promise((r) => setTimeout(r, 5000));
  }
  throw new Error("still rate limited");
}

test.afterEach(async ({ request }) => {
  for (const id of startedSessions.splice(0)) await postWithRetry(request, `/api/v1/sessions/${id}/stop`).catch(() => undefined);
});

const transcript = (page: Page) => page.getByRole("log", { name: "Transcript lines" });

const gumCalls = (page: Page) => page.evaluate(() => (window as unknown as { __gum?: number }).__gum ?? 0);

/** Counts getUserMedia calls without changing behavior; "deny" rejects like a blocked permission. */
async function spyMic(page: Page, mode: "pass" | "deny" = "pass") {
  await page.addInitScript((m) => {
    const w = window as unknown as { __gum: number };
    w.__gum = 0;
    const md = navigator.mediaDevices;
    const real = md.getUserMedia.bind(md);
    md.getUserMedia = (c) => {
      w.__gum++;
      return m === "deny" ? Promise.reject(new DOMException("blocked", "NotAllowedError")) : real(c);
    };
  }, mode);
}

// One course for the file, created over the API: the API allows 30 writes per minute per user.
const course = { id: "", name: `Lecture e2e ${uid()}` };
test.beforeAll(async ({ playwright }) => {
  const ctx = await playwright.request.newContext({ baseURL: ORIGIN, storageState: stateFor("alice"), extraHTTPHeaders: { Origin: ORIGIN } });
  // Clear anything a crashed earlier run left live (one recording per user).
  const list = (await (await ctx.get("/api/v1/courses")).json()) as { items: { id: string }[] };
  for (const c of list.items) {
    const page = (await (await ctx.get(`/api/v1/courses/${c.id}/sessions?limit=50`)).json()) as { items?: { id: string; state: string }[] };
    for (const x of page.items ?? []) if (x.state === "Active" || x.state === "Paused") await postWithRetry(ctx, `/api/v1/sessions/${x.id}/stop`);
  }
  const res = await postWithRetry(ctx, "/api/v1/courses", { name: course.name });
  expect(res.status()).toBe(201);
  course.id = ((await res.json()) as { id: string }).id;
  await ctx.dispose();
});

async function openConsent(page: Page, _label: string) {
  await page.goto(`/courses/${course.id}/lecture`);
  await expect(page.getByRole("heading", { level: 1, name: `Start lecture: ${course.name}` })).toBeVisible();
}

/** Ticks the consent box, presses Start, returns the new session id from the create response. */
async function checkAndStart(page: Page): Promise<string> {
  await page.getByLabel(/I checked that recording and transcribing is allowed/).check();
  const created = page.waitForResponse((r) => /\/sessions$/.test(r.url()) && r.request().method() === "POST");
  await page.getByRole("button", { name: "Start recording" }).click();
  const res = await created;
  expect(res.status()).toBe(201);
  const body = (await res.json()) as { id: string; demoSource?: boolean };
  startedSessions.push(body.id);
  expect(body.demoSource).toBe(true);
  return body.id;
}

async function stopLecture(page: Page) {
  await page.getByRole("button", { name: "Stop", exact: true }).first().click();
  await page.getByRole("dialog").getByRole("button", { name: "Stop", exact: true }).click();
  await expect(page.getByTestId("state-label")).toHaveText("Stopped");
}

test("start needs consent; no mic prompt before Start; demo transcript arrives over the hub", async ({ page }) => {
  await spyMic(page);
  await openConsent(page, `Bio ${uid()}`);
  await expect(page.getByText("Draft", { exact: true }).first()).toBeVisible();
  await page.getByRole("button", { name: "Start recording" }).click({ force: true }); // aria-disabled, unchecked: does nothing
  expect(await gumCalls(page)).toBe(0);
  await expect(page.getByTestId("state-label")).toHaveCount(0);

  await checkAndStart(page);
  await expect(page.getByTestId("state-label")).toHaveText("Recording");
  expect(await gumCalls(page)).toBe(1);
  await expect(page.getByTestId("demo-tag")).toBeVisible();
  await expect(page.getByTestId("demo-notice")).toContainText("not from your microphone");
  await expect(page.getByTestId("connection")).toHaveText("Connected");
  await expect(transcript(page)).toContainText(DEMO_LINE_1);
  await expect(page).toHaveTitle(/^Recording \|/);
  await stopLecture(page);
  await expect(page.getByRole("heading", { name: "Lecture stopped" })).toBeVisible();
});

test("pause turns the microphone off; resume continues; stop ends it", async ({ page }) => {
  await spyMic(page);
  await openConsent(page, `Pause ${uid()}`);
  await checkAndStart(page);
  await expect(transcript(page)).toContainText(DEMO_LINE_1);

  await page.getByRole("button", { name: "Pause", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Recording is paused" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Resume recording" })).toBeFocused();
  await expect(page.getByTestId("state-label")).toHaveText("Paused");

  await page.getByRole("button", { name: "Resume recording" }).click();
  await expect(page.getByTestId("state-label")).toHaveText("Recording");
  expect(await gumCalls(page)).toBe(2); // one per press: start, resume
  await stopLecture(page);
});

test("hub drop shows Not connected, then reconnects with no duplicate lines", async ({ page }) => {
  await spyMic(page);
  const hubSockets: { close(): void }[] = [];
  let blockHub = false;
  await page.routeWebSocket(/\/hubs\/lecture/, (ws) => {
    if (blockHub) return void ws.close();
    ws.connectToServer();
    hubSockets.push(ws);
  });
  await openConsent(page, `Net ${uid()}`);
  await checkAndStart(page);
  await expect(transcript(page)).toContainText(DEMO_LINE_1);

  blockHub = true;
  hubSockets.forEach((s) => s.close());
  await expect(page.getByTestId("connection")).toHaveText("Not connected");
  await expect(page.getByTestId("state-label")).not.toHaveText("Recording");
  await expect(page.getByText("Not connected. Audio is not being saved.").first()).toBeVisible();

  blockHub = false;
  await expect(page.getByTestId("connection")).toHaveText("Connected", { timeout: 60_000 });
  await expect(page.getByTestId("state-label")).toHaveText("Recording");
  const ids = await page.locator("[data-segment-id]").evaluateAll((els) => els.map((e) => e.getAttribute("data-segment-id")));
  expect(new Set(ids).size).toBe(ids.length);
  await stopLecture(page);
});

test("denied microphone: calm blocked screen, no retry loop, session stopped", async ({ page }) => {
  await spyMic(page, "deny");
  await openConsent(page, `Denied ${uid()}`);
  await checkAndStart(page).catch(() => undefined);
  await expect(page.getByRole("heading", { name: "Microphone is blocked" })).toBeVisible();
  await expect(page.getByText("Nothing has been recorded.")).toBeVisible();
  expect(await gumCalls(page)).toBe(1);
  await page.waitForTimeout(2500);
  expect(await gumCalls(page)).toBe(1); // no automatic retry
  await expect(page.getByRole("button", { name: "Try again" })).toBeVisible();
});

test("another user cannot read, join or stream into someone else's lecture", async ({ page, browser }) => {
  await spyMic(page);
  await openConsent(page, `Private ${uid()}`);
  const sid = await checkAndStart(page);
  await expect(transcript(page)).toContainText(DEMO_LINE_1);

  const bobCtx = await browser.newContext({ storageState: stateFor("bob"), extraHTTPHeaders: { Origin: ORIGIN } });
  for (const [method, path] of [
    ["GET", `/api/v1/sessions/${sid}`],
    ["GET", `/api/v1/sessions/${sid}/transcript?afterSeq=0&limit=50`],
    ["POST", `/api/v1/sessions/${sid}/audio-ticket`],
    ["POST", `/api/v1/sessions/${sid}/pause`],
  ] as const) {
    const res = await bobCtx.request.fetch(`${ORIGIN}${path}`, { method });
    expect(res.status(), `${method} ${path}`).toBe(404);
  }
  const bobPage = await bobCtx.newPage();
  await bobPage.goto(`/lectures/${sid}`);
  await expect(bobPage.getByRole("heading", { name: "We can't find that lecture" })).toBeVisible();
  await bobCtx.close();

  await expect(page.getByTestId("state-label")).toHaveText("Recording"); // alice's lecture was untouched
  await stopLecture(page);
});

// --- AI routes (demo mode: the backend's deterministic mock AI, not a language model) --------------------------

const catchUpButton = (page: Page, minutes: number) => page.getByRole("button", { name: `recap of the last ${minutes} minutes` });

test("Quick recap: one press posts once with an Idempotency-Key, shows what was said, then the Demo summary recap; citation opens the transcript line", async ({ page }) => {
  await spyMic(page);
  const posts: { key: string | null; body: string | null }[] = [];
  page.on("request", (r) => {
    if (r.method() === "POST" && /\/sessions\/[^/]+\/quick-recap$/.test(r.url())) posts.push({ key: r.headers()["idempotency-key"] ?? null, body: r.postData() });
  });
  await openConsent(page, `QuickRecap ${uid()}`);
  await checkAndStart(page);
  await expect(transcript(page)).toContainText(DEMO_LINE_1);
  await expect(page.locator("[data-segment-id]").first()).toBeVisible(); // a saved (final) line, not only a partial

  // Three windows, no confirm step.
  await expect(page.getByRole("button", { name: /recap of the last \d+ minutes$/ })).toHaveCount(3);
  await catchUpButton(page, 5).click();
  const result = page.getByTestId("catchup-result");
  await expect(result).toBeVisible();
  await expect(result.getByRole("heading", { name: "Here is what was said" })).toBeFocused();
  expect(posts).toHaveLength(1);
  expect(posts[0]!.key).toMatch(/^[A-Za-z0-9_-]{1,64}$/);
  expect(JSON.parse(posts[0]!.body!)).toEqual({ windowMinutes: 5 });

  // The AI version (hub catchup.ready or the bounded poll) replaces the excerpt, labelled as a demo.
  await expect(result).toHaveAttribute("data-kind", "Ai", { timeout: 30_000 });
  await expect(result.getByTestId("demo-summary-tag")).toContainText("Demo summary");
  const cite = result.getByRole("button", { name: /go to transcript line$/ }).first();
  await cite.click();
  await expect(page.locator("[data-segment-id]:focus")).toHaveCount(1);

  await result.getByRole("button", { name: "Back to lecture" }).click();
  await expect(result).toHaveCount(0);
  await expect(transcript(page)).toBeFocused();
  expect(posts).toHaveLength(1);
  await stopLecture(page);
});

test("TL;DR: the live summary arrives labelled as a Demo summary and is fetched again with GET when the lecture is reopened", async ({ page }) => {
  test.setTimeout(180_000); // the summary job flushes after 90 s (or 8 lines); the demo script has 4
  await spyMic(page);
  await openConsent(page, `Tldr ${uid()}`);
  const sid = await checkAndStart(page);
  await expect(transcript(page)).toContainText(DEMO_LINE_1);
  const tldr = page.locator("#lecture-tldr");
  await expect(tldr.getByTestId("demo-summary-tag")).toContainText("Demo summary", { timeout: 140_000 });
  await expect(tldr).not.toContainText("The summary will appear here");
  await stopLecture(page);

  const summaryGet = page.waitForResponse((r) => new RegExp(`/sessions/${sid}/summary$`).test(r.url()));
  await page.goto(`/lectures/${sid}`);
  expect((await summaryGet).status()).toBe(200);
  await expect(page.locator("#lecture-tldr").getByTestId("demo-summary-tag")).toBeVisible();
});

test("Quick recap on an ended lecture and under a rate limit: calm message, one request each, no retry", async ({ page }) => {
  await spyMic(page);
  await openConsent(page, `LtErr ${uid()}`);
  await checkAndStart(page);
  await expect(transcript(page)).toContainText(DEMO_LINE_1);

  let limited = 0;
  await page.route(/\/sessions\/[^/]+\/quick-recap$/, (route) => {
    limited++;
    return route.fulfill({ status: 429, contentType: "application/problem+json", body: JSON.stringify({ status: 429, code: "rate_limited", title: "Too many requests." }) });
  });
  await catchUpButton(page, 2).click();
  await expect(page.getByTestId("qr-error")).toContainText("Try again in about a minute");
  await page.waitForTimeout(2500);
  expect(limited).toBe(1);
  await page.unroute(/\/sessions\/[^/]+\/quick-recap$/);

  await stopLecture(page);
  await catchUpButton(page, 2).click();
  await expect(page.getByTestId("qr-error")).toContainText("This lecture has ended");
});

test("Quick recap stays on screen on a phone while the transcript scrolls, and the skip link reaches it", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 667 });
  await spyMic(page);
  await openConsent(page, `LtPhone ${uid()}`);
  await checkAndStart(page);
  await expect(transcript(page)).toContainText(DEMO_LINE_1);
  const usual = page.getByRole("button", { name: /^Last 5 min \(your usual\): recap of the last 5 minutes$/ });
  await expect(usual).toBeInViewport();
  await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight));
  await expect(usual).toBeInViewport();
  const box = await usual.boundingBox();
  expect(box!.height).toBeGreaterThanOrEqual(44);
  await page.keyboard.press("Tab"); // first stop is a skip link; "Skip to Quick recap" is reachable by keyboard
  await page.getByRole("link", { name: "Skip to Quick recap" }).focus();
  await page.keyboard.press("Enter");
  await expect(usual).toBeFocused();
  await stopLecture(page);
});
