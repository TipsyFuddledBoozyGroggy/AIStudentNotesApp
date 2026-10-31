import { chromium, expect, request, test, type Browser, type Page } from "@playwright/test";
import { mkdirSync } from "node:fs";
import path from "node:path";
import { baseline, box, contentEdges, controls, ink, near, offGrid, overflowX, px, type Rect } from "./geometry";

// Alignment and theme-independence gate (themes-spec 5.2 / 5.3, redesign-round2 3.3 item 3). Runs against `npm run
// dev:real` on :3000 (real API for the signed-in pages; lecture states from the dev-only harness: synthetic data, no
// microphone). Run: npx playwright test -c playwright.screens.config.ts e2e-screens/geometry.spec.ts --workers=1
//
// Part 1, Paper at 320 / 390 / 768 / 1024 / 1280 / 1440 / 1920: shared edges (R3), one content left edge per template
// (R2), 44px controls (R4), equal control heights and centres in a row, heading + action baselines (R7), no horizontal
// overflow, the 56px lecture bar and 44px pill, the Focus dock on the column (A11), the transcript panel top level with
// the rail's first card (A9), and the on-grid report (item 10, report only).
// Part 2, the set of rects of 12 landmark elements is equal within 1px across the 10 looks (5 skins x 2 modes) at 390
// and 1280, except Calm's card padding and the heading-driven text wrap it allows (themes-spec 5.3 "theme independence").

const ORIGIN = "http://localhost:3000";
const OUT = path.join(__dirname, "..", "test-results", "redesign-screens");
const RUN = `geo-${Date.now().toString(36)}`;
const WIDTHS = [320, 390, 768, 1024, 1280, 1440, 1920] as const;
const SKINS = ["paper", "portal", "gamer", "calm", "glass"] as const;
const MODES = ["light", "dark"] as const;

const ctx = { courseId: "", state: path.join(OUT, `.auth-${RUN}.json`) };

// Playwright's default headless flags hide scrollbars while `scrollbar-gutter: stable` still reserves their 15px, which
// no real browser does: with a classic scrollbar the gutter holds it, with overlay scrollbars there is no gutter. So this
// spec runs its own Chromium with scrollbars shown for desktop widths and emulates a phone (overlay scrollbars, touch)
// below 640px. Layout width is then `document.body.clientWidth`.
let browser: Browser;
test.beforeAll(async () => {
  browser = await chromium.launch({ ignoreDefaultArgs: ["--hide-scrollbars"] });
});
test.afterAll(async () => browser?.close());

test.beforeAll(async () => {
  mkdirSync(OUT, { recursive: true });
  const c = await request.newContext({ baseURL: ORIGIN, extraHTTPHeaders: { Origin: ORIGIN } });
  // A short name: the h1 "Hi, {name}." must not wrap at 390 in any heading face (Gamer's monospace is the widest), or
  // every landmark below it moves for a text reason, not a layout one.
  const res = await c.post("/api/v1/dev/login", { data: { email: `geo${Date.now().toString(36).slice(-4)}@dev.local` } });
  expect(res.status(), "dev login").toBe(204);
  const r = await c.post("/api/v1/courses", { data: { name: "Biology 101", code: "BIO 101", term: "Fall 2026", instructor: "Dr. Rivera" } });
  expect(r.status()).toBe(201);
  ctx.courseId = ((await r.json()) as { id: string }).id;
  await c.storageState({ path: ctx.state });
  await c.dispose();
});

async function open(b: Browser, url: string, width: number, opts: { auth?: boolean; prefs?: Record<string, unknown> } = {}) {
  const phone = width < 640;
  const context = await b.newContext({
    viewport: { width, height: phone ? 844 : 900 },
    isMobile: phone,
    hasTouch: phone,
    colorScheme: (opts.prefs?.theme as "light" | "dark" | undefined) ?? "light",
    storageState: opts.auth ? ctx.state : undefined,
  });
  const page = await context.newPage();
  await page.addInitScript((prefs) => window.localStorage.setItem("tether.prefs.v1", JSON.stringify(prefs)), { theme: "light", ...(opts.prefs ?? {}) });
  await page.goto(url);
  await page.evaluate(() => document.fonts.ready);
  return { page, close: () => context.close() };
}

