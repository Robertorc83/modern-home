import "server-only";

/**
 * How many reverse proxies in front of this app append the caller's address to
 * `X-Forwarded-For`. 0 (the default) means the rightmost entry is the address the nearest proxy
 * saw, i.e. the client; each further trusted hop moves the pick one entry to the left.
 */
function trustedProxyHops(): number {
  const n = Number.parseInt(process.env.TRUSTED_PROXY_HOPS ?? "0", 10);
  return Number.isFinite(n) && n > 0 ? n : 0;
}

/**
 * The client IP for `X-Forwarded-For`. Next fills the header with the socket address when no
 * header arrived, so a direct caller shows up as itself. When a header did arrive (from a
 * proxy, or from a direct caller trying to spoof one), the LEFTMOST entries are whatever the
 * caller wrote; only the entry appended by the nearest proxy can be trusted, which is the
 * rightmost one unless TRUSTED_PROXY_HOPS says more proxies appended after it.
 */
function clientIp(request: Request): string | undefined {
  const entries = (request.headers.get("x-forwarded-for") ?? "")
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean);
  if (entries.length === 0) return undefined;
  return entries[Math.max(0, entries.length - 1 - trustedProxyHops())];
}

/**
 * `X-Forwarded-For` / `X-Forwarded-Proto` for the API, which rate-limits auth per client IP and
 * trusts forwarded headers. The BFF sits in front of it, so without these every user would look
 * like one client. A route handler has no access to the socket address, so the IP is omitted when
 * no header says where the request came from.
 */
export function forwardedHeaders(request: Request): Record<string, string> {
  const headers: Record<string, string> = {};
  const ip = clientIp(request);
  if (ip) headers["x-forwarded-for"] = ip;
  const proto =
    request.headers.get("x-forwarded-proto")?.split(",")[0].trim() || new URL(request.url).protocol.replace(":", "");
  if (proto) headers["x-forwarded-proto"] = proto;
  return headers;
}
