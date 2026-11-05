import { NextResponse, type NextRequest } from "next/server";
import { rejectCrossSite } from "@/lib/server/csrf";
import { SESSION_COOKIE } from "@/lib/server/session";

/** The app signs out with a POST (see the Sign out button). */
export async function POST(request: Request) {
  const rejected = rejectCrossSite(request);
  if (rejected) return rejected;
  const response = new NextResponse(null, { status: 204 });
  response.cookies.delete(SESSION_COOKIE);
  return response;
}

/**
 * Used by server components when the API rejects the session (they cannot clear cookies).
 * A GET is only allowed to clear the cookie when the browser says the navigation is our own
 * (or user-typed); a cross-site link or image just lands on /login without signing anyone out.
 */
export async function GET(request: NextRequest) {
  const target = new URL("/login", request.url);
  if (request.nextUrl.searchParams.has("expired")) target.searchParams.set("expired", "1");
  const response = NextResponse.redirect(target);
  const site = request.headers.get("sec-fetch-site");
  if (site === "same-origin" || site === "none") response.cookies.delete(SESSION_COOKIE);
  return response;
}
