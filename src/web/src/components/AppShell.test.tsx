import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axeViolations } from "@/test/axe";
import { PREFS_KEY } from "@/lib/design/prefs";

const auth = vi.hoisted(() => ({
  status: "authenticated" as "authenticated" | "unauthenticated",
  user: { id: "u1", email: "alice@dev.local", displayName: "Alice" } as { id: string; email: string; displayName: string } | null,
  signOut: vi.fn(),
}));
vi.mock("next/navigation", () => ({ usePathname: () => "/courses/c1", useRouter: () => ({ replace: vi.fn(), push: vi.fn() }) }));
vi.mock("./AuthProvider", () => ({ useAuth: () => ({ ...auth, refresh: vi.fn() }) }));

import { AppShell, templateFor } from "./AppShell";
import { PrefsProvider } from "./PrefsProvider";

const mount = () =>
  render(
    <PrefsProvider>
      <AppShell>
        <h1>Page</h1>
      </AppShell>
    </PrefsProvider>,
  );

afterEach(() => {
  window.localStorage.clear();
  for (const a of ["data-theme", "data-quiet", "data-text-size", "data-spacing", "data-density", "data-reduce-motion"]) document.documentElement.removeAttribute(a);
  auth.status = "authenticated";
});

describe("AppShell", () => {
  it("shows the wordmark, Preferences and Sign out; marks the current destination; axe-clean", async () => {
    const { container } = mount();
    expect(screen.getByRole("link", { name: "Tether" })).toHaveAttribute("href", "/");
    expect(screen.getByRole("button", { name: "Sign out" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Preferences" })).toHaveAttribute("aria-expanded", "false");
    const rail = screen.getByRole("navigation", { name: "Primary" });
    expect(within(rail).getByRole("link", { name: "Courses" })).toHaveAttribute("aria-current", "page");
    expect(within(rail).getByRole("link", { name: "Today" })).not.toHaveAttribute("aria-current");
    expect(screen.getByRole("main")).toHaveAttribute("id", "main");
    expect(await axeViolations(container)).toEqual([]);
  });

  it("signed out: no destinations and no Sign out, Preferences still reachable", () => {
    auth.status = "unauthenticated";
    mount();
    expect(screen.queryByRole("button", { name: "Sign out" })).toBeNull();
    expect(screen.queryByRole("navigation")).toBeNull();
    expect(screen.getByRole("button", { name: "Preferences" })).toBeInTheDocument();
  });

  it("Preferences: opens in place with focus on its heading, applies and saves a change, Escape closes and returns focus", async () => {
    const user = userEvent.setup();
    const { container } = mount();
    const open = screen.getByRole("button", { name: "Preferences" });
    await user.click(open);
    const panel = screen.getByRole("region", { name: "Preferences" });
    expect(within(panel).getByRole("heading", { name: "Preferences" })).toHaveFocus();
    expect(screen.queryByRole("dialog")).toBeNull(); // non-modal

    await user.click(within(panel).getByRole("radio", { name: "Dark" }));
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    await user.click(within(panel).getByRole("switch", { name: "Low-stimulation look" }));
    expect(document.documentElement.getAttribute("data-quiet")).toBe("true");
    await user.click(within(panel).getByRole("radio", { name: "Large" }));
    expect(document.documentElement.getAttribute("data-text-size")).toBe("l");
    expect(JSON.parse(window.localStorage.getItem(PREFS_KEY) ?? "{}")).toMatchObject({ theme: "dark", quiet: true, textSize: "l" });
    expect(await axeViolations(container)).toEqual([]);

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("region", { name: "Preferences" })).toBeNull();
    await new Promise((r) => setTimeout(r, 0));
    expect(open).toHaveFocus();
  });

  it("restores saved preferences on load", () => {
    window.localStorage.setItem(PREFS_KEY, JSON.stringify({ theme: "dark", density: "compact" }));
    mount();
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    expect(document.documentElement.getAttribute("data-density")).toBe("compact");
  });

  it("from 1024 the header is fixed (not sticky) over an aria-hidden spacer, and its right edge follows the Preferences panel", async () => {
    // A sticky header's controls are paired by axe target-size with page targets scrolled under it (CI run 41322171511);
    // a fixed one is not. The geometry spec (slice 8c straddle check) proves the browser side.
    const user = userEvent.setup();
    mount();
    const header = screen.getByRole("banner");
    expect(header.className).toMatch(/\blg:fixed\b/);
    expect(header.className).not.toMatch(/\blg:sticky\b/);
    expect(header.className).toMatch(/\blg:right-0\b/);
    const spacer = header.nextElementSibling as HTMLElement;
    expect(spacer).toHaveAttribute("data-site-header-spacer");
    expect(spacer).toHaveAttribute("aria-hidden", "true");
    // It mirrors the header: box-content height of the 56px bar plus the header's 1px bottom border.
    expect(spacer.className.split(" ")).toEqual(expect.arrayContaining(["box-content", "hidden", "border-b", "lg:block", "lg:h-14"]));
    await user.click(screen.getByRole("button", { name: "Preferences" }));
    expect(header.className).toMatch(/\blg:right-96\b/);
  });

  it("adds no live regions of its own (the lecture screen owns exactly three)", () => {
    const { container } = mount();
    expect(container.querySelectorAll('[aria-live],[role="status"],[role="alert"],[role="log"]')).toHaveLength(0);
  });
});

describe("AppShell header tabs and templates (redesign-round2 slice 1, PO-17)", () => {
  it("the two destinations are tabs inside the header bar; there is no Menu button and no rail; order is wordmark, Today, Courses, Search, Preferences, Sign out", () => {
    mount();
    const banner = screen.getByRole("banner");
    const nav = within(banner).getByRole("navigation", { name: "Primary" });
    expect(within(nav).getAllByRole("link").map((l) => l.textContent)).toEqual(["Today", "Courses"]);
    expect(within(nav).getByRole("link", { name: "Courses" })).toHaveClass("nav-tab");
    expect(screen.queryByRole("button", { name: "Menu" })).toBeNull();
    expect(screen.getAllByRole("navigation")).toHaveLength(1);
    const order = Array.from(banner.querySelectorAll<HTMLElement>("a, button")).map((el) => el.textContent?.replace(/Ctrl K$/, "").trim());
    expect(order).toEqual(["Tether", "Today", "Courses", "Search or jump", "Preferences", "Sign out"]);
    // The display name left the header for Preferences > Account.
    expect(within(banner).queryByText("Alice")).toBeNull();
  });

  it("Preferences > Account shows the signed-in name", async () => {
    const user = userEvent.setup();
    mount();
    await user.click(screen.getByRole("button", { name: "Preferences" }));
    const panel = screen.getByRole("region", { name: "Preferences" });
    expect(within(panel).getByRole("heading", { name: "Account" })).toBeInTheDocument();
    expect(within(panel).getByText("Signed in as Alice.")).toBeInTheDocument();
  });

  it("main carries the page template from the route", () => {
    expect(screen.queryByRole("main")).toBeNull();
    mount();
    expect(screen.getByRole("main")).toHaveAttribute("data-template", "desk"); // pathname /courses/c1
    expect(screen.getByRole("main").firstElementChild).toHaveAttribute("data-column-start");
    expect(templateFor("/")).toBe("desk");
    expect(templateFor("/courses")).toBe("desk");
    expect(templateFor("/courses/abc")).toBe("desk");
    expect(templateFor("/courses/abc/syllabus")).toBe("desk");
    expect(templateFor("/courses/new")).toBe("sheet");
    expect(templateFor("/courses/abc/lecture")).toBe("sheet");
    expect(templateFor("/lectures/ses_1")).toBe("sheet");
    expect(templateFor("/signin")).toBe("auth");
    expect(templateFor("/dev/ui-kit")).toBe("plain");
  });
});

describe("AppShell command palette (slice 7)", () => {
  it("opens from the visible button and from Ctrl+K, offers no recording controls, and restores focus on Escape", async () => {
    const user = userEvent.setup();
    mount();
    const button = screen.getByRole("button", { name: "Search or jump" });
    await user.click(button);
    expect(screen.getByRole("dialog", { name: "Search or jump" })).toBeInTheDocument();
    const labels = screen.getAllByRole("option").map((o) => o.textContent ?? "");
    expect(labels.join(" | ")).not.toMatch(/\b(start recording|pause|resume|stop)\b/i);
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).toBeNull();
    await new Promise((r) => setTimeout(r, 0));
    expect(button).toHaveFocus();
    await user.keyboard("{Control>}k{/Control}");
    expect(screen.getByRole("dialog", { name: "Search or jump" })).toBeInTheDocument();
  });

  it("Ctrl+K does nothing while a live lecture screen is mounted", async () => {
    const user = userEvent.setup();
    mount();
    const marker = document.createElement("div");
    marker.setAttribute("data-lecture-root", "");
    document.body.appendChild(marker);
    await user.keyboard("{Control>}k{/Control}");
    expect(screen.queryByRole("dialog")).toBeNull();
    marker.remove();
  });
});

describe("Preferences confirmation line (review ruling 2)", () => {
  it("says what changed in a status line that exists only while the panel is open", async () => {
    const user = userEvent.setup();
    const { container } = mount();
    expect(container.querySelectorAll('[role="status"]')).toHaveLength(0);
    await user.click(screen.getByRole("button", { name: "Preferences" }));
    await user.click(screen.getByRole("radio", { name: "Dark" }));
    // Was "Dark theme on." until slice 5: the control is now "Colour mode" and "Theme" names the five looks (themes-spec 3.2).
    expect(screen.getByRole("status")).toHaveTextContent("Dark mode on.");
    await user.click(screen.getByRole("switch", { name: "Reduce motion" }));
    expect(screen.getByRole("status")).toHaveTextContent("Reduce motion on.");
    await user.click(screen.getByRole("button", { name: "Close" }));
    expect(container.querySelectorAll('[role="status"]')).toHaveLength(0);
  });
});
