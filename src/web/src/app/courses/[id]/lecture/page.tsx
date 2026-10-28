import type { Metadata } from "next";
import { LectureStartPage } from "@/components/lecture/LectureStartPage";

export const metadata: Metadata = { title: "Start lecture" };

export default function Page() {
  return <LectureStartPage />;
}
