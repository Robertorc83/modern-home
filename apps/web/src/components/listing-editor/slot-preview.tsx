"use client";

import { RefreshCw } from "lucide-react";
import { SlotDays } from "@/components/slots";
import { ErrorState, ListSkeleton } from "@/components/states";
import { Button } from "@/components/ui/button";
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { useResource } from "@/hooks/use-resource";
import { api } from "@/lib/api";

const PREVIEW_DAYS = 7;

export function SlotPreview({
  listingId,
  timeZone,
  version,
}: {
  listingId: string | null;
  timeZone: string;
  /** Bumped after each save so the preview reflects the saved rules. */
  version: number;
}) {
  const slots = useResource(async () => {
    if (!listingId) return null;
    const from = new Date();
    const to = new Date(from.getTime() + PREVIEW_DAYS * 24 * 3600 * 1000);
    return api.slots(listingId, { from: from.toISOString(), to: to.toISOString(), timeZone });
  }, [listingId, timeZone, version]);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Next open slots</CardTitle>
        <CardDescription>
          What buyers can book in the next {PREVIEW_DAYS} days, in listing time. Reflects saved rules.
        </CardDescription>
        {listingId ? (
          <CardAction>
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label="Refresh preview"
              onClick={slots.reload}
              disabled={slots.loading}
            >
              <RefreshCw className={slots.loading ? "animate-spin" : undefined} />
            </Button>
          </CardAction>
        ) : null}
      </CardHeader>
      <CardContent>
        {!listingId ? (
          <p className="text-sm text-muted-foreground">Save the listing to preview its open slots.</p>
        ) : slots.error ? (
          <ErrorState message={slots.error.message} onRetry={slots.reload} />
        ) : slots.data === undefined ? (
          <ListSkeleton rows={3} />
        ) : !slots.data || slots.data.days.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No open slots in the next {PREVIEW_DAYS} days. Check the status is Active, the weekly windows,
            lead time and blackout dates.
          </p>
        ) : (
          <SlotDays days={slots.data.days} maxDays={5} />
        )}
      </CardContent>
    </Card>
  );
}
