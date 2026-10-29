import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { axeViolations } from "@/test/axe";
import { Segmented } from "./Segmented";
import { Switch } from "./Switch";
import { UiKit } from "./UiKit";

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
});

it("UI kit page has no axe violations (structure; colour contrast is checked in the browser)", async () => {
  const { container } = render(<UiKit />);
  expect(await axeViolations(container)).toEqual([]);
});
