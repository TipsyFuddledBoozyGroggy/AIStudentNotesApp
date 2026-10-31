"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState, type FormEvent } from "react";
import { api, ApiError } from "@/lib/api";
import { useAuth } from "./AuthProvider";

/**
 * DEV sign-in only (mock backend, POST /dev/login). The real flow is Google OIDC
 * (basic identity scopes only) and will replace this view.
 */
export function SignInView() {
  const router = useRouter();
  const { status, refresh } = useAuth();
  const [email, setEmail] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (status === "authenticated") router.replace("/");
  }, [status, router]);

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

  // Auth template (redesign-round2 2.3 / A14): 28rem centred, 96px from the top (main[data-template="auth"] in globals.css);
  // the "TETHER" eyebrow under the wordmark is gone (review N3).
  return (
    <form onSubmit={(e) => void submit(e)} noValidate className="card mx-auto max-w-narrow space-y-5" aria-labelledby="signin-heading">
      <h1 id="signin-heading" className="display text-3xl">
        Sign in
      </h1>
      <p className="text-muted">This is a development sign-in. Enter any school-style email. Google sign-in arrives later.</p>
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
      <button type="submit" className="btn-primary w-full sm:w-auto" disabled={busy}>
        {busy ? "Signing in..." : "Sign in"}
      </button>
    </form>
  );
}
