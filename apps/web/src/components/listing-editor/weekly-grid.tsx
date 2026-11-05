"use client";

import { Plus, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { toMinutes } from "@/lib/time";
import { DAYS_OF_WEEK, type DayOfWeek, type WeeklyWindow } from "@/lib/types";
import { TimeSelect } from "./time-select";

/** Returns a message per day with an invalid window (end before start, or overlapping). */
export function validateWeekly(weekly: WeeklyWindow[]): Partial<Record<DayOfWeek, string>> {
  const errors: Partial<Record<DayOfWeek, string>> = {};
  for (const day of DAYS_OF_WEEK) {
    const windows = weekly
      .filter((w) => w.dayOfWeek === day)
      .map((w) => ({ start: toMinutes(w.start), end: toMinutes(w.end) }))
      .sort((a, b) => a.start - b.start);
    if (windows.some((w) => w.end <= w.start)) {
      errors[day] = "Each window must end after it starts.";
      continue;
    }
    for (let i = 1; i < windows.length; i++) {
      if (windows[i].start < windows[i - 1].end) {
        errors[day] = "Windows on the same day must not overlap.";
        break;
      }
    }
  }
  return errors;
}

function pad(minutes: number): string {
  return `${String(Math.floor(minutes / 60)).padStart(2, "0")}:${String(minutes % 60).padStart(2, "0")}`;
}

function nextWindow(existing: WeeklyWindow[], day: DayOfWeek): WeeklyWindow {
  const latestEnd = Math.max(0, ...existing.map((w) => toMinutes(w.end)));
  if (existing.length === 0 || latestEnd > 20 * 60) return { dayOfWeek: day, start: "09:00", end: "12:00" };
  const start = latestEnd + 60;
  return { dayOfWeek: day, start: pad(start), end: pad(Math.min(start + 180, 23 * 60 + 45)) };
}

export function WeeklyGrid({
  weekly,
  onChange,
  errors,
}: {
  weekly: WeeklyWindow[];
  onChange: (weekly: WeeklyWindow[]) => void;
  errors: Partial<Record<DayOfWeek, string>>;
}) {
  function update(index: number, patch: Partial<WeeklyWindow>) {
    onChange(weekly.map((w, i) => (i === index ? { ...w, ...patch } : w)));
  }

  return (
    // The id and tabIndex let the editor scroll to and focus the grid when a window is invalid.
    <div id="weekly-grid" tabIndex={-1} className="divide-y rounded-lg border outline-none focus-visible:ring-2">
      {DAYS_OF_WEEK.map((day) => {
        const entries = weekly
          .map((w, index) => ({ w, index }))
          .filter(({ w }) => w.dayOfWeek === day)
          .sort((a, b) => toMinutes(a.w.start) - toMinutes(b.w.start));
        const error = errors[day];
        return (
          <div
            key={day}
            className={cn(
              "flex flex-col gap-2 px-3 py-2.5 sm:flex-row sm:items-start",
              error && "bg-destructive/5",
            )}
          >
            <div className="w-24 shrink-0 pt-1 text-sm font-medium">{day}</div>
            <div className="flex flex-1 flex-col gap-2">
              {entries.length === 0 ? (
                <span className="pt-1 text-sm text-muted-foreground">No showings</span>
              ) : (
                entries.map(({ w, index }) => (
                  <div key={index} className="flex flex-wrap items-center gap-2">
                    <TimeSelect
                      label={`${day} window start`}
                      value={w.start}
                      invalid={Boolean(error)}
                      onChange={(start) => update(index, { start })}
                    />
                    <span className="text-sm text-muted-foreground">to</span>
                    <TimeSelect
                      label={`${day} window end`}
                      value={w.end}
                      invalid={Boolean(error)}
                      includeMidnight
                      onChange={(end) => update(index, { end })}
                    />
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon-sm"
                      aria-label={`Remove ${day} window`}
                      onClick={() => onChange(weekly.filter((_, i) => i !== index))}
                    >
                      <X />
                    </Button>
                  </div>
                ))
              )}
              {error ? <p className="text-xs text-destructive">{error}</p> : null}
            </div>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="self-start"
              onClick={() =>
                onChange([
                  ...weekly,
                  nextWindow(
                    entries.map((e) => e.w),
                    day,
                  ),
                ])
              }
            >
              <Plus />
              Add window
            </Button>
          </div>
        );
      })}
    </div>
  );
}
