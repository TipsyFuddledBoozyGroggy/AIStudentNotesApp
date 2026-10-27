import type { Metadata } from "next";
import { SignInView } from "@/components/SignInView";

export const metadata: Metadata = { title: "Sign in" };

export default function Page() {
  return <SignInView />;
}
