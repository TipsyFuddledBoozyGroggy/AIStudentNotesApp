import Ajv2020, { type ValidateFunction } from "ajv/dist/2020";
import type { HubEvent, JoinRequest, JoinResponse } from "./types";

/**
 * Builds Ajv 2020 validators for every lecture.v1 schema.
 *
 * Schemas are passed in, never imported from a copy: the single source is
 * src/shared/contracts/lecture.v1/. Tests load them from disk with schemas.node.ts
 * (see README.md in this folder). Every file is registered with addSchema so that the
 * relative cross-file $refs resolve against each schema's $id.
 *
 * `strictTypes: false`: the schemas put `minLength` next to a `$ref` to a typed definition (legal in
 * 2020-12; Ajv strictTypes cannot see the type through the $ref). Unknown keywords stay errors (strictSchema).
 *
 * `validateFormats: false`: the only format used is "date-time" and the schema already
 * constrains timestamps with an RFC 3339 `pattern`, so ajv-formats is not needed.
 */
export type SchemaDoc = { $id: string } & Record<string, unknown>;

export interface LectureValidators {
  /** Validate by schema basename, e.g. "transcript.final" or "join-response". */
  byName(name: string): ValidateFunction;
  names(): string[];
  isHubEvent(value: unknown): value is HubEvent;
  isJoinResponse(value: unknown): value is JoinResponse;
  isJoinRequest(value: unknown): value is JoinRequest;
  /** Human-safe error summary; contains schema paths only, never the offending values. */
  explain(name: string): string;
}

const ID_RE = /\/lecture\.v1\/([^/]+)\.schema\.json$/;

/**
 * `unicodeRegExp` (Ajv's default is true) selects the regex engine mode: true compiles every `pattern` with the `u` flag
 * (code points), as the syllabus R3-07 test does; notesContract.test.ts passes it explicitly for the notes schemas.
 */
export function createLectureValidators(schemas: SchemaDoc[], opts: { unicodeRegExp?: boolean } = {}): LectureValidators {
  const ajv = new Ajv2020({ strict: true, strictTypes: false, allErrors: true, validateFormats: false, unicodeRegExp: opts.unicodeRegExp ?? true });
  const idByName = new Map<string, string>();
  for (const s of schemas) {
    const m = ID_RE.exec(s.$id);
    if (!m) throw new Error(`Unexpected schema $id: ${s.$id}`);
    ajv.addSchema(s);
    idByName.set(m[1], s.$id);
  }
  const compiled = new Map<string, ValidateFunction>();
  const byName = (name: string): ValidateFunction => {
    let fn = compiled.get(name);
    if (!fn) {
      const id = idByName.get(name);
      if (!id) throw new Error(`Unknown lecture.v1 schema: ${name}`);
      fn = ajv.getSchema(id);
      if (!fn) throw new Error(`Schema failed to compile: ${name}`);
      compiled.set(name, fn);
    }
    return fn;
  };
  return {
    byName,
    names: () => [...idByName.keys()],
    isHubEvent: (v): v is HubEvent => byName("hub-event")(v) === true,
    isJoinResponse: (v): v is JoinResponse => byName("join-response")(v) === true,
    isJoinRequest: (v): v is JoinRequest => byName("join-request")(v) === true,
    explain: (name) =>
      (byName(name).errors ?? []).map((e) => `${e.instancePath || "/"} ${e.message ?? "invalid"}`).join("; "),
  };
}
