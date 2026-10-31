"use client";

import { useId, useRef, type KeyboardEvent } from "react";
import type { Skin } from "@/lib/design/tokens";
import { PREFS_COPY, THEMES } from "./prefsCopy";

/**
 * Theme picker (themes-spec 3.2, redesign-round2 slice 5): a radiogroup of five cards. One Tab stop (the checked card);
 * arrow keys move and select, as native radios do; selecting applies at once (the caller saves and announces). Hover and
 * focus never preview: only selecting does. Each card is at least 64px tall and the whole card is the target. Three
 * swatch chips (canvas, surface, accent) are drawn by a nested data-skin / data-theme element, so they come from the
 * same palette blocks the app uses (no colour values here) and follow the current colour mode.
 */
export function ThemePicker({ value, mode, onChange }: { value: Skin; mode: "light" | "dark"; onChange(next: Skin): void }) {
  const id = useId();
  const refs = useRef<(HTMLButtonElement | null)[]>([]);
  const index = Math.max(
    0,
    THEMES.findIndex((t) => t.id === value),
  );
  const onKey = (e: KeyboardEvent) => {
    const d = e.key === "ArrowRight" || e.key === "ArrowDown" ? 1 : e.key === "ArrowLeft" || e.key === "ArrowUp" ? -1 : 0;
    if (!d) return;
    e.preventDefault();
    const next = (index + d + THEMES.length) % THEMES.length;
    onChange(THEMES[next].id);
    refs.current[next]?.focus();
  };
  return (
    <div>
      <p id={id} className="font-semibold text-ink">
        {PREFS_COPY.themeLegend}
      </p>
      {/* One answer for every card to "does it change the layout?" (redesign-round2-review 3.1; true by the landmark gate). */}
      <p id={`${id}-hint`} className="help mt-1">
        {PREFS_COPY.themeHint}
      </p>
      <div role="radiogroup" aria-labelledby={id} aria-describedby={`${id}-hint`} className="theme-cards mt-2">
        {THEMES.map((t, i) => {
          const on = t.id === value;
          return (
            <button
              key={t.id}
              ref={(el) => {
                refs.current[i] = el;
              }}
              type="button"
              role="radio"
              aria-checked={on}
              aria-labelledby={`${id}-${t.id}`}
              aria-describedby={`${id}-${t.id}-d`}
              tabIndex={on ? 0 : -1}
              data-theme-card={t.id}
              onKeyDown={onKey}
              onClick={() => onChange(t.id)}
              className="theme-card"
            >
              {/* A tiny page: the header bar (chrome), the page (canvas), a sheet (surface) with an accent bar. */}
              <span aria-hidden="true" data-skin={t.id} data-theme={mode} className="theme-swatch">
                <span className="theme-swatch-bar" />
                <span className="theme-swatch-sheet">
                  <span className="theme-swatch-accent" />
                </span>
              </span>
              <span className="min-w-0 flex-1">
                <span id={`${id}-${t.id}`} className="block font-semibold text-ink">
                  {t.name}
                </span>
                <span id={`${id}-${t.id}-d`} className="block text-sm text-muted">
                  {t.description}
                </span>
              </span>
              {/* Radio mark: a ring, filled when chosen (shape, not colour alone; the card border thickens too). */}
              <span aria-hidden="true" className="theme-mark" />
            </button>
          );
        })}
      </div>
    </div>
  );
}
