"use client";

import { useEffect, useId, useRef, useState } from "react";
import { PlainText } from "@/lib/lecture";
import { useAnnouncer } from "./Announcer";
import { CONSENT_COPY as C, DEMO_NOTICE_COPY } from "./copy";
import type { StartError } from "./ports";

export interface ConsentStartProps {
  courseName: string;
  /** Called only when the box is checked and Start is pressed. */
  onStart: () => void;
  onCancel: () => void;
  /** Course-level attestation from an earlier start (SEC-008); the box starts checked. */
  attestation?: { dateLabel: string };
  error?: StartError | null;
  /** Link target for the invalid_state error. */
  runningLectureHref?: string;
  busy?: boolean;
  /** The backend reports demoSource only after a session exists; the real route sets this while only the demo provider ships. */
  demoExpected?: boolean;
}

export function ConsentStart({ courseName, onStart, onCancel, attestation, error, runningLectureHref, busy, demoExpected }: ConsentStartProps) {
  const announcer = useAnnouncer();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const checkRef = useRef<HTMLInputElement>(null);
  const hintRef = useRef<HTMLParagraphElement>(null);
  const [checked, setChecked] = useState(attestation !== undefined);
  const [attested, setAttested] = useState(attestation !== undefined);
  const hintId = useId();
  const errId = useId();

  useEffect(() => {
    headingRef.current?.focus();
  }, []);

  useEffect(() => {
    if (!error) return;
    announcer.alert(`start-${error}`, C.errors[error]);
    if (error === "consent_required") checkRef.current?.focus();
    return () => announcer.clearEpisode(`start-${error}`);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [error]);

  const press = () => {
    if (busy) return;
    if (!checked) {
      hintRef.current?.focus();
      return;
    }
    onStart();
  };

  return (
    <section aria-labelledby="consent-h1" className="card mx-auto max-w-[640px]">
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

      {attested ? (
        <p className="mt-4">
          <PlainText value={C.attested(courseName, attestation?.dateLabel ?? "")} />{" "}
          <button
            type="button"
            className="btn-quiet btn-sm"
            onClick={() => {
              setAttested(false);
              setChecked(false);
            }}
          >
            {C.attestedChange}
          </button>
        </p>
      ) : null}

      <div className="mt-5 flex items-start gap-3 rounded-control border-2 border-line bg-surface p-4">
        <input
          ref={checkRef}
          id="consent-check"
          type="checkbox"
          className="mt-0.5 h-6 w-6 shrink-0 accent-[var(--c-accent)]"
          checked={checked}
          onChange={(e) => setChecked(e.target.checked)}
        />
        <label htmlFor="consent-check" className="font-semibold">
          {C.checkbox}
        </label>
      </div>

      {error ? (
        <p id={errId} className="panel panel-notice mt-3">
          {C.errors[error]}{" "}
          {error === "invalid_state" && runningLectureHref ? (
            <a className="link" href={runningLectureHref}>
              Open the running lecture
            </a>
          ) : null}
        </p>
      ) : null}

      {!checked ? (
        <p id={hintId} ref={hintRef} tabIndex={-1} className="mt-3 text-sm text-muted">
          {C.startHint}
        </p>
      ) : null}

      <div className="mt-5 flex flex-wrap gap-3">
        <button
          type="button"
          className="btn-primary min-h-[48px] min-w-[48px] px-6"
          aria-disabled={!checked || busy ? true : undefined}
          aria-describedby={!checked ? hintId : undefined}
          onClick={press}
        >
          {busy ? C.starting : C.start}
        </button>
        <button type="button" className="btn-quiet min-h-[48px]" onClick={onCancel}>
          {C.notNow}
        </button>
      </div>
    </section>
  );
}
