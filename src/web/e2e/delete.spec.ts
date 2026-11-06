import { test, expect, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync } from "node:fs";
import path from "node:path";
import { settle } from "../e2e-screens/settle";
import { uid } from "./helpers";
import { stateFor } from "./users";

// PO-20by (docs/ux/delete-wording-draft.md 5.1, 5.2a, 6): "Delete this lecture" and "Delete this course" against the REAL
// stack (Next dev on :3000 proxying the API on :5080). Lectures are started over the API with the draft consent
// attestation and no audio, microphone or AI. Run serially (--workers=1). User: deleter (its own courses only).

const SHOT_DIR = process.env.SHOT_DIR ?? path.join(__dirname, "..", "test-results", "delete-shots");
mkdirSync(SHOT_DIR, { recursive: true });
const ORIGIN = { Origin: "http://localhost:3000" };

test.use({ storageState: stateFor("deleter") });

async function scan(page: Page, label: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa", "best-practice"]).analyze();
  expect(r.violations.map((v) => `${label}: ${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`)).toEqual([]);
}

async function newCourse(page: Page, name: string) {
  const res = await page.request.post("/api/v1/courses", { headers: ORIGIN, data: { name } });
  expect(res.status()).toBe(201);
  return ((await res.json()) as { id: string }).id;
}

async function newLecture(page: Page, courseId: string, title: string) {
  const res = await page.request.post(`/api/v1/courses/${courseId}/sessions`, {
    headers: { ...ORIGIN, "Idempotency-Key": `del-${uid()}` },
    data: { title, consent: { acknowledged: true, textVersion: "draft-2" } },
  });
  expect(res.status(), await res.text()).toBe(201);
  return ((await res.json()) as { id: string }).id;
}

const stop = async (page: Page, sid: string) => expect((await page.request.post(`/api/v1/sessions/${sid}/stop`, { headers: ORIGIN })).status()).toBeLessThan(300);

/** The dialog's buttons and the opener are 44px targets, and at 390 the dialog fits the viewport (measured after settle). */
async function geometry(page: Page, label: string) {
  for (const [w, h] of [
    [1280, 900],
    [390, 844],
  ] as const) {
    await page.setViewportSize({ width: w, height: h });
    await settle(page);
    const dialog = page.getByRole("dialog");
    const d = (await dialog.boundingBox())!;
    expect(d.x, `${label}@${w} dialog left`).toBeGreaterThanOrEqual(0);
    expect(d.x + d.width, `${label}@${w} dialog right`).toBeLessThanOrEqual(w);
    expect(d.y + d.height, `${label}@${w} dialog bottom`).toBeLessThanOrEqual(h);
    for (const b of await dialog.getByRole("button").all()) expect((await b.boundingBox())!.height, `${label}@${w} dialog button`).toBeGreaterThanOrEqual(44);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `${label}@${w} no horizontal scroll`).toBe(true);
    await scan(page, `${label}@${w}`);
    await page.screenshot({ path: path.join(SHOT_DIR, `${label}-${w}.png`) });
  }
  await page.setViewportSize({ width: 1280, height: 900 });
  await settle(page);
}

