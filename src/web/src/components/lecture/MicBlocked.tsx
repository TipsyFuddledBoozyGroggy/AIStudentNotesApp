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
    <section aria-labelledby="blocked-h1" className="mx-auto max-w-[640px]">
      <h1 id="blocked-h1" ref={h} tabIndex={-1} className="text-2xl font-bold">
        {B.heading}
      </h1>
      <p className="mt-3">{body}</p>
      <div className="mt-4 flex flex-wrap gap-3">
        <button type="button" className="btn-primary min-h-[48px]" aria-disabled={busy ? true : undefined} onClick={() => !busy && onTryAgain()}>
          {B.tryAgain}
        </button>
        <button type="button" className="btn-secondary min-h-[48px]" onClick={onBack}>
          {B.back}
        </button>
      </div>
      <h2 className="mt-6 text-lg font-bold">{B.howTo}</h2>
      {help.map(([name, text]) => (
        <details key={name} name="mic-help" className="mt-2">
          <summary className="min-h-[44px] cursor-pointer py-2 font-semibold">{name}</summary>
          <p className="pb-2">{text}</p>
        </details>
      ))}
    </section>
  );
}
