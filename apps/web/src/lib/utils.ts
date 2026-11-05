export { cn } from "cn";

/**
 * Accepts only a same-origin path for post-login redirects: it must start with a single "/"
 * ("//host" and "/\host" are protocol-relative URLs to browsers). Returns null otherwise.
 */
export function safeNextPath(value: unknown): string | null {
  if (typeof value !== "string" || !/^\/(?![/\\])/.test(value)) return null;
  return value;
}
