"use client";

import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { ApiError } from "@/lib/api/errors";
import { PlainText } from "@/lib/lecture/PlainTextSpan";
import type { SyllabusApi } from "@/lib/syllabus/client";
import { dateRange, formatDate, KIND_LABEL, todayYmd, weekdayList, whenLine } from "@/lib/syllabus/format";
import type { Decision, SyllabusItem } from "@/lib/syllabus/types";
import { useAnnouncer } from "../lecture/Announcer";
import { ActionProblem } from "./ActionProblem";
import { ACTION_COPY, ITEM_COPY as C } from "./copy";
import { Chevron, ConfidenceIcon, DecisionIcon } from "./icons";
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

/** Meta-line segments parted by a middot that is not read out. */
function Meta({ parts }: { parts: ReactNode[] }) {
  const shown = parts.filter((p) => p !== null && p !== false && p !== undefined);
  return (
    <>
      {shown.map((p, i) => (
        <span key={i} className="inline-flex items-center gap-x-2">
          {i > 0 && (
            <span aria-hidden="true" className="text-muted">
              ·
            </span>
          )}
          {p}
        </span>
      ))}
    </>
  );
}

/**
 * One item as a ledger row (UX 5.2-5.7; redesign-round2 slice 6, wireframe 5.4). Line 1: title (h3, so heading navigation
 * steps through items) with its date and "Date has passed"; the actions on the right from 768 (Approve bordered, Skip and
 * Edit quiet). Line 2: kind, recurrence, confidence (visible only when it is not "Looks right"; the words stay in the
 * accessible text), decision word, "You edited this"; the source disclosure as a link on the right; the quote opens by
 * default only on a decidable `please_check` row; policy rows always start closed (docs/ux/policy-row-source-open.md). Untrusted text
 * (title, snippet, context, notes, reasons) is rendered only as plain text nodes. Approve and Skip turn into Undo in the
 * same position, so focus stays on the pressed control; Undo never expires. The visible state is always the server's
 * state: a failed save leaves the row as it was and offers "Try again" beside it.
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
  const [sourceOpen, setSourceOpen] = useState(!isPolicy && item.confidence === "please_check");
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
  const clear = item.confidence === "looks_right";

  // Approve position: Approve or Undo (or Add date for an undated item; it cannot be approved, contract 6.1). The one
  // bordered control in the row (redesign-round2 5.4).
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
    <button type="button" className="btn-tertiary btn-sm" aria-label={C.undoSkipName(title)} aria-busy={busy || undefined} onClick={() => decide({ target: "undecided", from: "skip" })}>
      {C.undo}
    </button>
  ) : (
    <button type="button" className="btn-tertiary btn-sm" aria-label={C.skipName(title)} aria-busy={busy || undefined} onClick={() => decide({ target: "skipped", from: "skip" })}>
      {C.skip}
    </button>
  );

  const extras: ReactNode[] = [];
  if (needsDate && item.when.unknownReason)
    extras.push(
      <p key="needs" className="text-sm">
        {C.needsDate[item.when.unknownReason]}
        {item.when.alternatives.length > 0 && <> {C.alternatives(item.when.alternatives.map((a) => formatDate(a.date)).join(" or "))}</>}
      </p>,
    );
  if (!isPolicy && item.confidence === "please_check" && !skipped)
    extras.push(
      <p key="reason" className="text-sm text-muted">
        <PlainText value={item.confidenceReason} />
      </p>,
    );
  // The source quote comes before the hint and the actions (redesign-round2-review 4): what is to be checked first.
  const sourceShown = showSource && snippet && snippet.text !== null && !skipped && !editing;
  if (sourceShown)
    extras.push(
      <div key="source" id={sourceId} hidden={!sourceOpen} className="space-y-2 border-l-2 border-hair pl-3 text-sm">
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
      </div>,
    );
  if (!isPolicy && item.confidence === "please_check" && !needsDate && item.decision !== "approved")
    extras.push(
      <p key="hint" id={checkHintId} className="help">
        {C.checkHint}
      </p>,
    );
  if (item.notes)
    extras.push(
      <p key="notes" className="whitespace-pre-line text-sm">
        <PlainText value={item.notes} />
      </p>,
    );
  if (failed?.kind === "session") extras.push(<ActionProblem key="session" kind="session" />);
  if (failed && failed.kind !== "session")
    extras.push(
      <div key="failed" className="flex flex-wrap items-center gap-3 text-sm">
        <p className="font-semibold">{failed.kind === "conflict" ? C.changedElsewhere : C.saveFailed}</p>
        {failed.retry && (
          <button type="button" className="btn-secondary btn-sm" onClick={() => failed.retry && decide(failed.retry)}>
            {C.retry}
          </button>
        )}
      </div>,
    );
  if (editing)
    extras.push(
      <ItemEditForm
        key="edit"
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
      />,
    );
  if (!isPolicy && !editing) extras.push(<ItemCalendar key="cal" api={api} item={item} onChange={onChange} />);
  if (snippetPurged && !skipped)
    extras.push(
      <p key="purged" className="text-sm text-muted">
        {C.purgedSnippet}
      </p>,
    );

  return (
    <li aria-labelledby={titleId} className="ledger-row item-row" data-item-id={item.id}>
      <div className="item-main flex flex-wrap items-baseline gap-x-3">
        <h3 id={titleId} className="break-words text-lg font-bold text-ink">
          <PlainText value={title} />
        </h3>
        {when && <span className="text-sm">{when}</span>}
        {zone && <span className="text-sm text-muted">({zone})</span>}
        {past && <span className="text-sm text-muted">{C.past}</span>}
      </div>

      <p className="item-meta flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-muted">
        <Meta
          parts={[
            // Confidence: "Looks right" stays in the accessible text (after the kind) but is not drawn, so only a row that
            // needs a look carries a word and a shape (redesign-round2 5.4; the icon and word stay visible in one-at-a-time).
            <span key="kind">
              <span>{KIND_LABEL[item.kind]}</span>
              {!isPolicy && clear && <span className="sr-only">, {C.conf[item.confidence]}</span>}
            </span>,
            item.recurrence && <span key="rep">{C.repeats(weekdayList(item.recurrence.byDay), dateRange(item.recurrence.from, item.recurrence.until))}</span>,
            !isPolicy && !clear && (
              <span key="conf" className="inline-flex items-center gap-1 font-semibold text-ink">
                <ConfidenceIcon value={item.confidence} />
                {C.conf[item.confidence]}
              </span>
            ),
            !isPolicy && (
              <span key="dec" className={`inline-flex items-center gap-1 ${item.decision === "undecided" ? "" : "font-semibold text-ink"}`}>
                <DecisionIcon value={item.decision} />
                {C.status[item.decision]}
              </span>
            ),
            !isPolicy && item.edited && <span key="edited">{C.edited}</span>,
          ]}
        />
      </p>

      {sourceShown && (
        <button
          type="button"
          className="item-source item-source-toggle link inline-flex min-h-[44px] items-center gap-2 rounded-control text-sm"
          aria-expanded={sourceOpen}
          aria-controls={sourceId}
          onClick={() => setSourceOpen((o) => !o)}
        >
          <Chevron open={sourceOpen} />
          {C.sourceToggle}
        </button>
      )}

      {extras.length > 0 && <div className="item-extra">{extras}</div>}

      {!isPolicy && (
        <div className="item-actions flex flex-wrap items-center gap-x-1">
          {approveBtn}
          {skipBtn}
          {!needsDate && (
            <button ref={editBtn} data-edit-button="" type="button" className="btn-tertiary btn-sm" aria-label={C.editName(title)} aria-expanded={editing ? true : false} onClick={() => setEditing("title")}>
              {C.edit}
            </button>
          )}
        </div>
      )}
    </li>
  );
}
