import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { readFileSync } from "node:fs";
import path from "node:path";
import { axeViolations } from "@/test/axe";
import { collectStrings, FORBIDDEN, sentencesOver } from "@/lib/copyLint";
import { PREFS_KEY } from "@/lib/design/prefs";

// Redesign-round2 slice 7 (= themes T3 + T4): Gamer's "Grid texture" and Glass's "Solid surfaces" switches in
// Preferences > Look. Shown only while their theme is chosen, directly under the theme cards; persisted in
// tether.prefs.v1 (`texture`, `solid`); announced once through the panel's existing status line (no new live region);
// strings from the linted registry. The CSS they drive is pinned in skinBlocks.test.ts and checked in a browser by
// e2e-screens/gamer-glass.spec.ts.

const auth = vi.hoisted(() => ({
  status: "authenticated" as const,
  user: { id: "u1", email: "alice@dev.local", displayName: "Alice" },
  signOut: vi.fn(),
}));
vi.mock("next/navigation", () => ({ usePathname: () => "/", useRouter: () => ({ replace: vi.fn(), push: vi.fn() }) }));
vi.mock("./AuthProvider", () => ({ useAuth: () => ({ ...auth, refresh: vi.fn() }) }));

import { AppShell, isLectureRoute } from "./AppShell";
import { LookControls } from "./PreferencesPanel";
import { PrefsProvider } from "./PrefsProvider";
import { PREFS_COPY, THEMES } from "./prefsCopy";

const ATTRS = ["data-skin", "data-theme", "data-quiet", "data-text-size", "data-spacing", "data-density", "data-reduce-motion", "data-texture", "data-solid", "data-themes"];
afterEach(() => {
  window.localStorage.clear();
  for (const a of ATTRS) document.documentElement.removeAttribute(a);
});

const html = () => document.documentElement;
const stored = () => JSON.parse(window.localStorage.getItem(PREFS_KEY) ?? "{}") as Record<string, unknown>;
async function openPrefs() {
  const user = userEvent.setup();
  const view = render(
    <PrefsProvider>
      <AppShell>
        <h1>Page</h1>
      </AppShell>
    </PrefsProvider>,
  );
  await user.click(screen.getByRole("button", { name: "Preferences" }));
  return { user, view, panel: screen.getByRole("region", { name: "Preferences" }) };
}
const grid = (root: HTMLElement) => within(root).queryByRole("switch", { name: PREFS_COPY.gridLabel });
const solid = (root: HTMLElement) => within(root).queryByRole("switch", { name: PREFS_COPY.solidLabel });

