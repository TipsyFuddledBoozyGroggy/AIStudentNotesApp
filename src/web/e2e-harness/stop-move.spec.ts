import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";

// PO-19 (docs/ux/stop-redirect.md section 6, harness criteria 11 and 12) on the dev-only harness: the `live` fixture renders
// as the live route (link + move notice; the harness has no router, so "Open the lecture's page" is recorded in the
// dev-only controls bar). Synthetic data, fake microphone, no API. The ten looks at 390 / 1280 are in
// e2e-screens/lecture-looks.spec.ts ("stop-move"); this file covers the dialog's behaviour, geometry and motion.

const AXE_TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa", "best-practice"];
const DIALOG = { name: "This lecture has its own page" };

async function openLive(page: Page, width: number, prefs: Record<string, unknown> = {}, height = 900) {
  await page.setViewportSize({ width, height });
  if (Object.keys(prefs).length) await page.addInitScript((v) => window.localStorage.setItem("tether.prefs.v1", JSON.stringify(v)), prefs);
  await page.goto("/dev/lecture-harness/live");
  await expect(page.getByText(/^Fixture:/)).toBeVisible();
  await expect(page.getByTestId("state-label")).toHaveText("Recording");
}

/** Stop in the bar, confirm, wait for the notice; returns it. */
async function stopToNotice(page: Page) {
  await page.getByRole("button", { name: "Stop", exact: true }).click();
  await page.getByRole("dialog", { name: "Stop and save this lecture?" }).getByRole("button", { name: "Stop", exact: true }).click();
  const dlg = page.getByRole("dialog", DIALOG);
  await expect(dlg).toBeVisible();
  await expect(page.getByTestId("state-label")).toHaveText("Stopped");
  return dlg;
}

const pill = (page: Page) =>
  page.evaluate(() => {
    const label = document.querySelector("[data-testid=state-label]")!;
    const el = label.closest(".rounded-full")!;
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return { text: label.textContent, box: `${Math.round(r.x)},${Math.round(r.y)} ${Math.round(r.width)}x${Math.round(r.height)}`, bg: s.backgroundColor, color: getComputedStyle(label).color, border: s.borderTopColor, opacity: s.opacity };
  });

