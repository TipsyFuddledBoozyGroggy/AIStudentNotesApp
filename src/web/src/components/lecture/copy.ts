// Single source of truth for every student-facing string on the live-lecture screens.
// ALL TEXT HERE IS DRAFT (docs/ux/m2-live-lecture-screens.md, gate G1 BLOCKED): placeholder wording for
// the mock-backed build, not legal wording, and must not be shown to real students.
// copy.test.ts lints this file for forbidden phrases.

/** Must equal ConsentTextRegistry.DraftV2 on the server (src/api/.../ConsentTextRegistry.cs); its text is the dialog checkbox sentence. */
export const CONSENT_TEXT_VERSION = "draft-2";

export const DEMO_SOURCE_TAG = "Demo source: replayed test audio";

export const DEMO_NOTICE_COPY = {
  heading: "Demo source: these words are not from your microphone",
  body: "The transcript is a fixed test script that plays as audio arrives. No real speech service is listening, so what you say is not turned into text. Audio is only used to time the script and is not kept. (Draft wording.)",
  consentBullet: "Draft: while Tether runs on its demo source, the transcript is a fixed test script, not what your microphone hears.",
} as const;

export const FATAL_COPY = {
  signedOutHeading: "You were signed out",
  signedOutBody: "The microphone is off and nothing more is being recorded. Lines already saved are kept, but the last moments may be missing. Sign in to see the lecture again.",
  signIn: "Sign in",
  unavailableHeading: "This lecture can not be reached",
  unavailableBody: "The microphone is off. We could not confirm what was saved, so the last moments may be missing. Go back to the course and open the lecture again.",
  back: "Back to course",
} as const;

export const AUDIO_NOTE_COPY = {
  skipped: (seconds: number) => `Some audio could not be sent in time and was skipped (about ${seconds} seconds). Those moments may be missing from the transcript.`,
} as const;

export const CONSENT_COPY = {
  heading: (courseName: string) => `Start lecture: ${courseName}`,
  draftTag: "Draft",
  draftNotice: "Draft wording, not reviewed by counsel or your school.",
  intro: "We turn on your microphone and write what is said into a transcript.",
  bullets: [
    'A "Recording" indicator stays on screen the whole time.',
    "You can pause or stop whenever you like.",
    "Audio is not kept. The transcript is kept until you delete it. (Draft: retention is not final.)",
    "Draft: text is processed by a speech service and an AI service.",
    "Deleting a lecture here does not remove copies already held by those services or by Google.",
  ],
  whatDoesThisMean: "What does this mean?",
  whatDoesThisMeanBody: "Tether is not a legal advisor. Ask your instructor if you are unsure.",
  attested: (courseName: string, dateLabel: string) => `You confirmed this for ${courseName} on ${dateLabel}.`,
  /** Opens the teacher-permission dialog; nothing starts until the dialog is confirmed. */
  start: "Start recording",
  starting: "Starting...",
  notNow: "Not now",
  errors: {
    consent_required: "We need your confirmation before starting.",
    invalid_state: "You already have a lecture running. Open it or stop it first.",
    network: "We could not reach the server. Try again.",
  },
} as const;

/**
 * DRAFT wording, not legal text. Teacher-permission pop-up from PO-16 (personal use; gate G1 stays BLOCKED).
 * Plain, warm and blame-free per docs/ux/language-guide.md. It makes no legal claim and does not say recording
 * is allowed, only that the student confirms their teacher said yes. Fuller consent wording stays DEFERRED.
 * The server stores this as textVersion CONSENT_TEXT_VERSION; see the note on that constant.
 */
export const CONSENT_DIALOG_COPY = {
  heading: "Did your teacher say you can record?",
  body: (courseName: string) => `Tether turns on the microphone only after you confirm this for ${courseName}.`,
  checkbox: "My teacher said I can record this class.",
  notSure: "Not sure? Choose Cancel. The microphone stays off, and you can ask your teacher first.",
  checkHint: "Check the box above to continue.",
  confirm: "Confirm and start recording",
  cancel: "Cancel",
} as const;

export const STATUS_COPY = {
  recording: "Recording",
  paused: "Paused",
  notRecording: "Not recording",
  stopped: "Stopped",
  pausing: "Pausing...",
  resuming: "Resuming...",
  stopping: "Stopping...",
  connected: "Connected",
  notConnected: "Not connected",
  helpQuiet: "Prompts: Quiet",
  pause: "Pause",
  resume: "Resume",
  stop: "Stop",
  unavailable: "Not available at the moment",
  keyboardHelp: "Keyboard help",
} as const;

