import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { readFileSync } from "node:fs";
import path from "node:path";
import type { Course } from "@/lib/api";
import { FORBIDDEN, sentencesOver } from "@/lib/copyLint";
import { CourseDetailView } from "./CourseDetailView";
import { CourseForm } from "./CourseForm";
import { DashboardView } from "./DashboardView";

// Copy lint for the slice 4 screens (redesign-round2 slice 4; docs/ux/language-guide.md sections 3 and 9). These screens
// keep their strings inline rather than in a registry, so the lint reads (1) every text node the views render in their
// states and (2) the string literals and JSX text of the two pages that need a signed-in shell to render (All courses,
// sign-in). Same rules as the lecture and syllabus registries.

function textNodes(root: Node): string[] {
  const out: string[] = [];
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  for (let n = walker.nextNode(); n; n = walker.nextNode()) {
    const t = n.textContent?.trim();
    if (t) out.push(t);
  }
  return out;
}

function sourceStrings(file: string): string[] {
  const s = readFileSync(path.join(__dirname, file), "utf8");
  const literals = [...s.matchAll(/"([A-Z][^"]{3,})"/g)].map((m) => m[1]);
  const jsx = [...s.matchAll(/>\s*([A-Z][^<>{}]{3,}?)\s*</g)].map((m) => m[1].trim());
  return [...literals, ...jsx].filter((x) => /\s/.test(x)); // sentences and labels, not class names or ids
}

const c = (i: number, extra: Partial<Course> = {}): Course => ({ id: `c${i}`, name: `Course ${i}`, createdAt: `2026-01-0${i}T00:00:00Z`, updatedAt: `2026-01-0${i}T00:00:00Z`, ...extra });
const noop = () => undefined;

async function rendered(): Promise<string[]> {
  const all: string[] = [];
  const views = [
    <DashboardView key="empty" state={{ status: "ready", courses: [] }} onRetry={noop} />,
    <DashboardView key="ready" state={{ status: "ready", courses: [c(1, { lastSessionAt: "2026-10-27T15:00:00Z" }), ...[2, 3, 4, 5, 6, 7].map((i) => c(i))] }} displayName="Alice" onRetry={noop} />,
    <DashboardView key="loading" state={{ status: "loading" }} onRetry={noop} />,
    <DashboardView key="error" state={{ status: "error", message: "We couldn't load your courses." }} onRetry={noop} />,
    <CourseDetailView key="course" course={{ ...c(1), code: "BIO 101", term: "Fall 2026" }} justAdded />,
  ];
  for (const v of views) {
    const { container, unmount } = render(v);
    all.push(...textNodes(container));
    unmount();
  }
  // The course form with its error summary and the duplicate hint open.
  const { container } = render(<CourseForm onSubmit={() => Promise.reject(new Error("x"))} onCreated={noop} existingNames={["Course 1"]} />);
  await userEvent.click(screen.getByRole("button", { name: "Save course" }));
  await userEvent.click(screen.getByRole("button", { name: "Show options" }));
  await userEvent.type(screen.getByLabelText(/Course name/), "Course 1");
  await userEvent.click(screen.getByRole("button", { name: "Save course" }));
  await screen.findByText(/Your answers are kept/);
  all.push(...textNodes(container));
  return all;
}

describe("slice 4 copy lint (Today, course page, Add a course, All courses, sign-in)", () => {
  let strings: string[] = [];
  beforeAll(async () => {
    strings = [...(await rendered()), ...sourceStrings("AllCoursesPage.tsx"), ...sourceStrings("SignInView.tsx")];
  });

  it("has strings to lint", () => {
    expect(strings.length).toBeGreaterThan(40);
    expect(strings).toContain("No courses yet. Add one when you want to.");
  });

  it.each(FORBIDDEN)("no %s", (_name, re) => {
    expect(strings.filter((s) => re.test(s))).toEqual([]);
  });

  it("no sentence is longer than 30 words; \"No lectures yet\" is gone", () => {
    expect(sentencesOver(strings, 30)).toEqual([]);
    expect(strings.filter((s) => /No lectures yet/.test(s))).toEqual([]);
  });
});
