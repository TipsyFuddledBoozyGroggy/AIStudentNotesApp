import { test, expect, type Page, type Request } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync } from "node:fs";
import path from "node:path";
import { settle } from "../e2e-screens/settle";
import { createCourseViaUI, uid } from "./helpers";
import { stateFor } from "./users";

// PO-20b / SBV-05 "Dates we noticed" (docs/ux/unclaimed-dates-list.md) against the REAL stack: Next dev on :3000 proxying
// the ASP.NET Core API on :5080, mock rule extractor. Synthetic text only. Lines that carry a date but no item keyword
// become unclaimed dates (MockRuleExtractor: kind null -> AddUnclaimed); 201 such lines fill the 200 cap and the one over
// it is counted in droppedInvalid, so the dropped-dates Note shows "See dates we noticed". Covers the flow (open, density
// at 200, add as item with a typed date, approve it, hide, reload, show hidden) plus axe and phone-width overflow.
// Run serially (--workers=1). User: sylOther (one syllabus create; it is otherwise only used for read-only checks).

const SHOT_DIR = process.env.SHOT_DIR ?? path.join(__dirname, "..", "test-results", "syllabus-shots");
mkdirSync(SHOT_DIR, { recursive: true });

test.use({ storageState: stateFor("sylOther") });

async function scan(page: Page, label: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa", "best-practice"]).analyze();
  expect(r.violations.map((v) => `${label}: ${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`)).toEqual([]);
}

/** axe + screenshot at 1280 and 390 (no horizontal scroll at 390), then back to 1280. */
async function both(page: Page, name: string) {
  for (const [w, h] of [
    [1280, 900],
    [390, 844],
  ] as const) {
    await page.setViewportSize({ width: w, height: h });
    // The resize reflows the page: measure (overflow, axe target-size) only once it has settled (settle.ts).
    await settle(page);
    if (w === 390) expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `${name}: no horizontal scroll at 390px`).toBe(true);
    await scan(page, `${name}@${w}`);
    await page.screenshot({ path: path.join(SHOT_DIR, `${name}-${w}.png`), fullPage: true });
  }
  await page.setViewportSize({ width: 1280, height: 900 });
  await settle(page); // the 1280 geometry check below reads boxes right after this resize
}

const MONTHS = ["Sep", "Oct", "Nov"];
/** Line k (1-based) of the noticed part: no item keyword, one date. Line 1 is the one the student adds as an item. */
const noticedLine = (k: number) => (k === 1 ? "Bring a calculator to the studio on Oct 5." : `Bring handout ${k} to the studio on ${MONTHS[k % 3]} ${(k % 28) + 1}.`);
const SYLLABUS = [
  "Art 210 syllabus (synthetic test text)",
  "Quiz 1 on September 14, 2026 in class",
  "Problem set 1 due Friday, September 18, 2026, 11:59 PM",
  ...Array.from({ length: 201 }, (_, i) => noticedLine(i + 1)),
].join("\n");

test.describe.configure({ mode: "serial" });

