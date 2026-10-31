// Student-facing strings of Preferences > Look (redesign-round2 slice 5 = themes T2). Theme names and descriptions are
// the themes-spec 3.2 strings word for word (Q-PO1: the portal theme is labelled "Course portal"; no third-party name).
// Every string here goes through the language-guide copy lint (ThemePicker.test.tsx). No string may describe a theme as
// helping attention, focus or learning (themes-spec 6 "Copy").

import type { Skin } from "@/lib/design/tokens";
import type { ThemePref } from "@/lib/design/prefs";

export const THEMES: readonly { id: Skin; name: string; description: string }[] = [
  { id: "paper", name: "Paper", description: "Cream page, dark ink, serif headings. The default." },
  { id: "portal", name: "Course portal", description: "The look of a course website: dark top bar, light grey page, small corners." },
  // Slice 7: the Grid texture switch shipped, so the first sentence names it (redesign-round2-review 3.1 allowed this then).
  { id: "gamer", name: "Gamer", description: "Sharp corners, monospace headings, an optional faint grid. No game mechanics." },
  { id: "calm", name: "Calm", description: "Muted green and grey, rounded shapes, no shadows." },
  { id: "glass", name: "Glass", description: "Frosted top bar on wide screens, soft gradient page. Text and the lecture always sit on solid surfaces." },
];

// Wording fixes (redesign-round2-review 3.1 / 3.2): a theme is paint only, so no description claims layout, cards,
// a dark mode or extra room; the Low-stimulation hint lives here (not as a prop literal) so the copy lint sees it.
export const PREFS_COPY = {
  themeLegend: "Theme",
  themeHint: "A theme changes colours, corners and heading type. Layout, words and controls stay the same.",
  lowStimLabel: "Low-stimulation look",
  lowStimHint: "Removes shadows, texture, gradients and movement. On Paper the colours soften too. Recording status is never dimmed.",
  // Slice 7 (themes T3 / T4): one switch per theme, shown only while that theme is chosen. Hints are the themes-spec 3.2
  // strings (the Grid hint as replaced by redesign-round2-review 3.2: the old one said "behind", which the lint rejects).
  gridLabel: "Grid texture",
  gridHint: "Adds a faint grid under the page. Never under lecture text.",
  gridMsg: { on: "Grid texture on.", off: "Grid texture off." },
  solidLabel: "Solid surfaces",
  solidHint: "Replaces the frosted bar with a solid one. Your device setting is respected too.",
  solidMsg: { on: "Solid surfaces on.", off: "Solid surfaces off." },
  // Moved from a prop literal into the linted registry in slice 7 (review 5.3: no hint="..." literal in Preferences).
  reduceMotionLabel: "Reduce motion",
  reduceMotionHint: "Turns off every transition. Your device setting is always respected too.",
  colourModeLegend: "Colour mode",
  colourModeHint: "System follows your device.",
  themeOn: (name: string) => `${name} theme on.`,
  mode: { system: "Colour mode follows your device.", light: "Light mode on.", dark: "Dark mode on." } satisfies Record<ThemePref, string>,
  reset: "Reset look to defaults",
  resetDone: "Look reset to defaults.",
  resetHint: "Theme, colour mode, text and motion go back to how they started. Lecture settings stay as they are.",
  previewCaption: "Preview with your current look",
  preview: {
    heading: "The chain rule",
    line1: "So the derivative of x squared is two x.",
    line2: "The power rule says bring the exponent down.",
    primary: "Last 5 min",
    secondary: "Pause",
    field: "Biology 101",
    recording: "Recording",
    paused: "Paused",
    minutes: "12 min",
  },
} as const;
