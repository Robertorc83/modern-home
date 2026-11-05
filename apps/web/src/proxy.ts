import { NextResponse, type NextRequest } from "next/server";

const SESSION_COOKIE = "hss_session";

/** Optimistic check only: no cookie means no session. The API remains the authority. */
export function proxy(request: NextRequest) {
  if (request.cookies.has(SESSION_COOKIE)) return NextResponse.next();
  const login = new URL("/login", request.url);
  const next = request.nextUrl.pathname + request.nextUrl.search;
  if (next !== "/") login.searchParams.set("next", next);
  return NextResponse.redirect(login);
}

export const config = {
  matcher: ["/((?!login|register|api|_next|favicon.ico|.*\\.(?:svg|png|ico|txt)$).*)"],
};
