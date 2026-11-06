import { ApiError } from "@/lib/api/errors";
import { DELETE_PROBLEM_COPY as P } from "./deleteCopy";
import { markDeleted, runDelete, takeDeleted } from "./deleteFlow";

const err = (status: number, code = "x") => new ApiError(status, code, "server detail that must never be shown");
const ok = () => Promise.resolve();
const fail = (e: unknown) => () => Promise.reject(e);

describe("runDelete (draft 6.3)", () => {
  it("204 is deleted; the probe is not called", async () => {
    const probe = vi.fn();
    expect(await runDelete(ok, probe)).toEqual({ kind: "deleted" });
    expect(probe).not.toHaveBeenCalled();
  });

  it.each([
    [404, { kind: "gone" }],
    [401, { kind: "problem", text: P.session }],
    [429, { kind: "problem", text: P.rate }],
    [409, { kind: "problem", text: P.conflict }],
    [403, { kind: "problem", text: P.generic }],
    [400, { kind: "problem", text: P.generic }],
  ])("%i maps to its row and never shows the server's text", async (status, want) => {
    const probe = vi.fn();
    expect(await runDelete(fail(err(status)), probe)).toEqual(want);
    expect(probe).not.toHaveBeenCalled();
  });

  it("network or 5xx: the resource is read once; still there means nothing was deleted", async () => {
    for (const e of [err(0, "network_error"), err(500), err(503)]) {
      const probe = vi.fn(ok);
      expect(await runDelete(fail(e), probe)).toEqual({ kind: "problem", text: P.network });
      expect(probe).toHaveBeenCalledTimes(1);
    }
  });

  it("network failure but the read says 404: the delete went through (the answer was lost), so it is gone, not 'Nothing was deleted'", async () => {
    expect(await runDelete(fail(err(0, "network_error")), fail(err(404)))).toEqual({ kind: "gone" });
  });

  it("network failure and the read fails too: the network line", async () => {
    expect(await runDelete(fail(err(0, "network_error")), fail(err(0, "network_error")))).toEqual({ kind: "problem", text: P.network });
  });

  it("a non-API exception is the generic line", async () => {
    expect(await runDelete(fail(new TypeError("boom")), ok)).toEqual({ kind: "problem", text: P.generic });
  });
});

describe("the one-shot marker", () => {
  afterEach(() => window.sessionStorage.clear());

  it("is read once, by the page it was written for", () => {
    markDeleted({ kind: "lecture", courseId: "c1", note: "Deleted: Week 3.", announce: "The lecture was deleted." });
    expect(takeDeleted({ kind: "lecture", courseId: "c1" })).toEqual({ kind: "lecture", courseId: "c1", note: "Deleted: Week 3.", announce: "The lecture was deleted." });
    expect(takeDeleted({ kind: "lecture", courseId: "c1" })).toBeNull();
  });

  it("is never shown on another page, and is removed when another page reads it", () => {
    markDeleted({ kind: "lecture", courseId: "c1", note: "n", announce: "a" });
    expect(takeDeleted({ kind: "lecture", courseId: "c2" })).toBeNull();
    expect(takeDeleted({ kind: "lecture", courseId: "c1" })).toBeNull();
    markDeleted({ kind: "course", note: "n", announce: "a" });
    expect(takeDeleted({ kind: "lecture", courseId: "c1" })).toBeNull();
    markDeleted({ kind: "lecture", courseId: "c1", note: "n", announce: "a" });
    expect(takeDeleted({ kind: "course" })).toBeNull();
  });

  it("ignores a malformed value", () => {
    window.sessionStorage.setItem("tether.deleted.v1", "{not json");
    expect(takeDeleted({ kind: "course" })).toBeNull();
    window.sessionStorage.setItem("tether.deleted.v1", JSON.stringify({ kind: "course", note: 3 }));
    expect(takeDeleted({ kind: "course" })).toBeNull();
  });
});
