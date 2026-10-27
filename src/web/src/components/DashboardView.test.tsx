import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { Course } from "@/lib/api";
import { axeViolations } from "@/test/axe";
import { DashboardView } from "./DashboardView";

const course = (i: number): Course => ({
  id: `c${i}`,
  name: `Course ${i}`,
  createdAt: `2026-01-0${i}T00:00:00Z`,
  updatedAt: `2026-01-0${i}T00:00:00Z`,
});
const noop = () => undefined;

describe("DashboardView", () => {
  it("empty state: one sentence and exactly one primary action", async () => {
    const { container } = render(<DashboardView state={{ status: "ready", courses: [] }} onRetry={noop} />);
    expect(screen.getByRole("heading", { level: 1 })).toBeInTheDocument();
    const links = screen.getAllByRole("link");
    expect(links).toHaveLength(1);
    expect(links[0]).toHaveTextContent("Add your first course");
    expect(await axeViolations(container)).toEqual([]);
  });

  it("shows one next step and course cards with no counts or shaming copy", async () => {
    const { container } = render(
      <DashboardView state={{ status: "ready", courses: [1, 2, 3].map(course) }} displayName="Alice" onRetry={noop} />,
    );
    const next = screen.getByRole("region", { name: "Next step" });
    expect(within(next).getByText("Course 3", { selector: "p" })).toBeInTheDocument(); // newest, deterministic
    expect(screen.getAllByRole("listitem")).toHaveLength(3);
    const text = container.textContent ?? "";
    expect(text).not.toMatch(/overdue|behind|missed|failed|unfinished|streak|\d+\s+(left|remaining|pending)/i);
    expect(await axeViolations(container)).toEqual([]);
  });

  it("caps cards at 6 and offers See all courses", () => {
    render(<DashboardView state={{ status: "ready", courses: [1, 2, 3, 4, 5, 6, 7, 8].map(course) }} onRetry={noop} />);
    expect(screen.getAllByRole("listitem")).toHaveLength(6);
    expect(screen.getByRole("link", { name: "See all courses" })).toHaveAttribute("href", "/courses");
  });

  it("explains the choice via Why this?", async () => {
    render(<DashboardView state={{ status: "ready", courses: [course(1)] }} onRetry={noop} />);
    await userEvent.click(screen.getByText("Why this?"));
    expect(screen.getByText(/added most recently/)).toBeVisible();
  });

  it("loading and error states are accessible and offer retry", async () => {
    const { container, rerender } = render(<DashboardView state={{ status: "loading" }} onRetry={noop} />);
    expect(screen.getByRole("status")).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);

    const onRetry = vi.fn();
    rerender(<DashboardView state={{ status: "error", message: "We couldn't load your courses." }} onRetry={onRetry} />);
    await userEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(onRetry).toHaveBeenCalled();
    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });
});
