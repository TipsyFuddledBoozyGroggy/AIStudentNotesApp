"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import { lookDefaults, resolveTheme, themesEnabled, type Density, type Spacing, type TextSize, type ThemePref } from "@/lib/design/prefs";
import { Disclosure } from "./ui/Disclosure";
import { Segmented } from "./ui/Segmented";
import { Switch } from "./ui/Switch";
import { usePrefs } from "./PrefsProvider";
import { PREFS_COPY as P, THEMES } from "./prefsCopy";
import { ThemePicker } from "./ThemePicker";

/** The Look controls, shared by the site Preferences panel and the lecture's settings. Changes apply at once. */
const SIZE_MSG: Record<TextSize, string> = { s: "Text size: small.", m: "Text size: medium.", l: "Text size: large." };

/** Rollback (themes-spec 6): NEXT_PUBLIC_THEMES=off, or <html data-themes="off"> from the build flag, hides the Theme control. */
function themesOn(): boolean {
  if (!themesEnabled()) return false;
  return typeof document === "undefined" || document.documentElement.getAttribute("data-themes") !== "off";
}

const systemDark = () => typeof window !== "undefined" && typeof window.matchMedia === "function" && window.matchMedia("(prefers-color-scheme: dark)").matches;

/**
 * compact: the lecture's "Lecture settings" popover, which keeps Colour mode and Low-stimulation look only (themes-spec
 * 3.2: no Theme control mid-lecture, so nothing restyles the lecture suddenly). Full: Preferences > Look in the
 * spec's order: Theme, Colour mode, Low-stimulation look, Text size, Reading spacing, Density, Reduce motion, preview, Reset.
 */
export function LookControls({ compact = false, onAnnounce }: { compact?: boolean; onAnnounce?(message: string): void }) {
  const { prefs, update: rawUpdate } = usePrefs();
  const say = (m: string) => onAnnounce?.(m);
  const update = (patch: Parameters<typeof rawUpdate>[0]) => {
    rawUpdate(patch);
    if (patch.skin) say(P.themeOn(THEMES.find((t) => t.id === patch.skin)?.name ?? "Paper"));
    if (patch.theme) say(P.mode[patch.theme]);
    if (patch.quiet !== undefined) say(patch.quiet ? "Low-stimulation look on." : "Low-stimulation look off.");
    if (patch.textSize) say(SIZE_MSG[patch.textSize]);
    if (patch.spacing) say(patch.spacing === "wide" ? "Reading spacing: wider." : "Reading spacing: standard.");
    if (patch.density) say(patch.density === "compact" ? "Density: compact." : "Density: comfortable.");
    if (patch.reduceMotion !== undefined) say(patch.reduceMotion ? "Reduce motion on." : "Reduce motion off.");
  };
  const [showThemes] = useState(themesOn);
  return (
    <div className="space-y-5">
      {compact || !showThemes ? null : <ThemePicker value={prefs.skin} mode={resolveTheme(prefs.theme, systemDark())} onChange={(skin) => update({ skin })} />}
      <Segmented<ThemePref>
        legend={P.colourModeLegend}
        hint={compact ? undefined : P.colourModeHint}
        value={prefs.theme}
        onChange={(theme) => update({ theme })}
        options={[
          { value: "system", label: "System" },
          { value: "light", label: "Light" },
          { value: "dark", label: "Dark" },
        ]}
      />
      <Switch
        label={P.lowStimLabel}
        hint={P.lowStimHint}
        checked={prefs.quiet}
        onChange={(quiet) => update({ quiet })}
      />
      {compact ? null : (
        <>
          <Segmented<TextSize>
            legend="Text size"
            value={prefs.textSize}
            onChange={(textSize) => update({ textSize })}
            options={[
              { value: "s", label: "Small" },
              { value: "m", label: "Medium" },
              { value: "l", label: "Large" },
            ]}
          />
          <Segmented<Spacing>
            legend="Reading spacing"
            value={prefs.spacing}
            onChange={(spacing) => update({ spacing })}
            options={[
              { value: "standard", label: "Standard" },
              { value: "wide", label: "Wider" },
            ]}
          />
          <Segmented<Density>
            legend="Density"
            value={prefs.density}
            onChange={(density) => update({ density })}
            options={[
              { value: "comfortable", label: "Comfortable" },
              { value: "compact", label: "Compact" },
            ]}
          />
          <Switch
            label="Reduce motion"
            hint="Turns off every transition. Your device setting is always respected too."
            checked={prefs.reduceMotion}
            onChange={(reduceMotion) => update({ reduceMotion })}
          />
          <p className="help">A sample sentence at your size and spacing: the lecture keeps going while you read.</p>
          <LookPreview />
          <div>
            <button
              type="button"
              className="btn-secondary"
              aria-describedby="prefs-reset-hint"
              onClick={() => {
                rawUpdate(lookDefaults());
                say(P.resetDone);
              }}
            >
              {P.reset}
            </button>
            <p id="prefs-reset-hint" className="help mt-1">
              {P.resetHint}
            </p>
          </div>
        </>
      )}
    </div>
  );
}

