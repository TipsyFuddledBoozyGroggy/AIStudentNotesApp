import type { ReactNode } from "react";

/** Native <details> with a chevron and a 44px summary row: the default for "Why this?", "Details", "Show more". */
export function Disclosure({ summary, children, className = "", name }: { summary: ReactNode; children: ReactNode; className?: string; name?: string }) {
  return (
    <details name={name} className={`group ${className}`}>
      <summary className="inline-flex min-h-[44px] cursor-pointer list-none items-center gap-2 rounded-control font-semibold text-accent underline underline-offset-[0.2em] [&::-webkit-details-marker]:hidden">
        <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden="true" focusable="false" className="shrink-0 transition-transform duration-fast group-open:rotate-90">
          <path d="M5 2.5 9.5 7 5 11.5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
        {summary}
      </summary>
      <div className="mt-1 text-ink">{children}</div>
    </details>
  );
}
