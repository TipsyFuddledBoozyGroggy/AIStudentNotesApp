"use client";

import { useState } from "react";
import { Chevron, Disclosure } from "./Disclosure";
import { Segmented } from "./Segmented";
import { Switch } from "./Switch";

/**
 * Every primitive in every state, on one page (development only). Slice 2 of redesign-round2 (section 2.7): sheet,
 * ledger, note, stamp, label, button tiers incl. tertiary and the solid disabled style, link, dialog, plus the ui
 * components. The screens spec captures it in all 12 palettes with axe; the geometry spec measures targets, focus rings
 * and the 4px grid on it. Strings here are development copy and still go through the copy lint (primitives.test.tsx).
 */
export const UI_KIT_STRINGS = {
  title: "UI kit (development only)",
  intro: "Every primitive in every state. Paper is the surface, ink is the voice: the serif speaks for what the lecture or the app concluded, the sans for verbatim words, controls and data.",
  sheetHeading: "Transcript",
  sheetBody: "The one white sheet on a screen holds what is being read or filled in. It has a hair border and the skin's radius on desktop, and is square-edged on phones.",
  synthesis: "Derivatives and the power rule.",
  verbatim: "So the derivative of x squared is two x.",
  disabledWhy: "A control that cannot be used now stays focusable and says why.",
  noteNeutral: ["Lecture stopped", "The transcript so far is kept."],
  noteNotice: ["Not connected", "Audio is not being saved. The app keeps trying to reconnect."],
  noteSafety: ["Recording is paused", "Your microphone is off. Nothing is being heard or written."],
  noteDanger: ["We could not save that", "Nothing was changed. Try again when you like."],
  stampSentence: "Made by a test routine, not by an AI model.",
  dialogHeading: "Stop recording?",
  dialogBody: "The transcript so far is kept.",
  ledgerRows: [
    ["Calculus II", "MATH 152, Fall 2026"],
    ["History of Law", "HIST 340, Fall 2026"],
    ["Biology 101", "BIO 101, Fall 2026"],
  ],
} as const;

const S = UI_KIT_STRINGS;

