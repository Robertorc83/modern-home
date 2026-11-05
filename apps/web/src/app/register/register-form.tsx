"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState, type FormEvent } from "react";
import { Loader2 } from "lucide-react";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { problemMessage } from "@/lib/api";
import type { ProblemDetails, Tenant } from "@/lib/types";

export function RegisterForm() {
  const router = useRouter();
  const [tenants, setTenants] = useState<Tenant[] | null>(null);
  const [tenantError, setTenantError] = useState<string | null>(null);
  const [form, setForm] = useState({ tenantSlug: "", name: "", email: "", password: "" });
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetch("/api/proxy/tenants", { cache: "no-store" })
      .then(async (res) => {
        if (!res.ok) throw new Error(problemMessage(await res.json().catch(() => ({})), res.status));
        return (await res.json()) as Tenant[];
      })
      .then((list) => {
        setTenants(list);
        if (list.length > 0) setForm((f) => (f.tenantSlug ? f : { ...f, tenantSlug: list[0].slug }));
      })
      .catch((err: Error) => setTenantError(err.message));
  }, []);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setPending(true);
    setError(null);
    try {
      const res = await fetch("/api/auth/register", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(form),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        setError(
          res.status === 409
            ? "An account with this email already exists. Sign in instead."
            : problemMessage(data as ProblemDetails, res.status),
        );
        setPending(false);
        return;
      }
      router.replace((data as { home: string }).home);
      router.refresh();
    } catch {
      setError("Could not reach the server.");
      setPending(false);
    }
  }

  const update = (key: keyof typeof form) => (e: { target: { value: string } }) =>
    setForm((f) => ({ ...f, [key]: e.target.value }));

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle>Create a buyer account</CardTitle>
          <CardDescription>Buyers belong to one brokerage and see only its listings.</CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={onSubmit} className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="tenant">Brokerage</Label>
              {tenantError ? (
                <p className="text-sm text-destructive">Could not load brokerages: {tenantError}</p>
              ) : (
                <Select
                  value={form.tenantSlug}
                  onValueChange={(tenantSlug) => setForm((f) => ({ ...f, tenantSlug }))}
                  disabled={!tenants}
                >
                  <SelectTrigger id="tenant" className="w-full">
                    <SelectValue placeholder={tenants ? "Choose a brokerage" : "Loading…"} />
                  </SelectTrigger>
                  <SelectContent>
                    {tenants?.map((t) => (
                      <SelectItem key={t.slug} value={t.slug}>
                        {t.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="name">Full name</Label>
              <Input id="name" required autoComplete="name" value={form.name} onChange={update("name")} />
            </div>
            <div className="space-y-2">
              <Label htmlFor="email">Email</Label>
              <Input
                id="email"
                type="email"
                required
                autoComplete="email"
                value={form.email}
                onChange={update("email")}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="password">Password</Label>
              <Input
                id="password"
                type="password"
                required
                minLength={8}
                autoComplete="new-password"
                value={form.password}
                onChange={update("password")}
              />
              <p className="text-xs text-muted-foreground">At least 8 characters.</p>
            </div>
            {error ? (
              <Alert variant="destructive">
                <AlertDescription>{error}</AlertDescription>
              </Alert>
            ) : null}
            <Button type="submit" className="w-full" disabled={pending || !form.tenantSlug}>
              {pending ? <Loader2 className="animate-spin" /> : null}
              Create account
            </Button>
          </form>
        </CardContent>
      </Card>
      <p className="text-center text-sm text-muted-foreground">
        Already have an account?{" "}
        <Link href="/login" className="font-medium text-foreground underline underline-offset-4">
          Sign in
        </Link>
      </p>
    </div>
  );
}
