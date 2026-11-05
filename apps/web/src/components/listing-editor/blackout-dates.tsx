"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { formatLocalDate } from "@/lib/time";

export function BlackoutDates({
  dates,
  onChange,
  minDate,
}: {
  dates: string[];
  onChange: (dates: string[]) => void;
  minDate: string;
}) {
  const [draft, setDraft] = useState("");

  function add() {
    if (!draft || dates.includes(draft)) {
      setDraft("");
      return;
    }
    onChange([...dates, draft].sort());
    setDraft("");
  }

  return (
    <div className="space-y-3">
      <div className="flex gap-2">
        <Input
          type="date"
          aria-label="Blackout date"
          className="w-44"
          min={minDate}
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              add();
            }
          }}
        />
        <Button type="button" variant="outline" onClick={add} disabled={!draft}>
          Add date
        </Button>
      </div>
      {dates.length === 0 ? (
        <p className="text-sm text-muted-foreground">No blackout dates.</p>
      ) : (
        <div className="flex flex-wrap gap-2">
          {dates.map((date) => (
            <span
              key={date}
              className="inline-flex items-center gap-1 rounded-full border bg-muted/50 py-0.5 pr-1 pl-3 text-sm"
            >
              {formatLocalDate(date, { year: "numeric" })}
              <button
                type="button"
                className="rounded-full p-0.5 text-muted-foreground hover:bg-muted hover:text-foreground"
                aria-label={`Remove ${date}`}
                onClick={() => onChange(dates.filter((d) => d !== date))}
              >
                <X className="size-3.5" />
              </button>
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
