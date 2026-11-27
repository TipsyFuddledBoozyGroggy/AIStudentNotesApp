"use client";

import { useRouter } from "next/navigation";
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { api, ApiError, type UserProfile } from "@/lib/api";
import { clearAllDrafts, setDraftOwner } from "./syllabus/storage";

type Status = "loading" | "authenticated" | "unauthenticated" | "error";

interface AuthValue {
  status: Status;
  user: UserProfile | null;
  refresh: () => Promise<void>;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const router = useRouter();
  const [status, setStatus] = useState<Status>("loading");
  const [user, setUser] = useState<UserProfile | null>(null);

  const refresh = useCallback(async () => {
    try {
      const me = await api.getMe();
      setDraftOwner(me.id); // SB-06: syllabus drafts are keyed by this id; other users' and unscoped ones are removed
      setUser(me);
      setStatus("authenticated");
    } catch (e) {
      if (e instanceof ApiError && e.isUnauthenticated) setDraftOwner(null);
      setUser(null);
      setStatus(e instanceof ApiError && e.isUnauthenticated ? "unauthenticated" : "error");
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const signOut = useCallback(async () => {
    try {
      await api.logout();
    } finally {
      clearAllDrafts(); // SB-06: pasted syllabus text and item edit drafts never outlive the session on this device
      setUser(null);
      setStatus("unauthenticated");
      router.replace("/signin");
    }
  }, [router]);

  const value = useMemo(() => ({ status, user, refresh, signOut }), [status, user, refresh, signOut]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside AuthProvider");
  return ctx;
}

/** For protected pages: redirects to /signin when the session is missing (M1-S1 AC1), remembering where the student was. */
export function useRequireUser(): AuthValue {
  const auth = useAuth();
  const router = useRouter();
  useEffect(() => {
    if (auth.status !== "unauthenticated") return;
    rememberReturnPath(window.location.pathname + window.location.search);
    router.replace("/signin");
  }, [auth.status, router]);
  return auth;
}

// ---------------------------------------------------------------------------------------------------------------------
// Return path after a Google sign-in (auth.v1 section 4, ADR-022 R10). The server accepts no return URL: the path stays in this tab's
// sessionStorage and is used only if it is recent and passes a structural same-origin check. Anything else lands on home.
// ---------------------------------------------------------------------------------------------------------------------

const NEXT_KEY = "tether.signin.next";
const RETURN_KEY = "tether.signin.return";
const RETURN_MAX_AGE_MS = 10 * 60 * 1000;
const RESERVED = /^\/(api|hubs|ingest)(\/|\?|#|$)/i;

function structurallySafe(p: string): boolean {
  return (
    p.length <= 200 &&
    p.startsWith("/") &&
    !p.includes("//") &&
    !p.includes("\\") &&
    !/[\u0000-\u001f\u007f]/.test(p) &&
    !RESERVED.test(p)
  );
}

/** True for a same-origin app path: one leading slash, no //, no backslash, no control characters, also after one decode, at most 200 characters, not /api, /hubs or /ingest. */
export function isSafeReturnPath(p: unknown): p is string {
  if (typeof p !== "string" || !structurallySafe(p)) return false;
  let decoded: string;
  try {
    decoded = decodeURIComponent(p);
  } catch {
    return false;
  }
  return structurallySafe(decoded) && !/^\/signin(\/|\?|#|$)/.test(p);
}

function storage(): Storage | null {
  try {
    return typeof window === "undefined" ? null : window.sessionStorage;
  } catch {
    return null;
  }
}

/** Called when a protected page sends the student to /signin. */
export function rememberReturnPath(path: string): void {
  const s = storage();
  if (!s) return;
  if (isSafeReturnPath(path)) s.setItem(NEXT_KEY, path);
  else s.removeItem(NEXT_KEY);
}

/** Called just before the browser leaves for Google: dates the remembered path so a stale one is ignored on return. */
export function stampReturnPath(now: number = Date.now()): void {
  const s = storage();
  if (!s) return;
  const path = s.getItem(NEXT_KEY);
  if (path && isSafeReturnPath(path)) s.setItem(RETURN_KEY, JSON.stringify({ path, at: now }));
  else s.removeItem(RETURN_KEY);
}

/** Read once after sign-in: the checked path, or null. Always clears both keys. */
export function takeReturnPath(now: number = Date.now()): string | null {
  const s = storage();
  if (!s) return null;
  const raw = s.getItem(RETURN_KEY);
  s.removeItem(RETURN_KEY);
  s.removeItem(NEXT_KEY);
  if (!raw) return null;
  try {
    const v = JSON.parse(raw) as { path?: unknown; at?: unknown };
    if (typeof v.at !== "number" || now - v.at < 0 || now - v.at > RETURN_MAX_AGE_MS) return null;
    return isSafeReturnPath(v.path) ? v.path : null;
  } catch {
    return null;
  }
}
