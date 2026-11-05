import Link from "next/link";
import { CalendarClock, Eye, Pencil } from "lucide-react";
import { Button } from "@/components/ui/button";
import { summarizeRules } from "@/lib/format";
import type { Listing } from "@/lib/types";
import { ListingCard } from "./listing-card";

/** Listing card for agents and admins: status, showing rules and management actions. */
export function ManagedListingCard({
  listing,
  editable,
  showOwner,
}: {
  listing: Listing;
  editable: boolean;
  showOwner?: boolean;
}) {
  return (
    <ListingCard
      listing={listing}
      href={editable ? `/agent/listings/${listing.id}` : `/buyer/listings/${listing.id}`}
      showStatus
      footer={
        <div className="space-y-3">
          <p className="flex items-start gap-1.5 text-xs text-muted-foreground">
            <CalendarClock className="mt-px size-3.5 shrink-0" />
            <span>{summarizeRules(listing.showingRules)}</span>
          </p>
          {showOwner ? <p className="text-xs text-muted-foreground">Agent: {listing.ownerName}</p> : null}
          <div className="flex gap-2">
            {editable ? (
              <Button asChild size="sm" variant="outline" className="flex-1">
                <Link href={`/agent/listings/${listing.id}`}>
                  <Pencil />
                  Edit
                </Link>
              </Button>
            ) : null}
            <Button asChild size="sm" variant="ghost" className="flex-1">
              <Link href={`/buyer/listings/${listing.id}`}>
                <Eye />
                Preview as buyer
              </Link>
            </Button>
          </div>
        </div>
      }
    />
  );
}
