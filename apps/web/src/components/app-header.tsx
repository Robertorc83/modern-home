"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useState } from "react";
import { Building2, CalendarClock, LogOut } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { useUser } from "@/components/user-context";
import { cn } from "@/lib/utils";
import type { Role } from "@/lib/types";

const NAV: Record<Role, { href: string; label: string }[]> = {
  BrokerageAdmin: [
    { href: "/admin", label: "Agents" },
    { href: "/admin/listings", label: "Listings" },
    { href: "/admin/showings", label: "Showings" },
    { href: "/admin/outbox", label: "Outbox" },
  ],
  Agent: [
    { href: "/agent/inbox", label: "Inbox" },
    { href: "/agent/listings", label: "My listings" },
    { href: "/settings/ai-assistant", label: "AI assistant" },
  ],
  Buyer: [
    { href: "/buyer", label: "Find showings" },
    { href: "/buyer/showings", label: "My showings" },
    { href: "/settings/ai-assistant", label: "AI assistant" },
  ],
};

export const ROLE_LABEL: Record<Role, string> = {
  BrokerageAdmin: "Brokerage admin",
  Agent: "Agent",
  Buyer: "Buyer",
};

function isActive(pathname: string, href: string): boolean {
  // Section roots ("/admin", "/buyer") only match exactly; deeper links match their subtree.
  if (href === "/buyer") return pathname === href || pathname.startsWith("/buyer/listings/");
  if (href === "/admin") return pathname === href;
  return pathname === href || pathname.startsWith(`${href}/`);
}

export function AppHeader() {
  const user = useUser();
  const pathname = usePathname();
  const router = useRouter();
  const [signingOut, setSigningOut] = useState(false);

  async function signOut() {
    setSigningOut(true);
    await fetch("/api/auth/logout", { method: "POST" }).catch(() => undefined);
    router.replace("/login");
    router.refresh();
  }

  return (
    <header className="sticky top-0 z-40 border-b bg-card/95 backdrop-blur">
      <div className="mx-auto flex h-14 max-w-6xl items-center gap-6 px-4">
        <Link href="/" className="flex items-center gap-2 font-semibold tracking-tight">
          <CalendarClock className="size-5" />
          <span className="hidden sm:inline">Showings</span>
        </Link>

        <nav className="hidden items-center gap-1 md:flex" aria-label="Main">
          {NAV[user.role].map((item) => (
            <Link
              key={item.href}
              href={item.href}
              className={cn(
                "rounded-md px-3 py-1.5 text-sm text-muted-foreground transition-colors hover:bg-muted hover:text-foreground",
                isActive(pathname, item.href) && "bg-muted font-medium text-foreground",
              )}
            >
              {item.label}
            </Link>
          ))}
        </nav>

        <div className="ml-auto flex min-w-0 items-center gap-2 sm:gap-3">
          <div
            className="flex min-w-0 items-center gap-1.5 rounded-md border bg-background px-2.5 py-1 text-sm"
            title={`Tenant: ${user.tenantName} (${user.tenantSlug}). All data on this page is scoped to it.`}
          >
            <Building2 className="size-4 shrink-0 text-muted-foreground" />
            <span className="truncate font-medium">{user.tenantName}</span>
          </div>
          <Badge variant="outline" className="shrink-0">
            {ROLE_LABEL[user.role]}
          </Badge>
          <span className="hidden text-sm text-muted-foreground lg:inline">{user.name}</span>
          <Button variant="ghost" size="sm" onClick={signOut} disabled={signingOut}>
            <LogOut />
            <span className="hidden sm:inline">Sign out</span>
          </Button>
        </div>
      </div>

      <nav className="flex gap-1 overflow-x-auto border-t px-4 py-1.5 md:hidden" aria-label="Main">
        {NAV[user.role].map((item) => (
          <Link
            key={item.href}
            href={item.href}
            className={cn(
              "shrink-0 rounded-md px-3 py-1 text-sm text-muted-foreground",
              isActive(pathname, item.href) && "bg-muted font-medium text-foreground",
            )}
          >
            {item.label}
          </Link>
        ))}
      </nav>
    </header>
  );
}
