import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { UiKit } from "@/components/ui/UiKit";

export const metadata: Metadata = { title: "UI kit (dev)", robots: { index: false, follow: false } };

// Development-only primitives page for axe and screenshots in every palette (design-direction.md slice 1).
export default function Page() {
  if (process.env.NODE_ENV === "production") notFound();
  return <UiKit />;
}
