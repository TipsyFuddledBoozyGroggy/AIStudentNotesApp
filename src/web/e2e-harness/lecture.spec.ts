import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";

const FIXTURES = [
  "consent",
  "consent-attested",
  "consent-error",
  "start-flow",
  "start-flow-denied",
  "mic-denied",
  "live",
  "live-redteam",
  "paused",
  "paused-mic-lost",
  "paused-auto",
  "stopped",
  "stopped-auto",
  "disconnected",
  "mismatch",
];

const BASE = "http://localhost:3100";

for (const id of FIXTURES) {
  test(`axe (WCAG 2.2 AA tags, incl. colour contrast) has no violations: ${id}`, async ({ page }) => {
    await page.goto(`/dev/lecture-harness/${id}`);
    await expect(page.getByText(/^Fixture:/)).toBeVisible();
    await page.waitForTimeout(700);
    const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"]).analyze();
    expect(r.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.html).join(" | ")}`)).toEqual([]);
  });
}

for (const id of ["consent", "live", "paused", "disconnected", "mismatch"]) {
  test(`UX 2 AC6 / 1 AC6: no horizontal scroll at 320px: ${id}`, async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 640 });
    await page.goto(`/dev/lecture-harness/${id}`);
    await page.waitForTimeout(500);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
  });
}

test("UX 2 AC6: status bar is visible at 320px and not clipped", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 640 });
  await page.goto("/dev/lecture-harness/live");
  const bar = page.locator("[data-lecture-status-bar]");
  await expect(bar).toBeVisible();
  const box = await bar.boundingBox();
  expect(box!.x).toBeGreaterThanOrEqual(-1);
  expect(box!.x + box!.width).toBeLessThanOrEqual(321);
  await expect(page.getByRole("button", { name: "Pause", exact: true })).toBeInViewport();
});

test("UX 2 AC2: a partial replaced by its final does not move lines above it", async ({ page }) => {
  await page.goto("/dev/lecture-harness/live");
  const first = page.locator('[data-segment-id="seg_1"]');
  await expect(first).toBeVisible();
  const offsetTop = () => first.evaluate((el) => (el as HTMLElement).offsetTop);
  const step = page.getByRole("button", { name: /Step next event/ });
  await step.click(); // partial arrives
  await expect(page.locator("[data-partial-row]")).toHaveText("so now the chain");
  const before = await offsetTop();
  await step.click(); // final for the same segment replaces it
  await expect(page.locator('[data-segment-id="seg_4"]')).toBeVisible();
  expect(await offsetTop()).toBe(before);
});

test("UX 2 AC3: scrolling up stops autoscroll and shows Jump to live; the button resumes it", async ({ page }) => {
  await page.goto("/dev/lecture-harness/live");
  const log = page.getByRole("log", { name: "Transcript lines" });
  await log.evaluate((el) => {
    const p = document.createElement("p");
    p.style.height = "2000px"; // make the log scrollable regardless of fixture length
    el.insertBefore(p, el.firstChild);
    el.scrollTop = el.scrollHeight;
  });
  await page.waitForTimeout(150);
  await log.evaluate((el) => {
    el.scrollTop = 0; // a real scroll event, as when the student scrolls up
  });
  await expect(page.getByRole("button", { name: "Jump to live" })).toBeVisible();
  const top = await log.evaluate((el) => el.scrollTop);
  await page.getByRole("button", { name: /Step next event/ }).click();
  await page.getByRole("button", { name: /Step next event/ }).click();
  expect(await log.evaluate((el) => el.scrollTop)).toBe(top); // not dragged to the bottom
  await page.getByRole("button", { name: "Jump to live" }).click();
  await expect(page.getByRole("button", { name: "Jump to live" })).toBeHidden();
});

test("UX 2 AC5: incoming events do not move focus", async ({ page }) => {
  await page.goto("/dev/lecture-harness/live");
  const hold = page.getByRole("button", { name: "Pause updates" });
  await hold.focus();
  const step = page.getByRole("button", { name: /Step next event/ });
  // Stepping uses a button elsewhere, so check focus is preserved by dispatching the step via script.
  await step.evaluate((b) => (b as HTMLButtonElement).click());
  await step.evaluate((b) => (b as HTMLButtonElement).click());
  await expect(hold).toBeFocused();
});

test("UX 2 AC7: with the in-app Reduce motion toggle, no animation or transition runs", async ({ page }) => {
  await page.goto("/dev/lecture-harness/live");
  await page.getByText("Lecture settings").click();
  await page.getByLabel("Reduce motion").check();
  const bad = await page.evaluate(
    () =>
      Array.from(document.querySelectorAll("[data-lecture-root] *")).filter((e) => {
        const s = getComputedStyle(e);
        const anim = s.animationName !== "none" && s.animationDuration !== "0s";
        const trans = s.transitionProperty !== "none" && s.transitionProperty !== "all" ? s.transitionDuration !== "0s" : s.transitionDuration !== "0s";
        return anim || trans;
      }).length,
  );
  expect(bad).toBe(0);
});

test("UX 2 AC7: prefers-reduced-motion emulation leaves no running animation", async ({ browser }) => {
  const ctx = await browser.newContext({ reducedMotion: "reduce" });
  const page = await ctx.newPage();
  await page.goto(`${BASE}/dev/lecture-harness/live`);
  await page.waitForTimeout(500);
  expect(await page.evaluate(() => document.getAnimations().length)).toBe(0);
  await ctx.close();
});

test("UX 3 AC3: keyboard Pause then Resume moves focus to panel Resume, then to status-bar Pause", async ({ page }) => {
  await page.goto("/dev/lecture-harness/live");
  const pause = page.getByRole("button", { name: "Pause", exact: true });
  await pause.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("button", { name: "Resume recording" })).toBeFocused();
  await expect(page.getByRole("region", { name: "Recording is paused" })).toBeVisible();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("button", { name: "Pause", exact: true })).toBeFocused();
});

test("UX 3 AC7: Stop confirm traps focus and Escape returns focus to Stop", async ({ page }) => {
  await page.goto("/dev/lecture-harness/paused");
  const stop = page.getByRole("button", { name: "Stop lecture" });
  await stop.focus();
  await page.keyboard.press("Enter");
  const dlg = page.getByRole("dialog", { name: "Stop and save this lecture?" });
  await expect(dlg).toBeVisible();
  for (let i = 0; i < 5; i++) {
    await page.keyboard.press("Tab");
    expect(await dlg.evaluate((d) => d.contains(document.activeElement))).toBe(true);
  }
  await page.keyboard.press("Escape");
  await expect(stop).toBeFocused();
});

test("UX 3 AC1: Paused panel needs no scrolling at 320px", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 640 });
  await page.goto("/dev/lecture-harness/paused");
  await expect(page.getByRole("button", { name: "Resume recording" })).toBeInViewport();
  await expect(page.getByText("Recording is paused", { exact: true })).toBeInViewport();
});

test("UX 6 AC1: disconnected banner is visible and the label is never Recording (forced colors, 320px)", async ({ browser }) => {
  const ctx = await browser.newContext({ forcedColors: "active", viewport: { width: 320, height: 640 } });
  const page = await ctx.newPage();
  await page.goto(`${BASE}/dev/lecture-harness/disconnected`);
  await expect(page.getByText("Not connected. Audio is not being saved.").first()).toBeVisible();
  await expect(page.getByTestId("state-label")).toHaveText("Not recording");
  await ctx.close();
});

test("UX 1/5/8 permission spy: no getUserMedia call on consent, live, paused, blocked or mismatch fixtures", async ({ page }) => {
  await page.addInitScript(() => {
    const w = window as unknown as { __gum: number };
    w.__gum = 0;
    const md = navigator.mediaDevices;
    if (md) {
      md.getUserMedia = async () => {
        w.__gum++;
        throw new DOMException("x", "NotAllowedError");
      };
    }
  });
  for (const id of ["consent", "live", "paused", "mic-denied", "mismatch"]) {
    await page.goto(`/dev/lecture-harness/${id}`);
    await page.waitForTimeout(400);
    expect(await page.evaluate(() => (window as unknown as { __gum: number }).__gum)).toBe(0);
  }
});

test("harness is not linked from the production navigation", async ({ page }) => {
  await page.goto("/signin");
  expect(await page.locator("a[href*='lecture-harness']").count()).toBe(0);
});
