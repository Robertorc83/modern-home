import { NextResponse, type NextRequest } from "next/server";
import { SESSION_COOKIE } from "@/lib/server/session";

export async function POST() {
  const response = new NextResponse(null, { status: 204 });
  response.cookies.delete(SESSION_COOKIE);
  return response;
}

/** Used by server components when the API rejects the session (they cannot clear cookies). */
export async function GET(request: NextRequest) {
  const target = new URL("/login", request.url);
  if (request.nextUrl.searchParams.has("expired")) target.searchParams.set("expired", "1");
  const response = NextResponse.redirect(target);
  response.cookies.delete(SESSION_COOKIE);
  return response;
}
