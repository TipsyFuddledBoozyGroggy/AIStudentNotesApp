import { ApiError } from "@/lib/api/errors";
import { isId, parseBatchResult, parseItem, parseItemPage, parseSyllabus, parseSyllabusPage } from "./parse";
import { batchDecisionBody, createFromTextBody, decisionBody, isIdempotencyKey, patchBody } from "./requests";
import type { BatchAction, BatchResult, CreateFromTextInput, Decision, ItemPatch, Page, Syllabus, SyllabusItem } from "./types";

/** Error for a body that does not match syllabus.v1. The UI shows a calm "could not show this" state. */
export class SyllabusParseError extends ApiError {
  constructor() {
    super(200, "malformed_response", "That did not work. Nothing was changed.");
    this.name = "SyllabusParseError";
  }
}

/** ApiError plus the syllabus problem extensions the UI may act on. Never carries server `detail` text to the UI. */
export class SyllabusApiError extends ApiError {
  readonly existingSyllabusId: string | null;
  readonly retryAfterSeconds: number | null;
  constructor(status: number, code: string, problem: Record<string, unknown> | undefined) {
    const errors = problem && typeof problem.errors === "object" && problem.errors !== null ? (problem.errors as Record<string, string[]>) : undefined;
    super(status, code, "That did not work. Nothing was changed.", { status, code, errors });
    this.name = "SyllabusApiError";
    this.existingSyllabusId = isId(problem?.existingSyllabusId) ? (problem.existingSyllabusId as string) : null;
    this.retryAfterSeconds = typeof problem?.retryAfterSeconds === "number" ? problem.retryAfterSeconds : null;
  }
}

export interface Created {
  syllabus: Syllabus;
  /** Idempotency-Replayed: true. A replayed body is history; callers re-read for current state (contract 2.1). */
  replayed: boolean;
}

export interface SyllabusApi {
  /** POST /courses/{courseId}/syllabi (JSON paste). Idempotency-Key is required; reuse it only to retry the same body. */
  createFromText(courseId: string, input: CreateFromTextInput, idempotencyKey: string): Promise<Created>;
  listSyllabi(courseId: string): Promise<Syllabus[]>;
  getSyllabus(id: string): Promise<Syllabus>;
  /** Follows nextCursor; stops at `maxPages` (300 items fit in 2 pages of 200). */
  listItems(id: string, maxPages?: number): Promise<SyllabusItem[]>;
  getItem(id: string, itemId: string): Promise<SyllabusItem>;
  /** PATCH with If-Match = the etag the student saw. 412 when the item changed elsewhere. */
  updateItem(id: string, itemId: string, patch: ItemPatch, etag: string): Promise<SyllabusItem>;
  /** POST decision with If-Match. `acknowledgeCheck` is required to approve a please_check item. */
  decide(id: string, itemId: string, decision: Decision, etag: string, acknowledgeCheck?: boolean): Promise<SyllabusItem>;
  /** Batch (map-keyed by item id). Not used by the F1 UI. */
  decideBatch(id: string, action: BatchAction, itemEtags: ReadonlyMap<string, string>): Promise<BatchResult>;
}

export interface SyllabusApiOptions {
  baseUrl: string;
  fetchImpl?: typeof fetch;
}

const enc = encodeURIComponent;

