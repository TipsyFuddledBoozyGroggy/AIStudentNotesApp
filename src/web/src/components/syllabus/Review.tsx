"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { SyllabusApi } from "@/lib/syllabus/client";
import { batchEligible, clearItems, syncCandidates } from "@/lib/syllabus/eligibility";
import { weekRangeLabel } from "@/lib/syllabus/format";
import { decidable, firstOpenGroup, groupItems, type GroupBy, type ItemGroup } from "@/lib/syllabus/grouping";
import type { BatchResult, EditableKind, Syllabus, SyllabusItem } from "@/lib/syllabus/types";
import { useAnnouncer } from "../lecture/Announcer";
import { Disclosure } from "../ui/Disclosure";
import { Segmented } from "../ui/Segmented";
import { ActionProblem, problemKind, type ProblemKind } from "./ActionProblem";
import { BatchPanel, BatchUndo, type BatchDone } from "./Batch";
import { ConfirmDialog } from "./ConfirmDialog";
import { BATCH_COPY as B, DELETE_COPY as D, DEMO_TAGS, EXPORT_COPY as X, KIND_GROUP_LABEL, LIST_COPY as L, ONE_COPY, REVIEW_COPY as C, SYNC_COPY as S } from "./copy";
import { DemoLine } from "./DemoLine";
import { Chevron } from "./icons";
import { ItemRow } from "./ItemRow";
import { OneAtATime } from "./OneAtATime";
import { clearSyllabusDrafts, readLocal, writeLocal } from "./storage";
import { SyncConfirm, SyncResultView, type SentRun } from "./Sync";

export interface ReviewProps {
  api: SyllabusApi;
  syllabus: Syllabus;
  courseId: string;
  /** The syllabus was deleted (DELETE takes no parameters and removes its demo calendar events too). */
  onDeleted?: (id: string) => void;
  onAddAnother?: () => void;
  /** Present when the course has other syllabi. */
  onShowAll?: () => void;
}

type Mode = { m: "list" } | { m: "one"; start: string | null } | { m: "confirm" } | { m: "result"; run: SentRun };
type BatchTarget = { scope: string; ids: string[] };

const LONG_LIST = 40;
const isGroupBy = (v: unknown): v is GroupBy => v === "week" || v === "type";
const isViewMode = (v: unknown): v is "list" | "one" => v === "list" || v === "one";

function groupLabel(g: ItemGroup): string {
  if (g.kind === "week") return C.weekGroup(weekRangeLabel(g.value));
  if (g.kind === "type") return KIND_GROUP_LABEL[g.value as EditableKind];
  if (g.kind === "policy") return C.policyGroup;
  return C.needsDateGroup;
}

function defaultOpen(items: SyllabusItem[], by: GroupBy): Set<string> {
  const first = firstOpenGroup(groupItems(items, by));
  return new Set(first ? [first] : []);
}

function pages(list: number[]): string {
  return list.map((p) => `page ${p}`).join(", ");
}

/** Saves the export as a file. Never rendered; the browser's download keeps it out of the page. */
function download(text: string, name: string): boolean {
  try {
    if (typeof URL.createObjectURL !== "function") return false;
    const url = URL.createObjectURL(new Blob([text], { type: "application/json" }));
    const a = document.createElement("a");
    a.href = url;
    a.download = name;
    a.rel = "noopener";
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    return true;
  } catch {
    return false;
  }
}

/**
 * Review (UX 5-7). List: grouped by week or type, the first group with an undecided item expanded, a one-line summary
 * (no running announcements), "Needs a date" last and calm. Optional batch approve of clear items only (never offered
 * for a suspect, hidden-content or purged syllabus), "Review one at a time", the calendar step (confirm and result,
 * mock "Demo calendar" only), JSON export and delete. Item order and counts are computed from the items.
 */
