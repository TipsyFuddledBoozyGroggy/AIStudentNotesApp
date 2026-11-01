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
    // DRAFT wording (frontend-engineer, 2026-11-01), not yet reviewed by neurodivergent-ux-specialist. Shown only when the API
    // names defaultTimezone as the problem, so the student is not told their text was at fault.
    zone: (tz: string) => `Tether cannot use your time zone, ${tz}, yet. Your text is kept. Nothing was changed.`,
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
  /** SBR-06: counts.droppedInvalid > 0. No count is shown (docs/ux/dropped-dates-notice.md section 2). */
  dropped: "Parts of this syllabus could not be turned into items.",
  droppedBody: "Your syllabus is the complete list. Check it for dates that are not shown here.",
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
  /** PO-20b: the one action the dropped-dates Note may carry (dropped-dates-notice.md 6), shown only when the list renders. */
  droppedSee: "See dates we noticed",
  /** Empty items but counts.unclaimedDates > 0 (unclaimed-dates-list.md 4.8). */
  emptyNoticed: "No items were made from this syllabus, but some dates were noticed.",
  emptyNoticedBody: "They are listed below. You can add any of them as an item.",
} as const;

/**
 * PO-20b (docs/ux/unclaimed-dates-list.md section 5): the "Dates we noticed" section on the Review list. DRAFT wording,
 * not yet reviewed by neurodivergent-ux-specialist; mock only; G4 NOT_RUN.
 *
 * `form.notReady` and `form.limit` are kept verbatim from the spec but are NOT shown in v1: the server answers both "not
 * ready" and the 100-student-item cap with the same 409 `invalid_state` (ItemService.CreateAsync), so the client cannot
 * tell them apart and shows ACTION_COPY.generic instead.
 */
export const UNCLAIMED_COPY = {
  label: "Dates we noticed",
  lines: (n: number) => (n === 1 ? "1 line" : `${n} lines`),
  allHandled: "all hidden or added",
  help: "These lines from your syllabus have a date, but Tether did not turn them into items.",
  help2: "Each line is shown word for word. You can add one as an item, or hide it.",
  help3: "Hiding a line keeps it out of this list on this device. You can undo it at any time.",
  add: "Add as item",
  addName: (t: string) => `Add as item: ${t}`,
  hide: "Hide",
  hideName: (t: string) => `Hide: ${t}`,
  hidden: "Hidden:",
  undo: "Undo",
  undoName: (t: string) => `Undo hiding ${t}`,
  hiddenMsg: (t: string) => `Hidden: ${t}.`,
  shownMsg: (t: string) => `Shown again: ${t}.`,
  added: "Added as an item:",
  goToItem: "Go to the item",
  goToItemName: (t: string) => `Go to the item ${t}`,
  addedMsg: (t: string) => `Added as an item: ${t}. It is not decided yet.`,
  showMore: (n: number) => `Show ${n} more`,
  showHandled: (n: number) => `Show hidden and added dates (${n})`,
  loading: "Getting the dates we noticed.",
  loadError: "The dates we noticed could not be shown. Nothing was changed.",
  tryAgain: "Try again",
  form: {
    legend: (t: string) => `Add an item from: ${t}`,
    titleHelp: "This starts as the line from your syllabus. You can change it.",
    dateHelp: "Read the date from the line above and enter it here.",
    save: "Add item",
    busy: "Adding...",
    cancel: "Cancel",
    notReady: "Items can't be added while this syllabus is being read. Nothing was changed.",
    limit: "This syllabus holds as many added items as it can. Nothing was changed.",
  },
} as const;

