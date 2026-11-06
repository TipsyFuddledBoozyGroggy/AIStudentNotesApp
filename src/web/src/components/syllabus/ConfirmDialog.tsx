"use client";

import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";

export interface ConfirmDialogProps {
  open: boolean;
  heading: string;
  children?: ReactNode;
  confirmLabel: string;
  cancelLabel: string;
  busy?: boolean;
  busyLabel?: string;
  onConfirm(): void;
  onCancel(): void;
}

/**
 * Destructive confirmation (UX 9: dialogs only for delete). Same pattern as the lecture StopDialog and the consent pop-up:
 * focus starts on the safe choice, Tab stays inside, Escape cancels, focus returns to the opener on cancel. Not a live
 * region. Since redesign-round2 slice 6 it is portalled to <body> and everything else in <body> is `inert` while it is
 * open, so the page behind cannot be reached by keyboard, pointer or screen reader (and a control half-scrolled under
 * the sticky header is not a reachable target while the dialog is up, which axe otherwise reports as target-size).
 */
export function ConfirmDialog({ open, heading, children, confirmLabel, cancelLabel, busy, busyLabel, onConfirm, onCancel }: ConfirmDialogProps) {
  const id = useId();
  const ref = useRef<HTMLDivElement>(null);
  const overlayRef = useRef<HTMLDivElement>(null);
  const opener = useRef<HTMLElement | null>(null);
  const keepRef = useRef<HTMLButtonElement>(null);
  const [mounted, setMounted] = useState(false);

  useEffect(() => setMounted(true), []);

  useEffect(() => {
    if (!open || !mounted) return;
    opener.current = document.activeElement as HTMLElement | null;
    const overlay = overlayRef.current;
    const made: Element[] = [];
    for (const el of Array.from(document.body.children)) {
      if (el === overlay || el.hasAttribute("inert")) continue;
      el.setAttribute("inert", "");
      made.push(el);
    }
    keepRef.current?.focus();
    return () => made.forEach((el) => el.removeAttribute("inert"));
  }, [open, mounted]);

  if (!open || !mounted) return null;
  const cancel = () => {
    if (busy) return;
    onCancel();
    queueMicrotask(() => opener.current?.focus());
  };
  return createPortal(
    <div ref={overlayRef} className="dialog-scrim fixed inset-0 z-40 flex items-end justify-center p-4 sm:items-center">
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-labelledby={`${id}-h`}
        aria-describedby={children ? `${id}-d` : undefined}
        className="dialog"
        onKeyDown={(e) => {
          if (e.key === "Escape") {
            e.stopPropagation();
            cancel();
          } else if (e.key === "Tab") {
            const btns = Array.from(ref.current?.querySelectorAll<HTMLElement>("button") ?? []);
            const first = btns[0];
            const last = btns[btns.length - 1];
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
        <h2 id={`${id}-h`} className="display text-xl">
          {heading}
        </h2>
        {children && (
          <div id={`${id}-d`} className="mt-2 space-y-2 text-muted">
            {children}
          </div>
        )}
        <div className="mt-6 flex flex-wrap justify-end gap-3">
          <button ref={keepRef} type="button" className="btn-primary" onClick={cancel}>
            {cancelLabel}
          </button>
          <button type="button" className="btn-secondary" aria-disabled={busy ? true : undefined} aria-busy={busy ? true : undefined} onClick={() => !busy && onConfirm()}>
            {busy && busyLabel ? busyLabel : confirmLabel}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