const header = (p: Page) => p.locator("[data-site-header] > div");
const column = (p: Page) => p.locator("[data-column-start]");
const fmt = (r: Rect) => `${Math.round(r.x)},${Math.round(r.y)} ${Math.round(r.width)}x${Math.round(r.height)}`;

/** (7) A heading and its action share a baseline (R7) whenever they sit on one row; a wrapped row (320) is skipped. */
async function sameBaseline(page: Page, heading: string, action: string, where: string) {
  const h = page.getByRole("heading", { name: heading });
  const a = page.getByRole("link", { name: action });
  const [hb, ab] = await Promise.all([box(h), box(a)]);
  if (ab.y >= hb.y + hb.height - 1) return; // wrapped onto its own row
  const [hl, al] = await Promise.all([baseline(h), baseline(a)]);
  expect(near(hl, al, 1), `${where} "${heading}" baseline ${hl} vs "${action}" ${al}`).toBe(true);
}

/** Checks shared by every shell page: R3 edges, R2 column start, R4 targets, header row, no overflow. */
async function shellChecks(page: Page, width: number, name: string, opts: { authed: boolean; template: "desk" | "sheet" | "auth" }) {
  const problems: string[] = [];
  const edges = await contentEdges(header(page));
  // (1) wordmark ink left = container left.
  const wm = await ink(page.getByRole("link", { name: "Tether" }));
  if (!near(wm.x, edges.left, 1.5)) problems.push(`wordmark ink left ${wm.x} vs container ${edges.left}`);
  // (2) rightmost header action ink right = container right (the last quiet button carries .optical-end).
  const last = page.getByRole("button", { name: opts.authed ? "Sign out" : "Preferences" });
  const li = await ink(last);
  if (!near(li.x + li.width, edges.right, 1.5)) problems.push(`last header action ink right ${li.x + li.width} vs container ${edges.right}`);
  // (3) one content left edge per template: the first heading (desk) or the first sheet (sheet) starts on the column.
  const col = await box(column(page));
  if (opts.template === "desk") {
    const h = await ink(page.locator("main h1, main h2").first());
    if (!near(h.x, col.x, 1.5)) problems.push(`first heading ink left ${h.x} vs column ${col.x}`);
    if (width >= 1024) {
      // Desk: the column starts one margin column (5rem) plus the 2rem gap after the container's left edge.
      const shell = await contentEdges(page.locator("[data-shell]"));
      if (!near(col.x, shell.left + 80 + 32, 1)) problems.push(`desk column start ${col.x} vs ${shell.left + 112}`);
    }
  } else if (opts.template === "sheet") {
    const sheet = await box(page.locator("main .card").first());
    if (!near(sheet.x, col.x, 1)) problems.push(`sheet left ${sheet.x} vs column ${col.x}`);
    if (width >= 1024 && !near(sheet.width, 640, 1)) problems.push(`sheet width ${sheet.width} (40rem expected)`);
  } else {
    const card = await box(page.locator("main .card").first());
    const main = await box(page.locator("main"));
    if (!near(card.x + card.width / 2, main.x + main.width / 2, 1)) problems.push(`auth card not centred: ${card.x + card.width / 2} vs ${main.x + main.width / 2}`);
    if (!near(card.y - main.y, 96, 1)) problems.push(`auth top offset ${card.y - main.y} (96 expected)`);
  }
  // (4) targets: controls >= 44 x 44; prose links >= 24 high.
  for (const c of await controls(page)) {
    if (c.kind === "control" && (c.box.height < 43.5 || c.box.width < 43.5)) problems.push(`target "${c.label}" ${Math.round(c.box.width)}x${Math.round(c.box.height)}`);
    if (c.kind === "link" && c.box.height < 23.5) problems.push(`link "${c.label}" height ${Math.round(c.box.height)}`);
  }
  // (6) header actions: one height, one vertical centre.
  const actions = await Promise.all((await page.locator("[data-site-header] button").all()).map(box));
  const centres = actions.map((b) => b.y + b.height / 2);
  if (actions.some((b) => !near(b.height, actions[0].height))) problems.push(`header action heights ${actions.map((b) => b.height).join(",")}`);
  // Rows: one 56px row from 768; two (wordmark + tabs / the three actions) from 390px of layout width; three below that,
  // where "Search or jump", "Preferences" and "Sign out" (342px) no longer share a row. Layout width = the viewport minus
  // the classic scrollbar gutter that `scrollbar-gutter: stable` reserves (15px in headless Chromium, 0 on phones).
  const layoutWidth = await page.evaluate(() => document.body.clientWidth);
  if (width >= 768 && centres.some((c) => !near(c, centres[0]))) problems.push(`header action centres ${centres.join(",")}`);
  const hb = await box(header(page));
  if (width >= 768 && !near(hb.height, 56, 1)) problems.push(`header height ${hb.height} (56 expected)`);
  if (width < 768 && layoutWidth >= 390 && hb.height > 101) problems.push(`phone header height ${hb.height} (two rows expected at ${layoutWidth})`);
  if (width < 768 && layoutWidth < 390 && hb.height > 150) problems.push(`phone header height ${hb.height} (three rows at most at ${layoutWidth})`);
  // (8) no horizontal scroll.
  const ov = await overflowX(page);
  if (ov > 0) problems.push(`horizontal overflow ${ov}px`);
  expect(problems, `${name} @ ${width}`).toEqual([]);
  return offGrid(page);
}

