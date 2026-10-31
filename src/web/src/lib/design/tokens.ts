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
  // redesign-round2 2.2: a list tint between canvas and surface (ledger rows, lecture rail). Paper sets its own value;
  // every other palette sets ledger = surface, so no new contrast pairs arise there.
  "ledger",
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

/**
 * Paper keeps its header on the surface colour: chrome = surface, on-chrome = ink, grid = canvas (themes-spec 2.4).
 * `ledger` defaults to surface (redesign-round2 2.2) unless the palette names its own tint (Paper light and dark do).
 */
const paperChrome = (p: Omit<Palette, "chrome" | "on-chrome" | "grid" | "ledger"> & { ledger?: string }): Palette => ({
  ...p,
  chrome: p.surface,
  "on-chrome": p.ink,
  grid: p.canvas,
  ledger: p.ledger ?? p.surface,
});

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
  ledger: "#FAF8F3",
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
  ledger: "#181B21",
});

// Fog and Dusk: ledger = their own surface (redesign-round2 2.2), so the Paper tints are overridden here on purpose.
const fog = paperChrome({
  ...paper,
  ledger: "#F6F5F1",
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
  ledger: "#1C1F26",
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
  "border-strong": "#6F7F8F", "border-hair": "#D5DCE3", focus: "#0A5AA8", chrome: "#1E3347", "on-chrome": "#FFFFFF", grid: "#F2F4F7", ledger: "#FFFFFF",
};
const portalDark: Palette = {
  canvas: "#10161C", surface: "#18202A", raised: "#202A36", sunken: "#0B1015", ink: "#E6ECF2", muted: "#A3B0BE",
  accent: "#6DB3F2", "on-accent": "#07182A", "accent-subtle": "#202A36", ...darkStatus("#202A36"),
  "border-strong": "#6B7B8C", "border-hair": "#26313D", focus: "#6DB3F2", chrome: "#0B1015", "on-chrome": "#E6ECF2", grid: "#10161C", ledger: "#18202A",
};
const gamerLight: Palette = {
  canvas: "#EEF1F5", surface: "#FFFFFF", raised: "#FFFFFF", sunken: "#DDE3EA", ink: "#0D1520", muted: "#455566",
  accent: "#006A85", "on-accent": "#FFFFFF", "accent-subtle": "#DDF1F7", ...lightStatus,
  "border-strong": "#66778A", "border-hair": "#D2DAE3", focus: "#006A85", chrome: "#0D1B2A", "on-chrome": "#E8EEF5", grid: "#E1E7EE", ledger: "#FFFFFF",
};
const gamerDark: Palette = {
  canvas: "#0A0E14", surface: "#101722", raised: "#172030", sunken: "#060A0F", ink: "#E8EEF5", muted: "#94A3B5",
  accent: "#4FD8F0", "on-accent": "#04141A", "accent-subtle": "#172030", ...darkStatus("#172030"),
  "border-strong": "#66778A", "border-hair": "#1F2A38", focus: "#4FD8F0", chrome: "#060A0F", "on-chrome": "#E8EEF5", grid: "#141C27", ledger: "#101722",
};
const calmLight: Palette = {
  canvas: "#EEF0EA", surface: "#F7F8F4", raised: "#F7F8F4", sunken: "#E2E6DD", ink: "#26302B", muted: "#55605A",
  accent: "#2F6B57", "on-accent": "#FFFFFF", "accent-subtle": "#E3EDE7", ...lightStatus,
  "border-strong": "#6E7B73", "border-hair": "#D8DDD3", focus: "#2F6B57", chrome: "#F7F8F4", "on-chrome": "#26302B", grid: "#EEF0EA", ledger: "#F7F8F4",
};
const calmDark: Palette = {
  canvas: "#151A18", surface: "#1D2321", raised: "#252C29", sunken: "#101412", ink: "#DCE3DE", muted: "#9DAAA3",
  accent: "#8FC7B0", "on-accent": "#0E1A15", "accent-subtle": "#252C29", ...darkStatus("#252C29"),
  "border-strong": "#6F7D75", "border-hair": "#2C3431", focus: "#8FC7B0", chrome: "#1D2321", "on-chrome": "#DCE3DE", grid: "#151A18", ledger: "#1D2321",
};
const glassLight: Palette = {
  canvas: "#DCE6F7", surface: "#FFFFFF", raised: "#FFFFFF", sunken: "#D9E2F1", ink: "#172033", muted: "#475569",
  accent: "#0A4FC9", "on-accent": "#FFFFFF", "accent-subtle": "#E4ECFB", ...lightStatus,
  "border-strong": "#62718C", "border-hair": "#C9D5EA", focus: "#0A4FC9", chrome: "#F4F7FD", "on-chrome": "#172033", grid: "#DCE6F7", ledger: "#FFFFFF",
};
const glassDark: Palette = {
  canvas: "#171B30", surface: "#1E2338", raised: "#272D47", sunken: "#0F1322", ink: "#E9ECF8", muted: "#A4ACC8",
  accent: "#8EA2FF", "on-accent": "#0B1030", "accent-subtle": "#272D47", ...darkStatus("#272D47"),
  "border-strong": "#6F7BA3", "border-hair": "#2E3554", focus: "#8EA2FF", chrome: "#121629", "on-chrome": "#E9ECF8", grid: "#171B30", ledger: "#1E2338",
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
  "ledger",
  "accent-subtle",
  "recording-subtle",
  "success-subtle",
  "warning-subtle",
];
/** Non-text UI boundaries (inputs, control edges, focus ring): 3:1 against every plain background (SC 1.4.11). */
export const BOUNDARY_ROLES: Role[] = ["border-strong", "focus", "accent", "recording", "warning", "danger"];
export const PLAIN_BACKGROUNDS: Role[] = ["canvas", "surface", "raised", "sunken", "ledger"];

/** Palettes that give `ledger` its own tint; everywhere else ledger = surface (redesign-round2 2.2). */
export const LEDGER_TINTED: PaletteName[] = ["paper", "nightDesk"];
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

// ---------------------------------------------------------------------------------------------------------------------
// Token contract v2 (redesign-round2 section 3). Three layers of CSS variables; skinBlocks.test.ts is the oracle.
// Layer A = the colour roles above (per skin x mode). Layer B = structure per skin. Layer C = component tokens, set once
// in :root from A and B; a skin may override only the rows marked `skinMaySet`.

/** Layer B: structure a skin block may set (themes-spec 2.5; redesign-round2 3.1). Mode-independent unless a skin says so. */
export const LAYER_B_VARS = [
  "font-head",
  "head-weight",
  "head-track",
  "r-control",
  "r-card",
  "r-sheet",
  "shadow-1",
  "shadow-2",
  "shadow-3",
  "scrim",
  "scrim-dialog",
  "page-bg",
  "blur",
  "chrome-glass",
  "card-deco",
  "dur-fast",
  "dur-base",
] as const;
/** The single sanctioned layout exception: Calm's roomier card padding (a step of the 4px scale), Comfortable density only. */
export const LAYER_B_LAYOUT_EXCEPTION = { skin: "calm" as Skin, variable: "pad-card", value: "2rem" } as const;

export interface ComponentToken {
  /** Variable name without the leading `--`. */
  name: string;
  /** The value globals.css must declare in :root. */
  default: string;
  /** Whether a skin block may redeclare it (redesign-round2 3.1, column "Skin may set"). */
  skinMaySet: boolean;
}
/** Layer C: component tokens. Components consume only these and Layer A roles. */
export const LAYER_C_TOKENS: ComponentToken[] = [
  { name: "sheet-bg", default: "var(--c-surface)", skinMaySet: false },
  { name: "sheet-border", default: "var(--c-border-hair)", skinMaySet: false },
  { name: "sheet-radius", default: "var(--r-sheet)", skinMaySet: true },
  { name: "ledger-bg", default: "var(--c-ledger)", skinMaySet: false },
  { name: "ledger-rule", default: "var(--c-border-hair)", skinMaySet: false },
  { name: "note-stripe-w", default: "4px", skinMaySet: false },
  { name: "btn-radius", default: "var(--r-control)", skinMaySet: true },
  { name: "btn-border-w", default: "2px", skinMaySet: false },
  { name: "link-ink", default: "var(--c-ink)", skinMaySet: false },
  { name: "link-line", default: "var(--c-accent)", skinMaySet: false },
  { name: "label-ink", default: "var(--c-muted)", skinMaySet: false },
  { name: "pill-h", default: "44px", skinMaySet: false },
  { name: "control-h", default: "44px", skinMaySet: false },
  { name: "control-h-lg", default: "56px", skinMaySet: false },
  { name: "control-h-xl", default: "64px", skinMaySet: false },
  { name: "focus-w", default: "3px", skinMaySet: false },
  { name: "focus-offset", default: "2px", skinMaySet: false },
  { name: "margin-col", default: "5rem", skinMaySet: false },
  { name: "aside-w", default: "20rem", skinMaySet: false },
  { name: "lecture-aside-w", default: "22.5rem", skinMaySet: false },
  { name: "stamp-ink", default: "var(--c-warning)", skinMaySet: false },
  { name: "stamp-border", default: "var(--c-warning)", skinMaySet: false },
  { name: "stamp-bg", default: "var(--c-warning-subtle)", skinMaySet: false },
  { name: "stamp-radius", default: "4px", skinMaySet: false },
  // Product Owner decision 2026-10-31: the serif is the voice of synthesis (what the lecture or the app concluded), the
  // sans is for verbatim words, controls and data. Roles only in slice 0; screens adopt them from slice 2 on. A skin
  // changes the synthesis face through Layer B (--font-head), never these.
  { name: "font-synthesis", default: "var(--font-head)", skinMaySet: false },
  { name: "font-verbatim", default: "var(--font-body), system-ui, sans-serif", skinMaySet: false },
];

/** Scrim behind the consent pop-up (and anything not yet moved to .dialog-scrim), per mode. */
export const SCRIM = { light: "rgba(20, 22, 27, 0.55)", dark: "rgba(20, 22, 27, 0.55)" } as const; // unchanged from before the refactor: the consent pop-up shares this backdrop (PO-17b, gate G1)
/**
 * Scrim behind every other dialog (Stop, Delete, the command palette): the spec's value (redesign-round2 2.7 Dialog,
 * .6 light / .7 dark), on its own variable so the consent pop-up, which keeps `--scrim`, is byte-identical (slice 2, G1).
 */
export const SCRIM_DIALOG = { light: "rgba(20, 22, 27, 0.6)", dark: "rgba(0, 0, 0, 0.7)" } as const;

/**
 * Slice 2 primitives: the foreground / background pairs each one draws, with the minimum ratio (4.5 text, 3 boundary).
 * Every pair is already inside the full matrix above; this list names which primitive depends on which pair so
 * tokens.test.ts reports them by name in all 12 palettes (redesign-round2 2.7 and 3.2).
 */
export const PRIMITIVE_PAIRS: { primitive: string; fg: Role; bg: Role; min: 4.5 | 3 }[] = [
  // .label: muted sentence-case label on canvas, on a sheet and in a ledger row.
  { primitive: "label", fg: "muted", bg: "canvas", min: 4.5 },
  { primitive: "label", fg: "muted", bg: "surface", min: 4.5 },
  { primitive: "label", fg: "muted", bg: "ledger", min: 4.5 },
  // .link: ink text with a 2px accent underline (the line is a boundary); hover text is accent.
  { primitive: "link", fg: "ink", bg: "canvas", min: 4.5 },
  { primitive: "link", fg: "ink", bg: "surface", min: 4.5 },
  { primitive: "link", fg: "ink", bg: "ledger", min: 4.5 },
  { primitive: "link line", fg: "accent", bg: "canvas", min: 3 },
  { primitive: "link line", fg: "accent", bg: "surface", min: 3 },
  { primitive: "link line", fg: "accent", bg: "ledger", min: 3 },
  { primitive: "link hover", fg: "accent", bg: "canvas", min: 4.5 },
  { primitive: "link hover", fg: "accent", bg: "surface", min: 4.5 },
  { primitive: "link hover", fg: "accent", bg: "ledger", min: 4.5 },
  // .btn-tertiary: ink text on whatever it sits on (no fill of its own).
  { primitive: "btn-tertiary", fg: "ink", bg: "canvas", min: 4.5 },
  { primitive: "btn-tertiary", fg: "ink", bg: "surface", min: 4.5 },
  { primitive: "btn-tertiary", fg: "ink", bg: "ledger", min: 4.5 },
  // Disabled button: muted text on sunken (WCAG exempts disabled controls; held to 4.5 anyway so the reason stays readable).
  { primitive: "btn disabled", fg: "muted", bg: "sunken", min: 4.5 },
  // .stamp: warning text and border on the warning tint.
  { primitive: "stamp", fg: "warning", bg: "warning-subtle", min: 4.5 },
  { primitive: "stamp border", fg: "warning", bg: "warning-subtle", min: 3 },
  // .stamp-line: the muted sentence beside the stamp.
  { primitive: "stamp-line", fg: "muted", bg: "canvas", min: 4.5 },
  { primitive: "stamp-line", fg: "muted", bg: "surface", min: 4.5 },
  // .note stripes against canvas, a sheet and a ledger (meaning also carried by shape and word).
  { primitive: "note stripe (notice)", fg: "warning", bg: "canvas", min: 3 },
  { primitive: "note stripe (notice)", fg: "warning", bg: "surface", min: 3 },
  { primitive: "note stripe (notice)", fg: "warning", bg: "ledger", min: 3 },
  { primitive: "note stripe (safety)", fg: "recording", bg: "canvas", min: 3 },
  { primitive: "note stripe (safety)", fg: "recording", bg: "surface", min: 3 },
  { primitive: "note stripe (danger)", fg: "danger", bg: "canvas", min: 3 },
  { primitive: "note stripe (danger)", fg: "danger", bg: "surface", min: 3 },
  { primitive: "note stripe (neutral)", fg: "border-strong", bg: "canvas", min: 3 },
  { primitive: "note stripe (neutral)", fg: "border-strong", bg: "surface", min: 3 },
  { primitive: "note stripe (neutral)", fg: "border-strong", bg: "ledger", min: 3 },
  // .ledger rows: ink and muted on the ledger tint; hover row on sunken.
  { primitive: "ledger row", fg: "ink", bg: "ledger", min: 4.5 },
  { primitive: "ledger row meta", fg: "muted", bg: "ledger", min: 4.5 },
  { primitive: "ledger row hover", fg: "ink", bg: "sunken", min: 4.5 },
  { primitive: "ledger row hover meta", fg: "muted", bg: "sunken", min: 4.5 },
  // .sheet and .dialog: body and helper text on surface.
  { primitive: "sheet", fg: "ink", bg: "surface", min: 4.5 },
  { primitive: "sheet helper", fg: "muted", bg: "surface", min: 4.5 },
  // Slice 4 (Today, course page, forms): the pairs those screens add beyond the primitives above. The focus ring on a
  // focused ledger row sits on the row's sunken tint; the trailing "Open" chevron is muted on the ledger and on the hover
  // tint (held to 3:1 although it is beside a word); the form's error-summary title is danger text on the sheet; the
  // "{course} added." line is success text on canvas.
  { primitive: "ledger row focus ring", fg: "accent", bg: "sunken", min: 3 },
  { primitive: "ledger row chevron", fg: "muted", bg: "ledger", min: 3 },
  { primitive: "ledger row chevron (hover)", fg: "muted", bg: "sunken", min: 3 },
  { primitive: "form error summary title", fg: "danger", bg: "surface", min: 4.5 },
  { primitive: "course added line", fg: "success", bg: "canvas", min: 4.5 },
];

/**
 * Type scale (redesign-round2 2.1): root 16px, body 17px; every line box a multiple of 4px so blocks sit on the spacing
 * grid. Declared as --type-* / --leading-* variables in :root in slice 0; Tailwind's fontSize keeps the shipped
 * line-heights until slice 1 (layout) switches to these, so nothing moves in this slice.
 */
export const TYPE_SCALE: Record<string, { size: string; leading: string }> = {
  xs: { size: "0.8125rem", leading: "1.25rem" },
  sm: { size: "0.9375rem", leading: "1.5rem" },
  base: { size: "1.0625rem", leading: "1.75rem" },
  lg: { size: "1.25rem", leading: "1.75rem" },
  xl: { size: "1.5rem", leading: "2rem" },
  "2xl": { size: "1.875rem", leading: "2.25rem" },
  display: { size: "2.75rem", leading: "3rem" },
};
/** The display step below 640px (34/40). */
export const DISPLAY_PHONE = { size: "2.125rem", leading: "2.5rem" } as const;

/** Spacing scale (redesign-round2 2.3 / themes-spec R1), px. Half steps and arbitrary px are banned except an allowlist. */
export const SPACING_SCALE_PX = [4, 8, 12, 16, 20, 24, 32, 40, 48, 64, 96] as const;

/**
 * Glass header: translucent chrome composited over the page gradient (themes-spec 2.3). Values must match globals.css
 * (--chrome-glass and --page-bg); tokens.test.ts checks that too.
 */
export const GLASS = {
  light: { chromeGlass: { rgb: "#F4F7FD", alpha: 0.72 }, stops: ["#DCE6F7", "#ECE6F7"] },
  dark: { chromeGlass: { rgb: "#121629", alpha: 0.72 }, stops: ["#0F1322", "#171B30"] },
} as const;
