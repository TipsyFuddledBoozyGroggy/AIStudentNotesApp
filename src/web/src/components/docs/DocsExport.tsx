"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { confirmBody, docsApi as defaultApi, DocsApiError, newIdempotencyKey, type DocRef, type DocsApi, type DocsConfirmBody, type DocsExportRecord, type DocsMode, type DocsPreview } from "@/lib/docs";
import type { LectureNotes } from "@/lib/lecture";
import { useAnnouncer } from "../lecture/Announcer";
import { DemoNotesTag } from "../lecture/QuickRecapPanel";
import { DOCS_COPY as C } from "./copy";
import { DocBlocks } from "./DocBlocks";
import { DocsDialog } from "./DocsDialog";
import { formatDay, openHref } from "./format";

export interface DocsExportProps {
  sessionId: string;
  notes: LectureNotes;
  api?: DocsApi;
  /** IANA zone for the "saved on" dates; the browser's own zone when absent (tests pin UTC, L-UTC). */
  timeZone?: string;
}

/** One confirm attempt: the preview it was made from, the body and its key. Retry reuses all three (F04). */
interface Attempt {
  p: DocsPreview;
  body: DocsConfirmBody;
  key: string;
}

type View =
  | { k: "loading" }
  | { k: "previewError" }
  | { k: "notAvailable" }
  | { k: "preview"; p: DocsPreview; key: string; stale: boolean }
  | { k: "saving"; a: Attempt }
  | { k: "success"; record: DocsExportRecord }
  | { k: "retry"; a: Attempt; wait: number | null }
  | { k: "lastAttempt" }
  | { k: "permanent" }
  | { k: "inProgress" };

const SUCCESS = { created: C.success.created, updated: C.success.updated, unchanged: C.success.unchanged } as const;

/**
 * "Save to a Demo Doc" on the Ready notes sheet (finish-backlog item 3, tasks F3; contract docs.export.v1, mock only).
 * Nothing is written until the student reads the preview and presses Save: the preview POST writes nothing, and the only
 * write is the confirm, which echoes the preview's digest and the document it showed (DXD-05). One Idempotency-Key per
 * preview; Try again resends the same key and body, a new preview makes a new key (F04). A double press sends one request
 * (F09). Results are said once through the existing status region (no new live region) and the message takes focus after
 * the student's own press; closing returns focus to the button (F12). Text is rendered as plain text only.
 */
