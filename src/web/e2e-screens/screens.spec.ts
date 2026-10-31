import AxeBuilder from "@axe-core/playwright";
import { expect, request, test, type Browser, type Page } from "@playwright/test";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

const ORIGIN = "http://localhost:3000";
const OUT = path.join(__dirname, "..", "test-results", "redesign-screens");
const RUN = Date.now().toString(36);

const LOOKS = {
  paper: { theme: "light", quiet: false },
  night: { theme: "dark", quiet: false },
  fog: { theme: "light", quiet: true },
  dusk: { theme: "dark", quiet: true },
} as const;
type Look = keyof typeof LOOKS;
const WIDTHS = [390, 1280] as const;
// The ui-kit is captured in all 12 palettes (redesign-round2 slice 2: every primitive in every look, with axe). The
// stored skin selects the palette; Fog / Dusk are Paper + Low-stimulation.
const ALL_LOOKS = {
  ...LOOKS,
  "portal-light": { skin: "portal", theme: "light", quiet: false },
  "portal-dark": { skin: "portal", theme: "dark", quiet: false },
  "gamer-light": { skin: "gamer", theme: "light", quiet: false },
  "gamer-dark": { skin: "gamer", theme: "dark", quiet: false },
  "calm-light": { skin: "calm", theme: "light", quiet: false },
  "calm-dark": { skin: "calm", theme: "dark", quiet: false },
  "glass-light": { skin: "glass", theme: "light", quiet: false },
  "glass-dark": { skin: "glass", theme: "dark", quiet: false },
} as const;
type AnyLook = keyof typeof ALL_LOOKS;
const ALL_PALETTES = ["ui-kit"];

type Ctx = { courseId: string; courseName: string };
interface Screen {
  name: string;
  auth: "none" | "full" | "empty";
  url: (c: Ctx) => string;
  ready: (p: Page, c: Ctx) => Promise<void>;
  /** Optional interaction after load (e.g. open a panel). */
  act?: (p: Page) => Promise<void>;
  /** Extra stored preferences for this screen (e.g. Focus mode). */
  prefs?: Record<string, unknown>;
}

const h1 = (p: Page, name: string | RegExp) => expect(p.getByRole("heading", { level: 1, name })).toBeVisible();
const fixture = (p: Page) => expect(p.getByText(/^Fixture:/)).toBeVisible();

const SCREENS: Screen[] = [
  { name: "signin", auth: "none", url: () => "/signin", ready: (p) => h1(p, "Sign in") },
  { name: "dashboard-empty", auth: "empty", url: () => "/", ready: (p) => expect(p.getByRole("link", { name: "Add your first course" })).toBeVisible() },
  { name: "dashboard", auth: "full", url: () => "/", ready: (p) => expect(p.getByRole("heading", { name: "Your courses" })).toBeVisible() },
  {
    name: "palette",
    auth: "full",
    url: () => "/",
    ready: (p) => expect(p.getByRole("heading", { name: "Your courses" })).toBeVisible(),
    act: async (p) => {
      await p.keyboard.press("Control+k");
      await expect(p.getByRole("dialog", { name: "Search or jump" })).toBeVisible();
      await p.getByRole("combobox").fill("bio");
      await expect(p.getByRole("option").first()).toBeVisible();
    },
  },
  {
    name: "preferences",
    auth: "full",
    url: () => "/",
    ready: (p) => expect(p.getByRole("heading", { name: "Your courses" })).toBeVisible(),
    act: async (p) => {
      await p.getByRole("button", { name: "Preferences" }).click();
      await expect(p.getByRole("region", { name: "Preferences" })).toBeVisible();
    },
  },
  { name: "courses", auth: "full", url: () => "/courses", ready: (p, c) => expect(p.getByText(c.courseName).first()).toBeVisible() },
  { name: "course-new", auth: "full", url: () => "/courses/new", ready: (p) => expect(p.getByLabel(/Course name/)).toBeVisible() },
  { name: "course-detail", auth: "full", url: (c) => `/courses/${c.courseId}?added=1`, ready: (p, c) => h1(p, c.courseName) },
  { name: "consent", auth: "full", url: (c) => `/courses/${c.courseId}/lecture`, ready: (p, c) => h1(p, `Start lecture: ${c.courseName}`) },
  { name: "lecture-live", auth: "none", url: () => "/dev/lecture-harness/live", ready: fixture },
  {
    name: "lecture-quick-recap",
    auth: "none",
    url: () => "/dev/lecture-harness/live-quick-recap",
    ready: fixture,
    act: async (p) => {
      await p.getByRole("button", { name: /\(your usual\)/ }).click();
      await expect(p.getByRole("heading", { name: "Here is what was said" })).toBeFocused();
      await p.locator(":focus").evaluate((el) => (el as HTMLElement).blur());
    },
  },
  { name: "lecture-focus", auth: "none", url: () => "/dev/lecture-harness/live-quick-recap", ready: fixture, prefs: { focusMode: true } },
  { name: "lecture-paused", auth: "none", url: () => "/dev/lecture-harness/paused", ready: fixture },
  { name: "lecture-disconnected", auth: "none", url: () => "/dev/lecture-harness/disconnected", ready: fixture },
  { name: "lecture-mismatch", auth: "none", url: () => "/dev/lecture-harness/mismatch", ready: fixture },
  { name: "lecture-stopped", auth: "none", url: () => "/dev/lecture-harness/stopped", ready: fixture },
  { name: "lecture-mic-denied", auth: "none", url: () => "/dev/lecture-harness/mic-denied", ready: fixture },
  { name: "ui-kit", auth: "none", url: () => "/dev/ui-kit", ready: (p) => h1(p, /UI kit/) },
];

