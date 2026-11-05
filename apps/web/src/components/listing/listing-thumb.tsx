import { cn } from "@/lib/utils";
import type { Listing } from "@/lib/types";
import { ListingPhoto } from "./listing-photo";

export function ListingThumb({ listing, className }: { listing: Listing | undefined; className?: string }) {
  return (
    <div className={cn("w-16 shrink-0", className)}>
      <ListingPhoto src={listing?.photos?.[0]} alt="" width={160} className="rounded-md" />
    </div>
  );
}
