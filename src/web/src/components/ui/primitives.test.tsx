import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { collectStrings, FORBIDDEN, sentencesOver } from "@/lib/copyLint";
import { axeViolations } from "@/test/axe";
import { Chevron, Disclosure } from "./Disclosure";
import { Segmented } from "./Segmented";
import { Switch } from "./Switch";
import { UI_KIT_STRINGS, UiKit } from "./UiKit";

describe("Switch", () => {
  it("is a named switch whose state is also a visible word", async () => {
    function S() {
      const [on, setOn] = useState(false);
      return <Switch label="Reduce motion" hint="Turns off all movement." checked={on} onChange={setOn} />;
    }
    render(<S />);
    const sw = screen.getByRole("switch", { name: "Reduce motion" });
    expect(sw).toHaveAttribute("aria-checked", "false");
    expect(sw).toHaveAccessibleDescription("Turns off all movement.");
    expect(sw).toHaveTextContent("Off");
    await userEvent.click(sw);
    expect(sw).toHaveAttribute("aria-checked", "true");
    expect(sw).toHaveTextContent("On");
  });

  it("is a hover row (A5 / R8): the hover area bleeds outward, the text stays on the column", () => {
    render(<Switch label="Reduce motion" checked={false} onChange={() => undefined} />);
    expect(screen.getByRole("switch")).toHaveClass("hover-row");
    expect(screen.getByRole("switch")).not.toHaveClass("w-full");
  });
});

describe("Segmented", () => {
  it("is one tab stop; arrow keys select like native radios", async () => {
    function G() {
      const [v, setV] = useState<"s" | "m" | "l">("m");
      return <Segmented legend="Text size" value={v} onChange={setV} options={[{ value: "s", label: "Small" }, { value: "m", label: "Medium" }, { value: "l", label: "Large" }]} />;
    }
    render(<G />);
    const group = screen.getByRole("radiogroup", { name: "Text size" });
    expect(group).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Medium" })).toHaveAttribute("aria-checked", "true");
    expect(screen.getAllByRole("radio").filter((r) => r.tabIndex === 0)).toHaveLength(1);
    screen.getByRole("radio", { name: "Medium" }).focus();
    await userEvent.keyboard("{ArrowRight}");
    expect(screen.getByRole("radio", { name: "Large" })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("radio", { name: "Large" })).toHaveFocus();
    await userEvent.keyboard("{ArrowRight}"); // wraps
    expect(screen.getByRole("radio", { name: "Small" })).toHaveAttribute("aria-checked", "true");
  });

  it("options share one width per group (equal grid columns), so stacked groups keep one rhythm", () => {
    render(<Segmented<"a" | "b"> legend="Density" value="a" onChange={() => undefined} options={[{ value: "a", label: "Comfortable" }, { value: "b", label: "Compact" }]} />);
    expect(screen.getByRole("radiogroup")).toHaveClass("inline-grid", "auto-cols-fr", "grid-flow-col");
  });
});

describe("Disclosure and Chevron (redesign-round2 2.5 / 2.6 / 2.7)", () => {
  it("summary is a link-styled 44px row with a muted chevron; the chevron swaps glyphs instead of rotating", async () => {
    render(
      <Disclosure summary="Why this?">
        <p>Because.</p>
      </Disclosure>,
    );
    const summary = screen.getByText("Why this?").closest("summary")!;
    expect(summary).toHaveClass("link", "min-h-[44px]");
    expect(summary.className).not.toMatch(/text-accent/);
    const svgs = summary.querySelectorAll("svg");
    expect(svgs).toHaveLength(2);
    for (const svg of Array.from(svgs)) {
      expect(svg).toHaveAttribute("aria-hidden", "true");
      expect(svg).toHaveClass("chevron");
      expect(svg.className.baseVal).not.toMatch(/rotate|transform/);
    }
    expect(svgs[0]).toHaveClass("group-open:hidden");
    expect(svgs[1]).toHaveClass("hidden", "group-open:block");
    await userEvent.click(summary);
    expect(summary.closest("details")).toHaveAttribute("open");
  });

  it("a controlled Chevron shows exactly one glyph for each state", () => {
    const { container, rerender } = render(<Chevron open={false} />);
    const visible = () => Array.from(container.querySelectorAll("svg")).filter((s) => !s.classList.contains("hidden")).map((s) => s.getAttribute("data-chevron"));
    expect(visible()).toEqual(["right"]);
    rerender(<Chevron open />);
    expect(visible()).toEqual(["down"]);
  });
});

