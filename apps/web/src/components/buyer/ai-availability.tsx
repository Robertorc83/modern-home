"use client";

import { useState } from "react";
import { Loader2, Search, Wand2, X } from "lucide-react";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { useResource } from "@/hooks/use-resource";
import { toast } from "sonner";
import { ApiError, api, toastError } from "@/lib/api";
import { formatLocalRange, formatLocalTime } from "@/lib/time";
import { shortDay } from "@/lib/format";
import type { LocalRange, ParsedAvailability } from "@/lib/types";
import { ProviderBadge } from "./provider-badge";

const PLACEHOLDER = "weekday evenings after 6 except Thursday, Saturday morning";
/** Ranges visible in the review list before it scrolls. */
const VISIBLE_RANGES = 12;

export function AiAvailability({
  timeZone,
  parsed,
  onParsed,
  onSearch,
  searching,
}: {
  timeZone: string;
  parsed: ParsedAvailability | null;
  onParsed: (parsed: ParsedAvailability | null) => void;
  onSearch: (ranges: LocalRange[]) => void;
  searching: boolean;
}) {
  const [text, setText] = useState("");
  const [parsing, setParsing] = useState(false);
  const provider = useResource(() => api.aiProvider());

  const empty = text.trim() === "";

  async function parse() {
    if (empty) return;
    setParsing(true);
    try {
      onParsed(await api.parseAvailability(text.trim(), timeZone));
    } catch (err) {
      if (err instanceof ApiError && err.status === 429) {
        // Per-user limit: say so by the problem's title; the text stays so the user can retry later.
        toast.error(err.problem.title || "Too many requests", { description: err.problem.detail });
      } else {
        toastError(err);
      }
    } finally {
      setParsing(false);
    }
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between gap-2">
        <span className="text-sm text-muted-foreground">Describe when you&apos;re free</span>
        <ProviderBadge provider={parsed?.provider ?? provider.data?.provider} />
      </div>
      <Textarea
        rows={3}
        value={text}
        placeholder={PLACEHOLDER}
        onChange={(e) => setText(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter" && (e.metaKey || e.ctrlKey)) {
            e.preventDefault();
            void parse();
          }
        }}
      />
      <div className="flex flex-wrap items-center gap-3">
        <Button type="button" variant="outline" onClick={parse} disabled={parsing || empty}>
          {parsing ? <Loader2 className="animate-spin" /> : <Wand2 />}
          Parse
        </Button>
        {empty ? (
          <Button
            type="button"
            variant="link"
            size="sm"
            className="h-auto px-0"
            onClick={() => setText(PLACEHOLDER)}
          >
            Try an example
          </Button>
        ) : null}
      </div>

      {parsed ? (
        <div className="space-y-3 rounded-lg border bg-muted/30 p-3">
          <div className="space-y-1">
            <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Understood as</p>
            <p className="text-sm font-medium">{parsed.summary || "No availability found in that text."}</p>
          </div>

          {parsed.windows.length > 0 ? (
            <div className="flex flex-wrap gap-1.5">
              {parsed.windows.map((w, i) => (
                <span key={i} className="rounded-md border bg-background px-2 py-0.5 text-xs">
                  {w.days.map(shortDay).join(", ")} · {formatLocalTime(w.start)}–{formatLocalTime(w.end)}
                </span>
              ))}
            </div>
          ) : null}

          {parsed.warnings.length > 0 ? (
            <Alert>
              <AlertDescription>
                <ul className="list-disc pl-4">
                  {parsed.warnings.map((w) => (
                    <li key={w}>{w}</li>
                  ))}
                </ul>
              </AlertDescription>
            </Alert>
          ) : null}

          <div className="space-y-1">
            <div className="flex items-baseline justify-between gap-2">
              <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Review the dates</p>
              <p className="text-xs text-muted-foreground">
                {parsed.ranges.length} {parsed.ranges.length === 1 ? "date" : "dates"}
                {parsed.ranges.length > VISIBLE_RANGES ? " · scroll for the rest" : ""}
              </p>
            </div>
            {parsed.ranges.length === 0 ? (
              <p className="text-sm text-muted-foreground">No concrete times. Try rephrasing.</p>
            ) : (
              // Tall enough for VISIBLE_RANGES rows (28px + 4px gap each) before it scrolls.
              <ul className="max-h-96 space-y-1 overflow-y-auto pr-1">
                {parsed.ranges.map((range, i) => (
                  <li
                    key={`${range.start}-${range.end}`}
                    className="flex items-center justify-between rounded-md bg-background px-2 py-1 text-sm"
                  >
                    <span className="tabular-nums">{formatLocalRange(range.start, range.end)}</span>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon-xs"
                      aria-label="Remove this range"
                      onClick={() => onParsed({ ...parsed, ranges: parsed.ranges.filter((_, j) => j !== i) })}
                    >
                      <X />
                    </Button>
                  </li>
                ))}
              </ul>
            )}
          </div>

          <Button
            type="button"
            className="w-full"
            disabled={parsed.ranges.length === 0 || searching}
            onClick={() => onSearch(parsed.ranges)}
          >
            {searching ? <Loader2 className="animate-spin" /> : <Search />}
            Search with these
          </Button>
        </div>
      ) : null}
    </div>
  );
}
