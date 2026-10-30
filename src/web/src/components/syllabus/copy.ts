// Every student-facing string on the syllabus screens (increment P2-F1, mock only). Written to docs/ux/language-guide.md
// (binding); the draft strings in docs/ux/phase2-syllabus-review.md section 14 predate it and were adapted where they
// conflict. copy.test.ts lints this file. NOT human-validated (gate G4 NOT_RUN).
//
// GATED, NOT EDITED FREELY: the two Demo tags below are taken verbatim from the UX spec (Demo-source wording sits under
// gate G1), and NOTICE_DRAFT is a PLACEHOLDER pending Product Owner and counsel decision P2-D5. Neither is legal text.

import type { ExtractionStatus, ItemKind, StatusReason, UnknownReason } from "@/lib/syllabus/types";

/** DRAFT, placeholder only (P2-D5). Must not imply Tether checked that the student may add the document. */
export const NOTICE_DRAFT = {
  tag: "Draft",
  heading: "Notice (draft placeholder)",
  body: "DRAFT placeholder. The notice for adding a syllabus is not written yet. The Product Owner and counsel must review it before real use.",
  checkbox: "I read the notice above. (Draft placeholder wording.)",
} as const;

/** Verbatim from phase2-syllabus-review.md section 14 (upload.demoTag, review.demoTag). Gate G1: do not reword. */
export const DEMO_TAGS = {
  source: "Demo mode: items are found by a test routine, not a real AI model.",
  calendar: "Demo mode: nothing here goes to Google Calendar.",
} as const;
/** The visible tag word beside the Demo lines (same pattern as the lecture "Demo" tags). */
export const DEMO_WORD = "Demo";

export const CARD_COPY = {
  title: "Syllabus",
  loading: "Getting your syllabus.",
  loadError: "Your syllabus could not be shown. Nothing was changed.",
  none: "Pull exam and assignment dates out of your syllabus. You check each one before anything goes on a calendar.",
  noneButton: "Add your syllabus",
  reading: "Tether is reading your syllabus. You can leave. It will be here when you come back.",
  readingButton: "See progress",
  ready: (n: number) => `We found ${n} ${n === 1 ? "item" : "items"}. Open them when you want to.`,
  readyButton: "Review items",
  partly: (d: number, n: number) => `${d} of ${n} decided. You can pick up where you left off.`,
  partlyButton: "Continue review",
  allDecided: "All items are decided.",
  allDecidedButton: "View items",
  synced: (n: number) => `${n} ${n === 1 ? "item is" : "items are"} in the demo calendar. Nothing was sent to Google.`,
  syncedButton: "View items",
  notRead: "We couldn't read this syllabus. You can try again.",
  notReadButton: "Try again",
} as const;

export const CREATE_COPY = {
  h1: "Add your syllabus",
  intro: (course: string) => `${course}. Tether finds dates for exams, assignments and readings. You check each one before it goes anywhere.`,
  pasteLabel: "Syllabus text",
  pasteHint: "Paste as much as you have. Up to about 50,000 characters.",
  pasteKept: "We kept what you pasted.",
  optionsToggle: "Add options",
  firstDayLabel: "First day of classes (optional)",
  firstDayHint: "Helps turn dates like 'Week 5' into a calendar date. You can skip it.",
  timezone: (tz: string) => `Times will use your time zone: ${tz}.`,
  submit: "Read my syllabus",
  submitBusy: "Reading...",
  hintNeedText: "Paste your syllabus text first.",
  hintNeedNotice: "Check the box above to continue.",
  cancel: "Back to course",
  errors: {
    textEmpty: "Paste your syllabus text first.",
    textLong: "That's a lot of text. Tether reads up to about 50,000 characters. Paste the part with dates.",
    textNul: "This text has a hidden character that cannot be used. Copy it again from the source.",
    firstDayFormat: "Enter a date like 2026-08-24.",
    firstDayReal: "That date isn't real. Check the month and day.",
    duplicate: "This syllabus is already in this course. Nothing new was added.",
    openExisting: "Open it",
    inProgress: "Another syllabus is being read. Try again when it is ready. Nothing was changed.",
    limit: "This course has reached its syllabus limit. Nothing was changed.",
    rateLimited: "One moment. Try again in about a minute. Nothing was changed.",
    network: "We could not reach the server. Your text is kept. Try again.",
    invalid: "One thing to check: the text or a date could not be used. Your text is kept.",
    generic: "That did not work. Nothing was changed. Your text is kept.",
  },
} as const;