export function UiKit() {
  const [on, setOn] = useState(true);
  const [seg, setSeg] = useState<"a" | "b" | "c">("a");
  const [size, setSize] = useState<"s" | "m" | "l">("m");
  return (
    <div className="space-y-12">
      <div>
        <p className="label">Development</p>
        <h1 className="display mt-1 text-3xl">{S.title}</h1>
        <p className="mt-3 max-w-prose text-muted">{S.intro}</p>
      </div>

      <section aria-labelledby="k-type" className="space-y-4">
        <h2 id="k-type" className="display text-2xl">Type roles and labels</h2>
        <div className="grid gap-6 sm:grid-cols-2">
          <div>
            <p className="label">Synthesis (serif): the TL;DR line</p>
            <p className="display font-synthesis mt-1 text-xl">{S.synthesis}</p>
          </div>
          <div>
            <p className="label">Verbatim (sans): a transcript line</p>
            <p className="font-verbatim mt-1">{S.verbatim}</p>
          </div>
        </div>
        <div className="grid gap-6 sm:grid-cols-3">
          <div>
            <p className="label">Next step</p>
            <p className="mt-1">A label is sentence case, 15/24, muted.</p>
          </div>
          <div>
            <h3 className="label">Your courses</h3>
            <p className="mt-1">It can be a heading.</p>
          </div>
          <div>
            <p className="label tabular">Friday, October 30</p>
            <p className="mt-1">Dates use tabular figures.</p>
          </div>
        </div>
        <p className="max-w-prose">
          A link in prose is ink text with an accent underline:{" "}
          <a className="link" href="#k-type">
            open the course
          </a>
          . On hover the text turns accent.
        </p>
      </section>

      <section aria-labelledby="k-buttons" className="space-y-3">
        <h2 id="k-buttons" className="display text-2xl">Button tiers</h2>
        <div className="flex flex-wrap items-center gap-3">
          <button type="button" className="btn-primary">Primary</button>
          <button type="button" className="btn-secondary">Secondary</button>
          <button type="button" className="btn-tertiary">Tertiary</button>
          <button type="button" className="btn-danger">Delete</button>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <button type="button" className="btn-primary" aria-disabled="true" aria-describedby="k-why">Unavailable</button>
          <button type="button" className="btn-secondary" aria-disabled="true" aria-describedby="k-why">Unavailable</button>
          <button type="button" className="btn-tertiary" aria-disabled="true" aria-describedby="k-why">Unavailable</button>
          <button type="button" className="btn-secondary" aria-busy="true">Saving...</button>
          <button type="button" className="btn-secondary btn-sm">Small</button>
          <button type="button" className="btn-tertiary btn-sm">Small tertiary</button>
        </div>
        <p id="k-why" className="help">{S.disabledWhy}</p>
        <div>
          <button type="button" className="btn-tertiary optical-start">Tertiary on the column edge</button>
        </div>
      </section>

      <section aria-labelledby="k-fields" className="space-y-3">
        <h2 id="k-fields" className="display text-2xl">Fields</h2>
        <div className="grid gap-6 sm:grid-cols-2">
          <div>
            <label htmlFor="k-a" className="field-label">Course name</label>
            <input id="k-a" className="field" defaultValue="Biology 101" />
            <p className="help mt-1">Helper text sits under the field.</p>
          </div>
          <div>
            <label htmlFor="k-b" className="field-label">With an error</label>
            <input id="k-b" className="field" aria-invalid="true" aria-describedby="k-b-err" />
            <p id="k-b-err" className="mt-1 font-semibold text-danger">Enter a course name.</p>
          </div>
        </div>
      </section>

      <section aria-labelledby="k-sheet" className="space-y-3">
        <h2 id="k-sheet" className="display text-2xl">Sheet</h2>
        <div className="sheet sheet-bleed max-w-sheet" data-kit="sheet">
          <h3 className="display text-xl">{S.sheetHeading}</h3>
          <p className="mt-2 max-w-prose">{S.sheetBody}</p>
        </div>
      </section>

      <section aria-labelledby="k-ledger" className="space-y-3">
        <h2 id="k-ledger" className="display text-2xl">Ledger</h2>
        <ul className="ledger max-w-sheet" data-kit="ledger">
          {S.ledgerRows.map(([name, meta]) => (
            <li key={name} className="ledger-row">
              <span className="min-w-0">
                <a href={`#k-${name.replace(/\W+/g, "-")}`} className="ledger-row-link">
                  {name}
                </a>
                <span className="ml-3 text-sm text-muted">{meta}</span>
              </span>
              <span className="ledger-row-end" aria-hidden="true">
                Open <Chevron open={false} />
              </span>
            </li>
          ))}
        </ul>
      </section>

      <section aria-labelledby="k-notes" className="space-y-3">
        <h2 id="k-notes" className="display text-2xl">Notes</h2>
        <div className="grid gap-4 sm:grid-cols-2">
          {(
            [
              ["note-neutral", S.noteNeutral, "Review the transcript"],
              ["note-notice", S.noteNotice, "Details"],
              ["note-safety", S.noteSafety, "Resume recording"],
              ["note-danger", S.noteDanger, "Try again"],
            ] as const
          ).map(([kind, [title, body], action]) => (
            <div key={kind} className={`note ${kind}`} data-kit={kind}>
              <p className="note-title">{title}</p>
              <p className="mt-1">{body}</p>
              <div className="mt-2">
                <button type="button" className={`${kind === "note-safety" ? "btn-primary" : "btn-tertiary optical-start"}`}>
                  {action}
                </button>
              </div>
            </div>
          ))}
        </div>
      </section>

      <section aria-labelledby="k-stamps" className="space-y-3">
        <h2 id="k-stamps" className="display text-2xl">Stamps</h2>
        <p className="stamp-line" data-kit="stamp-line">
          <span className="stamp">Demo summary</span>
          {S.stampSentence}
        </p>
        <div className="flex flex-wrap gap-2">
          <span className="stamp">Demo</span>
          <span className="stamp">Draft</span>
          <span className="stamp">Demo calendar</span>
        </div>
      </section>

      <section aria-labelledby="k-dialog" className="space-y-3">
        <h2 id="k-dialog" className="display text-2xl">Dialog</h2>
        <p className="help">Shown in place (not modal) so the page below stays readable. Live dialogs use the same classes over the whole viewport.</p>
        <div className="dialog-scrim flex justify-center rounded-card p-6" data-kit="dialog-scrim">
          <div className="dialog" role="group" aria-labelledby="k-dialog-h" data-kit="dialog">
            <h3 id="k-dialog-h" className="display text-xl">{S.dialogHeading}</h3>
            <p className="mt-2 text-muted">{S.dialogBody}</p>
            <div className="mt-6 flex flex-wrap justify-end gap-3">
              <button type="button" className="btn-primary">Keep recording</button>
              <button type="button" className="btn-secondary">Stop</button>
            </div>
          </div>
        </div>
      </section>

      <section aria-labelledby="k-controls" className="sheet max-w-sheet space-y-5">
        <h2 id="k-controls" className="display text-2xl">Switch, segmented, disclosure</h2>
        <Switch label="Announce new transcript lines" hint="Read new lines aloud, at most every 30 seconds." checked={on} onChange={setOn} />
        <Segmented legend="Colour mode" value={seg} onChange={setSeg} options={[{ value: "a", label: "System" }, { value: "b", label: "Light" }, { value: "c", label: "Dark" }]} />
        <Segmented legend="Text size" value={size} onChange={setSize} options={[{ value: "s", label: "Small" }, { value: "m", label: "Medium" }, { value: "l", label: "Large" }]} />
        <Disclosure summary="Why this?">
          <p>It is the course you added most recently.</p>
        </Disclosure>
      </section>

      <section aria-labelledby="k-status" className="space-y-3">
        <h2 id="k-status" className="display text-2xl">Status text</h2>
        <p className="text-recording">Recording text</p>
        <p className="text-success">Success text</p>
        <p className="text-warning">Warning text</p>
        <p className="text-danger">Danger text</p>
      </section>

      <section aria-labelledby="k-legacy" className="space-y-3">
        <h2 id="k-legacy" className="display text-2xl">Still in use</h2>
        <p className="help max-w-prose">
          Card, panel and tag stay until slices 3, 4 and 6 move each screen to the primitives above. The quiet button and the uppercase eyebrow are used only by the
          consent surfaces, whose presentation is frozen (gate G1).
        </p>
        <div className="flex flex-wrap items-center gap-3">
          <button type="button" className="btn-quiet">Quiet (consent only)</button>
          <span className="eyebrow">Eyebrow (consent only)</span>
          <span className="tag tag-warning">Draft</span>
          <span className="tag tag-neutral">Demo summary</span>
        </div>
        <div className="card max-w-sheet">
          <p className="font-semibold">Card</p>
          <p className="text-muted">One or two sentences.</p>
        </div>
        <div className="panel panel-notice max-w-sheet">
          <p className="font-semibold">Notice panel</p>
          <p>Something needs a decision, nothing is harmed.</p>
        </div>
      </section>
    </div>
  );
}
