import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { readFileSync } from "node:fs";
import path from "node:path";
import { axeViolations } from "@/test/axe";
import { collectStrings, FORBIDDEN, sentencesOver } from "@/lib/copyLint";
import { PREFS_KEY } from "@/lib/design/prefs";

// Redesign-round2 slice 5 (= themes T2): the Theme picker, Colour mode, Reset look, the Preview card and the status
// messages in Preferences; rollback flag; copy lint over the new strings. themes-spec 3.2 / 3.3 / 6.

const auth = vi.hoisted(() => ({
  status: "authenticated" as const,
  user: { id: "u1", email: "alice@dev.local", displayName: "Alice" },
  signOut: vi.fn(),
}));
vi.mock("next/navigation", () => ({ usePathname: () => "/", useRouter: () => ({ replace: vi.fn(), push: vi.fn() }) }));
vi.mock("./AuthProvider", () => ({ useAuth: () => ({ ...auth, refresh: vi.fn() }) }));

import { AppShell } from "./AppShell";
import { LookControls } from "./PreferencesPanel";
import { PrefsProvider } from "./PrefsProvider";
import { PREFS_COPY, THEMES } from "./prefsCopy";

const ATTRS = ["data-skin", "data-theme", "data-quiet", "data-text-size", "data-spacing", "data-density", "data-reduce-motion", "data-texture", "data-solid", "data-themes"];
afterEach(() => {
  window.localStorage.clear();
  for (const a of ATTRS) document.documentElement.removeAttribute(a);
});

const mount = () =>
  render(
    <PrefsProvider>
      <AppShell>
        <h1>Page</h1>
      </AppShell>
    </PrefsProvider>,
  );

async function openPrefs() {
  const user = userEvent.setup();
  const view = mount();
  const opener = screen.getByRole("button", { name: "Preferences" });
  await user.click(opener);
  const panel = screen.getByRole("region", { name: "Preferences" });
  return { user, view, opener, panel };
}
const html = () => document.documentElement;
const stored = () => JSON.parse(window.localStorage.getItem(PREFS_KEY) ?? "{}") as Record<string, unknown>;

