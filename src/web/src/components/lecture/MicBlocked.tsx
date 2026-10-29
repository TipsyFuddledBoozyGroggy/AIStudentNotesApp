"use client";

import { useEffect, useRef } from "react";
import { BLOCKED_COPY as B } from "./copy";
import type { MicFailure } from "./micSource";

export interface MicBlockedProps {
  reason: MicFailure;
  /** True only after the server confirmed the session was stopped. */
  nothingRecordedConfirmed: boolean;
  /** Runs getUserMedia (via the start flow) only when pressed. */
  onTryAgain: () => void;
  onBack: () => void;
  busy?: boolean;
}

export function MicBlocked({ reason, nothingRecordedConfirmed, onTryAgain, onBack, busy }: MicBlockedProps) {
  const h = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    h.current?.focus();
  }, []);
  const body = reason === "no_device" ? B.noDevice : nothingRecordedConfirmed ? B.bodyConfirmed : B.bodyUnconfirmed;
  const help: [string, string][] = [
    [B.chrome, B.chromeBody],
    [B.safari, B.safariBody],
    [B.firefox, B.firefoxBody],
  ];
  return (
    <section aria-labelledby="blocked-h1" className="card mx-auto max-w-[640px]">
      <div className="flex items-start gap-3">
        <svg width="28" height="28" viewBox="0 0 28 28" aria-hidden="true" focusable="false" className="mt-1 shrink-0 text-muted">
          <rect x="10" y="3" width="8" height="14" rx="4" fill="none" stroke="currentColor" strokeWidth="2" />
          <path d="M6.5 13a7.5 7.5 0 0 0 15 0M14 20.5V25" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
          <line x1="4" y1="25" x2="24" y2="3" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
        </svg>
        <h1 id="blocked-h1" ref={h} tabIndex={-1} className="display text-3xl">
          {B.heading}
        </h1>
      </div>
      <p className="mt-3 text-lg">{body}</p>
      <div className="mt-5 flex flex-wrap gap-3">
        <button type="button" className="btn-primary min-h-[48px]" aria-disabled={busy ? true : undefined} onClick={() => !busy && onTryAgain()}>
          {B.tryAgain}
        </button>
        <button type="button" className="btn-secondary min-h-[48px]" onClick={onBack}>
          {B.back}
        </button>
      </div>
      <h2 className="eyebrow mt-8">{B.howTo}</h2>
      {help.map(([name, text]) => (
        <details key={name} name="mic-help" className="group mt-1 border-b border-hair">
          <summary className="flex min-h-[44px] cursor-pointer list-none items-center gap-2 font-semibold text-accent [&::-webkit-details-marker]:hidden">
            <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden="true" focusable="false" className="shrink-0 transition-transform duration-fast group-open:rotate-90">
              <path d="M5 2.5 9.5 7 5 11.5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
            <span className="underline underline-offset-[0.2em]">{`How to turn it on in ${name}`}</span>
          </summary>
          <p className="pb-3 text-muted">{text}</p>
        </details>
      ))}
    </section>
  );
}
