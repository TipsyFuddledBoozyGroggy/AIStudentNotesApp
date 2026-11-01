import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { syllabusApi } from "@/lib/syllabus/client";
import { server } from "@/mocks/server";
import { buildUnclaimed } from "@/mocks/syllabusData";
import * as sdb from "@/mocks/syllabusDb";
import { ROW_CONTEXT_IS_SNIPPET, ROW_OFFSET_MISMATCH, rowLongLine } from "@/mocks/unclaimedFixtures";
import { axeViolations } from "@/test/axe";
import { ACTION_COPY, EDIT_COPY, REVIEW_COPY, UNCLAIMED_COPY as U } from "./copy";
import { Review } from "./Review";
import { draftStorageKey, COURSE, EMAIL, renderWithAnnouncer, signedInWithCourse } from "./testUtils";

// PO-20b / SBV-05 (docs/ux/unclaimed-dates-list.md section 10, checklist 2 to 10, 15, 16): "Dates we noticed" on the
// Review list against the MSW mock. Synthetic text only.

const BASE = "http://localhost/api/v1";
const SID = "syl_ready";
type Wire = Record<string, unknown>;

/** Counts requests to the unclaimed-dates route (MSW request events). */
function countUnclaimedRequests() {
  const urls: string[] = [];
  const on = ({ request }: { request: Request }) => {
    if (/\/unclaimed-dates/.test(new URL(request.url).pathname)) urls.push(request.url);
  };
  server.events.on("request:start", on);
  return { urls, stop: () => server.events.removeListener("request:start", on) };
}

async function mount(opts: { rows?: Wire[]; n?: number; empty?: boolean; droppedInvalid?: number; scenario?: "ready" | "purged" } = {}) {
  const scenario = opts.scenario ?? "ready";
  const id = `syl_${scenario}`;
  signedInWithCourse();
  sdb.seedSyllabus(EMAIL, COURSE, scenario);
  sdb.seedUnclaimed(EMAIL, id, opts.rows ?? buildUnclaimed(id, opts.n ?? 12));
  if (opts.empty) sdb.find(EMAIL, id)!.items = [];
  const fetched = await syllabusApi.getSyllabus(id);
  const syllabus = opts.droppedInvalid ? { ...fetched, counts: { ...fetched.counts, droppedInvalid: opts.droppedInvalid } } : fetched;
  const r = renderWithAnnouncer(<Review api={syllabusApi} syllabus={syllabus} courseId={COURSE} />);
  await screen.findByText(opts.empty ? REVIEW_COPY.emptyNoticed : /We found \d+ items/);
  return { ...r, syllabus };
}

const section = () => document.querySelector<HTMLElement>("[data-unclaimed-section]");
const head = () => within(section()!).getByRole("button", { name: /^Dates we noticed/ });
const rows = () => Array.from(document.querySelectorAll<HTMLElement>("li[data-unclaimed-id]"));
const rowById = (id: string) => document.querySelector<HTMLElement>(`li[data-unclaimed-id="${id}"]`)!;
const liveInside = () => section()!.querySelectorAll("[aria-live],[role=status],[role=alert]");
const before = (a: Node, b: Node) => Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);

async function openList(expected = 12) {
  await userEvent.click(head());
  await waitFor(() => expect(rows()).toHaveLength(Math.min(expected, 25)));
}

