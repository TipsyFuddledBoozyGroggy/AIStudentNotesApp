import type { Metadata, Viewport } from "next";
import type { ReactNode } from "react";
import { AuthProvider } from "@/components/AuthProvider";
import { MockProvider } from "@/components/MockProvider";
import { SiteHeader } from "@/components/SiteHeader";
import "./globals.css";

export const metadata: Metadata = {
  title: { default: "Tether", template: "%s | Tether" },
  description: "A calm place for your courses and lectures.",
};

export const viewport: Viewport = { width: "device-width", initialScale: 1 };

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <body>
        <a
          href="#main"
          className="sr-only focus:not-sr-only focus:absolute focus:left-2 focus:top-2 focus:z-20 focus:rounded-lg focus:bg-white focus:px-4 focus:py-2 focus:font-semibold focus:text-blue-900"
        >
          Skip to main content
        </a>
        <MockProvider>
          <AuthProvider>
            <SiteHeader />
            <main id="main" tabIndex={-1} className="mx-auto max-w-3xl px-4 py-8">
              {children}
            </main>
          </AuthProvider>
        </MockProvider>
      </body>
    </html>
  );
}
