import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { FIXTURE_IDS } from "@/components/lecture/harness/fixtures";
import { HarnessFixture } from "@/components/lecture/harness/HarnessFixture";

export const metadata: Metadata = { title: "Lecture fixture (dev)", robots: { index: false, follow: false } };
export const dynamicParams = false;

export function generateStaticParams() {
  return FIXTURE_IDS.map((fixture) => ({ fixture }));
}

// Development-only; a 404 in production builds. Fixtures use FakeMicSource and MockLectureSource only.
export default async function Page({ params }: { params: Promise<{ fixture: string }> }) {
  if (process.env.NODE_ENV === "production") notFound();
  const { fixture } = await params;
  return <HarnessFixture id={fixture} />;
}
