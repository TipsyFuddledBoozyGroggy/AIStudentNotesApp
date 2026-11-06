import { test, expect, type APIRequestContext, type Page, type Request } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { settle } from "../e2e-screens/settle";
import { uid } from "./helpers";
import { stateFor } from "./users";

// Finish-backlog item 3 (PO-20cb / PO-20cc): Save to a Demo Doc, against the REAL stack (Next dev on :3000 proxying the API
// on :5080): the backend's ReplaySttProvider for the transcript, its MOCK AI for the notes, and the Docs module's
// DemoDocsProvider, which keeps the "document" in our own database. No Google, no token, no network beyond the two local
// servers. The provider failure (F21) is forced with the Development-only X-Demo-Docs-Fault request header, added by a page
// route to exactly one confirm; the API honours it only because Docs__Demo__Faults__Enabled is set (docker-compose.dev.yml
// and the CI API step). Users: docsExport (its own) and docsOther (F23). Run serially (--workers=1).
test.describe.configure({ mode: "serial" });
test.use({ storageState: stateFor("docsExport") });

const ORIGIN = "http://localhost:3000";
const DEMO_LINE_1 = "Demo replay segment one";
const started: string[] = [];
const lecture = { id: "", docId: "" };

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

const course = { id: "", name: `Demo Doc e2e ${uid()}` };
test.beforeAll(async ({ playwright }) => {
  const ctx = await playwright.request.newContext({ baseURL: ORIGIN, storageState: stateFor("docsExport"), extraHTTPHeaders: { Origin: ORIGIN } });
  const res = await postWithRetry(ctx, "/api/v1/courses", { name: course.name });
  expect(res.status()).toBe(201);
  course.id = ((await res.json()) as { id: string }).id;
  await ctx.dispose();
});

