import { chromium, expect, request, test, type Browser, type Page } from "@playwright/test";
import { mkdirSync } from "node:fs";
import path from "node:path";
import { baseline, box, contentEdges, controls, ink, near, offGrid, overflowX, px, type Rect } from "./geometry";
import { settle } from "./settle";

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
// Since slice 7 Gamer and Glass are also measured with their switch on (Grid texture, Solid surfaces): 14 looks per width.

const ORIGIN = "http://localhost:3000";
const OUT = path.join(__dirname, "..", "test-results", "redesign-screens");
const RUN = `geo-${Date.now().toString(36)}`;
const WIDTHS = [320, 390, 768, 1024, 1280, 1440, 1920] as const;
const SKINS = ["paper", "portal", "gamer", "calm", "glass"] as const;
const MODES = ["light", "dark"] as const;

const ctx = { courseId: "", syllabusCourseId: "", state: path.join(OUT, `.auth-${RUN}.json`) };

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
  // Slice 6: a second course with a syllabus (synthetic text, mock extractor, demo calendar only) so the review screen is
  // measured too; the first course stays without one, so its page keeps "Add your syllabus".
  const r2 = await c.post("/api/v1/courses", { data: { name: "History of Law", code: "HIST 340", term: "Fall 2026" } });
  expect(r2.status()).toBe(201);
  ctx.syllabusCourseId = ((await r2.json()) as { id: string }).id;
  const syl = await c.post(`/api/v1/courses/${ctx.syllabusCourseId}/syllabi`, {
    headers: { "Idempotency-Key": `geo-${RUN}-syllabus` },
    data: {
      text: [
        "Biology 101 syllabus (synthetic test text)",
        "Quiz 1 on September 14, 2026 in class",
        "Problem set 1 due Friday, September 18, 2026, 11:59 PM",
        "Midterm exam, Wednesday, October 14, 2026, 10:00 AM",
        "Lab report 2 due October 16, 2026 (tentative)",
        "Final exam: date TBA",
      ].join("\n"),
      termStart: "2026-08-24",
      termEnd: "2026-12-11",
      defaultTimezone: "America/New_York",
      noticeVersion: "draft-2026-10",
      attestation: { accepted: true, acceptedAt: new Date().toISOString() },
    },
  });
  expect(syl.status(), "create syllabus").toBeLessThan(300);
  const sylId = ((await syl.json()) as { id: string }).id;
  await expect.poll(async () => ((await (await c.get(`/api/v1/syllabi/${sylId}`)).json()) as { status: string }).status, { timeout: 60_000 }).toMatch(/^(ready|partial)$/);
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
/** Lecture bar failure detail: content width, each child/control width ("text":w), row count and document.fonts state. */
async function barDiagnostic(page: Page): Promise<string> {
  return page.evaluate(async () => {
    const bar = document.querySelector<HTMLElement>("[data-lecture-status-bar] > div");
    if (!bar) return "no bar";
    const cs = getComputedStyle(bar);
    const content = bar.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight);
    const els = Array.from(bar.querySelectorAll<HTMLElement>(":scope > *, button, summary")).filter((e) => e.offsetParent !== null);
    const tops = new Set(els.map((e) => Math.round(e.getBoundingClientRect().top)));
    const w = els.map((e) => `"${(e.innerText || "").trim().replace(/\s+/g, " ").slice(0, 24)}":${e.getBoundingClientRect().width.toFixed(2)}`);
    const fonts = Array.from(document.fonts).filter((f) => f.status === "loaded").map((f) => `${f.family}/${f.weight}`);
    return `content ${content.toFixed(2)}; distinct tops ${tops.size}; ${w.join(", ")}; fonts ${document.fonts.status} loaded [${[...new Set(fonts)].join(", ")}]`;
  });
}

const fmt = (r: Rect) =>`${Math.round(r.x)},${Math.round(r.y)} ${Math.round(r.width)}x${Math.round(r.height)}`;

/** (7) A heading and its action share a baseline (R7) whenever they sit on one row; a wrapped row (320) is skipped. */
async function sameBaseline(page: Page, heading: string, action: string, where: string) {
  const h = page.getByRole("heading", { name: heading });
  const a = page.getByRole("link", { name: action });
  const [hb, ab] = await Promise.all([box(h), box(a)]);
  if (ab.y >= hb.y + hb.height - 1) return; // wrapped onto its own row
  const [hl, al] = await Promise.all([baseline(h), baseline(a)]);
  expect(near(hl, al, 1), `${where} "${heading}" baseline ${hl} vs "${action}" ${al}`).toBe(true);
}

