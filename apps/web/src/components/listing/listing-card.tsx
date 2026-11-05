import Link from "next/link";
import type { ReactNode } from "react";
import { MapPin } from "lucide-react";
import { StatusBadge } from "@/components/status-badge";
import { cn } from "@/lib/utils";
import { formatBedsBaths, formatDistance, formatPrice, keyAttribute } from "@/lib/format";
import type { Listing } from "@/lib/types";
import { ListingPhoto } from "./listing-photo";

export function ListingFacts({ listing, className }: { listing: Listing; className?: string }) {
  const attribute = keyAttribute(listing);
  return (
    <p className={cn("text-sm text-muted-foreground", className)}>
      <span className="text-foreground">{formatBedsBaths(listing)}</span> · {listing.propertyType}
      {attribute ? ` · ${attribute}` : ""}
    </p>
  );
}

export function ListingPlace({ listing }: { listing: Listing }) {
  return (
    <p className="truncate text-sm text-muted-foreground">
      {[listing.neighborhood, listing.city].filter(Boolean).join(" · ")}
    </p>
  );
}

/**
 * Visual overlay on the photo. It sits inside a link that is hidden from assistive tech, so cards
 * also render `DistanceText` in their body for screen readers.
 */
export function DistanceBadge({ km, className }: { km: number | null | undefined; className?: string }) {
  const distance = formatDistance(km);
  if (!distance) return null;
  return (
    <span
      aria-hidden
      className={cn(
        "inline-flex items-center gap-1 rounded-full bg-background/95 px-2 py-0.5 text-xs font-medium text-foreground shadow-sm",
        className,
      )}
    >
      <MapPin className="size-3" />
      {distance}
    </span>
  );
}

/** Screen-reader-only counterpart of `DistanceBadge`. */
export function DistanceText({ km }: { km: number | null | undefined }) {
  const distance = formatDistance(km);
  if (!distance) return null;
  return <span className="sr-only">{distance}</span>;
}

/**
 * Grid card: cover photo with price overlay, then address, place and key facts.
 * The photo and title link to `href`; `footer` holds extra content or actions.
 */
export function ListingCard({
  listing,
  href,
  distanceKm,
  showStatus = false,
  footer,
}: {
  listing: Listing;
  href?: string;
  distanceKm?: number | null;
  showStatus?: boolean;
  footer?: ReactNode;
}) {
  const photo = (
    <div className="relative">
      <ListingPhoto
        src={listing.photos?.[0]}
        alt={listing.address}
        label={listing.propertyType}
        width={720}
        rounded={false}
        className="transition-transform duration-300 group-hover:scale-[1.02]"
      />
      <span className="absolute bottom-2 left-2 rounded-md bg-black/70 px-2 py-1 text-base font-semibold text-white tabular-nums backdrop-blur-sm">
        {formatPrice(listing.price, listing.timeZone)}
      </span>
      <div className="absolute top-2 right-2 left-2 flex justify-between gap-2">
        {showStatus ? <StatusBadge status={listing.status} className="shadow-sm" /> : <span />}
        <DistanceBadge km={distanceKm} />
      </div>
    </div>
  );

  return (
    <article className="group flex flex-col overflow-hidden rounded-xl bg-card ring-1 ring-foreground/10 transition-shadow hover:shadow-lg">
      <div className="overflow-hidden">
        {href ? (
          <Link href={href} aria-label={listing.address} tabIndex={-1}>
            {photo}
          </Link>
        ) : (
          photo
        )}
      </div>
      <div className="flex flex-1 flex-col gap-1 p-4">
        <h3 className="truncate font-semibold">
          {href ? (
            <Link href={href} className="hover:underline hover:underline-offset-4">
              {listing.address}
            </Link>
          ) : (
            listing.address
          )}
        </h3>
        <DistanceText km={distanceKm} />
        <ListingPlace listing={listing} />
        <ListingFacts listing={listing} />
        {footer ? <div className="mt-auto pt-3">{footer}</div> : null}
      </div>
    </article>
  );
}

export function ListingGrid({ children }: { children: ReactNode }) {
  return <div className="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">{children}</div>;
}
