"use client";

import { useId, useRef, type KeyboardEvent } from "react";

export interface SegmentedOption<T extends string> {
  value: T;
  label: string;
}

export interface SegmentedProps<T extends string> {
  legend: string;
  value: T;
  options: SegmentedOption<NoInfer<T>>[];
  onChange(next: NoInfer<T>): void;
  /** Optional one-line explanation under the group (described-by on the radiogroup). */
  hint?: string;
}

/**
 * Segmented control = radio group (2-4 text options). Arrow keys move and select, as native radios do; one Tab stop.
 * Selected = filled accent plus bold text plus aria-checked, so not colour alone.
 */
export function Segmented<T extends string>({ legend, value, options, onChange, hint }: SegmentedProps<T>) {
  const id = useId();
  const refs = useRef<(HTMLButtonElement | null)[]>([]);
  const index = Math.max(
    0,
    options.findIndex((o) => o.value === value),
  );
  const onKey = (e: KeyboardEvent) => {
    const d = e.key === "ArrowRight" || e.key === "ArrowDown" ? 1 : e.key === "ArrowLeft" || e.key === "ArrowUp" ? -1 : 0;
    if (!d) return;
    e.preventDefault();
    const next = (index + d + options.length) % options.length;
    onChange(options[next].value);
    refs.current[next]?.focus();
  };
  return (
    <div>
      <p id={id} className="font-semibold text-ink">
        {legend}
      </p>
      {/* Options share one width per group (auto-cols-fr), so a group's segments are equal and stacked groups keep one rhythm (redesign-round2 2.7). */}
      <div role="radiogroup" aria-labelledby={id} aria-describedby={hint ? `${id}-hint` : undefined} className="mt-2 inline-grid auto-cols-fr grid-flow-col gap-1 rounded-control border-2 border-line bg-surface p-1">
        {options.map((o, i) => {
          const on = o.value === value;
          return (
            <button
              key={o.value}
              ref={(el) => {
                refs.current[i] = el;
              }}
              type="button"
              role="radio"
              aria-checked={on}
              tabIndex={on ? 0 : -1}
              onKeyDown={onKey}
              onClick={() => onChange(o.value)}
              className={`min-h-[44px] min-w-[44px] rounded-[6px] px-4 text-sm ${on ? "bg-accent font-bold text-on-accent" : "font-semibold text-ink hover:bg-sunken"}`}
            >
              {o.label}
            </button>
          );
        })}
      </div>
      {hint ? (
        <p id={`${id}-hint`} className="help mt-1">
          {hint}
        </p>
      ) : null}
    </div>
  );
}
