import type { Metadata } from "next";
import { SyllabusPage } from "@/components/syllabus/SyllabusPage";

export const metadata: Metadata = { title: "Syllabus" };

export default function Page() {
  return <SyllabusPage />;
}