describe("theme switches (slice 7): shown only for their theme, under the cards", () => {
  it("Paper, Course portal and Calm show neither switch; Gamer shows Grid texture only, Glass shows Solid surfaces only", async () => {
    const { user, panel } = await openPrefs();
    for (const name of ["Paper", "Course portal", "Calm"]) {
      await user.click(within(panel).getByRole("radio", { name }));
      expect([name, grid(panel), solid(panel)]).toEqual([name, null, null]);
    }
    await user.click(within(panel).getByRole("radio", { name: "Gamer" }));
    expect(grid(panel)).not.toBeNull();
    expect(solid(panel)).toBeNull();
    await user.click(within(panel).getByRole("radio", { name: "Glass" }));
    expect(grid(panel)).toBeNull();
    expect(solid(panel)).not.toBeNull();
  });

  it("the switch is the next control after the theme cards (before Colour mode), Off by default, described by its hint", async () => {
    const { user, panel } = await openPrefs();
    await user.click(within(panel).getByRole("radio", { name: "Gamer" }));
    const sw = grid(panel)!;
    expect(sw).toHaveAttribute("aria-checked", "false");
    expect(sw).toHaveTextContent("Off");
    expect(sw).toHaveAccessibleDescription(PREFS_COPY.gridHint);
    // Tab order: the checked card, then the switch, then Colour mode.
    within(panel).getByRole("radio", { name: "Gamer" }).focus();
    await user.tab();
    expect(sw).toHaveFocus();
    await user.tab();
    expect(document.activeElement?.closest('[role="radiogroup"]')).toBe(within(panel).getByRole("radiogroup", { name: "Colour mode" }));
    // DOM order: theme group < switch < Colour mode group.
    const order = (a: Element, b: Element) => a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING;
    expect(order(within(panel).getByRole("radiogroup", { name: "Theme" }), sw)).toBeTruthy();
    expect(order(sw, within(panel).getByRole("radiogroup", { name: "Colour mode" }))).toBeTruthy();
  });

  it("Grid texture: applies at once, persists, announces once in the one status line; Off again says so", async () => {
    const { user, view, panel } = await openPrefs();
    await user.click(within(panel).getByRole("radio", { name: "Gamer" }));
    await user.click(grid(panel)!);
    expect(html()).toHaveAttribute("data-texture", "true");
    expect(stored()).toMatchObject({ skin: "gamer", texture: true });
    expect(within(panel).getByRole("status")).toHaveTextContent(PREFS_COPY.gridMsg.on);
    expect(grid(panel)).toHaveAttribute("aria-checked", "true");
    expect(grid(panel)).toHaveTextContent("On");
    // Still exactly one live region in the shell with the switch shown.
    expect(view.container.querySelectorAll('[aria-live],[role="status"],[role="alert"],[role="log"]')).toHaveLength(1);
    await user.click(grid(panel)!);
    expect(html()).toHaveAttribute("data-texture", "false");
    expect(stored()).toMatchObject({ texture: false });
    expect(within(panel).getByRole("status")).toHaveTextContent(PREFS_COPY.gridMsg.off);
  });

  it("Solid surfaces: applies at once, persists, announces once; the stored value survives choosing another theme", async () => {
    const { user, panel } = await openPrefs();
    await user.click(within(panel).getByRole("radio", { name: "Glass" }));
    await user.click(solid(panel)!);
    expect(html()).toHaveAttribute("data-solid", "true");
    expect(stored()).toMatchObject({ skin: "glass", solid: true });
    expect(within(panel).getByRole("status")).toHaveTextContent(PREFS_COPY.solidMsg.on);
    // Another theme hides the switch but keeps the choice (it has no effect outside Glass); coming back shows it On.
    await user.click(within(panel).getByRole("radio", { name: "Paper" }));
    expect(solid(panel)).toBeNull();
    expect(html()).toHaveAttribute("data-solid", "true");
    await user.click(within(panel).getByRole("radio", { name: "Glass" }));
    expect(solid(panel)).toHaveAttribute("aria-checked", "true");
  });

  it("restores a stored switch on load; Reset look turns both off", async () => {
    window.localStorage.setItem(PREFS_KEY, JSON.stringify({ skin: "gamer", theme: "dark", texture: true, solid: true }));
    const { user, panel } = await openPrefs();
    expect(html()).toHaveAttribute("data-texture", "true");
    expect(grid(panel)).toHaveAttribute("aria-checked", "true");
    await user.click(within(panel).getByRole("button", { name: PREFS_COPY.reset }));
    expect(stored()).toMatchObject({ skin: "paper", texture: false, solid: false });
    expect(html()).toHaveAttribute("data-texture", "false");
    expect(html()).toHaveAttribute("data-solid", "false");
    expect(grid(panel)).toBeNull();
  });

  it("is axe-clean with each switch shown", async () => {
    const { user, view, panel } = await openPrefs();
    await user.click(within(panel).getByRole("radio", { name: "Gamer" }));
    expect(await axeViolations(view.container)).toEqual([]);
    await user.click(within(panel).getByRole("radio", { name: "Glass" }));
    await user.click(solid(panel)!);
    expect(await axeViolations(view.container)).toEqual([]);
  });

  it("never in the lecture's settings (compact), and not rendered when themes are rolled back", async () => {
    window.localStorage.setItem(PREFS_KEY, JSON.stringify({ skin: "gamer", texture: true }));
    const v = render(
      <PrefsProvider>
        <LookControls compact />
      </PrefsProvider>,
    );
    expect(grid(v.container)).toBeNull();
    v.unmount();
    window.localStorage.setItem(PREFS_KEY, JSON.stringify({ skin: "glass", solid: true }));
    html().setAttribute("data-themes", "off");
    const { panel } = await openPrefs();
    expect(solid(panel)).toBeNull();
    expect(grid(panel)).toBeNull();
  });
});

