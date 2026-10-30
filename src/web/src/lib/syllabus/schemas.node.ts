// Node-only loader (tests). Do not import from browser code: it uses node:fs. Reads src/shared/contracts directly.
import fs from "node:fs";
import path from "node:path";
import { contractsDir } from "@/lib/lecture/schemas.node";
import { createSyllabusValidators, type SchemaDoc, type SyllabusValidators } from "./validators";

export function loadSyllabusSchemas(dir = contractsDir()): SchemaDoc[] {
  const schemaDir = path.join(dir, "syllabus.v1");
  return fs
    .readdirSync(schemaDir)
    .filter((f) => f.endsWith(".schema.json"))
    .sort()
    .map((f) => JSON.parse(fs.readFileSync(path.join(schemaDir, f), "utf8")) as SchemaDoc);
}

export function loadSyllabusValidators(opts: { unicodeRegExp: boolean } = { unicodeRegExp: true }, dir = contractsDir()): SyllabusValidators {
  return createSyllabusValidators(loadSyllabusSchemas(dir), opts);
}
