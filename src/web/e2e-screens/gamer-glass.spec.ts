import AxeBuilder from "@axe-core/playwright";
import { expect, request, test, type Browser, type BrowserContext, type Page } from "@playwright/test";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { contrastRatio } from "../src/lib/design/contrast";
import { GLASS } from "../src/lib/design/tokens";

// Redesign-round2 slice 7 (= themes T3 Gamer + T4 Glass) in a real browser (headless Chromium, the clean `npm run dev:real`
// on :3000; lecture states from the dev-only harness: synthetic data, no microphone). What it proves, by computed style:
//  1. the Grid texture and Solid surfaces switches in Preferences work end to end (apply at once, persist, announce once);
//  2. the grid and the Glass gradient never sit on the lecture route (consent, mic blocked, live, recap, paused, real route)
//     and no lecture text (transcript lines, TL;DR, Quick recap) has anything but an opaque surface under it;
//  3. backdrop-filter exists only on the site header, never on anything that holds main content, lecture text or a field,
//     and no other element is translucent except the dialog scrims;
//  4. every fallback (reduced motion, forced colours, more contrast, reduced transparency, Solid surfaces,
//     Low-stimulation, phone width) for Gamer and Glass on key screens;
//  5. no glow in Gamer (no text-shadow, no blurred shadow, no filter, nothing animated);
//  6. contrast of every page-text colour over every texture / gradient stop in both modes, and of the frosted header's
//     text composited over both gradient stops;
//  7. one Low-stimulation capture per skin (viewed by eye, review 5.3) and a report-only scroll frame-time trace.
// Run: npx playwright test -c playwright.screens.config.ts e2e-screens/gamer-glass.spec.ts --workers=1

const ORIGIN = "http://localhost:3000";
const OUT = path.join(__dirname, "..", "test-results", "redesign-screens");
const RUN = Date.now().toString(36);
const STATE = path.join(OUT, `.auth-gg-${RUN}.json`);
const ctx = { courseId: "" };

test.beforeAll(async () => {
  mkdirSync(OUT, { recursive: true });
  const c = await request.newContext({ baseURL: ORIGIN, extraHTTPHeaders: { Origin: ORIGIN } });
  expect((await c.post("/api/v1/dev/login", { data: { email: `gg-${RUN}@dev.local` } })).status(), "dev login").toBe(204);
  // Enough courses that All courses scrolls at 800px (the frame-time trace), synthetic names only.
  for (let i = 0; i < 14; i++) {
    const r = await c.post("/api/v1/courses", { data: { name: i === 0 ? "Biology 101" : `Course ${i + 1}`, code: `TST ${100 + i}`, term: "Fall 2026" } });
    expect(r.status()).toBe(201);
    if (i === 0) ctx.courseId = ((await r.json()) as { id: string }).id;
  }
  await c.storageState({ path: STATE });
  await c.dispose();
});

type Media = Parameters<Page["emulateMedia"]>[0];
interface OpenOpts {
  width?: number;
  prefs?: Record<string, unknown>;
  media?: Media;
  reducedTransparency?: boolean;
  auth?: boolean;
}
async function open(browser: Browser, url: string, o: OpenOpts = {}): Promise<{ page: Page; context: BrowserContext }> {
  const width = o.width ?? 1280;
  const context = await browser.newContext({ viewport: { width, height: width < 600 ? 844 : 800 }, storageState: o.auth === false ? undefined : STATE });
  const page = await context.newPage();
  if (o.media) await page.emulateMedia(o.media);
  if (o.reducedTransparency) {
    // Playwright 1.64's emulateMedia has no reducedTransparency option; Chromium's DevTools protocol does.
    const cdp = await context.newCDPSession(page);
    await cdp.send("Emulation.setEmulatedMedia", { features: [{ name: "prefers-reduced-transparency", value: "reduce" }] });
  }
  if (o.prefs) await page.addInitScript((p) => window.localStorage.getItem("tether.prefs.v1") ?? window.localStorage.setItem("tether.prefs.v1", JSON.stringify(p)), o.prefs);
  await page.goto(`${ORIGIN}${url}`);
  return { page, context };
}
const fixture = (p: Page) => expect(p.getByText(/^Fixture:/)).toBeVisible();
const today = (p: Page) => expect(p.getByRole("heading", { name: "Your courses" })).toBeVisible();
const css = (page: Page, selector: string, prop: string) => page.locator(selector).first().evaluate((el, p) => getComputedStyle(el).getPropertyValue(p), prop);
const pageDeco = (page: Page) => page.evaluate(() => getComputedStyle(document.body, "::before").backgroundImage);
const header = (page: Page) =>
  page.locator("[data-site-header]").evaluate((el) => {
    const s = getComputedStyle(el);
    // Chromium resolves the -webkit- alias to "" when it equals the standard property; report it as "none" then.
    return { bg: s.backgroundColor, filter: s.backdropFilter, webkit: s.getPropertyValue("-webkit-backdrop-filter") || s.backdropFilter };
  });
