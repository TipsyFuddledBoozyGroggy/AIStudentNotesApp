import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { axeViolations } from "@/test/axe";
import { server } from "@/mocks/server";
import * as db from "@/mocks/db";
import { AuthProvider, isSafeReturnPath, rememberReturnPath, stampReturnPath, takeReturnPath } from "./AuthProvider";
import { OUTCOME_COPY, SignInView } from "./SignInView";

const { replace, router } = vi.hoisted(() => {
  const replace = vi.fn();
  return { replace, router: { replace, push: vi.fn() } };
});
vi.mock("next/navigation", () => ({ usePathname: () => "/signin", useRouter: () => router }));

const P = "*/api/v1";
const GOOGLE_URL =
  "https://accounts.google.com/o/oauth2/v2/auth?client_id=1234567890-fake.apps.googleusercontent.com&redirect_uri=https%3A%2F%2Fapp.tether.invalid%2Fapi%2Fv1%2Fauth%2Fgoogle%2Fcallback&response_type=code&scope=openid%20email%20profile&state=S&nonce=N&code_challenge=C&code_challenge_method=S256&prompt=select_account";

function methods(google: boolean, devLogin: boolean) {
  server.use(http.get(`${P}/auth/methods`, () => HttpResponse.json({ google, devLogin })));
}

function renderView(navigate = vi.fn()) {
  const utils = render(
    <AuthProvider>
      <SignInView navigate={navigate} />
    </AuthProvider>,
  );
  return { ...utils, navigate };
}

beforeEach(() => {
  replace.mockReset();
  window.history.replaceState({}, "", "/signin");
  window.sessionStorage.clear();
});

