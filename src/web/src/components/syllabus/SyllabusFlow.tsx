"use client";

import { useState } from "react";
import type { SyllabusApi } from "@/lib/syllabus/client";
import { IN_PROGRESS, type Syllabus } from "@/lib/syllabus/types";
import { PAGE_COPY as P } from "./copy";
import { CreateForm } from "./CreateForm";
import { NotRead } from "./NotRead";
import { Processing } from "./Processing";
import { Review } from "./Review";

export interface SyllabusFlowProps {
  api: SyllabusApi;
  courseId: string;
  courseName: string;
  /** The course's syllabi, newest first or in any order; the newest is shown. */
  syllabi: Syllabus[];
  demoExpected: boolean;
  msPerSecond?: number;
}

type View = { k: "create" } | { k: "show"; s: Syllabus } | { k: "review"; s: Syllabus } | { k: "opening" } | { k: "openFailed" };

export function newest(list: Syllabus[]): Syllabus | null {
  return [...list].sort((a, b) => b.createdAt.localeCompare(a.createdAt) || b.id.localeCompare(a.id))[0] ?? null;
}

/**
 * Chooses the screen for /courses/{id}/syllabus (UX sections 3-5): create when there is no syllabus, processing while
 * the newest one is read, review when it is ready or partial, the not-read state otherwise. Each screen focuses its
 * own h1 when it is opened by the student. Several syllabi per course are allowed (contract 13 row 5); F1 shows the
 * newest.
 */
export function SyllabusFlow({ api, courseId, courseName, syllabi, demoExpected, msPerSecond }: SyllabusFlowProps) {
  const [view, setView] = useState<View>(() => {
    const s = newest(syllabi);
    return s ? { k: "show", s } : { k: "create" };
  });

  const toCreate = () => setView({ k: "create" });

  if (view.k === "opening") return <p className="text-muted">{P.loading}</p>;
  if (view.k === "openFailed")
    return (
      <div className="max-w-prose space-y-4">
        <h1 className="display text-3xl">{P.errorHeading}</h1>
        <p>{P.errorBody}</p>
        <button type="button" className="btn-secondary" onClick={toCreate}>
          {P.back}
        </button>
      </div>
    );
  if (view.k === "create")
    return (
      <CreateForm
        api={api}
        courseId={courseId}
        courseName={courseName}
        demoExpected={demoExpected}
        onCreated={(s) => setView({ k: "show", s })}
        onOpenExisting={(id) => {
          setView({ k: "opening" });
          api.getSyllabus(id).then(
            (s) => setView({ k: "show", s }),
            () => setView({ k: "openFailed" }),
          );
        }}
      />
    );
  if (view.k === "review") return <Review api={api} syllabus={view.s} courseId={courseId} />;
  const s = view.s;
  if (IN_PROGRESS.has(s.status))
    return <Processing api={api} initial={s} courseId={courseId} msPerSecond={msPerSecond} onReview={(next) => setView({ k: "review", s: next })} onPasteInstead={toCreate} />;
  if (s.status === "ready" || s.status === "partial") return <Review api={api} syllabus={s} courseId={courseId} />;
  return <NotRead syllabus={s} courseId={courseId} onPasteInstead={toCreate} />;
}
