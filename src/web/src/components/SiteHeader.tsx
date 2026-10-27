"use client";

import Link from "next/link";
import { useAuth } from "./AuthProvider";

export function SiteHeader() {
  const { status, user, signOut } = useAuth();
  return (
    <header className="sticky top-0 z-10 border-b border-slate-300 bg-white">
      <div className="mx-auto flex max-w-3xl flex-wrap items-center justify-between gap-2 px-4 py-2">
        <Link href="/" className="text-lg font-bold text-blue-900">
          College Companion
        </Link>
        {status === "authenticated" && user ? (
          <nav aria-label="Account" className="flex items-center gap-3">
            <span className="text-sm text-slate-700">{user.displayName}</span>
            <button type="button" className="btn-secondary" onClick={() => void signOut()}>
              Sign out
            </button>
          </nav>
        ) : null}
      </div>
    </header>
  );
}
