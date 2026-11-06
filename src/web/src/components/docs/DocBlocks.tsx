import type { ReactNode } from "react";
import { PlainText } from "@/lib/lecture";
import type { DocBlock } from "@/lib/docs";
import { formatMs } from "../lecture/format";

/**
 * A Demo Doc's blocks as plain text (contract section 7, F16, F18): every string goes through PlainText as a single text
 * node, so HTML, Markdown or a `javascript:` link in the notes shows as literal characters. There is no
 * raw-HTML injection, no Markdown and no link here. Citations are lecture times in brackets, as text (the Demo Doc
 * keeps no transcript to jump to). Headings are synthesis (serif) as on the Notes sheet; bullets and paragraphs stay in the
 * verbatim face. `base` is the heading level of the surrounding heading, so a level 1 block is one below it.
 */
export function DocBlocks({ blocks, base }: { blocks: readonly DocBlock[]; base: 1 | 2 }) {
  const out: ReactNode[] = [];
  let bullets: { b: Extract<DocBlock, { kind: "bullet" }>; i: number }[] = [];
  const flush = (key: number) => {
    if (bullets.length === 0) return;
    out.push(
      <ul key={`ul-${key}`} className="mt-2 space-y-2">
        {bullets.map(({ b, i }) => (
          <li key={i} data-doc-bullet className="max-w-prose">
            <PlainText value={b.text} />{" "}
            <span className="tabular text-muted" data-doc-times>
              {b.citations.map((c) => `[${formatMs(c.startMs)}]`).join(" ")}
            </span>
          </li>
        ))}
      </ul>,
    );
    bullets = [];
  };
  blocks.forEach((b, i) => {
    if (b.kind === "bullet") {
      bullets.push({ b, i });
      return;
    }
    flush(i);
    if (b.kind === "heading") {
      const level = Math.min(6, base + b.level);
      const Tag = `h${level}` as "h2" | "h3" | "h4";
      out.push(
        <Tag key={i} data-doc-heading className={`synthesis ${b.level === 1 ? "mt-4 text-xl" : "mt-4 text-lg"}`}>
          <PlainText value={b.text} />
        </Tag>,
      );
    } else {
      out.push(
        <p key={i} data-doc-paragraph className="mt-2 max-w-prose">
          <PlainText value={b.text} />
        </p>,
      );
    }
  });
  flush(blocks.length);
  return <>{out}</>;
}