describe("Dates we noticed: placement, head and fetch (checklist 2, 3)", () => {
  it("is the last child of .review-groups, after Needs a date and before the Calendar step; collapsed; nothing fetched", async () => {
    const req = countUnclaimedRequests();
    await mount();
    const groups = document.querySelector(".review-groups")!;
    expect(groups.lastElementChild).toBe(section());
    const needs = screen.getByRole("button", { name: /^Needs a date/ }).closest("h2")!;
    expect(before(needs, section()!)).toBe(true);
    expect(before(section()!, document.querySelector("[data-calendar-step]")!)).toBe(true);
    const h = head();
    expect(h.closest("h2")?.id).toBe("unclaimed-h");
    expect(h).toHaveClass("group-head");
    expect(h).toHaveAttribute("aria-expanded", "false");
    expect(h).toHaveAttribute("aria-controls", "unclaimed-list");
    expect(h).toHaveAccessibleName("Dates we noticed, 12 lines");
    expect(document.getElementById("unclaimed-list")).not.toBeNull();
    expect(screen.getByRole("heading", { level: 1 })).toHaveFocus(); // never focused on mount
    expect(req.urls).toEqual([]);
    req.stop();
  });

  it("opening sends one GET (plain loading sentence, not live), then rows with caption, one blockquote with the date in bold, and two named buttons", async () => {
    const req = countUnclaimedRequests();
    let release: () => void = () => {};
    const gate = new Promise<void>((r) => (release = r));
    const original = buildUnclaimed(SID, 12);
    server.use(
      http.get(`${BASE}/syllabi/:id/unclaimed-dates`, async () => {
        await gate;
        return HttpResponse.json({ items: original, nextCursor: null });
      }),
    );
    const r = await mount({ rows: original });
    await userEvent.click(head());
    const loading = await within(section()!).findByText(U.loading);
    expect(loading.tagName).toBe("P");
    expect(liveInside()).toHaveLength(0);
    await act(async () => release());
    await waitFor(() => expect(rows()).toHaveLength(12));
    expect(within(section()!).queryByText(U.loading)).toBeNull();
    expect(req.urls).toHaveLength(1);
    expect(new URL(req.urls[0]).searchParams.get("limit")).toBe("200");
    rows().forEach((li, i) => {
      const s = original[i].snippet as { text: string; page: number | null; context: { text: string } };
      expect(within(li).getByText(s.page ? `From your syllabus, page ${s.page}:` : "From your syllabus:")).toBeInTheDocument();
      const quotes = li.querySelectorAll("blockquote");
      expect(quotes).toHaveLength(1);
      expect(quotes[0].querySelector("strong")?.textContent).toBe(s.text);
      expect(quotes[0].textContent).toBe(s.context.text);
      expect(within(li).getByRole("button", { name: `Add as item: ${s.text}` })).toHaveAttribute("aria-expanded", "false");
      expect(within(li).getByRole("button", { name: `Hide: ${s.text}` })).toBeInTheDocument();
      expect(li.querySelector("h3")).toBeNull();
    });
    // closing and opening again does not refetch; neither does a later items re-read
    await userEvent.click(head());
    await userEvent.click(head());
    expect(req.urls).toHaveLength(1);
    expect(r.statusLog).toEqual([]);
    req.stop();
  });

  it("follows nextCursor: two requests when the mock pages the list", async () => {
    const req = countUnclaimedRequests();
    const all = buildUnclaimed(SID, 12);
    server.use(
      http.get(`${BASE}/syllabi/:id/unclaimed-dates`, ({ request }) =>
        HttpResponse.json(new URL(request.url).searchParams.get("cursor") ? { items: all.slice(5), nextCursor: null } : { items: all.slice(0, 5), nextCursor: "u5" }),
      ),
    );
    await mount({ rows: all });
    await openList(12);
    expect(req.urls).toHaveLength(2);
    req.stop();
  });
});

describe("Dates we noticed: the quote fallbacks (checklist 4)", () => {
  it("offsets that disagree with the line: the date as its own quote, then 'The line around it:' and the line; no bold", async () => {
    await mount({ rows: [ROW_OFFSET_MISMATCH] });
    await openList(1);
    const li = rows()[0];
    const quotes = li.querySelectorAll("blockquote");
    expect(Array.from(quotes).map((q) => q.textContent)).toEqual(["Nov 2", "Field trip permission slips due Nov 2; see the course site."]);
    expect(within(li).getByText("The line around it:")).toBeInTheDocument();
    expect(li.querySelector("strong")).toBeNull();
  });

  it("context equal to the date: one quote, all bold, no second caption", async () => {
    await mount({ rows: [ROW_CONTEXT_IS_SNIPPET] });
    await openList(1);
    const li = rows()[0];
    expect(li.querySelectorAll("blockquote")).toHaveLength(1);
    expect(li.querySelector("blockquote strong")?.textContent).toBe("Oct 19");
    expect(within(li).queryByText("The line around it:")).toBeNull();
    expect(within(li).getByText("From your syllabus:")).toBeInTheDocument();
  });
});

describe("Dates we noticed: fetch failure (checklist 5)", () => {
  it("a calm Note with Try again; nothing announced or focused; Try again repeats the request", async () => {
    const req = countUnclaimedRequests();
    server.use(http.get(`${BASE}/syllabi/:id/unclaimed-dates`, () => HttpResponse.json({ code: "x" }, { status: 500 }), { once: true }));
    const r = await mount();
    await userEvent.click(head());
    const msg = await within(section()!).findByText(U.loadError);
    const note = msg.closest(".note")!;
    expect(note).toHaveClass("note-notice");
    expect(liveInside()).toHaveLength(0);
    expect(head()).toHaveFocus();
    expect(r.statusLog).toEqual([]);
    expect(r.alertLog).toEqual([]);
    await userEvent.click(within(note as HTMLElement).getByRole("button", { name: U.tryAgain }));
    await waitFor(() => expect(rows()).toHaveLength(12));
    expect(within(section()!).queryByText(U.loadError)).toBeNull();
    expect(req.urls).toHaveLength(2);
    req.stop();
  });
});

