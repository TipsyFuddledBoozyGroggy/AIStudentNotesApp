"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { SyllabusApi } from "@/lib/syllabus/client";
import { weekRangeLabel } from "@/lib/syllabus/format";
import { decidable, firstOpenGroup, groupItems, type GroupBy, type ItemGroup } from "@/lib/syllabus/grouping";
import type { EditableKind, Syllabus, SyllabusItem } from "@/lib/syllabus/types";
import { Segmented } from "../ui/Segmented";
import { DEMO_TAGS, KIND_GROUP_LABEL, REVIEW_COPY as C } from "./copy";
import { DemoLine } from "./DemoLine";
import { Chevron } from "./icons";
import { ItemRow } from "./ItemRow";
import { readLocal, writeLocal } from "./storage";

export interface ReviewProps {
  api: SyllabusApi;
  syllabus: Syllabus;
  courseId: string;
}

const LONG_LIST = 40;
const isGroupBy = (v: unknown): v is GroupBy => v === "week" || v === "type";

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

/**
 * Review list (UX 5.1-5.7, F1 scope): grouped by week or type, the first group with an undecided item expanded,
 * a one-line summary (no running announcements), "Needs a date" last and calm. No batch approve in F1; a suspect or
 * purged syllabus says why each item needs its own decision. Item order and counts are computed from the items.
 */
