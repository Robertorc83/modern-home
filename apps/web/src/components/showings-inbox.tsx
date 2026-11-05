"use client";

import { useEffect, useState } from "react";
import { Check, Loader2, X } from "lucide-react";
import { toast } from "sonner";
import { ListingThumb } from "@/components/listing/listing-thumb";
import { LiveIndicator } from "@/components/live-indicator";
import { PageHeader } from "@/components/page-header";
import { EmptyState, ErrorState, ListSkeleton } from "@/components/states";
import { StatusBadge } from "@/components/status-badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useUser } from "@/components/user-context";
import { useDebouncedCallback } from "@/hooks/use-debounced";
import { useFlash } from "@/hooks/use-flash";
import { useListingIndex } from "@/hooks/use-listing-index";
import { useResource } from "@/hooks/use-resource";
import { useShowingStream } from "@/hooks/use-showing-stream";
import { api, toastError } from "@/lib/api";
import { isPast, listingTimeLabel, sortShowings } from "@/lib/showings";
import { formatRelative } from "@/lib/time";
import { cn } from "@/lib/utils";
import type { Showing, ShowingStatus } from "@/lib/types";

type Tab = "Requested" | "Confirmed" | "All";
const TABS: Tab[] = ["Requested", "Confirmed", "All"];

const EMPTY_COPY: Record<Tab, { title: string; description: string }> = {
  Requested: {
    title: "No requests waiting",
    description:
      "New showing requests appear here the moment a buyer books, from the web or an AI assistant.",
  },
  Confirmed: { title: "No confirmed showings", description: "Approved showings will be listed here." },
  All: {
    title: "No showings yet",
    description: "Once buyers book your listings, every showing shows up here.",
  },
};

