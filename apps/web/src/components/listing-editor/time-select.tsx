"use client";

import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { formatLocalTime, quarterHours } from "@/lib/time";

const TIMES = quarterHours();

export function TimeSelect({
  value,
  onChange,
  invalid,
  label,
}: {
  value: string;
  onChange: (value: string) => void;
  invalid?: boolean;
  label: string;
}) {
  return (
    <Select value={value} onValueChange={onChange}>
      <SelectTrigger size="sm" className="w-28" aria-label={label} aria-invalid={invalid || undefined}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent className="max-h-72">
        {TIMES.map((t) => (
          <SelectItem key={t} value={t}>
            {formatLocalTime(t)}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