test("Paper, 7 widths: shell pages share edges, one column start per template, 44px targets, baselines, no overflow", async () => {
  test.setTimeout(600_000);
  const report: string[] = [];
  for (const width of WIDTHS) {
    // Today (desk).
    {
      const { page, close } = await open(browser, "/", width, { auth: true });
      await expect(page.getByRole("heading", { name: "Your courses" })).toBeVisible();
      report.push(...(await shellChecks(page, width, "dashboard", { authed: true, template: "desk" })).map((s) => `dashboard@${width} ${s}`));
      // (7) heading + action share a baseline (when the row has not wrapped, which it does at 320).
      await sameBaseline(page, "Your courses", "Add a course", `dashboard@${width}`);
      // (5) card text inset = pad + 1px border on the striped card too (A4).
      const card = page.locator("main .card").first();
      const cb = await box(card);
      const label = await ink(card.locator("h2").first());
      const pad = await px(card, "padding-left");
      expect(near(label.x, cb.x + pad + 1, 1.5), `dashboard@${width} card inset ${label.x - cb.x} vs ${pad + 1}`).toBe(true);
      await close();
    }
    // All courses (desk) and the course page (desk).
    {
      const { page, close } = await open(browser, "/courses", width, { auth: true });
      await expect(page.getByRole("heading", { name: "All courses" })).toBeVisible();
      report.push(...(await shellChecks(page, width, "courses", { authed: true, template: "desk" })).map((s) => `courses@${width} ${s}`));
      await sameBaseline(page, "All courses", "Add a course", `courses@${width}`);
      await close();
    }
    {
      const { page, close } = await open(browser, `/courses/${ctx.courseId}`, width, { auth: true });
      await expect(page.getByRole("heading", { level: 1, name: "Biology 101" })).toBeVisible();
      report.push(...(await shellChecks(page, width, "course-detail", { authed: true, template: "desk" })).map((s) => `course-detail@${width} ${s}`));
      await close();
    }
    // Add a course (sheet) and sign-in (auth).
    {
      const { page, close } = await open(browser, "/courses/new", width, { auth: true });
      await expect(page.getByLabel(/Course name/)).toBeVisible();
      report.push(...(await shellChecks(page, width, "course-new", { authed: true, template: "sheet" })).map((s) => `course-new@${width} ${s}`));
      await close();
    }
    {
      const { page, close } = await open(browser, "/signin", width);
      await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
      report.push(...(await shellChecks(page, width, "signin", { authed: false, template: "auth" })).map((s) => `signin@${width} ${s}`));
      await close();
    }
  }
  // (10) on-grid report: padding and gaps of .card/.btn/.field/.panel that are not multiples of 4. Report first, then enforce.
  test.info().annotations.push({ type: "off-grid", description: report.length ? report.join("; ") : "none" });
  expect(report.length, `off-grid values: ${report.join("; ")}`).toBeLessThanOrEqual(0);
});

