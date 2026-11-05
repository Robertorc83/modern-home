"use client";

import { Plus, X } from "lucide-react";
import { TimeSelect } from "@/components/listing-editor/time-select";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { addDays, formatLocalDate, toMinutes } from "@/lib/time";
import type { LocalRange } from "@/lib/types";

export interface ManualRange {
  id: number;
  date: string;
  start: string;
  end: string;
}

export function rangeError(range: ManualRange, today: string): string | null {
  if (!range.date) return "Pick a date.";
  if (range.date < today) return "That date has passed.";
  if (toMinutes(range.end) <= toMinutes(range.start)) return "End must be after start.";
  return null;
}

export function toLocalRanges(ranges: ManualRange[]): LocalRange[] {
  // "24:00" (end of day) is not a valid local time; it is midnight at the start of the next day.
  return ranges.map((r) => ({
    start: `${r.date}T${r.start}`,
    end: r.end === "24:00" ? `${addDays(r.date, 1)}T00:00` : `${r.date}T${r.end}`,
  }));
}

let nextId = 1;
export function newRange(date: string, start = "09:00", end = "12:00"): ManualRange {
  return { id: nextId++, date, start, end };
}

export function ManualRanges({
  ranges,
  onChange,
  today,
}: {
  ranges: ManualRange[];
  onChange: (ranges: ManualRange[]) => void;
  today: string;
}) {
  function update(id: number, patch: Partial<ManualRange>) {
    onChange(ranges.map((r) => (r.id === id ? { ...r, ...patch } : r)));
  }

  function add() {
    const last = ranges.at(-1);
    onChange([
      ...ranges,
      last ? newRange(addDays(last.date || today, 1), last.start, last.end) : newRange(addDays(today, 1)),
    ]);
  }

  return (
    <div className="space-y-2">
      {ranges.map((range) => {
        const error = rangeError(range, today);
        // Only the field at fault is outlined: a bad date marks the date input, a bad time order
        // marks the time selects (the date is fine then).
        const dateInvalid = error !== null && (!range.date || range.date < today);
        const timeInvalid = error !== null && !dateInvalid;
        return (
          <div key={range.id} className="space-y-1">
            <div className="flex flex-wrap items-center gap-2 rounded-lg border bg-muted/20 p-2">
              {/* The readable date is the primary text; the native picker (whose display format
                  follows the browser locale, e.g. DD/MM) is kept for input but shown as secondary. */}
              <label className="flex min-w-0 flex-1 flex-col gap-0.5">
                <span className="truncate text-sm font-medium">
                  {range.date ? formatLocalDate(range.date, { weekday: "long" }) : "Pick a date"}
                </span>
                <Input
                  type="date"
                  aria-label="Date"
                  lang="en-US"
                  className="h-7 w-36 bg-background text-xs text-muted-foreground"
                  min={today}
                  value={range.date}
                  aria-invalid={dateInvalid || undefined}
                  onChange={(e) => update(range.id, { date: e.target.value })}
                />
              </label>
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                aria-label="Remove range"
                disabled={ranges.length === 1}
                onClick={() => onChange(ranges.filter((r) => r.id !== range.id))}
              >
                <X />
              </Button>
              <div className="flex w-full items-center gap-2">
                <TimeSelect
                  label="From"
                  value={range.start}
                  invalid={timeInvalid}
                  onChange={(start) => update(range.id, { start })}
                />
                <span className="text-sm text-muted-foreground">to</span>
                <TimeSelect
                  label="To"
                  value={range.end}
                  includeMidnight
                  invalid={timeInvalid}
                  onChange={(end) => update(range.id, { end })}
                />
              </div>
            </div>
            {error ? <p className="text-xs text-destructive">{error}</p> : null}
          </div>
        );
      })}
      <Button type="button" variant="ghost" size="sm" onClick={add} className="-ml-2">
        <Plus />
        Add another time
      </Button>
    </div>
  );
}
