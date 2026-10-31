"use client";

import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { ApiError } from "@/lib/api/errors";
import { PlainText } from "@/lib/lecture/PlainTextSpan";
import type { SyllabusApi } from "@/lib/syllabus/client";
import { reviewOrder } from "@/lib/syllabus/eligibility";
import { KIND_LABEL, whenLine } from "@/lib/syllabus/format";
import type { Decision, Syllabus, SyllabusItem } from "@/lib/syllabus/types";
import { useAnnouncer } from "../lecture/Announcer";
import { ActionProblem, problemKind, type ProblemKind } from "./ActionProblem";
import { DEMO_TAGS, ITEM_COPY as I, ONE_COPY as C, SYNC_COPY } from "./copy";
import { DemoLine } from "./DemoLine";
import { ConfidenceIcon, DecisionIcon } from "./icons";
import { ItemEditForm } from "./ItemEditForm";

export interface OneAtATimeProps {
  api: SyllabusApi;
  syllabus: Syllabus;
  items: SyllabusItem[];
  /** Item to start on (the one the student was looking at), else the first one not decided. */
  startId?: string | null;
  onChange(item: SyllabusItem): void;
  /** Back to the list, focused on this item's row (null at the end screen). */
  onExit(itemId: string | null): void;
  onToCalendar(): void;
}

/**
 * "One at a time" (UX 5.5): "Item N of M" as text (no progress bar, no "left" language). Approve and Skip decide and
 * advance; "Next, decide later" advances without deciding; "Back" shows the previous item with its decision. After a
 * decision or a move, focus goes to the new item's heading and the status region says its title once. Escape returns
 * to the list. Order: by date, "Needs a date" last; course policies are not decided.
 */
