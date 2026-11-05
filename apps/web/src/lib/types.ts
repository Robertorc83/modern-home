// Types mirroring docs/api-contract.md (v1).

export type Role = "BrokerageAdmin" | "Agent" | "Buyer";

export interface User {
  id: string;
  email: string;
  name: string;
  role: Role;
  tenantId: string;
  tenantSlug: string;
  tenantName: string;
  timeZone: string;
}

export interface Tenant {
  slug: string;
  name: string;
  defaultTimeZone: string;
}

export interface AccessToken {
  id: string;
  name: string;
  prefix: string;
  createdAt: string;
  lastUsedAt: string | null;
  revokedAt: string | null;
}

export interface CreatedAccessToken {
  id: string;
  name: string;
  token: string;
}

export interface AgentSummary {
  id: string;
  name: string;
  email: string;
  listingCount: number;
}

export type DayOfWeek = "Monday" | "Tuesday" | "Wednesday" | "Thursday" | "Friday" | "Saturday" | "Sunday";

export const DAYS_OF_WEEK: DayOfWeek[] = [
  "Monday",
  "Tuesday",
  "Wednesday",
  "Thursday",
  "Friday",
  "Saturday",
  "Sunday",
];

export interface WeeklyWindow {
  dayOfWeek: DayOfWeek;
  /** "HH:mm" in the listing's zone */
  start: string;
  end: string;
}

export interface ShowingRules {
  slotMinutes: number;
  bufferMinutes: number;
  leadTimeHours: number;
  horizonDays: number;
  autoConfirm: boolean;
  weekly: WeeklyWindow[];
  /** "YYYY-MM-DD" in the listing's zone */
  blackoutDates: string[];
}

export type PropertyType = "House" | "Condo" | "Townhouse";
export type ListingStatus = "Active" | "Draft" | "Off";

export interface ListingAttributes {
  hoaMonthly?: number | null;
  floor?: number | null;
  lotSizeSqft?: number | null;
  [key: string]: unknown;
}

export interface Listing {
  id: string;
  ownerUserId: string;
  ownerName: string;
  status: ListingStatus;
  address: string;
  city: string;
  neighborhood: string | null;
  price: number;
  beds: number;
  baths: number;
  propertyType: PropertyType;
  attributes: ListingAttributes | null;
  description: string | null;
  photos: string[];
  timeZone: string;
  location: { lat: number; lng: number } | null;
  showingRules: ShowingRules | null;
}

export type ListingInput = Omit<Listing, "id" | "ownerUserId" | "ownerName">;

export interface Slot {
  startUtc: string;
  endUtc: string;
  /** local start in the viewer's zone, "YYYY-MM-DDTHH:mm" */
  localStart: string;
  localEnd: string;
  /** local start in the listing's zone */
  listingLocalStart: string;
  /** e.g. "11:00 AM" in the viewer's zone */
  label: string;
  /** e.g. "9:00 AM MST" in the listing's zone */
  listingLabel: string;
}

export interface DayGroup {
  date: string;
  label: string;
  slots: Slot[];
}

export interface SlotsResponse {
  listingId: string;
  timeZone: string;
  days: DayGroup[];
}

export interface LocalRange {
  /** "YYYY-MM-DDTHH:mm" */
  start: string;
  end: string;
}

export interface MatchRequest {
  timeZone: string;
  ranges: LocalRange[];
  city: string | null;
  maxPrice: number | null;
  minBeds: number | null;
  propertyType: PropertyType | null;
  near: string | null;
  nearLat: number | null;
  nearLng: number | null;
  radiusKm: number | null;
}

export interface MatchResult {
  listing: Listing;
  distanceKm: number | null;
  matchCount: number;
  days: DayGroup[];
}

export interface MatchSuggestion {
  listing: Listing;
  slots: Slot[];
}

export interface MatchResponse {
  timeZone: string;
  results: MatchResult[];
  suggestions: MatchSuggestion[] | null;
}

export type ShowingStatus = "Requested" | "Confirmed" | "Declined" | "Cancelled";

export interface Showing {
  id: string;
  listingId: string;
  listingAddress: string;
  listingCity: string;
  listingTimeZone: string;
  buyerId: string;
  buyerName: string;
  agentId: string;
  agentName: string;
  slotStartUtc: string;
  slotEndUtc: string;
  status: ShowingStatus;
  createdAt: string;
  updatedAt: string;
  listingLocalLabel: string;
}

export interface SlotHold {
  id: string;
  listingId: string;
  slotStartUtc: string;
  expiresAt: string;
}

export interface ShowingStreamEvent {
  showingId: string;
  status: ShowingStatus;
  /** "insert" | "update" */
  operation: string;
  listingAddress?: string;
  buyerName?: string;
}

export interface AvailabilityWindow {
  days: DayOfWeek[];
  start: string;
  end: string;
}

export interface ParsedAvailability {
  provider: string;
  windows: AvailabilityWindow[];
  ranges: LocalRange[];
  summary: string;
  warnings: string[];
}

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
  alternatives?: Slot[];
  extensions?: { alternatives?: Slot[]; [key: string]: unknown };
  [key: string]: unknown;
}
