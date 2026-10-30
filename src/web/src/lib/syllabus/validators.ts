import Ajv2020, { type ValidateFunction } from "ajv/dist/2020";

/**
 * Ajv 2020 validators for every syllabus.v1 schema (single source: src/shared/contracts/syllabus.v1, loaded from disk
 * by schemas.node.ts in tests; nothing is copied into src/web).
 *
 * `unicodeRegExp` selects the regex engine mode, and is the point of R3-07 (docs/API_CONTRACTS_P2.md sections 2.2, 12):
 *  - true (ajv's default, the `u` flag): a CODE-POINT engine. The schema patterns write their astral exclusions as UTF-16
 *    surrogate-pair lookaheads, which match nothing here, so tag characters, the U+E0100 supplement and plane 15-16
 *    private use VALIDATE. This is the mode the contract requires the web suite to run.
 *  - false: a UTF-16 engine like .NET's, where the surrogate-pair exclusions work.
 * Either way, schema validity is NOT TextPolicy validity: the server TextPolicy is authoritative and the web client never
 * treats a schema pass as a text verdict.
 *
 * `strictTypes: false` and `validateFormats: false`: same reasons as lib/lecture/validators.ts (minLength beside a $ref;
 * the only format is date-time and every timestamp also has an RFC 3339 pattern).
 */
export type SchemaDoc = { $id: string } & Record<string, unknown>;

export interface SyllabusValidators {
  byName(name: string): ValidateFunction;
  names(): string[];
  /** Schema paths and messages only; never the offending values. */
  explain(name: string): string;
}

const ID_RE = /\/syllabus\.v1\/([^/]+)\.schema\.json$/;

export function createSyllabusValidators(schemas: SchemaDoc[], opts: { unicodeRegExp: boolean }): SyllabusValidators {
  const ajv = new Ajv2020({ strict: true, strictTypes: false, allErrors: true, validateFormats: false, unicodeRegExp: opts.unicodeRegExp });
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
      if (!id) throw new Error(`Unknown syllabus.v1 schema: ${name}`);
      fn = ajv.getSchema(id);
      if (!fn) throw new Error(`Schema failed to compile: ${name}`);
      compiled.set(name, fn);
    }
    return fn;
  };
  return {
    byName,
    names: () => [...idByName.keys()],
    explain: (name) => (byName(name).errors ?? []).map((e) => `${e.instancePath || "/"} ${e.message ?? "invalid"}`).join("; "),
  };
}
