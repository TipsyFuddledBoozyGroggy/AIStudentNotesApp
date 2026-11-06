"use client";

import { useEffect, useId, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { inertOutside } from "@/lib/inertOutside";
import { PlainText } from "@/lib/lecture";
import { CONSENT_DIALOG_COPY as D } from "./copy";

export interface ConsentDialogProps {
  courseName: string;
  /** Called only when the student has ticked the attestation box and pressed the confirm button. */
  onConfirm: () => void;
  /** Cancel button or Escape. Nothing is started and the microphone is not touched. */
  onCancel: () => void;
}

const FOCUSABLE = 'button, input, a[href], select, textarea, [tabindex]:not([tabindex="-1"])';

/**
 * Teacher-permission attestation (PO-16): the only consent step for now, for personal use.
 * DRAFT wording (see CONSENT_DIALOG_COPY): not legal text, not reviewed by counsel or a school; gate G1 stays BLOCKED.
 *
 * Behaviour: rendered only while open (so the box always starts unchecked); no timer, no auto-dismiss, and a click on
 * the backdrop does nothing. Escape and Cancel call onCancel. Tab and Shift+Tab stay inside. Everything else in <body>
 * is made `inert` while it is open, so the page behind cannot be reached by keyboard, pointer or screen reader; the two
 * announcer regions are exempt (inertOutside) so they can still be heard.
 */
export function ConsentDialog({ courseName, onConfirm, onCancel }: ConsentDialogProps) {
  const overlayRef = useRef<HTMLDivElement>(null);
  const dialogRef = useRef<HTMLDivElement>(null);
  const checkRef = useRef<HTMLInputElement>(null);
  const hintRef = useRef<HTMLParagraphElement>(null);
  const [checked, setChecked] = useState(false);
  const [mounted, setMounted] = useState(false);
  const headingId = useId();
  const bodyId = useId();
  const hintId = useId();

  useEffect(() => setMounted(true), []);

  useEffect(() => {
    if (!mounted) return;
    const overlay = overlayRef.current;
    if (!overlay) return;
    const undo = inertOutside(overlay); // the announcer regions stay reachable (S2)
    checkRef.current?.focus();
    return undo;
  }, [mounted]);

  if (!mounted) return null;

  const confirm = () => {
    if (!checked) {
      hintRef.current?.focus();
      return;
    }
    onConfirm();
  };

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Escape") {
      e.stopPropagation();
      e.preventDefault();
      onCancel();
      return;
    }
    if (e.key !== "Tab") return;
    const items = Array.from(dialogRef.current?.querySelectorAll<HTMLElement>(FOCUSABLE) ?? []);
    if (items.length === 0) return;
    const first = items[0];
    const last = items[items.length - 1];
    const active = document.activeElement;
    const inside = active instanceof Node && dialogRef.current?.contains(active);
    if (e.shiftKey && (active === first || !inside)) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && (active === last || !inside)) {
      e.preventDefault();
      first.focus();
    }
  };

  return createPortal(
    <div
      ref={overlayRef}
      data-consent-dialog-overlay=""
      className="fixed inset-0 z-50 flex items-end justify-center overflow-y-auto bg-[var(--scrim)] p-4 sm:items-center"
    >
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={headingId}
        aria-describedby={bodyId}
        className="w-full max-w-md rounded-sheet border border-hair bg-raised p-6 shadow-3"
        onKeyDown={onKeyDown}
      >
        <h2 id={headingId} className="display text-2xl">
          {D.heading}
        </h2>
        <p id={bodyId} className="mt-3 break-words">
          <PlainText value={D.body(courseName)} />
        </p>

        <div className="mt-5 flex items-start gap-3 rounded-control border-2 border-line bg-surface p-4">
          <input
            ref={checkRef}
            id="consent-teacher-check"
            type="checkbox"
            className="mt-0.5 h-6 w-6 shrink-0 accent-[var(--c-accent)]"
            checked={checked}
            onChange={(e) => setChecked(e.target.checked)}
          />
          <label htmlFor="consent-teacher-check" className="font-semibold">
            {D.checkbox}
          </label>
        </div>

        <p className="mt-3 text-sm text-muted">{D.notSure}</p>

        {!checked ? (
          <p id={hintId} ref={hintRef} tabIndex={-1} className="mt-3 text-sm text-muted">
            {D.checkHint}
          </p>
        ) : null}

        <div className="mt-6 flex flex-wrap gap-3">
          <button
            type="button"
            className="btn-primary min-h-[48px] px-6"
            aria-disabled={!checked ? true : undefined}
            aria-describedby={!checked ? hintId : undefined}
            onClick={confirm}
          >
            {D.confirm}
          </button>
          <button type="button" className="btn-quiet min-h-[48px]" onClick={onCancel}>
            {D.cancel}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
