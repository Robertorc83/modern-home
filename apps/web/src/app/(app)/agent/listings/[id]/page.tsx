import type { Metadata } from "next";
import { ListingEditor } from "@/components/listing-editor/listing-editor";

export const metadata: Metadata = { title: "Edit listing" };

export default async function Page({ params }: PageProps<"/agent/listings/[id]">) {
  const { id } = await params;
  return <ListingEditor id={id} />;
}
