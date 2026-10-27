"use client";

import { useEffect, useId, useRef, useState } from "react";
import { PlainText } from "@/lib/lecture";
import { useAnnouncer } from "./Announcer";
import { CONSENT_COPY as C } from "./copy";
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
}

export function ConsentStart({ courseName, onStart, onCancel, attestation, error, runningLectureHref, busy }: ConsentStartProps) {
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
    <section aria-labelledby="consent-h1" className="mx-auto max-w-[640px]">
      <h1 id="consent-h1" ref={headingRef} tabIndex={-1} className="text-2xl font-bold">
        <PlainText value={C.heading(courseName)} />
      </h1>
      <p className="mt-3">
        <span className="mr-2 inline-block rounded border-2 border-amber-800 px-2 text-sm font-bold text-amber-900">{C.draftTag}</span>
        {C.draftNotice}
      </p>
      <p className="mt-3">{C.intro}</p>
      <ul className="mt-3 list-disc space-y-1 pl-6">
        {C.bullets.map((b) => (
          <li key={b}>{b}</li>
        ))}
      </ul>

      <details className="mt-4">
        <summary className="min-h-[44px] cursor-pointer py-2 font-semibold underline">{C.whatDoesThisMean}</summary>
        <p className="mt-2">{C.whatDoesThisMeanBody}</p>
      </details>

      {attested ? (
        <p className="mt-4">
          <PlainText value={C.attested(courseName, attestation?.dateLabel ?? "")} />{" "}
          <button
            type="button"
            className="min-h-[44px] underline"
            onClick={() => {
              setAttested(false);
              setChecked(false);
            }}
          >
            {C.attestedChange}
          </button>
        </p>
      ) : null}

      <div className="mt-4 flex items-start gap-3">
        <input
          ref={checkRef}
          id="consent-check"
          type="checkbox"
          className="mt-1 h-6 w-6 shrink-0"
          checked={checked}
          onChange={(e) => setChecked(e.target.checked)}
        />
        <label htmlFor="consent-check" className="font-semibold">
          {C.checkbox}
        </label>
      </div>

      {error ? (
        <p id={errId} className="mt-3 rounded border-2 border-slate-600 bg-white p-3">
          {C.errors[error]}{" "}
          {error === "invalid_state" && runningLectureHref ? (
            <a className="underline" href={runningLectureHref}>
              Open the running lecture
            </a>
          ) : null}
        </p>
      ) : null}

      {!checked ? (
        <p id={hintId} ref={hintRef} tabIndex={-1} className="mt-3 text-sm">
          {C.startHint}
        </p>
      ) : null}

      <div className="mt-4 flex flex-wrap gap-3">
        <button
          type="button"
          className="btn-primary min-h-[48px] min-w-[48px]"
          aria-disabled={!checked || busy ? true : undefined}
          aria-describedby={!checked ? hintId : undefined}
          onClick={press}
        >
          {busy ? C.starting : C.start}
        </button>
        <button type="button" className="btn-secondary min-h-[48px]" onClick={onCancel}>
          {C.notNow}
        </button>
      </div>
    </section>
  );
}
