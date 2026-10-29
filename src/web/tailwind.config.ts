import type { Config } from "tailwindcss";

// Design tokens: docs/ux/design-direction.md section 2.7 and docs/ux/themes-spec.md section 2.6. Colours are CSS variables without <alpha-value>, so
// opacity modifiers (text-ink/60) cannot create unlisted contrast pairs. Values live in src/app/globals.css and are
// checked against src/lib/design/tokens.ts by tokens.test.ts.
const v = (n: string) => `var(--c-${n})`;

const config: Config = {
  content: ["./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        canvas: v("canvas"),
        surface: v("surface"),
        raised: v("raised"),
        sunken: v("sunken"),
        ink: v("ink"),
        muted: v("muted"),
        accent: v("accent"),
        "on-accent": v("on-accent"),
        "accent-subtle": v("accent-subtle"),
        recording: v("recording"),
        "recording-subtle": v("recording-subtle"),
        success: v("success"),
        "success-subtle": v("success-subtle"),
        warning: v("warning"),
        "warning-subtle": v("warning-subtle"),
        danger: v("danger"),
        line: v("border-strong"),
        hair: v("border-hair"),
        focus: v("focus"),
        // themes-spec 2.6: site header and the decorative grid.
        chrome: v("chrome"),
        "on-chrome": v("on-chrome"),
        grid: v("grid"),
      },
      fontFamily: {
        sans: ["var(--font-body)", "system-ui", "sans-serif"],
        // The heading face is chosen per skin in globals.css (--font-head); Paper's is Newsreader.
        serif: "var(--font-head)",
        head: "var(--font-head)",
      },
      fontSize: {
        xs: ["0.8125rem", "1.4"],
        sm: ["0.9375rem", "1.4"],
        base: ["1.0625rem", "1.55"],
        lg: ["1.25rem", "1.4"],
        xl: ["1.5rem", "1.2"],
        "2xl": ["1.875rem", "1.2"],
        "3xl": ["2.375rem", "1.2"],
      },
      // Per-skin radii (themes-spec 2.5); Paper keeps 8 / 12 / 16.
      borderRadius: { control: "var(--r-control)", card: "var(--r-card)", sheet: "var(--r-sheet)" },
      boxShadow: { 1: "var(--shadow-1)", 2: "var(--shadow-2)", 3: "var(--shadow-3)" },
      transitionDuration: { fast: "var(--dur-fast)", base: "var(--dur-base)" },
      transitionTimingFunction: { calm: "cubic-bezier(.2,0,0,1)" },
      maxWidth: { prose: "68ch", content: "72rem" },
      minHeight: { target: "44px", primary: "48px" },
      minWidth: { target: "44px", primary: "48px" },
      spacing: { block: "var(--gap-block)", card: "var(--pad-card)" },
    },
  },
  plugins: [],
};
export default config;
