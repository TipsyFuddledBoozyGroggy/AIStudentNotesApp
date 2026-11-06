// Modal dialogs (DocsDialog, ConfirmDialog, ConsentDialog) make everything behind them `inert`. The two announcer regions
// (Announcer.tsx: role=status and role=alert, two of the three allowed live regions) must stay out of that sweep: an inert
// subtree leaves the accessibility tree, so a live region inside it is not announced while the dialog is open (UX review
// docs/ux/demo-doc-and-export-ux-review.md section 7, S2; a spec-based hypothesis until the G3 screen-reader run). This
// adds no live region; it only keeps the existing two reachable.

/** The announcer's regions. The transcript finals region is not exempt: it is behind the dialog like the rest of the page. */
export const ANNOUNCER_REGIONS = '[data-region="status"], [data-region="alert"]';

/**
 * Sets `inert` on every element in <body> except `keep` (the dialog's overlay, a direct child of <body>), elements that are
 * already inert, and the announcer regions. An element that contains a region is not made inert itself; its other
 * children are, recursively, so only the path down to the regions stays reachable. Children added later to one of those
 * path elements while the dialog is open are made inert too. Returns the undo, which removes only what this call set.
 */
export function inertOutside(keep: Element | null): () => void {
  const made: Element[] = [];
  const observers: MutationObserver[] = [];
  function consider(el: Element) {
    if (el === keep || el.hasAttribute("inert") || el.matches(ANNOUNCER_REGIONS)) return;
    if (el.querySelector(ANNOUNCER_REGIONS)) {
      Array.from(el.children).forEach(consider);
      const mo = new MutationObserver((records) => {
        for (const r of records) r.addedNodes.forEach((n) => n instanceof Element && consider(n));
      });
      mo.observe(el, { childList: true });
      observers.push(mo);
      return;
    }
    el.setAttribute("inert", "");
    made.push(el);
  }
  // <body> itself is not observed: a later portal there (another dialog) is not ours to hide, as before.
  Array.from(document.body.children).forEach(consider);
  return () => {
    observers.forEach((o) => o.disconnect());
    made.forEach((el) => el.removeAttribute("inert"));
  };
}
