import AxeBuilder from "@axe-core/playwright";
import { chromium, expect, request, test, type Browser, type Page } from "@playwright/test";
import { mkdirSync } from "node:fs";
import path from "node:path";
import { contrastRatio } from "../src/lib/design/contrast";
import { settle } from "./settle";

// Redesign-round2 slice 8 (= themes T5): the lecture family under the ten looks (5 themes x light / dark) at 390 and 1280,
// in a real browser (headless Chromium, the clean `npm run dev:real` on :3000; lecture states from the dev-only harness:
// synthetic data, no microphone; the consent page and the reopen route on the real API with a synthetic course and a
// session that is started and stopped through the API with no audio). For every screen x width x look:
//  - a full-page capture `slice8-<screen>--<width>--<look>.png` (390 also `--viewport`), and axe with wcag2a/aa/21a/21aa/
//    22aa/best-practice (color-contrast included) at scroll 0;
//  - redesign-round2 3.3 / themes-spec 4, asserted, not eyeballed:
//    1. transcript text: ink on the opaque sheet surface, size 17 / line-height 28, the same face, the same timestamp
//       column in every look; transcript text contrast >= 4.5 on the surface (and the times on surface and canvas);
//    2. the synthesis size floor (review 5.1): every visible `.synthesis` is larger than the verbatim transcript size;
//    3. the recording pill and the bar: the same boxes as Paper light (within 1px), the pill's colours identical across the
//       five themes within a mode while Recording or Paused (status roles are shared per mode; the dark fills are each
//       theme's `raised` by design, so the fill is held by contrast instead), its text >= 4.5 on its
//       fill, its border >= 3 on the bar, nothing dimmed (opacity 1, no filter) up its ancestor chain; the bar opaque,
//       equal to the surface, with no blur or image;
//    4. the lecture text surfaces (transcript lines, TL;DR, Quick recap result, notes, notices, consent / mic-blocked
//       headings) opaque, with no texture, gradient, blur, filter or opacity up their ancestor chain;
//    5. the same live regions as Paper light (themes add none) and, on lecture screens, the three regions of Announcer;
//    6. the lecture landmarks (bar, pill, notice slots, transcript sheet, TL;DR, Quick recap, notes, recap result, search
//       field, headings, cards) equal to Paper light within 1px, except heading-face wrap above or inside an element (the
//       landmark gate's one allowance) and Calm's sanctioned card padding inside a `.card` (consent, mic blocked);
//    7. from 1280, every timestamp's and lead citation's ink inside the 5rem margin track and flush to its end (review 1.2).
// Then the Low-stimulation overlay on every theme: the pill, the bar and the safety-note fill are unchanged (never dimmed).
// Run: npx playwright test -c playwright.screens.config.ts e2e-screens/lecture-looks.spec.ts --workers=1

const ORIGIN = "http://localhost:3000";
const OUT = path.join(__dirname, "..", "test-results", "redesign-screens");
const RUN = `l8-${Date.now().toString(36)}`;
const SKINS = ["paper", "portal", "gamer", "calm", "glass"] as const;
const MODES = ["light", "dark"] as const;
const LOOKS = SKINS.flatMap((skin) => MODES.map((theme) => ({ id: `${skin}-${theme}`, prefs: { skin, theme } as Record<string, unknown> })));
const WIDTHS = [390, 1280] as const;

const ctx = { courseId: "", courseName: "Biology 101", sessionId: "", state: path.join(OUT, `.auth-${RUN}.json`) };

let browser: Browser;
test.beforeAll(async () => {
  // As geometry.spec.ts: scrollbars shown on desktop (real gutter), phone emulation below 640.
  browser = await chromium.launch({ ignoreDefaultArgs: ["--hide-scrollbars"] });
  mkdirSync(OUT, { recursive: true });
  const c = await request.newContext({ baseURL: ORIGIN, extraHTTPHeaders: { Origin: ORIGIN } });
  expect((await c.post("/api/v1/dev/login", { data: { email: `${RUN}@dev.local` } })).status(), "dev login").toBe(204);
  const r = await c.post("/api/v1/courses", { data: { name: ctx.courseName, code: "BIO 101", term: "Fall 2026" } });
  expect(r.status()).toBe(201);
  ctx.courseId = ((await r.json()) as { id: string }).id;
  // The reopen route needs a real stopped session: started with the consent attestation the start flow sends (draft
  // version), never fed audio (no microphone, no transcript), and stopped at once. Nothing is sent to an AI: the page
  // only reads (GET notes answers 409 "not pressed"), and this spec never presses Make notes.
  const s = await c.post(`/api/v1/courses/${ctx.courseId}/sessions`, {
    headers: { "Idempotency-Key": `${RUN}-session` },
    data: { consent: { acknowledged: true, textVersion: "draft-2" } },
  });
  expect(s.status(), `start session: ${await s.text()}`).toBe(201);
  ctx.sessionId = ((await s.json()) as { id: string }).id;
  expect((await c.post(`/api/v1/sessions/${ctx.sessionId}/stop`)).status()).toBeLessThan(300);
  await c.storageState({ path: ctx.state });
  await c.dispose();
});
test.afterAll(async () => browser?.close());

