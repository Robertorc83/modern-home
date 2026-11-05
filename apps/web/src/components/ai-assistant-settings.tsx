"use client";

import { useState, type FormEvent } from "react";
import { KeyRound, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { CodeBlock } from "@/components/code-block";
import { CopyButton } from "@/components/copy-button";
import { PageHeader } from "@/components/page-header";
import { ErrorState, ListSkeleton } from "@/components/states";
import { useUser } from "@/components/user-context";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useResource } from "@/hooks/use-resource";
import { api, toastError } from "@/lib/api";
import { formatRelative } from "@/lib/time";
import type { AccessToken, CreatedAccessToken } from "@/lib/types";

const TOKEN_PLACEHOLDER = "<token>";

const EXAMPLE_PROMPTS = {
  Buyer: "Find me a Saturday morning showing in Denver under 600k and book it.",
  Agent: "Show my pending showing requests and confirm the earliest one.",
};

function claudeCodeCommand(mcpUrl: string, token: string) {
  return `claude mcp add --transport http showings ${mcpUrl} --header "Authorization: Bearer ${token}"`;
}

function desktopConfig(mcpUrl: string, token: string) {
  return JSON.stringify(
    {
      mcpServers: {
        showings: {
          command: "npx",
          args: ["mcp-remote", mcpUrl, "--header", `Authorization: Bearer ${token}`],
        },
      },
    },
    null,
    2,
  );
}