describe("the lecture route marker (slice 7: no page decoration or frosted bar anywhere on it)", () => {
  it.each([
    ["/courses/abc/lecture", true],
    ["/lectures/s1", true],
    ["/dev/lecture-harness/consent", true],
    ["/", false],
    ["/courses", false],
    ["/courses/abc", false],
    ["/courses/abc/syllabus", false],
    ["/courses/new", false],
    ["/dev/ui-kit", false],
    ["/signin", false],
  ] as const)("%s -> %s", (p, want) => {
    expect(isLectureRoute(p)).toBe(want);
  });
  it("AppShell puts it on <main> only there", () => {
    const v = render(
      <PrefsProvider>
        <AppShell>
          <h1>Page</h1>
        </AppShell>
      </PrefsProvider>,
    );
    expect(v.container.querySelector("main")).not.toHaveAttribute("data-route"); // usePathname is mocked to "/"
  });
});

describe("copy (language guide; slice 7 strings)", () => {
  const spec = readFileSync(path.join(__dirname, "../../../../docs/ux/themes-spec.md"), "utf8");
  const strings = [...collectStrings(THEMES), ...collectStrings(PREFS_COPY)];
  it("the switch strings are the spec's (Grid hint as replaced by review 3.2), in the linted registry, and pass every rule", () => {
    expect(PREFS_COPY.gridHint).toBe("Adds a faint grid under the page. Never under lecture text.");
    expect(PREFS_COPY.solidHint).toBe("Replaces the frosted bar with a solid one. Your device setting is respected too.");
    for (const s of [PREFS_COPY.gridHint, PREFS_COPY.solidHint]) expect(spec).toContain(`"${s}"`);
    for (const s of [PREFS_COPY.gridLabel, PREFS_COPY.gridHint, PREFS_COPY.gridMsg.on, PREFS_COPY.gridMsg.off, PREFS_COPY.solidLabel, PREFS_COPY.solidHint, PREFS_COPY.solidMsg.on, PREFS_COPY.solidMsg.off, PREFS_COPY.reduceMotionHint])
      expect(strings).toContain(s);
    for (const [name, re] of FORBIDDEN) expect([name, strings.filter((s) => re.test(s))]).toEqual([name, []]);
    expect(sentencesOver(strings, 20)).toEqual([]);
    // The status lines follow the existing pattern ("Calm theme on.", "Reduce motion on.").
    expect(PREFS_COPY.gridMsg).toEqual({ on: "Grid texture on.", off: "Grid texture off." });
    expect(PREFS_COPY.solidMsg).toEqual({ on: "Solid surfaces on.", off: "Solid surfaces off." });
  });
  it("the lint would have caught the spec's old Grid hint ('behind' is guilt or pressure framing)", () => {
    const old = "Adds a faint grid behind the page. Never behind lecture text.";
    expect(FORBIDDEN.filter(([, re]) => re.test(old)).map(([n]) => n)).toEqual(["guilt or pressure"]);
  });
  it("no game mechanics anywhere in the theme copy, Gamer included (PO-17: themes are visual only)", () => {
    expect(strings.join(" ")).not.toMatch(/\b(streaks?|points?|levels?|xp|badges?|ranks?|leaderboards?|scores?|achievements?|unlock\w*|rewards?)\b/i);
    expect(THEMES.find((t) => t.id === "gamer")?.description).toBe("Sharp corners, monospace headings, an optional faint grid. No game mechanics.");
  });
  it("Preferences passes every switch label and hint by reference: no label=\"...\" or hint=\"...\" literal (review 5.3)", () => {
    for (const f of ["PreferencesPanel.tsx", "ThemePicker.tsx"]) {
      const src = readFileSync(path.join(__dirname, f), "utf8");
      expect([f, src.match(/\b(label|hint)="[^"]*"/g)]).toEqual([f, null]);
    }
  });
});
