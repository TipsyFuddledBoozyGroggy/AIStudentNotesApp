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
});
