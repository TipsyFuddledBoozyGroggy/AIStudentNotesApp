import type { Locator, Page } from "@playwright/test";

// Geometry helpers for the alignment tests (themes-spec 5.3, redesign-round2 3.3 item 3). Screenshots are not enough:
// these read boxes, text ink, baselines and computed values from the live page.

export interface Rect {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** Border box via getBoundingClientRect (page coordinates). Throws when the element is not rendered. */
export async function box(l: Locator): Promise<Rect> {
  let b = await l.boundingBox();
  if (!b) {
    // A null box for an element that exists and is visible means the node was replaced between locating and measuring
    // (seen once on the lecture harness when a landmark was read within three round trips of the server-rendered text
    // becoming visible, slice 3 record). One short retry separates that from a real missing or invisible element.
    await l.page().waitForTimeout(150);
    b = await l.boundingBox();
  }
  if (!b) {
    // Say what the page looked like: a missing element and an invisible one are different bugs.
    const count = await l.count().catch(() => -1);
    const visible = count > 0 ? await l.first().isVisible().catch(() => false) : false;
    const url = l.page().url();
    const h1 = await l.page().locator("h1").first().textContent().catch(() => null);
    throw new Error(`no box for ${String(l)} (count ${count}, visible ${visible}, url ${url}, h1 ${JSON.stringify(h1)})`);
  }
  return { x: b.x, y: b.y, width: b.width, height: b.height };
}

/**
 * The text's own box (union of the client rects of the element's visible text nodes), so "ink" edges can be compared
 * with a column edge regardless of the element's padding. Screen-reader-only text and aria-hidden hints are skipped.
 */
export async function ink(l: Locator): Promise<Rect> {
  const r = await l.evaluate((el) => {
    const hidden = (n: Node) => {
      for (let p = n.parentElement; p; p = p.parentElement) {
        if (p.classList.contains("sr-only") || p.getAttribute("aria-hidden") === "true") return true;
        if (p === el) break;
      }
      return false;
    };
    const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
    let u: { l: number; t: number; r: number; b: number } | null = null;
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
      if (!n.textContent?.trim() || hidden(n)) continue;
      const range = document.createRange();
      range.selectNodeContents(n);
      for (const c of Array.from(range.getClientRects())) {
        if (c.width === 0 && c.height === 0) continue;
        u = u
          ? { l: Math.min(u.l, c.left), t: Math.min(u.t, c.top), r: Math.max(u.r, c.right), b: Math.max(u.b, c.bottom) }
          : { l: c.left, t: c.top, r: c.right, b: c.bottom };
      }
    }
    if (!u) return null;
    return { x: u.l + window.scrollX, y: u.t + window.scrollY, width: u.r - u.l, height: u.b - u.t };
  });
  if (!r) throw new Error(`no text ink in ${String(l)}`);
  return r;
}

/**
 * The y of the first text line's alphabetic baseline: a zero-size inline-block probe is placed beside the first visible
 * text node (inside a wrapper span, so flex containers such as buttons align it with the text, not the box), read and removed.
 */
export async function baseline(l: Locator): Promise<number> {
  return l.evaluate((el) => {
    const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
    let text: Text | null = null;
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
      if (!n.textContent?.trim()) continue;
      let hidden = false;
      for (let p = n.parentElement; p && p !== el; p = p.parentElement) if (p.classList.contains("sr-only") || p.getAttribute("aria-hidden") === "true") hidden = true;
      if (!hidden) {
        text = n as Text;
        break;
      }
    }
    if (!text) throw new Error("no visible text for a baseline");
    const wrap = document.createElement("span");
    text.parentNode!.insertBefore(wrap, text);
    wrap.appendChild(text);
    const probe = document.createElement("span");
    probe.style.cssText = "display:inline-block;width:0;height:0;vertical-align:baseline";
    wrap.appendChild(probe);
    const y = probe.getBoundingClientRect().bottom + window.scrollY;
    wrap.parentNode!.insertBefore(text, wrap);
    wrap.remove();
    return y;
  });
}