describe("Dates we noticed: Hide and Undo (checklist 6)", () => {
  it("the same button becomes Undo with focus kept; the row shrinks to one line; one status each way; ids only in storage; remount hides it", async () => {
    const r = await mount();
    await openList();
    const first = rowById("unc_ready-001");
    const hide = within(first).getByRole("button", { name: "Hide: Oct 5" });
    await userEvent.click(hide);
    expect(hide).toHaveAccessibleName("Undo hiding Oct 5");
    expect(hide).toHaveTextContent("Undo");
    expect(hide).toHaveFocus();
    expect(within(first).getByText("Hidden:")).toBeInTheDocument();
    expect(first.querySelector(".item-main strong")?.textContent).toBe("Oct 5");
    expect(first.querySelector("blockquote")).toBeNull();
    expect(within(first).queryByRole("button", { name: /^Add as item/ })).toBeNull();
    expect(head()).toHaveAccessibleName("Dates we noticed, 11 lines");
    await waitFor(() => expect(r.statusLog).toEqual(["Hidden: Oct 5."]));
    const key = draftStorageKey(`unclaimed.${SID}.unc_ready-001`);
    expect(window.localStorage.getItem(key)).toBe('{"s":"hidden"}');
    const everything = Object.keys(window.localStorage).map((k) => window.localStorage.getItem(k)).join(" ");
    expect(everything).not.toMatch(/calculator|Oct 5/);
    expect(liveInside()).toHaveLength(0);

    await userEvent.click(hide);
    expect(hide).toHaveAccessibleName("Hide: Oct 5");
    expect(hide).toHaveFocus();
    expect(first.querySelector("blockquote")).not.toBeNull();
    expect(head()).toHaveAccessibleName("Dates we noticed, 12 lines");
    // The shared queue spaces status messages by 2 s (announcerQueue MIN_SPACING_MS).
    await waitFor(() => expect(r.statusLog).toEqual(["Hidden: Oct 5.", "Shown again: Oct 5."]), { timeout: 3500 });
    expect(window.localStorage.getItem(key)).toBeNull();

    // Hide again, then a new mount: not rendered; "Show hidden and added dates (1)" brings it back with focus on Undo.
    await userEvent.click(hide);
    r.unmount();
    r.stop();
    const syllabus = await syllabusApi.getSyllabus(SID);
    renderWithAnnouncer(<Review api={syllabusApi} syllabus={syllabus} courseId={COURSE} />);
    await screen.findByText(/We found \d+ items/);
    expect(head()).toHaveAccessibleName("Dates we noticed, 11 lines"); // known before the fetch
    await openList(11);
    expect(rowById("unc_ready-001")).toBeNull();
    const show = within(section()!).getByRole("button", { name: "Show hidden and added dates (1)" });
    await userEvent.click(show);
    expect(within(section()!).queryByRole("button", { name: /^Show hidden and added/ })).toBeNull();
    const back = rowById("unc_ready-001");
    expect(back).toBe(rows()[rows().length - 1]); // revealed at the end of the list
    expect(within(back).getByRole("button", { name: "Undo hiding Oct 5" })).toHaveFocus();
  });

  it("when every row is hidden the head says so", async () => {
    await mount({ n: 1 });
    await openList(1);
    await userEvent.click(within(rows()[0]).getByRole("button", { name: /^Hide:/ }));
    expect(head()).toHaveAccessibleName("Dates we noticed, all hidden or added");
  });
});

