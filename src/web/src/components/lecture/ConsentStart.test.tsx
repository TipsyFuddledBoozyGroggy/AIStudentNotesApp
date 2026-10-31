import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { axeViolations } from "@/test/axe";
import { AnnouncerProvider } from "./Announcer";
import { ConsentStart } from "./ConsentStart";
import { MicBlocked } from "./MicBlocked";

const wrap = (ui: React.ReactElement) => render(<AnnouncerProvider>{ui}</AnnouncerProvider>);

const dialog = () => screen.getByRole("dialog", { name: "Did your teacher say you can record?" });
const openDialog = async () => userEvent.click(screen.getByRole("button", { name: "Start recording" }));

describe("ConsentStart", () => {
  it("UX 1 AC1: nothing touches the microphone or starts until the dialog box is ticked and confirmed", async () => {
    const gum = vi.fn();
    Object.defineProperty(navigator, "mediaDevices", { value: { getUserMedia: gum }, configurable: true });
    const onStart = vi.fn();
    wrap(<ConsentStart courseName="Biology 101" onStart={onStart} onCancel={() => undefined} />);
    expect(screen.queryByRole("dialog")).toBeNull();
    await openDialog();
    expect(onStart).not.toHaveBeenCalled();
    const box = within(dialog()).getByRole("checkbox", { name: "My teacher said I can record this class." });
    expect(box).not.toBeChecked(); // never pre-checked
    await userEvent.click(box);
    expect(onStart).not.toHaveBeenCalled();
    await userEvent.click(within(dialog()).getByRole("button", { name: "Confirm and start recording" }));
    expect(onStart).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(gum).not.toHaveBeenCalled(); // the component itself never requests the mic
  });

  it("UX 1 AC2: confirm with the box unticked only moves focus to the hint", async () => {
    const onStart = vi.fn();
    wrap(<ConsentStart courseName="Biology 101" onStart={onStart} onCancel={() => undefined} />);
    await openDialog();
    const confirm = within(dialog()).getByRole("button", { name: "Confirm and start recording" });
    expect(confirm).toHaveAttribute("aria-disabled", "true");
    expect(confirm).not.toBeDisabled(); // stays focusable
    await userEvent.click(confirm);
    expect(onStart).not.toHaveBeenCalled();
    expect(screen.getByText("Check the box above to continue.")).toHaveFocus();
    expect(confirm).toHaveAccessibleDescription("Check the box above to continue.");
    expect(dialog()).toBeInTheDocument();
  });

  it("UX 1 AC3: the DRAFT tag is present; heading takes focus on open; Start recording announces a dialog", () => {
    wrap(<ConsentStart courseName="Biology 101" onStart={() => undefined} onCancel={() => undefined} />);
    expect(screen.getByText("Draft")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent("Start lecture: Biology 101");
    expect(screen.getByRole("heading", { level: 1 })).toHaveFocus();
    expect(screen.getByRole("button", { name: "Start recording" })).toHaveAttribute("aria-haspopup", "dialog");
    expect(screen.queryByRole("checkbox")).toBeNull(); // the attestation lives only in the dialog
  });

  it("UX 1 AC5: DOM order is heading, what-does-this-mean, Start, Not now; axe clean on page and open dialog", async () => {
    const { container } = wrap(<ConsentStart courseName="Biology 101" onStart={() => undefined} onCancel={() => undefined} />);
    const order = Array.from(container.querySelectorAll("h1, summary, input, button")).map((e) => e.textContent || e.tagName);
    expect(order).toEqual(["Start lecture: Biology 101", "What does this mean?", "Start recording", "Not now"]);
    expect(await axeViolations(container)).toEqual([]);
    await openDialog();
    expect(await axeViolations(document.body)).toEqual([]);
  });

  it("dialog: modal semantics, labelled and described; focus starts on the checkbox; the page behind is inert", async () => {
    const { container } = wrap(<ConsentStart courseName="Biology 101" onStart={() => undefined} onCancel={() => undefined} />);
    await openDialog();
    const d = dialog();
    expect(d).toHaveAttribute("aria-modal", "true");
    expect(d).toHaveAccessibleDescription("Tether turns on the microphone only after you confirm this for Biology 101.");
    expect(within(d).getByRole("checkbox")).toHaveFocus();
    expect(container).toHaveAttribute("inert"); // RTL's container is a direct child of <body>
    expect(d.closest("[inert]")).toBeNull();
    await userEvent.click(within(d).getByRole("button", { name: "Cancel" }));
    expect(container).not.toHaveAttribute("inert");
  });

  it("dialog: Tab and Shift+Tab stay inside", async () => {
    wrap(<ConsentStart courseName="Bio" onStart={() => undefined} onCancel={() => undefined} />);
    await openDialog();
    const d = dialog();
    const box = within(d).getByRole("checkbox");
    const confirm = within(d).getByRole("button", { name: "Confirm and start recording" });
    const cancel = within(d).getByRole("button", { name: "Cancel" });
    expect(box).toHaveFocus();
    await userEvent.tab();
    expect(confirm).toHaveFocus();
    await userEvent.tab();
    expect(cancel).toHaveFocus();
    await userEvent.tab();
    expect(box).toHaveFocus();
    await userEvent.tab({ shift: true });
    expect(cancel).toHaveFocus();
  });

  it("dialog: Escape cancels, starts nothing, returns focus to Start recording; reopening starts unchecked", async () => {
    const onStart = vi.fn();
    const onCancel = vi.fn();
    wrap(<ConsentStart courseName="Bio" onStart={onStart} onCancel={onCancel} />);
    await openDialog();
    await userEvent.click(within(dialog()).getByRole("checkbox"));
    await userEvent.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(onStart).not.toHaveBeenCalled();
    expect(onCancel).not.toHaveBeenCalled(); // Escape closes the dialog only; the student stays on the page
    await waitFor(() => expect(screen.getByRole("button", { name: "Start recording" })).toHaveFocus());
    await openDialog();
    expect(within(dialog()).getByRole("checkbox")).not.toBeChecked();
  });

  it("dialog: Cancel starts nothing and returns focus to Start recording", async () => {
    const onStart = vi.fn();
    wrap(<ConsentStart courseName="Bio" onStart={onStart} onCancel={() => undefined} />);
    await openDialog();
    await userEvent.click(within(dialog()).getByRole("button", { name: "Cancel" }));
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(onStart).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.getByRole("button", { name: "Start recording" })).toHaveFocus());
  });

  it("dialog: no auto-dismiss; it stays open with no action", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      wrap(<ConsentStart courseName="Bio" onStart={() => undefined} onCancel={() => undefined} />);
      await openDialog();
      vi.advanceTimersByTime(10 * 60_000);
      expect(dialog()).toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it("busy: Start recording does not open the dialog", async () => {
    wrap(<ConsentStart courseName="Bio" onStart={() => undefined} onCancel={() => undefined} busy />);
    const start = screen.getByRole("button", { name: "Starting..." });
    expect(start).toHaveAttribute("aria-disabled", "true");
    await userEvent.click(start);
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("UX 1: consent_required shows the message and focuses Start recording", () => {
    wrap(<ConsentStart courseName="Bio" onStart={() => undefined} onCancel={() => undefined} error="consent_required" />);
    expect(screen.getAllByText("We need your confirmation before starting.").length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Start recording" })).toHaveFocus();
  });

  it("UX 1: invalid_state shows a link; an earlier attestation is information only and the dialog box still starts unchecked", async () => {
    const { unmount } = wrap(
      <ConsentStart courseName="Bio" onStart={() => undefined} onCancel={() => undefined} error="invalid_state" runningLectureHref="/x" />,
    );
    expect(screen.getByRole("link", { name: "Open the running lecture" })).toHaveAttribute("href", "/x");
    unmount();
    wrap(<ConsentStart courseName="Bio" onStart={() => undefined} onCancel={() => undefined} attestation={{ dateLabel: "Oct 1" }} />);
    expect(screen.getByText(/You confirmed this for Bio on Oct 1/)).toBeInTheDocument();
    await openDialog();
    expect(within(dialog()).getByRole("checkbox")).not.toBeChecked();
  });
});

describe("MicBlocked", () => {
  it("UX 5 AC1: shows blocked screen with Try again, back; no dead end; Try again is the first control; axe clean", async () => {
    const onTry = vi.fn();
    const { container } = wrap(<MicBlocked reason="denied" nothingRecordedConfirmed onTryAgain={onTry} onBack={() => undefined} />);
    expect(screen.getByRole("heading", { level: 1, name: "Microphone is blocked" })).toHaveFocus();
    expect(screen.getByText(/Nothing has been recorded/)).toBeInTheDocument();
    expect(container.querySelector("button")).toHaveTextContent("Try again");
    await userEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(onTry).toHaveBeenCalledTimes(1);
    expect(await axeViolations(container)).toEqual([]);
  });

  it("UX 5: never claims 'nothing recorded' unless the server confirmed the stop", () => {
    wrap(<MicBlocked reason="denied" nothingRecordedConfirmed={false} onTryAgain={() => undefined} onBack={() => undefined} />);
    expect(screen.queryByText(/Nothing has been recorded/)).toBeNull();
    expect(screen.getByText(/could not confirm/)).toBeInTheDocument();
  });

  it("UX 5 AC2: rendering does not call getUserMedia (no auto retry)", () => {
    const gum = vi.fn();
    Object.defineProperty(navigator, "mediaDevices", { value: { getUserMedia: gum }, configurable: true });
    wrap(<MicBlocked reason="no_device" nothingRecordedConfirmed onTryAgain={() => undefined} onBack={() => undefined} />);
    expect(gum).not.toHaveBeenCalled();
    expect(screen.getByText(/No microphone was found/)).toBeInTheDocument();
  });
});
