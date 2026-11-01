import { ApiError } from "@/lib/api/errors";
import { isExportOf, isId, parseBatchResult, parseItem, parseItemPage, parseRemoveResult, parseSyllabus, parseSyllabusPage, parseSyncResult, parseUnclaimedPage } from "./parse";
import { batchDecisionBody, calendarRemoveBody, calendarSyncBody, createFromTextBody, createMetadataBody, decisionBody, isIdempotencyKey, itemCreateBody, patchBody } from "./requests";
import type {
  BatchAction,
  BatchResult,
  CreateFromFileInput,
  CreateFromTextInput,
  Decision,
  ItemCreate,
  ItemPatch,
  Page,
  RemoveResult,
  Syllabus,
  SyllabusItem,
  SyncResult,
  UnclaimedDate,
} from "./types";

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
  /** Batch (map-keyed by item id, 1 to 100). Per-item outcomes; never full success when any item was rejected. */
  decideBatch(id: string, action: BatchAction, itemEtags: ReadonlyMap<string, string>): Promise<BatchResult>;
  /** POST multipart (one `metadata` part, one `file` part). Same Idempotency-Key rule as createFromText. */
  createFromFile(courseId: string, input: CreateFromFileInput, idempotencyKey: string): Promise<Created>;
  /** DELETE /syllabi/{id}: no parameters. Cancels a running read and removes everything, demo calendar events included. */
  deleteSyllabus(id: string): Promise<void>;
  /** Mock calendar sync. One Idempotency-Key per press; reuse it only to retry the same body after a network failure. */
  syncCalendar(id: string, itemRevisions: ReadonlyMap<string, number>, idempotencyKey: string): Promise<{ result: SyncResult; replayed: boolean }>;
  /** Removes demo calendar events; the items stay approved. */
  removeFromCalendar(id: string, itemIds: readonly string[]): Promise<RemoveResult>;
  /** The export body as text (downloaded, never rendered), after checking it is this syllabus's syllabus.v1 export. */
  exportSyllabus(id: string): Promise<string>;
  /**
   * GET /syllabi/{id}/unclaimed-dates, following nextCursor with limit=200 (at most 2 pages). More rows than the 200 cap in
   * total is refused as malformed rather than shown cut short.
   */
  listUnclaimedDates(id: string): Promise<UnclaimedDate[]>;
  /** POST /syllabi/{id}/items (a student item). One Idempotency-Key per press; reuse it only to retry the same body. */
  createItem(id: string, input: ItemCreate, idempotencyKey: string): Promise<SyllabusItem>;
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
          // JSON bodies only. A FormData body gets its multipart boundary from the browser, so no Content-Type here.
          ...(typeof init.body === "string" ? { "Content-Type": "application/json" } : {}),
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
    async createFromFile(courseId, input, idempotencyKey) {
      requireId(courseId);
      if (!isIdempotencyKey(idempotencyKey)) throw new SyllabusApiError(400, "validation_failed", undefined);
      const form = new FormData();
      form.append("metadata", JSON.stringify(createMetadataBody(input)));
      form.append("file", input.file, input.fileName);
      const res = await request(`/courses/${enc(courseId)}/syllabi`, { method: "POST", headers: { "Idempotency-Key": idempotencyKey }, body: form });
      const syllabus = await json(res, parseSyllabus);
      return { syllabus, replayed: res.headers.get("Idempotency-Replayed") === "true" };
    },
    async deleteSyllabus(id) {
      requireId(id);
      await request(`/syllabi/${enc(id)}`, { method: "DELETE" });
    },
    async syncCalendar(id, itemRevisions, idempotencyKey) {
      requireId(id, ...itemRevisions.keys());
      if (!isIdempotencyKey(idempotencyKey)) throw new SyllabusApiError(400, "validation_failed", undefined);
      const res = await request(`/syllabi/${enc(id)}/calendar-sync`, {
        method: "POST",
        headers: { "Idempotency-Key": idempotencyKey },
        body: JSON.stringify(calendarSyncBody(itemRevisions)),
      });
      const result = await json(res, parseSyncResult);
      return { result, replayed: res.headers.get("Idempotency-Replayed") === "true" };
    },
    async removeFromCalendar(id, itemIds) {
      requireId(id, ...itemIds);
      const res = await request(`/syllabi/${enc(id)}/calendar-sync/remove`, { method: "POST", body: JSON.stringify(calendarRemoveBody(itemIds)) });
      return json(res, parseRemoveResult);
    },
    async exportSyllabus(id) {
      requireId(id);
      const res = await request(`/syllabi/${enc(id)}/export`);
      let text: string;
      try {
        text = await res.text();
      } catch {
        throw new SyllabusParseError();
      }
      let body: unknown;
      try {
        body = JSON.parse(text);
      } catch {
        throw new SyllabusParseError();
      }
      if (!isExportOf(body, id)) throw new SyllabusParseError();
      return text;
    },
    async listUnclaimedDates(id) {
      requireId(id);
      const out: UnclaimedDate[] = [];
      let cursor: string | null = null;
      for (let i = 0; i < 2; i++) {
        const q = new URLSearchParams({ limit: "200", ...(cursor ? { cursor } : {}) });
        const page: Page<UnclaimedDate> = await json(await request(`/syllabi/${enc(id)}/unclaimed-dates?${q}`), parseUnclaimedPage);
        out.push(...page.items);
        cursor = page.nextCursor;
        if (!cursor) break;
      }
      if (out.length > 200) throw new SyllabusParseError();
      return out;
    },
    async createItem(id, input, idempotencyKey) {
      requireId(id);
      if (!isIdempotencyKey(idempotencyKey)) throw new SyllabusApiError(400, "validation_failed", undefined);
      const res = await request(`/syllabi/${enc(id)}/items`, {
        method: "POST",
        headers: { "Idempotency-Key": idempotencyKey },
        body: JSON.stringify(itemCreateBody(input)),
      });
      return json(res, parseItem);
    },
  };
}

/** Singleton for the app (same-origin base URL, ADR-006). */
export const syllabusApi = createSyllabusApi({ baseUrl: process.env.NEXT_PUBLIC_API_BASE_URL ?? "/api/v1" });
