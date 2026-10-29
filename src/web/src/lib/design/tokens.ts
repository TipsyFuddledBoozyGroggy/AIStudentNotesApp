// Colour tokens from docs/ux/design-direction.md section 2.2 ("the reading room").
// This object is the test oracle: tokens.test.ts checks that globals.css declares exactly these values and that
// every text/background pair the UI may use meets WCAG 2.2 AA. CSS variable names are the contract.

export const ROLES = [
  "canvas",
  "surface",
  "raised",
  "sunken",
  "ink",
  "muted",
  "accent",
  "on-accent",
  "accent-subtle",
  "recording",
  "recording-subtle",
  "success",
  "success-subtle",
  "warning",
  "warning-subtle",
  "danger",
  "border-strong",
  "border-hair",
  "focus",
] as const;

export type Role = (typeof ROLES)[number];
export type Palette = Record<Role, string>;
export type PaletteName = "paper" | "nightDesk" | "fog" | "dusk";

const paper: Palette = {
  canvas: "#F5F2EA",
  surface: "#FFFFFF",
  raised: "#FFFFFF",
  sunken: "#ECE8DE",
  ink: "#1C1B19",
  muted: "#57534A",
  accent: "#2E3C8C",
  "on-accent": "#FFFFFF",
  "accent-subtle": "#E6E8F5",
  recording: "#B42318",
  "recording-subtle": "#FCEBE8",
  success: "#1B6B3A",
  "success-subtle": "#E7F3EA",
  warning: "#7A4300",
  "warning-subtle": "#FBF0D9",
  danger: "#9F1D24",
  "border-strong": "#8A8579",
  "border-hair": "#DAD5C8",
  focus: "#2E3C8C",
};

const nightDesk: Palette = {
  canvas: "#14161B",
  surface: "#1C1F26",
  raised: "#242831",
  sunken: "#0F1115",
  ink: "#ECEAE4",
  muted: "#A3A7B3",
  accent: "#A9B8FF",
  "on-accent": "#10131F",
  "accent-subtle": "#242831",
  recording: "#FF8A7A",
  "recording-subtle": "#242831",
  success: "#6FD39A",
  "success-subtle": "#242831",
  warning: "#F2B84B",
  "warning-subtle": "#242831",
  danger: "#FF8F98",
  "border-strong": "#7A7F90",
  "border-hair": "#2E333D",
  focus: "#A9B8FF",
};

const fog: Palette = {
  ...paper,
  canvas: "#EEEDE8",
  surface: "#F6F5F1",
  raised: "#F6F5F1",
  sunken: "#E4E2DB",
  ink: "#2B2A27",
  muted: "#5E5B54",
  accent: "#3F4A6B",
  "accent-subtle": "#F6F5F1",
  recording: "#A12B20",
  "recording-subtle": "#F6F5F1",
  "success-subtle": "#F6F5F1",
  "warning-subtle": "#F6F5F1",
  "border-strong": "#7F7B70",
  "border-hair": "#D6D4CC",
  focus: "#3F4A6B",
};

const dusk: Palette = {
  ...nightDesk,
  ink: "#D6D4CE",
  accent: "#B4BCD6",
  focus: "#B4BCD6",
};

export const PALETTES: Record<PaletteName, Palette> = { paper, nightDesk, fog, dusk };

/** CSS selector in globals.css that declares each palette. */
export const PALETTE_SELECTORS: Record<PaletteName, string> = {
  paper: ":root",
  nightDesk: ':root[data-theme="dark"]',
  fog: ':root[data-quiet="true"]',
  dusk: ':root[data-theme="dark"][data-quiet="true"]',
};

/** Text-like roles: every one must reach 4.5:1 on every background role. */
export const TEXT_ROLES: Role[] = ["ink", "muted", "accent", "recording", "success", "warning", "danger"];
export const BACKGROUND_ROLES: Role[] = [
  "canvas",
  "surface",
  "raised",
  "sunken",
  "accent-subtle",
  "recording-subtle",
  "success-subtle",
  "warning-subtle",
];
/** Non-text UI boundaries (inputs, control edges, focus ring): 3:1 against every plain background (SC 1.4.11). */
export const BOUNDARY_ROLES: Role[] = ["border-strong", "focus", "accent", "recording", "warning", "danger"];
export const PLAIN_BACKGROUNDS: Role[] = ["canvas", "surface", "raised", "sunken"];
/** Filled controls: text on a filled background. */
export const FILLED_PAIRS: [Role, Role][] = [["on-accent", "accent"]];
