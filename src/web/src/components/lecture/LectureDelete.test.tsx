import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError } from "@/lib/api/errors";
import { PREFS_KEY } from "@/lib/design/prefs";
import { axeViolations } from "@/test/axe";
import { DELETE_PROBLEM_COPY as P, LECTURE_DELETE_COPY as C } from "../deleteCopy";
import { PrefsProvider } from "../PrefsProvider";
import { AnnouncerProvider } from "./Announcer";
import { createHarnessRuntime } from "./harnessRuntime";
import { LectureScreen } from "./LectureScreen";
import type { LectureDeletion } from "./LectureDelete";
import { FakeMicSource } from "./micSource";
import { baseEvents, flush, mountLecture, SID } from "./testUtils";

// PO-20by / docs/ux/delete-wording-draft.md 5.1 and 6: "Delete this lecture" on the stopped lecture's page.

function deletion(over: Partial<NonNullable<LectureDeletion["api"]>> = {}) {
  const api = {
    deleteSession: vi.fn(() => Promise.resolve()),
    getSession: vi.fn(() => Promise.reject(new ApiError(404, "not_found", "x"))),
    ...over,
  };
  const onDeleted = vi.fn();
  return { api, onDeleted, props: { sessionId: SID, onDeleted, api } as LectureDeletion };
}

const stopped = { events: baseEvents("Stopped", "user"), track: "ended" as const };

