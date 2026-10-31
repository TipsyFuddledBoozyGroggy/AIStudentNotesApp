"use client";

import { useEffect, useId, useMemo, useRef, useState } from "react";

export interface Command {
  id: string;
  label: string;
  /** Short muted hint on the right, e.g. "Course" or "Look". */
  group: string;
  keywords?: string;
  run(): void;
}

export function filterCommands(commands: Command[], query: string): Command[] {
  const q = query.trim().toLowerCase();
  if (!q) return commands;
  const words = q.split(/\s+/);
  return commands.filter((c) => {
    const hay = `${c.label} ${c.group} ${c.keywords ?? ""}`.toLowerCase();
    return words.every((w) => hay.includes(w));
  });
}

/**
 * Command palette (design-direction section 6): navigation and look only. It has no command that starts, pauses, resumes or
 * stops recording; "Open the start screen" only navigates to the consent page. Modal: focus stays on the input, Escape closes
 * and the caller restores focus. Results count goes to its own status line, debounced, only while open.
 */
export function CommandPalette({ commands, onClose, loading }: { commands: Command[]; onClose(): void; loading?: boolean }) {
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const [announced, setAnnounced] = useState("");
  const input = useRef<HTMLInputElement>(null);
  const listId = useId();
  const titleId = useId();
  const results = useMemo(() => filterCommands(commands, query), [commands, query]);

  useEffect(() => {
    input.current?.focus();
  }, []);
  useEffect(() => setActive(0), [query]);
  useEffect(() => {
    const t = setTimeout(() => setAnnounced(results.length === 1 ? "1 result" : `${results.length} results`), 500);
    return () => clearTimeout(t);
  }, [results.length]);

  const run = (c: Command | undefined) => {
    if (!c) return;
    onClose();
    c.run();
  };

  return (
    <div className="dialog-scrim fixed inset-0 z-50 flex items-start justify-center px-4 pt-[12vh]" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div role="dialog" aria-modal="true" aria-labelledby={titleId} className="w-full max-w-xl overflow-hidden rounded-sheet border border-line bg-raised shadow-3">
        <h2 id={titleId} className="sr-only">
          Search or jump
        </h2>
        <div className="border-b border-hair p-3">
          <label htmlFor={`${listId}-input`} className="sr-only">
            Search courses and places
          </label>
          <input
            id={`${listId}-input`}
            ref={input}
            type="text"
            role="combobox"
            aria-expanded="true"
            aria-controls={listId}
            aria-autocomplete="list"
            aria-activedescendant={results[active] ? `${listId}-${results[active].id}` : undefined}
            autoComplete="off"
            placeholder="Type a course or a place"
            className="field mt-0 border-0 bg-transparent text-lg focus-visible:outline-none focus-visible:[box-shadow:none]"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "ArrowDown") {
                e.preventDefault();
                setActive((i) => Math.min(results.length - 1, i + 1));
              } else if (e.key === "ArrowUp") {
                e.preventDefault();
                setActive((i) => Math.max(0, i - 1));
              } else if (e.key === "Enter") {
                e.preventDefault();
                run(results[active]);
              } else if (e.key === "Escape") {
                e.preventDefault();
                onClose();
              } else if (e.key === "Tab") {
                e.preventDefault(); // the input is the only stop; arrows move through results
              }
            }}
          />
        </div>
        <ul id={listId} role="listbox" aria-label="Results" className={`max-h-[50vh] overflow-y-auto ${results.length ? "p-2" : ""}`}>
          {results.map((c, i) => (
            <li
              key={c.id}
              id={`${listId}-${c.id}`}
              role="option"
              aria-selected={i === active}
              className={`flex min-h-[44px] cursor-pointer items-center justify-between gap-3 rounded-control border-l-4 px-3 py-2 text-ink ${
                i === active ? "border-accent bg-accent-subtle" : "border-transparent"
              }`}
              onMouseMove={() => setActive(i)}
              onClick={() => run(c)}
            >
              <span className={`min-w-0 break-words ${i === active ? "font-semibold" : ""}`}>{c.label}</span>
              <span className="shrink-0 text-xs text-muted">{c.group}</span>
            </li>
          ))}
        </ul>
        {results.length === 0 ? <p className="px-5 pb-4 text-muted">{loading ? "Getting your courses." : "Nothing matches. Try a course name."}</p> : null}
        <div className="flex flex-wrap items-center justify-between gap-2 border-t border-hair px-4 py-2 text-xs text-muted">
          <span>Arrow keys to move, Enter to open, Escape to close.</span>
          <span role="status">{announced}</span>
        </div>
      </div>
    </div>
  );
}
