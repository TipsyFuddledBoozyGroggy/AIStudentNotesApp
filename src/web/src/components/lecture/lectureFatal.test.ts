import { describe, expect, it } from "vitest";
import { lectureFatal } from "./useLectureSession";

/**
 * PO-20ag (T-CSRF-403): the hook's fatal mapping, extracted from useLectureSession so both paths can be checked side by
 * side. Signed out comes from the audio ticket path ending "auth" (401, or 403 csrf_failed since PO-20ag) or from the
 * hub's problem code (`unauthenticated`, `csrf_failed`). A hub `consent_required` is not signed out: the resume
 * controller treats it as fatal (status "failed"), which is "unavailable", matching the ticket path's "conflict".
 */
describe("lectureFatal (PO-20ag)", () => {
  it("audio ticket ended as auth is signed out", () => {
    expect(lectureFatal("auth", { status: "connected", lastError: null })).toBe("signed_out");
  });

  it("hub csrf_failed and unauthenticated are signed out, even while the status is failed", () => {
    expect(lectureFatal(null, { status: "failed", lastError: "csrf_failed" })).toBe("signed_out");
    expect(lectureFatal(null, { status: "failed", lastError: "unauthenticated" })).toBe("signed_out");
  });

  it("hub consent_required is unavailable, not signed out", () => {
    expect(lectureFatal(null, { status: "failed", lastError: "consent_required" })).toBe("unavailable");
  });

  it("audio conflict / rejected do not by themselves make the page fatal", () => {
    expect(lectureFatal("conflict", { status: "connected", lastError: null })).toBeNull();
    expect(lectureFatal("rejected", { status: "connected", lastError: null })).toBeNull();
  });

  it("a failed hub with another code is unavailable; a retrying one is not fatal", () => {
    expect(lectureFatal(null, { status: "failed", lastError: "not_found" })).toBe("unavailable");
    expect(lectureFatal(null, { status: "reconnecting", lastError: "connection_failed" })).toBeNull();
  });
});
