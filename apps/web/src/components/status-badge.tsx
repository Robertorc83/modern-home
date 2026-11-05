import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import type { ListingStatus, ShowingStatus } from "@/lib/types";

const STYLES: Record<ShowingStatus | ListingStatus, string> = {
  Requested: "bg-amber-100 text-amber-900",
  Confirmed: "bg-emerald-100 text-emerald-900",
  Declined: "bg-rose-100 text-rose-900",
  Cancelled: "bg-muted text-muted-foreground line-through decoration-1",
  Active: "bg-emerald-100 text-emerald-900",
  Draft: "bg-slate-100 text-slate-700",
  Off: "bg-muted text-muted-foreground",
};

export function StatusBadge({
  status,
  className,
}: {
  status: ShowingStatus | ListingStatus;
  className?: string;
}) {
  return (
    <Badge variant="secondary" className={cn(STYLES[status] ?? "", className)}>
      {status}
    </Badge>
  );
}
