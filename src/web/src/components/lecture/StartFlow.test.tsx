import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { axeViolations } from "@/test/axe";
import { baseEvents, SID } from "./testUtils";
import { createHarnessRuntime } from "./harnessRuntime";
import { FakeMicSource } from "./micSource";
import type { LectureBackend, StartError } from "./ports";
import { StartFlow } from "./StartFlow";
import { CONSENT_TEXT_VERSION } from "./copy";

function setup(micOutcome: "ok" | "denied" | "no_device" = "ok", startError?: StartError, failStop = false) {
  const { runtime } = createHarnessRuntime({ sessionId: SID, events: baseEvents() });
  const stop = vi.fn(async () => {
    if (failStop) throw new Error("x");
  });
  runtime.actions.stop = stop;
  const startSession = vi.fn<LectureBackend["startSession"]>(async () =>
    startError ? { ok: false, error: startError } : { ok: true, runtime },
  );
  const mic = new FakeMicSource(micOutcome);
  const onBack = vi.fn();
  const ui = render(<StartFlow courseId="c1" courseName="Biology 101" backend={{ startSession }} mic={mic} onBackToCourse={onBack} />);
  return { ...ui, startSession, mic, stop, onBack };
}

const checkAndStart = async () => {
  await userEvent.click(screen.getByRole("checkbox"));
  await userEvent.click(screen.getByRole("button", { name: "Start recording" }));
};

describe("StartFlow", () => {
  it("UX 1 AC1/AC4: no session or mic before the press; the press posts consent with the registry textVersion, then the mic", async () => {
    const t = setup();
    expect(t.startSession).not.toHaveBeenCalled();
    expect(t.mic.requestCount).toBe(0);
    await userEvent.click(screen.getByRole("checkbox"));
    expect(t.mic.requestCount).toBe(0);
    await userEvent.click(screen.getByRole("button", { name: "Start recording" }));
    await waitFor(() => expect(screen.getByTestId("state-label")).toBeInTheDocument());
    expect(t.startSession).toHaveBeenCalledWith({ courseId: "c1", consent: { acknowledged: true, textVersion: CONSENT_TEXT_VERSION } });
    expect(t.mic.requestCount).toBe(1);
  });

  it("UX 1: on success focus moves to the status-bar heading and 'Recording started.' is announced", async () => {
    setup();
    await checkAndStart();
    await waitFor(() => expect(screen.getByRole("heading", { name: "Recording" })).toHaveFocus());
    await waitFor(() => expect(document.querySelector('[data-region="status"]')).toHaveTextContent("Recording started."));
    expect(screen.getByTestId("state-label")).toHaveTextContent("Recording");
  });

  it("UX 5 AC1-AC3: denied mic shows the blocked screen after stopping the session, focus on heading, no loop; Try again re-asks on press only", async () => {
    const t = setup("denied");
    await checkAndStart();
    const h = await screen.findByRole("heading", { level: 1, name: "Microphone is blocked" });
    expect(h).toHaveFocus();
    expect(t.stop).toHaveBeenCalledTimes(1);
    expect(screen.getByText(/Nothing has been recorded/)).toBeInTheDocument();
    expect(screen.queryByTestId("state-label")).toBeNull(); // no live view / transcript for a mic that never opened
    expect(t.mic.requestCount).toBe(1);
    await new Promise((r) => setTimeout(r, 50));
    expect(t.mic.requestCount).toBe(1);
    await userEvent.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(t.mic.requestCount).toBe(2));
    expect(await axeViolations(document.body)).toEqual([]);
  });

  it("UX 5: if the stop cannot be confirmed the screen does not claim nothing was recorded", async () => {
    setup("denied", undefined, true);
    await checkAndStart();
    await screen.findByRole("heading", { name: "Microphone is blocked" });
    expect(screen.queryByText(/Nothing has been recorded/)).toBeNull();
    expect(screen.getByText(/could not confirm/)).toBeInTheDocument();
  });

  it("UX 1: 403 consent_required returns to the form with the message and focus on the checkbox; no mic was asked", async () => {
    const t = setup("ok", "consent_required");
    await checkAndStart();
    await waitFor(() => expect(screen.getByRole("checkbox")).toHaveFocus());
    expect(screen.getAllByText("We need your confirmation before starting.").length).toBeGreaterThan(0);
    expect(t.mic.requestCount).toBe(0);
  });

  it("UX 1: network error keeps the form state (box still checked) and offers retry", async () => {
    setup("ok", "network");
    await checkAndStart();
    await screen.findAllByText(/could not reach the server/);
    expect(screen.getByRole("checkbox")).toBeChecked();
  });

  it("Not now calls back without starting anything", async () => {
    const t = setup();
    await userEvent.click(screen.getByRole("button", { name: "Not now" }));
    expect(t.onBack).toHaveBeenCalled();
    expect(t.startSession).not.toHaveBeenCalled();
  });
});
