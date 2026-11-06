import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createDocsApi, type DocsApi } from "@/lib/docs";
import type { LectureNotes } from "@/lib/lecture";
import * as db from "@/mocks/db";
import { docsMock, resetDocsMock, seedDocsSession, updateDocsSession } from "@/mocks/docsDb";
import { DEMO_DOCS_FAULT_HEADER } from "@/mocks/docsHandlers";
import { server } from "@/mocks/server";
import { axeViolations } from "@/test/axe";
import { AnnouncerProvider } from "../lecture/Announcer";
import { DOCS_COPY as C } from "./copy";
import { DocsExport } from "./DocsExport";
import { DocsExportEntry } from "./DocsExportEntry";

// DX-F02..F15 on the Save to a Demo Doc dialog, over the MSW stand-in (synthetic data, no network, no Google). Requests
// are recorded at the fetch boundary so the tests can assert exactly what the client sent.

const SID = "0192f0a1-b2c3-4d5e-8f9a-0b1c2d3e4f01";
const NOTES_ID = "nt_0192f0a1b2c34d5e8f9a0b1c2d3e4f52";
const READY: LectureNotes = {
  notesId: NOTES_ID,
  revision: 1,
  status: "Ready",
  source: "mock",
  requestedAt: "2026-11-05T15:02:11Z",
  generatedAt: "2026-11-05T15:02:13Z",
  coveredFromMs: 1000,
  coveredToMs: 60000,
  sections: [{ heading: "Mitosis", bullets: [{ text: "Mitosis has four phases.", citations: [{ segmentId: "seg_1", startMs: 2000 }] }] }],
  finalSummaryRevision: null,
  finalSummaryStatus: "Ready",
  truncated: false,
};

interface Sent {
  method: string;
  url: string;
  key: string | null;
  body: unknown;
}
let sent: Sent[];
let fault: string | null;
let api: DocsApi;

function seed(over: Partial<Parameters<typeof seedDocsSession>[0]> = {}) {
  seedDocsSession({
    sessionId: SID,
    owner: "ana@dev.local",
    notesId: NOTES_ID,
    title: "Cell division",
    startedAt: "2026-11-05T15:00:00Z",
    sections: [{ heading: "Mitosis", bullets: [{ text: "Mitosis has four phases.", startMs: [2000] }, { text: "<img src=x onerror=alert(1)> [x](javascript:alert(1))", startMs: [4000] }] }],
    ...over,
  });
}

beforeEach(() => {
  resetDocsMock();
  db.signIn("ana@dev.local");
  seed();
  sent = [];
  fault = null;
  api = createDocsApi({
    baseUrl: "http://localhost/api/v1",
    fetchImpl: (input, init) => {
      const headers = new Headers(init?.headers);
      sent.push({ method: init?.method ?? "GET", url: String(input), key: headers.get("Idempotency-Key"), body: typeof init?.body === "string" ? JSON.parse(init.body) : undefined });
      if (fault && init?.method === "POST" && String(input).endsWith("/docs-export")) headers.set(DEMO_DOCS_FAULT_HEADER, fault);
      return fetch(input, { ...init, headers });
    },
  });
});

const confirms = () => sent.filter((s) => s.method === "POST" && s.url.endsWith("/docs-export"));
const previews = () => sent.filter((s) => s.url.endsWith("/docs-export/preview"));
const statusText = () => document.querySelector('[data-region="status"]')?.textContent ?? "";

async function mount() {
  const user = userEvent.setup();
  const r = render(
    <AnnouncerProvider>
      <DocsExport sessionId={SID} notes={READY} api={api} timeZone="UTC" />
    </AnnouncerProvider>,
  );
  await act(async () => undefined);
  return { user, ...r };
}
const dialog = () => screen.getByRole("dialog");
async function openPreview(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: C.entry.button }));
  await waitFor(() => expect(within(dialog()).getByTestId("docs-preview")).toBeInTheDocument());
}

