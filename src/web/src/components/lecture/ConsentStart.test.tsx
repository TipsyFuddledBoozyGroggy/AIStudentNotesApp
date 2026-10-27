import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { axeViolations } from "@/test/axe";
import { AnnouncerProvider } from "./Announcer";
import { ConsentStart } from "./ConsentStart";
import { MicBlocked } from "./MicBlocked";

const wrap = (ui: React.ReactElement) => render(<AnnouncerProvider>{ui}</AnnouncerProvider>);

describe("ConsentStart", () => {
  it("UX 1 AC1: nothing touches the microphone before Start is pressed with the box checked", async () => {
    const gum = vi.fn();
    Object.defineProperty(navigator, "mediaDevices", { value: { getUserMedia: gum }, configurable: true });
    const onStart = vi.fn();
    wrap(<ConsentStart courseName="Biology 101" onStart={onStart} onCancel={() => undefined} />);
    expect(gum).not.toHaveBeenCalled();
    expect(onStart).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole("checkbox"));
    expect(onStart).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole("button", { name: "Start recording" }));
    expect(onStart).toHaveBeenCalledTimes(1);
    expect(gum).not.toHaveBeenCalled(); // the component itself never requests the mic
  });

  it("UX 1 AC2: Start with the box unchecked only moves focus to the hint", async () => {
    const onStart = vi.fn();
    wrap(<ConsentStart courseName="Biology 101" onStart={onStart} onCancel={() => undefined} />);
    const start = screen.getByRole("button", { name: "Start recording" });
    expect(start).toHaveAttribute("aria-disabled", "true");
    expect(start).not.toBeDisabled(); // stays focusable
    await userEvent.click(start);
    expect(onStart).not.toHaveBeenCalled();
    expect(screen.getByText("Check the box above to continue")).toHaveFocus();
    expect(start).toHaveAccessibleDescription("Check the box above to continue");
  });

  it("UX 1 AC3: the DRAFT tag is present and contains 'Draft'; heading takes focus on open", () => {
    wrap(<ConsentStart courseName="Biology 101" onStart={() => undefined} onCancel={() => undefined} />);
    expect(screen.getByText("Draft")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent("Start lecture: Biology 101");
    expect(screen.getByRole("heading", { level: 1 })).toHaveFocus();
  });

  it("UX 1 AC5: DOM order is heading, what-does-this-mean, checkbox, Start, Not now; axe clean", async () => {
    const { container } = wrap(<ConsentStart courseName="Biology 101" onStart={() => undefined} onCancel={() => undefined} />);
    const order = Array.from(container.querySelectorAll("h1, summary, input, button")).map((e) => e.textContent || e.tagName);
    expect(order[0]).toContain("Start lecture");
    expect(order[1]).toBe("What does this mean?");
    expect(order[2]).toBe("INPUT");
    expect(order[3]).toBe("Start recording");
    expect(order[4]).toBe("Not now");
    expect(await axeViolations(container)).toEqual([]);
  });

  it("UX 1: consent_required focuses the checkbox and shows the message", () => {
    wrap(<ConsentStart courseName="Bio" onStart={() => undefined} onCancel={() => undefined} error="consent_required" />);
    expect(screen.getAllByText("We need your confirmation before starting.").length).toBeGreaterThan(0);
    expect(screen.getByRole("checkbox")).toHaveFocus();
  });

  it("UX 1: invalid_state shows a link; attestation starts checked and can be changed", async () => {
    const { unmount } = wrap(
      <ConsentStart courseName="Bio" onStart={() => undefined} onCancel={() => undefined} error="invalid_state" runningLectureHref="/x" />,
    );
    expect(screen.getByRole("link", { name: "Open the running lecture" })).toHaveAttribute("href", "/x");
    unmount();
    wrap(<ConsentStart courseName="Bio" onStart={() => undefined} onCancel={() => undefined} attestation={{ dateLabel: "Oct 1" }} />);
    expect(screen.getByRole("checkbox")).toBeChecked();
    expect(screen.getByText(/You confirmed this for Bio on Oct 1/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Change" }));
    expect(screen.getByRole("checkbox")).not.toBeChecked();
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
