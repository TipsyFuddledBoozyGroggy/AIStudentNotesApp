"use client";

import { useEffect, useId, useRef, useState } from "react";
import { ApiError } from "@/lib/api/errors";
import { PlainText } from "@/lib/lecture/PlainTextSpan";
import type { SyllabusApi } from "@/lib/syllabus/client";
import { dateRange, formatDate, KIND_LABEL, todayYmd, weekdayList, whenLine } from "@/lib/syllabus/format";
import type { Decision, SyllabusItem } from "@/lib/syllabus/types";
import { useAnnouncer } from "../lecture/Announcer";
import { ActionProblem } from "./ActionProblem";
import { ACTION_COPY, ITEM_COPY as C } from "./copy";
import { ConfidenceIcon, DecisionIcon } from "./icons";
import { ItemCalendar } from "./ItemCalendar";
import { hasDraft, ItemEditForm } from "./ItemEditForm";

export interface ItemRowProps {
  api: SyllabusApi;
  item: SyllabusItem;
  /** The syllabus default zone; a row names its own zone only when it differs. */
  defaultTimezone: string;
  /** The canonical text was purged on schedule (snippets are null). */
  purged: boolean;
  onChange(item: SyllabusItem): void;
}

type Pending = { target: Decision; from: "approve" | "skip" };

/**
 * One item (UX 5.2-5.7). Title is an h3 (heading navigation steps through items); untrusted text (title, snippet,
 * context, notes, reasons) is rendered only as plain text nodes. Approve and Skip turn into Undo in the same position,
 * so focus stays on the pressed control; Undo never expires. The visible state is always the server's state: a failed
 * save leaves the row as it was and offers "Try again" beside it.
 */
