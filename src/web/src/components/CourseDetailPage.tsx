"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";
import { api, ApiError, type Course } from "@/lib/api";
import { useRequireUser } from "./AuthProvider";
import { CourseDetailView } from "./CourseDetailView";

type State = { s: "loading" } | { s: "notfound" } | { s: "error" } | { s: "ready"; course: Course };

export function CourseDetailPage() {
  const params = useParams<{ id: string }>();
  const id = params?.id;
  const { status } = useRequireUser();
  const [state, setState] = useState<State>({ s: "loading" });
  const [justAdded, setJustAdded] = useState(false);

  useEffect(() => {
    setJustAdded(new URLSearchParams(window.location.search).get("added") === "1");
  }, []);

  useEffect(() => {
    if (status !== "authenticated" || !id) return;
    let cancelled = false;
    api
      .getCourse(id)
      .then((course) => !cancelled && setState({ s: "ready", course }))
      .catch((e: unknown) => {
        if (!cancelled) setState(e instanceof ApiError && e.isNotFound ? { s: "notfound" } : { s: "error" });
      });
    return () => {
      cancelled = true;
    };
  }, [status, id]);

  if (state.s === "loading")
    return (
      <p role="status" className="text-muted">
        Getting this course.
      </p>
    );
  if (state.s === "notfound" || state.s === "error") {
    return (
      <div className="max-w-prose space-y-4">
        <h1 className="display text-3xl">{state.s === "notfound" ? "We can't find that course" : "We couldn't load this course"}</h1>
        <p className="text-muted">{state.s === "notfound" ? "It may have been removed, or it belongs to another account." : "Nothing was deleted. Try again in a moment."}</p>
        <Link href="/" className="btn-primary">
          Back to dashboard
        </Link>
      </div>
    );
  }
  return <CourseDetailView course={state.course} justAdded={justAdded} />;
}
