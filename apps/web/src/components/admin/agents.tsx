"use client";

import { useState, type FormEvent } from "react";
import { Loader2, UserPlus } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/page-header";
import { EmptyState, ErrorState, ListSkeleton } from "@/components/states";
import { useUser } from "@/components/user-context";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useResource } from "@/hooks/use-resource";
import { ApiError, api } from "@/lib/api";

const EMPTY = { name: "", email: "", password: "" };

export function AgentsAdmin() {
  const user = useUser();
  const agents = useResource(() => api.agents());
  const [form, setForm] = useState(EMPTY);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function invite(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(null);
    try {
      const created = await api.inviteAgent(form);
      toast.success("Agent added", { description: `${created.name} can sign in as ${created.email}.` });
      setForm(EMPTY);
      void agents.reload();
    } catch (err) {
      setError(
        err instanceof ApiError && err.status === 409
          ? "That email is already in use."
          : (err as Error).message,
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <PageHeader title="Agents" description={`Agents at ${user.tenantName}.`} />
      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_340px]">
        <div>
          {agents.error ? (
            <ErrorState message={agents.error.message} onRetry={agents.reload} />
          ) : !agents.data ? (
            <ListSkeleton rows={3} />
          ) : agents.data.length === 0 ? (
            <EmptyState title="No agents yet" description="Invite your first agent with the form." />
          ) : (
            <Card className="py-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="pl-4">Name</TableHead>
                    <TableHead>Email</TableHead>
                    <TableHead className="pr-4 text-right">Listings</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {agents.data.map((a) => (
                    <TableRow key={a.id}>
                      <TableCell className="pl-4 font-medium">{a.name}</TableCell>
                      <TableCell className="text-muted-foreground">{a.email}</TableCell>
                      <TableCell className="pr-4 text-right tabular-nums">{a.listingCount}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Card>
          )}
        </div>

        <Card className="self-start">
          <CardHeader>
            <CardTitle>Invite an agent</CardTitle>
            <CardDescription>They join {user.tenantName} and can sign in right away.</CardDescription>
          </CardHeader>
          <CardContent>
            <form onSubmit={invite} className="space-y-3">
              <div className="space-y-1.5">
                <Label htmlFor="agentName">Name</Label>
                <Input
                  id="agentName"
                  required
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="agentEmail">Email</Label>
                <Input
                  id="agentEmail"
                  type="email"
                  required
                  value={form.email}
                  onChange={(e) => setForm({ ...form, email: e.target.value })}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="agentPassword">Temporary password</Label>
                <Input
                  id="agentPassword"
                  type="password"
                  required
                  minLength={8}
                  autoComplete="new-password"
                  value={form.password}
                  onChange={(e) => setForm({ ...form, password: e.target.value })}
                />
              </div>
              {error ? <p className="text-sm text-destructive">{error}</p> : null}
              <Button type="submit" className="w-full" disabled={saving}>
                {saving ? <Loader2 className="animate-spin" /> : <UserPlus />}
                Add agent
              </Button>
            </form>
          </CardContent>
        </Card>
      </div>
    </>
  );
}
