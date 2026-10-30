"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";
import { api, ApiError, type Course } from "@/lib/api";
import { syllabusApi } from "@/lib/syllabus/client";
import type { Syllabus } from "@/lib/syllabus/types";
import { useRequireUser } from "../AuthProvider";
import { AnnouncerProvider } from "../lecture/Announcer";
import { PAGE_COPY as P } from "./copy";
import { SyllabusFlow } from "./SyllabusFlow";

type State = { s: "loading" } | { s: "notfound" } | { s: "error" } | { s: "ready"; course: Course; syllabi: Syllabus[] };

/** Route /courses/{id}/syllabus. Mounts the shared announcer once (status + alert regions; no other live region). */
export function SyllabusPage() {
  const params = useParams<{ id: string }>();
  const id = params?.id;
  const { status } = useRequireUser();
  const [state, setState] = useState<State>({ s: "loading" });
  const [tick, setTick] = useState(0);

  useEffect(() => {
    if (status !== "authenticated" || !id) return;
    let cancelled = false;
    Promise.all([api.getCourse(id), syllabusApi.listSyllabi(id)])
      .then(([course, syllabi]) => !cancelled && setState({ s: "ready", course, syllabi }))
      .catch((e: unknown) => {
        if (!cancelled) setState(e instanceof ApiError && e.isNotFound ? { s: "notfound" } : { s: "error" });
      });
    return () => {
      cancelled = true;
    };
  }, [status, id, tick]);

  if (state.s === "loading") return <p className="text-muted">{P.loading}</p>;
  if (state.s !== "ready" || !id)
    return (
      <div className="max-w-prose space-y-4">
        <h1 className="display text-3xl">{state.s === "notfound" ? P.courseNotFound : P.errorHeading}</h1>
        <p className="text-muted">{state.s === "notfound" ? P.notFoundBody : P.errorBody}</p>
        <div className="flex flex-wrap gap-3">
          {state.s === "error" && (
            <button type="button" className="btn-primary" onClick={() => setTick((t) => t + 1)}>
              {P.tryAgain}
            </button>
          )}
          <Link href={id ? `/courses/${encodeURIComponent(id)}` : "/"} className="btn-secondary">
            {P.back}
          </Link>
        </div>
      </div>
    );
  return (
    <AnnouncerProvider>
      <SyllabusFlow
        api={syllabusApi}
        courseId={id}
        courseName={state.course.name}
        syllabi={state.syllabi}
        demoExpected={process.env.NEXT_PUBLIC_EXPECT_DEMO_SOURCE !== "0"}
      />
    </AnnouncerProvider>
  );
}
