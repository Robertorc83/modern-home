import { cookies } from "next/headers";
import { NextResponse, type NextRequest } from "next/server";
import { rejectCrossSite } from "@/lib/server/csrf";
import { forwardedHeaders } from "@/lib/server/forwarded";
import { API_URL, SESSION_COOKIE } from "@/lib/server/session";

// Never cache, never buffer: this handler also streams Server-Sent Events.
export const dynamic = "force-dynamic";

const FORWARDED_REQUEST_HEADERS = ["accept", "content-type", "last-event-id"];
const FORWARDED_RESPONSE_HEADERS = ["content-type", "location", "retry-after"];

/** How long (ms) an EventSource should wait before retrying when the API is down. */
const STREAM_RETRY_MS = 5000;

async function forward(request: NextRequest, ctx: RouteContext<"/api/proxy/[...path]">) {
  if (request.method !== "GET" && request.method !== "HEAD") {
    const rejected = rejectCrossSite(request);
    if (rejected) return rejected;
  }

  const { path } = await ctx.params;
  const target = `${API_URL}/${path.map(encodeURIComponent).join("/")}${request.nextUrl.search}`;
  const wantsStream = request.headers.get("accept")?.includes("text/event-stream") ?? false;

  const headers = new Headers(forwardedHeaders(request));
  for (const name of FORWARDED_REQUEST_HEADERS) {
    const value = request.headers.get(name);
    if (value) headers.set(name, value);
  }
  const token = (await cookies()).get(SESSION_COOKIE)?.value;
  if (token) headers.set("authorization", `Bearer ${token}`);

  const hasBody = request.method !== "GET" && request.method !== "HEAD";
  let upstream: Response;
  try {
    upstream = await fetch(target, {
      method: request.method,
      headers,
      body: hasBody ? await request.arrayBuffer() : undefined,
      cache: "no-store",
      redirect: "manual",
      // Closing the browser's EventSource aborts the upstream stream too.
      signal: request.signal,
    });
  } catch (err) {
    if (request.signal.aborted) return new Response(null, { status: 499 });
    console.error(`Could not reach the API at ${target}`, err);
    if (wantsStream) {
      // A real SSE body (with a retry hint) keeps EventSource's built-in reconnect going; a JSON
      // error would make it give up for good.
      return new Response(`retry: ${STREAM_RETRY_MS}\n: API unavailable\n\n`, {
        status: 200,
        headers: {
          "content-type": "text/event-stream; charset=utf-8",
          "cache-control": "no-cache, no-transform",
          "x-accel-buffering": "no",
        },
      });
    }
    return NextResponse.json(
      { title: "API unavailable", status: 502, detail: "The API is not reachable right now." },
      { status: 502 },
    );
  }

  const responseHeaders = new Headers();
  for (const name of FORWARDED_RESPONSE_HEADERS) {
    const value = upstream.headers.get(name);
    if (value) responseHeaders.set(name, value);
  }

  const isStream = upstream.headers.get("content-type")?.includes("text/event-stream") ?? false;
  if (isStream) {
    responseHeaders.set("cache-control", "no-cache, no-transform");
    responseHeaders.set("connection", "keep-alive");
    responseHeaders.set("x-accel-buffering", "no");
  } else {
    responseHeaders.set("cache-control", "no-store");
  }

  const bodyless = upstream.status === 204 || upstream.status === 304 || request.method === "HEAD";
  const response = new Response(bodyless ? null : upstream.body, {
    status: upstream.status,
    headers: responseHeaders,
  });

  // The API rejected the session: drop the cookie so the browser lands on /login cleanly.
  if (upstream.status === 401 && token) {
    response.headers.append("set-cookie", `${SESSION_COOKIE}=; Path=/; Max-Age=0; HttpOnly; SameSite=Lax`);
  }
  return response;
}

export const GET = forward;
export const POST = forward;
export const PUT = forward;
export const PATCH = forward;
export const DELETE = forward;
