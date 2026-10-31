import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";

// Slice 8a (PO-18d option B; docs/ux/make-notes-rail-demo-notice.md 1.9): the Notes block after Stop on the dev-only harness
// (fake AI stand-in with source "mock", no API, synthetic data). Geometry, focus, the status region and axe in a real browser.

const NOTES_FIXTURES = ["stopped", "stopped-notes-pending", "stopped-notes-ready", "stopped-notes-ready-truncated", "stopped-notes-unavailable", "stopped-notes-not-needed"];

async function open(page: Page, id: string) {
  await page.goto(`/dev/lecture-harness/${id}`);
  await expect(page.getByText(/^Fixture:/)).toBeVisible();
  await expect(page.locator("#lecture-notes")).toBeVisible();
}

const statusText = (page: Page) => page.locator('[data-region="status"]').textContent();

test("1280: press Make notes once: busy, then Pending with focus on Notes and one status line; Ready arrives from the poll without moving focus or scroll", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await open(page, "stopped");
  const column = page.locator("#lecture-notes").locator("xpath=../..");
  await expect(column.locator(".btn-primary")).toHaveCount(1);
  const btn = page.getByRole("button", { name: "Make notes" });
  await expect(btn).toHaveAccessibleDescription(/\(Draft wording\.\)$/);
  const w0 = (await btn.boundingBox())!.width;
  await btn.click();
  await expect(btn).toHaveAttribute("aria-busy", "true");
  expect((await btn.boundingBox())!.width).toBe(w0);
  await expect(page.getByTestId("notes-pending")).toBeVisible();
  await expect(page.getByRole("button", { name: "Make notes" })).toHaveCount(0);
  await expect(page.locator("#notes-h2")).toBeFocused();
  await expect.poll(() => statusText(page)).toBe("Writing notes. You can leave and come back.");
  const y = await page.evaluate(() => window.scrollY);
  await expect(page.locator("#lecture-notes.lecture-sheet")).toBeVisible({ timeout: 8000 }); // the 3 s poll
  await expect(page.locator("#notes-h2")).toBeFocused(); // unchanged by the arrival
  expect(await page.evaluate(() => window.scrollY)).toBe(y);
  await expect.poll(() => statusText(page)).toBe("Notes ready.");
  await expect(page.getByTestId("demo-notes-tag")).toContainText("Demo notes");
  await expect(page.getByRole("button", { name: "Make notes" })).toHaveCount(0);
  // The TL;DR row now shows the Final revision the press made, captioned.
  await expect(page.locator("#lecture-tldr").getByTestId("tldr-final")).toHaveText("Final summary");
});

test("1280 Paper: the Ready sheet shares the transcript sheet's edges; heading on the text edge; lead citations hang in the margin; 44px citation targets", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await open(page, "stopped-notes-ready");
  const notes = (await page.locator("#lecture-notes").boundingBox())!;
  const sheet = (await page.locator(".transcript-panel").boundingBox())!;
  expect(Math.abs(notes.x - sheet.x)).toBeLessThanOrEqual(1);
  expect(Math.abs(notes.width - sheet.width)).toBeLessThanOrEqual(1);
  // The painted surface starts on the transcript track in both sheets (::before inset = --sheet-start).
  const start = await page.evaluate(() => {
    const read = (el: Element) => getComputedStyle(el, "::before").left;
    return [read(document.getElementById("lecture-notes")!), read(document.querySelector(".transcript-panel")!)];
  });
  expect(start[0]).toBe(start[1]);
  const h2 = (await page.locator("#notes-h2").boundingBox())!;
  const th = (await page.getByRole("heading", { level: 2, name: "Transcript" }).boundingBox())!;
  expect(Math.abs(h2.x - th.x)).toBeLessThanOrEqual(1);
  const lead = (await page.locator("[data-notes-bullet] .cite").first().boundingBox())!;
  const time = (await page.locator(".transcript-time").first().boundingBox())!;
  expect(Math.abs(lead.x - time.x)).toBeLessThanOrEqual(1); // in the margin track, like the transcript times
  expect(lead.x + lead.width).toBeLessThan(h2.x);
  for (const c of await page.locator("#lecture-notes .cite").all()) expect((await c.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  // Serif headings, verbatim-face bullets (PO-17a).
  const faces = await page.evaluate(() => ({
    h3: getComputedStyle(document.querySelector("#lecture-notes h3")!).fontFamily,
    li: getComputedStyle(document.querySelector("#lecture-notes [data-notes-bullet] span.max-w-prose")!).fontFamily,
    liSize: getComputedStyle(document.querySelector("#lecture-notes [data-notes-bullet] span.max-w-prose")!).fontSize,
    h3Size: getComputedStyle(document.querySelector("#lecture-notes h3")!).fontSize,
  }));
  expect(faces.h3).not.toBe(faces.li);
  expect(faces.liSize).toBe("17px");
  expect(faces.h3Size).toBe("24px");
  // A cited bullet focuses its transcript line.
  await page.getByRole("button", { name: "00:09, go to transcript line" }).click();
  await expect(page.locator('[data-segment-id="seg_3"]')).toBeFocused();
});

test("390: the Ready sheet is square and full-bleed like the transcript sheet; the entry block sits on the page; no overflow at 320", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await open(page, "stopped-notes-ready");
  const notes = (await page.locator("#lecture-notes").boundingBox())!;
  const sheet = (await page.locator(".transcript-panel").boundingBox())!;
  expect(Math.abs(notes.x - sheet.x)).toBeLessThanOrEqual(1);
  expect(Math.abs(notes.width - sheet.width)).toBeLessThanOrEqual(1);
  expect(await page.locator("#lecture-notes").evaluate((el) => getComputedStyle(el).borderTopLeftRadius)).toBe("0px");
  for (const id of NOTES_FIXTURES) {
    await page.setViewportSize({ width: 320, height: 640 });
    await open(page, id);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect([id, overflow <= 0]).toEqual([id, true]); // negative = a classic scrollbar, never sideways scroll
  }
});

test("exactly three live regions with Ready notes (finals is off until opted in); no live region inside the Notes block", async ({ page }) => {
  await open(page, "stopped-notes-ready");
  const regions = await page.evaluate(() => ({
    all: Array.from(document.querySelectorAll("[data-region]")).map((e) => e.getAttribute("data-region")),
    live: Array.from(document.querySelectorAll('[role="status"],[role="alert"],[role="log"],[aria-live]'))
      .filter((e) => (e.getAttribute("aria-live") ?? (e.getAttribute("role") === "log" ? "off" : "polite")) !== "off")
      .map((e) => e.getAttribute("data-region")),
  }));
  expect(regions.all).toEqual(["status", "alert", "finals"]);
  expect(regions.live).toEqual(["status", "alert"]);
  await expect(page.locator('#lecture-notes [aria-live], #lecture-notes [role="status"], #lecture-notes [role="alert"]')).toHaveCount(0);
});

for (const id of NOTES_FIXTURES) {
  for (const width of [390, 1280]) {
    test(`axe (WCAG 2.2 AA + best practice, incl. colour contrast): ${id} at ${width}`, async ({ page }) => {
      await page.setViewportSize({ width, height: width < 600 ? 844 : 900 });
      await open(page, id);
      await page.waitForTimeout(300);
      const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa", "best-practice"]).analyze();
      expect(r.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`)).toEqual([]);
    });
  }
}
