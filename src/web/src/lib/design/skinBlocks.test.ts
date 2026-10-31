import { readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import tailwind from "../../../tailwind.config";
import { DISPLAY_PHONE, LAYER_B_LAYOUT_EXCEPTION, LAYER_B_VARS, LAYER_C_TOKENS, SCRIM, TYPE_SCALE } from "./tokens";

// Token contract v2 (docs/ux/redesign-round2.md section 3): Layer B (structure per skin) and Layer C (component tokens)
// as declared in globals.css, the "never varies between themes" list (3.3) as far as it is static CSS, and the source
// lints the slice-0 row of the plan asks for (skin blocks carry no layout; accent, half-step and arbitrary-px ratchets).
// Colour (Layer A) is tokens.test.ts.

const here = path.dirname(fileURLToPath(import.meta.url));
const SRC = path.resolve(here, "../..");
const rawCss = readFileSync(path.join(SRC, "app/globals.css"), "utf8");
const MARK = "/* end of palette tokens */";
const paletteCss = rawCss.slice(0, rawCss.indexOf(MARK));
const structureCss = rawCss.slice(rawCss.indexOf(MARK) + MARK.length).replace(/\/\*[\s\S]*?\*\//g, "");

// ---------------------------------------------------------------------------------------------------------------------
// A tiny brace parser: every `selector { declarations }` with the at-rules that enclose it.
interface Rule {
  selector: string;
  context: string[];
  decls: [string, string][];
}
function parseRules(src: string): Rule[] {
  const rules: Rule[] = [];
  const stack: string[] = [];
  let buf = "";
  for (let i = 0; i < src.length; i++) {
    const ch = src[i];
    if (ch === "{") {
      const prelude = buf.trim();
      buf = "";
      if (prelude.startsWith("@")) {
        stack.push(prelude);
        continue;
      }
      // Find the matching close brace (declaration blocks do not nest).
      const end = src.indexOf("}", i);
      const body = src.slice(i + 1, end);
      rules.push({
        selector: prelude.replace(/\s+/g, " "),
        context: [...stack],
        decls: body
          .split(";")
          .map((d) => d.trim())
          .filter(Boolean)
          .map((d) => {
            if (d.startsWith("@apply")) return ["@apply", d.slice(6).trim()] as [string, string];
            const k = d.indexOf(":");
            return [d.slice(0, k).trim(), d.slice(k + 1).replace(/\s+/g, " ").trim()] as [string, string];
          }),
      });
      i = end;
    } else if (ch === "}") {
      stack.pop();
      buf = "";
    } else buf += ch;
  }
  return rules;
}
const RULES = parseRules(structureCss);
const selectors = (r: Rule) => r.selector.split(",").map((s) => s.trim());
const inMedia = (r: Rule, q: string) => r.context.some((c) => c.includes(q));
const skinRules = RULES.filter((r) => r.selector.includes("[data-skin"));
/** A skin block proper: only attribute selectors on the themed element, no descendant, no class. */
const BARE = /^(:root)?(\[[a-z-]+(="[^"]*")?\]|:not\(\[[a-z-]+(="[^"]*")?\]\))+$/;
const bareSkinBlocks = skinRules.filter((r) => selectors(r).every((s) => BARE.test(s)));
const rootRules = RULES.filter((r) => selectors(r).every((s) => s === ":root"));

const LAYER_B = new Set(LAYER_B_VARS.map((v) => `--${v}`));
const LAYER_C = new Map(LAYER_C_TOKENS.map((t) => [`--${t.name}`, t]));

// ---------------------------------------------------------------------------------------------------------------------
describe("Layer A palette blocks (redesign-round2 3.2)", () => {
  it("contain no var(): every role is a literal hex so the cascade parser stays simple", () => {
    expect(paletteCss).not.toMatch(/var\(/);
  });
  it("declare --c-ledger in all 12 blocks", () => {
    expect(paletteCss.match(/--c-ledger\s*:/g)).toHaveLength(12);
  });
});

describe("Layer B skin blocks (rule R11, redesign-round2 3.1)", () => {
  it("exist for the four extra skins and are found by the parser", () => {
    const named = bareSkinBlocks.flatMap(selectors).join(" ");
    for (const skin of ["portal", "gamer", "calm", "glass"]) expect(named).toContain(`[data-skin="${skin}"]`);
  });

  it("declare only Layer B variables, the Layer C rows a skin may set, and Calm's --pad-card", () => {
    const bad: string[] = [];
    for (const r of bareSkinBlocks) {
      for (const [prop, value] of r.decls) {
        if (LAYER_B.has(prop)) continue;
        const c = LAYER_C.get(prop);
        if (c?.skinMaySet) continue;
        const ex = LAYER_B_LAYOUT_EXCEPTION;
        if (prop === `--${ex.variable}` && value === ex.value && selectors(r).every((s) => s.includes(`[data-skin="${ex.skin}"]`))) continue;
        bad.push(`${r.selector} { ${prop}: ${value} }`);
      }
    }
    expect(bad).toEqual([]);
  });

  it("Calm's --pad-card exception applies at Comfortable density only (the compact block comes later and wins)", () => {
    const calm = RULES.findIndex((r) => r.selector === '[data-skin="calm"]');
    const compact = RULES.findIndex((r) => r.selector === ':root[data-density="compact"]');
    expect(calm).toBeGreaterThan(-1);
    expect(compact).toBeGreaterThan(calm);
    expect(RULES[compact].decls).toContainEqual(["--pad-card", "1rem"]);
  });

  // The spec's lint: grep the skin blocks for layout properties and fail. Extended to every rule scoped to a skin.
  const LAYOUT_PROP =
    /^(padding|margin|gap|row-gap|column-gap|width|height|min-width|min-height|max-width|max-height|inline-size|block-size|display|position|order|font-size|line-height|top|right|bottom|left|inset|flex|grid|float|transform|translate|overflow)/;
  const LAYOUT_CLASS =
    /\b(-?(p|m|gap|space-[xy]|w|h|min-w|min-h|max-w|max-h|top|left|right|bottom|inset|order|leading|translate)-[\w[\]./-]+|text-(xs|sm|base|lg|xl|\dxl)|block|inline(-\w+)?|flex|grid|hidden|absolute|relative|fixed|sticky)\b/;
  const LAYOUT_TOKEN = /^--(?!c-)(.*(pad|gap|margin|width|height|col|inset|size).*|.*-(w|h))$/;
  it("no rule scoped to a skin sets padding, margin, gap, width, height, display, position, order, font-size or line-height", () => {
    const bad: string[] = [];
    for (const r of skinRules)
      for (const [prop, value] of r.decls) {
        const ex = LAYER_B_LAYOUT_EXCEPTION;
        if (prop === `--${ex.variable}` && value === ex.value) continue;
        if (LAYOUT_PROP.test(prop) || (prop === "@apply" && LAYOUT_CLASS.test(value)) || LAYOUT_TOKEN.test(prop)) bad.push(`${r.selector} { ${prop}: ${value} }`);
      }
    expect(bad).toEqual([]);
  });
});

describe("Layer C component tokens (redesign-round2 3.1 / 3.2)", () => {
  const rootDecls = new Map<string, string>();
  for (const r of rootRules.filter((r) => r.context.length === 0)) for (const [k, v] of r.decls) rootDecls.set(k, v);

  it.each(LAYER_C_TOKENS.map((t) => [t.name, t.default] as const))("--%s is declared once in :root as %s", (name, def) => {
    expect(rootDecls.get(`--${name}`)).toBe(def);
    const declaredIn = RULES.filter((r) => r.decls.some(([k]) => k === `--${name}`));
    expect(declaredIn.filter((r) => r.context.length === 0 && selectors(r).every((s) => s === ":root"))).toHaveLength(1);
  });

  it("no rule other than :root declares a Layer C token, except a skin block setting a row marked 'skin may set'", () => {
    const bad: string[] = [];
    for (const r of RULES) {
      if (selectors(r).every((s) => s === ":root")) continue;
      for (const [prop] of r.decls) {
        const c = LAYER_C.get(prop);
        if (!c) continue;
        if (c.skinMaySet && bareSkinBlocks.includes(r)) continue;
        bad.push(`${r.selector} { ${prop} }`);
      }
    }
    expect(bad).toEqual([]);
  });

  it("the serif/sans roles: synthesis follows the skin's heading face (Layer B), verbatim is the body face", () => {
    expect(rootDecls.get("--font-synthesis")).toBe("var(--font-head)");
    expect(rootDecls.get("--font-verbatim")).toMatch(/^var\(--font-body\)/);
    expect(tailwind.theme?.extend?.fontFamily).toMatchObject({ synthesis: "var(--font-synthesis)", verbatim: "var(--font-verbatim)" });
  });

  it("--scrim is per mode: .55 in :root and .55 in :root[data-theme=dark], nowhere else but a skin block", () => {
    expect(rootDecls.get("--scrim")).toBe(SCRIM.light);
    const dark = RULES.find((r) => r.selector === ':root[data-theme="dark"]' && r.context.length === 0);
    expect(dark?.decls).toContainEqual(["--scrim", SCRIM.dark]);
    for (const r of RULES.filter((r) => r.decls.some(([k]) => k === "--scrim")))
      expect([r.selector, r === dark || selectors(r).every((s) => s === ":root") || bareSkinBlocks.includes(r)]).toEqual([r.selector, true]);
  });

  it("ledger reaches Tailwind as bg-ledger / text-ledger via var(--c-ledger)", () => {
    expect((tailwind.theme?.extend?.colors as Record<string, string>).ledger).toBe("var(--c-ledger)");
  });
});

describe("Type scale (redesign-round2 2.1): line boxes on the 4px grid", () => {
  const rootDecls = new Map<string, string>();
  for (const r of rootRules.filter((r) => r.context.length === 0)) for (const [k, v] of r.decls) rootDecls.set(k, v);
  const px = (rem: string) => parseFloat(rem) * 16;

  it.each(Object.entries(TYPE_SCALE).filter(([k]) => k !== "display"))("--type-%s / --leading-%s declared, leading a multiple of 4px, size >= 13px", (k, v) => {
    expect(rootDecls.get(`--type-${k}`)).toBe(v.size);
    expect(rootDecls.get(`--leading-${k}`)).toBe(v.leading);
    expect(px(v.leading) % 4).toBe(0);
    expect(px(v.size)).toBeGreaterThanOrEqual(13);
    expect(px(v.leading)).toBeGreaterThan(px(v.size));
  });

  it("display: 34/40 below 640px, 44/48 from 640px; both line boxes on the grid; Tailwind text-display consumes the tokens", () => {
    expect(rootDecls.get("--text-display")).toBe(DISPLAY_PHONE.size);
    expect(rootDecls.get("--leading-display")).toBe(DISPLAY_PHONE.leading);
    const wide = RULES.find((r) => r.selector === ":root" && inMedia(r, "min-width: 640px"));
    expect(wide?.decls).toEqual([
      ["--text-display", TYPE_SCALE.display.size],
      ["--leading-display", TYPE_SCALE.display.leading],
    ]);
    for (const l of [DISPLAY_PHONE.leading, TYPE_SCALE.display.leading]) expect(px(l) % 4).toBe(0);
    expect((tailwind.theme?.extend?.fontSize as Record<string, unknown>).display).toEqual(["var(--text-display)", "var(--leading-display)"]);
  });

  it("the shipped Tailwind sizes are untouched in this slice (slice 1 moves line boxes; nothing may move here)", () => {
    expect(tailwind.theme?.extend?.fontSize).toMatchObject({ xs: ["0.8125rem", "1.4"], base: ["1.0625rem", "1.55"], "3xl": ["2.375rem", "1.2"] });
  });
});

// ---------------------------------------------------------------------------------------------------------------------
// What must never change between themes (redesign-round2 3.3), as far as it lives in CSS. Geometry across looks is the
// Playwright landmark-rect test (slice 8); DOM, roles and live regions are the component tests and tokens.test.ts.
describe("never varies between themes (redesign-round2 3.3)", () => {
  // Component classes live in @layer components; media/supports variants are looked up separately.
  const find = (sel: string) => RULES.find((r) => r.selector === sel && r.context.every((c) => c.startsWith("@layer")));

  it("focus ring: 3px focus colour + 2px offset + canvas halo, from the Layer C tokens; Highlight under forced colours", () => {
    const ring = find(":focus-visible");
    expect(ring?.decls).toEqual([
      ["outline", "var(--focus-w) solid var(--c-focus)"],
      ["outline-offset", "var(--focus-offset)"],
      ["box-shadow", "0 0 0 calc(var(--focus-w) + var(--focus-offset)) var(--c-canvas)"],
    ]);
    expect(LAYER_C.get("--focus-w")?.default).toBe("3px");
    expect(LAYER_C.get("--focus-offset")?.default).toBe("2px");
    const forced = RULES.find((r) => r.selector === ":focus-visible" && inMedia(r, "forced-colors: active"));
    expect(forced?.decls).toContainEqual(["outline-color", "Highlight"]);
  });

  it("control heights: .btn and .field are 44px, matching --control-h; Quick recap windows match --control-h-lg / -xl", () => {
    expect(LAYER_C.get("--control-h")?.default).toBe("44px");
    for (const cls of [".btn", ".field"]) expect(find(cls)?.decls.find(([k]) => k === "@apply")?.[1]).toContain("min-h-[44px]");
    const recap = readFileSync(path.join(SRC, "components/lecture/QuickRecapPanel.tsx"), "utf8");
    expect(recap).toContain("min-h-[64px]"); // the usual window
    expect(recap).toContain("min-h-[56px]");
    expect(LAYER_C.get("--control-h-lg")?.default).toBe("56px");
    expect(LAYER_C.get("--control-h-xl")?.default).toBe("64px");
  });

  it("transcript metrics: 17px body, 68ch measure, 3.25rem hanging timestamp column; no theme rule touches the lecture surfaces", () => {
    expect(find("body")?.decls).toContainEqual(["font-size", "1.0625rem"]);
    expect((tailwind.theme?.extend?.maxWidth as Record<string, string>).prose).toBe("68ch");
    const line = find(".transcript-line")?.decls.find(([k]) => k === "@apply")?.[1] ?? "";
    expect(line).toContain("grid-cols-[3.25rem_minmax(0,1fr)]");
    expect(line).toContain("max-w-prose");
    // Themes may restyle the Gamer timestamp face only (themes-spec 4), never the transcript, the lecture root, the pill or a panel.
    const FORBIDDEN = /\.transcript(?!-time)|\[data-lecture-root\]|#lecture-|state-label|\.panel|\.btn|\.field/;
    for (const r of skinRules) {
      expect([r.selector, FORBIDDEN.test(r.selector)]).toEqual([r.selector, false]);
      if (r.selector.includes(".transcript-time")) for (const [k] of r.decls) expect(["font-family", "font-variant-numeric"]).toContain(k);
    }
  });

  it("no decoration under lecture text: the page background layer is removed on lecture routes, and nothing is animated", () => {
    expect(find("html:has([data-lecture-root]) body::before")?.decls).toEqual([["background", "none"]]);
    expect(rawCss).not.toMatch(/@keyframes/);
    const anim = [...structureCss.matchAll(/animation(?:-[a-z-]+)?\s*:\s*([^;]+);/g)].map((m) => m[1].trim());
    expect(anim.length).toBeGreaterThan(0);
    for (const a of anim) expect(["none !important", "0.01ms !important", "1 !important"]).toContain(a);
  });

  it("Low-stimulation never touches a safety surface (recording pill, Paused/Not connected panels, Demo notices)", () => {
    const SAFETY = /panel-safety|panel-notice|recording|state-label|demo|connection|aria-live/i;
    for (const r of RULES.filter((r) => r.selector.includes("[data-quiet"))) expect([r.selector, SAFETY.test(r.selector)]).toEqual([r.selector, false]);
  });

  it("lecture bar and pill never use chrome roles: no [data-chrome] rule reaches into the lecture root, and the lecture route forces an opaque header", () => {
    for (const r of RULES.filter((r) => r.selector.includes("[data-chrome]"))) expect(r.selector).not.toMatch(/\[data-lecture-root\] [^,]*\[data-chrome\]/);
    expect(RULES.some((r) => r.selector.includes("html:has([data-lecture-root]) [data-chrome]") && r.decls.some(([k, v]) => k === "background" && v === "var(--c-chrome)"))).toBe(true);
  });
});

// ---------------------------------------------------------------------------------------------------------------------
// Source lints. Ratchets count today's offenders per file and fail on any increase (the plan's slice 0 row); slices 1
// and 2 bring the counts down and lower the baselines in the same change.
function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((f) => {
    const p = path.join(dir, f);
    if (statSync(p).isDirectory()) return sourceFiles(p);
    return /\.(tsx?|css)$/.test(f) && !/\.test\.tsx?$/.test(f) ? [p] : [];
  });
}
const files = sourceFiles(SRC).filter((f) => !f.includes(`${path.sep}mocks${path.sep}`));
const rel = (f: string) => path.relative(SRC, f).split(path.sep).join("/");
const countBy = (re: RegExp, filter: (m: string) => boolean = () => true) =>
  Object.fromEntries(
    files
      .map((f) => [rel(f), [...readFileSync(f, "utf8").matchAll(re)].map((m) => m[0]).filter(filter).length] as const)
      .filter(([, n]) => n > 0)
      .sort(([a], [b]) => a.localeCompare(b)),
  );
/** Fails on any file whose count rose above its baseline (or any new file with offenders); lower counts pass. */
function ratchet(actual: Record<string, number>, baseline: Record<string, number>) {
  const worse = Object.entries(actual).filter(([f, n]) => n > (baseline[f] ?? 0)).map(([f, n]) => `${f}: ${n} (baseline ${baseline[f] ?? 0})`);
  expect(worse, `counts rose: ${JSON.stringify(actual)}`).toEqual([]);
}

describe("source lints (redesign-round2 slice 0)", () => {
  it("no raw hex colours in any stylesheet outside the palette blocks, except the Glass gradient stops and the forced black/white", () => {
    const allowed = new Set(["#DCE6F7", "#ECE6F7", "#0F1322", "#171B30", "#000000", "#FFFFFF"]);
    const hexes = [...structureCss.matchAll(/#[0-9A-Fa-f]{6}\b/g)].map((m) => m[0].toUpperCase()).filter((h) => !allowed.has(h));
    expect(hexes).toEqual([]);
    for (const f of files.filter((f) => f.endsWith(".css") && !f.endsWith("globals.css"))) expect([rel(f), readFileSync(f, "utf8")]).not.toMatch(/#[0-9A-Fa-f]{6}\b/);
  });

  // Accent is spent on the one primary fill, the focus ring and citation/time links (redesign-round2 2.2). Until slice 2
  // retires accent text elsewhere, the count may only go down. Baseline recorded 2026-10-30.
  it("accent-text usage ratchet: no new text-accent outside today's files", () => {
    ratchet(countBy(/\btext-accent\b/g), {
      "app/globals.css": 3,
      "app/layout.tsx": 1,
      "components/DashboardView.tsx": 2,
      "components/lecture/ConsentStart.tsx": 1,
      "components/lecture/LectureScreen.tsx": 2,
      "components/lecture/MicBlocked.tsx": 1,
      "components/lecture/Notices.tsx": 1,
      "components/lecture/QuickRecapPanel.tsx": 1,
      "components/ui/Disclosure.tsx": 1,
    });
  });

  // themes-spec R1 / 5.3: half-step spacing classes are off the 4px scale. Slice 1 removes them (A13).
  it("half-step spacing ratchet: no new p-/m-/gap-/space- *.5 classes", () => {
    ratchet(countBy(/\b(?:p|m|gap|space-[xy]|inset|top|left|right|bottom)[xytblr]?-\d+\.5\b/g), {
      "app/globals.css": 2,
      "components/AppShell.tsx": 4,
      "components/CourseCard.tsx": 1,
      "components/SignInView.tsx": 1,
      "components/lecture/ConsentDialog.tsx": 1,
      "components/lecture/Notices.tsx": 2,
      "components/lecture/QuickRecapPanel.tsx": 1,
      "components/lecture/StatusBar.tsx": 2,
    });
  });

  // Arbitrary lengths outside the allowlist (control heights 44/56/64, the 48px primaries that slice 1 lowers to 44, the
  // 3.25rem timestamp column, the 0.2em underline offset).
  it("arbitrary-length ratchet: no new -[Npx]/-[Nrem]/... values outside the allowlist", () => {
    const ALLOW = /-\[(44px|48px|56px|64px|3\.25rem_|0\.2em)\]?/;
    ratchet(countBy(/-\[[0-9.]+(?:px|rem|em|vh|dvh|vw|ch)(?:\]|_)/g, (m) => !ALLOW.test(m)), {
      "app/globals.css": 4,
      "components/AppShell.tsx": 2,
      "components/CommandPalette.tsx": 2,
      "components/PreferencesPanel.tsx": 1,
      "components/lecture/ConsentStart.tsx": 1,
      "components/lecture/LectureScreen.tsx": 2,
      "components/lecture/MicBlocked.tsx": 1,
      "components/lecture/Notices.tsx": 1,
      "components/lecture/QuickRecapPanel.tsx": 9,
      "components/lecture/TldrPanel.tsx": 1,
      "components/lecture/Transcript.tsx": 2,
      "components/ui/Segmented.tsx": 1,
      "components/ui/Switch.tsx": 3,
    });
  });

  it("no Tailwind animate-* utilities in components (nothing loops, pulses or shimmers)", () => {
    const bad = files.filter((f) => f.endsWith(".tsx")).flatMap((f) => [...readFileSync(f, "utf8").matchAll(/\banimate-[a-z]+\b/g)].map((m) => `${rel(f)}: ${m[0]}`));
    expect(bad).toEqual([]);
  });
});
