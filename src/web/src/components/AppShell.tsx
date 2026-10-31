"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { api, type Course } from "@/lib/api";
import { useAuth } from "./AuthProvider";
import { CommandPalette, type Command } from "./CommandPalette";
import { PreferencesPanel } from "./PreferencesPanel";
import { usePrefs } from "./PrefsProvider";

const DESTINATIONS = [
  { href: "/", label: "Today", match: (p: string) => p === "/" },
  { href: "/courses", label: "Courses", match: (p: string) => p.startsWith("/courses") },
] as const;

/** Two destinations as header tabs (redesign-round2 2.3, PO-17): a nav of links, the current one marked with aria-current. */
function NavTabs({ pathname }: { pathname: string }) {
  return (
    // The wordmark sits in a fixed 7rem slot, so the tabs start at the same x under every heading face; -ml-5 (the bar's
    // 8px gap plus the tab's 12px padding) puts the first tab's ink on that slot's edge, which from 1024px is also the
    // Desk column start (5rem margin + 2rem gap). At 320 the wordmark and both tabs then share the first row.
    <nav aria-label="Primary">
      <ul className="-ml-5 flex items-center gap-x-2">
        {DESTINATIONS.map((d) => (
          <li key={d.href}>
            <Link href={d.href} aria-current={d.match(pathname) ? "page" : undefined} className="nav-tab">
              {d.label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}

export type Template = "desk" | "sheet" | "auth" | "plain";

/**
 * Page template by route (redesign-round2 2.3): Desk for Today, the course page, All courses and the syllabus screens;
 * Sheet for Add a course and the consent screen; Auth for sign-in. Dev routes carry no template. On the live lecture
 * screen the lecture root lays itself out and the shell's gutters are removed (globals.css).
 */
export function templateFor(pathname: string): Template {
  if (pathname === "/signin") return "auth";
  if (pathname.startsWith("/dev/")) return "plain";
  if (pathname === "/courses/new" || /^\/courses\/[^/]+\/lecture$/.test(pathname) || pathname.startsWith("/lectures/")) return "sheet";
  return "desk";
}

/**
 * The lecture route (redesign-round2 slice 7): the consent screen, the mic-blocked screen, the live lecture and a reopened
 * lecture, plus the dev-only lecture harness that renders the same screens. globals.css keys off `main[data-route="lecture"]`
 * so no page decoration (Gamer grid, Glass gradient) and no frosted header appear anywhere on it, not only once
 * `[data-lecture-root]` exists: starting a recording never restyles the page around the student.
 */
export function isLectureRoute(pathname: string): boolean {
  return /^\/courses\/[^/]+\/lecture$/.test(pathname) || pathname.startsWith("/lectures/") || pathname.startsWith("/dev/lecture-harness/");
}

/**
 * Site shell (redesign-round2 2.3, slice 1): one 56px bar with the wordmark, the two destinations as text tabs (Today,
 * Courses), then Search or jump, Preferences and Sign out as quiet buttons; the display name lives in Preferences > Account.
 * Keyboard order: skip link, wordmark, Today, Courses, Search, Preferences, Sign out, main. Below 640px the actions wrap
 * onto a second row. On the live lecture screen the lecture bar replaces all of this (globals.css).
 */
export function AppShell({ children }: { children: ReactNode }) {
  const { status, user, signOut } = useAuth();
  const pathname = usePathname() ?? "/";
  const authed = status === "authenticated" && !!user;
  const [prefsOpen, setPrefsOpen] = useState(false);
  const prefsButton = useRef<HTMLButtonElement>(null);
  const router = useRouter();
  const { prefs, update } = usePrefs();
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [courses, setCourses] = useState<Course[] | null>(null);
  const paletteOpener = useRef<HTMLElement | null>(null);

  const openPalette = useCallback(() => {
    paletteOpener.current = document.activeElement as HTMLElement | null;
    setPaletteOpen(true);
    setCourses(null);
    api
      .listCourses()
      .then((r) => setCourses(r.items))
      .catch(() => setCourses([])); // places still work without the course list
  }, []);
  const closePalette = useCallback(() => {
    setPaletteOpen(false);
    queueMicrotask(() => paletteOpener.current?.focus());
  }, []);

  // Ctrl/Cmd+K: the one default-on shortcut (it has a visible button). Never during a live lecture.
  useEffect(() => {
    if (!authed) return;
    const h = (e: KeyboardEvent) => {
      if (!(e.ctrlKey || e.metaKey) || e.altKey || e.key.toLowerCase() !== "k") return;
      if (document.querySelector("[data-lecture-root]")) return;
      e.preventDefault();
      if (!paletteOpen) openPalette();
    };
    window.addEventListener("keydown", h);
    return () => window.removeEventListener("keydown", h);
  }, [authed, paletteOpen, openPalette]);

  const commands = useMemo<Command[]>(() => {
    const go = (href: string) => () => router.push(href);
    const list: Command[] = [
      { id: "today", label: "Today", group: "Place", keywords: "home dashboard", run: go("/") },
      { id: "courses", label: "All courses", group: "Place", run: go("/courses") },
      { id: "add", label: "Add a course", group: "Place", keywords: "new create", run: go("/courses/new") },
    ];
    for (const c of courses ?? []) {
      const id = encodeURIComponent(c.id);
      list.push({ id: `c-${c.id}`, label: c.name, group: "Course", keywords: `${c.code ?? ""} ${c.term ?? ""} ${c.instructor ?? ""}`, run: go(`/courses/${id}`) });
      // Opens the consent screen only: nothing is recorded until Start recording is pressed there (review S4 wording).
      list.push({ id: `l-${c.id}`, label: `Open the lecture screen for ${c.name} (asks first)`, group: "Lecture", keywords: "lecture start record consent", run: go(`/courses/${id}/lecture`) });
    }
    list.push(
      { id: "prefs", label: "Open Preferences", group: "Look", keywords: "settings", run: () => setPrefsOpen(true) },
      // Colour mode, not Theme: since slice 5 "Theme" names the five looks (Paper, Course portal, ...), chosen in Preferences.
      { id: "t-light", label: "Use light mode", group: "Look", keywords: "theme colour color mode light paper", run: () => update({ theme: "light" }) },
      { id: "t-dark", label: "Use dark mode", group: "Look", keywords: "theme colour color mode dark night", run: () => update({ theme: "dark" }) },
      { id: "t-sys", label: "Follow your device's colour mode", group: "Look", keywords: "theme colour color mode system auto", run: () => update({ theme: "system" }) },
      {
        id: "quiet",
        label: prefs.quiet ? "Turn off the low-stimulation look" : "Turn on the low-stimulation look",
        group: "Look",
        keywords: "quiet calm fog dusk",
        run: () => update({ quiet: !prefs.quiet }),
      },
      {
        id: "focus",
        label: prefs.focusMode ? "Start lectures without Focus mode" : "Start lectures in Focus mode",
        group: "Look",
        keywords: "focus fewer panels",
        run: () => update({ focusMode: !prefs.focusMode }),
      },
    );
    return list;
  }, [courses, prefs.quiet, prefs.focusMode, router, update]);

  const closePrefs = () => {
    setPrefsOpen(false);
    queueMicrotask(() => prefsButton.current?.focus());
  };

  // Container 72rem with the gutters 16 / 24 / 32 outside it (themes-spec R2): content-box, so the header, the Preferences
  // panel, the content column and the lecture bar / columns all share one content edge (R3).
  const container = "mx-auto box-content max-w-content px-4 sm:px-6 lg:px-8";

  return (
    <>
      <header data-site-header data-chrome className="z-10 border-b border-hair lg:sticky lg:top-0">
        {/* 56px bar: 48px content minimum plus 4px padding above and below. */}
        <div className={`${container} flex min-h-12 flex-wrap items-center gap-x-2 gap-y-1 py-1`}>
          <Link href="/" className="wordmark display inline-flex min-h-[44px] min-w-28 items-center text-2xl leading-none">
            Tether
          </Link>
          {authed ? <NavTabs pathname={pathname} /> : null}
          {/* R6: one gap between the actions; the last one carries .optical-end so its ink, not its box, meets the container edge. */}
          <div className="ml-auto flex flex-wrap items-center justify-end gap-x-2 gap-y-1">
            {authed ? (
              <button type="button" className="btn-tertiary btn-sm" aria-keyshortcuts="Control+K Meta+K" onClick={openPalette}>
                Search or jump
                <kbd aria-hidden="true" className="hidden rounded border border-hair bg-sunken px-1 font-sans text-xs text-muted md:inline">
                  Ctrl K
                </kbd>
              </button>
            ) : null}
            <button
              ref={prefsButton}
              type="button"
              className={`btn-tertiary btn-sm ${authed ? "" : "optical-end"}`}
              aria-expanded={prefsOpen}
              aria-controls="preferences"
              onClick={() => (prefsOpen ? closePrefs() : setPrefsOpen(true))}
            >
              Preferences
            </button>
            {authed ? (
              <button type="button" className="btn-tertiary btn-sm optical-end" onClick={() => void signOut()}>
                Sign out
              </button>
            ) : null}
          </div>
        </div>
      </header>
      {prefsOpen ? (
        <div data-site-prefs className={container}>
          <PreferencesPanel onClose={closePrefs} userId={authed ? user.id : null}>
            {authed ? (
              <>
                <h3 className="label mt-8">Account</h3>
                <p className="mt-2">Signed in as {user.displayName}.</p>
              </>
            ) : null}
          </PreferencesPanel>
        </div>
      ) : null}
      <div data-shell className={container}>
        <main id="main" tabIndex={-1} data-template={templateFor(pathname)} data-route={isLectureRoute(pathname) ? "lecture" : undefined} className="min-w-0 py-8">
          <div data-column-start className="min-w-0">
            {children}
          </div>
        </main>
      </div>
      {paletteOpen ? <CommandPalette commands={commands} onClose={closePalette} loading={courses === null} /> : null}
    </>
  );
}