export function ItemRow({ api, item, defaultTimezone, purged, onChange }: ItemRowProps) {
  const announcer = useAnnouncer();
  const uid = useId();
  const titleId = `${uid}-title`;
  const sourceId = `${uid}-source`;
  const checkHintId = `${uid}-check`;
  const isPolicy = item.kind === "policy";
  const needsDate = item.when.status === "needs_date";
  const [editing, setEditing] = useState<false | "title" | "date">(() => (hasDraft(item) ? (needsDate ? "date" : "title") : false));
  const [sourceOpen, setSourceOpen] = useState(item.confidence === "please_check");
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState<null | { kind: "save" | "conflict" | "session"; retry: Pending | null }>(null);
  const editBtn = useRef<HTMLButtonElement>(null);
  const returnFocus = useRef(false);

  useEffect(() => {
    if (!editing && returnFocus.current) {
      returnFocus.current = false;
      editBtn.current?.focus(); // Save/Cancel -> the Edit (or Add date) button (UX 9 focus map)
    }
  }, [editing]);

  async function decide(p: Pending) {
    if (busy) return;
    setBusy(true);
    setFailed(null);
    try {
      const ack = p.target === "approved" && item.confidence === "please_check";
      const next = await api.decide(item.syllabusId, item.id, p.target, item.etag, ack);
      onChange(next);
      const t = next.title;
      announcer.status(p.target === "approved" ? C.approvedMsg(t) : p.target === "skipped" ? C.skippedMsg(t) : C.undoneMsg(t));
    } catch (e) {
      if (e instanceof ApiError && e.isUnauthenticated) {
        setFailed({ kind: "session", retry: null });
        announcer.alert("syllabus-session", ACTION_COPY.sessionHeading);
      } else if (e instanceof ApiError && e.status === 412) {
        try {
          onChange(await api.getItem(item.syllabusId, item.id));
        } catch {
          /* keep the current row */
        }
        setFailed({ kind: "conflict", retry: null });
      } else {
        setFailed({ kind: "save", retry: p });
        announcer.alert(`syllabus-save-${item.id}`, C.saveFailed);
      }
    } finally {
      setBusy(false);
    }
  }

  const title = item.title;
  const when = whenLine(item, C.allDay);
  const past = item.when.status === "resolved" && item.when.date !== null && (item.when.endDate ?? item.when.date) < todayYmd();
  const zone = item.when.timezone && item.when.timezone !== defaultTimezone ? item.when.timezone : null;
  const snippet = item.snippet;
  const snippetPurged = snippet !== null && (snippet.text === null || purged);
  const showSource = snippet !== null && !snippetPurged;
  const skipped = item.decision === "skipped";

  // Approve position: Approve or Undo (or Add date for an undated item; it cannot be approved, contract 6.1).
  const approveBtn = needsDate ? (
    <button
      ref={editBtn}
      type="button"
      className="btn-secondary btn-sm"
      aria-label={C.addDateName(title)}
      aria-expanded={editing ? true : false}
      onClick={() => setEditing("date")}
    >
      {C.addDate}
    </button>
  ) : item.decision === "approved" ? (
    <button type="button" className="btn-secondary btn-sm" aria-label={C.undoApproveName(title)} aria-busy={busy || undefined} onClick={() => decide({ target: "undecided", from: "approve" })}>
      {C.undo}
    </button>
  ) : (
    <button
      type="button"
      className="btn-secondary btn-sm"
      aria-label={C.approveName(title)}
      aria-describedby={item.confidence === "please_check" ? checkHintId : undefined}
      aria-busy={busy || undefined}
      onClick={() => decide({ target: "approved", from: "approve" })}
    >
      {C.approve}
    </button>
  );
  const skipBtn = skipped ? (
    <button type="button" className="btn-secondary btn-sm" aria-label={C.undoSkipName(title)} aria-busy={busy || undefined} onClick={() => decide({ target: "undecided", from: "skip" })}>
      {C.undo}
    </button>
  ) : (
    <button type="button" className="btn-secondary btn-sm" aria-label={C.skipName(title)} aria-busy={busy || undefined} onClick={() => decide({ target: "skipped", from: "skip" })}>
      {C.skip}
    </button>
  );

  return (
    <li aria-labelledby={titleId} className="rounded-card border border-hair bg-surface p-4" data-item-id={item.id}>
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div className="min-w-0 space-y-1">
          <h3 id={titleId} className="break-words text-lg font-semibold text-ink">
            <PlainText value={title} />
          </h3>
          <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
            <span className="tag tag-neutral">{KIND_LABEL[item.kind]}</span>
            {when && <span>{when}</span>}
            {zone && <span className="text-muted">({zone})</span>}
            {past && <span className="text-muted">{C.past}</span>}
          </p>
          {item.recurrence && (
            <p className="text-sm text-muted">{C.repeats(weekdayList(item.recurrence.byDay), dateRange(item.recurrence.from, item.recurrence.until))}</p>
          )}
          {needsDate && item.when.unknownReason && (
            <p className="text-sm">
              {C.needsDate[item.when.unknownReason]}
              {item.when.alternatives.length > 0 && <> {C.alternatives(item.when.alternatives.map((a) => formatDate(a.date)).join(" or "))}</>}
            </p>
          )}
          {!isPolicy && (
            <p className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm">
              <span className="inline-flex items-center gap-1 font-semibold">
                <ConfidenceIcon value={item.confidence} />
                {C.conf[item.confidence]}
              </span>
              <span className="inline-flex items-center gap-1">
                <DecisionIcon value={item.decision} />
                {C.status[item.decision]}
              </span>
              {item.edited && <span className="text-muted">{C.edited}</span>}
            </p>
          )}
          {!isPolicy && item.confidence === "please_check" && !skipped && (
            <p className="text-sm text-muted">
              <PlainText value={item.confidenceReason} />
            </p>
          )}
          {item.notes && (
            <p className="whitespace-pre-line text-sm">
              <PlainText value={item.notes} />
            </p>
          )}
        </div>

        {!isPolicy && (
          <div className="flex shrink-0 flex-wrap gap-2">
            {approveBtn}
            {skipBtn}
            {!needsDate && (
              <button ref={editBtn} data-edit-button="" type="button" className="btn-quiet btn-sm" aria-label={C.editName(title)} aria-expanded={editing ? true : false} onClick={() => setEditing("title")}>
                {C.edit}
              </button>
            )}
          </div>
        )}
      </div>

      {!isPolicy && item.confidence === "please_check" && !needsDate && item.decision !== "approved" && (
        <p id={checkHintId} className="help mt-2">
          {C.checkHint}
        </p>
      )}

      {failed?.kind === "session" && <ActionProblem kind="session" className="mt-3" />}
      {failed && failed.kind !== "session" && (
        <div className="mt-3 flex flex-wrap items-center gap-3 text-sm">
          <p className="font-semibold">{failed.kind === "conflict" ? C.changedElsewhere : C.saveFailed}</p>
          {failed.retry && (
            <button type="button" className="btn-secondary btn-sm" onClick={() => failed.retry && decide(failed.retry)}>
              {C.retry}
            </button>
          )}
        </div>
      )}

      {editing && (
        <ItemEditForm
          api={api}
          item={item}
          initialFocus={editing}
          onConflict={onChange}
          onCancel={() => {
            returnFocus.current = true;
            setEditing(false);
          }}
          onSaved={(saved) => {
            onChange(saved);
            announcer.status(C.savedMsg(saved.title));
            returnFocus.current = true;
            setEditing(false);
          }}
        />
      )}

      {!isPolicy && !editing && <ItemCalendar api={api} item={item} onChange={onChange} />}

      {snippetPurged && !skipped && <p className="mt-2 text-sm text-muted">{C.purgedSnippet}</p>}

      {showSource && snippet && snippet.text !== null && !skipped && !editing && (
        <div className="mt-2">
          <button type="button" className="btn-quiet btn-sm px-0" aria-expanded={sourceOpen} aria-controls={sourceId} onClick={() => setSourceOpen((o) => !o)}>
            {C.sourceToggle}
          </button>
          <div id={sourceId} hidden={!sourceOpen} className="mt-1 space-y-2 border-l-4 border-line pl-3 text-sm">
            <p className="text-muted">{C.sourceFrom(snippet.page)}</p>
                <blockquote className="break-words">
                  <PlainText value={snippet.text} />
                </blockquote>
            {snippet.context && snippet.context.text !== snippet.text && (
              <>
                <p className="text-muted">{C.sourceAround}</p>
                <blockquote className="whitespace-pre-line break-words">
                  <PlainText value={snippet.context.text} />
                </blockquote>
              </>
            )}
          </div>
        </div>
      )}
    </li>
  );
}
