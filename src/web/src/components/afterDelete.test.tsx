import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { StrictMode } from "react";
import { axeViolations } from "@/test/axe";
import { COURSE_DELETE_COPY as CD, DELETE_PROBLEM_COPY as P, LECTURE_DELETE_COPY as LD } from "./deleteCopy";
import { markDeleted, takeDeleted } from "./deleteFlow";

// PO-20by / draft 6.2: after a delete the student lands on the course page (lecture) or the dashboard (course); focus goes
// to the h1, the existing announcer says the line once, and a dismissable note without a timer says what was deleted.
// Rendered under StrictMode (as `next dev` does), so the marker's double read is covered. Route, auth and API are stand-ins.

const push = vi.fn();
const course = { id: "c1", name: "Biology 101", createdAt: "2026-01-01T00:00:00Z", updatedAt: "2026-01-01T00:00:00Z" };
const deleteCourse = vi.fn<(id: string) => Promise<void>>();
vi.mock("next/navigation", () => ({ useParams: () => ({ id: "c1" }), useRouter: () => ({ replace: vi.fn(), push }) }));
vi.mock("./AuthProvider", () => ({ useRequireUser: () => ({ status: "authenticated", user: { id: "u1", displayName: "Sam" } }) }));
vi.mock("@/lib/api", async (orig) => {
  const real = await orig<typeof import("@/lib/api")>();
  return {
    ...real,
    api: {
      getCourse: vi.fn(async () => course),
      listCourses: vi.fn(async () => ({ items: [], nextCursor: null })),
      listSessions: vi.fn(async () => []),
      deleteCourse: (id: string) => deleteCourse(id),
    },
  };
});
vi.mock("@/lib/syllabus/client", async (orig) => {
  const real = await orig<typeof import("@/lib/syllabus/client")>();
  return { ...real, syllabusApi: { ...real.syllabusApi, listSyllabi: vi.fn(async () => []) } };
});

import { CourseDetailPage } from "./CourseDetailPage";
import { DashboardPage } from "./DashboardPage";

function statusLog() {
  const log: string[] = [];
  const obs = new MutationObserver(() => {
    const t = document.querySelector('[data-region="status"]')?.textContent;
    if (t && log.at(-1) !== t) log.push(t);
  });
  obs.observe(document.body, { childList: true, subtree: true, characterData: true });
  return log;
}

beforeEach(() => {
  window.sessionStorage.clear();
  push.mockReset();
  deleteCourse.mockReset();
});

describe("course page after a lecture delete", () => {
  it("note under the h1, h1 focused, said once, marker removed; Dismiss removes it and keeps focus on the page; no axe violations", async () => {
    markDeleted({ kind: "lecture", courseId: "c1", note: LD.doneNote("Week 3"), announce: LD.done });
    const log = statusLog();
    const { container } = render(
      <StrictMode>
        <CourseDetailPage />
      </StrictMode>,
    );
    const h1 = await screen.findByRole("heading", { level: 1, name: "Biology 101" });
    const note = await screen.findByText("Deleted: Week 3.");
    await waitFor(() => expect(h1).toHaveFocus());
    expect(window.sessionStorage.getItem("tether.deleted.v1")).toBeNull();
    await waitFor(() => expect(log).toContain(LD.done));
    await new Promise((r) => setTimeout(r, 300));
    expect(log.filter((t) => t === LD.done)).toHaveLength(1);
    expect(await axeViolations(container)).toEqual([]);
    await userEvent.click(within(note.closest("[data-deleted-note]") as HTMLElement).getByRole("button", { name: P.dismiss }));
    expect(screen.queryByText("Deleted: Week 3.")).toBeNull();
    expect(h1).toHaveFocus();
  });

  it("no marker (a normal visit): no note, nothing said", async () => {
    const log = statusLog();
    render(<CourseDetailPage />);
    await screen.findByRole("heading", { level: 1, name: "Biology 101" });
    await new Promise((r) => setTimeout(r, 200));
    expect(document.querySelector("[data-deleted-note]")).toBeNull();
    expect(log).toEqual([]);
  });

  it("Delete this course: after the 204 the dashboard's note is written and the page moves to /", async () => {
    deleteCourse.mockResolvedValue(undefined);
    render(<CourseDetailPage />);
    await userEvent.click(await screen.findByRole("button", { name: CD.button }));
    await userEvent.click(await screen.findByRole("button", { name: CD.confirm }));
    await waitFor(() => expect(push).toHaveBeenCalledWith("/"));
    expect(deleteCourse).toHaveBeenCalledWith("c1");
    expect(takeDeleted({ kind: "course" })).toEqual({ kind: "course", note: "Deleted: Biology 101.", announce: CD.done });
  });
});

describe("dashboard after a course delete", () => {
  it("note under the greeting, h1 focused, said once; Dismiss removes it", async () => {
    markDeleted({ kind: "course", note: CD.doneNote("Biology 101"), announce: CD.done });
    const log = statusLog();
    render(
      <StrictMode>
        <DashboardPage />
      </StrictMode>,
    );
    const h1 = await screen.findByRole("heading", { level: 1, name: "Hi, Sam." });
    const note = await screen.findByText("Deleted: Biology 101.");
    await waitFor(() => expect(h1).toHaveFocus());
    await waitFor(() => expect(log).toContain(CD.done));
    await new Promise((r) => setTimeout(r, 300));
    expect(log.filter((t) => t === CD.done)).toHaveLength(1);
    await userEvent.click(within(note.closest("[data-deleted-note]") as HTMLElement).getByRole("button", { name: P.dismiss }));
    expect(screen.queryByText("Deleted: Biology 101.")).toBeNull();
    expect(h1).toHaveFocus();
  });

  it("a lecture's marker is never shown on the dashboard", async () => {
    markDeleted({ kind: "lecture", courseId: "c1", note: "Deleted: Week 3.", announce: LD.done });
    render(<DashboardPage />);
    await screen.findByRole("heading", { level: 1, name: "Hi, Sam." });
    await new Promise((r) => setTimeout(r, 200));
    expect(screen.queryByText("Deleted: Week 3.")).toBeNull();
    expect(screen.getByRole("heading", { level: 1 })).not.toHaveFocus();
  });
});
