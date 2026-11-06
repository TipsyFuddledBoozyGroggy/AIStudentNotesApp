import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { StrictMode } from "react";
import { LECTURE_DELETE_COPY as C } from "../deleteCopy";
import { takeDeleted } from "../deleteFlow";
import { baseEvents, SID } from "./testUtils";

// PO-20by: the lecture's own page wires "Delete this lecture" to DELETE /sessions/{id}, writes the one-shot note for the
// course page only after the server answered, and moves there. Route, auth, API and runtime are stand-ins (no network).

const push = vi.fn();
const deleteSession = vi.fn<(id: string) => Promise<void>>();
let title: string | null = "Week 3";
vi.mock("next/navigation", () => ({ useParams: () => ({ sessionId: SID }), useRouter: () => ({ replace: vi.fn(), push }) }));
vi.mock("../AuthProvider", () => ({ useRequireUser: () => ({ status: "authenticated", user: { id: "u1" } }) }));
vi.mock("@/lib/api", async (orig) => {
  const real = await orig<typeof import("@/lib/api")>();
  return {
    ...real,
    api: {
      getSession: vi.fn(async () => ({ id: SID, courseId: "c1", title, state: "Stopped" })),
      deleteSession: (id: string) => deleteSession(id),
    },
  };
});
vi.mock("./realRuntime", async () => {
  const { createHarnessRuntime } = await import("./harnessRuntime");
  return { createRealRuntime: () => createHarnessRuntime({ sessionId: SID, events: baseEvents("Stopped", "user") }).runtime };
});

import { LectureReopenPage } from "./LectureReopenPage";

beforeEach(() => {
  window.sessionStorage.clear();
  push.mockReset();
  deleteSession.mockReset();
  title = "Week 3";
});

async function deleteIt() {
  render(
    <StrictMode>
      <LectureReopenPage />
    </StrictMode>,
  );
  await userEvent.click(await screen.findByRole("button", { name: C.button }));
  await userEvent.click(screen.getByRole("button", { name: C.confirm }));
}

it("Delete: one DELETE for this lecture, then the note for its course page, then the move", async () => {
  let resolve!: () => void;
  deleteSession.mockImplementation(() => new Promise<void>((r) => (resolve = r)));
  await deleteIt();
  expect(deleteSession).toHaveBeenCalledWith(SID);
  expect(push).not.toHaveBeenCalled(); // never before the 204
  expect(window.sessionStorage.length).toBe(0);
  resolve();
  await waitFor(() => expect(push).toHaveBeenCalledWith("/courses/c1"));
  expect(deleteSession).toHaveBeenCalledTimes(1);
  expect(takeDeleted({ kind: "lecture", courseId: "c1" })).toEqual({ kind: "lecture", courseId: "c1", note: "Deleted: Week 3.", announce: C.done });
});

it("an untitled lecture: the note is the plain sentence", async () => {
  title = null;
  deleteSession.mockResolvedValue(undefined);
  await deleteIt();
  await waitFor(() => expect(push).toHaveBeenCalled());
  expect(takeDeleted({ kind: "lecture", courseId: "c1" })?.note).toBe(C.done);
});
