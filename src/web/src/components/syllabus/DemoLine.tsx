import { DEMO_WORD } from "./copy";

/** Permanent, non-dismissible Demo line (UX 6): a text tag plus one sentence. Not a live region. */
export function DemoLine({ text }: { text: string }) {
  return (
    <p className="flex flex-wrap items-center gap-2 text-sm text-ink" data-demo-tag="">
      <span className="tag tag-warning">{DEMO_WORD}</span>
      <span>{text}</span>
    </p>
  );
}
