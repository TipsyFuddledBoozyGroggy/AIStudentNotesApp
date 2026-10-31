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
  /** Set once the course is saved: a debounced autosave still pending must not re-create the cleared draft. */
  const saved = useRef(false);
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
      if (saved.current) return;
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
      saved.current = true;
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
    // Sheet template (themes-spec R2): 40rem, left-aligned to the column start. Slice 4 moves the h1 above the sheet.
    <form onSubmit={(e) => void handleSubmit(e)} noValidate aria-labelledby="form-heading" className="card max-w-sheet space-y-6">
      <div>
        <h1 id="form-heading" className="display text-3xl">
          Add a course
        </h1>
        <p className="mt-1 text-muted">Only the name is needed. Your answers are saved on this device as you type.</p>
      </div>

      {failCount > 0 && nameError && (
        <div ref={summaryRef} tabIndex={-1} role="alert" className="panel panel-danger">
          <h2 className="font-bold text-danger">One thing to fix</h2>
          <ul className="mt-1 list-disc pl-5">
            <li>
              <a
                href="#course-name"
                className="text-danger underline"
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

      {restored && (
        <p role="status" className="panel panel-neutral text-sm">
          Welcome back. We restored your draft.
        </p>
      )}

      <div>
        <label htmlFor="course-name" className="label">
          Course name <span aria-hidden="true" className="font-normal text-muted">(required)</span>
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
          <p id="course-name-error" className="mt-2 font-semibold text-danger">
            {nameError}
          </p>
        )}
        {duplicate && (
          <p id="course-name-dup" className="mt-2 text-sm text-muted">
            You already have a course with this name. You can still save it.
          </p>
        )}
      </div>

      <div>
        <button
          type="button"
          className="btn-quiet optical-start"
          aria-expanded={showOptions}
          aria-controls="course-options"
          onClick={() => setShowOptions((s) => !s)}
        >
          {showOptions ? "Hide options" : "Show options"}
        </button>
        <p className="help">Code, term and instructor. Optional; you can add these later.</p>
        <div id="course-options" hidden={!showOptions} className="mt-4 space-y-4 border-l-2 border-hair pl-4">
          <div>
            <label htmlFor="course-code" className="label">Course code</label>
            <input id="course-code" className="field" value={draft.code} onChange={update("code")} autoComplete="off" />
          </div>
          <div>
            <label htmlFor="course-term" className="label">Term</label>
            <input id="course-term" className="field" value={draft.term} onChange={update("term")} autoComplete="off" />
          </div>
          <div>
            <label htmlFor="course-instructor" className="label">Instructor</label>
            <input id="course-instructor" className="field" value={draft.instructor} onChange={update("instructor")} autoComplete="off" />
          </div>
        </div>
      </div>

      {saveError && (
        <div role="alert" className="panel panel-notice">
          {saveError}
        </div>
      )}

      <div className="flex flex-wrap gap-3">
        <button type="submit" className="btn-primary" disabled={saving} aria-disabled={saving}>
          {saving ? "Saving..." : saveError ? "Try again" : "Save course"}
        </button>
        <Link href="/" className="btn-quiet">
          Cancel
        </Link>
      </div>
    </form>
  );
}
