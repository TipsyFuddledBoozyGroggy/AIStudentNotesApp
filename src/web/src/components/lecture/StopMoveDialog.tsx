"use client";

import { useEffect, useRef, useState } from "react";
import { STOP_MOVE_COPY as C } from "./copy";

/**
 * PO-19 move notice (docs/ux/stop-redirect.md sections 2 and 3): the second and last modal of the lecture flow, the same
 * primitive as the Stop confirm (.dialog-scrim + .dialog). Opens only when the student's own confirmed Stop reached
 * Stopped. Focus starts on "Open the lecture's page"; Tab cycles every control inside (checkbox included); Escape equals
 * "Stay here"; a press on the backdrop does nothing. Nothing counts down, nothing closes by itself, and it adds no live
 * region (the dialog's name carries the moment).
 */
export function StopMoveDialog(props: {
  /** "Open the lecture's page": `remember` is the checkbox's value at the press. */
  onOpen(remember: boolean): void;
  /** "Stay here" or Escape: the checkbox value is discarded by the caller. */
  onStay(): void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const openRef = useRef<HTMLButtonElement>(null);
  const [remember, setRemember] = useState(false);
  useEffect(() => {
    openRef.current?.focus();
  }, []);
  return (
    <div className="dialog-scrim fixed inset-0 z-40 flex items-end justify-center p-4 sm:items-center">
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-labelledby="stop-move-h2"
        data-testid="stop-move-dialog"
        // At 320 with text size Large the spec's copy needs about 700px: the sheet never exceeds the scrim's box and
        // scrolls inside, so nothing is cut off above the screen (recorded deviation; the spec estimated under 70%).
        className="dialog max-h-full overflow-y-auto overscroll-contain"
        onKeyDown={(e) => {
          if (e.key === "Escape") {
            e.stopPropagation();
            props.onStay();
          } else if (e.key === "Tab") {
            // Every focusable inside, not buttons only: the checkbox is part of the cycle (spec 3, "Modal").
            const items = Array.from(ref.current?.querySelectorAll<HTMLElement>("input, button") ?? []);
            const first = items[0];
            const last = items[items.length - 1];
            if (!first || !last) return;
            if (e.shiftKey && document.activeElement === first) {
              e.preventDefault();
              last.focus();
            } else if (!e.shiftKey && document.activeElement === last) {
              e.preventDefault();
              first.focus();
            }
          }
        }}
      >
        <h2 id="stop-move-h2" className="display text-xl">
          {C.heading}
        </h2>
        <p className="mt-2">{C.body}</p>
        <p className="mt-2 text-muted">{C.bodyHere}</p>
        <div className="mt-4">
          <label className="flex min-h-[44px] cursor-pointer items-start gap-3 py-2">
            <input
              type="checkbox"
              className="mt-1 h-5 w-5 shrink-0 accent-[var(--c-accent)]"
              checked={remember}
              aria-describedby="stop-move-hint"
              onChange={(e) => setRemember(e.target.checked)}
            />
            <span>{C.checkbox}</span>
          </label>
          <p id="stop-move-hint" className="pl-8 text-sm text-muted">
            {C.checkboxHint}
          </p>
        </div>
        <div className="mt-6 flex flex-wrap justify-end gap-3">
          <button ref={openRef} type="button" className="btn-primary grow sm:grow-0" onClick={() => props.onOpen(remember)}>
            {C.open}
          </button>
          <button type="button" className="btn-secondary grow sm:grow-0" onClick={props.onStay}>
            {C.stay}
          </button>
        </div>
      </div>
    </div>
  );
}
