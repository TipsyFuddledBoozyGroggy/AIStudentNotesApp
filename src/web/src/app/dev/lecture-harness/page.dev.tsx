import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { FIXTURES } from "@/components/lecture/harness/fixtures";

export const metadata: Metadata = { title: "Lecture harness (dev)", robots: { index: false, follow: false } };

// Development-only: one fixture per state of docs/ux/m2-live-lecture-screens.md. Not linked from any nav,
// and a 404 in production builds.
export default function Page() {
  if (process.env.NODE_ENV === "production") notFound();
  return (
    <>
      <h1 className="text-2xl font-bold">Lecture screen harness (development only)</h1>
      <ul className="mt-4 list-disc pl-6">
        {Object.entries(FIXTURES).map(([id, f]) => (
          <li key={id}>
            <Link className="underline" href={`/dev/lecture-harness/${id}`}>
              {f.title}
            </Link>{" "}
            (UX {f.ux})
          </li>
        ))}
      </ul>
    </>
  );
}