test("Delete this lecture: Keep it keeps; Delete lecture deletes, lands on the course page with a note, and the lecture is gone", async ({ page }) => {
  const name = `Delete lecture ${uid()}`;
  const courseId = await newCourse(page, name);
  const title = `Week 3 ${uid()}`;
  const sid = await newLecture(page, courseId, title);
  await stop(page, sid);

  await page.goto(`/lectures/${sid}`);
  await expect(page.getByTestId("state-label")).toHaveText("Stopped");
  await settle(page); // L-HYDRATE: before the first action
  const opener = page.getByRole("region", { name: "Review this lecture" }).getByRole("button", { name: "Delete this lecture" });
  await expect(opener).toBeVisible();
  expect((await opener.boundingBox())!.height).toBeGreaterThanOrEqual(44);

  // Keep it: focused on open, nothing is sent, focus returns to the opener.
  let deletes = 0;
  page.on("request", (r) => {
    if (r.method() === "DELETE" && r.url().includes(`/api/v1/sessions/${sid}`)) deletes++;
  });
  await opener.click();
  const dialog = page.getByRole("dialog", { name: "Delete this lecture?" });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Keep it" })).toBeFocused();
  await expect(dialog).toContainText("This deletes the transcript, the summary, any recaps and the notes.");
  await expect(dialog).toContainText("It can't be undone.");
  await expect(dialog).toContainText("None of the words stay.");
  await geometry(page, "lecture-dialog");
  await page.keyboard.press("Escape");
  await expect(dialog).toBeHidden();
  await expect(opener).toBeFocused();
  expect(deletes).toBe(0);

  await opener.click();
  await expect(dialog.getByRole("button", { name: "Keep it" })).toBeFocused();
  await dialog.getByRole("button", { name: "Delete lecture" }).click();
  await expect(page).toHaveURL(new RegExp(`/courses/${courseId}$`));
  const h1 = page.getByRole("heading", { level: 1, name });
  await expect(h1).toBeVisible();
  await settle(page);
  await expect(page.getByText(`Deleted: ${title}.`)).toBeVisible();
  await expect(h1).toBeFocused();
  expect(deletes).toBe(1);
  await scan(page, "course-after-lecture-delete");

  expect((await page.request.get(`/api/v1/sessions/${sid}`)).status()).toBe(404);
  await page.getByRole("button", { name: "Dismiss" }).click();
  await expect(page.getByText(`Deleted: ${title}.`)).toBeHidden();
  await expect(h1).toBeFocused();

  // A reload does not bring the note back (one-shot).
  await page.reload();
  await expect(h1).toBeVisible();
  await settle(page);
  await expect(page.getByText(`Deleted: ${title}.`)).toHaveCount(0);
});

test("Delete this course: hidden while a lecture is recording; once stopped, the dialog counts what goes, and the course is deleted", async ({ page }) => {
  const name = `Delete course ${uid()}`;
  const courseId = await newCourse(page, name);
  const sid = await newLecture(page, courseId, `Live ${uid()}`); // Active (no audio is sent; it may auto-pause, which is still live)

  await page.goto(`/courses/${courseId}`);
  await expect(page.getByRole("heading", { level: 1, name })).toBeVisible();
  await settle(page);
  const section = page.getByRole("region", { name: "Your data" });
  await expect(section.getByText("A lecture is recording. Stop it first.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Delete this course" })).toHaveCount(0);

  await stop(page, sid);
  await page.reload();
  await expect(page.getByRole("heading", { level: 1, name })).toBeVisible();
  await settle(page);
  const opener = section.getByRole("button", { name: "Delete this course" });
  await expect(opener).toBeVisible();
  await expect(opener).not.toHaveAttribute("aria-busy", "true");
  expect((await opener.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  await opener.click();
  const dialog = page.getByRole("dialog", { name: "Delete this course?" });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Keep it" })).toBeFocused();
  await expect(dialog.getByRole("listitem")).toHaveText(["1 lecture: transcript, summary, recaps and notes"]);
  await expect(dialog).toContainText("Tether keeps a record that these lectures and syllabi existed, when, and how much AI they used.");
  await expect(dialog.getByRole("button", { name: /hide/i })).toHaveCount(0);
  await geometry(page, "course-dialog");

  const confirm = dialog.getByRole("button", { name: "Delete course" });
  await expect(confirm).not.toHaveAttribute("aria-busy", "true");
  await confirm.click();
  await expect(page).toHaveURL(/\/$/);
  const h1 = page.getByRole("heading", { level: 1 });
  await expect(h1).toBeVisible();
  await settle(page);
  await expect(page.getByText(`Deleted: ${name}.`)).toBeVisible();
  await expect(h1).toBeFocused();
  await scan(page, "dashboard-after-course-delete");

  expect((await page.request.get(`/api/v1/courses/${courseId}`)).status()).toBe(404);
  expect((await page.request.get(`/api/v1/sessions/${sid}`)).status()).toBe(404);
  const list = (await (await page.request.get("/api/v1/courses?includeArchived=true")).json()) as { items: { id: string }[] };
  expect(list.items.map((c) => c.id)).not.toContain(courseId); // deleted, not hidden
});
