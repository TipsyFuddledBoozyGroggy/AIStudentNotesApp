"use client";

import { useEffect, useRef, useState, type ReactNode, type Ref } from "react";
import { LookControls } from "../PreferencesPanel";
import { usePrefs } from "../PrefsProvider";
import {
  AUDIO_NOTE_COPY,
  BLOCKED_COPY,
  DEMO_NOTICE_COPY,
  FATAL_COPY,
  MISMATCH_COPY as M,
  OFFLINE_COPY as O,
  PAUSED_COPY as P,
  SETTINGS_COPY as ST,
  STATUS_COPY,
  STOP_DIALOG_COPY as D,
  STOPPED_COPY as SC,
} from "./copy";
import type { MicFailure } from "./micSource";
import type { LectureSettings } from "./settings";

/** Panel slot directly under the lecture bar (section 4 "Panel"): stripe + shape + words, never colour alone. */
const slot = "mx-auto mt-3 max-w-content px-4";

function PanelIcon({ kind }: { kind: "bars" | "slash" | "square" | "alert" | "info" }) {
  const c = { width: 22, height: 22, viewBox: "0 0 22 22", "aria-hidden": true, focusable: false, className: "mt-0.5 shrink-0" } as const;
  switch (kind) {
    case "bars":
      return (
        <svg {...c}>
          <rect x="5" y="4" width="4" height="14" rx="1" fill="currentColor" />
          <rect x="13" y="4" width="4" height="14" rx="1" fill="currentColor" />
        </svg>
      );
    case "slash":
      return (
        <svg {...c}>
          <circle cx="11" cy="11" r="7.5" fill="none" stroke="currentColor" strokeWidth="2" />
          <line x1="5" y1="17" x2="17" y2="5" stroke="currentColor" strokeWidth="2" />
        </svg>
      );
    case "square":
      return (
        <svg {...c}>
          <rect x="5" y="5" width="12" height="12" rx="1" fill="currentColor" />
        </svg>
      );
    case "alert":
      return (
        <svg {...c}>
          <path d="M11 2.5 20.5 19h-19z" fill="none" stroke="currentColor" strokeWidth="2" strokeLinejoin="round" />
          <path d="M11 8.5v5M11 16v.5" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" />
        </svg>
      );
    case "info":
      return (
        <svg {...c}>
          <circle cx="11" cy="11" r="8.5" fill="none" stroke="currentColor" strokeWidth="2" />
          <path d="M11 10v5.5M11 6.8v.4" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" />
        </svg>
      );
  }
}

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
    <div className={slot}>
      <section aria-labelledby="paused-h2" data-panel="paused" className="panel panel-notice">
        <div className="flex gap-3">
          <span className="text-warning">
            <PanelIcon kind="bars" />
          </span>
          <div className="min-w-0 flex-1">
            <h2 id="paused-h2" className="display text-xl">
              {heading}
            </h2>
            <p className="mt-1">{body}</p>
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
              <button ref={props.stopRef} type="button" className="btn-secondary min-h-[56px] min-w-[48px]" onClick={props.onStop}>
                {P.stop}
              </button>
            </div>
            {props.resumeDisabledReason ? (
              <p id="resume-reason" className="mt-2 text-sm">
                {props.resumeDisabledReason}
              </p>
            ) : null}
            <p className="mt-3 text-sm text-muted">
              {props.sinceLabel ? `${P.since(props.sinceLabel)} · ` : ""}
              {P.autoStopRule}
            </p>
          </div>
        </div>
      </section>
    </div>
  );
}

