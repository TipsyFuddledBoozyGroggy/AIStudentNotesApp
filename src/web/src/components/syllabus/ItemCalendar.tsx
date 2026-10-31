"use client";

import { useRef, useState } from "react";
import type { SyllabusApi } from "@/lib/syllabus/client";
import { inDemoCalendar } from "@/lib/syllabus/eligibility";
import { newIdempotencyKey } from "@/lib/syllabus/requests";
import type { SyllabusItem } from "@/lib/syllabus/types";
import { useAnnouncer } from "../lecture/Announcer";
import { ActionProblem, problemKind, type ProblemKind } from "./ActionProblem";
import { ITEM_COPY, SYNC_COPY as C } from "./copy";

/**
 * Demo calendar state of one approved item, plus "Remove from the demo calendar" and its own Undo (contract 6.3:
 * calendar-sync/remove keeps the item approved; re-adding is an explicit one-item sync with a new Idempotency-Key).
 * Undo stays until the student does something else with this row. The visible state is always re-read from the server.
 */
export function ItemCalendar({ api, item, onChange }: { api: SyllabusApi; item: SyllabusItem; onChange(i: SyllabusItem): void }) {
  const announcer = useAnnouncer();
  const [removed, setRemoved] = useState(false);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<ProblemKind | null>(null);
  const keyRef = useRef<{ revision: number; key: string } | null>(null);
  const btnRef = useRef<HTMLButtonElement>(null);

  if (item.decision !== "approved") return null;
  const present = inDemoCalendar(item);
  if (!present && !removed && item.syncState !== "sync_failed") return null;

  async function refresh(): Promise<SyllabusItem | null> {
    try {
      const next = await api.getItem(item.syllabusId, item.id);
      onChange(next);
      return next;
    } catch {
      return null;
    }
  }

  async function remove() {
    if (busy) return;
    setBusy(true);
    setProblem(null);
    try {
      const r = await api.removeFromCalendar(item.syllabusId, [item.id]);
      const one = r.results.find((x) => x.itemId === item.id);
      if (!one || one.outcome === "failed") throw new Error("not removed");
      await refresh();
      setRemoved(true);
      announcer.status(C.removedMsg(item.title));
    } catch (e) {
      setProblem(problemKind(e));
    } finally {
      setBusy(false);
      queueMicrotask(() => btnRef.current?.focus()); // the Undo takes the same position
    }
  }

  async function addBack() {
    if (busy) return;
    setBusy(true);
    setProblem(null);
    try {
      if (keyRef.current?.revision !== item.revision) keyRef.current = { revision: item.revision, key: newIdempotencyKey() };
      const { result } = await api.syncCalendar(item.syllabusId, new Map([[item.id, item.revision]]), keyRef.current.key);
      const one = result.results.find((x) => x.itemId === item.id);
      keyRef.current = null;
      if (!one || (one.outcome !== "created" && one.outcome !== "updated" && one.outcome !== "unchanged")) {
        await refresh();
        throw new Error("not added");
      }
      await refresh();
      setRemoved(false);
      announcer.status(C.addedBackMsg(item.title));
    } catch (e) {
      setProblem(problemKind(e));
    } finally {
      setBusy(false);
      queueMicrotask(() => btnRef.current?.focus());
    }
  }

  const changed = item.syncState === "changed_since_sync";
  const notThrough = item.syncState === "sync_failed";
  return (
    <div className="space-y-2 border-t border-hair pt-2 text-sm">
      {removed && !present ? (
        <p>{C.removedNote}</p>
      ) : (
        <>
          <p className="font-semibold">{changed ? C.rowChanged : notThrough && !present ? C.rowNotThrough : C.rowIn}</p>
          {changed && <p className="text-muted">{C.rowChangedNote}</p>}
          {present && <p className="text-muted">{C.withdrawNote}</p>}
        </>
      )}
      {removed && !present ? (
        <button ref={btnRef} type="button" className="btn-tertiary btn-sm optical-start" aria-label={C.undoRemoveName(item.title)} aria-busy={busy || undefined} onClick={() => void addBack()}>
          {ITEM_COPY.undo}
        </button>
      ) : present ? (
        <button ref={btnRef} type="button" className="btn-tertiary btn-sm optical-start" aria-label={C.removeName(item.title)} aria-busy={busy || undefined} onClick={() => void remove()}>
          {C.remove}
        </button>
      ) : null}
      {problem && <ActionProblem kind={problem} />}
    </div>
  );
}
