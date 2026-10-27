import { act, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { axeViolations } from "@/test/axe";
import { FIXTURE_IDS, FIXTURES } from "./fixtures";
import { HarnessFixture } from "./HarnessFixture";

// Harness fixtures are the QA surface for every UX state: each must render and be axe-clean in jsdom.
// (Colour contrast and layout need the real-browser run in e2e-harness/.)
describe("harness fixtures", () => {
  it("has one fixture for each required state", () => {
    for (const id of ["consent", "live", "paused", "stopped", "mic-denied", "disconnected", "mismatch", "paused-mic-lost"]) {
      expect(FIXTURE_IDS).toContain(id);
    }
  });

  it.each(FIXTURE_IDS)("fixture %s renders and has no axe violations", async (id) => {
    const { container } = render(<HarnessFixture id={id} />);
    await act(async () => {
      await new Promise((r) => setTimeout(r, 400)); // let timed fixtures (mic unplug) settle
    });
    expect(FIXTURES[id].title).toBeTruthy();
    expect(screen.getByText(/Fixture:/)).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });
});
