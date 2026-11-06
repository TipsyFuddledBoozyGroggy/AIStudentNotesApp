import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { LectureSessionDto } from "@/lib/api";
import { ApiError } from "@/lib/api/errors";
import type { Syllabus } from "@/lib/syllabus/types";
import { axeViolations } from "@/test/axe";
import { CourseDelete, type CourseDeleteProps } from "./CourseDelete";
import { COURSE_DELETE_COPY as C, DELETE_PROBLEM_COPY as P } from "./deleteCopy";
import { AnnouncerProvider } from "./lecture/Announcer";

// PO-20by / docs/ux/delete-wording-draft.md 5.2a and 6: "Delete this course" on the course page. Delete only (no Hide);
// not shown while a lecture of the course is Active or Paused.

const ses = (state: LectureSessionDto["state"], id = `s-${state}-${Math.random()}`) => ({ id, state }) as LectureSessionDto;
const syl = (id: string, counts: Partial<Syllabus["counts"]> = {}) =>
  ({ id, counts: { total: 0, undecided: 0, approved: 0, skipped: 0, needsDate: 0, synced: 0, droppedInvalid: 0, unclaimedDates: 0, ...counts } }) as Syllabus;

function setup(o: { sessions?: LectureSessionDto[][]; syllabi?: Syllabus[]; del?: () => Promise<void>; listFails?: boolean } = {}) {
  const pages = o.sessions ?? [[]];
  let call = 0;
  const api = {
    listSessions: vi.fn(() => {
      if (o.listFails) return Promise.reject(new ApiError(0, "network_error", "x"));
      const r = pages[Math.min(call, pages.length - 1)]!;
      call++;
      return Promise.resolve(r);
    }),
    deleteCourse: vi.fn(o.del ?? (() => Promise.resolve())),
    getCourse: vi.fn(() => Promise.reject(new ApiError(404, "not_found", "x"))),
  };
  const syllabi = { listSyllabi: vi.fn(() => Promise.resolve(o.syllabi ?? [])) };
  const onDeleted = vi.fn();
  const statusLog: string[] = [];
  const obs = new MutationObserver(() => {
    const t = document.querySelector('[data-region="status"]')?.textContent;
    if (t && statusLog.at(-1) !== t) statusLog.push(t);
  });
  obs.observe(document.body, { childList: true, subtree: true, characterData: true });
  const props: CourseDeleteProps = { courseId: "c1", onDeleted, api: api as unknown as CourseDeleteProps["api"], syllabi: syllabi as unknown as CourseDeleteProps["syllabi"] };
  const r = render(
    <AnnouncerProvider>
      <CourseDelete {...props} />
    </AnnouncerProvider>,
  );
  return { ...r, api, syllabi, onDeleted, statusLog };
}

const openDialog = async () => {
  await userEvent.click(await screen.findByRole("button", { name: C.button }));
  return screen.findByRole("dialog", { name: C.heading });
};

