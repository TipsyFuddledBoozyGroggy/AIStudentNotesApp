import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { confirmBody, createDocsApi, newIdempotencyKey, type DocsApi } from "@/lib/docs";
import * as db from "@/mocks/db";
import { resetDocsMock, seedDocsSession } from "@/mocks/docsDb";
import { axeViolations } from "@/test/axe";
import { DOCS_COPY } from "./copy";

// DX-F16..F18a: the Demo Doc page renders title and blocks as text only, shows the practice-copy banner and lecture times,
// treats a foreign, missing or malformed id as the same calm not-found (no request for a malformed one), and deletes with
// the shared destructive dialog. MSW stand-in, synthetic data only.

const params: { docId: string } = { docId: "" };
vi.mock("next/navigation", () => ({ useParams: () => params, useRouter: () => ({ replace: vi.fn(), push: vi.fn() }) }));
vi.mock("../AuthProvider", () => ({ useRequireUser: () => ({ status: "authenticated", user: { id: "u1" } }) }));

import { DemoDocPage } from "./DemoDocPage";

const C = DOCS_COPY.page;
const SID = "0192f0a1-b2c3-4d5e-8f9a-0b1c2d3e4f01";
const NOTES_ID = "nt_0192f0a1b2c34d5e8f9a0b1c2d3e4f52";
let sent: string[];
let api: DocsApi;

async function makeDoc(over: Partial<Parameters<typeof seedDocsSession>[0]> = {}) {
  seedDocsSession({
    sessionId: SID,
    owner: "ana@dev.local",
    notesId: NOTES_ID,
    title: "Cell <b>division</b>",
    startedAt: "2026-11-05T15:00:00Z",
    sections: [{ heading: "<script>x</script>", bullets: [{ text: "<img src=x onerror=alert(1)>", startMs: [2000] }, { text: "[x](javascript:alert(1)) **bold**", startMs: [65000, 3600000] }] }],
    ...over,
  });
  const p = await api.preview(SID, NOTES_ID);
  const { record } = await api.confirm(SID, confirmBody(p, "create"), newIdempotencyKey());
  sent = [];
  return record.docRef!.docId;
}

beforeEach(() => {
  resetDocsMock();
  db.signIn("ana@dev.local");
  window.history.replaceState(null, "", "/");
  sent = [];
  api = createDocsApi({
    baseUrl: "http://localhost/api/v1",
    fetchImpl: (input, init) => {
      sent.push(`${init?.method ?? "GET"} ${String(input)}`);
      return fetch(input, init);
    },
  });
});