export function Review({ api, syllabus, courseId, onDeleted, onAddAnother, onShowAll }: ReviewProps) {
  const announcer = useAnnouncer();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const groupButtons = useRef(new Map<string, HTMLButtonElement | null>());
  const [items, setItems] = useState<SyllabusItem[] | null>(null);
  const [loadFailed, setLoadFailed] = useState(false);
  const [by, setBy] = useState<GroupBy>(() => readLocal("groupBy", isGroupBy) ?? "week");
  const [open, setOpen] = useState<Set<string> | null>(null);
  const [focusGroup, setFocusGroup] = useState<string | null>(null);
  const [tick, setTick] = useState(0);
  const [mode, setMode] = useState<Mode>(() => (readLocal("mode", isViewMode) === "one" ? { m: "one", start: null } : { m: "list" }));
  const [focusHeading, setFocusHeading] = useState(true);
  const [batchTarget, setBatchTarget] = useState<BatchTarget | null>(null);
  const [batchDone, setBatchDone] = useState<BatchDone | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [dataProblem, setDataProblem] = useState<{ what: "delete" | "export"; kind: ProblemKind } | null>(null);
  const [exporting, setExporting] = useState(false);
  const dataProblemRef = useRef<HTMLDivElement>(null);
  const calendarStepRef = useRef<HTMLHeadingElement>(null);

  // A press that did not go through: focus its message (UX 8, the student caused it).
  useEffect(() => {
    if (dataProblem) dataProblemRef.current?.focus();
  }, [dataProblem]);

  useEffect(() => {
    let cancelled = false;
    setLoadFailed(false);
    api
      .listItems(syllabus.id)
      .then((list) => {
        if (cancelled) return;
        setItems(list);
        // Default expansion (UX 5.1), set in the same update as the data so it never renders all-collapsed first.
        setOpen((prev) => prev ?? defaultOpen(list, by));
      })
      .catch(() => !cancelled && setLoadFailed(true));
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the grouping only seeds the first expansion
  }, [api, syllabus.id, tick]);

  // The list's h1 takes focus when the list is shown by the student (first open, back from another view).
  useEffect(() => {
    if (mode.m !== "list" || !focusHeading) return;
    headingRef.current?.focus();
    setFocusHeading(false);
  }, [mode, focusHeading]);

  const groups = useMemo(() => (items ? groupItems(items, by) : []), [items, by]);

  useEffect(() => {
    if (!focusGroup) return;
    groupButtons.current.get(focusGroup)?.focus();
    setFocusGroup(null);
  }, [focusGroup, open]);

  const byRef = useRef(by);
  byRef.current = by;
  const itemsRef = useRef(items);
  itemsRef.current = items;
  const [focusItem, setFocusItem] = useState<{ id: string; target: "edit" | "row" } | null>(null);

  const applyItems = useCallback((changed: SyllabusItem[]) => {
    const map = new Map(changed.map((i) => [i.id, i]));
    const list = (itemsRef.current ?? []).map((i) => map.get(i.id) ?? i);
    itemsRef.current = list;
    setItems(list);
    return list;
  }, []);

  const onChange = useCallback(
    (next: SyllabusItem) => {
      const before = itemsRef.current?.find((i) => i.id === next.id);
      const list = applyItems([next]);
      if (before && before.decision !== next.decision) setBatchDone(null); // "Undo this" stays until the next decision
      // A date added to a "Needs a date" item moves it to its week or type group: open that group and keep focus on
      // the item's Edit button so focus is never lost (UX 9 focus map).
      if (before && before.when.status !== next.when.status) {
        const g = groupItems(list, byRef.current).find((x) => x.items.some((i) => i.id === next.id));
        if (g) setOpen((prev) => new Set([...(prev ?? []), g.key]));
        setFocusItem({ id: next.id, target: "edit" });
      }
    },
    [applyItems],
  );

  useEffect(() => {
    if (!focusItem || mode.m !== "list") return;
    const row = document.querySelector<HTMLElement>(`[data-item-id="${CSS.escape(focusItem.id)}"]`);
    const el = focusItem.target === "edit" ? row?.querySelector<HTMLElement>("[data-edit-button]") : row?.querySelector<HTMLElement>("button");
    el?.focus();
    setFocusItem(null);
  }, [focusItem, open, mode]);

  const toggle = (key: string) =>
    setOpen((prev) => {
      const s = new Set(prev ?? []);
      if (s.has(key)) s.delete(key);
      else s.add(key);
      return s;
    });

  const chooseBy = (v: GroupBy) => {
    setBy(v);
    setBatchTarget(null);
    if (items) setOpen(defaultOpen(items, v)); // a new grouping starts from the default expansion again
    writeLocal("groupBy", v);
  };

  const toMode = (next: Mode) => {
    setBatchTarget(null);
    if (next.m === "list" || next.m === "one") writeLocal("mode", next.m);
    setMode(next);
  };

  function exitOne(itemId: string | null) {
    toMode({ m: "list" });
    if (itemId && itemsRef.current) {
      const g = groupItems(itemsRef.current, byRef.current).find((x) => x.items.some((i) => i.id === itemId));
      if (g) setOpen((prev) => new Set([...(prev ?? []), g.key]));
      setFocusItem({ id: itemId, target: "row" });
    } else setFocusHeading(true);
  }

  function onBatchDone(r: BatchResult, done: BatchDone) {
    setBatchTarget(null);
    applyItems(r.results.flatMap((x) => (x.item ? [x.item] : [])));
    if (r.counts.rejected > 0) setTick((t) => t + 1); // re-read: a rejected item may have changed elsewhere
    setBatchDone(done);
    announcer.status(done.approvedIds.length === done.asked ? B.done(done.approvedIds.length) : B.partial(done.approvedIds.length, done.asked));
  }

  function onBatchUndone(r: BatchResult | null, problem: ProblemKind | null) {
    if (problem) return;
    if (r) {
      applyItems(r.results.flatMap((x) => (x.item ? [x.item] : [])));
      if (r.counts.rejected > 0) setTick((t) => t + 1);
      announcer.status(B.undone(r.counts.applied));
    }
    setBatchDone(null);
    setFocusHeading(true);
  }

  async function doDelete() {
    if (deleting) return;
    setDeleting(true);
    setDataProblem(null);
    try {
      await api.deleteSyllabus(syllabus.id);
      clearSyllabusDrafts(syllabus.id, courseId); // SB-06
      setConfirmDelete(false);
      announcer.status(D.done);
      onDeleted?.(syllabus.id);
    } catch (e) {
      const kind = problemKind(e);
      setConfirmDelete(false);
      if (kind === "gone") {
        clearSyllabusDrafts(syllabus.id, courseId);
        announcer.status(D.done);
        onDeleted?.(syllabus.id);
        return;
      }
      setDataProblem({ what: "delete", kind });
    } finally {
      setDeleting(false);
    }
  }

  async function doExport() {
    if (exporting) return;
    setExporting(true);
    setDataProblem(null);
    try {
      const text = await api.exportSyllabus(syllabus.id);
      if (!download(text, X.fileName)) throw new Error("download unavailable");
      announcer.status(X.done);
    } catch (e) {
      setDataProblem({ what: "export", kind: problemKind(e) });
    } finally {
      setExporting(false);
    }
  }

  const d = items ? decidable(items) : [];
  const approved = d.filter((i) => i.decision === "approved").length;
  const skipped = d.filter((i) => i.decision === "skipped").length;
  const undecided = d.length - approved - skipped;
  const needsDate = d.filter((i) => i.when.status === "needs_date").length;
  const allOpen = groups.length > 0 && groups.every((g) => open?.has(g.key));
  const purged = syllabus.retention.canonicalTextPurged;
  const { injectionSuspect, hiddenContentDropped } = syllabus.document;
  const clear = items ? clearItems(syllabus, items) : [];
  const candidates = items ? syncCandidates(items) : [];
  const byId = new Map((items ?? []).map((i) => [i.id, i]));
  const target = batchTarget ? batchTarget.ids.map((id) => byId.get(id)).filter((i): i is SyllabusItem => Boolean(i && batchEligible(i))) : [];

  if (items && mode.m === "one")
    return <OneAtATime api={api} syllabus={syllabus} items={items} startId={mode.start} onChange={onChange} onExit={exitOne} onToCalendar={() => toMode({ m: "confirm" })} />;
  if (items && mode.m === "confirm")
    return (
      <SyncConfirm
        api={api}
        syllabus={syllabus}
        items={items}
        onBack={() => {
          toMode({ m: "list" });
          setFocusHeading(true);
        }}
        onDone={(run) => {
          setBatchDone(null);
          setMode({ m: "result", run });
          setTick((t) => t + 1); // the result is history (it may be a replay): re-read the items for current state
        }}
      />
    );
  if (items && mode.m === "result")
    return (
      <SyncResultView
        api={api}
        syllabusId={syllabus.id}
        courseId={courseId}
        run={mode.run}
        items={items}
        onItemChange={onChange}
        onBack={() => {
          setTick((t) => t + 1);
          toMode({ m: "list" });
          setFocusHeading(true);
        }}
      />
    );

  const batchPanel = (scope: string) =>
    batchTarget?.scope === scope && target.length > 0 ? (
      <BatchPanel api={api} syllabusId={syllabus.id} selected={target} level={scope === "all" ? 2 : 3} onDone={onBatchDone} onCancel={() => setBatchTarget(null)} />
    ) : null;

  // Redesign-round2 slice 6 (wireframe 5.4): one display h1, the Demo lines (wording and tag presentation unchanged, G1),
  // a two-line summary, one toolbar row, then each group as a label-style disclosure over a ledger of item rows.
  // Slice 8c (PO-18b; make-notes-rail-demo-notice 3): the groups and the Calendar step share `.review-body`. Below 1280 it
  // is a plain 68ch block and the step is a Note in the flow after the groups, as slice 6 built it. From 1280 it is a grid
  // and the step sits in `.review-rail`, a reserved 20rem track to the right of the groups, sticky under the header: no row
  // can scroll under it, so "one fill always in view" holds on desktop without covering a target (SC 2.5.8). DOM order is
  // unchanged (groups, then the step), so the Tab order, the skip link and the h2 target are unchanged. The prose blocks
  // above and below keep their 68ch measure.
  return (
    <div>
      <div className="max-w-prose">
      <h1 ref={headingRef} tabIndex={-1} className="display text-display">
        {C.h1}
      </h1>
      <div className="mt-3 space-y-1">
        <DemoLine text={DEMO_TAGS.calendar} tag={S.tagWord} />
        {syllabus.demoSource && <DemoLine text={DEMO_TAGS.source} />}
      </div>

      {syllabus.status === "partial" && (
        <div className="note note-notice mt-6 space-y-1">
          <p className="font-semibold">{C.partial}</p>
          {syllabus.unprocessedRanges.length > 0 && (
            <p>{C.partialPages(syllabus.unprocessedRanges.map((r) => (r.fromPage === r.toPage ? `page ${r.fromPage}` : `pages ${r.fromPage} to ${r.toPage}`)).join(", "))}</p>
          )}
          {syllabus.document.unreadablePages.length > 0 && <p>{C.unreadablePages(pages(syllabus.document.unreadablePages))}</p>}
        </div>
      )}
      {(hiddenContentDropped || injectionSuspect) && (
        <div className="note note-notice mt-6">
          <p>{hiddenContentDropped ? C.hostile : C.suspect}</p>
        </div>
      )}
      {purged && (
        <div className="note note-neutral mt-6">
          <p>{C.purged}</p>
        </div>
      )}

      {loadFailed && (
        <div className="note note-notice mt-6 space-y-3">
          <p>{C.itemsError}</p>
          <button type="button" className="btn-secondary" onClick={() => setTick((t) => t + 1)}>
            {C.tryAgain}
          </button>
        </div>
      )}
      {!items && !loadFailed && <p className="mt-6 text-muted">{C.loadingItems}</p>}

      {items && d.length === 0 && (
        <div className="mt-6 space-y-2">
          <p className="font-semibold">{C.empty}</p>
          <p className="text-muted">{C.emptyBody}</p>
        </div>
      )}

      {items && d.length > 0 && (
        <>
          <section aria-label={C.summaryRegion} className="mt-6">
            <p className="flex flex-wrap items-baseline gap-x-4">
              <span>{C.summary(d.length, approved, skipped, undecided)}</span>
              {needsDate > 0 && (
                <span className="inline-flex flex-wrap items-baseline gap-x-1">
                  <span>{C.needsDate(needsDate)}</span>
                  <button
                    type="button"
                    className="btn-tertiary btn-sm"
                    onClick={() => {
                      setOpen((prev) => new Set([...(prev ?? []), "needs_date"]));
                      setFocusGroup("needs_date");
                    }}
                  >
                    {C.needsDateShow}
                  </button>
                </span>
              )}
            </p>
            <p className="help">{C.timezone(syllabus.context.defaultTimezone)}</p>
            {d.length > LONG_LIST && <p className="help">{C.long}</p>}
            {/* A skip link for keyboard users (UX 9): shown when it has focus, like the lecture's skip links. */}
            <button
              type="button"
              className="sr-only focus:not-sr-only focus:fixed focus:left-2 focus:top-2 focus:z-50 focus:inline-flex focus:min-h-[44px] focus:items-center focus:rounded-control focus:border-2 focus:border-line focus:bg-surface focus:px-4 focus:font-semibold focus:text-ink"
              onClick={() => calendarStepRef.current?.focus()}
            >
              {S.skipTo}
            </button>
          </section>

          {batchDone && (
            <div className="mt-6">
              <BatchUndo api={api} syllabusId={syllabus.id} done={batchDone} items={items} onUndone={onBatchUndone} />
            </div>
          )}

          {/* Toolbar: Group by, then the two quiet view controls on the segmented control's row (redesign-round2 5.4). */}
          <div className="mt-6 flex flex-wrap items-end gap-x-2 gap-y-3">
            <Segmented
              legend={C.groupByLegend}
              value={by}
              onChange={chooseBy}
              options={[
                { value: "week", label: C.groupByWeek },
                { value: "type", label: C.groupByType },
              ]}
            />
            <button type="button" className="btn-tertiary btn-sm" onClick={() => toMode({ m: "one", start: null })}>
              {ONE_COPY.open}
            </button>
            <button type="button" className="btn-tertiary btn-sm" onClick={() => setOpen(allOpen ? new Set() : new Set(groups.map((g) => g.key)))}>
              {allOpen ? C.collapseAll : C.expandAll}
            </button>
          </div>

          {clear.length > 0 && (
            <div className="mt-3 space-y-3">
              <Disclosure summary={B.menu}>
                <div className="mt-2 space-y-2">
                  <p className="help">{B.what}</p>
                  <button type="button" className="btn-secondary btn-sm" onClick={() => setBatchTarget({ scope: "all", ids: clear.map((i) => i.id) })}>
                    {B.all(clear.length)}
                  </button>
                </div>
              </Disclosure>
              {batchPanel("all")}
            </div>
          )}
        </>
      )}
      </div>

      {items && d.length > 0 && (
        <div className="review-body mt-6">
          <div className="review-groups space-y-4">
            {groups.map((g) => {
              const isOpen = open?.has(g.key) ?? false;
              const label = groupLabel(g);
              const u = g.kind === "policy" ? 0 : g.items.filter((i) => i.decision === "undecided").length;
              const listId = `group-${g.key.replace(/[^a-z0-9-]/gi, "-")}`;
              const groupClear = clear.filter((c) => g.items.some((i) => i.id === c.id));
              return (
                <section key={g.key} aria-labelledby={`${listId}-h`}>
                  <h2 id={`${listId}-h`}>
                    <button
                      ref={(el) => {
                        groupButtons.current.set(g.key, el);
                      }}
                      type="button"
                      aria-expanded={isOpen}
                      aria-controls={listId}
                      onClick={() => toggle(g.key)}
                      className="group-head min-h-[44px]"
                    >
                      <Chevron open={isOpen} />
                      <span>
                        <span className="font-bold text-ink">{label}</span>
                        <span className="text-muted">
                          {g.kind === "policy" ? `, ${g.items.length} ${g.items.length === 1 ? "item" : "items"}` : C.groupName("", g.items.length, u)}
                        </span>
                      </span>
                    </button>
                  </h2>
                  <div id={listId} hidden={!isOpen}>
                    {g.kind === "policy" && <p className="help mb-2">{C.policyNote}</p>}
                    {groupClear.length > 0 && (g.kind === "week" || g.kind === "type") && (
                      <button
                        type="button"
                        className="btn-tertiary btn-sm optical-start"
                        aria-label={B.groupName(g.kind === "week" ? B.week(groupClear.length) : B.group(groupClear.length), label)}
                        onClick={() => setBatchTarget({ scope: g.key, ids: groupClear.map((i) => i.id) })}
                      >
                        {g.kind === "week" ? B.week(groupClear.length) : B.group(groupClear.length)}
                      </button>
                    )}
                    {batchPanel(g.key)}
                    <ul className="ledger">
                      {g.items.map((i) => (
                        <ItemRow key={i.id} api={api} item={i} defaultTimezone={syllabus.context.defaultTimezone} purged={purged} onChange={onChange} />
                      ))}
                    </ul>
                  </div>
                </section>
              );
            })}
          </div>

          <div className="review-rail">
            <section aria-labelledby="calendar-step-h" className="note note-neutral calendar-step mt-6" data-calendar-step="">
              <h2 id="calendar-step-h" ref={calendarStepRef} tabIndex={-1} className="note-title">
                {S.barRegion}
              </h2>
              {/* Plain text, not live: the status region already announces each decision. */}
              <p className="help" data-calendar-counts="">
                {S.railCounts(approved, skipped, undecided)}
              </p>
              <div className="calendar-step-action mt-3 flex flex-wrap items-center gap-x-4 gap-y-2">
                <button
                  type="button"
                  className="btn-primary"
                  aria-disabled={candidates.length === 0 ? true : undefined}
                  aria-describedby={candidates.length === 0 ? "calendar-step-hint" : undefined}
                  onClick={() => candidates.length > 0 && toMode({ m: "confirm" })}
                >
                  {S.bar(candidates.length)}
                </button>
                {candidates.length === 0 && (
                  <p id="calendar-step-hint" className="help">
                    {S.barHint}
                  </p>
                )}
              </div>
            </section>
          </div>
        </div>
      )}

      <div className="max-w-prose">
      {items && (
        <section aria-labelledby="syllabus-data-h" className="mt-12 space-y-3">
          <h2 id="syllabus-data-h" className="font-semibold text-ink">
            {X.heading}
          </h2>
          <p className="help">{X.hint}</p>
          <div className="flex flex-wrap gap-x-2">
            <button type="button" className="btn-tertiary btn-sm optical-start" aria-disabled={exporting ? true : undefined} onClick={() => void doExport()}>
              {exporting ? X.busy : X.button}
            </button>
            <button type="button" className="btn-tertiary btn-sm" onClick={() => setConfirmDelete(true)}>
              {D.button}
            </button>
          </div>
          {dataProblem && <ActionProblem ref={dataProblemRef} kind={dataProblem.kind} />}
        </section>
      )}

      <div className="mt-12 flex flex-wrap gap-x-2">
        {onAddAnother && (
          <button type="button" className="btn-tertiary optical-start" onClick={onAddAnother}>
            {L.addAnother}
          </button>
        )}
        {onShowAll && (
          <button type="button" className={`btn-tertiary ${onAddAnother ? "" : "optical-start"}`} onClick={onShowAll}>
            {L.all}
          </button>
        )}
        <Link href={`/courses/${encodeURIComponent(courseId)}`} className={`btn-tertiary ${onAddAnother || onShowAll ? "" : "optical-start"}`}>
          {C.back}
        </Link>
      </div>
      </div>

      <ConfirmDialog
        open={confirmDelete}
        heading={D.heading}
        confirmLabel={D.confirm}
        cancelLabel={D.keep}
        busy={deleting}
        busyLabel={D.busy}
        onConfirm={() => void doDelete()}
        onCancel={() => setConfirmDelete(false)}
      >
        <p>{D.body}</p>
        <p>{D.copies}</p>
      </ConfirmDialog>
    </div>
  );
}
