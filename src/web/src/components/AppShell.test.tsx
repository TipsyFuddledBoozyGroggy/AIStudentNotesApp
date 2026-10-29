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

import { AppShell } from "./AppShell";
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

  it("adds no live regions of its own (the lecture screen owns exactly three)", () => {
    const { container } = mount();
    expect(container.querySelectorAll('[aria-live],[role="status"],[role="alert"],[role="log"]')).toHaveLength(0);
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
    expect(screen.getByRole("status")).toHaveTextContent("Dark theme on.");
    await user.click(screen.getByRole("switch", { name: "Reduce motion" }));
    expect(screen.getByRole("status")).toHaveTextContent("Reduce motion on.");
    await user.click(screen.getByRole("button", { name: "Close" }));
    expect(container.querySelectorAll('[role="status"]')).toHaveLength(0);
  });
});
