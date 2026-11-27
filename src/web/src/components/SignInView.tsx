"use client";

import { useRouter } from "next/navigation";
import { useCallback, useEffect, useId, useRef, useState, type FormEvent } from "react";
import { api, ApiError, type AuthMethods } from "@/lib/api";
import { takeReturnPath, stampReturnPath, useAuth } from "./AuthProvider";

/** auth.v1 callback outcomes (docs/API_CONTRACTS_AUTH.md section 4). Anything else is treated as provider_unavailable. */
export type SignInOutcome =
  | "signed_in"
  | "access_denied"
  | "not_invited"
  | "school_account"
  | "account_closed"
  | "state_invalid"
  | "provider_unavailable"
  | "rate_limited";

const GOOGLE_AUTHORIZE = "https://accounts.google.com/o/oauth2/v2/auth?";

/**
 * DRAFT copy (go-live stories S1 items 4 to 6; UX review pending). Calm, one idea per sentence, no blame, says what did and did not happen.
 * Keyed by outcome; the query value itself is never shown.
 */
export const OUTCOME_COPY: Record<Exclude<SignInOutcome, "signed_in">, { title: string; body: string }> = {
  access_denied: { title: "Sign-in cancelled", body: "You cancelled. Nothing was changed. You can try again whenever you like." },
  not_invited: {
    title: "Not on the invite list",
    body: "This site is invite-only for now. Your Google account is not on the list. Nothing was saved. You can try a different Google account.",
  },
  school_account: {
    title: "Personal accounts only",
    body: "This looks like a school or work Google account. For now Tether works with personal Google accounts only. Nothing was saved.",
  },
  account_closed: { title: "Account closed", body: "Your account is closed. Tether is finishing the delete. Nothing else was changed." },
  state_invalid: { title: "Sign-in did not work", body: "Sign-in did not work this time. Nothing was changed. Try again in a moment." },
  provider_unavailable: { title: "Sign-in did not work", body: "Sign-in did not work this time. Nothing was changed. Try again in a moment." },
  rate_limited: { title: "Too many tries", body: "There were many sign-in tries in a short time. Wait a minute, then try again." },
};

const KNOWN = new Set<string>(["signed_in", ...Object.keys(OUTCOME_COPY)]);

/** Reads ?outcome= from the URL once. An unknown value becomes provider_unavailable (closed set; never echoed). */
function readOutcome(): SignInOutcome | null {
  if (typeof window === "undefined") return null;
  const raw = new URLSearchParams(window.location.search).get("outcome");
  if (raw === null) return null;
  return (KNOWN.has(raw) ? raw : "provider_unavailable") as SignInOutcome;
}

type Methods = { status: "loading" } | { status: "ready"; methods: AuthMethods } | { status: "error" };

/**
 * Sign-in page (auth.v1, ADR-022). The server says which controls exist (GET /auth/methods): the Google button when Google sign-in is
 * configured, the email form only where dev login is enabled (local Development). The outcome of a Google return arrives as
 * ?purpose=signin&outcome=<code>; its message gets the focus on its heading, so a screen reader hears it once.
 */