test("Paper, 7 widths: lecture bar 56px with 44px pill and controls, columns share edges with the bar, dock equals the Focus column, panel tops level", async () => {
  test.setTimeout(600_000);
  for (const width of WIDTHS) {
    for (const focus of [false, true]) {
      const { page, close } = await open(browser, "/dev/lecture-harness/live-quick-recap", width, { prefs: focus ? { focusMode: true } : {} });
      await expect(page.getByText(/^Fixture:/)).toBeVisible();
      const problems: string[] = [];
      const bar = page.locator("[data-lecture-status-bar]");
      const barInner = bar.locator("> div");
      const bb = await box(bar);
      // One 56px row (plus the 1px rule) from 1280: with the Demo-source tag (shown from 640, gate G1) and the Focus button
      // (from 1024) the bar's content is about 1090px, so between 640 and ~1170 it is two rows (<= 112 + 1), as it was
      // before slice 1. Two rows from 390px of layout width on phones; below that Pause / Stop / Lecture settings (350px)
      // need a third row, and at 320 the pill (211px) and "Connected" (94px) cannot share a row either, so four rows
      // (189px). All recorded in redesign-round2 "Slice 1 results"; the pill stays in view at 320 (harness e2e).
      const layoutWidth = await page.evaluate(() => document.body.clientWidth);
      if (width >= 1280 && !near(bb.height, 57, 1)) problems.push(`bar height ${bb.height}`);
      if (width >= 640 && width < 1280 && bb.height > 113) problems.push(`bar height ${bb.height} > 112 at ${width}`);
      if (width < 640 && layoutWidth >= 390 && bb.height > 113) problems.push(`phone bar height ${bb.height} > 112 at ${layoutWidth}`);
      if (width < 640 && layoutWidth >= 360 && layoutWidth < 390 && bb.height > 158) problems.push(`phone bar height ${bb.height} > 157 at ${layoutWidth}`);
      if (layoutWidth < 360 && bb.height > 190) problems.push(`320 bar height ${bb.height} > 189`);
      // Pill and bar controls: 44px, equal heights and centres per row.
      const pill = page.getByTestId("state-label").locator("xpath=ancestor::div[contains(@class,'rounded-full')]");
      const pb = await box(pill);
      if (!near(pb.height, 44, 1)) problems.push(`pill height ${pb.height}`);
      const ctl = await Promise.all((await bar.locator("button:visible, summary:visible").all()).map(box));
      for (const c of ctl) if (!near(c.height, 44, 1)) problems.push(`bar control height ${c.height}`);
      if (width >= 1024) {
        const centres = [pb, ...ctl].map((b) => b.y + b.height / 2);
        if (centres.some((c) => !near(c, centres[0]))) problems.push(`bar centres ${centres.map(Math.round).join(",")}`);
      }
      // (9) sticky bar content edges = lecture column edges (standard layout; in Focus the bar keeps the full container
      // while the column narrows to 52rem); the Focus dock's box = the Focus column's box (A11).
      const be = await contentEdges(barInner);
      const col = await box(page.locator("[data-layout]"));
      if (!focus && (!near(be.left, col.x, 1) || !near(be.right, col.x + col.width, 1))) problems.push(`bar edges ${be.left}-${be.right} vs column ${col.x}-${col.x + col.width}`);
      // The notice slot (Demo-source notice here) shares the column's edges too (R3).
      const notice = await box(page.getByTestId("demo-notice"));
      if (!focus && (!near(notice.x, col.x, 1) || !near(notice.width, col.width, 1))) problems.push(`notice ${notice.x} w${notice.width} vs column ${col.x} w${col.width}`);
      if (focus && width >= 1024) {
        const dock = await box(page.locator("#lecture-quick-recap"));
        if (!near(dock.x, col.x, 1) || !near(dock.width, col.width, 1)) problems.push(`dock ${dock.x} w${dock.width} vs column ${col.x} w${col.width}`);
        if (!near(col.width, Math.min(832, layoutWidth - 64), 1)) problems.push(`focus column width ${col.width}`);
      }
      if (!focus && width >= 1024) {
        // (A9) the transcript panel and the rail's first card share a top edge; the aside is 22.5rem after a 40px gap.
        const panel = await box(page.locator(".transcript-panel"));
        const rail = await box(page.locator("#lecture-quick-recap"));
        if (!near(panel.y, rail.y, 1)) problems.push(`transcript panel top ${panel.y} vs Quick recap top ${rail.y}`);
        if (!near(rail.width, 360, 1)) problems.push(`aside width ${rail.width}`);
        if (!near(rail.x - (panel.x + panel.width), 40, 1)) problems.push(`column gap ${rail.x - (panel.x + panel.width)}`);
        // 72rem container, 80rem from 1536, inside 32px gutters of the layout width (viewport minus a classic scrollbar).
        const expected = Math.min(width >= 1536 ? 1280 : 1152, layoutWidth - 64);
        if (!near(col.width, expected, 1)) problems.push(`lecture container ${col.width} (expected ${expected})`);
      }
      // Transcript line boxes: 17px / 28px, timestamp column 52px.
      const line = page.locator(".transcript-line").first();
      if (!near(await px(line, "line-height"), 28, 0.5)) problems.push(`transcript line-height ${await px(line, "line-height")}`);
      if (!near(await px(line, "font-size"), 17, 0.5)) problems.push(`transcript font-size ${await px(line, "font-size")}`);
      // Slice 3, the margin track from 1280 (redesign-round2 2.3 / 5.1): the first timestamp starts on the column's left
      // edge, on the page; the sheet's surface starts 5rem + 40px in; the heading and the words sit 24px + 1px inside it
      // (the same text edge in standard and Focus layouts). Below 1280 the timestamp column is inside the sheet.
      {
        const sheet = await box(page.locator(".transcript-panel"));
        const time = await box(page.locator(".transcript-time").first());
        const words = await box(line.locator("> span").nth(1));
        const head = await ink(page.getByRole("heading", { name: "Transcript" }));
        if (width >= 1280) {
          if (!near(sheet.x, col.x, 1)) problems.push(`sheet box left ${sheet.x} vs column ${col.x}`);
          if (!near(time.x, col.x, 1)) problems.push(`timestamp left ${time.x} vs margin ${col.x}`);
          if (!near(words.x, col.x + 80 + 40 + 25, 1.5)) problems.push(`transcript words left ${words.x} vs ${col.x + 145}`);
          if (!near(head.x, col.x + 145, 1.5)) problems.push(`transcript heading left ${head.x} vs ${col.x + 145}`);
        } else {
          const pad = width >= 640 ? 24 : 16;
          if (!near(time.x, sheet.x + pad + (width >= 640 ? 1 : 0), 1.5)) problems.push(`timestamp left ${time.x} vs sheet inset ${sheet.x + pad}`);
        }
      }
      // Targets in the lecture screen (Quick recap windows are 56 / 64 by design, so >= 44 holds).
      for (const c of await controls(page)) {
        if (c.label.startsWith("Step next") || c.label.startsWith("Drop") || c.label.startsWith("Allow") || c.label.startsWith("Unplug") || c.label.startsWith("Server")) continue; // harness-only controls
        if (c.kind === "control" && (c.box.height < 43.5 || c.box.width < 43.5)) problems.push(`target "${c.label}" ${Math.round(c.box.width)}x${Math.round(c.box.height)}`);
      }
      const ov = await overflowX(page);
      if (ov > 0) problems.push(`horizontal overflow ${ov}px`);
      expect(problems, `lecture ${focus ? "focus" : "standard"} @ ${width}`).toEqual([]);
      await close();
    }
  }
});

