"use client";

import { useCallback, useEffect, useRef, useState, type RefObject } from "react";
import { PlainText } from "@/lib/lecture/PlainTextSpan";
import type { SyllabusApi } from "@/lib/syllabus/client";
import type { Snippet, Syllabus, SyllabusItem, UnclaimedDate } from "@/lib/syllabus/types";
import { useAnnouncer } from "../lecture/Announcer";
import { ITEM_COPY, UNCLAIMED_COPY as U } from "./copy";
import { Chevron } from "./icons";
import { isRecord, readDraftsUnder, removeDraft, writeDraft } from "./storage";
import { UnclaimedAddForm } from "./UnclaimedAddForm";

// PO-20b / SBV-05 (docs/ux/unclaimed-dates-list.md): "Dates we noticed", the last section of the Review groups. Each row
// is a line from the syllabus with the date span the server found in bold; the student can add it as an item or hide it.
// Hide and "added" live on this device only (v1: the contract has no route for them), as ids, never text.

const PAGE = 25;

/** v1 local state of one row (unclaimed-dates-list.md 4.6). Ids only. */
export type UnclaimedState = { s: "hidden" } | { s: "added"; itemId: string };
const isState = (v: unknown): v is UnclaimedState =>
  isRecord(v) && ((v.s === "hidden" && Object.keys(v).length === 1) || (v.s === "added" && typeof v.itemId === "string" && Object.keys(v).length === 2));
export const stateKey = (syllabusId: string, unclaimedId: string) => `unclaimed.${syllabusId}.${unclaimedId}`;

/** The row's quote: the line with the date span in bold when the offsets agree with the text; otherwise the two blocks. */
function Quote({ s }: { s: Snippet }) {
  const text = s.text as string;
  const ctx = s.context;
  if (ctx && s.start !== null && s.end !== null) {
    const a = s.start - ctx.start;
    const b = s.end - ctx.start;
    if (a >= 0 && a < b && b <= ctx.text.length && ctx.text.slice(a, b) === text) {
      return (
        <blockquote className="whitespace-pre-line break-words">
          <PlainText value={ctx.text.slice(0, a)} />
          <strong className="font-semibold">
            <PlainText value={text} />
          </strong>
          <PlainText value={ctx.text.slice(b)} />
        </blockquote>
      );
    }
  }
  return (
    <>
      <blockquote className="whitespace-pre-line break-words">
        <PlainText value={text} />
      </blockquote>
      {ctx && ctx.text !== text && (
        <>
          <p className="text-sm text-muted">{ITEM_COPY.sourceAround}</p>
          <blockquote className="whitespace-pre-line break-words">
            <PlainText value={ctx.text} />
          </blockquote>
        </>
      )}
    </>
  );
}

interface RowProps {
  api: SyllabusApi;
  syllabusId: string;
  row: UnclaimedDate;
  state: UnclaimedState | undefined;
  /** The title of the item this row was added as (only when state is "added" and the item exists). */
  addedTitle: string | null;
  onHide(): void;
  onUndo(): void;
  onCreated(item: SyllabusItem): void;
  onGoTo(): void;
}

function UnclaimedRow({ api, syllabusId, row, state, addedTitle, onHide, onUndo, onCreated, onGoTo }: RowProps) {
  const [formOpen, setFormOpen] = useState(false);
  const addBtn = useRef<HTMLButtonElement>(null);
  const returnFocus = useRef(false);
  const text = row.snippet.text as string;
  const hidden = state?.s === "hidden";
  const added = state?.s === "added" && addedTitle !== null;

  useEffect(() => {
    if (!formOpen && returnFocus.current) {
      returnFocus.current = false;
      addBtn.current?.focus(); // Cancel / Escape -> Add as item (spec 6)
    }
  }, [formOpen]);

  return (
    <li className="ledger-row item-row" data-unclaimed-id={row.id}>
      <div className="item-main">
        {hidden ? (
          <p className="text-sm text-muted">
            {U.hidden}{" "}
            <strong className="font-semibold text-ink">
              <PlainText value={text} />
            </strong>
          </p>
        ) : added ? (
          <p className="text-sm">
            {U.added} <PlainText value={addedTitle} />
          </p>
        ) : (
          <>
            <p className="text-sm text-muted">{ITEM_COPY.sourceFrom(row.snippet.page)}</p>
            <div className="mt-1 space-y-2 border-l-2 border-hair pl-3">
              <Quote s={row.snippet} />
            </div>
          </>
        )}
      </div>
      {formOpen && !hidden && !added && (
        <div className="item-extra">
          <UnclaimedAddForm
            api={api}
            syllabusId={syllabusId}
            row={row}
            onCancel={() => {
              returnFocus.current = true;
              setFormOpen(false);
            }}
            onCreated={(item) => {
              setFormOpen(false);
              onCreated(item);
            }}
          />
        </div>
      )}
      <div className="item-actions flex flex-wrap items-center gap-x-1">
        {added ? (
          <button key="goto" type="button" className="btn-tertiary btn-sm" data-unc="goto" aria-label={U.goToItemName(addedTitle)} onClick={onGoTo}>
            {U.goToItem}
          </button>
        ) : hidden ? null : (
          <button
            key="add"
            ref={addBtn}
            type="button"
            className="btn-secondary btn-sm"
            data-unc="add"
            aria-label={U.addName(text)}
            aria-expanded={formOpen}
            onClick={() => setFormOpen(true)}
          >
            {U.add}
          </button>
        )}
        {/* One element for Hide and Undo, so focus stays on it when it changes (spec 4.5). Not offered while the form is open. */}
        {!added && !formOpen && (
          <button
            key="hide"
            type="button"
            className="btn-tertiary btn-sm"
            data-unc="hide"
            aria-label={hidden ? U.undoName(text) : U.hideName(text)}
            onClick={hidden ? onUndo : onHide}
          >
            {hidden ? U.undo : U.hide}
          </button>
        )}
      </div>
    </li>
  );
}