describe("Delete this lecture", () => {
  it("is in the review toolbar of a stopped lecture, as a tertiary button whose name is its visible text", async () => {
    const d = deletion();
    await mountLecture({ ...stopped, route: { deletion: d.props } });
    const toolbar = screen.getByRole("region", { name: "Review this lecture" });
    const btn = within(toolbar).getByRole("button", { name: C.button });
    expect(btn).toHaveClass("btn-tertiary");
    expect(btn).toHaveTextContent(C.button);
  });

  it.each([
    ["Active", { events: baseEvents("Active"), track: "live" as const }],
    ["Paused", { events: baseEvents("Paused", "user"), track: "ended" as const }],
  ])("is not shown while the lecture is %s (Stop is the one action then)", async (_s, o) => {
    await mountLecture({ ...o, route: { deletion: deletion().props } });
    expect(screen.queryByRole("button", { name: C.button })).toBeNull();
  });

  it("is not shown without the deletion prop (the live route and the harness)", async () => {
    await mountLecture(stopped);
    expect(screen.queryByRole("button", { name: C.button })).toBeNull();
  });

  it("is hidden in focus mode", async () => {
    window.localStorage.setItem(PREFS_KEY, JSON.stringify({ focusMode: true }));
    const { runtime, controls } = createHarnessRuntime({ sessionId: SID, events: stopped.events });
    render(
      <PrefsProvider>
        <AnnouncerProvider>
          <LectureScreen runtime={runtime} track={null} mic={new FakeMicSource("ok")} onBackToCourse={() => {}} initialLecture={controls.initialLecture} settingsOverride={{}} deletion={deletion().props} />
        </AnnouncerProvider>
      </PrefsProvider>,
    );
    await flush();
    await waitFor(() => expect(document.querySelector("[data-layout]")).toHaveAttribute("data-layout", "focus"));
    expect(screen.queryByRole("button", { name: C.button })).toBeNull();
  });

  it("the dialog: draft wording, Keep it filled and focused, Escape keeps and returns focus, nothing is sent; no axe violations", async () => {
    const d = deletion();
    const m = await mountLecture({ ...stopped, route: { deletion: d.props } });
    const opener = screen.getByRole("button", { name: C.button });
    await userEvent.click(opener);
    const dialog = screen.getByRole("dialog", { name: C.heading });
    expect(dialog).toHaveAccessibleDescription(`${C.body} ${C.cannot} ${C.kept}`);
    const keep = within(dialog).getByRole("button", { name: C.keep });
    expect(keep).toHaveClass("btn-primary");
    expect(keep).toHaveFocus();
    expect(within(dialog).getByRole("button", { name: C.confirm })).toHaveClass("btn-secondary");
    expect(within(dialog).queryByRole("textbox")).toBeNull(); // no typing to confirm (draft section 4 rule 6)
    expect(await axeViolations(document.body)).toEqual([]);
    await userEvent.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).toBeNull();
    await waitFor(() => expect(opener).toHaveFocus());
    await userEvent.click(opener);
    await userEvent.click(screen.getByRole("button", { name: C.keep }));
    await waitFor(() => expect(opener).toHaveFocus());
    expect(d.api.deleteSession).not.toHaveBeenCalled();
    expect(m.statusLog).toEqual([]);
  });

  it("Tab stays inside the dialog", async () => {
    await mountLecture({ ...stopped, route: { deletion: deletion().props } });
    await userEvent.click(screen.getByRole("button", { name: C.button }));
    const dialog = screen.getByRole("dialog");
    await userEvent.tab();
    expect(within(dialog).getByRole("button", { name: C.confirm })).toHaveFocus();
    await userEvent.tab();
    expect(within(dialog).getByRole("button", { name: C.keep })).toHaveFocus();
  });

  it("confirm: one DELETE, busy and double-press safe, then onDeleted('deleted') (the page moves; nothing is announced here)", async () => {
    let resolve!: () => void;
    const d = deletion({ deleteSession: vi.fn(() => new Promise<void>((r) => (resolve = r))) });
    const m = await mountLecture({ ...stopped, route: { deletion: d.props } });
    await userEvent.click(screen.getByRole("button", { name: C.button }));
    const confirm = screen.getByRole("button", { name: C.confirm });
    await userEvent.click(confirm);
    expect(confirm).toHaveTextContent(C.busy);
    expect(confirm).toHaveAttribute("aria-busy", "true");
    expect(confirm).toHaveAttribute("aria-disabled", "true");
    await userEvent.click(confirm);
    await userEvent.keyboard("{Escape}"); // Keep is inert while the request runs
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(d.api.deleteSession).toHaveBeenCalledTimes(1);
    expect(d.api.deleteSession).toHaveBeenCalledWith(SID);
    expect(d.onDeleted).not.toHaveBeenCalled(); // never before the server answered
    resolve();
    await waitFor(() => expect(d.onDeleted).toHaveBeenCalledWith("deleted"));
    expect(m.statusLog).toEqual([]);
  });

  it("404: already gone, treated as deleted", async () => {
    const d = deletion({ deleteSession: vi.fn(() => Promise.reject(new ApiError(404, "not_found", "x"))) });
    await mountLecture({ ...stopped, route: { deletion: d.props } });
    await userEvent.click(screen.getByRole("button", { name: C.button }));
    await userEvent.click(screen.getByRole("button", { name: C.confirm }));
    await waitFor(() => expect(d.onDeleted).toHaveBeenCalledWith("gone"));
  });

  it.each([
    [409, P.conflict],
    [429, P.rate],
    [401, P.session],
  ])("%i: the dialog stays open with one line in its description, said once, focus moves to that line (S3); a retry works", async (status, text) => {
    const del = vi.fn().mockRejectedValueOnce(new ApiError(status, "x", "server detail")).mockResolvedValueOnce(undefined);
    const d = deletion({ deleteSession: del });
    const m = await mountLecture({ ...stopped, route: { deletion: d.props } });
    await userEvent.click(screen.getByRole("button", { name: C.button }));
    const confirm = screen.getByRole("button", { name: C.confirm });
    await userEvent.click(confirm);
    const dialog = await screen.findByRole("dialog");
    await waitFor(() => expect(dialog).toHaveAccessibleDescription(new RegExp(`${text.replace(/[.?]/g, "\\$&")}$`)));
    expect(within(dialog).queryByText(/server detail/)).toBeNull();
    // UX review 7.3 (S3) amends delete-wording-draft 6.3: the problem line takes focus; Tab and Shift+Tab stay inside.
    const line = within(dialog).getByText(text);
    await waitFor(() => expect(line).toHaveFocus());
    await userEvent.tab();
    expect(within(dialog).getByRole("button", { name: C.keep })).toHaveFocus();
    line.focus();
    await userEvent.tab({ shift: true });
    expect(confirm).toHaveFocus();
    expect(confirm).toHaveTextContent(C.confirm);
    expect(m.statusLog).toEqual([text]);
    expect(d.onDeleted).not.toHaveBeenCalled();
    await userEvent.click(confirm);
    await waitFor(() => expect(d.onDeleted).toHaveBeenCalledWith("deleted"));
  });

  it("network failure but the lecture is gone when read back: deleted, not 'Nothing was deleted'", async () => {
    const d = deletion({ deleteSession: vi.fn(() => Promise.reject(new ApiError(0, "network_error", "x"))) });
    await mountLecture({ ...stopped, route: { deletion: d.props } });
    await userEvent.click(screen.getByRole("button", { name: C.button }));
    await userEvent.click(screen.getByRole("button", { name: C.confirm }));
    await waitFor(() => expect(d.onDeleted).toHaveBeenCalledWith("gone"));
    expect(d.api.getSession).toHaveBeenCalledWith(SID);
  });

  it("network failure and the lecture is still there: the network line", async () => {
    const d = deletion({
      deleteSession: vi.fn(() => Promise.reject(new ApiError(0, "network_error", "x"))),
      getSession: vi.fn(() => Promise.resolve({} as never)),
    });
    await mountLecture({ ...stopped, route: { deletion: d.props } });
    await userEvent.click(screen.getByRole("button", { name: C.button }));
    await userEvent.click(screen.getByRole("button", { name: C.confirm }));
    expect(await within(screen.getByRole("dialog")).findByText(P.network)).toBeInTheDocument();
    expect(d.onDeleted).not.toHaveBeenCalled();
  });

  it("adds no live region to the lecture screen (still exactly the announcer's two plus the finals region)", async () => {
    await mountLecture({ ...stopped, route: { deletion: deletion().props } });
    await userEvent.click(screen.getByRole("button", { name: C.button }));
    const live = Array.from(document.querySelectorAll('[role="status"],[role="alert"],[aria-live]')).filter((e) => e.getAttribute("aria-live") !== "off");
    expect(live.map((e) => e.getAttribute("data-region"))).toEqual(["status", "alert"]);
  });
});
