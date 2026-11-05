"use client";

import { useMemo } from "react";
import { useResource } from "@/hooks/use-resource";
import { api } from "@/lib/api";
import type { Listing, Role } from "@/lib/types";

/** Listings visible to the user, by id: used to decorate showing rows with a thumbnail. */
export function useListingIndex(role: Role): Map<string, Listing> {
  const listings = useResource(() => api.listings(role === "Agent" ? { mine: true } : undefined), [role]);
  return useMemo(() => new Map((listings.data ?? []).map((l) => [l.id, l])), [listings.data]);
}
