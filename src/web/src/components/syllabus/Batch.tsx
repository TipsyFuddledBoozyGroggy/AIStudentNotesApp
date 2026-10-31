"use client";

import { useEffect, useId, useRef, useState } from "react";
import { PlainText } from "@/lib/lecture/PlainTextSpan";
import type { SyllabusApi } from "@/lib/syllabus/client";
import { KIND_LABEL, whenLine } from "@/lib/syllabus/format";
import type { BatchResult, SyllabusItem } from "@/lib/syllabus/types";
import { ActionProblem, problemKind, type ProblemKind } from "./ActionProblem";
import { BATCH_COPY as C, ITEM_COPY } from "./copy";

/** What a finished batch approve leaves behind: the ids it approved, for its own "Undo this". */
export interface BatchDone {
  approvedIds: string[];
  asked: number;
}

/**
 * Inline batch-approve summary (UX 5.4; not a modal). Lists every selected item with its date, source wording and
 * context (contract 8, R2-09: the student reads each one; no label says "verified"), then "Approve N items" / "Cancel".
 * Sends itemEtags keyed by id with the etag the student saw; the server re-checks eligibility per item.
 */
export function BatchPanel({
  api,
  syllabusId,
  selected,
  onDone,
  onCancel,
  level = 3,
}: {
  api: SyllabusApi;
  syllabusId: string;
  selected: SyllabusItem[];
  /** Heading level that fits where the panel sits (2 at the top of the list, 3 inside a group). */
  level?: 2 | 3;
  onDone(result: BatchResult, done: BatchDone): void;
  onCancel(): void;
}) {
  const id = useId();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<ProblemKind | null>(null);
  const problemRef = useRef<HTMLDivElement>(null);
  const n = selected.length;
  const H = level === 2 ? "h2" : "h3";

  useEffect(() => headingRef.current?.focus(), []);
  useEffect(() => {
    if (problem) problemRef.current?.focus();
  }, [problem]);

  async function approve() {
    if (busy || n === 0) return;
    setBusy(true);
    setProblem(null);
    try {
      const r = await api.decideBatch(syllabusId, "approve", new Map(selected.map((i) => [i.id, i.etag])));
      onDone(r, { approvedIds: r.results.filter((x) => x.outcome === "applied").map((x) => x.itemId), asked: n });
    } catch (e) {
      setProblem(problemKind(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section aria-labelledby={`${id}-h`} className="note note-neutral space-y-3" data-batch-panel="">
      <H id={`${id}-h`} ref={headingRef} tabIndex={-1} className="font-semibold text-ink">
        {C.explain(n)}
      </H>
      <p className="help">{C.readEach}</p>
      <ul className="divide-y divide-hair">
        {selected.map((i) => (
          <li key={i.id} className="py-3 text-sm">
            <p className="break-words font-semibold text-ink">
              <PlainText value={i.title} />
            </p>
            <p>
              {KIND_LABEL[i.kind]} · {whenLine(i, ITEM_COPY.allDay)}
            </p>
            {i.snippet?.text && (
              <div className="mt-1 space-y-1 border-l-2 border-hair pl-3">
                <p className="text-muted">{ITEM_COPY.sourceFrom(i.snippet.page)}</p>
                <blockquote className="break-words">
                  <PlainText value={i.snippet.text} />
                </blockquote>
                {i.snippet.context && i.snippet.context.text !== i.snippet.text && (
                  <>
                    <p className="text-muted">{ITEM_COPY.sourceAround}</p>
                    <blockquote className="whitespace-pre-line break-words">
                      <PlainText value={i.snippet.context.text} />
                    </blockquote>
                  </>
                )}
              </div>
            )}
          </li>
        ))}
      </ul>
      {problem && <ActionProblem ref={problemRef} kind={problem} />}
      <div className="flex flex-wrap gap-3">
        <button type="button" className="btn-primary btn-sm" aria-disabled={busy ? true : undefined} onClick={() => void approve()}>
          {busy ? C.busy : C.go(n)}
        </button>
        <button type="button" className="btn-secondary btn-sm" onClick={onCancel}>
          {C.cancel}
        </button>
      </div>
    </section>
  );
}

/**
 * Result of a batch approve with its own "Undo this" (UX 5.4): restores exactly the items it approved (they were all
 * "Not decided" before) and stays until the next decision. Focus lands on "Undo this" (UX 9 focus map).
 */
export function BatchUndo({
  api,
  syllabusId,
  done,
  items,
  onUndone,
}: {
  api: SyllabusApi;
  syllabusId: string;
  done: BatchDone;
  items: SyllabusItem[];
  onUndone(result: BatchResult | null, problem: ProblemKind | null): void;
}) {
  const btnRef = useRef<HTMLButtonElement>(null);
  const boxRef = useRef<HTMLDivElement>(null);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<ProblemKind | null>(null);
  const k = done.approvedIds.length;

  useEffect(() => (btnRef.current ?? boxRef.current)?.focus(), []);

  async function undo() {
    if (busy) return;
    const still = items.filter((i) => done.approvedIds.includes(i.id) && i.decision === "approved");
    if (still.length === 0) {
      onUndone(null, null);
      return;
    }
    setBusy(true);
    setProblem(null);
    try {
      const r = await api.decideBatch(syllabusId, "undecide", new Map(still.map((i) => [i.id, i.etag])));
      onUndone(r, null);
    } catch (e) {
      setProblem(problemKind(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div ref={boxRef} tabIndex={-1} className="note note-neutral space-y-2" data-batch-undo="">
      <p className="font-semibold">{k === done.asked ? C.done(k) : C.partial(k, done.asked)}</p>
      {k > 0 && (
        <button ref={btnRef} type="button" className="btn-secondary btn-sm" aria-busy={busy || undefined} onClick={() => void undo()}>
          {busy ? C.undoBusy : C.undo}
        </button>
      )}
      {problem && <ActionProblem kind={problem} />}
    </div>
  );
}
