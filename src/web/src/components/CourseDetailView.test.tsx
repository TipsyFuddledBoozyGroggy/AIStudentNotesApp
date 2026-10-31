import { render, screen } from "@testing-library/react";
import { axeViolations } from "@/test/axe";
import { CourseDetailView } from "./CourseDetailView";

const course = { id: "c1", name: "Biology 101", createdAt: "2026-01-01T00:00:00Z", updatedAt: "2026-01-01T00:00:00Z" };

describe("CourseDetailView", () => {
  it("moves focus to the heading, announces success and has no axe violations", async () => {
    const { container } = render(<CourseDetailView course={course} justAdded />);
    expect(screen.getByRole("heading", { level: 1, name: "Biology 101" })).toHaveFocus();
    expect(screen.getByRole("status")).toHaveTextContent("Biology 101 added.");
    expect(await axeViolations(container)).toEqual([]);
  });

  it("lecture entry is a link to the consent screen and never touches the microphone", () => {
    const getUserMedia = vi.fn();
    Object.defineProperty(navigator, "mediaDevices", { value: { getUserMedia }, configurable: true });
    render(<CourseDetailView course={course} />);
    const link = screen.getByRole("link", { name: "Start a lecture" });
    expect(link).toHaveAttribute("href", "/courses/c1/lecture");
    link.addEventListener("click", (e) => e.preventDefault());
    link.click();
    expect(getUserMedia).not.toHaveBeenCalled();
  });

  it("slice 4: Desk sections with margin labels, one filled button, Syllabus as one ledger row, no cards", async () => {
    const { container } = render(<CourseDetailView course={course} />);
    expect(container.querySelectorAll(".btn-primary")).toHaveLength(1);
    expect(container.querySelectorAll(".card, .panel")).toHaveLength(0);
    for (const name of ["Lecture", "Syllabus"]) {
      const h = screen.getByRole("heading", { level: 2, name });
      expect(h).toHaveClass("label", "desk-label");
      expect(h.parentElement).toHaveClass("desk-section");
    }
    const syllabus = screen.getByRole("region", { name: "Syllabus" });
    expect(syllabus.querySelector(".ledger > .ledger-row")).not.toBeNull();
    // Unchanged wording beside the lecture entry (consent-adjacent; layout only in this slice).
    expect(screen.getByText("Nothing is recorded until you choose to start. You will see what Tether does first, and you can pause or stop at any time.")).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });
});
