"use client";

import { useEffect, useRef, type Ref } from "react";
import {
  BLOCKED_COPY,
  MISMATCH_COPY as M,
  OFFLINE_COPY as O,
  PAUSED_COPY as P,
  SETTINGS_COPY as ST,
  STOP_DIALOG_COPY as D,
  STOPPED_COPY as SC,
} from "./copy";
import type { MicFailure } from "./micSource";
import type { LectureSettings } from "./settings";

const panel = "mx-3 mt-3 rounded-lg border-2 border-slate-700 bg-white p-4";

export type PausedVariant = "user" | "mic-stopped" | "auto";

export function PausedPanel(props: {
  variant: PausedVariant;
  sinceLabel?: string | null;
  micIssue: MicFailure | null;
  busy: boolean;
  resumeDisabledReason?: string | null;
  onResume(): void;
  onStop(): void;
  resumeRef?: Ref<HTMLButtonElement>;
  stopRef?: Ref<HTMLButtonElement>;
}) {
  const heading = props.variant === "mic-stopped" ? P.micStoppedHeading : props.variant === "auto" ? P.autoHeading : P.heading;
  const body = props.variant === "mic-stopped" ? P.micStoppedBody : props.variant === "auto" ? P.autoBody : P.body;
  const disabled = props.busy || !!props.resumeDisabledReason;
  return (
    <section aria-labelledby="paused-h2" data-panel="paused" className={panel}>
      <h2 id="paused-h2" className="text-xl font-bold">
        {heading}
      </h2>
      <p className="mt-2">{body}</p>
      {props.micIssue ? (
        <p className="mt-2 font-semibold" data-testid="resume-mic-issue">
          {props.micIssue === "no_device" ? BLOCKED_COPY.noDevice : "We could not turn the microphone on. Check your browser's microphone setting, then press Resume again."}
        </p>
      ) : null}
      <div className="mt-3 flex flex-wrap gap-3">
        <button
          ref={props.resumeRef}
          type="button"
          className="btn-primary min-h-[56px] min-w-[48px] px-8"
          aria-disabled={disabled ? true : undefined}
          aria-describedby={props.resumeDisabledReason ? "resume-reason" : undefined}
          onClick={() => !disabled && props.onResume()}
        >
          {P.resume}
        </button>
        <button ref={props.stopRef} type="button" className="btn-secondary min-h-[48px] min-w-[48px]" onClick={props.onStop}>
          {P.stop}
        </button>
      </div>
      {props.resumeDisabledReason ? (
        <p id="resume-reason" className="mt-2 text-sm">
          {props.resumeDisabledReason}
        </p>
      ) : null}
      <p className="mt-3 text-sm">
        {props.sinceLabel ? `${P.since(props.sinceLabel)} · ` : ""}
        {P.autoStopRule}
      </p>
    </section>
  );
}

export function MismatchPanel(props: { micOff: boolean; onCheck(): void; onStop(): void }) {
  return (
    <section aria-labelledby="mismatch-h2" data-panel="mismatch" className={`${panel} border-4`}>
      <h2 id="mismatch-h2" className="text-xl font-bold">
        <span aria-hidden="true">! </span>
        {M.heading}
      </h2>
      <p className="mt-2">{props.micOff ? M.bodyMicOff : M.body}</p>
      <div className="mt-3 flex flex-wrap gap-3">
        <button type="button" className="btn-primary min-h-[48px]" onClick={props.onCheck}>
          {M.check}
        </button>
        <button type="button" className="btn-secondary min-h-[48px]" onClick={props.onStop}>
          {M.stop}
        </button>
      </div>
    </section>
  );
}

