import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { fireEvent } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { syllabusApi } from "@/lib/syllabus/client";
import * as db from "@/mocks/db";
import { server } from "@/mocks/server";
import * as sdb from "@/mocks/syllabusDb";
import { AuthProvider, useAuth } from "../AuthProvider";
import { Processing } from "./Processing";
import { Review } from "./Review";
import { clearAllDrafts, clearSyllabusDrafts, readDraft, removeDraft, setDraftOwner, writeDraft } from "./storage";
import { SyllabusFlow } from "./SyllabusFlow";
import { COURSE, draftStorageKey, EMAIL, renderWithAnnouncer, signedInWithCourse, TEST_USER_ID } from "./testUtils";

// Slice 8x (3), SB-06: pasted syllabus text and item edit drafts are keyed by the signed-in user's id and cleared on
// sign-out, on syllabus delete and after a successful create; every storage access is wrapped.

vi.mock("next/navigation", () => ({ usePathname: () => "/", useRouter: () => ({ replace: vi.fn(), push: vi.fn() }) }));

const ls = () => window.localStorage;
const keys = () => Array.from({ length: ls().length }, (_, i) => ls().key(i)!).sort();
const isStr = (v: unknown): v is string => typeof v === "string";
const OTHER = "usr_bob";

afterEach(() => {
  setDraftOwner(null);
  vi.restoreAllMocks();
});

describe("storage: user-scoped drafts", () => {
  it("drafts are written under the owner's id; with no owner nothing is read or written", () => {
    writeDraft("draft.c1", "x");
    expect(keys()).toEqual([]);
    setDraftOwner(TEST_USER_ID);
    writeDraft("draft.c1", "pasted text");
    expect(keys()).toEqual([draftStorageKey("draft.c1")]);
    expect(readDraft("draft.c1", isStr)).toBe("pasted text");
    setDraftOwner(null);
    expect(readDraft("draft.c1", isStr)).toBeNull();
  });

  it("another user never reads them: setting a new owner removes every other user's drafts and the old unscoped keys", () => {
    ls().setItem(draftStorageKey("draft.c1", OTHER), '"bob text"');
    ls().setItem(draftStorageKey("edit.s1.i1", OTHER), "{}");
    ls().setItem("tether.syllabus.draft.c1", '"legacy text"'); // before 8x
    ls().setItem("tether.syllabus.edit.s1.i1", "{}");
    ls().setItem("tether.syllabus.groupBy", '"type"'); // a view setting: kept
    ls().setItem("tether.prefs.v1", "{}"); // not a syllabus key: kept
    setDraftOwner(TEST_USER_ID);
    expect(readDraft("draft.c1", isStr)).toBeNull();
    expect(keys()).toEqual(["tether.prefs.v1", "tether.syllabus.groupBy"]);
  });

  it("an id with a dot cannot reach into another user's key space", () => {
    setDraftOwner("a.b");
    writeDraft("draft.c1", "t");
    expect(keys()).toEqual(["tether.syllabus.u.a%2Eb.draft.c1"]);
  });

  it("clearAllDrafts (sign-out) removes every user's drafts and legacy keys, keeps view settings, and forgets the owner", () => {
    setDraftOwner(TEST_USER_ID);
    writeDraft("draft.c1", "mine");
    writeDraft("edit.s1.i1", {});
    ls().setItem(draftStorageKey("draft.c2", OTHER), '"bob"');
    ls().setItem("tether.syllabus.draft.c3", '"legacy"');
    ls().setItem("tether.syllabus.mode", '"one"');
    clearAllDrafts();
    expect(keys()).toEqual(["tether.syllabus.mode"]);
    writeDraft("draft.c1", "after sign-out");
    expect(keys()).toEqual(["tether.syllabus.mode"]);
  });

  it("clearSyllabusDrafts removes that syllabus's item drafts and its course's paste draft only", () => {
    setDraftOwner(TEST_USER_ID);
    writeDraft("edit.s1.i1", {});
    writeDraft("edit.s1.i2", {});
    writeDraft("edit.s10.i1", {}); // a different syllabus whose id starts the same
    writeDraft("draft.c1", "course 1 paste");
    writeDraft("draft.c2", "course 2 paste");
    ls().setItem("tether.syllabus.edit.s1.i3", "{}"); // legacy
    clearSyllabusDrafts("s1", "c1");
    expect(keys()).toEqual([draftStorageKey("draft.c2"), draftStorageKey("edit.s10.i1")]);
  });

  it("every storage access is wrapped: a throwing localStorage never breaks a caller", () => {
    setDraftOwner(TEST_USER_ID);
    const boom = () => {
      throw new DOMException("blocked", "SecurityError");
    };
    for (const m of ["getItem", "setItem", "removeItem", "key"] as const) vi.spyOn(Storage.prototype, m).mockImplementation(boom);
    vi.spyOn(Storage.prototype, "length", "get").mockImplementation(boom);
    expect(() => writeDraft("draft.c1", "x")).not.toThrow();
    expect(readDraft("draft.c1", isStr)).toBeNull();
    expect(() => removeDraft("draft.c1")).not.toThrow();
    expect(() => clearSyllabusDrafts("s1", "c1")).not.toThrow();
    expect(() => clearAllDrafts()).not.toThrow();
    expect(() => setDraftOwner("u2")).not.toThrow();
  });
});

