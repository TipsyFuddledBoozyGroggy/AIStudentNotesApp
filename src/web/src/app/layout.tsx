import type { Metadata, Viewport } from "next";
import { Atkinson_Hyperlegible_Next, Inter, JetBrains_Mono, Newsreader, Source_Sans_3 } from "next/font/google";
import type { ReactNode } from "react";
import { AuthProvider } from "@/components/AuthProvider";
import { MockProvider } from "@/components/MockProvider";
import { AppShell } from "@/components/AppShell";
import { PrefsProvider } from "@/components/PrefsProvider";
import { PRE_HYDRATION_SCRIPT_SRC, themesEnabled } from "@/lib/design/prefs";
import { PALETTES } from "@/lib/design/tokens";
import "./globals.css";

// Self-hosted at build time by next/font (no runtime request to Google). All are SIL Open Font License.
// Body face is the same in every theme. Heading faces are per theme (themes-spec 2.5): only Paper's is preloaded; the
// browser downloads another face only when the active theme's headings use it.
const body = Atkinson_Hyperlegible_Next({ subsets: ["latin", "latin-ext"], variable: "--font-body", display: "swap" });
const newsreader = Newsreader({ subsets: ["latin"], variable: "--f-newsreader", axes: ["opsz"], display: "swap" });
const sourceSans = Source_Sans_3({ subsets: ["latin"], variable: "--f-source-sans", weight: ["600"], display: "swap", preload: false });
const jetbrainsMono = JetBrains_Mono({ subsets: ["latin"], variable: "--f-jetbrains-mono", weight: ["400", "600"], display: "swap", preload: false });
const inter = Inter({ subsets: ["latin"], variable: "--f-inter", weight: ["600"], display: "swap", preload: false });
const fontVars = [body, newsreader, sourceSans, jetbrainsMono, inter].map((f) => f.variable).join(" ");

export const metadata: Metadata = {
  title: { default: "Tether", template: "%s | Tether" },
  description: "A calm place for your courses and lectures.",
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  colorScheme: "light dark",
  themeColor: [
    // Paper's canvas colours. No per-theme theme-color (Lead decision Q-LEAD2).
    { media: "(prefers-color-scheme: light)", color: PALETTES.paper.canvas },
    { media: "(prefers-color-scheme: dark)", color: PALETTES.nightDesk.canvas },
  ],
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    // The head script sets data-theme etc. before paint, so React must not complain that they differ from SSR.
    // data-themes="off" (build flag NEXT_PUBLIC_THEMES=off) makes prefs-init.js force the Paper theme (themes-spec 6 rollback).
    <html lang="en" className={fontVars} data-themes={themesEnabled() ? "on" : "off"} suppressHydrationWarning>
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