/** Start through the UI (consent dialog, Demo source), wait for a saved line, Stop, and open the lecture's own page. */
async function finishedDemoLecture(page: Page): Promise<string> {
  await page.goto(`/courses/${course.id}/lecture`);
  await expect(page.getByRole("heading", { level: 1, name: `Start lecture: ${course.name}` })).toBeVisible();
  await settle(page); // L-HYDRATE
  await page.getByRole("button", { name: "Start recording", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Did your teacher say you can record?" });
  await dialog.getByRole("checkbox", { name: "My teacher said I can record this class." }).check();
  const created = page.waitForResponse((r) => /\/sessions$/.test(r.url()) && r.request().method() === "POST");
  await dialog.getByRole("button", { name: "Confirm and start recording" }).click();
  const res = await created;
  expect(res.status()).toBe(201);
  const body = (await res.json()) as { id: string; demoSource?: boolean };
  expect(body.demoSource).toBe(true); // the replay source, not a real speech service
  started.push(body.id);
  await expect(page.getByRole("log", { name: "Transcript lines" })).toContainText(DEMO_LINE_1);
  await expect(page.locator("[data-segment-id]").first()).toBeVisible();
  await page.getByRole("button", { name: "Stop", exact: true }).first().click();
  await page.getByRole("dialog").getByRole("button", { name: "Stop", exact: true }).click();
  await expect(page.getByTestId("state-label")).toHaveText("Stopped");
  started.splice(started.indexOf(body.id), 1);
  await page.getByRole("dialog", { name: "This lecture has its own page" }).getByRole("button", { name: "Open the lecture's page" }).click();
  await expect(page).toHaveURL(new RegExp(`/lectures/${body.id}$`));
  return body.id;
}

async function readyNotes(page: Page) {
  await expect(page.locator("#lecture-notes.lecture-sheet")).toBeVisible({ timeout: 90_000 });
  await settle(page);
}

async function scan(page: Page, label: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa", "best-practice"]).analyze();
  expect(r.violations.map((v) => `${label}: ${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`)).toEqual([]);
}

/** The notes sheet's bullets as "text [mm:ss] [mm:ss]", the form the Demo Doc shows them in. */
async function sheetBullets(page: Page): Promise<string[]> {
  return page.locator("#lecture-notes [data-notes-bullet]").evaluateAll((lis) =>
    lis.map((li) => {
      const text = (li.querySelector("span.max-w-prose")?.firstChild?.textContent ?? "").trim();
      const times = Array.from(li.querySelectorAll(".cite")).map((c) => `[${c.textContent!.trim()}]`);
      return `${text} ${times.join(" ")}`;
    }),
  );
}
const docBullets = (scope: ReturnType<Page["locator"]>) => scope.locator("[data-doc-bullet]").evaluateAll((lis) => lis.map((li) => li.textContent!.replace(/\s+/g, " ").trim()));

const isConfirm = (r: Request) => r.method() === "POST" && /\/api\/v1\/sessions\/[^/]+\/docs-export$/.test(r.url());

async function openDialog(page: Page) {
  const entry = page.getByRole("button", { name: "Save to a Demo Doc" });
  await expect(entry).toBeVisible();
  await expect(entry).not.toHaveAttribute("aria-busy", "true");
  await entry.click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByRole("heading", { level: 2 })).toBeFocused();
  await expect(dialog.getByTestId("docs-loading")).toHaveCount(0);
  return dialog;
}

test("F19/F20: preview, confirm, success, open the Demo Doc with the same bullets; a second export already matches and writes nothing", async ({ page }) => {
  test.setTimeout(240_000);
  lecture.id = await finishedDemoLecture(page);
  const notes = page.locator("#lecture-notes");
  await settle(page);
  const make = page.getByRole("button", { name: "Make notes" });
  await expect(make).not.toHaveAttribute("aria-busy", "true");
  await make.click();
  await readyNotes(page);
  const shown = await sheetBullets(page);
  expect(shown.length).toBeGreaterThan(0);

  const entry = notes.getByRole("button", { name: "Save to a Demo Doc" });
  await expect(entry).toBeVisible();
  for (const b of [entry, notes.getByRole("button", { name: "Export notes" })]) {
    const box = (await b.boundingBox())!;
    expect(box.height, "44px target").toBeGreaterThanOrEqual(44);
  }
  await expect(page.locator("#docs-export-help")).toHaveText("A Demo Doc is a practice copy kept inside Tether. Nothing is sent to Google.");
  await expect(notes.getByTestId("docs-export-saved")).toHaveCount(0);

  const confirms: Request[] = [];
  page.on("request", (r) => {
    if (isConfirm(r)) confirms.push(r);
  });

  // Preview from the keyboard: nothing is saved yet.
  await entry.focus();
  await page.keyboard.press("Enter");
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByRole("heading", { level: 2, name: "Check before saving" })).toBeFocused();
  await expect(dialog.getByTestId("docs-preview")).toContainText("This is exactly what will be saved. Nothing has been saved yet.");
  await expect(dialog.getByTestId("docs-preview")).toContainText("Where: a Demo Doc (a practice copy inside Tether, not Google Docs)");
  await expect(dialog.getByTestId("docs-preview-title")).toHaveText(/^Title: Lecture notes - [A-Z][a-z]{2} \d{1,2}, \d{4} UTC$/);
  await expect(dialog.getByTestId("demo-notes-tag")).toBeVisible();
  const region = dialog.getByRole("region", { name: "What will be saved" });
  expect(await docBullets(region)).toEqual(shown);
  expect(confirms).toHaveLength(0);
  await settle(page);
  await scan(page, "docs-preview");

  const save = dialog.getByRole("button", { name: "Save to Demo Doc" });
  const box = (await save.boundingBox())!;
  expect(box.height, "44px target").toBeGreaterThanOrEqual(44);
  const answered = page.waitForResponse((r) => isConfirm(r.request()));
  await save.click();
  const res = await answered;
  expect(res.status()).toBe(200);
  const record = (await res.json()) as { status: string; outcome: string; docRef: { docId: string; openPath: string; version: number } };
  expect([record.status, record.outcome, record.docRef.version]).toEqual(["succeeded", "created", 1]);
  lecture.docId = record.docRef.docId;
  await expect(dialog.getByTestId("docs-success")).toHaveText("Saved to a Demo Doc.");
  await expect(dialog.getByTestId("docs-success")).toBeFocused();
  await expect(dialog).toContainText("This copy stays inside Tether. It is deleted when you delete this lecture.");
  await expect(page.locator('[data-region="status"]')).toHaveText("Saved to a Demo Doc.", { timeout: 10_000 });
  expect(confirms).toHaveLength(1);
  expect(confirms[0]!.headers()["idempotency-key"]).toMatch(/^[A-Za-z0-9_-]{8,128}$/);
  const sentBody = confirms[0]!.postDataJSON() as Record<string, unknown>;
  expect(Object.keys(sentBody).sort()).toEqual(["confirm", "contentDigest", "expectedExistingDocId", "expectedExistingVersion", "mode", "notesId"]);
  await settle(page);
  await scan(page, "docs-success");

  // Open the Demo Doc: the same bullets with their times, the practice-copy banner, text only.
  await dialog.getByRole("link", { name: "Open Demo Doc" }).click();
  await expect(page).toHaveURL(new RegExp(`/demo-docs/${record.docRef.docId}\\?lecture=${lecture.id}$`));
  const doc = page.getByTestId("demo-doc");
  await expect(doc).toBeVisible();
  await settle(page);
  await expect(page.getByTestId("demo-doc-banner")).toHaveText("Demo Doc: a practice copy kept inside Tether. It is not in Google Docs.");
  expect(await docBullets(doc)).toEqual(shown);
  expect(await doc.locator("img, script, a").count()).toBe(0);
  await scan(page, "demo-doc-page");
  await page.setViewportSize({ width: 390, height: 844 });
  await settle(page);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), "no horizontal scroll at 390").toBe(true);
  await scan(page, "demo-doc-page-390");
  await page.setViewportSize({ width: 1280, height: 900 });

  // Back to the lecture: the entry now says when it was saved; a second export of the same notes has nothing to save.
  await page.getByRole("link", { name: "Back to the lecture" }).click();
  await expect(page).toHaveURL(new RegExp(`/lectures/${lecture.id}$`));
  await readyNotes(page);
  await expect(page.getByTestId("docs-export-saved")).toContainText(/^Saved to a Demo Doc on [A-Z][a-z]+ \d{1,2}\. Open Demo Doc$/);
  const again = await openDialog(page);
  await expect(again.getByTestId("docs-matches")).toHaveText("Your Demo Doc already matches these notes. There is nothing to save.");
  await expect(again.getByRole("button", { name: "Save to Demo Doc" })).toHaveCount(0);
  await again.getByRole("button", { name: "Close" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Save to a Demo Doc" })).toBeFocused();
  expect(confirms, "the already-matching export sent no confirm").toHaveLength(1);
});

