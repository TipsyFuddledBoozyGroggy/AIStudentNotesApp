// Shared copy lint for student-facing string registries (lecture copy.ts, syllabus copy.ts). Test-only helper: it has no
// runtime users. Rules come from design-direction section 1 and docs/ux/language-guide.md sections 2, 3, 6 and 9.

/** Every string in a module, calling functions with sample arguments so templated strings are checked too. */
export function collectStrings(v: unknown, out: string[] = []): string[] {
  if (typeof v === "string") out.push(v);
  else if (typeof v === "function") collectStrings((v as (...a: unknown[]) => unknown)("Biology 101", "00:12", 3, 2), out);
  else if (Array.isArray(v)) v.forEach((x) => collectStrings(x, out));
  else if (v && typeof v === "object") Object.values(v).forEach((x) => collectStrings(x, out));
  return out;
}

export const FORBIDDEN: [string, RegExp][] = [
  ["exclamation mark", /!(?!\s*$)|!$/],
  ["emoji", /\p{Extended_Pictographic}/u],
  ["chirpy filler", /\b(oops|awesome|don't worry)\b|\bjust\b(?! now)/i],
  ["clinical or labelling", /\b(adhd|focus problems?|distracted|lazy)\b/i],
  ["guilt or pressure", /\b(behind|overdue|missed|streak)\b/i],
  ["attention judgement", /\b(lost focus|inactive|idle|away)\b/i],
  // Product Owner rule (2026-10-29): the recovery feature is "Quick recap"; the old deficit-framed name must not return.
  ["old recovery name", /\b(lost track|lost the thread)\b/i],
  ["dishonest offline claim", /still recording on your device|we will fill it in/i],
  // Language guide (docs/ux/language-guide.md sections 2, 3.9, 9): pressure and deficit framing, raw errors.
  ["pressure or deficit framing", /\b(right now|nothing (is|was) lost|here's where you are|you have only been here|catch(-| )up on)\b/i],
  ["raw error text", /request failed|status code|\(\d{3}\)/i],
];

/**
 * Extra rules for the syllabus screens (language guide sections 3.6, 6 and 9; API_CONTRACTS_P2 section 4: no label may
 * read as "verified"; contract 13 row 2: no numeric confidence).
 */
export const FORBIDDEN_SYLLABUS: [string, RegExp][] = [
  ["fault word", /\b(fail|fails|failed|failure|wrong file|you uploaded|your fault|mistake)\b/i],
  ["'missing' about dates (say 'not written')", /\bmissing\b/i],
  ["backlog framing", /\b(pending|outstanding|incomplete|unfinished|undecided|still have|left to do|remaining)\b/i],
  ["urgency", /\b(hurry|urgent|asap|immediately|last chance|don't forget|make sure|you (should|must|need to|have to))\b/i],
  ["certainty claim", /\b(verified|guaranteed|definitely|certainly|confirmed by tether)\b/i],
  ["numeric confidence", /%|\bpercent\b|\b\d+ in 100\b/i],
  ["vague alarm", /\bnothing is wrong\b|\berror\b/i],
  ["praise or ease", /\b(great job|well done|simply|easy|easily)\b/i],
];

export function sentencesOver(strings: string[], maxWords: number): string[] {
  return strings.flatMap((s) => s.split(/(?<=[.?])\s+/)).filter((s) => s.split(/\s+/).length > maxWords);
}