interface Screen {
  name: string;
  url: () => string;
  auth?: boolean;
  prefs?: Record<string, unknown>;
  ready: (p: Page) => Promise<void>;
  act?: (p: Page) => Promise<void>;
  /** Extra per-look checks on the loaded page (problems as strings). */
  check?: (p: Page, where: string) => Promise<string[]>;
}
let reopenReloads = 0;
const fixture = (p: Page) => expect(p.getByText(/^Fixture:/)).toBeVisible();
const SCREENS: Screen[] = [
  { name: "live", url: () => "/dev/lecture-harness/live", ready: fixture },
  { name: "paused", url: () => "/dev/lecture-harness/paused", ready: fixture },
  { name: "stopped", url: () => "/dev/lecture-harness/stopped", ready: async (p) => (await fixture(p), await expect(p.locator("#lecture-notes")).toBeVisible()) },
  { name: "notes-ready", url: () => "/dev/lecture-harness/stopped-notes-ready", ready: (p) => expect(p.locator("#lecture-notes.lecture-sheet")).toBeVisible() },
  {
    name: "quick-recap",
    url: () => "/dev/lecture-harness/live-quick-recap",
    ready: fixture,
    act: async (p) => {
      await p.getByRole("button", { name: /\(your usual\)/ }).click();
      await expect(p.getByRole("heading", { name: "Here is what was said" })).toBeFocused();
      await p.locator(":focus").evaluate((el) => (el as HTMLElement).blur());
    },
  },
  { name: "disconnected", url: () => "/dev/lecture-harness/disconnected", ready: fixture },
  { name: "mismatch", url: () => "/dev/lecture-harness/mismatch", ready: fixture },
  { name: "mic-denied", url: () => "/dev/lecture-harness/mic-denied", ready: fixture },
  { name: "consent", url: () => `/courses/${ctx.courseId}/lecture`, auth: true, ready: (p) => expect(p.getByRole("heading", { level: 1, name: `Start lecture: ${ctx.courseName}` })).toBeVisible() },
  { name: "focus", url: () => "/dev/lecture-harness/live-quick-recap", prefs: { focusMode: true }, ready: fixture },
  {
    // PO-19 (stop-redirect.md criterion 11): the move notice open over the Stopped screen, reached through the UI (Stop in
    // the bar, Stop in the confirm) on the `live` fixture, which renders as the live route. Axe and every invariant run
    // with it open; the landmarks compared with Paper light are the page behind it (the notice's own box follows each
    // theme's heading face and Calm's card padding, so it is checked per look instead: inside the screen, controls at
    // least 44px, the primary focused, no horizontal overflow, the pill behind it reading Stopped).
    name: "stop-move",
    url: () => "/dev/lecture-harness/live",
    ready: async (p) => (await fixture(p), await expect(p.getByTestId("state-label")).toHaveText("Recording")),
    act: async (p) => {
      await p.getByRole("button", { name: "Stop", exact: true }).click();
      await p.getByRole("dialog", { name: "Stop and save this lecture?" }).getByRole("button", { name: "Stop", exact: true }).click();
      await expect(p.getByRole("dialog", { name: "This lecture has its own page" })).toBeVisible();
      await expect(p.getByRole("button", { name: "Open the lecture's page" })).toBeFocused();
    },
    check: async (p, where) =>
      (
        await p.evaluate(() => {
          const out: string[] = [];
          const d = document.querySelector<HTMLElement>("[data-testid=stop-move-dialog]");
          if (!d) return ["the move notice is not open"];
          const r = d.getBoundingClientRect();
          if (r.top < 15.5 || r.bottom > innerHeight - 15.5) out.push(`notice ${Math.round(r.top)}..${Math.round(r.bottom)} outside the screen ${innerHeight}`);
          for (const t of [...Array.from(d.querySelectorAll("button")), d.querySelector("input")!.closest("label")!]) {
            const b = t.getBoundingClientRect();
            if (b.height < 43.5 || b.width < 43.5) out.push(`"${(t.textContent ?? "").trim()}" ${Math.round(b.width)}x${Math.round(b.height)}`);
          }
          if ((document.activeElement?.textContent ?? "").trim() !== "Open the lecture's page") out.push(`focus on "${document.activeElement?.textContent}"`);
          if (document.querySelector("[data-testid=state-label]")?.textContent !== "Stopped") out.push("pill not Stopped");
          if (document.documentElement.scrollWidth > document.documentElement.clientWidth) out.push("horizontal overflow");
          return out;
        })
      ).map((x) => `${where}: ${x}`),
  },
  {
    name: "reopen",
    url: () => `/lectures/${ctx.sessionId}`,
    auth: true,
    ready: async (p) => {
      // The label is "Not recording" until the hub has joined (deriveRecordingIndicator: NotConnected). Slice 8 saw it stay
      // so (4 reloads in 20 loads): StrictMode left an orphaned second hub socket per page, and with the previous load's
      // sockets the joining one went over the per-user hub cap. Fixed in slice 8x (hubTransport.ts epoch; e2e/reopen.spec.ts
      // asserts it). Reloads are still needed here for a different, server-side reason measured in slice 8x: this spec
      // loads the same session's reopen route 20 times in about 70s, and the hub allows 10 Joins per minute per (user,
      // session) (C-26 / B2-02), so the 11th Join onwards is `rate_limited` until the minute passes. Counted and printed.
      const label = p.getByTestId("state-label");
      for (let attempt = 0; ; attempt++) {
        try {
          await expect(label).toHaveText("Stopped", { timeout: 10_000 });
          break;
        } catch (e) {
          if (attempt >= 4) throw e;
          reopenReloads++;
          await p.reload();
        }
      }
      await expect(p.locator("#lecture-notes")).toBeVisible();
    },
  },
];

