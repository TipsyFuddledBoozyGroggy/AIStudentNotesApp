"use client";

import { useEffect, useId, useRef, type ReactNode } from "react";

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
 * Destructive confirmation (UX 9: dialogs only for delete). Same pattern as the lecture StopDialog: focus starts on the
 * safe choice, Tab stays inside, Escape cancels, focus returns to the opener on cancel. Not a live region.
 */
export function ConfirmDialog({ open, heading, children, confirmLabel, cancelLabel, busy, busyLabel, onConfirm, onCancel }: ConfirmDialogProps) {
  const id = useId();
  const ref = useRef<HTMLDivElement>(null);
  const opener = useRef<HTMLElement | null>(null);
  const keepRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    opener.current = document.activeElement as HTMLElement | null;
    keepRef.current?.focus();
  }, [open]);

  if (!open) return null;
  const cancel = () => {
    if (busy) return;
    onCancel();
    queueMicrotask(() => opener.current?.focus());
  };
  return (
    <div className="fixed inset-0 z-40 flex items-end justify-center bg-[var(--scrim)] p-4 sm:items-center">
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-labelledby={`${id}-h`}
        aria-describedby={children ? `${id}-d` : undefined}
        className="w-full max-w-md rounded-sheet border border-hair bg-raised p-6 shadow-3"
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
        <h2 id={`${id}-h`} className="display text-2xl">
          {heading}
        </h2>
        {children && (
          <div id={`${id}-d`} className="mt-2 space-y-2 text-muted">
            {children}
          </div>
        )}
        <div className="mt-6 flex flex-wrap justify-end gap-3">
          <button ref={keepRef} type="button" className="btn-primary min-h-[48px]" onClick={cancel}>
            {cancelLabel}
          </button>
          <button type="button" className="btn-secondary min-h-[48px]" aria-disabled={busy ? true : undefined} onClick={() => !busy && onConfirm()}>
            {busy && busyLabel ? busyLabel : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