test("Q3 delete the Demo Doc, then F21: a forced provider failure, then Try again succeeds with one document", async ({ page }) => {
  test.setTimeout(120_000);
  expect(lecture.docId).not.toBe("");
  await page.goto(`/demo-docs/${lecture.docId}?lecture=${lecture.id}`);
  await expect(page.getByTestId("demo-doc")).toBeVisible();
  await settle(page);
  const del = page.getByRole("button", { name: "Delete this Demo Doc" });
  await del.click();
  const confirmDel = page.getByRole("dialog", { name: "Delete this Demo Doc?" });
  await expect(confirmDel).toContainText("Your notes and lecture are not affected.");
  await expect(confirmDel.getByRole("button", { name: "Keep it" })).toBeFocused();
  await confirmDel.getByRole("button", { name: "Delete Demo Doc" }).click();
  await expect(page.getByTestId("demo-doc-deleted")).toHaveText("The Demo Doc was deleted.");
  await expect(page.getByTestId("demo-doc-deleted")).toBeFocused();
  const gone = await page.request.get(`${ORIGIN}/api/v1/demo-docs/${lecture.docId}`);
  expect(gone.status()).toBe(404);

  // The lecture has no Demo Doc now, so the next export is a create. Fault exactly the first confirm.
  await page.getByRole("link", { name: "Back to the lecture" }).click();
  await readyNotes(page);
  await expect(page.getByTestId("docs-export-saved")).toHaveCount(0);
  const confirms: Request[] = [];
  let faulted = 0;
  await page.route(/\/api\/v1\/sessions\/[^/]+\/docs-export$/, async (route) => {
    const r = route.request();
    if (r.method() === "POST" && faulted === 0) {
      faulted++;
      await route.continue({ headers: { ...r.headers(), "x-demo-docs-fault": "unavailable" } });
    } else await route.continue();
  });
  page.on("request", (r) => {
    if (isConfirm(r)) confirms.push(r);
  });
  const dialog = await openDialog(page);
  await expect(dialog.getByTestId("docs-preview")).toBeVisible();
  const first = page.waitForResponse((r) => isConfirm(r.request()));
  await dialog.getByRole("button", { name: "Save to Demo Doc" }).click();
  const failed = (await (await first).json()) as { status: string; exportId: string; attempt: number; error: { code: string; retryAfterSeconds: number } };
  expect([failed.status, failed.error.code, failed.attempt]).toEqual(["failed", "provider_unavailable", 1]);
  await expect(dialog.getByTestId("docs-retry")).toHaveText("That did not save. Nothing was saved, and your notes are not affected. You can try again.");
  await expect(dialog.getByTestId("docs-retry")).toBeFocused();
  await expect(dialog.getByTestId("docs-retry-wait")).toHaveText(`Please wait about ${failed.error.retryAfterSeconds} seconds, then try again.`);
  expect((await page.request.get(`${ORIGIN}/api/v1/sessions/${lecture.id}/docs-export`).then((r) => r.json())).target).toBeNull();
  await settle(page);
  await scan(page, "docs-retry");

  const tryAgain = dialog.getByRole("button", { name: "Try again" });
  await expect(tryAgain).not.toHaveAttribute("aria-disabled", "true");
  const second = page.waitForResponse((r) => isConfirm(r.request()));
  await tryAgain.click();
  const ok = (await (await second).json()) as { status: string; outcome: string; exportId: string; attempt: number; docRef: { docId: string } };
  expect([ok.status, ok.outcome, ok.attempt, ok.exportId]).toEqual(["succeeded", "created", 2, failed.exportId]);
  await expect(dialog.getByTestId("docs-success")).toHaveText("Saved to a Demo Doc.");
  expect(confirms).toHaveLength(2);
  expect(confirms[1]!.headers()["idempotency-key"]).toBe(confirms[0]!.headers()["idempotency-key"]);
  expect(confirms[1]!.postData()).toBe(confirms[0]!.postData());
  // One document: the lecture's state points at it, and a replay of the same key returns it without a second one.
  const state = (await page.request.get(`${ORIGIN}/api/v1/sessions/${lecture.id}/docs-export`).then((r) => r.json())) as { target: { docId: string; version: number } };
  expect([state.target.docId, state.target.version]).toEqual([ok.docRef.docId, 1]);
  const replay = await page.request.post(`${ORIGIN}/api/v1/sessions/${lecture.id}/docs-export`, {
    headers: { Origin: ORIGIN, "Idempotency-Key": confirms[0]!.headers()["idempotency-key"]!, "Content-Type": "application/json" },
    data: confirms[0]!.postData()!,
  });
  expect(replay.headers()["idempotency-replayed"]).toBe("true");
  expect(((await replay.json()) as { docRef: { docId: string } }).docRef.docId).toBe(ok.docRef.docId);
  lecture.docId = ok.docRef.docId;
  await page.unroute(/\/api\/v1\/sessions\/[^/]+\/docs-export$/);
});

