"use client";

import { useState } from "react";
import { Disclosure } from "./Disclosure";
import { Segmented } from "./Segmented";
import { Switch } from "./Switch";

/** Every primitive in every state, on one page (development only). */
export function UiKit() {
  const [on, setOn] = useState(true);
  const [seg, setSeg] = useState<"a" | "b" | "c">("a");
  return (
    <div className="space-y-8">
      <h1 className="display text-3xl">UI kit (development only)</h1>
      <section aria-labelledby="k-buttons" className="space-y-3">
        <h2 id="k-buttons" className="display text-xl">Buttons</h2>
        <div className="flex flex-wrap gap-3">
          <button type="button" className="btn-primary">Primary</button>
          <button type="button" className="btn-secondary">Secondary</button>
          <button type="button" className="btn-quiet">Quiet</button>
          <button type="button" className="btn-danger">Delete</button>
          <button type="button" className="btn-primary" aria-disabled="true" aria-describedby="k-why">Unavailable</button>
          <button type="button" className="btn-secondary" aria-busy="true">Saving...</button>
        </div>
        <p id="k-why" className="help">Disabled buttons stay focusable and say why.</p>
      </section>
      <section aria-labelledby="k-fields" className="space-y-3">
        <h2 id="k-fields" className="display text-xl">Fields</h2>
        <div>
          <label htmlFor="k-a" className="label">Course name</label>
          <input id="k-a" className="field" defaultValue="Biology 101" />
          <p className="help mt-1">Helper text sits under the field.</p>
        </div>
        <div>
          <label htmlFor="k-b" className="label">With an error</label>
          <input id="k-b" className="field" aria-invalid="true" aria-describedby="k-b-err" />
          <p id="k-b-err" className="mt-1 font-semibold text-danger">Enter a course name.</p>
        </div>
      </section>
      <section aria-labelledby="k-controls" className="card space-y-4">
        <h2 id="k-controls" className="display text-xl">Switch, segmented, disclosure</h2>
        <Switch label="Announce new transcript lines" hint="Read new lines aloud, at most every 30 seconds." checked={on} onChange={setOn} />
        <Segmented legend="Theme" value={seg} onChange={setSeg} options={[{ value: "a", label: "System" }, { value: "b", label: "Light" }, { value: "c", label: "Dark" }]} />
        <Disclosure summary="Why this?">
          <p>It is the course you added most recently.</p>
        </Disclosure>
      </section>
      <section aria-labelledby="k-status" className="space-y-3">
        <h2 id="k-status" className="display text-xl">Panels and tags</h2>
        <div className="flex flex-wrap gap-2">
          <span className="tag tag-warning">Draft</span>
          <span className="tag tag-neutral">Demo summary</span>
          <span className="tag tag-accent">Your usual</span>
        </div>
        <div className="panel panel-neutral"><p className="font-semibold">Neutral panel</p><p className="text-muted">One or two sentences.</p></div>
        <div className="panel panel-notice"><p className="font-semibold">Notice panel</p><p>Something needs a decision, nothing is harmed.</p></div>
        <div className="panel panel-safety"><p className="font-semibold">Recording is paused</p><p>Your microphone is off.</p></div>
        <p className="text-recording">Recording text</p>
        <p className="text-success">Success text</p>
        <p className="text-warning">Warning text</p>
        <p className="text-danger">Danger text</p>
      </section>
    </div>
  );
}
