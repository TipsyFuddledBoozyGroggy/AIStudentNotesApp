import type { Page } from "@playwright/test";

/**
 * Wait until a page's layout no longer depends on timing, before anything is measured (CI runs 41328040245 and
 * 41331066937: on the same code the failing look changed from run to run).
 *  1. React has hydrated the page root, so effects that read the stored prefs (Focus mode, density, ...) have run. The
 *     server-rendered HTML already shows "Fixture:", so a visible text alone can be the pre-hydration page (seen: the Focus
 *     dock measured in the standard layout at 1280).
 *  2. <html> carries the requested skin / theme, and `[data-layout]` (lecture pages) matches the Focus pref.
 *  3. Every face the page's text actually uses is loaded. `document.fonts.ready` alone is not enough: the heading faces
 *     use preload:false, so straight after navigation they are not requested yet and `ready` resolves at once; each
 *     distinct computed font is loaded explicitly here (a no-op when it already is).
 *  4. Two frames apart, the boxes of the main landmarks are identical (late swaps or re-renders settle first).
 */
export async function settle(page: Page, prefs: Record<string, unknown> = {}, timeout = 15_000): Promise<void> {
  await page.waitForFunction(
    () => {
      const el = document.querySelector("main") ?? document.body.firstElementChild;
      return !!el && Object.keys(el).some((k) => k.startsWith("__reactFiber") || k.startsWith("__reactProps"));
    },
    undefined,
    { timeout },
  );
  await page.waitForFunction(
    (p) => {
      const h = document.documentElement;
      if (typeof p.skin === "string" && h.getAttribute("data-themes") !== "off" && h.getAttribute("data-skin") !== p.skin) return false;
      if ((p.theme === "light" || p.theme === "dark") && h.getAttribute("data-theme") !== p.theme) return false;
      const layout = document.querySelector("[data-layout]");
      if (layout && typeof p.focusMode === "boolean" && layout.getAttribute("data-layout") !== (p.focusMode ? "focus" : "standard")) return false;
      return true;
    },
    prefs,
    { timeout },
  );
  await page.evaluate(async () => {
    const fonts = new Set<string>();
    for (const el of Array.from(document.body.querySelectorAll<HTMLElement>("*"))) {
      if (!Array.from(el.childNodes).some((n) => n.nodeType === Node.TEXT_NODE && n.textContent?.trim())) continue;
      const s = getComputedStyle(el);
      fonts.add(`${s.fontStyle} ${s.fontWeight} ${s.fontSize} ${s.fontFamily}`);
    }
    await Promise.all([...fonts].map((f) => document.fonts.load(f).catch(() => undefined)));
    await document.fonts.ready;
  });
  const signature = () =>
    page.evaluate(
      () =>
        new Promise<string>((resolve) =>
          requestAnimationFrame(() =>
            requestAnimationFrame(() =>
              resolve(
                Array.from(document.querySelectorAll("main, [data-layout], .transcript-panel, .transcript-line, #lecture-quick-recap, #lecture-tldr, #lecture-notes, [data-lecture-status-bar], .notice-slot > *"))
                  .map((e) => {
                    const r = e.getBoundingClientRect();
                    return `${r.x},${r.y},${r.width},${r.height}`;
                  })
                  .join(";"),
              ),
            ),
          ),
        ),
    );
  const deadline = Date.now() + timeout;
  let last = await signature();
  for (;;) {
    await page.waitForTimeout(100);
    const now = await signature();
    if (now === last) return;
    if (Date.now() > deadline) throw new Error("layout did not settle");
    last = now;
  }
}