describe("Dates we noticed: Add as item (checklist 7)", () => {
  it("opens a prefilled form; an empty date is refused locally; a save sends one POST and lands the item in its group", async () => {
    const posts: { key: string | null; body: unknown }[] = [];
    server.events.on("request:start", async ({ request }) => {
      if (request.method === "POST" && /\/items$/.test(new URL(request.url).pathname)) posts.push({ key: request.headers.get("Idempotency-Key"), body: await request.clone().json() });
    });
    const r = await mount();
    await openList();
    const li = rowById("unc_ready-001");
    const add = within(li).getByRole("button", { name: "Add as item: Oct 5" });
    await userEvent.click(add);
    expect(add).toHaveAttribute("aria-expanded", "true");
    const form = within(li).getByRole("form", { name: "Add an item from: Oct 5" });
    expect(form).toHaveClass("sheet");
    const title = within(form).getByLabelText("Title");
    expect(title).toHaveValue("Bring a calculator on Oct 5 for the in-class exercise.");
    expect(title).toHaveFocus();
    expect(title).toHaveAccessibleDescription(U.form.titleHelp);
    expect(within(form).getByLabelText("Type")).toHaveValue("other");
    const date = within(form).getByLabelText("Date");
    expect(date).toHaveValue("");
    expect(date).toHaveAccessibleDescription(`${EDIT_COPY.dateHint} ${U.form.dateHelp}`);
    expect(within(form).getByRole("checkbox", { name: "All day" })).toBeChecked();
    expect(within(li).queryByRole("button", { name: /^Hide/ })).toBeNull(); // one decision at a time

    await userEvent.click(within(form).getByRole("button", { name: U.form.save }));
    expect(within(form).getByText("Enter a date like 2026-10-14.")).toBeInTheDocument();
    expect(date).toHaveFocus();
    expect(posts).toHaveLength(0);

    fireEvent.change(date, { target: { value: "2026-10-05" } });
    await userEvent.click(within(form).getByRole("button", { name: U.form.save }));
    const goTo = await within(li).findByRole("button", { name: "Go to the item Bring a calculator on Oct 5 for the in-class exercise." });
    await waitFor(() => expect(goTo).toHaveFocus());
    expect(within(li).getByText(/^Added as an item:/)).toHaveTextContent("Added as an item: Bring a calculator on Oct 5 for the in-class exercise.");
    expect(li.querySelector("blockquote")).toBeNull();
    expect(posts).toHaveLength(1);
    expect(posts[0].key).toMatch(/^[A-Za-z0-9_-]{8,128}$/);
    expect(posts[0].body).toEqual({ kind: "other", title: "Bring a calculator on Oct 5 for the in-class exercise.", date: "2026-10-05", allDay: true, time: null });
    await waitFor(() => expect(r.statusLog).toEqual(["Added as an item: Bring a calculator on Oct 5 for the in-class exercise. It is not decided yet."]));
    expect(screen.getByText("We found 12 items. 0 approved, 0 skipped, 12 not decided.")).toBeInTheDocument();
    const week = screen.getByRole("button", { name: /^Week of Oct 5 to 11/ });
    expect(week).toHaveAttribute("aria-expanded", "true");
    const item = screen.getByRole("listitem", { name: "Bring a calculator on Oct 5 for the in-class exercise." });
    expect(within(item).getByText("Not decided")).toBeInTheDocument();
    expect(within(item).queryByRole("button", { name: "See where this came from" })).toBeNull();
    expect(head()).toHaveAccessibleName("Dates we noticed, 11 lines");
    expect(window.localStorage.getItem(draftStorageKey(`unclaimed.${SID}.unc_ready-001`))).toMatch(/^\{"s":"added","itemId":"itm_[a-z0-9-]+"\}$/);

    await userEvent.click(goTo);
    await waitFor(() => expect(within(item).getByRole("button", { name: "Approve Bring a calculator on Oct 5 for the in-class exercise." })).toHaveFocus());
    expect(liveInside()).toHaveLength(0);
    server.events.removeAllListeners();
  });

  it("Escape and Cancel close the form, drop the draft and return focus to Add as item", async () => {
    await mount();
    await openList();
    const li = rowById("unc_ready-002");
    const add = within(li).getByRole("button", { name: "Add as item: Nov 2" });
    await userEvent.click(add);
    await userEvent.type(within(li).getByLabelText("Title"), " x");
    expect(window.localStorage.getItem(draftStorageKey(`unclaimed-add.${SID}.unc_ready-002`))).not.toBeNull();
    await userEvent.keyboard("{Escape}");
    expect(within(li).queryByRole("form")).toBeNull();
    expect(add).toHaveFocus();
    expect(window.localStorage.getItem(draftStorageKey(`unclaimed-add.${SID}.unc_ready-002`))).toBeNull();
    await userEvent.click(add);
    await userEvent.click(within(li).getByRole("button", { name: U.form.cancel }));
    expect(add).toHaveFocus();
    expect(within(li).getByRole("button", { name: "Hide: Nov 2" })).toBeInTheDocument();
  });

  it("a draft survives a remount: the row stays closed until Add as item, which restores it with 'We kept your changes.'", async () => {
    const r = await mount();
    await openList();
    let li = rowById("unc_ready-003");
    await userEvent.click(within(li).getByRole("button", { name: "Add as item: Oct 19" }));
    const t = within(li).getByLabelText("Title");
    await userEvent.clear(t);
    await userEvent.type(t, "Museum talk");
    r.unmount();
    r.stop();
    renderWithAnnouncer(<Review api={syllabusApi} syllabus={await syllabusApi.getSyllabus(SID)} courseId={COURSE} />);
    await screen.findByText(/We found \d+ items/);
    await openList();
    li = rowById("unc_ready-003");
    const add = within(li).getByRole("button", { name: "Add as item: Oct 19" });
    expect(add).toHaveAttribute("aria-expanded", "false");
    expect(within(li).queryByRole("form")).toBeNull();
    await userEvent.click(add);
    expect(within(li).getByText(EDIT_COPY.kept)).toBeInTheDocument();
    expect(within(li).getByLabelText("Title")).toHaveValue("Museum talk");
  });

  it("the prefilled title collapses whitespace and is cut at the last space before 200 code points", async () => {
    await mount({ rows: [rowLongLine()] });
    await openList(1);
    await userEvent.click(within(rows()[0]).getByRole("button", { name: "Add as item: Dec 12" }));
    const v = (within(rows()[0]).getByLabelText("Title") as HTMLInputElement).value;
    expect([...v].length).toBeLessThanOrEqual(200);
    expect(v).toBe("Long line ".repeat(20).trim());
  });
});

