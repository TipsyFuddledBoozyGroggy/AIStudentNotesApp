import { readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { contrastRatio, hexToRgb, relativeLuminance } from "./contrast";
import {
  BACKGROUND_ROLES,
  BOUNDARY_ROLES,
  CHROME_TEXT_ROLES,
  FILLED_PAIRS,
  GLASS,
  GRID_TEXT_ROLES,
  LEDGER_TINTED,
  PALETTES,
  PALETTE_LOOKS,
  PLAIN_BACKGROUNDS,
  PRIMITIVE_PAIRS,
  REMAPPED_CHROME_SKINS,
  ROLES,
  SKINS,
  TEXT_ROLES,
  paletteFor,
  type Palette,
  type PaletteName,
  type Role,
} from "./tokens";

// Source of truth for colour (design-direction.md slice 0, themes-spec.md 2.3): the docs' contrast tables were computed
// by hand. This test recomputes every pair with the WCAG 2.x formula for all 12 palettes (Paper, Night desk, Fog, Dusk
// and the 8 skin x mode palettes), and checks that globals.css resolves to exactly tokens.ts for every look.

const here = path.dirname(fileURLToPath(import.meta.url));
const SRC = path.resolve(here, "../..");
const css = readFileSync(path.join(SRC, "app/globals.css"), "utf8");
const NAMES = Object.keys(PALETTES) as PaletteName[];

describe("WCAG contrast formula", () => {
  it("matches known reference values", () => {
    expect(relativeLuminance("#FFFFFF")).toBeCloseTo(1, 5);
    expect(relativeLuminance("#000000")).toBeCloseTo(0, 5);
    expect(contrastRatio("#000000", "#FFFFFF")).toBeCloseTo(21, 5);
    expect(contrastRatio("#FFFFFF", "#FFFFFF")).toBeCloseTo(1, 5);
    expect(contrastRatio("#767676", "#FFFFFF")).toBeCloseTo(4.54, 2); // the classic AA grey
  });
});

// ---------------------------------------------------------------------------------------------------------------------
// Cascade resolver: a tiny selector matcher for the palette blocks (":root", '[a="v"]', ":not([a])"), ordered by
// specificity then source order, like the browser.
type Attrs = Record<string, string>;
interface Block {
  selectors: string[];
  decls: [string, string][];
  order: number;
}
const tokensCss = css.split("/* end of palette tokens */")[0].replace(/\/\*[\s\S]*?\*\//g, "");
const BLOCKS: Block[] = [...tokensCss.matchAll(/([^{}]+)\{([^}]*)\}/g)].map((m, order) => ({
  selectors: (m[1].split(";").pop() ?? "").split(",").map((x) => x.trim()).filter(Boolean), // drop "@tailwind ...;" lines
  decls: [...m[2].matchAll(/--c-([a-z-]+)\s*:\s*(#[0-9A-Fa-f]{6})/g)].map((d) => [d[1], d[2].toUpperCase()]),
  order,
}));

/** Specificity (class/attribute count) of a selector if it matches an element with `attrs`; null if it does not. */
function match(selector: string, attrs: Attrs, isRoot: boolean): number | null {
  let rest = selector;
  let spec = 0;
  if (rest.startsWith(":root")) {
    if (!isRoot) return null;
    spec++;
    rest = rest.slice(5);
  }
  for (const m of rest.matchAll(/:not\(\[([a-z-]+)\]\)|\[([a-z-]+)="([^"]*)"\]/g)) {
    spec++;
    if (m[1]) {
      if (m[1] in attrs) return null;
    } else if (attrs[m[2]] !== m[3]) return null;
  }
  const leftover = rest.replace(/:not\(\[[a-z-]+\]\)|\[[a-z-]+="[^"]*"\]/g, "").trim();
  if (leftover) throw new Error(`palette selector not understood by the test: ${selector}`);
  return spec;
}

function resolve(attrs: Attrs, isRoot = true): Partial<Palette> {
  const hits: { spec: number; order: number; decls: [string, string][] }[] = [];
  for (const b of BLOCKS) {
    const specs = b.selectors.map((s) => match(s, attrs, isRoot)).filter((x): x is number => x !== null);
    if (specs.length) hits.push({ spec: Math.max(...specs), order: b.order, decls: b.decls });
  }
  hits.sort((a, b) => a.spec - b.spec || a.order - b.order);
  const out: Record<string, string> = {};
  for (const h of hits) for (const [k, v] of h.decls) out[k] = v;
  return out as Partial<Palette>;
}

const rootAttrs = (n: PaletteName): Attrs => {
  const l = PALETTE_LOOKS[n];
  return { "data-skin": l.skin, "data-theme": l.mode, "data-quiet": l.quiet ? "true" : "false" };
};

describe("globals.css cascade equals tokens.ts", () => {
  it.each(NAMES)("%s: <html> attributes resolve to exactly the token values", (n) => {
    const r = resolve(rootAttrs(n));
    for (const role of ROLES) expect([role, r[role]]).toEqual([role, PALETTES[n][role].toUpperCase()]);
  });

  it.each(NAMES.filter((n) => !PALETTE_LOOKS[n].quiet))("%s: a nested [data-skin][data-theme] element (theme swatch) re-scopes the palette", (n) => {
    const l = PALETTE_LOOKS[n];
    const r = resolve({ "data-skin": l.skin, "data-theme": l.mode }, false);
    for (const role of ROLES) expect([role, r[role]]).toEqual([role, PALETTES[n][role].toUpperCase()]);
  });

  it("no data-skin at all (prefs script blocked) still resolves Paper, Night desk, Fog and Dusk", () => {
    const eq = (a: Partial<Palette>, n: PaletteName) => ROLES.forEach((role) => expect([role, a[role]]).toEqual([role, PALETTES[n][role].toUpperCase()]));
    eq(resolve({}), "paper");
    eq(resolve({ "data-theme": "dark" }), "nightDesk");
    eq(resolve({ "data-quiet": "true" }), "fog");
    eq(resolve({ "data-theme": "dark", "data-quiet": "true" }), "dusk");
  });

  it("every palette block declares every role (no leakage between palettes)", () => {
    const paletteBlocks = BLOCKS.filter((b) => b.decls.length > 0);
    expect(paletteBlocks).toHaveLength(NAMES.length);
    for (const b of paletteBlocks) {
      const declared = b.decls.map(([k]) => k).sort();
      expect([b.selectors.join(", "), declared]).toEqual([b.selectors.join(", "), [...ROLES].sort()]);
    }
  });

  it("ledger: Paper light and dark have their own tint; every other palette sets ledger = surface (redesign-round2 2.2)", () => {
    expect(PALETTES.paper.ledger).toBe("#FAF8F3");
    expect(PALETTES.nightDesk.ledger).toBe("#181B21");
    for (const n of NAMES) {
      if (LEDGER_TINTED.includes(n)) {
        // Between canvas and surface in luminance, so rows can sit on the page without a box.
        const [lc, ll, ls] = [PALETTES[n].canvas, PALETTES[n].ledger, PALETTES[n].surface].map(relativeLuminance);
        expect(lc < ll && ll < ls || lc > ll && ll > ls).toBe(true);
      } else expect([n, PALETTES[n].ledger]).toEqual([n, PALETTES[n].surface]);
    }
  });

  it("Low-stimulation does not swap palette on the four new skins (their colours are tested as they are)", () => {
    for (const skin of SKINS.filter((s) => s !== "paper"))
      for (const mode of ["light", "dark"] as const) {
        const r = resolve({ "data-skin": skin, "data-theme": mode, "data-quiet": "true" });
        expect(r.canvas).toBe(PALETTES[paletteFor(skin, mode)].canvas.toUpperCase());
        expect(paletteFor(skin, mode, true)).toBe(paletteFor(skin, mode));
      }
  });
});

// ---------------------------------------------------------------------------------------------------------------------
// The full matrix, per palette. Stricter than the docs' tables (every text role on every background, not only listed pairs).
describe.each(NAMES)("palette %s", (name) => {
  const p = PALETTES[name];
  const skin = PALETTE_LOOKS[name].skin;

  it.each(TEXT_ROLES.flatMap((t) => BACKGROUND_ROLES.map((b) => [t, b] as const)))("text %s on %s >= 4.5:1", (t, b) => {
    expect(contrastRatio(p[t], p[b])).toBeGreaterThanOrEqual(4.5);
  });

  it.each(FILLED_PAIRS)("filled: %s on %s >= 4.5:1", (t, b) => {
    expect(contrastRatio(p[t], p[b])).toBeGreaterThanOrEqual(4.5);
  });

  it.each(BOUNDARY_ROLES.flatMap((u) => PLAIN_BACKGROUNDS.map((b) => [u, b] as const)))("boundary %s vs %s >= 3:1", (u, b) => {
    expect(contrastRatio(p[u], p[b])).toBeGreaterThanOrEqual(3);
  });

  it("header: on-chrome edges and focus vs chrome >= 3:1; unmapped skins' text roles on chrome >= 4.5:1", () => {
    expect(contrastRatio(p["on-chrome"], p.chrome)).toBeGreaterThanOrEqual(3);
    if (!REMAPPED_CHROME_SKINS.includes(skin)) {
      for (const t of CHROME_TEXT_ROLES) expect([t, contrastRatio(p[t], p.chrome) >= 4.5]).toEqual([t, true]);
      expect(contrastRatio(p["border-strong"], p.chrome)).toBeGreaterThanOrEqual(3);
      expect(contrastRatio(p.focus, p.chrome)).toBeGreaterThanOrEqual(3);
    }
  });

  if (skin === "gamer") {
    it.each(GRID_TEXT_ROLES)("grid texture: %s on grid >= 4.5:1", (t) => {
      expect(contrastRatio(p[t], p.grid)).toBeGreaterThanOrEqual(4.5);
    });
  }
});

// ---------------------------------------------------------------------------------------------------------------------
// Glass: the translucent header composited over each gradient stop, plus the stops' range (themes-spec 2.3).
const composite = (fg: string, alpha: number, bg: string) => {
  const f = hexToRgb(fg);
  const g = hexToRgb(bg);
  return `#${f.map((v, i) => Math.round(v * alpha + g[i] * (1 - alpha)).toString(16).padStart(2, "0")).join("")}`.toUpperCase();
};

describe.each(["light", "dark"] as const)("Glass %s", (mode) => {
  const g = GLASS[mode];
  const p = PALETTES[paletteFor("glass", mode)];
  const block = mode === "dark" ? /\[data-skin="glass"\]\[data-theme="dark"\]\s*\{([^}]*)\}/ : /\[data-skin="glass"\]\s*\{([^}]*)\}/;
  const decl = css.slice(css.indexOf("/* end of palette tokens */")).match(block)?.[1] ?? "";

  it("globals.css --chrome-glass and --page-bg stops equal the tested values", () => {
    const [r, gg, b] = hexToRgb(g.chromeGlass.rgb);
    expect(decl.replace(/\s+/g, "")).toContain(`--chrome-glass:rgba(${r},${gg},${b},${g.chromeGlass.alpha})`);
    for (const stop of g.stops) expect(decl.toUpperCase()).toContain(stop.toUpperCase());
    expect(g.chromeGlass.rgb.toUpperCase()).toBe(p.chrome.toUpperCase()); // the opaque fallback is the same colour
  });

  it.each(g.stops)("header text on chrome at alpha .72 over stop %s: on-chrome, ink, muted, accent >= 4.5:1, border-strong >= 3:1", (stop) => {
    const bg = composite(g.chromeGlass.rgb, g.chromeGlass.alpha, stop);
    for (const t of ["on-chrome", "ink", "muted", "accent"] as const) expect([t, contrastRatio(p[t], bg) >= 4.5]).toEqual([t, true]);
    expect(contrastRatio(p["border-strong"], bg)).toBeGreaterThanOrEqual(3);
  });

  it("gradient stops never give page text a worse background than the tested plain backgrounds", () => {
    const ls = PLAIN_BACKGROUNDS.map((r) => relativeLuminance(p[r]));
    for (const stop of g.stops) {
      const l = relativeLuminance(stop);
      if (mode === "light") expect(l).toBeGreaterThanOrEqual(Math.min(...ls) - 1e-9);
      else expect(l).toBeLessThanOrEqual(Math.max(...ls) + 1e-9);
      for (const t of TEXT_ROLES) expect([t, stop, contrastRatio(p[t], stop) >= 4.5]).toEqual([t, stop, true]);
    }
  });
});

// ---------------------------------------------------------------------------------------------------------------------
// The docs' hand-computed values. Hand arithmetic may be off by rounding; anything more than 0.02 away is flagged so the
// doc can be corrected. The pass/fail thresholds above, not these numbers, are what matter.
const HAND: [string, string, number][] = [
  // design-direction.md 2.3
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

// themes-spec.md 2.3 table: ink, muted, accent, on-accent/accent, recording, success, warning, danger, border-strong on the
// worst background (sunken in light, raised in dark), and on-chrome/chrome. Three doc values were rounded by up to 0.04
// (gamerDark warning 9.10 -> 9.13, calmDark on-chrome 12.20 -> 12.24, glassLight on-chrome 15.20 -> 15.16); they are
// listed here with the recomputed value and the doc value in a comment. All pass AA by a wide margin.
const SPEC_TABLE: Record<string, number[]> = {
  portalLight: [12.32, 5.92, 5.61, 6.9, 5.35, 5.31, 6.49, 6.38, 3.34, 12.97],
  portalDark: [12.2, 6.58, 6.49, 8.0, 6.34, 7.93, 8.1, 6.65, 3.35, 16.05],
  gamerLight: [14.19, 5.92, 4.78, 6.18, 5.09, 5.06, 6.18, 6.07, 3.55, 14.9],
  gamerDark: [13.98, 6.35, 9.65, 11.08, 7.13, 8.91, 9.13 /* doc 9.10 */, 7.48, 3.56, 17.0],
  calmLight: [10.77, 5.17, 4.94, 6.25, 5.2, 5.17, 6.31, 6.2, 3.5, 12.78],
  calmDark: [10.93, 5.92, 7.45, 9.31, 6.23, 7.79, 7.97, 6.54, 3.31, 12.24 /* doc 12.20 */],
  glassLight: [12.47, 5.81, 5.41, 7.06, 5.04, 5.01, 6.12, 6.01, 3.78, 15.16 /* doc 15.20 */],
  glassDark: [11.48, 6.0, 5.64, 7.75, 5.9, 7.38, 7.55, 6.2, 3.25, 15.2],
};
// redesign-round2.md 2.4: the new `ledger` role, hand-computed for Paper light (#FAF8F3) and Night desk (#181B21). The
// recomputation wins; a disagreement beyond 0.02 is flagged here and the doc is corrected ("Slice 0 results").
const LEDGER_HAND: [PaletteName, Role, number, number][] = [
  ["paper", "ink", 16.21, 4.5],
  ["paper", "muted", 7.23, 4.5],
  ["paper", "accent", 9.24, 4.5],
  ["paper", "recording", 6.2, 4.5],
  ["paper", "success", 6.16, 4.5],
  ["paper", "warning", 7.53, 4.5],
  ["paper", "danger", 7.4, 4.5],
  ["paper", "border-strong", 3.47, 3],
  ["nightDesk", "ink", 14.33, 4.5],
  ["nightDesk", "muted", 7.17, 4.5],
  ["nightDesk", "accent", 9.02, 4.5],
  ["nightDesk", "recording", 7.53, 4.5],
  ["nightDesk", "success", 9.41, 4.5],
  ["nightDesk", "warning", 9.63, 4.5],
  ["nightDesk", "danger", 7.91, 4.5],
  ["nightDesk", "border-strong", 4.33, 3],
];
it.each(LEDGER_HAND)("redesign-round2 2.4: %s %s on ledger recomputes to %s (within 0.02) and meets %s:1", (n, role, hand, need) => {
  const got = contrastRatio(PALETTES[n][role], PALETTES[n].ledger);
  expect(got).toBeGreaterThanOrEqual(need);
  expect([role, Math.abs(got - hand) <= 0.02, Number(got.toFixed(2))]).toEqual([role, true, Number(got.toFixed(2))]);
});

it.each(Object.entries(SPEC_TABLE))("themes-spec hand values for %s are within 0.02", (n, hand) => {
  const p = PALETTES[n as PaletteName];
  const bg = n.endsWith("Dark") ? p.raised : p.sunken;
  const got = [
    contrastRatio(p.ink, bg),
    contrastRatio(p.muted, bg),
    contrastRatio(p.accent, bg),
    contrastRatio(p["on-accent"], p.accent),
    contrastRatio(p.recording, bg),
    contrastRatio(p.success, bg),
    contrastRatio(p.warning, bg),
    contrastRatio(p.danger, bg),
    contrastRatio(p["border-strong"], bg),
    contrastRatio(p["on-chrome"], p.chrome),
  ];
  got.forEach((g, i) => expect([i, Math.abs(g - hand[i]) <= 0.02]).toEqual([i, true]));
});

// ---------------------------------------------------------------------------------------------------------------------
// Guard rails from design-direction 2.1 / 7: tokens are plain colours, so opacity modifiers would create unlisted pairs.
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

it("no raw hex colours in components (themes add colour through tokens only)", () => {
  const bad = files
    .filter((f) => /\.tsx$/.test(f))
    .flatMap((f) => [...readFileSync(f, "utf8").matchAll(/#[0-9A-Fa-f]{6}\b/g)].map((m) => `${path.relative(SRC, f)}: ${m[0]}`));
  expect(bad).toEqual([]);
});

it("aria-live appears only in the transcript finals region (live-region allow-list, section 7)", () => {
  const withLive = files.filter((f) => /aria-live=/.test(readFileSync(f, "utf8"))).map((f) => path.relative(SRC, f).split(path.sep).join("/"));
  expect(withLive.sort()).toEqual(["components/lecture/TldrPanel.tsx", "components/lecture/Transcript.tsx"].sort());
});

// ---------------------------------------------------------------------------------------------------------------------
// Slice 2 primitives (redesign-round2 2.7): every foreground / background pair a primitive draws, named by primitive, in
// all 12 palettes. Each pair is also inside the full matrix above; this block records which primitive depends on which
// pair, so a future token change shows what it would break.
describe.each(NAMES)("slice 2 primitives on palette %s", (name) => {
  const p = PALETTES[name];
  it.each(PRIMITIVE_PAIRS.map((x) => [x.primitive, x.fg, x.bg, x.min] as const))("%s: %s on %s >= %s:1", (_primitive, fg, bg, min) => {
    expect(contrastRatio(p[fg], p[bg])).toBeGreaterThanOrEqual(min);
  });
});
