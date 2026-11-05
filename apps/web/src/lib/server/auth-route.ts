import "server-only";

import { NextResponse } from "next/server";
import { API_URL, SESSION_COOKIE, roleHome, sessionCookieOptions } from "./session";
import type { User } from "@/lib/types";

/**
 * Forwards a credential exchange (login / register) to the API. On success the JWT is stored in an
 * httpOnly cookie and only the user (plus where to go next) is returned to the browser.
 */
export async function exchangeForSession(apiPath: string, request: Request): Promise<Response> {
  const body = await request.text();
  let upstream: Response;
  try {
    upstream = await fetch(`${API_URL}${apiPath}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body,
      cache: "no-store",
    });
  } catch {
    return NextResponse.json(
      { title: "API unavailable", status: 502, detail: `Could not reach the API at ${API_URL}.` },
      { status: 502 },
    );
  }

  const text = await upstream.text();
  if (!upstream.ok) {
    return new Response(text || JSON.stringify({ title: upstream.statusText, status: upstream.status }), {
      status: upstream.status,
      headers: { "Content-Type": upstream.headers.get("content-type") ?? "application/problem+json" },
    });
  }

  const { token, user } = JSON.parse(text) as { token: string; user: User };
  const response = NextResponse.json({ user, home: roleHome(user.role) });
  response.cookies.set(SESSION_COOKIE, token, sessionCookieOptions);
  return response;
}
