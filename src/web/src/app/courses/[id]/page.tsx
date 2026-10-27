import type { Metadata } from "next";
import { CourseDetailPage } from "@/components/CourseDetailPage";

export const metadata: Metadata = { title: "Course" };

export default function Page() {
  return <CourseDetailPage />;
}