export function ShowingsInbox({
  title,
  description,
  canRespond,
  showAgent = false,
}: {
  title: string;
  description: string;
  canRespond: boolean;
  showAgent?: boolean;
}) {
  const [tab, setTab] = useState<Tab>("Requested");
  const [busy, setBusy] = useState<string | null>(null);
  // Requests that arrived live while another tab was open; shown as a badge instead of switching tabs.
  const [unseenRequests, setUnseenRequests] = useState(0);
  const showings = useResource(() => api.inbox());
  // A new request is the demo's key moment: keep the highlight on long enough to be noticed.
  const { isFlashing, flash } = useFlash(4000);
  const listingsById = useListingIndex(useUser().role);

  // Events can arrive in bursts; one reload after the last of them is enough to reconcile the list.
  const reloadSoon = useDebouncedCallback(() => void showings.reload(), 150);
  const streamState = useShowingStream(
    (event) => {
      // Toast and flash from the event payload itself, so a burst of events drops none of them.
      flash(event.showingId);
      if (event.operation?.toLowerCase() === "insert") {
        // Auto-confirmed listings skip the request step: the showing arrives already confirmed.
        toast.info(event.status === "Confirmed" ? "New showing booked" : "New showing request", {
          description: [event.buyerName, event.listingAddress].filter(Boolean).join(" · ") || undefined,
        });
        if (event.status === "Requested" && tab !== "Requested") setUnseenRequests((n) => n + 1);
      }
      reloadSoon();
    },
    () => void showings.reload(),
  );

  function changeTab(next: Tab) {
    setTab(next);
    if (next === "Requested") setUnseenRequests(0);
  }

  // "Requested 3 minutes ago" drifts while the page sits open; re-render it now and then.
  const [, setTick] = useState(0);
  useEffect(() => {
    const timer = setInterval(() => setTick((t) => t + 1), 30_000);
    return () => clearInterval(timer);
  }, []);

  async function respond(showing: Showing, action: "confirm" | "decline") {
    setBusy(`${showing.id}:${action}`);
    try {
      const updated =
        action === "confirm" ? await api.confirmShowing(showing.id) : await api.declineShowing(showing.id);
      const status: ShowingStatus = updated?.status ?? (action === "confirm" ? "Confirmed" : "Declined");
      showings.setData(showings.data?.map((s) => (s.id === showing.id ? { ...s, ...updated, status } : s)));
      toast.success(action === "confirm" ? "Showing confirmed" : "Showing declined", {
        description: `${showing.listingAddress} · ${listingTimeLabel(showing)}`,
      });
    } catch (err) {
      toastError(err);
      void showings.reload();
    } finally {
      setBusy(null);
    }
  }

  const all = showings.data ? sortShowings(showings.data) : [];
  const counts: Record<Tab, number> = {
    Requested: all.filter((s) => s.status === "Requested").length,
    Confirmed: all.filter((s) => s.status === "Confirmed").length,
    All: all.length,
  };
  const visible = tab === "All" ? all : all.filter((s) => s.status === tab);

  const hasActions = (s: Showing) => canRespond && s.status === "Requested" && !isPast(s);
  // No row the viewer can act on (admins, or a tab of confirmed showings): skip the empty column.
  const showActions = visible.some(hasActions);

  const actionsFor = (s: Showing) =>
    hasActions(s) ? (
      <div className="flex justify-end gap-2">
        <Button size="sm" variant="outline" disabled={busy !== null} onClick={() => respond(s, "decline")}>
          {busy === `${s.id}:decline` ? <Loader2 className="animate-spin" /> : <X />}
          Decline
        </Button>
        <Button size="sm" disabled={busy !== null} onClick={() => respond(s, "confirm")}>
          {busy === `${s.id}:confirm` ? <Loader2 className="animate-spin" /> : <Check />}
          Approve
        </Button>
      </div>
    ) : null;

  // Highlight for rows touched by a live event: an amber wash plus a left bar that reads at a glance.
  const flashClass = (s: Showing) =>
    isFlashing(s.id) && "bg-amber-100/70 shadow-[inset_4px_0_0_0_var(--color-amber-500)] hover:bg-amber-100/70";

  return (
    <>
      <PageHeader title={title} description={description} actions={<LiveIndicator state={streamState} />} />

      <Tabs value={tab} onValueChange={(v) => changeTab(v as Tab)} className="mb-4">
        <TabsList>
          {TABS.map((t) => (
            <TabsTrigger key={t} value={t}>
              {t}
              {showings.data ? (
                <span
                  className={cn(
                    "ml-1 rounded-full bg-muted px-1.5 text-xs tabular-nums text-muted-foreground",
                    t === "Requested" && unseenRequests > 0 && "bg-amber-200 font-semibold text-amber-950",
                  )}
                  aria-label={
                    t === "Requested" && unseenRequests > 0
                      ? `${counts[t]} requested, ${unseenRequests} new`
                      : undefined
                  }
                >
                  {counts[t]}
                </span>
              ) : null}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>

      {showings.error && !showings.data ? (
        <ErrorState message={showings.error.message} onRetry={showings.reload} />
      ) : !showings.data ? (
        <ListSkeleton />
      ) : visible.length === 0 ? (
        <EmptyState {...EMPTY_COPY[tab]} />
      ) : (
        <>
          {/* Narrow screens: stacked cards, so the actions never hide behind a horizontal scroll. */}
          <Card className="gap-0 divide-y py-0 md:hidden">
            {visible.map((s) => (
              <article
                key={s.id}
                aria-label={`${s.listingAddress}, ${listingTimeLabel(s)}`}
                className={cn(
                  "space-y-3 px-4 py-3 transition-colors duration-700",
                  flashClass(s),
                  isPast(s) && "text-muted-foreground",
                )}
              >
                <div className="flex items-start gap-3">
                  <ListingThumb listing={listingsById.get(s.listingId)} className="w-16 shrink-0" />
                  <div className="min-w-0 flex-1 space-y-0.5">
                    <div className="font-medium">{listingTimeLabel(s)}</div>
                    {/* Address only: with the city appended it truncates on narrow phones. */}
                    <div className="truncate text-sm">{s.listingAddress}</div>
                    <div className="text-xs text-muted-foreground">
                      {s.buyerName}
                      {showAgent ? ` · with ${s.agentName}` : ""} · requested {formatRelative(s.createdAt)}
                    </div>
                  </div>
                  <StatusBadge status={s.status} className="shrink-0" />
                </div>
                {actionsFor(s)}
              </article>
            ))}
          </Card>

          <Card className="hidden py-0 md:block">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="pl-4">When (listing time)</TableHead>
                  <TableHead>Listing</TableHead>
                  <TableHead>Buyer</TableHead>
                  {showAgent ? <TableHead>Agent</TableHead> : null}
                  <TableHead>Status</TableHead>
                  {showActions ? <TableHead className="pr-4 text-right">Actions</TableHead> : null}
                </TableRow>
              </TableHeader>
              <TableBody>
                {visible.map((s) => (
                  <TableRow
                    key={s.id}
                    className={cn("transition-colors duration-700", flashClass(s), isPast(s) && "text-muted-foreground")}
                  >
                    <TableCell className="pl-4">
                      <div className="font-medium">{listingTimeLabel(s)}</div>
                      <div className="text-xs text-muted-foreground">
                        Requested {formatRelative(s.createdAt)}
                      </div>
                    </TableCell>
                    <TableCell>
                      <div className="flex items-center gap-3">
                        <ListingThumb listing={listingsById.get(s.listingId)} className="w-14" />
                        <div>
                          <div>{s.listingAddress}</div>
                          <div className="text-xs text-muted-foreground">{s.listingCity}</div>
                        </div>
                      </div>
                    </TableCell>
                    <TableCell>{s.buyerName}</TableCell>
                    {showAgent ? <TableCell>{s.agentName}</TableCell> : null}
                    <TableCell>
                      <StatusBadge status={s.status} />
                    </TableCell>
                    {showActions ? <TableCell className="pr-4">{actionsFor(s)}</TableCell> : null}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Card>
        </>
      )}
    </>
  );
}
