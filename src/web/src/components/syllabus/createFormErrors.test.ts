import { ApiError } from "@/lib/api/errors";
import { SyllabusApiError } from "@/lib/syllabus/client";
import { CREATE_COPY as C } from "./copy";
import { mapError } from "./CreateForm";

// docs/ops/ci-first-run-e2e-diagnosis.md 2: a 400 that names defaultTimezone must say the zone is the problem, not
// "the text or a date". Any other 400 keeps the general message.
describe("create form error mapping", () => {
  it("a 400 naming defaultTimezone becomes the zone message with the zone that was sent", () => {
    const err = new SyllabusApiError(400, "validation_failed", { errors: { defaultTimezone: ["not_allowed"] } });
    expect(mapError(err, "paste", "Etc/UTC")).toEqual({ zone: "Etc/UTC" });
    expect(mapError(err, "file", "Etc/UTC")).toEqual({ zone: "Etc/UTC" });
    expect(C.errors.zone("Etc/UTC")).toContain("Etc/UTC");
    expect(C.errors.zone("Etc/UTC")).not.toMatch(/text or a date/);
  });

  it("other 400s keep the general message", () => {
    expect(mapError(new SyllabusApiError(400, "validation_failed", { errors: { termStart: ["invalid"] } }), "paste", "Etc/UTC")).toEqual({ key: "invalid" });
    expect(mapError(new SyllabusApiError(400, "validation_failed", undefined), "paste", "Etc/UTC")).toEqual({ key: "invalid" });
  });

  it("session, network and other codes are unchanged", () => {
    expect(mapError(new ApiError(401, "unauthenticated", "x"), "paste", "Etc/UTC")).toEqual({ session: true });
    expect(mapError(new ApiError(0, "network", "x"), "paste", "Etc/UTC")).toEqual({ key: "network" });
    expect(mapError(new SyllabusApiError(429, "rate_limited", undefined), "paste", "Etc/UTC")).toEqual({ key: "rateLimited" });
  });
});
