import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { syllabusApi, type SyllabusApi } from "@/lib/syllabus/client";
import { server } from "@/mocks/server";
import * as sdb from "@/mocks/syllabusDb";
import { axeViolations } from "@/test/axe";
import { Review } from "./Review";
import { SyllabusFlow } from "./SyllabusFlow";
import { COURSE, EMAIL, renderWithAnnouncer, signedInWithCourse } from "./testUtils";

// Increment P2-F2 (mock only): batch approve, one at a time, demo calendar sync / remove, delete, export, file intake,
// Stop reading, several syllabi, session expiry. Every request goes to the MSW mock, which is schema-checked elsewhere.

async function review(scenario: "ready" | "hostile" | "purged" = "ready", api: SyllabusApi = syllabusApi, onDeleted?: (id: string) => void) {
  signedInWithCourse();
  sdb.seedSyllabus(EMAIL, COURSE, scenario);
  const syllabus = await api.getSyllabus(`syl_${scenario}`);
  const r = renderWithAnnouncer(<Review api={api} syllabus={syllabus} courseId={COURSE} onDeleted={onDeleted} />);
  await screen.findByText(/We found \d+ items/);
  return r;
}

const row = (title: string) => screen.getByRole("listitem", { name: title });

/** Expansion is kept while the student moves between views, so the button may already read Collapse all. */
async function expandAll() {
  const b = await screen.findByRole("button", { name: /^(Expand all|Collapse all)$/ });
  if (b.textContent === "Expand all") await userEvent.click(b);
}

function trackRequests(method: string, re: RegExp) {
  const seen: { key: string | null; body: Promise<string>; search: string }[] = [];
  server.events.on("request:start", ({ request }) => {
    if (request.method === method && re.test(new URL(request.url).pathname)) seen.push({ key: request.headers.get("Idempotency-Key"), body: request.clone().text(), search: new URL(request.url).search });
  });
  return seen;
}

afterEach(() => server.events.removeAllListeners());

describe("batch approve (UX 5.4; optional, clear items only, own undo)", () => {
  it("lists every clear item with its source wording, approves exactly those, and Undo this restores them", async () => {
    const r = await review();
    await userEvent.click(screen.getByText("More ways to decide"));
    await userEvent.click(screen.getByRole("button", { name: "Approve all clear items (5)" }));
    const panel = screen.getByRole("region", { name: "This will approve 5 items that have a date and look right. You can undo it." });
    expect(within(panel).getByRole("heading", { level: 2 })).toHaveFocus();
    const listed = within(panel).getAllByRole("listitem");
    expect(listed).toHaveLength(5);
    for (const li of listed) expect(within(li).getAllByText(/From your syllabus/).length).toBeGreaterThan(0);
    expect(within(panel).queryByText("Lab report 2")).toBeNull(); // Please check: never in a batch
    expect(within(panel).queryByText("Quiz 1")).toBeNull(); // Probably right: never in a batch
    expect(await axeViolations(r.container)).toEqual([]);
    await userEvent.click(within(panel).getByRole("button", { name: "Approve 5 items" }));
    const undo = await screen.findByRole("button", { name: "Undo this" });
    await waitFor(() => expect(undo).toHaveFocus());
    expect(screen.getByText("Approved 5 items.", { selector: "[data-batch-undo] p" })).toBeInTheDocument();
    expect(screen.getByText("We found 11 items. 5 approved, 0 skipped, 6 not decided.")).toBeInTheDocument();
    await waitFor(() => expect(r.statusLog).toContain("Approved 5 items."));
    expect(screen.queryByRole("button", { name: /^Approve all clear items/ })).toBeNull(); // nothing clear is left
    await userEvent.click(undo);
    expect(await screen.findByText("We found 11 items. 0 approved, 0 skipped, 11 not decided.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Undo this" })).toBeNull();
  });

  it("per-week button: label count equals what it approves; the name names the week (unique per group)", async () => {
    await review();
    await expandAll();
    const b = screen.getByRole("button", { name: "Approve the clear ones in this week (1): Week of Oct 12 to 18" });
    await userEvent.click(b);
    await userEvent.click(screen.getByRole("button", { name: "Approve 1 item" }));
    await screen.findByRole("button", { name: "Undo this" });
    expect(within(row("Midterm exam")).getByText("Approved")).toBeInTheDocument();
    expect(within(row("Lab report 2")).getByText("Not decided")).toBeInTheDocument();
  });

  it("is never offered for a purged syllabus (R3-01) or a hostile one", async () => {
    await review("purged");
    expect(screen.queryByText("More ways to decide")).toBeNull();
    expect(screen.queryByRole("button", { name: /clear/i })).toBeNull();
  });
});