describe("Save to a Demo Doc dialog", () => {
  it("F07/F12: opens on the heading, shows exactly what will be saved and nothing 'saved' before the confirm; text stays text", async () => {
    const { user } = await mount();
    const opener = screen.getByRole("button", { name: C.entry.button });
    expect(opener).toHaveAccessibleDescription(C.entry.help);
    await user.click(opener);
    expect(screen.getByRole("heading", { level: 2, name: C.preview.heading })).toHaveFocus();
    await waitFor(() => expect(within(dialog()).getByTestId("docs-preview")).toBeInTheDocument());
    const d = dialog();
    for (const t of [C.preview.line1, C.preview.where, "Title: Cell division - Nov 5, 2026 UTC", "1 section, 2 bullets. Each bullet keeps its lecture time."]) expect(d).toHaveTextContent(t);
    expect(within(d).getByTestId("demo-notes-tag")).toBeInTheDocument();
    for (const t of [C.success.created, C.success.updated, C.success.unchanged, C.entry.saved("")]) expect(d.textContent).not.toContain(t.replace(/\.$/, ""));
    const region = within(d).getByRole("region", { name: C.preview.regionLabel });
    expect(region).toHaveTextContent("<img src=x onerror=alert(1)> [x](javascript:alert(1)) [00:04]");
    expect(region.querySelector("img, a, script")).toBeNull();
    expect(previews()).toHaveLength(1);
    expect(previews()[0]!.body).toEqual({ notesId: NOTES_ID });
    expect(confirms()).toHaveLength(0);
    expect(docsMock.docCount()).toBe(0);
  });

  it("F12: Escape and Not now close without writing and return focus to the button", async () => {
    const { user } = await mount();
    await openPreview(user);
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).toBeNull();
    await waitFor(() => expect(screen.getByRole("button", { name: C.entry.button })).toHaveFocus());
    await openPreview(user);
    await user.click(within(dialog()).getByRole("button", { name: C.preview.notNow }));
    await waitFor(() => expect(screen.getByRole("button", { name: C.entry.button })).toHaveFocus());
    expect(confirms()).toHaveLength(0);
    expect(docsMock.docCount()).toBe(0);
  });

  it("F03/F09/F10: confirm sends ids and the digest only, once for a double press; success says so and links to the Demo Doc", async () => {
    const { user } = await mount();
    await openPreview(user);
    const save = within(dialog()).getByRole("button", { name: C.preview.save });
    await user.dblClick(save);
    await waitFor(() => expect(within(dialog()).getByTestId("docs-success")).toHaveTextContent(C.success.created));
    expect(confirms()).toHaveLength(1);
    const c = confirms()[0]!;
    expect(c.body).toEqual({
      notesId: NOTES_ID,
      contentDigest: expect.stringMatching(/^cd_/) as unknown as string,
      mode: "create",
      expectedExistingVersion: null,
      expectedExistingDocId: null,
      confirm: { provider: "demo_docs", acknowledgedDemo: true },
    });
    expect(JSON.stringify(c.body)).not.toMatch(/Cell division|Mitosis|"title"|"blocks"/);
    expect(c.key).toMatch(/^[A-Za-z0-9_-]{8,128}$/);
    expect(within(dialog()).getByTestId("docs-success")).toHaveFocus();
    expect(dialog()).toHaveTextContent(C.success.where);
    const link = within(dialog()).getByRole("link", { name: C.entry.open });
    const docId = docsMock.target(SID)!.docId;
    expect(link).toHaveAttribute("href", `/demo-docs/${docId}?lecture=${SID}`);
    await waitFor(() => expect(statusText()).toBe(C.success.created), { timeout: 3000 });
    await user.click(within(dialog()).getByRole("button", { name: C.preview.close }));
    expect(screen.getByTestId("docs-export-saved")).toHaveTextContent(`${C.entry.saved("November 6")} ${C.entry.open}`);
    expect(docsMock.docCount()).toBe(1);
  });

  it("F09: while saving the button says Saving..., is aria-busy, the dialog stays open and Escape does nothing", async () => {
    let release!: () => void;
    server.use(
      http.post("*/api/v1/sessions/:id/docs-export", async () => {
        await new Promise<void>((r) => (release = r));
        return HttpResponse.json({}, { status: 503 });
      }),
    );
    const { user } = await mount();
    await openPreview(user);
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    const busy = within(dialog()).getByRole("button", { name: C.saving.button });
    expect(busy).toHaveAttribute("aria-busy", "true");
    expect(within(dialog()).getByRole("button", { name: C.preview.notNow })).toHaveAttribute("aria-disabled", "true");
    expect(statusText()).toBe(C.saving.announce);
    await user.keyboard("{Escape}");
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    await act(async () => release());
    await waitFor(() => expect(within(dialog()).getByTestId("docs-retry")).toBeInTheDocument());
  });

  it("F04/F10: a provider failure says nothing was saved, gives the advisory wait, and Try again resends the same key and body", async () => {
    const { user } = await mount();
    await openPreview(user);
    fault = "rate_limited:20";
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-retry")).toHaveTextContent(C.retry.body));
    expect(within(dialog()).getByTestId("docs-retry")).toHaveFocus();
    expect(within(dialog()).getByTestId("docs-retry-wait")).toHaveTextContent(C.retry.wait(20));
    const again = within(dialog()).getByRole("button", { name: C.retry.tryAgain });
    expect(again).not.toHaveAttribute("aria-disabled"); // the wait is advice, not a lock
    fault = null;
    await user.click(again);
    await waitFor(() => expect(within(dialog()).getByTestId("docs-success")).toBeInTheDocument());
    const [a, b] = confirms();
    expect(b!.key).toBe(a!.key);
    expect(b!.body).toEqual(a!.body);
    expect(docsMock.docCount()).toBe(1);
    // A new preview makes a new key.
    await user.click(within(dialog()).getByRole("button", { name: C.preview.close }));
    updateDocsSession(SID, { title: "Cell division, revised" });
    await openPreview(user);
    await user.click(within(dialog()).getByRole("radio", { name: C.changed.update }));
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-success")).toHaveTextContent(C.success.updated));
    expect(confirms()[2]!.key).not.toBe(a!.key);
  });

  it("F10: after the fifth failed attempt it stops and offers Start again; not-retryable shows the permanent line", async () => {
    const { user } = await mount();
    await openPreview(user);
    fault = "unavailable";
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    for (let i = 0; i < 4; i++) {
      await waitFor(() => expect(within(dialog()).getByRole("button", { name: C.retry.tryAgain })).toBeInTheDocument());
      await user.click(within(dialog()).getByRole("button", { name: C.retry.tryAgain }));
    }
    await waitFor(() => expect(within(dialog()).getByTestId("docs-last-attempt")).toHaveTextContent(C.lastAttempt.body));
    expect(new Set(confirms().map((c) => c.key)).size).toBe(1);
    fault = null;
    await user.click(within(dialog()).getByRole("button", { name: C.lastAttempt.startAgain }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-preview")).toBeInTheDocument());
    fault = "rejected";
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-permanent")).toHaveTextContent(C.permanent));
    expect(confirms().at(-1)!.key).not.toBe(confirms()[0]!.key);
    expect(within(dialog()).getAllByRole("button").map((b) => b.textContent)).toEqual([C.preview.close]);
  });

  it("F11: a stale confirm (412) refetches the preview, says nothing was saved yet, and the student confirms again", async () => {
    const { user } = await mount();
    await openPreview(user);
    updateDocsSession(SID, { title: "Renamed while looking" });
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-stale")).toHaveTextContent(C.stale));
    expect(within(dialog()).getByTestId("docs-stale")).toHaveFocus();
    expect(within(dialog()).getByTestId("docs-preview-title")).toHaveTextContent("Renamed while looking");
    expect(docsMock.docCount()).toBe(0);
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-success")).toBeInTheDocument());
    expect(confirms()[1]!.key).not.toBe(confirms()[0]!.key);
  });

  it("M1: a 429 is a server-confirmed failure: the 'nothing was saved' line, the advisory wait, and the same key on Try again", async () => {
    server.use(
      http.post(
        "*/api/v1/sessions/:id/docs-export",
        () => HttpResponse.json({ type: "https://errors.tether.invalid/rate_limited", title: "slow", status: 429, code: "rate_limited", traceId: "t" }, { status: 429, headers: { "Retry-After": "7" } }),
        { once: true },
      ),
    );
    const { user } = await mount();
    await openPreview(user);
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-retry")).toHaveTextContent(C.retry.body));
    expect(within(dialog()).getByTestId("docs-retry")).toHaveFocus();
    await waitFor(() => expect(statusText()).toBe(C.retry.body), { timeout: 3000 });
    await user.click(within(dialog()).getByRole("button", { name: C.retry.tryAgain }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-success")).toBeInTheDocument());
    const [a, b] = confirms();
    expect([b!.key, b!.body]).toEqual([a!.key, a!.body]);
  });

  it.each([
    ["no answer (network error)", () => HttpResponse.error()],
    ["a 503", () => HttpResponse.json({ type: "https://errors.tether.invalid/provider_unavailable", title: "x", status: 503, code: "provider_unavailable", traceId: "t" }, { status: 503 })],
    ["a 200 body that does not parse", () => HttpResponse.json({ status: "succeeded" })],
  ])("M1: %s may have saved, so the line never says nothing was saved; Try again reuses the key and saves once", async (_n, reply) => {
    server.use(http.post("*/api/v1/sessions/:id/docs-export", reply, { once: true }));
    const { user } = await mount();
    await openPreview(user);
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-retry")).toHaveTextContent(C.retry.unknown));
    const line = within(dialog()).getByTestId("docs-retry");
    expect(line).toHaveFocus();
    expect(dialog().textContent).not.toMatch(/nothing was saved/i);
    expect(within(dialog()).queryByTestId("docs-retry-wait")).toBeNull();
    await waitFor(() => expect(statusText()).toBe(C.retry.unknown), { timeout: 3000 });
    await user.click(within(dialog()).getByRole("button", { name: C.retry.tryAgain }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-success")).toHaveTextContent(C.success.created));
    const [a, b] = confirms();
    expect([b!.key, b!.body]).toEqual([a!.key, a!.body]);
    expect(docsMock.docCount()).toBe(1);
  });

  const invalidState = () =>
    HttpResponse.json({ type: "https://errors.tether.invalid/invalid_state", title: "state", status: 409, code: "invalid_state", traceId: "t" }, { status: 409 });

  it("M3: a first 409 invalid_state reloads the preview with the changed-state line (focused, said once); nothing was saved", async () => {
    server.use(http.post("*/api/v1/sessions/:id/docs-export", invalidState, { once: true }));
    const { user } = await mount();
    await openPreview(user);
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-changed-state")).toHaveTextContent(C.changedState));
    expect(within(dialog()).getByTestId("docs-changed-state")).toHaveFocus();
    expect(within(dialog()).queryByTestId("docs-permanent")).toBeNull();
    expect(within(dialog()).queryByTestId("docs-stale")).toBeNull();
    expect(within(dialog()).getByTestId("docs-preview")).toBeInTheDocument();
    await waitFor(() => expect(statusText()).toBe(C.changedState), { timeout: 3000 });
    expect(previews()).toHaveLength(2);
    expect(docsMock.docCount()).toBe(0);
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-success")).toBeInTheDocument());
    expect(confirms()[1]!.key).not.toBe(confirms()[0]!.key); // a new preview makes a new key (F04)
    expect(docsMock.docCount()).toBe(1);
  });

  it("M3: a second 409 invalid_state in the same opening shows the permanent line; a new opening starts the count again", async () => {
    server.use(http.post("*/api/v1/sessions/:id/docs-export", invalidState));
    const { user } = await mount();
    await openPreview(user);
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-changed-state")).toBeInTheDocument());
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-permanent")).toHaveTextContent(C.permanent));
    expect(within(dialog()).getByTestId("docs-permanent")).toHaveFocus();
    expect(previews()).toHaveLength(2);
    await user.click(within(dialog()).getByRole("button", { name: C.preview.close }));
    await openPreview(user);
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-changed-state")).toBeInTheDocument());
    expect(confirms()).toHaveLength(3);
  });

  it("F08 (1b): with an earlier Demo Doc and changed notes, Save stays disabled until a choice; no default; update and second doc", async () => {
    const { user } = await mount();
    await openPreview(user);
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-success")).toBeInTheDocument());
    await user.click(within(dialog()).getByRole("button", { name: C.preview.close }));
    updateDocsSession(SID, { title: "Cell division, revised" });
    await openPreview(user);
    const d = dialog();
    expect(within(d).getByTestId("docs-changed")).toHaveTextContent(C.changed.line("November 6"));
    const group = within(d).getByRole("group", { name: C.changed.legend });
    const radios = within(group).getAllByRole("radio");
    expect(radios.map((r) => (r as HTMLInputElement).checked)).toEqual([false, false]);
    const save = within(d).getByRole("button", { name: C.preview.save });
    expect(save).toHaveAttribute("aria-disabled", "true");
    expect(save).toHaveAccessibleDescription(C.changed.hint);
    const before = confirms().length;
    await user.click(save);
    expect(confirms()).toHaveLength(before);
    await user.click(within(d).getByRole("radio", { name: C.changed.another }));
    expect(save).not.toHaveAttribute("aria-disabled");
    await user.click(save);
    await waitFor(() => expect(within(dialog()).getByTestId("docs-success")).toHaveTextContent(C.success.created));
    const body = confirms().at(-1)!.body as { mode: string; expectedExistingDocId: string; expectedExistingVersion: number };
    expect(body.mode).toBe("create_another");
    expect(body.expectedExistingDocId).toMatch(/^ddc_/);
    expect(body.expectedExistingVersion).toBe(1);
    expect(docsMock.docCount()).toBe(2);
  });

  it("1c / 1d: already matches offers Open Demo Doc and Close only; too long and nothing-to-save offer Close only", async () => {
    const { user } = await mount();
    await openPreview(user);
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-success")).toBeInTheDocument());
    await user.click(within(dialog()).getByRole("button", { name: C.preview.close }));
    await openPreview(user);
    expect(within(dialog()).getByTestId("docs-matches")).toHaveTextContent(C.matches);
    expect(within(dialog()).queryByRole("button", { name: C.preview.save })).toBeNull();
    expect(within(dialog()).getByRole("link", { name: C.entry.open })).toBeInTheDocument();
    await user.click(within(dialog()).getByRole("button", { name: C.preview.close }));

    seed({ sections: [{ heading: "Long", bullets: Array.from({ length: 401 }, (_, i) => ({ text: `Point ${i + 1}.`, startMs: [i * 1000] })) }] });
    resetTargetsKeepingSession();
    await openPreview(user);
    expect(within(dialog()).getByTestId("docs-blocked")).toHaveTextContent(C.tooLarge);
    expect(within(dialog()).getAllByRole("button").map((b) => b.textContent)).toEqual([C.preview.close]);
    await user.click(within(dialog()).getByRole("button", { name: C.preview.close }));
    seed({ sections: [] });
    await openPreview(user);
    expect(within(dialog()).getByTestId("docs-blocked")).toHaveTextContent(C.nothing);
  });

  it("F02 / 8: a preview body that does not parse shows the calm could-not-show state, Try again works; a 404 says not available", async () => {
    server.use(http.post("*/api/v1/sessions/:id/docs-export/preview", () => HttpResponse.json({ previewOnly: true, document: { title: "partial" } }), { once: true }));
    const { user } = await mount();
    await user.click(screen.getByRole("button", { name: C.entry.button }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-preview-error")).toHaveTextContent(C.preview.error));
    expect(dialog().textContent).not.toContain("partial");
    await user.click(within(dialog()).getByRole("button", { name: C.preview.tryAgain }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-preview")).toBeInTheDocument());
    await user.click(within(dialog()).getByRole("button", { name: C.preview.notNow }));
    db.signIn("ben@dev.local");
    await user.click(screen.getByRole("button", { name: C.entry.button }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-not-available")).toHaveTextContent(C.notAvailable));
  });

  it("7: a 409 export_in_progress (reserved) shows Check again, which reloads the preview", async () => {
    server.use(
      http.post("*/api/v1/sessions/:id/docs-export", () =>
        HttpResponse.json({ type: "https://errors.tether.invalid/export_in_progress", title: "busy", status: 409, code: "export_in_progress", traceId: "t" }, { status: 409 }),
      ),
    );
    const { user } = await mount();
    await openPreview(user);
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-in-progress")).toHaveTextContent(C.inProgress.body));
    await user.click(within(dialog()).getByRole("button", { name: C.inProgress.checkAgain }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-preview")).toBeInTheDocument());
  });

  it("F15: axe finds nothing on the entry, the preview, the choice, saving's result and the error states (jsdom; contrast in the browser)", async () => {
    const { user, container } = await mount();
    expect(await axeViolations(container)).toEqual([]);
    await openPreview(user);
    expect(await axeViolations(document.body)).toEqual([]);
    fault = "unavailable";
    await user.click(within(dialog()).getByRole("button", { name: C.preview.save }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-retry")).toBeInTheDocument());
    expect(await axeViolations(document.body)).toEqual([]);
    fault = null;
    await user.click(within(dialog()).getByRole("button", { name: C.retry.tryAgain }));
    await waitFor(() => expect(within(dialog()).getByTestId("docs-success")).toBeInTheDocument());
    expect(await axeViolations(document.body)).toEqual([]);
    await user.click(within(dialog()).getByRole("button", { name: C.preview.close }));
    updateDocsSession(SID, { title: "Cell division, revised" });
    await openPreview(user);
    expect(await axeViolations(document.body)).toEqual([]);
  });

  it("F12: Tab stays inside the dialog and the page behind is inert while it is open", async () => {
    const { user } = await mount();
    await openPreview(user);
    const outside = screen.getByTestId("docs-export");
    expect(outside.closest("[inert]")).not.toBeNull();
    for (let i = 0; i < 8; i++) {
      await user.tab();
      expect(dialog().contains(document.activeElement)).toBe(true);
    }
    await user.keyboard("{Escape}");
    expect(outside.closest("[inert]")).toBeNull();
  });
});

describe("F06: the entry shows only for Ready notes with sections, on a canonical session id", () => {
  const mountEntry = (notes: LectureNotes, sessionId = SID) =>
    render(
      <AnnouncerProvider>
        <DocsExportEntry notes={notes} sessionId={sessionId} api={api} />
      </AnnouncerProvider>,
    );

  it.each(["Pending", "Unavailable", "BudgetLimited", "NotNeeded"] as const)("absent for %s", (status) => {
    mountEntry({ ...READY, status, sections: [] });
    expect(screen.queryByRole("button", { name: C.entry.button })).toBeNull();
  });

  it("absent for a non-canonical session id (no request made with it)", async () => {
    for (const id of ["ses_test", SID.toUpperCase(), `{${SID}}`, "../x"]) {
      const r = mountEntry(READY, id);
      expect(screen.queryByRole("button", { name: C.entry.button })).toBeNull();
      r.unmount();
    }
    await act(async () => undefined);
    expect(sent).toHaveLength(0);
  });

  it("present for Ready; an existing Demo Doc shows the saved line and link from the state", async () => {
    const preview = await api.preview(SID, NOTES_ID);
    const { confirmBody, newIdempotencyKey } = await import("@/lib/docs");
    await api.confirm(SID, confirmBody(preview, "create"), newIdempotencyKey());
    vi.useRealTimers();
    render(
      <AnnouncerProvider>
        <DocsExport sessionId={SID} notes={READY} api={api} timeZone="UTC" />
      </AnnouncerProvider>,
    );
    await waitFor(() => expect(screen.getByTestId("docs-export-saved")).toHaveTextContent(C.entry.saved("November 6")));
    expect(screen.getByRole("link", { name: C.entry.open })).toHaveAttribute("href", `/demo-docs/${docsMock.target(SID)!.docId}?lecture=${SID}`);
  });
});

function resetTargetsKeepingSession() {
  // A new lecture state for the 1d cases: same session, no Demo Doc yet.
  const s = docsMock.session("ana@dev.local", SID)!;
  resetDocsMock();
  seedDocsSession({ ...s });
}
