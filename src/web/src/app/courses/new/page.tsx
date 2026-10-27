import type { Metadata } from "next";
import { NewCoursePage } from "@/components/NewCoursePage";

export const metadata: Metadata = { title: "Add a course" };

export default function Page() {
  return <NewCoursePage />;
}