test("M1: the save commits but its answer is lost; the line does not say nothing was saved, and Try again replays the same key into one document", async ({ page }) => {
  test.setTimeout(120_000);
  expect(lecture.docId).not.toBe("");
  // Start from no Demo Doc, so the next export is a create.
  const del = await page.request.delete(`${ORIGIN}/api/v1/demo-docs/${lecture.docId}`, { headers: { Origin: ORIGIN } });
  expect(del.status()).toBe(204);
  await page.goto(`/lectures/${lecture.id}`);
  await readyNotes(page);
  await expect(page.getByTestId("docs-export-saved")).toHaveCount(0);
  const confirms: Request[] = [];
  let lost = 0;
  // The first confirm reaches the API (which commits), then the browser is told the connection failed.
  await page.route(/\/api\/v1\/sessions\/[^/]+\/docs-export$/, async (route) => {
    const r = route.request();
    if (r.method() === "POST" && lost === 0) {
      lost++;
      const res = await route.fetch();
      expect(res.status()).toBe(200);
      await route.abort("failed");
    } else await route.continue();
  });
  page.on("request", (r) => {
    if (isConfirm(r)) confirms.push(r);
  });
  const dialog = await openDialog(page);
  await expect(dialog.getByTestId("docs-preview")).toBeVisible();
  await dialog.getByRole("button", { name: "Save to Demo Doc" }).click();
  const line = dialog.getByTestId("docs-retry");
  await expect(line).toHaveText("We did not get an answer, so we could not confirm whether it saved. Your notes are not affected. You can try again. It will not be saved twice.");
  await expect(line).toBeFocused();
  await expect(dialog).not.toContainText("Nothing was saved");
  await expect(dialog.getByTestId("docs-retry-wait")).toHaveCount(0);
  // The write did commit: the lecture already has a Demo Doc.
  const before = (await page.request.get(`${ORIGIN}/api/v1/sessions/${lecture.id}/docs-export`).then((r) => r.json())) as { target: { docId: string } | null };
  expect(before.target).not.toBeNull();
  await settle(page);
  await scan(page, "docs-retry-unknown");

  const tryAgain = dialog.getByRole("button", { name: "Try again" });
  await expect(tryAgain).not.toHaveAttribute("aria-busy", "true");
  const second = page.waitForResponse((r) => isConfirm(r.request()));
  await tryAgain.click();
  const res = await second;
  expect(res.headers()["idempotency-replayed"]).toBe("true");
  const ok = (await res.json()) as { status: string; docRef: { docId: string } };
  expect([ok.status, ok.docRef.docId]).toEqual(["succeeded", before.target!.docId]);
  await expect(dialog.getByTestId("docs-success")).toHaveText("Saved to a Demo Doc.");
  expect(confirms).toHaveLength(2);
  expect(confirms[1]!.headers()["idempotency-key"]).toBe(confirms[0]!.headers()["idempotency-key"]);
  expect(confirms[1]!.postData()).toBe(confirms[0]!.postData());
  const state = (await page.request.get(`${ORIGIN}/api/v1/sessions/${lecture.id}/docs-export`).then((r) => r.json())) as { target: { docId: string; version: number } };
  expect([state.target.docId, state.target.version]).toEqual([ok.docRef.docId, 1]);
  lecture.docId = ok.docRef.docId;
  await page.unroute(/\/api\/v1\/sessions\/[^/]+\/docs-export$/);
});