describe("Demo Doc page", () => {
  it("F16/F18: banner, title and blocks as literal text, lecture times in brackets, back to the lecture", async () => {
    params.docId = await makeDoc();
    window.history.replaceState(null, "", `/demo-docs/${params.docId}?lecture=${SID}`);
    const { container } = render(<DemoDocPage api={api} timeZone="UTC" />);
    await waitFor(() => expect(screen.getByTestId("demo-doc")).toBeInTheDocument());
    expect(screen.getByTestId("demo-doc-banner")).toHaveTextContent(C.banner);
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent("Cell <b>division</b> - Nov 5, 2026 UTC");
    expect(screen.getByRole("heading", { level: 3, name: "<script>x</script>" })).toBeInTheDocument();
    const items = screen.getAllByRole("listitem").map((li) => li.textContent);
    expect(items).toEqual(["<img src=x onerror=alert(1)> [00:02]", "[x](javascript:alert(1)) **bold** [01:05] [1:00:00]"]);
    const doc = screen.getByTestId("demo-doc");
    expect(doc.querySelector("img, script, b, a, strong")).toBeNull();
    expect(screen.getByRole("link", { name: C.back })).toHaveAttribute("href", `/lectures/${SID}`);
    expect(await axeViolations(container)).toEqual([]);
    expect(sent).toEqual([`GET http://localhost/api/v1/demo-docs/${params.docId}`]);
  });

  it("F18a: a notes-truncated document shows its closing 'where the notes stop' paragraph", async () => {
    params.docId = await makeDoc({ truncated: true, coveredToMs: 570000 });
    render(<DemoDocPage api={api} />);
    await waitFor(() => expect(screen.getByText("These notes stop at 09:30. Later parts of the lecture are not included.")).toBeInTheDocument());
  });

  it("F17/F04a: another user's, a missing and a malformed id all show the same calm not-found; a malformed id is never requested", async () => {
    const id = await makeDoc();
    const headings: string[] = [];
    db.signIn("ben@dev.local");
    for (const docId of [id, "ddc_00000000-0000-4000-8000-000000000999", "../x", "ddc_%2e", "ddc_%2e%2e%2fsessions", id.toUpperCase(), id.replace(/-/g, "")]) {
      params.docId = docId;
      const r = render(<DemoDocPage api={api} />);
      await waitFor(() => expect(screen.getByRole("heading", { level: 1 })).toBeInTheDocument());
      headings.push(screen.getByRole("heading", { level: 1 }).textContent!);
      expect(screen.getByRole("link", { name: C.backHome })).toHaveAttribute("href", "/");
      r.unmount();
    }
    expect(new Set(headings)).toEqual(new Set([C.notFound]));
    expect(sent).toEqual([`GET http://localhost/api/v1/demo-docs/${id}`, "GET http://localhost/api/v1/demo-docs/ddc_00000000-0000-4000-8000-000000000999"]);
  });

  it("without a lecture link it offers the dashboard; a ?lecture= that is not a session id is ignored", async () => {
    params.docId = await makeDoc();
    window.history.replaceState(null, "", `/demo-docs/${params.docId}?lecture=../../courses`);
    render(<DemoDocPage api={api} />);
    await waitFor(() => expect(screen.getByTestId("demo-doc")).toBeInTheDocument());
    expect(screen.queryByRole("link", { name: C.back })).toBeNull();
    expect(screen.getByRole("link", { name: C.backHome })).toHaveAttribute("href", "/");
  });

  it("Q3 delete: the shared dialog (Keep it focused, no typing to confirm); Keep it changes nothing; Delete removes it and says so", async () => {
    const user = userEvent.setup();
    params.docId = await makeDoc();
    render(<DemoDocPage api={api} />);
    await waitFor(() => expect(screen.getByTestId("demo-doc")).toBeInTheDocument());
    const del = screen.getByRole("button", { name: C.delete });
    await user.click(del);
    const dialog = screen.getByRole("dialog", { name: C.deleteHeading });
    expect(dialog).toHaveAccessibleDescription(C.deleteBody);
    expect(within(dialog).queryByRole("textbox")).toBeNull();
    expect(within(dialog).getByRole("button", { name: C.keep })).toHaveFocus();
    await user.click(within(dialog).getByRole("button", { name: C.keep }));
    await waitFor(() => expect(del).toHaveFocus());
    expect(sent.filter((s) => s.startsWith("DELETE"))).toEqual([]);
    await user.click(del);
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: C.deleteConfirm }));
    await waitFor(() => expect(screen.getByTestId("demo-doc-deleted")).toHaveTextContent(C.deleted));
    expect(screen.getByTestId("demo-doc-deleted")).toHaveFocus();
    expect(sent.filter((s) => s.startsWith("DELETE"))).toEqual([`DELETE http://localhost/api/v1/demo-docs/${params.docId}`]);
    await expect(api.getDemoDoc(params.docId)).rejects.toMatchObject({ status: 404 });
    await waitFor(() => expect(document.querySelector('[data-region="status"]')).toHaveTextContent(C.deleted));
  });
});

// F18 (no raw-HTML rendering API anywhere, components/docs included) is the tree-wide source guard in
// src/lib/lecture/plainText.test.tsx; it is not repeated here.
