"use client";

import { useState } from "react";
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
  const showings = useResource(() => api.inbox());
  const { isFlashing, flash } = useFlash();
  const listingsById = useListingIndex(useUser().role);

  const streamState = useShowingStream(async (event) => {
    const latest = await showings.reload();
    flash(event.showingId);
    if (event.operation?.toLowerCase() === "insert") {
      const s = latest?.find((x) => x.id === event.showingId);
      toast.info("New showing request", {
        description: s
          ? `${s.buyerName} · ${s.listingAddress} · ${listingTimeLabel(s)}`
          : [event.buyerName, event.listingAddress].filter(Boolean).join(" · ") || undefined,
      });
      if (s && s.status === "Requested") setTab((t) => (t === "Confirmed" ? "Requested" : t));
    }
  });

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

  return (
    <>
      <PageHeader title={title} description={description} actions={<LiveIndicator state={streamState} />} />

      <Tabs value={tab} onValueChange={(v) => setTab(v as Tab)} className="mb-4">
        <TabsList>
          {TABS.map((t) => (
            <TabsTrigger key={t} value={t}>
              {t}
              {showings.data ? (
                <span className="ml-1 rounded-full bg-muted px-1.5 text-xs tabular-nums text-muted-foreground">
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
        <Card className="py-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="pl-4">When (listing time)</TableHead>
                <TableHead>Listing</TableHead>
                <TableHead>Buyer</TableHead>
                {showAgent ? <TableHead>Agent</TableHead> : null}
                <TableHead>Status</TableHead>
                {canRespond ? <TableHead className="pr-4 text-right">Actions</TableHead> : null}
              </TableRow>
            </TableHeader>
            <TableBody>
              {visible.map((s) => (
                <TableRow
                  key={s.id}
                  className={cn(
                    "transition-colors duration-700",
                    isFlashing(s.id) && "bg-amber-100/70 hover:bg-amber-100/70",
                    isPast(s) && "text-muted-foreground",
                  )}
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
                  {canRespond ? (
                    <TableCell className="pr-4">
                      {s.status === "Requested" && !isPast(s) ? (
                        <div className="flex justify-end gap-2">
                          <Button
                            size="sm"
                            variant="outline"
                            disabled={busy !== null}
                            onClick={() => respond(s, "decline")}
                          >
                            {busy === `${s.id}:decline` ? <Loader2 className="animate-spin" /> : <X />}
                            Decline
                          </Button>
                          <Button size="sm" disabled={busy !== null} onClick={() => respond(s, "confirm")}>
                            {busy === `${s.id}:confirm` ? <Loader2 className="animate-spin" /> : <Check />}
                            Approve
                          </Button>
                        </div>
                      ) : null}
                    </TableCell>
                  ) : null}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}
    </>
  );
}
