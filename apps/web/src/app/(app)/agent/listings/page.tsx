import type { Metadata } from "next";
import { MyListings } from "./my-listings";

export const metadata: Metadata = { title: "My listings" };

export default function Page() {
  return <MyListings />;
}
