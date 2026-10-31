"use client";

import Link from "next/link";
import { useEffect, useId, useRef, useState } from "react";
import { ApiError } from "@/lib/api/errors";
import { PlainText } from "@/lib/lecture/PlainTextSpan";
import type { SyllabusApi } from "@/lib/syllabus/client";
import { inDemoCalendar, syncCandidates } from "@/lib/syllabus/eligibility";
import { KIND_LABEL, weekRangeLabel, whenLine } from "@/lib/syllabus/format";
import { groupItems } from "@/lib/syllabus/grouping";
import { newIdempotencyKey } from "@/lib/syllabus/requests";
import type { ItemErrorCode, Syllabus, SyllabusItem, SyncResult } from "@/lib/syllabus/types";
import { ActionProblem, problemKind, type ProblemKind } from "./ActionProblem";
import { ITEM_COPY, REVIEW_COPY, SYNC_COPY as C } from "./copy";
import { DemoLine } from "./DemoLine";

export interface SentRun {
  result: SyncResult;
  replayed: boolean;
  /** Titles as the student saw them on the confirm step (the result carries ids only). */
  titles: Record<string, string>;
}

const stateNote = (i: SyllabusItem) =>
  i.syncState === "changed_since_sync" ? C.state.changed : i.syncState === "sync_failed" ? C.state.retry : inDemoCalendar(i) ? C.state.already : C.state.new;

/**
 * Confirm step (UX 6.1, contract 6.3). Lists exactly what will be sent: the approved, dated items, each with its state
 * ("New", "Already in the demo calendar", ...). A changed item is sent only when the student ticks "Update it in the
 * demo calendar" (nothing updates silently; unticked by default). The count in the button equals the list that is sent.
 * No reminder option exists in v1. One Idempotency-Key per press, reused only to retry the same body after a network
 * failure, so a double press or retry writes each item once.
 */
