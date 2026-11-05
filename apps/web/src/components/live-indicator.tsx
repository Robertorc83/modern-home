import { cn } from "@/lib/utils";
import type { StreamState } from "@/hooks/use-showing-stream";

const LABELS: Record<StreamState, string> = {
  live: "Live",
  connecting: "Connecting…",
  offline: "Offline",
};

export function LiveIndicator({ state }: { state: StreamState }) {
  return (
    <span
      className="inline-flex items-center gap-1.5 rounded-full border bg-card px-2.5 py-1 text-xs font-medium"
      title={
        state === "live"
          ? "Updates arrive in real time from the database change stream"
          : state === "connecting"
            ? "Connecting to live updates"
            : "Live updates unavailable; reload to retry"
      }
    >
      <span className="relative flex size-2">
        {state === "live" ? (
          <span className="absolute inline-flex size-full animate-ping rounded-full bg-emerald-500 opacity-60" />
        ) : null}
        <span
          className={cn(
            "relative inline-flex size-2 rounded-full",
            state === "live"
              ? "bg-emerald-500"
              : state === "connecting"
                ? "bg-amber-400"
                : "bg-muted-foreground",
          )}
        />
      </span>
      {LABELS[state]}
    </span>
  );
}
