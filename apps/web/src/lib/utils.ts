export { cn } from "cn";

/** A single leading "/" only: "//host" and "/\host" are protocol-relative URLs to browsers. */
const LOCAL_PATH = /^\/(?![/\\])/;

/**
 * Accepts only a same-origin path for post-login redirects and returns it normalised; null for
 * anything else. Besides the obvious "//evil" the raw value may not contain control characters
 * or spaces: the WHATWG URL parser strips tabs and newlines, so "/\t/evil" would otherwise
 * become "//evil". The value is then parsed against a fixed origin and must stay on it.
 */
export function safeNextPath(value: unknown): string | null {
  if (typeof value !== "string" || !LOCAL_PATH.test(value) || /[\x00-\x20]/.test(value)) return null;
  let url: URL;
  try {
    url = new URL(value, "http://x");
  } catch {
    return null;
  }
  if (url.origin !== "http://x" || !LOCAL_PATH.test(url.pathname)) return null;
  return url.pathname + url.search + url.hash;
}
