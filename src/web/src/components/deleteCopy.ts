// Delete one lecture and delete a course (PO-20by: "Approve as DRAFT wording; leave the consent text alone").
// DRAFT wording from docs/ux/delete-wording-draft.md sections 5.1, 5.2a and 6.3; deleteCopy.test.ts pins every string
// that the draft states literally. Strings marked "derived" are not in the draft word for word (a singular form, a course
// variant of a lecture line, a fallback); they are DRAFT until neurodivergent-ux-specialist reviews them.
// Deliberately absent: the vendor lines (vendorMock needs an aiSource fact the session does not carry; vendorReal holds an
// UNVERIFIED placeholder), the Google line (export is not built) and any backup or retention promise (L-07, L-08).
// The G1 consent bullets in lecture/copy.ts are not touched here.

const plural = (n: number, one: string, many: string) => (n === 1 ? one : many);

export const LECTURE_DELETE_COPY = {
  button: "Delete this lecture",
  heading: "Delete this lecture?",
  body: "This deletes the transcript, the summary, any recaps and the notes.",
  cannot: "It can't be undone.",
  kept: "Tether keeps a record that a lecture happened: when it started, paused and stopped, and how much AI it used. None of the words stay.",
  confirm: "Delete lecture",
  keep: "Keep it",
  busy: "Deleting...",
  done: "The lecture was deleted.",
  doneNote: (title: string) => `Deleted: ${title}.`,
  gone: "This lecture is no longer here. It may have been deleted in another tab.",
} as const;

export const COURSE_DELETE_COPY = {
  /** Section label on the course page (reused from the draft's 5.4 "Your data" label and the syllabus EXPORT_COPY heading). */
  section: "Your data",
  button: "Delete this course",
  blocked: "A lecture is recording. Stop it first.",
  heading: "Delete this course?",
  body: "This deletes the course and everything in it:",
  /** Derived: the course has no lectures and no syllabi, so there is no list to introduce. */
  bodyEmpty: "This deletes the course.",
  /** Draft: "{n} lectures: transcripts, summaries, recaps and notes"; the singular form is derived. Omitted when n = 0 (derived). */
  lectures: (n: number) => (n === 1 ? "1 lecture: transcript, summary, recaps and notes" : `${n} lectures: transcripts, summaries, recaps and notes`),
  /** Draft: "{m} syllabi: the text, the items and your decisions"; the singular form is derived. Omitted when m = 0. */
  syllabi: (m: number) => `${m} ${plural(m, "syllabus", "syllabi")}: the text, the items and your decisions`,
  demoCalendar: "its items in the demo calendar",
  cannot: "It can't be undone.",
  kept: "Tether keeps a record that these lectures and syllabi existed, when, and how much AI they used. None of the words stay.",
  confirm: "Delete course",
  keep: "Keep it",
  busy: "Deleting...",
  done: "The course was deleted.",
  doneNote: (name: string) => `Deleted: ${name}.`,
  /** Derived from the lecture's 404 line (6.3). */
  gone: "This course is no longer here. It may have been deleted in another tab.",
} as const;

/** Failure lines (draft 6.3). One line each, no status codes; each says whether anything changed. */
export const DELETE_PROBLEM_COPY = {
  network: "We could not reach the server. Nothing was deleted. Try again when you like.",
  conflict: "Something changed while deleting. Nothing was deleted. Try again.",
  rate: "One moment. Try again in about a minute. Nothing was deleted.",
  session: "Please sign in again. Nothing was deleted.",
  /** Derived from the syllabus ACTION_COPY.generic ("That did not work. Nothing was changed."), for any other refusal. */
  generic: "That did not work. Nothing was deleted.",
  /** The draft's 6.2 "Dismiss button" on the after-delete note. */
  dismiss: "Dismiss",
} as const;
