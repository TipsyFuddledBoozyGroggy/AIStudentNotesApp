import type { Metadata } from "next";
import { DemoDocPage } from "@/components/docs/DemoDocPage";

export const metadata: Metadata = { title: "Demo Doc" };

export default function Page() {
  return <DemoDocPage />;
}
