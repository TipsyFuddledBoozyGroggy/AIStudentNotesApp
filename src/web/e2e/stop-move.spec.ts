import { test, expect, type APIRequestContext, type Page } from "@playwright/test";
import { uid } from "./helpers";
import { stateFor } from "./users";

// PO-19 on the REAL stack (docs/ux/stop-redirect.md section 6, criteria 13 to 17): API + Postgres, the backend's
// deterministic ReplaySttProvider and its mock AI (no language model, no vendor, no real classroom audio; the fake Chromium
// microphone only paces the replay). Own user `stopmove`: the "Don't show this again" preference is per user and this
// spec turns it on and off. Serial: one lecture per user at a time, and the preference carries from test to test through
// a shared browser context (one device).
test.describe.configure({ mode: "serial" });

const ORIGIN = "http://localhost:3000";
const NOTICE = "This lecture has its own page";
const MOVED = "Lecture stopped. This is the lecture's own page.";
const started: string[] = [];

async function postWithRetry(ctx: APIRequestContext, path: string, data?: unknown) {
  for (let i = 0; i < 16; i++) {
    const res = await ctx.post(`${ORIGIN}${path}`, { headers: { Origin: ORIGIN }, data });
    if (res.status() !== 429) return res;
    await new Promise((r) => setTimeout(r, 5000));
  }
  throw new Error("still rate limited");
}

const course = { id: "", name: `Stop move e2e ${uid()}` };
let page: Page;
test.beforeAll(async ({ browser, playwright }) => {
  const ctx = await playwright.request.newContext({ baseURL: ORIGIN, storageState: stateFor("stopmove"), extraHTTPHeaders: { Origin: ORIGIN } });
  const res = await postWithRetry(ctx, "/api/v1/courses", { name: course.name });
  expect(res.status()).toBe(201);
  course.id = ((await res.json()) as { id: string }).id;
  await ctx.dispose();
  const context = await browser.newContext({ storageState: stateFor("stopmove") });
  page = await context.newPage();
  // Records every status-region text and whether a move notice was ever in the DOM, across client navigations.
  await page.addInitScript(() => {
    const w = window as unknown as { __status: string[]; __notice: boolean };
    w.__status = [];
    w.__notice = false;
    new MutationObserver(() => {
      const el = document.querySelector('[data-region="status"]');
      const t = el?.textContent ?? "";
      if (t && w.__status.at(-1) !== t) w.__status.push(t);
      if (!t && w.__status.at(-1) !== "") w.__status.push("");
      if (document.querySelector('[data-testid="stop-move-dialog"]')) w.__notice = true;
    }).observe(document, { childList: true, subtree: true, characterData: true });
  });
});
test.afterAll(async ({ playwright }) => {
  const ctx = await playwright.request.newContext({ baseURL: ORIGIN, storageState: stateFor("stopmove"), extraHTTPHeaders: { Origin: ORIGIN } });
  for (const id of started.splice(0)) await postWithRetry(ctx, `/api/v1/sessions/${id}/stop`).catch(() => undefined);
  await ctx.dispose();
  await page?.context().close();
});

const statusLog = () => page.evaluate(() => (window as unknown as { __status: string[] }).__status.filter(Boolean));
const noticeSeen = () => page.evaluate(() => (window as unknown as { __notice: boolean }).__notice);
const resetWatch = () => page.evaluate(() => Object.assign(window, { __status: [], __notice: false }));

