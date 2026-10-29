import { readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { contrastRatio, relativeLuminance } from "./contrast";
import {
  BACKGROUND_ROLES,
  BOUNDARY_ROLES,
  FILLED_PAIRS,
  PALETTES,
  PALETTE_SELECTORS,
  PLAIN_BACKGROUNDS,
  ROLES,
  TEXT_ROLES,
  type Palette,
  type PaletteName,
} from "./tokens";

// Slice 0 of docs/ux/design-direction.md: the design doc's contrast table was computed by hand. This test is the
// source of truth: it recomputes every pair with the WCAG 2.x formula, and checks globals.css against tokens.ts.

const here = path.dirname(fileURLToPath(import.meta.url));
const SRC = path.resolve(here, "../..");
const css = readFileSync(path.join(SRC, "app/globals.css"), "utf8");

describe("WCAG contrast formula", () => {
  it("matches known reference values", () => {
    expect(relativeLuminance("#FFFFFF")).toBeCloseTo(1, 5);
    expect(relativeLuminance("#000000")).toBeCloseTo(0, 5);
    expect(contrastRatio("#000000", "#FFFFFF")).toBeCloseTo(21, 5);
    expect(contrastRatio("#FFFFFF", "#FFFFFF")).toBeCloseTo(1, 5);
    expect(contrastRatio("#767676", "#FFFFFF")).toBeCloseTo(4.54, 2); // the classic AA grey
  });
});

/** Resolves the token cascade in globals.css for one palette (blocks before the end marker, in source order). */
function resolveCss(name: PaletteName): Partial<Palette> {
  const tokensCss = css.split("/* end of palette tokens */")[0];
  const attrs: Record<PaletteName, string[]> = {
    paper: [],
    nightDesk: ['[data-theme="dark"]'],
    fog: ['[data-quiet="true"]'],
    dusk: ['[data-theme="dark"]', '[data-quiet="true"]'],
  };
  const out: Partial<Palette> = {};
  const blockRe = /(:root[^{]*)\{([^}]*)\}/g;
  for (const m of tokensCss.matchAll(blockRe)) {
    const selector = m[1].trim();
    const needed = selector.match(/\[[^\]]+\]/g) ?? [];
    if (!needed.every((a) => attrs[name].includes(a))) continue;
    for (const d of m[2].matchAll(/--c-([a-z-]+)\s*:\s*(#[0-9A-Fa-f]{6})/g)) {
      (out as Record<string, string>)[d[1]] = d[2].toUpperCase();
    }
  }
  return out;
}

describe.each(Object.keys(PALETTES) as PaletteName[])("palette %s", (name) => {
  const p = PALETTES[name];

  it(`globals.css (${PALETTE_SELECTORS[name]}) resolves to exactly the token values`, () => {
    const resolved = resolveCss(name);
    for (const role of ROLES) expect([role, resolved[role]]).toEqual([role, p[role].toUpperCase()]);
  });

  it.each(TEXT_ROLES.flatMap((t) => BACKGROUND_ROLES.map((b) => [t, b] as const)))("text %s on %s >= 4.5:1", (t, b) => {
    expect(contrastRatio(p[t], p[b])).toBeGreaterThanOrEqual(4.5);
  });

  it.each(FILLED_PAIRS)("filled control: %s on %s >= 4.5:1", (t, b) => {
    expect(contrastRatio(p[t], p[b])).toBeGreaterThanOrEqual(4.5);
  });

  it.each(BOUNDARY_ROLES.flatMap((u) => PLAIN_BACKGROUNDS.map((b) => [u, b] as const)))("boundary %s vs %s >= 3:1", (u, b) => {
    expect(contrastRatio(p[u], p[b])).toBeGreaterThanOrEqual(3);
  });
});

// The doc's hand-computed table (section 2.3). Hand arithmetic may be off by rounding; anything more than 0.02 away is
// flagged so the doc can be corrected. The pass/fail thresholds above, not these numbers, are what matter.
const HAND: [string, string, number][] = [
  ["#1C1B19", "#FFFFFF", 17.21],
  ["#1C1B19", "#F5F2EA", 15.38],
  ["#57534A", "#FFFFFF", 7.67],
  ["#2E3C8C", "#FFFFFF", 9.81],
  ["#2E3C8C", "#E6E8F5", 8.05],
  ["#B42318", "#FCEBE8", 5.69],
  ["#1B6B3A", "#E7F3EA", 5.73],
  ["#7A4300", "#FBF0D9", 7.06],
  ["#9F1D24", "#F5F2EA", 7.02],
  ["#8A8579", "#F5F2EA", 3.29],
  ["#ECEAE4", "#14161B", 15.04],
  ["#A3A7B3", "#242831", 6.14],
  ["#10131F", "#A9B8FF", 9.68],
  ["#FF8A7A", "#242831", 6.45],
  ["#7A7F90", "#242831", 3.7],
  ["#2B2A27", "#EEEDE8", 12.24],
  ["#5E5B54", "#EEEDE8", 5.78],
  ["#FFFFFF", "#3F4A6B", 8.74],
  ["#7F7B70", "#EEEDE8", 3.61],
  ["#D6D4CE", "#1C1F26", 11.13],
  ["#B4BCD6", "#14161B", 9.57],
];
it.each(HAND)("design doc hand value %s on %s is within 0.02 of %s", (a, b, hand) => {
  expect(Math.abs(contrastRatio(a, b) - hand)).toBeLessThanOrEqual(0.02);
});

// Guard rails from section 2.1 / 7: tokens are plain colours, so opacity modifiers would create unlisted pairs.
function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((f) => {
    const p = path.join(dir, f);
    if (statSync(p).isDirectory()) return sourceFiles(p);
    return /\.(tsx?|css)$/.test(f) && !/\.test\.tsx?$/.test(f) ? [p] : [];
  });
}
const files = sourceFiles(SRC).filter((f) => !f.includes(`${path.sep}mocks${path.sep}`));

it("no Tailwind opacity modifiers on colour utilities (text-ink/60 etc.)", () => {
  const bad = files.flatMap((f) =>
    [...readFileSync(f, "utf8").matchAll(/\b(?:text|bg|border|ring|outline|fill|stroke)-[a-z-]+\/\d{1,3}\b/g)].map((m) => `${path.relative(SRC, f)}: ${m[0]}`),
  );
  expect(bad).toEqual([]);
});

// Enabled in slice 1 (the doc's acceptance: "no bg-slate-*/text-slate-* left after slice 1").
it("no raw Tailwind palette colours remain (slate, blue, amber, red, ...): only design tokens", () => {
  const re = /\b(?:bg|text|border|ring|outline|from|to|via|fill|stroke|divide|shadow)-(?:slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose|white|black)(?:-\d{2,3})?\b/g;
  const bad = files.flatMap((f) => [...readFileSync(f, "utf8").matchAll(re)].map((m) => `${path.relative(SRC, f)}: ${m[0]}`));
  expect(bad).toEqual([]);
});

it("aria-live appears only in the transcript finals region (live-region allow-list, section 7)", () => {
  const withLive = files.filter((f) => /aria-live=/.test(readFileSync(f, "utf8"))).map((f) => path.relative(SRC, f));
  expect(withLive.sort()).toEqual(["components/lecture/TldrPanel.tsx", "components/lecture/Transcript.tsx"].sort());
});