describe("Theme picker (themes-spec 3.2)", () => {
  it("is a radiogroup named Theme with five cards in the spec's order, Paper chosen, one Tab stop, names and descriptions from the spec", async () => {
    const { panel } = await openPrefs();
    const group = within(panel).getByRole("radiogroup", { name: "Theme" });
    const cards = within(group).getAllByRole("radio");
    expect(cards.map((c) => c.getAttribute("data-theme-card"))).toEqual(["paper", "portal", "gamer", "calm", "glass"]);
    expect(cards.map((c) => c.textContent)).toEqual(THEMES.map((t) => `${t.name}${t.description}`));
    expect(within(group).getByRole("radio", { name: "Paper" })).toHaveAttribute("aria-checked", "true");
    // Pinned literally (redesign-round2-review 3.1 replaced all five descriptions: a theme is paint only, no layout claim).
    expect(within(group).getByRole("radio", { name: "Course portal" })).toHaveAccessibleDescription(
      "The look of a course website: dark top bar, light grey page, small corners.",
    );
    // The one sentence above the cards describes the whole group (review 3.1 themeHint).
    expect(group).toHaveAccessibleDescription("A theme changes colours, corners and heading type. Layout, words and controls stay the same.");
    expect(cards.filter((c) => c.tabIndex === 0)).toHaveLength(1);
    expect(cards.filter((c) => c.tabIndex === 0)[0]).toHaveAttribute("data-theme-card", "paper");
    // Theme comes first in Look, then Colour mode (separate control).
    const groups = within(panel).getAllByRole("radiogroup").map((g) => g.getAttribute("aria-labelledby") && document.getElementById(g.getAttribute("aria-labelledby")!)?.textContent);
    expect(groups.slice(0, 2)).toEqual(["Theme", "Colour mode"]);
  });

  it("selecting applies at once, persists, and announces once; arrows move and select with focus; Colour mode stays independent", async () => {
    const { user, panel } = await openPrefs();
    await user.click(within(panel).getByRole("radio", { name: "Calm" }));
    expect(html().getAttribute("data-skin")).toBe("calm");
    expect(stored()).toMatchObject({ skin: "calm", theme: "system" });
    expect(within(panel).getByRole("status")).toHaveTextContent("Calm theme on.");

    await user.keyboard("{ArrowDown}");
    const glass = within(panel).getByRole("radio", { name: "Glass" });
    expect(glass).toHaveFocus();
    expect(glass).toHaveAttribute("aria-checked", "true");
    expect(html().getAttribute("data-skin")).toBe("glass");
    await user.keyboard("{ArrowRight}"); // wraps to the first card
    expect(within(panel).getByRole("radio", { name: "Paper" })).toHaveFocus();
    await user.keyboard("{ArrowLeft}{ArrowUp}"); // Paper -> Glass (wraps) -> Calm
    expect(within(panel).getByRole("radio", { name: "Calm" })).toHaveFocus();
    expect(html().getAttribute("data-skin")).toBe("calm");

    // Colour mode is its own control; changing it keeps the theme.
    await user.click(within(panel).getByRole("radio", { name: "Dark" }));
    expect(html().getAttribute("data-skin")).toBe("calm");
    expect(html().getAttribute("data-theme")).toBe("dark");
    expect(within(panel).getByRole("status")).toHaveTextContent("Dark mode on.");
    // The swatches follow the current colour mode through nested data-skin / data-theme.
    for (const t of THEMES) {
      const sw = panel.querySelector(`[data-theme-card="${t.id}"] [data-skin]`);
      expect(sw).toHaveAttribute("data-skin", t.id);
      expect(sw).toHaveAttribute("data-theme", "dark");
      expect(sw).toHaveAttribute("aria-hidden", "true");
    }
  });

  it("Tab reaches the chosen card as the group's one stop; Escape from a card closes Preferences and returns focus to its opener", async () => {
    const { user, panel, opener } = await openPrefs();
    await user.click(within(panel).getByRole("radio", { name: "Gamer" }));
    within(panel).getByRole("heading", { name: "Preferences" }).focus();
    await user.tab(); // Close
    await user.tab(); // the checked theme card
    expect(within(panel).getByRole("radio", { name: "Gamer" })).toHaveFocus();
    await user.tab(); // leaves the group for Colour mode
    expect(document.activeElement?.closest('[role="radiogroup"]')?.getAttribute("aria-labelledby")).not.toBe(
      within(panel).getByRole("radiogroup", { name: "Theme" }).getAttribute("aria-labelledby"),
    );
    within(panel).getByRole("radio", { name: "Gamer" }).focus();
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("region", { name: "Preferences" })).toBeNull();
    await new Promise((r) => setTimeout(r, 0));
    expect(opener).toHaveFocus();
    expect(html().getAttribute("data-skin")).toBe("gamer"); // closing keeps the choice
  });

  it("restores a stored theme on load", () => {
    window.localStorage.setItem(PREFS_KEY, JSON.stringify({ skin: "portal", theme: "dark" }));
    mount();
    expect(html().getAttribute("data-skin")).toBe("portal");
    expect(html().getAttribute("data-theme")).toBe("dark");
  });

  it("is axe-clean open, and the panel adds exactly one status line and no other live region", async () => {
    const { view, panel } = await openPrefs();
    expect(view.container.querySelectorAll('[aria-live],[role="status"],[role="alert"],[role="log"]')).toHaveLength(1);
    expect(within(panel).getByRole("status")).toBeInTheDocument();
    expect(await axeViolations(view.container)).toEqual([]);
  });
});

