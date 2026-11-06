"use client";

import { useParams } from "next/navigation";
import { docsApi, isSessionId, type DocsApi } from "@/lib/docs";
import type { LectureNotes } from "@/lib/lecture";
import { DocsExport } from "./DocsExport";

/**
 * The Notes sheet's mount point for Save to a Demo Doc (Lead decision Q6: Notes sheet only). Shown only for Ready notes
 * (F06) and only on a lecture's own page, whose route carries the session id (`/lectures/[sessionId]`); the id must be the
 * canonical lowercase GUID before it is used in any request (DXD-12). Elsewhere (the live route, the dev harness) there is
 * no session id in the route and nothing is shown. `sessionId` and `api` are for tests.
 */
export function DocsExportEntry({ notes, sessionId, api = docsApi }: { notes: LectureNotes; sessionId?: string; api?: DocsApi }) {
  const params = useParams<{ sessionId?: string }>();
  const sid = sessionId ?? params?.sessionId;
  if (notes.status !== "Ready" || notes.sections.length === 0 || !isSessionId(sid)) return null;
  return <DocsExport sessionId={sid} notes={notes} api={api} />;
}
