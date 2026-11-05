import type { Metadata } from "next";
import { FindShowings } from "@/components/buyer/find-showings";

export const metadata: Metadata = { title: "Find showings" };

export default function Page() {
  return <FindShowings />;
}
