import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { syllabusApi } from "@/lib/syllabus/client";
import { server } from "@/mocks/server";
import * as sdb from "@/mocks/syllabusDb";
import { axeViolations } from "@/test/axe";
import { cardContent, SyllabusCard } from "./SyllabusCard";
import { SyllabusFlow } from "./SyllabusFlow";
import { COURSE, EMAIL, renderWithAnnouncer, signedInWithCourse } from "./testUtils";

const flow = (syllabi = [] as Awaited<ReturnType<typeof syllabusApi.listSyllabi>>) =>
  renderWithAnnouncer(<SyllabusFlow api={syllabusApi} courseId={COURSE} courseName="Biology 101" syllabi={syllabi} demoExpected msPerSecond={2} />);

function trackPosts() {
  const keys: (string | null)[] = [];
  server.events.on("request:start", ({ request }) => {
    if (request.method === "POST" && /\/courses\/[^/]+\/syllabi$/.test(new URL(request.url).pathname)) keys.push(request.headers.get("Idempotency-Key"));
  });
  return keys;
}

async function fillAndAttest(text = "Week 1: Quiz 1 due Sept 14.") {
  fireEvent.change(screen.getByLabelText("Syllabus text"), { target: { value: text } });
  await userEvent.click(screen.getByRole("checkbox", { name: "I read the notice above. (Draft placeholder wording.)" }));
}

afterEach(() => server.events.removeAllListeners());