export function SyncConfirm({ api, syllabus, items, onBack, onDone }: { api: SyllabusApi; syllabus: Syllabus; items: SyllabusItem[]; onBack(): void; onDone(run: SentRun): void }) {
  const uid = useId();
  const h1Ref = useRef<HTMLHeadingElement>(null);
  const problemRef = useRef<HTMLDivElement>(null);
  const [update, setUpdate] = useState<Set<string>>(() => new Set());
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const [problem, setProblem] = useState<ProblemKind | null>(null);
  const keyRef = useRef<{ fingerprint: string; key: string } | null>(null);

  useEffect(() => h1Ref.current?.focus(), []);
  useEffect(() => {
    if (problem) problemRef.current?.focus();
  }, [problem]);

  const candidates = syncCandidates(items);
  const sent = candidates.filter((i) => i.syncState !== "changed_since_sync" || update.has(i.id));
  const heldBack = candidates.length - sent.length;
  const decidable = items.filter((i) => i.kind !== "policy");
  const undecided = decidable.filter((i) => i.decision === "undecided" && i.when.status === "resolved").length;
  const skipped = decidable.filter((i) => i.decision === "skipped").length;
  const needsDate = decidable.filter((i) => i.when.status === "needs_date" && i.decision !== "skipped").length;
  const groups = groupItems(candidates, "week");

  async function send() {
    if (busyRef.current || sent.length === 0) return;
    const revisions = new Map(sent.map((i) => [i.id, i.revision]));
    const fingerprint = JSON.stringify([...revisions]);
    if (keyRef.current?.fingerprint !== fingerprint) keyRef.current = { fingerprint, key: newIdempotencyKey() };
    busyRef.current = true;
    setBusy(true);
    setProblem(null);
    try {
      const { result, replayed } = await api.syncCalendar(syllabus.id, revisions, keyRef.current.key);
      keyRef.current = null;
      onDone({ result, replayed, titles: Object.fromEntries(sent.map((i) => [i.id, i.title])) });
    } catch (e) {
      if (!(e instanceof ApiError && e.isNetwork)) keyRef.current = null; // the server answered: a retry is a new request
      setProblem(problemKind(e));
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  }

  return (
    <div className="max-w-prose space-y-6">
      <div className="space-y-3">
        <h1 ref={h1Ref} tabIndex={-1} className="display text-3xl">
          {C.confirmH1}
        </h1>
        <DemoLine text={C.confirmDemo} tag={C.tagWord} />
      </div>
      <p>{C.lead(candidates.length)}</p>
      <div className="space-y-4">
        {groups.map((g) => (
          <section key={g.key} aria-labelledby={`${uid}-${g.key}`} className="space-y-2">
            <h2 id={`${uid}-${g.key}`} className="font-semibold text-ink">
              {REVIEW_COPY.weekGroup(weekRangeLabel(g.value))}
            </h2>
            <ul className="ledger">
              {g.items.map((i) => (
                <li key={i.id} className="ledger-row block text-sm">
                  <p className="break-words font-semibold text-ink">
                    <PlainText value={i.title} />
                  </p>
                  <p>
                    {KIND_LABEL[i.kind]} · {whenLine(i, ITEM_COPY.allDay)}
                  </p>
                  <p className="text-muted">{stateNote(i)}</p>
                  {i.syncState === "changed_since_sync" && (
                    <label className="mt-1 flex min-h-[44px] cursor-pointer items-center gap-2">
                      <input
                        type="checkbox"
                        className="h-5 w-5"
                        checked={update.has(i.id)}
                        onChange={(e) =>
                          setUpdate((prev) => {
                            const s = new Set(prev);
                            if (e.target.checked) s.add(i.id);
                            else s.delete(i.id);
                            return s;
                          })
                        }
                      />
                      <span>{C.updateLabel}</span>
                    </label>
                  )}
                </li>
              ))}
            </ul>
          </section>
        ))}
      </div>
      <p className="text-muted">{C.notAdded(undecided, skipped, needsDate)}</p>
      {heldBack > 0 && <p className="text-muted">{C.notUpdated(heldBack)}</p>}
      {problem && <ActionProblem ref={problemRef} kind={problem} />}
      <div className="flex flex-wrap items-center gap-3">
        <button
          type="button"
          className="btn-primary"
          aria-disabled={busy || sent.length === 0 ? true : undefined}
          aria-describedby={sent.length === 0 ? `${uid}-none` : undefined}
          onClick={() => void send()}
        >
          {busy ? C.busy : C.go(sent.length)}
        </button>
        <button type="button" className="btn-secondary" onClick={onBack}>
          {C.back}
        </button>
      </div>
      {sent.length === 0 && (
        <p id={`${uid}-none`} className="help">
          {C.noneChosen}
        </p>
      )}
    </div>
  );
}

const OK = new Set(["created", "updated", "unchanged"]);
const reasonText = (code: ItemErrorCode | null) => (code && code in C.reason ? C.reason[code as keyof typeof C.reason] : C.reason.other);

/**
 * Result (UX 6.2). Focus lands on the h1 and the status region is not also used. Never full success when anything did
 * not go through: a partial run says "Added N of M" and lists the others with a reason and "Try again" each. A re-run
 * shows "Already in the demo calendar" for unchanged items. A replayed body (Idempotency-Replayed) is history; the
 * review list is re-read from the server when the student goes back.
 */
export function SyncResultView({
  api,
  syllabusId,
  courseId,
  run,
  items,
  onBack,
  onItemChange,
}: {
  api: SyllabusApi;
  syllabusId: string;
  courseId: string;
  run: SentRun;
  items: SyllabusItem[];
  onBack(): void;
  onItemChange(i: SyllabusItem): void;
}) {
  const h1Ref = useRef<HTMLHeadingElement>(null);
  const [rows, setRows] = useState(run.result.results);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [problem, setProblem] = useState<{ id: string; kind: ProblemKind } | null>(null);

  useEffect(() => h1Ref.current?.focus(), []);

  const n = rows.length;
  const ok = rows.filter((r) => OK.has(r.outcome)).length;
  const changed = rows.filter((r) => r.outcome === "created" || r.outcome === "updated").length;
  const heading = ok === n ? (changed === 0 ? C.resultAlready : C.resultDone(n)) : ok === 0 ? C.resultNone : C.resultPartial(ok, n);
  const title = (id: string) => items.find((i) => i.id === id)?.title ?? run.titles[id] ?? "";

  async function retry(id: string) {
    if (busyId) return;
    setBusyId(id);
    setProblem(null);
    try {
      const latest = await api.getItem(syllabusId, id); // the revision the student sees now
      onItemChange(latest);
      const { result } = await api.syncCalendar(syllabusId, new Map([[id, latest.revision]]), newIdempotencyKey());
      const one = result.results.find((r) => r.itemId === id);
      if (one) setRows((prev) => prev.map((r) => (r.itemId === id ? one : r)));
      try {
        onItemChange(await api.getItem(syllabusId, id));
      } catch {
        /* the row result above is still true */
      }
    } catch (e) {
      setProblem({ id, kind: problemKind(e) });
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div className="max-w-prose space-y-6">
      <div className="space-y-3">
        <h1 ref={h1Ref} tabIndex={-1} className="display text-3xl">
          {heading}
        </h1>
        <DemoLine text={C.resultDemo} tag={C.tagWord} />
        <p className="text-muted">{C.later}</p>
      </div>
      <ul className="ledger">
        {rows.map((r) => (
          <li key={r.itemId} className="ledger-row block text-sm" data-outcome={r.outcome}>
            <p className="break-words font-semibold text-ink">
              <PlainText value={title(r.itemId)} />
            </p>
            <p>{C.outcome[r.outcome]}</p>
            {!OK.has(r.outcome) && <p className="text-muted">{reasonText(r.errorCode)}</p>}
            {r.outcome === "failed" && (
              <button type="button" className="btn-secondary btn-sm mt-2" aria-label={C.retryName(title(r.itemId))} aria-busy={busyId === r.itemId || undefined} onClick={() => void retry(r.itemId)}>
                {busyId === r.itemId ? C.retryBusy : C.retry}
              </button>
            )}
            {problem?.id === r.itemId && <ActionProblem kind={problem.kind} className="mt-2" />}
          </li>
        ))}
      </ul>
      <div className="flex flex-wrap gap-3">
        <button type="button" className="btn-primary" onClick={onBack}>
          {C.back}
        </button>
        <Link href={`/courses/${encodeURIComponent(courseId)}`} className="btn-secondary">
          {C.backToCourse}
        </Link>
      </div>
    </div>
  );
}
