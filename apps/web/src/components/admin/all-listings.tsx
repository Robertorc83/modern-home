"use client";

import { ListingGrid } from "@/components/listing/listing-card";
import { ManagedListingCard } from "@/components/listing/listing-admin-card";
import { PageHeader } from "@/components/page-header";
import { EmptyState, ErrorState, ListSkeleton } from "@/components/states";
import { useResource } from "@/hooks/use-resource";
import { api } from "@/lib/api";
import { listingCountSummary, sortByAddress } from "@/lib/format";

export function AllListings() {
  const listings = useResource(() => api.listings());
  const sorted = listings.data ? sortByAddress(listings.data) : undefined;
  return (
    <>
      <PageHeader
        title="All listings"
        description={
          sorted ? listingCountSummary(sorted) : "Every listing in your brokerage, including drafts."
        }
      />
      {listings.error ? (
        <ErrorState message={listings.error.message} onRetry={listings.reload} />
      ) : !sorted ? (
        <ListSkeleton />
      ) : sorted.length === 0 ? (
        <EmptyState
          title="No listings yet"
          description="Listings appear here once your agents create them."
        />
      ) : (
        <ListingGrid>
          {sorted.map((listing) => (
            <ManagedListingCard key={listing.id} listing={listing} editable={false} showOwner />
          ))}
        </ListingGrid>
      )}
    </>
  );
}
