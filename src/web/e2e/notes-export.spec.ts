import { test, expect, type APIRequestContext, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { readFileSync } from "node:fs";
import { settle } from "../e2e-screens/settle";
import { uid } from "./helpers";
import { stateFor } from "./users";

// Finish-backlog item 2 (PO-20cc): Export notes / Copy notes on a finished lecture's Ready notes, against the REAL stack
// (Next dev on :3000 proxying the API on :5080): the backend's deterministic ReplaySttProvider for the transcript and its
// MOCK AI for the notes and the Final summary. The fake Chromium microphone only paces the replay; no real audio, no
// provider, no Google. Export and Copy use only what the page already has: the spec asserts no request is made by either.
// User: notesExport (its own; a user may have only one pending notes press). Run serially (--workers=1).
test.describe.configure({ mode: "serial" });
test.use({ storageState: stateFor("notesExport") });

const ORIGIN = "http://localhost:3000";
const DEMO_LINE_1 = "Demo replay segment one";
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

const course = { id: "", name: `Export e2e ${uid()}` };
test.beforeAll(async ({ playwright }) => {
  const ctx = await playwright.request.newContext({ baseURL: ORIGIN, storageState: stateFor("notesExport"), extraHTTPHeaders: { Origin: ORIGIN } });
  const res = await postWithRetry(ctx, "/api/v1/courses", { name: course.name });
  expect(res.status()).toBe(201);
  course.id = ((await res.json()) as { id: string }).id;
  await ctx.dispose();
});

const day = (iso: string, timeZone: string) => {
  const p = new Intl.DateTimeFormat("en-US", { timeZone, year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(new Date(iso));
  const g = (t: string) => p.find((x) => x.type === t)!.value;
  return `${g("year")}-${g("month")}-${g("day")}`;
};
const longDay = (iso: string, timeZone: string) => new Intl.DateTimeFormat("en-US", { timeZone, year: "numeric", month: "long", day: "numeric" }).format(new Date(iso));

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
  await expect(page.locator("#lecture-tldr").getByTestId("tldr-final")).toHaveText("Final summary", { timeout: 30_000 });
  await settle(page);
}

async function scan(page: Page, label: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa", "best-practice"]).analyze();
  expect(r.violations.map((v) => `${label}: ${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`)).toEqual([]);
}

test("Export notes and Copy notes: a Markdown file with the course, date, Final summary and cited notes; no request; announced; UTC day", async ({ page, browser }) => {
  test.setTimeout(240_000);
  const sid = await finishedDemoLecture(page);
  const notes = page.locator("#lecture-notes");
  await expect(notes.getByRole("heading", { level: 2, name: "Notes" })).toBeVisible();
  await settle(page);
  const make = page.getByRole("button", { name: "Make notes" });
  await expect(make).not.toHaveAttribute("aria-busy", "true");
  await make.click();
  await readyNotes(page);

  const exportBtn = notes.getByRole("button", { name: "Export notes" });
  const copyBtn = notes.getByRole("button", { name: "Copy notes" });
  for (const b of [exportBtn, copyBtn]) {
    await expect(b).toBeVisible();
    const box = (await b.boundingBox())!;
    expect(box.height, "44px target").toBeGreaterThanOrEqual(44);
    expect(box.width, "44px target").toBeGreaterThanOrEqual(44);
  }
  await expect(page.locator("#notes-export-help")).toHaveText(
    "Export notes saves them as a Markdown file on this device, with their times. Copy notes puts the same text on your clipboard. Nothing is sent anywhere.",
  );

  // What the page shows, to compare with the file: the bullet texts and their citation times, in order.
  const shown = await notes.locator("[data-notes-bullet]").evaluateAll((lis) =>
    lis.map((li) => ({
      text: (li.querySelector("span.max-w-prose")?.firstChild?.textContent ?? "").trim(),
      times: Array.from(li.querySelectorAll(".cite")).map((c) => c.textContent!.trim()),
    })),
  );
  expect(shown.length).toBeGreaterThan(0);
  const transcriptTimes = new Set(await page.locator(".transcript-time > [aria-hidden=true]").allTextContents().then((a) => a.map((t) => t.trim())));
  const session = (await (await page.request.get(`${ORIGIN}/api/v1/sessions/${sid}`)).json()) as { startedAt: string; title: string | null };
  expect(session.title).toBeNull(); // the live route starts lectures without a title

  // From here on the page must not call anything: export and copy are local.
  const calls: string[] = [];
  page.on("request", (r) => {
    if (r.url().includes("/api/") || r.url().includes("/hubs/")) calls.push(`${r.method()} ${r.url()}`);
  });

  // Export notes from the keyboard.
  await exportBtn.focus();
  const dl = page.waitForEvent("download");
  await page.keyboard.press("Enter");
  const download = await dl;
  expect(download.suggestedFilename()).toBe(`lecture notes ${day(session.startedAt, "America/New_York")}.md`);
  const text = readFileSync((await download.path())!, "utf8");
  console.log(`[notes-export e2e] ${download.suggestedFilename()}, ${text.length} chars, ${shown.length} bullets`);
  const lines = text.split("\n");
  expect(lines[0]).toBe("# Lecture notes");
  expect(lines).toContain(`- Course: ${course.name}`);
  expect(lines).toContain(`- Lecture date: ${longDay(session.startedAt, "America/New_York")}`);
  expect(text).toContain("Demo notes: Made by a test routine, not an AI model.");
  expect(text).toContain("Nothing was sent anywhere.");
  expect(text).toMatch(/\n## Final summary\n\n\S/);
  expect(text).not.toContain("The final summary could not be loaded");
  expect(text).toContain("\n## Notes\n");
  // Every bullet on screen is in the file with the same times, and every time is a transcript line's time.
  for (const b of shown) {
    expect(lines, b.text).toContain(`- ${b.text.replace(/([\\`*_[\]<>|~#])/g, "\\$1")} (${b.times.join(", ")})`);
    for (const t of b.times) expect(transcriptTimes.has(t), `citation ${t} is a transcript time`).toBe(true);
  }
  expect(text).not.toMatch(/seg_|nt_|ses_/); // no ids
  await expect(page.locator('[data-region="status"]')).toHaveText("Your notes file is ready.");
  await expect(notes.getByTestId("notes-export-result")).toHaveText("Your notes file is ready.");
  await expect(exportBtn).toBeFocused();

  // Copy notes: the same text on the clipboard.
  await page.context().grantPermissions(["clipboard-read", "clipboard-write"], { origin: ORIGIN });
  await copyBtn.click();
  await expect(page.locator('[data-region="status"]')).toHaveText("Notes copied. You can paste them where you like.");
  // The Windows clipboard hands text back with CRLF line ends; the text written was the file's LF text.
  expect((await page.evaluate(() => navigator.clipboard.readText())).replace(/\r\n/g, "\n")).toBe(text);
  await expect(copyBtn).toBeFocused();
  await page.waitForTimeout(1000);
  expect(calls, "export and copy made no request").toEqual([]);

  await scan(page, "notes-export-1280");
  await page.setViewportSize({ width: 390, height: 844 });
  await settle(page);
  for (const b of [exportBtn, copyBtn]) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), "no horizontal scroll at 390").toBe(true);
  await scan(page, "notes-export-390");

  // A UTC browser (L-UTC): the file name and date line use the browser's own day.
  const utc = await browser.newContext({ storageState: stateFor("notesExport"), timezoneId: "UTC" });
  const up = await utc.newPage();
  await up.goto(`/lectures/${sid}`);
  await expect(up.getByTestId("state-label")).toHaveText("Stopped");
  await readyNotes(up);
  const dl2 = up.waitForEvent("download");
  await up.locator("#lecture-notes").getByRole("button", { name: "Export notes" }).click();
  const d2 = await dl2;
  expect(d2.suggestedFilename()).toBe(`lecture notes ${day(session.startedAt, "UTC")}.md`);
  expect(readFileSync((await d2.path())!, "utf8")).toContain(`- Lecture date: ${longDay(session.startedAt, "UTC")}`);
  await utc.close();
});
