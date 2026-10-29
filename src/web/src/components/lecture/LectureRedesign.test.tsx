import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { axeViolations } from "@/test/axe";
import { baseEvents, mountLecture } from "./testUtils";
import type { LectureAi } from "./ports";

afterEach(() => {
  vi.useRealTimers();
  window.localStorage.clear();
});

const fakeAi = () =>
  ({
    getSummary: vi.fn().mockResolvedValue(null),
    startQuickRecap: vi.fn(),
    getCatchUp: vi.fn().mockResolvedValue(null),
  }) as unknown as LectureAi;

describe("Lecture screen redesign (design-direction slices 4-5)", () => {
  it("has exactly one h1 and the bar keeps its frozen names", async () => {
    await mountLecture();
    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
    const bar = screen.getByRole("banner", { name: "Recording status" });
    expect(within(bar).getByRole("heading", { level: 2, name: "Recording" })).toBeInTheDocument();
    expect(within(bar).getByRole("button", { name: "Pause" })).toBeInTheDocument();
    expect(within(bar).getByRole("button", { name: "Stop" })).toBeInTheDocument();
  });

  it("Leave is absent while Active or Paused (Lead decision) and present once Stopped", async () => {
    await mountLecture();
    expect(screen.queryByRole("button", { name: /Leave/ })).toBeNull();
  });

  it("Leave is absent while Paused", async () => {
    await mountLecture({ events: baseEvents("Paused", "user"), track: "ended" });
    expect(screen.getByRole("region", { name: "Recording is paused" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Leave/ })).toBeNull();
  });

  it("Stopped: one exit (Back to course), no Leave, no Focus toggle, no Pause updates (review S2)", async () => {
    const m = await mountLecture({ events: baseEvents("Stopped", "user"), track: "ended" });
    expect(screen.queryByRole("button", { name: /Leave/ })).toBeNull();
    expect(screen.queryByRole("button", { name: "Focus" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Pause updates" })).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: "Back to course" }));
    expect(m.onBack).toHaveBeenCalled();
  });

  it("finished lecture: Search this lecture filters loaded lines; Export is disabled with its reason (slice 8)", async () => {
    await mountLecture({ events: baseEvents("Stopped", "user"), track: "ended" });
    const search = screen.getByRole("searchbox", { name: "Search this lecture" });
    expect(screen.getByText("2 lines.")).toBeInTheDocument();
    await userEvent.type(search, "second");
    expect(screen.getByText("1 of 2 lines match.")).toBeInTheDocument();
    expect(screen.getByText("Second line of the lecture.")).toBeInTheDocument();
    expect(screen.queryByText("First line of the lecture.")).toBeNull();
    const exp = screen.getByRole("button", { name: "Export to Google Docs" });
    expect(exp).toHaveAttribute("aria-disabled", "true");
    expect(exp).toHaveAccessibleDescription(/Nothing is sent anywhere/);
  });

  it("no review search while the lecture is live", async () => {
    await mountLecture();
    expect(screen.queryByRole("searchbox", { name: "Search this lecture" })).toBeNull();
  });

  it("elapsed time shows whole minutes by default (nothing ticks every second)", async () => {
    await mountLecture();
    expect(screen.getByTestId("elapsed").textContent).toMatch(/^\d+ min$/);
  });

  it("Focus toggle: pressed state, keeps the bar and Quick recap, collapses the TL;DR in place, adds no live region", async () => {
    const m = await mountLecture({ ai: fakeAi() });
    const focus = screen.getByRole("button", { name: "Focus" });
    expect(focus).toHaveAttribute("aria-pressed", "false");
    expect(screen.getByRole("button", { name: /recap of the last 5 minutes$/ })).toBeInTheDocument();
    await userEvent.click(focus);
    expect(focus).toHaveAttribute("aria-pressed", "true");
    expect(m.container.querySelector('[data-layout="focus"]')).not.toBeNull();
    // Bar and dock stay; the TL;DR becomes a one-line summary with its rest one press away.
    expect(screen.getByRole("banner", { name: "Recording status" })).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: /recap of the last \d+ minutes$/ })).toHaveLength(3);
    const more = screen.getByRole("button", { name: "More of the TL;DR" });
    expect(more).toHaveAttribute("aria-expanded", "false");
    await userEvent.click(more);
    expect(screen.getByRole("button", { name: "Less of the TL;DR" })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("button", { name: "Pause updates" })).toBeInTheDocument();
    const live = Array.from(document.querySelectorAll('[role="status"],[role="alert"],[aria-live]')).filter((e) => e.getAttribute("aria-live") !== "off");
    expect(live.map((e) => e.getAttribute("data-region"))).toEqual(["status", "alert"]);
    expect(await axeViolations(m.container)).toEqual([]);
    await userEvent.click(focus);
    expect(focus).toHaveAttribute("aria-pressed", "false");
  });

  it("the usual Quick recap window is the only filled button in the dock; busy never greys the buttons out", async () => {
    await mountLecture({ ai: fakeAi() });
    const usual = screen.getByRole("button", { name: /\(your usual\)/ });
    expect(usual.className).toMatch(/btn-primary/);
    const others = screen.getAllByRole("button", { name: /recap of the last \d+ minutes$/ }).filter((b) => b !== usual);
    for (const b of others) expect(b.className).not.toMatch(/btn-primary/);
    for (const b of [usual, ...others]) expect(b.className).toMatch(/btn-steady/);
  });
});
