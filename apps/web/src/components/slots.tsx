"use client";

import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";
import type { DayGroup, Slot } from "@/lib/types";

const chipClass =
  "inline-flex h-8 items-center rounded-md border bg-background px-2.5 text-sm font-medium tabular-nums transition-colors";

/** A bookable (or read-only) slot. When the listing is in another zone, shows its local time too. */
export function SlotChip({
  slot,
  label,
  showListingTime,
  onSelect,
}: {
  slot: Slot;
  label?: string;
  showListingTime?: boolean;
  onSelect?: (slot: Slot) => void;
}) {
  const text = label ?? slot.label;
  const chip = onSelect ? (
    <button
      type="button"
      onClick={() => onSelect(slot)}
      className={cn(
        chipClass,
        "hover:border-primary hover:bg-primary hover:text-primary-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none",
        showListingTime && "h-auto flex-col items-start py-1 leading-tight",
      )}
    >
      <span>{text}</span>
      {showListingTime ? (
        <span className="text-[11px] font-normal opacity-70">{slot.listingLabel} local</span>
      ) : null}
    </button>
  ) : (
    <span className={cn(chipClass, "text-muted-foreground")}>{text}</span>
  );

  if (!showListingTime) return chip;
  return (
    <Tooltip>
      <TooltipTrigger asChild>{chip}</TooltipTrigger>
      <TooltipContent>At the listing: {slot.listingLabel}</TooltipContent>
    </Tooltip>
  );
}

export function SlotDays({
  days,
  showListingTime,
  onSelect,
  maxDays,
}: {
  days: DayGroup[];
  showListingTime?: boolean;
  onSelect?: (slot: Slot) => void;
  maxDays?: number;
}) {
  const visible = maxDays ? days.slice(0, maxDays) : days;
  const hidden = days.length - visible.length;
  return (
    <div className="space-y-3">
      {visible.map((day) => (
        <div key={day.date} className="space-y-1.5">
          <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{day.label}</div>
          <div className="flex flex-wrap gap-1.5">
            {day.slots.map((slot) => (
              <SlotChip
                key={slot.startUtc}
                slot={slot}
                showListingTime={showListingTime}
                onSelect={onSelect}
              />
            ))}
          </div>
        </div>
      ))}
      {hidden > 0 ? (
        <p className="text-xs text-muted-foreground">
          +{hidden} more {hidden === 1 ? "day" : "days"}
        </p>
      ) : null}
    </div>
  );
}
