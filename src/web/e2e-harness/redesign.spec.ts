import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";

// Redesign checks (design-direction slices 4-5) on the dev-only harness: fake microphone, fake AI, no API. These stand in
// for e2e/lecture.spec.ts's phone check while getUserMedia cannot run in this machine's automation browser
// (see docs/ux/redesign-progress.md). Synthetic data only.

const usual = (p: Page) => p.getByRole("button", { name: /^Last 5 min \(your usual\): recap of the last 5 minutes$/ });
const setPrefs = (p: Page, prefs: Record<string, unknown>) =>
  p.addInitScript((v) => window.localStorage.setItem("tether.prefs.v1", JSON.stringify(v)), prefs);

test("phone: Quick recap dock stays in view while the page scrolls; skip link reaches it; 44px+ targets", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 667 });
  await page.goto("/dev/lecture-harness/live-quick-recap");
  await expect(usual(page)).toBeInViewport();
  await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight));
  await expect(usual(page)).toBeInViewport();
  for (const b of await page.getByRole("button", { name: /recap of the last \d+ minutes$/ }).all()) {
    const box = (await b.boundingBox())!;
    expect(box.height).toBeGreaterThanOrEqual(56);
  }
  await page.getByRole("link", { name: "Skip to Quick recap" }).focus();
  await page.keyboard.press("Enter");
  await expect(usual(page)).toBeFocused();
});

test("phone: the newest transcript line is not hidden behind the dock once the transcript is in view", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/dev/lecture-harness/live-quick-recap");
  const log = page.getByRole("log", { name: "Transcript lines" });
  await log.scrollIntoViewIfNeeded();
  const logBox = (await log.boundingBox())!;
  const dockBox = (await page.locator("#lecture-quick-recap").boundingBox())!;
  const bar = (await page.locator("[data-lecture-status-bar]").boundingBox())!;
  // The transcript viewport fits between the sticky bar and the dock (it may start under the bar only if scrolled).
  expect(logBox.height).toBeLessThanOrEqual(dockBox.y - (bar.y + bar.height) + 1);
});

test("a cited line or focused control is never covered by the sticky bar or dock (scroll-padding)", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 667 });
  await page.goto("/dev/lecture-harness/live-quick-recap");
  const hold = page.getByRole("button", { name: "Pause updates" });
  // On phones the TL;DR is collapsed; open it, then tab to a control low on the page.
  await page.getByRole("button", { name: "More of the TL;DR" }).click();
  await hold.focus();
  const box = (await hold.boundingBox())!;
  const bar = (await page.locator("[data-lecture-status-bar]").boundingBox())!;
  const dock = (await page.locator("#lecture-quick-recap").boundingBox())!;
  expect(box.y).toBeGreaterThanOrEqual(bar.y + bar.height - 1);
  expect(box.y + box.height).toBeLessThanOrEqual(dock.y + 1);
});

test("Focus mode: toggle is pressed, keeps the bar, the dock and the TL;DR heading, and is remembered on reload", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto("/dev/lecture-harness/live-quick-recap");
  const focus = page.getByRole("button", { name: "Focus" });
  await expect(focus).toHaveAttribute("aria-pressed", "false");
  await focus.click();
  await expect(focus).toHaveAttribute("aria-pressed", "true");
  await expect(page.locator("[data-lecture-status-bar]")).toBeInViewport();
  await expect(usual(page)).toBeInViewport();
  await expect(page.getByRole("heading", { name: "TL;DR" })).toBeVisible();
  await page.reload();
  await expect(page.getByRole("button", { name: "Focus" })).toHaveAttribute("aria-pressed", "true");
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"]).analyze();
  expect(r.violations.map((v) => v.id)).toEqual([]);
});

test("Focus mode never lowers text contrast: no opacity or filter on lecture content", async ({ page }) => {
  await setPrefs(page, { focusMode: true });
  await page.goto("/dev/lecture-harness/live-quick-recap");
  await expect(page.getByRole("button", { name: "Focus" })).toHaveAttribute("aria-pressed", "true");
  const dimmed = await page.evaluate(() =>
    Array.from(document.querySelectorAll("[data-lecture-root] *")).filter((e) => {
      const s = getComputedStyle(e);
      return Number(s.opacity) < 1 || (s.filter !== "none" && s.filter !== "");
    }).length,
  );
  expect(dimmed).toBe(0);
});

