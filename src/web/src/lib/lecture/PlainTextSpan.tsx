import { plainText } from "./plainText";

/** Renders untrusted text as a single text node inside a span. No markup is ever interpreted. */
export function PlainText({ value, className }: { value: unknown; className?: string }) {
  return <span className={className}>{plainText(value)}</span>;
}
