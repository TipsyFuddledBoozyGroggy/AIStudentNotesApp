import { render, screen } from "@testing-library/react";
import { axeViolations } from "@/test/axe";
import { AuthProvider } from "./AuthProvider";
import { SignInView } from "./SignInView";

vi.mock("next/navigation", () => ({ usePathname: () => "/signin", useRouter: () => ({ replace: vi.fn(), push: vi.fn() }) }));

describe("SignInView (redesign-round2 slice 4, Auth template)", () => {
  it("h1 and sentence above the one sheet, which holds the field and the button; no card; no axe violations", async () => {
    const { container } = render(
      <AuthProvider>
        <SignInView />
      </AuthProvider>,
    );
    const form = screen.getByRole("form", { name: "Sign in" });
    expect(form).toHaveClass("sheet", "sheet-page");
    expect(form.contains(screen.getByRole("heading", { level: 1, name: "Sign in" }))).toBe(false);
    expect(form).toContainElement(screen.getByLabelText("Email"));
    expect(form).toContainElement(screen.getByRole("button", { name: "Sign in" }));
    expect(container.querySelectorAll(".card, .eyebrow")).toHaveLength(0);
    expect(await axeViolations(container)).toEqual([]);
  });
});
