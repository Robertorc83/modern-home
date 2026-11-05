"use client";

import Link from "next/link";
import { useState } from "react";
import { ArrowLeft, CalendarClock, Eye, UserRound } from "lucide-react";
import { BookingDialog, type BookingTarget } from "@/components/buyer/booking-dialog";
import { SlotDays } from "@/components/slots";
import { ErrorState, ListSkeleton } from "@/components/states";
import { StatusBadge } from "@/components/status-badge";
import { useUser } from "@/components/user-context";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { useResource } from "@/hooks/use-resource";
import { api } from "@/lib/api";
import { attributeRows, formatBedsBaths, formatPrice, rulesSentence } from "@/lib/format";
import { browserTimeZone } from "@/lib/time";
import { cn } from "@/lib/utils";
import type { Listing } from "@/lib/types";
import { ListingPhoto } from "./listing-photo";

const COLLAPSED_DAYS = 6;

const BACK: Record<string, { href: string; label: string }> = {
  Buyer: { href: "/buyer", label: "Find showings" },
  Agent: { href: "/agent/listings", label: "My listings" },
  BrokerageAdmin: { href: "/admin/listings", label: "All listings" },
};

export function ListingDetail({ id }: { id: string }) {
  const user = useUser();
  const listing = useResource(() => api.listing(id), [id]);
  const back = BACK[user.role];

  return (
    <>
      <Button asChild variant="ghost" size="sm" className="mb-4 -ml-2">
        <Link href={back.href}>
          <ArrowLeft />
          {back.label}
        </Link>
      </Button>
      {listing.error ? (
        <ErrorState
          message={
            listing.error.status === 404
              ? "This listing was not found in your brokerage."
              : listing.error.message
          }
          onRetry={listing.error.status === 404 ? undefined : listing.reload}
        />
      ) : !listing.data ? (
        <div className="grid gap-8 lg:grid-cols-[minmax(0,1fr)_380px]">
          <Skeleton className="aspect-[4/3] w-full rounded-xl" />
          <ListSkeleton rows={4} />
        </div>
      ) : (
        <DetailBody listing={listing.data} />
      )}
    </>
  );
}