describe("Reset look, Preview card, rollback, lecture settings", () => {
  it("Reset look to defaults restores every Look field and nothing else, and says so", async () => {
    window.localStorage.setItem(
      PREFS_KEY,
      JSON.stringify({ skin: "portal", theme: "dark", quiet: true, textSize: "l", spacing: "wide", density: "compact", reduceMotion: true, focusMode: true, showSeconds: true }),
    );
    const { user, panel } = await openPrefs();
    expect(html().getAttribute("data-skin")).toBe("portal");
    await user.click(within(panel).getByRole("button", { name: "Reset look to defaults" }));
    expect(stored()).toMatchObject({ skin: "paper", theme: "system", quiet: false, texture: false, solid: false, textSize: "m", spacing: "standard", density: "comfortable", reduceMotion: false });
    // Lecture settings are not touched.
    expect(stored()).toMatchObject({ focusMode: true, showSeconds: true });
    expect(html().getAttribute("data-skin")).toBe("paper");
    expect(html().getAttribute("data-quiet")).toBe("false");
    expect(within(panel).getByRole("status")).toHaveTextContent("Look reset to defaults.");
    expect(within(panel).getByRole("radio", { name: "Paper" })).toHaveAttribute("aria-checked", "true");
    expect(within(panel).getByRole("radio", { name: "System" })).toHaveAttribute("aria-checked", "true");
  });

  it("the Preview card is static: a caption for assistive technology, the rest hidden, nothing focusable, the pieces a lecture shows", async () => {
    const { panel } = await openPrefs();
    const fig = within(panel).getByRole("figure");
    expect(fig.querySelector("figcaption")).toHaveTextContent(PREFS_COPY.previewCaption);
    expect(fig.querySelector("figcaption")?.closest("[aria-hidden]")).toBeNull();
    const body = fig.querySelector('[aria-hidden="true"]');
    expect(body).not.toBeNull();
    expect(body!.querySelectorAll("button, a, input, select, textarea, [tabindex]")).toHaveLength(0);
    for (const s of [PREFS_COPY.preview.line1, PREFS_COPY.preview.recording, PREFS_COPY.preview.paused, PREFS_COPY.preview.primary]) expect(body!.textContent).toContain(s);
    expect(body!.querySelectorAll(".transcript-time")).toHaveLength(2);
  });

  it("rollback: with <html data-themes=off> the Theme control is not rendered; Colour mode and the rest stay", async () => {
    html().setAttribute("data-themes", "off");
    const { panel } = await openPrefs();
    expect(within(panel).queryByRole("radiogroup", { name: "Theme" })).toBeNull();
    expect(within(panel).getByRole("radiogroup", { name: "Colour mode" })).toBeInTheDocument();
    expect(within(panel).getByRole("button", { name: "Reset look to defaults" })).toBeInTheDocument();
  });

  it("the lecture's settings (compact) offer Colour mode and Low-stimulation look only: no Theme, no Reset, no preview", () => {
    render(
      <PrefsProvider>
        <LookControls compact />
      </PrefsProvider>,
    );
    expect(screen.queryByRole("radiogroup", { name: "Theme" })).toBeNull();
    expect(screen.getByRole("radiogroup", { name: "Colour mode" })).toBeInTheDocument();
    expect(screen.getByRole("switch", { name: "Low-stimulation look" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reset look to defaults" })).toBeNull();
    expect(screen.queryByRole("figure")).toBeNull();
  });
});

describe("copy (language guide; themes-spec 3.2 and 6)", () => {
  const strings = [...collectStrings(THEMES), ...collectStrings(PREFS_COPY)];
  it.each(FORBIDDEN)("no %s in the Preferences copy", (_name, re) => {
    expect(strings.filter((s) => re.test(s))).toEqual([]);
  });
  it("no sentence over 30 words; no theme is described as treatment, recommendation or an attention aid", () => {
    expect(sentencesOver(strings, 30)).toEqual([]);
    for (const t of THEMES) expect(t.description).not.toMatch(/\b(focus|attention|concentrat\w*|calm(s|ing)? you|helps?|recommended|best|treat\w*|adhd|symptom\w*)\b/i);
  });
  it("theme names and descriptions are the spec's strings word for word (themes-spec 3.2; Q-PO1 label 'Course portal')", () => {
    const spec = readFileSync(path.join(__dirname, "../../../../docs/ux/themes-spec.md"), "utf8");
    for (const t of THEMES) expect(spec).toContain(`"${t.description}"`);
    expect(THEMES.map((t) => t.name)).toEqual(["Paper", "Course portal", "Gamer", "Calm", "Glass"]);
    expect(strings.join(" ")).not.toMatch(/\bcanvas\b/i); // no third-party product name in the UI
  });
  it("the Low-stimulation hint is the spec's string and the rendered hint comes from the linted registry (review 3.2)", () => {
    const spec = readFileSync(path.join(__dirname, "../../../../docs/ux/themes-spec.md"), "utf8");
    expect(spec).toContain(`"${PREFS_COPY.lowStimHint}"`);
    expect(PREFS_COPY.lowStimHint).toBe("Removes shadows, texture, gradients and movement. On Paper the colours soften too. Recording status is never dimmed.");
    // The registry is what the FORBIDDEN loop above scans; prove the hint is in it.
    expect(strings).toContain(PREFS_COPY.lowStimHint);
    expect(strings).toContain(PREFS_COPY.themeHint);
    // Both Look surfaces render exactly the registry string as the switch's description...
    for (const compact of [false, true]) {
      const view = render(
        <PrefsProvider>
          <LookControls compact={compact} />
        </PrefsProvider>,
      );
      expect(within(view.container).getByRole("switch", { name: PREFS_COPY.lowStimLabel })).toHaveAccessibleDescription(PREFS_COPY.lowStimHint);
      view.unmount();
    }
    // ...and the component passes it by reference, not as a prop literal the lint would never see.
    const src = readFileSync(path.join(__dirname, "PreferencesPanel.tsx"), "utf8");
    expect(src).toContain("hint={P.lowStimHint}");
    expect(src).not.toMatch(/label="Low-stimulation look"/);
  });
});
