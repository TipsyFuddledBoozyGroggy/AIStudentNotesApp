"use client";

import { useId } from "react";

export interface SwitchProps {
  label: string;
  checked: boolean;
  onChange(next: boolean): void;
  /** Optional one-line explanation under the label. */
  hint?: string;
}

/**
 * role=switch with a visible "On"/"Off" word beside the track, so state is never colour or position alone (section 4).
 * The whole row is the target (>= 44px tall).
 */
export function Switch({ label, checked, onChange, hint }: SwitchProps) {
  const hintId = useId();
  const labelId = useId();
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-labelledby={labelId}
      aria-describedby={hint ? hintId : undefined}
      onClick={() => onChange(!checked)}
      // .hover-row (A5 / R8): the row bleeds 8px outward so its text aligns with the legends above it and the On/Off word
      // ends on the same edge as its neighbours; the hover area, not the text, carries the inset.
      className="group hover-row flex min-h-[44px] items-center justify-between gap-4 rounded-control py-2 text-left hover:bg-sunken"
    >
      <span className="min-w-0">
        <span id={labelId} className="block font-semibold text-ink">
          {label}
        </span>
        {hint ? (
          <span id={hintId} className="block text-sm text-muted">
            {hint}
          </span>
        ) : null}
      </span>
      <span className="flex shrink-0 items-center gap-2" aria-hidden="true">
        <span className="w-7 text-right text-sm font-semibold text-ink">{checked ? "On" : "Off"}</span>
        <span
          className={`relative inline-block h-6 w-11 rounded-full border-2 ${checked ? "border-accent bg-accent" : "border-line bg-surface"}`}
        >
          <span
            className={`absolute top-[2px] h-4 w-4 rounded-full transition-[left] duration-fast ${checked ? "left-[22px] bg-on-accent" : "left-[2px] bg-muted"}`}
          />
        </span>
      </span>
    </button>
  );
}