export function DocsExport({ sessionId, notes, api = defaultApi, timeZone }: DocsExportProps) {
  const announcer = useAnnouncer();
  const [saved, setSaved] = useState<{ ref: DocRef; at: string | null } | null>(null);
  const [view, setView] = useState<View | null>(null);
  const [choice, setChoice] = useState<DocsMode | null>(null);
  const [focusMsg, setFocusMsg] = useState(0);
  const openerRef = useRef<HTMLButtonElement>(null);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const msgRef = useRef<HTMLParagraphElement>(null);
  const inFlight = useRef(false);
  // Every request belongs to one opening of the dialog; an answer for an earlier opening is dropped.
  const epoch = useRef(0);
  const notesId = notes.notesId;

  useEffect(() => {
    let cancelled = false;
    api
      .getState(sessionId)
      .then((s) => {
        if (cancelled || !s.target) return;
        const last = s.lastExport;
        const at = last && last.status === "succeeded" && last.docRef?.docId === s.target.docId ? last.completedAt : null;
        setSaved({ ref: s.target, at });
      })
      .catch(() => undefined); // the entry still works; only the "saved on" line is not shown
    return () => {
      cancelled = true;
    };
  }, [api, sessionId]);

  useEffect(() => {
    if (focusMsg > 0) (msgRef.current ?? headingRef.current)?.focus();
  }, [focusMsg]);

  const loadPreview = useCallback(
    async (stale: boolean) => {
      const my = epoch.current;
      setView({ k: "loading" });
      setChoice(null);
      try {
        const p = await api.preview(sessionId, notesId);
        if (my !== epoch.current) return;
        setView({ k: "preview", p, key: newIdempotencyKey(), stale });
        if (stale) {
          announcer.status(C.stale);
          setFocusMsg((n) => n + 1);
        }
      } catch (e) {
        if (my !== epoch.current) return;
        setView(e instanceof DocsApiError && e.isNotFound ? { k: "notAvailable" } : { k: "previewError" });
        if (stale) setFocusMsg((n) => n + 1);
      }
    },
    [api, sessionId, notesId, announcer],
  );

  const settle = (next: View, say: string) => {
    setView(next);
    announcer.status(say);
    setFocusMsg((n) => n + 1);
  };

  const onRecord = (a: Attempt, record: DocsExportRecord) => {
    if (record.status === "succeeded" && record.outcome !== "failed" && record.docRef) {
      setSaved({ ref: record.docRef, at: record.completedAt });
      settle({ k: "success", record }, SUCCESS[record.outcome]);
      return;
    }
    const err = record.error;
    if (err?.retryable && record.retry.allowed) settle({ k: "retry", a, wait: err.retryAfterSeconds }, C.retry.body);
    else if (err?.retryable) settle({ k: "lastAttempt" }, C.lastAttempt.body);
    else settle({ k: "permanent" }, C.permanent);
  };

  const send = async (a: Attempt) => {
    if (inFlight.current) return; // double press: one request (F09)
    inFlight.current = true;
    const my = epoch.current;
    setView({ k: "saving", a });
    announcer.status(C.saving.announce);
    try {
      const { record } = await api.confirm(sessionId, a.body, a.key);
      if (my === epoch.current) onRecord(a, record);
    } catch (e) {
      if (my !== epoch.current) return;
      const status = e instanceof DocsApiError ? e.status : 0;
      const code = e instanceof DocsApiError ? e.code : "";
      if (status === 412) {
        // The content or the document moved: nothing was saved; show the new version and let the student confirm again (F11).
        inFlight.current = false;
        await loadPreview(true);
        return;
      }
      if (status === 404) settle({ k: "notAvailable" }, C.notAvailable);
      else if (status === 409 && code === "retry_limit_reached") settle({ k: "lastAttempt" }, C.lastAttempt.body);
      else if (status === 409 && code === "export_in_progress") settle({ k: "inProgress" }, C.inProgress.body);
      else if (status === 429) settle({ k: "retry", a, wait: e instanceof DocsApiError ? e.retryAfterSeconds : null }, C.retry.body);
      // No answer, a server failure or a body we could not read: the same key and body are safe to send again (a replay
      // returns the stored record, so nothing can be written twice).
      else if (status === 0 || status >= 500 || code === "malformed_response") settle({ k: "retry", a, wait: null }, C.retry.body);
      else settle({ k: "permanent" }, C.permanent);
    } finally {
      inFlight.current = false;
    }
  };

  const open = () => {
    epoch.current += 1;
    void loadPreview(false);
  };
  const close = () => {
    if (view?.k === "saving") return;
    epoch.current += 1;
    setView(null);
    queueMicrotask(() => openerRef.current?.focus());
  };

  const save = (p: DocsPreview, key: string) => {
    const mode: DocsMode | null = p.actions.includes("create") ? "create" : choice;
    if (!mode || !p.actions.includes(mode)) return; // aria-disabled until a choice is made (F08)
    void send({ p, body: confirmBody(p, mode), key });
  };

  const day = (iso: string) => formatDay(iso, timeZone);
  const closeBtn = (primary = false) => (
    <button type="button" className={primary ? "btn-primary" : "btn-tertiary"} onClick={close}>
      {C.preview.close}
    </button>
  );
  const message = (text: string, testId: string) => (
    <p ref={msgRef} tabIndex={-1} data-testid={testId} className="mt-3 max-w-prose">
      {text}
    </p>
  );
  const actions = (...nodes: ReactNode[]) => <div className="mt-6 flex flex-wrap justify-end gap-3">{nodes}</div>;

  function previewBody(p: DocsPreview, key: string, opts: { stale: boolean; saving: boolean }) {
    const writable = p.canWrite && p.actions.length > 0;
    const needsChoice = p.actions.includes("update");
    const chosen = !needsChoice || choice !== null;
    const matches = p.canWrite && p.actions.length === 0;
    return (
      <>
        {opts.stale ? (
          <p ref={msgRef} tabIndex={-1} data-testid="docs-stale" className="note note-notice mt-3 max-w-prose">
            {C.stale}
          </p>
        ) : null}
        <div className="mt-3 space-y-2" data-testid="docs-preview">
          <p>{C.preview.line1}</p>
          <p>{C.preview.where}</p>
          <p data-testid="docs-preview-title">{C.preview.title(p.document.title)}</p>
          <p>{C.preview.count(p.counts.sections, p.counts.bullets)}</p>
          {p.notesSource === "mock" ? (
            <p>
              <DemoNotesTag />
            </p>
          ) : null}
          {p.existing && !p.existing.upToDate ? <p data-testid="docs-changed">{C.changed.line(day(p.existing.lastExportedAt))}</p> : null}
        </div>
        {!p.canWrite ? (
          <>
            {message(p.blockedReason === "too_large" ? C.tooLarge : C.nothing, "docs-blocked")}
            {actions(<span key="c">{closeBtn(true)}</span>)}
          </>
        ) : matches ? (
          <>
            {message(C.matches, "docs-matches")}
            {actions(
              p.existing ? (
                <Link key="o" className="btn-primary" href={openHref(p.existing.docRef, sessionId)}>
                  {C.entry.open}
                </Link>
              ) : null,
              <span key="c">{closeBtn()}</span>,
            )}
          </>
        ) : writable ? (
          <>
            <div role="region" aria-label={C.preview.regionLabel} tabIndex={0} data-testid="docs-preview-blocks" className="sheet mt-4 max-h-64 overflow-y-auto">
              <DocBlocks blocks={p.document.blocks} base={2} />
            </div>
            {needsChoice ? (
              <fieldset className="mt-4" disabled={opts.saving} aria-describedby={chosen ? undefined : "docs-choice-hint"}>
                <legend className="font-semibold text-ink">{C.changed.legend}</legend>
                {(
                  [
                    ["update", C.changed.update],
                    ["create_another", C.changed.another],
                  ] as const
                ).map(([value, label]) => (
                  <label key={value} className="mt-2 flex min-h-[44px] cursor-pointer items-start gap-3">
                    <input type="radio" name="docs-mode" value={value} className="mt-1 h-6 w-6 shrink-0" checked={choice === value} onChange={() => setChoice(value)} />
                    <span>{label}</span>
                  </label>
                ))}
                {!chosen ? (
                  <p id="docs-choice-hint" className="help mt-1">
                    {C.changed.hint}
                  </p>
                ) : null}
              </fieldset>
            ) : null}
            {actions(
              <button
                key="s"
                type="button"
                className="btn-primary btn-steady"
                data-testid="docs-save"
                aria-disabled={!chosen || opts.saving ? true : undefined}
                aria-busy={opts.saving ? true : undefined}
                aria-describedby={chosen ? undefined : "docs-choice-hint"}
                onClick={() => !opts.saving && save(p, key)}
              >
                {opts.saving ? C.saving.button : C.preview.save}
              </button>,
              <button key="n" type="button" className="btn-tertiary" aria-disabled={opts.saving ? true : undefined} onClick={close}>
                {C.preview.notNow}
              </button>,
            )}
          </>
        ) : null}
      </>
    );
  }

  function body(v: View) {
    switch (v.k) {
      case "loading":
        return (
          <>
            <p className="mt-3" data-testid="docs-loading">
              {C.preview.loading}
            </p>
            {actions(<span key="c">{closeBtn()}</span>)}
          </>
        );
      case "previewError":
        return (
          <>
            {message(C.preview.error, "docs-preview-error")}
            {actions(
              <button key="t" type="button" className="btn-primary" onClick={() => void loadPreview(false)}>
                {C.preview.tryAgain}
              </button>,
              <span key="c">{closeBtn()}</span>,
            )}
          </>
        );
      case "notAvailable":
        return (
          <>
            {message(C.notAvailable, "docs-not-available")}
            {actions(<span key="c">{closeBtn(true)}</span>)}
          </>
        );
      case "preview":
        return previewBody(v.p, v.key, { stale: v.stale, saving: false });
      case "saving":
        return previewBody(v.a.p, v.a.key, { stale: false, saving: true });
      case "success": {
        const ref = v.record.docRef!;
        return (
          <>
            {message(SUCCESS[v.record.outcome as keyof typeof SUCCESS], "docs-success")}
            <p className="mt-2 max-w-prose">{C.success.where}</p>
            {actions(
              <Link key="o" className="btn-primary" href={openHref(ref, sessionId)}>
                {C.entry.open}
              </Link>,
              <span key="c">{closeBtn()}</span>,
            )}
          </>
        );
      }
      case "retry":
        return (
          <>
            {message(C.retry.body, "docs-retry")}
            {v.wait !== null ? (
              <p className="mt-2 max-w-prose" data-testid="docs-retry-wait">
                {C.retry.wait(v.wait)}
              </p>
            ) : null}
            {actions(
              <button key="t" type="button" className="btn-primary btn-steady" onClick={() => void send(v.a)}>
                {C.retry.tryAgain}
              </button>,
              <span key="c">{closeBtn()}</span>,
            )}
          </>
        );
      case "lastAttempt":
        return (
          <>
            {message(C.lastAttempt.body, "docs-last-attempt")}
            {actions(
              <button key="s" type="button" className="btn-primary" onClick={() => void loadPreview(false)}>
                {C.lastAttempt.startAgain}
              </button>,
              <span key="c">{closeBtn()}</span>,
            )}
          </>
        );
      case "permanent":
        return (
          <>
            {message(C.permanent, "docs-permanent")}
            {actions(<span key="c">{closeBtn(true)}</span>)}
          </>
        );
      case "inProgress":
        return (
          <>
            {message(C.inProgress.body, "docs-in-progress")}
            {actions(
              <button key="k" type="button" className="btn-primary" onClick={() => void loadPreview(false)}>
                {C.inProgress.checkAgain}
              </button>,
              <span key="c">{closeBtn()}</span>,
            )}
          </>
        );
    }
  }

  const previewish = view !== null && (view.k === "loading" || view.k === "preview" || view.k === "saving");
  return (
    <div className="mt-6" data-testid="docs-export">
      <button ref={openerRef} type="button" className="btn-secondary" aria-describedby="docs-export-help" aria-haspopup="dialog" onClick={open}>
        {C.entry.button}
      </button>
      <p id="docs-export-help" className="help mt-2 max-w-prose">
        {C.entry.help}
      </p>
      {saved ? (
        <p className="mt-2 max-w-prose text-sm" data-testid="docs-export-saved">
          {saved.at ? C.entry.saved(day(saved.at)) : C.entry.savedUndated}{" "}
          <Link className="link" href={openHref(saved.ref, sessionId)}>
            {C.entry.open}
          </Link>
        </p>
      ) : null}
      {view ? (
        <DocsDialog heading={previewish ? C.preview.heading : C.entry.button} headingRef={headingRef} onClose={close} busy={view.k === "saving"}>
          {body(view)}
        </DocsDialog>
      ) : null}
    </div>
  );
}
