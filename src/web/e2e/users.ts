import path from "node:path";

// Fresh per-run users (auto-created by /dev/login) avoid dependence on shared DB state.
// Workers re-import this module, so the run id is shared through the environment.
const id = (process.env.E2E_RUN_ID ??= `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`);
export const AUTH_DIR = path.join(__dirname, ".auth");
export const USERS = {
  alice: "alice@dev.local",
  bob: "bob@dev.local",
  empty: `e2e-empty-${id}@dev.local`,
  signout: `e2e-signout-${id}@dev.local`,
  lostSession: `e2e-lost-${id}@dev.local`,
  keyboard: `e2e-kbd-${id}@dev.local`,
  reload: `e2e-reload-${id}@dev.local`,
  axe: `e2e-axe-${id}@dev.local`,
  axeEmpty: `e2e-axeempty-${id}@dev.local`,
  // Syllabus specs: one user per test keeps each inside the per-user syllabus write bucket (20/min) and the daily
  // create quota (10/day).
  sylMain: `e2e-syl-main-${id}@dev.local`,
  sylOther: `e2e-syl-other-${id}@dev.local`,
  sylHostile: `e2e-syl-hostile-${id}@dev.local`,
  sylUpload: `e2e-syl-upload-${id}@dev.local`,
  // A browser reporting UTC (syllabus-utc.spec.ts): its own user so it never shares the syllabus write bucket.
  sylUtc: `e2e-syl-utc-${id}@dev.local`,
  // Notes after Stop (slice 8a): its own user, because a user may have only one pending notes press across all lectures.
  notes: `e2e-notes-${id}@dev.local`,
  // Reopen-route hub lifecycle (slice 8x): its own user, because the hub caps concurrent connections per user (C-26: 3).
  reopen: `e2e-reopen-${id}@dev.local`,
  // PO-19 move after Stop: its own user, because "Don't show this again" is a per-user preference the spec turns on and off.
  stopmove: `e2e-stopmove-${id}@dev.local`,
  // PO-20by delete a lecture / delete a course: its own user, so the courses it deletes are never another spec's.
  deleter: `e2e-delete-${id}@dev.local`,
  // Finish-backlog item 2, Export notes / Copy notes: its own user, because a user may have only one pending notes press.
  notesExport: `e2e-notes-export-${id}@dev.local`,
  // Finish-backlog item 3, Save to a Demo Doc: its own user (one pending notes press per user), and a second user that
  // must not be able to open the first one's Demo Doc (F23).
  docsExport: `e2e-docs-export-${id}@dev.local`,
  docsOther: `e2e-docs-other-${id}@dev.local`,
} as const;
export const authFile = (email: string) => path.join(AUTH_DIR, `${email}.json`);
export const stateFor = (k: keyof typeof USERS) => authFile(USERS[k]);
