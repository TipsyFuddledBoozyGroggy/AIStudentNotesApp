// Save to a Demo Doc (finish-backlog item 3, PO-20cb / PO-20cc: mock only, real Google OFF). DRAFT wording from
// docs/product/docs-export-tasks.md section 4, word for word unless marked "derived"; every string is DRAFT until
// neurodivergent-ux-specialist reviews it. Not consent, recording or Demo-source text (G1): the mock notes line reuses the
// existing Demo notes wording (DemoNotesTag) and is not repeated here. copy.test.ts pins the section 4 strings and lints all.
//
// Two deliberate departures, both flagged for review:
//  - retry.body: section 4 says "Nothing was saved and nothing was lost.", which the shared copy lint forbids ("nothing was
//    lost" is listed as deficit framing, language guide 2/3.9). Derived: the "lost" clause is replaced by "Your notes are
//    fine.", the phrase section 4 itself uses in the last-attempt and permanent lines.
//  - The three lines that say what a Demo Doc is NOT (entry.help, preview.where, page.banner) name Google only to say nothing
//    goes there. They are kept word for word; whether to keep the negation is a question for the Lead and UX.

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

export const DOCS_COPY = {
  entry: {
    button: "Save to a Demo Doc",
    help: "A Demo Doc is a practice copy kept inside Tether. Nothing is sent to Google.",
    saved: (date: string) => `Saved to a Demo Doc on ${date}.`,
    /** Derived: the state says a Demo Doc exists but not when (its newest attempt is not that save). */
    savedUndated: "Saved to a Demo Doc.",
    open: "Open Demo Doc",
  },
  preview: {
    heading: "Check before saving",
    line1: "This is exactly what will be saved. Nothing has been saved yet.",
    where: "Where: a Demo Doc (a practice copy inside Tether, not Google Docs)",
    title: (title: string) => `Title: ${title}`,
    /** Section 4 has the plural form; the singular forms ("1 section", "1 bullet") are derived. */
    count: (sections: number, bullets: number) => `${plural(sections, "section", "sections")}, ${plural(bullets, "bullet", "bullets")}. Each bullet keeps its lecture time.`,
    /** Derived: the accessible name of the read-only region that shows the blocks. */
    regionLabel: "What will be saved",
    save: "Save to Demo Doc",
    notNow: "Not now",
    loading: "Getting your preview...",
    error: "We could not show the preview. Nothing was saved.",
    tryAgain: "Try again",
    close: "Close",
  },
  changed: {
    line: (date: string) => `You saved these notes to a Demo Doc on ${date}. The notes or title have changed since.`,
    legend: "What would you like to do?",
    update: "Update that Demo Doc. Its contents will be replaced with what you see here.",
    another: "Make a second Demo Doc. The first one stays as it is.",
    hint: "Choose one to continue.",
  },
  matches: "Your Demo Doc already matches these notes. There is nothing to save.",
  tooLarge: "These notes are too long for one Demo Doc. Nothing was cut and nothing was saved. You can still use Export notes.",
  nothing: "There is nothing in these notes to save yet.",
  saving: {
    button: "Saving...",
    announce: "Saving to a Demo Doc.",
  },
  success: {
    created: "Saved to a Demo Doc.",
    updated: "Your Demo Doc was updated.",
    unchanged: "Nothing to update. It already matched.",
    where: "This copy stays inside Tether. It is deleted when you delete this lecture.",
  },
  stale: "The notes changed while you were looking. Here is the new version. Nothing was saved yet.",
  retry: {
    /** Derived (see the header): section 4's "nothing was lost" clause fails the copy lint. */
    body: "That did not save. Nothing was saved and your notes are fine. You can try again.",
    wait: (n: number) => `Please wait about ${n} seconds, then try again.`,
    tryAgain: "Try again",
  },
  lastAttempt: {
    body: "We stopped trying after a few attempts. Your notes are fine. You can start again from the preview, or use Export notes.",
    startAgain: "Start again",
  },
  permanent: "This Demo Doc could not be saved. Your notes are fine. You can still use Export notes or Copy notes.",
  inProgress: {
    body: "A save is already running. Give it a moment, then check again.",
    checkAgain: "Check again",
  },
  notAvailable: "This lecture is not available.",
  page: {
    banner: "Demo Doc: a practice copy kept inside Tether. It is not in Google Docs.",
    back: "Back to the lecture",
    /** Existing wording (LectureReopenPage), used when the page was opened without its lecture. */
    backHome: "Back to dashboard",
    delete: "Delete this Demo Doc",
    deleteHeading: "Delete this Demo Doc?",
    deleteBody: "Your notes and lecture are not affected.",
    /** Derived: the confirm button of the delete dialog (the lecture's is "Delete lecture"). */
    deleteConfirm: "Delete Demo Doc",
    keep: "Keep it",
    deleting: "Deleting...",
    /** Derived from LECTURE_DELETE_COPY.done. */
    deleted: "The Demo Doc was deleted.",
    /** Derived from LectureReopenPage ("Getting this lecture."). */
    loading: "Getting this Demo Doc.",
    /** Derived from LectureReopenPage ("We can't find that lecture"). */
    notFound: "We can't find that Demo Doc",
    /** Derived from LectureReopenPage ("We couldn't load this lecture"). */
    loadError: "We couldn't load this Demo Doc",
    /** Derived: what the version line says (plain fact, no judgement). */
    version: (n: number, date: string) => `Version ${n}, saved ${date}.`,
  },
} as const;
