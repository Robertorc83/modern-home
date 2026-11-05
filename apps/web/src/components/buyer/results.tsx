"use client";

import Link from "next/link";
import { useState, type ReactNode } from "react";
import { DistanceBadge, DistanceText, ListingFacts, ListingPlace } from "@/components/listing/listing-card";
import { ListingPhoto } from "@/components/listing/listing-photo";
import { SlotChip, SlotDays } from "@/components/slots";
import { EmptyState } from "@/components/states";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { formatPrice } from "@/lib/format";
import { formatLocalDate } from "@/lib/time";
import type { Listing, MatchResponse, MatchResult, Slot } from "@/lib/types";

const COLLAPSED_DAYS = 3;

/** Wide result card: photo on the left (top on mobile), listing facts and bookable times on the right. */
function ResultShell({
  listing,
  distanceKm,
  badge,
  children,
}: {
  listing: Listing;
  distanceKm?: number | null;
  badge?: ReactNode;
  children: ReactNode;
}) {
  const href = `/buyer/listings/${listing.id}`;
  return (
    <article className="group flex flex-col overflow-hidden rounded-xl bg-card ring-1 ring-foreground/10 transition-shadow hover:shadow-lg md:flex-row">
      <Link href={href} className="relative block shrink-0 overflow-hidden md:w-64" tabIndex={-1} aria-hidden>
        <ListingPhoto
          src={listing.photos?.[0]}
          alt=""
          label={listing.propertyType}
          width={600}
          rounded={false}
          className="transition-transform duration-300 group-hover:scale-[1.02] md:aspect-auto md:h-full md:min-h-52"
        />
        <DistanceBadge km={distanceKm} className="absolute top-2 left-2" />
      </Link>
      <div className="flex min-w-0 flex-1 flex-col gap-3 p-4">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0 space-y-0.5">
            <div className="flex flex-wrap items-baseline gap-x-3">
              <span className="text-lg font-semibold tabular-nums">
                {formatPrice(listing.price, listing.timeZone)}
              </span>
              <h3 className="truncate font-medium">
                <Link href={href} className="hover:underline hover:underline-offset-4">
                  {listing.address}
                </Link>
              </h3>
            </div>
            <DistanceText km={distanceKm} />
            <ListingPlace listing={listing} />
            <ListingFacts listing={listing} />
          </div>
          {badge}
        </div>
        {children}
      </div>
    </article>
  );
}

function ResultCard({
  result,
  timeZone,
  onSelect,
}: {
  result: MatchResult;
  timeZone: string;
  onSelect: (listing: Listing, slot: Slot) => void;
}) {
  const [expanded, setExpanded] = useState(false);
  const zonesDiffer = result.listing.timeZone !== timeZone;
  const hiddenDays = result.days.length - COLLAPSED_DAYS;
  return (
    <ResultShell
      listing={result.listing}
      distanceKm={result.distanceKm}
      badge={
        <Badge variant="secondary" className="shrink-0 bg-emerald-100 text-emerald-900">
          {result.matchCount} matching {result.matchCount === 1 ? "slot" : "slots"}
        </Badge>
      }
    >
      {zonesDiffer ? (
        <p className="text-xs text-muted-foreground">
          Times in your zone ({timeZone}); listing local time ({result.listing.timeZone}) under each.
        </p>
      ) : null}
      <SlotDays
        days={result.days}
        maxDays={expanded ? undefined : COLLAPSED_DAYS}
        showListingTime={zonesDiffer}
        onSelect={(slot) => onSelect(result.listing, slot)}
      />
      {hiddenDays > 0 ? (
        <Button
          variant="link"
          size="sm"
          className="h-auto self-start px-0"
          onClick={() => setExpanded((e) => !e)}
        >
          {expanded ? "Show fewer days" : `Show all ${result.days.length} days`}
        </Button>
      ) : null}
    </ResultShell>
  );
}

export function MatchResults({
  response,
  onSelect,
}: {
  response: MatchResponse;
  onSelect: (listing: Listing, slot: Slot) => void;
}) {
  if (response.results.length > 0) {
    return (
      <div className="space-y-4">
        {response.results.map((result) => (
          <ResultCard
            key={result.listing.id}
            result={result}
            timeZone={response.timeZone}
            onSelect={onSelect}
          />
        ))}
      </div>
    );
  }

  const suggestions = response.suggestions?.filter((s) => s.slots.length > 0) ?? [];
  if (suggestions.length === 0) {
    return (
      <EmptyState
        title="No listings match"
        description="Nothing is open in your availability with these filters. Try widening the radius, raising the price or adding more times."
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-950">
        <p className="font-medium">No openings in your availability.</p>
        <p>Nearest open slots:</p>
      </div>
      {suggestions.map((s) => (
        <ResultShell key={s.listing.id} listing={s.listing}>
          <div className="flex flex-wrap gap-1.5">
            {s.slots.map((slot) => (
              <SlotChip
                key={slot.startUtc}
                slot={slot}
                label={`${formatLocalDate(slot.localStart)} · ${slot.label}`}
                showListingTime={s.listing.timeZone !== response.timeZone}
                onSelect={(picked) => onSelect(s.listing, picked)}
              />
            ))}
          </div>
        </ResultShell>
      ))}
    </div>
  );
}