export const ANNOUNCE = {
  starting: "Starting.",
  started: "Recording started.",
  paused: "Recording is paused. Microphone is off.",
  resumed: "Recording resumed.",
  backOnline: "Back online. Recording is on.",
  micStopped: "The microphone stopped. Recording is paused.",
  disconnected: "Not connected. Audio is not being saved.",
  mismatch: "The app and your microphone disagree, so the microphone is off.",
  autoPaused: "Recording was paused automatically. Nothing is being recorded.",
  autoStopped: "The lecture was stopped automatically. Your transcript is saved.",
  tldrUpdated: "TL;DR updated",
  actionFailed: "That did not work. The status bar shows what is true now.",
  resumeBusy: "Give it a moment, then try Resume again. Nothing was changed.",
  offlinePaused: "No connection for a minute, so recording was paused. The microphone is off.",
  signedOut: "You were signed out. The microphone is off and nothing more is being recorded.",
  unavailable: "This lecture can not be reached. The microphone is off.",
} as const;

export const PAUSED_COPY = {
  heading: "Recording is paused",
  body: "Your microphone is off. Nothing is being heard or written.",
  resume: "Resume recording",
  stop: "Stop lecture",
  since: (t: string) => `Paused since ${t}`,
  autoStopRule: "Will stop on its own after 30 min paused",
  micStoppedHeading: "Microphone stopped",
  micStoppedBody: "Your microphone stopped, so recording is paused. Nothing is being heard or written.",
  autoHeading: "Recording was paused automatically",
  autoBody: "No audio was received for 5 minutes, so we paused to be safe. Nothing is being recorded.",
  divider: (from: string, to: string | null) => (to === null ? `Paused ${from} to now` : `Paused ${from} to ${to}`),
} as const;

export const STOP_DIALOG_COPY = {
  heading: "Stop and save this lecture?",
  confirm: "Stop",
  cancel: "Keep paused",
  cancelActive: "Keep recording",
} as const;

export const STOPPED_COPY = {
  heading: "Lecture stopped",
  user: "Your transcript is saved.",
  auto_stop_paused_timeout: "Lecture stopped automatically after 30 minutes paused. Your transcript is saved.",
  hard_cap: "This lecture reached the 3 hour limit and was stopped. Your transcript is saved.",
  abandoned: "This lecture ended unexpectedly. Here is what we saved.",
  system: "This lecture was ended by the system. Your transcript is saved.",
  back: "Back to course",
  stoppingOffline: "Stopping... will finish when you are back online.",
} as const;

export const BLOCKED_COPY = {
  heading: "Microphone is blocked",
  bodyConfirmed: "We can't hear the lecture yet. Nothing has been recorded.",
  bodyUnconfirmed: "We can't hear the lecture yet. We could not confirm that nothing was saved; check your lectures list.",
  noDevice: "No microphone was found. Connect one, then try again.",
  tryAgain: "Try again",
  back: "Back to course",
  howTo: "How to turn it on",
  chrome: "Chrome",
  chromeBody: "Select the icon at the left of the address bar, then allow Microphone for this site. (Draft instructions.)",
  safari: "Safari",
  safariBody: "Open Safari settings, choose Websites, then Microphone, and allow this site. (Draft instructions.)",
  firefox: "Firefox",
  firefoxBody: "Select the permissions icon in the address bar and clear the blocked Microphone setting. (Draft instructions.)",
} as const;

export const OFFLINE_COPY = {
  banner: "Not connected. Audio is not being saved.",
  retrying: "Trying to reconnect.",
  resumeDisabledReason: "Resume is not available while not connected",
} as const;

export const MISMATCH_COPY = {
  heading: "The app and your microphone disagree",
  body: "The app and your microphone disagree, so the microphone is off. Nothing is being recorded. Choose Check again.",
  bodyMicOff: "The server shows a recording, but your microphone is not on. Nothing is being recorded here. Choose Check again.",
  check: "Check again",
  stop: "Stop lecture",
} as const;

export const TRANSCRIPT_COPY = {
  heading: "Transcript",
  logLabel: "Transcript lines",
  empty: "Words will appear here once the lecture starts.",
  edited: "edited",
  jumpToLive: "Jump to live",
  skipToTranscript: "Skip to transcript",
  skipToTldr: "Skip to TL;DR",
  skipToQuickRecap: "Skip to Quick recap",
  gap: (from: string, to: string, reason: string) => `Audio not available ${from} to ${to} (${reason})`,
  gapReasons: { paused: "paused", network: "connection", device: "device", provider: "speech service" },
} as const;

