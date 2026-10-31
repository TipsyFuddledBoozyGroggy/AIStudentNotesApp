import { render, screen, waitFor } from "@testing-library/react";
import { StrictMode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ANNOUNCE } from "./copy";
import { ARRIVED_FROM_STOP_KEY, markArrivedFromStop } from "./settings";
import { baseEvents, SID } from "./testUtils";

// PO-19 (stop-redirect.md section 3 "Arrival", criterion 6): LectureReopenPage reads and removes the one-shot marker on
// mount, so the move's arrival focuses the Stopped heading and is announced once; a reload (no marker) does neither.
// Rendered under StrictMode (as `next dev` does), so the marker's double read is covered. The route, auth, API and the
// real runtime are replaced by stand-ins: the harness runtime with a Stopped session (synthetic data, no network).

vi.mock("next/navigation", () => ({ useParams: () => ({ sessionId: SID }), useRouter: () => ({ replace: vi.fn(), push: vi.fn() }) }));
vi.mock("../AuthProvider", () => ({ useRequireUser: () => ({ status: "authenticated", user: { id: "u1" } }) }));
vi.mock("@/lib/api", async (orig) => {
  const real = await orig<typeof import("@/lib/api")>();
  return {
    ...real,
    api: { getSession: vi.fn(async () => ({ id: SID, courseId: "c1", title: "Biology 101", state: "Stopped" })) },
  };
});
vi.mock("./realRuntime", async () => {
  const { createHarnessRuntime } = await import("./harnessRuntime");
  return { createRealRuntime: () => createHarnessRuntime({ sessionId: SID, events: baseEvents("Stopped", "user") }).runtime };
});

import { LectureReopenPage } from "./LectureReopenPage";

function mount() {
  const statusLog: string[] = [];
  const obs = new MutationObserver(() => {
    const el = document.querySelector('[data-region="status"]');
    if (el?.textContent && statusLog.at(-1) !== el.textContent) statusLog.push(el.textContent);
  });
  obs.observe(document.body, { childList: true, subtree: true, characterData: true });
  render(
    <StrictMode>
      <LectureReopenPage />
    </StrictMode>,
  );
  return { statusLog, stop: () => obs.disconnect() };
}

beforeEach(() => window.sessionStorage.clear());
afterEach(() => window.sessionStorage.clear());

describe("LectureReopenPage and the arrival marker (PO-19)", () => {
  it("with the marker for this session: removed on mount, focus on #stopped-h2 once Stopped, one announcement", async () => {
    markArrivedFromStop(SID);
    const m = mount();
    await waitFor(() => expect(screen.getByTestId("state-label")).toHaveTextContent("Stopped"));
    expect(window.sessionStorage.getItem(ARRIVED_FROM_STOP_KEY)).toBeNull();
    await waitFor(() => expect(document.getElementById("stopped-h2")).toHaveFocus());
    await waitFor(() => expect(m.statusLog).toContain(ANNOUNCE.movedAfterStop));
    await new Promise((r) => setTimeout(r, 300));
    expect(m.statusLog.filter((t) => t === ANNOUNCE.movedAfterStop)).toHaveLength(1);
    // The reopen page never links to itself.
    expect(screen.queryByRole("link", { name: "Open this lecture's page" })).toBeNull();
    m.stop();
  });

  it("a reload (no marker): Stopped is shown, no announcement, no focus move", async () => {
    const m = mount();
    await waitFor(() => expect(screen.getByTestId("state-label")).toHaveTextContent("Stopped"));
    await new Promise((r) => setTimeout(r, 300));
    expect(m.statusLog).not.toContain(ANNOUNCE.movedAfterStop);
    expect(document.getElementById("stopped-h2")).not.toHaveFocus();
    expect(screen.queryByRole("dialog")).toBeNull();
    m.stop();
  });

  it("a marker for another session is removed and ignored", async () => {
    markArrivedFromStop("ses_other");
    const m = mount();
    await waitFor(() => expect(screen.getByTestId("state-label")).toHaveTextContent("Stopped"));
    expect(window.sessionStorage.getItem(ARRIVED_FROM_STOP_KEY)).toBeNull();
    await new Promise((r) => setTimeout(r, 300));
    expect(m.statusLog).not.toContain(ANNOUNCE.movedAfterStop);
    expect(document.getElementById("stopped-h2")).not.toHaveFocus();
    m.stop();
  });
});
