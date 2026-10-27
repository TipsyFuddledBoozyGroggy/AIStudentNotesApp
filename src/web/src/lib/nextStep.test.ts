import type { Course } from "@/lib/api";
import { chooseNextStep, sortByRecentUse } from "./nextStep";

const c = (id: string, createdAt: string, lastSessionAt?: string): Course => ({
  id,
  name: `Course ${id}`,
  createdAt,
  updatedAt: createdAt,
  lastSessionAt,
});

describe("chooseNextStep", () => {
  it("asks to add a first course when there are none", () => {
    expect(chooseNextStep([])).toEqual({ kind: "add-first-course" });
  });

  it("picks the most recently used course", () => {
    const step = chooseNextStep([
      c("a", "2026-01-01T00:00:00Z", "2026-03-01T00:00:00Z"),
      c("b", "2026-01-02T00:00:00Z", "2026-04-01T00:00:00Z"),
      c("c", "2026-05-01T00:00:00Z"),
    ]);
    expect(step.kind === "open-course" && step.course.id).toBe("c");
  });

  it("is deterministic regardless of input order and on ties", () => {
    const list = [c("b", "2026-01-01T00:00:00Z"), c("a", "2026-01-01T00:00:00Z")];
    expect(sortByRecentUse(list).map((x) => x.id)).toEqual(["a", "b"]);
    expect(sortByRecentUse([...list].reverse()).map((x) => x.id)).toEqual(["a", "b"]);
  });
});
