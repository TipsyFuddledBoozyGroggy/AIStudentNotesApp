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

function NavLinks({ pathname, vertical }: { pathname: string; vertical?: boolean }) {
  return (
    <ul className={vertical ? "space-y-1" : "flex flex-col gap-1 py-2"}>
      {DESTINATIONS.map((d) => {
        const current = d.match(pathname);
        return (
          <li key={d.href}>
            <Link
              href={d.href}
              aria-current={current ? "page" : undefined}
              className={`flex min-h-[44px] items-center rounded-control border-l-4 px-3 ${
                current ? "border-accent bg-surface font-bold text-ink" : "border-transparent font-semibold text-muted hover:border-line hover:text-ink"
              }`}
            >
              {d.label}
            </Link>
          </li>
        );
      })}
    </ul>
  );
}

/**
 * Site shell (design-direction section 3): top bar (wordmark, Menu below 1024px, Preferences, name, Sign out), a left rail
 * from 1024px, and the main column. On the live lecture screen the lecture bar replaces all of this (globals.css).
 */
export function AppShell({ children }: { children: ReactNode }) {
  const { status, user, signOut } = useAuth();
  const pathname = usePathname() ?? "/";
  const authed = status === "authenticated" && !!user;
  const [menuOpen, setMenuOpen] = useState(false);
  const [prefsOpen, setPrefsOpen] = useState(false);
  const prefsButton = useRef<HTMLButtonElement>(null);
  const router = useRouter();
  const { prefs, update } = usePrefs();
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [courses, setCourses] = useState<Course[] | null>(null);
  const paletteOpener = useRef<HTMLElement | null>(null);

  useEffect(() => setMenuOpen(false), [pathname]);

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
      { id: "t-light", label: "Use the light theme", group: "Look", keywords: "theme paper", run: () => update({ theme: "light" }) },
      { id: "t-dark", label: "Use the dark theme", group: "Look", keywords: "theme night", run: () => update({ theme: "dark" }) },
      { id: "t-sys", label: "Follow the system theme", group: "Look", keywords: "theme auto", run: () => update({ theme: "system" }) },
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

  return (
    <>
      <header data-site-header data-chrome className="z-10 border-b border-hair lg:sticky lg:top-0">
        <div className="mx-auto flex min-h-[56px] max-w-content flex-wrap items-center gap-x-1 gap-y-1 px-4 py-1 sm:gap-x-2">
          <Link href="/" className="display mr-auto inline-flex min-h-[44px] items-center text-2xl leading-none">
            Tether
          </Link>
          {authed ? (
            <button type="button" className="btn-secondary btn-sm hidden gap-3 sm:inline-flex" aria-keyshortcuts="Control+K Meta+K" onClick={openPalette}>
              Search or jump
              <kbd aria-hidden="true" className="rounded border border-hair bg-sunken px-1.5 py-0.5 font-sans text-xs text-muted">
                Ctrl K
              </kbd>
            </button>
          ) : null}
          {authed ? (
            <button
              type="button"
              className="btn-quiet btn-sm px-2.5 lg:hidden"
              aria-expanded={menuOpen}
              aria-controls="site-menu"
              onClick={() => setMenuOpen((o) => !o)}
            >
              Menu
            </button>
          ) : null}
          <button
            ref={prefsButton}
            type="button"
            className="btn-quiet btn-sm px-2.5"
            aria-expanded={prefsOpen}
            aria-controls="preferences"
            onClick={() => (prefsOpen ? closePrefs() : setPrefsOpen(true))}
          >
            Preferences
          </button>
          {authed ? (
            <>
              <span className="hidden px-2 text-sm text-muted md:inline">{user.displayName}</span>
              <button type="button" className="btn-secondary btn-sm px-3" onClick={() => void signOut()}>
                Sign out
              </button>
            </>
          ) : null}
        </div>
        {authed && menuOpen ? (
          <nav id="site-menu" aria-label="Primary" className="mx-auto max-w-content border-t border-hair px-4 lg:hidden">
            <NavLinks pathname={pathname} />
            <button type="button" className="btn-quiet mb-2 w-full justify-start sm:hidden" onClick={openPalette}>
              Search or jump
            </button>
          </nav>
        ) : null}
      </header>
      {prefsOpen ? (
        <div data-site-prefs className="px-4">
          <PreferencesPanel onClose={closePrefs} />
        </div>
      ) : null}
      <div data-shell className="mx-auto max-w-content px-4 lg:flex lg:gap-10">
        {authed ? (
          <nav data-site-rail aria-label="Primary" className="hidden w-[200px] shrink-0 pt-8 lg:block">
            <NavLinks pathname={pathname} vertical />
          </nav>
        ) : null}
        <main id="main" tabIndex={-1} className="min-w-0 flex-1 py-8">
          {children}
        </main>
      </div>
      {paletteOpen ? <CommandPalette commands={commands} onClose={closePalette} loading={courses === null} /> : null}
    </>
  );
}
