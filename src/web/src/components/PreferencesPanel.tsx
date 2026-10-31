"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import type { Density, Spacing, TextSize, ThemePref } from "@/lib/design/prefs";
import { Disclosure } from "./ui/Disclosure";
import { Segmented } from "./ui/Segmented";
import { Switch } from "./ui/Switch";
import { usePrefs } from "./PrefsProvider";

/** The Look controls, shared by the site Preferences panel and the lecture's settings. Changes apply at once. */
const THEME_MSG: Record<ThemePref, string> = { system: "Theme follows your system.", light: "Light theme on.", dark: "Dark theme on." };
const SIZE_MSG: Record<TextSize, string> = { s: "Text size: small.", m: "Text size: medium.", l: "Text size: large." };

export function LookControls({ compact = false, onAnnounce }: { compact?: boolean; onAnnounce?(message: string): void }) {
  const { prefs, update: rawUpdate } = usePrefs();
  const say = (m: string) => onAnnounce?.(m);
  const update = (patch: Parameters<typeof rawUpdate>[0]) => {
    rawUpdate(patch);
    if (patch.theme) say(THEME_MSG[patch.theme]);
    if (patch.quiet !== undefined) say(patch.quiet ? "Low-stimulation look on." : "Low-stimulation look off.");
    if (patch.textSize) say(SIZE_MSG[patch.textSize]);
    if (patch.spacing) say(patch.spacing === "wide" ? "Reading spacing: wider." : "Reading spacing: standard.");
    if (patch.density) say(patch.density === "compact" ? "Density: compact." : "Density: comfortable.");
    if (patch.reduceMotion !== undefined) say(patch.reduceMotion ? "Reduce motion on." : "Reduce motion off.");
  };
  return (
    <div className="space-y-5">
      <Segmented<ThemePref>
        legend="Theme"
        value={prefs.theme}
        onChange={(theme) => update({ theme })}
        options={[
          { value: "system", label: "System" },
          { value: "light", label: "Light" },
          { value: "dark", label: "Dark" },
        ]}
      />
      <Switch
        label="Low-stimulation look"
        hint="Softer colours, no shadows, no movement. Recording status stays just as clear."
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
        </>
      )}
    </div>
  );
}

/**
 * Non-modal Preferences panel (section 6). It pushes content down rather than covering it, takes focus on open, returns
 * focus to its opener on close, and Escape closes it. Saved on this device only.
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
  // Sheet template (themes-spec R2 / A3): 40rem, left-aligned to the column start, not centred. Slice 5 makes it a side panel.
  return (
    <section
      id="preferences"
      aria-labelledby="prefs-h2"
      className="card mb-6 mt-4 max-w-sheet"
      onKeyDown={(e) => {
        if (e.key === "Escape") {
          e.stopPropagation();
          onClose();
        }
      }}
    >
      <div className="flex items-baseline justify-between gap-4">
        <div>
          <h2 id="prefs-h2" ref={heading} tabIndex={-1} className="display text-2xl">
            Preferences
          </h2>
          <p className="help mt-1">Changes apply right away and are saved on this device.</p>
        </div>
        <button type="button" className="btn-secondary btn-sm" onClick={onClose}>
          Close
        </button>
      </div>
      <h3 className="eyebrow mt-6">Look</h3>
      <div className="mt-3">
        <LookControls onAnnounce={announce} />
      </div>
      <p role="status" className="mt-4 min-h-[1.5em] text-sm font-semibold text-success">
        {message}
      </p>
      {children}
      <h3 className="eyebrow mt-8">About</h3>
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
    </section>
  );
}