/** `mcpUrl` is resolved by the server page at request time from PUBLIC_MCP_URL. */
export function AiAssistantSettings({ mcpUrl }: { mcpUrl: string }) {
  const user = useUser();
  const tokens = useResource(() => api.tokens());
  const [name, setName] = useState("Claude Code");
  const [creating, setCreating] = useState(false);
  const [created, setCreated] = useState<CreatedAccessToken | null>(null);
  const [revoking, setRevoking] = useState<AccessToken | null>(null);
  const [revokeBusy, setRevokeBusy] = useState(false);

  async function create(event: FormEvent) {
    event.preventDefault();
    setCreating(true);
    try {
      const token = await api.createToken(name.trim() || "AI assistant");
      setCreated(token);
      void tokens.reload();
    } catch (err) {
      toastError(err);
    } finally {
      setCreating(false);
    }
  }

  async function revoke(token: AccessToken) {
    setRevokeBusy(true);
    try {
      await api.revokeToken(token.id);
      toast.success("Token revoked", { description: `${token.name} can no longer connect.` });
      if (created?.id === token.id) setCreated(null);
      setRevoking(null);
      void tokens.reload();
    } catch (err) {
      toastError(err);
    } finally {
      setRevokeBusy(false);
    }
  }

  const tokenValue = created?.token ?? TOKEN_PLACEHOLDER;
  const role = user.role === "Agent" ? "Agent" : "Buyer";

  return (
    <>
      <PageHeader
        title="Connect an AI assistant"
        description={
          role === "Buyer"
            ? "Search listings, find open slots and book showings from Claude Code or Claude Desktop."
            : "Review showing requests, approve or decline, and check listing schedules from Claude Code or Claude Desktop."
        }
      />

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle>1. Create a personal access token</CardTitle>
              <CardDescription>
                It acts as you ({user.name}, {role.toLowerCase()} at {user.tenantName}) and sees only what you
                can see. Only a hash is stored; revoke it any time.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <form onSubmit={create} className="flex items-end gap-2">
                <div className="flex-1 space-y-1.5">
                  <Label htmlFor="tokenName">Token name</Label>
                  <Input
                    id="tokenName"
                    value={name}
                    maxLength={60}
                    onChange={(e) => setName(e.target.value)}
                  />
                </div>
                <Button type="submit" disabled={creating}>
                  {creating ? <Loader2 className="animate-spin" /> : <KeyRound />}
                  Create token
                </Button>
              </form>

              {created ? (
                <Alert className="border-emerald-300 bg-emerald-50">
                  <AlertTitle>Copy your token now</AlertTitle>
                  <AlertDescription className="space-y-2">
                    <p>It is shown only once. The commands on this page already include it.</p>
                    <div className="flex items-center gap-2">
                      <code className="min-w-0 flex-1 truncate rounded border bg-background px-2 py-1 font-mono text-xs text-foreground">
                        {created.token}
                      </code>
                      <CopyButton value={created.token} />
                    </div>
                  </AlertDescription>
                </Alert>
              ) : null}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Your tokens</CardTitle>
            </CardHeader>
            <CardContent className="px-0">
              {tokens.error ? (
                <div className="px-4">
                  <ErrorState message={tokens.error.message} onRetry={tokens.reload} />
                </div>
              ) : !tokens.data ? (
                <div className="px-4">
                  <ListSkeleton rows={2} />
                </div>
              ) : tokens.data.length === 0 ? (
                <p className="px-4 text-sm text-muted-foreground">No tokens yet.</p>
              ) : (
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead className="pl-4">Name</TableHead>
                      <TableHead>Token</TableHead>
                      <TableHead>Last used</TableHead>
                      <TableHead className="pr-4 text-right" />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {tokens.data.map((t) => (
                      <TableRow key={t.id} className={t.revokedAt ? "text-muted-foreground" : undefined}>
                        <TableCell className="pl-4">
                          <div className="font-medium">{t.name}</div>
                          <div className="text-xs text-muted-foreground">
                            Created {formatRelative(t.createdAt)}
                          </div>
                        </TableCell>
                        <TableCell className="font-mono text-xs">{t.prefix}…</TableCell>
                        <TableCell className="text-sm">{formatRelative(t.lastUsedAt)}</TableCell>
                        <TableCell className="pr-4 text-right">
                          {t.revokedAt ? (
                            <Badge variant="outline">Revoked</Badge>
                          ) : (
                            <Button variant="ghost" size="sm" onClick={() => setRevoking(t)}>
                              Revoke
                            </Button>
                          )}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </CardContent>
          </Card>
        </div>

        <Card className="self-start">
          <CardHeader>
            <CardTitle>2. Add the server to your client</CardTitle>
            <CardDescription>
              No Anthropic key needed. Your Claude client brings the model; this app only exposes tools over
              MCP.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-5">
            {!created ? (
              <p className="text-sm text-muted-foreground">
                Create a token first and it will be filled in below. Until then the commands show{" "}
                <code className="font-mono">{TOKEN_PLACEHOLDER}</code>.
              </p>
            ) : null}
            <div className="space-y-2">
              <p className="text-sm font-medium">Claude Code</p>
              <CodeBlock label="Terminal" code={claudeCodeCommand(mcpUrl, tokenValue)} />
            </div>
            <div className="space-y-2">
              <p className="text-sm font-medium">Claude Desktop</p>
              <p className="text-xs text-muted-foreground">
                Add to <code className="font-mono">claude_desktop_config.json</code>, then restart Claude
                Desktop.
              </p>
              <CodeBlock label="claude_desktop_config.json" code={desktopConfig(mcpUrl, tokenValue)} />
            </div>
            <div className="space-y-2">
              <p className="text-sm font-medium">Then try</p>
              <CodeBlock label="Prompt" code={EXAMPLE_PROMPTS[role]} />
            </div>
          </CardContent>
        </Card>
      </div>

      <Dialog
        open={revoking !== null}
        onOpenChange={(open) => (!open && !revokeBusy ? setRevoking(null) : undefined)}
      >
        <DialogContent className="sm:max-w-sm">
          <DialogHeader>
            <DialogTitle>Revoke this token?</DialogTitle>
            <DialogDescription>
              Assistants using {revoking ? `"${revoking.name}"` : "it"} lose access immediately. This cannot
              be undone.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setRevoking(null)} disabled={revokeBusy}>
              Keep it
            </Button>
            <Button variant="destructive" onClick={() => revoking && revoke(revoking)} disabled={revokeBusy}>
              {revokeBusy ? <Loader2 className="animate-spin" /> : null}
              Revoke
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
