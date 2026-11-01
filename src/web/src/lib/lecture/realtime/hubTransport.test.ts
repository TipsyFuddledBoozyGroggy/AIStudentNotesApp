import { HttpError } from "@microsoft/signalr";
import { describe, expect, it } from "vitest";
import { hubErrorCode } from "./hubTransport";

/**
 * PO-20ag (T-CSRF-403): the live-session path reads the problem code too. A refused negotiate / upgrade arrives as a
 * SignalR HttpError whose message is the response body (FetchHttpClient and XhrHttpClient both put the body text there),
 * which for this API is the ProblemDetails JSON written by ApiProblemResult (`code` field). The server's only 403 on
 * /hubs is RealtimeOriginMiddleware's `csrf_failed`.
 */
const problem = (status: number, code: string) =>
  JSON.stringify({ type: `https://errors.tether.invalid/${code}`, title: "t", status, code, traceId: "x" });

describe("hubErrorCode reads the problem code of a refused negotiate (PO-20ag)", () => {
  it("403 with a csrf_failed body is csrf_failed (signed out downstream)", () => {
    expect(hubErrorCode(new HttpError(problem(403, "csrf_failed"), 403))).toBe("csrf_failed");
  });

  it("403 with a consent_required body is consent_required, not csrf_failed", () => {
    expect(hubErrorCode(new HttpError(problem(403, "consent_required"), 403))).toBe("consent_required");
  });

  it("403 with no body, a non-JSON body or a code outside the contract keeps the current mapping (csrf_failed)", () => {
    expect(hubErrorCode(new HttpError("Forbidden", 403))).toBe("csrf_failed");
    expect(hubErrorCode(new HttpError("", 403))).toBe("csrf_failed");
    expect(hubErrorCode(new HttpError(problem(403, "forbidden"), 403))).toBe("csrf_failed");
    expect(hubErrorCode(new HttpError('{"code":42}', 403))).toBe("csrf_failed");
  });

  it("other statuses are unchanged", () => {
    expect(hubErrorCode(new HttpError(problem(401, "unauthenticated"), 401))).toBe("unauthenticated");
    expect(hubErrorCode(new HttpError(problem(404, "not_found"), 404))).toBe("not_found");
    expect(hubErrorCode(new HttpError("Bad Gateway", 502))).toBe("connection_failed");
  });

  it("hub invocation errors still end with the code", () => {
    expect(hubErrorCode(new Error("An unexpected error occurred invoking 'Join' on the server. HubException: rate_limited"))).toBe("rate_limited");
    expect(hubErrorCode(new Error("boom"))).toBe("connection_failed");
  });
});