test("the live screen has exactly one h1 and the recording pill is visible at 320px in every look", async ({ page }) => {
  for (const look of [{ theme: "light" }, { theme: "dark" }, { theme: "light", quiet: true }, { theme: "dark", quiet: true }]) {
    await setPrefs(page, look);
    await page.setViewportSize({ width: 320, height: 640 });
    await page.goto("/dev/lecture-harness/live-quick-recap");
    await expect(page.getByTestId("state-label")).toBeInViewport();
    expect(await page.locator("h1").count()).toBe(1);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
  }
});

test("Low-stimulation look: no transitions or shadows under the lecture root; recording pill unchanged in prominence", async ({ page }) => {
  await setPrefs(page, { quiet: true });
  await page.goto("/dev/lecture-harness/live");
  const bad = await page.evaluate(() =>
    Array.from(document.querySelectorAll("[data-lecture-root] *")).filter((e) => {
      const s = getComputedStyle(e);
      return s.transitionDuration.split(",").some((d) => parseFloat(d) > 0) || (s.boxShadow !== "none" && !/^rgba\(0, 0, 0, 0\)/.test(s.boxShadow) && e !== document.activeElement);
    }).map((e) => e.className.toString().slice(0, 60)),
  );
  expect(bad).toEqual([]);
  const pill = page.getByTestId("state-label");
  await expect(pill).toHaveText("Recording");
  await expect(pill).toBeVisible();
});

// Slice 3 (redesign-round2 5.2): on desktop the Quick recap result opens as a sheet in the transcript column, above the
// transcript, so the first line and the TL;DR stay in view; its citation times hang in the margin track at 1280.
test("desktop 1280: the recap result is a sheet above the transcript; first line and TL;DR stay in view; Close recap returns to the transcript", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto("/dev/lecture-harness/live-quick-recap");
  await usual(page).click();
  const result = page.getByTestId("catchup-result");
  await expect(result.getByRole("heading", { level: 2, name: "Here is what was said" })).toBeFocused();
  const [rb, tb, qb] = await Promise.all([result.boundingBox(), page.locator(".transcript-panel").boundingBox(), page.locator("#lecture-quick-recap").boundingBox()]);
  expect(Math.abs(rb!.x - tb!.x)).toBeLessThanOrEqual(1); // the same column as the transcript sheet
  expect(Math.abs(rb!.width - tb!.width)).toBeLessThanOrEqual(1);
  expect(rb!.y + rb!.height).toBeLessThanOrEqual(tb!.y + 1); // above it
  expect(rb!.x + rb!.width).toBeLessThan(qb!.x); // not in the rail: the rail keeps the windows
  expect(await page.locator("#lecture-quick-recap [data-testid=catchup-result]").count()).toBe(0);
  await expect(page.locator(".transcript-line").first()).toBeInViewport();
  await expect(page.getByRole("heading", { name: "TL;DR" })).toBeInViewport();
  await expect(usual(page)).toBeInViewport();
  await expect(result.getByRole("button", { name: "Close recap" })).toBeVisible();
  await expect(result.getByRole("button", { name: "Back to lecture" })).toBeVisible();
  // Citation times hang in the margin, on the page, left of the sheet's painted surface; the words share the transcript's text edge.
  const cite = (await result.getByRole("button", { name: /go to transcript line$/ }).first().boundingBox())!;
  expect(Math.abs(cite.x - rb!.x)).toBeLessThanOrEqual(1);
  const lead = (await result.locator(".synthesis").boundingBox())!;
  const words = (await page.locator(".transcript-line > span").nth(1).boundingBox())!;
  expect(Math.abs(lead.x - words.x)).toBeLessThanOrEqual(1);
  await result.getByRole("button", { name: "Close recap" }).click();
  await expect(result).toHaveCount(0);
  await expect(page.getByRole("log", { name: "Transcript lines" })).toBeFocused();
});

