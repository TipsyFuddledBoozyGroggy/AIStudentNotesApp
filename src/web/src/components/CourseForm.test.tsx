import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { api, ApiError } from "@/lib/api";
import * as db from "@/mocks/db";
import { server } from "@/mocks/server";
import { axeViolations } from "@/test/axe";
import { CourseForm, DRAFT_KEY } from "./CourseForm";

function setup(over: Partial<React.ComponentProps<typeof CourseForm>> = {}) {
  const onCreated = vi.fn();
  const utils = render(
    <CourseForm onSubmit={(i) => api.createCourse(i)} onCreated={onCreated} draftDelayMs={10} {...over} />,
  );
  return { onCreated, ...utils };
}

describe("CourseForm", () => {
  beforeEach(() => db.signIn("alice@school.edu"));

  it("has no axe violations (collapsed and expanded)", async () => {
    const { container } = setup();
    expect(await axeViolations(container)).toEqual([]);
    await userEvent.click(screen.getByRole("button", { name: "Show options" }));
    expect(screen.getByLabelText("Term")).toBeVisible();
    expect(await axeViolations(container)).toEqual([]);
  });

  it("empty submit shows an error summary that takes focus and links to the field", async () => {
    const { container, onCreated } = setup();
    await userEvent.click(screen.getByRole("button", { name: "Save course" }));
    const summary = await screen.findByRole("alert");
    await waitFor(() => expect(summary).toHaveFocus());
    expect(summary).toHaveTextContent("Enter a course name.");
    const input = screen.getByLabelText(/Course name/);
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input).toHaveAccessibleDescription("Enter a course name.");
    expect(onCreated).not.toHaveBeenCalled();
    expect(await axeViolations(container)).toEqual([]);
  });

  it("rejects whitespace-only names and names over 120 characters", async () => {
    setup();
    const input = screen.getByLabelText(/Course name/);
    await userEvent.type(input, "   ");
    await userEvent.click(screen.getByRole("button", { name: "Save course" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Enter a course name.");
    await userEvent.clear(input);
    await userEvent.click(input);
    await userEvent.paste("x".repeat(121));
    await userEvent.click(screen.getByRole("button", { name: "Save course" }));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("120 characters or fewer"));
  });

  it("autosaves a draft and restores it on remount", async () => {
    const first = setup();
    await userEvent.type(screen.getByLabelText(/Course name/), "Biology 101");
    await waitFor(() => expect(window.localStorage.getItem(DRAFT_KEY)).toContain("Biology 101"));
    first.unmount();

    setup();
    await waitFor(() => expect(screen.getByLabelText(/Course name/)).toHaveValue("Biology 101"));
    expect(screen.getByRole("status")).toHaveTextContent("restored your draft");
  });

  it("saves via the API, clears the draft and reports the created course", async () => {
    const { onCreated } = setup();
    await userEvent.type(screen.getByLabelText(/Course name/), "  Calculus II ");
    await userEvent.click(screen.getByRole("button", { name: "Save course" }));
    await waitFor(() => expect(onCreated).toHaveBeenCalled());
    expect(onCreated.mock.calls[0][0]).toMatchObject({ name: "Calculus II" });
    expect(window.localStorage.getItem(DRAFT_KEY)).toBeNull();
    expect(db.listCourses()).toHaveLength(1);
  });

  it("keeps input and offers retry when the save fails (offline)", async () => {
    server.use(http.post("*/api/v1/courses", () => HttpResponse.error()));
    const { onCreated } = setup();
    await userEvent.type(screen.getByLabelText(/Course name/), "Psych 210");
    await userEvent.click(screen.getByRole("button", { name: "Save course" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("isn't saved yet");
    expect(screen.getByLabelText(/Course name/)).toHaveValue("Psych 210");
    expect(screen.getByRole("button", { name: "Try again" })).toBeEnabled();
    expect(onCreated).not.toHaveBeenCalled();
  });

  it("warns, without blocking, about a duplicate name", async () => {
    setup({ existingNames: ["Biology 101"] });
    await userEvent.type(screen.getByLabelText(/Course name/), "biology 101");
    expect(screen.getByLabelText(/Course name/)).toHaveAccessibleDescription(/already have a course/);
    expect(screen.getByRole("button", { name: "Save course" })).toBeEnabled();
  });
});

describe("api client", () => {
  it("maps 401 and 404 problem responses to ApiError", async () => {
    await expect(api.getMe()).rejects.toMatchObject({ status: 401, code: "unauthenticated" });
    db.signIn("alice@school.edu");
    const err = await api.getCourse("nope").catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).isNotFound).toBe(true);
  });

  it("does not leak one user's courses to another (ADR-002)", async () => {
    db.signIn("alice@school.edu");
    const mine = await api.createCourse({ name: "Mine" });
    db.signIn("bob@school.edu");
    await expect(api.getCourse(mine.id)).rejects.toMatchObject({ status: 404 });
    expect((await api.listCourses()).items).toEqual([]);
  });

  it("reports network failure distinctly", async () => {
    server.use(http.get("*/api/v1/me", () => HttpResponse.error()));
    await expect(api.getMe()).rejects.toMatchObject({ status: 0, code: "network_error" });
  });
});