const isOpaque = (rgb: string) => /^rgb\(/.test(rgb) || /^rgba\(.*,\s*1\)$/.test(rgb);

// Key screens: Today, Preferences open, Add a course, consent (harness and the real route), and the lecture fixtures.
const LECTURE_FIXTURES = ["consent", "mic-denied", "live", "live-quick-recap", "paused"] as const;

// ---------------------------------------------------------------------------------------------------------------------
test("Preferences: Grid texture and Solid surfaces apply at once, persist with no flash, announce once; each only for its theme", async ({ browser }) => {
  const { page, context } = await open(browser, "/", { prefs: { theme: "light" } });
  await today(page);
  await page.getByRole("button", { name: "Preferences" }).click();
  const panel = page.getByRole("region", { name: "Preferences" });
  const grid = panel.getByRole("switch", { name: "Grid texture" });
  const solid = panel.getByRole("switch", { name: "Solid surfaces" });
  await expect(grid).toHaveCount(0);
  await expect(solid).toHaveCount(0);
  const liveBefore = await page.locator('[aria-live],[role="status"],[role="alert"],[role="log"]').count();

  await panel.getByRole("radio", { name: "Gamer" }).click();
  await expect(grid).toBeVisible();
  await expect(grid).toHaveAttribute("aria-checked", "false");
  expect(await pageDeco(page)).toBe("none"); // off by default
  await grid.click();
  await expect(page.locator("html")).toHaveAttribute("data-texture", "true");
  await expect(panel.getByRole("status")).toHaveText("Grid texture on.");
  expect(await pageDeco(page)).toMatch(/^linear-gradient/);
  // The panel still holds exactly one live region (its status line); showing the switch added none to the page. (The
  // page-wide count includes Next's route announcer in its shadow root, which Playwright's CSS engine pierces.)
  expect(await panel.locator('[aria-live],[role="status"],[role="alert"],[role="log"]').count()).toBe(1);
  expect(await page.locator('[aria-live],[role="status"],[role="alert"],[role="log"]').count()).toBe(liveBefore);
  const gb = (await grid.boundingBox())!;
  expect(gb.height).toBeGreaterThanOrEqual(44);

  // Reload: data-texture is on <html> before <body> exists (pre-hydration script), so the grid does not flash in late.
  await page.addInitScript(() => {
    const seen: string[] = [];
    (window as unknown as { __atBody: string[] }).__atBody = seen;
    new MutationObserver((records, obs) => {
      for (const r of records) for (const n of r.addedNodes) if (n.nodeName === "BODY") { seen.push(`${document.documentElement.getAttribute("data-skin")}/${document.documentElement.getAttribute("data-texture")}`); obs.disconnect(); }
    }).observe(document, { childList: true, subtree: true });
  });
  await page.reload();
  await today(page);
  expect(await page.evaluate(() => (window as unknown as { __atBody: string[] }).__atBody)).toEqual(["gamer/true"]);

  await page.getByRole("button", { name: "Preferences" }).click();
  await panel.getByRole("radio", { name: "Glass" }).click();
  await expect(grid).toHaveCount(0);
  await expect(solid).toBeVisible();
  let h = await header(page);
  expect(h.filter).toBe("blur(20px) saturate(1.4)");
  expect(isOpaque(h.bg)).toBe(false);
  await solid.click();
  await expect(panel.getByRole("status")).toHaveText("Solid surfaces on.");
  await expect(page.locator("html")).toHaveAttribute("data-solid", "true");
  h = await header(page);
  expect(h.filter).toBe("none");
  expect(isOpaque(h.bg)).toBe(true);
  await solid.click();
  await expect(panel.getByRole("status")).toHaveText("Solid surfaces off.");
  expect((await header(page)).filter).toBe("blur(20px) saturate(1.4)");
  await context.close();
});

// ---------------------------------------------------------------------------------------------------------------------
/** In the page: for each lecture-text element, walk up to <html> and report any decoration and the first painted background. */
const lectureTextScan = (page: Page) =>
  page.evaluate(() => {
    const out: string[] = [];
    const targets = Array.from(document.querySelectorAll<HTMLElement>("[data-segment-id], #lecture-tldr, #lecture-quick-recap, #consent-h1, #blocked-h1"));
    for (const t of targets) {
      let opaque = "";
      for (let el: HTMLElement | null = t; el && el !== document.documentElement; el = el.parentElement) {
        for (const pseudo of [null, "::before", "::after"] as const) {
          const s = getComputedStyle(el, pseudo);
          if (s.backdropFilter !== "none") out.push(`${t.id || t.dataset.segmentId}: backdrop-filter on ${el.tagName}${pseudo ?? ""}`);
          // A background image is page texture or a gradient, except Gamer's 12px x 1px corner ticks on a .card (the
          // mic-blocked card keeps them; the consent card does not, G1): corners only, never under a line of text.
          const ticks = el.classList.contains("card") && s.backgroundSize.startsWith("12px 1px") && s.backgroundRepeat.startsWith("no-repeat");
          if (s.backgroundImage !== "none" && !ticks) out.push(`${t.id || t.dataset.segmentId}: background-image ${s.backgroundImage.slice(0, 40)} on ${el.tagName}.${el.className}${pseudo ?? ""}`);
        }
        const bg = getComputedStyle(el).backgroundColor;
        if (!opaque && bg !== "rgba(0, 0, 0, 0)") {
          opaque = bg;
          if (!/^rgb\(/.test(bg)) out.push(`${t.id || t.dataset.segmentId}: first background is translucent ${bg} on ${el.tagName}.${el.className}`);
        }
      }
      if (!opaque && t.id !== "consent-h1" && t.id !== "blocked-h1") out.push(`${t.id || t.dataset.segmentId}: no opaque surface under it`);
    }
    const pageLayer = getComputedStyle(document.body, "::before").backgroundImage;
    return { count: targets.length, problems: out, pageLayer, route: document.querySelector("main")?.getAttribute("data-route") ?? "" };
  });

for (const theme of ["light", "dark"] as const) {
  test(`Gamer grid on (${theme}): present on Today, absent on the whole lecture route; lecture text only on opaque surfaces`, async ({ browser }) => {
    const prefs = { skin: "gamer", theme, texture: true };
    {
      // Positive control: the grid is really on.
      const { page, context } = await open(browser, "/", { prefs });
      await today(page);
      expect(await pageDeco(page)).toMatch(/^linear-gradient.*linear-gradient/);
      await context.close();
    }
    for (const id of LECTURE_FIXTURES) {
      const { page, context } = await open(browser, `/dev/lecture-harness/${id}`, { prefs, auth: false });
      await fixture(page);
      if (id === "live-quick-recap") {
        await page.getByRole("button", { name: /\(your usual\)/ }).click();
        await expect(page.getByRole("heading", { name: "Here is what was said" })).toBeFocused();
      }
      const r = await lectureTextScan(page);
      expect([id, r.route, r.pageLayer, r.problems]).toEqual([id, "lecture", "none", []]);
      expect(r.count, id).toBeGreaterThan(0);
      await context.close();
    }
    {
      // The real consent route (signed in), not only the harness.
      const { page, context } = await open(browser, `/courses/${ctx.courseId}/lecture`, { prefs });
      await expect(page.getByRole("heading", { level: 1, name: "Start lecture: Biology 101" })).toBeVisible();
      const r = await lectureTextScan(page);
      expect([r.route, r.pageLayer, r.problems]).toEqual(["lecture", "none", []]);
      await context.close();
    }
  });
}

// ---------------------------------------------------------------------------------------------------------------------
/** Every element with a backdrop-filter or a translucent background, and whether it holds forbidden content. */
const glassScan = (page: Page) =>
  page.evaluate(() => {
    const filtered: string[] = [];
    const translucent: string[] = [];
    const forbidden: string[] = [];
    const FORBID = "[data-segment-id], #lecture-tldr, #lecture-quick-recap, input, textarea, select, main, [role=log]";
    const name = (el: Element) => el.hasAttribute("data-site-header") ? "header[data-site-header]" : `${el.tagName.toLowerCase()}${el.id ? "#" + el.id : ""}${el.hasAttribute("data-site-header") ? "[data-site-header]" : ""}${typeof el.className === "string" && el.className ? "." + el.className.split(/\s+/)[0] : ""}`;
    for (const el of Array.from(document.querySelectorAll("*"))) {
      for (const pseudo of [null, "::before", "::after"] as const) {
        const s = getComputedStyle(el, pseudo);
        const f = s.backdropFilter !== "none" || (s.getPropertyValue("-webkit-backdrop-filter") || "none") !== "none";
        if (f) {
          filtered.push(name(el) + (pseudo ?? ""));
          if (el.matches(FORBID) || el.querySelector(FORBID)) forbidden.push(name(el));
        }
        const m = s.backgroundColor.match(/^rgba\((?:[\d.]+,\s*){3}([\d.]+)\)$/);
        if (m && Number(m[1]) > 0 && Number(m[1]) < 1) translucent.push(name(el) + (pseudo ?? ""));
      }
    }
    return { filtered, translucent, forbidden };
  });

for (const theme of ["light", "dark"] as const) {
  test(`Glass (${theme}): backdrop-filter only on the site header and only where allowed; nothing else translucent; never on the lecture route`, async ({ browser }) => {
    const prefs = { skin: "glass", theme };
    const SIGNED_IN: [string, string, (p: Page) => Promise<void>, ((p: Page) => Promise<void>)?][] = [
      ["today", "/", today],
      ["preferences", "/", today, async (p) => {
        await p.getByRole("button", { name: "Preferences" }).click();
        await expect(p.getByRole("region", { name: "Preferences" })).toBeVisible();
      }],
      ["palette", "/", today, async (p) => {
        await p.keyboard.press("Control+k");
        await expect(p.getByRole("dialog", { name: "Search or jump" })).toBeVisible();
      }],
      ["course-new", "/courses/new", (p) => expect(p.getByLabel(/Course name/)).toBeVisible()],
      ["course", `/courses/${ctx.courseId}`, (p) => expect(p.getByRole("heading", { level: 1, name: "Biology 101" })).toBeVisible()],
      ["consent", `/courses/${ctx.courseId}/lecture`, (p) => expect(p.getByRole("heading", { level: 1, name: "Start lecture: Biology 101" })).toBeVisible()],
    ];
    for (const width of [390, 1280]) {
      for (const [label, url, ready, act] of SIGNED_IN) {
        const { page, context } = await open(browser, url, { prefs, width });
        await ready(page);
        if (act) await act(page);
        const r = await glassScan(page);
        const wantHeader = width >= 1024 && label !== "consent";
        expect([label, width, r.filtered]).toEqual([label, width, wantHeader ? ["header[data-site-header]"] : []]);
        expect([label, width, r.forbidden]).toEqual([label, width, []]);
        // Translucent: the frosted header where it is frosted, and the dialog scrim when a dialog is open. Nothing else.
        const allowed = [...(wantHeader ? ["header[data-site-header]"] : []), ...(label === "palette" ? ["div.dialog-scrim"] : [])];
        expect([label, width, r.translucent]).toEqual([label, width, allowed]);
        await context.close();
      }
      for (const id of LECTURE_FIXTURES) {
        const { page, context } = await open(browser, `/dev/lecture-harness/${id}`, { prefs, width, auth: false });
        await fixture(page);
        const r = await glassScan(page);
        expect([id, width, r.filtered, r.translucent]).toEqual([id, width, [], []]);
        expect([id, width, await pageDeco(page)]).toEqual([id, width, "none"]);
        await context.close();
      }
    }
  });
}

// ---------------------------------------------------------------------------------------------------------------------
// Fallbacks by computed style for Gamer (grid on) and Glass on Today, consent and the live lecture.
const FALLBACK_SCREENS: [string, string, boolean, (p: Page) => Promise<void>][] = [
  ["today", "/", true, today],
  ["consent", "/dev/lecture-harness/consent", false, fixture],
  ["lecture-live", "/dev/lecture-harness/live", false, fixture],
];
async function transitionsZero(page: Page) {
  return page.evaluate(() => {
    const bad: string[] = [];
    for (const el of Array.from(document.querySelectorAll("*"))) {
      const s = getComputedStyle(el);
      const ds = s.transitionDuration.split(",").map((d) => parseFloat(d));
      if (ds.some((d) => d > 0.0001)) bad.push(`${el.tagName}.${typeof el.className === "string" ? el.className.split(" ")[0] : ""} ${s.transitionDuration}`);
      if (s.animationName !== "none" && parseFloat(s.animationDuration) > 0.0001) bad.push(`${el.tagName} animation ${s.animationName}`);
    }
    return bad;
  });
}

for (const skin of ["gamer", "glass"] as const) {
  for (const theme of ["light", "dark"] as const) {
    test(`${skin} ${theme}: fallbacks (reduced motion, forced colours, more contrast, reduced transparency, switch, Low-stimulation, phone width)`, async ({ browser }) => {
      const base = { skin, theme, ...(skin === "gamer" ? { texture: true } : {}) };
      for (const [label, url, signedIn, ready] of FALLBACK_SCREENS) {
        const run = async (o: OpenOpts, check: (p: Page) => Promise<void>) => {
          const { page, context } = await open(browser, url, { prefs: base, auth: signedIn, ...o });
          await ready(page);
          await check(page);
          await context.close();
        };
        const onLecture = label !== "today";
        // Reduced motion: no transition or animation left anywhere.
        await run({ media: { reducedMotion: "reduce" } }, async (p) => expect([label, await transitionsZero(p)]).toEqual([label, []]));
        // Forced colours: no page layer, no filter, no card ticks / gradients; system colours in charge.
        await run({ media: { forcedColors: "active" } }, async (p) => {
          expect([label, await pageDeco(p)]).toEqual([label, "none"]);
          if (!onLecture) expect((await header(p)).filter).toBe("none");
          const imgs = await p.evaluate(() => Array.from(document.querySelectorAll("*")).filter((e) => getComputedStyle(e).backgroundImage !== "none").map((e) => e.tagName));
          expect([label, imgs]).toEqual([label, []]);
        });
        // More contrast: ink to pure black / white, no page layer, opaque header without filter.
        await run({ media: { contrast: "more" } }, async (p) => {
          expect(await css(p, "body", "color")).toBe(theme === "dark" ? "rgb(255, 255, 255)" : "rgb(0, 0, 0)");
          expect([label, await pageDeco(p)]).toEqual([label, "none"]);
          if (!onLecture) {
            const h = await header(p);
            expect([h.filter, isOpaque(h.bg)]).toEqual(["none", true]);
          }
        });
        // Reduced transparency (CDP): opaque header, no filter. (The grid and gradient are opaque paint, so they stay off
        // the lecture route only, as always.)
        await run({ reducedTransparency: true }, async (p) => {
          expect(await p.evaluate(() => matchMedia("(prefers-reduced-transparency: reduce)").matches)).toBe(true);
          if (!onLecture) {
            const h = await header(p);
            expect([label, h.filter, h.webkit, isOpaque(h.bg)]).toEqual([label, "none", "none", true]);
          }
          if (onLecture) expect(await pageDeco(p)).toBe("none");
        });
        // The theme's own switch: Gamer grid off -> no layer; Glass Solid surfaces on -> opaque, no filter.
        await run({ prefs: skin === "gamer" ? { ...base, texture: false } : { ...base, solid: true } }, async (p) => {
          if (skin === "gamer") expect(await pageDeco(p)).toBe("none");
          if (!onLecture) {
            const h = await header(p);
            expect([label, h.filter, isOpaque(h.bg)]).toEqual([label, "none", true]);
          }
        });
        // Low-stimulation: no page layer, no filter, no shadow token, 0ms.
        await run({ prefs: { ...base, quiet: true } }, async (p) => {
          expect(await pageDeco(p)).toBe("none");
          if (!onLecture) expect((await header(p)).filter).toBe("none");
          expect(await p.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue("--shadow-1").trim())).toBe("none");
          expect([label, await transitionsZero(p)]).toEqual([label, []]);
        });
        // Phone width: never a filter below 1024.
        await run({ width: 390 }, async (p) => {
          if (!onLecture) {
            const h = await header(p);
            expect([label, h.filter, isOpaque(h.bg)]).toEqual([label, "none", true]);
          }
        });
      }
    });
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// No glow in Gamer (themes-spec 1.2: no glow, bloom, pulsing, flicker, chromatic split, scanlines or neon).
const glowScan = (page: Page) =>
  page.evaluate(() => {
    const bad: string[] = [];
    for (const el of Array.from(document.querySelectorAll("*"))) {
      for (const pseudo of [null, "::before", "::after"] as const) {
        const s = getComputedStyle(el, pseudo);
        const n = `${el.tagName}.${typeof el.className === "string" ? el.className.split(" ")[0] : ""}${pseudo ?? ""}`;
        if (s.textShadow !== "none") bad.push(`${n} text-shadow ${s.textShadow}`);
        if (s.filter !== "none") bad.push(`${n} filter ${s.filter}`);
        if (s.animationName !== "none") bad.push(`${n} animation ${s.animationName}`);
        // A glow is a blurred shadow: "<colour> x y blur [spread]" with blur > 0, outside or inside.
        for (const part of s.boxShadow === "none" ? [] : s.boxShadow.split(/,(?![^(]*\))/)) {
          const lens = part.replace(/rgba?\([^)]*\)/g, "").trim().split(/\s+/).filter((t) => /px$/.test(t)).map(parseFloat);
          if (lens.length >= 3 && lens[2] > 0) bad.push(`${n} blurred box-shadow ${part.trim()}`);
        }
      }
    }
    return bad;
  });

test("Gamer, light and dark, grid on: no glow anywhere on Today, Preferences, consent, live lecture, Quick recap, paused (and the synthesis floor)", async ({ browser }) => {
  for (const theme of ["light", "dark"] as const) {
    const prefs = { skin: "gamer", theme, texture: true };
    {
      const { page, context } = await open(browser, "/", { prefs });
      await today(page);
      expect([theme, "today", await glowScan(page)]).toEqual([theme, "today", []]);
      await page.getByRole("button", { name: "Preferences" }).click();
      await expect(page.getByRole("switch", { name: "Grid texture" })).toBeVisible();
      expect([theme, "preferences", await glowScan(page)]).toEqual([theme, "preferences", []]);
      await context.close();
    }
    for (const id of LECTURE_FIXTURES) {
      const { page, context } = await open(browser, `/dev/lecture-harness/${id}`, { prefs, auth: false });
      await fixture(page);
      if (id === "live-quick-recap") await page.getByRole("button", { name: /\(your usual\)/ }).click();
      expect([theme, id, await glowScan(page)]).toEqual([theme, id, []]);
      if (id === "live") {
        // Synthesis floor (review 5.3): the TL;DR line in Gamer's mono stays 24/32 and the transcript stays the body face 17/28.
        const syn = await page.locator("#lecture-tldr .synthesis").first().evaluate((e) => [getComputedStyle(e).fontSize, getComputedStyle(e).lineHeight]);
        expect(syn).toEqual(["24px", "32px"]);
        const line = await page.locator("[data-segment-id]").first().evaluate((e) => [getComputedStyle(e).fontSize, getComputedStyle(e).lineHeight, getComputedStyle(e).fontFamily]);
        expect(line.slice(0, 2)).toEqual(["17px", "28px"]);
        expect(line[2]).not.toMatch(/mono/i);
      }
      await context.close();
    }
  }
});

// ---------------------------------------------------------------------------------------------------------------------
// Contrast in the real DOM: every text colour that sits directly on the page (no painted box of its own between it and
// <body>) against every stop of the page decoration (Gamer: grid line and canvas; Glass: both gradient stops and canvas),
// and every frosted-header text colour composited over both gradient stops. Both modes.
/** "rgb(…)" / "rgba(…)" / "#RRGGBB" -> [r, g, b, a]. */
const rgba = (c: string): number[] => {
  if (c.startsWith("#")) return [...(c.slice(1).match(/../g) ?? []).map((h) => parseInt(h, 16)), 1];
  const n = (c.match(/[\d.]+/g) ?? []).map(Number);
  return n.length === 3 ? [...n, 1] : n;
};
const hex = (rgb: number[]) => `#${rgb.slice(0, 3).map((v) => Math.round(v).toString(16).padStart(2, "0")).join("")}`;
/** Source-over composite of a possibly translucent colour on an opaque one, as hex. */
const over = (fg: string, bg: string) => {
  const f = rgba(fg);
  const b = rgba(bg);
  return hex(f.slice(0, 3).map((v, i) => v * f[3] + b[i] * (1 - f[3])));
};

const textOnPage = (page: Page) =>
  page.evaluate(() => {
    const onPage = new Set<string>();
    const onHeader = new Set<string>();
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
      if (!n.textContent?.trim()) continue;
      const el = n.parentElement!;
      const r = el.getBoundingClientRect();
      if (r.width === 0 || r.height === 0 || getComputedStyle(el).visibility === "hidden" || el.closest('[aria-hidden="true"], .sr-only')) continue;
      let host: Element | null = el;
      let painted = "";
      for (; host && host !== document.body; host = host.parentElement) {
        const bg = getComputedStyle(host).backgroundColor;
        if (bg !== "rgba(0, 0, 0, 0)") { painted = bg; break; }
      }
      const color = getComputedStyle(el).color;
      if (!painted) onPage.add(color);
      else if (host?.hasAttribute("data-site-header") && !/^rgb\(/.test(painted)) onHeader.add(`${color}|${painted}`);
    }
    const root = getComputedStyle(document.documentElement);
    return { onPage: [...onPage], onHeader: [...onHeader], canvas: root.getPropertyValue("--c-canvas").trim(), grid: root.getPropertyValue("--c-grid").trim() };
  });

for (const skin of ["gamer", "glass"] as const) {
  test(`${skin}: contrast of page text over every decoration stop, light and dark (header text over the gradient for Glass)`, async ({ browser }) => {
    const report: Record<string, number> = {};
    for (const theme of ["light", "dark"] as const) {
      const prefs = { skin, theme, texture: skin === "gamer" };
      const screens: [string, string, (p: Page) => Promise<void>, ((p: Page) => Promise<void>)?][] = [
        ["today", "/", today],
        ["preferences", "/", today, async (p) => {
          await p.getByRole("button", { name: "Preferences" }).click();
          await expect(p.getByRole("region", { name: "Preferences" })).toBeVisible();
        }],
        ["courses", "/courses", (p) => expect(p.getByText("Biology 101").first()).toBeVisible()],
        ["course", `/courses/${ctx.courseId}`, (p) => expect(p.getByRole("heading", { level: 1, name: "Biology 101" })).toBeVisible()],
        ["course-new", "/courses/new", (p) => expect(p.getByLabel(/Course name/)).toBeVisible()],
      ];
      for (const [label, url, ready, act] of screens) {
        const { page, context } = await open(browser, url, { prefs });
        await ready(page);
        if (act) await act(page);
        const t = await textOnPage(page);
        const stops = skin === "gamer" ? [t.grid, t.canvas] : [...GLASS[theme].stops, t.canvas];
        expect(t.onPage.length, `${label} has page text`).toBeGreaterThan(0);
        for (const c of t.onPage)
          for (const s of stops) {
            const bg = over(s, "#000000"); // normalise to hex (the stops are opaque)
            const ratio = contrastRatio(over(c, bg), bg);
            report[`${theme} ${label} ${c} on ${bg}`] = Math.round(ratio * 100) / 100;
            expect([theme, label, c, bg, ratio >= 4.5]).toEqual([theme, label, c, bg, true]);
          }
        if (skin === "glass") {
          // Only where the header is frosted (>= 1024): its text over the translucent bar over each gradient stop.
          expect(t.onHeader.length, `${label} frosted header text`).toBeGreaterThan(0);
          for (const pair of t.onHeader) {
            const [c, hb] = pair.split("|");
            for (const s of GLASS[theme].stops) {
              const bg = over(hb, s);
              const ratio = contrastRatio(over(c, bg), bg);
              report[`${theme} ${label} header ${c} on ${hb} over ${s}`] = Math.round(ratio * 100) / 100;
              expect([theme, label, "header", c, s, ratio >= 4.5]).toEqual([theme, label, "header", c, s, true]);
            }
          }
        }
        await context.close();
      }
    }
    writeFileSync(path.join(OUT, `contrast-${skin}.json`), JSON.stringify(report, null, 2));
    const min = Math.min(...Object.values(report));
    test.info().annotations.push({ type: "min contrast", description: `${skin}: ${min} over ${Object.keys(report).length} pairs` });
  });
}

// ---------------------------------------------------------------------------------------------------------------------
// axe on the Gamer and Glass screens with the switches ON at 390 and 1280 (the switch-off looks are in screens.spec.ts),
// including Preferences with the switch visible; plus one Low-stimulation capture per skin for viewing by eye.
test("axe: Gamer with Grid texture and Glass with Solid surfaces, light and dark, 390 and 1280, key screens incl. Preferences", async ({ browser }) => {
  const report: Record<string, string[]> = {};
  const looks = { "gamer-grid": { skin: "gamer", texture: true }, "glass-solid": { skin: "glass", solid: true } } as const;
  const screens: [string, string, boolean, (p: Page) => Promise<void>, ((p: Page) => Promise<void>)?][] = [
    ["today", "/", true, today],
    ["preferences", "/", true, today, async (p) => {
      await p.getByRole("button", { name: "Preferences" }).click();
      await expect(p.getByRole("region", { name: "Preferences" }).getByRole("switch").first()).toBeVisible();
    }],
    ["course-new", "/courses/new", true, (p) => expect(p.getByLabel(/Course name/)).toBeVisible()],
    ["consent", `/courses/${ctx.courseId}/lecture`, true, (p) => expect(p.getByRole("heading", { level: 1, name: "Start lecture: Biology 101" })).toBeVisible()],
    ["lecture-live", "/dev/lecture-harness/live", false, fixture],
  ];
  for (const [lookName, look] of Object.entries(looks))
    for (const theme of ["light", "dark"] as const)
      for (const width of [390, 1280])
        for (const [label, url, signedIn, ready, act] of screens) {
          const { page, context } = await open(browser, url, { prefs: { ...look, theme }, width, auth: signedIn });
          await ready(page);
          if (act) await act(page);
          await page.evaluate(() => document.fonts.ready);
          if (label === "preferences") await page.screenshot({ path: path.join(OUT, `slice7-${label}--${width}--${lookName}-${theme}.png`), fullPage: true });
          const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa", "best-practice"]).analyze();
          report[`${label}--${width}--${lookName}-${theme}`] = r.violations.map((v) => `${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`);
          await context.close();
        }
  writeFileSync(path.join(OUT, "axe-report-slice7.json"), JSON.stringify(report, null, 2));
  expect(Object.entries(report).filter(([, v]) => v.length > 0)).toEqual([]);
  expect(Object.keys(report)).toHaveLength(40);
});

test("Low-stimulation on each of the five skins: one capture each at 1280 for viewing (review 5.3), recording-free Today", async ({ browser }) => {
  for (const skin of ["paper", "portal", "gamer", "calm", "glass"] as const) {
    const { page, context } = await open(browser, "/", { prefs: { skin, theme: "light", quiet: true, texture: true } });
    await today(page);
    await page.evaluate(() => document.fonts.ready);
    expect(await pageDeco(page)).toBe("none");
    await page.screenshot({ path: path.join(OUT, `slice7-lowstim--1280--${skin}.png`), fullPage: true });
    await context.close();
  }
});

// ---------------------------------------------------------------------------------------------------------------------
// Report only (themes-spec T4: "Chrome performance trace at 4x CPU throttle on dashboard scroll, target p95 frame <=
// 16.7ms", a [J] target). Headless Chromium on this machine, rAF intervals while wheel-scrolling All courses (14 rows).
test("report only: scroll frame times at 4x CPU throttle, Glass frosted header vs Paper (All courses, 1280)", async ({ browser }) => {
  const results: Record<string, { p50: number; p95: number; frames: number }> = {};
  for (const [name, prefs] of [["paper", { skin: "paper", theme: "light" }], ["glass", { skin: "glass", theme: "light" }], ["gamer-grid", { skin: "gamer", theme: "dark", texture: true }]] as const) {
    const { page, context } = await open(browser, "/courses", { prefs });
    await expect(page.getByText("Course 14")).toBeAttached();
    const cdp = await context.newCDPSession(page);
    await cdp.send("Emulation.setCPUThrottlingRate", { rate: 4 });
    await page.evaluate(() => {
      const w = window as unknown as { __f: number[] };
      w.__f = [];
      let last = performance.now();
      const tick = (t: number) => { w.__f.push(t - last); last = t; if (w.__f.length < 400) requestAnimationFrame(tick); };
      requestAnimationFrame(tick);
    });
    await page.mouse.move(640, 400);
    for (let i = 0; i < 30; i++) { await page.mouse.wheel(0, i < 15 ? 120 : -120); await page.waitForTimeout(30); }
    const f = (await page.evaluate(() => (window as unknown as { __f: number[] }).__f)).slice(5).sort((a, b) => a - b);
    results[name] = { p50: Math.round(f[Math.floor(f.length * 0.5)] * 10) / 10, p95: Math.round(f[Math.floor(f.length * 0.95)] * 10) / 10, frames: f.length };
    await cdp.send("Emulation.setCPUThrottlingRate", { rate: 1 });
    await context.close();
  }
  writeFileSync(path.join(OUT, "slice7-frames.json"), JSON.stringify(results, null, 2));
  test.info().annotations.push({ type: "frames (ms)", description: JSON.stringify(results) });
  console.log("slice 7 frame times (ms):", JSON.stringify(results));
});
