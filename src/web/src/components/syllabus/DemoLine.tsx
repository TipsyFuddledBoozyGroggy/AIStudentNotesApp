import { DEMO_WORD } from "./copy";

/**
 * Permanent, non-dismissible Demo line (UX 6): a text tag plus one sentence. Not a live region. `tag` defaults to "Demo";
 * calendar lines pass "Demo calendar".
 */
export function DemoLine({ text, tag = DEMO_WORD }: { text: string; tag?: string }) {
  return (
    <p className="flex flex-wrap items-center gap-2 text-sm text-ink" data-demo-tag="">
      <span className="tag tag-warning">{tag}</span>
      <span>{text}</span>
    </p>
  );
}