test("dates we noticed: 200 at the cap through the real API; see, page, hide, add as item, approve, reload, show hidden", async ({ page }) => {
  test.setTimeout(240_000);
  const reqs: Request[] = [];
  const google: string[] = [];
  page.on("request", (r) => {
    if (r.url().includes("/api/v1/")) reqs.push(r);
    if (/google/i.test(new URL(r.url()).hostname)) google.push(r.url());
  });
  const find = (method: string, re: RegExp) => reqs.filter((r) => r.method() === method && re.test(new URL(r.url()).pathname));

  const courseId = await createCourseViaUI(page, `Noticed ${uid()}`);
  await page.goto(`/courses/${courseId}/syllabus`);
  await expect(page.getByRole("heading", { level: 1, name: "Add your syllabus" })).toBeFocused();
  await page.getByLabel("Syllabus text").fill(SYLLABUS);
  await page.getByRole("checkbox", { name: "I read the notice above. (Draft placeholder wording.)" }).check();
  await page.getByRole("button", { name: "Read my syllabus" }).click();
  await page.getByRole("button", { name: "Review items" }).click({ timeout: 60_000 });
  await expect(page.getByRole("heading", { level: 1, name: "Review your syllabus items" })).toBeFocused();
  await expect(page.getByText(/^We found 2 items\./)).toBeVisible();

  // Collapsed by default, count in the head, nothing fetched until opened (spec 4.2, 4.3).
  const section = page.locator("[data-unclaimed-section]");
  const head = section.getByRole("button", { name: /^Dates we noticed, / });
  await expect(head).toHaveAccessibleName("Dates we noticed, 200 dates");
  await expect(head).toHaveAttribute("aria-expanded", "false");
  expect(find("GET", /\/unclaimed-dates$/)).toHaveLength(0);
  expect(await page.evaluate(() => document.querySelector(".review-groups")?.lastElementChild?.hasAttribute("data-unclaimed-section"))).toBe(true);

  // The 201st line went over the cap: the dropped-dates Note, with its one action (spec 4.9).
  const note = page.locator("[data-dropped-notice]");
  await expect(note).toBeVisible();
  await both(page, "u1-review-collapsed");
  await note.getByRole("button", { name: "See dates we noticed" }).click();
  await expect(head).toBeFocused();
  await expect(head).toHaveAttribute("aria-expanded", "true");
  const rows = page.locator("li[data-unclaimed-id]");
  await expect(rows).toHaveCount(25);
  const gets = find("GET", /\/unclaimed-dates$/);
  expect(gets).toHaveLength(1);
  expect(new URL(gets[0].url()).searchParams.get("limit")).toBe("200");
  const first = rows.first();
  await expect(first.locator("blockquote")).toHaveText("Bring a calculator to the studio on Oct 5.");
  await expect(first.locator("blockquote strong")).toHaveText("Oct 5");
  await both(page, "u2-noticed-open-25");

  // Geometry at 1280 (checklist 17): the section is in the groups column; no row box meets the rail.
  const geo = await page.evaluate(() => {
    const groups = document.querySelector(".review-groups")!.getBoundingClientRect();
    const sec = document.querySelector("[data-unclaimed-section]")!.getBoundingClientRect();
    const rail = document.querySelector(".review-rail")!.getBoundingClientRect();
    const hits = Array.from(document.querySelectorAll("li[data-unclaimed-id]")).filter((li) => li.getBoundingClientRect().right > rail.left).length;
    return { groupsLeft: groups.left, secLeft: sec.left, hits };
  });
  expect(geo.secLeft).toBe(geo.groupsLeft);
  expect(geo.hits).toBe(0);

  // Density: seven presses show all 200; the last moves focus to row 176 (spec 4.12).
  for (let press = 1; press <= 7; press++) {
    await section.getByRole("button", { name: "Show 25 more" }).click();
    await expect(rows).toHaveCount(25 + press * 25);
  }
  await expect(section.getByRole("button", { name: /^Show \d+ more$/ })).toHaveCount(0);
  await expect(rows.nth(175).getByRole("button", { name: /^Add as item: / })).toBeFocused();
  await page.screenshot({ path: path.join(SHOT_DIR, "u3-noticed-200-1280.png"), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await settle(page);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), "200 rows: no horizontal scroll at 390px").toBe(true);
  await page.setViewportSize({ width: 1280, height: 900 });
  await settle(page);

  // Hide row 2: the same control becomes Undo and keeps focus; the head count drops (spec 4.5).
  const second = rows.nth(1);
  const hide = second.getByRole("button", { name: /^Hide: / });
  await hide.click();
  const undo = second.getByRole("button", { name: /^Undo hiding / });
  await expect(undo).toBeFocused();
  await expect(second.getByText("Hidden:")).toBeVisible();
  await expect(head).toHaveAccessibleName("Dates we noticed, 199 dates");

  // Add row 1 as an item: prefilled title, Other, a typed date; one POST with exactly the create members (spec 4.10).
  await first.getByRole("button", { name: "Add as item: Oct 5" }).click();
  const form = first.getByRole("form", { name: "Add an item from: Oct 5" });
  await expect(form.getByLabel("Title")).toBeFocused();
  await expect(form.getByLabel("Title")).toHaveValue("Bring a calculator to the studio on Oct 5.");
  await expect(form.getByLabel("Type")).toHaveValue("other");
  await expect(form.getByLabel("Date", { exact: true })).toHaveValue("");
  await form.getByLabel("Date", { exact: true }).fill("2026-10-05");
  await both(page, "u4-add-form-open");
  await form.getByRole("button", { name: "Add item" }).click();
  const goTo = first.getByRole("button", { name: "Go to the item: Bring a calculator to the studio on Oct 5." });
  await expect(goTo).toBeFocused();
  const post = find("POST", /\/syllabi\/[^/]+\/items$/);
  expect(post).toHaveLength(1);
  expect(post[0].headers()["idempotency-key"]).toMatch(/^[A-Za-z0-9_-]{8,128}$/);
  expect(post[0].postDataJSON()).toEqual({ kind: "other", title: "Bring a calculator to the studio on Oct 5.", date: "2026-10-05", allDay: true, time: null });
  expect((await post[0].response())?.status()).toBe(201);
  await expect(page.getByText(/^We found 3 items\. 0 approved, 0 skipped, 3 not decided\.$/)).toBeVisible();
  await expect(head).toHaveAccessibleName("Dates we noticed, 198 dates");

  // Go to the item, then approve it like any other (a student item is approved one by one).
  await goTo.click();
  const item = page.getByRole("listitem", { name: "Bring a calculator to the studio on Oct 5." });
  const approve = item.getByRole("button", { name: "Approve Bring a calculator to the studio on Oct 5." });
  await expect(approve).toBeFocused();
  await expect(item.getByText("Not decided", { exact: true })).toBeVisible();
  await approve.click();
  await expect(item.getByRole("button", { name: /^Undo approval of Bring a calculator/ })).toBeFocused();
  await both(page, "u5-added-and-approved");

  // Reload: hidden and added lines stay out of the list on this device; one press brings them back (spec 4.7).
  await page.reload();
  await expect(page.getByRole("heading", { level: 1, name: "Review your syllabus items" })).toBeVisible();
  await expect(head).toHaveAccessibleName("Dates we noticed, 198 dates");
  await expect(head).toHaveAttribute("aria-expanded", "false");
  await head.click();
  await expect(rows).toHaveCount(25);
  const showHandled = section.getByRole("button", { name: "Show hidden and added dates (2)" });
  await expect(showHandled).toBeVisible();
  await showHandled.click();
  await expect(showHandled).toHaveCount(0);
  await expect(rows).toHaveCount(27);
  await expect(rows.nth(25).getByRole("button", { name: /^Go to the item: Bring a calculator/ })).toBeFocused();
  await expect(rows.nth(26).getByRole("button", { name: /^Undo hiding / })).toBeVisible();
  await both(page, "u6-reload-show-hidden");

  expect(google).toEqual([]);
});