/** Course page -> "Start a lecture" (a client push, as a student goes) -> consent -> recording. Returns the session id. */
async function startFromCoursePage(): Promise<string> {
  await page.goto(`/courses/${course.id}`);
  await page.getByRole("link", { name: "Start a lecture" }).click();
  await expect(page).toHaveURL(new RegExp(`/courses/${course.id}/lecture$`));
  await expect(page.getByRole("heading", { level: 1, name: `Start lecture: ${course.name}` })).toBeVisible();
  await page.getByRole("button", { name: "Start recording", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Did your teacher say you can record?" });
  await dialog.getByRole("checkbox", { name: "My teacher said I can record this class." }).check();
  const created = page.waitForResponse((r) => /\/sessions$/.test(r.url()) && r.request().method() === "POST");
  await dialog.getByRole("button", { name: "Confirm and start recording" }).click();
  const res = await created;
  expect(res.status()).toBe(201);
  const body = (await res.json()) as { id: string; demoSource?: boolean };
  expect(body.demoSource).toBe(true);
  started.push(body.id);
  await expect(page.getByTestId("state-label")).toHaveText("Recording");
  return body.id;
}

async function confirmStop(sid: string) {
  await page.getByRole("button", { name: "Stop", exact: true }).first().click();
  await page.getByRole("dialog", { name: "Stop and save this lecture?" }).getByRole("button", { name: "Stop", exact: true }).click();
  started.splice(started.indexOf(sid), 1);
}

const historyLength = () => page.evaluate(() => history.length);

test("13 + 14: Stop -> the notice -> Open the lecture's page: the URL is replaced, Back goes to the course page; a reload shows the stopped lecture with no notice and no announcement", async () => {
  test.setTimeout(120_000);
  const sid = await startFromCoursePage();
  const before = await historyLength();
  await resetWatch();
  await confirmStop(sid);
  const notice = page.getByRole("dialog", { name: NOTICE });
  await expect(notice).toBeVisible();
  await expect(notice.getByRole("button", { name: "Open the lecture's page" })).toBeFocused();
  await expect(page).toHaveURL(new RegExp(`/courses/${course.id}/lecture$`)); // nothing moved without a press
  await notice.getByRole("button", { name: "Open the lecture's page" }).click();
  await expect(page).toHaveURL(new RegExp(`/lectures/${sid}$`));
  await expect(page.getByTestId("state-label")).toHaveText("Stopped");
  await expect(page.locator("#lecture-notes").getByRole("button", { name: "Make notes" })).toBeVisible();
  expect(await historyLength()).toBe(before); // replaced, not pushed
  await expect(page.locator("#stopped-h2")).toBeFocused(); // the arrival marker
  await expect.poll(statusLog).toContain(MOVED);
  expect((await statusLog()).filter((t) => t === MOVED)).toHaveLength(1);
  // The reopen page never links to itself.
  await expect(page.getByRole("link", { name: "Open this lecture's page" })).toHaveCount(0);

  await page.goBack();
  await expect(page).toHaveURL(new RegExp(`/courses/${course.id}$`)); // not the start screen
  await expect(page.getByRole("link", { name: "Start a lecture" })).toBeVisible();

  await page.goForward();
  await expect(page).toHaveURL(new RegExp(`/lectures/${sid}$`));
  await page.reload();
  await resetWatch();
  await expect(page.getByTestId("state-label")).toHaveText("Stopped");
  await expect(page.locator("#lecture-notes").getByRole("button", { name: "Make notes" })).toBeVisible();
  await page.waitForTimeout(1500);
  expect(await noticeSeen()).toBe(false);
  expect(await page.getByRole("dialog").count()).toBe(0);
  expect(await page.locator('[data-region="status"]').textContent()).not.toContain(MOVED);
  expect(await statusLog()).not.toContain(MOVED);
  const focused = await page.evaluate(() => document.activeElement?.id || document.activeElement?.tagName);
  console.log(`[stop-move e2e] after reload the focus is on ${focused}`);
  expect(focused).not.toBe("stopped-h2"); // a reload moves no focus
});

test("17: Stay here keeps the live route with focus on the Stopped heading; the Stopped note's link replaces the URL with the lecture's page and Back goes to the course page", async () => {
  test.setTimeout(120_000);
  const sid = await startFromCoursePage();
  await confirmStop(sid);
  const notice = page.getByRole("dialog", { name: NOTICE });
  await notice.getByRole("checkbox", { name: "Don't show this again" }).check(); // discarded by Stay here
  await notice.getByRole("button", { name: "Stay here" }).click();
  await expect(notice).toHaveCount(0);
  await expect(page).toHaveURL(new RegExp(`/courses/${course.id}/lecture$`));
  await expect(page.locator("#stopped-h2")).toBeFocused();
  const link = page.getByRole("link", { name: "Open this lecture's page" });
  await expect(link).toHaveAttribute("href", `/lectures/${sid}`);
  const before = await historyLength();
  await link.click();
  await expect(page).toHaveURL(new RegExp(`/lectures/${sid}$`));
  await expect(page.getByTestId("state-label")).toHaveText("Stopped");
  expect(await historyLength()).toBe(before); // replaced, not pushed (Lead decision 1 on the Stop redirect)
  await expect(page.locator("#stopped-h2")).not.toBeFocused(); // no marker on this path: no arrival focus
  await page.goBack();
  // Back skips the start flow (whose consent screen was the original confusion) and lands on the course page, exactly
  // as after the notice's "Open the lecture's page" (test 13 + 14).
  await expect(page).toHaveURL(new RegExp(`/courses/${course.id}$`));
  await expect(page.getByRole("link", { name: "Start a lecture" })).toBeVisible();
  await expect(page.getByRole("heading", { level: 1, name: `Start lecture: ${course.name}` })).toHaveCount(0);
  // The checked box was discarded: the next Stop asks again (asserted in the next test's first lecture).
  expect(await page.evaluate(() => Object.keys(localStorage).filter((k) => k.includes("stopMove")))).toEqual([]);
});

test("15: the box checked on Open: the next Stop moves at once with no notice, focus on the Lecture stopped heading, one announcement", async () => {
  test.setTimeout(180_000);
  const sid2 = await startFromCoursePage();
  await confirmStop(sid2);
  const notice = page.getByRole("dialog", { name: NOTICE });
  await notice.getByRole("checkbox", { name: "Don't show this again" }).check();
  await notice.getByRole("button", { name: "Open the lecture's page" }).click();
  await expect(page).toHaveURL(new RegExp(`/lectures/${sid2}$`));
  await expect(page.getByTestId("state-label")).toHaveText("Stopped");

  const sid3 = await startFromCoursePage();
  const before = await historyLength();
  await resetWatch();
  await confirmStop(sid3);
  await expect(page).toHaveURL(new RegExp(`/lectures/${sid3}$`));
  await expect(page.getByTestId("state-label")).toHaveText("Stopped");
  await expect(page.getByRole("heading", { name: "Lecture stopped" })).toBeFocused();
  await expect.poll(statusLog).toContain(MOVED);
  await page.waitForTimeout(1500);
  expect((await statusLog()).filter((t) => t === MOVED)).toHaveLength(1);
  expect(await noticeSeen()).toBe(false);
  expect(await historyLength()).toBe(before);
});

test("16: Preferences > Lectures reads Off after the box; On brings the notice back; Off again moves directly", async () => {
  test.setTimeout(180_000);
  const sw = () => page.getByRole("switch", { name: "Ask before opening the lecture's page after Stop" });
  await page.goto(`/courses/${course.id}`);
  await page.getByRole("button", { name: "Preferences" }).click();
  await expect(page.getByRole("heading", { level: 3, name: "Lectures" })).toBeVisible();
  await expect(sw()).toHaveAttribute("aria-checked", "false");
  await sw().click();
  await expect(sw()).toHaveAttribute("aria-checked", "true");
  await expect(page.locator("#preferences [role=status]")).toHaveText("Ask before opening the lecture's page: on.");

  const sid4 = await startFromCoursePage();
  await resetWatch();
  await confirmStop(sid4);
  const notice = page.getByRole("dialog", { name: NOTICE });
  await expect(notice).toBeVisible();
  await notice.getByRole("button", { name: "Open the lecture's page" }).click();
  await expect(page).toHaveURL(new RegExp(`/lectures/${sid4}$`));

  await page.goto(`/courses/${course.id}`);
  await page.getByRole("button", { name: "Preferences" }).click();
  await expect(sw()).toHaveAttribute("aria-checked", "true");
  await sw().click();
  await expect(sw()).toHaveAttribute("aria-checked", "false");
  await expect(page.locator("#preferences [role=status]")).toHaveText("Ask before opening the lecture's page: off.");
  await page.getByRole("button", { name: "Close" }).click();

  const sid5 = await startFromCoursePage();
  await resetWatch();
  await confirmStop(sid5);
  await expect(page).toHaveURL(new RegExp(`/lectures/${sid5}$`));
  await expect(page.getByTestId("state-label")).toHaveText("Stopped");
  expect(await noticeSeen()).toBe(false);
});