// ---------------------------------------------------------------------------------------------------------------------
// Theme independence: 12 landmarks, 10 looks, 2 widths. Themes are skins over one geometry (redesign-round2 3.3).
type Landmarks = Record<string, Rect & { headingPx?: number }>;
const diag: Record<string, string> = {};

/**
 * A landmark's box plus the pixels its heading-face text occupies: the sum over `.display` and `.synthesis` elements
 * inside it of (line count x line-height). A skin may change the heading face (which the synthesis role follows, PO-17),
 * and a wider face may add a line, which is the one text-driven height change the gate allows ("heading-driven text
 * wrap inside cards, compared by landmark not by text").
 */
async function landmark(l: import("@playwright/test").Locator): Promise<Rect & { headingPx: number }> {
  const b = await box(l);
  const headingPx = await l.evaluate((el) => {
    const nodes = Array.from(el.querySelectorAll<HTMLElement>(".display, .synthesis"));
    if (el.classList.contains("display") || el.classList.contains("synthesis")) nodes.push(el as HTMLElement);
    let total = 0;
    for (const h of nodes) {
      const range = document.createRange();
      range.selectNodeContents(h);
      const tops = new Set(Array.from(range.getClientRects()).map((r) => Math.round(r.top)));
      total += Math.max(1, tops.size) * (parseFloat(getComputedStyle(h).lineHeight) || 0);
    }
    return total;
  });
  return { ...b, headingPx };
}

