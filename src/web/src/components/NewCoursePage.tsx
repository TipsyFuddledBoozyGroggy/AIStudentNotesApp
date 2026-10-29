"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useRequireUser } from "./AuthProvider";
import { CourseForm } from "./CourseForm";

export function NewCoursePage() {
  const router = useRouter();
  const { status } = useRequireUser();
  const [names, setNames] = useState<string[]>([]);

  useEffect(() => {
    if (status !== "authenticated") return;
    api
      .listCourses()
      .then((r) => setNames(r.items.map((c) => c.name)))
      .catch(() => undefined); // duplicate warning is best-effort
  }, [status]);

  if (status !== "authenticated")
    return (
      <p role="status" className="text-muted">
        Getting ready.
      </p>
    );
  return (
    <CourseForm
      existingNames={names}
      onSubmit={(input) => api.createCourse(input)}
      onCreated={(c) => router.push(`/courses/${encodeURIComponent(c.id)}?added=1`)}
    />
  );
}