async function open(width: number, s: Screen, prefs: Record<string, unknown>) {
  const phone = width < 640;
  const context = await browser.newContext({
    viewport: { width, height: phone ? 844 : 800 },
    isMobile: phone,
    hasTouch: phone,
    colorScheme: prefs.theme as "light" | "dark",
    storageState: s.auth ? ctx.state : undefined,
  });
  const page = await context.newPage();
  await page.addInitScript((v) => window.localStorage.setItem("tether.prefs.v1", JSON.stringify(v)), { ...prefs, ...(s.prefs ?? {}) });
  await page.goto(`${ORIGIN}${s.url()}`);
  await s.ready(page);
  // Hydrate first: a click on server-rendered markup before React attaches its handlers does nothing (CI: the Quick recap
  // 'Here is what was said' heading never appeared, in screens.spec and lecture-looks.spec).
  await settle(page, { focusMode: false, ...prefs, ...(s.prefs ?? {}) });
  if (s.act) await s.act(page);
  // Hydrated, prefs applied, every used face loaded, boxes stable (settle.ts; the failing look varied between CI runs).
  await settle(page, { focusMode: false, ...prefs, ...(s.prefs ?? {}) });
  await page.waitForTimeout(300);
  return { page, close: () => context.close() };
}

interface Box { x: number; y: number; w: number; h: number; headIn: number; headAbove: number; inCard: boolean; isHead: boolean }
interface Probe {
  boxes: Record<string, Box>;
  transcript: { size: string; line: string; family: string; columns: string; ink: string; inkIsInk: boolean; bg: string; bgIsSurface: boolean; time: string; surface: string; canvas: string } | null;
  rows: string[];
  synthesis: { size: number; text: string }[];
  pill: { label: string; color: string; bg: string; border: string; family: string; barBg: string; dimmed: string[] } | null;
  bar: { bg: string; surface: string; filter: string; image: string } | null;
  surfaces: string[];
  live: string[];
  regions: string[];
  margin: string[];
}