export function OneAtATime({ api, syllabus, items, startId, onChange, onExit, onToCalendar }: OneAtATimeProps) {
  const announcer = useAnnouncer();
  const h1Ref = useRef<HTMLHeadingElement>(null);
  const itemRef = useRef<HTMLHeadingElement>(null);
  const endRef = useRef<HTMLHeadingElement>(null);
  // The order is fixed when the mode opens, so deciding an item never reorders what "Next" means.
  const [order] = useState(() => reviewOrder(items).map((i) => i.id));
  const byId = new Map(items.map((i) => [i.id, i]));
  const list = order.map((id) => byId.get(id)).filter((i): i is SyllabusItem => Boolean(i));
  const [index, setIndex] = useState(() => {
    const at = startId ? list.findIndex((i) => i.id === startId) : -1;
    if (at >= 0) return at;
    const firstOpen = list.findIndex((i) => i.decision === "undecided");
    return firstOpen >= 0 ? firstOpen : 0;
  });
  const [moved, setMoved] = useState(false);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<ProblemKind | null>(null);
  const [editing, setEditing] = useState<false | "title" | "date">(false);
  const editBtn = useRef<HTMLButtonElement>(null);

  useEffect(() => h1Ref.current?.focus(), []);

  const atEnd = index >= list.length;
  const item = atEnd ? null : list[index];

  useEffect(() => {
    if (!moved) return;
    if (atEnd) endRef.current?.focus();
    else {
      itemRef.current?.focus();
      if (item) announcer.status(item.title);
    }
    setMoved(false);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- runs once per move
  }, [moved]);

  const go = (next: number) => {
    setProblem(null);
    setEditing(false);
    setIndex(Math.max(0, Math.min(list.length, next)));
    setMoved(true);
  };

  async function decide(target: Decision, advance: boolean) {
    if (!item || busy) return;
    setBusy(true);
    setProblem(null);
    try {
      const next = await api.decide(item.syllabusId, item.id, target, item.etag, target === "approved" && item.confidence === "please_check");
      onChange(next);
      if (advance) go(index + 1);
      else announcer.status(I.undoneMsg(next.title));
    } catch (e) {
      if (e instanceof ApiError && e.status === 412) {
        try {
          onChange(await api.getItem(item.syllabusId, item.id));
        } catch {
          /* keep */
        }
      }
      setProblem(problemKind(e));
    } finally {
      setBusy(false);
    }
  }

  const onKeyDown = (e: KeyboardEvent) => {
    if (e.key === "Escape" && !editing) {
      e.preventDefault();
      onExit(item?.id ?? null);
    }
  };

  const decidable = items.filter((i) => i.kind !== "policy");
  const approved = decidable.filter((i) => i.decision === "approved").length;
  const skipped = decidable.filter((i) => i.decision === "skipped").length;
  const undecided = decidable.length - approved - skipped;

  return (
    <div className="max-w-prose space-y-6" onKeyDown={onKeyDown}>
      <div className="space-y-3">
        <div className="flex flex-wrap items-baseline justify-between gap-3">
          <h1 ref={h1Ref} tabIndex={-1} className="display text-3xl">
            {C.h1}
          </h1>
          <button type="button" className="btn-secondary btn-sm" onClick={() => onExit(item?.id ?? null)}>
            {C.exit}
          </button>
        </div>
        <DemoLine text={DEMO_TAGS.calendar} tag={SYNC_COPY.tagWord} />
        {syllabus.demoSource && <DemoLine text={DEMO_TAGS.source} />}
      </div>

      {list.length === 0 && <p>{C.empty}</p>}

      {item && (
        <section aria-labelledby="one-item-h" className="card space-y-3">
          <p className="text-sm text-muted" data-testid="one-position">
            {C.position(index + 1, list.length)}
          </p>
          <h2 id="one-item-h" ref={itemRef} tabIndex={-1} className="display break-words text-2xl">
            <PlainText value={item.title} />
          </h2>
          <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
            <span className="tag tag-neutral">{KIND_LABEL[item.kind]}</span>
            {whenLine(item, I.allDay) && <span>{whenLine(item, I.allDay)}</span>}
          </p>
          {item.when.status === "needs_date" && item.when.unknownReason && <p className="text-sm">{I.needsDate[item.when.unknownReason]}</p>}
          <p className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm">
            <span className="inline-flex items-center gap-1 font-semibold">
              <ConfidenceIcon value={item.confidence} />
              {I.conf[item.confidence]}
            </span>
            <span className="inline-flex items-center gap-1">
              <DecisionIcon value={item.decision} />
              {I.status[item.decision]}
            </span>
            {item.edited && <span className="text-muted">{I.edited}</span>}
          </p>
          {item.confidence === "please_check" && (
            <p className="text-sm text-muted">
              <PlainText value={item.confidenceReason} />
            </p>
          )}
          {item.snippet?.text ? (
            <div className="space-y-1 border-l-4 border-line pl-3 text-sm">
              <p className="text-muted">{I.sourceFrom(item.snippet.page)}</p>
              <blockquote className="break-words">
                <PlainText value={item.snippet.text} />
              </blockquote>
              {item.snippet.context && item.snippet.context.text !== item.snippet.text && (
                <>
                  <p className="text-muted">{I.sourceAround}</p>
                  <blockquote className="whitespace-pre-line break-words">
                    <PlainText value={item.snippet.context.text} />
                  </blockquote>
                </>
              )}
            </div>
          ) : (
            item.snippet && <p className="text-sm text-muted">{I.purgedSnippet}</p>
          )}
          {item.confidence === "please_check" && item.when.status === "resolved" && item.decision !== "approved" && <p className="help">{I.checkHint}</p>}

          <div className="flex flex-wrap gap-2">
            {item.when.status === "needs_date" ? (
              <button ref={editBtn} type="button" className="btn-secondary" aria-label={I.addDateName(item.title)} onClick={() => setEditing("date")}>
                {I.addDate}
              </button>
            ) : item.decision === "approved" ? (
              <button type="button" className="btn-secondary" aria-label={I.undoApproveName(item.title)} aria-busy={busy || undefined} onClick={() => void decide("undecided", false)}>
                {I.undo}
              </button>
            ) : (
              <button type="button" className="btn-primary" aria-label={I.approveName(item.title)} aria-busy={busy || undefined} onClick={() => void decide("approved", true)}>
                {I.approve}
              </button>
            )}
            {item.decision === "skipped" ? (
              <button type="button" className="btn-secondary" aria-label={I.undoSkipName(item.title)} aria-busy={busy || undefined} onClick={() => void decide("undecided", false)}>
                {I.undo}
              </button>
            ) : (
              <button type="button" className="btn-secondary" aria-label={I.skipName(item.title)} aria-busy={busy || undefined} onClick={() => void decide("skipped", true)}>
                {I.skip}
              </button>
            )}
            {item.when.status !== "needs_date" && (
              <button ref={editBtn} type="button" className="btn-tertiary" aria-label={I.editName(item.title)} aria-expanded={editing ? true : false} onClick={() => setEditing("title")}>
                {I.edit}
              </button>
            )}
          </div>
          {problem && <ActionProblem kind={problem} />}
          {editing && (
            <ItemEditForm
              api={api}
              item={item}
              initialFocus={editing}
              onConflict={onChange}
              onCancel={() => {
                setEditing(false);
                queueMicrotask(() => editBtn.current?.focus());
              }}
              onSaved={(saved) => {
                onChange(saved);
                announcer.status(I.savedMsg(saved.title));
                setEditing(false);
                queueMicrotask(() => editBtn.current?.focus());
              }}
            />
          )}
          <div className="flex flex-wrap justify-between gap-3 border-t border-hair pt-3">
            <button type="button" className="btn-tertiary" aria-disabled={index === 0 ? true : undefined} onClick={() => index > 0 && go(index - 1)}>
              {C.back}
            </button>
            <button type="button" className="btn-secondary" onClick={() => go(index + 1)}>
              {C.next}
            </button>
          </div>
        </section>
      )}

      {atEnd && list.length > 0 && (
        <section aria-labelledby="one-end-h" className="card space-y-3">
          <h2 id="one-end-h" ref={endRef} tabIndex={-1} className="display text-2xl">
            {C.endHeading}
          </h2>
          <p>{C.endCounts(approved, skipped, undecided)}</p>
          <div className="flex flex-wrap gap-3">
            {approved > 0 && (
              <button type="button" className="btn-primary" onClick={onToCalendar}>
                {C.toCalendar}
              </button>
            )}
            {undecided > 0 && (
              <button
                type="button"
                className={approved > 0 ? "btn-secondary" : "btn-primary"}
                onClick={() => go(Math.max(0, list.findIndex((i) => i.decision === "undecided")))}
              >
                {C.reviewLeft}
              </button>
            )}
            <button type="button" className="btn-tertiary" onClick={() => go(list.length - 1)}>
              {C.back}
            </button>
          </div>
        </section>
      )}
    </div>
  );
}
