import { isIdempotencyKey, newIdempotencyKey } from "@/lib/syllabus/requests";
import { isContentDigest, isDocId, isNotesId } from "./parse";
import type { DocsConfirmBody, DocsMode, DocsPreview } from "./types";

// Request bodies for docs.export.v1. Each builder returns exactly the members the request schema allows. The client never
// sends notes text, a title or a document (F03): only ids, the digest the preview showed and the student's choice.
// The Idempotency-Key format is byte-identical to syllabus-common (contract section 2), so the syllabus generator is reused.

export { isIdempotencyKey, newIdempotencyKey };

export function previewBody(notesId: string): { notesId: string } {
  if (!isNotesId(notesId)) throw new Error("Not a notes id.");
  return { notesId };
}

/**
 * The confirm for the preview the student is looking at. `expectedExistingDocId` / `expectedExistingVersion` come from the
 * preview's `existing` (both null when it showed none), so the confirm is bound to the document reviewed (DXD-05).
 */
export function confirmBody(preview: DocsPreview, mode: DocsMode): DocsConfirmBody {
  const ex = preview.existing;
  return {
    notesId: preview.notesId,
    contentDigest: preview.contentDigest,
    mode,
    expectedExistingVersion: mode === "create" ? null : (ex?.docRef.version ?? null),
    expectedExistingDocId: mode === "create" ? null : (ex?.docRef.docId ?? null),
    confirm: { provider: "demo_docs", acknowledgedDemo: true },
  };
}

type Obj = Record<string, unknown>;
const isObj = (v: unknown): v is Obj => typeof v === "object" && v !== null && !Array.isArray(v);

/** docs-preview-request, strictly (used by the mock handlers and the fixture tests). */
export function parsePreviewRequest(v: unknown): { notesId: string } | null {
  if (!isObj(v) || Object.keys(v).length !== 1 || !isNotesId(v.notesId)) return null;
  return { notesId: v.notesId };
}

const CONFIRM_KEYS = ["notesId", "contentDigest", "mode", "expectedExistingVersion", "expectedExistingDocId", "confirm"];

/** docs-export-request, strictly, including the create / update rule of its allOf (used by the mock handlers and tests). */
export function parseConfirmBody(v: unknown): DocsConfirmBody | null {
  if (!isObj(v) || Object.keys(v).length !== CONFIRM_KEYS.length || !CONFIRM_KEYS.every((k) => k in v)) return null;
  if (!isNotesId(v.notesId) || !isContentDigest(v.contentDigest)) return null;
  if (v.mode !== "create" && v.mode !== "update" && v.mode !== "create_another") return null;
  const ver = v.expectedExistingVersion;
  if (ver !== null && !(typeof ver === "number" && Number.isInteger(ver) && ver >= 1 && ver <= 2147483647)) return null;
  const doc = v.expectedExistingDocId;
  if (doc !== null && !isDocId(doc)) return null;
  if (v.mode === "create" ? ver !== null || doc !== null : ver === null || doc === null) return null;
  const c = v.confirm;
  if (!isObj(c) || Object.keys(c).length !== 2 || c.provider !== "demo_docs" || c.acknowledgedDemo !== true) return null;
  return {
    notesId: v.notesId,
    contentDigest: v.contentDigest,
    mode: v.mode,
    expectedExistingVersion: ver as number | null,
    expectedExistingDocId: doc as string | null,
    confirm: { provider: "demo_docs", acknowledgedDemo: true },
  };
}
