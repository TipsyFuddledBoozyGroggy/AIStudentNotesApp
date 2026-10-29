// Node-only loader (tests, scripts). Do not import from browser code or from index.ts:
// it uses node:fs. Reads src/shared/contracts directly; nothing is copied into src/web.
import fs from "node:fs";
import path from "node:path";
import { createLectureValidators, type LectureValidators, type SchemaDoc } from "./validators";

export function contractsDir(): string {
  return process.env.TETHER_CONTRACTS_DIR ?? path.resolve(process.cwd(), "../shared/contracts");
}

export function loadSchemasFromDisk(dir = contractsDir()): SchemaDoc[] {
  const schemaDir = path.join(dir, "lecture.v1");
  return fs
    .readdirSync(schemaDir)
    .filter((f) => f.endsWith(".schema.json"))
    .sort()
    .map((f) => JSON.parse(fs.readFileSync(path.join(schemaDir, f), "utf8")) as SchemaDoc);
}

export function loadValidatorsFromDisk(dir = contractsDir()): LectureValidators {
  return createLectureValidators(loadSchemasFromDisk(dir));
}

/**
 * Schema names (file stems) of the OTHER contract families next to lecture.v1 (syllabus.v1, calendar.v1, ...). The shared
 * fixtures folder holds fixtures for all of them; the web validator only registers lecture.v1.
 */
export function otherContractSchemaNames(dir = contractsDir()): Set<string> {
  const names = new Set<string>();
  for (const ent of fs.readdirSync(dir, { withFileTypes: true })) {
    if (!ent.isDirectory() || ent.name === "lecture.v1" || ent.name === "fixtures") continue;
    for (const f of fs.readdirSync(path.join(dir, ent.name))) if (f.endsWith(".schema.json")) names.add(f.slice(0, -".schema.json".length));
  }
  return names;
}

export interface FixtureFile {
  file: string;
  schema: string;
  json: unknown;
}

export function loadFixtures(kind: "valid" | "invalid", dir = contractsDir()): FixtureFile[] {
  const d = path.join(dir, "fixtures", kind);
  return fs
    .readdirSync(d)
    .filter((f) => f.endsWith(".json"))
    .sort()
    .map((file) => ({
      file,
      schema: file.slice(0, file.indexOf("--")),
      json: JSON.parse(fs.readFileSync(path.join(d, file), "utf8")) as unknown,
    }));
}
