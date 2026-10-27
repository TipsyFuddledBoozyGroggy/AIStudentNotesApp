import type { ProblemDetails } from "./types";

export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly fieldErrors: Record<string, string[]>;
  readonly traceId?: string;

  constructor(status: number, code: string, message: string, problem?: ProblemDetails) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
    this.fieldErrors = problem?.errors ?? {};
    this.traceId = problem?.traceId;
  }

  get isUnauthenticated(): boolean {
    return this.status === 401;
  }
  get isNotFound(): boolean {
    return this.status === 404;
  }
  /** Fetch itself failed (offline, DNS, server down). */
  get isNetwork(): boolean {
    return this.status === 0;
  }
}
