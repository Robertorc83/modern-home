"use client";

import { ListingGrid } from "@/components/listing/listing-card";
import { ManagedListingCard } from "@/components/listing/listing-admin-card";
import { PageHeader } from "@/components/page-header";
import { EmptyState, ErrorState, ListSkeleton } from "@/components/states";
import { useResource } from "@/hooks/use-resource";
import { api } from "@/lib/api";

export function AllListings() {
  const listings = useResource(() => api.listings());
  return (
    <>
      <PageHeader title="All listings" description="Every listing in your brokerage, including drafts." />
      {listings.error ? (
        <ErrorState message={listings.error.message} onRetry={listings.reload} />
      ) : !listings.data ? (
        <ListSkeleton />
      ) : listings.data.length === 0 ? (
        <EmptyState
          title="No listings yet"
          description="Listings appear here once your agents create them."
        />
      ) : (
        <ListingGrid>
          {listings.data.map((listing) => (
            <ManagedListingCard key={listing.id} listing={listing} editable={false} showOwner />
          ))}
        </ListingGrid>
      )}
    </>
  );
}