export function Review({ api, syllabus, courseId }: ReviewProps) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  const groupButtons = useRef(new Map<string, HTMLButtonElement | null>());
  const [items, setItems] = useState<SyllabusItem[] | null>(null);
  const [loadFailed, setLoadFailed] = useState(false);
  const [by, setBy] = useState<GroupBy>(() => readLocal("groupBy", isGroupBy) ?? "week");
  const [open, setOpen] = useState<Set<string> | null>(null);
  const [focusGroup, setFocusGroup] = useState<string | null>(null);
  const [tick, setTick] = useState(0);

  useEffect(() => headingRef.current?.focus(), []);

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
  const [focusItem, setFocusItem] = useState<string | null>(null);

  const onChange = useCallback((next: SyllabusItem) => {
    const before = itemsRef.current?.find((i) => i.id === next.id);
    const list = (itemsRef.current ?? []).map((i) => (i.id === next.id ? next : i));
    setItems(list);
    // A date added to a "Needs a date" item moves it to its week or type group: open that group and keep focus on
    // the item's Edit button so focus is never lost (UX 9 focus map).
    if (before && before.when.status !== next.when.status) {
      const g = groupItems(list, byRef.current).find((x) => x.items.some((i) => i.id === next.id));
      if (g) setOpen((prev) => new Set([...(prev ?? []), g.key]));
      setFocusItem(next.id);
    }
  }, []);

  useEffect(() => {
    if (!focusItem) return;
    document.querySelector<HTMLElement>(`[data-item-id="${CSS.escape(focusItem)}"] [data-edit-button]`)?.focus();
    setFocusItem(null);
  }, [focusItem, open]);

  const toggle = (key: string) =>
    setOpen((prev) => {
      const s = new Set(prev ?? []);
      if (s.has(key)) s.delete(key);
      else s.add(key);
      return s;
    });

  const chooseBy = (v: GroupBy) => {
    setBy(v);
    if (items) setOpen(defaultOpen(items, v)); // a new grouping starts from the default expansion again
    writeLocal("groupBy", v);
  };

  const d = items ? decidable(items) : [];
  const approved = d.filter((i) => i.decision === "approved").length;
  const skipped = d.filter((i) => i.decision === "skipped").length;
  const undecided = d.length - approved - skipped;
  const needsDate = d.filter((i) => i.when.status === "needs_date").length;
  const allOpen = groups.length > 0 && groups.every((g) => open?.has(g.key));
  const purged = syllabus.retention.canonicalTextPurged;
  const { injectionSuspect, hiddenContentDropped } = syllabus.document;

  return (
    <div className="max-w-prose space-y-6">
      <div className="space-y-3">
        <h1 ref={headingRef} tabIndex={-1} className="display text-3xl">
          {C.h1}
        </h1>
        <DemoLine text={DEMO_TAGS.calendar} />
        {syllabus.demoSource && <DemoLine text={DEMO_TAGS.source} />}
      </div>

      {syllabus.status === "partial" && (
        <div className="panel panel-notice space-y-1">
          <p className="font-semibold">{C.partial}</p>
          {syllabus.unprocessedRanges.length > 0 && (
            <p>{C.partialPages(syllabus.unprocessedRanges.map((r) => (r.fromPage === r.toPage ? `page ${r.fromPage}` : `pages ${r.fromPage} to ${r.toPage}`)).join(", "))}</p>
          )}
          {syllabus.document.unreadablePages.length > 0 && <p>{C.unreadablePages(pages(syllabus.document.unreadablePages))}</p>}
        </div>
      )}
      {(hiddenContentDropped || injectionSuspect) && (
        <div className="panel panel-notice">
          <p>{hiddenContentDropped ? C.hostile : C.suspect}</p>
        </div>
      )}
      {purged && (
        <div className="panel panel-neutral">
          <p>{C.purged}</p>
        </div>
      )}

      {loadFailed && (
        <div className="panel panel-notice space-y-3">
          <p>{C.itemsError}</p>
          <button type="button" className="btn-secondary" onClick={() => setTick((t) => t + 1)}>
            {C.tryAgain}
          </button>
        </div>
      )}
      {!items && !loadFailed && <p className="text-muted">{C.loadingItems}</p>}

      {items && d.length === 0 && (
        <div className="space-y-2">
          <p className="font-semibold">{C.empty}</p>
          <p className="text-muted">{C.emptyBody}</p>
        </div>
      )}

      {items && d.length > 0 && (
        <>
          <section aria-label={C.summaryRegion} className="space-y-2">
            <p>{C.summary(d.length, approved, skipped, undecided)}</p>
            {needsDate > 0 && (
              <p className="flex flex-wrap items-center gap-2">
                <span>{C.needsDate(needsDate)}</span>
                <button
                  type="button"
                  className="btn-quiet btn-sm"
                  onClick={() => {
                    setOpen((prev) => new Set([...(prev ?? []), "needs_date"]));
                    setFocusGroup("needs_date");
                  }}
                >
                  {C.needsDateShow}
                </button>
              </p>
            )}
            <p className="help">{C.timezone(syllabus.context.defaultTimezone)}</p>
            {d.length > LONG_LIST && <p className="help">{C.long}</p>}
          </section>

          <div className="flex flex-wrap items-end justify-between gap-3">
            <Segmented
              legend={C.groupByLegend}
              value={by}
              onChange={chooseBy}
              options={[
                { value: "week", label: C.groupByWeek },
                { value: "type", label: C.groupByType },
              ]}
            />
            <button type="button" className="btn-quiet btn-sm" onClick={() => setOpen(allOpen ? new Set() : new Set(groups.map((g) => g.key)))}>
              {allOpen ? C.collapseAll : C.expandAll}
            </button>
          </div>

          <div className="space-y-4">
            {groups.map((g) => {
              const isOpen = open?.has(g.key) ?? false;
              const label = groupLabel(g);
              const u = g.kind === "policy" ? 0 : g.items.filter((i) => i.decision === "undecided").length;
              const listId = `group-${g.key.replace(/[^a-z0-9-]/gi, "-")}`;
              return (
                <section key={g.key} aria-labelledby={`${listId}-h`} className="space-y-3">
                  <h2 id={`${listId}-h`} className="display text-xl">
                    <button
                      ref={(el) => {
                        groupButtons.current.set(g.key, el);
                      }}
                      type="button"
                      aria-expanded={isOpen}
                      aria-controls={listId}
                      onClick={() => toggle(g.key)}
                      className="flex min-h-[44px] w-full items-center gap-2 rounded-control text-left"
                    >
                      <Chevron open={isOpen} />
                      <span>
                        {label}
                        <span className="text-base font-normal text-muted">
                          {g.kind === "policy" ? `, ${g.items.length} ${g.items.length === 1 ? "item" : "items"}` : C.groupName("", g.items.length, u)}
                        </span>
                      </span>
                    </button>
                  </h2>
                  <div id={listId} hidden={!isOpen} className="space-y-3">
                    {g.kind === "policy" && <p className="help">{C.policyNote}</p>}
                    <ul className="space-y-3">
                      {g.items.map((i) => (
                        <ItemRow key={i.id} api={api} item={i} defaultTimezone={syllabus.context.defaultTimezone} purged={purged} onChange={onChange} />
                      ))}
                    </ul>
                  </div>
                </section>
              );
            })}
          </div>
        </>
      )}

      <Link href={`/courses/${encodeURIComponent(courseId)}`} className="btn-quiet px-0">
        {C.back}
      </Link>
    </div>
  );
}
