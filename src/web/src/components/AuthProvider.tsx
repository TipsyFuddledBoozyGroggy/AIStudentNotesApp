"use client";

import { useRouter } from "next/navigation";
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { api, ApiError, type UserProfile } from "@/lib/api";

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
      setUser(await api.getMe());
      setStatus("authenticated");
    } catch (e) {
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

/** For protected pages: redirects to /signin when the session is missing (M1-S1 AC1). */
export function useRequireUser(): AuthValue {
  const auth = useAuth();
  const router = useRouter();
  useEffect(() => {
    if (auth.status === "unauthenticated") router.replace("/signin");
  }, [auth.status, router]);
  return auth;
}
