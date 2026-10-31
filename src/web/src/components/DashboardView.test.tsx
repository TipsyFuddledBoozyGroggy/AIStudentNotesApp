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

  // Redesign-round2 slice 4 (wireframe 5.3): Desk template, no cards, margin labels, courses as a ledger.
  describe("slice 4: Today on the Desk template", () => {
    it("courses are ledger rows whose one link is the course name; \"Open\" is visual only; no \"No lectures yet\"", async () => {
      const withLecture = { ...course(1), lastSessionAt: "2026-10-27T15:00:00Z" };
      const { container } = render(<DashboardView state={{ status: "ready", courses: [withLecture, course(2), course(3)] }} displayName="Alice" onRetry={noop} />);
      const list = screen.getByRole("list");
      expect(list).toHaveClass("ledger");
      const rows = within(list).getAllByRole("listitem");
      expect(rows).toHaveLength(3);
      for (const row of rows) {
        expect(row).toHaveClass("ledger-row");
        const links = within(row).getAllByRole("link");
        expect(links).toHaveLength(1); // one tab stop, one name per row
        expect(links[0]).toHaveClass("ledger-row-link");
        expect(links[0].getAttribute("href")).toMatch(/^\/courses\/c\d$/);
        expect(row.querySelector(".ledger-row-end")).toHaveAttribute("aria-hidden", "true");
      }
      expect(within(list).getByRole("link", { name: "Course 1" })).toBeInTheDocument();
      expect(container.textContent).not.toMatch(/No lectures yet/);
      expect(within(rows.find((r) => r.textContent?.includes("Course 1"))!).getByText(/^Last lecture: /)).toBeInTheDocument();
      expect(await axeViolations(container)).toEqual([]);
    });

    it("one filled button; Next step, Your courses and the date are margin labels; How a lecture works follows the list", () => {
      const { container } = render(<DashboardView state={{ status: "ready", courses: [1, 2].map(course) }} displayName="Alice" onRetry={noop} />);
      expect(container.querySelectorAll(".btn-primary")).toHaveLength(1);
      expect(container.querySelectorAll(".card, .panel")).toHaveLength(0);
      for (const name of ["Next step", "Your courses"]) {
        const h = screen.getByRole("heading", { level: 2, name });
        expect(h).toHaveClass("label", "desk-label");
        expect(h.parentElement).toHaveClass("desk-section");
      }
      const h1 = screen.getByRole("heading", { level: 1, name: "Hi, Alice." });
      expect(h1.previousElementSibling).toHaveClass("label", "desk-label"); // the date
      expect(screen.getByRole("link", { name: "Add a course" })).toHaveClass("desk-action");
      const list = screen.getByRole("list");
      const how = screen.getByRole("complementary", { name: "About lectures" });
      expect(list.compareDocumentPosition(how) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(how.querySelector("summary")).toHaveTextContent("How a lecture works");
    });

    it("loading keeps the greeting row and says one calm sentence; error is a note with one action", () => {
      const { container, rerender } = render(<DashboardView state={{ status: "loading" }} onRetry={noop} />);
      expect(screen.getByRole("heading", { level: 1, name: "Your dashboard" })).toBeInTheDocument();
      expect(screen.getByRole("status")).toHaveTextContent("Getting your courses.");
      expect(container.querySelector("[aria-hidden=true].h-44")).toBeNull(); // the boxed placeholder is gone
      rerender(<DashboardView state={{ status: "error", message: "We couldn't load your courses." }} onRetry={noop} />);
      expect(screen.getByRole("alert")).toHaveClass("note", "note-notice");
      expect(within(screen.getByRole("alert")).getAllByRole("button")).toHaveLength(1);
    });
  });
});