/**
 * Preview card (themes-spec 3.2): static, in the current look, so a student sees lecture-relevant pieces without leaving
 * Preferences (the panel is hidden on lecture routes). Hidden from assistive technology except its caption; it holds
 * no controls (the "buttons" and "field" are spans), so nothing in it is focusable and it adds no live region.
 */
function LookPreview() {
  const v = P.preview;
  return (
    <figure className="prefs-preview">
      <figcaption className="label">{P.previewCaption}</figcaption>
      <div aria-hidden="true" className="prefs-preview-body mt-2">
        <span className="display block text-xl">{v.heading}</span>
        <span className="transcript-line mt-2">
          <span className="transcript-time">00:01</span>
          <span>{v.line1}</span>
        </span>
        <span className="transcript-line">
          <span className="transcript-time">00:02</span>
          <span>{v.line2}</span>
        </span>
        <span className="mt-1 flex flex-wrap items-center gap-3">
          <span className="btn-primary">{v.primary}</span>
          <span className="btn-secondary">{v.secondary}</span>
        </span>
        <span className="field mt-3">{v.field}</span>
        <span className="mt-3 flex flex-wrap items-center gap-3">
          {/* Same classes and shapes as the lecture bar's pill (StatusBar.tsx): recording red only here, paused amber. */}
          <span className="inline-flex min-h-[44px] items-center gap-2 rounded-full border-2 border-recording bg-recording-subtle py-1 pl-3 pr-4 font-bold text-recording">
            <svg width="20" height="20" viewBox="0 0 20 20" focusable="false">
              <circle cx="10" cy="10" r="7" fill="currentColor" />
            </svg>
            {v.recording}
            <span className="tabular font-semibold">{v.minutes}</span>
          </span>
          <span className="inline-flex min-h-[44px] items-center gap-2 rounded-full border-2 border-warning bg-warning-subtle py-1 pl-3 pr-4 font-bold text-warning">
            <svg width="20" height="20" viewBox="0 0 20 20" focusable="false">
              <rect x="4" y="3" width="4" height="14" rx="1" fill="currentColor" />
              <rect x="12" y="3" width="4" height="14" rx="1" fill="currentColor" />
            </svg>
            {v.paused}
            <span className="tabular font-semibold">{v.minutes}</span>
          </span>
        </span>
      </div>
    </figure>
  );
}

/**
 * Preferences (redesign-round2 2.7 Side panel, slice 5). From 1024px a fixed right-hand panel (24rem, surface, hair left
 * edge); the page beside it reflows so all of it stays visible and usable (non-modal: no scrim, no inert, no focus trap).
 * Below 1024px it sits in the page flow under the header, as before. It takes focus on open, returns focus to its opener
 * on close, Escape closes it. Its head (title, Close, the status line) stays in view while the panel scrolls. Saved on
 * this device only.
 */
export function PreferencesPanel({ onClose, children }: { onClose(): void; children?: ReactNode }) {
  const heading = useRef<HTMLHeadingElement>(null);
  // One plain confirmation per change, visible and in a status line that exists only while this panel is open (it is
  // never mounted on the live lecture screen, which keeps exactly three live regions). Identical text within 10 s is dropped.
  const [message, setMessage] = useState("");
  const last = useRef<{ text: string; at: number }>({ text: "", at: 0 });
  const announce = (text: string) => {
    const now = Date.now();
    if (text === last.current.text && now - last.current.at < 10_000) return;
    last.current = { text, at: now };
    setMessage(text);
  };
  useEffect(() => {
    heading.current?.focus();
  }, []);
  return (
    <section
      id="preferences"
      aria-labelledby="prefs-h2"
      className="prefs-panel"
      onKeyDown={(e) => {
        if (e.key === "Escape") {
          e.stopPropagation();
          onClose();
        }
      }}
    >
      <div className="prefs-head">
        <div className="flex items-baseline justify-between gap-4">
          <h2 id="prefs-h2" ref={heading} tabIndex={-1} className="display text-2xl">
            Preferences
          </h2>
          <button type="button" className="btn-secondary btn-sm" onClick={onClose}>
            Close
          </button>
        </div>
        <p className="help mt-1">Changes apply right away and are saved on this device.</p>
        <p role="status" className="mt-1 min-h-6 text-sm font-semibold text-success">
          {message}
        </p>
      </div>
      <div className="prefs-body">
        <h3 className="label">Look</h3>
        <div className="mt-3">
          <LookControls onAnnounce={announce} />
        </div>
        {children}
        <h3 className="label mt-8">About</h3>
        <div className="mt-2 space-y-1">
          <Disclosure summary="What is coming later">
            <p className="pb-2">A planner, study mode and syllabus review are not built yet. Nothing here depends on them.</p>
          </Disclosure>
          <Disclosure summary={'What "Demo source" means'}>
            <p className="pb-2">
              While Tether runs on its demo source, lecture text comes from a fixed test script and summaries from a test routine,
              not from your microphone or an AI model. Those screens always say so.
            </p>
          </Disclosure>
        </div>
      </div>
    </section>
  );
}
