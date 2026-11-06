import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useRef, useState } from "react";
import { describe, expect, it } from "vitest";
import { inertOutside } from "@/lib/inertOutside";
import { DocsDialog } from "./docs/DocsDialog";
import { AnnouncerProvider, useAnnouncer } from "./lecture/Announcer";
import { ConsentDialog } from "./lecture/ConsentDialog";
import { ConfirmDialog } from "./syllabus/ConfirmDialog";

// UX review docs/ux/demo-doc-and-export-ux-review.md section 7 (S2): while a modal dialog is open, the page behind is inert
// but the announcer's two regions are not, so a line said through them can still reach a screen reader. jsdom does not
// implement inert semantics, so this checks the attribute structure only; whether it is heard is the G3 manual script.
// No live region is added: the three-region tests elsewhere still count status, alert and finals only.

const REGIONS = ['[data-region="status"]', '[data-region="alert"]'];

function Page({ dialog }: { dialog: "docs" | "confirm" | "consent" }) {
  const announcer = useAnnouncer();
  const [open, setOpen] = useState(false);
  const headingRef = useRef<HTMLHeadingElement>(null);
  return (
    <main>
      <div data-testid="behind">
        <button type="button" onClick={() => setOpen(true)}>
          Open
        </button>
        <div data-region="finals" aria-live="off" />
      </div>
      <button type="button" data-testid="say" onClick={() => announcer.status("Said while open.")}>
        Say
      </button>
      {open && dialog === "docs" ? (
        <DocsDialog heading="Docs" headingRef={headingRef} onClose={() => setOpen(false)} busy={false}>
          <button type="button" onClick={() => setOpen(false)}>
            Close
          </button>
        </DocsDialog>
      ) : null}
      <ConfirmDialog open={open && dialog === "confirm"} heading="Confirm" confirmLabel="Delete" cancelLabel="Keep it" onConfirm={() => undefined} onCancel={() => setOpen(false)} />
      {open && dialog === "consent" ? <ConsentDialog courseName="Biology 101" onConfirm={() => undefined} onCancel={() => setOpen(false)} /> : null}
    </main>
  );
}

describe.each(["docs", "confirm", "consent"] as const)("%s dialog: the announcer regions stay out of the inert sweep", (dialog) => {
  it("while open, status and alert have no inert ancestor; the page behind (and the finals region) is inert; closing undoes it", async () => {
    render(
      <AnnouncerProvider>
        <Page dialog={dialog} />
      </AnnouncerProvider>,
    );
    await act(async () => screen.getByRole("button", { name: "Open" }).click());
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByRole("dialog").closest("[inert]")).toBeNull();
    for (const sel of REGIONS) {
      const region = document.querySelector(sel)!;
      expect(region, sel).not.toBeNull();
      expect(region.closest("[inert]"), sel).toBeNull();
    }
    expect(screen.getByTestId("behind").closest("[inert]")).not.toBeNull();
    expect(document.querySelector('[data-region="finals"]')!.closest("[inert]")).not.toBeNull();
    expect(screen.getByTestId("say").closest("[inert]")).not.toBeNull();
    // A line said while the dialog is up lands in the reachable region.
    await act(async () => (document.querySelector('[data-testid="say"]') as HTMLButtonElement).click());
    await act(async () => new Promise((r) => setTimeout(r, 50)));
    expect(document.querySelector('[data-region="status"]')!.textContent).toBe("Said while open.");
    await userEvent.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(document.querySelectorAll("[inert]")).toHaveLength(0);
  });
});

describe("inertOutside", () => {
  it("leaves only the path to the regions reachable, hides children added to that path later, and undoes only its own marks", async () => {
    // body > #app > (header#h, #wrap > (status, alert, section#page)), #already[inert], #overlay
    const el = (tag: string, attrs: Record<string, string>, ...kids: Element[]) => {
      const e = document.createElement(tag);
      for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, v);
      e.append(...kids);
      return e;
    };
    const tree = [
      el("div", { id: "app" }, el("header", { id: "h" }), el("div", { id: "wrap" }, el("div", { "data-region": "status" }), el("div", { "data-region": "alert" }), el("section", { id: "page" }))),
      el("div", { id: "already", inert: "" }),
      el("div", { id: "overlay" }),
    ];
    document.body.append(...tree);
    const undo = inertOutside(document.getElementById("overlay"));
    const inert = (id: string) => document.getElementById(id)!.hasAttribute("inert");
    expect([inert("app"), inert("wrap"), inert("h"), inert("page"), inert("overlay"), inert("already")]).toEqual([false, false, true, true, false, true]);
    for (const sel of REGIONS) expect(document.querySelector(sel)!.closest("[inert]")).toBeNull();
    const late = document.createElement("div");
    document.getElementById("wrap")!.appendChild(late);
    await new Promise((r) => setTimeout(r, 0));
    expect(late.hasAttribute("inert")).toBe(true);
    undo();
    expect([inert("h"), inert("page"), late.hasAttribute("inert"), inert("already")]).toEqual([false, false, false, true]);
    tree.forEach((n) => n.remove());
  });
});
