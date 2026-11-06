import { ApiError } from "@/lib/api/errors";
import { isDocId, isSessionId, parseDemoDoc, parseExportRecord, parsePreview, parseProblem, parseState } from "./parse";
import { isIdempotencyKey, parseConfirmBody, previewBody } from "./requests";
import type { DemoDoc, DocsConfirmBody, DocsExportRecord, DocsPreview, DocsState } from "./types";

/** A 2xx body that does not match docs.export.v1. The UI shows a calm "could not show this" state, never partial data. */
export class DocsParseError extends ApiError {
  constructor() {
    super(200, "malformed_response", "That did not work. Nothing was changed.");
    this.name = "DocsParseError";
  }
}

/**
 * ApiError plus the docs problem fields the UI acts on (`code`, `retryAfterSeconds`). Never carries server `detail` text to
 * the UI. A problem body that does not parse keeps the HTTP status and the code `http_error`.
 */
export class DocsApiError extends ApiError {
  readonly retryAfterSeconds: number | null;
  constructor(status: number, code: string, retryAfterSeconds: number | null = null) {
    super(status, code, "That did not work. Nothing was changed.");
    this.name = "DocsApiError";
    this.retryAfterSeconds = retryAfterSeconds;
  }
}

export interface ConfirmResult {
  record: DocsExportRecord;
  /** Idempotency-Replayed: true. The body is the stored record (history by design). */
  replayed: boolean;
}

export interface DocsApi {
  /** POST /sessions/{id}/docs-export/preview: writes nothing. Body `{notesId}` only. */
  preview(sessionId: string, notesId: string): Promise<DocsPreview>;
  /**
   * POST /sessions/{id}/docs-export: the explicit confirm. `idempotencyKey` is one per confirm attempt and is reused ONLY to
   * retry that attempt with the same body (F04). A provider failure is a 200 record with status "failed".
   */
  confirm(sessionId: string, body: DocsConfirmBody, idempotencyKey: string): Promise<ConfirmResult>;
  /** GET /sessions/{id}/docs-export. */
  getState(sessionId: string): Promise<DocsState>;
  /** GET /demo-docs/{docId}. 404 for a foreign, missing or deleted document. */
  getDemoDoc(docId: string): Promise<DemoDoc>;
  /** DELETE /demo-docs/{docId}. 204. */
  deleteDemoDoc(docId: string): Promise<void>;
}

export interface DocsApiOptions {
  baseUrl: string;
  fetchImpl?: typeof fetch;
}

const enc = encodeURIComponent;

export function createDocsApi({ baseUrl, fetchImpl }: DocsApiOptions): DocsApi {
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
          ...(typeof init.body === "string" ? { "Content-Type": "application/json" } : {}),
          ...init.headers,
        },
      });
    } catch {
      throw new DocsApiError(0, "network_error");
    }
    if (!res.ok) {
      let raw: unknown;
      try {
        raw = await res.json();
      } catch {
        raw = undefined;
      }
      const p = parseProblem(raw);
      const header = Number(res.headers.get("Retry-After"));
      const retryAfter = p?.retryAfterSeconds ?? (Number.isInteger(header) && header > 0 ? header : null);
      throw new DocsApiError(res.status, p?.code ?? "http_error", retryAfter);
    }
    return res;
  }

  async function json<T>(res: Response, parse: (v: unknown) => T | null): Promise<T> {
    let body: unknown;
    try {
      body = await res.json();
    } catch {
      throw new DocsParseError();
    }
    const out = parse(body);
    if (out === null) throw new DocsParseError();
    return out;
  }

  // A malformed id would be a 400 on the server; refuse it here so it never becomes part of a path (F04a: `../x`,
  // `ddc_%2e`, an upper-case or braced GUID).
  const requireSession = (id: string) => {
    if (!isSessionId(id)) throw new DocsApiError(400, "validation_failed");
  };
  const requireDoc = (id: string) => {
    if (!isDocId(id)) throw new DocsApiError(400, "validation_failed");
  };

  return {
    async preview(sessionId, notesId) {
      requireSession(sessionId);
      const res = await request(`/sessions/${enc(sessionId)}/docs-export/preview`, { method: "POST", body: JSON.stringify(previewBody(notesId)) });
      return json(res, (v) => parsePreview(v, sessionId));
    },
    async confirm(sessionId, body, idempotencyKey) {
      requireSession(sessionId);
      if (!isIdempotencyKey(idempotencyKey) || parseConfirmBody(body) === null) throw new DocsApiError(400, "validation_failed");
      const res = await request(`/sessions/${enc(sessionId)}/docs-export`, {
        method: "POST",
        headers: { "Idempotency-Key": idempotencyKey },
        body: JSON.stringify(body),
      });
      const record = await json(res, (v) => parseExportRecord(v, sessionId));
      return { record, replayed: res.headers.get("Idempotency-Replayed") === "true" };
    },
    async getState(sessionId) {
      requireSession(sessionId);
      return json(await request(`/sessions/${enc(sessionId)}/docs-export`), (v) => parseState(v, sessionId));
    },
    async getDemoDoc(docId) {
      requireDoc(docId);
      return json(await request(`/demo-docs/${enc(docId)}`), (v) => parseDemoDoc(v, docId));
    },
    async deleteDemoDoc(docId) {
      requireDoc(docId);
      await request(`/demo-docs/${enc(docId)}`, { method: "DELETE" });
    },
  };
}

/** Singleton for the app (same-origin base URL, ADR-006). */
export const docsApi = createDocsApi({ baseUrl: process.env.NEXT_PUBLIC_API_BASE_URL ?? "/api/v1" });