describe("clearing paths in the app", () => {
  function SignOutButton() {
    const { status, signOut } = useAuth();
    return status === "authenticated" ? <button onClick={() => void signOut().catch(() => undefined)}>Sign out</button> : <p>signed out</p>;
  }

  it("sign-in: AuthProvider sets the owner from /me (drafts keyed by that id; other users' removed)", async () => {
    db.signIn(EMAIL);
    const me = (await (await fetch("http://localhost/api/v1/me")).json()) as { id: string };
    ls().setItem(draftStorageKey("draft.c1", OTHER), '"bob"');
    render(
      <AuthProvider>
        <SignOutButton />
      </AuthProvider>,
    );
    await screen.findByRole("button", { name: "Sign out" });
    expect(keys()).toEqual([]);
    writeDraft("draft.c1", "mine");
    expect(keys()).toEqual([`tether.syllabus.u.${me.id}.draft.c1`]);
  });

  it.each([
    ["the server answers", false],
    ["the logout request fails", true],
  ])("sign-out clears every syllabus draft (%s)", async (_label, fail) => {
    db.signIn(EMAIL);
    if (fail) server.use(http.post("*/api/v1/auth/logout", () => HttpResponse.error()));
    render(
      <AuthProvider>
        <SignOutButton />
      </AuthProvider>,
    );
    await screen.findByRole("button", { name: "Sign out" });
    writeDraft("draft.c1", "x".repeat(50_000));
    writeDraft("edit.s1.i1", {});
    ls().setItem(draftStorageKey("draft.c9", OTHER), '"bob"');
    expect(keys()).toHaveLength(3);
    await userEvent.click(screen.getByRole("button", { name: "Sign out" }));
    await screen.findByText("signed out");
    expect(keys()).toEqual([]);
  });

  it("syllabus delete (Review): that syllabus's item drafts and the course's paste draft are removed", async () => {
    signedInWithCourse();
    sdb.seedSyllabus(EMAIL, COURSE, "ready");
    const syllabus = await syllabusApi.getSyllabus("syl_ready");
    const r = renderWithAnnouncer(<Review api={syllabusApi} syllabus={syllabus} courseId={COURSE} onDeleted={() => undefined} />);
    await screen.findByText(/We found \d+ items/);
    await userEvent.click(screen.getByRole("button", { name: "Edit Office hours" }));
    await userEvent.type(screen.getByLabelText("Title"), " draft");
    writeDraft(`draft.${COURSE}`, "a pasted draft");
    writeDraft("edit.syl_other.itm_1", {}); // another syllabus: kept
    expect(keys().filter((k) => k.includes(".edit.syl_ready."))).toHaveLength(1);
    await userEvent.click(screen.getByRole("button", { name: "Delete this syllabus" }));
    await userEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Delete syllabus" }));
    await waitFor(() => expect(keys()).toEqual([draftStorageKey("edit.syl_other.itm_1")]));
    r.stop();
  });

  it("syllabus delete when it was already gone (another tab): the drafts are removed too", async () => {
    signedInWithCourse();
    sdb.seedSyllabus(EMAIL, COURSE, "ready");
    const syllabus = await syllabusApi.getSyllabus("syl_ready");
    renderWithAnnouncer(<Review api={syllabusApi} syllabus={syllabus} courseId={COURSE} onDeleted={() => undefined} />);
    await screen.findByText(/We found \d+ items/);
    writeDraft("edit.syl_ready.itm_x", {});
    server.use(http.delete("*/api/v1/syllabi/:id", () => HttpResponse.json({ type: "about:blank", title: "Not found", status: 404, code: "not_found" }, { status: 404 })));
    await userEvent.click(screen.getByRole("button", { name: "Delete this syllabus" }));
    await userEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Delete syllabus" }));
    await waitFor(() => expect(keys()).toEqual([]));
  });

  it("Stop reading (Processing deletes the syllabus): its drafts are removed", async () => {
    signedInWithCourse();
    sdb.seedSyllabus(EMAIL, COURSE, "processing");
    const initial = await syllabusApi.getSyllabus("syl_processing");
    renderWithAnnouncer(<Processing api={syllabusApi} initial={initial} courseId={COURSE} msPerSecond={100000} onStopped={() => undefined} onReview={() => undefined} onPasteInstead={() => undefined} />);
    writeDraft("edit.syl_processing.itm_1", {});
    writeDraft(`draft.${COURSE}`, "paste");
    await userEvent.click(screen.getByRole("button", { name: "Stop reading" }));
    await userEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Stop reading" }));
    await waitFor(() => expect(keys()).toEqual([]));
  });

  it("a successful create removes the course's paste draft (the 50,000-character text does not stay)", async () => {
    signedInWithCourse();
    renderWithAnnouncer(<SyllabusFlow api={syllabusApi} courseId={COURSE} courseName="Biology 101" syllabi={[]} demoExpected msPerSecond={2} />);
    fireEvent.change(screen.getByLabelText("Syllabus text"), { target: { value: "Week 1: Quiz 1 due Sept 14." } });
    await waitFor(() => expect(ls().getItem(draftStorageKey(`draft.${COURSE}`))).toContain("Quiz 1"));
    await userEvent.click(screen.getByRole("checkbox", { name: "I read the notice above. (Draft placeholder wording.)" }));
    await act(async () => void (await userEvent.click(screen.getByRole("button", { name: "Read my syllabus" }))));
    await screen.findByRole("heading", { level: 1, name: "Reading your syllabus" });
    await new Promise((r) => setTimeout(r, 450)); // past the 400ms autosave debounce: nothing writes it back
    expect(ls().getItem(draftStorageKey(`draft.${COURSE}`))).toBeNull();
    expect(keys().filter((k) => k.includes("draft."))).toEqual([]);
  });
});
