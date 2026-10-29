// Colour tokens. Paper / Night desk / Fog / Dusk: docs/ux/design-direction.md section 2.2 ("the reading room").
// The four extra skins (portal, gamer, calm, glass), each light and dark: docs/ux/themes-spec.md section 2.4.
// This object is the test oracle: tokens.test.ts checks that globals.css declares exactly these values (resolving the
// cascade for every look) and that every text/background pair the UI may use meets WCAG 2.2 AA. CSS variable names are
// the contract.

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
  // themes-spec 2.1: site header background (opaque), text and control edges inside it, decorative grid lines.
  "chrome",
  "on-chrome",
  "grid",
] as const;

export type Role = (typeof ROLES)[number];
export type Palette = Record<Role, string>;

/** Skins (themes-spec section 0). A skin is paint only: colour, radius, shadow, heading face, decoration. */
export const SKINS = ["paper", "portal", "gamer", "calm", "glass"] as const;
export type Skin = (typeof SKINS)[number];
export type Mode = "light" | "dark";

export type PaletteName =
  | "paper"
  | "nightDesk"
  | "fog"
  | "dusk"
  | "portalLight"
  | "portalDark"
  | "gamerLight"
  | "gamerDark"
  | "calmLight"
  | "calmDark"
  | "glassLight"
  | "glassDark";

/** Paper keeps its header on the surface colour: chrome = surface, on-chrome = ink, grid = canvas (themes-spec 2.4). */
const paperChrome = (p: Omit<Palette, "chrome" | "on-chrome" | "grid">): Palette => ({ ...p, chrome: p.surface, "on-chrome": p.ink, grid: p.canvas });

const paper = paperChrome({
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
});

const nightDesk = paperChrome({
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
});

const fog = paperChrome({
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
});

const dusk = paperChrome({
  ...nightDesk,
  ink: "#D6D4CE",
  accent: "#B4BCD6",
  focus: "#B4BCD6",
});

/** Status colours are shared per mode across the four new skins (themes-spec 2.2), so "only red is recording" holds. */
const lightStatus = {
  recording: "#B42318",
  "recording-subtle": "#FCEBE8",
  success: "#1B6B3A",
  "success-subtle": "#E7F3EA",
  warning: "#7A4300",
  "warning-subtle": "#FBF0D9",
  danger: "#9F1D24",
} as const;
/** Dark: each subtle tint equals that skin's `raised`. */
const darkStatus = (raised: string) =>
  ({
    recording: "#FF8A7A",
    "recording-subtle": raised,
    success: "#6FD39A",
    "success-subtle": raised,
    warning: "#F2B84B",
    "warning-subtle": raised,
    danger: "#FF8F98",
  }) as const;

const portalLight: Palette = {
  canvas: "#F2F4F7", surface: "#FFFFFF", raised: "#FFFFFF", sunken: "#E4E8ED", ink: "#1B2733", muted: "#4A5866",
  accent: "#0A5AA8", "on-accent": "#FFFFFF", "accent-subtle": "#E3EEF9", ...lightStatus,
  "border-strong": "#6F7F8F", "border-hair": "#D5DCE3", focus: "#0A5AA8", chrome: "#1E3347", "on-chrome": "#FFFFFF", grid: "#F2F4F7",
};
const portalDark: Palette = {
  canvas: "#10161C", surface: "#18202A", raised: "#202A36", sunken: "#0B1015", ink: "#E6ECF2", muted: "#A3B0BE",
  accent: "#6DB3F2", "on-accent": "#07182A", "accent-subtle": "#202A36", ...darkStatus("#202A36"),
  "border-strong": "#6B7B8C", "border-hair": "#26313D", focus: "#6DB3F2", chrome: "#0B1015", "on-chrome": "#E6ECF2", grid: "#10161C",
};
const gamerLight: Palette = {
  canvas: "#EEF1F5", surface: "#FFFFFF", raised: "#FFFFFF", sunken: "#DDE3EA", ink: "#0D1520", muted: "#455566",
  accent: "#006A85", "on-accent": "#FFFFFF", "accent-subtle": "#DDF1F7", ...lightStatus,
  "border-strong": "#66778A", "border-hair": "#D2DAE3", focus: "#006A85", chrome: "#0D1B2A", "on-chrome": "#E8EEF5", grid: "#E1E7EE",
};
const gamerDark: Palette = {
  canvas: "#0A0E14", surface: "#101722", raised: "#172030", sunken: "#060A0F", ink: "#E8EEF5", muted: "#94A3B5",
  accent: "#4FD8F0", "on-accent": "#04141A", "accent-subtle": "#172030", ...darkStatus("#172030"),
  "border-strong": "#66778A", "border-hair": "#1F2A38", focus: "#4FD8F0", chrome: "#060A0F", "on-chrome": "#E8EEF5", grid: "#141C27",
};
const calmLight: Palette = {
  canvas: "#EEF0EA", surface: "#F7F8F4", raised: "#F7F8F4", sunken: "#E2E6DD", ink: "#26302B", muted: "#55605A",
  accent: "#2F6B57", "on-accent": "#FFFFFF", "accent-subtle": "#E3EDE7", ...lightStatus,
  "border-strong": "#6E7B73", "border-hair": "#D8DDD3", focus: "#2F6B57", chrome: "#F7F8F4", "on-chrome": "#26302B", grid: "#EEF0EA",
};
const calmDark: Palette = {
  canvas: "#151A18", surface: "#1D2321", raised: "#252C29", sunken: "#101412", ink: "#DCE3DE", muted: "#9DAAA3",
  accent: "#8FC7B0", "on-accent": "#0E1A15", "accent-subtle": "#252C29", ...darkStatus("#252C29"),
  "border-strong": "#6F7D75", "border-hair": "#2C3431", focus: "#8FC7B0", chrome: "#1D2321", "on-chrome": "#DCE3DE", grid: "#151A18",
};
const glassLight: Palette = {
  canvas: "#DCE6F7", surface: "#FFFFFF", raised: "#FFFFFF", sunken: "#D9E2F1", ink: "#172033", muted: "#475569",
  accent: "#0A4FC9", "on-accent": "#FFFFFF", "accent-subtle": "#E4ECFB", ...lightStatus,
  "border-strong": "#62718C", "border-hair": "#C9D5EA", focus: "#0A4FC9", chrome: "#F4F7FD", "on-chrome": "#172033", grid: "#DCE6F7",
};
const glassDark: Palette = {
  canvas: "#171B30", surface: "#1E2338", raised: "#272D47", sunken: "#0F1322", ink: "#E9ECF8", muted: "#A4ACC8",
  accent: "#8EA2FF", "on-accent": "#0B1030", "accent-subtle": "#272D47", ...darkStatus("#272D47"),
  "border-strong": "#6F7BA3", "border-hair": "#2E3554", focus: "#8EA2FF", chrome: "#121629", "on-chrome": "#E9ECF8", grid: "#171B30",
};

