"use client";

import Link from "next/link";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { EmptyState, ErrorState, ListSkeleton } from "@/components/states";
import { ListingGrid } from "@/components/listing/listing-card";
import { ManagedListingCard } from "@/components/listing/listing-admin-card";
import { Button } from "@/components/ui/button";
import { useResource } from "@/hooks/use-resource";
import { api } from "@/lib/api";

export function MyListings() {
  const listings = useResource(() => api.listings({ mine: true }));

  return (
    <>
      <PageHeader
        title="My listings"
        description="Each listing has its own weekly showing windows, in the listing's time zone."
        actions={
          <Button asChild>
            <Link href="/agent/listings/new">
              <Plus />
              New listing
            </Link>
          </Button>
        }
      />
      {listings.error ? (
        <ErrorState message={listings.error.message} onRetry={listings.reload} />
      ) : !listings.data ? (
        <ListSkeleton rows={3} />
      ) : listings.data.length === 0 ? (
        <EmptyState
          title="No listings yet"
          description="Create a listing and set its showing windows so buyers can book."
          action={
            <Button asChild variant="outline">
              <Link href="/agent/listings/new">Create your first listing</Link>
            </Button>
          }
        />
      ) : (
        <ListingGrid>
          {listings.data.map((listing) => (
            <ManagedListingCard key={listing.id} listing={listing} editable />
          ))}
        </ListingGrid>
      )}
    </>
  );
}
