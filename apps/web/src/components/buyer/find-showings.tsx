"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { ListingCard, ListingGrid } from "@/components/listing/listing-card";
import { Loader2, Search } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { EmptyState, ErrorState } from "@/components/states";
import { useUser } from "@/components/user-context";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useDebounced } from "@/hooks/use-debounced";
import { useResource } from "@/hooks/use-resource";
import { ApiError, api } from "@/lib/api";
import { browserTimeZone, nextSaturday, todayInZone, zoneOptions } from "@/lib/time";
import { cn } from "@/lib/utils";
import type { Listing, LocalRange, MatchRequest, MatchResponse, ParsedAvailability } from "@/lib/types";
import { AiAvailability } from "./ai-availability";
import { BookingDialog, type BookingTarget } from "./booking-dialog";
import { DEFAULT_FILTERS, Filters, type SearchFilters } from "./filters";
import { ManualRanges, newRange, rangeError, toLocalRanges, type ManualRange } from "./manual-ranges";
import { MatchResults } from "./results";

function toNumber(value: string): number | null {
  if (value.trim() === "" || value === "any") return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

function buildRequest(timeZone: string, ranges: LocalRange[], filters: SearchFilters): MatchRequest {
  const near = filters.near.trim() || null;
  return {
    timeZone,
    ranges,
    city: filters.city.trim() || null,
    maxPrice: toNumber(filters.maxPrice),
    minBeds: toNumber(filters.minBeds),
    propertyType: filters.propertyType === "any" ? null : filters.propertyType,
    near,
    nearLat: null,
    nearLng: null,
    radiusKm: near ? toNumber(filters.radiusKm) : null,
  };
}

export function FindShowings() {
  const user = useUser();
  const [timeZone, setTimeZone] = useState(() => user.timeZone || browserTimeZone());
  const [mode, setMode] = useState<"manual" | "ai">("manual");
  const [manual, setManual] = useState<ManualRange[]>(() => [newRange(nextSaturday(timeZone))]);
  const [parsed, setParsed] = useState<ParsedAvailability | null>(null);
  const [filters, setFilters] = useState<SearchFilters>(DEFAULT_FILTERS);

  // The ranges of the last search; filter changes re-run it.
  const [searchRanges, setSearchRanges] = useState<LocalRange[] | null>(null);
  const [response, setResponse] = useState<MatchResponse | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [searching, setSearching] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);
  const [booking, setBooking] = useState<BookingTarget | null>(null);

  const today = todayInZone(timeZone);
  const manualValid = manual.length > 0 && manual.every((r) => rangeError(r, today) === null);
  const request = useMemo(
    () => (searchRanges ? buildRequest(timeZone, searchRanges, filters) : null),
    [timeZone, searchRanges, filters],
  );

  // Before any search, show the brokerage's homes so the page is never empty. Filters apply here too
  // (except "near", which only the matcher understands).
  const browseFilters = useDebounced(filters, 300);
  const browse = useResource(
    () =>
      api.listings({
        city: browseFilters.city.trim() || null,
        maxPrice: toNumber(browseFilters.maxPrice),
        minBeds: toNumber(browseFilters.minBeds),
        propertyType: browseFilters.propertyType === "any" ? null : browseFilters.propertyType,
      }),
    [browseFilters.city, browseFilters.maxPrice, browseFilters.minBeds, browseFilters.propertyType],
  );

  const generation = useRef(0);
  useEffect(() => {
    if (!request) return;
    const current = ++generation.current;
    const timer = setTimeout(async () => {
      setSearching(true);
      try {
        const result = await api.match(request);
        if (current !== generation.current) return;
        setResponse(result);
        setError(null);
      } catch (err) {
        if (current !== generation.current) return;
        setError(err instanceof ApiError ? err : new ApiError(0, { detail: String(err) }));
      } finally {
        if (current === generation.current) setSearching(false);
      }
    }, 300);
    return () => clearTimeout(timer);
  }, [request, refreshKey]);

  function changeTimeZone(zone: string) {
    setTimeZone(zone);
    // Local ranges are meaningless in another zone: parsed text must be re-parsed.
    setParsed(null);
    if (mode === "ai") setSearchRanges(null);
  }

  function onBooked() {
    // The booked slot is gone now; refresh so the results reflect it.
    setBooking(null);
    setRefreshKey((k) => k + 1);
  }

  return (
    <>
      <PageHeader
        title="Find showings"
        description="Tell us when you're free. We match it against every listing's open slots, across time zones."
      />

      <div className="grid gap-6 lg:grid-cols-[380px_minmax(0,1fr)]">
        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle>When are you free?</CardTitle>
              <CardDescription>Times are in the zone below.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="space-y-1.5">
                <Label htmlFor="buyerZone">Your time zone</Label>
                <Select value={timeZone} onValueChange={changeTimeZone}>
                  <SelectTrigger id="buyerZone" className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {zoneOptions(user.timeZone, browserTimeZone()).map((z) => (
                      <SelectItem key={z} value={z}>
                        {z}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>

              <Tabs value={mode} onValueChange={(v) => setMode(v as "manual" | "ai")}>
                <TabsList className="w-full">
                  <TabsTrigger value="manual">Pick times</TabsTrigger>
                  <TabsTrigger value="ai">Describe it</TabsTrigger>
                </TabsList>
                <TabsContent value="manual" className="space-y-4 pt-2">
                  <ManualRanges ranges={manual} onChange={setManual} today={today} />
                  <Button
                    className="w-full"
                    disabled={!manualValid || searching}
                    onClick={() => {
                      setSearchRanges(toLocalRanges(manual));
                      setRefreshKey((k) => k + 1);
                    }}
                  >
                    {searching ? <Loader2 className="animate-spin" /> : <Search />}
                    Find showings
                  </Button>
                </TabsContent>
                <TabsContent value="ai" className="pt-2">
                  <AiAvailability
                    timeZone={timeZone}
                    parsed={parsed}
                    onParsed={setParsed}
                    searching={searching}
                    onSearch={(ranges) => {
                      setSearchRanges(ranges);
                      setRefreshKey((k) => k + 1);
                    }}
                  />
                </TabsContent>
              </Tabs>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Filters</CardTitle>
              <CardDescription>Results update as you change them.</CardDescription>
            </CardHeader>
            <CardContent>
              <Filters value={filters} onChange={setFilters} />
            </CardContent>
          </Card>
        </div>

        <section aria-label="Results" aria-busy={searching} className="min-w-0">
          {error && !searching ? (
            <ErrorState message={error.message} onRetry={() => setRefreshKey((k) => k + 1)} />
          ) : !searchRanges ? (
            <BrowseListings
              listings={browse.data}
              error={browse.error?.message}
              onRetry={browse.reload}
              nearIgnored={Boolean(filters.near.trim())}
            />
          ) : !response ? (
            <div className="space-y-4">
              {[0, 1, 2].map((i) => (
                <Skeleton key={i} className="h-44 w-full rounded-xl" />
              ))}
            </div>
          ) : (
            <div className={cn("transition-opacity", searching && "opacity-60")}>
              <div className="mb-3 flex items-center justify-between gap-2">
                <p className="text-sm text-muted-foreground">
                  {response.results.length > 0
                    ? `${response.results.length} ${response.results.length === 1 ? "home" : "homes"} with open slots in your times`
                    : "Search results"}
                </p>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => {
                    setSearchRanges(null);
                    setResponse(null);
                    setError(null);
                  }}
                >
                  Browse all homes
                </Button>
              </div>
              <MatchResults response={response} onSelect={(listing, slot) => setBooking({ listing, slot })} />
            </div>
          )}
        </section>
      </div>

      <BookingDialog
        target={booking}
        searchTimeZone={response?.timeZone ?? timeZone}
        onClose={() => setBooking(null)}
        onBooked={onBooked}
      />
    </>
  );
}

function BrowseListings({
  listings,
  error,
  onRetry,
  nearIgnored,
}: {
  listings: Listing[] | undefined;
  error?: string;
  onRetry: () => void;
  nearIgnored: boolean;
}) {
  if (error) return <ErrorState message={error} onRetry={onRetry} />;
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-lg font-semibold">Homes in your brokerage</h2>
        <p className="text-sm text-muted-foreground">
          {nearIgnored ? "Distance applies when you search. " : ""}Pick your times to see open slots.
        </p>
      </div>
      {!listings ? (
        <ListingGrid>
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="aspect-[4/5] w-full rounded-xl" />
          ))}
        </ListingGrid>
      ) : listings.length === 0 ? (
        <EmptyState title="No homes match these filters" description="Clear a filter to see more homes." />
      ) : (
        <ListingGrid>
          {listings.map((listing) => (
            <ListingCard key={listing.id} listing={listing} href={`/buyer/listings/${listing.id}`} />
          ))}
        </ListingGrid>
      )}
    </div>
  );
}