const only = process.env.SCREENS?.split(",").filter(Boolean);
const looks = (process.env.LOOKS?.split(",").filter(Boolean) as Look[] | undefined) ?? (Object.keys(LOOKS) as Look[]);
const extra = process.env.EXTRA_SCREENS === "1";

async function login(email: string, file: string) {
  const ctx = await request.newContext({ baseURL: ORIGIN, extraHTTPHeaders: { Origin: ORIGIN } });
  const res = await ctx.post("/api/v1/dev/login", { data: { email } });
  expect(res.status(), `dev login ${email}`).toBe(204);
  await ctx.storageState({ path: file });
  return ctx;
}

const ctx: Ctx = { courseId: "", courseName: "" };
const states = { full: path.join(OUT, `.auth-full-${RUN}.json`), empty: path.join(OUT, `.auth-empty-${RUN}.json`) };

test.beforeAll(async () => {
  mkdirSync(OUT, { recursive: true });
  const full = await login(`screens-${RUN}@dev.local`, states.full);
  const courses = [
    { name: "Biology 101", code: "BIO 101", term: "Fall 2026", instructor: "Dr. Rivera" },
    { name: "History of Law", code: "HIST 340", term: "Fall 2026", instructor: "Prof. Okafor" },
    { name: "Calculus II", code: "MATH 152", term: "Fall 2026" },
  ];
  for (const c of courses) {
    const r = await full.post("/api/v1/courses", { data: c });
    expect(r.status()).toBe(201);
    if (c.name === "Biology 101") ctx.courseId = ((await r.json()) as { id: string }).id;
  }
  ctx.courseName = "Biology 101";
  await full.dispose();
  await (await login(`screens-empty-${RUN}@dev.local`, states.empty)).dispose();
});

const report: Record<string, string[]> = {};
// The WCAG 2.5.3 label-in-name issue on Quick recap was fixed (2026-10-29); nothing is exempt any more.
const KNOWN_RULES = new Set<string>();
const known: Record<string, string[]> = {};

async function shoot(browser: Browser, s: Screen, look: AnyLook, width: number) {
  const context = await browser.newContext({
    viewport: { width, height: width < 600 ? 844 : 800 },
    colorScheme: ALL_LOOKS[look].theme,
    storageState: s.auth === "none" ? undefined : states[s.auth],
  });
  const page = await context.newPage();
  await page.addInitScript((prefs) => {
    try {
      window.localStorage.setItem("tether.prefs.v1", JSON.stringify(prefs));
    } catch {
      /* ignore */
    }
  }, { ...ALL_LOOKS[look], ...(s.prefs ?? {}) });
  await page.goto(s.url(ctx));
  await s.ready(page, ctx);
  if (s.act) await s.act(page);
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(OUT, `${s.name}--${width}--${look}.png`), fullPage: true });
  // Full-page captures stretch the viewport, which misplaces fixed elements (the Quick recap dock): also keep what a phone
  // actually shows, scrolled so the transcript is in view.
  if (s.name.startsWith("lecture") && width < 600) {
    const log = page.locator("#lecture-transcript");
    if ((await log.count()) > 0) await log.scrollIntoViewIfNeeded({ timeout: 5000 }).catch(() => undefined);
    await page.screenshot({ path: path.join(OUT, `${s.name}--${width}--${look}--viewport.png`) });
  }
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa", "best-practice"]).analyze();
  // 320 CSS px reflow (SC 1.4.10): no horizontal page scroll.
  if (width < 600) {
    await page.setViewportSize({ width: 320, height: 640 });
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    if (overflow > 0) r.violations.push({ id: "reflow-320", impact: "serious", nodes: [{ target: [`overflow ${overflow}px`] }] } as unknown as (typeof r.violations)[number]);
  }
  const fmt = (v: (typeof r.violations)[number]) => `${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`;
  report[`${s.name}--${width}--${look}`] = r.violations.filter((v) => !KNOWN_RULES.has(v.id)).map(fmt);
  const k = r.violations.filter((v) => KNOWN_RULES.has(v.id)).map(fmt);
  if (k.length) known[`${s.name}--${width}--${look}`] = k;
  await context.close();
}

for (const s of SCREENS.filter((x) => !only || only.includes(x.name))) {
  test(`screens: ${s.name}`, async ({ browser }) => {
    const perScreen: AnyLook[] = ALL_PALETTES.includes(s.name) && !process.env.LOOKS ? (Object.keys(ALL_LOOKS) as AnyLook[]) : looks;
    for (const width of WIDTHS) for (const look of perScreen) await shoot(browser, s, look, width);
    const bad = Object.entries(report).filter(([k, v]) => k.startsWith(`${s.name}--`) && v.length > 0);
    expect.soft(bad).toEqual([]);
  });
}

test.afterAll(() => {
  writeFileSync(path.join(OUT, `axe-report${only ? "-partial" : ""}.json`), JSON.stringify({ violations: report, knownEscalated: known }, null, 2));
  void extra;
});
