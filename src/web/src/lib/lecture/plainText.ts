/**
 * Plain-text guard for AI/transcript output (contract section 10, C-28).
 *
 * Rule: every server-provided text field (transcript text, tldr, bullets, catch-up text,
 * error detail, titles) is rendered ONLY as a React text node, i.e. `{plainText(value)}`.
 * Never a raw-HTML injection API, never a markdown renderer, never turned into href/src/style.
 * A test (plainText.test.tsx) scans src for the raw-HTML APIs.
 *
 * plainText() does not escape HTML (React already does that for text nodes, and escaping here
 * would double-encode). It removes characters that can disguise text even as a text node:
 * C0/C1 control characters (except newline and tab) and Unicode bidirectional overrides/isolates.
 */
const CONTROL = /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/g;
const BIDI = new RegExp("[\u202a-\u202e\u2066-\u2069\u200e\u200f\u061c]", "g");

export function plainText(value: unknown): string {
  if (typeof value !== "string") return "";
  return value.replace(CONTROL, "").replace(BIDI, "");
}
