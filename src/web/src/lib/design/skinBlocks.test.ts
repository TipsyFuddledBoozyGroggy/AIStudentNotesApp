import { readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import tailwind from "../../../tailwind.config";
import { DISPLAY_PHONE, LAYER_B_LAYOUT_EXCEPTION, LAYER_B_VARS, LAYER_C_TOKENS, SCRIM, SCRIM_DIALOG, TYPE_SCALE } from "./tokens";

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

  // Slice 2: the spec's dialog scrim (.6 light / .7 dark) lives on its own variable so the consent pop-up keeps --scrim.
  it("--scrim-dialog is per mode (.6 light / .7 dark) and only .dialog-scrim consumes it; the consent pop-up keeps --scrim", () => {
    expect(rootDecls.get("--scrim-dialog")).toBe(SCRIM_DIALOG.light);
    const dark = RULES.find((r) => r.selector === ':root[data-theme="dark"]' && r.context.length === 0);
    expect(dark?.decls).toContainEqual(["--scrim-dialog", SCRIM_DIALOG.dark]);
    const consumers = RULES.filter((r) => r.decls.some(([, v]) => v.includes("var(--scrim-dialog)")));
    expect(consumers.map((r) => r.selector)).toEqual([".dialog-scrim"]);
    const consentDialog = readFileSync(path.join(SRC, "components/lecture/ConsentDialog.tsx"), "utf8");
    expect(consentDialog).toContain("bg-[var(--scrim)]");
    expect(consentDialog).not.toMatch(/className="[^"]*(?<![\w-])(dialog-scrim|dialog|sheet|btn-tertiary|label|stamp|note)(?![\w-])/);
  });
});

