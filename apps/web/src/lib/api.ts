"use client";

import { toast } from "sonner";
import type {
  AccessToken,
  AgentSummary,
  CreatedAccessToken,
  Listing,
  ListingInput,
  MatchRequest,
  MatchResponse,
  ParsedAvailability,
  ProblemDetails,
  Showing,
  ShowingRules,
  ShowingStatus,
  SlotHold,
  SlotsResponse,
  Slot,
  Tenant,
  User,
} from "./types";

export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails;

  constructor(status: number, problem: ProblemDetails) {
    super(problemMessage(problem, status));
    this.status = status;
    this.problem = problem;
  }

  /** `alternatives` may be serialized at the root (ASP.NET default) or under `extensions`. */
  get alternatives(): Slot[] {
    return this.problem.alternatives ?? this.problem.extensions?.alternatives ?? [];
  }
}

export function problemMessage(problem: ProblemDetails, status: number): string {
  if (problem.errors) {
    const first = Object.values(problem.errors).flat()[0];
    if (first) return first;
  }
  return problem.detail || problem.title || `Request failed (${status})`;
}

/** Show an error as a toast; returns the message for inline use. */
export function toastError(err: unknown, fallback = "Something went wrong"): string {
  const message = err instanceof Error ? err.message : fallback;
  toast.error(message);
  return message;
}

type Query = Record<string, string | number | boolean | null | undefined>;

function buildUrl(path: string, query?: Query): string {
  const url = `/api/proxy${path.startsWith("/") ? path : `/${path}`}`;
  if (!query) return url;
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value === null || value === undefined || value === "") continue;
    params.set(key, String(value));
  }
  const qs = params.toString();
  return qs ? `${url}?${qs}` : url;
}

let redirectingToLogin = false;

/**
 * Sends the browser to /login (full navigation, so all client state of the old session is dropped)
 * and remembers where the user was. Returns a promise that never settles: callers are unloading.
 */
export function redirectToLogin(): Promise<never> {
  if (!redirectingToLogin) {
    redirectingToLogin = true;
    const login = new URL("/login", window.location.origin);
    login.searchParams.set("expired", "1");
    const here = window.location.pathname + window.location.search;
    if (here !== "/" && !here.startsWith("/login")) login.searchParams.set("next", here);
    window.location.assign(login);
  }
  return new Promise<never>(() => undefined);
}

async function request<T>(method: string, path: string, body?: unknown, query?: Query): Promise<T> {
  let res: Response;
  try {
    res = await fetch(buildUrl(path, query), {
      method,
      headers: body === undefined ? undefined : { "Content-Type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
      cache: "no-store",
    });
  } catch {
    throw new ApiError(0, { title: "Network error", detail: "Could not reach the server." });
  }

  // Session expired or revoked: the proxy already cleared the cookie. Nothing should render an
  // error for this, so the promise stays pending while the page navigates away.
  if (res.status === 401) return redirectToLogin();

  if (res.status === 204) return undefined as T;

  const text = await res.text();
  let data: unknown = undefined;
  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      data = { detail: text };
    }
  }

  if (!res.ok) {
    const problem: ProblemDetails =
      data && typeof data === "object" ? (data as ProblemDetails) : { title: res.statusText };
    throw new ApiError(res.status, problem);
  }
  return data as T;
}

export const api = {
  get: <T>(path: string, query?: Query) => request<T>("GET", path, undefined, query),
  post: <T>(path: string, body?: unknown) => request<T>("POST", path, body ?? {}),
  put: <T>(path: string, body?: unknown) => request<T>("PUT", path, body ?? {}),
  del: <T = void>(path: string) => request<T>("DELETE", path),

  // Identity
  me: () => api.get<User>("/auth/me"),
  tenants: () => api.get<Tenant[]>("/tenants"),
  tokens: () => api.get<AccessToken[]>("/auth/tokens"),
  createToken: (name: string) => api.post<CreatedAccessToken>("/auth/tokens", { name }),
  revokeToken: (id: string) => api.del(`/auth/tokens/${encodeURIComponent(id)}`),
  agents: () => api.get<AgentSummary[]>("/admin/agents"),
  inviteAgent: (body: { name: string; email: string; password: string }) =>
    api.post<User>("/admin/agents", body),

  // Listings
  listings: (query?: Query) => api.get<Listing[]>("/listings", query),
  listing: (id: string) => api.get<Listing>(`/listings/${encodeURIComponent(id)}`),
  createListing: (body: ListingInput) => api.post<Listing>("/listings", body),
  updateListing: (id: string, body: ListingInput) =>
    api.put<Listing>(`/listings/${encodeURIComponent(id)}`, body),
  saveRules: (id: string, rules: ShowingRules) =>
    api.put<Listing | ShowingRules>(`/listings/${encodeURIComponent(id)}/showing-rules`, rules),
  slots: (id: string, query: { from: string; to: string; timeZone?: string }) =>
    api.get<SlotsResponse>(`/listings/${encodeURIComponent(id)}/slots`, query),

  // Matching
  match: (body: MatchRequest) => api.post<MatchResponse>("/match", body),

  // Scheduling. `timeZone` is the buyer's zone, so 409 alternatives come back labelled in it.
  hold: (listingId: string, slotStart: string, timeZone: string) =>
    api.post<SlotHold>("/showings/holds", { listingId, slotStart, timeZone }),
  releaseHold: (id: string) => api.del(`/showings/holds/${encodeURIComponent(id)}`),
  book: (listingId: string, slotStart: string, timeZone: string) =>
    api.post<Showing>("/showings", { listingId, slotStart, timeZone }),
  confirmShowing: (id: string) => api.post<Showing>(`/showings/${encodeURIComponent(id)}/confirm`),
  declineShowing: (id: string) => api.post<Showing>(`/showings/${encodeURIComponent(id)}/decline`),
  cancelShowing: (id: string) => api.post<Showing>(`/showings/${encodeURIComponent(id)}/cancel`),
  myShowings: () => api.get<Showing[]>("/showings/mine"),
  inbox: (status?: ShowingStatus) => api.get<Showing[]>("/showings/inbox", { status }),

  // AI
  aiProvider: () => api.get<{ provider: string }>("/ai/provider"),
  parseAvailability: (text: string, timeZone: string) =>
    api.post<ParsedAvailability>("/ai/parse-availability", {
      text,
      timeZone,
      now: new Date().toISOString(),
    }),

  // Ops
  outbox: () => api.get<unknown>("/admin/outbox"),
};
