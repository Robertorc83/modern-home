import type { Metadata } from "next";
import { ListingDetail } from "@/components/listing/listing-detail";

export const metadata: Metadata = { title: "Listing" };

// Open to every role: buyers book from here, agents and admins use it as "preview as buyer".
export default async function Page({ params }: PageProps<"/buyer/listings/[id]">) {
  const { id } = await params;
  return <ListingDetail id={id} />;
}