export const TLDR_COPY = {
  heading: "TL;DR",
  empty: "The summary will appear here as the lecture goes on.",
  updated: (t: string, revision: number) => `Updated at ${t} (revision ${revision})`,
  hold: "Pause updates",
  resume: "Resume updates",
  paused: (t: string) => `Updates are paused. You are reading the summary from ${t}.`,
  newer: "Newer summary available",
  showLatest: "Show latest",
  showMore: "Show more",
  showLess: "Show less",
  from: (t: string) => `From ${t}`,
} as const;

export const SETTINGS_COPY = {
  heading: "Lecture settings",
  announceLines: "Announce new transcript lines",
  announceTldr: "Announce TL;DR updates",
  reduceMotion: "Reduce motion",
  shortcuts: "Enable Alt+ keyboard shortcuts (experimental, off until tested with screen readers)",
  shortcutList: "Alt+P pauses or resumes. Alt+J jumps to the live transcript. Alt+R goes to Quick recap.",
} as const;

/** Shown wherever text came from the deterministic mock (source === "mock"), never hidden or dismissible. */
export const DEMO_SUMMARY_COPY = {
  tag: "Demo summary",
  note: "Made by a test routine, not an AI model.",
} as const;

export const SUMMARY_STATUS_COPY = {
  delayed: "The summary is updating more slowly than usual. The transcript is not affected.",
  failed: "The summary could not update just now. The transcript is not affected.",
  draft: "Draft",
  asOf: (t: string) => `As of ${t}`,
} as const;

export const QUICK_RECAP_COPY = {
  heading: "Quick recap",
  hint: "How far back? The lecture keeps going.",
  groupLabel: "How far back",
  window: (m: number) => `Last ${m} min`,
  windowUsual: (m: number) => `Last ${m} min (your usual)`,
  // WCAG 2.5.3 Label in Name: the visible text starts the accessible name (Lead approval, 2026-10-29).
  windowUsualLabel: (m: number) => `Last ${m} min (your usual): recap of the last ${m} minutes`,
  windowLabel: (m: number) => `Last ${m} min: recap of the last ${m} minutes`,
  covers: (m: number) => `Covers the last ${m} minutes`,
  coversShort: (n: number) => `The lecture has run ${n} ${n === 1 ? "minute" : "minutes"}. Here is all of it.`,
  // The guide proposes "The lecture has only just begun."; its own checklist bans "just", so this states the fact instead.
  coversShortBrief: "The lecture began less than a minute ago. Here is all of it.",
  coversRange: (from: string, to: string) => `Covers ${from} to ${to}.`,
  emptyWindow: "Nothing was said in this time. If you paused, the lecture may have been quiet.",
  previousLabel: (m: number) => `Earlier recap, last ${m} min`,
  working: "Getting what was said...",
  resultHeading: "Here is what was said",
  said: "Word for word",
  saidNote: "These are the words from the lecture, newest last. Not a summary.",
  showEarlier: "Show earlier words",
  hideEarlier: "Show fewer words",
  rightNow: "Most recent",
  pending: "A short recap is being written. The words themselves are below.",
  slow: "Still writing your recap. Here is what was said so far.",
  unavailable: "A recap is not available at the moment. Here is what was said.",
  budget: "A recap is not available for this lecture. Here is what was said.",
  recapReady: "Recap ready.",
  showDetail: "Show more detail",
  hideDetail: "Show less detail",
  done: "Back to lecture",
  backShort: "Close recap",
  moreHeading: "More detail",
  whyItMatters: "Why it matters",
  keyTerms: "Key terms",
  questions: "Questions you could ask",
  gaps: "Also mentioned",
  confidence: (c: string) => `Confidence: ${c}`,
  basis: {
    lecture: null,
    general_knowledge: "Not from your lecture",
    uncertain: "Not sure about this",
  },
  from: (t: string) => `From ${t}`,
  errors: {
    invalid_state: "This lecture has ended, so there is nothing new to recap. Your transcript is saved.",
    rate_limited: "One moment. Try again in about a minute. Nothing was changed.",
    not_found: "We can't find this lecture. Go back to the course and open it again.",
    network: "We could not reach the server. Nothing was changed. Try again when you like.",
    generic: "That did not work. Nothing was changed. You can try again when you like.",
  },
} as const;