describe("Delete this course", () => {
  it("a 'Your data' section with one tertiary button, name equal to its visible text; no Hide control", async () => {
    setup();
    const btn = await screen.findByRole("button", { name: C.button });
    expect(btn).toHaveClass("btn-tertiary");
    expect(screen.getByRole("region", { name: C.section })).toContainElement(btn);
    expect(screen.queryByRole("button", { name: /hide/i })).toBeNull();
  });

  it.each(["Active", "Paused"] as const)("while a lecture is %s the button is not shown; one plain line says why", async (state) => {
    setup({ sessions: [[ses("Stopped"), ses(state)]] });
    expect(await screen.findByText(C.blocked)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: C.button })).toBeNull();
  });

  it("the dialog: counts from fresh reads, draft wording as a real list, Keep it focused, nothing sent on Keep; no axe violations", async () => {
    const { api, container } = setup({ sessions: [[ses("Stopped"), ses("Stopped"), ses("Failed")]], syllabi: [syl("y1", { approved: 2, synced: 0 }), syl("y2")] });
    const dialog = await openDialog();
    const items = within(dialog).getAllByRole("listitem").map((li) => li.textContent);
    expect(items).toEqual([C.lectures(3), C.syllabi(2), C.demoCalendar]);
    expect(items[0]).toBe("3 lectures: transcripts, summaries, recaps and notes");
    expect(items[1]).toBe("2 syllabi: the text, the items and your decisions");
    expect(dialog).toHaveAccessibleDescription(`${C.body} ${items.join(" ")} ${C.cannot} ${C.kept}`);
    expect(within(dialog).getByRole("button", { name: C.keep })).toHaveFocus();
    expect(within(dialog).getByRole("button", { name: C.keep })).toHaveClass("btn-primary");
    expect(within(dialog).getByRole("button", { name: C.confirm })).toHaveClass("btn-secondary");
    expect(await axeViolations(document.body)).toEqual([]);
    await userEvent.keyboard("{Escape}");
    await waitFor(() => expect(screen.getByRole("button", { name: C.button })).toHaveFocus());
    expect(api.deleteCourse).not.toHaveBeenCalled();
    expect(api.listSessions).toHaveBeenCalledTimes(2); // on load, and again on the press
    expect(await axeViolations(container)).toEqual([]);
  });

  it("singular forms; the demo-calendar line only when a syllabus has approved items", async () => {
    setup({ sessions: [[ses("Stopped")]], syllabi: [syl("y1", { total: 4 })] });
    const dialog = await openDialog();
    expect(within(dialog).getAllByRole("listitem").map((li) => li.textContent)).toEqual([
      "1 lecture: transcript, summary, recaps and notes",
      "1 syllabus: the text, the items and your decisions",
    ]);
  });

  it("no lectures and no syllabi: no empty list, the short body", async () => {
    setup();
    const dialog = await openDialog();
    expect(within(dialog).queryByRole("list")).toBeNull();
    expect(dialog).toHaveAccessibleDescription(`${C.bodyEmpty} ${C.cannot} ${C.kept}`);
  });

  it("a lecture started in another tab before the press: no dialog; the reason replaces the button and takes focus, said once", async () => {
    const r = setup({ sessions: [[], [ses("Active")]] });
    await userEvent.click(await screen.findByRole("button", { name: C.button }));
    const line = await within(screen.getByRole("region", { name: C.section })).findByText(C.blocked);
    expect(screen.queryByRole("dialog")).toBeNull();
    await waitFor(() => expect(line).toHaveFocus());
    expect(r.statusLog).toEqual([C.blocked]);
  });

  it("a lecture started after the dialog opened: checked again before sending; nothing is deleted", async () => {
    const r = setup({ sessions: [[], [], [ses("Paused")]] });
    await openDialog();
    await userEvent.click(screen.getByRole("button", { name: C.confirm }));
    expect(await within(screen.getByRole("region", { name: C.section })).findByText(C.blocked)).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(r.api.deleteCourse).not.toHaveBeenCalled();
  });

  it("confirm: one purge DELETE, busy and double-press safe, then onDeleted; the deleted syllabi's drafts on this device go too", async () => {
    let resolve!: () => void;
    window.localStorage.setItem("tether.syllabus.u.x.edit.y1.item1", "{}");
    window.localStorage.setItem("tether.syllabus.u.x.draft.c1", "{}");
    window.localStorage.setItem("tether.syllabus.u.x.edit.other.item1", "{}");
    const r = setup({ syllabi: [syl("y1")], del: () => new Promise<void>((res) => (resolve = res)) });
    await openDialog();
    const confirm = screen.getByRole("button", { name: C.confirm });
    await userEvent.click(confirm);
    await waitFor(() => expect(confirm).toHaveAttribute("aria-busy", "true"));
    expect(confirm).toHaveTextContent(C.busy);
    await userEvent.click(confirm);
    expect(r.onDeleted).not.toHaveBeenCalled();
    resolve();
    await waitFor(() => expect(r.onDeleted).toHaveBeenCalledWith("deleted"));
    expect(r.api.deleteCourse).toHaveBeenCalledTimes(1);
    expect(r.api.deleteCourse).toHaveBeenCalledWith("c1");
    expect(window.localStorage.getItem("tether.syllabus.u.x.edit.y1.item1")).toBeNull();
    expect(window.localStorage.getItem("tether.syllabus.u.x.draft.c1")).toBeNull();
    expect(window.localStorage.getItem("tether.syllabus.u.x.edit.other.item1")).toBe("{}");
  });

  it.each([
    [409, P.conflict],
    [429, P.rate],
  ])("%i: the dialog stays open with one line, said once; focus stays on the confirm button", async (status, text) => {
    const r = setup({ del: () => Promise.reject(new ApiError(status, "x", "server detail")) });
    const dialog = await openDialog();
    const confirm = within(dialog).getByRole("button", { name: C.confirm });
    await userEvent.click(confirm);
    expect(await within(dialog).findByText(text)).toBeInTheDocument();
    expect(confirm).toHaveFocus();
    expect(r.statusLog).toEqual([text]);
    expect(r.onDeleted).not.toHaveBeenCalled();
  });

  it("the lectures cannot be read on the press: no dialog, the network line under the button", async () => {
    setup({ listFails: true });
    await userEvent.click(await screen.findByRole("button", { name: C.button }));
    expect(await within(screen.getByRole("region", { name: C.section })).findByText(P.network)).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).toBeNull();
  });
});
