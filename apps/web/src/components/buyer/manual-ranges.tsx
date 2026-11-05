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
  return ranges.map((r) => ({ start: `${r.date}T${r.start}`, end: `${r.date}T${r.end}` }));
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
        return (
          <div key={range.id} className="space-y-1">
            <div className="flex flex-wrap items-center gap-2 rounded-lg border bg-muted/20 p-2">
              <Input
                type="date"
                aria-label="Date"
                className="h-7 w-36 bg-background"
                min={today}
                value={range.date}
                aria-invalid={Boolean(error) || undefined}
                onChange={(e) => update(range.id, { date: e.target.value })}
              />
              <span className="flex-1 text-sm text-muted-foreground">
                {range.date ? formatLocalDate(range.date, { weekday: "long" }) : null}
              </span>
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
                  onChange={(start) => update(range.id, { start })}
                />
                <span className="text-sm text-muted-foreground">to</span>
                <TimeSelect label="To" value={range.end} onChange={(end) => update(range.id, { end })} />
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
