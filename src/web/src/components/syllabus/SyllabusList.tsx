"use client";

import Link from "next/link";
import { useEffect, useRef } from "react";
import { IN_PROGRESS, type Syllabus } from "@/lib/syllabus/types";
import { LIST_COPY as C, PAGE_COPY } from "./copy";
import { Chevron } from "./icons";

/** "Oct 30, 3:05 PM" in the student's own zone (an instant, so local time is the honest reading). */
export function addedWhen(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  return new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", hour: "numeric", minute: "2-digit" }).format(d);
}

function summary(s: Syllabus): string {
  const source = s.inputKind === "paste" ? C.source.paste : C.source[s.fileType ?? "txt"];
  const state = IN_PROGRESS.has(s.status) ? C.reading : s.status === "failed" || s.status === "needs_text" ? C.notRead : C.found(s.counts.total);
  return `${source} · ${state}`;
}

/**
 * The course's syllabi (several are allowed: contract decision 8, at most 5 per course). Shown when there is more than
 * one; newest first. Plain list with one Open button each and one "Add another syllabus".
 */
export function SyllabusList({
  courseName,
  courseId,
  syllabi,
  onOpen,
  onAdd,
}: {
  courseName: string;
  courseId: string;
  syllabi: Syllabus[];
  onOpen(s: Syllabus): void;
  onAdd(): void;
}) {
  const ref = useRef<HTMLHeadingElement>(null);
  useEffect(() => ref.current?.focus(), []);
  const sorted = [...syllabi].sort((a, b) => b.createdAt.localeCompare(a.createdAt) || b.id.localeCompare(a.id));
  return (
    <div className="max-w-prose space-y-6">
      <div className="space-y-2">
        <h1 ref={ref} tabIndex={-1} className="display text-3xl">
          {C.h1}
        </h1>
        <p className="text-muted">{C.intro(courseName)}</p>
      </div>
      {/* A ledger (redesign-round2 2.7): each row's one action, "Open", is stretched over the whole row; one tab stop, one name. */}
      <ul className="ledger">
        {sorted.map((s) => {
          const when = addedWhen(s.createdAt);
          return (
            <li key={s.id} className="ledger-row">
              <div className="min-w-0">
                <p className="font-semibold text-ink">{C.added(when)}</p>
                <p className="text-sm text-muted">{summary(s)}</p>
              </div>
              <span className="inline-flex items-center gap-1">
                <button type="button" className="ledger-row-link min-h-[44px]" aria-label={C.openName(when)} onClick={() => onOpen(s)}>
                  {C.open}
                </button>
                <Chevron open={false} />
              </span>
            </li>
          );
        })}
      </ul>
      <div className="flex flex-wrap gap-3">
        <button type="button" className="btn-primary" onClick={onAdd}>
          {C.addAnother}
        </button>
        <Link href={`/courses/${encodeURIComponent(courseId)}`} className="btn-tertiary">
          {PAGE_COPY.back}
        </Link>
      </div>
    </div>
  );
}
