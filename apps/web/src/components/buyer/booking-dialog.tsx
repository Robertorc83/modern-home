"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { CalendarCheck, Clock, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { SlotChip } from "@/components/slots";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { ApiError, api } from "@/lib/api";
import { formatBedsBaths, formatPrice } from "@/lib/format";
import { formatLocalDate } from "@/lib/time";
import type { Listing, Showing, Slot, SlotHold } from "@/lib/types";

export interface BookingTarget {
  listing: Listing;
  slot: Slot;
}

type HoldState =
  | { kind: "holding" }
  | { kind: "held"; hold: SlotHold }
  | { kind: "unheld" } // the API has no hold for us (e.g. holds disabled); booking still works
  | { kind: "conflict"; message: string; alternatives: Slot[] } // 409: someone else has it
  | { kind: "failed"; message: string }; // network / server error: worth retrying

// Releases run before the next hold is taken, so a quick re-open never races its own hold.
let pendingRelease: Promise<unknown> = Promise.resolve();

function release(holdPromise: Promise<SlotHold | null>) {
  pendingRelease = holdPromise
    .then((hold) => (hold ? api.releaseHold(hold.id) : undefined))
    .catch(() => undefined);
}

const HOLD_TTL_SECONDS = 5 * 60;

function useCountdown(expiresAt: string | null): number | null {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!expiresAt) return;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [expiresAt]);
  if (!expiresAt) return null;
  // Clamped to the hold TTL so a client clock running behind the server never shows more than 5:00.
  return Math.min(HOLD_TTL_SECONDS, Math.max(0, Math.round((new Date(expiresAt).getTime() - now) / 1000)));
}

function mmss(seconds: number): string {
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}

export function BookingDialog({
  target,
  searchTimeZone,
  onClose,
  onBooked,
  onSlotsChanged,
  onHeld,
}: {
  target: BookingTarget | null;
  searchTimeZone: string;
  onClose: () => void;
  onBooked: (showing: Showing) => void;
  /** The slot was taken by someone else (hold or booking conflict): the caller's slot list is stale. */
  onSlotsChanged?: () => void;
  /** A hold was taken (possibly on an alternative slot after a conflict): the race is over. */
  onHeld?: () => void;
}) {
  const [slot, setSlot] = useState<Slot | null>(target?.slot ?? null);
  const [holdState, setHoldState] = useState<HoldState>({ kind: "holding" });
  const [attempt, setAttempt] = useState(0);
  const [booking, setBooking] = useState(false);
  const [bookError, setBookError] = useState<{ message: string; alternatives: Slot[] } | null>(null);
  const booked = useRef(false);
  const router = useRouter();
  const slotsChanged = useRef(onSlotsChanged);
  const held = useRef(onHeld);
  useEffect(() => {
    slotsChanged.current = onSlotsChanged;
    held.current = onHeld;
  });

  // Reset when a new chip is clicked.
  const [prevTarget, setPrevTarget] = useState(target);
  if (target !== prevTarget) {
    setPrevTarget(target);
    setSlot(target?.slot ?? null);
    setBookError(null);
    setHoldState({ kind: "holding" });
  }

  const listing = target?.listing;

  // Take a hold for the selected slot; release it when the slot changes or the dialog closes.
  useEffect(() => {
    if (!listing || !slot) return;
    let active = true;
    booked.current = false;

    const holdPromise: Promise<SlotHold | null> = pendingRelease.then(() =>
      api.hold(listing.id, slot.startUtc, searchTimeZone).then(
        (hold) => {
          if (active) {
            setHoldState({ kind: "held", hold });
            held.current?.();
          }
          return hold;
        },
        (err: unknown) => {
          if (!active) return null;
          if (err instanceof ApiError && err.status === 409) {
            setHoldState({ kind: "conflict", message: err.message, alternatives: err.alternatives });
            slotsChanged.current?.();
          } else if (err instanceof ApiError && (err.status === 405 || err.status === 501)) {
            // Holds are not supported by this API; booking still works.
            setHoldState({ kind: "unheld" });
          } else if (err instanceof ApiError && err.status === 404) {
            setHoldState({ kind: "failed", message: "This listing is no longer available." });
          } else {
            setHoldState({
              kind: "failed",
              message: err instanceof Error ? err.message : "Could not reach the server.",
            });
          }
          return null;
        },
      ),
    );

    return () => {
      active = false;
      if (!booked.current) release(holdPromise);
    };
  }, [listing, slot, attempt, searchTimeZone]);

  const expiresAt = holdState.kind === "held" ? holdState.hold.expiresAt : null;
  const secondsLeft = useCountdown(expiresAt);
  const expired = holdState.kind === "held" && secondsLeft === 0;

  async function confirm() {
    if (!listing || !slot) return;
    setBooking(true);
    setBookError(null);
    try {
      const showing = await api.book(listing.id, slot.startUtc, searchTimeZone);
      booked.current = true;
      toast.success(showing.status === "Confirmed" ? "Showing confirmed" : "Showing requested", {
        description: `${showing.listingAddress} · ${showing.listingLocalLabel}`,
        action: { label: "My showings", onClick: () => router.push("/buyer/showings") },
      });
      onBooked(showing);
    } catch (err) {
      if (err instanceof ApiError && (err.status === 409 || err.status === 422)) {
        setBookError({ message: err.message, alternatives: err.alternatives });
        if (err.status === 409) onSlotsChanged?.();
      } else {
        setBookError({ message: err instanceof Error ? err.message : "Booking failed.", alternatives: [] });
      }
    } finally {
      setBooking(false);
    }
  }

  const alternatives = bookError?.alternatives.length
    ? bookError.alternatives
    : holdState.kind === "conflict"
      ? holdState.alternatives
      : [];
  const zonesDiffer = listing ? listing.timeZone !== searchTimeZone : false;
  // A booking error with alternatives means this slot is gone: pick another. One without (network,
  // 5xx) is transient, so the hold is still ours and the same slot can be confirmed again.
  const retryable = bookError !== null && bookError.alternatives.length === 0;
  const canConfirm =
    !booking &&
    !expired &&
    (holdState.kind === "held" || holdState.kind === "unheld") &&
    (!bookError || retryable);

  return (
    // Closing while the booking request is in flight would release the hold under it; wait instead.
    <Dialog open={target !== null} onOpenChange={(open) => (!open && !booking ? onClose() : undefined)}>
      <DialogContent className="sm:max-w-md">
        {listing && slot ? (
          <>
            <DialogHeader>
              <DialogTitle>Book a showing</DialogTitle>
              <DialogDescription>
                {listing.address}, {listing.city} · {formatPrice(listing.price, listing.timeZone)} ·{" "}
                {formatBedsBaths(listing)}
              </DialogDescription>
            </DialogHeader>

            <div className="space-y-1 rounded-lg border p-4">
              <div className="flex items-center gap-2 text-lg font-semibold">
                <CalendarCheck className="size-5 text-muted-foreground" />
                {formatLocalDate(slot.localStart)} · {slot.label}
              </div>
              <p className="text-sm text-muted-foreground">
                {zonesDiffer
                  ? `Your time (${searchTimeZone}). At the listing: ${slot.listingLabel}.`
                  : `Listing time (${listing.timeZone}).`}
              </p>
              <p className="text-sm text-muted-foreground">With {listing.ownerName}</p>
            </div>

            {bookError ? null : (
              <HoldStatus
                state={holdState}
                secondsLeft={secondsLeft}
                expired={expired}
                onRetry={() => {
                  setHoldState({ kind: "holding" });
                  setAttempt((a) => a + 1);
                }}
              />
            )}

            {bookError ? (
              <Alert variant="destructive">
                <AlertTitle>Couldn&apos;t book this slot</AlertTitle>
                <AlertDescription className={retryable ? "flex items-center justify-between gap-2" : undefined}>
                  <span>{bookError.message}</span>
                  {retryable ? (
                    <Button size="sm" variant="outline" onClick={() => setBookError(null)}>
                      Try again
                    </Button>
                  ) : null}
                </AlertDescription>
              </Alert>
            ) : null}

            {alternatives.length > 0 ? (
              <div className="space-y-2">
                <p className="text-sm font-medium">Nearest open slots</p>
                <div className="flex flex-wrap gap-1.5">
                  {alternatives.map((alt) => (
                    <SlotChip
                      key={alt.startUtc}
                      slot={alt}
                      label={`${formatLocalDate(alt.localStart)} · ${alt.label}`}
                      showListingTime={zonesDiffer}
                      onSelect={(next) => {
                        setBookError(null);
                        setHoldState({ kind: "holding" });
                        setSlot(next);
                      }}
                    />
                  ))}
                </div>
              </div>
            ) : null}

            <DialogFooter>
              <Button variant="outline" onClick={onClose} disabled={booking}>
                Cancel
              </Button>
              <Button onClick={confirm} disabled={!canConfirm}>
                {booking ? <Loader2 className="animate-spin" /> : null}
                Confirm booking
              </Button>
            </DialogFooter>
            <p className="text-center text-xs text-muted-foreground">
              {listing.showingRules?.autoConfirm ? (
                "This listing confirms bookings instantly."
              ) : (
                <>
                  The agent approves requests. Follow the status live in{" "}
                  <Link href="/buyer/showings" className="underline underline-offset-4">
                    My showings
                  </Link>
                  .
                </>
              )}
            </p>
          </>
        ) : null}
      </DialogContent>
    </Dialog>
  );
}