// ---------------------------------------------------------------------------------------------------------------------
// Slice 2 primitives (redesign-round2 2.7 / 3.2): each consumes Layer C tokens and Layer A roles only.
describe("primitives v2 consume the Layer C tokens (redesign-round2 slice 2)", () => {
  const find = (sel: string) => RULES.find((r) => r.selector === sel && r.context.every((c) => c.startsWith("@layer")));
  const decls = (sel: string) => Object.fromEntries(find(sel)?.decls ?? []) as Record<string, string>;
  const apply = (sel: string) => decls(sel)["@apply"] ?? "";

  it(".sheet: surface, hair border, --sheet-radius, --pad-card; square-edged below 640px; .sheet-bleed is the phone gutter", () => {
    expect(decls(".sheet")).toEqual({ background: "var(--sheet-bg)", border: "1px solid var(--sheet-border)", "border-radius": "var(--sheet-radius)", padding: "var(--pad-card)" });
    const phone = RULES.find((r) => r.selector === ".sheet" && inMedia(r, "max-width: 639px"));
    expect(phone?.decls).toEqual([
      ["border-radius", "0"],
      ["border-inline-width", "0"],
    ]);
    expect(RULES.find((r) => r.selector === ".sheet-bleed" && inMedia(r, "max-width: 639px"))?.decls).toEqual([["margin-inline", "-1rem"]]);
  });

  it(".ledger: ledger tint, rows parted by --ledger-rule, 16px row padding, text on the column edge (16px bleed)", () => {
    expect(decls(".ledger").background).toBe("var(--ledger-bg)");
    expect(decls(".ledger")["margin-inline"]).toBe("calc(-1 * var(--ledger-inset))");
    expect(decls(".ledger-row").padding).toBe("1rem var(--ledger-inset)");
    expect(decls(".ledger-row + .ledger-row")["border-top"]).toBe("1px solid var(--ledger-rule)");
    expect(decls(".ledger-row-link::after")).toMatchObject({ position: "absolute", inset: "0" });
  });

  it(".note: 4px --note-stripe-w stripe, 16px padding, stripe colour per kind from the status roles, no outer border", () => {
    expect(decls(".note")["padding-inline-start"]).toBe("calc(1rem + var(--note-stripe-w))");
    expect(decls(".note").padding).toBe("1rem");
    expect(decls(".note")).not.toHaveProperty("border");
    expect(decls(".note::before").width).toBe("var(--note-stripe-w)");
    expect(decls(".note-notice")["--note-stripe"]).toBe("var(--c-warning)");
    expect(decls(".note-safety")["--note-stripe"]).toBe("var(--c-recording)");
    expect(decls(".note-danger")["--note-stripe"]).toBe("var(--c-danger)");
    expect(decls(".note-neutral")["--note-stripe"]).toBe("var(--c-border-strong)");
    expect(apply(".note-title")).toContain("text-lg");
    expect(apply(".note-title")).toContain("font-bold");
  });

  it(".stamp: the four --stamp-* tokens, xs 700 in the verbatim face, 28px tall; .stamp-line is sm muted", () => {
    expect(decls(".stamp")).toMatchObject({
      border: "1px solid var(--stamp-border)",
      "border-radius": "var(--stamp-radius)",
      background: "var(--stamp-bg)",
      color: "var(--stamp-ink)",
      "font-family": "var(--font-verbatim)",
      "font-size": "var(--type-xs)",
      "line-height": "var(--leading-xs)",
      "font-weight": "700",
      "min-height": "1.75rem",
    });
    expect(decls(".stamp-line")).toMatchObject({ "font-size": "var(--type-sm)", "line-height": "var(--leading-sm)", color: "var(--c-muted)" });
  });

  it(".label: sentence case (no transform, no tracking), sm 15/24 regular in the verbatim face, --label-ink", () => {
    expect(decls(".label")).toEqual({
      "font-family": "var(--font-verbatim)",
      "font-size": "var(--type-sm)",
      "line-height": "var(--leading-sm)",
      "font-weight": "400",
      "letter-spacing": "0",
      "text-transform": "none",
      color: "var(--label-ink)",
    });
    // The form field label kept its look under a new name.
    expect(apply(".field-label")).toBe("font-semibold text-ink");
  });

  it(".link: --link-ink text, 2px --link-line underline at 0.2em; hover accent text; .btn-tertiary: ink text, underline on hover and focus", () => {
    expect(decls(".link")).toMatchObject({ color: "var(--link-ink)", "text-decoration-color": "var(--link-line)", "text-decoration-thickness": "2px" });
    expect(apply(".link")).toContain("underline-offset-[0.2em]");
    expect(decls(".link:hover").color).toBe("var(--c-accent)");
    expect(apply(".btn-tertiary")).toContain("text-ink");
    expect(apply(".btn-tertiary")).toContain("border-transparent");
    expect(find(".btn-tertiary:hover, .btn-tertiary:focus-visible")?.decls).toEqual([["text-decoration-color", "currentColor"]]);
  });

  it(".btn: radius and border width from --btn-radius / --btn-border-w; disabled is solid hair border, muted text, no opacity", () => {
    expect(decls(".btn")).toMatchObject({ "border-radius": "var(--btn-radius)", "border-width": "var(--btn-border-w)" });
    const disabled = apply('.btn-disabled, .btn[aria-disabled="true"]:not(.btn-steady)');
    expect(disabled).toContain("border-solid");
    expect(disabled).toContain("border-hair");
    expect(disabled).toContain("text-muted");
    expect(disabled).not.toMatch(/dashed|opacity/);
    expect(structureCss).not.toMatch(/\.btn[^{]*\{[^}]*opacity/);
  });

  it(".dialog / .dialog-scrim: a 28rem sheet with the dialog shadow over --scrim-dialog", () => {
    expect(decls(".dialog-scrim")).toEqual({ background: "var(--scrim-dialog)" });
    expect(apply(".dialog")).toContain("max-w-narrow");
    expect(apply(".dialog")).toContain("shadow-3");
    expect(decls(".dialog")).toMatchObject({ background: "var(--sheet-bg)", border: "1px solid var(--sheet-border)", "border-radius": "var(--sheet-radius)", padding: "var(--pad-card)" });
  });

  it("A5 / R8: .hover-row bleeds 8px outward and is 100% + 1rem wide; Switch uses it; Segmented options share one width", () => {
    expect(apply(".hover-row")).toBe("-mx-2 px-2");
    expect(decls(".hover-row").width).toBe("calc(100% + 1rem)");
    expect(readFileSync(path.join(SRC, "components/ui/Switch.tsx"), "utf8")).toMatch(/className="group hover-row /);
    expect(readFileSync(path.join(SRC, "components/ui/Segmented.tsx"), "utf8")).toContain("inline-grid auto-cols-fr grid-flow-col");
  });

  // Gate G1: the consent surfaces keep their pre-slice-2 look. Where they share a restyled class (.link, the disabled
  // .btn), a carve-out keyed on attributes the consent components already carry restores the old declarations; where a
  // legacy class exists (.btn-quiet, .eyebrow, .tag), it is unchanged and used only there. consent-look.spec.ts asserts
  // the computed values in a browser.
  it("G1: consent carve-outs restore the legacy link and dashed disabled look; .btn-quiet and .eyebrow are unchanged", () => {
    const disabledConsent = find('section:has(> #consent-h1) .btn[aria-disabled="true"], [data-consent-dialog-overlay] .btn[aria-disabled="true"]');
    expect(disabledConsent?.decls).toEqual([["@apply", "border-dashed border-line"]]);
    const linkConsent = find("section:has(> #consent-h1) .link, section:has(> #consent-h1) .link:hover");
    expect(Object.fromEntries(linkConsent?.decls ?? [])).toEqual({ "@apply": "text-accent", "padding-block": "0", "text-decoration-color": "currentColor", "text-decoration-thickness": "auto" });
    // The 24px link hit area (padding-block) is on .link and .ledger-row-link, and reset to 0 inside the consent page.
    expect(decls(".link")["padding-block"]).toBe("0.25rem");
    expect(decls(".ledger-row-link")["padding-block"]).toBe("0.25rem");
    expect(apply(".btn-quiet")).toBe("btn border-transparent bg-transparent px-3 text-accent");
    expect(find(".btn-quiet:hover")?.decls).toEqual([["@apply", "border-hair"]]);
    expect(apply(".eyebrow")).toBe("text-xs font-bold uppercase tracking-[0.08em] text-muted");
    expect(apply(".tag")).toBe("inline-flex items-center gap-1 rounded-full border px-2.5 py-0.5 text-xs font-semibold leading-snug");
  });
});

// ---------------------------------------------------------------------------------------------------------------------
// Slice 3 (redesign-round2 2.3 Lecture template, 5.1 / 5.2): the lecture sheet, the margin track, the rail ledger, the
// synthesis role, citation links and the states, as far as they are static CSS. Browser geometry is geometry.spec.ts and
// e2e-harness/redesign.spec.ts.
describe("lecture screen (redesign-round2 slice 3)", () => {
  const find = (sel: string, ctx?: string) => RULES.find((r) => r.selector === sel && (ctx ? inMedia(r, ctx) : r.context.every((c) => c.startsWith("@layer"))));
  const decls = (sel: string, ctx?: string) => Object.fromEntries(find(sel, ctx)?.decls ?? []) as Record<string, string>;
  const unlayered = (sel: string, ctx?: string) => Object.fromEntries(RULES.find((r) => r.selector === sel && (ctx ? inMedia(r, ctx) : r.context.length === 0))?.decls ?? []) as Record<string, string>;

  it(".lecture-sheet consumes the sheet tokens with a fixed 24px inset (Calm's --pad-card does not reach the lecture sheets)", () => {
    expect(decls(".lecture-sheet")).toEqual({ background: "var(--sheet-bg)", border: "1px solid var(--sheet-border)", "border-radius": "var(--sheet-radius)" });
    expect(decls(".sheet-head")["@apply"]).toContain("px-4");
    expect(decls(".sheet-head")["@apply"]).toContain("sm:px-6");
    expect(decls(".sheet-body")["@apply"]).toContain("sm:px-6");
    expect(structureCss).not.toMatch(/\.lecture-sheet[^{]*\{[^}]*--pad-card/);
    // Phone: square-edged and full-bleed (2.7 Sheet).
    expect(unlayered("[data-lecture-root] .lecture-sheet", "max-width: 639px")).toEqual({ "border-radius": "0", "border-inline-width": "0", "margin-inline": "-1rem" });
  });

  it("the transcript grows with its content: a viewport bound (max-height) and a 12rem floor, never a fixed height", () => {
    expect(decls(".transcript")).toEqual({ "max-height": "50vh", "min-height": "12rem" });
    expect(unlayered("[data-lecture-root] .transcript")).toEqual({ "max-height": "max(12rem, calc(100dvh - 23rem))" });
    expect(unlayered("[data-lecture-root] .transcript", "min-width: 1024px")).toEqual({ "max-height": "max(22rem, calc(100vh - 16rem))" });
    expect(unlayered('[data-lecture-root] [data-layout="focus"] .transcript', "min-width: 1024px")).toEqual({ "max-height": "max(16rem, calc(100vh - 26rem))" });
    expect(structureCss).not.toMatch(/\.transcript\s*\{[^}]*[^-]height\s*:/);
  });

  it("the margin track from 1280: the sheet paints from margin + gap, text sits at margin + gap + 24 + 1, rows hang in the margin", () => {
    const sheet = unlayered("[data-lecture-root] .lecture-sheet", "min-width: 1280px");
    expect(sheet["--sheet-start"]).toBe("calc(var(--margin-col) + 2.5rem)");
    expect(sheet["--hang-gap"]).toBe("calc(2.5rem + 1.5rem + 1px)");
    expect(sheet["--hang-text"]).toBe("calc(var(--margin-col) + var(--hang-gap))");
    expect(sheet.background).toBe("none");
    const paint = unlayered("[data-lecture-root] .lecture-sheet::before", "min-width: 1280px");
    expect(paint).toMatchObject({ inset: "0 0 0 var(--sheet-start)", background: "var(--sheet-bg)", border: "1px solid var(--sheet-border)", "pointer-events": "none" });
    expect(unlayered("[data-lecture-root] .lecture-sheet > .sheet-head, [data-lecture-root] .lecture-sheet > .sheet-body", "min-width: 1280px")).toEqual({ "padding-inline-start": "var(--hang-text)" });
    expect(unlayered("[data-lecture-root] .transcript-line, [data-lecture-root] .hang-row", "min-width: 1280px")).toEqual({ "grid-template-columns": "var(--margin-col) minmax(0, 1fr)", "column-gap": "var(--hang-gap)" });
    expect(unlayered("[data-lecture-root] .transcript-line", "min-width: 1280px")).toEqual({ "max-width": "calc(68ch + var(--hang-text))" });
    expect(unlayered("[data-lecture-root] .transcript-divider, [data-lecture-root] .transcript-partial, [data-lecture-root] .hang-indent", "min-width: 1280px")).toEqual({ "margin-inline-start": "var(--hang-text)" });
    // The screen's grids use the same track and gap (LectureScreen.tsx), so the paint starts where the transcript track does.
    const screen = readFileSync(path.join(SRC, "components/lecture/LectureScreen.tsx"), "utf8");
    expect(screen).toContain("xl:grid-cols-[var(--margin-col)_minmax(0,1fr)_var(--lecture-aside-w)]");
    expect(screen).toContain("xl:grid-cols-[var(--margin-col)_minmax(0,1fr)] xl:gap-x-10");
    expect(screen).toContain("xl:grid-cols-subgrid");
  });

  it("slice 8: from 1280 times and lead citations are right-aligned in the margin track; below 1280 no rule moves them; desk labels stay left", () => {
    expect(unlayered("[data-lecture-root] .transcript-time, [data-lecture-root] .hang-row > :first-child", "min-width: 1280px")).toEqual({
      "justify-self": "end",
      "text-align": "end",
      "align-items": "flex-end",
    });
    expect(unlayered("[data-lecture-root] .hang-row > :first-child .cite", "min-width: 1280px")).toEqual({ "justify-content": "flex-end" });
    // Only inside the 1280 query: no other rule aligns a time or a lead column to the end.
    for (const r of RULES.filter((r) => /transcript-time|hang-row > :first-child/.test(r.selector) && r.decls.some(([k, v]) => /justify-self|text-align|justify-content/.test(k) && /end/.test(v))))
      expect(inMedia(r, "min-width: 1280px"), r.selector).toBe(true);
    for (const r of RULES.filter((r) => r.selector.includes("desk-label"))) for (const [k, v] of r.decls) expect([k, v]).not.toEqual(["text-align", "end"]);
  });

  it("slice 8: the collapsed TL;DR line is 20/28 with a 56px reserve below 640 and 24/32 + 64 from 640 (review 1.1); expanded stays 24/32", () => {
    const tldr = readFileSync(path.join(SRC, "components/lecture/TldrPanel.tsx"), "utf8");
    expect(tldr).toContain('collapsed ? "line-clamp-2 min-h-14 text-lg sm:min-h-16 sm:text-xl" : "text-xl"');
  });

  it(".lecture-rail is a ledger (tint, rule between rows, no bleed) and sticks under the bar from 1024; .rail-title is sans 15/24 bold ink", () => {
    expect(decls(".lecture-rail")).toEqual({ "--ledger-inset": "1rem", background: "var(--ledger-bg)" });
    expect(decls(".rail-row + .rail-row")).toEqual({ "border-top": "1px solid var(--ledger-rule)" });
    expect(unlayered("[data-lecture-root] .lecture-rail", "min-width: 1024px")).toMatchObject({ position: "sticky", top: "4.5rem", "max-height": "calc(100vh - 7rem)", "overflow-y": "auto" });
    expect(decls(".rail-title")).toEqual({ "font-family": "var(--font-verbatim)", "font-size": "var(--type-sm)", "line-height": "var(--leading-sm)", "font-weight": "700", color: "var(--c-ink)" });
  });

  it(".synthesis is the heading face at 500 (PO-17); .cite is accent from the token, tabular, 44px without growing its row", () => {
    expect(decls(".synthesis")).toEqual({ "font-family": "var(--font-synthesis)", "font-weight": "500", "letter-spacing": "var(--head-track)", "font-optical-sizing": "auto" });
    expect(decls(".cite")).toMatchObject({ color: "var(--c-accent)", "min-height": "var(--control-h)", "min-width": "var(--control-h)", "padding-block": "0.5rem", "margin-block": "-0.5rem", "text-decoration-thickness": "1px" });
    expect(decls(".cite")["@apply"]).toContain("tabular-nums");
    expect(find(".cite:hover, .cite:focus-visible")?.decls).toEqual([["text-decoration-thickness", "2px"]]);
    for (const f of ["components/lecture/QuickRecapPanel.tsx", "components/lecture/TldrPanel.tsx"]) {
      const src = readFileSync(path.join(SRC, f), "utf8");
      expect([f, /\btext-accent\b/.test(src)]).toEqual([f, false]);
      expect(src).toContain('className="cite"');
    }
  });

  it("states: a pressed tertiary is bold with the 3px ink underline; inside the lecture a filled primary answers hover with an on-accent inner edge, not an underline", () => {
    expect(decls('.btn-tertiary[aria-pressed="true"]')).toEqual({ "@apply": "font-bold", "box-shadow": "inset 0 -3px 0 var(--c-ink)" });
    expect(unlayered("[data-lecture-root] .btn-primary:hover")).toEqual({ "text-decoration-color": "transparent", "box-shadow": "inset 0 0 0 1px var(--c-on-accent)" });
    // Only inside the lecture root: the consent surfaces keep the generic hover (G1).
    expect(RULES.filter((r) => r.selector.includes(".btn-primary:hover")).map((r) => r.selector)).toEqual(["[data-lecture-root] .btn-primary:hover"]);
  });

  // Rewritten in slice 8b (PO-18c supersedes the PO-17b / G1 presentation pin for the Demo-source notice only): the notice
  // is now a one-line Note with its body behind "Details", the bar's Demo tag is gone, and the four safety notes carry the
  // state tint except Fatal (no danger-subtle role). The wording stays byte-identical: the two strings are quoted here as
  // they were before the slice (make-notes-rail-demo-notice 2.1 / 2.6 item 4).
  it("safety surfaces are notes (Paused, Offline, Mismatch filled; Stopped and Fatal unfilled); the Demo-source notice is a one-line Note with Details; no Demo tag in the bar (PO-18c)", () => {
    const notices = readFileSync(path.join(SRC, "components/lecture/Notices.tsx"), "utf8");
    expect(notices).toContain('data-panel="paused" className="note note-notice note-filled"');
    expect(notices).toContain('data-panel="mismatch" className="note note-safety note-filled"');
    expect(notices).toContain('data-banner="offline" className="note note-notice note-filled');
    expect(notices).toContain('data-panel="stopped" className="note note-neutral"');
    expect(notices).toContain('data-testid="fatal-panel" aria-labelledby="fatal-h2" className="note note-danger"');
    expect(notices).toContain('data-testid="demo-notice" aria-labelledby="demo-notice-h2" className="note note-notice demo-note"');
    expect(notices).toContain('<h2 id="demo-notice-h2" className="note-title demo-note-title">');
    expect(notices).toContain('className="btn-tertiary btn-sm demo-note-toggle"');
    expect(notices).toContain('aria-controls="demo-notice-body"');
    expect(notices).toContain('<p id="demo-notice-body" className="demo-note-body text-sm text-muted" hidden={!open}>');
    const demo = notices.slice(notices.indexOf("export function DemoNotice("), notices.indexOf("export function FatalPanel("));
    expect(demo).toMatch(/^export function DemoNotice\(\) \{/); // the compact prop is gone
    expect(demo).not.toMatch(/<details|<summary|compact|text-accent/);
    expect(notices).not.toMatch(/className="[^"]*\bpanel\b/);
    expect(decls(".note-filled.note-notice")).toEqual({ "background-color": "var(--c-warning-subtle)" });
    expect(decls(".note-filled.note-safety")).toEqual({ "background-color": "var(--c-recording-subtle)" });
    expect(RULES.some((r) => r.selector.includes("note-danger") && r.selector.includes("note-filled"))).toBe(false);
    expect(decls(".notice-slot + .notice-slot")).toEqual({ "margin-top": "1.5rem" });
    const copy = readFileSync(path.join(SRC, "components/lecture/copy.ts"), "utf8");
    expect(copy).toContain('heading: "Demo source: these words are not from your microphone",');
    expect(copy).toContain(
      'body: "The transcript is a fixed test script that plays as audio arrives. No real speech service is listening, so what you say is not turned into text. Audio is only used to time the script and is not kept. (Draft wording.)",',
    );
    const bar = readFileSync(path.join(SRC, "components/lecture/StatusBar.tsx"), "utf8");
    expect(bar).not.toMatch(/demo-tag|DEMO_SOURCE_TAG|demoSource/);
  });
});

// ---------------------------------------------------------------------------------------------------------------------
// Slice 4 (redesign-round2 2.3 Desk / Sheet / Auth, 2.7 Margin label, 5.3): the Desk margin section, the page sheet, and
// no boxes left on Today, the course page, All courses, Add a course and sign-in. Browser geometry is geometry.spec.ts.
describe("Today, course page, forms, sign-in (redesign-round2 slice 4)", () => {
  const find = (sel: string, ctx?: string) => RULES.find((r) => r.selector === sel && (ctx ? inMedia(r, ctx) : r.context.every((c) => c.startsWith("@layer"))));
  const decls = (sel: string, ctx?: string) => Object.fromEntries(find(sel, ctx)?.decls ?? []) as Record<string, string>;
  const SLICE4 = [
    "components/DashboardView.tsx",
    "components/DashboardPage.tsx",
    "components/CourseCard.tsx",
    "components/CourseDetailView.tsx",
    "components/CourseDetailPage.tsx",
    "components/AllCoursesPage.tsx",
    "components/CourseForm.tsx",
    "components/SignInView.tsx",
    "components/syllabus/SyllabusCard.tsx",
  ];
  const src = (f: string) => readFileSync(path.join(SRC, f), "utf8");

  it("a Desk section stacks label / action / body below 1024 and hangs its label in the 5rem margin from 1024", () => {
    expect(decls(".desk-section")).toMatchObject({ display: "grid", "grid-template-areas": '"label action" "body body"', "align-items": "baseline" });
    expect(decls(".desk-body")).toMatchObject({ "grid-area": "body", "min-width": "0" });
    // From 1024 a margin label never sizes its row: a wrapped label hangs beside what follows (landmark gate across faces).
    expect(decls('main[data-template="desk"] .desk-label', "min-width: 1024px")).toEqual({ "padding-inline-end": "1rem", "margin-block-end": "-3rem" });
    const wide = decls('main[data-template="desk"] .desk-section', "min-width: 1024px");
    expect(wide).toEqual({
      "grid-template-columns": "calc(var(--margin-col) + 2rem) minmax(0, 1fr) auto",
      "grid-template-areas": '"label body body"',
      "column-gap": "0",
      // The step back equals the Desk grid's margin column plus its 2rem gap, so the label's ink is on the container edge.
      "margin-inline-start": "calc(-1 * (var(--margin-col) + 2rem))",
    });
    expect(decls('main[data-template="desk"] .desk-section:has(> .desk-action)', "min-width: 1024px")).toEqual({ "grid-template-areas": '"label . action" ". body body"' });
    const desk = RULES.find((r) => r.selector === 'main[data-template="desk"]' && inMedia(r, "min-width: 1024px"));
    expect(desk?.decls).toContainEqual(["column-gap", "2rem"]);
  });

  it("the page sheet is full-bleed below 640 with its text on the 16px gutter", () => {
    expect(decls(".sheet-page", "max-width: 639px")).toEqual({ "margin-inline": "-1rem", "padding-inline": "1rem" });
  });

  it("no card or panel boxes remain on the slice 4 screens; ledgers and notes instead; labels are .label, never uppercase", () => {
    for (const f of SLICE4) {
      const s = src(f);
      expect(s, f).not.toMatch(/className="[^"]*\b(card|card-accent|panel|panel-\w+|eyebrow)\b/);
      expect(s, f).not.toMatch(/\bborder border-hair\b|rounded-card/);
    }
    expect(src("components/CourseCard.tsx")).toContain('className="ledger-row-link"');
    expect(src("components/DashboardView.tsx")).toContain('<ul className="ledger">');
    expect(src("components/AllCoursesPage.tsx")).toContain('<ul className="ledger">');
    expect(src("components/syllabus/SyllabusCard.tsx")).toContain('className="ledger-row-link"');
    // Sheet and Auth pages: the h1 sits outside the one sheet, which is a .sheet (not a card).
    for (const f of ["components/CourseForm.tsx", "components/SignInView.tsx"]) expect(src(f)).toMatch(/<\/h1>[\s\S]*className="sheet sheet-page/);
  });
});

// ---------------------------------------------------------------------------------------------------------------------
// Slice 6 (redesign-round2 1.7, 2.7 Ledger / Note, 5.4; redesign-round2-review 4): the syllabus review as ledger rows, the
// group head as a label-style disclosure, the calendar step as a sticky Note, and no boxes left in the syllabus files
// except the Draft notice panel (G1) and the one-at-a-time card (unchanged by the brief).
describe("Syllabus review density (redesign-round2 slice 6)", () => {
  const find = (sel: string, ctx?: string) => RULES.find((r) => r.selector === sel && (ctx ? inMedia(r, ctx) : r.context.every((c) => c.startsWith("@layer"))));
  const decls = (sel: string, ctx?: string) => Object.fromEntries(find(sel, ctx)?.decls ?? []) as Record<string, string>;
  const src = (f: string) => readFileSync(path.join(SRC, f), "utf8");
  const SLICE6 = [
    "components/syllabus/Review.tsx",
    "components/syllabus/ItemRow.tsx",
    "components/syllabus/ItemEditForm.tsx",
    "components/syllabus/ItemCalendar.tsx",
    "components/syllabus/Batch.tsx",
    "components/syllabus/Sync.tsx",
    "components/syllabus/SyllabusList.tsx",
    "components/syllabus/ActionProblem.tsx",
    "components/syllabus/ConfirmDialog.tsx",
    "components/syllabus/Processing.tsx",
  ];

  it("an item row is a grid of named areas: stacked below 768 with the source and the quote before the actions; actions on the title line from 768", () => {
    expect(decls(".item-row")).toMatchObject({ display: "grid", "grid-template-areas": '"main" "meta" "source" "extra" "actions"', "align-items": "baseline", "row-gap": "0" });
    expect(decls(".item-row", "min-width: 768px")).toMatchObject({ "grid-template-columns": "minmax(0, 1fr) auto", "grid-template-areas": '"main actions" "meta source" "extra extra"' });
    expect(decls(".item-actions", "min-width: 768px")).toEqual({ "margin-block-start": "0", "justify-self": "end" });
    // The source toggle keeps its 44px hit area without growing its line (padding inside, equal negative margin outside).
    expect(decls(".item-source-toggle")).toEqual({ "padding-block": "0.5rem", "margin-block": "-0.5rem" });
  });

  it("the group head is a 44px full-width disclosure in the verbatim face at 15/24 (a week range is data, not synthesis)", () => {
    const g = decls(".group-head");
    expect(g["@apply"]).toContain("min-h-[44px]");
    expect(g["@apply"]).toContain("w-full");
    expect(g).toMatchObject({ "font-family": "var(--font-verbatim)", "font-size": "var(--type-sm)", "line-height": "var(--leading-sm)" });
  });

  // Rewritten in slice 8c (PO-18b; make-notes-rail-demo-notice 3.5): slice 6 pinned "no rule mentions .calendar-step"
  // because a sticky bar over the list covered row targets. The step now sits in a reserved rail track from 1280; it is
  // still never positioned itself, and the only positioned box is `.review-rail` (sticky) inside the 1280 media query, in
  // a grid track no row occupies. Below 1280 nothing positions either.
  it("the calendar step is never positioned itself; from 1280 only its reserved rail track (.review-rail) is sticky; below 1280 it is in the flow", () => {
    const stepRules = RULES.filter((r) => r.selector.includes("calendar-step"));
    expect(stepRules.map((r) => r.selector).sort()).toEqual([".review-rail .calendar-step", ".review-rail .calendar-step-action", ".review-rail .calendar-step::before"]);
    for (const r of stepRules) {
      expect([r.selector, inMedia(r, "min-width: 1280px")]).toEqual([r.selector, true]);
      expect(r.decls.map(([k]) => k)).not.toContain("position");
    }
    const positioned = RULES.filter((r) => /\.review-(rail|body|groups)/.test(r.selector) && r.decls.some(([k]) => k === "position" || k === "inset" || k === "top" || k === "bottom"));
    expect(positioned.map((r) => r.selector)).toEqual([".review-rail"]);
    expect(inMedia(positioned[0], "min-width: 1280px")).toBe(true);
    expect(Object.fromEntries(positioned[0].decls)).toEqual({
      position: "sticky",
      top: "5rem",
      "max-height": "calc(100vh - 6rem)",
      "overflow-y": "auto",
      background: "var(--ledger-bg)",
      "--ledger-inset": "1rem",
    });
    expect(Object.fromEntries(RULES.find((r) => r.selector === ".review-body" && inMedia(r, "min-width: 1280px"))?.decls ?? [])).toEqual({
      display: "grid",
      "grid-template-columns": "minmax(0, 1fr) var(--aside-w)",
      "column-gap": "2.5rem",
      "align-items": "start",
      "max-width": "none",
    });
    // Below 1280 the body is the 68ch block slice 6 had (the old outer max-w-prose), with no grid or position.
    expect(Object.fromEntries(RULES.find((r) => r.selector === ".review-body" && !r.context.some((c) => c.startsWith("@media")))?.decls ?? [])).toEqual({ "max-width": "68ch" });
    expect(LAYER_C.get("--aside-w")?.default).toBe("20rem");
    // DOM order: the groups, then the rail holding the step (Tab order, skip link and h2 target unchanged).
    const review = src("components/syllabus/Review.tsx");
    expect(review.indexOf('className="review-groups space-y-4"')).toBeGreaterThan(0);
    expect(review.indexOf('<div className="review-rail">')).toBeGreaterThan(review.indexOf('className="review-groups space-y-4"'));
    expect(review.indexOf('className="note note-neutral calendar-step mt-6"')).toBeGreaterThan(review.indexOf('<div className="review-rail">'));
  });

  it("no card, panel or bordered box remains in the slice 6 files; the review is ledgers, notes, one sheet (the edit form) and the v2 dialog", () => {
    for (const f of SLICE6) {
      const s = src(f);
      expect(s, f).not.toMatch(/className="[^"]*\b(card|card-accent|panel|panel-\w+|eyebrow)\b/);
      expect(s, f).not.toMatch(/\bborder border-hair\b|rounded-card/);
    }
    expect(src("components/syllabus/Review.tsx")).toContain('<ul className="ledger">');
    expect(src("components/syllabus/Review.tsx")).toContain('className="note note-neutral calendar-step mt-6"');
    expect(src("components/syllabus/ItemRow.tsx")).toContain('className="ledger-row item-row"');
    expect(src("components/syllabus/ItemEditForm.tsx")).toContain('className="sheet mt-1 space-y-4"');
    expect(src("components/syllabus/SyllabusList.tsx")).toContain('className="ledger-row-link min-h-[44px]"');
    expect(src("components/syllabus/ConfirmDialog.tsx")).toContain('className="dialog"');
    expect(src("components/syllabus/ConfirmDialog.tsx")).not.toMatch(/48px/);
  });

  it("G1: the Add syllabus form is on the Sheet template around an unchanged Draft notice panel, tag and checkbox; the Demo lines keep their tag", () => {
    const form = src("components/syllabus/CreateForm.tsx");
    expect(form).toMatch(/<\/h1>[\s\S]*className="sheet sheet-page mt-6 space-y-6"/);
    expect(form).toContain('<section aria-labelledby={ids.notice} className="panel panel-notice space-y-3">');
    expect(form).toContain('<span className="tag tag-warning">{NOTICE_DRAFT.tag}</span>');
    expect(form).toContain("{NOTICE_DRAFT.heading}");
    expect(form).toContain("<p>{NOTICE_DRAFT.body}</p>");
    expect(form).toContain("<span>{NOTICE_DRAFT.checkbox}</span>");
    expect(form.match(/className="panel/g)?.length).toBe(1); // the one notice section and nothing else
    expect(src("components/syllabus/DemoLine.tsx")).toContain('<span className="tag tag-warning">{tag}</span>');
    // One-at-a-time is unchanged by slice 6 except the "Please check" Approve weight (review 4).
    const one = src("components/syllabus/OneAtATime.tsx");
    expect(one).toContain('className={item.confidence === "please_check" ? "btn-secondary" : "btn-primary"}');
  });
});

describe("Type scale (redesign-round2 2.1): line boxes on the 4px grid", () => {
  const rootDecls = new Map<string, string>();
  for (const r of rootRules.filter((r) => r.context.length === 0)) for (const [k, v] of r.decls) rootDecls.set(k, v);
  const px = (rem: string) => parseFloat(rem) * 16;
  const find = (sel: string) => RULES.find((r) => r.selector === sel && r.context.every((c) => c.startsWith("@layer")));

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

  // Slice 1 (= themes T1) flipped Tailwind's fontSize from the shipped ratios (1.4 / 1.55 / 1.2) to the 4px-grid tokens, as
  // slice 0 deferred and the Lead confirmed (redesign-round2 "Slice 0 results", open item 2). Slice 0 pinned the old values
  // here so the flip would be a deliberate edit; this is that edit.
  it("Tailwind's fontSize equals the type-scale tokens (every line box on the 4px grid); 3xl is 38/44; body leading is the base token", () => {
    const sizes = tailwind.theme?.extend?.fontSize as Record<string, [string, string]>;
    for (const [k, v] of Object.entries(TYPE_SCALE).filter(([k]) => k !== "display")) expect([k, sizes[k]]).toEqual([k, [v.size, v.leading]]);
    expect(sizes["3xl"]).toEqual(["2.375rem", "2.75rem"]);
    for (const [k, [size, leading]] of Object.entries(sizes).filter(([k]) => k !== "display")) {
      expect([k, px(leading) % 4]).toEqual([k, 0]);
      expect(px(leading)).toBeGreaterThan(px(size));
    }
    expect(find("body")?.decls).toContainEqual(["line-height", "var(--leading-base)"]);
  });

  it("transcript lines are 17/28 with the timestamp on the text's baseline; the partial row reserves one 28px line", () => {
    const line = find(".transcript-line")?.decls.find(([k]) => k === "@apply")?.[1] ?? "";
    expect(line).toContain("leading-7");
    expect(line).toContain("items-baseline");
    expect(find(".transcript-time")?.decls.find(([k]) => k === "@apply")?.[1]).toContain("leading-7");
    expect(find(".transcript-partial")?.decls.find(([k]) => k === "@apply")?.[1]).toContain("min-h-7");
  });

  it("layout system (slice 1): scrollbar-gutter stable, 56px bar / 44px controls, templates, optical utilities", () => {
    expect(RULES.find((r) => r.selector === "html" && r.context.length === 0)?.decls).toContainEqual(["scrollbar-gutter", "stable"]);
    expect(find(".optical-start")?.decls).toEqual([["margin-inline-start", "calc(-1 * (var(--btn-px, 0.75rem) + var(--btn-border-w)))"]]);
    expect(find(".optical-end")?.decls).toEqual([["margin-inline-end", "calc(-1 * (var(--btn-px, 0.75rem) + var(--btn-border-w)))"]]);
    expect(find(".btn")?.decls).toContainEqual(["--btn-px", "1.25rem"]);
    for (const cls of [".btn-quiet", ".btn-sm"]) expect(find(cls)?.decls).toContainEqual(["--btn-px", "0.75rem"]);
    expect(find('.nav-tab[aria-current="page"]')?.decls).toContainEqual(["box-shadow", "inset 0 -3px 0 var(--c-ink)"]);
    // The accent stripe is painted, so a striped card's text inset stays --pad-card + 1px (A4 / R8).
    expect(find(".card-accent::before")?.decls).toContainEqual(["width", "var(--note-stripe-w)"]);
    expect(find(".card-accent")?.decls).not.toContainEqual(expect.arrayContaining(["padding"]));
    const desk = RULES.find((r) => r.selector === 'main[data-template="desk"]' && inMedia(r, "min-width: 1024px"));
    expect(desk?.decls).toContainEqual(["grid-template-columns", "var(--margin-col) minmax(0, 1fr)"]);
    expect(RULES.find((r) => r.selector === 'main[data-template="auth"]')?.decls).toEqual([["padding-top", "6rem"]]);
    expect(tailwind.theme?.extend?.maxWidth).toMatchObject({ content: "72rem", lecture: "80rem", sheet: "40rem", narrow: "28rem", focus: "52rem" });
    expect(tailwind.theme?.extend?.minHeight).toEqual({ target: "44px" }); // "primary 48" is gone (A8)
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

describe("Preferences side panel, Theme picker, Portal and Calm (redesign-round2 slice 5)", () => {
  const at = (sel: string, q: string) => RULES.find((r) => r.selector === sel && inMedia(r, q));
  it("from 1024 Preferences is a fixed 24rem right-hand panel; the page reflows beside it except on lecture routes; below 1024 it is an in-flow sheet", () => {
    expect(at(".prefs-panel", "min-width: 1024px")?.decls).toEqual(
      expect.arrayContaining([["position", "fixed"], ["inset-inline-end", "0"], ["width", "24rem"], ["overflow-y", "auto"]]),
    );
    expect(at("html:has(.prefs-panel):not(:has([data-lecture-root])) body", "min-width: 1024px")?.decls).toEqual([["padding-inline-end", "24rem"]]);
    expect(at(".prefs-head", "min-width: 1024px")?.decls).toContainEqual(["position", "sticky"]);
    const flow = RULES.find((r) => r.selector === ".prefs-panel" && r.context.every((c) => c.startsWith("@layer")));
    expect(flow?.decls).toEqual(expect.arrayContaining([["background", "var(--sheet-bg)"], ["border-radius", "var(--sheet-radius)"], ["max-width", "40rem"]]));
    // The site Preferences wrapper is still hidden on lecture routes.
    expect(RULES.some((r) => r.selector.includes("body:has([data-lecture-root]) [data-site-prefs]") && r.decls.some(([k, v]) => k === "display" && v === "none"))).toBe(true);
  });

  it("theme cards: >= 64px, chosen = accent edge plus a filled mark; swatches paint from the theme's own palette roles (no colour values)", () => {
    const card = RULES.find((r) => r.selector === ".theme-card")!;
    expect(card.decls).toContainEqual(["min-height", "4rem"]);
    expect(RULES.find((r) => r.selector === '.theme-card[aria-checked="true"] .theme-mark')?.decls).toContainEqual(["background", "var(--c-accent)"]);
    for (const sel of [".theme-swatch", ".theme-swatch-bar", ".theme-swatch-sheet", ".theme-swatch-accent"])
      for (const [k, v] of RULES.find((r) => r.selector === sel)!.decls) if (/color|background|border(?!-radius)/.test(k)) expect([sel, k, v]).toEqual([sel, k, expect.stringMatching(/var\(--c-[a-z-]+\)/)]);
  });

  it("Portal / Calm finishing is paint only, on .sheet and .ledger (never a lecture sheet), stripped by Low-stimulation and forced colours", () => {
    const slice5 = skinRules.filter((r) => /\.(sheet|ledger)/.test(r.selector) && !inMedia(r, "forced-colors"));
    expect(slice5.map((r) => r.selector)).toEqual(
      expect.arrayContaining(['[data-skin="portal"] .sheet', '[data-skin="portal"] .ledger', '[data-skin="calm"] .ledger']),
    );
    for (const r of slice5) {
      expect(r.selector).not.toMatch(/lecture|transcript/);
      for (const [k] of r.decls) expect(["box-shadow", "border-radius", "border-start-start-radius", "border-start-end-radius", "border-end-start-radius", "border-end-end-radius"]).toContain(k);
    }
    expect(RULES.find((r) => r.selector === ':root[data-quiet="true"][data-skin] .sheet')?.decls).toEqual([["box-shadow", "none"]]);
    expect(RULES.find((r) => r.selector === "[data-skin][data-theme] .sheet, [data-skin][data-theme] .ledger" && inMedia(r, "forced-colors: active"))?.decls).toEqual([["box-shadow", "none"]]);
  });

  it("G1: no skin decoration reaches the consent card, and the carve-out leaves Paper (the default) untouched", () => {
    const carve = RULES.filter((r) => r.selector.includes("#consent-h1") && r.selector.includes("[data-skin"));
    expect(carve.length).toBe(2);
    for (const r of carve) expect(r.selector).toContain(':not([data-skin="paper"])');
    expect(carve[0].decls).toEqual([["box-shadow", "var(--shadow-1)"], ["background-image", "none"]]);
  });
});

describe("Gamer and Glass (redesign-round2 slice 7 = themes T3 + T4)", () => {
  const filterDecls = (r: Rule) => r.decls.filter(([k]) => k === "backdrop-filter" || k === "-webkit-backdrop-filter");
  it("backdrop-filter is only ever switched on for the site header, and only by the Glass rule that excludes Solid and Low-stimulation", () => {
    const on = RULES.filter((r) => filterDecls(r).some(([, v]) => !/^none( !important)?$/.test(v)));
    expect(on.map((r) => r.selector)).toEqual(['[data-skin="glass"]:not([data-solid="true"]):not([data-quiet="true"]) [data-chrome]']);
    for (const r of on) {
      expect(r.context.some((c) => c.startsWith("@supports (backdrop-filter"))).toBe(true);
      for (const [, v] of filterDecls(r)) expect(v).toBe("blur(var(--blur)) saturate(1.4)");
    }
    // At most 20px (themes-spec 6 budget), and Low-stimulation zeroes it.
    expect(bareSkinBlocks.find((r) => r.selector === '[data-skin="glass"]')?.decls).toContainEqual(["--blur", "20px"]);
    expect(RULES.find((r) => r.selector.startsWith(':root[data-quiet="true"]') && r.decls.some(([k]) => k === "--blur"))?.decls).toContainEqual(["--blur", "0px"]);
  });

  it("every fallback clears the filter with !important (it must beat the 0,4,0 Glass rule): phone width, reduced transparency, more contrast, forced colours, the lecture route", () => {
    const media = RULES.find((r) => r.selector === "[data-chrome]" && r.context.some((c) => /prefers-reduced-transparency: reduce.*prefers-contrast: more.*forced-colors: active.*max-width: 1023px/.test(c)));
    expect(media?.decls).toEqual([["background", "var(--c-chrome)"], ["-webkit-backdrop-filter", "none !important"], ["backdrop-filter", "none !important"]]);
    const route = RULES.find((r) => r.selector === 'html:has(main[data-route="lecture"]) [data-chrome]');
    expect(route?.decls).toEqual([["background", "var(--c-chrome)"], ["-webkit-backdrop-filter", "none !important"], ["backdrop-filter", "none !important"]]);
    // Solid surfaces, Low-stimulation and the live lecture: opaque chrome (the Glass rule's :not()s already keep its filter off).
    expect(RULES.find((r) => r.selector.includes('[data-solid="true"] [data-chrome]'))?.decls).toContainEqual(["background", "var(--c-chrome)"]);
    // No @supports: the opaque chrome.
    expect(RULES.find((r) => r.selector === "[data-chrome]" && r.context.some((c) => c.startsWith("@supports not")))?.decls).toEqual([["background", "var(--c-chrome)"]]);
  });

  it("page decoration (Gamer grid, Glass gradient) lives only in --page-bg on body::before; texture is off unless data-texture=true; never on the lecture route; cleared under forced colours", () => {
    const consumers = RULES.filter((r) => r.decls.some(([, v]) => v.includes("var(--page-bg)")));
    expect(consumers.map((r) => r.selector)).toEqual(["body::before"]);
    const setters = RULES.filter((r) => r.decls.some(([k, v]) => k === "--page-bg" && v !== "none" && !v.startsWith("none"))).map((r) => r.selector);
    expect(setters).toEqual(['[data-skin="glass"]', '[data-skin="glass"][data-theme="dark"]', '[data-skin="gamer"][data-texture="true"]']);
    // The bare Gamer block sets no page background: no grid by default.
    expect(bareSkinBlocks.filter((r) => r.selector === '[data-skin="gamer"]').flatMap((r) => r.decls).some(([k]) => k === "--page-bg")).toBe(false);
    expect(RULES.find((r) => r.selector === 'html:has(main[data-route="lecture"]) body::before')?.decls).toEqual([["background", "none"]]);
    expect(RULES.find((r) => r.selector === "body::before" && inMedia(r, "forced-colors: active"))?.decls).toEqual([["background", "none"]]);
    // body::before is fixed, behind everything, inert.
    expect(RULES.find((r) => r.selector === "body::before" && r.context.length === 0)?.decls).toEqual(
      expect.arrayContaining([["z-index", "-1"], ["pointer-events", "none"], ["position", "fixed"]]),
    );
  });

  it("no glow in Gamer (or anywhere): no text-shadow, no filter, no blurred box-shadow in a Gamer rule, Gamer shadow tokens none, nothing animated", () => {
    expect(structureCss).not.toMatch(/text-shadow\s*:/);
    expect(structureCss).not.toMatch(/(^|[^-])filter\s*:\s*(?!none)/m);
    for (const r of skinRules.filter((r) => r.selector.includes('[data-skin="gamer"]')))
      for (const [k, v] of r.decls) {
        expect([r.selector, k]).not.toEqual([r.selector, "text-shadow"]);
        if (k === "box-shadow") expect([r.selector, v]).toEqual([r.selector, expect.not.stringMatching(/\d+px \d+px [1-9]/)]);
        expect(k).not.toMatch(/^(animation|transition|filter)/);
      }
    const gamer = bareSkinBlocks.find((r) => r.selector === '[data-skin="gamer"]')!;
    for (const s of ["--shadow-1", "--shadow-2", "--shadow-3"]) expect(gamer.decls).toContainEqual([s, "none"]);
  });

  it("the synthesis floor: no skin sets the size or line height of .synthesis or a transcript line (review 5.3)", () => {
    for (const r of skinRules.filter((r) => /\.synthesis|\.transcript/.test(r.selector)))
      for (const [k] of r.decls) expect([r.selector, k]).toEqual([r.selector, expect.not.stringMatching(/^(font-size|line-height|font)$/)]);
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

  // Accent is spent on the one primary fill, the focus ring and citation/time links (redesign-round2 2.2). Slice 2 retired
  // it from quiet buttons, links, disclosure summaries and step numerals; slice 3 from the Focus toggle's pressed state
  // and the recap citations (`.cite` draws accent from the token, baseline tightened 2026-10-31). What remains:
  // globals.css (.btn-quiet and the consent link carve-out, both G1; .tag-accent), layout.tsx (theme-color), the consent
  // page's "What does this mean" summary (G1) and the lecture skip links' focus style (focus is an allowed use). Slice 8b
  // (PO-18c): the Demo-source notice's accent "Details" summary became an ink tertiary button, so Notices.tsx left the
  // baseline (1 -> 0).
  it("accent-text usage ratchet: no new text-accent outside today's files", () => {
    ratchet(countBy(/\btext-accent\b/g), {
      "app/globals.css": 3,
      "app/layout.tsx": 1,
      "components/lecture/ConsentStart.tsx": 1,
      "components/lecture/LectureScreen.tsx": 1,
    });
  });

  // Slice 2 (redesign-round2 2.7): the uppercase eyebrow is replaced by .label everywhere except the consent page (G1),
  // and the accent quiet button by .btn-tertiary everywhere except the consent surfaces (G1). The ui-kit shows the legacy
  // classes once so they stay under axe and contrast in every look.
  it("legacy classes are used only by the consent surfaces (G1) and shown once on the ui-kit: .eyebrow, .btn-quiet; no other uppercase", () => {
    const uses = (re: RegExp) => Object.keys(countBy(re)).filter((f) => f !== "app/globals.css");
    expect(uses(/\beyebrow\b/g).filter((f) => !/SignInView\.tsx$/.test(f))).toEqual(["components/lecture/ConsentStart.tsx", "components/ui/UiKit.tsx"]); // SignInView: a comment
    expect(uses(/\bbtn-quiet\b/g)).toEqual(["components/lecture/ConsentDialog.tsx", "components/lecture/ConsentStart.tsx", "components/ui/UiKit.tsx"]);
    const upper = [...structureCss.matchAll(/[^{}]*\{[^}]*\buppercase\b[^}]*\}/g)].map((m) => m[0].trim().split("{")[0].trim());
    expect(upper).toEqual([".eyebrow"]);
    expect(Object.keys(countBy(/\buppercase\b/g)).filter((f) => f.endsWith(".tsx"))).toEqual(["components/ui/UiKit.tsx"]); // prose on the kit page, not a class
  });

  it("the form field label is .field-label; .label is the section label (never on a <label for=...>)", () => {
    expect(countBy(/<label[^>]*className="label\b/g)).toEqual({});
    expect(Object.keys(countBy(/\bfield-label\b/g)).length).toBeGreaterThanOrEqual(8);
  });

  it("no transforms anywhere (redesign-round2 2.6): no rotate-/translate-/scale-/transition-transform utility and no transform property", () => {
    expect(countBy(/\b(rotate|translate|scale)-[\w[\]./-]+|transition-transform/g)).toEqual({});
    expect(structureCss).not.toMatch(/(^|[^-])transform\s*:/);
  });

  // themes-spec R1 / 5.3: half-step spacing classes are off the 4px scale. Slice 1 removed them (A13) except: the .tag
  // padding (the consent page's Draft tag, gate G1) and the consent pop-up's checkbox nudge (G1). Slice 3 moved the Demo
  // summary tag onto the Stamp primitive. Baselines tightened 2026-10-31.
  it("half-step spacing ratchet: no new p-/m-/gap-/space- *.5 classes", () => {
    ratchet(countBy(/\b(?:p|m|gap|space-[xy]|inset|top|left|right|bottom)[xytblr]?-\d+\.5\b/g), {
      "app/globals.css": 2,
      "components/lecture/ConsentDialog.tsx": 1,
      // The Demo summary tag keeps its pre-redesign classes (px-2.5): Demo-source labels are not restyled (PO-17b, gate G1).
      "components/lecture/QuickRecapPanel.tsx": 1,
    });
  });

  // Arbitrary lengths outside the allowlist (control heights 44/56/64, the 3.25rem timestamp column, the 0.2em underline
  // offset). 48px left the allowlist in slice 1 (one control height, 44): the two files that still carry it are the
  // consent surfaces (gate G1), pinned here so no new 48px control appears; the syllabus confirm dialog's 48px buttons
  // became 44 in slice 6 (baseline 2 -> 0).
  // Slice 3: the recap's em nudges and the rail's sticky offsets moved into globals.css (baseline-aligned grids); the
  // phone bottom sheet keeps its one 60dvh bound.
  it("arbitrary-length ratchet: no new -[Npx]/-[Nrem]/... values outside the allowlist", () => {
    const ALLOW = /-\[(44px|56px|64px|3\.25rem_|0\.2em)\]?/;
    ratchet(countBy(/-\[[0-9.]+(?:px|rem|em|vh|dvh|vw|ch)(?:\]|_)/g, (m) => !ALLOW.test(m)), {
      "app/globals.css": 1,
      "components/CommandPalette.tsx": 2,
      "components/PreferencesPanel.tsx": 1,
      "components/lecture/ConsentDialog.tsx": 2,
      "components/lecture/ConsentStart.tsx": 3,
      "components/lecture/Notices.tsx": 1,
      "components/lecture/QuickRecapPanel.tsx": 1,
      "components/ui/Segmented.tsx": 1,
      "components/ui/Switch.tsx": 3,
    });
  });

  it("no Tailwind animate-* utilities in components (nothing loops, pulses or shimmers)", () => {
    const bad = files.filter((f) => f.endsWith(".tsx")).flatMap((f) => [...readFileSync(f, "utf8").matchAll(/\banimate-[a-z]+\b/g)].map((m) => `${rel(f)}: ${m[0]}`));
    expect(bad).toEqual([]);
  });
});
