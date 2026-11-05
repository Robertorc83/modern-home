"use client";

import Link from "next/link";
import { useState } from "react";
import { Loader2 } from "lucide-react";
import { toast } from "sonner";
import { ListingThumb } from "@/components/listing/listing-thumb";
import { LiveIndicator } from "@/components/live-indicator";
import { PageHeader } from "@/components/page-header";
import { EmptyState, ErrorState, ListSkeleton } from "@/components/states";
import { StatusBadge } from "@/components/status-badge";
import { useUser } from "@/components/user-context";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { useFlash } from "@/hooks/use-flash";
import { useListingIndex } from "@/hooks/use-listing-index";
import { useResource } from "@/hooks/use-resource";
import { useShowingStream } from "@/hooks/use-showing-stream";
import { api, toastError } from "@/lib/api";
import { isPast, listingTimeLabel, sortShowings } from "@/lib/showings";
import { formatInZone } from "@/lib/time";
import { cn } from "@/lib/utils";
import type { Listing, Showing } from "@/lib/types";

const STATUS_NOTE: Partial<Record<Showing["status"], string>> = {
  Requested: "Waiting for the agent to approve",
  Confirmed: "See you there",
  Declined: "The agent declined this time",
  Cancelled: "Cancelled",
};

export function MyShowings() {
  const user = useUser();
  const showings = useResource(() => api.myShowings());
  const { isFlashing, flash } = useFlash();
  const listingsById = useListingIndex(user.role);
  const [cancelling, setCancelling] = useState<Showing | null>(null);
  const [busy, setBusy] = useState(false);

  const streamState = useShowingStream(async (event) => {
    const before = showings.data?.find((s) => s.id === event.showingId);
    const latest = await showings.reload();
    const after = latest?.find((s) => s.id === event.showingId);
    if (!after) return;
    flash(after.id);
    if (
      before &&
      before.status !== after.status &&
      (after.status === "Confirmed" || after.status === "Declined")
    ) {
      toast[after.status === "Confirmed" ? "success" : "warning"](`Showing ${after.status.toLowerCase()}`, {
        description: `${after.listingAddress} · ${listingTimeLabel(after)}`,
      });
    }
  });

  async function cancel(showing: Showing) {
    setBusy(true);
    try {
      const updated = await api.cancelShowing(showing.id);
      showings.setData(
        showings.data?.map((s) => (s.id === showing.id ? { ...s, ...updated, status: "Cancelled" } : s)),
      );
      toast.success("Showing cancelled", { description: showing.listingAddress });
      setCancelling(null);
    } catch (err) {
      toastError(err);
    } finally {
      setBusy(false);
    }
  }

  const sorted = showings.data ? sortShowings(showings.data) : [];
  const upcoming = sorted.filter((s) => !isPast(s));
  const past = sorted.filter(isPast);

  return (
    <>
      <PageHeader
        title="My showings"
        description={`Times in your zone (${user.timeZone}), with the listing's local time.`}
        actions={<LiveIndicator state={streamState} />}
      />

      {showings.error && !showings.data ? (
        <ErrorState message={showings.error.message} onRetry={showings.reload} />
      ) : !showings.data ? (
        <ListSkeleton />
      ) : sorted.length === 0 ? (
        <EmptyState
          title="No showings yet"
          description="Find a home and pick a time that works for you."
          action={
            <Button asChild>
              <Link href="/buyer">Find showings</Link>
            </Button>
          }
        />
      ) : (
        <div className="space-y-8">
          <ShowingList
            title="Upcoming"
            showings={upcoming}
            empty="Nothing upcoming."
            userZone={user.timeZone}
            isFlashing={isFlashing}
            listingsById={listingsById}
            onCancel={setCancelling}
          />
          {past.length > 0 ? (
            <ShowingList
              title="Past"
              showings={past}
              userZone={user.timeZone}
              isFlashing={isFlashing}
              listingsById={listingsById}
            />
          ) : null}
        </div>
      )}

      <Dialog
        open={cancelling !== null}
        onOpenChange={(open) => (!open && !busy ? setCancelling(null) : undefined)}
      >
        <DialogContent className="sm:max-w-sm">
          <DialogHeader>
            <DialogTitle>Cancel this showing?</DialogTitle>
            <DialogDescription>
              {cancelling ? `${cancelling.listingAddress}, ${listingTimeLabel(cancelling)}. ` : ""}The agent
              is notified and the slot opens up for other buyers.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setCancelling(null)} disabled={busy}>
              Keep it
            </Button>
            <Button variant="destructive" onClick={() => cancelling && cancel(cancelling)} disabled={busy}>
              {busy ? <Loader2 className="animate-spin" /> : null}
              Cancel showing
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

function ShowingList({
  title,
  showings,
  empty,
  userZone,
  isFlashing,
  listingsById,
  onCancel,
}: {
  title: string;
  showings: Showing[];
  empty?: string;
  userZone: string;
  isFlashing: (id: string) => boolean;
  listingsById: Map<string, Listing>;
  onCancel?: (showing: Showing) => void;
}) {
  return (
    <section className="space-y-3">
      <h2 className="text-sm font-medium tracking-wide text-muted-foreground uppercase">{title}</h2>
      {showings.length === 0 ? (
        <p className="text-sm text-muted-foreground">{empty}</p>
      ) : (
        <Card className="gap-0 divide-y py-0">
          {showings.map((s) => {
            const zonesDiffer = s.listingTimeZone !== userZone;
            const cancellable = onCancel && (s.status === "Requested" || s.status === "Confirmed");
            return (
              <div
                key={s.id}
                className={cn(
                  "flex items-start gap-3 px-4 py-3 transition-colors duration-700 sm:items-center",
                  isFlashing(s.id) && "bg-amber-100/70",
                  isPast(s) && "text-muted-foreground",
                )}
              >
                <Link href={`/buyer/listings/${s.listingId}`} tabIndex={-1} aria-hidden>
                  <ListingThumb listing={listingsById.get(s.listingId)} className="w-20 sm:w-24" />
                </Link>
                <div className="flex min-w-0 flex-1 flex-col gap-2 sm:flex-row sm:items-center sm:gap-3">
                  <div className="min-w-0 flex-1 space-y-0.5">
                    <div className="font-medium">{formatInZone(s.slotStartUtc, userZone)}</div>
                    {zonesDiffer ? (
                      <div className="text-xs text-muted-foreground">
                        At the listing: {listingTimeLabel(s)}
                      </div>
                    ) : null}
                    <div className="text-sm">
                      <Link href={`/buyer/listings/${s.listingId}`} className="hover:underline">
                        {s.listingAddress}, {s.listingCity}
                      </Link>
                      <span className="text-muted-foreground"> · with {s.agentName}</span>
                    </div>
                  </div>
                  <div className="flex items-center justify-between gap-3 sm:justify-end">
                    <div className="sm:text-right">
                      <StatusBadge status={s.status} />
                      {!isPast(s) && STATUS_NOTE[s.status] ? (
                        <div className="mt-1 text-xs text-muted-foreground">{STATUS_NOTE[s.status]}</div>
                      ) : null}
                    </div>
                    {cancellable ? (
                      <Button variant="outline" size="sm" onClick={() => onCancel(s)}>
                        Cancel
                      </Button>
                    ) : null}
                  </div>
                </div>
              </div>
            );
          })}
        </Card>
      )}
    </section>
  );
}
