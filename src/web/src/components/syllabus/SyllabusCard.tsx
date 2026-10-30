"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { syllabusApi, type SyllabusApi } from "@/lib/syllabus/client";
import { IN_PROGRESS, type Syllabus } from "@/lib/syllabus/types";
import { CARD_COPY as C } from "./copy";
import { newest } from "./SyllabusFlow";

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
 * "Syllabus" card on the course page, below "Lecture" (UX section 2). Static text, no badge, count or live region;
 * a plain link to /courses/{id}/syllabus. Counts come from the syllabus resource.
 */
export function SyllabusCard({ courseId, api = syllabusApi }: { courseId: string; api?: SyllabusApi }) {
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
    <section aria-labelledby="syllabus-heading" className="card">
      <h2 id="syllabus-heading" className="display text-2xl">
        {C.title}
      </h2>
      <p className="mt-2 max-w-prose text-muted">{state.s === "loading" ? C.loading : state.s === "error" ? C.loadError : content?.text}</p>
      {content && (
        <Link href={href} className="btn-secondary mt-5">
          {content.button}
        </Link>
      )}
    </section>
  );
}
