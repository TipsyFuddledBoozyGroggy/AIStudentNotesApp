import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axeViolations } from "@/test/axe";
import { clearAllDrafts, setDraftOwner } from "./syllabus/storage";
import { loadStopMove, saveStopMove, stopMoveKey } from "./lecture/settings";

// PO-19 (docs/ux/stop-redirect.md section 4, criterion 9): Preferences > Lectures, one switch (On = ask, the default)
// that reads and writes the signed-in user's own key; "Reset look to defaults", sign-out's draft clearing and the
// syllabus owner switch leave it alone; the Preferences panel adds no live region beyond its own status line.

const auth = vi.hoisted(() => ({
  status: "authenticated" as "authenticated" | "unauthenticated",
  user: { id: "u.1", email: "alice@dev.local", displayName: "Alice" } as { id: string; email: string; displayName: string } | null,
  signOut: vi.fn(),
}));
vi.mock("next/navigation", () => ({ usePathname: () => "/courses/c1", useRouter: () => ({ replace: vi.fn(), push: vi.fn() }) }));
vi.mock("./AuthProvider", () => ({ useAuth: () => ({ ...auth, refresh: vi.fn() }) }));

import { AppShell } from "./AppShell";
import { PrefsProvider } from "./PrefsProvider";
import { PREFS_COPY as P } from "./prefsCopy";

const KEY = stopMoveKey("u.1");
beforeEach(() => {
  window.localStorage.clear();
  auth.status = "authenticated";
  auth.user = { id: "u.1", email: "alice@dev.local", displayName: "Alice" };
});

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
  const panel = screen.getByRole("region", { name: "Preferences" });
  return { user, view, panel };
}
const status = (panel: HTMLElement) => panel.querySelector('[role="status"]')!;

describe("Preferences > Lectures (PO-19)", () => {
  it("a Lectures section between Look and Account / About with one switch, On by default, its hint as the description", async () => {
    const { panel, view } = await openPrefs();
    const heads = within(panel).getAllByRole("heading", { level: 3 }).map((h) => h.textContent);
    expect(heads).toEqual(["Look", P.lecturesLegend, "Account", "About"]);
    const sw = within(panel).getByRole("switch", { name: P.stopMoveLabel });
    expect(sw).toHaveAttribute("aria-checked", "true");
    expect(sw).toHaveAccessibleDescription(P.stopMoveHint);
    expect(sw).toHaveTextContent("On");
    expect(await axeViolations(view.container)).toEqual([]);
  });

  it("turning it Off stores direct for this user and says so; On removes the key and says so", async () => {
    const { user, panel } = await openPrefs();
    const sw = within(panel).getByRole("switch", { name: P.stopMoveLabel });
    await user.click(sw);
    expect(sw).toHaveAttribute("aria-checked", "false");
    expect(window.localStorage.getItem(KEY)).toBe("direct");
    expect(loadStopMove("u.1")).toBe(true);
    expect(status(panel)).toHaveTextContent(P.stopMoveMsg.off);
    await user.click(sw);
    expect(sw).toHaveAttribute("aria-checked", "true");
    expect(window.localStorage.getItem(KEY)).toBeNull();
    expect(status(panel)).toHaveTextContent(P.stopMoveMsg.on);
  });

  it("reads the stored choice: a saved direct shows Off", async () => {
    saveStopMove("u.1", true);
    const { panel } = await openPrefs();
    expect(within(panel).getByRole("switch", { name: P.stopMoveLabel })).toHaveAttribute("aria-checked", "false");
  });

  it("another user's choice is not shown or changed", async () => {
    saveStopMove("u.2", true);
    const { user, panel } = await openPrefs();
    const sw = within(panel).getByRole("switch", { name: P.stopMoveLabel });
    expect(sw).toHaveAttribute("aria-checked", "true");
    await user.click(sw);
    await user.click(sw);
    expect(loadStopMove("u.2")).toBe(true);
  });

  it("Reset look to defaults leaves the key (it is not a look setting)", async () => {
    saveStopMove("u.1", true);
    const { user, panel } = await openPrefs();
    await user.click(within(panel).getByRole("button", { name: P.reset }));
    expect(window.localStorage.getItem(KEY)).toBe("direct");
    expect(within(panel).getByRole("switch", { name: P.stopMoveLabel })).toHaveAttribute("aria-checked", "false");
  });

  it("not signed in: no Lectures section (there is no user to scope it to)", async () => {
    auth.status = "unauthenticated";
    auth.user = null;
    const { panel } = await openPrefs();
    expect(within(panel).queryByRole("switch", { name: P.stopMoveLabel })).toBeNull();
    expect(within(panel).queryByRole("heading", { name: P.lecturesLegend })).toBeNull();
  });

  it("the panel's only live region is its own status line (the switch adds none)", async () => {
    const { panel } = await openPrefs();
    expect(panel.querySelectorAll('[role="status"],[role="alert"],[aria-live]')).toHaveLength(1);
  });

  it("kept across sign-out and the syllabus owner switch (not a draft: SB-06's clearing does not touch it)", () => {
    saveStopMove("u.1", true);
    clearAllDrafts();
    setDraftOwner("someone-else");
    expect(window.localStorage.getItem(KEY)).toBe("direct");
  });

  it("the lecture settings popover does not get a copy of the switch (one place to change it)", async () => {
    const { readFileSync } = await import("node:fs");
    const path = await import("node:path");
    const notices = readFileSync(path.join(__dirname, "lecture", "Notices.tsx"), "utf8");
    expect(notices).not.toMatch(/stopMove/);
  });
});