test("F15: the Demo Doc page passes axe (contrast included) in all five themes, light and dark, with no sideways scroll", async ({ browser }) => {
  test.setTimeout(180_000);
  expect(lecture.docId).not.toBe("");
  for (const skin of ["paper", "portal", "gamer", "calm", "glass"] as const) {
    for (const theme of ["light", "dark"] as const) {
      const context = await browser.newContext({ storageState: stateFor("docsExport"), colorScheme: theme, viewport: { width: 1280, height: 900 } });
      const page = await context.newPage();
      await page.addInitScript((v) => window.localStorage.setItem("tether.prefs.v1", JSON.stringify(v)), { skin, theme });
      await page.goto(`/demo-docs/${lecture.docId}?lecture=${lecture.id}`);
      await expect(page.getByTestId("demo-doc")).toBeVisible();
      await settle(page, { skin, theme });
      await scan(page, `demo-doc-${skin}-${theme}`);
      for (const b of await page.getByRole("button").all()) expect((await b.boundingBox())!.height, `${skin}-${theme} 44px`).toBeGreaterThanOrEqual(44);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
      await context.close();
    }
  }
});

test("F23: another student cannot open this Demo Doc; it is the same calm not-found", async ({ browser }) => {
  expect(lecture.docId).not.toBe("");
  const other = await browser.newContext({ storageState: stateFor("docsOther") });
  const page = await other.newPage();
  await page.goto(`/demo-docs/${lecture.docId}`);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("We can't find that Demo Doc");
  await expect(page.getByText("Demo replay segment")).toHaveCount(0);
  expect((await page.request.get(`${ORIGIN}/api/v1/demo-docs/${lecture.docId}`)).status()).toBe(404);
  // A malformed id never reaches the API.
  const calls: string[] = [];
  page.on("request", (r) => {
    if (r.url().includes("/api/v1/demo-docs/")) calls.push(r.url());
  });
  await page.goto("/demo-docs/ddc_%2e%2e");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("We can't find that Demo Doc");
  expect(calls).toEqual([]);
  await other.close();
});

test("F22: deleting the lecture deletes its Demo Doc", async ({ page }) => {
  expect(lecture.docId).not.toBe("");
  const res = await page.request.delete(`${ORIGIN}/api/v1/sessions/${lecture.id}`, { headers: { Origin: ORIGIN } });
  expect(res.status()).toBe(204);
  await page.goto(`/demo-docs/${lecture.docId}?lecture=${lecture.id}`);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("We can't find that Demo Doc");
  expect((await page.request.get(`${ORIGIN}/api/v1/demo-docs/${lecture.docId}`)).status()).toBe(404);
});