describe("UI kit: every slice 2 primitive is present with its states", () => {
  it("renders sheet, ledger, notes, stamps, label, button tiers, link and the dialog sample", () => {
    const { container } = render(<UiKit />);
    const kit = (k: string) => container.querySelectorAll(`[data-kit="${k}"]`);
    expect(kit("sheet")).toHaveLength(1); // exactly one sheet per screen (2.7) besides the dialog sample
    expect(kit("sheet")[0]).toHaveClass("sheet", "sheet-bleed");
    const ledger = kit("ledger")[0];
    expect(ledger).toHaveClass("ledger");
    const rows = ledger.querySelectorAll(".ledger-row");
    expect(rows).toHaveLength(3);
    for (const row of Array.from(rows)) {
      expect(row.querySelectorAll("a.ledger-row-link")).toHaveLength(1); // one tab stop, one name per row
      expect(row.querySelector(".ledger-row-end")).toHaveAttribute("aria-hidden", "true");
    }
    for (const kind of ["note-neutral", "note-notice", "note-safety", "note-danger"]) {
      expect(kit(kind)).toHaveLength(1);
      expect(kit(kind)[0]).toHaveClass("note", kind);
      expect(kit(kind)[0].querySelector(".note-title")).not.toBeNull();
      expect(kit(kind)[0].querySelectorAll("button").length).toBeLessThanOrEqual(2);
    }
    // Only the safety note (a recording fact) carries a filled action; the others are tertiary.
    expect(kit("note-safety")[0].querySelector("button")).toHaveClass("btn-primary");
    expect(kit("note-notice")[0].querySelector("button")).toHaveClass("btn-tertiary");
    expect(kit("stamp-line")[0]).toHaveClass("stamp-line");
    expect(kit("stamp-line")[0].querySelector(".stamp")).toHaveTextContent("Demo summary");
    expect(container.querySelectorAll(".label").length).toBeGreaterThanOrEqual(4);
    expect(container.querySelectorAll(".eyebrow")).toHaveLength(1); // the legacy sample only
    expect(kit("dialog-scrim")[0]).toHaveClass("dialog-scrim");
    expect(kit("dialog")[0]).toHaveClass("dialog");
    expect(within(kit("dialog")[0] as HTMLElement).getByRole("heading", { level: 3 })).toHaveClass("display", "text-xl");
    expect(screen.getByRole("button", { name: "Tertiary" })).toHaveClass("btn-tertiary");
    expect(screen.getByRole("link", { name: "open the course" })).toHaveClass("link");
  });

  it("disabled buttons use aria-disabled (still focusable) with a reason, in every tier", () => {
    render(<UiKit />);
    const disabled = screen.getAllByRole("button", { name: "Unavailable" });
    expect(disabled).toHaveLength(3);
    for (const b of disabled) {
      expect(b).toHaveAttribute("aria-disabled", "true");
      expect(b).not.toBeDisabled();
      expect(b).toHaveAccessibleDescription(UI_KIT_STRINGS.disabledWhy);
    }
    expect(screen.getByRole("button", { name: "Saving..." })).toHaveAttribute("aria-busy", "true");
  });

  it("field labels use .field-label; no <label> carries the section .label class", () => {
    const { container } = render(<UiKit />);
    for (const l of Array.from(container.querySelectorAll("label"))) expect(l).toHaveClass("field-label");
  });

  it("has no axe violations (structure; colour contrast is checked in the browser for all 12 palettes)", async () => {
    const { container } = render(<UiKit />);
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe("UI kit copy follows the language guide (copy lint)", () => {
  const all = collectStrings(UI_KIT_STRINGS);
  it("has strings to lint", () => {
    expect(all.length).toBeGreaterThan(10);
  });
  it.each(FORBIDDEN)("no %s in ui-kit copy", (_name, re) => {
    expect(all.filter((s) => re.test(s))).toEqual([]);
  });
  it("no sentence is longer than 30 words", () => {
    expect(sentencesOver(all, 30)).toEqual([]);
  });
});
