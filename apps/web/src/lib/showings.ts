import type { Showing } from "./types";
import { formatInZone } from "./time";

/** Upcoming showings soonest first, then past ones most recent first. */
export function sortShowings(showings: Showing[]): Showing[] {
  const now = Date.now();
  const upcoming = showings.filter((s) => new Date(s.slotEndUtc).getTime() >= now);
  const past = showings.filter((s) => new Date(s.slotEndUtc).getTime() < now);
  const byStart = (a: Showing, b: Showing) =>
    new Date(a.slotStartUtc).getTime() - new Date(b.slotStartUtc).getTime();
  return [...upcoming.sort(byStart), ...past.sort((a, b) => byStart(b, a))];
}

export function isPast(showing: Showing): boolean {
  return new Date(showing.slotEndUtc).getTime() < Date.now();
}

/** Prefer the server's label (listing zone); fall back to formatting in the listing's zone. */
export function listingTimeLabel(showing: Showing): string {
  return showing.listingLocalLabel || formatInZone(showing.slotStartUtc, showing.listingTimeZone);
}
