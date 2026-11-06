"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { docsApi as defaultApi, DocsApiError, isDocId, isSessionId, type DemoDoc, type DocsApi } from "@/lib/docs";
import { PlainText } from "@/lib/lecture";
import { useRequireUser } from "../AuthProvider";
import { runDelete } from "../deleteFlow";
import { AnnouncerProvider, useAnnouncer } from "../lecture/Announcer";
import { ConfirmDialog } from "../syllabus/ConfirmDialog";
import { DOCS_COPY } from "./copy";
import { DocBlocks } from "./DocBlocks";
import { formatDay } from "./format";

const C = DOCS_COPY.page;

type State = { s: "loading" } | { s: "notfound" } | { s: "error" } | { s: "ready"; doc: DemoDoc } | { s: "deleted" };

export interface DemoDocPageProps {
  api?: DocsApi;
  /** IANA zone for the version date; the browser's own when absent (tests pin UTC, L-UTC). */
  timeZone?: string;
}

/**
 * `/demo-docs/[docId]` (finish-backlog item 3, tasks F4): the read-only practice copy kept inside Tether. The id from the
 * route is checked against the docId pattern before any request (F04a); a foreign, missing or deleted document is the same
 * calm not-found (F17). Title and blocks are plain text only (F16, F18). "Delete this Demo Doc" uses the shared
 * destructive ConfirmDialog and runDelete (Q3; Keep it is focused; a lost answer is checked with one read). The lecture
 * link comes from `?lecture=` (the Demo Doc body carries no session id) and only when it is a canonical session id.
 */
export function DemoDocPage(props: DemoDocPageProps) {
  return (
    <AnnouncerProvider>
      <DemoDocBody {...props} />
    </AnnouncerProvider>
  );
}

function DemoDocBody({ api = defaultApi, timeZone }: DemoDocPageProps) {
  const params = useParams<{ docId: string }>();
  const raw = params?.docId;
  const docId = typeof raw === "string" ? safeDecode(raw) : null;
  const { status } = useRequireUser();
  const announcer = useAnnouncer();
  const [state, setState] = useState<State>({ s: "loading" });
  const [lecture, setLecture] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const deletedRef = useRef<HTMLHeadingElement>(null);
  const problemRef = useRef<HTMLParagraphElement>(null);

  // A delete problem line takes focus when it appears (UX review section 7.3, S3), as LectureDelete does.
  useEffect(() => {
    if (problem) problemRef.current?.focus();
  }, [problem]);

  useEffect(() => {
    const q = new URLSearchParams(window.location.search).get("lecture");
    setLecture(isSessionId(q) ? q : null);
  }, []);

  useEffect(() => {
    if (status !== "authenticated") return;
    if (!isDocId(docId)) {
      setState({ s: "notfound" }); // never put a malformed id into a request path
      return;
    }
    let cancelled = false;
    api
      .getDemoDoc(docId)
      .then((doc) => !cancelled && setState({ s: "ready", doc }))
      .catch((e: unknown) => {
        if (cancelled) return;
        // A 400 can only be an id the server does not accept: the same calm not-found as a foreign one.
        setState(e instanceof DocsApiError && (e.isNotFound || e.status === 400) ? { s: "notfound" } : { s: "error" });
      });
    return () => {
      cancelled = true;
    };
  }, [status, docId, api]);

  useEffect(() => {
    if (state.s === "deleted") deletedRef.current?.focus();
  }, [state.s]);

  const back = lecture ? (
    <Link className="btn-tertiary optical-start" href={`/lectures/${lecture}`}>
      {C.back}
    </Link>
  ) : (
    <Link className="btn-tertiary optical-start" href="/">
      {C.backHome}
    </Link>
  );

  if (state.s === "loading") return <p className="text-muted">{C.loading}</p>;
  if (state.s === "notfound" || state.s === "error") {
    return (
      <div className="max-w-prose space-y-4">
        <h1 className="display text-3xl">{state.s === "notfound" ? C.notFound : C.loadError}</h1>
        <Link href="/" className="btn-primary">
          {C.backHome}
        </Link>
      </div>
    );
  }
  if (state.s === "deleted") {
    return (
      <div className="max-w-prose space-y-4">
        <h1 ref={deletedRef} tabIndex={-1} className="display text-3xl" data-testid="demo-doc-deleted">
          {C.deleted}
        </h1>
        <div>{back}</div>
      </div>
    );
  }

  const doc = state.doc;
  async function confirmDelete() {
    if (busy) return;
    setBusy(true);
    setProblem(null);
    const r = await runDelete(
      () => api.deleteDemoDoc(doc.docId),
      () => api.getDemoDoc(doc.docId),
    );
    if (r.kind !== "problem") {
      setOpen(false);
      setBusy(false);
      setState({ s: "deleted" });
      announcer.status(C.deleted);
      return;
    }
    setBusy(false);
    setProblem(r.text);
    announcer.status(r.text);
  }

  return (
    <div className="space-y-6">
      <article aria-labelledby="demo-doc-h1" className="sheet sheet-bleed max-w-sheet" data-testid="demo-doc">
        <p className="note note-neutral" data-testid="demo-doc-banner">
          {C.banner}
        </p>
        <h1 id="demo-doc-h1" className="display mt-4 text-3xl">
          <PlainText value={doc.document.title} />
        </h1>
        <p className="help mt-2">{C.version(doc.version, formatDay(doc.updatedAt, timeZone))}</p>
        <div className="mt-2">
          <DocBlocks blocks={doc.document.blocks} base={1} />
        </div>
      </article>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        {back}
        <button
          type="button"
          className="btn-tertiary"
          onClick={() => {
            setProblem(null);
            setOpen(true);
          }}
        >
          {C.delete}
        </button>
      </div>
      <ConfirmDialog
        open={open}
        heading={C.deleteHeading}
        confirmLabel={C.deleteConfirm}
        cancelLabel={C.keep}
        busy={busy}
        busyLabel={C.deleting}
        onConfirm={() => void confirmDelete()}
        onCancel={() => setOpen(false)}
      >
        <p>{C.deleteBody}</p>
        {problem ? (
          <p ref={problemRef} tabIndex={-1} data-delete-problem className="font-semibold text-ink">
            {problem}
          </p>
        ) : null}
      </ConfirmDialog>
    </div>
  );
}

function safeDecode(s: string): string | null {
  try {
    return decodeURIComponent(s);
  } catch {
    return null;
  }
}
