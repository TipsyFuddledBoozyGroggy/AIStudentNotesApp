import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axeViolations } from "@/test/axe";
import { searchCourses } from "./AllCoursesPage";
import { CommandPalette, filterCommands, type Command } from "./CommandPalette";

vi.mock("next/navigation", () => ({ usePathname: () => "/courses", useRouter: () => ({ replace: vi.fn(), push: vi.fn() }) }));
vi.mock("./AuthProvider", () => ({ useRequireUser: () => ({ status: "loading" }), useAuth: () => ({ status: "loading" }) }));

const cmd = (id: string, label: string, group = "Place", keywords = ""): Command => ({ id, label, group, keywords, run: vi.fn() });

describe("CommandPalette", () => {
  it("filters by every word, across label, group and keywords", () => {
    const list = [cmd("a", "Biology 101", "Course", "BIO Fall"), cmd("b", "History of Law", "Course"), cmd("c", "Start screen for Biology 101", "Lecture", "record")];
    expect(filterCommands(list, "bio").map((c) => c.id)).toEqual(["a", "c"]);
    expect(filterCommands(list, "bio lecture").map((c) => c.id)).toEqual(["c"]);
    expect(filterCommands(list, "  ").map((c) => c.id)).toEqual(["a", "b", "c"]);
  });

  it("is a modal combobox: arrows move, Enter runs the active item and closes, Escape closes; axe-clean", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    const items = [cmd("a", "Today"), cmd("b", "All courses"), cmd("c", "Add a course")];
    const { container } = render(<CommandPalette commands={items} onClose={onClose} />);
    expect(screen.getByRole("dialog", { name: "Search or jump" })).toHaveAttribute("aria-modal", "true");
    const box = screen.getByRole("combobox");
    expect(box).toHaveFocus();
    await user.keyboard("{ArrowDown}");
    expect(screen.getByRole("option", { name: /All courses/ })).toHaveAttribute("aria-selected", "true");
    await user.keyboard("{Tab}");
    expect(box).toHaveFocus(); // the input is the only tab stop
    await user.keyboard("{Enter}");
    expect(items[1].run).toHaveBeenCalledTimes(1);
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(await axeViolations(container)).toEqual([]);
    await user.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it("says when nothing matches, outside the listbox", async () => {
    render(<CommandPalette commands={[cmd("a", "Today")]} onClose={vi.fn()} />);
    await userEvent.type(screen.getByRole("combobox"), "zzz");
    expect(screen.queryAllByRole("option")).toHaveLength(0);
    expect(screen.getByText("Nothing matches. Try a course name.")).toBeInTheDocument();
  });
});

describe("course search", () => {
  const c = (id: string, name: string, code?: string) => ({ id, name, code, createdAt: "2026-01-01T00:00:00Z", updatedAt: "2026-01-01T00:00:00Z" });
  it("matches name or code, every word, case-insensitively", () => {
    const list = [c("1", "Biology 101", "BIO 101"), c("2", "History of Law", "HIST 340"), c("3", "Calculus II", "MATH 152")];
    expect(searchCourses(list, "hist").map((x) => x.id)).toEqual(["2"]);
    expect(searchCourses(list, "math 152").map((x) => x.id)).toEqual(["3"]);
    expect(searchCourses(list, "").map((x) => x.id)).toEqual(["1", "2", "3"]);
    expect(searchCourses(list, "chemistry")).toEqual([]);
  });
});