function Gallery({ listing }: { listing: Listing }) {
  const photos = listing.photos ?? [];
  const [index, setIndex] = useState(0);
  return (
    <div className="space-y-2">
      <ListingPhoto
        src={photos[index]}
        alt={`${listing.address}, photo ${index + 1} of ${Math.max(photos.length, 1)}`}
        label={listing.propertyType}
        width={1400}
        eager
        className="rounded-xl sm:aspect-[16/10]"
      />
      {photos.length > 1 ? (
        <div className="grid grid-cols-4 gap-2 sm:grid-cols-6">
          {photos.map((photo, i) => (
            <button
              key={`${photo}-${i}`}
              type="button"
              onClick={() => setIndex(i)}
              aria-label={`Show photo ${i + 1}`}
              aria-current={i === index}
              className={cn(
                "overflow-hidden rounded-md ring-offset-2 transition focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none",
                i === index ? "ring-2 ring-foreground" : "opacity-70 hover:opacity-100",
              )}
            >
              <ListingPhoto src={photo} alt="" width={240} rounded={false} />
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function DetailBody({ listing }: { listing: Listing }) {
  const user = useUser();
  const isBuyer = user.role === "Buyer";
  const timeZone = user.timeZone || browserTimeZone();
  const [booking, setBooking] = useState<BookingTarget | null>(null);
  const horizon = listing.showingRules?.horizonDays ?? 14;
  const slots = useResource(() => {
    const from = new Date();
    const to = new Date(from.getTime() + horizon * 24 * 3600 * 1000);
    return api.slots(listing.id, { from: from.toISOString(), to: to.toISOString(), timeZone });
  }, [listing.id, timeZone, horizon]);
  const zonesDiffer = listing.timeZone !== timeZone;
  const [allDays, setAllDays] = useState(false);
  const dayCount = slots.data?.days.length ?? 0;
  const facts = [
    { label: "Price", value: formatPrice(listing.price, listing.timeZone) },
    { label: "Beds / baths", value: formatBedsBaths(listing) },
    { label: "Type", value: listing.propertyType },
    ...attributeRows(listing),
  ];

  return (
    <div className="grid gap-8 lg:grid-cols-[minmax(0,1fr)_380px]">
      <div className="min-w-0 space-y-8">
        <div className="space-y-2">
          <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
            <h1 className="text-2xl font-semibold tracking-tight">{listing.address}</h1>
            <span className="text-2xl font-semibold tabular-nums">
              {formatPrice(listing.price, listing.timeZone)}
            </span>
          </div>
          <p className="text-muted-foreground">
            {[listing.neighborhood, listing.city].filter(Boolean).join(" · ")} · {formatBedsBaths(listing)} ·{" "}
            {listing.propertyType}
          </p>
          {!isBuyer ? (
            <div className="flex items-center gap-2 pt-1">
              <StatusBadge status={listing.status} />
            </div>
          ) : null}
        </div>

        <Gallery listing={listing} />

        {listing.description ? (
          <section className="space-y-2">
            <h2 className="text-lg font-semibold">About this home</h2>
            <p className="leading-relaxed whitespace-pre-line text-muted-foreground">{listing.description}</p>
          </section>
        ) : null}

        <section className="space-y-3">
          <h2 className="text-lg font-semibold">Facts</h2>
          <dl className="grid gap-px overflow-hidden rounded-xl bg-border ring-1 ring-foreground/10 sm:grid-cols-2">
            {facts.map((f) => (
              <div
                key={f.label}
                className="flex justify-between gap-4 bg-card px-4 py-2.5 text-sm sm:odd:last:col-span-2"
              >
                <dt className="text-muted-foreground">{f.label}</dt>
                <dd className="text-right font-medium">{f.value}</dd>
              </div>
            ))}
          </dl>
        </section>
      </div>

      <div className="space-y-4">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <CalendarClock className="size-5 text-muted-foreground" />
              Open showing times
            </CardTitle>
            <CardDescription>{rulesSentence(listing.showingRules)}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            {!isBuyer ? (
              <Alert>
                <Eye />
                <AlertDescription>Preview as a buyer sees it. Only buyers can book.</AlertDescription>
              </Alert>
            ) : null}
            {zonesDiffer ? (
              <p className="text-xs text-muted-foreground">
                Times in your zone ({timeZone}); the home is in {listing.timeZone}, shown under each time.
              </p>
            ) : null}
            {slots.error ? (
              <ErrorState message={slots.error.message} onRetry={slots.reload} />
            ) : !slots.data ? (
              <ListSkeleton rows={3} />
            ) : slots.data.days.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                No open times in the next {horizon} days. Check back soon.
              </p>
            ) : (
              <>
                <SlotDays
                  days={slots.data.days}
                  maxDays={allDays ? undefined : COLLAPSED_DAYS}
                  showListingTime={zonesDiffer}
                  onSelect={isBuyer ? (slot) => setBooking({ listing, slot }) : undefined}
                />
                {dayCount > COLLAPSED_DAYS ? (
                  <Button
                    variant="link"
                    size="sm"
                    className="h-auto px-0"
                    onClick={() => setAllDays((a) => !a)}
                  >
                    {allDays ? "Show fewer days" : `Show all ${dayCount} days`}
                  </Button>
                ) : null}
              </>
            )}
          </CardContent>
        </Card>

        <Card size="sm">
          <CardContent className="flex items-center gap-3">
            <div className="flex size-9 items-center justify-center rounded-full bg-muted">
              <UserRound className="size-4 text-muted-foreground" />
            </div>
            <div>
              <p className="text-sm font-medium">{listing.ownerName}</p>
              <p className="text-xs text-muted-foreground">Listing agent</p>
            </div>
          </CardContent>
        </Card>
      </div>

      <BookingDialog
        target={booking}
        searchTimeZone={timeZone}
        onClose={() => setBooking(null)}
        onBooked={() => {
          setBooking(null);
          void slots.reload();
        }}
      />
    </div>
  );
}
