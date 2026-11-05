import "server-only";

import { NextResponse } from "next/server";

/** The origin the browser believes it is talking to (honours a TLS-terminating proxy's headers). */
function requestOrigin(request: Request): string {
  const url = new URL(request.url);
  const host = request.headers.get("x-forwarded-host") ?? request.headers.get("host") ?? url.host;
  const proto = request.headers.get("x-forwarded-proto")?.split(",")[0].trim() ?? url.protocol.replace(":", "");
  return `${proto}://${host}`;
}

/**
 * Rejects state-changing requests that did not come from our own pages: browsers mark them with
 * `Sec-Fetch-Site: cross-site`, and send an `Origin` that is not ours. Both checks are cheap and
 * independent of the session cookie (SameSite=Lax already blocks most of this; this is the backstop).
 */
export function rejectCrossSite(request: Request): Response | null {
  const site = request.headers.get("sec-fetch-site");
  if (site === "cross-site") return forbidden("Cross-site requests are not allowed.");
  const origin = request.headers.get("origin");
  if (origin && origin !== "null" && origin !== requestOrigin(request)) {
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
