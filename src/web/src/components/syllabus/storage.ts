// Per-viewer conveniences only (drafts, view settings). Every access is wrapped: storage can be missing or throw
// (private windows, blocked site data), and the screens must work without it. Server state is never stored here.

const PREFIX = "tether.syllabus.";

export function readLocal<T>(key: string, check: (v: unknown) => v is T): T | null {
  try {
    const raw = window.localStorage.getItem(PREFIX + key);
    if (raw === null) return null;
    const v: unknown = JSON.parse(raw);
    return check(v) ? v : null;
  } catch {
    return null;
  }
}

export function writeLocal(key: string, value: unknown): void {
  try {
    window.localStorage.setItem(PREFIX + key, JSON.stringify(value));
  } catch {
    /* a lost draft is acceptable; the page keeps working */
  }
}

export function removeLocal(key: string): void {
  try {
    window.localStorage.removeItem(PREFIX + key);
  } catch {
    /* ignore */
  }
}

export const isRecord = (v: unknown): v is Record<string, unknown> => typeof v === "object" && v !== null && !Array.isArray(v);

// ---------------------------------------------------------------------------------------------------------------------
// Drafts (SB-06): the pasted syllabus text (up to 50,000 characters) and item edit drafts are the student's own words, so
// they are keyed by the signed-in user's id (`tether.syllabus.u.<userId>.draft.<courseId>`,
// `tether.syllabus.u.<userId>.edit.<syllabusId>.<itemId>`) and cleared on sign-out (every user's), on syllabus delete
// (that syllabus's item drafts and its course's paste draft) and after a successful create (that course's paste draft).
// When a user is set, drafts of any other user and the old unscoped keys (before slice 8x) are removed too, so a session
// that expired without a sign-out does not leave them for the next person on a shared computer. With no signed-in user
// known, nothing is kept or read. The owner is set by AuthProvider from /me (client only).

let owner: string | null = null;
const USER_PREFIX = `${PREFIX}u.`;
const LEGACY = [/^tether\.syllabus\.draft\./, /^tether\.syllabus\.edit\./];
const seg = (id: string) => encodeURIComponent(id).replace(/\./g, "%2E"); // one dot-free key segment
const ownedPrefix = (id: string) => `${USER_PREFIX}${seg(id)}.`;

/** Removes every localStorage key the predicate accepts. Never throws. */
function removeWhere(pred: (key: string) => boolean): void {
  try {
    const ls = window.localStorage;
    const keys: string[] = [];
    for (let i = 0; i < ls.length; i++) {
      const k = ls.key(i);
      if (k !== null && pred(k)) keys.push(k);
    }
    for (const k of keys) ls.removeItem(k);
  } catch {
    /* storage unavailable: nothing could have been kept either */
  }
}

/** Called by AuthProvider whenever the signed-in user is known (or null when not). */
export function setDraftOwner(userId: string | null): void {
  owner = userId;
  if (userId === null) return;
  const mine = ownedPrefix(userId);
  removeWhere((k) => LEGACY.some((r) => r.test(k)) || (k.startsWith(USER_PREFIX) && !k.startsWith(mine)));
}

/** Sign-out: every syllabus draft of every user, and the legacy unscoped ones. View settings are kept. */
export function clearAllDrafts(): void {
  owner = null;
  removeWhere((k) => k.startsWith(USER_PREFIX) || LEGACY.some((r) => r.test(k)));
}

/**
 * A deleted syllabus: its item edit drafts, its "Dates we noticed" states and add-form drafts (PO-20b,
 * docs/ux/unclaimed-dates-list.md 4.6), and its course's paste draft (this user's; legacy keys too). Each prefix is named
 * on its own: `unclaimed.` and `unclaimed-add.` are matched explicitly, never one through the other.
 */
export function clearSyllabusDrafts(syllabusId: string, courseId?: string): void {
  const prefixes = [`edit.${syllabusId}.`, `unclaimed.${syllabusId}.`, `unclaimed-add.${syllabusId}.`];
  const draft = courseId === undefined ? null : `draft.${courseId}`;
  removeWhere((k) => {
    const rest = k.startsWith(USER_PREFIX) ? k.slice(k.indexOf(".", USER_PREFIX.length) + 1) : k.startsWith(PREFIX) ? k.slice(PREFIX.length) : null;
    return rest !== null && (prefixes.some((p) => rest.startsWith(p)) || (draft !== null && rest === draft));
  });
}

/**
 * Every draft of the signed-in user whose key starts with `prefix`, as [rest of the key, value] pairs (values that fail
 * `check` are skipped). Empty with no signed-in user or no storage. Used to read the "Dates we noticed" states before the
 * list itself is fetched.
 */
export function readDraftsUnder<T>(prefix: string, check: (v: unknown) => v is T): [string, T][] {
  if (owner === null) return [];
  const full = `${ownedPrefix(owner)}${prefix}`;
  const out: [string, T][] = [];
  try {
    const ls = window.localStorage;
    for (let i = 0; i < ls.length; i++) {
      const k = ls.key(i);
      if (k === null || !k.startsWith(full)) continue;
      const raw = ls.getItem(k);
      if (raw === null) continue;
      try {
        const v: unknown = JSON.parse(raw);
        if (check(v)) out.push([k.slice(full.length), v]);
      } catch {
        /* a corrupt entry is ignored */
      }
    }
  } catch {
    /* storage unavailable */
  }
  return out;
}

export function readDraft<T>(key: string, check: (v: unknown) => v is T): T | null {
  if (owner === null) return null;
  return readLocal(`u.${seg(owner)}.${key}`, check);
}

export function writeDraft(key: string, value: unknown): void {
  if (owner === null) return;
  writeLocal(`u.${seg(owner)}.${key}`, value);
}

export function removeDraft(key: string): void {
  if (owner === null) return;
  removeLocal(`u.${seg(owner)}.${key}`);
}
