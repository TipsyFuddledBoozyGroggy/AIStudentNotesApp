// Node-only loader (tests). Do not import from browser code: it uses node:fs. Reads src/shared/contracts/docs.export.v1
// directly (schemas and its own fixtures folder); nothing is copied into src/web.
import Ajv2020, { type ValidateFunction } from "ajv/dist/2020";
import fs from "node:fs";
import path from "node:path";
import { contractsDir, type FixtureFile } from "@/lib/lecture/schemas.node";

const folder = (dir = contractsDir()) => path.join(dir, "docs.export.v1");
const ID_RE = /\/docs\.export\.v1\/([^/]+)\.schema\.json$/;

export function loadDocsSchemas(dir = contractsDir()): ({ $id: string } & Record<string, unknown>)[] {
  return fs
    .readdirSync(folder(dir))
    .filter((f) => f.endsWith(".schema.json"))
    .sort()
    .map((f) => JSON.parse(fs.readFileSync(path.join(folder(dir), f), "utf8")) as { $id: string } & Record<string, unknown>);
}

export function loadDocsFixtures(kind: "valid" | "invalid", dir = contractsDir()): FixtureFile[] {
  const d = path.join(folder(dir), "fixtures", kind);
  return fs
    .readdirSync(d)
    .filter((f) => f.endsWith(".json"))
    .sort()
    .map((file) => ({ file, schema: file.slice(0, file.indexOf("--")), json: JSON.parse(fs.readFileSync(path.join(d, file), "utf8")) as unknown }));
}

/** ajv 2020 over every docs.export.v1 schema (strict, formats off: every timestamp also carries an RFC 3339 pattern). */
export function loadDocsValidators(dir = contractsDir()): { byName(name: string): ValidateFunction; names(): string[] } {
  const ajv = new Ajv2020({ strict: true, strictTypes: false, allErrors: true, validateFormats: false });
  const ids = new Map<string, string>();
  for (const s of loadDocsSchemas(dir)) {
    const m = ID_RE.exec(s.$id);
    if (!m) throw new Error(`Unexpected schema $id: ${s.$id}`);
    ajv.addSchema(s);
    ids.set(m[1]!, s.$id);
  }
  return {
    byName(name) {
      const id = ids.get(name);
      const fn = id ? ajv.getSchema(id) : undefined;
      if (!fn) throw new Error(`Unknown docs.export.v1 schema: ${name}`);
      return fn;
    },
    names: () => [...ids.keys()],
  };
}
