// Student-facing strings of Preferences > Look (redesign-round2 slice 5 = themes T2). Theme names and descriptions are
// the themes-spec 3.2 strings word for word (Q-PO1: the portal theme is labelled "Course portal"; no third-party name).
// Every string here goes through the language-guide copy lint (ThemePicker.test.tsx). No string may describe a theme as
// helping attention, focus or learning (themes-spec 6 "Copy").

import type { Skin } from "@/lib/design/tokens";
import type { ThemePref } from "@/lib/design/prefs";

export const THEMES: readonly { id: Skin; name: string; description: string }[] = [
  { id: "paper", name: "Paper", description: "Warm paper and ink. The default." },
  { id: "portal", name: "Course portal", description: "A familiar course-site layout: dark top bar, light page, plain cards." },
  { id: "gamer", name: "Gamer", description: "Technical look with sharp edges and a dark first feel. No game mechanics." },
  { id: "calm", name: "Calm", description: "Quiet colours, soft shapes, extra room." },
  { id: "glass", name: "Glass", description: "A frosted top bar and soft layers. Text always sits on solid panels." },
];

export const PREFS_COPY = {
  themeLegend: "Theme",
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
