import { expect, request, test, type Browser, type Page } from "@playwright/test";
import { mkdirSync } from "node:fs";
import path from "node:path";

// Redesign-round2 slice 5 (= themes T2) in a real browser: the Preferences side panel and the Theme picker (applies at
// once, persists, no flash on reload, page stays visible at 1280, no sideways scroll at 320), and the Portal / Calm
// fallbacks by computed style under emulated media (forced colours, reduced motion, more contrast, reduced transparency)
// and under Low-stimulation. Gamer and Glass switches and their fallbacks are slice 7.

const ORIGIN = "http://localhost:3000";
const OUT = path.join(__dirname, "..", "test-results", "redesign-screens");
const RUN = Date.now().toString(36);
const STATE = path.join(OUT, `.auth-skins-${RUN}.json`);

test.beforeAll(async () => {
  mkdirSync(OUT, { recursive: true });
  const ctx = await request.newContext({ baseURL: ORIGIN, extraHTTPHeaders: { Origin: ORIGIN } });
  expect((await ctx.post("/api/v1/dev/login", { data: { email: `skins-${RUN}@dev.local` } })).status()).toBe(204);
  expect((await ctx.post("/api/v1/courses", { data: { name: "Biology 101", code: "BIO 101", term: "Fall 2026" } })).status()).toBe(201);
  await ctx.storageState({ path: STATE });
  await ctx.dispose();
});

async function open(browser: Browser, width: number, prefs: Record<string, unknown> | null, media: Parameters<Page["emulateMedia"]>[0] = {}) {
  const context = await browser.newContext({ viewport: { width, height: width < 600 ? 844 : 800 }, storageState: STATE });
  const page = await context.newPage();
  await page.emulateMedia(media);
  if (prefs) await page.addInitScript((p) => window.localStorage.getItem("tether.prefs.v1") ?? window.localStorage.setItem("tether.prefs.v1", JSON.stringify(p)), prefs);
  await page.goto(`${ORIGIN}/`);
  await expect(page.getByRole("heading", { name: "Your courses" })).toBeVisible();
  return { page, context };
}
const css = (page: Page, selector: string, prop: string) => page.locator(selector).first().evaluate((el, p) => getComputedStyle(el).getPropertyValue(p), prop);