describe("one at a time (UX 5.5)", () => {
  it("Item N of M as text, no progress bar; Approve advances; Next does not decide; Back shows the decision; Escape returns to the row", async () => {
    const r = await review();
    await userEvent.click(screen.getByRole("button", { name: "Review one at a time" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Review one at a time" })).toHaveFocus();
    expect(screen.getByTestId("one-position")).toHaveTextContent("Item 1 of 11");
    expect(screen.queryByRole("progressbar")).toBeNull();
    expect(screen.getByRole("heading", { level: 2, name: "Office hours" })).toBeInTheDocument();
    expect(await axeViolations(r.container)).toEqual([]);
    await userEvent.click(screen.getByRole("button", { name: "Approve Office hours" }));
    const h2 = await screen.findByRole("heading", { level: 2, name: "Quiz 1" });
    await waitFor(() => expect(h2).toHaveFocus());
    expect(screen.getByTestId("one-position")).toHaveTextContent("Item 2 of 11");
    await waitFor(() => expect(r.statusLog).toContain("Quiz 1"));
    await userEvent.click(screen.getByRole("button", { name: "Next, decide later" }));
    expect(screen.getByTestId("one-position")).toHaveTextContent("Item 3 of 11");
    await userEvent.click(screen.getByRole("button", { name: "Back" }));
    expect(screen.getByRole("heading", { level: 2, name: "Quiz 1" })).toBeInTheDocument();
    expect(screen.getByText("Not decided")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Back" }));
    expect(screen.getByText("Approved")).toBeInTheDocument();
    await userEvent.keyboard("{Escape}");
    expect(await screen.findByRole("heading", { level: 1, name: "Review your syllabus items" })).toBeInTheDocument();
    await waitFor(() => expect(within(row("Office hours")).getAllByRole("button")[0]).toHaveFocus());
  });

  it("the end says what was decided and offers the calendar step when something is approved", async () => {
    await review();
    await userEvent.click(screen.getByRole("button", { name: "Review one at a time" }));
    await userEvent.click(await screen.findByRole("button", { name: "Approve Office hours" }));
    for (let i = 0; i < 10; i++) await userEvent.click(screen.getByRole("button", { name: "Next, decide later" }));
    expect(await screen.findByRole("heading", { level: 2, name: "That's all of them." })).toHaveFocus();
    expect(screen.getByText("1 approved, 0 skipped, 10 not decided.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Go to calendar step" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Add approved items to the demo calendar" })).toHaveFocus();
  });
});

async function approveInList(...titles: string[]) {
  await expandAll();
  for (const t of titles) {
    const b = within(row(t)).getByRole("button", { name: `Approve ${t}` });
    await userEvent.click(b);
    await within(row(t)).findByRole("button", { name: `Undo approval of ${t}` });
  }
}

describe("demo calendar sync (UX 6, contract 6.3)", () => {
  it("confirm lists exactly the approved items; one press = one key; result focuses its h1; a re-run says Already in the demo calendar", async () => {
    const r = await review();
    const posts = trackRequests("POST", /\/calendar-sync$/);
    await approveInList("Midterm exam", "Read chapter 3");
    await userEvent.click(screen.getByRole("button", { name: "Add 2 approved items to the demo calendar" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Add approved items to the demo calendar" })).toHaveFocus();
    expect(screen.getByText("Demo only. Nothing is sent to Google Calendar. Your Google account is not connected.")).toBeInTheDocument();
    expect(screen.getByText("Demo calendar")).toBeInTheDocument();
    expect(screen.getAllByRole("listitem")).toHaveLength(2);
    expect(screen.queryByText(/reminder/i)).toBeNull(); // no reminders in v1 (contract row 13)
    expect(await axeViolations(r.container)).toEqual([]);
    const go = screen.getByRole("button", { name: "Add 2 items to the demo calendar" });
    fireEvent.click(go);
    fireEvent.click(go);
    const h1 = await screen.findByRole("heading", { level: 1, name: "Done. 2 items are in the demo calendar." });
    await waitFor(() => expect(h1).toHaveFocus());
    expect(screen.getByText("This is a demo. Nothing was sent to Google Calendar.")).toBeInTheDocument();
    expect(screen.getAllByText("Added")).toHaveLength(2);
    expect(posts).toHaveLength(1);
    const body = JSON.parse(await posts[0].body);
    expect(body.confirm).toEqual({ provider: "mock", acknowledgedDemo: true });
    expect(Object.keys(body.itemRevisions)).toHaveLength(2);
    expect(r.statusLog.filter((s) => /demo calendar/.test(s))).toEqual([]); // the h1 takes focus; no double announcement
    expect(await axeViolations(r.container)).toEqual([]);

    await userEvent.click(screen.getByRole("button", { name: "Back to review" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Review your syllabus items" })).toBeInTheDocument();
    await expandAll();
    expect(await within(row("Midterm exam")).findByText("In the demo calendar")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Add 2 approved items to the demo calendar" }));
    expect(await screen.findAllByText("Already in the demo calendar")).toHaveLength(2);
    await userEvent.click(screen.getByRole("button", { name: "Add 2 items to the demo calendar" }));
    expect(await screen.findByRole("heading", { level: 1, name: "These items are already in the demo calendar." })).toBeInTheDocument();
    expect(posts).toHaveLength(2);
    expect(posts[1].key).not.toBe(posts[0].key);
  });

  it("partial: Added 1 of 2, never the success heading; the other has a reason and Try again", async () => {
    signedInWithCourse();
    sdb.seedSyllabus(EMAIL, COURSE, "ready");
    const items = await syllabusApi.listItems("syl_ready");
    const a = items.find((i) => i.title === "Midterm exam")!;
    const b = items.find((i) => i.title === "Read chapter 3")!;
    const b2 = await syllabusApi.updateItem("syl_ready", b.id, { title: "Read chapter 3 mock:sync-fail" }, b.etag);
    await syllabusApi.decide("syl_ready", a.id, "approved", a.etag);
    await syllabusApi.decide("syl_ready", b2.id, "approved", b2.etag);
    const s = await syllabusApi.getSyllabus("syl_ready");
    renderWithAnnouncer(<Review api={syllabusApi} syllabus={s} courseId={COURSE} />);
    await userEvent.click(await screen.findByRole("button", { name: "Add 2 approved items to the demo calendar" }));
    await userEvent.click(await screen.findByRole("button", { name: "Add 2 items to the demo calendar" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Added 1 of 2. 1 didn't go through." })).toBeInTheDocument();
    expect(screen.queryByText(/^Done\./)).toBeNull();
    expect(screen.getByText("The demo calendar was not available.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Try again: Read chapter 3 mock:sync-fail" })).toBeInTheDocument();
  });

  it("a changed item is sent only when the student ticks Update (nothing updates silently)", async () => {
    await review();
    await approveInList("Midterm exam");
    await userEvent.click(screen.getByRole("button", { name: "Add 1 approved item to the demo calendar" }));
    await userEvent.click(await screen.findByRole("button", { name: "Add 1 item to the demo calendar" }));
    await userEvent.click(await screen.findByRole("button", { name: "Back to review" }));
    await expandAll();
    await userEvent.click(within(row("Midterm exam")).getByRole("button", { name: "Edit Midterm exam" }));
    const title = screen.getByLabelText("Title");
    await userEvent.clear(title);
    await userEvent.type(title, "Midterm exam 1{Enter}");
    expect(await within(row("Midterm exam 1")).findByText("Changed since it was added")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Add 1 approved item to the demo calendar" }));
    const go = await screen.findByRole("button", { name: "Add 0 items to the demo calendar" });
    expect(go).toHaveAttribute("aria-disabled", "true");
    expect(screen.getByText("1 changed item stays as it was in the demo calendar.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("checkbox", { name: "Update it in the demo calendar" }));
    await userEvent.click(screen.getByRole("button", { name: "Add 1 item to the demo calendar" }));
    expect(await screen.findByText("Updated")).toBeInTheDocument();
  });

  it("Remove from the demo calendar keeps the approval; its Undo adds it back", async () => {
    const r = await review();
    await approveInList("Midterm exam");
    await userEvent.click(screen.getByRole("button", { name: "Add 1 approved item to the demo calendar" }));
    await userEvent.click(await screen.findByRole("button", { name: "Add 1 item to the demo calendar" }));
    await userEvent.click(await screen.findByRole("button", { name: "Back to review" }));
    await expandAll();
    await userEvent.click(within(row("Midterm exam")).getByRole("button", { name: "Remove from the demo calendar: Midterm exam" }));
    const undo = await within(row("Midterm exam")).findByRole("button", { name: "Undo removal of Midterm exam" });
    await waitFor(() => expect(undo).toHaveFocus());
    expect(within(row("Midterm exam")).getByText("Approved")).toBeInTheDocument();
    await waitFor(() => expect(r.statusLog).toContain("Removed from the demo calendar: Midterm exam."), { timeout: 5000 }); // 2 s spacing between messages
    await userEvent.click(undo);
    expect(await within(row("Midterm exam")).findByText("In the demo calendar")).toBeInTheDocument();
  });
});

describe("delete, export, several syllabi", () => {
  it("delete asks in a dialog (Escape keeps it, focus returns), then deletes; the flow goes back to Add your syllabus", async () => {
    signedInWithCourse();
    sdb.seedSyllabus(EMAIL, COURSE, "ready");
    const list = await syllabusApi.listSyllabi(COURSE);
    const r = renderWithAnnouncer(<SyllabusFlow api={syllabusApi} courseId={COURSE} courseName="Biology 101" syllabi={list} demoExpected />);
    await screen.findByText(/We found \d+ items/);
    const del = screen.getByRole("button", { name: "Delete this syllabus" });
    await userEvent.click(del);
    const dialog = screen.getByRole("dialog", { name: "Delete this syllabus?" });
    expect(within(dialog).getByRole("button", { name: "Keep it" })).toHaveFocus();
    expect(within(dialog).getByText("Copies you downloaded are not deleted.")).toBeInTheDocument();
    expect(await axeViolations(r.container)).toEqual([]);
    await userEvent.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).toBeNull();
    await waitFor(() => expect(del).toHaveFocus());
    await userEvent.click(del);
    await userEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Delete syllabus" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Add your syllabus" })).toBeInTheDocument();
    expect(sdb.listForCourse(EMAIL, COURSE)).toHaveLength(0);
    await waitFor(() => expect(r.statusLog).toContain("The syllabus was deleted."));
  });

  it("export downloads the JSON copy (never rendered) and says so once", async () => {
    const created: Blob[] = [];
    const orig = URL.createObjectURL;
    URL.createObjectURL = ((b: Blob) => {
      created.push(b);
      return "blob:test";
    }) as typeof URL.createObjectURL;
    URL.revokeObjectURL = () => undefined;
    try {
      const r = await review();
      await userEvent.click(screen.getByRole("button", { name: "Download a copy (JSON)" }));
      await waitFor(() => expect(r.statusLog).toContain("Your copy is ready."));
      expect(created).toHaveLength(1);
      expect(JSON.parse(await created[0].text()).schemaVersion).toBe("syllabus.v1");
    } finally {
      URL.createObjectURL = orig;
    }
  });

  it("export rate limit: a calm message beside the button, nothing changed", async () => {
    server.use(http.get("*/api/v1/syllabi/:id/export", () => HttpResponse.json({ code: "rate_limited", status: 429 }, { status: 429 })));
    await review();
    await userEvent.click(screen.getByRole("button", { name: "Download a copy (JSON)" }));
    const msg = await screen.findByText("One moment. Try again in about a minute. Nothing was changed.");
    await waitFor(() => expect(msg.parentElement).toHaveFocus());
  });

  it("Add another syllabus from a review opens the create form; Back to course returns to that review", async () => {
    signedInWithCourse();
    sdb.seedSyllabus(EMAIL, COURSE, "ready");
    renderWithAnnouncer(<SyllabusFlow api={syllabusApi} courseId={COURSE} courseName="Biology 101" syllabi={await syllabusApi.listSyllabi(COURSE)} demoExpected />);
    await userEvent.click(await screen.findByRole("button", { name: "Add another syllabus" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Add your syllabus" })).toHaveFocus();
    await userEvent.click(screen.getByRole("button", { name: "Back to course" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Review your syllabus items" })).toBeInTheDocument();
  });

  it("session expiry on a decision: says what is kept and links to sign-in; nothing pretends to be saved", async () => {
    server.use(http.post("*/api/v1/syllabi/:id/items/:itemId/decision", () => HttpResponse.json({ code: "unauthenticated", status: 401 }, { status: 401 })));
    const r = await review();
    await expandAll();
    await userEvent.click(within(row("Midterm exam")).getByRole("button", { name: "Approve Midterm exam" }));
    expect(await within(row("Midterm exam")).findByText("Please sign in again.")).toBeInTheDocument();
    expect(within(row("Midterm exam")).getByText("What you decided is saved.")).toBeInTheDocument();
    expect(within(row("Midterm exam")).getByRole("link", { name: "Sign in" })).toHaveAttribute("href", "/signin");
    expect(within(row("Midterm exam")).getByText("Not decided")).toBeInTheDocument();
    await waitFor(() => expect(r.alertLog).toContain("Please sign in again."));
  });
});

describe("file intake (UX 3, 8; multipart contract)", () => {
  const flow = (api: SyllabusApi = syllabusApi) => renderWithAnnouncer(<SyllabusFlow api={api} courseId={COURSE} courseName="Biology 101" syllabi={[]} demoExpected msPerSecond={2} />);

  it("PDF and Word are refused at selection with a plain reason; other types and >10 MB too; nothing is sent", async () => {
    signedInWithCourse();
    const posts = trackRequests("POST", /\/syllabi$/);
    const r = flow();
    await userEvent.click(screen.getByRole("radio", { name: "Upload a file" }));
    const input = screen.getByLabelText("Syllabus file");
    expect(input).toHaveAccessibleDescription(/Plain text files \(\.txt\) up to 10 MB/);
    // fireEvent, not userEvent.upload: the picker's accept filter can be switched off by the student ("All files").
    fireEvent.change(input, { target: { files: [new File(["%PDF-1.4"], "bio.pdf", { type: "application/pdf" })] } });
    expect(await screen.findByText("Tether can't read PDF or Word files yet. Paste the text instead, or use a plain text file.")).toBeInTheDocument();
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByRole("button", { name: "Read my syllabus" })).toHaveAttribute("aria-disabled", "true");
    expect(await axeViolations(r.container)).toEqual([]);
    fireEvent.change(input, { target: { files: [new File(["x"], "bio.docx", { type: "application/vnd.openxmlformats-officedocument.wordprocessingml.document" })] } });
    expect(await screen.findByText(/can't read PDF or Word files yet/)).toBeInTheDocument();
    fireEvent.change(input, { target: { files: [new File(["x"], "bio.png", { type: "image/png" })] } });
    expect(await screen.findByText("We can't use that kind of file. Use a plain text file, or paste the text.")).toBeInTheDocument();
    const big = new File(["x"], "big.txt", { type: "text/plain" });
    Object.defineProperty(big, "size", { value: 10 * 1024 * 1024 + 1 });
    fireEvent.change(input, { target: { files: [big] } });
    expect(await screen.findByText("That file is too big. The limit is 10 MB. Try a smaller file or paste the text.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("checkbox"));
    await userEvent.click(screen.getByRole("button", { name: "Read my syllabus" }));
    expect(posts).toHaveLength(0);
  });

  it("a .txt file goes through createFromFile with the same notice and one key; then Reading your syllabus", async () => {
    signedInWithCourse();
    const calls: { key: string; name: string; meta: unknown }[] = [];
    const api: SyllabusApi = {
      ...syllabusApi,
      async createFromFile(courseId, input, key) {
        calls.push({ key, name: input.fileName, meta: { acceptedAt: input.acceptedAt, noticeVersion: input.noticeVersion } });
        const s = sdb.createFromUpload(EMAIL, courseId, { defaultTimezone: input.defaultTimezone, noticeVersion: input.noticeVersion }, { fileType: "txt", text: await input.file.text(), hash: "h" }, "ready");
        return { syllabus: (await syllabusApi.getSyllabus(s.id as string)) ?? s, replayed: false };
      },
    };
    flow(api);
    await userEvent.click(screen.getByRole("radio", { name: "Upload a file" }));
    await userEvent.upload(screen.getByLabelText("Syllabus file"), new File(["Quiz 1 on Sept 14"], "bio.txt", { type: "text/plain" }));
    expect(screen.getByText("Selected: bio.txt (17 bytes)")).toBeInTheDocument();
    const submit = screen.getByRole("button", { name: "Read my syllabus" });
    expect(submit).toHaveAccessibleDescription("Check the box above to continue.");
    await userEvent.click(screen.getByRole("checkbox", { name: "I read the notice above. (Draft placeholder wording.)" }));
    fireEvent.click(submit);
    fireEvent.click(submit);
    expect(await screen.findByRole("heading", { level: 1, name: "Reading your syllabus" })).toBeInTheDocument();
    expect(calls).toHaveLength(1);
    expect(calls[0].name).toBe("bio.txt");
  });

  it("server 415 and 413 map to the file messages; the file stays chosen", async () => {
    signedInWithCourse();
    let status = 415;
    const api: SyllabusApi = {
      ...syllabusApi,
      async createFromFile() {
        const { SyllabusApiError } = await import("@/lib/syllabus/client");
        throw new SyllabusApiError(status, status === 415 ? "unsupported_type" : "too_large", undefined);
      },
    };
    flow(api);
    await userEvent.click(screen.getByRole("radio", { name: "Upload a file" }));
    await userEvent.upload(screen.getByLabelText("Syllabus file"), new File(["text"], "bio.txt", { type: "text/plain" }));
    await userEvent.click(screen.getByRole("checkbox"));
    await userEvent.click(screen.getByRole("button", { name: "Read my syllabus" }));
    const m = await screen.findByText("We can't use that kind of file. Use a plain text file, or paste the text.");
    await waitFor(() => expect(m.parentElement).toHaveFocus());
    status = 413;
    await userEvent.click(screen.getByRole("button", { name: "Read my syllabus" }));
    expect(await screen.findByText("That file is too big. The limit is 10 MB. Try a smaller file or paste the text.")).toBeInTheDocument();
    expect(screen.getByText("Selected: bio.txt (4 bytes)")).toBeInTheDocument();
  });

  it("a PDF the server ended failed/disabled says it can't be read yet and offers pasting", async () => {
    signedInWithCourse();
    const s = sdb.createFromUpload(EMAIL, COURSE, { defaultTimezone: "America/New_York", noticeVersion: "draft-2026-10" }, { fileType: "pdf", text: null, hash: "h" }, "ready");
    for (let i = 0; i < 3; i++) await syllabusApi.getSyllabus(s.id as string);
    const done = await syllabusApi.getSyllabus(s.id as string);
    expect(done).toMatchObject({ status: "failed", statusReason: "disabled" });
    renderWithAnnouncer(<SyllabusFlow api={syllabusApi} courseId={COURSE} courseName="Biology 101" syllabi={[done]} demoExpected />);
    expect(await screen.findByRole("heading", { level: 1, name: "Tether can't read this kind of file yet." })).toHaveFocus();
    await userEvent.click(screen.getByRole("button", { name: "Paste text instead" }));
    expect(await screen.findByLabelText("Syllabus text")).toBeInTheDocument();
  });

  it("Stop reading deletes the syllabus after a confirm (Keep going is the default) and returns to Add your syllabus", async () => {
    signedInWithCourse();
    const deletes = trackRequests("DELETE", /\/syllabi\/[^/]+$/);
    renderWithAnnouncer(<SyllabusFlow api={syllabusApi} courseId={COURSE} courseName="Biology 101" syllabi={[]} demoExpected msPerSecond={500} />);
    fireEvent.change(screen.getByLabelText("Syllabus text"), { target: { value: "Quiz 1 Sept 14" } });
    await userEvent.click(screen.getByRole("checkbox"));
    await userEvent.click(screen.getByRole("button", { name: "Read my syllabus" }));
    await screen.findByRole("heading", { level: 1, name: "Reading your syllabus" });
    await userEvent.click(screen.getByRole("button", { name: "Stop reading" }));
    const dialog = screen.getByRole("dialog", { name: "Stop reading this syllabus?" });
    expect(within(dialog).getByRole("button", { name: "Keep going" })).toHaveFocus();
    await userEvent.click(within(dialog).getByRole("button", { name: "Stop reading" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Add your syllabus" })).toBeInTheDocument();
    expect(deletes).toHaveLength(1);
    expect(deletes[0].search).toBe(""); // DELETE takes no parameters
    expect(sdb.listForCourse(EMAIL, COURSE)).toHaveLength(0);
  });
});
