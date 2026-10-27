import { describe, expect, it } from "vitest";
import { loadFixtures, loadSchemasFromDisk, loadValidatorsFromDisk } from "./schemas.node";

const validators = loadValidatorsFromDisk();
const valid = loadFixtures("valid");
const invalid = loadFixtures("invalid");

describe("lecture.v1 contract (same fixtures as the .NET tests)", () => {
  it("registers every schema file", () => {
    expect(validators.names().length).toBe(loadSchemasFromDisk().length);
    expect(validators.names()).toEqual(expect.arrayContaining(["common", "hub-event", "transcript.final", "join-response"]));
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
