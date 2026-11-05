"use client";

import { PageHeader } from "@/components/page-header";
import { EmptyState, ErrorState, ListSkeleton } from "@/components/states";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useResource } from "@/hooks/use-resource";
import { api } from "@/lib/api";
import { formatRelative } from "@/lib/time";
import { ExternalLink, RefreshCw } from "lucide-react";

type Row = Record<string, unknown>;

function humanize(key: string): string {
  return key.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/^./, (c) => c.toUpperCase());
}

/** The outbox shape is not pinned in the contract, so render any list of objects generically. */
function sections(data: unknown): { title: string; rows: Row[] }[] {
  if (Array.isArray(data)) return [{ title: "Outbox", rows: data as Row[] }];
  if (data && typeof data === "object") {
    return Object.entries(data as Row)
      .filter(([, v]) => Array.isArray(v))
      .map(([k, v]) => ({ title: humanize(k), rows: v as Row[] }));
  }
  return [];
}

function cell(value: unknown): string {
  if (value === null || value === undefined) return "—";
  if (typeof value === "string" && /^\d{4}-\d{2}-\d{2}T/.test(value)) return formatRelative(value);
  if (typeof value === "object") return JSON.stringify(value);
  return String(value);
}

function DataTable({ rows }: { rows: Row[] }) {
  const columns = Array.from(new Set(rows.flatMap((r) => Object.keys(r ?? {})))).slice(0, 8);
  return (
    <Table>
      <TableHeader>
        <TableRow>
          {columns.map((c) => (
            <TableHead key={c} className="first:pl-4 last:pr-4">
              {humanize(c)}
            </TableHead>
          ))}
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map((row, i) => (
          <TableRow key={String(row.id ?? i)}>
            {columns.map((c) => (
              <TableCell
                key={c}
                className="max-w-80 truncate first:pl-4 last:pr-4"
                title={typeof row[c] === "string" ? (row[c] as string) : undefined}
              >
                {cell(row[c])}
              </TableCell>
            ))}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

/** The scalar part of the outbox answer: how emails leave the system, and where to see them. */
function delivery(data: unknown): { transport: string | null; sesInboxUrl: string | null } {
  const row = data && typeof data === "object" && !Array.isArray(data) ? (data as Row) : {};
  const url = typeof row.sesInboxUrl === "string" && /^https?:\/\//.test(row.sesInboxUrl) ? row.sesInboxUrl : null;
  return { transport: typeof row.transport === "string" ? row.transport : null, sesInboxUrl: url };
}

export function Outbox() {
  const outbox = useResource(() => api.outbox());
  const notEnabled = outbox.error?.status === 404;
  const { transport, sesInboxUrl } = delivery(outbox.data);

  return (
    <>
      <PageHeader
        title="Outbox"
        description={
          <>
            Domain events written in the same transaction as the change, and the emails they produced.
            {transport ? (
              <>
                {" "}
                Transport: <code className="rounded bg-muted px-1 py-0.5 text-xs">{transport}</code>.{" "}
                {sesInboxUrl ? (
                  <a
                    href={sesInboxUrl}
                    target="_blank"
                    rel="noreferrer"
                    className="inline-flex items-center gap-1 underline underline-offset-4"
                  >
                    Open the LocalStack SES inbox
                    <ExternalLink className="size-3" />
                  </a>
                ) : (
                  "Emails are logged by the worker instead of being sent."
                )}
              </>
            ) : null}
          </>
        }
        actions={
          <Button variant="outline" size="sm" onClick={outbox.reload} disabled={outbox.loading}>
            <RefreshCw className={outbox.loading ? "animate-spin" : undefined} />
            Refresh
          </Button>
        }
      />
      {notEnabled ? (
        <EmptyState
          title="Outbox not enabled"
          description="This API build does not expose the outbox endpoint."
        />
      ) : outbox.error ? (
        <ErrorState message={outbox.error.message} onRetry={outbox.reload} />
      ) : outbox.data === undefined ? (
        <ListSkeleton />
      ) : sections(outbox.data).length === 0 ? (
        <EmptyState
          title="Nothing in the outbox"
          description="Book or respond to a showing to see events here."
        />
      ) : (
        <div className="space-y-6">
          {sections(outbox.data).map((section) => (
            <Card key={section.title} className="gap-0 pb-0">
              <CardHeader className="pb-4">
                <CardTitle>
                  {section.title}{" "}
                  <span className="text-sm font-normal text-muted-foreground">({section.rows.length})</span>
                </CardTitle>
              </CardHeader>
              <CardContent className="px-0">
                {section.rows.length === 0 ? (
                  <p className="px-4 pb-4 text-sm text-muted-foreground">Empty.</p>
                ) : (
                  <DataTable rows={section.rows} />
                )}
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </>
  );
}