export const PALETTES: Record<PaletteName, Palette> = {
  paper,
  nightDesk,
  fog,
  dusk,
  portalLight,
  portalDark,
  gamerLight,
  gamerDark,
  calmLight,
  calmDark,
  glassLight,
  glassDark,
};

/**
 * The attributes on <html> that select each palette (what prefs-init.js / applyPrefs set). tokens.test.ts resolves the
 * globals.css cascade for each of these attribute sets and compares it with PALETTES.
 */
export const PALETTE_LOOKS: Record<PaletteName, { skin: Skin; mode: Mode; quiet: boolean }> = {
  paper: { skin: "paper", mode: "light", quiet: false },
  nightDesk: { skin: "paper", mode: "dark", quiet: false },
  fog: { skin: "paper", mode: "light", quiet: true },
  dusk: { skin: "paper", mode: "dark", quiet: true },
  portalLight: { skin: "portal", mode: "light", quiet: false },
  portalDark: { skin: "portal", mode: "dark", quiet: false },
  gamerLight: { skin: "gamer", mode: "light", quiet: false },
  gamerDark: { skin: "gamer", mode: "dark", quiet: false },
  calmLight: { skin: "calm", mode: "light", quiet: false },
  calmDark: { skin: "calm", mode: "dark", quiet: false },
  glassLight: { skin: "glass", mode: "light", quiet: false },
  glassDark: { skin: "glass", mode: "dark", quiet: false },
};

/** The palette a skin x mode (x Low-stimulation) look uses. Only Paper swaps palette under Low-stimulation (themes-spec 3.1). */
export function paletteFor(skin: Skin, mode: Mode, quiet = false): PaletteName {
  if (skin === "paper") return mode === "dark" ? (quiet ? "dusk" : "nightDesk") : quiet ? "fog" : "paper";
  return `${skin}${mode === "dark" ? "Dark" : "Light"}` as PaletteName;
}

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
/** Filled controls: text on a filled background. Header text (on-chrome) on the header (chrome). */
export const FILLED_PAIRS: [Role, Role][] = [
  ["on-accent", "accent"],
  ["on-chrome", "chrome"],
];

/**
 * Skins whose header is a dark bar in light mode. Inside `[data-chrome]` globals.css maps ink, muted, accent, focus and the
 * borders to `on-chrome` for these, so only on-chrome/chrome pairs occur there. For the other skins the header keeps the
 * normal text roles, which must then also pass on `chrome` (CHROME_TEXT_ROLES).
 */
export const REMAPPED_CHROME_SKINS: Skin[] = ["portal", "gamer"];
export const CHROME_TEXT_ROLES: Role[] = ["ink", "muted", "accent"];

/** Gamer's grid texture: text that can sit on a grid line (themes-spec 2.3). */
export const GRID_TEXT_ROLES: Role[] = ["ink", "muted", "accent", "recording", "success"];

/**
 * Glass header: translucent chrome composited over the page gradient (themes-spec 2.3). Values must match globals.css
 * (--chrome-glass and --page-bg); tokens.test.ts checks that too.
 */
export const GLASS = {
  light: { chromeGlass: { rgb: "#F4F7FD", alpha: 0.72 }, stops: ["#DCE6F7", "#ECE6F7"] },
  dark: { chromeGlass: { rgb: "#121629", alpha: 0.72 }, stops: ["#0F1322", "#171B30"] },
} as const;
