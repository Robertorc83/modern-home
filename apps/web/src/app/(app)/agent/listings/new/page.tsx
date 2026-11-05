import type { Metadata } from "next";
import { ListingEditor } from "@/components/listing-editor/listing-editor";

export const metadata: Metadata = { title: "New listing" };

export default function Page() {
  return <ListingEditor />;
}