async function landmarks(browser: Browser, width: number, prefs: Record<string, unknown>): Promise<Landmarks> {
  const out: Landmarks = {};
  {
    const { page, close } = await open(browser, "/", width, { auth: true, prefs });
    await expect(page.getByRole("heading", { name: "Your courses" })).toBeVisible();
    out.container = await box(header(page));
    out.header = await box(page.locator("[data-site-header]"));
    out.nav = await box(page.getByRole("navigation", { name: "Primary" }));
    out.main = await landmark(page.locator("main"));
    out.card = await landmark(page.locator("main .card").first());
    out.primary = await box(page.locator("main .btn-primary").first());
    // Diagnostic only (printed when main differs): the heights of the dashboard's blocks.
    diag[`${width}:${JSON.stringify(prefs)}`] = await page.evaluate(() =>
      Array.from(document.querySelectorAll<HTMLElement>("main [data-column-start] > div > *, main section, main aside, main ul"))
        .map((e) => `${e.tagName.toLowerCase()}${e.id ? "#" + e.id : ""}${e.getAttribute("aria-labelledby") ? "@" + e.getAttribute("aria-labelledby") : ""}:${Math.round(e.getBoundingClientRect().height)}`)
        .join(" "),
    );
    await close();
  }
  {
    const { page, close } = await open(browser, "/courses/new", width, { auth: true, prefs });
    out.field = await box(page.getByLabel(/Course name/));
    await close();
  }
  {
    const { page, close } = await open(browser, "/dev/lecture-harness/live-quick-recap", width, { prefs });
    await expect(page.getByText(/^Fixture:/)).toBeVisible();
    out.transcript = await landmark(page.locator(".transcript-panel"));
    out.quickRecap = await landmark(page.locator("#lecture-quick-recap"));
    out.tldr = await landmark(page.locator("#lecture-tldr"));
    out.pill = await box(page.getByTestId("state-label").locator("xpath=ancestor::div[contains(@class,'rounded-full')]"));
    await close();
  }
  {
    const { page, close } = await open(browser, "/dev/lecture-harness/live-quick-recap", width, { prefs: { ...prefs, focusMode: true } });
    await expect(page.getByText(/^Fixture:/)).toBeVisible();
    out.dock = await box(page.locator("#lecture-quick-recap"));
    await close();
  }
  return out;
}

for (const width of [390, 1280] as const) {
  test(`landmark rects are equal within 1px across the 10 looks at ${width}`, async () => {
    test.setTimeout(600_000);
    const base = await landmarks(browser, width, { skin: "paper", theme: "light" });
    expect(Object.keys(base)).toHaveLength(12);
    const diffs: string[] = [];
    for (const skin of SKINS) {
      for (const theme of MODES) {
        if (skin === "paper" && theme === "light") continue;
        const got = await landmarks(browser, width, { skin, theme });
        for (const [k, r] of Object.entries(base)) {
          const g = got[k];
          // Calm's roomier card padding (the one sanctioned layout exception, 32 instead of 24) changes the first card's
          // height and everything below it in the same column, and moves what sits inside a card (the primary button, the
          // field) 8px inward. The card's own x and width must still match; so must every landmark outside a card.
          const calm = skin === "calm";
          const insideCard = k === "primary" || k === "field";
          // A wider heading face may add lines to a .display element inside the landmark; the landmark may then grow by at
          // most those lines (and must not shrink). Every other height change is a layout difference and fails.
          const extraHeading = Math.max(0, (g.headingPx ?? 0) - (r.headingPx ?? 0));
          const dh = g.height - r.height;
          const heightOk = near(dh, 0) || (extraHeading > 0 && dh > 0 && dh <= extraHeading + 1);
          const calmShift = near(g.x - r.x, 8) && (k === "field" ? near(r.width - g.width, 16) : near(r.width, g.width));
          const okX = (calm && insideCard && calmShift) || (near(r.x, g.x) && near(r.width, g.width));
          const okY = (calm && (insideCard || k === "card" || k === "main")) || (near(r.y, g.y) && heightOk);
          if (!(okX && okY)) {
            diffs.push(`${skin}/${theme} ${k}: ${fmt(g)} vs paper ${fmt(r)}`);
            if (k === "main") diffs.push(`  main blocks ${skin}/${theme}: ${diag[`${width}:${JSON.stringify({ skin, theme })}`]} | paper: ${diag[`${width}:${JSON.stringify({ skin: "paper", theme: "light" })}`]}`);
          }
        }
      }
    }
    expect(diffs, `landmarks differ at ${width}`).toEqual([]);
  });
}