export function MismatchPanel(props: { micOff: boolean; onCheck(): void; onStop(): void }) {
  return (
    <div className={slot}>
      <section aria-labelledby="mismatch-h2" data-panel="mismatch" className="panel panel-safety border-2">
        <div className="flex gap-3">
          <span className="text-recording">
            <PanelIcon kind="alert" />
          </span>
          <div className="min-w-0 flex-1">
            <h2 id="mismatch-h2" className="display text-xl">
              {M.heading}
            </h2>
            <p className="mt-1">{props.micOff ? M.bodyMicOff : M.body}</p>
            <div className="mt-3 flex flex-wrap gap-3">
              <button type="button" className="btn-primary min-h-[48px]" onClick={props.onCheck}>
                {M.check}
              </button>
              <button type="button" className="btn-secondary min-h-[48px]" onClick={props.onStop}>
                {M.stop}
              </button>
            </div>
          </div>
        </div>
      </section>
    </div>
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
    <div className={slot}>
      <section aria-labelledby="stopped-h2" data-panel="stopped" className="panel panel-neutral">
        <div className="flex gap-3">
          <span className="text-muted">
            <PanelIcon kind="square" />
          </span>
          <div className="min-w-0 flex-1">
            <h2 id="stopped-h2" ref={props.headingRef} tabIndex={-1} className="display text-xl">
              {SC.heading}
            </h2>
            <p className="mt-1">{text}</p>
            <button type="button" className="btn-secondary mt-3 min-h-[48px]" onClick={props.onBack}>
              {SC.back}
            </button>
          </div>
        </div>
      </section>
    </div>
  );
}

/** Persistent text banner (no role, not focusable): the alert region announces it once per episode. */
export function OfflineBanner({ stopQueued }: { stopQueued: boolean }) {
  return (
    <div className={slot}>
      <div data-banner="offline" className="panel panel-notice flex items-start gap-3">
        <span className="text-warning">
          <PanelIcon kind="slash" />
        </span>
        <p>
          <strong>{O.banner}</strong> <span className="text-muted">{O.retrying}</span>
          {stopQueued ? <> {SC.stoppingOffline}</> : null}
        </p>
      </div>
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
    <div className="fixed inset-0 z-40 flex items-end justify-center bg-[var(--scrim)] p-4 sm:items-center">
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-labelledby="stop-h2"
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
        <h2 id="stop-h2" className="display text-2xl">
          {D.heading}
        </h2>
        <p className="mt-2 text-muted">The transcript so far is kept.</p>
        <div className="mt-6 flex flex-wrap justify-end gap-3">
          <button ref={keepRef} type="button" className="btn-primary min-h-[48px]" onClick={cancel}>
            {props.paused ? D.cancel : D.cancelActive}
          </button>
          <button type="button" className="btn-secondary min-h-[48px]" onClick={props.onConfirm}>
            {D.confirm}
          </button>
        </div>
      </div>
    </div>
  );
}

/**
 * Lecture settings popover in the lecture bar: look (theme, low-stimulation) plus the lecture's own options. Native
 * checkboxes keep the labels the harness tests use. Reduce motion is the site-wide preference (one source of truth).
 */
