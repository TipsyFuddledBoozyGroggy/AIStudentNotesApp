import type { Confidence, Decision } from "@/lib/syllabus/types";

// Shape icons that sit beside a text label (never colour or shape alone). currentColor, so forced colours keep them.

const base = { width: 16, height: 16, viewBox: "0 0 16 16", "aria-hidden": true, focusable: false, className: "shrink-0" } as const;

export function ConfidenceIcon({ value }: { value: Confidence }) {
  if (value === "looks_right")
    return (
      <svg {...base}>
        <path d="M3 8.5 6.5 12 13 4.5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    );
  if (value === "probably_right")
    return (
      <svg {...base}>
        <circle cx="8" cy="8" r="5.5" fill="none" stroke="currentColor" strokeWidth="2" />
      </svg>
    );
  return (
    <svg {...base}>
      <path d="M5.5 6a2.5 2.5 0 1 1 3.5 2.3c-.7.3-1 .8-1 1.5V10" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
      <circle cx="8" cy="13" r="1.1" fill="currentColor" />
    </svg>
  );
}

export function DecisionIcon({ value }: { value: Decision }) {
  if (value === "approved")
    return (
      <svg {...base}>
        <path d="M3 8.5 6.5 12 13 4.5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    );
  if (value === "skipped")
    return (
      <svg {...base}>
        <path d="M3.5 8h9" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
      </svg>
    );
  return null;
}

/** Disclosure chevron: the shared one (two glyphs, no rotation: redesign-round2 2.6 bans transforms). */
export { Chevron } from "../ui/Disclosure";
