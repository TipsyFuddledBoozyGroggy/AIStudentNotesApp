"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { syllabusApi, type SyllabusApi } from "@/lib/syllabus/client";
import { IN_PROGRESS, type Syllabus } from "@/lib/syllabus/types";
import { CARD_COPY as C } from "./copy";
import { newest } from "./SyllabusFlow";
import { Chevron } from "../ui/Disclosure";

type State = { s: "loading" } | { s: "error" } | { s: "ready"; syllabus: Syllabus | null };

/** Sentence and button for the card (UX section 2 state table): one sentence, at most one button, same position. */
export function cardContent(s: Syllabus | null): { text: string; button: string } {
  if (!s) return { text: C.none, button: C.noneButton };
  if (IN_PROGRESS.has(s.status)) return { text: C.reading, button: C.readingButton };
  if (s.status === "failed" || s.status === "needs_text") return { text: C.notRead, button: C.notReadButton };
  const c = s.counts;
  if (c.synced > 0) return { text: C.synced(c.synced), button: C.syncedButton };
  const decided = c.approved + c.skipped;
  if (c.total > 0 && decided === 0) return { text: C.ready(c.total), button: C.readyButton };
  if (c.total > 0 && decided < c.total) return { text: C.partly(decided, c.total), button: C.partlyButton };
  if (c.total > 0) return { text: C.allDecided, button: C.allDecidedButton };
  return { text: C.ready(0), button: C.readyButton };
}

/**
 * "Syllabus" on the course page, below "Lecture" (UX section 2). Static text, no badge, count or live region; a plain
 * link to /courses/{id}/syllabus. Counts come from the syllabus resource. Since redesign-round2 slice 4 it is a Desk
 * section (the label hangs in the margin) holding one ledger row: the sentence, then the action as the row's one link,
 * stretched over the whole row. Same sentence, same link name, same position.
 */
export function SyllabusCard({ courseId, api = syllabusApi, className = "" }: { courseId: string; api?: SyllabusApi; className?: string }) {
  const [state, setState] = useState<State>({ s: "loading" });

  useEffect(() => {
    let cancelled = false;
    api
      .listSyllabi(courseId)
      .then((list) => !cancelled && setState({ s: "ready", syllabus: newest(list) }))
      .catch(() => !cancelled && setState({ s: "error" }));
    return () => {
      cancelled = true;
    };
  }, [api, courseId]);

  const content = state.s === "ready" ? cardContent(state.syllabus) : null;
  const href = `/courses/${encodeURIComponent(courseId)}/syllabus`;

  return (
    <section aria-labelledby="syllabus-heading" className={`desk-section ${className}`}>
      <h2 id="syllabus-heading" className="label desk-label">
        {C.title}
      </h2>
      <div className="desk-body ledger">
        <div className="ledger-row">
          <p className="min-w-0 max-w-prose text-muted">{state.s === "loading" ? C.loading : state.s === "error" ? C.loadError : content?.text}</p>
          {content && (
            <span className="inline-flex items-center gap-1">
              <Link href={href} className="ledger-row-link">
                {content.button}
              </Link>
              <Chevron open={false} />
            </span>
          )}
        </div>
      </div>
    </section>
  );
}
