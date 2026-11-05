"use client";

import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { formatLocalTime, quarterHours } from "@/lib/time";

const TIMES = quarterHours();
const TIMES_WITH_MIDNIGHT = quarterHours(true);

function timeLabel(hhmm: string): string {
  return hhmm === "24:00" ? "Midnight (end of day)" : formatLocalTime(hhmm);
}

export function TimeSelect({
  value,
  onChange,
  invalid,
  label,
  includeMidnight = false,
}: {
  value: string;
  onChange: (value: string) => void;
  invalid?: boolean;
  label: string;
  /** Offer "24:00" so a window can run to the end of the day (use for end times only). */
  includeMidnight?: boolean;
}) {
  const base = includeMidnight ? TIMES_WITH_MIDNIGHT : TIMES;
  // A saved value off the 15-minute grid (e.g. "09:05" set through the API, which keeps "24:00" as
  // is) must still be selectable. An empty value never becomes an item: Radix rejects "" as a
  // SelectItem value, and the trigger simply shows nothing selected.
  const times = !value || base.includes(value) ? base : [...base, value].sort();
  return (
    <Select value={value} onValueChange={onChange}>
      <SelectTrigger size="sm" className="w-28" aria-label={label} aria-invalid={invalid || undefined}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent className="max-h-72">
        {times.map((t) => (
          <SelectItem key={t} value={t}>
            {timeLabel(t)}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
