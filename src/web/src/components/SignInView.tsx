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

  return (
    <form onSubmit={(e) => void submit(e)} noValidate className="card max-w-md space-y-4" aria-labelledby="signin-heading">
      <h1 id="signin-heading" className="text-2xl font-bold">
        Sign in
      </h1>
      <p className="text-slate-700">Development sign-in. No password needed. Real Google sign-in comes later.</p>
      <div>
        <label htmlFor="email" className="font-semibold">
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
          <p id="email-error" role="alert" className="mt-1 font-medium text-red-900">
            {error}
          </p>
        )}
      </div>
      <button type="submit" className="btn-primary" disabled={busy}>
        {busy ? "Signing in..." : "Sign in"}
      </button>
    </form>
  );
}