/**
 * Slice 4 Desk margin: every `.desk-label` starts its ink on the container edge from 1024 (and keeps a clear space before
 * the column), on the column below 1024; from 1024 it shares a baseline with the first line of its section's body, or with
 * its action when the section has one.
 */
async function marginChecks(page: Page, width: number, where: string) {
  const problems: string[] = [];
  const shell = await contentEdges(page.locator("[data-shell]"));
  const col = await box(column(page));
  const labels = await page.locator("main .desk-label").all();
  expect(labels.length, `${where}@${width} margin labels`).toBeGreaterThan(0);
  for (const l of labels) {
    const name = (await l.textContent())?.trim() ?? "";
    const li = await ink(l);
    const edge = width >= 1024 ? shell.left : col.x;
    if (!near(li.x, edge, 1.5)) problems.push(`label "${name}" ink left ${li.x} vs ${width >= 1024 ? "container" : "column"} ${edge}`);
    if (width >= 1024) {
      if (li.x + li.width > col.x - 8) problems.push(`label "${name}" ink right ${li.x + li.width} runs into the column ${col.x}`);
      const section = l.locator("xpath=..");
      const action = section.locator("> .desk-action");
      const partner = (await action.count()) ? action : section.locator("> .desk-body");
      const [lb, pb] = await Promise.all([baseline(l), baseline(partner)]);
      if (!near(lb, pb, 1)) problems.push(`label "${name}" baseline ${lb} vs ${(await action.count()) ? "action" : "body"} ${pb}`);
    }
  }
  expect(problems, `${where} @ ${width} margin`).toEqual([]);
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
    // Slice 4: the h1 sits above the sheet on the column start; the sheet starts on the column too, except below 640
    // where it is full-bleed (16px outward) with its text on the gutter, i.e. on the column edge again.
    const h = await ink(page.locator("main h1").first());
    if (!near(h.x, col.x, 1.5)) problems.push(`sheet page h1 ink left ${h.x} vs column ${col.x}`);
    const sheet = await box(page.locator("main .sheet").first());
    const bleed = width < 640 ? 16 : 0;
    if (!near(sheet.x, col.x - bleed, 1)) problems.push(`sheet left ${sheet.x} vs column ${col.x} - ${bleed}`);
    if (width >= 1024 && !near(sheet.width, 640, 1)) problems.push(`sheet width ${sheet.width} (40rem expected)`);
    const field = await box(page.locator("main .sheet .field").first());
    if (width < 640 && !near(field.x, col.x, 1)) problems.push(`phone field left ${field.x} vs column ${col.x} (text on the gutter)`);
  } else {
    // Auth: the block (h1 + sentence + sheet) is 28rem centred, 96px from the top of main; the sheet is centred too.
    const block = await box(page.locator("[data-column-start] > *").first());
    const sheet = await box(page.locator("main .sheet").first());
    const main = await box(page.locator("main"));
    if (!near(block.x + block.width / 2, main.x + main.width / 2, 1)) problems.push(`auth block not centred: ${block.x + block.width / 2} vs ${main.x + main.width / 2}`);
    if (!near(sheet.x + sheet.width / 2, main.x + main.width / 2, 1)) problems.push(`auth sheet not centred: ${sheet.x + sheet.width / 2} vs ${main.x + main.width / 2}`);
    if (!near(block.y - main.y, 96, 1)) problems.push(`auth top offset ${block.y - main.y} (96 expected)`);
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
      await settle(page);
      report.push(...(await shellChecks(page, width, "dashboard", { authed: true, template: "desk" })).map((s) => `dashboard@${width} ${s}`));
      // (7) heading + action share a baseline (when the row has not wrapped, which it does at 320).
      await sameBaseline(page, "Your courses", "Add a course", `dashboard@${width}`);
      // (5) Slice 4 (redesign-round2 2.7 Margin label, 5.3): no cards on Today. Margin labels hang on the container edge
      // from 1024 (the edge the lecture timestamps use) and sit on the column below; ledger row text sits on the column.
      await marginChecks(page, width, "dashboard");
      const row = await ink(page.locator("main .ledger-row-link").first());
      const colX = (await box(column(page))).x;
      expect(near(row.x, colX, 1.5), `dashboard@${width} ledger row text ${row.x} vs column ${colX}`).toBe(true);
      await close();
    }
    // All courses (desk) and the course page (desk).
    {
      const { page, close } = await open(browser, "/courses", width, { auth: true });
      await expect(page.getByRole("heading", { name: "All courses" })).toBeVisible();
      await settle(page);
      report.push(...(await shellChecks(page, width, "courses", { authed: true, template: "desk" })).map((s) => `courses@${width} ${s}`));
      await sameBaseline(page, "All courses", "Add a course", `courses@${width}`);
      await close();
    }
    {
      const { page, close } = await open(browser, `/courses/${ctx.courseId}`, width, { auth: true });
      await expect(page.getByRole("heading", { level: 1, name: "Biology 101" })).toBeVisible();
      await settle(page);
      report.push(...(await shellChecks(page, width, "course-detail", { authed: true, template: "desk" })).map((s) => `course-detail@${width} ${s}`));
      await expect(page.getByRole("link", { name: "Add your syllabus" })).toBeVisible();
      await marginChecks(page, width, "course-detail");
      await close();
    }
    // Syllabus review (desk; slice 6): the shell checks plus item-row text and the group head (its chevron first) on the
    // column edge (ledger), and the row's one bordered button.
    {
      const { page, close } = await open(browser, `/courses/${ctx.syllabusCourseId}/syllabus`, width, { auth: true });
      await expect(page.getByRole("heading", { level: 1, name: "Review your syllabus items" })).toBeVisible({ timeout: 30_000 });
      await expect(page.getByText(/^We found \d+ items\./)).toBeVisible();
      await settle(page);
      report.push(...(await shellChecks(page, width, "syllabus-review", { authed: true, template: "desk" })).map((s) => `syllabus-review@${width} ${s}`));
      const colX = (await box(column(page))).x;
      const title = await ink(page.locator("main li.item-row h3").first());
      expect(near(title.x, colX, 1.5), `syllabus-review@${width} item title ${title.x} vs column ${colX}`).toBe(true);
      const head = await box(page.locator("main .group-head").first());
      expect(near(head.x, colX, 1.5), `syllabus-review@${width} group head ${head.x} vs column ${colX}`).toBe(true);
      for (const li of await page.locator("main li.item-row:visible").all()) {
        const bordered = await li.locator("button.btn-secondary, button.btn-primary, button.btn-danger").count();
        expect(bordered, `syllabus-review@${width} bordered buttons in a row`).toBeLessThanOrEqual(1);
      }
      // The calendar step itself is never fixed or sticky (a bar over the list would cover row targets). Slice 8c: below 1280
      // its rail wrapper is in the flow too and the step sits after the last group, visually and in the DOM; from 1280 the
      // rail is checked in the "slice 8c" test below.
      expect(await page.locator(".calendar-step").evaluate((el) => getComputedStyle(el).position), `syllabus-review@${width} calendar step position`).toMatch(/^(static|relative)$/);
      if (width < 1280) {
        expect(await page.locator(".review-rail").evaluate((el) => getComputedStyle(el).position), `syllabus-review@${width} rail position`).toMatch(/^(static|relative)$/);
        const lastGroup = await box(page.locator(".review-groups > section").last());
        const step = await box(page.locator("[data-calendar-step]"));
        expect(step.y, `syllabus-review@${width} calendar step below the last group`).toBeGreaterThanOrEqual(lastGroup.y + lastGroup.height);
        expect(near(step.x, colX, 1.5), `syllabus-review@${width} calendar step on the column ${step.x} vs ${colX}`).toBe(true);
      }
      await close();
    }
    // Add a course (sheet) and sign-in (auth).
    {
      const { page, close } = await open(browser, "/courses/new", width, { auth: true });
      await expect(page.getByLabel(/Course name/)).toBeVisible();
      await settle(page);
      report.push(...(await shellChecks(page, width, "course-new", { authed: true, template: "sheet" })).map((s) => `course-new@${width} ${s}`));
      await close();
    }
    {
      const { page, close } = await open(browser, "/signin", width);
      await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
      await settle(page);
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
      // CI run 41966230539 (Linux): the bar measured 137px at 390, the "Not connected" layout (slice 8b's disconnected@390
      // is 137 too), while the diagnostic read a moment later found "Connected" and two rows. The box was taken before the
      // fixture had hydrated and connected. Wait for the connected state, then for a settled layout (settle.ts), first.
      await expect(page.getByTestId("connection")).toHaveText("Connected");
      await settle(page, { focusMode: focus });
      const problems: string[] = [];
      const bar = page.locator("[data-lecture-status-bar]");
      const barInner = bar.locator("> div");
      const bb = await box(bar);
      // One 56px row (plus the 1px rule) from 1280. Before slice 8b the Demo-source tag (shown from 640) made the bar two
      // rows between 640 and ~1170 (<= 112 + 1); the tag left the bar in slice 8b (PO-18c), the <= 113 bound stays. Two rows from 390px of layout width on phones; below that Pause / Stop / Lecture settings (325px on
      // Windows Chromium since the phone padding fix of 2026-11-01, was 350px; Resume adds 15px) may need a third row
      // (<= 157), and at 320 the pill (211px) and "Connected" (94px) cannot share a row either, so four rows
      // (189px). All recorded in redesign-round2 "Slice 1 results"; the pill stays in view at 320 (harness e2e).
      const layoutWidth = await page.evaluate(() => document.body.clientWidth);
      if (width >= 1280 && !near(bb.height, 57, 1)) problems.push(`bar height ${bb.height}`);
      if (width >= 640 && width < 1280 && bb.height > 113) problems.push(`bar height ${bb.height} > 112 at ${width}`);
      if (width < 640 && layoutWidth >= 390 && bb.height > 113) problems.push(`phone bar height ${bb.height} > 112 at ${layoutWidth}`);
      if (width < 640 && layoutWidth >= 360 && layoutWidth < 390 && bb.height > 158) problems.push(`phone bar height ${bb.height} > 157 at ${layoutWidth}`);
      if (layoutWidth < 360 && bb.height > 190) problems.push(`320 bar height ${bb.height} > 189`);
      // On a bar-height miss, say why (docs/ops/ci-first-run-e2e-diagnosis.md 1/3): each control's width and the font state.
      if (problems.some((s) => s.includes("bar height"))) problems.push(`bar diag: ${await barDiagnostic(page)}`);
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
      // Slice 3, the margin track from 1280 (redesign-round2 2.3 / 5.1): the timestamps hang in the margin, on the page;
      // the sheet's surface starts 5rem + 40px in; the heading and the words sit 24px + 1px inside it (the same text edge
      // in standard and Focus layouts). Below 1280 the timestamp column is inside the sheet.
      // Slice 8 (redesign-round2-review 1.2, recorded change): the first timestamp's ink ENDS at the margin track's end
      // (column + 5rem), right-aligned against the sheet, and starts inside the track; before slice 8 it started on the
      // column's left edge.
      {
        const sheet = await box(page.locator(".transcript-panel"));
        const time = await box(page.locator(".transcript-time").first());
        const timeInk = await box(page.locator(".transcript-time > [aria-hidden]").first());
        const words = await box(line.locator("> span").nth(1));
        const head = await ink(page.getByRole("heading", { name: "Transcript" }));
        if (width >= 1280) {
          if (!near(sheet.x, col.x, 1)) problems.push(`sheet box left ${sheet.x} vs column ${col.x}`);
          if (!near(timeInk.x + timeInk.width, col.x + 80, 1)) problems.push(`timestamp ink right ${timeInk.x + timeInk.width} vs margin end ${col.x + 80}`);
          if (timeInk.x < col.x - 0.5) problems.push(`timestamp ink left ${timeInk.x} outside the margin track ${col.x}`);
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
// Slice 8b (PO-18c; make-notes-rail-demo-notice 2.6 item 6): with the Demo tag out of the bar, the bar is one 57px row at
// 1280 in every lecture fixture (Mismatch and Disconnected included, which wrapped to two rows before); the collapsed Demo
// note is one line at 1280; notes under the bar are 24px apart (the first 12px under the bar); every notice slot shares
// the column's edges; the recording pill stays 44px. Measured values are attached as an annotation.
const NOTE_FIXTURES = ["live", "paused", "disconnected", "mismatch", "stopped"] as const;
test("slice 8b: lecture bar one row at 1280 in every fixture; one-line Demo note; notes 24px apart; pill 44px", async () => {
  test.setTimeout(600_000);
  const measured: string[] = [];
  for (const width of [320, 390, 1280, 1440] as const) {
    for (const fixture of NOTE_FIXTURES) {
      const { page, close } = await open(browser, `/dev/lecture-harness/${fixture}`, width);
      await expect(page.getByText(/^Fixture:/)).toBeVisible();
      await settle(page);
      const problems: string[] = [];
      const bar = await box(page.locator("[data-lecture-status-bar]"));
      if (width >= 1280 && !near(bar.height, 57, 1)) problems.push(`bar height ${bar.height} (one 56px row + rule expected)`);
      const pill = await box(page.getByTestId("state-label").locator("xpath=ancestor::div[contains(@class,'rounded-full')]"));
      if (!near(pill.height, 44, 1)) problems.push(`pill height ${pill.height}`);
      if (await page.getByTestId("demo-tag").count()) problems.push("demo tag present in the bar");
      const demo = await box(page.getByTestId("demo-notice"));
      if (width >= 1280 && demo.height > 64) problems.push(`collapsed Demo note ${demo.height}px tall at ${width} (one line, <= 64 expected)`);
      if (await page.locator("#demo-notice-body").isVisible()) problems.push("Demo note body visible while collapsed");
      const col = await box(page.locator("[data-layout]"));
      const slots = await Promise.all((await page.locator(".notice-slot > *").all()).map(box));
      for (const s of slots) if (!near(s.x, col.x, 1) || !near(s.width, col.width, 1)) problems.push(`notice ${s.x} w${s.width} vs column ${col.x} w${col.width}`);
      if (slots.length && !near(slots[0].y - (bar.y + bar.height), 12, 1)) problems.push(`first note ${slots[0].y - (bar.y + bar.height)}px under the bar (12 expected)`);
      for (let i = 1; i < slots.length; i++) {
        const gap = slots[i].y - (slots[i - 1].y + slots[i - 1].height);
        if (!near(gap, 24, 1)) problems.push(`gap between notes ${i - 1} and ${i}: ${gap} (24 expected)`);
      }
      measured.push(`${fixture}@${width}: bar ${Math.round(bar.height)}, demo note ${Math.round(demo.height)}, notes ${slots.map((s) => Math.round(s.height)).join("+")}`);
      expect(problems, `${fixture} @ ${width}`).toEqual([]);
      await close();
    }
  }
  test.info().annotations.push({ type: "slice-8b", description: measured.join("; ") });
  console.log(`[slice 8b] ${measured.join("; ")}`);
});

// ---------------------------------------------------------------------------------------------------------------------
// Slice 8a (PO-18d; make-notes-rail-demo-notice 1.9 items 10 and 11). Paper at 320 / 390 / 1280 / 1440: the entry block
// sits on the transcript track like the review toolbar (its heading on the search field's edge); the Ready sheet has the
// transcript sheet's box, paints from the same track, puts its heading on the transcript heading's text edge and hangs
// its lead citation times in the margin with the transcript times; below 640 it is square and full-bleed; every citation
// is a 44px target; nothing scrolls sideways. Then the Ready sheet across the 14 looks at 390 and 1280: the same x, y and
// width within 1px (height may grow only by heading-face lines, as the landmark gate allows), and in every look the h3
// differs from the bullet text by face or by size.
test("slice 8a: Make notes block and Ready sheet geometry; the Ready sheet equal across the 14 looks", async () => {
  test.setTimeout(600_000);
  const measured: string[] = [];
  for (const width of [320, 390, 1280, 1440] as const) {
    for (const fixture of ["stopped", "stopped-notes-ready"] as const) {
      const { page, close } = await open(browser, `/dev/lecture-harness/${fixture}`, width);
      await expect(page.locator("#lecture-notes")).toBeVisible();
      await settle(page);
      const problems: string[] = [];
      const notes = await box(page.locator("#lecture-notes"));
      const sheet = await box(page.locator(".transcript-panel"));
      const h2 = await box(page.locator("#notes-h2"));
      if (fixture === "stopped") {
        const field = await box(page.locator("#lecture-search"));
        if (!near(h2.x, field.x, 1)) problems.push(`entry heading x ${h2.x} vs search field ${field.x}`);
        if (width >= 1280 && !near(notes.x, field.x, 1)) problems.push(`entry block x ${notes.x} vs transcript track ${field.x}`);
      } else {
        if (!near(notes.x, sheet.x, 1) || !near(notes.width, sheet.width, 1)) problems.push(`sheet ${notes.x} w${notes.width} vs transcript sheet ${sheet.x} w${sheet.width}`);
        const th = await box(page.getByRole("heading", { level: 2, name: "Transcript" }));
        if (!near(h2.x, th.x, 1)) problems.push(`heading x ${h2.x} vs transcript heading ${th.x}`);
        const before = await page.evaluate(() => [document.getElementById("lecture-notes")!, document.querySelector(".transcript-panel")!].map((e) => getComputedStyle(e, "::before").left));
        if (before[0] !== before[1]) problems.push(`painted start ${before[0]} vs ${before[1]}`);
        const lead = await box(page.locator("[data-notes-bullet] .cite").first());
        const time = await box(page.locator(".transcript-time").first());
        // Slice 8 (recorded change): both are right-aligned in the margin track now, so their END edges match (before: x).
        if (width >= 1280 && !near(lead.x + lead.width, time.x + time.width, 1)) problems.push(`lead citation right ${lead.x + lead.width} vs transcript time right ${time.x + time.width}`);
        if (width < 640 && (await page.locator("#lecture-notes").evaluate((e) => getComputedStyle(e).borderTopLeftRadius)) !== "0px") problems.push("phone sheet not square");
        for (const c of await page.locator("#lecture-notes .cite").all()) {
          const b = await box(c);
          if (b.height < 44 || b.width < 44) problems.push(`citation ${b.width}x${b.height}`);
        }
      }
      const over = await overflowX(page);
      if (over > 0) problems.push(`horizontal overflow ${over}px`);
      measured.push(`${fixture}@${width}: notes x${Math.round(notes.x)} w${Math.round(notes.width)} h${Math.round(notes.height)}`);
      expect(problems, `${fixture} @ ${width}`).toEqual([]);
      await close();
    }
  }
  for (const width of [390, 1280] as const) {
    const measure = async (prefs: Record<string, unknown>) => {
      const { page, close } = await open(browser, "/dev/lecture-harness/stopped-notes-ready", width, { prefs });
      await expect(page.locator("#lecture-notes")).toBeVisible();
      await settle(page, prefs);
      const r = await landmark(page.locator("#lecture-notes"));
      const faces = await page.evaluate(() => {
        const h3 = getComputedStyle(document.querySelector("#lecture-notes h3")!);
        const li = getComputedStyle(document.querySelector("#lecture-notes [data-notes-bullet] span.max-w-prose")!);
        return { differ: h3.fontFamily !== li.fontFamily || h3.fontSize !== li.fontSize, liSize: li.fontSize, liLine: li.lineHeight };
      });
      await close();
      return { r, faces };
    };
    const base = await measure({ skin: "paper", theme: "light" });
    const diffs: string[] = [];
    for (const skin of SKINS) {
      for (const theme of MODES) {
        const variants: Record<string, unknown>[] =
          skin === "gamer" ? [{ skin, theme }, { skin, theme, texture: true }] : skin === "glass" ? [{ skin, theme }, { skin, theme, solid: true }] : [{ skin, theme }];
        for (const prefs of variants) {
          const { r, faces } = await measure(prefs);
          const extra = Math.max(0, r.headingPx - base.r.headingPx);
          const dh = r.height - base.r.height;
          const heightOk = near(dh, 0) || (extra > 0 && dh > 0 && dh <= extra + 1);
          if (!near(r.x, base.r.x) || !near(r.y, base.r.y) || !near(r.width, base.r.width) || !heightOk) diffs.push(`${JSON.stringify(prefs)}: ${fmt(r)} vs paper ${fmt(base.r)}`);
          if (!faces.differ) diffs.push(`${JSON.stringify(prefs)}: h3 and bullets share face and size`);
          if (faces.liSize !== "17px" || faces.liLine !== "28px") diffs.push(`${JSON.stringify(prefs)}: bullets ${faces.liSize}/${faces.liLine}`);
        }
      }
    }
    expect(diffs, `Ready sheet differs across looks at ${width}`).toEqual([]);
  }
  console.log(`[slice 8a] ${measured.join("; ")}`);
});

// ---------------------------------------------------------------------------------------------------------------------
// Slice 8c (PO-18b; make-notes-rail-demo-notice 3.5 items 1 and 2): from 1280 the Calendar step sits in a reserved 20rem
// sticky track to the right of the groups. Rail geometry at scroll 0, then a scroll scan every 200px from the top to the
// bottom of the page, with the first group open and with every group open: no item row, no button in a row and no group
// head ever intersects the rail's box; the one fill stays inside the viewport below the header; and axe's target-size rule
// reports nothing at any sampled position.
test("slice 8c: Calendar step rail from 1280: 20rem sticky track, shared edges, no row or head under it at any scroll position", async () => {
  test.setTimeout(600_000);
  const { default: AxeBuilder } = await import("@axe-core/playwright");
  const measured: string[] = [];
  for (const width of [1280, 1440, 1920] as const) {
    for (const allOpen of [false, true]) {
      const { page, close } = await open(browser, `/courses/${ctx.syllabusCourseId}/syllabus`, width, { auth: true });
      await expect(page.getByRole("heading", { level: 1, name: "Review your syllabus items" })).toBeVisible({ timeout: 30_000 });
      await expect(page.getByText(/^We found \d+ items\./)).toBeVisible();
      await settle(page);
      if (allOpen) {
        await page.getByRole("button", { name: "Expand all" }).click();
        await expect(page.getByRole("button", { name: "Collapse all" })).toBeVisible();
        await settle(page);
      }
      const where = `syllabus-review@${width}${allOpen ? " all open" : ""}`;
      const problems: string[] = [];
      const rail = page.locator(".review-rail");
      expect(await rail.locator("[data-calendar-step]").count(), `${where}: the step is inside the rail`).toBe(1);
      const style = await rail.evaluate((el) => ({ position: getComputedStyle(el).position, top: getComputedStyle(el).top }));
      if (style.position !== "sticky" || style.top !== "80px") problems.push(`rail ${style.position} top ${style.top} (sticky 80px expected)`);
      expect(await page.locator("[data-calendar-step]").evaluate((el) => getComputedStyle(el).position)).toMatch(/^(static|relative)$/);
      const col = await box(column(page));
      const rb = await box(rail);
      const groups = await box(page.locator(".review-groups"));
      const firstHead = await box(page.locator("main .group-head").first());
      if (!near(rb.width, 320, 1)) problems.push(`rail width ${rb.width}`);
      if (!near(rb.x + rb.width, col.x + col.width, 1)) problems.push(`rail right ${rb.x + rb.width} vs column right ${col.x + col.width}`);
      if (!near(rb.y, firstHead.y, 1)) problems.push(`rail top ${rb.y} vs first group head top ${firstHead.y}`);
      if (!near(rb.x - (groups.x + groups.width), 40, 1)) problems.push(`gap groups -> rail ${rb.x - (groups.x + groups.width)}`);
      measured.push(`${where}: rows ${Math.round(groups.width)}, rail ${Math.round(rb.width)}x${Math.round(rb.height)}`);
      // Scroll scan.
      const total = await page.evaluate(() => document.documentElement.scrollHeight - window.innerHeight);
      const header = await box(page.locator("[data-site-header]"));
      const viewportH = page.viewportSize()!.height;
      for (let y = 0; ; y = Math.min(total, y + 200)) {
        await page.evaluate((top) => window.scrollTo(0, top), y);
        await page.waitForTimeout(30);
        const hits = await page.evaluate(() => {
          const r = document.querySelector(".review-rail")!.getBoundingClientRect();
          const out: string[] = [];
          const targets = document.querySelectorAll<HTMLElement>("main li.ledger-row, main li.ledger-row button, main .group-head");
          for (const el of Array.from(targets)) {
            const b = el.getBoundingClientRect();
            if (b.width === 0 || b.height === 0) continue;
            if (b.left < r.right && b.right > r.left && b.top < r.bottom && b.bottom > r.top) out.push(`${el.tagName.toLowerCase()}.${el.className.toString().split(" ")[0]} "${(el.getAttribute("aria-label") ?? el.textContent ?? "").trim().slice(0, 30)}"`);
          }
          return out;
        });
        if (hits.length) problems.push(`scroll ${y}: ${hits.join(", ")} under the rail`);
        const fill = await box(page.locator(".review-rail .btn-primary"));
        if (fill.y < header.height - 0.5 || fill.y + fill.height > viewportH + 0.5) problems.push(`scroll ${y}: the fill ${Math.round(fill.y)}-${Math.round(fill.y + fill.height)} is not inside the viewport below the header`);
        const axe = await new AxeBuilder({ page }).withRules(["target-size"]).analyze();
        for (const v of axe.violations) problems.push(`scroll ${y}: axe ${v.id}: ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`);
        if (y >= total) break;
      }
      // Header straddle (CI run 41322171511: "Edit Quiz 1 on in class" at scroll 600 on Linux only). A page target that is
      // half scrolled under the header, beneath one of the header's own links or buttons, keeps only a sliver below that
      // control, and axe target-size counts the sliver as the target's size. The 200px scan above only finds that when a
      // sample happens to land in the ~20px window, which depends on font metrics. So, deterministically: every target in
      // the page (not the rail) that shares x with a header control is scrolled until its bottom is 4px below the header
      // (measured with the old sticky header: axe failed "Week" when its bottom was 1-8px below the header bottom, or its
      // top just above the viewport). The header is fixed from 1024 (AppShell), so axe never pairs it with page targets.
      if (allOpen) {
        await page.evaluate(() => window.scrollTo(0, 0));
        const straddles = await page.evaluate(() => {
          const hb = document.querySelector("[data-site-header]")!.getBoundingClientRect();
          const controls = Array.from(document.querySelectorAll<HTMLElement>("[data-site-header] a[href], [data-site-header] button")).map((e) => e.getBoundingClientRect());
          return Array.from(document.querySelectorAll<HTMLElement>("main a[href], main button"))
            .filter((el) => !el.closest(".review-rail"))
            .flatMap((el) => {
              const b = el.getBoundingClientRect();
              if (b.width === 0 || b.height === 0 || !controls.some((w) => b.left < w.right && b.right > w.left)) return [];
              return [{ name: (el.getAttribute("aria-label") ?? el.textContent ?? "").trim().slice(0, 30), scrollTo: Math.round(b.bottom + window.scrollY - hb.bottom - 4) }];
            });
        });
        if (straddles.length === 0) problems.push("straddle check found no target under a header control");
        for (const s of straddles) {
          const y = Math.max(0, Math.min(total, s.scrollTo));
          await page.evaluate((top) => window.scrollTo(0, top), y);
          await page.waitForTimeout(30);
          const axe = await new AxeBuilder({ page }).withRules(["target-size"]).analyze();
          for (const v of axe.violations) problems.push(`straddle "${s.name}" (scroll ${y}): axe ${v.id}: ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`);
        }
        measured.push(`${where}: ${straddles.length} header straddles`);
      }
      measured.push(`${where}: scanned 0-${total}px`);
      expect(problems, where).toEqual([]);
      await close();
    }
  }
  test.info().annotations.push({ type: "slice-8c", description: measured.join("; ") });
  console.log(`[slice 8c] ${measured.join("; ")}`);
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
    await settle(page, prefs);
    out.container = await box(header(page));
    out.header = await box(page.locator("[data-site-header]"));
    out.nav = await box(page.getByRole("navigation", { name: "Primary" }));
    out.main = await landmark(page.locator("main"));
    // Slice 4: Today has no card any more; the 12th landmark is the Next step section (margin label + block).
    out.nextStep = await landmark(page.locator("section[aria-labelledby=\"next-step-heading\"]"));
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
    await expect(page.getByLabel(/Course name/)).toBeVisible();
    await settle(page, prefs);
    out.field = await box(page.getByLabel(/Course name/));
    await close();
  }
  {
    const { page, close } = await open(browser, "/dev/lecture-harness/live-quick-recap", width, { prefs });
    await expect(page.getByText(/^Fixture:/)).toBeVisible();
    await settle(page, { focusMode: false, ...prefs });
    out.transcript = await landmark(page.locator(".transcript-panel"));
    out.quickRecap = await landmark(page.locator("#lecture-quick-recap"));
    out.tldr = await landmark(page.locator("#lecture-tldr"));
    out.pill = await box(page.getByTestId("state-label").locator("xpath=ancestor::div[contains(@class,'rounded-full')]"));
    await close();
  }
  {
    const { page, close } = await open(browser, "/dev/lecture-harness/live-quick-recap", width, { prefs: { ...prefs, focusMode: true } });
    await expect(page.getByText(/^Fixture:/)).toBeVisible();
    await settle(page, { ...prefs, focusMode: true });
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
        // Slice 7: Gamer and Glass are measured with their switch off (the default) and on (Grid texture / Solid surfaces).
        const variants: Record<string, unknown>[] =
          skin === "gamer" ? [{ skin, theme }, { skin, theme, texture: true }] : skin === "glass" ? [{ skin, theme }, { skin, theme, solid: true }] : [{ skin, theme }];
        for (const prefs of variants) {
        const got = await landmarks(browser, width, prefs);
        for (const [k, r] of Object.entries(base)) {
          const g = got[k];
          // Calm's roomier card padding (the one sanctioned layout exception, 32 instead of 24) moves what sits inside a
          // padded sheet 8px inward. Since slice 4 only the Add a course field sits in one (Today has no cards), so the
          // allowance covers the field alone; every other landmark, main included, must match in Calm too.
          const calm = skin === "calm";
          const insideCard = k === "field";
          // A wider heading face may add lines to a .display element inside the landmark; the landmark may then grow by at
          // most those lines (and must not shrink). Every other height change is a layout difference and fails.
          const extraHeading = Math.max(0, (g.headingPx ?? 0) - (r.headingPx ?? 0));
          const dh = g.height - r.height;
          const heightOk = near(dh, 0) || (extraHeading > 0 && dh > 0 && dh <= extraHeading + 1);
          const calmShift = near(g.x - r.x, 8) && (k === "field" ? near(r.width - g.width, 16) : near(r.width, g.width));
          const okX = (calm && insideCard && calmShift) || (near(r.x, g.x) && near(r.width, g.width));
          const okY = (calm && insideCard) || (near(r.y, g.y) && heightOk);
          if (!(okX && okY)) {
            diffs.push(`${JSON.stringify(prefs)} ${k}: ${fmt(g)} vs paper ${fmt(r)}`);
            if (k === "main") diffs.push(`  main blocks ${JSON.stringify(prefs)}: ${diag[`${width}:${JSON.stringify(prefs)}`]} | paper: ${diag[`${width}:${JSON.stringify({ skin: "paper", theme: "light" })}`]}`);
          }
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
      await settle(page, { skin, theme: "light" });
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
