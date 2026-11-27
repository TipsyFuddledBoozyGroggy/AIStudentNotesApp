// Fails when a compiled ARM template (the JSON from `bicep build`) has a dependency circle that Azure refuses at
// validation ("Circular dependency detected") but `bicep build` does not report. Azure refused the whole template
// in Run A (2026-11-27) because of one, and nothing short of a real deployment showed it.
//
// Edges counted, conditions ignored (Azure checks the whole graph, whether or not a resource is deployed):
//   1. dependsOn entries;
//   2. reference('x'), resourceInfo('x') and list*('x') calls in a resource's body, x being a symbolic name;
//   3. an `existing` declaration of a resource that the template also deploys (same type, same name expression)
//      depends on that deployed resource. This is the edge Run A tripped over.
//
// Usage: node infra/scripts/template-cycles.mjs <compiled-template.json>
// Exit 0 with "no dependency circle", 1 with the circle printed, 2 on unreadable input.

import { readFileSync } from 'node:fs';

const file = process.argv[2];
if (!file) {
  console.error('usage: node template-cycles.mjs <compiled-template.json>');
  process.exit(2);
}
let template;
try {
  template = JSON.parse(readFileSync(file, 'utf8').replace(/^﻿/, ''));
} catch (e) {
  console.error(`cannot read ${file}: ${e.message}`);
  process.exit(2);
}
if (template.languageVersion !== '2.0' || Array.isArray(template.resources)) {
  console.error('expected a template with symbolic resource names (languageVersion 2.0), which bicep emits for infra/main.bicep');
  process.exit(2);
}

const resources = template.resources;
const names = Object.keys(resources);
const edges = new Map(names.map((n) => [n, new Set()]));

for (const [sym, r] of Object.entries(resources)) {
  for (const d of r.dependsOn ?? []) if (edges.has(d)) edges.get(sym).add(d);
  const { dependsOn, ...body } = r;
  const text = JSON.stringify(body);
  for (const m of text.matchAll(/\b(?:reference|resourceInfo|list\w*)\('([A-Za-z_][A-Za-z0-9_]*)'/g)) {
    if (edges.has(m[1]) && m[1] !== sym) edges.get(sym).add(m[1]);
  }
}
for (const [sym, r] of Object.entries(resources)) {
  if (!r.existing) continue;
  for (const [other, o] of Object.entries(resources)) {
    if (other !== sym && !o.existing && o.type === r.type && JSON.stringify(o.name) === JSON.stringify(r.name)) {
      edges.get(sym).add(other);
    }
  }
}

const state = new Map(); // undefined = not visited, 1 = on the current path, 2 = done
const path = [];
function visit(n) {
  state.set(n, 1);
  path.push(n);
  for (const next of edges.get(n)) {
    if (state.get(next) === 1) return [...path.slice(path.indexOf(next)), next];
    if (state.get(next) === undefined) {
      const found = visit(next);
      if (found) return found;
    }
  }
  path.pop();
  state.set(n, 2);
  return null;
}
for (const n of names) {
  if (state.get(n) !== undefined) continue;
  const circle = visit(n);
  if (circle) {
    console.error(`dependency circle: ${circle.join(' -> ')}`);
    process.exit(1);
  }
}
console.log(`no dependency circle (${names.length} resources)`);
