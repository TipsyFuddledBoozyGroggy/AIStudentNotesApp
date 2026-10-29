import { describe, expect, it } from "vitest";
import { loadFixtures, loadSchemasFromDisk, loadValidatorsFromDisk, otherContractSchemaNames } from "./schemas.node";

const validators = loadValidatorsFromDisk();
// The shared fixtures folder also holds syllabus.v1 / calendar.v1 fixtures (validated by the .NET contract tests). This
// suite runs only the fixtures of schemas the web validator registers (lecture.v1), and fails on any fixture that belongs
// to no contract family at all, so a mistyped lecture fixture name cannot be skipped silently.
const lectureNames = new Set(validators.names());
const otherNames = otherContractSchemaNames();
const allValid = loadFixtures("valid");
const allInvalid = loadFixtures("invalid");
const valid = allValid.filter((f) => lectureNames.has(f.schema));
const invalid = allInvalid.filter((f) => lectureNames.has(f.schema));

describe("lecture.v1 contract (same fixtures as the .NET tests)", () => {
  it("registers every schema file", () => {
    expect(validators.names().length).toBe(loadSchemasFromDisk().length);
    expect(validators.names()).toEqual(expect.arrayContaining(["common", "hub-event", "transcript.final", "join-response"]));
  });

  it("every fixture belongs to lecture.v1 or to another contract family's schema", () => {
    const orphans = [...allValid, ...allInvalid].filter((f) => !lectureNames.has(f.schema) && !otherNames.has(f.schema)).map((f) => f.file);
    expect(orphans).toEqual([]);
  });

  it("has fixtures to run", () => {
    expect(valid.length).toBeGreaterThan(20);
    expect(invalid.length).toBeGreaterThan(40);
  });

  it.each(valid.map((f) => [f.file, f] as const))("valid fixture passes: %s", (_n, f) => {
    const fn = validators.byName(f.schema);
    expect(fn(f.json), validators.explain(f.schema)).toBe(true);
  });

  it.each(invalid.map((f) => [f.file, f] as const))("invalid fixture is rejected: %s", (_n, f) => {
    expect(validators.byName(f.schema)(f.json)).toBe(false);
  });

  it("hub-event entry point accepts valid events and rejects an unknown type", () => {
    for (const f of valid.filter((x) => x.schema === "hub-event")) expect(validators.isHubEvent(f.json)).toBe(true);
    for (const f of invalid.filter((x) => x.schema === "hub-event")) expect(validators.isHubEvent(f.json)).toBe(false);
  });

  it("explain() never echoes offending values", () => {
    const bad = { ...(valid.find((f) => f.schema === "transcript.final")!.json as object), seq: "SECRET-CANARY" };
    validators.byName("transcript.final")(bad);
    expect(validators.explain("transcript.final")).not.toContain("SECRET-CANARY");
  });
});
