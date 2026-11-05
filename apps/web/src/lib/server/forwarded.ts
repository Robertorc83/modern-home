import "server-only";

/**
 * `X-Forwarded-For` / `X-Forwarded-Proto` for the API, which rate-limits auth per client IP and
 * trusts forwarded headers. The BFF sits in front of it, so without these every user would look
 * like one client. The IP is taken from an upstream proxy's header when there is one; a route
 * handler has no access to the socket address, so it is omitted when nothing says where the
 * request came from.
 */
export function forwardedHeaders(request: Request): Record<string, string> {
  const headers: Record<string, string> = {};
  const ip =
    request.headers.get("x-forwarded-for")?.split(",")[0].trim() || request.headers.get("x-real-ip")?.trim();
  if (ip) headers["x-forwarded-for"] = ip;
  const proto =
    request.headers.get("x-forwarded-proto")?.split(",")[0].trim() || new URL(request.url).protocol.replace(":", "");
  if (proto) headers["x-forwarded-proto"] = proto;
  return headers;
}