describe("Dates we noticed: create errors (checklist 8)", () => {
  async function openForm() {
    const r = await mount();
    await openList();
    const li = rowById("unc_ready-001");
    await userEvent.click(within(li).getByRole("button", { name: "Add as item: Oct 5" }));
    fireEvent.change(within(li).getByLabelText("Date"), { target: { value: "2026-10-05" } });
    return { r, li, save: () => userEvent.click(within(li).getByRole("button", { name: U.form.save })) };
  }
  const itemsCount = () => screen.getByText(/^We found \d+ items/).textContent;

  it.each([
    [400, "validation_failed", EDIT_COPY.errors.server],
    [409, "invalid_state", ACTION_COPY.generic], // not-ready and the 100-student-item cap share this code on the server
    [429, "rate_limited", ACTION_COPY.rateLimited],
  ])("%i %s: a sentence inside the form, focus on Title, values kept, nothing inserted", async (status, code, text) => {
    server.use(http.post(`${BASE}/syllabi/:id/items`, () => HttpResponse.json({ status, code, title: "x", type: "x", traceId: "t", detail: "SERVER DETAIL" }, { status })));
    const { li, save } = await openForm();
    const count = itemsCount();
    await save();
    expect(await within(li).findByText(text)).toHaveClass("font-semibold");
    expect(within(li).getByLabelText("Title")).toHaveFocus();
    expect(within(li).getByLabelText("Date")).toHaveValue("2026-10-05");
    expect(within(li).queryByText(/SERVER DETAIL/)).toBeNull();
    expect(itemsCount()).toBe(count);
    expect(liveInside()).toHaveLength(0);
  });

  it("network: values and the Idempotency-Key are kept for the retry; an answered error discards the key", async () => {
    const keys: (string | null)[] = [];
    let mode: "network" | "400" | "ok" = "network";
    server.use(
      http.post(`${BASE}/syllabi/:id/items`, ({ request }) => {
        keys.push(request.headers.get("Idempotency-Key"));
        if (mode === "network") return HttpResponse.error();
        if (mode === "400") return HttpResponse.json({ code: "validation_failed" }, { status: 400 });
        return undefined; // fall through to the mock handler
      }),
    );
    const { li, save } = await openForm();
    await save();
    expect(await within(li).findByText(ACTION_COPY.network)).toBeInTheDocument();
    await save();
    await waitFor(() => expect(keys).toHaveLength(2));
    await within(li).findByText(ACTION_COPY.network);
    expect(keys[1]).toBe(keys[0]);
    expect(within(li).getByLabelText("Date")).toHaveValue("2026-10-05");
    mode = "400";
    await save();
    await within(li).findByText(EDIT_COPY.errors.server);
    expect(keys).toHaveLength(3);
    expect(keys[2]).toBe(keys[1]); // the same body after a network failure: same key
    mode = "ok";
    await save();
    await within(li).findByRole("button", { name: /^Go to the item/ });
    expect(keys[3]).not.toBe(keys[2]); // the 400 was an answer: a fresh key
  });

  it("401: the session problem with Sign in, one alert, nothing inserted", async () => {
    server.use(http.post(`${BASE}/syllabi/:id/items`, () => HttpResponse.json({ code: "unauthenticated" }, { status: 401 })));
    const { r, li, save } = await openForm();
    const count = itemsCount();
    await save();
    expect(await within(li).findByText(ACTION_COPY.sessionHeading)).toBeInTheDocument();
    expect(within(li).getByRole("link", { name: ACTION_COPY.signIn })).toHaveAttribute("href", "/signin");
    await waitFor(() => expect(r.alertLog).toEqual([ACTION_COPY.sessionHeading]));
    expect(itemsCount()).toBe(count);
  });
});

