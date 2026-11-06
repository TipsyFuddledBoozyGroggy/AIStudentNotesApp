"use client";

import { useEffect, useId, useRef, useState, type ReactNode, type RefObject } from "react";
import { createPortal } from "react-dom";

const FOCUSABLE = 'button, a[href], input:not([disabled]), [tabindex="0"]';

export interface DocsDialogProps {
  heading: string;
  headingRef: RefObject<HTMLHeadingElement | null>;
  /** Escape and the Tab trap; Escape is ignored while `busy` (the dialog stays open while saving, section 4 state 2). */
  onClose(): void;
  busy: boolean;
  children: ReactNode;
}

/**
 * The Save to a Demo Doc dialog shell. Same primitives and behaviour as the shared ConfirmDialog (.dialog-scrim, .dialog,
 * portalled to <body>, everything else `inert`, Tab stays inside, Escape closes) but not destructive: focus starts on the
 * dialog heading (F12), not on a button, and the caller returns focus to its opener. Not a live region; results are said
 * through the existing announcer.
 */
export function DocsDialog({ heading, headingRef, onClose, busy, children }: DocsDialogProps) {
  const id = useId();
  const ref = useRef<HTMLDivElement>(null);
  const overlayRef = useRef<HTMLDivElement>(null);
  const [mounted, setMounted] = useState(false);
  useEffect(() => setMounted(true), []);

  useEffect(() => {
    if (!mounted) return;
    const overlay = overlayRef.current;
    const made: Element[] = [];
    for (const el of Array.from(document.body.children)) {
      if (el === overlay || el.hasAttribute("inert")) continue;
      el.setAttribute("inert", "");
      made.push(el);
    }
    headingRef.current?.focus();
    return () => made.forEach((el) => el.removeAttribute("inert"));
  }, [mounted, headingRef]);

  if (!mounted) return null;
  return createPortal(
    <div ref={overlayRef} className="dialog-scrim fixed inset-0 z-40 flex items-end justify-center p-4 sm:items-center">
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-labelledby={`${id}-h`}
        aria-busy={busy ? true : undefined}
        data-docs-dialog
        className="dialog max-h-full overflow-y-auto"
        onKeyDown={(e) => {
          if (e.key === "Escape") {
            e.stopPropagation();
            if (!busy) onClose();
          } else if (e.key === "Tab") {
            const items = Array.from(ref.current?.querySelectorAll<HTMLElement>(FOCUSABLE) ?? []);
            if (items.length === 0) return;
            const first = items[0]!;
            const last = items[items.length - 1]!;
            const active = document.activeElement;
            if (e.shiftKey && (active === first || active === headingRef.current)) {
              e.preventDefault();
              last.focus();
            } else if (!e.shiftKey && active === last) {
              e.preventDefault();
              first.focus();
            }
          }
        }}
      >
        <h2 id={`${id}-h`} ref={headingRef} tabIndex={-1} className="display text-xl">
          {heading}
        </h2>
        {children}
      </div>
    </div>,
    document.body,
  );
}