export function StoppedPanel(props: { reason: string | null; failed: boolean; onBack(): void; headingRef?: Ref<HTMLHeadingElement> }) {
  const r = props.reason;
  const text =
    r === "auto_stop_paused_timeout" || r === "hard_cap" || r === "abandoned" || r === "user"
      ? SC[r]
      : props.failed || (r !== null && r !== "")
        ? SC.system // unknown reason or server Failed: "ended by the system" (contract section 12.1 / 13.4)
        : SC.user;
  return (
    <section aria-labelledby="stopped-h2" data-panel="stopped" className={panel}>
      <h2 id="stopped-h2" ref={props.headingRef} tabIndex={-1} className="text-xl font-bold">
        {SC.heading}
      </h2>
      <p className="mt-2">{text}</p>
      <button type="button" className="btn-secondary mt-3 min-h-[48px]" onClick={props.onBack}>
        {SC.back}
      </button>
    </section>
  );
}

/** Persistent text banner (no role, not focusable): the alert region announces it once per episode. */
export function OfflineBanner({ stopQueued }: { stopQueued: boolean }) {
  return (
    <div data-banner="offline" className="mx-3 mt-3 flex items-start gap-2 rounded-lg border-2 border-slate-700 bg-white p-3">
      <svg width="20" height="20" viewBox="0 0 20 20" aria-hidden="true" focusable="false" className="mt-0.5 shrink-0">
        <circle cx="10" cy="10" r="7" fill="none" stroke="currentColor" strokeWidth="2" />
        <line x1="4" y1="16" x2="16" y2="4" stroke="currentColor" strokeWidth="2" />
      </svg>
      <p>
        <strong>{O.banner}</strong> {O.retrying}
        {stopQueued ? <> {SC.stoppingOffline}</> : null}
      </p>
    </div>
  );
}

/** The only modal in the flow. Escape and Keep cancel; Tab stays inside; focus returns to the opener. */
export function StopDialog(props: { open: boolean; paused: boolean; onConfirm(): void; onCancel(): void }) {
  const ref = useRef<HTMLDivElement>(null);
  const opener = useRef<HTMLElement | null>(null);
  const keepRef = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (!props.open) return;
    opener.current = document.activeElement as HTMLElement | null;
    keepRef.current?.focus();
  }, [props.open]);
  if (!props.open) return null;
  const cancel = () => {
    props.onCancel();
    queueMicrotask(() => opener.current?.focus());
  };
  return (
    <div className="fixed inset-0 z-30 flex items-center justify-center bg-slate-900/60 p-4">
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-labelledby="stop-h2"
        className="w-full max-w-md rounded-xl bg-white p-5"
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
        <h2 id="stop-h2" className="text-xl font-bold">
          {D.heading}
        </h2>
        <div className="mt-4 flex flex-wrap gap-3">
          <button ref={keepRef} type="button" className="btn-secondary min-h-[48px]" onClick={cancel}>
            {props.paused ? D.cancel : D.cancelActive}
          </button>
          <button type="button" className="btn-primary min-h-[48px]" onClick={props.onConfirm}>
            {D.confirm}
          </button>
        </div>
      </div>
    </div>
  );
}

export function SettingsPanel({ settings, onChange }: { settings: LectureSettings; onChange(p: Partial<LectureSettings>): void }) {
  const rows: [keyof LectureSettings, string][] = [
    ["announceLines", ST.announceLines],
    ["announceTldr", ST.announceTldr],
    ["reduceMotion", ST.reduceMotion],
    ["shortcuts", ST.shortcuts],
  ];
  return (
    <details className="mt-2">
      <summary className="min-h-[44px] cursor-pointer py-2 font-semibold">{ST.heading}</summary>
      <div className="space-y-2 pb-2">
        {rows.map(([k, label]) => (
          <label key={k} className="flex min-h-[44px] items-start gap-2">
            <input type="checkbox" className="mt-1 h-6 w-6 shrink-0" checked={settings[k]} onChange={(e) => onChange({ [k]: e.target.checked })} />
            <span>{label}</span>
          </label>
        ))}
        <p className="text-sm">{ST.shortcutList}</p>
      </div>
    </details>
  );
}
