"use client";

import Link from "next/link";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { ApiError, type Course, type CreateCourseInput } from "@/lib/api";
import { isDuplicateName, validateCourseName } from "@/lib/courseValidation";

export const DRAFT_KEY = "cc.courseDraft.v1";

interface Draft {
  name: string;
  code: string;
  term: string;
  instructor: string;
}
const EMPTY: Draft = { name: "", code: "", term: "", instructor: "" };

interface Props {
  onSubmit: (input: CreateCourseInput) => Promise<Course>;
  onCreated: (course: Course) => void;
  existingNames?: string[];
  /** Debounce for draft autosave, in ms. */
  draftDelayMs?: number;
}

function readDraft(): Draft | null {
  try {
    const raw = window.localStorage.getItem(DRAFT_KEY);
    return raw ? { ...EMPTY, ...(JSON.parse(raw) as Partial<Draft>) } : null;
  } catch {
    return null;
  }
}

export function CourseForm({ onSubmit, onCreated, existingNames = [], draftDelayMs = 400 }: Props) {
  const [draft, setDraft] = useState<Draft>(EMPTY);
  const [restored, setRestored] = useState(false);
  const [showOptions, setShowOptions] = useState(false);
  const [nameError, setNameError] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const loaded = useRef(false);
  const summaryRef = useRef<HTMLDivElement>(null);
  const nameRef = useRef<HTMLInputElement>(null);
  const [failCount, setFailCount] = useState(0);

  // Restore a saved draft after mount (avoids SSR/CSR mismatch).
  useEffect(() => {
    const saved = readDraft();
    if (saved && (saved.name || saved.code || saved.term || saved.instructor)) {
      setDraft(saved);
      setRestored(true);
      if (saved.code || saved.term || saved.instructor) setShowOptions(true);
    }
    loaded.current = true;
  }, []);

  // Debounced autosave. Never saves before the restore ran.
  useEffect(() => {
    if (!loaded.current) return;
    const t = window.setTimeout(() => {
      try {
        const empty = !draft.name && !draft.code && !draft.term && !draft.instructor;
        if (empty) window.localStorage.removeItem(DRAFT_KEY);
        else window.localStorage.setItem(DRAFT_KEY, JSON.stringify(draft));
      } catch {
        /* storage unavailable: form still works */
      }
    }, draftDelayMs);
    return () => window.clearTimeout(t);
  }, [draft, draftDelayMs]);

  useEffect(() => {
    if (failCount > 0) summaryRef.current?.focus();
  }, [failCount]);

  const update = (k: keyof Draft) => (e: { target: { value: string } }) => {
    setDraft((d) => ({ ...d, [k]: e.target.value }));
    setSaveError(null);
  };

  const duplicate = isDuplicateName(draft.name, existingNames);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const err = validateCourseName(draft.name);
    setNameError(err);
    if (err) {
      setFailCount((n) => n + 1); // focus the error summary, also on repeated failures
      return;
    }
    setSaving(true);
    setSaveError(null);
    const opt = (v: string) => (v.trim() ? v.trim() : undefined);
    try {
      const course = await onSubmit({
        name: draft.name.trim(),
        code: opt(draft.code),
        term: opt(draft.term),
        instructor: opt(draft.instructor),
      });
      try {
        window.localStorage.removeItem(DRAFT_KEY);
      } catch {
        /* ignore */
      }
      onCreated(course);
    } catch (e2) {
      // Input is kept (M1-S2 AC5). Say what happened and what to do next.
      setSaveError(
        e2 instanceof ApiError && e2.isNetwork
          ? "We couldn't reach the server, so your course isn't saved yet. Your answers are kept. Try again when you're back online."
          : "We couldn't save this course. Your answers are kept. Please try again.",
      );
      setSaving(false);
    }
  }

  return (
    <form onSubmit={(e) => void handleSubmit(e)} noValidate aria-labelledby="form-heading" className="space-y-5">
      <h1 id="form-heading" className="text-2xl font-bold">
        Add a course
      </h1>

      {failCount > 0 && nameError && (
        <div ref={summaryRef} tabIndex={-1} role="alert" className="rounded-lg border-2 border-red-800 bg-red-50 p-4">
          <h2 className="font-bold text-red-900">One thing to fix</h2>
          <ul className="mt-1 list-disc pl-5">
            <li>
              <a
                href="#course-name"
                className="text-red-900 underline"
                onClick={(ev) => {
                  ev.preventDefault();
                  nameRef.current?.focus();
                }}
              >
                {nameError}
              </a>
            </li>
          </ul>
        </div>
      )}

      {restored && <p role="status">Welcome back. We restored your draft.</p>}

      <div>
        <label htmlFor="course-name" className="font-semibold">
          Course name <span aria-hidden="true">(required)</span>
        </label>
        <input
          id="course-name"
          ref={nameRef}
          type="text"
          className="field"
          value={draft.name}
          onChange={update("name")}
          onBlur={() => setNameError(draft.name ? validateCourseName(draft.name) : nameError)}
          required
          aria-required="true"
          aria-invalid={nameError ? true : undefined}
          aria-describedby={[nameError ? "course-name-error" : "", duplicate ? "course-name-dup" : ""].filter(Boolean).join(" ") || undefined}
          autoComplete="off"
        />
        {nameError && (
          <p id="course-name-error" className="mt-1 font-medium text-red-900">
            {nameError}
          </p>
        )}
        {duplicate && (
          <p id="course-name-dup" className="mt-1 text-slate-800">
            You already have a course with this name. You can still save it.
          </p>
        )}
      </div>

      <div>
        <button
          type="button"
          className="font-medium text-blue-900 underline"
          aria-expanded={showOptions}
          aria-controls="course-options"
          onClick={() => setShowOptions((s) => !s)}
        >
          {showOptions ? "Hide options" : "Show options"}
        </button>
        <p className="text-sm text-slate-700">Optional. You can add these later.</p>
        <div id="course-options" hidden={!showOptions} className="mt-3 space-y-4">
          <div>
            <label htmlFor="course-code" className="font-semibold">Course code</label>
            <input id="course-code" className="field" value={draft.code} onChange={update("code")} autoComplete="off" />
          </div>
          <div>
            <label htmlFor="course-term" className="font-semibold">Term</label>
            <input id="course-term" className="field" value={draft.term} onChange={update("term")} autoComplete="off" />
          </div>
          <div>
            <label htmlFor="course-instructor" className="font-semibold">Instructor</label>
            <input id="course-instructor" className="field" value={draft.instructor} onChange={update("instructor")} autoComplete="off" />
          </div>
        </div>
      </div>

      {saveError && (
        <div role="alert" className="rounded-lg border-2 border-amber-800 bg-amber-50 p-4">
          {saveError}
        </div>
      )}

      <div className="flex flex-wrap gap-3">
        <button type="submit" className="btn-primary" disabled={saving} aria-disabled={saving}>
          {saving ? "Saving..." : saveError ? "Try again" : "Save course"}
        </button>
        <Link href="/" className="btn-secondary">
          Cancel
        </Link>
      </div>
    </form>
  );
}