test("Preferences side panel from 1024: theme applies at once, persists with no flash, the page stays visible beside it", async ({ browser }) => {
  const { page, context } = await open(browser, 1280, { theme: "light" });
  const opener = page.getByRole("button", { name: "Preferences" });
  await opener.click();
  const panel = page.getByRole("region", { name: "Preferences" });
  await expect(panel.getByRole("heading", { name: "Preferences" })).toBeFocused();
  const box = (await panel.boundingBox())!;
  // Right edge = the viewport edge less the stable scrollbar gutter (scrollbar-gutter: stable reserves it; headless
  // Chromium hides the bar itself, slice 1 record), so at most 15px short of 1280.
  expect(box.x + box.width).toBeGreaterThanOrEqual(1264);
  expect(box.x + box.width).toBeLessThanOrEqual(1280);
  expect(box.width).toBe(384);
  expect(box.y).toBe(0);
  // The page reflows beside the panel: main's right edge is left of the panel and nothing scrolls sideways.
  const main = (await page.locator("main").boundingBox())!;
  expect(main.x + main.width).toBeLessThanOrEqual(box.x);
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  await expect(page.getByRole("heading", { name: "Your courses" })).toBeInViewport();
  await expect(opener).toBeInViewport();
  await expect(opener).toHaveAttribute("aria-expanded", "true");

  await panel.getByRole("radio", { name: "Course portal" }).click();
  await expect(page.locator("html")).toHaveAttribute("data-skin", "portal");
  await expect(panel.getByRole("status")).toHaveText("Course portal theme on.");
  // The header is the portal's dark bar at once.
  expect(await css(page, "[data-site-header]", "background-color")).toBe("rgb(30, 51, 71)");
  await panel.getByRole("radio", { name: "Course portal" }).press("ArrowDown");
  await panel.getByRole("radio", { name: "Gamer" }).press("ArrowDown");
  await expect(panel.getByRole("radio", { name: "Calm" })).toBeFocused();
  await expect(page.locator("html")).toHaveAttribute("data-skin", "calm");

  // Reload: the stored theme is on <html> before the body exists (pre-hydration script), so there is no flash.
  await page.addInitScript(() => {
    const seen: string[] = [];
    (window as unknown as { __skinAtBody: string[] }).__skinAtBody = seen;
    new MutationObserver((records, obs) => {
      for (const r of records) for (const n of r.addedNodes) if (n.nodeName === "BODY") { seen.push(document.documentElement.getAttribute("data-skin") ?? "none"); obs.disconnect(); }
    }).observe(document, { childList: true, subtree: true });
  });
  await page.reload();
  await expect(page.getByRole("heading", { name: "Your courses" })).toBeVisible();
  expect(await page.evaluate(() => (window as unknown as { __skinAtBody: string[] }).__skinAtBody)).toEqual(["calm"]);
  await expect(page.locator("html")).toHaveAttribute("data-skin", "calm");

  // Escape closes and returns focus; the page takes its full width back.
  await page.getByRole("button", { name: "Preferences" }).click();
  await page.getByRole("region", { name: "Preferences" }).getByRole("radio", { name: "Calm" }).focus();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("region", { name: "Preferences" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Preferences" })).toBeFocused();
  expect(await css(page, "body", "padding-right")).toBe("0px");
  await context.close();
});

test("Preferences below 1024 stays in the page flow; at 320 nothing scrolls sideways; every theme card is a 44px+ target", async ({ browser }) => {
  for (const width of [390, 768]) {
    const { page, context } = await open(browser, width, { theme: "light", skin: "calm" });
    await page.getByRole("button", { name: "Preferences" }).click();
    const panel = page.getByRole("region", { name: "Preferences" });
    expect(await css(page, "#preferences", "position")).toBe("static");
    for (const card of await panel.getByRole("radio").all()) {
      const b = (await card.boundingBox())!;
      expect(b.height).toBeGreaterThanOrEqual(44);
      expect(b.width).toBeGreaterThanOrEqual(44);
    }
    if (width === 390) {
      await page.setViewportSize({ width: 320, height: 640 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
    }
    await context.close();
  }
});

for (const skin of ["portal", "calm"] as const) {
  test(`${skin}: fallbacks by computed style (forced colours, reduced motion, more contrast, reduced transparency, Low-stimulation)`, async ({ browser }) => {
    // Base: the skin's decoration is present (Portal) and the header is opaque in both skins.
    {
      const { page, context } = await open(browser, 1280, { skin, theme: "light" });
      const shadow = await css(page, ".ledger", "box-shadow");
      if (skin === "portal") expect(shadow).toMatch(/1px/);
      else expect(await css(page, ".ledger", "border-top-left-radius")).toBe("16px");
      expect(await css(page, "[data-site-header]", "background-color")).not.toMatch(/rgba\(.*, 0\.\d+\)/);
      await context.close();
    }
    if (skin === "portal") {
      // Dark: --shadow-1 is none there, so the edge must not be written as "<edge>, var(--shadow-1)" (an invalid list that
      // drops the whole declaration; found in slice 5).
      const { page, context } = await open(browser, 1280, { skin, theme: "dark" });
      expect(await css(page, ".ledger", "box-shadow")).toBe("rgb(38, 49, 61) 0px 0px 0px 1px");
      await context.close();
    }
    {
      const { page, context } = await open(browser, 1280, { skin, theme: "light" }, { forcedColors: "active" });
      expect(await css(page, ".ledger", "box-shadow")).toBe("none");
      expect(await css(page, ".ledger", "outline-style")).toBe("solid");
      await context.close();
    }
    {
      const { page, context } = await open(browser, 1280, { skin, theme: "light" }, { reducedMotion: "reduce" });
      const d = await css(page, ".nav-tab", "transition-duration");
      expect(parseFloat(d)).toBeLessThanOrEqual(0.00001);
      await context.close();
    }
    {
      const { page, context } = await open(browser, 1280, { skin, theme: "dark" }, { contrast: "more" });
      expect(await css(page, "body", "color")).toBe("rgb(255, 255, 255)");
      await context.close();
    }
    {
      // Reduced transparency: neither skin has any translucency; the header stays opaque chrome.
      const { page, context } = await open(browser, 1280, { skin, theme: "light" });
      const bg = await css(page, "[data-site-header]", "background-color");
      expect(bg).toMatch(/^rgb\(/);
      expect(await css(page, "[data-site-header]", "backdrop-filter")).toBe("none");
      await context.close();
    }
    {
      // Low-stimulation strips the Portal list shadow (keeps the hair outline) and every transition; Calm is already 0ms.
      const { page, context } = await open(browser, 1280, { skin, theme: "light", quiet: true });
      if (skin === "portal") expect(await css(page, ".ledger", "box-shadow")).toMatch(/^rgb\(213, 220, 227\) 0px 0px 0px 1px$/);
      expect(parseFloat(await css(page, ".nav-tab", "transition-duration"))).toBe(0);
      await context.close();
    }
  });
}

test("G1: under Portal the consent card carries no skin decoration (no accent strip), only the tokens it consumed", async ({ browser }) => {
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await context.newPage();
  await page.addInitScript(() => window.localStorage.setItem("tether.prefs.v1", JSON.stringify({ theme: "light", skin: "portal" })));
  await page.goto(`${ORIGIN}/dev/lecture-harness/consent`);
  await expect(page.getByText(/^Fixture:/)).toBeVisible();
  const shadow = await page.locator("section:has(> #consent-h1)").evaluate((el) => getComputedStyle(el).boxShadow);
  expect(shadow).not.toMatch(/inset/);
  expect(shadow).toMatch(/1px 3px/); // Portal's --shadow-1, a token the card already consumed
  await context.close();
});