function HoldStatus({
  state,
  secondsLeft,
  expired,
  onRetry,
}: {
  state: HoldState;
  secondsLeft: number | null;
  expired: boolean;
  onRetry: () => void;
}) {
  if (state.kind === "holding") {
    return (
      <p className="flex items-center gap-2 text-sm text-muted-foreground">
        <Loader2 className="size-4 animate-spin" /> Holding this slot for you…
      </p>
    );
  }
  if (expired) {
    return (
      <Alert>
        <AlertDescription className="flex items-center justify-between gap-2">
          Your hold expired, so others can book this slot again.
          <Button size="sm" variant="outline" onClick={onRetry}>
            Hold again
          </Button>
        </AlertDescription>
      </Alert>
    );
  }
  if (state.kind === "held" && secondsLeft !== null) {
    return (
      <p className="flex items-center gap-2 text-sm">
        <Clock className="size-4 text-emerald-600" />
        <span>
          Held for you for <span className="font-semibold tabular-nums">{mmss(secondsLeft)}</span>
          {". Nobody else can book it meanwhile."}
        </span>
      </p>
    );
  }
  if (state.kind === "conflict") {
    return (
      <Alert variant="destructive">
        <AlertTitle>This slot is no longer available</AlertTitle>
        <AlertDescription>{state.message}</AlertDescription>
      </Alert>
    );
  }
  if (state.kind === "failed") {
    return (
      <Alert variant="destructive">
        <AlertTitle>Couldn&apos;t reserve the slot</AlertTitle>
        <AlertDescription className="flex items-center justify-between gap-2">
          <span>{state.message}</span>
          <Button size="sm" variant="outline" onClick={onRetry}>
            Retry
          </Button>
        </AlertDescription>
      </Alert>
    );
  }
  return null;
}