export interface UnclaimedDatesProps {
  api: SyllabusApi;
  syllabus: Syllabus;
  items: SyllabusItem[];
  open: boolean;
  onToggle(): void;
  headRef: RefObject<HTMLButtonElement | null>;
  /** A student item was created from a row (201): the parent inserts it and opens its group. */
  onAdded(item: SyllabusItem): void;
  /** "Go to the item": the parent opens the item's group and focuses its first button. */
  onGoToItem(itemId: string): void;
}

type Load = { k: "idle" } | { k: "loading" } | { k: "error" } | { k: "done"; rows: UnclaimedDate[] };
type FocusReq = { id: string; target: "add" | "goto" | "first" };

/**
 * The section (spec 4.1 to 4.12). Collapsed unless the parent opens it; fetched once, on first open; 25 rows at a time.
 * No live region: announcements go through the shared announcer (exactly three regions stay).
 */
export function UnclaimedDates({ api, syllabus, items, open, onToggle, headRef, onAdded, onGoToItem }: UnclaimedDatesProps) {
  const announcer = useAnnouncer();
  const sid = syllabus.id;
  const itemIds = new Set(items.map((i) => i.id));
  // Local states as they stood at mount, with an "added" whose item is gone treated as kept (spec 4.7).
  const [mount] = useState(() => {
    const all = readDraftsUnder(`unclaimed.${sid}.`, isState);
    const stale = all.filter(([, st]) => st.s === "added" && !itemIds.has(st.itemId)).map(([k]) => k);
    const kept = all.filter(([k]) => !stale.includes(k));
    return { states: Object.fromEntries(kept) as Record<string, UnclaimedState>, stale, handled: new Set(kept.map(([k]) => k)) };
  });
  const [states, setStates] = useState<Record<string, UnclaimedState>>(mount.states);
  const [load, setLoad] = useState<Load>({ k: "idle" });
  const [shown, setShown] = useState(PAGE);
  const [revealHandled, setRevealHandled] = useState(false);
  const [focusReq, setFocusReq] = useState<FocusReq | null>(null);
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    for (const k of mount.stale) removeDraft(stateKey(sid, k));
    return () => {
      alive.current = false;
    };
  }, [mount, sid]);

  const fetchRows = useCallback(() => {
    setLoad({ k: "loading" });
    api
      .listUnclaimedDates(sid)
      .then((rows) => alive.current && setLoad({ k: "done", rows }))
      .catch(() => alive.current && setLoad({ k: "error" }));
  }, [api, sid]);

  // Fetched on the first open only (spec 4.3); never on a tick re-read.
  useEffect(() => {
    if (open && load.k === "idle") fetchRows();
  }, [open, load.k, fetchRows]);

  useEffect(() => {
    if (!focusReq) return;
    const li = document.querySelector<HTMLElement>(`[data-unclaimed-id="${CSS.escape(focusReq.id)}"]`);
    const el = focusReq.target === "first" ? li?.querySelector<HTMLElement>(".item-actions button") : li?.querySelector<HTMLElement>(`[data-unc="${focusReq.target}"]`);
    el?.focus();
    setFocusReq(null);
  }, [focusReq, shown, revealHandled, states]);

  // An "added" row whose item no longer exists (deleted elsewhere, then re-read) is kept again (spec 4.7, 4.11).
  const effective = (id: string): UnclaimedState | undefined => {
    const st = states[id];
    return st?.s === "added" && !itemIds.has(st.itemId) ? undefined : st;
  };

  const setState = (id: string, st: UnclaimedState | undefined) => {
    setStates((prev) => {
      const next = { ...prev };
      if (st) next[id] = st;
      else delete next[id];
      return next;
    });
    if (st) writeDraft(stateKey(sid, id), st);
    else removeDraft(stateKey(sid, id));
  };

  const rows = load.k === "done" ? load.rows.filter((r) => r.snippet.text !== null) : null; // a null text cannot occur unpurged; skipped
  const keptCount = rows ? rows.filter((r) => !effective(r.id)).length : Math.max(0, syllabus.counts.unclaimedDates - Object.keys(mount.states).length);
  const paged = rows ? rows.filter((r) => !mount.handled.has(r.id)) : [];
  const handledRows = rows ? rows.filter((r) => mount.handled.has(r.id)) : [];
  const visible = paged.slice(0, shown);
  const left = paged.length - shown;
  const byItem = new Map(items.map((i) => [i.id, i.title]));

  const renderRow = (r: UnclaimedDate) => {
    const st = effective(r.id);
    const text = r.snippet.text as string;
    return (
      <UnclaimedRow
        key={r.id}
        api={api}
        syllabusId={sid}
        row={r}
        state={st}
        addedTitle={st?.s === "added" ? (byItem.get(st.itemId) ?? null) : null}
        onHide={() => {
          setState(r.id, { s: "hidden" });
          announcer.status(U.hiddenMsg(text));
        }}
        onUndo={() => {
          setState(r.id, undefined);
          announcer.status(U.shownMsg(text));
        }}
        onCreated={(item) => {
          setState(r.id, { s: "added", itemId: item.id });
          onAdded(item);
          setFocusReq({ id: r.id, target: "goto" });
          announcer.status(U.addedMsg(item.title));
        }}
        onGoTo={() => {
          const st2 = states[r.id];
          if (st2?.s === "added") onGoToItem(st2.itemId);
        }}
      />
    );
  };

  return (
    <section aria-labelledby="unclaimed-h" data-unclaimed-section="">
      <h2 id="unclaimed-h">
        <button ref={headRef} type="button" aria-expanded={open} aria-controls="unclaimed-list" onClick={onToggle} className="group-head min-h-[44px]">
          <Chevron open={open} />
          <span>
            <span className="font-bold text-ink">{U.label}</span>
            <span className="text-muted">, {keptCount > 0 ? U.lines(keptCount) : U.allHandled}</span>
          </span>
        </button>
      </h2>
      <div hidden={!open} data-unclaimed-body="">
        <div className="mb-2 space-y-1">
          <p className="help">{U.help}</p>
          <p className="help">{U.help2}</p>
          <p className="help">{U.help3}</p>
        </div>
        {load.k === "loading" && <p className="help">{U.loading}</p>}
        {load.k === "error" && (
          <div className="note note-notice space-y-3">
            <p>{U.loadError}</p>
            <button type="button" className="btn-secondary" onClick={fetchRows}>
              {U.tryAgain}
            </button>
          </div>
        )}
        <ul id="unclaimed-list" className="ledger">
          {visible.map(renderRow)}
          {revealHandled && handledRows.map(renderRow)}
        </ul>
        {(left > 0 || (!revealHandled && handledRows.length > 0)) && (
          <div className="mt-2 flex flex-wrap gap-x-2">
            {left > 0 && (
              <button
                type="button"
                className="btn-tertiary btn-sm optical-start"
                onClick={() => {
                  const next = shown + PAGE;
                  setShown(next);
                  // The press that shows the last rows removes the button: focus the first new row (spec 4.12).
                  if (next >= paged.length && paged[shown]) setFocusReq({ id: paged[shown].id, target: "add" });
                }}
              >
                {U.showMore(Math.min(PAGE, left))}
              </button>
            )}
            {!revealHandled && handledRows.length > 0 && (
              <button
                type="button"
                className={`btn-tertiary btn-sm ${left > 0 ? "" : "optical-start"}`}
                onClick={() => {
                  setRevealHandled(true);
                  setFocusReq({ id: handledRows[0].id, target: "first" });
                }}
              >
                {U.showHandled(handledRows.length)}
              </button>
            )}
          </div>
        )}
      </div>
    </section>
  );
}
