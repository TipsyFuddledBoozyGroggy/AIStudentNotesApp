// Student-owned look preferences (design-direction.md sections 2.4, 2.5, 6). Per-viewer convenience only, kept in
// localStorage under tether.prefs.v1. Storage can be missing or blocked: every read falls back to defaults.
// The lecture-scoped keys tether.lecture.settings.v1 and tether.lecture.quickRecapWindow.v1 keep working and are never deleted.

import { SKINS, type Skin } from "./tokens";

/** Colour mode (stored key stays `theme` for compatibility; the UI label is "Colour mode", themes-spec 3.1). */
export type ThemePref = "system" | "light" | "dark";
export type TextSize = "s" | "m" | "l";
export type Spacing = "standard" | "wide";
export type Density = "comfortable" | "compact";

export interface Prefs {
  theme: ThemePref;
  /** The theme (skin, themes-spec section 0). Never inferred from the device. */
  skin: Skin;
  /** Gamer only: faint grid behind the page. Off by default; never behind lecture text. */
  texture: boolean;
  /** Glass only: solid header instead of the frosted one. */
  solid: boolean;
  /** Low-stimulation look (Fog / Dusk palettes, no shadows, no transitions). Not the "Prompts: Quiet" engagement level. */
  quiet: boolean;
  textSize: TextSize;
  spacing: Spacing;
  density: Density;
  reduceMotion: boolean;
  /** Lecture Focus mode, remembered between lectures. Never turned on automatically. */
  focusMode: boolean;
  /** Recording time in the lecture bar with seconds (default: whole minutes, so nothing ticks every second). */
  showSeconds: boolean;
}

export const PREFS_KEY = "tether.prefs.v1";
const LECTURE_SETTINGS_KEY = "tether.lecture.settings.v1";

export const DEFAULT_PREFS: Prefs = {
  theme: "system",
  skin: "paper",
  texture: false,
  solid: false,
  quiet: false,
  textSize: "m",
  spacing: "standard",
  density: "comfortable",
  reduceMotion: false,
  focusMode: false,
  showSeconds: false,
};

const pick = <T extends string>(v: unknown, allowed: readonly T[], fallback: T): T =>
  typeof v === "string" && (allowed as readonly string[]).includes(v) ? (v as T) : fallback;

/** Look fields that "Reset look to defaults" restores (themes-spec 3.2). Lecture settings and Focus mode are not touched. */
export const LOOK_KEYS = ["skin", "theme", "quiet", "texture", "solid", "textSize", "spacing", "density", "reduceMotion"] as const;
export function lookDefaults(): Pick<Prefs, (typeof LOOK_KEYS)[number]> {
  return Object.fromEntries(LOOK_KEYS.map((k) => [k, DEFAULT_PREFS[k]])) as Pick<Prefs, (typeof LOOK_KEYS)[number]>;
}

/** Validates untrusted stored JSON field by field. */
export function parsePrefs(raw: unknown, legacyReduceMotion = false): Prefs {
  const p = raw && typeof raw === "object" ? (raw as Record<string, unknown>) : {};
  return {
    theme: pick(p.theme, ["system", "light", "dark"] as const, DEFAULT_PREFS.theme),
    // Additive fields, no version bump: older builds ignore them, so a rollback is safe. Unknown skin -> Paper.
    skin: pick(p.skin, SKINS, DEFAULT_PREFS.skin),
    texture: p.texture === true,
    solid: p.solid === true,
    quiet: p.quiet === true,
    textSize: pick(p.textSize, ["s", "m", "l"] as const, DEFAULT_PREFS.textSize),
    spacing: pick(p.spacing, ["standard", "wide"] as const, DEFAULT_PREFS.spacing),
    density: pick(p.density, ["comfortable", "compact"] as const, DEFAULT_PREFS.density),
    // Migration: before tether.prefs.v1 existed, Reduce motion lived in the lecture settings.
    reduceMotion: typeof p.reduceMotion === "boolean" ? p.reduceMotion : legacyReduceMotion,
    focusMode: p.focusMode === true,
    showSeconds: p.showSeconds === true,
  };
}

export function loadPrefs(): Prefs {
  try {
    const raw = window.localStorage.getItem(PREFS_KEY);
    let legacy = false;
    try {
      legacy = (JSON.parse(window.localStorage.getItem(LECTURE_SETTINGS_KEY) ?? "{}") as { reduceMotion?: unknown }).reduceMotion === true;
    } catch {
      legacy = false;
    }
    return parsePrefs(raw ? JSON.parse(raw) : null, legacy);
  } catch {
    return DEFAULT_PREFS;
  }
}

export function savePrefs(p: Prefs): void {
  try {
    window.localStorage.setItem(PREFS_KEY, JSON.stringify(p));
  } catch {
    /* storage unavailable: the choice applies for this visit only */
  }
}

export function resolveTheme(theme: ThemePref, systemDark: boolean): "light" | "dark" {
  return theme === "system" ? (systemDark ? "dark" : "light") : theme;
}

/**
 * Build-time rollback flag (themes-spec 6): NEXT_PUBLIC_THEMES=off forces Paper and hides the Theme control. Stored
 * choices are kept and ignored. Next inlines NEXT_PUBLIC_* at build time.
 */
export function themesEnabled(): boolean {
  return process.env.NEXT_PUBLIC_THEMES !== "off";
}

/** The attributes on <html> that globals.css keys off. Shared by the pre-hydration script and the React provider. */
export function htmlAttributes(p: Prefs, systemDark: boolean, themesOn = true): Record<string, string> {
  return {
    "data-skin": themesOn ? p.skin : "paper",
    "data-texture": p.texture ? "true" : "false",
    "data-solid": p.solid ? "true" : "false",
    "data-theme": resolveTheme(p.theme, systemDark),
    "data-quiet": p.quiet ? "true" : "false",
    "data-text-size": p.textSize,
    "data-spacing": p.spacing,
    "data-density": p.density,
    "data-reduce-motion": p.reduceMotion ? "true" : "false",
  };
}

export function applyPrefs(p: Prefs, root: HTMLElement = document.documentElement): void {
  const systemDark = typeof window.matchMedia === "function" && window.matchMedia("(prefers-color-scheme: dark)").matches;
  const themesOn = root.getAttribute("data-themes") !== "off" && themesEnabled();
  for (const [k, v] of Object.entries(htmlAttributes(p, systemDark, themesOn))) root.setAttribute(k, v);
}

/** Same-origin script loaded synchronously in <head> (public/prefs-init.js): sets the attributes before first paint. */
export const PRE_HYDRATION_SCRIPT_SRC = "/prefs-init.js";