for (const width of [390, 1280]) {
  test(`${width}: Stop -> confirm -> the move notice: focus on the primary, axe clean, three live regions, the pill behind it unchanged`, async ({ page }) => {
    await openLive(page, width);
    const dlg = await stopToNotice(page);
    await expect(dlg.getByRole("button", { name: "Open the lecture's page" })).toBeFocused();
    await expect(page.locator("#stopped-h2")).not.toBeFocused();
    expect(await page.locator('[data-region="status"]').textContent()).toBe("");
    const r = await new AxeBuilder({ page }).withTags(AXE_TAGS).analyze();
    expect(r.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`)).toEqual([]);
    expect(await page.locator("[data-region]").evaluateAll((els) => els.map((e) => e.getAttribute("data-region")).sort())).toEqual(["alert", "finals", "status"]);
    expect(await dlg.locator("[aria-live],[role=status],[role=alert]").count()).toBe(0);
    const behind = await pill(page);
    expect(behind.text).toBe("Stopped");
    expect(behind.opacity).toBe("1");
    await dlg.getByRole("button", { name: "Stay here" }).click();
    expect(await pill(page)).toEqual(behind);
  });

  test(`${width}: geometry: bottom sheet below 640 and centred from 640, on the 4px grid, every control at least 44px`, async ({ page }) => {
    await openLive(page, width);
    const dlg = await stopToNotice(page);
    const g = await dlg.evaluate((d) => {
      const out: string[] = [];
      const els = [d, ...Array.from(d.querySelectorAll<HTMLElement>(".btn, .btn-primary, .btn-secondary, div"))];
      for (const el of els) {
        const s = getComputedStyle(el);
        for (const p of ["padding-top", "padding-bottom", "padding-left", "padding-right", "row-gap", "column-gap", "margin-top"]) {
          const v = parseFloat(s.getPropertyValue(p));
          if (v && Math.abs(v / 4 - Math.round(v / 4)) > 0.01) out.push(`${el.className.toString().slice(0, 30)} ${p} ${v}`);
        }
      }
      const targets = [...Array.from(d.querySelectorAll("button")), d.querySelector("input")!.closest("label")!].map((t) => {
        const r = t.getBoundingClientRect();
        return { label: (t.textContent ?? "").trim(), w: r.width, h: r.height };
      });
      const r = d.getBoundingClientRect();
      // Centred within the scrim (the fixed box the sheet lays out in), not the window: a scrollbar gutter differs.
      const sc = d.parentElement!.getBoundingClientRect();
      return { off: out, targets, box: { x: r.x - sc.x, y: r.y, w: r.width, h: r.height, bottom: innerHeight - r.bottom, right: sc.right - r.right } };
    });
    expect(g.off).toEqual([]);
    for (const t of g.targets) expect(t.h, `${t.label} height`).toBeGreaterThanOrEqual(44);
    for (const t of g.targets) expect(t.w, `${t.label} width`).toBeGreaterThanOrEqual(44);
    if (width < 640) expect(Math.round(g.box.bottom)).toBe(16); // bottom sheet: the scrim's 16px padding
    else expect(Math.abs(g.box.x - g.box.right)).toBeLessThanOrEqual(1); // centred
    expect(g.box.w).toBeLessThanOrEqual(448);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
  });
}

// Spec section 3 estimated the sheet under 70% of the viewport at 320 with text size Large. Measured: the spec's copy
// needs about 700px there (the heading was cut off above the screen before the fix). Deviation recorded: the sheet is held
// inside the screen and scrolls; the 70% bound is asserted where it holds (390, Medium) and the 320 Large figure printed.
for (const [width, height, textSize] of [[320, 640, "l"], [390, 844, "m"]] as const) {
  test(`${width} with text size ${textSize === "l" ? "Large" : "Medium"}: no horizontal overflow, the two buttons take full rows, the sheet inside the screen with every control reachable`, async ({ page }) => {
    await openLive(page, width, { textSize }, height);
    const dlg = await stopToNotice(page);
    const m = await dlg.evaluate((d) => {
      const s = getComputedStyle(d);
      const inner = d.getBoundingClientRect().width - parseFloat(s.paddingLeft) - parseFloat(s.paddingRight) - parseFloat(s.borderLeftWidth) - parseFloat(s.borderRightWidth);
      const r = d.getBoundingClientRect();
      return { inner, buttons: Array.from(d.querySelectorAll("button")).map((b) => b.getBoundingClientRect().width), top: r.top, bottom: r.bottom, h: r.height, need: d.scrollHeight, vh: innerHeight, scroll: d.scrollWidth - d.clientWidth };
    });
    for (const w of m.buttons) expect(Math.abs(w - m.inner)).toBeLessThanOrEqual(1);
    expect(m.scroll).toBeLessThanOrEqual(0);
    expect(m.top).toBeGreaterThanOrEqual(16 - 0.5); // never cut off above the screen
    expect(m.bottom).toBeLessThanOrEqual(m.vh - 16 + 0.5);
    console.log(`[stop-move] ${width} ${textSize}: sheet ${Math.round(m.h)}px shown of ${Math.round(m.need)}px needed, viewport ${m.vh}px (${Math.round((100 * m.need) / m.vh)}% needed)`);
    if (textSize === "m") expect(m.need / m.vh).toBeLessThan(0.7);
    // Every part is reachable: the heading scrolls into view inside the sheet, and the primary is focused and visible.
    await expect(dlg.getByRole("button", { name: "Open the lecture's page" })).toBeInViewport();
    await dlg.getByRole("heading").scrollIntoViewIfNeeded();
    await expect(dlg.getByRole("heading")).toBeInViewport();
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
  });
}

test("keyboard: Tab cycles checkbox, primary, secondary inside; Escape stays with focus on the Stopped heading; the link is in the Stopped note", async ({ page }) => {
  await openLive(page, 1280);
  const dlg = await stopToNotice(page);
  const order: string[] = [];
  for (let i = 0; i < 6; i++) {
    await page.keyboard.press("Tab");
    order.push(await page.evaluate(() => (document.activeElement as HTMLElement).getAttribute("type") === "checkbox" ? "checkbox" : (document.activeElement?.textContent ?? "").trim()));
  }
  expect(order).toEqual(["Stay here", "checkbox", "Open the lecture's page", "Stay here", "checkbox", "Open the lecture's page"]);
  await page.keyboard.press("Shift+Tab");
  expect(await dlg.evaluate((d) => d.contains(document.activeElement) && (document.activeElement as HTMLInputElement).type === "checkbox")).toBe(true);
  await page.keyboard.press("Escape");
  await expect(dlg).toHaveCount(0);
  await expect(page.locator("#stopped-h2")).toBeFocused();
  const link = page.getByRole("link", { name: "Open this lecture's page" });
  await expect(link).toHaveAttribute("href", "/lectures/ses_harness");
  await expect(link).toHaveClass(/btn-tertiary/);
  await expect(page.getByTestId("harness-opened")).toHaveCount(0); // nothing moved
});

test("a press on the backdrop does nothing; nothing counts down", async ({ page }) => {
  await openLive(page, 390);
  const dlg = await stopToNotice(page);
  await page.mouse.click(195, 40); // the scrim above the bottom sheet
  await page.waitForTimeout(4000);
  await expect(dlg).toBeVisible();
  await expect(page.getByTestId("harness-opened")).toHaveCount(0);
});

test("Open with the box checked records the move once and saves the harness user's flag; the next Stop moves with no notice", async ({ page }) => {
  await openLive(page, 1280);
  const dlg = await stopToNotice(page);
  await dlg.getByRole("checkbox", { name: "Don't show this again" }).check();
  await dlg.getByRole("button", { name: "Open the lecture's page" }).click();
  await expect(page.getByTestId("harness-opened")).toContainText("pressed 1 time(s)");
  expect(await page.evaluate(() => window.localStorage.getItem("tether.lecture.u.harness-user.stopMove.v1"))).toBe("direct");
  // A new lecture on the same device: the fixture reloads, Stop moves at once.
  await page.reload();
  await expect(page.getByTestId("state-label")).toHaveText("Recording");
  let sawDialog = false;
  page.on("console", () => undefined);
  await page.evaluate(() => {
    new MutationObserver(() => {
      if (document.querySelector('[data-testid="stop-move-dialog"]')) (window as unknown as { __saw: boolean }).__saw = true;
    }).observe(document.body, { childList: true, subtree: true });
  });
  await page.getByRole("button", { name: "Stop", exact: true }).click();
  await page.getByRole("dialog", { name: "Stop and save this lecture?" }).getByRole("button", { name: "Stop", exact: true }).click();
  await expect(page.getByTestId("harness-opened")).toContainText("pressed 1 time(s)");
  sawDialog = await page.evaluate(() => (window as unknown as { __saw?: boolean }).__saw === true);
  expect(sawDialog).toBe(false);
  await expect(page.locator("#stopped-h2")).not.toBeFocused();
});

test("criterion 12: Low-stimulation on and off: the notice's geometry equal within 1px; reduce motion: no transition on the notice or its scrim", async ({ browser }) => {
  const read = async (prefs: Record<string, unknown>, width: number) => {
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    await openLive(page, width, prefs);
    const dlg = await stopToNotice(page);
    const v = await dlg.evaluate((d) => {
      const box = (e: Element) => {
        const r = e.getBoundingClientRect();
        return [r.x, r.y, r.width, r.height].map((n) => Math.round(n));
      };
      const t = (e: Element) => {
        const s = getComputedStyle(e);
        return `${s.transitionProperty} ${s.transitionDuration} ${s.animationName}`;
      };
      return {
        dialog: box(d),
        parts: Array.from(d.querySelectorAll("h2, p, label, button")).map(box),
        motion: [t(d), t(d.parentElement!)],
      };
    });
    await ctx.close();
    return v;
  };
  for (const width of [390, 1280]) {
    const off = await read({ quiet: false }, width);
    const on = await read({ quiet: true }, width);
    const diff = (a: number[], b: number[]) => a.some((n, i) => Math.abs(n - b[i]!) > 1);
    expect(diff(on.dialog, off.dialog), `dialog box ${on.dialog} vs ${off.dialog} @${width}`).toBe(false);
    on.parts.forEach((p, i) => expect(diff(p, off.parts[i]!), `part ${i} ${p} vs ${off.parts[i]} @${width}`).toBe(false));
    const still = await read({ reduceMotion: true }, width);
    for (const m of [...still.motion, ...off.motion]) expect(m, `@${width}`).toMatch(/(^all 0s none$)|(^none )|( 0s none$)/);
  }
});