export const ITEM_COPY = {
  conf: { looks_right: "Looks right", probably_right: "Probably right", please_check: "Please check" },
  status: { undecided: "Not decided", approved: "Approved", skipped: "Skipped" },
  allDay: "All day",
  past: "Date has passed",
  edited: "You edited this",
  /** `days` from weekdayList ("Tuesday and Thursday"), `range` from dateRange ("Aug 25 to Dec 11"): the weekday is named once. */
  repeats: (days: string, range: string) => `Repeats every ${days}, ${range}.`,
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

// ---------------------------------------------------------------------------------------------------------------------
// Increment P2-F2 (mock only). Same rules: language guide, copy lint, NOT human-validated (G4 NOT_RUN).

const items = (n: number) => `${n} ${n === 1 ? "item" : "items"}`;

/** File intake (UX 3 and 8). The backend reads plain text only in this increment; PDF and Word end "disabled". */
export const FILE_COPY = {
  methodLegend: "How to add your syllabus",
  methodPaste: "Paste text",
  methodFile: "Upload a file",
  label: "Syllabus file",
  hint: "Plain text files (.txt) up to 10 MB. PDF and Word files can't be read yet.",
  selected: (name: string, size: string) => `Selected: ${name} (${size})`,
  remove: "Remove",
  removeName: "Remove the chosen file",
  hintNeedFile: "Choose a file first.",
  hintOtherFile: "Choose another file first.",
  lastDayLabel: "Last day of classes (optional)",
  termHint: "Add both dates, or leave both empty.",
  errors: {
    type: "We can't use that kind of file. Use a plain text file, or paste the text.",
    notYet: "Tether can't read PDF or Word files yet. Paste the text instead, or use a plain text file.",
    size: "That file is too big. The limit is 10 MB. Try a smaller file or paste the text.",
    empty: "This file is empty. Choose another file or paste the text.",
    network: "We could not reach the server. Your syllabus wasn't sent. Try again when you're back online.",
    termPair: "Add both the first and last day of classes, or leave both empty.",
    termOrder: "The last day of classes is before the first day. Check both dates.",
  },
} as const;

/** A PDF or Word upload ends `failed` with `disabled`: no parser exists in this increment. */
export const NOT_READ_FILE_NOT_YET = {
  heading: "Tether can't read this kind of file yet.",
  body: "PDF and Word files are not read in this version. Nothing was saved from this file. You can paste the text instead.",
} as const;

export const STOP_COPY = {
  button: "Stop reading",
  heading: "Stop reading this syllabus?",
  body: "Nothing is saved from it.",
  confirm: "Stop reading",
  keep: "Keep going",
  busy: "Stopping...",
  done: "Stopped. Nothing was saved from this syllabus.",
} as const;

/** UX 5.4. Batch approve is optional, never the default, and only for clear items. */
export const BATCH_COPY = {
  week: (n: number) => `Approve the clear ones in this week (${n})`,
  group: (n: number) => `Approve the clear ones in this group (${n})`,
  /** Accessible name: the visible label first (WCAG 2.5.3), then the group, so each group's button is unique. */
  groupName: (visible: string, group: string) => `${visible}: ${group}`,
  menu: "More ways to decide",
  all: (n: number) => `Approve all clear items (${n})`,
  what: "Clear items have a date and look right. Items that need a check are never included.",
  explain: (n: number) => `This will approve ${items(n)} that have a date and look right. You can undo it.`,
  readEach: "Read each date against its source before you approve.",
  sourceLabel: "From your syllabus:",
  go: (n: number) => `Approve ${items(n)}`,
  busy: "Approving...",
  cancel: "Cancel",
  done: (n: number) => `Approved ${items(n)}.`,
  partial: (k: number, n: number) => `Approved ${k} of ${n}. The others are unchanged and can be decided one by one.`,
  undo: "Undo this",
  undoBusy: "Undoing...",
  undone: (n: number) => `Undone: ${items(n)}.`,
} as const;

/** UX 5.5. Position is text ("Item 3 of 24"); no progress bar, no "left" language. */
export const ONE_COPY = {
  open: "Review one at a time",
  h1: "Review one at a time",
  position: (i: number, n: number) => `Item ${i} of ${n}`,
  next: "Next, decide later",
  back: "Back",
  exit: "Back to the list",
  endHeading: "That's all of them.",
  endCounts: (a: number, s: number, u: number) => `${a} approved, ${s} skipped, ${u} not decided.`,
  reviewLeft: "Review the ones left",
  toCalendar: "Go to calendar step",
  empty: "There is nothing to decide here.",
} as const;

/**
 * Mock calendar sync (UX 6, contract 6.3). `confirmDemo` and `resultDemo` are verbatim from the UX spec (sync.confirm.demo,
 * sync.result.demo; gate G1, not reworded). The tag word "Demo calendar" sits beside every calendar Demo line.
 */
export const SYNC_COPY = {
  tagWord: "Demo calendar",
  confirmDemo: "Demo only. Nothing is sent to Google Calendar. Your Google account is not connected.",
  resultDemo: "This is a demo. Nothing was sent to Google Calendar.",
  bar: (n: number) => `Add ${n} approved ${n === 1 ? "item" : "items"} to the demo calendar`,
  barHint: "Approve an item to continue.",
  barRegion: "Calendar step",
  /** Slice 8c (make-notes-rail-demo-notice 3.2): the counts inside the Calendar step, in the summary line's own words. */
  railCounts: (approved: number, skipped: number, undecided: number) => `${approved} approved, ${skipped} skipped, ${undecided} not decided.`,
  skipTo: "Skip to calendar step",
  confirmH1: "Add approved items to the demo calendar",
  lead: (n: number) => `You approved ${items(n)}:`,
  state: {
    new: "New",
    already: "Already in the demo calendar",
    changed: "Changed since it was added",
    retry: "Did not go through last time",
  },
  updateLabel: "Update it in the demo calendar",
  notUpdated: (n: number) => (n === 1 ? "1 changed item stays as it was in the demo calendar." : `${n} changed items stay as they were in the demo calendar.`),
  notAdded: (u: number, s: number, d: number) => `Not added: ${u} not decided, ${s} skipped, ${d} need a date.`,
  noneChosen: "Check an item above to send it.",
  go: (n: number) => `Add ${items(n)} to the demo calendar`,
  busy: "Adding...",
  back: "Back to review",
  resultDone: (n: number) => `Done. ${n} ${n === 1 ? "item is" : "items are"} in the demo calendar.`,
  resultAlready: "These items are already in the demo calendar.",
  resultPartial: (k: number, n: number) => `Added ${k} of ${n}. ${n - k} didn't go through.`,
  resultNone: "None of these items went into the demo calendar.",
  later: "You can change or remove any of these later.",
  outcome: {
    created: "Added",
    updated: "Updated",
    unchanged: "Already in the demo calendar",
    failed: "Didn't go through",
    not_eligible: "Not added",
  },
  reason: {
    item_not_approved: "It is no longer approved.",
    item_needs_date: "It needs a date first.",
    item_not_syncable: "Course policies are never added to a calendar.",
    precondition_failed: "It changed after you reviewed it. Check it, then try again.",
    not_found: "It is no longer in this syllabus.",
    provider_unavailable: "The demo calendar was not available.",
    event_limit_reached: "The demo calendar holds no more entries for this syllabus.",
    other: "That did not work. Nothing was changed for this item.",
  },
  retry: "Try again",
  retryName: (t: string) => `Try again: ${t}`,
  retryBusy: "Trying...",
  backToCourse: "Back to course",
  // Item row (review list)
  rowIn: "In the demo calendar",
  rowChanged: "Changed since it was added",
  rowChangedNote: "The demo calendar keeps the earlier version until you add it again.",
  rowNotThrough: "This did not go through to the demo calendar. You can try again from the calendar step.",
  withdrawNote: "Undo or Skip also removes it from the demo calendar.",
  remove: "Remove from the demo calendar",
  removeName: (t: string) => `Remove from the demo calendar: ${t}`,
  removedMsg: (t: string) => `Removed from the demo calendar: ${t}.`,
  removedNote: "Removed from the demo calendar. It is still approved.",
  undoRemoveName: (t: string) => `Undo removal of ${t}`,
  addedBackMsg: (t: string) => `Added back to the demo calendar: ${t}.`,
} as const;

export const DELETE_COPY = {
  button: "Delete this syllabus",
  heading: "Delete this syllabus?",
  body: "This deletes the syllabus, the items Tether found and your decisions. Its items in the demo calendar are removed too.",
  copies: "Copies you downloaded are not deleted.",
  confirm: "Delete syllabus",
  keep: "Keep it",
  busy: "Deleting...",
  done: "The syllabus was deleted.",
} as const;

export const EXPORT_COPY = {
  heading: "Your data",
  button: "Download a copy (JSON)",
  hint: "A file with the items, the source wording and the demo calendar entries. The original file is not kept, so it is not in the copy.",
  busy: "Preparing...",
  done: "Your copy is ready.",
  fileName: "syllabus-export.json",
} as const;

/** Messages for a press that did not go through (UX 8). Never a status code, never the server's text. */
export const ACTION_COPY = {
  network: "We could not reach the server. Nothing was changed. Try again.",
  rateLimited: "One moment. Try again in about a minute. Nothing was changed.",
  generic: "That did not work. Nothing was changed.",
  gone: "This syllabus is no longer here. It may have been deleted in another tab.",
  sessionHeading: "Please sign in again.",
  sessionBody: "What you decided is saved.",
  signIn: "Sign in",
} as const;

/** Several syllabi per course are allowed (contract decision 8, row 5). */
export const LIST_COPY = {
  h1: "Syllabi for this course",
  intro: (course: string) => `${course}. A course can hold up to 5 syllabi, for example an updated version.`,
  added: (when: string) => `Added ${when}`,
  source: { paste: "Pasted text", txt: "Text file", pdf: "PDF file", docx: "Word file" },
  reading: "Being read",
  notRead: "Could not be read",
  found: (n: number) => `${items(n)} found`,
  open: "Open",
  openName: (when: string) => `Open the syllabus added ${when}`,
  addAnother: "Add another syllabus",
  all: "All syllabi for this course",
} as const;