export function createSyllabusApi({ baseUrl, fetchImpl }: SyllabusApiOptions): SyllabusApi {
  const base = baseUrl.replace(/\/$/, "");

  async function request(path: string, init: RequestInit = {}): Promise<Response> {
    const doFetch = fetchImpl ?? fetch;
    let res: Response;
    try {
      res = await doFetch(`${base}${path}`, {
        credentials: "include",
        ...init,
        headers: {
          Accept: "application/json, application/problem+json",
          ...(init.body ? { "Content-Type": "application/json" } : {}),
          ...init.headers,
        },
      });
    } catch {
      throw new ApiError(0, "network_error", "We could not reach the server.");
    }
    if (!res.ok) {
      let problem: Record<string, unknown> | undefined;
      try {
        const j: unknown = await res.json();
        problem = typeof j === "object" && j !== null ? (j as Record<string, unknown>) : undefined;
      } catch {
        problem = undefined;
      }
      throw new SyllabusApiError(res.status, typeof problem?.code === "string" ? problem.code : "http_error", problem);
    }
    return res;
  }

  async function json<T>(res: Response, parse: (v: unknown) => T | null): Promise<T> {
    let body: unknown;
    try {
      body = await res.json();
    } catch {
      throw new SyllabusParseError();
    }
    const out = parse(body);
    if (out === null) throw new SyllabusParseError();
    return out;
  }

  const requireId = (...ids: string[]) => {
    // A malformed id would be 400 on the server (R3-14); refuse it locally so it is never put in a URL.
    for (const i of ids) if (!isId(i)) throw new SyllabusApiError(400, "validation_failed", undefined);
  };

  return {
    async createFromText(courseId, input, idempotencyKey) {
      requireId(courseId);
      if (!isIdempotencyKey(idempotencyKey)) throw new SyllabusApiError(400, "validation_failed", undefined);
      const res = await request(`/courses/${enc(courseId)}/syllabi`, {
        method: "POST",
        headers: { "Idempotency-Key": idempotencyKey },
        body: JSON.stringify(createFromTextBody(input)),
      });
      const syllabus = await json(res, parseSyllabus);
      return { syllabus, replayed: res.headers.get("Idempotency-Replayed") === "true" };
    },
    async listSyllabi(courseId) {
      requireId(courseId);
      const out: Syllabus[] = [];
      let cursor: string | null = null;
      for (let i = 0; i < 5; i++) {
        const q = new URLSearchParams({ limit: "50", ...(cursor ? { cursor } : {}) });
        const page: Page<Syllabus> = await json(await request(`/courses/${enc(courseId)}/syllabi?${q}`), parseSyllabusPage);
        out.push(...page.items);
        cursor = page.nextCursor;
        if (!cursor) break;
      }
      return out;
    },
    async getSyllabus(id) {
      requireId(id);
      return json(await request(`/syllabi/${enc(id)}`), parseSyllabus);
    },
    async listItems(id, maxPages = 3) {
      requireId(id);
      const out: SyllabusItem[] = [];
      let cursor: string | null = null;
      for (let i = 0; i < maxPages; i++) {
        const q = new URLSearchParams({ limit: "200", ...(cursor ? { cursor } : {}) });
        const page: Page<SyllabusItem> = await json(await request(`/syllabi/${enc(id)}/items?${q}`), parseItemPage);
        out.push(...page.items);
        cursor = page.nextCursor;
        if (!cursor) break;
      }
      return out;
    },
    async getItem(id, itemId) {
      requireId(id, itemId);
      return json(await request(`/syllabi/${enc(id)}/items/${enc(itemId)}`), parseItem);
    },
    async updateItem(id, itemId, patch, etag) {
      requireId(id, itemId);
      const res = await request(`/syllabi/${enc(id)}/items/${enc(itemId)}`, {
        method: "PATCH",
        // Merge-patch semantics, sent as application/json: the server reads JSON bodies only (415 otherwise).
        headers: { "If-Match": etag },
        body: JSON.stringify(patchBody(patch)),
      });
      return json(res, parseItem);
    },
    async decide(id, itemId, decision, etag, acknowledgeCheck) {
      requireId(id, itemId);
      const res = await request(`/syllabi/${enc(id)}/items/${enc(itemId)}/decision`, {
        method: "POST",
        headers: { "If-Match": etag },
        body: JSON.stringify(decisionBody(decision, acknowledgeCheck)),
      });
      return json(res, parseItem);
    },
    async decideBatch(id, action, itemEtags) {
      requireId(id, ...itemEtags.keys());
      const res = await request(`/syllabi/${enc(id)}/decisions`, { method: "POST", body: JSON.stringify(batchDecisionBody(action, itemEtags)) });
      return json(res, parseBatchResult);
    },
  };
}

/** Singleton for the app (same-origin base URL, ADR-006). */
export const syllabusApi = createSyllabusApi({ baseUrl: process.env.NEXT_PUBLIC_API_BASE_URL ?? "/api/v1" });
