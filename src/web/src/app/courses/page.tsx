import type { Metadata } from "next";
import { AllCoursesPage } from "@/components/AllCoursesPage";

export const metadata: Metadata = { title: "All courses" };

export default function Page() {
  return <AllCoursesPage />;
}
