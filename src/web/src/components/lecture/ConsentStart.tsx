"use client";

import { useEffect, useRef, useState } from "react";
import { PlainText } from "@/lib/lecture";
import { useAnnouncer } from "./Announcer";
import { ConsentDialog } from "./ConsentDialog";
import { CONSENT_COPY as C, DEMO_NOTICE_COPY } from "./copy";
import type { StartError } from "./ports";

export interface ConsentStartProps {
  courseName: string;
  /** Called only after the teacher-permission dialog is confirmed (box ticked, confirm pressed). */
  onStart: () => void;
  onCancel: () => void;
  /**
   * Course-level attestation from an earlier start (SEC-008). Shown as information only: the dialog box always starts
   * unchecked, so every start needs a fresh, explicit confirmation (PO-16).
   */
  attestation?: { dateLabel: string };
  error?: StartError | null;
  /** Link target for the invalid_state error. */
  runningLectureHref?: string;
  busy?: boolean;
  /** The backend reports demoSource only after a session exists; the real route sets this while only the demo provider ships. */
  demoExpected?: boolean;
}

/**
 * "Before you start" page. Start recording opens ConsentDialog (teacher-permission attestation, DRAFT wording); only
 * confirming there calls onStart. Cancel or Escape closes the dialog, returns focus to Start recording, and starts
 * nothing: no session, no microphone.
 */
export function ConsentStart({ courseName, onStart, onCancel, attestation, error, runningLectureHref, busy, demoExpected }: ConsentStartProps) {
  const announcer = useAnnouncer();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const startRef = useRef<HTMLButtonElement>(null);
  const [dialogOpen, setDialogOpen] = useState(false);

  useEffect(() => {
    headingRef.current?.focus();
  }, []);

  useEffect(() => {
    if (!error) return;
    announcer.alert(`start-${error}`, C.errors[error]);
    startRef.current?.focus();
    return () => announcer.clearEpisode(`start-${error}`);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [error]);

  const press = () => {
    if (busy) return;
    setDialogOpen(true);
  };

  const cancelDialog = () => {
    setDialogOpen(false);
    queueMicrotask(() => startRef.current?.focus());
  };

  const confirmDialog = () => {
    setDialogOpen(false);
    queueMicrotask(() => startRef.current?.focus());
    onStart();
  };

  return (
    // Sheet template container only (slice 1): 40rem = the former 640px, left-aligned to the column start instead of centred.
    // Everything inside the card (wording, tag, buttons, dialog) is unchanged: gate G1.
    <section aria-labelledby="consent-h1" className="card max-w-sheet">
      <p className="eyebrow">Before you start</p>
      <h1 id="consent-h1" ref={headingRef} tabIndex={-1} className="display mt-1 break-words text-3xl">
        <PlainText value={C.heading(courseName)} />
      </h1>
      <p className="mt-4 flex flex-wrap items-center gap-2 text-sm text-muted">
        <span className="tag tag-warning">{C.draftTag}</span>
        {C.draftNotice}
      </p>
      <p className="mt-5 text-lg">{C.intro}</p>
      <ul className="mt-3 list-disc space-y-2 pl-6 marker:text-muted">
        {C.bullets.map((b) => (
          <li key={b}>{b}</li>
        ))}
        {demoExpected ? <li data-testid="consent-demo-bullet">{DEMO_NOTICE_COPY.consentBullet}</li> : null}
      </ul>

      <details className="mt-4">
        <summary className="inline-flex min-h-[44px] cursor-pointer items-center font-semibold text-accent underline underline-offset-[0.2em]">
          {C.whatDoesThisMean}
        </summary>
        <p className="mt-1 text-muted">{C.whatDoesThisMeanBody}</p>
      </details>

      {attestation ? (
        <p className="mt-4">
          <PlainText value={C.attested(courseName, attestation.dateLabel)} />
        </p>
      ) : null}

      {error ? (
        <p className="panel panel-notice mt-4">
          {C.errors[error]}{" "}
          {error === "invalid_state" && runningLectureHref ? (
            <a className="link" href={runningLectureHref}>
              Open the running lecture
            </a>
          ) : null}
        </p>
      ) : null}

      <div className="mt-5 flex flex-wrap gap-3">
        <button
          ref={startRef}
          type="button"
          className="btn-primary min-h-[48px] min-w-[48px] px-6"
          aria-haspopup="dialog"
          aria-disabled={busy ? true : undefined}
          onClick={press}
        >
          {busy ? C.starting : C.start}
        </button>
        <button type="button" className="btn-quiet min-h-[48px]" onClick={onCancel}>
          {C.notNow}
        </button>
      </div>

      {dialogOpen ? <ConsentDialog courseName={courseName} onConfirm={confirmDialog} onCancel={cancelDialog} /> : null}
    </section>
  );
}
