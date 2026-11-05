import type { Metadata } from "next";
import { AllListings } from "@/components/admin/all-listings";

export const metadata: Metadata = { title: "All listings" };

export default function Page() {
  return <AllListings />;
}