describe("Dates we noticed: reconciliation and paging (checklist 9, 10)", () => {
  it("an 'added' state whose item is gone: the row is shown in full and the stale key is removed", async () => {
    signedInWithCourse();
    window.localStorage.setItem(draftStorageKey(`unclaimed.${SID}.unc_ready-002`), JSON.stringify({ s: "added", itemId: "itm_gone" }));
    await mount();
    expect(head()).toHaveAccessibleName("Dates we noticed, 12 lines");
    expect(window.localStorage.getItem(draftStorageKey(`unclaimed.${SID}.unc_ready-002`))).toBeNull();
    await openList();
    expect(within(rowById("unc_ready-002")).getByRole("button", { name: "Add as item: Nov 2" })).toBeInTheDocument();
    expect(within(section()!).queryByRole("button", { name: /^Show hidden and added/ })).toBeNull();
  });

  it("200 rows: 25 at a time; seven presses show all; then focus is on row 176's Add as item; Expand/Collapse all leave the section alone", async () => {
    const all = buildUnclaimed(SID, 200);
    await mount({ rows: all });
    expect(head()).toHaveAccessibleName("Dates we noticed, 200 lines");
    await userEvent.click(screen.getByRole("button", { name: "Expand all" }));
    expect(head()).toHaveAttribute("aria-expanded", "false");
    await openList(200);
    expect(rows()).toHaveLength(25);
    for (let press = 1; press <= 7; press++) {
      const more = within(section()!).getByRole("button", { name: "Show 25 more" });
      await userEvent.click(more);
      expect(rows()).toHaveLength(25 + press * 25);
      if (press < 7) expect(more).toHaveFocus();
    }
    expect(within(section()!).queryByRole("button", { name: /^Show \d+ more/ })).toBeNull();
    const s176 = all[175].snippet as { text: string };
    await waitFor(() => expect(within(rows()[175]).getByRole("button", { name: `Add as item: ${s176.text}` })).toHaveFocus());
    await userEvent.click(screen.getByRole("button", { name: "Collapse all" }));
    expect(head()).toHaveAttribute("aria-expanded", "true");
  });

  it("the last batch can be smaller than 25", async () => {
    await mount({ n: 30 });
    await openList(30);
    await userEvent.click(within(section()!).getByRole("button", { name: "Show 5 more" }));
    expect(rows()).toHaveLength(30);
  });
});

describe("Dates we noticed: live regions and axe (checklist 15, 16)", () => {
  it("no live region inside the section and no axe violations: closed, open with 25 rows, a form open, a hidden row, an added row", async () => {
    const { container } = await mount({ n: 40 });
    expect(liveInside()).toHaveLength(0);
    expect(await axeViolations(container)).toEqual([]);
    await openList(40);
    expect(rows()).toHaveLength(25);
    expect(await axeViolations(container)).toEqual([]);
    await userEvent.click(within(rowById("unc_ready-002")).getByRole("button", { name: /^Hide:/ }));
    const li = rowById("unc_ready-001");
    await userEvent.click(within(li).getByRole("button", { name: /^Add as item:/ }));
    expect(liveInside()).toHaveLength(0);
    expect(await axeViolations(container)).toEqual([]);
    fireEvent.change(within(li).getByLabelText("Date"), { target: { value: "2026-10-05" } });
    await userEvent.click(within(li).getByRole("button", { name: U.form.save }));
    await within(li).findByRole("button", { name: /^Go to the item/ });
    expect(liveInside()).toHaveLength(0);
    expect(await axeViolations(container)).toEqual([]);
  });
});
