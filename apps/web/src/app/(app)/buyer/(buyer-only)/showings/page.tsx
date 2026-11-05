import type { Metadata } from "next";
import { MyShowings } from "@/components/my-showings";

export const metadata: Metadata = { title: "My showings" };

export default function Page() {
  return <MyShowings />;
}
