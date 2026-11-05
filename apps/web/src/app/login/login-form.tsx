"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { Loader2 } from "lucide-react";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Separator } from "@/components/ui/separator";
import { problemMessage } from "@/lib/api";
import type { ProblemDetails } from "@/lib/types";

const DEMO_PASSWORD = "demo1234";

const DEMO_ACCOUNTS = [
  { email: "admin@acme.test", role: "Admin", tenant: "Acme Realty" },
  { email: "agent@acme.test", role: "Agent", tenant: "Acme Realty" },
  { email: "buyer@acme.test", role: "Buyer", tenant: "Acme Realty" },
  { email: "agent@northstar.test", role: "Agent", tenant: "Northstar Homes" },
  { email: "buyer@northstar.test", role: "Buyer", tenant: "Northstar Homes" },
];

export function LoginForm({ next, expired }: { next: string | null; expired: boolean }) {
  const router = useRouter();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [pending, setPending] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function signIn(credentials: { email: string; password: string }, key: string) {
    setPending(key);
    setError(null);
    try {
      const res = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(credentials),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        setError(
          res.status === 401
            ? "Email or password is incorrect."
            : problemMessage(data as ProblemDetails, res.status),
        );
        setPending(null);
        return;
      }
      router.replace(next ?? (data as { home: string }).home);
      router.refresh();
    } catch {
      setError("Could not reach the server.");
      setPending(null);
    }
  }

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    void signIn({ email, password }, "form");
  }

  return (
    <div className="space-y-4">
      {expired ? (
        <Alert>
          <AlertDescription>Your session ended. Please sign in again.</AlertDescription>
        </Alert>
      ) : null}
      {error ? (
        <Alert variant="destructive">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      ) : null}

      <Card>
        <CardHeader>
          <CardTitle>Try a demo account</CardTitle>
          <CardDescription>
            One click signs you in. Two brokerages share the app; each sees only its own data.
          </CardDescription>
        </CardHeader>
        <CardContent className="grid gap-2 sm:grid-cols-2">
          {DEMO_ACCOUNTS.map((account) => (
            <Button
              key={account.email}
              variant="outline"
              className="h-auto justify-start py-2 text-left"
              disabled={pending !== null}
              onClick={() => signIn({ email: account.email, password: DEMO_PASSWORD }, account.email)}
            >
              {pending === account.email ? <Loader2 className="animate-spin" /> : null}
              <span className="flex flex-col">
                <span className="font-medium">{account.role}</span>
                <span className="text-xs font-normal text-muted-foreground">{account.tenant}</span>
              </span>
            </Button>
          ))}
        </CardContent>
      </Card>

      <div className="flex items-center gap-3">
        <Separator className="flex-1" />
        <span className="text-xs text-muted-foreground">or sign in with email</span>
        <Separator className="flex-1" />
      </div>

      <Card>
        <CardContent>
          <form onSubmit={onSubmit} className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="email">Email</Label>
              <Input
                id="email"
                type="email"
                autoComplete="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="password">Password</Label>
              <Input
                id="password"
                type="password"
                autoComplete="current-password"
                required
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
            </div>
            <Button type="submit" className="w-full" disabled={pending !== null}>
              {pending === "form" ? <Loader2 className="animate-spin" /> : null}
              Sign in
            </Button>
          </form>
        </CardContent>
      </Card>

      <p className="text-center text-sm text-muted-foreground">
        New buyer?{" "}
        <Link href="/register" className="font-medium text-foreground underline underline-offset-4">
          Create a buyer account
        </Link>
      </p>
    </div>
  );
}
