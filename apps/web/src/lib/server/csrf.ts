import "server-only";

import { NextResponse } from "next/server";

/** The first value of a header that proxies append to (`a, b` becomes `a`). */
function first(header: string | null): string | undefined {
  return header?.split(",")[0].trim() || undefined;
}

/** The origin the browser believes it is talking to (honours a TLS-terminating proxy's headers). */
function requestOrigin(request: Request): string {
  const url = new URL(request.url);
  const host = first(request.headers.get("x-forwarded-host")) ?? request.headers.get("host") ?? url.host;
  const proto = first(request.headers.get("x-forwarded-proto")) ?? url.protocol.replace(":", "");
  return `${proto}://${host}`.toLowerCase();
}

/**
 * Origins the app is published under (`PUBLIC_ORIGIN`, comma-separated), for a reverse proxy
 * that does not pass `X-Forwarded-Proto` / `X-Forwarded-Host`: without them the computed origin
 * is the upstream one (`http://web:3000`) and every legitimate request would be rejected. Read
 * per request so one image serves any deployment.
 */
function publicOrigins(): string[] {
  return (process.env.PUBLIC_ORIGIN ?? "")
    .split(",")
    .map((s) => s.trim().replace(/\/+$/, "").toLowerCase())
    .filter(Boolean);
}

/**
 * Rejects state-changing requests that did not come from our own pages: browsers mark them with
 * `Sec-Fetch-Site: cross-site`, and send an `Origin` that is not ours. Both checks are cheap and
 * independent of the session cookie (SameSite=Lax already blocks most of this; this is the backstop).
 */
export function rejectCrossSite(request: Request): Response | null {
  const site = request.headers.get("sec-fetch-site");
  if (site === "cross-site") return forbidden("Cross-site requests are not allowed.");
  const origin = request.headers.get("origin")?.trim().toLowerCase();
  // No Origin header: same-origin navigation or a non-browser client (the cookie decides then).
  if (origin === undefined) return null;
  // "null" is an opaque origin (sandboxed iframe, data: page, cross-site redirect): not ours.
  if (origin === "null" || (origin !== requestOrigin(request) && !publicOrigins().includes(origin))) {
    return forbidden("Cross-site requests are not allowed.");
  }
  return null;
}

/** Credential exchanges must be JSON: a plain HTML form cannot send that content type cross-site. */
export function requireJson(request: Request): Response | null {
  const type = request.headers.get("content-type") ?? "";
  if (!type.toLowerCase().startsWith("application/json")) {
    return NextResponse.json(
      { title: "Unsupported Media Type", status: 415, detail: "Send a JSON body." },
      { status: 415 },
    );
  }
  return null;
}

function forbidden(detail: string): Response {
  return NextResponse.json({ title: "Forbidden", status: 403, detail }, { status: 403 });
}
