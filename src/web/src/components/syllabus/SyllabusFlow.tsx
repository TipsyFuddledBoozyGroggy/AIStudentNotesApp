"use client";

import { useState } from "react";
import type { SyllabusApi } from "@/lib/syllabus/client";
import { IN_PROGRESS, type Syllabus } from "@/lib/syllabus/types";
import { PAGE_COPY as P } from "./copy";
import { CreateForm, type Method } from "./CreateForm";
import { NotRead } from "./NotRead";
import { Processing } from "./Processing";
import { Review } from "./Review";
import { SyllabusList } from "./SyllabusList";

export interface SyllabusFlowProps {
  api: SyllabusApi;
  courseId: string;
  courseName: string;
  /** The course's syllabi, in any order. */
  syllabi: Syllabus[];
  demoExpected: boolean;
  msPerSecond?: number;
}

type View =
  | { k: "create"; method: Method; back: View | null }
  | { k: "list" }
  | { k: "show"; s: Syllabus }
  | { k: "review"; s: Syllabus }
  | { k: "opening" }
  | { k: "openFailed" };

export function newest(list: Syllabus[]): Syllabus | null {
  return [...list].sort((a, b) => b.createdAt.localeCompare(a.createdAt) || b.id.localeCompare(a.id))[0] ?? null;
}

const home = (list: Syllabus[]): View => (list.length === 0 ? { k: "create", method: "paste", back: null } : list.length === 1 ? { k: "show", s: list[0] } : { k: "list" });

/**
 * Chooses the screen for /courses/{id}/syllabus (UX sections 3-5): create when there is no syllabus, the list when
 * there are several (contract decision 8), processing while one is read, review when it is ready or partial, the
 * not-read state otherwise. Each screen focuses its own h1 when it is opened by the student.
 */
export function SyllabusFlow({ api, courseId, courseName, syllabi: initial, demoExpected, msPerSecond }: SyllabusFlowProps) {
  const [syllabi, setSyllabi] = useState(initial);
  const [view, setView] = useState<View>(() => home(initial));

  const upsert = (s: Syllabus) => setSyllabi((prev) => [s, ...prev.filter((x) => x.id !== s.id)]);
  const removed = (id: string) => {
    const rest = syllabi.filter((x) => x.id !== id);
    setSyllabi(rest);
    setView(rest.length === 0 ? { k: "create", method: "paste", back: null } : { k: "list" });
  };
  const toCreate = (method: Method = "paste") => setView({ k: "create", method, back: syllabi.length > 0 ? (syllabi.length > 1 ? { k: "list" } : view) : null });
  const open = (id: string) => {
    setView({ k: "opening" });
    api.getSyllabus(id).then(
      (s) => {
        upsert(s);
        setView({ k: "show", s });
      },
      () => setView({ k: "openFailed" }),
    );
  };
  const others = syllabi.length > 1 ? () => setView({ k: "list" }) : undefined;

  if (view.k === "opening") return <p className="text-muted">{P.loading}</p>;
  if (view.k === "openFailed")
    return (
      <div className="max-w-prose space-y-4">
        <h1 className="display text-3xl">{P.errorHeading}</h1>
        <p>{P.errorBody}</p>
        <button type="button" className="btn-secondary" onClick={() => setView(home(syllabi))}>
          {P.back}
        </button>
      </div>
    );
  if (view.k === "list") return <SyllabusList courseName={courseName} courseId={courseId} syllabi={syllabi} onOpen={(s) => open(s.id)} onAdd={() => toCreate()} />;
  if (view.k === "create") {
    const back = view.back;
    return (
      <CreateForm
        key={view.method}
        api={api}
        courseId={courseId}
        courseName={courseName}
        demoExpected={demoExpected}
        initialMethod={view.method}
        onCancel={back ? () => setView(back) : undefined}
        onCreated={(s) => {
          upsert(s);
          setView({ k: "show", s });
        }}
        onOpenExisting={open}
      />
    );
  }
  const reviewProps = { api, courseId, onDeleted: removed, onAddAnother: () => toCreate(), onShowAll: others };
  if (view.k === "review") return <Review key={view.s.id} syllabus={view.s} {...reviewProps} />;
  const s = view.s;
  if (IN_PROGRESS.has(s.status))
    return (
      <Processing
        key={s.id}
        api={api}
        initial={s}
        courseId={courseId}
        msPerSecond={msPerSecond}
        onReview={(next) => {
          upsert(next);
          setView({ k: "review", s: next });
        }}
        onPasteInstead={() => toCreate("paste")}
        onStopped={() => removed(s.id)}
      />
    );
  if (s.status === "ready" || s.status === "partial") return <Review key={s.id} syllabus={s} {...reviewProps} />;
  return <NotRead syllabus={s} courseId={courseId} onPasteInstead={() => toCreate("paste")} />;
}