/** Everything measured in the page (one evaluate, so the numbers come from one layout). */
const probe = (page: Page, width: number): Promise<Probe> =>
  page.evaluate((width) => {
    const cs = (el: Element, pseudo?: string) => getComputedStyle(el, pseudo);
    const visible = (el: Element) => {
      const r = el.getBoundingClientRect();
      return r.width > 0 && r.height > 0 && cs(el).visibility !== "hidden";
    };
    // Heading-face text: each .display / .synthesis element with its rendered line count x line-height.
    const heads = Array.from(document.querySelectorAll<HTMLElement>(".display, .synthesis")).filter(visible).map((h) => {
      const range = document.createRange();
      range.selectNodeContents(h);
      const tops = new Set(Array.from(range.getClientRects()).map((r) => Math.round(r.top)));
      const r = h.getBoundingClientRect();
      return { el: h, top: r.top + scrollY, bottom: r.bottom + scrollY, px: Math.max(1, tops.size) * (parseFloat(cs(h).lineHeight) || 0) };
    });
    const resolve = (prop: string, value: string) => {
      const p = document.createElement("div");
      p.style.setProperty(prop, value);
      (document.querySelector("[data-lecture-root]") ?? document.querySelector("main") ?? document.body).appendChild(p);
      const v = cs(p).getPropertyValue(prop);
      p.remove();
      return v;
    };
    const surface = resolve("background-color", "var(--c-surface)");
    const canvas = resolve("background-color", "var(--c-canvas)");
    const inkColour = resolve("color", "var(--c-ink)");

    const boxes: Record<string, { x: number; y: number; w: number; h: number; headIn: number; headAbove: number; inCard: boolean; isHead: boolean }> = {};
    const add = (key: string, el: Element | null) => {
      if (!el || !visible(el)) return;
      const r = el.getBoundingClientRect();
      const top = r.top + scrollY;
      boxes[key] = {
        x: r.left + scrollX,
        y: top,
        w: r.width,
        h: r.height,
        headIn: heads.filter((h) => el.contains(h.el)).reduce((a, h) => a + h.px, 0),
        headAbove: heads.filter((h) => !el.contains(h.el) && h.bottom <= top + 1).reduce((a, h) => a + h.px, 0),
        inCard: !!el.closest(".card") && !el.matches(".card"),
        isHead: el.matches(".display, .synthesis"),
      };
    };
    const pillEl = document.querySelector("[data-testid=state-label]")?.closest(".rounded-full") ?? null;
    add("bar", document.querySelector("[data-lecture-status-bar]"));
    add("pill", pillEl);
    add("layout", document.querySelector("[data-layout]"));
    document.querySelectorAll(".notice-slot > *").forEach((n, i) => add(`notice${i}`, n));
    add("transcript", document.querySelector(".transcript-panel"));
    add("firstLine", document.querySelector(".transcript-line"));
    add("tldr", document.getElementById("lecture-tldr"));
    add("quickRecap", document.getElementById("lecture-quick-recap"));
    add("notes", document.getElementById("lecture-notes"));
    add("recap", document.querySelector("[data-testid=catchup-result]"));
    add("search", document.getElementById("lecture-search"));
    add("h1", document.querySelector("main h1"));
    add("card", document.querySelector("main section.card"));
    add("primary", document.querySelector("main .btn-primary"));

    // 1. Transcript text.
    let transcript: Probe["transcript"] = null;
    const line = document.querySelector<HTMLElement>(".transcript-line");
    if (line) {
      const words = line.children[1] as HTMLElement;
      const ws = cs(words);
      // The painted surface under the words: the first background up the chain, the lecture sheet's ::before included.
      let bg = "";
      for (let el: HTMLElement | null = words; el && !bg; el = el.parentElement) {
        const b = cs(el).backgroundColor;
        if (b !== "rgba(0, 0, 0, 0)") bg = b;
        else if (el.classList.contains("lecture-sheet") && cs(el, "::before").content !== "none") {
          const pb = cs(el, "::before").backgroundColor;
          if (pb !== "rgba(0, 0, 0, 0)") bg = pb;
        }
      }
      transcript = {
        size: ws.fontSize,
        line: ws.lineHeight,
        family: ws.fontFamily,
        columns: cs(line).gridTemplateColumns,
        ink: ws.color,
        inkIsInk: ws.color === inkColour,
        bg,
        bgIsSurface: bg === surface,
        time: cs(line.querySelector(".transcript-time")!).color,
        surface,
        canvas,
      };
    }
    // 1b. Row boxes: a transcript row is exactly its words' box (whole 28px lines); the baseline-aligned time never adds
    // height whatever its face (CI run 41328040245: Gamer's mono time made rows 31px on Linux only).
    const rows = Array.from(document.querySelectorAll<HTMLElement>(".transcript-line")).slice(0, 4).flatMap((row, i) => {
      const words = (row.children[1] as HTMLElement).getBoundingClientRect().height;
      const h = row.getBoundingClientRect().height;
      return Math.abs(h - words) > 0.5 ? [`transcript row ${i} ${h}px vs its words ${words}px (the time's face leaks into the row)`] : [];
    });
    // 2. Synthesis.
    const synthesis = Array.from(document.querySelectorAll<HTMLElement>(".synthesis")).filter(visible).map((s) => ({ size: parseFloat(cs(s).fontSize), text: (s.textContent ?? "").trim().slice(0, 30) }));
    // 3. Pill and bar.
    let pill: Probe["pill"] = null;
    let bar: Probe["bar"] = null;
    const barEl = document.querySelector<HTMLElement>("[data-lecture-status-bar]");
    if (pillEl && barEl) {
      const ps = cs(pillEl);
      const dimmed: string[] = [];
      for (let el: Element | null = pillEl.querySelector("[data-testid=state-label]"); el; el = el.parentElement) {
        const s = cs(el);
        if (Number(s.opacity) < 1) dimmed.push(`opacity ${s.opacity} on ${el.tagName}`);
        if (s.filter !== "none") dimmed.push(`filter ${s.filter} on ${el.tagName}`);
        if (s.backdropFilter !== "none") dimmed.push(`backdrop-filter on ${el.tagName}`);
        if (s.mixBlendMode !== "normal") dimmed.push(`blend ${s.mixBlendMode} on ${el.tagName}`);
      }
      pill = {
        label: (document.querySelector("[data-testid=state-label]")!.textContent ?? "").trim(),
        color: cs(document.querySelector("[data-testid=state-label]")!).color,
        bg: ps.backgroundColor,
        border: ps.borderTopColor,
        family: cs(document.querySelector("[data-testid=state-label]")!).fontFamily,
        barBg: cs(barEl).backgroundColor,
        dimmed,
      };
      const bs = cs(barEl);
      bar = { bg: bs.backgroundColor, surface, filter: bs.backdropFilter, image: bs.backgroundImage };
    }
    // 4. Lecture text surfaces.
    const surfaces: string[] = [];
    const targets = Array.from(document.querySelectorAll<HTMLElement>("[data-segment-id], #lecture-tldr, [data-testid=catchup-result], #lecture-notes, .notice-slot > *, #consent-h1, #blocked-h1")).filter(visible);
    for (const t of targets) {
      const name = t.id || t.dataset.segmentId || t.className.toString().split(" ").slice(0, 2).join(".");
      let painted = "";
      for (let el: HTMLElement | null = t; el && el !== document.documentElement; el = el.parentElement) {
        const s = cs(el);
        if (s.backdropFilter !== "none") surfaces.push(`${name}: backdrop-filter on ${el.tagName}`);
        if (s.filter !== "none") surfaces.push(`${name}: filter ${s.filter} on ${el.tagName}`);
        if (Number(s.opacity) < 1) surfaces.push(`${name}: opacity ${s.opacity} on ${el.tagName}`);
        // Gamer's 12px x 1px corner ticks on a .card (mic blocked) are corners only, never under a line (slice 7).
        const ticks = el.classList.contains("card") && s.backgroundSize.startsWith("12px 1px");
        if (s.backgroundImage !== "none" && !ticks) surfaces.push(`${name}: background-image on ${el.tagName}.${el.className.toString().split(" ")[0]}`);
        if (!painted) {
          if (s.backgroundColor !== "rgba(0, 0, 0, 0)") painted = s.backgroundColor;
          else if (el.classList.contains("lecture-sheet") && cs(el, "::before").content !== "none" && cs(el, "::before").backgroundColor !== "rgba(0, 0, 0, 0)") painted = cs(el, "::before").backgroundColor;
          if (painted && !/^rgb\(/.test(painted)) surfaces.push(`${name}: first surface translucent ${painted} on ${el.tagName}`);
        }
      }
      if (!painted) surfaces.push(`${name}: no painted surface under it`);
    }
    // 5. Live regions (document only: Next's route announcer lives in a shadow root and is not ours).
    const live = Array.from(document.querySelectorAll("[role=status],[role=alert],[role=log],[aria-live]")).map(
      (e) => `${e.tagName.toLowerCase()}[${e.getAttribute("role") ?? ""}|${e.getAttribute("aria-live") ?? ""}|${e.getAttribute("data-region") ?? e.id ?? ""}]`,
    );
    const regions = Array.from(document.querySelectorAll("[data-region]")).map((e) => e.getAttribute("data-region")!);
    // 7. Margin track from 1280: time ink and lead citation boxes inside [column, column + 5rem], flush to its end.
    const margin: string[] = [];
    const col = document.querySelector("[data-layout]")?.getBoundingClientRect();
    if (width >= 1280 && col) {
      const end = col.left + 80;
      const check = (what: string, r: DOMRect) => {
        if (r.left < col.left - 0.5) margin.push(`${what} starts at ${r.left}, outside the margin track ${col.left}`);
        if (Math.abs(r.right - end) > 1) margin.push(`${what} ends at ${r.right}, not at the track end ${end}`);
      };
      document.querySelectorAll(".transcript-time > [aria-hidden]").forEach((t, i) => i < 3 && check(`time ${i}`, t.getBoundingClientRect()));
      // Every other line of text in a lecture sheet sits on the painted surface (starts at or after the sheet's paint,
      // margin + 40px): only the times and lead citations hang in the margin. (Found in slice 8: the empty-transcript
      // line on the reopen route started in the margin and ran across the sheet's border.)
      document.querySelectorAll<HTMLElement>("[data-lecture-root] .lecture-sheet").forEach((sheet) => {
        const paint = sheet.getBoundingClientRect().left + parseFloat(getComputedStyle(sheet, "::before").left || "0");
        const walker = document.createTreeWalker(sheet, NodeFilter.SHOW_TEXT);
        for (let n = walker.nextNode(); n; n = walker.nextNode()) {
          const host = n.parentElement!;
          if (!n.textContent?.trim() || host.closest(".transcript-time, .hang-row > :first-child, .sr-only")) continue;
          const range = document.createRange();
          range.selectNodeContents(n);
          const r = range.getBoundingClientRect();
          if (r.width > 0 && r.left < paint - 0.5) margin.push(`text "${n.textContent.trim().slice(0, 30)}" starts at ${Math.round(r.left)}, left of the sheet's paint ${Math.round(paint)}`);
        }
      });
      document.querySelectorAll("[data-lecture-root] .hang-row > :first-child .cite").forEach((c, i) => i < 3 && check(`lead citation ${i}`, c.getBoundingClientRect()));
    }
    return { boxes, transcript, rows, synthesis, pill, bar, surfaces, live, regions, margin };
  }, width);

const hex = (rgb: string) => {
  const m = /^rgba?\((\d+),\s*(\d+),\s*(\d+)/.exec(rgb);
  if (!m) throw new Error(`not an rgb colour: ${rgb}`);
  return "#" + [m[1], m[2], m[3]].map((v) => Number(v).toString(16).padStart(2, "0")).join("");
};
const ratio = (a: string, b: string) => contrastRatio(hex(a), hex(b));

/** The per-look invariants that do not need a reference look. */
function invariants(p: Probe, where: string): string[] {
  const out: string[] = [];
  if (p.transcript) {
    const t = p.transcript;
    if (t.size !== "17px" || t.line !== "28px") out.push(`${where}: transcript ${t.size}/${t.line} (17/28 expected)`);
    if (!t.inkIsInk) out.push(`${where}: transcript text ${t.ink} is not the ink role`);
    if (!t.bgIsSurface) out.push(`${where}: transcript surface ${t.bg} is not the surface role ${t.surface}`);
    if (ratio(t.ink, t.bg) < 4.5) out.push(`${where}: transcript contrast ${ratio(t.ink, t.bg).toFixed(2)}`);
    for (const [n, bg] of [["surface", t.surface], ["canvas", t.canvas]] as const) if (ratio(t.time, bg) < 4.5) out.push(`${where}: timestamp on ${n} ${ratio(t.time, bg).toFixed(2)}`);
  }
  const verbatim = p.transcript ? parseFloat(p.transcript.size) : 17;
  for (const s of p.synthesis) if (!(s.size > verbatim)) out.push(`${where}: synthesis "${s.text}" ${s.size}px is not larger than verbatim ${verbatim}px`);
  if (p.pill && p.bar) {
    if (ratio(p.pill.color, p.pill.bg) < 4.5) out.push(`${where}: pill text ${ratio(p.pill.color, p.pill.bg).toFixed(2)} on its fill`);
    if (ratio(p.pill.border, p.pill.barBg) < 3) out.push(`${where}: pill border ${ratio(p.pill.border, p.pill.barBg).toFixed(2)} on the bar`);
    out.push(...p.pill.dimmed.map((d) => `${where}: pill ${d}`));
    if (!/^rgb\(/.test(p.bar.bg) || p.bar.bg !== p.bar.surface) out.push(`${where}: bar ${p.bar.bg} is not the opaque surface ${p.bar.surface}`);
    if (p.bar.filter !== "none" || p.bar.image !== "none") out.push(`${where}: bar filter ${p.bar.filter} image ${p.bar.image}`);
  }
  out.push(...p.surfaces.map((s) => `${where}: ${s}`), ...p.margin.map((s) => `${where}: ${s}`), ...p.rows.map((s) => `${where}: ${s}`));
  if (p.regions.length) {
    // The lecture screen owns exactly three (status, alert, finals); the consent and mic-blocked screens have the
    // Announcer's two and no transcript.
    const want = p.boxes.transcript ? ["alert", "finals", "status"] : ["alert", "status"];
    if (JSON.stringify([...p.regions].sort()) !== JSON.stringify(want)) out.push(`${where}: live regions ${p.regions.join(",")} (${want.join(", ")} expected)`);
  }
  return out;
}

/** Against Paper light at the same width: boxes, transcript metrics, live regions, pill colours within a mode. */
function sameAsReference(p: Probe, ref: Probe, look: string, where: string): string[] {
  const out: string[] = [];
  const calm = look.startsWith("calm");
  for (const [k, r] of Object.entries(ref.boxes)) {
    const g = p.boxes[k];
    if (!g) {
      out.push(`${where}: ${k} missing (present in paper-light)`);
      continue;
    }
    // Calm's 32px card padding (the one sanctioned layout exception) moves what sits inside a .card 8px inward and makes
    // the card itself 16px taller (8 top + 8 bottom); nothing else may differ for Calm.
    if (calm && r.inCard) continue;
    // The 16px narrower text box inside may also wrap the card's text to more lines (seen: the consent card at 390,
    // +44px), so in Calm the card may only grow, by at least the 16px of padding; its x, y and width may not change.
    const calmCard = calm && k === "card";
    const above = Math.max(0, g.headAbove - r.headAbove);
    const inside = Math.max(0, g.headIn - r.headIn);
    const dy = g.y - r.y;
    const dh = g.h - r.h;
    const okY = Math.abs(dy) <= 1 || (above > 0 && dy > 0 && dy <= above + 1);
    const okH = calmCard ? dh >= 15 : Math.abs(dh) <= 1 || (inside > 0 && dh > 0 && dh <= inside + 1);
    // A heading-face element that sizes to its text (the consent / mic-blocked h1 beside its glyph) may be as wide as its
    // face makes it; its start edge may not move.
    const okW = Math.abs(g.w - r.w) <= 1 || r.isHead;
    if (Math.abs(g.x - r.x) > 1 || !okW || !okY || !okH)
      out.push(`${where}: ${k} ${Math.round(g.x)},${Math.round(g.y)} ${Math.round(g.w)}x${Math.round(g.h)} vs paper-light ${Math.round(r.x)},${Math.round(r.y)} ${Math.round(r.w)}x${Math.round(r.h)} (heading px above +${above}, inside +${inside})`);
  }
  for (const k of Object.keys(p.boxes)) if (!ref.boxes[k]) out.push(`${where}: ${k} present only in this look`);
  // The pill and the bar never move or resize for a heading face: no allowance at all.
  for (const k of ["bar", "pill"]) {
    const g = p.boxes[k];
    const r = ref.boxes[k];
    if (g && r && (Math.abs(g.y - r.y) > 1 || Math.abs(g.h - r.h) > 1)) out.push(`${where}: ${k} y/h ${g.y}/${g.h} vs ${r.y}/${r.h} (no allowance)`);
  }
  if (p.transcript && ref.transcript) {
    if (p.transcript.family !== ref.transcript.family) out.push(`${where}: transcript face ${p.transcript.family} vs ${ref.transcript.family}`);
    if (p.transcript.columns !== ref.transcript.columns) out.push(`${where}: transcript columns ${p.transcript.columns} vs ${ref.transcript.columns}`);
  }
  if (p.pill && ref.pill && p.pill.family !== ref.pill.family) out.push(`${where}: pill face ${p.pill.family} vs ${ref.pill.family}`);
  if (JSON.stringify(p.live) !== JSON.stringify(ref.live)) out.push(`${where}: live regions ${p.live.join(" ")} vs ${ref.live.join(" ")}`);
  return out;
}

const AXE_TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa", "best-practice"];
const measured: string[] = [];

for (const s of SCREENS) {
  test(`slice 8: ${s.name} under the ten looks at 390 and 1280: axe, invariants, landmarks equal to Paper light`, async () => {
    test.setTimeout(600_000);
    const problems: string[] = [];
    for (const width of WIDTHS) {
      let ref: Probe | null = null;
      // Pill colours per mode, keyed by mode, compared across the five themes while the state uses status roles only.
      const pillByMode: Record<string, { look: string; v: string }> = {};
      for (const look of LOOKS) {
        const where = `${s.name}@${width} ${look.id}`;
        const { page, close } = await open(width, s, look.prefs);
        await page.screenshot({ path: path.join(OUT, `slice8-${s.name}--${width}--${look.id}.png`), fullPage: true });
        if (width < 640) {
          const log = page.locator("#lecture-transcript");
          if ((await log.count()) > 0) await log.scrollIntoViewIfNeeded({ timeout: 5000 }).catch(() => undefined);
          await page.screenshot({ path: path.join(OUT, `slice8-${s.name}--${width}--${look.id}--viewport.png`) });
          // Axe at the load position (slice 8b Lead decision: a target half under the sticky bar at an arbitrary offset).
          await page.evaluate(() => window.scrollTo(0, 0));
          await page.waitForTimeout(100);
        }
        const p = await probe(page, width);
        const axe = await new AxeBuilder({ page }).withTags(AXE_TAGS).analyze();
        for (const v of axe.violations) problems.push(`${where}: axe ${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`);
        if (s.check) problems.push(...(await s.check(page, where)));
        await close();
        problems.push(...invariants(p, where));
        if (!ref) ref = p;
        else problems.push(...sameAsReference(p, ref, look.id, where));
        if (p.pill && (p.pill.label === "Recording" || p.pill.label === "Paused")) {
          const mode = look.id.split("-")[1];
          // The state colour (word, glyph, border) is shared per mode across the five themes. The fill is the look's own
          // `*-subtle` role: equal across themes in light, and by design each dark theme's `raised` (tokens.ts darkStatus),
          // so it is checked by contrast (above), not by equality.
          const v = `${p.pill.color} / ${p.pill.border}`;
          const first = pillByMode[mode];
          if (!first) pillByMode[mode] = { look: look.id, v };
          else if (first.v !== v) problems.push(`${where}: pill colours ${v} vs ${first.look} ${first.v} (status roles are shared per mode)`);
        }
        if (look.id === "paper-light" || look.id === "gamer-dark")
          measured.push(`${s.name}@${width} ${look.id}: ${Object.entries(p.boxes).map(([k, b]) => `${k} ${Math.round(b.w)}x${Math.round(b.h)}`).join(", ")}${p.transcript ? `; contrast ${ratio(p.transcript.ink, p.transcript.bg).toFixed(2)}` : ""}${p.synthesis.length ? `; synthesis ${p.synthesis.map((x) => x.size).join("/")}px` : ""}`);
      }
    }
    expect(problems, `${s.name}`).toEqual([]);
  });
}

// The transcript row cannot depend on the timestamp's face (CI run 41328040245: Gamer's mono time made every first row
// 31px instead of Paper's 29 on the Linux runner, while Windows rendered both alike, so the landmark gate failed on Linux
// only). Windows cannot reproduce Linux's metrics, so this emulates them: the time is re-set in faces with extreme ascent
// and descent (a same-origin webfont the page already uses, loaded again with ascent/descent overrides), a generic
// monospace and serif, and 11 / 20px sizes. In each, every row must stay exactly its words' box and the first row must
// keep the height it has unmodified. Deterministic on any OS; it failed on Windows before the fix (Paper rows 29 vs 28).
test("slice 8: the transcript row height does not depend on the timestamp's face or metrics (emulated wide fallback)", async () => {
  test.setTimeout(180_000);
  const problems: string[] = [];
  const variants: { name: string; css?: string; face?: { ascent: string; descent: string } }[] = [
    // Ascent minus descent of 1.0em and 0.4em: wider than the common monospace fallbacks on either side (DejaVu Sans Mono
    // about 0.69em, Liberation Mono 0.53, Noto Sans Mono 0.78, Consolas 0.49, JetBrains Mono 0.72). The 20px time box
    // keeps the row the words' 28px for faces in that range (globals.css .transcript-time).
    { name: "tall ascent", face: { ascent: "115%", descent: "15%" } },
    { name: "deep descent", face: { ascent: "75%", descent: "35%" } },
    { name: "generic monospace", css: "font-family: monospace !important;" },
    { name: "generic serif", css: "font-family: serif !important;" },
    { name: "11px", css: "font-size: 11px !important;" },
    { name: "20px", css: "font-size: 20px !important;" },
  ];
  const s = SCREENS.find((x) => x.name === "live")!;
  for (const width of WIDTHS) {
    for (const look of LOOKS.filter((l) => l.id === "paper-light" || l.id === "gamer-dark")) {
      const where = `face-independence@${width} ${look.id}`;
      const { page, close } = await open(width, s, look.prefs);
      const measure = () =>
        page.evaluate(() => {
          const rows = Array.from(document.querySelectorAll<HTMLElement>(".transcript-line")).slice(0, 4);
          return rows.map((r) => ({ h: r.getBoundingClientRect().height, words: (r.children[1] as HTMLElement).getBoundingClientRect().height }));
        });
      const base = await measure();
      if (base.length === 0) problems.push(`${where}: no transcript rows`);
      for (const v of variants) {
        const applied = await page.evaluate(async (v) => {
          document.getElementById("face-probe")?.remove();
          let decl = v.css ?? "";
          if (v.face) {
            // A webfont file the page already loaded (same origin, so the rules are readable), as a new face with overrides.
            let url = "";
            for (const sheet of Array.from(document.styleSheets)) {
              let rules: CSSRuleList;
              try {
                rules = sheet.cssRules;
              } catch {
                continue;
              }
              for (const r of Array.from(rules)) {
                const m = r instanceof CSSFontFaceRule ? /url\(["']?([^"')]+)["']?\)/.exec(r.style.getPropertyValue("src")) : null;
                if (m && !url) url = m[1];
              }
            }
            if (!url) return "no same-origin webfont found to re-load with overrides";
            const name = `FaceProbe${v.face.ascent.replace("%", "")}x${v.face.descent.replace("%", "")}`;
            const f = new FontFace(name, `url(${url})`, { ascentOverride: v.face.ascent, descentOverride: v.face.descent });
            await f.load();
            document.fonts.add(f);
            decl = `font-family: "${name}" !important;`;
          }
          const st = document.createElement("style");
          st.id = "face-probe";
          st.textContent = `.transcript-time { ${decl} }`;
          document.head.appendChild(st);
          await document.fonts.ready;
          const t = getComputedStyle(document.querySelector(".transcript-time")!);
          if (v.face && !t.fontFamily.includes("FaceProbe")) return `override not applied (${t.fontFamily})`;
          return "";
        }, v);
        if (applied) {
          problems.push(`${where} ${v.name}: ${applied}`);
          continue;
        }
        // The injected face reflows every row: wait for that layout to settle before measuring it (settle.ts).
        await settle(page, { focusMode: false, ...look.prefs, ...(s.prefs ?? {}) });
        const got = await measure();
        got.forEach((r, i) => {
          if (Math.abs(r.h - r.words) > 0.5) problems.push(`${where} ${v.name}: row ${i} ${r.h}px vs its words ${r.words}px`);
          if (base[i] && Math.abs(r.h - base[i].h) > 0.5) problems.push(`${where} ${v.name}: row ${i} ${r.h}px vs ${base[i].h}px unmodified`);
        });
      }
      base.forEach((r, i) => Math.abs(r.h - r.words) > 0.5 && problems.push(`${where} unmodified: row ${i} ${r.h}px vs its words ${r.words}px`));
      await close();
    }
  }
  expect(problems).toEqual([]);
});

// Low-stimulation on every theme (redesign-round2 3.3 item 6: safety surfaces never reduced; recording status never
// dimmed). On Portal, Gamer, Calm and Glass the overlay keeps the palette, so the pill, the bar and the safety-note fill
// must be the same values with it on. On Paper the overlay swaps the palette to Fog / Dusk ("On Paper the colours soften
// too", the shipped hint), whose `*-subtle` roles equal their surface or raised: there the pill and the notes keep their
// shape, word, 2px border / 4px stripe and contrast, and the measured differences are reported (slice 8 record), not
// hidden. Everywhere: pill box unchanged, opacity 1, no filter, text >= 4.5 on its fill, border >= 3 on the bar, and
// (slice 8x) the Recording pill fill and the Mismatch note fill identical on and off (recording-subtle is never flattened;
// the Paused fills on Paper stay reported: warning-subtle also paints G1 surfaces, Lead decision 1 on slice 8).
const paperQuiet: string[] = [];
test("slice 8: Low-stimulation on each of the ten looks never dims the pill or the bar and never removes a safety note's stripe", async () => {
  test.setTimeout(600_000);
  const problems: string[] = [];
  for (const name of ["live", "paused", "mismatch"] as const) {
    const s = SCREENS.find((x) => x.name === name)!;
    for (const look of LOOKS) {
      const read = async (quiet: boolean) => {
        const { page, close } = await open(1280, s, { ...look.prefs, quiet });
        const v = await page.evaluate(() => {
          const label = document.querySelector("[data-testid=state-label]")!;
          const pill = label.closest(".rounded-full")!;
          const ps = getComputedStyle(pill);
          const note = document.querySelector(".note-filled");
          const stripe = note ? getComputedStyle(note, "::before") : null;
          const r = pill.getBoundingClientRect();
          return {
            color: getComputedStyle(label).color,
            fill: ps.backgroundColor,
            border: ps.borderTopColor,
            borderW: ps.borderTopWidth,
            opacity: ps.opacity,
            filter: ps.filter,
            box: `${Math.round(r.x)},${Math.round(r.y)} ${Math.round(r.width)}x${Math.round(r.height)}`,
            note: note ? getComputedStyle(note).backgroundColor : "",
            stripe: stripe ? `${stripe.backgroundColor} ${stripe.width}` : "",
            bar: getComputedStyle(document.querySelector("[data-lecture-status-bar]")!).backgroundColor,
          };
        });
        await close();
        return v;
      };
      const on = await read(true);
      const off = await read(false);
      const where = `${name} ${look.id}`;
      if (on.box !== off.box) problems.push(`${where}: pill box ${on.box} with Low-stimulation vs ${off.box}`);
      if (on.opacity !== "1" || on.filter !== "none") problems.push(`${where}: pill opacity ${on.opacity} filter ${on.filter} with Low-stimulation`);
      if (on.borderW !== off.borderW) problems.push(`${where}: pill border ${on.borderW} vs ${off.borderW}`);
      if (!/^rgb\(/.test(on.bar)) problems.push(`${where}: bar ${on.bar} not opaque with Low-stimulation`);
      if (ratio(on.color, on.fill) < 4.5) problems.push(`${where}: pill text ${ratio(on.color, on.fill).toFixed(2)} on its fill with Low-stimulation`);
      if (ratio(on.border, on.bar) < 3) problems.push(`${where}: pill border ${ratio(on.border, on.bar).toFixed(2)} on the bar with Low-stimulation`);
      // The stripe stays an opaque 4px bar in the state colour (shape + word + stripe is the safety triple).
      if (name !== "live" && !/^rgb\([^)]*\) 4px$/.test(on.stripe))
        problems.push(`${where}: safety note stripe "${on.stripe}" with Low-stimulation (off: "${off.stripe}")`);
      // Slice 8x (Lead decision 1 on slice 8): the Recording pill's fill (and the Mismatch note's, the same role
      // recording-subtle) is identical with Low-stimulation on and off in EVERY look, Paper included (Fog took Paper's
      // value, Dusk has Night desk's). Before 8x this was only reported for Paper (Fog's fill equalled its surface).
      if (name === "live" && on.fill !== off.fill) problems.push(`${where}: Recording pill fill ${on.fill} with Low-stimulation vs ${off.fill}`);
      if (name === "mismatch" && on.note !== off.note) problems.push(`${where}: Mismatch note fill ${on.note} with Low-stimulation vs ${off.note}`);
      const offV = `${off.color} / ${off.fill} / ${off.border} / note ${off.note}`;
      const onV = `${on.color} / ${on.fill} / ${on.border} / note ${on.note}`;
      if (look.prefs.skin !== "paper") {
        if (onV !== offV || on.stripe !== off.stripe) problems.push(`${where}: ${onV} stripe ${on.stripe} with Low-stimulation vs ${offV} stripe ${off.stripe}`);
      } else if (onV !== offV) {
        paperQuiet.push(`${where}: off ${offV}; on (${look.prefs.theme === "light" ? "Fog" : "Dusk"}) ${onV}; pill text ${ratio(off.color, off.fill).toFixed(2)} -> ${ratio(on.color, on.fill).toFixed(2)}`);
      }
    }
  }
  if (paperQuiet.length) {
    test.info().annotations.push({ type: "paper-low-stimulation", description: paperQuiet.join("; ") });
    console.log(`[slice 8] Paper + Low-stimulation (Fog / Dusk) differences:\n${paperQuiet.join("\n")}`);
  }
  expect(problems).toEqual([]);
});

test.afterAll(() => {
  if (measured.length) console.log(`[slice 8] ${measured.join("\n")}`);
  console.log(`[slice 8] reopen route: ${reopenReloads} reload(s) needed before the hub joined (the hub's 10 Joins / minute per session, see the slice 8x record)`);
});