/** A computed style length in px. */
export async function px(l: Locator, prop: string): Promise<number> {
  return l.evaluate((el, p) => parseFloat(getComputedStyle(el).getPropertyValue(p)) || 0, prop);
}

export const near = (a: number, b: number, tol = 1) => Math.abs(a - b) <= tol;

/** Content-box edges of a container element (border box minus padding), in page coordinates. */
export async function contentEdges(l: Locator): Promise<{ left: number; right: number; top: number }> {
  const b = await box(l);
  const [pl, pr, pt] = await Promise.all([px(l, "padding-left"), px(l, "padding-right"), px(l, "padding-top")]);
  return { left: b.x + pl, right: b.x + b.width - pr, top: b.y + pt };
}

/** Horizontal page overflow in CSS px (0 when nothing scrolls sideways). */
export async function overflowX(page: Page): Promise<number> {
  return page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
}

/** Visible interactive controls with their boxes, for the target-size check (themes-spec 5.3 item 4). */
export async function controls(page: Page): Promise<{ label: string; kind: "control" | "link"; box: Rect }[]> {
  return page.evaluate(() => {
    const sel = "button, [role=button], [role=switch], [role=radio], a[href], input:not([type=hidden]), select, summary";
    const out: { label: string; kind: "control" | "link"; box: { x: number; y: number; width: number; height: number } }[] = [];
    for (const el of Array.from(document.querySelectorAll<HTMLElement>(sel))) {
      // Chromium keeps layout boxes inside a closed <details> (content-visibility: hidden); checkVisibility() knows.
      if (typeof el.checkVisibility === "function" ? !el.checkVisibility({ contentVisibilityAuto: true, visibilityProperty: true }) : false) continue;
      const closed = el.closest("details:not([open])");
      if (closed && el.tagName !== "SUMMARY" && closed.contains(el) && !(el.parentElement === closed && el.tagName === "SUMMARY")) continue;
      // A checkbox or radio's target is its 44px label row (the input itself is 20px by design).
      const target = (el as HTMLInputElement).type === "checkbox" || (el as HTMLInputElement).type === "radio" ? el.closest("label") ?? el : el;
      const r = target.getBoundingClientRect();
      if (r.width === 0 || r.height === 0) continue;
      const s = getComputedStyle(el);
      if (s.visibility === "hidden" || el.closest(".sr-only")) continue;
      if (el.classList.contains("sr-only")) continue; // skip links, visible only on focus
      const isLink = el.tagName === "A" && !/\bbtn\b|\bbtn-/.test(el.className) && !el.classList.contains("nav-tab");
      out.push({
        label: (el.getAttribute("aria-label") ?? el.innerText ?? "").trim().slice(0, 40) || el.tagName.toLowerCase(),
        kind: isLink ? "link" : "control",
        box: { x: r.x + window.scrollX, y: r.y + window.scrollY, width: r.width, height: r.height },
      });
    }
    return out;
  });
}

/** Padding, gap and height of the boxed primitives, for the on-grid report (themes-spec 5.3 item 10). */
export async function offGrid(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const out: string[] = [];
    // Slice 2 added the sheet, ledger row, note, stamp and dialog to the boxed primitives.
    for (const el of Array.from(document.querySelectorAll<HTMLElement>(".card, .btn, .field, .panel, .sheet, .ledger-row, .note, .stamp, .dialog, .lecture-sheet, .rail-row"))) {
      if (el.getBoundingClientRect().width === 0) continue;
      const s = getComputedStyle(el);
      for (const p of ["padding-top", "padding-bottom", "padding-left", "padding-right", "row-gap", "column-gap"]) {
        const v = parseFloat(s.getPropertyValue(p));
        if (v && Math.abs(v / 4 - Math.round(v / 4)) > 0.01) out.push(`${el.className.toString().split(" ").slice(0, 2).join(" ")} ${p} ${v}`);
      }
    }
    return out;
  });
}