export function SignInView({ navigate = (url: string) => window.location.assign(url) }: { navigate?: (url: string) => void } = {}) {
  const router = useRouter();
  // The init effect must run once: a router object whose identity changes between renders must not re-run it.
  const routerRef = useRef(router);
  routerRef.current = router;
  const { status, refresh } = useAuth();
  const [methods, setMethods] = useState<Methods>({ status: "loading" });
  const [outcome, setOutcome] = useState<SignInOutcome | null>(null);
  const [startError, setStartError] = useState<string | null>(null);
  const [googleBusy, setGoogleBusy] = useState(false);
  const messageHeading = useRef<HTMLHeadingElement>(null);
  const messageId = useId();

  const loadMethods = useCallback(async () => {
    setMethods({ status: "loading" });
    try {
      setMethods({ status: "ready", methods: await api.getAuthMethods() });
    } catch {
      setMethods({ status: "error" });
    }
  }, []);

  useEffect(() => {
    void loadMethods();
    const o = readOutcome();
    if (o === null) return;
    setOutcome(o);
    // Keep the message, drop the query, so a reload does not repeat it.
    routerRef.current.replace("/signin");
    if (o === "signed_in") void refresh();
  }, [loadMethods, refresh]);

  useEffect(() => {
    if (status !== "authenticated") return;
    // After a Google return the page goes back where the student was (sessionStorage, same tab, checked); otherwise home.
    routerRef.current.replace(takeReturnPath() ?? "/");
  }, [status]);

  useEffect(() => {
    if (outcome && outcome !== "signed_in") messageHeading.current?.focus();
  }, [outcome]);

  // Back from Google can restore this page from the back-forward cache with the button still busy; make it usable again (O8).
  useEffect(() => {
    const onPageShow = (e: PageTransitionEvent) => {
      if (e.persisted) setGoogleBusy(false);
    };
    window.addEventListener("pageshow", onPageShow);
    return () => window.removeEventListener("pageshow", onPageShow);
  }, []);

  async function startGoogle() {
    setGoogleBusy(true);
    setStartError(null);
    try {
      stampReturnPath();
      const { authorizationUrl } = await api.startGoogleSignIn();
      if (!authorizationUrl.startsWith(GOOGLE_AUTHORIZE)) throw new Error("unexpected sign-in address");
      navigate(authorizationUrl);
    } catch (err) {
      setStartError(
        err instanceof ApiError && err.status === 429
          ? OUTCOME_COPY.rate_limited.body
          : err instanceof ApiError && err.isNetwork
            ? "We can't reach Tether. Check your connection, then try again."
            : OUTCOME_COPY.provider_unavailable.body,
      );
      setGoogleBusy(false);
    }
  }

  const message = outcome && outcome !== "signed_in" ? OUTCOME_COPY[outcome] : null;
  const ready = methods.status === "ready" ? methods.methods : null;

  // Auth template (redesign-round2 2.3 / A14, slice 4): 28rem centred, 96px from the top (main[data-template="auth"] in globals.css). The h1
  // and its sentence sit on the canvas; the sheet holds the sign-in control. Full-bleed on phones with its text on the gutter.
  return (
    <div className="mx-auto max-w-narrow">
      <h1 id="signin-heading" className="display text-3xl">
        Sign in
      </h1>
      <p className="mt-1 text-muted">Tether uses your Google account. Only invited accounts can sign in for now.</p>

      {message && (
        <section aria-labelledby={`${messageId}-title`} className="mt-6">
          <h2 id={`${messageId}-title`} ref={messageHeading} tabIndex={-1} aria-describedby={`${messageId}-body`} className="text-lg font-semibold">
            {message.title}
          </h2>
          <p id={`${messageId}-body`} className="mt-1">
            {message.body}
          </p>
        </section>
      )}

      <div className="sheet sheet-page mt-6 space-y-5" aria-busy={methods.status === "loading" || undefined}>
        {methods.status === "loading" && <p className="text-muted">Getting the sign-in options ready.</p>}
        {methods.status === "error" && (
          <div className="space-y-3">
            <p role="alert">We can&apos;t reach Tether. Check your connection, then try again.</p>
            <button type="button" className="btn-secondary" onClick={() => void loadMethods()}>
              Try again
            </button>
          </div>
        )}
        {ready && !ready.google && !ready.devLogin && <p>Sign-in is not available on this server.</p>}
        {ready?.google && (
          <div>
            <button
              type="button"
              className="btn-primary inline-flex min-h-11 w-full items-center justify-center gap-3 sm:w-auto"
              onClick={() => void startGoogle()}
              disabled={googleBusy}
              aria-busy={googleBusy || undefined}
            >
              <GoogleMark />
              <span>{googleBusy ? "Opening Google..." : "Continue with Google"}</span>
            </button>
            {startError && (
              <p role="alert" className="mt-3 font-semibold text-danger">
                {startError}
              </p>
            )}
          </div>
        )}
        {ready?.devLogin && <DevLoginForm separate={ready.google} />}
      </div>
    </div>
  );
}

/** The Google "G" mark (public/google-g.svg), decorative: the button's text is its accessible name. Brand-guideline compliance is UNVERIFIED (ADR-022). */
function GoogleMark() {
  // A static brand asset in its own colours, so it stays outside the theme tokens; next/image would add nothing for an 18px SVG.
  // eslint-disable-next-line @next/next/no-img-element
  return <img src="/google-g.svg" alt="" aria-hidden="true" width={18} height={18} className="shrink-0" />;
}

/** DEV sign-in only (POST /dev/login), shown only when the server says dev login is enabled (local Development). */
function DevLoginForm({ separate }: { separate: boolean }) {
  const router = useRouter();
  const { refresh } = useAuth();
  const [email, setEmail] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.devLogin(email);
      await refresh();
      router.replace("/");
    } catch (err) {
      setError(
        err instanceof ApiError && err.isNetwork
          ? "We can't reach the server. Try again in a moment."
          : "Enter an email like name@school.edu.",
      );
      setBusy(false);
    }
  }

  return (
    <form onSubmit={(e) => void submit(e)} noValidate className="space-y-5" aria-labelledby={separate ? "devlogin-heading" : "signin-heading"}>
      {separate ? (
        <h2 id="devlogin-heading" className="text-lg font-semibold">
          Developer sign-in (this computer only)
        </h2>
      ) : (
        <p className="text-muted">This is a development sign-in. Enter any school-style email.</p>
      )}
      <div>
        <label htmlFor="email" className="field-label">
          Email
        </label>
        <input
          id="email"
          type="email"
          className="field"
          autoComplete="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? "email-error" : undefined}
        />
        {error && (
          <p id="email-error" role="alert" className="mt-2 flex items-start gap-2 font-semibold text-danger">
            <svg width="18" height="18" viewBox="0 0 18 18" aria-hidden="true" focusable="false" className="mt-1 shrink-0">
              <circle cx="9" cy="9" r="7.5" fill="none" stroke="currentColor" strokeWidth="1.8" />
              <path d="M9 4.8v5M9 12.4v.6" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
            </svg>
            {error}
          </p>
        )}
      </div>
      <button type="submit" className={separate ? "btn-secondary w-full sm:w-auto" : "btn-primary w-full sm:w-auto"} disabled={busy}>
        {busy ? "Signing in..." : "Sign in"}
      </button>
    </form>
  );
}