test("phone 390: the recap result is the bottom sheet (unchanged mechanics); at least three transcript lines stay readable between the bar and the sheet", async ({ browser }) => {
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
  const page = await ctx.newPage();
  await page.goto("/dev/lecture-harness/live-quick-recap");
  await usual(page).click();
  const result = page.getByTestId("catchup-result");
  await expect(result.getByRole("heading", { level: 3, name: "Here is what was said" })).toBeFocused();
  expect(await page.locator("#lecture-quick-recap [data-testid=catchup-result]").count()).toBe(1); // inside the dock
  // With the sheet open the page keeps 60dvh of scroll room, so the student can bring the lines they were reading up
  // under the bar: park the first line there, then count what sits between the bar and the sheet.
  // Two passes: the bar is sticky, so its bottom edge moves up once the page scrolls past the fixture caption.
  for (let i = 0; i < 2; i++) {
    await page.evaluate(() => {
      const line = document.querySelector(".transcript-line")!;
      const bar = document.querySelector("[data-lecture-status-bar]")!;
      window.scrollBy(0, line.getBoundingClientRect().top - bar.getBoundingClientRect().bottom - 8);
    });
  }
  const dock = (await page.locator("#lecture-quick-recap").boundingBox())!;
  const bar = (await page.locator("[data-lecture-status-bar]").boundingBox())!;
  const lines = await page.locator(".transcript-line").evaluateAll((els) => els.map((e) => ({ top: e.getBoundingClientRect().top, bottom: e.getBoundingClientRect().bottom })));
  const readable = lines.filter((l) => l.top >= bar.y + bar.height - 1 && l.bottom <= dock.y + 1);
  expect(readable.length, `lines ${JSON.stringify(lines)} bar bottom ${bar.y + bar.height} sheet top ${dock.y}`).toBeGreaterThanOrEqual(3);
  await expect(result.getByRole("button", { name: "Close recap" })).toBeInViewport();
  await ctx.close();
});

/** Visible text must appear in the accessible name of every control that overrides its name (WCAG 2.5.3 Label in Name). */
async function labelInNameViolations(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const norm = (s: string) => s.toLowerCase().replace(/[^\p{L}\p{N}]+/gu, " ").trim();
    const out: string[] = [];
    for (const el of Array.from(document.querySelectorAll<HTMLElement>("button, a[href], summary, [role=button], [role=switch], [role=radio], [role=option]"))) {
      const label = el.getAttribute("aria-label") ?? (el.getAttribute("aria-labelledby") ?? "").split(/\s+/).filter(Boolean).map((id) => document.getElementById(id)?.textContent ?? "").join(" ");
      if (!label.trim()) continue; // name comes from content: visible text is the name
      if (el.offsetParent === null && getComputedStyle(el).position !== "fixed") continue; // not rendered
      const visible = norm(el.innerText);
      if (visible && !norm(label).includes(visible)) out.push(`"${el.innerText.trim()}" not in "${label}"`);
    }
    return out;
  });
}

for (const id of ["live-quick-recap", "live", "paused", "stopped", "disconnected", "mismatch", "consent", "mic-denied"]) {
  test(`label in name (2.5.3): visible text is part of every overridden accessible name: ${id}`, async ({ page }) => {
    await page.goto(`/dev/lecture-harness/${id}`);
    await expect(page.getByText(/^Fixture:/)).toBeVisible();
    if (id === "live-quick-recap") {
      await usual(page).click();
      await expect(page.getByRole("heading", { name: "Here is what was said" })).toBeFocused();
      await expect(page.getByRole("button", { name: /^\d\d:\d\d, go to transcript line$/ }).first()).toBeVisible();
    }
    expect(await labelInNameViolations(page)).toEqual([]);
    const r = await new AxeBuilder({ page }).withRules(["label-content-name-mismatch"]).analyze();
    expect(r.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.html).join(" | ")}`)).toEqual([]);
  });
}