describe("SignInView (auth.v1, ADR-022; redesign-round2 slice 4 Auth template)", () => {
  it("in local mock mode shows only the dev form, inside the one sheet below the h1; no card; no axe violations", async () => {
    const { container } = renderView();
    const form = await screen.findByRole("form", { name: "Sign in" });
    const sheet = container.querySelector(".sheet.sheet-page");
    expect(sheet).toContainElement(form);
    expect(sheet!.contains(screen.getByRole("heading", { level: 1, name: "Sign in" }))).toBe(false);
    expect(form).toContainElement(screen.getByLabelText("Email"));
    expect(form).toContainElement(screen.getByRole("button", { name: "Sign in" }));
    expect(screen.queryByRole("button", { name: "Continue with Google" })).toBeNull();
    expect(container.querySelectorAll(".card, .eyebrow")).toHaveLength(0);
    expect(await axeViolations(container)).toEqual([]);
  });

  it("with Google on and dev login off (Azure) shows only the Google button; pressing it starts sign-in and leaves for Google", async () => {
    methods(true, false);
    let body: unknown = null;
    server.use(
      http.post(`${P}/auth/google/signin/start`, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ purpose: "signin", authorizationUrl: GOOGLE_URL, expiresAt: "2026-11-27T12:10:00Z" });
      }),
    );
    const { container, navigate } = renderView();
    const button = await screen.findByRole("button", { name: "Continue with Google" });
    expect(screen.queryByLabelText("Email")).toBeNull();
    expect(container.querySelector("img[src='/google-g.svg'][alt='']")).not.toBeNull(); // the G mark is decorative
    expect(await axeViolations(container)).toEqual([]);
    await userEvent.click(button);
    await waitFor(() => expect(navigate).toHaveBeenCalledWith(GOOGLE_URL));
    expect(body).toEqual({ landing: "home" }); // the client can name nothing but the landing key
  });

  it("a page restored by Back from the back-forward cache gets a usable Google button again", async () => {
    methods(true, false);
    server.use(
      http.post(`${P}/auth/google/signin/start`, () =>
        HttpResponse.json({ purpose: "signin", authorizationUrl: GOOGLE_URL, expiresAt: "2026-11-27T12:10:00Z" }),
      ),
    );
    const { navigate } = renderView();
    await userEvent.click(await screen.findByRole("button", { name: "Continue with Google" }));
    await waitFor(() => expect(navigate).toHaveBeenCalled());
    expect(screen.getByRole("button", { name: "Opening Google..." })).toBeDisabled();
    const notPersisted = new Event("pageshow");
    Object.defineProperty(notPersisted, "persisted", { value: false });
    window.dispatchEvent(notPersisted);
    expect(screen.getByRole("button", { name: "Opening Google..." })).toBeDisabled(); // an ordinary load changes nothing
    const restored = new Event("pageshow");
    Object.defineProperty(restored, "persisted", { value: true });
    window.dispatchEvent(restored);
    expect(await screen.findByRole("button", { name: "Continue with Google" })).toBeEnabled();
  });

  it.each([
    [true, false],
    [false, true],
    [true, true],
  ])("S1 item 14: no password field, no sign-up form and no create-account control (google %s, devLogin %s)", async (google, devLogin) => {
    methods(google, devLogin);
    const { container } = renderView();
    await screen.findByRole(google ? "button" : "form", { name: google ? "Continue with Google" : "Sign in" });
    expect(container.querySelectorAll("input[type='password'], input[autocomplete='new-password'], input[autocomplete='current-password']")).toHaveLength(0);
    expect(screen.queryByRole("button", { name: /sign up|create (an )?account|register/i })).toBeNull();
    expect(screen.queryByRole("link", { name: /sign up|create (an )?account|register/i })).toBeNull();
    expect(container.textContent).not.toMatch(/sign up|create (an )?account|password/i);
  });

  it("refuses to leave for anything but Google's authorization address", async () => {
    methods(true, false);
    server.use(
      http.post(`${P}/auth/google/signin/start`, () =>
        HttpResponse.json({ purpose: "signin", authorizationUrl: "https://evil.example.invalid/o/oauth2/v2/auth?x=1", expiresAt: "2026-11-27T12:10:00Z" }),
      ),
    );
    const { navigate } = renderView();
    await userEvent.click(await screen.findByRole("button", { name: "Continue with Google" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(OUTCOME_COPY.provider_unavailable.body);
    expect(navigate).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Continue with Google" })).toBeEnabled();
  });

  it("a 429 on start shows the calm many-tries line", async () => {
    methods(true, false);
    server.use(
      http.post(`${P}/auth/google/signin/start`, () =>
        HttpResponse.json({ code: "rate_limited", status: 429, title: "Too many requests. Please slow down." }, { status: 429 }),
      ),
    );
    renderView();
    await userEvent.click(await screen.findByRole("button", { name: "Continue with Google" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(OUTCOME_COPY.rate_limited.body);
  });

  it("with both on (local with a Google client) shows the Google button and the developer form under its own heading", async () => {
    methods(true, true);
    const { container } = renderView();
    expect(await screen.findByRole("button", { name: "Continue with Google" })).toBeInTheDocument();
    expect(screen.getByRole("form", { name: "Developer sign-in (this computer only)" })).toContainElement(screen.getByLabelText("Email"));
    expect(await axeViolations(container)).toEqual([]);
  });

  it("when the methods cannot be read it says so plainly and offers Try again", async () => {
    server.use(http.get(`${P}/auth/methods`, () => HttpResponse.error()));
    renderView();
    expect(await screen.findByRole("alert")).toHaveTextContent("We can't reach Tether. Check your connection, then try again.");
    expect(screen.queryByLabelText("Email")).toBeNull(); // never a fallback dev form
    methods(false, true);
    await userEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByLabelText("Email")).toBeInTheDocument();
  });

  it.each(Object.keys(OUTCOME_COPY) as (keyof typeof OUTCOME_COPY)[])("outcome %s: one message, focus on its heading, query removed", async (outcome) => {
    methods(true, false);
    window.history.replaceState({}, "", `/signin?purpose=signin&outcome=${outcome}`);
    const { container } = renderView();
    const heading = await screen.findByRole("heading", { level: 2, name: OUTCOME_COPY[outcome].title });
    await waitFor(() => expect(heading).toHaveFocus());
    expect(heading).toHaveAccessibleDescription(OUTCOME_COPY[outcome].body);
    expect(replace).toHaveBeenCalledWith("/signin");
    expect(container.querySelectorAll("[role='alert'], [role='status'], [aria-live]")).toHaveLength(0); // announced once: by focus, not also by a live region
    expect(await axeViolations(container)).toEqual([]);
  });

  it("an unknown outcome is shown as the generic message and its value is never echoed", async () => {
    methods(true, false);
    window.history.replaceState({}, "", "/signin?purpose=signin&outcome=%3Cb%3Eyou%20were%20hacked%3C%2Fb%3E");
    renderView();
    expect(await screen.findByRole("heading", { level: 2, name: OUTCOME_COPY.provider_unavailable.title })).toBeInTheDocument();
    expect(document.body.textContent).not.toContain("hacked");
  });

  it("signed_in: refreshes the session and returns to the remembered page, once", async () => {
    methods(true, false);
    rememberReturnPath("/courses/c1");
    stampReturnPath();
    db.signIn("invited@example.invalid");
    window.history.replaceState({}, "", "/signin?purpose=signin&outcome=signed_in");
    renderView();
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/courses/c1"));
    expect(window.sessionStorage.getItem("tether.signin.return")).toBeNull();
  });

  it("signed_in without a remembered page goes home", async () => {
    methods(true, false);
    db.signIn("invited@example.invalid");
    window.history.replaceState({}, "", "/signin?purpose=signin&outcome=signed_in");
    renderView();
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/"));
  });
});

describe("return path (auth.v1 section 4)", () => {
  it.each([
    ["/", true],
    ["/courses/c1", true],
    ["/lectures/0193-abc?tab=notes", true],
    ["//evil.example.invalid", false],
    ["/\\evil.example.invalid", false],
    ["https://evil.example.invalid/", false],
    ["/%2F%2Fevil.example.invalid", false],
    ["/%5Cevil.example.invalid", false],
    ["/api/v1/me", false],
    ["/hubs/lecture", false],
    ["/ingest", false],
    ["/signin", false],
    ["/a\u0000b", false],
    ["/%E0%A4%A", false],
    ["/" + "a".repeat(200), false],
    ["courses", false],
  ])("%s -> %s", (path, ok) => {
    expect(isSafeReturnPath(path)).toBe(ok);
  });

  it("a stamped path older than 10 minutes is ignored, and reading clears it", () => {
    rememberReturnPath("/courses/c1");
    stampReturnPath(1_000);
    expect(takeReturnPath(1_000 + 10 * 60 * 1000 + 1)).toBeNull();
    rememberReturnPath("/courses/c1");
    stampReturnPath(1_000);
    expect(takeReturnPath(2_000)).toBe("/courses/c1");
    expect(takeReturnPath(2_000)).toBeNull();
  });

  it("an unsafe path is never remembered", () => {
    rememberReturnPath("//evil.example.invalid");
    stampReturnPath();
    expect(takeReturnPath()).toBeNull();
  });
});
