import type { Metadata, Viewport } from "next";
import { Atkinson_Hyperlegible_Next, Newsreader } from "next/font/google";
import type { ReactNode } from "react";
import { AuthProvider } from "@/components/AuthProvider";
import { MockProvider } from "@/components/MockProvider";
import { AppShell } from "@/components/AppShell";
import { PrefsProvider } from "@/components/PrefsProvider";
import { PRE_HYDRATION_SCRIPT_SRC } from "@/lib/design/prefs";
import "./globals.css";

// Self-hosted at build time by next/font (no runtime request to Google). Both are SIL Open Font License.
const body = Atkinson_Hyperlegible_Next({ subsets: ["latin", "latin-ext"], variable: "--font-body", display: "swap" });
const head = Newsreader({ subsets: ["latin"], variable: "--font-head", axes: ["opsz"], display: "swap" });

export const metadata: Metadata = {
  title: { default: "Tether", template: "%s | Tether" },
  description: "A calm place for your courses and lectures.",
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: "#F5F2EA" },
    { media: "(prefers-color-scheme: dark)", color: "#14161B" },
  ],
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    // The head script sets data-theme etc. before paint, so React must not complain that they differ from SSR.
    <html lang="en" className={`${body.variable} ${head.variable}`} suppressHydrationWarning>
      <head>
        {/* Deliberately synchronous: it must set data-theme before the body paints (no dark-mode flash). ~1 KB, same origin. */}
        {/* eslint-disable-next-line @next/next/no-sync-scripts */}
        <script src={PRE_HYDRATION_SCRIPT_SRC} />
      </head>
      <body>
        <a
          href="#main"
          className="sr-only focus:not-sr-only focus:absolute focus:left-2 focus:top-2 focus:z-30 focus:rounded-control focus:bg-surface focus:px-4 focus:py-3 focus:font-semibold focus:text-accent"
        >
          Skip to main content
        </a>
        <PrefsProvider>
          <MockProvider>
            <AuthProvider>
              <AppShell>{children}</AppShell>
            </AuthProvider>
          </MockProvider>
        </PrefsProvider>
      </body>
    </html>
  );
}
