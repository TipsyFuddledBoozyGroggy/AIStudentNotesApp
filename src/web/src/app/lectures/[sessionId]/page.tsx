import type { Metadata } from "next";
import { LectureReopenPage } from "@/components/lecture/LectureReopenPage";

export const metadata: Metadata = { title: "Lecture" };

export default function Page() {
  return <LectureReopenPage />;
}