export const PROCESS_COPY = {
  h1: "Reading your syllabus",
  body: "Tether is looking for dates and deadlines.",
  leave: "You can leave this page. Tether keeps reading and it will be waiting for you.",
  step: (n: number, name: string) => `Step ${n} of 3: ${name}`,
  steps: ["got your text", "finding dates", "getting items ready"],
  done: "Done",
  slow: "This is taking a little longer. Nothing was changed.",
  stopped: "This is taking longer than usual. You can leave and come back, or check again.",
  checkAgain: "Check again",
  offline: "We could not reach the server. Tether checks again when you press Check again.",
  readyStatus: "Your items are ready.",
  review: "Review items",
  back: "Back to course",
} as const;

/** Statuses that are finished but have no items (UX section 8), keyed by statusReason. */
export const NOT_READ_COPY: Record<StatusReason | "default", { heading: string; body: string }> = {
  no_text_layer: { heading: "We couldn't read the words in this file.", body: "It may be a scan or a photo. You can paste the text instead." },
  password_protected: { heading: "This file is locked.", body: "Remove the password and add it again, or paste the text." },
  too_long: { heading: "That's a lot of text.", body: "Tether reads up to about 50,000 characters. Paste the part with dates." },
  budget_limited: { heading: "Reading new syllabi is paused for now.", body: "Your other items are kept. Nothing was changed." },
  disabled: { heading: "Reading new syllabi is paused for now.", body: "Your other items are kept. Nothing was changed." },
  unreadable_file: { heading: "Something went wrong while reading.", body: "Nothing was saved from this syllabus. You can try again." },
  provider_unavailable: { heading: "Something went wrong while reading.", body: "Nothing was saved from this syllabus. You can try again." },
  timeout: { heading: "Something went wrong while reading.", body: "Nothing was saved from this syllabus. You can try again." },
  invalid_output: { heading: "Something went wrong while reading.", body: "Nothing was saved from this syllabus. You can try again." },
  default: { heading: "Something went wrong while reading.", body: "Nothing was saved from this syllabus. You can try again." },
};

export const NOT_READ_ACTIONS = { paste: "Paste text instead", tryAgain: "Try again", back: "Back to course" } as const;

export const PAGE_COPY = {
  loading: "Getting your syllabus.",
  notFoundHeading: "We can't find that syllabus",
  notFoundBody: "It may have been removed, or it belongs to another account.",
  errorHeading: "We couldn't show your syllabus",
  errorBody: "Nothing was changed. Try again in a moment.",
  tryAgain: "Try again",
  back: "Back to course",
  courseNotFound: "We can't find that course",
} as const;

export const KIND_GROUP_LABEL: Record<Exclude<ItemKind, "policy">, string> = {
  exam: "Exams",
  assignment: "Assignments",
  reading: "Readings",
  office_hours: "Office hours",
  other: "Other",
};

export const REVIEW_COPY = {
  h1: "Review your syllabus items",
  summary: (n: number, a: number, s: number, u: number) => `We found ${n} ${n === 1 ? "item" : "items"}. ${a} approved, ${s} skipped, ${u} not decided.`,
  needsDate: (n: number) => (n === 1 ? "1 needs a date." : `${n} need a date.`),
  needsDateShow: "Show them",
  timezone: (tz: string) => `Times are in ${tz}.`,
  groupByLegend: "Group by",
  groupByWeek: "Week",
  groupByType: "Type",
  expandAll: "Expand all",
  collapseAll: "Collapse all",
  weekGroup: (range: string) => `Week of ${range}`,
  policyGroup: "Course policies",
  policyNote: "These have no date. They are here for reference and are never added to a calendar.",
  needsDateGroup: "Needs a date",
  groupName: (label: string, n: number, u: number) => `${label}, ${n} ${n === 1 ? "item" : "items"}, ${u} not decided`,
  long: "This is a long list. Taking it one week at a time can help.",
  empty: "No dates or deadlines were found in this syllabus.",
  emptyBody: "Some syllabi list dates in a table or an image, which can be hard to read. You can try another version.",
  partial: "Tether read only part of this syllabus. The items below are not the whole list.",
  partialPages: (pages: string) => `Not read: ${pages}.`,
  unreadablePages: (pages: string) => `Pages that could not be read: ${pages}.`,
  hostile: "This file has hidden text, so please check each date. Each item needs its own decision.",
  suspect: "Parts of this file look like instructions rather than dates. Please check each date. Each item needs its own decision.",
  purged: "The original wording was removed on schedule. Titles, dates and your decisions are kept.",
  back: "Back to course",
  loadingItems: "Getting your items.",
  itemsError: "Your items could not be shown. Nothing was changed.",
  tryAgain: "Try again",
  summaryRegion: "Review summary",
} as const;