describe("create (paste text)", () => {
  it("h1 has focus; Read my syllabus is aria-disabled (still focusable) with a reason until text and the box are set", async () => {
    signedInWithCourse();
    const { container } = flow();
    expect(screen.getByRole("heading", { level: 1, name: "Add your syllabus" })).toHaveFocus();
    expect(screen.getByText("Demo mode: items are found by a test routine, not a real AI model.")).toBeInTheDocument();
    expect(screen.getByText("DRAFT placeholder. The notice for adding a syllabus is not written yet. The Product Owner and counsel must review it before real use.")).toBeInTheDocument();
    const submit = screen.getByRole("button", { name: "Read my syllabus" });
    expect(submit).toHaveAttribute("aria-disabled", "true");
    expect(submit).not.toBeDisabled();
    expect(submit).toHaveAccessibleDescription("Paste your syllabus text first.");
    fireEvent.change(screen.getByLabelText("Syllabus text"), { target: { value: "Quiz 1 Sept 14" } });
    expect(submit).toHaveAccessibleDescription("Check the box above to continue.");
    await userEvent.click(submit); // aria-disabled: nothing is sent
    expect(sdb.listForCourse(EMAIL, COURSE)).toHaveLength(0);
    await userEvent.click(screen.getByRole("checkbox"));
    expect(submit).not.toHaveAttribute("aria-disabled");
    expect(await axeViolations(container)).toEqual([]);
  });

  it("a double press creates exactly one syllabus, with one Idempotency-Key", async () => {
    signedInWithCourse();
    const keys = trackPosts();
    flow();
    await fillAndAttest();
    const submit = screen.getByRole("button", { name: "Read my syllabus" });
    fireEvent.click(submit);
    fireEvent.click(submit);
    await screen.findByRole("heading", { level: 1, name: "Reading your syllabus" });
    expect(sdb.listForCourse(EMAIL, COURSE)).toHaveLength(1);
    expect(keys).toHaveLength(1);
    expect(keys[0]).toMatch(/^[A-Za-z0-9_-]{8,128}$/);
  });

  it("a network failure keeps the text and a retry reuses the same key (no second syllabus)", async () => {
    signedInWithCourse();
    const keys = trackPosts();
    server.use(http.post("*/api/v1/courses/:courseId/syllabi", () => HttpResponse.error(), { once: true }));
    flow();
    await fillAndAttest();
    await userEvent.click(screen.getByRole("button", { name: "Read my syllabus" }));
    const msg = await screen.findByText("We could not reach the server. Your text is kept. Try again.");
    expect(msg.parentElement).toHaveFocus();
    expect(screen.getByLabelText("Syllabus text")).toHaveValue("Week 1: Quiz 1 due Sept 14.");
    await userEvent.click(screen.getByRole("button", { name: "Read my syllabus" }));
    await screen.findByRole("heading", { level: 1, name: "Reading your syllabus" });
    expect(keys).toHaveLength(2);
    expect(keys[1]).toBe(keys[0]);
    expect(sdb.listForCourse(EMAIL, COURSE)).toHaveLength(1);
  });

  it("server refusals are calm and say nothing changed (413, 429, duplicate with Open it)", async () => {
    signedInWithCourse();
    flow();
    await fillAndAttest("mock:too-large");
    await userEvent.click(screen.getByRole("button", { name: "Read my syllabus" }));
    expect(await screen.findByText(/That's a lot of text\./)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Syllabus text"), { target: { value: "mock:rate-limited" } });
    await userEvent.click(screen.getByRole("button", { name: "Read my syllabus" }));
    expect(await screen.findByText("One moment. Try again in about a minute. Nothing was changed.")).toBeInTheDocument();
  });

  it("duplicate text offers Open it, which shows the existing syllabus", async () => {
    signedInWithCourse();
    const existing = sdb.createFromPaste(EMAIL, COURSE, { text: "Same text", defaultTimezone: "America/New_York", noticeVersion: "draft-2026-10" }, "ready");
    const e = sdb.find(EMAIL, existing.id as string)!;
    e.stepsLeft = 1;
    sdb.advance(e); // finished
    flow();
    await fillAndAttest("Same text");
    await userEvent.click(screen.getByRole("button", { name: "Read my syllabus" }));
    expect(await screen.findByText("This syllabus is already in this course. Nothing new was added.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Open it" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Review your syllabus items" })).toBeInTheDocument();
  });

  it("the pasted draft survives a reload: 'We kept what you pasted.'", async () => {
    signedInWithCourse();
    const r = flow();
    fireEvent.change(screen.getByLabelText("Syllabus text"), { target: { value: "Midterm Oct 14" } });
    await waitFor(() => expect(window.localStorage.getItem(`tether.syllabus.draft.${COURSE}`)).toContain("Midterm Oct 14"));
    r.unmount();
    flow();
    expect(await screen.findByText("We kept what you pasted.")).toBeInTheDocument();
    expect(screen.getByLabelText("Syllabus text")).toHaveValue("Midterm Oct 14");
    expect(screen.getByLabelText("Syllabus text")).toHaveAccessibleDescription(/We kept what you pasted\./);
  });

  it("the term dates are optional, collapsed under Add options, and sent as termStart and termEnd when both are given", async () => {
    signedInWithCourse();
    flow();
    await fillAndAttest();
    expect(screen.getByText("Add options").closest("details")).not.toHaveAttribute("open");
    await userEvent.click(screen.getByText("Add options"));
    fireEvent.change(screen.getByLabelText("First day of classes (optional)"), { target: { value: "2026-08-24" } });
    fireEvent.change(screen.getByLabelText("Last day of classes (optional)"), { target: { value: "2026-12-11" } });
    await userEvent.click(screen.getByRole("button", { name: "Read my syllabus" }));
    await screen.findByRole("heading", { level: 1, name: "Reading your syllabus" });
    const [s] = sdb.listForCourse(EMAIL, COURSE);
    expect(s.context).toMatchObject({ termStart: "2026-08-24", termEnd: "2026-12-11" });
  });

  it("one term date alone is refused beside the fields (the real server needs both), nothing is sent, the text is kept", async () => {
    signedInWithCourse();
    const keys = trackPosts();
    flow();
    await fillAndAttest();
    await userEvent.click(screen.getByText("Add options"));
    const first = screen.getByLabelText("First day of classes (optional)");
    fireEvent.change(first, { target: { value: "2026-08-24" } });
    await userEvent.click(screen.getByRole("button", { name: "Read my syllabus" }));
    expect(await screen.findByText("Add both the first and last day of classes, or leave both empty.")).toBeInTheDocument();
    expect(first).toHaveFocus();
    expect(first).toHaveAttribute("aria-invalid", "true");
    expect(keys).toHaveLength(0);
    fireEvent.change(screen.getByLabelText("Last day of classes (optional)"), { target: { value: "2026-08-01" } });
    await userEvent.click(screen.getByRole("button", { name: "Read my syllabus" }));
    expect(await screen.findByText("The last day of classes is before the first day. Check both dates.")).toBeInTheDocument();
    expect(keys).toHaveLength(0);
    expect(screen.getByLabelText("Syllabus text")).toHaveValue("Week 1: Quiz 1 due Sept 14.");
  });
});

describe("processing (polling)", () => {
  it("steps are text, no spinner/percent/timer; on completion one 'Your items are ready.' and focus does not move", async () => {
    signedInWithCourse();
    const r = flow();
    await fillAndAttest();
    await userEvent.click(screen.getByRole("button", { name: "Read my syllabus" }));
    const h1 = await screen.findByRole("heading", { level: 1, name: "Reading your syllabus" });
    expect(h1).toHaveFocus();
    expect(screen.getByTestId("step-line")).toHaveTextContent(/^Step [1-3] of 3: /);
    expect(screen.queryByRole("progressbar")).toBeNull();
    expect(document.body.textContent).not.toMatch(/\d+ ?%|\d+:\d\d|seconds? (left|elapsed)/i);
    const review = await screen.findByRole("button", { name: "Review items" }, { timeout: 3000 });
    expect(h1).toHaveFocus(); // focus did not move
    expect(screen.getByRole("link", { name: "Back to course" })).toBeInTheDocument();
    await waitFor(() => expect(r.statusLog).toEqual(["Your items are ready."]));
    await new Promise((res) => setTimeout(res, 30));
    expect(r.statusLog).toEqual(["Your items are ready."]); // once
    expect(r.alertLog).toEqual([]);
    expect(await axeViolations(r.container)).toEqual([]);
    await userEvent.click(review);
    expect(await screen.findByRole("heading", { level: 1, name: "Review your syllabus items" })).toHaveFocus();
  });

  it("leave and return: an in-progress syllabus resumes polling from the server state", async () => {
    signedInWithCourse();
    sdb.seedSyllabus(EMAIL, COURSE, "processing");
    const list = await syllabusApi.listSyllabi(COURSE);
    flow(list);
    expect(await screen.findByRole("button", { name: "Review items" }, { timeout: 3000 })).toBeInTheDocument();
  });

  it("finishing without items (needs_text) offers Paste text instead, announced once", async () => {
    signedInWithCourse();
    const r = flow();
    await fillAndAttest("mock:needs-text");
    await userEvent.click(screen.getByRole("button", { name: "Read my syllabus" }));
    const paste = await screen.findByRole("button", { name: "Paste text instead" }, { timeout: 3000 });
    await waitFor(() => expect(r.statusLog).toEqual(["We couldn't read the words in this file."]));
    await userEvent.click(paste);
    expect(await screen.findByRole("heading", { level: 1, name: "Add your syllabus" })).toHaveFocus();
  });

  it("polling is bounded: repeated failures stop it and offer Check again (no silent loop)", async () => {
    signedInWithCourse();
    sdb.seedSyllabus(EMAIL, COURSE, "processing");
    const list = await syllabusApi.listSyllabi(COURSE);
    let calls = 0;
    server.use(
      http.get("*/api/v1/syllabi/:id", () => {
        calls += 1;
        return HttpResponse.error();
      }),
    );
    flow(list);
    const again = await screen.findByRole("button", { name: "Check again" }, { timeout: 3000 });
    const n = calls;
    expect(n).toBe(3);
    await new Promise((res) => setTimeout(res, 50));
    expect(calls).toBe(n);
    server.resetHandlers();
    await userEvent.click(again);
    expect(await screen.findByRole("button", { name: "Review items" }, { timeout: 3000 })).toBeInTheDocument();
  });
});

describe("arriving at an existing syllabus", () => {
  it.each([
    ["failed", "Something went wrong while reading.", "Try again"],
    ["needs-text", "We couldn't read the words in this file.", "Paste text instead"],
  ] as const)("%s shows its heading (focused) and the next step", async (scenario, heading, action) => {
    signedInWithCourse();
    sdb.seedSyllabus(EMAIL, COURSE, scenario);
    flow(await syllabusApi.listSyllabi(COURSE));
    expect(await screen.findByRole("heading", { level: 1, name: heading })).toHaveFocus();
    expect(screen.getByRole("button", { name: action })).toBeInTheDocument();
  });

  it("several syllabi: a list, newest first, each with Open; Open shows that one (F2: several per course)", async () => {
    signedInWithCourse();
    sdb.seedSyllabus(EMAIL, COURSE, "failed", "syl_old", "2026-10-01T00:00:00.000Z");
    sdb.seedSyllabus(EMAIL, COURSE, "ready", "syl_new", "2026-10-20T00:00:00.000Z");
    flow(await syllabusApi.listSyllabi(COURSE));
    expect(await screen.findByRole("heading", { level: 1, name: "Syllabi for this course" })).toHaveFocus();
    const rows = screen.getAllByRole("listitem");
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent("Pasted text · 11 items found");
    expect(rows[1]).toHaveTextContent("Pasted text · Could not be read");
    await userEvent.click(within(rows[0]).getByRole("button", { name: /^Open the syllabus added / }));
    expect(await screen.findByRole("heading", { level: 1, name: "Review your syllabus items" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "All syllabi for this course" })).toBeInTheDocument();
  });
});

describe("course page Syllabus card", () => {
  it("states follow the UX table: one sentence, one button", () => {
    const base = { status: "ready", counts: { total: 24, undecided: 24, approved: 0, skipped: 0, needsDate: 3, synced: 0, droppedInvalid: 0, unclaimedDates: 0 } };
    const s = (o: object) => ({ ...base, ...o }) as never;
    expect(cardContent(null)).toEqual({ text: "Pull exam and assignment dates out of your syllabus. You check each one before anything goes on a calendar.", button: "Add your syllabus" });
    expect(cardContent(s({ status: "extracting" })).button).toBe("See progress");
    expect(cardContent(s({})).text).toBe("We found 24 items. Open them when you want to.");
    expect(cardContent(s({ counts: { ...base.counts, approved: 6, skipped: 3 } })).text).toBe("9 of 24 decided. You can pick up where you left off.");
    expect(cardContent(s({ counts: { ...base.counts, approved: 20, skipped: 4 } })).button).toBe("View items");
    expect(cardContent(s({ counts: { ...base.counts, approved: 12, synced: 12 } })).text).toBe("12 items are in the demo calendar. Nothing was sent to Google.");
    expect(cardContent(s({ status: "failed" })).button).toBe("Try again");
  });

  it("renders below Lecture with a link to the syllabus route, no live region, no axe violations", async () => {
    signedInWithCourse();
    const { container } = render(<SyllabusCard courseId={COURSE} />);
    const link = await screen.findByRole("link", { name: "Add your syllabus" });
    expect(link).toHaveAttribute("href", `/courses/${COURSE}/syllabus`);
    expect(container.querySelector("[role=status],[role=alert],[aria-live]")).toBeNull();
    expect(await axeViolations(container)).toEqual([]);
  });

  it("shows the reading state while a syllabus is in progress", async () => {
    signedInWithCourse();
    sdb.seedSyllabus(EMAIL, COURSE, "processing");
    render(<SyllabusCard courseId={COURSE} />);
    expect(await screen.findByRole("link", { name: "See progress" })).toBeInTheDocument();
  });
});