export function SettingsPanel({ settings, onChange, extra }: { settings: LectureSettings; onChange(p: Partial<LectureSettings>): void; extra?: ReactNode }) {
  const { prefs, update } = usePrefs();
  const [open, setOpen] = useState(false);
  const rows: [string, string, boolean, (v: boolean) => void][] = [
    ["announceLines", ST.announceLines, settings.announceLines, (v) => onChange({ announceLines: v })],
    ["announceTldr", ST.announceTldr, settings.announceTldr, (v) => onChange({ announceTldr: v })],
    ["reduceMotion", ST.reduceMotion, prefs.reduceMotion || settings.reduceMotion, (v) => {
      update({ reduceMotion: v });
      if (!v && settings.reduceMotion) onChange({ reduceMotion: false });
    }],
    ["shortcuts", ST.shortcuts, settings.shortcuts, (v) => onChange({ shortcuts: v })],
    ["showSeconds", "Show seconds in the recording time", prefs.showSeconds, (v) => update({ showSeconds: v })],
  ];
  return (
    <details
      className="relative"
      open={open}
      onToggle={(e) => setOpen((e.currentTarget as HTMLDetailsElement).open)}
      onKeyDown={(e) => {
        if (e.key === "Escape" && open) {
          e.stopPropagation();
          setOpen(false);
          (e.currentTarget.querySelector("summary") as HTMLElement | null)?.focus();
        }
      }}
    >
      <summary className="btn-quiet min-h-[48px] cursor-pointer list-none px-2 sm:px-3 [&::-webkit-details-marker]:hidden">{ST.heading}</summary>
      <div className="absolute right-0 top-full z-30 mt-2 max-h-[70vh] w-[min(26rem,calc(100vw-2rem))] overflow-y-auto rounded-sheet border border-line bg-raised p-5 shadow-2">
        <p className="eyebrow">Look</p>
        <div className="mt-3">
          <LookControls compact />
        </div>
        <p className="eyebrow mt-6">During the lecture</p>
        <div className="mt-2 space-y-1">
          {rows.map(([k, label, checked, set]) => (
            <label key={k} className="flex min-h-[44px] cursor-pointer items-start gap-3 rounded-control px-1 py-2 hover:bg-sunken">
              <input type="checkbox" className="mt-0.5 h-5 w-5 shrink-0 accent-[var(--c-accent)]" checked={checked} onChange={(e) => set(e.target.checked)} />
              <span>{label}</span>
            </label>
          ))}
        </div>
        {extra}
        <p className="mt-3 text-sm text-muted lg:hidden">Focus mode is for wider screens. On a phone the layout is already one column.</p>
        <p className="mt-3 text-sm text-muted">{ST.shortcutList}</p>
        <p className="mt-2 text-sm text-muted">{STATUS_COPY.helpQuiet}</p>
      </div>
    </details>
  );
}

/**
 * Demo source notice. Never dismissible. While a safety panel is open (compact) only the body collapses behind "Details";
 * the Demo heading itself always stays visible (review M4).
 */
export function DemoNotice({ compact = false }: { compact?: boolean }) {
  return (
    <div className={slot}>
      <section data-testid="demo-notice" aria-labelledby="demo-notice-h2" className="panel panel-notice flex gap-3 py-3">
        <span className="text-warning">
          <PanelIcon kind="info" />
        </span>
        <div className="min-w-0">
          <h2 id="demo-notice-h2" className="font-bold">
            {DEMO_NOTICE_COPY.heading}
          </h2>
          {compact ? (
            <details className="text-sm">
              <summary className="inline-flex min-h-[44px] cursor-pointer items-center font-semibold text-accent underline underline-offset-[0.2em]">
                Details
              </summary>
              <p className="pb-1 text-muted">{DEMO_NOTICE_COPY.body}</p>
            </details>
          ) : (
            <p className="mt-1 text-sm text-muted">{DEMO_NOTICE_COPY.body}</p>
          )}
        </div>
      </section>
    </div>
  );
}

export function FatalPanel(props: { kind: "signed_out" | "unavailable"; onBack(): void; headingRef: Ref<HTMLHeadingElement> }) {
  const signedOut = props.kind === "signed_out";
  return (
    <div className={slot}>
      <section data-testid="fatal-panel" aria-labelledby="fatal-h2" className="panel panel-danger">
        <h2 id="fatal-h2" ref={props.headingRef} tabIndex={-1} className="display text-xl">
          {signedOut ? FATAL_COPY.signedOutHeading : FATAL_COPY.unavailableHeading}
        </h2>
        <p className="mt-1">{signedOut ? FATAL_COPY.signedOutBody : FATAL_COPY.unavailableBody}</p>
        <div className="mt-3 flex flex-wrap gap-3">
          {signedOut ? (
            <a className="btn-primary min-h-[48px]" href="/signin">
              {FATAL_COPY.signIn}
            </a>
          ) : (
            <button type="button" className="btn-primary min-h-[48px]" onClick={props.onBack}>
              {FATAL_COPY.back}
            </button>
          )}
        </div>
      </section>
    </div>
  );
}

export function AudioSkippedNote({ ms }: { ms: number }) {
  return (
    <div className={slot}>
      <p data-testid="audio-skipped" className="panel panel-notice text-sm">
        {AUDIO_NOTE_COPY.skipped(Math.max(1, Math.round(ms / 1000)))}
      </p>
    </div>
  );
}