export const ITEM_COPY = {
  conf: { looks_right: "Looks right", probably_right: "Probably right", please_check: "Please check" },
  status: { undecided: "Not decided", approved: "Approved", skipped: "Skipped" },
  allDay: "All day",
  past: "Date has passed",
  edited: "You edited this",
  repeats: (days: string, from: string, until: string) => `Repeats weekly on ${days}, ${from} to ${until}.`,
  sourceToggle: "See where this came from",
  sourceFrom: (page: number | null) => (page ? `From your syllabus, page ${page}:` : "From your syllabus:"),
  sourceAround: "The line around it:",
  purgedSnippet: "The original wording was removed on schedule.",
  checkHint: "Approving this means you checked the date against your syllabus.",
  approve: "Approve",
  skip: "Skip",
  edit: "Edit",
  undo: "Undo",
  addDate: "Add date",
  approveName: (t: string) => `Approve ${t}`,
  skipName: (t: string) => `Skip ${t}`,
  editName: (t: string) => `Edit ${t}`,
  addDateName: (t: string) => `Add date for ${t}`,
  undoApproveName: (t: string) => `Undo approval of ${t}`,
  undoSkipName: (t: string) => `Undo skip of ${t}`,
  approvedMsg: (t: string) => `Approved: ${t}.`,
  skippedMsg: (t: string) => `Skipped: ${t}.`,
  undoneMsg: (t: string) => `Undone: ${t}.`,
  savedMsg: (t: string) => `Saved: ${t}.`,
  saveFailed: "Couldn't save this one. Nothing was changed. Try again.",
  retry: "Try again",
  changedElsewhere: "This item changed in another tab or window. This is the latest version. You can try again.",
  needsDate: {
    not_stated: "No full date is written for this. You can add one or leave it.",
    tba: "The syllabus says the date is to be announced.",
    relative_unresolvable: "The date is written as a week of the term. You can add the date.",
    conflicting: "The syllabus gives more than one date.",
    ambiguous_format: "The date could be read two ways.",
    table_unreadable: "The date is in a table that was hard to read.",
    date_not_verifiable: "The date could not be matched to the syllabus text.",
  } satisfies Record<UnknownReason, string>,
  alternatives: (dates: string) => `Dates written: ${dates}.`,
} as const;

export const EDIT_COPY = {
  legend: (t: string) => `Edit ${t}`,
  title: "Title",
  type: "Type",
  date: "Date",
  dateHint: "Example: 2026-10-14",
  time: "Time (optional)",
  allDay: "All day",
  save: "Save",
  cancel: "Cancel",
  kept: "We kept your changes.",
  approvedNote: "Saving a change sets this back to Not decided, so you can check it again.",
  errors: {
    titleEmpty: "Enter a title.",
    titleLong: "Use 200 characters or fewer.",
    dateEmpty: "Enter a date like 2026-10-14.",
    dateFormat: "Enter a date like 2026-10-14.",
    dateReal: "That date isn't real. Check the month and day.",
    time: "Enter a time like 14:30, or choose All day.",
    server: "One thing to check: a value could not be used. Your changes are kept.",
  },
  kinds: { exam: "Exam", assignment: "Assignment", reading: "Reading", office_hours: "Office hours", other: "Other" },
} as const;

/** Card copy key for each extraction status (in progress vs finished-without-items). */
export const isNotRead = (s: ExtractionStatus) => s === "failed" || s === "needs_text";