// Slice 2 (redesign-round2 2.7, acceptance row "2 Primitives v2"): on the ui-kit page every control is >= 44px (prose
// links >= 24), every focusable primitive shows the 3px ring with its 2px offset, the boxed primitives (sheet, ledger
// row, note, stamp, dialog, button, field) sit on the 4px grid, and nothing scrolls sideways. Paper and the four other
// skins at 390 and 1280, so a skin's radius or face cannot move a primitive's box.
test("ui-kit: 44px targets, visible focus ring on every primitive, primitives on the 4px grid, in five skins at 390 and 1280", async () => {
  test.setTimeout(600_000);
  for (const width of [390, 1280]) {
    for (const skin of SKINS) {
      const { page, close } = await open(browser, "/dev/ui-kit", width, { prefs: { skin, theme: "light" } });
      await expect(page.getByRole("heading", { level: 1, name: /UI kit/ })).toBeVisible();
      const problems: string[] = [];
      for (const c of await controls(page)) {
        const min = c.kind === "link" ? 24 : 44;
        if (c.box.height < min - 0.5 || (c.kind === "control" && c.box.width < 44 - 0.5)) problems.push(`${c.kind} "${c.label}" ${Math.round(c.box.width)}x${Math.round(c.box.height)}`);
      }
      // Focus ring: Tab once (keyboard modality), then move focus to each primitive by script and read the computed ring.
      await page.keyboard.press("Tab");
      const rings = await page.evaluate(() => {
        const out: string[] = [];
        const sel = "main button, main a[href], main summary, main [role=switch], main [role=radio][tabindex='0'], main input";
        for (const el of Array.from(document.querySelectorAll<HTMLElement>(sel))) {
          if (el.closest("details:not([open])") && el.tagName !== "SUMMARY") continue;
          // tabindex=-1 is a programmatic target by design (unselected segmented options reach tabindex 0 when arrowed to;
          // headings and transcript lines are focused by script): no ring, as globals.css states.
          if (el.getAttribute("tabindex") === "-1") continue;
          el.focus();
          if (document.activeElement !== el) {
            out.push(`not focusable: ${el.className.toString().slice(0, 40)}`);
            continue;
          }
          const s = getComputedStyle(el);
          if (!el.matches(":focus-visible")) out.push(`no :focus-visible: ${el.className.toString().slice(0, 40)}`);
          else if (s.outlineStyle !== "solid" || parseFloat(s.outlineWidth) !== 3 || parseFloat(s.outlineOffset) !== 2)
            out.push(`ring ${s.outlineStyle} ${s.outlineWidth} ${s.outlineOffset}: ${el.className.toString().slice(0, 40)}`);
        }
        return out;
      });
      problems.push(...rings);
      const ov = await overflowX(page);
      if (ov > 0) problems.push(`horizontal overflow ${ov}px`);
      const grid = await offGrid(page);
      expect(problems, `ui-kit ${skin} @ ${width}`).toEqual([]);
      expect(grid, `ui-kit ${skin} @ ${width} off-grid`).toEqual([]);
      await close();
    }
  }
});
