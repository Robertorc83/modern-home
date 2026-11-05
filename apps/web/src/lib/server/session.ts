import "server-only";

import { cache } from "react";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import type { Role, User } from "@/lib/types";

export const SESSION_COOKIE = "hss_session";
export const API_URL = (process.env.API_URL ?? "http://localhost:5080").replace(/\/$/, "");

export const sessionCookieOptions = {
  httpOnly: true,
  sameSite: "lax" as const,
  secure: process.env.COOKIE_SECURE === "true",
  path: "/",
  // JWT lifetime is decided by the API; the cookie just needs to outlive a working session.
  maxAge: 60 * 60 * 12,
};

export function roleHome(role: Role): string {
  switch (role) {
    case "BrokerageAdmin":
      return "/admin";
    case "Agent":
      return "/agent/inbox";
    case "Buyer":
      return "/buyer";
  }
}

export class ApiUnavailableError extends Error {}

/**
 * Resolves the signed-in user from the session cookie by calling the API's /auth/me.
 * Redirects to /login when there is no session, or clears it when the API rejects it.
 * Deduplicated per request, so layouts and pages can all call it.
 */
export const getCurrentUser = cache(async function getCurrentUser(): Promise<User> {
  const token = (await cookies()).get(SESSION_COOKIE)?.value;
  if (!token) redirect("/login");

  let res: Response;
  try {
    res = await fetch(`${API_URL}/auth/me`, {
      headers: { Authorization: `Bearer ${token}` },
      cache: "no-store",
    });
  } catch {
    throw new ApiUnavailableError(`Could not reach the API at ${API_URL}.`);
  }

  if (res.status === 401 || res.status === 403) redirect("/api/auth/logout?expired=1");
  if (!res.ok) throw new ApiUnavailableError(`The API returned ${res.status} for /auth/me.`);
  return (await res.json()) as User;
});

/** Use in a section layout: lets only the given roles through, others go to their home. */
export async function requireRole(...roles: Role[]): Promise<User> {
  const user = await getCurrentUser();
  if (!roles.includes(user.role)) redirect(roleHome(user.role));
  return user;
}
