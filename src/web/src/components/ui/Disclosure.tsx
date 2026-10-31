import type { ReactNode } from "react";

/**
 * Chevron for disclosures: right when closed, down when open. Two glyphs swapped by the open state instead of a
 * rotation, because nothing may transform (redesign-round2 2.6). Muted, decorative, always beside a word (2.5).
 */
export function Chevron({ open, className = "" }: { open?: boolean; className?: string }) {
  // Without `open` (inside a native <details>), the parent's `group` + `group-open:` swaps the glyphs.
  const auto = open === undefined;
  return (
    <>
      <svg
        width="14"
        height="14"
        viewBox="0 0 14 14"
        aria-hidden="true"
        focusable="false"
        className={`chevron ${auto ? "group-open:hidden" : open ? "hidden" : ""} ${className}`}
        data-chevron="right"
      >
        <path d="M5 2.5 9.5 7 5 11.5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
      <svg
        width="14"
        height="14"
        viewBox="0 0 14 14"
        aria-hidden="true"
        focusable="false"
        className={`chevron ${auto ? "hidden group-open:block" : open ? "" : "hidden"} ${className}`}
        data-chevron="down"
      >
        <path d="M2.5 5 7 9.5 11.5 5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    </>
  );
}

/**
 * Native <details> with a muted chevron and a 44px summary row: the default for "Why this?", "Details", "Show more".
 * The summary is a link (ink text, accent underline), not accent text (redesign-round2 2.2).
 */
export function Disclosure({ summary, children, className = "", name }: { summary: ReactNode; children: ReactNode; className?: string; name?: string }) {
  return (
    <details name={name} className={`group ${className}`}>
      <summary className="link inline-flex min-h-[44px] cursor-pointer list-none items-center gap-2 rounded-control [&::-webkit-details-marker]:hidden">
        <Chevron />
        {summary}
      </summary>
      <div className="mt-1 text-ink">{children}</div>
    </details>
  );
}
